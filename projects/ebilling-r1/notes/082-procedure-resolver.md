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

## 082a review and decisions (2026-11-04)

### Reviews

- **uac-r2 review: OK, no F.** Decisions 1-3 (the `Guid?` pair type, the two optional parameters, the reason on `NotAFilingParent`) are confirmed equivalent to the old code branch by branch. Notes:
  - **U1** (fixed here): a comment on the `NoLock = true` app-only read in `CoreRecordChain`. It serves procedure selection only, is NOT an access decision, and must not be reused to judge access.
  - **U2** (fixed here): the "pair id is not a record id" cases are now in uac-r2's own access suite, one per caller, in `SecureRootInheritanceTests`: `AProjectWhosePairIdIsNotARecordId_IsUnverifiable_ThroughTheRecordWalk` (through `ReadSecureParentsAsync`, the public entry to the private `NameParentsAsync`) and `NamePairParentAsync_APairIdThatIsNotARecordId_IsUnknown` (direct, on a project and a work assignment).
  - **U3** (acceptance criterion for 082b / 082c, below).
- **ontology-r1 review: OK, no F.** The chain matches D-172 and D-177 (3), (8), (9). Notes:
  - **O1** (082b criterion, below).
  - **O2** (082c criterion, below).
  - **O3:** `NoLock` during a re-file is accepted as K4.
  - **The K1 proposal (item 2 under "Questions before 082c") is CONFIRMED** by ontology (D-177 (1), (2), (14); D-175; D-202): a rule applies on a date only with a Published version under that date's Procedure Version, regardless of `sprk_enabled`. A record rule runs only where its base rule is resolved for that date, before exclusion. This also closes 089c K3.
- **Adversarial pass 1: no F.** Four known-limit findings, K1 to K4.
  - **K1 (fixed here):** the no-multi-hop guard was a deny-list. `CoreRecordChainNoMultiHopGuardTests` is now an allow-list (below).
  - **K2 (fixed here):** the `PairParentAnswer` switch in `CoreRecordChain` had no `default`. It now handles `None` and `NotAFilingParent` explicitly (no parent, no fault), `Unknown` (throws, as before), and `default:` throws `UnreachableException` naming the answer type. **No unit test:** `NamePairParentAsync` / `ReadAsync` has no seam for injecting an answer, so a fake future answer cannot be fed in. (A non-sealed record has a protected copy constructor, so code in the BFF assembly could derive from the base type; the private constructor only stops other assemblies.) The `default` guards a fifth nested record added later.
  - **K3 and K4:** see the 082b and 082c criteria and the census below.
- Adversarial pass 2: no F; K1, K2 and K4 fixed in 42a4af82ee.

### Fixes in this round

- **Guard allow-list.** Any `SecureRootInheritance.<member>` reference in `CoreRecordChain.cs` outside {`TypedFilingColumns`, `PairNamesAParentOnItsOwn`, `PairIdColumn`, `PairTypeColumn`, `NamePairParentAsync`, `Matter`, `Project`, `WorkAssignment`} fails (the members the file really uses). A reference to `CoreAncestorResolver` fails. An intermediate literal fails: the lookup columns and tables are read live from `SecureRootInheritance.IntermediateFilingColumns` and `IntermediateParentColumns` (`sprk_regardinginvoice`, `sprk_regardingevent`, `sprk_invoice`, `sprk_event`, and the columns on those that name a matter or project), so the list cannot drift. The by-name deny list stays as a second net. One must-fire control per class (a member outside the allow-list, `CoreAncestorResolver`, an intermediate literal), each planted into a copy of the real text. Must-not-fire: the real file and a synthetic sanctioned shape.
- **Seeding proof:** planting `OwnerLookupsThroughIntermediatesAsync` in `CoreRecordChain.cs` made the guard fail (see the 082a fix-round gates below).

### OWNER DECISIONS (2026-11-04, typed in session c7)

- **Gap hold on SPA.** Owner: "if this is flagged when the SPA user (a partner user) then the user needs to receive the warning message so they can fix it at that time. whichever is the rights way to ensure the issue is surfaced to the SPA user".
  - 082c criterion: an SPA submission with a gap is never auto-rejected and never silently held. The partner user gets a descriptive warning at submit time (the submit response and the SPA status view), naming what is missing in terms the partner can act on, with a reason code so it can be tracked. The internal channel holds as Blocked and stays overridable. 082c designs the exact shape (classifier and 073 decider) and shows it to uac-r2 / SPA-r3. This answers Question 1 above.
- **Record-rule date window.** Owner chose "Only from its own effective-from". A record rule runs only on work dates on or after its own effective-from. Earlier dates are judged by the base version in force on that date, without the record's change. This answers ontology's deviation 4 question (option 1).

### Acceptance criteria carried forward

**082b**
- **U3, disclosure test:** the chain may include a Secure parent (a project under a Secure matter, or a work assignment under a Secure project). Billing outputs judged with that parent's Procedures must not carry the parent record's id or name to readers: findings JSON, line reasons, Signals, the review page, the add-in replay. Procedure and rule names are OK. Test: a project under a Secure matter, findings rendered, and no matter id or name in the reader-facing output. Inventory any existing output that already names the core or parent record; it belongs with the owner's open 095a question.
- **O1, order-swap test:** a work assignment with both a matter and a project lookup gives [W0, M1, P1]. The table-then-id sort is for stability only, NOT precedence. D-177(3): Exclude wins, and two different pins raise a blocking gap. A resolver test swaps the order and expects the same result. It needs seeded data; dev has 0 such rows.
- **K1 / K3 date rule:** a rule applies on a date only with a Published version under that date's Procedure Version, regardless of `sprk_enabled`; a record rule runs only where its base rule is resolved for that date, before exclusion. Plus the owner's limit: a record rule runs only on work dates on or after its own effective-from; earlier dates use the base version in force that day, without the record's change.

**082c**
- **O2:** when the job runs out of retries after a `CoreRecordChainUnreadableException`, the submission ends held or blocked with a stated reason. It never passes, and is never judged on [W0] alone. Needs a must-fire test.
- **Review K3 (permanent vs transient):** the reason shown says whether the chain fault is permanent (a core record not found, a pair UAC answers Unknown for) or transient (a read fault), so the operator knows whether a retry can help.
- **The owner's SPA warning-at-submit requirement** (above): the partner user gets the warning with a reason code at submit time and in the SPA status view; never auto-rejected, never silently held.
- **Census open item:** none from dev (below); 082c still decides "backfill or fault" for a linked project with an empty pair on another environment.

### Census (review K4): projects linked to a matter with no BFF-managed pair

Read-only on dev, 2026-11-04, through the Dataverse MCP `read_query`, the same route as the step 0 count. The Project to Matter N:N is `sprk_Project_Matter_nn`, intersect `sprk_project_sprk_matter`.

| Query | Result |
|---|---|
| `sprk_project` rows | 30 |
| `sprk_project_sprk_matter` link rows | 7 (7 distinct projects) |
| of those, projects with an empty `sprk_regardingrecordid` or `sprk_regardingrecordtype` | **0** |
| of those, projects whose pair id equals the linked matter's id | 7 of 7; the pair type on all 7 is the `sprk_matter` record type |

**Count: 0.** Every project linked through the N:N also has a BFF-managed pair naming the same matter, so on dev no linked project would lose its matter from the chain. Dev is not the whole estate: today a linked project with an empty pair gets [P0] with no fault (`None` is "no parent"), so 082c should decide "backfill or fault" before another environment is judged. Nothing was written to Dataverse.

### 082a fix-round gates

| Run | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | 0 warnings, 0 errors |
| Unit `~CoreRecordChain\|~SecureRoot\|~SecureChild` | 1864/1864 (7 new: 3 + 4 cases of the two U2 tests) |
| ArchTests `~CoreRecordChain` | 9/9 (3 new must-fire controls) |
| Guard seeding proof: `nameof(SecureRootInheritance.OwnerLookupsThroughIntermediatesAsync)` planted in `CoreRecordChain.cs` | 1 failed ("... is not in the allow-list"); restored, `cmp` identical, 9/9 again. The old deny-list did not name this member, so it would have passed. |
| U2 seeding proof: `NamePairParentAsync` answers `None` for a pair id that is not a record id | 7 of 9 matched tests failed (all 7 new U2 cases); restored, 1864/1864 |

## Slice 082b: ProcedureResolver and the Replace / Additive / Split classification (2026-11-04)

Branch `task/ebilling-082b`, from `origin/work/ebilling-r1` at `0735a566e5`. Nothing was written to Dataverse; no live call was needed.

### What landed

- **`BillingRuleCatalogue.PrecedenceOf(RuleType)`** and the enum **`BillingRulePrecedence`** (Replace, Additive, Split). An exhaustive switch over the 14 billing types, taken from the reviewed table in `rule-set-scope-design.md` 3.3. A non-billing type throws. ExpenseRule is the only Split type. AddedFee is Replace, keyed by `PrecedenceKey` (D-39). The evaluator applies the classification in 082c; nothing reads it yet.
- **`ProcedureResolver`** (`Services/Signals/Billing/ProcedureResolver.cs`). A public static class with no DI registration, like `CoreRecordChain`.
  - `ReadAsync(entities, logger, core?, firmId, ct)` reads every row once and returns a `ProcedureResolution`.
  - `ProcedureResolution.RulesOn(workDate)` is pure. It returns `ProcedureRulesOnDate`: the Procedures applied (version, rank, sources, deciding rows), the rules in force (the policy version content plus the Procedure whose slot each rule takes), the rules excluded, the Procedures excluded, and the gaps.
  - `ForReaders()` is the reader-facing projection (U3).
- **Reads, each once per invoice** (ids chunked at 500, every query paged, every row re-checked after its query):
  1. the chain (`CoreRecordChain`);
  2. the active `sprk_procedureassignment` rows on the chain;
  3. the Active Billing Procedures that apply to all records, apply to the invoice's firm, or are assigned (Follow or Pin) on the chain;
  4. their versions;
  5. their rule sets;
  6. the chain's record-scoped sets (Record, or Firm record of the invoice's firm, with exactly one core lookup naming a chain record);
  7. those sets' enabled, active Billing policies;
  8. those policies' Published versions.

  `sprk_currentversion` is never read (7.2), and `BILL-SYSTEM-SUBMISSION` is skipped (D-125).

### Semantics as implemented (for the ontology-r1 owner review)

