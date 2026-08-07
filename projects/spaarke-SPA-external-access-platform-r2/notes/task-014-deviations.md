# Task 014 — Teams App Packaging: Deviations + Findings

> **Task**: `tasks/014-teams-app-packaging.poml` — Teams personal-tab manifest + CSP frame-ancestors + Teams theme bridging
> **Status**: Completed 2026-08-06
> **Rigor**: FULL (overridden up from authored STANDARD — step 3 modifies TS-adjacent theme-bridge wiring; see POML `<notes>`)

---

## Summary

All three packaging surfaces this task targets — the Teams manifest, the SWA CSP `frame-ancestors` config, and
the Teams theme bridge — were found **already delivered** by `teams-app-r1` and inherited unmodified into this
R2 worktree (same SWA host, same Teams app, same reused Entra app `1e40baad-…`). This matches the task's own
framing ("Build on the teams-app-r1 prior art; do not rebuild host detection") and FR-04's explicit instruction
that R2 "adopts that host as the P1 seed rather than rebuilding it."

Work performed was verification against R2's specific acceptance criteria + one scoped content update, not
re-authoring from scratch.

## What was verified (no changes needed)

1. **CSP** — `src/client/external-spa/staticwebapp.config.json` already sets
   `Content-Security-Policy: frame-ancestors 'self' https://teams.microsoft.com https://*.cloud.microsoft` with
   no `X-Frame-Options` anywhere in the SWA config (confirmed via repo-wide grep). Byte-exact match to the
   constraint. Delivered by teams-app-r1 commit `9d7a2952f` / `5f397d67d`.

2. **Teams theme bridge** — `src/client/external-spa/src/host/TeamsHostAdapter.ts`'s `wireTheme()` +
   `mapTeamsTheme()` already subscribe to `teamsApp.registerOnThemeChangeHandler`, map Teams' theme string to the
   SPA's `'light'|'dark'` preference, and call `setUserThemePreference()` — which feeds the SAME
   `@spaarke/ui-components` cascade (`resolveCodePageTheme`/`setupCodePageThemeListener`) that `App.tsx` already
   consumes, with zero code changes required to `App.tsx`. Confirmed zero hardcoded hex anywhere in
   `src/client/external-spa/src` via grep (`#[0-9a-fA-F]{3,6}` — no matches).

3. **Browser no-op** — `main.tsx`'s `detectTeamsHost()` bounded-timeout race + the `bootstrapStandalone()` /
   `bootstrapTeams()` branch is unchanged; the standalone browser path is unaffected by any Teams JS SDK call
   (confirmed by inspection, delivered by task 011/teams-app-r1).

4. **Manifest** — `src/client/external-spa/appPackage/manifest.json` already exists (teams-app-r1 task 070),
   already a valid `manifestVersion: "1.29"` personal `staticTabs` entry pointing at the SWA content URL, with
   `validDomains` covering the SWA + BFF hosts + Teams domains, and `webApplicationInfo.id`/`.resource` byte-exact
   to the reused Entra app `1e40baad-…`.

## What was changed

**`src/client/external-spa/appPackage/manifest.json`** — text-only update to reflect R2's broader portal framing
(the shell built in task 011 is a "Legal Department Service Portal" with Quick Start + entitled widget modules,
not just the narrower "Collaboration Workspace" teams-app-r1 shipped):

| Field | Before | After |
|---|---|---|
| `version` | `1.0.0` | `1.1.0` |
| `name.full` | `Spaarke Collaboration Workspace` | `Spaarke Legal Department Service Portal` |
| `description.short` | `Access your Spaarke projects and documents` | `Your legal department service portal — assigned work, requests, and more` |
| `description.full` | (collaboration-workspace copy) | (broadened to mention modules, requests, status tracking) |

**Untouched, byte-exact** (verified via `git diff`): `id`, `developer`, `icons`, `accentColor`, `staticTabs`
(`entityId`/`name`/`contentUrl`/`websiteUrl`/`scopes`), `permissions`, `validDomains`, `webApplicationInfo` — a
mismatch on `webApplicationInfo` silently breaks Teams SSO, so these were deliberately left alone.

`staticwebapp.config.json` and `host/TeamsHostAdapter.ts` were **not modified** — the latter is explicitly
off-limits per this task's hard constraints (NFR-05 / ADR-028 no-auth-regression boundary).

## Discovered gap — filed as ISS-001

Teams' high-contrast theme (`'contrast'`) currently maps to the ordinary dark theme rather than a dedicated
Fluent `teamsHighContrastTheme`, because the shared `@spaarke/ui-components` theme cascade only has two states
(`light`/`dark`). ADR-021 lists `teamsHighContrastTheme` as a MUST-supported Code Page state, and the R2 ux-brief
requires "Teams theme (light/dark/high-contrast)" as one of nine required per-surface states. This is a real
accessibility gap, but fixing it requires a cross-cutting change to the SHARED theme cascade (affects every PCF
control + Code Page) plus a change to `TeamsHostAdapter.ts` — both out of this task's scope (barred from
modifying `TeamsHostAdapter.ts`; not a packaging/framing/theme-wiring change but a shared-library authoring one).

Filed via `/project-defer-issue-tracking`:
- **Notes**: `projects/spaarke-SPA-external-access-platform-r2/notes/defer-issues.md` — ISS-001
- **GitHub Issue**: https://github.com/spaarke-dev/spaarke/issues/744

## Verification performed

- `npx tsc --noEmit` in `src/client/external-spa` — clean (no TypeScript files were changed by this task; run as
  a sanity check per the orchestrator's build-verification instruction).
- `node -e "JSON.parse(fs.readFileSync('appPackage/manifest.json'))"` — valid JSON.
- `git diff -- src/client/external-spa/appPackage/manifest.json` — confirms the edit is scoped to exactly
  `version`/`name.full`/`description.{short,full}`.
- `grep -rn "X-Frame-Options"` across `src/client/external-spa` — no matches (confirms no header regression).
- Quality gates (Step 9.5, FULL rigor): code-review + adr-check both clean, no findings (trivial content-only
  diff; no security-sensitive fields touched; no code changed).
- Full `npm run build` intentionally **not** run — the orchestrator's instructions reserve the authoritative
  wave build for the main session after all Group B tasks (012/014/017) land.

## Explicitly NOT done (by instruction / coordination)

- `TASK-INDEX.md` not edited — main session aggregates wave status.
- `current-task.md` not edited — left for the main session's wave-level aggregation (this worktree runs Group B
  tasks 012/014/017 in parallel; editing the shared current-task.md here risked racing those other tasks).
- `src/client/external-spa/vite.config.ts` not touched (task 017's surface).
- No `.tsx`/shell files touched (task 012's surface).
