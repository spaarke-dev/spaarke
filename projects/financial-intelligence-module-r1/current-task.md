# Current Task — Finance Intelligence Module R1

> **Last Updated**: 2026-02-12 (Phase 4: Output Orchestrator Implementation)
> **Recovery**: Read "Quick Recovery" section first

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Project** | financial-intelligence-module-r1 |
| **Branch** | `work/financial-intelligence-module-r1` |
| **Task** | Phase 4: Output Orchestrator Implementation (NEW APPROACH) |
| **Status** | in-progress |
| **Next Action** | Implement IOutputOrchestratorService per Output-Orchestrator-Design.md (Task 1.1) |
| **Last Checkpoint** | 2026-02-12 (Architecture pivot to generic Output Orchestrator) |

### Files Modified This Session
- `scripts/seed-data/tools.json` - Added TL-009, TL-010, TL-011 (TL-010 will be REMOVED in new approach)
- `scripts/seed-data/playbooks.json` - Added PB-013 with outputMapping (will be UPDATED with enhanced schema)
- `scripts/seed-data/Verify-Finance-Playbook.ps1` - Verification script
- `projects/financial-intelligence-module-r1/notes/Phase-3-Playbook-Configuration-Summary.md` - Phase 3 complete
- `projects/financial-intelligence-module-r1/notes/Phase-4-7-Implementation-Plan.md` - Original plan (superseded)
- `projects/financial-intelligence-module-r1/notes/Output-Orchestrator-Design.md` - **NEW**: Generic Output Orchestrator design

### Critical Context

**ARCHITECTURE PIVOT (2026-02-12)**: Instead of TL-010 tool handler (imperative), building `IOutputOrchestratorService` that reads playbook `outputMapping` and applies updates declaratively. This enables business analysts to configure field mappings without code deployment.

**Vision**: Business analysts configure end-to-end processes via Playbook Builder:
- Select tools from catalog (TL-009, TL-011)
- Define output mappings (tool results → Dataverse fields)
- Configure workflow order
- NO code deployment needed to change behavior

**Current State**:
- Phase 1-2: COMPLETE (Dataverse schema, tool handlers)
- Phase 3: COMPLETE (Playbook PB-013 deployed with tools TL-009, TL-010, TL-011)
- Phase 4: IN PROGRESS (Output Orchestrator implementation - NEW approach)
- Phase 5-7: PENDING (API endpoints, charts, deployment)

**Implementation Plan**: See `Output-Orchestrator-Design.md` for complete design with 6 implementation phases.

## Active Work: Output Orchestrator Implementation

### Phase 1: Core Infrastructure (Current)

**Tasks** (2-3 hours):
- [ ] Task 1.1: Create `IOutputOrchestratorService.cs` interface
- [ ] Task 1.2: Implement `OutputOrchestratorService.cs`
- [ ] Task 1.3: Create `IDataverseUpdateHandler.cs` interface
- [ ] Task 1.4: Implement `DataverseUpdateHandler.cs`
- [ ] Task 1.5: Register services in DI (FinanceModule.cs)

**Next Phases**:
- Phase 2: Update Playbook Schema (1h) - Enhanced outputMapping, remove TL-010
- Phase 3: Update Job Handler (1-2h) - Simplify InvoiceExtractionJobHandler
- Phase 4: IDataverseService Extensions (1h) - Add RetrieveAsync for optimistic concurrency
- Phase 5: Testing (2-3h) - Unit + integration tests
- Phase 6: Deployment (1h) - Redeploy playbooks, remove TL-010

### Completed Sessions

**2026-02-11**: Phase 1-2 Complete
- Dataverse schema field diffs documented
- Tool handlers implemented (InvoiceAnalysisService, InvoiceExtractionToolHandler)
- Unit tests for structured output

**2026-02-12**: Phase 3 Complete
- Deployed tools.json (TL-009, TL-010, TL-011)
- Deployed playbooks.json (PB-013)
- Verified playbook associations in Dataverse

**2026-02-12**: Architecture Design Complete
- Analyzed playbook execution infrastructure
- Identified gap: outputMapping defined but not executed
- Designed generic Output Orchestrator solution
- Documented in Output-Orchestrator-Design.md

## Key Design Decisions

