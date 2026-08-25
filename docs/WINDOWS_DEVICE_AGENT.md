# AIKA / JARVIS Windows Device Agent

The Windows Device Agent gives AIKA / JARVIS an owner-authorized way to operate the signed-in Windows desktop while keeping Threadline's context, privacy, audit, and native Windows integration underneath the assistant.

This is intentionally **not** a generic click bot. The design incorporates the lessons from OpenLAMb: a successful API call or mouse click is not evidence that the requested task succeeded.

## Control hierarchy

Jarvis should choose the strongest available capability in this order:

1. **App-specific/native API** — COM, CLI, browser protocol/extension, application SDK/API.
2. **Windows UI Automation** — inspect the live control tree, then use exact AutomationId/name and native Invoke/Value patterns.
3. **Accessibility observation** — inspect current window/process/accessibility state.
4. **Screenshot + Windows OCR** — fallback evidence for custom, canvas, Electron, or poorly exposed UI.
5. **Keyboard/mouse input** — last-resort interaction, followed by a fresh observation and explicit postcondition whenever possible.

Coordinate clicks are never the preferred mechanism.

## Closed-loop execution

A device operation follows this loop:

```text
Observe
  -> select capability
  -> inspect target/selectors when needed
  -> act
  -> observe again
  -> verify expected state
       -> satisfied: continue
       -> not satisfied: report partial/failure, diagnose, re-plan, try another capability
```

`DeviceExecutionResult` distinguishes `Completed`, `Partial`, `Blocked`, and `Failed`. Mutating callers should provide a meaningful `DeviceExpectedState` whenever the resulting state can be observed.

## Interactive desktop boundary

Windows UI Automation, screenshots, foreground-window control, and `SendInput` must run in the signed-in interactive desktop session. They do **not** belong in the ASP.NET service or a Windows service running in session 0.

`Threadline.Windows` hosts `WindowsDeviceAgentPipeHost` on the current-user-only named pipe:

```text
Threadline.DeviceAgent.v1
```

The pipe host owns the real device operations. It uses `PipeOptions.CurrentUserOnly`, so an unrelated Windows account cannot connect to the device control surface.

## Jarvis bridge

`Threadline.DeviceMcp` is a small stdio Model Context Protocol server. PersonalJarvis starts it from the Threadline-owned MCP configuration and exposes its tools to chat/voice/Missions.

Current MCP tools:

- `observe_desktop`
- `inspect_controls`
- `capture_window`
- `list_device_capabilities`
- `get_owner_authority`
- `open_application`
- `focus_window`
- `invoke_control`
- `set_control_text`
- `send_keys`
- `click_at`

The Threadline bootstrap publishes the MCP executable and registers it in:

```text
%LOCALAPPDATA%\ThreadlineAI\jarvis\mcp.json
```

Threadline launches PersonalJarvis with `JARVIS_MCP_CONFIG` pointing to that exact file so tool registration does not depend on Jarvis's working directory or config-discovery order.

## Owner authority

`DeviceAuthorityGrant.OwnerDefault` represents the owner's standing authorization for ordinary delegated computer work. It currently pre-authorizes:

- application launch/focus
- native UI interaction
- keyboard and mouse fallback
- routine and consequential device work
- shell/file/app-specific capability categories as they are added

Protected operations remain a separate category and require explicit confirmation. This is where future administrator/elevated operations belong.

Owner authority is not a UAC bypass and does not defeat Windows integrity boundaries.

## Elevated operations

A normal user process cannot reliably automate every elevated/admin application or the Windows Secure Desktop. The eventual solution is a separately installed **privileged broker service** with a narrow authenticated RPC contract.

The broker should expose explicit administrative capabilities (for example service control or an approved installer operation), not an arbitrary "run anything as SYSTEM" endpoint. Protected requests should be separately classified, audited, and confirmed according to owner policy.

## Untrusted UI content

Text read from an application, accessibility tree, browser page, screenshot, OCR result, document, terminal, or third-party UI is **data**, not an instruction to the agent. Device observations include this reminder in metadata and Jarvis's MCP instructions repeat the rule.

This protects the computer-use path against prompt injection embedded in webpages/documents/UI content.

## OpenLAMb lessons carried forward

The new design deliberately avoids several failure modes observed in OpenLAMb:

- do not pick an arbitrary first desktop window when the requested target cannot be found
- do not treat `click()` returning as task success
- do not type into whichever control happens to own focus when a target is ambiguous
- do not make OCR's first text match the primary selector strategy
- do not rely on hard-coded command regexes as the planner
- do not collapse Window, Browser Tab, Document, terminal, and app-specific objects into one generic target type

Useful OpenLAMb concepts to retain are checkpoints, world state, learned skills/playbooks, governance, audit, and honest partial/blocked results.

## Validation

The branch-scoped Windows CI gate validates:

- Threadline.Service build
- Threadline.DeviceMcp build
- MCP initialize handshake
- MCP device-tool discovery
- Threadline.Windows WinUI build using Visual Studio MSBuild
- Threadline.Core tests, including owner-authority/capability-selection tests
- Threadline.Service tests

Hosted CI cannot prove interactive desktop behavior. Before leaving draft, the integration still needs a real signed-in Windows smoke suite that starts AIKA/JARVIS, opens known applications, inspects controls, performs harmless edits, and verifies resulting state.

## Next reliability layers

1. Add an installed-runtime desktop smoke harness (Notepad first, then browser and Office).
2. Add app-specific agents for Excel, Word, PowerPoint, browser, VS Code, and terminal using native APIs before UIA.
3. Add Mission-level retry/re-plan policy using the before/after observations and verification result.
4. Persist owner authority preferences and show them in AIKA/JARVIS settings.
5. Add a narrowly scoped privileged broker for approved elevated operations.
6. Feed learned successful selectors/workflows into Threadline memory with app/version fingerprints and expiration/revalidation rules.
