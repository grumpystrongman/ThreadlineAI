# Adaptive Model Loop

A provider-neutral framework for testing prompts across leading AI models, capturing feedback, scoring results, and proposing safer prompt improvements.

## What this proves

- Switch between providers with one click.
- Run the same task against OpenAI, Anthropic, Google, or a deterministic mock provider.
- Capture prompt version, model, latency, cost metadata, output, feedback, and evaluation scores.
- Compare prompt variants against a repeatable test suite.
- Propose an improved prompt only when it beats the active prompt on the configured evaluation set.
- Preserve every run as an auditable trace.

## Quick start

```bash
cd experiments/adaptive-model-loop
python -m venv .venv
source .venv/bin/activate   # Windows: .venv\Scripts\activate
pip install -e .[dev]
uvicorn app.main:app --reload
```

Open http://127.0.0.1:8000.

The mock provider works immediately. To enable hosted providers, copy `.env.example` to `.env` and add the relevant API keys.

## API

- `GET /api/providers` — provider availability.
- `POST /api/run` — execute a prompt with one provider.
- `POST /api/compare` — execute the same prompt across selected providers.
- `POST /api/feedback` — attach user feedback to a run.
- `POST /api/improve` — generate and test a candidate prompt revision.
- `GET /api/runs` — inspect the black-box trace log.

## Improvement guardrails

The loop does not silently rewrite production prompts. A candidate prompt must:

1. Run against the evaluation cases.
2. Beat the active prompt's aggregate score by the configured margin.
3. Avoid regressions on required checks.
4. Be explicitly promoted by a human.

This keeps the framework useful for continuous improvement without allowing uncontrolled self-modification.

## Architecture

```text
UI / API
  -> Provider Registry
  -> Model Adapter
  -> Trace Store
  -> Evaluators
  -> Prompt Optimizer
  -> Candidate Validation
  -> Human Promotion
```

## Next steps

- Add structured tool-call evaluation.
- Add retrieval/context quality scoring.
- Add token and provider cost normalization.
- Add prompt experiment dashboards.
- Add organization-specific policy and safety evaluators.
- Connect Threadline conversations as real evaluation cases.
