# BFF API Remediation & Publish Debt

> **Last Updated**: 2026-05-20
>
> **Status**: In Progress

## Overview

Remediation of the `Sprk.Bff.Api` deploy package (75.19 MB compressed / 212 MB uncompressed as of 2026-05-19) plus codification of guardrails so the debt does not return. Five outcomes execute across a 7-phase pipeline (Phase 0–6) with dev → demo → prod promotion gated by 24–48h observation windows.

## Quick Links

| Document | Description |
|----------|-------------|
| [Project Plan](./plan.md) | 7-phase implementation plan with WBS + discovered resources |
| [Design Spec](./design.md) | Full design document (594 lines) — decisions, ADRs, risk register |
| [AI Specification](./spec.md) | Machine-readable spec — FRs, NFRs, success criteria |
| [Approach (upstream)](./approach.md) | Original 2026-05-19 framing — preserved for context |
| [Extraction Assessment](./CC-PROMPT-bff-extraction-assessment.md) | Phase 0 input → produced [`docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`](../../docs/assessments/bff-ai-extraction-assessment-2026-05-20.md) |
| [Task Index](./tasks/TASK-INDEX.md) | Per-phase task tracker (created by task-create) |
| [AI Context](./CLAUDE.md) | Claude Code context file for this project |
| [Active Task](./current-task.md) | Active task state (for context recovery) |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Phase 0 (Pre-flight resolution) — pending |
| **Progress** | Scaffolding complete; awaiting Phase 0 sign-offs |
| **Target Date** | 4–6 weeks calendar (bake-window dominated) |
| **Completed Date** | — |
| **Owner** | Project owner (TBD per design §3) |

## Problem Statement

The BFF API (`Sprk.Bff.Api`) is the single backend for every Spaarke client surface (PCFs, Code Pages, External SPA, Office Add-ins, M365 Copilot plugin, Dataverse plugins). It hosts ~120 endpoints, ~99 DI registrations, ~13 background job types. By 2026-05-19 its deploy package grew from a ~60 MB baseline to 75.19 MB compressed / 212 MB uncompressed. Root causes: multi-platform native binaries shipped to a Linux App Service, sourcemaps in production, duplicate Cosmos `ServiceInterop.dll`, no CI guard against drift, and 20+ inbound CRUD→AI direct dependencies that violate clean-architecture boundaries (refined ADR-013).

Without remediation the debt rebuilds within 12–18 months and security-vulnerable transitives slip in unnoticed (already observed: NU1903 HIGH on `Microsoft.Kiota.Abstractions 1.21.2`).

## Solution Summary

Five outcomes execute in parallel where the dependency graph allows: (A) Size reduction via `linux-x64` framework-dependent publish + sourcemap exclusion + Cosmos native dedup; (B) Security hygiene via vuln-transitive triage; (C) CI guardrails with PR-label escape hatches; (D) Codified prevention via ADR-029, constraint + skill + FAILURE-MODES + CLAUDE.md updates; (E) Internal AI hygiene via `Services/Ai/PublicContracts/` facade + AI job handler relocation. Extraction of AI to a separate service is **explicitly out of scope** per the 2026-05-20 assessment and refined ADR-013.

## Graduation Criteria

The project is considered **complete** when:

**Outcome A — Size**
- [ ] `INVENTORY.md` and `CANDIDATES.md` committed and approved (SC-01)
- [ ] All approved SAFE candidates stable in dev 24–48h each (SC-02)
- [ ] Compressed package ≤60 MB OR documented gap (SC-03)
- [ ] Uncompressed publish ≤150 MB (SC-04)

**Outcome B — Security**
- [ ] Zero HIGH-severity CVEs in vulnerable-transitive scan (SC-05)
- [ ] Outdated transitives triaged with documented decisions (SC-06)
- [ ] Pre-release pinning rationale re-verified (SC-07)

**Outcome C — Operational**
- [ ] Zero new exception types in App Insights vs Phase 3 baseline (SC-08)
- [ ] Error rates within 10% of baseline (SC-09)
- [ ] P95 latency within 10% of baseline per endpoint (SC-10)

**Outcome D — Codification**
- [ ] Deploy-script size guard hard-fails by default (SC-11)
- [ ] CI guards: non-Linux RID, `*.js.map`, vulnerable-transitive HIGH (SC-12, SC-13, SC-14)
- [ ] `deploy-bff-api.yml` G-2 health-check + G-3 actions versions fixed (SC-15, SC-16)
- [ ] ADR-029 published — concise + full + indexed (SC-17)
- [ ] `.claude/constraints/azure-deployment.md` updated with Publish Hygiene (SC-18)
- [ ] `.claude/skills/bff-deploy/SKILL.md` updated with next-review-date (SC-19)
- [ ] `.claude/FAILURE-MODES.md` updated with bloat root cause + process pattern (SC-20)
- [ ] `LESSONS-LEARNED.md` committed (SC-21)

**Outcome E — Internal AI Hygiene**
- [ ] `Services/Ai/PublicContracts/` facade namespace created (SC-22)
- [ ] All inbound CRUD→AI direct dependencies migrated to facade (SC-23)
- [ ] 6 AI-coupled job handlers relocated to `Services/Ai/Jobs/` (SC-24)
- [ ] All tests pass; no behavioral regression (SC-25)
- [ ] BFF CLAUDE.md documents facade pattern + ADR-013 boundary (SC-26, SC-27)

