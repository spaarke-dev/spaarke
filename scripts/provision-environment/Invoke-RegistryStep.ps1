<#
.SYNOPSIS
  /provision-environment Steps 1a (registry probe / upgrade detection) and 1f (placeholder create + verify).

.DESCRIPTION
  The registry is sprk_dataverseenvironment in the REGISTRY environment (name_templates.registryDvUrl.{env};
  spaarkedev1 for dev), not the customer's environment. The preferred reader/writer is the Dataverse MCP (an agent
  tool); pass its result in with -ProbeJson. When the MCP is disconnected (Fallback F1), -UseWebApi reads/writes
  through the Dataverse Web API with the operator's az token.

  -Action Probe    (1a) rows for sprk_customerid = customerId:
                     none                        → REGISTRY=new (fresh provisioning)
                     sprk_provisionedon not null → UPGRADE. Batch: needs intake.acknowledgeUpgradeMode (BAT-05) else
                                                   runs/pre-dispatch-upgrade-required.json + exit 1. Interactive: needs
                                                   -OperatorAnswer yes (exit 7 = ask "Continue as UPGRADE run? (yes/no)";
                                                   any other answer → exit 1). Records environmentId + isUpgradeRun.
                     otherwise                   → REGISTRY=in-progress (prior halt/quarantine/partial — Fallback F1 recovery)
  -Action Plan     (1f) prints the create payload for mcp__dataverse__create_record (stops if tenancyModel has no
                   sprk_tenancymodel mapping). Prints SKIP when environmentId is already known (upgrade / intake).
  -Action CreateWebApi (1f, Fallback F1) creates the row through the Web API and prints ENVIRONMENT_ID=<guid>.
  -Action Confirm  (1f, PRQ-05) -EnvironmentId from the create + the alt-key read-back (-ProbeJson or -UseWebApi):
                   the id must be a GUID and the read-back must return the same id → records environmentId. Exit 1 otherwise.
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$SessionFile,
  [Parameter(Mandatory)][ValidateSet('Probe', 'Plan', 'CreateWebApi', 'Confirm')][string]$Action,
  [string]$ProbeJson,
  [switch]$UseWebApi,
  [string]$OperatorAnswer,
  [string]$EnvironmentId
)

Import-Module (Join-Path $PSScriptRoot 'ProvisionEnvironment.psm1') -Force
$s = Read-ProvisionSession -SessionFile $SessionFile
$isBatch = $s.mode -eq 'batch'
$i = Get-ProvisionIntake -Session $s
$customerId = [string]$i['customerId']
$cpEnv = $s.controlPlaneEnv

function Get-ProbeRow {
  if ($UseWebApi) { return Get-RegistryRowByCustomerId -Env $cpEnv -CustomerId $customerId }
  if (-not $ProbeJson) { Stop-Provision '[skill] Pass the MCP read_query rows as -ProbeJson (JSON array; [] for none), or -UseWebApi when the MCP is disconnected.' }
  $rows = @($ProbeJson | ConvertFrom-Json -Depth 10)
  if ($rows.Count -gt 0 -and $rows[0].PSObject.Properties.Name -contains 'rows') { $rows = @($rows[0].rows) }
  return ($rows | Where-Object { $null -ne $_ } | Select-Object -First 1)
}

