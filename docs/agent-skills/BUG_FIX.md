# Agent Skill: Bug Fix

Use this workflow for regressions, incorrect behavior, crashes, broken interactions, or failing tests.

## 1. Reproduce from evidence

Start with the reported symptom, logs, failing test, source behavior, or relevant code path. Do not guess at the fix before locating the failure boundary.

## 2. Find the narrowest cause

Trace the behavior across the involved layers and distinguish root cause from secondary symptoms. Check recent related code and existing tests before adding workarounds.

## 3. Protect product invariants

A fix must not quietly weaken privacy controls, context honesty, provider abstraction, local-first behavior, or the primary sidecar workflow.

## 4. Fix the cause

Prefer a targeted correction over broad refactoring. If a larger structural change is actually required, explain why the narrow fix is unsafe or insufficient.

## 5. Add a regression test

When practical, add a test that fails before the fix and passes after it. Include the failure mode, not just the happy path.

## 6. Verify the affected surface

Run relevant builds/tests/smokes from `AGENTS.md`. For UI bugs, verify actual state/interaction when possible. For service/provider bugs, verify error behavior and audit/diagnostic boundaries as well as success.

## 7. Report confidence honestly

State the root cause, fix, regression coverage, verification run, and anything that could not be validated in the current environment.
