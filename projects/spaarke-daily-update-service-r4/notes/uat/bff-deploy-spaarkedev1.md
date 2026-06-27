# BFF Deploy to spaarkedev1 — UAT Readiness Report

> **Project**: spaarke-daily-update-service-r4
> **Deploy date**: 2026-06-26 07:10 UTC-local
> **Operator**: Claude Code (Opus 4.7) on behalf of ralph.schroeder@spaarke.com
> **Environment**: spaarkedev1 (Spaarke Development Environment, subscription `484bc857-3802-427f-9ea5-ca47b43db0f0`)
> **Source commit**: `072ba99e0` (master HEAD; PR #456 merge)
> **Working tree**: `c:\code_files\spaarke-wt-spaarke-daily-update-service-r4` (worktree branch `work/spaarke-daily-update-service-r4`; BFF source identical to master per `git diff master -- src/server/api/Sprk.Bff.Api/` empty)

---

## 1. Deployment Summary

| Item | Value |
|------|-------|
| Tool | `pwsh -ExecutionPolicy Bypass -File scripts/Deploy-BffApi.ps1` |
| Resource Group | `rg-spaarke-dev` |
| App Service | `spaarke-bff-dev` (Linux app, Running) |
| API URL | `https://spaarke-bff-dev.azurewebsites.net` |
| Deploy mode | Direct Deploy (single-step `az webapp deploy`) |
| Package size | **46.3 MB** (well under 60 MB NFR-01 ceiling) |
| Hash-verify | **PASS** — all 4 critical files match local build (SHA-256) |
| Health-check window | 120 s (Linux cold-start tolerance) |
| Build | Release mode, `dotnet publish` from `src/server/api/Sprk.Bff.Api/publish/` |

**Pre-flight**:
- Worktree BFF source verified identical to master HEAD (no diff)
- Azure CLI authenticated as `ralph.schroeder@spaarke.com` to subscription `Spaarke Devlopment Environment`
- App Service existence verified via `az webapp show` before invoking script
- Stale `publish/` directory removed pre-deploy to avoid MSB3030 nesting

---

## 2. Smoke Check Results

All checks executed immediately post-deploy (no warm-up delay needed; healthz already 200 by hash-verify completion).

| Endpoint | Method | Auth | Expected | Actual | Status |
|----------|--------|------|----------|--------|--------|
| `/healthz` | GET | none | 200 | **200** | PASS |
| `/ping` | GET | none | 200 | **200** | PASS |
| `/api/ai/daily-briefing/narrate` | POST + `Content-Type: application/json` | none | 401 (route found, auth required) | **401** | **PASS — new endpoint live** |
| `/api/ai/daily-briefing/summarize` | POST + `Content-Type: application/json` | none | 401 | **401** | PASS (regression check) |

**Note on the 404 false alarm**: An initial `curl -X POST` without `Content-Type` returned 404 on `/narrate`. This is ASP.NET Minimal API's expected behavior — typed JSON body binding for `DailyBriefingNarrateRequest` requires a recognizable content-type before the auth filter is hit. With `Content-Type: application/json`, the route correctly returns 401 ("auth required"), confirming the route is registered and the Path A.5 dispatch wrapper compiled into the published assembly. This matches the bff-deploy skill's "401 = route registered" verification rule.

**No errors or warnings observed during deploy.** The script output showed:
- "Captured pre-deploy hashes for 4 critical files"
- "Deployment command returned success"
- "All 4 critical files match local build (SHA-256 verified)"
- "dev health check passed!"

---

## 3. R4 Surface Deployed

Per the deployment task brief, the following R4 BFF changes are now live on spaarkedev1:

1. **`Services/Ai/NodeExecutors/EntityNameValidatorNodeExecutor.cs`** — new playbook node executor (anti-hallucination guard)
2. **`Services/Ai/AnalysisServicesModule.cs`** — DI registration for `EntityNameValidatorNodeExecutor`
3. **`Services/Ai/NodeExecutors/CreateNotificationNodeExecutor.BuildNotificationEntity`** — enriched with `viaMatter` / `regardingName` / `source` customData
4. **`Services/Communication/MembershipResolverService.cs`** — `member_skipped` warning emission for unresolvable members
5. **`Api/Ai/DailyBriefingEndpoints.HandleNarrate`** — rewritten as Path A.5 dispatch wrapper via `IConsumerRoutingService` + `IInvokePlaybookAi`, dispatching on `ConsumerTypes.DailyBriefingNarrate`
6. **`Models/Ai/ConsumerTypes.DailyBriefingNarrate`** — new compile-time consumer-type constant

Local build verification (pre-deploy):
- `dotnet build src/server/api/Sprk.Bff.Api/ -c Release` → 0 errors
- `dotnet test tests/unit/Sprk.Bff.Api.Tests/` → 7879 passed / 0 failed / 134 skipped

---

## 4. Recommended UAT Test Scenarios

### 4.1 Anti-hallucination (EntityNameValidatorNodeExecutor)

**Goal**: Verify the validator catches model-fabricated entity names in playbook output.

- Trigger a daily-briefing-narrate run against a user with at least one matter (e.g., via the SpaarkeAi widget or direct `POST /api/ai/daily-briefing/narrate` with a valid OBO token)
- In App Service log stream, search for `EntityNameValidator` events
- Compare narrative output against the source notifications: every `regardingName` cited in a bullet MUST appear in the source `DailyBriefingNarrateRequest.priorityItems[*].regardingName` set (no fabricated matter/case names)
- Expected: zero hallucinated names; validator either passes silently or rejects with a structured error projecting to 422 (per playbook contract)

### 4.2 `member_skipped` warning log scrape (MembershipResolverService)

**Goal**: Confirm the new warning fires for unresolvable members and is queryable in Application Insights.

- Send a notification creation request (e.g., via Service Bus job or direct CreateNotification trigger) where one of the recipient member IDs is intentionally non-existent in Dataverse
- App Insights query (KQL):
  ```kql
  traces
  | where timestamp > ago(15m)
  | where message contains "member_skipped"
  | project timestamp, severityLevel, message, customDimensions
  ```
- Expected: at least one `member_skipped` warning trace per unresolvable recipient with structured `customDimensions` (memberId, reason)
- Negative case: a fully-resolvable membership should produce **zero** `member_skipped` warnings

### 4.3 customData enrichment verification (CreateNotificationNodeExecutor)

**Goal**: Confirm `viaMatter` / `regardingName` / `source` flow through to created `sprk_notification` rows.

- Trigger a notification-producing playbook run (briefing or other) that includes a matter-regarding notification
- In Dataverse (via XrmToolBox FetchXML Builder or `Get-CrmRecord`), inspect the newly created `sprk_notification` row's `sprk_customdata` field (JSON column)
- Expected JSON contains `{ "viaMatter": "<matter-guid>", "regardingName": "<matter-name>", "source": "<source-key>" }`
- Verify the widget's notification card UI surfaces "via matter X" correctly when this customData is present (front-end rendering check)

### 4.4 Path A.5 dispatch — `/narrate` end-to-end

**Goal**: Confirm the rewritten HandleNarrate dispatches through `IConsumerRoutingService` → `IInvokePlaybookAi` correctly and the `daily-briefing-narrate` consumer is configured.

- With a valid OBO token, POST a realistic `DailyBriefingNarrateRequest` to `/api/ai/daily-briefing/narrate`
- Expected:
  - 200 OK with `DailyBriefingNarrateResponse` containing `tldr` + `channelNarratives[]`
  - App Insights trace: "Dispatching daily-briefing-narrate via consumer routing" with the resolved `PlaybookId` + `RunId`
  - **NO 503** ("Daily briefing dispatch is unconfigured"). If 503, the `sprk_playbookconsumer` row for `daily-briefing-narrate` is missing — escalate to data ops.
- Negative case: empty `priorityItems` + empty `categories` should return 200 with empty bullets (early-return path), no playbook call

### 4.5 Publish-size regression (NFR-01)

- Deployment package was 46.3 MB compressed — well within the 60 MB ceiling
- No new NuGet additions were flagged in R4 (per project task ledger)
- No action required; this is recorded for the baseline trend

---

## 5. UAT Readiness Assessment

| Surface | Status |
|---------|--------|
| BFF process up | **READY** — `/healthz` 200, hash-verify passed |
| New `/narrate` route registered | **READY** — 401 with proper Content-Type confirms route + auth filter |
| Existing `/summarize` regression | **CLEAN** — 401 (no break) |
| R4 code present in published assemblies | **VERIFIED** — SHA-256 of `Sprk.Bff.Api.dll` matches local Release build |
| Publish size within NFR-01 | **GREEN** — 46.3 MB << 60 MB ceiling |
| Auth path (OBO + MI) functional | **NOT TESTED HERE** — covered by `auth-deployment-setup.md` §9 smokes; no auth-related code changed in R4, so existing config remains valid |

**Recommendation**: Hand off to UAT for scenarios 4.1–4.4. No deploy-side blockers.

---

## 6. Failure Modes / Known Caveats

- **None observed during this deploy.**
- The bff-deploy skill notes a historical "silent file-lock failure" on Windows App Service (May 2026 incident). spaarke-bff-dev is Linux (`kind: app,linux`), which does not exhibit this failure mode; hash-verify ran defensively anyway and passed.
- If `/narrate` returns **503** ("Daily briefing dispatch is unconfigured") during UAT scenario 4.4, this indicates the `sprk_playbookconsumer` row for `daily-briefing-narrate` is missing in Dataverse — this is a **data-config issue**, NOT a deploy issue. Refer to project task ledger for the consumer-row provisioning task.

---

## 7. Rollback Plan (if UAT finds a regression)

- Re-run `pwsh -File scripts/Deploy-BffApi.ps1` with the master commit BEFORE PR #456 merge: `git checkout <pre-456-commit>` then deploy
- The script's hash-verify will confirm the rollback completed (old hashes restored)
- No DB schema migration was part of R4 BFF surface, so a code-only rollback is safe

---

## 8. Sign-off

- Deploy script: completed without intervention
- Smoke checks: 4 of 4 passed
- UAT scenarios drafted: 4 functional + 1 NFR
- This report is the artifact handed to UAT operators alongside the project's existing `lessons-learned.md` and `risks.md`.
