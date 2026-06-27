# Task 054 evidence — SendWorkspaceArtifactHandler

**Pillar / Spec ref**: R6 Pillar 6b / D-C-05 — chat-side typed handler for dispatching a
finished artifact (Summary / DocumentViewer / Dashboard / Table) to the workspace pane.
**Wave**: C-G2 gap-fill (source survived prior failed dispatch; tests added here).
**Date**: 2026-06-11.

## Implementation

Source files (already existed pre-gap-fill):
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/SendWorkspaceArtifactHandler.cs`
- `infra/dataverse/sprk_analysistool-send-workspace-artifact-row.json` (seed row)

Test file added in this gap-fill:
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/SendWorkspaceArtifactHandlerTests.cs`

## Key design decisions

- **One-handler, one-method**: only LLM-facing tool is
  `send_workspace_artifact(widgetType, title, widgetData, matterId?)`. The closed
  4-variant `WidgetType` enum (Summary | DocumentViewer | Dashboard | Table) is the
  authorization surface — no free-text widget escape hatch.
- **Chat-context only**: `InvocationContextKind.Chat`. Playbook nodes write to
  `sprk_analysisoutput` + `sprk_workingdocument`, not the chat-session workspace.
- **Matter precedence**: explicit `matterId` arg > `ChatInvocationContext.MatterId` >
  synthetic "Unattached" sentinel. User pins later via the workspace strip.
- **Agent-created defaults**: `VisibleToAssistant=true` (Pillar 9), `CanEdit=false`
  (chat is the authoring surface), `IsPinned=false`.

## Governance

- **ADR-010**: auto-discovered via `AddToolHandlersFromAssembly`. ZERO manual DI line.
- **ADR-013**: handler under `Services/Ai/Handlers/` injects `IWorkspaceStateService`
  directly — workspace state is BFF-internal plumbing, not an AI capability. Mirrors
  task 053 placement.
- **ADR-014 / NFR-16**: `TenantId` enforced on chat invocation; flows into both the
  `WorkspaceTab.TenantId` and the underlying Redis key + Cosmos partition key.
- **ADR-015**: telemetry emits handler name + decision + tabId + widgetType + matter
  scope present/absent + title LENGTH only. NEVER widgetData body, NEVER title text.
  `SourceProvenance.CreatedBy` is the deterministic `agent:{chatSessionId}` sentinel.
- **ADR-016**: rate-limit is the chat session's existing per-session concurrency slot.
- **ADR-018**: NO new feature flag — auto-discovery is gated by `IWorkspaceStateService`
  resolving (i.e., Pillar 6a deployed).
- **ADR-029**: BCL-only; per-handler publish-size delta ≤+0.1 MB.

## Test coverage

7 tests (all pass):
1. `ExecuteChatAsync_Succeeds_AndPersistsTab_OnHappyPath` — Summary tab dispatch.
2. `ExecuteChatAsync_Fails_WhenWidgetTypeMissing` — arg validation + no persistence on failure.
3. `ValidateChat_Fails_WhenTitleMissing` — required-field validation.
4. `ExecuteChatAsync_ForwardsTenantId_ToWorkspaceStateService` — ADR-014 forwarding.
5. `ValidateChat_Fails_WhenTenantIdMissing` — tenant isolation precondition.
6. `ExecuteChatAsync_ReturnsError_WhenWorkspaceServiceThrows` — graceful failure.
7. `ExecuteAsync_Playbook_ReturnsValidationError` — chat-only invocation guard.

## Build status

- Source build: clean (0 errors).
- Test build: clean (0 errors).
- Tests: 7/7 pass (as part of 31-test gap-fill run).
