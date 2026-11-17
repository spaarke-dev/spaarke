# Task 131: Firm notice Action `INVOICE-FIRM-NOTICE@v1` with template fallback

Branch `task/ebilling-131`, from `a93e30cedc`. Rigor STANDARD. Model: Sonnet @ high. FR-50, D-60, D-52.

**Held: no Dataverse write was made.** The Action row and the Binding row are authored as files only. Seeding them in dev waits on the owner's typed OK and on task 136 (see "Seeding commands").

## What was built

| Component | File | What it does |
|---|---|---|
| Action mirror | `infra/dataverse/actions/invoice-firm-notice.action.json` | JPS Action. Standard tier, temperature 0.2. Output `subject` and `body` (strings only). Nine instruction constraints (no digit, no rule-set name, no quoted clause, no link) and two digit-free examples. |
| Output schema | `infra/dataverse/outputschemas/invoice-firm-notice.schema.json` | `subject` (max 120), `body` (max 1500), both required, `additionalProperties: false`, no numeric type. |
| Input schema | `infra/dataverse/inputschemas/invoice-firm-notice.input.schema.json` | `outcome` (closed enum) and `findings` (strings only: rule-set name, rule name, consequence, issue). No numeric type. |
| Binding row | `infra/dataverse/sprk_playbookconsumer-rows.json` | `invoice-firm-notice`, consumer code default, environment `*`, priority 500, enabled. |
| Consumer type | `Services/Ai/PublicContracts/ConsumerTypes.cs` | `InvoiceFirmNotice` added to `All` in the same change as the Binding row (`RoutingConsumerTypeHealthCheck`). |
| Composer | `Services/Finance/FirmNoticeComposer.cs` | Projects the outcome (130), drafts prose through `IInvoiceAi.DraftFirmNoticeAsync`, screens it, renders the HTML, falls back to fixed wording on any failure. |
| Template | `Services/Finance/EmailTemplates/FirmNoticeTemplate.html` | Embedded resource (csproj glob, same precedent as `RegistrationEmailService`). Shell with four placeholders; no image, no link. |
| Route | `Api/Finance/FirmNoticeEndpoints.cs` | `POST /api/finance/invoices/{invoiceId}/firm-notice/draft?kind=` (reviewer preview, read-only). |
| DI, routes, csproj | `FinanceModule.cs`, `EndpointMappingExtensions.cs`, `Sprk.Bff.Api.csproj` | One line each. |
| Arch ledger | `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs` | `GovernedFile` (RouteLevelGate) entry; endpoint file count 154 to 155. |
| Eval case | `tests/integration/contract/Eval/golden-utterances.json` | GU-146 (the forcing function requires a case per `ConsumerTypes.All` entry). Shared append point. |

## Behaviour

1. **Where the data comes from.** Only the 130 projection (`FirmFindingsProjection`). The notice carries the tracking number, the billed and approved totals, and a findings table (item, decision, billed, approved, reason, rule). A rule is cited only when the projection cites it (the firm flag publishes the rule). The composer reads nothing else.
2. **What the model sees.** `InvoiceFirmNoticeRequest` is strings only: the outcome name and up to 20 findings, each rule-set name, rule name, consequence name and an issue sentence. The issue is a fixed sentence per decision, or the projection's firm reason when it passes the same prose screen. Reviewer reasons, clauses and amounts never go to the model. No number can reach it.
3. **What comes back.** `subject` and `body`. Every figure in the email (totals, line amounts) is rendered by code from the projection, never from the model.
4. **The prose screen** (`FirmNoticeProse.Check`, second wall; the first is that nothing numeric goes in and the schema has no numeric property). The model's text is refused, and the template used, on: empty text, text over the length cap, a control or invisible character, any digit (any script), a currency symbol or code, an amount word (a small closed list: "percent", spelled-out numbers up to the list, "hundred", and similar), a link or an address.
5. **Fallbacks.** The template wording is used, with a reason code in the draft, when: the AI setting is off (`ai.setting_off`), settings cannot be read (`ai.settings_unavailable`), the kill switch is engaged (`ai.disabled`), 30 s timeout (`ai.timeout`), any other model fault (`ai.error`), the Action is not bound (`ai.invoice.binding_missing`), the completion failed (`ai.invoice.completion_failed`), the output did not parse (`ai.invoice.output_invalid`), or the screen refused the prose (`prose.*`). A caller cancellation is never a fallback; it propagates.
6. **Status and applicability.** `FirmNoticeStatus` is Composed, NotFound, NotApplicable or Unavailable. A kind that does not fit the invoice's state (for example a ReturnedToFirm notice for a Paid invoice) is NotApplicable.
7. **Route.** `kind` is exactly `ReturnedToFirm` or `InvoiceRejected` (400 `ebilling.firm-notice.invalid_kind` otherwise, including empty, `SubmissionRejected`, a number, or a different case). Errors: 404 uniform (no such invoice), 409 `ebilling.firm-notice.not_applicable`, 503 `ebilling.firm-notice.unavailable` (the projection faulted). 200 returns `{ kind, subject, prose, html, source, actionVersion, fallbackReason }` with `Cache-Control: no-store`. Authorization: step 0 on the group, `AddFinanceAuthorizationFilter("ebilling.invoice.decide", invoice, routeKey: invoiceId)` (the same Write right as the Return and Reject routes), then `RequireEbillingEnabled`. It writes nothing and sends nothing. 132 owns sending and storage and re-renders edited prose through `FirmNoticeComposer.RenderHtml(outcome, prose)`.
8. **Logging** (ADR-015): ids, kind, source, sizes, timings and codes only; never prose, findings or reasons (a test pins it).

