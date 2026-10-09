#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Replaces the platform's subgrid "+ New" under a SECURE project / matter / work assignment with Spaarke's BFF-backed
    "New <thing>" commands (unified-access-control-r2 task 147 r1; owner round 28 item 2, "E2"). Dry run by default;
    -Apply writes; -Verify checks. Idempotent.

.DESCRIPTION
    WHY. The platform "+ New" on a subgrid creates the child AS THE USER, owned by the user - under a secure record that
    makes it readable by the user's whole business unit until the reconcile job re-owns it. Owner round 28: "in-product
    native creates of a child under a secure parent (subgrid '+ New', quick create, form New with parent prefilled) are
    replaced by BFF-backed commands in the existing ribbon command-script pattern; enable rules read sprk_issecure via
    Xrm.WebApi; a read failure hides the native command and shows the BFF one". The Create privilege is NOT removed.

    SURFACES (task 147 census + read-only live inventory of EVERY active main form of spaarkedev1, 2026-10-04):
      - Subgrids on the three root main forms - project: sprk_todo, sprk_event, sprk_document, sprk_invoice,
        sprk_analysis, sprk_budget; matter: sprk_analysis, sprk_budget, sprk_communication, sprk_invoice, sprk_reportcard;
        work assignment: sprk_document, sprk_event.
      - Subgrids on CHILD main forms (r1c) - a child created there is filed THROUGH the host under its secure record:
        analysis, budget, document, event: sprk_todo; document: sprk_analysis; invoice: sprk_communication, sprk_document,
        sprk_event, sprk_todo. The script hides the platform "+ New" on every non-root host (its secure ancestor is not
        known client-side) and shows the BFF command, which files the new row under the host and lets the BFF decide.
      - Party hosts (contact, organization: sprk_todo) keep the platform "+ New": a party is never an ownership parent.
      Every one of those subgrids' "+ New" is the platform command Mscrm.AddNewRecordFromSubGridStandard (the
      parent-prefilled create).
        * sprk_analysis is ALREADY covered: AnalysisRibbons hides its native "+ New" outright and shows "New Analysis"
          (the analysis wizard, BFF-backed since task 147 r1). Not touched here.
        * contact / email / sprk_kpiassessment / sprk_organization / sprk_project / sprk_matter /
          sprk_externalrecordaccess subgrids are not secure-child tables (no ownership rule files them under the root).
      The dry run and -Verify re-read that inventory live and FAIL on a subgrid of an ownership-child table this change
      does not serve (a new form or subgrid since 2026-10-04).
      - Quick create: IsQuickCreateEnabled = false on every child table (live), so no subgrid opens a quick-create form;
        nothing to replace. The script reports it, and FAILS -Verify if any child table turns it on.
      - Form "New" / "Save & New" on a child's own form: creates an UNFILED record (no parent prefilled), owned per the
        unfiled rule by the reconcile pass - not a secure-parent create. Not replaced.

    WHAT -Apply DOES, in order (every step a live write - a manual gate, run by the main session):
      1. Web resource sprk_/scripts/secure_child_ribbon.js <- src/client/webresources/js/sprk_secure_child_ribbon.js
         (created or updated) and published. Its two helper libraries (sprk_/scripts/bff_auth.js,
         sprk_/scripts/assignedaccess_postsave.js) must already exist (task 142) - checked, never written here.
      2. For each served child table, merges the ONE template into the table's RibbonDiff.xml inside -ExportDir (an
         UNPACKED fresh export of the dedicated ribbon solution -SolutionUniqueName, holding the nine served tables ribbon-only,
         exported per .claude/skills/ribbon-edit/SKILL.md - never SpaarkeCore) with
         infrastructure/dataverse/ribbon/SecureChildRibbons/Merge-SecureChildRibbon.ps1. Before anything is written, the
         export is checked against the environment (infrastructure/dataverse/ribbon/Test-RibbonExportCurrent.ps1): if it
         lacks any unmanaged ribbon command, rule, custom / hide action or label the environment holds for an exported
         table - an export taken before another ribbon import, e.g. task 180's Create-privilege rules - nothing is written.
      3. Packs (pac solution pack) and imports the solution into -EnvironmentUrl (pac solution import --environment), then
         publishes all customizations.

    THE DRY RUN (default; zero writes) reads, per served table:
      - the LIVE Mscrm.AddNewRecordFromSubGridStandard (RetrieveEntityRibbon) and compares it with the template's copy:
        any drift (Microsoft changed the platform definition) is FAIL - the copy must be refreshed before an import;
      - whether the secure rule and the "New" button are live already;
      - IsQuickCreateEnabled;
      and whether the three web resources exist (and whether the deployed script matches the repository file).
      With -ExportDir it also runs the merge into a scratch copy and prints the before / after command lists.

