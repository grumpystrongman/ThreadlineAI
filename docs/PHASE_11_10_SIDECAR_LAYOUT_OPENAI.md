# Phase 11.10 Build Notes — Sidecar Layout and OpenAI Default

Build 11.10 responds to sidecar usability testing from the live Windows app.

## Delivered

- Moved **Current Context** out of the left target column.
- Placed **Current Context** above the conversation area so context status stays visible without consuming Open Apps and Tabs space.
- Expanded the left column's Open Apps and Tabs area by removing the context summary block from that column.
- Kept diagnostics available from the same Diagnostics button, now associated with the top context card.
- Made the context summary compact: status pill, source/confidence, and a short summary.
- Changed the provider dropdown default from `Local` to `OpenAI`.
- Changed the internal fallback default provider from `Local` to `OpenAI`.
- Hardened Ask failure handling so provider call failures, including provider endpoint errors, update the pending Threadline message with the local visibility fallback instead of appending a raw service/action error.

## OpenAI provider setup

Threadline's provider adapter is OpenAI-compatible and calls the Chat Completions path by appending `chat/completions` to the configured base URL. For OpenAI, configure the provider base URL as:

```text
https://api.openai.com/v1/
```

Use the provider credential endpoint with a valid OpenAI API credential and a model available to the active OpenAI project. This keeps the provider row pointed at a stored secret reference instead of placing the credential directly in the connection record.

## Manual verification

1. Pull and rebuild the repo.
2. Start `Threadline.Service`.
3. Configure the `OpenAI` provider.
4. Start the Windows sidecar.
5. Confirm the provider dropdown starts on `OpenAI`, not `Local`.
6. Confirm the Current Context block appears above the Conversation header.
7. Confirm the left Open Apps and Tabs list has the freed vertical space.
8. Select a browser tab captured through the extension.
9. Ask: `what can you actually see`.
10. Confirm one of these outcomes:
    - provider answer returns successfully from OpenAI; or
    - if OpenAI/provider fails, the pending Threadline message is replaced with local visibility fallback instead of a raw service/action error.

## Known limitations

- OpenAI still requires a valid API credential and a model available to the active OpenAI project.
- The sidecar currently uses the existing OpenAI-compatible Chat Completions adapter. A later build can add a first-class Responses API adapter.
- The context card is compact text-only. Visual styling can be improved after the sidecar layout settles.
