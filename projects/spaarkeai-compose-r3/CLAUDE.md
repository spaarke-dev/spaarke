# Spaarke Compose R3 — AI Context

> **Purpose**: Context for Claude Code when working on `spaarkeai-compose-r3`.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Pipeline-initialized — ~28 tasks generated; ready to execute.
- **Last Updated**: 2026-07-16
- **Current Task**: Not started (see [current-task.md](current-task.md)).
- **Next Action**: ✅ `spaarkeai-compose-r2` confirmed completed/closed + on master (2026-07-16) — E1-cutover gate cleared. Execute Phase 0 (task 001). See [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md).

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) — AI implementation spec (FR-01→26, NFR-01→10) — **source of truth**.
- [`design.md`](design.md) — code-grounded design + July-2026 research + 6 passed spikes.
- [`plan.md`](plan.md) — phase/track WBS + critical path + parallel groups.
- [`README.md`](README.md) — overview + G-R3 graduation criteria.
- [`ooxml-fidelity-findings.md`](ooxml-fidelity-findings.md) — E1/E2/E3 verdicts.
- [`notes/seed-README.md`](notes/seed-README.md) — original seed (Scope Areas A–J; superseded by spec).
- [`current-task.md`](current-task.md) — **active task state** (context recovery).
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — task tracker.

### Project Metadata
- **Project Name**: spaarkeai-compose-r3
- **Type**: BFF (`Sprk.Bff.Api/Services/Compose`) + client shared lib (`Spaarke.Compose.Components`); NO Dataverse catalog change (engine frozen).
- **Complexity**: High (OOXML delta save, new NuGet, paraId substrate, import round-trip; brownfield extension of R2).
- **Hot-path**: BFF=Y · SpaarkeAi=Y · ci-workflows=N · skill-directives=N · root-CLAUDE=N

---

## Context Loading Rules

1. **Load this file first** when starting any task.
2. **Check current-task.md** for active work state (especially after compaction/new session).
3. **Reference spec.md** for FRs/NFRs, acceptance criteria, ADR Tensions, owner clarifications.
4. **Load the relevant task POML** from `tasks/`.
5. **Apply ADRs** via adr-aware.

**Before ANY task that adds to `Sprk.Bff.Api`**: load [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) (§10 BFF hygiene) and state the **Placement Justification** + report publish-size delta vs ~49.63 MB baseline (ceiling 60 MB).

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

| User Says | Required Action |
|-----------|-----------------|
| "work on task X" | Execute task X via task-execute |
| "continue" / "keep going" / "next task" | Execute next pending 🔲 in TASK-INDEX.md via task-execute |
| "continue with task X" / "resume task X" | Execute task X via task-execute |
| "pick up where we left off" | Load current-task.md, invoke task-execute |

---

## Project-Specific Binding Rules

These are the R3 MUST/MUST-NOT rules (from spec §Technical Constraints + design §13). Every task inherits them:

- ✅ **MUST** derive dirty-save output from the retained load-time original (FR-01); **MUST NOT** reconstruct the whole doc from TipTap (`docx.js` dropped from export).
- ✅ **MUST** exclude SkiaSharp assets when adding Docxodus; **MUST NOT** invoke Docxodus `HtmlToWml` / `FormattingAssembler` (re-pulls SkiaSharp).
- ✅ **MUST** anchor primarily by `w14:paraId`; **MUST** retain `AnnotationReanchorService` fuzzy match as the cross-Word-session fallback (Word regenerates paraIds on external edits).
- ✅ **MUST** derive the E3 confidence band **client-side at render** from grounding evidence + live-doc resolvability (§6.5 Path B, 2026-07-18 — supersedes "server-side"; the band/offsets are client-derived VIEWS of the opaque payload, keeping the AI ledger path envelope-only per ADR-013/040 — see [`docs/architecture/COMPOSE-REDLINE-DERIVED-VIEWS.md`](../../docs/architecture/COMPOSE-REDLINE-DERIVED-VIEWS.md)); **MUST NOT** emit a false-precision numeric self-report, treat it as a model self-report, or auto-accept low-confidence edits.
- ❌ **MUST NOT** add any TipTap product feature (paid or unpaid) or any AGPL code. MIT base + `@tiptap/extension-*` only (verify never `@tiptap-pro/*`).
- ❌ **MUST NOT** add a new AI dispatch endpoint (ADR-039) or new/changed AI catalog rows (engine frozen).
- ✅ **MUST NOT** inject AI internals into `Services/Compose/` (ADR-013 Tier-1 NetArchTest); **MUST NOT** leak `Microsoft.Graph` types above `SpeFileStore` (ADR-007).
- ✅ **E2E Definition-of-Done (NFR-06)**: every save/load/dispatch change carries a through-the-wire `WebApplicationFactory` seam slice test (`tests/integration/seam/**`). Unit-green ≠ done. No `Mock<HttpMessageHandler>`, no DI-registration/ctor-null tests (ADR-038).

## ADR Tensions (resolved at spec authoring — carry into PRs)

- **Path B (supersede)**: R2 project decision "Save regenerates `.docx` from editor" → R3 "delta onto retained original." Cite in every E1 PR description (design §11). *Project-level decision amendment — not an ADR-doc change.*
- **Path C (comply, measured)**: Docxodus publish-size/CVE; Graph isolation; no-TipTap-product; ADR-039 no new dispatch.
- **Path A (inherited)**: `AnchoredAnnotation` gains `paraId`/offsets, stays Compose-domain (never written via `memory.*`) — same as R2.

If a new ADR conflict surfaces during execution, invoke the **root CLAUDE.md §6.5 protocol** (surface as path A/B/C; do not silently comply or violate).

---

## Cross-Project Coordination (hot-path)

Per [`projects/INDEX.md`](../INDEX.md), R3's BFF `Services/Compose/` surface overlaps active peers:

- **`spaarkeai-compose-r2`** — direct predecessor; R3 extends its merged seams. ✅ **Confirmed completed/closed + on master (owner, 2026-07-16)** — the E1-cutover (Phase 2) coordination gate is CLEARED.
- **`spaarke-ai-architecture-redesign-r2`** — sole owner of `Services/Ai/` internals. R3's E3 confidence band is **client-derived at render** (no server band; the AI ledger path stays envelope-only per ADR-013/040 — §6.5 Path B) — **NO fork of `Services/Ai/`; consume `PublicContracts` seams only if a server touch is ever needed.**

Run `/conflict-check` before opening any BFF PR.

---

## Rigor Levels

- **FULL** (code-review + adr-check at Step 9.5): all `Services/Compose/` code, client editor/save-path code, the paraId substrate, the Docxodus adapter, and any task modifying `tests/**` (TEST-MODIFYING override).
- **STANDARD**: config-only / packaging / deploy tasks without new logic.
- **MINIMAL**: doc/index updates.

Default execution: **Sonnet 5 @ high**; `opus` / `xhigh` reserved for high-blast-radius tasks (E1 keystone save inversion, Docxodus adapter, real-template hardening) per the POML `<model-tier>` / `<effort>`.

---

*All work on the MIT TipTap base — no TipTap product features; permissive licenses only (no AGPL). Source of truth: [`spec.md`](spec.md).*
