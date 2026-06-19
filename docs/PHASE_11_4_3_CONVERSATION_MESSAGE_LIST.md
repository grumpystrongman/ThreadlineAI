# Phase 11.4.3 — Conversation Message List Stabilization

This phase stabilizes the sidecar conversation surface after replacing the old monolithic transcript textbox with a ListView-based message list.

## Goals

- Keep the app building and launching.
- Use `TranscriptList` as the single visible conversation surface.
- Keep `ChatTranscript` out of the code path.
- Render each conversation entry as its own message item.
- Keep messages copyable through read-only TextBox content inside each message item.
- Cap visible transcript items and message length so large resolved context does not poison the UI.
- Keep full resolved context internal to Ask and avoid dumping the composed prompt into the transcript.
- Keep timeline as a small activity strip, not the main conversation.

## Current Implementation

- `MainWindow.xaml` defines `TranscriptList` as the conversation ListView.
- `AppendTranscript` creates a message card for each speaker/message pair.
- Each message card contains:
  - speaker label
  - read-only selectable message text
  - timestamp
- Transcript item count is capped.
- Message text is capped.
- The transcript scrolls to the newest item after append.
- Timeline entries are capped and also scroll to newest.

## Validation Checklist

Run:

```powershell
git pull
./eng/build-windows.ps1
./eng/run-windows.ps1
```

Validate:

1. App builds.
2. App launches.
3. Initial Threadline message appears.
4. Start session writes a session message.
5. Use Session writes a session message.
6. Clear Transcript clears the list and writes a fresh confirmation.
7. Follow mode writes visible messages without flooding the transcript.
8. Ask writes:
   - context source confirmation from resolver path
   - user message
   - prompt composed confirmation
9. Large context is not dumped into the transcript.
10. Individual message text can be selected and copied.
11. Conversation scrolls to the latest message.

## Next Step

After this is stable, proceed to Phase 11.5: real LLM response path.
