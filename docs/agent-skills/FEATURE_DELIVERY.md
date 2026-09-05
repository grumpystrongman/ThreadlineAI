# Agent Skill: Feature Delivery

Use this workflow for a new ThreadlineAI capability or meaningful enhancement.

## 1. Establish the contract

Identify the user outcome, current behavior, affected layer(s), privacy implications, and acceptance criteria. Inspect existing code/tests/docs before designing a new abstraction.

## 2. Trace the vertical slice

Map the feature through only the layers it truly needs, for example:

`UI -> local service -> core abstraction -> infrastructure/provider/storage -> persistence -> response/UI`

Do not force every feature through every layer.

## 3. Check invariants

Before coding, confirm the design preserves:

- local-first behavior where applicable;
- provider abstraction boundaries;
- visible/approved context use;
- context source/confidence honesty;
- consent/redaction rules;
- clean sidecar-first UX.

## 4. Implement incrementally

Prefer the smallest end-to-end path that proves the feature. Reuse existing services, models, registries, storage, and `eng/` scripts before adding parallel mechanisms.

## 5. Add evidence

Add or update tests that prove the intended behavior and meaningful failure paths. For context/provider/privacy work, include negative cases such as missing consent, unavailable provider, redaction, or degraded context.

## 6. Validate

Run the relevant repository build/test/smoke paths described in `AGENTS.md`. For UI work, inspect the actual interaction or screenshot when possible rather than relying on XAML compilation alone.

## 7. Update product truth

If user-visible behavior, architecture, setup, support status, privacy behavior, or limitations changed, update the relevant docs. Do not create a new historical build document unless the change genuinely needs one.

## Completion report

Report:

- outcome delivered;
- files/layers changed;
- important design choice(s);
- verification performed and results;
- known limitations or product decisions still open.
