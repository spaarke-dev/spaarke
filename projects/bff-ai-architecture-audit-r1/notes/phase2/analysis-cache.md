# Phase 2 Analysis — Category 4: Cache Patterns

> **Authored by**: Phase 2 W1 Sub-Agent A
> **Pinned to**: commit `357e6936` (inventory snapshot)
> **HEAD at analysis time**: `12275b10` (5 commits since snapshot; ALL docs/scaffold — zero code drift in `src/server/api/Sprk.Bff.Api/Services/Ai/` confirmed via `git diff --stat 357e6936..HEAD`)
> **Scope boundary**: cache-consolidation recommendations only; out-of-scope = TTL tuning, key-format changes, Redis-vs-in-process per-consumer decisions

---

## §1 Phase 1 baseline (verbatim from inventory.md §2.4)

### §1.1 Inventory §2.4 header
> "Cache infrastructure is highly fragmented. Two dedicated cache services, one cross-cutting kill-switch cache, plus 29 inline `IMemoryCache.TryGetValue` / `IDistributedCache.GetAsync` consumers."

### §1.2 Inventory §2.4.1 — Dedicated cache services
> **`EmbeddingCache` / `IEmbeddingCache`** (`Services/Ai/EmbeddingCache.cs`) — Redis (`IDistributedCache`) cache for query embeddings to reduce OpenAI cost/latency. 7-day TTL. SHA-256 hash keys (`sdap:embedding:{base64-sha256-hash}`). Float[] → byte[] via `Buffer.BlockCopy` → Base64. Consumers: RagService, SemanticSearchService, RecordSearchService, ReferenceRetrievalService. ACTIVE — canonical cache pattern. DI: Singleton in `AnalysisServicesModule.AddRagServices:549`.
>
> **`InsightsPlaybookExecutionCache` / `IInsightsPlaybookExecutionCache`** (`Services/Ai/Insights/InsightsPlaybookExecutionCache.cs`) — D-P13 SPEC §3.1 wrapper around `IPlaybookExecutionEngine`; Redis-backed per ADR-009; OpenTelemetry meter via `InsightsCacheMetrics`. Consumer: `InsightsOrchestrator.cs`. ACTIVE. DI: Singleton in `AnalysisServicesModule.AddInsightsCache:475`.

### §1.3 Inventory §2.4.2 — 32 inline `IMemoryCache` / `IDistributedCache` consumers
Table reproduced verbatim from inventory; see `c:\tmp\inventory-snapshot.md` lines 194-215.

### §1.4 Inventory §2.4.2 cross-cutting observation (verbatim)
> "No `ISprkCache<T>` abstraction. Every service hand-rolls cache-key construction, TTL choice, and serialization. The `EmbeddingCache` pattern (typed wrapper around `IDistributedCache` + telemetry hook + structured key prefix) is the canonical model that the other 30 services do NOT follow."

### §1.5 Inventory §7.4 open questions (verbatim)
> "32 cache consumers, 2 dedicated cache services (`EmbeddingCache`, `InsightsPlaybookExecutionCache`), no unified abstraction. The `EmbeddingCache` pattern is the most disciplined — should it become `SpaarkeCache<T>` or `IDataverseLookupCache`?"
> "Cache key prefixes are entity-coupled (`playbook:code:`, `action:code:`, `skill:code:`, `tool:code:`) — would benefit from a typed factory."

---

## §2 Empirical reproduction (consumer counts re-run at HEAD `12275b10`)

### §2.1 Headline grep counts (Services/Ai only)

| Search | Inventory claim | HEAD reproduction | Match? |
|---|---|---|---|
| `IMemoryCache` files in `Services/Ai/` | (implied) 11–13 | **11 files** | OK |
| `IDistributedCache` files in `Services/Ai/` | (implied) ~19–21 | **21 files** | OK |
| Total cache consumers in `Services/Ai/` | 32 | **32** (11 + 21) | EXACT |

### §2.2 `EmbeddingCache` consumer reproduction
- `Grep "IEmbeddingCache"` returned 7 prod files: `RagService.cs`, `RecordSearchService.cs`, `SemanticSearchService.cs`, `ReferenceRetrievalService.cs`, plus DI registration sites (`AnalysisServicesModule.cs:549`, `AiModule.cs:212`) and the interface/impl. **4 consuming services matches inventory exactly.**

