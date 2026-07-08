# set-regarding-and-field-mapping-resolver-r1 — Project CLAUDE.md

> **Purpose**: Project-scoped AI context. Loaded automatically when Claude Code is working in this project.
> **Owner**: Ralph Schroeder
> **Created**: 2026-07-02
> **Portfolio**: [Project #536](https://github.com/spaarke-dev/spaarke/issues/536) · Epic [#535 ENTITY FUNCTIONALITY](https://github.com/spaarke-dev/spaarke/issues/535)

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE** (from root `CLAUDE.md` §4): when executing project tasks, you MUST invoke the `task-execute` skill. Do NOT read POML files directly and implement manually. Trigger phrases that MUST route through `task-execute`:

| User says | Action |
|---|---|
| "work on task X" / "execute task X" | Invoke `task-execute` with tasks/{ID}.poml |
| "continue" / "keep going" / "next task" | Read `TASK-INDEX.md`, find first 🔲, invoke `task-execute` |
| "continue with task X" / "resume task X" | Invoke `task-execute` |
| "pick up where we left off" | Load `current-task.md`, invoke `task-execute` |

**Rigor levels for this project** (per root `CLAUDE.md` §8):
- Tasks touching `.cs`/`.ts`/`.tsx` → **FULL** rigor (code-review + adr-check at Step 9.5)
- Tasks 020, 021, 022, 023 (shared library), 030-033 (RegardingResolver), 040 (presave), 050-053 (AssociationResolver), 061 (push webresource) → **FULL**
- Wave 0 001-002 (audit + data-entry), Wave 1 010 (schema), Wave 6 060 (MDA form), 062 (ribbon), Wave 7 070/072 → **STANDARD**
- Wave 7 071 (ADR-024 doc update), Wave 8 UAT 084, Wave 9 090 (wrap-up docs) → **MINIMAL** or **STANDARD**

**Parallel execution**: Waves 2, 3, 5, 6, 7 have parallel groups (see `plan.md` § Parallel Execution Groups). Each parallel task in a group STILL uses `task-execute`. Send ONE message with MULTIPLE Skill tool invocations.

---

## Project Scope

Three workstreams (see `spec.md`):
- **A** — RegardingResolver v1.2.0 → v1.3.0: 2-row streamlined layout, manifest-property field binding, modal-open, `sprk_regardingrecordnumber` on 10 entities, data-driven resolution via `sprk_recordtype_ref`
- **B** — AssociationResolver v1.1.0 → v1.2.0: retire hardcoded `ENTITY_LOOKUP_CONFIGS`, wire "Push Updates" ribbon button, MDA form for `sprk_fieldmappingprofile`, OOB-mapping audit, Field Mapping subsystem spec (Appendix A of `spec.md`)
- **C** — Shared library refactor (`@spaarke/ui-components`): extract `PolymorphicPicker` Fluent v9 component, relocate `FieldMappingHandler`, extend `PolymorphicResolverService.applyResolverFields()` for 5-field write

Hot-path declaration: `<bff>N</bff> <spaarkeAi>N</spaarkeAi> <ci-workflows>N</ci-workflows> <skill-directives>N</skill-directives> <root-CLAUDE-md>N</root-CLAUDE-md>`.

Follow-on stub: `admin-cascade-batch-job-r1` — Idea Issue opened at Wave 9 wrap-up for the >500-child batch service.

---

## Applicable ADRs — Load these when relevant

| ADR | When to load | File |
|---|---|---|
| **ADR-024** — Polymorphic Resolver Pattern | Any Wave 2 or Wave 3 task; ADR-024 amendment lands as task 071 | [`.claude/adr/ADR-024-polymorphic-resolver-pattern.md`](../../.claude/adr/ADR-024-polymorphic-resolver-pattern.md) · [`docs/adr/ADR-024-*.md`](../../docs/adr/) |
| **ADR-012** — Shared Component Library | Wave 2 (all shared-lib tasks) | [`.claude/adr/ADR-012-*.md`](../../.claude/adr/) |
| **ADR-022** — PCF Platform Libraries | Wave 3 + Wave 5 (both PCFs); Wave 8 deploys | [`.claude/adr/ADR-022-*.md`](../../.claude/adr/) |
| **ADR-006** — UI Surface Architecture | Wave 6 (ribbon vs new-PCF decision) | [`.claude/adr/ADR-006-*.md`](../../.claude/adr/) |
| **ADR-038** — Testing Strategy | All test-touching tasks; Wave 9 wrap-up (`/test-diet`) | [`.claude/adr/ADR-038-*.md`](../../.claude/adr/) |

**ADR-024 amendment (Path B per CLAUDE.md §6.5)**: task 071 extends the "Fields written" section from 4 → 5 to reflect the new `sprk_regardingrecordnumber` addition. Amendment is documentation-only; no MUST/MUST NOT rule changes. Must merge alongside or before Wave 8 deploy.

---

## Patterns to consult

| Pattern | Waves that need it |
|---|---|
| [`.claude/patterns/dataverse/polymorphic-resolver.md`](../../.claude/patterns/dataverse/polymorphic-resolver.md) | 2, 3, 5 |
| [`.claude/patterns/ui/fluent-v9-component-authoring.md`](../../.claude/patterns/ui/fluent-v9-component-authoring.md) | 2 (task 021 PolymorphicPicker) |
| [`.claude/patterns/ui/record-modal-selection.md`](../../.claude/patterns/ui/record-modal-selection.md) | 3 (task 031 modal-open) |
| [`.claude/patterns/pcf/fluent-v9-modern-theming.md`](../../.claude/patterns/pcf/fluent-v9-modern-theming.md) | 3, 5 (any PCF Fluent v9 work) |
| [`.claude/patterns/pcf/fluent-v9-canvas-vs-mda-disabled.md`](../../.claude/patterns/pcf/fluent-v9-canvas-vs-mda-disabled.md) | 3 (RegardingResolver read-only, task 033) |

---

## Skills to invoke

| Skill | Which tasks |
|---|---|
| `/task-execute` | ALL tasks (mandatory per §4 of root CLAUDE.md) |
| `/fluent-v9-component` | 021 (PolymorphicPicker extraction) |
| `/pcf-deploy` | 080 (RegardingResolver deploy), 081 (AssociationResolver deploy) |
| `/dataverse-deploy` | 082 (webresources + ribbon), 083 (MDA form) |
| `/dataverse-create-schema` | 002 (`sprk_recordtype_ref` populate), 010 (add `sprk_regardingrecordnumber` to 10 entities) |
| `/ribbon-edit` | 062 (ribbon CustomAction on Matter form) |
| `/code-review` + `/adr-check` | Step 9.5 in FULL-rigor tasks |
| `/test-diet` | 090 (wrap-up) |
| `/devops-idea-create` | 090 (open `admin-cascade-batch-job-r1` Idea Issue) |
| `/context-handoff` | Any time context > 60% (per root §5) |

---

## Canonical implementations to mirror

| Surface | Reference path |
|---|---|
| Ribbon button pattern | [`infrastructure/dataverse/ribbon/CommunicationRibbons/Entities/sprk_communication/RibbonDiff.xml`](../../infrastructure/dataverse/ribbon/CommunicationRibbons/Entities/sprk_communication/RibbonDiff.xml) — Send button structure |
| PolymorphicResolverService current | [`src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts`](../../src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts) — `applyResolverFields()` |
| RegardingResolver current | [`src/client/pcf/RegardingResolver/`](../../src/client/pcf/RegardingResolver/) v1.2.0 |
| AssociationResolver current | [`src/client/pcf/AssociationResolver/`](../../src/client/pcf/AssociationResolver/) v1.1.0 |
| Presave webresource | [`src/client/webresources/js/sprk_todo_regarding_presave.js`](../../src/client/webresources/js/sprk_todo_regarding_presave.js) v1.1.0 |
| BFF field-mapping endpoints (consumed unchanged) | [`src/server/api/Sprk.Bff.Api/Api/FieldMappings/FieldMappingEndpoints.cs`](../../src/server/api/Sprk.Bff.Api/Api/FieldMappings/FieldMappingEndpoints.cs) |

---

## Owner clarifications carried forward (from spec.md §Owner Clarifications)

- **B3 multi-target UX**: Push all targets sequentially with combined progress report (no picker dialog).
- **B3 >500 children**: Reject with clear error; admin batch service is FOLLOW-ON project.
- **C1 FieldMappingHandler location**: Move to `@spaarke/ui-components` for symmetry with `PolymorphicResolverService`.
- **A1 field binding**: Manifest properties with sensible defaults.
- **Q-02 presave**: EXPLICIT enumeration confirmed; targeted 5-step update per FR-A5-04.
- **Q-03 ENTITY_LOOKUP_CONFIGS**: No external consumers; retirement scoped to two internal call sites + interface extension.
- **Q-04 ribbon pattern**: Communication Send button is canonical; use `/ribbon-edit`.
- **Q-05 metadata population**: Only Matter populated today; task 002 populates the 10 others.

---

## Residual Wave-0 questions to resolve first

- **Q-06 (from spec A-07)**: Contact and Account target-field name for `sprk_recordtype_ref.sprk_regardingrecordnumberfield`. Prefer graceful-blank (NFR-06) over new column on OOB entities unless owner confirms.
- **Q-07 (from spec)**: Confirm which parent-entity forms carry the ribbon button beyond Matter (audit `sprk_fieldmappingprofile` records).

Both are Wave 0 task 001 deliverables.

---

## Version tracking

| Artifact | Baseline | Target |
|---|---|---|
| RegardingResolver PCF | 1.2.0 | 1.3.0 |
| AssociationResolver PCF | 1.1.0 | 1.2.0 |
| `sprk_todo_regarding_presave.js` webresource | 1.1.0 | 1.2.0 |
| `sprk_fieldmapping_push.js` webresource | — (new) | 1.0.0 |
| `@spaarke/ui-components` package | current | minor bump (single release per A-05) |
| ADR-024 | current | amended (Path B) |

---

## References

- Spec: [`spec.md`](./spec.md) — 24 FRs, 6 NFRs, Appendix A (Field Mapping subsystem spec)
- Design: [`design.md`](./design.md) — owner's original design document
- Plan: [`plan.md`](./plan.md) — Wave breakdown, effort estimates, parallel groups
- Task index: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md) — registry of all 28 POML tasks
- Root repo instructions: [`../../CLAUDE.md`](../../CLAUDE.md)
- PCF module instructions: [`../../src/client/pcf/CLAUDE.md`](../../src/client/pcf/CLAUDE.md)
