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
| tip `e28ebc8a4` | 39,629,979 B (37.79 MB) | 192 | 20,702,208 |

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
8. **Prior lines are not narrowed** (D-112 says do not restrict the loader): all lines of all the firm's non-failed priors are read, in
   pages, two queries per 500 invoices. Known limit: a firm with very many prior invoices makes this read large (perf, not correctness).
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
