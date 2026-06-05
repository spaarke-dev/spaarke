# Migration Plan — Spaarke BFF AI Architecture Audit r1

> **Author**: Phase 3 Sub-Agent J
> **Date**: 2026-06-04
> **Status**: Phase 3 deliverable; consumed by downstream projects to plan implementation work
> **Pinned to**: Phase 1 inventory commit `357e6936`; [`canonical-architecture-decisions.md`](canonical-architecture-decisions.md) is the design authority
> **Companion**: [`r3-scope-recommendations.md`](r3-scope-recommendations.md) (r3 Wave 2 unblock guidance)
> **Q-locks applied**: Q-001 scope · Q-002 single end-of-audit review · Q-003 sequential cross-team coordination · Q-004 "Spaarke Canonical AI Stack" framing · Q-005 ADRs DEFERRED · Q-006 quarterly skill DEFERRED

---

## §1 Executive Summary

### §1.1 What this plan does

Phase 2 produced 8 categories of canonical-architecture decisions, 1 HIGH-priority LATENT BUG, ~2000 LOC of dead-code DELETE scope, 18 inventory accuracy corrections, 26 cache-adoption migration sites, 3 security adjudication surfaces, 8 canonical pattern docs to author, and 34 ADR candidates. This document translates those decisions into a sequenced PR pipeline with effort estimates, dependencies, cross-team coordination, and acceptance gates.

### §1.2 Roll-up — total migration footprint

