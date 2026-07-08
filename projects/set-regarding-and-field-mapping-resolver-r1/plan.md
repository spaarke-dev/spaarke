# set-regarding-and-field-mapping-resolver-r1 · Implementation Plan

> **Source spec**: [`spec.md`](./spec.md) (24 FRs, 6 NFRs, Appendix A = Field Mapping subsystem spec)
> **Portfolio**: [Project #536](https://github.com/spaarke-dev/spaarke/issues/536) · Epic [#535 ENTITY FUNCTIONALITY](https://github.com/spaarke-dev/spaarke/issues/535)
> **Created**: 2026-07-02
> **Status**: Ready for Wave 0

---

## Architecture Context

### Applicable ADRs

| ADR | Relevance | Key rules |
|---|---|---|
| **ADR-006** — UI Surface Architecture (Code Pages, PCF, Web Resources) | PCFs remain PCFs; ribbons + presave stay as web resources | Field-bound custom controls justify PCF; no new webresources for custom UI |
| **ADR-012** — Shared Component Library | Workstream C moves shared code to `@spaarke/ui-components` | Fluent v9 only, service abstractions, no bundling of platform libs |
| **ADR-022** — PCF Platform Libraries (Field-Bound Only) | Both PCFs remain virtual; React 16 platform-library + Fluent v9 platform-library | No bundling of react/fluentui in PCF outputs |
| **ADR-024** — Polymorphic Resolver Pattern | This project **extends** the pattern (4 → 5 denormalized fields); requires ADR-024 amendment (documentation-only, Path B per CLAUDE.md §6.5) | Client-side dual-field write via `PolymorphicResolverService.applyResolverFields()` |
| **ADR-038** — Testing Strategy (Integration-Heavy Pyramid) | 6 KEEP path categories; no `Mock<HttpMessageHandler>`; `TimeProvider` over `Stopwatch`; ban ctor null-check tests | `/test-diet` at project close per CLAUDE.md §7 |
| ADR-011 (implicit) — Dataset PCF Over Subgrids | Indirect: `PolymorphicPicker` uses dropdown + `Xrm.Utility.lookupObjects`, not dataset | Not blocking |
| ADR-021 (implicit) — Fluent UI v9 Standardization | All Fluent v9 authoring in this project | Loaded via `/fluent-v9-component` skill |

### Discovered Resources

**Skills load-bearing for this project**
- `pcf-deploy` — build/pack/deploy RegardingResolver + AssociationResolver via solution ZIP
- `dataverse-deploy` — deploy solutions, ribbon customizations, web resources
- `dataverse-create-schema` — create Dataverse columns (`sprk_regardingrecordnumber` on 10 entities)
- `ribbon-edit` — export/import ribbon customizations for parent forms
- `fluent-v9-component` — author `PolymorphicPicker` in shared lib
- `code-review` + `adr-check` — Step 9.5 quality gates (FULL rigor per task-execute)
- `task-execute` — canonical execution protocol
- `test-diet` — project-close test reconciliation per ADR-038

**Patterns to consult**
- [`.claude/patterns/dataverse/polymorphic-resolver.md`](../../.claude/patterns/dataverse/polymorphic-resolver.md) — client + server resolver dual-field strategy (`applyResolverFields`, `resolveRecordType`)
- [`.claude/patterns/pcf/fluent-v9-modern-theming.md`](../../.claude/patterns/pcf/fluent-v9-modern-theming.md) — Griffel + tokens in PCF hosts
- [`.claude/patterns/pcf/fluent-v9-canvas-vs-mda-disabled.md`](../../.claude/patterns/pcf/fluent-v9-canvas-vs-mda-disabled.md) — disabled-state rendering
- [`.claude/patterns/ui/fluent-v9-component-authoring.md`](../../.claude/patterns/ui/fluent-v9-component-authoring.md) — shared-lib authoring conventions
- [`.claude/patterns/ui/record-modal-selection.md`](../../.claude/patterns/ui/record-modal-selection.md) — modal-open pattern (`Xrm.Navigation.navigateTo`) for FR-A2-01

**Canonical implementations**
- [RegardingResolver v1.2.0](../../src/client/pcf/RegardingResolver/) — current PCF baseline
- [AssociationResolver v1.1.0](../../src/client/pcf/AssociationResolver/) — current PCF baseline
- [`PolymorphicResolverService.ts`](../../src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts) — `applyResolverFields()` current 4-field write
- [`FieldMappingEndpoints.cs`](../../src/server/api/Sprk.Bff.Api/Api/FieldMappings/FieldMappingEndpoints.cs) — 4 BFF endpoints (unchanged consumption)
- [`sprk_communication/RibbonDiff.xml`](../../infrastructure/dataverse/ribbon/CommunicationRibbons/Entities/sprk_communication/RibbonDiff.xml) — canonical ribbon-button pattern (Send button)
- [`sprk_todo_regarding_presave.js`](../../src/client/webresources/js/sprk_todo_regarding_presave.js) v1.1.0 — presave webresource baseline

**Scripts**
- `Build-AllClientComponents.ps1` — orchestrated client-side build (PCF + Code Pages + shared lib)
- `Build-ViteSolutionsDirect.ps1` — Vite direct build for `src/solutions/*` (MDA form solutions if applicable)

**Hot-path check** (per CLAUDE.md §10 / CICD-061): all hot-paths declared `N`; no active project overlap on our surfaces (`src/client/pcf/*`, `src/client/shared/Spaarke.UI.Components/`) as of 2026-07-02.

---

## Phase Breakdown (Waves)

**Wave-based execution** — tasks within a Wave run in parallel where `parallel-safe` allows; Waves run sequentially. Max concurrency: 6 agents per Wave.

### Wave 0 — Discovery & Metadata Population (BLOCKS ALL)

**Goal**: Confirm current state, populate metadata that data-driven code will read.

| # | Task | Effort | Parallel-safe |
|---|---|---|---|
| 001 | Wave-0 discovery audit (Q-06, Q-07, active-profile inventory, presave/handler grep-and-check) | 3h | — |
| 002 | Populate `sprk_recordtype_ref.sprk_regardingrecordnumberfield` for 10 non-Matter entities (FR-A4-02) | 2h | after 001 |

**Exit criteria**: Discovery report in `notes/wave-0-discovery.md`; all 11 `sprk_recordtype_ref` rows have populated `sprk_regardingrecordnumberfield` (or documented graceful-blank for Contact/Account per A-07).

### Wave 1 — Dataverse Schema (`sprk_regardingrecordnumber` on 10 entities)

**Goal**: Every target entity carries the new denormalized column so PCFs can render + write uniformly.

| # | Task | Effort | Parallel-safe |
|---|---|---|---|
| 010 | Add `sprk_regardingrecordnumber` (text, 100 chars, indexed) to 10 target entities via one solution package | 4h | — |

**Exit criteria**: All 11 target entities (Matter + 10 new) have the column; solution export verifies.

### Wave 2 — Shared library (`@spaarke/ui-components`)

**Goal**: Extract shared implementation; land one shared-lib minor release covering all three additions (per A-05).

| # | Task | Effort | Parallel-safe |
|---|---|---|---|
| 020 | Extend `PolymorphicResolverService.applyResolverFields()` for 5-field write; add data-driven lookup on `sprk_regardingrecordnumberfield` (FR-C1-01 + FR-A4-01) | 5h | yes |
| 021 | Extract `PolymorphicPicker` Fluent v9 component (FR-C2-01) | 6h | yes |
| 022 | Relocate `FieldMappingHandler` to `@spaarke/ui-components/src/services/` (FR-C1b-01) | 3h | yes |
| 023 | Extend `EntityLookupConfig` interface with `regardingRecordNumberField?: string` (FR-B4-01 interface part) | 1h | yes |

**Exit criteria**: `@spaarke/ui-components` publishes with all 4 additions; unit tests updated per ADR-038 KEEP paths; consumers uncompiled but ready.

### Wave 3 — RegardingResolver PCF Workstream A

**Goal**: Ship v1.3.0 with streamlined 2-row layout.

| # | Task | Effort | Parallel-safe |
|---|---|---|---|
| 030 | RegardingResolver 2-row layout + toolbar-icon entity picker + `PolymorphicPicker` consumption (FR-A1-01/02/03) | 6h | yes with 031, 032 |
| 031 | RegardingResolver modal-open on record-number click via `Xrm.Navigation.navigateTo` `target: 2` (FR-A2-01) | 2h | yes with 030 |
| 032 | RegardingResolver populates `pending.recordNumber` on presave global seam (FR-A5-04 client half) | 2h | yes with 030 |
| 033 | Preserve read-only mode + URL field behavior; version bump v1.2.0 → v1.3.0 (FR-A5-01/03, FR-A6) | 2h | after 030-032 |

**Exit criteria**: RegardingResolver v1.3.0 buildable; visual regression snapshots for 2-row layout; unit tests cover all 11 entities.

### Wave 4 — Presave webresource (parallel with Wave 3)

| # | Task | Effort | Parallel-safe |
|---|---|---|---|
| 040 | Update `sprk_todo_regarding_presave.js` to v1.2.0: append `sprk_regardingrecordnumber` to `TEXT_FIELDS`, add `textKeyForField` case for `recordNumber`, extend pending-payload docstring (FR-A5-04) | 2h | yes with Wave 3 |

**Exit criteria**: Webresource v1.2.0 diff clean; `TEXT_FIELDS.length === 4`; docstring reflects `recordNumber` key.

### Wave 5 — AssociationResolver PCF Workstream B

**Goal**: Ship v1.2.0 as thin adapter delegating to shared services.

| # | Task | Effort | Parallel-safe |
|---|---|---|---|
| 050 | Retire `ENTITY_LOOKUP_CONFIGS` const; transition `getEntityConfig` + `getAllEntityConfigs` to dynamic-first (FR-B4-01) | 3h | yes with 051, 052 |
| 051 | `RecordSelectionHandler` becomes thin adapter delegating to `PolymorphicResolverService.applyResolverFields()` (FR-C1-01 consumer) | 4h | yes with 050 |
| 052 | AssociationResolver consumes shared `PolymorphicPicker` component (FR-C2 consumer) | 3h | yes with 050, 051 |
| 053 | AssociationResolver imports relocated `FieldMappingHandler` from `@spaarke/ui-components`; version bump v1.1.0 → v1.2.0 (FR-C1b-01 consumer + FR-B7) | 2h | after 050-052 |

**Exit criteria**: AssociationResolver v1.2.0 buildable; `grep -RE "ENTITY_LOOKUP_CONFIGS\b" src/client/pcf/AssociationResolver` returns zero; unit tests confirm write parity with RegardingResolver.

### Wave 6 — Field Mapping subsystem: MDA form + push webresource + ribbon

| # | Task | Effort | Parallel-safe |
|---|---|---|---|
| 060 | Native MDA form for `sprk_fieldmappingprofile` with `sprk_fieldmappingrules` editable subgrid (FR-B2-01) | 4h | yes with 061 |
| 061 | New `sprk_fieldmapping_push.js` webresource — `Sprk.FieldMapping.Push.hasSourceProfile()` visibility check + `Sprk.FieldMapping.Push.pushUpdates()` sequential multi-target push + toast (FR-B3-01/02/03/04) | 6h | yes with 060 |
| 062 | Ribbon `CustomAction` on Matter form + others via `/ribbon-edit` (FR-B3-01 deploy path) | 3h | after 061 |

**Exit criteria**: MDA form deployable; `sprk_fieldmapping_push.js` v1.0.0 deployable; ribbon customization exports + reimports cleanly on Matter form.

### Wave 7 — Docs + audit (parallel)

| # | Task | Effort | Parallel-safe |
|---|---|---|---|
| 070 | OOB Dataverse mapping audit script + report (FR-B6-01) | 4h | yes with 071, 072 |
| 071 | Update ADR-024 "Fields written" section 4 → 5 (documentation-only ADR amendment, Path B per CLAUDE.md §6.5) | 1h | yes with 070, 072 |
| 072 | Update `FieldMappingHandler.ts:10` inline reference + Appendix A cross-links from architecture docs (FR-B1-01) | 1h | yes with 070, 071 |

**Exit criteria**: `notes/oob-mapping-audit.md` exists; ADR-024 diff shows 5-field update; stale inline reference resolved.

### Wave 8 — Deploy + UAT

| # | Task | Effort | Parallel-safe |
|---|---|---|---|
| 080 | Build + deploy RegardingResolver v1.3.0 to UAT (env: spaarkedev1) | 2h | yes with 081, 082 |
| 081 | Build + deploy AssociationResolver v1.2.0 to UAT | 2h | yes with 080, 082 |
| 082 | Deploy web resources (presave v1.2.0 + `sprk_fieldmapping_push` v1.0.0) + ribbon customizations | 2h | yes with 080, 081 |
| 083 | Deploy `sprk_fieldmappingprofile` MDA form solution | 1h | after 082 |
| 084 | UAT: Matter → Event profile end-to-end (create profile → add rules → push → verify child updates) | 3h | after 080, 081, 082, 083 |

**Exit criteria**: All artifacts deployed to UAT; end-to-end scenario passes; visual verification of footer version bumps.

### Wave 9 — Wrap-up

| # | Task | Effort | Parallel-safe |
|---|---|---|---|
| 090 | Project wrap-up — mark README status Complete, author `notes/lessons-learned.md`, run `/test-diet`, run `/devops-idea-create` for `admin-cascade-batch-job-r1` follow-on Idea Issue | 2h | — |

**Exit criteria**: README status = Complete; `notes/lessons-learned.md` exists; `notes/test-diet-report.md` exists; Idea Issue for follow-on batch service created and linked in wrap-up notes.

---

## Effort estimate

| Wave | Task count | Effort range |
|---|---|---|
| 0 | 2 | 5h |
| 1 | 1 | 4h |
| 2 | 4 | 15h |
| 3 | 4 | 12h |
| 4 | 1 | 2h |
| 5 | 4 | 12h |
| 6 | 3 | 13h |
| 7 | 3 | 6h |
| 8 | 5 | 10h |
| 9 | 1 | 2h |
| **Total** | **28 tasks** | **~81h (10-12 workdays; 3-4 weeks calendar with parallelism)** |

Critical path: 0→1→2→3+5+6→8→9. With Wave-3 + Wave-5 + Wave-6 in parallel and Wave-4 + Wave-7 opportunistic, expected calendar time is **~4 weeks** including UAT.

---

## Parallel Execution Groups (Wave-scoped)

| Wave | Parallel group | Prerequisite | Notes |
|---|---|---|---|
| 0 | — | — | Serial |
| 1 | — | Wave 0 complete | Serial (single task) |
| 2 | A: 020, 021, 022, 023 | Wave 1 complete | All in shared lib, different services/files |
| 3 | B: 030, 031, 032 | Wave 2 complete | RegardingResolver internal parallelism |
| 4 | — | Wave 0 complete | Serial with Wave 3 (can run concurrently) |
| 5 | C: 050, 051, 052 | Wave 2 complete | AssociationResolver internal parallelism |
| 6 | D: 060, 061 | Wave 2 complete | 062 after 061 |
| 7 | E: 070, 071, 072 | independent | Docs/audit; can run anytime after Wave 6 |
| 8 | F: 080, 081, 082 | Waves 3-7 complete | Independent deploys |
| 9 | — | Wave 8 complete | Serial |

**Cross-wave parallelism opportunity**: Waves 3, 4, 5, 6, 7 can overlap significantly after Wave 2 lands. Coordinate ordering to keep merge-conflict risk low (they touch different surfaces).

---

## Risks & mitigations

| Risk | Mitigation |
|---|---|
| Contact/Account business-key field ambiguity (A-07) | Wave 0 task 001 confirms; fallback = graceful-blank (NFR-06) rather than new column |
| Ribbon customization import cycles or CustomAction ID collisions | Use `/ribbon-edit` skill (round-trip export/edit/import) rather than manual XML surgery |
| Shared-lib version thrash across consumers | Land ALL Wave 2 changes as ONE `@spaarke/ui-components` minor release (per A-05) |
| PCF platform-library mismatch after Fluent v9 upgrades | Verify `platform-library name="Fluent" version="9"` unchanged in ControlManifest.Input.xml; re-check at Wave 3/5 |
| ADR-024 amendment landing timing (Path B) | Author amendment as part of Wave 7 task 071; merge alongside or before Wave 8 deploy |
| Q-06/Q-07 residual questions block Wave 6 | Address in Wave 0 task 001; escalate to owner if blocking |

---

## References

- **Spec**: [`spec.md`](./spec.md)
- **Design**: [`design.md`](./design.md)
- **Project Issue**: [#536](https://github.com/spaarke-dev/spaarke/issues/536)
- **Epic**: [#535 ENTITY FUNCTIONALITY](https://github.com/spaarke-dev/spaarke/issues/535)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md) *(generated by `/project-pipeline` Step 3)*
- **Root repo instructions**: [`../../CLAUDE.md`](../../CLAUDE.md) §4 (Task Execution Protocol), §6.5 (ADR Conflict Resolution), §10 (BFF Hygiene — N/A here), §11 (Component Justification)
- **Task template**: [`../../.claude/templates/task-execution.template.md`](../../.claude/templates/task-execution.template.md)
