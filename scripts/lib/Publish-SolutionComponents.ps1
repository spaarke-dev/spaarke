<#
.SYNOPSIS
    The ONE scoped-publish procedure for every Dataverse deploy script (task 130, owner decision D-83).
    Dot-source it:  . (Join-Path $PSScriptRoot 'lib' 'Publish-SolutionComponents.ps1')

.DESCRIPTION
    WHY. Tenant-wide publish (PublishAllXml, `pac solution publish`, `pac solution publish-all`,
    `pac solution import --publish-changes`) publishes EVERY customization in the environment, including other
    people's unpublished work. Nothing in this repo may do that (tests/scripts/Publish-SolutionComponents.Tests.ps1
    and the scoped-publish-lint workflow fail if it comes back).

    PROCEDURE (import, then publish only what was imported)
      1. Import WITHOUT publishing:  pac solution import --environment <url> --path <zip> --force-overwrite
      2. Read the solution's components (solutioncomponents) and map each to a publishable bucket.
      3. POST PublishXml with an exact ParameterXml for those components only.
      4. Read back what can be read back and report anything that is still unpublished.

    PublishXml accepts: entities, webresources, optionsets, sitemaps, dashboards, appmodules (Microsoft Learn:
    PublishXml action; "Create, manage, and publish model-driven apps using code"). Ribbons, forms, views, charts and
    attributes publish WITH their entity; a custom control (PCF) publishes through its bundle web resources.
    A component type this module cannot map is reported in Plan.Unmapped and stops the publish. It is NEVER answered
    with a publish-all.

    Component types that need no publish (security roles, plug-ins, flows, environment variables, ...) are listed in
    $script:NoPublishComponentTypes and are skipped on purpose.
#>

Set-StrictMode -Version Latest

# solutioncomponent.componenttype values that have nothing to publish.
$script:NoPublishComponentTypes = @{
    20 = 'security role'; 21 = 'privilege'; 22 = 'display string'; 29 = 'workflow'; 91 = 'plug-in assembly'
    92 = 'sdk message processing step'; 93 = 'sdk message processing step image'; 95 = 'service endpoint'
    150 = 'routing rule'; 151 = 'routing rule item'; 152 = 'sla'; 154 = 'sla item'
    300 = 'canvas app'; 371 = 'connector'; 372 = 'connector'; 380 = 'environment variable definition'
    381 = 'environment variable value'
}

function ConvertTo-PublishGuid {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Value)
    $g = [guid]::Empty
    if (-not [guid]::TryParse($Value.Trim('{', '}'), [ref]$g)) { throw "Not a GUID: '$Value'" }
    return '{' + $g.ToString() + '}'
}

function New-PublishParameterXml {
<#
.SYNOPSIS  Builds the PublishXml ParameterXml for exactly the listed components. Pure function (unit-tested).
.NOTES     Throws when nothing is listed: an empty set must never turn into "publish something broader".
           Entity and option set names are validated (lower-case schema names); ids are normalised to {guid}.
#>
    [CmdletBinding()]
    param(
        [string[]]$Entities = @(),
        [string[]]$WebResources = @(),
        [string[]]$OptionSets = @(),
        [string[]]$SiteMaps = @(),
        [string[]]$Dashboards = @(),
        [string[]]$AppModules = @()
    )
    $name = '^[a-z_][a-z0-9_]*$'
    $sections = [ordered]@{}
    $add = {
        param($section, $element, $values, $isGuid)
        $items = @(foreach ($v in @($values)) {
                if ([string]::IsNullOrWhiteSpace($v)) { continue }
                if ($isGuid) { ConvertTo-PublishGuid $v }
                else {
                    $n = $v.Trim()
                    if ($n -cnotmatch $name) { throw "Invalid $element name '$v' (expected a lower-case schema name)." }
                    $n
                }
            }) | Sort-Object -Unique
        if (@($items).Count -gt 0) {
            $sections[$section] = "<$section>" + ((@($items) | ForEach-Object { "<$element>$_</$element>" }) -join '') + "</$section>"
        }
    }
    & $add 'entities' 'entity' $Entities $false
    & $add 'webresources' 'webresource' $WebResources $true
    & $add 'optionsets' 'optionset' $OptionSets $false
    & $add 'sitemaps' 'sitemap' $SiteMaps $true
    & $add 'dashboards' 'dashboard' $Dashboards $true
    & $add 'appmodules' 'appmodule' $AppModules $true
    if ($sections.Count -eq 0) { throw 'Nothing to publish: refusing to build an empty ParameterXml (an empty publish must never widen into a publish-all).' }
    return '<importexportxml>' + ($sections.Values -join '') + '</importexportxml>'
}

