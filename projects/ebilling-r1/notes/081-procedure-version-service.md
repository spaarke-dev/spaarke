# Task 081: Procedure and Procedure Version service and endpoints (EB-D-148)

Branch `task/ebill-081-procedure-service`, base `162369cdf5` (origin/work/ebilling-r1). Rigor FULL, opus/high, directional steps.
Written 2026-10-22.

## What was built

- `ProcedureService` (`Services/Finance/Procedures/`). It owns these operations on a Procedure:
  - create, with its rule set;
  - update;
  - version create, with copy-forward;
  - publish;
  - date change;
  - version retire;
  - retire preview;
  - retire.

  Every write is one `ExecuteTransactionAsync` that includes a conditional touch of the Procedure row (`IfRowVersionMatches`), so concurrent version writes on one Procedure are serialised.

  Invariants it enforces:
  - I-P1: versions in force never overlap, using half-open `[from, to)` windows.
  - I-P3: a Rate card, or a Procedure that applies to a firm, names its firm.
  - I-P4: Applies to and Firm are locked once a version has been in force.
  - I-P5: retiring the Procedure switches every rule Off and closes their open Signals (D-170); retiring a version closes nothing.
- `AffectedInvoiceCounter` is the one "open invoices you can see" count, made as the caller through `CallerRecordAccessProbe.GetCallerRightsForRecordsAsync`. If any step fails, it shows no number. Task 089 reuses it unchanged (POML note c).
- `SignalClosureService.SelectOpenSignalsOfPolicyAsync` is the one Signal selection. Both the close and the preview use it, and the preview is a count over that selection.
- `ProcedureEndpoints` (Minimal API under `/api/v1/finance/procedures`):
  - Step 0: internal finance caller and eBilling enabled.
  - Per-route filter `BillingAdminAuthorization.OnProcedure`: record Write plus WriteRuleSet, plus the extras.
  - Errors are ProblemDetails with an `errorCode`.
- `RuleSetService` is re-keyed per (policy, Procedure Version) for edit, decide, `IsDraft` and `Predecessor`, with copy-forward and publish plans. Its assign and visibility routes refuse a Procedure's rule set with 409 `ruleset.procedure_owned`.
- `PolicyVersionValidator` allows a `sprk_procedureversion` link only on a billing policy.
- Creator-person stamping:
  - `RecordCreatorPerson.StampedChildTables` and `scripts/Set-ChildRecordCreatorPersonSchema.ps1` gain `sprk_procedure`, `sprk_procedureversion`, `sprk_policy` and `sprk_policyversion` (POML note b, D-173).
  - Each of these is secured only after a read finds zero non-null values. Otherwise the table is reported and skipped: no securing and no grant.
  - The Pester pin and the census follow.
- `ProcedureRolePrivilegeGuardTests` (ArchTest) covers POML note a and the uac-r2 constraint:
  - No source-controlled role holds Create or Write on `sprk_procedureversion` or `sprk_procedureassignment`.
  - Until uac-r2 task 191, no role holds any privilege on the three Procedure tables.
  - Must-fire and must-not-fire controls are included.
- Write-path registry row **I-22** (numbered I-21 on the task branch; renumbered at the merge into work, after master took I-19) added in `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md` §5.

## Deviations and decisions

1. **D-79 firm clause reader** (`RuleSetService.GetFirmClauseAsync`). `wasInForce` now also counts a policy version with `sprk_publishstate = Published`.
   - Under model 7.2, `sprk_currentversion` means "latest published" and is display only.
   - A version published under an earlier Procedure Version was in force for its window, even though the pointer has moved on.
2. **Legacy `BillingFactLoader` excludes Procedure rule sets** (`sprk_procedure` must be null, checked in the query and again per row), until task 082's resolver reads Procedure Versions.
   - The legacy loader selects by the set's scope and firm. On a Procedure set that would apply an `AssignedRecordsOnly` Procedure's rules to every invoice, and it would ignore the version windows.
   - Until 082, a Procedure set's scope and firm are display only.