.PARAMETER EnvironmentUrl
    e.g. https://spaarkedev1.crm.dynamics.com
.PARAMETER ExportDir
    The unpacked export of -SolutionUniqueName (folder holding Entities/<table>/RibbonDiff.xml). Required for -Apply.
    It must be current: an export missing anything the environment holds is refused (export again right before -Apply).
.PARAMETER SolutionUniqueName
    The dedicated ribbon solution. Default SpaarkeSecureChildRibbons.
.PARAMETER Apply
    Write mode. Without it the script never writes.
.PARAMETER Verify
    Read-only check with a pass/fail exit code (1 names each gap). Cannot be combined with -Apply.
.EXAMPLE
    & ./scripts/Deploy-SecureChildNewCommands.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com
.EXAMPLE
    & ./scripts/Deploy-SecureChildNewCommands.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -ExportDir C:\wt\securechild-export -Apply
.EXAMPLE
    & ./scripts/Deploy-SecureChildNewCommands.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Verify
.NOTES
    Order: the BFF carrying task 147 r1 (POST /api/v1/child-records/sprk_budget) and
    scripts/Set-ChildRecordCreatorPersonSchema.ps1 -Apply (gate G147-5: sprk_budget, sprk_kpiassessment and sprk_billingevent gain sprk_createdbyperson) BEFORE this
    script's -Apply. Auth: the operator's own az CLI identity (System Administrator). pac CLI with an auth profile that can
    reach -EnvironmentUrl for -Apply: the import names -EnvironmentUrl explicitly (--environment), never pac's active
    profile. No secrets.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EnvironmentUrl,
    [string]$ExportDir,
    [string]$SolutionUniqueName = 'SpaarkeSecureChildRibbons',
    [switch]$Apply,
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
if ($Apply -and $Verify) { throw '-Apply and -Verify are separate modes; run -Apply first, then -Verify.' }
if ($Apply -and -not $ExportDir) { throw '-Apply needs -ExportDir: the unpacked fresh export of the ribbon solution.' }

$Api = "$EnvironmentUrl/api/data/v9.2"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$Template = Join-Path $RepoRoot 'infrastructure/dataverse/ribbon/SecureChildRibbons/secure-child-new.template.xml'
$Merger = Join-Path $RepoRoot 'infrastructure/dataverse/ribbon/SecureChildRibbons/Merge-SecureChildRibbon.ps1'
$ScriptFile = Join-Path $RepoRoot 'src/client/webresources/js/sprk_secure_child_ribbon.js'
$ScriptName = 'sprk_/scripts/secure_child_ribbon.js'
$HelperNames = @('sprk_/scripts/bff_auth.js', 'sprk_/scripts/assignedaccess_postsave.js')
$NativeCommandId = 'Mscrm.AddNewRecordFromSubGridStandard'

# The child tables this change serves (Spaarke.SecureChild.Ribbon.TABLES; Merge-SecureChildRibbon.ps1 -Entity).
$Tables = @('sprk_todo', 'sprk_event', 'sprk_invoice', 'sprk_reportcard', 'sprk_document', 'sprk_communication', 'sprk_budget',
    'sprk_kpiassessment', 'sprk_billingevent')

