# Wave 12 Batch 4 — Combined Deploy + Smoke (T136 + T154)

> **Date**: 2026-06-30
> **Project**: spaarke-ai-platform-unification-r7
> **Branch**: `work/spaarke-ai-platform-unification-r7`
> **Author**: Claude Code (combined deploy + smoke session)
> **Tasks**: T136 (Daily Briefing 6-channel deploy + smoke + UAT prep) + T154 (Assistant↔Workspace UAT prep)
> **Target environment**: spaarkedev1 (rg-spaarke-dev / spaarke-bff-dev)
> **Operator UAT**: PENDING — this doc + UAT checklist (§7) handed off to operator

---

## 1. Pre-deploy gates — ALL GREEN

### 1.1 Build (`dotnet build src/server/api/Sprk.Bff.Api/ -c Release`)

| Metric | Value |
|---|---|
| Errors | **0** |
| Warnings | 19 (all pre-existing; 0 new from Wave 12 code) |
| Elapsed | 7.92 s |
| Status | ✅ **PASS** |

### 1.2 Tests (`dotnet test tests/unit/Sprk.Bff.Api.Tests/ -c Release --no-build`)

| Metric | Value |
|---|---|
| Total | 7,645 |
| Passed | **7,537** |
| Skipped | 101 |
| **Failed** | **7** |
| Status | ✅ **PASS** (zero new regressions; all 7 failures pre-existing baseline) |

**Pre-existing baseline failures (carried forward from Wave 1/2/3/audit 120)**:

| Test | Pre-existing per |
|---|---|
| `KnowledgeDeploymentConfigTests.KnowledgeDeploymentConfig_DefaultValues_AreCorrect` | Wave 1 sign-off |
| `SessionFilesCleanupJobTests.RunScheduledScanAsync_Evicts_Only_Orphans_Not_In_Active_Set` | Wave 1 sign-off |
| `SummarizeSessionEndpointContractTests.Post_HappyPath_PassesFileIdsAndStyleToOrchestrator` | Wave 3 sign-off (Wave 9 introduced) |
| `SummarizeSessionEndpointContractTests.Post_FeatureDisabled_Returns503_WithFeatureKey` | Wave 3 sign-off (Wave 9 introduced) |
| `SummarizeSessionEndpointContractTests.Post_HappyPath_StreamsSseAnalysisChunks` | Wave 3 sign-off (Wave 9 introduced) |
| `PlaybookDispatcherPhaseBTests.RunPhaseBVectorMatchAsync_ManifestAbsent_MeetsLatencyBudgetFor3Files` | Audit 120 baseline (timing-flaky) |
| `ExecutorConfigSchemasEndpointTests.GetExecutorConfigSchemas_PlaceholderExecutors_HaveEmptyFieldsAndNonEmptyDescription` | Wave 3 task 034 (Start node grew 2 description fields — known assertion mismatch; non-functional) |

Verified zero Wave 12 BFF commits (`git log master..HEAD`) modify any of the 7 failing-test source files (except `ExecutorConfigSchemasEndpointTests`, last R7 touch was Wave 3 commit `60fcb1366`, well before Wave 12).

### 1.3 Publish-size — NFR-01 (CLAUDE.md §10 hard ceiling 60 MB)

```
dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/
```

| Measurement | Value |
|---|---|
| Uncompressed publish | 142 MB (148,148,911 bytes) |
| Compressed (`Compress-Archive -CompressionLevel Optimal`) | **45.42 MB** (47,625,657 bytes) |
| Wave 4 baseline (last published handoff) | 46.72 MB |
| **Wave 12 single-batch delta vs Wave 4** | **-1.30 MB** (SHRINK) |
| Pre-R7 baseline (2026-05-26) | 45.65 MB |
| **Cumulative R7 delta vs pre-R7 baseline** | **-0.23 MB** (NET-NEGATIVE) |
| NFR-01 R7 cumulative budget (≤ +2 MB) | ✅ **PASS** (budget unused; 2.23 MB headroom) |
| 60 MB hard ceiling | ✅ **PASS** (14.58 MB headroom) |
| 55 MB architecture-review trigger | ✅ **PASS** (9.58 MB headroom) |
| ≥+5 MB single-task escalation threshold | ✅ NOT TRIPPED |

