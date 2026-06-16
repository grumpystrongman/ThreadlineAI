# ThreadlineAI

ThreadlineAI is a Windows-first, session-aware AI companion framework. It is designed to let a user start a named work session, move across browser, terminal, and desktop applications, and ask a chosen LLM about the approved context of that session.

The project is provider-agnostic. OpenAI, Anthropic Claude, Gemini, DeepSeek, OpenRouter, and local OpenAI-compatible runtimes should sit behind the same provider interface.

## Current status

ThreadlineAI is in early alpha engineering.

- Phase 0/1: scaffold and engineering foundation are complete.
- Phase 2: core domain and local service spine are complete enough for adapter and UI wiring.
- Phase 3: local service/context broker hardening is underway, with optional protected local access, validation, adapter registration, and a smoke-test script.
- The local service uses SQLite for sessions, context events, summaries, provider connection records, and audit events.
- Context preview is a first-class concept and should be called before context is stored or sent to a model.

## What is in this scaffold

- Core domain model for sessions, context events, capture rules, prompt composition, provider abstraction, provider connections, artifacts, audit events, context preview, and adapter registration.
- Infrastructure for SQLite persistence, in-memory adapter registration, in-memory testing, and OpenAI-compatible HTTP providers.
- Local service API for adapters and the Windows shell.
- Windows app shell scaffold for the future slide-out panel, active-window monitoring, and hotkey support.
- Browser extension skeleton for Chrome/Edge context capture through native messaging.
- PowerShell transcript adapter scripts.
- Privacy/security design notes and implementation roadmap.

## Target MVP

The first useful MVP should let a Windows user:

1. Install ThreadlineAI.
2. Configure an LLM provider.
3. Start or resume a named session.
4. Capture approved context from Chrome/Edge selected text, active window metadata, and PowerShell transcript output.
5. Preview the context that will be sent.
6. Ask questions about the current window or full active session.
7. Pause capture, block apps/domains, and export a session summary.

## Repository layout

```text
src/
  Threadline.Core/           Domain model, abstractions, prompt composition, privacy rules
  Threadline.Infrastructure/ Storage and provider implementations
  Threadline.Service/        Local HTTP API for adapters and app shell
  Threadline.Windows/        Windows shell/panel scaffold
adapters/
  browser-extension/         Chrome/Edge extension scaffold
  powershell/                PowerShell transcript adapter scripts
docs/
  Architecture, provider, privacy, roadmap, and service API notes
tests/
  Threadline.Core.Tests/
  Threadline.Infrastructure.Tests/
```

## Development prerequisites

- Windows 11
- .NET 8 SDK or newer
- Visual Studio 2022 with Windows App SDK / WinUI workload
- Node.js 20+ for the browser extension
- PowerShell 7+

## Build and test

```powershell
./eng/build.ps1
./eng/test.ps1
```

Run the local service:

```powershell
dotnet run --project src/Threadline.Service/Threadline.Service.csproj --urls "http://localhost:5057"
```

Then run the local smoke test:

```powershell
./eng/smoke.ps1 -BaseUrl http://localhost:5057
```

See `docs/SERVICE_API.md` for local API details.

## Privacy-first defaults

ThreadlineAI should never behave like invisible spyware. Capture should be visible, pausable, previewable, and rule-driven. Sensitive applications, private browsing windows, credentials, and private records must be excluded or redacted before any provider call.

## License

No license has been selected yet. Choose a license before public distribution or external contribution.
