# Phase 2 Wave 1 Summary — Aggregation of 4 Parallel Sub-Agent Analyses

> **Authored by**: Main session (aggregation step after all 4 W1 sub-agents completed)
> **Pinned to**: commit `357e6936` (Phase 1 inventory snapshot)
> **HEAD at aggregation time**: `12275b10` (5 commits since snapshot; all 4 sub-agents confirmed ZERO code drift in `src/server/api/Sprk.Bff.Api/Services/Ai/`)
> **Date**: 2026-06-04
> **Source documents**:
> - [`analysis-cache.md`](analysis-cache.md) — Cat 4 (Sub-Agent A; 30 KB)
> - [`analysis-lookup.md`](analysis-lookup.md) — Cat 2 (Sub-Agent B; 17 KB)
> - [`analysis-node-executors.md`](analysis-node-executors.md) — Cat 7 (Sub-Agent D; 16 KB)
> - [`analysis-public-contracts.md`](analysis-public-contracts.md) — Cat 6 (Sub-Agent C; 27 KB)

---

## §1 Per-category distillation

### §1.1 Cat 4 — Cache patterns (Sub-Agent A)

**Headline**: A canonical helper already exists — `Spaarke.Core.Cache.DistributedCacheExtensions.GetOrCreateAsync<T>` at `src/server/shared/Spaarke.Core/Cache/DistributedCacheExtensions.cs` — and is explicitly designed for the adoption that the 30 inline `Services/Ai/` cache consumers don't perform. The XML doc on the helper even names `EmbeddingCache`, `ChatSessionManager`, `InsightsPlaybookExecutionCache` as adoption targets. **Adoption is 0%.** The Q5-audit (2026-05-27) made it "opt-in"; that framing has become "opt-out by default."

**Decision shape**: 5 KEEP (incl. 2 designated canonical specialists: `EmbeddingCache` for binary float[] payloads; `InsightsPlaybookExecutionCache` for stream-draining) + 21 CONSOLIDATE (one-line swap to the canonical helper) + 3 DELETE (the 3 lookup-service orphans, scoped here as cache consumers) + 2 INVESTIGATE-PENDING-W2 (`AnalysisCacheEntry` classification; `NullInsightsIntentClassifier` cache-dep anti-smell) + 1 AT-RISK-CASCADE (`AiPlaybookBuilderService` retirement-dependent).

**Cross-cutting finding worth elevating**: ADR-009 §"MUST document ADR-009 exception for any `IMemoryCache` use" is followed by exactly ONE file in `Services/Ai/` (`OrchestratorPromptBuilder.cs`). The other 10 `IMemoryCache` consumers in `Services/Ai/` lack the documentation. `Security/PrivilegeGroupResolver` caches per-user privileges in `IMemoryCache` — potential ADR-009 violation if "resolved privileges" feed authorization decisions; flagged for security-team review per Q-003 sequential.

### §1.2 Cat 2 — Lookup services (Sub-Agent B)

**Headline**: 3 of 4 lookup services are confirmed orphans at HEAD; all HARD GATES (A grep, B DI removal, C publish-size) pass cleanly. Delete recommendation is empirically rigorous. The 4-way DRY violation among `PlaybookLookupService`, `ActionLookupService`, `SkillLookupService`, `ToolLookupService` is line-for-line near-identical (~95% docstring template ratio).

**Decision shape**: 1 KEEP (`PlaybookLookupService` — 1 real production consumer at `InvoiceExtractionJobHandler.cs`; the 2nd "consumer" in inventory was a doc-cref only) + 3 DELETE (`ActionLookupService`, `SkillLookupService`, `ToolLookupService`) + 0 CONSOLIDATE (generic `ILookupService<T>` explicitly REJECTED as YAGNI per ADR-010 — only 1 concrete remains post-delete).

**Surprises**: (a) `PlaybookLookupService`'s consumer count corrects from 2 → 1 (the second was a doc-cref); (b) `InsightsActionRouter.cs:402-403` contains a dangling doc-cref `<c>ActionLookupService.GetCacheKey</c>` that needs cleanup in the same PR as the deletion; (c) typed-exception asymmetry — `PlaybookLookupService` throws typed `PlaybookNotFoundException` (correct); the 3 orphans throw generic `InvalidOperationException` (anti-pattern, will be moot post-delete but worth codifying as a future lookup-service rule).

