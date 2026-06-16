# ThreadlineAI

ThreadlineAI is a Windows-first, session-aware AI companion framework. It is designed to let a user start a named work session, move across browser, terminal, and desktop applications, and ask a chosen LLM about the approved context of that session.

The project is provider-agnostic. OpenAI, Anthropic Claude, Gemini, DeepSeek, OpenRouter, and local OpenAI-compatible runtimes should sit behind the same provider interface.

## What is in this scaffold

- Core domain model for sessions, context events, capture rules, prompt composition, and provider abstraction.
- Infrastructure skeleton for in-memory storage and OpenAI-compatible HTTP providers.
- Local service API scaffold for adapters and the Windows shell.
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
  Architecture, provider, privacy, and roadmap notes
```

## Development prerequisites

- Windows 11
- .NET 8 SDK or newer
- Visual Studio 2022 with Windows App SDK / WinUI workload
- Node.js 20+ for the browser extension
- PowerShell 7+

## Privacy-first defaults

ThreadlineAI should never behave like invisible spyware. Capture should be visible, pausable, previewable, and rule-driven. Sensitive applications, password managers, private browsing windows, tokens, credentials, PHI, and other sensitive information must be excluded or redacted before any provider call.

## License

No license has been selected yet. Choose a license before public distribution or external contribution.
