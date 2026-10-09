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
    [switch]$IncludeControlHostEntities,
    # Resume after a failed publish request: publish the installed solution's components, no import.
    [switch]$PublishOnly,
    # The solution contains workflows (type 29): import with --activate-plugins and fail unless every workflow reads statecode 1.
    [switch]$AllowWorkflows,
    # With -PublishOnly: components published outside the solution in the failed run (the resume message prints them).
    [string[]]$ExtraWebResources = @(),
    [string[]]$ExtraEntities = @(),
    # Validate and print the normalized arguments, then stop (no token, no Dataverse call). Used by the end-to-end argument test.
    [switch]$CheckArguments
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib' 'Publish-SolutionComponents.ps1')

# Extras arrive as 'g1,g2' (the form the resume command prints), as 'g1','g2', or one per entry: split, trim, drop empties, VALIDATE.
# A bad value is rejected here, before any import or publish.
$ExtraWebResources = @(ConvertTo-ExtraList -Values $ExtraWebResources -Kind WebResource)
$ExtraEntities = @(ConvertTo-ExtraList -Values $ExtraEntities -Kind Entity)
if ($CheckArguments) {
    [pscustomobject]@{ ExtraWebResources = $ExtraWebResources; ExtraEntities = $ExtraEntities } | ConvertTo-Json -Compress
    return
}

$ctx = Get-DataverseApiContext -EnvironmentUrl $EnvironmentUrl
if ($PlanOnly) {
    $plan = Get-SolutionPublishPlan -Context $ctx -SolutionUniqueName $SolutionUniqueName -IncludeControlHostEntities:$IncludeControlHostEntities -AllowWorkflows:$AllowWorkflows
    if (@($plan.Unmapped).Count -gt 0) { Write-Warning "Unmapped component(s): $(@($plan.Unmapped) -join ', ')" }
    New-PublishParameterXmlFromPlan -Plan $plan
    return
}
if ($PublishOnly) {
    Publish-SolutionComponents -Context $ctx -SolutionUniqueName $SolutionUniqueName -IncludeControlHostEntities:$IncludeControlHostEntities -AllowWorkflows:$AllowWorkflows -ExtraWebResources $ExtraWebResources -ExtraEntities $ExtraEntities | Out-Null
    Write-Host "Scoped-published $SolutionUniqueName (no import). No tenant-wide publish was run."
    return
}
if (-not $ZipPath -or -not (Test-Path -LiteralPath $ZipPath)) { throw "-ZipPath is required and must exist (got '$ZipPath')." }
Invoke-ScopedSolutionImport -EnvironmentUrl $EnvironmentUrl -ZipPath $ZipPath -SolutionUniqueName $SolutionUniqueName `
    -ImportArgs $ImportArgs -Context $ctx -IncludeControlHostEntities:$IncludeControlHostEntities -AllowWorkflows:$AllowWorkflows `
    -ExtraWebResources $ExtraWebResources -ExtraEntities $ExtraEntities | Out-Null
Write-Host "Imported and scoped-published $SolutionUniqueName. No tenant-wide publish was run."
