# Side-Pane Navigation & Quick-Access (Navigation History) — r1

> **Status**: ✅ COMPLETE 2026-08-15 — all 21 tasks done; Navigator deployed + owner-UAT-passed (light+dark) on spaarkedev1; auto-load live on Matter/Document/Project/Event/Communication/Todo entity ribbons + SpaarkeAi/Email/Reconciliation code pages. (Initialized 2026-08-13 via `/project-pipeline`.)
> **Portfolio**: [Project #764](https://github.com/spaarke-dev/spaarke/issues/764) · Epic [#430 Insights/Widgets/Search](https://github.com/spaarke-dev/spaarke/issues/430) · [Board — Project #2](https://github.com/users/spaarke-dev/projects/2)
> **Branch**: `work/side-pane-navigation-history-r1`
> **Slug**: `spaarke-side-pane-navigation-history-r1`
> **Source docs**: [`design.md`](design.md) (owner-approved 2026-08-12, Path B) → [`spec.md`](spec.md)

---

## What this delivers

A reusable **side-pane framework** (host + registry + thin contributors) for the model-driven app (MDA) shell, and **Navigation History ("Navigator")** as its first tenant: a single, always-available docked pane giving one-gesture access from anywhere to **Recent** (Viewed / Edited), **Pinned** (records + bookmarks + a shared Monitored lens), and **Views** — all cross-device via a new per-user `sprk_navitem` Dataverse entity.

The load-bearing technical move: capture browse history with **zero per-form web resources and zero Dataverse plugins**, by exploiting a persistent app-level side pane that polls `Xrm.Utility.getPageContext()`. **No `Sprk.Bff.Api` code is added** — all data access is host-context `Xrm.WebApi` under the signed-in user.

## Architecture direction (decided)

- **Path B — global always-available docked pane + continuous capture** (owner, 2026-08-12), building on the recovered `SidePaneManager` Code Page-injection bootstrap. Path A (contextual launch) is the documented rejected fallback.
- **Framework, not a pane**: `SprkSidePaneHost` **wraps `SidePaneShell`** (chrome) and **generalizes `DataGridSidePaneOrchestrator`** (pane lifecycle) + a new `sidePaneRegistry`. `NavigatorPane` is the first contributor. (OQ-9 resolved in pipeline Step 2: `SidePaneShell` is presentational-only.)

## Scope at a glance

**In scope (r1):** the framework; the Path B app-startup bootstrap; `NavigatorPane` (Recent/Pinned/Views + persistent search); `sprk_navitem` (prune-on-write 30-day retention); capture engine; read-time security trimming; a stub contributor proving the framework.

**Out of scope (phase 2):** SpaarkeAi dashboard "Recent & Pinned" widget; full-page management view; folders/tags on pins; "back to previous record" navigation *stack*; external-SPA capture; change-notification fusion.

See [`spec.md`](spec.md) for the full FR/NFR set, ADR tensions, and owner clarifications.

## Graduation criteria (from spec Success Criteria)

- [ ] From any MDA page, the docked Navigator opens in one click; Recent (Viewed) populates by navigation with **no form code deployed**.
- [ ] Recent → Edited shows records recently modified by the user (derived from `modifiedon`, **no audit entity**).
- [ ] Starring a record creates a **per-user** pin, unaffected by other users; `sprk_monitor` stays independent.
- [ ] Pins/bookmarks survive sign-out and appear on another device (cross-device).
- [ ] "Pin this page" (one click) + "+ Add bookmark" (URL parse → logical target; non-Dataverse URL → raw weblink).
- [ ] Monitored group lists the user's monitor-flagged records, distinct from personal pins.
- [ ] History older than 30 days pruned on next capture write.
- [ ] Search box fuzzy-matches Recent/Pinned/Views + finds a live Dataverse record/view; keyboard accelerator focuses it.
- [ ] Views tab lists saved views grouped by entity; click opens the grid with that view.
- [ ] A record the user lost access to does not display its cached name.
- [ ] A stub contributor registers with only `{ id, icon, title, component }` — proving the framework.
- [ ] **No changes to `Sprk.Bff.Api`; no plugin; no per-form web resource.**

## Key facts for implementers

- **Hot-path declaration**: BFF=N, SpaarkeAi=N, CI=N, Skill-directives=N, root-CLAUDE=N. §10 publish-size/CVE/BFF-test obligations **N/A**.
- **Shared-lib note**: touches `@spaarke/ui-components` (`SidePane/`, `xrmContext.ts`). No active worktree touches the `SidePane/` subfolder, but run `/conflict-check` before any `@spaarke/ui-components` PR.
- **Data access**: host-context `Xrm.WebApi` only (per `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md`).
- **First gate**: Task 001 is a **spike** (FR-00) re-validating the Path B injection bootstrap on current UCI. Go/no-go — failure triggers the documented Path A fallback.

## Project artifacts

- [`design.md`](design.md) — human design (Path B rationale, prior art, ADR tensions)
- [`spec.md`](spec.md) — AI-optimized spec (14 FRs, 8 NFRs, ADR tensions, owner clarifications)
- [`plan.md`](plan.md) — implementation plan (phases, WBS, parallel groups, discovered resources)
- [`CLAUDE.md`](CLAUDE.md) — project AI context
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — task registry, dependencies, parallel groups
- [`notes/retired-sidepane-code/`](notes/retired-sidepane-code/) — recovered `SidePaneManager.ts` / `contextService.ts` / `ContextSwitchDialog.tsx` to re-adopt