1. **Sources and the firm condition (6.1, O-20).** A Procedure has three possible sources: All records, Firm (Applies to = Firm, naming the invoice's firm), and Manual (a Follow or Pin row on the chain).
   - The firm condition (the Procedure's Firm is empty or is the invoice's firm) is checked before any source counts, so it applies to all three.
   - "Assigned records only" with no assignment on the chain has no source: not applicable, no gap.
   - Retired Procedures are filtered in the query and re-checked (089b K1).
2. **Procedure-level rows (6.5, D-177 (3)).**
   - The rows at the nearest distance decide, whatever their mode. An Exclude among them wins, so a d1 Exclude is lifted only by a d0 row.
   - Level: Manual d0 or Manual d1 when a deciding row exists, else Firm, else All records.
   - Rank: `(Level, -Distance, FirmSpecific)`, where Distance is the deciding row's distance (0 for a computed source) and FirmSpecific means the Procedure names a firm.
3. **Version per date (7.1, 7.2).**
   - Each deciding row makes a choice: a Pin names its version; a Follow, or a computed source alone, takes the version in force on the date.
   - "In force" means Active or Superseded with an effective-from, inside the half-open [from, to) window (model 3.1).
   - Two versions in force on the date throw `ProcedureResolutionFaultException` (I-P1).
   - Before the first in-force version's from: not applicable. A Draft-only Procedure, or one whose only versions are Retired, is also not applicable.
   - Otherwise a date in no window is a gap: `HoleBetweenVersions` when a later version exists, else `AfterLastVersion`.
   - A pin to a Retired version is `PinnedVersionRetired`. A pin to a Draft version, or to a version that is missing or belongs to another Procedure, is `PinnedVersionNotInForce`.
   - Deciding rows that make different choices for the date raise `ConflictingParents`.
4. **Rules (7.2; 089c K1 confirmed by ontology-r1).** For each enabled policy of the Procedure's set, only the Published policy versions linked to the chosen Procedure Version count. The latest one with effective-from on or before the date applies, so corrections apply from their own date. A rule that a later version dropped is still enabled, but it has no version under the later Procedure Version, so it applies only in the earlier window, or on a record pinned to the earlier version.
5. **Rule-level overrides (6.3, 6.5).**
   - Rows for this firm (row Firm empty or equal to the invoice's firm) and the record rules based on the rule are grouped as overrides of their base rule. The overrides at the nearest distance decide.
   - A plain Exclude among them removes the rule. Otherwise the rows are changes: each record rule whose own version is in force on the date runs in the base rule's slot, with the base rule's Procedure and rank, and the base rule is listed as excluded (`ReplacedByRecordRule`).
   - A record rule is paired with its change's Exclude row by the same chain record and the same firm as its set. A record rule whose Exclude row is missing still counts as a change.
6. **Record rule slot (089c K3, confirmed).** A record rule is considered only through its base rule, so only when the base rule was resolved for that date, before exclusion. That covers a dropped rule, a retired Procedure, and a deleted base (`sprk_basedonversion` names a version that is not among the rows read).
7. **Owner decision (record-rule date window).** A record rule runs only on dates on or after its own effective-from (the latest Published version with from on or before the date). On earlier dates the change is not in effect, so the base version in force on that date runs and is not excluded.
8. **U3 (`ForReaders`).** An allow-list DTO built field by field. It carries Procedure names, Procedure Version numbers, rule codes, level and distance, the pin flag and the record-rule flag. It carries no record id, record name, override reason, row id or Procedure id. The internal basis (`ProcedureOverrideRow.ChainRecord`, `Reason`) is for the 7.4 snapshot only.

### Decisions and deviations (each one line; owner or reviewer to confirm)

1. **A Follow and a Pin at the same distance that choose different versions are a gap (`ConflictingParents`).** D-177 (3) names only two different pins. A version disagreement between two d1 parents is read as the same conflict, failing closed with a hold. **Open for ontology-r1.**
2. **A pinned version's rules apply before its own window.** For work dated before the pinned version's first policy version, the earliest Published policy version under it is taken ("whatever the date", 7.2). **Open for ontology-r1.**
3. **An Exclude row with a record rule paired is a change.** Before the record rule's effective-from, that Exclude does not remove the base rule (the owner's decision, applied to the pair). A plain Exclude removes the rule on every date.
4. **Among rule-level overrides at one distance, a plain Exclude wins over a change.** This is D-177 (3) applied to rule-level rows, which 6.5 says "follow the same rules". A nearer change that is not yet in effect keeps the base in force even when a farther plain Exclude exists, because the nearer row wins.
5. **In force means Active or Superseded** (model 3.1, `ProcedureService.IsInForce`), although the 7.1 pseudo code says "Active". A Superseded version stays in force for its own window.
6. **The rule set's `statuscode` is not required.** The Procedure Version's status and the policy version's `sprk_publishstate` decide. The set's statecode must be Active.
7. **Inactive assignment rows (statecode not 0) are ignored.** The BFF only deletes rows, so an inactive row exists only if someone deactivated it outside the BFF.
8. **Fail closed on rows the BFF never writes.** An unknown override mode, a mode other than Excluded on a rule-level row, or an unknown or empty Applies to on an Active Procedure throws `ProcedureResolutionFaultException`. An override row whose core lookups name no chain record, and a record-scoped set without exactly one core lookup or of another firm, contribute nothing.
9. **Not in 082b:** the rate card per Procedure Version (7.4 / 085). The output names the Procedure Version id; 085 or 082d maps it to the card. The rank-based displacement (Replace types) is the evaluator's (082c), because it depends on gate and handler acceptance.
10. **POML proof (a) re-targeted**, per the 082a notes, to the resolver test "a Procedure assigned on M applies on P (d1) and does not apply on W1 under P".
11. **For 082c:** legacy ClientDefault and Firm sets (no `sprk_procedure`) are not read by the resolver. 082c decides whether the loader keeps loading them beside the resolver until 075's copy-forward, or drops them.

### U3 inventory: existing outputs that already name the core or parent record (read-only, for the owner's open 095a question)

| Surface | Names the core record? | Names a parent? |
|---|---|---|
| `sprk_findingsjson` (`InvoiceSubmissionPersister.cs:2016-2051`) | No (`invoiceName` is the firm, invoice number and date, `:239`) | No |
| Finding facts and templates (`BillingRuleCatalogue.cs:221`; `BillingCoreRecordFacts`, `BillingFactSet.cs:211`) | No: CoreRecordWindow cites the open and close dates only | No |
| Billing Signals (`BillingFindingSignalMapper.cs`; subjects are the invoice and its lines, `InvoiceSubmissionPersister.cs:247`) | Not found (`SignalWriter` not read in full) | Not found |
| Internal review read model (`InvoiceReviewReadService.cs:48`, `:59`, `:658-675`, `ReviewCoreRecordDto(Type, Id, Name)`) | **Yes: id and name** of the invoice's own core record | No |
| Firm-facing `GET /invoice-submissions/firm-records` (`ExternalInvoiceSubmissionEndpoints.cs:351`) | **Yes: ids** of the caller's own readable core records, no names | No |
| Add-in replay | No findings-replay surface found in the BFF (only idempotency replay) | n/a |
| Record-rule codes (`BRR-` plus a derived id, `ProcedureAssignmentService.cs:1444`) | No. The derived id is a hash of (core table, record, firm, base rule), so someone who knows a record id could confirm that a code belongs to it (a K-class oracle). | Same |

No surface names a parent core record today. 082d must keep `ChainRecord` and `Reason` out of any reader-facing member of `sprk_findingsjson`. Model 7.4 lists "chain record" in the submission snapshot while U3 lists findings JSON as reader-facing. **082d must resolve that tension**: for example, an internal-only member that the review read model never projects, or the distance only.

### 082b gates and seeding proofs

| Run | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | 0 warnings, 0 errors |
| Unit `~ProcedureResolverTests\|~BillingRulePrecedenceTests\|~CoreRecordChain\|~BillingFactLoader\|~InvoiceComplianceEvaluator\|~BillingRuleCatalogue` | 572/572 (49 resolver cases and 20 precedence cases are new) |
| ArchTests `~CoreRecordChain\|~RecordOwnerAssignmentCensus\|~WaivedRuleFamily\|~BillingRuleDefinitionCaller\|~Billing\|~Procedure` | 169/169 |
| `dotnet list package --vulnerable --include-transitive` (BFF) | No vulnerable packages; no package or project file changed |
| Publish size, `linux-x64`, framework-dependent, base `0735a566e5` vs HEAD | 137,296,440 → 137,431,980 bytes (+135,540, about 0.13 MB); `Sprk.Bff.Api.dll` +115,712 |
| Proof (a), re-targeted: `CoreRecordChain` adds a project parent's own parents (a second hop) | 2 of 46 failed: "a Procedure assigned on M applies on P (d1) and does not apply on W1 under P" and "a Pin keeps its version for work dated in a later version's window". Restored, `cmp` identical. |
| Proof (b): the firm condition is skipped when manual rows exist | 1 of 46 failed: "a manually assigned Procedure naming firm F does not apply to G's invoice on that record". Restored. |
| Extra mutation (c): `decisive.Any(Excluded)` → `.All(...)` | Both O1 "Exclude wins" cases failed (run with (d)-(g); `All` over no rows also excludes every unassigned Procedure, so that run's 22 failures were confounded) |
| Extra mutations (d)-(g), rerun without (c): record rule judged at `DateOnly.MaxValue`; rules not filtered by the chosen Procedure Version; rule-exclude firm filter dropped; chain record ids appended to the reader-view Procedure name | 7 of 49 failed, each a targeted test: the owner date window, record rule per firm, K1/K3 dropped base, versions per window, Pin in a later window, rule excluded for firm F, U3. Restored. |
| Fail-closed fix (`b7d3589bed`): unknown or empty Applies to throws; an unnamed Procedure reads "(unnamed Procedure)" | The 3 new cases failed before the fix and pass after |

## 082b review and fix round (2026-11-04)

Branch `task/ebilling-082b`, from `0e264aaa04`. Nothing was written to Dataverse; no live call. Where the coordinator's correction from ontology-r1 D-203 (5)-(7) (items A1, A2, A6) conflicts with the first fix brief, the correction is followed.

### Reviews received

- **uac-r2: U3 APPROVED, no F.** One known-limit note, K1: `Level` and `Distance` in `ForReaders()` show that a parent at that level holds a Procedure assignment. That is accepted while Procedures stay catalogue-level. If Procedures are ever created per record, or named from the record, it becomes F and the two fields must go. A one-line comment now sits at `ForReaders()`.
- **ontology-r1 D-203** (recorded in full below).
- **Adversarial pass:** two F1 (retiring the first or only version switches the Procedure off; the content of a removed change comes back), one F3 (the firm condition untested for an All-records Procedure), and K1 to K6.

### Findings and what changed

| Finding | What changed | Tests (all fail against the old file unless marked regression) |
|---|---|---|
| **F1 #1: retiring the first or only version** | `ChooseVersion` runs the F2-d test first (`NotApplicableOn`). "Before the Procedure's first version opened" now means before the earliest effective-from over Active, Superseded **and Retired** versions that have one (`ProcedureVersionRow.OpenedAWindow`). Retired versions stay out of the in-force set, so work in a Retired version's window, or after the last end, is a gap (model 3.1 I-P5). A Draft never opens a window. The whole Procedure retired is untouched: the Procedure query filters it, so no gap. | V1 Superseded then Retired + V2 Active: Feb 1 is a gap, Mar 15 is V2, a date before Jan 1 is not applicable; the only version Retired: any date from its from-date is a gap, a date before is not applicable; with an end: after the end is `AfterLastVersion`; retiring the Procedure raises no gap (regression, 3 version statuses) |
| **F1 #2: the content of a removed change comes back** | `RecordRuleVersionOn` counts only the versions of the **current episode** (below). This replaces the brief's "highest-numbered Published version only", which breaks an edit inside one episode (coordinator A2). | removal then re-change: Jan 20 and Apr 1 give the base rule, Jul 1 gives v2; an edit inside one episode: v1, then v2 between its from-date and v3's, then v3 (regression for the rule it replaces) |
| **F3: firm condition untested for All-records** | Tests only. | An All-records Procedure naming F applies to F and not to G, on a matter and with no core record; the same Procedure also assigned on the matter still does not apply to G (the case the seeding mutation reaches) |
| **K1: record-rule pairing** | `RuleOverridesOf` finds a record rule by its **derived id**, `ProcedureAssignmentService.RecordRulePolicyIdFor(RecordRuleSetIdFor(table, record, firm), basePolicyId)` (the helpers are reused, not copied). `RequireBasedOn` then checks that every Published version of it names a version of its base rule; if not, `ProcedureResolutionFaultException`. A cleared link can no longer turn the change's Exclude into a plain Exclude. | a cleared based-on link, and one naming another rule's version, both throw |
| **K2: ToString leaks** | `PrintMembers` on `ProcedureOverrideRow`, `AppliedProcedure`, `RecordRuleSlot` and `ProcedureGap`: names, numbers, kinds, distance and counts only. Xmldoc on `ProcedureResolutionFaultException`, `RulesOn` and the class remarks: `ex.Message` names ids for operators and must never be shown to a reader (see the 082c criteria). | each record's `ToString()` holds no id, row id, Procedure id, reason or matter name, both built directly and from a resolution over a Secure matter |
| **K4: wording** | The comment at `RulesOf` and one test name now say "enabled is required but not sufficient". `sprk_enabled` is Off only for a removed record rule or a rule of a retired Procedure; it never picks a version, and "dropped" is never inferred from Off. | (comment only) |
| **K6: malformed rows** | A row naming both a Procedure and a rule, or neither, throws `ProcedureResolutionFaultException`, after the "core lookups name no chain record" skip. | both throw |

### D-203 (ontology-r1), as received

- **Q1:** a Follow and a Pin that choose different outcomes for the date (two versions, or a version vs none) are a `ConflictingParents` gap. If they choose the same version, no gap.
- **Q2:** keep the earliest Published policy version under the pin. The F2-d applicability test runs FIRST: work dated before the Procedure's first Active version is not applicable, pinned or not. A Retired pinned version is a gap.
- **Deviation 1** (Superseded Procedure Versions are in force for their window; Retired is a gap) and **deviation 2** (only the Exclude paired with a record rule is date-limited; a plain Exclude removes the base on every date): agreed.
- **(5) / A1, retiring a version is a gap:** agreed, with (a) the model 7.1 applicability test ("at least one Active version") widened the same way, to Active, Superseded or Retired; (b) it must NOT fire when the whole Procedure is retired (a normal end of life: its policies are Off, its dates not applicable); (c) prefer that 081 refuses to retire a version that was never Active, or record a fail-closed known limit.
- **(6) / A2, a change removed and made again:** count only the versions published since the last removal (the current episode). Inside the episode the latest version whose from-date is on or before the work date applies. Before the episode's first from-date the base rule runs.
- **(7) / A6, `sprk_enabled`:** "required but not sufficient". The enabled filter only switches off a removed record rule and a policy whose Procedure was retired. It never picks a version.

### Decisions made in this round (confirm or correct)

1. **Episode boundary (A2).** The current episode starts at the **`createdon` of the change's Exclude row**: a version counts if its `createdon` is on or after it. Why this and not the version number: an edit adds a version in the same episode, so version numbers do not mark episodes. Why it holds: `ProcedureAssignmentService` writes the Exclude row, the version and the switch-on in one transaction (row first), and a removal deletes the row, so the next change has a newer row. A version of the current episode is never created before its row. `ProcedureOverrideRow.CreatedOn` and `ResolvedPolicyVersion.CreatedOn` (internal) carry the two values; both queries now read `createdon`.
   - **No Exclude row, or one with no `createdon`:** no boundary is known, so every version counts (the earlier behaviour). A version with no `createdon` beside a known boundary does not count.
   - **Model 6.3 check.** "Remove an override: the base rule runs again for invoices checked from then on." This matches: between a removal and the next change the record rule is Off and not read, so the base runs; once a re-change exists, dates before its first from-date also get the base, and the removed change's v1 is never used again (Jan 20 gives the base in the removal test).
2. **Gap kind for a Retired version's window** is `HoleBetweenVersions` (its doc already reads "a hole, or a withdrawn version's window"). A date past the last end with no successor stays `AfterLastVersion`.
3. **Follow vs Pin (D-203 Q1).** A version beside "none" (a gap) is `ConflictingParents` too, not the first gap's own kind. Rows that all end in a gap keep the first gap. When rows agree on a version the result is `Pinned` if any of them pins; before, `Pinned` followed the RowId order of the deciding rows, which is random (found while writing the agree test).
4. **Pins run the F2-d test first (D-203 Q2).** The old test "a pinned version's rules apply before its effective-from" (one version from Mar 1, pin, Jan 15) now gives *not applicable*; it was replaced by two: a pin dated inside the Procedure but before the pinned version's window takes that version's earliest policy version, and a pin dated before the Procedure's first version is not applicable for a pin to an Active, a Retired and a Draft version alike (after that date the pin's own outcome decides).
5. **081 `RetireVersionAsync` was NOT changed (A1 c).** It cannot refuse a Draft cheaply: "retire" is the only way to discard a Draft (`ProcedureService` has no delete, and `procedure.draft_exists` tells the user to "publish or retire it before creating another"). An existing test relies on a Draft being retired without publishing (`Update_ScopeChange_AfterADraftWasRetiredWithoutPublishing_Succeeds`). Refusing would remove the discard path, which is a 081 contract change, not a resolver fix. ~~Recorded as a fail-closed known limit: a Draft that has an effective-from and is then retired counts as a Retired version with a from-date, so it can open a window early.~~ **FIXED in round 2 (D-203 (9)), no longer a known limit:** a Retired version counts as having opened a window only when a Published policy version sits under it (see "082b re-check and fix round 2", F1). The alternative recorded here as "for later" is what shipped.
6. **Model 7.1 text** ("at least one Active version"): ~~not edited here~~ edited in round 2 (D-203 (10)); see the section below.
7. **`RequireBasedOn` checks every Published version of a record rule**, as `EnsureRecordRuleAsync` does before it writes. If a path is ever added that edits a record rule by adding a version (today `RuleSetService` refuses record-scoped sets), it must copy `sprk_basedonversion`, or the resolver faults.

### Known limits (K)

- **K3 (082c criterion):** 082c must run the gate before it calls `BillingRuleCatalogue.PrecedenceOf`, which throws for a non-billing rule type.
- **K5:** the change's start is a UTC date (`sprk_inforcefrom` is written as `UtcNow` and read as its UTC date) and errs strict (coordinator's classification).
- ~~**Draft retired with a from-date:** decision 5 above.~~ Fixed in round 2 (D-203 (9)).
- **`createdon` tie** (wording corrected in round 2, re-check K4): an older episode's version counts as current only if the change, its removal and the re-change ALL fall in one stored second (the older version's `createdon` must be equal to or after the new Exclude row's, and it cannot be later than the removal). That is three BFF calls in one second; not seen. The boundary has not been checked against live Dataverse timestamps (no live call here).
- **A record rule whose policy id is not the BFF-derived one** (made by hand) is not recognised; its Exclude reads as a plain Exclude. The BFF never writes one.
- **`ToString` of other records still prints ids:** `ExcludedProcedure` (Procedure id), `ExcludedRule`, `ResolvedRule` and `ResolvedPolicyVersion` (policy and version ids), `CoreRecordLink` (record id). The brief named four types. They carry no override reason, and a collection member never prints its elements, but a direct `ToString()` of one is the same class of leak. Not changed; now an 082c / 082d criterion (round 2, re-check K6, below).

