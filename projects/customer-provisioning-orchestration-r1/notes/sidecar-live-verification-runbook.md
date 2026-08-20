# Runbook — Exchange policy sidecar live verification

> **Task**: 162 (Wave G-6 Batch G-6C, `Sidecar live verification against dev L2 Worker App Service`)
> **Authored**: 2026-08-20, Phase C'' Wave G-6
> **Status of this document**: AUTHORING ONLY — infrastructure delivered; live execution deferred to owner-in-the-loop live ceremony (see "Who runs this" below).
> **References**: [`DS-1b §3`](design-study-ds1b-option-d-hybrid-deep-dive.md) (topology + auth legs), task 114 [`Listener.ps1`](../../src/server/services/Sprk.Provisioning.ControlPlane.Sidecar/Listener.ps1), task 101 [`controlplane-worker-app-service.bicep`](../../infrastructure/bicep/modules/controlplane-worker-app-service.bicep), task 113 [`Deploy-ControlPlane.ps1`](../../scripts/provisioning/Deploy-ControlPlane.ps1), task 161 [`ExchangePolicySidecarClient.cs`](../../src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/IntegrationWiring/ExchangePolicySidecarClient.cs), spec.md T4 silent-fail trap.

---

## 1. Why this runbook exists

Task 114 (sidecar image) + task 161 (`ExchangePolicySidecarClient`) landed the LOGICAL half of Option D's sidecar path: the Dockerfile, `Listener.ps1`, `ExchangePolicySidecarClient` HTTP client, and 21 fake-transport contract tests that prove the client + wire envelope correctness in isolation. What they do NOT prove: that the client can actually reach a real sidecar container running as a sitecontainer under the dev L2 Worker App Service, that the localhost-namespace isolation genuinely holds against the public front-end, or that the per-boot shared-secret rejection path fires as designed against a real request from the public internet.

Those are LIVE properties. This runbook is the operator's guide for verifying them end-to-end AFTER the live ceremony deploys the platform. It is the only verification that closes the multi-year Option D architectural bet DS-1b §0's verified finding rests on (no Graph API for Exchange app-only policy management exists, so the sidecar is not a stopgap — it is the R22 migration point for the day Microsoft eventually ships a REST equivalent).

---

## 2. Who runs this

