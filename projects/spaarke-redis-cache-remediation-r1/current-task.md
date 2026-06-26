# Current Task State — spaarke-redis-cache-remediation-r1

> **Last Updated**: 2026-06-26 13:25 UTC (by context-handoff)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | R7-S7 inline fix — wire OpenTelemetry → Azure Monitor exporter (Path A) |
| **Step** | 1 of 7: Add `Azure.Monitor.OpenTelemetry.AspNetCore` package and replace `AddApplicationInsightsTelemetry()` |
| **Status** | in-progress |
| **Next Action** | Add `<PackageReference Include="Azure.Monitor.OpenTelemetry.AspNetCore" Version="1.4.0" />` to `src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj` (after the Redis OTel package, in the Observability block); then edit `src/server/api/Sprk.Bff.Api/Program.cs:16` — replace `builder.Services.AddApplicationInsightsTelemetry();` with `builder.Services.AddOpenTelemetry().UseAzureMonitor();` |

### Files Modified This Session (committed)

- `a0cf1df5a` — Meter rename `Spaarke.Cache` → `Sprk.Bff.Api.Cache` + Redis OTel instrumentation registration
  - `src/server/api/Sprk.Bff.Api/Infrastructure/Cache/TenantCache.cs`
  - `src/server/api/Sprk.Bff.Api/Infrastructure/DI/TelemetryModule.cs`
  - `src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj`
  - `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/Cache/TenantCacheMetricsTests.cs`
  - `projects/spaarke-redis-cache-remediation-r1/notes/r7-backlog.md` (added S6 + S7)
  - `projects/spaarke-redis-cache-remediation-r1/notes/cutover-deploy-log.md` (post-deploy validation matrix)
- `9bbfc7f0c` — AI-Search handoff doc at `projects/spaarke-redis-cache-remediation-r1/notes/handoff-to-ai-search-project.md`

### Critical Context

