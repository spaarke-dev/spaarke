# 095g: Assign a firm from the Outlook pane (EB-D-224)

Status: design drafted 2026-11-13 (section "Design for uac-r2 review" below), sent to uac-r2; no code. Order: after 095e and 095f. A design note goes to uac-r2 before any code; uac-r2 reviews before merge; two adversarial passes.

## What

- An internal-only list of active firms, searchable.
- An assign route on matter, project and work assignment (e.g. `POST /api/finance/core-records/{entityType}/{entityId}/firms {firmId}`). It writes Assigned Law Firm 1/2. The answer is the record's firm list, as the §8 GET returns it, plus closed refusal codes: no right to change the record; both slots full; inactive firm; record unavailable (503).
- The slots are what the external access model reads, so a save gives that firm's external contacts access to the record. This makes it a sharing change.

## uac-r2 conditions (binding, 2026-11-13)

1. **G5:** check the caller as the user, then write as the app. The caller needs Write on the record, which is the C4 share right. Reuse `CallerRecordAccessProbe`; build no new check.
2. **One eligibility rule:** use `InternalShareEndpoints.ClassifyEligibility` / `IsBarredOnRestricted`, the rule every share writer uses.
   - A Restricted record refuses.
   - A Secure record refuses with a clear message, because the save would grant nothing.
   - A record filed under a secure or Restricted parent refuses, decided through the parent-derived value.
3. **NFR-01:**
   - An unknown record or firm id gets the same answer as a denied one.
   - Any read fault fails closed: 503, never a silent write.
   - The firm must be active, checked on the server. Never trust the pane's list.
4. **Write path:** one server-side writer (ADR-002), with the person recorded as on our other writes. Replacing or clearing a firm is a revoke: the old firm's external users lose access. The contract says so, and the pane confirms it.
5. **Route ledger:** declare a record check in `RouteAuthorizationGuardTests`. The tests need:
   - negative cases for Restricted, Secure, a secure parent, no Write, an external caller and an inactive firm;
   - a must-fire control.
6. **List route:** internal callers may see all active firms. Firm names are not access-sensitive, and the decision that matters is made when the firm is assigned.
   - It is internal-only. Every external or non-licensed caller gets the same answer as an unknown caller.
   - Add a stated waiver line in the route ledger, with that reason.

## Grant and revoke: one rule

| Action | What it does | Right needed |
|---|---|---|
| Add a firm to an empty slot | Grant | Write on the record, checked as the user |
| Replace a firm | Grant, plus a revoke of the old firm | Write on the record, checked as the user; the pane confirms the revoke |
| Clear a slot | Pure revoke | Write on the record, checked as the user |

(uac-r2, 2026-11-13)

- 2026-11-13 (from 045f review K2): a work assignment is a non-family pair table; field-mapping 045f refuses a generic-path write whose sprk_regardingrecordurl names another record (`regarding_record_url_mismatch`). If 095g writes the firm slots through a generic path, send no URL or the canonical one.

---

# Design for uac-r2 review (2026-11-13)

All paths are under `src/server/api/Sprk.Bff.Api/` unless stated. No code yet.

## 0. Grounding

- **Slots.** `sprk_assignedlawfirm1` and `sprk_assignedlawfirm2` (lookup → `sprk_organization`) on `sprk_matter`, `sprk_project` and `sprk_workassignment`. They are:
  - in the access-conferring registry (`Services/Ai/Membership/MembershipOptions.cs:311-312`);
  - in the deny-veto org registry (`Infrastructure/ExternalAccess/ExternalParticipationService.cs:1159-1165`);
  - the eBilling linked firm (`Services/Finance/Submission/InvoiceSubmissionService.cs:2024` `CheckFirmLinkAsync`);
  - read by the §8 GET (`Api/Finance/InvoiceSubmissionFromEmailHttp.cs:503-547`);
  - read as slot 1 only by Signals, as the outside firm (`Services/Signals/SignalCoreRecordAccess.cs:606`, `Services/Signals/Actions/DecisionActionExecutors.cs:1094-1106`).
