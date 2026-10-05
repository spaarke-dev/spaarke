# Runbook — Exchange sidecar live verification

> **Tasks**: 162 (harness) · 251 (rework after the first live run, 2026-10-04)
> **Script**: [`scripts/provisioning/Verify-Sidecar-Live.ps1`](../../scripts/provisioning/Verify-Sidecar-Live.ps1)
> **Last run**: 2026-10-04, dev, `-InTenant` → 5 PASS · 1 WARN (check 5 not requested) — [`sidecar-live-verification-2026-10-04.json`](sidecar-live-verification-2026-10-04.json)
> **Design**: [`t251-exchange-sidecar-design.md`](t251-exchange-sidecar-design.md) (identity, RBAC for Applications, spike log)

---

## 1. What it verifies

The Exchange sidecar runs beside the L2 Worker as an App Service sitecontainer on `127.0.0.1:8091`. H14a
(`/apply-mailbox-access`) and H13 T4 (`/read-mailbox-access`) reach it only from the Worker. The sidecar holds
no credential: each request carries an Exchange Online token for `Spaarke Exchange Admin` (minted by the Worker
through its managed identity's federated credential) and names the tenant's initial domain as `organization`.

| # | Check | How | Security-critical |
|---|---|---|---|
| 1 | CONTAINER_HEALTH | ARM: the sitecontainer exists with port 8091 and the expected image | No |
| 2 | LOCALHOST_BIND | Kudu `/api/logs/docker`: the latest `"Sidecar listening"` line from the sidecar has `degraded: false` | No |
| 3 | PUBLIC_ISOLATION | `https://{worker}.azurewebsites.net:8091/healthz` must not answer | **Yes** |
| 4 | ROUND_TRIP_AUTH | `-InTenant`: `/read-mailbox-access` with the right secret and a real token → HTTP 200, outcome Success | No |
| 5 | ROUND_TRIP_IDEMP | `-InTenant` + a TEST app and group: apply twice → second is `AlreadyCompliant` (**creates** an assignment) | No |
| 6 | AUTH_REJECTION | `-InTenant`: wrong secret → 401; tenant id as `organization` → 400 | **Yes** |

**Why checks 4–6 run in a container instance, not through Kudu** (found 2026-10-04): on Linux App Service Kudu
runs in its own container, so it cannot reach the Worker's `127.0.0.1:8091`, and `/api/command` runs no shell.
`-InTenant` instead starts a temporary container instance from the Worker's sidecar image, with the Worker's
managed identity. It runs the image's own `Listener.ps1`, signs in exactly as the Worker does, reads the
initial domain from Graph `GET /organization`, and calls the routes on localhost. The token never leaves that
container and is never printed. The container is deleted at the end, also on failure.

## 2. Who runs it

The operator, as themselves (NFR-11): `az login` with read on the Worker and `rg-spaarke-platform-{env}`, Kudu
(SCM) access on the Worker, and — for `-InTenant` — rights to create a container instance in that resource group
and assign the Worker's user-assigned identity to it. **Ask the owner before each `-InTenant` run** (it creates a
resource and signs in to Exchange); check 5 additionally creates an Exchange role assignment.

## 3. Steps

```powershell
# Configuration only — creates nothing. Expected: 3 PASS, 3 WARN.
./scripts/provisioning/Verify-Sidecar-Live.ps1 -Environment dev

# In-tenant, read-only against Exchange. Expected: 5 PASS, 1 WARN (check 5).
./scripts/provisioning/Verify-Sidecar-Live.ps1 -Environment dev -InTenant `
    -PolicyScopeGroupId <test group object id> `
    -ReportPath ./projects/customer-provisioning-orchestration-r1/notes/sidecar-live-verification-{yyyy-mm-dd}.json

# Including check 5 — creates assignment 'Spaarke-liveverify-MailRead' for the TEST app. Expected: 6 PASS.
./scripts/provisioning/Verify-Sidecar-Live.ps1 -Environment dev -InTenant `
    -PolicyScopeGroupId <test group> -AppId <test app client id> -ServicePrincipalObjectId <its SP object id>
```

Dev test group: `sprk-t251-spike-scope` (Entra object id `c709af95-0332-4ea2-a9d4-6925b1666bad`, member
`testuser1@spaarke.com`), kept after T251 for this purpose. After check 5, remove the assignment and the test
app's Exchange service principal (`Remove-ManagementRoleAssignment`, `Remove-ServicePrincipal`).

## 4. Reading a failure

- **CONTAINER_HEALTH** — sitecontainer missing or wrong port: redeploy `platform-controlplane`.
- **LOCALHOST_BIND, "never bound"** — the sidecar did not start: read the Worker's container log
  (`az webapp log tail`) for its `exchange-policy-sidecar` lines. **"DEGRADED"** — a setting is missing: its
  `missing settings` line names it; the usual cause is `ExchangeSidecar__SharedSecret` (a Key Vault reference)
  not resolving.
- **ROUND_TRIP_AUTH** — the result names the step: sign-in (check the admin app's federated credential and
  `Exchange.ManageAsApp`), the Graph lookup (the Worker identity needs `Organization.Read.All` or
  `Directory.Read.All`), or the sidecar's diagnostic.
- **PUBLIC_ISOLATION / AUTH_REJECTION** — security findings; see §5.

## 5. Escalation (security-critical checks)

**PUBLIC_ISOLATION FAIL** (the port answers publicly): STOP — no provisioning against this environment. Remove
the sitecontainer (Bicep) and redeploy before investigating. Escalate to the owner with the JSON report, the log
tail and the URL that answered. Root CLAUDE.md §6.

**AUTH_REJECTION FAIL** (a wrong secret was not refused with 401, or a tenant id was accepted as organization):
STOP — the sidecar is not guarding the Exchange-admin capability. Check the deployed image matches
`Listener.ps1` / `SidecarCore.psm1` on the branch, and escalate to the owner with the report and the log tail.

## 6. Rollback

The sidecar binds its port even when a setting is missing (it answers with a named diagnostic), so it cannot hold
the Worker at 503. To take it out entirely, remove the `exchangePolicySidecar` resource from
[`controlplane-worker-app-service.bicep`](../../infrastructure/bicep/modules/controlplane-worker-app-service.bicep)
and redeploy `platform-controlplane`. H14a and H13 T4 then fail with a transport error, classified Resumable —
the run waits on that step; no Exchange change is skipped silently.

## 7. Not covered

- A full customer run (task 186) — the first real Worker → sidecar call inside a provisioning run happens there.
- The Graph *effect* of a grant (Exchange applies RBAC for Applications in 30 min–2 h); T4 checks configuration.
