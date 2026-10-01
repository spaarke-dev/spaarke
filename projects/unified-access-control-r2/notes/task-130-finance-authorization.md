# Task 130 — finance + scorecard recalculate IDOR, summary gate, id substitution (defect C8)

> **Status**: code complete on `task/uac-r2-130`; **one part escalated** (§6 — the confirm route's invoice
> create does not match the live `sprk_invoice` schema); **live gate pending** (§7, manual, no live writes
> were made by this run).
> **GitHub**: #1053. **Supersedes** the summary half of finding A-20 that task 003 closed (§8).

## 1. What was wrong (re-verified against source 2026-09-30)

| Route | Before | Defect |
|---|---|---|
| `POST /api/finance/{matters\|projects}/{id}/recalculate` | bare `RequireAuthorization()` | any signed-in user: app-only read of invoices/budgets, app-only write of 9 rollup fields, response discloses spend / budget / utilization / 12-month timeline |
| `POST /api/{matters\|projects}/{id}/recalculate-grades` | bare `RequireAuthorization()` | same shape (FinanceRollupEndpoints was copied from it): app-only KPI read + app-only write of 6 grade fields |
| `GET /api/finance/matters/{matterId}/summary` | group + route `"finance.read"` filter | matter id evaluated against `sprk_documents` → None → **403 for every caller** |
| `POST /api/finance/invoice-review/confirm`, `/reject` | group `"finance.read"` + route `"finance.confirm"` | filter never read the BODY: body-only → 400; `?documentId=<doc I can write>` passed while the handler acted on the BODY ids |
| `GET /api/finance/invoices/search` | group + route `"finance.read"` | optional `matterId`; `?documentId=<mine>` passed and the search ran **tenant-wide** |

## 2. What changed

**Placement (CLAUDE.md §10, `.claude/constraints/bff-extensions.md`)**: in the BFF — these are existing BFF
routes; no new service, no new endpoint, no new DI registration, **no NuGet change**.

- **`Api/Filters/FinanceAuthorizationFilter.cs`** — extended, not replaced, not duplicated (no new filter
  class; `RecordRouteAccessAuthorizationFilter` was NOT also extended). The id-fallback chain is deleted. A
  route now declares either a fixed `(operation, entity set, route key)` or a resolver returning the exact
  `(path, entity set, id, operation)` checks for the ids its handler consumes. Two paths: **Document**
  (`AuthorizeAsync` + rule chain — Write on a document) and **Record** (`GetCallerRecordAccessAsync` +
  `OperationAccessPolicy.HasRequiredRights`). Fail closed on: missing oid (401), missing token (None),
  resolver throw, seam throw, empty id, no declared target. Deny = 403 + `reasonCode`, or — for the four
  recalculate routes — `UniformRecordNotFound` (404, `sdap.access.deny.record_unavailable`, no id echoed).
- **`Api/Finance/FinanceEndpoints.cs`** — group-level filter removed; each of the 4 routes carries its own:
  summary → Read `sprk_matters(route matterId)`; search → `matterId` REQUIRED (exactly one value) + Read
  `sprk_matters`; reject → Write on body DocumentId (document path); confirm → Write on body DocumentId +
  AppendTo on `sprk_documents(DocumentId)`, `sprk_matters(MatterId)`, `sprk_organizations(VendorOrgId)`.
  Body ids are read from the BOUND argument (the instance the handler receives). Field validation runs
  first → empty id is the field-level 400 with no rights query. Search handler also refuses a null matterId.
- **`Api/Finance/FinanceRollupEndpoints.cs`, `Api/ScorecardCalculatorEndpoints.cs`** — each route gated
  `"finance.read"` on the fixed parent set, `FinanceDenial.UniformNotFound`; the handlers' `KeyNotFoundException`
  branch returns the SAME uniform 404 (previously echoed the id + entity label).
- **No-create write** — `IFieldMappingDataverseService.UpdateExistingRecordFieldsAsync` (new member;
  `DataverseWebApiService` PATCH + `If-Match: *`, 404/412 → `KeyNotFoundException`; the SDK impl throws per
  the RED-4 single-live-impl rule). `FinanceRollupService` and `ScorecardCalculatorService` write through it.
  `UpdateRecordFieldsAsync` is unchanged (InvoiceReviewService relies on its upsert to create the invoice).
- **`OperationAccessPolicy`** — new key `"finance.attach_invoice"` = AppendTo; stale `"finance.read"` comment
  corrected (it described the deleted fallback chain); `"finance.confirm"` comment re-pointed to the body id.
- **Docs**: `docs/architecture/{uac-access-control,sdap-bff-api-patterns,sdap-auth-patterns}.md` filter rows.

### §11 three-question justification (new surface)

