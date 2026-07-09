# Set-Regarding and Field-Mapping Resolver — R2

> **Status**: DRAFT — pending spec authoring
> **Created**: 2026-07-08
> **Owner**: Ralph Schroeder
> **Related ADRs**: ADR-024 (Polymorphic Resolver Pattern), ADR-012 (Shared Component Library), ADR-006 (PCF over Webresources), ADR-001 (Minimal API)
> **Predecessor**: `set-regarding-and-field-mapping-resolver-r1` (2026-07-01 → merged as PR #549) — this project **amends one of r1's explicit non-goals**; see §2.
> **Triggered by**: UAT on `visual-host-create-button-r1` (2026-07-08) — owner expected Assigned Attorney 1 (and other mapped fields) to auto-populate from Matter when a wizard creates an Event/Invoice/Report Card. It doesn't, because the runtime engine that used to do this was deleted as unrelated collateral damage during r1.

## 1. Problem statement

`visual-host-create-button-r1`'s UAT surfaced that no field values are inherited from a host record (Matter/Project) into a child record created by the "+" wizards (Event, Invoice, Report Card). Investigation traced the full history:

1. **`events-and-workflow-automation-r1`** (Feb 2026) built a complete, working Field Mapping Framework: two Dataverse tables (`sprk_fieldmappingprofile`, `sprk_fieldmappingrule`), a BFF API (`/api/v1/field-mappings/*`, fully implemented — no stubs remain), 4 admin PCF controls (Field Mapping Admin, Profile Editor, Rules List, Rule Editor), and a client-side engine (`FieldMappingHandler.ts`, 547 lines) embedded in the **`AssociationResolver`** PCF that auto-applied mappings **the moment a child record's regarding association was set** ("child-write-time cascade").

2. **`set-regarding-and-field-mapping-resolver-r1`** (July 2026) explicitly scoped automatic cascade OUT (§8 Non-goals: *"Automatic cascade-on-parent-save — deferred. This project ships manual (ribbon button) only."*) in favor of a **manual, ribbon-triggered push** (`UpdateRelatedButton` PCF → `POST /api/v1/field-mappings/push`) — a deliberate, reasoned decision for **already-existing** child records (predictability/auditability: don't silently overwrite a user's edits on save).

3. **SRFR-045** (a sub-effort inside/after r1, 2026-07-05) retired `AssociationResolver` PCF entirely in favor of `RegardingResolver` v1.4.0, on the reasoning that AssociationResolver's picker duty was 100% redundant with RegardingResolver, and its field-mapping duty was "redundant with parent-side push" (per ADR-024's revision log). **This is where the gap was actually created**: the "auto-apply at creation time" capability was deleted as collateral damage of a picker consolidation, without anyone weighing whether *creation-time* cascade is the same risk profile as *update-time* cascade (it is not — see §2).

4. **Today**, the shared lib's `FieldMappingService.ts` (`src/client/shared/Spaarke.UI.Components/src/services/`) is a *different*, never-finished implementation — every Dataverse-querying method is stubbed to return empty results. It was never connected to the real (Feb-era) engine and is dead code today.

**Net effect**: none of `eventService`, `invoiceService`, `reportCardService` (or `matterService`, `projectService`, `workAssignmentService`, `todoService`) call anything that applies field mappings. The two tables and the BFF API are live and functional; nothing invokes the "get profile for this entity pair" path at record-creation time.

## 2. The core design tension — amending r1's non-goal (CLAUDE.md §6.5, Path B)

🔔 **ADR/Design Conflict — Resolution Required**

