# tests/scripts/Deploy-AllIndexes.Tests.ps1
# ---------------------------------------------------------------------------
# Row: customer-provisioning-orchestration-r1 punch row A43
#   (projects/customer-provisioning-orchestration-r1/notes/auth-v4-integration-draft-punch-rows.md)
#
# §11 justification (root CLAUDE.md — Existing / Extension / Cost-of-doing-nothing):
#   Existing  — `scripts/ai-search/Deploy-AllIndexes.ps1` already carries the `-CutoverBffSettings`
#               MI-check refusal shape (lines ~753-776) that A43 extends to the general admin-key
#               resolution path via the new `Resolve-AiSearchAuthContext` function; the shared
#               secret-free-marker detector (`scripts/common/Assert-SpaarkeSecretFreeGate.ps1`,
#               punch row A38c) already has its own Pester coverage in
#               `tests/scripts/Auth-V4-Operator-Script-Gates.Tests.ps1`.
#   Extension — this file adds the FIRST test coverage for `Deploy-AllIndexes.ps1` itself (none
#               existed before A43), exercising the new three-branch gate via the same
#               function-extraction + child-process techniques already established by the A38c
#               test file (Pester 3.4-only environment — no `Mock` support for external
#               applications; `az`/`Get-AzAccessToken` are shadowed as plain functions instead).
#   Cost-of-doing-nothing — without this coverage, a future edit to the gate (or an accidental
#               reordering that lets `az search admin-key show` execute before the secret-free
#               check) would go undetected until a live index re-deploy against a secret-free
#               environment silently re-minted the AI Search admin key — the exact §10.5 trap 2
#               this row exists to close.
#
# Technique note: `Resolve-AiSearchAuthContext` is defined INLINE in Deploy-AllIndexes.ps1 (per
# the row's "script IS the catalog authority — keep the gate inline" constraint), not extracted to
# a separate function-library file. To unit-test it without executing the rest of the script (which
# has mandatory top-level flow, including several `exit` statements before the function's
# definition point is reached under some parameter combinations), this file extracts JUST that
# function's source text via the PowerShell AST parser and re-defines it in an isolated scope
# (in-process for non-exiting branches; a child `pwsh` process for exit-calling branches — exit
# would otherwise terminate the Pester test runner itself, same rationale as the A38c test file's
# Invoke-A38cGateChildProcess helper).
# ---------------------------------------------------------------------------

$repoRoot     = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$deployScript = Join-Path $repoRoot 'scripts/ai-search/Deploy-AllIndexes.ps1'
$deployScriptForwardSlash = $deployScript -replace '\\', '/'
$gateScript   = Join-Path $repoRoot 'scripts/common/Assert-SpaarkeSecretFreeGate.ps1'
$gateScriptForwardSlash = $gateScript -replace '\\', '/'

# Shared marker-detection helper needed by Resolve-AiSearchAuthContext (dot-sourced for in-process
# tests; child-process tests dot-source it independently inside their own temp script).
. $gateScript

