# Task 098: (a′) on sprk_invoice, hiding invoices with an isolated Submit Invoice source from external callers

Branch `task/ebill-098-external-marker`, based on `786d1ea17a`. Rigor FULL, opus tier. One adversarial-review pass (operator direction; the POML asks for two).

## What was built

- **The predicate**: `Infrastructure/ExternalAccess/ExternalInvoiceVisibility.cs` (`DecideAsync`, `HiddenFamilyRowsAsync`). It is a thin composition: task 188's `ExternalDocumentNarrowing` decides the source for rules 2–4, and it adds the roots term (`ChildRowRoots` plus the live-parent walk, then effective root flags). Rule 1 (root grants) is never applied.
- **The marker option**: `ExternalDocumentNarrowing.DecideAsync(..., honourSubmissionMarker)` defaults to `false`. When it is `true`, `SkipsOwnLeg` skips only hop 0, and only when the row's own value is Restricted and the row carries `sprk_restrictedbysubmission` (ExternalDocumentNarrowing.cs:322). Ancestors, the communication rule and the thread rule still apply. All 188 tests pass unchanged with the option at its default.
- **Seams** (`ExternalDataService`, registered in `ExternalAccessModule.cs:129-132`):
  - `HideInvoicesForAsync` (ExternalDataService.cs:1929) is caller-aware, exposed as `ExternalInvoiceHiding`.
    - An internal systemuser hides nothing and causes no reads.
    - An unresolvable caller, or any fault, gives `ReadFailed`, which hides every asked invoice.
  - `HideInvoiceFamilyRowsAsync` (ExternalDataService.cs:1962) is exposed as `AppOnlyInvoiceFamilyHiding` for the AI tools. It maps family rows to their invoice. It throws on a fault, and the tool then answers with an error and no data.

## Surfaces (a)–(d)

| Surface | Where | Behaviour for a hidden invoice | Fail-closed path |
|---|---|---|---|
| (a) Module grid fetch over any family table | ExternalModuleDataEndpoints.cs:404-406, `HideInvoiceRowsAsync` :480 | Row absent, even when the grid does not ask for its id (the id is projected server-side and stripped) | Fault, no predicate, or an unreadable id: the row is hidden and `Truncated=true` |
| (a) Single-record route on the invoices module | Unchanged | Already 403 for every invoice id, with no reads (pinned by test) | n/a |
| (b) Invoice thread: list, post, delete-own | ExternalMessageEndpoints.cs `EvaluateRecordGateAsync` invoice block (~810-827) | Same answer as a non-existent or cross-tenant invoice id, with no message read and no row written | Fault or no predicate: refused the same way |
| (b) Messages on a matter or project thread regarding an invoice, and the notification feed | ExternalMessageEndpoints.cs:514 (`SecureRootsFilter`), `RegardingInvoicesVisibleAsync` :532, ExternalNotificationFeedEndpoints.cs | Withheld from the list, the feed and the count. One predicate call per page | Fault or malformed regarding: withheld |
| (b′) Documents filed to an invoice: list, content, versions, upload | ExternalProjectDataEndpoints.cs:1415, `RightsForInvoiceAsync` → `InvoiceVisibleToAsync` :1350 | 403 with the same body as a missing invoice; nothing touched | Fault: refused the same way |
| (c) Submission read | ExternalInvoiceSubmissionAuthorizationFilter.cs:309 | `UniformRecordNotFound`, the same 404 as a missing submission | Fault or no predicate: 404 |
| (c) Submission list | ExternalInvoiceSubmissionEndpoints.cs:253/266 `WithoutHiddenInvoicesAsync` | Row omitted; the others are kept | Fault: the row is omitted |
| (d) AI `read_query` | DataverseReadQueryHandler.cs:191-212, 369-418 | Invoice or line rows dropped. One seam call per table per page | The seam throws: error with no data. A row without an id is hidden |
| (d) AI `search_data` | DataverseSearchDataHandler.cs:358-375 | Hidden hits dropped and the count adjusted. No warning reveals that anything was dropped | The seam throws: error with no data |
| (d) AI `describe` | DataverseDescribeHandler.cs:378-453 | Same `NotFound` and message as a missing record | The seam throws: error with no data |

`InvoiceSubmissionView.InvoiceId` is new and marked `[JsonIgnore]`. It is read only so the server can decide; the response contract is unchanged, and a test pins that GET never returns it.

### Surfaces outside the list (classification)

