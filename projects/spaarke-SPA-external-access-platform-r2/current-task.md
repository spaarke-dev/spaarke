# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-08-06 (session handoff — P0 complete + design locked + tasks re-decomposed)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | none active — **011, 012, 014, 017 ✅**; next = 013 + 015 |
| **Step** | — |
| **Status** | not-started (P1 Group B done; wave build green) |
| **Next Action** | Next P1 unblocked: **013** (dual-plane auth bootstrap — CIAM + realm discovery; O/h; prereq 011 ✅) and **015** (FR-22 module/widget-data framework generalization — core BFF; O/x; prereq 010 ✅; `/conflict-check` before its PR). Then 016 (deps 012,015) → 018 (deps 015,016) → 019 (deploy P1). |

### P1 Group B — COMPLETE (2026-08-06, 3 parallel sub-agents; wave build green)
- **012 ✅** widget registry: `api/me-client.ts` (`/me` contract + mock), `registry/widgetRegistry.ts` (11 defs, `isEntitled`/`defaultWidgetIdsFor`/`libraryWidgetsFor`/lazy loader), `registry/PlaceholderWidgetBody.tsx`, `components/shell/WidgetLibraryModal.tsx` (shared `FormModal`); rewrote `QuickStartPane` (role-gated cards + More Services `FormModal`) + `WorkspaceHomePage` (fetch `/me`, seed role-defaulted tabs behind pinned Quick Start, single `onEntitledOpenWidget` entitlement-honest choke point — unknown/unentitled ids non-routable). Plane derived from real `teamsHost`; entitlements mocked pending **task 022**. `?dv_persona=` dev override behind `VITE_DEV_MOCK`.
- **014 ✅** Teams packaging: manifest/CSP/theme already delivered by teams-app-r1 (inherited); branding-only manifest bump (1.1.0). **ISS-001 / GH #744** filed: Teams high-contrast theme maps to plain dark (shared cascade 2-state) — ADR-021 gap, cross-cutting shared-lib fix deferred.
- **017 ✅** Power Pages cleanup: removed dead vite dev-proxy + `removeModuleScriptType` plugin, deleted `powerpages.config.json`, rewrote README. Escalation did NOT fire (grep-verified dead). Left `format:'iife'` unchanged (documented follow-up). `tsconfig.node.json` has 8 pre-existing `@types/node` errors (predate; not a regression).
- **Wave verify**: `tsc --noEmit` clean + `npm run build` green (2480 modules) with all three integrated. Each sub-agent ran its own Step 9.5 gates; none touched auth/**, TASK-INDEX (main-session aggregated), or each other's surfaces.
- Deferred/notes: `notes/task-012|014|017-deviations.md`, `notes/defer-issues.md` (ISS-001).

### Task 011 — COMPLETE (2026-08-06)
Workspace-shell chassis extracted into `src/client/external-spa`. **Build green** (`npm run build` — Vite, not `build:prod`; SPA is a code page not PCF, owner-confirmed), **`tsc --noEmit` clean**. Code-review done (2 minor React-hygiene warnings fixed: atomic tab-state; stable callback deps). ADR-021/022/028/050 + §11 compliant.
- **New** `components/shell/`: `TabStrip` (§11 corner-× exception), `PortalWorkspaceShell` (tabs+dock chassis), `QuickStartPane` (shared `SectionPanel`+`ActionCardRow`+`ChoiceModal`), `AssistantPane` (placeholder → SprkChat is FR-26/051), `useWorkspaceTabs`, `index.ts`.
- **Rewrote** `AppHeader`→branded portal header (shared `ThemeToggle`); `App.tsx`+`main.tsx` thread `teamsHost`; `WorkspaceHomePage`→shell host.
- **Preserved** R1 dashboard via `git mv` → `pages/OutsideCounselDashboard.tsx` (task 016 re-homes as widgets).
- **Auth untouched** (NFR-05): `auth/**`, `TeamsHostAdapter`, `AuthGuard`, `config.ts` unchanged.
- Deviations + §11 justifications: `notes/task-011-deviations.md`. UI `<ui-tests>` deferred to owner browser (`npm run dev`).
- **Extension seam for 012**: `PortalWorkspaceShell` takes `widgetTabs` + `renderWidget(id)`; `useWorkspaceTabs.openTab` is the placeholder path 012 replaces with the real entitlement-gated registry.

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
