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

    Component types that need no publish (security roles, plug-ins, environment variables, ...) are listed in
    $script:NoPublishComponentTypes and are skipped on purpose.

    ORDER. A publish is split into requests of at most 25 items (one PublishXml call over about 129 entities and 270 web
    resources can exceed the request time limit). Option sets and web resources go first, then entities, the application
    ribbon, site maps and dashboards, and app modules LAST, so a form or an app never goes live before the web resource, PCF
    bundle or option set it uses. If a request fails, the error prints the exact resume command (re-run the publish
    WITHOUT re-importing): pwsh scripts/Import-SolutionScoped.ps1 -EnvironmentUrl <url> -SolutionUniqueName <name> -PublishOnly

    READ-BACK after every request (a failure stops the run and names what is still unpublished):
      web resources  published content equals RetrieveUnpublished content
      app modules    published modifiedon equals the unpublished one
      app settings   published value and modifiedon equal the unpublished ones
      entities       no system form, saved query or chart of the entity differs from its RetrieveUnpublished copy
      dashboards     system form (type 0): published formxml equals the unpublished one (systemform has no modifiedon column)
      site maps      published modifiedon and sitemapxml equal the unpublished ones. Microsoft Learn says the <sitemap> value in
                     ParameterXml "is not used", so a site map that failed to publish would otherwise pass silently
    PENDING COLLATERAL (owner decision D-103): PublishXml has no per-view or per-form element, so publishing an entity also publishes
    every pending (unpublished) view, form and chart of that entity, and publishing an app (needed for an app setting) publishes the
    whole app. When that would publish SOMEONE ELSE'S pending change, the procedure STOPS before any import or publish, lists every
    entity or app and each pending component, and prints the exact re-run command with the opt-in flag -AllowPendingCollateral (the
    flag name was chosen because no existing module name fits). With the flag it warns, lists and continues. The solution's own
    installed items are left out. The check runs before `pac solution import`, so nothing is half-imported when it stops. The
    -PublishOnly resume applies the same rule, and the printed resume command carries the flag only if the original run had it.

    KNOWN LIMITS (read-back and workflows)
      K3  -AllowWorkflows requires EVERY workflow in the solution to read statecode 1, so a workflow deliberately left as a draft cannot
          pass. No solution in the repo or on spaarkedev1 has one today; handle that case by hand if it ever appears.
      K4  The entity read-back can fail falsely if someone edits a form or view of that entity between the publish and the check. The
          failure is loud and the printed resume command recovers it.
      Option sets and the application ribbon have no read-back surface here. They are sent in the request, and a
    failure of the request itself is the only signal. Check them by effect (a choice column's options; the effective ribbon).

    WORKFLOWS (type 29) are refused by default: the RetrieveUnpublished probe answers 200 for them, so a draft layer cannot be
    excluded, and a workflow is activated by the import, not by PublishXml. When a solution does contain one, opt in with
    -AllowWorkflows (Import-SolutionScoped.ps1 / Invoke-ScopedSolutionImport). That adds `--activate-plugins` to the pac import
    (pac help: "Activate plug-ins and workflows on the solution"; same switch as the provisioning importer's PublishWorkflows)
    and, after the publish, reads workflows(id).statecode for every workflow in the solution and fails unless it is 1 (Activated).

    PROBE METHOD (why a type is in the no-publish list): <set>(<fake id>)/Microsoft.Dynamics.CRM.RetrieveUnpublished(). Only error
    code 0x80060888 ("Resource not found for the segment 'Microsoft.Dynamics.CRM.RetrieveUnpublished()'") proves the table is not
    bound to the unpublished-layer function. Error 0x80040217 ("... With Id = ... Does Not Exist") means the function IS bound and
    only the record is missing, so the table has a draft layer. A bare 404 on a real id is not enough on its own.
#>

# No Set-StrictMode here: a dot-sourced module's strict mode would leak into the calling script (reading a missing
# property such as @odata.nextLink on a last page would start to throw). Task 130 review F1.

# solutioncomponent.componenttype values that have nothing to publish. Evidence (task 130 review rounds 2-4): for each table,
# <set>(<fake id>)/Microsoft.Dynamics.CRM.RetrieveUnpublished() on spaarkedev1 answers error 0x80060888 "Resource not found for the
# segment", i.e. the table is not bound to the unpublished-layer function. (0x80040217 would mean "record missing", the function IS
# bound: that is what appsettings and workflows answer.) The reviewer re-probed every type below with a fake id: all 0x80060888.
# Tables: roles, privileges, display strings, entity/attribute maps, field security profiles, field permissions, plug-in assemblies,
# steps and step images, service endpoints, canvas apps, environment variables, Dataverse search tables, ai skill configs.
# Types without that proof are deliberately absent so they stay unmapped and block the import: workflows/flows (29, bound), SLAs and
# routing rules (150-154) and connectors (371/372) (no rows in dev to probe).
$script:NoPublishComponentTypes = @{
    20 = 'security role'; 21 = 'privilege'; 22 = 'display string'
    46 = 'entity map'; 47 = 'attribute map'
    70 = 'field security profile'; 71 = 'field permission'
    91 = 'plug-in assembly'; 92 = 'sdk message processing step'; 93 = 'sdk message processing step image'; 95 = 'service endpoint'
    300 = 'canvas app'; 380 = 'environment variable definition'; 381 = 'environment variable value'
    10139 = 'dataverse search table'; 10141 = 'dataverse search table entity'; 10314 = 'ai skill config'
}

# Component types this module maps to a publish bucket (see Resolve-PublishPlan). 2/10 are published with their entity.
# 10075 (app setting) has an unpublished layer (RetrieveUnpublished answers 200 on spaarkedev1): it is published through its parent app module.
$script:MappedComponentTypes = @(1, 2, 9, 10, 26, 50, 59, 60, 61, 62, 66, 80, 10075)

function Test-ComponentTypeKnown {
<# True when the type is mapped to a publish bucket or is deliberately a no-publish type. Pure. #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][int]$ComponentType, [switch]$AllowWorkflows)
    if ($AllowWorkflows -and $ComponentType -eq 29) { return $true }
    return ($script:MappedComponentTypes -contains $ComponentType) -or $script:NoPublishComponentTypes.ContainsKey($ComponentType)
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
        [string[]]$AppModules = @(),
        [switch]$ApplicationRibbon
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
    if ($ApplicationRibbon) { $sections['ribbons'] = '<ribbons><ribbon /></ribbons>' }
    if ($sections.Count -eq 0) { throw 'Nothing to publish: refusing to build an empty ParameterXml (an empty publish must never widen into a publish-all).' }
    return '<importexportxml>' + ($sections.Values -join '') + '</importexportxml>'
}

function New-PublishParameterXmlFromPlan {
<# Pure: the complete (unchunked) ParameterXml for a plan, including the application ribbon. Used by -PlanOnly. #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Plan)
    return New-PublishParameterXml -Entities $Plan.Entities -WebResources $Plan.WebResources -OptionSets $Plan.OptionSets `
        -SiteMaps $Plan.SiteMaps -Dashboards $Plan.Dashboards -AppModules $Plan.AppModules -ApplicationRibbon:([bool]$Plan.ApplicationRibbon)
}

function ConvertTo-ExtraList {
<#
.SYNOPSIS  Splits comma-separated values, trims, drops empties and VALIDATES them (task 130 round 6, F4/K5). Web resources must be
           GUIDs (returned as lower-case, no braces); entities must be lower-case logical names. Anything else throws, before any
           import or publish, so an unvalidated value can never reach a resume command.
#>
    [CmdletBinding()]
    param([string[]]$Values = @(), [Parameter(Mandatory)][ValidateSet('WebResource', 'Entity')][string]$Kind)
    $out = @()
    foreach ($v in @($Values)) {
        foreach ($part in @("$v" -split ',')) {
            $t = $part.Trim()
            if (-not $t) { continue }
            if ($Kind -eq 'WebResource') {
                $m = [regex]::Match($t, '^\{?([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\}?$')
                if (-not $m.Success) { throw "Invalid web resource id (a GUID is required); rejected before any import or publish." }
                $out += $m.Groups[1].Value.ToLowerInvariant()
            } else {
                if ($t -cnotmatch '^[a-z_][a-z0-9_]*$') { throw "Invalid entity logical name (lower-case letters, digits and underscores only); rejected before any import or publish." }
                $out += $t
            }
        }
    }
    return @($out | Select-Object -Unique)
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
    param([Parameter(Mandatory)][object[]]$Components, [Parameter(Mandatory)][scriptblock]$Lookup, [switch]$IncludeControlHostEntities, [switch]$AllowWorkflows)
    $entities = New-Object System.Collections.Generic.HashSet[string]
    $webs = New-Object System.Collections.Generic.HashSet[string]
    $opts = New-Object System.Collections.Generic.HashSet[string]
    $maps = New-Object System.Collections.Generic.HashSet[string]
    $dash = New-Object System.Collections.Generic.HashSet[string]
    $apps = New-Object System.Collections.Generic.HashSet[string]
    $sets = New-Object System.Collections.Generic.HashSet[string]
    $skipped = @(); $unmapped = @(); $appRibbon = $false; $flows = @()
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
            50 { $e = & $Lookup 'ribbon' $id; if ($e) { [void]$entities.Add($e) } else { $appRibbon = $true } }
            61 { [void]$webs.Add($id) }
            62 { [void]$maps.Add($id) }
            80 { [void]$apps.Add($id) }
            10075 {
                # An app setting has an unpublished layer; PublishXml has no element for it, so publish its parent app module and read the setting back.
                $parent = & $Lookup 'appsetting' $id
                if ($parent) { [void]$apps.Add("$parent"); [void]$sets.Add($id) } else { $unmapped += "$t/$id (app setting with no parent app module)" }
            }
            66 {
                foreach ($w in @(& $Lookup 'control' $id)) { if ($w) { [void]$webs.Add("$w") } }
                # A PCF import is NOT served until its bundle web resources are published (task 129, 2026-10-08). PublishXml has
                # no element for the registered custom control row (customcontrols.version stays at the old value). Publishing
                # the entities of the forms that host the control is OFF by default: it also publishes anything else pending
                # on those entities. Opt in with -IncludeControlHostEntities once the owner has proven it is needed.
                if ($IncludeControlHostEntities) { foreach ($e in @(& $Lookup 'controlhosts' $id)) { if ($e) { [void]$entities.Add("$e") } } }
            }
            default {
                if ($AllowWorkflows -and $t -eq 29) { $flows += $id; $skipped += '29 (workflow, activated by the import)' }
                elseif ($script:NoPublishComponentTypes.ContainsKey($t)) { $skipped += "$t ($($script:NoPublishComponentTypes[$t]))" }
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
        AppSettings  = & $sorted $sets
        Workflows    = @($flows)
        ApplicationRibbon = $appRibbon
        Skipped      = $skipped
        Unmapped     = $unmapped
    }
}

function Get-SolutionComponentRows {
<# Components of an installed solution (componenttype, objectid), or $null when the solution is not installed. Read-only. #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][hashtable]$Context, [Parameter(Mandatory)][string]$SolutionUniqueName)
    $api = $Context.Api; $h = $Context.Headers
    $sol = @((Invoke-RestMethod -Method Get -Headers $h -Uri "$api/solutions?`$select=solutionid&`$filter=uniquename eq '$SolutionUniqueName'").value | Where-Object { $_ })
    if ($sol.Count -eq 0) { return $null }
    if ($sol.Count -gt 1) { throw "Solution '$SolutionUniqueName' is ambiguous in $api." }
    return @(Get-DvPages "$api/solutioncomponents?`$select=componenttype,objectid,rootcomponentbehavior&`$filter=_solutionid_value eq $($sol[0].solutionid)" $h)
}

function Get-FullyOwnedEntityNames {
<#
.SYNOPSIS  F2 (round 9). Logical names of the entities the solution includes WITH ALL their subcomponents (rootcomponentbehavior 0).
           Their forms, views and charts have no solutioncomponents rows of their own, so ExcludeIds cannot recognise them as the
           solution's own; every subcomponent of such an entity is the solution's own. Read-only.
#>
    [CmdletBinding()]
    param([Parameter(Mandatory)][hashtable]$Context, [object[]]$Rows = @())
    $names = @()
    foreach ($r in @($Rows | Where-Object { $_ -and [int]$_.componenttype -eq 1 -and $null -ne $_.rootcomponentbehavior -and "$($_.rootcomponentbehavior)" -ne '' -and [int]$_.rootcomponentbehavior -eq 0 })) {
        $n = (Invoke-RestMethod -Method Get -Headers $Context.Headers -Uri "$($Context.Api)/EntityDefinitions($("$($r.objectid)".Trim('{', '}')))?`$select=LogicalName").LogicalName
        if ($n) { $names += "$n".ToLowerInvariant() }
    }
    return @($names | Select-Object -Unique)
}

# What a solution ZIP carries besides its root components (K3). customizations.xml top-level elements and the ZIP's top-level
# folders each stand for a component type. Names read from a real SpaarkeMaster export (spaarkedev1, 2026-10-08) and the
# Dataverse Customizations.xml schema. A NON-EMPTY element or folder that is not listed here is an unknown part and blocks the import.
$script:ZipElementTypes = @{
    'Entities' = 1; 'EntityRelationships' = 10; 'EntityMaps' = 46; 'optionsets' = 9; 'Roles' = 20; 'Workflows' = 29
    'FieldSecurityProfiles' = 70; 'CustomControls' = 66; 'WebResources' = 61; 'AppModuleSiteMaps' = 62; 'AppModules' = 80; 'Dashboards' = 60
}
$script:ZipIgnoredElements = @('Languages')
$script:ZipFolderTypes = @{
    'controls' = 66; 'webresources' = 61; 'workflows' = 29; 'environmentvariabledefinitions' = 380; 'environmentvariablevalues' = 381
    'aiskillconfigs' = 10314; 'dvtablesearchs' = 10139; 'dvtablesearchentities' = 10141; 'appsettings' = 10075; 'canvasapps' = 300
    'pluginassemblies' = 91; 'sdkmessageprocessingsteps' = 92; 'serviceendpoints' = 95
}
$script:ZipIgnoredEntries = @('[content_types].xml', 'solution.xml', 'customizations.xml')

function Get-ZipSolutionInfo {
<#
.SYNOPSIS  Reads a solution ZIP before anything is imported: unique name, managed flag, root component types and root entity
           names (solution.xml), plus every component type implied by customizations.xml elements and top-level folders.
           UnknownParts lists non-empty elements/folders this module cannot map. Pure file read.
#>
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ZipPath)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $ZipPath).Path)
    try {
        $readXml = {
            param($name)
            $entry = $zip.Entries | Where-Object { $_.FullName -ieq $name } | Select-Object -First 1
            if (-not $entry) { return $null }
            $reader = New-Object System.IO.StreamReader($entry.Open())
            try { return [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
        }
        $doc = & $readXml 'solution.xml'
        if (-not $doc) { throw "No solution.xml in $ZipPath." }
        $cust = & $readXml 'customizations.xml'
        $folders = @($zip.Entries | ForEach-Object { ($_.FullName -split '/')[0] } | Sort-Object -Unique)
        # Best effort: the parent app module id of every app setting file in the ZIP (appsettings/<id>/appsetting.xml).
        $settingParents = @()
        foreach ($en in @($zip.Entries | Where-Object { $_.FullName -ilike 'appsettings/*' -and $_.FullName -like '*.xml' })) {
            $sr = New-Object System.IO.StreamReader($en.Open())
            try { $txt = $sr.ReadToEnd() } finally { $sr.Dispose() }
            $mm = [regex]::Match($txt, 'parentappmodule[^>]*>\s*\{?([0-9a-fA-F-]{36})')
            if ($mm.Success) { $settingParents += $mm.Groups[1].Value.ToLowerInvariant() }
        }
    } finally { $zip.Dispose() }
    $m = $doc.ImportExportXml.SolutionManifest
    $roots = @($m.RootComponents.RootComponent | Where-Object { $_ })
    $types = New-Object System.Collections.Generic.HashSet[int]
    $unknown = @()
    foreach ($r in $roots) { [void]$types.Add([int]$r.type) }
    if ($cust) {
        foreach ($n in $cust.ImportExportXml.ChildNodes) {
            if ($n.NodeType -ne 'Element') { continue }
            $kids = @($n.ChildNodes | Where-Object { $_.NodeType -eq 'Element' }).Count
            if ($kids -eq 0 -or $script:ZipIgnoredElements -contains $n.LocalName) { continue }
            $key = @($script:ZipElementTypes.Keys | Where-Object { $_ -ieq $n.LocalName }) | Select-Object -First 1
            if ($key) { [void]$types.Add([int]$script:ZipElementTypes[$key]) } else { $unknown += "customizations.xml/$($n.LocalName)" }
        }
    }
    foreach ($f in $folders) {
        if ($script:ZipIgnoredEntries -contains $f.ToLowerInvariant()) { continue }
        if ($script:ZipFolderTypes.ContainsKey($f.ToLowerInvariant())) { [void]$types.Add([int]$script:ZipFolderTypes[$f.ToLowerInvariant()]) } else { $unknown += "folder/$f" }
    }
    return [pscustomobject]@{
        UniqueName   = "$($m.UniqueName)"
        Managed      = ("$($m.Managed)" -eq '1')
        RootTypes    = @($roots | ForEach-Object { [int]$_.type } | Sort-Object -Unique)
        AllTypes     = @($types | Sort-Object)
        UnknownParts = $unknown
        AppSettingParents = @($settingParents | Sort-Object -Unique)
        RootEntities = @($roots | Where-Object { [int]$_.type -eq 1 } | ForEach-Object { "$($_.schemaName)".ToLowerInvariant() } | Sort-Object -Unique)
        FullEntities = @($roots | Where-Object { [int]$_.type -eq 1 -and (-not $_.behavior -or [int]$_.behavior -eq 0) } | ForEach-Object { "$($_.schemaName)".ToLowerInvariant() } | Sort-Object -Unique)
    }
}

function Resolve-PendingCollateral {
<#
.SYNOPSIS  D-103. Lists the pending (unpublished) changes that a publish of these entities and parent apps would ALSO publish, and
           STOPS (throws) unless -Allow. With -Allow it warns for each item and returns. Nothing is imported or published by this
           function. -RerunCommand is the exact command (without the flag) to run again; the message appends -AllowPendingCollateral.
#>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][hashtable]$Context,
        [string[]]$Entities = @(), [string[]]$ParentApps = @(), [string[]]$OwnIds = @(), [string[]]$InSolutionApps = @(), [string[]]$OwnedEntities = @(),
        [switch]$Allow, [Parameter(Mandatory)][string]$RerunCommand
    )
    $items = @()
    $owned = @($OwnedEntities | ForEach-Object { "$_".ToLowerInvariant() })
    foreach ($ent in @($Entities | Where-Object { $_ } | Select-Object -Unique)) {
        if ($owned -contains "$ent".ToLowerInvariant()) { continue }   # included with all subcomponents: its forms, views and charts are the solution's own
        foreach ($c in @(Get-EntityPublishCollateral -Context $Context -Entity $ent -ExcludeIds $OwnIds)) { $items += "entity ${ent}: $c" }
    }
    $inApps = @($InSolutionApps | ForEach-Object { "$_".Trim('{', '}').ToLowerInvariant() })
    foreach ($app in @($ParentApps | Where-Object { $_ } | ForEach-Object { "$_".Trim('{', '}').ToLowerInvariant() } | Select-Object -Unique)) {
        if ($inApps -contains $app) { continue }   # the app is part of the solution: its own changes are the ones being published
        foreach ($c in @(Get-AppPendingChanges -Context $Context -AppId $app)) { $items += "parent app ${app} (outside the solution; an app setting publish publishes the whole app): $c" }
    }
    if ($items.Count -eq 0) { return @() }
    if ($Allow) {
        foreach ($i in $items) { Write-Warning "Publishing will also publish someone else's pending change (allowed by -AllowPendingCollateral): $i" }
        return $items
    }
    throw ("Stopping BEFORE any import or publish: this publish would also publish pending (unpublished) changes that are not part of the solution:`n  - " +
        ($items -join "`n  - ") + "`nIf publishing them is intended, re-run with the opt-in flag:`n  $RerunCommand -AllowPendingCollateral")
}

