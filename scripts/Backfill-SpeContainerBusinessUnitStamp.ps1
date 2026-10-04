#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Stamps EXISTING SharePoint Embedded containers with the business unit that owns them — the backfill for owner
    round 20 item 1 (unified-access-control-r2 task 165). Dry-run by default; every write is reversible.

.DESCRIPTION
    THE BINDING. Every SPE container carries its owning business unit as the fileStorageContainer custom property
    'spaarkeBusinessUnitId' (the BFF constant SpeContainerBusinessUnitStamp.PropertyName). The SPE admin plane
    authorizes every container, item, permission and bulk route PER CONTAINER against it: a container bound to a unit
    the admin reaches (their own unit or a descendant); an UNBOUND container only for a root-unit admin; an unreadable
    binding fails closed. New containers are stamped by the BFF when it creates them. This script stamps the ones that
    already exist.

    WHICH UNIT — derived ONLY from the authoritative records that created or own the container. Never guessed:
      1. businessunit.sprk_containerid            the unit whose default container it is
      2. a SECURE root's own sprk_containerid      (sprk_matter / sprk_project / sprk_workassignment with
                                                    sprk_issecure = true) -> the root's owningbusinessunit
                                                    (the Secure Record unit; provisioning stamps the same)
      3. sprk_speauditlog 'CreateContainer' (201) the admin-plane create: the row's own business unit, else the
                                                    config's business unit
    Several claimants -> their NEAREST COMMON ANCESTOR in the business-unit hierarchy: the narrowest unit whose
    administrators reach every claimant (a container shared by a unit and its parent belongs to the parent; one shared
    by two sibling customers belongs to whoever sits above both). A claimant the hierarchy does not contain makes the
    container UNDERIVABLE. A configuration's sprk_defaultcontainerid is NOT a source: an administrator types it in.

    WHAT IT WILL NOT DO.
      - Stamp a container no authoritative record claims. Those are LISTED as UNDERIVABLE and left unbound (only a
        root-unit admin reaches them). In a shared Model 1 consuming tenant another environment's containers appear
        here too: they are underivable or FOREIGN and must never be stamped by this environment.
      - Overwrite an existing stamp. A stamp that differs from the derivation (MISMATCH), names a unit this
        environment does not know (FOREIGN) or is not one GUID (MALFORMED) is LISTED for an operator, never changed.

    SAFETY MODEL
      - Dry-run is the DEFAULT; -WhatIf forces a dry-run even with -Apply. A dry-run issues zero writes.
      - WRITE-AHEAD REVERSAL MANIFEST: each container's id, config and the stamp about to be written are appended to
        a CSV and flushed BEFORE the write. -RevertManifest <csv> -Apply removes exactly those stamps (only where the
        container still carries the stamp this run wrote).
      - READ-BACK: every write is re-read; Graph accepting a PATCH is not evidence of the stamp.
      - SAMPLE FIRST: -MaxWritesPerRun bounds a run.
      - Idempotent: a stamped container is no longer a candidate; a re-run reports ToStamp: 0.
      - -Verify: read-only. Exit 1 while ANY derivable container is unstamped or carries a different stamp, a
        binding cannot be read, or a config's containers could not be listed at all (SKIPPED-CONFIG: an incomplete
        config, or an owning-app secret the vault does not return) — a pass must not claim containers it never saw.

    AUTH — the operator's own az CLI identity reads Dataverse and the BFF Key Vault (the owning apps' secrets, never
    printed); Graph is called app-only as each config's owning app, exactly as the BFF does. No secret in this script.

.PARAMETER EnvironmentUrl
    Dataverse environment URL, e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER KeyVaultName
    The Key Vault holding the owning apps' client secrets (the vault the BFF's SpeAdmin:KeyVaultUri names).

.PARAMETER ConfigId
    Restrict the run to the containers of one sprk_specontainertypeconfig (default: every active config).

.PARAMETER Apply
    Write mode. Without it the script always dry-runs.

.PARAMETER Verify
    Read-only verification; exit 1 if any derivable container is not stamped with its derived unit.

.PARAMETER WhatIf
    Forces a dry-run even when -Apply is passed.

.PARAMETER MaxWritesPerRun
    Cap on actual writes in one -Apply run (0 = unlimited).

.PARAMETER RevertManifest
    A manifest from an earlier -Apply run. With -Apply, removes every stamp in it that is still in place.

