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

    The apply is get-before-set (T4): everything is inspected first. If a named assignment exists
    with a different role / app / scope, the app holds ANY other "Application *" role, or its
    Exchange service principal points at a different object, the result is Drift and NOTHING is
    created (not even the service principal). Otherwise the service principal (if new) and the
    missing assignments are created and all of them are read back. Groups and service principals
    are matched by unique ids only — on Model 1 every customer's group is in Spaarke's tenant.

    WHO SIGNS IN (owner D24): the sidecar holds no credential. The Worker obtains an Exchange
    Online token for the 'Spaarke Exchange Admin' app (federated credential trusting the
    Worker's managed identity) and sends it with each request in X-Exchange-Access-Token; the
    sidecar runs Connect-ExchangeOnline -AccessToken. The token is never logged.

    ORGANIZATION: every request names the tenant's initial domain (contoso.onmicrosoft.com), the
    only -Organization value Microsoft documents for app-only sign-in. A tenant GUID connects and
    reads, but writes then fail with "doesn't have write permission to target DC" (live, 2026-10-04).
#>

Set-StrictMode -Version Latest

$script:SettingNames = @{
    SharedSecret = 'SIDECAR_SHARED_SECRET'
    ListenPrefix = 'SIDECAR_LISTEN_PREFIX'
}
$script:DefaultListenPrefix = 'http://127.0.0.1:8091/'
# Defence in depth (code review S2): the only roles H14a may grant. The admin app's delegating
# assignments enforce the same set in Exchange; adding a mailbox role means changing both and
# IGraphAppRolesRegistry.ExchangeScopedValues in the Worker.
$script:AllowedRoles = @('Application Mail.Read', 'Application Mail.ReadWrite', 'Application Mail.Send', 'Application MailboxSettings.Read')
$script:GuidPattern  = '^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$'
$script:EmailPattern = '^[^@\s'']+@[^@\s'']+\.[^@\s'']+$'
$script:DomainPattern = '^[A-Za-z0-9]([A-Za-z0-9-]{0,62})(\.[A-Za-z0-9]([A-Za-z0-9-]{0,62}))+$'
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
    if ([string]::IsNullOrWhiteSpace($Organization)) { throw 'organization (the tenant''s initial .onmicrosoft.com domain) is required to connect to Exchange Online.' }
    @{ AccessToken = $AccessToken; Organization = $Organization; ShowBanner = $false; SkipLoadingFormatData = $true }
}

