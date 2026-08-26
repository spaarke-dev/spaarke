# tests/scripts/Auth-V4-Operator-Script-Gates.Tests.ps1
# ---------------------------------------------------------------------------
# Row: customer-provisioning-orchestration-r1 punch row A38c
#   (projects/customer-provisioning-orchestration-r1/notes/auth-v4-integration-draft-punch-rows.md)
#
# §11 justification (root CLAUDE.md — Existing / Extension / Cost-of-doing-nothing):
#   Existing  — no PowerShell test surface exists anywhere in this repo (grep-verified 2026-08-26,
#               row A38c Step 9); only Pester 3.4.0 is installed (bundled Windows PowerShell module
#               path), not a repo dependency. This file is written against Pester 3.4 syntax
#               (`Describe`/`Context`/`It`, `Should Be`, `Mock`) so it actually runs with the
#               tooling present, rather than authoring against a Pester 5 API this environment
#               cannot execute.
#   Extension — the "extension" here is of the TEST PYRAMID, not an existing script test file:
#               this is the first PowerShell-script test file in the repo, exercising the shared
#               A38c gate (scripts/common/Assert-SpaarkeSecretFreeGate.ps1) that all three gated
#               operator scripts consume, plus source-level regression guards on the three scripts
#               themselves.
#   Cost-of-doing-nothing — without these tests, a future edit to the shared gate function (or an
#               accidental re-introduction of a BFF-API-ClientSecret / Dataverse-ClientSecret write
#               path in one of the three gated scripts) would go undetected until a live rotation /
#               seed run against a secret-free or unmigrated environment produced the exact silent
#               reversal this row was created to close.
#
# Escalation note (row A38c `tests-unavailable` trigger, per POML): a FULL Pester-infra decision
# (version pin, CI wiring, mocking-harness conventions for `az` CLI calls) is out of this row's
# 1.5h scope. This file works within the Pester version ALREADY installed rather than proposing new
# test infrastructure — no escalation fired. Manual smoke coverage (parse-check + grep-verify) is
# additionally documented in the row's task report per the trigger's sanctioned interim.
# ---------------------------------------------------------------------------

$repoRoot   = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$gateScript = Join-Path $repoRoot 'scripts/common/Assert-SpaarkeSecretFreeGate.ps1'
$gateScriptForwardSlash = $gateScript -replace '\\', '/'

. $gateScript

# ---------------------------------------------------------------------------
# Helper: shadow the real `az` executable for the duration of one test. Pester 3.4.0's `Mock`
# cmdlet (the only version installed in this environment — grep-verified 2026-08-26, no repo Pester
# dependency exists) cannot intercept `CommandType=Application` commands (verified empirically: a
# `Mock -CommandName 'az'` in this Pester version silently no-ops and the REAL `az` CLI still runs).
# A plain PowerShell function defined in global scope DOES correctly shadow the external `az.cmd`
# for unqualified calls (PowerShell's command-resolution precedence favors Function over
# Application) — verified empirically. This helper wraps that pattern so callers don't restate it.
# ---------------------------------------------------------------------------
function Set-A38cAzShadow {
    param([Parameter(Mandatory = $true)][scriptblock]$Body)
    Set-Item -Path function:global:az -Value $Body
}
function Remove-A38cAzShadow {
    Remove-Item -Path function:global:az -ErrorAction SilentlyContinue
}

