<#
.SYNOPSIS
  Shared helpers for the /provision-environment skill scripts (.claude/skills/provision-environment/SKILL.md).

.DESCRIPTION
  The skill runs each step as a separate `pwsh -NoProfile -File <script>` call, and no PowerShell variable survives
  between calls. State is therefore passed through two files:

    * the INTAKE file — the operator's answers, in the intake.schema.json shape. Batch mode: the operator's own file
      (never modified: its SHA-256 is sent to L2 as the audit record). Interactive mode: the skill writes
      runs/{customerId}-intake.json (ignored by git — `runs/*-intake.json`) from the operator's answers.
    * the SESSION file — runs/provision-session-{timestamp}.json (ignored by git — `runs/provision-session-*.json`).
      Values the skill derives or decides: mode, control-plane env, environmentId, runId, the confirmation phrase,
      MCP fallback state, registry-stale flag, prerequisite results. No personal data.

  Every script takes -SessionFile; New-ProvisionSession.ps1 creates it.
#>

Set-StrictMode -Off

$script:ConfirmationLiteral = 'proceed with provisioning'

function Get-ProvisionRepoRoot {
  $root = git rev-parse --show-toplevel 2>$null
  if (-not $root) { $root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path }
  return $root
}

function Stop-Provision {
  <# Prints the message the way the original procedure did (Write-Error text) and exits with the given code. #>
  param([Parameter(Mandatory)][string]$Message, [int]$Code = 1)
  [Console]::Error.WriteLine($Message)
  Write-Host $Message -ForegroundColor Red
  exit $Code
}

function Read-ProvisionSession {
  param([Parameter(Mandatory)][string]$SessionFile)
  if (-not (Test-Path -LiteralPath $SessionFile)) {
    Stop-Provision "[skill] Session file '$SessionFile' not found. Start with scripts/provision-environment/New-ProvisionSession.ps1."
  }
  $raw = Get-Content -LiteralPath $SessionFile -Raw | ConvertFrom-Json -AsHashtable -Depth 20
  return $raw
}

function Update-ProvisionSession {
  <# Merges -Values into the session file and writes it back. Returns the merged session. #>
  param([Parameter(Mandatory)][string]$SessionFile, [Parameter(Mandatory)][hashtable]$Values)
  $s = Read-ProvisionSession -SessionFile $SessionFile
  foreach ($k in $Values.Keys) { $s[$k] = $Values[$k] }
  $s['updatedAt'] = (Get-Date -Format 'o')
  ($s | ConvertTo-Json -Depth 20) | Set-Content -LiteralPath $SessionFile -Encoding utf8
  return $s
}

function Get-ProvisionIntake {
  <#
    Loads the intake file named in the session and applies the normalisations the original inline code applied
    when it bound the values (Step 1.0 / 1a-bis / 1e-bis). Returns a hashtable.
  #>
  param([Parameter(Mandatory)][hashtable]$Session)
  $path = $Session['intakeFile']
  if (-not $path -or -not (Test-Path -LiteralPath $path)) {
    Stop-Provision "[skill] Intake file '$path' not found. Interactive mode: write the operator's answers to runs/{customerId}-intake.json first (SKILL Step 1)."
  }
  $i = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable -Depth 10
  if (-not $i) { $i = @{} }
  # Step 1a-bis / T237: displayName defaults to customerId.
  if ([string]::IsNullOrWhiteSpace([string]$i['displayName'])) { $i['displayName'] = $i['customerId'] }
  # Step 1e-bis: @($null).Count is 1 in PowerShell — normalise first, or an unassigned users list looks like one entry.
  $i['users'] = @(@($i['users']) | Where-Object { $null -ne $_ })
  $i['customerWorkforceTenantIds'] = @(@($i['customerWorkforceTenantIds']) | Where-Object { $null -ne $_ })
  # Step 1e-bis / T232 (owner D2): Model 1 customer users are B2B guests — the preset is not a choice there.
  if ($i['tenancyModel'] -ceq 'Model1' -and [string]::IsNullOrEmpty([string]$i['identityPreset'])) { $i['identityPreset'] = 'B2BGuest' }
  # PRQ-C-14 is a boolean attestation; absent = false. Interactive mode asks once when it is absent (Step 1e-bis).
  $i['_b2bAttestationAnswered'] = ($i.ContainsKey('customerOutboundB2BAttested') -and $null -ne $i['customerOutboundB2BAttested'])
  $i['customerOutboundB2BAttested'] = [bool]$i['customerOutboundB2BAttested']
  # Step 1e-bis normalises GUID-shaped values to the 'D' form (lower case) before they are sent (POST /api/runs rules).
  $g = [guid]::Empty
  if ([guid]::TryParse([string]$i['environmentSecurityGroupId'], [ref]$g) -and $g -ne [guid]::Empty) { $i['environmentSecurityGroupId'] = $g.ToString('D') }
  $i['customerWorkforceTenantIds'] = @($i['customerWorkforceTenantIds'] | ForEach-Object {
      $w = [guid]::Empty
      if ([guid]::TryParse(([string]$_).Trim(), [ref]$w) -and $w -ne [guid]::Empty) { $w.ToString('D') } else { [string]$_ } })
  return $i
}

