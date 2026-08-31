# Design — ChatEndpoints Split (R1)

> **Status**: INITIALIZED (design only) · **Surface**: BFF · **Origin**: r3 RED-2 seed

## Hot-Path Declaration (CLAUDE.md §10)

```xml
<hot-path-declaration>
  <bff>Y</bff>                <!-- splits Api/Ai/ChatEndpoints.cs + relocates into Services/Ai/Chat/** -->
  <spaarke-ai>N</spaarke-ai>  <!-- server-side endpoints; no src/solutions/SpaarkeAi edit -->
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

## Problem

`src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` is **4,066 LOC mapping 18 routes** in a single static
class, with orchestration/validation/response-shaping interleaved inline. It violates the BFF "keep
endpoints thin, delegate to services" rule at the worst offender, grew **+479 lines during r3** (actively
worsening), and is a cross-worktree merge-contention magnet.

## Goals

1. Split into cohesive route-group endpoint files; push inline handler bodies into `Services/Ai/Chat/**`
   (much already exists: `ChatSessionManager`, `SprkChatAgentFactory`, `PlaybookChatContextProvider`).
2. Endpoints become thin route registrations.
3. Drop below the 2,000-line ceiling; **remove the `ChatEndpoints.cs` waiver**.

## Non-goals

- No route contract change; no SSE behavior change; no new capability/package.

## Approach

Split by route family (each `MapGroup`-rooted):

| New endpoint file | Routes (approx) |
|---|---|
| `ChatSessionEndpoints` | create / switch / get session, host-context |
| `ChatMessageEndpoints` | send message (SSE), stream, cancel |
| `ChatPlaybookEndpoints` | playbook discovery / list (pre-session) |
| `ChatToolEndpoints` | tool / catalog-projection routes |

Relocate inline orchestration into `Services/Ai/Chat/**`. **Do not touch the SSE write/flush path** — only
relocate it verbatim.

## Placement Justification (CLAUDE.md §11)

New files are **route-group extractions of existing endpoints**, not new capability — net-negative on
complexity. No new endpoint *route*, service capability, or package. Handler logic moves into services that
mostly already exist.

## Risks & mitigations

| Risk | Mitigation |
|---|---|
| SSE streaming / cancellation is subtle — a split could drop a header/flush | Contract tests (`tests/integration/contract/Api/Ai/**`) + route-dump diff must be identical; relocate the SSE path verbatim, don't rewrite |
| **High merge-contention** with ~8 active AI/Compose worktrees | Schedule into a quiet window; `/conflict-check` first; land as ONE atomic PR; coordinate with the compose/assistant worktrees |
| God-class ratchet trips mid-refactor | Remove the waiver as `ChatEndpoints.cs` drops below 2,000 |

## Acceptance criteria

- No resulting endpoint file > 2,000 LOC; `ChatEndpoints.cs` waiver removed from `GodClassGuardTests`.
- Route-dump identical; all chat contract + SSE tests green; `dotnet build -c Release` 0/0; ArchTests 38/0.

## Dependencies / coordination

**High cross-worktree contention** — sequence AFTER RED-1 and into a quiet window; coordinate with the active
`spaarkeai-compose-*` + `spaarkeai-assistant-*` worktrees (they edit adjacent `Api/Ai` + `Services/Ai`).
INITIALIZE-ONLY; worktree + tasks created at execution start.
