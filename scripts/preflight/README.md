# scripts/preflight/ — H0 Preflight Quota Checks

> **Project**: customer-provisioning-orchestration-r1
> **Task**: 016 (Phase B)
> **Spec**: FR-01 (H0 preflight) · R19 · NFR-12 · §4B trap catalog · §4C rollback (Resumable class)
> **Design**: `projects/customer-provisioning-orchestration-r1/design.md` § handlers H0
> **Author**: SPAARKE platform DevOps
> **Created**: 2026-08-17

---

## Purpose

Four reusable PowerShell prep modules invoked by the **H0 preflight handler** (Wave C4, forthcoming) before H1 starts. Each module verifies quota headroom for **one Azure/PAC resource** required by the Model 2 dedicated-stamp provisioning pipeline. If ANY check fails, H0 blocks the run before H1 — surfacing quota bumps UP-FRONT (1–3 day lead time per spec.md § External Dependencies) instead of failing deep in H2a/H2b/H8 with cryptic "quota exceeded" errors that cascade into partial state + Quarantine per §4C.

**Why four checks** (per spec.md FR-01 + design.md § handlers H0):

1. **Azure OpenAI regional TPM headroom** — 150+200+30+350 per-model TPM sum per NFR-12
2. **Dataverse environment-creation rate** — ~4/hour per tenant typical
3. **Subscription vCPU quota** — per SKU family per region for App Service Plan + AI Search stamps
4. **SPE cert-bootstrap status** — cert present in KV AND ≥24h old (SPE replication complete per FR-11 T6)

---

## Shared Return Contract

**Every check returns a `[PSCustomObject]` with exactly these four fields:**

```powershell
[PSCustomObject]@{
    Result     = 'Pass' | 'Fail'          # required: string
    CheckName  = '<CheckName>'            # required: string (matches script basename)
    Headroom   = @{                       # required: hashtable (contents vary per check)
        observed = ...                    #   what the API reported
        required = ...                    #   what H0 needs for +1 customer
        region   = '<region>'             #   applicable region/env
        # additional per-check keys as needed (e.g., per-model breakdown)
    }
    Diagnostic = 'Human-readable message' # required: string
                                          # ON FAIL: MUST cite BOTH observed headroom AND requested capacity + region
                                          #          — actionable for operator to file a quota-bump request
}
```

**Exit codes:**
- `0` — check passed (Result = 'Pass')
- non-zero — check failed OR an unexpected error occurred (Result = 'Fail' or exception)

**H0 handler orchestrates all four in parallel** (per constraint: no cross-module state dependency). Any Fail → run blocked before H1.

---

## Script Inventory

| Script | Checks | Underlying API |
|---|---|---|
| `Test-AzureOpenAiTpmHeadroom.ps1` | Regional TPM headroom for gpt-4o + gpt-4o-mini + text-embedding-3-large + text-embedding-3-small (150+200+30+350 TPM sum per NFR-12) | `az cognitiveservices usage list --location <region>` |
| `Test-DataverseEnvCreationRate.ps1` | ≥1 environment-creation slot available in the current hourly bucket (~4/hr typical per tenant) | `pac admin list --query` (or Dataverse API for quota when PAC output unavailable) |
| `Test-SubscriptionVCpuQuota.ps1` | Per-SKU-family regional vCPU headroom for the expected +1-customer stamp (App Service Plan + AI Search) | `az vm list-usage --location <region>` |
| `Test-SpeCertBootstrap.ps1` | KV cert-secret exists AND is ≥24h old (SPE container-type replication complete per FR-11 T6) | `az keyvault secret show --vault-name <vault> --name <secret>` |

---

## Invocation Examples

Each script is independently invocable. Handler H0 orchestrates them in parallel (e.g. via PS7 `ForEach-Object -Parallel` or four sequential `Start-Job` calls that Wait-Job together).

### OpenAI TPM headroom
```powershell
$r = & ./Test-AzureOpenAiTpmHeadroom.ps1 `
    -SubscriptionId $env:SPAARKE_SUBSCRIPTION_ID `
    -Region        'eastus' `
    -RequestedTpmPerModel @{
        'gpt-4o'                    = 150
        'gpt-4o-mini'               = 200
        'text-embedding-3-large'    = 30
        'text-embedding-3-small'    = 350
    }
if ($r.Result -eq 'Fail') { Write-Host $r.Diagnostic; exit 1 }
```

### Dataverse env-creation rate
```powershell
$r = & ./Test-DataverseEnvCreationRate.ps1 `
    -TenantId $env:SPAARKE_TENANT_ID `
    -MinSlotsRequired 1
```

### Subscription vCPU quota
```powershell
$r = & ./Test-SubscriptionVCpuQuota.ps1 `
    -SubscriptionId $env:SPAARKE_SUBSCRIPTION_ID `
    -Region 'eastus' `
    -RequestedVCpuPerFamily @{
        'standardDv5Family' = 8
        'standardFsv2Family' = 4
    }
```

### SPE cert-bootstrap
```powershell
$r = & ./Test-SpeCertBootstrap.ps1 `
    -KeyVaultName $env:SPE_KV_NAME `
    -CertSecretName 'spe-owner-cert-pfx' `
    -MinAgeHours 24
```

---

## H0 Handler Consumption (Wave C4)

The H0 handler (forthcoming, tagged `l1-handler`, `orchestration`) will:

1. Read run parameters (region, subscriptionId, tenantId, KV name, cert secret name, per-model TPM, per-family vCPU) from Cosmos `ProvisioningRun.parameters`.
2. Dispatch all 4 checks in parallel; collect the 4 `PSCustomObject` results.
3. Aggregate: **any Result = 'Fail' → block the run** (Cosmos state → `Failed`; §4C class = `Resumable` per design.md § Rollback semantics; operator resolves external precondition + `POST /api/runs/{id}/resume`).
4. On `Pass` from all four: transition state to allow H1 to enqueue.

**H0's contract with this directory is the shared return object** — do not break the field names/types without a coordinated H0 handler change.

---

## Testing These Scripts

Each script has a "pass-path" mode and a "simulated fail-path" mode:

- **Pass path**: run against a healthy dev subscription/tenant/KV with reasonable RequestedTpm/vCPU values.
- **Fail path**: pass absurdly high requested values (e.g., `RequestedTpm = 999999`) or point at a KV that lacks the cert — verify the diagnostic cites both observed and requested.

The scripts do not depend on each other and can be tested independently (per FR-01 constraint: "no cross-module state").

---

## Escalation

If any Azure/PAC API drifts (command name changes, output-shape changes), STOP and escalate per root CLAUDE.md §6 — do NOT silently adapt. H0 handler depends on the return contract being stable.

---

## References

- **spec.md**: FR-01 (H0 preflight) · R19 (concurrency) · NFR-12 (regional TPM headroom) · §4B (silent-failure trap catalog)
- **design.md**: § handlers H0 (v3 catalog) · §4A tooling stack · §4C rollback (Resumable class)
- **.claude/constraints/azure-deployment.md**: broader Azure deployment safety rules (BFF publish size et al.)
- **scripts/common/Get-SpeConfidentialClientToken.ps1**: reusable helper structure/style precedent