| New surface | Existing (grep) | Extension? | Cost of doing nothing |
|---|---|---|---|
| `FinanceAuthorizationCheck` / `FinanceAuthorizationTargets` / `FinanceDenial` / `FinanceCheckPath` (types inside the existing filter file) | `FinanceAuthorizationFilter` (`ExtractResourceId` chain), `RecordRouteAccessAuthorizationFilter` (fixed `{entityLogicalName}/{recordId}` keys, 403 only) | They ARE the extension of `FinanceAuthorizationFilter` — the per-route declaration the constraint asks for; no second filter | Without a declaration the filter can only guess the id → the substitution and wrong-entity defects in §1 |
| `"finance.attach_invoice"` policy key | `"entity.associate_document"` (AppendTo) | Reusing it would log every confirm deny as an Office document association — misdescribes the operation | Without an AppendTo key, confirm cannot ask AppendTo; `"finance.confirm"` (Write) is the wrong right for linking |
| `UpdateExistingRecordFieldsAsync` | `UpdateRecordFieldsAsync` (upsert; `grep` → 10 test files mock it with all 5 args) | An optional flag would break every Moq expression tree using it (CS0854) and put a create/no-create switch on a shared write; InvoiceReviewService depends on the upsert | A recalculate racing a delete recreates the matter/project as an empty row (Dataverse documents PATCH-without-If-Match as upsert) |

Entity sets: **no new logical-name → set map**; `EntityAccessFilter.EntitySetByType` and
`SemanticSearchAuthorizationFilter.AuthorizableEntitySets` are **unchanged**. Per-route constants, each read
from live metadata (spaarkedev1, read-only GET `EntityDefinitions(LogicalName='…')?$select=EntitySetName`,
2026-09-30) and pinned by literal in `FinanceEndpointsAuthorizationContractTests`:

| Logical | Live EntitySetName | Constant |
|---|---|---|
| `sprk_matter` | `sprk_matters` | `MatterEntitySet` |
| `sprk_project` | `sprk_projects` | `ProjectEntitySet` |
| `sprk_document` | `sprk_documents` | `DocumentEntitySet` |
| `sprk_organization` | **`sprk_organizations`** | `VendorOrganizationEntitySet` |

## 3. Guard (RouteAuthorizationGuardTests)

`Api/Finance/FinanceEndpoints.cs`, `Api/Finance/FinanceRollupEndpoints.cs`, `Api/ScorecardCalculatorEndpoints.cs`
added to `GovernedFiles` as `Scope.RouteLevelGate` with reasons. **No waiver** for any of the 8 routes. Census
count unchanged (120 — no file added or removed). `FinanceAuthorizationFilter` is credited by `FilterMarker`
(`.Add\w*AuthorizationFilter`) and inspected by Rule B (references `AuthorizationService`).

**Perturbation proof (2026-09-30)**: removed `.AddFinanceAuthorizationFilter(...)` from
`POST /api/projects/{projectId:guid}/recalculate-grades` → Rule A FAILED with
`Ungated routes: POST /api/projects/{projectId:guid}/recalculate-grades at Api/ScorecardCalculatorEndpoints.cs:60`;
restored → green.

## 4. Tests — and proof each one bites

| File | What |
|---|---|
| `tests/integration/contract/Api/Finance/FinanceEndpointsAuthorizationContractTests.cs` (new, 49 cases) | REAL `MapFinanceEndpoints` + `MapFinanceRollupEndpoints` + `MapScorecardCalculatorEndpoints`, real `AuthorizationService` + `OperationAccessRule`; substitution only at `IAccessDataSource` (hand-written recording fake) and the service seams |
| `tests/integration/data-mutation/FinanceRollup/RecalculateWriteNoCreateTests.cs` (new, 4) | `If-Match: *` sent; 404/412 → `KeyNotFoundException`; `UpdateRecordFieldsAsync` still sends no `If-Match` |
| `tests/Spaarke.ArchTests/RecalculateServicesCallerFreeGuardTests.cs` (new, 3) | NetArchTest: neither service depends on caller context (job handler unaffected); both write update-only |
| `ScorecardCalculator{Service,Integration,Error}Tests.cs` | mocks retargeted to `UpdateExistingRecordFieldsAsync` (§10 test-update obligation) |
| `OperationAccessPolicyCompletenessTests.cs` | `finance.attach_invoice` discovered + AppendTo + no Delete/Share |
| `FinanceRollupEndpointsContractTests.cs` | 401 tests unchanged + pointer remark |

**Seeded-violation proofs (each seeded, run, watched fail, restored):**

