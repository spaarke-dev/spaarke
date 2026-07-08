# Lessons Learned — set-regarding-and-field-mapping-resolver-r1

**Project Duration**: 2026-07-02 → 2026-07-08 (~7 days)
**Scope**: Consolidate two overlapping cross-entity utility PCFs (RegardingResolver + AssociationResolver), close field-mapping cascade path (Matter/Project → child rows), add `sprk_regardingrecordnumber` to 10 entities (ADR-024 extended 4 → 5 field write), retire hardcoded ENTITY_LOOKUP_CONFIGS, ship RegardingResolver v1.4.6 with subgrid auto-detect.
**Delivered**: RegardingResolver PCF v1.2.0 → v1.4.6 (11 iterations), FieldMappingPushUpdate webresource v1.0.0 → v1.1.0, BFF schema-realigned queries, ADR-024 amended (5-field baseline + retirement of AssociationResolver).

---

## Executive Summary

Nearly the entire project's iteration burn (~40% of the effort) came from **defects discovered only when features hit real Dataverse metadata**. Every one of them was invisible during shared-lib unit tests + PCF harness tests because the tests mocked metadata that in production either doesn't exist at all or has drift from the code's assumptions. The single most-impactful class of lesson is: **write integration tests that exercise real Dataverse metadata queries, not just field-write payloads.**

Below: 10 concrete lessons, each with the failure, the fix, and the future-proofing rule.

---

## L-1. Dataverse metadata endpoints silent-empty on wrong case (SRFR-050)

**What happened**: Owner placed RegardingResolver on `sprk_communication`. Console warnings said "No nav-prop for sprk_matter" + "No nav-prop for sprk_recordtype_ref". Cascade + writes silently failed.

**Root cause**: `discoverHostNavProps` builds a URL like `/api/data/v9.0/EntityDefinitions(LogicalName='sprk_communication')/ManyToOneRelationships`. If the maker typed the wrong case in the manifest `entity` input (e.g., `sprk_Communication`), the Dataverse metadata endpoint returns **200 with EMPTY results** — NOT 404. Silent failure downstream.

**Fix**: v1.4.2 lowercase `hostEntity` before URL construction + add a diagnostic warning when discovery returns zero entries.

**Rule**: **Always lowercase-normalize entity logical names before any Dataverse metadata / OData URL construction.** Metadata endpoints are case-sensitive but non-diagnostic on mismatch. Any silent-empty response through a `map/filter/find` pipeline warrants an explicit "0 results — verify config" warning.

---

## L-2. Mutual-exclusivity clear-loop must be schema-membership-limited (SRFR-048)

**What happened**: `sprk_event` placement of RegardingResolver → `updateRecord failed: Invalid property 'sprk_regardingcommunication'`.

**Root cause**: The clear loop iterated the full 11-entity `TODO_REGARDING_CATALOG` to null "other" lookups per FR-13 mutual-exclusivity. But `sprk_event` only carries 4 parent lookups (Matter/Project/Invoice/WorkAssignment), not all 11. When `navProps.find(...)` returned undefined for a lookup that doesn't exist on the host, the code fell back to `other.lookupAttribute` (the catalog literal `sprk_regardingcommunication`) and wrote `@odata.bind = null` on a field that doesn't exist → Dataverse 400.

**Fix**: v1.4.1 changed the clear loop to skip entries where the discovered nav-prop is undefined. `navProps` is the DISCOVERED host schema — if a lookup exists on the host, its nav-prop will be present.

**Rule**: **Iteration for "clear other lookups" must filter by discovered schema membership, not catalog membership.** The catalog is superset (all possible parents); host-specific schema is subset. Fallback to catalog literals when nav-prop is missing = writing invalid columns.

---

## L-3. Wave 1 schema batches leave subset gaps for non-canonical entities (SRFR-049)

**What happened**: Owner placed RegardingResolver on `sprk_reportcard`. First selection → `Invalid property 'sprk_regardingrecordurl'`. This was after Wave 1 SRFR-010 "added the 5-field resolver set to 10 entities" and Wave 2's clear-loop fix was in place.

