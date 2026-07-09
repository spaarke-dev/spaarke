# Set-Regarding and Field-Mapping Resolver — R2

> **Status**: DESIGN REFINED — decisions 1-9 resolved (2026-07-08), ready for spec authoring
> **Created**: 2026-07-08
> **Owner**: Ralph Schroeder
> **Related ADRs**: ADR-024 (Polymorphic Resolver Pattern), ADR-012 (Shared Component Library), ADR-006 (PCF over Webresources), ADR-001 (Minimal API), ADR-010 (DI Minimalism)
> **Predecessor**: `set-regarding-and-field-mapping-resolver-r1` (2026-07-01 → merged as PR #549) — this project **amends one of r1's explicit non-goals**; see §2.
> **Triggered by**: UAT on `visual-host-create-button-r1` (2026-07-08) — owner expected Assigned Attorney 1 (and other mapped fields) to auto-populate from Matter when a wizard creates an Event/Invoice/Report Card. It doesn't, because the runtime engine that used to do this was deleted as unrelated collateral damage during r1.

---

## 0. Decision log (resolved during design refinement, 2026-07-08)

Grounded against live `spaarkedev1` schema + current BFF/shared-lib source. See §§4-9 for detail.

| # | Decision | Rationale |
|---|---|---|
| 1 | **Path B amendment accepted** — creation-time cascade IS in scope; update-time stays manual push (`UpdateRelatedButton` → `/push`, untouched) | Brand-new records have no existing data to protect; creation-time carries none of r1's overwrite risk (§2) |
| 2 | **Build all four mapping-type engines** (Copy, Default/Constant, Concat, Template) in the client engine | Owner directive — close the topic, don't reopen it later |
| 3 | **BFF → Y** (was N). Extend `FieldMappingRuleDto` + endpoint projection to surface `mappingType`, `defaultValue`, `isRequired`, `compatibilityMode`; add `sprk_mapping_type` to the server `$select` | Contract can't currently express anything but Copy; the read layer already reads the rest — ~15-20 additive lines. Touch the governed BFF surface **once, completely** |
| 4 | **Add `sprk_expression` column** (`NVARCHAR(2000)`) to `sprk_fieldmappingrule` to hold Concat/Template format strings | Existing `sprk_defaultvalue` is `NVARCHAR(100)` — too small/wrong-semantics for a template. Additive, nullable, no existing rule touched |
| 5 | **Wire the helper into all 7 wizard services** (event, invoice, reportCard, matter, project, workAssignment, todo). **All go through the shared `CreateRecordWizard` → entity `onFinish`/service — NO Dataverse plugins (owner constraint, absolute).** | Helper is generic; marginal per-service cost is small; have them all in place |
| 5a | **Dependency SATISFIED (2026-07-09)**: `invoiceService`/`reportCardService` (+ their wizards) merged to master via `visual-host-create-button-r1`; worktree synced. All 7 services present and wireable. A new **`WizardRegistry`** + **`WizardHostProps`** injection contract (provides `dataService`/`authenticatedFetch`/`bffBaseUrl`) is the engine's hook surface; each service creates via nav-prop → payload → BU cascade → `applyResolverFields` → `createRecord`, so the engine slots in adjacent to `applyResolverFields`. Per-target field names diverge (Invoice renames + drops law-firm; Report Card renames law-firm1) — seed authored per-pair against verified schema. | Owner clarification 2026-07-08/09 |
| 6 | **Field matrix: seed the 8 assigned-resource attorney fields**; owner extends via native MDA form | The 8 lookups are confirmed on `sprk_matter`; config is admin data, not code |
| 7 | **Rewrite stubbed `FieldMappingService.ts` context-agnostic** (`IDataService`/`authenticatedFetch`, not PCF `WebApi`); hoist nav-prop discovery for lookup `@odata.bind` binding | ADR-012 compliance + the attorney fields are all lookups requiring bind-string construction |
| 8 | **Document extensibility** — new Field Mapping Framework architecture doc + updated admin authoring guide | Owner requirement; the four-type model + seam must be discoverable |
| 9 | **Same-entity mapping (matter→matter) is a supported, tested creation-time capability** (acceptance criterion + negative test against a `source === target` guard); update-time same-entity cascade stays a documented non-goal | Storage/contract/helper are already pair-agnostic; creation-time is single-hop and recursion-safe (§10) |

## 1. Problem statement

`visual-host-create-button-r1`'s UAT surfaced that no field values are inherited from a host record (Matter/Project) into a child record created by the "+" wizards (Event, Invoice, Report Card). Investigation traced the full history:

1. **`events-and-workflow-automation-r1`** (Feb 2026) built a complete, working Field Mapping Framework: two Dataverse tables (`sprk_fieldmappingprofile`, `sprk_fieldmappingrule`), a BFF API (`/api/v1/field-mappings/*`, fully implemented — no stubs remain), 4 admin PCF controls (Field Mapping Admin, Profile Editor, Rules List, Rule Editor), and a client-side engine (`FieldMappingHandler.ts`, 547 lines) embedded in the **`AssociationResolver`** PCF that auto-applied mappings **the moment a child record's regarding association was set** ("child-write-time cascade").

2. **`set-regarding-and-field-mapping-resolver-r1`** (July 2026) explicitly scoped automatic cascade OUT (§8 Non-goals: *"Automatic cascade-on-parent-save — deferred. This project ships manual (ribbon button) only."*) in favor of a **manual, ribbon-triggered push** (`UpdateRelatedButton` PCF → `POST /api/v1/field-mappings/push`) — a deliberate, reasoned decision for **already-existing** child records (predictability/auditability: don't silently overwrite a user's edits on save).

3. **SRFR-045** (a sub-effort inside/after r1, 2026-07-05) retired `AssociationResolver` PCF entirely in favor of `RegardingResolver` v1.4.0, on the reasoning that AssociationResolver's picker duty was 100% redundant with RegardingResolver, and its field-mapping duty was "redundant with parent-side push" (per ADR-024's revision log). **This is where the gap was actually created**: the "auto-apply at creation time" capability was deleted as collateral damage of a picker consolidation, without anyone weighing whether *creation-time* cascade is the same risk profile as *update-time* cascade (it is not — see §2).

