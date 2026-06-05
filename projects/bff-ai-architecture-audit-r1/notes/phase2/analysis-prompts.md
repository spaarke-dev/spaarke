# Phase 2 Analysis — Category 5: Prompt Builders

> **Authored by**: Phase 2 W3 Sub-Agent G
> **Pinned to**: commit `357e6936` (Phase 1 inventory snapshot)
> **HEAD at analysis time**: `3abbe918` (ZERO code drift on Cat 5 surfaces verified)
> **Scope boundary**: prompt-construction pattern decisions; out-of-scope per brief §4 = prompt content, token budgets, PromptLibrary schema, LLM model choice
> **Notable**: corrects W2 Cat 1 §3.1 cascade DELETE estimate (~100 LOC → ~1280 LOC); discovers 5th orphan (`BuildPlanGenerationService`)

---

## §1 Phase 1 baseline (verbatim from inventory §2.5 + §7.5)

From `c:\tmp\inventory-snapshot.md` §2.5:

> **§2.5.1 `CapabilityClassificationPromptBuilder`**
> - Path: `Services/Ai/Capabilities/CapabilityClassificationPromptBuilder.cs`
> - Purpose: Builds compact GPT-4o-mini prompt for Layer 2 routing (AIPU2-013). ≤600 token target. Static class.
> - Consumer: `CapabilityRouter` (Layer 2 helper).
> - State: **ACTIVE**.
> - DI: **NOT REGISTERED** (static class).
>
> **§2.5.2 `OrchestratorPromptBuilder` / `IOrchestratorPromptBuilder`**
> - Path: `Services/Ai/Chat/OrchestratorPromptBuilder.cs`
> - Purpose: Two-layer system prompt for orchestrator LLM. Layer 1 (stable prefix, ~2000 tokens, cached by manifest hash). Layer 2 (per-turn suffix, 0-3000 tokens, never cached). 9000-token total budget.
> - Consumers: `DirectOpenAiAgent`, `SprkChatAgent`, others via `IOrchestratorPromptBuilder` interface.
> - State: **ACTIVE**.
> - DI: Singleton in `AiCapabilitiesModule.cs:102-104` (concrete + interface).
>
> **§2.5.3 `PlaybookBuilderSystemPrompt`**
> - Path: `Services/Ai/Prompts/PlaybookBuilderSystemPrompt.cs`
> - Purpose: System prompts for AI Playbook Builder. 11 intent categories, confidence thresholds (Intent=0.75, Entity=0.80, ScopeMatch=0.70), canvas-state awareness. Static class.
> - Consumer: `IntentClassificationService` (§2.1.3 — itself unused). Indirect: `AiPlaybookBuilderService`.
> - State: **AT RISK** — consumed primarily by an orphaned service (§2.1.3).
> - DI: **NOT REGISTERED** (static class).
>
> **§2.5.4 Inline prompt construction (anti-pattern)** — many services build prompts inline; PromptLibrary exists with limited adoption.

From §7.5: "Three explicit builders, many inline. The `PromptLibrary` Cosmos-backed service exists but isn't broadly adopted. Should builders consolidate behind a single `IPromptComposer` interface?"

---

## §2 Empirical reproduction at HEAD `3abbe918`

### §2.1 The "three explicit builders" claim is INCOMPLETE

Empirical Grep at HEAD discovers **5 explicit prompt sources** (not 3) once `Services/Ai/` is walked:

| # | Source | Kind | DI | Lines | Inventory? |
|---|---|---|---|---|---|
| 1 | `Services/Ai/Capabilities/CapabilityClassificationPromptBuilder.cs` | static class, ≤600 token compact builder | not registered (static) | 151 | YES §2.5.1 |
| 2 | `Services/Ai/Chat/OrchestratorPromptBuilder.cs` | sealed class behind `IOrchestratorPromptBuilder` | Singleton (`AiCapabilitiesModule.cs:102-104`) | 483 | YES §2.5.2 |
| 3 | `Services/Ai/Prompts/PlaybookBuilderSystemPrompt.cs` | static class with 5 const strings + 2 builder methods | not registered (static) | 969 | YES §2.5.3 |
| 4 | `Services/Ai/AnalysisContextBuilder.cs` (`IAnalysisContextBuilder`) | instance class with `BuildSystemPrompt(action, skills)` + `BuildUserPromptAsync(...)` | Scoped (`AnalysisServicesModule.cs:282`) | ~200 | **MISSED** by inventory |
| 5 | `Services/Ai/FallbackPrompts.cs` | static class with `IntentClassification` const (fallback for ACT-BUILDER-001) | not registered (static) | ~150 | **MISSED** by inventory (referenced only as a §2.5.4 consumer) |

