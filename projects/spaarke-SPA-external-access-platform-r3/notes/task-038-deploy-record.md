# Task 038: R3 BFF deploy to dev (spaarke-bff-dev): plan and record

> Status: **Phases A and B done 2026-10-13.** R3 is live on dev through the master deploy `99ec749cd` (see Record). Phase C needs a separate approval.
> Owner said "ok" to dev dispatch on 2026-10-12, on condition that each command and its rollback are shown first.

## What gets deployed

- Branch `work/spaarke-SPA-external-access-platform-r3` at `ae2ecb5f5` (2026-10-13): R3 after PR #1795 merged to master, current with master `ba1242b5a`. It differs from master only by the A8 clarification commit (docs and a comment); BFF code is the same as master's.
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

Owner said "yes let's proceed" (2026-10-13) to preparing it. **Plan below; nothing has run.** Tokens stay in a PowerShell variable and are never printed; evidence records status codes and masked claims only (`tid`/`aud` first 8 characters, `scp`, `idtyp`).

**Identities.** Workforce: the owner's member account `ralph.schroeder@spaarke.com` through Azure CLI (no CIAM-style restriction in the Spaarke tenant). CIAM: an interactive browser sign-in by the owner is the only way to get a CIAM user token (CLI and device-code sign-in to `spaarkeextid` are refused, T240d S1), so the CIAM probes run in the owner's browser after the SPA deploy (`notes/task-040-spa-dev-deploy-plan.md`).

**Test record.** A dev matter the owner names, which the workforce account can open. Outside-access probes use a random GUID (an unknown record and a no-access record are denied identically, so it exercises the same 403 path).

### C-W: workforce probes (session, PowerShell)

```powershell
$b   = 'https://spaarke-bff-dev.azurewebsites.net/api/v1/external'
$tok = az account get-access-token --resource api://1e40baad-e065-4aea-a8d4-4b7ab273458c --query accessToken -o tsv
$h   = @{ Authorization = "Bearer $tok" }
# C-W0: print masked claims only (tid, aud, scp, idtyp) by decoding the payload locally
```

| # | Request | Expected | Creates data |
|---|---|---|---|
| C-W1 | `GET $b/me` | 200, plane = workforce | no |
| C-W2 | `GET $b/me/entitlements` | 200; modules include `legal-front-door` and `policy-library`; `intakeRequestTypes` = all four (FR-19) | no |
| C-W3 | `GET $b/matters/{testMatter}/messages`, then the same for a random GUID | 200; 403 (FR-04 / FR-11) | no |
| C-W4 | `POST $b/matters/{testMatter}/messages` body `{"sprk_body":"[R3 probe 038] safe to delete"}`; then `POST` with an extra `attachments` property | 201 (or 200) and no email (standing rule); the second is refused (FR-11, no attachments) | **1 message** |
| C-W5 | `GET $b/notifications` | 200; record whether the new message appears (the poster's own message may be excluded by design) (FR-08) | no |
| C-W6 | `POST $b/servicerequests` body `{"requestType":100000002,"title":"[R3 probe 038] safe to delete","answers":{"question":"probe"}}` | 201 with the new id (FR-05) | **1 service request** |
| C-W7 | `POST $b/matters/{testMatter}/documents`, multipart field `file` = a 1 KB text file `r3-probe-038.txt` | 201 with the document id (FR-24) | **1 document + 1 SPE file** |
| C-W8 | `DELETE $b/documents/{docId}`; `DELETE $b/servicerequests/{srId}` | 204 both (FR-26) | removes C-W6/C-W7 |

**Cleanup.** C-W8 deletes the document and the service request through R3's own delete routes. R3 has **no** route to delete a message, so the C-W4 message is removed by a Dataverse delete of the id the POST returns (table confirmed from that response), with the owner's approval of that one delete at the time, or it is left on the test matter marked "safe to delete" if the owner prefers.

### C-C: CIAM probes (owner's browser, after the SPA deploy)

| # | Step | Expected |
|---|---|---|
| C-C1 | Sign in to the dev SPA as a CIAM test partner; DevTools Network: the `/api/v1/external/me` request | 200; token `aud` = `4a4d5126...` (bare GUID, v2), `iss` = the `ciamlogin.com` v2 issuer (FR-22) |
| C-C2 | DevTools console: `fetch('https://spaarke-bff-dev.azurewebsites.net/api/me', {headers:{Authorization:'Bearer ' + <the same token>}})` and read the status in the Network tab | 401 (FR-23: a CIAM token on a workforce route) |

The `api://{appId}` audience form cannot be produced live: the CIAM API app issues v2 tokens, whose `aud` is always the bare GUID. That form stays covered by task 010's unit tests (`BuildCiamValidAudiences`); recorded as not live-testable.

**Rollback.** Probes change no configuration. If a probe shows a regression in existing (non-R3) behaviour, BFF rollback means redeploying an earlier master commit, which is owned by whoever deploys dev next (see Phase B).

## Measurements

Measured 2026-10-12 per `.claude/constraints/azure-deployment.md` (fresh short-path worktrees, same publish command as `Deploy-BffApi.ps1`, `Compress-Archive` Optimal on both):

| | master `6be73d8a1` | branch `260e10fc3` |
|---|---|---|
| Zip | 36.480 MB | 36.580 MB |
| Files | 192 | 192 (path-by-path identical set) |

Delta **+0.100 MB** (under the +5 MB trigger; total far under 55/60 MB). `dotnet list package --vulnerable --include-transitive`: none on either side. SDK 10.0.101 present. Note: origin/master moved past the branch's last merge (`e5059672d`), so the delta also includes those later master commits.

2026-10-13: R3 is now on master, so the deploy payload is master's BFF (`src/server` changed in 18 files between `260e10fc3` and `ae2ecb5f5`, mostly master's work). Before phase B, the local publish is re-run once to record the zip size (must stay under 55 MB). This is a local build only and touches no Azure resource.

