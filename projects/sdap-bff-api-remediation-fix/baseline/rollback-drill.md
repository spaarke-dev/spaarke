# Rollback Drill — NFR-06 Verification

> **Task**: 009
> **Date**: 2026-05-24
> **Operator**: Project owner (operator-only model per NFR-08 revised)
> **Environment**: `spe-api-dev-67e2xz` (dev only per NFR-07)
> **Purpose**: Verify NFR-06 (<10 min rollback) under realistic conditions before Phase 4 begins (G5)

---

## Summary

| Metric | Value | NFR-06 Target | Status |
|---|---|---|---|
| Wall-clock: decision detected → reverted state healthz green | **2 min 23 sec** | < 10 min | ✅ **PASS** (~24% of budget) |
| Drill total duration (deploy + decision + revert + redeploy) | ~4 min | n/a | n/a |
| Auto-recover triggered? | YES (silent file-lock on 4 DLLs) | n/a | Hardened script worked as designed |
| Final state verified | `/ping`=`pong`, `/healthz`=200 | — | ✅ |

**NFR-06 status**: VERIFIED. Operator has wall-clock confidence in rollback path. Phase 4 bake-window regressions can be resolved well within budget.

---

## Drill procedure

1. Made trivial no-op change to `src/server/api/Sprk.Bff.Api/Infrastructure/DI/EndpointMappingExtensions.cs:79` — `/ping` response: `pong` → `pong-drill-2026-05-24`
2. Committed: `0c620068` "test(rollback-drill): no-op /ping response change for NFR-06 wall-clock verification"
3. Deployed via `.\scripts\Deploy-BffApi.ps1` (deploy 1)
4. Verified `/ping` returns `pong-drill-2026-05-24` via curl
5. Noted "decision detected" timestamp (simulated regression)
6. Ran `git revert HEAD --no-edit` (auto-message: "Revert ...")
7. Redeployed via `.\scripts\Deploy-BffApi.ps1` (deploy 2 — the timed cycle)
8. Verified `/ping` returns `pong` + `/healthz` returns 200
9. Captured end timestamp

---

## Timestamps (ISO-8601)

| Marker | Time | Δ vs prior | Note |
|---|---|---|---|
| T0 — Drill deploy 1 start | 2026-05-24T16:17:41-04:00 | — | Deploy with drill change |
| T1 — Drill deploy 1 complete | 2026-05-24T16:18:59-04:00 | +1m 18s | `/ping`=`pong-drill-2026-05-24` verified live |
| **T2 — Decision detected** | 2026-05-24T16:19:12-04:00 | +13s | Simulated regression; **clock starts for NFR-06** |
| T3 — Revert complete | 2026-05-24T16:19:13-04:00 | +1s | `git revert HEAD --no-edit` |
| T4 — Redeploy start | 2026-05-24T16:19:23-04:00 | +10s | `Deploy-BffApi.ps1` start |
| **T5 — Reverted state live** | 2026-05-24T16:21:35-04:00 | +2m 12s | `/ping`=`pong` + `/healthz`=200 verified; **clock stops for NFR-06** |

**NFR-06 measured**: T5 − T2 = **2 min 23 sec**.

---

## Deploy 2 (the timed cycle) — breakdown

```
[1/4] Building API in Release mode...                          ~25s
[2/4] Creating deployment package... (75.2 MB)                 ~5s
[3/4] Deploying directly to App Service... (success returned)  ~30s
[4/4] Verifying file replacement on server...
  Deploy reported success BUT 4 file(s) were not replaced:
    - Spaarke.Core.dll
    - Sprk.Bff.Api.exe
    - Sprk.Bff.Api.dll
    - Spaarke.Dataverse.dll
  Auto-recovering: stop -> zipdeploy via Kudu -> start...      ~45s
  Auto-recover succeeded - all files now match local build
[5/4] Verifying health endpoint...
  dev health check passed! (attempt 1/24)                      ~3s
```

Total Deploy 2 = ~132s (2m 12s) including auto-recover overhead.

---

## Friction observed

| Friction | Detail | Mitigation |
|---|---|---|
| Silent file-lock failure on redeploy | Windows file lock on 4 DLLs caused `az webapp deploy --type zip` to return 200 + success while DLLs not replaced | **Handled** — `Deploy-BffApi.ps1` hash-verify caught it; auto-recover (stop → Kudu zipdeploy → start) succeeded. This is exactly the FAILURE-MODES G-2 scenario the hardened script was designed for. |
| Linux App Service cold-start | First `/healthz` attempt was successful (1/24) | None needed — likely warm from deploy 1 ~3 min earlier. 120s tolerance still applies for cold starts. |

**No unmitigated friction.** Operator's rollback path is reliable end-to-end.

---

## Cleanup

- Drill commit `0c620068` (drill change) and revert commit `ba810663` (Revert "...") are intentionally left in branch history as drill provenance.
- Net branch effect: zero (revert commit cancels drill commit).
- No state change to dev environment beyond ~4 minutes of `/ping`=`pong-drill-2026-05-24` between T1 (16:18:59) and T5 (16:21:35).

---

## NFR-06 verification per acceptance criteria

| Criterion | Result |
|---|---|
| Wall-clock from "decision" to "healthz green on reverted state" recorded | ✅ T2 → T5 = 2m 23s |
| Elapsed ≤ 10 minutes (NFR-06 pass) OR mitigation documented | ✅ PASS (24% of budget) |
| Drill executed by project owner (operator-only model per NFR-08 revised) | ✅ Single-operator drill |
| `baseline/rollback-drill.md` committed with timestamps + friction notes | ✅ This file |

---

## Confidence statement

The operator has executed the rollback path end-to-end within the 24-hour window before Phase 4 starts. The drill proved:

1. **Hash-verify works.** Silent file-lock failure was caught immediately rather than reaching the App Insights bake-window observation phase as a real regression.
2. **Auto-recover works.** Stop → Kudu zipdeploy → start cycle replaced the locked DLLs cleanly.
3. **Total wall-clock is well within NFR-06 budget.** Even with auto-recover overhead, full rollback completed in 2m 23s.
4. **Hardened `Deploy-BffApi.ps1` is production-ready** for Phase 4 + Phase 5 use.

**Phase 4 can proceed without rollback-path uncertainty.**