.PARAMETER LogPath
    Per-run log. Defaults to scripts/logs/backfill-spe-container-stamp-<timestamp>.log.

.EXAMPLE
    .\Backfill-SpeContainerBusinessUnitStamp.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -KeyVaultName <vault>
    Dry run: every container, its derived unit (with the claimants), and the UNDERIVABLE / MISMATCH / FOREIGN lists.

.EXAMPLE
    .\Backfill-SpeContainerBusinessUnitStamp.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -KeyVaultName <vault> -Apply -MaxWritesPerRun 1
    The SAMPLE: stamp one container, read back; writes a reversal manifest.

.EXAMPLE
    .\Backfill-SpeContainerBusinessUnitStamp.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -KeyVaultName <vault> -Verify
    Exit 0 only when every derivable container carries its derived unit.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$EnvironmentUrl,

    [Parameter(Mandatory)]
    [string]$KeyVaultName,

    [string]$ConfigId,

    [switch]$Apply,

    [switch]$Verify,

    [switch]$WhatIf,

    [int]$MaxWritesPerRun = 0,

    [string]$RevertManifest,

    [string]$LogPath
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$IsDryRun = (-not $Apply.IsPresent) -or $WhatIf.IsPresent -or $Verify.IsPresent

# ── The binding. MUST equal SpeContainerBusinessUnitStamp.PropertyName (SpeContainerStampScriptAgreementTests). ──
$StampProperty = 'spaarkeBusinessUnitId'

# ── The authoritative sources. MUST match the BFF's creation paths (SpeContainerStampScriptAgreementTests). ───────
$SecureRootSets = [ordered]@{
    'sprk_matters'         = 'sprk_matterid'
    'sprk_projects'        = 'sprk_projectid'
    'sprk_workassignments' = 'sprk_workassignmentid'
}
$CreateContainerOperation = 'CreateContainer'
$GraphBase = 'https://graph.microsoft.com/beta'

# ── Logging ────────────────────────────────────────────────────────────────────────────────────────────────
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$logDir = Join-Path $PSScriptRoot 'logs'
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }
if (-not $LogPath) { $LogPath = Join-Path $logDir "backfill-spe-container-stamp-$stamp.log" }
$script:LogWriter = [System.IO.StreamWriter]::new($LogPath, $false, [System.Text.Encoding]::UTF8)
function Write-StampLog { param([string]$Line) $script:LogWriter.WriteLine("$(Get-Date -Format 'HH:mm:ss') $Line"); $script:LogWriter.Flush() }

