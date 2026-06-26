# ThreadlineAI Product Overview

ThreadlineAI is a Windows-native AI sidecar for people who work across many apps, tabs, documents, terminals, dashboards, and conversations. It follows the user's approved work context, keeps that context visible, and helps turn scattered work into answers, summaries, handoffs, decisions, risks, and next actions.

It is not designed to be a hidden background scraper. The product direction is local-first, visible, pausable, provider-agnostic, and approval-driven.

## What ThreadlineAI is

ThreadlineAI is a local companion made of four main parts:

1. **Windows sidecar** — a WinUI 3 desktop companion that can attach beside a selected or followed work window.
2. **Local service** — an ASP.NET Core local context broker and provider bridge running on loopback.
3. **Context adapters** — browser extension, PowerShell terminal adapter, active-window resolver, UI Automation reader, and document/file resolver paths.
4. **Work Thread memory** — durable local session state for approved messages, context events, receipts, and work artifacts.

The intent is simple: help a user stay in flow while the AI understands the work they explicitly approve.

## Why this matters

Most AI assistants start from an empty text box. Users have to copy and paste from a browser tab, a document, a ticket, a dashboard, an email, a terminal, and a meeting note just to ask a useful question. That friction makes AI powerful but awkward.

ThreadlineAI is built around a different workflow:

- The user works normally in Windows.
- Threadline follows or locks to the active work target.
- The user previews what Threadline can see.
- The user asks a question against the approved context.
- Threadline responds with a visible context receipt and can save artifacts for continuity.

That makes the assistant feel less like a chatbot and more like a work companion.

## Core capabilities

### Attached Windows sidecar

ThreadlineAI can attach beside the selected, locked, or last active non-Threadline window. Users can also switch to screen-docked mode when they do not want the sidecar following the target.

Use this for:

- reviewing a dashboard while asking questions about definitions or gaps;
- working through a requirements document;
- analyzing a browser page with extension-provided context;
- keeping AI assistance beside a terminal session;
- maintaining continuity while switching between related apps.

### Current Context panel

The sidecar shows the current context source, confidence, summary, and diagnostics. This prevents the product from pretending it understands something it cannot actually see.

Supported context paths include:

- browser page and selected text via Chrome/Edge extension;
- active window metadata;
- UI Automation text from readable desktop apps;
- file-backed document resolution where available;
- PowerShell notes and command output;
- consent-gated screenshot/OCR text extraction (per-app allow/deny decisions persist across restarts).

### Provider-backed Ask

ThreadlineAI routes Ask through the local service and configured provider connection. Supported providers include OpenAI-compatible endpoints (OpenAI, Gemini, DeepSeek, OpenRouter, Local) and Anthropic/Claude with native Messages API support.

The Ask path supports:

- OpenAI-compatible and Anthropic provider execution;
- local provider credentials stored through secure local secret references;
- provider testing;
- provider success/failure audit events without storing prompt content or secrets.

### Work Thread memory

A Work Thread is a named work session. It gives ThreadlineAI a durable place to keep approved work continuity.

A Work Thread can hold:

- transcript messages;
- approved context events;
- context receipts;
- artifacts;
- decisions;
- risks;
- next-action style outputs.

This is useful when work spans multiple apps or days and the user needs to resume without rebuilding all context from scratch.

### Context receipts and trust controls

ThreadlineAI's product model treats trust as a feature, not an afterthought. The sidecar is expected to show what context was used, what was not used, and when richer app-specific context is required.

Important principles:

- approved context should be visible before use;
- blocked/sensitive sources should stay out of provider prompts;
- browser title-only context should be called out honestly;
- provider calls should not silently include hidden data;
- users should be able to pause, clear, and control memory behavior.

### Artifact actions

The sidecar includes artifact actions that turn the current work into reusable outputs:

- Summary
- Handoff
- Decisions
- Risks
- Next Actions

These are not just cosmetic buttons. They are a path toward practical work products: handoff bundles, decision logs, release notes, implementation notes, meeting prep, and operational follow-through.

### Threadline Doctor

Threadline Doctor reports product readiness through structured checks. It helps a normal user, tester, or support person understand what is configured, degraded, or missing.

