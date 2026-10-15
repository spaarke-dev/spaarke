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

    THE CUSTOMER MARKER (customer-provisioning-orchestration-r1 tasks 227d/227e). A container the BFF serves app-only must
    also carry $SpeContainerCustomerMarkerProperty = the BFF's Customer__Id app setting: SpeContainerOwnershipGuard
    refuses (404 spe_container_not_owned) any container that is neither configured nor marked for its customer. So a
    container a script creates is stamped AND marked, both are read back, and it is removed if either did not land. The
    C# side's one constant is Spaarke.Contracts.Spe.SpeContainerCustomerMarker.PropertyName;
    tests/Spaarke.ArchTests/TenantIsolation/SpeContainerMarkerParityTests pins that they are equal and that every
    Invoke-SpeContainerBindOrRemove call passes -CustomerId.

    Callers: New-BusinessUnitContainer.ps1, Provision-Customer.ps1 (step 10), Create-NewContainerType.ps1
    (-CreateTestContainer) and Backfill-SpeContainerBusinessUnitStamp.ps1 (which binds existing containers).
#>

# ── THE binding. MUST equal Spaarke.Contracts.Spe.SpeContainerBusinessUnitBinding.PropertyName. ───────────────────────
$SpeContainerStampProperty = 'spaarkeBusinessUnitId'

# ── THE customer marker. MUST equal Spaarke.Contracts.Spe.SpeContainerCustomerMarker.PropertyName. ─────────────────────
$SpeContainerCustomerMarkerProperty = 'spaarkeCustomerId'

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
        The binding of ONE container: @{ Found; TypeId; Stamp; Malformed; Marker; MarkerMalformed } (Marker: the spaarkeCustomerId
        value, $null when absent or ambiguous; MarkerMalformed: $true when the marker is present but ambiguous — several
        entries, or one blank value — so "absent" and "unreadable" are never confused). Graph drops customProperties on the
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
    $markers = @()
    if ($c.customProperties) {
        foreach ($p in $c.customProperties.PSObject.Properties) {
            if ($p.Name.Trim() -ieq $SpeContainerStampProperty) { $values += , [string]$p.Value.value }
            if ($p.Name.Trim() -ieq $SpeContainerCustomerMarkerProperty) { $markers += , [string]$p.Value.value }
        }
    }
    # Marker: the one value, trimmed (the BFF compares it ordinally with Customer__Id); $null when absent or ambiguous.
    $marker = if ($markers.Count -eq 1 -and -not [string]::IsNullOrWhiteSpace($markers[0])) { $markers[0].Trim() } else { $null }
    # Present but ambiguous (several entries, or one blank value): NOT absent — a backfill must never write over it.
    $markerMalformed = ($markers.Count -gt 1) -or ($markers.Count -eq 1 -and [string]::IsNullOrWhiteSpace($markers[0]))
    $typeId = ConvertTo-SpeBindingGuid $c.containerTypeId
    if ($values.Count -eq 0) { return @{ Found = $true; TypeId = $typeId; Stamp = $null; Malformed = $false; Marker = $marker; MarkerMalformed = $markerMalformed } }
    $clean = @($values | ForEach-Object { ConvertTo-SpeBindingGuid $_ })
    if (($clean | Where-Object { -not $_ }).Count -gt 0 -or ($clean | Select-Object -Unique).Count -ne 1) {
        return @{ Found = $true; TypeId = $typeId; Stamp = $null; Malformed = $true; Marker = $marker; MarkerMalformed = $markerMalformed }
    }
    return @{ Found = $true; TypeId = $typeId; Stamp = $clean[0]; Malformed = $false; Marker = $marker; MarkerMalformed = $markerMalformed }
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