function ConvertTo-CleanGuid {
    # ADR-044: bare, lowercase, at every boundary. $null for anything that is not one GUID.
    param([AllowEmptyString()][AllowNull()][string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return $null }
    $g = [guid]::Empty
    if ([guid]::TryParse($Value.Trim(), [ref]$g) -and $g -ne [guid]::Empty) { return $g.ToString('D') }
    return $null
}

# ── Dataverse (the operator's own identity) ───────────────────────────────────────────────────────────────
function Get-DvToken {
    $t = az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv 2>$null
    if (-not $t) { throw "No Dataverse token for $EnvironmentUrl. Run 'az login' and retry." }
    return $t
}
$script:DvToken = Get-DvToken
$script:DvTokenAt = Get-Date
function Invoke-DvGet {
    param([Parameter(Mandatory)][string]$RelativePath)
    if (((Get-Date) - $script:DvTokenAt).TotalMinutes -gt 45) { $script:DvToken = Get-DvToken; $script:DvTokenAt = Get-Date }
    $headers = @{ Authorization = "Bearer $script:DvToken"; Accept = 'application/json'; 'OData-MaxVersion' = '4.0'; 'OData-Version' = '4.0' }
    $uri = "$EnvironmentUrl/api/data/v9.2/$RelativePath"
    $all = [System.Collections.Generic.List[object]]::new()
    while ($uri) {
        $resp = Invoke-RestMethod -Uri $uri -Headers $headers -Method Get
        if ($null -ne $resp.value) { $all.AddRange(@($resp.value)) } else { return , @($resp) }
        $uri = $resp.'@odata.nextLink'
    }
    return , $all.ToArray()
}
function Get-Lookup { param($Row, [string]$Attribute) $p = $Row.PSObject.Properties["_${Attribute}_value"]; if ($null -eq $p) { return $null }; return ConvertTo-CleanGuid $p.Value }

# ── Graph, app-only as a config's owning app (the BFF's own path; secret never printed) ──────────────────────
$script:GraphTokens = @{}
function Get-GraphToken {
    param([string]$TenantId, [string]$ClientId, [string]$SecretName)
    $key = "$TenantId|$ClientId|$SecretName"
    if ($script:GraphTokens.ContainsKey($key) -and ((Get-Date) - $script:GraphTokens[$key].At).TotalMinutes -lt 45) {
        return $script:GraphTokens[$key].Token
    }
    $secret = az keyvault secret show --vault-name $KeyVaultName --name $SecretName --query value -o tsv 2>$null
    if (-not $secret) { throw "Key Vault '$KeyVaultName' returned no secret '$SecretName' (owning app $ClientId)." }
    try {
        $resp = Invoke-RestMethod -Method Post -Uri "https://login.microsoftonline.com/$TenantId/oauth2/v2.0/token" -Body @{
            client_id = $ClientId; client_secret = $secret; scope = 'https://graph.microsoft.com/.default'; grant_type = 'client_credentials'
        }
    } finally { $secret = $null }
    $script:GraphTokens[$key] = @{ Token = $resp.access_token; At = Get-Date }
    return $resp.access_token
}
function Invoke-Graph {
    param([string]$Token, [string]$Method, [string]$Uri, $Body)
    $headers = @{ Authorization = "Bearer $Token"; Accept = 'application/json' }
    if ($null -ne $Body) {
        return Invoke-RestMethod -Method $Method -Uri $Uri -Headers $headers -ContentType 'application/json' -Body ($Body | ConvertTo-Json -Depth 5 -Compress)
    }
    return Invoke-RestMethod -Method $Method -Uri $Uri -Headers $headers
}

# The binding of ONE container. Graph drops customProperties on the containers COLLECTION (measured 2026-10-04), so
# this is always a single-container GET. Returns @{ Found; TypeId; Stamp; Malformed }.
function Read-ContainerBinding {
    param([string]$Token, [string]$ContainerId)
    try {
        $c = Invoke-Graph $Token 'Get' "$GraphBase/storage/fileStorage/containers/$([uri]::EscapeDataString($ContainerId))?`$select=id,containerTypeId,customProperties"
    } catch {
        if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 404) { return @{ Found = $false } }
        throw
    }
    $values = @()
    if ($c.customProperties) {
        foreach ($p in $c.customProperties.PSObject.Properties) {
            if ($p.Name.Trim() -ieq $StampProperty) { $values += , [string]$p.Value.value }
        }
    }
    $clean = @($values | ForEach-Object { ConvertTo-CleanGuid $_ })
    if ($values.Count -eq 0) { return @{ Found = $true; TypeId = (ConvertTo-CleanGuid $c.containerTypeId); Stamp = $null; Malformed = $false } }
    if (($clean | Where-Object { -not $_ }).Count -gt 0 -or ($clean | Select-Object -Unique).Count -ne 1) {
        return @{ Found = $true; TypeId = (ConvertTo-CleanGuid $c.containerTypeId); Stamp = $null; Malformed = $true }
    }
    return @{ Found = $true; TypeId = (ConvertTo-CleanGuid $c.containerTypeId); Stamp = $clean[0]; Malformed = $false }
}

# PATCH /containers/{id}/customProperties with the property map as the BODY ROOT (merge semantics — no other property
# is touched; a null value removes the property). The shape SpeAdminGraphService.WriteBusinessUnitStampAsync sends.
function Write-ContainerStamp {
    param([string]$Token, [string]$ContainerId, [AllowNull()][string]$BusinessUnitId)
    $value = if ($BusinessUnitId) { @{ value = $BusinessUnitId; isSearchable = $false } } else { $null }
    Invoke-Graph $Token 'Patch' "$GraphBase/storage/fileStorage/containers/$([uri]::EscapeDataString($ContainerId))/customProperties" @{ $StampProperty = $value } | Out-Null
}

# ══ Load the authoritative records ═══════════════════════════════════════════════════════════════════════════
$units = Invoke-DvGet "businessunits?`$select=businessunitid,_parentbusinessunitid_value,sprk_containerid,name"
$parentOf = @{}
$unitName = @{}
foreach ($u in $units) {
    $id = ConvertTo-CleanGuid $u.businessunitid
    if (-not $id) { continue }
    $parentOf[$id] = Get-Lookup $u 'parentbusinessunitid'
    $unitName[$id] = $u.name
}