### Criteria carried forward

**082c**
- `ex.Message` of `ProcedureResolutionFaultException` (it names row, Procedure and rule ids) must never be shown to a reader. In particular the SPA warning at submit time (owner decision above) is built from a reason code and `ForReaders()` only, never from the exception text.
- K3 above: gate before `PrecedenceOf`.
- A gap raised for a Retired version's window (`HoleBetweenVersions`) reads the same as any hole to the partner; the reason code can tell them apart if 082c wants to.

**082d snapshot design (uac-r2, binding)**
- Store ids; never recompute.
- Chain ids, Procedure Version ids, override row ids and reasons go in an app-only or field-secured part of the snapshot.
- The findings JSON carries only the `ForReaders()` projection.
- Names in the internal part are resolved at read time, behind the record check, and never frozen.

**#2563 (existing output that names the core record)**: `InvoiceReviewReadService` returns the core record name after the invoice check only. uac-r2 triaged it as **not a defect** under the current access model and closed it: an internal caller who passes the invoice check is already a sharee of a Secure core record (an invoice is a codified secure child), a non-Secure record's name is the invoice lookup's display name, and the finance routes are internal-only (step 0). A correction narrowed the third point: the review header shows nothing beyond what the finance authorization filter already allows for the invoice itself; any change to that filter belongs to unified-access-control-r2. **Reopen if `sprk_invoice` stops being a codified secure child, or if any finance route starts admitting external callers.**

### Supersedes (in the 082b notes above)

