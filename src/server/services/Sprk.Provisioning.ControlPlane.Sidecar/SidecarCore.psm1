<#
.SYNOPSIS
    Logic of the H14a Exchange sidecar (task 251): settings, request checks, the Exchange
    connection, and the RBAC-for-Applications apply / read operations. Listener.ps1 is the HTTP
    loop around these functions; Sidecar.Tests.ps1 (Pester) covers the settings and connection
    rules.

.DESCRIPTION
    WHAT H14a DOES (owner D26, 2026-10-04 -- replaces ApplicationAccessPolicy, which Microsoft
    calls legacy): grant ONE app -- the customer stamp's managed identity -- the Exchange
    "Application Mail.*" roles, scoped to the customer's mail-enabled security group, so the
    stamp's Graph mail calls reach that group's mailboxes and no others. Exchange role
    assignments ADD to Entra app permissions, which is why H10 no longer grants the Entra
    mailbox roles (an Entra grant would make the scope meaningless).

    The apply is get-before-set (T4): every expected assignment is inspected first. If any
    named assignment exists with a different role / app / scope, or the app holds one of these
    roles under another name, the result is Drift and NOTHING is created. Otherwise the missing
    assignments are created and all of them are read back.

    WHO SIGNS IN (owner D24): the sidecar holds no credential. The Worker obtains an Exchange
    Online token for the 'Spaarke Exchange Admin' app (federated credential trusting the
    Worker's managed identity) and sends it with each request in X-Exchange-Access-Token; the
    sidecar runs Connect-ExchangeOnline -AccessToken. The token is never logged.
#>

Set-StrictMode -Version Latest

$script:SettingNames = @{
    SharedSecret = 'SIDECAR_SHARED_SECRET'
    ListenPrefix = 'SIDECAR_LISTEN_PREFIX'
}
$script:DefaultListenPrefix = 'http://127.0.0.1:8091/'
$script:TokenHeaderName     = 'X-Exchange-Access-Token'
$script:AuthHeaderName      = 'X-Sidecar-Auth'

function Get-SidecarSettings {
    <#
    .SYNOPSIS
        Reads the sidecar's settings. Never throws: a missing required setting is reported in
        .Missing so the listener can still bind its port and answer every request with a named
        diagnostic -- a misconfigured sidecar must not hold the whole Worker site down (G30).
    #>
    param([System.Collections.IDictionary]$Environment)
    $secret = [string]$Environment[$script:SettingNames.SharedSecret]
    $prefix = [string]$Environment[$script:SettingNames.ListenPrefix]
    $missing = @()
    if ([string]::IsNullOrWhiteSpace($secret)) { $missing += $script:SettingNames.SharedSecret }
    # App Service passes an unresolvable Key Vault reference through as its literal text.
    elseif ($secret.StartsWith('@Microsoft.KeyVault(', [StringComparison]::OrdinalIgnoreCase)) {
        $missing += "$($script:SettingNames.SharedSecret) (Key Vault reference did not resolve)"
        $secret = ''
    }
    [pscustomobject]@{
        SharedSecret = $secret
        ListenPrefix = if ([string]::IsNullOrWhiteSpace($prefix)) { $script:DefaultListenPrefix } else { $prefix }
        Missing      = $missing
    }
}

function Test-SecretEqual {
    <# Constant-time comparison of the X-Sidecar-Auth header with the shared secret. #>
    param([string]$Provided, [string]$Expected)
    if ([string]::IsNullOrEmpty($Provided) -or [string]::IsNullOrEmpty($Expected)) { return $false }
    if ($Provided.Length -ne $Expected.Length) { return $false }
    $result = 0
    for ($i = 0; $i -lt $Expected.Length; $i++) { $result = $result -bor ([int][char]$Provided[$i] -bxor [int][char]$Expected[$i]) }
    return ($result -eq 0)
}

function Get-ExchangeConnectParameters {
    <#
    .SYNOPSIS
        The ONE way the sidecar signs in to Exchange: the caller's access token. Returns the
        Connect-ExchangeOnline splat, or throws when no token was sent (there is no certificate,
        secret or managed-identity fallback by design -- owner D24).
    #>
    param([string]$AccessToken, [string]$Organization)
    if ([string]::IsNullOrWhiteSpace($AccessToken)) {
        throw "No Exchange access token: the Worker must send $($script:TokenHeaderName) (the sidecar holds no credential)."
    }
    if ([string]::IsNullOrWhiteSpace($Organization)) { throw 'organization (the tenant) is required to connect to Exchange Online.' }
    @{ AccessToken = $AccessToken; Organization = $Organization; ShowBanner = $false; SkipLoadingFormatData = $true }
}

function Test-ApplyRequest {
    <# Returns a list of validation errors for a POST /apply-mailbox-access body (empty = valid). #>
    param($Body)
    $errors = @()
    foreach ($f in 'tenantId', 'appId', 'servicePrincipalObjectId', 'scopeGroupId', 'correlationId') {
        if (-not ($Body.PSObject.Properties[$f] -and -not [string]::IsNullOrWhiteSpace([string]$Body.$f))) { $errors += "$f is required" }
    }
    $assignments = if ($Body.PSObject.Properties['assignments']) { @($Body.assignments) } else { @() }
    if ($assignments.Count -eq 0) { $errors += 'assignments must list at least one {name, role}' }
    foreach ($a in $assignments) {
        if (-not ($a.PSObject.Properties['name'] -and $a.name) -or -not ($a.PSObject.Properties['role'] -and $a.role)) {
            $errors += 'every assignment needs name and role'; break
        }
        if ([string]$a.name -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$') { $errors += "assignment name '$($a.name)' is not a valid Exchange name (64 chars max)" }
    }
    return , $errors
}

function Get-PropertyText($Object, [string]$Name) {
    if ($null -ne $Object -and $Object.PSObject.Properties[$Name]) { return [string]$Object.$Name }
    return ''
}

function Test-AssigneeIs {
    <# True when a management role assignment belongs to the given Exchange service principal. #>
    param($Assignment, $ServicePrincipal)
    $assignee = @((Get-PropertyText $Assignment 'RoleAssignee'), (Get-PropertyText $Assignment 'RoleAssigneeName')) | Where-Object { $_ }
    $ids = @('Identity', 'Name', 'DisplayName', 'ObjectId', 'AppId') | ForEach-Object { Get-PropertyText $ServicePrincipal $_ } | Where-Object { $_ }
    foreach ($x in $assignee) { foreach ($y in $ids) { if ([string]::Equals($x, $y, [StringComparison]::OrdinalIgnoreCase)) { return $true } } }
    return $false
}

function Test-AssignmentInScope {
    <# True when an assignment is limited to exactly the customer's scope group. #>
    param($Assignment, $Group)
    $scope = Get-PropertyText $Assignment 'RecipientGroupScope'
    if (-not $scope) { return $false }
    $ids = @('Identity', 'Name', 'DistinguishedName', 'Guid', 'ExternalDirectoryObjectId') | ForEach-Object { Get-PropertyText $Group $_ } | Where-Object { $_ }
    foreach ($y in $ids) { if ([string]::Equals($scope, $y, [StringComparison]::OrdinalIgnoreCase)) { return $true } }
    return $false
}

function Resolve-ScopeGroup {
    <# The customer's mail-enabled security group, by its Entra object id (or any Exchange identity). #>
    param([string]$GroupId)
    $g = @(Get-Recipient -Filter "ExternalDirectoryObjectId -eq '$GroupId'" -ErrorAction SilentlyContinue)
    if ($g.Count -eq 0) { $g = @(Get-Group -Identity $GroupId -ErrorAction SilentlyContinue) }
    if ($g.Count -ne 1) { return $null }
    $group = Get-Group -Identity ([string]$g[0].DistinguishedName) -ErrorAction Stop
    if ((Get-PropertyText $group 'RecipientTypeDetails') -ne 'MailUniversalSecurityGroup') {
        throw "Scope group '$GroupId' is a $(Get-PropertyText $group 'RecipientTypeDetails'), not a mail-enabled security group."
    }
    return $group
}

function Get-AppRoleAssignments {
    <# Every assignment of the given roles held by the service principal. #>
    param($ServicePrincipal, [string[]]$Roles)
    $all = @()
    foreach ($r in ($Roles | Sort-Object -Unique)) {
        $all += @(Get-ManagementRoleAssignment -Role $r -ErrorAction Stop | Where-Object { Test-AssigneeIs $_ $ServicePrincipal })
    }
    return , $all
}

function ConvertTo-AssignmentView($Assignment, $Group) {
    [ordered]@{
        name            = Get-PropertyText $Assignment 'Name'
        role            = Get-PropertyText $Assignment 'Role'
        scope           = Get-PropertyText $Assignment 'RecipientGroupScope'
        inExpectedScope = [bool]($Group -and (Test-AssignmentInScope $Assignment $Group))
    }
}

function Invoke-MailboxAccessApply {
    <#
    .SYNOPSIS
        Get-before-set apply of the group-scoped Exchange application roles for one app.
        Returns @{ outcome = Success|AlreadyCompliant|Drift|Failure; createdCount; assignments;
        conflicts; diagnostic }. Requires an open Exchange Online session.
    #>
    param($Request)
    $assignments = @($Request.assignments)
    $roles = @($assignments | ForEach-Object { [string]$_.role })
    $group = Resolve-ScopeGroup ([string]$Request.scopeGroupId)
    if (-not $group) {
        return @{ outcome = 'Failure'; createdCount = 0; assignments = @(); conflicts = @()
                  diagnostic = "Scope group '$($Request.scopeGroupId)' was not found in Exchange Online (it must be a mail-enabled security group)." }
    }

    $sp = Get-ServicePrincipal -Identity ([string]$Request.appId) -ErrorAction SilentlyContinue
    $spCreated = $false
    if (-not $sp) {
        $sp = New-ServicePrincipal -AppId ([string]$Request.appId) -ObjectId ([string]$Request.servicePrincipalObjectId) `
                                   -DisplayName ([string]$(if ($Request.PSObject.Properties['displayName'] -and $Request.displayName) { $Request.displayName } else { $Request.appId })) -ErrorAction Stop
        $spCreated = $true
    }
    elseif ((Get-PropertyText $sp 'ObjectId') -and -not [string]::Equals((Get-PropertyText $sp 'ObjectId'), [string]$Request.servicePrincipalObjectId, [StringComparison]::OrdinalIgnoreCase)) {
        return @{ outcome = 'Drift'; createdCount = 0; assignments = @()
                  conflicts = @("Exchange service principal for app $($Request.appId) points at object $(Get-PropertyText $sp 'ObjectId'), expected $($Request.servicePrincipalObjectId).")
                  diagnostic = 'The Exchange service principal does not match the Entra service principal; nothing was changed.' }
    }

    # Pass 1 -- inspect everything; decide before changing anything (T4: no silent overwrite).
    $held = Get-AppRoleAssignments $sp $roles
    $conflicts = @(); $missing = @()
    foreach ($a in $assignments) {
        $named = Get-ManagementRoleAssignment -Identity ([string]$a.name) -ErrorAction SilentlyContinue
        if ($named) {
            if ((Get-PropertyText $named 'Role') -ne [string]$a.role) { $conflicts += "Assignment '$($a.name)' has role '$(Get-PropertyText $named 'Role')', expected '$($a.role)'." }
            elseif (-not (Test-AssigneeIs $named $sp)) { $conflicts += "Assignment '$($a.name)' belongs to '$(Get-PropertyText $named 'RoleAssignee')', not app $($Request.appId)." }
            elseif (-not (Test-AssignmentInScope $named $group)) { $conflicts += "Assignment '$($a.name)' is scoped to '$(Get-PropertyText $named 'RecipientGroupScope')', expected group '$($group.Name)'." }
        }
        else { $missing += $a }
    }
    $expectedNames = @($assignments | ForEach-Object { [string]$_.name })
    foreach ($h in $held) {
        if ($expectedNames -notcontains (Get-PropertyText $h 'Name')) {
            $conflicts += "App $($Request.appId) also holds '$(Get-PropertyText $h 'Role')' through assignment '$(Get-PropertyText $h 'Name')' (scope '$(Get-PropertyText $h 'RecipientGroupScope')')."
        }
    }
    if ($conflicts.Count -gt 0) {
        return @{ outcome = 'Drift'; createdCount = 0; assignments = @($held | ForEach-Object { ConvertTo-AssignmentView $_ $group }); conflicts = $conflicts
                  diagnostic = 'Existing Exchange role assignments differ from the expected set; nothing was created or changed.' }
    }

    # Pass 2 -- create the missing ones.
    foreach ($a in $missing) {
        New-ManagementRoleAssignment -Name ([string]$a.name) -App ([string]$Request.appId) -Role ([string]$a.role) -RecipientGroupScope ([string]$group.Identity) -ErrorAction Stop | Out-Null
    }

    # Pass 3 -- read back.
    $after = Get-AppRoleAssignments $sp $roles
    $views = @($after | ForEach-Object { ConvertTo-AssignmentView $_ $group })
    $bad = @($assignments | Where-Object { $n = [string]$_.name; -not ($views | Where-Object { $_.name -eq $n -and $_.inExpectedScope }) })
    if ($bad.Count -gt 0) {
        return @{ outcome = 'Failure'; createdCount = $missing.Count; assignments = $views; conflicts = @()
                  diagnostic = "Read-back did not show: $(($bad | ForEach-Object { $_.name }) -join ', ')." }
    }
    return @{ outcome = $(if ($missing.Count -gt 0) { 'Success' } else { 'AlreadyCompliant' }); createdCount = $missing.Count
              assignments = $views; conflicts = @()
              diagnostic = "Group-scoped Exchange roles in place for app $($Request.appId) ($($assignments.Count) assignment(s), $($missing.Count) created$(if ($spCreated) { ', service principal registered' }))." }
}

function Get-MailboxAccessState {
    <#
    .SYNOPSIS
        READ-ONLY (H13 T4): the app's assignments of the given roles, each marked with whether it
        is limited to the expected group. Never creates, changes or removes anything.
    #>
    param([string]$AppId, [string]$ScopeGroupId, [string[]]$Roles)
    $sp = Get-ServicePrincipal -Identity $AppId -ErrorAction SilentlyContinue
    if (-not $sp) { return @{ outcome = 'Success'; servicePrincipalRegistered = $false; assignments = @(); diagnostic = "App $AppId is not registered in Exchange Online." } }
    $group = if ($ScopeGroupId) { Resolve-ScopeGroup $ScopeGroupId } else { $null }
    $views = @(Get-AppRoleAssignments $sp $Roles | ForEach-Object { ConvertTo-AssignmentView $_ $group })
    return @{ outcome = 'Success'; servicePrincipalRegistered = $true; assignments = $views
              diagnostic = "App $AppId holds $($views.Count) assignment(s) of the requested roles$(if (-not $group) { '; scope group not found' })." }
}

Export-ModuleMember -Function Get-SidecarSettings, Test-SecretEqual, Get-ExchangeConnectParameters, Test-ApplyRequest,
    Test-AssigneeIs, Test-AssignmentInScope, Invoke-MailboxAccessApply, Get-MailboxAccessState