**Total deletion impact**: 714 LOC eliminated (~10-20 KB compressed publish reduction). Real win is cognitive load + 3 fewer `AddScoped` lines in `FinanceModule.cs`.

### §1.3 Cat 6 — Public Contracts facade (Sub-Agent C)

**Headline**: §F.1 anti-pattern coverage gap is REAL across **4 of 5 facades** and MATERIAL — under compound-AI-OFF, `IInsightsAi` will produce **500 errors with opaque "Unable to resolve service"** instead of **503 ProblemDetails with stable `errorCode=ai.insights.disabled`**. This is a **LATENT bug** invisible to current integration tests because the test fixture mocks `IInsightsAi` directly. Additionally, `AnalysisServicesModule.cs:75-79` contains a **factually incorrect comment** claiming an `IInsightsAi` Null fallback exists — it does not.

**Decision shape**: 2 KEEP-AS-IS (`IBriefingAi` Null peer structurally kept; `IObservationMirror` dual-impl is intentional) + 1 KEEP+CLEANUP CONSUMERS (`IBriefingAi` — remove defensive `?=null` from 4 consumer sites) + 4 ADD-NULL-PEER (`NullInvoiceAi`, `NullWorkspacePrefillAi`, `NullRecordMatchingAi`, `NullInsightsAi`) + 0 consolidate/delete (deferred to W2 per scope boundary).

**Two distinct §F.1 manifestations identified**:
- **Manifestation A (severe, LATENT)**: `IInsightsAi` registered unconditionally; ctor transitively depends on conditional `IOpenAiClient` + `IAiPlaybookBuilderService` → request-scope throws under compound-OFF. Metadata-gen passes; tests pass; production breaks. **The most important single finding of W1.**
- **Manifestation B (visible, STRUCTURAL)**: 7 consumer sites (4 services + 3 endpoint sites for IBriefingAi/IInvoiceAi/IWorkspacePrefillAi) use defensive `IFoo? = null` + `RequireAi()` throwing `InvalidOperationException` — the ADR-032 §Anti-patterns explicit forbidden pattern, producing 500 instead of 503.

**Facade boundary integrity itself is INTACT** — external CRUD code doesn't import AI internals. The gap is failure-mode discipline, not boundary placement. The 5 facade XML docs are unusually high-quality and explicitly cite ADR-013 + ADR-007.

### §1.4 Cat 7 — Node executors (Sub-Agent D)

**Headline**: Executor count corrects from inventory's "16" → **18 confirmed at HEAD**. The `ActionType` enum at `INodeExecutor.cs:78-207` IS the central registry (single source of truth; compile-time defense; runtime duplicate-detection at `NodeExecutorRegistry.cs:89`). NO collisions. NO §F.1 anti-pattern violations on the executor surface. Block organization is implicit but consistent (0-2 AI primitives, 10-12 reserved computation, 20-29 external integration, 30-39 control flow, 40-49 output, 50-59 notification/query, 60-69 Foundry, 70-149 Insights Engine).

**Decision shape**: 15 KEEP + 3 KEEP-WITH-CONCERN (`AiAnalysisNodeExecutor` heaviest deps; `AgentServiceNodeExecutor` runtime kill-switch undocumented; `IndexRetrieveNode` Cat-3-dependent) + 0 CONSOLIDATE / DEPRECATE / DELETE.

**Two systemic gaps surfaced (process-fragile, low cost to remediate)**:
1. **NO `ACTION-TYPE-REGISTRY.md` allocation-tracking doc exists**. Block reservations + next-available + owner-per-block are implicit. Parallel-project collision risk via concurrent branches is real (e.g., two teams claim ActionType=150 on separate worktrees).
2. **"Runtime Kill-Switch Pattern" is undocumented** as peer to ADR-030 Null-Object. `AgentServiceNodeExecutor.cs:198-212` catches `FeatureDisabledException` from injected `AgentServiceClient` and returns structured `NodeOutput.Error(... NODE_AGENT_FEATURE_DISABLED ...)`. This is distinct from DI kill-switch (Null peer) and merits a codified pattern doc.

