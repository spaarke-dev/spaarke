# .NET 10 Cutover Process + Worktree Reactivation

> **Date**: 2026-08-13 · Supersedes the "13 worktrees" framing in `051-coordination.md` — owner confirms **only 4–5 active worktrees**. Lighter coordination.
> **Prereq**: net10 GO recorded (`051-smoke-result.md` — validated on the isolated slot).

---

## The one constraint that drives everything

`spaarke-bff-dev` is a **single shared App Service** with one global runtime string. **net8 binary on a net10 runtime = 503, and vice-versa.** So the instant dev flips to net10, every active worktree must be on net10 (merged master) before its **next BFF deploy**. Day-to-day coding on a net8 branch is unaffected — only *deploying to dev* is gated.

Nothing conflicts today: this branch isn't merged, master is net8, dev is net8.

---

## Cutover process (coordinated window, ~30–60 min)

### Pre-cutover (this branch)
0. **Re-sync master into this branch** (it's ~40 commits behind): `git fetch origin && git merge origin/master`; resolve conflicts; re-verify `dotnet build` + full test suite green on net10. (This is the 090 wrap-up merge.)

### The window
1. **Merge net10 → master.** Master is now the net10 baseline; master CI runs net10 (task 040).
2. **Broadcast to the 4–5 active worktrees** (see message below): install .NET 10 SDK + `git merge origin/master` before next BFF deploy.
3. **Flip dev to net10.** Two options:

   **Option A — direct deploy to main dev (RECOMMENDED for dev; simplest).** Main dev already has the correct `keyVaultReferenceIdentity`, so no slot gymnastics. ~1–2 min cold-start window (dev has no SLA).
   ```powershell
   # runtime -> net10 (escaped-quote for the pipe in pwsh)
   az webapp config set -g rg-spaarke-dev -n spaarke-bff-dev --linux-fx-version '"DOTNETCORE|10.0"'
   # disable the classic AI codeless agent (net10 + FR-06; OTel via APPLICATIONINSIGHTS_CONNECTION_STRING unaffected)
   az webapp config appsettings set -g rg-spaarke-dev -n spaarke-bff-dev --settings `
     ApplicationInsightsAgent_EXTENSION_VERSION=disabled DiagnosticServices_EXTENSION_VERSION=disabled XDT_MicrosoftApplicationInsights_Mode=disabled
   # deploy the net10 build (post-master-merge)
   pwsh -ExecutionPolicy Bypass -File scripts/Deploy-BffApi.ps1
   ```
   **Rollback**: set runtime back to `DOTNETCORE|8.0` + redeploy the last net8 artifact (a few min).

   **Option B — zero-downtime slot swap (rehearses the prod procedure; instant rollback).** Re-deploy the FINAL post-merge net10 build to the `staging` slot, confirm the slot's app settings match main + codeless-AI disabled, then swap:
   ```bash
   az webapp deployment slot swap -g rg-spaarke-dev -n spaarke-bff-dev --slot staging --target-slot production
   ```
   **Rollback**: swap back (instant — main's old net8 content is parked on the slot). Only pick B if you want the zero-downtime rehearsal; for dev, A is less fuss.

4. **Smoke main dev on net10**: `/healthz` 200, `/ping`, + the browser/OBO paths (§9b/§9e) that need the real hostname.
5. **Done.** Main dev is net10. Delete the staging slot if you used A and don't need it (`az webapp deployment slot delete -g rg-spaarke-dev -n spaarke-bff-dev --slot staging`) — it shares the P1v3 single instance.

### Broadcast message (send to the 4–5 worktrees)
> **dev is now .NET 10.** Before your next BFF build/deploy: (1) install the **.NET 10 SDK** (`global.json` pins 10.0.100 → without it you get NETSDK1045); (2) in your worktree, `git fetch origin && git merge origin/master` to pick up net10 (TFMs, package alignment, pin removals). After that your builds/deploys are net10-compatible with dev. Until you merge, keep coding on net8 — just don't deploy net8 to dev (it'll 503).

---

## Reactivating an OLD / dormant worktree to .NET 10

When you pick a stale net8 branch back up after the cutover:

1. **Install the .NET 10 SDK** on the machine (once). Verify: `dotnet --list-sdks` shows a `10.0.1xx`.
2. **Pull net10 in**: from the worktree, `git fetch origin && git merge origin/master` (or rebase onto master). This brings:
   - `global.json` → 10.0.100; every `.csproj` TFM `net8.0` → `net10.0`
   - package alignment (Extensions 10.0.x, **Graph 6.5 / Kiota 2.0**, MSAL/Identity.Web bumps), removed CVE pins
   - CI workflow bumps (`setup-dotnet@v6` 10.x), hit-site fixes (H1/H2/H3), ArchTests changes
3. **Resolve merge conflicts** — for a dormant branch they cluster in `.csproj` (both sides changed package versions) and occasionally `Program.cs` / DI modules. Rule of thumb: **take the net10 side for framework/package versions**, then re-apply your branch's functional intent on top.
4. **Restore + build**: `dotnet restore` then `dotnet build -c Release`. Fix any call sites the bumped packages changed — in practice the only real one is **Microsoft.Graph 5→6.5 / Kiota 1→2** (`ServiceException` is retained; Kiota now throws `ODataError`; see this project's task 033 + `notes/graph6-kiota2-break-assessment.md` for the exact patterns). Everything else is a transparent recompile.
5. **Run the tests** for the areas you touched (or the full suite) to confirm green on net10.
6. Now the branch builds/deploys net10 — compatible with the flipped dev runtime.

**If a dormant branch must ship a net8 hotfix urgently AFTER the flip**: either (a) merge master to make it net10 then deploy (preferred), or (b) temporarily roll dev back to net8 (Option A rollback / Option B swap-back), deploy the hotfix, then re-cut to net10. Don't leave dev straddling both.

---

## Why this is low-risk now

- net10 is **already validated in the real Azure runtime** (slot smoke — `051-smoke-result.md`): DI graph, Graph 6.5, packages, auth all work on `DOTNETCORE-10.0.9`.
- The retarget is a **merge, not a rewrite** — TFM + package moves + a handful of hit-sites, all done and green on this branch.
- Rollback is always available (runtime string + last net8 artifact, or slot swap-back).
- Only **4–5 worktrees** to notify; each is a one-time `merge origin/master` + SDK install.
