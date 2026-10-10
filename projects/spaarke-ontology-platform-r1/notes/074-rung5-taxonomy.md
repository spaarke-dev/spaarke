# Task 074, D-117(b): rung 5 reads the editable triage taxonomy

Branch `feat/074-rung5-taxonomy-ob`, cut from `origin/docs/ontology-platform-design`. The coordinator chose option B:
rung 5 depends on task 072's guidance read, which exists only on the project branch, and both reach master together
through the project merge (#1111). PR into the project branch.

## 1. Inventory: every consumer of rung 5's output

Rung 5 = `AiClassificationRung` → `ICommunicationClassificationAi` / `CommunicationClassificationAi` →
`CommunicationClassificationResult` {candidateRecordTypes, category, urgency, obligations, suggestedActions,
privilegeFlagged, rationale}. The result reaches consumers only through `RungMatch` → `AssociationStatusMapper`
→ `provenance.signals[]` (`SignalTrace`) → `sprk_communication.sprk_associationprovenance` (JSON). From there it is
read back by `PersistedClassificationSignalReader` (provenance string `ai-classify:category=..:urgency=..:types=[..]:actions=[..]:rationale`)
or by the UI directly.

| # | Consumer | Reads | Depends on rung 5's vocabulary? |
|---|---|---|---|
| 1 | `AssociationStatusMapper.Decide` (`Engine/AssociationStatusMapper.cs`) | every metadata-only match with a non-null `Category` → `SignalTrace`; rung 5 counts as an AI rung (`AiInvolved`) | No: it copies `Category` through. A signal is created only when `Category` is non-null; rung 5 substitutes the sentinel `communication-triage` |
| 2 | `PersistedClassificationSignalReader` | the provenance-string format, the `privilege-flag` signal, the `communication-triage`/`unspecified`/`(none)` sentinels | Format, yes. Vocabulary, no |
| 3 | TRIAGE-EMAIL step (`CommunicationEnrichmentService.RunEmailTriageAsync` → `CommunicationTriageAi.BuildInput`) | all 7 fields as `{classification:{...}}` Action input; triage runs only when the reader finds an AI-classify signal | The Action maps `category` onto the taxonomy (constraint 1; D-117(a) rewrites this as data) |
| 4 | RI-confidence (`RiConfidenceScorer.UrgencyWeightFromClassification`, enrichment lines ~607 and ~2182) | `urgency` (routine / elevated / urgent) when triage gave no priority | `urgency` vocabulary, yes. Untouched by this change |
| 5 | Propose step (`CommunicationEnrichmentService` ~900) | `privilegeFlagged` only (ADR-015 flag into the proposal JSON) | No |
| 6 | Review UI create suggestions: `src/client/shared/Spaarke.Communication.Components/src/logic/connections/provenance.ts` `deriveCreateActions`, `src/client/code-pages/CommunicationPage/src/components/provenance.ts`, `src/client/pcf/CommunicationActions/.../CommunicationActionsApp.tsx` `deriveSuggestedCreates` | `signal.category === 'invoice'` → "Link or create Invoice"; `category === 'event'`; obligations `deadline-response`, `payment-review`, `calendar-response` | **Yes.** Rung 5's free-form `invoice` drives the invoice suggestion. Taxonomy names ("Invoice / Billing") would silently drop it |
| 7 | Review UI AI-suggested types (`deriveAiSuggestedTypes`, shared `provenance.ts` ~461) | regex `types=\[...\]` over the provenance string | Provenance-string format, yes |
| 8 | Suggest-associations API (`SuggestAssociationsResponse`, `SuggestionCandidateAccess` access trim) | `SignalTrace` Category / Confidence / Provenance / Obligations | No. A new `SignalTrace` property is not exposed here |
| 9 | Office add-in (`communicationSuggestionsService.ts`) | sends `signals: []` | No |
| 10 | Telemetry: rung-5 structured log `Rung 5 (AI classify) fired \| ...` (task 032 hooks) | counts, privilege flag | No. One property added (`TriageCategory`) |
| 11 | Routing: `CategoryRoutingOptions` / `CategoryRoutingGate` | the **triage** result's category (step 2), not rung 5's | No |
| 12 | Record matching | rung 5 resolves no target and never contributes to a candidate | No |
| 13 | Tests: `AiClassificationRungTests`, `PersistedClassificationSignalReaderTests`, `AssociationLadderIntegrationTests`, `AssociationStatusMapperTests`, seam tests `EmailTriageSeamTests`, `TriagePersistenceSeamTests`, `CommsAssessedProducerSeamTests` (pinned legacy provenance JSON) | as above | Format plus sample values |
| 14 | Golden-utterance evals: `TriageEmailEvalTests` (+ create-task / propose families) | the Action contract; structural "no `ICommunicationClassificationAi` in the triage ctor" (FR-05) | No |
| 15 | 074 live harness (`stream/d-074-run` `ClassifierRecallLiveHarness`) | real DI graph: rung 5 → mapper → JSON → reader → triage | Registers `LookupChoicesResolver` already |

**Conclusion.** It is not safe to change `category` to the taxonomy: consumer 6 keys on `invoice` / `event`, and
consumer 7 and the reader key on the provenance-string format. So the change is **additive**. A new `triageCategory`
field travels structurally (`RungMatch` → `SignalTrace`, omitted from JSON when null) and leaves `category`, the
provenance string and every other field as they are. No consumer needs an email-project or owner decision.

## 2. Design

- `CommunicationClassificationAi` reads `AiClassificationOptions.TriageTaxonomyChoicesRef`
  (default `lookup:sprk_triagecategory.sprk_name`, the TRIAGE-EMAIL `category` reference). It reads through the
  existing `LookupChoicesResolver` (new public `ResolveChoicesReferenceAsync`, the single-reference form of
  `ResolveFromJpsAsync`, so the result has the same shape). Both steps therefore read the same enabled rows, with
  072's `sprk_enabled` filter, `sprk_classifierguidance`, the 1000/8000-char budget and `GuidanceKey`.
- The system prompt is the unchanged code constant plus a "triageCategory" instruction and a `Triage taxonomy:` list.
  Every list line comes from the rows ("name — guidance"; bare name when there is no guidance or the guidance read
  fails). The schema gains a required `triageCategory` `{type:string, enum:[live names]}`, the same enum shape
  `ActionRunner` injects for TRIAGE-EMAIL. No fee/scope word is in code.
- Rung 5 puts `TriageCategory` on its signal; `AssociationStatusMapper` copies it to `SignalTrace.TriageCategory`
  (`JsonIgnore WhenWritingNull`); the reader restores it; `CommunicationTriageAi.BuildInput` adds
  `classification.triageCategory` **only when present**, so an input without it is byte-identical to before.
  This is the informed hint for D-117(a).
- `HasUsefulSignal` is unchanged: a classification with only a `triageCategory` emits no signal, so the change
  creates no new triage runs.
- Degrades to exactly the pre-D-117 prompt and schema when the option is empty (kill switch, no redeploy), when the
  taxonomy resolves no rows, or when the read throws.
- Cost (FR-05): no extra model call. Per email, two small Dataverse reads (names + guidance) through the per-request
  resolver, the same reads TRIAGE-EMAIL already makes. No cross-request cache of this taxonomy exists; adding one
  is document-only (below). The prompt grows by the taxonomy list, at most the 8000-char guidance budget plus the
  names.
- `triageCategory` is a required non-null enum, like TRIAGE-EMAIL's own `category` enum, and the prompt says
  "always choose the closest one". This is deliberate: it is a hint, step 2 decides, and the taxonomy has catch-all
  rows (Administrative, Client instruction). A nullable variant was rejected because it adds a schema shape no
  other Spaarke schema uses (nullable enum), for a field that is only advisory. Duplicate row names are removed
  before the enum is built (the first row wins), so a duplicated row cannot get the schema rejected.
- DI: `TryAddScoped<LookupChoicesResolver>()` next to the facade registration, so the facade still resolves when
  `ToolFramework:Enabled=false` (where `AddToolFramework` skips the resolver).

## 3. §11 answers

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `CommunicationClassificationResult.TriageCategory`, `RungMatch.TriageCategory`, `SignalTrace.TriageCategory` | `Category` on all three | `Category` can't take taxonomy names: the review UI keys on `invoice` / `event` (inventory #6) | Step 2 gets no taxonomy-aligned hint; rung 5 keeps filing 46/55 fee-or-scope emails as general-correspondence/invoice (074 §8.4) |
| `LookupChoicesResolver.ResolveChoicesReferenceAsync` | `ResolveFromJpsAsync` | It is that method's body, extracted (`ResolveIntoAsync`). A synthetic JPS string would be the only alternative | Rung 5 can't read the taxonomy through the resolver; it would need a second, divergent reader |
| `AiClassificationOptions.TriageTaxonomyChoicesRef` | `Enabled`, `MaxInputChars` on the same options | Added to the existing options class | No way to revert rung 5 to its pre-D-117 prompt without a code deploy if the taxonomy prompt regresses association signals |
| `TryAddScoped<LookupChoicesResolver>()` | `AddToolFramework` registration | Idempotent TryAdd | With `ToolFramework:Enabled=false` the facade fails to resolve and rung 5 goes silent |

## 4. Document only (D-106)

- No cross-request cache for `sprk_triagecategory` reads: each triage run (and now each rung-5 run) reads the rows
  twice. A short-TTL cache would cut that, but it would delay an admin edit by the TTL. Not needed for D-117.
- The provenance-string format is parsed by both the reader and the UI regex (`types=[...]`). Fragile, pre-existing.
- The ADR-032 gate-combination constructibility matrix (`GateCombinationConstructibilityTests`) does not vary
  `ToolFramework:Enabled`; the `TryAddScoped` above is argued from the module order, not covered by that matrix.
- The 074 live harness (`stream/d-074-run`) records only rung 5's free-form category. Adding
  `Rung5TriageCategory` to its per-item result would show whether the hint was right; that is the 074 lane's
  call.
- On a Dataverse outage, `ChoicesResolutionTelemetry` failures for `sprk_triagecategory` now come from both rung 5 and
  TRIAGE-EMAIL, roughly double the count of the same outage.
- `PersistedClassificationSignalReaderTests.BuildProvenance` hand-mirrors the mapper's projection (the new tests
  use the real mapper instead).

## 5. Review and verification

- Adversarial review (one pass, read-only): no F-class finding. The duplicate-name finding (K2) was fixed; the others are recorded above.
- Golden-utterance evals 156/156; triage/comms seams 242 passed (1 skipped); full BFF unit suite 20017 passed,
  0 failed, 54 skipped; ArchTests 958/958.
- Publish size (fresh short-path worktrees; Release, framework-dependent linux-x64, `Compress-Archive Optimal`, PDBs
  included, 192/192 files): base `1df5a2c56` 38,340,655 B (36.56 MB) → head 38,343,757 B (36.57 MB), +3,102 B.
- `dotnet list package --vulnerable --include-transitive`: no vulnerable packages, and no package changes.
