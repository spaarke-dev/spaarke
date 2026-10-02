# Task 130 — finance + scorecard recalculate IDOR, summary gate, id substitution (defect C8)

> **Status**: code complete on `task/uac-r2-130`. The §6 escalation is **RESOLVED** (owner decision G5, round 3b,
> 2026-10-01 — option (a), adjusted: check as the user, create as the app, owned by the team; implemented in §11).
> **Live gate pending** (§7 + §11.6, manual; no live writes were made by either run).
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

## 6. ✅ RESOLVED (was 🔔 ESCALATED) — confirm's invoice create did not match the live schema (trigger 3 fired)

> Resolved 2026-10-01 by owner decision G5 (round 3b); implementation in §11. The text below is the original escalation.

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

## 11. Escalation resolved — owner decision G5 (round 3b, 2026-10-01), implemented

> Owner: "yes if user can create the invoice; but won't this use the same rule that assigned to the bu/team not
> individual". Recorded binding in `notes/session27-owner-decisions-and-research.md` round 3b: the BFF checks AS THE
> USER (Create on sprk_invoice, AppendTo on matter and vendor, Write+Append on the document), then the APP creates
> the invoice OWNED BY THE TEAM (RecordOwnershipResolver, record-first from the matter), never user-owned.

**Interpretation recorded (fix round 2): `prvCreatesprk_Invoice` held at ANY depth satisfies G5's "if user can
create the invoice".** The check asks Dataverse whether the caller holds the Create privilege on `sprk_invoice` at
all (`RetrieveUserSetOfPrivilegesByNames` answers the privilege with its depth — Basic / Local / Deep / Global — and
`ResponseGrantsPrivilege` accepts any of them). It does NOT ask whether the caller could create THIS invoice with
THIS owner. That is deliberate: the app creates the invoice owned by the matter's TEAM, not by the user, so the
depth question ("could the user create a row owned by that team / in that BU?") does not describe what actually
happens — a Basic-depth user could never create a team-owned row themselves, yet G5 still wants their confirm to
work when they can create invoices. The per-record scope is carried instead by the record checks (Write+Append on
the document, AppendTo on the matter and the vendor), which ARE asked of the specific records. If the owner wants
the depth to matter (e.g. Basic = deny), that is a change to `ResponseGrantsPrivilege` plus a mapping from the
team's BU to a required depth — not done, not assumed.

### 11.1 Live schema re-verified (spaarkedev1, read-only GETs, 2026-10-01)

| Fact | Live value |
|---|---|
| `sprk_invoice` set / primary name / ownership | `sprk_invoices` / **`sprk_name`** (ApplicationRequired, MaxLength 850) / UserOwned (team-ownable) |
| invoice → matter lookup | `sprk_matter`, nav **`sprk_Matter`** → `sprk_Matter@odata.bind: /sprk_matters(id)` |
| invoice → vendor lookup | `sprk_vendororg` → `sprk_organization`, nav **`sprk_vendororg`** → `/sprk_organizations(id)` |
| document → invoice link | `sprk_document.sprk_invoice`, rel `sprk_document_Invoice_n1`, nav **`sprk_Invoice`** → `sprk_Invoice@odata.bind: /sprk_invoices(id)` |
| document notes column | **`sprk_invoicereviewnotes`** (Memo, 5000); `sprk_invoicerejectionnotes` absent |
| absent columns (dropped) | `sprk_invoice.sprk_reviewernotes`, `sprk_invoice.sprk_createdon`, `sprk_invoice.sprk_document` |
| option sets | doc review status ToReview 100000000 / ConfirmedInvoice 100000001 / RejectedNotInvoice 100000002; invoice status ToReview 100000000; extraction NotRun 100000000 |
| `sprk_organization` primary name | `sprk_organizationname` |
| privilege name | **`prvCreatesprk_Invoice`** (schema name `sprk_Invoice` — not all lower case) |
| `RetrieveUserSetOfPrivilegesByNames` shape | `{"RolePrivileges":[{"Depth":"Deep","PrivilegeName":"prvCreatesprk_Invoice",…}]}` (called read-only for Test User 1) |

### 11.2 What changed

