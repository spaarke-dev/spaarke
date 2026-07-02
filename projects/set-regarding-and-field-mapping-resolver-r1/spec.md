# Set-Regarding and Field-Mapping Resolver — R1 · AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-07-02
> **Source**: [`design.md`](./design.md)
> **Owner**: Ralph Schroeder
> **Related ADRs**: ADR-006, ADR-012, ADR-022, ADR-024, ADR-038
> **Predecessors**: `smart-todo-r4` R4-112 (RegardingResolver v1.2.0), inline AssociationResolver work (never had a spec)

---

## Executive Summary

Consolidates two overlapping cross-entity utility PCFs (RegardingResolver, AssociationResolver) by extracting shared implementation into `@spaarke/ui-components`, redesigning RegardingResolver to Spaarke's streamlined 2-row layout, closing the dormant field-mapping cascade path (parent→children propagation via existing BFF `/api/v1/field-mappings/push`), and retiring code-driven entity metadata in favor of data-driven `sprk_recordtype_ref`. Adds `sprk_regardingrecordnumber` to 10 additional target entities so the streamlined layout renders identically across all supported hosts.

---

## Scope

### In Scope

**Workstream A — RegardingResolver UI redesign**
- A1: 2-row streamlined layout (title + toolbar icon; number-hyperlink + name)
- A2: Modal open on record-number click (`Xrm.Navigation.navigateTo` with `target: 2`, 80% × 80%)
- A3: Add `sprk_regardingrecordnumber` (text, 100 chars, indexed) to 10 target entities: Project, Invoice, Event, Analysis, Organization, Contact, Document, WorkAssignment, Budget, Account
- A4: Data-driven record-number resolution via `sprk_recordtype_ref.sprk_regardingrecordnumberfield`
- A5: Preserve read-only mode, CREATE-mode presave bridge, `sprk_regardingrecordurl` population
- A6: RegardingResolver v1.2.0 → v1.3.0; `PolymorphicResolverService` minor bump

**Workstream B — AssociationResolver + Field Mapping polish**
- B1: Author the missing Field Mapping subsystem spec (profile schema, rules schema, sync-mode semantics, dirty-field protection, cascade endpoint contract, OOB-mapping mutual-exclusivity anti-pattern)
- B2: Native MDA form for `sprk_fieldmappingprofile` authoring with `sprk_fieldmappingrules` editable subgrid
- B3: "Push Updates to Related Records" ribbon button on parent forms (sequential multi-target push; reject-with-error for >500 children)
- B3b: **Admin batch-cascade service** (deferred to follow-on project or expanded scope — see §Placement Justification)
- B4: Retire hardcoded 8-entity list (`ENTITY_LOOKUP_CONFIGS`) in AssociationResolver; unify on `sprk_recordtype_ref`
- B5: AssociationResolver writes `sprk_regardingrecordnumber` (free once C1 lands)
- B6: OOB Dataverse mapping audit + report (report-only, no auto-delete)
- B7: AssociationResolver v1.1.0 → v1.2.0

**Workstream C — Shared library refactor**
- C1: Consolidate relationship-creation in `PolymorphicResolverService`; AssociationResolver's `RecordSelectionHandler` becomes thin adapter
- C1b: Move `FieldMappingHandler` to `@spaarke/ui-components` alongside `PolymorphicResolverService` (per owner clarification)
- C2: Extract shared `PolymorphicPicker` Fluent v9 component in `@spaarke/ui-components/components/PolymorphicPicker/`

### Out of Scope
- N:N inheritance semantics — deferred
- Automatic cascade-on-parent-save — deferred; R1 ships manual (ribbon button) only
- Profile-authoring PCF or Code Page — MVP is native MDA form
- Deprecating AssociationResolver — remains active, distinct
- Deleting OOB Dataverse mappings — audit and report only
- Adding sync-mode `Automatic = 2` — deferred; today's set stays `{ OneTime, ManualRefresh }`
- BFF Change Feed / Service Bus for auto-cascade — architectural pivot
- Retiring `sprk_regardingrecordurl` — kept, still populated
- Auth changes — `@spaarke/auth` used unchanged

### Affected Areas

**Client — PCF**
- `src/client/pcf/RegardingResolver/` — v1.2.0 → v1.3.0 (layout redesign, new field write, manifest properties)
- `src/client/pcf/AssociationResolver/` — v1.1.0 → v1.2.0 (thin-adapter refactor, hardcoded-list retirement, PolymorphicPicker consumption)
- `src/client/pcf/AssociationResolver/handlers/RecordSelectionHandler.ts` — `ENTITY_LOOKUP_CONFIGS` retired
- `src/client/pcf/AssociationResolver/handlers/FieldMappingHandler.ts` — relocated to shared lib

