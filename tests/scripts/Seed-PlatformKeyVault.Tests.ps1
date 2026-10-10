# tests/scripts/Seed-PlatformKeyVault.Tests.ps1
# ---------------------------------------------------------------------------
# customer-provisioning-orchestration-r1 task 252 (owner-approved 2026-10-09): the platform control-plane vault
# seeder never creates BFF-API-ClientSecret or Dataverse-ClientSecret -- not as the old 'pending-oob-population'
# sentinel and not as a real value (.claude/constraints/provisioning.md "KV credential lifecycle" rule 1; nothing on a
# secret-free control plane references either). Proves: a -DryRun on an empty vault plans only Sidecar-Shared-Secret
# and writes nothing; a real run creates only Sidecar-Shared-Secret; no az call ever names either retired secret (not
# even an existence probe); an existing secret is skipped, never overwritten; the removed -BffApiClientSecret /
# -DataverseClientSecret parameters are gone.
#
# Technique (as Set-AiSpendLimit.Tests.ps1): Pester 3.4 cannot Mock an external application, so `az` is shadowed by a
# global function that records each call and serves an in-memory vault.
# Run: Import-Module Pester -RequiredVersion 3.4.0; Invoke-Pester tests/scripts/Seed-PlatformKeyVault.Tests.ps1
# ---------------------------------------------------------------------------

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$script:Seeder = Join-Path $repoRoot 'scripts/provisioning/Seed-PlatformKeyVault.ps1'
$script:Retired = 'BFF-API-ClientSecret|bff-api-client-secret|Dataverse-ClientSecret'

function Set-AzShadow {
    param([string[]]$Existing = @())
    $global:AzCalls = [System.Collections.Generic.List[string]]::new()
    $global:AzVault = @{}
    foreach ($name in $Existing) { $global:AzVault[$name] = 'existing' }
    Set-Item -Path function:global:az -Value {
        $line = $args -join ' '
        $global:AzCalls.Add($line)
        $global:LASTEXITCODE = 0
        if ($args[0] -eq '--version') { return 'azure-cli 2.77.0' }
        if ($args[0] -eq 'account') { return '{"name":"test-subscription","user":{"name":"tester@example.com"}}' }
        if ($args[0] -eq 'keyvault' -and $args[1] -eq 'show') { return 'sprk-controlplane-dev-kv' }
        if ($args[0] -eq 'keyvault' -and $args[1] -eq 'secret') {
            $name = $args[[array]::IndexOf($args, '--name') + 1]
            switch ($args[2]) {
                'show' {
                    if (-not $global:AzVault.ContainsKey($name)) { $global:LASTEXITCODE = 3; return }
                    if ($line -match '--output json') { return (@{ name = $name; enabled = $true } | ConvertTo-Json -Compress) }
                    return $name
                }
                'set' { $global:AzVault[$name] = 'set-by-seeder'; return }
            }
        }
        $global:LASTEXITCODE = 1
    }
}

function Remove-AzShadow { Remove-Item -Path function:global:az -ErrorAction SilentlyContinue }

Describe 'Seed-PlatformKeyVault.ps1 (T252)' {
    AfterEach { Remove-AzShadow }

    It '-DryRun on an empty vault plans only Sidecar-Shared-Secret and writes nothing' {
        Set-AzShadow

        $output = & $script:Seeder -Environment dev -DryRun 6>&1 | Out-String

        $LASTEXITCODE | Should Be 0
        @($global:AzCalls | Where-Object { $_ -match 'secret set' }).Count | Should Be 0
        $output | Should Match 'WHATIF:\s+Sidecar-Shared-Secret \(would be created'
        $output | Should Not Match $script:Retired
        @($global:AzCalls | Where-Object { $_ -match $script:Retired }).Count | Should Be 0
    }

    It 'a real run on an empty vault creates only Sidecar-Shared-Secret' {
        Set-AzShadow

        & $script:Seeder -Environment dev 6>&1 | Out-Null

        $LASTEXITCODE | Should Be 0
        @($global:AzCalls | Where-Object { $_ -match 'secret set' }).Count | Should Be 1
        @($global:AzCalls | Where-Object { $_ -match 'secret set' -and $_ -match '--name Sidecar-Shared-Secret' }).Count | Should Be 1
        @($global:AzVault.Keys) -join ',' | Should Be 'Sidecar-Shared-Secret'
        @($global:AzCalls | Where-Object { $_ -match $script:Retired }).Count | Should Be 0
    }

    It 'skips a secret that already exists and never overwrites it' {
        Set-AzShadow -Existing 'Sidecar-Shared-Secret', 'BFF-API-ClientSecret', 'Dataverse-ClientSecret'

        & $script:Seeder -Environment dev 6>&1 | Out-Null

        $LASTEXITCODE | Should Be 0
        @($global:AzCalls | Where-Object { $_ -match 'secret set' }).Count | Should Be 0
        $global:AzVault['Sidecar-Shared-Secret'] | Should Be 'existing'
        # Copies already in a vault are left exactly as they are -- never read, changed or deleted.
        $global:AzVault['BFF-API-ClientSecret'] | Should Be 'existing'
        $global:AzVault['Dataverse-ClientSecret'] | Should Be 'existing'
        @($global:AzCalls | Where-Object { $_ -match $script:Retired }).Count | Should Be 0
    }

    It 'no longer accepts -BffApiClientSecret' {
        Set-AzShadow

        { & $script:Seeder -Environment dev -DryRun -BffApiClientSecret 'x' } |
            Should Throw "A parameter cannot be found that matches parameter name 'BffApiClientSecret'."
    }

    It 'no longer accepts -DataverseClientSecret' {
        Set-AzShadow

        { & $script:Seeder -Environment dev -DryRun -DataverseClientSecret 'x' } |
            Should Throw "A parameter cannot be found that matches parameter name 'DataverseClientSecret'."
    }
}
