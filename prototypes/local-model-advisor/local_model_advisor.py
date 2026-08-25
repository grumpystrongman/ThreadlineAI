#!/usr/bin/env python3
"""
Threadline Local Model Advisor prototype.

Scans local hardware, searches Hugging Face for local GGUF model candidates,
explains model-fit tradeoffs, and downloads selected artifacts.

This is intentionally dependency-light so the recommendation logic can be moved
into Threadline.Service later without being tied to a specific UI framework.
"""
from __future__ import annotations

import argparse
import json
import math
import os
import platform
import re
import shutil
import subprocess
import sys
import textwrap
import time
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Iterable

HF_API = "https://huggingface.co/api"
DEFAULT_CACHE = Path(os.environ.get("THREADLINE_MODEL_CACHE", Path.home() / ".threadline" / "models"))

QUANT_MEMORY_MULTIPLIER = {
    "Q2": 0.32,
    "Q3": 0.42,
    "Q4": 0.55,
    "Q5": 0.68,
    "Q6": 0.80,
    "Q8": 1.05,
    "F16": 2.05,
}

QUALITY_BY_FAMILY = {
    "llama": 0.90,
    "mistral": 0.88,
    "mixtral": 0.92,
    "qwen": 0.88,
    "phi": 0.78,
    "gemma": 0.82,
    "deepseek": 0.90,
    "openhermes": 0.82,
}


@dataclass
class HardwareProfile:
    os_name: str
    machine: str
    cpu_cores: int
    total_ram_gb: float
    available_ram_gb: float
    disk_free_gb: float
    gpu_name: str | None = None
    vram_gb: float | None = None
    notes: list[str] = field(default_factory=list)


@dataclass
class ModelArtifact:
    repo_id: str
    filename: str
    size_gb: float | None
    quant: str | None
    params_b: float | None
    downloads: int
    likes: int
    tags: list[str]
    license: str | None
    last_modified: str | None


@dataclass
class Recommendation:
    artifact: ModelArtifact
    score: float
    fit_score: float
    quality_score: float
    speed_score: float
    margin_score: float
    popularity_score: float
    usability_score: float
    decision: str
    reasons: list[str]
    cautions: list[str]


def bytes_to_gb(value: float | int | None) -> float | None:
    if value is None:
        return None
    return round(float(value) / (1024 ** 3), 2)


def run_powershell_json(script: str) -> Any | None:
    if platform.system().lower() != "windows":
        return None
    try:
        completed = subprocess.run(
            ["powershell", "-NoProfile", "-Command", script],
            check=False,
            capture_output=True,
            text=True,
            timeout=8,
        )
        if completed.returncode != 0 or not completed.stdout.strip():
            return None
        return json.loads(completed.stdout)
    except (OSError, subprocess.SubprocessError, json.JSONDecodeError):
        return None


def detect_windows_memory() -> tuple[float | None, float | None]:
    data = run_powershell_json(
        "Get-CimInstance Win32_OperatingSystem | "
        "Select-Object TotalVisibleMemorySize,FreePhysicalMemory | ConvertTo-Json"
    )
    if not data:
        return None, None
    total_gb = float(data.get("TotalVisibleMemorySize", 0)) / 1024 / 1024
    free_gb = float(data.get("FreePhysicalMemory", 0)) / 1024 / 1024
    return round(total_gb, 2), round(free_gb, 2)


def detect_windows_gpu() -> tuple[str | None, float | None, list[str]]:
    notes: list[str] = []
    data = run_powershell_json(
        "Get-CimInstance Win32_VideoController | "
        "Select-Object Name,AdapterRAM | ConvertTo-Json"
    )
    if not data:
        return None, None, notes
    devices = data if isinstance(data, list) else [data]
    best_name = None
    best_vram = 0.0
    for device in devices:
        name = device.get("Name")
        adapter_ram = device.get("AdapterRAM")
        if name and "basic display" not in name.lower():
            gb = bytes_to_gb(adapter_ram) or 0.0
            if gb >= best_vram:
                best_name = name
                best_vram = gb
    if best_name and best_vram <= 0:
        notes.append("GPU was detected, but VRAM was not reported by Windows WMI.")
    return best_name, (best_vram or None), notes


