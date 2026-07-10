# Spaarke AI Architecture Redesign R2 (Core) - AI Context

> **Purpose**: This file provides context for Claude Code when working on spaarke-ai-architecture-redesign-r2.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Planning → Ready for Tasks
- **Last Updated**: 2026-07-08
- **Current Task**: Not started
- **Next Action**: Run task-create to decompose plan into task files (task decomposition = pipeline Step 3)

---

## Quick Reference

### Key Files
- [`design.md`](design.md) - Charter (v0.4) — permanent reference
- [`spec.md`](spec.md) - 51 FRs, browser-UAT-gated
- [`plan.md`](plan.md) - WBS + Component Justification + discovered `file:line` anchors
- [`notes/d-f0-eval-family-spec.md`](notes/d-f0-eval-family-spec.md) - D-F0(e) resourcefulness eval family
- [`notes/policy-v2-origin-classification-decision-tree.md`](notes/policy-v2-origin-classification-decision-tree.md) - Policy v2 + E-1..E-6
- [`current-task.md`](current-task.md) - **Active task state** (context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) - Task tracker (created by task-create)

### Project Metadata
- **Project Name**: spaarke-ai-architecture-redesign-r2
- **Type**: BFF / AI-platform core (judgment + memory)
- **Complexity**: High

---

## Context Loading Rules

1. **Always load this file first** when starting work on any task
2. **Check current-task.md** for active work state (especially after compaction/new session)
3. **Reference spec.md + plan.md** for requirements, WBS, and the discovered `file:line` anchors
4. **Load the relevant task file** from `tasks/` based on current work
5. **Apply ADRs** relevant to the technologies used (loaded via adr-aware)

**Context Recovery**: If resuming work, see [Context Recovery Protocol](../../docs/procedures/context-recovery.md)

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

| User Says | Required Action |
|-----------|-----------------|
| "work on task X" | Execute task X via task-execute |
| "continue" / "keep going" / "next task" | Execute next pending task (check TASK-INDEX.md for next 🔲) |
| "continue with task X" / "resume task X" | Execute task X via task-execute |
| "pick up where we left off" | Load current-task.md, invoke task-execute |

task-execute ensures: knowledge/ADRs loaded, current-task.md tracked, checkpointing every 3 steps, quality gates (code-review + adr-check) at Step 9.5, recoverable progress.

### Parallel Task Execution
Tasks with satisfied dependencies + non-overlapping files → ONE message with MULTIPLE task-execute calls. **`.claude/` tasks are main-session-only (sub-agent write boundary)** — task-create marks them `parallel-safe: false`. MAX 6 agents per wave.

---

## Execution Model & Tiering (Sonnet-5 — CLAUDE.md §8.5)

- **Planning** (design-to-spec, project-pipeline Steps 0–3): Opus 4.8 / Fable 5.
- **Execution**: default **Sonnet 5 @ effort `high`**; per-POML `<model-tier>` + `<effort>`.
- **This project's tiering** (per plan §5): contracts (010–017), gate/completion/trace (030/032/035/036/038), memory/governance (050–058), ADRs (043/065) → **opus/fable**; catalog-row, test-repair, hygiene, docs → **sonnet**. `xhigh` reserved for brownfield/root-cause.
- FULL-rigor gates (code-review + adr-check) stay unconditional + coverage-first.

---

## Key Technical Constraints

- **Build ON ADR-039/040** — no second dispatch protocol, no parallel session cache, no routing outside Bindings.
- **Determinism for side effects; reads are free** (D-F0(b)); D-F0 never weakens a gate/hard block.
- **Store before render** (ADR-040).
- **Structured memory objects, not embeddings** — two scopes: **Record** (generic `(entityType,entityId)`) + **User** (per-user). **Generalize `MatterMemoryService`** (reuse code); **NEW container partitioned by SUBJECT** (`entityId`/`userId`, not `/tenantId`). `memory.write` is **AI-initiated + silent + provenance-tagged** (automatic memory; no write-gate; user review/delete is the control; hard-governance rules deferred).
- **Publish seams FIRST** (Phase A0) so Compose r2 is never blocked.
- **Triple-twin hoist (task 020) BEFORE any catalog-row task.**
- **Untrusted content can NEVER originate a memory write.**
- **BFF Hygiene (root CLAUDE.md §10)** — load `.claude/constraints/bff-extensions.md` before adding to `Sprk.Bff.Api`; publish-size ≤60 MB per-task; Placement Justification in PRs; use `PublicContracts/` facade.

---

## Decisions Made

