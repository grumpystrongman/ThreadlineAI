# Roadmap

## v0.1 Foundational alpha (complete)

- Windows shell launches.
- Local service runs.
- Start/resume/end sessions.
- Store context events.
- Browser extension sends URL/title/selection.
- PowerShell transcript adapter captures command output.
- Prompt composer builds structured prompts.
- One OpenAI-compatible provider works.
- Context preview exists.

## v0.2 Usable local workflow (complete)

- Active window polling/event loop.
- Browser visible text capture with domain allow/block rules.
- Better PowerShell transcript tailing.
- Session timeline UI.
- Rolling summary.
- Secure credential storage.

## v0.3 Release-candidate cleanup (complete)

Completed in this pass:

- Removed placeholder behavior that misleads users: ambient capture stop message no longer claims a transcript was saved when only audio/manifest/handoff are produced.
- Removed hardcoded version-leaking strings from process intelligence diagnostics.
- Removed empty-payload fallback that sent fake "placeholder" text to the service.
- Screenshot/OCR consent allow/deny decisions now persist across app restarts via SQLite PrivacySettings table.
- README and PRODUCT_OVERVIEW reconciled with actual capabilities; SVG screenshots clearly labeled as illustrative placeholders.
- Known limitations documented honestly in README, PRODUCT_OVERVIEW, and this roadmap.

## v0.4 Feature completion (this pass)

Completed in this pass:

- Ambient capture real transcription via Whisper API: TranscriptionService connects to OpenAI-compatible providers, POST /transcribe endpoint, automatic transcription on capture stop with transcript.md and handoff.md generation.
- Anthropic/Claude provider adapter: native Messages API implementation with system prompt extraction, proper content block serialization, and x-api-key authentication. Claude re-added to selectable providers with correct defaults.
- Raw screenshot retention: consent-gated PNG storage to %LOCALAPPDATA%/ThreadlineAI/screenshots/ with timestamp and process name.
- Vision model image support: LlmImageAttachment added to LlmMessage, OpenAI provider sends data URI content arrays, Anthropic provider sends base64 image source blocks. Both providers report SupportsVision=true.
- Image extraction capability updated from not-implemented to implemented in process intelligence diagnostics.
- All six providers (OpenAI, Gemini, DeepSeek, OpenRouter, Local, Claude) execute end-to-end.
- Documentation updated to reflect new capabilities honestly.

Known limitations remaining after this pass:

- Ambient capture transcription requires a provider with Whisper-compatible support (e.g. OpenAI). Non-transcription providers produce audio and metadata only.
- Layout analysis and full visual layout extraction remain future work.
- Screenshots in README/docs are illustrative SVG placeholders, not captures of the running application.
- Signing requires a real certificate (`THREADLINE_SIGN_CERT_SHA1`).

## v1.0 Enterprise-ready beta (out of scope for v0.4)

- Admin policy controls.
- Enterprise SSO.
- Audit logs.
- Local-only/private mode.
- Signed installer and auto-update channel.
- Real screenshots from signed Windows build.
- Full visual layout analysis.