## Decisions

- **Tier: Standard.** The task is bounded drafting of two short strings from a closed set; no multi-step reasoning is needed and the output is screened regardless. Premium would not change what is allowed through. (Revisit only if the owner finds the Standard prose weak.)
- **One template, not one per outcome.** The brief allowed a template per outcome; the outcomes differ only in one fixed sentence and the findings table, so there is one HTML shell and a closed table of fixed wording per kind (`FirmNoticeFallbackText`). Fewer files to review; a change to wording is a code review.
- **No currency in 130's contract.** The projection has amounts with no currency code, so the HTML shows plain figures (`9,876.54`), no symbol. If a currency is later added to the projection, `Money` is the one place to change.
- **Submission-rejected notices** (`ComposeForSubmissionAsync`) read the projection with the decided flag passed unchanged; this is the entry point 132 uses for a rejected submission. It is not on the reviewer route (a rejected submission has no invoice to review).

## Fallback wording for owner approval

This is firm-facing correspondence, so the owner approves it before it ships. It states outcome facts only; no legal language is added. It is a closed table in `FirmNoticeFallbackText`, so a change is a reviewed code change.

| Kind | Subject | Paragraph |
|---|---|---|
| SubmissionRejected | `Invoice submission rejected` | `Your invoice submission was rejected and was not accepted for review.` |
| ReturnedToFirm | `Invoice returned to firm` | `Your invoice has been returned to you and is now closed. A corrected invoice can be submitted.` |
| InvoiceRejected | `Invoice rejected` | `Your invoice has been rejected in full.` |
| ApprovedWithAdjustments | `Invoice approved with adjustments` | `Your invoice has been approved with adjustments.` |

Words around the data (also for approval):

- Greeting: `Hello,`
- Findings heading: `Findings`
- Table headings: `Item`, `Decision`, `Billed`, `Approved`, `Reason`, `Rule`
- Details lines: `Tracking number: ...`, `Billed total: ...`, `Approved total: ...`
- Decision labels: `Reduced`, `Not allowed`, `Line added`, `Invoice adjustment`, `Rejection finding`
- Fixed issue sentences (used instead of a firm reason that does not pass the screen): `A billed line was reduced.`, `A billed line was not allowed.`, `A line was added to the invoice.`, `An adjustment was applied to the invoice.`, `A finding led to the rejection.`
- Overflow note (more than 20 findings): `{n} more findings are not shown here.`

## Tests