function Get-Ancestry {
    # The unit and every ancestor, nearest first; cycle-safe.
    param([string]$Unit)
    $chain = [System.Collections.Generic.List[string]]::new(); $seen = @{}
    $current = $Unit
    while ($current -and -not $seen.ContainsKey($current)) { $seen[$current] = $true; $chain.Add($current); $current = $parentOf[$current] }
    return , $chain.ToArray()
}

function Get-NearestCommonAncestor {
    param([string[]]$Claimants)
    if ($Claimants.Count -eq 0) { return $null }
    foreach ($c in $Claimants) { if (-not $parentOf.ContainsKey($c)) { return $null } }
    $first = Get-Ancestry $Claimants[0]
    foreach ($candidate in $first) {
        $all = $true
        foreach ($c in $Claimants) { if ((Get-Ancestry $c) -notcontains $candidate) { $all = $false; break } }
        if ($all) { return $candidate }
    }
    return $null
}

$configFilter = 'statecode eq 0'
if ($ConfigId) { $configFilter += " and sprk_specontainertypeconfigid eq $(ConvertTo-CleanGuid $ConfigId)" }
$configs = Invoke-DvGet "sprk_specontainertypeconfigs?`$select=sprk_specontainertypeconfigid,sprk_name,sprk_containertypeid,sprk_owningappid,sprk_keyvaultsecretname,_sprk_environment_value,_sprk_businessunit_value&`$filter=$configFilter"
$allConfigs = if ($ConfigId) { Invoke-DvGet "sprk_specontainertypeconfigs?`$select=sprk_specontainertypeconfigid,_sprk_businessunit_value" } else { $configs }
$configUnit = @{}
foreach ($c in $allConfigs) { $configUnit[(ConvertTo-CleanGuid $c.sprk_specontainertypeconfigid)] = Get-Lookup $c 'sprk_businessunit' }

# containerId -> list of @{ Unit; Source }
$claims = @{}
function Add-Claim {
    param([string]$ContainerId, [string]$Unit, [string]$Source)
    if ([string]::IsNullOrWhiteSpace($ContainerId) -or -not $Unit) { return }
    $key = $ContainerId.Trim()
    if (-not $claims.ContainsKey($key)) { $claims[$key] = [System.Collections.Generic.List[object]]::new() }
    $claims[$key].Add(@{ Unit = $Unit; Source = $Source })
}

foreach ($u in $units) { Add-Claim $u.sprk_containerid (ConvertTo-CleanGuid $u.businessunitid) "businessunit($($u.name))" }

foreach ($set in $SecureRootSets.Keys) {
    $idColumn = $SecureRootSets[$set]
    $rows = Invoke-DvGet "$set`?`$select=$idColumn,sprk_containerid,_owningbusinessunit_value&`$filter=sprk_issecure eq true and sprk_containerid ne null"
    foreach ($r in $rows) { Add-Claim $r.sprk_containerid (Get-Lookup $r 'owningbusinessunit') "$set($(ConvertTo-CleanGuid $r.$idColumn)) secure root" }
}

$audits = Invoke-DvGet "sprk_speauditlogs?`$select=sprk_targetresourceid,sprk_responsestatus,_sprk_containertypeconfig_value,_sprk_businessunit_value&`$filter=sprk_operation eq '$CreateContainerOperation'"
foreach ($a in $audits) {
    if ([int]$a.sprk_responsestatus -ne 201) { continue }
    $unit = Get-Lookup $a 'sprk_businessunit'
    if (-not $unit) { $cfg = Get-Lookup $a 'sprk_containertypeconfig'; if ($cfg) { $unit = $configUnit[$cfg] } }
    Add-Claim $a.sprk_targetresourceid $unit "speauditlog CreateContainer"
}

function Get-DerivedOwner {
    param([string]$ContainerId)
    if (-not $claims.ContainsKey($ContainerId)) { return @{ Unit = $null; Why = 'no authoritative record claims it' } }
    $claimants = @($claims[$ContainerId] | ForEach-Object { $_.Unit } | Select-Object -Unique)
    $sources = ($claims[$ContainerId] | ForEach-Object { $_.Source }) -join '; '
    $owner = Get-NearestCommonAncestor $claimants
    if (-not $owner) { return @{ Unit = $null; Why = "claimants not all in this environment's hierarchy ($sources)" } }
    return @{ Unit = $owner; Why = $sources }
}

