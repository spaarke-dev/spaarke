# Pre-pipeline context: held PRs #2580 (server) and #2579 (client)

Generated 2026-11-06 from the PR heads (read-only; `git diff origin/master...<branch>`, `git show origin/<branch>:<path>`).

| PR | Branch | Head SHA |
|---|---|---|
| #2580 (task 174, server) | `origin/task/174-billing-refusal` | `9423f370045cde4f747fa52c2c9a7e4ee09b2b5c` |
| #2579 (task 175, client) | `origin/task/175-invoice-decisions-card` | `ab04492ab7f8fdb957db47f4cdf0d7e2e02d763a` |
| this worktree HEAD (work/spaarkeai-mcp-bff-api-r1) | | `53dbc7233045695ce643a268ccf60017162e24e8` |

Line numbers below are on the PR head. Both PRs ship as a pair and are held until ebilling task 112 puts `sprk_invoicereviewpage` on dev (PR #2580 completion note; spec §Sequencing and the open item "PR #2580 timing").

## 1. PR #2580 (server, task 174, D-207 / EB-D-194)

Production files changed (4), all under `src/server/api/Sprk.Bff.Api/`. No new endpoint, service, DI registration or package. Writers and `DecisionCallerRights` untouched.

### `Services/Signals/DecisionCommitService.cs`
- `using Sprk.Bff.Api.Services.Signals.Billing;` added (line 19).
- New reason code `DecisionCommitReasons.DecidedInInvoiceReview = "ontology.decision.decided_in_invoice_review"` (line 245). Raised in one place only (below). Status is `DecisionCommitStatus.Conflict`, which `DecisionCommitEndpoints.ToHttp` already maps to 409.
- New step **(1c-billing)** inside `CommitAsync` (lines 974-996). `CommitAsync` is at line 439; its signature is unchanged: `public async Task<DecisionCommitResult> CommitAsync(Guid signalId, DecisionReview review, HttpContext http, CancellationToken ct)` (HEAD: same signature at line 432).
  - Position: after (1a) per-Signal caller readability (a non-reader still gets the uniform 404) and after (1c) the app-only row read that fills `rows`; immediately before `var finishing = ...` and `PrepareAsync`, so before the caller-rights probe (FR-51), any executor preflight, and every write.
  - Condition: `existingRecord is null && rows.Any(r => IsBillingSubject(r.SubjectEntity))`. `rows` covers the primary and every `AlsoResolve` Signal, so one billing Signal refuses the whole review.
  - Inputs/reads: only `rows[].SubjectEntity` (already read) and `existingRecord` (the Decision Record found by caller + review id in step 1b, already checked to cover these Signals and content). No new read, no new caller-token ask. Nothing in the request body decides "recorded".
  - Result: `new DecisionCommitResult { Status = Conflict, ReviewId = reviewId, ReasonCode = DecidedInInvoiceReview, Detail = "Invoice and invoice-line items are decided in Invoice Review. Open the invoice there; nothing was done." }`.
  - It sits inside the lease region (locks and the in-progress marker are taken before 1c); nothing is written before it (PR completion note).
- **Replay guard behaviour** (no code in the replay path changed): the refusal is skipped when `existingRecord is not null` (a review recorded before D-207 must still finish its closes). A real ledger replay goes through `CommitLeasedAsync` and never reaches 1c. A finishing replay (ledger forgot, record stays) passes via `existingRecord`. A replay-shaped request with no recorded review is still refused.
- New `internal static bool IsBillingSubject(string? subjectEntity)` (line 1684): case-insensitive compare with `BillingFactLoader.InvoiceEntity` / `BillingFactLoader.LineEntity`. Its doc comment says it is "shared with the worklist route", but the `SignalReadService` diff does not call it (it compares catalog table names itself).

### `Api/Signals/DecisionCommitEndpoints.cs`
- Only the `WithDescription` text changes (lines 35-38: the 409 now also covers an invoice / invoice-line item, `ontology.decision.decided_in_invoice_review`). `ToHttp` (line 61) is NOT changed: `Conflict or InProgress => 409` (line 76) already carries the new code through `result.ReasonCode`.

### `Services/Signals/EarlierInvoiceReadability.cs`
- New `internal const string DuplicateCheck = "Duplicate earlier-invoice check"` (line 191) and `DuplicateConsequence` (line 193).
- `ReadAsCallerAsync(IDataverseUserClient caller, string relativePath, string table, CancellationToken ct, string check = DuplicateCheck, string consequence = DuplicateConsequence)` (line 202): the three Warning logs now use `{Check}` / `{Consequence}` placeholders. Default arguments reproduce the old D-176 text. Behaviour unchanged.

### `Services/Signals/SignalReadService.cs`
- `WorklistItemRow` (record, around lines 119-141) gains a last member `WorklistInvoiceReview? InvoiceReview = null` (line 141). **The list row type in the repo is `WorklistItemRow`; there is no type named `WorklistItemSummary` on HEAD or in the PR** (B-5 creates it).
- New `public sealed record WorklistInvoiceReview(Guid InvoiceId, Guid? LineId)` (line 145). JSON: `invoiceReview: { invoiceId, lineId }`; `lineId` is null for an invoice subject and is the subject id for a line subject; the member is null/omitted on every other item and on a line whose invoice cannot be named (never an `invoiceId` that is not known).
- Worklist pipeline: new step **(9a)** `ReadLineInvoicesAsync(visible, catalog, ct)` (call line 442; method around line 1773). It reads `sprk_billingevent?$select=sprk_billingeventid,sprk_invoice&$filter=<In list>` AS THE CALLER, once per response (chunks of `WitnessIdChunk` ids), via `CallerLookupAsync`. It returns an empty dict with no read when there are no line subjects, and null on a fault (logged once: label "Worklist invoice-line read" plus a consequence text). It runs after the witness rule (9) and before the Duplicate rule (9b).
- (9b) `ApplyDuplicateRuleAsync(rows, duplicates, catalog, Dictionary<Guid,Guid>? invoiceOfLine, ct)` and `DuplicatesShownAsync(..., invoiceOfLine, ct)` take the shared line→invoice map instead of reading `sprk_billingevent` again (line 1578: `var invoiceOfEvent = invoiceOfLine;`). D-176 fail-closed behaviour kept: null still withholds.
- `CallerLookupAsync(entitySet, key, lookup, ids, table, ct, string check = ..., string consequence = ...)` forwards the label/consequence to `ReadAsCallerAsync`.
- New step **(12)** `ApplyInvoiceReview(visible, catalog, lineInvoices)` (call line 482; after (11) `ApplyPolicyColumnsAsync`, before the INFO log). Invoice subject: `new WorklistInvoiceReview(subject, null)`. Line subject with a known parent: `(invoice, subject)`. Line with no parent: omitted, ids logged once at Warning. Fault: omitted for every line, route still 200.
- Private `VisibleRow` gains `WorklistInvoiceReview? InvoiceReview = null`; the row mapping (around line 1969) passes `visible.InvoiceReview` as the last constructor argument.

## 2. PR #2579 (client, task 175, D-207)

Production files changed (13 plus the task poml). Paths relative to `src/client/shared/Spaarke.UI.Components/src/components/` unless stated.

- `Worklist/types.ts`: `WorklistItem.invoiceReview?: InvoiceReviewTarget | null` (line 63); new `interface InvoiceReviewTarget { invoiceId: string; lineId?: string | null }` (line 67); new `hasInvoiceReview(item)` = `Boolean(item.invoiceReview?.invoiceId)` (line 75). The `WorkType` union is only reformatted.
- `Worklist/worklistService.ts`: new `parseInvoiceReview(raw)` (line 237) returns `{ invoiceId, lineId }` or null when there is no `invoiceId`; `parseItem` spreads `...(invoiceReview ? { invoiceReview } : {})` (line 271). Wire contract = #2580's `invoiceReview { invoiceId, lineId }`.
- `Worklist/laneCountFilters.ts`: `LaneFilterKey = 'all' | 'invoiceDecisions' | 'other' | WorkType` (line 32). `bucketLaneItems` (Decide lane only) pushes `{ key: 'invoiceDecisions', label: 'Invoice decisions' }` right after All when it holds an item (lines 69-71); work-type cards and Other are computed over `rest` (billing excluded), so the non-All cards still partition the lane. Do lane unchanged. **Yes: #2579 adds an "Invoice decisions" card / category key.**
- `Worklist/openInvoiceReview.ts` (new, 61 lines): exports `INVOICE_REVIEW_WEBRESOURCE_NAME = 'sprk_invoicereviewpage'` (21), `INVOICE_REVIEW_FAILED = 'Invoice Review could not be opened'` (24), `buildInvoiceReviewLaunch(target)` (33), `openInvoiceReview(target)` (52). Link logic: `cleanGuid` (strips braces, trims, lower-cases) on both ids; `data = "invoiceId=" + encodeURIComponent(invoiceId) + (lineId ? "&lineId=" + encodeURIComponent(lineId) : "")`; `pageInput = { pageType: 'webresource', webresourceName: 'sprk_invoicereviewpage', data }`; `navigationOptions = { target: 2 (dialog), position: 1, width/height 90% }`. `openInvoiceReview` throws without an `invoiceId` or when `Xrm.Navigation.navigateTo` is missing (via `getXrm('navigation')`). It builds a `navigateTo` dialog argument, NOT a URL; the `data` string is raw (`invoiceId=..&lineId=..`) because the platform encodes it (the spec's FR-12 `data=invoiceId%3D..%26lineId%3D..` is that string encoded once more inside a URL).
- `Worklist/WorklistWidget.tsx`: new optional prop `openInvoiceReview?: (target: InvoiceReviewTarget) => Promise<unknown>` (92, destructured 154). New `openReview` callback (277-294): bumps `opens.current.seq`, clears the notice, shows the `INVOICE_REVIEW_FAILED` warning when the prop is absent or rejects, and always `reload()`s after the promise settles. `openWizard(rawEvent, sourceSet)` now first checks `findItem(...).item.invoiceReview` and calls `openReview` instead of the wizard, and filters billing ids out of `event.visibleList` (the browse set). `startLane` starts on the first non-billing item (falls back to the first item when only billing items exist).
- `Worklist/worklistWizardItem.ts`: `otherOpenItems` excludes billing items (`!hasInvoiceReview(i)`, line 167), so the wizard's "Also resolve" never offers one (the server would refuse the whole review).
- `Worklist/index.ts`: exports `InvoiceReviewTarget` (type, line 9), `hasInvoiceReview` (17), `buildInvoiceReviewLaunch`, `openInvoiceReview` (19).
- `DecisionWizard/decisionDisplay.ts`: `INVOICE_REVIEW_REASON = 'ontology.decision.decided_in_invoice_review'` (116); `failureHeading` returns "Decide this in Invoice Review" first when `failure.reasonCode` matches (121); `failureMessage` has a case for it: "Invoice items are decided in Invoice Review. Nothing was recorded." (150).
- `src/solutions/LegalWorkspace/src/sections/worklist.registration.ts`: imports `openInvoiceReview` from `@spaarke/ui-components` (72) and passes it to `WorklistWidget` props (148). The header comment (lines 27-45) says this section is the Console's LIVE Worklist mount; any new widget slot must be filled here too.
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/WorklistWorkspaceWidget.tsx`: imports `openInvoiceReview` (35) and passes `openInvoiceReview={openInvoiceReview}` (105).
- No change to `WorklistLanePanel`, `useWorklistData` or `WorklistService` method signatures.

## 3. Impact on planned work

- **B-1 capability extraction:** `CommitAsync(Guid signalId, DecisionReview review, HttpContext http, CancellationToken ct)` keeps its signature. Plan against the post-merge file: the 1c-billing block (974-996) and `IsBillingSubject` (1684) must move with the `HttpContext`-free core byte-for-byte; add them to the B-1 PR's invariant list. A B-1 diff written before #2580 merges will conflict textually in `DecisionCommitService.cs`.
- **B-2 result mapper:** include `ontology.decision.decided_in_invoice_review` as `Conflict` → 409 (MCP: `isError` plus `reasonCode`, message "decide in Invoice Review"). `ToHttp` needs no new status mapping; record contract tests for the unchanged REST responses plus this one code.
- **B-5 slim list projection:** the real type to project from is `WorklistItemRow` (no `WorklistItemSummary` exists yet). The slim projection must keep `WorklistInvoiceReview? InvoiceReview` (`{ InvoiceId, LineId }`, about 60 bytes), and the 600-byte-per-row budget must include it. Do not re-read the line→invoice mapping: `SignalReadService` steps (9a)/(12) already do it once per response, as the caller.
- **FR-03 / FR-12 category enum and lane cards:** `LaneFilterKey` on the #2579 head is `'all' | 'invoiceDecisions' | 'other' | WorkType`; the Decide lane gets an "Invoice decisions" card right after All (shown only when non-empty; billing excluded from work-type cards and Other). The FR-03 closed enum "All / Approve or rebudget / Other" (spec line 141) is out of date: add `invoiceDecisions` (and the work types, if the enum mirrors `laneCountFilters.ts`). Counts for the "same counts as the Console" acceptance must use the same partition.
- **FR-12 billing link-out:** reuse the rules of `openInvoiceReview.ts` (`sprk_invoicereviewpage`, `cleanGuid` bare lower-case ids, `lineId` only when present). The shared opener is Xrm-coupled (`navigateTo`), so the MCP widget cannot call it; a widget link-out needs a URL / host `openLink`. Reuse `buildInvoiceReviewLaunch(...).pageInput.data` (the raw `data` string) and encode it once more when building a URL. Use "Finding" wording (D-208) in link-out text. Billing rows must not reach the wizard (`hasInvoiceReview(item)` gate in `openWizard`, `startLane`, `otherOpenItems`).
- **FR-08 billing refusal test:** call the commit through the extracted capability with a Signal whose subject is `sprk_invoice` or `sprk_billingevent`; expect `Status = Conflict`, `ReasonCode = ontology.decision.decided_in_invoice_review`, nothing written, rights probe not called. Do NOT add a second check. Reuse the PR's fixture shapes (section 5).
- **FR-13 wizard shim:** `failureHeading`/`failureMessage` already map the new code; the MCP fetch shim must pass `reasonCode` through so the wizard shows "Decide this in Invoice Review". Keep the `otherOpenItems` filter and the browse-set filter if `WorklistLanePanel` is mounted standalone.

## 4. Merge-tree (read-only, onto this worktree HEAD `53dbc7233`)

`git -C <wt> merge-tree --write-tree HEAD origin/<branch>`:
- `origin/task/174-billing-refusal`: tree `f46359f61df8601b261d041c319eda618fa82805`, **one conflict**: `.claude/agent-memory/researcher/MEMORY.md` (content conflict, unrelated to the PR's code). `tests/integration/data-mutation/Signals/DecisionCommitServiceTests.cs` auto-merged. No production `.cs` file conflicts.
- `origin/task/175-invoice-decisions-card`: tree `b7896adc2c9b9dc02bbdbc7dbf21fb793bbb8604`, **one conflict**: the same `.claude/agent-memory/researcher/MEMORY.md`. No client file conflicts.
- Meaning: both PRs apply cleanly to the code. The MEMORY.md conflict is a side effect of the old merge base (700+ commits behind master); it is not in either PR's three-dot diff. Plan on the post-merge code; do not merge them into this worktree.

## 5. Tests added or changed (must stay green through our refactor)

Server (#2580), under `tests/integration/`:
- `data-mutation/Signals/DecisionCommitServiceTests.InvoiceReview.cs` (new partial, 272 lines, 13 cases): `Task174_ASignalWhoseSubjectIsAnInvoiceOrALine_Is409DecidedInInvoiceReview_BeforeTheRightsProbeAndAnyWrite` (Theory), `Task174_ADismissalOfABillingSignal_IsRefusedToo_NothingIsWritten`, `Task174_ACallerWhoCannotReadABillingSignal_GetsTheUniform404_NotTheBillingRefusal`, `Task174_AnAlsoResolveBillingSignal_RefusesTheWholeReview_AndNothingIsWritten`, `Task174_AnAlsoResolveBillingSignalTheCallerCannotRead_IsTheUniform404_NotTheBillingRefusal`, `Task174_AMatterSubjectSignal_IsUnchanged_AndStillAsksTheCallersRights`, `Task174_K1_ARealLedgerReplay_OfAReviewRecordedBeforeD207_StillFinishesItsCloses_ForABillingSignal`, `Task174_K1_AFinishingReplay_WhenTheLedgerForgot_ButTheRecordStays_StillClosesABillingSignal`, `Task174_K1_ARequestShapedLikeAReplay_WithNoRecordedReview_IsStillRefused_AndNothingIsWritten`, `Task174_K1_ARecordForAnotherReview_DoesNotMakeThisOneARecordedReplay`, `Task174_K4a_A409ForABillingAlsoResolve_ThenTheSameReviewIdWithoutIt_IsRecorded`, `Task174_K4b_ABillingSignalUnderRetry_Is409_AndTheResumedFailureIsRestoredForTheNextRetry`.
- `data-mutation/Signals/DecisionCommitServiceTests.cs` (+5 lines): the existing-record fake now honours the `sprk_steps LIKE %reviewId%` condition so a record for another review is not found.
- `Sprk.Bff.Api.IntegrationTests/Signals/SignalReadRouteTests.InvoiceReview.cs` (new partial, 139 lines): `Task174_ALineItemCarriesItsParentInvoiceAndLine_AnInvoiceItemItsInvoiceWithNoLine_OthersNone_InOneExtraRead`, `Task174_WithNoLineItems_NoExtraReadIsMade`, `Task174_ALineWithNoParentInvoiceOrAnUnreadableOne_OmitsTheMember_AndIsNamedOnceByIdAtWarning`, `Task174_AFaultOnTheBatchedRead_OmitsTheMemberForEveryLine_LogsOnce_AndTheRouteStill200s` (Theory).
- `.../SignalReadRouteTests.cs`: class made `partial` (line 39); one D-176 assertion now expects exactly one `BillingEventRead` (the shared line→invoice read). Our B-1/B-5 refactor must keep the "one batched read per response" count.
- PR reports Signal|Worklist|DecisionCommit|Billing|Finance 8033 passed with 1 pre-existing unrelated failure (`D134_NoSingleEnabledAssignee`), `SignalReadRouteTests` 118/118, ArchTests 1882/1882, publish size -246,046 B vs fresh master.

Client (#2579):
- `Worklist/__tests__/WorklistInvoiceReview.test.tsx` (new, 296 lines): route-member parsing; Invoice decisions card (right after All, sums, select/deselect, absent when none); opening a billing item (`navigateTo` args on `sprk_invoicereviewpage`, `target: 2`, no wizard; re-read after close and after reject; no-Xrm and no-opener notices); "Also resolve" excludes billing; browse set excludes billing; Start review skips billing, or opens Invoice Review when only billing remains.
- `Worklist/__tests__/laneCountFilters.test.tsx` (+84): Decide partition with 2 billing + 3 matter items; no bucket without billing; Do lane unchanged; filter/toggle behaviour.
- `DecisionWizard/__tests__/decisionDisplay.failureMessage.test.ts` (new, 55): the code wins over status; negatives for another 409 code and an unknown code on 422.
- `Spaarke.AI.Widgets/src/widgets/workspace/__tests__/WorklistWorkspaceWidget.test.tsx` (+10) and `Spaarke.DailyBriefing.Components/test/worklistSectionRegistration.test.tsx` (+14), with mock `test/__mocks__/spaarke-ui-components.tsx` (+3): the opener is handed to `WorklistWidget` with or without Xrm.

## 6. Other facts

- PR #2580 completion note: the commit route is the only human-decision path outside eBilling; decision action executors are built only by `DecisionCommitService` (`:320`); there is no chat, assistant, MCP or playbook decide tool yet. Our MCP commit (FR-08) is the first new entry point and gets the refusal by calling the capability.
- PR #2579 note: the live Console mounts the Worklist through LegalWorkspace's `worklist.registration.ts`, not the direct widget; any change to the Console list contract (spec FR-12 "Console's own worklist unchanged") must include that mount.