| Metric | Value |
|---|---|
| Total PRs in pipeline | **8** (4 HIGH, 3 MEDIUM, 1 LOW-bundled with MED) + ~9 follow-on ADR PRs |
| Total LOC delta (code) | **~2,560 LOC removed** (DELETE PR) + **~280 LOC added** (LATENT BUG PR) = NET **~-2,280 LOC** |
| Total LOC delta (docs) | **~1,200-1,800 LOC** (pattern docs + ACTION-TYPE-REGISTRY + inventory corrections + amended XML docs) |
| Total estimated effort (engineering) | **~9-13 weeks** across all teams (cross-team coordination dominates) |
| Total estimated effort (audit-team direct PRs) | **~4-5 weeks** (PR #1 + PR #2 + PR #3 + PR #4 + half of PR #5) |
| HIGH-priority work | PR #1 + PR #2 (~3-4 weeks total — LATENT BUG + bundled DELETE) |
| Teams requiring coordination | **13** (per [canonical-architecture-decisions.md §9](canonical-architecture-decisions.md#9-cross-team-coordination-needs-q-003-sequential)) |
| Publish-size win from DELETE | ~20-30 KB compressed (~0.05% of 45.65 MB baseline; below NFR-01 attention threshold) |

### §1.3 HIGH-PRIORITY PRs (sequenced)

```
PR #1 (HIGH) ─── LATENT BUG remediation + 4 Null peers ── ~280 LOC ── ~2 weeks ── Insights+Workspace+Finance teams
   │
   ├─→ PR #2 (HIGH) ── Bundled orphan DELETE (5 orphans, 2 prunes) ── ~2,000 LOC ── ~1.5-2 weeks
   │                       └─ depends on AI Chat Playbook Builder team confirmation (AddDispBetweenBuildPlanG + IntentClassificationService)
   │
   ├─→ PR #3 (LOW)  ── Inventory corrections + Options→Configuration mv + comment-bug fix + PD XML doc ── ~2 days
   │
   └─→ PR #4 (MED)  ── ACTION-TYPE-REGISTRY.md authoring ── 1-2 days ── ungated
```

PRs #5, #6, #7, #8 follow after PR #1–#4 land — sequenced per team availability per Q-003.

### §1.4 Dependency graph (textual)

- PR #1 (LATENT BUG) UNBLOCKS PR #2 (DELETE PR is safer once 503-vs-500 contract is correct)
- PR #2 DELETE confirms scope by AI Chat Playbook Builder team (BuildPlanGenerationService, IntentClassificationService, PlaybookBuilderSystemPrompt Option B)
- PR #3 inventory + dir-mv is INDEPENDENT (can ship in parallel with PR #1 if reviewer bandwidth permits)
- PR #4 ACTION-TYPE-REGISTRY is INDEPENDENT (no code change)
- PR #5+ cache adoption depends on PR #1 contract (canonical helper, ADR-009 exception pattern)
- PR #6 InsightsIntentClassifier extraction is OWNER-TIMED (Phase 2 multi-playbook trigger)
- PR #7 pattern doc authoring is INDEPENDENT (can ship in any order)
- PR #8 runtime §F.1 fixture depends on PR #1 (the fixture must verify Option A symmetry)

### §1.5 Cross-team coordination cycles

Per Q-003 (sequential, NOT parallel), 13 teams are coordinated in priority bands:

| Band | Teams | Timing |
|---|---|---|
| **HIGH (Phase 4 follow-on)** | Insights, AI Chat Playbook Builder, Finance Intelligence, Security | Immediate after Phase 4 owner sign-off |
| **MEDIUM (sprint 1-3 post-merge)** | AIPL, AIPU R1, Workspace, SprkChat, Finance Intelligence (Invoice) | Phased per cache-adoption migration cycles |
| **LOW (background)** | AIPU R2, Foundry, Records-Matching, All-teams (registry doc) | Owner-team-driven; opportunistic timing |

---

## §2 PR sequence (HIGH-priority first)

> Effort scale: **S** = <1d; **M** = 1-3d; **L** = >3d

### §2.1 PR #1 (HIGH) — LATENT BUG remediation + 4 Null peers + comment fix

**Title**: `fix(bff): IInsightsAi compound-OFF returns 503 (not 500); add 4 missing facade Null peers`

**Source**: [canonical-architecture-decisions.md §4](canonical-architecture-decisions.md#4-the-latent-bug-and-structural-remediation-pattern) (LATENT BUG Option A) + [§2.3](canonical-architecture-decisions.md#23-layer-3--spaarke-public-contracts-facade-di-fascia) (5 facades, 5 Null peers required) + [Cat 6 §4.5](phase2/analysis-public-contracts.md) + [W4 §4.5](phase2/analysis-di-configuration.md)

**Scope** (~280 LOC):

| Item | LOC | File |
|---|---|---|
| Move `services.AddScoped<IInsightsAi, InsightsOrchestrator>()` | ~2 LOC | `InsightsFacadeModule.cs:105` → `AnalysisServicesModule.AddPublicContractsFacade:357-363` |
| Move `services.AddScoped<IPlaybookExecutionEngine, PlaybookExecutionEngine>()` | ~2 LOC | `InsightsFacadeModule.cs:95` → same compound-ON helper |
| New `NullInsightsAi` impl (6 methods throwing `FeatureDisabledException`) | ~100 LOC | `Services/Ai/PublicContracts/NullInsightsAi.cs` (new) |
| Register `NullInsightsAi` in `AddNullObjectsForCompoundOff` | ~2 LOC | `AnalysisServicesModule.cs` |
| Fix misleading comment | ~5 LOC | `AnalysisServicesModule.cs:75-79` |
| New `NullInvoiceAi` (P3 Fail-Fast) | ~25 LOC | `Services/Ai/PublicContracts/NullInvoiceAi.cs` |
| New `NullWorkspacePrefillAi` | ~25 LOC | `Services/Ai/PublicContracts/NullWorkspacePrefillAi.cs` |
| New `NullRecordMatchingAi` (forward-mitigation) | ~25 LOC | `Services/Ai/PublicContracts/NullRecordMatchingAi.cs` |
| Register all 4 Null peers in `AddNullObjectsForCompoundOff` | ~8 LOC | `AnalysisServicesModule.cs` |
| Workspace consumer cleanup (remove defensive `IBriefingAi?=null` per ADR-032 §Anti-patterns) | ~30 LOC | 4 sites (Workspace team owns) |
| Integration test asserting 503 (not 500) under `DocumentIntelligence:Enabled=false` | ~60 LOC | `tests/integration/.../InsightsCompoundOffContractTests.cs` (new) |

**Effort estimate**: **L** (~2 weeks, including cross-team Workspace consumer cleanup + integration-test authoring + review cycles)

**Cross-team coordination (Q-003 sequential)**:
- **Insights team** — owns `IInsightsAi`/`InsightsOrchestrator`; reviews structural relocation
- **Workspace team** — owns 4 `IBriefingAi` consumer sites; defensive-nullable removal per ADR-032 §Anti-patterns
- **Finance Intelligence (Invoice) team** — owns 3 `IInvoiceAi` consumer sites
- **Records-Matching team** — forward-mitigation Null peer (no current consumers)

**HARD GATES applied**:
- Grep verification: every `IInsightsAi`/`IInvoiceAi`/`IWorkspacePrefillAi`/`IRecordMatchingAi` consumer hard-injected (not defensive nullable)
- DI symmetry check: every facade has corresponding Null peer registered in same module under gate-OFF branch
- Publish-size: ≤+5 KB compressed (per [`.claude/constraints/azure-deployment.md`](.claude/constraints/azure-deployment.md) NFR-01)
- HIGH-CVE check: `dotnet list package --vulnerable --include-transitive` returns no new HIGH severities
- Format gate: `dotnet format whitespace Spaarke.sln --verify-no-changes`

**Dependencies**: NONE (this is the prerequisite for PR #2; ungated by all other PRs)

**Test plan**:
- New integration test in `tests/integration/Sprk.Bff.Api.IntegrationTests/Ai/InsightsCompoundOffContractTests.cs`:
  1. Boot BFF with `DocumentIntelligence:Enabled=false` + `Analysis:Enabled=true`
  2. POST `/api/insights/ask` with valid payload → expect 503 with body `errorCode=ai.insights.disabled`
  3. POST `/api/insights/search` → expect 503
  4. POST `/api/insights/assistant/query` → expect 503
- All 4 combinations of compound gate (both ON, DocIntel OFF only, Analysis OFF only, both OFF) — runtime-§F.1 fixture (deferred to PR #8)
- Per-facade unit test: ctor of each new `Null*Ai` resolves with `ILogger<T>` only

**Acceptance**: Owner sign-off + Insights team sign-off + Workspace team sign-off on consumer cleanup approach.

---

### §2.2 PR #2 (HIGH) — Bundled orphan DELETE (~2,000 LOC dead code)

**Title**: `chore(bff): delete 5 orphan AI services + prune PlaybookBuilderSystemPrompt + extract live builder prompt`

**Source**: [canonical-architecture-decisions.md §1.6](canonical-architecture-decisions.md#16-bundled-delete-pr--2000-loc-dead-code) (bundled DELETE) + [W3 Cat 5 §4.2](phase2/analysis-prompts.md) (Option B cascade) + [W3 wave summary §4.2](phase2/wave-3-summary.md#42-new-high-urgency-from-w3-cat-5) (W2 Cat 1 cascade correction) + [Cat 2 §3](phase2/analysis-lookup.md) (3 lookup orphans)

**Scope** (~2,000 LOC delete + ~200 LOC EXTRACT-then-relocate):

| Source | LOC | Files |
|---|---|---|
| **W1 Cat 2 lookup orphans** | ~714 | `ActionLookupService.cs` + `IActionLookupService.cs` + `SkillLookupService.cs` + `ISkillLookupService.cs` + `ToolLookupService.cs` + `IToolLookupService.cs` + 3 `AddScoped` lines in `FinanceModule.cs` + dangling cref at `InsightsActionRouter.cs:402-403` |
| **W2 Cat 1 intent classifier orphan** | ~408 | `IntentClassificationService.cs` (Type A, `Services/Ai/IntentClassificationResult.cs`) |
| **W3 Cat 5 NEW 5th orphan** | ~530 | `BuildPlanGenerationService.cs` (zero production consumers; W3 confirmed independent of `BuilderAgentService.Build()` keep-live) |
| **W2 Cat 1 secondary orphans** | ~30 | `ClarificationService.cs` (if confirmed dead by AI Chat Playbook Builder) |
| **W3 Cat 5 Option B EXTRACT-then-DELETE** | ~969 prune + ~200 create | (1) Create `Services/Ai/Builder/BuilderAgentSystemPrompt.cs` with live `Build(actions, skills, knowledge)` method (~200 LOC); (2) Update `BuilderAgentService.cs:270` reference; (3) DELETE `Services/Ai/Prompts/PlaybookBuilderSystemPrompt.cs` (~969 LOC; 80% dead post-extraction); (4) DELETE empty `Services/Ai/Prompts/` directory |
| **Total** | **~2,651 LOC delete + ~200 LOC create = NET ~-2,451 LOC** | |

**Effort estimate**: **L** (~1.5-2 weeks — review cycles, scope confirmation with AI Chat Playbook Builder team, cascade-impact verification)

**Cross-team coordination (Q-003 sequential)**:
- **Finance Intelligence** — confirms DELETE of 3 lookup orphans
- **AI Chat Playbook Builder** — confirms DELETE of `IntentClassificationService` cascade + `BuildPlanGenerationService` (NEW 5th orphan) + `PlaybookBuilderSystemPrompt.cs` Option B EXTRACT
- **Insights** — confirms downstream consumer chain is unaffected (cross-validates `BuilderAgentService` still compiles + functions)

**HARD GATES applied**:
- Grep verification: every deletable type has ZERO production consumers (verified by Phase 2 Sub-Agents B, E, G; documented in respective analysis docs)
- DI cleanup: every `AddScoped`/`AddSingleton` for deleted types removed
- Build pass: `dotnet build src/server/api/Sprk.Bff.Api/` succeeds (Option B EXTRACT preserves `BuilderAgentService.Build()` consumer)
- Test pass: full test suite green; no orphaned `*.Tests/.../IntentClassificationServiceTests.cs` etc.
- Publish-size: expected ~20-30 KB compressed win (below NFR-01 attention threshold, but a positive delta)

**Dependencies**:
- PR #1 (LATENT BUG remediation) — SHOULD land first so contract is 503-correct before refactoring downstream
- AI Chat Playbook Builder team sign-off on Option B scope
- Finance Intelligence team sign-off on lookup-orphan DELETE

**Test plan**:
- Existing tests for `BuilderAgentService` still pass (Option B preserves live method)
- Confirm no test file references deleted types (`grep -r IntentClassificationService tests/` returns empty)
- Confirm `dotnet format` passes (whitespace + style)

**Acceptance**: Owner sign-off + AI Chat Playbook Builder team sign-off + Finance Intelligence team sign-off.

---

### §2.3 PR #3 (LOW) — Inventory corrections + Options→Configuration directory mv + AnalysisServicesModule comment-bug fix + PlaybookDispatcher XML doc amendment

**Title**: `docs(bff): inventory corrections (18) + Options dir consolidation + comment-bug fix + PD XML doc amendment`

**Source**: [canonical-architecture-decisions.md §6](canonical-architecture-decisions.md#6-inventory-corrections-consolidated-for-inventory-correction-pr) (18 inventory corrections) + [W4 §3 row 2](phase2/analysis-di-configuration.md) (Options→Configuration dir consolidation) + [§4.1 LATENT BUG §4.5](canonical-architecture-decisions.md#43-option-a-structural-remediation-w4-45--recommended) (comment-bug at `AnalysisServicesModule.cs:75-79`) + [§9 SprkChat row](canonical-architecture-decisions.md#9-cross-team-coordination-needs-q-003-sequential) (PD XML doc lead with tenantId rationale)

**Scope** (~2 days; minor edits across audit notes + 2 file moves + 2 documentation amendments):

| Item | LOC delta | File |
|---|---|---|
| Inventory accuracy corrections (18 findings from §6) | ~120 LOC docs | `projects/bff-ai-architecture-audit-r1/notes/inventory.md` (or amend in-place) |
| `Options/` → `Configuration/` directory `git mv` | 2 files moved | `src/server/api/Sprk.Bff.Api/Options/*.cs` → `src/server/api/Sprk.Bff.Api/Configuration/*.cs` (already-correct namespace `Sprk.Bff.Api.Configuration`; no source edits required) |
| `AnalysisServicesModule.cs:75-79` factually-incorrect comment fix | ~5 LOC | Already covered by PR #1 (LATENT BUG) — bundle here OR keep in PR #1 (recommended: PR #1) |
| `PlaybookDispatcher.cs:99-102` XML doc amendment | ~10 LOC | Lead with tenant-scoping rationale, ADR-010 budget secondary |
| Inventory doc-bug fix at `BuildPlanGenerationService` (NEW 5th orphan) entry | ~5 LOC docs | Inventory addition (or amend §6.2 orphan list) |

**Effort estimate**: **S** (~1-2 days — almost entirely docs)

**Cross-team coordination (Q-003 sequential)**:
- **SprkChat team** — confirms PlaybookDispatcher XML doc rewrite is correct
- Otherwise audit-team-only

**HARD GATES applied**:
- Build pass after `git mv` (namespace unchanged; only directory location changes)
- Verify all `using` statements unaffected (namespace match)

**Dependencies**: NONE; can ship in parallel with PR #1.

**Test plan**: Build + format pass; no functional tests required (docs + file moves only).

**Acceptance**: Owner sign-off; SprkChat sign-off on PD XML doc amendment.

---

### §2.4 PR #4 (MEDIUM) — ACTION-TYPE-REGISTRY.md authoring

**Title**: `docs(bff): author ACTION-TYPE-REGISTRY.md (block reservations + owner-per-block + deprecation policy)`

**Source**: [canonical-architecture-decisions.md §2.4](canonical-architecture-decisions.md#24-layer-4--spaarke-node-executor-registry) (missing companion artifact) + [Cat 7 §3](phase2/analysis-node-executors.md) (HIGH-priority deliverable to preempt collision risk)

**Scope** (~100-200 LOC doc):

| Section | Content |
|---|---|
| Block reservation table | Block 0-2 AI primitives (Foundry), 10-12 reserved computation, 20-29 external integration (Workspace+SprkChat), 30-39 control flow, 40-49 output, 50-59 notification/query, 60-69 Foundry, 70-149 Insights Engine |
| Next-available roster | Current `ActionType` enum members + next-free per block |
| Owner-per-block matrix | Which team owns enum-member allocation per block |
| Deprecation policy | How to retire an `ActionType` member without breaking deployed playbooks |
| Concurrency rule | New `ActionType` allocation requires `ACTION-TYPE-REGISTRY.md` PR before code lands (preempts parallel-worktree collision) |

**Effort estimate**: **S** (1-2 days — docs only)

**Cross-team coordination (Q-003 sequential)**: All teams that own a block (Foundry, Workspace, SprkChat, Insights, Finance, Records-Matching, AIPL, AIPU R1, AIPU R2). Single coordination cycle: circulate draft, collect block-ownership confirmations, publish.

**HARD GATES applied**: Doc-only; build/test gate trivially passes.

**Dependencies**: NONE; ungated by other PRs.

**Test plan**: N/A (doc-only).

**Acceptance**: Owner sign-off + all team-block owners ack.

---

### §2.5 PR #5+ (MEDIUM, phased) — Cache adoption migration

**Title**: `refactor(bff): migrate {service} to DistributedCacheExtensions.GetOrCreateAsync<T> canonical helper`

**Source**: [canonical-architecture-decisions.md §2.1](canonical-architecture-decisions.md#21-layer-1--spaarke-canonical-cache-stack) (Cache Stack adoption gap — 0% adoption inside `Services/Ai/`) + [Cat 4 §3](phase2/analysis-cache.md) (21 sites + 5 NEW from W2 Cat 1+3 = 26 sites)

**Scope** (~26 sites; phased per team):

| Team | Sites | Effort | Recommended sprint |
|---|---|---|---|
| **AIPL** | ~3 (Rag-adjacent) | S each | Sprint 1 |
| **AIPU R1** | ~4 (SessionPersistenceService + adjacencies) | M | Sprint 1 |
| **SprkChat** | 8+ (`Chat/` namespace) | M | Sprint 2 |
| **Insights** | ~5 (PlaybookExecutionCache adjacencies + IntentClassifier cache) | M | Sprint 2 |
| **Workspace** | ~3 (briefing cache adjacencies) | S | Sprint 3 |
| **Finance Intelligence** | ~3 (invoice-cache + lookup-cache) | S | Sprint 3 |

**Effort estimate**: **L (aggregate)** (~3-5 weeks aggregate across teams; ~5-10 days per team in parallel cycles)

**Cross-team coordination (Q-003 sequential)**:
- **Per-team owner decisions** — each team commits to a sprint for their owned sites
- **AIPL leads** — they ship the canonical reference impl change first (highest visibility)
- **Security team** — adjudicates `PrivilegeGroupResolver` cache (§7.3 — out-of-band; may delay that 1 site)

**HARD GATES applied (per migrating PR)**:
- Behavior preservation: cache hit ratio + miss latency unchanged in OTEL telemetry
- ADR-009 exception XML doc added IF the migrated service preserves `MemoryCache` (per the gold-standard `OrchestratorPromptBuilder.cs:36-44` precedent)
- Publish-size: no net increase
- Format + format + tests pass

**Dependencies**:
- PR #1 (LATENT BUG) — establishes ADR-009 exception XML doc convention as binding precedent
- ADR-009 amendment landing (Phase 4 follow-on) helpful but not strictly required

**Test plan**:
- Per-site unit test: cache hit + cache miss preserve current behavior
- Integration test: OTEL hit-ratio + latency unchanged within ±5%
- Smoke verification post-deploy

**Acceptance**: Per-team sign-off + audit-team sign-off on canonical-helper adoption pattern.

---

### §2.6 PR #6 (MEDIUM, time-boxed) — InsightsIntentClassifier.BuildPrompt() extraction

**Title**: `refactor(bff): extract InsightsIntentClassifier hardcoded prompt to sprk_analysisaction.sprk_systemprompt`

**Source**: [canonical-architecture-decisions.md §3 Cat 5 row](canonical-architecture-decisions.md#3-per-category-decisions-roll-up) + [§11.7 Q-28](canonical-architecture-decisions.md#117-low-priority) (time-boxed inline extraction trigger) + r3 Tier 2.5 §2.2.1 F-2 work item ([`projects/ai-spaarke-insights-engine-r3/design.md`](../../ai-spaarke-insights-engine-r3/design.md) §2.2.1)

**Scope** (~200-300 LOC):

| Item | LOC delta | File |
|---|---|---|
| Extract `BuildPrompt()` hardcoded C# block to JPS `sprk_analysisaction.sprk_systemprompt` row | ~80 LOC removed | `InsightsIntentClassifier.cs:200-260` (approx) |
| Update `InsightsIntentClassifier` to fetch prompt via `IPlaybookService` or `IAnalysisActionRepository` | ~40 LOC added | `InsightsIntentClassifier.cs` |
| Add Dataverse seed/migration for new `sprk_analysisaction` row | ~50 LOC PS | `scripts/Seed-InsightsIntentClassifier-Prompt.ps1` (new) |
| Test fixture for prompt-retrieval path | ~80 LOC | `tests/unit/.../InsightsIntentClassifierPromptFetchTests.cs` |

**Effort estimate**: **M** (~3-5 days — extraction + Dataverse seed + test)

**Cross-team coordination (Q-003 sequential)**:
- **Insights team** — owns timing; trigger is "Phase 2 multi-playbook ships" (already triggered by r3 Tier 2.5 — see [`r3-scope-recommendations.md` §3](r3-scope-recommendations.md))
- **No cross-team handoff** required (internal Insights refactor)

**HARD GATES applied**:
- Behavior preservation: classification accuracy unchanged on golden-set fixtures
- Cache strategy preserved (SHA-256 keys + 1-hour TTL); ADR-009 exception XML doc retained
- Format + tests pass

**Dependencies**:
- r3 Tier 1.5 (index rename `playbook-embeddings` → `spaarke-playbook-index`) — must complete first to avoid double-rename
- r3 Tier 2.5 reconciliation (§2.2.1 F-1 decision spike) — informs whether this extraction merges into the dispatcher refactor

**Test plan**:
- Golden-set test: existing intent-classification fixtures all still pass
- Dataverse-seed integration test: prompt row populated correctly
- Smoke deploy: Dev environment classification round-trip works

**Acceptance**: Insights team sign-off; r3 owner confirms timing aligns with Tier 2.5.

---

### §2.7 PR #7+ (LOW) — Pattern doc authoring (8 canonical patterns)

**Title** (per doc): `docs(architecture): author {canonical-pattern-name} pattern doc`

**Source**: [canonical-architecture-decisions.md §10.3](canonical-architecture-decisions.md#103-pattern-doc-authoring-600-800-loc-docs) + [§2.1-§2.8 per-layer descriptors](canonical-architecture-decisions.md#2-the-spaarke-canonical-ai-stack-q-004-naming-synthesis)

**Scope** (~600-800 LOC docs aggregate; 8 separate doc PRs):

| Pattern doc | Source layer | LOC | Owner |
|---|---|---|---|
| Spaarke Canonical Cache Stack | §2.1 | ~100 LOC | AIPL |
| Spaarke Lookup Pattern (degraded to pattern-only) | §2.2 | ~50 LOC | Finance Intelligence (post-DELETE) |
| Spaarke Public-Contracts Facade DI Fascia | §2.3 | ~150 LOC | Insights + audit team |
| Spaarke Node Executor Registry (companion to ACTION-TYPE-REGISTRY.md) | §2.4 | ~80 LOC | Foundry |
| Spaarke Canonical Intent Classifier Pattern | §2.5 | ~80 LOC | Insights |
| Spaarke Canonical Search Substrate Architecture + Double-Gate Null-Object Pattern | §2.6 | ~120 LOC | AIPL + AIPU R1 |
| Spaarke Canonical Prompt Construction Pattern | §2.7 | ~100 LOC | AIPU R1 |
| Spaarke Endpoint↔DI Symmetry Rule + DI Module Audit Convention | §2.8 | ~150 LOC | Audit team |

**Effort estimate**: **L (aggregate)** (~1-1.5 weeks aggregate; ~1 day per doc)

**Cross-team coordination (Q-003 sequential)**: Per-doc owner team; no global coordination cycle (each doc is independent).

**HARD GATES applied**: Doc-only.

**Dependencies**: PR #1 + PR #2 should land first so the canonical-stack pattern docs reflect post-remediation reality.

**Test plan**: N/A (doc-only).

**Acceptance**: Owner sign-off + per-pattern team sign-off.

---

### §2.8 PR #8 (MEDIUM) — Runtime §F.1 detection fixture

**Title**: `test(bff): add runtime §F.1 detection fixture (4 compound-gate combinations × all unconditionally-mapped endpoints)`

**Source**: [canonical-architecture-decisions.md §4.5](canonical-architecture-decisions.md#45-runtime-f1-detection-fixture-w4-42) + [W4 §4.2](phase2/analysis-di-configuration.md) (proposed ADR-CAND-W4-2)

**Scope** (~300-500 LOC):

| Item | LOC | File |
|---|---|---|
| Reusable `WebApplicationFactory<Program>` subclass | ~80 LOC | `tests/integration/Sprk.Bff.Api.IntegrationTests/Infrastructure/CompoundGateFixture.cs` (new) |
| 4-combination test class (both ON, DocIntel-OFF only, Analysis-OFF only, both OFF) | ~80 LOC per combination = ~320 LOC | `tests/integration/Sprk.Bff.Api.IntegrationTests/Ai/CompoundGateF1DetectionTests.cs` (new) |
| Endpoint enumeration helper (probes every unconditionally-mapped endpoint in `EndpointMappingExtensions.cs`) | ~100 LOC | Helper within fixture |
| Per-combination assertion: NO 500 with "Unable to resolve" body | ~20 LOC | Helper |

**Effort estimate**: **L** (~1-2 weeks — fixture authoring + per-combination test debug)

**Cross-team coordination (Q-003 sequential)**:
- **Insights team** — owns (the team most affected by the LATENT BUG); per [canonical-architecture-decisions.md §9](canonical-architecture-decisions.md#9-cross-team-coordination-needs-q-003-sequential) Insights row
- **Audit team** — consults on §F.1 rule formalization

**HARD GATES applied**:
- Fixture catches the LATENT BUG on first run (regression-test confidence)
- Fixture runs in CI; passes under current HEAD (post-PR #1)
- Runtime ≤2 minutes for full 4-combination matrix

**Dependencies**:
- PR #1 (LATENT BUG remediation) — fixture must verify Option A symmetry; without PR #1, fixture would fail on baseline

**Test plan**:
- Self-test: fixture run on HEAD pre-PR #1 → expect failure (regression catch)
- Fixture run on HEAD post-PR #1 → expect pass
- Fixture runs in CI on every PR touching `EndpointMappingExtensions.cs` or DI modules

**Acceptance**: Insights team sign-off + audit team sign-off.

---

## §3 Effort buckets

### §3.1 Immediate (this sprint)

| Bucket | Effort | Scope |
|---|---|---|
| PR #1 (LATENT BUG + 4 Null peers) | ~2 weeks | ~280 LOC code + integration test |
| PR #2 (Bundled DELETE) | ~1.5-2 weeks | ~2,000 LOC removed + ~200 LOC EXTRACT-then-relocate |
| PR #3 (Inventory + dir mv) | ~1-2 days | Doc + 2 file moves |
| PR #4 (ACTION-TYPE-REGISTRY) | ~1-2 days | ~100-200 LOC doc |
| **Total immediate** | **~3.5-4.5 weeks** | **~280 LOC added + ~2,000 LOC removed + ~400 LOC docs** |

### §3.2 Short-term (1-2 sprints)

| Bucket | Effort | Scope |
|---|---|---|
| PR #5 cache adoption Phase 1 (AIPL + AIPU R1 first 7 sites) | ~1 week | ~7 sites |
| PR #6 InsightsIntentClassifier extraction (after r3 Tier 2.5 informs) | ~3-5 days | ~200-300 LOC |
| PR #7 first 3 pattern docs (Cache, Intent, Search) | ~3-5 days | ~300 LOC docs |
| PR #8 Runtime §F.1 fixture | ~1-2 weeks | ~500 LOC test infra |
| **Total short-term** | **~3-5 weeks** | |

### §3.3 Medium-term (3-5 sprints)

| Bucket | Effort | Scope |
|---|---|---|
| PR #5 cache adoption Phase 2 (SprkChat + Insights + Workspace + Finance Intelligence — remaining 19 sites) | ~3-5 weeks | ~19 sites in parallel teams |
| PR #7 remaining pattern docs (Public-Contracts Facade, Node Executor Registry, Prompt Construction, Symmetry Rule, Module Audit) | ~5-7 days | ~500 LOC docs |
| **Total medium-term** | **~4-6 weeks** | |

### §3.4 Long-term (follow-on phase per Q-005)

| Bucket | Effort | Scope |
|---|---|---|
| 34 ADR candidates → ADRs (HIGH 11 + MEDIUM 12 + LOW 11) | ~2-3 weeks per audit design.md §3.1 | ~30 ADR docs |
| Quarterly Review Skill design (Q-006 DEFERRED) | TBD | TBD |
| **Total long-term** | **~2-3 weeks** | |

---

## §4 Per-team coordination cycles (Q-003 sequential)

> For each of 13 teams from [canonical-architecture-decisions.md §9](canonical-architecture-decisions.md#9-cross-team-coordination-needs-q-003-sequential): what audit needs + PR list + recommended sequencing.

### §4.1 Insights team (HIGH)

| Audit asks | PRs | Recommended sequencing |
|---|---|---|
| Approve LATENT BUG Option A structural relocation | PR #1 | Immediate; PR #1 starts here |
| Confirm `BuilderAgentService` consumer chain unaffected by Option B EXTRACT | PR #2 | Sequence after PR #1 lands |
| Approve `InsightsIntentClassifier.BuildPrompt()` extraction timing (Phase 2 trigger via r3 Tier 2.5) | PR #6 | Insights team owns timing |
| Author runtime §F.1 detection fixture | PR #8 | After PR #1 lands |
| Cache adoption — 5 sites in Insights-owned services | PR #5 (Sprint 2) | Phase 2 |
| Authoring Spaarke Canonical Intent Classifier Pattern + Public-Contracts Facade DI Fascia docs | PR #7 | Anytime; opportunistic |

### §4.2 AI Chat Playbook Builder team (HIGH)

| Audit asks | PRs | Recommended sequencing |
|---|---|---|
| Confirm DELETE of `IntentClassificationService` cascade (orphan since 2026-03) | PR #2 | Before PR #2 opens |
| Confirm DELETE of NEW 5th orphan `BuildPlanGenerationService.cs` (~530 LOC) | PR #2 | Before PR #2 opens |
| Confirm Option B EXTRACT scope for `PlaybookBuilderSystemPrompt.cs` | PR #2 | Before PR #2 opens |
| Confirm `ClarificationService` orphan status | PR #2 | Before PR #2 opens |

### §4.3 Finance Intelligence team (HIGH)

| Audit asks | PRs | Recommended sequencing |
|---|---|---|
| Confirm DELETE of 3 lookup orphans (`ActionLookupService`, `SkillLookupService`, `ToolLookupService`) — ~714 LOC | PR #2 | Before PR #2 opens |
| Confirm DI cleanup in `FinanceModule.cs` | PR #2 | Bundled with above |
| Confirm dangling cref at `InsightsActionRouter.cs:402-403` | PR #2 | Bundled with above |

### §4.4 Finance Intelligence (Invoice) team (MEDIUM)

| Audit asks | PRs | Recommended sequencing |
|---|---|---|
| Add `NullInvoiceAi` P3 Fail-Fast peer | PR #1 | Bundled with PR #1 |
| Remove defensive `IInvoiceAi?=null` + `RequireAi()` pattern in 3 consumer sites | PR #1 | Bundled with PR #1 |
| Cache adoption — 3 sites in invoice-cache + lookup-cache | PR #5 (Sprint 3) | Phase 3 |

### §4.5 Workspace team (MEDIUM)

| Audit asks | PRs | Recommended sequencing |
|---|---|---|
| Add `NullWorkspacePrefillAi` P3 Fail-Fast peer | PR #1 | Bundled with PR #1 |
| Clean up 4 `IBriefingAi` consumer sites (remove defensive `?=null` + `RequireAi()`) | PR #1 | Bundled with PR #1 |
| Cache adoption — 3 sites in briefing-cache adjacencies | PR #5 (Sprint 3) | Phase 3 |

### §4.6 Records-Matching team (LOW — forward-mitigation)

| Audit asks | PRs | Recommended sequencing |
|---|---|---|
| Add `NullRecordMatchingAi` (zero current consumers; forward-mitigation) | PR #1 | Bundled with PR #1 |

### §4.7 Security team (HIGH)

| Audit asks | PRs | Recommended sequencing |
|---|---|---|
| Adjudicate `RecordSearchService` tenant-isolation model (§7.1) | Out-of-band | Phase 4 owner-driven |
| Adjudicate `SemanticSearchService` privilege-filter gap (§7.2) | Out-of-band | Phase 4 owner-driven |
| Adjudicate `PrivilegeGroupResolver` ADR-009 conformance (§7.3) | Out-of-band; informs PR #5 (PrivilegeGroupResolver migration) | Phase 4 owner-driven; gates PrivilegeGroupResolver cache PR |

### §4.8 AIPL team (MEDIUM — cache canonical authority)

| Audit asks | PRs | Recommended sequencing |
|---|---|---|
| Confirm `EmbeddingCache` + `DistributedCacheExtensions.GetOrCreateAsync<T>` as canonicals | PR #4 / PR #5 | Sprint 1 |
| Cache adoption — 3 Rag-adjacent sites | PR #5 (Sprint 1) | Sprint 1 |
| Author Spaarke Canonical Cache Stack pattern doc + Search Substrate doc | PR #7 | Anytime |

### §4.9 AIPU R1 team (MEDIUM)

| Audit asks | PRs | Recommended sequencing |
|---|---|---|
| Confirm `OrchestratorPromptBuilder` as canonical two-layer cached prompt | PR #4 / PR #5 | Sprint 1 |
| Cache adoption — `SessionPersistenceService` + 3 adjacencies | PR #5 (Sprint 1) | Sprint 1 |
| Author Spaarke Canonical Prompt Construction pattern doc | PR #7 | Anytime |

### §4.10 AIPU R2 team (LOW)

| Audit asks | PRs | Recommended sequencing |
|---|---|---|
| Confirm `CapabilityManifest` cache designated keep-special-case (correctness-critical) | Out-of-band | Sprint 4+ |

### §4.11 SprkChat team (MEDIUM)

| Audit asks | PRs | Recommended sequencing |
|---|---|---|
| Cache adoption — 8+ sites in `Chat/` namespace | PR #5 (Sprint 2) | Sprint 2 |
| PlaybookDispatcher XML doc amendment (lead with tenantId rationale, ADR-010 budget secondary) | PR #3 | Bundled with PR #3 |
| Confirm `PlaybookDispatcher` is shared invocation pattern (Insights consumes via r3 Tier 2.5) | Out-of-band | r3 Tier 2.5 dependency |

### §4.12 Foundry team (LOW)

| Audit asks | PRs | Recommended sequencing |
|---|---|---|
| Runtime Kill-Switch Pattern codification (peer to ADR-030 DI Null-Object) | PR #7 | Anytime |
| Cache adoption — Foundry-adjacent sites | PR #5 (Sprint 2-3) | Phase 2-3 |
| Author Spaarke Node Executor Registry pattern doc | PR #7 | After PR #4 ACTION-TYPE-REGISTRY ships |

### §4.13 All teams (block owners for ACTION-TYPE-REGISTRY)

| Audit asks | PRs | Recommended sequencing |
|---|---|---|
| Confirm block ownership (Foundry 0-2/60-69, Workspace 20-29, SprkChat 30-39, Insights 70-149, Finance 50-59, etc.) | PR #4 | Sprint 1 |

---

## §5 HARD GATE application examples

> Documents how HARD GATES are applied to each PR, providing downstream team reference per [`.claude/constraints/bff-extensions.md`](.claude/constraints/bff-extensions.md).

### §5.1 PR #1 LATENT BUG remediation — HARD GATE walkthrough

| HARD GATE | Verification command | Expected outcome |
|---|---|---|
| Grep verification (DI symmetry) | `grep -rn "AddScoped<.*Ai," src/server/api/Sprk.Bff.Api/Infrastructure/DI/` + `grep -rn "AddScoped<Null.*Ai," ...` | Every facade `IXxxAi` has corresponding `NullXxxAi` registration |
| Publish-size | `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/ && du -h deploy/api-publish/` | ≤+5 KB delta vs prior baseline (~45.65 MB) |
| HIGH-CVE | `dotnet list package --vulnerable --include-transitive` | No new HIGH severities |
| Format gate | `dotnet format whitespace Spaarke.sln --verify-no-changes` | Exit 0 |
| Test pass | `dotnet test` | All tests green |
| Integration test for 503-not-500 | `dotnet test --filter "InsightsCompoundOffContractTests"` | Pass — body contains `errorCode=ai.insights.disabled` |

### §5.2 PR #2 Bundled DELETE — HARD GATE walkthrough

| HARD GATE | Verification command | Expected outcome |
|---|---|---|
| Zero consumers (per deletable type) | `grep -rn "IntentClassificationService" src/ tests/` | Returns empty (post-DELETE) |
| DI cleanup (per deletable type) | `grep -rn "AddScoped<.*IntentClassificationService" src/server/` | Returns empty |
| Build pass | `dotnet build src/server/api/Sprk.Bff.Api/` | Exit 0 |
| Test pass | `dotnet test` | All tests green (no orphaned test files) |
| Format gate | `dotnet format whitespace Spaarke.sln --verify-no-changes` | Exit 0 |
| Publish-size | (same command) | Expected ~20-30 KB compressed win |

### §5.3 PR #5+ Cache adoption (per migrating service) — HARD GATE walkthrough

| HARD GATE | Verification command | Expected outcome |
|---|---|---|
| Behavior preservation | OTEL hit-ratio + latency comparison (pre vs post) | Within ±5% |
| ADR-009 exception XML doc (if `MemoryCache` retained) | `grep "ADR-009" src/server/api/Sprk.Bff.Api/Services/Ai/{ServiceName}.cs` | XML doc present per `OrchestratorPromptBuilder.cs:36-44` precedent |
| Test pass | `dotnet test` | All tests green |
| Smoke verification post-deploy | Manual + telemetry | Cache hit ratio recovers within 10 min of warm-up |

---

## §6 Dependency graph

```
                         ┌──────────────────────────────────────┐
                         │  Phase 4 Owner Review (Q-002)        │
                         │  Sign-off on:                        │
                         │    - LATENT BUG Option A             │
                         │    - DELETE PR scope (3 teams)       │
                         │    - Cross-team coord timing         │
                         │    - 34 ADR candidates prioritization│
                         │    - Canonical names lock (Q-004)    │
                         └────────────────┬─────────────────────┘
                                          │
                ┌─────────────────────────┼─────────────────────────┐
                ▼                         ▼                         ▼
    ┌──────────────────┐      ┌──────────────────┐      ┌──────────────────┐
    │  PR #1 LATENT BUG│      │  PR #3 Inventory │      │  PR #4 ACTION-   │
    │  + 4 Null peers  │      │  + dir mv + XML  │      │  TYPE-REGISTRY   │
    │  + 503 fix       │      │  doc amend       │      │  authoring       │
    │  ~280 LOC, ~2w   │      │  ~2 days         │      │  ~1-2 days       │
    └────────┬─────────┘      └──────────────────┘      └──────────────────┘
             │                          ▲                          ▲
             │                          │ (independent)            │ (independent)
             ├──────────────┐           │                          │
             ▼              ▼           │                          │
    ┌──────────────┐ ┌──────────────┐   │                          │
    │  PR #2 DELETE│ │  PR #8 §F.1  │   │                          │
    │  ~2000 LOC   │ │  Runtime fix │   │                          │
    │  ~1.5-2w     │ │  ~1-2 weeks  │   │                          │
    │              │ │  Insights    │   │                          │
    │  Needs:      │ │  owns        │   │                          │
    │  - Finance   │ └──────────────┘   │                          │
    │  - AI Chat PB│                    │                          │
    └──────┬───────┘                    │                          │
           │                            │                          │
           ▼                            │                          │
    ┌──────────────────────────────────────────────────────────────────┐
    │  PR #5 Cache adoption (phased)         PR #6 InsightsIC Extract │
    │  - Sprint 1: AIPL + AIPU R1 (~7 sites) - depends r3 Tier 2.5    │
    │  - Sprint 2: SprkChat + Insights       - ~3-5 days              │
    │  - Sprint 3: Workspace + Finance Int.  - Insights team owns     │
    │  Total ~26 sites, ~3-5 weeks aggregate                           │
    └──────────────────────────────────────────────────────────────────┘

    ┌──────────────────────────────────────────────────────────────────┐
    │  PR #7 Pattern doc authoring (8 docs, ~600-800 LOC)             │
    │  Per-team owners; independent; opportunistic; bundle/serial OK   │
    │  Recommended: ship 3 docs (Cache, Intent, Search) after PR #5    │
    │  Sprint 1; ship remaining 5 after PR #2 + #1 land                │
    └──────────────────────────────────────────────────────────────────┘
```

### §6.1 Critical-path PRs

The audit's critical-path PRs (sequenced):
1. Phase 4 Owner Review (gates all)
2. PR #1 (LATENT BUG) — gates PR #2 + PR #8
3. PR #2 (DELETE) — frees ~2,000 LOC and unlocks pattern-doc adoption
4. PR #5 (Cache adoption Sprint 1) — opens phased cross-team migration
5. PR #8 (Runtime §F.1 fixture) — closes the §F.1 governance loop

PR #3, #4, #6, #7 are parallel-track work; can ship in any order.

### §6.2 PR dependencies summary table

| PR | Depends on | Blocks |
|---|---|---|
| PR #1 | Phase 4 Owner Review | PR #2, PR #8 |
| PR #2 | PR #1 (preferred sequencing) + Finance + AI Chat PB team sign-off | (Sequential pattern docs) |
| PR #3 | None | None |
| PR #4 | None | (Sequential PR #7 for Node Executor doc) |
| PR #5 | PR #1 (precedent for ADR-009 exception XML doc) | None |
| PR #6 | r3 Tier 2.5 reconciliation decision | None |
| PR #7 | (per-doc dependencies; see PR #1+#2 for accuracy) | None |
| PR #8 | PR #1 (fixture must verify Option A) | None |

---

## §7 Risk register

> Per migration work item, risk + mitigation + owner.

| # | Risk | Mitigation | Owner |
|---|---|---|---|
| R-1 | PR #1 integration test misses an edge case → LATENT BUG ships under different gate combination | PR #8 Runtime §F.1 fixture provides 4-combination coverage | Insights team |
| R-2 | PR #2 DELETE breaks `BuilderAgentService.Build()` due to incomplete Option B EXTRACT | Mandatory build + test gate; W3 §3.1 verified extract scope; cross-validate with AI Chat Playbook Builder team | AI Chat PB team |
| R-3 | PR #2 confirmation cycle stalls due to AI Chat Playbook Builder team availability | Phase-4 owner can escalate; PR #2 ships in parallel sub-bundle (lookups only, keep cascade pending) | Audit team + owner |
| R-4 | PR #5 cache migration regresses cache hit-ratio | OTEL hit-ratio + latency comparison HARD GATE per service | Per-team owner |
| R-5 | PR #5 PrivilegeGroupResolver site stalls on Security adjudication (§7.3) | Sequence PrivilegeGroupResolver site LAST; Security team adjudication independent track | AIPL + Security |
| R-6 | PR #6 InsightsIntentClassifier extraction collides with r3 Tier 2.5 reconciliation work | Audit team + r3 owner coordinate timing; r3 Tier 2.5 F-2 may absorb PR #6 scope | Insights team + r3 owner |
| R-7 | PR #8 §F.1 fixture hits race conditions on parallel CI runs | xUnit `IClassFixture` lifetime; per-combination test isolation | Insights team |
| R-8 | ACTION-TYPE-REGISTRY.md (PR #4) ships without all team-block owners confirming → registry drifts | PR #4 reviewer enforces per-block sign-off; circulate before merge | Audit team |
| R-9 | Cross-team coordination cascades exceed 5-7 weeks → audit recommendations lose currency | Phase 4 owner reviews progress at week-3 checkpoint; deprioritize LOW-effort PRs if needed | Owner |
| R-10 | r3 Tier 2.5 work proceeds without PR #1 LATENT BUG fix → r3 inherits 500-not-503 contract | r3 Wave 2 design.md must reference PR #1 as dependency (per [`r3-scope-recommendations.md` §3](r3-scope-recommendations.md)) | r3 owner |
| R-11 | Publish-size delta exceeds NFR-01 attention threshold (~+5 MB) | PR #1 + PR #2 are net-negative; PR #5 cache adoption neutral; no add-only PR | All teams |
| R-12 | Security adjudication (§7.1/§7.2/§7.3) takes ≥2 weeks | Phase 4 owner schedules Security review immediately at sign-off; tracks separately | Security + owner |

---

## §8 Verification — how to confirm migration is complete

> Per PR, the binary "is it done" check.

| PR | Done check |
|---|---|
| PR #1 | `dotnet test --filter "InsightsCompoundOffContractTests"` returns 4 passing tests (one per endpoint × 3 + global symmetry assertion). `grep -rn "Null.*Ai," src/server/api/.../PublicContracts/` returns 5 matches. `AnalysisServicesModule.cs:75-79` comment matches code reality. |
| PR #2 | `grep -rn "IntentClassificationService\|BuildPlanGenerationService\|ActionLookupService\|SkillLookupService\|ToolLookupService\|PlaybookBuilderSystemPrompt" src/ tests/` returns empty (except in archive notes). `BuilderAgentSystemPrompt.cs` exists at `Services/Ai/Builder/`. Empty `Services/Ai/Prompts/` directory removed. Build + test green. |
| PR #3 | `git diff --stat origin/master..HEAD` shows ~120 LOC docs + 2 file moves; namespace `Sprk.Bff.Api.Configuration` unchanged. Inventory §6 corrections applied. |
| PR #4 | `docs/architecture/ACTION-TYPE-REGISTRY.md` exists with 5 sections (block reservation, next-available, owner-per-block, deprecation policy, concurrency rule). All teams ack via PR review. |
| PR #5 (per service migrated) | OTEL telemetry shows ±5% hit-ratio + latency vs baseline. `grep -A 5 "GetOrCreateAsync" src/server/api/Sprk.Bff.Api/Services/{Service}.cs` shows canonical helper in use. If `MemoryCache` retained, XML doc per `OrchestratorPromptBuilder.cs:36-44` precedent. |
| PR #6 | `sprk_analysisaction` row for `insights-intent-classifier` populated in Dev environment; `InsightsIntentClassifier.cs` no longer contains hardcoded prompt; classification golden-set tests pass. |
| PR #7 (per doc) | Doc exists at `docs/architecture/{pattern-name}.md`; cross-references canonical reference impl(s) + ADR candidates; per-team owner ack via PR review. |
| PR #8 | Runtime fixture in `tests/integration/.../CompoundGateF1DetectionTests.cs` covers 4 combinations × all unconditionally-mapped endpoints; CI runs <2 minutes; regression-catch confidence established. |

---

## §9 Open questions for owner review

> Specific to migration plan (different from [canonical-architecture-decisions.md §11](canonical-architecture-decisions.md#11-open-questions-packaged-for-q-002-single-end-of-audit-review) which captures audit-wide questions).

### §9.1 PR sequencing approvals

1. **MIGR-Q1**: Confirm PR #1 (LATENT BUG) ships BEFORE PR #2 (DELETE) — or accept parallel ship if cross-team confirmation cycles permit?
2. **MIGR-Q2**: Confirm PR #3 (inventory + dir mv) bundling: inventory corrections + Options→Configuration mv in single PR, or split for review-bandwidth ease?
3. **MIGR-Q3**: Confirm PR #4 (ACTION-TYPE-REGISTRY) standalone vs bundled with PR #7 first pattern doc (Node Executor Registry pattern)?

### §9.2 Team coordination sequencing

4. **MIGR-Q4**: Recommended cache-adoption phasing (AIPL+AIPU R1 → SprkChat+Insights → Workspace+Finance) — accept Q-003 sequential cadence?
5. **MIGR-Q5**: Security adjudication of `PrivilegeGroupResolver` (§7.3) blocks 1 cache-migration site — accept sequence-last approach OR ship migration provisionally w/ docs flagging Security review pending?
6. **MIGR-Q6**: r3 Tier 2.5 reconciliation (§3.1 F-1 decision spike) may absorb PR #6 — accept r3 owner timing authority?

### §9.3 Effort estimate validations

7. **MIGR-Q7**: PR #1 estimate ~2 weeks — does owner concur, or expect ~1 week (smaller integration-test scope) / ~3 weeks (broader Workspace consumer rework)?
8. **MIGR-Q8**: PR #2 estimate ~1.5-2 weeks — does owner concur, accounting for 3-team confirmation cycles?
9. **MIGR-Q9**: Total migration footprint ~9-13 weeks across all teams — acceptable, or scope reduce (defer LOW-priority PRs) to ~5-7 weeks?

### §9.4 Phase 4 → ADR phase handoff

10. **MIGR-Q10**: After PR #1 + PR #2 land, recommended ADR phase priority order — HIGH 11 first (LATENT BUG + Symmetry Rule + Cache Stack + Facade Null-Peer Mandate), or interleaved by team?
11. **MIGR-Q11**: Quarterly Review Skill design (Q-006 DEFERRED) — author after ADR phase, or defer further pending production drift telemetry?

### §9.5 Misc

12. **MIGR-Q12**: PR #7 pattern doc authoring — bundle multiple docs per PR (e.g., Cat 1+3+5+7 pattern docs into 1 PR), or 8 separate PRs for per-team reviewer bandwidth?
13. **MIGR-Q13**: Inventory corrections PR (PR #3) — apply corrections in-place to `inventory.md`, OR amend an appendix `inventory-corrections-2026-06-04.md` for audit-traceability?

---

## §10 Footer

This migration plan translates [canonical-architecture-decisions.md](canonical-architecture-decisions.md) into a 8-PR sequenced pipeline. Effort estimates aggregate to ~9-13 weeks across 13 teams, dominated by cross-team cache adoption (PR #5) and the LATENT BUG remediation cycle (PR #1).

**Phase 4 unblocks** these PRs by owner sign-off on §9 open questions. After Phase 4, PR #1 + PR #2 + PR #3 + PR #4 ship in the first ~3-4 weeks (audit-team direct); PR #5 + PR #6 + PR #7 + PR #8 ship in phased ~6-9 weeks (per-team coordination).

**r3 unblock** via [`r3-scope-recommendations.md`](r3-scope-recommendations.md) — see specifically §3 Wave 2 reconciliation scope.

*Migration plan authored 2026-06-04 by Phase 3 Sub-Agent J synthesizing canonical-architecture-decisions.md (Sub-Agent I) + 4 wave summaries (W1+W2+W3+W4) + r3 project files.*
