# tests/scripts/Deploy-EbillingStoredFileSchema.Tests.ps1
# ---------------------------------------------------------------------------
# ebilling-r1 task 088: scripts/ebilling/Deploy-EbillingStoredFileSchema.ps1. These tests prove, with no live call:
#   1. the plan (Get-StoredFilePlan): exactly the six lookups of F2 with their targets and delete behaviours (the three parents
#      RemoveLink, the two sources Restrict, the creator person RemoveLink; nothing Cascade), the relationship names, every other
#      F2 column and F4 with its type and length; the F3 secured set is every F2 column except sprk_name and sprk_createdbyperson,
#      plus the marker; only sprk_blobpath has no reader row (Get-Grants);
#   2. the F5 decision (Get-StatusDecision): Insert on exactly the seven task-012 values, Skip once Superseded is there, and an
#      ESCALATION (exit 2, nothing written) when 659490005 or the label exists with another meaning, a task-012 value is missing
#      or relabelled, or statuscode holds a value the task does not know;
#   3. the F1 values and the cascade comparison the verify uses;
#   4. the script text: no form, view, role, profile-member, invoice-row or document-row write, and no PublishAllXml.
# The script's constants are taken from the script itself (its top-level assignments), not retyped here.
#
# Pester 3.4:  Import-Module Pester -RequiredVersion 3.4.0 ; Invoke-Pester tests/scripts/Deploy-EbillingStoredFileSchema.Tests.ps1
# ---------------------------------------------------------------------------

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$script:ScriptPath = Join-Path $repoRoot 'scripts/ebilling/Deploy-EbillingStoredFileSchema.ps1'
$script:Text = Get-Content -LiteralPath $script:ScriptPath -Raw

# The script's EscalationException normally comes from the module (`using module`); a stand-in with the same name is enough here.
if (-not ('EscalationException' -as [type])) {
    Add-Type -TypeDefinition 'public class EscalationException : System.Exception { public EscalationException(string m) : base(m) {} }'
}

$errs = $null; $tokens = $null
$script:Ast = [System.Management.Automation.Language.Parser]::ParseFile($script:ScriptPath, [ref]$tokens, [ref]$errs)

# The script's own constants (top-level assignments), so a change there is what the tests see.
$constants = 'StoredFile', 'Document', 'Invoice', 'Version', 'Export', 'Communication', 'CreatorColumn', 'MarkerColumn', 'BlobPathColumn', 'PurposeChoice', 'FileStatusLabels', 'KnownStatus', 'SupersededStatus'
foreach ($st in $script:Ast.EndBlock.Statements) {
    if ($st -is [System.Management.Automation.Language.AssignmentStatementAst] -and $st.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and $st.Left.VariablePath.UserPath -in $constants) {
        Invoke-Expression $st.Extent.Text
    }
}

# The functions under test (the script cannot be dot-sourced: it runs on load).
foreach ($name in 'New-LookupSpec', 'Get-NewTableRel', 'Get-ExpectedCascade', 'Get-CascadeMismatches', 'Get-StoredFilePlan', 'Get-Grants', 'ConvertTo-LabelKey', 'Get-StatusDecision', 'Get-ChoiceOptions', 'Get-PublishEntities') {
    $fn = $script:Ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name }, $true) | Select-Object -First 1
    if (-not $fn) { throw "function $name not found in the script" }
    Invoke-Expression $fn.Extent.Text
}

# Variables Get-Grants reads from the script scope.
$WriterProfileName = 'Writers'; $ReaderProfileName = 'Readers'; $script:WriterId = 'w-id'; $script:ReaderId = 'r-id'

function New-Status([int]$Value, [int]$State, [string]$Label) { [pscustomobject]@{ Value = $Value; State = $State; Label = $Label } }
function Get-SevenStatuses { @($KnownStatus | ForEach-Object { New-Status $_.Value $_.State $_.Label }) }

Describe 'the constants taken from the script' {
    It 'were all found (the tests read the script, not a copy)' {
        $StoredFile | Should Be 'sprk_storedfile'
        $MarkerColumn | Should Be 'sprk_restrictedbysubmission'
        @($KnownStatus).Count | Should Be 7
        $SupersededStatus.Value | Should Be 659490005
    }
}

