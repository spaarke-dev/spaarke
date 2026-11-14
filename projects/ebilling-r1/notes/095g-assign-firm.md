# 095g: Assign a firm from the Outlook pane (EB-D-224)

Status: built 2026-11-14 on `task/ebill-095g-assign-firm` (section "Build" at the end; its contract supersedes section 7). Design drafted 2026-11-13 and approved by uac-r2 with R1/R2 and the Q2/Q3 conditions. Order: after 095e and 095f. A design note goes to uac-r2 before any code; uac-r2 reviews before merge; two adversarial passes.

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

## Owner answers (2026-11-14)

- **Q2 → allow (EB-D-233).** The design's recommendation was refuse; the owner chose allow ("it may be required"). Step 2 drops the inactive-record check and the `record_inactive` code. The other step 2 refusals stay.
- **Q3 → the existing organization type (EB-D-234).** Only an active organization whose `sprk_organizationtype` is the "Law Firm" row is listed or accepted on add; anything else gets 422 `not_a_law_firm`. The row is matched by `sprk_organizationtypecode`, which is empty on dev today, so 095g's deploy seeds it.
  - **Dev data, read 2026-11-14.** There is one type row, "Law Firm". Of the active organizations, 3 have that type and 3 have no type. Two of the untyped ones are assigned firms today: one appears on 6 matters, and one is the customer's own organization, as firm 2 on 2 matters. Neither would be offered for a new add until its type is set. Existing assignments are not affected, because a clear does not check the type. Setting a type is a live data write and needs the owner's OK.
- **Q8, Q9, Q10 → as recommended (EB-D-235).**
- **No owner questions remain open.** Build per the design with uac-r2's R1 and R2. uac-r2 was told that Q2 changed.

## uac-r2 on the owner answers (2026-11-14): approval stands, plus conditions for Q2 and Q3

R1, R2 and the Limited / Secure / Restricted / No Access refusals stand, whatever the record's state.

**Q2 (inactive record) conditions:**
1. **No promise of access.** The materializer keeps skipping the organization grant on an inactive record; 095g must not change that.
   - An add to an inactive record writes the slot and grants nothing. The L4 job grants within about 5 minutes once the record is active again.
   - The 200 is `access.state = "applied"` with `granted: []`.
   - The R2 notice reads "<firm>'s people will get access to this record when it is active again." It never says they have access now.
2. **Keep the one-rule theory exact.** Step 2's refusals come from `OrganizationGrantSkipReason(RootRecordFlags)`, the flag-based reasons only. Record state stays out of that function, or is a separate argument the route ignores. The 32-combination theory still proves that the route and the materializer agree on every flag reason.
3. **Tests:**
   - Inactive Standard record: the slot is written, no organization grant, state `applied` with an empty `granted`.
   - Inactive Secure or Restricted record: refused, the same as active.
   - Reactivation then grants (a materializer-level test is enough).

**Q3 conditions:**
- Match the Law Firm type by its stable code, never by id or name.
- `not_a_law_firm` is checked server-side, on add only. The active-organization check stays alongside it.
- A clear removes any firm.
- **If the type code is missing on an environment, the add and the list fail closed with 503**, never "every organization counts".

uac-r2: "Go ahead and build."

