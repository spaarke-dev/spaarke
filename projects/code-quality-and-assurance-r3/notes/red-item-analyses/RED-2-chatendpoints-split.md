# RED-2 — Split `ChatEndpoints` (4,066 LOC, 18 routes, actively growing)

> **Type**: remediation-project seed · **Origin**: r3 post-program review (2026-08-15)
> **Surface**: BFF (`Sprk.Bff.Api`) · **Effort**: M · **Value**: Med (endpoint hygiene; stops the fastest-growing god-file)

## Summary

`src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` is **4,066 lines mapping 18 routes** in one file.
Endpoint files should be thin route groups that delegate to services; this one has absorbed handler logic
inline and is the **fastest-growing** BFF god-file (3,587 → 4,066 during r3 per the SCORECARD net10
refresh). It is the #2 `GodClassGuardTests` waiver.

## Evidence

- LOC 4,066; 18 `MapPost/MapGet/...` route registrations in a single static class.
- Growth trend: +479 lines across the r3 window alone (measured 2026-08-14 vs the task-040 baseline).

## Why it matters

1. A 4,000-line endpoint file means route registration, request validation, orchestration, and response
   shaping are interleaved — the "keep endpoints thin, delegate to services" rule (BFF CLAUDE.md) is
   violated at the worst offender.
2. Merge-contention magnet: every chat feature touches this one file → cross-worktree conflicts (the
   Compose/Communication/AI worktrees all touch adjacent chat surface).
3. It is actively worsening, so the cost compounds.

## Proposed approach

Split by route family into cohesive endpoint groups (each `MapGroup`-rooted), pushing inline handler
bodies into existing/new services:

| New endpoint file | Routes (approx) |
|---|---|
| `ChatSessionEndpoints` | create/switch/get session, host-context |
| `ChatMessageEndpoints` | send message (SSE), stream, cancel |
| `ChatPlaybookEndpoints` | playbook discovery/list (pre-session) |
| `ChatToolEndpoints` | tool/catalog-projection routes |

Inline orchestration → move to `Services/Ai/Chat/**` (much already exists: `ChatSessionManager`,
`SprkChatAgentFactory`, `PlaybookChatContextProvider`). The endpoints become thin.

## Risks & mitigations

- **Risk**: SSE streaming + cancellation semantics are subtle; a split could drop a header/flush. **Mitigation**:
  the contract tests (`tests/integration/contract/Api/Ai/**`) + a route-dump diff must be identical;
  don't touch the SSE write path, only relocate it.
- **Risk**: high merge contention with active AI worktrees. **Mitigation**: schedule into a quiet window;
  `/conflict-check` first; land as one atomic PR.

## Acceptance criteria

- No resulting endpoint file > 2,700 LOC (remove the waiver); route-dump identical; all chat contract +
  SSE tests green.

## Dependencies / coordination

Coordinate with the active AI/Compose worktrees (`Services/Ai/**`, `Api/Ai/**` contention). BFF-hot-path.
