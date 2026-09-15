<#
.SYNOPSIS
    Answers the one question task 024 could not: does the SPE container-permissions endpoint
    actually emit `@odata.nextLink`?

.DESCRIPTION
    `unified-access-control-r2` task 024 fixed both ExternalAccess permission reads to follow
    `@odata.nextLink`. The CODE defect was verified; the EXPLOITABILITY was not, and still is not:

      * Microsoft's docs for `GET /storage/fileStorage/containers/{id}/permissions` list
        `$skip`, `$top`, `$orderBy`, `$filter` — and NOT `$skiptoken`.
      * The documented sample response carries `@odata.context` and no `@odata.nextLink`.
      * Server-driven paging in Graph IS `@odata.nextLink` carrying a `$skiptoken`.

    So the evidence LEANS AGAINST this endpoint paging — but nobody has looked. The fix stands
    regardless (ignoring `@odata.nextLink` is an OData protocol violation independent of current
    server behaviour), yet the SEVERITY of finding M1 depends on this answer, and so does whether
    task 047 needs a live paging assertion at all.

    This script is READ-ONLY. It issues GETs and prints what came back. It creates, modifies and
    deletes nothing.

.PARAMETER ContainerId
    An SPE container id (the `b!...` form). Any container with at least TWO permissions will do —
    see the `-Top 1` probe below, which is why this does NOT require a container seeded past a
    page boundary.

.PARAMETER AccessToken
    A Microsoft Graph token whose app holds `FileStorageContainer.Selected` AND the container-type
    level permission for Spaarke's container type. The BFF's own app registration has both.

    ⚠️ An `az account get-access-token --resource https://graph.microsoft.com` token will NOT work:
    that is the Azure CLI's own delegated identity and it 403s with `accessDenied` /
    "Caller does not have required permissions for this API". Confirmed 2026-09-09.

.EXAMPLE
    ./scripts/Test-SpeContainerPermissionPaging.ps1 -ContainerId 'b!xxx' -AccessToken $tok

.NOTES
    Filed alongside task 024. Owner of the answer: task 047 (live verification).
    Related: GitHub #968, #969; projects/unified-access-control-r2/notes/task-024-spe-paging-parity.md §1
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ContainerId,
    [Parameter(Mandatory = $true)][string]$AccessToken,
    [int]$Top = 1
)

$ErrorActionPreference = 'Stop'
$base = "https://graph.microsoft.com/v1.0/storage/fileStorage/containers/$ContainerId/permissions"
$headers = @{ Authorization = "Bearer $AccessToken" }

function Invoke-Probe {
    param([string]$Url, [string]$Label)

    Write-Host ""
    Write-Host "── $Label" -ForegroundColor Cyan
    Write-Host "   GET $Url"

    try {
        $r = Invoke-RestMethod -Uri $Url -Headers $headers -Method Get -ErrorAction Stop
    }
    catch {
        Write-Host "   FAILED: $($_.Exception.Message)" -ForegroundColor Red
        if ($_.ErrorDetails.Message) {
            Write-Host "   $($_.ErrorDetails.Message)" -ForegroundColor DarkGray
        }
        Write-Host "   If this is 403 accessDenied, the token lacks FileStorageContainer.Selected" -ForegroundColor Yellow
        Write-Host "   or the container-type-level grant. See .PARAMETER AccessToken." -ForegroundColor Yellow
        return $null
    }

    $count = if ($r.value) { $r.value.Count } else { 0 }
    $next = $r.'@odata.nextLink'

    Write-Host "   permissions returned : $count"
    if ($next) {
        Write-Host "   @odata.nextLink      : PRESENT" -ForegroundColor Green
        Write-Host "     $next" -ForegroundColor DarkGray
    }
    else {
        Write-Host "   @odata.nextLink      : absent" -ForegroundColor Yellow
    }

    return [pscustomobject]@{ Count = $count; NextLink = $next }
}

Write-Host "SPE container-permission paging probe (read-only)" -ForegroundColor White
Write-Host "Container: $ContainerId"

# Probe 1 — the endpoint exactly as the BFF calls it. If a nextLink appears here, paging is real
# AND this container already exceeds the server's default page size.
$plain = Invoke-Probe -Url $base -Label "Probe 1: unmodified request (what the BFF issues)"

if ($null -eq $plain) { exit 1 }

# Probe 2 — the cheap decisive test. `$top` is DOCUMENTED as supported. If the service does
# server-driven paging at all, capping the page below the total is the condition that produces a
# nextLink — so this answers the question WITHOUT needing a container seeded past the default
# page size, which is what made this look expensive to establish.
$topProbe = $null
if ($plain.Count -gt $Top) {
    $topProbe = Invoke-Probe -Url "$base`?`$top=$Top" -Label "Probe 2: `$top=$Top (forces a partial page)"
}
else {
    Write-Host ""
    Write-Host "── Probe 2 SKIPPED" -ForegroundColor Yellow
    Write-Host "   This container has $($plain.Count) permission(s), which is not more than -Top $Top."
    Write-Host "   Re-run against a container with more permissions, or lower -Top."
}

Write-Host ""
Write-Host "══ VERDICT ══" -ForegroundColor White

if ($plain.NextLink) {
    Write-Host "PAGES. The unmodified request returned a nextLink." -ForegroundColor Green
    Write-Host "→ Finding M1 was a LIVE false-assurance defect. Task 024's fix is load-bearing, and"
    Write-Host "  task 047 should assert multi-page enumeration against a real container."
}
elseif ($topProbe -and $topProbe.NextLink) {
    Write-Host "PAGES (server-driven paging is supported)." -ForegroundColor Green
    Write-Host "→ `$top=$Top produced a nextLink, so the service DOES emit them; this container is"
    Write-Host "  simply under the default page size today. M1 is a real latent defect that becomes"
    Write-Host "  live as soon as any container grows. Task 024's fix is load-bearing."
}
elseif ($topProbe) {
    Write-Host "NO EVIDENCE OF PAGING." -ForegroundColor Yellow
    Write-Host "→ Even a partial page produced no nextLink. That is consistent with the docs"
    Write-Host "  (`$skip`/`$top` documented, `$skiptoken` not) and means M1 is NOT live today."
    Write-Host "  Task 024's fix remains correct as OData-protocol compliance — a service may begin"
    Write-Host "  server-driven paging at any time and that is not a breaking change — but it should"
    Write-Host "  be re-ranked as defensive, and task 047 need not assert live multi-page behaviour."
}
else {
    Write-Host "INCONCLUSIVE — probe 2 did not run." -ForegroundColor Yellow
    Write-Host "→ Re-run against a container holding more than -Top permissions."
}

Write-Host ""
Write-Host "Record the answer in projects/unified-access-control-r2/notes/task-024-spe-paging-parity.md §1"