## Record

### Phase A, 2026-10-13 ~03:48 UTC (owner-approved "approve 038 A+B")

| # | Result |
|---|---|
| A1 | Subscription "Spaarke Devlopment Environment", tenant `a221a95e...` |
| A2 | `spaarke-bff-dev` Running, `DOTNETCORE\|10.0` |
| A3 | `Ciam__Audience` `4a4d5126...`; `Ciam__TenantId` `7052feba...` differs from `AzureAd__TenantId` `a221a95e...`; customer tenant ids `a221a95e...`, `bc3aa7f4...`; `IdentityLink__Reconciliation__WritesEnabled` true; `ExternalAccess__PortalUrl`, `Customer__Id`, `Communication__DefaultMailbox` set; 2 approved senders (one default). Both partner flags MISSING = off (code: `ExternalAccessOptions.cs:28,40`, absent is false; only empty or non-boolean fails startup) |
| A4 | Staging slot has no `Communication__*` settings. Not used by this direct deploy; a future swap to staging would need them first |
| A5 | Kudu records no author (3 OneDeploy entries, latest 2026-10-13 00:59 UTC). The live DLL's version is `1.0.0+f9cc25f73`: **`work/spaarkeai-word-add-in-r1` task 129, NOT on master** (11 commits ahead of master; 11 files / +1620 lines in `src/server`; no open PR). **STOP condition met: deploying R3 overwrites that project's unmerged live build. Phase B held for owner decision** |
| A6 | Rollback package `deploy/rollback/spaarke-bff-dev-wwwroot-20261013T034808Z.zip` (50.6 MB), expanded to `deploy/rollback/wwwroot-20261013T034808Z/` (222 files, 144.3 MB) |
| A7 | `/healthz` 200, `/healthz/dataverse` 200 |

### Phase C-W, 2026-10-13 ~12:40 UTC (owner-approved "yes approved to run workforce probes")

Identity `ralph.schroeder@spaarke.com` via Azure CLI. Token (masked): `tid=a221a95e...`, `aud=api://1e40baad...` (the `api://` form, accepted), `scp=SDAP.Access user_impersonation`, `ver=1.0`, `acct=0`. Test matter "Test New Matter via Workspace" `34625b38-68a5-f111-aaad-7ced8ddc4cc6`.