function Invoke-ImportPreflight {
<#
.SYNOPSIS  Runs before ANY import of an unmanaged solution: (1) every component type the ZIP carries (root components,
           customizations.xml elements, top-level folders) and the installed solution's components must be mapped or a proven
           no-publish type, else throws with nothing imported; (2) warns about other people's pending views/forms on the entities
           the import will publish (the solution's own installed items are left out). Returns the ZIP info.
#>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][hashtable]$Context, [Parameter(Mandatory)][string]$ZipPath, [Parameter(Mandatory)][string]$SolutionUniqueName,
        [string[]]$ExtraEntities = @(),
        [switch]$AllowWorkflows,
        [switch]$AllowPendingCollateral,
        [string]$RerunCommand = 'the same command'
    )
    $zipInfo = Get-ZipSolutionInfo -ZipPath $ZipPath
    $installed = Get-SolutionComponentRows -Context $Context -SolutionUniqueName $SolutionUniqueName
    $types = @($zipInfo.AllTypes) + @($installed | Where-Object { $_ } | ForEach-Object { [int]$_.componenttype })
    $unknown = @($types | Sort-Object -Unique | Where-Object { -not (Test-ComponentTypeKnown $_ -AllowWorkflows:$AllowWorkflows) } | ForEach-Object { "type $_" }) + @($zipInfo.UnknownParts)
    if ($unknown.Count -gt 0) {
        throw "Refusing to import ${SolutionUniqueName}: $($unknown -join ', ') cannot be published by the scoped procedure. Map them in scripts/lib/Publish-SolutionComponents.ps1 first. Nothing was imported."
    }
    $own = @($installed | Where-Object { $_ } | ForEach-Object { $_.objectid })
    # K2: an app setting whose parent app sits OUTSIDE the solution publishes that whole app, with anyone else's pending changes to it.
    $inSolutionApps = @($installed | Where-Object { $_ -and [int]$_.componenttype -eq 80 } | ForEach-Object { "$($_.objectid)" })
    $parents = @()
    foreach ($row in @($installed | Where-Object { $_ -and [int]$_.componenttype -eq 10075 })) {
        $p = Get-AppSettingParent -Context $Context -AppSettingId "$($row.objectid)"
        if ($p) { $parents += "$p" }
    }
    $parents += @($zipInfo.AppSettingParents)
    # D-103: stop (before the import) unless the caller opted in.
    $ownedEntities = @(Get-FullyOwnedEntityNames -Context $Context -Rows $installed) + @($zipInfo.FullEntities)
    Resolve-PendingCollateral -Context $Context -Entities (@($zipInfo.RootEntities) + $ExtraEntities) -ParentApps $parents -OwnIds $own -OwnedEntities $ownedEntities `
        -InSolutionApps $inSolutionApps -Allow:$AllowPendingCollateral -RerunCommand $RerunCommand | Out-Null
    return $zipInfo
}

function Get-SolutionPublishPlan {
<# Reads the solution's components from Dataverse and returns the plan. Read-only. #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][hashtable]$Context, [Parameter(Mandatory)][string]$SolutionUniqueName, [switch]$IncludeControlHostEntities, [switch]$AllowWorkflows)
    $api = $Context.Api; $h = $Context.Headers
    $components = Get-SolutionComponentRows -Context $Context -SolutionUniqueName $SolutionUniqueName
    if ($null -eq $components) { throw "Solution '$SolutionUniqueName' not found in $api." }
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
            'appsetting' { (Invoke-RestMethod -Method Get -Headers $h -Uri "$api/appsettings($id)?`$select=_parentappmoduleid_value").'_parentappmoduleid_value' }
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
    return Resolve-PublishPlan -Components $components -Lookup $lookup -IncludeControlHostEntities:$IncludeControlHostEntities -AllowWorkflows:$AllowWorkflows
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
    param([Parameter(Mandatory)][hashtable]$Context, [Parameter(Mandatory)][string]$Entity, [string[]]$ExcludeIds = @())
    $api = $Context.Api; $h = $Context.Headers; $out = @()
    $skipIds = @($ExcludeIds | Where-Object { $_ } | ForEach-Object { $_.Trim('{', '}').ToLowerInvariant() })
    # Columns are per table (round 9, F1): systemform has NO modifiedon (it exposes formxml, overwritetime, versionnumber), so a form is
    # compared by a hash of its formxml; savedquery and savedqueryvisualization do have modifiedon. Pinned by a test.
    foreach ($spec in @(
            @{ Set = 'savedqueries'; Id = 'savedqueryid'; Stamp = 'modifiedon'; Filter = "returnedtypecode eq '$Entity'" },
            @{ Set = 'systemforms'; Id = 'formid'; Stamp = 'formxml'; Filter = "objecttypecode eq '$Entity'" },
            @{ Set = 'savedqueryvisualizations'; Id = 'savedqueryvisualizationid'; Stamp = 'modifiedon'; Filter = "primaryentitytypecode eq '$Entity'" })) {
        $sel = "`$select=$($spec.Id),name,$($spec.Stamp)&`$filter=$($spec.Filter)"
        $map = {
            param($rows)
            @(@($rows) | Where-Object { $_ } | ForEach-Object {
                    $stamp = "$($_.($spec.Stamp))"
                    if ($spec.Stamp -eq 'formxml') { $stamp = [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($stamp))) }
                    @{ id = $_.($spec.Id); modifiedon = $stamp; name = $_.name } })
        }
        $published = & $map (Get-DvPages "$api/$($spec.Set)?$sel" $h)
        $unpublished = & $map (Get-DvPages "$api/$($spec.Set)/Microsoft.Dynamics.CRM.RetrieveUnpublishedMultiple()?$sel" $h)
        foreach ($d in @(Compare-UnpublishedArtifacts -Published $published -Unpublished $unpublished)) {
            if ($skipIds -contains "$($d.id)".ToLowerInvariant()) { continue }
            $out += "$($spec.Set): $($d.name) ($($d.id))"
        }
    }
    return $out
}

