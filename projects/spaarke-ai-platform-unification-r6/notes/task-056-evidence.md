# Task 056 evidence — CloseWorkspaceTabHandler

**Pillar / Spec ref**: R6 Pillar 6b / D-C-07 / FR-35 — chat-only typed handler that
closes an open workspace tab. Implements the pin guard (refuse close when pinned;
the user must explicitly unpin first).
**Wave**: C-G2 gap-fill.
**Date**: 2026-06-11.

## Implementation

Source files (already existed pre-gap-fill):
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/CloseWorkspaceTabHandler.cs`
- `infra/dataverse/sprk_analysistool-close-workspace-tab-row.json` (seed row)

Test file added in this gap-fill:
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/CloseWorkspaceTabHandlerTests.cs`

## Key design decisions

- **Tool surface**: `close_workspace_tab(tabId)`.
- **Decision discriminators**: `closed`, `refused_pinned`, `internal_error`.
- **Pin guard policy**: on `tab.IsPinned == true` → return success-with-refusal so
  the LLM can relay the polite "please unpin first" guidance. NO mutation occurs.
- **Idempotent close**: missing tab (already closed or 24h TTL expired) is NOT an
  error — returns `closed` discriminator with "already absent" message. CloseTabAsync
  itself is idempotent (Redis DEL is no-op for missing keys).
- **Persistence semantics**: removes Redis hot-tier row only. Cosmos durable rows
  for previously-pinned tabs are preserved per FR-32 / Q4 hybrid persistence.
- **Service-failure resilience**: exceptions from `IWorkspaceStateService` surface
  as `ToolErrorCodes.InternalError` — the agent surfaces graceful "could not close
  the tab right now" to the user; chat session continues.

## Governance

- **ADR-010**: auto-discovered; ZERO manual DI line.
- **ADR-013**: handler injects `IWorkspaceStateService` directly — workspace state
  is BFF-internal plumbing, not AI capability. Mirrors tasks 054 + 055 placement.
- **ADR-014**: `TenantId` required + forwarded into `IWorkspaceStateService` calls.
  Cross-tenant close structurally impossible (tabId lookup misses wrong partition).
- **ADR-015**: telemetry = handler name + decision + IDs + duration + `isPinned` boolean
  ONLY. NEVER tab content, widget data, matter name as content, user message text.
- **ADR-029**: BCL-only implementation; ≤+0.1 MB delta.

## Test coverage

5 tests (all pass):
1. `ExecuteChatAsync_ClosesUnpinnedTab_OnHappyPath` — Redis DEL via service.
2. `ValidateChat_Fails_WhenTabIdMissing` — required-arg validation.
3. `ExecuteChatAsync_ForwardsTenantId_ToWorkspaceState` — tenant flows through.
4. `ExecuteChatAsync_RefusesClose_WhenTabIsPinned` — **pin guard**: asserts
   `Decision=refused_pinned` AND CloseTabAsync NEVER called.
5. `ExecuteChatAsync_ReturnsError_WhenCloseTabAsyncThrows` — graceful failure.

## Build status

- Source build: clean.
- Test build: clean.
- Tests: 5/5 pass.
