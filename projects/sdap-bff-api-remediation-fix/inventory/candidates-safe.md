# SAFE-Tier Candidates (Task 020)

> **Captured**: 2026-05-24
> **Definition (per design.md §6 Phase 2)**: SAFE = affects deploy artifact only; no source code change; no runtime behavior change. Examples: `--runtime linux-x64`, exclude `*.js.map` from zip, exclude `runtimes/win-x64/` from copy.
> **Approval**: Owner ACK
> **Context**: Phase 1 finding 1 RESOLVED — dev migrated to Linux App Service (`spaarke-bff-dev`) on 2026-05-24. All 3 envs now Linux. FR-A1 (`--runtime linux-x64`) applies cleanly across all envs.

---

## SAFE-1 — Publish with `--runtime linux-x64` (FR-A1)

| Field | Value |
|---|---|
| **Cost** | ~67 MB uncompressed saved (per `native-binaries.md`). Compressed savings ~15-20 MB. |
| **Evidence of waste** | All 3 target envs are now Linux (dev migrated 2026-05-24). 10 RIDs currently shipped: `browser`, `linux-arm64`, `linux-musl-x64`, `linux-x64`, `osx-arm64`, `osx-x64`, `unix`, `win`, `win-x64`, `win-x86`. Only `linux-x64` is needed. |
| **Action** | Modify `Sprk.Bff.Api.csproj`: add `<RuntimeIdentifier>linux-x64</RuntimeIdentifier>` OR pass `--runtime linux-x64` to `dotnet publish` in Deploy-BffApi.ps1 + CI workflow. **Note**: Sprk.Bff.Api.csproj currently has NO `<RuntimeIdentifier>` — this is a NEW publish flag, not a modification. |
| **Test plan** | (a) Confirm publish output contains ONLY `runtimes/linux-x64/`; (b) Deploy to dev (now Linux); (c) Verify `/healthz` 200 + `/ping` pong + auth-protected `/api/documents/test/preview-url` returns 401; (d) Smoke test all 7 functional domains per `auth-deployment-setup.md` §9; (e) 24h bake on dev — App Insights P95 latency within 10% of Phase 3 baseline. |
| **Rollback** | `git revert` on the csproj/script commit + redeploy. ~3 min wall-clock per rollback drill (NFR-06 verified). |
| **Tier rationale** | SAFE-tier per design definition: publish-time config only; no source code change; no runtime behavior change. Native binary subset is functionally equivalent for the target OS. |
| **Phase 4 task** | 040 |

---

## SAFE-2 — Exclude `*.js.map` from publish (FR-A2)

| Field | Value |
|---|---|
| **Cost** | ~5-7 MB uncompressed saved (4 files in `wwwroot/playbook-builder/assets/`). Compressed savings ~2-3 MB. |
| **Evidence of waste** | Per `wwwroot-assets.md`: `flow-vendor-BHHmI87s.js.map`, `fluent-vendor-CmJVTK5h.js.map`, `index-BWeOj5bW.js.map`, `react-vendor-BWFb42Va.js.map`. Sourcemaps are debugging artifacts; not needed for prod execution. |
| **Action** | Add to `Sprk.Bff.Api.csproj`: `<ItemGroup><Content Update="wwwroot\**\*.js.map" CopyToPublishDirectory="Never" /></ItemGroup>`. |
| **Test plan** | (a) Confirm `find deploy/api-publish/wwwroot -name "*.map"` returns 0; (b) Deploy to dev; (c) Verify `/healthz` + playbook-builder loads in browser (manual smoke); (d) 24h bake. |
| **Rollback** | `git revert` on the csproj commit + redeploy. |
| **Tier rationale** | SAFE: publish exclusion; no runtime impact (sourcemaps are inert on server). |
| **Phase 4 task** | 041 |

---

## SAFE-3 — ~~Remove duplicate Cosmos ServiceInterop.dll~~ (FR-A3) — **NO-OP**

| Field | Value |
|---|---|
| **Status** | ✅ **ALREADY RESOLVED UPSTREAM** (Phase 1 Finding 4) |
| **Evidence** | `find deploy/api-publish -name "ServiceInterop.dll"` returns 0 entries. Microsoft.Azure.Cosmos 3.47.0 no longer ships this DLL. |
| **Phase 4 task** | 042 — convert to verification-only step (`find` check + commit log entry confirming absence) |

---

## Combined SAFE-tier projected savings

| Metric | Current | After SAFE-1 + SAFE-2 | Delta |
|---|---|---|---|
| Uncompressed | 212 MB | ~140 MB | -72 MB (-34%) |
| Compressed | 75.2 MB | ~53 MB | -22 MB (-30%) |
| File count | 287 | ~245 | -42 (-15%) |

**Outcome A target (≤60 MB compressed)**: ✅ **achievable with SAFE-tier alone** (~53 MB projected) — no MEDIUM/HIGH tier removals needed for size goal.

---

## Phase 4 ordering recommendation (SAFE-tier track)

Per design §6 Phase 4 "Per-candidate procedure" with 24h dev bake between each:

1. **SAFE-1** (FR-A1, `--runtime linux-x64`) — first; biggest individual savings; deploy script changes are isolated
2. **SAFE-2** (FR-A2, sourcemap exclusion) — second; smaller delta; csproj change only
3. **SAFE-3** (FR-A3, ServiceInterop) — third; verification-only no-op; no actual deploy needed

Total SAFE-tier calendar: ~48-72h with bakes. Outcome A complete at end of SAFE-3.