def detect_hardware() -> HardwareProfile:
    total_ram = None
    available_ram = None
    notes: list[str] = []

    if platform.system().lower() == "windows":
        total_ram, available_ram = detect_windows_memory()

    if total_ram is None or available_ram is None:
        if hasattr(os, "sysconf"):
            try:
                page_size = os.sysconf("SC_PAGE_SIZE")
                phys_pages = os.sysconf("SC_PHYS_PAGES")
                avail_pages = os.sysconf("SC_AVPHYS_PAGES")
                total_ram = bytes_to_gb(page_size * phys_pages)
                available_ram = bytes_to_gb(page_size * avail_pages)
            except (OSError, ValueError, AttributeError):
                pass

    if total_ram is None:
        total_ram = 8.0
        notes.append("Total RAM could not be detected; using a conservative 8 GB assumption.")
    if available_ram is None:
        available_ram = max(2.0, total_ram * 0.5)
        notes.append("Available RAM could not be detected; assuming half of total RAM is free.")

    gpu_name, vram_gb, gpu_notes = detect_windows_gpu()
    notes.extend(gpu_notes)

    disk = shutil.disk_usage(Path.home())
    return HardwareProfile(
        os_name=platform.system(),
        machine=platform.machine(),
        cpu_cores=os.cpu_count() or 4,
        total_ram_gb=round(total_ram, 2),
        available_ram_gb=round(available_ram, 2),
        disk_free_gb=bytes_to_gb(disk.free) or 0,
        gpu_name=gpu_name,
        vram_gb=vram_gb,
        notes=notes,
    )


def hf_get(path: str, params: dict[str, Any] | None = None) -> Any:
    query = urllib.parse.urlencode({k: v for k, v in (params or {}).items() if v is not None})
    url = f"{HF_API}{path}" + (f"?{query}" if query else "")
    headers = {"User-Agent": "ThreadlineAI-local-model-advisor/0.1"}
    token = os.environ.get("HF_TOKEN") or os.environ.get("HUGGINGFACE_TOKEN")
    if token:
        headers["Authorization"] = f"Bearer {token}"
    request = urllib.request.Request(url, headers=headers)
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.loads(response.read().decode("utf-8"))


def extract_quant(filename: str) -> str | None:
    match = re.search(r"\b(Q[2-8](?:_[A-Z0-9]+)?|F16)\b", filename.upper())
    if not match:
        return None
    token = match.group(1)
    return token.split("_")[0] if token.startswith("Q") else token


def extract_params(text: str) -> float | None:
    match = re.search(r"(?:^|[-_/.])([0-9]+(?:\.[0-9]+)?)\s*[bB](?:[-_/.]|$)", text)
    if match:
        return float(match.group(1))
    # Common compact spellings like 7B, 13B in repo names without separators.
    match = re.search(r"([0-9]+(?:\.[0-9]+)?)\s*[bB]\b", text)
    return float(match.group(1)) if match else None


def estimate_model_memory_gb(params_b: float | None, quant: str | None, file_size_gb: float | None) -> float:
    if file_size_gb:
        # Add runtime/KV/cache overhead so fit is not overly optimistic.
        return round(file_size_gb * 1.25 + 0.75, 2)
    if not params_b:
        return 6.0
    multiplier = QUANT_MEMORY_MULTIPLIER.get(quant or "Q4", 0.55)
    return round(params_b * multiplier + 1.25, 2)


def infer_family_score(repo_id: str, filename: str) -> float:
    haystack = f"{repo_id} {filename}".lower()
    for family, score in QUALITY_BY_FAMILY.items():
        if family in haystack:
            return score
    return 0.72


