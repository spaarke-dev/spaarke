# Phase 4 EXECUTION-LOG

> Per-task entries for every Phase 4 candidate (Outcome A SAFE → Outcome B MEDIUM → Outcome E facade).
> Each entry records baseline-vs-post metrics, deploy outcome, and bake-window status.

---

## Task 040 — Publish with `<RuntimeIdentifier>linux-x64</RuntimeIdentifier>` framework-dependent (FR-A1)

**Status**: Deployed 2026-05-25; 24h bake in flight (closes 2026-05-26 UTC).
**Commit**: [`d49adb69`](../../) — `feat(sdap-bff-api-remediation): FR-A1 publish linux-x64 framework-dependent (task 040)`
**Files changed**: `src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj` (added `<RuntimeIdentifier>linux-x64</RuntimeIdentifier>` + `<SelfContained>false</SelfContained>`)

### Size delta (publish-040 vs Phase 3 baseline)

| Metric | Phase 3 baseline | Post-task 040 | Delta |
|---|---:|---:|---:|
| Uncompressed publish | 212.5 MB | 147 MB | **-65.5 MB (-31%)** |
| File count | 287 | 279 | -8 |
| Zip (compressed) | 72.9 MB | 47.08 MB | **-25.8 MB (-35%)** |
| deps.json entries | 526 | 268 | -49% |
| `runtimes/` directory | 10 RIDs (~77 MB) | **eliminated** | -100% |

Result exceeded POML expected savings (~25–30 MB uncompressed, ~10 MB compressed) by ~2×.

### Smoke (synthetic baseline post-deploy)

`scripts/Capture-BffBaseline.ps1` against `spaarke-bff-dev`: 3,230 probes, 410 s runtime.

| Status | Phase 3 baseline | Post-deploy | Delta | Within ±5%? |
|---|---:|---:|---:|:---:|
| 200 | 80 | 80 | 0 | ✅ |
| 400 | 30 | 40 | +10 | ✅ (0.3% of total) |
| 401 | 1320 | 1310 | -10 | ✅ |
| 404 | 1790 | 1790 | 0 | ✅ |
| 429 | 10 | 10 | 0 | ✅ |
| Avg P95 latency | 133 ms | 134 ms | +0.75% | ✅ (within ±10%) |

Output: [`baseline/task-040-post-deploy.json`](baseline/task-040-post-deploy.json).

### Reflection-load probe (deps.json delta vs `baseline/reflection-probe-baseline.txt`)

- Total deps entries: 526 → 268 (-49%) — multi-platform native package METADATA refs trimmed (corresponding `runtimes/{rid}/` binaries no longer published; Linux App Service binds to OS-installed OpenSSL etc.)
- All 4 KEEP packages confirmed present in new deps.json:
  - `Microsoft.Agents.AI/1.0.0-rc1` ✅
  - `Microsoft.Agents.Hosting.AspNetCore/1.0.1` ✅
  - `Microsoft.Extensions.Http.Polly/8.0.8` ✅
  - `OpenTelemetry/1.15.0` ✅
- 37 `runtime.*` metadata entries remain (down from 100+); these are non-binary metadata refs that the asset resolver uses to look up RID-specific natives — harmless on Linux.

### Deploy outcome

`scripts/Deploy-BffApi.ps1 -AppServiceName spaarke-bff-dev -ResourceGroupName rg-spaarke-dev -SubscriptionId 484bc857-3802-427f-9ea5-ca47b43db0f0`:
- Build: 17 warnings (matches Phase 3 baseline exactly — no NFR-09 regression)
- Package: 47.08 MB
- Hash-verify: 4/4 critical files match SHA-256 (no Windows file-lock failure)
- Healthz: passed within default 120 s window

### Acceptance criteria

- ✅ `publish/runtimes/` contains only `linux-x64/` OR no native runtime folders → ELIMINATED
- ✅ Zero win-x64/osx-x64/osx-arm64/linux-musl-x64/linux-arm64 subdirs
- ⏭️ Test pass count matches Phase 3 baseline ±5% — N/A (Phase 3 finding: 69 pre-existing compile errors in test project; falling back to smoke probe acceptance per current-task.md decision log)
- ✅ Build warning count ≤ Phase 3 baseline → 17 == 17
- 🔄 24h dev bake: zero new exception types — IN FLIGHT (started 2026-05-25; closes 2026-05-26)
- ✅ P95 latency within 10% per endpoint vs baseline → +0.75% avg
- ✅ Reflection-load probe matches baseline (or diff accounted for) → 4 KEEP present; reduction explained
- ✅ Size delta documented in EXECUTION-LOG.md → this entry

### Notes

- Deploy script defaults still reference deleted `spe-api-dev-67e2xz` — invoked with explicit `-AppServiceName`/`-ResourceGroupName`/`-SubscriptionId` overrides. Default-update is out of scope for task 040; track for separate cleanup.
- `<SelfContained>false</SelfContained>` made explicit alongside the RID to prevent accidental self-contained publishes when a future `dotnet publish` call lacks `--no-self-contained`. Framework-dependent is canonical per spec FR-A1 + ADR-001.