| Layer | File | Covers |
|---|---|---|
| Unit | `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/FirmNoticeComposerTests.cs` | Action prose used; request has no digit or amount; every projected figure shown and nothing else; citation shown only when projected; rejected-submission rendering; refused prose (digit in any script, currency, amount word, link, control character, empty, too long); HTML encoding and single-pass placeholders; every fallback trigger; the applicability matrix; the fixed wording passes its own screen; the request's allow-list is pinned by reflection (no numeric member can be added unnoticed); logs carry no prose. |
| Contract | `tests/integration/contract/Catalog/InvoiceFirmNoticeActionContractTests.cs` | Tier, action code, `ConsumerTypes.All` and the Binding row move together, prompt constraints, examples parse and pass the screen, schemas valid and numeric-free (with a must-fire control for the numeric walk), Binding row fields. |
| Contract (route) | `tests/integration/contract/Api/Finance/FirmNoticeEndpointsAuthorizationContractTests.cs` | 401; 403 without Write, Read alone, and a right on another invoice (reads nothing, calls no model); step 0 must-fire and must-not-fire (task 099); fail-closed on a rights fault; `ebilling.disabled` 503 only for an authorized caller; 400 on a bad kind; 200 shape and `no-store`; 200 with the template and the reason when the Action is unavailable; 404, 409, 503. |
| Seam (ADR-043) | `tests/integration/seam/Finance/FirmNoticeActionSeamTests.cs` | The production `InvoiceAi` over the real `ActionResolver`, `ActionRunner` and `PromptSchemaRenderer`, with the authored mirror as the catalog row. The authored prompt reaches the model; the rendered `## Input` section carries no number; figures are the projection's; a reply with digits falls back; parser-refused replies fall back with `output_invalid`; a missing Binding falls back with `binding_missing` and no model call; a completion failure falls back. |

### Gate counts

- Task tests (`FirmNotice`, `InvoiceFirmNotice`): 127 passed.
- Wider unit filter (`FirmNotice|ConsumerType|RoutingConsumer|FirmFindings|InvoiceAi|GoldenUtterance|P2LoopInjection|CatalogInputSchema|PlaybookConsumer`): 484 passed, 0 failed.
- ArchTests filter (`RouteAuthorizationGuard|Finance|Consumer|ExternalInvoice|Money|Ai`): 419 passed, 0 failed.
- BFF build with `--warnaserror`: 0 warnings, 0 errors.
- The full `Sprk.Bff.Api.Tests` and `Spaarke.ArchTests` suites were not run (brief: filtered gates only).

### Fail-before proof

Three mutations applied together on the committed code, tests run, then each file restored from a copy (no stash): the prose screen bypassed, the route's `AddFinanceAuthorizationFilter` removed, and `InvoiceFirmNotice` removed from `ConsumerTypes.All`.

- Mutated: **40 failed / 159 passed** (199 tests in the proof set). Failures include the prose theory (all 19 cases), `AModelReplyWithAFigure_NeverReachesTheNotice_TheTemplateIsUsed` (seam), `Logs_Carry...`, the route's `Denied_*`, `NeedsWrite_ReadAloneIsNotEnough`, `RightOnAnotherInvoice_*`, `FailedRightsCheck_*` and the task 099 cases, `ConsumerTypesKey_MatchesTheActionCode_AndIsListedInAll_...` and the golden-utterance grounding case.
- Restored: **0 failed / 199 passed**.

## BFF hygiene (root §10)

- No new package, so no CVE delta is expected and none was introduced.
- New surface (§11): the composer is new because nothing existing both projects the firm-visible outcome and drafts screened prose (the 130 projection is read-only data, `InvoiceAi` is the model call only); extending either would mix responsibilities. The route is new because 132 needs a reviewer preview and the reject/return routes act on state. Without them: no firm notice exists, and no reviewer can see what the firm will receive before it is sent.
- Placement: the composer is a scoped service in `Services/Finance` (`FinanceModule`); the AI call goes only through the `IInvoiceAi` facade (`PublicContracts`).
- Publish-size delta: see below.

## Publish-size delta

