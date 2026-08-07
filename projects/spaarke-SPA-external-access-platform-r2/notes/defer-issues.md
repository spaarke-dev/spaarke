# Defer / Issue Tracking — spaarke-SPA-external-access-platform-r2

> **Source of truth** for deferred work + newly-discovered issues in this project.
> Each entry has a paired GitHub Issue. See `/project-defer-issue-tracking` skill for the protocol.
>
> **Rollup view**: `gh issue list --label spaarke-SPA-external-access-platform-r2` (visible to whole team via portfolio board)
> **CLAUDE.md §11 rule**: every entry MUST name a concrete behavior or contract that fails without it. "For future flexibility" / "improve testability" / "separation of concerns" = NOT a valid deferral reason — refuse to file.

---

## Open (in priority order)

### ISS-001 — Teams high-contrast theme falls back to dark (no dedicated Fluent high-contrast theme wired into Code Page cascade)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-08-06 |
| **Source** | task 014 (Teams app packaging — manifest + CSP + theme bridging) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/744 |

**Description**

`TeamsHostAdapter.mapTeamsTheme()` (`src/client/external-spa/src/host/TeamsHostAdapter.ts`) maps Teams'
`'contrast'` theme value to `'dark'` as an interim approximation. This is forced by the shared theme
cascade: `ThemePreference` (`src/client/shared/Spaarke.UI.Components/src/utils/themeStorage.ts`) is typed
`'light' | 'dark' | 'auto'` and `resolveCodePageTheme()` / `setupCodePageThemeListener()` /
`resolveThemeWithUserPreference()` only ever resolve to `webLightTheme` or `webDarkTheme` — there is no
wired-in `teamsHighContrastTheme`, even though ADR-021's Dark Mode Support table lists
`High Contrast | teamsHighContrastTheme` as a MUST-supported state for BOTH PCF and Code Page surfaces, and
the R2 ux-brief (`notes/ux-brief.md` §1 + §4 required-states checklist) explicitly requires
"Teams theme (light/dark/high-contrast)" to be verified as one of nine required states per surface.

Concrete failing behavior: a Teams user running Windows/Teams high-contrast mode gets the ordinary dark
theme instead of a true high-contrast-optimized theme — an accessibility gap, not a cosmetic nice-to-have.

**Entry-points**

- `src/client/external-spa/src/host/TeamsHostAdapter.ts:121-133` (`mapTeamsTheme`) and `:184-201` (`wireTheme`)
- `src/client/shared/Spaarke.UI.Components/src/utils/themeStorage.ts:46` (`ThemePreference` type), `:331-352` (`resolveCodePageTheme`), `:443-468` (`setupCodePageThemeListener`), `:256-258` (`resolveThemeWithUserPreference`, PCF path)
- `.claude/adr/ADR-021-fluent-design-system.md` — "Dark Mode Support" table, `High Contrast | teamsHighContrastTheme` row

**Suggested fix** (if known)

Extend the SHARED `@spaarke/ui-components` theme cascade to add a genuine third state wired to
`@fluentui/react-components`'s `teamsHighContrastTheme` (touches `ThemePreference`,
`resolveCodePageTheme`/`setupCodePageThemeListener` for Code Pages, and `resolveThemeWithUserPreference`/
`getEffectiveDarkMode` for PCF — every consumer of the cascade). Then update `TeamsHostAdapter.ts`'s
`mapTeamsTheme()`/`wireTheme()` to pass `'contrast'` through instead of collapsing it to `'dark'`.

**Estimated effort**: unknown — needs spike (cross-cutting shared-library change; blast radius = every PCF control + Code Page consuming the theme cascade)
**Blockers**: none
**Related**: ADR-021 (Fluent UI v9 Design System); `notes/ux-brief.md` §1/§4; task 014 (this project)

---

## In Progress

*None.*

---

## Closed (Done / Won't Fix / Superseded)

*None.*

---

## Notes

- IDs are sequential per kind: DEF-001, DEF-002, ... ISS-001, ISS-002, ...
- ID never gets reused after closure — preserves traceability.
- When a Closed entry is reopened (rare), file a NEW entry referencing the old ID — don't reopen the original.
- Bulk operations: see `/project-defer-issue-tracking` skill, "Status lifecycle" section.