def fetch_candidates(task: str, limit: int) -> list[ModelArtifact]:
    search_text = "GGUF instruct" if task in {"chat", "instruct"} else "GGUF"
    models = hf_get(
        "/models",
        {
            "search": search_text,
            "sort": "downloads",
            "direction": -1,
            "limit": max(limit * 2, 20),
            "full": "true",
        },
    )

    artifacts: list[ModelArtifact] = []
    for model in models:
        repo_id = model.get("modelId") or model.get("id")
        if not repo_id:
            continue
        tags = model.get("tags") or []
        siblings = model.get("siblings") or []
        for sibling in siblings:
            filename = sibling.get("rfilename") or sibling.get("filename")
            if not filename or not filename.lower().endswith(".gguf"):
                continue
            if any(skip in filename.lower() for skip in ["mmproj", "vision", "tokenizer"]):
                continue
            quant = extract_quant(filename)
            params = extract_params(f"{repo_id}/{filename}")
            size_gb = bytes_to_gb(sibling.get("size"))
            artifacts.append(
                ModelArtifact(
                    repo_id=repo_id,
                    filename=filename,
                    size_gb=size_gb,
                    quant=quant,
                    params_b=params,
                    downloads=int(model.get("downloads") or 0),
                    likes=int(model.get("likes") or 0),
                    tags=list(tags),
                    license=next((tag.split(":", 1)[1] for tag in tags if str(tag).startswith("license:")), None),
                    last_modified=model.get("lastModified"),
                )
            )
    return artifacts[: max(limit * 3, limit)]


def score_artifact(artifact: ModelArtifact, hardware: HardwareProfile) -> Recommendation:
    estimated_memory = estimate_model_memory_gb(artifact.params_b, artifact.quant, artifact.size_gb)
    effective_ram = min(hardware.available_ram_gb, max(1.0, hardware.total_ram_gb * 0.72))
    vram = hardware.vram_gb or 0.0
    gpu_candidate = vram >= estimated_memory * 0.72

    if estimated_memory <= effective_ram * 0.75:
        fit_score = 1.0
    elif estimated_memory <= effective_ram:
        fit_score = 0.72
    elif estimated_memory <= hardware.total_ram_gb * 0.85:
        fit_score = 0.38
    else:
        fit_score = 0.08

    quality_base = infer_family_score(artifact.repo_id, artifact.filename)
    param_bonus = min((artifact.params_b or 3.0) / 14.0, 1.0) * 0.18
    quality_score = min(1.0, quality_base * 0.82 + param_bonus)

    quant = artifact.quant or "Q4"
    quant_speed = {"Q2": 0.95, "Q3": 0.90, "Q4": 0.84, "Q5": 0.72, "Q6": 0.62, "Q8": 0.46, "F16": 0.25}.get(quant, 0.70)
    size_penalty = min((artifact.params_b or 7.0) / 20.0, 1.0) * 0.35
    speed_score = max(0.05, min(1.0, quant_speed - size_penalty + (0.20 if gpu_candidate else 0.0)))

    margin = (effective_ram - estimated_memory) / max(effective_ram, 1.0)
    margin_score = max(0.0, min(1.0, margin * 1.5))

    popularity_score = min(1.0, math.log10(max(artifact.downloads, 1)) / 6.0 + min(artifact.likes, 500) / 3000.0)
    usability_score = 0.0
    name_blob = f"{artifact.repo_id} {artifact.filename} {' '.join(artifact.tags)}".lower()
    if "instruct" in name_blob or "chat" in name_blob:
        usability_score += 0.45
    if artifact.filename.lower().endswith(".gguf"):
        usability_score += 0.35
    if artifact.license:
        usability_score += 0.20

    score = (
        fit_score * 0.35
        + quality_score * 0.20
        + speed_score * 0.15
        + margin_score * 0.15
        + popularity_score * 0.10
        + usability_score * 0.05
    )

    reasons: list[str] = []
    cautions: list[str] = []
    reasons.append(f"Estimated loaded footprint is about {estimated_memory:.1f} GB.")
    if gpu_candidate:
        reasons.append(f"Detected VRAM ({vram:.1f} GB) appears sufficient for meaningful GPU offload.")
    elif hardware.gpu_name:
        cautions.append("GPU was detected, but VRAM headroom may be tight; expect partial offload or CPU-heavy inference.")
    else:
        cautions.append("No usable GPU/VRAM was detected; recommendation assumes CPU-friendly local inference.")

    if artifact.quant:
        reasons.append(f"{artifact.quant} quantization balances footprint and quality for local use.")
    else:
        cautions.append("Could not infer quantization from the filename; memory estimate is less reliable.")

    if (artifact.params_b or 0) >= 13:
        cautions.append("Larger model class: better potential quality, but slower startup and higher memory pressure.")
    elif (artifact.params_b or 0) <= 4:
        reasons.append("Small model class: good speed and safety margin on modest hardware.")

    if fit_score < 0.4:
        decision = "Avoid unless you are willing to tune runtime settings."
    elif speed_score > quality_score and margin_score > 0.55:
        decision = "Best for fast, low-friction local chat."
    elif quality_score > 0.82 and fit_score > 0.65:
        decision = "Best quality option that still appears to fit."
    else:
        decision = "Balanced local recommendation."

    return Recommendation(
        artifact=artifact,
        score=round(score, 4),
        fit_score=round(fit_score, 3),
        quality_score=round(quality_score, 3),
        speed_score=round(speed_score, 3),
        margin_score=round(margin_score, 3),
        popularity_score=round(popularity_score, 3),
        usability_score=round(usability_score, 3),
        decision=decision,
        reasons=reasons,
        cautions=cautions,
    )