# ---------------------------------------------------------------------------
# Helper: run a snippet in a CHILD pwsh process. Assert-SpaarkeSecretFreeGateNotTripped calls
# `exit 7` on refusal, which would terminate the Pester test runner itself if invoked in-process
# with the marker tripped — spawning a child process is the standard-safe pattern for testing
# exit-calling code. The child process defines its OWN plain `function az { ... }` shadow (same
# technique as Set-A38cAzShadow above), since it runs as fresh process state.
# ---------------------------------------------------------------------------
function Invoke-A38cGateChildProcess {
    param(
        [Parameter(Mandatory = $true)][string]$AzMockBody,
        [Parameter(Mandatory = $true)][string]$AssertCall
    )

    $tmpScript = Join-Path ([System.IO.Path]::GetTempPath()) ("a38c-child-{0}.ps1" -f ([guid]::NewGuid()))
    $childSource = @"
Set-StrictMode -Version Latest
. '$gateScriptForwardSlash'
function az { $AzMockBody }
$AssertCall
Write-Output '__A38C_TEST_REACHED_END__'
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

Describe "Test-SpaarkeSecretFreeMarker (A38c)" {

    AfterEach {
        Remove-A38cAzShadow
    }

    It "returns true when -CredentialMode is 'secret-free' (case-insensitive), without calling az" {
        Set-A38cAzShadow { throw "az must not be called when CredentialMode short-circuits" }
        $result = Test-SpaarkeSecretFreeMarker -KeyVaultName 'kv-test' -CredentialMode 'Secret-Free'
        $result | Should Be $true
    }

    It "returns true when the KV tag 'spaarke-secret-free-identity' is present" {
        Set-A38cAzShadow { $global:LASTEXITCODE = 0; return "true" }
        $result = Test-SpaarkeSecretFreeMarker -KeyVaultName 'kv-test'
        $result | Should Be $true
    }

    It "returns false when the KV tag is absent (empty tsv output) — backwards compat" {
        Set-A38cAzShadow { $global:LASTEXITCODE = 0; return "" }
        $result = Test-SpaarkeSecretFreeMarker -KeyVaultName 'kv-test'
        $result | Should Be $false
    }

    It "returns false (does not block) when az fails — vault not found / no access" {
        Set-A38cAzShadow { $global:LASTEXITCODE = 1; return $null }
        $result = Test-SpaarkeSecretFreeMarker -KeyVaultName 'kv-nonexistent'
        $result | Should Be $false
    }

    It "is idempotent — repeated calls against a marker-tagged vault return true every time" {
        Set-A38cAzShadow { $global:LASTEXITCODE = 0; return "true" }
        foreach ($i in 1..3) {
            (Test-SpaarkeSecretFreeMarker -KeyVaultName 'kv-test') | Should Be $true
        }
    }

    It "is idempotent — repeated calls against a pre-migration vault return false every time" {
        Set-A38cAzShadow { $global:LASTEXITCODE = 0; return "" }
        foreach ($i in 1..3) {
            (Test-SpaarkeSecretFreeMarker -KeyVaultName 'kv-test') | Should Be $false
        }
    }

    It "works identically across N distinct per-customer (Model 2) vault names" {
        Set-A38cAzShadow {
            # Simulate per-customer vaults: only 'kv-acme-2-kv' carries the marker.
            # $args[1] is the value passed to --name (args[0] is the literal 'show' subcommand
            # is not present here since Test-SpaarkeSecretFreeMarker calls `az keyvault show
            # --name $KeyVaultName --query ... -o tsv`).
            if ($args -contains 'kv-acme-2-kv') { $global:LASTEXITCODE = 0; return "true" }
            $global:LASTEXITCODE = 0
            return ""
        }
        (Test-SpaarkeSecretFreeMarker -KeyVaultName 'kv-acme-1-kv' -CustomerId 'acme-1') | Should Be $false
        (Test-SpaarkeSecretFreeMarker -KeyVaultName 'kv-acme-2-kv' -CustomerId 'acme-2') | Should Be $true
        (Test-SpaarkeSecretFreeMarker -KeyVaultName 'kv-acme-3-kv' -CustomerId 'acme-3') | Should Be $false
    }
}

Describe "Assert-SpaarkeSecretFreeGateNotTripped (A38c)" {

    Context "marker present (secret-free) — FAIL LOUD" {

        It "exits with code 7 (non-zero) and never proceeds past the gate" {
            $r = Invoke-A38cGateChildProcess `
                -AzMockBody "`$global:LASTEXITCODE = 0; return 'true'" `
                -AssertCall "Assert-SpaarkeSecretFreeGateNotTripped -SecretName 'ServiceBus-ConnectionString' -KeyVaultName 'kv-test'"

            $r.ExitCode | Should Be 7
            $r.Output   | Should Not Match '__A38C_TEST_REACHED_END__'
        }

        It "names the marker and links the A38a omit contract in the refusal message" {
            $r = Invoke-A38cGateChildProcess `
                -AzMockBody "`$global:LASTEXITCODE = 0; return 'true'" `
                -AssertCall "Assert-SpaarkeSecretFreeGateNotTripped -SecretName 'ServiceBus-ConnectionString' -KeyVaultName 'kv-test'"

            $r.Output | Should Match 'ServiceBus-ConnectionString'
            $r.Output | Should Match 'spaarke-secret-free-identity'
            $r.Output | Should Match 'A38a'
            $r.Output | Should Match 'auth\.md'
        }

        It "is idempotent — repeated invocations against a marker-tagged env refuse consistently (no partial writes)" {
            foreach ($i in 1..3) {
                $r = Invoke-A38cGateChildProcess `
                    -AzMockBody "`$global:LASTEXITCODE = 0; return 'true'" `
                    -AssertCall "Assert-SpaarkeSecretFreeGateNotTripped -SecretName 'ServiceBus-ConnectionString' -KeyVaultName 'kv-test'"
                $r.ExitCode | Should Be 7
            }
        }
    }

    Context "marker absent — backwards compatible fallthrough" {

        It "returns normally (no exit, no error) and lets the caller's write proceed unchanged" {
            $r = Invoke-A38cGateChildProcess `
                -AzMockBody "`$global:LASTEXITCODE = 0; return ''" `
                -AssertCall "Assert-SpaarkeSecretFreeGateNotTripped -SecretName 'ServiceBus-ConnectionString' -KeyVaultName 'kv-test'"

            $r.ExitCode | Should Be 0
            $r.Output   | Should Match '__A38C_TEST_REACHED_END__'
        }

        It "is idempotent — repeated invocations against a pre-migration env behave identically every time" {
            foreach ($i in 1..3) {
                $r = Invoke-A38cGateChildProcess `
                    -AzMockBody "`$global:LASTEXITCODE = 0; return ''" `
                    -AssertCall "Assert-SpaarkeSecretFreeGateNotTripped -SecretName 'AiSearch--AdminKey' -KeyVaultName 'kv-test'"
                $r.ExitCode | Should Be 0
                $r.Output   | Should Match '__A38C_TEST_REACHED_END__'
            }
        }
    }
}

Describe "Source-level regression guards (A38c no-regress obligations)" {

    $rotateSecretsSource   = Get-Content -Raw (Join-Path $repoRoot 'scripts/Rotate-Secrets.ps1')
    $seedKeyVaultSource    = Get-Content -Raw (Join-Path $repoRoot 'scripts/Seed-ProductionKeyVault.ps1')
    $provisionCustomerSrc  = Get-Content -Raw (Join-Path $repoRoot 'scripts/Provision-Customer.ps1')

    It "Rotate-Secrets.ps1: BFF-API-ClientSecret rotation remains a documented no-op (not reintroduced)" {
        $rotateSecretsSource | Should Match 'BFF-API-ClientSecret'
        $rotateSecretsSource | Should Match 'RETIRED 2026-08-24'
        # NEGATIVE: no actual credential-reset call for the BFF app registration
        $rotateSecretsSource | Should Not Match 'az ad app credential reset[^\n]*BFF'
    }

    It "Rotate-Secrets.ps1: Redis rotation branches are present and untouched (separate concern)" {
        $rotateSecretsSource | Should Match 'function Rotate-RedisKey'
        $rotateSecretsSource | Should Match 'az redis regenerate-keys'
    }

    It "Rotate-Secrets.ps1: both ServiceBus-ConnectionString gate sites are present (platform + per-customer)" {
        ([regex]::Matches($rotateSecretsSource, 'Assert-SpaarkeSecretFreeGateNotTripped -SecretName "ServiceBus-ConnectionString"')).Count | Should Be 2
    }

    It "Rotate-Secrets.ps1: gate comments document Model 1 vs Model 2 fleet consistency" {
        $rotateSecretsSource | Should Match 'Model 1'
        $rotateSecretsSource | Should Match 'Model 2'
        $rotateSecretsSource | Should Match 'per-customer'
    }

    It "Seed-ProductionKeyVault.ps1: BFF-API-ClientSecret is NOT seeded (verified removed per auth-v4 task 033)" {
        $seedKeyVaultSource | Should Not Match 'Set-VaultSecret -Name "BFF-API-ClientSecret"'
    }

    It "Seed-ProductionKeyVault.ps1: both ServiceBus-ConnectionString and AiSearch--AdminKey gates are present" {
        $seedKeyVaultSource | Should Match 'Assert-SpaarkeSecretFreeGateNotTripped -SecretName "ServiceBus-ConnectionString" -KeyVaultName \$VaultName'
        $seedKeyVaultSource | Should Match 'Assert-SpaarkeSecretFreeGateNotTripped -SecretName "AiSearch--AdminKey" -KeyVaultName \$VaultName'
    }

    It "Provision-Customer.ps1: ServiceBus-ConnectionString gate precedes the `$secrets hashtable write" {
        $gateIndex = $provisionCustomerSrc.IndexOf('Assert-SpaarkeSecretFreeGateNotTripped -SecretName "ServiceBus-ConnectionString"')
        $hashtableIndex = $provisionCustomerSrc.IndexOf('$secrets = [ordered]@{')
        $gateIndex | Should Not Be -1
        $hashtableIndex | Should Not Be -1
        ($gateIndex -lt $hashtableIndex) | Should Be $true
    }

    It "Provision-Customer.ps1 carries an optional deprecation banner naming L2 control-plane supersession" {
        $provisionCustomerSrc | Should Match 'legacy'
        $provisionCustomerSrc | Should Match 'provision-environment'
    }

    foreach ($fileEntry in @(
            @{ Name = 'Rotate-Secrets.ps1';          Source = $rotateSecretsSource },
            @{ Name = 'Seed-ProductionKeyVault.ps1'; Source = $seedKeyVaultSource },
            @{ Name = 'Provision-Customer.ps1';      Source = $provisionCustomerSrc }
        )) {
        It "$($fileEntry.Name): Dataverse-ClientSecret is never written (protected under §6.5 Path A through 2026-11-23)" {
            $fileEntry.Source | Should Not Match 'az keyvault secret set[^\n]*Dataverse-ClientSecret'
            $fileEntry.Source | Should Not Match 'Set-VaultSecret -Name "Dataverse-ClientSecret"'
            $fileEntry.Source | Should Not Match '"Dataverse-ClientSecret"\s*=\s*'
        }

        It "$($fileEntry.Name): dot-sources the shared A38c gate module" {
            $fileEntry.Source | Should Match "common/Assert-SpaarkeSecretFreeGate\.ps1"
        }
    }
}
