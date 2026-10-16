<#
.SYNOPSIS
    eBilling R1 decision D-90 - grants the Spaarke Core User role (and Spaarke Basic User, when it holds sprk_invoice
    privileges) privileges on the eBilling tables by mirroring what the role already holds on sprk_invoice.

.DESCRIPTION
    Idempotent Dataverse Web API script (dev only in R1). Every step is read, compare, then add; a second run reports no change
    and writes nothing. Run -WhatIf first: it reads everything, prints the privileges it would add and writes nothing.

    Why (decisions-log D-90, task 029 finding B): the Spaarke roles carried no privilege on the new tables, so a non-admin got
    0x80040220 (no prvRead) on sprk_invoiceapproval and the "Invoices to review" view failed.

    Rule (per role, read live from the role in the root business unit):
      WORKING tables  sprk_invoiceapproval, sprk_invoicesubmission, sprk_invoicecorrection, sprk_externalidentifier,
                      sprk_billingexport: for every privilege the role holds on sprk_invoice (Create, Read, Write, Delete,
                      Append, AppendTo, Assign, Share) the same privilege on the working table at the same depth.
      CONFIG tables   sprk_ebillingsetting, sprk_ratecard, sprk_ratecardrate, sprk_billingcodeset, sprk_billingcode,
                      sprk_ruleset, sprk_timekeeper: Read at the depth the role holds for Read on sprk_invoice. An
                      organization-owned table only offers Global depth (the platform refuses Deep), so Read is then added at
                      Global - the only depth such a table has. AppendTo is added on a config table only when a lookup of one
                      of the five working tables targets it (computed from the metadata; none does today). No Create / Write /
                      Delete on config tables: only admin roles write configuration.
      A role that holds no sprk_invoice privilege is left alone (reported).

    NEVER removes or lowers anything: a privilege already held at the target depth or above is skipped; one held at a lower
    depth is raised through AddPrivilegesRole; nothing is passed to ReplacePrivilegesRole or RemovePrivilegeRole. The roles are
    modified in the root business unit; Dataverse propagates the change to the per-business-unit copies, and the script checks
    that the copies picked it up.

    No publish is needed (privileges are not a customization that is published). The solution source of the roles
    (src/dataverse/solutions/SpaarkeMaster/Roles) is kept in step by hand from an export, see
    projects/ebilling-r1/notes/029-wp1-apply-and-export.md "Role privileges (D-90)".

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else the dev environment.

.PARAMETER SolutionUniqueName
    Only used by the shared helper state; no solution component is created.

.PARAMETER RoleNames
    The roles to mirror. Default: Spaarke Core User, Spaarke Basic User.

.EXAMPLE
    pwsh scripts/ebilling/Deploy-EbillingRolePrivileges.ps1 -WhatIf
    pwsh scripts/ebilling/Deploy-EbillingRolePrivileges.ps1

.NOTES
    Project: ebilling-r1, decision D-90 (follow-up to task 029). Dataverse helpers: EbillingSchema.psm1.
    Exit codes: 0 = done (added / skipped), 1 = error, 2 = escalation (a refused guard; nothing further changed).
#>

using module .\EbillingSchema.psm1

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$EnvironmentUrl = $(if ($env:DATAVERSE_URL) { $env:DATAVERSE_URL } else { 'https://spaarkedev1.crm.dynamics.com' }),
    [string]$SolutionUniqueName = 'SpaarkeMaster',
    [string[]]$RoleNames = @('Spaarke Core User', 'Spaarke Basic User')
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$DryRun = [bool]$WhatIfPreference

$Reference = 'sprk_invoice'
$WorkingTables = 'sprk_invoiceapproval', 'sprk_invoicesubmission', 'sprk_invoicecorrection', 'sprk_externalidentifier', 'sprk_billingexport'
$ConfigTables = 'sprk_ebillingsetting', 'sprk_ratecard', 'sprk_ratecardrate', 'sprk_billingcodeset', 'sprk_billingcode', 'sprk_ruleset', 'sprk_timekeeper'
$Verbs = 'Create', 'Read', 'Write', 'Delete', 'Append', 'AppendTo', 'Assign', 'Share'
$Rank = @{ Basic = 0; Local = 1; Deep = 2; Global = 3 }

$State = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName $SolutionUniqueName -DryRun:$DryRun
$script:Changed = $State.Changed
$script:Skipped = $State.Skipped

# ---------------------------------------------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------------------------------------------

# The table's schema name (privilege names are prv{Verb}{SchemaName}, with the schema name's case) and ownership.
function Get-TableInfo([string]$Table) {
    $e = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')?`$select=SchemaName,OwnershipType" -Retries 2
    if (-not $e) { throw "$Table does not exist in $EnvironmentUrl." }
    return [pscustomobject]@{ Table = $Table; Schema = [string]$e.SchemaName; Ownership = [string]$e.OwnershipType }
}