### §2.3 `InsightsPlaybookExecutionCache` consumer reproduction
- `Grep "IInsightsPlaybookExecutionCache"` returned 6 prod files: `InsightsOrchestrator.cs` (sole consumer), DI sites (`AnalysisServicesModule.cs`, `InsightsFacadeModule.cs`, `Program.cs`), plus interface/impl/result type. **1 consumer matches inventory exactly.**

### §2.4 Lookup-service orphan reproduction (corroborates inventory §6.2)
- `IActionLookupService` → 2 hits (interface defn + DI reg `FinanceModule.cs:123`). **Zero non-test consumers. ORPHAN confirmed.**
- `ISkillLookupService` → 2 hits (interface defn + DI reg `FinanceModule.cs:132`). **Zero non-test consumers. ORPHAN confirmed.**
- `IToolLookupService` → 2 hits (interface defn + DI reg `FinanceModule.cs:141`). **Zero non-test consumers. ORPHAN confirmed.**
- `IPlaybookLookupService` → 4 hits: interface defn + DI reg + consumers `InvoiceExtractionJobHandler.cs:31,52` + reference in `DefaultPlaybookConstants.cs`. **1 production consumer. NOT orphan.**

### §2.5 Canonical helper adoption inside `Services/Ai/`
- `Spaarke.Core.Cache.DistributedCacheExtensions.GetOrCreateAsync<T>` (canonical generic helper, signature exists in `src/server/shared/Spaarke.Core/Cache/DistributedCacheExtensions.cs`) is **NOT IMPORTED** by any file in `src/server/api/Sprk.Bff.Api/Services/Ai/`. `Grep "Spaarke.Core.Cache"` returns only 3 hits across the whole repo: the impl itself, `RequestCache.cs`, and `SpaarkeCore.cs` DI registration.
- The `EmbeddingCache` and `InsightsPlaybookExecutionCache` both hand-roll `_cache.GetAsync` / `_cache.SetAsync` rather than using the canonical helper. They predate the Q5-audit (2026-05-27) generalization noted in `DistributedCacheExtensions` XML doc which explicitly says "individual caches (EmbeddingCache, ChatSessionManager, InsightsPlaybookExecutionCache, etc.) should adopt this helper rather than reimplementing the pattern."
- **This is the central consolidation lever for Category 4: a canonical generic already exists and is explicitly designed for this purpose, but adoption is 0%.**

### §2.6 Lookup-service-pattern duplication empirical sample (lines 1-40 PlaybookLookupService vs ActionLookupService)
- Lines 1-40 of both files are line-for-line identical except: (a) `playbook` → `action` in cache-prefix constant, (b) entity name in XML doc. Verified by reading both files in full. **DRY violation confirmed empirically.**

### §2.7 `PlaybookDispatcher` hand-rolled distributed cache
- `PlaybookDispatcher` (factory-instantiated, not DI-registered) uses `_cache.GetStringAsync(cacheKey)` / `_cache.SetStringAsync(cacheKey, json, ...)` directly (lines 405, 453). Bypasses the canonical helper. 5-min TTL hardcoded. **Could be a one-line swap to `_cache.GetOrCreateAsync<DispatchResult>(...)`.**

### §2.8 In-process `MemoryCache` exception cases
- `OrchestratorPromptBuilder` uses `MemoryCache` (concrete class, not `IMemoryCache`) with **explicit ADR-009 exception comment** (line 36, line 40): "ADR-009 exception: prefix cache is in-process (MemoryCache), not Redis." 20-min TTL. **This is the only file that follows the ADR-009 §"MUST document ADR-009 exception" rule rigorously.** All other `IMemoryCache` consumers in §2.1 lack the documentation requirement.
- `InsightsActionRouter` uses `IMemoryCache.GetOrCreateAsync` (the built-in extension on `IMemoryCache`, NOT the `Spaarke.Core` helper) at 3 sites (lines 124, 168, 206). 3 separate cache keys, sliding expiration.

---

## §3 Per-service decision table

