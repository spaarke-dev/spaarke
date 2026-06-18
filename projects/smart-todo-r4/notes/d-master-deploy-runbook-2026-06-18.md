# R4-092 master-deploy runbook (Wave D redeploy)

> **Date drafted**: 2026-06-18 (pre-stage; waiting on PR #391 to auto-merge)
> **Purpose**: One-pass deploy of Wave D widget-parity changes to spaarkedev1 from master.
> **Trigger**: Fire after PR #391 (`feat(smart-todo-r4): Wave D widget parity + 094 grep gate + 092 deploy-in-progress`) auto-merges to master.
> **Owner**: Whoever lands LAST among in-flight PRs touching SpaarkeAi / SmartTodo / shared Code Page surfaces (durable rule: spaarkedev1 deploys are master-only).
> **Skill**: Reference `.claude/skills/master-deploy/SKILL.md` for the canonical wrapper; this runbook is the SmartTodo-R4-specific instance.

## What changed in Wave D (drives what to redeploy)

| Surface | Wave D source touches | Redeploy required? |
|---|---|---|
| **SmartTodo Code Page** (`sprk_smarttodo.html` web resource) | `SmartTodoApp.tsx` + `useLaunchContext.ts` (R4-100) | ✅ YES |
| **LegalWorkspace Code Page** | `todo.registration.ts` (R4-099 + R4-100) + `SmartToDo.tsx` import swap (R4-101) + `@spaarke/smart-todo-components` peer package (widget chrome + grouping — bundles into LW) | ✅ YES |
| **CreateTodoWizard Code Page** | `main.tsx` BroadcastChannel producer + `vite.config.ts` alias (R4-100) | ✅ YES |
| **RegardingResolver PCF v1.1.0** | Untouched in Wave D (deployed pre-Wave-D in 2026-06-18 session at v1.1.0) | ❌ No |
| **BFF API** | Untouched in Wave D | ❌ No |
| **`sprk_todo_dirty_check.js` web resource** | Untouched in Wave D (from R4-041; already on master from PR #384) | ❌ No |

**Net**: 3 Code Pages need rebuild + redeploy. PCF + BFF + web resources stay as-is.

## Pre-flight (run BEFORE deploy)

```bash
# 1. Confirm PR #391 is merged
gh pr view 391 --json state,mergedAt,mergeCommit | head -3
# Expected: {"mergeCommit":{"oid":"<sha>"},"mergedAt":"<iso>","state":"MERGED"}

# 2. Switch to MAIN repo (deploys run from master, not the worktree branch)
cd C:/code_files/spaarke
git fetch origin master --prune
git checkout master
git reset --hard origin/master
git log -1 --oneline   # should be the PR #391 merge commit

# 3. Confirm DATAVERSE_URL env var
echo $env:DATAVERSE_URL   # PowerShell — expect https://spaarkedev1.crm.dynamics.com
# If empty:
$env:DATAVERSE_URL = "https://spaarkedev1.crm.dynamics.com"

# 4. Confirm az auth fresh (token-expiry is the #1 deploy failure cause)
az account get-access-token --resource $env:DATAVERSE_URL --query expiresOn -o tsv
# If expired: az login
```

## Deploy sequence (run from main repo on master)

### Step 1 — SmartTodo Code Page

```powershell
# Build (Vite production)
cd src/solutions/SmartTodo
npm install --legacy-peer-deps --no-audit --no-fund   # only if package-lock changed
npm run build
# Expected: ✓ built in ~7-14s, dist/index.html ~1.74 MB / ~474 KB gzip
cd ../../..

# Deploy
./scripts/Deploy-SmartTodo.ps1
# Expected: 5/5 steps green; web resource sprk_smarttodo.html updated; publish-customizations succeeded
```

**Acceptance check**: Look for `Updated: sprk_smarttodo.html (1.7 MB)` and `Publish: Success` in the script output.

### Step 2 — LegalWorkspace Code Page

```powershell
# Build (Vite production — includes @spaarke/smart-todo-components widget changes from Wave D)
cd src/solutions/LegalWorkspace
npm install --legacy-peer-deps --no-audit --no-fund   # only if package-lock changed
npm run build
# Expected: ✓ built in ~9-13s, dist/index.html ~2.25 MB / ~624 KB gzip
cd ../../..

# Deploy — this is the LW Custom Page (different deployment path than SmartTodo)
./scripts/Deploy-LegalWorkspaceCustomPage.ps1
# Expected: solution imported, publish-customizations succeeded
```

**Acceptance check**: solution import succeeded; canvasapps query confirms `sprk_LegalOperationsWorkspace` registered.

### Step 3 — CreateTodoWizard Code Page

```powershell
# Build (Vite production — includes BroadcastChannel producer from R4-100)
cd src/solutions/CreateTodoWizard
npm install --legacy-peer-deps --no-audit --no-fund   # only if package-lock changed (R4-100 added sdap-client alias to vite.config.ts; deps may have shifted)
npm run build
# Expected: ✓ built in ~7s, dist/index.html ~1.66 MB / ~451 KB gzip
cd ../../..

# Deploy (Deploy-WizardCodePages.ps1 deploys ALL wizard code pages — CreateTodoWizard is one of them)
./scripts/Deploy-WizardCodePages.ps1
# Expected: all wizard web resources updated; publish-customizations succeeded
```

**Acceptance check**: `sprk_createtodowizard.html` updated; publish succeeded.

## Smoke verification (before handing back to user)

```powershell
# Quick "does it load?" check via Dataverse Web API
$accessToken = az account get-access-token --resource $env:DATAVERSE_URL --query accessToken -o tsv
$headers = @{ 'Authorization' = "Bearer $accessToken" }
$apiUrl = "$env:DATAVERSE_URL/api/data/v9.2"

# Confirm all 3 web resources present + their content-length (rough sanity check)
foreach ($wr in @('sprk_smarttodo.html','sprk_createtodowizard.html')) {
  Invoke-RestMethod -Uri "$apiUrl/webresourceset?`$filter=name eq '$wr'&`$select=name,modifiedon" -Headers $headers |
    Select-Object -ExpandProperty value
}
# Expected: both rows present; modifiedon = recent (within this deploy session)
```

For LegalWorkspace, verify the Custom Page entity instead:
```powershell
Invoke-RestMethod -Uri "$apiUrl/canvasapps?`$filter=name eq 'sprk_LegalOperationsWorkspace'&`$select=name,modifiedon" -Headers $headers
```

## Rollback plan (if a deploy fails or UAT finds a regression)

The previous deploy state (pre-Wave-D) is preserved in two ways:

1. **Master commit history**: revert PR #391's merge commit on master via `gh pr revert 391` or `git revert <merge-sha> -m 1`. Then re-run this runbook from the reverted master.
2. **Solution backups**: Dataverse solution layers preserve the prior version of each web resource. To roll back a single web resource without touching code, use the Dataverse Maker Portal → Solutions → Web Resources → version history → restore.

## Common failure modes (from prior sessions)

| Failure | Workaround |
|---|---|
| `az login` token expired mid-deploy | Re-run `az login`; script will reacquire token on next invocation |
| `npm ci` fails on a Vite solution | Use `npm install --legacy-peer-deps --no-audit --no-fund` (per root CLAUDE.md §11 Node Installs section) |
| `npm install` shows lockfile drift | Commit the regenerated lockfiles in a follow-up commit before deploy; mirrors PR #383 / `89a2e9395` pattern |
| `@spaarke/sdap-client` cascade rebuild | Verify per-code-page vite alias (added by R4-100 for CreateTodoWizard; already in SmartTodo + LW + 5 others); project-wide tsconfig refs fix tracked for R4-092 / R4-098 |
| PCF manifest version-bump quirks | Not applicable — Wave D did not modify the PCF; v1.1.0 from the 2026-06-18 deploy stays |
| publish-customizations 504 timeout | Retry; Dataverse publish is idempotent for web resources |
| `Build-AllClientComponents.ps1` shows false-FAILED for a solution despite exit-code 0 | Fixed by PR #386; root-cause was `$ErrorActionPreference = "Stop"` capturing npm stderr warnings. If you see this, you're on stale master — pull first. |

## After deploy succeeds

1. Update `projects/smart-todo-r4/tasks/092-deploy-all-affected-solutions.poml` `<status>` to `complete`
2. Append a deploy-session entry to `current-task.md` Quick Recovery noting the 3 Code Page redeploys
3. Hand back to user for UAT checklist walk-through
4. **Do NOT touch R4-098 wrap-up** — held open per user instruction until UAT acceptance sign-off
5. R4-093 UI test suite — user is running their own UAT checklist (per their session note "for testing I'll go through our UAT check list"); the automated 093 NFR-05/07/08 sweep may still be desirable as a closeout artifact, but it's not the path the user is taking for this UAT round.

## References

- `.claude/skills/master-deploy/SKILL.md` — canonical master-deploy wrapper
- `scripts/master-deploy/build-all-vite-solutions.mjs` — node helper for batch Vite builds (alternative to per-solution build loop)
- `scripts/master-deploy/deploy-webresource-inline.mjs` — node helper for in-place web resource updates without solution import
- `projects/smart-todo-r4/notes/d-widget-parity-audit-2026-06-18.md` — audit + remediation plan that justifies this redeploy
- PR #391 — the merge that brings Wave D to master
