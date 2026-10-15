<#
.SYNOPSIS
    Removes the Microsoft Graph app roles a customer STAMP identity holds beyond the stamp set (task 261 / G31).
    DRY RUN by default; pass -Apply to delete.

.DESCRIPTION
    For a stamp provisioned before task 261, H10 already completed and the reconciler will not re-dispatch it, so
    nothing removes the old tenant-wide Graph roles (Directory.ReadWrite.All, User.ReadWrite.All, Files.*, Sites.*, ...).
    This is the operator path (the in-pipeline path is a re-run of H10: its idempotency key carries a catalog
    fingerprint, so a changed catalog re-runs the reconcile). It does what H10's RemoveUnexpectedRolesAsync does:

      - the target must be the stamp's managed identity: the service principal's appId must equal -UamiClientId and its
        servicePrincipalType must be ManagedIdentity (otherwise nothing is touched);
      - the allowed set is parsed from src/server/api/Sprk.Bff.Api/Infrastructure/Auth/GraphAppRoles.cs, keeping only
        the roles that are NOT Exchange-scoped (FileStorageContainer.Selected) - the mailbox roles are never Entra
        grants on a stamp, so a mailbox role found in Entra IS removed;
      - only assignments on the Microsoft Graph resource are considered; other resources' assignments are untouched;
      - every page of appRoleAssignments is read; a response without a 'value' array stops the script.

    Evidence for the set: projects/customer-provisioning-orchestration-r1/notes/t261-stamp-graph-least-privilege.md.
    The operator needs AppRoleAssignment.ReadWrite.All (Global / Privileged Role / Cloud Application Administrator) and
    signs in with their own identity (az login). Never `az account set`: pass nothing here that changes the context.

.PARAMETER TenantId        Entra tenant id (mandatory; no default, tenant-isolation invariant I1).
.PARAMETER UamiPrincipalId Object id of the stamp managed identity's service principal.
.PARAMETER UamiClientId    Client (application) id of the same identity - must match the principal.
.PARAMETER Apply           Actually delete. Without it the script only lists what it would delete.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string]$TenantId,
    [Parameter(Mandatory = $true)] [string]$UamiPrincipalId,
    [Parameter(Mandatory = $true)] [string]$UamiClientId,
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'
$graph = 'https://graph.microsoft.com/v1.0'
$graphAppId = '00000003-0000-0000-c000-000000000000'
$catalogPath = Join-Path $PSScriptRoot '..\..\src\server\api\Sprk.Bff.Api\Infrastructure\Auth\GraphAppRoles.cs'
$exchangeScoped = @('Mail.Read', 'Mail.ReadWrite', 'Mail.Send', 'MailboxSettings.Read')

function Invoke-Graph {
    param([string]$Method, [string]$Uri)
    $raw = az rest --method $Method.ToLowerInvariant() --uri $Uri --output json 2>&1
    if ($LASTEXITCODE -ne 0) { throw "az rest $Method $Uri failed: $(($raw | Out-String).Trim())" }
    $text = ($raw | Out-String).Trim()
    if (-not $text) { return $null }
    return $text | ConvertFrom-Json
}

if (-not (Test-Path -LiteralPath $catalogPath)) { throw "Catalog not found: $catalogPath (run from the repo checkout)." }
$src = Get-Content -Raw -LiteralPath $catalogPath
$values = @{}
foreach ($m in [regex]::Matches($src, 'public\s+const\s+string\s+(\w+)\s*=\s*"([^"]+)"\s*;')) {
    if ($m.Groups[1].Value -ne 'GraphResourceAppId') { $values[$m.Groups[1].Value] = $m.Groups[2].Value }
}
$ids = @{}
foreach ($m in [regex]::Matches($src, 'private\s+const\s+string\s+(Id\w+)\s*=\s*"([0-9a-fA-F-]{36})"\s*;')) { $ids[$m.Groups[1].Value] = $m.Groups[2].Value }
$allowed = @()
foreach ($m in [regex]::Matches($src, 'new\s+GraphAppRole\s*\(\s*(\w+)\s*,\s*"([^"]+)"\s*,\s*(\w+)')) {
    $v = $values[$m.Groups[1].Value]; $id = $ids[$m.Groups[3].Value]
    if (-not $v -or -not $id) { throw "Parser desync on '$($m.Value)'." }
    if ($exchangeScoped -notcontains $v) { $allowed += $id.ToLowerInvariant() }
}
if ($allowed.Count -eq 0) { throw 'The allowed set parsed empty - refusing to remove anything.' }

$sp = Invoke-Graph GET "$graph/servicePrincipals/$([uri]::EscapeDataString($UamiPrincipalId))?`$select=id,displayName,appId,servicePrincipalType"
if ($sp.appId -ne $UamiClientId -or $sp.servicePrincipalType -ne 'ManagedIdentity') {
    throw "Refusing: SP '$($sp.displayName)' has appId '$($sp.appId)' / type '$($sp.servicePrincipalType)', expected the stamp managed identity (appId '$UamiClientId', ManagedIdentity). Nothing was removed."
}

$g = (Invoke-Graph GET "$graph/servicePrincipals?`$filter=appId eq '$graphAppId'&`$select=id,appRoles").value[0]
$names = @{}; foreach ($r in $g.appRoles) { $names[$r.id.ToLowerInvariant()] = $r.value }

$assignments = @(); $uri = "$graph/servicePrincipals/$([uri]::EscapeDataString($UamiPrincipalId))/appRoleAssignments"; $page = 0
while ($uri) {
    if (++$page -gt 50) { throw 'More than 50 pages of appRoleAssignments - refusing to decide on a partial list.' }
    $doc = Invoke-Graph GET $uri
    if ($null -eq $doc -or -not ($doc.PSObject.Properties.Name -contains 'value')) { throw "A response without a 'value' array is not an empty list - stopping." }
    $assignments += @($doc.value | Where-Object { $_.resourceId -eq $g.id })
    $uri = $doc.'@odata.nextLink'
    if ($uri -and -not $uri.StartsWith('https://graph.microsoft.com/')) { throw "nextLink '$uri' is not on graph.microsoft.com." }
}

$extra = @($assignments | Where-Object { $allowed -notcontains $_.appRoleId.ToLowerInvariant() })
Write-Host "Stamp identity '$($sp.displayName)' holds $($assignments.Count) Microsoft Graph role(s); allowed in Entra: FileStorageContainer.Selected. Extra: $($extra.Count)."
foreach ($e in $extra) {
    $name = $names[$e.appRoleId.ToLowerInvariant()]; if (-not $name) { $name = $e.appRoleId }
    if ($Apply) {
        Invoke-Graph DELETE "$graph/servicePrincipals/$([uri]::EscapeDataString($UamiPrincipalId))/appRoleAssignments/$([uri]::EscapeDataString($e.id))" | Out-Null
        Write-Host "  REMOVED $name ($($e.id))" -ForegroundColor Yellow
    }
    else {
        Write-Host "  [dry run] would remove $name ($($e.id))"
    }
}
if (-not $Apply -and $extra.Count -gt 0) { Write-Host 'Dry run only. Re-run with -Apply to delete.' }
