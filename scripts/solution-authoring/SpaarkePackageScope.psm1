<#
.SYNOPSIS
    The Spaarke package scope rule (T218c): what belongs in SpaarkeMaster, decided by rule — never by which
    solution a component happens to sit in.

.DESCRIPTION
    Rule (docs/procedures/SPAARKE-SOLUTION-RELEASE-PROCESS.md §1; ADR-027 §3 amended 2026-10-07):
      every UNMANAGED component of these types in the authoring environment whose name starts with the
      publisher prefix (sprk_):
        Entity (not N:N intersect tables — they ship with their relationship), global OptionSet, WebResource
        (incl. every code page), CustomControl (PCF), AppModule, SiteMap, EnvironmentVariableDefinition;
      + sprk_ custom columns on non-sprk (OOB) tables;
      + root-business-unit security roles whose name matches the scope's role pattern, plus the named extras;
      − the committed exclusions in docs/data-model/package-scope.json (each with a reason and a date).
    Environment-variable VALUES are never in scope (H7 writes them per customer).

    Why a rule: components are created in dev by many scripts and projects, mostly outside any Spaarke solution;
    a membership-based scope ships only what someone remembered to add. the first rule run against spaarkedev1 (2026-10-07) found 44 in-scope components missing from SpaarkeMaster:
    11 PCF controls (incl. RecordHeader), the AI Setup app and site map, sprk_assignedaccess, the noaccessentry/assignedaccess/secure-child scripts, the Console User and Ontology roles, the contact identity columns.

    Discovery queries come from the retired Build-SpaarkeMaster.ps1 (live-validated 2026-04-06). Every function
    takes the Dataverse GET as a scriptblock, so tests run without a network.
#>

Set-StrictMode -Version Latest

$script:TypeCodes = [ordered]@{
    Entity                        = 1
    Attribute                     = 2
    OptionSet                     = 9
    Role                          = 20
    WebResource                   = 61
    SiteMap                       = 62
    CustomControl                 = 66
    AppModule                     = 80
    EnvironmentVariableDefinition = 380
}

function Get-PackageComponentTypeCode {
    param([Parameter(Mandatory)][string]$TypeName)
    if (-not $script:TypeCodes.Contains($TypeName)) {
        throw "Unknown package component type '$TypeName'. Known: $($script:TypeCodes.Keys -join ', ')."
    }
    return [int]$script:TypeCodes[$TypeName]
}

function Read-PackageScope {
    <#
    .SYNOPSIS Loads and validates docs/data-model/package-scope.json. Throws on any exclusion without a reason or date.
    #>
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path $Path)) { throw "Package scope file not found: $Path" }
    $scope = Get-Content $Path -Raw | ConvertFrom-Json

    foreach ($required in 'prefix', 'roleNamePattern', 'roleNamesAlsoIncluded', 'exclusions') {
        if (-not ($scope.PSObject.Properties.Name -contains $required)) {
            throw "Package scope file $Path is missing '$required'."
        }
    }
    $i = 0
    foreach ($e in @($scope.exclusions)) {
        $i++
        foreach ($field in 'type', 'name', 'reason', 'date') {
            $value = if ($e.PSObject.Properties.Name -contains $field) { $e.$field } else { $null }
            if ([string]::IsNullOrWhiteSpace([string]$value)) {
                throw "Package scope exclusion #$i ('$($e.name)') has no '$field' — every exclusion needs type, name, reason and date."
            }
        }
        [void](Get-PackageComponentTypeCode -TypeName $e.type)
        if ($e.date -notmatch '^\d{4}-\d{2}-\d{2}$') {
            throw "Package scope exclusion '$($e.name)' has date '$($e.date)'; use yyyy-MM-dd."
        }
    }
    return $scope
}

function Get-AllPages {
    # Follows @odata.nextLink. $Get takes a relative endpoint OR an absolute nextLink URL.
    param([Parameter(Mandatory)][scriptblock]$Get, [Parameter(Mandatory)][string]$Endpoint)
    $items = [System.Collections.Generic.List[object]]::new()
    $page = & $Get $Endpoint
    while ($true) {
        foreach ($v in @($page.value)) { $items.Add($v) }
        $next = if ($page.PSObject.Properties.Name -contains '@odata.nextLink') { $page.'@odata.nextLink' } else { $null }
        if (-not $next) { break }
        $page = & $Get $next
    }
    return $items.ToArray()   # enumerated: an empty result is nothing, not one empty element
}

