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

# ---------------------------------------------------------------------------------------------------------------------
# Task 263 (H14m) — the customer's Spaarke-tenant shared mailbox (owner decision 2026-10-10, #1562).
# One shared mailbox per customer, named sprk-{customerId}-mail, a DIRECT member of the customer's scope group
# (Spaarke-AppAccess-{customerId}) so H14a's group-scoped Mail.* roles reach it. Get-before-set: every fact is read
# before any write, and anything foreign is Drift with NOTHING created. Writes need PRQ-E-16 (New-Mailbox -Shared +
# Add-DistributionGroupMember -BypassSecurityGroupManagerCheck); reads need only what PRQ-E-15 already grants
# (Get-Recipient, Test-ServicePrincipalAuthorization). Design: projects/customer-provisioning-orchestration-r1/notes/
# t263-customer-shared-mailbox.md.
# ---------------------------------------------------------------------------------------------------------------------
$script:MailboxNamePattern    = '^sprk-(?<cid>[a-z][a-z0-9]{2,7})-mail$'
$script:ScopeGroupNamePattern = '^Spaarke-AppAccess-(?<cid>[a-z][a-z0-9]{2,7})$'
# Join retry for a freshly created mailbox (replication); 6 x 10 s stays well inside the Worker's 6-minute call timeout.
$script:JoinAttempts = 6
$script:JoinRetryDelaySeconds = 10

function Test-CustomerMailboxRequest {
    <# Validation errors for POST /ensure-customer-mailbox and /read-customer-mailbox (same body; empty = valid). #>
    param($Body)
    $errors = @()
    foreach ($f in 'tenantId', 'organization', 'appId', 'scopeGroupId', 'expectedScopeGroupName', 'name', 'displayName', 'primarySmtpAddress', 'correlationId') {
        if (-not ($Body.PSObject.Properties[$f] -and -not [string]::IsNullOrWhiteSpace([string]$Body.$f))) { $errors += "$f is required" }
    }
    $errors += Test-OrganizationShape $Body
    $text = { param($n) if ($Body.PSObject.Properties[$n]) { [string]$Body.$n } else { '' } }
    if ((& $text 'appId') -and (& $text 'appId') -notmatch $script:GuidPattern) { $errors += 'appId must be a GUID' }
    if ((& $text 'scopeGroupId') -and -not (Test-ScopeGroupIdShape (& $text 'scopeGroupId'))) { $errors += 'scopeGroupId must be the group''s Entra object id (GUID) or email address' }
    $name = & $text 'name'
    $groupName = & $text 'expectedScopeGroupName'
    $nameMatch = [regex]::Match($name, $script:MailboxNamePattern)
    $groupMatch = [regex]::Match($groupName, $script:ScopeGroupNamePattern)
    if ($name -and -not $nameMatch.Success) { $errors += "name '$name' must be sprk-{customerId}-mail" }
    if ($groupName -and -not $groupMatch.Success) { $errors += "expectedScopeGroupName '$groupName' must be Spaarke-AppAccess-{customerId}" }
    # Both names must name the SAME customer — the mailbox may only ever join its own customer's group.
    if ($nameMatch.Success -and $groupMatch.Success -and $nameMatch.Groups['cid'].Value -cne $groupMatch.Groups['cid'].Value) {
        $errors += 'name and expectedScopeGroupName name different customers'
    }
    $address = & $text 'primarySmtpAddress'
    if ($address -and $address -notmatch $script:EmailPattern) { $errors += 'primarySmtpAddress must be an email address' }
    $display = & $text 'displayName'
    if ($display.Length -gt 256 -or $display -match '[\x00-\x1F\x7F]') { $errors += 'displayName must be 1-256 characters without control characters' }
    # @(...) around the whole if: an if statement unrolls a one-element array to a scalar (no .Count under StrictMode).
    $roles = @(if ($Body.PSObject.Properties['roles']) { $Body.roles })
    if ($roles.Count -eq 0) { $errors += 'roles must list at least one role' }
    foreach ($r in $roles) { if ($script:AllowedRoles -notcontains [string]$r) { $errors += "role '$r' is not one the sidecar knows ($($script:AllowedRoles -join ', '))" } }
    return , $errors
}

function ConvertTo-FilterLiteral([string]$Value) { return $Value.Replace("'", "''") }