3. **Scope lock (I-P4) definition.** A scope change is locked when any version is Active or Superseded, or when it is Retired and published a policy version (it put a rule in force).
   - A Draft retired without publishing never locks.
   - Known limit: a version published with no rule and then retired does not lock. It governed nothing.
4. **Visibility to firms follows the Procedure's type.** It is set at create and re-set in the same transaction when the type changes.
   - The rule set's own visibility route refuses a Procedure set, so the Procedure is the one place it changes.
5. **Policy tables stamped** (POML note b). `sprk_policy` and `sprk_policyversion` join the stamped tables and the script, and are homed in SpaarkeMaster.
6. **Update answers from what was written.** The update response is built from the row as read plus the written fields, not re-read, so a failed re-read cannot turn a committed change into a 500.
7. **Unknown outcomes.** A transaction with no fault answer, or a send cancelled by its own timeout, is reported as "may or may not have been saved", never as "nothing was written".
   - The create path names the Procedure id that may exist.
   - Version create does not retry after an unknown outcome.
8. **Date-only values** are written as UTC midnight, as the rest of Finance writes them.
9. **Version names** are `"{Procedure name} v{n}"`, shortened to fit the 200-character name column.

## Seeding proof (the preview's caller filter)

I made `AffectedInvoiceCounter` count every open invoice, ignoring the caller's rights. Three tests then failed:
- the preview count: expected 2, got 3;
- the retire count;
- the singular-text test.

The change was reverted, and all three pass again.

## Publish size

Method: Release, framework-dependent `linux-x64`, `Compress-Archive` Optimal over `deploy/api-publish/*` (PDBs included, 192 files each), from fresh short-path worktrees.

| Build | Zip |
|---|---|
| master `ef8a91a157` | 37.749 MB |
| base `162369cdf5` | 38.297 MB |
| task `91c6560a2e` | 38.348 MB |

- Task delta: **+0.051 MB**. Against master: +0.599 MB, which is the rest of ebilling-r1.
- The later review-fix commit changes only source in the same assemblies, so its delta is negligible and was not re-measured.
- No package added. `dotnet list package --vulnerable --include-transitive`: no vulnerable packages.

## Tests

- Unit tests, filtered (Procedure, RuleSet, BillingFactLoader, SignalReevaluation, RecordCreatorPerson): 958 / 958.
  - Two unrelated timing tests failed only under parallel load and pass in isolation (105 / 105): `InvoiceContextServiceTests.ASlowSource_TimesOutThatSectionOnly` and `ProvisionProjectIdempotencyTests` (a log-count assertion).
- ArchTests: 1357 / 1357, including the census, the creator-person agreement, the ledger and the new role guard.
- Pester `Set-ChildRecordCreatorPersonSchema.Tests.ps1`: 35 / 35, run with Pester 5.7.1. Pester 6.2 is broken on this machine.
- The must-fire case was proven by mutation: a script that grants on an unsecured column fails the test.

## Reviews

Two Opus adversarial passes, then code-review and adr-check (Sonnet). Every F-class finding is fixed:

- The create, update, dates and version-retire routes now report an unknown outcome, and a cancellation as Unknown.
- Retire: the close loop survives timeouts, and the final count cannot turn a committed retire into a 500.
- Assign and visibility are refused on a Procedure set.
- Script: securing happens only after the empty check, with no grant on an unsecured column.
- Scope lock: retired versions handled as described in decision 3.
- Update no longer re-reads.
- Policy tables stamped.
- The legacy loader excludes Procedure sets.
- Exceptions are logged.
- Role-privilege pin added.
- Version-mismatch tests now cover every version write.
- Write-path registry row I-22 added (I-21 on the task branch).
- Version name length capped.
- Date kind aligned with the rest of Finance.