An operator with:
- Contributor on `rg-spaarke-platform-{env}` (able to `az login` + read App Service metadata via ARM).
- Key Vault Secrets User on `sprk-controlplane-{env}-kv` (able to `az keyvault secret show` on `Sidecar-Shared-Secret`).
- Kudu SCM (deployment) access on the Worker App Service `spaarke-provisioning-controlplane-worker-{env}` (used by the Verify-Sidecar-Live.ps1 harness to `curl` from inside the Worker's network namespace via `/api/command`).

NOT a subagent. Task 162's `<escalation>` triggers explicitly cite root CLAUDE.md §6 for security-sensitive verification — subagents cannot make the escalation call this ceremony requires.

---

## 3. Prerequisites (must be TRUE before running this runbook)

| # | Prerequisite | How to verify |
|---|---|---|
| 1 | Live ceremony backlog step 1 (Service Bus queue recreate) has run | `az servicebus queue show -n sprk-provisioning-jobs --namespace-name spaarke-servicebus-{env} -g SharePointEmbedded --query "{sess:requiresSession,dup:requiresDuplicateDetection}"` — both `true` |
| 2 | Live ceremony backlog step 5 (Deploy-ControlPlane.ps1 executed) has run — Worker App Service exists + latest code deployed | `az webapp show -n spaarke-provisioning-controlplane-worker-{env} -g rg-spaarke-platform-{env} --query state -o tsv` — `"Running"` |
| 3 | Sidecar image has been published to ACR + sitecontainer's `acrImageTag` param references the real image (not the `mcr.microsoft.com/appsvc/staticsite:latest` placeholder) | `az resource show --resource-group rg-spaarke-platform-{env} --name spaarke-provisioning-controlplane-worker-{env}/exchange-policy-sidecar --resource-type "Microsoft.Web/sites/sitecontainers" --query "properties.image" -o tsv` — must NOT be `mcr.microsoft.com/appsvc/staticsite:latest` |
| 4 | Platform KV secret `Sidecar-Shared-Secret` is populated | `az keyvault secret show --vault-name sprk-controlplane-{env}-kv --name Sidecar-Shared-Secret --query value -o tsv` — non-empty (**Wave G-8 Batch 4** — `scripts/provisioning/Seed-PlatformKeyVault.ps1`, commit `a083db73a`. NOTE: this is NOT H4 / task 125-126 — H4 seeds CUSTOMER-stamp Key Vaults only; the L2/platform-KV seed has no owner in the H-catalog, hence the dedicated Batch 4 script) |
| 5 | Worker has `keyVaultReferenceIdentity` PATCHed to the UAMI | `az webapp show -n spaarke-provisioning-controlplane-worker-{env} -g rg-spaarke-platform-{env} --query "properties.keyVaultReferenceIdentity" -o tsv` — matches UAMI resource id (**Wave G-8 Batch 4** — new step added to `scripts/provisioning/Deploy-ControlPlane.ps1`, commit `a083db73a`. NOTE: this is NOT H4 / task 125-126 — H4's T5 mitigation PATCHes CUSTOMER-stamp App Services only; L2's own `keyVaultReferenceIdentity` PATCH was previously unowned per audit gap #8) |
| 6 | Exchange app-registration + cert + mail-enabled test group provisioned (only required for check 5 full pass — see §5) | H3 output + operator-created test group + Exchange admin console verification |

If any of prereqs 1–5 fail, STOP and complete the live ceremony first. Prereq 6 is required only for the operator-override full pass of check 5 (get-before-set idempotency); the safe-default run does not need it.

---

## 4. Runbook steps (LIVE — run by the operator, NOT by an agent)

### Step 4.1 — Default safe run

```powershell
cd c:/code_files/spaarke-wt-customer-provisioning-orchestration-r1
./scripts/provisioning/Verify-Sidecar-Live.ps1 `
    -Environment dev `
    -ReportPath ./projects/customer-provisioning-orchestration-r1/notes/sidecar-live-verification-{yyyy-mm-dd}.json
```

Expected result: **5 PASS + 1 WARN** (WARN on check 5, idempotency, because the safe-default all-zero tenantId cannot demonstrate AlreadyCompliant — Connect-ExchangeOnline rejects it before get-before-set can run).

If ANY check reports FAIL:

- **CONTAINER_HEALTH FAIL** — sitecontainer configuration doesn't match expectations. Verify Bicep + redeploy Worker.
- **LOCALHOST_BIND FAIL** — sidecar container not running or Listener.ps1 crashed. Check Kudu log stream: `az webapp log tail -n spaarke-provisioning-controlplane-worker-dev -g rg-spaarke-platform-dev`. Look for `"Sidecar startup: missing required environment variables — refusing to bind port."` (missing env — env-var-KeyVault-reference not resolving; check keyVaultReferenceIdentity).
- **PUBLIC_ISOLATION FAIL** — HIGH-severity security finding. STOP and escalate per POML `<escalation>` trigger #1 (see §7).
- **ROUND_TRIP_AUTH FAIL with HTTP 401** — the shared-secret value in KV does not match what the sidecar was booted with. Likely operator forgot to restart the Worker after rotating the KV secret. Fix: `az webapp restart -n spaarke-provisioning-controlplane-worker-dev -g rg-spaarke-platform-dev`, then re-run.
- **AUTH_REJECTION FAIL** — HIGH-severity security finding. STOP and escalate per POML `<escalation>` trigger #2 (see §7).

### Step 4.2 — Full idempotency pass (operator opt-in, real Exchange tenant)

Only run this if you have a safely-scoped test Exchange tenant + a mail-enabled test group. Do NOT run this against production data — real `New-ApplicationAccessPolicy` calls will be made.

```powershell
./scripts/provisioning/Verify-Sidecar-Live.ps1 `
    -Environment dev `
    -TenantId <test-tenant-guid> `
    -PolicyScopeGroupId <mail-enabled-test-group-object-id> `
    -ExpectedAppIds @('<real-bff-app-reg-client-id>', '<real-uami-client-id>') `
    -ReportPath ./projects/customer-provisioning-orchestration-r1/notes/sidecar-live-verification-{yyyy-mm-dd}-full.json
```

Expected result: **6 PASS** including check 5's AlreadyCompliant on 2nd run.

If check 5 fails with `2nd run did NOT return AlreadyCompliant` — either the script's get-before-set failed, or Exchange's list-vs-create-timing is different than assumed. Read the `run2Output` field in the JSON report; compare against the `Set-ExchangeApplicationAccessPolicy.ps1` structured JSON envelope.

### Step 4.3 — C# client end-to-end verification (optional, deeper coverage)

To exercise the ACTUAL production `ExchangePolicySidecarClient` (not just curl) against the live sidecar, run the env-gated live-verification xUnit tests from a workstation that can reach the sidecar (either via a local `docker run` or a Kudu SSH tunnel — see §6 for tunnel setup):

```powershell
$env:SIDECAR_LIVE_VERIFY_URL='http://127.0.0.1:8091/'   # via tunnel or local docker
$env:SIDECAR_LIVE_VERIFY_SECRET='<value from az keyvault secret show>'
cd c:/code_files/spaarke-wt-customer-provisioning-orchestration-r1
dotnet test src/server/services/Sprk.Provisioning.ControlPlane.Tests/Sprk.Provisioning.ControlPlane.Tests.csproj `
    --nologo --no-build `
    --filter FullyQualifiedName~ExchangePolicySidecarLiveVerificationTests
Remove-Item env:SIDECAR_LIVE_VERIFY_URL, env:SIDECAR_LIVE_VERIFY_SECRET
```

Expected result: **4 PASS** (health, round-trip-with-valid-auth, auth-rejection-wrong-secret, empty-shared-secret-config-short-circuits). Without the env vars set the same tests short-circuit as no-op passes so they don't run in CI unmarked.

---

## 5. Check-by-check expected outputs

| Check | Success signal | Failure signals + likely cause | Security-critical? |
|---|---|---|---|
| CONTAINER_HEALTH | `sitecontainer 'exchange-policy-sidecar' configured on port 8091 with image '{acr-tag}'` | 404 (site or sitecontainer missing — deploy incomplete); wrong targetPort (Bicep drift) | No |
| LOCALHOST_BIND | `GET http://127.0.0.1:8091/healthz returned 200 'ok' from inside the Worker's network namespace` | ExitCode -1 (Kudu unreachable — operator RBAC); non-zero exit (Listener.ps1 crashed at startup — env-var resolution failure most likely) | No |
| PUBLIC_ISOLATION | `sidecar port 8091 correctly UNREACHABLE at public hostname` (timeout / connection refused) | 2xx response — sidecar port IS publicly reachable | **YES — POML `<escalation>` trigger #1** |
| ROUND_TRIP_AUTH | `sidecar accepted X-Sidecar-Auth (HTTP 200|400)` — anything not 401 | HTTP 401 (KV secret vs sidecar env mismatch; Worker needs restart after rotation) | No |
| ROUND_TRIP_IDEMP | `2nd run returned wire outcome AlreadyCompliant` | 2nd run returned different outcome — idempotency broken OR safe-default mode (WARN not FAIL) | No |
| AUTH_REJECTION | `sidecar correctly REJECTED (HTTP 401) a request with a wrong X-Sidecar-Auth header` | Any HTTP status other than 401 — auth path broken | **YES — POML `<escalation>` trigger #2** |

---

## 6. Optional: Kudu SSH tunnel for check 4.3 (C# client from workstation)

If the operator wants to run the env-gated xUnit tests (§4.3) from a workstation, the sidecar's private port needs a tunnel. Kudu SCM's `/DebugConsole` supports SSH tunneling but requires the operator to already be inside the App Service network namespace. Practical alternatives:

1. **Local Docker rehearsal** — build task 114's Dockerfile locally + `docker run -e SIDECAR_SHARED_SECRET=whatever -e PLATFORM_KV_URI=... -e EXCHANGE_CERT_SECRET_NAME=... -e EXCHANGE_CONNECT_APP_ID=... -p 8091:8091 sprk-provisioning-sidecar:local`. Note: the MSI + KV cert fetch legs won't work locally (no MSI endpoint outside App Service) — Listener.ps1 will fail at `Get-ExchangeCertificate` before invoking the script. That's fine for exercising the HTTP + auth paths (checks 1/3/4 from §4.3); real Exchange work requires the deployed environment.
2. **`az webapp ssh`** to the Worker + `curl http://127.0.0.1:8091/apply-policy` directly — no C# client involved but same wire-level verification as check 4.

Live-in-Azure C# execution is not required for the acceptance criteria — the PowerShell harness (§4.1) covers every acceptance criterion.

---

## 7. Escalation procedure (per POML `<escalation>` triggers)

### Trigger #1 — Public port reachable (PUBLIC_ISOLATION FAIL with 2xx)

If Verify-Sidecar-Live.ps1 reports `SECURITY-CRITICAL: sidecar port 8091 is REACHABLE at public hostname`:

1. **STOP** — do NOT re-run the harness, do NOT proceed with any customer provisioning against this environment.
2. Set the sitecontainer's `targetPort` back to a non-8091 value in Bicep, redeploy, verify the port is closed, ONLY then investigate.
3. Escalate to the owner with:
   - The full JSON report output (`-ReportPath` flag)
   - Az log stream output (`az webapp log tail`)
   - The exact URL that returned 2xx
   - Whether this is a first-run finding or a regression (compare against any prior verification report)
4. Root CLAUDE.md §6 security escalation applies — this is not a routine bug.

### Trigger #2 — Auth-rejection failed (AUTH_REJECTION FAIL with non-401)

If Verify-Sidecar-Live.ps1 reports `SECURITY-CRITICAL: sidecar returned HTTP {N} (expected 401) for a request with a WRONG X-Sidecar-Auth header`:

1. **STOP** — the sidecar is accepting unauthenticated requests. The Exchange-admin capability is not being properly guarded.
2. Read task 114 [`Listener.ps1`](../../src/server/services/Sprk.Provisioning.ControlPlane.Sidecar/Listener.ps1) lines 344-352 (the auth-check block) — was the file corrupted / did a deploy overwrite it with a wrong version?
3. Escalate to the owner with the full JSON report + `az webapp log tail` output.

---

## 8. What this runbook does NOT cover

- **New customer provisioning end-to-end.** That is Wave G-7's Phase F E2E acceptance run (task 186). This runbook only verifies the sidecar is deployable + isolable + reachable + authenticating.
- **Exchange app-only cert renewal.** Cert rotation is a platform-KV operation; the sidecar reads whatever is current at call time (Listener.ps1 line 138-173). Rotation testing is out of scope.
- **Sidecar cold-start timing measurement.** Task 161's `SidecarRequestTimeout` defaults to 6 minutes (accommodates ~10s cold-start + Listener.ps1's 300s advisory script timeout + margin); measuring actual observed cold-start is a nice-to-have but not an acceptance criterion.
- **Sidecar crash-restart in-flight-request behavior.** Deferred as a Wave G-7 chaos-testing item (task 186 or a Phase F follow-on); the reconciler's own resumability posture (§4C rollback + I6 crash-recovery) already covers this at the run level.

---

## 9. Handoff to next task

After a successful verification run:

1. Copy the JSON report file into [`notes/sidecar-live-verification-{yyyy-mm-dd}.md`](.) formatted per the [`h10-live-verification-2026-08.md`](h10-live-verification-2026-08.md) precedent.
2. Update TASK-INDEX.md row 162 🟡 → ✅ (live-ceremony complete).
3. Update `current-task.md` § Live Ceremony Backlog: mark sidecar verification as done, delete this runbook's line item from the pending list.
4. Wave G-7 (17-task final acceptance gate) is now UNBLOCKED and can dispatch — task 180 (T4 probe) + task 186 (Phase F E2E) both depend on this verification being complete.

---

## 10. Rollback procedure (if verification fails and mitigation isn't clear)

The sidecar's failure mode is Fail-Closed by construction — Listener.ps1 will not bind the port on missing env vars, and the sitecontainer is unregistered in Bicep by simply removing the `exchangePolicySidecar` resource block. Rollback steps:

1. Comment out (or delete) the `exchangePolicySidecar` resource in [`controlplane-worker-app-service.bicep`](../../infrastructure/bicep/modules/controlplane-worker-app-service.bicep) lines 297-325.
2. Redeploy: `./scripts/provisioning/Deploy-ControlPlane.ps1 -Target Worker` (script re-deploys code + honors the current Bicep sitecontainer registration — after Bicep is re-run the sitecontainer will be removed).
3. Note: with the sitecontainer removed, ANY H14a dispatch will fail with a connection-refused transport exception from `ExchangePolicySidecarClient` → HandlerResult classifies as Resumable → run stays in-progress on that step. This is deliberate; it prevents silent Exchange policy skipping.
4. Follow-up: once the root cause of the verification failure is identified + fixed, re-add the sitecontainer resource + re-run this runbook.

Rollback is safe because Option D quarantines every Exchange-admin operation into this sidecar — turning the sidecar off temporarily disables H14a without affecting any other handler in the DAG.
