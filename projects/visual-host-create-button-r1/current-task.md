# Current Task State - visual-host-create-button-r1

> **Last Updated**: 2026-07-09 (by context-handoff)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Project COMPLETE. All work merged to master. |
| **Step** | Task 090 wrap-up done (README→Complete, lessons-learned, valid-keys doc, ISS-01/#587 filed). PR #588 (v1.4.32 fix + wrap-up docs) merged to master (`983d1f5ec`, 2026-07-09). Main repo's local master synced. **Owner explicitly wants this worktree kept open** (not archived) — not registered with the portfolio system either, so `/devops-project-archive` isn't applicable as-is. |
| **Status** | not-started (project complete — no active task; branch fully in sync with master, nothing stranded) |
| **Next Action** | None required. If resuming work here: either (a) pick up Issue #587 (Invoice Vendor Org lookup) with live browser access, or (b) start a fresh task/project in this worktree. |

### Root cause found + fixed (2026-07-09) — v1.4.32
`CreateEventWizard.tsx` had two real bugs, both in its own local, duplicated search-helper code (not shared with the already-correct `matterService.ts`/`workAssignmentService.ts`):
1. **Matter Type / Practice Area (Event → Assign Work follow-on) — root cause CONFIRMED**: `CreateRecordWizard.tsx:350-351` falls back to `EMPTY_SEARCH = () => Promise.resolve([])` when a wizard's config doesn't supply `searchMatterTypes`/`searchPracticeAreas`. `CreateEventWizard.tsx` never set either config key — exactly matching the reported symptom (blank results, **zero console errors**, since `EMPTY_SEARCH` never throws).
2. **Bonus find (same file)**: Event's local `searchOrganizationsAsLookup` queried the WRONG field (`sprk_name` instead of the real `sprk_organizationname`, confirmed live via `mcp__dataverse__describe` in the prior session) — a duplicate, incorrect copy of logic that already exists correctly elsewhere.

**Fix**: Deleted Event's 3 local duplicate search-helper functions; now imports the canonical, verified-correct helpers from `../CreateWorkAssignmentWizard/workAssignmentService` (same pattern `CreateInvoiceWizard.tsx` already used) — `searchContactsAsLookup`, `searchOrganizationsAsLookup`, `searchUsersAsLookup`, `searchMatterTypes`, `searchPracticeAreas`. Added the two missing config keys (`searchMatterTypes`, `searchPracticeAreas`) wired to new `handleSearchMatterTypes`/`handleSearchPracticeAreas` callbacks.
**Verified**: shared-lib `tsc` clean; all 3 `CreateEventWizard` test suites pass (26/26); full suite unchanged from baseline (107/114 suites, 1749/1765 tests, same 7 pre-existing unrelated failures — no new regressions). Deployed bundle grepped to confirm the buggy `sprk_organizationid,sprk_name` query string is gone and the correct queries are present.
**Still unexplained**: Invoice's "Vendor Organization" field (main step, `CreateInvoiceStep.tsx`) — this field was NOT affected by either bug above; it directly wires `searchOrganizationsAsLookup` from `matterService.ts` (the canonical, correct implementation) with a correctly-plumbed `dataService`. Re-verified this session, still no code-level defect found. If still broken after retest on v1.4.32, this needs a live browser Network-tab check.

### Files Modified This Session
- `src/client/shared/Spaarke.UI.Components/src/components/CreateEventWizard/CreateEventWizard.tsx` — **root-cause fix**: removed 3 local duplicate search-helper functions (one had a wrong field name); now imports canonical helpers from `workAssignmentService.ts`; wired the 2 missing `searchMatterTypes`/`searchPracticeAreas` config keys that were silently falling back to `EMPTY_SEARCH`.
- `src/client/pcf/VisualHost/control/components/VisualHostRoot.tsx` — dialog-size fix (two iterations: first wrongly matched WizardShell's internal 95vw/70vh default, corrected to the REAL repo-wide standard **60% × 70%** found in `src/client/webresources/js/sprk_wizard_commands.js`'s `DIALOG_OPTIONS` — "standardized... per UAT feedback E-03"); toolbar icon gap tightened (`toolbarIcons`/`toolbarFloat`: hardcoded 10px/8px → `tokens.spacingHorizontalXXS`); version footer now v1.4.32.
- `src/client/pcf/VisualHost/control/components/CardChrome.tsx` — icon slots gap tightened (`spacingHorizontalXS` → `spacingHorizontalXXS`).
- 5 version-bump locations — now at **v1.4.32**, deployed + confirmed live on spaarkedev1 (`pac solution list`).
- `C:\code_files\spaarke\projects\set-regarding-and-field-mapping-resolver-r2\design.md` (**different repo/worktree** — `C:\code_files\spaarke`, not this worktree) — new project design doc, drafted per owner request, NOT yet spec'd or executed. Covers restoring field-mapping inheritance (Matter→child auto-populate) that was found working (Feb 2026) then deleted as collateral damage (July 2026, AssociationResolver retirement). Recommends reusing the already-live BFF `/api/v1/field-mappings/*` endpoints rather than rebuilding client-side, and NOT rebuilding the 4 retired admin PCFs (native Dataverse forms instead).

### Critical Context
UAT results across all 3 wizards (Event/Invoice/Report Card): **Report Card fully works**. Invoice and Event both work for their core create-flow but each had a broken typeahead lookup. **Event's Matter Type + Practice Area (Assign Work follow-on) are now FIXED** (root cause confirmed + deployed as v1.4.32, see above). **Invoice's Vendor Organization lookup remains unresolved** — no code-level defect found after two full investigation passes; needs live retest.

---

## Investigation dead-end (2026-07-08/09) — read before re-investigating

Verified ALL of the following are correct — do not re-check these, move to a different hypothesis:
- **Field/entity names correct**: `sprk_organization.sprk_organizationname` (Vendor Org), `sprk_mattertype_ref.sprk_mattertypename` (Matter Type), `sprk_practicearea_ref.sprk_practiceareaname` (Practice Area) — all confirmed via live `mcp__dataverse__describe`.
- **Data exists**: sprk_organization has records ("Morrison Foerster LLP" etc.), sprk_mattertype_ref has 5 rows, sprk_practicearea_ref has 6 rows (confirmed via live query).
- **Query/wiring code correct**: `searchOrganizationsAsLookup`/`searchMatterTypes`/`searchPracticeAreas` (all in `CreateMatterWizard/matterService.ts`, re-exported into `CreateInvoiceWizard` and `CreateWorkAssignmentWizard`) use correct OData syntax, correct entity names, correct field names.
- **`dataService` wiring correct**: `VisualHostRoot.tsx`'s `createWizardDataService = createXrmDataService()` — genuine Xrm.WebApi-backed adapter, same as every other Code-Page-hosted wizard.
- **`LookupField.tsx` component itself has no entity-specific bug** — fully generic, debounced (300ms), catches+logs errors.

**Not yet checked / next hypotheses to try**:
1. Is `onSearch` actually being invoked at all when the user types? (Add a temporary `console.log` at the very top of `LookupField`'s debounced effect, or ask owner to confirm the `[MatterService] search... query:` info-level console log appears at all — owner only confirmed no *errors*, not whether the info logs are present.)
2. Check the browser Network tab for the actual outgoing OData request — does it fire, what's the response body/status?
3. Security-role/privilege gap on `sprk_organization`/`sprk_mattertype_ref`/`sprk_practicearea_ref` for the browser session's user (would not throw if Dataverse just returns 0 rows rather than 403 for a privilege-filtered query — worth double-checking this assumption, since a true 403 WOULD throw and get caught+logged, but Dataverse's `retrieveMultipleRecords` under certain privilege configurations can silently return an empty set instead of erroring).
4. Whether the built/deployed bundle (v1.4.31) actually contains the current `matterService.ts` source (VisualHost imports shared-lib SOURCE directly per project CLAUDE.md, not a possibly-stale `dist/` — should be fine, but worth a sanity `grep` of the deployed `bundle.js` for one of these query strings to rule out a stale-bundle theory).

---

## Full State (Detailed)

### Project status
All 16 implementation tasks (001–050) complete and previously verified via live-record-creation (not full browser click-through — this was the FIRST real browser UAT pass). **Task 090 (wrap-up) is the only task left** in `tasks/TASK-INDEX.md`, deliberately held pending resolution of the 3 UAT-found lookup bugs plus any other findings from continued UAT.

### Decisions made this session
- 2026-07-08/09: Dialog sizing — corrected twice; final value **60vw × 70vh** matches the documented repo-wide ribbon-launch standard (`sprk_wizard_commands.js` `DIALOG_OPTIONS`, "per UAT feedback E-03").
- 2026-07-08/09: Toolbar icon spacing tightened per owner request ("reduce spacing... a little bit") — used the next-smaller Fluent semantic token (`spacingHorizontalXXS`) rather than a hardcoded pixel value, consistent with this repo's token-based styling convention.
- 2026-07-08: Field-mapping inheritance (Matter→child auto-populate, e.g. Assigned Attorney 1) confirmed OUT of `visual-host-create-button-r1`'s scope (was always deferred in spec.md) — spun into a new project `set-regarding-and-field-mapping-resolver-r2`, design.md drafted at `C:\code_files\spaarke\projects\set-regarding-and-field-mapping-resolver-r2\design.md`. This is a SEPARATE project/repo path from this worktree — not part of this project's remaining work.

### Owner gates / pending ratifications (carried forward, still valid)
- Task 023's Path-A exception (WorkAssignment kept 2 local follow-on steps instead of migrating) — **already ratified** by owner 2026-07-08 after concrete investigation.
- Human UI click-through — **now done** (this session's UAT), superseding the earlier "never verified live" gap. New gap: 3 lookup bugs found by that UAT, unresolved.

---

## Next Action

**Next Step**: Resume lookup-bug investigation per the 4 untried hypotheses above, most likely starting with asking the owner to check the Network tab or confirm whether the `[MatterService] search...` info-level console logs appear at all during their next UAT attempt.
**Pre-conditions**: None — this is pure investigation, no deploy needed until a fix is identified.
**After that's resolved**: Execute task 090 (wrap-up): `/test-diet`, maker-facing valid-keys note (`event`/`invoice`/`report-card`), README → Complete, lessons-learned (must capture: the KPI→Report Card pivot, task 023's exception, the two-iteration dialog-sizing bug, the toolbar-spacing tweak, the field-mapping discovery spun into r2, and however the lookup bugs get resolved), PR citing hot-path declaration + `git diff --stat`.

---

## Blockers

**Status**: Soft blocker — 3 lookup bugs (Vendor Org, Matter Type, Practice Area) unresolved; root cause unknown despite thorough code/schema/data verification. Not blocking further UAT of other areas, but should be resolved before task 090 wrap-up.

---

## Quick Reference

- **Project**: visual-host-create-button-r1
- **Project CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md)
- **ADRs**: ADR-024 (central; amended by #549), ADR-022/021 (PCF+Fluent), ADR-007/028 (SPE+auth)
- **Related but separate project** (different repo path): `C:\code_files\spaarke\projects\set-regarding-and-field-mapping-resolver-r2\design.md` — field-mapping inheritance restoration, drafted not yet executed.
