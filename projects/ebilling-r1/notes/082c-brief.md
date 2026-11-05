# 082c brief: loader and evaluator re-keyed onto `ProcedureResolver`

> Status: **in review** (2026-11-04). ontology-r1 reviews 082c-1 (C1 deviation, decision (a) open question, C6 note). uac-r2 (approved with conditions, §7) and External Access R4 review the warning shape (c) before 082c-2 is coded. Nothing is implemented yet.
> Base: `work/ebilling-r1` at c4b00b5fd7 (082b merged; 089e merged since, at 6bb7d928a9). Paths are under `src/server/api/Sprk.Bff.Api/` unless stated. `SB/` = `Services/Signals/Billing/`, `SF/` = `Services/Finance/`.

## 1. Scope

**Files to change (anchors at c4b00b5fd7)**
- `SB/BillingFactLoader.cs`: `LoadAsync` :265-293 calls `ProcedureResolver.ReadAsync` once, beside the legacy read (decision a). The Duplicate gate at :277-283 looks at both streams. `LoadRulesAsync` :301-430: the null filter :313 and the re-check :327 (C1). `BillingRuleCandidate` :81-153 gains `Source`, `Rank`, `ProcedureId?`, `ProcedureVersionId?`. `BillingLoadedFacts` :156-173 gains `Procedures` (the `ProcedureResolution`).
- `SB/InvoiceComplianceEvaluator.cs`: subject dates :191-196; gate :202-231; D-39 precedence :283-312 (reads `Scope` at :290, :299; replaced by rank and classification); coverage :320-338 and `RefusalScopeOf` :441-463; new `BillingEvaluationResult.Gaps`.
- `SB/ProcedureResolver.cs`: `PrintMembers` on :95, :175, :181, :185; an internal accessor returning every policy version read (for the Duplicate gate).
- `SB/CoreRecordChain.cs` :28: `PrintMembers` on `CoreRecordLink`.
- `SB/BillingSystemFindings.cs`: two builders (the `FirmMissing` pattern, :167).
- `SB/BillingFinding.cs`: reason codes near :198.
- `SB/BillingRuleCatalogue.cs`: tokens :256-258, `ReasonTemplate` :307, `ReasonHeadline` :456, `IsFlagOnlyReason` :487.
- `SB/BillingFindingSignalMapper.cs` :427: an order entry per new code.
- `SF/Submission/InvoiceSubmissionProcessor.cs`: `Classify` :235-252; step-4 call :201; a `finalDelivery` parameter. `SF/Submission/IInvoiceSubmissionProcessor.cs`.
- `Services/Jobs/Handlers/InvoiceSubmissionProcessingJobHandler.cs` :325: pass `job.IsFinalDelivery`.
- `SF/Submission/InvoiceSubmissionAfterSteps.cs`: cases :1573-1577, :1584-1603.
- `SF/Submission/InvoiceSubmissionPersister.cs`: `TerminalFor` :1809; `RulesNotChecked` :1659; line cause :2006-2010; `Validate` :2224-2240. **Conflicts with 095d-1b: keep each change a small hunk.**
- `SF/Submission/SubmissionReasonCodes.cs`; `SF/Submission/InvoiceSubmissionContracts.cs` :252 (view); `SF/Submission/InvoiceSubmissionService.cs` :2368-2385.
- `SF/InvoiceApprovalService.cs` :533-538: a hold check.
- `SF/InvoiceAdjustmentService.cs` :191-192, :211, :288-304.
- `SF/InvoiceCorrectionReevaluation.cs` :204-205 (C12 interim).
- `Infrastructure/DI/FinanceModule.cs` :79-86: comment only (the resolver is static; no registration).
- Client (082c-3): `src/client/shared/Spaarke.UI.Components/src/components/InvoiceSubmission/{types.ts :83-93, InvoiceSubmissionOutcome.tsx :236-255}`.

