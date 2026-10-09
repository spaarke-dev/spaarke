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
      + sprk_ custom columns on non-sprk (OOB) tables, unmanaged system views / forms on OOB tables, and unmanaged
        dashboards;
      + security roles of the ROOT business unit whose name matches the scope's role pattern, plus the named extras;
      + every unmanaged field security profile (they secure sprk_ columns; no other publisher authors in dev);
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
    SavedQuery                    = 26
    SystemForm                    = 60
    FieldSecurityProfile          = 70
}

# Types the "packaged but outside the rule" check covers (membership of these types must be explained by the rule).
$script:AccountedTypes = @(1, 9, 20, 26, 60, 61, 62, 66, 70, 80, 380, 381)

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
        # Columns, views and forms of OOB tables: the table's MetadataId — Assemble adds the table as a shell first,
        # and the outside-the-rule check treats that table row as explained.
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

    # Views and forms on OOB tables (those on sprk_ tables ship with their table); unmanaged dashboards
    # (systemforms with objecttypecode 'none') are in scope too. A view/form carries its OOB table as ParentEntityId so
    # the table row Dataverse adds with it is explained, not reported as outside the rule.
    $tableIds = @{}
    foreach ($e in $entities) { $tableIds[$e.LogicalName] = $e.MetadataId }
    foreach ($q in @(Get-AllPages $Get "savedqueries?`$select=savedqueryid,name,returnedtypecode&`$filter=ismanaged eq false")) {
        $table = [string]$q.returnedtypecode
        if ($table -and -not $table.StartsWith($prefix)) {
            $items.Add((New-ScopeItem SavedQuery $q.savedqueryid "${table}: $($q.name)" $tableIds[$table]))
        }
    }
    foreach ($f in @(Get-AllPages $Get "systemforms?`$select=formid,name,objecttypecode&`$filter=ismanaged eq false")) {
        $table = [string]$f.objecttypecode
        if (-not $table -or $table.StartsWith($prefix)) { continue }
        if ($table -eq 'none') { $items.Add((New-ScopeItem SystemForm $f.formid "dashboard: $($f.name)")); continue }
        $items.Add((New-ScopeItem SystemForm $f.formid "${table}: $($f.name)" $tableIds[$table]))
    }

    foreach ($p in @(Get-AllPages $Get "fieldsecurityprofiles?`$select=fieldsecurityprofileid,name&`$filter=ismanaged eq false")) {
        $items.Add((New-ScopeItem FieldSecurityProfile $p.fieldsecurityprofileid $p.name))
    }

    # Roles: those of the ROOT business unit only (copies in child units share the root's id and are skipped).
    # Dataverse packages no role authored in a child unit ("root component Role is missing", T218e 2026-10-08):
    # such a role is per-environment by design - e.g. Secure Record Owner, contained in the Secure Record unit
    # (SECURE-PROJECT-ENVIRONMENT-SETUP.md 5.2), which provisioning H7b creates (T256).
    $alsoIncluded = @($Scope.roleNamesAlsoIncluded)
    $rootUnits = @(Get-AllPages $Get "businessunits?`$select=businessunitid&`$filter=_parentbusinessunitid_value eq null")
    if ($rootUnits.Count -ne 1) { throw "Expected exactly one root business unit; found $($rootUnits.Count)." }
    foreach ($r in @(Get-AllPages $Get "roles?`$select=roleid,name,_parentrootroleid_value&`$filter=ismanaged eq false and _businessunitid_value eq $($rootUnits[0].businessunitid)")) {
        if ([string]$r.roleid -ne [string]$r._parentrootroleid_value) { continue }
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
    <#
    .SYNOPSIS "componenttype|objectid" → rootcomponentbehavior (0 = with all subcomponents, 1 = no subcomponents,
              2 = shell) for every component of a solution (lowercase ids).
    #>
    param([Parameter(Mandatory)][scriptblock]$Get, [Parameter(Mandatory)][string]$SolutionUniqueName)
    $sol = @(Get-AllPages $Get "solutions?`$select=solutionid&`$filter=uniquename eq '$SolutionUniqueName'")
    if ($sol.Count -eq 0) { throw "Solution '$SolutionUniqueName' not found." }
    $keys = [System.Collections.Generic.Dictionary[string, int]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($c in @(Get-AllPages $Get "solutioncomponents?`$select=componenttype,objectid,rootcomponentbehavior&`$filter=_solutionid_value eq $($sol[0].solutionid)")) {
        $behavior = if ($null -ne $c.rootcomponentbehavior) { [int]$c.rootcomponentbehavior } else { 0 }
        $keys["$($c.componenttype)|$(([string]$c.objectid).ToLowerInvariant())"] = $behavior
    }
    return , $keys
}

function Compare-PackageScope {
    <#
    .SYNOPSIS Two-way comparison of the rule against a solution's membership (from Get-SolutionMembershipKeys).
    .OUTPUTS
      MissingFromPackage   — in scope, not excluded, not in the solution.
      PackagedAsShell      — an in-scope sprk_ table packaged without its subcomponents (behavior ≠ 0): it would
                             ship without its columns, forms and views.
      ExcludedButInPackage — excluded, yet packaged.
      OutsideRule          — packaged components of the accounted types the rule does not explain (e.g. Microsoft
                             tables dragged in as dependencies, env-var VALUES); OOB parent tables of in-scope
                             columns / views / forms are explained.
      UnmatchedExclusions  — exclusions that match nothing (stale).
      UnmatchedAlsoIncluded— roleNamesAlsoIncluded names that match no root role.
    #>
    param(
        [Parameter(Mandatory)]$RuleComponents,
        [Parameter(Mandatory)]$MembershipKeys,
        [Parameter(Mandatory)]$Scope,
        [string[]]$ExplainedEntityIds = @()
    )
    $rule = @($RuleComponents)
    $key = { param($c) "$($c.ComponentType)|$($c.ObjectId)" }
    $missing = @($rule | Where-Object { -not $_.Excluded -and -not $MembershipKeys.ContainsKey((& $key $_)) })
    $shell = @($rule | Where-Object {
            -not $_.Excluded -and $_.TypeName -eq 'Entity' -and $MembershipKeys.ContainsKey((& $key $_)) -and
            $MembershipKeys[(& $key $_)] -ne 0
        })
    $excludedIn = @($rule | Where-Object { $_.Excluded -and $MembershipKeys.ContainsKey((& $key $_)) })

    $explained = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($c in $rule) { [void]$explained.Add((& $key $c)); if ($c.ParentEntityId) { [void]$explained.Add("1|$($c.ParentEntityId)") } }
    foreach ($id in $ExplainedEntityIds) { [void]$explained.Add("1|$($id.ToLowerInvariant())") }
    $outside = @($MembershipKeys.Keys | Where-Object {
            $t = [int]($_ -split '\|')[0]
            $script:AccountedTypes -contains $t -and -not $explained.Contains($_)
        } | Sort-Object | ForEach-Object {
            $parts = $_ -split '\|'
            [PSCustomObject]@{ ComponentType = [int]$parts[0]; ObjectId = $parts[1]; Behavior = $MembershipKeys[$_] }
        })

    $unmatched = @(@($Scope.exclusions) | Where-Object {
            $e = $_
            -not ($rule | Where-Object { $_.TypeName -eq $e.type -and $_.Name -eq $e.name })
        })
    $unmatchedAlso = @(@($Scope.roleNamesAlsoIncluded) | Where-Object {
            $n = $_
            -not ($rule | Where-Object { $_.TypeName -eq 'Role' -and $_.Name -eq $n })
        })
    [PSCustomObject]@{
        MissingFromPackage    = $missing
        PackagedAsShell       = $shell
        ExcludedButInPackage  = $excludedIn
        OutsideRule           = $outside
        UnmatchedExclusions   = $unmatched
        UnmatchedAlsoIncluded = $unmatchedAlso
    }
}

function Get-EntityNameMap {
    <# .SYNOPSIS MetadataId (lowercase) → LogicalName, for naming OutsideRule tables in reports. #>
    param([Parameter(Mandatory)][scriptblock]$Get)
    $map = @{}
    foreach ($e in @(Get-AllPages $Get "EntityDefinitions?`$select=LogicalName,MetadataId")) { $map[([string]$e.MetadataId).ToLowerInvariant()] = $e.LogicalName }
    return $map
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

function Find-LeakyDependencies {
    <#
    .SYNOPSIS F12 guard: the exported solution's Other/Solution.xml must list NO missing dependency on
              solution="Active" — that is a Spaarke component the package references but does not contain, so the
              managed import fails in a fresh environment (lessons-learned-model1-prod-standup-2026-08-22.md F12).
              Returns one line per leaky dependency.
    #>
    param([Parameter(Mandatory)][string]$SolutionXmlPath)
    [xml]$xml = Get-Content $SolutionXmlPath -Raw
    $leaks = $xml.SelectNodes('//MissingDependency') | Where-Object {
        $r = $_.SelectSingleNode('Required')
        $r -and ([string]$r.GetAttribute('solution')).StartsWith('Active')
    }
    return $leaks | ForEach-Object {
        $r = $_.SelectSingleNode('Required'); $d = $_.SelectSingleNode('Dependent')
        "requires type $($r.GetAttribute('type')) $($r.GetAttribute('schemaName'))$($r.GetAttribute('id')) (solution=Active) <- needed by type $($d.GetAttribute('type')) $($d.GetAttribute('schemaName'))"
    }
}

function Get-NextPackageVersion {
    <#
    .SYNOPSIS The package's next version: an explicit -Version (must be higher than the current one, compared part by
              part with missing parts as 0, like H6's SpaarkePackage.CompareVersions), or the current version bumped
              by -Kind. Always four parts. H6 refuses a downgrade, so a version at or below one already shipped is
              refused here rather than at a customer import.
    #>
    param(
        [Parameter(Mandatory)][string]$Current,
        [ValidateSet('Build', 'Revision', 'Minor', 'Major')][string]$Kind = 'Build',
        [string]$Version
    )
    $parse = {
        param([string]$v)
        $p = @($v -split '\.')
        if ($p.Count -gt 4 -or @($p | Where-Object { $_ -notmatch '^\d+$' }).Count -gt 0) { throw "Not a version: '$v'." }
        @(0..3 | ForEach-Object { if ($_ -lt $p.Count) { [int]$p[$_] } else { 0 } })
    }
    $cur = & $parse $Current
    if ($Version) {
        $next = & $parse $Version
        $cmp = 0
        foreach ($i in 0..3) { if ($next[$i] -ne $cur[$i]) { $cmp = $next[$i] - $cur[$i]; break } }
        if ($cmp -le 0) { throw "Version $Version is not higher than the current $Current." }
    } else {
        $next = switch ($Kind) {
            'Major' { @(($cur[0] + 1), 0, 0, 0) }
            'Minor' { @($cur[0], ($cur[1] + 1), 0, 0) }
            'Build' { @($cur[0], $cur[1], ($cur[2] + 1), 0) }
            'Revision' { @($cur[0], $cur[1], $cur[2], ($cur[3] + 1)) }
        }
    }
    return ($next -join '.')
}

function Get-PackedSolutionInfo {
    <#
    .SYNOPSIS Reads a packed solution zip's solution.xml: unique name, version and whether it is managed (T218d —
              the CI publish checks what it packed before uploading it).
    #>
    param([Parameter(Mandatory)][string]$ZipPath)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path $ZipPath).Path)
    try {
        $entry = $zip.Entries | Where-Object { $_.FullName -ieq 'solution.xml' } | Select-Object -First 1
        if (-not $entry) { throw "No solution.xml in $ZipPath." }
        $reader = [System.IO.StreamReader]::new($entry.Open())
        try { [xml]$xml = $reader.ReadToEnd() } finally { $reader.Dispose() }
    } finally { $zip.Dispose() }
    $manifest = $xml.ImportExportXml.SolutionManifest
    [PSCustomObject]@{
        UniqueName = [string]$manifest.UniqueName
        Version    = [string]$manifest.Version
        Managed    = ([string]$manifest.Managed) -eq '1'
    }
}

