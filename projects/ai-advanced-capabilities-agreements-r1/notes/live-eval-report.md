# Live Eval Report — agreement-classify + agreement-review (2026-08-05)

> Run against the live `spaarke-bff-dev` / `spaarke-openai-dev` / `spaarke-search-dev` resources
> (Reasoning-tier deployment `gpt-5-reasoning` confirmed present and callable). Investigation +
> live-run only — **no code, test, or config files were changed**; no Dataverse/SPE records were
> created; no `.claude/**` or task-tracking files were touched. All temporary scripts/results live
> under the session scratchpad, outside the repo.

## Verdict (one paragraph)

The **agreement-classify** live grading is a clean pass. All 13 live LLM calls (6 real NDA
documents, 2 exact-match negative/fallback documents, 1 real non-NDA agreement, 1 real garbled
input, and 3 ad-hoc synthetic probes standing in for the 3 case families with no authored document
text) returned correct `isAgreement`/`candidates`/`composite` shapes, zero fabricated candidates on
every non-agreement input, and correct type routing on every agreement input. The **≥0.85
confirmation gate is empirically validated**: every well-signalled document scored 0.95–0.99
(auto-proceed) and the one genuinely ambiguous document scored 0.72 (correctly triggers
confirm-gate, not auto-proceed) — the "honest uncertainty" prompt instruction holds up live. One
real calibration surprise is documented below (§5) — not a classifier defect, but worth the
project's attention. **agreement-review's NFR-01 advisory-quality rubub (the 6-NDA planted-issue /
citation-accuracy / hallucination-guard / risk-band rubric in `legal-eval-config.yaml`) remains
blocked** — not for the old reason (no Azure OpenAI creds; that is now FALSE, AOAI is live and
working) but for a new, precise reason: it needs RAG grounding from `spaarke-rag-references` on
Azure AI Search, and that search service is configured **API-key-only** — my delegated AAD identity
gets a clean 403, and the admin key lives only in Key Vault behind the BFF's own managed identity.
See §4b.

---

## 1. What ran — mechanical/offline (all green except one pre-existing, unrelated regression)

