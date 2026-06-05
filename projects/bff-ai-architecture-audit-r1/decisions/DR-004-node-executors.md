# DR-004 — Node Executors (Category 7)

> **Author**: Phase 3 Sub-Agent K (synthesis from Phase 2 outputs)
> **Date**: 2026-06-04
> **Status**: PROPOSED (pending Q-002 owner review)
> **Pinned to**: Phase 1 inventory commit `357e6936`
> **Source analysis**: [`notes/phase2/analysis-node-executors.md`](../notes/phase2/analysis-node-executors.md)
> **Canonical authority**: [`notes/canonical-architecture-decisions.md` §2.4](../notes/canonical-architecture-decisions.md) · §3 (W1 Cat 7 row) · §8.2 (W1-5, W1-6) · §8.3 (W1-13, W1-14)

## Context

Phase 1 inventory §2.7 catalogued 16 registered concrete node executors implementing `INodeExecutor` (interface at `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/INodeExecutor.cs`) — each executor implements a single `ActionType` enum member (with `SupportedActionTypes` returning a singleton set). Inventory surfaced two questions: (a) should this layer be consolidated behind a smaller abstraction, and (b) is the ActionType enum a central registry or scattered?

W1 Sub-Agent D applied empirical reproduction and corrected the inventory count: **18 executors** at HEAD (Foundry + Insights Engine blocks were undercounted by 2). Additionally, inventory §2.7 mislabelled the first 9 executors' ActionType labels as "(default)" — the audit confirmed all are **explicit numerics** (`AiAnalysis = 0`, `CreateTask = 20`, etc.).

The audit's key architectural insight: the `ActionType` enum at `INodeExecutor.cs:78-207` **IS the central registry** — compile-time defense via enum + runtime duplicate-detection at `NodeExecutorRegistry.cs:89`. No consolidation needed; the registry already exists at the appropriate layer.

The block organization (implicit but consistent) reveals an allocation pattern: 0-2 AI primitives, 10-12 reserved computation, 20-29 external integration, 30-39 control flow, 40-49 output, 50-59 notification/query, 60-69 Foundry, 70-149 Insights Engine. Parallel-project worktrees (e.g., the multiple in-flight Insights/Foundry feature branches) are at risk of ActionType collision unless allocation is governed.

A second architectural insight: `AgentServiceNodeExecutor.cs:198-212` catches `FeatureDisabledException` from injected `AgentServiceClient` and returns structured `NodeOutput.Error(... NODE_AGENT_FEATURE_DISABLED ...)`. This is the canonical reference for the **Runtime Kill-Switch Pattern** — distinct from ADR-030's DI Null-Object pattern. The pattern is undocumented; ADR-030 currently covers only DI-layer kill-switches.

A documentation correction surfaced: inventory §2.7 labelled `AgentServiceNodeExecutor` as "Singleton (kill-switched)" — the audit confirmed DI is **unconditional**; the kill-switch is **runtime-only** at the catch block.

## Decision

1. **KEEP all 18 executors** as-is. No consolidation. `INodeExecutor` interface + `ActionType` enum already provide the appropriate abstraction layer.

2. **DESIGNATE `ActionType` enum + `NodeExecutorRegistry` as canonical central registry**. Compile-time defense + runtime duplicate-detection is the load-bearing pattern. No new abstraction warranted.

3. **AUTHOR `ACTION-TYPE-REGISTRY.md` allocation contract** as a HIGH-priority deliverable (~XS effort). Block reservations + next-available + owner-per-block + deprecation policy. Preempts collision risk from parallel-project worktrees.

4. **DESIGNATE `AgentServiceNodeExecutor.cs:198-212` as canonical reference for Runtime Kill-Switch Pattern** — distinct from ADR-030 DI Null-Object. Surface as ADR-CAND-W1-6.

5. **KEEP per-executor single-element `SupportedActionTypes`** convention. Multi-action-type capability of the interface is unused in practice; codify the single-element convention as ADR-CAND-W1-13 (LOW priority simplification).

6. **CORRECT inventory documentation gaps** (count 16→18; ActionType label fixes; `AgentServiceNodeExecutor` lifetime/kill-switch framing) in inventory-correction PR (canonical §6 rows 7-9).

## Consequences

### Positive
- Zero code change; full KEEP verdict — minimal risk.
- `ACTION-TYPE-REGISTRY.md` is high-leverage, low-effort — preempts cross-project collision risk before it materializes (parallel worktrees for Foundry, Insights, future blocks).
- Runtime Kill-Switch Pattern codification gives `AgentServiceNodeExecutor` precedent a name + ADR-030 peer status, enabling future runtime-gated executors to follow the pattern explicitly.
- Inventory accuracy corrections close 3 framing gaps.