# ══ REVERT ═══════════════════════════════════════════════════════════════════════════════════════════════════
$configById = @{}
foreach ($c in $configs) { $configById[(ConvertTo-CleanGuid $c.sprk_specontainertypeconfigid)] = $c }
$tenantByEnv = @{}
function Get-ConfigToken {
    param($Config)
    $env = Get-Lookup $Config 'sprk_environment'
    if (-not $env) { throw "Config $($Config.sprk_specontainertypeconfigid) links no environment (no tenant)." }
    if (-not $tenantByEnv.ContainsKey($env)) { $tenantByEnv[$env] = (Invoke-DvGet "sprk_speenvironments($env)?`$select=sprk_tenantid")[0].sprk_tenantid }
    return Get-GraphToken $tenantByEnv[$env] $Config.sprk_owningappid $Config.sprk_keyvaultsecretname
}

if ($RevertManifest) {
    $rows = Import-Csv $RevertManifest
    Write-Host "Revert: $($rows.Count) stamp(s) from $RevertManifest ($(if ($IsDryRun) { 'DRY RUN' } else { 'APPLY' }))"
    $removed = 0; $skipped = 0; $failed = 0
    foreach ($r in $rows) {
        $config = $configById[(ConvertTo-CleanGuid $r.ConfigId)]
        if (-not $config) { $failed++; Write-StampLog "REVERT-FAIL $($r.ContainerId): config $($r.ConfigId) not loaded"; continue }
        $token = Get-ConfigToken $config
        $current = Read-ContainerBinding $token $r.ContainerId
        # Remove ONLY a stamp still equal to the one this run wrote — a later deliberate change is not clobbered.
        if (-not $current.Found -or $current.Stamp -ne (ConvertTo-CleanGuid $r.NewStamp)) {
            $skipped++; Write-StampLog "REVERT-SKIP $($r.ContainerId): stamp is no longer $($r.NewStamp)"; continue
        }
        if ($IsDryRun) { Write-StampLog "REVERT-PLAN $($r.ContainerId): remove $StampProperty"; continue }
        try {
            Write-ContainerStamp $token $r.ContainerId $null
            $after = Read-ContainerBinding $token $r.ContainerId
            if ($after.Stamp) { throw "read-back still carries $($after.Stamp)" }
            $removed++; Write-StampLog "REVERTED $($r.ContainerId)"
        } catch { $failed++; Write-StampLog "REVERT-FAIL $($r.ContainerId): $($_.Exception.Message)" }
    }
    $script:LogWriter.Dispose()
    Write-Host "Removed: $removed  Skipped (changed since): $skipped  Failed: $failed  Log: $LogPath"
    exit ([int]($failed -gt 0))
}

# ══ BACKFILL / VERIFY ════════════════════════════════════════════════════════════════════════════════════════
$manifestPath = Join-Path $logDir "spe-container-stamp-manifest-$stamp.csv"
$manifest = $null
if (-not $IsDryRun) {
    $manifest = [System.IO.StreamWriter]::new($manifestPath, $false, [System.Text.Encoding]::UTF8)
    $manifest.WriteLine('ContainerId,ConfigId,PreviousStamp,NewStamp,WrittenAtUtc')
    $manifest.Flush()
}

$stats = [ordered]@{ ConfigsSkipped = 0; Containers = 0; AlreadyStamped = 0; ToStamp = 0; Stamped = 0; Failed = 0; Underivable = 0; Mismatch = 0; Foreign = 0; Malformed = 0; Unreadable = 0 }
$listed = [System.Collections.Generic.List[string]]::new()
$seen = @{}
$writes = 0

