# tests/scripts/Set-AiSpendLimit.Tests.ps1
# ---------------------------------------------------------------------------
# customer-provisioning-orchestration-r1 task 254 (owner G37): the documented procedure that adds, changes or
# removes a customer stamp's optional monthly OpenAI spend limit after provisioning. Proves: both slots are
# written; a second run changes nothing; -Remove deletes from both slots; a value POST /api/runs would refuse
# is refused before any az call; every az call carries --subscription.
#
# Technique (as Deploy-AllIndexes.Tests.ps1 / Auth-V4-Operator-Script-Gates.Tests.ps1): Pester 3.4 cannot Mock an
# external application, so `az` is shadowed by a global function that records each call and serves the settings
# from an in-memory table per slot.
# ---------------------------------------------------------------------------

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
$script:SetLimit = Join-Path $repoRoot 'scripts/Set-AiSpendLimit.ps1'
$script:Sub = '11111111-2222-3333-4444-555555555555'

function Set-AzShadow {
    param([hashtable]$Initial)
    $global:AzCalls = [System.Collections.Generic.List[string]]::new()
    $global:AzSettings = @{ production = $Initial.production; staging = $Initial.staging }
    Set-Item -Path function:global:az -Value {
        $global:AzCalls.Add(($args -join ' '))
        $slot = 'production'
        $i = [array]::IndexOf($args, '--slot'); if ($i -ge 0) { $slot = $args[$i + 1] }
        $global:LASTEXITCODE = 0
        switch ($args[3]) {
            'list'   { return $global:AzSettings[$slot] }
            'set'    { $global:AzSettings[$slot] = ($args[[array]::IndexOf($args, '--settings') + 1] -split '=', 2)[1] }
            'delete' { $global:AzSettings[$slot] = $null }
        }
    }
}

function Remove-AzShadow { Remove-Item -Path function:global:az -ErrorAction SilentlyContinue }

Describe 'Set-AiSpendLimit.ps1 (T254)' {
    AfterEach { Remove-AzShadow }

    It 'sets the limit on the production AND staging slots, passing --subscription on every call' {
        Set-AzShadow @{ production = $null; staging = $null }

        $result = & $script:SetLimit -SubscriptionId $script:Sub -ResourceGroupName rg -AppServiceName bff -MonthlyLimitUsd '500'

        $global:AzSettings.production | Should Be '500'
        $global:AzSettings.staging | Should Be '500'
        @($result | Where-Object { $_.Action -eq 'set' }).Count | Should Be 2
        @($global:AzCalls | Where-Object { $_ -notmatch "--subscription $($script:Sub)" }).Count | Should Be 0
    }

    It 'is idempotent: a second run with the same value writes nothing' {
        Set-AzShadow @{ production = '500'; staging = '500' }

        $result = & $script:SetLimit -SubscriptionId $script:Sub -ResourceGroupName rg -AppServiceName bff -MonthlyLimitUsd '500'

        @($global:AzCalls | Where-Object { $_ -match 'appsettings set' }).Count | Should Be 0
        @($result | Where-Object { $_.Action -eq 'unchanged' }).Count | Should Be 2
    }

    It 'changes an existing limit' {
        Set-AzShadow @{ production = '500'; staging = '500' }

        & $script:SetLimit -SubscriptionId $script:Sub -ResourceGroupName rg -AppServiceName bff -MonthlyLimitUsd '750.50' | Out-Null

        $global:AzSettings.production | Should Be '750.50'
        $global:AzSettings.staging | Should Be '750.50'
    }

    It '-Remove deletes the setting from both slots, and a second -Remove writes nothing' {
        Set-AzShadow @{ production = '500'; staging = '500' }

        & $script:SetLimit -SubscriptionId $script:Sub -ResourceGroupName rg -AppServiceName bff -Remove | Out-Null
        $global:AzSettings.production | Should BeNullOrEmpty
        $global:AzSettings.staging | Should BeNullOrEmpty

        $global:AzCalls.Clear()
        & $script:SetLimit -SubscriptionId $script:Sub -ResourceGroupName rg -AppServiceName bff -Remove | Out-Null
        @($global:AzCalls | Where-Object { $_ -match 'appsettings delete' }).Count | Should Be 0
    }

    foreach ($bad in @('0', '-5', 'lots', '5.', '1,000', '1000000.01')) {
        It "refuses '$bad' before any az call" {
            Set-AzShadow @{ production = $null; staging = $null }

            # try/catch in the It's own scope: Pester 3.4 runs a `Should Throw` scriptblock outside the foreach's scope.
            $threw = $false
            try { & $script:SetLimit -SubscriptionId $script:Sub -ResourceGroupName rg -AppServiceName bff -MonthlyLimitUsd $bad }
            catch { $threw = $_.Exception.Message -match 'not a plain decimal' }
            $threw | Should Be $true
            $global:AzCalls.Count | Should Be 0
        }
    }

    It '-WhatIf reads but writes nothing' {
        Set-AzShadow @{ production = $null; staging = $null }

        & $script:SetLimit -SubscriptionId $script:Sub -ResourceGroupName rg -AppServiceName bff -MonthlyLimitUsd '500' -WhatIf | Out-Null

        @($global:AzCalls | Where-Object { $_ -match 'appsettings (set|delete)' }).Count | Should Be 0
    }
}