function Get-CustomerMailboxFacts {
    <#
    .SYNOPSIS
        READ-ONLY: everything H14m decides on. Returns @{ group; mailbox; conflicts; whenCreated }. Never writes.
        Conflicts are the Drift reasons (each would make the ensure create or change nothing):
          - the scope group's Name is not the expected Spaarke-AppAccess-{customerId};
          - more than one recipient carries the mailbox's name, alias or address;
          - the recipient found is not a SharedMailbox, or its name / alias / address differ (a foreign mailbox,
            e.g. another customer's or a person's at that address);
          - the mailbox is not a member of the scope group, or is a member of any OTHER group.
        A missing scope group is returned as group = $null (the caller reports Failure, not Drift).
    #>
    param($Request)
    $group = Resolve-ScopeGroup ([string]$Request.scopeGroupId)
    if (-not $group) { return @{ group = $null; mailbox = $null; conflicts = @(); whenCreated = '' } }
    $conflicts = @()
    $groupName = Get-PropertyText $group 'Name'
    if (-not [string]::Equals($groupName, [string]$Request.expectedScopeGroupName, [StringComparison]::OrdinalIgnoreCase)) {
        $conflicts += "Scope group '$($Request.scopeGroupId)' is named '$groupName', expected '$($Request.expectedScopeGroupName)' — it is not this customer's group (a mailbox joining it would be reachable by another stamp)."
    }
    $name = [string]$Request.name
    $address = [string]$Request.primarySmtpAddress
    $filter = "Alias -eq '$(ConvertTo-FilterLiteral $name)' -or Name -eq '$(ConvertTo-FilterLiteral $name)' -or EmailAddresses -eq 'smtp:$(ConvertTo-FilterLiteral $address)'"
    $found = @(Get-Recipient -Filter $filter -ErrorAction Stop)
    if ($found.Count -gt 1) {
        $conflicts += "$($found.Count) recipients carry name/alias '$name' or address '$address': $(($found | ForEach-Object { "$(Get-PropertyText $_ 'Name') ($(Get-PropertyText $_ 'RecipientTypeDetails'))" }) -join ', ')."
        return @{ group = $group; mailbox = $null; conflicts = $conflicts; whenCreated = '' }
    }
    if ($found.Count -eq 0) { return @{ group = $group; mailbox = $null; conflicts = $conflicts; whenCreated = '' } }

    $m = $found[0]
    $whenCreated = Get-PropertyText $m 'WhenCreatedUTC'
    $type = Get-PropertyText $m 'RecipientTypeDetails'
    if ($type -ne 'SharedMailbox') { $conflicts += "Recipient '$(Get-PropertyText $m 'Name')' is a $type, not a SharedMailbox." }
    foreach ($pair in @(@('Name', $name), @('Alias', $name), @('PrimarySmtpAddress', $address))) {
        $actual = Get-PropertyText $m $pair[0]
        if (-not [string]::Equals($actual, $pair[1], [StringComparison]::OrdinalIgnoreCase)) {
            $conflicts += "Recipient '$(Get-PropertyText $m 'Name')' has $($pair[0]) '$actual', expected '$($pair[1])' — not this customer's mailbox."
        }
    }
    # In the scope group: MemberOfGroup is a documented recipient filter property (DN; direct membership).
    $dn = Get-PropertyText $m 'DistinguishedName'
    $groupDn = Get-PropertyText $group 'DistinguishedName'
    $inScope = @(Get-Recipient -Filter "MemberOfGroup -eq '$(ConvertTo-FilterLiteral $groupDn)' -and Alias -eq '$(ConvertTo-FilterLiteral (Get-PropertyText $m 'Alias'))'" -ErrorAction Stop).Count -gt 0
    # Every group the mailbox is a DIRECT member of — for the "no other group" rule (live check M5 in the design note).
    $groups = @(Get-Recipient -Filter "Members -eq '$(ConvertTo-FilterLiteral $dn)'" -ErrorAction Stop)
    if (-not $inScope) {
        $conflicts += "Mailbox '$name' exists (created $whenCreated UTC) but is not a member of the scope group '$groupName'. If this run created it and stopped before joining it, add it to the group, then clear the quarantine."
    }
    $others = @($groups | Where-Object { -not [string]::Equals((Get-PropertyText $_ 'DistinguishedName'), $groupDn, [StringComparison]::OrdinalIgnoreCase) })
    if ($others.Count -gt 0) {
        $conflicts += "Mailbox '$name' is also a member of: $(($others | ForEach-Object { Get-PropertyText $_ 'Name' }) -join ', ') — another stamp may reach it."
    }
    return @{ group = $group; mailbox = $m; conflicts = $conflicts; whenCreated = $whenCreated }
}