function Set-SpeContainerCustomerMarker {
    <#
    .SYNOPSIS
        PATCH /containers/{id}/customProperties with the customer marker (merge semantics, the shape the BFF's
        SpeContainerOwnershipGuard.MarkOwnedAsync and L2's H8 send).
    #>
    param(
        [Parameter(Mandatory)][string]$Token,
        [Parameter(Mandatory)][string]$ContainerId,
        [Parameter(Mandatory)][string]$CustomerId,
        [string]$GraphBase = 'https://graph.microsoft.com/v1.0'
    )
    Invoke-SpeGraph $Token 'Patch' "$GraphBase/storage/fileStorage/containers/$([uri]::EscapeDataString($ContainerId))/customProperties" @{ $SpeContainerCustomerMarkerProperty = @{ value = $CustomerId; isSearchable = $false } } | Out-Null
}

function Remove-SpeContainerCustomerMarker {
    <#
    .SYNOPSIS
        PATCH /containers/{id}/customProperties with a $null value for the customer marker — removes ONLY the marker (merge
        semantics; the same null-removal shape Set-SpeContainerStamp sends for the stamp). Used by the backfill's
        -RevertManifest. Callers check the marker still holds the value they wrote before calling.
    #>
    param(
        [Parameter(Mandatory)][string]$Token,
        [Parameter(Mandatory)][string]$ContainerId,
        [string]$GraphBase = 'https://graph.microsoft.com/v1.0'
    )
    Invoke-SpeGraph $Token 'Patch' "$GraphBase/storage/fileStorage/containers/$([uri]::EscapeDataString($ContainerId))/customProperties" @{ $SpeContainerCustomerMarkerProperty = $null } | Out-Null
}

function Get-SpeContainerMarkerDecision {
    <#
    .SYNOPSIS
        THE pure per-container decision of Backfill-SpeContainerBusinessUnitStamp.ps1's customer-marker pass (issue #1889:
        per-record containers created before the marker shipped carry none, and SpeContainerOwnershipGuard refuses them).
        No I/O - everything is passed in, so it is unit-tested (tests/scripts/Backfill-SpeContainerMarker.Tests.ps1).

        THE RULE. The marker is what makes the BFF SERVE a container to this customer, and every customer stamp shares one
        container type, so a container is marked only on a basis the customer cannot forge on its own:
          (a) at the FIRST read of the run the container ALREADY carried a stamp naming a unit in this environment's
              hierarchy (StampState = Stamped) and the records derive its owner - the stamp is an earlier, reviewed act; or
          (b) an explicit operator -Bind names it (-BindNamed) and does not conflict with the stamp or the records.
        A container this run itself stamps from records alone (StampState = Unstamped, no -Bind) is NOT marked in the same
        pass: sprk_containerid on a secure root is a field the customer can write, so the operator reviews the stamp first.

        Returns $null when -CustomerId is blank (no marker pass runs), else exactly one of:
          SkipUnderivable   no authoritative record (and no -Bind) names the container's owner: NEVER marked. In a shared
                            container type other environments' containers land here.
          UnmarkedNoClaim   the container IS stamped with a unit of this environment (an earlier -Bind) but no record and no
                            -Bind in this run claim it, and its marker is not -CustomerId: listed, never marked automatically.
          SkipDisputedStamp the stamp is MISMATCH / FOREIGN / MALFORMED (or a -Bind contradicts it): ownership is disputed,
                            so it is only listed.
          MarkerMalformed   the marker is present but ambiguous (several entries / blank): listed, never written.
          AlreadyMarked     the marker equals -CustomerId (ordinal, case-sensitive, trimmed): no-op.
          MarkerForeign     the marker is a DIFFERENT value (a case-differing one included): listed, never overwritten.
          MarkNextRun       the container was unstamped at the first read and is derivable from records alone (no -Bind):
                            this run stamps it; it is listed and NOT marked until a later run, after the operator has
                            reviewed the stamp.
          Mark              derivable, undisputed, the marker is ABSENT and basis (a) or (b) holds: the only outcome that writes.
    .PARAMETER Derivable   $true when the records derive the container's owner, or an explicit -Bind names it.
    .PARAMETER StampState  Unstamped | Stamped | Mismatch | Foreign | Malformed - the stamp as read at the FIRST read of
                           this run (Unstamped stays Unstamped even after this run writes the stamp).
    .PARAMETER Marker      The trimmed marker value Get-SpeContainerBinding read ($null when absent or ambiguous).
    .PARAMETER MarkerMalformed  Get-SpeContainerBinding's MarkerMalformed.
    .PARAMETER BindNamed   $true when an explicit -Bind names this container (it is only passed as such when it does not
                           conflict with the stamp or the records).
    #>
    param(
        [AllowEmptyString()][AllowNull()][string]$CustomerId,
        [Parameter(Mandatory)][bool]$Derivable,
        [Parameter(Mandatory)][ValidateSet('Unstamped', 'Stamped', 'Mismatch', 'Foreign', 'Malformed')][string]$StampState,
        [AllowEmptyString()][AllowNull()][string]$Marker,
        [bool]$MarkerMalformed = $false,
        [bool]$BindNamed = $false
    )
    if ([string]::IsNullOrWhiteSpace($CustomerId)) { return $null }
    if (-not $Derivable) {
        # Stamped for this environment (an earlier -Bind) yet claimed by nothing: not marked, but never silent.
        if ($StampState -eq 'Stamped' -and ($MarkerMalformed -or [string]::IsNullOrWhiteSpace($Marker) -or $Marker.Trim() -cne $CustomerId.Trim())) {
            return 'UnmarkedNoClaim'
        }
        return 'SkipUnderivable'
    }
    if ($StampState -in 'Mismatch', 'Foreign', 'Malformed') { return 'SkipDisputedStamp' }
    if ($MarkerMalformed) { return 'MarkerMalformed' }
    if ([string]::IsNullOrWhiteSpace($Marker)) {
        # Basis: (a) already stamped at the first read, or (b) an explicit -Bind. Anything else is a record claim alone.
        if ($StampState -eq 'Unstamped' -and -not $BindNamed) { return 'MarkNextRun' }
        return 'Mark'
    }
    if ($Marker.Trim() -ceq $CustomerId.Trim()) { return 'AlreadyMarked' }
    return 'MarkerForeign'
}