4. **Today**, the shared lib's `FieldMappingService.ts` (`src/client/shared/Spaarke.UI.Components/src/services/`) is a *different*, never-finished implementation — every Dataverse-querying method is stubbed to return empty results. It was never connected to the real (Feb-era) engine and is dead code today.

**Net effect**: none of `eventService`, `invoiceService`, `reportCardService` (or `matterService`, `projectService`, `workAssignmentService`, `todoService`) call anything that applies field mappings. The two tables and the BFF API are live and functional; nothing invokes the "get profile for this entity pair" path at record-creation time.

## 2. The core design tension — amending r1's non-goal (CLAUDE.md §6.5, Path B)

🔔 **ADR/Design Conflict — RESOLVED: Path B accepted by owner 2026-07-08 (decision 1).**

- **Prior decision**: r1 design.md §8: *"Automatic cascade-on-parent-save — deferred. This project ships manual (ribbon button) only."*
- **Conflict**: The owner's current, explicit requirement (2026-07-08) is that mapped fields **must** auto-populate when a wizard creates a new Event/Invoice/Report Card from a Matter/Project.
- **Why creation-time is a different risk profile than update-time**: r1's caution (manual/predictable/auditable) was motivated by **overwrite risk on existing records** — a "push" that silently clobbers a user's already-edited child field is a real hazard. A **brand-new record being created by a wizard has no existing field values to protect** — the target fields are empty by construction. Applying mappings once, at the moment of creation, carries none of the overwrite risk r1 was guarding against.
- **Proposed path**: **B (amendment)** — narrowly. Amend r1's non-goal to read: *"Automatic cascade on **existing-record update** — still deferred, still manual-only via the ribbon push. Automatic cascade **at child-record creation time** — in scope as of R2, since there is no pre-existing target data to protect."* The manual ribbon-push (`UpdateRelatedButton` → `/push`) is **unchanged** and remains the only mechanism for refreshing already-existing children.
- **Rationale for Path B over Path A (exception) or C (comply)**: This isn't a narrow, one-off deviation (Path A) — it's a general capability every wizard-driven creation flow needs, present or future. And Path C (comply with the existing non-goal) means silently accepting that Assigned Attorney 1 (and everything else) never inherits from Matter into Event/Invoice/Report Card, which contradicts the owner's explicit, current requirement.
- **Impact if accepted**: `eventService`/`invoiceService`/`reportCardService` (and optionally `matterService`/`projectService`/`workAssignmentService`/`todoService`, see §7 open question) gain one additional call before `createRecord` — no schema change, no new BFF endpoint (see §3).
- **Alternative considered (and rejected)**: Leave it manual-only and have the owner click "Push Updates to Related Records" immediately after every wizard-created child. Rejected — defeats the purpose of a "+" button that's supposed to auto-associate *and* auto-populate in one step; adds a mandatory extra click to every single wizard completion.