1. **Check as the user** (`FinanceAuthorizationFilter`, `FinanceEndpoints.ResolveConfirmTargets`). Confirm now asks, in
   order: Write on the document (document path, `finance.confirm`); **Write+Append** on the document (record path,
   NEW key `finance.link_invoice`) — the document holds the lookup, so Append replaces the earlier AppendTo-on-document;
   AppendTo on the matter and the vendor (`finance.attach_invoice`, now scoped to those two); and the caller's
   **Create privilege on sprk_invoice** through a new `FinanceCheckPath.Privilege`. Denials: 403
   `sdap.access.deny.insufficient_rights` / `sdap.access.deny.insufficient_privilege`; a throwing check →
   `sdap.access.error.system_failure`. Fails closed when the probe is not registered.
   - **Privilege mechanism — choice and justification.** `CallerRecordAccessProbe.CallerHoldsPrivilegeAsync` (new
     virtual member): OBO token → `WhoAmI()` → `systemusers({me})/RetrieveUserSetOfPrivilegesByNames`. Rejected
     `UserPrivilegeChecker`: Read-only, app-identity impersonation (the caller's id as DATA — the A-2 shape), and a
     24 h cache that would honour a removed Create privilege a day late (against round 3's "minutes, not hours").
     The probe already owns the caller-as-credential OBO path; no new class, no new DI registration.
2. **Create as the app, owned by the team** (`InvoiceReviewService`). `IRecordOwnershipResolver.ResolveOwningTeamAsync`
   with target `sprk_matter`/MatterId and NO caller ids (record-first; a named-but-unresolvable target refuses, never
   falls back to the user). For a secure matter the matter's owning BU is the Secure Record BU, so the answer is that
   BU's owner team. Unresolved → `OwnerTeamUnresolved`, 403, **nothing written**. `ownerid@odata.bind: /teams(id)`.
3. **Live-schema create**: `sprk_Matter@odata.bind`, `sprk_vendororg@odata.bind`, `sprk_name` = "{vendor} - {invoice
   number}" else "{vendor} - {date}" else "{vendor} - Invoice" (capped 850); `sprk_createdon` / `sprk_reviewernotes`
   dropped; reviewer notes go to the document's `sprk_invoicereviewnotes`. All writes moved to the Web API seam
   (`IFieldMappingDataverseService`): the old status write went through the SDK `UpdateDocumentFieldsAsync` with a
   bare `int` for a picklist, which the SDK rejects — so confirm and reject were both latent failures.
4. **No partial write**. The order is create → link → status LAST → enqueue:
   - create fails → nothing written;
   - link (update-only PATCH of `sprk_Invoice@odata.bind`) fails → the created invoice is **deleted**
     (`IGenericEntityService.DeleteAsync`, `CancellationToken.None`), 500 `link_failed` "Nothing was saved"; if the
     delete also fails → 500 `link_failed_invoice_orphaned` naming the invoice id;
   - status fails → 500 `status_not_updated` naming the invoice id. **A retry is idempotent**: confirm first reads the
     document's `_sprk_invoice_value`; a link to an invoice with the SAME matter and vendor is resumed (no second
     create or link); a link to a different matter's/vendor's invoice → 409 `document_linked_elsewhere`, nothing written;
   - enqueue fails → 500 `extraction_not_queued` naming the invoice; the job's idempotency key is per invoice;
   - document gone → 404 `document_not_found`, nothing written.
5. **Reject** writes `sprk_invoicereviewnotes` (update-only); a deleted document → 404.
6. **Finance recalculate KeyNotFound-after-check**: `FinanceRollupService` unsealed with `virtual`
   `RecalculateMatterAsync`/`RecalculateProjectAsync` (ADR-010 concrete-with-virtual-seam, the probe precedent), so
   the uniform 404 is now tested on the two finance routes (previously scorecard only).
7. **SpendSnapshotGenerationJobHandler**: a `KeyNotFoundException` from the rollup step (matter deleted after
   queueing) is a logged skip and the job COMPLETES; other failures keep their retry/poison classification.

### 11.3 §11 three-question justification (new surface in this round)

