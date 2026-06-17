# Phase 7 — Windows companion UI

Phase 7 turns the existing Windows shell scaffold into the first usable companion UI for Threadline.

## Completed in this phase

- Enriched foreground-window snapshots with:
  - title
  - process name
  - process id
  - executable path when Windows allows it
- Added a typed local-service client for the Windows app.
- Replaced the placeholder MainWindow with a companion layout for:
  - service status
  - session state
  - attached-window state
  - timeline
  - chat/prompt transcript
  - action proposal controls
- Wired UI buttons to local service flows:
  - check service health
  - start a session
  - use active session
  - refresh foreground window
  - attach current foreground window
  - preview attached-window context
  - store attached-window context
  - compose a prompt from session context
  - propose an approved insert action
  - mark last action complete
- Added a Windows-specific build script.

## What this phase does not yet do

- It does not yet perform actual keyboard/mouse/clipboard execution into another app.
- It does not yet dock beside arbitrary windows.
- It does not yet have a global hotkey.
- It does not yet stream real LLM responses.
- It does not yet use browser/native app deep adapters.

## Manual test path

1. Start the local service.
2. Build and run the Windows companion app.
3. Click **Check Service**.
4. Click **Start Session**.
5. Switch focus to a target window, then return to Threadline and click **Refresh Foreground**.
6. Click **Attach Window**.
7. Click **Preview Window Context**.
8. Click **Store Window Context**.
9. Type text in the question box.
10. Click **Ask / Compose Prompt**.
11. Click **Propose Insert Action**.
12. Click **Mark Last Action Complete**.

## Next work

- Real foreground-window following timer.
- Global hotkey.
- Clipboard/paste executor with confirmation.
- Docked/floating side panel behavior.
- Actual provider response streaming.
- Browser and terminal adapters feeding richer context into this UI.
