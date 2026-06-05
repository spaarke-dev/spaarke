---
title: BFF AI Architecture Audit — Initial Findings
created: 2026-06-04
authored-by: Claude (Anthropic AI agent) during r3 design discussion
status: INPUT FOR AUDIT — captured before findings are lost to context
trigger: r3 design discussion surfaced multiple parallel intent-classification systems; audit project initiated per Option C
---

# Initial Findings — BFF AI Infrastructure Catalog

> **Critical preservation note**: These findings were surfaced during a focused ~30-minute investigation triggered by r3 design discussion. Owner explicitly chose Option C ("pause r3, do dedicated audit FIRST") based on these findings. This document is the primary input to the audit's actual work; do NOT lose context here.

---

## 1. Triggering observation

During r3 design discussion (Tier 2.5 "embedding-based intent classifier"), Claude (main session) recommended building "playbook self-description schema + indexing substrate" from scratch — ~2-3 week effort. Owner pushed back: *"in AI Search the index we have is 'playbook-embeddings' and there are already playbooks in the index; including fields for 'trigger phrases'"*.

Investigation confirmed: SprkChat Platform Enhancement R2 (completed 2026-03-17) had shipped exactly this infrastructure. Wave E2 of Insights Engine r2 built a parallel LLM-only classifier without leveraging it.

That single discovery triggered the broader audit captured below.

---

## 2. Parallel intent-classification systems (4 found — REAL DUPLICATION)

The BFF has accumulated FOUR independent intent-classification subsystems:

### 2.1 `CapabilityRouter` (AIPU2-012/013/014)

- **Path**: `src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/`
- **Architecture**: Three-tier
  - Layer 1 (AIPU2-012): Synchronous keyword classifier; <50ms; no LLM cost
  - Layer 2 (AIPU2-013): LLM intent classifier; single gpt-4o-mini call
  - Layer 3 (AIPU2-014): Broad superset fallback; synchronous; <1ms
- **Manifest source**: Dataverse-backed via `DataverseCapabilityManifestLoader` + `ManifestRefreshService`
- **Used by**: `SprkChatAgentFactory` (singleton)
- **Maturity**: HIGH — has manifest, validation, refresh, telemetry, options class
- **Files**: `CapabilityRouter`, `CapabilityManifest`, `CapabilityManifestEntry`, `CapabilityManifestInitializer`, `DataverseCapabilityManifestLoader`, `IManifestRefreshTrigger`, `ManifestRefreshService`, `CapabilityValidator`, `CapabilityClassificationPromptBuilder`, `CapabilityRouterOptions`, `CapabilityRoutingResult`

### 2.2 `PlaybookDispatcher` (SprkChat r2-015)

- **Path**: `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookDispatcher.cs`
- **Architecture**: Two-stage
  - Stage 1: Vector similarity over `playbook-embeddings` AI Search index (1.5s budget; ≥0.85 skips Stage 2)
  - Stage 2: LLM refinement + parameter extraction (0.5s budget)
- **Supports**: `PlaybookEmbeddingService`, `PlaybookIndexingService`, `PlaybookEmbeddingDocument`, scripts `Create-PlaybookEmbeddingsIndex.ps1`, `Index-ExistingPlaybooks.ps1`, `Seed-PlaybookTriggerMetadata.ps1`
- **Used by**: `SprkChatAgentFactory` (factory-instantiated)
- **Maturity**: HIGH — has full index, embedding pipeline, Redis cache, version key
- **Origin**: `projects/ai-sprk-chat-platform-enhancement-r2/` (Complete 2026-03-17)

### 2.3 `IntentClassificationService` (playbook-builder origin)

- **Path**: `src/server/api/Sprk.Bff.Api/Services/Ai/IntentClassificationService.cs`
- **Related**: `AiPlaybookBuilderService`, `BuilderAgentService`, `BuildPlanGenerationService`, `BuilderToolCall`, `PlaybookBuilderSystemPrompt`
- **Origin**: Likely an earlier SprkChat AI assistant for playbook AUTHORING (not consumption)
- **Maturity**: Unknown; needs investigation — may be deprecated path
- **Used by**: Unknown; investigation needed

### 2.4 `InsightsIntentClassifier` (Insights r2 Wave E2)

- **Path**: `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Routing/InsightsIntentClassifier.cs`
- **Architecture**: Single LLM call with hardcoded C# prompt (`BuildPrompt()` line ~230)
- **Decides**: BOTH path (playbook vs RAG) AND which playbook in one call
- **Used by**: `AssistantToolCallHandler` (post-Wave-E3)
- **Maturity**: Wave E2 ship; code comment line ~226 admits hardcoded prompt is wrong long-term, defers to Phase 2
- **Origin**: `projects/ai-spaarke-insights-engine-r2/` Wave E2

### 2.5 Architectural insight

These four systems serve DIFFERENT slices of similar problems:

- `CapabilityRouter`: routes user message → which **CAPABILITY/PATH** (e.g., "use a playbook", "use RAG", "use a chat tool")
- `PlaybookDispatcher`: once routed to "use a playbook", dispatches to **WHICH SPECIFIC PLAYBOOK**
- `IntentClassificationService`: dedicated to playbook **AUTHORING-time** intent (separate from consumption)
- `InsightsIntentClassifier`: combines capability routing + playbook dispatch into one LLM call for Insights consumers