def print_hardware(profile: HardwareProfile) -> None:
    print("\nHardware scan")
    print("-------------")
    print(f"OS:        {profile.os_name} {profile.machine}")
    print(f"CPU cores: {profile.cpu_cores}")
    print(f"RAM:       {profile.available_ram_gb:.1f} GB available / {profile.total_ram_gb:.1f} GB total")
    print(f"Disk:      {profile.disk_free_gb:.1f} GB free in home volume")
    print(f"GPU:       {profile.gpu_name or 'not detected'}")
    print(f"VRAM:      {profile.vram_gb if profile.vram_gb is not None else 'unknown'} GB")
    for note in profile.notes:
        print(f"Note:      {note}")


def print_recommendations(recommendations: list[Recommendation]) -> None:
    print("\nRecommended local models")
    print("------------------------")
    for idx, rec in enumerate(recommendations, start=1):
        artifact = rec.artifact
        print(f"\n[{idx}] {artifact.repo_id}")
        print(f"    file:    {artifact.filename}")
        print(f"    score:   {rec.score:.3f} | fit {rec.fit_score:.2f}, quality {rec.quality_score:.2f}, speed {rec.speed_score:.2f}, margin {rec.margin_score:.2f}")
        print(f"    size:    {artifact.size_gb or 'unknown'} GB | params: {artifact.params_b or 'unknown'}B | quant: {artifact.quant or 'unknown'}")
        print(f"    why:     {rec.decision}")
        for reason in rec.reasons[:3]:
            print(f"             + {reason}")
        for caution in rec.cautions[:2]:
            print(f"             ! {caution}")