### 1. Generic vs. Specific Tool Handler

**Decision**: Generic `IOutputOrchestratorService` instead of TL-010 tool handler

**Rationale**:
- Declarative configuration (outputMapping in playbook JSON)
- Business analysts can change field mappings without code
- Reusable across ALL playbooks
- Cleaner architecture aligned with playbook vision

### 2. Where to Capture Update Specs

**Decision**: In playbook definition (playbooks.json)

**Alternative Options Rejected**:
- Output Types (sprk_aioutputtypes) - too granular, limited to predefined types
- Nodes (node-based execution) - not using node-based mode for MVP

**Enhanced outputMapping Schema**:
```json
"outputMapping": {
  "updates": [
    {
      "entityType": "sprk_invoice",
      "recordIdSource": "${context.invoiceId}",
      "fields": {
        "sprk_aisummary": "${extraction.aiSummary}",
        "sprk_totalamount": {
          "type": "Money",
          "value": "${extraction.totalAmount}"
        }
      }
    }
  ]
}
```

### 3. Optimistic Concurrency for Matter Totals

**Decision**: DataverseUpdateHandler supports optimistic concurrency with retry

**Implementation**:
- Read current row version
- Include version in update request
- Retry up to 3 times with exponential backoff on conflict

**Rationale**:
- Matter totals updated by multiple invoices concurrently
- Prevent lost updates when multiple job handlers run
- Standard Dataverse pattern for concurrent writes

## Files to Create (Phase 1)

| File | Purpose | Lines |
|------|---------|-------|
| `IOutputOrchestratorService.cs` | Interface + models | ~100 |
| `OutputOrchestratorService.cs` | Implementation | ~200 |
| `IDataverseUpdateHandler.cs` | Interface | ~30 |
| `DataverseUpdateHandler.cs` | Implementation | ~100 |

## Files to Modify (Phases 2-4)

| File | Changes |
|------|---------|
| `playbooks.json` | Enhanced outputMapping, remove TL-010 from scopes |
| `tools.json` | Delete TL-010 entry |
| `InvoiceExtractionJobHandler.cs` | Remove hardcoded updates, call OutputOrchestrator |
| `IDataverseService.cs` | Add RetrieveAsync method |
| `DataverseServiceClientImpl.cs` | Implement RetrieveAsync |
| `FinanceModule.cs` | Register OutputOrchestrator and DataverseUpdateHandler |

## Success Criteria

- [ ] IOutputOrchestratorService reads outputMapping from playbook
- [ ] Variable resolution works (${context.invoiceId} → Guid)
- [ ] Type conversions work (Money, EntityReference, DateTime)
- [ ] Invoice record updated with AI summary, extracted JSON, total amount
- [ ] Matter record updated with optimistic concurrency (total spend, invoice count)
- [ ] TL-010 removed from codebase and Dataverse
- [ ] All unit tests pass
- [ ] Integration test demonstrates end-to-end flow

## Next Steps

**Immediate** (after this checkpoint):
1. Implement `IOutputOrchestratorService.cs` (Task 1.1)
2. Implement `OutputOrchestratorService.cs` (Task 1.2)
3. Implement `IDataverseUpdateHandler.cs` (Task 1.3)
4. Implement `DataverseUpdateHandler.cs` (Task 1.4)
5. Register services in DI (Task 1.5)

**Then**:
- Phase 2: Update playbook schema
- Phase 3: Update job handler
- Phase 4: Extend IDataverseService
- Phase 5: Testing
- Phase 6: Deployment

## Related Documentation

| Document | Purpose |
|----------|---------|
| `Output-Orchestrator-Design.md` | **PRIMARY**: Complete design with code examples |
| `Phase-4-7-Implementation-Plan.md` | Original plan (superseded by Output Orchestrator approach) |
| `Phase-3-Playbook-Configuration-Summary.md` | Playbook deployment summary |
| `playbooks.json` | PB-013 definition (will be enhanced) |
| `tools.json` | Tool definitions (TL-010 will be removed) |

## Recovery Instructions

If resuming after compaction:
1. Read `Output-Orchestrator-Design.md` for complete context
2. Check "Quick Recovery" section above for next task
3. Review "Success Criteria" to understand goals
4. Continue with Phase 1 implementation tasks