# verb -> privilege row (id, name, which depths exist) of one table.
function Get-TablePrivileges($info) {
    $r = Invoke-Dv "privileges?`$select=name,privilegeid,canbebasic,canbelocal,canbedeep,canbeglobal&`$filter=endswith(name,'$($info.Schema)')"
    $map = @{}
    foreach ($p in @($r.value)) {
        foreach ($v in $Verbs) {
            if ($p.name -ceq "prv$v$($info.Schema)") {
                $depths = @(); if ($p.canbebasic) { $depths += 'Basic' }; if ($p.canbelocal) { $depths += 'Local' }; if ($p.canbedeep) { $depths += 'Deep' }; if ($p.canbeglobal) { $depths += 'Global' }
                $map[$v] = [pscustomobject]@{ Verb = $v; Name = [string]$p.name; Id = [string]$p.privilegeid; Depths = $depths }
            }
        }
    }
    return $map
}

# The root role of a name (the role in the root business unit; the per-business-unit copies point at it).
function Get-RootRole([string]$Name) {
    $r = Invoke-Dv "roles?`$select=roleid,name,_businessunitid_value,_parentrootroleid_value&`$filter=name eq '$Name'"
    $root = @($r.value | Where-Object { $_._parentrootroleid_value -eq $_.roleid })
    if ($root.Count -ne 1) { return $null }
    return [pscustomobject]@{ Id = [string]$root[0].roleid; Name = $Name; BusinessUnitId = [string]$root[0]._businessunitid_value }
}

# privilege id (lower case) -> depth, for a role.
function Get-RoleDepths([string]$RoleId) {
    $r = Invoke-Dv "RetrieveRolePrivilegesRole(RoleId=$RoleId)"
    $m = @{}
    foreach ($p in @($r.RolePrivileges)) { $m[([string]$p.PrivilegeId).ToLower()] = [string]$p.Depth }
    return $m
}

# The smallest depth the privilege offers that is at or above the wanted one ($null when none).
function Select-Depth([string]$Wanted, $priv) {
    foreach ($d in 'Basic', 'Local', 'Deep', 'Global') {
        if ($Rank[$d] -ge $Rank[$Wanted] -and $priv.Depths -contains $d) { return $d }
    }
    return $null
}

# Config tables that a lookup of a working table targets (those need AppendTo).
function Get-LookupTargets([string[]]$From, [string[]]$Among) {
    $hit = @{}
    foreach ($t in $From) {
        $a = Get-DvOrNull "EntityDefinitions(LogicalName='$t')/Attributes/Microsoft.Dynamics.CRM.LookupAttributeMetadata?`$select=LogicalName,Targets"
        foreach ($x in @($a.value)) {
            foreach ($tg in @($x.Targets)) { if ($Among -contains $tg) { if (-not $hit.ContainsKey($tg)) { $hit[$tg] = @() }; $hit[$tg] += "$t.$($x.LogicalName)" } }
        }
    }
    return $hit
}

# ---------------------------------------------------------------------------------------------------------------
# Run
# ---------------------------------------------------------------------------------------------------------------

Write-Host ""
Write-Host "eBilling D-90 - role privileges on the eBilling tables (mirror of sprk_invoice)" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Roles: $($RoleNames -join ', ')   Mode: $(if ($DryRun) { 'WHATIF (no writes)' } else { 'APPLY' })"
Write-Host ""

$Report = [System.Collections.Generic.List[object]]::new()   # {Role, Table, Verb, Depth, Status}

