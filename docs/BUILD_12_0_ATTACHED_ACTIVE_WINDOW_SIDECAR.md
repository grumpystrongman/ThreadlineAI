# Build 12.0 Notes — Attached Active Window Sidecar

Build 12.0 changes the Windows companion from a mostly screen-docked panel into an attached sidecar that follows the user's active work target.

## Delivered

- Added attached sidecar mode, enabled by default.
- The sidecar now positions itself beside the selected, locked, or last active non-Threadline target window.
- Placement prefers the right side of the target, falls back to the left side, then uses a screen-right overlay when the target fills the monitor.
- The sidecar resizes to a more usable width for the current two-column target/conversation layout.
- Added a visible sidecar attachment status line near the top of the window.
- Added a **Screen Dock / Attach Sidecar** toggle so the user can park the sidecar at the screen edge when they do not want it moving with the target.
- Follow/Lock now also controls sidecar placement:
  - unlocked follow mode attaches beside the last active non-Threadline app;
  - locked mode keeps the sidecar beside the locked target;
  - selected target mode moves the sidecar beside the chosen app/tab immediately.
- Ask context resolution re-attaches to the context target before resolving content, so the sidecar and prompt context stay aligned.

## User flow

1. Start `Threadline.Service`.
2. Start the Windows sidecar.
3. Switch to a target app, document, browser, or tab.
4. Return to Threadline.
5. Confirm the target card shows the last active non-Threadline app.
6. Confirm the sidecar moves beside that target window.
7. Click **Follow / Lock** to lock the target.
8. Move focus to another app.
9. Confirm the sidecar stays beside the locked target until unlocked.
10. Click **Screen Dock** to park the sidecar at the right edge of the screen.
11. Click **Attach Sidecar** to resume attached behavior.

## Placement rules

Threadline reads the target window bounds through Win32 window geometry and uses the target's nearest display work area. It then places the sidecar in this order:

1. Right of the target when there is enough room.
2. Left of the target when the right side is crowded.
3. Screen-right overlay when the target occupies most of the monitor.

If target bounds cannot be read, Threadline falls back to the normal screen dock position instead of failing the app.

## Known limitations

- Build 12.0 does not implement always-on-top behavior.
- Build 12.0 does not snap or resize the target app itself.
- Full-screen target apps may cause overlay placement because there is no adjacent work-area space.
- Multi-monitor behavior uses the target window's nearest display work area; unusual virtual desktop/window manager setups may need later tuning.