**Correction**: Cat 5 has FOUR formally registered/instantiable prompt sources plus ONE static fallback constant — not "three explicit + many inline."

### §2.2 PromptLibrary adoption — empirical reading

DI registration (`Infrastructure/DI/AiPersistenceModule.cs:102`): `services.AddScoped<IPromptLibraryService, PromptLibraryService>();`

Consumer Grep across `src/server/api/Sprk.Bff.Api/` finds **6 references, all in `Api/Ai/PromptLibraryEndpoints.cs`** (lines 106, 125, 141, 170, 195, 221 — one per REST handler).

**Verdict: ZERO non-endpoint consumers.** `IPromptLibraryService` is consumed EXCLUSIVELY by its own REST endpoints (6 routes under `/api/ai/prompts`). Not a single inline-prompt site, not a single explicit builder, not a single orchestrator references it.

PromptLibrary is **not "a prompt-construction abstraction with limited adoption" — it is a self-contained external CRUD/Render facade** for end-user-managed templates (Personal + Team tiers in Cosmos, Org + System tiers in Dataverse per AIPU2-035 / AIPU2-036).

### §2.3 `PlaybookBuilderSystemPrompt.cs` consumer audit — corrects W2 Cat 1's cascade DELETE claim

Empirical Grep `PlaybookBuilderSystemPrompt` across `src/server/api/Sprk.Bff.Api/`:

| Reference | File | Symbol |
|---|---|---|
| Definition | `Services/Ai/Prompts/PlaybookBuilderSystemPrompt.cs:15` | class |
| Consumer 1 | `Services/Ai/IntentClassificationService.cs:92` | `.IntentClassification` const |
| Consumer 2 | `Services/Ai/IntentClassificationService.cs:175,334,335` | `.Thresholds.IntentConfidence` const |
| **Consumer 3** | `Services/Ai/BuildPlanGenerationService.cs:126` | `.BuildPlanGeneration` const |
| **Consumer 4** | `Services/Ai/Builder/BuilderAgentService.cs:270` | `.Build(actions, skills, knowledge)` METHOD |

W2 Cat 1's cascade DELETE claim missed Consumers 3 and 4.

#### §2.3.1 Sub-cascade audit: `BuildPlanGenerationService`

DI registration Grep `IBuildPlanGenerationService|BuildPlanGenerationService` in `Infrastructure/DI/`: **ZERO matches.** Not registered.

Consumer Grep across `src/`: only the file itself. **ZERO non-self consumers.**

**Verdict**: `BuildPlanGenerationService` is a NEW orphan not in inventory §6.2's list of 4. It is dead code (~530 LOC).

#### §2.3.2 Live consumer: `BuilderAgentService.Build()`

`BuilderAgentService` IS DI-registered (`AnalysisServicesModule.cs:369`) and IS consumed by `AiPlaybookBuilderEndpoints.cs:1073` which is mapped inside the compound-AI gate (`EndpointMappingExtensions.cs:125`).

`PlaybookBuilderSystemPrompt.Build(actions, skills, knowledge)` (lines 729-927, ~200 LOC) IS active production code.

#### §2.3.3 What's actually live vs dead inside `PlaybookBuilderSystemPrompt.cs`

| Member | LOC est | Consumer at HEAD | State |
|---|---|---|---|
| `Thresholds.IntentConfidence` | 1 | `IntentClassificationService` (orphan) | DEAD post-cascade |
| `Thresholds.EntityConfidence` | 1 | ZERO | DEAD |
| `Thresholds.ScopeMatchScore` | 1 | ZERO | DEAD |
| `IntentClassification` const | ~100 (lines 36-135) | `IntentClassificationService` (orphan) | DEAD post-cascade |
| `BuildPlanGeneration` const | ~110 (lines 140-248) | `BuildPlanGenerationService` (NEW orphan) | DEAD post-cascade |
| `ToolExecution` const | ~256 (lines 253-509) | ZERO | DEAD (already dead) |
| `ScopeRecommendation` const | ~80 (lines 514-593) | ZERO | DEAD (already dead) |
| `PlaybookExplanation` const | ~63 (lines 598-660) | ZERO | DEAD (already dead) |
| `BuildCompletePrompt(canvasContext)` method | ~25 (lines 667-689) | ZERO | DEAD |
| `BuildCanvasContextSection(context)` private | ~25 (lines 694-719) | only `BuildCompletePrompt` | DEAD (transitive) |
| `Build(actions, skills, knowledge)` method | ~200 (lines 729-927) | **`BuilderAgentService.cs:270`** | **LIVE** |