Known limits (K), reported rather than fixed:

- **Timing channel in the retire preview.** The selection runs app-side before the caller-rights probe, so response time grows with the hidden Signal volume. Fixing it would need new auth surface (an OBO query); the POML specifies the probe. Question for uac-r2.
- **Lookup existence checks** (firm, issued by, contacts) run app-only. A caller learns only whether an id exists. Question for uac-r2.
- **If the caller cannot be resolved**, the count is omitted, which leaks one bit. It fails closed.
- **No route yet puts rules into a version.** That is task 116.
- **The retire close runs in the request.** A Procedure with thousands of open Signals may outlast the front-end timeout. The Procedure is already Retired and the rules Off, so a retry or the nightly D-170 sweep finishes the close.
- **A retired Procedure's rule set stays Published.** It is excluded by the loader until 082.
- **A name change does not rename the rule set.** Display only.
- **`Notes` has no length check.** An oversized value fails closed with `write_failed`.
- **`SignalClosureService` is unsealed** with virtual members, as a test seam. For ontology-r1.

Optimistic concurrency was read on dev (2026-10-22, read-only metadata GET) and is enabled on `sprk_procedure`, `sprk_procedureversion` and `sprk_procedureassignment`. Whether a same-value touch moves the row version is confirmed at the first live write.

## Ontology review (2026-10-22)

**F1 (fixed).** `SignalClosureService.SelectOpenSignalsOfPolicyAsync` had put `sprk_regardinginvoice` and `sprk_regardinginvoiceline` into the one shared ColumnSet. That selection also serves the nightly sweep (`ClosePoliciesNotInForceAsync`, every policy, Ontology included) and the admin retire close (`ClosePolicyRetiredNowAsync`). The two columns exist only where ebilling's Signal schema is deployed, so elsewhere every per-policy select would return 400: the sweep swallows it (no Off/Retired policy ever closes its Signals) and the admin retire close throws.