function Get-A43GateFunctionSource {
    <#
    .SYNOPSIS
    Extracts the literal source text of Resolve-AiSearchAuthContext from Deploy-AllIndexes.ps1
    via the PowerShell AST parser, so it can be redefined in an isolated scope without running
    the rest of the script (which has mandatory top-level flow + several `exit` statements).
    #>
    $tokens = $null
    $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($deployScript, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors -and $parseErrors.Count -gt 0) {
        throw "Deploy-AllIndexes.ps1 failed to parse: $($parseErrors -join '; ')"
    }
    $fn = $ast.FindAll(
        { param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Resolve-AiSearchAuthContext' },
        $true
    ) | Select-Object -First 1
    if (-not $fn) {
        throw "Resolve-AiSearchAuthContext not found in Deploy-AllIndexes.ps1 — has punch row A43's gate been renamed or removed?"
    }
    return $fn.Extent.Text
}

$script:A43GateSource = Get-A43GateFunctionSource

# ---------------------------------------------------------------------------
# az / Get-AzAccessToken shadow helpers (mirrors Auth-V4-Operator-Script-Gates.Tests.ps1's
# Set-A38cAzShadow technique — Pester 3.4.0's Mock cannot intercept CommandType=Application
# commands; a global function of the same name correctly shadows it instead, verified
# empirically by the A38c test file).
# ---------------------------------------------------------------------------
function Set-A43AzShadow {
    param([Parameter(Mandatory = $true)][scriptblock]$Body)
    Set-Item -Path function:global:az -Value $Body
}
function Remove-A43AzShadow {
    Remove-Item -Path function:global:az -ErrorAction SilentlyContinue
    Remove-Item -Path function:global:Get-AzAccessToken -ErrorAction SilentlyContinue
}

# A single parameterized az-shadow body reused across in-process + child-process tests. Branches
# on argument content to distinguish the 4 distinct `az` invocations this gate can make.
function New-A43AzShadowBody {
    param(
        [string]$MiEnabledValue        = '',
        [string]$KvSecretValue         = '',
        [string]$KvTagValue            = '',
        [string]$CliAccessTokenValue   = 'CLI-FALLBACK-TOKEN',
        [string]$AdminKeyShowValue     = 'LIVE-ADMIN-KEY-VALUE'
    )
    return @"
        if (`$args -contains 'appsettings') {
            `$global:LASTEXITCODE = 0
            return '$MiEnabledValue'
        } elseif (`$args -contains 'get-access-token') {
            `$global:LASTEXITCODE = 0
            return '$CliAccessTokenValue'
        } elseif ((`$args -contains 'secret') -and (`$args -contains 'show')) {
            `$global:LASTEXITCODE = 0
            return '$KvSecretValue'
        } elseif (`$args -contains 'admin-key') {
            `$global:A43AdminKeyShowCalled = `$true
            `$global:LASTEXITCODE = 0
            return '$AdminKeyShowValue'
        } elseif (`$args -contains 'keyvault') {
            `$global:LASTEXITCODE = 0
            return '$KvTagValue'
        } else {
            `$global:LASTEXITCODE = 0
            return ''
        }
"@
}

function Invoke-A43GateChildProcess {
    <#
    .SYNOPSIS
    Runs Resolve-AiSearchAuthContext in a fresh child pwsh process — required for any branch that
    calls `exit`, which would otherwise terminate the Pester test runner itself.
    #>
    param(
        [Parameter(Mandatory = $true)][string]$AzMockBody,
        [Parameter(Mandatory = $true)][string]$InvokeCall,
        [switch]$ClearPSModulePath
    )

    $tmpScript = Join-Path ([System.IO.Path]::GetTempPath()) ("a43-child-{0}.ps1" -f ([guid]::NewGuid()))
    $modulePathReset = if ($ClearPSModulePath) { '$env:PSModulePath = ""' } else { '' }
    $childSource = @"
Set-StrictMode -Version Latest
. '$gateScriptForwardSlash'
$($script:A43GateSource)
function az {
$AzMockBody
}
$modulePathReset
$InvokeCall
Write-Output '__A43_TEST_REACHED_END__'
"@
    Set-Content -Path $tmpScript -Value $childSource -Encoding UTF8

    try {
        $output = & pwsh -NoProfile -File $tmpScript 2>&1 | Out-String
        $exitCode = $LASTEXITCODE
        return [PSCustomObject]@{ ExitCode = $exitCode; Output = $output }
    }
    finally {
        Remove-Item -Path $tmpScript -ErrorAction SilentlyContinue
    }
}

# ---------------------------------------------------------------------------
Describe "Resolve-AiSearchAuthContext (A43 - S10.5 trap 2 three-branch gate)" {

    AfterEach {
        Remove-A43AzShadow
    }

    Context "Branch 2 - MI enabled (AAD-token path)" {

        It "uses Get-AzAccessToken when available and returns a Bearer header (no admin key)" {
            . ([scriptblock]::Create($script:A43GateSource))
            Set-A43AzShadow ([scriptblock]::Create((New-A43AzShadowBody -MiEnabledValue 'true')))
            Set-Item -Path function:global:Get-AzAccessToken -Value {
                param($ResourceUrl)
                [PSCustomObject]@{ Token = 'AZACCOUNTS-TOKEN' }
            }

            $result = Resolve-AiSearchAuthContext -Environment 'dev' -SearchServiceName 'spaarke-search-dev' -ResourceGroup 'rg-test' -KeyVaultName 'kv-test'

            $result.UsingAadAuth | Should Be $true
            $result.Headers['Authorization'] | Should Be 'Bearer AZACCOUNTS-TOKEN'
            $result.Headers.ContainsKey('api-key') | Should Be $false
        }

        It "never calls 'az search admin-key show' when MI is enabled" {
            . ([scriptblock]::Create($script:A43GateSource))
            Set-Item -Path function:global:Get-AzAccessToken -Value {
                param($ResourceUrl)
                [PSCustomObject]@{ Token = 'AZACCOUNTS-TOKEN' }
            }
            Set-A43AzShadow {
                if ($args -contains 'admin-key') {
                    throw "REGRESSION: 'az search admin-key show' must never execute when MI is enabled (A43 branch 2)"
                }
                if ($args -contains 'appsettings') { $global:LASTEXITCODE = 0; return 'true' }
                $global:LASTEXITCODE = 0
                return ''
            }

            { Resolve-AiSearchAuthContext -Environment 'dev' -SearchServiceName 'spaarke-search-dev' -ResourceGroup 'rg-test' -KeyVaultName 'kv-test' } | Should Not Throw
        }

        It "falls back to 'az account get-access-token' when Get-AzAccessToken is unavailable" {
            $r = Invoke-A43GateChildProcess -ClearPSModulePath `
                -AzMockBody (New-A43AzShadowBody -MiEnabledValue 'true' -CliAccessTokenValue 'CLI-FALLBACK-TOKEN') `
                -InvokeCall "Remove-Item Function:\Get-AzAccessToken -ErrorAction SilentlyContinue; `$r = Resolve-AiSearchAuthContext -Environment 'dev' -SearchServiceName 'spaarke-search-dev' -ResourceGroup 'rg-test' -KeyVaultName 'kv-test'; Write-Output (`"HEADER=`$(`$r.Headers['Authorization'])`")"

            $r.Output | Should Match 'HEADER=Bearer CLI-FALLBACK-TOKEN'
            $r.Output | Should Match '__A43_TEST_REACHED_END__'
        }
    }

    Context "Branch 1 - secret-free marker present, secret missing (FAIL LOUD)" {

        It "exits with code 10 and never proceeds (no admin-key mint)" {
            $r = Invoke-A43GateChildProcess `
                -AzMockBody (New-A43AzShadowBody -MiEnabledValue '' -KvSecretValue '' -KvTagValue 'true') `
                -InvokeCall "Resolve-AiSearchAuthContext -Environment 'dev' -SearchServiceName 'spaarke-search-dev' -ResourceGroup 'rg-test' -KeyVaultName 'kv-test'"

            $r.ExitCode | Should Be 10
            $r.Output   | Should Not Match '__A43_TEST_REACHED_END__'
        }

        It "names the marker and links the A38a omit contract in the refusal message" {
            $r = Invoke-A43GateChildProcess `
                -AzMockBody (New-A43AzShadowBody -MiEnabledValue '' -KvSecretValue '' -KvTagValue 'true') `
                -InvokeCall "Resolve-AiSearchAuthContext -Environment 'dev' -SearchServiceName 'spaarke-search-dev' -ResourceGroup 'rg-test' -KeyVaultName 'kv-test'"

            $r.Output | Should Match 'AiSearch--AdminKey'
            $r.Output | Should Match 'spaarke-secret-free-identity'
            $r.Output | Should Match 'A38a'
            $r.Output | Should Match 'auth\.md'
        }

        It "is idempotent - repeated invocations against a marker-tagged vault refuse consistently (no mint)" {
            foreach ($i in 1..3) {
                $r = Invoke-A43GateChildProcess `
                    -AzMockBody (New-A43AzShadowBody -MiEnabledValue '' -KvSecretValue '' -KvTagValue 'true') `
                    -InvokeCall "Resolve-AiSearchAuthContext -Environment 'dev' -SearchServiceName 'spaarke-search-dev' -ResourceGroup 'rg-test' -KeyVaultName 'kv-test'"
                $r.ExitCode | Should Be 10
                $r.Output   | Should Not Match '__A43_TEST_REACHED_END__'
            }
        }
    }

    Context "Branch 3 - no marker, no MI flag (backwards-compatible pre-migration fallback)" {

        It "falls back to live admin-key show unchanged and uses an 'api-key' header" {
            . ([scriptblock]::Create($script:A43GateSource))
            Set-A43AzShadow ([scriptblock]::Create((New-A43AzShadowBody -MiEnabledValue '' -KvSecretValue '' -KvTagValue '' -AdminKeyShowValue 'LIVE-KEY-123')))

            $result = Resolve-AiSearchAuthContext -Environment 'dev' -SearchServiceName 'spaarke-search-dev' -ResourceGroup 'rg-test' -KeyVaultName 'kv-test'

            $result.UsingAadAuth | Should Be $false
            $result.Headers['api-key'] | Should Be 'LIVE-KEY-123'
            $result.Headers.ContainsKey('Authorization') | Should Be $false
            $global:A43AdminKeyShowCalled | Should Be $true
        }

        It "uses the KV secret directly (no live admin-key call) when the KV secret IS present" {
            . ([scriptblock]::Create($script:A43GateSource))
            $global:A43AdminKeyShowCalled = $false
            Set-A43AzShadow ([scriptblock]::Create((New-A43AzShadowBody -MiEnabledValue '' -KvSecretValue 'KV-KEY-456' -KvTagValue '')))

            $result = Resolve-AiSearchAuthContext -Environment 'dev' -SearchServiceName 'spaarke-search-dev' -ResourceGroup 'rg-test' -KeyVaultName 'kv-test'

            $result.Headers['api-key'] | Should Be 'KV-KEY-456'
            $global:A43AdminKeyShowCalled | Should Be $false
        }

        It "resolves via live admin-key show unchanged when no -KeyVaultName is supplied (legacy invocation shape)" {
            . ([scriptblock]::Create($script:A43GateSource))
            Set-A43AzShadow ([scriptblock]::Create((New-A43AzShadowBody -MiEnabledValue '' -AdminKeyShowValue 'LIVE-KEY-NOKV')))

            $result = Resolve-AiSearchAuthContext -Environment 'dev' -SearchServiceName 'spaarke-search-dev' -ResourceGroup 'rg-test'

            $result.Headers['api-key'] | Should Be 'LIVE-KEY-NOKV'
        }

        It "is idempotent (byte-identical, integration-style) - repeated invocations against a pre-migration env resolve the same header every time" {
            foreach ($i in 1..3) {
                $r = Invoke-A43GateChildProcess `
                    -AzMockBody (New-A43AzShadowBody -MiEnabledValue '' -KvSecretValue '' -KvTagValue '' -AdminKeyShowValue 'STABLE-KEY') `
                    -InvokeCall "`$r = Resolve-AiSearchAuthContext -Environment 'dev' -SearchServiceName 'spaarke-search-dev' -ResourceGroup 'rg-test' -KeyVaultName 'kv-test'; Write-Output (`"HEADER=`$(`$r.Headers['api-key'])`")"
                $r.Output | Should Match 'HEADER=STABLE-KEY'
                $r.ExitCode | Should Be 0
            }
        }
    }
}

Describe "Deploy-AllIndexes.ps1 static regression guards (A43 no-regress obligations)" {

    $source = Get-Content -Raw $deployScript

    It "the -CutoverBffSettings gate block remains present and untouched in shape" {
        $source | Should Match 'GUARD \(added 2026-08-25, spaarke-auth-v4-dataverse-MI task 090\)'
        $source | Should Match "-CutoverBffSettings refused: '\`$bffAppName' is on managed-identity AI Search auth\."
        $source | Should Match "-CutoverBffSettings refused: 'AiSearch--AdminKey' not found in '\`$KeyVaultName'\."
    }

    It "the gate comment block documents Section 10.5 trap 2 and the three-branch decision" {
        $source | Should Match '§10\.5 trap 2'
        $source | Should Match 'Three-branch decision'
    }

    It "the gate comment block documents Model 1, Model 2, and N per-customer fleet consistency" {
        $source | Should Match 'Model 1'
        $source | Should Match 'Model 2'
        $source | Should Match 'per-customer'
    }

    It "the gate comment block states the A38 tag scheme is FINALIZED (no TODO placeholder)" {
        $source | Should Match 'FINALIZED, not a placeholder'
        $source | Should Not Match 'TODO\(A38\)'
    }

    It "dot-sources the shared A38a/A38c marker-detection helper (extend, not duplicate - CLAUDE.md S11)" {
        $source | Should Match "common/Assert-SpaarkeSecretFreeGate\.ps1"
    }

    It "branch 1 never calls 'az search admin-key show' before the secret-free check (source-order guard)" {
        $refuseIndex = $source.IndexOf('REFUSED:')
        $adminKeyShowIndex = $source.IndexOf("az search admin-key show", $source.IndexOf('function Resolve-AiSearchAuthContext'))
        $refuseIndex | Should Not Be -1
        $adminKeyShowIndex | Should Not Be -1
        ($refuseIndex -lt $adminKeyShowIndex) | Should Be $true
    }

    It "parses cleanly via the PowerShell AST parser" {
        $parseErrors = $null
        $null = [System.Management.Automation.Language.Parser]::ParseFile($deployScript, [ref]$null, [ref]$parseErrors)
        $parseErrors.Count | Should Be 0
    }
}
