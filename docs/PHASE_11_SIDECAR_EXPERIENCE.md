# Phase 11 — Threadline Sidecar Experience

Phase 10.9 established the active-window content resolver and proved the provider ladder:

- browser/provider-backed capture works
- file-backed text resolution works for saved Notepad tabs
- generic native UI is useful for diagnostics but unsafe as the final answer for tabbed editors

Phase 11 moves Threadline toward the product experience: a persistent right-side AI sidecar that follows the user across Windows applications, browser tabs, and documents.

## Goal

Threadline should feel like a companion panel, not an engineering dashboard.

The user should be able to:

- keep one conversation alive
- switch between apps/windows/tabs
- see the current target clearly
- ask about the current target or session context
- clear local context
- start a new chat
- approve proposed actions
- keep diagnostics available but secondary

## Phase 11.0 delivered

- The Windows companion UI was reshaped into a compact sidecar shell.
- Conversation and ask box are now central.
- Current thread and current target are shown as the primary context card.
- Open app/tab selection remains available but is no longer the whole product.
- Timeline is demoted to a small activity strip.
- The companion attempts to open as a narrow right-side window.

## Next steps

- Auto-refresh current target while the sidecar is open.
- Add a visible context status pill: Browser, File-backed, Native, Needs provider, or Screenshot required.
- Add a compact diagnostics flyout instead of dumping diagnostics into chat.
- Add an approved screenshot/vision fallback for apps that do not expose structured content.
- Add hover/slide behavior once the sidecar shell is stable.