- **No dedicated writer.** Today the slots are written only by the model-driven form and by the generic paths that call the L1 hook afterwards (`DataverseUpdateRecordHandler.cs:451`, `UpdateRecordActionCore.cs:432`, `FieldMappingEndpoints.cs:1666`, `RecordCreationService.cs:2235`).
- **Slot to access.** `AssignedAccessMaterializer` is the one owner of the Assigned-To auto grants (`Services/ExternalAccess/AssignedAccessMaterializer.cs:131-193`). It has three triggers:
  - L1 inline `AfterWriteAsync` (:453-486) with `RevokeOnChange`;
  - Sync `POST /api/v1/external-access/assigned-access/sync` (`Api/ExternalAccess/AssignedAccessSyncEndpoint.cs:65-67`);
  - L4 `AssignedAccessReconciliationJob` every 5 min.
- **Organization grant skip** (:1143-1168). The grant is skipped for:
  - an inactive record;
  - an effectively Restricted record;
  - a Secure or Limited record (`IsDirectOnly`);
  - an inactive organization;
  - a firm on the record's No Access list.

  On change or clear, the old subject's unmodified auto access is removed; adopted and declined access is kept (:174-176). `GrantorOid` becomes `sprk_grantedby`.
- **Effective flags.** `ExternalParticipationService.GetEffectiveRootRecordFlagsAsync` (:1025) folds the record's own flags with everything it is filed under (`EffectiveRootFlags.cs:42-64`). An unreadable value gives `RootRecordFlags.Unreadable`.
- **`ClassifyEligibility` is per-person** (`Api/ExternalAccess/InternalShareEndpoints.cs:1242-1255`). Only its Restricted term, `IsBarredOnRestricted` (:1265-1266), applies to an organization.
- **Precedents.**
  - Set firm: Finance filters as the caller (`Api/Finance/InvoiceApprovalEndpoints.cs:74-80`), a per-act operation key (`OperationAccessPolicy.cs:226-231`), and an optimistic-concurrency write (`InvoiceAssignFirmService.cs:1102-1111`).
  - `IGenericEntityService.UpdateIfRowVersionMatchesAsync` throws `DBConcurrencyException` on a mismatch.
- **Firm "active"** means `statecode = 0` (`InvoiceSubmissionService.cs:1997-2013`). No server code reads a law-firm marker; the schema has `sprk_organization.sprk_organizationtype` → `sprk_organizationtype_ref`.
- **The pane** reads only the §8 GET today (`src/client/office-addins/shared/taskpane/services/invoiceSubmissionService.ts:587-627`).

## 1. Routes

The routes go in the new file `Api/Finance/CoreRecordFirmEndpoints.cs`. The group is `/api/finance` with `RequireAuthorization()`, `RequireInternalFinanceCaller()` (step 0) and `RequireEbillingEnabled()`.

**A. `GET /api/finance/firms?search=&page=1&pageSize=25`**
- Filter: `AddFinanceAuthorizationFilter(<caller Read on sprk_organization>, UniformNotFoundFailClosed)`, plus `RequireRateLimiting("dataverse-query")`.
- The read is app-only and returns active firms (plus the law-firm marker, Q3).
- `search`:
  - is a contains match on `sprk_organizationname`;
  - has its wildcards escaped;
  - is at most 100 characters.

  `pageSize` is 1-50.
- 200 `{ items: [{ firmId, name }], page, hasMore }`, sorted by name (case-insensitive), then by id.
- 404 uniform `sdap.access.deny.record_unavailable` for any of these, with the same bytes:
  - a flagged-external caller;
  - no systemuser row;
  - no table Read.
- 503 `ebilling.submission.record_unavailable` on any fault, never an empty list.

