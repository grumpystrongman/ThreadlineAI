# Screenshots and Product Visuals

This folder documents the public screenshot set for ThreadlineAI.

The current repository includes screenshot-style SVG visuals. These are designed to make the README and documentation useful before final release screenshots are captured from a signed Windows build.

## Current visuals

### Sidecar overview

![ThreadlineAI sidecar overview](assets/screenshots/threadline-sidecar-overview.svg)

Shows ThreadlineAI attached beside a work application, with current context, Ask, and artifact actions visible.

### Doctor/readiness overview

![ThreadlineAI Doctor readiness](assets/screenshots/threadline-doctor-readiness.svg)

Shows the commercial-readiness concept: local service status, Doctor checks, provider readiness, diagnostics, and release validation gates.

## Real screenshots to capture before public release

Replace or supplement the SVG visuals with real screenshots from a clean release build.

Recommended files:

| File | Capture |
| --- | --- |
| `threadline-sidecar-attached.png` | Sidecar attached beside a normal work window. |
| `threadline-current-context.png` | Current Context panel showing source, confidence, and summary. |
| `threadline-browser-extension.png` | Chrome/Edge extension sending page or selected text context. |
| `threadline-provider-settings.png` | Provider Settings flyout with safe placeholder values. |
| `threadline-doctor.png` | Doctor/readiness panel showing service, provider, extension, session, and context checks. |
| `threadline-artifacts.png` | Summary, Handoff, Decisions, Risks, and Next Actions artifact buttons/output. |
| `threadline-first-run.png` | First-run setup wizard explaining local service, browser extension, token, privacy, and diagnostics. |
| `threadline-diagnostics-export.png` | Diagnostics export confirmation with secrets redacted. |

## Capture standards

Use screenshots that make the product feel trustworthy and real.

- Capture from a release or release-candidate build, not a dirty developer build.
- Use realistic but non-sensitive sample work.
- Avoid real credentials, patient data, internal documents, private URLs, or customer names.
- Use a normal Windows 11 theme and legible scaling.
- Crop only enough to remove unrelated desktop clutter.
- Prefer 16:9 or wide layouts for README use.
- Include the sidecar beside a recognizable work surface so the "attached AI companion" idea is immediately clear.
- Show context confidence and receipts when possible.
- Show privacy/trust controls where they matter.

## Suggested README screenshot order

1. Sidecar attached to current work.
2. Current Context and confidence.
3. Ask with context receipt.
4. Artifact actions.
5. Doctor/readiness.
6. First-run setup or diagnostics export.

## What not to show

Do not show:

- raw API keys;
- local API token values;
- real customer data;
- protected health information;
- internal production system names;
- private browser tabs;
- broken provider calls unless documenting troubleshooting;
- test windows that make the product look like a toy.

## Screenshot naming convention

Use lowercase kebab-case names:

```text
docs/assets/screenshots/threadline-sidecar-attached.png
docs/assets/screenshots/threadline-current-context.png
docs/assets/screenshots/threadline-doctor.png
```

Keep generated or conceptual SVGs in the same folder, but real screenshots should use `.png` unless there is a strong reason to use another format.