- **Single-record route of the invoices module**: SAFE. It already answers 403 for every invoice id, pinned by `InvoicesModule_SingleRecordRead_IsRefusedForEveryInvoiceId...`.
- **Guard `NotInvoiceSurfaces`**. Each entry was reviewed and has a reason in the code:
  - Configuration or tables that read no row: the message roots `.cctor`, `DataverseConfirmedEntitySet`, the WorkforceRoleCeilingGate `.cctor` and `SubmitInvoiceTo`, search `ToTableLogicalName`, and the EmailDraftToolHandler `.cctor` and `.ctor`.
  - Inputs or consequences of an applied predicate: `RegardingInvoicesOf`, `RightsFromGate`, `InvoiceFamilyKeyAttributes`.
  - Reads that return no family row: `DenyUnlinkedFirmAsync`, `FirmRecordsAsync`, `SubmitAsync` (a new submission has no invoice), and `ExternalUserContextEndpoint.HandleAsync`.
- **090's internal file route and upload**: `StoredFileAccessFilter.InvokeAsync` and `StoredFileService.UploadForCallerAsync` refuse every caller that step 0 refuses, flagged-external included. The guard checks this by transitive reach to `FinanceAuthorizationFilter.EvaluateStepZeroAsync`.
- **Later tasks (093, 130, 152)**: listed in `PendingSurfaces`. They are keyed by the guard's primitives (a family-reader call from the external-plane namespace, or a named external type), so each one fails the guard when it lands until it reaches the predicate.
- **Event, memo and analysis records regarding an invoice**: not in model 9.1's closed taint list (`FamilyTables`). Not changed.
- **Internal-plane routes**: internal callers are never hidden by this rule (must-not-fire).

## Query cost and worst case

The cost per call to `DecideAsync` for N invoices:
- Stored File reads: ⌈N/50⌉, each with `$top=2001`. More than 2000 rows hides the whole chunk (`SourcesTruncated`).
- Source document reads: ⌈S/50⌉ for S distinct sources.
- 188 decision: at most 3 hop reads, plus the communication and thread reads.
- One live-parent walk.
- Effective root flag reads: at most 3, one per root type.

Each surface calls the predicate at most once per page or request, and the tests pin the call counts. Invoice upload reads the predicate twice: once in the rights gate and once in the upload path's gate (see Deviations). The logs carry counts only (ADR-015).

## Cache statement

Nothing is cached. Every call reads the current state, so a source that is lowered after hiding is visible again on the next read (tested). The only cache on the path is the identity resolver's existing caller cache.

## Mutation and seeding proofs

Each seed changed one source line, then rebuilt and ran the targeted tests, and the file was restored afterwards (`098_seeds.py` in the session scratchpad). Every seed made its targeted tests fail.

| # | Seed | Result |
|---|---|---|
| S1 | (a) Module fetch: predicate call skipped | 4/4 failed: hides, fault, no predicate, unreadable id |
| S2 | (b) Invoice thread gate removed | 5/5 failed: thread hidden, fault, no predicate, post hidden and post fault |
| S3 | (b) Regarding-invoice term removed from `SecureRootsFilter` | 4/5 failed: matter-thread hidden and fault, feed hidden and fault. The malformed-regarding test still passes: the invoice is a live parent of the message, so 187's multi-root rule already withholds a message whose link cannot be resolved. The (a′) term is a second fail-closed layer there |
| S4 | (b′) Invoice documents: `InvoiceVisibleToAsync` skipped | 2/2 failed (hidden, fault) |
| S5 | (c) Submission read: the `view.InvoiceId` block skipped | 3/3 failed (hidden, fault, no predicate) |
| S6 | (c) Submission list: decision ignored | 2/2 failed (hidden, fault) |
| S7 | (d) `read_query` guard off | 4/5 failed. The internal-caller test passes, as expected |
| S8 | (d) `search_data` hidden hits kept | 1/1 failed |
| S9 | (d) `describe` guard off | 2/4 failed: hidden and fault. Visible and internal pass, as expected |
| S10 | Marker exception removed (`SkipsOwnLeg` always false) | 3/3 failed: the marked-.eml test for contact and for flagged-systemuser, and the option-default test |
| S11 | Roots term removed (`RootRestricted` never returned) | 4/4 failed: source and live parent, for both callers |
| S12 | Guard reach check removed (`IsReach` never adds) | 2 failed: the negative control (lambda or helper no longer found) and the main surface test |

## Tests

New tests:
- `ExternalInvoiceVisibilityTests` (74 tests): must-fire, must-not-fire, the option default, the selected columns, and the taint closure per family table, including rows linked through a line.
- Task098 tests added to the existing surface test classes: `ExternalMultiRootListReadTests`, `ExternalMessageEndpointsTests`, `ExternalMessageSendTests`, `ExternalNotificationFeedTests`, `ExternalRecordDocumentRoutesTests`, `InvoiceSubmissionEndpointsContractTests`, and the three AI handler test classes.
- `ExternalInvoiceVisibilityGuardTests` (ArchTests), with must-fire and must-not-fire controls.

Run results: see "Verification".

## Verification

All of these ran on commit `1ca81502a6`.

