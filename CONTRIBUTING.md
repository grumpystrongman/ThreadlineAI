# Contributing to ThreadlineAI

ThreadlineAI is early-stage and security-sensitive. Treat every capture, storage, and provider change as a privacy-impacting change.

## Local setup

1. Install .NET 8 SDK.
2. Install Node.js 20+.
3. Clone the repo.
4. Run `./eng/build.ps1`.
5. Run `./eng/test.ps1`.

## Pull request checklist

Before opening a pull request, confirm:

- [ ] The change has a clear product purpose.
- [ ] Tests were added or updated.
- [ ] Capture behavior is visible and controllable.
- [ ] Sensitive data is not logged or sent silently.
- [ ] Documentation was updated when needed.

## Code style

The repo uses `.editorconfig` and .NET analyzers. Keep code straightforward and testable. Avoid clever abstractions unless they clearly reduce risk or complexity.
