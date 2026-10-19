# Task 072 - InvoiceComplianceEvaluator, BillingFactLoader, BillingFindingSignalMapper (FR-25)

Rigor: FULL. Model: Opus 5.5 @ high. Base: `98a65eb66` (`work/ebilling-r1`). Step 0: `f292ad119` is an ancestor; 026 has landed
(`SignalWriter.VerifiedSubjects` holds `sprk_invoice` and `sprk_billingevent`); the policy store (019) expresses rule set scope and
in-force dates. No escalation trigger fired.

## 1. What shipped

| File | Role |
|---|---|
| `Services/Signals/Billing/BillingFactLoader.cs` | `BillingEvaluationRequest` (what 049 hands in), `BillingRuleCandidate` (one version as read, with `CanBeInForce` / `IsInForceOn`), `BillingLoadedFacts`, and the loader. Reads only. |
| `Services/Signals/Billing/InvoiceComplianceEvaluator.cs` | `EvaluateAsync` = load + the pure `Evaluate(request, loaded)`. Result: findings, `RulesByVersion`, `Refusals`, `Skips`, the fact set judged. Writes nothing. |
| `Services/Signals/Billing/BillingFindingSignalMapper.cs` | Static `Map(finding, rule, subjects)` to a `SignalWriteRequest` plus the message source. |
| `Infrastructure/DI/FinanceModule.cs` | Two `AddScoped` lines (concrete, ADR-010), unconditional (flag rule), placed after `IInvoiceResolutionService`. |

Placement: Signals/Billing beside 070/071 (the handlers and fact set live there; the mapper targets `SignalWriter`). Scoped because the
loader holds the scoped `SecureChildShareSynchronizer`; the gate `PolicyVersionValidator` is the SignalsModule singleton.

## 2. Behaviour

