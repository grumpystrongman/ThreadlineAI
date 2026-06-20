# Phase 11.12.1 Build Notes — Sidecar Post-Ask Stability

Build 11.12.1 is a stability patch after the first successful OpenAI Responses API smoke test. The provider call succeeded and returned a grounded answer, but the Windows sidecar could still crash after success because several visual update paths were allowed to throw outside the Ask provider call itself.

## Delivered

- Hardened sidecar UI event handlers by routing direct button actions through the same guarded `RunUiActionAsync` path.
- Guarded startup health check through `RunUiActionAsync` instead of launching an unguarded task.
- Made foreground-window refresh null-safe when Windows does not return a foreground app snapshot.
- Wrapped timeline updates so timeline rendering cannot crash the app.
- Wrapped transcript append/update rendering so transcript binding or collection updates cannot crash the app.
- Wrapped delayed dispatcher transcript scrolling so post-render `UpdateLayout`, `ScrollableHeight`, or `ChangeView` failures cannot crash after a successful Ask.
- Clarified readable browser targets from `[Browser provider; needs provider]` to `[Browser extension; ready]`.

## Why this build matters

The 11.12 smoke test proved the important product path: browser context resolved, OpenAI answered, and the response was grounded in the Notion page. A crash after that kind of success is usually not a provider failure; it is often a UI/rendering edge after the response is inserted into the transcript. This patch keeps non-critical visual updates from taking down the sidecar.

## Validation

Run from the repository root on Windows:

```powershell
git pull
dotnet restore ThreadlineAI.sln
dotnet build ThreadlineAI.sln
./eng/build-windows.ps1
./eng/run-windows.ps1
```

Manual checks:

1. Start `Threadline.Service`.
2. Open the Windows sidecar.
3. Save or confirm OpenAI provider settings.
4. Start a new OpenAI session.
5. Open a Notion/browser page with browser extension context available.
6. Ask: `what can you actually see`.
7. Confirm the answer returns and the sidecar remains open for at least 60 seconds after the answer.
8. Click Copy Last Answer, Jump Bottom, Jump Top, Refresh, and Ask again to verify visual update actions no longer destabilize the app.
9. Confirm browser target badges say `Browser extension; ready` instead of `Browser provider; needs provider`.

## Known limitations

- This patch does not replace the transcript ListView with a more robust virtualized chat renderer.
- This patch does not add crash logging or dump capture yet.
- If the app still exits, the next build should add first-chance/unhandled exception logging to a local `%LOCALAPPDATA%` Threadline crash log before moving to larger UX features.
