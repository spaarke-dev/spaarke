<#
.SYNOPSIS
  /provision-environment Step 0 (0a, 0b, 0c, 0e, 0f, 0g) — operator machine, identity and L2 checks. Read-only.

.DESCRIPTION
  Runs every check and prints the Step 0g checklist. Any FAIL exits 1 (HARD STOP — do not proceed to Step 1).
  Step 0d (Dataverse MCP) is an agent tool call; record its outcome with Set-McpStatus.ps1.

  0a  pwsh >= 7.4, az >= 2.60, pac >= 1.35, git >= 2.40. -Batch adds (COMP-15): `az account show` exits 0 within 5 s.
  0b  az tenant == Spaarke tenant; signed in as a USER, never a service principal (NFR-11).
  0c  L2 /healthz 200; READ-ONLY role probe GET /api/runs/{random}?customerId=__role-probe__ (ISH-10 — never a POST).
  0e  inside a git work tree (warning only).
  0f  L2 App Service state Running (HARD STOP otherwise); /healthz buildTag mismatch is a WARNING (COMP-04).
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory)][ValidateSet('dev', 'demo', 'prod')][string]$Env,
  [switch]$Batch,
  [string]$SpaarkeTenantId = 'a221a95e-6abc-4434-aecc-e48338a1b2f2',
  [string]$ExpectedBuildTag = 'SESSION-15-wave-2-handler-01'   # placeholder until the L2 deploy pipeline stamps a build tag
)

Import-Module (Join-Path $PSScriptRoot 'ProvisionEnvironment.psm1') -Force

$rows = [System.Collections.Generic.List[object]]::new()
function Add-Row([string]$State, [string]$Text, [string]$Fix) { $rows.Add([pscustomobject]@{ State = $State; Text = $Text; Fix = $Fix }) }

# --- 0a. Tool versions -------------------------------------------------------------------------------------------
function Get-FirstVersion([string]$text) { if ($text -match '(\d+\.\d+(\.\d+)?)') { return [version]$Matches[1] } return $null }
$tools = @(
  @{ Name = 'pwsh';   Min = [version]'7.4';  Get = { $PSVersionTable.PSVersion.ToString() };                Fix = 'Install PowerShell 7.4+: https://aka.ms/powershell' }
  @{ Name = 'az-cli'; Min = [version]'2.60'; Get = { (az version -o json 2>$null | ConvertFrom-Json).'azure-cli' }; Fix = 'Upgrade: az upgrade (or https://aka.ms/installazurecli)' }
  @{ Name = 'pac';    Min = [version]'1.35'; Get = { ((pac 2>$null | Select-String 'Version:') -replace '.*Version:\s*', '') }; Fix = 'Upgrade: pac install latest' }
  @{ Name = 'git';    Min = [version]'2.40'; Get = { (git --version 2>$null) };                              Fix = 'Upgrade Git for Windows: https://git-scm.com' }
)
foreach ($t in $tools) {
  $raw = try { & $t.Get } catch { $null }
  $v = Get-FirstVersion ([string]$raw)
  if (-not $v) { Add-Row 'FAIL' "$($t.Name) not found" $t.Fix }
  elseif ($v -lt $t.Min) { Add-Row 'FAIL' "$($t.Name) $v (< $($t.Min))" $t.Fix }
  else { Add-Row 'PASS' "$($t.Name) $v" '' }
}

if ($Batch) {
  # COMP-15: batch dispatch needs a live az login with a fresh token cache.
  $job = Start-Job { az account show -o none 2>$null; $LASTEXITCODE }
  $done = Wait-Job $job -Timeout 5
  $code = if ($done) { Receive-Job $job } else { -1 }
  Remove-Job $job -Force -ErrorAction SilentlyContinue
  if ($code -ne 0) {
    Add-Row 'FAIL' 'az account show (batch, 5 s)' "Batch dispatch requires a fresh ``az login`` session. Run ``az login --scope api://spaarke.com/provisioning-controlplane-$Env/.default`` interactively before re-invoking."
  } else { Add-Row 'PASS' 'az account show (batch, < 5 s)' '' }
}

# --- 0b. Operator AAD identity ------------------------------------------------------------------------------------
$acct = az account show --query "{name:name, tenantId:tenantId, user:user.name, type:user.type}" -o json 2>$null | ConvertFrom-Json
if (-not $acct) {
  Add-Row 'FAIL' 'AAD identity: az account show failed' 'Run az login interactively.'
} else {
  $isSp = ($acct.type -eq 'servicePrincipal') -or ($acct.user -as [guid])
  if ($isSp) {
    Add-Row 'FAIL' "AAD identity: $($acct.user) is a service principal" 'L3 skill requires operator''s own AAD identity per NFR-11. Run `az login` interactively. Refusing to proceed under SP auth.'
  } elseif ($acct.tenantId -ne $SpaarkeTenantId) {
    Add-Row 'FAIL' "AAD identity: tenant $($acct.tenantId) is not the Spaarke tenant $SpaarkeTenantId" "az login --tenant $SpaarkeTenantId"
  } else {
    Add-Row 'PASS' "AAD identity: $($acct.user) (tenant: $($acct.tenantId))" ''
  }
}