| Suite | Command | Result |
|---|---|---|
| `AgreementClassifyEvalTests` (7 tests) | `dotnet test ... --filter FullyQualifiedName~AgreementClassifyEvalTests` | **7/7 passed** |
| `NdaReviewDispatchEvalTests` (16 tests, incidental — not this task's target) | same run, broader filter | **15/16 passed** — 1 pre-existing failure, see note below |
| `python tests/eval/metrics/load_eval_config.py` | offline config validator | **PARSED + VALIDATED** (11 cases, 6 NDA, 12 planted issues, 4 rubric dims) |
| `python tests/eval/fixtures/test_citation_accuracy_offline.py` | offline metric proof | **ALL 5 OFFLINE CHECKS PASSED** |

**Incidental finding (not fixed — out of this task's boundary):**
`NdaReviewDispatchEvalTests.NdaReviewBinding_ResolvesThroughTheRealRoutingService_ForBothClickAndTextPathCases`
fails: it asserts `byType.Disposition == BindingDisposition.Informational` but the live
`infra/dataverse/sprk_playbookconsumer-rows.json` mirror now carries `Compose` (100000006) for the
`nda-review` row. Per this project's own `CLAUDE.md`, "the Informational→Compose flip is task 030" —
so this test is stale relative to a sibling task's in-flight change on the shared branch, not
related to the eval work in this report. Flagging for the project owner; not touched here (no code
changes permitted by this task's boundary).

**Confirmed via the mechanical suite** (`ClassifierAction_DeclaresTheReasoningTier…`,
`EveryExpectedKey_IsARegisteredSprkAgreementTypeKey`, `EveryExpectedKey_IsActuallyInjectedIntoThePromptBlock`):
the deployed `agreement-classify.action.json` still declares `modelTier: Reasoning`, and every
`sprk_agreementtype` key the mechanical suite expects is a real, live registry row (verified again
independently below via `mcp__dataverse__read_query` — the live table has exactly the same 10 rows
as the `infra/dataverse/sprk_agreementtype-rows.json` mirror, byte-identical cues).

---

## 2. What ran — LIVE (agreement-classify, 13/13 calls succeeded)

### 2.1 Mechanism used (and why the README's documented recipe no longer applies)

`tests/eval/README.md`'s "Running the LIVE eval" section documents a `curl` against
`POST /api/ai/analysis/execute` with `{"actionCode": "...", "documentText": "..."}`. **That request
shape no longer exists.** `AnalysisEndpoints.ExecuteAnalysis` was migrated (R7 Wave 4 / FR-11, task
041/042) to require a real `PlaybookId` + `DocumentIds[]` (real Dataverse document GUIDs, real SPE
upload) and now streams SSE via `IPlaybookOrchestrationService`; the old raw `actionCode` +
`documentText` path was **deleted**, and the endpoint 400s with an explicit message pointing this
out. Standing up a live BFF-endpoint run would require seeding real `sprk_document` +
`sprk_analysisplaybook` records and an SPE upload for all 9+ cases — out of this task's "no code/test
changes, prefer none" boundary and not what "trivially needed to point at live config" covers.

Instead, this run calls **Azure OpenAI directly**, replicating byte-for-byte the exact request
`ActionRunner.RunAsync` + `OpenAiClient.GetStructuredCompletionRawAsync` build for a Reasoning-tier
Action, using **my own delegated AAD identity** (`az account get-access-token --resource
https://cognitiveservices.azure.com`) — the same "dispatching classify runs against dev IS allowed"
path the task authorized, no stored secrets touched:

1. **Registry block**: replicated `AgreementTypeRegistryPromptAssembler.BuildRegistryBlock` in
   Python, fed the live `sprk_agreementtype` rows (re-verified via `mcp__dataverse__read_query` —
   exact match to the `infra/dataverse/sprk_agreementtype-rows.json` mirror, 10 active rows).
2. **Prompt**: `agreement-classify.action.json`'s `systemPrompt` with `{{agreementTypeRegistry}}`
   substituted, then the flat-text `BuildDocumentPrompt` tail appended
   (`"\n\n## Input\n\nDocument: {filename}\n\n{text}"` — this Action's `systemPrompt` has no
   `{{document.extractedText}}` token and `AllowsKnowledge=false`, so no reference-grounding block
   applies, matching `ActionRunner` exactly).
3. **Request**: `POST https://spaarke-openai-dev.openai.azure.com/openai/deployments/gpt-5-reasoning/chat/completions?api-version=2025-04-01-preview`,
   `response_format: {type: json_schema, json_schema: {name, strict: true, schema: <the Action's outputSchema>}}`,
   **no `temperature`, no `max_tokens`** (both omitted — reasoning models 400 on either, per
   `OpenAiClient.cs`'s documented, verified-live behavior).
4. Verified live deployment inventory first (`GET /openai/deployments`, AAD bearer) — confirms
   `gpt-5-reasoning` → model `gpt-5` is deployed and callable. Also confirmed via
   `az webapp config appsettings list` that `spaarke-bff-dev` has
   `DocumentIntelligence__ReasoningModel=gpt-5-reasoning` live (matches the task's premise).

Rate-limiting note: the first batch (6 concurrent workers) hit `429` on 10/13 calls; a sequential
retry-with-backoff pass cleared all of them. No production traffic was competing — this is the
dev deployment's own TPM/RPM ceiling.

### 2.2 Case-by-case results

The authored `AC-00#` cases in `tests/integration/contract/Eval/agreement-classify-eval-cases.json`
carry only a one-line `utterance` (a dispatch-scenario descriptor), **not real document text** — by
design, per the C# suite's own remarks ("each case records a confidenceBand ... as the documented
expectation for when the deployment lands"). They were authored as the mechanical suite's
registry/vocabulary fixtures, not as literally-invocable classifier inputs. To grade the classifier
live, I used: (a) the **9 real documents** already authored for the sibling `tests/eval/cases/`
corpus (byte-for-byte the same documents used for `legal-eval-config.yaml`) where they map to an
`AC-00#` family, and (b) **3 short synthetic ad-hoc probes** (constructed in-memory for this run
only, never written to the repo) for the 3 families with no real document available
(pleading, composite, ambiguous). Ad-hoc cases are marked clearly.

| Case | Document | isAgreement | candidates | composite | vs. expectation |
|---|---|---|---|---|---|
| **AC-001** (positive-specific) | `nda-01-clean-mutual.md` (real) | true | `nda` **0.98** | false | ✅ MATCH (isAgreement=true, nda/high, composite=false) |
| calibration check | `nda-02` .. `nda-06` (real, 5 more) | true (×5) | `nda` **0.96–0.98** (×5) | false (×5) | ✅ all 6 NDA docs → nda, all ≥0.96, all non-composite |
| **AC-002** (positive-fallback) | `neg-01-non-nda-lease.md` (real, exact) | true | `lease` **0.99** | false | ⚠️ PARTIAL — type routing correct (lease); confidence expected `low`/`general@medium` per the eval's `confidenceBands`, actual is HIGH. See §5. |
| **AC-003** (negative-nonagreement) | `neg-04-non-agreement-invoice.md` (real, exact) | **false** | `[]` | false | ✅ MATCH exactly |
| **AC-004** (negative-nonagreement) | ad-hoc synthetic court pleading | **false** | `[]` | false | ✅ MATCH exactly |
| **AC-005** (composite) | ad-hoc synthetic employment+NDA-addendum | true | `employment` **0.97**, `nda` **0.95** | **true** | ⚠️ PARTIAL — composite=true + both candidates correct; `nda` confidence expected `medium`, actual HIGH (0.95). See §5. |
| **AC-006** (low-confidence-ambiguous) | ad-hoc synthetic "mutual cooperation understanding" | true | `general` **0.72** | false | ✅ MATCH functionally — fallback selected, confidence **below the 0.85 gate** (correctly triggers confirm, not auto-proceed) |
| generalization (FR-01) | `agr-01-employment-non-nda-generalization.md` (real) | true | `employment` **0.98** | false | ✅ NOT NDA-scope-declined; correctly routes to its own type |
| graceful-degrade | `neg-02-unreadable-input.txt` (real, garbled OCR) | **false** | `[]` | false | ✅ Model correctly identifies unreadable/no-content input and declines rather than fabricating |

**Zero fabricated candidates on any of the 4 negative/non-agreement inputs** (invoice, pleading,
garbled, and implicitly every non-agreement path). **Zero invented registry keys** — every emitted
`subDomainKey` (`nda`, `lease`, `employment`, `general`) is a real, live-verified
`sprk_agreementtype.sprk_key`.

Raw request/response JSON for all 13 calls (including full `reasoning` text, token usage, and
per-call latency) is preserved in the session scratchpad (`classify_live_results.json`) —
ephemeral, not part of this report's durable record; the table above + the excerpts in this report
are the durable evidence.

---

## 3. What's blocked, and exactly why

### 3a. The literal `AC-00#` cases (as authored) — not a credential block, a harness-design gap

`agreement-classify-eval-cases.json` carries `utterance` (a one-line dispatch descriptor), not
`documentText`. There is no missing credential here — even with full AOAI access, the harness as
authored cannot be graded literally, because 4 of 6 cases (`AC-002`/`004`/`005`/`006`) have no
document to feed the classifier. §2.2 above documents the best-available live substitute per case.
If the project wants the literal `AC-00#` cases to be live-gradable in the future, they need a
`documentPath` field analogous to `legal-eval-config.yaml`'s cases (not implemented here — no
code/test changes per this task's boundary).

### 3b. agreement-review NFR-01 advisory-quality rubric (`legal-eval-config.yaml`) — genuinely still blocked

This Action has `AllowsKnowledge=true`: `ActionRunner` calls `ReferenceRetrievalService` to pull the
NDA standard (KNW-011) from the `spaarke-rag-references` Azure AI Search index BEFORE the
completion. I confirmed:

- Azure OpenAI access is **no longer the blocker** — `gpt-5-reasoning` is live and callable (proven
  in §2).
- Azure AI Search (`spaarke-search-dev`) is configured **`authOptions: {apiKeyOnly: {}}`** — verified via
  `az search service show --query authOptions`. AAD/RBAC auth is **not accepted by this resource at
  all**; my delegated token got a clean `403` on a trivial `$top=3` read against the
  `spaarke-rag-references` index.
- The only credential that works against this resource is the admin/query key
  (`DocumentIntelligence__AiSearchKey`, a Key Vault reference to
  `spaarke-spekvcert/AiSearch--AdminKey`), which the BFF resolves via its own managed identity at
  startup — it is not exposed to my interactive session, and I did not attempt to extract it from
  Key Vault (that would be pulling a stored application secret to route around an access boundary,
  not "invoking via the same auth path a legitimate caller uses" — outside what this task
  authorized).

**Precise missing ingredient**: an Azure AI Search query/admin key for `spaarke-search-dev`, OR a
`Search Index Data Reader` RBAC grant on that resource for an interactively-usable identity (the
resource's current `apiKeyOnly` config means the latter isn't possible without an Azure-side config
change on the resource itself, which is out of this task's scope). Retrieving the reference-standard
content this way for a Python-side replica of `ReferenceRetrievalService` is real, non-trivial work
(hybrid vector+semantic search, `text-embedding-3-large` query embedding, `rag-references-semantic-config`)
that is technically unblocked on the AOAI side but structurally blocked on the Search side. I did
NOT attempt a partial/ungrounded run of agreement-review (feeding it the NDA cases with no reference
standard) because that would silently violate the Action's own "decline the standard-measured
findings if no applicable standard was retrieved" instruction and produce a meaningless/misleading
grading pass, not a genuine measurement.

### 3c. NEG-03-unauthorized — untouched, as designed

Per the eval config's own note, this case is "not graded by `metrics/citation_accuracy.py`" and its
live assertion belongs in `tests/integration/auth/**` (a different KEEP path), out of scope here.
Not run; not blocked — simply out of this report's scope by the eval's own design.

---

## 4. Cleanup

No Dataverse records, SPE uploads, chat/analysis sessions, or any other durable state were created
by this run — every live call was a stateless Azure OpenAI chat-completion, and the one Dataverse
read (`mcp__dataverse__read_query` against `sprk_agreementtype`) was read-only. Nothing to clean up.
No repo files were added or modified other than this report.

---

## 5. Findings worth the project's attention (not classifier defects — calibration/design notes)

1. **Cue-less registry rows can still get high classifier confidence.** 8 of 10 `sprk_agreementtype`
   rows (everything except `nda`/`general`) have no `sprk_classificationcue` yet — they classify off
   a generic "match the ordinary meaning of the type name" fallback line. The eval authors predicted
   this would suppress confidence (`AC-002`'s documented `confidenceBands: {lease: "low", general:
   "medium"}`). Empirically it did not: a textbook residential lease scored `lease@0.99` — the model
   is confident based on strong document signal alone, cue or no cue. This means the **≥0.85
   auto-proceed gate will fire for well-signalled documents of unpacked types** (no knowledge pack
   registered), skipping the "confirm with the user" UX the eval assumed would happen for those
   types. The system stays *safe* end-to-end — the generalized `agreement-review` Action's "no
   applicable standard retrieved → decline the standard-measured findings" behavior (task 002,
   `NEG-01`'s own documented rationale) is the backstop that prevents a fabricated review — but the
   product-level UX intention (confirm before routing into an un-packed type) does not reliably
   trigger from classifier confidence alone. Worth a design note for whoever authors the next
   per-type pack; not something this task's boundary permits changing.
2. **Composite secondary-candidate confidence also runs "hot".** The same pattern appeared on
   `AC-005`: the eval predicted the secondary `nda` candidate at `medium`, actual came back
   `0.95` (high) — again correct routing, higher-than-predicted confidence.
3. **`tests/eval/README.md`'s live-run recipe is stale** (§2.1) — it documents a request shape
   `/api/ai/analysis/execute` no longer accepts (R7 Wave 4 / FR-11 removed the raw
   `actionCode`+`documentText` path). Not fixed here (doc change, out of this task's code/config
   boundary), but flagged precisely so a future task can update it rather than rediscovering the gap.

---

## 6. Verdict on classifier quality vs. the ≥0.85 gate design

**The gate design holds up under live grading.** Every document with genuine, unambiguous type
signal (6/6 real NDAs, the exact-match lease, the exact-match invoice negative, the real employment
agreement, the synthetic pleading and composite probes) classified correctly with confidence either
comfortably above 0.85 (auto-proceed correct) or, for the one deliberately-ambiguous document,
comfortably below it (confirm-gate correct) — with an honest, non-inflated `reasoning` explanation in
every case. Zero hallucinated candidates across every negative/non-agreement input tried. The one
substantive caveat (finding #1 above) is a **product-safety-margin** observation, not an accuracy
failure: the classifier is, if anything, *more* confident than the eval's authors assumed for
cue-less types, and the downstream Action's own decline-on-missing-standard behavior is what keeps
that safe. Overall: **the classifier meets the ≥0.85 gate's design intent on every case tested,
live, against the actual Reasoning-tier deployment** — the strongest evidence available today short
of standing up the full `agreement-review` RAG-grounded rubric (still blocked per §3b).