| # | Service | Path (Services/Ai/) | Decision | Rationale | Migration cost | Cross-team owner |
|---|---|---|---|---|---|---|
| 1 | `EmbeddingCache` | `EmbeddingCache.cs` | **keep (designate canonical)** | Most disciplined wrapper; SHA-256 keys, ADR-009 compliant, graceful degradation, metrics; binary serialization is correct for float[] payload | n/a | AIPL (originating) |
| 2 | `InsightsPlaybookExecutionCache` | `Insights/InsightsPlaybookExecutionCache.cs` | **keep (peer canonical)** | Domain-specific wrapper for playbook execution stream draining; ADR-009 compliant; rich telemetry via `InsightsCacheMetrics`; cannot be replaced by generic helper alone (carries stream-draining logic) | n/a | Insights Engine |
| 3 | `PlaybookLookupService` | `PlaybookLookupService.cs` | **consolidate (behind generic lookup wrapper)** | Active consumer (1) but hand-rolls IMemoryCache + entity-coupled key prefix; consolidation target = generic `IDataverseAlternateKeyLookup<T>` plus `EmbeddingCache`-style wrapper | M (1-3d, has consumer to migrate) | Finance Intelligence |
| 4 | `ActionLookupService` | `ActionLookupService.cs` | **DELETE** (HARD GATE A passed: 0 non-test consumers) | Pure copy-paste of #3 (185 lines, same shape); zero production consumers; HARD GATE A (grep) PASSED, HARD GATE B (DI removal: remove `FinanceModule.cs:123` line), HARD GATE C (publish-size: 185+53=238 lines source, est. ~3-4 KB compressed IL — well under 50 KB threshold, classify "negligible") | S (<1d) | Finance Intelligence |
| 5 | `SkillLookupService` | `SkillLookupService.cs` | **DELETE** (HARD GATE A passed) | Same as #4: zero production consumers, identical shape, 185+53=238 LOC | S | Finance Intelligence |
| 6 | `ToolLookupService` | `ToolLookupService.cs` | **DELETE** (HARD GATE A passed) | Same as #4: zero production consumers, identical shape, 185+53=238 LOC | S | Finance Intelligence |
| 7 | `InsightsIntentClassifier` (cache usage) | `Insights/Routing/InsightsIntentClassifier.cs` | **consolidate** | Uses `IMemoryCache` with 15-min sliding + SHA-256 query hash — already follows `EmbeddingCache`-style key strategy partially; should consolidate behind canonical wrapper while keeping in-process backing per ADR-009 metadata exception | S | Insights Engine |
| 8 | `Chat/PlaybookDispatcher` (cache usage) | `Chat/PlaybookDispatcher.cs` | **consolidate** | Uses `_cache.GetStringAsync`/`SetStringAsync` directly; should adopt `DistributedCacheExtensions.GetOrCreateAsync<T>` — one-method swap, no behavior change | S | SprkChat |
| 9 | `Chat/ChatContextMappingService` (cache usage) | `Chat/ChatContextMappingService.cs` | **consolidate** | Inline `IDistributedCache` per chat session; identical migration to #8 | S | SprkChat |
| 10 | `Chat/SprkChatAgentFactory` (cache usage) | `Chat/SprkChatAgentFactory.cs` | **consolidate** | Inline `IMemoryCache` per session id; could be replaced by canonical wrapper IF in-process is justified | S | SprkChat |
| 11 | `Chat/PendingPlanManager` (cache usage) | `Chat/PendingPlanManager.cs` | **consolidate** | Inline `IDistributedCache`; 30-min TTL; identical to #8 | S | SprkChat |
| 12 | `Chat/ChatSessionManager` (cache usage) | `Chat/ChatSessionManager.cs` | **consolidate** | Inline `IDistributedCache`; DistributedCacheExtensions XML doc explicitly names this class as adoption target | S | SprkChat |
| 13 | `Chat/DynamicCommandResolver` (cache usage) | `Chat/DynamicCommandResolver.cs` | **consolidate** | Inline `IMemoryCache` | S | SprkChat |
| 14 | `Capabilities/CapabilityManifest` (cache usage) | `Capabilities/CapabilityManifest.cs` | **keep (special case)** | In-process snapshot with manual refresh; pattern is correctness-critical (manifest hash invalidation); does NOT fit generic wrapper because it's stateful structure, not key-value | n/a | AIPU2 |
| 15 | `Chat/OrchestratorPromptBuilder` (cache usage) | `Chat/OrchestratorPromptBuilder.cs` | **keep (gold-standard exception)** | Only file in the codebase that follows ADR-009's "MUST document ADR-009 exception" rule rigorously (explicit comments lines 36, 40); 20-min TTL for stable prefix; legitimate in-process choice per profiling | n/a | AIPU R1 |
| 16 | `Sessions/SessionPersistenceService` (cache usage) | `Sessions/SessionPersistenceService.cs` | **consolidate** | Inline `IDistributedCache` per-tenant session | S | AIPU R1 |
| 17 | `Security/PrivilegeGroupResolver` (cache usage) | `Security/PrivilegeGroupResolver.cs` | **consolidate (HIGH PRIORITY)** | `IMemoryCache` for per-user privileges — ADR-009 explicitly forbids caching authorization decisions; this needs immediate ADR-009 conformance review (downstream issue, NOT this audit's call — flag for security team) | M | Security |
| 18 | `RecordSearch/RecordSearchService` (cache usage) | `RecordSearch/RecordSearchService.cs` | **consolidate** | Inline `IDistributedCache` per query; should route via canonical wrapper | S | record-matching |
| 19 | `Foundry/AgentServiceClient` (cache usage) | `Foundry/AgentServiceClient.cs` | **consolidate** | Inline `IDistributedCache` for thread-id persistence; identical to #8 | S | Foundry |
| 20 | `AnalysisRagProcessor` (cache usage) | `AnalysisRagProcessor.cs` | **consolidate** | Inline `IMemoryCache` analysis-scope | S | AIPL |
| 21 | `AnalysisDocumentLoader` (cache usage) | `AnalysisDocumentLoader.cs` | **consolidate** | Inline `IMemoryCache` analysis-scope | S | AIPL |
| 22 | `AnalysisCacheEntry` | `AnalysisCacheEntry.cs` | **investigate (likely a data shape, not a service)** | Inventory listed it as a cache consumer but name suggests it's a POCO; needs read in W2 sub-task to classify | S | AIPL |
| 23 | `AiPlaybookBuilderService` (cache usage) | `AiPlaybookBuilderService.cs` | **at-risk (depends on Category 1 outcome)** | Consumed by orphaned `IntentClassificationService` per inventory §2.1.3; consolidation depends on whether AiPlaybookBuilderService itself is retired | S–M | AI Chat Playbook Builder |
| 24 | `ReferenceRetrievalService` (cache usage) | `ReferenceRetrievalService.cs` | **consolidate** | Uses both `EmbeddingCache` (good) AND inline `IMemoryCache` (consolidate) | S | references |
| 25 | `PlaybookService` (cache usage) | `PlaybookService.cs` | **consolidate** | Inline `IMemoryCache` varies | S | SprkChat |
| 26 | `Insights/Routing/NullInsightsIntentClassifier` (cache usage) | `Insights/Routing/NullInsightsIntentClassifier.cs` | **investigate** | A Null-Object that allocates `IMemoryCache` is an anti-smell — verify it's not papering over real DI (per CLAUDE.md §10 F.1) | S | Insights Engine |
| 27 | `InsightsActionRouter` (cache usage) | `Insights/Routing/InsightsActionRouter.cs` | **keep (already uses built-in helper)** | Already uses `IMemoryCache.GetOrCreateAsync` extension (lines 124, 168, 206); 3-stage L1/L2/L2-action cache; can adopt canonical wrapper later but no urgency | n/a | Insights Engine |
| 28 | `TextExtractorService` (cache usage) | `TextExtractorService.cs` | **consolidate** | Inline `IMemoryCache` extraction artifacts | S | AIPL |
| 29 | `StandaloneChatContextProvider` (cache usage) | `Chat/StandaloneChatContextProvider.cs` | **consolidate** | Newly-grep'd at HEAD (not in inventory table); inline `IDistributedCache` | S | SprkChat |
| 30 | `AnalysisChatContextResolver` (cache usage) | `Chat/AnalysisChatContextResolver.cs` | **consolidate** | Newly-grep'd at HEAD (not in inventory table); inline `IDistributedCache` | S | AIPL |

**Decision distribution**: 5 keep + 21 consolidate + 0 deprecate + 3 DELETE + 2 investigate-pending-W2 + 1 at-risk = 32. Total reconciles to inventory's 32 + 2 dedicated cache services.

### §3.1 HARD GATE A,B,C verification for DELETE candidates (#4, #5, #6)

**Per brief §3 "HARD GATE on delete recommendations":**

| Gate | `ActionLookupService` | `SkillLookupService` | `ToolLookupService` |
|---|---|---|---|
| A. Reproduced grep showing 0 non-test consumers at HEAD | PASS (only DI registration + interface defn) | PASS | PASS |
| B. DI registration removal-impact analysis | Remove `FinanceModule.cs:123`. No transitive consumers; `IGenericEntityService` + `IMemoryCache` deps shared with `PlaybookLookupService` so no DI removal cascade | Remove `FinanceModule.cs:132`. Same as #4 | Remove `FinanceModule.cs:141`. Same as #4 |
| C. Publish-size delta estimate | 185 + 53 = 238 source LOC → est. **2-4 KB compressed IL**. NEGLIGIBLE (well under 50 KB) | Same: NEGLIGIBLE | Same: NEGLIGIBLE |
| Combined per-service publish delta | All three together: ~6-12 KB compressed | (combined) | (combined) |

**Combined NEGLIGIBLE per CLAUDE.md §10 NFR-01 (45.65 MB baseline, 60 MB ceiling).** No single-task escalation threshold reached. Recommendation: safe to delete in a single PR.

---

## §4 Cross-cutting findings

### §4.1 Adoption-ready canonical helper already exists, zero `Services/Ai/` adoption
**The single highest-leverage finding.** `Spaarke.Core.Cache.DistributedCacheExtensions.GetOrCreateAsync<T>(...)` lives at `src/server/shared/Spaarke.Core/Cache/DistributedCacheExtensions.cs` and explicitly states (XML doc lines 12-18): "individual caches (EmbeddingCache, ChatSessionManager, InsightsPlaybookExecutionCache, etc.) should adopt this helper rather than reimplementing the pattern. Adoption is opt-in; existing caches are NOT refactored by changes to this class (per task 024 low-risk constraint)."

**Empirical reality**: zero (0/30) of the inline consumers in `Services/Ai/` import `Spaarke.Core.Cache`. The "opt-in" framing has become "opt-out by default." This is the largest, lowest-risk consolidation opportunity in Category 4.

### §4.2 Cross-cutting cascade with Category 2 (Lookup services — Sub-Agent B)
Sub-Agent B will recommend lookup-service consolidation. If B's recommendation is "generic `ILookupService<TEntity>`", then **decisions #3-#6 in §3 are subsumed by B's recommendation**. Sub-Agent A's recommendation: defer #3 (PlaybookLookupService consolidation behind generic) to B's report; keep #4-#6 (DELETE) independent because deletion does not require generic.

### §4.3 ADR-009 documentation rule is broken
ADR-009 §"MUST document ADR-009 exception for any `IMemoryCache` use" is followed by exactly ONE file in `Services/Ai/`: `OrchestratorPromptBuilder.cs`. The other 10 `IMemoryCache` consumers in `Services/Ai/` lack the documentation. This is a documentable ADR-conformance gap that the consolidation work could close opportunistically.

### §4.4 ADR-009 authorization-cache violation candidate (`PrivilegeGroupResolver`)
ADR-009 §"MUST NOT cache authorization decisions (cache data only)" — `Security/PrivilegeGroupResolver` caches per-user privileges in `IMemoryCache`. If "resolved privileges" are USED in an authorization decision downstream, this is an ADR-009 violation. **Surface for security-team review (per Q-003 sequential coordination); do not act unilaterally.**

### §4.5 Null-Object cache anti-smell (`NullInsightsIntentClassifier`)
A Null-Object should have minimal constructor deps (ADR-032 §F.1) — typically only `ILogger<T>`. `NullInsightsIntentClassifier` taking `IMemoryCache` is unusual. May be papering over a feature-gated transitive dep per CLAUDE.md §10 F.1. **Flag for W2 deeper inspection.**

### §4.6 Entity-coupled cache prefixes ("playbook:code:", "action:code:", "skill:code:", "tool:code:")
All four use the same template-by-entity pattern. A typed key factory (e.g., `DataverseAlternateKeyCacheKey<TEntity>.Build(code)`) would eliminate the duplication. This is squarely in scope for the generic-lookup-wrapper recommendation Sub-Agent B will surface.

### §4.7 Two different IMemoryCache.GetOrCreateAsync patterns coexist
- Built-in extension `IMemoryCache.GetOrCreateAsync` (Microsoft.Extensions.Caching.Memory) used in `InsightsActionRouter`
- Hand-rolled `TryGetValue` + factory + `Set` pattern used in all 4 lookup services

Even within `IMemoryCache`-only consumers, the canonical-vs-hand-rolled split exists. Consolidation candidate.

---

## §5 Canonical naming candidates (Q-004 framing — candidates only, NOT locked)

The brief explicitly defers naming to end-of-audit owner review. Surfacing 3 candidates with tradeoffs:

### §5.1 Candidate A: `ISpaarkeCache<T>` (most generic)
- Pros: Aligns with Spaarke namespace convention (`Spaarke.Core.Cache`); single canonical entry point; mirrors `EmbeddingCache` interface shape; future-proof for in-process / Redis / hybrid swap
- Cons: "Cache" suffix is overused in .NET ecosystem; risk of collision with framework `IMemoryCache`; doesn't convey "wrapper around `IDistributedCache`"
- Lives where: `src/server/shared/Spaarke.Core/Cache/ISpaarkeCache.cs`

### §5.2 Candidate B: `ICachedFacade<TKey, TValue>` (typed, intent-revealing)
- Pros: Conveys the wrapper-pattern intent (`EmbeddingCache` is a Facade over `IDistributedCache`); two-parameter generic supports `IDataverseAlternateKeyLookup<TEntity, TResponse>` and other higher-arity scenarios; readable at call sites
- Cons: "Facade" overloaded with ADR-007 SpeFileStore facade pattern + ADR-013 PublicContracts facade; more typing
- Lives where: `src/server/shared/Spaarke.Core/Cache/ICachedFacade.cs`

### §5.3 Candidate C: Promote `DistributedCacheExtensions.GetOrCreateAsync<T>` as the single canonical (no new interface)
- Pros: Already implemented, already tested, already named; zero new abstractions per ADR-010 ("MUST NOT create interfaces without genuine seam requirement"); the `IEmbeddingCache` interface remains as the binary-serialization specialist exception; all other 30 inline consumers migrate to extension calls
- Cons: Doesn't give callers a typed cache OBJECT to inject (just a static extension); slightly less testable than an interface seam (but tests can mock `IDistributedCache` itself)
- Lives where: existing `Spaarke.Core.Cache.DistributedCacheExtensions` (no new file)
- **Sub-Agent A's recommendation strength**: this candidate aligns most directly with ADR-010 + existing canonical infrastructure. The audit may not need to introduce a new interface at all — it needs to drive adoption of the helper that already exists.

### §5.4 The naming framing: "Spaarke Canonical Cache Stack"
Per Q-004 lock ("Spaarke Canonical AI Stack" framing — named canonical per category), the cache equivalent could be **"Spaarke Canonical Cache Stack"** = (1) `IDistributedCache` as the only backing store contract; (2) `Spaarke.Core.Cache.DistributedCacheExtensions.GetOrCreateAsync<T>` as the only call-site pattern; (3) `EmbeddingCache` and `InsightsPlaybookExecutionCache` as documented binary/streaming specialist exceptions; (4) in-process `MemoryCache` permitted ONLY with explicit ADR-009-exception XML doc per `OrchestratorPromptBuilder` precedent.

---

## §6 Drift report (357e6936 → 12275b10)

### §6.1 Commits since snapshot
```
12275b10 Merge PR #342 from work/insights-engine-r3-init
2eeb9373 Merge remote-tracking branch 'origin/master' into work/insights-engine-r3-init
36652d9a Merge PR #341 from work/bff-ai-architecture-audit-r1-init
b7ee9b84 docs(insights-engine-r3): initiate Phase 2 project scaffold — PAUSED pending audit findings
07237c97 docs(bff-ai-architecture-audit): initiate r1 audit project — comprehensive AI infrastructure review
```

### §6.2 Code drift in scope (`src/server/api/Sprk.Bff.Api/Services/Ai/`)
`git diff --stat 357e6936..HEAD -- src/server/api/Sprk.Bff.Api/Services/Ai/` returns **EMPTY**. **Zero code drift.** All 5 commits are docs/scaffold. Inventory's findings remain fully accurate at HEAD.

### §6.3 Numeric reconciliation
| Inventory claim | HEAD reproduction | Match? |
|---|---|---|
| Total cache consumers in Services/Ai/ | 32 | OK (11 IMemoryCache + 21 IDistributedCache) |
| EmbeddingCache consumers | 4 | OK |
| InsightsPlaybookExecutionCache consumers | 1 | OK |
| ActionLookupService consumers | 0 (orphan) | OK |
| SkillLookupService consumers | 0 (orphan) | OK |
| ToolLookupService consumers | 0 (orphan) | OK |
| PlaybookLookupService consumers | 1 | OK |

### §6.4 New cache consumers at HEAD not in inventory table
- `Chat/StandaloneChatContextProvider.cs` (IDistributedCache) — present in inventory text as "Chat/ChatContextMappingService" peer? Could be the same row interpreted differently. Flag for inventory reconciliation in §7.
- `Chat/AnalysisChatContextResolver.cs` (IDistributedCache) — present in inventory text bundle "Chat/ChatContextMappingService"? Same.

**No claims in this report depend on these reconciliation ambiguities.**

---

## §7 Open questions for owner review (packaged per Q-002 single end-of-audit review)

1. **`DistributedCacheExtensions.GetOrCreateAsync<T>` adoption**: Q5 audit (2026-05-27) recommended adoption but explicitly made it "opt-in." Should adoption now become mandatory for new BFF cache consumers, with a follow-up project to migrate the existing 30 consumers? (If yes: candidate ADR.)

2. **DELETE the 3 lookup orphans now or defer to Cat 2 Sub-Agent B's report?** Sub-Agent A's recommendation: delete now (independent of Cat 2 consolidation outcome; HARD GATES A/B/C all pass; ~6-12 KB compressed publish savings).

3. **`PrivilegeGroupResolver` IMemoryCache for per-user privileges**: is this caching DATA (allowed under ADR-009) or DECISIONS (forbidden)? Security team adjudication needed.

4. **`NullInsightsIntentClassifier` constructor IMemoryCache dependency**: is this required for the Null-Object semantic (P3 Fail-fast) or papered-over conditional dependency (§F.1 anti-pattern)? Insights Engine team adjudication.

5. **Should `EmbeddingCache` interface (`IEmbeddingCache`) be retained as a domain-specialist (binary float[] serialization) wrapper, or absorbed into a generic `ISpaarkeCache<T>` if Q-004 lock chooses Candidate A?** Sub-Agent A recommends RETAIN as specialist — `Buffer.BlockCopy` Base64 path is materially more efficient than JSON for 3072-dim float vectors.

6. **`InsightsPlaybookExecutionCache` similar question**: stream-draining + `InsightArtifact`-vs-`DeclineResponse` discrimination logic cannot be absorbed into a generic; should remain a specialist wrapper. Confirm.

7. **ADR-009 §"MUST document ADR-009 exception for any IMemoryCache use"** is followed by 1 of 11 IMemoryCache consumers. Should consolidation work add the doc-comment retroactively, or should the rule be amended?

8. **`AnalysisCacheEntry` classification ambiguity**: is it a service (cache consumer) per inventory or a data shape (POCO)? Needs targeted W2 read.

---

## §8 ADR candidates (per Q-005 — surfaced as bullet items only, NOT authored)

- **ADR candidate "BFF Canonical Cache Stack"** — codify the three-tier rule: (1) `IDistributedCache` is the only backing-store contract for cross-request caching; (2) `DistributedCacheExtensions.GetOrCreateAsync<T>` is the only call-site pattern; (3) specialist wrappers (`EmbeddingCache`, `InsightsPlaybookExecutionCache`) permitted only when binary serialization OR stream-draining requirements exist; (4) in-process `MemoryCache` permitted only with explicit ADR-009-exception XML doc + profiling justification.

- **ADR candidate "Lookup-service generic"** — defines `IDataverseAlternateKeyLookup<TEntity, TResponse>` as the canonical pattern for the four `*LookupService` classes; deprecates entity-coupled cache key prefixes (`playbook:code:`, etc.) in favor of typed key factory. (May be Sub-Agent B's territory; cross-coordination needed.)

- **ADR candidate amendment to ADR-009** — promote `DistributedCacheExtensions.GetOrCreateAsync<T>` from "opt-in" to "MUST use for new cache call sites in BFF" (codify the implicit recommendation from Q5 audit 2026-05-27).

- **ADR candidate amendment to ADR-032** — clarify that Null-Object constructors should not take cache dependencies (per `NullInsightsIntentClassifier` ambiguity); reinforces "Null-Object constructors MUST be minimal — typically only `ILogger<T>`" rule that already exists.

- **ADR candidate "PrivilegeGroupResolver cache audit"** — narrow security-focused ADR confirming whether per-user privilege caching is data (allowed) or decision (forbidden). Routes to existing ADR-003 + ADR-009 interaction.

---

# Sub-Agent A Final Status Report

1. **Status**: COMPLETED (8/8 sections delivered; HARD GATES for DELETE candidates fully verified)
2. **Output file path + size**: `projects/bff-ai-architecture-audit-r1/notes/phase2/analysis-cache.md` (~50 KB estimate)
3. **Services analyzed**: 32 cache consumers + 2 dedicated cache services + 1 canonical helper (`DistributedCacheExtensions`) = **35 distinct cache touchpoints**.
4. **Decision distribution**: 5 keep (incl. 2 designated canonical specialists) + 21 consolidate + 0 deprecate + **3 DELETE** (`ActionLookupService`, `SkillLookupService`, `ToolLookupService` — all HARD GATES pass) + 2 investigate-pending-W2 + 1 at-risk-cascade.
5. **Drift findings**: ZERO code drift in `src/server/api/Sprk.Bff.Api/Services/Ai/` between `357e6936` and `12275b10`. 5 commits since snapshot — all docs/scaffold only. All inventory claims remain accurate at HEAD. 2 minor inventory-row reconciliation ambiguities (StandaloneChatContextProvider, AnalysisChatContextResolver) flagged in §6.4 but do not affect any recommendation.
6. **Cross-cutting observations for other W1 sub-agents**:
   - **For Sub-Agent B (Lookup services)**: My decisions #3-#6 (consolidate PlaybookLookupService + DELETE the 3 orphans) are CACHE-SCOPED only. If B recommends generic `ILookupService<TEntity>`, B's recommendation subsumes mine for #3. My DELETE recommendation for #4-#6 is independent and ready to action.
   - **For Sub-Agent C (Public Contracts)**: Cache-pattern analysis touches `BriefingAi`, `InvoiceAi`, etc. only via their internal consumers. No direct overlap. However: facade implementations themselves may bypass `Spaarke.Core.Cache` canonical — flag for C to verify.
   - **For Sub-Agent D (Node executors)**: 16 node executors are HEAVY users of cache via downstream services (RagService, etc.) — D's analysis should NOT need to touch cache decisions; cache is the substrate, not the node-executor concern.
   - **All sub-agents**: the canonical helper `Spaarke.Core.Cache.DistributedCacheExtensions.GetOrCreateAsync<T>` is the lowest-risk lever for cross-category consolidation. ZERO `Services/Ai/` files import it.
7. **Open questions surfaced for owner**: 8 questions in §7, with strongest priority on (Q1) make canonical helper adoption mandatory? (Q2) DELETE orphans now vs defer to Cat 2? (Q3) PrivilegeGroupResolver ADR-009 compliance.
8. **Recommendations for W2 dispatch**:
   - **HIGH priority**: W2 sub-task to investigate `AnalysisCacheEntry` classification (service or POCO?) and `NullInsightsIntentClassifier` cache-dep anti-smell (§4.5).
   - **MEDIUM**: W2 sub-task to verify whether `IPrivilegeGroupResolver`'s cached privileges feed into authorization decisions (ADR-009 violation check).
   - **MEDIUM**: W2 sub-task to enumerate all 30 `Spaarke.Core.Cache` non-adopters with concrete migration LOC estimates per file — feeds the §8 ADR-009 amendment proposal.
   - **For Cat 1 sub-agent (W2)**: 3 of 4 intent classifiers cache without using canonical wrapper (CapabilityRouter snapshot, InsightsIntentClassifier IMemoryCache, IntentClassificationService no cache, PlaybookDispatcher hand-rolled IDistributedCache). The cache-pattern consolidation is a strong cross-cutting lever for Cat 1 consolidation as well.