$token = az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv
if (-not $token) { throw 'No az CLI token for the environment - run az login as an administrator of it.' }
$Headers = @{ Authorization = "Bearer $token"; Accept = 'application/json'; 'OData-MaxVersion' = '4.0'; 'OData-Version' = '4.0' }

$Failures = [System.Collections.Generic.List[string]]::new()
function Fail([string] $text) { $Failures.Add($text); Write-Host "  FAIL  $text" -ForegroundColor Red }
function Ok([string] $text) { Write-Host "  ok    $text" }
function Info([string] $text) { Write-Host "  ..    $text" }

function Get-LiveRibbon([string] $entity) {
    $r = Invoke-RestMethod -Headers $Headers -Uri "$Api/RetrieveEntityRibbon(EntityName='$entity',RibbonLocationFilter=Microsoft.Dynamics.CRM.RibbonLocationFilters'All')"
    $bytes = [Convert]::FromBase64String($r.CompressedEntityXml)
    $zip = [System.IO.Compression.ZipArchive]::new([System.IO.MemoryStream]::new($bytes))
    $reader = [System.IO.StreamReader]::new($zip.Entries[0].Open())
    try { [xml] $reader.ReadToEnd() } finally { $reader.Dispose(); $zip.Dispose() }
}

# The native command as the template carries it, minus the one rule this change adds - the comparison baseline.
function Get-NativeShape([System.Xml.XmlNode] $command, [string] $entity) {
    if (-not $command) { return $null }
    $rules = @($command.SelectNodes(".//*[local-name()='EnableRules']/*[local-name()='EnableRule']") |
        ForEach-Object { $_.GetAttribute('Id') } | Where-Object { $_ -ne "sprk.SecureChild.$entity.NativeNewAllowed.EnableRule" })
    $display = @($command.SelectNodes(".//*[local-name()='DisplayRules']/*[local-name()='DisplayRule']") | ForEach-Object { $_.GetAttribute('Id') })
    $action = $command.SelectSingleNode(".//*[local-name()='JavaScriptFunction']")
    $params = @($action.SelectNodes("*") | ForEach-Object { $_.GetAttribute('Value') })
    return ('rules=' + ($rules -join ',') + ' | display=' + ($display -join ',') + ' | action=' + $action.GetAttribute('FunctionName') +
        '@' + $action.GetAttribute('Library') + '(' + ($params -join ',') + ')')
}

function Get-WebResource([string] $name) {
    $q = "$Api/webresourceset?`$select=webresourceid,name,content&`$filter=name eq '$name'"
    (Invoke-RestMethod -Headers $Headers -Uri $q).value | Select-Object -First 1
}

$mode = if ($Apply) { 'APPLY' } elseif ($Verify) { 'VERIFY' } else { 'DRY RUN (no writes)' }
Write-Host "Secure-record child New commands - $EnvironmentUrl - $mode"

# ── Web resources ──────────────────────────────────────────────────────────────────────────────────────────────
Write-Host 'Web resources'
foreach ($helper in $HelperNames) {
    if (Get-WebResource $helper) { Ok "$helper exists" } else { Fail "$helper is missing (task 142 deploys it) - the commands load it first" }
}
$repoContent = [Convert]::ToBase64String([System.IO.File]::ReadAllBytes($ScriptFile))
$live = Get-WebResource $ScriptName
if (-not $live) {
    if ($Verify) { Fail "$ScriptName is missing" } else { Info "$ScriptName is missing (-Apply creates it)" }
} elseif ($live.content -ne $repoContent) {
    if ($Verify) { Fail "$ScriptName differs from $ScriptFile" } else { Info "$ScriptName differs from the repository file (-Apply updates it)" }
} else { Ok "$ScriptName matches the repository file" }

# ── Live subgrid inventory (r1c): every ownership-child subgrid on an active main form is served ──────────────────
# The ownership-child tables (RecordOwnershipResolver.OwnershipParentEntities without the four roots); sprk_analysis is
# served by AnalysisRibbons; party hosts keep the platform "+ New".
$OwnershipChildren = @('sprk_agreement', 'sprk_analysis', 'sprk_billingevent', 'sprk_budget', 'sprk_communication',
    'sprk_document', 'sprk_event', 'sprk_invoice', 'sprk_kpiassessment', 'sprk_memo', 'sprk_reportcard', 'sprk_spendsignal',
    'sprk_spendsnapshot', 'sprk_todo')
