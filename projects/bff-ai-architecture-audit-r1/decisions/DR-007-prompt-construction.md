# DR-007 — Prompt Construction (Category 5)

> **Author**: Phase 3 Sub-Agent K (synthesis from Phase 2 outputs)
> **Date**: 2026-06-04
> **Status**: PROPOSED (pending Q-002 owner review)
> **Pinned to**: Phase 1 inventory commit `357e6936`
> **Source analysis**: [`notes/phase2/analysis-prompts.md`](../notes/phase2/analysis-prompts.md)
> **Canonical authority**: [`notes/canonical-architecture-decisions.md` §2.7](../notes/canonical-architecture-decisions.md) · §3 (W3 Cat 5 row) · §8.1 (W3-1) · §8.2 (W3-2, W3-3) · §8.3 (W3-4, W3-5)

## Context

Phase 1 inventory §2.5.4 catalogued "3 explicit prompt builders + many inline prompt construction sites" across `Services/Ai/` and framed `PromptLibrary` as having "limited adoption — most LLM-calling services do NOT route through it". Inventory questioned whether a generic `IPromptComposer` abstraction should consolidate the prompt construction layer.

W3 Sub-Agent G applied empirical reproduction and corrected the inventory substantially:
- **5 explicit prompt sources at HEAD** (not 3): `CapabilityClassificationPromptBuilder` (canonical compact), `OrchestratorPromptBuilder` (canonical two-layer cached), `PlaybookBuilderSystemPrompt` (80% dead + 20% alive), **`AnalysisContextBuilder`** (MISSED by inventory; `IAnalysisContextBuilder` Scoped), **`FallbackPrompts`** (MISSED by inventory; static fallback constants).
- **PromptLibrary architectural-layer reframe** — inventory's "limited adoption" framing is **architecturally misleading**. PromptLibrary is the BACKING SERVICE for an end-user-managed template store (Cosmos + Dataverse tiers per AIPU2-035/036), using `{{variableName}}` Mustache substitution; ZERO non-endpoint consumers — exactly as designed. PromptLibrary belongs to the **user-managed-template layer**, NOT the LLM-call-site layer.
- **NEW 5th orphan**: `BuildPlanGenerationService.cs` (~530 LOC) — missed by inventory + W2 Cat 1; W3 Cat 5 surfaced as part of the orphan cascade.

**`OrchestratorPromptBuilder` is canonical for two-layer cached prompts**: Singleton; Layer 1 stable prefix cached by manifest hash with 20-min TTL; Layer 2 per-turn suffix never cached; 9000-token total budget with rebalancing. Crucially, this file is the **ONLY file in `Services/Ai/` that documents in-process `MemoryCache` use per ADR-009's "MUST document ADR-009 exception" rule** — W3 designates it as the gold-standard XML-doc reference (see DR-002).

**`CapabilityClassificationPromptBuilder` is canonical for compact single-call prompts**: static class; ≤600 token target for Layer 2 routing.

**REJECT generic `IPromptComposer`**: input shapes domain-specific, output shapes diverge (`IList<ChatMessage>` vs `OrchestratorPrompt` record vs `string`), token budgets per-builder, DI lifetimes differ (static vs Singleton vs Scoped), stable-prefix caching only in `OrchestratorPromptBuilder`.

**Option B EXTRACT-then-DELETE for `PlaybookBuilderSystemPrompt.cs`** (REQUIRED to make bundled DELETE PR viable per DR-005): the file is 80% dead but contains a small live `Build(actions, skills, knowledge)` method consumed by `BuilderAgentService.cs:270`. The whole-file DELETE would break the build; Option A (PRUNE-IN-PLACE) keeps the file with 80% removed; Option B (EXTRACT-THEN-DELETE) moves the live method to a co-located file and deletes the original. W3 recommends Option B for prompt source co-location consistency.

## Decision

1. **KEEP 6 prompt sources** (5 explicit + PromptLibrary backing service for user-managed-template layer):
   - `CapabilityClassificationPromptBuilder` (canonical compact single-call)
   - `OrchestratorPromptBuilder` (canonical two-layer cached)
   - `AnalysisContextBuilder` (`IAnalysisContextBuilder` Scoped — MISSED by inventory)
   - `FallbackPrompts` (static fallback constants — MISSED by inventory)
   - `PlaybookBuilderSystemPrompt.cs` live tail (`Build(actions, skills, knowledge)`) — RELOCATED via Option B
   - `PromptLibrary` (user-managed-template layer; Cosmos + Dataverse tiers; Mustache substitution) — architectural layer distinct from LLM-call-site prompts

