# Local Model Advisor Prototype

This prototype is a first working spine for the "better Athanor Lite" idea: scan the machine, search Hugging Face for local model candidates, explain the tradeoffs, and download a selected model artifact.

It is intentionally CLI-based so the recommendation engine can be tested before it is moved into the Threadline service and WinUI setup flow.

## Run

```powershell
cd prototypes/local-model-advisor
python local_model_advisor.py recommend
```

Optional token for gated Hugging Face repositories:

```powershell
$env:HF_TOKEN = "hf_..."
python local_model_advisor.py recommend --task chat --limit 12
```

Download the top recommendation:

```powershell
python local_model_advisor.py download --repo TheBloke/Mistral-7B-Instruct-v0.2-GGUF --filename mistral-7b-instruct-v0.2.Q4_K_M.gguf
```

## What it does now

- Detects operating system, CPU cores, RAM, available RAM, and disk space.
- Attempts Windows GPU/VRAM detection through PowerShell CIM/WMI.
- Searches Hugging Face model metadata for GGUF/instruct/chat candidates.
- Estimates RAM/VRAM fit from filename quantization and parameter count hints.
- Scores candidates using an explainable scoring model.
- Prints the reasons for and against each model.
- Downloads selected artifacts with resume-friendly streaming.

## What still needs production hardening

- Replace CLI prompts with the WinUI Local Models panel.
- Move hardware scan and recommendation logic into `Threadline.Service` endpoints.
- Use a proper inference runtime manager instead of only downloading artifacts.
- Persist selected model profiles into Threadline provider settings.
- Add progress events, cancellation, checksum validation, and cache management.
