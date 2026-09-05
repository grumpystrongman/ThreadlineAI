# ThreadlineAI AI Operating Playbook

This playbook explains how Jeff and AI coding/research agents should collaborate on ThreadlineAI. `AGENTS.md` is the enforceable repo-level contract; this document explains the operating model behind it.

## Goal

The goal is to make AI collaboration cumulative instead of episodic. Each session should start with the repository's existing product intent, architecture, privacy boundaries, implementation state, and verification standards rather than from a blank prompt.

## The operating loop

### 1. Orient

Before changing anything, inspect the relevant implementation and source-of-truth documentation. Do not assume an older roadmap note is current when the code or newer release documentation says otherwise.

Useful orientation sources include:

- `README.md` for current product status and supported capabilities;
- `docs/ARCHITECTURE.md` for structural boundaries;
- privacy/security documentation for data-handling constraints;
- service/API documentation for contracts;
- recent build/release documents for current implementation decisions;
- tests and `eng/` scripts for executable expectations.

### 2. Plan

For non-trivial work, state a short plan before implementation. The plan should identify:

- intended user/product outcome;
- code and documentation areas likely to change;
- architecture/privacy risks;
- how completion will be objectively verified.

Planning is not permission theater. If the work is reversible and requirements are sufficiently clear, proceed after surfacing the plan. Escalate only consequential choices.

### 3. Implement

Prefer a small coherent vertical slice over broad speculative refactoring. Respect existing abstractions and reuse repository scripts/utilities before adding new machinery.

Do not push diagnostic chores to Jeff when repository access, tests, logs, source inspection, or connected tools can answer the question directly.

### 4. Verify

Verification is part of implementation, not a follow-up wish. Match verification to the affected layer:

| Change | Minimum verification |
| --- | --- |
| Core/domain | Build + relevant unit tests |
| Provider/storage | Build + unit/integration tests + security/privacy checks |
| Service/API | Build + service tests + relevant smoke tests |
| WinUI sidecar | Windows build when available + relevant tests + visual/interaction inspection |
| Browser extension | Extension build + bridge behavior check |
| Installer/service lifecycle | Packaging/install scripts or release validation as applicable |
| Privacy/context capture | Tests + consent/redaction review + diagnostics behavior |
| Release-sensitive change | `eng/release-validate.ps1` or documented supported subset |

Never claim a check passed if the environment could not run it. Record the limitation explicitly.

### 5. Report

A useful completion report answers four questions:

1. What changed?
2. Why is this the right change?
3. What evidence says it works?
4. What remains uncertain or needs Jeff's product decision?

### 6. Learn

If a task reveals a durable rule, anti-pattern, architectural boundary, or repeatable sequence, capture it. Update `AGENTS.md` only for cross-cutting rules. Add or refine an agent skill for repeatable procedures. Update normal product/architecture docs when the product itself changed.

## Decision ownership

### Agents may decide autonomously

- local implementation details that preserve documented architecture;
- naming/refactoring required to complete a scoped change;
- which existing tests/scripts to run;
- reversible UI details consistent with established design principles;
- documentation updates that accurately reflect implemented behavior.

### Surface to Jeff

- meaningful product behavior changes;
- new privacy or data-retention behavior;
- new external/cloud dependencies;
- changes to provider strategy or abstraction boundaries;
- destructive migrations or breaking API changes;
- large architectural rewrites;
- commercial/licensing/release policy changes;
- UX choices that materially alter the primary workflow.

## Context hierarchy

When sources conflict, use this hierarchy and surface material contradictions:

1. Jeff's explicit current instruction.
2. Current executable behavior and tests.
3. `AGENTS.md` architectural/product invariants.
4. Current product/architecture/privacy/service documentation.
5. Recent build/release documentation.
6. Older backlog or historical build notes.
7. Agent memory or assumptions.

Historical docs are evidence of why something happened, not automatically current requirements.

## Product principles that should survive feature churn

- Follow the work, not just the prompt.
- Use only approved context.
- Be explicit about context source and confidence.
- Never pretend to see more than the system actually sees.
- Keep privacy controls visible and understandable.
- Keep the primary sidecar conversational and low-friction.
- Turn conversations into durable work artifacts when useful.
- Prefer local control and auditable behavior over invisible automation.
- Ship with objective readiness evidence, not optimism.

## What Jeff should have to remember

Very little. For normal project work, Jeff should be able to say things like:

- "Fix this bug."
- "Build the next part."
- "Make this UX better."
- "Review this architecture."
- "Get this ready to release."

The agent should use the repository contract and relevant skill to determine the standard operating procedure automatically.

## Maintaining this system

Keep the operating system lightweight. Do not turn it into a second bureaucracy.

Add a rule only when it is durable and broadly applicable. Add a skill when a multi-step workflow repeats. Delete or revise instructions that become false. Verification commands should point to real repository scripts rather than duplicated shell recipes whenever possible.
