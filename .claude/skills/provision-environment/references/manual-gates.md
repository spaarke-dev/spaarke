# Manual gates — Step 5 operator messages (interactive mode)

Moved from SKILL.md Step 5 (2026-11-03). SKILL.md keeps the rules (advance, never resume; never trust the operator's
assertion). `Watch-ProvisioningRun.ps1` exits 4 at a gate and prints `GATE_ID=` / `PHASE=`; batch mode applies
`onManualGate` inside that script and never reaches these messages.

Run-record fields (MOVE-MAP D5): the run record has no `gateId`, `gateReason` or `gateInstructions` field. The gate id is
the `gateStates` key whose `status` is `Pending`; the reason and instructions come from that entry's `evidence`, the
run's `currentPhase` and `errorDetail`, and the messages below. Where a message says `{gate reason from L2 response}`,
fill it from those.

`abandon` (MOVE-MAP D13): the control plane has no quarantine endpoint (`RunsEndpoints.cs` maps create, preflight, get,
gate advance, resume, cancel and clear-quarantine). An operator `abandon` is `POST /api/runs/{runId}/cancel?customerId=`
— confirm with the operator first, as for any mutation — then Step 6 and Step 7 as for a cancelled run.

## Contents

- What a gate holds up (DAG)
- 5a. H0.5 Model 2 admin consent
- 5b. H1 Azure quota bump
- 5c. H8 SPE container-type replication
- 5d. Generic pattern for any other gate
- Advance, never resume

## What a gate holds up

> **What a gate holds up (task 245a DAG edges).** H3 (the per-customer BFF app registration, which also
> verifies the customer's admin consent on Model 2) is an ancestor of H4b, H6, H8 and H9, and through them
> of H7 onward, because each reads H3's `bffAppRegId`. While H3 waits on consent, the Dataverse branch
> (H6 solution import → H7 env vars) waits with it; only H2b and H5 keep moving. Expect that when
> estimating a gated run's time.

## 5a. H0.5 Model 2 admin consent (Model 2 only)

```
🔔 MANUAL GATE: Customer admin consent required (Model 2)

  Handler: H0.5 consent-callback
  NOTE (T258): the BFF maps this route only with `Onboarding__Enabled=true`, which no stamp sets; the endpoint cannot carry consent to L2 as built (ISS-013).
  Reason:  The customer's BFF app-reg (created by H3) needs admin consent on the customer's
           Entra tenant before H10 can create a Dataverse Application User.

  ACTION FOR CUSTOMER ADMIN (send this URL to the customer — skill substitutes {tokens} before display):
    URL construction:
      # T227a: there is no shared BFF app id or base URL in spaarke-constants.yaml. Model 2 is out of scope (D-12);
      # if it returns, take the customer's BFF app id from the run (H3's InterStepState.BffAppRegId via GET /api/runs/{runId})
      # and the callback base from the customer stamp's BFF host name.
      $bffAppId = '<customer BFF app id from the run>'
      $callback = "https://<customer stamp BFF host>/api/onboarding/consent-callback"
      $consentUrl = "https://login.microsoftonline.com/$tenantId/adminconsent" +
                    "?client_id=$bffAppId&redirect_uri=$([Uri]::EscapeDataString($callback))&state=$runId"
      Write-Host $consentUrl

    Example (shape only — real values substituted at runtime):
      https://login.microsoftonline.com/<customer-tenant-guid>/adminconsent
        ?client_id=<multitenant-bff-app-id>
        &redirect_uri=https%3A%2F%2Fspaarke-bff-prod.azurewebsites.net%2Fapi%2Fonboarding%2Fconsent-callback
        &state=<runId>

  The customer admin clicks, signs in with a Global Admin account, and consents.
  H0.5 will auto-detect the callback (HMAC-verified) and advance the run.

  Skill will auto-poll every 30s for the next 2 hours. If no callback in 2h,
  the skill will pause + ask you what to do.

  Type 'status' anytime to see current wait state.
  Type 'abandon' to abort the run + quarantine (requires clear-quarantine to resume).
```

## 5b. H1 Azure quota bump (rare)

```
🔔 MANUAL GATE: Azure quota bump required (H1)

  Handler: H1 resource-group provisioning
  Reason:  Target subscription hit soft cap on {App Service PremiumV3 workers /
           Storage GB / whatever the specific quota is}. H0 does not check
           App Service quota (no readable API; T186 L17), so it surfaces here.

  ACTION FOR OPERATOR:
    1. Open Azure Portal → Subscription → Usage + Quotas → filter by {quota-name}
    2. Request quota increase (may require Microsoft support ticket)
    3. Wait for approval email (usually 15-60 min for standard bumps)
    4. Return here and type 'advance' to have L2 re-verify quota + release the gate

  Skill call on 'advance': POST /api/runs/{runId}/gates/{gateId}/advance?customerId={cid}
  (NOT /resume — /resume is for retrying a Failed run per RunsEndpoints.cs:232-244.)
```

## 5c. H8 SPE container-type replication (per operator memory `feedback_spe_container_timing`, MS's documented 24h wait is near-instantaneous in practice)

```
🔔 MANUAL GATE: SPE container-type replication in progress (H8)

  Handler: H8 spe-container-create
  Reason:  Container-type created successfully; H8.a needs Microsoft-side replication
           to complete before it can verify or H9 can bind BFF to the container.

  ACTION: none required. The skill polls H8.a on this schedule:
    - Minutes 0-15: every 30-60s (empirical: near-instant per operator memory
      `feedback_spe_container_timing`; 2026-08-22 Model 1 Prod stand-up saw
      replication complete within ~2 min)
    - Minutes 15-60: every 5 min
    - Hour 1+: alert operator + fall back to 25h ceiling (defensive; almost
      never fires in practice)

  The skill will not exit the session; it stays on this gate until H8.a succeeds
  OR operator types 'abandon'.
```

## 5d. Generic pattern for any other `WaitingOnGate`

```
🔔 MANUAL GATE: {gate name} ({handler name})

  Reason: {gate reason from L2 response}
  Action: {gate.instructions from L2 response}

  Type 'advance' when the action is complete — skill will POST
  /api/runs/{runId}/gates/{gateId}/advance?customerId={cid} and let L2 re-verify.
  Type 'abandon' to quarantine the run.
```

## Advance, never resume

**IMPORTANT**: NEVER auto-advance past a gate by trusting the operator's assertion. Always call `POST /api/runs/{id}/gates/{gateId}/advance?customerId={cid}` (per `RunsEndpoints.cs` GateAdvance handler) and let L2 re-verify the underlying condition (Dataverse query, Graph query, Azure resource state). If verification fails, the run stays at `WaitingOnGate`. `/resume` is ONLY for retrying a Failed run — using it for gate advance either does nothing (if run is still WaitingOnGate) or wastes a retry budget entry (if run has since transitioned to Failed).