**Test-file note (ontology-r1, #2822 review):** `BillingCodePatternTests` reads `src/solutions/RuleReview/src/services/ruleTypes.ts`. That file is tracked; the review's 1/2990 failure came from a partial export. Not a defect.

---

# Build (2026-11-14)

Branch `task/ebill-095g-assign-firm`. Built per the design, uac-r2 R1/R2 and the Q2/Q3 conditions. Deviations from the design text:

- **Two operation keys, not one.** `ebilling.record.assign_firm` = Read, Write and Append (an add or a replace, R1); `ebilling.record.remove_firm` = Read and Write (a pure clear). Read was added in the uac-r2 review (see below). The route declares ONE Record check, so the rights come from one caller snapshot.
- **No `record_inactive`** (EB-D-233). An inactive Standard record is accepted; the 200 carries `recordActive: false` and the when-active notice.
- **`notice` and `recordActive`** are added to the 200 body (R2, Q2).
- **The type-code "shared constant"** is `LawFirmOrganizationType.Code` in C#. The seeding script states the same literal, and a test pins the two equal (a PowerShell script cannot import the C# constant).
- **A replace whose old firm sits in both slots** (possible through other writers) puts the new firm in the first slot and clears the other, so the replaced firm leaves the record.
- **The Law Firm type row** must be active, and the code must be on exactly one active row. Zero or two rows is the fail-closed 503 on both routes.
- **A filter-level 503** (a step-0, rights or privilege fault) is the filter's existing fail-closed answer. Its detail text names invoice submissions; the code is the same `ebilling.submission.record_unavailable`, which is what the pane reads.

## Review 1 fixes (2026-11-14)

- **F1, re-check on retry.** On a row-version mismatch, an add now runs the step-2 eligibility check again before the second write. A record made Restricted (or Secure or Limited) between the check and the write is refused with its 409 code, and nothing is written. A clear is not re-checked, because a revoke is never blocked by flags. Tests: `Mismatch_RecordBecameRestricted_Is409` (must fire) and `Mismatch_RecordBecameRestricted_ClearIsApplied` (must not fire).
- **K-class limit (known, accepted).** The retry runs only when this record's own version moves. A change to a PARENT's flags (a work assignment's or project's matter) does not move this row's version, so it does not trigger the re-check in that window. The add can then land on a record whose effective flags have just turned against it. No access follows from it: the inline access pass reads the effective flags itself and the materializer refuses the organization grant there. What remains is a slot value the route would have refused a moment later. It is visible on the record and is cleared like any other.
- **F2, one record-rights helper.** `FinanceAuthorizationFilter.CallerRecordVerdictAsync` returns allowed, denied or faulted from the one caller snapshot. The filter's Record check (every mode) and `CallerHoldsRightsAsync` (the handlers' second-record checks) both use it. The fail-closed mode answers faulted with the 503; the other modes deny on it, as before. The stale remark on `EvaluateCheckForCallerAsync` is rewritten. **Corrected in review 2:** the first version also made an incomplete answer never count as held, on the premise that the production source returns no rights on a fault. That premise was false. When the full rights question goes unanswered, the source falls back to a read probe and returns a degraded Read, marked faulted, for a readable record. So that version refused Read-only Finance routes and handler checks during any such outage. The rule now decides by the rights first (see Review 2 fixes).

## Review 2 fixes (2026-11-14)

- **F1, held wins.** `CallerRecordVerdictAsync` now works in this order:
  - Allowed when the snapshot's rights satisfy the operation, whatever `Faulted` says.
  - Otherwise Faulted when the snapshot is faulted.
  - Otherwise Denied.
- **Effect by mode.** Outside the fail-closed mode the filter and `CallerHoldsRightsAsync` behave exactly as on the base commit: allowed exactly when the rights are held, and refused with the mode's existing refusal otherwise. `EvaluateCheckForCallerAsync` also behaves as on base. In the fail-closed mode, an answer that is faulted and not held is the 503 (design 3e). The 095g POST still answers 503 on a fault, because a degraded Read never satisfies Write and Append.
- **Tests.**
  - `RecordRightsHelper_HeldWins_ThenFaulted_ThenDenied` pins the rule.
  - `Summary_DegradedReadOnAFault_Gets200` and `Recalculate_FaultedAnswer_DecidedByTheRightsItCarries` show that a Read-only route (403 mode and uniform-404 mode) is allowed on a degraded Read, with no-rights controls.
  - `Add_GivenADegradedReadOnAFault_Is503` covers the POST.
  - `List_IsNotDecidedByARecordFault`: the list asks table privilege only. Its privilege fault is the 503 of `List_PrivilegeFault_Is503`.
