# 084d — dry-run design (for ontology-r1 and uac-r2 review before the build)

Status: DESIGN, nothing built. This came from a read-only investigation of `work/ebilling-r1` at `cd30c642c7`, on 2026-11-13. Neither POML escalation fires: no change under `Services/Signals/**` is needed, and no override kind's authorization is looser than its write route's.

## 1. The row-source seam

**How the resolver gets its rows.**
- `ProcedureResolver.ReadRowsAsync(IGenericEntityService entities, …)` (`ProcedureResolver.cs:898-919`) reads every row through `entities.RetrieveMultipleAsync`, inside `AllAsync` (`:1302-1319`). That covers chain, assignment, exclusion, Procedure, version, rule-set, policy and policy-version rows.
- It builds `ProcedureRows` (`:1331`), and `new ProcedureResolution(rows)` (`:416`) is the only consumer of that.
- The read service calls it at `RecordProcedureReadService.cs:409-411`.

**The overlay** is a per-request decorator around `IGenericEntityService`, new as `Services/Finance/Procedures/ProcedureRowOverlay.cs`. It is passed to `ReadRowsAsync` for the "After" reading only, and `ProcedureResolver.cs` stays unchanged.
- **Scope.** It holds deleted ids plus after-images, for four tables only: assignment, rule set, policy and policy version. Its constructor refuses any other table, so there are never synthetic Procedures or Procedure Versions.
- **Queries.** On those four tables it drops returned rows that are deleted or replaced, then appends the after-images on the last page, cut to the query's columns. Every other query passes through untouched.
- **Writes.** Every write method throws: create, update, upsert, delete, transaction and associate. The dry run therefore cannot write, by construction.
- **No criteria evaluation.** The overlay never evaluates query criteria. The resolver's own per-row re-checks decide membership: assignments `:973`, record rule sets `:1178-1186`, Procedure rule sets `:1141-1144`, policies `:1212-1217`, policy versions `:1248-1253`.
- **The one exception (for ontology-r1).** For chunked queries (policies by rule set, versions by policy), an after-image is appended only to the chunk whose `In` list holds its parent id. Otherwise the duplicate breaks the dictionary at `:422`. If this is rejected, the fallback is an optional extra-Procedure-ids parameter on `ReadRowsAsync`. That is a row-source change only, but it is a Signals diff.

**Why not a typed overlay on `ProcedureRows`.** It would need rows the first read never loads:
- an "assigned records only" or another firm's Procedure (`:1021-1026`);
- an Off record rule being re-enabled (`:1206`).

Their readers are private to the resolver, so a typed overlay would mean a Signals change. The decorator gets those rows from the resolver's own real reads.

## 2. Override kinds as overlay rows

**Where the rows come from: 089's own service.**
- **Validation.** The read-and-validate half of each `*CoreAsync` becomes a shared `Plan*Async`, so the dry run gives the write's own 400/404/409 through the same `Map` (`ProcedureAssignmentEndpoints.cs:526`).
- **Row images.** The four `*CreateAsync` get an image-only mode that returns the built row before the owner question. Row construction stays in one place, so the ownership census and the creator-person list are unchanged.
- **Server-filled columns.** The overlay adds `statecode = 0` and one per-request `createdon` (service clock).