**Client — Shared library**
- `src/client/shared/Spaarke.UI.Components/src/services/PolymorphicResolverService.ts` — `applyResolverFields()` extended for 5-field write
- `src/client/shared/Spaarke.UI.Components/src/services/FieldMappingHandler.ts` — new home for handler
- `src/client/shared/Spaarke.UI.Components/src/components/PolymorphicPicker/` — new Fluent v9 component

**Client — Presave scripts (JS webresource)**
- `sprk_todo_regarding_presave.js` — verify pending-field payload handling for `sprk_regardingrecordnumber` (A5)

**Dataverse — Schema**
- 10 entities gain `sprk_regardingrecordnumber` column (Project, Invoice, Event, Analysis, Organization, Contact, Document, WorkAssignment, Budget, Account)
- `sprk_recordtype_ref.sprk_regardingrecordnumberfield` — already added 2026-07-01; populate values

**Dataverse — Ribbon customizations**
- "Push Updates to Related Records" ribbon button on Matter, Project, Invoice, and any other source-entity forms with active `sprk_fieldmappingprofile`
- Ribbon button visibility rule wired to `GET /profiles?sourceEntity=X`

**Dataverse — Forms**
- `sprk_fieldmappingprofile` main form with `sprk_fieldmappingrules` editable subgrid

**BFF (existing endpoints; no new BFF services in this project — see §Placement Justification)**
- `src/server/api/Sprk.Bff.Api/Services/FieldMappings/FieldMappingEndpoints.cs` — consumed unchanged by new ribbon button

**Documentation / ADR updates**
- `docs/adr/ADR-024-polymorphic-resolver-pattern.md` — "Fields written" section extended (4 → 5)
- New: Field Mapping subsystem spec (embedded in this spec.md, cross-linked from `docs/architecture/`)

---

## Requirements

### Functional Requirements

**Workstream A — RegardingResolver UI redesign**

1. **FR-A1-01 (Layout)**: RegardingResolver renders a 2-row layout. Row 1 = title text ("Related Record") + toolbar icon (right-aligned). Row 2 = `sprk_regardingrecordnumber` hyperlink + `sprk_regardingrecordname` plain text.
   - Acceptance: Visual regression test / snapshot confirms 2-row structure; toolbar icon reveals entity-type dropdown on click.

2. **FR-A1-02 (Manifest properties)**: The two bound fields (`sprk_regardingrecordnumber`, `sprk_regardingrecordname`) are exposed as **manifest properties** with sensible defaults matching current host entities.
   - Acceptance: Maker can rebind either field to a differently-named column on a new host entity without code change.

3. **FR-A1-03 (Toolbar-icon → OOB lookup)**: Clicking the toolbar icon shows a dropdown of entity types sourced from `sprk_recordtype_ref`. Selecting an entity opens `Xrm.Utility.lookupObjects()` filtered to that entity type.
   - Acceptance: All 11 entities from `sprk_recordtype_ref` appear; lookup is scoped correctly.

4. **FR-A2-01 (Modal open)**: Clicking the `sprk_regardingrecordnumber` hyperlink opens the related record in a Dataverse modal via `Xrm.Navigation.navigateTo({ pageType: 'entityrecord', entityName, entityId }, { target: 2, width: { value: 80, unit: '%' }, height: { value: 80, unit: '%' } })`.
   - Acceptance: `target: 2` (modal, not new tab); dimensions honored.

5. **FR-A3-01 (Schema addition — 10 entities)**: `sprk_regardingrecordnumber` (text, max 100 chars, indexed) exists on Project, Invoice, Event, Analysis, Organization, Contact, Document, WorkAssignment, Budget, Account.
   - Acceptance: Solution export confirms column on all 10 entities; Matter is already present.

6. **FR-A4-01 (Data-driven resolution)**: `PolymorphicResolverService.applyResolverFields()` looks up `sprk_recordtype_ref` for the selected entity, reads `sprk_regardingrecordnumberfield`, queries the target record for that field's value, and writes it to host's `sprk_regardingrecordnumber`.
   - Acceptance: Selecting any of the 11 entities writes the correct record-number value to host; no hardcoded field-name map exists in code.

7. **FR-A5-01 (Read-only mode)**: In read-only mode, RegardingResolver renders row 2 only (no toolbar icon on row 1, or row 1 hidden).
   - Acceptance: `readOnly` control property confirmed via manifest; UI reflects state.

8. **FR-A5-02 (CREATE-mode presave bridge)**: `window.__sprk_regarding_pending__` + `sprk_todo_regarding_presave.js` continue to stage regarding writes prior to first save, now including `sprk_regardingrecordnumber` alongside the existing 4 fields.
   - Acceptance: Create-then-save flow on `sprk_todo` persists all 5 fields correctly; verified in UAT.