**Interpretation**: Wave 12 net-shrunk the BFF by 1.30 MB vs Wave 4 — Wave 12 added inline membership FetchXml + 6-entity collector code BUT also benefits from Wave 4's 524-LOC `ExecuteAnalysisAsync` deletion (Wave 4 measured FLAT in compressed bytes because shrink was IL-level, not wire-level; Wave 12's measurement now reflects accumulated post-Wave-4 cleanup). R7 net publish-size impact is now **negative**.

### 1.4 CVE scan — NFR-02 (no new HIGH-severity CVE)

```
dotnet list src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj package --vulnerable --include-transitive
```

| Metric | Value |
|---|---|
| HIGH-severity entries | **1** (`Microsoft.Kiota.Abstractions 1.21.2` — pre-existing) |
| Critical-severity entries | 0 |
| HIGH entries introduced by Wave 12 | **0** |
| Status | ✅ **PASS** (Kiota is documented accepted-risk per ADR-029 §4) |

---

## 2. Rollback tag

Created BEFORE deploy:

```
git tag -a deploy/spaarkedev1/pre-wave12-batch4 \
  -m "Rollback target — BFF state on spaarkedev1 BEFORE Wave 12 Batch 4 deploy (2026-06-30)..." \
  4fc73ae4a
```

**Rollback procedure if deploy regresses**: `git checkout deploy/spaarkedev1/pre-wave12-batch4`, then `Deploy-BffApi.ps1` from that commit.

---

## 3. BFF deploy outcome

### 3.1 Command + outcome

```
powershell.exe -ExecutionPolicy Bypass -File scripts/Deploy-BffApi.ps1 -SkipBuild
```

| Step | Result |
|---|---|
| Package created | 45.42 MB zip |
| First attempt `az webapp deploy` | exit 1 (intermittent Kudu 400 — common with Linux App Service) |
| Automatic fallback: stop → Kudu zipdeploy → start | ✅ **succeeded** |
| Post-deploy SHA-256 hash verification (4 critical files) | ✅ **all match local build** |
| Healthz check | ✅ green on attempt 2/24 |
| Final state | spaarke-bff-dev.azurewebsites.net deployed + Healthy |

**Commit deployed**: `37ef38c2f3a1a27393981d056777babd4e3c3e01` (HEAD of `work/spaarke-ai-platform-unification-r7`)

### 3.2 Code changes covered by this BFF deploy

| Task | Subsystem | Commit (most recent affecting BFF) |
|---|---|---|
| T130 | `IMembershipResolverService` canonical defaults + 3 regression tests | `451603bac` |
| T131 | `DailyBriefingCollector` 6-entity extension + resolver consumption + 10 unit tests | `5480ef830` + `badcc1309` |
| T132 | TLDR↔Notes chaining in narrator | `524d1bee9` |
| T133 | Channel registry expansion (BFF response shape side) | `5480ef830` |
| T135 | EnrichBulletWithEntityRefs orphan-fallback + 9 unit tests | `ef4b0ebcb` |
| T150 | EntityType normalization helper + ChatHostContext boundary fix | `287e7b0a9` |
| T151 | PlaybookChatContextProvider EntityName lazy-fetch | `e2b4abdad` |
| T152 | PlaybookChatContextProvider default PageType | `800f23a0a` |

### 3.3 App Service env var verification

`Workspace__SummarizePlaybookId = 4a72f99c-a119-f111-8343-7ced8d1dc988` ✅ confirmed present (T140 — already set, not a Batch 4 change).

---

## 4. Widget deploy outcome

### 4.1 Build + deploy

```
cd src/solutions/SpaarkeAi && npm run build
# vite v5.4.21 → 3,991.37 kB single-file HTML, built in 17.77s

powershell.exe -ExecutionPolicy Bypass -File scripts/Deploy-SpaarkeAi.ps1
```