**B. `POST /api/finance/core-records/{entityType}/{entityId}/firms`**
- `entityType` is `Matter`, `Project` or `WorkAssignment`.
- Body: `{ add: guid|null, remove: guid|null, confirmRevoke: guid|null }`.
- `add` alone is a grant: the first empty slot.
- `add` + `remove` is a replace: `add` takes the slot that holds `remove`.
- `remove` alone is a clear.
- Neither, or `add == remove`, is 400.

It is one route because a replace must be one conditional update of one row. Delete-then-post would leave a moment with an empty or full slot and run two access passes. Ids are firm ids, never slot numbers.

## 2. Assign check sequence (in order; nothing is written before step 8)

| # | Check | Refusal |
|---|---|---|
| 0 | Filter, in order: step 0 (flagged external or no systemuser) → body shape → Write on the record as the caller. The Write check uses the new key `ebilling.record.assign_firm` (Record path, RetrievePrincipalAccess over OBO, via `AuthorizationService.GetCallerRecordAccessAsync`). An unknown record answers refused, so unknown = denied. An unlicensed user gets no Write | 400 ValidationProblem; 404 uniform (step 0, unknown record, no Write); 503 on a fault (needs §3e) |
| 1 | Record read, app-only: both slots, `versionnumber`, `statecode` | Gone: 404 uniform. Fault: 503 |
| 2 | Eligibility, only when `add` is set. Effective flags: `IsUnreadable` → 503; `IsBarredOnRestricted(true, eff.IsRestricted)` → Restricted; `IsSecure` → Secure; `IsLimited` → Limited (Q1); inactive record (Q2). Parents come through the fold. A pure clear skips this step: a revoke is never blocked by flags | 409 `ebilling.firm_assign.record_restricted` / `record_secure` / `record_limited` / `record_inactive` |
| 3 | When `add` is set: the caller needs Read and AppendTo on the firm (`FinanceAuthorizationFilter.EvaluateCheckForCallerAsync`) (Q4) | Denied or unknown: 404 uniform (same bytes). Faulted: 503 |
| 4 | Firm row, app-only: active (and a law firm, Q3) | 422 `ebilling.firm_assign.firm_inactive` (`not_a_law_firm`). Fault: 503 |
| 5 | No Access: `IAccessibleRecordSetService.CheckGranteeNoAccessAsync` (Q7) | 409 `ebilling.firm_assign.firm_no_access`. Unverifiable: 503 |
| 6 | Slot plan: pure `FirmSlotPlan.Decide(slots, add, remove)` | See below |
| 7 | Revoke confirmation: `remove` is in a slot and `confirmRevoke != remove` | 409 `ebilling.firm_assign.revoke_confirmation_required` + `revokes`, `assigned` |
| 8 | Write, then access (§3) | |

Slot rules:
- **Add.**
  - A firm already present is a no-op.
  - With no empty slot: 409 `slots_full` + `assigned`.
  - Fills slot 1, then slot 2.
- **Replace** onto the firm in the other slot: 409 `firm_already_assigned`.
- **`remove` not in any slot.**
  - With no `add`, or with an `add` already present: a no-op (a retry after success).
  - Otherwise: 409 `record_changed` + `assigned`.
- **Clear** never moves the other firm up (Q8).

## 3. The write: one server-side writer

- **a. Writer.** `Services/Finance/CoreRecordFirmAssignmentService.cs`, a concrete class registered in the Finance module.
  - It writes only `sprk_assignedlawfirmN`, app-only, through `UpdateIfRowVersionMatchesAsync` with step 1's `versionnumber`.
  - It sends no `sprk_regardingrecordurl` and uses no field-mapping generic path, so 045f's `regarding_record_url_mismatch` cannot arise.
- **b. Person.**
  - The caller's oid is the `grantorOid`, so each grant the pass writes carries `sprk_grantedby`.
  - A structured audit log line records the caller, record, slot and old → new firm.
  - No new column (Q9).
