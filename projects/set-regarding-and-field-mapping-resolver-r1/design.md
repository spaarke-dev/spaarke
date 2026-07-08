# Set-Regarding and Field-Mapping Resolver — R1

> **Status**: DRAFT — pending spec authoring
> **Created**: 2026-07-01
> **Owner**: Ralph Schroeder
> **Related ADRs**: ADR-022 (PCF Virtual Pattern), ADR-024 (Polymorphic Resolver Pattern), ADR-012 (Shared Component Library), ADR-006 (PCF over Webresources)
> **Predecessors**: `smart-todo-r4` R4-112 (RegardingResolver v1.2.0), inline AssociationResolver work (never had a spec)

## 1. Problem statement

Two cross-entity utility PCF controls exist for polymorphic parent-child relationship management, with significant overlap and open architectural gaps:

- **RegardingResolver** (v1.2.0, Smart To Do R4) — host-side polymorphic regarding picker. Writes the 4 denormalized regarding fields + entity-specific lookup on host records (`sprk_todo`, `sprk_communication`). Newer, delegates all field-write logic to shared `PolymorphicResolverService` (ADR-024 / FR-21).
- **AssociationResolver** (v1.1.0, pre-Spec) — target-side picker used in wizards and on Matter main form. Does the same relationship-creation writes AS RegardingResolver **plus** applies field-inheritance rules from `sprk_fieldmappingprofile` / `sprk_fieldmappingrules` (auto-populate child fields from parent).

The overlap is **100% on relationship-creation**; the divergence is field-inheritance on top. Additionally:

- **UX**: RegardingResolver's current layout doesn't match Spaarke's other PCFs (three separate fields visible on the form; no title row; no unified toolbar-icon lookup).
- **Field inheritance gaps**: `sprk_fieldmappingprofile` tables exist and the BFF has 4 endpoints including `/api/v1/field-mappings/push` for parent→children cascade, but **nothing invokes the cascade endpoint** today. The AssociationResolver's "Refresh from Parent" button is child-form-only.
- **Configuration drift**: AssociationResolver has a hardcoded 8-entity map ([RecordSelectionHandler.ts:46](../../src/client/pcf/AssociationResolver/handlers/RecordSelectionHandler.ts#L46), flagged `STUB: [CONFIG] - S021-01`) while RegardingResolver supports 11 entities from data. Should be data-driven from `sprk_recordtype_ref` in both.
- **New field `sprk_regardingrecordnumber`**: added to Matter in preparation for this project; must be added to the remaining 10 target entities and wired through `PolymorphicResolverService`.

## 2. Guiding principles

- **Do not combine the two PCFs.** They have distinct purposes (host-side regarding vs. target-side field inheritance) and distinct hosting contexts. Their overlap is the *implementation*, not the *interface*.
- **Extract shared implementation into `@spaarke/ui-components`.** Both PCFs consume `PolymorphicResolverService` for writes and (new) a shared `PolymorphicPicker` for the entity-selection UI. Cross-entity utility controls are not tied to any one record type.
- **Data-driven, not code-driven, for entity metadata.** `sprk_recordtype_ref` is authoritative — no hardcoded entity lists in either PCF.
- **Dataverse tables for admin-authorable configuration.** Field mapping profiles + rules stay in `sprk_fieldmappingprofile` / `sprk_fieldmappingrules`. Native MDA forms for MVP authoring. Don't over-engineer authoring UX.
- **Manual cascade trigger, not automatic.** User-initiated ribbon button on parent form → BFF `/push` endpoint. Predictable, auditable.
- **N:N is out of scope.** Regarding pattern is single-parent. N:N inheritance semantics are ambiguous and no concrete use case is in-flight.

## 3. Two workstreams, one project

### Workstream A — RegardingResolver UI redesign

**Goal**: Streamlined 2-row layout matching Spaarke's other PCFs, backed by data-driven entity + record-number resolution.

#### A1. Streamlined 2-row layout

```
┌──────────────────────────────────────────────────┐
│ Related Record                              [🔍] │  ← Row 1: title + toolbar icon
├──────────────────────────────────────────────────┤
│ MATTER-2026-001    Smith v. Jones                │  ← Row 2: number (hyperlink) + name
└──────────────────────────────────────────────────┘
```

- **Row 1**: Title text ("Related Record") + toolbar icon on the right. Toolbar icon click reveals a dropdown of entity types (from `sprk_recordtype_ref`); selecting one opens the **OOB Dataverse side-pane lookup** (`Xrm.Utility.lookupObjects`) filtered to that entity type.
- **Row 2**: Two bound fields — `sprk_regardingrecordnumber` (hyperlink) and `sprk_regardingrecordname` (plain text). Configured either via default binding or by the maker when the PCF is added to the form.
- **Third bound field** (hidden or shown per maker preference): `sprk_regardingrecordtype` (the lookup to `sprk_recordtype_ref`). Not visually rendered in the streamlined layout — its value is displayed as part of Row 1 title if desired, or omitted.

#### A2. Modal open on record-number click

- Clicking the `sprk_regardingrecordnumber` hyperlink calls `Xrm.Navigation.navigateTo({ pageType: 'entityrecord', entityName, entityId }, { target: 2, width: { value: 80, unit: '%' }, height: { value: 80, unit: '%' } })`.
- **`target: 2` = modal** ([confirmed](../../docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md#L513)).

#### A3. Add `sprk_regardingrecordnumber` to remaining target entities

- Matter already has it (2026-07-01).
- Add to: Project, Invoice, Event, Analysis, Organization, Contact, Document, WorkAssignment, Budget, Account.
- Text field, max 100 chars, indexed for search.
- Data model: schema addition — 10 columns across 10 entities.

#### A4. Data-driven record-number resolution

- `sprk_recordtype_ref.sprk_regardingrecordnumberfield` (text, already added by user 2026-07-01) holds the *source* field name on the target entity — e.g., for the "Matter" row, value = `"sprk_matternumber"`.
- `PolymorphicResolverService.applyResolverFields()` extended to:
  1. Look up `sprk_recordtype_ref` for the selected entity → get `sprk_regardingrecordnumberfield` (source field on target entity) and `sprk_regardingfield` (entity-specific regarding lookup name on host).
  2. Query the target record (`entitySet(entityId)?$select=<sprk_regardingrecordnumberfield>`) to read the number value.
  3. Write host's `sprk_regardingrecordnumber` = the queried value alongside the existing 4 fields.

#### A5. Preserve existing behavior

- Read-only mode (FR-24): streamlined layout renders row 2 only, no toolbar icon.
- CREATE-mode bridge (`window.__sprk_regarding_pending__` + `sprk_todo_regarding_presave.js`): must continue to work. Presave handler updated to stage `sprk_regardingrecordnumber` alongside the existing 4 fields.
- `sprk_regardingrecordurl` URL field: **still populated** by `PolymorphicResolverService` (not displayed in the new layout, but the value is available for other consumers like unified views).

#### A6. Version bump

- RegardingResolver: v1.2.0 → v1.3.0
- `PolymorphicResolverService` in `@spaarke/ui-components`: minor bump (backward compatible — new field added to write payload)

---

### Workstream B — AssociationResolver + Field Mapping polish

**Goal**: Author the missing spec, wire the parent-change cascade, unify entity metadata source with RegardingResolver.

#### B1. Author the missing Field Mapping spec

- Inline reference in [FieldMappingHandler.ts:10](../../src/client/pcf/AssociationResolver/handlers/FieldMappingHandler.ts#L10) points at a "spec.md Field Mapping Framework section" that no longer exists in the tree. Author this fresh as part of this project's `spec.md`:
  - `sprk_fieldmappingprofile` schema documented
  - `sprk_fieldmappingrules` schema documented
  - Sync mode semantics (OneTime, ManualRefresh) documented
  - Dirty-field protection rules documented
  - Cascade endpoint contract documented
  - Anti-patterns section: OOB Dataverse mapping mutual-exclusivity

#### B2. Profile authoring UX — MVP

- **In scope**: Native MDA form on `sprk_fieldmappingprofile` with `sprk_fieldmappingrules` as an editable subgrid. Confirm rules subgrid columns: source field, target field, mapping type, default value, execution order.
- **Not in scope**: Custom PCF or Code Page for authoring. Deferred to future phase if MDA form proves inadequate.

#### B3. Cascade trigger — ribbon button on parent form

- Add a **"Push Updates to Related Records"** ribbon button on parent forms (Matter, Project, Invoice, etc. — any entity that is a source in at least one `sprk_fieldmappingprofile`).
- Button handler:
  1. Confirm dialog: "This will push field-mapping updates from this record to related child records. Continue?"
  2. Call `POST /api/v1/field-mappings/push` with `{ sourceEntity, sourceRecordId, targetEntity }`.
  3. Show progress spinner / toast on completion: "Updated X of Y child records. Z skipped, W errors."
- **Target entity selection**: if multiple profiles exist (same source, different targets — e.g., Matter → Event AND Matter → Invoice), show a picker for which target(s) to push to; otherwise auto-pick single.
- **Ribbon button visibility**: only shown when a profile with `sourceEntity` = current record's entity type exists (queried at ribbon-load via BFF `GET /profiles?sourceEntity=X`).

#### B4. Retire the hardcoded 8-entity list

- Delete `ENTITY_LOOKUP_CONFIGS` const in [RecordSelectionHandler.ts:46](../../src/client/pcf/AssociationResolver/handlers/RecordSelectionHandler.ts#L46).
- Replace with runtime query to `sprk_recordtype_ref` (same source RegardingResolver uses).
- Result: AssociationResolver supports the same 11 entities as RegardingResolver, and any future 12th entity is a data change with zero code touch.

#### B5. AssociationResolver writes `sprk_regardingrecordnumber` too

- Free once Workstream B refactors AssociationResolver to delegate to `PolymorphicResolverService` (Workstream C1). Otherwise, add explicit write to its `RecordSelectionHandler`.

#### B6. OOB Dataverse mapping audit + cleanup

- Query all 1:N relationships from source entities that have an active `sprk_fieldmappingprofile` record.
- For each, check whether an OOB attribute-mapping exists on the same relationship (via metadata API).
- **If overlaps found**: report to maker for manual review; do not auto-delete (OOB mappings could have been intentional for pre-existing forms).
- Deliverable: a one-shot audit script + a report in project `notes/`.
- Document the policy in `spec.md`: OOB mapping on a source→target pair with an active `sprk_fieldmappingprofile` is an anti-pattern.

#### B7. Version bump

- AssociationResolver: v1.1.0 → v1.2.0
- BFF `/api/v1/field-mappings/*` endpoints: unchanged (already exist, contract stable)

---

### Workstream C — Shared library refactor (small, high-leverage)

**Goal**: Eliminate implementation duplication between the two PCFs.

#### C1. Consolidate relationship-creation logic in `PolymorphicResolverService`

- `PolymorphicResolverService.applyResolverFields()` already handles the RegardingResolver case.
- Extend it to handle the AssociationResolver case: same 5-field write, plus expose a callable seam that AssociationResolver's post-write flow can subscribe to (for FieldMappingHandler to trigger inheritance).
- AssociationResolver's `RecordSelectionHandler` becomes a thin adapter that calls `PolymorphicResolverService.applyResolverFields()` then invokes `FieldMappingHandler.applyMappingsForSelection()`.

#### C2. Extract shared `PolymorphicPicker` component

- New Fluent v9 component in `@spaarke/ui-components/components/PolymorphicPicker/`:
  - Props: `catalog` (from `sprk_recordtype_ref`), `onSelect(entityType, recordId, recordName)`, `disabled`, `readOnly`.
  - Renders: entity-type dropdown + toolbar icon → opens `Xrm.Utility.lookupObjects` scoped to selected entity.
- RegardingResolver + AssociationResolver both consume `<PolymorphicPicker>` instead of implementing their own dropdown/lookup UI.
- Per CLAUDE.md §11 justification: **Existing** — no equivalent shared component today; both PCFs duplicate the picker UI. **Extension** — cannot extend an existing service since neither PCF exposes the picker. **Cost-of-doing-nothing** — every UI-polish change (e.g., typeahead, recent selections, entity icons) has to be applied to two PCFs independently; drift is inevitable.

## 4. Confirmed decisions

| Item | Decision |
|---|---|
| Configuration home | Dataverse tables (`sprk_fieldmappingprofile` + `sprk_fieldmappingrules`) |
| Authoring UX MVP | Native MDA form with rules subgrid |
| Cascade trigger | Ribbon button on parent form → BFF `/push` |
| N:N handling | Out of scope; declared in design.md |
| Entity-list source | `sprk_recordtype_ref` (data-driven; retire hardcoded 8-entity list) |
| Record-number source | `sprk_recordtype_ref.sprk_regardingrecordnumberfield` (data-driven) |
| Entity-specific lookup source | `sprk_recordtype_ref.sprk_regardingfield` (already there) |
| Combining the two PCFs | Not combining; keeping separate roles + extracting shared implementation |
| OOB Dataverse mapping | Out of scope for competing implementation; audit for overlaps as cleanup task |
| Modal open behavior | `target: 2` (modal), 80% × 80% |
| Streamlined layout | 2-row: title + toolbar icon on row 1, number-hyperlink + name on row 2 |

## 5. ADR alignment

Per CLAUDE.md §6.5, no anticipated ADR tensions:

- **ADR-006 (PCF over Webresources)**: ✅ Both controls are PCFs; no new webresources.
- **ADR-012 (Shared Component Library)**: ✅ Workstream C explicitly moves shared code to `@spaarke/ui-components`.
- **ADR-022 (PCF Virtual Pattern)**: ✅ Both controls already use virtual pattern; changes preserve it.
- **ADR-024 (Polymorphic Resolver Pattern)**: ✅ This project extends the pattern (adds record-number field to the 4 → 5 denormalized field set). Update ADR-024's "Fields written" section as part of this project.

## 6. Hot-path declaration (per CLAUDE.md §10)

```xml
<hot-path-declaration>
  <bff>N</bff>                <!-- Wires an existing endpoint to a ribbon button; no new BFF services or endpoints added -->
  <spaarkeAi>N</spaarkeAi>     <!-- No touches to src/solutions/SpaarkeAi/ -->
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-CLAUDE-md>N</root-CLAUDE-md>
</hot-path-declaration>
```

**Justification for BFF=N**: `/api/v1/field-mappings/push` and related endpoints already exist in `FieldMappingEndpoints.cs` and are stable. This project consumes them from a new ribbon button; it does not add new services, DI registrations, or NuGet packages to `Sprk.Bff.Api`. If spec authoring reveals a need for a new BFF surface, this declaration will be updated and `bff-extensions.md` decision criteria applied.

## 7. Component justification (per CLAUDE.md §11)

For each net-new surface:

### `PolymorphicPicker` (Workstream C2)

- **Existing**: No equivalent shared component. Both PCFs duplicate a dropdown-plus-lookup implementation.
- **Extension**: Cannot extend an existing service — neither PCF's picker is currently exposed as a reusable component; both are private.
- **Cost-of-doing-nothing**: Every UI-polish task (typeahead, recent selections, entity icons, keyboard shortcuts) will require identical changes in two places; drift is inevitable and observable (see current 8-vs-11 entity discrepancy).

### `sprk_regardingrecordnumber` column on 10 additional entities (Workstream A3)

- **Existing**: Matter already has the column (added 2026-07-01). The other 10 target entities do not.
- **Extension**: This *is* the extension — replicating the schema addition. Not a new abstraction, just consistent application.
- **Cost-of-doing-nothing**: The streamlined RegardingResolver layout cannot render the hyperlink label if the underlying field doesn't exist on the host entity when the regarding target changes.

### `sprk_fieldmappingprofile` ribbon button on parent forms (Workstream B3)

- **Existing**: No ribbon action today; BFF endpoint exists but has no client caller.
- **Extension**: Cannot extend an existing button — no cascade-oriented button exists today.
- **Cost-of-doing-nothing**: The `/push` endpoint is dead code. Field-mapping profiles document intent but changes never propagate. The whole cascade layer is theoretical.

## 8. Non-goals

- **N:N inheritance semantics** — deferred.
- **Automatic cascade-on-parent-save** — deferred. This project ships manual (ribbon button) only.
- **Profile-authoring PCF or Code Page** — MVP is native MDA form. Deferred if maker feedback demands more.
- **Deprecating AssociationResolver** — remains an active, distinct control with a distinct purpose.
- **Deleting OOB Dataverse mappings** — audit and report, do not auto-delete.
- **Sync-mode extensibility (adding `Automatic` = 2)** — deferred unless spec authoring shows we need it now.
- **BFF Change Feed / Service Bus for auto-cascade** — architectural pivot; not in this project.
- **Retiring `sprk_regardingrecordurl`** — kept, still populated, just not displayed in the new streamlined layout.

## 9. Open questions (to resolve during spec authoring)

1. **B3 button — target-entity picker UX**: when a parent record's entity type is the source in multiple profiles (Matter → Event, Matter → Invoice, Matter → Todo), does the ribbon button (a) push to all targets sequentially with a combined progress report, (b) show a picker for the user to choose which target(s), or (c) split into one button per target?
2. **B3 button — throttling and safety**: `/push` endpoint has a 500-child-record limit. What happens when a parent has >500 children? Reject with error, chunk automatically, or require an admin override?
3. **A1 layout — bound-field configuration**: does the streamlined PCF hardcode the two bound fields as `sprk_regardingrecordnumber` + `sprk_regardingrecordname`, or expose them as manifest properties so a maker could rebind if a future host entity uses different field names? (Recommendation: manifest properties with sensible defaults.)
4. **A5 CREATE-mode presave**: does `sprk_todo_regarding_presave.js` need to write to `sprk_regardingrecordnumber` from the pending payload, or does the presave already handle "all pending fields" uniformly? (Verification needed — code check, not a design decision.)
5. **B4 supported entity list**: does retiring the hardcoded 8-entity list in AssociationResolver break any existing consumers (wizards) that assume a specific ordering or filter? Grep-and-check.
6. **B8 sync-mode extensibility (was previously enumerated)** — reconfirmed **deferred**; call out in spec that today's set is `{ OneTime, ManualRefresh }` and any new sync mode requires a follow-on project.
7. **C1 — should FieldMappingHandler move to `@spaarke/ui-components` too?** — probably yes for symmetry with `PolymorphicResolverService`. Decide in spec.
8. **B6 — audit script placement**: `scripts/` at repo root? Or `projects/set-regarding-and-field-mapping-resolver-r1/scripts/`? Latter fits project ephemerality.

## 10. Success criteria

1. RegardingResolver v1.3.0 deployed with streamlined 2-row layout on `sprk_todo` and `sprk_communication` forms in UAT; footer version confirms new build.
2. All 11 target entities carry `sprk_regardingrecordnumber` column.
3. `PolymorphicResolverService.applyResolverFields()` writes the new field alongside the existing 4; unit tests updated.
4. AssociationResolver v1.2.0 delegates writes to `PolymorphicResolverService`; hardcoded entity list retired; consumes shared `PolymorphicPicker`.
5. Ribbon button on Matter main form triggers `/push` and updates related child records; toast reports counts.
6. `sprk_fieldmappingprofile` for at least one source→target pair (recommended: Matter → Event) exists and works end-to-end.
7. OOB-mapping audit report delivered in `notes/` with an inventory of any overlapping mappings.
8. `spec.md` for the Field Mapping subsystem (never existed before) written and merged.

## 11. Out-of-scope from this document

- Task-level breakdown → produced by `project-pipeline` from this `design.md` + a subsequent `spec.md`.
- Deployment procedures → covered by existing `/pcf-deploy`, `/bff-deploy`, `/dataverse-deploy` skills.
- Test strategy → per ADR-038 and repo-standard test practices.
- Any Auth changes → out of scope; existing `@spaarke/auth` used unchanged.
