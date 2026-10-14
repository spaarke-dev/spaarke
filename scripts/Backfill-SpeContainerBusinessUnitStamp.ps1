#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Stamps EXISTING SharePoint Embedded containers with the business unit that owns them — the backfill for owner
    round 20 item 1 (unified-access-control-r2 task 165). With -CustomerId it ALSO marks the same containers with their
    customer (issue #1889). Dry-run by default; every write is reversible.

.DESCRIPTION
    THE BINDING. Every SPE container carries its owning business unit as the fileStorageContainer custom property
    $SpeContainerStampProperty (scripts/common/SpeContainerBinding.ps1 — the ONE PowerShell constant, equal to the C#
    constant Spaarke.Contracts.Spe.SpeContainerBusinessUnitBinding.PropertyName). The SPE admin plane authorizes every
    container, item, permission and bulk route PER CONTAINER against it: a container bound to a unit the admin reaches
    (their own unit or a descendant); an UNBOUND container by NO admin route, root-unit admins included (owner round 35
    item 2); an unreadable binding fails closed. New containers are stamped by every creation path (the BFF, the L2 H8
    handler, New-BusinessUnitContainer.ps1, Provision-Customer.ps1, Create-NewContainerType.ps1). This script stamps
    the ones that already exist — from records, or from an operator's explicit -Bind.

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

    -BIND (owner round 35 item 2). A container no authoritative record claims is UNDERIVABLE. An operator who knows its
    owner binds it explicitly: -Bind '<containerId>=<businessUnitId>' (repeatable). The unit must be one this
    environment's hierarchy holds; a -Bind for a DERIVABLE container must name the derived unit (records win — a
    disagreeing -Bind is listed as BIND-CONFLICT and not written); a -Bind never overwrites a stamp; a -Bind naming a
    container no config lists is BIND-UNUSED. Any invalid -Bind entry stops the run before any container is read or
    anything is written (only the business-unit hierarchy, needed to validate it, has been read).

    THE CUSTOMER MARKER (-CustomerId; issue #1889). The BFF's SpeContainerOwnershipGuard refuses (404
    spe_container_not_owned) an app-only SPE call on any container that is neither configured in the BFF's app settings
    nor marked with the custom property $SpeContainerCustomerMarkerProperty equal to the BFF's Customer__Id. The BFF writes
    the marker only when IT creates a container, so per-record (secure) containers created BEFORE the marker shipped
    (task 227d, 2026-10-08) carry none and are refused forever. -CustomerId makes this script mark them, in the same pass
    and with the same safety model as the stamp:
      - Candidates are ONLY containers this script already treats as DERIVABLE from records (plus explicit -Bind
        entries). An UNDERIVABLE container is never marked - in a shared container type other environments' containers
        appear in the listing and must never be marked by this environment.
      - The marker is written only when ABSENT, and never overwritten. Equal to -CustomerId: already marked (no-op). A
        different value (a case-differing one included): MARKER-FOREIGN, listed. Several or blank values:
        MARKER-MALFORMED, listed. Neither is ever written.
      - A container whose stamp is MISMATCH, FOREIGN or MALFORMED (or contradicted by -Bind) is disputed: listed, not marked.
      - Marking does not depend on the stamp: a container that is already stamped but unmarked is still marked.
      - Each write is preceded by a manifest row (Kind = marker), read back (the marker must equal -CustomerId exactly),
        and counts toward -MaxWritesPerRun. -RevertManifest removes a marker only where the container still carries the
        value this run wrote.
      - -Verify exits 1 while any derivable container is unmarked, MARKER-FOREIGN or MARKER-MALFORMED.
    Without -CustomerId the script behaves exactly as before. This script never deletes a container.

    WHAT IT WILL NOT DO.
      - Stamp a container no authoritative record claims and no -Bind names. Those are LISTED as UNDERIVABLE and left
        unbound — and an unbound container is reached by NO admin route, so each must be bound (-Bind) or removed. In
        a shared Model 1 consuming tenant another environment's containers appear here too: they are underivable or
        FOREIGN and must never be stamped by this environment (do not -Bind a container you cannot attribute).
      - Overwrite an existing stamp (or customer marker). A stamp that differs from the derivation (MISMATCH), names a unit this
        environment does not know (FOREIGN) or is not one GUID (MALFORMED) is LISTED for an operator, never changed.

    SAFETY MODEL
      - Dry-run is the DEFAULT; -WhatIf forces a dry-run even with -Apply. A dry-run issues zero writes.
      - WRITE-AHEAD REVERSAL MANIFEST: each container's id, config and the stamp (or, with -CustomerId, the marker)
        about to be written are appended to a CSV and flushed BEFORE the write. -RevertManifest <csv> -Apply removes
        exactly those stamps and markers (only where the container still carries the value this run wrote).
      - READ-BACK: every write is re-read; Graph accepting a PATCH is not evidence of the stamp.
      - SAMPLE FIRST: -MaxWritesPerRun bounds a run.
      - Idempotent: a stamped container is no longer a candidate; a re-run reports ToStamp: 0.
      - -Verify: read-only (with -CustomerId, also unmarked / MARKER-FOREIGN / MARKER-MALFORMED derivable containers). LISTS every container still unbound (UNBOUND: derivable but unstamped, underivable, or
        malformed) and exits 1 while ANY is, while a container carries a stamp other than its records derive, a binding
        cannot be read, or a config's containers could not be listed at all (SKIPPED-CONFIG: an incomplete config, or an
        owning-app secret the vault does not return) — a pass must not claim containers it never saw.

    MANUAL GATE (round 35 item 2). -Apply then -Verify exit 0 in EVERY environment is required BEFORE task 165's BFF is
    deployed there, and again before a further environment is onboarded onto a container type another environment
    already uses (docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md).

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
    Read-only verification: lists every unbound container; exit 1 if any container is unbound or stamped other than its
    records derive, a binding is unreadable, or a config's containers could not be listed.

