# Build 11.6 — Conversation UI Stabilization

Build 11.6 stabilizes the sidecar conversation surface and prepares the Ask flow for the real LLM response path.

## Scope

- Use `TranscriptList` as the single visible conversation surface.
- Back the transcript with a real message collection instead of raw string items.
- Render each entry as a structured message card with speaker, timestamp, and selectable message text.
- Keep User and Threadline messages visibly distinct by speaker label.
- Auto-scroll to the latest transcript item after append or update.
- Keep Follow / Lock behavior connected to Ask context resolution.
- Keep large resolved context internal to Ask and avoid dumping the composed prompt into the transcript.
- Show a temporary Threadline thinking message while Ask resolves context and waits for the provider path.
- Replace the thinking message with either the provider response or the local `/ask` fallback message.

## Current Implementation

- `MainWindow.xaml` defines `TranscriptList` as the conversation `ListView`.
- `MainWindow.xaml.cs` assigns `TranscriptList.ItemsSource` to `_transcriptMessages`.
- `TranscriptMessage` represents each row with:
  - speaker
  - message
  - timestamp
  - timestamp display
- `AppendTranscript` appends message objects, caps transcript size, and scrolls to the newest item.
- `UpdateTranscript` updates an existing message object, used by the Ask thinking state.
- `AskAsync` now appends:
  - the user question
  - a temporary `Threadline` thinking message
  - the final provider response or fallback response by updating the thinking message
- `ResolveContextForAskAsync` still uses the locked target, selected target, last follow target, previous summary, native UI summary, or attachment/foreground fallback.

## Build Validation

Run from the repository root in PowerShell:

```powershell
git pull
./eng/build-windows.ps1
```

Expected result:

- The script finds a .NET SDK.
- The script finds Visual Studio / Build Tools with Windows App SDK packaging tasks.
- Restore completes.
- Release build completes.
- No XAML compile errors.
- No C# compile errors.

## Launch Validation

Run:

```powershell
./eng/run-windows.ps1
```

Expected result:

- Existing `Threadline.Windows` processes are stopped.
- The Release executable launches.
- The app stays running after the startup check.
- The initial Threadline message appears in the Conversation panel.

## Manual Test Steps

1. Launch ThreadlineAI Sidecar.
2. Confirm the Conversation panel shows message cards, not one large textbox.
3. Confirm the first message speaker is `Threadline`.
4. Click `Start`.
5. Confirm a `Threadline Session` message appears.
6. Type a simple question into the Ask box.
7. Click `Ask`.
8. Confirm the transcript immediately shows:
   - a `You` message containing the question
   - a `Threadline` message saying it is thinking
9. Confirm the Conversation list scrolls to the latest item.
10. Confirm the thinking message is replaced by either:
   - the provider answer, if `/ask` is available
   - the fallback message explaining that `/ask` is not exposed yet, if the local service does not support it
11. Switch to another app, then return to Threadline.
12. Confirm Current Target updates to the last active non-Threadline app.
13. Click `Follow / Lock`.
14. Confirm the target changes to locked mode.
15. Ask another question.
16. Confirm Ask uses the locked/selected/follow target path and does not dump full resolved context into the transcript.
17. Click `Clear Transcript`.
18. Confirm the list clears and shows a fresh confirmation message.
19. Ask enough short questions to exceed the visible transcript cap.
20. Confirm old transcript items are removed and the UI remains responsive.

## Go / No-Go Criteria

Go for 11.6 test if:

- Build succeeds.
- App launches and stays running.
- Transcript displays structured message cards.
- Ask creates a user message and a temporary Threadline thinking message.
- The temporary thinking message updates to the final response/fallback.
- Auto-scroll works after append and update.
- Follow / Lock still affects Ask context selection.
- Full resolved context is not dumped into the visible transcript.

No-go if:

- XAML fails to compile.
- The app crashes on startup.
- Ask appends duplicate final responses instead of replacing the thinking message.
- Follow / Lock no longer updates Current Target or Ask context.
- Transcript stops scrolling or freezes with normal usage.

## Next Step

After 11.6 passes manual testing, proceed to Build 11.7: real LLM response path.