2. **EXECUTE Option B EXTRACT-then-DELETE for `PlaybookBuilderSystemPrompt.cs`** (REQUIRED — pre-condition for the bundled DELETE PR with DR-001 + DR-005):
   1. Create `Services/Ai/Builder/BuilderAgentSystemPrompt.cs` with live `Build(actions, skills, knowledge)` method (~200 LOC)
   2. Update `BuilderAgentService.cs:270` reference to point at new file
   3. DELETE entire `Services/Ai/Prompts/PlaybookBuilderSystemPrompt.cs` (~969 LOC original; ~340 LOC dead members after extraction)
   4. DELETE empty `Services/Ai/Prompts/` directory

3. **REJECT generic `IPromptComposer` abstraction**:
   - Input shapes are domain-specific.
   - Output shapes diverge structurally (`IList<ChatMessage>` chat-message list vs `OrchestratorPrompt` record vs raw `string`).
   - Token budgets vary by builder (≤600 vs 9000) — NOT a single shared budget.
   - DI lifetimes differ (static vs Singleton vs Scoped).
   - Two-layer cached stable-prefix is unique to `OrchestratorPromptBuilder`.

4. **DESIGNATE 2 canonical reference impls**:
   - `OrchestratorPromptBuilder` — two-layer cached prompt pattern
   - `CapabilityClassificationPromptBuilder` — compact single-call prompt pattern
   Both are PATTERN DOCS, NOT binding interfaces.

5. **CODIFY ADR-009 in-process exception XML-doc convention** using `OrchestratorPromptBuilder.cs:36-44` as the gold-standard reference (W3-2 ADR candidate; see DR-002 cross-reference).

6. **CODIFY prompt source co-location rule** — co-locate with sole consumer; FORBID generic `/Prompts/` directories for shared prompts (W3-3 ADR candidate). The `Services/Ai/Prompts/` directory deletion (Step 2 bullet 4) sets this precedent.

7. **REFRAME PromptLibrary** in inventory-correction PR — architectural-layer reframe: PromptLibrary is the user-managed-template layer (Cosmos + Dataverse tiers, per-user/team/tenant authz), NOT an LLM-call-site abstraction (W3-4 ADR candidate).

8. **CORRECT inventory** (canonical §6 rows 4, 5, 16): explicit-builder count 3→5; PromptLibrary framing reframe; missed `AnalysisContextBuilder` + `FallbackPrompts`.

## Consequences

### Positive
- `OrchestratorPromptBuilder` gains canonical reference status for two-layer cached prompts + ADR-009 exception XML-doc gold-standard.
- `CapabilityClassificationPromptBuilder` gains canonical reference status for compact single-call prompts.
- Option B EXTRACT-then-DELETE makes the bundled DELETE PR (DR-001 + DR-005 + DR-007) viable — without Option B, the bundle would fail to compile.
- PromptLibrary architectural-layer reframe corrects the inventory's misleading "limited adoption" framing.
- Co-location rule (no `/Prompts/` dirs) eliminates one source of orphan accumulation.

### Negative
- Option B EXTRACT-then-DELETE requires careful surgery on `PlaybookBuilderSystemPrompt.cs` — 80% dead + 20% live boundary must be correctly identified. Mitigation: AI Chat Playbook Builder team review of the extracted method signature; CI build verification.
- `Services/Ai/Prompts/` directory deletion is irreversible without git history; document the rationale prominently.

### Migration impact
- **Cross-team coordination**: AI Chat Playbook Builder team (Option B EXTRACT-then-DELETE owner; cascade-DELETE orphan owner per DR-005); AIPU R1 (`OrchestratorPromptBuilder` canonical confirmation); AIPU R2 (`CapabilityClassificationPromptBuilder` canonical confirmation).
- **Effort estimate**: **M (Medium)** — Option B EXTRACT (~200 LOC new file) + cascade DELETE (~340 LOC in PlaybookBuilderSystemPrompt + dir deletion). Bundled with DR-001 + DR-005 = ~2000 LOC total.
- **Sequencing**: Option B EXTRACT-THEN-DELETE is the **pre-condition** for the bundled DELETE PR. Must complete before DR-005's whole-orphan-cluster DELETE.

## Canonical naming (Q-004 — surfaced not locked)

- **Candidate**: "Spaarke Canonical Prompt Construction Pattern" (pattern-doc, NOT interface)
- **Reference impls**:
  - **Two-layer cached prompt**: `OrchestratorPromptBuilder` (Singleton; Layer 1 stable prefix cached by manifest hash with 20-min TTL; Layer 2 per-turn suffix never cached; 9000-token total budget with rebalancing) — at `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/OrchestratorPromptBuilder.cs`
  - **Compact single-call prompt**: `CapabilityClassificationPromptBuilder` (static class; ≤600 token target for Layer 2 routing) — at `src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/CapabilityClassificationPromptBuilder.cs`