Doctor checks include:

- service running;
- SQLite writable;
- provider configured;
- provider test result;
- active session;
- active Work Thread;
- browser extension reachability;
- current context source;
- last provider error;
- sidecar geometry state.

### Commercial lifecycle foundation

The repo now includes commercial lifecycle pieces such as:

- Windows Service hosting;
- service install/uninstall scripts;
- package staging script;
- WiX MSI definition and signing hooks;
- first-run setup wizard;
- diagnostics export;
- guarded local-data clearing;
- release validation script;
- smoke-test coverage;
- automated service/security tests.

This does not mean every commercial feature is finished. It means the foundation is being shaped like real Windows software instead of a developer-only prototype.

## Screenshots and visuals

The repository includes **illustrative SVG placeholders** under `docs/assets/screenshots/` so GitHub can show the intended product flow. These are not real screenshots of the running application.

Current visuals:

- `threadline-sidecar-overview.svg` — sidecar attached beside a work application (placeholder).
- `threadline-doctor-readiness.svg` — Doctor/readiness and release-gate view (placeholder).

Real screenshots will be captured from a signed Windows build before general availability.

## Who should try it

ThreadlineAI is worth trying if you regularly do work that crosses many sources and loses context quickly.

Good early users include:

- analysts moving between dashboards, data definitions, tickets, and documents;
- engineering leads reviewing code, terminals, PRs, and release notes;
- operations leaders who need handoffs, decisions, risks, and next actions;
- consultants preparing client summaries from scattered work;
- product managers turning research, requirements, and discussions into action;
- power users who want a local-first AI companion instead of another disconnected chat tab.

## Why someone should try it

Try ThreadlineAI if you want to evaluate a different interaction model for AI at work:

- **Context-aware by design** — it starts from the app, tab, document, or workflow the user is actually working in.
- **Visible and auditable** — it shows context state and can produce context receipts.
- **Local-first** — the service, storage, diagnostics, and provider bridge are built around local control.
- **Provider-flexible** — the provider layer supports OpenAI-compatible providers (OpenAI, Gemini, DeepSeek, OpenRouter, Local) and Anthropic/Claude with native Messages API.
- **Workflow-oriented** — summaries, handoffs, decisions, risks, and next actions are first-class outputs.
- **Commercially shaped** — installer, service lifecycle, diagnostics, clear-data, tests, smoke scripts, and release gates are present.

That is the real value proposition: not a prettier chatbot, but a sidecar that can become part of how work is understood, resumed, and handed off.

## Commercial viability notes

ThreadlineAI has a commercially viable direction because it solves a practical workflow problem: people already pay for AI tools, but they still waste time moving context into and out of them.

The strongest commercial positions are:

1. **Windows knowledge-work companion** — a local AI sidecar for regulated or high-context work.
2. **Enterprise analytics and operations assistant** — help teams turn dashboards, requests, definitions, and decisions into durable artifacts.
3. **Local-first AI context broker** — safe context capture, receipts, provider routing, and audit-friendly execution.
4. **Workflow memory layer** — preserve work continuity across apps without forcing every workflow into a single SaaS tool.
5. **Supportable desktop product** — service lifecycle, diagnostics, clear-data paths, and release gates are already being added.

The next commercial hardening passes should focus on signed installer polish, real screenshots, extension marketplace packaging, update flow, enterprise deployment policy, privacy exclusions UI, and formal security review.

## Honest current boundary

ThreadlineAI is still an actively developing product. It is not yet a polished general-availability app. The repo should be evaluated as a serious pre-release product foundation with real commercial direction, not as a finished enterprise SKU.

Known limitations:

- Ambient capture transcription requires a provider with Whisper-compatible transcription support (e.g. OpenAI). Providers without transcription capability will produce audio and metadata only.
- Layout analysis and full visual layout extraction remain future work.
- Screenshots in the README and docs are illustrative SVG placeholders.

The strongest thing about the product is the shape: context-aware Windows sidecar, local service, visible trust controls, provider flexibility, durable Work Thread memory, and artifact outputs. That is enough to make it worth testing, refining, and packaging properly.