- **Prior decision**: r1 design.md §8: *"Automatic cascade-on-parent-save — deferred. This project ships manual (ribbon button) only."*
- **Conflict**: The owner's current, explicit requirement (2026-07-08) is that mapped fields **must** auto-populate when a wizard creates a new Event/Invoice/Report Card from a Matter/Project.
- **Why creation-time is a different risk profile than update-time**: r1's caution (manual/predictable/auditable) was motivated by **overwrite risk on existing records** — a "push" that silently clobbers a user's already-edited child field is a real hazard. A **brand-new record being created by a wizard has no existing field values to protect** — the target fields are empty by construction. Applying mappings once, at the moment of creation, carries none of the overwrite risk r1 was guarding against.
- **Proposed path**: **B (amendment)** — narrowly. Amend r1's non-goal to read: *"Automatic cascade on **existing-record update** — still deferred, still manual-only via the ribbon push. Automatic cascade **at child-record creation time** — in scope as of R2, since there is no pre-existing target data to protect."* The manual ribbon-push (`UpdateRelatedButton` → `/push`) is **unchanged** and remains the only mechanism for refreshing already-existing children.
- **Rationale for Path B over Path A (exception) or C (comply)**: This isn't a narrow, one-off deviation (Path A) — it's a general capability every wizard-driven creation flow needs, present or future. And Path C (comply with the existing non-goal) means silently accepting that Assigned Attorney 1 (and everything else) never inherits from Matter into Event/Invoice/Report Card, which contradicts the owner's explicit, current requirement.
- **Impact if accepted**: `eventService`/`invoiceService`/`reportCardService` (and optionally `matterService`/`projectService`/`workAssignmentService`/`todoService`, see §7 open question) gain one additional call before `createRecord` — no schema change, no new BFF endpoint (see §3).
- **Alternative considered (and rejected)**: Leave it manual-only and have the owner click "Push Updates to Related Records" immediately after every wizard-created child. Rejected — defeats the purpose of a "+" button that's supposed to auto-associate *and* auto-populate in one step; adds a mandatory extra click to every single wizard completion.

## 3. Guiding principles (carried forward from r1, still true) + one addition

