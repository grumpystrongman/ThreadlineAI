# Phase 11.9 Build Notes — Visible Context Status

Build 11.9 makes Threadline's context quality visible in the sidecar instead of hiding it inside diagnostics.

## Delivered

- Added a compact context status pill beside **Current Context**.
- Classified resolved context into user-facing status labels:
  - Browser
  - File-backed
  - Native UI
  - Screenshot required
  - Provider needed
  - No readable context
- Added the same status wording to the Ask timeline so it is clear which context path was used.
- Added context status to the `Threadline Context` transcript message before provider execution.
- Reset the context status, context panel, and diagnostics panel when local context is cleared or a new chat starts.
- Hardened New Chat and Clear Context so they clear the bound transcript collection instead of mutating the UI control items directly.

## Build 11.9.1 correction

A manual test exposed a bad fallback experience: when the running local service did not expose `/sessions/{sessionId}/ask`, Threadline answered with a service-plumbing message even though the resolver had already captured useful local context.

11.9.1 changes that fallback. If `/ask` is missing but prompt composition still works, Threadline now reports what it can actually see locally:

- Target
- Context status
- Source
- Confidence
- App/window information
- Summary
- Key details
- Warnings

This keeps the sidecar useful even when the service binary is stale or the provider execution path is unavailable.

## Build 11.9.3 correction

A second manual test confirmed that `/ask` can be present and still fail with provider configuration status `409 Conflict`, for example when the session provider is `Local` but no `Local` provider connection exists.

11.9.3 treats provider configuration failures like a recoverable Ask execution failure. The sidecar now keeps the pending `Threadline` message and replaces it with the same local visibility report, including the provider failure reason, instead of appending a separate service/action error after the thinking message.

## Local provider setup example

`Local` must be configured as an OpenAI-compatible endpoint before provider-written answers can work. The `baseUrl` should include the OpenAI-compatible API root and should end with `/` because the provider appends `chat/completions`.

For LM Studio:

```powershell
Invoke-RestMethod -Method Post -Uri "http://localhost:5057/providers" -ContentType "application/json" -Body '{
  "providerName": "Local",
  "authType": "LocalEndpoint",
  "baseUrl": "http://localhost:1234/v1/",
  "defaultModel": "local-model",
  "status": "Ready"
}'
```

For Ollama's OpenAI-compatible endpoint:

```powershell
Invoke-RestMethod -Method Post -Uri "http://localhost:5057/providers" -ContentType "application/json" -Body '{
  "providerName": "Local",
  "authType": "LocalEndpoint",
  "baseUrl": "http://localhost:11434/v1/",
  "defaultModel": "llama3.1",
  "status": "Ready"
}'
```

Use whichever model name your local runtime actually exposes.

## Why this matters

The resolver can now distinguish high-confidence provider/file context from lower-confidence native UI or screenshot-required fallback. The sidecar should expose that distinction before the user asks a question. This keeps Threadline honest and prevents a bad product habit: answering with confidence when it barely saw the target.

The Ask fallback must follow the same principle. If provider execution fails because the running service is old or the active provider is not configured, Threadline should still tell the user what local context it resolved instead of hiding the useful answer behind an implementation detail.

## Manual verification

Run from the repository root on Windows:

```powershell
git pull
dotnet restore ThreadlineAI.sln
dotnet build ThreadlineAI.sln
./eng/build-windows.ps1
./eng/run-windows.ps1
```

Then validate:

1. Start or use a Threadline session.
2. Click **Refresh** in Open Apps and Tabs.
3. Select a browser tab target and click **Use**.
4. Confirm the context pill shows a Browser or provider-needed status depending on captured extension context.
5. Select a saved Notepad-backed file and click **Use**.
6. Confirm the context pill shows File-backed when a unique file-backed match is found.
7. Select a generic desktop app and click **Use**.
8. Confirm the context pill shows Native UI, Screenshot required, or Provider needed based on the resolver path.
9. Ask `what can you actually see` while running against a service that lacks `/ask`.
10. Confirm the Threadline answer reports the local visibility fallback instead of only saying `/ask` is missing.
11. Ask `what can you actually see` with the session provider set to `Local` before configuring a `Local` provider.
12. Confirm the Threadline answer reports the local visibility fallback and states the provider is not ready.
13. Configure the `Local` provider and start a new session with provider `Local`.
14. Ask a normal question and confirm the transcript includes a provider-written answer plus a `Threadline Context` message with Status, Source, and Confidence.
15. Click **Clear Context** and confirm the pill resets to No context.
16. Click **New** and confirm the transcript clears cleanly without UI binding errors.

## Known limitations

- The pill is text-only in this build. Color/state styling can come later once the sidecar theme is stable.
- Screenshot/OCR remains a placeholder route. Build 11.9 makes the need visible but does not implement image reading.
- Browser and Office-class apps still need deeper app-specific provider work for confident body capture.
- If `/ask` is missing, Threadline can only report local resolved context; it still cannot generate a provider-written answer until the local service is rebuilt/restarted with the current Ask endpoint.
- If `/ask` exists but the active provider is not configured, Threadline can report local resolved context but cannot generate a provider-written answer until the provider record is saved as `Ready`.