function Get-BatchPolicies {
  <# BAT-04..09 skill-local control-flow policies with the schema defaults (Step 1.0). #>
  param([hashtable]$Intake)
  return @{
    mcpDisconnectPolicy    = if ($Intake['mcpDisconnectPolicy']) { $Intake['mcpDisconnectPolicy'] } else { 'failFast' }         # BAT-04 → Step 0d
    acknowledgeUpgradeMode = [bool]$Intake['acknowledgeUpgradeMode']                                                               # BAT-05 → Step 1a
    onFailedPolicy         = if ($Intake['onFailedPolicy']) { $Intake['onFailedPolicy'] } else { 'abandon' }                    # BAT-07 → Step 4b Failed
    onQuarantinedPolicy    = if ($Intake['onQuarantinedPolicy']) { $Intake['onQuarantinedPolicy'] } else { 'failFast' }        # BAT-07 → Step 4b Quarantined
    onManualGatePolicy     = if ($Intake['onManualGatePolicy']) { $Intake['onManualGatePolicy'] } else { 'waitAndExit' }       # BAT-08 → Step 5
    postmortemFile         = $Intake['postmortemFile']                                                                         # BAT-09 → Step 7b
  }
}

function Get-L2Base {
  param([Parameter(Mandatory)][string]$Env)
  if ($Env -eq 'prod') { return 'https://spaarke-provisioning-controlplane-prod.azurewebsites.net' }
  if ($Env -eq 'demo') { return 'https://spaarke-provisioning-controlplane-demo.azurewebsites.net' }
  return 'https://spaarke-provisioning-controlplane-dev.azurewebsites.net'
}

function Get-L2Token {
  <# The operator's own token for the L2 audience (NFR-11) — never a service principal. #>
  param([Parameter(Mandatory)][string]$Env)
  $t = az account get-access-token --resource "api://spaarke.com/provisioning-controlplane-$Env" --query accessToken -o tsv 2>$null
  return $t
}

function Invoke-L2Request {
  <#
    One L2 REST call with the Fallback F2 token rule: on 401 re-acquire the L2-audience token and retry; after
    3 consecutive 401s stop (Bucket B LOW#4). Any other error is rethrown for the caller (Fallback F3).
  #>
  param(
    [Parameter(Mandatory)][string]$Env,
    [Parameter(Mandatory)][ValidateSet('GET', 'POST')][string]$Method,
    [Parameter(Mandatory)][string]$Uri,
    [string]$Body,
    [string]$RunId = '<runId>'
  )
  $maxConsecutive401 = 3
  $consecutive401 = 0
  $token = Get-L2Token -Env $Env
  while ($true) {
    try {
      $req = @{ Uri = $Uri; Method = $Method; Headers = @{ Authorization = "Bearer $token" } }
      if ($Body) { $req.Body = $Body; $req.ContentType = 'application/json; charset=utf-8' }   # names may be non-ASCII
      return Invoke-RestMethod @req
    } catch {
      $status = $null
      if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
      elseif ($_.Exception.StatusCode) { $status = [int]$_.Exception.StatusCode }
      if ($status -ne 401) { throw }
      $consecutive401++
      if ($consecutive401 -ge $maxConsecutive401) {
        throw "Poll loop hit $maxConsecutive401 consecutive 401s after token refresh — escalate per Fallback F2. Operator's AAD context is broken; run 'az login' + rerun skill with -Resume $RunId."
      }
      Write-Warning "L2 call got 401 (attempt $consecutive401/$maxConsecutive401) — re-acquiring L2-audience token via 'az account get-access-token' then retrying."
      $token = Get-L2Token -Env $Env
      if ([string]::IsNullOrWhiteSpace($token)) {
        throw "az account get-access-token returned empty for api://spaarke.com/provisioning-controlplane-$Env — az context lost. Run 'az login' + rerun skill with -Resume $RunId."
      }
    }
  }
}

function Import-SpaarkeYaml {
  <# Step 0.5a: powershell-yaml provides ConvertFrom-Yaml (one-time install, idempotent). #>
  if (-not (Get-Module -ListAvailable -Name powershell-yaml)) {
    Install-Module powershell-yaml -Scope CurrentUser -Force -Confirm:$false
  }
  Import-Module powershell-yaml
}

function Get-SpaarkeConstants {
  Import-SpaarkeYaml
  $path = Join-Path (Get-ProvisionRepoRoot) 'scripts/provisioning-prereqs/spaarke-constants.yaml'
  return (Get-Content $path -Raw | ConvertFrom-Yaml)
}

function Write-ProvisionDiagnostic {
  <# Writes a JSON diagnostic under runs/ (pre-dispatch or per-run) and returns its path. #>
  param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)]$Data)
  $dir = Split-Path -Parent $Path
  if ($dir) { New-Item -Path $dir -ItemType Directory -Force -ErrorAction SilentlyContinue | Out-Null }
  ($Data | ConvertTo-Json -Depth 6) | Set-Content -Path $Path -Encoding utf8
  return $Path
}

