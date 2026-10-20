<#
.SYNOPSIS
    eBilling R1 decision D-135 - grants the role "Spaarke Internal Contact User (Workforce)" Create (Basic) and Read (Deep)
    on sprk_invoicesubmission, so the six external invoice-submission routes pass their workforce ceiling (UAC task 186).

.DESCRIPTION
    Idempotent Dataverse Web API script (dev only in R1). Reads, compares, then adds; a second run reports no change and
    writes nothing. Run -WhatIf first: it reads everything, prints what it would add and writes nothing.

    Why (decisions-log D-135, owner 2026-10-20): since the #1911 master merge every /api/v1/external route declares a
    workforce ceiling. uac-r2 set the invoice-submission routes to Create on sprk_invoicesubmission (the three POSTs and
    firm-records) and Read on it (GET by id, the list). The dev role held no privilege on the table, so D-118 workforce users
    were refused on all six routes. The privileges satisfy the ceiling only; the BFF's app identity still reads and writes.

    Depths (uac-r2, following the role's convention): Create at Basic (as the role's sprk_document Create), Read at Deep (as
    its matter / project / work assignment Read). Nothing else: no Write, Append or AppendTo on sprk_invoicesubmission.

    NEVER removes or lowers anything: AddPrivilegesRole merges into the root role; a privilege already held at the target
    depth or above is skipped. The script checks that no other privilege of the role reads lower afterwards.

    Solution source: none. The role is excluded from SpaarkeMaster until UAC task 186 closes (docs/data-model/package-scope.json);
    dev is its record until then, and UAC exports it from dev with these two privileges included.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.EXAMPLE
    pwsh scripts/ebilling/Grant-EbillingWorkforceSubmissionPrivileges.ps1 -WhatIf
    pwsh scripts/ebilling/Grant-EbillingWorkforceSubmissionPrivileges.ps1

.NOTES
    Project: ebilling-r1, decision D-135. Dataverse helpers: EbillingSchema.psm1.
    Exit codes: 0 = done (added / skipped), 1 = error, 2 = escalation (a refused guard; nothing further changed).
#>

using module .\EbillingSchema.psm1

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$EnvironmentUrl = $(if ($env:DATAVERSE_URL) { $env:DATAVERSE_URL } else { 'https://spaarkedev1.crm.dynamics.com' })
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$DryRun = [bool]$WhatIfPreference

$RoleName = 'Spaarke Internal Contact User (Workforce)'
$Table = 'sprk_invoicesubmission'
$Wanted = [ordered]@{ Create = 'Basic'; Read = 'Deep' }
$Rank = @{ Basic = 0; Local = 1; Deep = 2; Global = 3 }

$State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName 'SpaarkeMaster' -DryRun:$DryRun

function Get-RoleDepths([string]$RoleId) {
    $r = Invoke-Dv "RetrieveRolePrivilegesRole(RoleId=$RoleId)"
    $m = @{}
    foreach ($p in @($r.RolePrivileges)) { $m[([string]$p.PrivilegeId).ToLower()] = [string]$p.Depth }
    return $m
}

Write-Host ""
Write-Host "eBilling D-135 - $RoleName on $Table" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Mode: $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' })"
Write-Host ""

