# Build 11.6.1 — Conversation Usability Cleanup

Build 11.6.1 is a focused usability pass on the sidecar shell before moving deeper into slide-out experiences.

## Changes

- Moved workspace/session controls to the top header area:
  - New
  - Use Session
  - Check
  - Start
  - Clear Context
- Moved conversation actions into the ask input area:
  - Ask
  - Propose
  - Done
  - Clear
- Made app target controls smaller while keeping them readable:
  - Refresh
  - Use
  - Follow / Lock
- Narrowed the Current Target / Open Apps column so the conversation gets more horizontal space.
- Added a compact item template to the Open Apps and Tabs list so target selection is still understandable without dominating the UI.
- Added transcript-level copy controls:
  - Copy Conversation
  - Copy Last Answer

## Why this build exists

The 11.6 message cards worked, but card-level text selection made it hard to copy across multiple messages. The controls also felt flat: session controls, ask controls, target controls, and transcript controls all competed at the same visual level.

11.6.1 separates those responsibilities without over-polishing the shell ahead of the planned slide-out architecture.

## Test Steps

Run:

```powershell
git pull
./eng/build-windows.ps1
./eng/run-windows.ps1
```

Validate:

1. App builds and launches.
2. Top header shows the high-level controls: New, Use Session, Check, Start, Clear Context.
3. Current Target panel is narrower and less visually dominant.
4. Refresh / Use / Follow Lock controls are smaller but still readable.
5. Open Apps and Tabs list is compact and readable enough to identify targets.
6. Conversation panel has more readable width than 11.6.
7. Copy Conversation copies all visible transcript messages in order.
8. Copy Last Answer copies the most recent Threadline message.
9. Ask / Propose / Done / Clear appear in the ask input area.
10. Ask still appends a You message and a Threadline thinking message.
11. Thinking message still updates to the response or fallback.
12. Follow / Lock still updates Current Target and still influences Ask context.
13. Clear clears the transcript and shows the confirmation message.

## Go / No-Go

Go if:

- Build succeeds.
- Layout is less cramped.
- Copy Conversation works across multiple cards.
- Ask flow still works.
- Follow / Lock still works.

No-go if:

- XAML fails to compile.
- Clipboard commands throw errors.
- Ask buttons become hard to find.
- The left target selector becomes too compressed to identify a target.
- Follow / Lock behavior regresses.