- **c. Concurrency.**
  - On `DBConcurrencyException`: re-read, re-plan steps 6-7 once, and write again only if the plan is identical. Otherwise answer from the new state.
  - A second mismatch is 409 `record_changed`.
  - Two concurrent adds: the first wins; the second gets `slots_full` or a no-op.
  - Any other write fault is 503. A repeat is safe.
- **d. Idempotency and access.**
  - The plan comes from the stored state, so a repeat is a no-op.
  - After every successful plan, no-ops included, the writer calls `AssignedAccessMaterializer.AfterWriteAsync(entity, id, [both slots], callerOid, ct)` (L1, `RevokeOnChange`). The pass is idempotent.
  - `Complete` gives `access.state = "applied"`. Incomplete or null gives `"pending"`: the slot write stands, and a repeat of the same request finishes the pass.
- **e. Extension, not a new check.** `FinanceDenial.UniformNotFoundFailClosed` (`FinanceAuthorizationFilter.cs:178-201`) also treats a Faulted Record snapshot as 503, using the file's `FaultObservingAccessDataSource` (:654-673). Its only current user, the 095a route, declares Privilege checks only, so it is unchanged; a regression test pins that.
- **f. One rule.** The materializer's organization-grant skip is extracted into `internal static string? OrganizationGrantSkipReason(RootRecordFlags)`. The materializer and step 2 both call it, so the route's refusal and the materializer's skip cannot drift.

## 4. Revoke semantics

- Replace and clear remove the access this assignment gave: the old firm's unmodified automatic grant.
- They keep:
  - operator-adopted or changed grants;
  - contact-level grants;
  - POA shares.
- `access.revoked` reports what was removed.
- The server requires `confirmRevoke == remove`. Otherwise it answers 409 `revoke_confirmation_required` naming the firm, and writes nothing. Because the confirmation names a firm, a stale confirmation cannot revoke a different firm.

## 5. Component justification (§11) and placement (§10)

| New surface | Existing | Why not extend | Cost of doing nothing |
|---|---|---|---|
| `GET /api/finance/firms` | `/office/search/entities` (`Api/Office/OfficeEndpoints.cs:1069`); §8 GET; `/external/me` firms | Office search returns association targets read as the caller (no org type, no step 0; word-add-in-owned). The §8 GET lists linked firms only. `/me` lists an external caller's own firms | The pane cannot offer an unlinked firm; EB-D-224 cannot be built |
| `POST .../firms` + `CoreRecordFirmAssignmentService` | Form, `/assigned-access/sync`, generic update tools | Sync takes no input. Generic paths have no slot rules, eligibility refusal or revoke confirmation | Condition 4 (one server-side writer) is unmet; the pane has no write |
| Key `ebilling.record.assign_firm` | `ebilling.invoice.assign_firm` | That key names the invoice act (precedent `OperationAccessPolicy.cs:230`) | Deny logs and the ledger name the wrong act |
| §3e extension | The same mode | It is the extension | A rights fault would be 404, not the 503 condition 3 requires |
| `OrganizationGrantSkipReason` | Materializer inline code | It is an extraction | Two copies of one rule |

- Placement is the BFF: a pane request with a latency budget, running the BFF-owned materializer in-request.
- There is no package, background work or new Dataverse column.
- Expected publish delta is under +0.1 MB, measured against a fresh master build.

## 6. Route ledger and tests

**Ledger.**
- `GovernedFile("Api/Finance/CoreRecordFirmEndpoints.cs", RouteLevelGate, ...)`; the census pin moves +1.
- Both routes pass Rule A by credit (`AddFinanceAuthorizationFilter`).
- The list route's reason sits in the `GovernedFile` text: internal only, firm names not access-sensitive, decision at assign. It cannot also carry a waiver (Q6).
- `FinanceStepZeroCoverageGuardTests` covers both routes.

**Contract tests** (`tests/integration/contract/Api/Finance/CoreRecordFirmEndpointContractTests.cs`, modelled on `InvoiceAssignFirmEndpointContractTests.cs`). Every negative asserts the status, the code, zero writes and zero materializer calls.