Describe 'Get-StoredFilePlan (F2, F4 and the F3 secured set)' {
    $plan = Get-StoredFilePlan
    $lookups = @($plan.Lookups)

    It 'has exactly the six F2 lookups, each on sprk_storedfile, with its target' {
        $lookups.Count | Should Be 6
        (($lookups | ForEach-Object { "$($_.Table).$($_.Column)->$($_.Target)" } | Sort-Object) -join ',') |
            Should Be 'sprk_storedfile.sprk_billingexport->sprk_billingexport,sprk_storedfile.sprk_createdbyperson->systemuser,sprk_storedfile.sprk_invoice->sprk_invoice,sprk_storedfile.sprk_procedureversion->sprk_procedureversion,sprk_storedfile.sprk_sourcecommunication->sprk_communication,sprk_storedfile.sprk_sourcedocument->sprk_document'
    }
    It 'makes the three parent lookups RemoveLink (orphan reconcile, model 4.2)' {
        foreach ($c in 'sprk_procedureversion', 'sprk_invoice', 'sprk_billingexport') {
            ($lookups | Where-Object { $_.Column -eq $c }).Delete | Should Be 'RemoveLink'
        }
    }
    It 'makes exactly the two source lookups Restrict (uac-r2 answer B)' {
        (($lookups | Where-Object { $_.Delete -eq 'Restrict' } | ForEach-Object { $_.Column } | Sort-Object) -join ',') | Should Be 'sprk_sourcecommunication,sprk_sourcedocument'
    }
    It 'never makes a lookup Cascade (no relationship of sprk_storedfile is parental) (must-fire if one comes back)' {
        @($lookups | Where-Object { $_.Delete -notin 'RemoveLink', 'Restrict' }).Count | Should Be 0
    }
    It 'names the relationships {table}_{column}_{target} and the creator by the submission-schema shape; unique, at most 100 characters' {
        (($lookups | ForEach-Object { $_.Rel } | Sort-Object) -join ',') |
            Should Be 'sprk_storedfile_billingexport_billingexport,sprk_storedfile_invoice_invoice,sprk_storedfile_procedureversion_procedureversion,sprk_storedfile_sourcecommunication_communication,sprk_storedfile_sourcedocument_document,sprk_systemuser_sprk_storedfile_createdbyperson'
        @($lookups | ForEach-Object { $_.Rel } | Select-Object -Unique).Count | Should Be 6
        @($lookups | Where-Object { $_.Rel.Length -gt 100 }).Count | Should Be 0
    }
    It 'lists every other F2 column and F4 with its type and length' {
        $cols = (@($plan.Columns) | ForEach-Object { "$($_.Table).$($_.Column):$($_.Type)$(if ($_.MaxLength) { "/$($_.MaxLength)" })" } | Sort-Object) -join ','
        $cols | Should Be 'sprk_document.sprk_restrictedbysubmission:Boolean,sprk_storedfile.sprk_blobpath:String/1024,sprk_storedfile.sprk_contenttype:String/255,sprk_storedfile.sprk_filestatus:Picklist,sprk_storedfile.sprk_firmvisible:Boolean,sprk_storedfile.sprk_name:String/400,sprk_storedfile.sprk_purpose:Picklist,sprk_storedfile.sprk_sha256:String/64,sprk_storedfile.sprk_sizebytes:BigInt,sprk_storedfile.sprk_sourceattachmentidentity:String/1000,sprk_storedfile.sprk_sourcesha256:String/64,sprk_storedfile.sprk_sourceversion:BigInt'
    }
    It 'secures every F2 column except sprk_name and sprk_createdbyperson, plus the marker (16 columns)' {
        $sec = @($plan.Secured)
        $sec.Count | Should Be 16
        (($sec | ForEach-Object { "$($_.Table).$($_.Column)" } | Sort-Object) -join ',') |
            Should Be 'sprk_document.sprk_restrictedbysubmission,sprk_storedfile.sprk_billingexport,sprk_storedfile.sprk_blobpath,sprk_storedfile.sprk_contenttype,sprk_storedfile.sprk_filestatus,sprk_storedfile.sprk_firmvisible,sprk_storedfile.sprk_invoice,sprk_storedfile.sprk_procedureversion,sprk_storedfile.sprk_purpose,sprk_storedfile.sprk_sha256,sprk_storedfile.sprk_sizebytes,sprk_storedfile.sprk_sourceattachmentidentity,sprk_storedfile.sprk_sourcecommunication,sprk_storedfile.sprk_sourcedocument,sprk_storedfile.sprk_sourcesha256,sprk_storedfile.sprk_sourceversion'
    }
    It 'does not secure sprk_name or sprk_createdbyperson (task 086 secures the creator) (must-fire if one comes back)' {
        @($plan.Secured | Where-Object { $_.Column -in 'sprk_name', 'sprk_createdbyperson' }).Count | Should Be 0
    }
    It 'gives every secured column the metadata kind its column has' {
        foreach ($s in $plan.Secured) {
            $want = if ($lookups | Where-Object { $_.Column -eq $s.Column -and $_.Table -eq $s.Table }) { 'Lookup' } else { ($plan.Columns | Where-Object { $_.Column -eq $s.Column -and $_.Table -eq $s.Table }).Type }
            $s.Kind | Should Be $want
        }
    }
    It 'leaves the reader row off sprk_blobpath only (server-only)' {
        (($plan.Secured | Where-Object { -not $_.Reader } | ForEach-Object { $_.Column }) -join ',') | Should Be 'sprk_blobpath'
    }
}

