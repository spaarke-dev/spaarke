# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-08-06 (session handoff — P0 complete + design locked + tasks re-decomposed)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **011 — Workspace-shell scaffold** (not-started; next) |
| **Step** | — |
| **Status** | not-started (010 ✅ complete) |
| **Next Action** | `task-execute` on **011** (portal header + tab host + pane layout + dockable assistant). Deps 004,010 both ✅. Then Group B {012,014,017}. |

### Task 010 — COMPLETE (2026-08-06)
ADR-028 **Amendment A3** authored (concise-only, additive after A2). Ratifies the dual-plane module-host platform + shipped principal-agnostic endpoint pattern (`CallerPrincipalResolver`/`ExternalCollaboration` dual-scheme/plane-by-iss+tid/third-plane seam) as canonical, with Tier-1⟂Tier-2 invariant. CHANGELOG entry added; TASK-INDEX 010 ✅; POML status=completed. Quality gates skipped (documentation-only). **Escalation trigger did NOT fire** — A2 covers only the collaboration product line, not the platform generalization. Not yet committed (owner-gated).

### Files Modified This Session
- `spec.md` (FR-01/02 reframed to workspace shell; FR-23–27 amendment; document architecture; assistant bounding + NFR-EXT-AI)
- `design.md` §12 (P0 outcome — foundation & architecture decisions)
- `tasks/` re-decomposed to 40 tasks (workspace-shell P1 + expanded P3 + new P5 + 2 spikes); `TASK-INDEX.md` rewritten
- `notes/`: `workspace-shell-foundation.md`, `review-additions-analysis.md`, `external-assistant-access-model.md`, `prototype-findings.md`
- Prototype (separate repo): `spaarke-prototype/projects/2026-08-external-access-module-host` (committed `3832580`)

### Critical Context (READ before starting implementation)
**P0 is done + owner-signed-off.** The prototype validated the FOUNDATION MODEL, not pixels (UI refinements deferred to the project). Key decisions (all in `design.md` §12 + `spec.md` amendment):
1. **Foundation = WORKSPACE SHELL** (not card-launcher): branded portal + pinned Quick Start tab + tabbed role-defaulted **widgets** + entitlement-gated widget library + dockable(left/right) assistant. Reuse the SpaarkeAi workspace **chassis** (Xrm-free) + `BffDataverseClient` widget-data seam; do NOT fork `LegalWorkspaceApp`.
2. **Ask Legal assistant** = EXACTLY 2 tools (P&P `policy_search` + `launch_wizard`); **no file-upload in the chat at all**; workforce-only; **gated by the task-050 security spike (human sign-off)**.
3. **P&P = `sprk_document` + `sprk_documentcategory`** (NO `sprk_policy` entity) + type-routed indexing + Policy Library grid (`sprk_gridconfiguration`). Golden/Reference RAG docs reuse the SAME mechanism. NOT Knowledge Articles.
4. **FR-24 (feedback) + FR-27 (messaging) share ONE thread-on-request** model.
5. **Two spikes gate their builds**: 033 (NDA-redline surface), 050 (external-assistant security — human sign-off).
- teams-app-r1 is **COMPLETE/merged** (external-access surface is a stable base; still `/conflict-check` before BFF PRs).

---

## Active Task (Full Details)
| Field | Value |
|-------|-------|
| **Task ID** | none (next: 010) |
| **Task File** | `tasks/010-adr-028-amendment-a3.poml` |
| **Title** | ADR-028 Amendment A3 |
| **Phase** | P1 |
| **Status** | not-started |

---

## Next Action
**Next**: `task-execute` on **010** (ADR-028 A3 — main-session-only, reads existing A2 first). Then P1
per TASK-INDEX: 011 (shell scaffold) → Group B {012,014,017} → 013 → 015 → 016 → 018 → 019.

**Pre-conditions**: `dotnet build src/server/api/Sprk.Bff.Api/` green; `/conflict-check` before any BFF PR.

---

## Blockers
**Status**: None — awaiting owner-gated execution start.

---

## Session Notes
### Key learnings
- Prototype-first P0 caught a foundation pivot (card-launcher → workspace-shell) BEFORE any production code — the intended value of P0.
- Most additions (FR-23–27) turned out to be **reuse** of existing Spaarke capabilities re-hosted on the external framework; P&P generalized into a reusable "typed document → typed RAG index → typed grid" capability.
### Handoff notes
All design decisions are durable in `spec.md` + `design.md` §12 + `notes/`. The 40-task set is in `TASK-INDEX.md`. Start a fresh session with `/project-continue` or "work on task 010".

---

## Quick Reference
- **Project**: spaarke-SPA-external-access-platform-r2 · **CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md) · **Tasks**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md)
- **Prototype**: `c:/code_files/spaarke-prototype/projects/2026-08-external-access-module-host` (`npm run dev` → :5175)
- **ADRs**: ADR-028(+A1/A2/A3), ADR-008/009/007/001/010/019/021/022/024/034/038/039/050
- **Design outcome**: `design.md` §12

---

## Recovery Instructions
1. Read Quick Recovery + Critical Context above.
2. Read `design.md` §12 + `spec.md` amendment for the locked design.
3. `task-execute` on task 010, then P1 per TASK-INDEX.
**Commands**: `/project-continue` · `/context-handoff` · "work on task 010"

---

*Primary source of truth for active work state. Keep updated.*