9. **FR-A5-03 (URL field preserved)**: `PolymorphicResolverService` continues to populate `sprk_regardingrecordurl`; only visual rendering changes (URL not shown in streamlined layout).
   - Acceptance: Host record's URL field still populated post-selection; validated via Web API query.

**Workstream B — AssociationResolver + Field Mapping**

10. **FR-B1-01 (Field Mapping subsystem spec)**: A comprehensive Field Mapping subsystem spec exists as **Appendix A** of this document, covering: `sprk_fieldmappingprofile` schema, `sprk_fieldmappingrules` schema, sync-mode semantics (`OneTime`, `ManualRefresh`), dirty-field protection, cascade endpoint contract, OOB-mapping mutual-exclusivity anti-pattern.
    - Acceptance: Appendix A merged; cross-referenced from architecture docs; stale inline reference in `FieldMappingHandler.ts:10` updated to point at this appendix.

11. **FR-B2-01 (Profile authoring MDA form)**: Native MDA form on `sprk_fieldmappingprofile` includes: source entity, target entity, sync mode, active flag, and editable subgrid of `sprk_fieldmappingrules` with columns: source field, target field, mapping type, default value, execution order.
    - Acceptance: Maker can create a Matter → Event profile end-to-end via form alone; rules can be added, edited, deleted, reordered.

12. **FR-B3-01 (Ribbon button — "Push Updates to Related Records")**: Ribbon button visible on parent forms of any entity that is a source in at least one active `sprk_fieldmappingprofile`. Visibility rule queries `GET /profiles?sourceEntity=X` at ribbon-load; button hidden if no profiles.
    - Acceptance: Ribbon appears on Matter form (given active Matter→Event profile); hidden on Contact form (no active source profile).

13. **FR-B3-02 (Confirm dialog)**: Clicking the ribbon button shows a confirmation dialog: "This will push field-mapping updates from this record to related child records. Continue?"
    - Acceptance: Dialog appears with Continue/Cancel; Cancel aborts silently.

14. **FR-B3-03 (Multi-target — sequential push)**: When multiple profiles share the same source (e.g., Matter → Event AND Matter → Invoice), the button pushes to ALL targets sequentially with a combined progress report. **No target picker.**
    - Acceptance: Toast reports totals across all target entities: "Updated X of Y across N target entities. Z skipped, W errors."
    - Owner clarification: Confirmed 2026-07-02 (option b — push all sequentially).

15. **FR-B3-04 (>500-child guard — reject with error)**: If the `/push` endpoint returns an over-limit error (parent has >500 children for a target), the ribbon button shows: "Too many related records to push interactively (X > 500). Contact your administrator to run the batch cascade job."
    - Acceptance: >500-child parent shows the specific message and does not partially update; message references the admin batch service (see FR-B3b-01).
    - Owner clarification: Confirmed 2026-07-02 (reject-with-error + admin batch service is the sanctioned path).

16. **FR-B3b-01 (Admin batch-cascade service — scope note)**: An admin-invocable batch service that can execute cascades for parents with >500 children is required to complete the story. This service's implementation is **flagged for scope decision** (see §Placement Justification and §Unresolved Questions Q-01) — it may ship in R1 as a follow-on workstream or be split into `admin-cascade-batch-job-r1`.
    - Acceptance: Scope decision recorded in `TASK-INDEX.md` before Wave planning begins; if in-R1, admin CLI or Foundry-agent trigger produces same field-update outcome as `/push`.

17. **FR-B4-01 (Retire hardcoded entity list)**: `ENTITY_LOOKUP_CONFIGS` constant in `AssociationResolver/handlers/RecordSelectionHandler.ts` deleted. Entity metadata sourced at runtime from `sprk_recordtype_ref`.
    - Acceptance: `grep -R ENTITY_LOOKUP_CONFIGS src/client/pcf/AssociationResolver` returns zero hits; AssociationResolver supports all 11 entities the RegardingResolver supports.

18. **FR-B5-01 (Regarding-record-number write)**: AssociationResolver writes `sprk_regardingrecordnumber` on selection (free once C1 lands; explicit write otherwise).
    - Acceptance: Selecting a target via AssociationResolver persists all 5 regarding fields identically to RegardingResolver.

19. **FR-B6-01 (OOB mapping audit)**: A one-shot audit script queries all 1:N relationships from source entities with active `sprk_fieldmappingprofile` records and cross-checks OOB attribute-mappings via metadata API. Report delivered as markdown in `projects/set-regarding-and-field-mapping-resolver-r1/notes/oob-mapping-audit.md`.
    - Acceptance: Report enumerates every overlap; **no auto-deletion** occurs; policy documented in Appendix A (§Anti-patterns).

**Workstream C — Shared library refactor**

