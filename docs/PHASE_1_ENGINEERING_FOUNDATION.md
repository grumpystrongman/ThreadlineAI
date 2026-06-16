# Phase 1 — Engineering foundation

Phase 1 turns the scaffold into an engineering-ready repository.

## Completed in this phase

- Repository formatting rules through `.editorconfig`.
- Shared .NET project defaults through `Directory.Build.props`.
- SDK pinning through `global.json`.
- Git line-ending normalization through `.gitattributes`.
- Core unit test project.
- Infrastructure unit test project.
- CI validation workflow for .NET core/service/test projects and browser extension TypeScript.
- Local build and test scripts under `eng/`.
- Development, branching, contribution, pull request, issue, and security documentation.

## Explicitly deferred

- Full solution rewrite after local Visual Studio validation.
- Warnings-as-errors enforcement for every project.
- Windows app CI packaging.
- Code signing.
- Local API authentication.
- SQLite persistence.

## Exit criteria

Phase 1 is complete when CI can restore/build/test core projects and the browser extension, and a developer has a documented local setup path.
