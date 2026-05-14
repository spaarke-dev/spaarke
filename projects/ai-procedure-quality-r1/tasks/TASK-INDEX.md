# Task Index — AI Procedure Quality R1

> **Project**: ai-procedure-quality-r1
> **Last Updated**: 2026-05-14
> **Total tasks**: 25 (24 implementation + 1 wrap-up)
> **Status legend**: 🔲 pending · ⏳ in-progress · ✅ complete · 🔄 needs retry · ❌ abandoned

## Task Registry

### Phase 0 — Inventory + Baseline

| ID | Title | Status | Parallel Group | Dependencies | Spec |
|---|---|---|---|---|---|
| 001 | Inventory all 49 skills under `.claude/skills/` | 🔲 | 0-A | none | F-1 |
| 002 | Inventory CLAUDE.md (1190 lines, section breakdown) | 🔲 | 0-A | none | F-2 |
| 003 | Inventory all `.github/workflows/*.yml` (status, triggers, action versions) | 🔲 | 0-A | none | F-16 |
| 004 | Inventory `.claude/settings.json` + `.mcp.json` against published schemas | 🔲 | 0-A | none | F-7 |
| 005 | Baseline PCF bundle.js sizes for bundle-size guard | 🔲 | 0-B | 001 | F-10 |
| 006 | Build skill cross-reference map (7 surfaces) | 🔲 | 0-B | 001, 002, 003, 004 | F-15.2 |

### Phase 1 — Additive Infrastructure

| ID | Title | Status | Parallel Group | Dependencies | Spec |
|---|---|---|---|---|---|
| 010 | Create `.claude/agents/researcher.md` subagent | 🔲 | 1-A | none | F-5 |
| 011 | Create `.claude/skills/_template/SKILL.md` canonical scaffold | 🔲 | 1-A | none | F-6 |
| 012 | Create `.claude/CHANGELOG.md` (forward-only) | 🔲 | 1-A | none | F-11 |
| 013 | Create `.claude/FAILURE-MODES.md` with 4 inaugural entries | 🔲 | 1-A | none | F-12 |
| 014 | Establish `.claude/archive/` convention | 🔲 | 1-A | none | NF-1 |

### Phase 2a — Skills Audit

| ID | Title | Status | Parallel Group | Dependencies | Spec |
|---|---|---|---|---|---|
| 020 | Audit skills batch 1 (skills 1–6 alphabetically) | 🔲 | 2a-W1 | 001, 011 | F-1 |
| 021 | Audit skills batch 2 (skills 7–12) | 🔲 | 2a-W2 | 001, 011 | F-1 |
| 022 | Audit skills batch 3 (skills 13–18) | 🔲 | 2a-W3 | 001, 011 | F-1 |
| 023 | Audit skills batch 4 (skills 19–24) | 🔲 | 2a-W4 | 001, 011 | F-1 |
| 024 | Audit skills batch 5 (skills 25–30) | 🔲 | 2a-W5 | 001, 011 | F-1 |
| 025 | Audit skills batch 6 (skills 31–36) | 🔲 | 2a-W6 | 001, 011 | F-1 |
| 026 | Audit skills batch 7 (skills 37–42) | 🔲 | 2a-W7 | 001, 011 | F-1 |
| 027 | Audit skills batch 8 (skills 43–49) | 🔲 | 2a-W8 | 001, 011 | F-1 |
| 028 | Consolidate batch findings into `.claude/AUDIT-FINDINGS-SKILLS.md` | 🔲 | serial | 020–027 | F-1 |

**🚪 Human Gate 1** — Reviewer signs off on `.claude/AUDIT-FINDINGS-SKILLS.md` before Phase 2b.

### Phase 2b — Skills Refinement

| ID | Title | Status | Parallel Group | Dependencies | Spec |
|---|---|---|---|---|---|
| 030 | Apply approved skill refinements (parallel-safe: false — main session only) | 🔲 | serial | Human-Gate-1 | F-3 |

### Phase 3a — CLAUDE.md Audit

| ID | Title | Status | Parallel Group | Dependencies | Spec |
|---|---|---|---|---|---|
| 040 | Audit `CLAUDE.md` → `.claude/AUDIT-FINDINGS-CLAUDEMD.md` with target outline | 🔲 | serial | 002, 028 | F-2 |

**🚪 Human Gate 2** — Reviewer signs off on target outline in `.claude/AUDIT-FINDINGS-CLAUDEMD.md` before Phase 3b.

### Phase 3b — CLAUDE.md Rewrite

| ID | Title | Status | Parallel Group | Dependencies | Spec |
|---|---|---|---|---|---|
| 050 | Rewrite CLAUDE.md per approved outline (<200 lines); move extracted content; archive original | 🔲 | serial | Human-Gate-2 | F-4 |

### Phase 4a — `.claude/` Standing Infrastructure

| ID | Title | Status | Parallel Group | Dependencies | Spec |
|---|---|---|---|---|---|
| 060 | Build `Validate-ClaudeSettings.ps1` schema validator | 🔲 | 4a-A | 004 | F-7 |
| 061 | Build `Find-PatternDrift.ps1` drift detector | 🔲 | 4a-A | 001 | F-8 |
| 062 | Build `Test-ReferenceExemplars.ps1` exemplar harness | 🔲 | 4a-A | 005, 028 | F-9, F-15 |
| 063 | Build `Check-BundleSizeDrift.ps1` bundle-size guard | 🔲 | 4a-A | 005 | F-10 |
| 064 | `.github/workflows/procedure-quality.yml` orchestrating validators (<30s) | 🔲 | 4a-B | 060–063, 065, 066 | F-7 |
| 065 | Build `Validate-SkillReferences.ps1` (Light check across all skills) | 🔲 | 4a-A | 001, 006 | F-15.4 |
| 066 | Build `Find-SkillReferenceDrift.ps1` (7-surface drift detector) | 🔲 | 4a-A | 006 | F-15.4 |