| # | Result | Verdict |
|---|---|---|
| C-W1 `GET /me` | 200; contact `8e9918a9...`, systemuser `1d02f31c...` | PASS |
| C-W2 `GET /me/entitlements` | 200; `plane=workforce`, `["legal-front-door","policy-library"]`, `intakeRequestTypes` all four | PASS (FR-19) |
| C-W3 messages read | test matter 200; random GUID 403 | PASS (FR-04/FR-11) |
| C-W4 message post | 201, message `b38f0ab7-03c7-f111-a05c-0022482913fc` (`isMine` true). With an `attachments` property: 400 `EXT_MESSAGES_ATTACHMENTS_NOT_SUPPORTED` | PASS (FR-04/FR-11). **Message left on the test matter, body "[R3 probe 038] safe to delete"; owner deletes it manually** |
| C-W5 `GET /notifications` | 200; lists other users' messages on the test matter; the probe's own message is absent, as designed (own messages dropped by the creator stamp, `ExternalNotificationFeedEndpoints.cs:21-22`) | PASS (FR-08) |
| C-W6 intake submit (Policy Question) | **500 `EXT_INTAKE_INTERNAL_ERROR`**. App Insights `spe-insights-dev-67e2xz`, operation `f280d5f9...`: Dataverse 403 on `POST sprk_servicerequests`, `0x80040299` "Read Privilege Check For Owner failed: Principal team cf15f587-baa0-f111-aaac-000d3a99d1d7 (owning team of 'Spaarke Business Unit 1') is missing prvReadsprk_ServiceRequest". No partial row (checked by name) | **FAIL (FR-05): dev configuration, not code.** The owning team's roles are Spaarke Basic User, Reporting Access Viewer, AI Analysis User, Office Add In User; none reads `sprk_servicerequest`. Role "Spaarke Internal Contact User (Workforce)": root-BU role `9deaac9c-02c7-f111-a05c-0022482913fc` has 655 privileges, none on `sprk_servicerequest` (the BU1 inherited copy `7eb393bd` lists none of its own); it is UAC round 97's baseline role. Owner decision pending |
| C-W7 document upload | 201, document `2a0b7abd-03c7-f111-a05c-0022482913fc` | PASS (FR-24) |
| C-W8a delete own document | 204; no longer listed | PASS (FR-26) |
| C-W8b delete own service request | not run: C-W6 created nothing | blocked by C-W6 |

### Phase B, 2026-10-13: R3 reached dev by the master deploy (no R3 deploy run)

Owner direction: word-add-in-r1 merges to master (PR #1796, `99ec749cd`) and deploys the BFF from master, which already holds R3 (#1795). That session reported: 36.6 MB package, SHA-256 match on the critical files, `/healthz` pass.

Independent check by R3: the live `Sprk.Bff.Api.dll` ProductVersion is `1.0.0+99ec749cd...`; that commit is on master and contains `ba1242b5a` (the R3 merge); `/healthz` 200, `/healthz/dataverse` 200. **R3 is live on dev.** Not in this build: PR #1799 (mapped `upn`, auto-merge) and the R3 branch's A8 clarification commit (docs only).

The A6 package (`wwwroot-20261013T034808Z`) is the word-add-in `f9cc25f73` build. It is not a rollback target for this deploy; rolling back now means redeploying an earlier master commit, which the deploying session owns.

### Intake 403 fix, 2026-10-13 (owner-approved: "approve intake fix", then "go")

- **Repo:** `Spaarke Basic User.xml` gains `prvReadsprk_ServiceRequest` at Basic level (commit `5bfedfe3d`). Customers get it with the solution.
- **Dev (live change):** `AddPrivilegesRole` on the root role `11f93c04-ddf6-f011-8406-7c1e520aa4df` (Spaarke Basic User), privilege `80592209-a062-49a9-b307-fb6177df6a46`, depth Basic. Before: False; after: True.
  - The BU1 owning team `cf15f587` holds the BU copy `dc44312f…`, whose root is `11f93c04`.
  - Rollback: `RemovePrivilegeRole` with the same privilege id (scratchpad `intake-role-fix.ps1 -Rollback`).
- **Re-test:**
  - First try right after the change: still 500, with the same `0x80040299`. Dataverse's security cache had not refreshed.
  - About 2 minutes later: **C-W6 201** (service request `6af398b9-16c7-f111-a05c-3833c5e9614d`), then **C-W8b 204** (deleted through R3's delete-own route).
  - Nothing is left behind. C-W6 and C-W8b: **PASS** (FR-05, FR-26).