Method: `git archive` of the base (`a93e30cedc`, this branch's parent) and the tip (`2388b3740b`) into fresh short-path trees (`C:\wt131a`, `C:\wt131b`), then `dotnet publish -c Release -r linux-x64 --self-contained false`, then `Compress-Archive -CompressionLevel Optimal`. PDBs are INCLUDED. The base is the branch parent rather than a fresh `origin/master`, because master is 45 commits ahead and would fold other tasks' growth into this task's number.

| | Folder bytes | Zip bytes |
|---|---|---|
| Base `a93e30cedc` | 140,551,641 | 42,389,586 |
| Tip `2388b3740b` | 140,592,861 | 42,402,461 |
| Delta | +41,220 | **+12,875 (about 0.012 MB)** |

Well under the +5 MB per-task line. The absolute zip size (about 40.4 MB with PDBs) is the existing level, not this task's; the 55 MB and 60 MB thresholds are not approached.

## Seeding commands (NOT run by this task; the dev seed is approved, EB-D-256, and the coordinator runs it after review round 1)

**Order: deploy the BFF that carries the `ConsumerTypes.All` entry FIRST, then seed.** `RoutingConsumerTypeHealthCheck` is tagged `catalog`, so it is served on `/healthz/catalog`, not on `/healthz`. A constant with no row is only Degraded on `/healthz/catalog`; a row with no constant is full drift, a 503 on `/healthz/catalog`. Seeding before the BFF is deployed would put the environment in the worse state.

1. Deploy the BFF (task 136, `/bff-deploy`).
2. Preview, then create the Action row, set the input schema and deploy the mirror (`Deploy-ActionMirrors.ps1` only patches existing rows, so use the ensure script):
   `pwsh scripts/ebilling/Ensure-ActionRow.ps1 -ActionCode invoice-firm-notice -DryRun`
   `pwsh scripts/ebilling/Ensure-ActionRow.ps1 -ActionCode invoice-firm-notice`
3. Upsert the Binding row (diff first):
   `pwsh scripts/dataverse/Seed-PlaybookConsumers.ps1 -DryRun`
   `pwsh scripts/dataverse/Seed-PlaybookConsumers.ps1`
4. Verify with read-only queries:
   - `sprk_analysisactions?$filter=sprk_actioncode eq 'invoice-firm-notice'`
   - `sprk_playbookconsumers?$filter=sprk_consumertype eq 'invoice-firm-notice'`

Between the BFF deploy and the seed, `/healthz/catalog` reports Degraded (a constant with no row). With no row the composer still works: it falls back to the template with `ai.invoice.binding_missing`.

## Known limits

- The prose screen is heuristic. Digits (any script, outside the BMP too), currency, links and a closed list of number and amount words (after NFKC folding; see Review round 1) are refused; a figure phrased outside that list (for example "a couple of") is not caught. The first wall (nothing numeric reaches the model) is the main guarantee.
- After a Return or Reject has been decided, 130 projects status and totals only, so the post-decision draft has an empty findings table; the reviewer's reason is not in the projection and is not sent to the firm by this task.
- `golden-utterances.json`, `FinanceModule.cs` and `EndpointMappingExtensions.cs` are shared append points; expect a trivial merge there.
- The model is Standard tier; the wording of model prose is the owner's to judge once the Action is seeded in dev.
- 132 owns sending, storage and the in-app feed; this task returns a draft only.

## Review round 1 (Opus review; owner decisions EB-D-255 and EB-D-256)

- **EB-D-255:** the fallback wording above is APPROVED as drafted and is unchanged, byte for byte (`FirmNoticeFallbackText` was not touched; a test pins that it passes its own screen).
- **EB-D-256:** the dev seed is approved; the coordinator runs it after this round. Nothing was seeded here.

| Finding | Change |
|---|---|
| F1-a (ArchTests red) | `FirmNoticeComposer` is added to `FirmFindingsProjectionCallerBoundaryGuardTests.AllowedReferrers` (its only callers are the `ebilling.invoice.decide`-filtered draft route and 132's job, both with server-read ids). New `FirmNoticeComposerCallerBoundaryGuardTests`: allowed `FirmNoticeEndpoints` and `FinanceModule`; 132's handler is added when it lands; must-fire controls (call, field, generic resolution) and a must-not-fire control (a type that uses only `FirmNoticeDraft`). |
| F1-b (non-BMP digits) | The prose screen walks runes (`Rune.DecodeFromUtf16`), refuses `Rune.IsNumber`, currency, control (other than line breaks in the body), Format, line and paragraph separators, Surrogate, PrivateUse and OtherNotAssigned. A lone surrogate is refused (it is not valid text). Cases for U+1D7CF..D1 and U+1D7DC added to `BadProse`. |
| F1-c (spelled-out figures) | Prose is NFKC-folded before every word check (and the rune scan runs again on the folded text). Whole-word, case-insensitive refusal of two..ninety (cardinals incl. teens, tens), hundred(s), thousand(s), million(s), billion(s), dozen(s), half, halves, quarter(s), twice, double, triple, and first..tenth. "one" is deliberately not refused (ordinary prose; "line one" is still refused by the numbered-reference check). `.Distinct()` removed from `BuildRequest`: one finding per item (capped at 20). A count of findings is inherently visible to the model (up to 20 entries); it is not a figure it may write, and any number word it writes is refused. Probe cases added: "Seven lines were reduced.", "Twelve of your lines were not allowed.", "A fifty hour charge was disallowed.", "Half of the fees were not allowed.", the bold-letter "two hundred", and more (ordinals, "twice", "dozen", full-width "two", "twenty-five"). |
| F2-a (health check wording) | Corrected above: the check is tagged `catalog` (on `/healthz/catalog`, not `/healthz`); a constant with no row is Degraded there, a row with no constant is a 503. Order is deploy the BFF first, then seed. The Binding row's `$comment` and the seeding section say the same. |
| F3 (rate limit) | `.RequireRateLimiting("ai-batch")` on the draft route: a non-streaming model call, the closest existing policy (20 per minute per user, ADR-016). A route test runs a host with a one-permit policy of that name and shows the second request is 429 and calls no model. |
| F2-b (test that proved nothing) | The negative assertions on a hand-made outcome were removed from `FirmNoticeComposerTests` (they could never fail). New test `FirmFindingsProjectionTests.FirmNotice_NoOverrideReasonProcedureNameOrProcedureId_ReachesTheModelOrTheNotice` (a partial of 130's test class, so it uses that class's `World` rig as it is): the REAL `FirmFindingsProjection` over Dataverse doubles that hold an override record with a reason, a Procedure with a name and an id, and a rule set with a name; the model double echoes whatever it is told. None of the three appears in the subject, prose, HTML or the serialized model request. Controls: the firm-visible reason and label are in the notice, the override record is in the store, the Procedure was read. |
| K4 | The link screen refuses any bare domain shape (`label.label` with a 2-24 letter TLD), not six TLDs; `acme-billing.co.uk`, `evilsite.xyz` and `billing.example.firm` are cases. `CleanTrackingNumber` removes control, format, private-use and unassigned characters from the tracking number and turns any whitespace into one space, in the subject and the details table. |

**Pending owner decision (not changed):** review finding 1, the post-decision empty findings table. It needs a decision on 130's projection (whether it may project a decided invoice's items and reasons). The existing test is left as it is.

### Fail-before (round 1)

- Composer, endpoint and projection mutated together (composer reverted to the committed round-0 version with a stub `CleanTrackingNumber`, `.RequireRateLimiting` removed, projection's reduce action code pointed at `override-submission`): **51 failed / 266 passed** of 317 in `FirmNotice|FirmFindings`. These include every new `BadProse` case that exercises F1-b/c/K4 (non-BMP digits, number words, ordinals, bold and full-width words, bare domains, private use, unassigned, tag character), the tracking-number test, the one-per-item test, the lone-surrogate test, the rate-limit 429 test, and three of 130's own tests that the projection mutation breaks on purpose.
- F2-b alone, projection mutated only: **1 failed** on the leak assertion ("Did not expect text ... SENT-OVERRIDE-REASON"); restored: pass. Deviation: the coordinator suggested putting the override reason into the prose request. The composer has no access to an override reason, a Procedure name or a Procedure id (they exist only below the projection), so the mutation that can make the test fail is in the projection; that is the layer the rebuilt test exercises.
- ArchTests: with the allow-list entries removed from the two guards, **2 failed**; restored: pass.

### Gates (round 1)

- `dotnet build src/server/api/Sprk.Bff.Api/ --warnaserror`: succeeded, 0 warnings, 0 errors.
- Unit filter (`FirmNotice|ConsumerType|RoutingConsumer|FirmFindings|InvoiceAi|GoldenUtterance|P2LoopInjection|CatalogInputSchema|PlaybookConsumer`): 508 passed, 0 failed.
- `FirmNotice|FirmFindings`: 317 passed. Seam (`FirmNoticeActionSeamTests`): 9 passed.
- ArchTests (`Finance|Firm|Boundary|Route|Consumer|Ai`): 508 passed, 0 failed. The pre-existing "082c-2 C16 ... InvoiceAdjustmentService::IsOtherContent" failure the coordinator named did not appear in this run (it is not mine and was not touched).
