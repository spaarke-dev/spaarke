# Task 054 - Code-set matching: tuning data, AI review step, none-fits (D-93)

Status: code complete and committed. Spec: FR-22, owner decision D-93, D-51/D-52/D-61/D-64, ADR-013/032/039. Live evaluation **12/12** (11 dev rows + 1 negative control), twice. Live changes on spaarkedev1: two additive columns and their tuning text (section 5). The Action and Binding rows are NOT seeded; task 053 seeds them (its POML now lists the commands). Also fixes the 048 confirm lost update (section 6).

## 1. What shipped

| Area | Files |
|---|---|
| Action (mirror-first) | `infra/dataverse/actions/invoice-codeset-match.action.json` (INVOICE-CODESET-MATCH@v1, Fast tier, temperature 0, 9 constraints, 2 examples incl. a `none`), `outputschemas/invoice-codeset-match.schema.json`, `inputschemas/invoice-codeset-match.input.schema.json` |
| Binding / registry / eval | `infra/dataverse/sprk_playbookconsumer-rows.json` (one global row, no tool description), `ConsumerTypes.InvoiceCodeSetMatch` + `All`, `tests/integration/contract/Eval/golden-utterances.json` GU-144 |
| Facade | `IInvoiceAi.MatchCodeSetAsync` (+ `NullInvoiceAi` throws feature-disabled, ADR-032), `InvoiceAi.MatchCodeSetAsync` (operand only; prompt/schema/tier are the Action row's), `InvoiceAiOutputParser.ParseCodeSetMatch`; DTOs in `InvoiceAiContracts.cs` |
| Service | `Services/Finance/Coding/CodeSetProposalService.cs`: rich set profiles, embedding pre-rank, AI review with confirmed mappings as examples, output guard, `NoneFits` outcome, unreviewed fallback, coverage counts, conditional confirm |
| Endpoint | `Api/Finance/CodeSetMappingEndpoints.cs`: GET proposals returns `{ proposals, coverage }` |
| Schema / solution | `scripts/ebilling/Deploy-EbillingBillingCodeSchema.ps1` (+2 Memo columns); `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_billingcodeset/Entity.xml` (+`sprk_Description`, `sprk_MatchHints`), main form (+ "Matching" section with the two fields, both `.xml` and `_managed.xml`) |
| Tuning data / seed | `scripts/seed-data/ebilling/utbms-codeset-tuning.json` (6 task sets); `scripts/ebilling/Seed-EbillingBillingCodes.ps1` tuning step (fill-if-empty with If-Match, drift reported) |
| Tests | seam `CodeSetMatchingSeamTests.cs` (15 cases), `CodeSetProposalServiceSeamTests.cs` (+1 race test, 048 tests adapted), live `CodeSetMatchingLiveTests.cs` (Skipped without env), contract `Catalog/InvoiceCodeSetMatchActionContractTests.cs` (7), `Api/Finance/CodeSetMappingEndpointsContractTests.cs` (+2); helpers `AuthoredActionMirror.cs` (lifted unchanged from `LineCodingSeamTests`), `FakeCodingReferenceData.cs` (conditional write, versions, `AfterQuery`, tuning columns) |
| Evaluation | `notes/evaluation-set/code-set-matching/cases.json` (12 cases), `run-2026-10-16.md` (live report) |
| 053 | `tasks/053-wp2-deploy.poml`: dependency on 054, a constraint with the three seeding commands, steps 2/3 and the first criterion name `invoice-codeset-match` |

### Placement (CLAUDE.md section 10, bff-extensions A.1)

In the BFF beside 047/048 (`Services/Finance/Coding/`, the AI `PublicContracts` facade). Synchronous request work only (embeddings + one Fast completion per unmapped row); no background work, no new route, no DI change (`CodeSetProposalService` gains a constructor dependency, `IFieldMappingDataverseService`, which `GraphModule` already registers unconditionally). No package added.

### Component justification (CLAUDE.md section 11)

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `sprk_billingcodeset.sprk_description` / `sprk_matchhints` | `sprk_body` (standards body), per-code `sprk_description` / `sprk_guidancetext` | No set-level column describes scope; `sprk_body` holds ABA/LOC | Commercial, M&A and Commercial Transactions keep pointing at Trademark/Bankruptcy (048 probe); admins cannot steer matching |
| Action `invoice-codeset-match` + Binding + `ConsumerTypes` entry + GU-144 | `invoice-line-code` (codes lines within one set) | No existing Action chooses between sets; D-52 forbids a prompt in C# | Embeddings alone get 9/11 (section 3) and cannot answer "none fits" (D-93) |
| `IInvoiceAi.MatchCodeSetAsync` + 5 DTO records/enum | `CodeLinesAsync` | Added as a member of the existing facade (ADR-013), not a new interface | The service cannot reach the Action without bypassing the facade |
| `CodeSetMatchOutcome`, `CodeSetCoverage*`, `CodeSetProposalList`, `CodeSetProposal.Outcome/Closest/Confidence/Reviewed` | 048's `CodeSetProposal` | Extended in place | A none-fits row would need a fake "proposed" set; coverage ("no row silently omitted") has no place |
| `CodeSetProposalsResponse.Coverage` | 048 response | One field added to the existing response | Acceptance criterion 2 (coverage summary) |
| `AuthoredActionMirror` (test helper) | private class in `LineCodingSeamTests` | Lifted unchanged and shared | Two seams would render mirrors differently |
| `utbms-codeset-tuning.json` + seed step | `utbms-*.csv` | The seed is extended; CSVs are per-code rows | The six sets on dev and new environments carry no tuning text |

**ADR-010:** no new interface; delta **0** (ceiling `knownOneToOneCeiling` unchanged at 165; ArchTests green).

## 2. Behaviour

**Usable set** (unchanged from 048): active, with at least one active Task code. So "UTBMS Activity" and "UTBMS Expense" are never ranked, never candidates, never confirmable.

**Set profile** (embedded once per service instance, and cached across requests in `IEmbeddingCache` when it is registered; fix-round-1): `"{name} ({body})\n{description}\nFits: {matchHints}\nTask codes: L110 Fact Investigation/Development; ..."`. Reference rows are embedded by name (also cached, keyed by the name).

**Matching one row** (`MatchAsync`, used by GET proposals and by the 047 hook):
1. A nameless row, or no usable set at all: `NoneFits`, unreviewed, with a rationale (never omitted).
2. Rank every usable set by cosine. Candidates: all when there are at most 6 (dev has 6), else the top 4.
3. Examples: the confirmed mappings of this environment (active rows of both tables mapped to a usable set) whose names are most similar to the row's, at most 10 (fix-round-1; was the first 10 by name). Their name embeddings are computed once per scope; if they cannot be embedded, name order is used.
4. `IInvoiceAi.MatchCodeSetAsync` -> the Action answers `{ codeSetId, confidence, reason }`.
5. `codeSetId == "none"` -> `NoneFits`: `proposed` and `runnerUp` are null (nothing to confirm by mistake), `closest` names the most similar set as context only, rationale "No code set fits: {reason} Map this row by hand or add a code set (closest by similarity: '...')".
6. A Guid that is one of THIS request's candidates -> `Proposed`, `reviewed: true`, the model's confidence and reason; runner-up = the best other ranked set.
7. Anything else (an id outside the candidates, the Activity set's id, a set name, an empty string) is **refused** and, like a failed or unavailable Action (`binding_missing`, completion failure, linear consumers off), falls back to the embedding top as `Proposed`, `reviewed: false`, `confidence: null`, rationale "Not reviewed by AI (...); low confidence, check before confirming. ...". A failure never becomes a none-fits and never fails the list.

**GET proposals** returns every unmapped active row (proposed, none-fits or failed) plus `coverage.matterTypes` / `coverage.practiceAreas` = `{ active, mapped, proposed, noneFits, unreviewed, failed }`. `outcome` serialises as the string `Proposed` / `NoneFits` / `Failed`. Since fix-round-1 the rows are matched 4 at a time (order = row order); a row whose matching throws (e.g. its name cannot be embedded) is `Failed` with no proposal and does not affect the others; the AI being off still answers 503. The first row that reaches the Action is matched alone, and if the Action is unavailable (`binding_missing`, switched off) the rest skip it; the request logs one warning per distinct review issue, not one per row.

**047 hook (FR-22 step 4).** Unchanged gate (only when neither lookup is mapped). It matches the matter type; if that is none-fits it tries the practice area; the first `Proposed` set id is returned (the resolver flags it `AiSuggestion` / `NeedsReview`). Both none-fits -> no suggestion -> the resolver's settings default. Nothing is persisted (seam tests assert no write and no conditional write).

**Confirm** is the only writer (FR-22). See section 6 for the conditional write.

### Deviations and interpretations

1. **Hook fall-through (deliberate).** When the matter type is none-fits the hook tries the practice area before giving up. A none-fits matter type says nothing about the practice area, and the practice area is the resolver's own next step.
2. **Employment -> UTBMS Counseling.** In a corporate legal department, employment matters are mostly advice (policies, investigations, terminations, compliance), which is what the Counseling task codes cover; a dispute is filed under a Litigation matter type. The Counseling hints say so, and Litigation's hints point employment litigation to Litigation. Both live runs chose Counseling (0.85 / 0.90).
3. **Banking & Finance -> UTBMS Project.** The UTBMS Bankruptcy codes (B110 case administration, B120 asset analysis, B310 claims, plan and disclosure statement ...) are insolvency-case tasks; ordinary banking and finance work of a solvent business is lending and financing transactions, which the Project codes (due diligence, drafting, negotiation, closing) cover. Bankruptcy's hints say "not for ordinary banking or lending of a solvent business; use UTBMS Project".
4. **Negative control** "Office Catering" added to the case file (not a dev row) to prove none-fits live.
5. **`LineCodingSeamTests` refactor**: its private `AuthoredAction` class moved unchanged to `AuthoredActionMirror` so both Action seams share one renderer (no behaviour change; its tests still pass).
6. **Live evaluation data**: the live test loads the same seed files the dev seed uses into the fake Dataverse instead of reading dev, so it runs without Dataverse credentials and cannot write. The seed rerun reports the dev rows equal to the files (12 tuning columns unchanged, 161 codes unchanged).

## 3. Probe (embeddings alone, rich profiles, name-only reference text)

| Row | Top 1 (score) | Runner-up (score) | Correct? |
|---|---|---|---|
| Litigation | Litigation (.542) | Trademark (.342) | yes |
| Patent | IP Patent (.426) | Trademark (.318) | yes |
| Trademark | Trademark (.483) | IP Patent (.321) | yes |
| Employment | Litigation (.276) | Trademark (.273) | **no** |
| Commercial | Project (.328) | Trademark (.306) | yes |
| Appellate | Litigation (.328) | IP Patent (.244) | yes |
| IP Patents | IP Patent (.459) | Trademark (.368) | yes |
| IP Trademarks | Trademark (.529) | IP Patent (.365) | yes |
| Banking & Finance | Bankruptcy (.367) | Project (.320) | **no** |
| M&A | Project (.381) | Bankruptcy (.293) | yes |
| Commercial Transactions | Project (.430) | Trademark (.370) | yes |
| Office Catering (negative) | Project (.223) | Counseling (.222) | no "none" possible |

9/11 by embeddings (048 had Commercial, M&A and Commercial Transactions wrong as well). Margins are thin (Employment .276 vs .273), so the Action decides; the ranking only orders candidates and is the fallback.

## 4. Live evaluation (acceptance criterion 3)

Model: Azure OpenAI `spaarke-openai-dev`, Fast tier deployment `gpt-4o-mini` (the existing tier map; D-51), embeddings `text-embedding-3-large` (3072). Date 2026-10-16. Production service, facade, runner and renderer; the authored Action mirror; no examples (all rows unmapped, as on a fresh environment). Full report with reasons: `evaluation-set/code-set-matching/run-2026-10-16.md`.

| Row | Expected | Result | Confidence run 1 / run 2 |
|---|---|---|---|
| Litigation (MT) | UTBMS Litigation | UTBMS Litigation | 0.95 / 0.95 |
| Commercial (MT) | UTBMS Project | UTBMS Project | 0.90 / 0.90 |
| Patent (MT) | UTBMS IP Patent Prosecution | UTBMS IP Patent Prosecution | 0.95 / 0.95 |
| Trademark (MT) | UTBMS Trademark | UTBMS Trademark | 0.95 / 0.95 |
| Employment (MT) | UTBMS Counseling | UTBMS Counseling | 0.85 / 0.90 |
| Appellate (PA) | UTBMS Litigation | UTBMS Litigation | 0.90 / 0.90 |
| Intellectual Property Patents (PA) | UTBMS IP Patent Prosecution | UTBMS IP Patent Prosecution | 0.95 / 0.95 |
| Intellectual Property Trademarks (PA) | UTBMS Trademark | UTBMS Trademark | 0.95 / 0.95 |
| Mergers & Acquisitions (PA) | UTBMS Project | UTBMS Project | 0.95 / 0.95 |
| Banking & Finance (PA) | UTBMS Project | UTBMS Project | 0.90 / 0.90 |
| Commercial Transactions (PA) | UTBMS Project | UTBMS Project | 0.90 / 0.90 |
| Office Catering (negative) | none | none (closest: Counseling) | 0.90 / 0.90 |

**11/11 + negative control, both runs, all reviewed.** No tuning iteration was needed after the first draft of the descriptions, hints and prompt. Rerun: `$env:CODESET_MATCH_LIVE_OPENAI_ENDPOINT='https://spaarke-openai-dev.cognitiveservices.azure.com/'; $env:AZURE_TOKEN_CREDENTIALS='AzureCliCredential'; dotnet test tests/unit/Sprk.Bff.Api.Tests --filter FullyQualifiedName~CodeSetMatchingLiveTests` (optional `CODESET_MATCH_LIVE_REPORT=<path>`). About 25-30 s for 12 rows before fix-round-1; 10 s with parallel matching.

**Fix-round-1 rerun (2026-10-17 00:58Z, same deployments):** 12/12 again (11/11 + negative control), all reviewed, confidences 0.90-0.95. The none rationale for Office Catering now names only the similarity-closest set: "No code set fits: Office Catering is not a legal matter type ... so none of the candidate code sets ... applies. Map this row by hand or add a code set (closest by similarity: 'UTBMS Counseling')."

## 5. Live changes (spaarkedev1, 2026-10-16) and seeding

- `Deploy-EbillingBillingCodeSchema.ps1`: WhatIf (2 would-create), real (created `sprk_billingcodeset.sprk_description`, `sprk_matchhints`; scoped PublishXml of the four tables, not PublishAll), rerun (created 0, changed 0, skipped 20).
- `Seed-EbillingBillingCodes.ps1`: WhatIf (12 columns would fill, 0 writes), real (6 set rows PATCHed with If-Match, 12 columns filled), rerun (0 writes; 12 tuning columns unchanged; 161 codes unchanged).
- Read-only: Azure OpenAI calls for the probe and the two evaluation runs; Dataverse reads of reference rows and sets.
- **Not done here (task 053):** `scripts/ebilling/Ensure-ActionRow.ps1 -ActionCode invoice-codeset-match`; `scripts/Deploy-ActionMirrors.ps1 -Filter invoice-codeset-match` (-DryRun first); the BFF deploy together with `scripts/dataverse/Seed-PlaybookConsumers.ps1`, then `-DiffOnly` and `/healthz`. Until then dev proposals are all `reviewed: false` (binding_missing fallback), which is safe.
- No role, privilege or app setting changed.

## 6. Fix: 048 confirm lost update (coordinator fix-now item)

Found by the 048 adversarial review: `ConfirmAsync` re-read the row, then wrote unconditionally, so a mapping another admin confirmed between the re-read and the write was silently replaced and both admins got 200. Fix: the re-read selects `versionnumber`; the write goes through `IFieldMappingDataverseService.UpdateRecordFieldsIfUnchangedAsync` (Web API PATCH with `If-Match`, `@odata.bind` to `/sprk_billingcodesets({id})`); `DBConcurrencyException` (412) re-reads and decides again, up to 3 attempts; the second attempt sees the other admin's mapping and answers `AlreadyMapped` (409); exhausted attempts on a row that is still unmapped answer `ConcurrentChange` (409 `ebilling.codeset.concurrent_change`, its own detail; fix-round-1 K4) without writing; a missing version refuses rather than writing blind; `KeyNotFoundException` (row deleted) answers 404. Test `Confirm_WhenAnotherAdminMapsTheRowBetweenTheReReadAndTheWrite_Returns409Outcome_AndKeepsTheOtherMapping` lands the other admin's mapping after the re-read (`FakeCodingReferenceData.AfterQuery`). Mutation proof: with the old unconditional `UpdateAsync` swapped back in, it fails ("Expected outcome to be AlreadyMapped, but found Confirmed"); with the fix it passes. 048 notes L1 corrected.

## 7. Verification

- Build: `dotnet build src/server/api/Sprk.Bff.Api/` and the test project: 0 warnings, 0 errors.
- Finance + eval-contract + catalog + facade filter (`.Finance.`, `Contract.Eval`, `Contract.Catalog`, `InvoiceAi`, `ConsumerType`, `PlaybookConsumer`, `CodeSet`, `LineCoding`): **992 passed, 0 failed, 1 skipped** (the live test without its env var).
- Eval/catalog guards (`P2LoopInjectionEvalSuiteTests`, `CatalogInputSchemaContractTests`, `RoutingConsumerTypeHealthCheck`, `ConsumerTypes`): 123 passed.
- AI area (`.Ai.` + `PublicContracts`, every `IInvoiceAi` consumer and mock): 6291 passed, 0 failed, 1 skipped.
- `tests/Spaarke.ArchTests`: **1056 passed**, 0 failed. ADR-010 delta 0.
- A whole-project run of `Sprk.Bff.Api.Tests` was started and stopped after ~45 min still running (no result printed; filters above cover every area this task touches).
- Mutation proof of the race test: section 6. (Other new tests cover new behaviour that did not exist before this task; they were not mutation-run.)
- Publish size (section 10 method, `dotnet publish -c Release -r linux-x64 --self-contained false`, `Compress-Archive` Optimal, each tree from `git archive` into a short path): master `9b72ea2c8` 37.17 MB; task base `bbb10b78c` 37.38 MB; with this task `4a8e31930` 37.39 MB. **Task delta +0.01 MB** (branch vs master +0.22 MB); 192 files each. No package added; `dotnet list package --vulnerable --include-transitive`: none.
- Quality gates (Step 9.5, self-run; the main session runs the adversarial review): code-review and adr-check over the diff. ADR-013 (facade member only), ADR-032 (Null peer throws feature-disabled), ADR-039 (prompted runner, Action row holds prompt/schema/tier), ADR-002 (confirm is the one writer), ADR-015 (logs carry ids and reason codes, never names or model text), D-52 (no prompt, model or schema literal in C#). No finding left open.

## 8. Known limits (K-class)

- K1 (latency): GET proposals makes one Fast completion per unmapped row, 4 at a time since fix-round-1 (12 rows on dev: 10 s, was 21-31 s). ~100 unmapped rows now take about 60 s, inside the 230 s front-end timeout; several hundred unmapped rows would need paging (not needed for any known tenant).
- K2: resolved in fix-round-1 (examples are chosen by similarity).
- K3: a row mapped to a set that has since become unusable counts as `mapped` in coverage and is not proposed again (048 behaviour); the resolver skips such a mapping at coding time and the hook, by design, does not second-guess it.
- K4: the seed refills a tuning column an admin deliberately cleared (an empty column cannot be told apart from never-seeded). Edited text is never replaced.
- K5: `confidence` is the model's self-report, not a calibrated probability; the UI should show it as a hint only.
- K6: coverage is returned by the API only; no admin screen shows it yet (an admin UI is out of scope, escalation trigger 2 not fired).
- K7 (seed text updates; coordinator "K5"): the tuning seed fills a column only when it is empty, so it cannot tell outdated seed text from an admin's edit. If the seed text in `utbms-codeset-tuning.json` is improved later, environments already seeded keep the old text until an admin clears or edits the column. Scenario: a release improves the Counseling hints; spaarkedev1 keeps the 2026-10-16 hints and matches with them.
- K8 (hook cost once 049 wires a production consumer of `ILineCodingService`): each invoice coding job that reaches FR-22 step 4 (neither lookup mapped) builds a fresh scope that reads all usable sets, their Task codes and the confirmed mappings from Dataverse, embeds what the cache does not hold, and makes one or two Fast completions. With `IEmbeddingCache` registered the embeddings are cache hits after the first job; without it (no AI Search configured) every job embeds every set profile and every confirmed mapping name. Revisit with 049 (e.g. a short-lived cache of the loaded sets) if the volume of unmapped-reference invoices is high.
- K9 (embedding cache keys): the cache keys (`sprk-code-set-profile:v1`, `sprk-code-set-reference-name:v1`) do not include the embedding deployment, because `IInvoiceAi` does not expose it. If the embedding model behind `IInvoiceAi.GenerateEmbeddingAsync` changes, bump the key versions; otherwise cached vectors from the old model are compared with new ones until the 7-day TTL expires.
- K10 (probe): only the first row that reaches the Action decides whether the Action is "unavailable" for the request. A Binding added in the middle of a request is seen by the next request.

## 9. Deferred defect

- D1 (scale): **fixed in fix-round-1** (bounded parallel matching, see K1).

## 10. Fix-round-1 (coordinator review of 054)

| Item | Change | Test (fails before, passes after) | Mutation proof |
|---|---|---|---|
| F1 | Action constraint, none example and output-schema `reason` description no longer ask for / show a closest set; the constraint says the application reports it. `$version` stays 1 (all versioned mirrors are 1, no bump-on-text convention, not yet seeded). | `InvoiceCodeSetMatchActionContractTests.NoneAnswer_NeverNamesAClosestSet_...` failed on the old text | - |
| F3 guard | (test only) | Theory case `fifth-ranked`: the Action answers the 5th-ranked (usable, non-candidate) set, Trademark -> `Reviewed == false` | `candidates.FirstOrDefault` -> `ranked.FirstOrDefault`: that case turns red |
| F3 profile | (test only) | `Propose_EmbedsEachSetsProfile_...`: the Project profile holds the description, `Fits: ` + hints and `Task codes: P100 Project Administration; P210 Corporate Review`, and not the inactive P900 | name-only profile: red |
| D1 | Rows matched 4 at a time (`MatchParallelism`), results in row order, cancellation propagates, a throwing row is `Failed`; coverage gains `failed`; the test fake is thread-safe | in-flight (exactly 4), order, isolation, cancellation tests | `MatchParallelism = 1`, single-slot gate, unbounded gate: in-flight red; completion-order results: order red; no per-row catch: isolation red |
| Examples | The 10 most similar confirmed mappings, name embeddings once per scope | `Propose_SendsTheConfirmedMappingsMostSimilarToTheRow_...` ("Zz Corporate Deals" beats A00-A09) | ordering removed: red |
| K6 | Set profiles cached across requests in the existing `IEmbeddingCache` (optional ctor dependency; registered only with AI Search), key = set id + `modifiedon` + full profile text (includes the Task-code text); names cached by name | cache hit (second scope embeds nothing) and invalidation (an edited code re-embeds only its set; a newer `modifiedon` re-embeds only its set) | cache ignored, name cache removed, key without profile text, key without `modifiedon`: each red |
| K4 | Exhausted confirm on a still-unmapped row -> `ConcurrentChange` -> 409 `ebilling.codeset.concurrent_change`, own detail | seam + contract tests | exhausted -> `AlreadyMapped`: both red |
| K logging | Probe first row; the rest skip an unavailable Action; per-row logs Debug; one warning per issue per request | `Propose_WhenTheBindingIsMissing_ResolvesItOnce_AndLogsOneWarning...` (3 rows: 1 resolver call, 1 warning) | probe result ignored: red |

Beyond the brief (reported): row and example **names** also go through the cache, because similarity examples would otherwise add one embedding per confirmed mapping to every hook job (K8). The live test maps a `Failed` row to "failed" in its report.

Verification: BFF and test builds clean; CodeSet + LineCoding 175 passed, 1 skipped (live); eval/catalog (`P2LoopInjectionEvalSuiteTests`, `CatalogInputSchemaContractTests`, `ConsumerTypes`, `Contract.Catalog`) 136 passed; Finance filter 1001 passed, 3 skipped; ArchTests 1056 passed. No new interface (ADR-010 delta 0); no package. Publish size (same method as section 7): base `73e912d22` 37.41 MB, fix-round-1 37.41 MB, delta 0.00 MB, 192 files each. Live: Azure OpenAI calls for the evaluation rerun only; no Dataverse write.