- 2026-07-08 — Compose r1 kept separate (executed+closed, not absorbed); Compose r2 is a separate parallel project consuming core seams (already re-based). Core keeps FULL seam set. — operator
- 2026-07-08 — Daily Briefing remediation → separate project (not core Wave 0). — operator
- 2026-07-08 — Memory refined (operator review): TWO active scopes — **Record** (generic `(entityType,entityId)`, generalizing `MatterMemoryService` off `sprk_matter`) + **User** (general per-user, not per-matter). **Reuse the service CODE**, NEW Cosmos container **partitioned by SUBJECT** (`entityId`/`userId`, not `/tenantId` — dedicated-per-customer envs). Record memory holds derived knowledge (not Dataverse duplicates); positioned as future Insights-Engine durable store (wiring = follow-on). `memory.write` is **AI-initiated + silent + provenance-tagged** (automatic memory = the value prop; explicit-only floor REMOVED as over-engineered 2026-07-08; user review/delete + provenance + content-safety + scope-isolation are the interim controls); hard-governance rules (untrusted-origin ban, semantic trust boundary, litigation-hold, poisoning evals) **DEFERRED** to a separate project. — operator
- 2026-07-08 — Work IQ: provider interface in scope; researcher spike deferred. — operator
- 2026-07-09 — **Memory-wave rulings round 2 (operator)**: (f) **GOVERNANCE = MINIMAL at this stage** ("way over-emphasizing and over-complicating") — 052 rescoped: keep review/delete surface + record-auth-aligned read + GDPR erasure; retention = retentionClass→Cosmos `ttl` only; `sensitivity`/`deletionPolicy` = inert fields, NO enforcement machinery; point-delete only. Envelope FIELDS ride along (cheap, migration-free); BEHAVIOR only where load-bearing. (g) **CACHE STABILITY = don't build what won't be used** — 053 keeps free determinism (ordering, no timestamps in prefix, cheap byte-stability test as regression pinning); NO cache-key machinery/hit-rate plumbing (follow-on once cache metrics exist). (h) **NO mid-wave smoke gate** — keep progressing continuously; full-solution feedback at 069 UAT. — operator
- 2026-07-09 — **Memory-wave pre-flight rulings (operator, Fable architecture review)**: (a) new memory container = **PER-FACT documents** aligned to MemoryItem v1 (partition `/subjectId`, id = fact id; `tenantId` kept as plain metadata field) — legacy aggregate-doc-per-subject shape NOT carried forward; (b) migration = **fresh container, no doc migration, leave legacy docs** (non-destructive; purge = post-069 hygiene follow-up; legacy `memory` container is SHARED with pins + workspace tabs and must not be retired); (c) canonical `userId` for User-scope = Dataverse **`systemuserid`** (ADR-028 one-hop); (d) **preserve upsert-by-(Type,Key)-per-subject supersession** (memory hygiene under silent writes); (e) **task 053 converges the INTERACTIVE chat path onto the Binder in r2 — do not defer** (audit found 0 of 6 primitives folded; Binder serves only dispatch; Business-slice producer is net-new; OrchestratorPromptBuilder confirmed dead). POMLs 050/053/056/057/062/065 + spec FR-B-04/09/16 amended accordingly. — operator
- 2026-07-08 — **Tool-description source-of-truth = Model 1 (GitOps)** (task 020 escalation): the seed JSON (`infra/dataverse/sprk_analysistool-*-row.json` `sprk_description`) is THE authored source; the live Dataverse `sprk_analysistool.sprk_description` is a **managed mirror** (seed PATCHes-if-drift → JSON wins, so manual live edits silently revert on next deploy — a latent foot-gun). Live field made **managed/read-only** (form read-only where a form exists + health-check parity dimension flags drift + seed-managed doc). Code `Metadata.Description` (discovery-only, NOT LLM-read) kept in parity via **Option C validate-only** (KEEP-path contract test fails build on drift; NO codegen). `inputschemas/` + Binding-intent OUT of the hoist. Future catalog rows (memory.*, Compose's 5) author THROUGH the JSON source. Live prompt-tuning, if wanted later, = a deliberate "bootstrap-only seed + export-back" capability, not an editable field. — operator (Model 1 + Option C ratified)

---

## Implementation Notes

*No notes yet* — see plan.md §3 for the discovered `file:line` reuse map.

---

## Deferrals & Issues — tracking obligation

Track deferred work + issues in BOTH `notes/defer-issues.md` (source of truth) AND GitHub Issues via `/project-defer-issue-tracking` (`/defer`). CLAUDE.md §11 applies — every entry names a concrete failing behavior/contract. `push-to-github` blocks push on entries without GitHub URLs.

Named deferrals for close (090): Work IQ/Foundry IQ researcher spike + runtime providers; workspace-intelligence goal-tracking subsystem; admin observability dashboards; Spaarke-as-MCP-server outbound surface.

---

## Resources

### Applicable ADRs
039, 040 (binding), 013, 037, 015, 029, 032, 038; standing set 008/009-014/010/016/018/019/028/030/031/036. New candidates: **ADR-041** (Judgment/Confirmation/Completion), **ADR-042** (Memory Architecture/Governance).

### Related Projects
- `spaarkeai-compose-r2` — seam consumer (parallel; BFF+SpaarkeAi)
- `spaarke-ai-architecture-redesign-r1` — predecessor (ADR-039/040 shipped)
- Daily Briefing remediation — separate project (consumes GroundednessCheck threshold→action pattern)

### External Documentation
- `.claude/constraints/bff-extensions.md`, `.claude/constraints/azure-deployment.md`
- `docs/adr/ADR-039-*.md`, `docs/adr/ADR-040-session-ledger.md`
- `docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md`

---

*This file should be kept updated throughout project lifecycle*
