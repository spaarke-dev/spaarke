# TASK-INDEX — set-regarding-and-field-mapping-resolver-r1

> **Project**: set-regarding-and-field-mapping-resolver-r1
> **Plan**: [`../plan.md`](../plan.md)
> **Total tasks**: 36 (28 original + 8 out-of-plan polish/fix follow-ons SRFR-034, SRFR-035, SRFR-036, SRFR-037, SRFR-038, SRFR-039, SRFR-041, SRFR-042)
> **Wave count**: 10 (0–9)

## Legend
- 🔲 not-started
- 🔄 in-progress / needs-retry
- ✅ complete
- ⛔ blocked

## Task registry

### Wave 0 — Discovery & Metadata Population (BLOCKS ALL)

| # | Task | Status | Rigor | Effort | Parallel-safe |
|---|---|---|---|---|---|
| 001 | [Wave-0 discovery audit](./001-wave-0-discovery-audit.poml) | ✅ | STANDARD | 3h | — |
| 002 | [Wave 0 data-fix (D-2 mappingtype + D-4 typo recreates + D-5 all 13 + D-6 contact fix)](./002-populate-recordtype-metadata.poml) | ✅ | STANDARD | 5h est / **1.5h actual** | after 001 |

### Wave 1 — Dataverse Schema

| # | Task | Status | Rigor | Effort | Parallel-safe |
|---|---|---|---|---|---|
| 010 | [Add `sprk_regardingrecordnumber` column to 11 target entities (incl. Matter per D-12; Billing Analysis excluded per D-9)](./010-add-regardingrecordnumber-column.poml) | ✅ | STANDARD | 4h est / **~30min actual** | — |

### Wave 2 — Shared library (`@spaarke/ui-components`)

| # | Task | Status | Rigor | Effort | Parallel-safe |
|---|---|---|---|---|---|
| 020 | [Extend `PolymorphicResolverService.applyResolverFields()` for 5-field write](./020-extend-polymorphic-resolver-service.poml) | ✅ | FULL | 5h est / **~1.5h actual** | group A |
| 021 | [Extract `PolymorphicPicker` Fluent v9 shared component](./021-extract-polymorphic-picker.poml) | ✅ | FULL | 6h est / **~2h actual** | group A |
| 022 | [Relocate `FieldMappingHandler` to `@spaarke/ui-components`](./022-relocate-field-mapping-handler.poml) | ✅ | FULL | 3h est / **~1h actual** | group A |
| 023 | [Extend `EntityLookupConfig` interface with `regardingRecordNumberField?`](./023-extend-entity-lookup-config-interface.poml) | ✅ | FULL | 1h est / **~30min actual** | group A |

### Wave 3 — RegardingResolver PCF Workstream A

