# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-06-07
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | 002 - D-A-02 Add `GET /api/ai/scopes/personas` BFF Endpoint (Pillar 1) |
| **Step** | — |
| **Status** | not-started |
| **Next Action** | Begin Step 1 of task 002. Task 001 (sprk_aipersona entity) complete + deployed. Tasks 002, 003, 004 now unblocked (Wave A-G1). Task 002 can run in parallel with 003 and 004. |

### Files Modified This Session

(reset for next task)

### Critical Context

Task 001 complete 2026-06-07. `sprk_aipersona` entity deployed to Spaarke Dev with full canonical schema (sprk_name primary, sprk_personacode, sprk_description, sprk_systemprompt, sprk_scopetype CHOICE NOT NULL, sprk_tags, sprk_availableadhoc, sprk_parentpersonaid self-lookup). Quality gates PASS (code-review + adr-check both clean). BFF publish-size delta = 0 MB. Downstream tasks 002/003/004 unblocked.

Next task (002) adds the `GET /api/ai/scopes/personas` BFF endpoint mirroring existing scope endpoints in `ScopeEndpoints.cs`. Uses `bff-api` + `endpoints` + `scope` + `ai` tags — FULL rigor; will require BFF publish-size verification per CLAUDE.md §10.

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | 002 |
| **Task File** | tasks/002-add-personas-bff-endpoint.poml |
| **Title** | D-A-02 Add `GET /api/ai/scopes/personas` BFF Endpoint (Pillar 1) |
| **Phase** | Phase A — Data-driven Foundation |
| **Status** | not-started |
| **Started** | — |

---

## Progress

### Completed Steps

*Task not yet started.*

### Current Step

*Task not yet started.*

### Files Modified (All Task)

*Task not yet started.*

### Decisions Made

*Task not yet started.*

---

## Next Action

**Next Step**: Begin Step 1 of task 002 — execute via `task-execute` skill with file path `projects/spaarke-ai-platform-unification-r6/tasks/002-add-personas-bff-endpoint.poml`.

**Parallel-safe with**: 003 (persona resolver), 004 (SYS- seed row). Wave A-G1 can run in parallel after 001 closes.

---

## Blockers

**Status**: None

---

## Session Notes

### Current Session
- Started: 2026-06-07
- Focus: Task 001 complete → ready for Wave A-G1 (002 + 003 + 004 in parallel)

### Key Learnings (carry-forward from task 001)

- **Idempotent script pattern works**: First-run transient "An unexpected error occurred" on second attribute (Dataverse entity propagation timing) is a known pattern per `dataverse-create-schema` skill. Re-running the idempotent script picks up cleanly. The `Test-EntityExists` / `Test-AttributeExists` / `Test-RelationshipExists` guards are mandatory for this pattern.
- **Prefix enforcement lives in BFF**: The canonical 4-scope schema verified via MCP describe has NO Dataverse-side SYS-/CUST- enforcement; lives in `OwnershipValidator.cs` / `ScopeManagementService.cs`. New scope entities should follow the same pattern (schema only; API-side enforcement). Task 002 must wire `OwnershipValidator` for persona CRUD.
- **MCP describe_table is the fastest post-deployment verification**: Returns full T-SQL schema in one call. Use it instead of multiple Web API calls.
- **Canonical exemplar `Deploy-ChartDefinitionEntity.ps1`** matched 1:1 — saved ~30 mins of pattern discovery.

### Handoff Notes

If a new session needs to pick up here:
1. Read CLAUDE.md §R6 Binding Decisions for Q1–Q11
2. Read task POML at `tasks/002-add-personas-bff-endpoint.poml`
3. Run task-execute on task 002 (FULL rigor; BFF publish-size verification required per §10)

---

## Quick Reference

### Project Context
- **Project**: spaarke-ai-platform-unification-r6
- **Project CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md)

### Applicable ADRs (task 002)

- **ADR-001** (Minimal API) — endpoint MUST be Minimal API style
- **ADR-008** (Endpoint filters) — authorization via endpoint filter
- **ADR-010** (DI minimalism) — register inside existing module
- **ADR-013** (AI architecture / facade boundary) — endpoint sits in BFF (correct placement per §10 "Placement Justification")
- **ADR-029** (BFF publish-size) — verify post-build delta

### Recently Completed

- ✅ **Task 001** (2026-06-07) — `sprk_aipersona` entity deployed to Spaarke Dev. Schema verified via MCP describe. Quality gates passed. See `tasks/001-create-sprk-aipersona-entity.poml` `<notes>` for full evidence.

---

## Recovery Instructions

**To recover context after compaction or new session:**

1. Read "Quick Recovery" section above (< 30 seconds)
2. Read task POML at `tasks/002-add-personas-bff-endpoint.poml`
3. Resume from "Next Action"

**Commands**:
- `/project-continue` — full project context reload + master sync
- `/context-handoff` — save current state before compaction

**For full protocol**: see [docs/procedures/context-recovery.md](../../docs/procedures/context-recovery.md)

---

*This file is the primary source of truth for active work state. Keep it updated.*