| New surface | Existing (grep) | Extension? | Cost of doing nothing |
|---|---|---|---|
| `FinanceCheckPath.Privilege` + `FinanceAuthorizationCheck.CallerPrivilege` | the filter's Document/Record paths (record rights only) | It IS the extension of the one finance filter — a third path kind, no new filter | A table privilege has no record to ask RetrievePrincipalAccess about; without it "Create on sprk_invoice, as the user" cannot be checked |
| `CallerRecordAccessProbe.CallerHoldsPrivilegeAsync` (+ `ResponseGrantsPrivilege`) | `UserPrivilegeChecker` (Read-only, impersonated, 24 h cache); `GetCallerRightsAsync` (record rights) | Extends the existing OBO probe; reuses its exchange + WhoAmI | App-created invoices for users who could never create one (G5's condition unenforced) |
| `"finance.link_invoice"` key (Write + Append) | `finance.attach_invoice` (AppendTo), `finance.confirm` (Write) | Neither names Append; changing attach_invoice would alter the matter/vendor checks | The document holds the lookup; without Append the link PATCH would 403 live after the filter allowed it |
| `InvoiceReviewException` + `InvoiceReviewFailure` | generic `catch (Exception)` → one 500 | The handler must know which state was left; a message string is not a contract | A partial confirm would be an anonymous 500 with no invoice id to find or retry |
| `virtual` on `FinanceRollupService` entry points | sealed concrete over an unwrapped `ServiceClient` | Minimal seam, no interface (ADR-010) | Items 5 and 6 (post-check 404, job skip) untestable in-process |

Placement (CLAUDE.md §10): all in the BFF — existing routes and services, no new endpoint, no new DI registration,
no NuGet change. `IRecordOwnershipResolver` was already registered unconditionally (`AddDataverseMetadataServices`,
Program.cs); `InvoiceReviewService` is its first non-Office consumer (one of GitHub #1034's "other BFF writers").

### 11.4 Tests and seeded-violation proofs (this round)

New: `tests/integration/data-mutation/FinanceInvoiceReview/InvoiceReviewWritePathTests.cs` (18 cases — order, exact
payloads, team ownership, every failure point, retry, reject notes, name),
`tests/integration/data-mutation/FinanceRollup/SpendSnapshotDeletedMatterTests.cs` (2),
`tests/integration/auth/UnifiedAccessControl/CallerPrivilegeCheckTests.cs` (8). Extended:
`FinanceEndpointsAuthorizationContractTests` (finance post-check 404 ×2, AppendTo-not-Append, no Create privilege,
privilege check throws, 7 failure renderings; probe registered in the host), completeness + characterization tests
(`finance.link_invoice`), `RouteAuthorizationGuardTests` reason text.

**Suites (2026-10-01)**: BFF unit suite 13155 passed / 56 skipped / 1 failed — the failure is
`PinnedMemoryEndpointsContractTests.DeletePin_Authenticated_Returns204AndEmitsCounter` (memory endpoints, untouched
here; a metrics-counter test that flakes under full-suite load — its class re-run in isolation 3× = 15/15 passed).
ArchTests 340/340. Affected-test filter (finance + policy + new files): 157/157.

Seeded, run, watched fail, restored (2026-10-01):

| # | Seed | Failed |
|---|---|---|
| 1 | Create-privilege check removed from confirm | `Confirm_CallerWithoutCreateOnSprkInvoice…`, `Confirm_PrivilegeCheckThrows…`, `Confirm_AuthorizedBodyOnly…` |
| 2 | document check back to `finance.attach_invoice` | `Confirm_DocumentWithAppendToButNotAppend…` (+ every confirm case granting Append) |
| 3 | Privilege path fails open | `Confirm_CallerWithoutCreateOnSprkInvoice…` |
| 4 | compensation delete removed | `Confirm_LinkFails_Deletes…`, `Confirm_LinkFailsAndTheUndoFails…` |
| 5 | status written before the link | `Confirm_WritesCreateThenLinkThenStatus…`, payload + link-failure tests |
| 6 | `_sprk_matter_value` key reintroduced | `Confirm_CreatePayload_BindsLookupsByNavigationProperty…` |
| 7 | resume removed (seeded alone) | `Confirm_StatusUpdateFails_…ARetryResumesWithoutASecondInvoice` |
| 8 | notes column back to `sprk_invoicerejectionnotes` | `Reject_WithNotes_WritesTheLiveNotesColumn…` |
| 9 | job skip removed | `MatterDeletedBeforeTheRollup_CompletesTheJob…` |
| 10 | finance KeyNotFound echoes the id | both `Recalculate_FinanceRecordDeletedAfterTheCheck…` |
| 12 | parser reads an empty array as held | `AnythingElse_IsNotHeld({"RolePrivileges":[]})` |
| 13 | `finance.link_invoice` weakened to Write | completeness rights row, `LinkInvoice_RequiresWriteAndAppend…`, document contract case |

### 11.5 Related findings, not fixed here (outside this task's files)

- **`OfficeService.QuickCreateAsync` (Invoice leg) writes `sprk_invoicename`**, which does not exist on live
  `sprk_invoice` (primary name is `sprk_name`; `sprk_invoicename` exists only as the formatted name of
  `sprk_document.sprk_invoice`). The Office "New invoice" quick-create therefore fails live. Owner: word-add-in-r1.
- ~~`sprk_invoice.sprk_regardingrecordtype` is ApplicationRequired (not API-enforced); confirm does not set it.~~
  Set by fix round 2 — see §11.7 item 8.
- Secure matters: the resolver answers the Secure Record BU's **default** owner team. When C10 Part 1 (named
  non-default owner team, task 133) lands, the record-first answer for a secure matter must follow it.
- `sprk_invoice.sprk_issecure` is not copied from a secure matter — child-coverage scope of C10 Part 2 (tasks 145/146).

### 11.6 Live gate additions (manual, pending)

- **(f) unblocked**: read-only `RetrieveUserSetOfPrivilegesByNames` for Test User 1 (2026-10-01) now returns
  `prvAppendTosprk_Organization` **Deep** (and `prvCreatesprk_Invoice` Deep), so the §7 AppendTo-on-organization gap
  is gone. The same read returns `prvAppendsprk_Document` Deep, `prvWritesprk_Document` Deep and
  `prvAppendTosprk_Matter` Deep, so Test User 1 holds every privilege the new confirm rule asks.
- (h) confirm as Test User 1 → 202; read back: `sprk_invoice.ownerid` = the matter's BU owner team (not the user),
  `_sprk_matter_value`/`_sprk_vendororg_value` set, `sprk_name` derived; `sprk_document._sprk_invoice_value` = the
  invoice; review status ConfirmedInvoice. Repeat the confirm → same invoice id, no second row.
- (i) `RetrieveUserSetOfPrivilegesByNames` under the caller's OBO token answers for that user (incl. a Create held
  only via a team role, if a test user has one). The function was verified with an admin token only.
- (j) reject-with-notes → `sprk_invoicereviewnotes` populated.

