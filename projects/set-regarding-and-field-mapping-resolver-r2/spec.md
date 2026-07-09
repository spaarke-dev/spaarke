# Set-Regarding and Field-Mapping Resolver — R2 — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-07-08
> **Source**: design.md (decisions 1-9 resolved during refinement)
> **Predecessor**: `set-regarding-and-field-mapping-resolver-r1` (PR #549) — amends one non-goal (Path B, §ADR Tensions)

## Executive Summary

R2 restores **automatic field inheritance at child-record creation time**: when a "+" wizard creates a child record from a Matter/Project (or another Matter), the mapped fields — including lookup fields like Assigned Attorney 1 — auto-populate from the parent. It does this with a **context-agnostic client engine** in `@spaarke/ui-components` that reads the existing Dataverse-configured field-mapping profiles (via one existing BFF call) and applies all four mapping types (Copy, Default, Concat, Template) onto the wizard's create payload. The capability was deleted as collateral damage in r1 (SRFR-045); R2 re-homes it correctly and permanently, closing the BFF contract and rule schema so the topic never reopens.

## Scope

### In Scope
- Rewrite the stubbed `FieldMappingService.ts` into a working, context-agnostic engine (no `ComponentFramework.WebApi` dependency).
- Implement **all four** `sprk_mapping_type` engines: Copy (scalar + lookup `@odata.bind`), Default/Constant, Concat, Template.
- Hoist nav-prop discovery (currently private to `eventService.ts`) to the shared lib for lookup binding.
- **Additive BFF contract extension** so `GET /api/v1/field-mappings/profiles/{source}/{target}` returns `mappingType`, `defaultValue`, `expression`, `isRequired`, `compatibilityMode`.
- **Additive Dataverse schema**: one new column `sprk_expression` (`NVARCHAR(2000)`) on `sprk_fieldmappingrule`.
- Wire the engine into **7 wizard services** (5 present now + invoice/reportCard gated on an unmerged branch).
- Seed the attorney/assigned-resource field-mapping matrix (config data), per-pair against verified target schema.
- Support **same-entity (matter→matter) creation-time mapping** with an explicit negative test against a `source === target` guard.
- Documentation: Field Mapping Framework architecture doc + admin authoring guide.

### Out of Scope
- **Dataverse plugins / form scripts — never** (owner constraint, absolute). Creation-time mapping is client-side only; entities without a React wizard hook rely on the existing manual push.
- Automatic cascade on **existing-record update** — stays manual-only via `UpdateRelatedButton` → `POST /push` (untouched).
- Same-entity **update-time** cascade (matter A save → re-cascade to child matters) — needs recursion guards; not built.
- New PCF controls; resurrecting the 4 retired admin PCFs (native MDA form is the authoring surface).
- New BFF endpoint / service / DI registration / package; N:N inheritance; `Automatic` sync-mode value.
- Generalizing the push `DetermineParentLookupField` convention for same-entity push.

### Affected Areas
- `src/client/shared/Spaarke.UI.Components/src/services/FieldMappingService.ts` — rewrite (the engine).
- `src/client/shared/Spaarke.UI.Components/src/types/FieldMappingTypes.ts` — align types to the four engines + BFF DTO shape.
- `src/client/shared/Spaarke.UI.Components/src/components/CreateEventWizard/eventService.ts` (+ `matter`/`project`/`todo`/`workAssignment` services) — wire the engine; hoist `_discoverNavProps`/`_findNavProp` out to a shared util.
- `src/client/shared/Spaarke.UI.Components/src/components/CreateRecordWizard/` — the reusable wizard whose entity `onFinish` is the integration point.
- `invoiceService` / `reportCardService` (arriving via unmerged branch) — wire after merge.
- `src/server/api/Sprk.Bff.Api/Models/FieldMapping/FieldMappingRuleDto.cs` + `Api/FieldMappings/FieldMappingEndpoints.cs` (`MapRuleEntityToDto`) — additive DTO fields.
- `src/server/shared/Spaarke.Dataverse/Models.cs` (`FieldMappingRuleEntity`) + `DataverseWebApiService.cs` (rule `$select` + `MapToFieldMappingRuleEntity`) — read `sprk_mapping_type` + `sprk_expression`.
- `sprk_fieldmappingrule` (Dataverse) — new `sprk_expression` column; profile/rule data seed.
- `docs/architecture/` + `docs/guides/` — new framework doc + admin guide.

## Requirements

### Functional Requirements

1. **FR-01 (Engine rewrite, context-agnostic)**: Replace the stubbed `FieldMappingService.ts` with a working engine that depends only on `IDataService` + `authenticatedFetch` (shared-lib contracts), not `ComponentFramework.WebApi`. — Acceptance: no `ComponentFramework` import in the file; ADR-012 compliance; existing barrel exports still resolve.
2. **FR-02 (Single BFF read + graceful 404)**: The engine fetches the profile via one call to `GET /api/v1/field-mappings/profiles/{sourceEntity}/{targetEntity}` (rules `$expand`-ed). — Acceptance: exactly one BFF call per apply; 404 → returns `{ profileFound: false }`, no throw, no UI change.
3. **FR-03 (Copy — scalar)**: A Copy rule assigns the parent's source-field value to the child payload's target field. — Acceptance: scalar source→target populates on the created record.
4. **FR-04 (Copy — lookup via `@odata.bind`)**: A Copy rule on a lookup target writes `navProp@odata.bind = /entitySet(guid)`, resolving the referent entity from the source `_value` field's `@Microsoft.Dynamics.CRM.lookuplogicalname` annotation and discovering the target nav-prop. — Acceptance: Matter `sprk_assignedattorney1` (→contact) populates the target's attorney lookup on the created record, verified in Dataverse.
5. **FR-05 (Default/Constant)**: A Default (`sprk_mapping_type = 1`) rule writes the `sprk_defaultvalue` literal to the target field, ignoring the source. — Acceptance: literal appears on the created record.
6. **FR-06 (Concat)**: A Concat (`= 2`) rule resolves the `sprk_expression` format string, substituting `{sprk_field}` placeholders from the parent record, into a text/memo target. — Acceptance: `{sprk_matternumber} - {sprk_mattername}` yields the joined string; missing placeholder → warning + omit, not a throw.
7. **FR-07 (Template)**: A Template (`= 3`) rule uses the same placeholder resolver as Concat against `sprk_expression`. — Acceptance: template string with fixed scaffold + placeholders resolves correctly.
8. **FR-08 (Nav-prop discovery hoisted + shared)**: `_discoverNavProps`/`_findNavProp` move from `eventService.ts` to a shared `@spaarke/ui-components` utility consumed by the engine and all wired services. — Acceptance: one implementation; `eventService` regarding-link behavior unchanged.
9. **FR-09 (Result contract, never throws)**: The engine returns `{ profileFound, fieldsMapped, warnings }`. Type-incompatibility, missing source field, or unresolved placeholder produce a non-fatal warning + skip that rule and never abort record creation. — Acceptance: injected failure → record still created; warning surfaced via the wizard's existing warnings array.
10. **FR-10 (BFF DTO extension — additive)**: `FieldMappingRuleDto` gains `mappingType`, `defaultValue`, `expression`, `isRequired`, `compatibilityMode`; `MapRuleEntityToDto` populates them; `DataverseWebApiService` rule `$select` + `FieldMappingRuleEntity` + `MapToFieldMappingRuleEntity` read `sprk_mapping_type` + `sprk_expression`. — Acceptance: endpoint returns the new fields; no new endpoint/service/DI/package; `git diff --stat` shows only additive changes.
11. **FR-11 (Schema add — additive)**: Add nullable `sprk_expression` (`NVARCHAR(2000)`) to `sprk_fieldmappingrule` via `dataverse-create-schema`. — Acceptance: column exists in spaarkedev1; no existing rule modified; `sprk_defaultvalue` unchanged.
12. **FR-12 (Wire present wizards)**: Call the engine from `onFinish`/service create paths for `event`, `matter`, `project`, `todo`, `workAssignment`. — Acceptance: each populates mapped fields at creation when a profile exists; graceful no-op otherwise.
13. **FR-13 (Wire invoice/reportCard — gated)**: After the invoice/reportCard wizard branch merges to master + this worktree, wire `invoiceService`/`reportCardService`. — Acceptance: services confirmed to exist before wiring; both populate mapped fields at creation; task is blocked (not skipped) if the merge hasn't landed.
14. **FR-14 (Seed the matrix — config data, per-pair)**: Seed profile + Copy rules for the assigned-resource lookups for each Matter→(target) pair, mapping **verified** source→target logical names (e.g. Matter `sprk_assignedattorney1` → Invoice `sprk_assignedtoattorney1`; omit fields absent on a target, e.g. Invoice has no law-firm). Deactivate/repurpose the two stale "SRFR-084 UAT" profiles; delete the orphaned empty rule. — Acceptance: a wizard-created Event/Invoice inherits the seeded attorney fields; seed authored against `describe`-verified target schema.
15. **FR-15 (Same-entity support + negative test)**: Matter→matter creation-time mapping works; no `source === target` guard exists in engine/BFF/seed; a Copy rule mapping a field to the same-named field on a different record applies (not a no-op). — Acceptance: positive matter→matter test passes; negative test asserts no same-entity guard.
16. **FR-16 (Push regression)**: `UpdateRelatedButton` → `/push` still works after the DTO extension (additive fields don't break its deserialization). — Acceptance: smoke test of the existing push path passes.
17. **FR-17 (Documentation)**: Publish a Field Mapping Framework architecture doc (two tables, BFF contract, client engine, four mapping types, `sprk_expression` extensibility model, creation-vs-update boundary, same-entity note) and an admin authoring guide (native MDA form, mapping types, `sprk_expression` templates, attorney-seed worked example). — Acceptance: both docs exist; root `CLAUDE.md` §17 pointer table updated.

### Non-Functional Requirements
- **NFR-01 (BFF publish size)**: Report compressed publish size + delta vs baseline (~49.63 MB incl. PDBs) on the BFF-touching task. Expected delta ≈ 0 (DTO fields only). Ceiling ≤60 MB; escalate at +5 MB single-task / 55 MB cumulative.
- **NFR-02 (BFF additive-only)**: No new endpoint, service, DI registration, or package in `Sprk.Bff.Api`. Hot-path BFF=Y is justified by the additive DTO extension only (§ADR Tensions).
- **NFR-03 (No plugins)**: No Dataverse plugin or form script is created (owner constraint, absolute).
- **NFR-04 (Context-agnostic)**: Shared-lib code contains no PCF/`ComponentFramework` types (ADR-012).
- **NFR-05 (Graceful degradation)**: No profile, missing field, or mapping failure is always non-fatal — record creation proceeds; failures become warnings.
- **NFR-06 (No new PCF)**: No PCF control built.
- **NFR-07 (Test obligation)**: Per §10 bullet 6, BFF `Services/`/DTO changes add/update tests in `tests/unit/Sprk.Bff.Api.Tests/`; engine gets unit tests for all four mapping types + lookup binding + same-entity + graceful-degradation.

## Technical Constraints

### Applicable ADRs
- **ADR-024** (Polymorphic Resolver Pattern) — `sprk_recordtype_ref` authoritative; matter-as-parent linkage via polymorphic regarding (basis for same-entity support).
- **ADR-012** (Shared Component Library) — engine must be context-agnostic (FR-01, NFR-04).
- **ADR-001 / ADR-008 / ADR-010 / ADR-019** (Minimal API, endpoint filters, DI minimalism, ProblemDetails) — the additive BFF change stays within existing endpoint/DI patterns.
- **ADR-002** (Plugins) — explicitly NOT used; complied-with by avoidance per owner constraint (NFR-03).

### MUST Rules
- ✅ MUST reuse the existing `GET profiles/{source}/{target}` endpoint; MUST NOT add new BFF surface beyond additive DTO fields.
- ✅ MUST keep the engine non-throwing; mapping failures MUST be warnings.
- ✅ MUST build lookup targets as `@odata.bind` (payload model, not form binding).
- ✅ MUST author the seed per-pair against `describe`-verified target field names.
- ❌ MUST NOT introduce a `source === target` guard anywhere.
- ❌ MUST NOT create a Dataverse plugin or form script.
- ❌ MUST NOT modify the manual push behavior.

### Existing Patterns to Follow
- `eventService.ts` lines ~316-336 — lookup `@odata.bind` construction pattern (source of the hoisted nav-prop discovery).
- `applyResolverFields` (NFR-06 graceful-blank convention) — the non-fatal warning model the engine mirrors.
- `FieldMappingEndpoints.cs` `MapRuleEntityToDto` / `DataverseWebApiService.cs` rule `$select` — the additive extension sites.
- `.claude/constraints/bff-extensions.md` — the binding pre-merge checklist for the BFF task.

## ADR Tensions (per CLAUDE.md §6.5 — MANDATORY)

| ADR / prior decision | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| r1 design §8 non-goal | "Automatic cascade … manual (ribbon button) only" | Owner requires mapped fields to auto-populate at wizard creation | **B (amendment)** | Creation-time has no pre-existing target data to protect — none of r1's overwrite risk applies. Amend the non-goal to scope creation-time cascade IN; update-time stays manual. Accepted by owner 2026-07-08. |
| ADR-013 / §10 BFF Hygiene | New BFF work requires placement justification; project targeted BFF=N | Building 4 mapping types needs the endpoint to return `mappingType` + config fields the DTO drops | **A (project-scoped exception, documented)** | The change is minimal + additive to an existing stable contract already consumed by `UpdateRelatedButton`; server already reads the fields. Alternative (client-side Dataverse query) duplicates read logic + violates §11. Placement Justification in design §4.2; publish-size delta ≈ 0. |
| ADR-002 (Plugins) | Server-side record-event logic pattern | Invoice created via extraction/native form has no client hook | **C (comply — by avoidance)** | Owner constraint forbids plugins absolutely. Invoice creation-time mapping is delivered via its (incoming) React wizard, not a plugin; any hookless surface falls back to manual push. |

## Success Criteria
1. [ ] Wizard-created Event/Invoice, with an active profile for its Matter→target pair, has every mapped field — **including attorney/paralegal/law-firm lookups via `@odata.bind`** — populated at creation. Verify: inspect the created record in Dataverse.
2. [ ] All four mapping types produce correct output (Copy scalar + lookup, Default literal, Concat + Template from `sprk_expression`). Verify: unit tests + one live record per type.
3. [ ] No profile for a pair → wizard behaves exactly as today (no error, no UI change). Verify: create with no profile configured.
4. [ ] Same-entity (matter→matter) mapping works; no `source === target` guard. Verify: positive self-map test + negative guard test.
5. [ ] `UpdateRelatedButton` → `/push` still works post-DTO-extension. Verify: push smoke test.
6. [ ] BFF change additive-only (no new endpoint/service/DI/package); publish-size delta reported. Verify: `git diff --stat` + publish measurement.
7. [ ] No Dataverse plugin/form script created; no new PCF control. Verify: repo diff.
8. [ ] `sprk_expression` column exists; `FieldMappingService.ts` has no `ComponentFramework` dependency; nav-prop discovery hoisted. Verify: schema check + import scan.
9. [ ] Architecture doc + admin guide published; `CLAUDE.md` §17 updated. Verify: files exist + pointer present.
10. [ ] Attorney matrix seeded per-pair against verified schema; stale UAT profiles handled; orphan rule deleted. Verify: query `sprk_fieldmappingprofile`/`sprk_fieldmappingrule`.

## Dependencies

### Prerequisites
- **Invoice/Report Card wizard branch merged** to master + this worktree before FR-13 executes. The 5 present services (event, matter, project, todo, workAssignment) are unblocked immediately.
- `dataverse-create-schema` access to spaarkedev1 for the `sprk_expression` column + data seed.

### External
- Live BFF `/api/v1/field-mappings/*` (already deployed) — the engine's read path.

## Owner Clarifications

| Topic | Question | Answer | Impact |
|---|---|---|---|
| Mapping types | Build only Copy, or all four? | Build all four for real (Copy/Default/Concat/Template) | 4-engine build + `sprk_expression` schema add; BFF=Y |
| Constant vs Copy | Is a fixed-default (Constant) needed? | Yes — enable Constant + others; don't reopen later | Default engine + contract carries mappingType permanently |
| Concat/Template config | 100-char `defaultvalue` too small — add a wider column? | Yes, extend (`sprk_expression`) | New `NVARCHAR(2000)` column (FR-11) |
| Wiring scope | 3 UAT wizards or all services? | All 7 (invoice/reportCard arriving via unmerged branch) | FR-12 (5 now) + FR-13 (2 gated) |
| Field matrix | Seed attorney set, or owner configures? | Start with attorney fields; R2 seeds; config is admin data not code | FR-14 seed; owner extends via MDA form |
| Invoice mechanism | Plugin for native-form invoice creation? | Wizard will be available. **Never create a Dataverse plugin.** | NFR-03 absolute; invoice via its React wizard only |
| matter→matter | Must the framework support same-entity mapping? | Yes, ensure supportable (not required today) | FR-15 + negative test |

## Assumptions
- **Report Card entity/wizard**: assumed to arrive on the same unmerged branch as Invoice, with its own assigned-resource fields; exact field logical names verified at seed time (per-target).
- **Concat/Template semantics**: assumed identical placeholder-resolution engine, differing only by author intent (Concat = joined fields; Template = scaffold + fields). One resolver serves both.
- **Seed target coverage**: assumed only fields present on a given target are seeded (verified via `describe`); absent fields (e.g. Invoice law-firm) are simply not mapped.

## Unresolved Questions
- [ ] **Report Card field schema** — exact assigned-resource field logical names on the report-card target are unknown until its branch merges. Blocks: FR-13/FR-14 report-card seed rows (verify via `describe` post-merge).
- [ ] **Invoice/Report Card wizard `onFinish` shape** — confirm the incoming services expose a create path the engine can hook the same way as `eventService` before wiring. Blocks: FR-13 (verify post-merge, do not assume).

---
*AI-optimized specification. Original design: design.md*
