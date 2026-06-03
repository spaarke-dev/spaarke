# Agent Framework Fit Assessment — R1

> **Status**: Project staged · 8 tasks pending · ready for execution via `task-execute`
> **Owner**: Ralph Schroeder
> **Branch**: `work/coding-knowledge-base-setup-r1` (or split to a dedicated worktree)
> **Created**: 2026-06-03

---

## Goal

Produce a single decision document — `docs/assessments/agent-framework-fit-assessment-YYYY-MM-DD.md` — that answers, for every current and likely-future Spaarke AI surface:

1. **Should Spaarke adopt Microsoft Agent Framework (`Microsoft.Agents.AI`) here?** Yes / No / Partial — with rationale
2. **Where is it a good fit, where is it not?** Decision criteria + per-surface analysis
3. **How should we deploy and surface agents** in surfaces where adoption is recommended? Keyed to ADR-013 + ADR-001 + BFF-extensions constraint

**Problem solved**: Spaarke is half-adopted — `Microsoft.Extensions.AI` primitives are in production (`SprkChatAgent` uses `IChatClient`, `AIFunction`) but `Microsoft.Agents.AI` proper is not. Without a decision document, every R-series project will re-litigate whether to adopt. The assessment locks the answer per surface so future work proceeds from a settled baseline.

This project **blocks** [`agent-framework-knowledge-r1`](../agent-framework-knowledge-r1/) — the curation project is parked until this assessment lands.

## Canonical plan

[`SPEC.md`](./SPEC.md) is the authoritative spec. This README is a status overview.

## Surfaces in scope (per user scoping decision 2026-06-03)

| # | Surface | Current state |
|---|---|---|
| S1 | SprkChat conversational agent | In production (raw IChatClient + custom middleware) |
| S2 | AnalysisOrchestration + JPS playbooks | In production (deterministic node graph) |
| S3 | Builder agent | In flight |
| S4 | Background AI jobs | In production (Service Bus) |
| S5 | Foundry Agent Service overlap | Curated topic, no Spaarke code yet |
| S6 | M365 Copilot / Declarative Agent surface | Active project |
| S7 | Insights Engine MCP server | Active project |
| S8 | Future / discovered surfaces | TBD (caught at task 001) |

## Execution approach

- **POML-decomposed** — 9 tasks across 7 phases (primary-source baseline → inventory → mapping → analysis → deployment → synthesis → review)
- **Standard `task-execute` per task**
- **Read-only on `src/`** — assessment cites code, doesn't change it
- **Primary-source-first** — task 000 captures live Microsoft Learn pages + GitHub HEAD + Devblog/Issues sweep with recency floor 2026-04-01. All downstream §4-§7 citations trace to this baseline, not the 2026-05-14 curated snapshot
- **Synthesis in task 006** — analysis tasks (000-005) write structured findings to project-local `notes/`; task 006 pulls them into the canonical assessment document with mandatory §10 Sources appendix
- **Adversarial review in task 007** — explicit guard against assessment-by-intuition; includes source recency re-check (top 5 URLs re-WebFetched at review time)

## Phase summary

| Phase | Tasks | Purpose |
|---|---|---|
| **0. Primary-source baseline** | 000 | Re-pull microsoft/agent-framework at HEAD; WebFetch live Learn pages; sweep Devblogs + GitHub Issues; recency floor 2026-04-01 |
| **1. Inventory current state** | 001, 002 | Read Spaarke AI code + non-BFF AI touchpoints; produce structured findings tables |
| **2. Agent Framework feature mapping** | 003 | Map Agents.AI + Workflows surface against Spaarke needs; **mandatory live-URL citations** |
| **3. Per-surface decision analysis** | 004 | Apply SPEC §4 criteria to each surface (S1-S8); per-surface decision matrix |
| **4. Deployment + migration** | 005 | For adopt-surfaces: deployment model + aggregated migration cost / risks |
| **5. Synthesis** | 006 | Write `docs/assessments/agent-framework-fit-assessment-YYYY-MM-DD.md` with §10 Sources appendix |
| **6. Review + sign-off** | 007, 008 | Adversarial review + source recency re-check + sign-off + unblock note for `agent-framework-knowledge-r1` |

Detail per task: [`TASK-INDEX.md`](./TASK-INDEX.md).

## Out of scope (per SPEC §3)

- No code changes to `src/`
- No ADR amendments (downstream decision after assessment lands)
- No refinements to `agent-framework-knowledge-r1` SPEC (downstream)
- No TCO / licensing analysis (only what affects fit decision)

## Risks tracked

| Risk | Mitigation |
|---|---|
| Assessment biases toward adopting Agent Framework | Task 007 mandates adversarial framing |
| Surfaces missed in scoping | Task 001 grep-driven inventory catches S8 |
| Assessment too abstract to guide future projects | Required citations to concrete `.cs` paths |
| ADR-013 amendment question deferred but never resolved | Section 9 "Forward-references" names it as an open item |
| Future Agent Framework versions invalidate conclusions | Assessment dated; monthly REFRESH catches drift |

## How to start work

```text
work on task 000
```

The harness will invoke `task-execute` with `tasks/000-refresh-primary-sources.poml`. Task 000 MUST land before any other task — all downstream tasks cite the baseline it produces. Per root CLAUDE.md §4, every task in this project MUST go through `task-execute` — do NOT read POML files directly.

## What lands when this completes

- `docs/assessments/agent-framework-fit-assessment-YYYY-MM-DD.md` — the canonical decision document
- A short unblock-recommendation note in `projects/agent-framework-knowledge-r1/` outlining what the assessment implies for that SPEC (but NOT editing the SPEC — per scoping decision)
- Updates to this project's TASK-INDEX, README status line, and a COMPLETION.md
