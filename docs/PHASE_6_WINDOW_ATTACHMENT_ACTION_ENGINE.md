# Phase 6 — Window attachment and action engine

Phase 6 starts the real MVP path: Threadline can attach a session to a window, preview/store window context, and track approved actions intended for the attached window.

## Completed in this phase

- Added window attachment domain model:
  - `WindowSnapshot`
  - `WindowAttachment`
  - attachment status
- Added window action domain model:
  - action kind
  - action risk
  - action status
  - proposed / approved / completed / failed lifecycle
- Added window attachment repository abstraction.
- Added in-memory window attachment repository for local service/runtime use.
- Added window attachment service:
  - attach active window to session
  - detach current window
  - get current attachment
  - list attachments
  - preview active-window context
  - store active-window context
  - propose action
  - approve action
  - complete action
  - fail action
- Added local API endpoints for the above flows.
- Added audit events for window attachment and action lifecycle.
- Added core and infrastructure tests.
- Added smoke-test coverage using a simulated active PowerShell window.

## What this phase does not yet do

This phase does not yet read the Windows foreground window by itself and does not yet perform native input automation. It creates the trusted service-side contract that the Windows shell/adapter will call.

## API shape

```http
POST /sessions/{sessionId}/windows/attach
GET /sessions/{sessionId}/windows/current
DELETE /sessions/{sessionId}/windows/current
GET /sessions/{sessionId}/windows
POST /sessions/{sessionId}/windows/current/preview
POST /sessions/{sessionId}/windows/current/store
POST /sessions/{sessionId}/actions
GET /sessions/{sessionId}/actions
POST /actions/{actionId}/approve
POST /actions/{actionId}/complete
```

## Design rule

Window actions should be treated as proposals until approved. Threadline should not silently manipulate apps in the background. Read/write actions should be visible, auditable, and reversible where possible.

## Next work

- Windows foreground-window detector.
- Global hotkey.
- Companion panel UI.
- Clipboard/paste executor.
- UI Automation reader/writer.
- Browser and terminal adapters calling the attachment/action API.