20. **FR-C1-01 (Consolidation in `PolymorphicResolverService`)**: `PolymorphicResolverService.applyResolverFields()` handles BOTH RegardingResolver and AssociationResolver cases with identical 5-field-write semantics. AssociationResolver's `RecordSelectionHandler` becomes a thin adapter that calls `applyResolverFields()` then invokes `FieldMappingHandler.applyMappingsForSelection()`.
    - Acceptance: Duplicate write logic removed from AssociationResolver; unit tests confirm write parity.

21. **FR-C1b-01 (FieldMappingHandler relocation)**: `FieldMappingHandler` moves to `src/client/shared/Spaarke.UI.Components/src/services/FieldMappingHandler.ts`. AssociationResolver imports from `@spaarke/ui-components`.
    - Acceptance: Handler lives in shared lib; AssociationResolver has zero local copy; no cross-package leakage.
    - Owner clarification: Confirmed 2026-07-02 (yes, move for symmetry with `PolymorphicResolverService`).

22. **FR-C2-01 (`PolymorphicPicker` shared component)**: New Fluent v9 component at `@spaarke/ui-components/components/PolymorphicPicker/`. Props: `catalog` (from `sprk_recordtype_ref`), `onSelect(entityType, recordId, recordName)`, `disabled`, `readOnly`. Renders entity-type dropdown + toolbar icon → opens `Xrm.Utility.lookupObjects` scoped to selection.
    - Acceptance: Component exported from `@spaarke/ui-components`; both PCFs consume it; storybook/test harness renders it standalone.

### Non-Functional Requirements

- **NFR-01 (Version discipline)**: RegardingResolver v1.2.0 → v1.3.0; AssociationResolver v1.1.0 → v1.2.0; `@spaarke/ui-components` gets a minor bump for `applyResolverFields` payload change (backward compatible) + a minor bump for `PolymorphicPicker` addition + a minor bump for `FieldMappingHandler` addition. Prefer coalescing into ONE shared-lib minor release.
- **NFR-02 (Testing per ADR-038)**: Integration-heavy pyramid. Unit tests for `PolymorphicResolverService.applyResolverFields` covering all 11 entities; integration tests for the ribbon-button → `/push` round-trip on Matter → Event. No `Mock<HttpMessageHandler>`, no ctor null-check tests. Test-diet at project close per CLAUDE.md §7.
- **NFR-03 (PCF virtual pattern per ADR-022)**: Both PCFs remain virtual; no return to legacy shadow-DOM pattern.
- **NFR-04 (Backward compatibility)**: No breaking change to existing `sprk_todo` or `sprk_communication` regarding data. Old records without `sprk_regardingrecordnumber` render row 2 with number cell blank; UI does not crash.
- **NFR-05 (Ribbon-button perceived latency)**: `GET /profiles?sourceEntity=X` at ribbon-load must complete <200 ms P95 to avoid ribbon flicker. Cache response for the form session.
- **NFR-06 (Data-driven config integrity)**: If `sprk_recordtype_ref.sprk_regardingrecordnumberfield` is empty for an entity, the layout renders the record-number cell blank AND logs a warning to the browser console (not a hard failure).

---

## Technical Constraints

### Applicable ADRs

| ADR | Relevance | Load full? |
|---|---|---|
| **ADR-006** (PCF over Webresources) | Both controls remain PCFs; new `PolymorphicPicker` is a PCF-hosted React component | Yes for maintainers of new component |
| **ADR-012** (Shared Component Library) | Workstream C moves shared code into `@spaarke/ui-components` | Yes |
| **ADR-022** (PCF Virtual Pattern) | Both PCFs must remain virtual; NFR-03 | Reference only |
| **ADR-024** (Polymorphic Resolver Pattern) | This project extends the pattern (4 → 5 denormalized fields); "Fields written" section must be updated | **Yes — update required** |
| **ADR-038** (Testing Strategy) | Integration-heavy pyramid; MAINTAIN/SCAFFOLDING classification at close | Yes |

### MUST Rules (from ADRs)
- ✅ **MUST** update ADR-024's "Fields written" section to reflect the 5-field write set (task in Workstream A).
- ✅ **MUST** keep both PCFs on the virtual pattern (ADR-022).
- ✅ **MUST** locate shared code under `@spaarke/ui-components` (ADR-012); no cross-PCF direct imports.
- ✅ **MUST** run `/test-diet` at project close (CLAUDE.md §7); MAINTAIN tests live at KEEP paths per ADR-038 §7.
- ❌ **MUST NOT** introduce hardcoded entity metadata after B4/A4 (all comes from `sprk_recordtype_ref`).
- ❌ **MUST NOT** auto-delete OOB Dataverse attribute-mappings (B6 is audit-only).
- ❌ **MUST NOT** combine the two PCFs into one control (design principle §2).