function New-ScopeItem([string]$TypeName, [string]$ObjectId, [string]$Name, [string]$ParentEntityId = $null) {
    [PSCustomObject]@{
        TypeName       = $TypeName
        ComponentType  = Get-PackageComponentTypeCode -TypeName $TypeName
        ObjectId       = ([string]$ObjectId).ToLowerInvariant()
        Name           = $Name
        # Attributes of OOB tables: the table's MetadataId — Assemble adds the table metadata-only first.
        ParentEntityId = if ($ParentEntityId) { $ParentEntityId.ToLowerInvariant() } else { $null }
        Excluded       = $false
        Reason         = $null
    }
}

function Get-PackageRuleComponents {
    <#
    .SYNOPSIS Enumerates the rule's components in the authoring environment, marking committed exclusions.
    .PARAMETER Get  Scriptblock { param($endpoint) ... } returning the parsed JSON of a Dataverse Web API GET
                    (relative to /api/data/v9.2/, or an absolute @odata.nextLink).
    #>
    param(
        [Parameter(Mandatory)][scriptblock]$Get,
        [Parameter(Mandatory)]$Scope
    )
    $prefix = [string]$Scope.prefix
    $items = [System.Collections.Generic.List[object]]::new()
    $filterPrefix = "startswith({0},'$prefix') and ismanaged eq false"

    # Tables + their sprk_ columns (custom tables whole; OOB tables only their sprk_ columns).
    $entities = @(Get-AllPages $Get "EntityDefinitions?`$select=LogicalName,MetadataId,IsManaged,IsIntersect&`$expand=Attributes(`$select=LogicalName,MetadataId,IsCustomAttribute,IsManaged,AttributeOf)")
    foreach ($e in $entities) {
        if ($e.LogicalName.StartsWith($prefix)) {
            if (-not $e.IsManaged -and -not $e.IsIntersect) { $items.Add((New-ScopeItem Entity $e.MetadataId $e.LogicalName)) }
            continue
        }
        foreach ($a in @($e.Attributes)) {
            if ($a.IsCustomAttribute -and -not $a.IsManaged -and $a.LogicalName.StartsWith($prefix) -and -not $a.AttributeOf) {
                $items.Add((New-ScopeItem Attribute $a.MetadataId "$($e.LogicalName).$($a.LogicalName)" $e.MetadataId))
            }
        }
    }

    foreach ($os in @(Get-AllPages $Get "GlobalOptionSetDefinitions?`$select=Name,MetadataId,IsManaged")) {
        if ($os.Name.StartsWith($prefix) -and -not $os.IsManaged) { $items.Add((New-ScopeItem OptionSet $os.MetadataId $os.Name)) }
    }
    foreach ($w in @(Get-AllPages $Get ("webresourceset?`$select=webresourceid,name&`$filter=" + ($filterPrefix -f 'name')))) {
        $items.Add((New-ScopeItem WebResource $w.webresourceid $w.name))
    }
    foreach ($c in @(Get-AllPages $Get ("customcontrols?`$select=customcontrolid,name&`$filter=" + ($filterPrefix -f 'name')))) {
        $items.Add((New-ScopeItem CustomControl $c.customcontrolid $c.name))
    }
    foreach ($m in @(Get-AllPages $Get ("appmodules?`$select=appmoduleid,uniquename&`$filter=" + ($filterPrefix -f 'uniquename')))) {
        $items.Add((New-ScopeItem AppModule $m.appmoduleid $m.uniquename))
    }
    foreach ($s in @(Get-AllPages $Get ("sitemaps?`$select=sitemapid,sitemapnameunique&`$filter=" + ($filterPrefix -f 'sitemapnameunique')))) {
        $items.Add((New-ScopeItem SiteMap $s.sitemapid $s.sitemapnameunique))
    }
    foreach ($v in @(Get-AllPages $Get ("environmentvariabledefinitions?`$select=environmentvariabledefinitionid,schemaname&`$filter=" + ($filterPrefix -f 'schemaname')))) {
        $items.Add((New-ScopeItem EnvironmentVariableDefinition $v.environmentvariabledefinitionid $v.schemaname))
    }

    # Roles: root business unit only (a role's copies in child units share its root id).
    $rootBu = @(Get-AllPages $Get "businessunits?`$select=businessunitid&`$filter=_parentbusinessunitid_value eq null")[0]
    $alsoIncluded = @($Scope.roleNamesAlsoIncluded)
    foreach ($r in @(Get-AllPages $Get "roles?`$select=roleid,name&`$filter=_businessunitid_value eq $($rootBu.businessunitid) and ismanaged eq false")) {
        if ($r.name -match $Scope.roleNamePattern -or $alsoIncluded -contains $r.name) {
            $items.Add((New-ScopeItem Role $r.roleid $r.name))
        }
    }

    # Committed exclusions (type + exact name).
    foreach ($item in $items) {
        $hit = @($Scope.exclusions) | Where-Object { $_.type -eq $item.TypeName -and $_.name -eq $item.Name } | Select-Object -First 1
        if ($hit) { $item.Excluded = $true; $item.Reason = $hit.reason }
    }
    return $items.ToArray()
}

