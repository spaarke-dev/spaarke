# Task 031: Information fields, ReadableColumns additions (FR-18)

Rigor FULL (POML; tags bff-api, authorization). Model: Opus @ high, as a sub-agent of the main session. Nothing is committed, pushed or deployed.

## UAC review request

**For uac-r2. The main session relays this; task 031 did not contact uac-r2.**

R4 task 031 (spec FR-18) adds 12 columns to three external module allow-lists in `ExternalAccessModule.cs`. They feed the record template's Information section. Each column was found in live dev metadata in task 030, none is field-secured, and the owner approved all of them on 2026-11-23 ("Yes to both"). None is grid-derived, so each entry's comment cites FR-18 and "UAC review: pending".

| Module | Columns added | Type |
|---|---|---|
| `matters` (sprk_matter) | `sprk_matterdescription` | Memo |
| | `sprk_practicearea` | Lookup → `sprk_practicearea_ref` |
| | `sprk_assignedattorney1`, `sprk_assignedparalegal1` | Lookup → `contact` |
| | `createdon` | DateTime, shown as the open date (`sprk_opendate` does not exist; owner) |
| `collaboration` (sprk_project) | `sprk_projectdescription` | Memo |
| | `sprk_practicearea` | Lookup → `sprk_practicearea_ref` |
| | `sprk_assignedattorney1`, `sprk_assignedparalegal1` | Lookup → `contact` |
| `work-assignments` (sprk_workassignment) | `sprk_description` | Memo |
| | `sprk_assignedto` | Lookup → `contact` |
| | `sprk_regardingmatter` | Lookup → `sprk_matter` |

Invoice gets nothing (both candidates are NOT FOUND). There are no pointer columns, no `sprk_signal`, no new route, no DI registration and no row-scope change. Task 157 had dropped four of these as rule-(b) only (`sprk_project.sprk_practicearea`, `sprk_matter.createdon`, `sprk_matter.sprk_practicearea`, `sprk_workassignment.sprk_assignedto`). Task 031 re-admits them for a stated reason, as r3 task 002 did for `sprk_projecttype_ref`.

**Internal people's names (four contact lookups).** The partner sees the name of an internal attorney, paralegal or assignee. The owner approved this on 2026-11-08 and per column on 2026-11-23. The name arrives only as the lookup's `@formattedValues` entry, plus the lookup's own `name` and the contact's id. `contact` has no external module, so a direct read of it is still refused (a test pins this). No email or other contact column is exposed.

**The open disclosure question: work assignment `sprk_regardingmatter`.** It is a lookup to a ROOT `sprk_matter`, so its formatted name (and id) shows on any work assignment the partner can read, whether or not the partner has a grant on that matter. Two points make it sharper:
- The work-assignments module does not apply `RequireReadOnSecureRoots`, so the name of a SECURE matter would also show.
- The existing `sprk_regardingproject` on the same module (a grid column since task 134) already discloses a project's name in exactly this way. UAC may want to decide both together.

Options: (a) accept, as for `sprk_regardingproject`; (b) accept only for matters the caller can read, which needs new server logic and is outside this task; (c) drop `sprk_regardingmatter` from the list and from the SPA config.

**Asks:** a per-column decision on the 12, and a decision on the `sprk_regardingmatter` question.

## UAC review outcome (uac-r2, 2026-11-23, relayed by the main session)

- **11 of 12 APPROVED by uac-r2.** Its reasoning:
  - each is a column of the row the caller already reads;
  - a lookup exposes only the target's display name, through `@formattedValues` filtered by `StripToReadableColumns` (a missing list strips everything);
  - practice area is reference data;
  - the contact lookups name an internal person on the caller's own record;
  - re-admitting task 157's four columns is fine.