## 3. Guiding principles (carried forward from r1, still true) + one addition

- **Do not rebuild what already works.** The BFF `/api/v1/field-mappings/*` endpoints are live, fully implemented (no STUB markers), and already consumed by `UpdateRelatedButton`. R2 **reuses** `GET /api/v1/field-mappings/profiles/{sourceEntity}/{targetEntity}` — it does not re-implement profile/rule querying client-side, and it adds no new endpoint/service. The **only** BFF change is an additive DTO extension so the existing endpoint returns the mapping-type + config fields the four engines need (decision 3; §4.2) — hot-path flips to **BFF=Y** but minimally (see §6).
- **Data-driven, not code-driven, for entity metadata.** `sprk_recordtype_ref` remains authoritative (already used by the BFF endpoints, already used throughout this codebase's resolver ecosystem).
- **Configuration stays in Dataverse tables.** `sprk_fieldmappingprofile` / `sprk_fieldmappingrule`, unchanged schema.
- **NEW: prefer native Dataverse forms over custom PCFs for admin authoring** — this reverses the Feb 2026 project's actual build (4 custom PCFs), which contradicted *both* the Feb project's own original README (*"Native Dataverse forms for admin configuration (no PCF required)"*) *and* r1's own explicit decision (*"Not in scope: Custom PCF or Code Page for authoring... MVP is native MDA form"*). Two independent prior decisions agreed on native forms; only the actual Feb build drifted from both. R2 does not resurrect Field Mapping Admin / Profile Editor / Rules List / Rule Editor — see §5.
- **Manual push for existing records is untouched.** `UpdateRelatedButton` → `/push` keeps doing exactly what it does today for already-existing children.

## 4. What R2 actually builds

### 4.1 Client-side apply-at-creation engine (the core deliverable)

Replace the shared lib's stubbed `FieldMappingService.ts` (today: PCF-`WebApi`-bound, every Dataverse method returns `[]` — dead code that also violates ADR-012's context-agnostic rule) with a **working, context-agnostic engine** that:

