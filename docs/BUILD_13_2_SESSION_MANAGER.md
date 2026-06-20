# Build 13.2 — Session Manager

Build 13.2 moves ThreadlineAI from hidden Work Thread persistence toward visible session control.

Build 13.1 created the Work Thread foundation. Build 13.1A wired the sidecar to that foundation, but the experience is still too implicit: the user cannot easily see available sessions, select one, rename one, or deliberately tie a window to an existing Work Thread.

## Product goal

ThreadlineAI should make work continuity visible. A user should always be able to answer:

- Which Work Thread am I in?
- What other Work Threads exist?
- Which window is attached to this Work Thread?
- Can I rename this workstream so I can find it later?
- Can I connect this app/window to an existing Work Thread instead of starting over?
- Can I detach this app/window from the current Work Thread?

## Build 13.2 scope

### In scope

- Visible session manager section in the sidecar.
- List recent/open Work Threads.
- Select a Work Thread.
- Resume the selected Work Thread.
- Rename the selected or active Work Thread from a visible text field.
- Tie the current/pending window context to the selected Work Thread.
- Detach the current window from the active Work Thread/session binding.
- Keep Work Thread loading manual-first for startup stability.

### Out of scope

- True multi-sidecar windows.
- Multi-user sharing/collaboration.
- Cloud sync.
- Teams/Outlook integration.
- Artifact generation beyond the existing Context Receipt and saved transcript foundation.

## Acceptance criteria

- User can open the sidecar and refresh a visible list of Work Threads.
- User can select a Work Thread and resume it.
- User can rename a selected/active Work Thread without using hidden menus.
- User can tie the current window to a selected Work Thread.
- User can detach the current window binding.
- The app does not perform Work Thread service calls during launch-critical startup.

## Implementation notes

The build should preserve the existing service endpoints from Build 13.1. Prefer UI/client wiring over service churn unless an endpoint gap is discovered.

Session Manager must remain stable even if the local service is unavailable: show a visible status message and do not crash the shell.
