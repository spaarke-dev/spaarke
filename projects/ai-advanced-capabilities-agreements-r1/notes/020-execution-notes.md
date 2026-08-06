# Task 020 — Document classifier (Reasoning-tier work-type + sub-domain detection, registry-driven) — execution notes

> Rigor: FULL · Model tier: opus @ high · TEST-MODIFYING override (eval + contract tests added) → quality gates unconditional.
> Spec: FR-07 core · design Lens 3(d). Consumed by 021 (orientation + ≥0.85 gate) and 023 (explicit-path sanity check).

## Step 0 — Dispatch-path audit (audit-before-implement)

Traced how the two AI dispatch surfaces actually work (file:line evidence), because the packaging decision + the
registry-driven mechanism both hinge on it.

**Path A — `ActionRunner` (linear consumers / Compose review surface; where the sibling `agreement-review` runs):**
- Dispatched by `Services/Ai/LinearConsumers/ActionRunner.cs`. Uses **`action.OutputSchemaJson` (the static Dataverse
  column) verbatim** for constrained decoding (`ActionRunner.cs:154` `BinaryData.FromString(action.OutputSchemaJson)`),
  NOT the JPS-rendered schema.
- Renders `action.SystemPrompt` via `PromptSchemaRenderer.Render(...)` but passes **`templateParameters: null` and NO
  `preResolvedLookupChoices`** in every `BuildPrompt` arm (`ActionRunner.cs:353-359, 389-396, 414-420`). So at runtime
  ActionRunner resolves **no `$choices`** and applies **no template-parameter substitution**.
- Resolves the model tier via **`ModelTierDeploymentResolver.Resolve(action.ModelTier, options)`** (`ActionRunner.cs:134`)
  — ADR-016 Path C (catalog stores tier INTENT, config maps tier→deployment). `ModelTierDeploymentResolver`'s own class
  doc states it is the **single routing surface** and `ActionRunner` is its **sole caller**.
- Reference-knowledge grounding (`AllowsKnowledge`) is RAG over `spaarke-rag-references` only (`ActionRunner.cs:141`).

**Path B — `AiAnalysisNodeExecutor` + `GenericAnalysisHandler` (playbook node engine; the Insights Layer-1 path):**
- `AiAnalysisNodeExecutor` DOES resolve `$choices` lookups (`LookupChoicesResolver.ResolveFromJpsAsync` →
  `PreResolvedLookupChoices` on the tool context, `AiAnalysisNodeExecutor.cs:237-241, 459`), and `GenericAnalysisHandler`
  passes them to `Render(...)` and uses the rendered schema (`GenericAnalysisHandler.cs:262, 272`) — so registry-driven
  `$choices` DOES work end-to-end here **for a top-level output field**.
- BUT it selects its model by **deployment name** (`config.Model ?? ToolHandlerModel`, `GenericAnalysisHandler.cs:276`,
  default `gpt-4o-mini`) — it does **not** consult `ModelTierDeploymentResolver`. Getting the Reasoning tier here would
  require a hardcoded deployment name, which **violates ADR-016** ("catalog stores tier intent, not deployment name").

**Reconciling the triage-email `$choices` precedent:** `triage-email.action.json` declares `$choices:"lookup:…"` in its JPS
output, and its facade `CommunicationTriageAi` dispatches it via `IActionRunner.RunAsync` (`CommunicationTriageAi.cs:112`).
Since ActionRunner resolves no `$choices` and uses static `OutputSchemaJson`, that `$choices` is resolved at **deploy time**
into the static schema, not at runtime. Grep confirms **no LinearConsumers path calls `ResolveFromJpsAsync`/
`LookupChoicesResolver`**. So triage-email is NOT a counter-example — it confirms the finding.

## Step 1 — Packaging decision + §11 extension test

**DECISION: package the classifier as an Action row `agreement-classify` dispatched via `ActionRunner`** (flat
`systemPrompt` string + static `outputSchema` + `modelTier: Reasoning`) — the same shape/path as the sibling
`agreement-review`. Forced by:
1. **ADR-016 Reasoning tier (hard constraint)** is only resolvable on the ActionRunner path (`ModelTierDeploymentResolver`'s
   sole caller). The node/`GenericAnalysisHandler` path can only reach Reasoning via a deployment-name override → ADR-016
   violation. Reasoning tier ⟹ ActionRunner ⟹ Action row.
2. **Sibling parity + surface**: `agreement-review` is an ActionRunner Action on the Compose review surface; the
   classifier's consumers (021 orientation, 023 explicit) live on that same surface.
3. **Copies the Layer-1 CONTRACT, inverts the economics** (design Lens 3d): typed candidate set + per-candidate confidence
   + retry-once structured output, but Reasoning tier, not `gpt-4o-mini`.