def command_recommend(args: argparse.Namespace) -> int:
    hardware = detect_hardware()
    print_hardware(hardware)
    print("\nSearching Hugging Face for GGUF candidates...")
    try:
        candidates = fetch_candidates(args.task, args.limit)
    except urllib.error.HTTPError as exc:
        print(f"Hugging Face request failed: HTTP {exc.code}", file=sys.stderr)
        return 2
    except urllib.error.URLError as exc:
        print(f"Hugging Face request failed: {exc}", file=sys.stderr)
        return 2

    if not candidates:
        print("No GGUF artifacts were found. Try again with a broader task or check network access.")
        return 1

    scored = sorted((score_artifact(candidate, hardware) for candidate in candidates), key=lambda item: item.score, reverse=True)
    top = scored[: args.limit]
    print_recommendations(top)

    print("\nPlain-English choice")
    print("--------------------")
    best = top[0]
    fastest = max(top, key=lambda item: item.speed_score)
    quality = max((item for item in top if item.fit_score >= 0.65), key=lambda item: item.quality_score, default=best)
    print(f"Recommended: {best.artifact.repo_id} / {best.artifact.filename}")
    print(f"Fastest:     {fastest.artifact.repo_id} / {fastest.artifact.filename}")
    print(f"Quality:     {quality.artifact.repo_id} / {quality.artifact.filename}")
    print("\nDownload command:")
    print(f"python local_model_advisor.py download --repo {best.artifact.repo_id} --filename {best.artifact.filename}")
    return 0


def download_file(repo: str, filename: str, destination: Path) -> Path:
    destination.mkdir(parents=True, exist_ok=True)
    encoded_repo = urllib.parse.quote(repo, safe="")
    encoded_file = urllib.parse.quote(filename)
    url = f"https://huggingface.co/{repo}/resolve/main/{encoded_file}"
    target = destination / repo.replace("/", "__") / filename
    target.parent.mkdir(parents=True, exist_ok=True)
    tmp = target.with_suffix(target.suffix + ".part")
    headers = {"User-Agent": "ThreadlineAI-local-model-advisor/0.1"}
    token = os.environ.get("HF_TOKEN") or os.environ.get("HUGGINGFACE_TOKEN")
    if token:
        headers["Authorization"] = f"Bearer {token}"

    request = urllib.request.Request(url, headers=headers)
    with urllib.request.urlopen(request, timeout=60) as response, tmp.open("wb") as handle:
        total = int(response.headers.get("Content-Length") or 0)
        downloaded = 0
        started = time.time()
        while True:
            chunk = response.read(1024 * 1024)
            if not chunk:
                break
            handle.write(chunk)
            downloaded += len(chunk)
            if total:
                pct = downloaded / total * 100
                speed = downloaded / max(time.time() - started, 0.1) / (1024 ** 2)
                print(f"\rDownloading {pct:5.1f}% at {speed:5.1f} MB/s", end="")
    tmp.replace(target)
    print(f"\nSaved to {target}")
    return target


def command_download(args: argparse.Namespace) -> int:
    try:
        download_file(args.repo, args.filename, Path(args.cache_dir).expanduser())
        return 0
    except urllib.error.HTTPError as exc:
        print(f"Download failed: HTTP {exc.code}. If this is a gated model, set HF_TOKEN.", file=sys.stderr)
        return 2
    except urllib.error.URLError as exc:
        print(f"Download failed: {exc}", file=sys.stderr)
        return 2


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Threadline local model advisor prototype")
    sub = parser.add_subparsers(dest="command", required=True)

    recommend = sub.add_parser("recommend", help="Scan hardware and recommend Hugging Face GGUF models")
    recommend.add_argument("--task", default="chat", choices=["chat", "instruct", "general"], help="Model intent")
    recommend.add_argument("--limit", type=int, default=8, help="Number of recommendations to display")
    recommend.set_defaults(func=command_recommend)

    download = sub.add_parser("download", help="Download a selected model artifact")
    download.add_argument("--repo", required=True, help="Hugging Face repository id, e.g. TheBloke/Mistral-7B-Instruct-v0.2-GGUF")
    download.add_argument("--filename", required=True, help="GGUF filename inside the repository")
    download.add_argument("--cache-dir", default=str(DEFAULT_CACHE), help="Local model cache directory")
    download.set_defaults(func=command_download)
    return parser


def main(argv: Iterable[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
