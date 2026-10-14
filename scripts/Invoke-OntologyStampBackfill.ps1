<#
.SYNOPSIS
    Ontology task 145 (D-131 child-record contract, D-34): the report-first backfill of the live sprk_regarding{core} stamp on
    Signals and Decision Records. Default = a read-only inventory. -Apply releases the stamp job's hold on the two tables.

.DESCRIPTION
    WHAT "BACKFILL" MEANS HERE (144 review K1). The stamp follows the SUBJECT, not the core: a Signal / Decision Record filed
    under an intermediate (a communication, document, event, invoice or to-do) is stamped with that intermediate's CURRENT
    root, and a re-file P->Q moves the stamp to Q while sprk_corerecordid (and the typed sprk_matter / sprk_project /
    sprk_workassignment, which equal it) stay P. So the typed core columns are never copied into the stamp:
      * a row whose subject IS a core (sprk_regarding{matter,project,workassignment,servicerequest}) already carries its
        stamp: the subject lookup is the stamp. Nothing to write.
      * a row whose subject is an intermediate gets the intermediate's root, which only the BFF derives
        (CoreAncestorResolver / CoreAncestorStampReconciliationJob). A copy of the typed core would be wrong after any
        re-file, and a second derivation in this script would be a parallel owner of the invariant (D-131 C2).
    The write therefore IS the BFF's stamp reconciliation job (task 144), which holds sprk_signal and sprk_decisionrecord
    report-only (CoreAncestor:StampReconciliation:ReportOnlyTables, default "sprk_signal,sprk_decisionrecord"). -Apply sets
    that key to "," (a value that names no table: the job splits with RemoveEmptyEntries; an EMPTY app setting can be
    dropped by App Service and would bring the default back). The job then writes both tables every 5 minutes.

    THE REPORT (default, read-only, runs nothing but GETs). Per table:
      total            rows in the table
      coreSubject      subject lookup is a core column: the stamp is the subject; nothing to backfill
      intermediate     subject lookup names an intermediate: stamped by the job
        stampEmpty     ...with no sprk_regarding{core} set: the job WILL write these (the definite backfill set)
        stampSet       ...with a stamp: the job checks it against the subject's root (stale ones appear in its report-only
                       log: traces | where message has "[CORE-ANCESTOR-RECON] stale mode=report-only")
      noSubject        no subject lookup matches sprk_regardingrecordid: nothing derivable; listed for a hand check
      typed.{col}      rows with the typed core column set, and how many of them it is NOT in the stamp (the K1 set: a
                       re-filed subject; the typed column stays as decision context, D-34 / task 145)
    plus up to -SampleSize rows per bucket, and a JSON report (-ReportPath, default under $env:TEMP).

    PREREQUISITES FOR -Apply (D-138; the main session checks them, the owner approves):
      1. the BFF on the environment carries task 144 (the job knows the two tables) and task 145;
      2. a report-only run of the job is recorded and uac-r2 has reviewed it;
      3. the owner has approved the dev-data change.
    The live command (one line):
      & ./scripts/Invoke-OntologyStampBackfill.ps1 -Apply
    It prints the report, then sets the app setting (ShouldProcess: -WhatIf shows it, -Confirm asks). Re-run the default
    mode about 10 minutes later: stampEmpty should be 0 on both tables. To hold the tables again, remove the setting:
      az webapp config appsettings delete --resource-group rg-spaarke-dev --name spaarke-bff-dev --setting-names CoreAncestor__StampReconciliation__ReportOnlyTables

    Auth: the operator's own az CLI identity (reader in Dataverse; Website Contributor on the web app for -Apply). No secrets.

.EXAMPLE
    & ./scripts/Invoke-OntologyStampBackfill.ps1
#>