**Registry-driven mechanism = prompt template-data injection (NOT a `$choices` schema enum).** Forced by:
- (a) ActionRunner uses static `OutputSchemaJson` and injects neither `$choices` nor template params at runtime (Step 0);
- (b) `$choices` resolves only on **top-level** `output.fields[]` (`LookupChoicesResolver.ExtractChoicesReferences` +
  `PromptSchemaRenderer.ResolveChoices` both iterate top-level fields), and `subDomainKey` lives inside `candidates[]` — a
  nested array-item field — so a `$choices`/enum could not reach it regardless of path.
- This is exactly what design Lens 3d + the task step 2 prescribe ("registry-supplied type list + cues injected as template
  data"). The output schema keeps `subDomainKey` a plain string; the **prompt** (assembled from the registry) is the
  model's single source of truth for the valid key set.

**§11 extension test — escalation NOT triggered.** Registry-driven injection rides the **existing ActionRunner dispatch**
via a small, cohesive **assembler** (`AgreementTypeRegistryPromptAssembler`) that materializes the Action's flat
`systemPrompt` (substituting a `{{agreementTypeRegistry}}` placeholder) **before** dispatch. `ActionRunner`,
`IActionRunner`, `PromptSchemaRenderer` are **UNCHANGED**. **No new executor, no new endpoint** (the escalation trigger's
exact conditions) — only a new read abstraction + a pure formatter. Question-by-question:
- *Existing?* `IScopeResolverService.QueryLookupValuesAsync` returns a single field per call and cannot correlate
  `sprk_key` ↔ `sprk_classificationcue` ↔ `sprk_isfallback` across rows → insufficient.
- *Extension?* Extending it to multi-field changes its contract for every `$choices` caller → worse. A narrow, dedicated
  reader is the smaller change.
- *Cost-of-doing-nothing?* Without the assembler the classifier prompt can't be built from the registry at runtime; the
  candidate set would have to be hardcoded (the defect the project forbids) and the type-agnostic promise fails.

## Step 2-4 — Deliverables authored

### Config (mirror-first, the primary artifact)
- `infra/dataverse/actions/agreement-classify.action.json` — flat `systemPrompt` (ROLE + two-level inference [is-agreement
  vs which sub-domain] + composite/multiplicity detection + per-candidate **calibrated** confidence with an explicit
  honest-uncertainty instruction tied to the ≥0.85 gate + a scope guard for non-agreements + the `{{agreementTypeRegistry}}`
  placeholder), static `outputSchema` `{isAgreement, candidates[{subDomainKey, confidence}], composite, reasoning}`,
  `modelTier: Reasoning`, `temperature: 0.1` (calibration stability; advisory catalog data — o-series may ignore it),
  `AllowsKnowledge` implicitly false (no RAG; self-contained over doc + injected cues, mirroring Layer-1 `knowledgeRetrieval:
  Never`). No registry key appears anywhere in the file.
- `infra/dataverse/outputschemas/agreement-classify.schema.json` — verbatim mirror of the embedded `outputSchema`
  (OpenAI-subset: `additionalProperties:false`, object-level required arrays, no property-level boolean required, no
  in-schema numeric/length bounds).
- `infra/dataverse/inputschemas/agreement-classify.input.schema.json` — `{documentText}` (maxLength 60000), mirroring
  `agreement-review`'s input.

### BFF assembly wiring (minimal; `Services/Ai/Classification/`)
- `AgreementTypeRow.cs` — DTO `{Key, Name, ClassificationCue, IsFallback, ConfidenceThreshold}`. `ConfidenceThreshold` is
  carried for the task-021 gate but is **NOT** injected into the classifier prompt (it must not bias the model's honest
  calibration).
- `IAgreementTypeRegistryReader.cs` + `DataverseAgreementTypeRegistryReader.cs` — reads active `sprk_agreementtype` rows
  (single `$select` of the 5 columns) via the **same HttpClient + managed-identity `TokenCredential`** pattern as
  `ScopeResolverService` (ADR-028). Registered `AddHttpClient<…>` symmetric with the ScopeResolverService registration.
- `AgreementTypeRegistryPromptAssembler.cs` — **pure** (no I/O): `BuildRegistryBlock(rows)` formats a deterministic block
  (specific types alpha-by-key, then the fallback; each row = key + label + cue, cue-less rows get a **key-derived
  guidance** line, the fallback row flagged via `sprk_isfallback` + named in a summary line — **no magic "general"
  string**); `Materialize(action, rows)` substitutes the block into `action.SystemPrompt`. Singleton.
- DI: registered inside the same compound `Analysis:Enabled && DocumentIntelligence:Enabled` gate as its future consumers
  (021/023 dispatch via ActionRunner, itself gated there) — **no asymmetric-registration** (CLAUDE.md §10 F.1); the pure
  assembler needs no Null-Object peer.
- **`ActionRunner` / `IActionRunner` / `PromptSchemaRenderer` UNCHANGED.**

### Runtime dispatch seam (for 021/023)
The classifier dispatch is: read rows via `IAgreementTypeRegistryReader` → `AgreementTypeRegistryPromptAssembler.Materialize`
→ `ActionRunner.RunAsync(materializedAction, documentOperand, ctx)` (Reasoning tier via `ModelTierDeploymentResolver`,
structured output via the static `OutputSchemaJson`, output cap `MaxOutputTokensCeiling=4000` — ample for the small
classification output; full-doc input bounded by the input schema's 60000-char cap). 021 (interactive chat-upload) and 023
(explicit-path sanity check) own the trigger + the confidence gate that calls this seam; both are gated in the same DI block.

## Step 5 — Eval set (authored; live env-blocked, mechanical layers green)

Live LLM grading is **env-blocked** (no Reasoning-tier Azure OpenAI deployment in spaarkedev1 — nda-r1 task 013; task 002
reported the same). So — exactly per the task ("run what runs") — the eval set is authored and the deterministic/contract
layers run green; the live routing + confidence values are documented (per-case `confidenceBand`) for when the deployment
lands.

- `tests/integration/contract/Eval/agreement-classify-eval-cases.json` — 6 cases across 5 families: **positive-specific**
  (AC-001 NDA→high), **positive-fallback** (AC-002 lease-like→lease-low|general-medium — the cue-less/fallback case),
  **negative-nonagreement** (AC-003 invoice, AC-004 pleading→isAgreement=false, no candidates), **composite** (AC-005
  employment+NDA addendum→both candidates, composite=true), **low-confidence-ambiguous** (AC-006→general fallback, low band).
- `tests/integration/contract/Eval/AgreementClassifyEvalTests.cs` (`[Trait("Category","GoldenUtteranceEval")]` — joins the
  eval-gate CI job with zero YAML change, the established net-new-family pattern). Mechanical assertions, all green:
  inventory integrity + closed vocabularies + family floors; gate invariants (non-agreement ⟹ no candidates ∧ not composite;
  composite ⟹ ≥2 distinct); **registry-grounding** (every expected key is a real `sprk_agreementtype` key AND is actually
  injected into the assembler's prompt block — no case references a key the classifier cannot emit); Reasoning-tier config.

## Acceptance criteria

| Criterion | Result | Evidence |
|---|---|---|
| NDA → isAgreement=true, top candidate nda high; lease-like → lower-confidence candidate OR general fallback, composite=false | **partial (env-blocked live)** | Cases AC-001 + AC-002 authored with these exact expectations; contract shape + registry-grounding asserted green; live routing/confidence env-blocked (no Reasoning deployment) |
| Composite employment+NDA → BOTH candidates, composite=true | **partial (env-blocked live)** | Case AC-005 authored; `CompositeCases_NameTwoOrMoreDistinctCandidates` green; live grading env-blocked |
| Negative invoice/pleading → isAgreement=false, no fabricated candidates | **partial (env-blocked live)** | Cases AC-003/AC-004; `NonAgreementCases_FabricateNoCandidates_AndAreNeverComposite` green; scope guard in prompt; live grading env-blocked |
| Type list assembled from registry at runtime; adding a stub row extends the enum with zero code (test with a stub) | **PASS** | `AgreementTypeRegistryPromptAssemblerTests.BuildRegistryBlock_WithStubRow_…` + `Materialize_RealClassifierAction_WithStubRow_…` — a `franchise-stub` row flows into the real shipped prompt with zero code change; fallback via `IsFallback` flag not a magic string; null-cue → key-derived guidance |
| Run routes to the Reasoning deployment (assert via config, not assumption); eval suite green | **PASS (config) / partial (live)** | `AgreementClassifyActionContractTests.Action_DeclaresReasoningModelTier` + `AgreementClassifyEvalTests.ClassifierAction_DeclaresTheReasoningTier…` assert `modelTier=Reasoning`; `ModelTierDeploymentResolver` (ActionRunner's sole caller) maps Reasoning→ReasoningModel (StandardModel fallback until the o-series deployment lands). Live dispatch env-blocked |

## §10 BFF Hygiene checklist (BFF .cs touched)

- **Placement Justification**: the classifier is an ADR-039 catalog capability on the AI dispatch surface; the assembler +
  reader are the smallest registry-driven-injection extension (ActionRunner unchanged; no new executor/endpoint — §11
  extension test above). Reader/assembler live in `Services/Ai/Classification/`, registered inside the existing compound AI
  gate. This IS the BFF; no extraction candidate.
- **Publish size**: `dotnet publish -c Release` → **47 MB compressed incl. PDBs** (142.97 MB uncompressed excl. PDBs). vs
  ~49.63 MB baseline → **delta ≈ 0** (no NuGet added; ~5 small `.cs` files). Well under the 60 MB ceiling; no escalation.
- **CVE**: `dotnet list package --vulnerable --include-transitive` → the only HIGH is `System.Security.Cryptography.Xml
  8.0.3` (transitive), **pre-existing** — this task added **zero** package references (the new code uses HttpClient /
  `Azure.Core.TokenCredential` / `System.Text.Json`, all already referenced by `ScopeResolverService`). **No NEW HIGH CVE.**
- **Tests updated**: assembler unit test + Catalog contract test + eval family added (KEEP paths; see below).
- `dotnet build` green (0 errors); new + sibling tests green (see Step 6).

## Step 6 — Quality gates (FULL + TEST-MODIFYING; self-run code-review + adr-check)

**Test runs:** new tests **24 passed / 0 failed / 0 skipped** (`AgreementTypeRegistryPromptAssemblerTests` [11],
`AgreementClassifyActionContractTests` [7], `AgreementClassifyEvalTests` [6]). Regression: DI-gating + sibling catalog/choices
**80 passed / 0 failed** (`AnalysisServicesModuleGatingTests`, `AgreementReviewOutputSchemaContractTests`,
`ChoicesResolutionTests`, `CatalogInputSchemaContractTests`).

**adr-check (self):**
- **ADR-039** (grounded execution / closed catalogs): classifier is prompt-controlled + schema-validated inside the closed
  catalog; NO second intent-detection mechanism outside the catalog (the registry block is prompt data on the one Action);
  output determinism = `fact` (the default) — classification synthesizes no legal judgment. PASS.
- **ADR-016** (rate limits / tier resolver): Reasoning tier via `ModelTierDeploymentResolver` (catalog stores tier intent,
  not deployment name); this was the load-bearing reason the Action-row packaging was chosen. PASS.
- **ADR-013** (facade / no new executor): reuses `ActionRunner` + `PromptSchemaRenderer` unchanged; the reader is a thin
  Dataverse read, the assembler is pure — no new executor, no new pipeline primitive. PASS.
- **project registry-driven constraint**: enum values come from the registry at prompt-assembly time; hardcoding the keys is
  a defect — the contract test asserts the placeholder is present + the key set is not hardcoded + `subDomainKey` has no
  schema enum; the fallback is selected via `sprk_isfallback`, no magic string. PASS.
- **project confidence semantics**: per-candidate calibrated confidence with an explicit honest-uncertainty instruction that
  the ≥0.85 gate depends on; per-row `sprk_confidencethreshold` is read into the DTO for the task-021 gate but kept OUT of
  the classifier prompt so it cannot bias calibration. PASS.

**code-review (self):** small cohesive surface; reader mirrors the proven `ScopeResolverService` auth pattern exactly;
assembler is pure + fully unit-tested incl. edge cases (null cue/name, empty rows, missing placeholder, deterministic order);
no secrets/PII logged (ADR-015: reader logs keys/counts only); DI registered symmetric with its gate (no asymmetric-reg);
tests are maintain-class in KEEP paths (`tests/integration/contract/{Catalog,Eval}/**`) + the assembler unit test sits with
its sibling `ChoicesResolutionTests` under `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/`; named per `{Method}_{Scenario}_
{Expected}`; no banned antipattern (no HttpMessageHandler mock, no DI/ctor-null test, no mirror/coverage-filler). PASS.

## Boundaries honored / deviations

- **Did NOT edit** sibling-owned files: `sprk_agreementtype-rows.json` (003 — READ only, for eval grounding),
  `agreement-review.action.json` + its schemas (002 — read-only reference), Compose.Components (052), the Binding row
  (`sprk_playbookconsumer-rows.json` — my packaging needs **no** Binding row; the classifier dispatch seam is
  reader→assembler→ActionRunner, and the client trigger + any Binding are 021/023 territory). No `.claude/**` writes; no
  git commit/push.
- **POML step 6 ("Update TASK-INDEX.md: 020 ✅") NOT performed** — superseded by the task's HARD BOUNDARY ("no
  current-task.md / TASK-INDEX.md edits"). POML `<status>` set to `completed`; the main session owns the TASK-INDEX flip.
- **Escalation: NONE.** Registry-driven injection rides the existing ActionRunner dispatch with no new executor/endpoint
  (§11 extension test above); the trigger condition was not met.
