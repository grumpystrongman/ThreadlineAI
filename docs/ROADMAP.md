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

## v0.3 Release-candidate cleanup (this pass)

Completed in this pass:

- Removed placeholder behavior that misleads users: ambient capture stop message no longer claims a transcript was saved when only audio/manifest/handoff are produced.
- Removed hardcoded version-leaking strings from process intelligence diagnostics.
- Removed empty-payload fallback that sent fake "placeholder" text to the service.
- Removed Claude from selectable provider list (no Anthropic adapter exists); all remaining providers (OpenAI, Gemini, DeepSeek, OpenRouter, Local) execute end-to-end via the OpenAI-compatible path.
- Screenshot/OCR consent allow/deny decisions now persist across app restarts via SQLite PrivacySettings table.
- README and PRODUCT_OVERVIEW reconciled with actual capabilities; SVG screenshots clearly labeled as illustrative placeholders.
- Known limitations documented honestly in README, PRODUCT_OVERVIEW, and this roadmap.

Known limitations remaining after this pass:

- Ambient capture records audio and metadata but does not produce a real transcript (no speech-to-text integration yet).
- Anthropic/Claude provider requires a dedicated adapter before it can be offered.
- Screenshot/OCR vision extracts text via Windows OCR but does not retain raw screenshots or send images directly to vision-capable models.
- Screenshots in README/docs are illustrative SVG placeholders, not captures of the running application.
- Signing requires a real certificate (`THREADLINE_SIGN_CERT_SHA1`).

## v1.0 Enterprise-ready beta (out of scope for v0.3)

- Admin policy controls.
- Enterprise SSO.
- Audit logs.
- Local-only/private mode.
- Signed installer and auto-update channel.
- Anthropic/Claude provider adapter.
- Real transcript from ambient capture (speech-to-text).
- Real screenshots from signed Windows build.
- Raw screenshot retention and direct vision-model image submission.
