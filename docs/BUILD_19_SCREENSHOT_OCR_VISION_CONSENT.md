# Build 19 — Screenshot + OCR/Vision Consent Pipeline

Build 19 adds the privacy-sensitive fallback path for apps that expose no usable provider, file-backed, or UI Automation content.

This build intentionally treats screenshot/OCR as a last resort. It is powerful, but it is also the highest-risk context source because it can see anything visible inside the attached window. The implementation is therefore opt-in, visible, auditable, and reset after use.

## What changed

- Added a consent-gated `ScreenshotVisionContextProvider`.
- Captures only the attached window region, not the full desktop.
- Blocks minimized-window capture so Threadline does not accidentally capture the desktop behind a target.
- Runs Windows OCR against the approved screenshot region.
- Produces a local text vision summary from OCR output.
- Redacts sensitive-looking values before prompt/provider handoff where possible.
- Stores raw screenshots locally when consent allows (saved to `%LOCALAPPDATA%/ThreadlineAI/screenshots/`).
- Sends raw screenshots as image payloads to vision-capable models (GPT-4V, Claude) when image attachments are present.
- Emits a Context Receipt showing that screenshot/OCR/vision was used.
- Adds a per-app screenshot/OCR allow/deny policy.
- Adds a visible `Vision once` privacy control; approval is consumed and reset after context resolution.

## Consent model

Screenshot/OCR runs only when all of the following are true:

1. A target window is attached or selected.
2. The user has enabled the visible `Vision once` control.
3. The target app is not on the screenshot/OCR deny list.
4. The resolver reaches the screenshot/OCR fallback after stronger providers miss.

An app can be marked:

- `Allowed`: approved app, but every capture still requires visible one-time approval.
- `Denied`: screenshot/OCR is blocked even if `Vision once` is checked.
- `PromptEachTime`: default behavior; one-time approval is required for each capture.

There is no silent capture path.

## Provider ladder position

The fallback remains after the safer context providers:

1. Browser extension provider
2. File/document provider
3. UI Automation provider
4. Clipboard/selection placeholder, still blocked unless explicitly approved in a future build
5. Screenshot/OCR/vision provider, Build 19 consent-gated fallback
6. Title/process fallback

## Raw screenshot handling

The screenshot is captured into memory, OCR is run, then the image byte array is either saved to local storage or cleared depending on the user's raw screenshot storage consent.

When `RawScreenshotStorageAllowed` is true, the raw PNG is written to `%LOCALAPPDATA%/ThreadlineAI/screenshots/` with a timestamp and sanitized process name. These images can be attached to LLM requests as vision payloads.

When storage is not allowed, the byte array is cleared after OCR and only the redacted text summary is retained.

The provider handoff supports both text-only (redacted OCR text, vision summary, Context Receipt) and image-attached requests. Vision-capable providers (OpenAI with GPT-4V, Anthropic/Claude) receive image payloads as data URI content arrays or base64 image source blocks respectively.

## Redaction

Build 19 includes `SensitiveContentRedactor`, which redacts common sensitive-looking values from OCR text before it is inserted into prompt context:

- email addresses
- SSN-like values
- phone numbers
- credit-card-like long number sequences
- API key / token / password assignments
- bearer tokens
- connection-string passwords

This is best-effort redaction, not a compliance guarantee. The receipt states when redaction happened.

## Context Receipt behavior

When screenshot/OCR succeeds, the receipt reports:

- `SourceUsed = screenshot/ocr/vision provider`
- `CaptureKind = ScreenshotVision`
- user-approved attached-window screenshot region
- OCR character count after redaction
- vision summary character count
- raw screenshot stored: yes/no (depends on consent)
- raw screenshot provider image handoff: yes (when vision-capable provider and images are attached)
- provider ladder attempts

This keeps the answer honest about what Threadline actually saw.

## Files changed

- `src/Threadline.Windows/Services/ContextResolutionModels.cs`
- `src/Threadline.Windows/Services/SensitiveContentRedactor.cs`
- `src/Threadline.Windows/Services/ScreenshotVisionConsentPolicy.cs`
- `src/Threadline.Windows/Services/ScreenshotVisionContextProvider.cs`
- `src/Threadline.Windows/Services/ActiveWindowContentResolver.cs`
- `src/Threadline.Windows/Services/ProcessIntelligenceService.cs`
- `src/Threadline.Windows/MainWindow.xaml`
- `src/Threadline.Windows/MainWindow.ProviderPicker.cs`
- `src/Threadline.Windows/MainWindow.PromptContext.cs`
- `src/Threadline.Windows/Threadline.Windows.csproj`
- `src/Threadline.Windows/GlobalUsings.cs`

## Known limits

- OCR quality depends on Windows OCR availability and the user's installed language support.
- App allow/deny policies are persisted across restarts via the SQLite privacy store (PrivacySettings table).
- Raw screenshot storage requires explicit consent. Files are stored unencrypted on the local filesystem.
- Image payloads sent to vision-capable providers are the full captured region; no sub-region selection is available.
- Redaction is regex-based and should be treated as best effort.

## Validation checklist

- With `Vision once` off, screenshot/OCR is blocked and the receipt ladder says it was blocked.
- With `Vision once` on and a readable provider available, the stronger provider wins and `Vision once` resets.
- With `Vision once` on, a denied app still blocks screenshot/OCR.
- With `Vision once` on and no stronger context available, screenshot/OCR captures only the attached window region.
- After a screenshot/OCR attempt, `Vision once` resets.
- The transcript and receipt show that screenshot/OCR was used.
- When storage consent is denied, the raw screenshot is not stored and bytes are cleared.
- When storage consent is granted, the raw screenshot is saved to local storage.
- OCR text in the prompt context is redacted before handoff where patterns are detected.
