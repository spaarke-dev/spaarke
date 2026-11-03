# Fallback Matrix — F1 / F2 / F3

Moved from SKILL.md "Fallback Matrix" and the Step 6a background (2026-11-03). Per spec §4.3a.5. Three failure
modes, each as **primary → fallback → escalation trigger**. The scripts in `scripts/provision-environment/` apply the
automatic part; this file holds the decision trees, the operator prompts and the escalation.

What the scripts do:

| Mode | Implemented in | Behaviour |
|---|---|---|
| F1 | `ProvisionEnvironment.psm1` `Invoke-RegistryWebApi`; `Invoke-RegistryStep.ps1 -UseWebApi` / `-Action CreateWebApi`; `Sync-RegistryOnCompletion.ps1 -Action ApplyWebApi` | Fallback B (Web API with the operator's `az` token) against the registry env from `spaarke-constants.yaml` `name_templates.registryDvUrl.{env}` |
| F2 | `ProvisionEnvironment.psm1` `Invoke-L2Request` | 401 → re-acquire the L2-audience token, retry; 3 consecutive 401s → stop with the F2 message |
| F3 | `Watch-ProvisioningRun.ps1` (exit 9), `Start-ProvisioningRun.ps1` | 5xx / timeout → one retry after 30 s; a second failure → exit 9 with the escalation |

Corrections to the original text (annotated in place, original kept):

- **MOVE-MAP D2** — `pac data query` / `pac data update` do not exist (pac 1.46 `pac data` has only `export` / `import`).
  Fallback A cannot run; Fallback B is the fallback.
- **MOVE-MAP D11** — the F1 recipes write `sprk_currentrunid = null`. That contradicts Step 6a's single-writer rule
  (only `ICustomerRunGuard.ReleaseAsync` releases it — Bucket B HIGH#7). The scripts never write it; do not copy that
  line.
- **MOVE-MAP D13** — there is no `POST /api/runs/{id}/quarantine` route. "abandon" is `POST /api/runs/{id}/cancel`
  (confirm with the operator first).

## Contents

- F1. Dataverse MCP disconnect
- F2. `az` token expiry mid-run
- F3. L2 API unreachable
- Cross-references
- Step 6a background — the four registry states (MED#10)
## F1. Dataverse MCP disconnect

**Symptom**: `mcp__dataverse__*` calls return connection error, timeout, or "MCP server not connected."

**Impact**: Cannot query gate state via MCP; cannot update `sprk_dataverseenvironment` via MCP in Step 6.

**Decision tree**:

```
IF mcp__dataverse__* call fails with connection error:

  1. DO NOT attempt reconnect loop — MCP disconnects require the operator to
     re-authenticate the connector in VS Code / Claude Code settings, which
     is out-of-band. A skill-side reconnect will just retry the failed call.

  2. Log the fallback: "⚠ MCP disconnect at Step {N}; switching to pac data fallback"

  3. Fallback A (preferred if pac is authed): pac data
       # NOT AVAILABLE — pac 1.46 has no `pac data query/update` (MOVE-MAP D2). Skip to Fallback B.
       # Read
       pac data query --entity sprk_dataverseenvironment `
         --filter "sprk_customerid eq '{customerId}'" `
         --select sprk_dataverseenvironmentid,sprk_currentrunid,sprk_setupstatus

       # Write (Step 6 registry update — setupstatus 2 = Ready per EnvironmentSetupStatus enum)
       pac data update --entity sprk_dataverseenvironment `
         --id {envRecordId} `
         --data '{"sprk_provisionedon":"{timestamp}","sprk_setupstatus":2,"sprk_currentrunid":null}'

  4. Fallback B (if pac unavailable OR command shape not supported): raw Web API PS
       # Implemented: Invoke-RegistryWebApi (ProvisionEnvironment.psm1). It reads the registry URL from
       # spaarke-constants.yaml and never sends sprk_currentrunid (MOVE-MAP D11).
       # NOTE: registry updates target the REGISTRY env (spaarkedev1 for dev),
       # NOT the customer's just-provisioned env. `sprk_dataverseenvironment`
       # is the central catalog per operator memory feedback_no_central_managing_env_yet.
       $dvUrl = "https://spaarkedev1.crm.dynamics.com"  # registry env
       $dvToken = az account get-access-token --resource $dvUrl --query accessToken -o tsv
       $body = @{
         sprk_provisionedon = "{timestamp}"
         sprk_setupstatus   = 2         # 2 = Ready per EnvironmentSetupStatus enum
         sprk_currentrunid  = $null
       } | ConvertTo-Json
       Invoke-RestMethod `
         -Uri "$dvUrl/api/data/v9.2/sprk_dataverseenvironments({envRecordId})" `
         -Method PATCH `
         -Headers @{
           Authorization = "Bearer $dvToken"
           "OData-Version" = "4.0"
           "If-Match" = "*"
         } `
         -Body $body -ContentType "application/json"

  5. NOTE in handoff report which fallback was used + reason (MCP was
     disconnected). This becomes an operator action item — re-auth the MCP
     connector in VS Code settings BEFORE the next provisioning run.
```

**Escalation trigger**: BOTH primary + Fallback A + Fallback B fail. This is rare and indicates a broader Dataverse-connectivity issue (network, cert, or Dataverse-side outage). STOP + escalate; do NOT mark the run complete. Registry becomes stale (`CompleteButRegistryStale` state) until operator manually reconciles.

**Design intent**: MCP disconnect is a normal occurrence per our own experience (2026-08-14, 2026-08-15). The skill degrades gracefully via raw Web API (the original also named `pac data`, which has no query/update command — MOVE-MAP D2) (which reuses the `az` token operator already has). No user-facing prompt required.

---

## F2. `az` token expiry mid-run

**Symptom**: L2 REST call returns `401 Unauthorized` with `WWW-Authenticate: Bearer error="invalid_token"`; token acquired at start of run has aged past ~1 hour lifetime.

**Impact**: Skill loses ability to advance the run until token is refreshed.

**Decision tree**:

```
IF Invoke-RestMethod against $l2Base returns HTTP 401:

  1. DO NOT re-authenticate the operator — they are already logged in
     (`az login` succeeded at Step 0). Only refresh the L2-audience token.

  2. Refresh via:
       $token = az account get-access-token `
         --resource "api://spaarke.com/provisioning-controlplane-$env" `
         --query accessToken -o tsv

     `az` uses cached refresh tokens; this is silent + fast (<2s) if the
     operator's session is still valid.

  3. Retry the failed L2 call ONCE with the new token.

  4. IF retry also returns 401:
     - Operator's SSO session may have expired (rare inside a single skill
       invocation, but possible on long-running runs with human gates).
     - PAUSE the poll loop; prompt operator:
         "⚠ az token refresh failed. Please run 'az login' in a separate
          terminal and confirm here when done. The run continues in L2
          — it is not lost. Type 'refreshed' to resume, or 'abandon' to
          quarantine."
     - On 'refreshed' → re-acquire token + resume poll from where it stopped.
     - On 'abandon' → invoke POST /api/runs/{id}/quarantine with reason   [no such route — use POST /api/runs/{id}/cancel; MOVE-MAP D13]
       "operator session expired; abandoning skill session."

  5. Log every refresh event in the handoff report — repeated refreshes on
     the same run are a signal to shorten the run OR investigate token
     lifetime configuration on the L2 app-reg.
```

**Escalation trigger**: 3 consecutive refreshes fail. Indicates operator's `az` login is broken (tenant policy, MFA revocation, etc.). Skill hard-stops; operator addresses AAD state before re-invoking.

**Design intent**: Token expiry is expected on long runs (Model 2 full provisioning can exceed 1 hour). Auto-refresh keeps the operator out of the loop for the common case. The prompt is a last resort for genuinely-expired SSO sessions.

---

## F3. L2 API unreachable

**Symptom**: `POST /api/runs`, `GET /api/runs/{id}`, or `POST /api/runs/{id}/resume` returns 5xx (500, 502, 503, 504) OR the request times out (connection refused, DNS failure, gateway timeout).

**Impact**: Cannot advance the run through the skill. L2 continues running its state-reconciler independently (per FR-22 + FR-23 I6 crash-recovery) — the run is NOT lost; the skill is just cut off from steering it.

**Decision tree**:

```
IF L2 call returns 5xx OR times out:

  1. First occurrence: retry ONCE with 30s backoff. Transient errors
     (Azure ILB blip, App Service cold-start, Cosmos throttling) usually
     recover in <60s.

  2. If second attempt also fails, DO NOT auto-retry further. Auto-retry
     loops hide the outage from the operator + waste their time.

  3. ESCALATE IMMEDIATELY to the operator:

       🚨 L2 API UNREACHABLE

         Endpoint:  {failed URL}
         Response:  {status code + first 200 chars of body OR error}
         Elapsed:   {time since first failure}

         The provisioning run continues in L2 — your work is NOT lost.
         L2's state-reconciler (I6 crash-recovery) will resume orphan
         runs when L2 is back up.

         DIAGNOSTIC ACTIONS:
           1. Check L2 App Service status:
                az webapp show --resource-group rg-spaarke-{env} `
                  --name spaarke-provisioning-{env} --query state
              (expect "Running")

           2. Check L2 /healthz:
                curl -sf https://spaarke-provisioning-{env}.azurewebsites.net/healthz
              (expect 200)

           3. Check L2 App Insights for failures (last 15m):
                {App Insights URL for this L2 instance}

           4. Check Cosmos DB availability (partition rebalancing?):
                az cosmosdb show --resource-group rg-spaarke-{env} `
                  --name cosmos-spaarke-{env} --query provisioningState

           5. Escalate to on-call if outage exceeds 15 min:
                {on-call URL / channel}

         RESUME OPTIONS:
           - When L2 recovers, re-invoke:
                /provision-environment {customerId}
             The skill detects the in-progress run + resumes at Step 4 poll.
           - Do NOT create a new run for the same customerId — L2 will
             return 409 via optimistic concurrency lock on sprk_currentrunid.

  4. Write partial handoff report with status "SkillDetached" — captures
     what the skill saw before the outage. When operator re-invokes and
     the run reaches terminal state, a second (final) report is written
     at runs/{runId}.md (superseding the partial).
```

**Escalation trigger**: Any 5xx or timeout that doesn't clear after the first backoff. L2 outage is genuinely rare + operator-actionable.

**Design intent**: L2 owns run state; the skill is a thin driver. When L2 is unavailable, the skill can't drive but the state persists in Cosmos. I6 (FR-23) crash-recovery on L2 restart resumes orphan runs automatically. The skill's job is to surface the outage clearly + tell the operator how to check + reconnect — NOT to try to work around L2.

---

## Cross-references

- **Step 4 (Execute Loop)** — F1 (MCP not needed here) / F2 (token auto-refresh applies during poll) / F3 (L2 unreachable during poll) all in scope
- **Step 5 (Manual Gate Handling)** — F2 applies if operator's session expires during a long gate wait (Model 2 admin consent, SPE 24h)
- **Step 6 (Completion Handoff)** — F1 (MCP registry update fallback) is the primary use case; F2 (token refresh) rare but possible if the run took >1h
## Step 6a background — the four registry states (MED#10)

SKILL.md Step 6a keeps the rules; this is the reasoning behind them.

Per REG-04 (SESSION 15) and MED#10 (SESSION 19 — customer-provisioning-orchestration-r1 adversarial e2e verify workflow wepdcb8we, H13 Cosmos-first refactor): Step 6a is NOT belt-and-suspenders — it is the operator-side reconciliation for registry state that may have been PATCHed BEST-EFFORT by H13 after Cosmos-Completed already landed.

**Post-MED#10 semantic change** (binding, since SESSION 19): H13 writes to Cosmos FIRST (`RunStatus = Completed`), then attempts the registry PATCHes (promoted-columns → `sprk_setupstatus = Ready`) AFTER. If either registry PATCH fails, H13 logs a `REGISTRY-STALE (MED#10 SESSION-19)` warning + returns `HandlerResult.Success` — the run IS complete (Cosmos is authoritative), but the registry may show ANY combination of the following stale states:

| State | Cosmos | Registry columns (promoted) | Registry `sprk_setupstatus` |
|---|---|---|---|
| Full success (green path) | Completed | Ready-values | Ready |
| Columns PATCH failed | Completed | stale/missing | InProgress (short-circuited) |
| Setupstatus PATCH failed (columns OK) | Completed | Ready-values | InProgress |
| Both PATCHes failed | Completed | stale/missing | InProgress |

Step 6a MUST cover ALL FOUR cases. It:
1. Reads the current `sprk_dataverseenvironment` row via Dataverse MCP (or Web API fallback).
2. Compares observed columns to the run's Ready-state values (from `run.CompletedOn`, `run.InterStepState.*`, `run.Parameters.NonSecret.*`).
3. Writes the missing columns (idempotent PATCH — no-op if H13 already landed them).
4. Additionally sets `sprk_setupstatus` to Ready — the option-set value `2`, not the string `'Ready'` (MOVE-MAP D3) **IFF** the observed value is `InProgress` (indicating H13's setupstatus PATCH did not land). This is the MED#10-driven addition to the operator recovery recipe. On observed `Ready`, do NOT re-write (H13 already succeeded — a no-op re-write burns a Dataverse RU with no state change).

Prior REG-04 language "if that PATCH failed (leaving RunStatus=Running-blocked, not Completed), by the operator-side skill here" is NO LONGER TRUE after MED#10. RunStatus is Completed regardless of registry state; the operator-side skill picks up the residual registry PATCH in every failure combination above.

Step 6a never throws before Step 6b (Bucket B HIGH#10): a failed read or PATCH marks the registry stale
(`Sync-RegistryOnCompletion.ps1 -Action MarkStale`), 6b writes the handoff report on every outcome, 6c writes
`runs/{runId}-registry-stale.md`, and `Write-HandoffReport.ps1 -Action Check` exits 5 only after both exist. The
original heading's "HARD-STOP on any failure" was superseded by HIGH#10.