$PartyHosts = @('contact', 'sprk_organization', 'account')
Write-Host 'Subgrids of ownership-child tables on active main forms'
$formUri = "$Api/systemforms?`$select=name,objecttypecode,formxml&`$filter=type eq 2 and formactivationstate eq 1 and contains(formxml,'TargetEntityType')"
$forms = @()
while ($formUri) {
    $page = Invoke-RestMethod -Headers ($Headers + @{ Prefer = 'odata.maxpagesize=100' }) -Uri $formUri
    $forms += $page.value
    $formUri = $page.'@odata.nextLink'
}
foreach ($form in $forms) {
    if (-not $form.formxml) { continue }
    [xml] $x = $form.formxml
    foreach ($control in $x.SelectNodes('//control')) {
        $child = $control.parameters.TargetEntityType
        if (-not $child -or $OwnershipChildren -notcontains $child) { continue }
        $where = "$($form.objecttypecode) '$($form.name)' -> $child"
        if ($PartyHosts -contains $form.objecttypecode) { Ok "$where (party host: platform + New kept)" }
        elseif ($child -eq 'sprk_analysis') { Ok "$where (AnalysisRibbons hides the platform + New)" }
        elseif ($Tables -contains $child) { Ok "$where (served)" }
        else { Fail "$where is NOT served: its platform + New would create a user-owned child under or through a secure record" }
    }
}

