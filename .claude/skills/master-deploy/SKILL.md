# Master Deploy

> **Category**: Operations (Tier 3) — end-to-end unified-master deploy
> **Created**: 2026-06-11 (after the 4-parallel-session deploy collision was resolved)
> **When to use**: After multiple PRs have merged to master and the entire deployable surface needs to land on `spaarkedev1` from one unified master HEAD. The single source of truth for "deploy everything from master."
> **When NOT to use**: A single small change to one Code Page → use that surface's individual `Deploy-*.ps1` directly. A BFF-only change → use `/bff-deploy`.

## Discipline (binding)

1. **Master is the only deploy source for spaarkedev1 web resources.** No feature-branch deploys.
2. **Other Claude sessions: open PR, tell ralph, do NOT deploy.** Tell parallel sessions this when they ask.
3. **Whoever lands the LAST PR runs this skill** — exactly once — to bring all surfaces back to a unified state.
4. **The skill is run autonomously.** No questions. Reasonable defaults. If a step fails, the skill recovers per the embedded failure-mode handlers, not by asking.

## What this skill deploys (canonical surface manifest)

| # | Surface | Web resource | Deploy mechanism |
|---|---|---|---|
| 1 | SpaarkeAi | `sprk_spaarkeai` | `scripts/Deploy-SpaarkeAi.ps1` |
| 2 | DailyBriefing | `sprk_dailyupdate` | `scripts/Deploy-DailyBriefing.ps1` |
| 3 | Reporting | `sprk_reporting` | **`scripts/master-deploy/deploy-webresource-inline.mjs`** (see §"Reporting fallback") |
| 4 | SmartTodo | `sprk_smarttodo` | `scripts/Deploy-SmartTodo.ps1` |
| 5 | SpeAdminApp | `sprk_speadmin` | `scripts/Deploy-SpeAdminApp.ps1` |
| 6 | EventDetailSidePane | `sprk_eventdetailsidepane.html` | `scripts/Deploy-EventDetailSidePane.ps1` |
| 7 | CalendarSidePane | `sprk_calendarsidepane.html` | `scripts/Deploy-CalendarSidePane.ps1` |
| 8-19 | 12 wizards/CodePages | (see Deploy-WizardCodePages.ps1) | `scripts/Deploy-WizardCodePages.ps1` (one batch) |
| 20 | **BFF API** | `spaarke-bff-dev` App Service | `/bff-deploy` skill |

The 12 surfaces in `Deploy-WizardCodePages.ps1`: CreateMatter, CreateProject, CreateEvent, CreateTodo, CreateWorkAssignment, SummarizeFiles, FindSimilar, PlaybookLibrary, DocumentUpload, AllDocuments, WorkspaceLayoutWizard, sprk_wizard_commands.

