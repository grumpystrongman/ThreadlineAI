# Security and privacy model

ThreadlineAI handles sensitive desktop context. Its defaults must be privacy-preserving.

## Principles

1. Capture must be visible.
2. Capture must be pausable.
3. Context must be previewable before model calls.
4. Sensitive apps/domains must be blockable.
5. Secrets must be redacted before storage and provider calls.
6. Screenshots require explicit approval by default.
7. Local-first storage should be the default.
8. Users must be able to delete sessions.

## Capture modes

| Mode | Behavior |
|---|---|
| Paused | Capture nothing. |
| Ask only | User manually adds context. |
| Current window | Active window metadata and approved extracted text. |
| Current session | Current window plus approved recent session events. |
| Full approved context | Browser, terminal, selected text, and approved screenshots. |