# ── The export must be CURRENT (unified-access-control-r2 task 180, verifier K1) ───────────────────────────────
# -ExportDir is supplied by the caller. An unmanaged ribbon import replaces each exported table's WHOLE ribbon
# customisation, so an export taken before another ribbon import (task 180's sprk.CreatePrivilege.* rules, the Access
# group, ...) would silently delete it. Every unmanaged command, rule, custom / hide action and label the environment
# holds for each exported table must be in the export; anything missing fails here, and -Apply then writes nothing.
if ($ExportDir -and -not $Verify) {
    Write-Host "Export currency ($ExportDir against $EnvironmentUrl)"
    $stale = @(& (Join-Path $RepoRoot 'infrastructure/dataverse/ribbon/Test-RibbonExportCurrent.ps1') -EnvironmentUrl $EnvironmentUrl `
        -Token $token -UnpackedDir $ExportDir)
    if ($stale.Count -eq 0) { Ok 'the export holds every live unmanaged ribbon customisation of its tables' }
    foreach ($line in $stale) { Fail "stale export - importing it would delete $line (export the solution again, then re-run)" }
}

# ── Per table: platform drift, live state, quick create ────────────────────────────────────────────────────────
[xml] $templateXml = (Get-Content -Raw -LiteralPath $Template)
$templateNative = $templateXml.SelectSingleNode("//*[local-name()='CommandDefinition' and @Id='$NativeCommandId']")
$expectedShape = Get-NativeShape $templateNative '{{entity}}'

foreach ($table in $Tables) {
    Write-Host $table
    $ribbon = Get-LiveRibbon $table
    $native = $ribbon.SelectSingleNode("//CommandDefinition[@Id='$NativeCommandId']")
    $liveShape = Get-NativeShape $native $table
    if ($liveShape -ne $expectedShape) {
        Fail "$NativeCommandId on $table is not the template's platform copy - refresh the template before an import. Live: $liveShape"
    } else { Ok "$NativeCommandId matches the template's platform copy" }

    $ruleLive = [bool] ($native -and $native.SelectSingleNode(".//EnableRule[@Id='sprk.SecureChild.$table.NativeNewAllowed.EnableRule']"))
    $buttonLive = [bool] $ribbon.SelectSingleNode("//Button[@Id='sprk.SecureChild.$table.SubGrid.New']")
    if ($Verify) {
        if ($ruleLive) { Ok 'the platform + New carries the secure rule' } else { Fail "the platform + New on $table does not carry the secure rule" }
        if ($buttonLive) { Ok 'the New command is live' } else { Fail "sprk.SecureChild.$table.SubGrid.New is not live" }
    } else {
        Info "secure rule live: $ruleLive; New command live: $buttonLive"
    }

    $meta = Invoke-RestMethod -Headers $Headers -Uri "$Api/EntityDefinitions(LogicalName='$table')?`$select=IsQuickCreateEnabled"
    if ($meta.IsQuickCreateEnabled) {
        Fail "$table has quick create ON - a subgrid would open a quick-create form this change does not cover"
    } else { Ok 'quick create is off' }

    if ($ExportDir -and -not $Verify) {
        $exported = Get-ChildItem -LiteralPath (Join-Path $ExportDir 'Entities') -Directory |
            Where-Object { $_.Name -ieq $table } | Select-Object -First 1
        if (-not $exported) { Fail "$table is not in the export at $ExportDir (add it to $SolutionUniqueName, ribbon only)"; continue }
        $diff = Join-Path $exported.FullName 'RibbonDiff.xml'
        $out = if ($Apply) { $diff } else { Join-Path ([System.IO.Path]::GetTempPath()) "securechild-$table-RibbonDiff.xml" }
        & $Merger -ExportedRibbonDiff $diff -Entity $table -Out $out
    }
}

if ($Failures.Count -gt 0 -and $Apply) {
    throw "Nothing was written: $($Failures.Count) check(s) failed. $($Failures -join ' ; ')"
}

# ── -Apply: web resource, pack, import, publish ────────────────────────────────────────────────────────────────
if ($Apply) {
    Write-Host 'Applying'
    $body = @{ name = $ScriptName; displayname = 'Secure-record child New commands'; webresourcetype = 3; content = $repoContent } | ConvertTo-Json
    $writeHeaders = $Headers.Clone(); $writeHeaders['Content-Type'] = 'application/json'
    if ($live) {
        Invoke-RestMethod -Method Patch -Headers $writeHeaders -Uri "$Api/webresourceset($($live.webresourceid))" -Body $body | Out-Null
        Ok "$ScriptName updated"
    } else {
        Invoke-RestMethod -Method Post -Headers $writeHeaders -Uri "$Api/webresourceset" -Body $body | Out-Null
        Ok "$ScriptName created"
    }

    $zip = Join-Path ([System.IO.Path]::GetTempPath()) "$SolutionUniqueName.zip"
    pac solution pack --zipfile $zip --folder $ExportDir --packagetype Unmanaged
    if ($LASTEXITCODE -ne 0) { throw 'pac solution pack failed.' }
    # The import names its target explicitly (task 147 r1c-v1, verifier item 5): without --environment pac imports into its
    # ACTIVE auth profile's environment, which need not be -EnvironmentUrl - the web resource above and the import would
    # then land in two different environments.
    pac solution import --environment $EnvironmentUrl --path $zip --publish-changes
    if ($LASTEXITCODE -ne 0) { throw 'pac solution import failed.' }
    Invoke-RestMethod -Method Post -Headers $writeHeaders -Uri "$Api/PublishAllXml" -Body '{}' | Out-Null
    Ok 'imported and published - now run -Verify, then check the forms by hand (below)'
    Write-Host @'
  Manual check on each root main form, and on one child form (an event of a secure record: its To Do subgrid):
    secure record   - the subgrid shows "New <thing>", NOT the platform "+ New"; the command opens the wizard / compose
                      page / creates the budget, and the new row is owned by the Secure Record Owners team;
    ordinary record - the platform "+ New" shows, "New <thing>" does not;
    flag unreadable - (a user without read on sprk_issecure) the platform "+ New" is hidden.
'@
}

if ($Failures.Count -gt 0) {
    Write-Host "$($Failures.Count) check(s) failed." -ForegroundColor Red
    if ($Verify) { exit 1 }
} else {
    Write-Host 'All checks passed.'
}