function Get-SolutionMembershipKeys {
    <# .SYNOPSIS "componenttype|objectid" keys of a solution's components (lowercase ids). #>
    param([Parameter(Mandatory)][scriptblock]$Get, [Parameter(Mandatory)][string]$SolutionUniqueName)
    $sol = @(Get-AllPages $Get "solutions?`$select=solutionid&`$filter=uniquename eq '$SolutionUniqueName'")
    if ($sol.Count -eq 0) { throw "Solution '$SolutionUniqueName' not found." }
    $keys = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($c in @(Get-AllPages $Get "solutioncomponents?`$select=componenttype,objectid&`$filter=_solutionid_value eq $($sol[0].solutionid)")) {
        [void]$keys.Add("$($c.componenttype)|$(([string]$c.objectid).ToLowerInvariant())")
    }
    return , $keys
}

function Compare-PackageScope {
    <#
    .SYNOPSIS Two-way comparison of the rule against a solution's membership.
    .OUTPUTS MissingFromPackage (in scope, not excluded, not in the solution) · ExcludedButInPackage ·
             UnmatchedExclusions (exclusions that match nothing in the environment — stale entries).
    #>
    param([Parameter(Mandatory)]$RuleComponents, [Parameter(Mandatory)]$MembershipKeys, [Parameter(Mandatory)]$Scope)
    $missing = @($RuleComponents | Where-Object { -not $_.Excluded -and -not $MembershipKeys.Contains("$($_.ComponentType)|$($_.ObjectId)") })
    $excludedIn = @($RuleComponents | Where-Object { $_.Excluded -and $MembershipKeys.Contains("$($_.ComponentType)|$($_.ObjectId)") })
    $unmatched = @(@($Scope.exclusions) | Where-Object {
            $e = $_
            -not (@($RuleComponents) | Where-Object { $_.TypeName -eq $e.type -and $_.Name -eq $e.name })
        })
    [PSCustomObject]@{
        MissingFromPackage   = $missing
        ExcludedButInPackage = $excludedIn
        UnmatchedExclusions  = $unmatched
    }
}

function Find-EnvironmentVariableValues {
    <#
    .SYNOPSIS Env-var VALUES must never ship (H7 writes them per customer). Returns every offending path:
              environmentvariablevalues.json files under a folder, or such entries inside a solution zip.
    #>
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path $Path)) { return }
    if ((Get-Item $Path).PSIsContainer) {
        return Get-ChildItem -Path $Path -Recurse -File -Filter 'environmentvariablevalues.json' | ForEach-Object FullName
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path $Path).Path)
    try {
        return $zip.Entries | Where-Object { $_.Name -ieq 'environmentvariablevalues.json' } | ForEach-Object FullName
    } finally {
        $zip.Dispose()
    }
}

Export-ModuleMember -Function Get-PackageComponentTypeCode, Read-PackageScope, Get-PackageRuleComponents, `
    Get-SolutionMembershipKeys, Compare-PackageScope, Find-EnvironmentVariableValues
