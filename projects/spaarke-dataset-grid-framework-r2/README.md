# Spaarke DataGrid Framework — R2

> **Status**: Idea / discovery — awaiting project pipeline (`/design-to-spec` → `/project-pipeline`)
> **Predecessor**: `spaarke-datagrid-framework-r1` (shipped 2026-06)
> **Discovery session**: 2026-07-01 (ai-spaarke-ai-workspace-UI-r2 post-merge investigation)

## Purpose

R1 shipped the `<DataGrid configId=... />` framework as the canonical shared-lib grid for Spaarke. R2 addresses gaps discovered during production use — most notably the **height-chain break in multi-section workspace layouts** (grids grow to fit content instead of scrolling), plus a set of related maker-experience and per-instance-flexibility gaps.

## Contents

- [`design.md`](design.md) — the comprehensive design document. Read this first. Captures 11 discovered issues + enhancements with evidence, root cause, and proposed solution.
- [`spec.md`](spec.md) — TBD (produced by `/design-to-spec` from `design.md`)
- [`plan.md`](plan.md) — TBD (produced by `/project-pipeline` from `spec.md`)
- [`CLAUDE.md`](CLAUDE.md) — TBD (produced by `/project-pipeline`)
- `tasks/` — TBD (produced by `/project-pipeline`)

## Quick summary of the 11 issues (in priority order)

| # | Priority | Item | Effort |
|---|---|---|---|
| 1 | **P0** | **`contentSizing` on SectionMetadata** — fix height chain systemically (currently patched via per-section registration hack in ai-spaarke-ai-workspace-UI-r2 follow-up) | ~2 hours |
| 2 | **P0** | **Per-layout row-height override** — same widget in dashboard vs full-page needs different heights | ~4 hours |
| 3 | **P1** | **Per-instance `configId` override in layout JSON** — Documents at 25 in Dashboard I, 100 in Documents-only | ~1 day |
| 4 | **P1** | **Width preference on widgets** — some widgets need full-width; wizard should enforce | ~2 hours |
| 5 | **P1** | **View picker allowlist (`availableViews`)** — restrict which savedqueries show in ViewSelector | ~1 hour |
| 6 | **P2** | **Per-instance section rename** — call it "Email" in this layout, "Communications" in that one | folded into #3 |
| 7 | **P2** | **Config templates** in `scripts/config-templates/` — starter JSONs for common shapes | ~30 min |
| 8 | **P2** | **Framework `pageSize` default alignment** — code says `?? 100`, schema doc says `50`; align | ~15 min |
| 9 | **P3** | **Unwind ai-spaarke-ai-workspace-UI-r2 tactical `maxHeight` hack** — replace per-section maxHeight with #1's proper fix | ~30 min |
| 10 | **P3** | **Scrollbar UX polish (deferred, not blocking)** — Fluent's native scrollbar is fine; document decision | 0 |
| 11 | **P3** | **Better default `pageSize`** — currently 100; consider 50 for workspace-embedded default | ~15 min + reviewer discussion |
| 12 | **P2** | **SpaarkeAi build-time alias for LegalWorkspace** — deploying LegalWorkspace alone doesn't update SpaarkeAi consumers (cost ~30 min debug time to discover during 2026-07-01 UAT). Options: doc-only warning OR proper shared-package extraction. | 1 hour (doc) / 1 day (extract) |

**Total estimated effort**: ~2.5 days of focused work + a deploy cycle (or +1 day for Issue 12 Option B).

## Who benefits

- **Makers**: better authoring experience (templates, view allowlist, layout-time overrides)
- **End users**: correctly-scrolling grids in multi-section dashboards; no more "50 rows visible, no scrollbar" surprise
- **Framework maintainers**: the tactical `maxHeight` hack in section registrations gets replaced with a proper framework-level fix, restoring the "framework decides sizing, sections are dumb" principle
