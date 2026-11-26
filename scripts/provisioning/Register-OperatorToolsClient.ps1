#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Creates (idempotently) the "Spaarke Operator Tools (Demo)" public client: the one app registration an operator
    script signs in with to call a DEMO stamp's BFF as a user. One per Spaarke tenant, never per customer.

.DESCRIPTION
    Demo environment plan (projects/customer-provisioning-orchestration-r1/notes/demo-environment-plan.md) decision 1,
    approved by the owner 2026-11-26. Operator scripts such as the demo loader (W3 document files) need a USER token
    for a stamp BFF; the Azure CLI client is not pre-authorized on stamp BFF apps (AADSTS65001).

    This script only creates the CLIENT. Which stamps accept it is decided by provisioning: H3 pre-authorizes the ids
    in the Worker setting EntraAppRegOptions:DemoPreAuthorizedClientAppIds on a customer BFF app ONLY when the
    customer's registry row (sprk_dataverseenvironment) is Environment Type = Demo and active. On every other stamp H3's
    exact-list rule leaves it out (and removes it if a row stops being Demo).

    What it creates or corrects, in the Spaarke tenant:
      1. The application: single tenant (AzureADMyOrg), public client (isFallbackPublicClient, redirect
         http://localhost for the interactive flow; device code needs none). No secret, no certificate, no API
         permissions of its own: the stamp BFF's user_impersonation scope is reached by pre-authorization.
         A secret or certificate found on it is REPORTED and the script exits 1 (it never deletes credentials).
      2. Its service principal with appRoleAssignmentRequired = true, so only principals assigned to it can sign in.
      3. Assignments (default access) for each -AssignUserUpn: the operators, and later the demo personas.

    The operator's own az sign-in is used (NFR-11); a service principal is refused. Honours -WhatIf.

.PARAMETER AssignUserUpn
    UPNs (or guest UPNs as they appear in the Spaarke tenant) to assign to the client. Default: the signed-in operator.

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
$Graph = 'https://graph.microsoft.com/v1.0'
$DefaultAccessRoleId = '00000000-0000-0000-0000-000000000000'

$account = az account show --query "{tenantId:tenantId, type:user.type, name:user.name}" -o json | ConvertFrom-Json
if ($account.tenantId -ne $SpaarkeTenantId) { throw "Signed in to tenant $($account.tenantId), not the Spaarke tenant $SpaarkeTenantId. Run az login --tenant $SpaarkeTenantId." }
if ($account.type -ne 'user') { throw "Signed in as a $($account.type). This script runs under the operator's own identity (NFR-11)." }
$token = az account get-access-token --resource https://graph.microsoft.com --query accessToken -o tsv
if (-not $token) { throw 'Could not get a Microsoft Graph token for the signed-in operator.' }

function Invoke-GraphRequest {
    param([string]$Method = 'GET', [string]$Path, $Body)
    $p = @{ Method = $Method; Uri = "$Graph/$Path"; Headers = @{ Authorization = "Bearer $token"; ConsistencyLevel = 'eventual' } }
    if ($null -ne $Body) { $p.Body = ($Body | ConvertTo-Json -Depth 8); $p.ContentType = 'application/json' }
    Invoke-RestMethod @p
}

if (-not $AssignUserUpn) { $AssignUserUpn = @($account.name) }
$changes = 0
$escapedName = $DisplayName.Replace("'", "''")

# ---- 1. Application ---------------------------------------------------------------------------------------------
$apps = @((Invoke-GraphRequest -Path "applications?`$filter=displayName eq '$escapedName'&`$select=id,appId,displayName,signInAudience,isFallbackPublicClient,publicClient,passwordCredentials,keyCredentials,requiredResourceAccess").value)
if ($apps.Count -gt 1) { throw "$($apps.Count) applications are named '$DisplayName'. Resolve by hand; nothing was changed." }
$app = $apps | Select-Object -First 1
$desired = @{
    signInAudience         = 'AzureADMyOrg'
    isFallbackPublicClient = $true
    publicClient           = @{ redirectUris = @('http://localhost') }
}
if (-not $app) {
    Write-Host "[app] '$DisplayName' does not exist."
    if ($PSCmdlet.ShouldProcess($DisplayName, 'Create public client application')) {
        $body = $desired.Clone()
        $body.displayName = $DisplayName
        $body.notes = 'Operator scripts sign in with this client to call DEMO stamp BFFs as a user. Pre-authorized by provisioning H3 only on stamps whose registry row is Demo. No secrets, no certificates. Created by scripts/provisioning/Register-OperatorToolsClient.ps1.'
        $app = Invoke-GraphRequest -Method POST -Path 'applications' -Body $body
        Write-Host "[app] created appId $($app.appId)"
        $changes++
    }
}
else {
    Write-Host "[app] found appId $($app.appId)"
    $drift = @()
    if ($app.signInAudience -ne 'AzureADMyOrg') { $drift += "signInAudience=$($app.signInAudience)" }
    if ($app.isFallbackPublicClient -ne $true) { $drift += 'isFallbackPublicClient not true' }
    if (@($app.publicClient.redirectUris) -notcontains 'http://localhost') { $drift += 'redirect http://localhost missing' }
    if ($drift) {
        Write-Host "[app] drift: $($drift -join '; ')"
        if ($PSCmdlet.ShouldProcess($app.appId, 'Correct audience and public-client settings')) {
            Invoke-GraphRequest -Method PATCH -Path "applications/$($app.id)" -Body $desired | Out-Null
            $changes++
        }
    }
    $creds = @($app.passwordCredentials) + @($app.keyCredentials) | Where-Object { $_ }
    if ($creds.Count -gt 0) {
        Write-Error "[app] holds $($creds.Count) secret/certificate credential(s). This client must have none. Remove them by hand after checking who added them; this script never deletes credentials."
        exit 1
    }
    if (@($app.requiredResourceAccess).Count -gt 0) {
        Write-Warning "[app] declares API permissions; it needs none (stamp BFF access comes from pre-authorization). Left unchanged."
    }
}
if (-not $app) { Write-Host 'WhatIf: nothing else to check before the app exists.'; return }

# ---- 2. Service principal, assignment required ------------------------------------------------------------------
$sp = @((Invoke-GraphRequest -Path "servicePrincipals?`$filter=appId eq '$($app.appId)'&`$select=id,appId,appRoleAssignmentRequired").value) | Select-Object -First 1
if (-not $sp) {
    if ($PSCmdlet.ShouldProcess($app.appId, 'Create service principal (assignment required)')) {
        $sp = Invoke-GraphRequest -Method POST -Path 'servicePrincipals' -Body @{ appId = $app.appId; appRoleAssignmentRequired = $true }
        Write-Host "[sp] created $($sp.id)"
        $changes++
    }
}
elseif ($sp.appRoleAssignmentRequired -ne $true) {
    if ($PSCmdlet.ShouldProcess($sp.id, 'Set appRoleAssignmentRequired = true')) {
        Invoke-GraphRequest -Method PATCH -Path "servicePrincipals/$($sp.id)" -Body @{ appRoleAssignmentRequired = $true } | Out-Null
        Write-Host '[sp] appRoleAssignmentRequired set to true'
        $changes++
    }
}
else { Write-Host "[sp] found $($sp.id), assignment required" }

# ---- 3. Assignments ---------------------------------------------------------------------------------------------
if ($sp) {
    $assigned = @((Invoke-GraphRequest -Path "servicePrincipals/$($sp.id)/appRoleAssignedTo?`$select=principalId").value | ForEach-Object principalId)
    foreach ($upn in $AssignUserUpn) {
        $user = Invoke-GraphRequest -Path "users/$([uri]::EscapeDataString($upn))?`$select=id,userPrincipalName"
        if ($assigned -contains $user.id) { Write-Host "[assign] $upn already assigned"; continue }
        if ($PSCmdlet.ShouldProcess($upn, 'Assign to the operator tools client')) {
            Invoke-GraphRequest -Method POST -Path "servicePrincipals/$($sp.id)/appRoleAssignedTo" -Body @{ principalId = $user.id; resourceId = $sp.id; appRoleId = $DefaultAccessRoleId } | Out-Null
            Write-Host "[assign] $upn assigned"
            $changes++
        }
    }
}

Write-Host ''
Write-Host "Application (client) id: $($app.appId)"
Write-Host "Worker setting: EntraAppRegOptions__DemoPreAuthorizedClientAppIds__0 = $($app.appId) (Bicep parameter demoPreAuthorizedClientAppIds)"
Write-Host "Changes: $changes$(if ($WhatIfPreference) { ' (WhatIf: none made)' })"