- **`sprk_regardingmatter`: HELD by uac-r2, then ACCEPTED by the owner** in uac-r2's session on 2026-11-23 (recorded in uac-r2 `notes/decisions.md`). The existing `sprk_regardingproject` stays as it is.
  - **What the owner accepted:** a partner sees a parent's name on any work assignment they can read, even when the parent is secure. Only the name is shown, through the filtered `@formattedValues`.
  - **What uac-r2 had recommended:** (c), drop it, plus an issue for (b).
- **Conditions:**
  1. Keep the test that a direct `contact` read is refused. **Met:** `GetScopedRecord_WhenTheEntityIsALookupTargetTableWithNoModule_IsRefusedWithoutReading` (`[InlineData("contact")]`, `ExternalModuleColumnAllowListTests.cs` ~:1318) is kept.
  2. Add a test that a lookup's formatted value is dropped when the lookup is not on the allow-list, if none exists. **Met, already exists:** `ExecuteScopedFetch_WhenAMatterRowCarriesTheAdmittedAndAnUnadmittedLookup_KeepsOnlyTheAdmittedOneWithItsDisplayValue` (~:1287). Its row gives an unlisted lookup (`ownerid`, an EntityReference) the display value "Internal Owner". The test asserts that both the column and its `@formattedValues` entry are dropped, and that the allowed lookup keeps its display value. The `/record` path (~:1043, which includes `ownerid`) uses the same `StripToReadableColumns`, so no new test was added.
- **Task 034 polling:** no objection. Keep the pause on a hidden tab or collapsed section, keep the stop on 401/403 or after 3 failures, and never let the interval shrink under retries.

## Formatted-name finding (escalation trigger checked: does not fire)