- **Do not rebuild what already works.** The BFF `/api/v1/field-mappings/*` endpoints are live, fully implemented (no STUB markers), and already consumed by `UpdateRelatedButton`. R2 **reuses** `GET /api/v1/field-mappings/profiles/{sourceEntity}/{targetEntity}` — it does not re-implement profile/rule querying client-side, and it does not add new BFF surface. This keeps the hot-path declaration at **BFF=N** (see §6), consuming an existing, stable contract exactly as r1 did for the ribbon button.
- **Data-driven, not code-driven, for entity metadata.** `sprk_recordtype_ref` remains authoritative (already used by the BFF endpoints, already used throughout this codebase's resolver ecosystem).
- **Configuration stays in Dataverse tables.** `sprk_fieldmappingprofile` / `sprk_fieldmappingrule`, unchanged schema.
- **NEW: prefer native Dataverse forms over custom PCFs for admin authoring** — this reverses the Feb 2026 project's actual build (4 custom PCFs), which contradicted *both* the Feb project's own original README (*"Native Dataverse forms for admin configuration (no PCF required)"*) *and* r1's own explicit decision (*"Not in scope: Custom PCF or Code Page for authoring... MVP is native MDA form"*). Two independent prior decisions agreed on native forms; only the actual Feb build drifted from both. R2 does not resurrect Field Mapping Admin / Profile Editor / Rules List / Rule Editor — see §5.
- **Manual push for existing records is untouched.** `UpdateRelatedButton` → `/push` keeps doing exactly what it does today for already-existing children.

## 4. What R2 actually builds

### 4.1 Client-side apply-at-creation helper (the core deliverable)

Replace the shared lib's stubbed `FieldMappingService.ts` with a **thin, working implementation** that:

1. Calls the existing BFF `GET /api/v1/field-mappings/profiles/{sourceEntity}/{targetEntity}` (via `authenticatedFetch`, already available in every wizard service's `WizardHostProps`) to get the active profile + ordered rules for a source/target entity pair. No profile found → no-op, return gracefully (matches every other resolver-pattern graceful-blank convention in this codebase, e.g. `applyResolverFields`'s NFR-06 handling).
2. If a profile is found, fetches the needed source fields from the **already-resolved parent record** via the wizard's existing `IDataService` (no new query pattern — every wizard service already knows the parent entity/id from the Associate-To step).
3. Applies each rule (`Copy` or `Constant` mapping type, matching the live schema's `sprk_mapping_type` choice values) onto the target record payload, in execution order — same algorithm as the deleted `FieldMappingHandler.applyMappings()`, just re-homed to the shared lib instead of a PCF-local file (closing r1's own open question #7: *"should FieldMappingHandler move to `@spaarke/ui-components` too? — probably yes for symmetry with `PolymorphicResolverService`"*).
4. Returns a result object (`{ profileFound, fieldsMapped, warnings }`) — never throws; mapping failures are non-fatal warnings appended to the wizard's existing warnings array, same pattern as `applyResolverFields`.

This is a **much smaller build than re-creating `FieldMappingHandler.ts` from scratch** — the profile/rule Dataverse-querying logic is already live on the BFF; the shared-lib function only needs to call it, fetch parent field values, and apply them to a payload object. Estimated surface: one new/rewritten service file (~150-200 lines, well under the deleted engine's 547 because there's no `Xrm.Page` form-binding logic to port — our wizards build a payload object, they don't set values on an open classic form).

### 4.2 Wiring into wizard services

Call the new helper immediately before `createRecord`, in:
- `eventService.createEvent` (Matter/Project → Event)
- `invoiceService.createInvoice` (Matter/Project → Invoice)
- `reportCardService.createReportCard` (Matter/Project → Report Card)

Pass the resolved parent entity/id (already available — the same values passed into `applyResolverFields`). No behavior change when no profile exists for a pair (today: all pairs except Matter→Event and Project→Event).

### 4.3 Configuration audit (not a rebuild)

Two profiles already exist, live in spaarkedev1, both labeled "UAT (SRFR-084)":

| Profile | Rules |
|---|---|
| Matter → Event cascade UAT | `sprk_mattername → sprk_description`, `sprk_matternumber → sprk_priorityreason` |
| Project → Event cascade UAT | `sprk_projectname → sprk_description`, `sprk_projectnumber → sprk_priorityreason` |

Neither maps Assigned Attorney/Paralegal/Law Firm — the fields the owner actually tested. There's also one orphaned, empty `sprk_fieldmappingrule` record (no profile attached — junk, candidate for cleanup). **R2 does not guess at the "real" mapping matrix** — see §7 open question. Once the owner supplies the field list, R2 either seeds the profile/rule records directly (`dataverse-create-schema`-style data seed, not schema) or the owner configures them via the native MDA form (§5) — owner's choice, cheap either way since the runtime engine reads whatever's configured.

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
  <bff>N</bff>                <!-- Consumes an existing, stable BFF endpoint (GET profiles/{source}/{target}); no new services, DI, or packages added to Sprk.Bff.Api -->
  <spaarkeAi>N</spaarkeAi>
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-CLAUDE-md>N</root-CLAUDE-md>
</hot-path-declaration>
```

**Justification for BFF=N**: identical reasoning to r1's own justification for the ribbon button — `/api/v1/field-mappings/profiles/{sourceEntity}/{targetEntity}` already exists, is fully implemented, and is already consumed by `UpdateRelatedButton`. R2 adds a second client (the wizard services) to an existing, stable contract. If spec authoring reveals the GET endpoint's response shape doesn't cleanly support what the client needs (e.g., it doesn't currently return rule details, only profile metadata — **verify this during spec authoring**, it's an open question below), a minimal additive change to that one endpoint may be needed; if so, this declaration flips and `bff-extensions.md` criteria apply.

## 7. Open questions (to resolve during spec authoring)

1. **The field-mapping matrix**: which specific fields should map from Matter/Project into Event/Invoice/Report Card? The owner mentioned Assigned Attorney 1 as the concrete UAT example — is the full intended set the 8 assigned-resource fields (Attorney 1/2, Paralegal 1/2, Law Firm variants, Assigned External/Internal) mirroring what Report Card's Enter Info step already collects manually? Or a different/smaller set? **Needs an owner-provided manifest**, same pattern as the Phase-0 field manifests in `visual-host-create-button-r1`.
2. **Existing UAT test data**: deactivate the two "SRFR-084 UAT" profiles (Matter→Event, Project→Event mapping description/priorityreason) since they don't reflect real intended mappings, or repurpose/extend them? Delete the orphaned empty rule record either way.
3. **BFF response shape verification**: does `GET /api/v1/field-mappings/profiles/{sourceEntity}/{targetEntity}` return the profile **with its rules already expanded**, or just profile metadata (requiring a second call)? Read `FieldMappingEndpoints.cs` + `FieldMappingProfileWithRulesDto.cs` during spec authoring to confirm — this determines whether the client-side helper needs one BFF call or two.
4. **Scope of wizard wiring**: this design scopes the wiring to the three `visual-host-create-button-r1` wizards (Event/Invoice/Report Card). Should R2 also wire `matterService`/`projectService`/`workAssignmentService`/`todoService` for full-repo consistency, or is that explicitly deferred to keep R2 scoped? (Recommendation: wire all of them — the helper is generic and each of these services already has the same "resolved parent + about to createRecord" shape; the marginal cost per additional service is small once the helper exists.)
5. **Mapping-type coverage**: live schema shows `sprk_mapping_type` choice options `Copy (0) / Default (1) / Concat (2) / Template (3)`. The deleted engine only implemented `Copy` and a `Constant`-style default (`mappingType === 1` in the old code, which doesn't cleanly line up with the CURRENT choice set's `Default`/`Concat`/`Template` — the schema evolved since Feb). Confirm during spec authoring which mapping types R2 must actually implement (Copy is clearly required; Concat/Template may be aspirational schema not yet exercised by any real rule — check before building).
6. **`sprk_todo`'s own regarding-cascade**: `TodoRegardingUpdateBuilder`/`TODO_REGARDING_CATALOG` (built by `visual-host-create-button-r1` task 040) is a *different* mechanism (which entities a To Do's regarding lookup can point at) — unrelated to field-mapping inheritance. Confirm no naming/concept confusion carries into spec.md.
7. **UpdateRelatedButton status check**: confirm it's still fully functional against the current BFF (quick smoke, not a rebuild) — no changes anticipated, but r1 predates several schema/API changes since.

## 8. Non-goals (unchanged from r1, reaffirmed)

- N:N inheritance semantics — still out of scope.
- Sync-mode extensibility (`Automatic` as a distinct sync mode) — still deferred; this project achieves the practical effect via direct wizard-service calls, not a new `sprk_syncmode` value.
- Deprecating `RegardingResolver` or touching its picker UX — untouched.
- BFF Change Feed / Service Bus auto-cascade — architectural pivot, not this project.
- Rebuilding any of the 4 retired admin PCFs — see §5.

## 9. Success criteria (draft — refine during spec authoring)

1. [ ] A wizard-created Event/Invoice/Report Card, when an active field-mapping profile exists for its (Matter|Project) → (target) pair, has every mapped field populated at creation — verified by inspecting the created record in Dataverse.
2. [ ] No profile for a pair → wizard behaves exactly as it does today (graceful no-op, no error, no UI change).
3. [ ] `UpdateRelatedButton` → `/push` (existing, manual, for already-existing children) is unaffected.
4. [ ] No new BFF endpoints, services, or packages added (BFF=N holds — confirm via `git diff --stat`).
5. [ ] No new PCF controls built.
6. [ ] Owner has reviewed and either configured or delegated configuration of the real field-mapping matrix (§7 Q1) before this project's wrap-up.