### 11.7 Fix round 2 (branch `task/uac-r2-130b`, 2026-10-01) — review items 1-8

Live facts read for this round (spaarkedev1, read-only GETs / Dataverse SQL, 2026-10-01):

| Fact | Live value |
|---|---|
| a row's ETag | `"@odata.etag":"W/\"10039914\""` with `"versionnumber":10039914` — the ETag IS the version |
| `sprk_document_Invoice_n1` cascade | Delete = **RemoveLink** (deleting a linked invoice clears the document's lookup) |
| `sprk_invoice.sprk_regardingrecordtype` | Lookup → `sprk_recordtype_ref`, **ApplicationRequired**, nav prop `sprk_regardingrecordtype`, rel `sprk_sprk_recordtype_ref_sprk_invoice_sprk_regardingrecordtype`; `sprk_recordtype_refs` set |
| existing invoices' value | 10 rows read: 6 null; the 4 set ALL point at `e8547bb4-…` = the `sprk_recordtype_ref` row with `sprk_recordlogicalname = 'sprk_matter'` (and carry `sprk_regardingrecordid` = the matter id, `sprk_regardingrecordname` = the matter name) |

1. **Concurrency — no partial write between two confirms.** The resume check now also reads the document's
   `versionnumber`, and the link is `UpdateRecordFieldsIfUnchangedAsync` (`If-Match: W/"<version>"`, new on
   `IFieldMappingDataverseService`; Web API impl; the ServiceClient impl throws like its update-only sibling). A 412
   surfaces as `DBConcurrencyException`. On it the confirm **deletes its own just-created invoice**
   (`CancellationToken.None`) and re-runs the resume check: the document now linked to an invoice for the same
   matter and vendor → **resume with that invoice** (still exactly one invoice; the extraction key is per invoice);
   linked to another matter's/vendor's invoice → 409 `document_linked_elsewhere`; changed but not linked → new 409
   `sdap.finance.invoice_review.document_changed` ("Nothing was saved; retry"). An unreadable version refuses before
   any write. **Lost responses:** a create that reports failure is followed by a read of the invoice id (it is ours,
   client-generated): present → deleted, the original failure rethrown ("nothing saved"); cannot be ruled out or
   removed → new 500 `create_failed_invoice_may_remain` naming the id. A link that reports failure is followed by a
   read of the document: if it points at our invoice the link landed and the confirm continues (no undo); otherwise
   the invoice is deleted as before.
   Tests (deterministic — a create gate holds both confirms after they have read the same version, then releases
   them in a chosen order; no sleeps): `Confirm_TwoConcurrentConfirms_SameMatter_LeaveExactlyOneInvoice_InEitherOrder`
   (×2 orders), `Confirm_ConcurrentConfirmForAnotherMatterLinkedFirst_TheLoserIs409_AndLeavesNoInvoice` (×2),
   `Confirm_DocumentChangedWithoutALinkBetweenCheckAndLink_Is409_AndLeavesNoInvoice`,
   `Confirm_CreateResponseLost_ButTheRowWasCreated_…`, `Confirm_CreateResponseLost_AndTheRowCannotBeRemoved_…`,
   `Confirm_LinkResponseLost_ButTheLinkLanded_…`; wire: `ConditionalLinkWriteTests` (weak ETag sent; 412 →
   `DBConcurrencyException`, not "not found"; 404 → `KeyNotFoundException`).
   **Residuals (recorded, not closable in-process) — (a) and (b) CLOSED by fix round 3, §11.8 items 1 and 4:** (a) both racers run the status write and submit extraction with
   the SAME idempotency key, used as the Service Bus `MessageId` — a second message is suppressed only if the queue
   has duplicate detection enabled (not verified here), otherwise the job runs twice for one invoice (the job's own
   idempotency is out of scope); the loser's returned `jobId`/`statusUrl` may name a suppressed message. (b) A
   compensating delete whose response is lost reports `link_failed_invoice_orphaned` for an invoice that may in fact
   be gone — over-reporting, not an orphan. (c) If the post-failure read of the document itself fails, the link is
   treated as not landed and the invoice deleted; if it HAD landed, the RemoveLink cascade clears the document's
   lookup, so the end state is still "nothing saved". (d) If both the create's existence read and the delete fail,
   the invoice may remain unlinked — named in the 500 for manual removal.
2. **"No probe registered → deny" now proven.** `FinanceAuthHost.StartAsync(registerProbe: false)`; new
   `Confirm_NoPrivilegeProbeRegistered_IsDenied_NeverAllowed` grants every record right and expects 403
   `system_failure` with all four record checks asked and the service untouched.
3. **Reject on a deleted document** through the real host: `Reject_DocumentDeletedAfterTheCheck_Returns404WithReasonCode`
   (`RejectInvoiceAsync` throws `KeyNotFoundException`). The 404 now carries `reasonCode`
   `sdap.finance.invoice_review.document_not_found` (shared constant `InvoiceReviewException.DocumentNotFoundReasonCode`).
4. **Compensating delete on `CancellationToken.None`**: `Confirm_RequestCancelledBetweenCreateAndLink_TheUndoStillRuns`
   cancels the request token the moment the invoice exists; the fake transport (like the real one) throws on a
   cancelled token, the link fails, and the delete is asserted to run with an un-cancelled token and to remove the row.
5. **409 `document_linked_elsewhere` never names the other invoice.** The service logs the linked invoice id
   (warning) and throws with a generic message and NO `InvoiceId`; `ConfirmFailure` additionally renders that failure
   with a fixed detail and never adds `invoiceId` — even an exception carrying the id cannot leak it. Tests: the
   service case asserts message + `InvoiceId`; `Confirm_DocumentLinkedToAnotherMattersInvoice_Is409_AndNeverNamesThatInvoice`
   asserts the HTTP body (both id formats, no `invoiceId` key).
6. **`CallerPrivilegeCheckTests.NoCallerToken_DeniesWithoutAnyExchange` isolates the token guard.** The probe is now
   built with FULL OBO configuration (tenant, client, environment URL, an `OrderedCredentialClientProvider` whose
   managed-identity assertion stub records every mint and then fails like an unreachable identity, so nothing reaches
   the network). Null / empty / whitespace token → denied with ZERO mints. A control
   (`Control_WithACallerToken_TheSameConfigurationReachesTheExchange`) proves the same configuration does reach the
   exchange when a token is present — the previous version passed with an unconfigured probe, i.e. for the wrong reason.
7. **G5 depth interpretation** recorded at the top of §11.
8. **`sprk_regardingrecordtype` set** on the created invoice: `sprk_regardingrecordtype@odata.bind:
   /sprk_recordtype_refs(<id>)`, the id resolved at runtime by `ICommunicationDataverseService.QueryRecordTypeRefAsync("sprk_matter")`
   (the existing resolver used by `TodoRegardingBuilder` / `IncomingAssociationResolver` — the GUID differs per
   environment). Unresolvable → left unset with a warning (the `TodoRegardingBuilder` precedent; the column is
   form-level required only, as the 6 null live rows show). **Not done (scope):** the 4 typed live invoices also carry
   `sprk_regardingrecordid` / `sprk_regardingrecordname`; the item asked for the type only, so the other resolver
   fields are left for an owner call. → Done in fix round 3, §11.8 item 5.

**§11 justification (new surface this round):**

| New surface | Existing (grep) | Extension? | Cost of doing nothing |
|---|---|---|---|
| `IFieldMappingDataverseService.UpdateRecordFieldsIfUnchangedAsync` | `UpdateExistingRecordFieldsAsync` (`If-Match: *`), `UpdateRecordFieldsAsync` (upsert); no other `If-Match` in `Spaarke.Dataverse` | Adding an optional version parameter to `UpdateExistingRecordFieldsAsync` would break every Moq expression-tree setup of it (CS0854) and blur its 412 = "not found" contract | Two concurrent confirms orphan an invoice (the second link overwrites the first) |
| `InvoiceReviewFailure.DocumentChangedConcurrently` / `.CreateFailedInvoiceMayRemain` | the existing failure enum | They ARE extensions of it | A concurrent edit would surface as a generic 500; a possibly-orphaned invoice would be anonymous |
| `ICommunicationDataverseService` dependency on `InvoiceReviewService` | `QueryRecordTypeRefAsync` (already registered, `GraphModule`) | Reuse, no new resolver | `sprk_regardingrecordtype` (ApplicationRequired) left empty on every confirmed invoice |

No new DI registration, endpoint or package.

**Seeded, run, watched fail, restored (2026-10-01)** — script-applied seeds, each restored byte-for-byte from a backup:

| # | Seed | Result |
|---|---|---|
| S1 | link made unconditional (`UpdateExistingRecordFieldsAsync`) | 5/5 concurrency tests failed |
| S1b | 412 branch does not delete its own invoice | 5/5 failed |
| S1c | lost-create clean-up removed | 2 of 3 `ResponseLost` tests failed (the link one is unaffected, as expected) |
| S1d | landed link treated as failed | `Confirm_LinkResponseLost…` failed |
| S2 | no probe → allow | `Confirm_NoPrivilegeProbeRegistered…` failed |
| S3 | reasonCode removed from reject 404 | `Reject_DocumentDeletedAfterTheCheck…` failed |
| S4 | compensating delete given the request token | `Confirm_RequestCancelledBetweenCreateAndLink…` failed |
| S5a | endpoint renders linked-elsewhere like any failure | the HTTP-body test failed |
| S5b | service names the other invoice | the service test failed |
| S6 | token guard removed from `CallerHoldsPrivilegeAsync` | 3/3 no-token cases failed (control still passes) |
| S8 | regarding-type bind dropped | `Confirm_CreatePayload…` failed |

Measurement note: the seed script restores each file with `mv` from a backup, which keeps the BACKUP's older mtime,
so MSBuild's incremental check judged the last seeded binary (S6) up to date. The first post-seed suite runs
therefore failed the three no-token cases against the SEEDED probe; touching the restored sources and rebuilding
cleared them. Any future seed script should `touch` after restoring.

**Suites (2026-10-01, on the committed tree):** BFF unit suite **13176 passed / 56 skipped / 0 failed** (13232);
ArchTests **340/340**. Affected filter (finance + auth + invoice review + new files): 206 passed / 1 skipped.

### 11.8 Fix round 3 (branch `task/uac-r2-130c`, 2026-10-01) — residuals closed under "nothing knowingly deferred"

Live facts used this round (read-only):

| Fact | Live value |
|---|---|
| `sdap-jobs` queue (namespace `spaarke-servicebus-dev`) | `requiresDuplicateDetection = false` (verified by the orchestrator 2026-10-01; immutable on an existing queue) — a same-`MessageId` submission is NOT suppressed (also ADR-004 A1 MUST NOT) |
| `sprk_invoice` resolver columns (`describe tables/sprk_invoice`) | `sprk_regardingrecordid` NVARCHAR(100), `sprk_regardingrecordname` NVARCHAR(100), `sprk_regardingrecordnumber` NVARCHAR(1000), `sprk_regardingrecordurl` URL(250), `sprk_regardingrecordtype` lookup |
| the 4 typed live invoices (Dataverse SQL) | ALL FIVE resolver fields set: type = `e8547bb4-…` (Matter row), id = the matter id (lowercase "D"), number = the matter's `sprk_matternumber` (`REAL-2026-123456.01` / `.02`), url = absolute `https://spaarkedev1…/main.aspx?appid=…&pagetype=entityrecord&etn=sprk_matter&id=<matter>`, name = see next row |
| name vs the matter (audit) | 1 invoice's name equals matter `2444af6d`'s current `sprk_mattername`. The other 3 (matter `b68299c6`, created 2026-07-08) say "Real estate transaction analysis" while the matter is now "Real Estate Transaction Matter": its audit shows `sprk_mattername` = "Real estate transaction analysis" until 2026-07-17 (→ "Smith v Smith" → 2026-08-25 "Real Estate Transaction Matter"). So name = the matter's `sprk_mattername` **at creation** (denormalized, not kept in sync) — consistent with the convention |
| `sprk_recordtype_ref` Matter row | `sprk_regardingrecordnumberfield = sprk_matternumber`, `sprk_regardingfield = sprk_regardingmatter` |

1. **Duplicate extraction job — closed for the race loser.** When the conditional link 412s and the re-read finds the
   document linked to an invoice for the same matter and vendor, the confirm deletes its own invoice and returns
   `InvoiceReviewResult { InvoiceId = <winner's>, JobId = null, StatusUrl = null, ExtractionAlreadyQueued = true }`
   (202, no `Location`) — it submits NO job and writes NO status (the winner owns both). `JobId` / `StatusUrl` became
   nullable; no job id is fabricated. Tests: `Confirm_TwoConcurrentConfirms_SameMatter_LeaveExactlyOneInvoice_AndOneExtractionJob_InEitherOrder`
   (both orders: exactly one submitted job, the winner's; loser `JobId` null, flag true; one link, one status write);
   contract `Confirm_LostTheRaceToAConcurrentConfirm_Is202_WithNoJobId_AndExtractionAlreadyQueued`.
   **Handler defence in depth — NOT added, because it cannot be done cheaply or correctly with state the handler has:**
   (i) `InvoiceExtractionJobHandler.LoadInvoiceRecordAsync` never reads the invoice (placeholder, GitHub #229);
   (ii) `sprk_extractionstatus` has no in-progress value (NotRun / Extracted / Failed) and "Extracted" is never written —
   the handler's success path relies on the OutputOrchestrator mapping, which its own remarks record as unreachable;
   (iii) its "Failed" write goes through `IDocumentDataverseService.UpdateDocumentFieldsAsync(invoiceId, …)`, i.e. it
   updates a `sprk_document` row with the INVOICE's id (non-existent → the exception is swallowed as a warning), so the
   invoice never shows Failed either; (iv) the only de-dupe store, `IIdempotencyService`, is not injected here and,
   per ADR-004 A1, is check-then-set and fails open (#984) — wiring it would claim a guarantee it does not give.
   (iii) is a related defect outside this task's files (recorded below, not fixed).
2. **Spurious 409 `document_changed` — closed.** On a 412 the confirm re-reads the document BEFORE deleting anything;
   if it is still unlinked and its review decision unchanged (an unrelated write, e.g. profiling/indexing status), the
   conditional link is retried on the fresh version, at most `MaxConditionalWriteAttempts` = 3 attempts in total,
   then the invoice is deleted and 409 `document_changed` returned. Tests:
   `Confirm_AnUnrelatedWriteBetweenReadAndLink_IsRetried_AndSucceedsWithExactlyOneInvoice` (2 link attempts, one
   invoice, nothing deleted) and `Confirm_DocumentChangedOnEveryLinkAttempt_Is409AfterTheBound_AndLeavesNoInvoice`
   (exactly 3 attempts, then 409, invoice gone).
3. **Confirm vs reject — defined and implemented.** Outcome: **whichever decision is written first stands; the other
   is refused with a 409 and writes nothing.** Mechanics: (a) reject reads link + version; a linked document is
   refused 409 `sdap.finance.invoice_review.document_linked_to_invoice` (the invoice id is logged, never returned);
   its status write is `If-Match` on the version read (412 → re-read → linked → 409; unrelated write → retried, bound
   3 → 409 `document_changed`). (b) The confirm's link re-check after a 412 compares the review decision with its
   first read: changed (a reject landed) → invoice deleted, 409 `sdap.finance.invoice_review.review_decision_changed`.
   A document that was ALREADY rejected when the confirm read it can still be confirmed (a deliberate re-decision).
   (c) The confirm's step-3 status write is `If-Match` too — on the version read after its own link, after checking the
   document still points at its invoice (resume path: the version of the resume read). Re-pointed meanwhile →
   `StatusNotUpdated` naming the invoice, ConfirmedInvoice NOT written. The endpoint renders reject's refusals via the
   same `ConfirmFailure` (409; `ProducesProblem(409)` added to reject). **Behaviour change:** reject on a confirmed
   (linked) document used to flip it to RejectedNotInvoice and leave the invoice linked; it is now refused. Tests:
   `ConfirmLinksFirst_ThenAConcurrentReject_TheRejectIs409_AndConfirmedStands`,
   `RejectLandsFirst_BetweenAConfirmsReadAndLink_TheConfirmIs409_AndRejectedStands`,
   `Confirm_DocumentRelinkedBetweenLinkAndStatus_TheStatusIsNotWritten_AndTheInvoiceIsNamed`,
   `Reject_DocumentLinkedToAnInvoice_Is409_WithNoWrite_AndNeverNamesTheInvoice`, `Reject_DocumentGone_ThrowsNotFound_WithNoWrite`,
   `Reject_DocumentChangedOnEveryAttempt_Is409AfterTheBound_WithNoWrite`, contract `Reject_Refused_Is409_WithItsReasonCode_AndNoInvoiceId` (×2).
4. **Orphan over-report (residual b) — closed.** A delete that reports failure is followed by a read-back
   (`CancellationToken.None`): row gone → treated as removed (the caller reports its clean failure — `link_failed` /
   `document_changed` / …, or the original create failure — and names no invoice); row present →
   `link_failed_invoice_orphaned` naming it; read-back also failed → same code, message "may not have been removed".
   Applied to both delete sites (link compensation and the lost-create clean-up). Tests:
   `Confirm_LinkFails_AndTheUndoResponseIsLostButTheInvoiceIsGone_ReportsTheCleanFailure`,
   `Confirm_CreateResponseLost_AndTheUndoResponseLostToo_ButTheRowIsGone_ReportsTheOriginalFailure`; the existing
   orphan tests now also assert the row is still there.
5. **Regarding columns — the convention is clear, followed.** `TodoRegardingBuilder` populates type + id + name + url
   together (ADR-024: "never independently"); `IncomingAssociationResolver` adds `sprk_regardingrecordnumber`
   (ADR-024 concise: MUST populate all 5). The confirm now writes, with the type: `sprk_regardingrecordid` = matter id
   (lowercase "D"), `sprk_regardingrecordname` = the matter's `sprk_mattername` (empty when unknown, capped at 100),
   `sprk_regardingrecordnumber` = its `sprk_matternumber` (only when set, capped at 1000), `sprk_regardingrecordurl` =
   `/main.aspx?pagetype=entityrecord&etn=sprk_matter&id=<id>` (the RELATIVE form both server builders write; the 4
   live rows carry an absolute URL with an app id because a client wrote them — the server has no app id). The matter
   is read (one GET) before the create, so a failure saves nothing. Tests: the create-payload test (all five) and
   `Confirm_RegardingNameIsCappedAtTheLiveColumnLength_AndANumberlessMatterOmitsTheNumber`.

**§11 justification (new surface this round):** no new endpoint, DI registration, package or column. Extensions of
existing types only: `InvoiceReviewResult.ExtractionAlreadyQueued` (+ nullable `JobId`/`StatusUrl`) — without it the
race loser must either fabricate a job id or submit a second job; `InvoiceReviewFailure.ReviewDecisionChanged` /
`.DocumentLinkedToInvoice` — without them a concurrent reject is silently overwritten or a confirmed document silently
un-confirmed. Writes reuse round 2's `UpdateRecordFieldsIfUnchangedAsync`.

**Seeded, run, watched fail, restored (2026-10-01)** — each seed applied to the committed-to-be source, the affected
filter run, the file restored from a backup with `cp` and then `touch`ed (round 2's stale-mtime lesson):

| # | Seed | Failed |
|---|---|---|
| S1 | race loser falls through to status + enqueue | both orders of `Confirm_TwoConcurrentConfirms_…OneExtractionJob…` |
| S2 | bound = 1 (no retry) | `…UnrelatedWrite…IsRetried…`, `…ChangedOnEveryLinkAttempt…`, `Reject_…ChangedOnEveryAttempt…`, `ConfirmLinksFirst_…` |
| S2b | bound = 10 (effectively unbounded) | `…ChangedOnEveryLinkAttempt…Is409AfterTheBound…`, `Reject_…ChangedOnEveryAttempt…` |
| S3a | reject does not refuse a linked document | `Reject_DocumentLinkedToAnInvoice…`, `ConfirmLinksFirst_ThenAConcurrentReject…` |
| S3b | reject's status write unconditional | `Reject_WithNotes_…ConditionallyAndUpdateOnly`, `Reject_…ChangedOnEveryAttempt…`, `ConfirmLinksFirst_…` |
| S3c | confirm ignores a changed review decision | `RejectLandsFirst_BetweenAConfirmsReadAndLink…` |
| S3d | confirm's status write unconditional | `Confirm_WritesCreateThenLinkThenStatus…`, `Confirm_DocumentRelinkedBetweenLinkAndStatus…`, `Confirm_StatusUpdateFails_…` |
| S4a | delete failure → always "still exists" (no read-back) | both `…UndoResponse…Lost…Gone…` tests |
| S4b | delete failure → always "removed" | `Confirm_LinkFailsAndTheUndoFails_NamesTheOrphanedInvoice`, `Confirm_CreateResponseLost_AndTheRowCannotBeRemoved…` |
| S5 | regarding name + url dropped | the create-payload test, `Confirm_RegardingNameIsCapped…` |
| S6 | reject endpoint does not render `InvoiceReviewException` | `Reject_Refused_Is409_WithItsReasonCode…` (×2) |

**Remaining residuals (not closable inside this task — each with its reason):**

- **R1 — a confirm that STARTS after another has linked (no 412) still queues a job.** Its first read finds the
  same-matter link and resumes. It cannot tell an in-flight or completed confirm from an earlier attempt that failed
  before queuing (`StatusNotUpdated` / `ExtractionNotQueued`), which a retry MUST re-queue — and the same holds for a
  user repeating a successful confirm. With duplicate detection off, that is a second job run. Closing it needs
  durable per-invoice "extraction queued" state (a schema addition) or an atomic receive-side claim in the job
  pipeline (ADR-004 A1 / #984) — both outside this task. Item 1 closed the 412 race only.
- **R2 — `ExtractionAlreadyQueued` trusts that the concurrent linker was a confirm.** If something else (e.g. an MDA
  user setting the lookup by hand) links the document to a same-matter/vendor invoice in the same window, nothing
  queues extraction and the status is not set. Exotic; recorded.
- **R3 — re-pointed between link and status** (item 3c): the error names the invoice, which is then no longer linked
  to the document — manual clean-up, as the message says.
- **Related defect (not fixed, outside the task's files):** `InvoiceExtractionJobHandler.UpdateInvoiceExtractionStatusAsync`
  writes the invoice's extraction status to `sprk_document` with the invoice id (item 1 (iii)).
- Round 2 residuals (c) and (d) stand as recorded in §11.7.

**Suites (2026-10-01, on the committed tree):** BFF unit suite **13190 passed / 56 skipped / 0 failed** (13246);
ArchTests **340/340**. Affected filter (`FinanceInvoiceReview` + `FinanceEndpointsAuthorizationContractTests`): 110/110.

### 11.9 Round 3 verifier gap closed in the main session (2026-10-01) + tracking

**The gap the round-3 verifier found (it had not been disclosed).** The document is already `RejectedNotInvoice`. A
confirm reads it (a deliberate re-decision, which is allowed). A SECOND reject then lands between that read and the link.
The status does not change, so the confirm's 412 re-check, which compared `sprk_invoicereviewstatus` only, took
the reject for an unrelated write. It then retried the link and later wrote `ConfirmedInvoice`: the second reject's caller
was told "Rejected", but the document ended up Confirmed. The verifier's throwaway probe test confirmed this.

**Fix.** The re-check now compares the decision's full identity, `ReviewDecision(status, sprk_invoicereviewedon,
sprk_invoicereviewnotes)`. Every confirm and every reject writes all three through `BuildReviewStatusFields`, so any
decision written in between changes the reviewed-on stamp even when the status is the same. The stamp is read
through `TryReadUtc`, which accepts both the Web API's ISO string and the ServiceClient's `DateTime`. Notes are a second
discriminator, for two rejects in the same second (Dataverse stores date-times to the second).
- Test: `AlreadyRejected_ASecondRejectLandsBetweenAConfirmsReadAndLink_TheConfirmIs409_AndTheRejectStands`. The fake now
  stores `sprk_invoicereviewedon` as the Web API does (an ISO string, whole seconds) and stores the notes.
- Seed: reverting the comparison to status-only fails exactly this test (1 of 42 in the filter); restored.
- An unrelated write (`TouchDocument`) still retries, because it changes neither the stamp nor the notes; the existing bound
  tests still pass.

**Tracking (nothing hidden):**
- **R1, a second extraction job from a confirm that starts after a link** (or from a repeated confirm). It is linked on
  **#984**: every such job carries `IdempotencyKey = invoice-extraction-{invoiceId}`, so the atomic receive-side claim
  drops it. Live check: `sdap-jobs` has `requiresDuplicateDetection = false`, which cannot be changed on an existing queue.
  A second run has no effect until #229 makes the handler's output reachable.
- **The handler writes extraction status to `sprk_document` with the invoice id**: filed as **#1087**.
