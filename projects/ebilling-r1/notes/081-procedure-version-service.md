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
- Write-path registry row **I-21** added in `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md` §5.

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
- Write-path registry row I-21 added.
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

## Live steps left for the main session (none run here)

1. **Run `scripts/Set-ChildRecordCreatorPersonSchema.ps1` on dev: dry run, then `-Apply`, then `-Verify`, BEFORE the BFF carrying 081 is deployed.**
   - It creates or secures `sprk_createdbyperson` on the Procedure and policy tables after the empty checks, and publishes `sprk_policy`.
   - Order matters: once the BFF stamps the column, the empty check would find values and skip securing.
2. Deploy the BFF.
3. Smoke test: create, version, publish, retire against dev, which confirms the row-version touch.

## Open questions

- **uac-r2:**
  - the preview timing channel;
  - app-only lookup existence checks;
  - the rule-set proposal edit and decide routes on a Procedure set need only the admin marker, not Write on the Procedure (the task 076 route contract);
  - only System Administrators hold `prvCreatesprk_procedure` until task 191;
  - `sprk_ruleset` and `sprk_procedureassignment` creator-person stamping (080 created the columns; not stamped here).
- **ontology-r1:**
  - the unsealed `SignalClosureService`;
  - task 075 drafting creates policy versions with no Procedure Version link;
  - corrections under a published Procedure Version;
  - re-adding a rule that was Off at copy time.
- **Owner:** whether a Procedure needs a visibility-to-firms setting independent of its type (today it follows the type).
- **ontology-r1 and uac-r2 review records** for this task are still to be obtained by the main session (POML step 6).
- **Merge note:** open PR #2258 (`fix/uac-2257-playbook-writes-as-user`) also edits `tests/Spaarke.ArchTests/RecordOwnerAssignmentCensusTests.cs`. Expect a textual merge on the census lists. Task 090 shares `RecordCreatorPerson.cs`, the script and its Pester pin, and the census; 081 merges first.
