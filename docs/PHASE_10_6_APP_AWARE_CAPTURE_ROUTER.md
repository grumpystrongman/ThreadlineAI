# Phase 10.6 — App-aware capture router

Phase 10.6 moves Threadline away from treating every app window as a generic native UI capture problem.

The product target is the future `TL>>` slideout: Threadline follows the active app/window and chooses the best capture provider automatically.

## Why this phase exists

Phase 10 and 10.5 proved that native UI capture can read window structure, but they also exposed a hard limitation: modern tabbed apps can expose mixed or misleading native accessibility trees.

Example: modern Notepad can expose the active tab title while also exposing body text from another tab. Summarizing that text is false confidence.

## Router behavior

The app-aware capture router chooses a capture plan based on the selected window process and title.

Current plans:

- Chrome / Edge / Firefox: browser extension provider preferred; native UI is not trusted for page-level questions.
- Notepad: Notepad tab/document provider required; native UI body text is treated as ambiguous.
- OneNote: OneNote-aware provider preferred; native UI fallback can be attempted for visible note context.
- Terminal / PowerShell / CMD: terminal adapter preferred.
- Unknown apps: native UI fallback.

## Current implementation

- Added `AppCaptureRouter`.
- Added `AppCapturePlan` and `CaptureProviderKind`.
- `Use Selected App` now attaches the selected window and asks the router for the appropriate capture plan.
- If the plan can capture now, Threadline captures and summarizes.
- If the plan needs a provider that is not implemented yet, Threadline says so instead of pretending native UI is reliable.

## Product rule

Threadline must not confidently summarize data from the wrong tab, page, document, or app surface.

When capture confidence is low, the correct behavior is to say what Threadline can identify, explain what it cannot safely know, and route to the proper capture provider.

## Next provider work

1. Browser extension bridge into the Windows companion for Chrome/Edge page context.
2. Notepad tab/document provider or tab picker.
3. OneNote-specific context provider.
4. Terminal adapter integration into companion context.
5. Native UI fallback cleanup and confidence scoring.