- Fix: the criteria live in one builder, `SignalClosureService.OpenSignalsOfPolicyQuery(policyId, withBillingSubject)`. The flag changes only the ColumnSet (adds `BillingSubjectColumns`), so "preview = sweep" still holds for which rows are selected.
- `SelectOpenSignalsOfPolicyAsync` and `ClosePolicySignalsRetiredAsync` take `withBillingSubject` (no default). Only the Procedure retire preview and the Procedure retire close pass `true`; the sweep and `ClosePolicyRetiredNowAsync` pass `false`.
- Tests (`SignalReevaluationJobTests.ProcedureRetire`): must-fire, the sweep and the admin retire close select neither invoice column and close their Signals against a store that answers 400 to either column; and every PolicyRetired caller (Procedure preview, Procedure close, admin close, sweep) sends the builder's criteria, with columns differing only by the opt-in. `ProcedureServiceTests` asserts the preview and the close pass `true`.
- Seed: with the builder temporarily always adding the two columns, both new tests failed (the sweep reported a policy failure instead of `(1, 0, 0)`; the admin close's ColumnSet held 6 columns, not 4). Restored; they pass.

**ArchTest.** `tests/Spaarke.ArchTests/SignalClosureServiceDerivationGuardTests.cs`: no type in the `Sprk.Bff.Api` assembly derives from `SignalClosureService` (reflection over that assembly only, so test subclasses and mocks do not count). Must-fire: a direct and an indirect subclass fixture in the test assembly are reported. Must-not-fire: the base type and unrelated types are not.

**Ordering rule.** Until 082 is live in an environment, nothing there may set `sprk_ruleset.sprk_procedure` on a rule set whose rules are in force today, otherwise those rules silently stop being evaluated (`BillingFactLoader` skips Procedure rule sets until 082). On dev today 0 rule sets have it set and 081 writes none. Recorded as a constraint in the 082 POML, with the removal of the skip as MUST-DO acceptance criteria.

**Ontology's answers to the open questions.**

- **075's legacy-set rules** attach to a Procedure only by copy-forward into a Draft Procedure Version. Never PATCH `sprk_procedureversion` onto an existing policy version (D-14). The legacy rule is turned off in the same transaction as the publish that puts its copy in force, and none of this happens before 082.
- **Corrections** are a new Draft version (copy-forward), then publish; never an edit of a published version.
- **A re-added rule** gets a new policy version under the current Draft (highest + 1); the old chain is untouched. Earlier PolicyRetired Signals stay closed, and the D-11 30-day per (policy, matter) suppression still counts.
- **The unsealed `SignalClosureService`** is accepted with the ArchTest above.

## UAC review (2026-10-22)

**F1 (fixed): the affected-invoice count could come out lower instead of absent.** `CallerRecordAccessProbe.GetCallerRightsForRecordsAsync` never throws: a failed token exchange, a failed WhoAmI, or any `RetrievePrincipalAccess` answer other than 200 (a 429, a 5xx) came back as `AccessRights.None`. Under throttling the preview undercounted, and a failed page counted 0. The test doubles threw `HttpRequestException`, which the real probe never does.

- Fix: a new batched `GetCallerRightsForRecordsOrUnknownAsync` next to the existing method (UAC approved adding it in 081). It returns null (unknown) per record for any answer other than 200 or 403, and for every record on a failed exchange or WhoAmI; 403 stays None. The existing method's behaviour is unchanged: `RetrievePrincipalAccessAsync` reads the shared core's unknown as None.
- `AffectedInvoiceCounter` asks the new method and gives no number if any entry is unknown.
- The `ProcedureServiceTests` doubles return unknown instead of throwing.
- `ExternalPlaneNoOboGuardTests` forbids the new method on the external plane too (it is a token-taking question), with a must-fire control.
- Tests: `CallerRecordAccessProbeRecordsOrUnknownTests` (wire-seam style of `AuthorizationGateTimingTests`). Must-fire: 429, 500, 503, 401, 404, a transport fault, a failed exchange, a failed WhoAmI and no token each give unknown. Must-not-fire: 200 gives the rights, 403 gives None, and the existing method still gives None. `Preview_WhenOneInvoicesRightsAreUnknown_ShowsNoCount_NeverALowerOne` covers the counter.
- Seeds: with the probe core reading every non-200 as None, the 5 status-code must-fire cases failed. With the counter treating unknown as not visible, the counter test failed (count 1 instead of none). Both restored.

**A–E.**

- **A** is F1 above.
- **B, `Set-ChildRecordCreatorPersonSchema.ps1`.**
  - The empty-before-securing gate is now universal (UAC #2276). Before securing ANY unsecured `sprk_createdbyperson`, on any table, the script counts rows holding a value. A non-zero count FAILs that table: no PUT, no grant, exit 1.
  - After `-Apply` secures a column it counts again. A value that arrived in between FAILs the run (exit 1); the column stays secured.
  - The help text describes both.
  - Pester (41/41, run with a clean Pester 5.7.1): `-Verify` and the dry run on an unsecured Procedure column, empty and valued; the race; and a must-fire on an unsecured, valued `sprk_budgetrevision` (no PUT, exit 1).
  - Seeds: without the re-count, 3 tests failed (the race must-fire among them). With the gate back to the four ebilling tables, 2 failed (the `sprk_budgetrevision` must-fire among them).
  - On dev, uac-r2 found 9 existing values on `sprk_budgetrevision`, `sprk_decisionrecord` and `sprk_signal`, all stamped by an app user at create, so nothing needs clearing there. Any non-zero count on dev would therefore be new.
- **C.** `ProcedureRolePrivilegeGuardTests` scans `src/dataverse/solutions/**/Roles/*.xml` recursively, with a selector control. Today all 12 role files are under SpaarkeMaster.
- **D, I-P5.** The rule-set proposal edit, accept and reject routes, on a set with `sprk_procedure` set, also need the caller's Write on that Procedure. This is the same Record-path `write` check on `sprk_procedures` the Procedure routes declare, run by `FinanceAuthorizationFilter` from a new endpoint filter (`BillingAdminAuthorization.RequireWriteOnTheRuleSetsProcedure`). It reads the set's Procedure app-only through `RuleSetService.ProcedureOfRuleSetAsync`.
  - Without Write the routes return 403 `insufficient_rights`. A fault reading the set is a 403 `system_failure`. A legacy set is unchanged.
  - Must-fire and must-not-fire tests are in `RuleSetEndpointsTests`. Seed: without the filter on the routes, 6 of the 12 new tests failed.
  - Cost: on a proposal route, step 0 runs twice (once per filter).
- **E.** `RecordCreatorPerson.cs` now names `RecordOwnerAssignmentCensusTests.StampedOutsideCodifiedChildren` as the exemption list.

**Known limits accepted by uac-r2.**

- The retire preview's timing channel.
- The app-only firm and contact existence checks. Follow-up in 116: check the caller's Read through the probe.

**I-P2 is not enforced against app-only generic writers.** Decision: Procedure tables are not access tables (090's F4 lists `sprk_procedureversion` in NotAccessTables). No playbook node, output mapping or field-mapping rule writes them; the Procedure service is the only writer.

**Unconfirmed until the first live smoke test:**

- that a same-value status rewrite moves the row version;
- that `0x80060882` inside a transaction maps through `ExecuteTransactionFault`.

**Task 191** must, in the same change:

- flip `UntilTask191`;
- add org Read (O-6), plus Create and Write for billing-admin;
- give the external role nothing (design §3.8).

Until then GET is sysadmin-only too. (No `projects/ebilling-r1/tasks/191*.poml` exists yet, so this note lives here only.)

**Hard rule (UAC #2273).** `Set-ChildRecordCreatorPersonSchema.ps1 -Verify` must exit 0 in an environment before the BFF carrying 081 deploys there.

**Creator-person copy at persist (decision 2026-10-22, UAC #2277 review).** `InvoiceSubmissionPersister` copies the submission's `sprk_createdbyperson` onto the invoice, its lines and the approval, and that copy stays.

- It is safe because of the universal empty-before-securing gate plus a hard deploy order: `Set-ChildRecordCreatorPersonSchema.ps1 -Verify` must exit 0 in an environment before any BFF carrying ebilling deploys there.
- The owner cannot be re-resolved at persist time: the persister runs app-only in a job, and the submitter's systemuser is known only from intake.

**Stamping `sprk_ruleset` and `sprk_procedureassignment`** belongs to 090, not 081.

### UAC re-check round 2 (2026-10-22)

1. **`Set-ChildRecordCreatorPersonSchema.ps1`, (c2) granted the narrow profile on a column the gate refused to secure.**
   - Live Dataverse refuses a field permission on an unsecured column (0x8004f508): 12 retries of 10 s, then a throw, so the run died before (d) and the publish and printed no APPLY INCOMPLETE summary. The dry run printed "WOULD grant" right after its own FAIL.
   - (c) now records each table the empty-before-securing gate refuses; (c2) skips it before any output or grant. `-Apply` reaches the publish and the APPLY INCOMPLETE summary and exits 1.
   - The dry run now exits 1 when it has a FAIL or MISSING (it exited 0 before, whatever it had found).
   - Pester (43/43, clean Pester 5.7.1): the simulator refuses a fieldpermission POST on the unsecured `sprk_budgetrevision` column, as live does, and the seeded narrow row for that table is dropped in the scenario. Seed: without the (c2) skip, 3 tests failed (the Apply must-fire, the dry-run must-fire, and the other-tables must-not-fire).
2. **I-P5, `/draft`.** `POST .../{ruleSetId}/draft` carries `RequireWriteOnTheRuleSetsProcedure()` too, because `sprk_ruleset.sprk_sourcedocument` is not field-secured and a billing admin could set it on a Procedure's set. Tests: no Write on the Procedure gives 403 and queues nothing; a set with no Procedure is unchanged; Write lets it through.
3. **I-P5, reads of a Procedure's rules.**
   - New `BillingAdminAuthorization.RequireReadOnTheRuleSetsProcedure` on `GET .../{ruleSetId}`, a sibling of the Write filter (one shared body, same server-side resolution of the Procedure, same ordering: step 0, billing admin, Procedure). It asks the caller's Read on the Procedure through `CallerReadCheck`, which keeps "Dataverse said no" apart from "Dataverse said nothing": denied is 403 `insufficient_rights`; an unanswered question, or a fault reading the set, is 403 `system_failure`.
   - The shared body now uses `CallerReadCheck` for the Write filter too, so an unanswered question on the edit, accept and reject routes is also `system_failure` (it was `insufficient_rights`; still 403).
   - `GET .../rulesets` (the list) leaves out each Procedure set whose Procedure the caller cannot Read, and each whose rights are unknown (fail closed); legacy sets are unchanged; one rights question per distinct Procedure. The Procedure id travels on an internal, never serialized `RuleSetSummary.ProcedureId`.
   - Routes covered: GET one, GET list, `/draft`, PATCH, accept, reject. Judged out of scope: `PUT .../assignment` and `PUT .../visibility` (they return only the set's summary, no rules or clause text, and are billing-admin writes on the set's own metadata), `POST .../publish` (returns counts and a visibility flag; a Procedure's versions are published through the Procedure routes), and `POST .../rulesets` create (creates a new, Procedure-less set). Answered (main session, 2026-10-22): no extra gate is needed. Publish (`CodeProcedurePublish`), assignment and visibility (`CodeProcedureOwned`) already refuse every Procedure-owned set with 409, whatever the caller's rights, so none of them changes a Procedure's set.
   - Ledger: `RequireReadOnTheRuleSetsProcedure` is credited; the Write entry's line references are refreshed. Tests in `RuleSetEndpointsTests` (must-fire for denied and unknown, must-not-fire for legacy sets, list).
4. **Doc fix, `CallerRecordAccessProbe.GetCallerRightsForRecordsOrUnknownAsync`.** The remark said only the caller's own cancellation is rethrown. That holds per record; a cancellation during the OBO exchange or `WhoAmI` is caught by those steps and becomes "every entry unknown". Corrected. Two tests added: cancellation during `WhoAmI` (the production step) gives all-unknown; a 200 with an unreadable body gives unknown for that record. Not tested: cancellation inside the OBO exchange itself (it needs a real MSAL client; the same catch-all shape as `WhoAmI`).
5. Race FAIL is not sticky, and values written before the gate existed are never counted: filed as #2280 (DEF-040).

### UAC re-check round 3 (2026-10-22)

1. **F1, the Rule Review page misread the new Procedure 403s.**
   - Client: `App.tsx` turned the page read-only on ANY 403 and showed "You do not have the billing administrator right", so a Procedure `insufficient_rights` or a `system_failure` locked the whole page. Now only a 403 whose `reasonCode` is `sdap.access.deny.insufficient_privilege` does (`isBillingAdminRefusal`, `accessAfterFailure` in `ruleSetApi.ts`); any other 403 shows its per-action notice and leaves the access as it was. A refused list or create modal follows the same test, so a `system_failure` list shows as a notice with the page still usable.
   - Server: the Procedure 403 on the rule set routes (`OnTheRuleSetsProcedure`) now has a detail, "You need Write on this rule set's Procedure." or "You need Read on this rule set's Procedure.", matching the filter. A no-answer keeps the generic `system_failure` detail.
   - Tests: the client must-fire (billing-admin 403 gives readOnly) and must-not-fire (Procedure `insufficient_rights`, `system_failure`, a 403 without a reasonCode, a non-403 carrying the code) in `ruleSetApi.test.ts`; the detail asserted in the Write and Read 403 tests in `RuleSetEndpointsTests`.
2. **F2, the list hid that it was incomplete.** A `Failed` rights answer (429, 5xx, fault) dropped the set silently. Now ANY unknown answer logs a warning with the count and the list is 403 `system_failure` (the single-set GET's shape; the client shows it as a notice). A known deny still just leaves the set out. Tests: one unknown answer gives `system_failure` and no list (must-fire); a deny-only list is 200 without the denied set (must-not-fire).
3. **K2, no cap on the rights questions.** One question per distinct Procedure over up to 5,000 sets. `RuleSetEndpoints.MaxProcedureRightsChecks` = `InvoiceContextService.MaxSiblingAccessChecks` (1,000; the other `ScanAsync` caller uses 200 and there is no shared constant). Above it: a warning log, then 403 `system_failure`, before any question is asked. Tests: cap + 1 distinct Procedures gives `system_failure` and asks no more than the cap; exactly the cap is answered in full.
4. **F3, tests.** `List_LeavesOutProcedureSetsTheCallerCannotRead_...` reworked for F2: two sets share one Procedure and the rights-call count equals the number of distinct Procedures. `List_DoesNotSerializeTheProcedureId` now asserts 200 and exactly one element before checking the property is absent.
5. **F4, doc.** `Set-ChildRecordCreatorPersonSchema.ps1` help said the dry run only reports the gate's FAIL; it exits 1 on any FAIL or MISSING.
6. **K1, existence oracle** (403 versus 404 for a Procedure set the caller cannot Read, also through the 409s): filed: #2284.
7. **K3, Dataverse role reads on `sprk_ruleset` / `sprk_policy` bypass the BFF Procedure Read gate:** tracked: #2285.

## Live steps left for the main session (none run here)

1. **Run `scripts/Set-ChildRecordCreatorPersonSchema.ps1` on dev: dry run, then `-Apply`, then `-Verify`, BEFORE the BFF carrying 081 is deployed.**
   - It creates or secures `sprk_createdbyperson` on the Procedure and policy tables after the empty checks, and publishes `sprk_policy`.
   - Order matters: once the BFF stamps the column, the empty check would find values and skip securing.
2. Deploy the BFF.
3. Smoke test: create, version, publish, retire against dev, which confirms the row-version touch.

## Open questions

- **uac-r2** (answered 2026-10-22: see "UAC review" above):
  - the preview timing channel;
  - app-only lookup existence checks;
  - the rule-set proposal edit and decide routes on a Procedure set need only the admin marker, not Write on the Procedure (the task 076 route contract);
  - only System Administrators hold `prvCreatesprk_procedure` until task 191;
  - `sprk_ruleset` and `sprk_procedureassignment` creator-person stamping (080 created the columns; not stamped here).
- **ontology-r1** (answered 2026-10-22: see "Ontology review" above):
  - the unsealed `SignalClosureService`;
  - task 075 drafting creates policy versions with no Procedure Version link;
  - corrections under a published Procedure Version;
  - re-adding a rule that was Off at copy time.
- **Owner:** whether a Procedure needs a visibility-to-firms setting independent of its type (today it follows the type).
- **ontology-r1 and uac-r2 review records** for this task are still to be obtained by the main session (POML step 6).
- **Merge note:** open PR #2258 (`fix/uac-2257-playbook-writes-as-user`) also edits `tests/Spaarke.ArchTests/RecordOwnerAssignmentCensusTests.cs`. Expect a textual merge on the census lists. Task 090 shares `RecordCreatorPerson.cs`, the script and its Pester pin, and the census; 081 merges first.