| Step | Result |
|---|---|
| Build artifact (`dist/spaarkeai.html`) | 3,902 KB (3,995,682 bytes) |
| Web resource ID | `5206a442-3451-f111-bec7-7ced8d1dc988` |
| Update operation | ✅ existing resource updated |
| Publish customizations | ✅ completed |
| Dataverse `modifiedon` post-deploy | `2026-06-30T21:10:38Z` (just-deployed) |

### 4.2 Widget code changes covered by this deploy

| Task | Change | Commit |
|---|---|---|
| T133 | CHANNEL_REGISTRY expanded to 6 entries + fix my-updates raw-slug bug | `5480ef830` |
| T134 | Add To Do checkmark as primary visible tool; preserve three-dot menu | `ee12e172e` |

---

## 5. Curl smoke (server-side surface)

| Endpoint | Expected | Actual | Status |
|---|---|---|---|
| `GET /healthz` | 200 "Healthy" | 200 "Healthy" | ✅ |
| `GET /ping` | 200 "pong" | 200 "pong" | ✅ |
| `POST /api/ai/daily-briefing/render` (unauth, empty body) | 401 | 401 | ✅ (correctly auth-gated) |
| `GET /api/ai/daily-briefing/render` | 404 (POST-only) | 404 | ✅ |
| `GET /api/ai/chat/sessions` | 401 | 401 | ✅ |
| `GET /api/ai/chat/playbooks` | 401 | 401 | ✅ |
| Widget web-resource modifiedon | post-deploy timestamp | `2026-06-30T21:10:38Z` | ✅ |

**Note**: Full functional smoke (e.g. authenticated request returning 6 channels populated with real records) requires an OBO token from an interactive browser session. That portion of the smoke is folded into operator UAT §7 below. The agent-side smoke confirms: deploy artifacts landed, endpoints surface correctly with correct auth posture, no 5xx, env vars intact.

---

## 6. Defects surfaced (this deploy session)

**ISS-NNN filings**: 0 — none surfaced during pre-deploy or smoke.

| Observation | Disposition |
|---|---|
| `az webapp deploy` first attempt returned exit 1 (Kudu 400) | Known intermittent Linux App Service behavior; deploy script's stop→Kudu→start fallback succeeded on first retry; not a defect |
| 7 pre-existing test failures | Documented baseline per Wave 1/2/3/audit 120 sign-offs; not regressions; out of scope for Batch 4 |

---

## 7. Operator UAT checklist (HANDOFF)

> **Operator action required**: run the following in a browser against spaarkedev1. Mark each ✅ pass / ⚠️ minor / ❌ critical. File any ❌/⚠️ via `/project-defer-issue-tracking` (alias `/defer`) → ISS-NNN.

### 7.1 T136 — Daily Briefing widget (wave12 plan §3.1 AC1-AC7)

Open the Daily Briefing widget in spaarkedev1 (e.g. via LegalWorkspace dashboard or directly via SpaarkeAi entry point).

| AC | Criterion | Verify by |
|---|---|---|
| AC1 | Widget renders **6 channels** | Visual count: Upcoming Tasks, Overdue Tasks, Events, Documents, Matters, Projects, To Dos (6 entity-source channels — Events is its own subset of `sprk_event` of type Meeting/etc.) |
| AC2 | Each channel populated with **operator's real Dataverse records** | Open each channel; spot-check 2 records vs Dataverse direct lookup |
| AC3 | **Membership filter** correctly limits to your matters/projects/assignments | Verify records shown are matter/project member; verify no records from matters you are NOT a member of leak through |
| AC4 | **TL;DR ↔ Activity Notes consistency** — items in TL;DR appear in Activity Notes | Sample 3 TL;DR items; locate each in the bullets section |
| AC5 | Each Activity Notes bullet has **working entity link** | Click 1 bullet per entity type; verify navigation to correct record |
| AC6 | **'Add To Do' checkmark** present on each bullet AND **three-dot menu preserved** | Visual: checkmark icon next to each bullet + three-dot menu still opens with other tools |
| AC7 | **Timezone correctly applied** to date filters (5-day window for Upcoming/Overdue) | Cross-check vs Dataverse `dueDate` values for items at the edges of the window |

