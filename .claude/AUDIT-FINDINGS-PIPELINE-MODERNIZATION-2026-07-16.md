# Project-Setup Pipeline — Modernization Audit & Recommendations

> **Date**: 2026-07-16 · **Trigger**: `spaarkeai-compose-r3` project-pipeline run hit stale POML template
> ([`projects/spaarkeai-compose-r3/notes/FINDING-poml-template-drift.md`](../projects/spaarkeai-compose-r3/notes/FINDING-poml-template-drift.md)).
> **Scope reviewed**: `design-to-spec`, `project-pipeline`, `task-create`, `task-execute`, `project-setup`,
> `task-execution.template.md`, `code-review`, `CROSS-REFERENCE-MAP.md` — plus latest Claude Code best practices.
> **Status of Part 1 (drift fixes)**: ✅ APPLIED (this sweep). **Part 2 (larger improvements)**: 📋 RECOMMENDED — separate decision.

---

## Part 1 — Drift fixes applied (done)

| # | Fix | Files |
|---|---|---|
| 1 | **Template demoted to lean pointer** (v3.0). The v2.0/Dec-2025 fossil (missing every modern field; dead paths `docs/projects/`, `Spe.Bff.Api`, `docs/reference/adr/`, `docs/ai-knowledge/`) → a lean pointer with a current copy-paste skeleton + a field-semantics table pointing at task-create as the single source of truth. | `.claude/templates/task-execution.template.md` |
| 2 | **Canonical field set reconciled.** `<rigor>` (was `<rigor-hint>`), `<deps>` (was metadata `<dependencies>`), `<gate>` added; `<blocks>` dropped (DAG lives in TASK-INDEX). Deprecated aliases accepted for back-compat. | `task-create` (Step 4, Step 3.5.5, POML Tag Requirements) |
| 3 | **Template pointers repointed** to say "skeleton (pointer); authoritative source = task-create". | `task-create` L44/L894, `project-pipeline` Step 3, `CROSS-REFERENCE-MAP.md` |
| 4 | **Completeness lint** (finding rec C): task-create Validation Checklist step + `code-review` Step 6.7 + `scripts/Validate-TaskPoml.ps1` (regex-based — tolerant of imperfect XML in POML prose). Catches POMLs missing `<model-tier>`/`<effort>`/`<rigor>`/`<parallel-*>`/`<steps mode>`/`<justification>`. | `task-create`, `code-review`, `scripts/` |
| 5 | **project-pipeline producer/consumer gap** closed: Step 3's POML-generation field list now emits the full canonical set (model-tier/effort/step-mode/escalation/justification/parallel-*) that Step 5 dispatch + `/goal` consume. Added §10 Placement-Justification + §11 prompts. | `project-pipeline` Step 3 |
| 6 | **design-to-spec**: broken §13→§15 cross-ref fixed; mojibake fixed; added §10 `<hot-path-declaration>` + §11 component-justification seeding to the spec template (design-doc ingestion point). | `design-to-spec` |
| 7 | **task-execute**: dead `src/server/api/CLAUDE.md` → `…/Sprk.Bff.Api/CLAUDE.md`; `npm run build`→`build:prod` for PCF (AP-1); retired `Task`/`TaskOutput` tool names → `Agent`; rigor tree now reads the authored `<rigor>` hint; BFF checklist gained §10 publish-size (≤60 MB) + CVE + Placement-Justification; `projects/INDEX.md` maintenance noted at Step 0.5. | `task-execute` |
| 8 | **Structured outputs adopted** (partial, best-practice): Step 2 discovery + Step 5 task-outcome now specify machine-readable schemas for subagent returns. | `project-pipeline` |
| 9 | **Stale env/model cruft**: `MAX_THINKING_TOKENS` self-contradiction removed; planning tier updated to Opus 4.8 / Fable 5. | `project-pipeline`, `design-to-spec` |

**Not changed (correct as-is)**: `/goal` wave-loop (present + current — per-wave, ≥3 tasks, transcript-only evaluator, NOT a quality gate); §6.5 ADR-Tensions enforcement; `project-setup` (essentially current).

---

## Part 2 — Recommendations (separate decision, NOT yet applied)

These come from a mid-2026 Claude Code best-practices review. **Caveat**: several were sourced partly from
third-party blogs, not only official Anthropic docs — verify against `code.claude.com/docs` before acting. Treat
as candidates, not settled direction.

### R1 — Skill-size / progressive disclosure (MEDIUM)
Current guidance favors skill bodies < ~500 lines with detail in `references/`. The setup skills are large:
`task-execute` ~1,220 · `task-create` ~960 · `project-pipeline` ~965 · `design-to-spec` ~800. **BUT** `ai-procedure-quality-r1`
already made a documented `leave-alone-justified` call on `task-create`/`project-pipeline` length (step numbers are
external API; dereference-reliability concern). Any split must preserve the cited step-number contract. Low urgency.

### R2 — Dynamic Workflows for orchestration (MEDIUM–HIGH, evaluate)
The `Workflow` tool (deterministic JS orchestration, structured `schema` returns, pipeline/parallel primitives) is a
strong fit for `project-pipeline` Step 5 (wave dispatch) and Step 2 (discovery fan-out) — it would replace manual
multi-Agent fan-out with a resumable, schema-validated script. Keep the read-audit-apply pattern for decision-heavy
work. Recommend a scoped pilot on one wave dispatch before broad adoption. (The structured-output schemas added in
Part 1 #8 are the on-ramp.)

### R3 — POML → native task tracking bridge (LOW, optional)
POML is a Microsoft format, not Anthropic-mandated. It is deeply embedded here (50+ task projects) and works well as
the **design artifact**. A "bridge" (POML stays the spec; execution *state* tracked in a native task system) is the
low-risk path IF a native system is confirmed available — do NOT rip out POML. The best-practices report's claim that
a native Task system "replaced TodoWrite" is **unverified** (this environment still exposes `TodoWrite`, not
`TaskCreate`). Park until confirmed against official docs.

### R4 — `/rewind` granular checkpointing (LOW)
Complements existing §5 checkpointing; document as an option in `context-handoff` for granular rollback vs full compact.

### R5 — Finish structured-outputs rollout (LOW)
Part 1 #8 added schemas at two points; extend to any other subagent-return site if R2 is not adopted wholesale.

---

*Authored by the 2026-07-16 pipeline-modernization sweep (main session; sub-agents audited read-only per CLAUDE.md §3).*