function Get-DataverseApiContext {
<# Token via the operator's az CLI identity. Returns @{ Api; Headers }. #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$EnvironmentUrl)
    $base = $EnvironmentUrl.TrimEnd('/')
    $token = az account get-access-token --resource $base --query accessToken -o tsv 2>$null
    if (-not $token) { throw "No Dataverse token for $base. Run 'az login' and retry." }
    return @{
        Api     = "$base/api/data/v9.2"
        Headers = @{ Authorization = "Bearer $token"; Accept = 'application/json'; 'OData-MaxVersion' = '4.0'; 'OData-Version' = '4.0'; 'Content-Type' = 'application/json' }
    }
}

function Get-ControlWebResourcePrefix {
<# customcontrols.name is <publisherprefix>_<namespace>.<control>; its bundle web resources are cc_<namespace>.<control>/... #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ControlName)
    return 'cc_' + ($ControlName -replace '^[A-Za-z0-9]+_', '') + '/'
}

function Get-DvPages {
    param([string]$Uri, [hashtable]$Headers)
    $rows = @(); $next = $Uri
    while ($next) {
        $page = Invoke-RestMethod -Method Get -Headers $Headers -Uri $next
        $rows += @($page.value)
        $next = if ($page.PSObject.Properties['@odata.nextLink']) { $page.'@odata.nextLink' } else { $null }
    }
    return $rows
}

function Resolve-PublishPlan {
<#
.SYNOPSIS  Pure mapping: solution components -> publish buckets. Lookups are injected so it is unit-testable.
.PARAMETER Components   rows with componenttype, objectid
.PARAMETER Lookup       scriptblock { param($kind, $id) } returning the resolved value:
                          'entity' -> logical name; 'optionset' -> name; 'view'/'chart'/'form'/'ribbon' -> entity logical
                          name ('form' returns $null for a dashboard); 'control' -> web resource ids (array)
#>
    [CmdletBinding()]
    param([Parameter(Mandatory)][object[]]$Components, [Parameter(Mandatory)][scriptblock]$Lookup, [switch]$IncludeControlHostEntities)
    $entities = New-Object System.Collections.Generic.HashSet[string]
    $webs = New-Object System.Collections.Generic.HashSet[string]
    $opts = New-Object System.Collections.Generic.HashSet[string]
    $maps = New-Object System.Collections.Generic.HashSet[string]
    $dash = New-Object System.Collections.Generic.HashSet[string]
    $apps = New-Object System.Collections.Generic.HashSet[string]
    $skipped = @(); $unmapped = @()
    foreach ($c in $Components) {
        $t = [int]$c.componenttype; $id = "$($c.objectid)"
        switch ($t) {
            1 { [void]$entities.Add((& $Lookup 'entity' $id)) }
            2 { } # attribute: published with its entity
            10 { } # relationship: same
            9 { [void]$opts.Add((& $Lookup 'optionset' $id)) }
            26 { [void]$entities.Add((& $Lookup 'view' $id)) }
            59 { [void]$entities.Add((& $Lookup 'chart' $id)) }
            60 { $e = & $Lookup 'form' $id; if ($e) { [void]$entities.Add($e) } else { [void]$dash.Add($id) } }
            50 { $e = & $Lookup 'ribbon' $id; if ($e) { [void]$entities.Add($e) } }
            61 { [void]$webs.Add($id) }
            62 { [void]$maps.Add($id) }
            80 { [void]$apps.Add($id) }
            66 {
                foreach ($w in @(& $Lookup 'control' $id)) { if ($w) { [void]$webs.Add("$w") } }
                # A PCF import is NOT served until its bundle web resources are published (task 129, 2026-10-08). PublishXml has
                # no element for the registered custom control row (customcontrols.version stays at the old value). Publishing
                # the entities of the forms that host the control is OFF by default: it also publishes anything else pending
                # on those entities. Opt in with -IncludeControlHostEntities once the owner has proven it is needed.
                if ($IncludeControlHostEntities) { foreach ($e in @(& $Lookup 'controlhosts' $id)) { if ($e) { [void]$entities.Add("$e") } } }
            }
            default {
                if ($script:NoPublishComponentTypes.ContainsKey($t)) { $skipped += "$t ($($script:NoPublishComponentTypes[$t]))" }
                else { $unmapped += "$t/$id" }
            }
        }
    }
    $sorted = { param($set) , @($set | Where-Object { $_ } | Sort-Object) }
    return [pscustomobject]@{
        Entities     = & $sorted $entities
        WebResources = & $sorted $webs
        OptionSets   = & $sorted $opts
        SiteMaps     = & $sorted $maps
        Dashboards   = & $sorted $dash
        AppModules   = & $sorted $apps
        Skipped      = $skipped
        Unmapped     = $unmapped
    }
}