function Get-AppSettingParent {
<# The parent app module id of an app setting, or $null. Read-only. #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][hashtable]$Context, [Parameter(Mandatory)][string]$AppSettingId)
    return (Invoke-RestMethod -Method Get -Headers $Context.Headers -Uri "$($Context.Api)/appsettings($($AppSettingId.Trim('{', '}')))?`$select=_parentappmoduleid_value").'_parentappmoduleid_value'
}

function Get-AppPendingChanges {
<# Read-only. Pending (unpublished) changes on an app module and its settings. Publishing an app publishes ALL of them. #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][hashtable]$Context, [Parameter(Mandatory)][string]$AppId)
    $api = $Context.Api; $h = $Context.Headers; $g = $AppId.Trim('{', '}'); $out = @()
    $pub = Invoke-RestMethod -Method Get -Headers $h -Uri "$api/appmodules($g)?`$select=modifiedon,name"
    $unp = Invoke-RestMethod -Method Get -Headers $h -Uri "$api/appmodules($g)/Microsoft.Dynamics.CRM.RetrieveUnpublished()?`$select=modifiedon,name"
    if ($pub.modifiedon -ne $unp.modifiedon) { $out += "app module $($pub.name) ($g)" }
    $sel = "`$select=appsettingid,displayname,modifiedon&`$filter=_parentappmoduleid_value eq $g"
    $map = { param($rows) @(@($rows) | Where-Object { $_ } | ForEach-Object { @{ id = $_.appsettingid; modifiedon = $_.modifiedon; name = $_.displayname } }) }
    $published = & $map (Get-DvPages "$api/appsettings?$sel" $h)
    $unpublished = & $map (Get-DvPages "$api/appsettings/Microsoft.Dynamics.CRM.RetrieveUnpublishedMultiple()?$sel" $h)
    foreach ($d in @(Compare-UnpublishedArtifacts -Published $published -Unpublished $unpublished)) { $out += "app setting $($d.name) ($($d.id))" }
    return $out
}

