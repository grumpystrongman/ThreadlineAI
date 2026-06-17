# Phase 10.7 — Tab Target Registry

Phase 10.7 changes the Windows companion target model from simple open windows to selectable Threadline targets.

A Threadline target may be:

- a top-level app window
- an app tab
- a document surface
- a browser page target
- a shell target

This is needed for the future `TL>>` slideout because many useful work surfaces live inside a parent app window.

## Added

- `ThreadlineTarget`
- `ThreadlineTargetKind`
- `ITabProvider`
- `TabTargetRegistry`
- `NotepadTabProvider`
- `BrowserTabProvider`

## Behavior

The picker now loads targets from `TabTargetRegistry` instead of only raw top-level windows.

`Use Selected App` now accepts a selected target. It attaches the parent window, checks whether the target body can be read, and either captures context or explains which provider is needed.

## Notepad

Modern Notepad can expose tab titles while mixing body text from another tab. Phase 10.7 lists detected Notepad tabs as selectable targets but marks body capture as not available until a safer provider maps tab to body.

## Browser

Browser targets are routed toward the browser extension provider. Native UI should not be used for page-level questions.

## Next

- Complete safe Notepad tab body capture.
- Connect browser targets to extension context.
- Add shell target provider using the existing adapter.