foreach ($config in $configs) {
    $cid = ConvertTo-CleanGuid $config.sprk_specontainertypeconfigid
    $type = ConvertTo-CleanGuid $config.sprk_containertypeid
    if (-not $type -or -not $config.sprk_owningappid -or -not $config.sprk_keyvaultsecretname) {
        $stats.ConfigsSkipped++
        $listed.Add("SKIPPED-CONFIG $cid ($($config.sprk_name)): incomplete (container type, owning app or secret name missing) — its containers were NOT examined.")
        continue
    }
    try { $token = Get-ConfigToken $config }
    catch {
        $stats.ConfigsSkipped++
        $listed.Add("SKIPPED-CONFIG $cid ($($config.sprk_name)): $($_.Exception.Message) — its containers were NOT examined.")
        continue
    }

    $uri = "$GraphBase/storage/fileStorage/containers?`$filter=containerTypeId eq $type&`$select=id,displayName"
    while ($uri) {
        $page = Invoke-Graph $token 'Get' $uri
        foreach ($c in @($page.value)) {
            $containerId = [string]$c.id
            if ($seen.ContainsKey($containerId)) { continue }
            $seen[$containerId] = $true
            $stats.Containers++

            try { $binding = Read-ContainerBinding $token $containerId }
            catch { $stats.Unreadable++; $listed.Add("UNREADABLE $containerId ($($c.displayName)): $($_.Exception.Message)"); continue }
            if (-not $binding.Found) { continue }

            $derived = Get-DerivedOwner $containerId
            $label = "$containerId ($($c.displayName)) config $cid"

            if ($binding.Malformed) { $stats.Malformed++; $listed.Add("MALFORMED $label — the stamp is not one business-unit GUID; fix by hand."); continue }

            if ($binding.Stamp) {
                if (-not $parentOf.ContainsKey($binding.Stamp)) {
                    $stats.Foreign++; $listed.Add("FOREIGN $label — stamped $($binding.Stamp), a unit this environment does not know (another environment's container?). Untouched.")
                } elseif ($derived.Unit -and $derived.Unit -ne $binding.Stamp) {
                    $stats.Mismatch++; $listed.Add("MISMATCH $label — stamped $($binding.Stamp), records derive $($derived.Unit) [$($derived.Why)]. Untouched.")
                } else {
                    $stats.AlreadyStamped++
                }
                continue
            }

            if (-not $derived.Unit) {
                $stats.Underivable++; $listed.Add("UNDERIVABLE $label — $($derived.Why). Left unbound (root-unit admins only).")
                continue
            }

            $stats.ToStamp++
            Write-StampLog "PLAN $label -> $($derived.Unit) ($($unitName[$derived.Unit])) [$($derived.Why)]"
            if ($IsDryRun -or ($MaxWritesPerRun -gt 0 -and $writes -ge $MaxWritesPerRun)) { continue }

            # WRITE-AHEAD: the reversal row is on disk before the write it reverses.
            $manifest.WriteLine("$containerId,$cid,,$($derived.Unit),$((Get-Date).ToUniversalTime().ToString('o'))")
            $manifest.Flush()
            $writes++
            try {
                Write-ContainerStamp $token $containerId $derived.Unit
                $after = Read-ContainerBinding $token $containerId
                if ($after.Stamp -ne $derived.Unit) { throw "read-back: $($after.Stamp)" }
                $stats.Stamped++; Write-StampLog "STAMPED $label -> $($derived.Unit)"
            } catch { $stats.Failed++; Write-StampLog "FAIL ${label}: $($_.Exception.Message)" }
        }
        $uri = $page.'@odata.nextLink'
    }
}

foreach ($line in $listed) { Write-StampLog $line }
if ($manifest) { $manifest.Dispose() }
$script:LogWriter.Dispose()

$mode = if ($Verify) { 'VERIFY (read-only)' } elseif ($IsDryRun) { 'DRY RUN (no writes)' } else { 'APPLY' }
Write-Host ''
Write-Host "SPE container business-unit stamp backfill — $mode — $EnvironmentUrl"
[pscustomobject]$stats | Format-List | Out-String | Write-Host
if ($listed.Count -gt 0) {
    Write-Host 'Listed for an operator (never written by this script):'
    $listed | ForEach-Object { Write-Host "  $_" }
}
if (-not $IsDryRun) { Write-Host "Reversal manifest: $manifestPath  (undo: -RevertManifest `"$manifestPath`" -Apply)" }
Write-Host "Log: $LogPath"

if ($Verify) {
    # Every derivable container must carry its derived unit; a mismatch is a derivable container stamped otherwise. A
    # config whose containers could not be listed fails too: nothing was proven about them.
    exit ([int](($stats.ToStamp + $stats.Mismatch + $stats.Unreadable + $stats.ConfigsSkipped) -gt 0))
}
exit ([int]($stats.Failed -gt 0))