**Root cause**: `sprk_reportcard` was NOT one of the 10 entities in SRFR-010's batch. Someone (prior to this project) had added 4 of the 5 fields to sprk_reportcard but skipped `sprk_regardingrecordurl`. Wave 1 assumed the 10 entities covered the child-form scope, but the actual scope refined mid-project (SRFR-049 scope adjustment: Invoices dropped, KPI Assessments → ReportCards).

**Fix**: MCP-added `sprk_regardingrecordurl` to `sprk_reportcard` inline during UAT.

**Rule**: **Verify per-entity field coverage AT PLACEMENT TIME, not at batch-write time.** A schema-batch "10 entities" contract is a snapshot; entities added later, or existing "partially-migrated" entities, silently break the resolver. A pre-flight `SELECT sprk_regarding{5-field-set} FROM {entity}` before every new placement catches gaps.

---

## L-4. Auth gates hide subsequent bugs indefinitely (SRFR-053 + SRFR-056)

**What happened**: `sprk_fieldmapping_push.js` was authored in Wave 6 (SRFR-061) with `fetch(url, { credentials: 'include' })` — cookie-based auth. Owner UAT hit 401. After fixing auth (SRFR-053), the next call revealed **BFF 500 due to schema drift** (SRFR-056): BFF code queried `sprk_sourceentity` + `sprk_targetentity` + `sprk_isactive` — fields that have **never existed** on the profile entity in any version.

**Root cause**: The 401 auth wall blocked the endpoint before schema-drift caught it. When you don't reach the endpoint end-to-end, you don't discover its assumption errors. This particular BFF code drift went undetected for the entire Wave 6 → Wave 8 UAT window.

**Fix**: v1.1.0 MSAL Bearer + rewrote DataverseWebApiService.cs to use lookup-based queries (`_sprk_sourcerecordtype_value` + `_sprk_targetrecordtype_value` via `sprk_recordtype_ref` catalog).

**Rule**: **A working auth path is a prerequisite for any real integration testing.** Never merge a webresource that calls the BFF without a working token-acquisition path (MSAL silent → SSO → popup). And **never accept UAT PASS on a call that returns 401** — the 401 is masking the true state of the endpoint.

---

## L-5. Async gates on setValue writes race the user-save (SRFR-057)

**What happened**: Owner created event from Project subgrid + New. Auto-detect fired. ALL 5 resolver fields showed as NULL on the saved record. The `_sprk_regardingproject_value` was populated only because Dataverse's subgrid relationship mapping does it independently.

**Root cause**: v1.4.5 auto-detect CREATE-mode gated ALL `setFormLookupValue` / `setFormTextValue` calls behind 3 sequential `await`s (resolveRecordType, resolveRecordDisplayNameFieldName, retrieveMultipleRecords). If the user saved before all awaits completed — OR if any await threw silently — none of the setValue calls fired. The outer try/catch swallowed the exception.

**Fix**: v1.4.6 two-phase writes:
- Phase 1 (synchronous): write id + baseline name + url + selectedTarget + presave bridge BEFORE any await
- Phase 2 (async catch-up): recordtype resolution, display-name refinement, record-number resolution — each writes independently when its data arrives

**Rule**: **In form-attribute writes, sync-first / polish-second, never gate-all-behind-async.** Users can save at any moment; async data enrichment must never block the baseline write. This is a systemic rule for any Xrm.Page.setValue orchestrator.

---

## L-6. Method extraction breaks `this` binding on Xrm APIs (SRFR-044)

**What happened**: v1.3.6 hyperlink click → `TypeError: Cannot read properties of undefined (reading '_clientApiExecutor')`.

**Root cause**: Extracted `const navigateTo = xrm.Navigation.navigateTo;` then called `navigateTo(...)`. Lost `this` binding. The platform implementation internally accesses `this.Navigation._clientApiExecutor`; unbound = undefined = throw.

**Fix**: Call as method — `xrm.Navigation.navigateTo(...)` — preserving `this`.

**Rule**: **Never extract Xrm platform methods as bare variables.** Always call as method syntax `xrm.Namespace.method(...)`. Applies to all Xrm.WebApi, Xrm.Navigation, Xrm.Utility APIs. Same applies to any `.bind(...)` alternatives — most Xrm APIs assume method-call context.