1. summary filter removed → `EveryRoute(summary)`, `Summary_NonReader…`, `Summary_Reader…` FAIL.
2. confirm vendor check replaced → `Confirm_MissingAppendTo…(vendor)`, `Confirm_AuthorizedBodyOnly…` FAIL.
3. search old fallback restored (query documentId authorizes) → all 3 `Search_WithoutMatterId…` FAIL.
4. scorecard KeyNotFound reverted to the id-echoing 404 → both `Recalculate_RecordDeletedAfterTheCheck…` FAIL.
5. filter fail-open on seam exception → all 4 `Recalculate_AccessSeamThrows…` FAIL.
6. `If-Match` line removed → `UpdateExistingRecordFieldsAsync_SendsIfMatchStar…` FAIL.
7. scorecard write back to `UpdateRecordFieldsAsync` → arch `RecalculateWritesAreUpdateOnly(Scorecard)` + 3 `ScorecardCalculatorServiceTests` + 2 `Recalculate_ScorecardReader…` FAIL.
8. `AuthorizationService` member added to `FinanceRollupService` → arch `RecalculateServicesDoNotDependOnCallerContext` FAIL.

Scope note (one line each, beyond the closed set): the arch caller-free test is how the "job handler
unaffected" criterion is made testable; the `UpdateRecordFieldsAsync`-unchanged test pins the scoping
constraint ("not applied globally").

**Known limit**: the finance recalculate 200 cannot be asserted in-process — `FinanceRollupService` unwraps a
concrete `ServiceClient`. The test proves the authorized request REACHES the handler (its own 500 on the
unwrappable double; a denial is 404). The 200 is live-gate item (b).

## 5. Upsert verification