function Get-SolutionPublishPlan {
<# Reads the solution's components from Dataverse and returns the plan. Read-only. #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][hashtable]$Context, [Parameter(Mandatory)][string]$SolutionUniqueName, [switch]$IncludeControlHostEntities)
    $api = $Context.Api; $h = $Context.Headers
    $sol = @((Invoke-RestMethod -Method Get -Headers $h -Uri "$api/solutions?`$select=solutionid&`$filter=uniquename eq '$SolutionUniqueName'").value)
    if ($sol.Count -ne 1) { throw "Solution '$SolutionUniqueName' not found (or ambiguous) in $api." }
    $components = Get-DvPages "$api/solutioncomponents?`$select=componenttype,objectid&`$filter=_solutionid_value eq $($sol[0].solutionid)" $h
    $lookup = {
        param($kind, $id)
        switch ($kind) {
            'entity' { (Invoke-RestMethod -Method Get -Headers $h -Uri "$api/EntityDefinitions($id)?`$select=LogicalName").LogicalName }
            'optionset' { (Invoke-RestMethod -Method Get -Headers $h -Uri "$api/GlobalOptionSetDefinitions($id)?`$select=Name").Name }
            'view' { (Invoke-RestMethod -Method Get -Headers $h -Uri "$api/savedqueries($id)?`$select=returnedtypecode").returnedtypecode }
            'chart' { (Invoke-RestMethod -Method Get -Headers $h -Uri "$api/savedqueryvisualizations($id)?`$select=primaryentitytypecode").primaryentitytypecode }
            'form' {
                # type 0 = dashboard (no entity).
                $f = Invoke-RestMethod -Method Get -Headers $h -Uri "$api/systemforms($id)?`$select=objecttypecode,type"
                if ($f.type -eq 0 -or $f.objecttypecode -eq 'none') { $null } else { $f.objecttypecode }
            }
            'ribbon' { (Invoke-RestMethod -Method Get -Headers $h -Uri "$api/ribboncustomizations($id)?`$select=entity").entity }
            'controlhosts' {
                $cc = Invoke-RestMethod -Method Get -Headers $h -Uri "$api/customcontrols($id)?`$select=name"
                $forms = @(Get-DvPages "$api/systemforms?`$select=objecttypecode&`$filter=type eq 2 and contains(formxml,'$($cc.name)')" $h)
                @($forms | ForEach-Object { $_.objecttypecode } | Where-Object { $_ -and $_ -ne 'none' } | Sort-Object -Unique)
            }
            'control' {
                $cc = Invoke-RestMethod -Method Get -Headers $h -Uri "$api/customcontrols($id)?`$select=name"
                # Verified on spaarkedev1 (task 130): control sprk_Spaarke.Visuals.VisualHost owns cc_Spaarke.Visuals.VisualHost/bundle.js and /styles.css.
                @(@(Get-DvPages "$api/webresourceset?`$select=webresourceid&`$filter=startswith(name,'$(Get-ControlWebResourcePrefix $cc.name)')" $h) | Where-Object { $_ } | ForEach-Object { $_.webresourceid })
            }
        }
    }
    return Resolve-PublishPlan -Components $components -Lookup $lookup -IncludeControlHostEntities:$IncludeControlHostEntities
}