**Reconciliation question for audit**:
- Should `InsightsIntentClassifier` be replaced by `CapabilityRouter.RouteAsync` (for path) + `PlaybookDispatcher` (for playbook selection)?
- Is `IntentClassificationService` still active or deprecated?
- Should the playbook-builder AI services be revived/extended/deprecated?

---

## 3. Lookup services (4 found — DRY VIOLATION, not duplication)

Path: `src/server/api/Sprk.Bff.Api/Services/Ai/`

| Service | Interface | Entity type |
|---|---|---|
| `PlaybookLookupService` | `IPlaybookLookupService` | Playbook |
| `ActionLookupService` | `IActionLookupService` | Action |
| `ToolLookupService` | `IToolLookupService` | Tool |
| `SkillLookupService` | `ISkillLookupService` | Skill |

**Observed identical structure** (verified via head -25 on all four):
- Same XML docs (verbatim wording about cached lookup, alternate keys, multi-environment)
- Same dependencies (`IGenericEntityService` + `IMemoryCache`)
- Same TTL (1 hour)
- Same SaaS multi-environment text
- Same constructor signature

**Refactoring opportunity**: replace with `CachedLookupService<TEntity>` generic — single implementation, type-parameterized. ~1-2 day refactor.

**Origin**: likely shipped together with the playbook-builder project (they all serve playbook-building lookups).

---

## 4. Search services (4 found — DISTINCT but uncoordinated)

| Service | Substrate / Index | Likely origin |
|---|---|---|
| `IRagService` / `RagService` / `NullRagService` | Multiple indexes (passes index name as parameter) | Generic RAG; shipped 2026-06-01 master refactor; used by Insights Wave E1 (`/api/insights/search`) |
| `SemanticSearchService` | Generic AI Search hybrid | Likely playbook-builder: "find similar existing actions/playbooks to reuse" |
| `RecordSearchService` | `spaarke-records-index` | Likely playbook-builder: "find Dataverse records to reference" |
| `PlaybookEmbeddingService` | `playbook-embeddings` (→ `spaarke-playbook-index` per r3 Tier 1.5) | SprkChat r2 playbook discovery |

**Not duplicates** — each has a distinct substrate for a distinct purpose. But the proliferation reflects accumulated investment without consolidation.

**Audit question**: Are `SemanticSearchService` and `RecordSearchService` still used by current production paths, or are they vestiges of an abandoned playbook-builder project?

---

## 5. Cache patterns (~11+ services use `IMemoryCache` directly)

Services that use `IMemoryCache` directly per grep:

```
NullInsightsIntentClassifier.cs
InsightsIntentClassifier.cs
InsightsActionRouter.cs
ToolLookupService.cs
SkillLookupService.cs
Security/PrivilegeGroupResolver.cs
IPrivilegeGroupResolver.cs
PlaybookLookupService.cs
Capabilities/CapabilityManifest.cs
AiPlaybookBuilderService.cs
ActionLookupService.cs
```

**Pattern observation**: each service rolls its own cache (custom TTL semantics, key derivation, eviction). TTLs vary:
- Lookup services: 1 hour
- `InsightsActionRouter`: 15-min sliding
- `InsightsIntentClassifier`: 15-min sliding
- `CapabilityManifest`: TBD (likely longer, manifest-bound)

**Not necessarily duplication** — appropriate diversity. But could benefit from a shared `ICachedLookup<T>` abstraction that takes TTL as constructor parameter. Existing `EmbeddingCache` (Redis, shared per R5 coord doc §3.1) is the precedent for shared cache infrastructure.

---

## 6. Prompt builders (3+ found)

| Builder | Purpose |
|---|---|
| `CapabilityClassificationPromptBuilder` | Builds prompts for `CapabilityRouter` Layer 2 |
| `OrchestratorPromptBuilder` | Builds prompts for orchestration |
| `InsightsIntentClassifier.BuildPrompt()` | Hardcoded C# string-builder; flagged for migration to JPS Action row |
| `PlaybookBuilderSystemPrompt` | Builder system prompt (playbook-builder project) |
| (likely others) | TBD |

**Pattern**: each subsystem builds prompts its own way. Some hardcoded, some in classes, some as constants, some in Dataverse JPS rows.

