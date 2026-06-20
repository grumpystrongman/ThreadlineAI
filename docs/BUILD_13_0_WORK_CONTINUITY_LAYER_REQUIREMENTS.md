# Build 13.0 Requirements: ThreadlineAI Work Continuity Layer

## Status

Proposed product direction for the next major build sequence.

This document repositions ThreadlineAI from a generic AI sidecar into a Windows-native work continuity layer. The existing application should not be rewritten. The current WinUI 3 sidecar, ASP.NET Core service, SQLite persistence, browser extension support, active-window content resolution, Follow/Lock behavior, and context provider/resolver layer remain the foundation.

Build 13.0 should focus on making ThreadlineAI commercially useful, demoable, and adoption-ready for analytics, BI, data engineering, and operations teams.

## 1. Product Vision

ThreadlineAI is an AI work companion that follows the user's active work across apps, remembers the thread of work, and helps turn scattered activity into summaries, handoffs, decision logs, risks, stakeholder updates, and next actions.

ThreadlineAI should not be positioned as another empty prompt box, generic chatbot, or Copilot clone. Its value is reducing the prompt tax by collecting, organizing, and transparently presenting work context while giving the user control over what is followed, locked, saved, and shared.

The product should answer these questions:

- What is the user working on?
- What context matters?
- What changed?
- What decision was made?
- What risk exists?
- What needs to happen next?
- How can this work be handed off?
- How can the user resume it later?
- What did the AI use to produce this answer?

If a feature does not support work continuity, context transparency, handoff quality, decision memory, trust, or adoption, defer it.

## 2. Commercial Wedge

The initial commercial wedge is:

**ThreadlineAI for Analytics, BI, Data Engineering, and Operations teams.**

The first target user works across tickets, dashboards, SQL, documents, browser tabs, emails, code, and meeting notes, then needs to produce updates, handoffs, decisions, and next actions.

Primary use cases:

- Analytics issue investigation
- Dashboard explanation and metric review
- SQL/data engineering debugging handoff
- Operational ticket triage and next-action tracking
- Stakeholder update drafting
- Decision/risk capture during fragmented work
- Resume previous work after interruption
- Shareable handoff for another analyst, engineer, manager, or offshore teammate

## 3. Core User Stories

### Follow and lock context

As a user, I want ThreadlineAI to visibly follow the app, tab, document, ticket, dashboard, or file I am working in so I do not have to manually paste context into a prompt.

As a user, I want to lock ThreadlineAI onto a specific context so it does not drift when I click other windows.

### Work threads

As a user, I want to create a Work Thread for an investigation, ticket, dashboard, or task so my context, prompts, answers, decisions, risks, and artifacts are stored together.

As a user, I want to resume a prior Work Thread and quickly understand where I left off.

### Context transparency

As a user, I want every response and artifact to show a Context Receipt so I can trust what the AI used and what it did not use.

### Artifacts and handoffs

As a user, I want one-click actions that turn my current work context into a useful artifact: summary, stakeholder update, handoff, decision log, risk list, next actions, ticket draft, or response draft.

As a user, I want a handoff clear enough for someone else to continue my work without requiring a long meeting.

### Decisions, risks, and next actions

As a user, I want decisions, risks, and next actions extracted, saved, and retrievable so the thread of work does not disappear in chat history.

### Privacy and control

As a user, I want to know exactly what ThreadlineAI is following or locked onto, and I want to pause, clear, exclude, or detach context at any time.

## 4. Core Feature Requirements

## 4.1 Follow Me Mode

ThreadlineAI must visibly follow the user's active work context.

Requirements:

- Detect the current active app/window.
- Detect browser tab title and URL where available.
- Detect document, file, title, or app metadata where available.
- Show a visible indicator: `Following: [current app/document/tab]`.
- Persist follow events into the active Work Thread.
- Allow pause/resume.
- Never silently capture context without visible indication.

Acceptance criteria:

- User can turn Follow Me Mode on and off.
- UI clearly displays what is currently followed.
- Changing active apps updates the visible followed context when Follow Mode is enabled.
- Follow events are stored as ContextEvents on the active Work Thread.
- Follow Mode does not override Lock Mode.