- **Pattern elements** (4):
  1. Co-located with sole consumer (NO generic `/Prompts/` dir for shared prompts — W3-3 co-location rule)
  2. Token budget per-builder (≤600 vs 9000) — NOT a single shared budget
  3. Output shape per-builder (`IList<ChatMessage>` vs `OrchestratorPrompt` record vs `string`)
  4. Two-layer caching opt-in via stable prefix (Singleton only) — ADR-009 in-process exception XML doc convention (gold-standard at `OrchestratorPromptBuilder.cs:36-44`)
- **5 explicit prompt sources at HEAD** (CORRECTED from inventory's 3):
  1. `CapabilityClassificationPromptBuilder` (canonical compact)
  2. `OrchestratorPromptBuilder` (canonical two-layer cached)
  3. `PlaybookBuilderSystemPrompt` (80% dead + 20% alive — needs Option B EXTRACT-then-DELETE)
  4. `AnalysisContextBuilder` (`IAnalysisContextBuilder` Scoped) — MISSED by inventory
  5. `FallbackPrompts` (static fallback constants) — MISSED by inventory
- **Architectural-layer distinct**: `PromptLibrary` is user-managed-template layer (Mustache substitution; Cosmos + Dataverse tiers; per-user/team/tenant authz) — NOT LLM-call-site abstraction

## ADR candidates from this decision (Q-005 — bullets only)

- **W3-1** Spaarke Canonical Prompt Construction Pattern — HIGH priority (4-element pattern doc, NOT binding interface)
- **W3-2** In-process MemoryCache XML-doc convention for ADR-009 exceptions — MEDIUM priority (cross-references DR-002)
- **W3-3** Prompt source co-location rule — MEDIUM priority (forbid generic `/Prompts/` dirs)
- **W3-4** User-managed prompt template architectural layer — LOW priority (codifies PromptLibrary reframe)
- **W3-5** Time-boxed inline prompts MUST document extraction trigger — LOW priority

## Open questions for owner review (Q-002)

1. **Confirm Option B EXTRACT-then-DELETE** (canonical §11.1 Q-3): Owner confirms Option B (vs Option A PRUNE-IN-PLACE) for `PlaybookBuilderSystemPrompt.cs`?
2. **REJECT generic confirmation** (canonical §11.2 Q-8): Owner accepts `IPromptComposer` REJECTED + pattern-doc canonicalization?
3. **Canonical reference locks** (canonical §11.3 Q-14): Owner locks `OrchestratorPromptBuilder` + `CapabilityClassificationPromptBuilder` as canonical Prompt Construction references?
4. **PromptLibrary reframe**: Owner accepts architectural-layer reframe (user-managed-template layer, not LLM-call-site layer)?
5. **Co-location rule scope**: Codify "no `/Prompts/` shared dirs" as binding rule in `bff-extensions.md` §F, or pattern-doc only?
6. **`InsightsIntentClassifier.BuildPrompt()` extraction trigger** (canonical §11.7 Q-28; cross-references DR-005): Phase 2 multi-playbook timing — Insights team owns; when to extract the inline prompt?

## References

- Source analysis: [`notes/phase2/analysis-prompts.md`](../notes/phase2/analysis-prompts.md)
- Wave summary: [`notes/phase2/wave-3-summary.md`](../notes/phase2/wave-3-summary.md) §1.1 + §2.2 (W3 corrects W2 substantially) + §2.5 (ADR-009 documentation convention)
- Canonical authority: [`notes/canonical-architecture-decisions.md`](../notes/canonical-architecture-decisions.md) §2.7 + §3 + §5.3 (cross-sub-agent validation) + §6 (inventory corrections rows 4, 5, 14, 16) + §8.1+§8.2+§8.3 + §11.1 Q-3 + §11.2 Q-8 + §11.3 Q-14 + §11.7 Q-28
- Related ADR candidates: W3-1 (HIGH), W3-2/W3-3 (MEDIUM), W3-4/W3-5 (LOW)
- Related DRs: **DR-001** + **DR-005** (bundled DELETE PR — Option B EXTRACT-THEN-DELETE is pre-condition), **DR-002** (`OrchestratorPromptBuilder` is gold-standard ADR-009 XML-doc reference), **DR-006** (RagService consumes prompt builders indirectly via downstream LLM calls)
- ADR cross-references: ADR-009 (cache discipline + in-process exception convention), ADR-010 (interface budget cap), ADR-013 (facade-over-internal-SDK)
- Inventory corrections from this category: §6 rows 4 (explicit prompt count 3→5), 5 (PromptLibrary framing reframe), 14 (5th orphan `BuildPlanGenerationService.cs`), 16 (4 formally registered/instantiable + 1 static fallback)