function Test-WorkflowsActivated {
<# Read-back for -AllowWorkflows: every workflow in the list must have statecode 1 (Activated). Returns the ones that are not. #>
    [CmdletBinding()]
    param([Parameter(Mandatory)][hashtable]$Context, [string[]]$WorkflowIds = @())
    $bad = @()
    foreach ($id in @($WorkflowIds | Where-Object { $_ })) {
        $g = $id.Trim('{', '}')
        $w = Invoke-RestMethod -Method Get -Headers $Context.Headers -Uri "$($Context.Api)/workflows($g)?`$select=statecode,name"
        if ([int]$w.statecode -ne 1) { $bad += "workflow $($w.name) ($g) statecode=$($w.statecode)" }
    }
    return $bad
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
    foreach ($id in @($Plan.WebResources | Where-Object { $_ })) {
        $g = $id.Trim('{', '}')
        $pub = Invoke-RestMethod -Method Get -Headers $h -Uri "$api/webresourceset($g)?`$select=content"
        $unp = Invoke-RestMethod -Method Get -Headers $h -Uri "$api/webresourceset($g)/Microsoft.Dynamics.CRM.RetrieveUnpublished()?`$select=content"
        if ($pub.content -ne $unp.content) { $pending += "webresource $g" }
    }
    foreach ($id in @($Plan.AppModules | Where-Object { $_ })) {
        $g = $id.Trim('{', '}')
        $pub = Invoke-RestMethod -Method Get -Headers $h -Uri "$api/appmodules($g)?`$select=modifiedon"
        $unp = Invoke-RestMethod -Method Get -Headers $h -Uri "$api/appmodules($g)/Microsoft.Dynamics.CRM.RetrieveUnpublished()?`$select=modifiedon"
        if ($pub.modifiedon -ne $unp.modifiedon) { $pending += "appmodule $g" }
    }
    foreach ($id in @($Plan.SiteMaps | Where-Object { $_ })) {
        $g = $id.Trim('{', '}')
        # RetrieveUnpublished (single record) is NOT supported for sitemap (0x80040800, verified on spaarkedev1, round 9): use the Multiple form with a filter.
        $pub = @((Invoke-RestMethod -Method Get -Headers $h -Uri "$api/sitemaps?`$select=sitemapxml,modifiedon&`$filter=sitemapid eq $g").value | Where-Object { $_ }) | Select-Object -First 1
        $unp = @((Invoke-RestMethod -Method Get -Headers $h -Uri "$api/sitemaps/Microsoft.Dynamics.CRM.RetrieveUnpublishedMultiple()?`$select=sitemapxml,modifiedon&`$filter=sitemapid eq $g").value | Where-Object { $_ }) | Select-Object -First 1
        if ($unp -and ($pub.sitemapxml -ne $unp.sitemapxml -or $pub.modifiedon -ne $unp.modifiedon)) { $pending += "sitemap $g" }
    }
    foreach ($id in @($Plan.Dashboards | Where-Object { $_ })) {
        # Dashboards are system forms of type 0: same unpublished-copy comparison as forms.
        $g = $id.Trim('{', '}')
        $pub = Invoke-RestMethod -Method Get -Headers $h -Uri "$api/systemforms($g)?`$select=formxml"
        $unp = Invoke-RestMethod -Method Get -Headers $h -Uri "$api/systemforms($g)/Microsoft.Dynamics.CRM.RetrieveUnpublished()?`$select=formxml"
        if ($pub.formxml -ne $unp.formxml) { $pending += "dashboard $g" }
    }
    foreach ($ent in @($Plan.Entities | Where-Object { $_ })) {
        foreach ($c in @(Get-EntityPublishCollateral -Context $Context -Entity $ent)) { $pending += "entity ${ent}: $c" }
    }
    foreach ($id in @($Plan.AppSettings | Where-Object { $_ })) {
        $g = $id.Trim('{', '}')
        $pub = Invoke-RestMethod -Method Get -Headers $h -Uri "$api/appsettings($g)?`$select=value,modifiedon"
        $unp = Invoke-RestMethod -Method Get -Headers $h -Uri "$api/appsettings($g)/Microsoft.Dynamics.CRM.RetrieveUnpublished()?`$select=value,modifiedon"
        if ($pub.value -ne $unp.value -or $pub.modifiedon -ne $unp.modifiedon) { $pending += "appsetting $g" }
    }
    return $pending
}

