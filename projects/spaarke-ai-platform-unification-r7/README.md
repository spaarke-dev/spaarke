# Spaarke AI Platform Unification R7

> **Portfolio**: [Project #501](https://github.com/spaarke-dev/spaarke/issues/501) — Parent Epic [#421 SPAARKE AI](https://github.com/spaarke-dev/spaarke/issues/421) · Board [Project #2](https://github.com/users/spaarke-dev/projects/2)
> **Project Type**: AI
> **Worktree**: `C:/code_files/spaarke-wt-spaarke-ai-platform-unification-r7`
> **Branch**: `work/spaarke-ai-platform-unification-r7`
> **Status**: In Progress
> **Last Updated**: 2026-06-28
> **Owner**: ralph.schroeder@hotmail.com

## Overview

R7 collapses Spaarke's playbook dispatch model from three storage layers (C# enum, Action lookup table, plain INT column) into a single typed `sprk_executortype` Choice column on `sprk_playbooknode`. It builds the missing `AiCompletionNodeExecutor` to close R4's `/narrate` end-to-end, promotes `sprk_playbookconsumer` to first-class consumer-driven authoring, adds typed config schemas per executor, updates the Playbook Builder UI, and migrates all 94 existing playbook nodes in spaarkedev1.

## Quick Links

| Document | Description |
|---|---|
| [spec.md](./spec.md) | AI-optimized specification (438 lines, 33 FRs) |
| [design.md](./design.md) | Human design document (v0.6, 552 lines) |
| [plan.md](./plan.md) | Implementation plan + WBS + risk register |
| [CLAUDE.md](./CLAUDE.md) | AI context file for this project |
| [tasks/TASK-INDEX.md](./tasks/TASK-INDEX.md) | Task breakdown + parallel groups + dependencies |
| [current-task.md](./current-task.md) | Active task state (context recovery) |

## Problem Statement

Spaarke AI's dispatch model is split across too many surfaces with no canonical contract. Makers, deploy scripts, and runtime drift apart. Every release introduces another version of the same class of bug. Symptoms surfaced in R4 UAT: `/narrate` 503s caused by structural fallback routing; missing `AiCompletionNodeExecutor` for prompt-only LLM calls; schema introspection revealed three storage layers for "what executor handles this Action" with none enforced to agree.

## Solution Summary

Four invariants collapse the dispatch model:
1. **Playbook is the only AI invocation model** — every call goes through `PlaybookOrchestrationService.ExecuteAsync`
2. **Executor dispatch lives on the node**, not the Action (single typed Choice column `sprk_executortype`)
3. **Consumer-driven authoring** — `sprk_playbookconsumer` becomes first-class; no consumer = dead playbook
4. **Typed config schemas per executor** — each `INodeExecutor` declares its config shape; PlaybookBuilder renders typed forms

## Graduation Criteria

Project complete when:

- [ ] Every `sprk_playbooknode` row in spaarkedev1 has `sprk_executortype` populated (FR-19)
- [ ] `PlaybookOrchestrationService.ExecuteNodeAsync` reads dispatch from `node.sprk_executortype` only (FR-07)
- [ ] No remaining callers of legacy `ExecuteAnalysisAsync` (FR-11)
- [ ] `AiCompletionNodeExecutor` exists, is registered, handles prompt-only LLM calls (FR-12)
- [ ] `/narrate` works end-to-end through `DAILY-BRIEFING-NARRATE` (R4 graduation gate, FR-15)
- [ ] PlaybookBuilder canvas shows Executor Type selector with 33 values + tier grouping (FR-22)
- [ ] PlaybookBuilder renders typed config forms for ≥5 executors (FR-23)
- [ ] R4 canonical-truth docs reviewed; outdated sections DELETED, current sections UPDATED (FR-28)
- [ ] jps-* skills aligned with new node-first dispatch model (FR-32)
- [ ] `Deploy-Playbook.ps1` writes executor type per node, no name-detection hacks (FR-20)
- [ ] `docs/guides/ai-guide-consumer-wiring.md` exists, covers 6 consumers + chat-summarize case (FR-31)
- [ ] `chat-summarize` consumer routes through `IConsumerRoutingService` Path A.5 (FR-17)
- [ ] Playbook Library wired into ≥3 consumer surfaces (FR-18)
- [ ] BFF publish size ≤ +2 MB (NFR-01), no new HIGH-severity CVE (NFR-02)
- [ ] Test coverage >85% for new code (NFR-05)

See [spec.md §Success Criteria](./spec.md) for the full enumeration with verification steps.

## Scope

### In Scope

- Dispatch reform (`sprk_executortype` Choice column on `sprk_playbooknode` — schema DONE, code/UI cleanup REMAINING)
- `AiCompletionNodeExecutor` build (closes R4 `/narrate` end-to-end)
- Typed config schemas per executor (Invariant 5)
- Consumer-driven model promotion (`sprk_playbookconsumer` first-class; chat-summarize migration)
- Playbook Builder Code Page updates (Executor Type selector, typed config forms, Action tab)
- Schema migration (drop `sprk_actiontypeid`, `sprk_executoractiontype`; keep `sprk_analysisactiontype` decorative)
- Existing-playbook backfill (94 nodes in spaarkedev1, manual per-node review by owner)
- Documentation cleanup (DELETE outdated, UPDATE in place, CREATE consumer-wiring guide)
- Skill updates (jps-action-create, jps-playbook-design, jps-playbook-audit, jps-validate, jps-scope-refresh)

### Out of Scope

- Action Engine R1 territory (Spaarke Claw, Tool Registry classification, gate resolvers, three meta-tools, Action Templates, agent UX)
- Polished maker UX (mega menu, AI-assisted authoring copilot, templates browser)
- Multi-tenant rollout (R7 ships to spaarkedev1 only)
- Backward-compat shims (big-bang cutover per Q6 RESOLVED)
- Updates to `docs/architecture/ai-architecture-consumer-routing.md` (chat-routing-redesign-r1 owns)

## Key Decisions

| Decision | Rationale | Reference |
|---|---|---|
| Big-bang migration, no transition mode | Simpler dispatch + tests + docs (Q6) | spec §Owner Clarifications |
| Per-node prompt overrides KEPT | Personalization layer (Q2) | spec FR-25 |
| Lookup table REPURPOSED (not dropped) | Decorative maker categorization (Q4) | spec FR-05 |
| Manual per-node review of 94 nodes | Small scale; owner-controlled (2026-06-28) | spec FR-19 |
| Full C# enum rename `ActionType` → `ExecutorType` | Lift naming confusion (Q17) | spec FR-10 |
| Hot-path declaration: BFF=Y, SpaarkeAi=N, ci-workflows=N, skill-directives=Y, root-CLAUDE.md=N | Spec affected-areas analysis | [design.md](./design.md) top |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|---|---|---|---|
| Migration regresses Insights pipeline | High | Med | FR-19 manual review + dry-run mode; Wave 5 regression sweep |
| chat-summarize migration regresses chat | Med | Low | Smoke-test chat first; FR-17 integration test |
| Maker-facing doc rewrite is large | Med | Med | Wave 6 deliverables tracked; PR review gates drift |
| C# rename creates large diff / merge conflict | Med | High | Hold Action Engine R1 + R4; schedule rename early in Wave 2 |
| R4 docs already shipped to master become tech debt | Med | High | FR-28 explicitly DELETES outdated sections |
| BFF publish size grows beyond NFR-01 budget | Med | Low | Per-task publish-size check; net should be negative (deletions > additions) |

## Dependencies

| Dependency | Type | Status | Notes |
|---|---|---|---|
| `sprk_executortype` Choice column on `sprk_playbooknode` | Internal/Schema | ✅ Ready | Owner DONE 2026-06-27 |
| `sprk_nodetype` column removal | Internal/Schema | ✅ Ready | Owner DONE 2026-06-28 |
| Global Choice set `sprk_playbookexecutortype` (33 values) | Internal/Schema | ✅ Ready | Owner DONE 2026-06-27 |
| R4 `spaarke-daily-update-service-r4` hold-open | Sibling project | ✅ Aligned | Per R4 graduation decision 2026-06-28 |
| Action Engine R1 hold at Phase 0 | Sibling project | ✅ Aligned | Per Q14 confirmation 2026-06-28 |
| `docs/architecture/ai-architecture-consumer-routing.md` | External owner | Reference only | chat-routing-redesign-r1 owns updates |

## Team

| Role | Name | Responsibilities |
|---|---|---|
| Owner | ralph.schroeder@hotmail.com | Overall accountability, owner-review for backfill (FR-19), UAT sign-off |
| AI implementer | Claude Code | Wave execution per task POMLs, code-review at Step 9.5 of each FULL-rigor task |

## Changelog

| Date | Version | Change | Author |
|---|---|---|---|
| 2026-06-28 | 1.0 | Initial pipeline init (registered #501, design.md hot-path declared, foundation artifacts generated) | Claude Code |