### Existing Patterns to Follow
- `PolymorphicResolverService` current implementation (RegardingResolver v1.2.0 delegates here)
- Field Mapping BFF endpoints: `src/server/api/Sprk.Bff.Api/Services/FieldMappings/FieldMappingEndpoints.cs`
- Ribbon-button-with-visibility-rule + web-API-call pattern used by existing Spaarke ribbons (audit which one is canonical at Wave 0)
- Fluent v9 shared-component authoring: see `.claude/skills/fluent-v9-component/`

---

## ADR Tensions (per CLAUDE.md §6.5)

> No ADR tensions surfaced at design time. All listed ADRs (ADR-006, ADR-012, ADR-022, ADR-024, ADR-038) apply without exception. ADR-024 requires an **amendment** (documentation-only) to reflect the 4 → 5 field-write extension — this is treated as an in-scope task, not a challenge to the ADR, per CLAUDE.md §6.5 path B applied to a narrow doc update. Reviewers may re-classify as path A if they judge the extension broader in intent.

---

## Placement Justification (per CLAUDE.md §10 — BFF Hygiene)

This project's ONLY BFF touch is **consumption of existing `/api/v1/field-mappings/*` endpoints** from a new ribbon-button client caller. No new BFF services, DI registrations, NuGet packages, or endpoints are added — hence the `<bff>N</bff>` declaration in §Hot-Path Declaration.

**Open placement decision** — the admin batch-cascade service (FR-B3b-01) may or may not fit in this project's scope:

| Option | Placement | Scope impact | Hot-path impact |
|---|---|---|---|
| **In R1 — as BFF endpoint** | New `/api/v1/field-mappings/batch-cascade` (admin-only auth) | Adds ~2-3 tasks; flips `<bff>` to Y; triggers full `bff-extensions.md` checklist | Y |
| **In R1 — as Foundry-agent trigger** | New Foundry workflow with admin-only entry | Adds ~2-3 tasks; hot-path SpaarkeAi possible depending on wiring | Partial |
| **Follow-on — `admin-cascade-batch-job-r1`** | Separate worktree; this project's FR-B3-04 error message points at "future admin service" | R1 scope unchanged; admin story remains theoretical until follow-on | N (this project) |

**Recommendation for spec-review**: Follow-on. R1 already carries 3 workstreams + a schema change + a shared-component extraction + a ribbon-button + an audit. Adding a new BFF surface late is exactly the drift §10 was written to prevent. Owner should confirm before Wave 0.

---

## Hot-Path Declaration (per CLAUDE.md §10)

```xml
<hot-path-declaration>
  <bff>N</bff>
  <spaarkeAi>N</spaarkeAi>
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-CLAUDE-md>N</root-CLAUDE-md>
</hot-path-declaration>
```

**Justification for BFF=N**: `/api/v1/field-mappings/push` and related endpoints already exist in `FieldMappingEndpoints.cs` and are stable. This project consumes them from a new ribbon button; it does not add new services, DI registrations, or NuGet packages to `Sprk.Bff.Api`. If the FR-B3b-01 scope decision (admin batch-cascade) resolves to "in R1", this declaration MUST be updated to `<bff>Y</bff>` and `.claude/constraints/bff-extensions.md` decision criteria applied.

**Justification for others**: No touches to `src/solutions/SpaarkeAi/`, `.github/workflows/`, `.claude/skills/`, or root `CLAUDE.md`.

---

## Component Justification (per CLAUDE.md §11)

### `PolymorphicPicker` (Workstream C2)
- **Existing**: No equivalent shared component. Both PCFs duplicate a dropdown-plus-lookup implementation privately.
- **Extension**: Cannot extend — neither PCF's picker is currently exposed as a reusable component.
- **Cost-of-doing-nothing**: Every UI-polish task (typeahead, recent selections, entity icons, keyboard shortcuts) requires identical changes in two places. Drift is already observable (current 8-vs-11 entity discrepancy is a direct instance).

### `sprk_regardingrecordnumber` column on 10 additional entities (Workstream A3)
- **Existing**: Matter already has the column (added 2026-07-01). The other 10 target entities do not.
- **Extension**: This *is* the extension — consistent schema application, not a new abstraction.
- **Cost-of-doing-nothing**: The streamlined RegardingResolver layout cannot render the hyperlink label if the underlying field doesn't exist on the host entity when the regarding target changes.

### `sprk_fieldmappingprofile` ribbon button on parent forms (Workstream B3)
- **Existing**: No cascade-invoking ribbon action today; BFF endpoint exists but has no client caller.
- **Extension**: Cannot extend — no existing cascade-oriented button.
- **Cost-of-doing-nothing**: The `/push` endpoint is dead code. Field-mapping profiles document intent but changes never propagate. The whole cascade layer remains theoretical.

