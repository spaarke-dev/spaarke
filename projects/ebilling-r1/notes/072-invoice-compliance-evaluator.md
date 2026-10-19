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

**D-20.** Skip reasons `billing.currency.missing`, `.not_usd`, `.low_confidence` (unknown confidence counts as below the floor, as in
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

Model: Opus 5.5 @ high (implementer). Base: `work/ebilling-r1` `bdd373421` plus part L's `94de0f3c4` (cherry-picked as
`dea47a05e`: the required `BillingEvaluationRequest.ReceivedOn`). Branch `fix/ebilling-r1-072-E`. Part L owns the loader, the fact
set and their tests; nothing of theirs is edited here.

### E.1 Item map

| Item | Commit | Change |
|---|---|---|
| A3 (F1) in-force selection | `157f82292` | A subject with no line date and no invoice date is judged on `request.ReceivedOn` (data from 049), not by the current pointer. `SelectVersion` always takes a date. New ArchTest `BillingEvaluatorClockGuardTests`: the in-memory `Evaluate` and the mapper reach no clock (transitive IL scan; must-fire and must-not-fire controls). |
| B2 handler refusal | `0af7e0774` | Handlers run lazily, at most once per version: a firm version's to decide precedence, any other's only when it applies. D-39 counts only versions the gate AND the handler accept (owner, 2026-10-18). Handler refusals follow the gate's in candidate order. Remark fixed. |
| A5 / ontology-r1 F1 | `d4e1e800f` | `BillingRuleCatalogue.ReasonTemplate` (a reason's own message) and `IsFlagOnlyReason`. `required_data.currency_not_usd` renders "The invoice currency is not USD." and is flag-only. 073's decider holds a flag-only reason (`reason_flag_only`). |
| A4 (F4) Signal dedupe, option a | `e98acf482` | `BillingFindingSignalMapper.MapAll` sends one request per (policy code, subject). The single-finding `Map` is now internal. |
| A5 / D-115 | `359730e1e` | One flag-only invoice-level "rates and expense caps were not checked" finding. `"$"` is read as USD. `low_confidence` is renamed `billing.currency.unreliable`. |

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