### Negative
- `ACTION-TYPE-REGISTRY.md` requires ongoing maintenance — owner-per-block accountability needed.
- ADR-030 will need amendment or peer ADR to distinguish DI Null-Object kill-switch from Runtime Kill-Switch — small documentation surface adjustment.

### Migration impact
- **Cross-team coordination**: All teams (registry doc) + Foundry (kill-switch precedent codification).
- **Effort estimate**: **XS (Extra-Small)** — documentation only. `ACTION-TYPE-REGISTRY.md` is a standalone PR.
- **Sequencing**: Independent of all other DRs. Can ship at any time.

## Canonical naming (Q-004 — surfaced not locked)

- **Candidate**: "Spaarke Node Executor Registry"
- **Reference impls**:
  - `INodeExecutor` interface at `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/INodeExecutor.cs`
  - `ActionType` enum at `INodeExecutor.cs:78-207` (the central registry)
  - `NodeExecutorRegistry` (runtime duplicate-detection at `:89`)
  - 18 confirmed executors at HEAD (corrected from inventory's 16)
- **Pattern elements (Node Executor)** (4):
  1. Single source of truth: `ActionType` enum is the central registry
  2. Block organization (implicit but consistent): 0-2 AI primitives / 10-12 reserved computation / 20-29 external integration / 30-39 control flow / 40-49 output / 50-59 notification/query / 60-69 Foundry / 70-149 Insights Engine
  3. Compile-time defense (enum) + runtime duplicate-detection (registry init)
  4. Per-executor single `SupportedActionTypes` element (multi-action-type capability of interface is unused)
- **Pattern elements (Runtime Kill-Switch — NEW)** (3):
  1. Real impl registered unconditionally (no DI gate)
  2. Injected dependency (e.g., `AgentServiceClient`) IS gate-aware — throws `FeatureDisabledException` when disabled
  3. Executor catches `FeatureDisabledException` and returns structured `NodeOutput.Error(...)` with stable error code (e.g., `NODE_AGENT_FEATURE_DISABLED`)
- **Companion artifact**: `ACTION-TYPE-REGISTRY.md` allocation contract (TBA — HIGH-priority deliverable)

## ADR candidates from this decision (Q-005 — bullets only)

- **W1-5** ActionType Central Registry + Allocation Contract — MEDIUM priority
- **W1-6** Runtime Kill-Switch Pattern (peer to ADR-030 DI Null-Object) — MEDIUM priority
- **W1-13** Simplify `SupportedActionType` to singular — LOW priority
- **W1-14** ActionType enum member lifecycle policy — LOW priority

## Open questions for owner review (Q-002)

1. **`ACTION-TYPE-REGISTRY.md` authorship** (canonical §11.6 Q-26): Standalone PR; who authors? Block reservations + next-available + owner-per-block + deprecation policy.
2. **Runtime Kill-Switch ADR**: Codify as peer to ADR-030 (separate ADR) or amend ADR-030 to cover both patterns?
3. **`SupportedActionTypes` simplification**: Refactor interface to `SupportedActionType` singular now (small touch across 18 executors), or document the convention without changing the interface?
4. **Block allocation governance**: Owner-per-block accountability — assign per-block owners (Foundry team owns 60-69; Insights team owns 70-149; etc.) or rotate?

## References

- Source analysis: [`notes/phase2/analysis-node-executors.md`](../notes/phase2/analysis-node-executors.md) §2-§3
- Wave summary: [`notes/phase2/wave-1-summary.md`](../notes/phase2/wave-1-summary.md) §3
- Canonical authority: [`notes/canonical-architecture-decisions.md`](../notes/canonical-architecture-decisions.md) §2.4 + §3 + §6 (inventory corrections rows 7-9) + §11.6 Q-26
- Related ADR candidates: W1-5 (MEDIUM), W1-6 (MEDIUM), W1-13/W1-14 (LOW)
- Related DRs: minimal cross-cutting — Node Executor layer is largely self-contained. Tangentially relates to DR-003 (some executors consume facades) and DR-008 (DI registration patterns for executors).
- ADR cross-references: ADR-010 (interface budget cap), ADR-030 (Null-Object Kill-Switch — runtime peer pattern)
- Inventory corrections from this category: §6 rows 7 (count 16→18), 8 (ActionType labels), 9 (`AgentServiceNodeExecutor` lifetime/kill-switch framing)