| # | Task | Status | Rigor | Effort | Parallel-safe |
|---|---|---|---|---|---|
| 030 | [RegardingResolver 2-row layout + toolbar-icon + PolymorphicPicker consumption](./030-regarding-resolver-2-row-layout.poml) | ✅ | FULL | 6h | group B |
| 031 | [RegardingResolver modal-open on record-number click](./031-regarding-resolver-modal-open.poml) | ✅ | FULL | 2h est / **~40min actual** | group B |
| 032 | [RegardingResolver populates `pending.recordNumber` for presave bridge](./032-regarding-resolver-presave-record-number.poml) | ✅ | FULL | 2h est / **~1h actual** | group B |
| 033 | [Preserve read-only mode + URL field; version bump v1.2.0 → v1.3.0](./033-regarding-resolver-preserve-and-version.poml) | ✅ | FULL | 2h est / **~1h actual** | after 030–032 |
| 034 | [UI polish v1.3.0 → v1.3.1 (7 owner-requested changes: OOB section-header styling, refresh button, icon flip, row-2 simplify, showVersionFooter, title uppercase)](./034-regarding-resolver-ui-polish-v1.3.1.poml) | ✅ | FULL | 3h est / **~2h30min actual** | post-Wave 8 follow-on (after SRFR-080 UAT feedback) |
| 035 | [Auto-refresh form after picker selection v1.3.1 → v1.3.2 (owner post-UAT feedback: eliminate manual Save/Refresh click after selection; CREATE-mode presave path preserved)](./035-regarding-resolver-auto-refresh-v1.3.2.poml) | ✅ | FULL | 1h30min est / **~50min actual** | post-SRFR-034 follow-on (parallel with SRFR-036 form config) |
| 036 | [sprk_todo form config — companion webresource `sprk_regardingrecordnumber_hyperlink` v1.0.0 + REGARDING section row layout below RegardingResolver PCF (1/3 record-number-hyperlink + 2/3 record-name; sprk_regardingrecordurl hidden; formxml `<events>` + `<formLibraries>` registration)](./036-sprk-todo-form-config.poml) | ✅ | STANDARD | 2h est / **~1h15min actual** | post-SRFR-034 follow-on (parallel with SRFR-035 PCF auto-refresh) |
| 037 | [Title styling fix v1.3.2 → v1.3.3 (font-weight 400 → 600 per actual OOB DevTools inspection; root container top-padding reduced to 0 for OOB section-header alignment)](./037-regarding-resolver-title-styling-v1.3.3.poml) | ✅ | FULL | 1h est / **~35min actual** | post-SRFR-035 follow-on (parallel with SRFR-038 form labels fix) |
| 038 | [sprk_todo form fix — Name field visibility + top-aligned labels (SRFR-036 regression: cell substitution inherited `visible="false"` from old shells, so record-number + record-name cells rendered inside a visible section but were themselves hidden; flip both cells to `visible="true"`; labels inherit `celllabelposition="Top"` from section)](./038-sprk-todo-form-labels-fix.poml) | ✅ | STANDARD | 45min est / **~25min actual** | post-SRFR-036 fix (parallel with SRFR-037 PCF v1.3.3 polish) |
| 039 | [RegardingResolver v1.3.3 → v1.3.4 — restore Row 2 Name cell inline (1/3 : 2/3 grid) with top-aligned OOB-parity labels "Regarding Number" / "Regarding Name" (owner correction to SRFR-034 §6; Path A exception extended to Row 2 labels 12px/400/#616161)](./039-regarding-resolver-restore-name-cell-v1.3.4.poml) | ✅ | FULL | 1h30min est / **~55min actual** | post-SRFR-037 follow-on (parallel with SRFR-041 form revert) |
| 041 | [sprk_todo FormXml revert — hide OOB fields again (undo SRFR-038 visibility flip; preserve OnLoad handler wiring; owner clarified OOB fields were intentionally hidden)](./041-sprk-todo-form-revert-hide-oob-fields.poml) | ✅ | STANDARD | 30min est / **~15min actual** | post-SRFR-038 revert (parallel with SRFR-039 PCF v1.3.4) |
| 042 | [RegardingResolver v1.3.4 → v1.3.5 — Row 2 hyperlink onLoad fix (pre-loaded records: derive click target from bound sprk_regardingrecordurl attr via URL-parse fallback; selectedTarget wins on fresh picker selection)](./042-regarding-resolver-hyperlink-onload-fix-v1.3.5.poml) | ✅ | FULL | 1h30min est / **~50min actual** | post-SRFR-039 owner bug report (v1.3.4 hyperlink no-op on pre-loaded records) |

### Wave 4 — Presave webresource (independent, parallel with Wave 3)

| # | Task | Status | Rigor | Effort | Parallel-safe |
|---|---|---|---|---|---|
| 040 | [Update `sprk_todo_regarding_presave.js` to v1.2.0 (add recordNumber)](./040-presave-webresource-recordnumber.poml) | ✅ | FULL | 2h est / **~10min actual** | parallel with Wave 3 |

### Wave 5 — AssociationResolver PCF Workstream B

| # | Task | Status | Rigor | Effort | Parallel-safe |
|---|---|---|---|---|---|
| 050 | [Retire `ENTITY_LOOKUP_CONFIGS` const; transition getEntityConfig callers](./050-retire-entity-lookup-configs.poml) | ✅ | FULL | 3h est / **~25min actual** | group C |
| 051 | [`RecordSelectionHandler` → thin adapter delegating to `PolymorphicResolverService`](./051-record-selection-thin-adapter.poml) | ✅ | FULL | 4h | group C |
| 052 | [AssociationResolver consumes shared `PolymorphicPicker`](./052-association-resolver-polymorphic-picker.poml) | ✅ | FULL | 3h | group C |
| 053 | [AssociationResolver imports relocated FMH; version bump v1.1.0 → v1.2.0](./053-association-resolver-fmh-version.poml) | ✅ | FULL | 2h est / **~40min actual** | after 050–052 |

### Wave 6 — Field Mapping subsystem: MDA form + ribbon + push handler

| # | Task | Status | Rigor | Effort | Parallel-safe |
|---|---|---|---|---|---|
| 060 | [Native MDA form for `sprk_fieldmappingprofile` with rules subgrid](./060-fieldmappingprofile-mda-form.poml) | ✅ | STANDARD | 4h est / **~1h actual** | group D |
| 061 | [New `sprk_fieldmapping_push.js` webresource (hasSourceProfile + pushUpdates)](./061-fieldmapping-push-webresource.poml) | ✅ | FULL | 6h est / **~1h actual** | group D |
| 062 | [Ribbon `CustomAction` on Matter form + others via `/ribbon-edit`](./062-fieldmapping-push-ribbon.poml) | ✅ | STANDARD | 3h est / **~50min actual** | after 061 |

### Wave 7 — Docs + audit

| # | Task | Status | Rigor | Effort | Parallel-safe |
|---|---|---|---|---|---|
| 070 | [OOB Dataverse mapping audit script + report](./070-oob-mapping-audit.poml) | ✅ | STANDARD | 4h est / **~1h actual (2 profiles reviewed, 4 OOB mappings found, 0 collisions)** | group E |
| 071 | [ADR-024 amendment — "Fields written" 4 → 5 (Path B)](./071-adr-024-amendment.poml) | ✅ | MINIMAL | 1h est / **~5min actual (main-session edit; §3 boundary hit)** | group E |
| 072 | [Update `FieldMappingHandler.ts:10` inline reference + Appendix A cross-links](./072-fieldmapping-inline-reference.poml) | ✅ | MINIMAL | 1h est / **~10min actual (idempotent — SRFR-022 already applied)** | group E |

### Wave 8 — Deploy + UAT

| # | Task | Status | Rigor | Effort | Parallel-safe |
|---|---|---|---|---|---|
| 080 | [Build + deploy RegardingResolver v1.3.0 to UAT](./080-deploy-regarding-resolver.poml) | ✅ | STANDARD | 2h est / **~15min actual** | group F |
| 081 | [Build + deploy AssociationResolver v1.2.0 to UAT](./081-deploy-association-resolver.poml) | ✅ | STANDARD | 2h est / **~15min actual** | group F |
| 082 | [Deploy webresources (presave v1.2.0 + fieldmapping_push v1.0.0) + ribbon](./082-deploy-webresources-and-ribbon.poml) | ✅ | STANDARD | 2h est / **~30min actual** | group F |
| 083 | [Deploy `sprk_fieldmappingprofile` MDA form solution](./083-deploy-mda-form.poml) | ✅ | STANDARD | 1h est / **~2min (idempotent — Wave 6 SRFR-060 already deployed FieldMappingAdminSolution v1.0.5)** | after 082 |
| 084 | [UAT: Matter → Event profile end-to-end](./084-uat-matter-event-end-to-end.poml) | 🔲 | STANDARD | 3h | after 080–083 |

### Wave 9 — Wrap-up

| # | Task | Status | Rigor | Effort | Parallel-safe |
|---|---|---|---|---|---|
| 090 | [Project wrap-up (test-diet, lessons-learned, follow-on Idea Issue)](./090-project-wrap-up.poml) | 🔲 | STANDARD | 2h | — |

---

## Parallel Execution Groups

| Group | Wave | Tasks | Prerequisite | Notes |
|---|---|---|---|---|
| **A** | 2 | 020, 021, 022, 023 | Wave 1 complete | Shared lib — different services/components |
| **B** | 3 | 030, 031, 032 | Wave 2 complete | RegardingResolver internal parallelism |
| **C** | 5 | 050, 051, 052 | Wave 2 complete | AssociationResolver internal parallelism |
| **D** | 6 | 060, 061 | Wave 2 complete | 062 sequential after 061 |
| **E** | 7 | 070, 071, 072 | Independent | Docs + audit |
| **F** | 8 | 080, 081, 082 | Waves 3–7 complete | Independent deploys |

**Cross-wave parallelism opportunities** (execute after Wave 2 lands):
- Wave 3 + Wave 4 + Wave 5 + Wave 6 CAN overlap significantly (different surfaces)
- Wave 7 has no code dependencies — can run anytime

**Critical path**: 0 → 1 → 2 → (3 & 5 & 6 in parallel) → 8 → 9. Wave 4 & 7 fold into critical path opportunistically.

---

## Dependencies graph (task-level)

```
001 ─────► 002 ─────► 010 ─────► [020, 021, 022, 023]
                                       │
                                       ├───► [030, 031, 032] ───► 033 ───► 080
                                       ├───► 040 ─────────────────────────► 082
                                       ├───► [050, 051, 052] ───► 053 ───► 081
                                       └───► [060, 061] ───► 062 ─────────► 082
                                                              │
                                                              └► 083 ────► 084 ───► 090
                                       [070, 071, 072] ──────────────────► 084

010 also feeds 040 (schema exists on target host)
002 also feeds 020, 040, 050 (data-driven code reads the metadata field)
```

---

## Wrap-up gate

Task 090 invokes `/test-diet` per CLAUDE.md §7 BEFORE marking project Complete. Wrap-up PR description MUST cite `notes/test-diet-report.md` or document the skip rationale. Task 090 also invokes `/devops-idea-create` for the follow-on `admin-cascade-batch-job-r1` per FR-B3b-01.