function Invoke-SpeContainerBindOrRemove {
    <#
    .SYNOPSIS
        THE rule for a container a script has JUST CREATED (round 35 item 1; tasks 227d/227e): stamp it with its owning
        business unit AND mark it with its customer (-CustomerId = the BFF's Customer__Id app setting), read both back,
        and — when either did not land, for ANY reason — DELETE the container, so no unbound or unmarked container is left
        behind. Returns nothing on success; THROWS on failure, saying whether the container was removed.
    #>
    param(
        [Parameter(Mandatory)][string]$Token,
        [Parameter(Mandatory)][string]$ContainerId,
        [Parameter(Mandatory)][string]$BusinessUnitId,
        # Must equal the BFF's Customer__Id EXACTLY (the BFF compares ordinally, case-sensitive). Blank or $null is caught
        # inside the try below, so the just-created container is removed rather than orphaned by a binding error.
        [Parameter(Mandatory)][AllowEmptyString()][AllowNull()][string]$CustomerId,
        [string]$GraphBase = 'https://graph.microsoft.com/v1.0'
    )
    $unit = ConvertTo-SpeBindingGuid $BusinessUnitId
    $customer = if ([string]::IsNullOrWhiteSpace($CustomerId)) { $null } else { $CustomerId.Trim() }
    $failure = $null
    try {
        if (-not $unit) { throw "'$BusinessUnitId' is not one business-unit GUID." }
        if (-not $customer) { throw "no customer id: pass -CustomerId with the BFF's Customer__Id app setting." }
        Set-SpeContainerStamp -Token $Token -ContainerId $ContainerId -BusinessUnitId $unit -GraphBase $GraphBase
        Set-SpeContainerCustomerMarker -Token $Token -ContainerId $ContainerId -CustomerId $customer -GraphBase $GraphBase
        $after = Get-SpeContainerBinding -Token $Token -ContainerId $ContainerId -GraphBase $GraphBase
        if (-not $after.Found -or $after.Stamp -ne $unit) {
            throw "the stamp did not read back (read: '$($after.Stamp)', expected '$unit')."
        }
        if ($after.Marker -cne $customer) {
            throw "the customer marker did not read back (read: '$($after.Marker)', expected '$customer')."
        }
        Write-Host "Container $ContainerId bound to business unit $unit and marked for customer '$customer' (read back)." -ForegroundColor Green
        return
    } catch {
        $failure = $_.Exception.Message
    }

    try {
        Invoke-SpeGraph $Token 'Delete' "$GraphBase/storage/fileStorage/containers/$([uri]::EscapeDataString($ContainerId))" | Out-Null
    } catch {
        throw "Container $ContainerId was created but could NOT be bound and marked ($failure) and could NOT be " +
              "removed ($($_.Exception.Message)). Until it carries both its business unit ($SpeContainerStampProperty) and " +
              "its customer ($SpeContainerCustomerMarkerProperty), the BFF does not reach it. Remove it, or bind it with " +
              "Backfill-SpeContainerBusinessUnitStamp.ps1 -Bind '$ContainerId=<businessUnitId>' -Apply and mark it with " +
              "Set-SpeContainerCustomerMarker -ContainerId '$ContainerId' -CustomerId <the BFF's Customer__Id> (this module)."
    }
    throw "Container $ContainerId was created but could not be bound and marked ($failure); it was removed."
}
