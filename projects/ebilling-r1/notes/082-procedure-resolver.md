# Task 082 - Procedure resolver

Branch `task/ebilling-082`, from `work/ebilling-r1` at `dfcc9b7355`. Not merged into work. Nothing was written to Dataverse; the only live call was the read-only step 0 count below.

## Status: slice 082a done (step 0 and step 1); the rest is split

The POML is 3-4 days of FULL-rigor work. This session delivered the first, self-contained slice and stopped, with a split for the rest (section "Split for the rest of 082"). Two design points the POML does not answer must be decided before 082c (section "Questions before 082c").

## Step 0

### Merge of origin/master: not done here (deviation 1)

`origin/master` had 209 commits that `work/ebilling-r1` does not have. Of the files this task touches, master changes only two ArchTest files that 082a does not edit (`ContactCreateOwnerPolicyGuardTests.cs`, `RouteAuthorizationGuardTests.Ledger.cs`). Merging master in a task branch would put those 209 commits into the main session's merge of 082, beside 092 and 095d. The main session owns master merges into work. Task 070 is on master (`BillingRuleCatalogue.cs` is in `origin/master`), so the model 7.2 precondition holds.

### Dev count (read-only, 2026-11-04, Dataverse MCP)

| Query | Result |
|---|---|
| `sprk_workassignment` rows | 32 |
| rows with both `sprk_regardingmatter` and `sprk_regardingproject` set | **0** |
| rows with both set, joined to their project (to compare the matter with the project's pair) | 0 rows |

No work assignment carries both lookups, so none can carry "the project's matter only". **The escalation trigger did not fire.**

## Step 1 (082a): CoreRecordChain and NamePairParentAsync

### What landed

- **`SecureRootInheritance.NamePairParentAsync`** (internal static), with the closed result **`PairParentAnswer`** = `None | Parent(Table, Id) | NotAFilingParent(Unrepeated?) | Unknown(Reason)` (an abstract record with a private constructor; the four answers are nested sealed records).
  - It is the pair rule of the private `NameParentsAsync`, moved out with no logic change. `NameParentsAsync` now calls it and maps the answers back: `Parent` adds the parent, `NotAFilingParent` with a reason sets `unrepeated` (#1616), `Unknown` sets `unknown`. The order of the checks, the messages, the type cache, the `faultedTypes` set and the logged warning are unchanged.
  - `NameParentsAsync` stays private. `PairParentAsync` / `ExistingPairParentAsync` (the ownership resolver's pair read) are not touched.
- **`CoreRecordChain`** (`Services/Signals/Billing/CoreRecordChain.cs`, public static, no DI registration):
  - Matter: `[M 0]`, nothing read.
  - Project: one row read (the pair columns), then the pair through `NamePairParentAsync`. `Parent` is distance 1; `None` and `NotAFilingParent` give no parent; `Unknown` throws `CoreRecordChainUnreadableException`.
  - Work assignment: one row read of `SecureRootInheritance.TypedFilingColumns[sprk_workassignment]`; each set lookup is distance 1. The pair is not read (it files under nothing on its own, #1616), and the intermediate lookups are not read (#1630 is not followed).
  - A parent's own parent is never read. A core record that is not found throws; a read fault propagates.
  - A pair naming the record itself names no parent; the same parent twice is one link. Order: distance 0, then the parents by table and id.
- **`CoreRecordChainNoMultiHopGuardTests`** (ArchTests): a source scan of `CoreRecordChain.cs`, line comments ignored. It fails on `NameParentsAsync`, `FindFilingParentsAsync`, `IntermediateFilingColumns`, `IntermediateParentColumns`, `TypedLookupsOf` or `ReadSecureParentsAsync`, and when the file does not call `NamePairParentAsync` or does not reuse `TypedFilingColumns`. Controls: three planted references and a hand-parsed pair fire; the sanctioned shape and a comment naming the forbidden members do not.

### Decisions

1. **`pairType` is a `Guid?`, not the `EntityReference?` of the old 082 POML signature.** `NameParentsAsync` holds the type as a `Guid?` (`FilingFacts.PairType`), so the extraction keeps that; `CoreRecordChain` passes the lookup's id. uac-r2 to confirm.
2. **Two optional parameters were needed to keep `NameParentsAsync` unchanged:** `repeatedIds` (a pair repeating a typed parent or intermediate lookup names nothing separate, the #1630 identity rule) and `faultedTypes` (the batched walk's #1410 F3 set). Both default to `null`. uac-r2 to confirm.
3. **`NotAFilingParent` carries the #1616 reason** when a matter- or project-typed pair sits on a table whose pair does not file on its own (a work assignment). That keeps the answer set closed at four while `NameParentsAsync` still sets `unrepeated`. `CoreRecordChain` never meets that case (it reads the pair on a project only).
4. **A core record that is not found faults** (`CoreRecordChainUnreadableException`), as an unreadable row does, so the job retries and then fails. A missing record has no chain to judge on.
5. **A parent that no longer exists is not read.** One read per record (the constraint). A deleted parent holds no assignment rows, so it confers nothing in the resolver.
6. **`CoreRecordChain` is static, not a DI service.** It needs only the app-only `IGenericEntityService` and a logger, which the resolver (082b) will pass. No registration is added (CLAUDE.md §11).

### Component justification (CLAUDE.md §11)

- **Existing:** `SecureRootInheritance`'s walks (`NameParentsAsync`, `FindFilingParentsAsync`) and its maps.
- **Extension:** the walks follow intermediates and climb past the direct parent, which D-172 forbids for billing. The maps and the pair rule are reused, not copied. Only the one-row read and the distance are new.
- **Cost of doing nothing:** a project's or work assignment's invoice never sees its parent's Procedures and overrides.
- **New surface:** one internal static method and one internal closed record on `SecureRootInheritance` (extracted, not new logic); one public static class and one exception type in `Services/Signals/Billing`. No endpoint, no registration, no package.

### Seeding proofs (each restored; `cmp` confirmed the file identical)

The POML's proof (a) names a resolver test ("M's Procedure does not apply on W1"), which belongs to 082b; it must be re-run there. Proof (b) (the firm condition) is 082b's. 082a ran these:

| Change | Result |
|---|---|
| (a) at chain level: `CoreRecordChain` also reads a work assignment's project parent and adds that project's own parent | 2 of 25 failed: "a work assignment typed under a project is [W 0, P 1]; the project's matter is not in it (no second hop)", and the both-lookups case (the extra hop read a project row that the test does not seed) |
| (c) the shared rule: `NamePairParentAsync` answers `None` for a pair id that is not a record id | 2 of 895 failed (`~CoreRecordChainTests\|~SecureRoot`): the two "not a record id" cases of "a project pair UAC answers Unknown for fails closed". **Found in passing:** no uac-r2 test failed, so before this task nothing tested `NameParentsAsync`'s "does not hold a record identifier" branch (no test in `tests/` names it). Because `NameParentsAsync` now calls the same method, 082a's tests pin that branch for both callers. Reported to the main session; no issue filed, since the gap is closed by this change. |

### Code review and ADR check (Step 9.5)

Scope: the four files of commit `4e98e82a10`. No F-class finding.

- **K4:** `NamePairParentAsync` checks its non-nullable arguments with `ArgumentNullException.ThrowIfNull`, as `PairParentAsync` does next to it. No real path passes null.
- **K4:** the chain row is read with `NoLock`, like every other loader read. A dirty read during a re-file could name the old or the new parent; the evaluation of that moment is then judged on one of the two states.
- **K1:** `PairParentAnswer` is closed by a private constructor, but records also get a protected copy constructor, so code inside the BFF assembly could still derive from it. Only deliberately adversarial code in our own repo could do that.
- **Metrics:** `CoreRecordChain.cs` 138 lines, one public method, about 12 branches. `SecureRootInheritance.cs` +114/−43: the pair block moved into one method, and `NameParentsAsync` got shorter. Nothing new in that file except the extraction.
- **ADR check (applied inline, not as a separate skill run):** ADR-002 (no write, no plugin), ADR-010 (no DI registration added; a static reader), ADR-001 / ADR-008 (no endpoint), ADR-038 (unit tests in the billing test folder; an ArchTest with must-fire and must-not-fire controls). No conflict. §10: placement is the BFF, `Services/Signals/Billing`, beside the loader that calls it; no package; the publish size is below.

### Gates

| Run | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | 0 warnings, 0 errors |
| Unit `~CoreRecordChainTests` (new) | 25/25 |
| Unit `~SecureRoot\|~SecureChild\|~RecordOwnership\|~Inherit\|~ExternalAccess` (the extraction, no logic change) | 6649/6649 |
| Spaarke.ArchTests (full) | 2009/2009 (4 new: 1 real-file check, 3 controls, one a 3-case theory) |
| `dotnet list package --vulnerable --include-transitive` (BFF) | none; no package or csproj changed |
| Publish size: `git archive` of each commit, `dotnet publish -c Release -r linux-x64 --self-contained false`, uncompressed, 169 files each | fresh master `origin/master` 136,945,312 B; base `dfcc9b7355` 137,254,816 B; this branch 137,271,204 B. **Against master: +325,892 B**, most of it ebilling-r1 work not yet on master. **This task alone: +16,388 B** (dll +13,824, pdb +2,564). |

### Reviews still owed

- **uac-r2:** the extracted entry point (decisions 1-3 above: `Guid?` type, the two optional parameters, the reason on `NotAFilingParent`) and the reuse of `TypedFilingColumns`. 087 edits `SecureRootInheritance.cs` after this merges.
- **ontology-r1:** the owner review of `CoreRecordChain` now; of the loader and evaluator change in 082c.
- The adversarial passes are the main session's (two, `tenant-isolation`).

## Questions before 082c

1. **How a gap holds the invoice on each channel.** Model 7.1 says a gap raises one blocking internal finding and holds the invoice. 114's POML says an Override releases "a gap-held **internal** submission" through 105's path, which overrides a held submission (Rejected, reason Blocked, D-101). So on the internal channel the gap finding must make the outcome Blocked. The POML does not say what a gap does on an **SPA** submission: today a deciding finding there is an automatic rejection (`InvoiceSubmissionProcessor.Classify`), and a gap is not a rule failure, so it must not reject (I-5). Options: (a) a gap holds both channels as Blocked, and only internal submissions can be overridden (an SPA one waits for a check that clears it); (b) a gap on SPA is a new held kind. This changes `InvoiceSubmissionProcessor.Classify` and 073's decider, which are not in the POML's file list.
2. **089c pass-2 K1, a rule dropped by a newer Procedure Version.** Proposed decision for 082b's pin resolution: a rule applies for a work date only when it has a Published policy version under the Procedure Version chosen for that date (model 7.2). So a dropped rule applies in v1's window and on a record pinned to v1, and nowhere else, whatever its `sprk_enabled`. A record rule runs only in its base rule's slot: when the base rule's policy is resolved for that date (before exclusion). That also covers 089c pass-1 K3 (a deleted base rule, or a retired Procedure, strands an enabled record rule: its base is not resolved, so it never runs) and deviation 4 (the record rule copies the current version, but runs only where its base is in force). The owner's open question (refuse the change for a record that is not pinned) is a 089c route question, not a resolver one.

## Split for the rest of 082

| Slice | Scope | Files | Notes |
|---|---|---|---|
| **082b** | `ProcedureResolver`: the reads (assignment and override rows on the chain, Procedures by All records / Firm / assigned ids, Procedure Versions, rule sets, policies, policy versions with `sprk_procedureversion` and `sprk_publishstate` Published, record-scoped sets on the chain), each once per invoice; the pure resolution per work date (sources, firm condition, Exclude and Pin with distance, versions and gaps, rule excludes, record rules in their base rule's slot); `BillingRuleCatalogue` Replace / Additive / Split with the pinned list. The 089 must-items: ignore an assignment to a retired Procedure (089b review 2 K1); an assignment row with every core lookup null contributes nothing (080 limit); K3 and the K1 decision above; deviation 5 (an empty set, or an Off rule with no version, contributes nothing). | `ProcedureResolver.cs`, `BillingRuleCatalogue.cs`, `ProcedureResolverTests.cs` | Pure plus reads; no loader change yet. Seeding proof (a) and (b) here. |
| **082c** | Loader and evaluator re-keyed: `LoadRulesAsync` takes the resolver's rules (the `sprk_procedure` null filter and the row re-check removed, the 081 MUST-DO), candidates carry rank, Procedure Version and source; the evaluator's precedence by classification and rank (D-39 `PrecedenceKey` kept); per-line dates from the one resolution; the gap finding (new reason code, `BillingSystemFindings`) and the hold; `BILL-SYSTEM-SUBMISSION` exemption unchanged. Check every `LoadRulesAsync` caller (102's adjustment rule list). | `BillingFactLoader.cs`, `InvoiceComplianceEvaluator.cs`, `BillingSystemFindings.cs`, `InvoiceSubmissionAfterSteps.cs`, `FinanceModule.cs`, loader and evaluator tests | Needs question 1 answered. The ontology-r1 owner review is of this slice. |
| **082d** | The 7.4 snapshot members in `sprk_findingsjson` (one hunk in `InvoiceSubmissionPersister.cs`), the bounded Decision Record summary, the ratchet ArchTest over `sprk_currentversion` readers, NFR-01 timing on 150 lines, publish-size delta. | `InvoiceSubmissionPersister.cs`, `CurrentVersionReaderRatchetTests.cs` | After 092 and 095d merge, which also edit the persister. |
