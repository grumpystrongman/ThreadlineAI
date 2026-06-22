# Build 18: Native Window Context Providers

Build 18 makes attached windows more believable without pretending native capture is magic. The Windows sidecar now chooses an app-aware native context provider, captures whatever it can safely read, and labels the result with an honest context level.

## Context levels

Threadline now records one of these levels on native window capture:

- `FullDocument` / Full document
- `VisibleDocument` / Visible document
- `SelectedText` / Selected text
- `ReadableUiTree` / Readable UI tree
- `TitleOnly` / Title only
- `NoReadableContext` / No readable context

The level is carried in window metadata as `nativeContext.level` and `nativeContext.levelDisplay`. Captured text, when available, is carried as `nativeContext.content` and included in the active-window context event.

## Providers added

### Notepad / file-backed text

- Attempts safe file-backed capture when a real local file path can be resolved from the window title.
- Falls back to visible text through Windows accessibility.
- Falls back again to title-only when text is not readable.

### VS Code / IDE

- Infers active file and workspace from the window title when possible.
- Uses Windows accessibility for readable UI/editor surface content when exposed.
- Reports this as readable UI tree unless a stronger provider can prove full document access.

### Terminal / PowerShell

- Captures visible terminal text through accessibility when available.
- Reports the level as visible document because this is visible output, not guaranteed full scrollback or shell history.

### Office documents

- Uses safe title/path/UIA signals.
- Does not invoke COM automatically.
- Reports warnings that COM extraction is intentionally not part of this native capture path.

### PDF readers

- Captures title/path and accessible visible text when available.
- Explicitly reports that OCR/vision fallback is not wired into this build yet.

### Power BI / Tableau / browser dashboards

- Prefers browser-extension context first.
- Native capture is fallback only and should be treated as partial because dashboards often expose labels and chrome, not data semantics.

### Generic Win32/UIA

- Captures readable controls through Windows accessibility.
- Labels this as readable UI tree, title only, or no readable context.

## Service integration

The Windows client now attaches native context metadata when calling `/sessions/{sessionId}/windows/attach`. `WindowSnapshot.ToContextEvent(...)` now includes captured native content in the prompt context when present, along with provider name, context level, guidance, and warnings.

## Intentional limits

Build 18 does not promise perfect document extraction. It deliberately avoids claiming full context when the app only exposes a title, a UI tree, or partial visible text. This gives Threadline a sane base for future app-specific providers without lying to the user or to the prompt composer.