- "Semantics as implemented" 3: a Draft-only Procedure is still not applicable, but a Procedure whose only versions are Retired is now a gap from the earliest Retired from-date; the not-applicable date is the earliest from over Active, Superseded and Retired versions.
- Item 4 and the K1 / K3 date rule above: read "regardless of `sprk_enabled`" as "enabled is required but not sufficient".
- Item 5 and 7: a record rule's versions are those of the current episode (decision 1), not "the latest Published version with from on or before the date" over all of them. Pairing is by the derived id, not by `sprk_basedonversion`.
- Decision 2 (a pinned version's rules apply before its own window) holds only once the Procedure is applicable (decision 4).
- The first fix brief's "highest-numbered Published version only" was replaced by the episode rule (A2).
- Round 2 supersedes two lines of the above: the not-applicable date is the earliest from over Active, Superseded and Retired-with-a-Published-policy-version versions (a Retired Draft that never had one opens nothing); and "a record rule whose Exclude row is missing still counts as a change" (Semantics 5, and the "no boundary: every version counts" case) is now a `ProcedureResolutionFaultException`.

### Fix-round gates and seeding proofs

| Run | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | 0 warnings, 0 errors |
| Unit `ProcedureResolver\|BillingRulePrecedence\|CoreRecordChain\|BillingRule` | 550/550 (the resolver class alone: 76, was 49) |
| ArchTests `CoreRecordChain\|Billing` | 102/102 |
| New tests against the old `ProcedureResolver.cs` (HEAD copied back, then restored byte-identical) | 15 to 16 of 76 failed: K6 x2, K2 x2, K1 x2, retiring the first / only version x3, the removal then re-change test, the pin before the Procedure's first version x3, a version beside none x2, and the Follow / Pin agree case on one parent order (it depends on the random RowId order, so it fails in some runs). The other new tests are regressions on behaviour that already held (F3, an edit inside one episode, a Superseded / Retired mix, retiring the Procedure, Follow vs Pin conflict, the pin's earliest policy version). |
| Seeding proof F3: firm check skipped when the Procedure has rows on the chain (`&& !procedureRows[procedure.Id].Any()` at the firm check) | 2 failed: the new "All-records naming F, also assigned" test and the existing manual-assignment test. Restored, `cmp` identical. |
| Seeding proof F3b: firm check skipped for an All-records source | 2 failed: both new F3 tests. Restored. (The brief's mutation only reaches a Procedure with rows on the chain, so the no-assignment case needed this second one.) |
| Seeding proof F1 #1: the Retired from-date term removed from `OpenedAWindow` | 3 failed: first version retired, only version retired, only version retired with an end. Restored. |
| Seeding proof F1 #2: the latest of ALL record-rule versions (no episode filter) | 1 failed: the removal then re-change test. Restored. |
| Seeding proof A2: only the highest-numbered version of the episode counts (the rule the correction replaced) | 1 failed: the edit-inside-one-episode test. Restored. |
| Publish size | Not re-measured: no package or project change, and the fix adds about 170 lines of resolver code against the +0.13 MB measured for 082b. |

## 082b re-check and fix round 2 (2026-11-04)

Branch `task/ebilling-082b`, from `35eb5310ec`. Nothing was written to Dataverse; no live call. Inputs: ontology-r1 D-203 (8)-(10) (binding) and the re-check items K2-K6.

### D-203 (8)-(10), as received

- **(8) Episode boundary on `createdon`: accepted.** (a) compare with `>=` (kept as is); (b) a same-second test: a version written in the Exclude row's own transaction counts when the two stamps are equal; (c) a source-scan guard, with must-fire and must-not-fire controls: no writer sets `overriddencreatedon` on `sprk_policyversion` or `sprk_procedureassignment`. Known limit: removing and re-making the same change within one second. A sturdier marker (a lookup from the Exclude row to the first version) is a schema change and is NOT in R1.
- **(9) A never-Active Draft that is retired.** Preferred fix, no schema: a Retired Procedure Version counts toward the "not applicable before" date only if it has at least one Published policy version OR a published rate card. Check that 081's publish reliably sets `sprk_publishstate` = Published. Must-fire: retire an Active version, gap. Must-not-fire: retire a never-published Draft, not applicable, no gap. If any path leaves an Active version with no Published policy version and no published card, keep the fail-closed known limit and report the path.
- **(10) Model 7.1 wording:** "at least one Active version" becomes Active, Superseded or Retired (D-203 (5a)), and (9) for the Draft outcome. We own the file; the commit goes to ontology.
- Follow vs Pin (a version vs none gives `ConflictingParents`) and Pinned = any deciding row pins: confirmed.

### What changed

| Item | What changed | Tests |
|---|---|---|
| **F1 / D-203 (9): a discarded dated Draft opened a window** | `ProcedureVersionRow.OpenedAWindow` is now a method over the set of Procedure Version ids that have a Published policy version (built once from the rows already read: no new read; the same test as `ProcedureService.ScopeLockedAsync`). Active or Superseded with an effective-from still count; a **Retired** version counts only when its id is in that set. It feeds `NotApplicableOn` (the F2-d date) and the gap-kind test in `ByDate` (a Retired window is a hole only if the version was in force). `NotApplicableOn` and `ByDate` became instance methods to reach the set. | must-fire: retire an Active version that had Published rules, a gap. Must-not-fire: a never-published dated Draft retired, not applicable on every date, no gap. A Draft v2 dated before Active v1 (publish refused on overlap) then retired: no gap on a date before v1; control: the same version with a Published rule is a gap. Gap kind: a Retired window is `HoleBetweenVersions` with a Published rule under it and `AfterLastVersion` without. The five existing Retired tests got a Published policy version under the Retired version, so they keep meaning "a version that was in force". |
| **D-203 (8b): same second** | Test only. `>=` was already the comparison. | A version stamped equal to its Exclude row's `createdon` counts; a version a second older (a removed change's) does not. Every `ChangeRule` fixture writes the row and the first version with one stamp, so eight existing record-rule tests also pin the equal case. |
| **D-203 (8c): `overriddencreatedon`** | New ArchTest `OverriddenCreatedOnWriteGuardTests`. | See the guard below. |
| **Re-check K2: an episode with no version** | `RecordRuleVersionOn`: when the Exclude row has a `createdon` and the record rule has Published versions but none was created on or after it (or none has a `createdon`), it throws `ProcedureResolutionFaultException` instead of falling back to the base rule. A date before the episode's first effective-from is NOT this case (the episode has a version; the base runs). | all-older and no-`createdon` versions fault; a date before the episode's first effective-from does not fault and gives the base rule. |
| **Re-check K3: an enabled record rule with no paired Exclude row** | `RuleOverridesOf` throws `ProcedureResolutionFaultException` (it used to count every version). The row is looked up on the same record and firm as the set. The `RecordRuleVersionOn` parameter is no longer nullable. | no Exclude row at all, and only a plain (all-firms) Exclude beside a Firm-record rule for F: both fault. |

**The guard (D-203 (8c)).** `tests/Spaarke.ArchTests/OverriddenCreatedOnWriteGuardTests.cs` scans every `.cs` file under `src/server` (comments removed, any casing) for the column name. It is wider than the two tables: any mention fails, except the one baseline entry, `RecordTableRules.cs` (`CommonServerOwnedColumns`, the list of columns a caller payload may NOT set), which a second test proves sits in that statement. A ratchet: a new file or a second mention in the baseline file fails; a baseline entry with no mention fails, so it is removed. Controls: planted writes in the real `RuleSetService.cs` and `ProcedureAssignmentService.cs` fire (four lines: quoted name, another casing, the assignment table, a property); a comment and the two real writer files do not. Not covered: a name built at run time; a `//` inside a string before the name; client TypeScript and scripts (no hit today).

### 081 publish-state finding (D-203 (9) check)

- **The only path to Active** is `ProcedureService.PublishVersionAsync`. In ONE transaction it sets the version Active, the predecessor Superseded, and (`RuleSetService.PlanProcedureVersionPublishAsync`) marks every Accepted Draft policy version under the version `sprk_publishstate` = Published with `sprk_inforcefrom`; a Proposed rule refuses the whole publish, a Rejected one stays Draft. `ChangeDatesAsync` and `RetireVersionAsync` never set Active. No script or seed sets a version Active (the `scripts/ebilling` files are schema deploys). So an Active version that had at least one Accepted rule always has a Published policy version.
- **Path found with none: a version published with NO Accepted rule** (an empty rule set, or every rule rejected). It becomes Active with no Published policy version and no card. If it is later retired, the resolver reads it as never opened: no hold for its window. This is the same limit `ScopeLockedAsync` already documents. It cannot be told from a discarded Draft without a schema marker, so it is **kept as a known limit and named here**. Its effect is narrow: the version judged nothing, so no rule is skipped; what is lost is only the "No procedure associated" hold for work in that window. **For ontology-r1 to confirm: this path reads as fail-open on the hold (not fail-closed), because it cannot be separated from the discarded Draft that D-203 (9) wants not applicable.** If ontology wants it held, the fix is a marker (for example a 081 publish that refuses a version with no Accepted rule), not a resolver test.
- **Policies read are enabled and Active only.** A Published policy version under a retired version is seen only while its policy is enabled. The only writers of `sprk_enabled` = No on a Billing policy are a Procedure retire (the Procedure is then not read at all), a record-rule removal (a record rule has no Procedure Version), and a new Draft rule (Off until its publish turns it On). So no individual switch-off hides a retired version's rules.
- **Pre-081 data** (policy versions with no `sprk_publishstate`) are never loaded, by any resolver test. Rollout note, with K5 below.

### Rate cards

The resolver reads no rate card. The 080 schema has `sprk_ratecard.sprk_procedureversion`, but no BFF code reads or writes it yet (`RateCardImportService` and `RateCardWorkbook` do not name `sprk_procedureversion`; 085 owns that). So "or a published rate card" has nothing to read today, and no new read was added. **Task 085 must extend `ProcedureVersionRow.OpenedAWindow` (and `ProcedureService.ScopeLockedAsync`) with a published-card test when it keys the card to the Procedure Version**, with a must-fire (a Retired version with only a published card is a gap) and a must-not-fire (no card, no rule: not applicable). The xmldoc on `OpenedAWindow` says so.

### Decisions in this round (confirm or correct)

1. **A pin to a retired, never-published Draft is still `PinnedVersionRetired`.** `Pinned()` is unchanged: a pin names a version the user chose, and an unusable one is a gap whatever its history. The F2-d test still runs first, so before the Procedure's first opened version it is not applicable.
2. **K2 does not fire for a record rule with no Published version at all** (a Draft-only rule): the brief says "has versions but none in the episode". Such a rule falls to the base rule as before. The BFF writes the rule's first version Published, so the state needs a hand edit. Not changed; noted for review.
3. **The guard is wider than the brief's two tables** (any mention of the column under `src/server`), because the baseline has one known, harmless mention and a wider scan needs no judgment about "near".
4. **`RecordRuleSlot.Exclude` and `ExcludedRule.Overrides` stay nullable / possibly empty in their types** although a record rule now always has its row; changing the public record shapes is not part of this round.

### Known limits (round 2)