function Get-RegistryDvUrl {
  <# The REGISTRY environment (spaarkedev1 for dev) — not the customer's environment. #>
  param([Parameter(Mandatory)][string]$Env)
  $c = Get-SpaarkeConstants
  return [string]$c.name_templates.registryDvUrl.$Env
}

function Invoke-RegistryWebApi {
  <#
    Fallback F1 "B" — the registry table through the Dataverse Web API with the operator's own az token.
    Used when the Dataverse MCP is disconnected. (`pac data query/create/update` — F1 "A" — do not exist in
    pac 1.46: `pac data` has only export/import. See MOVE-MAP defect D2.)
  #>
  param(
    [Parameter(Mandatory)][string]$Env,
    [Parameter(Mandatory)][ValidateSet('GET', 'POST', 'PATCH')][string]$Method,
    [Parameter(Mandatory)][string]$RelativeUri,
    [hashtable]$Body
  )
  $dvUrl = Get-RegistryDvUrl -Env $Env
  if ([string]::IsNullOrWhiteSpace($dvUrl)) {
    throw "registry env dvUrl not resolvable for controlPlaneEnv='$Env' — verify scripts/provisioning-prereqs/spaarke-constants.yaml name_templates.registryDvUrl.$Env is populated"
  }
  $dvUrl = $dvUrl.TrimEnd('/')
  $dvToken = az account get-access-token --resource $dvUrl --query accessToken -o tsv
  if ([string]::IsNullOrWhiteSpace($dvToken)) {
    throw "az token acquisition failed for resource '$dvUrl' — operator's AAD context lost (run 'az login')"
  }
  $headers = @{ Authorization = "Bearer $dvToken"; 'OData-Version' = '4.0'; 'OData-MaxVersion' = '4.0'; Accept = 'application/json' }
  if ($Method -eq 'PATCH') { $headers['If-Match'] = '*' }
  if ($Method -eq 'POST') { $headers['Prefer'] = 'return=representation' }
  $req = @{ Uri = "$dvUrl/api/data/v9.2/$RelativeUri"; Method = $Method; Headers = $headers }
  if ($Body) { $req.Body = ($Body | ConvertTo-Json -Depth 5); $req.ContentType = 'application/json; charset=utf-8' }
  return Invoke-RestMethod @req
}

function Get-RegistryRowByCustomerId {
  <# Alt-key style lookup on sprk_customerid (Fallback F1 B). Returns the first row or $null. #>
  param([Parameter(Mandatory)][string]$Env, [Parameter(Mandatory)][string]$CustomerId)
  $cid = $CustomerId.Replace("'", "''")
  $r = Invoke-RegistryWebApi -Env $Env -Method GET -RelativeUri ("sprk_dataverseenvironments?`$select=sprk_dataverseenvironmentid,sprk_customerid,sprk_provisionedon,sprk_setupstatus,sprk_currentrunid,sprk_bffappid&`$filter=sprk_customerid eq '{0}'&`$top=1" -f $cid)
  return @($r.value)[0]
}

function Test-SetupStatusReady {
  <# sprk_setupstatus is an option set (EnvironmentSetupStatus: NotStarted=0, InProgress=1, Ready=2, Issue=3).
     The MCP may return the label; the Web API returns the integer. #>
  param($Value)
  return ("$Value" -eq '2' -or "$Value" -eq 'Ready')
}

Export-ModuleMember -Function * -Variable ConfirmationLiteral