Live PATCH testing was **not run** (this run is read-only on Dataverse). Basis for applying the guarantee:
Dataverse Web API documents PATCH-without-`If-Match` as upsert ("Upsert a table row" / "Prevent create in
upsert: `If-Match: *`"), and the codebase itself depends on it — `InvoiceReviewService.CreateInvoiceRecordAsync`
creates the invoice by PATCHing a new GUID. `If-Match: *` on an existing row is a no-op precondition, so the
guarantee costs nothing if the platform did not upsert. **Live confirmation = gate item (c).**

## 6. 🔔 ESCALATED — confirm's invoice create does not match the live schema (trigger 3 fired)

Read-only metadata (spaarkedev1, `EntityDefinitions(LogicalName='sprk_invoice')/Attributes` and
`/ManyToOneRelationships`, 2026-09-30):

| `InvoiceReviewService` writes | On live `sprk_invoice`? |
|---|---|
| `_sprk_document_value` = body DocumentId | **No `sprk_document` lookup exists.** The link is the OTHER way: `sprk_document.sprk_invoice` (rel `sprk_document_Invoice_n1`) and `sprk_document.sprk_relatedinvoice` |
| `_sprk_matter_value` = body MatterId | lookup exists, nav property **`sprk_Matter`**; `_x_value` is not a writable property → must be `sprk_Matter@odata.bind` |
| `_sprk_vendororg_value` = body VendorOrgId | lookup exists, nav property **`sprk_vendororg`** → `sprk_vendororg@odata.bind` |
| `sprk_reviewernotes`, `sprk_createdon` | **do not exist** |
| (not written) `sprk_name` | ApplicationRequired (not enforced by the API, but the row would be nameless) |

Also on reject: `sprk_invoicerejectionnotes` does **not** exist on `sprk_document` (live has
`sprk_invoicereviewnotes`) → a reject WITH notes would 400.

So the create PATCH always fails today (an undeclared property), independent of the lookup format. Task 130
makes confirm reachable for the first time, so this is now a live 500 on an authorized confirm. It was not
fixed here because the escalation trigger fires literally — a caller-supplied id (DocumentId) is written into
a field that is **not a lookup** (it does not exist) — and because the fix changes the right being checked:

- **(a) RECOMMENDED** — rewrite the create against the live schema: create the invoice with
  `sprk_Matter@odata.bind` / `sprk_vendororg@odata.bind` (+ `sprk_name`), drop `sprk_createdon` /
  `sprk_reviewernotes` (or map notes to the document's `sprk_invoicereviewnotes`), then link the document by
  PATCHing `sprk_document.sprk_Invoice@odata.bind`. The DOCUMENT is then the record holding the lookup, so the
  right on the document becomes **Append** (not AppendTo) alongside Write; the invoice side's AppendTo is the
  app's own (app-created row). Proposed key change: document check `"finance.attach_invoice"` (AppendTo) →
  a new `"finance.link_invoice"` = Append, or Write|Append on the existing document check.
- **(b)** add a `sprk_invoice.sprk_document` lookup (schema change — live write, owner decision) and keep the
  code's direction.

**Implemented authorization today** follows the POML's closed criteria (AppendTo on the document). That is
fail-closed: if (a) is chosen, AppendTo-on-document may over-restrict, never under-restrict.

## 7. MANUAL LIVE GATE — pending (dev; existing non-admin test users in their current BU; no relocation)

Read-only inputs gathered now:

- **(g) Create privilege on `sprk_invoice`** — `Test User 1` (`testuser1@spaarke.com`, the only `testuser*`
  account): **`prvCreatesprk_Invoice` = Deep** via *Spaarke Core User* and *Spaarke Office Add In User*. The
  confirm privilege-escalation trigger therefore does **not** fire for this user.
- ⚠️ **Same read**: no role held by Test User 1 grants **`prvAppendTosprk_Organization`** (NONE in all four
  roles). Under the confirm rule, Test User 1 is denied every confirm at the vendor-organization check. Gate
  item (f)'s positive half needs a user who holds AppendTo on `sprk_organization` — a role change, which this
  run may not make. Owner input needed (grant AppendTo on sprk_organization to *Spaarke Core User*, or name a
  different test user). Confirm has no shipped client (`grep` of `src/**/*.{ts,tsx,js}`: 0 hits; no Power
  Automate flow in dev references `/api/finance` — read-only `workflow.clientdata` query: 0 rows).
- Other Test User 1 rights (for gate planning): `prvWritesprk_Document` Deep, `prvAppendTosprk_Document` Deep,
  `prvAppendTosprk_Matter` Deep (Core User).

To run after deploy (record ids redacted to 8 chars):

- (a) user with NO access to matter M → `POST /api/finance/matters/{M}/recalculate` and
  `POST /api/matters/{M}/recalculate-grades` → 404; M's `modifiedon` unchanged.
- (b) user with Read on M → both 200.
- (c) recalculate on a random GUID → 404; no `sprk_matter`/`sprk_project` row with that id afterwards
  (also confirms §5 live).
- (d) `GET /api/finance/matters/{M}/summary` → 200 (or documented 404 "no financial data") for the reader;
  403 for the non-reader.
- (e) matter form subgrid rollup still refreshes for the reader; KPI quick-create save on a readable matter
  updates grades with **no** "Unable to recalculate grades" alert.
- (f) confirm — **blocked by §6 and by the AppendTo-on-organization gap above**.
- (g) recorded above.

## 8. Task-003 supersession

Task 003 (`notes/task-003-operation-rights-decisions.md` §4, line ~88) marked `FinanceAuthorizationFilter`
fixed after adding the `"finance.read"` / `"finance.confirm"` policy keys. That fixed only the
"unknown_operation" half of A-20: the summary route still evaluated the matter id against `sprk_documents`
and denied every caller. Task 130 closes the remaining half; task 003's POML is not reopened.

## 9. Quality gates (task-execute Step 9.5)

- **Suites**: BFF unit suite 13104 passed / 56 skipped (pre-existing) / 0 failed; ArchTests 340/340.
- **CVE**: `dotnet list package --vulnerable --include-transitive` → no vulnerable packages. No `.csproj` change.
- **adr-check**: 0 violations. ADR-001 (Minimal API, no new routes), ADR-003 (fail closed on every path, deny
  codes, decisions not cached), ADR-008 (per-route endpoint filters, no middleware), ADR-010 (no new interface;
  one member on an existing two-implementation seam), ADR-013 (no AI dependency), ADR-019 (ProblemDetails +
  reasonCode + correlation id), ADR-038 (no `Mock<HttpMessageHandler>`, no DI-registration or ctor-null tests),
  ADR-002 (no plugin; the confirm create's app-only write pre-exists and is part of the §6 escalation).
- **code-review**: no Critical. Warnings / suggestions recorded for the reviewer:
  - W: confirm is now reachable and its create fails against the live schema — §6 (escalated, not silent).
  - W: `FinanceEndpoints.cs` 378 → 523 lines (resolvers added). Cohesive (each resolver sits beside the
    handler whose binding it mirrors, which is the point); flagged per §11.5, not decomposed.
  - S: confirm asks Dataverse about the same document twice (Write via the document path, AppendTo via the
    record path) — kept because the constraint pins Write to the document path; one extra RPA round trip.
  - S: `UpdateExistingRecordFieldsAsync` repeats ~10 lines of `UpdateRecordFieldsAsync`'s error logging; left
    separate rather than threading an If-Match flag through the shared private PATCH helper.
  - S: `UniformRecordNotFound` carries a `sdap.access.deny.*` code even on the post-check KeyNotFound path —
    deliberate (one code for all three cases), noted so it is not "fixed" into an oracle.

## 10. Related, not in this task

- C12 / task 132: `CachedAccessDataSource` caches a None record snapshot for 60 s, so a newly granted reader may
  see the recalculate 404 for up to a minute.
- Publish size: not measured here (batch measurement by the main session). No NuGet change.