- **F2, test double.** The shared Finance test double's `Fault()` now returns the production shapes: Read when the record is readable (Read was granted), otherwise None, and never any other granted right. Before, it returned every granted right with `Faulted`, a shape production never emits. The existing 095g fault tests pass unchanged on the production shape.
- **F2, one list order.** `GET /api/finance/firms` keeps the server's order (name, then id) and no longer re-sorts each page in memory, since another collation could reorder a page against its neighbours. Test: `List_KeepsTheServersOrder`, with a culture-aware server order that an ordinal sort would change. `List_ActiveLawFirms_Sorted_Paged` did not pin the re-sort and is unchanged. The fake now takes the server's collation as a setting.
- **F4, R1 for a replace.** `{add, remove, confirmRevoke}` with Write only is the uniform 404 (the add needs Append), with a Write and Append control. Tests: `Replace_WithWriteOnly_IsUniform404` and `Replace_WithWriteAndAppend_IsApplied`.
- **F4, old firm in both slots.**
  - A replace on (A, A) gives (C, null).
  - A clear on (A, A) gives (null, null).
  - Each revokes A once.
  - Tests: `Replace_FirmInBothSlots_LeavesNewAndEmpty`, `Clear_FirmInBothSlots_EmptiesBoth`.
- **F1, seeding script.** The type code's alternate key spans every type row, inactive ones included. When no active row carries the code, step 1 now also looks for INACTIVE rows that carry it. If one does, the script stops with an escalation (exit 2, nothing written), naming the row: an owner must reactivate it or clear its code. The generic error text no longer suggests a re-run fixes a failure. Test: `SeedingScript_EscalatesOnAnInactiveRowHoldingTheCode` (a source check, because the script is not run).
- **Theory strengthening.** In the 32-combination theory, when the route admits an add to an inactive record, the same firm on the same flags with the record ACTIVE must now be granted. Before this, the inactive skip could hide a flag refusal the route had wrongly admitted.

## uac-r2 review fixes and known limits (2026-11-14)

uac-r2 reviewed 095g at `f1a53f306a`: the access gate holds, and passes with these fixes.

- **Read on the record.** An add or replace now needs Read, Write and Append on the record; a clear needs Read and Write.
  - Why: every answer describes the record. A refusal lists its firms by name, and 409 versus 200 on a clear tells whether a named firm is on it. So a caller who cannot read the record must learn nothing from it.
  - Without Read the answer is the uniform 404, byte-identical to an unknown record, and nothing is written.
  - Tests: `WithoutRead_IsUniform404_LikeAnUnknownRecord` (add, replace, a clear of a firm on the record and of one not on it). The controls are updated to Read and Write (and Append).
- **Page bound on the list.** `page` above 200 is 400, decided after step 0 like the other query 400s, with no privilege call and no Dataverse request. 200 pages of 50 is far beyond any firm list. Tests: `List_PageAboveTheBound_Is400` (201 refused, 200 accepted) and `List_PageAboveTheBound_AfterStepZero`.
- **Assertions.**
  - `Clear_WithoutWrite_IsUniform404` asserts the reason code.
  - `SecondMismatch_Is409` asserts that no access pass ran.
  - `List_PrivilegeFault_Is503` asserts the error code.
  - `FlagRefusals` gains a replace on a Secure record: 409 `record_secure`, the old firm stays, nothing is written or revoked.
- **K3 (known limit): two 503 texts.** A 503 from the route and a 503 from the filter carry the same `code` with different `detail` text. The pane keys on the code, never on `detail` (stated in the contract below).
- **K4 (known limit): grantor attribution.** The inline access pass reconciles the whole record, not only the firm the caller named. If the record holds a firm the caller did not name in this request (for example one left in a slot by an edit on the form), a grant the pass creates for it records this caller as grantor. Only the attribution is off: the access itself is what the record's slots call for, and the background access pass reaches the same state.
- **TOCTOU (known limit).** On the one retry after a version mismatch, the route re-checks the record's own flags but does not re-read a parent's flags, the firm's state or the record's No Access list. Nothing leaks: the access pass applies all three itself and does not grant against them.