**Verdict**: `PlaybookBuilderSystemPrompt.cs` is ~80% dead and ~20% alive. Whole-file DELETE is INCORRECT.

Estimated post-prune savings: ~750 LOC removed from `PlaybookBuilderSystemPrompt.cs` + ~530 LOC `BuildPlanGenerationService.cs` deleted = **~1280 LOC removed** (vs W2 Cat 1's ~100 LOC estimate).

### §2.4 Sampled inline-prompt sites — empirical confirmation

#### §2.4.1 `InsightsIntentClassifier.BuildPrompt()` (lines 230-274, ~45 lines StringBuilder)

XML doc admission (lines 225-228, verbatim): *"As more playbooks ship in Phase 2 the few-shot block can be extracted to `sprk_analysisaction.sprk_systemprompt` per the project's 'no .txt prompt files; prompts live in Dataverse' principle. For Phase 1.5's one-playbook scope, inline is the correct level of complexity."*

**Self-documented time-boxed inline.** Extraction trigger and destination both stated. The inline is justified for Phase 1.5.

#### §2.4.2 `PlaybookDispatcher.RefineWithLlmAsync()` inline (lines 294-300, 7 lines)

Stable 7-line raw-string literal. ZERO few-shots. ZERO parameters from options/config. **KEEP inline.**

#### §2.4.3 W2 Cat 1 §4.5 mislabel: `CapabilityClassificationPromptBuilder`

W2 Cat 1 §4.5 labels this as an "inline candidate." This is a **MISLABEL** — `CapabilityClassificationPromptBuilder.cs` IS the explicit builder source #1 in §2.1.

### §2.5 Additional inline sites discovered (variety sample)

| Site | LOC of inline prompt | Style | Stability | Recommendation |
|---|---|---|---|---|
| `Insights/Routing/InsightsIntentClassifier.cs:230-274` | ~45 | StringBuilder + 4 few-shots + JSON schema | self-documented temp inline | EXTRACT when Phase 2 ships |
| `Chat/PlaybookDispatcher.cs:294-300` | 7 | raw-string literal | stable, no fewshots | KEEP inline |
| `AnalysisActionService.cs:70,135` | 1 each | string literal fallback | fallback for Dataverse-sourced prompt | KEEP inline |
| `AnalysisOrchestrationService.cs:201` | delegates to `IAnalysisContextBuilder.BuildSystemPrompt` | abstracted | already canonical | KEEP |
| `BuilderAgentService.cs:270` | delegates to `PlaybookBuilderSystemPrompt.Build(...)` | abstracted | already canonical | KEEP |
| `FallbackPrompts.IntentClassification` const | ~125 | static const | live fallback for Dataverse miss | KEEP (static fallback pattern) |

**Verdict**: only ONE site (`InsightsIntentClassifier.BuildPrompt()`) is a self-flagged extraction candidate. The inventory §2.5.4 narrative overstates the prevalence.

---

## §3 Per-prompt-source decision table

| # | Source | Path | Decision | Rationale | Migration cost | Cross-team owner |
|---|---|---|---|---|---|---|
| 1 | `CapabilityClassificationPromptBuilder` | `Services/Ai/Capabilities/` | **KEEP (canonical reference: static-class compact builder)** | Single-purpose, token-budget-enforced; static class is correct | n/a | AIPU R2 |
| 2 | `OrchestratorPromptBuilder` + `IOrchestratorPromptBuilder` | `Services/Ai/Chat/` | **KEEP (canonical reference: two-layer instance builder + ADR-009 exception)** | Only file in `Services/Ai/` that follows ADR-009 §"MUST document" rigorously (W1 Cat 4 §4.3) | n/a | AIPU R1 |
| 3a | `PlaybookBuilderSystemPrompt.Build(actions,skills,knowledge)` method (lines 729-927) | `Services/Ai/Prompts/` | **KEEP, EXTRACT to `Services/Ai/Builder/BuilderAgentSystemPrompt.cs`** | Co-locate with sole consumer `BuilderAgentService` | XS | AI Chat Playbook Builder |
| 3b | `PlaybookBuilderSystemPrompt.*` dead members (~750 LOC) | `Services/Ai/Prompts/` | **DELETE** | ZERO live consumers post W2 Cat 1 orphan cascade | XS | AI Chat Playbook Builder |
| 3c | `PlaybookBuilderSystemPrompt.cs` (file) | `Services/Ai/Prompts/` | **DELETE (after 3a extracted)** | File becomes empty | XS | AI Chat Playbook Builder |
| 4 | `BuildPlanGenerationService.cs` (NEW orphan) | `Services/Ai/` | **DELETE** | Interface + impl with ZERO non-self consumers; not DI-registered; ~530 LOC. **5th orphan, NOT in inventory §6.2** | XS | AI Chat Playbook Builder |
| 5 | `AnalysisContextBuilder` + `IAnalysisContextBuilder` | `Services/Ai/` | **KEEP (canonical reference: scoped instance builder)** | DI Scoped; consumed by `AnalysisOrchestrationService`; legitimate seam. **INVENTORY MISS** | n/a | AIPL |
| 6 | `FallbackPrompts` | `Services/Ai/` | **KEEP (fallback constants)** | Live fallback when Dataverse unavailable | n/a | AI Chat Playbook Builder |
| 7 | `IPromptLibraryService` + `PromptLibraryService` (Cosmos) | `Services/Ai/PromptLibrary/` | **KEEP (NOT a prompt-construction abstraction; user-facing CRUD)** | ZERO non-endpoint consumers; reframe inventory §2.5.4 | n/a | AIPU R1 |
| 8 | `InsightsIntentClassifier.BuildPrompt()` inline (lines 230-274) | `Services/Ai/Insights/Routing/` | **KEEP inline, time-boxed extract** | Self-documented extraction trigger (Phase 2 multi-playbook); destination defined | XS when triggered | Insights team |
| 9 | `PlaybookDispatcher.RefineWithLlmAsync()` inline (lines 294-300) | `Services/Ai/Chat/` | **KEEP inline** | 7-line raw string with no fewshots — already maximally compact | n/a | SprkChat |
| 10 | `AnalysisActionService` line-67/135 fallback | `Services/Ai/` | **KEEP inline** | Single-line fallback when Dataverse `SystemPrompt` is null | n/a | AIPL |

**Total LOC delete impact**: ~1280 LOC (~750 LOC `PlaybookBuilderSystemPrompt` dead members + ~530 LOC `BuildPlanGenerationService` whole file). **Substantially larger than W2 Cat 1 §3.1's ~100 LOC estimate.**

**Combined bundled orphan-DELETE PR estimate** (W1 lookups + W2 Cat 1 + Cat 5 corrections): **~3000 LOC**, larger than W2 wave-2-summary §3 estimate of ~1700-1800 LOC.

---

## §4 Cross-cutting findings

### §4.1 REJECT `IPromptComposer` consolidation

| Builder | Input contract | Output contract | Token-budget | DI lifetime | Stable-prefix caching | Multi-stage |
|---|---|---|---|---|---|---|
| `CapabilityClassificationPromptBuilder.Build(userTurn, candidates[])` | string + `IReadOnlyList<CapabilityManifestEntry>` | `IList<ChatMessage>` (2 msgs) | ≤600 tokens hard | static | none | single message |
| `OrchestratorPromptBuilder.BuildSystemPrompt(routing, context)` | `CapabilityRoutingResult` + `OrchestratorPromptContext` | `OrchestratorPrompt` record | 9000 tokens with rebalancing | Singleton | Layer 1 cached by manifest hash | two-layer prefix+suffix |
| `PlaybookBuilderSystemPrompt.Build(actions, skills, knowledge)` | 3 `IReadOnlyList<ScopeCatalogEntry>` | `string` | none | static | none | single string |
| `AnalysisContextBuilder.BuildSystemPrompt(action, skills)` | `AnalysisAction` + `AnalysisSkill[]` | `string` | none | Scoped | none | single string |

**Verdict: REJECT generic `IPromptComposer` consolidation.** Reasons parallel Cat 1 §4.1 + Cat 3 §2.6: input shapes domain-specific; output shapes diverge; token-budget enforcement per-builder; DI lifetimes diverge (static vs Singleton vs Scoped); stable-prefix caching only in Orchestrator; multi-stage shapes differ.

**Recommendation**: pattern documentation, NOT interface abstraction. Two canonical reference impls:
- `CapabilityClassificationPromptBuilder` → "compact single-call builder" (≤600 token target).
- `OrchestratorPromptBuilder` → "two-layer prefix+suffix builder with stable-prefix caching" (Singleton, ADR-009 exception documented).

### §4.2 `OrchestratorPromptBuilder` is the gold-standard for ADR conformance

Verified at HEAD (`OrchestratorPromptBuilder.cs:36-44`): explicit "ADR-009 exception: prefix cache is in-process (MemoryCache), not Redis." comment.

**This is the binding precedent for any future BFF service that needs in-process caching of structural/derived metadata.** Cat 5 confirms Cat 4's designation.

### §4.3 PromptLibrary "limited adoption" is a category error — reframe

PromptLibrary is the BACKING SERVICE for an end-user-managed prompt template store. Evidence:
- `PromptTemplate.Body` uses `{{variableName}}` syntax (Mustache-style template substitution, not LLM-message-list assembly).
- `IPromptLibraryService` has CRUD methods (List/Get/Create/Update/Delete/Render) but NO `GetSystemPromptFor(string actionName)` or similar fetch-by-purpose method.
- Authorization model: per-user, per-team, per-tenant. LLM-call sites have a service-principal token context.
- `PromptLibraryEndpoints.cs:20`: "All routes require authentication (ADR-008). Tenant isolation is enforced by extracting the `tid` claim from the user's JWT" — user-facing API, not server-side abstraction.

**Conclusion**: PromptLibrary belongs to a different architectural layer — the "user-managed-template" layer, not the "LLM-call-site" layer. "Limited adoption" framing should be REPLACED with:

> PromptLibrary is the backing service for a user-managed prompt-template feature (AIPU2-035 / AIPU2-036). It is NOT the abstraction LLM-call sites route through. The 5 explicit prompt builders + N inline-prompt sites compose runtime LLM messages from manifest data, Action records, capability indexes, etc. — none of which match PromptLibrary's user-template-with-Mustache-variables data shape.

### §4.4 `PlaybookBuilderSystemPrompt.cs` correction is HIGH PRIORITY

**Path forward** (W2 Cat 1's cascade DELETE PR MUST be adjusted):

**Option A (PRUNE-IN-PLACE)**: Edit `PlaybookBuilderSystemPrompt.cs` to keep ONLY `Build(...)` + supporting records. ~750 LOC delete; 0 file rename.

**Option B (EXTRACT-THEN-DELETE — RECOMMENDED)**: Create new `Services/Ai/Builder/BuilderAgentSystemPrompt.cs` with `Build(...)` + supporting records. Update `BuilderAgentService.cs:270` reference. DELETE entire `Services/Ai/Prompts/PlaybookBuilderSystemPrompt.cs`. DELETE empty `Services/Ai/Prompts/` directory.

Both options also DELETE `BuildPlanGenerationService.cs` (~530 LOC NEW orphan).

### §4.5 `Services/Ai/Prompts/` directory becomes empty after Option B

Future "new prompts" do NOT belong in a generic `/Prompts/` directory — they belong co-located with their consumer.

### §4.6 Inline prompt count is OVERSTATED in inventory §2.5.4

Inventory §2.5.4 lists 7 inline sites; empirical count is 3-5 (after removing mislabels and self-documented time-boxed sites): 2 confirmed (InsightsIntentClassifier, PlaybookDispatcher) + 1 stable raw-string + up to 2 unverified.

### §4.7 No new security findings; no Cat 4 cross-cutting impact

- No prompt source caches authorization decisions.
- Only `OrchestratorPromptBuilder` participates in Cat 4 cache patterns (already documented and KEEP-verdict).
- No publish-size escalation expected from §3 deletes: ~1280 LOC C# = ~1 KB compressed; well under per-task threshold.

### §4.8 `PlaybookBuilderSystemPrompt.cs` cascade DELETE is NOT cleanly locked

**Cat 1 §3.1 should be updated** (or W2 wave-2-summary §3 footnote added) to reflect the corrected cascade extent before the bundled PR opens.

---

## §5 Canonical naming candidates (Q-004 framing)

### §5.1 Candidate A: "Spaarke Canonical Prompt Construction Pattern" (pattern doc, NO interface)

4-element empirical pattern: (1) Co-locate builders with sole consumer; (2) Choose lifetime by content stability (static / Singleton with cache / Scoped); (3) Token-budget enforcement with chars-per-token=4 heuristic; (4) Output shape returns LLM-ready primitives.

Pros: ADR-010 compliant; captures empirical best practice; applicable to FUTURE prompt builders without forcing consolidation; aligns with W2 pattern-doc framing.

### §5.2 Candidate B: "Spaarke Canonical Prompt Builders Stack" (umbrella for specialists)

Pros: mirrors Q-004 "Spaarke Canonical X Stack" convention. Cons: invites re-litigation; less actionable.

### §5.3 Candidate C: Promote `OrchestratorPromptBuilder` + `CapabilityClassificationPromptBuilder` as canonical reference impls

Matches Cat 1's promotion of `InsightsIntentClassifier` as canonical reference (W2 Cat 1 §5.3).

### §5.4 Sub-Agent G recommendation

**§5.1 (pattern doc) as primary** + **§5.3 (Orchestrator + CapabilityClassification as reference impls) as worked examples**. Same shape as Cat 1's recommendation. Pattern doc should cross-reference ADR-009 §"MUST document ADR-009 exception" convention.

---

## §6 Drift report (snapshot `357e6936` vs current HEAD `3abbe918`)

### §6.1 Code drift

`git log --oneline 357e6936..HEAD -- [5 Cat 5 paths]` → **EMPTY**. **ZERO code drift on Cat 5 surfaces.**

### §6.2 Inventory drift labels surfaced by Cat 5

| Inventory claim | Cat 5 correction |
|---|---|
| "Three explicit builders" (§2.5 header) | **Five** explicit prompt sources (adds `AnalysisContextBuilder`, `FallbackPrompts`) |
| `PlaybookBuilderSystemPrompt.cs` consumer: `IntentClassificationService` + indirect `AiPlaybookBuilderService` (§2.5.3) | **FOUR distinct consumers**: orphan `IntentClassificationService`, NEW orphan `BuildPlanGenerationService`, LIVE `BuilderAgentService`; `AiPlaybookBuilderService` references `FallbackPrompts`, NOT `PlaybookBuilderSystemPrompt` |
| Cascade DELETE scope: "~100 LOC" (W2 Cat 1 §3.1) | Cascade DELETE scope: **~1280 LOC** |
| §6.2 lists 4 orphans | **5th orphan**: `BuildPlanGenerationService` |
| "Many services build prompts inline" (§2.5.4) | **3-5 inline sites empirically**, not "many" |
| "PromptLibrary [...] limited adoption — most LLM-calling services do NOT route through it" (§2.5.4) | **PromptLibrary is NOT in the LLM-call-site layer.** User-facing CRUD facade; ZERO non-endpoint consumers |

**Aggregate verdict**: Phase 1 inventory's Category-5 narrative under-counts the surface (3 → 5), mis-states the cascade impact (~100 → ~1280 LOC), misses a 5th orphan, overstates inline prevalence, and mis-frames PromptLibrary. None are drift since snapshot — they are inventory accuracy corrections.

---

## §7 Open questions for owner review (Q-002)

1. **(HIGH)** Confirm 5th orphan: `BuildPlanGenerationService` (~530 LOC). AI Chat Playbook Builder team confirmation needed.

2. **(HIGH)** Adjust W2 Cat 1's `PlaybookBuilderSystemPrompt.cs` cascade DELETE scope. Pick Option A (PRUNE-IN-PLACE, ~750 LOC) or Option B (EXTRACT-THEN-DELETE — recommended).

3. **(MEDIUM)** Reframe inventory §2.5 "PromptLibrary limited adoption" — wrong architectural framing.

4. **(MEDIUM)** Designate canonical reference impls: `OrchestratorPromptBuilder` (two-layer cached + ADR-009 exception); `CapabilityClassificationPromptBuilder` (compact single-call).

5. **(MEDIUM)** Inventory miss correction: add `AnalysisContextBuilder` as 4th explicit builder.

6. **(MEDIUM)** Inline-prompt count correction in inventory §2.5.4 (7 → 3-5).

7. **(LOW)** Time-boxed inline `InsightsIntentClassifier.BuildPrompt()` extraction trigger: Phase 2 multi-playbook. Insights team owns.

8. **(LOW)** Pattern doc adoption (§5.1 Candidate A).

9. **(LOW)** Delete `Services/Ai/Prompts/` directory after Option B.

---

## §8 ADR candidates (per Q-005 — bullets only)

- **ADR-CAND-G-01 (HIGH)**: "Spaarke Canonical Prompt Construction Pattern" — codifies §5.1's 4-element pattern. Descriptive, NOT binding interface. Cross-refs ADR-009, ADR-010, ADR-013.

- **ADR-CAND-G-02 (MEDIUM, cross-coordinate with Cat 4)**: "In-process MemoryCache XML-doc convention for ADR-009 exceptions" — codifies the `OrchestratorPromptBuilder.cs:36-44` pattern as prescribed XML doc convention. Could be subsumed by Cat 4 §8 "BFF Canonical Cache Stack" candidate.

- **ADR-CAND-G-03 (MEDIUM)**: "Prompt source co-location rule" — prompt builders MUST be co-located with sole consumer subsystem; generic `/Prompts/` directories forbidden.

- **ADR-CAND-G-04 (LOW)**: "User-managed prompt template architectural layer" — PromptLibrary is a separate layer; codifies §4.3 reframe.

- **ADR-CAND-G-05 (LOW)**: "Time-boxed inline prompts MUST document extraction trigger" — codifies `InsightsIntentClassifier.cs:225-228`'s convention.

---

# Sub-Agent G Final Status Report

1. **Status**: COMPLETED (8/8 sections delivered)
2. **Output file path**: `projects/bff-ai-architecture-audit-r1/notes/phase2/analysis-prompts.md`
3. **Prompt sources analyzed**: 5 explicit (3 inventory + 2 NEW) + 1 user-CRUD facade (PromptLibrary) + 3 sampled inline sites
4. **Decision distribution**: 6 KEEP-as-is + 1 KEEP-with-EXTRACT (Build method) + 2 DELETE (dead members + `BuildPlanGenerationService` orphan) + 0 CONSOLIDATE-via-interface + 3 KEEP-inline + 1 KEEP-time-boxed-inline
5. **Drift findings**: ZERO code drift on Cat 5 surfaces (`357e6936` → HEAD `3abbe918`); all corrections are pre-existing inventory under-counts/mislabels
6. **Cross-cutting observations**:
   - 5 inventory corrections (not just drift labels): explicit-builder count 3→5; `PlaybookBuilderSystemPrompt` cascade scope wrong by 10×; NEW 5th orphan; inline-prompt count overstated; PromptLibrary mis-framed.
   - `OrchestratorPromptBuilder` is gold-standard ADR-009 exception reference (Cat 4 + Cat 5 confirm).
   - REJECT `IPromptComposer` interface consolidation; recommend pattern doc (consistent with Cat 1 + Cat 3 verdicts).
   - PromptLibrary reframe: user-facing template CRUD facade, NOT LLM-call-site layer.
   - Bundled orphan DELETE PR scope grows from ~1700-1800 LOC → **~3000 LOC**.
7. **Open questions for owner**: 9 items in §7; HIGH priority = confirm 5th orphan + adjust W2 Cat 1 cascade DELETE scope (Option A or B).
8. **Recommendations for W4 dispatch (DI + Configuration — final wave)**:
   - W4 precondition MET.
   - `AnalysisServicesModule.cs:282` (`IAnalysisContextBuilder` Scoped) inventory miss — add to W4.
   - `AiCapabilitiesModule.cs:102-104` (`OrchestratorPromptBuilder` Singleton) gold-standard — keep.
   - Bundle Cat 5's `BuildPlanGenerationService` DELETE with W2 Cat 1's bundled DELETE PR.
   - No additional Cat 7 or Cat 6 re-dispatch triggered by Cat 5.