| Kind | What 089 writes | Overlay |
|---|---|---|
| Assign / Follow / Pin / Exclude a Procedure | One assignment row, id `AssignmentIdFor(core, record, subject, P)` | After-image (synthetic id). A newly assigned Procedure's real rows come from the real read. |
| Remove (incl. Follow that removes the record's own pin) | Delete the row. For a changed rule, also switch the record rule Off. | Id deleted. A changed rule's policy is patched to `sprk_enabled = false`, and the resolver's re-check drops it. |
| Rule exclude | Assignment row (policy, optional firm, reason) | After-image (synthetic) |
| Rule change | Set and policy only if missing (policy created Off, code `BRR-…`); Exclude row; version n+1 on the version in force, `inforcefrom` = now; switch-on | Set and policy synthetic only if new. An existing Off policy is read whole and patched On; it stays real and is asked. Version and Exclude row synthetic. |
| Switch / re-change (084b decision 2, 084c F1-1) | Delete X, then create | Applied in order. The new row gets the same id X (keyed by subject), so it stays real and is asked. Older versions are dropped by the resolver's episode filter (`:779-795`). |

**Request.** One of four write kinds, plus an optional `replacesOverrideId`. This covers the 7 UI kinds and the wizards' two-call plans.

**Synthetic ids are the write's own derived ids.** This is forced: the resolver pairs a record rule only by derived id (`:812-814`). As a result, After and the post-write read compare byte for byte.

**EB-D-218.**
- The synthetic set is exactly the created rows whose by-id load during planning found nothing.
- The After reading gets `Reading.Exempt` for that exact set (an entity-set and id match, never a pattern). `Expose` records exempt pairs separately for tests and asks the caller about everything else.
- The Now reading has no exemptions.
- Real rows the overlay patches or brings in are always asked.
- Real values copied into synthetic rows come from already-asked rows.
- A firm named in the request body is a query firm (asked), so the route cannot answer "what is this firm called" for any id.
- The response lists `proposedRowIds`.

## 3. Authorization (uac-r2 C5)

**Route.** `POST /api/finance/{matters|projects|workassignments}/{recordId}/procedures/dry-run?firmId=&date=`. It goes in `RecordProcedureEndpoints.cs`, which is already in the ledger, so the count stays at 153.

**Filters, in order:**
1. Step 0 (group).
2. The record filter with uniform 404: `finance.read` (as on 084's routes) and `RecordWriteCheck`.
3. `DryRunTargets` (403), built per write in the plan from the existing declarations:
   - Assign: `AssignOrExcludePrivileges()` plus Read on the Procedure.
   - Rule exclude: the same, plus Read on the policy.
   - Rule change: `ChangePrivileges()` plus Read on the policy.
   - Remove or replace: `RemovePrivileges()`.
   - A missing id gets the same 400.
4. `RequireEbillingEnabled`.
5. For remove or replace: the body-aware core of `RequireReadOnTheOverridesSubject` (`:446-493`), as a shared helper, with the same 404 `assignment.override_not_found` and 403 `system_failure`.
6. 401 for a non-GUID caller id.
7. Service planning refusals use the write's codes. Then one EB-D-218 batched check over Now ∪ After; any refusal or fault gives 503 `procedures.unavailable`.

**Single source.** `AssignTargets`, `ExcludeTargets` and `ChangeTargets` (`:390-417`) are refactored onto `AssignChecks`, `ExcludeChecks` and `ChangeChecks`, which `DryRunTargets` also calls.

**Guards.**
- Declaration: the dry run's checks equal the union of the write routes' checks for the plan.
- Behaviour: each kind runs through the real filter chain, extending 084g's guard.

**Differences from the write (both stricter).**
- Remove now needs `RemovePrivileges`, where the removal preview needs `Admin()` only.
- The dry run also needs `finance.read` on the record.

## 4. Now → After and impact

**Response shape:**
```
{ record, date, firm, firmSource,
  proposal: { kind, replacesOverrideId, proposedRowIds[] },
  now:   { procedures, rules },          // 084's two views
  after: { procedures, rules },
  reach: { firm: FirmView|null /* null = every firm */, records: "ThisRecordAndDirectChildren" },
  closes: { d170: bool, warning: string|null, kinds: ["ChangedRuleRemoved"|"BaseRuleSuperseded"] },
  affectedOpenInvoices: { count: int|null, text, basis: "FindingsClosed"|"ResolutionChanged" },
  noRecheck: <O-21 line> }
```

**Reads.** One `Reading` per side, with the future-date probe and the legacy rules on. Legacy rules are read once and shared. Total: two `ReadRowsAsync` calls and one combined caller check.

**Reach.** The firm comes from the body, else the Procedure's or base rule's firm, else every firm. Records: this record plus its direct children (D-172).

**Count.**
- **Kinds that close findings use the write's own selection ("preview = sweep", model §15 answer 2).**
  - Removing a changed rule: `SelectOpenSignalsOfPolicyAsync`, as `PreviewRemovalAsync`, with the existing removal warning.
  - A change: the selection half of `SupersedeBaseFindingsAsync`, pulled out so the write and the dry run share it.
  - The selected set goes to `AffectedInvoiceCounter.CountVisibleOpenInvoicesAsync` unchanged.
- **Kinds that close nothing** (assign, pin, Procedure exclude, rule exclude, plain remove): the In Review invoices in reach whose resolution differs between Before and After (model 8.2's comparison), through a new invoice-id overload pulled out of `AffectedInvoiceCounter:85-115`.
  - The count is capped at 100 children (089e's bound) plus a resolution cap.
  - Past either cap the count is null, never an app-only number.

## 5. Parity test

**Rig.** Extend 084's `RecordProcedureRig` with a writable mode, on a separate instance:
- a transaction interpreter over the in-memory world (create, update by row version, delete; `createdon` and the row version stamped from the rig clock);
- a real `ProcedureAssignmentService` with a stub ownership answer and a mocked close.

**Matrix.** Each kind (incl. switch and re-change) × the guard seeds plus `outranked` and `change prefill` × the three record types × a date before and after the change.

**Steps.**
1. Run the dry run.
2. Write the same change through the service.
3. Read both lists.

**Asserts.**
- After equals the post-write read (only the proposed rows' stamps and `permissions` are normalized).
- Now equals the pre-write read.
- `proposedRowIds` equals the rows the write created.
- The 503s match.

Both sides run through the real resolver. Mutation proofs: dropping `createdon`, the chunk check, or the policy switch-on must each fail the test.

## 6. No-write and concurrency tests

**No write.**
- A strict world: no create, upsert, update, delete, transaction or associate call.
- A close mock with only its select methods set up.
- The ownership resolver gets zero calls.
- No `DecisionRecordWriter` or `SignalWriter` is reachable.
- An IL ArchTest scans the dry-run types for write members, with must-fire and must-not-fire controls.

**Concurrency.** The overlay is built inside the method and is never a field, a DI registration or static. Tests:
- two dry runs with different overrides on one record, interleaved on one service instance: each After equals its sequential answer;
- a concurrent plain read equals Now;
- the world is unchanged afterwards.

## 7. Risks and decisions

- **R1 (decided by the main session; the model is binding).** D-170 applies only when a rule is switched Off: a Procedure retired, or a changed-rule override removed (model line 725, §15 answer 2). A plain Exclude adds a row and closes nothing. The POML's ui-test "Exclude a rule with open findings → D-170 warning" is therefore reworded to "Remove a changed-rule override with open findings → the §15 removal warning". The dry run follows the write.
- **R2.** A change closes base findings as Superseded (089e), not as the D-170 close, and §15 has no warning text for it. Proposed text: "Changing this rule for this record closes the published rule's open findings on this record's In Review invoices; they are not re-checked." (Owner wording; reviewers may comment.)
- **R3.** Tests go under the KEEP contract path (`tests/integration/contract/Api/Finance/`), following 084's deviation 7, not the POML's `tests/unit/...`.
- **R4.** 089's write service is refactored (planning extracted, image mode). `ProcedureAssignmentServiceTests` and `ProcedureAssignmentEndpointsTests` stay byte-unchanged and green, and the ownership census still passes.
- **R5.** The dry run skips the ownership question. It cannot predict an owner refusal (409), and proposed rows have no creator.
- **R6 (uac-r2 to confirm).** A request-body firm is asked as a query firm. A firm the caller cannot Read gives 503 where the write would succeed.
- **R7.** Decorator fidelity (row shapes, chunk check) is covered by the per-kind parity test and a seed past 500 rows.
- **R8.** Cost: two full reads, the count's extra resolutions, and one combined check. A per-request query cache is an option.
- **R9.** The model 8.2 banner is not built yet. The wording must not promise it shows.
- **R10.** Now and After are not atomic: a change someone else writes between them shows up as part of the effect.

## Files

- `RecordProcedureReadService.cs`: the `DryRunAsync` entry, a row-source parameter (`:409`), exemptions (`:384-390`), the combined check (`:488-516`), query-origin firm (`:1124-1137`).
- New: `ProcedureRowOverlay.cs` plus the dry-run DTOs.
- `ProcedureAssignmentService.cs`: planning extraction, image mode, the supersede selection pulled out.
- `AffectedInvoiceCounter.cs`: the invoice-id overload.
- `RecordProcedureEndpoints.cs`: three POST routes.
- `ProcedureAssignmentEndpoints.cs`: the checks refactor, `DryRunTargets`, the shared override-subject helper.
- `RouteAuthorizationGuardTests.Ledger.cs`.
- Tests: the rig, the contract file, the IL guard.
- Client (later in the task): `ImpactStep.tsx`, `AssignProcedureWizard.tsx`.

## Review: uac-r2 (2026-11-13): OK with conditions (binding for the build)

- **C5 confirmed.** Each kind's checks come from one source (`AssignChecks`, `ExcludeChecks`, `ChangeChecks`), guarded by the declaration-equality test. For the steps it shares with the write routes, keep the same filter order: the record filter's uniform 404 runs before the 403s, so an unknown record and a denied one get the same answer.
- **R6 accepted.** A body firm the caller cannot Read gives 503. The client words it as "this preview isn't available for this firm", not as an outage.
- **R5 and R10 accepted, with disclosure.** The response, as a fixed flag or note, and the Impact text say:
  - the preview does not predict an ownership refusal;
  - Now and After are separate reads.

  Nothing presents After as "will succeed".
- **Synthetic-row exemption accepted, with two conditions:**
  1. The planning by-id load that decides "synthetic" runs app-only. A load that faults, is throttled or is refused never counts as "found nothing"; it refuses with 503. Test: when the by-id load faults, the answer is 503 and `Exempt` is never called.
  2. A synthetic id that collides with an existing real row takes the "real, asked" path. Test: a deny-alone on that row gives 503.
- **Before merge:** send uac-r2 the diff with the access files listed at the top.

## Review: ontology-r1 (2026-11-13): OK with conditions F1–F4 (binding for the build)

No Signals diff. Keep the chunk rule (§1). Drop the `ReadRowsAsync`-parameter fallback.

- **F1, planning can write.**
  - The change path sends while it is still planning: the set is created at `ProcedureAssignmentService` ~1095 and the policy at ~1128, then the conflict checks read those rows back (~1101-1104, ~1131-1137).
  - The dry-run extraction stops before those sends and runs the same checks against the image.
  - Planning gets its own write-throwing `IGenericEntityService`. No-write must not rest on a strict mock, since the IL scan cannot cover a type that writes.
  - Add `UpdateIfRowVersionMatchesAsync`, `BulkUpdateAsync` and `BulkUpdateIfRowVersionsMatchAsync` (`IGenericEntityService.cs` ~66, 95, 113) to the overlay's throw list.
- **F2, R2 text.** Write no new text. Reuse `ChangeCloseCompleteMessage` / `ChangeCloseChildrenOpenMessage` (PAS ~726). Those messages carry two scope limits:
  - the close also reaches the records directly under this one;
  - it covers only invoices whose work is all dated on or after the change.
- **F3, creator on replaced rows.**
  - In a switch or re-change the new row keeps id X. `RecordProcedureReadService` ~454-457 then fills the creator from X's old row and adds it to Exposed.
  - That shows the wrong creator, and gives a 503 for a caller who cannot read that person.
  - Fix: for After, null the creator on proposed row ids and leave it out of Exposed.
  - Test it; the parity test normalises stamps, so it misses this.
- **F4, the overlay fails closed.**
  - "Replaced" means any id that has an after-image. Otherwise a duplicate id throws at `ProcedureResolver` ~1231 or ~422.
  - The chunk rule matches the `sprk_ruleset` / `sprk_policy` In condition exactly, and throws if that condition is absent.
  - `RetrieveAsync`, Fetch reads and alternate-key reads on the four tables throw rather than pass through.
  - Invariant to test: each version after-image is appended exactly once.
- **§2 holds.**
  - The policy image is built with `sprk_enabled=false`, so the overlay also applies the switch-on (PAS ~629).
  - Use one `createdon` per request, Kind Utc. Each version after-image pairs with an Exclude row carrying the same stamp.
- **§5 parity conditions.**
  - (a) A seed larger than one 5000-row page (PR ~882), or a smaller page size in the rig.
  - (b) The parent policy sits in a chunk other than the last; assert each after-image is appended once.
  - (c) The rig clock is later than every seeded `createdon`.
- **Known limits, accepted:**
  - K1: the in-force date comes from the dry run's clock. Label the view "if saved now".
  - K2: Now and After are not read atomically (R10).
  - K3: clock skew does not matter.
- Send ontology-r1 the diff before merge.

## 084d-5 design (draft for uac-r2 and ontology-r1)

Status: DESIGN, nothing built. This came from a read-only look at `work/ebilling-r1` at `cd869e3050` on 2026-11-13. It covers what 084d-4 stopped on (its K-1): no route lists the Procedures a billing admin could assign to a record. Neither POML escalation fires: there is no Signals diff, and the route asks at least what the assign write asks.

### 1. The set

**Principle.** The query narrows only by what 089's write itself refuses. Everything else is decided by the real `ProcedureResolver`, through the 084d overlay. No applicability, firm, version or ranking predicate is copied into the list (§4).

**How it is computed** (one request, per record, firm and date):
1. **Now.** `ReadAsync` and `BuildProcedures`, exactly as `ListProceduresAsync` does it, for the lists' firm and date. Every Procedure in that view, of any status (InForce, Excluded, Gap, NotYetInForce), is **on the record**.
2. **Candidates.** Procedures with `sprk_status` = Active and `statecode` 0, optionally narrowed by name (`q`), and minus the ones on the record. Q1 asks about narrowing further by firm and area.
3. **Own rows.** These are the candidates that this record already has an assignment row for, of any state. It is `HasRowAsync`'s own predicate (the record lookup and the subject, no `statecode` filter), so a row the resolver ignores (inactive) still counts. They go to `notOffered` with reason `OwnRow`, because the write would answer 409 `assignment.duplicate`.
4. **Images.** Each remaining candidate gets the write's own Follow-latest row image: id `AssignmentIdFor(core, record, procedure, P)`, built by the existing row builder in image mode. All the images go into ONE `ProcedureOverridePlan`.
5. **After.** `ReadAsync` over `ProcedureRowOverlay(plan)` and `BuildProcedures`, as `DryRunAsync` does it, with `proposedRowIds` set to the images (ontology-r1 F3).
6. **Offered.** The After items whose id is not in Now. Each one carries what the resolver says it would be if assigned: `InForce`, `Gap` or `NotYetInForce`. That is the prototype's "CATALOG minus before.procedures", but decided by the resolver.

**What this gives, point by point:**
- **Status.** A Retired Procedure is never queried (the write refuses it: 409 `assignment.procedure_retired`). The resolver also drops non-Active ones.
- **Published version only.** A Draft-only Procedure, or one whose Drafts were only ever retired, never enters the After output, through the resolver's `NotApplicableOn` and `OpenedAWindow` (D-203, DEF-061). So it is not offered. The write would accept it (Q5).
- **AppliesTo.**
  - AssignedRecordsOnly: offered unless already on the record. This is the main case Assign exists for.
  - AllRecords, and Firm for the list's firm: already a source, so already on the record. Not offered; the Procedure wizard already offers Follow and Pin on its row.
  - An unknown AppliesTo makes the resolver fault (`ProcedureResolver.cs:1068`), which gives 503 for the list (K5).
- **Firm scope.** `firmId` works as on the lists: the query firm, else the record's `sprk_billingfirm`, else none (`NoFirm`). The resolver's O-20 firm condition (`FirmConditionHolds`) then keeps only Procedures with no firm or the list's firm. This matches the prototype's `!p.firm || p.firm === firm`. Another firm's Procedure is assigned after switching the panel's firm, so that Impact and the lists after the save show it (Q6).
- **Already on the record.** Not offered and not returned. The client already holds that list for the same firm and date, and it shows "already applies here", "applies from a parent" or "excluded here" with each row's own wizard. The only flagged entries are those whose refusal the client cannot see:
  - `notOffered: [{ id, name, reason: "OwnRow" }]`.
- **What the write refuses, and whether the list knows** (`PlanAssignmentRowAsync`, `ProcedureAssignmentService.cs:369-437`):

| Write refusal | Known at list time? | List |
|---|---|---|
| 404 `procedure_not_found` | yes | only rows that exist are listed |
| 409 `procedure_retired` | yes | Active only |
| 409 `duplicate` (any row on this record) | yes | `notOffered` `OwnRow` |
| 400 pin version not Active/Superseded of P | yes, per version | Pin versions come from 081 (§5); the client already filters them (084b fix 1 item 4) |
| 400 reason over 2000 characters | input | the client caps it |
| 403 no Read on P (route filter) | yes | the batched check gives 503, never a partial list (§3, Q2) |
| 409 owner refusal (`CreateRowAsync`) | no | the dry-run disclosure note (R5) applies to Impact |
| 503 `read_failed`, 500 `write_failed` | no | the write decides |

**Gap if assigned.** A candidate with a hole on the date is offered as `Gap`, with the After view's gap text. Assigning it holds invoices checked from then on (O-22), so the wizard warns at Choose and Impact shows it (Q7).

### 2. Route

`GET /api/finance/{matters|projects|workassignments}/{recordId}/procedures/assignable?firmId=&date=&q=`
- Three literal routes in `RecordProcedureEndpoints.cs`, beside each core's `procedures`, `rules-in-force` and `dry-run`. The table is fixed by the route.
- `firmId` and `date` parse as the lists parse them (`TryParseQuery`).
- `q` is trimmed, 1-100 characters, a case-insensitive name contains, with Like wildcards (`%`, `_`, `[`) escaped. It is narrowing only. Anything else is 400 `procedures.invalid_query`.

**Response, `RecordAssignableProceduresView`:**
```
{ record, date, firm, firmSource,                   // as RecordProceduresView
  q, bound: 50, tooMany: bool,
  procedures: AssignableProcedureItem[] | null,      // null exactly when tooMany
  notOffered: [{ id, name, reason: "OwnRow" }],
  label: "If assigned now" }
AssignableProcedureItem = { id, name, type, appliesTo, firm,
  statusIfAssigned: InForce | Gap | NotYetInForce,
  versionInForce: { id, number, status, from, to } | null,   // the After item's, for the date
  gap: { kind, text } | null }
```

- **The item is a projection** of the After `ProcedureListItem`: fields are copied, never recomputed. `sources`, `level`, `overrides` and `pinned` are left out, because they describe the synthetic row.
- **Pin versions are not in this answer.** The Pin step reads them from 081's `GET /api/finance/procedures/{id}`, as the Procedure wizard does (084b decision 3). That keeps one source for the version list and its own Read check, and this answer exposes fewer rows.
- **The bound.** The candidate query is `TopCount = bound + 1`. If more than `bound` rows come back:
  - `tooMany: true` and `procedures: null`, and NO After read is made;
  - `notOffered` is empty;
  - the client asks the user to type a name (`q`).
  - It is never a partial list.
  - 50 is proposed because each candidate brings its rules into the After read and its versions into the rights check (K1, Q4).

**Statuses.**
- 200.
- 400 `procedures.invalid_query`.
- 401: a caller id that is not a GUID.
- 403 `sdap.access.deny.*`: no billing-admin privilege.
- 404: step 0, or the uniform record 404.
- 503 `ebilling.disabled`.
- 503 `procedures.unavailable`.
- Nothing is written.

### 3. Access (EB-D-218, EB-D-220, EB-D-221, uac-r2 C5)

**Filters, in order** (the dry run's order, minus its body steps):
1. Step 0 on the group: `RequireInternalFinanceCaller` on the stored external flag, as on 084's routes.
2. `DryRunRecordTargets`, reused as is: `finance.read` then `RecordWriteCheck`, in ONE filter with `FinanceDenial.UniformNotFound`. An unknown record, an unreadable one and one the caller may not write all get one byte-identical 404 (NFR-01), before any 403.
3. `AssignableTargets` (403): `AssignOrExcludePrivileges()`, the assign write's privileges. The per-subject Read is the EB-D-218 check below.
4. `RequireEbillingEnabled`.
5. 401 for a non-GUID `oid`, then the query (400), then the service.

**Recommendation: yes, the caller also needs the assign write's authorization.** It is `RecordWriteCheck` plus `Admin()`, the same checks as the `assignOrExclude` flag (`WritePermissionChecks(AssignOrExclude)`) with `finance.read` in front.
- *Reason:* the list exists only to feed that write. Under uac-r2 C5, a list of what to write never reveals what the caller could not write (the dry run's rule).
- The entry point is already hidden when the flag is false (§5), so a legitimate caller never meets the 403.
- *Rejected:* `finance.read` alone. It would show a non-admin the tenant's Procedure catalog, a wider read than any existing route gives them.

**The one batched check.**
- Rows are read app-only.
- `SyntheticRowsAsync` runs first, app-only on the real service (uac-r2 condition 1). A fault is 503 with nothing exempted. A colliding real id is asked (condition 2).
- Then ONE `CallerCanReadAllAsync` over Now.Exposed ∪ After.Exposed ∪, for each `OwnRow` entry, the Procedure and the own row. The own row decides the refusal, so it is asked.
- Through `BuildProcedure`'s `ExposeVersionsOf`, every version of each offered Procedure is asked (084 F-B), because `versionInForce` and `Gap` derive from the whole set.
- Firms are lookup-origin (084 F-D). A `firmId` in the query is asked.
- Any refusal, unanswered row or fault is 503 `procedures.unavailable`, never a partial list.
- So the assignable read gives 503 whenever the record's Procedures list would. That is deliberate: "not offered because on the record" is derived from Now's rows.

**Ledger.** `RecordProcedureEndpoints.cs` is already a `GovernedFile` (RouteLevelGate). Its reason text gains the three GETs, and the count stays at 153.

### 4. Reuse (§11)

| New member | Existing | Extension / why not | Cost of doing nothing |
|---|---|---|---|
| 3 GET routes in `RecordProcedureEndpoints.cs` | `/procedures` lists only what applies; 081 reads one Procedure by id; `ProcedureEndpoints.cs` has no list | Added to the existing file and ledger entry. A catalog inside `/procedures` would change that list's meaning, add cost to every panel load, and widen its all-or-nothing check | A billing admin cannot assign an AssignedRecordsOnly Procedure from any UI (EB-D-217 B4) |
| `RecordProcedureReadService.ListAssignableAsync` | `ListProceduresAsync`, `DryRunAsync` | A composition of `ReadAsync`, `BuildProcedures`, `ProcedureRowOverlay`, `SyntheticRowsAsync` and `CallerCanReadAllAsync`, in the existing class. No new type | The route would need its own selection, which drifts from the resolver |
| `ProcedureAssignmentService.PlanAssignCatalogAsync(reads, core, record, excludeIds, q, bound)` | `PlanAssignProcedureAsync`, one Procedure, two reads | Per-candidate planning would be 2 × 50 sequential reads. The batch shares the write's checks through two extractions (next rows), on the `WriteRefusingEntityService` | Per-candidate latency, or a list that re-derives refusals |
| `OwnRowQuery(core, record, subjectColumn, ids)`, extracted from `HasRowAsync` | `HasRowAsync` | `HasRowAsync` calls it with one id. One builder, one predicate | The list's `OwnRow` drifts from the 409 |
| `AssignRefusalOf(procedureRow, hasOwnRow)`, pure, extracted from `PlanAssignmentRowAsync:400-431` | the inline checks | Both the write and the batch call it | Same |
| DTOs `RecordAssignableProceduresView`, `AssignableProcedureItem`, `NotOfferedProcedureView` | `RecordProceduresView`, `ProcedureListItem` | A projection: the full item would publish the synthetic row's id and sources | The wizard has no shape to read |

**Exact selection reused** (ontology-r1 condition style):
- applicability, the firm condition, Active and Billing: `ProcedureResolver.Applicability` (`:509-566`), through `ReadRowsAsync`;
- published window: `NotApplicableOn` and `ProcedureVersionRow.OpenedAWindow`, through the future probe in `BuildProcedures` (`RecordProcedureReadService.cs:984`);
- version in force and gaps: `RulesOn`;
- duplicate: `OwnRowQuery`, shared with `HasRowAsync`;
- retired and missing: `AssignRefusalOf`, shared with `PlanAssignmentRowAsync`.

The overlay is 084d-1's, unchanged, so it keeps ontology-r1's F1-F4. Many assignment images in one plan is its K1 case (the assignment table with no primary-key condition): the resolver's own re-checks decide. The only pre-filter is Active plus `q`, and the resolver decides the rest. No ranking code is touched.

**Publish size.** No package. Measure the delta against a fresh master build (BFF hygiene item 4).

### 5. Client

- **Entry point.** A button "Assign Procedure" in the Procedures list header. It is rendered only when `allowedSteps(procedures.permissions, rules.permissions).create` is true, so both answers must say `assignOrExclude`. Otherwise it is absent, not disabled. The panel decides; nothing under `components/wizards/**` reads permissions, so 084g's source scan still holds.
- **`AssignProcedureWizard.tsx`** on `WizardFrame` (084b), with `resetKey "assign"` and no nav:
  1. **Procedure (Choose).** Calls `readAssignable` in `recordProceduresApi.ts` (a read, so the "writes only in `overridesApi.ts`" scan holds) with the lists' own query (`queryOf`: the firm only when its source is `Query`, and the date).
     - Each offered item shows its name, type, appliesTo, firm and if-assigned status and version, with the gap text for `Gap`.
     - `notOffered` items are shown disabled: "This record already has an override for it."
     - When `tooMany`, the step shows a search box and no list. `q` is re-asked with a debounce, through the same `raceTimeout` and `AbortController` as 084d-4.
     - A 503 shows the panel's "unavailable" words and no list.
  2. **On this record.** Follow latest, or Pin a version.
     - The Pin versions come from 081's `getProcedure` in `overridesApi.ts`, which already exists, filtered to Active and Superseded by the existing pinnable rule.
     - A pin needs a version and a reason (at most 2000 characters).
  3. **Impact.** The 084d-4 `ImpactStep` as it is. `plan.ts` already maps `{ kind: 'assign' }` to the dry run's `Assign` request (084d-4 mapping table), and a plan with no removal never asks the removal-preview fallback.
  4. **Confirm.** The existing `executePlan` makes one call, POST `.../procedure-assignments` (089). It is exactly `{ procedureId, mode, pinnedVersionId?, reason? }`.
- **After a save.** A refusal (409 duplicate, 403, owner) shows its title and the wizard stays open (084b). Success triggers the panel's silent re-read of both lists.
- No new package. Semantic tokens only.

### 6. Tests (KEEP path `tests/integration/contract/Api/Finance/`, R3)

- **Contract, `RecordAssignableProceduresContractTests`.** The rig seeds Procedures across:
  - AppliesTo (AllRecords, Firm F, Firm G, AssignedRecordsOnly);
  - firm (none, F, G);
  - Area (Billing, other);
  - status (Active, Retired);
  - versions (none, Draft-only, Active, Superseded plus Active, future-dated, a hole);
  - own row (none, active, inactive);
  - parent row (none, Follow d1, Exclude d1).

  Assertions:
  - the exact offered set, statuses and `notOffered`, on 3 cores × 2 firms;
  - each 400 (`firmId`, `date`, `q` empty or over 100);
  - 401;
  - 403 without `Admin()`, with no read made;
  - 404 byte-identical for an unknown record, an unreadable one, an unwritable one and every step-0 kind;
  - 503 disabled;
  - the bound: exactly 50 gives a list; 51 gives null and `tooMany`, with no After read; `q` brings it back under the bound;
  - an unknown AppliesTo gives 503;
  - no write: a strict world, `WriteRefusingEntityService` on planning, and the overlay. Extend `DryRunNoWriteGuardTests` if a new type appears.
- **Access.**
  - `EveryAskedRow_DeniedAlone_Is503_WithNoList`, with the nothing-denied 200 as the must-not-fire control;
  - `Every200Guid_WasAskedOfTheCaller`, where `AlsoAsked` holds the Now rows that decide absence;
  - by-id synthetic load faults gives 503 and `Exempt` is never called;
  - a colliding id is asked;
  - declaration guard: `AssignableChecks` equals `DryRunRecordChecks` followed by `AssignOrExcludePrivileges()`, with must-fire controls (a check added or dropped on either side);
  - behaviour guard through the real filter chain on 3 cores.
- **Ledger.** `RouteAuthorizationGuardTests` stays green at 153, with the governed-file text updated.
- **Parity, in `RecordProcedureWriteRig`, on 3 cores.** For EVERY seeded Procedure P, assign P Follow-latest through the REAL writer service, then read `/procedures`:
  - offered ⇔ (the write gives `Done` AND the post-write list newly holds P);
  - `statusIfAssigned` and `versionInForce` equal the post-write item's;
  - a write refusal ⇒ P is not offered (and is `OwnRow` when the refusal is 409 `duplicate`);
  - not offered with the write `Done` ⇒ the post-write list is unchanged for P (Draft-only, other area, other firm, or already on the record).

  For each offered P and each version 081 offers, Pin is `Done`. Mutation seeds:
  - drop the `OwnRow` step;
  - the query includes Retired;
  - offered taken from the candidates instead of After − Now;
  - no `ExposeVersionsOf`.
- **Jest.**
  - The entry is absent or present by `create` from both answers.
  - Choose lists offered items, disables `OwnRow` with its reason, and shows the search with no list on `tooMany`.
  - A 503 shows no list.
  - Follow, and Pin (version and reason required), each plan one `assign` step.
  - The Impact body is `Assign`.
  - Finish posts once, with the exact body. A 409 is shown and the wizard stays open.
  - Timeout and abort.
  - Source scans keep their must-fire controls.
  - Dark mode: no colour literals.

### 7. Risks and known limits

- **K1 cost.** The After read pulls every candidate's rule sets, policies and policy versions (`ReadRowsAsync`), and the rights check asks one question per row (Procedure plus versions, 4 at a time). Hence the bound of 50. Measure at 112; above about 2 s is F3.
- **K2.** Now and After are separate reads (dry-run R10). The list can be stale by Confirm, and the write decides (409).
- **K3.** No owner prediction (R5): a 409 owner refusal can still come at Confirm.
- **K4. Absent without a reason.** Draft-only, other-area and other-firm Procedures are absent with no reason given. The fixes are: publish a version (081), or switch the panel's firm.
- **K5.** One corrupt candidate (an unknown AppliesTo) gives 503 for the whole assignable list, failing closed as the resolver does. `q` can narrow around it. The Procedure's admin route fixes it.
- **K6.** One candidate Procedure or version the caller cannot Read gives 503 (EB-D-218). Billing admins read Procedures at Deep from the top unit (084f, 089e), so this needs a Procedure owned outside that subtree (Q2).
- **R1.** 089's write accepts Procedures that can never apply here: other area, or Draft-only, which may be legitimate ahead of publication. The list hides them. Filed as Q5, not changed here.
- **R2.** A pre-filter that drops more than the resolver would can only under-offer. The parity test's other-firm, other-area and AppliesTo seeds catch it.

### 8. Open questions

- **Q1 (ontology-r1).** May the candidate query also narrow by the O-20 firm condition (Firm empty or the list's firm) and Area = Billing, so other firms' rate cards do not fill the bound? The resolver still re-checks both, and R2's parity seeds fail if the narrowing ever drops a Procedure the resolver would apply. *Recommend yes,* citing `FirmConditionHolds` and the resolver's area re-check in a comment.
- **Q2 (uac-r2).** A candidate the caller cannot Read: 503 (the design default, EB-D-218), or drop it, since the write's 403 makes it knowable at list time and a dropped choice reveals nothing? *Recommend 503 for R1,* and revisit with task 191.
- **Q3 (uac-r2).** Confirm §3: the list requires the assign write's own record and privilege checks, not `finance.read` alone. *Recommend yes.*
- **Q4 (owner).** A bound of 50 with name search, versus a larger bound or paging. *Recommend 50 + `q`.* Paging would make a partial list the norm.
- **Q5 (owner).** Should 089's assign refuse a Procedure of another area (O-24: document-only in R1)? *Recommend yes,* as a separate small 089 fix with its own test. Keep accepting Draft-only, since assigning ahead of publication is harmless.
- **Q6 (owner).** Offer only the list firm's Procedures (prototype, and this design), or every firm's, for records billed by two firms? *Recommend the list firm.* After and the lists then show the result, and the panel's firm picker reaches the others.
- **Q7 (owner).** Offer a Procedure that would be a Gap on the date, with a warning, or hide it? *Recommend offering it with the warning.* The hold is O-22's intended effect, and hiding it would be silent.

### 084d-5 design: answers and binding conditions (2026-11-13)

**Owner:** Q4 50 plus `q` (EB-D-227); Q5 refuse non-Billing at assign, Draft-only still allowed (EB-D-228); Q6 the panel's firm (EB-D-229); Q7 offer a Gap with a warning (EB-D-230).

**ontology-r1:** OK. Q1 yes: narrow the candidate query by Area = Billing and by Firm (empty, or the list's firm). Conditions:
1. Reuse, don't re-encode: `ProcedureEntity.AreaColumn` / `FirmColumn`, `RuleDomains.PolicyDomainBilling`. The firm filter is an OR of (`sprk_firm` null, `sprk_firm` = firm), mirroring `FirmConditionHolds` (`ProcedureResolver.cs` ~569). If possible, a test pins the two together.
2. The 50 bound never truncates silently: stable order (name, then id), a truncated flag, the wizard says so. Parity seeds include one case over the bound.
3. Active-but-unpublished (D-203 / DEF-061) comes out as not offered from the resolver, never from a query condition.

**uac-r2:** OK to build. Q2: 503 for a candidate the caller cannot Read (EB-D-218, all or nothing; revisit with 191). Q3: the list requires the assign write's checks (`RecordWriteCheck` + `Admin()`, after `finance.read`). Binding notes:
1. The `tooMany` path also runs the ONE batched check over Now's exposed rows (and the firm) before answering; a refusal or fault gives 503. Tests: `TooMany_StillAsksNowRows`, and denied-Now gives 503.
2. `notOffered` OwnRow asks the own row and its Procedure; an inactive own row counts and is asked too.
3. A byte-identical 404 across unknown, unreadable, unwritable and step-0, before the 403 `Admin()` check. Keep the no-read-before-404 test.
4. `q`: escape the Like wildcards AND reject control characters; 400 is the only bad-input answer.
5. The ledger reason text names all three GETs; the count stays 153.

Reviews: one Opus pass here, then uac-r2's; ontology-r1 sees the code.