### `FieldMappingHandler` relocation to `@spaarke/ui-components` (Workstream C1b)
- **Existing**: Handler lives inside AssociationResolver privately.
- **Extension**: This IS the extension — mirroring `PolymorphicResolverService`'s location, per owner clarification 2026-07-02.
- **Cost-of-doing-nothing**: Asymmetry between the two closely-related services obscures the mental model; future consumers (if any) can't discover the handler.

---

## Success Criteria

1. [ ] RegardingResolver v1.3.0 deployed with streamlined 2-row layout on `sprk_todo` and `sprk_communication` forms in UAT — Verify: PCF footer version confirms v1.3.0; visual inspection matches spec Row 1/Row 2 diagram; toolbar-icon dropdown works.
2. [ ] All 11 target entities carry `sprk_regardingrecordnumber` column — Verify: solution export or PowerShell `pac data list-attributes` returns the field on all 11 entities.
3. [ ] `PolymorphicResolverService.applyResolverFields()` writes 5 fields (4 + `sprk_regardingrecordnumber`) — Verify: unit tests updated; XRM API query on a fresh regarding-set returns all 5 fields populated.
4. [ ] AssociationResolver v1.2.0 delegates writes to `PolymorphicResolverService`; hardcoded entity list retired; consumes shared `PolymorphicPicker` — Verify: `grep -R ENTITY_LOOKUP_CONFIGS` empty; unit tests confirm delegation; visual parity with RegardingResolver's picker.
5. [ ] Ribbon button on Matter main form triggers `/push` and updates related child records; toast reports counts across all target entities — Verify: end-to-end UAT with a Matter → Event profile + child records; XRM query confirms field propagation.
6. [ ] `sprk_fieldmappingprofile` for Matter → Event exists and works end-to-end (create profile → add rules → push → observe child updates) — Verify: UAT walkthrough documented.
7. [ ] OOB-mapping audit report delivered in `projects/set-regarding-and-field-mapping-resolver-r1/notes/oob-mapping-audit.md` — Verify: file exists; enumerates all overlaps or explicitly states "none found."
8. [ ] Field Mapping subsystem spec (Appendix A of this document) merged; stale inline reference in `FieldMappingHandler.ts:10` updated — Verify: cross-link exists; `grep -R "spec.md Field Mapping Framework section"` returns updated reference.
9. [ ] ADR-024's "Fields written" section updated to reflect 5-field write set — Verify: git diff on `docs/adr/ADR-024-*` shows the extension.
10. [ ] FR-B3b-01 scope decision recorded — Verify: `TASK-INDEX.md` or `README.md` states whether admin batch service ships in R1 or as follow-on.
11. [ ] `/test-diet` run at project close per CLAUDE.md §7 — Verify: `notes/test-diet-report.md` exists.

---

## Dependencies

### Prerequisites
- `sprk_recordtype_ref` catalog populated for all 11 entities (already done as of Smart Todo R4)
- `sprk_recordtype_ref.sprk_regardingrecordnumberfield` field added (2026-07-01) — VERIFY values populated for all 11 entities before Wave 0
- Matter has `sprk_regardingrecordnumber` (2026-07-01, confirmed by design.md)
- BFF `/api/v1/field-mappings/*` endpoints deployed and stable
- `PolymorphicResolverService` shipped in `@spaarke/ui-components` (Smart Todo R4-112)

### External Dependencies
- Dataverse solution export/import cycle for the 10 schema additions
- Ribbon customization deploy (via `ribbon-edit` skill)
- Dataverse form deploy for `sprk_fieldmappingprofile` main form

---

## Owner Clarifications

Answers captured during design-to-spec interview 2026-07-02:

| Topic | Question | Answer | Impact |
|---|---|---|---|
| B3 multi-target UX | When a parent's entity type is source in multiple profiles, how should the button behave? | **Push all targets sequentially** with combined progress report. | FR-B3-03: no target picker; toast aggregates counts across N target entities. |
| B3 >500 children | What happens when parent has >500 children? | **Reject with clear error; set up an admin service that can do as a batch job.** | FR-B3-04: interactive reject with specific error message. FR-B3b-01: admin batch-cascade service added to scope with placement-decision flag (§Placement Justification). |
| C1 FieldMappingHandler location | Should FieldMappingHandler also move to `@spaarke/ui-components`? | **Yes — move to shared lib for symmetry.** | FR-C1b-01 added; AssociationResolver imports from `@spaarke/ui-components`. |
| A1 field binding | Should the two bound fields be hardcoded or manifest properties? | **Manifest properties with sensible defaults.** | FR-A1-02: manifest properties confirmed; defaults match current known hosts. |

---

## Assumptions

