# Cleanup placement plan (2026-10-03)

> **Owner rule, 2026-10-03:** fix every cleanup item now, never defer it to an issue. If an item is unrelated to
> ontology work, land it in **its own PR**, so the ontology PR (#1111) stays reviewable and does not touch shared
> surfaces such as root `CLAUDE.md`.
>
> Placement follows the reuse audit's own relevance column (`notes/reuse-verification-2026-10-02.md`): ✅ gates
> the worklist row, ⚠️ partly related, — unrelated. Where the audit marked an item unrelated but the worklist will
> be its next consumer, it stays here, because otherwise the row would add another copy.

## On the ontology branch

| Item | Task | Why here |
|---|---|---|
| C-1, C-3, C-4 | 010, 011, 012 ✅ | Gate the worklist row |
| C-8 Xrm frame-walk | 081 | The row runs in the same host as `DailyBriefingApp` |
| C-9 `RowActionMenu` | 052 | The row is built against `DocumentRowMenu` |
| C-11 `EmptyState` | 081 | The worklist needs an empty state |
| C-13 relative-time formatter | 081 | The row shows item age. Reuses 084's `dateLocal.ts` |
| C-17 due-date tiers | 081 | **Owner: 3/7/10 days.** The Do lane shares a screen with event cards |

## Their own PRs

| PR / task | Items | Note |
|---|---|---|
| [#1114](https://github.com/spaarke-dev/spaarke/pull/1114) | C-23 | `CalendarSidePane` is not currently in use but may return, so fixed rather than deleted |
| 084 | C-10 | **Live bug**, merge first |
| 085 | C-5, C-16 | |
| 086 | C-19 (+ #1113), C-21, C-14, C-20, C-26, C-6, C-27 | **C-21: owner chose delete.** C-27 is a record only |
| 087 | C-2, C-24, C-25 | The Events-local `FetchXmlService`, not the shared one ADR-012 cites |
| 088 | C-22 | Web resource; production changes only after a deploy |
| 089 | C-7, C-12, C-15 | |
| 091 | C-18 | Audit, then fix |

## How the moved fixes were moved

Task 080 had already fixed C-5, C-10, C-19 and C-22 inside the ontology commit `1c56cd29b`. Each was extracted as
a patch (`git diff 1c56cd29b~1 1c56cd29b -- <paths>`), checked to apply forward onto master **and** in reverse onto
this branch, then reverted here in one commit. The only shared file, `Spaarke.UI.Components/src/components/index.ts`,
was split by hunk: the StatusBadge export (task 012) stays, and the Toolbar removal moves to 086. After the revert,
every moved path is byte-identical to master.