function Compare-UnpublishedArtifacts {
<#
.SYNOPSIS  Pure: which rows differ between the published and unpublished copies (rows are @{ id; modifiedon; name }).
           A row only in the unpublished list, or with a different modifiedon, would be published by an entity publish.
#>
    [CmdletBinding()]
    param([object[]]$Published = @(), [object[]]$Unpublished = @())
    $pub = @{}
    foreach ($r in @($Published | Where-Object { $_ })) { $pub["$($r.id)"] = "$($r.modifiedon)" }
    return @(foreach ($u in @($Unpublished | Where-Object { $_ })) {
            if (-not $pub.ContainsKey("$($u.id)") -or $pub["$($u.id)"] -ne "$($u.modifiedon)") { $u }
        })
}

function Get-EntityPublishCollateral {
<#
.SYNOPSIS  Read-only. Views and forms of an entity that have unpublished changes: an entity publish (PublishXml has no
           per-view or per-form element) publishes ALL of them, not only the one you changed. Entity-scoped, not tenant-wide,
           but it can carry other people's pending work on the SAME entity. Report it before publishing.
#>
    [CmdletBinding()]
    param([Parameter(Mandatory)][hashtable]$Context, [Parameter(Mandatory)][string]$Entity)
    $api = $Context.Api; $h = $Context.Headers; $out = @()
    foreach ($spec in @(@{ Set = 'savedqueries'; Id = 'savedqueryid'; Filter = "returnedtypecode eq '$Entity'" }, @{ Set = 'systemforms'; Id = 'formid'; Filter = "objecttypecode eq '$Entity'" })) {
        $sel = "`$select=$($spec.Id),name,modifiedon&`$filter=$($spec.Filter)"
        $map = { param($rows) @(@($rows) | Where-Object { $_ } | ForEach-Object { @{ id = $_.($spec.Id); modifiedon = $_.modifiedon; name = $_.name } }) }
        $published = & $map (Get-DvPages "$api/$($spec.Set)?$sel" $h)
        $unpublished = & $map (Get-DvPages "$api/$($spec.Set)/Microsoft.Dynamics.CRM.RetrieveUnpublishedMultiple()?$sel" $h)
        foreach ($d in @(Compare-UnpublishedArtifacts -Published $published -Unpublished $unpublished)) { $out += "$($spec.Set): $($d.name) ($($d.id))" }
    }
    return $out
}

function Invoke-PublishXml {
<# POSTs PublishXml with an exact ParameterXml. This is the ONLY publish call the repo allows. #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][hashtable]$Context, [Parameter(Mandatory)][string]$ParameterXml)
    if ($ParameterXml -notmatch '^<importexportxml>.+</importexportxml>$') { throw 'ParameterXml must be an <importexportxml> document.' }
    Invoke-RestMethod -Method Post -Headers $Context.Headers -ContentType 'application/json' -Uri "$($Context.Api)/PublishXml" -Body (@{ ParameterXml = $ParameterXml } | ConvertTo-Json) | Out-Null
}

function Test-PublishedReadBack {
<#
.SYNOPSIS  Read-back after a publish. Returns the components still differing from their unpublished copy.
.NOTES     Web resources: published content must equal the unpublished (RetrieveUnpublished) content.
           App modules: published modifiedon must equal the unpublished modifiedon.
           Entities, option sets, site maps and dashboards have no cheap read-back; the caller checks them by their own
           effect (for example the effective ribbon for a ribbon import).
#>
    [CmdletBinding()]
    param([Parameter(Mandatory)][hashtable]$Context, [Parameter(Mandatory)][object]$Plan)
    $api = $Context.Api; $h = $Context.Headers; $pending = @()
    foreach ($id in @($Plan.WebResources)) {
        $g = $id.Trim('{', '}')
        $pub = Invoke-RestMethod -Method Get -Headers $h -Uri "$api/webresourceset($g)?`$select=content"
        $unp = Invoke-RestMethod -Method Get -Headers $h -Uri "$api/webresourceset($g)/Microsoft.Dynamics.CRM.RetrieveUnpublished()?`$select=content"
        if ($pub.content -ne $unp.content) { $pending += "webresource $g" }
    }
    foreach ($id in @($Plan.AppModules)) {
        $g = $id.Trim('{', '}')
        $pub = Invoke-RestMethod -Method Get -Headers $h -Uri "$api/appmodules($g)?`$select=modifiedon"
        $unp = Invoke-RestMethod -Method Get -Headers $h -Uri "$api/appmodules($g)/Microsoft.Dynamics.CRM.RetrieveUnpublished()?`$select=modifiedon"
        if ($pub.modifiedon -ne $unp.modifiedon) { $pending += "appmodule $g" }
    }
    return $pending
}

