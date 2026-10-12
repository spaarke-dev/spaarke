# Task 038: R3 BFF deploy to dev (spaarke-bff-dev): plan and record

> Status: **PLAN, awaiting owner approval** (2026-10-12). Nothing below has run against Azure yet.
> Owner said "ok" to dev dispatch on 2026-10-12, on condition that each command and its rollback are shown first.

## What gets deployed

- Branch `work/spaarke-SPA-external-access-platform-r3` at the commit pushed after the owner answers the ADR-028 A8 question (currently local `260e10fc3`: R3 + master `e5059672d` merged).
- Pre-deploy local gates (done 2026-10-12 on the merged tree): Release build 0 errors; ArchTests 969 passed; external/communication filter 4670 passed, 0 failed (8 pre-existing skips).
- Publish-size delta against a fresh master build and the CVE check: measured separately (§ Measurements).

## Phase A: read-only checks (run first; each prints no secret value)

| # | Command | Why |
|---|---|---|
| A1 | `az account show --query "{sub:name, tenant:tenantId}" -o table` | Confirm the subscription before anything else |
| A2 | `az webapp show -g rg-spaarke-dev -n spaarke-bff-dev --query "{state:state, runtime:siteConfig.linuxFxVersion, host:defaultHostName}" -o table` | Target exists, runtime is `DOTNETCORE\|10.0` |
| A3 | `az webapp config appsettings list -g rg-spaarke-dev -n spaarke-bff-dev -o json` piped to a local filter that prints, for the keys below, the NAME and: `true`/`false` for flags, the first 8 characters of GUIDs, `set`/`missing` for everything else. Keys: `Ciam__Audience`, `Ciam__TenantId`, `AzureAd__TenantId`, `WorkforceIdentity__CustomerTenantIds__*`, `ExternalAccess__PartnerServiceRequestsEnabled`, `ExternalAccess__PartnerInventionIntakeEnabled`, `ExternalAccess__PortalUrl`, `Customer__Id`, `IdentityLink__Reconciliation__WritesEnabled`, `Communication__DefaultMailbox`, `Communication__ApprovedSenders__*` | Start-up validators: a non-boolean partner flag, `Ciam:TenantId` = `AzureAd:TenantId`, or a bad customer tenant id stops the host. Also confirms the owner rule "reconciliation writes ON everywhere" |
| A4 | Same as A3 with `--slot staging`, `Communication__*` only | A swap to an unchecked staging slot would fail start-up (validator) |
| A5 | `GET https://spaarke-bff-dev.scm.azurewebsites.net/api/deployments` (management token), print the latest 3: time, author, message | Who deployed what last. If it is another project's unmerged branch, this deploy overwrites it: stop and ask |
| A6 | `GET https://spaarke-bff-dev.scm.azurewebsites.net/api/zip/site/wwwroot/` saved to `deploy/rollback/spaarke-bff-dev-wwwroot-<UTC timestamp>.zip`, then `Expand-Archive` to `deploy/rollback/wwwroot-<ts>/` and print file count and size | The rollback package (the exact files running now) |
| A7 | `curl -s -o NUL -w "%{http_code}" https://spaarke-bff-dev.azurewebsites.net/healthz` | Baseline health before the change |

`deploy/` is git-ignored; the rollback copy never leaves the machine.

## Phase B: deploy (one command)

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/Deploy-BffApi.ps1
```

Dev, direct deploy (no slot). The script publishes Release from `src/server/api/Sprk.Bff.Api/`, zips, deploys, SHA-256-verifies 6 files through Kudu (auto-recovers with stop -> zipdeploy -> start on a mismatch) and polls `/healthz` for up to 120 s.

**Impact:** `spaarke-bff-dev` restarts; other projects using dev see about 1-2 minutes of unavailability.

## Rollback

Trigger: `/healthz` not 200 after the script's window plus one manual retry 60 s later **with** a hash mismatch, or a probe failure that breaks existing (non-R3) behaviour.

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/Deploy-BffApi.ps1 -SkipBuild -PublishPath deploy/rollback/wwwroot-<ts>
```

This redeploys the exact files captured in A6, with the same hash verification and health check.

## Phase C: live probes (step 4 of the POML), separate approval

Need test identities (a CIAM partner test account and a workforce test user). Read-only where possible; test rows created (intake submit, message, document upload) are deleted afterwards through the R3 delete-own routes. Exact commands will be shown before Phase C runs.

| Probe | Expected |
|---|---|
| CIAM token on a workforce-only route | 401 (FR-23) |
| CIAM token, audience `{appId}` and `api://{appId}`, on `/api/v1/external/me` | 200 both (FR-22) |
| `/me/entitlements` as a workforce user | `legal-front-door` + `policy-library`, `intakeRequestTypes` present (FR-19) |
| Message read and send on an accessible record; 403 outside | FR-04 / FR-11 |
| Intake submit as workforce | 201 (FR-05) |
| Notification feed shows the new message | FR-08 |
| Document upload + own delete on a matter | FR-24 / FR-26 |

## Measurements

Measured 2026-10-12 per `.claude/constraints/azure-deployment.md` (fresh short-path worktrees, same publish command as `Deploy-BffApi.ps1`, `Compress-Archive` Optimal on both):

| | master `6be73d8a1` | branch `260e10fc3` |
|---|---|---|
| Zip | 36.480 MB | 36.580 MB |
| Files | 192 | 192 (path-by-path identical set) |

Delta **+0.100 MB** (under the +5 MB trigger; total far under 55/60 MB). `dotnet list package --vulnerable --include-transitive`: none on either side. SDK 10.0.101 present. Note: origin/master moved past the branch's last merge (`e5059672d`), so the delta also includes those later master commits.

## Record

(filled in after each phase runs)
