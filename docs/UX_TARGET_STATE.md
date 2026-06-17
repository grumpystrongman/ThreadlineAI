# UX target state

ThreadlineAI should feel like a companion that follows the user's work, not like a control panel that requires several manual setup steps.

## Product direction

The target experience is a slideout companion per app window or active work context.

A user should be able to move between Notepad, browser, PowerShell, Office, VS Code, File Explorer, and other apps while Threadline stays attached to the relevant window context. The chat/session should continue across those moves without forcing the user to repeatedly click refresh, attach, preview, store, and ask.

## Lessons from Phase 10 validation

Phase 10 proved that native window/accessibility context can be captured, but the workflow is too button-heavy.

Problems observed:

- Refresh, attach, and native capture can accidentally target the Threadline window itself.
- The user has to click too many buttons to get context into a useful prompt.
- Raw UI/accessibility output can be noisy.
- Notebook-style apps may expose every tab or control, which makes the prompt too detailed and not summarized enough.

## Required UX correction

Future phases should move from manual steps to intent-based capture.

Preferred flow:

1. User opens or focuses an app.
2. Threadline detects the app/window context.
3. Threadline shows a slideout beside or attached to that window.
4. User asks a question.
5. Threadline gathers the best available approved context automatically.
6. Threadline summarizes the context before composing the model prompt.
7. User can inspect what was used, pause capture, redact, or remove context.

## Capture ranking

Threadline should prefer cleaner context sources before noisy native accessibility dumps:

1. Browser extension page/selection context.
2. App-specific integrations such as Office/document workflows.
3. Selected text or clipboard-approved text.
4. PowerShell/terminal adapter context.
5. UI Automation/native accessibility context.
6. Screenshot/vision fallback only after explicit approval.

## Summary behavior

Native UI capture should not dump every control by default.

Before prompt composition, Threadline should create a compact app-context summary with:

- App name and window title.
- Likely document/page title.
- Main visible text or selected text.
- Important fields or controls only when relevant.
- Noise removed, including duplicate buttons, tabs, menus, resize grips, and system chrome.
- A short note when the capture is incomplete or low confidence.

The raw capture should remain available for audit/debug, but the prompt should use the summarized context unless the user asks for raw details.

## Slideout requirements

The slideout should:

- Track the active window without stealing focus.
- Avoid becoming the capture target.
- Provide one primary action: ask Threadline about the current window/session.
- Show a small current-context indicator.
- Let the user approve, pause, or clear context quickly.
- Keep the session continuous as the user changes apps.

## Near-term implementation path

- Add target-window tracking that is independent of button clicks.
- Add a global hotkey for capture/ask without focusing Threadline.
- Add a per-window context state model.
- Add a context summarizer before prompt composition.
- Add a slideout/docked companion shell that follows the target app window.
- Keep current manual buttons as diagnostics, not the primary user flow.
