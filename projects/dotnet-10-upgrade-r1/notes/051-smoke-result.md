# Task 051 — Phase 1 slot smoke result (net10 on `spaarke-bff-dev-staging`)

> **Date**: 2026-08-13 · **Executed by**: Claude (main session, at owner request) on the ISOLATED staging slot. Main dev untouched throughout (stayed `DOTNETCORE|8.0`). **No swap.**

## VERDICT: ✅ GO (net10 validated) — with two required slot-config steps documented

The net10 BFF runs correctly on Azure Linux App Service. Both startup blockers were **slot-provisioning gaps, NOT .NET 10 issues.**

## What was validated (server-side smoke on the slot)

| Check | Result |
|---|---|
| Runtime | `DOTNETCORE-10.0.9` (net10 platform image `appsvc/dotnetcore:10.0`) |
| `/healthz` | **200**, stable across 4 hits over ~12 s |
| `/ping` | **200** → `pong` |
| Auth-protected route (`/api/documents/test/preview-url`) | **401** (route registered on net10, auth middleware working — not 404/503) |
| Full DI startup | app builds the entire host — CacheModule/Redis, ServiceBus, Graph 6.5, Dataverse all constructed once KV refs resolved (implicitly exercises §9c/§9d service wiring) |

This is real-runtime confirmation of the whole retarget: H1/H2/H3 hit-sites, Graph 6.5/Kiota 2.0, package alignment, and the DI graph all function on net10 Linux.

## Root cause of the initial crash (exit 134 / SIGABRT) — NOT net10

Startup log stack trace:
```
System.InvalidOperationException: Failed to parse Redis connection string...
 ---> System.ArgumentException: Keyword '@Microsoft.KeyVault(VaultName' is not supported
   at Sprk.Bff.Api.Infrastructure.DI.CacheModule.AddCacheModule(...) CacheModule.cs:line 75
   at Program.<Main>$ Program.cs:line 128
```
The freshly-created slot had `keyVaultReferenceIdentity = SystemAssigned` (the clone reset it), so every `@Microsoft.KeyVault(...)` app setting (Redis, ServiceBus, Graph cert) resolved to the **literal string**, and StackExchange.Redis aborted parsing it. The .NET 10 runtime (10.0.9) had loaded fine and the app got all the way through host build to the cache module.

## Fixes applied to the slot (both slot-provisioning, reversible)

1. **`keyVaultReferenceIdentity` → user-assigned MI `mi-bff-api-dev`** (was `SystemAssigned`). THE fix — after this + restart, `/healthz` = 200. Set via `az resource update --ids <slotId> --set properties.keyVaultReferenceIdentity=<mi>` from PowerShell.
2. **Classic App Insights codeless agent disabled** (`ApplicationInsightsAgent_EXTENSION_VERSION`/`DiagnosticServices_EXTENSION_VERSION`/`XDT_...Mode` = `disabled`). Precautionary + FR-06-aligned (OTel is the sole telemetry path; the `~2` agent predates net10). *Not independently confirmed to be a blocker — KV was the proven cause — but correct end-state for net10.*

Both are now baked into the helper `deploy-net10-slot-phase1.ps1` (provision block) and `051-operator-runbook.md` Step 2b/2c.

## Implication for the real cutover (Phase 2 swap)

**The KV-identity fix is a validation-slot artifact, not a cutover risk.** Main dev already has `keyVaultReferenceIdentity` = the MI, so a production swap doesn't need step 2b. The codeless-AI-disable, however, **should be applied to main dev before/at cutover** (it swaps with the slot settings, so disabling on the slot → after swap main dev gets it disabled too — the correct net10 end-state).

## Remaining smoke best done by the operator (browser/token paths)

- §9b OBO round-trip + §9e browser MSAL — need a Spaarke client pointed at the slot hostname; server-side auth-route smoke (401) already passed.
- §9c MI→Dataverse with a real doc id, §9d EXO mailbox — services wired successfully at startup; a live request pair would fully confirm.

## Slot state now

`spaarke-bff-dev-staging` is running net10, healthy, **not swapped**. Leave it for Phase-2 swap, or delete to free plan compute: `az webapp deployment slot delete -g rg-spaarke-dev -n spaarke-bff-dev --slot staging`.