**Audit question**: Should there be an `IPromptBuilder<TContext>` pattern + a single source of truth for prompt content (likely Dataverse `sprk_analysisaction.sprk_systemprompt` rows per project's stated principle)?

---

## 7. Project origins inferred (5+ project accumulation pattern)

Best-guess attribution of accumulated infrastructure to source projects:

| Project | Likely BFF additions to audit |
|---|---|
| Earlier playbook-builder (SprkChat AI assistant for authoring) | `AiPlaybookBuilderService`, `IntentClassificationService`, `BuilderAgentService`, `BuildPlanGenerationService`, `BuilderToolCall`, `PlaybookBuilderSystemPrompt`, possibly `SemanticSearchService`, possibly the 4 lookup services |
| Capability routing project (AIPU2-012/013/014) | `CapabilityRouter`, `CapabilityManifest`, `CapabilityManifestEntry`, `DataverseCapabilityManifestLoader`, `ManifestRefreshService`, `CapabilityValidator`, `CapabilityClassificationPromptBuilder`, `CapabilityRouterOptions`, `CapabilityRoutingResult` |
| SprkChat r2 (March 2026; `ai-sprk-chat-platform-enhancement-r2`) | `PlaybookDispatcher`, `PlaybookEmbeddingService`, `PlaybookIndexingService`, `PlaybookEmbeddingDocument`, `PlaybookEmbeddingEndpoints`, `DynamicCommandResolver`, `playbook-embeddings` AI Search index, deploy scripts |
| Insights r1 (predecessor; `ai-spaarke-insights-engine-r1`) | `InsightsOrchestrator`, `IInsightsAi` facade, ingest pipeline, Insights nodes |
| Insights r2 (just shipped; `ai-spaarke-insights-engine-r2`) | `InsightsIntentClassifier`, `NullInsightsIntentClassifier`, `InsightsActionRouter`, `AssistantToolCallHandler`, `IInsightsAi.SearchAsync` + `AssistantQueryAsync` + `AssistantQueryStreamAsync`, RAG extension, citations href, SSE streaming, 3 `ILiveFactResolver` impls |
| R5 (in flight; `spaarke-ai-platform-unification-r5`) | Builds on top; reuse mandate documented in `projects/spaarke-ai-platform-unification-r5/notes/insights-r2-coordination.md` §3.1 |

**Pattern**: each project added what it needed. Nobody did holistic cleanup. R5 coord doc §3.1 documents the reuse mandate but ONLY for R5↔Insights — not cross-project for the BFF as a whole.

---

## 8. What the audit should produce

Based on these findings, the audit's deliverables should be:

### 8.1 Comprehensive inventory
- Every AI service in `src/server/api/Sprk.Bff.Api/Services/Ai/`
- Who uses each one (consumer mapping via grep)
- Whether it's active in production / deprecated / unused
- Originating project (best-guess)

### 8.2 Canonical-architecture decisions per category
For each category surfaced above (intent classification, lookup services, search services, cache patterns, prompt builders, possibly others discovered during audit):
- Which existing service is canonical
- Which should consume it
- Which should be deprecated/deleted
- Migration plan

### 8.3 Migration plan
Per service / category:
- Code paths that need updating
- Tests that need updating
- Deploy / config implications (cross-environment)
- R5 / SprkChat coordination required
- Effort estimate

### 8.4 r3 (and other downstream project) scope guidance
What can r3 lock now vs. what must wait for audit findings:
- r3 wave 1 (Tier 1 cleanup) — likely safe to proceed independent of audit (NullInsightsAi, test fixture hygiene, etc.)
- r3 wave 2 (Tier 2.5 reconciliation) — BLOCKED by audit; cannot lock scope without canonical decision

### 8.5 Process recommendation
Should Spaarke institutionalize periodic AI architecture review (e.g., per-quarter) to prevent recurrence?

---

## 9. Scope estimates

Audit project size (best estimate):
- Inventory: ~3 days (systematic file-by-file with consumer mapping)
- Canonical-architecture decisions: ~3 days (per-category analysis + owner review)
- Migration plan: ~2 days (per-service work-item sizing)
- Documentation + decision records: ~2 days
- Total: **~2 weeks** for the audit project

After audit completes:
- r3 wave 1 proceeds independently (~5d locked)
- r3 wave 2 scope set per audit findings (~1-3 weeks depending on decisions)
- r4 absorbs any "deferred to next project" items

---

## 10. References

### r3 design discussion artifacts (input to this finding)
- `projects/ai-spaarke-insights-engine-r3/design.md` — r3 design with Tier 1.5 + Tier 2.5 locks
- `projects/spaarke-ai-platform-unification-r5/notes/insights-r2-coordination.md` §8.12-§8.16 — R5 heads-up

### Memory notes
- `~/.claude/projects/.../memory/feedback-check-existing-infra-before-designing.md` — general lesson encoded

### Key code paths to audit
- `src/server/api/Sprk.Bff.Api/Services/Ai/` (entire AI service tree)
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/` (DI registration patterns)
- `src/server/api/Sprk.Bff.Api/Models/Ai/` (AI model proliferation)
- `infrastructure/ai-search/` (AI Search index definitions)
- `scripts/` (deploy/seed scripts)

### Related shipped projects
- `projects/ai-sprk-chat-platform-enhancement-r2/` (Complete 2026-03-17)
- `projects/ai-spaarke-insights-engine-r1/` (Complete)
- `projects/ai-spaarke-insights-engine-r2/` (Complete 2026-06-04)
- `projects/spaarke-ai-platform-unification-r5/` (in flight)

---

*Initial findings captured 2026-06-04 by Claude (main session) at owner's direction to preserve context for the dedicated audit project. Owner: Ralph Schroeder. Next action: audit project authors design.md based on these findings.*