## 4.2 Lock Onto This

ThreadlineAI must allow the user to lock onto a specific context.

Requirements:

- User can lock the current context.
- When locked, ThreadlineAI keeps that context as the primary source even if the user changes windows.
- UI clearly distinguishes Follow Mode from Lock Mode.
- User can unlock or switch locked context.

Acceptance criteria:

- User can lock onto a ticket, dashboard, browser tab, document, email, code file, or other active context.
- Assistant uses the locked context as the primary context for answers and artifacts.
- UI shows `Locked onto: [context name]`.
- Unlock returns to Follow Mode only if Follow Mode is enabled.

## 4.3 Work Thread Memory

A Work Thread represents a workstream, issue, project, ticket, dashboard, investigation, or task.

Each Work Thread stores:

- Title
- Description/summary
- Status
- Source contexts used
- Timeline of context events
- User prompts
- Assistant responses
- Decisions
- Risks
- Open questions
- Next actions
- Generated artifacts
- Created/updated timestamps
- Last resumed timestamp

Acceptance criteria:

- User can create, resume, rename, and close a Work Thread.
- Follow/Lock context is associated with the active Work Thread.
- Chat messages are stored as part of the active Work Thread.
- Work Threads can be resumed later.
- A Work Thread can have multiple source contexts over time.

## 4.4 Context Receipt

Every assistant answer and generated artifact must include or expose a Context Receipt.

A Context Receipt shows:

- Context sources used
- App/window/tab/document names
- Whether each source was followed, locked, manually added, or inferred
- Timestamp of the context snapshot
- What was not used, when relevant
- Privacy limitation or missing context

Example:

```text
Context used:
- Azure DevOps ticket: ED Throughput Metric Issue
- Power BI window: 2026 KPI Dashboard
- Browser tab: Metric Definition Draft
- User note: Concerned about denominator logic

Not used:
- Email
- Teams
- Local files outside active context
- Any patient-level data
```

Acceptance criteria:

- Each assistant response can display its Context Receipt.
- User can inspect what context informed a response.
- Generated artifacts preserve a reference to context used.
- Context Receipt can be copied/exported.

## 4.5 One-Click Work Artifacts

ThreadlineAI must provide high-value artifact actions that do not require the user to invent prompts.

Initial artifact actions:

- Summarize Current Work
- Create Stakeholder Update
- Create Handoff Note
- Create Decision Log
- Create Risk List
- Create Next Actions
- Turn This Into a Ticket
- Draft Response
- Explain This Dashboard/Issue
- Resume This Tomorrow
- What Am I Missing?

Each artifact must:

- Use the active Work Thread context.
- Include a Context Receipt.
- Be editable.
- Be copyable.
- Be saved back to the Work Thread.

Acceptance criteria:

- User can generate at least Summary, Stakeholder Update, Handoff, Decision Log, and Risk List from the UI.
- Artifact output is structured and useful.
- Artifact is stored in SQLite and associated with the Work Thread.
- User can copy artifact text.

## 4.6 Handoff Generator

The Handoff Generator is a flagship feature.

It should produce:

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

Acceptance criteria:

- User can click `Create Handoff`.
- Handoff uses the current Work Thread and context receipt.
- Handoff is clear enough for another person to continue the work.
- Handoff is saved as an Artifact.
- Handoff can be copied for Slack, Teams, email, GitHub, or ticket comments.

## 4.7 Decision Memory

ThreadlineAI must capture and retrieve decisions.

Requirements:

- User can manually mark text as a decision.
- Assistant can suggest possible decisions from context.
- Decisions are structured records.
- Decisions include date/time, source context, rationale, and related Work Thread.
- User can ask: `What decisions were made on this thread?` or `Why did we decide that?`

Acceptance criteria:

- Decision records can be created, listed, edited, and deleted.
- Decision records are linked to source context and Work Thread.
- Decision Log artifact can be generated.

## 4.8 Resume My Work

ThreadlineAI must help the user resume prior work.

When a user opens ThreadlineAI or selects a Work Thread, it should summarize:

- What the user was working on
- What changed
- Last known status
- Open questions
- Risks
- Next recommended action

Example:

```text
Yesterday you were working on the ED throughput metric issue. You reviewed the ticket, compared it to the dashboard, and identified a possible denominator mismatch. The next step is to confirm whether cancelled visits should be excluded.
```

Acceptance criteria:

- User can select a prior Work Thread and click `Resume`.
- Resume output includes a concise summary, next actions, risks, and open questions.
- Resume output includes Context Receipt.

## 4.9 What Am I Missing?

ThreadlineAI must include a gap-analysis action.

It should detect gaps such as:

- No clear owner
- No acceptance criteria
- Missing metric definition
- Missing source system
- Missing deadline
- Unclear decision
- Open risk without mitigation
- Stakeholder update lacks next action
- Ticket lacks reproduction steps
- Handoff lacks enough detail

Acceptance criteria:

- User can click `What Am I Missing?`.
- Assistant returns a structured gap analysis.
- Each gap includes why it matters and a recommended next step.
- Gaps can be saved to the Work Thread.

## 4.10 Privacy and Control

Privacy must be visible and central.

Requirements:

- Visible Follow/Pause control.
- Visible Lock/Unlock control.
- Visible current context indicator.
- Ability to clear current context.
- Ability to exclude specific apps, URLs, or windows.
- Ability to disable capture for sensitive contexts.
- Local-first storage by default where possible.
- Configurable retention policy.
- No hidden background screen capture.
- No patient-level data capture by default.
- Future enterprise admin policy support.

Acceptance criteria:

- User always knows what ThreadlineAI is following or locked onto.
- User can pause, clear, or exclude context.
- Sensitive contexts can be excluded.
- Privacy behavior is documented clearly in the app and docs.

## 5. Technical Requirements

## 5.1 Preserve the current architecture

ThreadlineAI should continue to use:

- WinUI 3 desktop sidecar shell
- ASP.NET Core local service
- SQLite persistence
- Context provider/resolver layer
- Browser extension support where available
- Native active-window and UI Automation context capture
- Existing Follow/Lock concepts, refactored as needed

Do not rewrite the app. Build the work-continuity layer incrementally over the existing service/UI boundary.

## 5.2 Service boundary

The local service should own durable state:

- Work Threads
- ContextEvents
- ConversationMessages
- ContextReceipts
- Artifacts
- Decisions
- Risks
- NextActions
- Privacy/exclusion settings

The WinUI app should display, collect, and trigger operations, but persistence should live behind service APIs.

## 5.3 Context capture

Context capture should be explicit and auditable:

- Follow Mode creates ContextEvents.
- Lock Mode creates or pins a ContextEvent as primary context.
- Manual context additions create ContextEvents with `CaptureMode = Manual`.
- Inferred context creates ContextEvents with `CaptureMode = Inferred` and lower confidence.

ContextEvents should not imply full screen capture. They should store metadata, extracted summaries, selected text, and available resolved content according to provider capability and privacy settings.

## 5.4 Artifact generation

Artifact generation should use:

- Active Work Thread
- Relevant ContextEvents
- Conversation history
- Decisions/Risks/NextActions where available
- Explicit artifact prompt templates
- ContextReceipt generated at artifact creation time

## 5.5 Context Receipt generation

Every response and artifact creation flow should assemble a receipt from:

- Sources included in model prompt/context
- Sources excluded by privacy, sensitivity, availability, or confidence
- Active mode: Follow, Lock, Manual, Inferred
- Timestamp
- Known limitations

## 5.6 Local-first and sharing future path

Build 13.0 should remain local-first, but the data model must not block future sharing.

Future sharing should support:

- Exporting a Work Thread handoff bundle
- Sharing an artifact with context receipt
- Storing a resumable thread snapshot in a repo, shared drive, or enterprise service
- Coworker continuation without exposing sensitive local-only data by default

Multi-user collaboration is not a Build 13.0 implementation goal, but the data model should be share-ready.

## 6. Data Model Changes

Add or update persistence models.

### WorkThread

- Id
- Title
- Description
- Status
- CreatedAt
- UpdatedAt
- ClosedAt
- LastResumedAt

### ContextEvent

- Id
- WorkThreadId
- SourceType
- SourceName
- AppName
- WindowTitle
- Url
- ContentSummary
- CaptureMode: Followed, Locked, Manual, Inferred
- Confidence
- CreatedAt

