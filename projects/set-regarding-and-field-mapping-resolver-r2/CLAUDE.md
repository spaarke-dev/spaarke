# CLAUDE.md — Set-Regarding and Field-Mapping Resolver R2

> **Project context file. Loaded on session start for this project.**

## Project Status
- **Phase**: Tasks created (16 tasks, 6 phases) — ready for execution
- **Last Updated**: 2026-07-09
- **Current Task**: none (see `current-task.md`); first task = 001
- **Next Action**: Execute task 001 via `task-execute`. Critical path starts 001 → 002 → 003 → engine. See `tasks/TASK-INDEX.md`.

## Quick Reference

**Key files**
- Spec: [spec.md](spec.md) · Design: [design.md](design.md) · Plan: [plan.md](plan.md)
- Task index: [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md)

**Deliverable surfaces**
- Engine: `src/client/shared/Spaarke.UI.Components/src/services/FieldMappingService.ts` (+ `types/FieldMappingTypes.ts`)
- Wiring: the 7 `Create*Wizard/*Service.ts` create methods (adjacent to `applyResolverFields`)
- Shared nav-prop: `src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts`
- BFF: `src/server/api/Sprk.Bff.Api/Models/FieldMapping/FieldMappingRuleDto.cs`, `Api/FieldMappings/FieldMappingEndpoints.cs`
- Dataverse read: `src/server/shared/Spaarke.Dataverse/Models.cs`, `DataverseWebApiService.cs`
- Schema/seed: `sprk_fieldmappingrule` (+ new `sprk_expression`), `sprk_fieldmappingprofile` (spaarkedev1)

## Context Loading Rules
On session start: read `current-task.md` → the active task POML → this file → spec.md §relevant FRs. Load ADRs per task tags (see below). The design decision log (design.md §0) is the fastest orientation.

## 🚨 MANDATORY: Task Execution Protocol
When the user says "work on task X" / "continue" / "next task", you MUST invoke the **`task-execute`** skill with the task POML. DO NOT read POML files and implement manually. task-execute loads knowledge/ADRs, tracks `current-task.md`, checkpoints, and runs Step 9.5 quality gates. See root CLAUDE.md §4.

## Multi-File Work Decomposition
- Phase 1 engine sub-deliverables (D1.3/D1.4/D1.5) touch one file (`FieldMappingService.ts`) — serialize.
- Phase 2 wiring touches 7 independent service files — parallel-safe **except** each must build against the shared engine; verify build between waves.
- BFF Phase 0 files (`.cs`) are separate from client — can parallel client Phase 1 shell, but Phase 1 engine logic depends on the extended DTO shape (Phase 0 D0.3).

## Key Technical Constraints
- **ADR-012**: engine context-agnostic — NO `ComponentFramework.WebApi`. Depend on `IDataService` + `authenticatedFetch`.
- **ADR-024**: `sprk_recordtype_ref` authoritative; matter→matter parent via polymorphic regarding (no self-lookup exists).
- **§10 BFF Hygiene (BFF=Y)**: additive DTO only — no new endpoint/service/DI/package. Run `bff-extensions.md` checklist; report publish-size delta (expected ≈0); state placement decision in PR.
- **NO Dataverse plugins / form scripts** (owner constraint, absolute). Client-only mechanism. See project memory `no-dataverse-plugins`.
- **Never-throw engine**: mapping failures → non-fatal warnings (mirror `applyResolverFields` NFR-06).
- **No `source === target` guard** anywhere (same-entity support). Negative test required.
- **Lookup targets** = `@odata.bind` (payload model, not form binding). All 8 attorney fields are lookups.
- **Per-pair seed against `describe`-verified names** — target field names diverge (Invoice renames + drops law-firm; Report Card renames lawfirm1). Never assume identical.

## Decisions Made
See design.md §0 decision log (decisions 1-9). Highlights: build all 4 mapping types; BFF=Y additive; add `sprk_expression`; wire all 7; seed attorney matrix; same-entity supported; client-only.

## Implementation Notes
- Single BFF call: `GET profiles/{source}/{target}` returns `FieldMappingProfileWithRulesDto` with `Rules[]` `$expand`-ed.
- Server already reads `defaultValue`/`isRequired`/`iscascadingsource`/`compatibilitymode`; only `sprk_mapping_type` + `sprk_expression` are NOT yet read (D0.2 adds them).
- Reference impl for wiring: `invoiceService.createInvoice` / `workAssignmentService` (nav-prop → payload → BU cascade → `applyResolverFields` → `createRecord`).

## Resources
- ADRs: 024, 012, 001/008/010/019, 002 (avoided). Constraints: `.claude/constraints/bff-extensions.md`.
- Related: `visual-host-create-button-r1` (source of the 7 wizards + UAT trigger), `set-regarding-and-field-mapping-resolver-r1` (predecessor, manual push).
- Project memory: `no-dataverse-plugins`.