**Builds**: six test projects built with 0 errors: Sprk.Bff.Api.Tests, Spaarke.ArchTests, Sprk.Bff.Api.IntegrationTests, Spe.Integration.Tests, Spaarke.Core.Tests and Spaarke.Scheduling.Tests.

**Test runs**:

| Suite | Passed | Failed | Skipped |
|---|---|---|---|
| ArchTests, full | 1561 | 0 | 0 |
| Integration (Sprk.Bff.Api.IntegrationTests) | 199 | 0 | 8 |
| Unit, broad filter plus the touched classes | 14345 | 0 | 7 |

The touched classes added to the unit filter were DataverseDescribe, DataverseReadQuery, DataverseSearchData, DataverseToolNameFreeze, DocumentNarrowing, ExternalLiveParentRule, StoredFileEndpointsTests and StoredFileServiceTests.

**Publish size**: `dotnet publish -c Release -r linux-x64 --self-contained false`, zipped, from clean `git archive` trees.

| Build | Zipped size |
|---|---|
| origin/master `de38296499` | 38,664,321 B (36.87 MB) |
| Base `786d1ea17a` | 39,505,518 B (37.68 MB) |
| This task `1ca81502a6` | 39,529,637 B (37.70 MB) |

- This task's delta is +24,119 B (+0.02 MB) against its base.
- The branch is +0.83 MB against master; nearly all of that is earlier ebilling work.
- Total size is well under the 55 MB review threshold.
- No package was added; no new CVE surface.

**/conflict-check**:
- No overlap with origin/master since the merge base, with work/ebilling-r1, or with any of the 25 open PRs.
- Soft overlap with the parallel task branches 092, 094 and 095-from-email on `InvoiceSubmissionContracts.cs`, `InvoiceSubmissionService.cs` and `InvoiceSubmissionEndpointsContractTests.cs`. This task's edits there are additive only: the `[JsonIgnore] InvoiceId` property, one select column with its mapping line, and new tests at the end of the class.
- No overlap with 089.

## uac-r2 conditions

1. **Nothing new is stored.** No column, row or cache is added. The marker `sprk_document.sprk_restrictedbysubmission` is only read. It is field-secured and refused in `ParentLineage.RefuseAppOnlyWrite` (ParentLineage.cs:1208-1210); nothing in this task writes it.
2. **Unknown fails closed.** An unreadable source, own value, marker, chain, communication, root or flag hides the invoice, and so does an unresolvable caller or a truncated Stored File page. Unknown is tested separately from Restricted on every axis and is never treated as false.
3. **The decision is server-side only.** Every surface gives a hidden invoice the same answer as a non-existent or cross-tenant id. Tests compare full ProblemDetails bodies (thread, documents), the 404 body with only the correlationId stripped (submission), and the error code and message (describe). No hidden-specific read or write follows.
4. **Caller rights.** The surfaces keep their existing gates, which already use `GetCallerRightsForAuthorizationGateAsync` (unknown and denied answer the same). This task adds no new caller-rights probe. The predicate itself reads app-only, inside the BFF, and returns only a hide set.
5. **One predicate.** Rules 2–4 are 188's `ExternalDocumentNarrowing.DecideAsync`, called through the single app-only seam `InvoiceVisibilityReaders.DecideSources`. There is no route-local check. The guard enforces that every external invoice-family surface reaches `ExternalInvoiceVisibility` and that only `ExternalAccessModule` and the three AI handlers take the family seam.

## Known limits

- **K: a message whose own regarding is unset** but whose thread regards an invoice. The invoice thread itself is gated at the record gate. A matter-thread message is checked by its own `sprk_regardinginvoice` only.
- **K: delete-own of a message on an invoice thread** is covered through the shared `RightsForInvoiceAsync` / record gate and the guard's `WiredReaches` pin (`AuthorizeDeleteOwnAsync`). It has no dedicated route test, because the delete fixture lacks invoice-parent support.

## Deviations from the POML

- The seams are injected as nullable `[FromServices]` parameters on the existing handlers, not as new required constructor dependencies. A missing registration fails closed (no predicate means hidden), and existing callers and tests keep compiling.
- The tests were added to the existing surface test classes rather than new files, so they reuse each class's fixture.
- Invoice upload reads the predicate twice: once in the rights gate and once in the upload gate. The extra read only adds cost; correctness is unaffected.
- The guard's pending 093, 130 and 152 entries are keyed by the guard's primitives (reader types, named external types, the external-plane namespace), not by method names, because those methods do not exist on this branch yet.
- One adversarial-review pass instead of two, as the operator directed.

## Escalation

- **A second app-only exception for the AI tools.** The AI handlers' rule is User-OBO reads. `AppOnlyInvoiceFamilyHiding` maps family rows to invoices and decides them app-only. It returns only a hide set and never row data, which matches the existing 188 document-narrowing exception. uac-r2 needs to accept it explicitly.