### 7.2 T154 — Assistant↔Workspace (wave12 plan §3.3 AC13-AC15)

Open the Assistant chat surface inside a workspace context (e.g. open a Matter, then open the Assistant pane).

| AC | Criterion | Verify by |
|---|---|---|
| AC13 | **Assistant knows current matter ID** | In chat, ask "what matter am I looking at?" — response should reference the open matter by name/ID; should NOT generic-respond |
| AC14 | **Responses reference matter-specific data** when present | Ask "summarize this matter's recent activity" — response should reference the open matter's real data, not generic text |
| AC15 | **End-to-end UAT** (audit 120 §4 scenarios A, B, D) | Walk through audit 120 §4 Scenarios A (matter-aware chat), B (entity-name resolution), D (PageType-default fallback for non-form contexts) |

### 7.3 Reporting

After UAT completion, append outcomes to:
- `projects/spaarke-ai-platform-unification-r7/notes/handoffs/wave12-2-uat-signoff.md` (T136 / AC1-AC7)
- `projects/spaarke-ai-platform-unification-r7/notes/handoffs/wave12-4-uat-signoff.md` (T154 / AC13-AC15)

(Both signoff files to be created by operator OR by Claude after operator delivers per-AC outcomes.)

---

## 8. Sign-off matrix

| Gate | Result |
|---|---|
| Pre-deploy build clean (0 errors, 0 new warnings) | ✅ PASS |
| Pre-deploy tests (0 new regressions; 7 pre-existing baseline) | ✅ PASS |
| Compressed publish size measured | ✅ 45.42 MB |
| Size delta vs Wave 4 baseline (≤ +5 MB single-task) | ✅ PASS (-1.30 MB SHRINK) |
| Cumulative ≤60 MB hard ceiling | ✅ PASS (14.58 MB headroom) |
| Cumulative R7 ≤ +2 MB budget (NFR-01) | ✅ PASS (-0.23 MB; net-negative) |
| CVE scan: no new HIGH (NFR-02) | ✅ PASS (1 HIGH pre-existing Kiota accepted-risk) |
| Rollback tag created BEFORE deploy | ✅ `deploy/spaarkedev1/pre-wave12-batch4` |
| BFF deployed via Deploy-BffApi.ps1 | ✅ PASS |
| Post-deploy file-hash verification | ✅ all 4 critical files match |
| Healthz green post-deploy | ✅ PASS |
| Widget rebuilt + deployed via Deploy-SpaarkeAi.ps1 | ✅ PASS |
| Widget Dataverse modifiedon post-deploy | ✅ 2026-06-30T21:10:38Z |
| Curl smoke: all endpoints surface correctly with correct auth | ✅ PASS |
| Operator UAT checklist generated + filed | ✅ this doc §7 |
| Defects (ISS-NNN) | 0 from agent smoke; operator UAT pending |

**Gate: PASSED. Operator UAT REQUESTED.**

---

## 9. Handoff to operator

**Operator action items**:

1. Open spaarkedev1, run UAT §7.1 (T136 Daily Briefing) — record per-AC outcomes
2. Open spaarkedev1, run UAT §7.2 (T154 Assistant↔Workspace) — record per-AC outcomes
3. File any ❌/⚠️ findings as ISS-NNN via `/defer` (binds to GitHub Issue + `notes/defer-issues.md`)
4. Confirm UAT signoff in `notes/handoffs/wave12-2-uat-signoff.md` + `notes/handoffs/wave12-4-uat-signoff.md`
5. On full UAT pass: Wave 12 close-out / R7 wrap-up can proceed

**Rollback procedure if UAT surfaces critical regression**:

```
git checkout deploy/spaarkedev1/pre-wave12-batch4
dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/
powershell.exe -ExecutionPolicy Bypass -File scripts/Deploy-BffApi.ps1 -SkipBuild
# Widget rollback: rebuild from pre-T133/T134 commit (4fc73ae4a) + redeploy
```

---

*Generated by combined Wave 12 Batch 4 deploy session — 2026-06-30.*
