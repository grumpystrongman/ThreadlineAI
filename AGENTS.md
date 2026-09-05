# ThreadlineAI Agent Operating Contract

This file is the canonical working agreement for AI coding agents operating in this repository.

## Mission

ThreadlineAI is a Windows-native, local-first AI sidecar for high-context work. It should follow user-approved context across apps, tabs, documents, terminals, and sessions, then turn that context into useful answers and durable work artifacts.

The product must remain visibly context-aware, privacy-first, and honest about what it can and cannot see.

## Product owner

Jeff is the product owner and final reviewer. Treat consequential product, UX, privacy, architecture, and release decisions as reviewable decisions, not silent assumptions.

## How to work

For any non-trivial task:

1. Inspect the relevant code, docs, tests, and recent implementation state before proposing changes.
2. State a concise plan with intended outcome, affected areas, risks, and verification approach.
3. Prefer direct implementation over asking Jeff to run diagnostics you can perform yourself.
4. Make the smallest coherent change that solves the real problem; avoid unrelated cleanup.
5. Verify against objective evidence: builds, tests, smoke scripts, source requirements, screenshots, logs, or API behavior.
6. Report what changed, what was verified, and any remaining uncertainty or decision Jeff should review.
7. If the task exposes a reusable lesson or repeated workflow, update the appropriate playbook/skill instead of forcing future sessions to rediscover it.

## Architectural invariants

Preserve these unless Jeff explicitly approves a change:

- `Threadline.Core` owns domain models, abstractions, prompt composition, and privacy rules.
- `Threadline.Infrastructure` owns storage and provider implementations.
- `Threadline.Service` is the local broker/API, provider bridge, Doctor, and lifecycle surface.
- `Threadline.Windows` is the WinUI 3 sidecar experience.
- SQLite remains the local continuity/memory store unless a documented migration is approved.
- Context acquisition must be source-aware and confidence-aware; never imply visual/page access that the system does not actually have.
- Capture must be visible, pausable, previewable, consent-aware, and auditable.
- Sensitive context must be blocked or redacted before provider calls.
- Provider implementations belong behind the provider abstraction; avoid provider-specific behavior leaking into the UI.
- The sidecar should remain focused on the current work, with advanced controls secondary to the conversational workflow.
- The local service should behave as product infrastructure, not as a manual developer prerequisite.

## UX principles

- Default experience: clean ChatGPT/Gemini/Claude-style conversation beside the user's work.
- Sidecar behavior should preserve the user's primary workspace rather than obscure it.
- Context state must be understandable: source, confidence, limitations, and errors should be visible when relevant.
- Advanced diagnostics and configuration belong in secondary surfaces/drawers, not in the main conversation path.
- Do not add UI complexity unless it materially improves comprehension, control, or workflow speed.
- Error states should explain the actionable problem without exposing secrets or unnecessary implementation detail.

## Privacy and security rules

- Never log provider secrets, credential values, raw tokens, or sensitive prompt content.
- Preserve consent boundaries for screenshot/OCR and ambient capture.
- Treat private browsing, credentials, private records, and sensitive applications as exclusion/redaction concerns by default.
- Diagnostics must be useful but redacted.
- Data clearing must remain explicit and verifiable.
- Any new context source requires a documented privacy path before it is considered complete.

## Definition of done

A change is not done because it compiles or looks plausible. Depending on scope, completion should include:

- relevant unit/integration tests;
- standard build validation;
- Windows build validation for WinUI changes when the environment supports it;
- browser-extension build validation for extension changes;
- smoke tests for service/API behavior;
- release validation for release-sensitive changes;
- updated documentation when behavior, architecture, setup, privacy, or support boundaries changed.

Use the scripts under `eng/` rather than inventing parallel verification commands when an existing script already covers the task.

## Standard verification commands

```powershell
./eng/build.ps1
./eng/test.ps1
./eng/build-windows.ps1
./eng/build-browser-extension.ps1
./eng/smoke.ps1 -BaseUrl http://localhost:5057
./eng/smoke-build23.ps1 -BaseUrl http://localhost:5057
./eng/smoke-powershell-adapter.ps1 -BaseUrl http://localhost:5057
./eng/release-validate.ps1
```

On non-Windows hosts, use the repository's supported skip path rather than claiming Windows UI validation succeeded:

```powershell
./eng/release-validate.ps1 -SkipWindows
```

## Reusable workflows

Before performing these task types, read the matching recipe:

- Feature work: `docs/agent-skills/FEATURE_DELIVERY.md`
- Bug fixes: `docs/agent-skills/BUG_FIX.md`
- UX/UI changes: `docs/agent-skills/UX_CHANGE.md`
- Release/readiness work: `docs/agent-skills/RELEASE_READINESS.md`

For product context, architecture, privacy, roadmap, service contracts, or historical build decisions, inspect the relevant document under `docs/` rather than relying on memory alone.

## Communication standard

Keep Jeff out of mechanical diagnostic loops when repository/tool access can answer the question. Surface only decisions, tradeoffs, blockers, meaningful deviations, and verification results.

When a requirement is ambiguous but a safe reversible interpretation exists, make the best-supported choice, document it, and continue. Ask only when the unresolved choice would materially change product behavior, privacy, architecture, data handling, or irreversible work.
