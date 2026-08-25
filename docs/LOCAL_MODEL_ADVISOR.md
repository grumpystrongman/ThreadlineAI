# Threadline Local Model Advisor

## Goal

Build the better version of the Athanor Lite idea inside ThreadlineAI: a local-first model advisor that scans the machine, pulls candidate local models from Hugging Face, explains its reasoning, downloads the right artifact, and wires the chosen model into the local provider path.

The product promise is simple: the user should not need to know what GGUF, quantization, context length, VRAM, CUDA, ROCm, Metal, or llama.cpp means before they can start using local AI safely.

## User experience

1. The user opens **Local Models** from provider setup.
2. Threadline scans hardware and runtime capability:
   - operating system and architecture;
   - total RAM and available RAM;
   - GPU vendor, name, driver/runtime hints;
   - VRAM when available;
   - CPU core count and SIMD hints when available;
   - available disk space for model cache.
3. Threadline fetches Hugging Face model candidates using a curated query profile, favoring GGUF/chat/instruct models suitable for local inference.
4. The user gets an interactive comparison, not just a download list.
5. Each model card explains:
   - why it fits or does not fit the machine;
   - expected speed tier;
   - memory headroom;
   - likely quality tier;
   - privacy/runtime caveats;
   - quantization tradeoffs;
   - whether GPU offload is likely or CPU-only is safer.
6. The user can choose **Recommended**, **Fastest**, **Best quality that fits**, or **Show me why**.
7. Threadline downloads the selected artifact into a local model cache and records a provider profile.

## Recommendation philosophy

The advisor should not pretend there is one universal best model. It should expose the tradeoff clearly:

- **Small quantized models** are faster and safer on constrained hardware, but may lose reasoning quality.
- **Larger models** can produce better answers but may stall, swap, or fail when VRAM/RAM headroom is too tight.
- **Q4 quantization** is usually the practical default for consumer machines.
- **Q5/Q6/Q8** can improve fidelity but increase memory pressure.
- **GPU offload** is valuable only when there is enough VRAM headroom after the operating system and desktop apps take their share.
- **CPU-only** may be acceptable for lightweight models, but the UI should set expectations honestly.

## Scoring model

The first implementation uses an explainable weighted score. It is intentionally simple enough to debug and revise:

- 35% fit score: model estimated memory footprint versus available RAM/VRAM.
- 20% quality proxy: parameter count and model family tier.
- 15% speed proxy: smaller quantized models and expected GPU offload get favored.
- 15% safety margin: rewards models with enough headroom to avoid swapping or thermal misery.
- 10% popularity/maintenance: downloads, likes, and recent metadata when available.
- 5% usability: chat/instruct tags, GGUF availability, and license visibility.

The UI should always show the ingredients of the score. Users trust a recommendation more when the system admits why it chose one model over another.

## Hugging Face integration

The advisor should use the Hugging Face model search API and prefer repos that expose GGUF artifacts. The downloader should support:

- unauthenticated public model search;
- optional Hugging Face token for gated models;
- artifact filtering by quantization suffix;
- resume-safe downloads;
- cache location selection;
- checksum/size verification when metadata is available.

## Runtime integration

This feature should sit beside the existing provider abstraction rather than becoming a separate product. The selected model should become a local provider profile that the existing `/sessions/{sessionId}/ask` path can call.

The recommended production path is:

- local model advisor selects and downloads a GGUF;
- local runtime manager starts or connects to the inference backend;
- Threadline provider abstraction calls the local runtime through the same Ask contract used by other providers;
- provider testing reports model name, runtime, load status, token/sec, and memory use.

## Prototype included

A CLI prototype lives under `prototypes/local-model-advisor/`. It demonstrates the recommendation logic, Hugging Face candidate discovery, model explanation output, and download handoff. It is not a final UI, but it gives the repo a working spine for the feature.

## Next build steps

1. Move hardware scanning into `Threadline.Service` behind `GET /local-models/hardware`.
2. Add `GET /local-models/candidates` with filters for task, size, quantization, and license.
3. Add `POST /local-models/recommend` that returns scored models with explanation text.
4. Add `POST /local-models/download` with progress events.
5. Add a WinUI Local Models panel with comparison cards and a plain-English recommendation explainer.
6. Wire selected models into the local provider configuration and provider test endpoint.
