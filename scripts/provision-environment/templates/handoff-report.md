# Provisioning Run {runId}

- **Customer**: {customerId}
- **Tenant**: {tenantId}
- **Tenancy Model**: {tenancyModel}
- **Profile**: {profile}
- **Started**: {startedAt}
- **Completed**: {completedAt}
- **Wall-clock duration**: {duration}
- **Status**: {status}
- **L2 run URL**: {l2Base}/api/runs/{runId}

## Handler outcomes

<!-- FILL: one row per handler from GET /api/runs/{runId} (completedPhases: phase, startedAt, completedAt; failedHandlers). Columns: # | Handler | Status | Duration | Notes. -->

## Traps verified (T1-T7)

<!-- FILL: T1 keyVaultReferenceIdentity == UAMI; T2 Dataverse App User for MI; T3 UAMI Graph app-role parity; T4 stamp identity's Exchange mailbox roles in the customer's group; T5 both slot MIs KV RBAC; T6 SPE owning app via the Worker UAMI's FIC; T7 both BFF slots carry Customer__Id == customerId. Mark each from H13's gate evidence (gateStates); "not verified" when the run did not reach H13. -->

## Invariants verified (I1-I5)

<!-- FILL: I1 no hardcoded tenant; I2 AI Search tenantId filter; I3 Cosmos partition-key predicate; I4 the BFF is configured with this run's container type and container; I5 Graph per-tenant token. From H13's evidence; "not verified" when the run did not reach H13. -->

## Cost snapshot

- Estimated monthly: ${estimatedMonthlyUsd}/mo (tier {tier}, ceiling ${tierCeiling}/mo)
- vs envelope: <!-- FILL: within limits / +N% over (H13 flags > 20% drift) and escalation notes -->

## Registry state

`sprk_dataverseenvironment` for customerId={customerId}:
- sprk_provisionedon: {provisionedOn}
- sprk_bffversion: {bffVersion}
- sprk_solutionversion: {solutionVersion}
- sprk_setupstatus: {setupStatus}

## Secure-record verification (Step 6e)

<!-- FILL: each item of docs/guides/SECURE-PROJECT-ENVIRONMENT-SETUP.md §7 with its result; any failure → "NOT secure-record ready". -->

## Copilot agent (Step 6f)

<!-- FILL: auth-config id, package path, version, manifest id, SHA-256 — or PENDING (batch mode / gate not done). -->

## Deviations / notes

<!-- FILL: manual gates encountered, quota bumps requested, drift detected, fallbacks used (MCP F1 / token F2 / L2 F3) and why. -->

## Next steps

- Notify customer admin: <!-- FILL: URL to send them / instructions -->
- Post-provision smoke tests: <!-- FILL: list from customer-comms template U-CB-01 -->
- Monitor for 24h via App Insights: <!-- FILL: URL -->
- Identity-link writes (T255): after one report-only `identity-link-reconciliation` cycle has been reviewed, set `IdentityLink__Reconciliation__WritesEnabled=true` on BOTH BFF slots (deployment guide §6.5.2) — provisioning never sets it
