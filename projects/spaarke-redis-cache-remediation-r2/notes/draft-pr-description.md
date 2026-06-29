# Draft PR Description — spaarke-redis-cache-remediation-r2

> **Operator-use only**: this is a skeleton for the PR body. The operator opens the PR; this file is a starting point, not a substitute for operator judgment.

---

## Title (suggested, ≤70 chars)

```
feat(redis): cache observability + key rotation automation + IaC closure (R2)
```

---

## Body

```markdown
## Summary

R2 is the closure-of-closure for `spaarke-redis-cache-remediation-r1` — operationalizes the R7-S7 senior-review backlog without re-architecting. Three coherent themes ship as one combined PR per spec NFR-01.

- **Theme A — Cache observability hardening** (FR-01..06, tasks 001-006)
  - `cache.failures` Counter with `op` + `outcome` tags emitted from try/catch in `MetricsDistributedCache` (5-outcome `ClassifyException`: timeout/canceled/connection/serialization/other)
  - **Meter consolidation** — `CacheMetrics` promoted to canonical static class; single `Meter("Sprk.Bff.Api.Cache")` instance; 6 consumer files simplified (DI registration removed, nullable `CacheMetrics? metrics = null` ctor params dropped)
  - `cache.hits.by_resource` + `cache.misses.by_resource` Counters at `TenantCache` wrapper layer (`resource` dimension, cardinality bounded ~10-20 per NFR-06)
  - **3 Bicep-deployed cache alerts** (memory metric + hit-rate scheduledQuery + P95 scheduledQuery) — `infrastructure/bicep/alerts.bicep` NEW + `Deploy-RedisCache.ps1 -DeployAlerts` flag
  - Decorator regression integration test (`MetricsDistributedCacheRegistrationTests`) — asserts decorator wrapping + single-Meter invariant via `MeterListener`
  - `UseAzureMonitor()` fails-open guard extracted to `AzureMonitorGuard.ShouldWireExporter` (Production env + missing conn string → throw at startup with actionable message; Development preserves dev-convenience pass-through) — 9 unit tests

- **Theme B — Redis key rotation automation** (FR-07..11, tasks 010-014)
  - `scripts/Rotate-RedisKey.ps1` (~497 lines, exit codes 0/2/3-10) — safe-window algorithm: rotate Secondary → KV update → BFF restart → `/healthz` verify → ONLY THEN rotate Primary; rollback on healthz failure; App Insights ingestion via direct REST
  - `.github/workflows/redis-key-rotation.yml` — 3 separate jobs (env isolation, NOT matrix) on staggered quarterly cron `0 6 1/8/15 */3 *`; OIDC auth mirroring `deploy-bff-api.yml`; concurrency serialized + `cancel-in-progress:false`; KQL verification appended to `$GITHUB_STEP_SUMMARY`
  - Per-env OIDC SP isolation runbook §6.1 (binding: compromised prod SP can't rotate dev)
  - Runbook §6.2/6.3/6.4: automated as primary, manual as emergency fallback only, KQL verification
  - Missed-rotation alert (>100 days) added to `alerts.bicep` with isnull-arm fallback for never-rotated bootstrap

- **Theme C — R1 implementation gap closure** (FR-12..14, tasks 020-022)
  - `customer.bicep` Redis removal — module call + params + var + outputs deleted; LIVE `az deployment sub what-if` PRE/POST verified against fresh `rg-spaarke-whatif-prod` (no live customer affected per `az group list` audit)
  - `customer-template.bicepparam` / `demo-customer.bicepparam` / `demo-customer.json` cleanup
  - `SPAARKE-DEPLOYMENT-GUIDE.md` §4.6 strikethrough/footnote cleanup

## Verification

- **Build**: `dotnet build src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj` → 0 errors, 0 warnings
- **Publish-size delta** (NFR-04 ≤+0.5 MB): **+0.01 MB** apples-to-apples vs R1 close-out (R1 46.67 MB → R2 46.68 MB post-master-sync). Pre-merge measurement of 49.66 MB was confounded by 50 master commits from `spaarke-ai-azure-setup-dev-r1` that net-reduced size by ~3 MB; see `projects/spaarke-redis-cache-remediation-r2/notes/post-deploy-verification.md` for methodology + `notes/lessons-learned.md` for the methodology lesson.
- **Tests**: 1 integration test (FR-05) + 9 unit tests (FR-06) added; `§F.2 Fixture-Config-FIRST` fix to `RecallSessionFileHandlerResolvableTests.cs` (pre-existing `chat-routing-redesign-r1` task 091 break)
- **ADRs**: ADR-009 untouched (NFR-08); ADR-010 surface decreased (CacheMetrics static class removed DI registration + simplified 6 consumer ctors); ADR-029 publish-size verified; ADR-032 symmetric IConnectionMultiplexer DI preserved; ADR-038 KEEP-path placement + naming + no banned antipatterns
- **`az bicep build`**: PASS for `alerts.bicep` + `customer.bicep` + bicepparams
- **`az deployment sub what-if`** (Theme C live verification): NO Redis in plan for fresh customer; no live-customer rollback exposure
- **PSScriptAnalyzer**: clean on `Rotate-RedisKey.ps1`

## Test plan

Operator post-merge verification (PARTIAL: ⏸ at task 030; see `projects/spaarke-redis-cache-remediation-r2/notes/post-deploy-verification.md` for runbook):

- [ ] Deploy `alerts.bicep` to dev via `pwsh ./scripts/Deploy-RedisCache.ps1 -DeployAlerts -Environment dev -ActionGroupResourceId "<on-call-AG-id>"`
- [ ] Deploy BFF API to dev; wait ~10 min for App Insights propagation
- [ ] KQL verification: confirm `cache.hits`, `cache.misses`, `cache.failures`, `cache.hits.by_resource`, `cache.misses.by_resource` in `customMetrics`
- [ ] Force-reboot dry-run: confirm `customEvents | where name == "RedisKeyRotation"` baseline event appears (`gh workflow run redis-key-rotation.yml -f environment=dev -f mode=dry-run`)
- [ ] After 24 hours: confirm 3 cache alerts + 1 missed-rotation alert evaluating without firing (healthy state)

## Issues closed / commented

- Closes [#462 DEF-001](https://github.com/spaarke-dev/spaarke/issues/462) — Redis key rotation automation (Theme B replaces ACR Premium spend)
- Closes #483 / #484 / #485 (filed by task 031 as R2 operator follow-ups)
- Comments + flips R1 `defer-issues.md` items DEF-007 / DEF-008 / DEF-009 from "open" to "closed in R2"

## Lessons learned

See `projects/spaarke-redis-cache-remediation-r2/notes/lessons-learned.md` for full retrospective. Notable: the publish-size methodology trap (pre-master-sync measurement showed +2.99 MB / 6× ceiling; post-sync showed +0.01 MB) — recommended addition to `.claude/constraints/azure-deployment.md` per Recommendation §4.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
```