function Publish-SolutionComponents {
<#
.SYNOPSIS  Scoped publish for one solution's components, then read-back. Returns the plan.
.PARAMETER ExtraWebResources / ExtraEntities  components the script wrote OUTSIDE the solution (for example a web
           resource PATCHed directly) that must be published with it.
#>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][hashtable]$Context,
        [Parameter(Mandatory)][string]$SolutionUniqueName,
        [string[]]$ExtraWebResources = @(),
        [string[]]$ExtraEntities = @(),
        [switch]$IncludeControlHostEntities
    )
    $plan = Get-SolutionPublishPlan -Context $Context -SolutionUniqueName $SolutionUniqueName -IncludeControlHostEntities:$IncludeControlHostEntities
    if (@($plan.Unmapped).Count -gt 0) {
        throw "Cannot publish $SolutionUniqueName without a tenant-wide publish: unmapped component(s) $(@($plan.Unmapped) -join ', '). Add a mapping in scripts/lib/Publish-SolutionComponents.ps1 (never use publish-all)."
    }
    $xml = New-PublishParameterXml -Entities (@($plan.Entities) + $ExtraEntities) -WebResources (@($plan.WebResources) + $ExtraWebResources) `
        -OptionSets $plan.OptionSets -SiteMaps $plan.SiteMaps -Dashboards $plan.Dashboards -AppModules $plan.AppModules
    Write-Host "Scoped publish of ${SolutionUniqueName}: $(@($plan.Entities).Count) entities, $(@($plan.WebResources).Count) web resources, $(@($plan.OptionSets).Count) option sets, $(@($plan.SiteMaps).Count) site maps, $(@($plan.Dashboards).Count) dashboards, $(@($plan.AppModules).Count) app modules."
    foreach ($e in @($plan.Entities) + $ExtraEntities | Select-Object -Unique) {
        foreach ($c in @(Get-EntityPublishCollateral -Context $Context -Entity $e)) { Write-Warning "Entity publish of $e will also publish pending change: $c" }
    }
    Invoke-PublishXml -Context $Context -ParameterXml $xml
    $pending = @(Test-PublishedReadBack -Context $Context -Plan $plan)
    if ($pending.Count -gt 0) { throw "Published, but still unpublished after PublishXml: $($pending -join ', ')." }
    return $plan
}

function Invoke-ScopedSolutionImport {
<#
.SYNOPSIS  Import a solution ZIP without publishing, then publish exactly its components.
.PARAMETER ImportArgs  extra pac arguments (for example --async). --publish-changes is rejected.
.PARAMETER PacExe      path to pac.cmd/pac.exe (a bash shim named pac cannot be run from PowerShell).
#>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$EnvironmentUrl,
        [Parameter(Mandatory)][string]$ZipPath,
        [Parameter(Mandatory)][string]$SolutionUniqueName,
        [string]$PacExe,
        [string[]]$ImportArgs = @('--force-overwrite'),
        [hashtable]$Context,
        [string[]]$ExtraWebResources = @(),
        [string[]]$ExtraEntities = @(),
        [switch]$IncludeControlHostEntities
    )
    if ($ImportArgs | Where-Object { $_ -match '^(--publish-changes|-pc)$' }) { throw '--publish-changes is a tenant-wide publish and is not allowed.' }
    if (-not $PacExe) {
        $PacExe = (Get-Command pac -CommandType Application -ErrorAction SilentlyContinue |
            Where-Object { $_.Extension -in '.cmd', '.exe', '.bat' } | Select-Object -First 1).Source
    }
    if (-not $PacExe) { throw 'pac CLI not found: need pac.cmd or pac.exe on PATH.' }
    if (-not $Context) { $Context = Get-DataverseApiContext -EnvironmentUrl $EnvironmentUrl }
    & $PacExe solution import --environment $EnvironmentUrl --path $ZipPath @ImportArgs
    if ($LASTEXITCODE -ne 0) { throw "pac solution import failed ($LASTEXITCODE)." }
    return Publish-SolutionComponents -Context $Context -SolutionUniqueName $SolutionUniqueName `
        -ExtraWebResources $ExtraWebResources -ExtraEntities $ExtraEntities -IncludeControlHostEntities:$IncludeControlHostEntities
}
