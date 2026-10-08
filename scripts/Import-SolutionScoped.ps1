#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Import a Dataverse solution ZIP and publish ONLY its components (task 130, owner decision D-83).
    The command every skill and guide points to instead of a tenant-wide publish.

.DESCRIPTION
    1. pac solution import --environment <url> --path <zip> --force-overwrite   (no publish)
    2. Read the solution's components, build an exact PublishXml ParameterXml, POST it.
    3. Read back web resources and app modules and fail if any is still unpublished.

    -PlanOnly reads the solution already installed in the environment and prints the ParameterXml that would be
    published; it imports and publishes nothing.

    Needs pac CLI and the operator's az login. dev environments only unless the owner approves otherwise.

.EXAMPLE
    pwsh scripts/Import-SolutionScoped.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -ZipPath .\VisualHostSolution_v1.4.39.zip -SolutionUniqueName VisualHostSolution
    pwsh scripts/Import-SolutionScoped.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -SolutionUniqueName VisualHostSolution -PlanOnly
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EnvironmentUrl,
    [string]$ZipPath,
    [Parameter(Mandatory)][string]$SolutionUniqueName,
    [string[]]$ImportArgs = @('--force-overwrite'),
    [switch]$PlanOnly,
    # Also publish the entities of forms that host an imported PCF control. Off by default (see the module note).
    [switch]$IncludeControlHostEntities
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib' 'Publish-SolutionComponents.ps1')

$ctx = Get-DataverseApiContext -EnvironmentUrl $EnvironmentUrl
if ($PlanOnly) {
    $plan = Get-SolutionPublishPlan -Context $ctx -SolutionUniqueName $SolutionUniqueName -IncludeControlHostEntities:$IncludeControlHostEntities
    if (@($plan.Unmapped).Count -gt 0) { Write-Warning "Unmapped component(s): $(@($plan.Unmapped) -join ', ')" }
    New-PublishParameterXml -Entities $plan.Entities -WebResources $plan.WebResources -OptionSets $plan.OptionSets `
        -SiteMaps $plan.SiteMaps -Dashboards $plan.Dashboards -AppModules $plan.AppModules
    return
}
if (-not $ZipPath -or -not (Test-Path -LiteralPath $ZipPath)) { throw "-ZipPath is required and must exist (got '$ZipPath')." }
Invoke-ScopedSolutionImport -EnvironmentUrl $EnvironmentUrl -ZipPath $ZipPath -SolutionUniqueName $SolutionUniqueName `
    -ImportArgs $ImportArgs -Context $ctx -IncludeControlHostEntities:$IncludeControlHostEntities | Out-Null
Write-Host "Imported and scoped-published $SolutionUniqueName. No tenant-wide publish was run."
