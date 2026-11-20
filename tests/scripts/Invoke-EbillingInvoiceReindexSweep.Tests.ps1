# tests/scripts/Invoke-EbillingInvoiceReindexSweep.Tests.ps1
# ---------------------------------------------------------------------------
# ebilling-r1 ISS-022 / #2844: scripts/ebilling/Invoke-EbillingInvoiceReindexSweep.ps1. These tests prove, with no live call:
#   1. the message is the BFF's own job: the body is InvoiceIndexingJobHandler.BuildJob's JobContract in JobSubmissionService's camelCase
#      (job type InvoiceIndexing, subject = the invoice id, idempotency key invoice-index-{N}-{change}, attempt 1 of 3, payload
#      {InvoiceId, Change}), and the broker properties carry MessageId = the idempotency key and Label = the job type;
#   2. a refused input (empty invoice id, blank change) throws;
#   3. the date filter is UTC, on modifiedon, inclusive;
#   4. the script text: a dry run unless -Apply; GET only against Dataverse (the one POST is to the queue); Consistency: Strong; it prints no id.
# The functions are taken from the script itself (its AST), not retyped here.
#
# Pester 5.7.1 under pwsh 7:  Import-Module Pester -RequiredVersion 5.7.1 ; Invoke-Pester tests/scripts/Invoke-EbillingInvoiceReindexSweep.Tests.ps1
# ---------------------------------------------------------------------------
#Requires -Modules @{ ModuleName = 'Pester'; RequiredVersion = '5.7.1' }

BeforeAll {
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $script:ScriptPath = Join-Path $repoRoot 'scripts/ebilling/Invoke-EbillingInvoiceReindexSweep.ps1'
    $script:Text = Get-Content -LiteralPath $script:ScriptPath -Raw
    $errs = $null; $tokens = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($script:ScriptPath, [ref]$tokens, [ref]$errs)

    foreach ($st in $ast.EndBlock.Statements) {
        if ($st -is [System.Management.Automation.Language.AssignmentStatementAst] -and $st.Left.VariablePath.UserPath -in 'JobType', 'MaxAttempts') {
            Invoke-Expression $st.Extent.Text
        }
    }
    foreach ($name in 'Get-SinceFilter', 'New-InvoiceIndexJobMessage') {
        $fn = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name }, $true) | Select-Object -First 1
        if (-not $fn) { throw "function $name not found in the script" }
        Invoke-Expression $fn.Extent.Text
    }

    $script:InvoiceId = [Guid]'28440000-0000-4000-8000-000000000001'
    $script:JobId = [Guid]'28440000-0000-4000-8000-0000000000aa'
    $script:Created = [datetimeoffset]'2026-11-15T19:00:00+00:00'
}

Describe 'New-InvoiceIndexJobMessage (the BFF job, as JobSubmissionService sends it)' {
    BeforeAll {
        $script:Message = New-InvoiceIndexJobMessage -InvoiceId $InvoiceId -Change 'sweep-2844' -CreatedAt $Created -JobId $JobId -CorrelationId 'corr-1'
        $script:Body = $Message.Body | ConvertFrom-Json
        $script:Broker = $Message.BrokerProperties | ConvertFrom-Json
    }

    It 'carries the job type, subject, attempt and the BuildJob idempotency key' {
        $Body.jobType | Should -Be 'InvoiceIndexing'
        $Body.subjectId | Should -Be '28440000-0000-4000-8000-000000000001'
        $Body.idempotencyKey | Should -Be 'invoice-index-28440000000040008000000000000001-sweep-2844'
        $Body.attempt | Should -Be 1
        $Body.maxAttempts | Should -Be 3
        $Body.jobId | Should -Be '28440000-0000-4000-8000-0000000000aa'
        $Body.correlationId | Should -Be 'corr-1'
    }

    It 'uses camelCase envelope names the processor reads, and the PascalCase payload BuildJob writes' {
        @($Body.PSObject.Properties.Name) | Sort-Object | Should -Be (@('attempt', 'correlationId', 'createdAt', 'idempotencyKey', 'jobId', 'jobType', 'maxAttempts', 'payload', 'subjectId') | Sort-Object)
        @($Body.payload.PSObject.Properties.Name) | Should -Be @('InvoiceId', 'Change')
        $Body.payload.InvoiceId | Should -Be '28440000-0000-4000-8000-000000000001'
        $Body.payload.Change | Should -Be 'sweep-2844'
    }

    It 'sets MessageId to the idempotency key (duplicate detection) and Label to the job type' {
        $Broker.MessageId | Should -Be $Body.idempotencyKey
        $Broker.Label | Should -Be 'InvoiceIndexing'
        $Broker.CorrelationId | Should -Be 'corr-1'
        $Message.Properties.JobType | Should -Be 'InvoiceIndexing'
        $Message.Properties.Attempt | Should -Be '1'
    }

    It 'keeps createdAt as a round-trip date' {
        ([datetimeoffset]$Body.createdAt) | Should -Be $Created
    }

    It 'refuses an empty invoice id' {
        { New-InvoiceIndexJobMessage -InvoiceId ([Guid]::Empty) -Change 'x' -CreatedAt $Created -JobId $JobId -CorrelationId 'c' } | Should -Throw
    }

    It 'refuses a blank change' {
        { New-InvoiceIndexJobMessage -InvoiceId $InvoiceId -Change ' ' -CreatedAt $Created -JobId $JobId -CorrelationId 'c' } | Should -Throw
    }
}

Describe 'Get-SinceFilter' {
    It 'is an inclusive UTC modifiedon filter at the start of the day' {
        Get-SinceFilter ([datetime]'2026-10-27') | Should -Be 'modifiedon ge 2026-10-27T00:00:00Z'
        Get-SinceFilter ([datetime]'2026-10-27 15:30') | Should -Be 'modifiedon ge 2026-10-27T00:00:00Z'
    }
}

Describe 'the script text' {
    It 'is a dry run unless -Apply is given' {
        $Text | Should -Match '\[switch\]\$Apply'
        $Text | Should -Match 'if \(-not \$Apply\)'
    }

    It 'reads Dataverse with GET only; its one POST goes to the queue' {
        ([regex]::Matches($Text, '-Method\s+(Post|Patch|Put|Delete)', 'IgnoreCase')).Count | Should -Be 1
        $Text | Should -Match 'Invoke-RestMethod -Method Post -Uri \$Uri'
        $Text | Should -Match 'Invoke-RestMethod -Method Get -Uri \$url'
    }

    It 'asks Dataverse for a strongly consistent read' {
        $Text | Should -Match "Consistency\s+=\s+'Strong'"
    }

    It 'prints counts, never an invoice id' {
        $Text | Should -Not -Match 'Write-Host[^\r\n]*\$id\b'
    }
}