# --- 0c. L2 reachability + read-only role probe (ISH-10) ----------------------------------------------------------
$l2Base = Get-L2Base -Env $Env
$token = Get-L2Token -Env $Env
if ([string]::IsNullOrWhiteSpace($token)) {
  Add-Row 'FAIL' "L2 token for api://spaarke.com/provisioning-controlplane-$Env" 'Token acquisition failed (AADSTS500011 = wrong env / app-reg not exposed). Run az login; verify az ad app show --id api://spaarke.com/provisioning-controlplane-{env}.'
} else {
  $health = try { Invoke-WebRequest -Uri "$l2Base/healthz" -Method GET -TimeoutSec 10 -SkipHttpErrorCheck } catch { $null }
  if ($health -and $health.StatusCode -eq 200) { Add-Row 'PASS' "L2 API reachable ($Env): $l2Base" '' }
  else { Add-Row 'FAIL' "L2 /healthz at $l2Base returned $(if ($health) { $health.StatusCode } else { 'no response' })" 'See Fallback F3 (references/fallback-matrix.md).' }

  # 404 proves auth + role (Reader or Operator). Operator-role is verified organically at Step 4 (a 403 there IS the signal).
  $probeUrl = "$l2Base/api/runs/$([Guid]::NewGuid().ToString())?customerId=__role-probe__"
  $probe = try { Invoke-WebRequest -Uri $probeUrl -Method GET -Headers @{ Authorization = "Bearer $token" } -TimeoutSec 15 -SkipHttpErrorCheck } catch { $null }
  $probeCode = if ($probe) { [string]$probe.StatusCode } else { 'none' }
  switch ($probeCode) {
    '404' { Add-Row 'PASS' "L2 reader/operator role check (HTTP $probeCode on read-probe)" '' }
    '200' { Add-Row 'PASS' "L2 reader/operator role check (HTTP $probeCode — probe GUID collision; retrying would return 404)" '' }
    '401' { Add-Row 'FAIL' 'L2 token rejected (HTTP 401).' 'Run `az login` interactively and retry.' }
    '403' { Add-Row 'FAIL' 'L2 rejected the operator''s identity with HTTP 403 — NO role assignment.' "Grant the operator's UPN at least the Reader app-role on 'api://spaarke.com/provisioning-controlplane-$Env' via Portal or 'az ad app app-role assignment create' (Operator role is required for the actual /provision-environment dispatch — Reader alone will pass this probe but 403 on Step 4 POST)." }
    default { Add-Row 'WARN' "Unexpected role-probe HTTP $probeCode against $probeUrl — proceeding cautiously; investigate if Step 4 POST returns 403." '' }
  }
  Add-Row 'INFO' 'Operator-role assignment is verified organically at Step 4 (POST /api/runs). Reader-tier verified here.' ''
}

# --- 0e. Working directory + git state ----------------------------------------------------------------------------
$top = git rev-parse --show-toplevel 2>$null
if ($top) {
  $dirty = @(git status --porcelain 2>$null).Count
  Add-Row 'PASS' "Working directory: $top ($dirty uncommitted change(s) — informational)" ''
} else {
  Add-Row 'WARN' "Working directory $(Get-Location) is not a git repo" "handoff report will be written to cwd but won't be checkpointed to git — consider running from repo root."
}

# --- 0f. L2 deployment probe (COMP-04) ----------------------------------------------------------------------------
$l2WebAppName = "spaarke-provisioning-controlplane-$Env"
$l2Rg = "rg-spaarke-platform-$Env"
$state = az webapp show -g $l2Rg -n $l2WebAppName --query state -o tsv 2>$null
if ($state -ne 'Running') {
  Add-Row 'FAIL' "L2 App Service '$l2WebAppName' is '$state' (expected 'Running')." 'Deploy L2 before /provision-environment.'
} else {
  Add-Row 'PASS' "L2 App Service $l2WebAppName Running" ''
  $h = try { Invoke-RestMethod -Uri "$l2Base/healthz" -Method GET -TimeoutSec 10 } catch { $null }
  if ($h -and $h.buildTag -and $h.buildTag -notmatch $ExpectedBuildTag) {
    Add-Row 'WARN' "L2 build-tag mismatch (got '$($h.buildTag)', expected match on '$ExpectedBuildTag'). Deployed image may lack the SESSION 15 Wave 2 fixes (HANDLER-01 DAG, REG-01 registry PATCH, ISH-01 tenantId validation). Run may HALT deep in the DAG." 'Deploy latest L2 image before proceeding.'
  }
}

# --- 0g. Report + gate --------------------------------------------------------------------------------------------
Write-Host ''
Write-Host 'PRE-FLIGHT CHECKS'
foreach ($r in $rows) {
  $color = switch ($r.State) { 'PASS' { 'Green' } 'FAIL' { 'Red' } 'WARN' { 'Yellow' } default { 'Cyan' } }
  Write-Host ("  [{0}] {1}" -f $r.State, $r.Text) -ForegroundColor $color
  if ($r.State -in 'FAIL', 'WARN' -and $r.Fix) { Write-Host "         Resolution: $($r.Fix)" }
}
if (@($rows | Where-Object State -eq 'FAIL').Count -gt 0) {
  Stop-Provision '[skill] Step 0 HARD STOP — resolve every FAIL above, then rerun Step 0.'
}
Write-Host 'All prerequisite checks passed (record Step 0d MCP status next).' -ForegroundColor Green
exit 0