---

## L-7. `Xrm.Utility.lookupObjects` returns Primary Name, not the "name" you want (SRFR-052 + SRFR-054)

**What happened**: Owner's UAT: Regarding Record Name showed `REAL-2026-123456.02` (the number) instead of `Real estate transaction analysis` (the actual matter name).

**Root cause**: For `sprk_matter`, the entity's **Primary Name column** is `sprk_matternumber`, not `sprk_mattername`. `Xrm.Utility.lookupObjects` returns `record.name = <Primary Name value>`. When applyResolverFields wrote `parentRecordName`, it wrote the number.

**Fix**: Mirror the SRFR-071 `sprk_regardingrecordnumberfield` pattern. Add `sprk_recorddisplaynamefield` column to `sprk_recordtype_ref`. Populate per entity (Matter → sprk_mattername, Event → sprk_eventname, etc.). Extend `applyResolverFields` to resolve display-name via metadata + target-record query. Fallback to picker's returned name when metadata is null (NFR-06 graceful).

**Rule**: **Don't assume any Dataverse API's returned "name" is the entity's business name.** Especially for entities where the Primary Name is deliberately the number/id (a common admin choice for search + navigation). Always define an explicit "which field IS the display name" mapping in your catalog, and resolve it at write time.

---

## L-8. Table recreate is a nuclear reset with a distant blast radius (SRFR-049)

**What happened**: Owner hit `n.toLowerCase is not a function` opening ANY field mapping profile form. The static XML looked fine — root cause is opaque without runtime debug. Owner elected to delete + recreate the profile table.

**Root cause of the toLowerCase error**: Never diagnosed — likely PowerAppsOneGrid runtime error triggered by a specific Choice column's malformed metadata OR by the specific form's `controlDescription` config. Recreation resolved it.

**Consequences of the recreate**:
- Column names not identical (`sprk_compatibilitymode` → `sprk_capabilitymode` on profile) — no code impact
- `sprk_isactive` had to be manually added post-recreate to satisfy BFF queries (before we also rewrote BFF)
- All existing profile records were wiped

**Rule**: **Table recreate should be a last resort, not a first-line fix.** Before recreating, verify that (a) no BFF/webresource/plugin code queries fields you might rename, (b) no relationships or ribbons reference specific column IDs (they usually reference by name, so recreate is safer than most people think, but not always), (c) column-value data isn't operationally load-bearing. When you do recreate, **run a full grep of the codebase for every column name** and update or add columns to match.

---

## L-9. FormXml editable subgrids (PowerAppsOneGrid) fail with generic errors (SRFR-049)

**What happened**: The profile form + rules subgrid used `controlDescription` with `Microsoft.PowerApps.PowerAppsOneGrid` + `EnableEditing=true`. On save + reopen, threw `n.toLowerCase is not a function` — no diagnostic path.

**Root cause**: Unknown. Not root-caused. The static XML validated. Recreation resolved.

**Rule**: **FormXml customization is a high-risk / low-diagnostic surface.** Prefer maker-authored forms (via Power Apps Studio) when possible. If FormXml patching is required, minimize surface area (identity fields only) + defer subgrid configuration to the maker. For SRFR-090 follow-on, we file `fieldmapping-profile-form-r1` as a project to author the profile form correctly via the maker UI.

---

## L-10. Consolidation late in a project is cheaper than a follow-on project (SRFR-045)

**What happened**: Mid-project, owner realized RegardingResolver + AssociationResolver did essentially the same job. Both used the same catalog, same write logic, same UI shell. The consolidation window opened AFTER waves 2/3/5 had already built up ~70% of the shared code (PolymorphicPicker, FieldMappingHandler, applyResolverFields 5-field write).