function Test-ApplyRequest {
    <# Returns a list of validation errors for a POST /apply-mailbox-access body (empty = valid). #>
    param($Body)
    $errors = @()
    foreach ($f in 'tenantId', 'organization', 'appId', 'servicePrincipalObjectId', 'scopeGroupId', 'correlationId') {
        if (-not ($Body.PSObject.Properties[$f] -and -not [string]::IsNullOrWhiteSpace([string]$Body.$f))) { $errors += "$f is required" }
    }
    foreach ($f in 'appId', 'servicePrincipalObjectId') {
        if ($Body.PSObject.Properties[$f] -and [string]$Body.$f -and [string]$Body.$f -notmatch $script:GuidPattern) { $errors += "$f must be a GUID" }
    }
    if ($Body.PSObject.Properties['scopeGroupId'] -and [string]$Body.scopeGroupId -and -not (Test-ScopeGroupIdShape ([string]$Body.scopeGroupId))) {
        $errors += 'scopeGroupId must be the group''s Entra object id (GUID) or email address'
    }
    $errors += Test-OrganizationShape $Body
    $assignments = if ($Body.PSObject.Properties['assignments']) { @($Body.assignments) } else { @() }
    if ($assignments.Count -eq 0) { $errors += 'assignments must list at least one {name, role}' }
    foreach ($a in $assignments) {
        if (-not ($a.PSObject.Properties['name'] -and $a.name) -or -not ($a.PSObject.Properties['role'] -and $a.role)) {
            $errors += 'every assignment needs name and role'; break
        }
        if ([string]$a.name -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$') { $errors += "assignment name '$($a.name)' is not a valid Exchange name (64 chars max)" }
        if ($script:AllowedRoles -notcontains [string]$a.role) { $errors += "role '$($a.role)' is not one the sidecar may grant ($($script:AllowedRoles -join ', '))" }
    }
    return , $errors
}

function Test-ReadRequest {
    <# Returns a list of validation errors for a POST /read-mailbox-access body (empty = valid). #>
    param($Body)
    $errors = @()
    foreach ($f in 'tenantId', 'organization', 'appId') {
        if (-not ($Body.PSObject.Properties[$f] -and -not [string]::IsNullOrWhiteSpace([string]$Body.$f))) { $errors += "$f is required" }
    }
    $errors += Test-OrganizationShape $Body
    if ($Body.PSObject.Properties['appId'] -and [string]$Body.appId -and [string]$Body.appId -notmatch $script:GuidPattern) { $errors += 'appId must be a GUID' }
    if (-not ($Body.PSObject.Properties['roles'] -and @($Body.roles).Count -gt 0)) { $errors += 'roles must list at least one role' }
    if (-not ($Body.PSObject.Properties['scopeGroupId'] -and [string]$Body.scopeGroupId)) { $errors += 'scopeGroupId is required' }
    elseif (-not (Test-ScopeGroupIdShape ([string]$Body.scopeGroupId))) { $errors += 'scopeGroupId must be the group''s Entra object id (GUID) or email address' }
    return , $errors
}

function Test-ScopeGroupIdShape([string]$Value) { return ($Value -match $script:GuidPattern -or $Value -match $script:EmailPattern) }

function Test-OrganizationShape($Body) {
    <# The organization must be a domain name, never the tenant GUID (writes fail with a GUID). #>
    $o = if ($Body.PSObject.Properties['organization']) { [string]$Body.organization } else { '' }
    if (-not $o) { return @() }
    if ($o -match $script:GuidPattern -or $o -notmatch $script:DomainPattern) {
        return @('organization must be the tenant''s initial domain (contoso.onmicrosoft.com), not a tenant id')
    }
    return @()
}

function Get-PropertyText($Object, [string]$Name) {
    if ($null -ne $Object -and $Object.PSObject.Properties[$Name]) { return [string]$Object.$Name }
    return ''
}

function Test-AssigneeIs {
    <# True when a management role assignment belongs to the given Exchange service principal. #>
    param($Assignment, $ServicePrincipal)
    $assignee = @((Get-PropertyText $Assignment 'RoleAssignee'), (Get-PropertyText $Assignment 'RoleAssigneeName')) | Where-Object { $_ }
    # Unique ids only (code review S4) — Exchange records the assignee as the service principal's object id.
    $ids = @('ObjectId', 'AppId', 'Identity') | ForEach-Object { Get-PropertyText $ServicePrincipal $_ } | Where-Object { $_ }
    foreach ($x in $assignee) { foreach ($y in $ids) { if ([string]::Equals($x, $y, [StringComparison]::OrdinalIgnoreCase)) { return $true } } }
    return $false
}

function Get-AssignmentScopeText($Assignment) {
    <# How an assignment is scoped, for views and diagnostics: 'Group:<name>', 'CustomRecipientScope:<name>', 'Organization', ... #>
    $kind = Get-PropertyText $Assignment 'RecipientWriteScope'
    $target = Get-PropertyText $Assignment 'CustomResourceScope'
    if ($target) { return "${kind}:$target" }
    return $kind
}

function Test-AssignmentInScope {
    <#
    True when an assignment is limited to exactly the customer's scope group. Exchange records an
    assignment made with -RecipientGroupScope as RecipientWriteScope 'Group' with the group's Name in
    CustomResourceScope — there is no RecipientGroupScope property to read back (live, 2026-10-04).
    A management-scope assignment reads 'CustomRecipientScope', so the two cannot be confused even
    when a scope and a group share a name. The group's Name is unique within the tenant's
    organization (Exchange refuses a duplicate), so on Model 1, where every customer's group lives
    in Spaarke's tenant, it still identifies one group (code review W2).
    #>
    param($Assignment, $Group)
    if ((Get-PropertyText $Assignment 'RecipientWriteScope') -ne 'Group') { return $false }
    $scope = Get-PropertyText $Assignment 'CustomResourceScope'
    $name = Get-PropertyText $Group 'Name'
    return [bool]($scope -and $name -and [string]::Equals($scope, $name, [StringComparison]::OrdinalIgnoreCase))
}

function Resolve-ScopeGroup {
    <# The customer's mail-enabled security group, by its Entra object id (or any Exchange identity). #>
    param([string]$GroupId)
    # Shape-checked by Test-ApplyRequest / Test-ReadRequest (GUID or email) before it reaches the OPATH filter.
    $g = @()
    # Get-Recipient, not Get-Group: Get-Group does not return ExternalDirectoryObjectId (live, 2026-10-04).
    if ($GroupId -match $script:GuidPattern) { $g = @(Get-Recipient -Filter "ExternalDirectoryObjectId -eq '$GroupId'" -ErrorAction SilentlyContinue) }
    if ($g.Count -eq 0) { $g = @(Get-Group -Identity $GroupId -ErrorAction SilentlyContinue) }
    if ($g.Count -ne 1) { return $null }
    $group = Get-Group -Identity ([string]$g[0].DistinguishedName) -ErrorAction Stop
    if ((Get-PropertyText $group 'RecipientTypeDetails') -ne 'MailUniversalSecurityGroup') {
        throw "Scope group '$GroupId' is a $(Get-PropertyText $group 'RecipientTypeDetails'), not a mail-enabled security group."
    }
    return $group
}

function Get-AppRoleAssignments {
    <#
    .SYNOPSIS
        Every "Application *" role assignment the service principal holds — not only the requested
        roles (code review W6: a stray Exchange Full Access or Mail.ReadBasic grant must be seen).
        Queried by assignee (W7: the per-role query grows with every customer in the tenant).
    #>
    param($ServicePrincipal)
    $assignee = Get-PropertyText $ServicePrincipal 'ObjectId'
    if (-not $assignee) { $assignee = Get-PropertyText $ServicePrincipal 'Identity' }
    $all = @(Get-ManagementRoleAssignment -RoleAssignee $assignee -ErrorAction Stop |
        Where-Object { (Get-PropertyText $_ 'Role') -like 'Application *' -and (Test-AssigneeIs $_ $ServicePrincipal) })
    # Plain return (callers wrap in @()): a comma-wrapped array would travel a pipeline as ONE object.
    return $all
}

function ConvertTo-AssignmentView($Assignment, $Group) {
    [ordered]@{
        name            = Get-PropertyText $Assignment 'Name'
        role            = Get-PropertyText $Assignment 'Role'
        scope           = Get-AssignmentScopeText $Assignment
        inExpectedScope = [bool]($Group -and (Test-AssignmentInScope $Assignment $Group))
    }
}

function Invoke-MailboxAccessApply {
    <#
    .SYNOPSIS
        Get-before-set apply of the group-scoped Exchange application roles for one app.
        Returns @{ outcome = Success|AlreadyCompliant|Drift|Failure; createdCount; assignments;
        conflicts; diagnostic }. Requires an open Exchange Online session. Every check runs before
        any change, so Drift and Failure never leave anything created.
    #>
    param($Request)
    $assignments = @($Request.assignments)
    $group = Resolve-ScopeGroup ([string]$Request.scopeGroupId)
    if (-not $group) {
        return @{ outcome = 'Failure'; createdCount = 0; assignments = @(); conflicts = @()
                  diagnostic = "Scope group '$($Request.scopeGroupId)' was not found in Exchange Online (it must be a mail-enabled security group)." }
    }
    $groupScope = Get-PropertyText $group 'DistinguishedName'

    # Pass 1 — inspect everything; decide before changing anything (T4: no silent overwrite).
    $sp = Get-ServicePrincipal -Identity ([string]$Request.appId) -ErrorAction SilentlyContinue
    $conflicts = @()
    if ($sp -and (Get-PropertyText $sp 'ObjectId') -and -not [string]::Equals((Get-PropertyText $sp 'ObjectId'), [string]$Request.servicePrincipalObjectId, [StringComparison]::OrdinalIgnoreCase)) {
        $conflicts += "Exchange service principal for app $($Request.appId) points at object $(Get-PropertyText $sp 'ObjectId'), expected $($Request.servicePrincipalObjectId)."
    }
    $held = if ($sp) { @(Get-AppRoleAssignments $sp) } else { @() }
    $missing = @()
    foreach ($a in $assignments) {
        $named = Get-ManagementRoleAssignment -Identity ([string]$a.name) -ErrorAction SilentlyContinue
        if (-not $named) { $missing += $a; continue }
        if ((Get-PropertyText $named 'Role') -ne [string]$a.role) { $conflicts += "Assignment '$($a.name)' has role '$(Get-PropertyText $named 'Role')', expected '$($a.role)'." }
        elseif (-not $sp -or -not (Test-AssigneeIs $named $sp)) { $conflicts += "Assignment '$($a.name)' belongs to '$(Get-PropertyText $named 'RoleAssignee')', not app $($Request.appId)." }
        elseif (-not (Test-AssignmentInScope $named $group)) { $conflicts += "Assignment '$($a.name)' is scoped to '$(Get-AssignmentScopeText $named)', expected 'Group:$(Get-PropertyText $group 'Name')'." }
    }
    $expectedNames = @($assignments | ForEach-Object { [string]$_.name })
    foreach ($h in $held) {
        if ($expectedNames -notcontains (Get-PropertyText $h 'Name')) {
            $conflicts += "App $($Request.appId) also holds '$(Get-PropertyText $h 'Role')' through assignment '$(Get-PropertyText $h 'Name')' (scope '$(Get-AssignmentScopeText $h)')."
        }
    }
    if ($conflicts.Count -gt 0) {
        return @{ outcome = 'Drift'; createdCount = 0; assignments = @($held | ForEach-Object { ConvertTo-AssignmentView $_ $group }); conflicts = $conflicts
                  diagnostic = 'Existing Exchange configuration differs from the expected set; nothing was created or changed.' }
    }

    # Pass 2 — create what is missing.
    $spCreated = $false
    if (-not $sp) {
        $displayName = if ($Request.PSObject.Properties['displayName'] -and $Request.displayName) { [string]$Request.displayName } else { [string]$Request.appId }
        $sp = New-ServicePrincipal -AppId ([string]$Request.appId) -ObjectId ([string]$Request.servicePrincipalObjectId) -DisplayName $displayName -ErrorAction Stop
        $spCreated = $true
    }
    foreach ($a in $missing) {
        New-ManagementRoleAssignment -Name ([string]$a.name) -App ([string]$Request.appId) -Role ([string]$a.role) -RecipientGroupScope $groupScope -ErrorAction Stop | Out-Null
    }

    # Pass 3 — read back.
    $views = @(Get-AppRoleAssignments $sp | ForEach-Object { ConvertTo-AssignmentView $_ $group })
    $bad = @($assignments | Where-Object { $n = [string]$_.name; -not ($views | Where-Object { $_.name -eq $n -and $_.inExpectedScope }) })
    if ($bad.Count -gt 0) {
        return @{ outcome = 'Failure'; createdCount = $missing.Count; assignments = $views; conflicts = @()
                  diagnostic = "Read-back did not show, in scope: $(($bad | ForEach-Object { $_.name }) -join ', ')." }
    }
    return @{ outcome = $(if ($missing.Count -gt 0) { 'Success' } else { 'AlreadyCompliant' }); createdCount = $missing.Count
              assignments = $views; conflicts = @()
              diagnostic = "Group-scoped Exchange roles in place for app $($Request.appId) ($($assignments.Count) assignment(s), $($missing.Count) created$(if ($spCreated) { ', service principal registered' }))." }
}

function Get-MailboxAccessState {
    <#
    .SYNOPSIS
        READ-ONLY (H13 T4): every "Application *" role assignment the app holds, each marked with
        whether it is limited to the expected group. Never creates, changes or removes anything.
        A scope group that cannot be found is a Failure (not "everything out of scope").
    #>
    param([string]$AppId, [string]$ScopeGroupId)
    $group = Resolve-ScopeGroup $ScopeGroupId
    if (-not $group) {
        return @{ outcome = 'Failure'; servicePrincipalRegistered = $false; assignments = @()
                  diagnostic = "Scope group '$ScopeGroupId' was not found in Exchange Online — cannot judge the scope of the app's roles." }
    }
    $sp = Get-ServicePrincipal -Identity $AppId -ErrorAction SilentlyContinue
    if (-not $sp) { return @{ outcome = 'Success'; servicePrincipalRegistered = $false; assignments = @(); diagnostic = "App $AppId is not registered in Exchange Online." } }
    $views = @(Get-AppRoleAssignments $sp | ForEach-Object { ConvertTo-AssignmentView $_ $group })
    return @{ outcome = 'Success'; servicePrincipalRegistered = $true; assignments = $views
              diagnostic = "App $AppId holds $($views.Count) application role assignment(s)." }
}

Export-ModuleMember -Function Get-SidecarSettings, Test-SecretEqual, Get-ExchangeConnectParameters, Test-ApplyRequest, Test-ReadRequest,
    Test-AssigneeIs, Test-AssignmentInScope, Invoke-MailboxAccessApply, Get-MailboxAccessState