Describe 'Get-Grants (F3 permission rows)' {
    It 'grants the writer (read/create/update) then the reader (read only) on an ordinary secured column' {
        $g = @(Get-Grants @{ Table = 'sprk_storedfile'; Column = 'sprk_purpose'; Kind = 'Picklist'; Reader = $true })
        $g.Count | Should Be 2
        $g[0].Name | Should Be 'Writers'; $g[0].Write | Should Be 4; $g[0].Id | Should Be 'w-id'
        $g[1].Name | Should Be 'Readers'; $g[1].Write | Should Be 0; $g[1].Id | Should Be 'r-id'
    }
    It 'grants the writer ONLY on a server-only column (must-fire: no reader row on sprk_blobpath)' {
        $g = @(Get-Grants @{ Table = 'sprk_storedfile'; Column = 'sprk_blobpath'; Kind = 'String'; Reader = $false })
        $g.Count | Should Be 1
        $g[0].Name | Should Be 'Writers'
    }
}

Describe 'Get-StatusDecision (F5)' {
    It 'inserts on exactly the seven task-012 values (must-not-fire)' {
        Get-StatusDecision (Get-SevenStatuses) | Should Be 'Insert'
    }
    It 'skips when Superseded is already 659490005 at statecode 1 (second run)' {
        Get-StatusDecision (@(Get-SevenStatuses) + @(New-Status 659490005 1 'Superseded')) | Should Be 'Skip'
    }
    It 'escalates when 659490005 is used by another label (must-fire)' {
        { Get-StatusDecision (@(Get-SevenStatuses) + @(New-Status 659490005 1 'On Hold')) } | Should Throw 'already used by'
    }
    It 'escalates when Superseded sits at 659490005 under statecode 0 (must-fire)' {
        { Get-StatusDecision (@(Get-SevenStatuses) + @(New-Status 659490005 0 'Superseded')) } | Should Throw 'already used by'
    }
    It 'escalates when the label exists at another value, case and spaces ignored (must-fire)' {
        { Get-StatusDecision (@(Get-SevenStatuses) + @(New-Status 659490009 1 'superseded')) } | Should Throw 'already exists as value 659490009'
    }
    It 'escalates when a task-012 value is relabelled or moved to the other state (must-fire)' {
        $s = @(Get-SevenStatuses); $s[4] = New-Status 659490002 1 'Settled'
        { Get-StatusDecision $s } | Should Throw "value 659490002 is 'Settled'"
        $s = @(Get-SevenStatuses); $s[2] = New-Status 659490000 1 'Approved'
        { Get-StatusDecision $s } | Should Throw 'value 659490000'
    }
    It 'escalates when a task-012 value is missing (must-fire)' {
        { Get-StatusDecision @(Get-SevenStatuses | Where-Object { $_.Value -ne 659490004 }) } | Should Throw 'is missing'
    }
    It 'escalates on a value the task does not know (must-fire)' {
        { Get-StatusDecision (@(Get-SevenStatuses) + @(New-Status 659490007 0 'Disputed')) } | Should Throw 'does not know'
    }
}

