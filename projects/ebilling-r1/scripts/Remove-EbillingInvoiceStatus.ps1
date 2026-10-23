<#
.SYNOPSIS
    eBilling R1 task 015 - retires sprk_invoicestatus (D-41, FR-06): inventory, data check, form / view / grid clean-up, and the
    guarded delete of the column and its global option set.

.DESCRIPTION
    The invoice status lives on statecode / statuscode (task 012 values, InvoiceStatusCodes). sprk_invoicestatus (global option
    set sprk_invoicestatus: 100000000 ToReview, 100000001 Reviewed) is retired. Owner approval: D-97 (2026-10-17).

    This script lives under projects/ (history), not scripts/ebilling/: the InvoiceStatusRetirementGuardTests ArchTest fails on
    the column's name anywhere under src/, tests/ and scripts/, and this script must name it.

    Each phase is a switch; several may be given in one run and they run in the order below. Without any switch the script is
    READ-ONLY: it prints the inventory and the verdict for -Delete.

      (none)                 Inventory: the column and option set, rows holding a value (ids), invoices by statecode /
                             statuscode, RetrieveDependenciesForDelete on both (classified), AI skill configs, forms / views /
                             personal views / workflows / charts / grid configurations that mention the column. Writes nothing.
      -Migrate               FR-06 data step. Every row with a value must map to In Review (ToReview -> In Review,
                             Reviewed -> In Review). A row already at statuscode In Review (1, statecode 0) needs no write.
                             A row at another statuscode, or holding a value other than the two known ones, REFUSES (exit 2):
                             moving it is a lifecycle decision for the owner, not a migration. Logs every row id.
      -FormsAndViews         Removes the column from the Invoice main form (body cell + the RecordHeader layoutJson entries)
                             and the quick-create form, moves the 'Invoice - Matter Context' view's status column to
                             statuscode, and the 'Invoice Review Queue Projects' filter from ToReview to statuscode In Review
                             (1). Publishes sprk_invoice only (never PublishAll).
      -GridConfig            Moves the outside-counsel invoices grid (sprk_gridconfiguration 3ff4102c, SPA-r3 / uac-r2 owned)
                             from the column to statuscode (fetch, layout, column settings). Run it ONLY right after a BFF
                             whose external invoices allow-list admits statuscode is live: before that the BFF refuses the
                             grid's fetch; after the column delete without it Dataverse refuses it. NEEDS
                             -BffDeployAttestation (checked against that commit's source, below). The write sends If-Match
                             with the row's etag, so a row changed since it was read is not overwritten (HTTP 412): the row is
                             read again; if it no longer names the column the run reports "already applied" and exits 0,
                             otherwise it is refused (exit 2). The replace is case-sensitive: a spelling it leaves behind
                             (other letter case) is refused before any write.
                             Runs even when the column is already gone (a grid row can still be moved then). Cannot be
                             combined with -Delete in one run: the grid blocker would clear partway through the run.
      -Delete                DESTRUCTIVE; needs -OwnerApproval, -BffDeployAttestation and -SpaDeployAttestation. Re-runs the inventory and REFUSES (exit 2, nothing deleted) on
                             any blocker: a row not at In Review, a form / view / grid configuration / personal view / workflow
                             / chart still naming the column, or a dependency other than the platform's Copilot form-fill
                             opt-out aiskillconfig. Then: delete that aiskillconfig -> RetrieveDependenciesForDelete on the
                             attribute must be 0 (printed) -> delete the attribute -> the option set's dependencies must be 0
                             (printed) -> delete the global option set -> publish sprk_invoice -> verify both read 404.

    -WhatIf: writes nothing, but it is NOT offline. After the git pre-flight (attestation checks; git only), -WhatIf still
    performs READ-ONLY GETs against Dataverse (metadata, the inventory queries, RetrieveDependenciesForDelete, the grid row) and
    signs in to get a token. The same applies to the no-switch report run. The pre-flight runs first, so a refused attestation
    stops the run before any Dataverse call, with or without -WhatIf.

    Idempotent: every step skips work already done; with the column already gone the script reports so and exits 0.
    This script does not edit src/dataverse/solutions/SpaarkeMaster/** (task 015 keeps the source in step by hand; DEF-021).

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL, else dev. Writes refuse an environment that came from
    $env:DATAVERSE_URL unless it is spaarkedev1 (pass -EnvironmentUrl explicitly). Dev only in R1.

.PARAMETER BffDeployAttestation
    A commit sha: the commit the BFF deployed to dev was built from. Mandatory for -GridConfig and -Delete. The script reads
    that commit's source (git show <sha>:<path>) and refuses unless it holds what the phase needs (a content check, not
    ancestry: a squash merge breaks ancestry checks, and project CLAUDE.md section 6 requires the check by content).
      -GridConfig : Infrastructure/DI/ExternalAccessModule.cs lists "statuscode" in the invoices ReadableColumns.
      -Delete     : the same, and Services/Finance/FinanceBaseValues.cs, FinanceRollupService.cs and InvoiceReviewService.cs
                    contain no sprk_invoicestatus, and FinanceBaseValues.cs contains InvoiceStatusCodeColumn (015's readers).
                    Then a content scan of the whole BFF source: `git grep -il sprk_invoicestatus <sha> -- src/server` must
                    find nothing, except that ExternalAccessModule.cs may hold the one exact transitional line
                    ("sprk_invoicestatus", // transitional (015/D-97): remove after the dev column delete). Any other file
                    (FinanceSummaryService.cs, FinancialCalculationToolHandler.cs, ...), any other line, or a second copy of
                    the marker line is refused. Keep 015's marker line when merging another branch's allow-list text.
    An unknown sha fails first (git cat-file -e).

.PARAMETER SpaDeployAttestation
    A commit sha: the commit the external SPA deployed to dev was built from. Mandatory for -Delete. Refuses unless
    src/client/external-spa/src/components/detail/RecordDetailModal.tsx and RecordDetailFields.tsx at that commit contain no
    sprk_invoicestatus (the SPA reads statuscode). Then `git grep -il sprk_invoicestatus <sha> -- src/client/external-spa/src`
    must find nothing (tests and comments included).

.EXAMPLE
    pwsh projects/ebilling-r1/scripts/Remove-EbillingInvoiceStatus.ps1
    pwsh projects/ebilling-r1/scripts/Remove-EbillingInvoiceStatus.ps1 -Migrate -FormsAndViews -WhatIf
    pwsh projects/ebilling-r1/scripts/Remove-EbillingInvoiceStatus.ps1 -Migrate -FormsAndViews
    pwsh projects/ebilling-r1/scripts/Remove-EbillingInvoiceStatus.ps1 -GridConfig -BffDeployAttestation <bff-sha> -WhatIf   # after the BFF + SPA deploy
    pwsh projects/ebilling-r1/scripts/Remove-EbillingInvoiceStatus.ps1 -Delete -OwnerApproval 'D-97 2026-10-17 owner' -BffDeployAttestation <bff-sha> -SpaDeployAttestation <spa-sha> -WhatIf

.NOTES
    Project: ebilling-r1, task 015. Exit codes: 0 = done / report, 1 = error, 2 = refused (blocker or unexpected state).
#>

using module ..\..\..\scripts\ebilling\EbillingSchema.psm1

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$EnvironmentUrl = $(if ($env:DATAVERSE_URL) { $env:DATAVERSE_URL } else { 'https://spaarkedev1.crm.dynamics.com' }),
    [switch]$Migrate,
    [switch]$FormsAndViews,
    [switch]$GridConfig,
    [switch]$Delete,
    [string]$OwnerApproval,
    [string]$BffDeployAttestation,
    [string]$SpaDeployAttestation
)

$ErrorActionPreference = 'Stop'
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$DryRun = [bool]$WhatIfPreference
$Table = 'sprk_invoice'
$Column = 'sprk_invoicestatus'
$OptionSetName = 'sprk_invoicestatus'
$KnownValues = @{ 100000000 = 'ToReview'; 100000001 = 'Reviewed' }
$InReview = 1          # InvoiceStatusCodes.InReview (statecode 0)

$MainForm = '93aa1c69-0406-f111-8406-7c1e525abd8b'
$QuickCreateForm = 'c5e5807d-e20c-f111-8342-7c1e520aa4df'
$KnownForms = @{ $MainForm = 'Invoice main form'; $QuickCreateForm = 'Invoice quick create form' }
$MatterContextView = 'b9f6d045-9a5e-f111-ab0c-7c1e521545d7'
$ReviewQueueView = '1ad5e8f3-4408-f111-8406-7c1e525abd8b'
$KnownViews = @{ $MatterContextView = 'Invoice - Matter Context'; $ReviewQueueView = 'Invoice Review Queue Projects' }
$InvoiceGridConfig = '3ff4102c-1092-f111-b8dc-7ced8ddc4a05'

# ---------------------------------------------------------------------------------------------------------------
# Pre-flight: deploy attestations (git only; runs before any Dataverse call, and also under -WhatIf)
# ---------------------------------------------------------------------------------------------------------------
# The dev BFF and external SPA are deployed only by SPA-r3, from its branch, so this script cannot see what is live. The operator
# attests the commit each was built from; the script checks that commit's SOURCE (content, not ancestry: a squash merge breaks
# ancestry checks). Project CLAUDE.md section 6.

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$BffModulePath = 'src/server/api/Sprk.Bff.Api/Infrastructure/DI/ExternalAccessModule.cs'
$FinanceDir = 'src/server/api/Sprk.Bff.Api/Services/Finance'
$BffFinanceReaders = @("$FinanceDir/FinanceBaseValues.cs", "$FinanceDir/FinanceRollupService.cs", "$FinanceDir/InvoiceReviewService.cs")
$SpaDetailDir = 'src/client/external-spa/src/components/detail'
$SpaReaders = @("$SpaDetailDir/RecordDetailModal.tsx", "$SpaDetailDir/RecordDetailFields.tsx")

# Throws an EscalationException unless $Sha names a commit in this repository. `git cat-file -e` first, so an unknown sha fails clearly.
function Assert-CommitExists([string]$Sha, [string]$Parameter) {
    if ($Sha -notmatch '^[0-9a-fA-F]{7,40}$') { throw [EscalationException]::new("$Parameter '$Sha' is not a commit sha (7 to 40 hex characters). Nothing was changed.") }
    $null = & git -C $RepoRoot cat-file -e "${Sha}^{commit}" 2>&1
    if ($LASTEXITCODE -ne 0) { throw [EscalationException]::new("$Parameter '$Sha' is not a commit in this repository (git cat-file -e failed): fetch it, or pass the right sha. Nothing was changed.") }
}

# The text of $Path at commit $Sha, or $null when the commit has no such file.
function Get-FileAtCommit([string]$Sha, [string]$Path) {
    $out = & git -C $RepoRoot show "${Sha}:${Path}" 2>$null
    if ($LASTEXITCODE -ne 0) { return $null }
    return ($out -join "`n")
}

# True when the invoices module's ReadableColumns (the HashSet that holds "sprk_invoiceid") lists "statuscode". Comments are removed
# first, so a comment that spells the word does not count.
function Test-InvoicesAllowListHasStatuscode([string]$ModuleSource) {
    $m = [regex]::Match($ModuleSource, '(?s)ReadableColumns\s*=\s*new\s+HashSet<string>\s*\{(?<body>[^}]*"sprk_invoiceid"[^}]*)\}')
    if (-not $m.Success) { return $false }
    $body = [regex]::Replace($m.Groups['body'].Value, '//[^\r\n]*', '')
    return ($body -match '"statuscode"')
}

# The ONE line that may still name the column in the BFF source at a -Delete attestation (ExternalAccessModule.cs; D-97 transition
# window). Same text as InvoiceStatusRetirementGuardTests.TransitionalExemptions[0]; compared after trimming.
$TransitionalModuleLine = '"sprk_invoicestatus", // transitional (015/D-97): remove after the dev column delete'

# Content scan of the whole source tree a deployed artifact was built from (git grep over the attested commit, not a list of known
# readers: the specific checks below name three files and missed FinanceSummaryService / FinancialCalculationToolHandler, which
# 9721819ed still has). Refuses on ANY file that names the column, case-insensitively, except the one exact transitional line in
# ExternalAccessModule.cs (at most once). Throws an EscalationException; returns nothing.
function Assert-NoRetiredColumnReaders([string]$Sha, [string[]]$Paths, [string]$Parameter) {
    $files = @(& git -C $RepoRoot grep -il 'sprk_invoicestatus' $Sha -- @Paths 2>&1)
    if ($LASTEXITCODE -gt 1) { throw [EscalationException]::new("${Parameter} ${Sha}: git grep failed ($($files -join ' ')). Nothing was changed.") }
    if ($LASTEXITCODE -eq 1 -or $files.Count -eq 0) { return }
    $names = @($files | ForEach-Object { ([string]$_) -replace '^[^:]+:', '' })       # "<sha>:<path>" -> "<path>"
    $others = @($names | Where-Object { $_ -ne $BffModulePath })
    if ($others.Count -gt 0) {
        throw [EscalationException]::new("${Parameter} ${Sha}: $($others.Count) file(s) under $($Paths -join ', ') still name sprk_invoicestatus at that commit: $($others -join ', '). A build from it breaks when the column is deleted (task 015's readers are not all in that build). Nothing was changed.")
    }
    # Only the module is left: every hit must be the exact transitional line, once.
    $lines = @(& git -C $RepoRoot grep -in 'sprk_invoicestatus' $Sha -- $BffModulePath 2>&1)
    $bad = [System.Collections.Generic.List[string]]::new(); $exact = 0
    foreach ($l in $lines) {
        $m = [regex]::Match([string]$l, '^[^:]+:[^:]+:(?<n>\d+):(?<text>.*)$')
        if ($m.Success -and $m.Groups['text'].Value.Trim() -ceq $TransitionalModuleLine) { $exact++ } else { $bad.Add("line $($m.Groups['n'].Value): $($m.Groups['text'].Value.Trim())") }
    }
    if ($bad.Count -gt 0 -or $exact -gt 1) {
        throw [EscalationException]::new("${Parameter} ${Sha}: $BffModulePath names sprk_invoicestatus other than as the one exact transitional line ($TransitionalModuleLine): $(if ($bad.Count -gt 0) { $bad -join ' | ' } else { "the exact line occurs $exact times" }). Keep 015's marker line when merging; another branch's text that names the column inline is refused. Nothing was changed.")
    }
}

function Assert-BffAttestation([string]$Sha, [bool]$ForDelete) {
    Assert-CommitExists $Sha '-BffDeployAttestation'
    $module = Get-FileAtCommit $Sha $BffModulePath
    if ($null -eq $module) { throw [EscalationException]::new("-BffDeployAttestation ${Sha}: $BffModulePath does not exist at that commit. Nothing was changed.") }
    if (-not (Test-InvoicesAllowListHasStatuscode $module)) {
        throw [EscalationException]::new("-BffDeployAttestation ${Sha}: the external invoices allow-list in $BffModulePath does not list ""statuscode"" at that commit, so a BFF built from it refuses the grid's statuscode fetch. Deploy a BFF that admits both columns first. Nothing was changed.")
    }
    if (-not $ForDelete) { return }
    foreach ($path in $BffFinanceReaders) {
        $text = Get-FileAtCommit $Sha $path
        if ($null -eq $text) { throw [EscalationException]::new("-BffDeployAttestation ${Sha}: $path does not exist at that commit. Nothing was changed.") }
        if ($text -match '(?i)sprk_invoicestatus') { throw [EscalationException]::new("-BffDeployAttestation ${Sha}: $path still reads sprk_invoicestatus at that commit; a BFF built from it breaks when the column is deleted (task 015's readers are not in that build). Nothing was changed.") }
        if ($path.EndsWith('/FinanceBaseValues.cs') -and $text -notmatch 'InvoiceStatusCodeColumn') { throw [EscalationException]::new("-BffDeployAttestation ${Sha}: $path has no InvoiceStatusCodeColumn at that commit (task 015's readers are not in that build). Nothing was changed.") }
    }
    # The three files above are the specific checks; this one covers every other BFF source file (FinanceSummaryService,
    # FinancialCalculationToolHandler, InvoiceLiveFactResolver, ...). The BFF is built from src/server.
    Assert-NoRetiredColumnReaders $Sha @('src/server') '-BffDeployAttestation'
}

function Assert-SpaAttestation([string]$Sha) {
    Assert-CommitExists $Sha '-SpaDeployAttestation'
    foreach ($path in $SpaReaders) {
        $text = Get-FileAtCommit $Sha $path
        if ($null -eq $text) { throw [EscalationException]::new("-SpaDeployAttestation ${Sha}: $path does not exist at that commit. Nothing was changed.") }
        if ($text -match '(?i)sprk_invoicestatus') { throw [EscalationException]::new("-SpaDeployAttestation ${Sha}: $path still reads sprk_invoicestatus at that commit; an SPA built from it breaks when the column is deleted. Nothing was changed.") }
    }
    # The two files above are the specific checks; this covers every other SPA source file. The SPA bundle is built from
    # src/client/external-spa/src plus the shared UI library, which vite.config.ts aliases as @spaarke/ui-components
    # (ontology-r1 015 re-check K2); tests and comments are scanned too, as the BFF-side guard scans comments.
    Assert-NoRetiredColumnReaders $Sha @('src/client/external-spa/src', 'src/client/shared/Spaarke.UI.Components/src') '-SpaDeployAttestation'
}

try {
    if ($GridConfig -and $Delete) {
        throw [EscalationException]::new("-GridConfig and -Delete cannot run together: the grid blocker would clear partway through the run, so the delete verdict could not be judged on the state the operator approved. Run -GridConfig, then -Delete as separate runs. Nothing was changed.")
    }
    if (($GridConfig -or $Delete) -and [string]::IsNullOrWhiteSpace($BffDeployAttestation)) {
        throw [EscalationException]::new("$(if ($Delete) { '-Delete' } else { '-GridConfig' }) needs -BffDeployAttestation <commit sha>: the commit the BFF deployed to dev was built from (see the parameter help). Nothing was changed.")
    }
    if ($Delete -and [string]::IsNullOrWhiteSpace($SpaDeployAttestation)) {
        throw [EscalationException]::new("-Delete needs -SpaDeployAttestation <commit sha>: the commit the external SPA deployed to dev was built from. Nothing was changed.")
    }
    if ($GridConfig -or $Delete) { Assert-BffAttestation $BffDeployAttestation ([bool]$Delete) }
    if ($Delete) { Assert-SpaAttestation $SpaDeployAttestation }
    if ($GridConfig -or $Delete) {
        Write-Host "Pre-flight (git only, before any Dataverse call) passed: -BffDeployAttestation $BffDeployAttestation$(if ($Delete) { " (allow-list, Finance readers, git grep over src/server), -SpaDeployAttestation $SpaDeployAttestation (SPA readers, git grep over src/client/external-spa/src)" } else { ' (allow-list lists statuscode)' })" -ForegroundColor Green
    }
}
catch [EscalationException] {
    Write-Host ""; Write-Host "ESCALATION - REFUSED: $($_.Exception.Message)" -ForegroundColor Red; exit 2
}

$null = Initialize-EbillingSchema -EnvironmentUrl $EnvironmentUrl -SolutionUniqueName 'SpaarkeMaster' -DryRun:$DryRun -Entity $Table

# ---------------------------------------------------------------------------------------------------------------
# Pure transforms (XML / JSON text in, text out)
# ---------------------------------------------------------------------------------------------------------------

# Form: removes each cell bound to the column (and its row when the row is left empty), and the column's entries in the
# RecordHeader card's layoutJson. Remaining = mentions left (the caller refuses on > 0).
function ConvertTo-FormWithoutColumn([string]$FormXml) {
    $doc = [xml]$FormXml
    $removed = [System.Collections.Generic.List[string]]::new()
    foreach ($ctl in @($doc.SelectNodes("//control[@datafieldname='$Column']"))) {
        $cell = $ctl.SelectSingleNode('ancestor::cell[1]')
        if (-not $cell -or -not $cell.ParentNode) { continue }
        $row = $cell.ParentNode
        [void]$row.RemoveChild($cell)
        $removed.Add("cell $($cell.GetAttribute('id'))")
        if ($row.LocalName -eq 'row' -and $row.SelectNodes('cell').Count -eq 0 -and $row.ParentNode) {
            [void]$row.ParentNode.RemoveChild($row)
            $removed.Add('its now-empty row')
        }
    }
    foreach ($node in @($doc.SelectNodes('//layoutJson'))) {
        $text = $node.InnerText
        $new = [regex]::Replace($text, '\{"name":"' + $Column + '"[^{}]*\},?', '')
        $new = $new -replace ',\]', ']'
        if ($new -ne $text) { $node.InnerText = $new; $removed.Add('RecordHeader layoutJson entry') }
    }
    $xml = $doc.OuterXml
    return [pscustomobject]@{ Xml = $xml; Removed = $removed; Remaining = [regex]::Matches($xml, $Column).Count }
}

# View: the column's attribute / cell become statuscode (dropped when statuscode is already there); an 'eq ToReview' condition
# becomes 'statuscode eq 1' (FR-06: ToReview -> In Review). Any other condition on the column is left, so Remaining refuses.
function ConvertTo-ViewWithStatuscode([string]$FetchXml, [string]$LayoutXml) {
    $changes = [System.Collections.Generic.List[string]]::new()
    $fetch = [xml]$FetchXml
    $entity = $fetch.SelectSingleNode('/fetch/entity')
    $hasStatus = [bool]$entity.SelectSingleNode("attribute[@name='statuscode']")
    foreach ($a in @($fetch.SelectNodes("//attribute[@name='$Column']"))) {
        if ($hasStatus) { [void]$a.ParentNode.RemoveChild($a); $changes.Add('fetch attribute removed (statuscode already read)') }
        else { $a.SetAttribute('name', 'statuscode'); $hasStatus = $true; $changes.Add('fetch attribute -> statuscode') }
    }
    foreach ($c in @($fetch.SelectNodes("//condition[@attribute='$Column']"))) {
        if ($c.GetAttribute('operator') -eq 'eq' -and $c.GetAttribute('value') -eq '100000000') {
            $c.SetAttribute('attribute', 'statuscode'); $c.SetAttribute('value', "$InReview"); $changes.Add('filter ToReview -> statuscode eq 1 (In Review)')
        }
    }
    $layoutOut = $LayoutXml
    if ($LayoutXml) {
        $layout = [xml]$LayoutXml
        $hasCell = [bool]$layout.SelectSingleNode("//cell[@name='statuscode']")
        foreach ($cell in @($layout.SelectNodes("//cell[@name='$Column']"))) {
            if ($hasCell) { [void]$cell.ParentNode.RemoveChild($cell); $changes.Add('layout cell removed') }
            else { $cell.SetAttribute('name', 'statuscode'); $hasCell = $true; $changes.Add('layout cell -> statuscode') }
        }
        $layoutOut = $layout.OuterXml
    }
    $fetchOut = $fetch.OuterXml
    return [pscustomobject]@{
        FetchXml = $fetchOut; LayoutXml = $layoutOut; Changes = $changes
        Remaining = [regex]::Matches($fetchOut + $layoutOut, $Column).Count
    }
}

# Grid configuration JSON: every mention of the column becomes statuscode (fetch attribute, layout cell, column settings key).
function ConvertTo-GridConfigWithStatuscode([string]$ConfigJson) {
    if ($ConfigJson -match 'statuscode') { throw [EscalationException]::new("Grid configuration $InvoiceGridConfig already mentions statuscode; edit it by hand.") }
    $new = $ConfigJson.Replace($Column, 'statuscode')     # case-sensitive; the check below catches any other spelling
    if ($new -match '(?i)sprk_invoicestatus') {
        throw [EscalationException]::new("Grid configuration $InvoiceGridConfig still names the column after the replace ($([regex]::Matches($new, '(?i)sprk_invoicestatus').Count) mention(s), probably another letter case; the replace is case-sensitive). Edit it by hand. Nothing was written.")
    }
    return $new
}

# ---------------------------------------------------------------------------------------------------------------
# Inventory (read-only)
# ---------------------------------------------------------------------------------------------------------------

function Get-Inventory {
    $inv = [ordered]@{}
    $attr = Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')/Microsoft.Dynamics.CRM.PicklistAttributeMetadata?`$select=MetadataId,LogicalName&`$expand=GlobalOptionSet(`$select=Name,MetadataId,IsGlobal)" -Retries 2
    $inv.Attribute = $attr
    $os = Get-DvOrNull "GlobalOptionSetDefinitions(Name='$OptionSetName')?`$select=Name,MetadataId,IsGlobal"
    $inv.OptionSet = $os
    if (-not $attr) { return $inv }

    $inv.Rows = @((Invoke-Dv "${Table}s?`$select=sprk_invoiceid,sprk_name,$Column,statecode,statuscode&`$filter=$Column ne null").value)
    $inv.ByStatus = @((Invoke-Dv "${Table}s?`$select=statecode,statuscode").value) | Group-Object statecode, statuscode | Sort-Object Name
    $inv.AiSkillConfigs = @((Invoke-Dv "aiskillconfigs?`$select=uniquename,aiskillconfigid,aiskill&`$filter=_attribute_value eq $($attr.MetadataId)").value)
    $inv.Forms = @((Invoke-Dv "systemforms?`$select=name,formid,objecttypecode,type&`$filter=contains(formxml,'$Column')").value)
    $inv.Views = @((Invoke-Dv "savedqueries?`$select=name,savedqueryid,returnedtypecode&`$filter=contains(fetchxml,'$Column') or contains(layoutxml,'$Column')").value)
    $inv.PersonalViews = @((Invoke-Dv "userqueries?`$select=name&`$filter=contains(fetchxml,'$Column') or contains(layoutxml,'$Column')").value)
    $inv.Workflows = @((Invoke-Dv "workflows?`$select=name,category&`$filter=contains(clientdata,'$Column') or contains(xaml,'$Column')").value)
    $inv.Charts = @((Invoke-Dv "savedqueryvisualizations?`$select=name&`$filter=contains(datadescription,'$Column')").value)
    $inv.GridConfigs = @((Invoke-Dv "sprk_gridconfigurations?`$select=sprk_name,sprk_gridconfigurationid&`$filter=contains(sprk_configjson,'$Column')").value)

    $aiIds = @($inv.AiSkillConfigs | ForEach-Object { $_.aiskillconfigid })
    $inv.Dependencies = @(foreach ($d in @((Invoke-Dv "RetrieveDependenciesForDelete(ObjectId=$($attr.MetadataId),ComponentType=2)").value)) {
        $id = $d.dependentcomponentobjectid
        $kind = switch ([int]$d.dependentcomponenttype) { 10314 { 'AI skill config' } 60 { 'system form' } 26 { 'view' } default { "component type $($d.dependentcomponenttype)" } }
        $label = if ($aiIds -contains $id) { ($inv.AiSkillConfigs | Where-Object aiskillconfigid -eq $id).uniquename } elseif ($KnownForms.ContainsKey($id)) { $KnownForms[$id] } elseif ($KnownViews.ContainsKey($id)) { $KnownViews[$id] } else { $id }
        [pscustomobject]@{ Kind = $kind; Label = $label; ObjectId = $id; IsOptOut = ($aiIds -contains $id); Known = ($aiIds -contains $id) -or $KnownForms.ContainsKey($id) -or $KnownViews.ContainsKey($id) }
    })
    $inv.OptionSetDependencies = if ($os) { @((Invoke-Dv "RetrieveDependenciesForDelete(ObjectId=$($os.MetadataId),ComponentType=9)").value) } else { @() }
    return $inv
}

function Write-Inventory($inv) {
    Write-Host "Column     : $Table.$Column  MetadataId $($inv.Attribute.MetadataId)  option set: $($inv.Attribute.GlobalOptionSet.Name) (global, $($inv.Attribute.GlobalOptionSet.MetadataId))"
    Write-Host "Rows with a value: $($inv.Rows.Count)"
    foreach ($r in $inv.Rows) { Write-Host ("  {0}  '{1}'  value {2} ({3})  statecode {4}  statuscode {5}" -f $r.sprk_invoiceid, $r.sprk_name, $r.$Column, $KnownValues[[int]$r.$Column], $r.statecode, $r.statuscode) }
    Write-Host "Invoices by (statecode, statuscode): $(($inv.ByStatus | ForEach-Object { "($($_.Name)) $($_.Count)" }) -join '; ')"
    Write-Host "AI skill configs on the column: $(($inv.AiSkillConfigs | ForEach-Object { "$($_.uniquename) ($($_.aiskillconfigid))" }) -join '; ')"
    Write-Host "Mentions: forms $($inv.Forms.Count), views $($inv.Views.Count), personal views $($inv.PersonalViews.Count), workflows / flows $($inv.Workflows.Count), charts $($inv.Charts.Count), grid configurations $($inv.GridConfigs.Count)"
    foreach ($f in $inv.Forms) { Write-Host "  form $($f.formid) '$($f.name)' ($($f.objecttypecode), type $($f.type))" }
    foreach ($v in $inv.Views) { Write-Host "  view $($v.savedqueryid) '$($v.name)' ($($v.returnedtypecode))" }
    foreach ($g in $inv.GridConfigs) { Write-Host "  grid configuration $($g.sprk_gridconfigurationid) '$($g.sprk_name)'" }
    Write-Host "RetrieveDependenciesForDelete(attribute $($inv.Attribute.MetadataId)): $($inv.Dependencies.Count)"
    foreach ($d in $inv.Dependencies) { Write-Host ("  {0,-8} {1}: {2} ({3})" -f $(if ($d.Known) { 'known' } else { 'UNKNOWN' }), $d.Kind, $d.Label, $d.ObjectId) -ForegroundColor $(if ($d.Known) { 'Gray' } else { 'Red' }) }
    Write-Host "RetrieveDependenciesForDelete(option set $($inv.OptionSet.MetadataId)): $($inv.OptionSetDependencies.Count)"
    foreach ($d in $inv.OptionSetDependencies) { Write-Host "  component type $($d.dependentcomponenttype) $($d.dependentcomponentobjectid)" }
}

# Rows that the FR-06 step cannot settle by itself.
function Get-RowBlockers($inv) {
    $b = [System.Collections.Generic.List[string]]::new()
    foreach ($r in $inv.Rows) {
        if (-not $KnownValues.ContainsKey([int]$r.$Column)) { $b.Add("row $($r.sprk_invoiceid) holds value $($r.$Column), not ToReview / Reviewed") }
        elseif ([int]$r.statuscode -ne $InReview -or [int]$r.statecode -ne 0) { $b.Add("row $($r.sprk_invoiceid) ($($KnownValues[[int]$r.$Column])) is at statecode $($r.statecode) / statuscode $($r.statuscode), not In Review: moving it is a lifecycle decision") }
    }
    return $b
}

function Get-DeleteBlockers($inv) {
    $b = [System.Collections.Generic.List[string]]::new()
    foreach ($x in @(Get-RowBlockers $inv)) { $b.Add($x) }
    foreach ($f in $inv.Forms) { $b.Add("form '$($f.name)' ($($f.formid)) still mentions the column (run -FormsAndViews)") }
    foreach ($v in $inv.Views) { $b.Add("view '$($v.name)' ($($v.savedqueryid)) still mentions the column (run -FormsAndViews)") }
    foreach ($g in $inv.GridConfigs) { $b.Add("grid configuration '$($g.sprk_name)' ($($g.sprk_gridconfigurationid)) still mentions the column (run -GridConfig after the BFF deploy)") }
    foreach ($v in $inv.PersonalViews) { $b.Add("personal view '$($v.name)' mentions the column") }
    foreach ($w in $inv.Workflows) { $b.Add("workflow / flow '$($w.name)' mentions the column") }
    foreach ($c in $inv.Charts) { $b.Add("chart '$($c.name)' mentions the column") }
    foreach ($d in $inv.Dependencies | Where-Object { -not $_.IsOptOut }) { $b.Add("dependency $($d.Kind) $($d.Label) ($($d.ObjectId)) is not the Copilot form-fill opt-out") }
    foreach ($a in $inv.AiSkillConfigs | Where-Object { $_.aiskill -ne 'FormFillFieldOptOut' }) { $b.Add("AI skill config $($a.uniquename) is not a form-fill opt-out") }
    foreach ($d in $inv.OptionSetDependencies | Where-Object { $_.dependentcomponentobjectid -ne $inv.Attribute.MetadataId }) { $b.Add("the option set is used by another component (type $($d.dependentcomponenttype) $($d.dependentcomponentobjectid))") }
    return $b
}

# ---------------------------------------------------------------------------------------------------------------
# Write phases
# ---------------------------------------------------------------------------------------------------------------

function Publish-Invoice {
    if ($DryRun) { Write-Host "  WOULD PUBLISH $Table" -ForegroundColor Cyan; return }
    Invoke-Dv -Path 'PublishXml' -Method POST -Body @{ ParameterXml = "<importexportxml><entities><entity>$Table</entity></entities></importexportxml>" } | Out-Null
    Write-Host "  published $Table" -ForegroundColor Green
}

function Invoke-Migrate($inv) {
    $blockers = @(Get-RowBlockers $inv)
    if ($blockers.Count -gt 0) { throw [EscalationException]::new("Data step refused: $($blockers -join '; '). Nothing was written.") }
    $toReview = @($inv.Rows | Where-Object { [int]$_.$Column -eq 100000000 }).Count
    $reviewed = @($inv.Rows | Where-Object { [int]$_.$Column -eq 100000001 }).Count
    foreach ($r in $inv.Rows) { Write-Host "  row $($r.sprk_invoiceid) ($($KnownValues[[int]$r.$Column])) already statuscode In Review - no write" -ForegroundColor Yellow }
    Write-Host "  migrated: 0 written; $($inv.Rows.Count) row(s) with a value ($toReview ToReview, $reviewed Reviewed), all already In Review" -ForegroundColor Green
}

function Invoke-FormsAndViews {
    $edited = 0
    foreach ($formId in $MainForm, $QuickCreateForm) {
        $form = Invoke-Dv "systemforms($formId)?`$select=name,formxml"
        $res = ConvertTo-FormWithoutColumn $form.formxml
        if ($res.Remaining -gt 0) { throw [EscalationException]::new("Form '$($form.name)' would still mention the column after the clean-up ($($res.Remaining)); edit it by hand. Earlier forms stay edited.") }
        if ($res.Removed.Count -eq 0) { Write-Host "  skipped  form '$($form.name)': no reference" -ForegroundColor Yellow; continue }
        if ($DryRun) { Write-Host "  WOULD EDIT form '$($form.name)': $($res.Removed -join '; ')" -ForegroundColor Cyan; continue }
        Invoke-Dv -Path "systemforms($formId)" -Method PATCH -Body @{ formxml = $res.Xml } | Out-Null
        $edited++; Write-Host "  EDITED   form '$($form.name)': $($res.Removed -join '; ')" -ForegroundColor Green
    }
    foreach ($viewId in $MatterContextView, $ReviewQueueView) {
        $view = Invoke-Dv "savedqueries($viewId)?`$select=name,fetchxml,layoutxml"
        $res = ConvertTo-ViewWithStatuscode $view.fetchxml $view.layoutxml
        if ($res.Remaining -gt 0) { throw [EscalationException]::new("View '$($view.name)' would still mention the column ($($res.Remaining)); edit it by hand. Earlier items stay edited.") }
        if ($res.Changes.Count -eq 0) { Write-Host "  skipped  view '$($view.name)': no reference" -ForegroundColor Yellow; continue }
        if ($DryRun) { Write-Host "  WOULD EDIT view '$($view.name)': $($res.Changes -join '; ')" -ForegroundColor Cyan; continue }
        $body = @{ fetchxml = $res.FetchXml }
        if ($view.layoutxml) { $body.layoutxml = $res.LayoutXml }
        Invoke-Dv -Path "savedqueries($viewId)" -Method PATCH -Body $body | Out-Null
        $edited++; Write-Host "  EDITED   view '$($view.name)': $($res.Changes -join '; ')" -ForegroundColor Green
    }
    # Always publish (a previous run may have edited and stopped before publishing).
    Publish-Invoice
}

function Invoke-GridConfig {
    $g = Get-DvOrNull "sprk_gridconfigurations($InvoiceGridConfig)?`$select=sprk_name,sprk_configjson"
    if (-not $g) { Write-Host "  skipped  grid configuration $InvoiceGridConfig not found" -ForegroundColor Yellow; return }
    if ($g.sprk_configjson -notmatch $Column) { Write-Host "  skipped  grid '$($g.sprk_name)': no reference" -ForegroundColor Yellow; return }
    $new = ConvertTo-GridConfigWithStatuscode $g.sprk_configjson
    # The row belongs to another project (SPA-r3 / uac-r2): the write is conditional on the row being unchanged since it was read.
    $etag = $g.'@odata.etag'
    if ([string]::IsNullOrWhiteSpace($etag)) { throw [EscalationException]::new("Grid configuration $InvoiceGridConfig was read without an @odata.etag, so the write cannot be made conditional (If-Match). Nothing was written.") }
    if ($DryRun) { Write-Host "  WOULD EDIT grid '$($g.sprk_name)': $Column -> statuscode ($([regex]::Matches($g.sprk_configjson, $Column, 'IgnoreCase').Count) mention(s)), If-Match $etag" -ForegroundColor Cyan; return }
    try { Invoke-Dv -Path "sprk_gridconfigurations($InvoiceGridConfig)" -Method PATCH -Body @{ sprk_configjson = $new } -ExtraHeaders @{ 'If-Match' = $etag } | Out-Null }
    catch {
        $failure = $_
        if ($failure.Exception.Data['Status'] -eq 412) {
            # The row changed after it was read. If it no longer names the column, the switch is already applied (another run, or
            # someone did it by hand): report that and stop cleanly. Otherwise the row holds an edit this run has not seen: refuse.
            # "Already applied" also needs statuscode in the row (ontology-r1 015 re-check K4): a row that names neither column
            # was edited some other way, and is refused rather than reported as done.
            $now = Get-DvOrNull "sprk_gridconfigurations($InvoiceGridConfig)?`$select=sprk_name,sprk_configjson"
            if (-not $now) { throw [EscalationException]::new("Grid configuration $InvoiceGridConfig changed after it was read (HTTP 412, If-Match $etag) and could not be read again (it may have been deleted). Nothing was written.") }
            if (-not [string]::IsNullOrWhiteSpace($now.sprk_configjson) -and $now.sprk_configjson -notmatch '(?i)sprk_invoicestatus' -and $now.sprk_configjson -notmatch 'statuscode') {
                throw [EscalationException]::new("Grid configuration $InvoiceGridConfig changed after it was read (HTTP 412, If-Match $etag) and now names neither $Column nor statuscode: another session edited it another way. Nothing was written.")
            }
            if (-not [string]::IsNullOrWhiteSpace($now.sprk_configjson) -and $now.sprk_configjson -notmatch '(?i)sprk_invoicestatus') {
                Write-Host "  already applied  grid '$($g.sprk_name)': the row changed after it was read (HTTP 412, If-Match $etag) and no longer names $Column; nothing to write" -ForegroundColor Yellow
                return
            }
            throw [EscalationException]::new("Grid configuration $InvoiceGridConfig changed after it was read (HTTP 412, If-Match $etag) and it still names the column: another session edited it. Nothing was written; re-run to read the current row.")
        }
        throw
    }
    Write-Host "  EDITED   grid '$($g.sprk_name)': $Column -> statuscode (If-Match $etag)" -ForegroundColor Green
}

function Invoke-Delete($inv) {
    $attrId = $inv.Attribute.MetadataId
    Write-Host "  1. Copilot form-fill opt-out row(s)" -ForegroundColor Cyan
    foreach ($a in $inv.AiSkillConfigs) {
        if ($DryRun) { Write-Host "    WOULD DELETE aiskillconfig $($a.aiskillconfigid) ($($a.uniquename))" -ForegroundColor Cyan; continue }
        try { Invoke-Dv -Path "aiskillconfigs($($a.aiskillconfigid))" -Method DELETE | Out-Null; Write-Host "    DELETED  aiskillconfig $($a.aiskillconfigid) ($($a.uniquename))" -ForegroundColor Green }
        catch { if ($_.Exception.Data['Status'] -eq 404) { Write-Host "    skipped  $($a.aiskillconfigid) already gone" -ForegroundColor Yellow } else { throw } }
    }
    Write-Host "  2. RetrieveDependenciesForDelete(attribute $attrId) must be 0" -ForegroundColor Cyan
    if ($DryRun) { Write-Host "    WhatIf: not re-checked (nothing was removed)" -ForegroundColor Cyan }
    else {
        $left = @()
        for ($i = 0; $i -lt 6; $i++) { $left = @((Invoke-Dv "RetrieveDependenciesForDelete(ObjectId=$attrId,ComponentType=2)").value); if ($left.Count -eq 0) { break }; Start-Sleep -Seconds 5 }
        Write-Host "    dependents: $($left.Count)" -ForegroundColor $(if ($left.Count -eq 0) { 'Green' } else { 'Red' })
        if ($left.Count -gt 0) { throw [EscalationException]::new("$($left.Count) dependent(s) remain: $(($left | ForEach-Object { "type $($_.dependentcomponenttype) $($_.dependentcomponentobjectid)" }) -join '; '). The column was NOT deleted.") }
    }
    Write-Host "  3. The column" -ForegroundColor Cyan
    if ($DryRun) { Write-Host "    WOULD DELETE EntityDefinitions(LogicalName='$Table')/Attributes($attrId)" -ForegroundColor Cyan }
    else {
        Invoke-WhilePropagating { Invoke-Dv -Path "EntityDefinitions(LogicalName='$Table')/Attributes($attrId)" -Method DELETE | Out-Null } | Out-Null
        Write-Host "    DELETED  $Table.$Column" -ForegroundColor Green
    }
    Write-Host "  4. The global option set" -ForegroundColor Cyan
    if (-not $inv.OptionSet) { Write-Host "    skipped  option set already gone" -ForegroundColor Yellow }
    elseif ($DryRun) { Write-Host "    WOULD DELETE GlobalOptionSetDefinitions($($inv.OptionSet.MetadataId)) ($OptionSetName) once its dependencies are 0" -ForegroundColor Cyan }
    else {
        $left = @()
        for ($i = 0; $i -lt 12; $i++) { $left = @((Invoke-Dv "RetrieveDependenciesForDelete(ObjectId=$($inv.OptionSet.MetadataId),ComponentType=9)").value); if ($left.Count -eq 0) { break }; Start-Sleep -Seconds 5 }
        Write-Host "    RetrieveDependenciesForDelete(option set): $($left.Count)" -ForegroundColor $(if ($left.Count -eq 0) { 'Green' } else { 'Red' })
        if ($left.Count -gt 0) { throw [EscalationException]::new("The option set still has $($left.Count) dependent(s); the column is deleted, the option set is NOT.") }
        Invoke-WhilePropagating { Invoke-Dv -Path "GlobalOptionSetDefinitions($($inv.OptionSet.MetadataId))" -Method DELETE | Out-Null } | Out-Null
        Write-Host "    DELETED  option set $OptionSetName" -ForegroundColor Green
    }
    Publish-Invoice
    Write-Host "  5. Verify" -ForegroundColor Cyan
    if ($DryRun) { Write-Host "    WhatIf: not verified" -ForegroundColor Cyan; return }
    $problems = [System.Collections.Generic.List[string]]::new()
    $gone = $false
    for ($i = 0; $i -lt 24 -and -not $gone; $i++) { $gone = -not (Get-DvOrNull "EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column')?`$select=LogicalName"); if (-not $gone) { Start-Sleep -Seconds 5 } }
    if ($gone) { Write-Host "    EntityDefinitions(LogicalName='$Table')/Attributes(LogicalName='$Column') -> 404" -ForegroundColor Green } else { $problems.Add('the column still reads') }
    if (Get-DvOrNull "GlobalOptionSetDefinitions(Name='$OptionSetName')?`$select=Name") { $problems.Add('the option set still reads') } else { Write-Host "    GlobalOptionSetDefinitions(Name='$OptionSetName') -> 404" -ForegroundColor Green }
    foreach ($formId in $MainForm, $QuickCreateForm) {
        $f = Invoke-Dv "systemforms($formId)?`$select=name,formxml"
        try { [void][xml]$f.formxml } catch { $problems.Add("form '$($f.name)' is not valid XML") }
    }
    if ($problems.Count -gt 0) { throw "Verification failed: $($problems -join '; ')" }
}

# ---------------------------------------------------------------------------------------------------------------
# Run
# ---------------------------------------------------------------------------------------------------------------

$writes = $Migrate -or $FormsAndViews -or $GridConfig -or $Delete
Write-Host ""
Write-Host "eBilling task 015 - retire $Table.$Column" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl   Phases: $(@(if ($Migrate) {'Migrate'}; if ($FormsAndViews) {'FormsAndViews'}; if ($GridConfig) {'GridConfig'}; if ($Delete) {'Delete'}) -join ', ' | ForEach-Object { if ($_) { $_ } else { 'REPORT (read-only)' } })$(if ($DryRun) { '   -WhatIf (NO WRITES; read-only GETs against Dataverse still run)' })"
Write-Host ""

try {
    if ($writes -and -not $PSBoundParameters.ContainsKey('EnvironmentUrl') -and ([uri]$EnvironmentUrl).Host -ne 'spaarkedev1.crm.dynamics.com') {
        throw [EscalationException]::new("Writes refuse the environment '$EnvironmentUrl' that came from `$env:DATAVERSE_URL: pass -EnvironmentUrl explicitly. Nothing was changed.")
    }
    if ($Delete -and [string]::IsNullOrWhiteSpace($OwnerApproval)) { throw "-Delete needs -OwnerApproval '<decision, date, who>' (project CLAUDE.md section 5; D-97)." }

    $inv = Get-Inventory
    if (-not $inv.Attribute) {
        Write-Host "$Table.$Column does not exist (already retired). Option set: $(if ($inv.OptionSet) { "STILL PRESENT ($($inv.OptionSet.MetadataId))" } else { 'gone' })." -ForegroundColor Yellow
        # The grid move does not depend on the column: a grid row that still names it can be moved after the column is gone.
        if ($GridConfig) { Write-Host ""; Write-Host "Outside-counsel invoices grid" -ForegroundColor Cyan; Invoke-GridConfig }
        if ($inv.OptionSet -and $Delete) { throw [EscalationException]::new("The column is gone but the option set remains; delete it after checking RetrieveDependenciesForDelete(ObjectId=$($inv.OptionSet.MetadataId),ComponentType=9).") }
        exit 0
    }
    Write-Inventory $inv
    $blockers = @(Get-DeleteBlockers $inv)
    Write-Host ""
    if ($blockers.Count -eq 0) { Write-Host "DELETE VERDICT: READY" -ForegroundColor Green }
    else { Write-Host "DELETE VERDICT: BLOCKED" -ForegroundColor Red; foreach ($x in $blockers) { Write-Host "  - $x" -ForegroundColor Red } }

    if ($Migrate) { Write-Host ""; Write-Host "Migrate (FR-06)" -ForegroundColor Cyan; Invoke-Migrate $inv }
    if ($FormsAndViews) { Write-Host ""; Write-Host "Forms and views" -ForegroundColor Cyan; Invoke-FormsAndViews }
    if ($GridConfig) { Write-Host ""; Write-Host "Outside-counsel invoices grid" -ForegroundColor Cyan; Invoke-GridConfig }
    if ($Delete) {
        Write-Host ""; Write-Host "Delete (owner approval: $OwnerApproval)" -ForegroundColor Cyan
        # Re-read after any earlier phase of this run, so the verdict is about the live state (a -WhatIf run changed nothing, so
        # its earlier phases' items still block: run them for real first).
        # (-GridConfig never runs in the same run as -Delete: the pre-flight refuses that, because this re-read would clear the grid blocker.)
        if ($FormsAndViews -or $Migrate) { $inv = Get-Inventory; $blockers = @(Get-DeleteBlockers $inv) }
        if ($blockers.Count -gt 0) { throw [EscalationException]::new("Refusing the delete: $($blockers.Count) blocker(s): $($blockers -join '; '). Nothing was deleted.") }
        Invoke-Delete $inv
    }
    if (-not $writes) { Write-Host ""; Write-Host "Report only. Nothing was changed." -ForegroundColor Cyan }
}
catch [EscalationException] {
    Write-Host ""; Write-Host "ESCALATION - REFUSED: $($_.Exception.Message)" -ForegroundColor Red; exit 2
}
catch {
    Write-Host ""; Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Steps already done stay done; re-run to continue (each step skips work already done)." -ForegroundColor Red
    exit 1
}

Write-Host ""
if ($DryRun) { Write-Host "WhatIf run: nothing was written (read-only GETs against Dataverse did run, after the git pre-flight)." -ForegroundColor Cyan }
exit 0
