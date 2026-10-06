<#
.SYNOPSIS
    The provenance backfill of sprk_externalrecordaccess.sprk_grantedbycontactid (unified-access-control-r2 task 140,
    session 27 round 50 item 2): WHICH rows need it, and the CONDITIONAL write. Dot-sourced by
    scripts/Deploy-ExternalRecordAccessContactGrantor.ps1 (step f).

.DESCRIPTION
    The BFF writes a contact issuer's id as text (sprk_grantedbycontactid) in the same write as the issuer lookup
    (sprk_grantedbycontact), so the record survives the contact's deletion — the lookup's Delete cascade (RemoveLink)
    empties the lookup alone, and the reconciliation job reads "recorded but the lookup is empty" as "the issuer was
    deleted". A row written before the provenance column existed records nothing; this backfills it from the lookup.

    Kept apart from the script so the decision and the write rule are exercised offline, with no Dataverse:
    tests/integration/auth/UnifiedAccessControl/GrantProvenanceBackfillScriptTests.cs runs these functions in pwsh over
    rows shaped like the Web API's and a recording PATCH.

    The rules:
      - A row needs the backfill when its lookup names a contact and its provenance is not exactly that contact's id in
        the ONE text form (ExternalGrantLifecycle.ContactIssuerProvenance: lower-case, hyphenated). A row whose lookup is
        EMPTY is never touched: if it records a provenance, its issuer was deleted — that record is the point.
      - Each write is conditional on the version the row was read at (If-Match = its @odata.etag): an internal take-over
        that cleared the issuer in between must not get a provenance written back over it, and If-Match also stops a
        PATCH from creating a row that was deleted. A row read WITHOUT a version is refused, never written unconditionally.
      - HTTP 412 (the row changed since it was read) is counted and the row left as it is — a re-run picks it up. Any
        other failure throws.
#>

# The ONE text form of a contact issuer's id — ExternalGrantLifecycle.ContactIssuerProvenance (Guid.ToString("D")).
function ConvertTo-ContactIssuerProvenance([string]$ContactId) {
    ([guid]$ContactId).ToString('D')
}

# The rows whose lookup names a contact but whose provenance does not record it: one item per row, with the value to
# write and the version (ETag) the row was read at.
function Get-GrantProvenanceBackfill {
    param(
        [object[]]$Rows,
        [Parameter(Mandatory)][string]$LookupProperty,
        [Parameter(Mandatory)][string]$ProvenanceProperty
    )
    foreach ($row in @($Rows)) {
        $lookup = "$($row.$LookupProperty)"
        if (-not $lookup) { continue }
        $value = ConvertTo-ContactIssuerProvenance $lookup
        if ("$($row.$ProvenanceProperty)" -ceq $value) { continue }
        [pscustomobject]@{ Id = $row.sprk_externalrecordaccessid; ETag = $row.'@odata.etag'; Value = $value }
    }
}

# Writes each item with If-Match on its version through $Patch (path, body, extra headers). Returns Written / Changed.
function Invoke-GrantProvenanceBackfill {
    param(
        [object[]]$Items,
        [Parameter(Mandatory)][string]$ProvenanceProperty,
        [Parameter(Mandatory)][scriptblock]$Patch
    )
    $written = 0; $changed = 0
    foreach ($item in @($Items)) {
        if (-not $item.ETag) { throw "Row $($item.Id) was read without a version (@odata.etag); refusing an unconditional write." }
        try {
            & $Patch "sprk_externalrecordaccesses($($item.Id))" @{ $ProvenanceProperty = $item.Value } @{ 'If-Match' = $item.ETag }
            $written++
        } catch {
            $status = $_.Exception.Response.StatusCode
            if ($null -eq $status -or [int]$status -ne 412) { throw }
            $changed++
        }
    }
    [pscustomobject]@{ Written = $written; Changed = $changed }
}