try {
    # --- table metadata, privileges -------------------------------------------------------------------------------
    Write-Host "0. Tables" -ForegroundColor Cyan
    $info = @{}; $privs = @{}
    foreach ($t in @($Reference) + $WorkingTables + $ConfigTables) {
        $info[$t] = Get-TableInfo $t
        $privs[$t] = Get-TablePrivileges $info[$t]
        Write-Host ("  {0,-26} {1,-17} {2} privileges" -f $t, $info[$t].Ownership, $privs[$t].Count)
    }
    foreach ($t in $WorkingTables) { foreach ($v in $Verbs) { if (-not $privs[$t].ContainsKey($v)) { throw "Privilege prv$v$($info[$t].Schema) not found." } } }
    foreach ($v in $Verbs) { if (-not $privs[$Reference].ContainsKey($v)) { throw "Privilege prv$v$($info[$Reference].Schema) not found." } }
    foreach ($t in $ConfigTables) { foreach ($v in 'Read', 'AppendTo') { if (-not $privs[$t].ContainsKey($v)) { throw "Privilege prv$v$($info[$t].Schema) not found." } } }

    # AppendTo on a config table: only where a working table's lookup targets it.
    $lookupHits = Get-LookupTargets -From $WorkingTables -Among $ConfigTables
    Write-Host "  lookups from the working tables to the configuration tables: $(if ($lookupHits.Count) { ($lookupHits.Keys | ForEach-Object { "$_ <- " + ($lookupHits[$_] -join ', ') }) -join '; ' } else { 'none (no configuration table needs AppendTo)' })"

    foreach ($roleName in $RoleNames) {
        Write-Host ""
        Write-Host "Role: $roleName" -ForegroundColor Cyan
        $role = Get-RootRole $roleName
        if (-not $role) { throw [EscalationException]::new("Role '$roleName' was not found as a single root role in $EnvironmentUrl. Nothing was changed.") }
        $have = Get-RoleDepths $role.Id
        Write-Host "  root role $($role.Id) (business unit $($role.BusinessUnitId)), $($have.Count) privileges"

        # What the role holds on sprk_invoice.
        $invoice = [ordered]@{}
        foreach ($v in $Verbs) {
            $k = $privs[$Reference][$v].Id.ToLower()
            if ($have.ContainsKey($k)) { $invoice[$v] = $have[$k] }
        }
        Write-Host "  sprk_invoice: $(if ($invoice.Count) { ($invoice.Keys | ForEach-Object { "$_=$($invoice[$_])" }) -join ', ' } else { 'no privileges' })"
        if ($invoice.Count -eq 0) {
            $script:Skipped.Add("$roleName (no sprk_invoice privilege)"); Write-Host "  skipped  role holds no sprk_invoice privilege - left alone" -ForegroundColor Yellow
            continue
        }

        # Wanted privileges: (table, verb) -> depth.
        $want = [System.Collections.Generic.List[object]]::new()
        foreach ($t in $WorkingTables) {
            foreach ($v in $invoice.Keys) { $want.Add([pscustomobject]@{ Table = $t; Verb = $v; Depth = [string]$invoice[$v]; Note = '' }) }
        }
        foreach ($t in $ConfigTables) {
            if ($invoice.Contains('Read')) {
                $d = Select-Depth $invoice['Read'] $privs[$t]['Read']
                if (-not $d) { throw "No depth at or above $($invoice['Read']) exists for prvRead$($info[$t].Schema)." }
                $note = if ($d -ne $invoice['Read']) { "$($info[$t].Ownership) table offers $($privs[$t]['Read'].Depths -join '/') only" } else { '' }
                $want.Add([pscustomobject]@{ Table = $t; Verb = 'Read'; Depth = $d; Note = $note })
            }
            if ($lookupHits.ContainsKey($t) -and $invoice.Contains('AppendTo')) {
                $d = Select-Depth $invoice['AppendTo'] $privs[$t]['AppendTo']
                if (-not $d) { throw "No depth at or above $($invoice['AppendTo']) exists for prvAppendTo$($info[$t].Schema)." }
                $want.Add([pscustomobject]@{ Table = $t; Verb = 'AppendTo'; Depth = $d; Note = "lookup from $($lookupHits[$t] -join ', ')" })
            }
        }

        # Compare with what the role holds; collect the adds.
        $toAdd = [System.Collections.Generic.List[object]]::new()
        foreach ($w in $want) {
            $p = $privs[$w.Table][$w.Verb]
            $cur = $have[$p.Id.ToLower()]
            $line = "{0,-26} {1,-9} {2,-6}" -f $w.Table, $w.Verb, $w.Depth
            if ($cur -and $Rank[$cur] -ge $Rank[$w.Depth]) {
                $script:Skipped.Add("$roleName $($p.Name)")
                $Report.Add([pscustomobject]@{ Role = $roleName; Table = $w.Table; Verb = $w.Verb; Depth = $cur; Status = 'held' })
                Write-Host "  skipped  $line (held at $cur)$(if ($Rank[$cur] -gt $Rank[$w.Depth]) { ' - higher than the mirror, left as is' })" -ForegroundColor Yellow
                continue
            }
            $toAdd.Add([pscustomobject]@{ Priv = $p; Depth = $w.Depth; Was = $cur; Table = $w.Table; Verb = $w.Verb; Note = $w.Note })
            if ($DryRun) {
                $script:Changed.Add("$roleName $($p.Name) $($w.Depth)")
                Write-Host "  WOULD ADD $line $(if ($cur) { "(raise from $cur)" }) $(if ($w.Note) { "[$($w.Note)]" })" -ForegroundColor Cyan
            }
        }

        if ($toAdd.Count -gt 0 -and -not $DryRun) {
            $body = @{ Privileges = @($toAdd | ForEach-Object { @{ Depth = $_.Depth; PrivilegeId = $_.Priv.Id; BusinessUnitId = $role.BusinessUnitId } }) }
            Invoke-Dv -Path "roles($($role.Id))/Microsoft.Dynamics.CRM.AddPrivilegesRole" -Method POST -Body $body | Out-Null
            # Read back (settled): every added privilege must now read at the wanted depth, and nothing the role held may be lower.
            $after = $null
            for ($i = 0; $i -lt 6; $i++) {
                $after = Get-RoleDepths $role.Id
                $miss = @($toAdd | Where-Object { -not $after.ContainsKey($_.Priv.Id.ToLower()) -or $Rank[$after[$_.Priv.Id.ToLower()]] -lt $Rank[$_.Depth] })
                if ($miss.Count -eq 0) { break }
                Start-Sleep -Seconds 3
            }
            if ($miss.Count) { throw "AddPrivilegesRole on '$roleName' did not take for: $(($miss | ForEach-Object { $_.Priv.Name }) -join ', ')" }
            $lowered = @($have.Keys | Where-Object { -not $after.ContainsKey($_) -or $Rank[$after[$_]] -lt $Rank[$have[$_]] })
            if ($lowered.Count) { throw [EscalationException]::new("After the add, $($lowered.Count) privilege(s) of '$roleName' read lower than before: $($lowered -join ', '). STOP: investigate before another run.") }
            foreach ($a in $toAdd) {
                $script:Changed.Add("$roleName $($a.Priv.Name) $($a.Depth)")
                $Report.Add([pscustomobject]@{ Role = $roleName; Table = $a.Table; Verb = $a.Verb; Depth = $a.Depth; Status = 'ADDED' })
                Write-Host ("  ADDED    {0,-26} {1,-9} {2,-6} {3}" -f $a.Table, $a.Verb, $a.Depth, $(if ($a.Note) { "[$($a.Note)]" })) -ForegroundColor Green
            }
            Write-Host "  role now holds $($after.Count) privileges (was $($have.Count), +$($toAdd.Count))"
        } elseif ($toAdd.Count -eq 0) {
            Write-Host "  nothing to add for $roleName" -ForegroundColor Yellow
        }

        # --- Evidence: the role's privileges on the 13 tables (settled read) -------------------------------------
        $final = if ($DryRun) { $have } else { Get-RoleDepths $role.Id }
        Write-Host "  privileges of '$roleName' on the tables ($(if ($DryRun) { 'current' } else { 'after the run' })):"
        foreach ($t in @($Reference) + $WorkingTables + $ConfigTables) {
            $held = foreach ($v in $Verbs) {
                if (-not $privs[$t].ContainsKey($v)) { continue }
                $k = $privs[$t][$v].Id.ToLower()
                if ($final.ContainsKey($k)) { "$v=$($final[$k])" }
            }
            Write-Host ("    {0,-26} {1}" -f $t, $(if ($held) { $held -join ' ' } else { '(none)' }))
        }

        # --- Per-business-unit copies pick the change up -------------------------------------------------------------
        if (-not $DryRun -and $toAdd.Count -gt 0) {
            $copies = @((Invoke-Dv "roles?`$select=roleid,_businessunitid_value&`$filter=_parentrootroleid_value eq $($role.Id) and roleid ne $($role.Id)").value)
            $bad = @()
            foreach ($c in $copies) {
                $cd = $null
                for ($i = 0; $i -lt 8; $i++) {
                    $cd = Get-RoleDepths ([string]$c.roleid)
                    $m = @($toAdd | Where-Object { -not $cd.ContainsKey($_.Priv.Id.ToLower()) -or $Rank[$cd[$_.Priv.Id.ToLower()]] -lt $Rank[$_.Depth] })
                    if ($m.Count -eq 0) { break }
                    Start-Sleep -Seconds 4
                }
                if ($m.Count) { $bad += "$($c.roleid) (business unit $($c._businessunitid_value)): $($m.Count) missing" }
            }
            if ($bad.Count) { Write-Host "  WARNING: business-unit copies without the change: $($bad -join '; ')" -ForegroundColor DarkYellow }
            else { Write-Host "  business-unit copies ($($copies.Count)) hold the added privileges" -ForegroundColor Green }
        }
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
Write-Host "================ Summary ================" -ForegroundColor Cyan
Write-Host ("{0}: {1}" -f $(if ($DryRun) { 'would add' } else { 'added' }), $script:Changed.Count)
foreach ($c in $script:Changed) { Write-Host "  $c" }
Write-Host ("held / skipped: {0}" -f $script:Skipped.Count)
if ($DryRun) { Write-Host "WhatIf run: nothing was written." -ForegroundColor Cyan }
exit 0