function New-SpaarkeMasterManifest {
    <#
    .SYNOPSIS The artifact manifest H6 reads (T218d) — the ONE producer of its shape:
              {"solutions":{"SpaarkeMaster":{"version","managedBlobName","unmanagedBlobName"}}} plus audit fields
              H6 ignores. DataverseWebApiSolutionImporter.ParsePackageEntry is the consumer; both sides are tested
              against src/server/services/Sprk.Provisioning.ControlPlane.Tests/Fixtures/spaarkemaster-manifest.sample.json.
    #>
    param(
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$ManagedBlobName,
        [Parameter(Mandatory)][string]$UnmanagedBlobName,
        [string]$ManagedSha256,
        [string]$UnmanagedSha256,
        [string]$BuildId,
        [string]$SourceSha,
        [string]$PublishedAt = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    )
    $entry = [ordered]@{
        version           = $Version
        managedBlobName   = $ManagedBlobName
        unmanagedBlobName = $UnmanagedBlobName
    }
    if ($ManagedSha256) { $entry.managedSha256 = $ManagedSha256 }
    if ($UnmanagedSha256) { $entry.unmanagedSha256 = $UnmanagedSha256 }
    $doc = [ordered]@{
        buildId     = $BuildId
        sha         = $SourceSha
        publishedAt = $PublishedAt
        solutions   = [ordered]@{ SpaarkeMaster = $entry }
    }
    return ($doc | ConvertTo-Json -Depth 5)
}

