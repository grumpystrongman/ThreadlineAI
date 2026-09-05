# Agent Skill: UX / UI Change

Use this workflow for changes to the WinUI sidecar, drawers, settings, context display, transcript, onboarding, or interaction model.

## 1. Start from the user task

Describe what the user is trying to accomplish and what currently creates friction. Do not begin from controls or styling.

## 2. Preserve the hierarchy

The primary surface should remain a familiar, low-friction conversation beside the user's work. Context status should be understandable without dominating the experience. Advanced controls, diagnostics, and configuration should remain secondary unless the task specifically requires them.

## 3. Respect workspace geometry

ThreadlineAI is a sidecar, not an overlay-first app. Changes should preserve the user's primary work surface and avoid surprising window movement, focus theft, or obscured content.

## 4. Make context legible

When context matters to the interaction, expose the source, confidence/quality, relevant limitations, and meaningful error state. Never design UI that suggests richer access than the resolver actually provides.

## 5. Design failure states

Account for unavailable service/provider, missing permissions/consent, weak context, unavailable hotkey, and recoverable errors. Keep messages actionable and avoid leaking sensitive implementation details.

## 6. Implement with existing patterns

Reuse existing WinUI components, view models, commands, drawers, spacing, and established colors/icons unless the product owner is intentionally changing the design language.

## 7. Verify visually and behaviorally

Compilation is insufficient. When tooling/environment allows, inspect the running UI or screenshots and verify:

- layout at expected sidecar widths;
- transcript scrolling and composer behavior;
- focus/keyboard behavior;
- drawer/secondary-surface behavior;
- context/error states;
- attached/follow/lock/park geometry affected by the change.

Document any visual verification that could not be performed.