**The right call**: Fold the last 30% into this project instead of a follow-on. Effort: ~7h (SRFR-045 + SRFR-046 owner form placement). Delivered:
- Deleted `src/client/pcf/AssociationResolver/` entirely
- Deleted `AssociationResolverSolution` from spaarkedev1
- Extended RegardingResolver with auto-detect from subgrid (AssociationResolver's one unique feature worth keeping)
- Updated ADR-024 to reflect single-PCF architecture
- Zero user-facing regression (AssociationResolver was never placed on any form)

**Rule**: **When consolidation clarity emerges mid-project, execute in-scope if the delta is <20% of the project's total effort.** Follow-on projects have overhead (spec, plan, PR, review, deploy) that easily exceeds the direct-execution cost. The exception is when consolidation requires deep architectural change or affects users — those warrant their own project.

---

## Bonus: process observations

### B-1. Sub-agents (parallel) + main-session (`.claude/` writes) works well

15+ sub-agents dispatched during this project (Wave 2/3/5/6/7 parallel groups, plus SRFR-042/043/045/052/053/056/057). Each hit the sub-agent write boundary (`.claude/` paths) exactly as designed. Main session picked up the constraint amendments (ADR-024). Zero code collisions between parallel sub-agents that touched different modules.

### B-2. Version-bump discipline is repetitive but necessary

7 anchor files per PCF version bump. 12 owner-visible version bumps across the project (v1.3.0 → v1.4.6). Every bump succeeded because we standardized on: `sed -i 's/1\.X\.Y/1\.X\.Z/g' <6 anchor files>`. Consider extracting to a script for future PCF-heavy projects.

### B-3. The "hard-refresh reminder" is real

Every PCF deploy required an owner hard-refresh (Ctrl+Shift+R) because the Power Apps bundle cache is aggressive. When owner reported "still broken" after a deploy, the first question was always "did you hard-refresh?" — usually the answer was yes, but occasionally not.

### B-4. Owner-driven scope adjustments mid-project

Three scope adjustments during the project (SRFR-049 KPI → ReportCard, Invoice deferred, Communications reinstated). Each was captured in `notes/task-049-scope-adjustment-and-reportcard-schema.md`. This kept the audit trail clean without adding process weight.

---

## Metrics

| Metric | Value |
|---|---|
| Original tasks | 28 |
| Total tasks completed | 57 (28 original + 29 out-of-plan iterations SRFR-034 through SRFR-057) |
| RegardingResolver versions shipped | 11 (v1.3.0 → v1.4.6) |
| Push webresource versions shipped | 2 (v1.0.0 → v1.1.0) |
| BFF deployments | 2 (initial SRFR-082 baseline + SRFR-056 realignment) |
| Schema fixes | 3 (sprk_reportcard URL, sprk_recorddisplaynamefield, sprk_isactive on rule) |
| Table recreates | 1 (sprk_fieldmappingprofile — nuclear reset) |
| Sub-agents dispatched | 15+ |
| Parallel groups | 5 (Waves 2, 3, 5, 6, 7) |
| ADR amendments | 2 (ADR-024 4→5 fields SRFR-071; ADR-024 single-PCF SRFR-045) |
| Merge commits | 1 (218 commits from master; 4 conflicts, all additive/deletion) |

---

## Recommended follow-on projects

1. **`invoice-polymorphic-multi-parent-r1`** (~3-5d) — Design a new schema for Invoices to associate with multiple parent records across multiple entity types (e.g., fees split across Matter 1, Matter 2, Project 1). Current single-lookup pattern doesn't fit.

2. **`fieldmapping-profile-form-r1`** (~1-2d) — Author the sprk_fieldmappingprofile main form via Power Apps Studio (maker UI, not FormXml patching). Include editable rules subgrid. Diagnose the `n.toLowerCase` error's true root cause if possible.

3. **`kpi-assessment-retirement-r1`** (~0.5d) — Formal retirement of sprk_kpiassessment as a primary child entity. Migrate any residual data to sprk_reportcard. Document the schema-level decision.

4. **`resolver-integration-test-hardening-r1`** (~2-3d) — Author integration tests that exercise real Dataverse metadata queries (nav-prop discovery, catalog resolution, display-name/number field resolution). Would have caught L-1, L-2, L-3, L-7 pre-UAT.

---

*Authored 2026-07-08 as part of SRFR-090 wrap-up.*