**Every `BillingFactLoader.LoadRulesAsync` caller**
1. `LoadAsync` :272: legacy plus resolver.
2. `InvoiceAdjustmentService` :211 (the `LoadRules` delegate): re-key to `(firmId, CoreRecordReference? core, DateOnly date, ct)`, returning one version per policy in force on that date (legacy `SelectVersion` plus the resolver's `RulesOn(date)`). The core record comes from the invoice's lookups (:179-181): exactly one set, use it; otherwise `Unavailable` (the fixed text at :267-271). No precedence (a reviewer may cite any rule in force). A Procedure rule's DTO `Scope` is `"Procedure"`.

`RuleSetService.LoadRulesAsync` (:2543) is a different private method: not touched.

**Must NOT change:** `ReadSystemPolicyAsync` (`InvoiceSubmissionAfterSteps.cs:1337-1413`; `BILL-SYSTEM-SUBMISSION` keeps its `sprk_currentversion` chain, D-125); the evaluator's system-type skip (:180-181); `PrecedenceKey` (`BillingRuleDefinition.cs:107-110`, D-39); the resolver's semantics (082b reviewed); `HeldSubmissionConditions` (`FinanceBaseValues.cs:179-184`, spend rollups); `SubmissionRejectionRule` logic.

## 2. Acceptance criteria (closed set)

**Loading and precedence**
- **C1, 081 MUST-DO.** Procedure rule sets are loaded and evaluated through the resolver; the loader no longer skips them. **Literal deviation (ontology to accept):** under (a) the legacy read keeps `sprk_procedure IS NULL` (:313, :327) as a partition, not a skip; renamed `LoadLegacyRulesAsync` with its comment changed. Why: Procedure sets are written with scope ClientDefault or Firm (`RuleSetService.cs:2149-2151`); without the partition the legacy read would apply an "Assigned records only" Procedure to every invoice.
- **C2, must-fire.** A rule of an Active Procedure Version raises a finding; fails if the resolver call is removed.
- **C3, must-fire.** An "Assigned records only" Procedure with no assignment on the chain gives no finding and no gap. Second must-fire: the same set with scope ClientDefault is not loaded by the legacy read (fails if the partition is removed).
- **C4.** The streams are disjoint: the same `PolicyVersionId` from both throws.
- **C5, rank map.** Legacy ClientDefault (AllRecords, 0, false); legacy Firm (Firm, 0, true); Procedure rule: its `AppliedProcedure.Rank`; record rule: its base rule's rank. Precedence reads `Rank` only, never `Scope`.
- **C6, precedence by `PrecedenceOf` (model 7.3),** per subject and per `PrecedenceKey`. Replace: versions below the highest *valid* rank (gate-accepted, handler-accepted, not downgraded) are displaced; a tie means all apply; an invalid higher version displaces nothing. Additive: all apply. Split (ExpenseRule): a displaced version keeps its code check and loses its `ExpenseAmountOverCap` findings. *Note for ontology:* "per category" has nothing to key on; ExpenseRule has no category field (`BillingRuleDefinition.cs:24-25, 66-69`).
- **C7, K3.** `PrecedenceOf` is called only on a gate-accepted definition. A stored row of a non-billing type is a `GateRefused` refusal, not an exception; the other rules still run.
- **C8, coverage (101 B2a).** A refused version's subject counts as covered only for a Replace key judged at an equal or higher rank. For Additive and Split its lines stay `rules.not_checked`.
- **C9, dates.** `ReadAsync` once per invoice; `RulesOn(d)` memoized per distinct subject date (line date, else invoice date, else `ReceivedOn`). A Procedure candidate never goes through `IsCurrent`, `IsInForceOn` or `SelectVersion`. Query count for a 1-line invoice equals that for 150 lines.
- **C10.** The Duplicate prior load (:277) fires when either stream holds a Duplicate rule.

**Gaps**
- **C11, gap hold.** One `SubmissionPipelineFinding(ProcedureGap)` per gapped Procedure (name, first date, kind), mapped to a blocking `BILL-SYSTEM-SUBMISSION` finding with the headline "No procedure associated with this invoice". Every line dated in a gap gets `rules.not_checked` with cause `ProcedureGap`. Internal: Blocked, reason `Blocked`. SPA: Accepted / AcceptedUncoded with reason `ProcedureGap`, never AutoRejected, even when 073 rejected; the gap reason wins over a coding code. Approval refused with `ebilling.approve.procedure_held` until a re-check clears the gap. The hold does not depend on the system policy's state (a missing policy loses only the Signal, :1300-1309).
- **C12, correction (101), interim.** If the after-side of a correction has a gap or resolution fault the before-side lacks, `PatchFindingsAsync` throws; the correction stays unapplied, which already holds approval (`InvoiceCorrectionReevaluation.cs:127-128`). A new gap hold through a correction is deferred to 082d / 101.
- **C13, the 082b criteria.** A hole, an end with no successor, a Retired pin, a pin not in force and conflicting parents each give exactly one finding. Work before the first version is not applicable: no finding.

**Resolution faults**
- **C14, ontology-r1 ruling (binding) and O2.** For `ProcedureResolutionFaultException` or `CoreRecordChainUnreadableException`: not the final delivery → rethrow, the job returns Failure and retries; no persist, no hold, no reader warning (only the status write to Processing). Final delivery → held: Internal Blocked; SPA Accepted / AcceptedUncoded with reason `ProceduresUnresolved`; a blocking internal finding with the cause, no rule finding at all, every line `rules.not_checked`. Never judged on [W0] alone; never passed. A raw Dataverse read fault keeps today's path (Failed on the final delivery; not a pass).
- **C15, K3 cause.** `chain_unreadable` (permanent) or `rows_inconsistent` (may clear once a change completes).

**Redaction**
- **C16.** No `ex.Message` or id-bearing `ToString()` reaches a reader. Surfaces: `sprk_findingsjson` (findings, `pipelineFindings`, plan); Signals (sentence, facts, evidence); the review read model; the SPA status view; the 202 body; `sprk_adminattention`; Decision Record facts; the submitter notification; correction and adjustment responses. Logs carry the type name only (as `JobHandler.cs:208`). Facts are built from codes and `ForReaders()` / names only.
- **C17, `PrintMembers` (yes), each with a test that `ToString()` holds no id, no rule body and no reason:** `ExcludedProcedure` (name, override count); `ExcludedRule` (code, Procedure name, distance, replaced flag, override count); `ResolvedRule` (code, Procedure name, version number, rank, record-rule flag); `ResolvedPolicyVersion` (code, version number, rule type, from-date); `CoreRecordLink` (table, distance).
- **C18.** The external view never carries `pipelineFindings` or a Procedure name (R3, model 7.4).

**Handed on or recorded**
- **C19, 089e hand-off: own slice.** The Superseded close should select through the chain (d1 children's invoices, respecting nearer rows); today it covers d0 only. It needs a reverse (parent-to-children) chain read that `CoreRecordChain` lacks. 089e is now merged (6bb7d928a9); this becomes its own slice after 082c-1.
- **C20, rollout checklist.** K5: count record rules whose Published version lacks `sprk_basedonversion`; backfill or retire. The census in (b). Count legacy sets per environment.
- **C21, known limit (D-203(11), confirmed by ontology).** A version published with no Accepted rule and later retired gives no hold. No 081 refusal in 082c.
- **C22, release dependency.** `SubmissionOverrideService.cs:700` refuses an Override when `DecidingIndexes.Count == 0`, so an invoice Blocked only by a gap cannot be overridden yet. Task 114 must lift that (and the SPA hold release). **082c must not reach master without 114.**

## 3. Decisions (proposed)

**(a) Keep loading the legacy sets beside the resolver (recommended).** 081's notes (:143) turn a legacy rule off "in the same transaction as the publish that puts its copy in force… none of this happens before 082", which assumes legacy rules keep running. Dev today: keep = invoices judged exactly as now (no Procedure sets on dev; the dev BFF is master and 081 is not deployed); drop = every dev invoice judged by no billing rule (system findings only), a silent pass, so drop is rejected. No seed creates legacy sets (`Seed-BillingSystemPolicy.ps1:182,384`); no rule-set switch; `POST /rulesets` still creates legacy sets (`RuleSetEndpoints.cs:75`).
**Open for ontology-r1:** C6 makes legacy Additive types add, where today a firm rule replaces the client-default one for every type (evaluator :82-87). Stricter; may raise new RequiredData rejections. Alternative: keep D-39 "all replace" between legacy sets only.

**(b) A linked project with an empty pair on a non-dev environment: no runtime fault (recommended).** A read-only census per environment as a rollout gate (the 082a census query, notes/082 :150-159); above 0, backfill the pair with the owner's go. Rejected: a runtime fault (an N:N read on every project chain, breaks 082a's one-read contract, holds every such invoice); silent acceptance (the matter's Procedures would be lost).

**(c) The SPA warning shape (for uac-r2 and the SPA owner).**
- Codes: `SubmissionReasonCodes.ProcedureGap = "ebilling.submission.procedure_gap"`, `ProceduresUnresolved = "ebilling.submission.procedures_unresolved"`. Finding codes `submission.procedure_gap`, `submission.procedures_unresolved`, both flag-only. Tokens `procedure.name`, `procedure.workDate`, `procedure.gapKind`, `procedure.cause`.
- Status view (both GET routes): `InvoiceSubmissionView.Warning { code, message }`, omitted when null, derived in `InvoiceSubmissionService.cs:2385` from `OutcomeReasonCode` with fixed text (no stored text read). Never names a Procedure.
  - gap: "We received this invoice and it has not been rejected. It is on hold for review because no billing terms are set up for some of its work dates on the record you submitted it to. Check that you chose the right record and that the work dates are correct. If both are right, you do not need to do anything; the client will follow up."
  - unresolved: "We received this invoice and it has not been rejected. It is on hold for review because its billing terms could not be checked. You do not need to do anything."
- Submit response: `InvoiceSubmissionAccepted.Warning` (`InvoiceSubmissionEndpoints.cs:1210`) from an advisory pre-check on the SPA channel only (`RulesOn(received date)` for the core record and firm). Text: "As of today no billing terms are set up for the record you chose. If the invoice's work dates are not covered either, it will be held for review, not rejected." A pre-check fault omits the warning and never refuses the submit. Rejected: status view only (the owner named both surfaces; the SPA shows the polling outcome right after the 202, `InvoiceSubmitPage.tsx:183,210`; external callers get no notification, `SubmissionOutcomeNotifier.cs:124-130`).
- `Classify(decision, channel, gap)`: gap + Internal → Blocked; gap + SPA → Accepted with `ProcedureGap`; otherwise unchanged. 073 unchanged: the gap is a pipeline finding and never reaches `Decide` (the flag-only listing at `SubmissionRejectionRule.cs:285` is defence in depth).

**(d) Gap kinds: one finding code (recommended).** The kind goes in `procedure.gapKind` from a closed map: "no version in force between versions", "after the last version ended", "the pinned version is retired", "the pinned version is not in force", "parent records choose different versions". `HoleBetweenVersions` cannot tell a Retired window from a real hole without a resolver change (`ProcedureResolver.cs:190-191`), and the partner must see no difference. Rejected: five codes.

## 4. Test scope

The contract C1-C18, plus: order-swap determinism; Replace tie; a refused higher rank keeps the lower; a downgraded higher rank keeps the lower; AddedFee keys differ; Split cap displaced but codes kept; legacy and Procedure rules of the same key; an undated line falls back to the invoice date; a gap on the invoice date only; SPA with gap plus a 073 rejection → Accepted and held; system policy missing still holds; adjustment options on a Procedure invoice; the correction interim (C12); the query-count bound; C14 in two halves: (1) first delivery throws, retry succeeds → no persist on the first, Accepted with no hold and no unresolved finding; (2) final delivery throws → held with the finding, both channels.

**Seeding proofs (fail-closed guards only):** (1) the gap hold dropped from `Classify` → the C11 tests fail; (2) the final-delivery catch made catch-always → C14(1) fails; (3) `ex.Message` planted into the unresolved finding's facts → a sentinel scan over the findings JSON, plan, view and admin attention fails.

## 5. Split

1. **082c-1 (ontology review).** Loader, evaluator, `PrintMembers`, the adjustment caller, C12, gaps exposed on the result. Interim fail-closed rule: the processor throws on any gap (transient → Failed after retries), removed in 082c-2. Covers C1-C10, C12, C17.
2. **082c-2 (uac-r2 and the SPA owner see the shape first).** Classifier, codes, system findings, after-steps, persister hunks, approval hold, final-delivery hold, status-view warning. C11, C13-C16, C18.
3. **082c-3 (SPA owner).** The 202 pre-check and the client warning render.
4. **089e chain close (C19)**, its own slice.

082c-1 through 082c-3 and 114 go to master together.

## 6. Review plan

One adversarial pass per slice (tenant isolation and fail-open; C16 and C18 the focus). ontology-r1 owner review of 082c-1, including the C1 deviation and the (a) open question. uac-r2 and the SPA owner review (c) before 082c-2 is coded.

**Not verified by the brief:** the legacy set count on dev (no Dataverse read); how `BuildFindingsJson` shows a finding with `rejects=true` under the Accepted kind (needed for C11 on SPA; add a test).

## 7. Review answers

### uac-r2 (2026-11-04): shape (c), C11, C14, C16, C18, C22 approved, with conditions (binding)

1. **The SPA 202 pre-check** (app-only Procedure reads for an external caller) is allowed only if:
   - (a) it runs only AFTER the submission's own authorization has passed for that caller;
   - (b) the core record and firm it asks about come from the authorized submission context (the record the caller is granted to submit for, and the caller's own firm), never from request fields the caller can vary independently. **Test:** a body naming another record or firm changes neither the warning nor the outcome;
   - (c) the answer is one fixed sentence: gap or no warning is the only variation; no counts, dates or Procedure names;
   - (d) a fault omits the warning and is never a distinct answer.
   - **Known limit (record it):** the warning tells the partner that the record's billing terms for that date are incomplete. The owner's rule requires that; it names nothing.
2. **Two codes: OK.** On the SPA channel uac-r2 prefers one generic warning for both, unless the owner's rule needs them told apart. **Proposed (082c-2):** keep the two texts, because they ask different things of the partner (the gap text asks them to check the record and the work dates; the unresolved text asks nothing). Condition if separate: "unresolved" must not be causable on purpose by the caller. `rows_inconsistent` comes from an internal admin change; `chain_unreadable` comes from the record's stored parent data, which the partner cannot write. 082c-2 adds a test or a written argument for each cause, and falls back to one generic text if either is caller-reachable.
3. **C16, C17, C18: agreed as written.** Add one ArchTest or source scan for C16 (no `ex.Message` and no `ToString()` of the resolver records on the listed reader surfaces), with a must-fire control.
4. **C22 is the binding pre-merge gate:** 082c does not go to master until task 114 lifts the `DecidingIndexes.Count == 0` refusal. uac-r2 checks it on the master PR.
5. **SPA owner:** R3 is closed and handed over to **External Access R4** (session `spaarke-wt-spa-external-access-platform-ad`, worktree spaarke-wt-SPA-external-access-platform-r4, in design). §3(c) with uac-r2's conditions sent to R4 on 2026-11-04. R4 asked to be copied on anything that changes the SPA invoice or record-modal UI (082c-2, 082c-3).

### ontology-r1 (2026-11-04, D-205): brief accepted with conditions (binding); 082c-1 may start

1. **C1 partition accepted** as a literal deviation: Procedure sets go through the resolver and are never skipped (C2/C3 must-fire, C4 throws on overlap). **075 note:** the partition is removed together with the legacy read at copy-forward.
2. **(a) keep legacy sets: agreed. Open question answered: apply `PrecedenceOf` to legacy sets too** (limits replace, checklists add); do NOT keep "all replace". This is the owner's D-172 Q7 (2026-10-20, "for checklist types the general and specific items BOTH apply"), carried into the rank model by D-177(12); the evaluator's D-39 all-replace (:82-87) never implemented it, so C6 closes that drift. **Condition (rollout):** before the deploy, count the dev legacy sets where a Firm set and a ClientDefault set share an Additive precedence key (RequiredData, NonBillableActivity, ApprovedTimekeeper, the ExpenseRule checklist); put the count in the deploy notice; if not 0, the owner hears before the deploy.
3. **C6 ExpenseRule split: agreed.** A displaced version keeps its code check (adds) and loses only its `ExpenseAmountOverCap` findings (the cap replaces). The C6 code comment says "per category" has no key today (`maxAmountPerItem` caps every expense line) and becomes per category only if the owner's six-gap triage adds a per-code cap.
4. **(b) census rollout gate: agreed, with two conditions:** the census is re-runnable (a `-Verify`, or part of an existing census or report script), not a one-off; and a create-path test (new or pointed to) proves a new project under a matter always gets its pair, so the count cannot grow after rollout.
5. **(d) one code with a kind token: agreed.** The kind map is closed; an unmapped kind throws when the finding is built (like `RuleDomains.DomainOf`); never a blank or free-text token.
6. **New C11 criterion (Signal side, ontology):** the ProcedureGap blocking finding maps under `BILL-SYSTEM-SUBMISSION`, so it joins the shared SubmissionIntegrity Signal per (policy, invoice), as in the D-201 amendment for firm_missing. A re-check that clears the gap re-maps the group: the title is recomputed, or the Signal closes `ConditionCleared` when no reason remains. A 114 Override release closes it through the billing decision path with a Decision Record. **Test:** a gap plus firm_missing on one invoice give ONE Signal; clearing the gap alone keeps it open with the title recomputed. Plus the mapper order entry.
7. **Accepted as written:** C14 (matches the transient-fault ruling), C21, C22, C11's "the gap wins over a 073 rejection on SPA", C16/C18 and the seeding proofs.
8. **For the 082c-1 review, send ontology:** the diff, the C6 precedence tests and the legacy-overlap count.

### External Access R4 (2026-11-04): shape fits; client change required (082c-3); wording needs the owner's OK

1. **Fit.** R4 does not change the submit wizard or the outcome screen; the record modal opens `InvoiceSubmitPage` from "Submit invoice" on Matter, Project and Work Assignment.
2. **Row status (R4 question, answered from code; OPEN with the owner: whether the partner's grid/record shows a held state distinct from In Review; R4 reports every SPA invoice surface reads the `statuscode` label through the generic external fetch, so the options are a new status reason or a special case in that fetch).** An Accepted SPA submission creates the `sprk_invoice` row at statuscode **In Review** (`InvoiceStatusCodes.InReview`, statecode 0). The firm-missing hold also sets `sprk_firmhold`, which is field-secured (095c-5) and not partner-readable; the gap hold is a blocking internal finding, not partner-visible. So the row alone shows "In Review", never "on hold". **082c-2 adds:** the partner-facing invoice read (the SPA invoice grid / R4 invoice record source) derives a held state from the latest submission's reason code (`procedure_gap`, `procedures_unresolved`, `firm_missing`) as fixed text "On hold for review", with no new column and no internal detail. R4 confirms the surface it reads.
3. **Client render (082c-3, required).** When `view.warning` is present: ONE `intent="warning"` MessageBar in place of the green Accepted bar (not stacked), title "Received: on hold for review", body `view.warning.message`, the tracking-number sentence kept; polite status region, not `role="alert"`; branch on `warning.code` only in tests (one copy of the wording, the server's). `warning?: { code: string; message: string } | null` on both `InvoiceSubmissionStatusView` and `InvoiceSubmissionAccepted` in types.ts. The firm hold gets the same treatment (filed #2567, lands with 082c-3).
4. **The 202 warning is provisional.** Shown as a quieter caption-level note under the Processing state; dropped when the terminal view arrives (the status view's warning, or none, is authoritative). The pre-check judges today, the hold judges the work dates. The Outlook add-in consumes the 202 and the status view: it renders the warning or at least does not drop it (notify word-add-in with 082c-3).
5. **Wording (R4 edits): APPROVED by the owner 2026-11-04 ("Approve as written").** The 202 note keeps the reviewed text below.
   - gap: "We received this invoice. It has not been rejected, but it is on hold for review: no billing terms are set up for some of its work dates on the record you chose. Check that you chose the right record and that the work dates are correct. If both are right, you don't need to do anything; the client will follow up."
   - unresolved: "We received this invoice. It has not been rejected, but it is on hold for review because its billing terms could not be checked. You don't need to do anything; the client will follow up."
   - 202: "As of today, no billing terms are set up for the record you chose. If the invoice's work dates are not covered either, it will be held for review, not rejected."
   - Two codes: fine with uac-r2's condition.
6. **Filed (pre-existing, not 082c):** #2566 (a Rejected outcome shows the raw reason code), #2567 (the firm hold shows the green Accepted bar).
7. R4 is copied on the 082c-2 and 082c-3 PRs; R4 records this in its design.md §7.

### Owner decision (2026-11-04): held stays "In Review", with a description of which kind of review

Owner, verbatim: "keep 'In Review' there should be some explanation description that provides details of the diffrent types of 'In Review' ; but "On Hold' is a status when it is not being actively worked".

- **No new status reason.** A held invoice stays statuscode In Review. "On Hold" is reserved for a status meaning *not being actively worked*. It is not this hold and is not added by 082c.
- **New 082c-2 item: a partner-readable review detail on the invoice.** Proposed: a choice column `sprk_reviewdetail` ("Review Detail") on `sprk_invoice`, closed options:
  - (blank) = normal review;
  - "Waiting for billing terms" = the Procedure gap;
  - "Billing terms could not be checked" = procedures_unresolved;
  - "Waiting for the firm to be confirmed" = firm_missing.
  It is added to `ExternalAccessModule` `ReadableColumns` for `sprk_invoice` (the SPA shows its formatted label beside the status), and internal views show it too.
  - **Ownership (one writer per invariant):** the submission persister sets it when it creates a held invoice. Whatever clears the hold clears it, in the same transaction: a re-check that clears the gap, the 114 Override release, and the 095d Set firm apply. No other writer; an ArchTest names the writers.
  - **§11.** Existing: `statuscode` (the owner keeps it In Review); `sprk_firmhold` (field-secured, internal, firm case only). Extension: `statuscode` cannot carry it by the owner's decision, and `sprk_firmhold` is not partner-readable by design (095c-5). Cost of doing nothing: the partner's grid and record say "In Review" with no explanation, which is what the owner asked to fix.
  - **Needs:** uac-r2 review of the `ReadableColumns` addition; R4 for the render (grid column or record field); ontology on the lifecycle writers; the owner's go for the dev column apply (entity-scoped publish).

**R4 answer (2026-11-04): render places and ownership.**
- **Invoices grid:** a separate "Review detail" column right after Status, blank for a normal review. Not merged into the Status cell: a combined cell would need a custom DataGrid renderer and would break sorting and filtering on status.
- **A record's Invoices section** (Matter and Project; `RecordDetailModal.tsx` `INVOICE_COLUMNS` ~:365-376): `sprk_reviewdetail` added, so the section matches the tab.
- **Invoice record (R4 owns it):** the header status reads "In Review" with the review detail as its secondary line; an Information field "Review detail"; one fixed explanation line per option (below). "Your access" names the caller's rights, not the invoice's state, and does not use it.
- **Ownership and order:** ebilling owns the column, its writers and the allow-list entry. R4 owns the record rendering. If 082c reaches master before R4's modal work, **082c-3 adds the grid column and the `INVOICE_COLUMNS` entry**. The BFF ships first; the SPA treats a missing value as blank, so either deploy order is safe. R4 says the labels are partner-safe.
- **Explanation lines (one per option, sent to R4; the first three reuse the owner-approved status-view openings; the firm line is new wording for the owner to confirm with 082c-2):**
  - blank: "The client is reviewing this invoice."
  - Waiting for billing terms: "This invoice has not been rejected. It is on hold for review because no billing terms are set up for some of its work dates on this record. If the record and the work dates are right, you don't need to do anything; the client will follow up."
  - Billing terms could not be checked: "This invoice has not been rejected. It is on hold for review because its billing terms could not be checked. You don't need to do anything; the client will follow up."
  - Waiting for the firm to be confirmed: "This invoice has not been rejected. It is on hold for review until the client confirms which firm sent it. You don't need to do anything; the client will follow up."

**ontology D-205 on the `sprk_reviewdetail` lifecycle (binding; supersedes "set, then clear" above).**
- **Derive, never set or clear on one writer's say-so.** An invoice can hold several reasons at once. One pure function `ReviewDetailOf(openHoldReasons)` with a fixed priority: (1) "Waiting for the firm to be confirmed" (without the firm, Procedures cannot resolve); (2) "Billing terms could not be checked"; (3) "Waiting for billing terms"; blank = none. Every writer recomputes from the invoice's REMAINING reasons in its own transaction.
- **Writers (all named in the ArchTest):**
  1. the submission persister (held create);
  2. the re-check that clears a gap or unresolved (082c-2, the 101 correction re-evaluation, later the ebilling-r2 explicit re-check);
  3. the 095d-1 Set firm apply (removes firm_missing; with a gap still open the value moves to the next reason, not blank);
  4. the 095d-2 re-run (re-maps the SubmissionIntegrity group; where the firm hold really lifts);
  5. the 114 Override release;
  6. every exit from In Review sets blank (reject, return to firm, cancel): a partner never reads a hold reason on a closed invoice;
  7. Superseded by a correction (097, the old invoice closes in the new one's commit);
  8. the 089e chain close (C19). *ebilling to confirm in C19 whether that path changes an invoice's status or only closes Signals; it is a writer only if it ends In Review.*
- **Invariant:** non-blank ⇒ statuscode In Review. Checked in the census or a `-Verify`, plus a unit test on the derivation (every reason combination maps to its priority value).
- Writers 3 and 4 make 095d a consumer: 095d-1b (Set firm) is not yet merged; the recompute lands with 082c-2 or with 095d, whichever adds the column second.