function Get-CustomerMailboxAuthorization {
    <#
    .SYNOPSIS
        READ-ONLY: Exchange's own answer to "do the stamp identity's roles reach this mailbox?" — one entry per expected
        role, inScope true only when Test-ServicePrincipalAuthorization reports that role InScope. Evaluates the
        configuration, not the 30 min - 2 h cache that Graph calls go through.
    #>
    param([string]$AppId, [string]$Mailbox, [string[]]$Roles)
    $rows = @(Test-ServicePrincipalAuthorization -Identity $AppId -Resource $Mailbox -ErrorAction Stop)
    $result = @()
    foreach ($role in $Roles) {
        $hit = @($rows | Where-Object { (Get-PropertyText $_ 'RoleName') -eq $role -and [string](Get-PropertyText $_ 'InScope') -eq 'True' })
        $result += [ordered]@{ role = $role; inScope = ($hit.Count -gt 0) }
    }
    return , $result
}

function Test-CustomerMailboxWritePermission {
    <#
    .SYNOPSIS
        Exchange Online loads only the cmdlets and parameters the caller's roles allow. Returns the missing ones (empty =
        both writes are possible), so an ensure without PRQ-E-16 fails BEFORE creating a mailbox it could not join.
    #>
    $missing = @()
    $create = Get-Command New-Mailbox -ErrorAction SilentlyContinue
    if (-not $create -or -not $create.Parameters.ContainsKey('Shared')) { $missing += 'New-Mailbox -Shared' }
    $join = Get-Command Add-DistributionGroupMember -ErrorAction SilentlyContinue
    if (-not $join -or -not $join.Parameters.ContainsKey('BypassSecurityGroupManagerCheck')) { $missing += 'Add-DistributionGroupMember -BypassSecurityGroupManagerCheck' }
    return , $missing
}

