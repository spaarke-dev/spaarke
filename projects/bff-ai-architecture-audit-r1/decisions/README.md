# Phase 3 Decision Records (DR-001 through DR-008)

> **Author**: Phase 3 Sub-Agent K (synthesis from Phase 2 outputs)
> **Date**: 2026-06-04
> **Status**: PROPOSED (pending Q-002 end-of-audit owner review)
> **Pinned to**: Phase 1 inventory commit `357e6936`

## Purpose

These 8 decision records package the audit's per-category verdicts in a structured DR format for end-of-audit owner review. Each DR distils the BINDING claims of Sub-Agent I's [`canonical-architecture-decisions.md`](../notes/canonical-architecture-decisions.md) into per-category Context / Decision / Consequences / References. The DRs are decision records, NOT full ADRs (Q-005 lock — ADR authoring DEFERRED to follow-on phase).

## Index

| DR # | Category | Verdict | Canonical reference impl (Q-004 surfaced) | Source analysis |
|---|---|---|---|---|
| [DR-001](DR-001-lookup-services.md) | Lookup services (Cat 2) | KEEP 1 + DELETE 3 orphans + REJECT generic | `PlaybookLookupService` | [`analysis-lookup.md`](../notes/phase2/analysis-lookup.md) |
| [DR-002](DR-002-cache-patterns.md) | Cache patterns (Cat 4) | KEEP 2 canonicals + ADOPT helper at 26+ sites + REJECT new abstraction | `EmbeddingCache` + `DistributedCacheExtensions.GetOrCreateAsync<T>` | [`analysis-cache.md`](../notes/phase2/analysis-cache.md) |
| [DR-003](DR-003-public-contracts-facade.md) | Public Contracts Facade (Cat 6) — **INCLUDES LATENT BUG** | KEEP 5 facades + ADD 4 Null peers + RESTRUCTURE 1 (Option A) | 5 facades + 5 Null peers post-remediation | [`analysis-public-contracts.md`](../notes/phase2/analysis-public-contracts.md) |
| [DR-004](DR-004-node-executors.md) | Node executors (Cat 7) | KEEP all 18 + AUTHOR allocation doc + DESIGNATE runtime kill-switch | `INodeExecutor` + `ActionType` enum + `AgentServiceNodeExecutor` | [`analysis-node-executors.md`](../notes/phase2/analysis-node-executors.md) |
| [DR-005](DR-005-intent-classifier.md) | Intent classifier (Cat 1) | KEEP 3 + DELETE 1 orphan cascade (~1280 LOC) + REJECT generic | `InsightsIntentClassifier` | [`analysis-intent-classification.md`](../notes/phase2/analysis-intent-classification.md) |
| [DR-006](DR-006-search-substrates.md) | Search substrates (Cat 3) | KEEP all 4 substrates + 3 DO-NOT-ADD-Null + REJECT merger | `RagService`/`NullRagService` (Double-Gate gold-standard) | [`analysis-search.md`](../notes/phase2/analysis-search.md) |
| [DR-007](DR-007-prompt-construction.md) | Prompt construction (Cat 5) | KEEP 6 + Option B EXTRACT-then-DELETE + REJECT generic | `OrchestratorPromptBuilder` + `CapabilityClassificationPromptBuilder` | [`analysis-prompts.md`](../notes/phase2/analysis-prompts.md) |
| [DR-008](DR-008-di-configuration.md) | DI + Configuration (W4) | KEEP 31 modules + 35 options + compound gate + ADD Endpoint↔DI Symmetry Rule + CONSOLIDATE `Options/`→`Configuration/` | `AiModule.cs:269-313` audit table + `AddPublicContractsFacade` post-remediation | [`analysis-di-configuration.md`](../notes/phase2/analysis-di-configuration.md) |

## How to consume

For Phase 4 end-of-audit owner review (per Q-002), read these DRs alongside:
1. [`canonical-architecture-decisions.md`](../notes/canonical-architecture-decisions.md) — Sub-Agent I synthesis (canonical authority)
2. [`migration-plan.md`](../notes/migration-plan.md) — Sub-Agent J sequencing + effort

The DRs are designed to be the most consumable artifact for owner adjudication on the 31 open questions packaged in canonical-architecture-decisions §11.

## Cross-reference map (between DRs)

- DR-001 ↔ DR-002 (lookup orphans participate in cache patterns)
- DR-001 ↔ DR-005 (cascade DELETE bundling)
- DR-002 ↔ DR-006 (search services share embedding-cache pattern)
- DR-003 ↔ DR-008 (LATENT BUG is at facade DI fascia layer)
- DR-005 ↔ DR-007 (`PlaybookBuilderSystemPrompt` cascade overlap)
- DR-006 ↔ DR-007 (RagService consumes prompt builders indirectly)
- DR-008 ↔ all (DI/Config touches every other category)
