# ThreadlineAI + PersonalJarvis runtime bridge

This integration keeps ThreadlineAI as the Windows-native context shell while using PersonalJarvis as an optional local agent runtime for Missions, worker/critic execution, computer-use, and future tool routing.

## Why this shape

Threadline already owns Windows attachment, approved context capture, provider setup, Work Threads, artifacts, privacy rules, and the native sidecar. PersonalJarvis already owns a mature mission subsystem with isolated workers, critic review, destructive-action confirmation, tool approvals, and computer-use. The bridge joins those systems without replacing either one.

The first seam is deliberately narrow:

```text
Threadline.Windows
      |
      v
Threadline.Service  -- local token / origin protection
      |
      +--> /v1/jarvis/*
                |
                v
      PersonalJarvis server
      http://127.0.0.1:47821
                |
                +--> Router / Missions / Workers / Critic / Computer Use
```

Threadline does not silently bypass PersonalJarvis safety gates. A destructive mission can return HTTP 409 and require the caller to resubmit with `confirmed: true`. Mission-level tool approvals are also proxied explicitly so the Threadline UI can present approve/deny controls.

## Upstream

- Repository: https://github.com/PersonalJarvis/PersonalJarvis
- License: MIT
- Pinned integration commit: `b85535a50a3cece8cd269325b6b3ea2cb900e43f`

The bootstrap script clones the upstream repository directly rather than copying its full source into Threadline. This makes the upstream license boundary clear and allows us to advance the pin intentionally after compatibility testing.

## Install the runtime

On Windows PowerShell:

```powershell
./eng/bootstrap-personal-jarvis.ps1 -Start
```

This clones the pinned upstream revision under:

```text
%LOCALAPPDATA%\ThreadlineAI\runtimes\PersonalJarvis
```

It creates an isolated Python virtual environment and installs the `full` PersonalJarvis extra. Use `-Headless` if you intentionally want the smaller server-only dependency set.

## Threadline configuration

Defaults are intentionally useful for a local install:

```text
Threadline:Jarvis:Enabled = true
Threadline:Jarvis:BaseAddress = http://127.0.0.1:47821/
Threadline:Jarvis:RequestTimeoutSeconds = 30
```

Environment-variable equivalents:

```powershell
$env:Threadline__Jarvis__Enabled = 'true'
$env:Threadline__Jarvis__BaseAddress = 'http://127.0.0.1:47821/'
$env:Threadline__Jarvis__RequestTimeoutSeconds = '30'
```

## Bridge endpoints

All endpoints live under Threadline's existing local-access guard.

```text
GET  /v1/jarvis/status
POST /v1/jarvis/missions
GET  /v1/jarvis/missions/{missionId}
GET  /v1/jarvis/missions/{missionId}/result
GET  /v1/jarvis/missions/{missionId}/changes
POST /v1/jarvis/missions/{missionId}/cancel
GET  /v1/jarvis/missions/{missionId}/tool-approvals
POST /v1/jarvis/missions/{missionId}/tool-approvals/{traceId}/approve
POST /v1/jarvis/missions/{missionId}/tool-approvals/{traceId}/deny
```

Dispatch example:

```json
{
  "prompt": "Research the current project and produce a tested implementation plan.",
  "language": "en",
  "confirmed": false
}
```

## Next integration stages

1. Add a Threadline agent router that decides between direct Ask and a Jarvis Mission.
2. Surface Mission progress, critic verdicts, artifacts, and approvals in the WinUI transcript.
3. Register Jarvis Missions and computer-use as Threadline capabilities/actions.
4. Add PersonalJarvis lifecycle management to Threadline Doctor and the Windows service startup path.
5. Connect MyAika as an optional companion/personality/memory provider behind the same capability interface.
6. Add local creative-model and ComfyUI adapters as separate capabilities so fiction and image workflows do not depend on one hosted provider.
7. Add scheduler/trigger routing for recurring personal automations.

The intended long-term product shape is one assistant surface with separate, replaceable subsystems: Threadline for native Windows context, PersonalJarvis for agentic execution, and AIKA for companion/personality workflows where desired.