[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [string]$EnvironmentUrl = 'https://spaarkedev1.crm.dynamics.com',
    [string]$ResourceGroup = 'rg-spaarke-dev',
    [string]$WebAppName = 'spaarke-bff-dev',
    [ValidateRange(0, 50)][int]$SampleSize = 5,
    [string]$ReportPath = '',
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'

# ── Constants (the BFF's: CoreAncestorResolver.CoreAncestorLookups / StampSourceColumns["sprk_signal"|"sprk_decisionrecord"]) ──
$CoreStampColumns = @('sprk_regardingmatter', 'sprk_regardingproject', 'sprk_regardingworkassignment', 'sprk_regardingservicerequest')
$IntermediateSubjectColumns = @('sprk_regardingcommunication', 'sprk_regardingdocument', 'sprk_regardingevent', 'sprk_regardinginvoice', 'sprk_regardingtodo')
$Tables = [ordered]@{
    sprk_signal         = [pscustomobject]@{ Set = 'sprk_signals'; Id = 'sprk_signalid'; Typed = @('sprk_matter') }
    sprk_decisionrecord = [pscustomobject]@{ Set = 'sprk_decisionrecords'; Id = 'sprk_decisionrecordid'; Typed = @('sprk_matter', 'sprk_project', 'sprk_workassignment') }
}
$SettingName = 'CoreAncestor__StampReconciliation__ReportOnlyTables'
$SettingReleaseValue = ','

# The id a lookup column carries on a Web API row (`_{column}_value`), lower-case; '' when unset.
function Get-LookupId($Row, [string]$Column) {
    $p = $Row.PSObject.Properties["_$($Column)_value"]
    if ($p -and $p.Value) { "$($p.Value)".ToLowerInvariant() } else { '' }
}

# Classifies one row. The subject is the sprk_regarding* lookup whose id equals the pair id (sprk_regardingrecordid), the
# rule SignalWriter uses; the stamp is the set of sprk_regarding{core} columns.
function Get-StampBackfillClass($Row, [string[]]$TypedColumns) {
    $pairId = if ($Row.sprk_regardingrecordid) { "$($Row.sprk_regardingrecordid)".Trim('{', '}').ToLowerInvariant() } else { '' }
    $stamp = [ordered]@{}
    foreach ($c in $CoreStampColumns) { $id = Get-LookupId $Row $c; if ($id) { $stamp[$c] = $id } }

    $subjectColumn = $null; $shape = 'noSubject'
    if ($pairId) {
        foreach ($c in $CoreStampColumns) { if ((Get-LookupId $Row $c) -eq $pairId) { $subjectColumn = $c; $shape = 'coreSubject'; break } }
        if (-not $subjectColumn) {
            foreach ($c in $IntermediateSubjectColumns) { if ((Get-LookupId $Row $c) -eq $pairId) { $subjectColumn = $c; $shape = 'intermediate'; break } }
        }
    }

    $typed = [ordered]@{}
    foreach ($c in $TypedColumns) {
        $id = Get-LookupId $Row $c
        if ($id) { $typed[$c] = [pscustomobject]@{ Id = $id; InStamp = @($stamp.Values) -contains $id } }
    }

    [pscustomobject]@{
        Shape         = $shape
        SubjectColumn = $subjectColumn
        StampEmpty    = $stamp.Count -eq 0
        Stamp         = $stamp
        Typed         = $typed
        # The definite backfill set: filed under an intermediate and carrying no stamp. The job writes these.
        NeedsStamp    = $shape -eq 'intermediate' -and $stamp.Count -eq 0
    }
}

# Folds classified rows into the per-table counts and samples.
function Get-StampBackfillSummary([string]$Table, $Rows, [string[]]$TypedColumns, [string]$IdColumn, [int]$SampleCount) {
    $counts = [ordered]@{ total = 0; coreSubject = 0; intermediate = 0; stampEmpty = 0; stampSet = 0; noSubject = 0 }
    $typedCounts = [ordered]@{}
    foreach ($c in $TypedColumns) { $typedCounts[$c] = [ordered]@{ set = 0; inStamp = 0; notInStamp = 0 } }
    $samples = [ordered]@{ coreSubject = @(); stampEmpty = @(); stampSet = @(); noSubject = @(); typedNotInStamp = @() }
    $take = { param($bucket, $row, $class)
        if ($samples[$bucket].Count -lt $SampleCount) {
            $samples[$bucket] += [pscustomobject]@{ id = $row.$IdColumn; subject = $class.SubjectColumn; stamp = $class.Stamp; typed = $class.Typed; createdon = $row.createdon }
        }
    }
    foreach ($row in @($Rows)) {
        $class = Get-StampBackfillClass $row $TypedColumns
        $counts.total++
        $counts[$class.Shape]++
        switch ($class.Shape) {
            'coreSubject' { & $take 'coreSubject' $row $class }
            'intermediate' { if ($class.StampEmpty) { $counts.stampEmpty++; & $take 'stampEmpty' $row $class } else { $counts.stampSet++; & $take 'stampSet' $row $class } }
            default { & $take 'noSubject' $row $class }
        }
        $kept = $false
        foreach ($c in $class.Typed.Keys) {
            $typedCounts[$c].set++
            if ($class.Typed[$c].InStamp) { $typedCounts[$c].inStamp++ } else { $typedCounts[$c].notInStamp++; if (-not $kept) { & $take 'typedNotInStamp' $row $class; $kept = $true } }
        }
    }
    [pscustomobject]@{ table = $Table; counts = $counts; typed = $typedCounts; samples = $samples }
}

# ── Live part (never runs when the file is dot-sourced by its test through the AST) ──
$EnvironmentUrl = $EnvironmentUrl.TrimEnd('/')
$Api = "$EnvironmentUrl/api/data/v9.2"
$token = az account get-access-token --resource $EnvironmentUrl --query accessToken -o tsv 2>$null
if (-not $token) { throw "No Dataverse token for $EnvironmentUrl. Run 'az login' and retry." }
$headers = @{
    Authorization      = "Bearer $token"
    Accept             = 'application/json'
    'OData-MaxVersion' = '4.0'
    'OData-Version'    = '4.0'
    Prefer             = 'odata.maxpagesize=5000'
}
# Every row a query matches, following @odata.nextLink past Dataverse's 5,000-row page. GET only.
function Get-DvRows([string]$Path) {
    $rows = [System.Collections.Generic.List[object]]::new(); $next = "$Api/$Path"
    while ($next) {
        $page = Invoke-RestMethod -Uri $next -Headers $headers -Method Get
        foreach ($r in @($page.value)) { $rows.Add($r) }
        $next = $page.'@odata.nextLink'
    }
    , $rows
}

$report = [ordered]@{ environment = $EnvironmentUrl; generatedUtc = (Get-Date).ToUniversalTime().ToString('o'); mode = $(if ($Apply) { 'apply' } else { 'report' }); tables = @() }
foreach ($table in $Tables.Keys) {
    $t = $Tables[$table]
    $select = @($t.Id, 'sprk_regardingrecordid', 'sprk_corerecordid', 'createdon') + (@($CoreStampColumns + $IntermediateSubjectColumns + $t.Typed) | ForEach-Object { "_$($_)_value" })
    $rows = Get-DvRows "$($t.Set)?`$select=$($select -join ',')"
    $summary = Get-StampBackfillSummary $table $rows $t.Typed $t.Id $SampleSize
    $report.tables += $summary
    $c = $summary.counts
    Write-Host ("{0,-20} total={1} coreSubject={2} intermediate={3} (stampEmpty={4} stampSet={5}) noSubject={6}" -f $table, $c.total, $c.coreSubject, $c.intermediate, $c.stampEmpty, $c.stampSet, $c.noSubject)
    foreach ($col in $summary.typed.Keys) {
        $tc = $summary.typed[$col]
        Write-Host ("{0,-20}   typed {1}: set={2} inStamp={3} notInStamp={4}" -f '', $col, $tc.set, $tc.inStamp, $tc.notInStamp)
    }
}

if (-not $ReportPath) { $ReportPath = Join-Path ([System.IO.Path]::GetTempPath()) "ontology-stamp-backfill-$((Get-Date).ToString('yyyyMMdd-HHmmss')).json" }
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ReportPath -Encoding utf8
Write-Host "Report: $ReportPath"

if (-not $Apply) {
    Write-Host 'Report only: nothing was written. The backfill is -Apply (see the help for its prerequisites).'
    return
}

# ── -Apply: release the stamp job's hold on sprk_signal / sprk_decisionrecord ──
$current = az webapp config appsettings list --resource-group $ResourceGroup --name $WebAppName --query "[?name=='$SettingName'].value | [0]" -o tsv 2>$null
Write-Host "Current $SettingName on $WebAppName = '$current' (absent = the default hold: sprk_signal,sprk_decisionrecord)"
if ($current -eq $SettingReleaseValue) { Write-Host 'Already released; nothing to change.'; return }
if ($PSCmdlet.ShouldProcess("$ResourceGroup/$WebAppName", "set $SettingName='$SettingReleaseValue' (the stamp job then writes Signals and Decision Records every 5 minutes)")) {
    az webapp config appsettings set --resource-group $ResourceGroup --name $WebAppName --settings "$SettingName=$SettingReleaseValue" --output none
    if ($LASTEXITCODE -ne 0) { throw "az webapp config appsettings set failed (exit $LASTEXITCODE)." }
    Write-Host "Released. Re-run this script without -Apply in ~10 minutes: stampEmpty should be 0 on both tables."
}
