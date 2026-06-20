# Build 12.3 — Window-Attached Session Fabric Direction

Threadline should not behave like one global helper panel. It should behave like a context/session fabric across active work.

## Immediate behavior target

- A visible **Start Session** action belongs on the main sidecar header, not buried in overflow controls.
- The floating AI edge icon should appear for other eligible windows even while Threadline is already open.
- Clicking the icon should retarget/open the sidecar beside the clicked window.
- Moving from one target window to another should preserve the active conversation unless the user explicitly starts a new session.

## Product direction

Each work target should be able to have its own attached Threadline surface:

- Notepad document session
- Outlook email/session
- Browser tab/session
- IDE/GitHub/vibecoding session
- Dashboard/session

The user should be able to choose between two modes:

1. **Continue current session on this window**
   - Keep the same conversation.
   - Add the new target's context to the existing thread.
   - Useful when moving from code to browser to email while solving one problem.

2. **Start new session for this window**
   - Create a separate conversation bound to the current window/document/tab.
   - Useful when the work should not mix with the prior thread.

## Saved and shareable session goal

Threadline sessions should be durable and shareable:

- Save transcript, provider, resolved context summaries, target metadata, actions, tests, and relevant Git/GitHub references.
- Retrieve prior sessions by target, project, repo, branch, date, or title.
- Export/share a handoff bundle so a coworker or offshore team member can resume from the last known state.
- Support a shared continuation workflow where another user can open the session, see the context, last tests, last files, prompts, answers, and continue the work.

## Example vibecoding handoff

A user works in an IDE/browser/GitHub window with Threadline attached. They run tests, ask questions, and make changes. Before handing off to offshore developers, they save/share the session. The offshore team opens the session, sees:

- Repo and branch
- Current files/areas being discussed
- Last test results
- Decisions made
- Threadline transcript
- Relevant context snapshots
- Suggested next actions

They can continue from that point instead of starting cold.

## Future build steps

- Add a session registry panel for open/recent sessions.
- Add `Continue here` and `New session here` actions when opening from the edge icon.
- Add persistent session titles and target bindings.
- Add export/share links or bundle files.
- Add GitHub-aware handoff metadata for coding sessions.
