# Task 051 — coordinating the net10 dev cutover with 13+ active BFF worktrees

> **Date**: 2026-08-13 · Owner strategic question: "other projects build/deploy BFF.API — hold 051 or hold those projects? how do they update?"

## The core conflict (why this needs coordination)

`spaarke-bff-dev` is a **single shared Linux App Service** (P1v3, capacity 1, no slot — 050 evidence). Its runtime string is one global setting:
- Today: `DOTNETCORE|8.0`. Every active BFF worktree deploys **net8** artifacts here.
- This project needs `DOTNETCORE|10.0` + a **net10** artifact.

**A net8 binary on a net10 runtime = hard 503; a net10 binary on a net8 runtime = hard 503.** The two TFM worlds cannot coexist on the same slot. `Deploy-BffApi.ps1` / `az webapp deploy --type zip` pushes only the **binary** — it does NOT change `linuxFxVersion` — so whichever project deploys against a mismatched runtime string breaks.

## Current state is SAFE (nothing conflicts today)

This branch is **NOT merged to master** (deferred to P5). Master is net8. All other worktrees branch off master → their `global.json` is 8.0.x, they build net8 with the 8.0 SDK, dev runtime is net8. **No conflict exists until we flip dev's runtime or merge net10 to master.** So there is no fire to put out right now.

## The answer: you don't have to hold EITHER — use a staging slot for 051

050 confirmed P1v3 **supports deployment slots** and West US 2 **offers `DOTNETCORE:10.0`**. So:

1. **Create a `staging` slot** on `spaarke-bff-dev`, set the slot runtime to `DOTNETCORE|10.0`, deploy the **net10** artifact to the **slot only**, run the 051 smoke against the slot hostname.
   - The **main dev slot stays `DOTNETCORE|8.0`** → the other 13 worktrees keep building and deploying net8 to dev with **zero impact**. Nobody is held.
   - This gets the 051 go/no-go evidence in isolation.
2. The **actual cutover** (flip the *main* dev slot to net10) is then a **separate, coordinated event** — the swap — done at the moment we also merge net10 to master. That single swap is atomic (runtime + binary together) and reversible (swap back = instant rollback).

**Without a slot** (deploy net10 straight to main dev): you WOULD have to freeze all other projects' dev deploys during/after 051, because their net8 deploys would 503. The slot is what avoids the freeze. **Recommendation: create the slot.**

## How the other projects update (the mechanism)

The trigger that moves everyone to net10 is the **branch→master merge** (net10 becomes the master baseline). After it, each active BFF worktree:

1. **Installs the .NET 10 SDK** locally (one-time, per developer machine). `global.json` pins `10.0.100` (rollForward `latestFeature`) — without the SDK the build hard-stops with **NETSDK1045**. CI is already handled (task 040 → `setup-dotnet@v6` 10.x).
2. **Merges master into its branch**: `git fetch origin && git merge origin/master` (or `/worktree-sync` Update Only). This pulls net10 TFMs, `global.json` 10.0.100, package alignment (Extensions 10.0.x, Graph 6.5/Kiota 2.0, MSAL/Identity.Web bumps), and the removed CVE pins.
3. Rebuilds — now produces **net10** artifacts compatible with the (post-swap) net10 dev runtime.

`projects/INDEX.md` + `/conflict-check` is the coordination surface listing the BFF worktrees to notify.

## Ordering constraint (to avoid 503s during the window)

- Do **NOT** flip the *main* dev runtime to net10 until master is net10 **and** the active worktrees are ready to merge it. Otherwise their next net8 deploy 503s (or they can't deploy without merging first).
- The clean sequence: **(a)** re-sync master into this branch (~40 commits landed since the last sync) + re-verify green → **(b)** merge net10 → master → **(c)** broadcast to the 13 worktrees: "install .NET 10 SDK + merge master before your next BFF deploy; dev is going net10" → **(d)** swap the dev slot (main dev now net10) → **(e)** worktrees merge master at their own pace; from then on their deploys are net10.
- Rollback at any point = **swap back** (main dev returns to net8 instantly) until everyone has migrated.

## Net recommendation

- **051 now, on a staging slot** → validates net10 in isolation, holds nobody, breaks nobody.
- **Main-dev flip (the swap) = the coordinated cutover**, done together with branch→master merge + a heads-up to the 13 worktrees.
- **Other projects update by**: install .NET 10 SDK + `git merge origin/master`. That's the whole mechanism — it's a merge, not a rewrite (the retarget is TFM + package moves + a handful of hit-sites, all already done and green on this branch).