function Compare-PackageVersion {
    <#
    .SYNOPSIS -1 / 0 / 1 for a < b / a = b / a > b, part by part with missing parts as 0 (H6's
              SpaarkePackage.CompareVersions). Throws on a version that is not 1-4 dot-separated numbers.
    #>
    param([Parameter(Mandatory)][string]$A, [Parameter(Mandatory)][string]$B)
    $parse = {
        param([string]$v)
        $p = @($v.Trim() -split '\.')
        if ($p.Count -gt 4 -or @($p | Where-Object { $_ -notmatch '^\d+$' }).Count -gt 0) { throw "Not a version: '$v'." }
        @(0..3 | ForEach-Object { if ($_ -lt $p.Count) { [long]$p[$_] } else { 0 } })
    }
    $x = & $parse $A; $y = & $parse $B
    foreach ($i in 0..3) { if ($x[$i] -lt $y[$i]) { return -1 }; if ($x[$i] -gt $y[$i]) { return 1 } }
    return 0
}

function Get-InstalledPackage {
    <#
    .SYNOPSIS The SpaarkeMaster installed in an environment ({Version, Managed}), or $null when absent. A failed read
              throws — "cannot tell" must never be taken for "not installed" (it would skip the type and downgrade
              refusals).
    #>
    param([Parameter(Mandatory)][scriptblock]$Get, [string]$SolutionUniqueName = 'SpaarkeMaster')
    $rows = @((& $Get "solutions?`$select=version,ismanaged&`$filter=uniquename eq '$SolutionUniqueName'").value)
    if ($rows.Count -eq 0) { return $null }
    [PSCustomObject]@{ Version = [string]$rows[0].version; Managed = [bool]$rows[0].ismanaged }
}