1. Calls the existing BFF `GET /api/v1/field-mappings/profiles/{sourceEntity}/{targetEntity}` (via `authenticatedFetch`, already available in every wizard service) to get the active profile + ordered, rule-expanded config for a source/target entity pair. **Single call** — the endpoint returns `FieldMappingProfileWithRulesDto` with `Rules[]` already `$expand`-ed (verified in `FieldMappingEndpoints.cs`). No profile found (404) → graceful no-op, return `{ profileFound: false }` (matches `applyResolverFields`'s NFR-06 graceful-blank convention).
2. Fetches the needed source fields from the **already-resolved parent record** via the wizard's existing `IDataService`. For **lookup** source fields, the retrieve must `$select` the `_field_value` form and read the `@Microsoft.Dynamics.CRM.lookuplogicalname` annotation — that annotation gives the referent entity, which is what a straight lookup Copy binds to.
3. Applies each rule in `sprk_executionorder` sequence, branching on **all four `sprk_mapping_type` values** (decision 2):
   - **Copy (0)** — source field → target field. For scalar targets, assign the value. For **lookup** targets, build `navProp@odata.bind = /entityset(guid)` (see §4.1a).
   - **Default/Constant (1)** — write the `sprk_defaultvalue` literal into the target field, ignoring the source.
   - **Concat (2)** — resolve the `sprk_expression` format string (decision 4), substituting `{sprk_field}` placeholders from the parent record, into a text/memo target.
   - **Template (3)** — same resolver as Concat; distinction is semantic (Concat = joined fields, Template = fixed scaffold + fields). One placeholder resolver serves both.
4. Returns `{ profileFound, fieldsMapped, warnings }` — **never throws**; mapping failures are non-fatal warnings appended to the wizard's existing warnings array (same pattern as `applyResolverFields`). Type-incompatibility, missing source field, or unresolved placeholder → warning + skip that rule, not an abort.

**Scope-honest note on size:** this is no longer the design's original "thin ~150-200 line helper." Building all four engines for real + lookup `@odata.bind` construction + the placeholder resolver puts the realistic surface at ~350-450 lines. That is the deliberate cost of decision 2 (close the topic permanently) and is still well-bounded because the profile/rule *querying* lives on the BFF, not in this file.

### 4.1a Lookup binding (the non-obvious hard part)

The deleted `FieldMappingHandler` set values on an open classic form (`Xrm.Page.getAttribute().setValue()`), which auto-handles lookup binding. Our wizards instead build an **OData create payload**, where a lookup MUST be written as `navProp@odata.bind = /entityset(guid)` — exactly the dance `eventService.ts` (lines ~316-336) already does by hand for the regarding link. The **8 confirmed attorney/assigned-resource fields are all lookups** (`sprk_assignedattorney1/2`, `sprk_assignedparalegal1/2`, `sprk_assignedtoexternal/internal` → `contact`; `sprk_assignedlawfirm1/2` → `sprk_organization`), so lookup binding is the *primary* path, not an edge case. The engine must:
- read the source lookup's GUID + referent logical name (from the `_value` field annotation),
- pluralize the referent to its entity set,
- discover the **target** nav-prop on the child entity.

**Decision 7 consequence:** the nav-prop discovery utility currently private to `eventService.ts` (`_discoverNavProps`/`_findNavProp`) must be **hoisted to `@spaarke/ui-components`** so the engine and all 7 services share one implementation rather than copying it.

### 4.2 BFF contract extension (decision 3 — BFF flips to Y)

The as-built `FieldMappingRuleDto` exposes only `SourceField / TargetField / SourceFieldType / TargetFieldType / Priority`. The server already **reads** `sprk_defaultvalue`, `sprk_isrequired`, `sprk_iscascadingsource`, `sprk_compatibilitymode` into `FieldMappingRuleEntity` (verified in `DataverseWebApiService.cs` `$select` + `MapToFieldMappingRuleEntity`) — the DTO simply drops them. `sprk_mapping_type` is **not read anywhere** server-side.

R2 makes a **single, complete, additive** change to the governed BFF surface:
- Add `sprk_mapping_type` + `sprk_expression` to the rule `$select` in `DataverseWebApiService.cs`, and corresponding properties (`MappingType`, `Expression`) to `FieldMappingRuleEntity`.
- Surface `mappingType`, `defaultValue`, `expression`, `isRequired`, `compatibilityMode` on `FieldMappingRuleDto` + the `MapRuleEntityToDto` projection.

No new endpoint, service, DI registration, or package. **Placement Justification (§10 / `bff-extensions.md`)**: this extends an existing, stable contract already consumed by `UpdateRelatedButton`; the alternative (a parallel client-side Dataverse query) would duplicate the profile/rule read logic and violate the "one excellent component" rule (§11). Publish-size delta ≈ 0 (DTO fields only). This is the "touch the governed surface once, completely, never reopen" move — the contract now carries every field all four mapping types will ever need.

### 4.3 Schema extension (decision 4 — additive)

Add one column to `sprk_fieldmappingrule` via `dataverse-create-schema`:
- **`sprk_expression`** `NVARCHAR(2000)` (or Memo) — holds the Concat/Template format string, e.g. `{sprk_matternumber} - {sprk_mattername} ({sprk_practicearea})`.

`sprk_defaultvalue` (`NVARCHAR(100)`) is retained unchanged for the Default/Constant literal. The column is nullable and touches no existing rule. This is the piece that makes Concat/Template genuinely usable rather than crippled at 100 chars.

### 4.4 Wiring into wizard services (decision 5 — all 7, client-only)

All creatable entities flow through the reusable **`CreateRecordWizard`**, whose per-entity `config.onFinish` callback performs the actual `createRecord` (verified in `CreateRecordWizard.tsx` — the wizard shell does not create; the entity's `onFinish`/service does). The engine is called from that `onFinish`/service, immediately before the record payload is created, passing the resolved parent entity/id (already available — the same values passed into `applyResolverFields`).

**Mechanism constraint (owner, absolute): NO Dataverse plugins or form scripts.** Creation-time mapping is client-side only, hooked into the wizard `onFinish`/service. Any creatable surface without a React wizard hook is out of scope for creation-time mapping (it falls back to the manual push), NOT a candidate for a plugin.

**The seven services:**
- **Present today, wireable immediately** (5): `eventService`, `matterService`, `projectService`, `todoService`, `workAssignmentService`.
- **Arriving via unmerged branch** (2, decision 5a): `invoiceService` / `reportCardService` (+ their wizards). Wiring these two is **gated on that branch merging** to master + this worktree. Spec/tasks must sequence them after the merge and verify the services exist before wiring.

No behavior change when no profile exists for a pair (graceful no-op). Each service needs its own lookup-binding verification since payload shapes differ. **Target field names differ per entity** — e.g. Matter's `sprk_assignedattorney1` maps to Invoice's `sprk_assignedtoattorney1` (different name), and Invoice has **no law-firm fields** — so the seed matrix (§4.5) is authored per-pair against verified target schema, never assumed identical.

### 4.5 Configuration seed (decision 6 — the attorney matrix)

Two "SRFR-084 UAT" profiles exist in spaarkedev1 (Matter→Event, Project→Event) mapping only `description`/`priorityreason` — **not** the assigned-resource fields the owner tested. Plus one orphaned empty `sprk_fieldmappingrule` (junk — delete).

R2 **seeds** the real attorney matrix as config **data** (not schema, not code) so the owner's re-test succeeds out-of-the-box: for the Matter→(Event/Invoice/Report Card) pairs, Copy rules for the 8 assigned-resource lookups **where the corresponding target field exists** (verify per-target during spec/execution — Event/Invoice/Report Card may not all carry all 8). The two stale UAT profiles are deactivated or repurposed (§7 Q2). Thereafter the owner extends the matrix via the native MDA form (§5) — zero code change, since the engine reads whatever's configured.

## 5. Admin authoring — native forms, no new PCF

**Decision**: do not resurrect `FieldMappingAdmin`/`ProfileEditor`/`RulesList`/`RuleEditor`. Use native Dataverse forms:
- `sprk_fieldmappingprofile` main form: standard fields (Name, Source Record Type, Target Record Type) + an editable `sprk_fieldmappingrule` subgrid (Source Field, Target Field, Mapping Type, Default Value, Execution Order, Is Active).
- No custom PCF, no Code Page. This matches what BOTH prior projects independently concluded was the right MVP scope — only the actual Feb build drifted from it.
- If the subgrid's inline-editable UX proves inadequate after the owner tries it, a future phase can reconsider — but that's a "wait and see," not a day-one build.

**Component justification (CLAUDE.md §11)**:
1. **Existing** — native Dataverse subgrid-on-form authoring already exists as a platform capability; no code required.
2. **Extension** — N/A; nothing to extend, this is configuration, not code.
3. **Cost-of-doing-nothing** — none identified; every prior design (Feb's own README, r1's explicit decision) already agreed native forms are sufficient for this authoring volume (a handful of profiles, a handful of rules each).

## 6. Hot-path declaration (per CLAUDE.md §10)

```xml
<hot-path-declaration>
  <bff>Y</bff>                <!-- Additive contract extension: FieldMappingRuleDto gains mappingType/defaultValue/expression/isRequired/compatibilityMode; sprk_mapping_type + sprk_expression added to the rule $select. No new endpoint/service/DI/package. See §4.2 Placement Justification. -->
  <spaarkeAi>N</spaarkeAi>
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-CLAUDE-md>N</root-CLAUDE-md>
</hot-path-declaration>
```

**Justification for BFF=Y (decision 3)**: the design originally targeted BFF=N, but building all four mapping types (decision 2) requires the endpoint to return `mappingType` + the config fields the DTO currently drops. This is a **minimal, additive** change to an existing, stable contract already consumed by `UpdateRelatedButton` — no new endpoints, services, DI registrations, or packages; publish-size delta ≈ 0 (DTO fields only). `bff-extensions.md` pre-merge checklist applies. Full Placement Justification in §4.2. The BFF-touch obligations (§10 bullets 4-6: publish-size verification, CVE scan, test update for `Services/`/endpoint changes) are in scope for the BFF task.

## 7. Resolved decisions (formerly open questions)

1. **Field-mapping matrix** → RESOLVED (decision 6). Seed the **8 assigned-resource lookups** confirmed on `sprk_matter` (`sprk_assignedattorney1/2` & `paralegal1/2` → `contact`; `sprk_assignedlawfirm1/2` → `sprk_organization`; `sprk_assignedtoexternal/internal` → `contact`), for each Matter→target pair **where the target field exists** (verify per-target). Owner extends via native MDA form thereafter.
2. **Existing UAT test data** → RESOLVED. Deactivate (or repurpose) the two stale "SRFR-084 UAT" profiles; **delete** the orphaned empty `sprk_fieldmappingrule` record.
3. **BFF response shape** → RESOLVED. `GET profiles/{source}/{target}` returns `FieldMappingProfileWithRulesDto` with `Rules[]` already `$expand`-ed → **one call**. Verified in `FieldMappingEndpoints.cs`.
4. **Scope of wizard wiring** → RESOLVED (decision 5). Wire **all 7** services.
5. **Mapping-type coverage** → RESOLVED (decision 2). Implement **all four** (Copy/Default/Concat/Template). `sprk_mapping_type` confirmed present on the live table; `sprk_expression` added (decision 4) so Concat/Template are genuinely configurable, not aspirational.
6. **`sprk_todo` regarding-cascade** → NOTED. `TodoRegardingUpdateBuilder`/`TODO_REGARDING_CATALOG` (which entities a To Do's *regarding lookup* can point at) is a distinct mechanism from field-mapping inheritance. Spec.md MUST NOT conflate them; `todoService` wiring (decision 5) is field-mapping, orthogonal to the regarding catalog.
7. **UpdateRelatedButton smoke** → carry into spec as a verification step: confirm the existing manual push still works against the current BFF after the DTO extension (the additive fields must not break its existing deserialization).

## 8. Non-goals (reaffirmed + one clarified boundary)

- N:N inheritance semantics — out of scope.
- **Same-entity *update-time* cascade** (e.g. matter A saved → re-cascade to child matters → their children) — out of scope; this is the case that genuinely needs recursion-depth guards. Same-entity **creation-time** mapping IS in scope and is recursion-safe by construction (§10).
- Sync-mode extensibility (`Automatic` as a distinct sync mode) — deferred; R2 achieves the practical effect via direct wizard-service calls.
- Deprecating `RegardingResolver` or touching its picker UX — untouched.
- BFF Change Feed / Service Bus auto-cascade — architectural pivot, not this project.
- Rebuilding any of the 4 retired admin PCFs — see §5.
- Generalizing the manual-push `DetermineParentLookupField` convention for same-entity push — documented boundary (§10), not built.
- **Dataverse plugins / form scripts — never (owner constraint, absolute).** Creation-time mapping is client-side only. Entities created outside a React wizard (e.g. via extraction/native form) are not given creation-time mapping via a plugin; they rely on the manual push. This is a hard mechanism boundary, not a deferral.

## 9. Success criteria

1. [ ] A wizard-created Event/Invoice/Report Card, when an active profile exists for its (Matter|Project) → (target) pair, has **every mapped field — including lookup fields (attorney/paralegal/law-firm) via `@odata.bind`** — populated at creation, verified in Dataverse.
2. [ ] All four mapping types produce correct output: Copy (scalar + lookup), Default (literal), Concat + Template (`sprk_expression` placeholders resolved from the parent record).
3. [ ] No profile for a pair → wizard behaves exactly as today (graceful no-op, no error, no UI change).
4. [ ] **Same-entity (matter→matter) mapping works at creation-time**, proven by a positive test (field self-maps to same-named field on a different record) and a **negative test** guaranteeing no `source === target` guard exists in engine/BFF/seed.
5. [ ] `UpdateRelatedButton` → `/push` still works after the DTO extension (existing deserialization unaffected by the additive fields).
6. [ ] BFF change is additive-only: no new endpoint/service/DI/package (confirm via `git diff --stat`); publish-size delta reported per §10 bullet 4.
7. [ ] No new PCF controls built.
8. [ ] `sprk_expression` column added; `FieldMappingService.ts` is context-agnostic (no `ComponentFramework.WebApi` dependency); nav-prop discovery hoisted to `@spaarke/ui-components`.
9. [ ] Field Mapping Framework architecture doc + admin authoring guide published (decision 8).
10. [ ] Attorney matrix seeded; owner has reviewed/extended it before wrap-up.

## 10. Same-entity (matter→matter) mapping — supported by design (decision 9)

**Requirement framing**: not needed today, but the framework must not foreclose it — e.g. one matter is the parent of another matter and the child should inherit fields.

**Why it already works with zero engine change:**
- **Storage is pair-agnostic** — the profile's source/target are `sprk_recordtype_ref` lookups; nothing rejects `source == target`. `GET profiles/sprk_matter/sprk_matter` is a valid call.
- **The engine is generic** — it takes `(sourceEntity, sourceId, targetEntity, payload)`; matter→matter is `source === target === 'sprk_matter'`. `matterService` is in the wired set (decision 5).
- **Parent linkage already exists via polymorphic regarding, not a self-lookup** — the matter table has **no** `sprk_parentmatter` column but does have `sprk_regardingrecordtype` + `sprk_regardingrecordnumber` (the ADR-024 pattern this whole resolver ecosystem uses). "Matter B's parent is Matter A" is expressed the same canonical way as any other regarding relationship; the creating wizard supplies the parent id exactly as the Event wizard does.
- **Creation-time is recursion-safe by construction** — a single hop (parent → new child, applied once). No re-firing on updates ⇒ no chain reaction. This is why same-entity is safe here but deferred for update-time (§8).

**What the spec MUST enforce so it isn't accidentally foreclosed:**
- Acceptance criterion + **negative test**: no `source === target` guard in engine, BFF, or seed validation (§9 item 4).
- **Field self-mapping test**: a Copy rule mapping `sprk_practicearea → sprk_practicearea` (same name, different record) must apply, not no-op.

**Documented boundary (not built):** the manual-push `DetermineParentLookupField` convention (`_sprk_regarding{basename}_value`) would need generalizing for matter→matter *push*; update-time same-entity cascade is a non-goal (§8).

## 11. Documentation deliverables (decision 8)

- **New architecture doc** — Field Mapping Framework: the two tables, the BFF contract, the client engine, the **four mapping types + the `sprk_expression` extensibility model**, the creation-time-vs-update-time boundary, and the same-entity support/recursion note. Authored via `docs-architecture`.
- **Admin authoring guide** — how a maker authors profiles/rules in the native MDA form (§5), which mapping types are live, how to write a `sprk_expression` template, worked example (the attorney seed). Authored via `docs-guide`.
- Update root `CLAUDE.md` §17 pointer table + `.claude/patterns/` if a pointer file is warranted.
