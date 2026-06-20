# Build 12.0 — Attached Active Window Sidecar

Build 12.0 changes the Windows sidecar from a mostly screen-docked companion into an attached active-window sidecar.

## What changed

- The sidecar now defaults to **Attach** mode.
- When Threadline finds a selected, locked, or followed target, it places the sidecar beside that target window.
- Placement prefers the right side of the target window, then the left side, then a safe screen-edge fallback when neither side has enough room.
- The Current Target panel now shows sidecar placement status.
- A new **Attach** checkbox lets the user switch between attached placement and the older screen-docked placement.
- Follow / Lock now maintains the sidecar beside the locked or followed target.
- Ask context resolution also re-attaches the sidecar to the target used for context.
- The selected-target handler name now matches the XAML `Use` button wiring.

## Intended behavior

1. Launch ThreadlineAI Sidecar.
2. Switch to another app or document.
3. Return to Threadline.
4. Follow mode should identify the last active non-Threadline target.
5. The sidecar should move beside that target window while still preserving the existing current-target and context-resolution behavior.
6. Use **Follow / Lock** to pin the sidecar to that target.
7. Uncheck **Attach** to return to screen-docked sidecar placement.

## Fallback behavior

If the target window handle is invalid, the target rectangle cannot be read, or the target does not leave enough side space, Threadline falls back to the screen-right docked placement instead of failing the UI.

## Validation notes

This build touches WinUI window positioning and user32 interop. The final validation gate is a local Windows build/run:

```powershell
dotnet restore ThreadlineAI.sln
dotnet build ThreadlineAI.sln
./eng/build-windows.ps1
./eng/run-windows.ps1
```

Manual checks:

- Verify the app starts without XAML event-handler errors.
- Verify the **Use** button works after selecting an app/tab target.
- Verify the sidecar attaches to the selected/followed target.
- Verify **Follow / Lock** keeps the sidecar beside the locked target.
- Verify unchecking **Attach** returns the sidecar to the screen-right dock.