### ConversationMessage

- Id
- WorkThreadId
- Role
- Content
- CreatedAt
- ContextReceiptId

### ContextReceipt

- Id
- WorkThreadId
- UsedSourcesJson
- NotUsedSourcesJson
- Limitations
- CreatedAt

### Artifact

- Id
- WorkThreadId
- ArtifactType
- Title
- Content
- CreatedAt
- UpdatedAt
- ContextReceiptId

### Decision

- Id
- WorkThreadId
- DecisionText
- Rationale
- SourceContextId
- CreatedAt
- UpdatedAt

### Risk

- Id
- WorkThreadId
- RiskText
- Severity
- Mitigation
- Owner
- Status
- CreatedAt
- UpdatedAt

### NextAction

- Id
- WorkThreadId
- ActionText
- Owner
- DueDate
- Status
- CreatedAt
- UpdatedAt

### PrivacyExclusion

- Id
- ExclusionType: App, Url, WindowTitle, ProcessName
- Pattern
- IsEnabled
- CreatedAt
- UpdatedAt

## 7. UX Requirements

The UI should emphasize:

- Active Work Thread
- Current followed context
- Current locked context
- Follow/Pause and Lock/Unlock
- Chat transcript
- One-click artifact actions
- Context Receipt
- Decisions
- Risks
- Next actions
- Resume button
- Privacy controls

The UI should feel like a work companion, not a diagnostics console.

Normal mode should hide low-level technical diagnostics. Developer/debug mode can expose provider names, confidence, capture status, window handles, raw source details, and resolver traces.

## 8. Implementation Phases

## Phase 1: Product foundation

- Update build/product docs.
- Define Work Thread model.
- Define Context Receipt model.
- Clarify Follow vs Lock behavior.
- Add visible UI indicators.
- Keep current sidecar stable.

## Phase 2: Work Thread persistence

- Persist Work Threads.
- Persist ContextEvents.
- Persist chat messages.
- Associate Follow/Lock context with active Work Thread.
- Add create/resume/rename/close APIs.

## Phase 3: Artifact generation

- Add one-click artifact buttons.
- Implement Summary, Stakeholder Update, Handoff, Decision Log, and Risk List first.
- Save artifacts to Work Thread.
- Include Context Receipt with each artifact.

## Phase 4: Resume and gap analysis

- Implement Resume My Work.
- Implement What Am I Missing?
- Add structured NextActions, Risks, and Decisions.

## Phase 5: Trust and privacy controls

- Add app/window/url exclusion.
- Add clear context.
- Add pause capture.
- Add retention setting.
- Add developer/debug mode separation.

## 9. Demo Acceptance Criteria

A compelling Build 13 demo should show:

1. User opens a ticket, dashboard, SQL file, browser tab, or text document.
2. ThreadlineAI visibly follows the context.
3. User locks onto a relevant context.
4. User starts or resumes a Work Thread.
5. User asks one question and receives an answer with Context Receipt.
6. User clicks `Create Handoff`.
7. ThreadlineAI generates a handoff with background, current status, decisions, risks, open questions, next step, and Context Receipt.
8. User clicks `What Am I Missing?`.
9. ThreadlineAI identifies gaps and recommended next steps.
10. User resumes the Work Thread later and gets a concise continuation summary.

## 10. Non-Goals For Build 13.0

Do not prioritize:

- Generic chatbot polish before Work Thread functionality
- Multi-user collaboration
- Enterprise admin dashboard
- Full Teams/Outlook/SharePoint integration
- Clinical decision support
- Patient-level healthcare workflows
- Agent automation that takes action without human approval
- Replacing Microsoft Copilot
- Replacing Jira, Azure DevOps, Power BI, or GitHub
- Perfect AI bubble behavior before Work Thread persistence and receipts

## 11. Product Guardrails

- Context must be visible.
- Capture must be controllable.
- Responses must be attributable to context.
- Artifacts must be useful without prompt engineering.
- Work must be resumable.
- Handoffs must be clear enough for another person to continue.
- Local-first should be the default until explicit sharing features are designed.
