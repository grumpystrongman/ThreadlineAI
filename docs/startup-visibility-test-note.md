# Startup visibility test plan

This branch is for validating that Threadline opens the main sidecar window first, then arms Shuttle tabs as an optional affordance.

Manual validation:

1. Run `./eng/smoke-shuttle-tabs.ps1`.
2. Run `./eng/build-windows.ps1 -Run`.
3. Confirm the main Threadline sidecar window appears every time.
4. Confirm blue `»` Shuttle tabs appear only after the sidecar is visible.
5. Confirm closing or losing Shuttle tabs does not hide the main sidecar.