The user authorized inline R7-S7 (Path A: replace classic SDK with `Azure.Monitor.OpenTelemetry.AspNetCore`'s `UseAzureMonitor()`) to honor spec FR-16 + Success Criterion #8 ("App Insights captures Redis dependency calls; cache hit rate metric visible in dashboards"). The user's directive: "we can't close this project until we get everything fully tested and validated."

**Root cause being fixed**: BFF uses classic `AddApplicationInsightsTelemetry()` (auto-instruments HTTP/ServiceBus/KeyVault but NOT StackExchange.Redis) + has OTel pipeline configured for 12 Meters and several ActivitySources **but NO exporter wired to Azure Monitor**. My commit `a0cf1df5a` registered the Redis OTel instrumentation + aligned the Cache Meter name — those activate immediately upon exporter wiring.

**Live infrastructure state (already validated)**:
- `spaarke-bff-redis-dev` (Basic C0) running in `spe-infrastructure-westus2` since 2026-06-26T11:18 UTC
- KV secret `Redis-ConnectionString` in `spaarke-spekvcert` (NOT `sprkspaarkedev-aif-kv` — spec assumption was correct)
- `spaarke-bff-dev` App Settings: `Redis__Enabled=true`, `Redis__InstanceName=spaarke:`, KV ref, `Redis__AllowInMemoryFallback=false` — all KV refs status "Resolved"
- BFF deployed at 2026-06-26T12:48 UTC with the Meter rename + Redis OTel registration
- Startup log at 12:24:57 + 12:48 UTC confirms: `"Distributed cache: Redis enabled with instance name 'spaarke:'"`
- Live Redis traffic: ~800-1000 commands/min via Azure Monitor resource metric `totalcommandsprocessed`
- Legacy `spe-redis-dev-67e2xz` TAGGED `decommission=2026-06-26` (NOT deleted; 7-14 day reversibility window)

**Project state on origin**:
- PR #458 MERGED to master at commit `567b98112` (this carried Phase 1+2+3+5 from the initial commits up through `dfc442567`)
- Subsequent commits `9bbfc7f0c` + `a0cf1df5a` are on `work/spaarke-redis-cache-remediation-r1` branch ONLY — NOT on master yet
- Will need a follow-up PR to merge handoff doc + Meter rename + Redis OTel + R7-S7 fix onto master

**User-flagged item still open**: `spe-insights-dev-67e2xz` App Insights still has the legacy off-pattern name. Added to R7 backlog as S6 (rename to `spaarke-bff-insights-dev`). Out of scope for this project but noted for the sister project or its own follow-up.

---

## Full Plan — R7-S7 Inline Fix

### Step 1 (NEXT): Add package + replace SDK call

1. `src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj` — add inside the Observability block (after the StackExchangeRedis line at ~line 96):
   ```xml
   <!-- spaarke-redis-cache-remediation-r1 R7-S7 closure: wire the OTel pipeline to
        Azure Monitor so the 12 registered Meters + AddRedisInstrumentation() actually
        flow to App Insights. Replaces classic AddApplicationInsightsTelemetry() in Program.cs. -->
   <PackageReference Include="Azure.Monitor.OpenTelemetry.AspNetCore" Version="1.4.0" />
   ```
2. `src/server/api/Sprk.Bff.Api/Program.cs:14-16` — replace:
   ```csharp
   // Application Insights telemetry
   builder.Services.AddApplicationInsightsTelemetry();
   ```
   with:
   ```csharp
   // OpenTelemetry → Azure Monitor (replaces classic Application Insights SDK).
   // Per spaarke-redis-cache-remediation-r1 R7-S7 (2026-06-26): the classic SDK
   // didn't auto-instrument StackExchange.Redis and the OTel pipeline had no
   // exporter — neither Redis dependency telemetry nor the 12 Sprk.Bff.Api.* Meters
   // reached App Insights. `UseAzureMonitor()` wires both pipelines to the same
   // App Insights resource pointed at by APPLICATIONINSIGHTS_CONNECTION_STRING.
   builder.Services.AddOpenTelemetry().UseAzureMonitor();
   ```

### Step 2: Build + run BFF tests

```
dotnet build src/server/api/Sprk.Bff.Api/
dotnet test tests/unit/Sprk.Bff.Api.Tests/ --nologo
```

Expect 0 errors + 7886 pass / 1 pre-existing fail / 135 skip. Investigate any new failures (potential telemetry-shape regression in existing telemetry-asserting tests).

### Step 3: Deploy

```
pwsh -ExecutionPolicy Bypass -File scripts/Deploy-BffApi.ps1
```

Expect hash-verify 4/4 + healthz 200.

### Step 4: Wait + generate traffic + verify telemetry flushes

Wait ~2-3 min after deploy. Hit `/healthz`, `/ping` a few times. Then run KQL:

```kql
// Redis dependencies should appear (the AddRedisInstrumentation() from commit a0cf1df5a)
dependencies
| where timestamp > ago(10m)
| where type contains 'Redis' or name has_any ('SET', 'GET', 'EVAL', 'DEL', 'EXPIRE')
| summarize count() by type, name = substring(name, 0, 30)

// Custom cache metrics should appear (Sprk.Bff.Api.Cache Meter)
customMetrics
| where timestamp > ago(10m)
| where name startswith 'cache.'
| summarize count = count(), sum_value = sum(value) by name

// Existing pre-R7-S7 dependencies should still appear (HTTP, ServiceBus, etc. — regression check)
dependencies
| where timestamp > ago(10m)
| summarize count() by type
| order by count_ desc
```

`az` invocation:
```
az monitor app-insights query -g spe-infrastructure-westus2 -a spe-insights-dev-67e2xz --analytics-query "<query>" 2>&1 | head -30
```

### Step 5: Commit + push the R7-S7 fix

```
git add src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj src/server/api/Sprk.Bff.Api/Program.cs

git commit -m "feat(spaarke-redis-cache-remediation-r1): wire OTel → Azure Monitor exporter (R7-S7 / FR-16 closure)

Replaces classic AddApplicationInsightsTelemetry() with AddOpenTelemetry().UseAzureMonitor() —
the bridge that ships the BFF's 12 registered Meters + ActivitySources + AddRedisInstrumentation()
to App Insights. Closes spec FR-16 + Success Criterion #8 (App Insights captures Redis
dependency calls; cache hit rate metric visible in dashboards).

Validation: Redis SET/GET dependencies visible; cache.hits/cache.misses/cache.redis_call_duration_ms
custom metrics visible; existing HTTP/ServiceBus/KeyVault dependency capture preserved."

git push origin work/spaarke-redis-cache-remediation-r1
```

### Step 6: Open follow-up PR to land the post-merge commits

The branch already has `567b98112` merged into master via PR #458. Commits `9bbfc7f0c` + `a0cf1df5a` + (this turn's R7-S7 commit) need their own PR.

```
gh pr create --base master --head work/spaarke-redis-cache-remediation-r1 \
  --title "feat(spaarke-redis-cache-remediation-r1): AI-Search handoff + R7-S7 OTel exporter (FR-16 closure)" \
  --body-file c:\tmp\pr-body-redis-r2.md
```

PR body should mention: brings AI-Search handoff doc onto master + FR-16 closure (App Insights now captures Redis deps + custom cache metrics).

After CI green (or auto-merge), invoke `/merge-to-master` or `gh pr merge --auto --merge`.

### Step 7: Final close-out

1. Update `projects/spaarke-redis-cache-remediation-r1/README.md`:
   - Status: 100% Complete (was "92%")
   - Graduation criterion 8 (App Insights captures Redis deps) flip [ ] → [x] with verification timestamp
2. Update `projects/spaarke-redis-cache-remediation-r1/tasks/TASK-INDEX.md`:
   - 040, 042 (App Insights verification) — flip ⏸ PARTIAL → ✅ with R7-S7 commit SHA
   - 044 — already ✅
3. Update `projects/spaarke-redis-cache-remediation-r1/notes/cutover-deploy-log.md`:
   - Validation matrix: change Redis dependencies + custom cache metrics rows from ⏸ to ✅
4. Commit + push close-out
5. Suggest `git push origin --delete work/spaarke-redis-cache-remediation-r1` to user (don't do it without explicit ask)
6. Suggest `git worktree remove c:\code_files\spaarke-wt-spaarke-redis-cache-remediation-r1` to user

### Sister-project (`spaarke-ai-azure-setup-dev-r1`) — confirmed NOT blocked

Per spec NFR-11/13: gate is Redis Phase 3 cutover success — that's been DONE since 2026-06-26T11:21:39 UTC (initial) + revalidated 12:24:57 UTC + 12:48 UTC (post-deploy). The AI-Search project owner can start their work today. R7-S7 doesn't block them — but if they want to verify their own Phase 4 observability, they'd benefit from R7-S7 being on master first (1-line BFF change unblocks all 12 BFF Meters + future AI-Search metrics).

---

## Files committed-but-not-yet-on-master

These are on `work/spaarke-redis-cache-remediation-r1` at commit `a0cf1df5a` but NOT yet on master:

- `projects/spaarke-redis-cache-remediation-r1/notes/handoff-to-ai-search-project.md` — the AI-Search project's prerequisite-gate document (user asked for path)
- `src/server/api/Sprk.Bff.Api/Infrastructure/Cache/TenantCache.cs` (Meter rename to Sprk.Bff.Api.Cache + internal counters)
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/TelemetryModule.cs` (`AddRedisInstrumentation()` added)
- `src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj` (`OpenTelemetry.Instrumentation.StackExchangeRedis` added)
- `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/Cache/TenantCacheMetricsTests.cs` (explicit instrument enable; reordering)
- `projects/spaarke-redis-cache-remediation-r1/notes/r7-backlog.md` (S6 + S7 added)
- `projects/spaarke-redis-cache-remediation-r1/notes/cutover-deploy-log.md` (validation matrix + classic-SDK gap explanation)

## Resume command (post-compaction)

```
"continue task — finish R7-S7 inline"
```

or just:

```
"where was I?"
```

then follow Step 1 of the Full Plan above.
