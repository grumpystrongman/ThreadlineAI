# Branching and release workflow

## Branches

- `main` is the protected production-development branch.
- Feature work should happen on `feature/<short-name>` branches once the repo leaves scaffold mode.
- Release hardening should happen on `release/<version>` branches when packaging begins.

## Pull request expectations

Every non-trivial pull request should include:

1. A short product summary.
2. Security/privacy impact.
3. Test evidence.
4. Screenshots or logs when UI/adapter behavior changes.
5. Documentation updates when behavior changes.

## Version ladder

- `v0.0.x` scaffold and foundation.
- `v0.1.x` local alpha.
- `v0.2.x` browser alpha.
- `v0.3.x` terminal alpha.
- `v0.8.x` installer beta.
- `v1.0.x` production release.
