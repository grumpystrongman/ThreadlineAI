# Phase 10.5 — Companion UX correction and context summary

Phase 10.5 corrects the main UX issue found during Phase 10 validation: raw native UI capture works, but it is too noisy and too manual for the target product experience.

## Completed in this phase

- Added a compact context summarizer for noisy native UI captures.
- Removed common window-chrome/control noise from native UI context.
- Added summarized context output to the Windows companion transcript.
- Updated prompt composition so it prefers summarized native context over raw UI dumps.
- Preserved raw capture as a debugging/audit-oriented view rather than the primary prompt input.
- Documented the target state for a slideout companion in `docs/UX_TARGET_STATE.md`.

## Behavior change

Before Phase 10.5, native capture could send long control lists into the prompt. This could include tabs, buttons, resize controls, title bar controls, menu chrome, and other low-value UI elements.

After Phase 10.5, native UI capture is transformed into a compact working summary before Ask / Compose Prompt uses it.

## Validation target

A prompt like:

```text
Describe the content in the Notepad window.
```

should now produce a composed prompt that uses a compact summary of the Notepad content instead of raw native UI control output.

## Product direction

The manual diagnostic buttons still exist, but they should not be the final user experience. Future work should move toward:

- A slideout companion per active app/window.
- Automatic target awareness.
- One primary user action: ask about the current app/session.
- Context source ranking.
- Context summaries by default.
- Raw capture only when requested or needed for diagnostics.

## Phase 11 licensing note

The user has Microsoft apps installed but does not have full edit capability for all Microsoft Office products. OneNote does work and should be a reliable validation target.

Phase 11 should still build Office/document workflow capability, but it should not depend on a fully licensed editable Office install. It should support multiple paths:

1. OneNote where available.
2. Read-only Office app context where installed.
3. Clipboard or selected-text capture.
4. File import for supported document formats.
5. Future Microsoft Graph or Office add-in integration when credentials and licensing allow it.

This lets Threadline have the capability even when the local test machine cannot edit every Office app.
