#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Creates (idempotently) the "Spaarke Operator Tools (Demo)" public client: the one app registration an operator
    script signs in with to call a DEMO stamp's BFF as a user. One per Spaarke tenant, never per customer.

.DESCRIPTION
    Demo environment plan decision 1 (customer-provisioning-orchestration-r1, notes/demo-environment-plan.md on that
    project's branch), approved by the owner 2026-11-26. Operator scripts such as the demo loader (W3 document files) need a USER token
    for a stamp BFF; the Azure CLI client is not pre-authorized on stamp BFF apps (AADSTS65001).

    This script only creates the CLIENT. Which stamps accept it is decided by provisioning: H3 pre-authorizes the ids
    in the Worker setting EntraAppRegOptions:DemoPreAuthorizedClientAppIds on a customer BFF app ONLY when the
    customer's registry row (sprk_dataverseenvironment) is Environment Type = Demo and active. On every other stamp H3's
    exact-list rule leaves it out (and removes it if a row stops being Demo).

    What it creates or corrects, in the Spaarke tenant:
      1. The application: single tenant (AzureADMyOrg), public client for the interactive browser flow only
         (public-client redirect exactly http://localhost; isFallbackPublicClient = false, so neither device code nor
         username/password sign-in can be used with it). No secret, no certificate, no federated credential, no web or
         SPA redirect, no API permissions of its own: the stamp BFF's user_impersonation scope is reached by
         pre-authorization.
      2. Its service principal with appRoleAssignmentRequired = true, so only principals assigned to it can sign in.
      3. Assignments (default access) for each -AssignUserUpn: the operators, and later the demo personas.

    It adopts an existing app of that name ONLY when it is provably this client: exactly one app has the name, the
    signed-in operator is its only owner (an ownerless app is adopted only if it carries this script's notes marker),
    its service principal has no other owner, it holds no secret, certificate or federated credential, and it has no
    web or SPA redirect. So a re-run (for example to assign demo personas) is done by the operator who created it, or
    after the platform admin makes the re-running operator its owner. Anything else is refused with exit 1 and nothing is changed; the script never deletes
    credentials or owners. (Same rule as H3's adoption of a customer BFF app: an app anyone else owns could gain a
    credential or redirect after this script finishes.)

    The operator's own az sign-in is used (NFR-11); a service principal is refused. Honours -WhatIf.

.PARAMETER AssignUserUpn
    UPNs as they appear in the Spaarke tenant (a guest's is the #EXT# form) to assign to the client.
    Default: the signed-in operator (resolved with /me, so a guest operator works too).

.PARAMETER DisplayName
    Application display name. Default 'Spaarke Operator Tools (Demo)'. The app is found by this name.

.EXAMPLE
    ./scripts/provisioning/Register-OperatorToolsClient.ps1 -WhatIf
    # Reads only: shows what would be created or corrected.

.EXAMPLE
    ./scripts/provisioning/Register-OperatorToolsClient.ps1
    # Creates or corrects the client, assigns the operator, prints the application (client) id for the Worker setting.
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Medium')]
param(
    [string[]]$AssignUserUpn,
    [string]$DisplayName = 'Spaarke Operator Tools (Demo)'
)

$ErrorActionPreference = 'Stop'
$SpaarkeTenantId = 'a221a95e-6abc-4434-aecc-e48338a1b2f2'
$GraphBase = 'https://graph.microsoft.com/v1.0'
$DefaultAccessRoleId = '00000000-0000-0000-0000-000000000000'
$LocalhostRedirect = 'http://localhost'

$account = az account show --query "{tenantId:tenantId, type:user.type}" -o json | ConvertFrom-Json
if ($account.tenantId -ne $SpaarkeTenantId) { throw "Signed in to tenant $($account.tenantId), not the Spaarke tenant $SpaarkeTenantId. Run az login --tenant $SpaarkeTenantId." }
if ($account.type -ne 'user') { throw "Signed in as a $($account.type). This script runs under the operator's own identity (NFR-11)." }
$token = az account get-access-token --resource https://graph.microsoft.com --query accessToken -o tsv
if (-not $token) { throw 'Could not get a Microsoft Graph token for the signed-in operator.' }

# Named so that no module alias can shadow it (Microsoft.Graph.Authentication exports the alias Invoke-GraphRequest).
function Invoke-OperatorToolsGraph {
    param([string]$Method = 'GET', [string]$Path, $Body)
    # No ConsistencyLevel=eventual: these reads must see an app created seconds earlier (no duplicate on a re-run).
    $p = @{ Method = $Method; Uri = "$GraphBase/$Path"; Headers = @{ Authorization = "Bearer $token" } }
    if ($null -ne $Body) { $p.Body = ($Body | ConvertTo-Json -Depth 8); $p.ContentType = 'application/json' }
    Microsoft.PowerShell.Utility\Invoke-RestMethod @p
}

# Reads every page of a Graph collection.
function Get-OperatorToolsGraphCollection {
    param([string]$Path)
    $items = @()
    $page = Invoke-OperatorToolsGraph -Path $Path
    while ($true) {
        $items += @($page.value)
        $next = $page.'@odata.nextLink'
        if (-not $next) { break }
        $page = Invoke-OperatorToolsGraph -Path $next.Substring($GraphBase.Length + 1)
    }
    $items
}

$me = Invoke-OperatorToolsGraph -Path 'me?$select=id,userPrincipalName'
$changes = 0
$NotesMarker = 'Created by scripts/provisioning/Register-OperatorToolsClient.ps1.'
$escapedName = $DisplayName.Replace("'", "''")

# ---- 1. Application ---------------------------------------------------------------------------------------------
$apps = @(Get-OperatorToolsGraphCollection -Path "applications?`$filter=displayName eq '$escapedName'&`$select=id,appId,displayName,signInAudience,isFallbackPublicClient,publicClient,web,spa,notes,passwordCredentials,keyCredentials,requiredResourceAccess")
if ($apps.Count -gt 1) { throw "$($apps.Count) applications are named '$DisplayName'. Resolve by hand; nothing was changed." }
$app = $apps | Select-Object -First 1
$desired = @{
    signInAudience         = 'AzureADMyOrg'
    isFallbackPublicClient = $false
    publicClient           = @{ redirectUris = @($LocalhostRedirect) }
}
if (-not $app) {
    Write-Host "[app] '$DisplayName' does not exist."
    if ($PSCmdlet.ShouldProcess($DisplayName, 'Create public client application')) {
        $body = $desired.Clone()
        $body.displayName = $DisplayName
        $body.notes = 'Operator scripts sign in with this client to call DEMO stamp BFFs as a user. Pre-authorized by provisioning H3 only on stamps whose registry row is Demo. No secrets, certificates, federated credentials or web/SPA redirects. ' + $NotesMarker
        # A delegated create makes the signed-in operator the app's only owner.
        $app = Invoke-OperatorToolsGraph -Method POST -Path 'applications' -Body $body
        Write-Host "[app] created appId $($app.appId) (owner: $($me.userPrincipalName))"
        $changes++
    }
}
else {
    Write-Host "[app] found appId $($app.appId)"

    # Adoption guard: refuse anything that is not provably this client. Nothing is changed on refusal.
    $refusals = @()
    $owners = @(Get-OperatorToolsGraphCollection -Path "applications/$($app.id)/owners?`$select=id")
    $otherOwners = @($owners | Where-Object { $_.id -ne $me.id })
    if ($otherOwners.Count -gt 0) { $refusals += "$($otherOwners.Count) owner(s) other than the signed-in operator" }
    if ($owners.Count -eq 0 -and -not ("$($app.notes)".Contains($NotesMarker))) { $refusals += 'no owner and no marker showing this script created it' }
    $existingSp = @(Get-OperatorToolsGraphCollection -Path "servicePrincipals?`$filter=appId eq '$($app.appId)'&`$select=id,passwordCredentials,keyCredentials") | Select-Object -First 1
    if ($existingSp) {
        $spOwners = @(Get-OperatorToolsGraphCollection -Path "servicePrincipals/$($existingSp.id)/owners?`$select=id" | Where-Object { $_.id -ne $me.id })
        if ($spOwners.Count -gt 0) { $refusals += "$($spOwners.Count) service-principal owner(s) other than the signed-in operator" }
        $spCreds = @(@($existingSp.passwordCredentials) + @($existingSp.keyCredentials) | Where-Object { $_ })
        if ($spCreds.Count -gt 0) { $refusals += "$($spCreds.Count) service-principal secret/certificate credential(s)" }
    }
    $creds = @(@($app.passwordCredentials) + @($app.keyCredentials) | Where-Object { $_ })
    if ($creds.Count -gt 0) { $refusals += "$($creds.Count) secret/certificate credential(s)" }
    $fics = @(Get-OperatorToolsGraphCollection -Path "applications/$($app.id)/federatedIdentityCredentials")
    if ($fics.Count -gt 0) { $refusals += "$($fics.Count) federated identity credential(s)" }
    if (@($app.web.redirectUris | Where-Object { $_ }).Count -gt 0) { $refusals += 'web redirect URI(s)' }
    if (@($app.spa.redirectUris | Where-Object { $_ }).Count -gt 0) { $refusals += 'SPA redirect URI(s)' }
    if ($refusals) {
        Write-Host "[app] REFUSED: the existing '$DisplayName' has $($refusals -join '; '). This client must have none of these. Check who changed it and correct it by hand (this script never deletes credentials, owners or redirects); nothing was changed."
        exit 1
    }

    $drift = @()
    if ($app.signInAudience -ne 'AzureADMyOrg') { $drift += "signInAudience=$($app.signInAudience)" }
    if ($app.isFallbackPublicClient -ne $false) { $drift += 'isFallbackPublicClient not false (device code / password sign-in allowed)' }
    $publicRedirects = @($app.publicClient.redirectUris)
    if ($publicRedirects.Count -ne 1 -or $publicRedirects[0] -ne $LocalhostRedirect) { $drift += "public-client redirects are [$($publicRedirects -join ', ')], not exactly $LocalhostRedirect" }
    if ($drift) {
        Write-Host "[app] drift: $($drift -join '; ')"
        if ($PSCmdlet.ShouldProcess($app.appId, 'Set audience and public-client settings exactly')) {
            Invoke-OperatorToolsGraph -Method PATCH -Path "applications/$($app.id)" -Body $desired | Out-Null
            $changes++
        }
    }
    if (@($app.requiredResourceAccess).Count -gt 0) {
        Write-Warning "[app] declares API permissions; it needs none (stamp BFF access comes from pre-authorization). Left unchanged."
    }
}
if (-not $app) {
    Write-Host "WhatIf: would then create the service principal (assignment required) and assign: $(if ($AssignUserUpn) { $AssignUserUpn -join ', ' } else { $me.userPrincipalName })"
    return
}

# ---- 2. Service principal, assignment required ------------------------------------------------------------------
$sp = @(Get-OperatorToolsGraphCollection -Path "servicePrincipals?`$filter=appId eq '$($app.appId)'&`$select=id,appId,appRoleAssignmentRequired") | Select-Object -First 1
if (-not $sp) {
    if ($PSCmdlet.ShouldProcess($app.appId, 'Create service principal (assignment required)')) {
        # A just-created application can take a few seconds to replicate: Graph answers 400/404 until it has.
        for ($attempt = 1; ; $attempt++) {
            try {
                $sp = Invoke-OperatorToolsGraph -Method POST -Path 'servicePrincipals' -Body @{ appId = $app.appId; appRoleAssignmentRequired = $true }
                break
            }
            catch {
                $status = [int]$_.Exception.Response.StatusCode
                if ($attempt -ge 6 -or ($status -ne 400 -and $status -ne 404)) { throw }
                Write-Host "[sp] application not replicated yet (HTTP $status); retry $attempt of 5 in 10 s"
                Start-Sleep -Seconds 10
            }
        }
        Write-Host "[sp] created $($sp.id)"
        $changes++
    }
}
elseif ($sp.appRoleAssignmentRequired -ne $true) {
    if ($PSCmdlet.ShouldProcess($sp.id, 'Set appRoleAssignmentRequired = true')) {
        Invoke-OperatorToolsGraph -Method PATCH -Path "servicePrincipals/$($sp.id)" -Body @{ appRoleAssignmentRequired = $true } | Out-Null
        Write-Host '[sp] appRoleAssignmentRequired set to true'
        $changes++
    }
}
else { Write-Host "[sp] found $($sp.id), assignment required" }

# ---- 3. Assignments ---------------------------------------------------------------------------------------------
$assignees = if ($AssignUserUpn) {
    foreach ($upn in $AssignUserUpn) { Invoke-OperatorToolsGraph -Path "users/$([uri]::EscapeDataString($upn))?`$select=id,userPrincipalName" }
}
else { @($me) }
if ($sp) {
    $assignments = @(Get-OperatorToolsGraphCollection -Path "servicePrincipals/$($sp.id)/appRoleAssignedTo?`$select=principalId,principalDisplayName,principalType")
    $assigned = @($assignments | ForEach-Object principalId)
    Write-Host "[assign] currently assigned ($($assignments.Count)): $(($assignments | ForEach-Object { "$($_.principalDisplayName) [$($_.principalType)]" }) -join ', ')"
    foreach ($user in $assignees) {
        if ($assigned -contains $user.id) { Write-Host "[assign] $($user.userPrincipalName) already assigned"; continue }
        if ($PSCmdlet.ShouldProcess($user.userPrincipalName, 'Assign to the operator tools client')) {
            Invoke-OperatorToolsGraph -Method POST -Path "servicePrincipals/$($sp.id)/appRoleAssignedTo" -Body @{ principalId = $user.id; resourceId = $sp.id; appRoleId = $DefaultAccessRoleId } | Out-Null
            Write-Host "[assign] $($user.userPrincipalName) assigned"
            $changes++
        }
    }
}
else { Write-Host "WhatIf: would assign: $(($assignees | ForEach-Object userPrincipalName) -join ', ')" }

Write-Host ''
Write-Host "Application (client) id: $($app.appId)"
Write-Host "Worker setting: EntraAppRegOptions__DemoPreAuthorizedClientAppIds__0 = $($app.appId) (Bicep parameter demoPreAuthorizedClientAppIds)"
Write-Host "Changes: $changes$(if ($WhatIfPreference) { ' (WhatIf: none made)' })"