Describe 'F1 values and the cascade comparison' {
    It 'numbers the purposes 100000000-100000003 in the task order' {
        ((Get-ChoiceOptions $PurposeChoice.Labels | ForEach-Object { "$($_.Value)=$($_.Label)" }) -join ',') |
            Should Be '100000000=Procedure source,100000001=Invoice PDF,100000002=Invoice supporting file,100000003=Export file'
    }
    It 'numbers the file statuses Not yet servable, Servable, Removed' {
        ($FileStatusLabels -join ',') | Should Be 'Not yet servable,Servable,Removed'
    }
    It 'is silent on the planned configuration and fires on another delete behaviour or a cascading member' {
        $spec = New-LookupSpec 'sprk_storedfile' 'sprk_sourcedocument' 'x' 'r' 'sprk_document' 'd' 'd' 'Restrict'
        $exact = [pscustomobject]@{ Delete = 'Restrict'; Assign = 'NoCascade'; Share = 'NoCascade'; Unshare = 'NoCascade'; Reparent = 'NoCascade'; Merge = 'NoCascade' }
        @(Get-CascadeMismatches $exact (Get-ExpectedCascade $spec)).Count | Should Be 0
        $removeLink = [pscustomobject]@{ Delete = 'RemoveLink'; Assign = 'NoCascade'; Share = 'NoCascade'; Unshare = 'NoCascade'; Reparent = 'NoCascade' }
        @(Get-CascadeMismatches $removeLink (Get-ExpectedCascade $spec)).Count | Should Be 1
        $shares = [pscustomobject]@{ Delete = 'Restrict'; Assign = 'NoCascade'; Share = 'Cascade'; Unshare = 'NoCascade'; Reparent = 'NoCascade' }
        @(Get-CascadeMismatches $shares (Get-ExpectedCascade $spec)).Count | Should Be 1
    }
    It 'publishes only the three tables it changes' {
        ((Get-PublishEntities) -join ',') | Should Be 'sprk_storedfile,sprk_document,sprk_invoice'
    }
}

Describe 'the script text (what it never does)' {
    It 'creates only the sprk_storedfile table' {
        @([regex]::Matches($script:Text, 'Add-Table\s+-Table\s+\$(\w+)') | ForEach-Object { $_.Groups[1].Value }) -join ',' | Should Be 'StoredFile'
    }
    It 'creates no form, view, chart or sitemap entry' {
        $script:Text -match '(?i)systemforms|savedqueries|savedqueryvisualizations|sitemaps|FormXml' | Should Be $false
    }
    It 'adds no role privilege' {
        $script:Text -match '(?i)AddPrivilegesRole|ReplacePrivilegesRole|roleprivileges|RemovePrivilegeRole' | Should Be $false
    }
    It 'never adds or removes a profile member (reads the associations only, never posts a $ref)' {
        $script:Text -match '(?i)_association/`?\$ref' | Should Be $false
    }
    It 'never writes an invoice or a document ROW (no record path: no invoice is set to Superseded, no marker is written)' {
        $script:Text -match "(?i)-Path\s+[`"']sprk_(invoices|documents)" | Should Be $false
        $script:Text -match '(?i)-Method\s+(PATCH|DELETE)' | Should Be $false
    }
    It 'publishes entity-scoped only (no PublishAllXml)' {
        $script:Text -match "(?i)-Path\s+['`"]PublishAllXml" | Should Be $false
        $script:Text -match "(?i)-Path\s+'PublishXml'" | Should Be $true
    }
    It 'refuses any environment but dev before the first read' {
        $script:Text -match "if \(\`$EnvironmentUrl -ne \`$DevUrl\)" | Should Be $true
    }
    It 'refuses (escalation) when sprk_procedureversion does not exist (task 080 first)' {
        $script:Text -match "throw \[EscalationException\]::new\(`"\`$Version does not exist" | Should Be $true
    }
}