switch ($Action) {
  'Probe' {
    $row = Get-ProbeRow
    if (-not $row) { Write-Host "customerId '$customerId' is new (fresh provisioning)"; Write-Host 'REGISTRY=new'; exit 0 }
    if ($null -ne $row.sprk_provisionedon -and "$($row.sprk_provisionedon)" -ne '') {
      if ($isBatch) {
        if (-not (Get-BatchPolicies -Intake $i).acknowledgeUpgradeMode) {
          $diagPath = Write-ProvisionDiagnostic -Path 'runs/pre-dispatch-upgrade-required.json' -Data @{
            check      = 'upgrade-mode-detection'
            customerId = $customerId
            detected   = (Get-Date -Format 'o')
            reason     = "Prior sprk_provisionedon=$($row.sprk_provisionedon) row exists AND intake.acknowledgeUpgradeMode is false"
            remedy     = 'Set intake.acknowledgeUpgradeMode=true if the upgrade path is intended (see design.md §14A upgrade model), OR change customerId to a fresh identifier'
          }
          Stop-Provision "[skill] Batch HARD STOP (BAT-05): customerId '$customerId' has prior successful provisioning but intake.acknowledgeUpgradeMode is false. Diagnostic: $diagPath"
        }
        Write-Host "  [BATCH] Upgrade-mode acknowledged in intake — proceeding as UPGRADE run against existing environmentId=$($row.sprk_dataverseenvironmentid)"
      } else {
        if (-not $OperatorAnswer) {
          Write-Host "customerId '$customerId' has a prior successful run (sprk_provisionedon=$($row.sprk_provisionedon)). Continue as UPGRADE run? (yes/no)"
          Write-Host 'FIELD=upgradeConfirmation'
          exit 7
        }
        if ($OperatorAnswer -ne 'yes') { Stop-Provision 'Aborted at Step 1a upgrade-mode confirmation prompt.' }
      }
      Update-ProvisionSession -SessionFile $SessionFile -Values @{ environmentId = [string]$row.sprk_dataverseenvironmentid; isUpgradeRun = $true } | Out-Null
      Write-Host 'REGISTRY=upgrade'
      exit 0
    }
    Write-Host "customerId '$customerId' has a prior in-progress row (setupstatus=$($row.sprk_setupstatus)). See Fallback Matrix F1 recovery path."
    Write-Host 'REGISTRY=in-progress'
    exit 0
  }

  { $_ -in 'Plan', 'CreateWebApi' } {
    if ($s.environmentId) { Write-Host "SKIP — environmentId $($s.environmentId) is already known (upgrade run or intake.environmentId); no placeholder create."; exit 0 }
    # environment (intake) -> sprk_environmenttype enum (per DataverseEnvironmentRecord.cs EnvironmentType)
    $envTypeMap = @{ 'dev' = 0; 'demo' = 1; 'sandbox' = 2; 'trial' = 3; 'partner' = 4; 'training' = 5; 'prod' = 6 }
    $envType = $envTypeMap[$cpEnv]
    # tenancyModel -> sprk_tenancymodel option-set integer. T224 renamed the values to Model1/Model2 (integers unchanged).
    $tenancyModelInt = @{ 'Model1' = 0; 'Model2' = 1 }[[string]$i['tenancyModel']]
    if ($null -eq $tenancyModelInt) {
      Stop-Provision "❌ tenancyModel '$($i['tenancyModel'])' has no sprk_tenancymodel mapping (expected Model1 or Model2). Refusing to write a placeholder row without it."
    }
    $attributes = [ordered]@{
      sprk_name            = [string]$i['displayName']        # T237: the customer's full name (1a-bis; defaults to customerId)
      sprk_environmenttype = $envType
      sprk_dataverseurl    = [string]$i['dataverseEnvUrl']    # T228: the environment the operator created (Step 1b-bis) — H5 adopts it
      sprk_isactive        = $true
      sprk_isdefault       = $false
      sprk_customerid      = $customerId                      # ALT-KEY for L2 CustomerRunGuard + DataverseRegistryConcurrencyStore lookup
      sprk_tenantid        = [string]$i['tenantId']
      sprk_tenancymodel    = $tenancyModelInt                 # option-set integer, NOT string
      sprk_setupstatus     = 1                                # 1=InProgress (NotStarted=0, InProgress=1, Ready=2, Issue=3)
    }
    if ($Action -eq 'Plan') {
      Write-Host 'mcp__dataverse__create_record payload (entity sprk_dataverseenvironment):'
      [ordered]@{ entityName = 'sprk_dataverseenvironment'; attributes = $attributes } | ConvertTo-Json -Depth 4
      exit 0
    }
    try {
      $created = Invoke-RegistryWebApi -Env $cpEnv -Method POST -RelativeUri 'sprk_dataverseenvironments' -Body $attributes
    } catch {
      Stop-Provision "❌ Placeholder create failed — $($_.Exception.Message). Cannot proceed to POST /api/runs without environmentId. Check registry-env connection / az login."
    }
    Write-Host "ENVIRONMENT_ID=$($created.sprk_dataverseenvironmentid)"
    exit 0
  }

  'Confirm' {
    if (-not ($EnvironmentId -match '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')) {
      Stop-Provision '❌ Placeholder create failed — no valid GUID returned. Cannot proceed to POST /api/runs without environmentId. Check registry-env MCP connection or Web API auth.'
    }
    # PRQ-05: round-trip the row via the sprk_customerid alt-key to prove the CustomerRunGuard lookup path works
    # BEFORE Step 4 POSTs /api/runs (avoids discovering the gap inside L2's 409-return path).
    $row = Get-ProbeRow
    if (-not $row -or [string]$row.sprk_dataverseenvironmentid -ne $EnvironmentId) {
      Stop-Provision '❌ Post-create verification failed — sprk_customerid alt-key returned no row OR returned a different GUID than the create call. CustomerRunGuard concurrency path will 409-loop indefinitely. Check sprk_customerid_key alt-key is registered on the entity (see scripts/Add-CustomerIdColumn.ps1).'
    }
    Update-ProvisionSession -SessionFile $SessionFile -Values @{ environmentId = $EnvironmentId } | Out-Null
    Write-Host "  [PASS] Placeholder $EnvironmentId verified via sprk_customerid alt-key (sprk_setupstatus=$($row.sprk_setupstatus))." -ForegroundColor Green
    exit 0
  }
}