function Invoke-CustomerMailboxEnsure {
    <#
    .SYNOPSIS
        Get-before-set ensure of the customer's shared mailbox. Returns @{ outcome = Success|AlreadyCompliant|Drift|Failure;
        created; verified; authorization; conflicts; diagnostic }. Requires an open Exchange Online session.
        Drift, and a Failure before the first write, leave nothing created. After the writes, a failed read-back is
        Failure with created = $true (the next run judges what exists); a crash between the two writes leaves a mailbox
        outside the group, which the next run reports as Drift (design note §8, K2).
    #>
    param($Request)
    $roles = @($Request.roles | ForEach-Object { [string]$_ })
    $facts = Get-CustomerMailboxFacts $Request
    if (-not $facts.group) {
        return @{ outcome = 'Failure'; created = $false; verified = $false; authorization = @(); conflicts = @()
                  diagnostic = "Scope group '$($Request.scopeGroupId)' was not found in Exchange Online (it must be a mail-enabled security group)." }
    }
    if ($facts.conflicts.Count -gt 0) {
        return @{ outcome = 'Drift'; created = $false; verified = $false; authorization = @(); conflicts = $facts.conflicts
                  diagnostic = 'Existing Exchange configuration differs from the expected mailbox; nothing was created or changed.' }
    }

    $created = $false
    if (-not $facts.mailbox) {
        $missing = @(Test-CustomerMailboxWritePermission)
        if ($missing.Count -gt 0) {
            return @{ outcome = 'Failure'; created = $false; verified = $false; authorization = @(); conflicts = @()
                      diagnostic = "Spaarke Exchange Admin cannot run $($missing -join ' / ') — prerequisite PRQ-E-16 (owner decision D32) is not applied. Nothing was created." }
        }
        $new = New-Mailbox -Shared -Name ([string]$Request.name) -Alias ([string]$Request.name) -DisplayName ([string]$Request.displayName) -PrimarySmtpAddress ([string]$Request.primarySmtpAddress) -ErrorAction Stop
        $created = $true
        # A new mailbox can take a little while to replicate before a group accepts it as a member, so the join is
        # retried inside this request. A mailbox left outside the group is Drift on the next run (design note §8).
        $joined = $false; $lastError = ''
        for ($attempt = 1; $attempt -le $script:JoinAttempts -and -not $joined; $attempt++) {
            try {
                Add-DistributionGroupMember -Identity (Get-PropertyText $facts.group 'DistinguishedName') -Member (Get-PropertyText $new 'DistinguishedName') -BypassSecurityGroupManagerCheck -ErrorAction Stop | Out-Null
                $joined = $true
            }
            catch {
                $lastError = $_.Exception.Message
                if ($attempt -lt $script:JoinAttempts) { Start-Sleep -Seconds $script:JoinRetryDelaySeconds }
            }
        }
        if (-not $joined) {
            return @{ outcome = 'Failure'; created = $true; verified = $false; authorization = @(); conflicts = @()
                      diagnostic = "Created shared mailbox '$($Request.name)' but could not add it to the scope group after $($script:JoinAttempts) attempts: $lastError. The next run will report it as outside the scope group (Drift): add it to the group, then clear the quarantine." }
        }
        # Read back: the same facts must now be conflict-free with the mailbox present.
        $facts = Get-CustomerMailboxFacts $Request
        if (-not $facts.mailbox -or $facts.conflicts.Count -gt 0) {
            return @{ outcome = 'Failure'; created = $true; verified = $false; authorization = @(); conflicts = @($facts.conflicts)
                      diagnostic = "Read-back after creating '$($Request.name)' did not show it, shared and in the scope group only: $(@($facts.conflicts) -join ' ')" }
        }
    }

    $authorization = Get-CustomerMailboxAuthorization -AppId ([string]$Request.appId) -Mailbox ([string]$Request.primarySmtpAddress) -Roles $roles
    $verified = @($authorization | Where-Object { -not $_.inScope }).Count -eq 0
    $state = if ($verified) { 'every role in scope' } else { "not in scope yet: $((@($authorization | Where-Object { -not $_.inScope }) | ForEach-Object { $_.role }) -join ', ')" }
    return @{ outcome = $(if ($created) { 'Success' } else { 'AlreadyCompliant' }); created = $created; verified = $verified
              authorization = $authorization; conflicts = @()
              diagnostic = "Shared mailbox '$($Request.name)' <$($Request.primarySmtpAddress)> $(if ($created) { 'created and added to' } else { 'already in' }) the scope group; authorization for app $($Request.appId): $state." }
}

function Get-CustomerMailboxState {
    <#
    .SYNOPSIS
        READ-ONLY (H13): @{ outcome = Success|Failure; exists; conflicts; authorization; diagnostic }. Never creates,
        changes or removes anything. A missing scope group is Failure (no verdict), not "everything wrong".
    #>
    param($Request)
    $facts = Get-CustomerMailboxFacts $Request
    if (-not $facts.group) {
        return @{ outcome = 'Failure'; exists = $false; conflicts = @(); authorization = @()
                  diagnostic = "Scope group '$($Request.scopeGroupId)' was not found in Exchange Online — cannot judge the mailbox." }
    }
    if (-not $facts.mailbox) {
        return @{ outcome = 'Success'; exists = $false; conflicts = @($facts.conflicts); authorization = @()
                  diagnostic = "No recipient named '$($Request.name)' or addressed '$($Request.primarySmtpAddress)'." }
    }
    $roles = @($Request.roles | ForEach-Object { [string]$_ })
    $authorization = Get-CustomerMailboxAuthorization -AppId ([string]$Request.appId) -Mailbox ([string]$Request.primarySmtpAddress) -Roles $roles
    return @{ outcome = 'Success'; exists = $true; conflicts = @($facts.conflicts); authorization = $authorization
              diagnostic = "Mailbox '$($Request.name)' found; $(@($facts.conflicts).Count) conflict(s)." }
}

Export-ModuleMember -Function Get-SidecarSettings, Test-SecretEqual, Get-ExchangeConnectParameters, Test-ApplyRequest, Test-ReadRequest,
    Test-AssigneeIs, Test-AssignmentInScope, Invoke-MailboxAccessApply, Get-MailboxAccessState,
    Test-CustomerMailboxRequest, Invoke-CustomerMailboxEnsure, Get-CustomerMailboxState
