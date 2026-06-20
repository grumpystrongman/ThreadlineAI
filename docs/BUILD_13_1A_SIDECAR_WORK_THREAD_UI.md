# Build 13.1A — Sidecar Work Thread UI Wiring

Build 13.1A wires the Windows sidecar UI into the Build 13.1 Work Thread persistence foundation.

## Scope

This build keeps the existing Windows sidecar architecture intact and adds the first visible work-continuity behaviors:

- Active Work Thread status in the sidecar header.
- Work Thread title box.
- Main-header actions for:
  - New Thread
  - Resume
  - Rename
  - Close
- Startup load/create of an active Work Thread.
- Loading stored messages for the active Work Thread.
- Persisting user Ask prompts as ConversationMessage records.
- Persisting assistant Ask responses as ConversationMessage records.
- Persisting followed, locked, manual, and inferred ContextEvent records.
- Creating Context Receipt v1 records for Ask responses.
- Displaying a copyable Context Receipt v1 under each Ask answer.

## User-visible behavior

On startup, Threadline loads the active Work Thread if one exists. If none exists, it creates a default Work Thread.

When the user asks a question, Threadline now:

1. saves the user prompt to the active Work Thread;
2. resolves the attached/followed context;
3. saves a ContextEvent where possible;
4. saves a ContextReceipt;
5. appends the Context Receipt text to the answer;
6. saves the assistant response to the active Work Thread.

## Context Receipt v1

The first receipt version records:

- Work Thread title
- used app/window/source
- capture mode
- snapshot time
- known exclusions
- limitations
- persisted receipt ID when save succeeds

## Not included yet

Build 13.1A does not yet implement:

- A full Work Thread picker/history browser.
- Artifact generation buttons.
- Handoff Generator.
- Decision/Risk/Next Action editors.
- Multi-user collaboration.
- True multiple simultaneous sidecar windows.

Those belong in later Build 13.x slices after the Work Thread foundation proves stable.

## Next recommended slice

Build 13.1B should add a Work Thread picker/resume panel and improve Copy All so it can copy the full stored Work Thread transcript, not only the visible transcript buffer.
