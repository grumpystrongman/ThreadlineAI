# Agent Skill: Release Readiness

Use this workflow when preparing a build, package, installer, release candidate, or confidence assessment.

## 1. Define the release boundary

Identify the target build/version, included changes, expected supported platforms, and any intentionally deferred limitations.

## 2. Inspect executable gates

Use the repository's existing build, test, smoke, packaging, signing, service-lifecycle, diagnostics, and release-validation scripts. Do not replace established gates with ad hoc commands unless a gate itself is being repaired.

## 3. Validate risk areas

Pay special attention to:

- local service startup/lifecycle;
- SQLite persistence/migrations;
- provider configuration and secret handling;
- context consent/redaction boundaries;
- sidecar launch/geometry/hotkey behavior;
- browser/PowerShell adapters;
- diagnostics export and data clearing;
- MSI/package staging and signing expectations.

## 4. Run release validation

Prefer:

```powershell
./eng/release-validate.ps1
```

On a supported non-Windows validation host:

```powershell
./eng/release-validate.ps1 -SkipWindows
```

A skipped Windows gate is a limitation, not a pass.

## 5. Reconcile docs with reality

Check README/product status, known limitations, setup instructions, screenshots/capture status, and release notes against what was actually validated.

## 6. Produce a release decision

Report one of:

- **Ready** — required gates passed and no known blocker remains.
- **Conditionally ready** — explicitly named validation or operational condition remains.
- **Not ready** — blocker(s) remain.

List evidence, failed/skipped gates, known limitations, and the exact next action for each blocker. Do not downgrade failures into vague warnings.