## Component justification (CLAUDE.md §11), per new surface

| New surface | (1) Existing | (2) Why not extend | (3) Cost of doing nothing |
|---|---|---|---|
| `GET /api/finance/firms` | Office entity search; the §8 GET (linked firms only); the external `/me` firms | Office search reads as the caller, with no organization type and no step 0. The §8 GET lists only firms already linked. `/me` lists the external caller's own firms | The pane cannot offer a firm that is not yet linked, so EB-D-224 cannot be built |
| `POST /api/finance/core-records/{entityType}/{entityId}/firms` | The model-driven form; the generic update tools; the assigned-access sync route | None of them carries the slot rules, the eligibility refusal, the law-firm rule or the revoke confirmation. The sync route takes no input | The pane cannot assign a firm, and uac-r2 condition 4 (one server-side writer) is unmet |
| `CoreRecordFirmAssignmentService` (scoped, `FinanceModule`) | `InvoiceAssignFirmService` (an invoice's firm); `AssignedAccessMaterializer` (access, not slots) | Extending either one would give it a second job and make it a second owner of the slots | The POST has no writer, and the slot rules would sit in the endpoint |
| Keys `ebilling.record.assign_firm` / `ebilling.record.remove_firm` | `ebilling.invoice.assign_firm`; `ebilling.record.override` | Those name other acts and other rights | Deny logs and the ledger would name the wrong act, and R1's Append could not be stated |
| `UniformNotFoundFailClosed` Record branch (design 3e) | The same mode (Privilege path only) | It is the extension | A rights fault on the record would be the uniform 404, not the 503 condition 3 requires |
| `AssignedAccessMaterializer.OrganizationGrantSkipReason` | The materializer's inline organization rule | It is an extraction | The route and the materializer would hold two copies of one rule |
| `ReadActiveFirmNamesAsync` (static overload), `CoreRecordFirm.Ordered` | The §8 GET's own read and sort | They are extractions, so the POST's `firms` is the §8 GET body | A second copy of the read and of the order |
| `scripts/ebilling/Set-EbillingLawFirmTypeCode.ps1` | The other `scripts/ebilling` data scripts | They set other rows | No repeatable, checked way to set the code, so both routes stay 503 |

- **Placement:** the BFF, because this is a pane request that runs the BFF-owned materializer inline.
- No package, no background work, no new column.
- **DI:** one `AddScoped<CoreRecordFirmAssignmentService>()`.
- **Endpoints:** one `MapCoreRecordFirmEndpoints()`.

## Contract for word-add-in-r1 (as built; supersedes section 7)

- The client reads `errorCode ?? reasonCode`, and keys its message on that code, never on `detail`: one code can carry different `detail` texts (the route's 503 and the filter's 503 do).
- Every refusal writes nothing.
- Both routes are internal-only and behind the eBilling kill switch.

### `GET /api/finance/firms?search=&page=1&pageSize=25`

- **Query parameters.**
  - `search`: optional, at most 100 characters. It is a contains match on the firm name, and `%`, `_` and `[` match literally.
  - `page`: 1-200 (default 1).
  - `pageSize`: 1-50 (default 25).
- **200:**
  ```json
  { "items": [{ "firmId": "guid", "name": "string" }], "page": 1, "hasMore": false }
  ```
  Only active organizations of the Law Firm type, in the server's order (name, then id). The BFF does not re-sort a page.
- **400 ValidationProblem:** a bad `search`, `page` or `pageSize` (a `page` above 200 included). Nothing is asked of Dataverse.
- **404 `sdap.access.deny.record_unavailable`** (uniform), for any of these:
  - an external caller;
  - a caller with no user row;
  - a caller with no Read on organizations.
- **503 `ebilling.submission.record_unavailable`:** any fault, or the Law Firm type code is not set. Never an empty list.
- **503 `ebilling.disabled`:** eBilling is off.

### `POST /api/finance/core-records/{entityType}/{entityId}/firms`

- **`entityType`:** `Matter`, `Project` or `WorkAssignment`.
- **Body:** `{ "add": "guid|null", "remove": "guid|null", "confirmRevoke": "guid|null" }`.
  - `add` alone fills slot 1, then slot 2.
  - `add` + `remove` replaces, in the slot that holds `remove`.
  - `remove` alone clears that slot; the other firm is never moved.
  - A call that takes a firm off the record needs `confirmRevoke` equal to `remove`.
- **200:**
  ```json
  { "changed": true,
    "firms": [{ "firmId": "guid", "name": "string" }],
    "assigned": [{ "slot": 1, "firmId": "guid", "name": "string", "active": true }],
    "access": { "state": "applied|pending",
                "granted": [{ "firmId": "guid", "name": "string" }],
                "revoked": [{ "firmId": "guid", "name": "string" }] },
    "recordActive": true,
    "notice": "string|null" }
  ```
  - `changed: false`: the record already said what was asked (a repeat), so nothing was written.
  - `firms`: the §8 GET body after the write. It is `null` when that read faulted after a successful write; the pane then reloads it.
  - `access.state`:
    - `applied`: the access pass completed.
    - `pending`: the pass did not complete. The slots stand; resending the same request finishes it. The pane shows "Saved. Spaarke is still updating who can see this record." with Retry.
  - `notice` (uac-r2 R2) is set on an add that left the firm assigned:
    - **Active record:** "<firm>'s people will get access to this record." It is given when the firm was granted, or when the pass is pending.
    - **Inactive record** (`recordActive: false`, EB-D-233): "<firm>'s people will get access to this record when it is active again." `access.state` is `applied` and `granted` is empty: nothing is granted until the record is active again.
    - **`null`:** on a clear, and on an add where the access pass decided not to grant.

| Status, code | Text (`detail`) | Extra |
|---|---|---|
| 400 ValidationProblem | Neither `add` nor `remove`; `add == remove`; an empty id; an unknown `entityType` | `errors` |
| 404 `sdap.access.deny.record_unavailable` | The pane's text: "This record or firm isn't available to you, or you can't change this record." One answer for all of: an external caller; no user row; an unknown record; no Read or no Write on the record (or, for an add or replace, no Append); an unknown firm; no Read or no AppendTo on the firm | `reasonCode` |
| 409 `ebilling.firm_assign.record_restricted` | "This record is Restricted to internal users, so no firm can be given access to it." | |
| 409 `ebilling.firm_assign.record_secure` | "This record is secure, so assigning a firm would give it no access. Use Manage Access to add named people." | |
| 409 `ebilling.firm_assign.record_limited` | "This record is limited, so assigning a firm would give it no access. Use Manage Access to add named people." | |
| 422 `ebilling.firm_assign.firm_inactive` | "This firm is inactive. Choose another firm." | |
| 422 `ebilling.firm_assign.not_a_law_firm` | "This organization is not a law firm. Choose a law firm." Checked on an add only; a clear removes any firm | |
| 409 `ebilling.firm_assign.firm_no_access` | "This firm can't be given access to this record." | |
| 409 `ebilling.firm_assign.slots_full` | "Two firms are already assigned. Replace one of them." | `assigned` |
| 409 `ebilling.firm_assign.firm_already_assigned` | "That firm is already assigned to this record." | |
| 409 `ebilling.firm_assign.record_changed` | "The firms on this record changed. Check them and try again." | `assigned` |
| 409 `ebilling.firm_assign.revoke_confirmation_required` | "Removing this firm takes away the access this assignment gave its people. Confirm to continue." The pane asks "Removing <firm> takes away the access this assignment gave its people. Continue?" and resends with `confirmRevoke` | `revokes`, `assigned` |
| 503 `ebilling.submission.record_unavailable` | "Spaarke couldn't do this right now. Try again." A filter-level fault carries the filter's own detail, with the same code; the pane shows its own text for the code and never `detail` (K3) | |
| 503 `ebilling.disabled` | eBilling is off | |

The restricted, secure and limited refusals apply only to an add or a replace, whatever the record's state. A clear is never refused for them.
