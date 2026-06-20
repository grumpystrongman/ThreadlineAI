# Build 13.0 Engineering Backlog: Work Continuity Layer

This backlog implements the Build 13.0 work-continuity-layer requirements incrementally without rewriting the current ThreadlineAI application.

The goal is to preserve the working WinUI 3 sidecar, ASP.NET Core service, SQLite persistence, active-window context resolution, Follow/Lock behavior, browser extension support, and current context provider/resolver layer while adding durable Work Threads, Context Receipts, artifacts, decisions, risks, next actions, and privacy controls.

## Delivery Principles

- Keep the app runnable after every increment.
- Prefer additive service APIs and UI panels over large rewrites.
- Keep current sidecar/chat/context features working.
- Separate normal UX from developer diagnostics.
- Store durable state in the service/SQLite layer.
- Make context transparent before making automation powerful.
- Build one useful artifact flow end-to-end before adding many partial flows.

## P0: Stabilize Product Foundation

### B13-P0-001: Add canonical Build 13 product docs

Status: complete when this backlog and the Build 13 requirements doc are committed.

Tasks:

- Add `docs/BUILD_13_0_WORK_CONTINUITY_LAYER_REQUIREMENTS.md`.
- Add `docs/BUILD_13_0_ENGINEERING_BACKLOG.md`.
- Reference Build 13 direction from future release notes.

Acceptance criteria:

- Repo contains a clear product vision, commercial wedge, requirements, data model, UX requirements, privacy requirements, implementation phases, and backlog.

### B13-P0-002: Preserve current launch/build stability

Tasks:

- Ensure `./eng/build-windows.ps1 -Run` still builds service and Windows app.
- Ensure service starts before Windows app.
- Ensure existing provider settings still work.
- Ensure current context resolution still works for Notepad/file-backed context.

Acceptance criteria:

- `build-windows.ps1 -Run` completes on a clean local checkout.
- Service listens on the configured localhost endpoint.
- Sidecar starts and can resolve at least one file-backed Notepad context.

### B13-P0-003: Clarify app mode labels in the UI

Tasks:

- Show one visible mode strip with:
  - Active Work Thread
  - Following status
  - Locked status
  - Pending connection status
- Hide raw provider/debug details unless developer mode is enabled.

Acceptance criteria:

- User can tell whether ThreadlineAI is following, locked, pending connection, or detached.
- Normal UX does not look like a diagnostics console.

## P1: Work Thread Persistence Foundation

### B13-P1-001: Add WorkThread persistence model

Tasks:

- Add `WorkThread` entity/model.
- Add SQLite migration/schema update.
- Add repository/service methods:
  - CreateWorkThread
  - GetWorkThread
  - ListWorkThreads
  - RenameWorkThread
  - CloseWorkThread
  - UpdateLastResumed

Suggested fields:

- Id
- Title
- Description
- Status
- CreatedAt
- UpdatedAt
- ClosedAt
- LastResumedAt

Acceptance criteria:

- Work Threads can be created, listed, renamed, resumed, and closed through service methods.
- Existing app startup creates or selects a default Work Thread if none exists.

### B13-P1-002: Add Work Thread service API endpoints

Tasks:

- Add HTTP endpoints for Work Thread CRUD/resume operations.
- Add DTOs separate from persistence models.
- Add simple error responses.

Acceptance criteria:

- WinUI app can create/list/resume Work Threads using the local service.
- API failures show useful UI messages.

### B13-P1-003: Add Work Thread selector to sidecar

Tasks:

- Add visible Active Work Thread display.
- Add `Start Session` / `New Thread` action.
- Add `Resume Thread` action or selector.
- Add rename/close affordance where simple.

Acceptance criteria:

- User can see the current Work Thread.
- User can start a new Work Thread from the main sidecar surface.
- User can resume a previous Work Thread.

### B13-P1-004: Persist ConversationMessage records

Tasks:

- Add `ConversationMessage` entity/model.
- Persist user prompts and assistant responses against active WorkThreadId.
- Load recent messages when resuming a Work Thread.

Suggested fields:

- Id
- WorkThreadId
- Role
- Content
- CreatedAt
- ContextReceiptId

Acceptance criteria:

- Messages survive app restart.
- Resuming a Work Thread restores the conversation transcript or a recent transcript window.

## P2: Context Events and Context Receipts

### B13-P2-001: Add ContextEvent model and persistence

Tasks:

- Add `ContextEvent` entity/model.
- Store Follow, Lock, Manual, and Inferred context snapshots.
- Include app/window/title/url/summary/confidence.
- Associate ContextEvents with active WorkThreadId.

Suggested fields:

- Id
- WorkThreadId
- SourceType
- SourceName
- AppName
- WindowTitle
- Url
- ContentSummary
- CaptureMode
- Confidence
- CreatedAt

Acceptance criteria:

- Follow Mode creates ContextEvents.
- Lock Mode pins or creates a ContextEvent with CaptureMode `Locked`.
- ContextEvents can be listed for a Work Thread.

### B13-P2-002: Refactor Follow/Lock to write ContextEvents

Tasks:

- Identify current Follow/Lock code paths.
- Add service calls to persist context changes.
- Debounce high-frequency follow updates.
- Do not persist sensitive/excluded windows.

Acceptance criteria:

- Changing active apps in Follow Mode creates meaningful context timeline entries.
- Locking context creates a durable locked event.
- Follow events are not spammy.

### B13-P2-003: Add ContextReceipt model and generator

Tasks:

- Add `ContextReceipt` entity/model.
- Implement a service that assembles used and not-used sources from current Work Thread context.
- Store receipt as JSON plus limitations text.

Suggested fields:

- Id
- WorkThreadId
- UsedSourcesJson
- NotUsedSourcesJson
- Limitations
- CreatedAt

Acceptance criteria:

- ContextReceipt can be created for a response or artifact.
- Receipt includes sources, capture modes, timestamps, and limitations.

### B13-P2-004: Show Context Receipt in the UI

Tasks:

- Add collapsed `Context Receipt` section under assistant responses or in a side panel.
- Add Copy Receipt action.
- Add developer detail expansion only in debug mode.

Acceptance criteria:

- User can inspect what context informed a response.
- User can copy a Context Receipt.

## P3: One-Click Artifacts

### B13-P3-001: Add Artifact model and persistence

Tasks:

- Add `Artifact` entity/model.
- Add service methods to create/list/update artifacts.
- Associate artifacts with WorkThreadId and ContextReceiptId.

Suggested fields:

- Id
- WorkThreadId
- ArtifactType
- Title
- Content
- CreatedAt
- UpdatedAt
- ContextReceiptId

Acceptance criteria:

- Artifacts can be saved and retrieved for a Work Thread.
- Artifacts preserve their Context Receipt reference.

### B13-P3-002: Add one-click artifact action bar

Tasks:

- Add visible artifact actions to the sidecar:
  - Summary
  - Stakeholder Update
  - Handoff
  - Decision Log
  - Risk List
- Keep layout compact and usable on narrow sidecar width.

Acceptance criteria:

- User can generate the first five artifact types from the UI.
- Buttons do not require the user to write a prompt.

### B13-P3-003: Implement artifact prompt templates

Tasks:

- Add template definitions for each artifact type.
- Include context receipt instruction in each template.
- Use current Work Thread context, recent messages, decisions, risks, and next actions.

Acceptance criteria:

- Generated artifacts are structured, copyable, and relevant.
- Each artifact includes or references a Context Receipt.

### B13-P3-004: Implement Handoff Generator end-to-end

Tasks:

- Create Handoff artifact template with:
  - Background
  - Current status
  - What was reviewed
  - What changed
  - What was ruled out
  - Decisions made
  - Risks/blockers
  - Open questions
  - Recommended next step
  - Owner, if known
  - Urgency, if known
  - Context Receipt
- Save handoff artifact.
- Add Copy Handoff action.

Acceptance criteria:

- User can click `Create Handoff`.
- Handoff is clear enough for someone else to continue the work.
- Handoff is persisted and copyable.

## P4: Decisions, Risks, and Next Actions

### B13-P4-001: Add Decision model and service

Tasks:

- Add `Decision` entity/model.
- Add CRUD service methods and endpoints.
- Link decisions to WorkThreadId and SourceContextId.

Suggested fields:

- Id
- WorkThreadId
- DecisionText
- Rationale
- SourceContextId
- CreatedAt
- UpdatedAt

Acceptance criteria:

- User can create/list/edit/delete decisions.
- Decision Log artifact can read saved decisions.

### B13-P4-002: Add Risk model and service

Tasks:

- Add `Risk` entity/model.
- Add CRUD service methods and endpoints.

Suggested fields:

- Id
- WorkThreadId
- RiskText
- Severity
- Mitigation
- Owner
- Status
- CreatedAt
- UpdatedAt

Acceptance criteria:

- User can save risks.
- Risk List artifact can read saved risks.

### B13-P4-003: Add NextAction model and service

Tasks:

- Add `NextAction` entity/model.
- Add CRUD service methods and endpoints.

Suggested fields:

- Id
- WorkThreadId
- ActionText
- Owner
- DueDate
- Status
- CreatedAt
- UpdatedAt

Acceptance criteria:

- User can save next actions.
- Resume artifact can include open next actions.

### B13-P4-004: Add mark-as-decision UI action

Tasks:

- Add a way to mark selected text or assistant output as a decision.
- Add edit prompt for rationale if needed.
- Link decision to active context receipt or ContextEvent.

Acceptance criteria:

- User can manually capture a decision from sidecar content.
- Decision appears in Decision Log.

## P5: Resume and Gap Analysis

### B13-P5-001: Implement Resume My Work

Tasks:

