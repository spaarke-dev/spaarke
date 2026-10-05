<#
.SYNOPSIS
    THE parse of an environment's SPE admin Spaarke-operator marker declaration in config/environments.json
    (unified-access-control-r2 task 165, owner round 49 item 1; round 57 item 3).

.DESCRIPTION
    environments.<name>.speAdminPlatformOperatorEnvironment declares whether a BFF environment is Spaarke-operated; only
    then does scripts/Deploy-BffApi.ps1 set App Service SpeAdmin__PlatformOperatorEnvironment=true. Today only `dev`
    declares it (round 57 item 1).

    The value must be a JSON BOOLEAN. Anything else is refused, never coerced: PowerShell's [bool] cast makes EVERY
    non-empty string true, so a "false" written as a string would have opened the tenant-wide and type-wide SPE admin
    routes on the environment it named (round 57 item 3). Spaarke.ArchTests (SpeAdminOperatorEnvironmentMarkerGuardTests)
    runs this function against a string "false" and the other non-boolean shapes.
#>

function Get-SpeAdminOperatorMarkerDeclaration {
    <#
    .SYNOPSIS
        $true or $false for a declared JSON boolean; $false when the registry, the environment or the key is absent.
        THROWS for any other value (a string, a number, null, an object or an array).
    #>
    param(
        [Parameter(Mandatory)][string]$RegistryPath,
        [Parameter(Mandatory)][string]$Environment
    )

    if (-not (Test-Path -LiteralPath $RegistryPath)) { return $false }

    $entry = (Get-Content -LiteralPath $RegistryPath -Raw | ConvertFrom-Json).environments.$Environment
    if ($null -eq $entry) { return $false }

    $declaration = $entry.PSObject.Properties['speAdminPlatformOperatorEnvironment']
    if ($null -eq $declaration) { return $false }

    if ($declaration.Value -is [bool]) { return $declaration.Value }

    $found = if ($null -eq $declaration.Value) { 'null' } else { "$($declaration.Value.GetType().Name) '$($declaration.Value)'" }
    throw ("config/environments.json: environments.$Environment.speAdminPlatformOperatorEnvironment must be a JSON boolean " +
        "(true or false, unquoted), not $found.")
}