Proceeding with these assumptions (owner did not specify or design.md left implicit):

- **A-01 (Ribbon-button scope entities)**: Ribbon button ships on Matter first (highest priority); other parent forms (Project, Invoice, etc.) get the button only where an active `sprk_fieldmappingprofile` names them as source. Wave 0 audit determines the initial set.
- **A-02 (Presave handler symmetry)**: Q4 in design.md §9 (does `sprk_todo_regarding_presave.js` need explicit writes to `sprk_regardingrecordnumber`?) is treated as a code-check task, NOT a design decision. Assuming existing "pending-fields" iteration in presave handles the new field uniformly; explicit update task added if grep proves otherwise.
- **A-03 (Sync-mode set frozen)**: `{ OneTime, ManualRefresh }` is the entire supported set for R1 per design §9 Q6. Any new mode is a follow-on project.
- **A-04 (Audit-script location)**: Per design §9 Q8 recommendation, audit script lives at `projects/set-regarding-and-field-mapping-resolver-r1/scripts/` — project-ephemeral, not repo-permanent.
- **A-05 (One shared-lib release)**: All shared-lib changes (`PolymorphicResolverService` extension, `PolymorphicPicker` addition, `FieldMappingHandler` relocation) ship in ONE `@spaarke/ui-components` minor release, not three. Reduces client-consumer coordination.
- **A-06 (`sprk_regardingrecordurl` unchanged in payload)**: `PolymorphicResolverService` continues to compute + write the URL exactly as today (5-field-write becomes 5+1 including URL; the "5" in this spec refers to number + name + type + regarding-lookup + regardingrecordnumber; URL is the 6th unchanged).

---

## Unresolved Questions

Still need answers before Wave planning; may block specific Waves rather than the whole project:

- [ ] **Q-01**: FR-B3b-01 scope decision — does the admin batch-cascade service ship in R1, or as follow-on `admin-cascade-batch-job-r1`? Blocks: FR-B3-04 error-message wording (reference to "future" vs. "administrator") and hot-path declaration (`<bff>` flips to Y if in-R1 as BFF endpoint).
- [ ] **Q-02**: FR-A5-02 presave verification — does `sprk_todo_regarding_presave.js` iterate `window.__sprk_regarding_pending__` generically, or does it enumerate the current 4 fields explicitly? Blocks: Wave A code-check task; explicit update task added if enumerated.
- [ ] **Q-03**: FR-B4-01 downstream consumer impact — does retiring `ENTITY_LOOKUP_CONFIGS` break any existing wizards or forms that rely on its ordering or filter? Blocks: Wave B refactor; grep-and-check task at Wave 0.
- [ ] **Q-04**: Ribbon-button canonical pattern — which existing Spaarke ribbon-button-with-visibility-rule is the reference implementation for FR-B3-01? Blocks: Wave B ribbon-authoring; Wave 0 discovery task.
- [ ] **Q-05**: `sprk_recordtype_ref.sprk_regardingrecordnumberfield` population state — is this field populated for all 11 entities today, or does data-entry belong in this project's scope? Blocks: FR-A4-01 (data-driven resolution) requires populated values; if empty, add data-entry task.

---

# Appendix A — Field Mapping Subsystem Spec

