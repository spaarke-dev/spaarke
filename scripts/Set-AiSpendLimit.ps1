#Requires -Version 7.0
<#
.SYNOPSIS
    Add, change or remove the OPTIONAL monthly Azure OpenAI spend limit of a customer stamp
    (customer-provisioning-orchestration-r1 task 254, owner G37).

.DESCRIPTION
    No limit is the default. A provisioning run sets one only when the operator supplies intake
    `openAiMonthlyLimitUsd`; this script adds, changes or removes it afterwards.

    It writes the BFF app setting AiSpendLimit__MonthlyLimitUsd on the production slot AND the staging
    slot (both serve the same customer, and a swap must not drop the limit). Idempotent: it reads each
    slot first and writes only when the value differs. The BFF refuses model calls with HTTP 429 once
    its month-to-date estimate reaches the limit (Retry-After = next UTC month start).

    Changing an app setting restarts the App Service site (platform behaviour) — run it outside busy
    hours. Runs as the operator's own identity (az login); every az call carries --subscription
    (never `az account set`). -WhatIf shows the writes without making them.

.PARAMETER SubscriptionId
    The customer stamp's subscription (the customer's own subscription — ADR-027).

.PARAMETER ResourceGroupName
    The customer stamp's resource group (e.g. rg-spaarke-{customerId}-prod).

.PARAMETER AppServiceName
    The customer's BFF App Service (e.g. spaarke-bff-{customerId}-prod).

.PARAMETER MonthlyLimitUsd
    The limit in USD: a plain decimal greater than 0 and at most 1000000 (the rule POST /api/runs applies).

.PARAMETER Remove
    Remove the limit (back to the default: no limit).

.PARAMETER IncludeStagingSlot
    Also write the staging slot. Default $true; set $false only for a site without a staging slot.

.EXAMPLE
    ./scripts/Set-AiSpendLimit.ps1 -SubscriptionId <sub> -ResourceGroupName rg-spaarke-acme-prod -AppServiceName spaarke-bff-acme-prod -MonthlyLimitUsd 500

.EXAMPLE
    ./scripts/Set-AiSpendLimit.ps1 -SubscriptionId <sub> -ResourceGroupName rg-spaarke-acme-prod -AppServiceName spaarke-bff-acme-prod -Remove
#>
[CmdletBinding(SupportsShouldProcess = $true, DefaultParameterSetName = 'Set')]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$')]
    [string]$SubscriptionId,

    [Parameter(Mandatory = $true)]
    [string]$ResourceGroupName,

    [Parameter(Mandatory = $true)]
    [string]$AppServiceName,

    [Parameter(Mandatory = $true, ParameterSetName = 'Set')]
    [string]$MonthlyLimitUsd,

    [Parameter(Mandatory = $true, ParameterSetName = 'Remove')]
    [switch]$Remove,

    [bool]$IncludeStagingSlot = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$settingName = 'AiSpendLimit__MonthlyLimitUsd'
$maxLimitUsd = [decimal]1000000

if ($PSCmdlet.ParameterSetName -eq 'Set') {
    # Same rule as OpenAiMonthlyLimitRule (POST /api/runs): digits with an optional '.', > 0, <= 1,000,000.
    $parsed = [decimal]0
    $usable = $MonthlyLimitUsd -match '^[0-9]+(\.[0-9]+)?$' -and
        [decimal]::TryParse($MonthlyLimitUsd, [System.Globalization.NumberStyles]::AllowDecimalPoint,
            [System.Globalization.CultureInfo]::InvariantCulture, [ref]$parsed) -and
        $parsed -gt 0 -and $parsed -le $maxLimitUsd
    if (-not $usable) {
        throw "MonthlyLimitUsd '$MonthlyLimitUsd' is not a plain decimal greater than 0 and at most 1000000 " +
            "(e.g. '500'). Use -Remove for no limit."
    }
}

$slots = @('production')
if ($IncludeStagingSlot) { $slots += 'staging' }

function Get-SlotArguments([string]$Slot) {
    $common = @('--subscription', $SubscriptionId, '--resource-group', $ResourceGroupName, '--name', $AppServiceName)
    if ($Slot -ne 'production') { $common += @('--slot', $Slot) }
    return $common
}

$results = foreach ($slot in $slots) {
    $slotArgs = Get-SlotArguments $slot
    $current = az webapp config appsettings list @slotArgs `
        --query "[?name=='$settingName'].value | [0]" --output tsv
    if ($LASTEXITCODE -ne 0) {
        throw "Reading the app settings of '$AppServiceName' ($slot slot) failed (az exit $LASTEXITCODE)."
    }
    $current = ([string]$current).Trim()

    if ($Remove) {
        if ([string]::IsNullOrEmpty($current)) {
            [pscustomobject]@{ Slot = $slot; Before = '(none)'; After = '(none)'; Action = 'unchanged' }
            continue
        }
        if ($PSCmdlet.ShouldProcess("$AppServiceName ($slot slot)", "remove $settingName (was $current)")) {
            az webapp config appsettings delete @slotArgs --setting-names $settingName --output none
            if ($LASTEXITCODE -ne 0) { throw "Removing $settingName from '$AppServiceName' ($slot slot) failed (az exit $LASTEXITCODE)." }
            [pscustomobject]@{ Slot = $slot; Before = $current; After = '(none)'; Action = 'removed' }
        }
        continue
    }

    if ($current -ceq $MonthlyLimitUsd) {
        [pscustomobject]@{ Slot = $slot; Before = $current; After = $current; Action = 'unchanged' }
        continue
    }
    $before = if ([string]::IsNullOrEmpty($current)) { '(none)' } else { $current }
    if ($PSCmdlet.ShouldProcess("$AppServiceName ($slot slot)", "set $settingName=$MonthlyLimitUsd (was $before)")) {
        az webapp config appsettings set @slotArgs --settings "$settingName=$MonthlyLimitUsd" --output none
        if ($LASTEXITCODE -ne 0) { throw "Setting $settingName on '$AppServiceName' ($slot slot) failed (az exit $LASTEXITCODE)." }
        [pscustomobject]@{ Slot = $slot; Before = $before; After = $MonthlyLimitUsd; Action = 'set' }
    }
}

$results
