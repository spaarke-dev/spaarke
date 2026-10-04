# scripts/common/DataverseSolutionMembership.ps1
# ---------------------------------------------------------------------------
# Reusable helper: answer "is this component in the solution?" the way Dataverse means it.
#
# Why this exists (unified-access-control-r2 batch 4 integration, schema-script verify defect):
#   Each schema script (scripts/Set-*Schema.ps1) used to read solutioncomponents itself, and the copies drifted:
#   (a) a column or relationship of a table added with rootcomponentbehavior = 0 ("include subcomponents") has NO
#       solutioncomponents row of its own, and AddSolutionComponent on it is a no-op -- so -Verify reported it
#       MISSING forever and the gate could never pass;
#   (b) most copies read ONE page, so in a solution past the page size a component that IS included read as missing.
#   One implementation, used by every schema script (pinned by SchemaScriptSolutionMembershipGuardTests).
#
# Usage:
#   . (Join-Path $PSScriptRoot 'common/DataverseSolutionMembership.ps1')
#   $membership = Get-DvSolutionMembership -Api $Api -Headers $headers -SolutionId $solution.solutionid
#   Test-DvInSolution -Membership $membership -ComponentId $attr.MetadataId -TableMetadataId $tableId
#     -> 'Direct'    the component has its own row
#     -> 'ViaTable'  its table is in the solution with rootcomponentbehavior = 0
#     -> $null       not in the solution
# Read-only. A failed read throws (a verify that cannot read the solution must not report anything as present).
# ---------------------------------------------------------------------------

# No Set-StrictMode here: this file is dot-sourced, so it would change the CALLING script's mode.

function Get-DvSolutionMembership {
    param(
        [Parameter(Mandatory)][string]$Api,
        [Parameter(Mandatory)][hashtable]$Headers,
        [Parameter(Mandatory)][Guid]$SolutionId
    )

    $objectIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $tablesWithSubcomponents = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

    $next = "$Api/solutioncomponents?`$select=objectid,componenttype,rootcomponentbehavior&`$filter=_solutionid_value eq $SolutionId"
    while ($next) {
        $page = Invoke-RestMethod -Uri $next -Headers $Headers -Method Get
        foreach ($row in @($page.value)) {
            $id = $row.objectid.ToString()
            [void]$objectIds.Add($id)
            # componenttype 1 = Entity; rootcomponentbehavior 0 = IncludeSubcomponents.
            if ($row.componenttype -eq 1 -and $row.rootcomponentbehavior -eq 0) { [void]$tablesWithSubcomponents.Add($id) }
        }
        $link = $page.PSObject.Properties['@odata.nextLink']
        $next = if ($link) { $link.Value } else { $null }
    }

    [pscustomobject]@{ ObjectIds = $objectIds; TablesWithSubcomponents = $tablesWithSubcomponents }
}

function Test-DvInSolution {
    param(
        [Parameter(Mandatory)]$Membership,
        [Parameter(Mandatory)]$ComponentId,
        # The owning table's MetadataId, for a column (type 2), key (14) or relationship (10). Omit for a component
        # that is not a table subcomponent (a field security profile, an option set).
        $TableMetadataId = $null
    )

    if ($Membership.ObjectIds.Contains($ComponentId.ToString())) { return 'Direct' }
    if ($TableMetadataId -and $Membership.TablesWithSubcomponents.Contains($TableMetadataId.ToString())) { return 'ViaTable' }
    return $null
}
