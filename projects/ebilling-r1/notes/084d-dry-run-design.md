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
