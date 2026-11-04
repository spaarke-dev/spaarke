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
5. **081 `RetireVersionAsync` was NOT changed (A1 c).** It cannot refuse a Draft cheaply: "retire" is the only way to discard a Draft (`ProcedureService` has no delete, and `procedure.draft_exists` tells the user to "publish or retire it before creating another"). An existing test relies on a Draft being retired without publishing (`Update_ScopeChange_AfterADraftWasRetiredWithoutPublishing_Succeeds`). Refusing would remove the discard path, which is a 081 contract change, not a resolver fix. **Recorded as a fail-closed known limit, releasable by Override:** a Draft that has an effective-from and is then retired counts as a Retired version with a from-date, so it can open a window early. A gap appears only if the Draft's from-date is earlier than every in-force version's from-date, or falls in a hole. An alternative for later, if it bites: count a Retired version only when a Published policy version sits under it (the test `ProcedureService.ScopeLockedAsync` already uses); its own limit is a version published with no rule.
6. **Model 7.1 text** ("at least one Active version") is not edited here (the model is final-state and owned elsewhere). The resolver implements the widened test; the model owner should change the sentence to "Active, Superseded or Retired".
7. **`RequireBasedOn` checks every Published version of a record rule**, as `EnsureRecordRuleAsync` does before it writes. If a path is ever added that edits a record rule by adding a version (today `RuleSetService` refuses record-scoped sets), it must copy `sprk_basedonversion`, or the resolver faults.

### Known limits (K)

- **K3 (082c criterion):** 082c must run the gate before it calls `BillingRuleCatalogue.PrecedenceOf`, which throws for a non-billing rule type.
- **K5:** the change's start is a UTC date (`sprk_inforcefrom` is written as `UtcNow` and read as its UTC date) and errs strict (coordinator's classification).
- **Draft retired with a from-date:** decision 5 above.
- **`createdon` tie:** if a removal and the next change fall within the same stored second, an older episode's version would count as current. Needs two BFF calls in one second; not seen. The boundary has not been checked against live Dataverse timestamps (no live call here).
- **A record rule whose policy id is not the BFF-derived one** (made by hand) is not recognised; its Exclude reads as a plain Exclude. The BFF never writes one.
- **`ToString` of other records still prints ids:** `ExcludedProcedure` (Procedure id), `ExcludedRule`, `ResolvedRule` and `ResolvedPolicyVersion` (policy and version ids), `CoreRecordLink` (record id). The brief named four types. They carry no override reason, and a collection member never prints its elements, but a direct `ToString()` of one is the same class of leak. Not changed.

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
