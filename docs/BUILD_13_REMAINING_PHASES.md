# Build 13 Remaining Phases

This is the working list after the 13.2A shell refactor, 13.3A-13.6A trusted workflow build, and 13.7A sidecar command-center visual alignment.

## Recently completed / in review

| Build | Focus | Status |
| --- | --- | --- |
| 13.2A | Shell refactor | Complete and merged in PR #7 |
| 13.3A | Real Ask + provider response | In PR #8 |
| 13.3B | Context receipts | In PR #8 |
| 13.4A | Work Thread memory + resume | In PR #8 |
| 13.5A | Artifact actions | In PR #8 |
| 13.6A | Privacy/trust controls | In PR #8 |
| 13.7A | Sidecar command-center visual alignment | In PR stacked after PR #8 |

## Remaining build phases

| Phase | Focus | Why |
| --- | --- | --- |
| 13.7B | Command Center full-window app shell | Turn the mockup into a larger management interface for Work Threads, sources, artifacts, decisions, risks, next actions, and privacy. |
| 13.8A | Real artifact generation templates | Replace transcript-filter artifacts with provider-authored, structured Summary, Handoff, Decision Log, Risk List, and Next Action outputs. |
| 13.8B | Artifact library and detail views | Let users open, copy, rename, update, and review generated artifacts instead of only seeing them in transcript. |
| 13.9A | Structured Decision model | Persist decisions as first-class records linked to Work Threads and context receipts. |
| 13.9B | Structured Risk model | Persist risks with severity, owner, mitigation, status, and source context. |
| 13.9C | Structured NextAction model | Persist next actions with owner, due date, status, and source context. |
| 13.10A | Resume My Work | Generate a concise resume summary from Work Thread messages, context events, artifacts, decisions, risks, and next actions. |
| 13.10B | What Am I Missing? | Identify missing owner, deadline, metric definition, source system, acceptance criteria, unmitigated risks, and unclear decisions. |
| 13.11A | Privacy exclusions UI/API | Add user-managed exclusions for apps, windows, processes, domains, and URL patterns before persistence or provider context. |
| 13.11B | Pause, clear, and retention controls | Add obvious capture pause/resume, clear current context, and retention cleanup settings. |
| 13.12A | Exportable handoff bundle | Export a safe Markdown bundle with summary, handoff, decisions, risks, next actions, and receipts. |
| 13.12B | Copy/share improvements | Make Copy All and per-artifact copy include enough context for human review outside ThreadlineAI. |
| 13.13A | Demo readiness and build hardening | Add demo script, small-window QA, debug-mode toggle, and normal-user vs developer UI separation. |
| 13.14A | Packaging and local install polish | Make the Windows app/service install, launch, update, and recover cleanly for early testers. |
| 13.15A | Product telemetry and diagnostics | Add local-first health/status reporting, failure surfaces, and opt-in telemetry boundaries. |

## Near-term recommended order

1. Merge PR #8 after Windows build validation.
2. Merge 13.7A visual alignment after validating the sidecar layout on common screen sizes.
3. Build 13.8A real artifact templates.
4. Build 13.9A-C structured decisions, risks, and next actions.
5. Build 13.10A-B resume and gap-analysis intelligence.
6. Build 13.11A-B privacy exclusions, pause, clear, and retention.
7. Build 13.12A-B export/copy handoff package.
8. Build 13.13A demo readiness and build hardening.
