# Task 055 evidence — UpdateWorkspaceTabHandler

**Pillar / Spec ref**: R6 Pillar 6b / D-C-06 — chat-side typed handler that updates an
existing workspace tab's widget data on behalf of the LLM. Implements Q8 conflict
resolution (USER WINS).
**Wave**: C-G2 gap-fill.
**Date**: 2026-06-11.

## Implementation

Source files (already existed pre-gap-fill):
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/UpdateWorkspaceTabHandler.cs`
- `infra/dataverse/sprk_analysistool-update-workspace-tab-row.json` (seed row)

Test file added in this gap-fill:
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/UpdateWorkspaceTabHandlerTests.cs`

## Key design decisions

- **Tool surface**: `update_workspace_tab(tabId, widgetData, expectedLastUserEditAt?)`.
- **Q8 USER WINS conflict resolution** (the load-bearing behavior):
  - stored `LastUserEditAt` > LLM-supplied `expectedLastUserEditAt` → REFUSE with
    `Status="stale_read"`; ToolResult success (re-actable response, not an error).
  - stored non-null + LLM supplied null → REFUSE (agent's view inconsistent).
  - both null → permitted (tab never user-edited).
  - parity / agent-fresh → permitted.
- **Field preservation**: `LastUserEditAt` is preserved (tracks USER edits only per
  FR-40); `UpdatedAt` bumped to agent edit time; `WidgetType` immutable from agent
  (no Summary→Table morph allowed — kind discriminator check).
- **Defense-in-depth tenant check**: tab.TenantId must equal ctx.TenantId despite
  service-key already scoping.

## Governance

- **ADR-010**: auto-discovered; ZERO manual DI line.
- **ADR-013**: handler injects `IWorkspaceStateService` directly (BFF-internal
  workspace plumbing — not AI capability). Mirrors task 054 placement.
- **ADR-014 / NFR-16**: `TenantId` required; forwarded into every Redis/Cosmos call.
- **ADR-015**: telemetry = handler name + decision (`applied` | `refused_stale_read`
  | `refused_not_found` | `refused_not_editable` | `refused_kind_mismatch`) + tabId +
  IDs + duration + boolean flags ONLY. NEVER widgetData body, NEVER tab content.
- **ADR-029**: BCL-only; ≤+0.1 MB delta.
- **ADR-030**: NO new SSE / PaneEventBus channel. Frontend re-materializes via the
  existing Pillar 6a polling endpoint.

## Test coverage

5 tests (all pass):
1. `ExecuteChatAsync_Succeeds_AndPersistsMutation_OnHappyPath` — clean update path.
2. `ValidateChat_Fails_WhenTabIdMissing` — required-arg validation.
3. `ExecuteChatAsync_ForwardsTenantId_ToWorkspaceState` — tenant isolation.
4. `ExecuteChatAsync_ReturnsError_WhenWorkspaceStateThrowsOnUpsert` — graceful failure.
5. `ExecuteChatAsync_Refuses_WhenLastUserEditAtIsNewerThanExpected` — **Q8 stale-read
   refusal** (load-bearing): asserts `Status=stale_read`, `CurrentLastUserEditAt`
   present, and `UpsertTabAsync` NEVER called.

## Build status

- Source build: clean.
- Test build: clean.
- Tests: 5/5 pass.