## Scope

### In Scope

- Outcome A — Size: `linux-x64` framework-dependent publish; exclude `wwwroot/**/*.js.map`; eliminate duplicate Cosmos native DLLs
- Outcome B — Security: zero HIGH-severity CVEs in transitive graph; triage all outdated transitives
- Outcome C — CI guardrails: hard CI gates against non-Linux RIDs, sourcemaps, vulnerable transitives, oversize publish; PR-label escape hatches
- Outcome D — Codification: ADR-029, constraint + skill + FAILURE-MODES + BFF CLAUDE.md updates, GitHub workflow alignment (G-2, G-3)
- Outcome E — Internal AI Hygiene: `Services/Ai/PublicContracts/` facade introduction, CRUD→AI migration, 6 AI-coupled job handler relocation

### Out of Scope (binding)

- Refactoring BFF business logic beyond Outcome E facade migration
- New features of any kind
- `<PublishTrimmed>` or `<PublishAot>` (reflection-hostile to Graph SDK / Identity.Web / EF / DI / serializers)
- .NET SDK / target framework upgrade (8.0 → 9.0)
- Graph SDK / Kiota version changes (chain-locked)
- Pre-release package version changes (Azure.AI.Projects, Microsoft.Agents.AI, Azure.AI.OpenAI betas)
- ADR-010 DI minimalism violation fix (99+ vs ≤15) — separate project
- Infrastructure changes (App Service Plan SKU, region, runtime stack)
- **Extraction of AI subsystem to separate service** (governed by refined ADR-013; explicitly deferred)
- Wholesale audit of `Spaarke.Core` / `Spaarke.Dataverse` publish outputs (inventory only)
- Adding `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` — separate project

## Key Decisions

| Decision | Rationale | Reference |
|----------|-----------|-----------|
| Keep AI in BFF (no extraction) | Latency budgets (<50ms/<100ms/<500ms) + transactional Cosmos + 100% streaming AI per assessment | [`docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`](../../docs/assessments/bff-ai-extraction-assessment-2026-05-20.md), refined [ADR-013](../../.claude/adr/ADR-013-ai-architecture.md) |
| Framework-dependent `linux-x64` publish | App Service is unambiguously Linux; multi-platform RIDs are pure waste | [`spec.md`](spec.md) FR-A1, [design.md](design.md) §6 Phase 4 |
| `Services/Ai/PublicContracts/` facade with small focused interfaces | Mirrors ADR-007 SpeFileStore pattern; easier testing, lower coupling | [`design.md`](design.md) §3 (UQ-07 default) |
| Hard-fail size guard at baseline+10% with `-AllowOversize` escape | Forces explicit acknowledgment of regressions | [`spec.md`](spec.md) FR-C5 |
| Dev → demo → prod with 24–48h dev bake / 48h demo bake / 7-day prod observation | High blast radius requires high rigor | [`design.md`](design.md) §5 |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Reflection-loaded code breaks after package removal | High | Medium | Tiered risk + Phase 1 dynamic probe + 24–48h bake + App Insights monitoring; REJECT trimming/AOT |
| Insights Engine Phase 1 inflates baseline mid-project | Medium | Medium | Phase 0 coordination with Engine owner; capture baseline OUTSIDE integration window |
| Vuln transitive bump introduces behavioral change | Medium | Medium per package | Treat each as own Phase 4 candidate with full bake; do not batch |
| Production regresses despite full validation | Very high | Very low | Cumulative pre-tested changeset; rollback via `git revert` + `Deploy-BffApi.ps1`; dual approval |
| Sole-approver unavailable | Medium | Medium | Phase 0 names dual approver; approval can come from either |
| Scope creep ("upgrade .NET 9 while we're at it") | High | Medium | §Out of Scope is binding; additions become separate projects |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| Azure App Insights access | External | Available | Phase 3 baseline + Phase 4 observation |
| Azure App Service `spe-api-dev-67e2xz` | External | Available | Dev environment for Phase 1–4 |
| Azure App Service `spaarke-demo` | External | Available | Phase 5 demo bake |
| `sdap-bff-api-and-performance-enhancement-r1` | Internal | Active | Coordinate to avoid in-flight BFF deploy during Phase 4 (UQ-03) |
| `ai-spaarke-insights-engine-r1` | Internal | Pre-implementation | Capture baseline BEFORE integration starts OR after stable (UQ-04) |
| `actionlint` | External | Optional | For FR-D6 verification; GitHub-side action linter is acceptable fallback |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2026-05-19 | 0.1 | Initial approach.md framing | Project owner |
| 2026-05-20 | 0.2 | design.md revised after extraction assessment; Outcome E added | Multi-turn design conversation |
| 2026-05-20 | 0.3 | spec.md authored from design.md (308 lines) | design-to-spec |
| 2026-05-20 | 0.4 | Project scaffolding generated (README, plan, CLAUDE.md, ~55 tasks) | project-pipeline |