- **Version published with no rule, then retired:** above. Same limit as `ScopeLockedAsync`.
- **Removing and re-making the same change within one second** (D-203 (8) known limit): see the corrected `createdon` tie wording above (the change, its removal and the re-change all in one stored second). A lookup from the Exclude row to its first version is a schema change and is not in R1.
- **Re-check K5, `RequireBasedOn` on pre-081 data.** `RequireBasedOn` faults on a record rule whose Published version has no `sprk_basedonversion` (or names another rule's version). A record rule written before 081 / 089 has none. This is a **data-migration note**: before an environment that holds older record rules is judged by the resolver (082c), check them, and backfill the link or retire the rule. Dev has no such rows known (not queried here).

### Criteria carried forward

**082c / 082d**
- **Re-check K6, `ToString` of the remaining records.** `ExcludedProcedure`, `ExcludedRule`, `ResolvedRule`, `ResolvedPolicyVersion` and `CoreRecordLink` still print ids (Procedure, policy, version and record ids). Beside the rule that `ex.Message` is never shown to a reader, no code in 082c or 082d may log, format or serialise one of these with `ToString()` or an interpolated string into a reader-facing surface. **`CoreRecordLink` and `ProcedureResolution.Chain` matter most**: they print the chain's record ids, and the chain may include a Secure parent. Preferred fix when 082c touches them: a `PrintMembers` that prints names, numbers and distance only, as the four reviewed records do, with the same kind of test.
- **K5 migration check** above, in the 082c rollout checklist.

**Task 117 (core-record delete)**
- **The delete planner switches the record rule Off in the same transaction that removes the assignment rows** (the change's Exclude row). The resolver now faults on an enabled record rule with no Exclude row (re-check K3). If the rows go first and the rule is switched Off later, any invoice on that core record is unresolvable (the job retries) until the no-plugin sweep retires the rule. Needs a test: after the planner runs, no enabled record rule is left on the record without its Exclude row.

**Task 085 (rate card per Procedure Version)**
- Extend `ProcedureVersionRow.OpenedAWindow` and `ProcedureService.ScopeLockedAsync` with a published-card test (see "Rate cards").

### Round 2 gates and seeding proofs

| Run | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | 0 warnings, 0 errors |
| Unit `ProcedureResolver\|BillingRulePrecedence\|CoreRecordChain` (filtered) | 133/133 (the resolver class alone: 88, was 76: 12 new cases in 8 methods) |
| ArchTests `CoreRecordChain\|Billing\|OverriddenCreatedOn` (filtered) | 109/109 (the new guard: 7 cases in 4 methods) |
| New tests against the HEAD `ProcedureResolver.cs` (copied back, then restored; `cmp` identical) | 7 of 88 failed: K2 x2, K3 x2, and three of the F1 tests (the never-published Draft retired, the Draft refused at publish then retired, the gap kind without a Published rule). The other new cases are controls or regressions that held before (the must-fire retire, the control with a Published rule, the gap kind with one, the same-second test, the K2 date-before-first-from control). |
| Seeding proof F1: the published-policy condition dropped from `OpenedAWindow` (`\|\| Status == Retired` alone) | 3 of 88 failed: the must-not-fire test, the Draft-refused-then-retired test, and the gap kind without a rule. Restored, `cmp` identical. |
| Seeding proof 8b: `created >= episodeStart` changed to `>` | 10 of 88 failed: the new same-second test, the new K2 date-before-first-from control, and eight existing record-rule tests whose fixtures write the row and the version with one stamp. Restored, `cmp` identical. |
| Seeding proof 8c: `e["overriddencreatedon"] = DateTime.UtcNow` appended to the real `ProcedureAssignmentService.cs` | the source-scan test failed with `ProcedureAssignmentService.cs: 1 mention(s) of overriddencreatedon (line 1523), 0 allowed` (and the must-not-fire control, which scans the real writers, failed with it). Restored, `cmp` identical, `git diff` clean. |
| Publish size | Not re-measured: no package or project change; about 40 net lines of resolver code. |

### Round 2 scoped re-check (2026-11-04)

No F-class finding. All six round-2 claims hold in the code; the filtered unit tests (133) and the ArchTests (7) pass. 082b merged into work at 7061b5b7ec.

- **K (new, transient): an evaluation that overlaps a create or re-change.** `ProcedureResolver.cs:756-763` reads the overrides first and the enabled policies and versions later, all with NoLock. An evaluation that lands between those reads can see the record rule enabled but no Exclude row (K3 throws), or the row and the enabled rule but no new version (K2 throws). The fault is thrown and the job retries, so it fails closed and clears on the next read. No fix. **082c criterion:** expect an occasional transient `ProcedureResolutionFaultException` on a record being changed; the retry path must not raise a hold or reach a reader for it before retries are exhausted.
- **K (documented): guard limits.** The `overriddencreatedon` guard strips `//` without regard to string literals and does not strip `/* */`. The second only gives false failures (the safe direction).
- **Task 117 ordering** (already a criterion): if the delete planner removes the Exclude rows before switching the rule Off, the K3 fault fires.

**ontology-r1 on the round-2 result (2026-11-04).** The 9854ccee25 wording is right on all three points.
- **D-203 (11) confirmed as a known limit:** a version published with no Accepted rule, then retired, raises no hold for its window. ontology does not ask for the 081 refusal (refusing an empty version would change product behaviour: an every-rule-rejected or documents-only version may be published on purpose; that is the owner's call). If the owner later wants it held, the 081 refusal is the place.
- **Transient fault accepted, with a binding 082c criterion:** no hold and no reader warning while retries remain; once retries are exhausted the submission ends **held**, with the blocking internal finding naming the cause, never passed. Tests for both halves: a retry clears with no hold; exhausted retries end held.

## 082c-1 implementation (2026-11-04)

Brief `notes/082c-brief.md` section 5 item 1, under the binding answers of section 7 (uac-r2; ontology D-205). Branch `task/ebilling-082c-1` from `work/ebilling-r1` at 6824af3bef. Criteria C1-C10, C12, C17, the C6 comment, the C20 read-only items.

### What changed

- **Loader (C1-C4, C10).** `LoadAsync` reads the legacy sets and calls `ProcedureResolver.ReadAsync` once, beside them. `LoadRulesAsync` is renamed `LoadLegacyRulesAsync`, and its `sprk_procedure IS NULL` comment is rewritten as the D-205 item 1 partition, which goes away with the legacy read at 075's copy-forward. `RequireDisjoint` throws `ProcedureResolutionFaultException` when a policy version is in both streams. The Duplicate gate looks at both streams, through `ProcedureResolution.PolicyVersions` (every Published version the resolution read). `BillingLoadedFacts.Procedures` carries the resolution. `Rules` holds the legacy stream only; the evaluator throws if it finds a Procedure candidate there.
- **Candidates (C5).** `BillingRuleCandidate` gains `Source` (Legacy or Procedure), `Rank`, `ProcedureId` and `ProcedureVersionId`.
  - Legacy candidates use `LegacyRank(scope)`: ClientDefault is (AllRecords, 0, false), Firm is (Firm, 0, true).
  - Procedure candidates come from `ProcedureCandidate(ResolvedRule)`. Their rank is that of the `AppliedProcedure` whose slot they take; a record rule takes its base rule's.
- **Evaluator (C5-C9).** Legacy versions are selected per date, as before. Procedure rules come from `RulesOn(d)`, memoized per distinct subject date (the line date, else the invoice date, else `ReceivedOn`).
  - **Precedence** is decided per subject and per `PrecedenceKey`, by `PrecedenceOf` and rank, for both streams:
    - **Replace** keeps only the top valid rank. Valid means gate-accepted, handler-accepted and not downgraded. Ties all apply. A displaced Replace version is never run.
    - **Additive** keeps every version.
    - **Split** (ExpenseRule) keeps a displaced version but drops its `ExpenseAmountOverCap` findings. The C6 comment sits at that line.
  - **C7:** `PrecedenceOf` is called only on gate-accepted definitions.
  - **C8:** a refusal counts as covered only where a version of the same Replace key judged the subject at an equal or higher rank.
  - `BillingEvaluationResult.Gaps` exposes each gap, with the subjects dated on it.
- **Interim fail-closed rule.** `InvoiceSubmissionProcessor` throws `ProcedureGapNotYetHeldException` when the evaluation has any gap. The throw sits at one marked spot (`// 082c-1 interim: replaced by the gap hold in 082c-2.`). Resolver faults are not caught; they propagate. The job retries, and on the final delivery the submission ends Failed (`processing_failed`), never Accepted. The persister is not touched.
- **Adjustment caller.** The `LoadRules` delegate is now `(firmId, CoreRecordReference? core, DateOnly date, ct)`.
  - `BillingFactLoader.LoadRulesOnAsync` returns one legacy version per policy (`SelectVersion`), plus every Procedure rule in force on the date, with no precedence.
  - The core record is the invoice's single lookup (`SingleCoreOf`). An invoice with none or several gets Unavailable.
  - A Procedure rule's DTO `Scope` is `"Procedure"`.
- **C12 (correction interim).** `PatchFindingsAsync` throws when the after-side has a gap, keyed `(ProcedureId, WorkDate, Kind)`, that the before-side lacks. The findings step then fails, the correction is Incomplete and nothing is patched.
- **C17.** `PrintMembers` on `ExcludedProcedure`, `ExcludedRule`, `ResolvedRule`, `ResolvedPolicyVersion` and `CoreRecordLink`. None of them prints an id, a rule body, free text or a reason.
- **C20.** `scripts/ebilling/Get-EbillingLegacyRuleOverlap.ps1` makes GET requests only, through a single `Invoke-DvGet`. The checklist types are pinned in the script because they cannot be derived from PowerShell; `BillingRulePrecedenceTests` fails if that line drifts from `PrecedenceOf`.

### Decisions and deviations

- `BillingRuleCandidate.Scope` is now nullable: it is null on a Procedure rule. `ReadSystemPolicyAsync` still sets ClientDefault (untouched).
- `Rank` defaults to the legacy ClientDefault rank, so a candidate built elsewhere without a rank ranks lowest.
- A Procedure candidate's `ProcedureVersionId` is the policy version's own link (null on a record rule's version). So one version maps to one candidate, whatever the date; the evaluator faults if one version ever maps to two.
- C4 is checked twice: by the loader over the whole read, and by the evaluator over what was resolved.
- **Expected outcome changes (C8; D-205 items 2 and 3; not escalated):**
  1. A refused firm (higher-rank) Replace version is no longer covered by a judged client default, so its subjects stay `rules.not_checked`. Two F2 tests and one persister test were reworked, and the reverse case is the new must-not-fire test.
  2. Under Split, a displaced client-default ExpenseRule keeps its code check, with its own consequence (which may be Reject). Two D-39 / 073 tests were reworked.
  3. Legacy Additive types now add (D-205 item 2): a client-default RequiredData or NonBillableActivity rule applies beside the firm's.
- **Split, literal reading (for ontology):** a valid higher-ranked ExpenseRule with no cap still displaces a lower version's cap, as D-39 did for the whole version. Ontology should say whether a cap-less higher version ought to leave the lower cap in force.
- **AiObservation** is Additive in the catalogue, although D-205's list omits it. The script counts it on its own line.
- The adjustment path now filters `IsCitableType` after selection; before, it filtered before `SelectVersion`. The two differ only where two versions of one policy overlap.
- `ProcedureResolution.DatesResolved` was added for the C9 test. It is a counter and decides nothing.

### Dev counts (GET only, https://spaarkedev1.crm.dynamics.com, as ralph.schroeder@spaarke.com)

| Item | Count |
|---|---|
| `sprk_ruleset` rows (so legacy sets, Procedure sets and record sets) | 0 |
| `sprk_procedure` | 0 |
| Billing `sprk_policy` | 1 (the system policy, no rule set) |
| `sprk_policyversion` | 10 |
| Overlapping (firm, checklist type) pairs | **0** |
| Firms affected | 0 |
| K5: record rules whose Published version lacks `sprk_basedonversion` | **0** |

Dev has no rule sets to migrate, so the census is only meaningful on an environment with authored sets.

### Gates

| Gate | Result |
|---|---|
| `dotnet build` (BFF and test project) | 0 warnings, 0 errors |
| Filtered unit (`ProcedureResolver\|BillingFactLoader\|InvoiceComplianceEvaluator\|BillingRule\|InvoiceAdjustment\|InvoiceCorrection\|InvoiceSubmissionProcess\|BillingSystemFindings\|CoreRecordChain\|BillingEvaluat`) | 1516/1516 |
| Namespaces `.Signals.Billing` + `.Finance` + the processing flow, once | 7203 passed, 3 skipped, 0 failed (of 7206) |
| ArchTests (`Billing\|CoreRecordChain\|Procedure\|AccessTableReader\|ADR010\|FindingsJson\|RecordOwnerAssignmentCensus\|Finance\|ExceptionMessage`) | 226/226 |
| Before the test updates | 5 existing tests failed under the new precedence (4 evaluator tests, 1 persister F2 test). All are the C8 / C6 outcome changes above; reworked |
| Seeding: resolver call replaced by an empty resolution | 5 of 53 loader tests failed, including C2 end-to-end (also C3-second, C4, C9, C10). Restored, `cmp` identical |
| Seeding: partition removed (query condition and re-check) | 7 of 53 failed, including the C3 second must-fire (also C2, C3, C4, C9, C10, adjustment). Restored, `cmp` identical |
| Seeding: gap allowed through (`Gaps.Count < 0`) | 2 of 79 failed: the processor interim test and the end-to-end flow test. Restored, `cmp` identical |
| Extra seeding: C12 check disabled | 1 of 15 failed: the C12 test. Restored, `cmp` identical |
| Extra seeding: precedence forced to Replace (D-39 all-replace) | 7 Additive / Split / C8 tests failed. Restored, `cmp` identical |
| Extra seeding: `RulesOn` per subject instead of the memo | the C9 test failed. Restored, `cmp` identical |
| Publish size (`dotnet publish -c Release`; base 6824af3bef archived and published the same way) | total 137,499,800 -> 137,507,420 bytes (+7,620); `Sprk.Bff.Api.dll` 25,276,928 -> 25,293,824 (+16,896). No package change |

### For 082c-2

- The interim throw goes when the gap hold lands. It is one spot in the processor, and `ProcedureGapNotYetHeldException` is deleted with it.
- `BillingEvaluationResult.Gaps` carries `AppliesToInvoice` and `LineIndexes` for each gap, for the C11 `rules.not_checked` lines.
- The C12 interim in the correction stays until 082d / 101.

## 082c-1 adversarial pass and fix round 1

Fixes follow the adversarial review of 082c-1 and ontology D-205. One commit per fix; the C8 tie and K4 each have their own commit so either can be reverted alone.

| Finding | Fix |
|---|---|
| F1: a capless higher-ranked ExpenseRule displaced a lower version's cap | Under Split, `top` is taken from the valid versions that state a cap (`MaxAmountPerItem` non-null). Only those can put a lower version into `capDisplaced`; the code check is never displaced. This settles the "Split, literal reading" question above (ontology D-205 ruling). A deliberate "no cap on this record" is a rule-level Exclude, or a record rule in the base's slot, never precedence. Two tests that pinned the defect now use a capped higher version; new tests: capless higher version leaves the lower cap firing (must-fire), capped higher version displaces, capless at a tie applies beside it |
| Cosmetic: a displaced cap was still listed as skipped in the currency "not checked" finding | A cap displaced on every subject it applies to is left out of the skipped list (test added) |
| F2: C12 compared gaps as `(ProcedureId, WorkDate, Kind)`, so a line moved onto a date that already had another subject's gap brought in no new key | Compared per subject: `(ProcedureId, Kind, subject)` from `AppliesToInvoice` and `LineIndexes`; any after-side triple not in the before set refuses the correction. Must-fire: the fee line is moved onto the received date, where the undated expense line already holds a gap. Must-not-fire neighbour: an Hours correction applies |
| F1-low: a C12 refusal surfaced as the generic 503 Incomplete ("Repeat the same request"), which never clears a gap | New `CorrectionProcedureGapException`, mapped to `CorrectionStatus.ProcedureGap` and a fixed-text 409 `ebilling.correction.procedure_gap` (same pattern as `CorrectionFirmMissingException`). No id, no `ex.Message`. Test covers the status, code, fixed detail and the HTTP mapping |
| C8 tie (confirmed by ontology): a refused version was covered by a tie-mate | `RefusalScopeOf` covers a refused version's subject only for a Replace rule of strictly HIGHER rank (`> 0`, was `>= 0`). Tie test: Firm-level TimeIncrement 0.1 and 0.5, the 0.5 refused, a 0.3 line stays `rules.not_checked` (own commit) |
| K2: a record rule's code prints a policy id | `PolicyCode` of a record rule is `BRR-` + the policy id. `ResolvedPolicyVersion`, `ResolvedRule` and `ExcludedRule` `PrintMembers` print the token `record rule` for such a code; C17 test extended |
| K4: the invoice date after the last version, in a hole, or before the first, held the whole invoice | **K4 resolved by ontology rule (c).** The invoice subject keeps the invoice date for a Procedure's version whenever that Procedure has one on it. A Procedure with none on the invoice date is judged, for the invoice subject only, by its version on the LATEST dated line's work date. The invoice-date gap stands only when that date has none either, or when no line is dated. Legacy selection and line subjects are unchanged; no new `RulesOn` call (C9's one resolution and memo per date hold, tested). One existing gap test changed: the invoice no longer holds the gap when the dated lines are inside the window (own commit) |

### Open items

- **K1:** the interim throw retries the full pipeline. Acceptable because 082c-1..3 and 114 ship together.
- **K4:** resolved by rule (c) above; nothing open with ontology.
- **C20 census and the pair create test** belong to 082c-2.
- **ForReaders and record rules (reported, not changed):** `ProcedureRulesOnDate.ForReaders()` builds `ProcedureReaderRule.PolicyCode` from `r.Version.PolicyCode`. For a record rule (`RecordRule is not null`) that is the `BRR-` code, which carries the record rule's policy id. The reader contract is uac-r2's; the decision on whether to blank or replace the code for record rules is theirs. The excluded-rule list is not affected (it carries the base rule's code).

## 082c-1 scoped re-checks and fix round 2

Two scoped re-checks of the round-1 fix range, then one fix round.

- **Ontology: PASS on the fix range.** One point, binding (item 1 below): the C12 correction message said "cannot be applied", but the value IS saved and only the rules were not re-run.
- **Our re-check: no F-class finding.** One F4 (item 1, the same message point) and four K items (K1, K2, K4 depth, below), all fixed here. K3 is recorded only.

| Item | Fix |
|---|---|
| F4 (ontology, binding): the C12 409 `ebilling.correction.procedure_gap` told the reviewer the correction "cannot be applied" | `CorrectionCodes.ProcedureGapDetail` is now exactly: "Saved, but its billing terms were not re-checked because the new dates have no terms in force; the invoice stays on hold until terms are set up or the dates are corrected." Status 409 and the code are unchanged. The log line says the same ("saved ... its rules were not re-run; the correction stays unapplied and holds approval"), and the status is the same outcome: the value is written, the follow-through is not, so `ReadUnappliedAsync` still lists the correction and approval answers 409 `corrections_pending`. Test (end to end over the approval world): after the outcome the date is stored, the stored findings are unchanged, and `ApproveAs` is refused with `corrections_pending` and writes nothing |
| K1: the adjustment options used `RulesOn(invoiceDate)` only, so a rule that judged the invoice subject through the rule (c) fallback could not be cited (a cited rule must be in the options) | `BillingFactLoader.RulesInForceOn` / `LoadRulesOnAsync` and the `InvoiceAdjustmentService.LoadRules` seam take the latest dated line's work date. The service reads it from the invoice's Fee and Expense lines' stored `sprk_eventdate` (a correction already writes that column; an Adjustment line is never counted). Legacy selection is unchanged. Tests: the fallback rule is in the options and citable; no dated line gives none; the latest of several dates is used and an Adjustment line's date is not |
| K2: the fallback keyed on "absent from `Procedures`", so it also lifted a `ConflictingParents` (pin vs follow) gap on the invoice date | **One shared definition**, `ProcedureRulesOnDate.FallbackProcedureIds` (+ `RulesWithFallback`), used by the evaluator and by the adjustment options (the loader). It lifts a Procedure with a hole, an end, or one not yet applicable on the invoice date, and only when it is applied on the work date. `ConflictingParents` and the pin gaps (`PinnedVersionRetired`, `PinnedVersionNotInForce`) stand. Tests: a ConflictingParents gap on the invoice date is not lifted (must-fire, evaluator end to end with a Follow and a Pin of version 1); a theory over all five gap kinds; a not-yet-applicable Procedure is lifted; the holes and ends tests of round 1 still hold |
| K4 depth (code was correct by trace) | Tests: with several Procedures only the one without an invoice-date version falls back (the other keeps the invoice date's version 2, not the line's version 1); a fallback date that is itself in a hole leaves the gap standing (both the line's and the invoice's); a refused capped higher version under Split leaves the lower cap firing |

Test support: `ProcedureKit.Pin(name, versionNumber)` adds a Pin on the kit's Matter beside the Follow row.

### Known limits (recorded, not changed)

- **K3:** a line moved between two gap kinds of the same Procedure (for example a hole to an end) is refused by C12 although it is unjudged on both sides, because the per-subject triple includes the kind. Fixing it needs a stored gap to compare against (082d / 101). It is rare.
- **K (ontology D-205, accepted):** a Follow-vs-Pin disagreement that exists only on the invoice date, while the parents agree on every work date, still holds the invoice (`ConflictingParents` is not lifted by the rule (c) fallback). The disagreement is real and will hold the next invoice too; a person resolves it with a distance-0 assignment or an Override.

### Verification

| Check | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | 0 errors, 0 warnings |
| Filtered unit (`ProcedureResolver\|BillingFactLoader\|InvoiceComplianceEvaluator\|BillingRule\|InvoiceAdjustment\|InvoiceCorrection\|InvoiceSubmissionProcessor\|InvoiceApproval\|BillingEvaluat\|CoreRecordChain`) | 1476/1476 |
| Contract tests of the adjustment endpoints (compiled into the same project) | 58/58 |
| ArchTests (`Billing\|CoreRecordChain\|Procedure\|ADR010\|FindingsJson\|Finance\|ExceptionMessage`) | 162/162 |
| Seeding (old behaviour restored in the three production files, then put back, `cp` from backups) | 8 of 518 failed: the K1 must-fire and the latest-date test, the K2 must-fire and its three pin / conflict theory rows, the item 1 end-to-end test and the C12 text assertion |

## 082c-2 implementation (2026-11-05)

Branch `task/ebilling-082c-2` from `work/ebilling-r1` at 871751240a. Contract: `notes/082c-brief.md` (section 5 item 2; C11, C13 to C16, C18; section 3(c) with section 7 binding).

### What changed

| Part | Where |
|---|---|
| **The gap hold (C11, C13).** One pipeline finding per gapped Procedure, at its first date. It carries the name, the date and the kind, and no id. The blocking BILL-SYSTEM-SUBMISSION finding "No procedure associated with this invoice" goes on the SubmissionIntegrity Signal, one Signal with firm_missing; the note order is firm, then unresolved, then gap. Gapped lines are `rules.not_checked` with cause `procedure_gap`. Classify gives Internal Blocked, and SPA Accepted or AcceptedUncoded with `procedure_gap`, never AutoRejected | `InvoiceSubmissionProcessor` (`Classify`, `HoldFor`, `ProcedureGapFindings`), `InvoiceSubmissionPersister` (`TerminalFor`, `RulesNotChecked`, `RulesNotCheckedCause`, `Validate`, `SignalPlanJson`), `InvoiceSubmissionAfterSteps`, `BillingSystemFindings`, `BillingRuleCatalogue`, `BillingFindingSignalMapper` |
| **Faults (C14, C15).** The job handler threads `finalDelivery` through as `job.IsFinalDelivery`. On an earlier delivery a resolver fault propagates and nothing is persisted. On the final delivery the invoice is held: Internal Blocked, SPA Accepted with `procedures_unresolved`. The hold has a blocking finding that names the cause (`chain_unreadable` / `rows_inconsistent`), no rule findings, and every line `rules.not_checked`. A fault that is not a resolver fault still propagates | `InvoiceSubmissionProcessor.EvaluateAsync` (`UnresolvedCauseOf`), `IInvoiceSubmissionProcessor`, `InvoiceSubmissionProcessingJobHandler` |
| **Interim removed** | `ProcedureGapNotYetHeldException` and its throw are deleted |
| **Approval.** Refused with `ebilling.approve.procedure_held` and a fixed detail. The check reads the stored submission state, not the system policy and not `sprk_reviewdetail` | `InvoiceProcedureHold` (new), `InvoiceApprovalService` |
| **Codes and status view.** New codes `ebilling.submission.procedure_gap` and `ebilling.submission.procedures_unresolved`; the finding codes are flag-only. `InvoiceSubmissionView.Warning {code, message}` comes from `sprk_outcomereasoncode`, uses the R4 owner-approved texts verbatim, and is omitted when null. No pipeline finding and no Procedure name reaches the view (C18) | `SubmissionReasonCodes`, `BillingFinding`, `SubmissionHoldWarning`, `InvoiceSubmissionService.ToView` |
| **Redaction (C16).** `ForReaders()` uses option (a): a record rule reads as its base code plus the existing `RecordRule` flag. The code is blank, with a warning log that carries no id, when the base is missing or is itself `BRR-` | `ProcedureResolver.ReaderCodeOf` |
| **C16 guards.** `ReaderSentinel` flags `BRR-` codes, 32 hex characters and D-form GUIDs, with must-fire and must-not-fire controls. The ArchTest `ProcedureReaderRedactionGuardTests` covers 16 reader-surface types. It rejects `get_Message` on any `Exception`, and `ToString()` or AppendFormatted / Join / Format / Concat applied to a resolver record. The receiver of `object.ToString()` is inferred from the instruction before the call. It also has a control class | tests |
| **`sprk_invoice.sprk_reviewdetail`.** A local choice, derived by `InvoiceReviewDetail.ReviewDetailOf`. Its writers are named in `InvoiceReviewDetailWritersGuardTests`. The column is in `ReadableColumns` and `MappingProtectedColumns`, and not in `AccessColumns` | `InvoiceReviewDetail` (new), `InvoiceLifecycleService`, `InvoiceSubmissionPersister`, `InvoiceAssignFirmService` (one hunk in `ApplyPlan.Build`, one property, the plan init), `ExternalAccessModule`, `RecordTableRules`, `scripts/ebilling/Deploy-EbillingInvoiceReviewDetailColumn.ps1` (new) |
| **Census** (brief (b), D-205 item 4) | `scripts/ebilling/Get-EbillingLegacyRuleOverlap.ps1` adds `projectsLinkedToAMatter` and `linkedProjectsWithAnEmptyPair` |

### sprk_reviewdetail option values (pinned in `InvoiceReviewDetail` and in the script; for R4)

| Value | Label | From |
|---|---|---|
| (blank) | normal review | none of the three |
| 659490000 | Waiting for billing terms | `ebilling.submission.procedure_gap` |
| 659490001 | Billing terms could not be checked | `ebilling.submission.procedures_unresolved` |
| 659490002 | Waiting for the firm to be confirmed | `ebilling.submission.firm_missing` |

Priority: firm, then could not be checked, then waiting for terms, then blank. In Review is `statuscode` 1 / `statecode` 0.

Writers, by the ontology D-205 numbering (fix round 1 renumbered this list; the earlier text numbered them 1 to 4 in a different order):

1. The persister's held create (In Review only, in the persist transaction). **Built.**
2. The re-check that clears a gap or unresolved (082d / 101, ebilling-r2). Pending; no 082c-2 path clears one.
3. The 095d-1 Set firm apply, recomputed from the Procedure reasons the findings still name (`InvoiceAssignFirmService`, `ApplyPlan.Build`). **Built.**
4. The 095d-2 re-run. Pending.
5. The 114 Override release. Pending.
6. Every exit from In Review: `InvoiceLifecycleService.TransitionAsync` on every move, writing blank in the same write as the status. **Built.**
7. The correction's Superseded close, writing blank in the same request (`BuildConditionalTransition`), and, as of fix round 1, its undo: `SubmissionCommitGate.ReopenSupersededAsync` derives the detail from the old invoice's remaining holds and passes it to `BuildSupersedeUndoRequest`. **Built.**
8. The 089e chain close. **Not a writer.** C19 found that the close writes `sprk_signal` only; it never sets or clears an invoice's detail, so it is not on the pending list.

Pending writers, listed in the guard: 2, 4 and 5. Each recomputes the detail from the invoice's remaining reasons in its own transaction and is added to `InvoiceReviewDetailWritersGuardTests.Writers` with it.

### Decisions and deviations

- **Column missing: skip with a warning logged once; never fail closed.** `ReviewDetailColumnProbe` makes one app-only `TopCount = 1` read.
  - A present answer is cached for good.
  - A missing answer is re-checked after 5 minutes.
  - A probe that fails for any other reason answers present, so the write names the column and fails, rather than leaving a detail on a closed invoice.

  Failing closed would refuse every status move in order to protect a label. The holds are unaffected: the persister and Set firm tests run with the column missing.
- **The approval order is FR-20, then firm, then procedure.** An Internal gap (Blocked) is therefore refused `ebilling.approve.held` first, and `procedure_held` only after the 114 Override lifts FR-20.
- **SPA gap over a 073 rejection** (brief section 6 open item; no escalation needed). `BuildFindingsJson` keeps the deciding finding `rejects=true` under kind Accepted, reason `procedure_gap`. The Signal plan presents those groups as Blocked, never as Rejects.
- **An invoice-date-only gap** flags no line, but the invoice is still held by the pipeline finding and the reason.
- **The existing `RecordRule` flag is kept;** no `IsRecordRule` was added. It is the one boolean option (a) allows. ForReaders gaps keep the enum kind name, which carries no id.
- **Sentinel scope.** The sentinel scans the reader parts of the stored JSON (pipeline findings, line findings, the mapped system Signal's headline, sentence and facts) and the status-view warning. It does not scan internal id members (`policyId`, `invoiceId`). The unresolved test also checks that the whole stored JSON lacks the planted `ex.Message`.
- **Writer 2 (the re-check that clears a gap) does not exist in 082c-2.** No path rewrites the stored hold markers. The 101 correction re-evaluation patches findings but not `reasonCode`, `pipelineFindings` or the line causes, and its C12 interim refuses a correction that brings in a new gap. The guard lists it as pending.
- **The generic Update Record node and output mappings are not refused in 082c-2** (uac-r2's open question). Only field-mapping rules are refused, through `MappingProtectedColumns`. A stray node write cannot release a hold, because approval never reads the column, but partners could see wrong text until the next writer runs. Recommendation: add the column to the typed-writer-only list once ontology defines one. Ontology to confirm.
- **The SpaarkeMaster solution source does not carry the column yet.** A new stamp gets it from the script until the next export.
- **Census.** The create path is pinned by the client test `CreateProjectWizard.projectFiling.test.tsx` ("writes the N:N, THEN files the project under the matter through the BFF"). The count can still grow: a refused or secure-blocked filing keeps the link (with a visible warning), and a subgrid "Add existing" writes no pair. It is therefore a gate before each rollout, and task 195 (`ProjectsWithMatterLinkButNoPair`) reports the same count on every run.
- **122 guard.** `InvoiceProcedureHold` reads `sprk_findingsjson` app-only. It is added to the 122 reviewed readers (row 11 of notes/122) in its own commit: the guard failed at af003e5704 and is fixed at db63310c54.
- **Publish size.** The BFF's total publish size of 137.6 MB is pre-existing and far over the 55 / 60 MB thresholds. This slice adds +33 KB.

### Known limits

- ~~The Superseded undo reopens the old invoice with a blank detail~~ Fixed in fix round 1 (writer 7, the undo): the reopened invoice carries the detail its remaining holds derive.
- Invoices held before the column exists read blank. There is no backfill.
- If the column is deleted after it was found present, writes fail until a restart, because the present answer is cached for good.

### uac-r2: who can reach the unresolved causes

`rows_inconsistent` comes from an admin's Procedure changes. `chain_unreadable` comes from stored parent data. A partner writes neither, so a partner cannot trigger either hold.

### Dev reads (GET only, https://spaarkedev1.crm.dynamics.com)

| Read | Result |
|---|---|
| `Deploy-EbillingInvoiceReviewDetailColumn.ps1` dry run | `WOULD CREATE sprk_reviewdetail (Picklist)`; publish skipped; nothing written; exit 0 |
| `... -Verify` | `GAPS: sprk_invoice.sprk_reviewdetail does not exist`; exit 1, as expected. `-Apply` was not run |
| `Get-EbillingLegacyRuleOverlap.ps1` | `projectsLinkedToAMatter` 7; `linkedProjectsWithAnEmptyPair` **0**; every legacy and K5 count 0 |
| Intersect metadata | `sprk_project_sprk_matter`, EntitySetName `sprk_project_sprk_matterset` |

### Gates

| Gate | Result |
|---|---|
| `dotnet build`: BFF, unit tests, ArchTests | 0 warnings, 0 errors |
| Filtered unit runs during the work | 1470/1470; 572/572; 282/282 (allow-list and auth); 182/182 |
| The `.Signals.Billing` and `.Finance` namespaces, run once | 7245 passed, 3 skipped, 0 failed (of 7248) |
| ArchTests (`InvoiceReviewDetail\|ProcedureReader\|Invoice\|External\|FindingsJson\|Lifecycle\|Status\|FirmHold\|Procedure\|RecordOwner\|Bff`) | 481/481 |
| Publish size: `git archive` of 871751240a and of HEAD, each published with `dotnet publish -c Release -r linux-x64 --self-contained false` (192 files each) | 137,614,960 to 137,648,216 B (**+33,256**): `Sprk.Bff.Api.dll` +28,672, pdb +4,584. No package change |

### Seeding proofs

Each seed was restored from its backup, and the restored file checked `cmp`-identical.

| Seed | Result |
|---|---|
| 1. The gap hold dropped from `Classify` | 10 of 151 failed |
| 2. The final-delivery catch made to catch on every delivery (`finalDelivery &&` removed) | 3 of 103 failed (the C14 (1) theory, two rows, and the end-to-end test) |
| 3. `ex.Message` planted in the unresolved pipeline finding | Sentinel test: 1 of 10 failed. ArchTest: failed with `InvoiceSubmissionProcessor::EvaluateAsync: reads an exception's Message` |
| 4. A `BRR-` code planted in `ForReaders` | 1 of 92 failed (the uac-r2 (a) reader test) |
| 5. The exit blank disabled | 14 of 124 failed (all 7 exit paths, and 7 transition tests that pin the blank) |
| 6. The derivation priority swapped (unresolved before firm) | 4 of 48 failed |

### For the next slices

- **082c-3:**
  - Render `view.warning` in the R4 section 3 shape: one warning MessageBar in a polite region.
  - Add `warning?: { code, message } | null` to both client types.
  - Add the 202 pre-check.
- **114 (Override release):**
  - Rewrite the stored hold state, or mark it released.
  - Recompute the detail with `ReviewDetailOf(remaining reasons)` in the same transaction.
  - Add the release to `InvoiceReviewDetailWritersGuardTests.Writers`.
  - Expect, under the approval order, that an Internal gap answers `ebilling.approve.held` until 114 lifts FR-20.
  - C22 (`DecidingIndexes`) is unchanged here.
- **095d-2:**
  - The re-run rewrites the stored firm and Procedure state, and recomputes the detail in its transaction.
  - Add it to the guard.
  - Merge with 095d-1c, which edits `InvoiceAssignFirmService` on another branch. This slice's changes to that file are: the review-detail lines in `ApplyPlan.Build` after the firm-hold clear, the `WriteReviewDetail` plan property, and the probe read before the plan.
- **R4:**
  - Use the option values above.
  - In Review is `statuscode` 1 / `statecode` 0.
  - `sprk_reviewdetail` is readable on `sprk_invoice`.
  - Render the label only while the invoice is In Review (uac-r2 (b)).
  - The two warning texts are the approved ones. The firm explanation line still needs the owner's OK (brief section 7).

## 082c-2 fix round 1 (2026-11-05)

Branch `task/ebilling-082c-2`, after adversarial pass 1 of 2. Merged `origin/work/ebilling-r1` first, then one commit per fix. Main-checkout and `spaarke-wt-ebilling-r1` working trees untouched; no live Dataverse writes.

### The merge

`origin/work/ebilling-r1` (c61f510080: 095d-1c, the Set firm Decision Record, `sprk_findingsjson` in `ParentLineage` AccessColumns for `sprk_invoicesubmission`, the `RecordTableRules.MappingProtectedColumns` entry, `FindingsJsonAppOnlyReadGuardTests` with `RefusalOnlyTypes`, `FindingsJsonFieldSecurityAgreementTests`) merged into the branch with `git merge` (no rebase).

| File | Result |
|---|---|
| `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/InvoiceAssignFirmServiceTests.cs` | The only conflict: `World.Service()`. 095d-1c passes its `Log` (a `CapturingLogger`); 082c-2 sets `ReviewDetailProbe` in an object initialiser. Both kept: `Log, new FixedTime(Now)) { ReviewDetailProbe = ReviewDetailProbe }`, with both fields. |
| `ParentLineage.cs`, `RecordTableRules.cs`, `FindingsJsonAppOnlyReadGuardTests.cs`, `InvoiceAssignFirmService.cs`, the AccessTable census tests | Auto-merged; every entry of both sides is present (checked: `sprk_findingsjson` and `sprk_reviewdetail` in `MappingProtectedColumns`, the Set firm apply with its Decision Record and the detail line). |

Gate after the merge: BFF build 0 warnings and 0 errors; filtered unit 3218 passed, 0 failed (5 skipped: live seam tests); ArchTests filtered 20 passed.

### Fixes

| # | Class | What | Where |
|---|---|---|---|
| 1 | F4 | `sprk_reviewdetail` is refused for app-only generic writes. It is in `AccessColumns["sprk_invoice"]` with the reason "it decides no access; it says why the invoice is held for review, and only its typed writers derive it", and stays in `RecordTableRules.MappingProtectedColumns`. The writers guard lists the two new naming sites (`InheritedAccessPermission` `.cctor` and `RefuseAppOnlyWrite`) as non-writers. No read-guard change was needed: the column is not field-secured. | `ParentLineage.cs` (AccessColumns entry, reason branch, doc item); tests `AccessTableAppOnlyWriterTests` (census row, new must-fire for the output mapping and the Update Record node, must-not-fire controls), `AccessTableReaderScanTests` (reason), `InvoiceReviewDetailWritersGuardTests` |
| 2 | F3 | The C16 reader-redaction guard also flags an exception's `ToString()`, string interpolation of an exception or a resolver record (with a format specifier or an alignment too), the string operator `+` (the compiler lowers `"x" + ex` to a null-guarded `object.ToString`), `string.Format` / `Concat` / `Join` of one. New must-fire controls for each shape (13) and must-not-fire controls (6: `ex.GetType().Name` bare, concatenated, interpolated and formatted, a count). **No new real hit** in the 16 scanned reader-surface types (a source grep agrees: the only `ex.` use is `ex.GetType().Name` at `InvoiceAdjustmentService.cs:622`). | `ProcedureReaderRedactionGuardTests.cs` |
| 3 | F2 | The supersede undo reopens the old invoice with the detail its remaining holds derive, in the same request as the status. `ReopenSupersededAsync` reads `InvoiceProcedureHold.ReasonsAsync` and `InvoiceFirmHold.IsHeldAsync` of the old invoice, calls `ReviewDetailOf`, and passes the result to `BuildSupersedeUndoRequest(..., int? reviewDetail = null)`. A read fault propagates (the undo is held and retried, as for the reads before it). The writers guard cannot list the gate (it names no column literal), so a new guard test pins that `SubmissionCommitGate.ReopenSupersededAsync` calls `ReviewDetailOf`, with a must-fire and a must-not-fire control. | `InvoiceLifecycleService.cs` (interface, `BuildSupersedeUndoRequest`, `BuildConditionalTransition`), `SubmissionCommitGate.cs` (`ReopenSupersededAsync`), `InvoiceReviewDetail.cs` (doc), `ApExportWorld.cs` (the fake) |
| 4 | F3 | A no-firm Internal held create writes 659490002 on the invoice create (`PersistAsync` test; the same create has no `sprk_vendororg`). | `InvoiceSubmissionPersisterReviewDetailTests.cs` |
| 5 | F4 | This file: the writers list is renumbered by D-205 (see "What changed" above), and writer 8 (the 089e chain close) is recorded as not a writer. | here |
| 6 | K2 | `Deploy-EbillingInvoiceReviewDetailColumn.ps1 -Verify` also counts the reverse invariant: In Review invoices whose detail is blank while an open Procedure or firm hold exists, read the way the BFF reads them (the invoice's own `sprk_firmhold`; the stored submissions' outcome reason, `reasonCode`, `pipelineFindings[].reasonCode` and the `rules.not_checked` line causes; unparsable findings count as held, fail closed). It is a WARN, never a FAIL: an invoice held before the column existed reads blank until its next writer (no backfill), so only a count that grows after the 082c-2 build is deployed is a defect. Read-only GETs; it follows `@odata.nextLink` to 20,000 rows and says when it stopped at the cap. Parse-checked; the hold-reason helper was run offline over five stored shapes. Not run against Dev (no live call this round). | the script |
| 7 | K1 | See "K1 outcome" below. | `SecureRootInheritance.cs`, `CoreRecordChain.cs`, `InvoiceSubmissionProcessor.cs` |
| 8 | K4 (c) | See "K4 (b)/(c) outcome" below. | `ProcedureResolver.cs`, `InvoiceSubmissionProcessor.cs`, `InvoiceReviewReadService.cs` |

### K1 outcome (uac-r2 authorised the additive edit in `SecureRootInheritance.cs`)

`PairParentAnswer.Unknown` gains `Transient` (default false), set true on the two fault paths only: the `faultedTypes` repeat and the type read's catch. Every existing caller is unchanged (they match on the type and read `Reason`). `CoreRecordChain` passes it to a new `CoreRecordChainUnreadableException.Transient`; `InvoiceSubmissionProcessor.UnresolvedCauseOf` maps a transient chain to `rows_inconsistent` and every other chain to `chain_unreadable`, as before. `rows_inconsistent` is the only existing cause whose reader text says it may clear ("this may clear once a change in progress completes"); no reader text was added.

Not done: `SecureParentsAnswer.UnknownIsTransient`. The only consumer of the flag is `CoreRecordChain`, which calls `NamePairParentAsync` directly and never sees a `SecureParentsAnswer`; threading the flag through `NamedParents` and the climb touches five sites in a 4,000-line file for no caller. Add it when a caller needs it.

Test: `CoreRecordChainTests` pins that a pair type READ fault and a pair with no type both fail closed the same way (the same exception, the same message prefix) while `Transient` differs; `InvoiceSubmissionProcessorTests` pins the cause mapping (both ways). Seed: the flag removed from the two fault paths, one test fails (the chain pin).

Wording to raise with the owner: `rows_inconsistent` reads "the procedure settings are inconsistent" for what is an outage. It is the best of the two existing texts but not a good one. A dedicated transient cause with its own R4 text is the clean fix.

### K4 (b)/(c) outcome: contained part done, one point stopped

Every writer of resolver-derived content into the three surfaces, by trace:

| Surface | Writer | Resolver content | Through `ForReaders()`? |
|---|---|---|---|
| Stored findings `pipelineFindings`, then the system Signal and the status warning | `InvoiceSubmissionProcessor.ProcedureGapFindings` | the gapped Procedure's name, date, kind | **Now yes.** It reads `ProcedureGap.ForReaders()`, the one mapping that `ProcedureRulesOnDate.ForReaders()` also uses, instead of the internal record's members. |
| `pipelineFindings` (unresolved) | `InvoiceSubmissionProcessor.EvaluateAsync` / `UnresolvedCauseOf` | the cause code only | Nothing resolver-derived is written (a fixed code); `ex.Message` is guarded by C16. |
| Status view `warning` | `SubmissionHoldWarning.For(reasonCode)` | none (a code and fixed text) | Nothing resolver-derived is written. |
| `lineFindings` | `RulesNotChecked*` | the cause code | Same. |
| **`findings[].policyCode`, `signalPlan[].policyCode`, `signalPlanRefusals[].policyCode`, the Signal dedupe key** | `InvoiceSubmissionPersister.FindingEntryJson` (`:2254`), `SignalPlanEntryJson`, `BillingFindingSignalMapper` (`:231`, `:379`), from `BillingFactLoader.ProcedureCandidate` (`PolicyCode = version.PolicyCode`) | **a record rule's own `BRR-` code (and `policyId`)** | **No, and not changed.** |

The last row is the open point. A rule judged in force through a record rule carries `PolicyCode = BRR- + policy id` into the stored findings, the plan and the Signal. These three are the join keys: the plan is the source the Signal is written from (part D writes it on every run), `InvoiceReviewReadService.PlanFor` matches a finding to its plan entry by `(subject, policyCode)`, and `InvoiceCorrectionReevaluation` keys its plan patches the same way. Writing the base code there would make the Signal reference the base rule (a different policy and version than the one that judged), break the finding-to-plan join for a record rule, and make a base rule and the record rule that replaces it collide on one dedupe key. So it needs a decision, not a mapping: (A) keep the internal key and add reader members (`recordRule` and the base `policyCode` through `ReaderCodeOf`) beside it, with every reader-facing consumer reading the reader members; or (B) store the base code plus the flag and key the plan by `policyVersionId`. Either is a change to the stored findings contract (schema 1) and to Signal identity. Not started; to uac-r2 and ontology. Until then the internal review route carries `ReviewFindingDto.PolicyCode` (a one-line comment now says any external or SPA mapping of it must go through `ForReaders()` first).

Sentinel: `InvoiceSubmissionProcedureHoldTests.Sentinel_PlantedResolverOutput_ReachesNoStoredFindingsPlanOrView` plants a Procedure id, an override row id, a chain record id, a policy id, a typed reason and a `BRR-` code in the resolver's gap, runs the real gap writer, `BuildFindingsJson` and part D's mapper, and asserts none of them (in D, N or B form) appears in the stored JSON, the plan, the reader text or the status warning, and that the gap finding holds exactly the three reader members. Seed: the writer made to read the internal record (`g.ProcedureName + " " + g.Overrides[0].Reason`), the sentinel and two neighbours failed; restored `cmp`-identical. The test does not plant a `BRR-` rule finding in `findings[]`, because that is the open point above, not a defect this round can close.

### Out of scope, as briefed

`ForReaders()` consumers other than the gap writer (K4: uac-r2); the writers guard's run-time column names (K3); solution export of the column (before the master PR).

### Verification

| Check | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | 0 errors, 0 warnings |
| Filtered unit (the brief's filter plus `CoreRecordChain` and `SupersedeUndo`) | 3256 passed, 0 failed, 5 skipped (live seam tests) |
| ArchTests (`InvoiceReviewDetail\|ProcedureReader\|FindingsJson\|AccessTable\|Redaction`, plus `CoreRecordChain` and `InvoiceFirmHold`) | 45 passed, 0 failed |

### Seeding proofs (each restored `cmp`-identical from a backup)

| Seed | Result |
|---|---|
| 1. The `InvoiceReviewDetail.Column` entry removed from `AccessColumns["sprk_invoice"]` | 6 failed: the new must-fire, the census equality, four column-form rows of the existing access-column theory |
| 2. The new detector rules downgraded to the old ones (records only, no Concat scan) | the control test failed, naming 10 shapes it missed |
| 3. The gate's undo made to pass no detail | 2 failed (the held-for-gap reopen and the firm / none test) |
| 4. The persister's held create made to write no detail | 3 failed (the new no-firm test and the two existing gap / unresolved tests) |
| 7. `Transient` removed from the type-read fault | 1 failed (the chain pin) |
| 8. The gap writer made to read the internal record | 3 failed (the end-to-end sentinel and two neighbours) |