The module fetch returns a lookup's display name without reading the referenced record, and without a new read or a `_ref`/contact table on any allow-list:
- `FetchService.cs:146-176` (`ProjectEntity`) copies the SDK's `Entity.FormattedValues` from the ONE `RetrieveMultiple` into `@formattedValues`. Dataverse resolves the lookup name inside that single-entity query, app-only. The BFF makes no second read.
- `ExternalModuleDataEndpoints.cs:25-30`: the guard refuses any `<link-entity>`, so no join can be used to pull referenced columns.
- `ExternalModuleDataEndpoints.cs:1454-1474` (`StripToReadableColumns`) keeps a `@formattedValues` entry only when its key (the lookup column's own name) is on the allow-list.
- The raw lookup value is the serialised `EntityReference` (`id`, `logicalName`, `name`). The SPA prefers the formatted value and falls back to `name`, and never shows the id (`RecordDetailFields.tsx` `formatDetailValue`).

Because the name is resolved app-only, it is also returned for a referenced record the caller has no grant on. That is the disclosure question above.

## What changed

| File | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/ExternalAccessModule.cs` | 12 `ReadableColumns` entries with FR-18 / task 031 / "UAC review: pending (relayed by main session)" comments |
| `src/server/api/Sprk.Bff.Api/Services/Dataverse/FetchService.cs` | D-33 (#3010): the `ProjectEntity` remarks now state the real wire shape and point to `rawWholeNumberOf`. Comment only. The summary named the wrong key (`FormattedValues` for `@formattedValues`); that is fixed too |
| `tests/integration/auth/UnifiedAccessControl/ExternalModuleColumnAllowListTests.cs` | Live-derivation lists and the task-134 "before" side updated; 4 re-admitted columns leave the rule-(b) drop set; sibling-refusal rows and the matter strip test now use still-unlisted columns (`ownerid`, project `createdon`); the lookup-target theories cover `sprk_practicearea_ref` and `contact`; new section 8 (must-fire / must-not-fire) |
| `src/client/external-spa/src/components/detail/RecordDetailFields.tsx` | Information field rows per type (lookups `kind: 'lookup'`, `createdon` labelled "Open date"); stale WA comment about invoice lookups corrected |
| `src/client/external-spa/tests/RecordTemplateSections.test.tsx` | New `Information fields (R4 task 031, FR-18)` block |
| `notes/defer-issues.md` | D-33 Status → Done (the issue is left open for the main session) |

Labels and order: Matter = Name, Number, Status, Open date, Practice area, Assigned attorney, Assigned paralegal, Description. Project = Name, Number, Status, Practice area, Assigned attorney, Assigned paralegal, Description, Last modified. Work assignment = Name, Number, Priority, Response due, Status, Project, Matter, Assigned to, Description.

## Decisions and deviations

- **Files differ from the POML.** `RecordDetailModal.tsx` and its test were deleted in task 005. Field config stays in `RECORD_DETAIL_ENTITIES` (`RecordDetailFields.tsx`), which `record/sections/InformationSection.tsx` renders. The SPA test went into `RecordTemplateSections.test.tsx`, the ported home of the deleted modal test, so it runs the real grid → module fetch → Information path.
- **Only the 12 new columns are shown.** The already-listed `sprk_mattertype` (matter and WA) and the matter's `modifiedon` are not added to the Information rows, because the brief says "exactly those columns". The owner may want them; it would be a one-line SPA change with no BFF change.
- **The existing tests changed, not just new ones.** `ExternalModuleColumnAllowListTests` pinned four of the re-admitted columns as unreadable (rule-(b) drop set, task-002 sibling refusals, the matter strip test). Those rows were moved or replaced with still-unlisted siblings, so every refusal theory still has a real column to refuse. No refusal coverage was removed: each replaced row refuses `ownerid` or project `createdon`.
- **Contract test shape.** `InformationReadFetchXml` hand-mirrors the SPA's column set. C# cannot import the TS config. If either side drifts, the must-fire tests stop proving the real request. This is accepted as a cross-language limit (K4). The SPA test also asserts each new column is in the FetchXML the SPA really sends.
- **Must-not-fire on the record route** (`$select … ,ownerid`) passes before and after the change. It is the record-route pair of the must-fire, not a fail-before test.

## Tests and gates

| Gate | Result |
|---|---|
| BFF build `dotnet build src/server/api/Sprk.Bff.Api/` | Succeeded, 0 warnings, 0 errors |
| `ExternalModuleColumnAllowListTests` | 173/173 pass. **Before** (HEAD's `ExternalAccessModule.cs` restored): 21 fail / 152 pass |
| External-module neighbours (`ExternalModule*`, `FetchXmlGuard*`, `ExternalFindingsJsonPin`, `ExternalRecordDocumentsGridConfiguration`) | 288/288 pass |
| ArchTests scanning `ExternalAccessModule.cs` (BillingLookupsConferNoAccess, PrivateContextColumnGuard, DirectMessageOwnerStepGuard, ContactCreateOwnerPolicyGuard, InvoiceNoteColumnsGuard, ExternalSpaGridViewSelectorGuard) | 386/386 pass |
| Seeding proof | `ownerid` added to the matter list temporarily: the three matter refusal tests (Information read + `ownerid`, record `$select`, task-002 sibling) fail, 11 others pass; restored |
| SPA vitest, new block | 9/9 pass. **Before** (HEAD's `RecordDetailFields.tsx` restored): 9/9 fail |
| SPA vitest, 7 files reading the record config (RecordTemplateSections, RecordTemplateModal, EventRecord, ReviewDetail, useRecord.sources, RecordSectionCollapse, RecordTemplateSubmitInvoice) | 271/271 pass |
| `npx tsc --noEmit -p tsconfig.json` | clean; the changed test file also type-checked once with a temporary config (tests are outside `include`) |
| eslint on the changed SPA files | clean |
| CVE `dotnet list package --vulnerable --include-transitive` | no vulnerable packages; no package change |
| Conflict check | `ExternalAccessModule.cs`, the allow-list test and `RecordDetailFields.tsx` have no commits on master since the merge-base, and no unmerged changes on `origin/work/unified-access-control-r2` or `origin/work/ebilling-r1`. Uncommitted work in those worktrees is not visible from here |

The full BFF unit suite was not run (other agents are building on this machine; project gotcha).

## Publish size (bff-hygiene item 4)

Convention: `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`, framework-dependent linux-x64, Release, `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, PDBs included (4). Each side was built in a fresh worktree at a short path, and all three worktrees were removed with `Remove-WorktreeSafe.ps1`.

| Side | Commit | Files | Zip bytes | MB |
|---|---|---|---|---|
| Fresh master (`C:\wt031m`) | `22c42efad6` | 194 | 43,562,055 | 41.54 |
| Branch HEAD, without this task (`C:\wt031h`) | `22096cf6fa` | 191 | 42,615,325 | 40.64 |
| Branch HEAD + this task (`C:\wt031b`) | `22096cf6fa` + diff | 191 | 42,615,382 | 40.64 |

- **This task's delta is +57 bytes**, measured HEAD vs HEAD + task with 191 = 191 files.
- Branch vs fresh master is −0.90 MB, but the file counts differ (194 vs 191). Master is 284 commits ahead and carries another project's `ModelContextProtocol*.dll` (3 files). That difference is master's own growth, not this task, and it is resolved when R4 next merges master.
- Total is 41.54 MB, under the 55 MB review line and the 60 MB stop.

## Step 9.5

**code-review** (standard, run inline over the five files):
- Security: the change adds readable columns only. Row scope, the guard and the strip are unchanged. The disclosure points are listed for UAC above (policy decisions, not code defects).
- Performance: more attributes on one existing single-record read; no new request.
- AI smells: none.
- ADR: compliant.
- BFF hygiene: Placement Justification = extends the existing external allow-list (`bff-extensions.md` §A: no new endpoint, service, registration, package or background work). Publish size and CVE above.
- Component justification: no new surface. The POML's `<justification>` covers the list extension.
- `ExternalAccessModule.cs` (> 300 lines) is cohesive: the external-access DI module, and this change is data-only.

Findings:
- F2, fixed: stale comment in `RecordDetailFields.tsx` (WA entry) said `sprk_invoice` has no work-assignment lookup; the invoices module has `sprk_regardingworkassignment` as a dimension.
- F2, fixed: `FetchService.ProjectEntity` summary named the wrong synthetic key.
- K4: the C# Information column set hand-mirrors the SPA config (see Decisions).
- Policy, relayed to UAC: `sprk_regardingmatter` (and the existing `sprk_regardingproject`) can name a root, including a secure one, that the caller has no grant on.

**adr-check:** compliant, no violations.
- ADR-001: no route.
- ADR-002: no write path.
- ADR-007: no Graph.
- ADR-008: filters unchanged.
- ADR-010: no registration or interface.
- ADR-013: no AI.
- ADR-019: error codes unchanged.
- ADR-021: no new styles; semantic tokens unchanged.
- ADR-028 A1: app-only, Tier-2 row scope unchanged; SPA uses the existing `bffApiCall`.
- ADR-038: KEEP path, no banned mocks, `{Method}_{Scenario}_{Expected}` names, must-fire / must-not-fire pairs.
- ADR-052: no background work.
- ADR-012/050: not touched.

**UI test (Step 9.7):** not run. No deploy in this task (deploy is task 038 via uac-r2).

## Out-of-scope items found

None filed. The `sprk_regardingproject` disclosure point is part of the UAC request above rather than a separate defect, because it was a reviewed grid-column decision.

rework: fix-round-1 task 031

## Owner decision 2026-11-23: two more Information fields

The owner said yes to also showing columns that were already on the allow-list. The matter shows `sprk_mattertype` ("Matter type") and `modifiedon` ("Last modified"); the work assignment shows `sprk_mattertype`. This is SPA config only (`RecordDetailFields.tsx`) and adds no BFF column. Covered by the `Information fields (R4 task 031, FR-18)` cases in `RecordTemplateSections.test.tsx`.