Negatives:
- **Uniform 404, identical bytes:**
  - N1 flagged-external caller;
  - N2 no systemuser;
  - N3 unknown record;
  - N4 no Write (Read only).
- **Record flags:**
  - N5 Restricted;
  - N6 Secure;
  - N7 work assignment under a secure matter, with its own flag clear;
  - N8 project under a Restricted matter;
  - N9 Limited.
- **Firm:**
  - N10 inactive firm;
  - N11 unknown firm and N12 firm without the caller's AppendTo, both with the same bytes as N3;
  - N13 firm on the No Access list.
- **Slots:**
  - N14 slots full;
  - N15 replace without a confirmation;
  - N16 confirmation naming another firm;
  - N17 replace onto the other slot's firm.
- **Input:** N18 bad body shapes or `entityType`.

Faults, each 503 with zero writes:
- step 0;
- record rights;
- row read;
- own flags;
- ancestry walk;
- firm rights;
- firm row;
- No Access Unverifiable;
- write throw.

Positives:
- add to slot 1, then slot 2;
- a repeat add (`changed: false`, no write, L1 still runs);
- a replace keeps its slot;
- a clear on a Secure record is allowed;
- a repeat clear is a no-op;
- an incomplete inline pass gives `pending`;
- concurrency: an identical re-plan writes, a different re-plan gives 409.

Must-fire controls:
- N4 succeeds once Write is held;
- N5-N9 succeed on a Standard record;
- N1 is refused while holding every right, with zero probe calls.

Seeded proofs:
- a theory over all 32 `RootRecordFlags` combinations (step 2 equals the materializer skip);
- a seeded chain without the filter fails Rule A;
- `UniformNotFoundFailClosed` unit tests (Record fault 503, unknown record 404, 095a unchanged);
- materializer integration: after an add, the org grant exists with `sprk_grantedby`; after a replace, the old unmodified grant is gone and an adopted one is kept.

List route:
- N1, N2 and no-table-Read give the same 404;
- a fault gives 503, never `[]`;
- inactive firms are absent;
- paging works;
- search escapes `%`, `_` and `[`.

## 7. Contract for word-add-in-r1

The client reads `errorCode ?? reasonCode`. Every refusal writes nothing.

**200 response.**
```json
{ "changed": true, "firms": [{ "firmId": "guid", "name": "string" }],
  "assigned": [{ "slot": 1, "firmId": "guid", "name": "string", "active": true }],
  "access": { "state": "applied|pending", "granted": [{ "firmId": "guid", "name": "string" }], "revoked": [{ "firmId": "guid", "name": "string" }] } }
```
`firms` is the §8 GET body after the write. It is `null` if that read faulted after a successful write; the pane then reloads it.

**Refusals.**

| Status, code | Text |
|---|---|
| 404 `sdap.access.deny.record_unavailable` | "This record or firm isn't available to you, or you can't change this record." |
| 409 `record_restricted` | "This record is Restricted to internal users, so no firm can be given access to it." |
| 409 `record_secure` / `record_limited` | "This record is secure, so assigning a firm would give it no access. Use Manage Access to add named people." (limited: same) |
| 409 `record_inactive` | "This record is inactive." |
| 422 `firm_inactive` | "This firm is inactive. Choose another firm." |
| 409 `firm_no_access` | "This firm can't be given access to this record." |
| 409 `slots_full` | "Two firms are already assigned. Replace one of them." (+ `assigned`) |
| 409 `firm_already_assigned` | "That firm is already assigned to this record." |
| 409 `record_changed` | "The firms on this record changed. Check them and try again." (+ `assigned`) |
| 409 `revoke_confirmation_required` | + `revokes`. The pane asks: "Removing <firm> takes away the access this assignment gave its people. Continue?" and resends with `confirmRevoke` |
| 503 `ebilling.submission.record_unavailable` | "Spaarke couldn't do this right now. Try again." |
| 503 `ebilling.disabled` | eBilling is off |