try {
    # The table's schema name gives the privilege names (read live, never guessed).
    $entity = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')?`$select=SchemaName" -Retries 2
    if (-not $entity) { throw "$Table does not exist in $EnvironmentUrl." }
    $privs = @{}
    foreach ($verb in $Wanted.Keys) {
        $name = "prv$verb$($entity.SchemaName)"
        $r = Invoke-Dv "privileges?`$select=name,privilegeid,canbebasic,canbedeep&`$filter=name eq '$name'"
        $p = @($r.value | Where-Object { $_.name -ceq $name })
        if ($p.Count -ne 1) { throw "Privilege $name not found." }
        $privs[$verb] = [pscustomobject]@{ Name = $name; Id = ([string]$p[0].privilegeid).ToLower() }
    }

    # The root role (the one in the root business unit; the per-business-unit copies point at it).
    $roles = Invoke-Dv "roles?`$select=roleid,name,_businessunitid_value,_parentrootroleid_value&`$filter=name eq '$RoleName'"
    $root = @($roles.value | Where-Object { $_._parentrootroleid_value -eq $_.roleid })
    if ($root.Count -ne 1) { throw [EscalationException]::new("Role '$RoleName' was not found as a single root role. Nothing was changed.") }
    $roleId = [string]$root[0].roleid
    $buId = [string]$root[0]._businessunitid_value

    $before = Get-RoleDepths $roleId
    Write-Host "Root role $roleId (business unit $buId): $($before.Count) privileges"
    $toAdd = @()
    foreach ($verb in $Wanted.Keys) {
        $p = $privs[$verb]; $cur = $before[$p.Id]
        Write-Host ("  before   {0,-36} {1}" -f $p.Name, $(if ($cur) { $cur } else { '(none)' }))
        if ($cur -and $Rank[$cur] -ge $Rank[$Wanted[$verb]]) { $State.Skipped.Add($p.Name); continue }
        $toAdd += [pscustomobject]@{ Priv = $p; Depth = $Wanted[$verb] }
        if ($DryRun) { $State.Changed.Add("$($p.Name) $($Wanted[$verb])"); Write-Host "  WOULD ADD $($p.Name) at $($Wanted[$verb])" -ForegroundColor Cyan }
    }

    if ($toAdd.Count -gt 0 -and -not $DryRun) {
        $body = @{ Privileges = @($toAdd | ForEach-Object { @{ Depth = $_.Depth; PrivilegeId = $_.Priv.Id; BusinessUnitId = $buId } }) }
        Invoke-Dv -Path "roles($roleId)/Microsoft.Dynamics.CRM.AddPrivilegesRole" -Method POST -Body $body | Out-Null
        $after = $null
        for ($i = 0; $i -lt 6; $i++) {
            $after = Get-RoleDepths $roleId
            $miss = @($toAdd | Where-Object { -not $after.ContainsKey($_.Priv.Id) -or $Rank[$after[$_.Priv.Id]] -lt $Rank[$_.Depth] })
            if ($miss.Count -eq 0) { break }
            Start-Sleep -Seconds 3
        }
        if ($miss.Count) { throw "AddPrivilegesRole did not take for: $(($miss | ForEach-Object { $_.Priv.Name }) -join ', ')" }
        $lowered = @($before.Keys | Where-Object { -not $after.ContainsKey($_) -or $Rank[$after[$_]] -lt $Rank[$before[$_]] })
        if ($lowered.Count) { throw [EscalationException]::new("After the add, $($lowered.Count) privilege(s) read lower than before: $($lowered -join ', '). STOP.") }
        foreach ($a in $toAdd) { $State.Changed.Add("$($a.Priv.Name) $($a.Depth)") }
        foreach ($verb in $Wanted.Keys) { Write-Host ("  after    {0,-36} {1}" -f $privs[$verb].Name, $after[$privs[$verb].Id]) -ForegroundColor Green }
        Write-Host "Role now holds $($after.Count) privileges (was $($before.Count))"
    } elseif ($toAdd.Count -eq 0) {
        Write-Host "Nothing to add: both privileges are held at the wanted depth or above." -ForegroundColor Yellow
    }
}
catch [EscalationException] {
    Write-Host ""
    Write-Host "ESCALATION - STOPPED, nothing further was changed: $($_.Exception.Message)" -ForegroundColor Red
    exit 2
}
catch {
    Write-Host ""
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "The script is re-runnable (it only adds what is missing)." -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host ("{0}: {1}" -f $(if ($DryRun) { 'would add' } else { 'added' }), $State.Changed.Count)
foreach ($c in $State.Changed) { Write-Host "  $c" }
if ($DryRun) { Write-Host "WhatIf run: nothing was written." -ForegroundColor Cyan }
exit 0