.PARAMETER CustomerId
    Optional. The BFF's Customer__Id app setting, EXACTLY (the BFF compares it ordinally and case-sensitively; this
    script trims it and compares the same way). When given, the script also marks the derivable containers that carry no
    customer marker (issue #1889: the per-record containers created before the marker shipped). Blank is refused
    before anything is read. Omit it and no marker is read or written.

.PARAMETER Bind
    Explicit owners for containers no record claims: one or more '<containerId>=<businessUnitId>' (split at the LAST
    '='). Validated against the business-unit hierarchy before any container is read; written only with -Apply (a dry run
    and -Verify show the plan).

.PARAMETER WhatIf
    Forces a dry-run even when -Apply is passed.

.PARAMETER MaxWritesPerRun
    Cap on actual writes in one -Apply run (0 = unlimited).

.PARAMETER RevertManifest
    A manifest from an earlier -Apply run. With -Apply, removes every stamp and customer marker in it that is still in
    place (a manifest from before -CustomerId existed holds stamps only).

.PARAMETER LogPath
    Per-run log. Defaults to scripts/logs/backfill-spe-container-stamp-<timestamp>.log.

.EXAMPLE
    .\Backfill-SpeContainerBusinessUnitStamp.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -KeyVaultName <vault>
    Dry run: every container, its derived unit (with the claimants), and the UNDERIVABLE / MISMATCH / FOREIGN lists.

.EXAMPLE
    .\Backfill-SpeContainerBusinessUnitStamp.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -KeyVaultName <vault> -Apply -MaxWritesPerRun 1
    The SAMPLE: stamp one container, read back; writes a reversal manifest.

.EXAMPLE
    .\Backfill-SpeContainerBusinessUnitStamp.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -KeyVaultName <vault> -Bind 'b!abc=0b0b0b0b-1111-2222-3333-444444444444' -Apply
    Binds one underivable container to a named business unit (read back, reversal manifest written first).

.EXAMPLE
    .\Backfill-SpeContainerBusinessUnitStamp.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -KeyVaultName <vault> -Verify
    Exit 0 only when NO container is unbound and every stamp matches its records.

.EXAMPLE
    .\Backfill-SpeContainerBusinessUnitStamp.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -KeyVaultName <vault> -CustomerId <the BFF's Customer__Id>
    Issue #1889 - DRY RUN of the marker pass: lists every derivable container that carries no customer marker (ToMark),
    and any MARKER-FOREIGN / MARKER-MALFORMED one. Pre-marker per-record containers (created before task 227d) carry none
    and the BFF's ownership guard refuses them.

.EXAMPLE
    .\Backfill-SpeContainerBusinessUnitStamp.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -KeyVaultName <vault> -CustomerId <the BFF's Customer__Id> -Apply -MaxWritesPerRun 1
    The SAMPLE for the marker pass: writes one marker, read back; a reversal manifest is written first. Re-run without
    -MaxWritesPerRun to mark the rest.

.EXAMPLE
    .\Backfill-SpeContainerBusinessUnitStamp.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -KeyVaultName <vault> -CustomerId <the BFF's Customer__Id> -Verify
    Exit 0 only when every derivable container is stamped AND marked. Run this on every environment that holds pre-marker
    containers BEFORE the guard-carrying BFF is deployed there.
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

    [string[]]$Bind = @(),

    [string]$CustomerId,

    [string]$LogPath
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$IsDryRun = (-not $Apply.IsPresent) -or $WhatIf.IsPresent -or $Verify.IsPresent

# ── -CustomerId (issue #1889): validated BEFORE any read or write. Given => the customer-marker pass runs. ───────────
$MarkerPass = $PSBoundParameters.ContainsKey('CustomerId')
if ($MarkerPass) {
    if ([string]::IsNullOrWhiteSpace($CustomerId)) {
        Write-Host '-CustomerId is blank - pass the BFF''s Customer__Id app setting (or omit -CustomerId). Nothing was read or written.' -ForegroundColor Red
        exit 2
    }
    $CustomerId = $CustomerId.Trim()   # compared ordinally and case-sensitively (-ceq) from here on, as the BFF does
}

# ── The binding: THE PowerShell constant (scripts/common/SpeContainerBinding.ps1), pinned equal to the C# one by
#    SpeAdminContainerBindingGuardTests. Never spelled again here. ──────────────────────────────────────────────────
. (Join-Path $PSScriptRoot 'common/SpeContainerBinding.ps1')
$StampProperty = $SpeContainerStampProperty

# ── The Key Vault secret-name allow-list: THE PowerShell copy of the BFF's rule (round 35 item 3; round 41 items 4-5).
#    A config naming a secret outside it is SKIPPED and its secret is never read — as the BFF refuses it. ─────────────
. (Join-Path $PSScriptRoot 'common/SpeConfigSecretNamePolicy.ps1')

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
    if (-not (Test-SpeConfigSecretNameAllowed $SecretName)) {
        throw "the config's secret name '$SecretName' is outside the BFF's allow-list ('$SpeConfigSecretNamePrefix…') — the vault was NOT read; repair it with Repair-SpeConfigSecretName.ps1"
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

# The binding of ONE container (single-container GET — the collection drops customProperties) and the stamp write
# (PATCH …/customProperties, property map as the body root): the shared functions in common/SpeContainerBinding.ps1,
# the same ones every creation script uses.
function Read-ContainerBinding {
    param([string]$Token, [string]$ContainerId)
    return Get-SpeContainerBinding -Token $Token -ContainerId $ContainerId -GraphBase $GraphBase
}

function Write-ContainerStamp {
    param([string]$Token, [string]$ContainerId, [AllowNull()][string]$BusinessUnitId)
    Set-SpeContainerStamp -Token $Token -ContainerId $ContainerId -BusinessUnitId $BusinessUnitId -GraphBase $GraphBase
}

function Remove-ContainerMarker {
    param([string]$Token, [string]$ContainerId)
    Remove-SpeContainerCustomerMarker -Token $Token -ContainerId $ContainerId -GraphBase $GraphBase
}

function ConvertTo-CsvField { param([string]$Value) return '"' + ($Value -replace '"', '""') + '"' }

# The customer-marker pass for ONE container (issue #1889). The decision itself is the pure
# Get-SpeContainerMarkerDecision (common/SpeContainerBinding.ps1, unit-tested); this applies it. The marker is written
# only when absent; every write is manifest-first, read back and counted toward -MaxWritesPerRun.
function Invoke-MarkerPass {
    param($Token, [string]$ContainerId, [string]$Label, [string]$ConfigId, $Binding, [bool]$Derivable, [string]$StampState)
    $decision = Get-SpeContainerMarkerDecision -CustomerId $CustomerId -Derivable $Derivable -StampState $StampState `
        -Marker $Binding.Marker -MarkerMalformed ([bool]$Binding.MarkerMalformed)
    switch ($decision) {
        'SkipUnderivable' { return }
        'SkipDisputedStamp' {
            # Only listed: ownership is disputed (the stamp line is already in the list). Verify still fails while unmarked.
            if ($Binding.Marker -cne $CustomerId) { $unmarked.Add("UNMARKED $Label — the stamp is disputed ($StampState): not marked, only listed.") }
            return
        }
        'MarkerMalformed' {
            $stats.MarkerMalformed++
            $listed.Add("MARKER-MALFORMED $Label — the customer marker is present but ambiguous (several entries or a blank value); fix by hand. Not written.")
            $unmarked.Add("UNMARKED $Label — malformed marker")
            return
        }
        'AlreadyMarked' { $stats.AlreadyMarked++; return }
        'MarkerForeign' {
            $stats.MarkerForeign++
            $listed.Add("MARKER-FOREIGN $Label — marked '$($Binding.Marker)', not '$CustomerId' (another customer's container, or a case-differing id?). Never overwritten.")
            $unmarked.Add("UNMARKED $Label — marked for a different customer")
            return
        }
        'Mark' {
            $stats.ToMark++
            Write-StampLog "PLAN-MARK $Label -> customer '$CustomerId'"
            if ($IsDryRun -or ($MaxWritesPerRun -gt 0 -and $script:writes -ge $MaxWritesPerRun)) {
                $unmarked.Add("UNMARKED $Label — to mark for customer '$CustomerId'")
                return
            }
            # WRITE-AHEAD: the reversal row (Kind = marker) is on disk before the write it reverses.
            $manifest.WriteLine("$ContainerId,$ConfigId,,,$((Get-Date).ToUniversalTime().ToString('o')),marker,$(ConvertTo-CsvField $CustomerId)")
            $manifest.Flush()
            $script:writes++
            try {
                Set-SpeContainerCustomerMarker -Token $Token -ContainerId $ContainerId -CustomerId $CustomerId -GraphBase $GraphBase
                $after = Read-ContainerBinding $Token $ContainerId
                if ($after.Marker -cne $CustomerId) { throw "read-back: '$($after.Marker)'" }
                $stats.Marked++; Write-StampLog "MARKED $Label -> customer '$CustomerId'"
            } catch {
                $stats.Failed++; Write-StampLog "FAIL-MARK ${Label}: $($_.Exception.Message)"
                $unmarked.Add("UNMARKED $Label — the marker write failed: $($_.Exception.Message)")
            }
        }
    }
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

# ── -Bind: validated BEFORE anything else is read or written (round 35 item 2) ─────────────────────────────────────
$explicitBind = @{}
$bindErrors = [System.Collections.Generic.List[string]]::new()
foreach ($entry in $Bind) {
    if ([string]::IsNullOrWhiteSpace($entry)) { continue }
    $cut = $entry.LastIndexOf('=')
    if ($cut -le 0 -or $cut -eq $entry.Length - 1) { $bindErrors.Add("'$entry' is not '<containerId>=<businessUnitId>'."); continue }
    $bindContainer = $entry.Substring(0, $cut).Trim()
    $bindUnit = ConvertTo-CleanGuid $entry.Substring($cut + 1)
    if (-not $bindUnit) { $bindErrors.Add("'$entry': the business unit is not one GUID."); continue }
    if (-not $parentOf.ContainsKey($bindUnit)) { $bindErrors.Add("'$entry': business unit $bindUnit is not in this environment's hierarchy."); continue }
    if ($explicitBind.ContainsKey($bindContainer) -and $explicitBind[$bindContainer] -ne $bindUnit) {
        $bindErrors.Add("'$bindContainer' is bound twice, to different units."); continue
    }
    $explicitBind[$bindContainer] = $bindUnit
}
if ($bindErrors.Count -gt 0) {
    $script:LogWriter.Dispose()
    Write-Host 'The -Bind input is invalid — no container was read and nothing was written:' -ForegroundColor Red
    $bindErrors | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    exit 2
}
$bindUsed = @{}

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
    $markerRows = @($rows | Where-Object { $_.PSObject.Properties['Kind'] -and $_.Kind -ceq 'marker' }).Count
    Write-Host "Revert: $($rows.Count - $markerRows) stamp(s)$(if ($markerRows -gt 0) { " and $markerRows customer marker(s)" }) from $RevertManifest ($(if ($IsDryRun) { 'DRY RUN' } else { 'APPLY' }))"
    $removed = 0; $skipped = 0; $failed = 0
    foreach ($r in $rows) {
        $config = $configById[(ConvertTo-CleanGuid $r.ConfigId)]
        if (-not $config) { $failed++; Write-StampLog "REVERT-FAIL $($r.ContainerId): config $($r.ConfigId) not loaded"; continue }
        $token = Get-ConfigToken $config
        $current = Read-ContainerBinding $token $r.ContainerId
        # A customer-marker row (Kind = marker; a manifest from before -CustomerId has no Kind column and is all stamps).
        if ($r.PSObject.Properties['Kind'] -and $r.Kind -ceq 'marker') {
            # Remove ONLY a marker still equal to the one this run wrote - a later deliberate change is not clobbered.
            if (-not $current.Found -or $current.MarkerMalformed -or [string]::IsNullOrWhiteSpace($r.NewMarker) -or $current.Marker -cne $r.NewMarker.Trim()) {
                $skipped++; Write-StampLog "REVERT-SKIP $($r.ContainerId): customer marker is no longer '$($r.NewMarker)'"; continue
            }
            if ($IsDryRun) { Write-StampLog "REVERT-PLAN $($r.ContainerId): remove $SpeContainerCustomerMarkerProperty"; continue }
            try {
                Remove-ContainerMarker $token $r.ContainerId
                $after = Read-ContainerBinding $token $r.ContainerId
                if ($after.Marker -or $after.MarkerMalformed) { throw "read-back still carries a marker ($($after.Marker))" }
                $removed++; Write-StampLog "REVERTED-MARKER $($r.ContainerId)"
            } catch { $failed++; Write-StampLog "REVERT-FAIL $($r.ContainerId): $($_.Exception.Message)" }
            continue
        }
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
    # With -CustomerId the manifest also carries marker rows: Kind (stamp | marker) and NewMarker. Without it, as before.
    $manifest.WriteLine($(if ($MarkerPass) { 'ContainerId,ConfigId,PreviousStamp,NewStamp,WrittenAtUtc,Kind,NewMarker' } else { 'ContainerId,ConfigId,PreviousStamp,NewStamp,WrittenAtUtc' }))
    $manifest.Flush()
}

$stats = [ordered]@{ ConfigsSkipped = 0; Containers = 0; AlreadyStamped = 0; ToStamp = 0; Stamped = 0; Failed = 0; Underivable = 0; Mismatch = 0; Foreign = 0; Malformed = 0; Unreadable = 0; BindConflict = 0; BindUnused = 0 }
if ($MarkerPass) { $stats['ToMark'] = 0; $stats['Marked'] = 0; $stats['AlreadyMarked'] = 0; $stats['MarkerForeign'] = 0; $stats['MarkerMalformed'] = 0 }
$listed = [System.Collections.Generic.List[string]]::new()
# Every container still unbound after this pass (round 35 item 2: -Verify lists them all; no admin route reaches one).
$unbound = [System.Collections.Generic.List[string]]::new()
# Every derivable container the BFF would still refuse (-CustomerId only): unmarked, MARKER-FOREIGN, MARKER-MALFORMED,
# or disputed and unmarked. -Verify exits 1 while any is listed.
$unmarked = [System.Collections.Generic.List[string]]::new()
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
            # Listed a moment ago, 404 now: deleted in between. Nothing to stamp or mark; say so rather than skip silently.
            if (-not $binding.Found) { $listed.Add("GONE $containerId ($($c.displayName)): listed, then not found on read (deleted meanwhile?). Not examined."); continue }

            $derived = Get-DerivedOwner $containerId
            $label = "$containerId ($($c.displayName)) config $cid"
            $operatorUnit = $null
            if ($explicitBind.ContainsKey($containerId)) { $operatorUnit = $explicitBind[$containerId]; $bindUsed[$containerId] = $true }

            if ($binding.Malformed) {
                $stats.Malformed++
                $listed.Add("MALFORMED $label — the stamp is not one business-unit GUID; fix by hand.")
                $unbound.Add("UNBOUND $label — malformed stamp")
                if ($MarkerPass) { Invoke-MarkerPass $token $containerId $label $cid $binding ([bool]($derived.Unit -or $operatorUnit)) 'Malformed' }
                continue
            }

            if ($binding.Stamp) {
                $stampState = 'Stamped'
                if (-not $parentOf.ContainsKey($binding.Stamp)) {
                    $stampState = 'Foreign'
                    $stats.Foreign++; $listed.Add("FOREIGN $label — stamped $($binding.Stamp), a unit this environment does not know (another environment's container?). Untouched.")
                } elseif ($derived.Unit -and $derived.Unit -ne $binding.Stamp) {
                    $stampState = 'Mismatch'
                    $stats.Mismatch++; $listed.Add("MISMATCH $label — stamped $($binding.Stamp), records derive $($derived.Unit) [$($derived.Why)]. Untouched.")
                } else {
                    $stats.AlreadyStamped++
                }
                if ($operatorUnit -and $operatorUnit -ne $binding.Stamp) {
                    $stampState = 'Mismatch'
                    $stats.BindConflict++; $listed.Add("BIND-CONFLICT $label — -Bind names $operatorUnit but the container is already stamped $($binding.Stamp). A stamp is never overwritten.")
                }
                if ($MarkerPass) { Invoke-MarkerPass $token $containerId $label $cid $binding ([bool]($derived.Unit -or $operatorUnit)) $stampState }
                continue
            }

            # Records win: an explicit -Bind may only confirm a derivable owner, or name the owner of an underivable one.
            $target = $derived.Unit
            $why = $derived.Why
            if ($operatorUnit) {
                if ($derived.Unit -and $derived.Unit -ne $operatorUnit) {
                    $stats.BindConflict++
                    $listed.Add("BIND-CONFLICT $label — -Bind names $operatorUnit but the records derive $($derived.Unit) [$($derived.Why)]. Not written.")
                    $unbound.Add("UNBOUND $label — -Bind conflicts with the records")
                    continue
                }
                if (-not $derived.Unit) { $target = $operatorUnit; $why = 'operator -Bind' }
            }

            if (-not $target) {
                $stats.Underivable++
                $listed.Add("UNDERIVABLE $label — $($derived.Why). Unbound: NO admin route reaches it — bind it with -Bind '$containerId=<businessUnitId>' -Apply, or remove it.")
                $unbound.Add("UNBOUND $label — underivable (needs -Bind)")
                continue
            }

            $stats.ToStamp++
            Write-StampLog "PLAN $label -> $target ($($unitName[$target])) [$why]"
            if ($IsDryRun -or ($MaxWritesPerRun -gt 0 -and $writes -ge $MaxWritesPerRun)) {
                $unbound.Add("UNBOUND $label — to stamp $target [$why]")
                if ($MarkerPass) { Invoke-MarkerPass $token $containerId $label $cid $binding $true 'Unstamped' }
                continue
            }

            # WRITE-AHEAD: the reversal row is on disk before the write it reverses.
            $manifest.WriteLine("$containerId,$cid,,$target,$((Get-Date).ToUniversalTime().ToString('o'))$(if ($MarkerPass) { ',stamp,' })")
            $manifest.Flush()
            $writes++
            $stampFailed = $false
            try {
                Write-ContainerStamp $token $containerId $target
                $after = Read-ContainerBinding $token $containerId
                if ($after.Stamp -ne $target) { throw "read-back: $($after.Stamp)" }
                $stats.Stamped++; Write-StampLog "STAMPED $label -> $target [$why]"
            } catch {
                $stats.Failed++; Write-StampLog "FAIL ${label}: $($_.Exception.Message)"
                $unbound.Add("UNBOUND $label — the stamp write failed: $($_.Exception.Message)")
                $stampFailed = $true
            }
            # A container whose stamp just failed is left alone (its ownership state is uncertain); otherwise mark it too.
            if ($MarkerPass -and -not $stampFailed) { Invoke-MarkerPass $token $containerId $label $cid $binding $true 'Stamped' }
        }
        $uri = $page.'@odata.nextLink'
    }
}

foreach ($container in $explicitBind.Keys) {
    if (-not $bindUsed.ContainsKey($container)) {
        $stats.BindUnused++
        $listed.Add("BIND-UNUSED $container — no examined config lists this container; nothing was written for it (typo, another environment's container, or its config was skipped).")
    }
}

foreach ($line in $listed) { Write-StampLog $line }
foreach ($line in $unbound) { Write-StampLog $line }
foreach ($line in $unmarked) { Write-StampLog $line }
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
if ($unbound.Count -gt 0) {
    Write-Host "Containers still UNBOUND ($($unbound.Count)) — no SPE admin route reaches them:"
    $unbound | ForEach-Object { Write-Host "  $_" }
}
if ($unmarked.Count -gt 0) {
    Write-Host "Containers still UNMARKED for customer '$CustomerId' ($($unmarked.Count)) — the BFF refuses them (spe_container_not_owned):"
    $unmarked | ForEach-Object { Write-Host "  $_" }
}
if (-not $IsDryRun) { Write-Host "Reversal manifest: $manifestPath  (undo: -RevertManifest `"$manifestPath`" -Apply)" }
Write-Host "Log: $LogPath"

if ($Verify) {
    # Round 35 item 2: NO container may be unbound (derivable-unstamped, underivable, malformed); every stamp must match
    # its records; every binding must be readable; every config's containers must have been listed.
    exit ([int](($unbound.Count + $stats.Mismatch + $stats.Unreadable + $stats.ConfigsSkipped + $stats.BindConflict + $stats.BindUnused + $unmarked.Count) -gt 0))
}
exit ([int](($stats.Failed + $stats.BindConflict + $stats.BindUnused) -gt 0))
