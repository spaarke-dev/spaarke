# Task 075 - Rule drafting Action BILLING-RULE-DRAFT@v1 and job (FR-27)

Rigor: **FULL** (POML said STANDARD; the task edits `.cs` files and is tagged `bff`, `ai`, `jps`, `jobs`, so the decision tree raises it). Step 9.5 code-review and adr-check ran.
Base `62a1ec56b`; commits `4d7a8a8f0` (Action, mirrors, Binding, `ConsumerTypes.All`), `a42d6ec66` (job, route, write path) and a third commit with the review fixes below.

## What was built

| Piece | Where |
|---|---|
| Action (Standard tier, temperature 0.0) | `infra/dataverse/actions/billing-rule-draft.action.json` |
| Output schema (proposals[], notConverted[]) | `infra/dataverse/outputschemas/billing-rule-draft.schema.json` |
| Input schema | `infra/dataverse/inputschemas/billing-rule-draft.input.schema.json` |
| Binding row (consumer type and action code `billing-rule-draft`, env `*`, enabled) | `infra/dataverse/sprk_playbookconsumer-rows.json` |
| `ConsumerTypes.BillingRuleDraft` in `All` (same commit as the Binding row) | `Services/Ai/PublicContracts/ConsumerTypes.cs` |
| Job handler `BillingRuleDraft` | `Services/Jobs/Handlers/BillingRuleDraftJobHandler.cs` |
| Proposal to rule translation, wording, chunking | `Services/Finance/BillingRuleDraftTranslator.cs` |
| Source read and Draft write seam | `Services/Finance/RuleSetService.cs` (`GetDraftSourceAsync`, `AddDraftsAsync`) |
| Trigger `POST /api/finance/rulesets/{ruleSetId}/draft` | `Api/Finance/RuleSetEndpoints.cs` (existing file) |
| Privilege set `DraftRules()` | `Api/Finance/BillingAdminAuthorization.cs` (076's helper, reused) |
| DI line | `Infrastructure/DI/FinanceModule.cs` |

The Action JSON stays under `infra/dataverse` (not `.claude`), so no hand-off to the main session is needed. 041's `IInvoiceAi.DraftRulesAsync` and its Null peer were already complete; `InvoiceAi.cs` was not touched.

## Deviations from the POML (recorded as asked)

1. **Route**: not a new `BillingRuleDraftEndpoints.cs`. The route is added to 076's `RuleSetEndpoints.cs` under the same group, with the same admin mechanism, so the route ledger stays 137 (it counts endpoint files) and ADR-010's ceiling stays 166. Only the ledger's description text for that file was extended.
2. **Action code**: `billing-rule-draft` (lowercase kebab, no version suffix) as the Action catalogue requires; `BILLING-RULE-DRAFT@v1` is the display name in the description and `$version` is 1.
3. **Seam test location**: `tests/integration/seam/Finance/BillingRuleDraftSeamTests.cs` (not `.../Ai/PublicContracts/`), next to the other Finance seams. It drives the real `InvoiceAi`, `ActionResolver` and `ActionRunner` with the authored Action mirror, into the real `RuleSetService` and `PolicyVersionValidator`.
4. **Idempotency key**: the rule set id (one drafting run per rule set, ever), plus a content fingerprint (type, body, clause quote) per rule. See "Idempotency".
5. **Where the not-converted list lives**: `sprk_ruleset.sprk_notconverted` (019 has the column, so no escalation). It doubles as the "already drafted" marker.
6. **Output schema shape**: Azure Structured Outputs forbids `anyOf`/`oneOf`, so `parameters` is ONE object that is the union of the 12 body members, each nullable and required. The translator strips nulls and sets `type` itself (a model-supplied `type` is ignored).
7. **Job status**: no persisted status row. ADR-017 is Proposed; adding one needs a schema or cache-key review. The status URL is 076's `GET /api/finance/rulesets/{id}` (rules appear as Proposed, `sprk_notconverted` appears when the run finished); failures are in the logs and the DLQ. Open question 1.

## How it behaves

- Trigger: 401 unauthenticated; 403 unless the caller holds Write on `sprk_ruleset` and Create on `sprk_Policy` and `sprk_PolicyVersion` (fails closed, D-76); eBilling flag off gives the eBilling-disabled code; 404 unknown or non-billing set; 409 retired; 409 `ruleset.already_drafted`; 422 `ruleset.source_missing` (no source document, or its container is not the Procedure Library container); 503 `ruleset.draft_not_queued` when the queue refuses; otherwise 202 with `{ruleSetId, jobId, statusUrl}` and a `Location` header.
- Job: take a processing lock on the rule set (`billing-rule-draft:{ruleSetId:N}`, 20 minutes, released in `finally`; not taken = Failure, retried, and the retry finds the marker), load source (a document in another container is refused, so the job never follows a pointer out of the Procedure Library), download, extract text, split at paragraph then line then hard cut (24,000 chars, at most 8 pieces), `DraftRulesAsync` per piece, translate (a clause quote that is not in the document text, ignoring white space, case and typographic quote and dash characters, is refused and listed), validate each body with `PolicyVersionValidator.ValidateForSave(..., Billing)`, store.
- A stored rule = `sprk_policy` (Billing, Off, lane Decide, priority 500, rule set lookup, no `sprk_currentversion`) with one `sprk_policyversion` v1, state **Proposed**, clause quote and location, consequence from the closed list. Nothing is Published, enabled or in force. The policy is deleted again if its version fails to write.
- Anything not stored goes to `sprk_notconverted` with its clause and a reason: unknown type (exact ordinal match against the 11 names), parameters or body that fail `ValidateForSave`, unknown consequence, over-long quote, the model's own unsupported clauses, text beyond the 8-piece cap, rules beyond the 500 cap, and an overflow entry if the JSON would pass 95,000 chars.
- Outcomes: AI off (Null peer), no extractable text, invalid payload, no owner for the rules, other refusals: **Poisoned** with a stable reason. Download null, completion or output failure, **Binding missing** (the facade documents it as possibly a transient routing read), lock held by another delivery, 5xx write, unexpected exception: **Failure** until `JobContract.IsFinalDelivery` (the broker's delivery count; `Attempt` never advances on a redelivery), then Poisoned with the reason. An `already_drafted` answer from the store is Success.
- Logs carry ids, counts, codes and timings only (ADR-015).

### Idempotency (NFR-08)

Two deliveries of one rule set cannot run together: the processing lock above (the main queue does not de-duplicate, and a run can outlast the message lock). The marker (`sprk_notconverted`, `[]` when empty) is written LAST. A redelivery after a finished run reads the marker and ends Success without downloading, asking the model or writing (handler guard, and `AddDraftsAsync` refuses a second time with 409). A run that died midway has no marker, so the repeat re-runs; `AddDraftsAsync` loads the set's existing rule fingerprints and skips any rule already stored, so nothing is stored twice.

## Retry compensation (review finding 2, F1: closed)

**Defect.** A run that died midway (for example a Dataverse 429 at rule 30 of 60) was retried; the model was asked again and, being non-deterministic at temperature 0, worded things differently, so its rules did not match the stored ones by fingerprint and both runs' drafts stayed.

**Fix (no schema change).** A run that finds no completion marker first removes the earlier run's untouched drafts, then drafts again. `BillingRuleDraftJobHandler` calls `RuleSetService.RemoveUntouchedDraftsAsync` right after the marker check and before the document is downloaded or the model is asked. The store decides what "untouched" means; the handler only orders the steps.

A rule (one policy and all of its versions) is **untouched** only when ALL of these hold; a rule failing any one is never deleted (fail closed):

| Criterion | Read from |
|---|---|
| Created by the job identity: the policy and every version have `createdby` = the identity the BFF writes Dataverse as | `IGenericEntityService.GetCurrentIdentityIdAsync()` (WhoAmI on the ServiceClient, cached; new member with a throwing default body, implemented by `DataverseServiceClientImpl`) |
| Created for this rule set | the `sprk_ruleset` lookup (query and per-row re-check), Billing domain, and the drafted code prefix `BRD-{set}-` (belt and braces: three layers, each alone is enough to keep another set's rule) |
| Still Off and Proposed | policy `sprk_enabled` is explicitly `false` (null is unknown, so kept); every version `sprk_proposalstatus` = Proposed; no version has `sprk_inforceto` |
| No admin touch | policy and each version have `modifiedon` equal to `createdon` (an edit of a draft is in place, so it moves `modifiedon`; so does accept, reject and publish) |
| Never published | the policy's `sprk_currentversion` is empty |

A policy with no version (a run that died between the two creates and could not remove the policy) is untouched when the policy itself is. Versions are deleted before their policy, so a death between the two deletes leaves a version-less policy that the next pass removes.

**Choice for a draft that is NOT untouched (an admin worked on it).** Keep it, and draft again only for what is missing. "Stop with a logged outcome" was rejected: the rule set would be stuck with no marker, every retry would stop again, and a Rejected or Accepted row would keep failing the untouched test, so the stop would repeat until someone deleted the rows by hand. Basis: the POML requires that re-delivery creates no duplicate drafts and ends in a stable outcome (NFR-08), and the earlier notes already kept admin-edited partial drafts. "What is missing" is decided by clause: `AddDraftsAsync` loads the normalised clause quotes of the rules that survived (the same normalisation as the quote-in-document check) and does not store a new rule for a clause one of them covers; it lists it in `sprk_notconverted` with the reason "a rule already in this rule set, from an earlier drafting run, covers this clause", so nothing is dropped silently. An identical rule (same type, body and clause) is still skipped quietly as before. Consequence: a rejected earlier draft is not resurrected by a retry, and an edited one is not duplicated.

**Failure handling.** Deletes run as the application identity through `IGenericEntityService`. A row that is already gone (`0x80040217` anywhere in the exception chain) counts as deleted, so a repeated pass is idempotent. Any other fault returns 500 `ruleset.write_failed`; the handler maps it to Failure (Poisoned on the final delivery) **before the model is asked**, nothing is drafted, rows deleted so far stay deleted, and the next delivery compensates again. An unresolvable identity (WhoAmI failed or named no user) is the same fault: nothing is deleted. A rule set with the marker is refused (409 `ruleset.already_drafted`) and nothing is deleted: a finished run is never compensated (the handler's own marker guard already ends such a delivery as Success before reaching this step). More than one read page of rules (5,000) refuses with 422 `ruleset.result_truncated` and deletes nothing. ADR-004: the handler stays idempotent under at-least-once delivery; the processing lock still serialises deliveries.

**Why not the alternative.** Persisting the translated proposals before the first row write and replaying them on retry needs a schema addition. Compensation needs none.

**Known limits.** (a) If the BFF's Dataverse credential is ever changed to a different identity between the dead run and its retry, the earlier drafts read as foreign and are kept (never deleted), so a duplicate set can result; an admin rejects the extras through 076. (b) The test of "untouched" relies on `modifiedon`, which Dataverse also moves for platform-side changes to the row; that only ever keeps a row. (c) The model's second wording of an already-covered clause is listed, not stored; a clause that legitimately yields two rules from one quote, when one of them was kept, loses the second (it is listed).

**Tests** (new, all in `tests/unit/Sprk.Bff.Api.Tests`): `Services/Jobs/Handlers/BillingRuleDraftRetryTests.cs` (the real handler over the real store and the 076 fake: a rerun after a partial run leaves exactly one set; an admin-edited draft is kept and its clause is not drafted twice; a completed run deletes nothing and does not redraft; a delete fault fails the run, asks no model and drafts nothing, and the next delivery finishes), `Services/Finance/RuleSetServiceCompensationTests.cs` (each criterion in isolation: edited version, edited policy, Accepted, Rejected, turned On, enabled unknown, has a current version, was in force, version or policy by another user, not a drafted code, a second version someone else added; another rule set and an Ontology policy; marker; not found; retired; already-gone rows; delete fault then retry; WhoAmI fault; version-less policy) and handler unit cases in `BillingRuleDraftJobHandlerTests.cs` (order compensate, ask, store; fault before the model; marker found; finished run never compensated). The 076 harness gained `createdby`, `modifiedon`, delete-fault and identity members.

**Fail before.** With `RuleSetService.cs` and the handler restored to the previous commit, `BillingRuleDraftRetryTests`: 4 failed, 1 passed (the completed-run control passes both ways by design): "Expected rig.Policies to be 3 ... but found 4"; the delete-fault cases completed the job and drafted.

**Mutation proofs.** Each mutant applied to the source alone, the 100 compensation, retry, handler and draft tests rerun, source restored. All killed.

| Mutant | Tests failed |
|---|---|
| Drafted-code prefix check removed | 1 |
| Off check removed | 2 |
| Current-version check removed | 1 |
| `createdby` check removed | 3 |
| `modifiedon = createdon` check removed | 4 |
| Proposed check removed | 2 |
| `sprk_inforceto` check removed | 1 |
| Versions not checked at all | 9 |
| Rule set scoping removed (query filter and row check together; either alone is covered by the other, so the first run of each alone survived by design) | 1 (after adding the look-alike-code case) |
| Marker refusal removed from the compensation | 1 |
| Not-found treated as a failure | 1 |
| Policy deleted before its versions | 1 |
| Delete fault swallowed | 3 |
| Covered-clause skip removed | 2 |
| Handler skips the compensation | 27 |

**Verification.** `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors. `Sprk.Bff.Api.Tests` filtered (`RuleSet|BillingRule|PolicyVersion|Finance|GoldenUtterance|Catalog|ConsumerType|JobHandler`): 2384 passed, 0 failed, 3 skipped (live-only). `Spaarke.ArchTests`: 1172 passed, 0 failed. `dotnet format` finds only the pre-existing whitespace finding in `RuleSetService.cs` (076's `Id = newId, ...` line). Publish size (Release, linux-x64, framework-dependent, zip Optimal, `git archive` copies, 192 files each): base `1713054e4` 39,591,749 B, head 39,597,223 B, **delta +5,474 B** (under every threshold); no PackageReference change.

## §11 answers

1. **Existing**: `IInvoiceAi.DraftRulesAsync` (041), `RuleSetService` and `BillingAdminAuthorization` (076), `InvoiceExtractionJobHandler` (pattern), `PolicyVersionValidator` (070), the Action and Binding catalogue (ADR-039/043). No rule-drafting job or trigger existed.
2. **Extension**: the row writes were added to `RuleSetService` (single owner of the rule row shape and validation) and the route to `RuleSetEndpoints`; no new interface, endpoint file, package or Dataverse column. New: one job handler and one pure translator, because no job wrote policy drafts and the NDA playbook has no closed rule-schema output.
3. **Cost of doing nothing**: admins hand-author every rule from each OCG; FR-27 and success criterion 3 (a published rule set where every rule cites its clause) fail.

## Acceptance criteria and proof

| Criterion | Proof |
|---|---|
| `ConsumerTypes.All` entry added with its Binding row; /healthz stays Healthy | Both in `4d7a8a8f0`. `BillingRuleDraftActionContractTests` pins the row; with the `All` entry removed 2 tests failed (`ConsumerTypesKey_...`, `GoldenUtteranceEvalSuiteTests.DispatchCases_...`), restored 48 passed. Live /healthz after seeding: **not run** (no live writes). |
| Action passes validation; schema restricts ruleType (11 names) and consequence; mirror matches | `BillingRuleDraftActionContractTests`: tier, code, prompt content, examples through `InvoiceAiOutputParser.ParseDraftRules`, ruleType enum equals `BillingRuleCatalogue`, `parameters` union equals the 11 body schemas, input schema. Mirror vs dev row: **not run** (live seeding held, below). |
| Unknown type or schema-invalid parameters not written, listed with a reason | `BillingRuleDraftTranslatorTests` (exact-name match, mutant case-insensitive match killed), `RuleSetServiceDraftTests` (validator refusal listed, not stored), seam test `TheJob_StoresTheValidProposals_AsProposedOffRules_AndListsEveryRefusalWithTheClauseThatCausedIt`. |
| Valid proposals written as Draft with quote and location; none Published or in force | `AddDrafts_StoresEachRuleAsAProposedOffBillingRule_InForceNowhere_AndWritesTheMarkerLast`, seam test above. |
| Redelivery creates no additional drafts | `Process_ARuleSetThatIsAlreadyDrafted_...`, `AddDrafts_AfterTheMarker_Is409AlreadyDrafted_AndWritesNothing`, `AddDrafts_ARunThatFailedMidWay_IsCompletedByTheRepeat_WithNoRuleStoredTwice`, `AddDrafts_ARepeatedRule_InOneRun_IsStoredOnce`, seam `ARedeliveredJob_AfterAFinishedRun_...`. |
| AI off or Binding missing: stable reason, nothing written, no document text logged | `Process_WhenAiIsSwitchedOff_IsPoisoned_AndNothingIsWritten`, `Process_AFacadeFailure_RetriesUnlessItIsConfiguration` (Binding missing is Poisoned, transient failures are retried), `Process_NeverLogsTheDocumentTextOrAClause_OnAnyPath`. |
| Trigger: 401, 403, eBilling-disabled code, 202 with job id | `RuleSetEndpointsTests`: route added to `AllRoutes` and the privilege matrix (each missing privilege gives 403), plus 202 / refused source / already drafted / queue down. Route ledger test: 137 unchanged. |
| ADR-043 seam through `IActionRunner` via the Binding | `BillingRuleDraftSeamTests` (4 tests) with the real authored Action mirror. |
| Publish-size delta against a fresh master build; no new HIGH CVE | See below. |
| Edge cases beyond the contract justified | Chunking, caps, container check and the marker are each justified above; each has a test. |

## Mutation proofs

Each mutant applied to the source alone, the filtered tests rerun, source restored. All 17 killed. (A mutant needs a non-constant condition, e.g. `Environment.ProcessorCount < 0`, because the build treats unreachable code as an error.)

| Mutant | Tests failed |
|---|---|
| Validation skipped | 9 |
| Marker guard removed | 1 |
| Repeat dedupe removed | 2 |
| Container check removed | 1 |
| Policy not removed after version failure | 2 |
| Handler no longer a no-op when already drafted | 2 |
| AI-off retried instead of Poisoned | 1 |
| Unknown type matched case-insensitively | 2 |
| Template names a field the type does not supply | 3 |
| Handler drops the model's not-converted list | 3 |
| Refused proposals dropped | 1 |
| Draft route without the Create-policy privilege | 1 |
| Processing lock not taken | 3 (incl. the two-deliveries seam test) |
| Processing lock not released | 1 |
| Clause quote not checked against the document | 5 |
| Retry keyed on `Attempt` instead of `IsFinalDelivery` | 3 |
| Owner refusal not mapped to a reason | 1 |

## Verification

- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors.
- `Sprk.Bff.Api.Tests` filtered (`RuleSet|Billing|PolicyVersion|Finance|GoldenUtterance|Catalog|ConsumerType|JobHandler`): **2515 passed, 0 failed, 3 skipped** (the skips are live-only tests); the 075 suites alone (`BillingRuleDraft|RuleSetServiceDraft|RuleSetEndpoints`): 249 passed (before the review fixes: 229 passed, 1 failed on a chunk-count arithmetic error in the test, since fixed).
- `Spaarke.ArchTests`: **1160 passed, 0 failed** (route ledger 137, ADR-010 ceiling 166 unchanged).
- Own suites: `RuleSetServiceDraftTests`, `BillingRuleDraftJobHandlerTests`, `BillingRuleDraftTranslatorTests`, `BillingRuleDraftSeamTests` (5, including two deliveries at once), `RuleSetEndpointsTests` (draft route rows added).
- `dotnet format --verify-no-changes` on touched files: my files clean after normalising to CRLF (`.gitattributes`). Two pre-existing findings are left alone: `FinanceModule.cs` line 1 import order, and `RuleSetService.cs` ~line 995 whitespace (from commit `194aada77`, 076).

## Publish size and packages (BFF hygiene)

Release, `-r linux-x64 --self-contained false`, `Compress-Archive` Optimal, short-path copies from `git archive`, 192 files each:

| Build | Bytes |
|---|---|
| master tip `9b72ea2c8` | 38,977,352 |
| branch base `62a1ec56b` | 39,545,337 |
| branch head `a42d6ec66` | 39,565,610 |

Task 075 delta (measured on the first two commits; the review fixes add no package or file) **+20,273 B** (+0.019 MB), total 37.73 MiB. Under the +5 MB, 55 MB and 60 MB thresholds. (Head minus master is +588,258 B, which is all of ebilling-r1's wave so far, not this task.) No PackageReference change. `dotnet list package --vulnerable --include-transitive`: none.

## Open questions and holds

1. **Job status store (accepted as a known limit, 2026-10-18).** ADR-017 is Proposed, not accepted, so it does not bind and there is no ADR conflict. The 077 page polls the rule set read, which shows drafting progress; failures go to the logs and the DLQ. ADR-017 calls for persisted status transitions; none was added. Smallest addition if wanted: a status option and a started/finished timestamp on `sprk_ruleset` (or a cache key with a TTL), written by the handler. Owner decision.
2. **Live seeding held.** The Action mirror and Binding row are in the repo but not deployed (no live writes in this run). Order: `Deploy-ActionMirrors.ps1`, then `Seed-PlaybookConsumers.ps1`, then verify /healthz. A constant in `ConsumerTypes.All` without a Binding row makes `RoutingConsumerTypeHealthCheck` report **Degraded** (forward-declared constant); an orphan row or handler is Unhealthy. Until the row is seeded the job would end Poisoned on its final delivery with the Binding-missing reason. Deploy the Binding with or before the BFF. This belongs with task 053's deploy order.
3. **Privilege name (closed 2026-10-18).** `prvCreatesprk_Policy` and `prvWritesprk_ruleset` were both checked to exist on spaarkedev1 by the main session.
4. **`sprk_subjecttype` left unset** on drafted policies (076 notes listed it; the data-model doc says only the lane is required). Lane = Decide and priority = 500 are my choices.
5. **Consequence eligibility** per type (task 073) does not exist yet, so Reject is accepted for any type here; 073 should tighten `CheckDraft`.
6. **Retry with different model output: CLOSED.** The proposed fix was applied, with the owner-decision parts settled as recorded in "Retry compensation": untouched drafts of the earlier run are deleted before drafting again; a draft an admin worked on is kept and its clause is not drafted a second time. No schema addition.
7. **Exception text in logs.** The handler passes the exception object to `LogError`; the message of a third-party exception is not under our control. Only ids and codes are logged by us.
8. **Drafted version carries `sprk_inforcefrom` = drafting time.** The column is required on the version; 076's proposed-rule definition lists it too. No reader treats a non-current version as in force (they key on `sprk_currentversion`), but a view that shows the column would read "in force from" for a rule that never was. The POML says the job sets no in-force dates; this is the one exception, forced by the required column. Publish overwrites it with the effective date.
9. **A deterministic write failure** re-runs every model call on each of the five deliveries (up to 40 Standard-tier calls). Bounded, not fixed.
10. **A clause that crosses a piece boundary** is seen as two halves and may end up in neither the rules nor the not-converted list (pieces do not overlap; a CRLF paragraph break falls back to single line breaks). Bounded by the 24,000-character piece size.

## Review (Step 9.5)

One adversarial review (Opus, read-only) of `62a1ec56b..a42d6ec66`; adr-check by hand against ADR-004, 010, 013, 015, 017, 019, 039 and 043 (no conflict; ADR-017's status store is the open question above).

| Finding | Class | Outcome |
|---|---|---|
| 1. Two deliveries of one rule set run together and store every rule twice | F1 | Fixed: processing lock (ADR-004 pattern, `IIdempotencyService`), seam test with two deliveries at once, two mutants |
| 2. Crash-retry with different model output leaves earlier rules beside new ones | F1 | Fixed in the retry-compensation round: see "Retry compensation" (deletes only untouched drafts; mutants and fail-before recorded there) |
| 3. Missing Binding dead-lettered on first delivery though the facade says it may be transient | F1 | Fixed: retried until the final delivery, then Poisoned with the reason |
| 4. Model's "verbatim" clause quote stored without checking it occurs in the document | F1 | Fixed: `Translate(proposals, sourceText)` refuses and lists a quote not found (white space, case, typographic characters ignored) |
| 5. Retry logic keyed on `job.Attempt`, which never advances on a redelivery | F3 | Fixed: `IsFinalDelivery`; tests set `DeliveryCount` |
| 6. Owner refusal ended as `refused (0)` with no reason | F2 | Fixed: Poisoned with the refusal code |
| 7. `sprk_inforcefrom` set on the drafted version | F4 | Recorded (open question 8): the column is required |
| 8. Notes not committed; healthz claim wrong | process | Fixed: notes committed with the review fixes; Degraded, not Unhealthy |
