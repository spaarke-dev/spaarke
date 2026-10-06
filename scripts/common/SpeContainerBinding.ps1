<#
.SYNOPSIS
    THE PowerShell side of the SharePoint Embedded container -> business-unit binding (unified-access-control-r2 task 165,
    owner round 35 item 1). Dot-source it: . (Join-Path $PSScriptRoot 'common/SpeContainerBinding.ps1')

.DESCRIPTION
    Every SPE container carries its owning business unit as the fileStorageContainer custom property named by
    $SpeContainerStampProperty. The BFF's SPE admin plane authorizes every container route against it, and reaches NO
    unbound container (round 35 item 2) — so every path that creates a container stamps it, reads the stamp back, and
    removes the container if the stamp did not land.

    ONE CONSTANT ON EACH SIDE. $SpeContainerStampProperty is the only PowerShell spelling of the name; the C# side's one
    constant is Spaarke.Contracts.Spe.SpeContainerBusinessUnitBinding.PropertyName (src/server/shared/Contracts).
    tests/Spaarke.ArchTests/SpeAdminContainerBindingGuardTests pins that they are equal, that the literal appears in no
    other script, and that every script that creates a container calls Invoke-SpeContainerBindOrRemove after it.

    Callers: New-BusinessUnitContainer.ps1, Provision-Customer.ps1 (step 10), Create-NewContainerType.ps1
    (-CreateTestContainer) and Backfill-SpeContainerBusinessUnitStamp.ps1 (which binds existing containers).
#>

# ── THE binding. MUST equal Spaarke.Contracts.Spe.SpeContainerBusinessUnitBinding.PropertyName. ───────────────────────
$SpeContainerStampProperty = 'spaarkeBusinessUnitId'

function ConvertTo-SpeBindingGuid {
    # ADR-044: bare, lowercase. $null for anything that is not one non-empty GUID.
    param([AllowEmptyString()][AllowNull()][string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return $null }
    $g = [guid]::Empty
    if ([guid]::TryParse($Value.Trim(), [ref]$g) -and $g -ne [guid]::Empty) { return $g.ToString('D') }
    return $null
}

function Invoke-SpeGraph {
    param([string]$Token, [string]$Method, [string]$Uri, $Body)
    $headers = @{ Authorization = "Bearer $Token"; Accept = 'application/json' }
    if ($null -ne $Body) {
        return Invoke-RestMethod -Method $Method -Uri $Uri -Headers $headers -ContentType 'application/json' `
            -Body ($Body | ConvertTo-Json -Depth 5 -Compress) -ErrorAction Stop
    }
    return Invoke-RestMethod -Method $Method -Uri $Uri -Headers $headers -ErrorAction Stop
}

function Get-SpeContainerBinding {
    <#
    .SYNOPSIS
        The binding of ONE container: @{ Found; TypeId; Stamp; Malformed }. Graph drops customProperties on the
        containers COLLECTION (measured 2026-10-04), so this is always a single-container GET. 404 -> Found = $false;
        any other failure throws (never read a fault as "unbound").
    #>
    param(
        [Parameter(Mandatory)][string]$Token,
        [Parameter(Mandatory)][string]$ContainerId,
        [string]$GraphBase = 'https://graph.microsoft.com/v1.0'
    )
    try {
        $c = Invoke-SpeGraph $Token 'Get' "$GraphBase/storage/fileStorage/containers/$([uri]::EscapeDataString($ContainerId))?`$select=id,containerTypeId,customProperties"
    } catch {
        if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 404) { return @{ Found = $false } }
        throw
    }
    $values = @()
    if ($c.customProperties) {
        foreach ($p in $c.customProperties.PSObject.Properties) {
            if ($p.Name.Trim() -ieq $SpeContainerStampProperty) { $values += , [string]$p.Value.value }
        }
    }
    $typeId = ConvertTo-SpeBindingGuid $c.containerTypeId
    if ($values.Count -eq 0) { return @{ Found = $true; TypeId = $typeId; Stamp = $null; Malformed = $false } }
    $clean = @($values | ForEach-Object { ConvertTo-SpeBindingGuid $_ })
    if (($clean | Where-Object { -not $_ }).Count -gt 0 -or ($clean | Select-Object -Unique).Count -ne 1) {
        return @{ Found = $true; TypeId = $typeId; Stamp = $null; Malformed = $true }
    }
    return @{ Found = $true; TypeId = $typeId; Stamp = $clean[0]; Malformed = $false }
}

function Set-SpeContainerStamp {
    <#
    .SYNOPSIS
        PATCH /containers/{id}/customProperties with the property map as the BODY ROOT (merge semantics — no other
        property is touched; a $null BusinessUnitId removes the stamp). The shape the BFF sends
        (SpeAdminGraphService.WriteBusinessUnitStampAsync) and the L2 H8 handler sends.
    #>
    param(
        [Parameter(Mandatory)][string]$Token,
        [Parameter(Mandatory)][string]$ContainerId,
        [AllowNull()][string]$BusinessUnitId,
        [string]$GraphBase = 'https://graph.microsoft.com/v1.0'
    )
    $value = if ($BusinessUnitId) { @{ value = $BusinessUnitId; isSearchable = $false } } else { $null }
    Invoke-SpeGraph $Token 'Patch' "$GraphBase/storage/fileStorage/containers/$([uri]::EscapeDataString($ContainerId))/customProperties" @{ $SpeContainerStampProperty = $value } | Out-Null
}

function Invoke-SpeContainerBindOrRemove {
    <#
    .SYNOPSIS
        THE rule for a container a script has JUST CREATED (round 35 item 1): stamp it with its owning business unit,
        read the stamp back, and — when it did not land, for ANY reason — DELETE the container, so no unbound container
        is left behind. Returns nothing on success; THROWS on failure, saying whether the container was removed.
    #>
    param(
        [Parameter(Mandatory)][string]$Token,
        [Parameter(Mandatory)][string]$ContainerId,
        [Parameter(Mandatory)][string]$BusinessUnitId,
        [string]$GraphBase = 'https://graph.microsoft.com/v1.0'
    )
    $unit = ConvertTo-SpeBindingGuid $BusinessUnitId
    $failure = $null
    try {
        if (-not $unit) { throw "'$BusinessUnitId' is not one business-unit GUID." }
        Set-SpeContainerStamp -Token $Token -ContainerId $ContainerId -BusinessUnitId $unit -GraphBase $GraphBase
        $after = Get-SpeContainerBinding -Token $Token -ContainerId $ContainerId -GraphBase $GraphBase
        if (-not $after.Found -or $after.Stamp -ne $unit) {
            throw "the stamp did not read back (read: '$($after.Stamp)', expected '$unit')."
        }
        Write-Host "Container $ContainerId bound to business unit $unit ($SpeContainerStampProperty, read back)." -ForegroundColor Green
        return
    } catch {
        $failure = $_.Exception.Message
    }

    try {
        Invoke-SpeGraph $Token 'Delete' "$GraphBase/storage/fileStorage/containers/$([uri]::EscapeDataString($ContainerId))" | Out-Null
    } catch {
        throw "Container $ContainerId was created but could NOT be bound to its business unit ($failure) and could NOT be " +
              "removed ($($_.Exception.Message)). It is UNBOUND: no SPE admin route reaches it. Bind it with " +
              "Backfill-SpeContainerBusinessUnitStamp.ps1 -Bind '$ContainerId=<businessUnitId>' -Apply, or remove it."
    }
    throw "Container $ContainerId was created but could not be bound to its business unit ($failure); it was removed."
}