function Resolve-PackageImportPlan {
    <#
    .SYNOPSIS What importing SpaarkeMaster {PackageVersion} as managed/unmanaged into an environment that holds
              $Installed would do — the same rules as H6 (DataverseWebApiSolutionImporter, ADR-027 §3-§4):
              never switch the package type, never downgrade, an equal version is already done, an older MANAGED
              package is upgraded with stage-and-upgrade, an older UNMANAGED one is updated by a plain import.
    .OUTPUTS  Action = Install | Upgrade | Update | AlreadyCurrent | Refuse; Refusal = package-type-mismatch |
              downgrade-refused (when Refuse); StageAndUpgrade; Message.
    #>
    param($Installed, [Parameter(Mandatory)][string]$PackageVersion, [Parameter(Mandatory)][bool]$Managed)
    $type = if ($Managed) { 'managed' } else { 'unmanaged' }
    # H6 validates the package version before anything else, so an unreadable one is refused even on a fresh install.
    [void](Compare-PackageVersion -A $PackageVersion -B $PackageVersion)
    $plan = { param($action, $refusal, $stage, $message) [PSCustomObject]@{ Action = $action; Refusal = $refusal; StageAndUpgrade = $stage; Message = $message } }
    if ($null -eq $Installed) { return & $plan 'Install' $null $false "Install SpaarkeMaster $PackageVersion ($type)." }
    $installedType = if ($Installed.Managed) { 'managed' } else { 'unmanaged' }
    if ([bool]$Installed.Managed -ne $Managed) {
        return & $plan 'Refuse' 'package-type-mismatch' $false ("The environment holds SpaarkeMaster $($Installed.Version) as $installedType; " +
            "this import asks for $type. The package type is never switched (ADR-027 §3) — set the environment's solutionPackageType " +
            "to $installedType, or convert the environment first (owner-approved, backed up).")
    }
    $cmp = Compare-PackageVersion -A $Installed.Version -B $PackageVersion
    if ($cmp -gt 0) {
        return & $plan 'Refuse' 'downgrade-refused' $false "The environment holds SpaarkeMaster $($Installed.Version), newer than the published $PackageVersion. Never downgraded."
    }
    if ($cmp -eq 0) { return & $plan 'AlreadyCurrent' $null $false "SpaarkeMaster $PackageVersion ($type) is already installed — nothing to import." }
    if ($Managed) { return & $plan 'Upgrade' $null $true "Upgrade SpaarkeMaster $($Installed.Version) -> $PackageVersion (managed, stage and upgrade)." }
    return & $plan 'Update' $null $false "Update SpaarkeMaster $($Installed.Version) -> $PackageVersion (unmanaged import over the existing components)."
}

Export-ModuleMember -Function Get-PackageComponentTypeCode, Read-PackageScope, Get-PackageRuleComponents, `
    Get-SolutionMembershipKeys, Compare-PackageScope, Get-EntityNameMap, Find-EnvironmentVariableValues, Find-LeakyDependencies, `
    Get-NextPackageVersion, `
    Get-PackedSolutionInfo, New-SpaarkeMasterManifest, Compare-PackageVersion, Get-InstalledPackage, Resolve-PackageImportPlan
