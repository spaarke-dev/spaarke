# Preflight Report — {customerId}-{runId}

> **Created by**: `customer-provisioning-orchestration-r1` task 203a per punch list row A06 (template).
> Written by `/provision-environment` skill Step 2 (post-intake, post-prereqs, pre-execute).
> Contains H0's own preflight output + Skill Step 2's cost / duration / naming-collision / admin-consent snapshot.

## H0 output (handler-side preflight)

- Cosmos-run-id: `{cosmosRunDocId}`
- POST /api/runs payload:
  ```json
  {
    "customerId": "{customerId}",
    "tenantId": "{tenantId}",
    "environmentId": "{environmentId}",
    "tenancyModel": "{tenancyModel}",
    "profile": "{profile}",
    "region": "{region}"
  }
  ```
- H0 status: {Success | Failed | WaitingOnGate}
- H0 duration: {N} seconds

## Skill Step 2 preflight

| Check | Result | Evidence | Notes |
|---|---|---|---|
| Naming collision (H0.1 global-namespace-availability precheck) | ✅ / ❌ | `Test-AzResource -Name '…' -Type 'Microsoft.Storage/storageAccounts'` — all unique | Per `.claude/patterns/provisioning/resource-name-availability-precheck.md` |
| Admin-consent status (H0.5 gate) | ✅ granted / ⏸ pending / ❌ denied | Graph `POST /oauth2PermissionGrants` returned {200 \| 202 \| 403} | If pending → operator flow starts here |
| Cost estimate | `{USD}/mo` | Bicep what-if + Azure Retail Prices API | Compare against customer profile cap |
| Duration estimate | `{minutes} min` | Historical median from H0 + running-runs analytics | Add 24h for SPE container-type gate (H8) if applicable |
| Quota headroom | {N} of {M} regions have headroom | `az cognitiveservices model list --region westus2` | Feeds `.claude/patterns/provisioning/openai-quota-region-composition.md` |

## Decisions locked at preflight

- Region selection: `{region}` (rationale: {rationale})
- OpenAI region: `{openaiRegion}` if != `region` (per F4 pattern)
- Handler execution order: {ordered list of Hn}
- Manual gates expected: {list of {Hn.n} — H0.5, H1 (if quota-fail), H8 (if SPE container-type absent), H11 (if user-create)}

## Go / No-Go

- **GO** → skill advances to Step 4 execute loop; handler-log.md starts appending
- **NO-GO** → cite blocking check; run enters `PreflightFailed` state; operator remediates + re-runs Step 2
