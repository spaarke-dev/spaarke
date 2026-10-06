<#
.SYNOPSIS
    THE PowerShell copy of the BFF's Key Vault secret-name allow-list for SPE container-type configs
    (unified-access-control-r2 task 165, owner round 35 item 3; round 41 items 4 and 5).

.DESCRIPTION
    sprk_specontainertypeconfig.sprk_keyvaultsecretname names the Key Vault secret the BFF reads, app-only, to act as a
    config's owning app. The BFF resolves ONLY names under ONE pinned prefix
    (Sprk.Bff.Api.Services.SpeAdmin.SpeConfigSecretNamePolicy). Every script that judges or writes such a name
    dot-sources THIS file, so the rule is spelled once on the PowerShell side; Spaarke.ArchTests
    (SpeAdminContainerBindingGuardTests) pins the prefix equal to the C# constant and the expression's end anchor.

    The expression ends at \z — the END of the string — never at $, which in .NET (and so in PowerShell's -match and
    [regex]) also matches just before a trailing newline: with $, 'spe-owning-app-secret' followed by a newline
    conformed (owner round 41 item 5).
#>

# ── THE pinned prefix. MUST equal SpeConfigSecretNamePolicy.RequiredPrefix (SpeAdminContainerBindingGuardTests). ─────
$SpeConfigSecretNamePrefix = 'spe-owning-app-'

# Key Vault's own name rule after the prefix (letters, digits, hyphens; 127 characters in all), case-insensitive — the
# same expression the BFF builds, ending at \z.
$SpeConfigSecretNamePattern = '^' + [regex]::Escape($SpeConfigSecretNamePrefix) + '[A-Za-z0-9-]{1,' + (127 - $SpeConfigSecretNamePrefix.Length) + '}\z'

function Test-SpeConfigSecretNameAllowed {
    <# True when the BFF would resolve this secret name. Blank, untrimmed and foreign names are refused. #>
    param([AllowNull()][AllowEmptyString()][string]$Name)
    if ([string]::IsNullOrEmpty($Name)) { return $false }
    return [regex]::IsMatch($Name, $SpeConfigSecretNamePattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
}