function Split-PublishPlan {
<#
.SYNOPSIS  Pure. Splits a publish into chunks of at most ChunkSize items so one PublishXml request stays inside the request
           time limit (SpaarkeMaster has about 129 entities and 270 web resources). Each chunk is a hashtable of the
           New-PublishParameterXml parameters. App settings are not published by id (they ride with their parent app module);
           they are attached to the LAST chunk for read-back only.
#>
    [CmdletBinding()]
    param(
        [string[]]$Entities = @(), [string[]]$WebResources = @(), [string[]]$OptionSets = @(), [string[]]$SiteMaps = @(),
        [string[]]$Dashboards = @(), [string[]]$AppModules = @(), [string[]]$AppSettings = @(),
        [switch]$ApplicationRibbon, [int]$ChunkSize = 25
    )
    if ($ChunkSize -lt 1) { throw 'ChunkSize must be at least 1.' }
    $items = New-Object System.Collections.Generic.List[object]
    # Order matters: what a form, app or site map USES goes live first (option sets, web resources), app modules last.
    foreach ($spec in @(@('OptionSets', $OptionSets), @('WebResources', $WebResources), @('Entities', $Entities))) {
        foreach ($v in @($spec[1] | Where-Object { $_ })) { $items.Add(@{ Kind = $spec[0]; Value = $v }) }
    }
    if ($ApplicationRibbon) { $items.Add(@{ Kind = 'Ribbon'; Value = 'application' }) }
    foreach ($spec in @(@('SiteMaps', $SiteMaps), @('Dashboards', $Dashboards), @('AppModules', $AppModules))) {
        foreach ($v in @($spec[1] | Where-Object { $_ })) { $items.Add(@{ Kind = $spec[0]; Value = $v }) }
    }
    $chunks = @()
    for ($i = 0; $i -lt $items.Count; $i += $ChunkSize) {
        $slice = @($items | Select-Object -Skip $i -First $ChunkSize)
        $c = @{ Entities = @(); WebResources = @(); OptionSets = @(); SiteMaps = @(); Dashboards = @(); AppModules = @(); AppSettings = @(); ApplicationRibbon = $false }
        foreach ($it in $slice) { if ($it.Kind -eq 'Ribbon') { $c.ApplicationRibbon = $true } else { $c[$it.Kind] += $it.Value } }
        $chunks += , $c
    }
    if ($chunks.Count -gt 0) { $chunks[$chunks.Count - 1].AppSettings = @($AppSettings | Where-Object { $_ }) }
    return $chunks
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
        [switch]$IncludeControlHostEntities,
        [switch]$SkipCollateralCheck,
        [switch]$AllowWorkflows,
        [switch]$AllowPendingCollateral,
        [int]$ChunkSize = 25
    )
    # Validate first: nothing unvalidated may reach a REST call or the resume command.
    $ExtraWebResources = @(ConvertTo-ExtraList -Values $ExtraWebResources -Kind WebResource)
    $ExtraEntities = @(ConvertTo-ExtraList -Values $ExtraEntities -Kind Entity)
    $plan = Get-SolutionPublishPlan -Context $Context -SolutionUniqueName $SolutionUniqueName -IncludeControlHostEntities:$IncludeControlHostEntities -AllowWorkflows:$AllowWorkflows
    if (@($plan.Unmapped).Count -gt 0) {
        throw "Cannot publish $SolutionUniqueName without a tenant-wide publish: unmapped component(s) $(@($plan.Unmapped) -join ', '). Add a mapping in scripts/lib/Publish-SolutionComponents.ps1 (never use publish-all)."
    }
    $allEntities = @(@($plan.Entities) + $ExtraEntities | Where-Object { $_ } | Select-Object -Unique)
    $allWebs = @(@($plan.WebResources) + $ExtraWebResources | Where-Object { $_ } | Select-Object -Unique)
    $chunks = @(Split-PublishPlan -Entities $allEntities -WebResources $allWebs -OptionSets $plan.OptionSets -SiteMaps $plan.SiteMaps `
        -Dashboards $plan.Dashboards -AppModules $plan.AppModules -AppSettings $plan.AppSettings -ApplicationRibbon:([bool]$plan.ApplicationRibbon) -ChunkSize $ChunkSize)
    if ($chunks.Count -eq 0) { throw 'Nothing to publish: refusing to build an empty ParameterXml (an empty publish must never widen into a publish-all).' }
    Write-Host "Scoped publish of ${SolutionUniqueName}: $($allEntities.Count) entities, $($allWebs.Count) web resources, $(@($plan.OptionSets).Count) option sets, $(@($plan.SiteMaps).Count) site maps, $(@($plan.Dashboards).Count) dashboards, $(@($plan.AppModules).Count) app modules, $(@($plan.AppSettings).Count) app settings, application ribbon: $([bool]$plan.ApplicationRibbon), in $($chunks.Count) request(s)."
    $envUrl = $Context.Api -replace '/api/data/v[0-9.]+$', ''
    # Components published in THIS run that are not in the solution (a caller's -Extra* lists) are lost by a plain -PublishOnly resume,
    # so the resume command carries them. FORMAT: one single-quoted, comma-separated string ('g1,g2'). Bash and PowerShell both deliver it
    # as ONE argument and Import-SolutionScoped.ps1 splits it; a quoted list ('g1','g2') would reach it from bash as g1,g2 anyway.
    $inPlanWebs = @($plan.WebResources | ForEach-Object { "$_".Trim('{', '}').ToLowerInvariant() })
    $inPlanEnts = @($plan.Entities | ForEach-Object { "$_".ToLowerInvariant() })
    $outsideWebs = @($ExtraWebResources | Where-Object { $_ -and ($inPlanWebs -notcontains "$_".Trim('{', '}').ToLowerInvariant()) } | Select-Object -Unique)
    $outsideEnts = @($ExtraEntities | Where-Object { $_ -and ($inPlanEnts -notcontains "$_".ToLowerInvariant()) } | Select-Object -Unique)
    $resume = "pwsh scripts/Import-SolutionScoped.ps1 -EnvironmentUrl $envUrl -SolutionUniqueName $SolutionUniqueName -PublishOnly" + $(if ($AllowWorkflows) { ' -AllowWorkflows' } else { '' })
    $outsideNote = ''
    if ($outsideWebs.Count -gt 0) { $resume += " -ExtraWebResources '$($outsideWebs -join ',')'"; $outsideNote += " web resources outside the solution: $($outsideWebs -join ', ');" }
    if ($outsideEnts.Count -gt 0) { $resume += " -ExtraEntities '$($outsideEnts -join ',')'"; $outsideNote += " entities outside the solution: $($outsideEnts -join ', ');" }
    # The pending-collateral rule applies to the resume too: it carries the opt-in flag ONLY if this run had it. $resumeBase is the command without the flag.
    $resumeBase = $resume
    if ($AllowPendingCollateral) { $resume += ' -AllowPendingCollateral' }
    if (-not $SkipCollateralCheck) {
        $rows = @(Get-SolutionComponentRows -Context $Context -SolutionUniqueName $SolutionUniqueName)
        $own = @($rows | Where-Object { $_ } | ForEach-Object { $_.objectid })
        $inApps = @($rows | Where-Object { $_ -and [int]$_.componenttype -eq 80 } | ForEach-Object { "$($_.objectid)" })
        $parents = @($plan.AppSettings | Where-Object { $_ } | ForEach-Object { Get-AppSettingParent -Context $Context -AppSettingId $_ })
        $ownedEntities = @(Get-FullyOwnedEntityNames -Context $Context -Rows $rows)
        Resolve-PendingCollateral -Context $Context -Entities (@($plan.Entities) + $ExtraEntities) -ParentApps $parents -OwnIds $own -OwnedEntities $ownedEntities -InSolutionApps $inApps `
            -Allow:$AllowPendingCollateral -RerunCommand $resumeBase | Out-Null
    }
    $n = 0
    foreach ($c in $chunks) {
        $n++
        try {
            $xml = New-PublishParameterXml -Entities $c.Entities -WebResources $c.WebResources -OptionSets $c.OptionSets -SiteMaps $c.SiteMaps `
                -Dashboards $c.Dashboards -AppModules $c.AppModules -ApplicationRibbon:([bool]$c.ApplicationRibbon)
            Invoke-PublishXml -Context $Context -ParameterXml $xml
            # Read this request back before the next one goes out.
            $pending = @(Test-PublishedReadBack -Context $Context -Plan ([pscustomobject]@{
                        WebResources = $c.WebResources; AppModules = $c.AppModules; AppSettings = $c.AppSettings; Entities = $c.Entities; SiteMaps = $c.SiteMaps; Dashboards = $c.Dashboards }))
            if ($pending.Count -gt 0) { throw "still unpublished after PublishXml: $($pending -join ', ')" }
        } catch {
            throw "Publish request $n of $($chunks.Count) for $SolutionUniqueName failed: $($_.Exception.Message). The solution is already imported; resume WITHOUT re-importing: $resume$(if ($outsideNote) { " (this run also published$outsideNote the resume command republishes them)" })"
        }
    }
    if ($AllowWorkflows -and @($plan.Workflows).Count -gt 0) {
        $inactive = @(Test-WorkflowsActivated -Context $Context -WorkflowIds $plan.Workflows)
        if ($inactive.Count -gt 0) { throw "Workflow(s) not activated after the import (statecode must be 1): $($inactive -join ', '). Import with --activate-plugins and re-check; resume the publish with: $resume" }
    }
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
        [switch]$IncludeControlHostEntities,
        [switch]$AllowWorkflows,
        [switch]$AllowPendingCollateral
    )
    $ExtraWebResources = @(ConvertTo-ExtraList -Values $ExtraWebResources -Kind WebResource)
    $ExtraEntities = @(ConvertTo-ExtraList -Values $ExtraEntities -Kind Entity)
    if ($ImportArgs | Where-Object { $_ -match '^(--publish-changes|-pc)$' }) { throw '--publish-changes is a tenant-wide publish and is not allowed.' }
    if (-not $PacExe) {
        $PacExe = (Get-Command pac -CommandType Application -ErrorAction SilentlyContinue |
            Where-Object { $_.Extension -in '.cmd', '.exe', '.bat' } | Select-Object -First 1).Source
    }
    if (-not $PacExe) { throw 'pac CLI not found: need pac.cmd or pac.exe on PATH.' }
    if (-not $Context) { $Context = Get-DataverseApiContext -EnvironmentUrl $EnvironmentUrl }

    # PRE-FLIGHT (before anything is imported): an import must never be left unpublished.
    $rerun = "pwsh scripts/Import-SolutionScoped.ps1 -EnvironmentUrl $EnvironmentUrl -ZipPath '$ZipPath' -SolutionUniqueName $SolutionUniqueName" +
        $(if ($AllowWorkflows) { ' -AllowWorkflows' } else { '' }) +
        $(if ($ExtraWebResources.Count -gt 0) { " -ExtraWebResources '$($ExtraWebResources -join ',')'" } else { '' }) +
        $(if ($ExtraEntities.Count -gt 0) { " -ExtraEntities '$($ExtraEntities -join ',')'" } else { '' })
    Invoke-ImportPreflight -Context $Context -ZipPath $ZipPath -SolutionUniqueName $SolutionUniqueName -ExtraEntities $ExtraEntities -AllowWorkflows:$AllowWorkflows `
        -AllowPendingCollateral:$AllowPendingCollateral -RerunCommand $rerun | Out-Null
    if ($AllowWorkflows -and ($ImportArgs -notcontains '--activate-plugins')) { $ImportArgs = @($ImportArgs) + '--activate-plugins' }

    & $PacExe solution import --environment $EnvironmentUrl --path $ZipPath @ImportArgs
    if ($LASTEXITCODE -ne 0) { throw "pac solution import failed ($LASTEXITCODE)." }
    return Publish-SolutionComponents -Context $Context -SolutionUniqueName $SolutionUniqueName `
        -ExtraWebResources $ExtraWebResources -ExtraEntities $ExtraEntities -IncludeControlHostEntities:$IncludeControlHostEntities -SkipCollateralCheck -AllowWorkflows:$AllowWorkflows -AllowPendingCollateral:$AllowPendingCollateral
}