**Rule selection.** Rule sets: domain Billing (query filter and row re-check, as 076), status Published, scope Client default, or Firm
with `sprk_firm` = the submission's firm. Policies: billing, enabled, active. Versions: only those that can be in force - the policy's
current version or one with an end date (076 K3); a draft, a rejected version or an abandoned edit is not even returned.
Per subject (the invoice on its invoice date; a line on its date, else the invoice date): each policy's version in force on that date
(`IsInForceOn`: the current version from its `sprk_inforcefrom` on, the pointer alone deciding even with a stale end date; an ended
version inside `[from, to)`). Two versions of one policy in force on one date (a failed publish's overlap): current, then highest number.
A subject with no date at all: each policy's current version (there is no date to pick an older one, and RequiredData must still run).

**D-39.** Per subject, a firm-scope version of rule type T drops the client-default versions of T. A version the gate refuses does not
count, so a broken firm rule leaves the client default in force (fails toward the stricter check).

**FR-24.** Each distinct selected version passes `TryPrepareBillingRule` once (the gate logs 50301 and meters). A handler's
`BillingRuleRefusedException` is logged as `PolicyVersionInvalid` (Error) and metered (`no_handler` -> `rule_type_unsupported`,
`parameter_missing` -> `schema_invalid`); the run continues. Any other exception propagates (a defect).

**Handlers** run once per applicable version on the whole fact set; 072 keeps the findings whose subject that version applies to.
Suggested adjustments are carried, never applied.

**D-20.** Skip reasons `billing.currency.missing`, `.not_usd`, `.unreliable` (unknown confidence counts as below the floor, as in
`HandlerContext`). ApprovedRate is not run; ExpenseRule runs and its `amount_over_cap` findings are dropped (the code check stays). Each
skip is logged (Warning) and listed in `Skips`. A missing currency is never taken as USD.

**Order.** Invoice first, then line index; within a subject catalogue position, policy code (ordinal), policy id, version id, then the
handler's own order. No clock read.

**Priors (D-112, K1).** Every submission of the firm (`sprk_vendororg`), from every partition, except this one and Failed attempts;
a Cancelled invoice is skipped too. Status: invoice Rejected / Returned to Firm, else Active; with no invoice, submission Rejected, else
Active. Lines: by `sprk_linesequence`, then id; hours only from Fee lines' `sprk_quantity`. The evaluator also drops a prior whose id is
the submission's. Priors and roots are read only when a Duplicate version can be in force.

**Secure roots (uac-r2's four calling conditions).**
1. Each prior row is read with every `SecureChildLineage` lookup of its table plus `owningteam` / `owninguser` (`WalkColumns`), and is
   passed as `readRows` too.
2. Child rows only: a prior's `sprk_invoice` row, or its `sprk_invoicesubmission` row when it has none. The judged invoice does not exist
   yet (049 creates it after evaluation), so it is walked as an unsaved `sprk_invoice` probe (id = submission id) carrying only the core
   lookup (`sprk_matter` / `sprk_project` / `sprk_regardingworkassignment`). No core record = no lookup = empty set.
3. `ResolveSecureOwnerTeamAsync` refusal (ambiguous team) or any fault in the walk = every set unresolved (`null`); per group, resolved
   only when Completed and no row of the group is undetermined.
4. No Secure Record owner team (NotApplicable) = every set empty.
The core record's `sprk_issecure` is never read; the walk goes by ownership.

**Mapper.** Subject `sprk_invoice` (49's invoice row) or `sprk_billingevent` (its line row by index; a missing one throws). Lane from
the policy (Decide when none), severity from the policy, headline the version's (non-blank, <= 200) else the type's default. Message:
withheld Duplicate -> `WithheldDuplicateTemplate` with NO fact values; otherwise the first template whose tokens all have values: the
version's, `InvoiceDuplicateTemplate` ("Invoice {{invoice.firmInvoiceNumber}} duplicates {{duplicateOf.trackingNumber}}.", invoice-level
Duplicate only), the type's default wording, the default headline + ".". Facts limited to `TemplateFactFields`.

## 3. Tests

| File | Tests |
|---|---|
| `tests/unit/domain/Signals/Billing/InvoiceComplianceEvaluatorTests.cs` | 26 cases in 21 methods (D-39 x3, `IsInForceOn` directly, refused firm rule, in force x6 incl. the draft / rejected theory, gate and handler refusals, propagation, currency theory x5 + control + ExpenseRule, K1, citation, determinism / order, 150-line timing) |
| `tests/unit/domain/Signals/Billing/BillingFindingSignalMapperTests.cs` | 8 (all 11 types render, blank and default templates; version template; fallback; missing line row; wrong version; invoice-level Duplicate written through the real `SignalWriter`; withheld Duplicate written with no tracking number in any column) |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Signals/Billing/BillingFactLoaderTests.cs` | 15 over `SecureChildShareWorld` with the real walker (rule selection, K3, no Duplicate = no prior reads, core record, priors and status, bounded reads, the seven D-112 cases plus unselected lookup, undetermined, NotApplicable) |
| `tests/unit/domain/Signals/Billing/BillingEvaluationKit.cs` | shared builders |

Counts: 49 new tests, all green (first run: 47 of 48 green, 1 test-side FluentAssertions misuse fixed; the 49th added for M3b). Fail-before: none of the three types
existed at base, so every new test fails to compile there. Mutation proofs: section 4.

**Timing (AC):** 150 lines, all 11 types, 1,276 findings: **3.9 ms** in-process after a warm-up run (limit 5 s).

**Bounded reads (AC):** the read sequence recorded by the mocked entity service is identical for 1 line / 1 prior line and 150 lines /
150 prior lines. The judged invoice's lines are never read (they come in the request).

**No writes (AC):** the evaluator's in-memory tests run over a strict entity double with no setups; the loader tests verify no
`UpdateAsync` / `DeleteAsync` (the strict double has no Create), and the world records no delete.

## 4. Mutation proofs

Each mutation was applied to the committed source, the test project rebuilt and the named suite run, then the file restored
(script: session scratchpad `mutate072.py`). All 16 are caught.

| # | Mutation | Caught by |
|---|---|---|
| M1 | D-39 precedence removed | `AFirmRule_OverridesTheClientDefault...`, `Precedence_IsDecidedPerLine...` |
| M2 | line date ignored (current version only) | 4 in-force tests |
| M3 | `CanBeInForce => true` (loader returns drafts) | `DraftAndRejectedVersionsWithNoEndDate_AreNotReturned...` (loader) |
| M3b | `IsInForceOn` treats an open-ended non-current version as in force | `IsInForceOn_AVersionThatIsNotCurrentAndHasNoEndDate...` (added after the first run: the two K3 layers covered each other, so the evaluator-level theory alone did not catch it) |
| M4 | missing currency treated as USD | the D-20 theory (both "missing" rows) |
| M5 | evaluator keeps its own submission among the priors | `TheSubmissionBeingJudged_IsNeverOneOfItsOwnPriors_K1` |
| M6 | handler catch widened to `InvalidOperationException` | `AnyOtherHandlerException_IsADefect_AndPropagates` |
| M7 | findings not limited to the subjects the version applies to | 3 in-force / precedence tests |
| M8 | loader keeps its own submission | `Priors_AreEverySubmissionOfThisFirm_ThisOneExcluded...` |
| M9 | `sprk_regardingworkassignment` left out of the walk columns | `APriorFiledThroughALookupOtherThanTheMatter...UacR2Condition1` |
| M10 | walk answer used without the Completed / undetermined check | `APriorUnderARootFlaggedButNotIsolated...UacR2Condition3` |
| M11 | a walk fault read as "ordinary" (empty sets) | `ARootSetReadFailure_HidesTheDetail_AndTheFindingStillFires_D112` |
| M12 | probe files a work-assignment core under the wrong column | `ACoreRecordSecureThroughItsParent...D112` |
| M13 | withheld branch disabled | `AWithheldDuplicate_StoresNoTrackingNumber...D112` |
| M14 | invoice-level Duplicate template removed | `AnInvoiceLevelDuplicate_WithTheDefaultTemplate_IsWritten_071FixRound2` |

Suites after the change: `dotnet build` BFF 0 warnings / 0 errors; `Sprk.Bff.Api.Tests` filtered
`Billing|Compliance|Finance`: 2,246 passed, 3 skipped, 0 failed (2,249 total; 49 of them new); `Spaarke.ArchTests`: 1,175 / 1,175.

## 5. Measurements

Publish (`dotnet publish -c Release -r linux-x64 --self-contained false`, zipped Optimal, PDBs included; fresh exports of each commit):

| Build | Zip | Files | `Sprk.Bff.Api.dll` |
|---|---|---|---|
| master `20fb86dc4` | 39,208,013 B (37.39 MB) | 192 | 19,651,584 |
| base `98a65eb66` | 39,602,143 B (37.77 MB) | 192 | 20,636,672 |
| tip `d4d11cb71` | 39,629,979 B (37.79 MB) | 192 | 20,702,208 |

Task delta: **+27,836 B (+0.03 MB)** against base; branch against master +0.40 MB. Total 37.79 MB (< 55 MB review line). No package
or project file changed; `dotnet list package --vulnerable --include-transitive`: no vulnerable packages.

## 6. Component justification (CLAUDE.md section 11)

- **BillingFactLoader.** Existing: `PolicyScopeResolver` (Ontology scope, tenant / matter) and the Ontology policy readers (allow-list
  excludes Billing). Extension: no; neither has a rule-set-scope or firm axis, and billing reads need the K3 version rule and D-112 roots.
  Cost of doing nothing: 049 has no facts to evaluate a submission with (FR-25).
- **InvoiceComplianceEvaluator.** Existing: `SignalEvaluator` / `SignalEvaluationService` (stored-row predicates; the compiler refuses
  every billing type, D-23) and 071's handlers (judge one rule, select nothing). Extension: no; selection by line date, scope and firm has
  no counterpart there. Cost of doing nothing: no component decides which rules apply to an invoice; 049 cannot produce findings.
- **BillingFindingSignalMapper.** Existing: `SignalEvaluationService` builds requests from a predicate hit. Extension: no; a billing
  finding's subject is a line index and needs the D-112 withheld variant. Cost of doing nothing: 049 has no tested mapping the writer
  accepts (several default templates name tokens many findings lack - section 8 Q2).
- **The two DI registrations** are the three classes above. No interface (ADR-010 ceiling untouched), no package.
- **Internal constructor on the evaluator** (handler dispatch seam). Existing: none. Extension: n/a. Cost: a handler refusal cannot be
  produced through the gate with a valid body, so FR-24's "refused at the handler, others still run" would be untested.

## 7. Deviations and judgment calls (one line each)

1. **Timekeepers, rates and code tables are not loaded** (POML step 1): 046 and 047 resolve them and 049 passes the resolved lines in
   `BillingEvaluationRequest.Lines`; 071's fact set carries no code tables, so a second read would only drift from 046/047.
2. **No core-record axis in rule selection** (POML step 2 "for the line's core record"): the 019 store scopes a rule set to client
   default or a firm only; nothing to select on.
3. **Retired rule sets are not evaluated**, even for lines dated while they were in force: a retired set is withdrawn (076).
4. **Scope is read as assigned now** (`PUT /assignment` re-scopes immediately); historic assignment is not modelled by 019.
5. **A disabled or inactive policy is not evaluated** (076 publish sets `sprk_enabled`; a proposal stays disabled until then).
6. **A refused firm version does not suppress the client default** (fails toward the stricter check).
7. **No-date subjects use the current version** (no date to choose by; RequiredData must still report the missing date).
8. **Prior lines are read only when needed** (review B1; D-112 still loads priors from both sides of the wall): only when a Duplicate
   version that can be in force names a line match field, and only for Active priors (the only ones Duplicate compares). The test
   is a superset of the versions the evaluator selects (no narrowing by date or precedence), and an unreadable body counts as naming
   one. Known limit: a firm with very many Active prior invoices under a line-comparing rule still makes this read large.
9. **"Inherited-secure core record takes its root's set"** is implemented as the uac-r2 walker defines roots: the walk stops at the
   first Secure-team-owned project / matter / work assignment, so a work assignment secure through its matter has the set {itself},
   whatever its own `sprk_issecure` says. Tested: such a core record with `sprk_issecure = false` resolves to {itself} and a prior filed
   under it keeps its detail.
10. **The judged invoice is a probe** (it does not exist yet): only its core lookup is walked; fewer lookups can only shrink its set,
    which hides more, never less.
11. **The current version ignores `sprk_inforceto`** (`BillingRuleCandidate.IsInForceOn`, 076's "the pointer decides"): a version
    the policy's `sprk_currentversion` points at is in force from its `sprk_inforcefrom` on, even if an end date was left on it by a
    failed publish; only a superseded (non-current) version is bounded by `[sprk_inforcefrom, sprk_inforceto)`.

## 8. Owner questions (with recommendation)

1. **RequiredData does not report a missing currency** (071, `RequiredDataHandler.cs:55-60`, by design per its remarks at lines 15-19:
   "A currency that was not read is not flagged (no evidence)"). The 072 POML (D-20 line) says it "still reports the unsupported or
   missing currency". The two disagree; not blocking 072 (072 never defaults to USD and skips money rules either way). Recommend: keep
   071's choice (a missing value is not evidence, NFR-06) and correct the POML wording; or, if the owner wants the flag, a 071 follow-up
   adds `missingField = currency` when no currency was read.
2. **Default templates name tokens many findings lack** (ApprovedTimekeeper name missing, ApprovedRate with no rate, CoreRecordWindow
   with no close date, NonBillableActivity with one code, any line with no date, invoice-level Duplicate). The mapper's fallback chain
   keeps every finding writable. Recommend: accept the chain, and have 077's review page warn when a template names a token the type
   does not always fill.
3. **Several findings of one policy on one subject share a dedupe key** (`PolicyCode|subject|id|episode`): ExpenseRule code + cap on one
   line, RequiredData with several missing fields. `SignalWriter` would keep one Signal. Recommend 049 decides: one Signal per
   (policy, subject) carrying the first finding (findings themselves stay complete in 049's persisted record), or a writer change.

## 9. Reviews

- ontology-r1 review: REQUESTED, not yet held (this session cannot message other sessions). Request for the main session: ask
  ontology-r1 to review the D-112 loader (section 2, "Secure roots"), the K1 / K3 handling and the mapper's withheld path.
- Step 9.5 code-review (self, at standard depth): no F-class finding. Metrics: loader 689 lines / 3 public / 16 private members,
  about 39 branches. It is cohesive: everything it reads is facts for one submission's evaluation, and it also holds the three
  contract records 049 uses. Evaluator 316 lines / 14 branches; mapper 152 lines / 7 branches; no interfaces. Observations (K4,
  none acted on):
  - The constructor null guards on non-nullable parameters follow the surrounding Finance idiom.
  - `Signals.Billing` now depends on `Services.Finance` (`RuleSetScope`, `IEbillingSettingsReader`, `BillingRuleDraftTranslator`),
    and Finance depends on Signals. This is the same assembly with no layering ArchTest, and 071's fact set already uses
    `Finance.Resolution` types.
  - The loader reads every prior line (section 7 item 8).
  - The handler-dispatch seam is an internal constructor. DI uses public constructors only.
  No DI wiring test was added (ADR-038); the dependencies are checked by hand. `BillingFactLoader` is scoped and takes the
  `IGenericEntityService` singleton, the `IEbillingSettingsReader` singleton, the scoped `SecureChildShareSynchronizer`
  (ExternalAccessModule, unconditional), `IConfiguration` and `ILogger`. The evaluator is scoped and takes the loader, the
  `PolicyVersionValidator` singleton and `ILogger`. Nothing captive.
- adr-check: compliant with every ADR it touches.
  - ADR-001 / 008: no endpoint.
  - ADR-002: reads only, no plugin. D-112 fails closed, which meets WP-6.
  - ADR-004 / 036 / 052: no background work.
  - ADR-007: no Graph.
  - ADR-009: no cache.
  - ADR-010: concrete classes, registered in the module, no new interface (ceiling untouched).
  - ADR-013: no AI dependency.
  - ADR-028: app-only reads through the existing `IGenericEntityService`; no credential change.
  - ADR-029: publish delta measured.
  - ADR-032: no conditional dependency, so no Null peer.
  - ADR-038: pure logic is in `tests/unit/domain`; the loader is tested over an in-memory Dataverse.
  - ADR-044: Guids stay typed.
  The index pass found nothing else in scope. The ArchTests are green, including the owner-assignment census: the probe is a
  two-argument `new Entity(table, id)`, which is not a create site.
- Adversarial review (one pass per task): not run by this agent. The caller schedules it.

## 10. Review fix round, part L (loader and secure roots)

Split of the review fix round (072 + 071 round 3): part L = A1, A2, B1, B3, B4 and B5's secure-root docs; part E = A3, B2, A4, A5;
part W = A6, A7, A8 and the rest of B5. Worktree `C:\wt072L`, branch `fix/ebilling-r1-072-L`, base `7b7b9b294` (`work/ebilling-r1`);
the main session cherry-picks. The first commit adds `BillingEvaluationRequest.ReceivedOn` (required `DateOnly`) for part E's A3; the
evaluator does not read it in part L.

| Item | Commit | Change | Fail-before / mutation evidence |
|---|---|---|---|
| (prep) | `2d95f765d` | `ReceivedOn` on the request; the two test builders set it | Build only (no behaviour) |
| A1 (F1) | `c9f4b64c3` | Each prior is walked from its invoice AND submission rows (each read with its lineage lookups and owner columns), sets unioned; a walked row owned by the Secure team whose set is empty makes that prior `null`. Fixture: an ordinary prior is owned by an ordinary team | Old loader + new tests: 3 failed (the three A1 tests). Mutation "drop (b)": 2 failed. Mutation "walk the invoice row only": 1 failed (submission-under-secure-root test). New loader + old fixture (every prior invoice Secure-owned): `AnOrdinaryPrior_OnASecureInvoice_KeepsItsDetail_D112` failed, which shows the old fixture asserted the leak |
| A2 (F3) | `96b21c746` | Test: an ambiguous Secure owner team leaves the invoice set and every prior set `null`; the Duplicate is withheld | Mutation "return empty sets instead of null" on the Failed path: the A2 test failed |
| B1 | `bb19a8f21` | Prior lines read only when a Duplicate version that can be in force names a line match field (`BillingFactLoader.ComparesLines`; vocabulary `DuplicateHandler.LineFieldOrder`, now internal), and only for Active priors | Read-count test: no `sprk_billingevent` query for a number-only rule, one for a line rule, a Rejected prior's lines not read. Mutation "always read lines": 1 failed. Mutation "lines of every prior": 2 failed |
| B3 | `3155dbc46` | Walk fault logged at Error and counted (`finance.billing.secure_root_walk.faults`, `FinanceTelemetry`) | Mutation "LogWarning": 1 failed. Mutation "no metric": 1 failed |
| B4 | `780912072` | A prior with no tracking number is cited like a prior across the wall (`DuplicateHandler.MayShowDetail`): the withheld template, no detail | Mutation "drop the tracking-number check": the B4 test failed |
| B5 (part) | `b31430d12` | `BillingFactSet` SecureRoots docs and the 071 notes: the walk starts from child rows, never a root table; Failed / NotApplicable / Undetermined per uac-r2's conditions. 071 notes: the "hashes below are the fix-round worktree's own" clause deleted | Docs |

**Judgment calls.**

1. **A1 (b) is applied per row, before the union.** The walk runs one group per row in one run (shared read cache), and the loader
   unions each prior's rows; that equals walking the rows as one group, and it gives each row its own answer, which (b) needs. So a
   Secure-owned invoice with a cleared lookup is unresolved even when its submission names a root: the invoice is secure, but its
   own wall is unknown (it may have been re-filed). This fails closed more often than "(b) on the union" would.
2. **A1 (b) reads the row's `owningteam` only.** A user-owned row is looked through by the walk (the ownership resolver's rule), so
   it is not treated as secure by itself.
3. **A walk that answers a different number of groups** than asked now throws inside the guarded block, so it fails closed to
   unresolved (it was silently tolerated before).
4. **B1 reads "in force" as "can be in force"** (`CanBeInForce`, every loaded version): the loader has no judging date per subject,
   and over-reading only costs a query.
5. **B4 uses `DetailWithheld`**, so a visible prior with no tracking number also loses its line values in the finding. The judged
   line's values are not secret, but citing a prior without its number would read "duplicates ."; the withheld template is the
   one generic text that exists. 073 reads a withheld finding's fields with their confidence and no value text, as for the wall.
6. **B3 counts only the fault path**, as the review asked. The ambiguous-team path stays a Warning: it is a configuration state that
   the provisioning checks own, and the A2 test pins its outcome.
7. **B3 adds a counter to `FinanceTelemetry`** (CLAUDE.md section 11). Existing: `FinanceTelemetry`, the Finance meter, registered as
   an unconditional singleton. Extension: yes, one counter and one method. Cost of doing nothing: a lasting walk fault hides every
   prior's detail on every submission, and nothing alerts on it. The loader's constructor takes `FinanceTelemetry`; DI already
   registers it, and the two test builders pass a new instance.

**Files outside the brief's list.** `Handlers/DuplicateHandler.cs` (B1 makes `LineFieldOrder` internal; B4 changes `MayShowDetail`),
`Telemetry/FinanceTelemetry.cs` (B3), and `tests/unit/domain/Signals/Billing/BillingEvaluationKit.cs` (`ReceivedOn`; the loader's
new constructor argument). None is a part E file.

**Tests.** `BillingFactLoaderTests`: 19 before, 27 after (8 new: three A1, one A2, two B1 of which one is a 5-case theory, one B3,
one B4). Filtered suite (`Billing|Compliance|Rejection|RuleSet|Finance|Signals`): 4,527 passed, 5 skipped, 0 failed. API build: 0
warnings, 0 errors.

**ArchTests: 1,174 passed, 1 failed, and the failure is not caused by this part.** `DecisionRecordContextImmutabilityTests.DecisionRecordWriterOnlyCreatesAndReads`
("D-34: DecisionRecordWriter may use the shared entity service only to declare it, assign it in the constructor and call
RetrieveMultipleAsync") fails at base `7b7b9b294`. That base carries 074's `DecisionRecordWriter` change (`7b7b9b294`, "the recorder
looks up the deciding user's contact itself"), and this part does not touch the file. It is reported to the main session.

**Publish delta.** `dotnet publish -c Release` of `Sprk.Bff.Api` from fresh worktrees at short paths, zipped with `Compress-Archive`, 192 files each. Fresh master `1473b34c6`: 39,215,553 bytes. Base `7b7b9b294`: 39,645,670 bytes. Part L tip `b31430d12`: 39,647,528 bytes. Part L delta: +1,858 bytes against its base. The whole `work/ebilling-r1` branch with part L is +431,975 bytes (+0.41 MB) against master. Both are under the +5 MB trigger, and about 37.8 MiB total is under 55 MB.

**For 049 (constraints, not code).** 049 supplies `ReceivedOn` from the submission's received date (data, never a clock read).


## 11. Review fix round, part E (evaluator, mapper, currency)

Model: Opus 5.5 @ high (implementer). Base: `work/ebilling-r1` `bdd373421` plus part L's `2d95f765d` (cherry-picked as
`2d95f765d`: the required `BillingEvaluationRequest.ReceivedOn`). Branch `fix/ebilling-r1-072-E`. Part L owns the loader, the fact
set and their tests; nothing of theirs is edited here.

### E.1 Item map

| Item | Commit | Change |
|---|---|---|
| A3 (F1) in-force selection | `46c45e4bc` | A subject with no line date and no invoice date is judged on `request.ReceivedOn` (data from 049), not by the current pointer. `SelectVersion` always takes a date. New ArchTest `BillingEvaluatorClockGuardTests`: the in-memory `Evaluate` and the mapper reach no clock (transitive IL scan; must-fire and must-not-fire controls). |
| B2 handler refusal | `33e1c2513` | Handlers run lazily, at most once per version: a firm version's to decide precedence, any other's only when it applies. D-39 counts only versions the gate AND the handler accept (owner, 2026-10-18). Handler refusals follow the gate's in candidate order. Remark fixed. |
| A5 / ontology-r1 F1 | `08ff6e1b1` | `BillingRuleCatalogue.ReasonTemplate` (a reason's own message) and `IsFlagOnlyReason`. `required_data.currency_not_usd` renders "The invoice currency is not USD." and is flag-only. 073's decider holds a flag-only reason (`reason_flag_only`). |
| A4 (F4) Signal dedupe, option a | `fb964ca04` | `BillingFindingSignalMapper.MapAll` sends one request per (policy code, subject). The single-finding `Map` is now internal. |
| A5 / D-115 | `3d406fd20` | One flag-only invoice-level "rates and expense caps were not checked" finding. `"$"` is read as USD. `low_confidence` is renamed `billing.currency.unreliable`. |

### E.2 Behaviour

**A3.** The judging date is line date, then invoice date, then `ReceivedOn`. An undated invoice received before a future-effective
current version is judged by the version in force on the received date, or by none.

**B2.** A firm version refused by the gate or by its handler leaves the client default of that type in force. A client default
displaced by a valid firm version is never run, so it cannot refuse or log.

**A4 (mapper).**
- **Grouping.** The group key is the writer's own `DedupeKeySubjectPrefix(policyCode, subjectEntity, subjectId)`. Groups come out in
  first-appearance order.
- **Reason order.** Within a group, reasons are sorted by code (ordinal), then by a content key, so the output does not depend on input
  order.
- **Consequence.** The strongest effective consequence wins: Reject > SuggestAdjustment > Flag. A flag-only reason counts as Flag, and
  so does a missing or unknown consequence.
- **Message.** The message is the primary reason's (the strongest, then the first). `FactValues` is the primary's allowed facts (the
  template source).
- **Evidence.** One `billing.reasonset` line carries `Ref` = fingerprint = `sorted codes joined by "," + "|" + consequence`. There is
  one `billing.reason` line per reason (`Ref` the code, `Text` its own sentence, `Source` `sprk_policyversion:{id}`). The union of the
  values read follows: `billing.field` (`Ref` `invoice.{Field}` / `line[n].{Field}`, Tier Observation, `Confidence`, `Text` the value)
  and `billing.reference`.
- **Mapping record.** `BillingSignalMapping` exposes `Findings`, `ReasonCodes`, `Consequence` and `Fingerprint`, which 049 uses to
  supersede and for the Decision Record snapshot.
- **Withheld group (D-112).** The group gets the withheld template and no facts. Reason texts are token-free (the withheld template,
  the reason's own message, or the type headline). No value text is stored, because a duplicate line's own date, amount and hours
  equal the hidden prior's.

**A5 (D-115).** When `currencySkip` is set and at least one money check was skipped, the evaluator raises ONE finding:
- **Subject and reason.** It is on the invoice, with reason `billing.currency.missing` / `.unreliable` / `.not_usd`.
- **Message.** "Rates and expense caps were not checked: the invoice currency is missing / unclear / not USD."
- **Version.** It hangs on the first skipped money policy (ordinal policy code, then version id; main-session decision), with that
  policy's rule type and clause.
- **Values read.** It reads `invoice.currency` (FieldRef(null, Currency), with its confidence and below-threshold flag). It cites every
  skipped version as a `skippedPolicyVersion` reference (`CODE:versionid`). It has no fact values.
- **Result.** The version is added to `RulesByVersion`. The finding does not depend on a RequiredData rule being in force. With no money
  rule in force, or only a cap-less ExpenseRule, nothing is skipped and no finding is raised.

### E.3 Tests and evidence

New or changed test methods: 23 added, 1 replaced (a Fact became a Theory). One existing 073 test changed its expectation: a foreign
currency under a RequiredData Reject rule now Flags (`reason_flag_only`), where it used to Reject. Those are the brief's semantics.

Fail-before or mutation proof (each mutation applied to the committed source, the suite run, the file restored; helper
`e_mutate.py` in the session scratchpad):

| Proof | Result |
|---|---|
| A3: the pre-fix evaluator (old `IsCurrent` path) | Both A3 tests fail (`AnUndatedInvoice_ReceivedBeforeAFutureEffective...`, `...BeforeTheOnlyVersionTakesEffect...`) |
| A3: `DateTime.UtcNow` read in `Evaluate` | Clock guard fails |
| B2: the pre-fix evaluator | `AFirmVersionItsHandlerRefuses_LeavesTheClientDefaultInForce_B2` fails |
| F1: `ReasonTemplate` loses `currency_not_usd` | Mapper F1 theory fails (2 cases) |
| F1: `IsFlagOnlyReason` loses `currency_not_usd` | The 073 foreign-currency test fails |
| M-A4-1: no grouping (one request per finding) | 5 A4 tests fail, including the 150-line must-fire guard |
| M-A4-2: reasons kept in arrival order | 4 fail, including order independence |
| M-A4-3: withheld group keeps value text | The leak test fails |
| M-A4-4: flag-only reason takes the version's consequence | The flag-only test fails |
| M-A4-5: weakest consequence wins | The flag-only / Reject test fails |
| M-A4-6: withheld group renders the visible reason's text | The leak test fails. On the first run it survived: the hand-built visible reason lacked `invoice.firmInvoiceNumber`, so its template never rendered. The test now asserts, as a control, that the visible reason alone names the prior. |
| M-A5-1: no D-115 finding | 15 fail |
| M-A5-2: `"$"` not read as USD | 3 fail |
| M-A5-3: currency reasons not flag-only | 12 fail, including both 073 D-115 tests |
| M-A5-4: hangs on the last skipped policy | Placement test fails |
| M-A5-5: cites only the first skipped version | Placement test fails |
| M-A5-6: `not_usd` message falls back to the rule's template | Mapper D-115 theory fails |

**150-line fixture.** 1,276 findings give 1,261 requests, matching the review. 15 pairs are merged, all
`expense_rule.amount_over_cap,expense_rule.code_not_reimbursable`. Mapped one per finding, the same fixture repeats 15 keys: this is
the guard's must-fire control.

**Suites.**
- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors.
- `Sprk.Bff.Api.Tests` filtered `Billing|Compliance|Rejection|RuleSet|Finance|Signals`: 4,543 passed, 5 skipped (live Dataverse), 0
  failed.
- `Spaarke.ArchTests`: 1,178 / 1,178 (3 new).

**Publish delta.** Not measured. No package, project file or DI registration changed, and the change is method bodies plus one
record's init properties and constants.

### E.4 A5 placement rationale

The finding is built in the evaluator, not in a handler, for three reasons. The skip is the evaluator's own decision (D-20). It must
fire whether or not a RequiredData rule is in force (D-115). And handlers judge one rule each, while this finding is about rules that
were NOT judged.

It hangs on a skipped money policy rather than on a synthetic policy, because SignalWriter needs a real policy and version (it writes
lookups to `sprk_policy` and `sprk_policyversion`). It is flag-only by reason code, so the version's configured consequence never
applies to it: the mapper counts it as Flag, and 073 holds it with `reason_flag_only` before eligibility and confidence are checked.

Under A4 it merges with that policy's other reasons on the invoice subject, and being a Flag it can never raise the merged
consequence. The real handlers give ApprovedRate and ExpenseRule no other invoice-level reason, so in practice it stands alone; the
merge case is tested with a hand-built reason.

### E.5 073 check (rejection decider vs the mapper change)

073 decides per finding, from `BillingEvaluationResult.Findings` and each finding's own version (`RulesByVersion`). It never reads
mappings, so aggregation cannot change a decision:
- **Flag-only reasons stay Flags.** A flag-only reason is held (`reason_flag_only`, new in this round), so it never rejects.
- **Reject-capable reasons keep their Reject.** A reject-capable reason is decided on its own and keeps its Reject. Test: ExpenseRule
  Reject on EUR, where the code finding decides and the currency finding is held.
- **The merged consequence is display and fingerprint only.** It is the maximum, so it never drops a Reject and a flag-only reason
  never contributes one.

### E.6 Deviations and open points

1. **"Hold" is not a stored consequence.** The brief and 049 say "Reject > Hold > Flag". `BillingConsequence` has Reject, Flag and
   SuggestAdjustment, so the order used is Reject > SuggestAdjustment > Flag. Holding is 073's per-finding outcome and is not modelled
   in the mapper.
2. **Tension with 049's option-a rule (5).** That rule says D-115's finding "must never be merged into a Reject". The brief (main
   session) decided the finding merges with the policy's other reasons on the invoice and cannot RAISE the consequence. A merged
   Signal can therefore carry Reject because of another reason, while the currency reason itself never rejects (073 holds it). If (5)
   means "never share a Signal with a Reject reason", the finding needs its own policy or subject. Owner or main-session call.
3. **`"$"` is inconsistent with RequiredData.** 071's RequiredDataHandler deliberately treats `"$"` as not USD and raises
   `currency_not_usd`, while the evaluator now reads `"$"` as USD (049 obligation). A `"$"` invoice gets its rates checked AND a "not
   USD" flag. Not changed: it is 071's documented rule. Recommendation: one shared normaliser for both.
4. **Identical reason texts (known limit).** The type's default wording does not tell ExpenseRule's code reason from its cap reason,
   so a merged Signal can carry two reason lines with the same text. The codes differ.
5. **Fingerprint granularity (known limit).** The fingerprint is the codes plus the consequence, as specified. Two RequiredData
   `missing_field` reasons whose fields change but whose count does not keep the same fingerprint, so 049 only refreshes them.
6. **Case-insensitive alternate key (known limit).** Dataverse alternate keys compare case-insensitively. Two policy codes that
   differ only in case would still collide in the writer. The mapper groups ordinally, as the writer builds the key.
7. **Null `Clause` on billing evidence lines.** These lines leave `Clause` null. `SignalEvidenceRef`'s doc says the UI shows such a
   line as "context, not tested"; a billing UI (077) should read the `billing.*` kinds instead.


## 12. Review fix round, part W

Model: Opus 5.5 @ high (implementer). Base `work/ebilling-r1` `db4eee636` (parts L and E and the 073 fix round landed). Branch
`fix/072-part-w`; the main session cherry-picks. Part W also carries 071's round-3 items (071 notes, "Round 3").

### W.1 Item map

| Item | Commit | Change |
|---|---|---|
| (base red) | `df771f186`, `d4a52a2e5` | Test-only. The base failed 9 cases in the filtered suite: part E was written before D-117 and F-ALLOW. `Expense()` bills E101, so fixtures that set only `ExpenseCode = "E109"` never fired the code reason (the 150-line fixture, the A4 two-reasons test, 073's `ARejectCapableReason_OfTheSameMoneyPolicy_...`); they now bill E109, and the union-of-reads assertion expects `line[0].BilledExpenseCode`. 073's `AnUnlistedReasonCode_...` theory no longer uses `billing.currency.missing` as its "new" reason (flag-only since part E, held as `reason_flag_only`); that case is asserted separately |
| A6 + A8 (D-116) | `ffe12af4b` | CoreRecordWindow reads the billing status; Describe text and schema description say what it does, "every charge line (credits excluded)"; docs/data-model lists the columns |
| A7 (071 round 3 F4) | `017503b2a` | A line with NEITHER Hours NOR Amount lists both as null-valued reads with unknown confidence |
| D-119 | `b1a6d27f3` | `BillingCurrency.Normalise` / `IsUsd`, used by the evaluator's money skip and by RequiredData: a bare `"$"` is USD in both |
| A5 placement (049 rule 5) | `1991539ae` | The D-115 finding hangs on a skipped ApprovedRate version; ExpenseRule only when no ApprovedRate version is in force |
| K2 (073 review, deferred) | `56c9f585d` | A stored consequence the type may not carry is evaluated as Flag, not dropped |
| ReceivedOn (main session) | `e310220d4` | `Evaluate` / `EvaluateAsync` throw `ArgumentException` on a default `ReceivedOn`; documented as a UTC date |
| B5 (rest) | this notes commit | Guard "Not covered" lists, section 7 item 11, the D-112 known limits below, `.unreliable` in section 2 |

### W.2 Behaviour

**A6 (D-116).** `BillingFactLoader` reads `sprk_billingstatus`, `sprk_openeddate` and `sprk_closeddate` on all three core tables
(verified live on spaarkedev1 2026-10-18: the three columns exist on sprk_matter, sprk_project and sprk_workassignment; the global
choice is Open 100000000, Final 100000001, Closed 100000002, form default Open; work-assignment dates are DateOnly behaviour,
matter and project TimeZoneIndependent, both DateOnly format). The values are named once, in `CoreRecordBillingStatus`
(`BillingFactSet.cs`). Per line, credits skipped first (D-105):
- **Open**: only `before_open` (with grace). The close date and the inactive state are ignored.
- **Final**: `before_open`; then with no close date every charge line (`core_record_closed`, a line with no date included);
  else `after_close` (with grace).
- **Closed**: every charge line, whatever its date: `core_record_window.closed_to_billing` (new).
- **Empty, or a value outside the set**: D-111 exactly as before. The loader logs an unknown value as a Warning.

Findings carry the reference value `coreRecord.billingStatus` (the label). **Allowlist choice:** CoreRecordWindow's existing three
codes are all on `BillingRuleCatalogue.RejectCapableReasons`, so `closed_to_billing` is added too; the rule's author decides the
consequence. 073's allowlist guard (the handler source names each listed code) passes.

**A7.** See the 071 notes, "Round 3". Through 073: ApprovedRate on "(2.5) @ 650 = (1,625.00)" (rate parsed, hours and amount not)
now holds as `read_field_confidence_unknown` with Hours and Amount as the unreliable fields, where it used to reject.

**D-119.** `"$"` gives no currency finding from either path and rates are checked; `"EUR"` gives both D-115's
`billing.currency.not_usd` flag and RequiredData's `required_data.currency_not_usd` (`"US$"` and `"USD$"` were in this list until the
W review fix round, section 13: they are USD now); a missing or blank currency gives only D-115's
`billing.currency.missing` (RequiredData still does not treat absence as evidence). The confidence key stays exactly `Currency`.

**A5.** The host is the first skipped ApprovedRate version (ordinal code, then version id), else the first skipped ExpenseRule
version. Why this keeps rule (5): the mapper groups by (policy code, subject). ApprovedRate is not run at all when the currency
skips, so its invoice-level group holds only the currency reason. ExpenseRule's handler emits line-level findings only, so the
fallback's invoice-level group is alone too. **The ExpenseRule merge case is therefore not reachable** with distinct policy codes;
nothing is refused and no D-115 reason is withheld. It would become reachable only if two policies shared a code (E.6 item 6, the
case-insensitive alternate key) or ExpenseRule gained an invoice-level reason; the must-fire test below would then fail.

**K2.** Before the gate, the evaluator checks the stored consequence against `BillingRuleCatalogue.IsConsequenceAllowed` (after
parsing the type; an unknown or non-billing type goes to the gate unchanged and is refused there). A disallowed consequence is left
out of the gate's snapshot, so the gate still validates the body, template and length and refuses an invalid body exactly as before.
An accepted version is evaluated; `RulesByVersion` carries it `with { Consequence = Flag }`, which 073 and the mapper read, so it can
never reject. It is logged as a Warning (`OntologyWriterEvents.PolicyVersionInvalid`, reason `consequence_not_allowed`, "evaluated
with Flag") and counted on the new `billing.rule.consequence_downgraded` (tag `rule_type`, `OntologyWriterTelemetry`, beside
`billing.rule.invalid`). It is not a refusal and not in `Refusals`. A downgraded firm version still runs, but since the W review fix round (section 13, F1-b) it
no longer displaces the client default (D-39, D-116). `PolicyVersionValidator.cs` is untouched, so the Ontology paths are byte-identical; publish, accept and edit still refuse at
authoring.

**ReceivedOn.** A default `ReceivedOn` (0001-01-01) has no version in force, so every undated subject was silently judged by
nothing. Both entry points now throw `ArgumentException` (param `request`) before the loader runs.

### W.3 Tests and evidence

Fail-before = the new tests against the pre-change production file (restored afterwards); mutation = applied to the committed
source, suite run, file restored (helper `w_mutate.py`, session scratchpad).

| Proof | Result |
|---|---|
| A6 mutation: handler ignores the billing status (always D-111) | 5 fail: Open x2, Final with no close date, Closed, the work-assignment end-to-end loader test |
| A6 mutation: loader drops `BillingStatus` | 5 fail: the 3-table theory x3, the end-to-end test, the unknown-value test |
| A6 mutation: work assignment read without its dates | 1 fails (the theory's WorkAssignment row) |
| A6 mutation: `closed_to_billing` off the allowlist | 1 fails (the Closed test's allowlist assertion) |
| A8 mutation: the Closed sentence removed from Describe | 1 fails (`Describer_CoreRecordWindow_SaysWhatEachBillingStatusDoes_...`) |
| A7 fail-before (old `HandlerContext.cs`) | 1 fails (the both-absent ApprovedRate case rejects); the one-absent negative case passes on both |
| D-119 fail-before (old `RequiredDataHandler.cs`) | 4 fail (`"$"`, `" $ "` in the handler theory and the evaluator's two-path theory) |
| A5 mutation: old placement (first skipped by code) | 43 fail: the placement test and 42 rows of the combination test (host type) |
| A5 mutation: the mapper ignores flag-only (currency takes the version's Reject) | 90 fail; e.g. "Expected mapping.Consequence to be Flag ... but found Reject" |
| K2 fail-before (old evaluator) | 7 fail: the ApprovedTimekeeper client default, the D-39 firm displacement, and 073's five-type theory (previously "refused, no finding"); the body-invalid control passes on both |
| ReceivedOn fail-before | 1 fails; the control passes on both |
| 150-line guard (main session ask) | With grouping broken (key suffixed per group), `TheMapper_NeverSendsTwoRequestsWithOneDedupeKey_OverThe150LineFixture_MustFireGuard_A4` fails listing exactly 15 repeated keys |

**The A5 must-fire test** (`TheSignalCarryingTheCurrencyReason_IsNeverStrongerThanFlag_OverEveryCombinationOfMoneyRules_MustFire`):
every non-empty subset of five money rules (client default and firm ApprovedRate, client default and firm ExpenseRule with a cap, a
cap-less ExpenseRule), each at Reject, times three currency cases (EUR, missing, USD at 0.50): 93 rows. Invoice-level RequiredData and
Duplicate Rejects are in force and fire in every row (asserted as a control). The Signal carrying the currency reason is Flag and
holds only flag-only reasons; the host is ApprovedRate whenever one is in the subset; with no rate and no cap, no currency reason.

**Changed existing tests.** 073's `TheBillingGate_RefusesAStoredVersion_WhoseConsequenceIsReject` became
`AStoredVersion_WhoseConsequenceIsReject_IsEvaluatedAsFlag_TheCheckSurvives_K2` (K2 is the brief's semantics). The D-115 placement
test now expects the ApprovedRate host. The 071 D-111 Describe assertion reads "every charge line (credits excluded)". RequiredData's
must-fire theory uses `"US$"` where it used `"$"`, and `"$"` / `" $ "` moved to its must-not-fire theory.

**Suites.**
- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors.
- `Sprk.Bff.Api.Tests` filtered `Billing|Compliance|Rejection|RuleSet|Finance|Signals|PolicyVersion`: base `db4eee636` 4,665 passed,
  9 failed, 5 skipped; tip 4,805 passed, 0 failed, 5 skipped (live Dataverse).
- `Spaarke.ArchTests`: 1,207 / 1,207 (the D-34 failure part L reported is no longer present at this base), including `OntologyBillingConsequenceIsolationGuardTests`.

**Publish delta.** `dotnet publish -c Release -r linux-x64`, short paths, `Compress-Archive` (Optimal). Fresh `origin/master` `d7eabba3b`: 39,237,970 bytes; base `db4eee636`: 39,742,544; tip `e310220d4`: 39,743,814. 192 files in all three. Tip vs master +505,844 bytes (+0.48 MB, all of it ebilling-r1 work already on the branch); part W alone (tip vs base) +1,270 bytes. Total 39.7 MB, under the +5 MB per-task and 55 MB review thresholds.

**Where that measurement was taken (corrected in the W review fix round).** The +1,270-byte part-W delta was measured **on the part-W agent branch, before the cherry-pick**: base `db4eee636` against that branch's tip, which held exactly the part-W commits. That branch was temporary; its commits are on this branch as `ffe12af4b` (A6 + A8) through `97b51f619` (notes). It is a branch-local measurement. It is NOT a measurement of `db4eee636..e310220d4` on `work/ebilling-r1`, because that range also contains #2095 (`a69e703fc`..`5abdaf7fb`) and the 073/074 review follow-ups.

### W.4 Deviations and judgment calls

1. **Final with no close date reuses `core_record_closed`** (D-111's "no window end" reason) rather than a second new code; only
   Closed gets a new code, as the brief asked.
2. **An unknown billing-status value is treated as empty (D-111)** and logged by the loader as a Warning. Failing safe (flag every
   line) was the alternative; D-111 is what such a row got before the column existed.
3. **Under Closed, a line before the open date reports `closed_to_billing`**, not `before_open`: the status decides first.
4. **The K2 downgrade is in the evaluator**, so the gate and every Ontology path are byte-identical. Warning plus a "downgraded"
   metric was chosen over keeping the Error log and a `Refusals` entry, because the version is not refused: it produces findings.
5. **A7 adds the unknown read even when the finding already lists the field with a known confidence** (the line's map can hold one for
   a value that did not parse); it is not added again when one with unknown confidence is already there.
6. **The guard "Not covered" wording** was added to both `BillingHandlerPurityGuardTests` and `BillingEvaluatorClockGuardTests` (the
   same scanner, `IlCallScan`, follows method references only).
7. **Commit order.** The A6 commit landed before the two test-only commits that turned the base green; A6 touches none of the
   failing tests, and the filtered suite was green before every later commit.
8. **`BillingRuleDraftTranslator`'s CoreRecordWindow description** (the drafting prompt: "within the open and close dates") is not
   changed: it describes what to draft, not how it is judged. Recommend a follow-up if 075 should mention the billing status.

### W.5 Known limits (recorded, not changed)

**D-112 (one sentence each).**
- A two-edit narrowing of a Secure-owned prior's union before reconciliation: if the root lookup is cleared on both the prior's
  invoice and its submission before the reconciler runs, the prior's union narrows; that widens what is shown, but it needs write
  access inside the wall, so it is not an escalation (uac-r2, K-ii).
- Whether detail is shown is decided once, at evaluation time, so a prior moved under a secure root later keeps the detail already
  written into its Duplicate finding and Signal.
- The single named Secure team (uac task 178, future): the A1(b) guard (`BillingFactLoader.cs` around line 697) tests
  `owningteam ==` the configured Secure team only, so a row owned by another family's Secure team (UAC task 178 /
  uac-secure-project-r3, not live yet) whose lookups are cleared would read as ordinary; when 178 lands, A1(b) must test "owned by
  any team in the Secure BU" (`ResolveSecureBusinessUnitAsync`, as `ReownOrdinaryRowAsync` does) and add a second-team test. uac-r2 is
  filing an issue against 178 (uac-r2, K-i).

**K2.**
- (Removed in the W review fix round, section 13, F1-b: a downgraded firm version no longer displaces the client default.) 073's known
  limit for a null-consequence firm row stays; see section 13.3.

**Mapper and currency (main session).**
- The reason-set fingerprint ignores which field is missing: two RequiredData `missing_field` reasons whose fields change but whose
  count does not keep the same fingerprint (E.6 item 5).
- A foreign-currency invoice with a RequiredData rule and a money rule gets two currency Signals: D-115's on the money policy and
  RequiredData's `currency_not_usd` on the RequiredData policy (different policy codes, so different groups). Both are flags.
- A withheld group's `BillingSignalMapping.Findings` still carries the visible reason's values (the request itself is scrubbed). 049
  must build the Decision Record snapshot from the merged, scrubbed form; the main session adds that constraint to 049.
- RequiredData's own `currency_not_usd` (flag-only) can share a group with a RequiredData `missing_field` Reject on the invoice; the
  merged Signal then shows Reject because of the missing field (E.6 item 2's case, not the D-115 reason).


## 13. Review fix round (W review)

Implementer, Sonnet 5.5 @ high (commit trailer as the brief required). Base `work/ebilling-r1` `bc501875b`. Branch
`worktree-agent-a4e999e511fa2ca29`; the main session cherry-picks. Inputs: the adversarial review of parts E and W, then ontology-r1's
re-check items (F2, the EventId, the optional mapper test). Read first: sections 11 and 12, D-39, D-115, D-116, D-119.

### 13.1 Item map

| Item | Commit | Change |
|---|---|---|
| F1-a | `4795a4209` | `core_record_window.closed_to_billing` and `core_record_window.core_record_closed` get their own token-free `ReasonTemplate` (`ClosedToBillingTemplate`, `NoCloseDateTemplate`). The mapper already took a reason's template before the version's and the type's, so only the catalogue changed. `before_open` / `after_close` keep the version's template (they are about the dates). |
| F2 | `2ef271b57` | `BillingRuleCatalogue.ReasonHeadline`: a reason with its own sentence has its own `sprk_name`. The mapper uses it when the PRIMARY reason has one (and the group is not withheld). A flag-only primary reason takes lane Decide and severity Info. |
| F1-b | `0da5c4b08` | A downgraded firm version is left out of the D-39 precedence set (`!downgraded.ContainsKey(...)`). It still runs as Flag; the client default stays in force. |
| EventId | `229cdbd7a` | The consequence-downgrade Warning has its own `EventId` (`PolicyVersionConsequenceDowngraded`), no longer 50301. First 50316; moved to 50318 in the re-check round, because ontology 042b's `ParticipantShareFailed` (50316) and `FilingReownFailed` (50317) take those ids on `origin/docs/ontology-platform-design`. |
| Optional mapper test | `05bdb0213` | A downgraded version maps to a Flag Signal with a Flag fingerprint. A pin: the behaviour already held, so no fail-before. |
| A7 extension | `61a2bd9c9` | A line with Hours (not negative) and no Amount lists Amount as an unknown read (null value, unknown confidence, below the floor). |
| Currency | `3668c83da` | Confidence is checked before "not USD"; `US$` and `USD$` are USD in the one shared normaliser. |
| Doc fix, this section | this notes commit | W.3 publish delta corrected; W.2 and W.5 updated; this section. |

### 13.2 Behaviour

**F1-a.** The sentence for a Closed core record is now "The matter, project or work assignment this line is billed to is closed to
billing." and names no date. For a core record with no close date (Final, or billing status empty and inactive) it is "This line
cannot be checked against a close date: the record is closed or final but has no close date."

*Deviation from the brief's example wording.* `core_record_window.core_record_closed` is one reason code with two sources: Final with
no close date (D-116) and the empty-status inactive record with no close date (D-111). The brief's example said "the record is final",
which would be false for the second. The sentence says "closed or final" so it is true for both; no new reason code was added.

**F2.** The title and ranking of a Signal now follow its primary reason:

| Reason | `sprk_name` | Lane / severity |
|---|---|---|
| `billing.currency.missing` / `.unreliable` / `.not_usd` | "Rates and expense caps not checked" | Decide / Info |
| `required_data.currency_not_usd` | "Invoice currency is not USD" | Decide / Info |
| `core_record_window.closed_to_billing` | "Closed to billing" | the rule's (a reject-capable reason) |
| `core_record_window.core_record_closed` | "Close date missing" | the rule's |
| any other reason | the version's `ShortHeadline`, else the type's | the rule's |

*Flag level, stated choice.* The mapper had no Flag-level lane or severity to reuse. A flag-only primary reason (the group then holds
only flag-only reasons, or its strongest member is one) takes **lane Decide and severity Info**: Info is the lowest severity, and
Decide is the reviewer's lane, never the action (Do) lane. They are constants on the mapper (`FlagOnlyLane`, `FlagOnlySeverity`).
A group whose primary is a reject-capable reason (the D-115 reason merged with a Reject, E.6 item 2) keeps the rule's headline, lane
and severity: the primary reason there is the breach.

**F1-b.** For a firm ExpenseRule stored as Suggest adjustment (downgraded to Flag) and a client-default ExpenseRule at Reject, both
now produce findings: the client default's code and cap findings (version consequence Reject) and the firm's own code finding (version
consequence Flag). Before, only the firm's ran. Cost of the change: none for a valid firm version, which still displaces the client
default (control test).

**A7 extension.** An ApprovedRate finding on "2.5 @ 650" with no parsed amount used to Reject (Hours read reliably, Amount not
listed). It now holds as `read_field_confidence_unknown`, Amount the unreliable field. An expense line (no hours, amount present) is
unchanged (the existing negative case, and a new "hours and amount both present still rejects" control).

**Currency.** `CurrencySkipReason` order is: missing, then unclear (unknown or below-floor confidence, whatever the code is), then
not USD. A low-confidence "EUR" is `billing.currency.unreliable`. `BillingCurrency.Normalise` reads `$`, `US$` and `USD$` (any case,
trimmed) as USD; `CA$` stays foreign. RequiredData goes through the same normaliser, so the two paths still agree (the D-119 theory
covers it). *Follow-up commit (main session's answer on this point, D-123):* RequiredData's flag-only
`required_data.currency_not_usd` now stays quiet when the currency confidence (`HeaderConfidence["Currency"]`) is unknown or below the
reliability floor, the evaluator's own test (`HandlerContext` marks the read from the same threshold; the floor itself is reliable). A
low-confidence EUR with a RequiredData rule therefore gets only D-115's "unclear"; a reliable EUR still gets both "not USD" flags.

### 13.3 Known limits (recorded, not changed)

**A null-consequence firm row still overrides a client default's Reject (073's limit).** Not changed on purpose. A null consequence
means "the type's default" and is a valid authored row: publish allows it, the gate accepts it, and the firm author chose a rule of
that type for the firm. It is not "a firm rule version that cannot be evaluated" (D-116), so nothing is downgraded and nothing is wrong
with the row; the firm version simply decides the type for that firm (D-39). A test pins the behaviour
(`ANullConsequenceFirmVersion_StillDisplacesTheClientDefaultsReject_KnownLimit_073`). The "a downgraded firm version still displaces a
client-default Reject" limit that W.5 listed is removed: F1-b fixes it.

**For 049: the merged Signal's consequence and primary message.** A merged Signal's `Consequence` is the version's configured one (the
strongest of the merged reasons, flag-only counting as Flag), and its primary message is the first reject-consequence reason in code
order. Neither is 073's deciding reason. 049 should choose the primary reason from 073's Rejects (`bc501875b` already records the
constraint in `tasks/049-submission-processing-job.poml`).

### 13.4 Tests and evidence

Fail-before: the new or changed tests against the pre-change production file (the new constants were added first, wired to nothing,
so the tests compile and fail on behaviour). Counts are failing cases.

| Item | Fail before | After |
|---|---|---|
| F1-a (mapper: Closed x3 template cases, Final with no close date; the date-reason control passes on both) | 4 | 0 |
| F2 (the two `ReasonTemplate` tests, the D-115 theory x3, the new flag-only lane/severity test; the real-breach and merged controls pass on both) | 10 | 0 |
| F1-b (the ExpenseRule case and the TimeIncrement variant; the valid-firm and null-consequence controls pass on both) | 2 | 0 |
| EventId (the K2 Warning's `EventId` is 50318, not 50301) | 1 | 0 |
| A7 extension (the hours-no-amount case; the both-present control passes on both) | 1 | 0 |
| Currency (low-confidence EUR x2, `US$` x3, `USD$` in two paths, RequiredData `US$` and `USD$`) | 9 | 0 |
| Optional mapper test (downgraded version, Flag Signal, fingerprint) | pin, 0 | 0 |
| RequiredData silent below the currency floor (follow-up commit; handler x2, evaluator x2 for 0.50 and unknown; the floor and reliable-EUR controls pass on both) | 4 | 0 |

Suites (at the final code commit):
- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors.
- `Sprk.Bff.Api.Tests` filtered `Billing|Compliance|Rejection|RuleSet|Finance|Signal|PolicyVersion|DecisionRecord`: 4,912 passed, 6
  skipped (live Dataverse), 0 failed (4,906 before the RequiredData follow-up).
- `Spaarke.ArchTests`: 1,207 / 1,207.

Existing tests changed: `ADowngradedFirmVersion_StillDisplacesTheClientDefault_D39_K2` was replaced by the F1-b tests; RequiredData's
`US$` row moved from its must-fire to its must-not-fire theory (and `CA$` took its place); the D-119 two-path theory's `US$` row now
expects USD.

**Publish delta.** Not measured. Method bodies, string constants and one `EventId`; no package, project file or DI registration.

## 14. ontology-r1 re-check of the W review fixes (2026-10-19)

ontology-r1 re-checked `4795a4209^..c02200029`. Headlines, the downgraded firm version, the downgrade test, F1-a, hours with no amount and the shared currency normaliser all held. Mutations were killed: `ReasonHeadline` returning null, the downgrade displacing the default again, removing "US$", and the stored consequence used instead of the effective one.

| Finding | Fix |
|---|---|
| F4: `EventId` 50316 collides with ontology 042b's `ParticipantShareFailed` (`origin/docs/ontology-platform-design`, `DecisionRecordWriter.cs:377`) once both reach master | `PolicyVersionConsequenceDowngraded` is 50318. The ontology range is free to 50339; 50340+ is `SignalEvaluationTriggerHandler`. |
| F2: in a Flag tie, the flag-only `currency_not_usd` sorted before `missing_field` and became the primary. The Signal was titled as the currency note at Decide / Info, and the author's Do / Critical breach showed only in evidence. Reject was correct. | The primary is the first member at the winning consequence that is not flag-only (a flag-only one only if no other). Test `InAFlagTie_ARealBreachIsThePrimary_NotAFlagOnlyNote_OntologyWRecheckF2`. |

**K1 (known limit, no code).** A low-confidence foreign currency with only RequiredData in force produces no finding: RequiredData stays quiet below the confidence floor, and D-115's "rates not checked" fires only when a money rule (ApprovedRate or ExpenseRule) was in force and skipped. Nothing is judged as if USD, because no rate rule ran. With a money rule in force, the D-115 "unclear" finding fires.

