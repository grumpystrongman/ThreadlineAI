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
- Does not store raw screenshots by default.
- Does not send raw screenshots as image provider payloads in this build.
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

Build 19 captures the screenshot into memory, runs OCR, then clears the image byte array unless raw screenshot storage is explicitly enabled. The UI does not expose persistent raw screenshot storage in this build, so raw screenshots are not written to disk.

The provider handoff remains text-only: redacted OCR text, local vision summary, and Context Receipt. Raw image provider calls are intentionally not wired yet.

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
- raw screenshot stored: no
- raw screenshot provider image handoff: no
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
- The build does not yet support raw screenshot retention, even when the consent model has a placeholder for it.
- The build does not yet send raw images to a vision-capable provider. That should remain a separate, explicit-consent build.
- Redaction is regex-based and should be treated as best effort.

## Validation checklist

- With `Vision once` off, screenshot/OCR is blocked and the receipt ladder says it was blocked.
- With `Vision once` on and a readable provider available, the stronger provider wins and `Vision once` resets.
- With `Vision once` on, a denied app still blocks screenshot/OCR.
- With `Vision once` on and no stronger context available, screenshot/OCR captures only the attached window region.
- After a screenshot/OCR attempt, `Vision once` resets.
- The transcript and receipt show that screenshot/OCR was used.
- The raw screenshot is not stored.
- OCR text in the prompt context is redacted before handoff where patterns are detected.