- Add Resume action on Work Thread selector and artifact bar.
- Use WorkThread summary, recent ContextEvents, messages, decisions, risks, and next actions.
- Generate concise resume summary.

Acceptance criteria:

- User can select a Work Thread and click `Resume`.
- Output includes status, open questions, risks, and next recommended action.
- Output includes Context Receipt.

### B13-P5-002: Implement What Am I Missing?

Tasks:

- Add artifact/action template for gap analysis.
- Analyze missing owner, acceptance criteria, metric definition, source system, deadline, unclear decision, unmitigated risk, missing next action, missing reproduction steps, weak handoff detail.
- Optionally save gaps as risks or next actions.

Acceptance criteria:

- User can click `What Am I Missing?`.
- Output includes gap, why it matters, and recommended next step.

## P6: Privacy, Retention, and Trust

### B13-P6-001: Add PrivacyExclusion model

Tasks:

- Add `PrivacyExclusion` entity/model.
- Add service APIs for app/url/window/process exclusions.
- Apply exclusions before ContextEvents are persisted.

Suggested fields:

- Id
- ExclusionType
- Pattern
- IsEnabled
- CreatedAt
- UpdatedAt

Acceptance criteria:

- User can exclude apps, URLs, windows, or processes.
- Excluded contexts are not persisted or sent to model context.

### B13-P6-002: Add visible clear/pause controls

Tasks:

- Add Clear Current Context.
- Add Pause Capture.
- Add Resume Capture.
- Make state visually obvious.

Acceptance criteria:

- User can stop capture quickly.
- User can clear current context without deleting the Work Thread.

### B13-P6-003: Add retention setting

Tasks:

- Add basic retention policy setting.
- Implement cleanup for closed Work Threads or old ContextEvents.
- Keep local-first behavior default.

Acceptance criteria:

- User can configure retention period.
- Cleanup does not remove active Work Thread content accidentally.

## P7: Shareable Handoff Foundation

Multi-user collaboration is not in scope for Build 13.0, but single-user export is in scope as the foundation.

### B13-P7-001: Export Work Thread handoff bundle

Tasks:

- Add export command for a Work Thread.
- Include summary, handoff artifact, decisions, risks, next actions, and context receipt.
- Exclude sensitive raw context by default.

Acceptance criteria:

- User can export a Markdown handoff bundle.
- Export is safe to share after review.

### B13-P7-002: Copy full conversation and artifacts

Tasks:

- Ensure Copy All includes conversation, active Work Thread title, context receipt summary, and generated artifacts where selected.
- Add copy artifact action per artifact.

Acceptance criteria:

- User can copy enough information to show how ThreadlineAI was working.
- Copied output is readable outside the app.

## P8: UI Hardening and Demo Readiness

### B13-P8-001: Improve small-window sidecar usability

Tasks:

- Ensure sidecar has minimum usable height.
- Ensure transcript and settings/artifacts scroll correctly.
- Ensure action buttons remain reachable.

Acceptance criteria:

- User can use ThreadlineAI even when target window is small.
- Ask, Copy, Clear, Start Session, and artifact buttons remain reachable.

### B13-P8-002: Add debug mode toggle

Tasks:

- Move raw provider/source/confidence/window diagnostics behind debug mode.
- Keep normal UX concise.

Acceptance criteria:

- Normal users see work context and controls, not raw technical details.
- Developers can still troubleshoot provider and window detection issues.

### B13-P8-003: Add demo script

Tasks:

- Create a short demo script for analytics/data engineering use case.
- Include a ticket/dashboard/text file/code/browser workflow.
- Demonstrate Follow, Lock, Work Thread, Context Receipt, Handoff, What Am I Missing, Resume.

Acceptance criteria:

- Demo can be run repeatedly without ad hoc explanation.
- Demo proves commercial wedge.

## Implementation Order Summary

1. Keep build/run stable.
2. Add WorkThread model/API/UI selector.
3. Persist chat messages.
4. Persist ContextEvents from Follow/Lock.
5. Add ContextReceipt model/generator/UI.
6. Add Artifact model and one-click artifact bar.
7. Implement Handoff end-to-end first.
8. Add Summary, Stakeholder Update, Decision Log, Risk List.
9. Add Decision/Risk/NextAction structured storage.
10. Add Resume My Work and What Am I Missing.
11. Add privacy exclusions and retention.
12. Add export/shareable handoff bundle.
13. Harden UI and create demo script.

## Build 13 Definition of Done

Build 13 is successful when ThreadlineAI can:

- Follow visible work context.
- Lock onto a chosen context.
- Create and resume Work Threads.
- Persist context events and conversation messages.
- Produce a Context Receipt for model output.
- Generate at least one high-quality handoff artifact.
- Save artifacts to the Work Thread.
- Capture at least basic decisions, risks, and next actions.
- Resume prior work with a useful summary.
- Show privacy/capture state clearly.
- Provide a demo that feels like a work companion, not a generic chatbot.