### Phase 4b — GitHub Actions Hardening

| ID | Title | Status | Parallel Group | Dependencies | Spec |
|---|---|---|---|---|---|
| 070 | Diagnose & fix 3 failing workflows (sdap-ci, deploy-promote, deploy-infrastructure) | 🔲 | 4b-A | 003 | F-16 |
| 071 | Add `actionlint` pre-commit hook + GHA workflow | 🔲 | 4b-A | 003 | F-17 |
| 072 | Pin all `.github/workflows/**` actions to commit SHAs | 🔲 | 4b-B | 070, 071 | F-20 |
| 073 | Write `docs/procedures/github-notification-routing.md` | 🔲 | 4b-A | none | F-18 |
| 074 | Update `.github/dependabot.yml` for github-actions | 🔲 | 4b-A | none | F-20 |
| 075 | Audit + align required status checks with passing workflows | 🔲 | 4b-A | 070 | F-19 |

### Phase 5 — Recurring Cadence

| ID | Title | Status | Parallel Group | Dependencies | Spec |
|---|---|---|---|---|---|
| 080 | Create `.claude/skills/procedure-quality-audit/SKILL.md` | 🔲 | serial | 060–064, 011 | F-13 |
| 081 | Build `scripts/quality/Run-ProcedureQualityAudit.ps1` orchestrator | 🔲 | serial | 080 | F-13 |
| 082 | Schedule monthly run via `scripts/quality/Schedule-MonthlyQualityAudit.ps1` | 🔲 | serial | 081 | F-14 |
| 083 | Write `docs/procedures/procedure-quality-cadence.md` | 🔲 | serial | 082 | F-14 |
| 084 | Run inaugural audit; produce first `.claude/QUALITY-AUDIT-<date>.md` | 🔲 | serial | 083 | success criterion |

### Wrap-up

| ID | Title | Status | Parallel Group | Dependencies | Spec |
|---|---|---|---|---|---|
| 090 | Project wrap-up — update README status, write lessons-learned, prompt for `/merge-to-master` | 🔲 | serial | 084 | — |

---

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|---|---|---|---|
| **0-A** | 001, 002, 003, 004 | none | 4 independent inventories — read-only, fully parallel |
| **0-B** | 005 | 001 complete | Baseline depends on the skill inventory |
| **1-A** | 010, 011, 012, 013, 014 | none | 5 independent additive artifacts — fully parallel |
| **2a-W1..W8** | 020–027 (one wave each) | 001, 011 complete | 8 waves of 6 skills each. Each wave: dispatch all 6 skills as parallel agents. ⚠️ Sub-agents are read-only on `.claude/`; they return findings as structured output. Main session writes batch findings. |
| **2a-serial** | 028 | 020–027 all complete | Main session consolidates per-batch findings |
| **3-serial** | 040, 050 | various | Single file, serial only |
| **4a-A** | 060, 061, 062, 063 | various | 4 validators, fully parallel |
| **4a-B** | 064 | 4a-A complete | Single workflow file, serial |
| **4b-A** | 070, 071, 073, 074, 075 | 003 complete (for some) | 5 independent streams, fully parallel |
| **4b-B** | 072 | 4b-A complete | SHA pinning touches every workflow file; serial to avoid conflicts |
| **5-serial** | 080–084 | various | Sequential dependency chain |

## Critical Path

```
Phase 0 (parallel, ~1h wall-clock) ─┐
                                    ├─→ Phase 1 (parallel, ~30min) ─→ Phase 2a (8 waves × 15min ≈ 2h) ─→ 🚪 Gate 1 ─→ Phase 2b (~2h)
                                    │                                                                                          │
                                    │                                                                                          ▼
                                    └─→ Phase 4a (parallel, ~1h) ─┐                                                Phase 3a (~30min) ─→ 🚪 Gate 2 ─→ Phase 3b (~1h)
                                                                  ├─→ Phase 5 (~1h) ─→ Wrap-up (~15min)                                                            │
                                    └─→ Phase 4b (parallel + serial, ~1.5h) ─┘                                                                                     │
                                                                                                                                                                   ▼
                                                                                                                                                              (Phase 5 + Wrap-up after Gate 2)
```

**Critical path duration**: Phase 0 → 1 → 2a → Gate1 (human) → 2b → 3a → Gate2 (human) → 3b → 5 → Wrap-up. With both human gates being 1-2 hours each, total wall-clock estimate is ~12-15 agent-hours + 3-4 human-review-hours.

## High-Risk Items

1. **Task 070 — Diagnose 3 failing workflows**. Unknown root cause; could surface complex permission/secrets issues. Estimate uncertainty is high.
2. **Task 050 — CLAUDE.md rewrite**. Touches a file loaded into every future session. Verification is the hardest part.
3. **Task 030 — Skill refinements**. 49 skills audited; the refinement set could be large. Serial execution may add wall-clock time vs the audit's parallel speed.

## Notes on `parallel-safe: false`

Tasks that modify `.claude/` paths are marked `parallel-safe: false` because sub-agents cannot write there. Main session picks them up. This is intentional per the root CLAUDE.md "Sub-Agent Write Boundary" section. The skill audits (Phase 2a) work around this: sub-agents READ skills in parallel, return findings as structured text, and the main session WRITES `AUDIT-FINDINGS-SKILLS.md`.
