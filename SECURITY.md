# Security policy

ThreadlineAI captures local desktop context. Security reports are high priority.

## Supported versions

The project is pre-release. Only the current `main` branch is actively maintained until versioned releases begin.

## Reporting a vulnerability

Do not open public issues for vulnerabilities involving credential exposure, local API bypass, hidden capture, prompt injection leakage, or provider key handling.

Use GitHub private vulnerability reporting if enabled for this repository. If private reporting is not enabled yet, contact the repository owner directly and avoid posting exploit details publicly.

## Security principles

- Capture must be visible.
- Capture must be pausable.
- Context sent to providers must be previewable.
- Secrets must be redacted before storage and provider calls.
- Provider credentials must never be stored in plaintext.
- Local APIs must reject untrusted callers before production release.
