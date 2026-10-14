# Pester 5. Run: Invoke-Pester tests/scripts/Invoke-OntologyStampBackfill.Tests.ps1
# Ontology task 145 (D-131, D-34, 144 review K1): the stamp backfill's report classifies each Signal / Decision Record by its
# SUBJECT. Only a row filed under an intermediate with no sprk_regarding{core} stamp is in the definite backfill set; a row whose
# subject is a core already carries its stamp; a typed core column that is not in the stamp (a re-filed subject) is counted,
# never copied.
#
# The script talks to a live environment on load, so it is never run here. Its classification functions and constants are read
# out of the script's own AST: the code under test is the shipped code, not a copy.

BeforeAll {
    $script:ScriptPath = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path 'scripts' 'Invoke-OntologyStampBackfill.ps1'
    $errors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($script:ScriptPath, [ref]$null, [ref]$errors)
    if ($errors.Count -gt 0) { throw "The script does not parse: $($errors[0].Message)" }

    foreach ($variable in 'CoreStampColumns', 'IntermediateSubjectColumns') {
        $assign = $ast.Find({ param($n) $n -is [System.Management.Automation.Language.AssignmentStatementAst] -and "$($n.Left)" -eq "`$$variable" }, $false)
        if (-not $assign) { throw "The script no longer defines `$$variable" }
        . ([scriptblock]::Create($assign.Extent.Text))
    }
    foreach ($name in 'Get-LookupId', 'Get-StampBackfillClass', 'Get-StampBackfillSummary') {
        $fn = $ast.Find({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name }, $true)
        if (-not $fn) { throw "The script no longer defines $name" }
        . ([scriptblock]::Create($fn.Extent.Text))
    }

    $script:M = '11111111-1111-1111-1111-111111111111'
    $script:Q = '22222222-2222-2222-2222-222222222222'
    $script:Comm = '33333333-3333-3333-3333-333333333333'

    # A Web API row: lookups as _{column}_value, the pair id as Dataverse returns it.
    function New-Row([hashtable]$Lookups, [string]$PairId, [string]$Id = ([guid]::NewGuid().ToString())) {
        $row = [ordered]@{ sprk_decisionrecordid = $Id; sprk_regardingrecordid = $PairId; createdon = '2026-10-14T00:00:00Z' }
        foreach ($k in $Lookups.Keys) { $row["_$($k)_value"] = $Lookups[$k] }
        [pscustomobject]$row
    }
}

Describe 'Get-StampBackfillClass' {
    It 'the constants are the BFF''s four core stamp columns and five intermediate subject lookups' {
        $CoreStampColumns | Should -Be @('sprk_regardingmatter', 'sprk_regardingproject', 'sprk_regardingworkassignment', 'sprk_regardingservicerequest')
        $IntermediateSubjectColumns | Should -Be @('sprk_regardingcommunication', 'sprk_regardingdocument', 'sprk_regardingevent', 'sprk_regardinginvoice', 'sprk_regardingtodo')
    }

    It 'must fire: a row filed under a communication with no stamp needs the stamp' {
        $row = New-Row @{ sprk_regardingcommunication = $Comm; sprk_matter = $M } $Comm
        $c = Get-StampBackfillClass $row @('sprk_matter')
        $c.Shape | Should -Be 'intermediate'
        $c.SubjectColumn | Should -Be 'sprk_regardingcommunication'
        $c.NeedsStamp | Should -BeTrue
    }

    It 'must not fire: a row whose subject is the matter already carries its stamp' {
        $row = New-Row @{ sprk_regardingmatter = $M; sprk_matter = $M } "{$($M.ToUpperInvariant())}"
        $c = Get-StampBackfillClass $row @('sprk_matter')
        $c.Shape | Should -Be 'coreSubject'
        $c.NeedsStamp | Should -BeFalse
        $c.Typed['sprk_matter'].InStamp | Should -BeTrue
    }

    It 'must not fire: a stamped intermediate row is left to the job (staleness is the job''s derivation, not this script''s)' {
        $row = New-Row @{ sprk_regardingcommunication = $Comm; sprk_regardingmatter = $M; sprk_matter = $M } $Comm
        (Get-StampBackfillClass $row @('sprk_matter')).NeedsStamp | Should -BeFalse
    }

    It 'K1: after a re-file P->Q the typed core is NOT in the stamp, and it is reported, not copied' {
        $row = New-Row @{ sprk_regardingcommunication = $Comm; sprk_regardingmatter = $Q; sprk_matter = $M } $Comm
        $c = Get-StampBackfillClass $row @('sprk_matter')
        $c.Typed['sprk_matter'].InStamp | Should -BeFalse
        $c.Stamp['sprk_regardingmatter'] | Should -Be $Q
    }

    It 'must not fire: a typed core alone, with no subject lookup matching the pair, is noSubject (never inferred from the typed column)' {
        $row = New-Row @{ sprk_matter = $M } ''
        $c = Get-StampBackfillClass $row @('sprk_matter')
        $c.Shape | Should -Be 'noSubject'
        $c.NeedsStamp | Should -BeFalse
    }
}

Describe 'Get-StampBackfillSummary' {
    It 'counts every bucket and the typed columns, and caps the samples' {
        $rows = @(
            (New-Row @{ sprk_regardingcommunication = $Comm; sprk_matter = $M } $Comm),
            (New-Row @{ sprk_regardingcommunication = $Comm; sprk_matter = $M } $Comm),
            (New-Row @{ sprk_regardingcommunication = $Comm; sprk_regardingmatter = $Q; sprk_matter = $M } $Comm),
            (New-Row @{ sprk_regardingmatter = $M; sprk_matter = $M } $M),
            (New-Row @{ sprk_project = $Q } '')
        )
        $s = Get-StampBackfillSummary 'sprk_decisionrecord' $rows @('sprk_matter', 'sprk_project') 'sprk_decisionrecordid' 1
        $s.counts.total | Should -Be 5
        $s.counts.coreSubject | Should -Be 1
        $s.counts.intermediate | Should -Be 3
        $s.counts.stampEmpty | Should -Be 2
        $s.counts.stampSet | Should -Be 1
        $s.counts.noSubject | Should -Be 1
        $s.typed['sprk_matter'].set | Should -Be 4
        $s.typed['sprk_matter'].inStamp | Should -Be 1
        $s.typed['sprk_matter'].notInStamp | Should -Be 3
        $s.typed['sprk_project'].notInStamp | Should -Be 1
        @($s.samples.stampEmpty).Count | Should -Be 1
    }

    It 'an empty table reports zeros' {
        $s = Get-StampBackfillSummary 'sprk_signal' @() @('sprk_matter') 'sprk_signalid' 5
        $s.counts.total | Should -Be 0
        $s.counts.stampEmpty | Should -Be 0
    }
}