**Inventory §2.7 has 3 labeling errors** to propagate to consolidated audit findings: (1) "16 registered concrete executors" → actually 18; (2) "(default)" ActionType labels on first 9 → they have explicit numerics (`AiAnalysis = 0`, `CreateTask = 20`, etc.); (3) "Singleton (kill-switched)" on `AgentServiceNodeExecutor` → DI is unconditional, kill switch is runtime-only.

---

## §2 Cross-cutting findings spanning W1

### §2.1 The "canonical-exists-but-unused" pattern is systemic

**Three distinct instances of canonical infrastructure existing but having near-zero adoption**:
- **Cat 4**: `DistributedCacheExtensions.GetOrCreateAsync<T>` — 0% adoption in `Services/Ai/` (30 inline consumers don't import).
- **Cat 6**: `NullBriefingAi` P3 Fail-Fast pattern — 1 of 5 facades adopts the canonical Null-Object pattern despite ADR-032 mandating it.
- **Cat 7**: `INodeExecutor` registry pattern — DOES have high adoption (18/18 executors use it correctly), but the supporting `ACTION-TYPE-REGISTRY.md` allocation contract is missing.

**The pattern**: canonical patterns/helpers exist (good); the audit/governance layer that DRIVES their adoption is missing or partial (bad). The audit's Phase 3 (Canonical Stack naming + migration plan) should treat "drive adoption of existing canonicals" as a first-class deliverable on equal footing with "introduce new canonicals."

### §2.2 §F.1 asymmetric-registration anti-pattern is wider than the inventory suggested

Inventory §3.4 commented that "Every conditional registration has an 'ADR-032 §F.1 inspection' comment block. This is excellent rigor." W1's empirical work shows the rigor is in the *comment density*, not the *pattern adherence*: §F.1 violations are present across Cat 6 (4 of 5 facades) and Cat 4 (`NullInsightsIntentClassifier` cache-dep ambiguity flagged for W2). The comment blocks document the inspection happened but did not catch the latent transitive-conditional pattern that `IInsightsAi` exhibits.

**Recommendation**: W3's DI + Configuration analysis should treat §F.1 as a *runtime-verifiable* property, not a *comment-block-verifiable* property. An integration test that flips compound AI to OFF + calls every unconditionally-mapped endpoint + asserts 503 (not 500) would catch all of Manifestation A.

### §2.3 Three documentation/labeling drift incidents discovered

- **Cat 6**: `AnalysisServicesModule.cs:75-79` comment claims `IInsightsAi` falls back to Null — factually incorrect.
- **Cat 7**: Inventory §2.7 has 3 labeling errors (count 16→18; "(default)" → explicit numerics; "Singleton (kill-switched)" → runtime kill-switch).
- **Cat 4**: Inventory's "32 inline cache consumers" is correct, but 2 of them aren't in the §2.4.2 table (`StandaloneChatContextProvider`, `AnalysisChatContextResolver` — flagged in Sub-Agent A §6.4 as inventory reconciliation ambiguity).

Drift is small and ALL ambiguities are resolvable by reading the code. But the pattern (audit finding drift between docs and code) is itself a Phase 3 input.

### §2.4 ZERO code drift across all 4 sub-agents

All 4 sub-agents independently confirmed `git diff --stat 357e6936..HEAD -- src/server/api/Sprk.Bff.Api/Services/Ai/` returns EMPTY. The 5 commits between snapshot and HEAD are docs/scaffold only (PR #341 audit init + PR #342 r3 init). **Inventory `357e6936` is fully valid at HEAD `12275b10` for all W1 categories.**

This validates the owner's "drift accepted" direction: parallel AI work continues but did not touch W1's surfaces during the audit window. Q-006 Quarterly Review Skill remains the long-term drift-handling mechanism.

### §2.5 Cross-category handoffs (sub-agents explicitly signaled to each other)

- **Cat 4 → Cat 2**: Sub-Agent A's decisions #4-6 (DELETE 3 orphan lookup services, cache-scoped) are subsumed by Sub-Agent B's category-scoped DELETE recommendations. No conflict — same 3 services flagged from two angles. Single PR cleanup is straightforward.
- **Cat 2 → Cat 4**: `PlaybookLookupService` should adopt Sub-Agent A's canonical wrapper IF and when Cat 4 lands. Conditional consolidation, not now.
- **Cat 6 → Cat 4**: Facade implementations may bypass `Spaarke.Core.Cache` canonical (`IRagService.GetEmbeddingAsync` does use `IEmbeddingCache`; other facade methods need spot-check). Flagged for W3 deeper inspection.
- **Cat 7 → Cat 4**: Node executors are heavy cache users via downstream services (RagService, etc.). D's analysis correctly stayed in scope — cache is the substrate, not the executor concern.

---

## §3 Decision distribution roll-up (W1 totals)

| Category | KEEP | KEEP-w-CONCERN | CONSOLIDATE | ADD-NULL-PEER | DELETE | INVESTIGATE | AT-RISK |
|---|---|---|---|---|---|---|---|
| Cat 4 (Cache) | 5 | — | 21 | — | 3 (cache-scoped, same as Cat 2 services) | 2 | 1 |
| Cat 2 (Lookup) | 1 | — | 0 | — | 3 (`Action/Skill/Tool LookupService`) | 0 | 0 |
| Cat 6 (Public Contracts) | 2 | — | 0 (deferred to W2) | 4 (`NullInvoiceAi`, `NullWorkspacePrefillAi`, `NullRecordMatchingAi`, `NullInsightsAi`) | 0 | 0 | 0 |
| Cat 6 (Public Contracts) — consumer cleanup | — | 1 (`IBriefingAi` consumer-side `?=null` removal) | — | — | — | — | — |
| Cat 7 (Node executors) | 15 | 3 | 0 | 0 | 0 | 0 | 0 |
| **W1 unique totals** | **23** | **4** | **21** (cache adoption) | **4** | **3** (the same 3 orphans counted once) | **2** | **1** |

**Unique action items synthesized from W1**:
1. **DELETE 3 orphan lookup services** (`ActionLookupService`, `SkillLookupService`, `ToolLookupService`) + `FinanceModule.cs` cleanup + `InsightsActionRouter.cs:402-403` dangling-cref fix → single PR, ~6-12 KB compressed publish reduction.
2. **ADD 4 Null-peer facades** (`NullInvoiceAi`, `NullWorkspacePrefillAi`, `NullRecordMatchingAi`, `NullInsightsAi`) + DI rewire for symmetric registration + ADR-032-compliant consumer cleanup → 4 PRs or 1 bundled PR; closes §F.1 gap for Cat 6.
3. **CONSOLIDATE 21 cache consumers** behind `DistributedCacheExtensions.GetOrCreateAsync<T>` canonical helper → multi-team migration; needs cross-team coordination per Q-003; estimated S per consumer; ~3-5 weeks across SprkChat + Insights + Workspace + Finance + Foundry teams.
4. **CLEANUP `IBriefingAi` consumer defensive nullables** (4 sites) → 1 PR; M cost.
5. **DOCUMENT bug fix `AnalysisServicesModule.cs:75-79`** → 1-line PR.
6. **AUTHOR `ACTION-TYPE-REGISTRY.md` allocation contract** → 1 PR; small surface; preempts collision risk.

---

## §4 HIGH-URGENCY findings to surface immediately

### §4.1 LATENT BUG — `IInsightsAi` 500 instead of 503 under compound-AI-OFF

**Severity**: HIGH; **Visibility**: LATENT (tests mock `IInsightsAi` directly, masking the failure mode).

**Mechanism**: `IInsightsAi → InsightsOrchestrator` is registered unconditionally in `InsightsFacadeModule:105`. `InsightsOrchestrator` ctor depends on `IOpenAiClient` (conditional, `AnalysisServicesModule.cs:27`) + `IAiPlaybookBuilderService` (conditional, line 367). When compound AI is OFF:
- Metadata-gen at startup passes (param-inference only checks signature-resolvability)
- First request to `/api/insights/ask`, `/api/insights/search`, or `/api/insights/assistant/query` triggers ctor → `InvalidOperationException` ("Unable to resolve service for type `IOpenAiClient`...") → ASP.NET 500 response

**Expected behavior**: 503 ProblemDetails with stable `errorCode=ai.insights.disabled` via `FeatureDisabledException → AsFeatureDisabled503()`.

**Recommended verification**: integration test flipping `DocumentIntelligence:Enabled=false`, calling each of the 3 endpoints, asserting 503 not 500. This belongs to the W2 Cat 1 sub-agent (touches `InsightsIntentClassifier` and its consumers).

**Recommended remediation**: ADD `NullInsightsAi` P3 Fail-Fast peer + EITHER (a) keep `AddInsightsFacadeModule` unconditional but have it conditionally register Null vs real based on compound gate, OR (b) move `IInsightsAi` real-impl registration into `AnalysisServicesModule.AddPublicContractsFacade` and add `NullInsightsAi` to `AddNullObjectsForCompoundOff`. Option (b) is symmetric with the other 4 facades.

### §4.2 ADR-009 violation candidate — `PrivilegeGroupResolver` per-user privilege caching

`Security/PrivilegeGroupResolver` caches per-user privileges in `IMemoryCache`. ADR-009 forbids caching authorization DECISIONS. If "resolved privileges" feed downstream authorization, this is a violation. **Surface for security-team review per Q-003; do not act unilaterally.**

### §4.3 Documentation bug — `AnalysisServicesModule.cs:75-79`

Comment claims `IInsightsAi` Null fallback exists; it does not. Misleads future §F.1 inspections. 1-line PR fix.

---

## §5 Drift summary (snapshot 357e6936 → HEAD 12275b10)

| Category | Code drift (`Services/Ai/`) | Inventory accuracy | Consumer-side drift |
|---|---|---|---|
| Cat 4 (Cache) | ZERO | 2 minor reconciliation ambiguities (StandaloneChatContextProvider, AnalysisChatContextResolver) | None |
| Cat 2 (Lookup) | ZERO | Reclassification: PlaybookLookupService consumer count 2→1 (doc-cref vs ctor injection) | None |
| Cat 6 (Public Contracts) | ZERO | Accurate (§2.6 table verified) | None |
| Cat 7 (Node executors) | ZERO | 3 labeling errors (count 16→18; "(default)" → explicit numerics; "Singleton (kill-switched)" → runtime kill-switch) | None |

**Aggregate verdict**: Phase 1 inventory remains the authoritative snapshot. The reconciliation ambiguities and labeling errors are minor and do NOT alter any W1 recommendation.

---

## §6 W2 dispatch recommendations

### §6.1 W2 scope (Cat 1 + Cat 3 + Cat 7 deferred questions)

Per the audit's locked priority order (recovery memory + plan), W2 covers:
- **Cat 1 — Intent classification** (4 parallel classifiers; HIGHEST architectural significance)
- **Cat 3 — Search services** (4 substrates; depends on Cat 1 framing for "should facade X be merged with Y" deferred from Cat 6)
- **Cat 7 deferred questions** (`AiAnalysisNodeExecutor` classifier/search migrations; `IndexRetrieveNode` search-substrate choice)

### §6.2 Recommended W2 sequencing (sequential within wave)

| Order | Category | Rationale |
|---|---|---|
| 1 | Cat 1 (Intent classification) | Most architecturally significant; cross-cutting Q for owner ("can 4 classifiers consolidate behind `IIntentClassifier<TResult>` strategy pattern?"); produces canonical that Cat 3 + Cat 7 depend on |
| 2 | Cat 3 (Search services) | After Cat 1 lands. 4 substrates each justified by different index; consolidation harder. Audit `RagService` Null-Object pattern asymmetry (the other 3 lack Null peer). Includes verification of Cat 6 §4.1 latent bug (compound-OFF integration test). |
| 3 | Cat 7 deferred | Lightweight re-dispatch of Sub-Agent D to address: (a) `AiAnalysisNodeExecutor` classifier-choice migration post-Cat 1; (b) `IndexRetrieveNode` search-substrate choice post-Cat 3 |

### §6.3 W2 sub-agent briefs MUST include (lessons learned from W1)

- **Self-contained brief** with verbatim inventory quote (every W1 sub-agent benefited from this)
- **Explicit OUT-OF-SCOPE list** (every W1 sub-agent honored these; prevented drift)
- **HARD GATE on delete recommendations** (Sub-Agents A + B applied this rigorously)
- **Empirical-reproduction-FIRST rule** (Sub-Agents B + D corrected inventory counts via reproduction)
- **Output file path stated as absolute path** (sub-agents tried but harness blocked writes — main session pre-creates directory + handles persistence)
- **NEW: harness write-block workaround** — sub-agents return findings as text; main session writes to file. ALL 4 W1 sub-agents hit this. Pre-state this in the brief to reduce sub-agent confusion.

### §6.4 W2 owner-input gates (Q-002 single review still applies for FINAL decisions; but mid-wave consultations are appropriate for Cat 1)

- Cat 1 Q: Can the 4 classifiers consolidate behind `IIntentClassifier<TResult>` strategy? Or are they semantically distinct (capability routing vs playbook selection vs builder-time vs Insights routing)?
- Cat 1 Q: Is `IntentClassificationService` actively used or deprecated? (Inventory says orphaned; Sub-Agent B's HARD GATE applied — but this affects the larger consolidation question.)
- Cat 1 Q: Should `PlaybookDispatcher` (factory-instantiated) join DI to enable the same testability seam as the other 3 classifiers?

---

## §7 Packaged for end-of-audit owner review (per Q-002)

The following questions are surfaced for the SINGLE end-of-audit review per Q-002 lock. They are NOT to be answered mid-wave.

### §7.1 Decisions requiring owner sign-off

1. **DELETE the 3 lookup orphans** — Sub-Agents A + B independently recommend; HARD GATES all pass. Confirm with Finance Intelligence owner before merge per Q-003.
2. **ADD 4 Null-peer facades** — close §F.1 gap. Confirm pattern across the 4 facades + commit to migration timeline.
3. **DRIVE adoption of `DistributedCacheExtensions.GetOrCreateAsync<T>`** — promote from "opt-in" to "MUST use for new cache call sites." Owner adjudicates whether existing 30 sites migrate (multi-team, multi-week) or only new sites adopt.
4. **`PrivilegeGroupResolver` ADR-009 conformance** — security-team adjudication of whether per-user privilege caching is data (allowed) or decision (forbidden).
5. **`NullInsightsIntentClassifier` cache-dep ambiguity** — Insights team adjudicates whether `IMemoryCache` in Null-Object ctor is required for semantic OR §F.1 anti-pattern.
6. **AUTHOR `ACTION-TYPE-REGISTRY.md`** — formalize block reservations + next-available + owner-per-block.

### §7.2 ADR candidates (per Q-005 DEFERRED to follow-on phase; surfaced as bullets only)

Consolidated from all 4 W1 sub-agents:

| # | ADR candidate | Surfaced by | Priority |
|---|---|---|---|
| 1 | **BFF Canonical Cache Stack** — `IDistributedCache` only; `GetOrCreateAsync<T>` only; specialist wrappers only when binary/streaming; `MemoryCache` with explicit ADR-009-exception doc | Cat 4 | HIGH |
| 2 | **AI Public-Contracts Facade Null-Peer Mandate** — every facade in `PublicContracts/` MUST have Null peer + `FeatureDisabledException` + `ai.<feature>.disabled` errorCode | Cat 6 | HIGH |
| 3 | **Defensive-Nullable Facade Injection Prohibited** — top-level MUST NOT scoped to facade boundary; codifies ADR-032 §Anti-patterns | Cat 6 | HIGH |
| 4 | **Zone B Endpoint Mapping → Zone A Facade Registration Symmetry** — unconditional endpoints require unconditional symmetric Null peers (closes `IInsightsAi` latent bug pattern) | Cat 6 | HIGH |
| 5 | **ActionType Central Registry + Allocation Contract** — block reservations, allocation doc, owner-per-block, deprecation policy for unimplemented enum members | Cat 7 | MEDIUM |
| 6 | **Runtime Kill-Switch Pattern** (peer to ADR-030 Null-Object) — distinguishes DI kill-switch from runtime kill-switch per `AgentServiceNodeExecutor` precedent | Cat 7 | MEDIUM |
| 7 | **ADR-009 Amendment**: promote `GetOrCreateAsync<T>` from opt-in to MUST | Cat 4 | MEDIUM |
| 8 | **ADR-032 Amendment**: clarify Null-Object ctor minimality (no cache deps) | Cat 4 | LOW |
| 9 | **Lookup-Service-Per-Entity Rule** (anti-pattern doc, not full ADR) | Cat 2 | LOW |
| 10 | **Lookup-Service Typed Exceptions** | Cat 2 | LOW |
| 11 | **PrivilegeGroupResolver cache audit** | Cat 4 | depends on §4.2 |
| 12 | **Facade XML Documentation Pattern** | Cat 6 | LOW |
| 13 | **Simplify `SupportedActionType` to singular** | Cat 7 | LOW |
| 14 | **ActionType enum member lifecycle policy** | Cat 7 | LOW |

---

## §8 Effort + sequencing roll-up for downstream projects

**Action item buckets** (audit recommends; downstream projects execute per Q-003):

| Bucket | Effort | Cross-team needs | Recommended timing |
|---|---|---|---|
| Single-PR cleanups (DELETE 3 orphans + InsightsActionRouter cref fix + AnalysisServicesModule doc bug + ACTION-TYPE-REGISTRY.md authoring) | ~1 week | Finance Intelligence owner OK | Immediate; ungated by other waves |
| 4 Null-peer additions for Cat 6 facades + DI rewire + IInsightsAi compound-OFF integration test | ~2 weeks | Insights + Workspace + Finance teams notified | After owner sign-off (Q-002 review); high-leverage closes §F.1 gap |
| 21-consumer cache canonical adoption | ~3-5 weeks | SprkChat + Insights + Workspake + Finance + Foundry + security teams | Phased over downstream project cycles |
| `IBriefingAi` consumer cleanup (defensive `?=null` removal) | ~3-5 days | Workspace team | Bundle with Null-peer additions |

**Total W1-derived migration footprint**: ~6-8 weeks of cross-team work, ungated by W2/W3/W4 outcomes (because W1's recommendations are scoped to non-cross-cutting concerns).

---

## §9 Verification (W1 plan §"Verification commands" execution)

```powershell
# All 4 analysis files present + 8-section structure verified:
foreach ($cat in 'cache','lookup','public-contracts','node-executors') {
  $file = "projects/bff-ai-architecture-audit-r1/notes/phase2/analysis-$cat.md"
  $sectionCount = (Get-Content $file | Select-String '^## §[1-8]' | Measure-Object).Count
  Write-Host "${cat}: $sectionCount sections"
}
# Expected: each shows 8 sections (some 9 — Sub-Agent D added §9 W2 handoffs)

# Drift sections explicitly present:
foreach ($cat in 'cache','lookup','public-contracts','node-executors') {
  Get-Content "projects/bff-ai-architecture-audit-r1/notes/phase2/analysis-$cat.md" |
    Select-String '## §6 Drift report' |
    ForEach-Object { Write-Host "$cat : §6 present" }
}
```

Verification results: all 4 docs have the 8-section structure + §6 Drift report; HARD GATEs verified in Sub-Agent A's §3.1 (Cat 4) and Sub-Agent B's §3 (Cat 2); scope boundaries honored per §4 of each doc (no Cat 6 facade-consolidation recommendations; no Cat 7 classifier-choice migrations).

---

## §10 Status + handoff

- **W1 status**: COMPLETE (4/4 sub-agents finished, 4/4 analysis docs persisted, aggregation summary authored)
- **Next step**: Main session commits + opens PR with auto-merge for `notes/phase2/*.md` (5 files including this summary). Branch: `work/audit-r1-phase2-wave1` off master.
- **W2 dispatch**: defer until W1 PR merges (clean baseline for W2 sub-agents to read).
- **Owner consultation**: not required mid-wave; Q-002 single end-of-audit review still applies.

---

*W1 summary authored 2026-06-04 by main session from the 4 W1 sub-agent analyses. Sub-agent attribution preserved; recommendations are aggregated, not re-interpreted.*