*Authored as part of FR-B1-01. Replaces the stale inline reference in [`FieldMappingHandler.ts:10`](../../src/client/pcf/AssociationResolver/handlers/FieldMappingHandler.ts#L10).*

## A.1 Purpose

The Field Mapping subsystem propagates values from a source (parent) record to related target (child) records according to admin-authored rules. Two invocation paths exist:

1. **Selection-time inheritance** (AssociationResolver client-side) — when a user picks a parent via AssociationResolver on a target-record form, matching profile rules populate child fields immediately.
2. **Push-time cascade** (ribbon button → BFF `/push`) — user-initiated propagation of source-record field values to ALL existing related children.

## A.2 Data model

### A.2.1 `sprk_fieldmappingprofile`

Top-level admin-authored container. One profile per source→target entity pair.

| Field | Type | Purpose |
|---|---|---|
| `sprk_fieldmappingprofileid` | GUID | PK |
| `sprk_name` | Text | Human name (e.g., "Matter → Event field inheritance") |
| `sprk_sourceentity` | Text (logical name) | Source entity logical name (e.g., `sprk_matter`) |
| `sprk_targetentity` | Text (logical name) | Target entity logical name (e.g., `sprk_event`) |
| `sprk_syncmode` | OptionSet | `OneTime = 1` \| `ManualRefresh = 2` (see §A.3) |
| `statecode` | State | Active / Inactive (standard) |

### A.2.2 `sprk_fieldmappingrules`

One row per field-level mapping. N:1 to `sprk_fieldmappingprofile`.

| Field | Type | Purpose |
|---|---|---|
| `sprk_fieldmappingruleid` | GUID | PK |
| `sprk_fieldmappingprofileid` | Lookup | Parent profile |
| `sprk_sourcefield` | Text (logical name) | Source field on source entity |
| `sprk_targetfield` | Text (logical name) | Target field on target entity |
| `sprk_mappingtype` | OptionSet | `Copy` \| `Default` \| `Concat` \| `Template` (extensible per WI) |
| `sprk_defaultvalue` | Text | Used when `mappingtype = Default` |
| `sprk_executionorder` | Whole Number | Determines apply order; ties broken by insertion order |

## A.3 Sync-mode semantics

| Mode | Selection-time | Push-time | Dirty-field behavior |
|---|---|---|---|
| **OneTime** | Applied once on parent-selection; never re-applied even if parent changes | Ignored (skips profile) | Overwrites unconditionally on selection; never touches after |
| **ManualRefresh** | Applied on parent-selection AND anytime the "Refresh from Parent" button is clicked on the child form | Applied on ribbon-button push | Respects dirty-field protection (§A.4) |

**Explicitly NOT supported in R1** (deferred): `Automatic = 2` mode that watches parent changes and cascades without manual trigger. Requires a BFF Change Feed / Service Bus subscription; not in this project.

## A.4 Dirty-field protection

- **Definition**: A child field is "dirty" if it has ever been non-default-populated since the last mapping application, OR the user has explicitly edited it.
- **Rule**: `ManualRefresh` mode SKIPS dirty fields during push. Toast reports "Skipped: X fields (dirty)" as part of the aggregated per-record report.
- **Reset**: A field ceases to be dirty when explicitly cleared to null AND the parent has not changed since. (Implementation detail: uses `overriddencreatedon`-style provenance stamping if adopted; otherwise heuristic based on `modifiedby != systemuser`.)
- **`OneTime` mode**: Ignores dirty-field state entirely — overwrites on selection, then never touches.

## A.5 Cascade endpoint contract

**`POST /api/v1/field-mappings/push`**

Request:
```json
{
  "sourceEntity": "sprk_matter",
  "sourceRecordId": "GUID",
  "targetEntity": "sprk_event"
}
```

Response (success, 200):
```json
{
  "updated": 42,
  "skipped": 8,
  "errors": 0,
  "childCount": 50,
  "profileId": "GUID",
  "durationMs": 780
}
```

Response (over-limit, 400):
```json
{
  "error": "over_limit",
  "childCount": 731,
  "maxAllowed": 500,
  "message": "Too many related records to push interactively; use admin batch service"
}
```

**Auth**: OBO (user identity flows). No app-only path exposed on this endpoint. Admin batch service (FR-B3b-01) would use app-only.

**Limit**: 500 children per push. Enforced server-side; client (FR-B3-04) surfaces the specific message.

**Other endpoints in scope for consumption (unchanged)**:
- `GET /api/v1/field-mappings/profiles?sourceEntity=X` — visibility rule for ribbon button
- `GET /api/v1/field-mappings/profiles/{id}` — profile detail (used by BFF cascade logic)
- `GET /api/v1/field-mappings/profiles/{id}/rules` — rules detail (used by BFF cascade logic)

## A.6 OOB Dataverse mapping mutual-exclusivity (Anti-pattern)

Dataverse's OOB attribute-mapping feature (part of relationship metadata) auto-copies fields at record-create time from a specific parent lookup. This overlaps with `sprk_fieldmappingprofile`.

**Rule**: A source→target entity pair with an active `sprk_fieldmappingprofile` MUST NOT ALSO have overlapping OOB attribute-mappings on any 1:N relationship between them. Overlap causes:
- Ambiguity: which mapping wins at create time?
- Diagnosis pain: users see values populated by "magic" with no rule-authoring surface.
- Drift: profile changes have no effect on OOB behavior; OOB changes bypass profile audit.

**Enforcement**: FR-B6-01 delivers a one-shot audit report enumerating overlaps. Report is human-reviewed; no auto-deletion. Documentation policy (this section) makes the rule visible.

## A.7 Testing surface

Per ADR-038 (integration-heavy pyramid):

- **Unit (KEEP)**: `applyResolverFields` covers all 11 entities × 2 modes; `FieldMappingHandler.applyMappingsForSelection` covers `OneTime` and `ManualRefresh` semantics; dirty-field skip logic.
- **Integration (KEEP)**: Ribbon-button → `/push` round-trip on Matter → Event with real profile + 5-child harness.
- **Rejected as SCAFFOLDING at close (per ADR-038 §7)**: DI-registration tests, ctor null-check tests, `Mock<HttpMessageHandler>` tests.

---

*AI-optimized specification. Original design: [`design.md`](./design.md).*
