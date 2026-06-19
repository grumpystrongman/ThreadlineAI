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

## Why this matters

The resolver can now distinguish high-confidence provider/file context from lower-confidence native UI or screenshot-required fallback. The sidecar should expose that distinction before the user asks a question. This keeps Threadline honest and prevents a bad product habit: answering with confidence when it barely saw the target.

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
9. Ask a question and confirm the transcript includes a `Threadline Context` message with Status, Source, and Confidence.
10. Click **Clear Context** and confirm the pill resets to No context.
11. Click **New** and confirm the transcript clears cleanly without UI binding errors.

## Known limitations

- The pill is text-only in this build. Color/state styling can come later once the sidecar theme is stable.
- Screenshot/OCR remains a placeholder route. Build 11.9 makes the need visible but does not implement image reading.
- Browser and Office-class apps still need deeper app-specific provider work for confident body capture.