- All 409 and 422 codes are prefixed `ebilling.firm_assign.`.
- For `pending` the pane shows "Saved. Spaarke is still updating who can see this record." with Retry, which resends the same request.

## 8. Open questions

| # | Question | For | Recommendation |
|---|---|---|---|
| Q1 | A Limited record grants an organization nothing (`IsDirectOnly`). Refuse, as for Secure? | uac-r2 | Refuse |
| Q2 | Inactive record: refuse? | Owner | Refuse |
| Q3 | What marks a firm as a law firm? No server rule exists | Owner | Name the `sprk_organizationtype_ref` row by a stable key; until then an active organization counts, as at intake |
| Q4 | Require Read + AppendTo on the firm, as the caller? | uac-r2 | Yes: the platform's own rule for setting a lookup, the Set firm precedent, and unknown = denied for firms too |
| Q5 | Condition 1 names `CallerRecordAccessProbe`. Does the Finance Record path (the same RetrievePrincipalAccess as the caller, via `AuthorizationService`) satisfy it? | uac-r2 | The Finance path: step 0, Rule A credit, uniform 404, the §8 / Set firm precedent |
| Q6 | List route: credit it (step 0 + table Read on `sprk_organization`) instead of a waiver? | uac-r2 | Credit: no waiver basis fits |
| Q7 | Refuse a firm on the record's No Access list? | uac-r2 | Refuse: the save would grant nothing yet still link the firm for invoices |
| Q8 | Fill slot 1 then 2; never move the other firm up on a clear? | Owner | Yes: slot 1 is the outside firm in Signals |
| Q9 | Record-level person column for this update? | Owner | None: `sprk_grantedby` on grants + the audit log |
| Q10 | Both routes behind the eBilling kill switch? | Owner | Yes |

## 9. ADR conflicts

None found.
- ADR-002: no plugin; one L1 server writer, with L4 as the safety net.
- ADR-003 / ADR-028: checks as the caller over OBO; faults fail closed; no new credential.
- ADR-008: the filters are credited.
- ADR-010: a concrete class.
- ADR-019: ProblemDetails with codes.

Q5 and the per-person scope of `ClassifyEligibility` are clarifications of uac-r2's conditions, not ADR conflicts.

## uac-r2 review of the design (2026-11-13): APPROVED to build, with R1 and R2

- **R1 (required).** Setting a lookup needs Append on the row that holds it. When `add` is set (grant or replace), the caller must also hold Append on the record, read from the same caller snapshot as Write. A pure clear needs Write only. Tests: "Write without Append → 404 uniform", plus its must-fire control.
- **R2 (pane text).** The pane states the access effect on every add, not only on revoke: "<firm>'s people will get access to this record." It is a notice only, not a second round trip.
- **Q1:** refuse a Limited record, as Secure.
- **Q4:** require Read and AppendTo on the firm, as the caller.
- **Q5:** the Finance Record path satisfies condition 1, provided the `userId` it receives is the token's own oid from the claims, never taken from the body or the route. The §3e extension (Faulted → 503) is accepted.
- **Q6:** credit the list route, keeping the reason in the `GovernedFile` text.
- **Q7:** refuse a firm on the No Access list.
- **ClassifyEligibility:** for an organization only `IsBarredOnRestricted` applies; Secure and Limited come from `OrganizationGrantSkipReason`. The 32-combination theory meets condition 2. A clear skips step 2.
- **Slot 2:** leave it as is. The Insights label is tracked in #2816.
- **Pending revoke:** a known, decided limit (uac-r2 D-10). `access.state = "pending"` plus Retry stays the contract.
- **Gate:** two adversarial passes before merge; uac-r2 reviews the PR.
- **Not affected by #2799:** the slot fields are outside its locked set.
- **Owner questions still open:** Q2, Q3, Q8, Q9, Q10.