**Excluded** (intentional): `sprk_tododetailsidepane` (deleted by #373), `sprk_corporateworkspace` (retired by OC-R4-05).

## Pre-flight (Step 0 — always run)

Before any deploy work, verify and report:

```bash
git fetch origin master
git log origin/master -1                         # the HEAD we'll deploy from
git status --short                               # warn on uncommitted changes
gh pr list --state open --json number,title      # surface what's still open
```

State to record at the start of the run (use TodoWrite):
- Master HEAD SHA
- Branch we're on (must be on master or have master fully merged in)
- Open PRs (sanity check — are we forgetting anyone?)

**Hard stops:**
- Uncommitted changes to `src/client/shared/**` or `src/solutions/**` → require commit first or `git stash` (auto-stash if obviously noise like package-locks).
- BFF code uncommitted → require commit first (no exceptions — affects production).

## Step 1 — Build shared libs

Order matters (each later one consumes earlier ones via path-mapped dist):

1. `src/client/shared/Spaarke.Auth` — `npm install --legacy-peer-deps --no-audit --no-fund && npm run build`
2. `src/client/shared/Spaarke.AI.Context` — same
3. `src/client/shared/Spaarke.SdapClient` — same. **Important**: this lib's `tsconfig.json` must have `declarationMap: false` and `sourceMap: false` (see Failure Mode F-1). The current source-tree version has this set correctly.
4. `src/client/shared/Spaarke.UI.Components` — same. Path map for `@spaarke/sdap-client` must point to `dist/index.d.ts` not `src/index.ts` (Failure Mode F-1).
5. `src/client/shared/Spaarke.AI.Outputs` — same
6. `src/client/shared/Spaarke.AI.Widgets` — same. tsc may report 70+ TS2307 errors in shared-lib path resolution; these are advisory and the emitted dist is still usable.
7. `src/client/shared/Spaarke.Events.Components` — same. Build script is `tsc --noEmit`; the lib is consumed via Vite source aliasing.
8. `src/client/shared/Spaarke.SmartTodo.Components` — same. Build script is `tsc --noEmit`; consumed via source aliasing.

## Step 2 — Build all Vite solutions

Use the proven Node script (NOT `Build-AllClientComponents.ps1` — see Failure Mode F-2):

```bash
node scripts/master-deploy/build-all-vite-solutions.mjs
```

Expected result: 18-19 successful builds, 0-2 known failures (LegalWorkspace retired; sprk_invoicespage and sprk_kpiassessmentspage may need `npm install` first if they're stale).

**Hard stop**: if any of these surfaces fail, stop and investigate before deploying. They're not optional:
SpaarkeAi, DailyBriefing, Reporting, SmartTodo, SpeAdminApp, EventDetailSidePane, CalendarSidePane, all 12 wizard surfaces.

Verify SpaarkeAi bundle markers exist (sanity that PRs landed in the bundle):

```bash
for m in "gridTableOverride" "BriefcaseSearchRegular" "create-new-todo" "matters-list" "WidgetErrorBoundary"; do
  c=$(grep -c "$m" src/solutions/SpaarkeAi/dist/spaarkeai.html); echo "$m : $c"
done
```

All 5 markers should be ≥ 1. (Add markers from any new PR in the batch you're deploying.)

## Step 3 — Deploy 12 wizards via batch script

```bash
DATAVERSE_URL=https://spaarkedev1.crm.dynamics.com powershell -NonInteractive -ExecutionPolicy Bypass -File scripts/Deploy-WizardCodePages.ps1 -DataverseUrl "https://spaarkedev1.crm.dynamics.com"
```

Expected output: `[12/12] UPDATE ... Done` for each, then `Published!`.

## Step 4 — Deploy 6 individual web resource scripts

Run these in parallel (independent web resources, no shared state):

```bash
DATAVERSE_URL=https://spaarkedev1.crm.dynamics.com
for s in Deploy-SpaarkeAi Deploy-DailyBriefing Deploy-SmartTodo Deploy-SpeAdminApp Deploy-EventDetailSidePane Deploy-CalendarSidePane; do
  powershell -NonInteractive -ExecutionPolicy Bypass -File scripts/$s.ps1 -DataverseUrl "$DATAVERSE_URL" &
done
wait
```

Or sequentially if parallelism causes az CLI rate-limit issues (rare but observed during 2026-06-10 deploy).

## Step 5 — Deploy Reporting (the special case)

`Deploy-ReportingCodePage.ps1`'s PowerShell wrapper converts Rollup's stderr warnings (`/* #__PURE__ */` annotation noise) into `NativeCommandError` exit-1 failures even though the build is fine. Use the Node fetch-based deploy instead:

```bash
# Make sure dist is built (Step 2 already did this)
node scripts/master-deploy/deploy-webresource-inline.mjs sprk_reporting src/solutions/Reporting/dist/index.html
```

The helper handles: token via `az account get-access-token`, PATCH `webresourceset(id)` content, POST `PublishXml`. Returns non-zero on any HTTP non-2xx.

## Step 6 — Deploy BFF via /bff-deploy skill

Delegate to `/bff-deploy` — do not run `Deploy-BffApi.ps1` directly from this skill.

The `Deploy-BffApi.ps1` script has a `--no-restore` bug (the publish step skips restore but the script doesn't restore first). Workaround before invoking the skill:

```bash
# Pre-build to populate the publish folder
cd src/server/api/Sprk.Bff.Api
dotnet publish -c Release -o c:/code_files/spaarke/deploy/api-publish/
cd -
# Then invoke the skill (which calls Deploy-BffApi.ps1 -SkipBuild)
```

Or: invoke `/bff-deploy` with the args specifying `-SkipBuild` after running the publish.

The skill's own hash-verify + auto-recover (`stop → Kudu zipdeploy → start`) handles the silent file-lock case automatically. Expected success looks like:

```
[4/4] Verifying file replacement on server...
  All 4 critical files match local build (SHA-256 verified)
[5/4] Verifying health endpoint...
  dev health check passed!
```

If the script's first `az webapp deploy` returns 400 but hash-verify still passes — that's expected; the auto-recover ran successfully.

## Step 7 — Verification

```bash
curl -s -o /dev/null -w "%{http_code}\n" https://spaarke-bff-dev.azurewebsites.net/healthz   # expect 200
curl -s -o /dev/null -w "%{http_code}\n" https://spaarke-bff-dev.azurewebsites.net/ping       # expect 200
```

If `/healthz` is slow (90-120 s after Linux App Service cold-start) but hash-verify in Step 6 passed: don't redeploy, just re-curl after 60 s.

The user should hard-refresh **`spaarkedev1`** in Incognito to verify SpaarkeAi shows all merged work — that's the eyeball check this skill can't do for them.

## Failure modes (encoded fixes — no user prompts)

### F-1: Transitive `@spaarke/sdap-client` or `@spaarke/smart-todo-components` not resolved by vite

**Cause**: A solution's `package.json` declares `@spaarke/ui-components` but not its transitive deps. After PR #369 (multi-container, added `@spaarke/sdap-client` import to `EntityCreationService.ts`) and PR #377 (smart-todo-r4, added `@spaarke/smart-todo-components` import to LegalWorkspace's `todo.registration.ts`), every Vite solution needs both as DIRECT deps even though they're transitive in source.

**Fix**: Run `node scripts/master-deploy/fix-vite-transitive-deps.mjs` — it walks every `src/solutions/*/package.json` that has `@spaarke/ui-components`, adds both transitive deps if missing, sorts the dependencies map. Then `npm install --legacy-peer-deps --no-audit --no-fund` in any solution that was just touched. Commit + PR the package.json changes after the deploy succeeds (don't block the deploy on the PR).

### F-2: `Build-AllClientComponents.ps1` reports all Vite solutions FAILED

**Cause**: PowerShell captured npm/Rollup stderr warnings (`/* #__PURE__ */`) via `2>&1` into a variable. Under script-level `$ErrorActionPreference = "Stop"`, those stderr lines became terminating error records — the catch marked every build FAILED even though `$LASTEXITCODE` was 0.

**Root-cause fix landed 2026-06-11**: localized `$ErrorActionPreference = 'Continue'` inside script blocks that invoke npm install + npm run build. Failure detection now relies on `$LASTEXITCODE` only. The script's outer Stop discipline is preserved for all other operations.

**Fallback** (Node script remains a safety net): `node scripts/master-deploy/build-all-vite-solutions.mjs` — calls `npm run build` per solution serially. Use if the PS1 ever exhibits new failure modes.

### F-3: `dotnet publish --no-restore` fails inside `Deploy-BffApi.ps1`

**Cause**: Script Step 1 ran `dotnet publish ... --no-restore` with no preceding `dotnet restore`. Fresh checkouts failed with exit 1.

**Root-cause fix landed 2026-06-11**: explicit `dotnet restore --verbosity minimal` step added immediately before publish. No more manual pre-publish needed before invoking `/bff-deploy`.

### F-4: Reporting deploy fails with `NativeCommandError` on Rollup warning

**Cause**: `Deploy-ReportingCodePage.ps1`'s npm install + npm run build calls inherited script-level `$ErrorActionPreference = "Stop"`. Rollup `/* #__PURE__ */` warnings on stderr became terminating error records.

**Root-cause fix landed 2026-06-11**: same localized `$ErrorActionPreference = 'Continue'` pattern as F-2. The PS1 script now deploys cleanly without falling back to the Node helper.

**Fallback** (Node script remains a safety net): `node scripts/master-deploy/deploy-webresource-inline.mjs sprk_reporting src/solutions/Reporting/dist/index.html` — fetch-based upload bypassing the PS1 entirely. Use if the PS1 ever exhibits new failure modes.

### F-5: `execSync` ENOBUFS on Dataverse Web API responses

**Cause**: Node `execSync` (used by some inline-Node helper scripts) has a 1 MB default output buffer. Web resource content responses easily exceed this.

**Fix**: All Node helper scripts under `scripts/master-deploy/` use `fetch()` (built-in to Node 18+), not `execSync` + curl.

### F-6: Auto-merge fired before batch-fix commit landed

**Cause**: Auto-merge on a "follow-up" PR sometimes fires before further commits to the same branch can be pushed. The first commit lands, subsequent ones are orphaned.

**Fix**: Push all related commits before enabling auto-merge. If it already happened: open a new PR for the orphaned commits (they're still valuable for future builds).

## Triggers

- Phrase: "/master-deploy" or "master deploy" or "deploy from master to spaarkedev1"
- Phrase: "deploy everything from master"
- After: multiple PRs land on master and someone says "now do the deploy"
- After: a parallel session reports a PR merged and you've confirmed all related PRs are in
- **NOT** for: single-surface tweaks (use the per-surface script directly)

## Autonomous-mode contract

When triggered with no explicit blockers from the user:

1. Set up TodoWrite with all 7 steps above as tasks
2. Run Step 0 pre-flight — report findings briefly
3. Execute Steps 1-7 sequentially without prompts
4. Use the F-1 through F-6 fixes inline as they trigger — do not ask the user
5. Final report: surface table with size + status for all 19 web resources + BFF
6. If a step has a HARD STOP, stop and surface the diagnostic — but only at the hard stops listed above

## Related skills

| Skill | When |
|---|---|
| `/bff-deploy` | BFF-only deploy (Step 6 delegates to it) |
| `/code-page-deploy` | Single Code Page tweak (NOT for unified deploy) |
| `/pcf-deploy` | PCF control solutions (different deploy mechanism — not in master-deploy scope) |

## Audit / next iteration

- **Deploy ledger** (`~/.spaarke-deploy-ledger.json`): proposed but not built. Would record `{branch, commit, deployed_at}` per web resource and warn on cross-branch overwrites. ~60-90 min of work.
- **Per-PR pre-flight CI**: detect when a PR adds a transitive `@spaarke/*` import that consumers don't declare. Would prevent the F-1 firefight at deploy time.
- **`Build-AllClientComponents.ps1` failure root-cause**: until diagnosed, the Node fallback is the canonical path.
