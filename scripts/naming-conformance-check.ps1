#!/usr/bin/env pwsh
<#
.SYNOPSIS
    KV-secret / Azure-resource naming-conformance gate (r3 task 063).

.DESCRIPTION
    Enforces the env-agnostic naming standard in
    docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md § "KV-Secret & Resource
    Naming Standard (Conformance-Gated)". The load-bearing rule: a name that is
    REPLICATED across environments MUST be env-agnostic — the environment lives in the
    VALUE (and in the vault name), never baked into the secret/resource NAME.

    Three rules, highest-signal first (low false-positive per root CLAUDE.md §16):

      R1  env-token-in-name    A KV-secret name contains an environment token
                               (DEV/DEMO/PROD/UAT/TEST/STAGING/SANDBOX, incl. an
                               SPRK-DEV-* style prefix). e.g. `SPRK-DEV-DATAVERSE-URL`
                               is a violation; `Dataverse-ServiceUrl` is conformant.
      R2  casing-drift         The SAME logical secret appears under >1 casing across
                               the scanned files (e.g. `BFF-API-ClientSecret` vs
                               `bff-api-client-secret`) — a rotation hazard.
      R3  vault-name-drift     A vault name other than the canonical `sprk-{env}-kv`
                               form appears, EXCEPT the codified legacy dev exception
                               `spaarke-spekvcert` (DO-NOT-RENAME).

    The gate is READ-ONLY. It renames nothing (r1 / customer-provisioning-orchestration-r1
    owns applying canonical names + live-env remediation). Exit 0 = conformant, 1 = violations.

.PARAMETER Path
    Files to scan. Defaults to the canonical secret-name sources.

.PARAMETER SelfTest
    Run built-in fixtures (a seeded violation MUST fail; conformant names MUST pass) and exit.

.EXAMPLE
    pwsh scripts/naming-conformance-check.ps1
    pwsh scripts/naming-conformance-check.ps1 -SelfTest
#>
[CmdletBinding()]
param(
    [string[]] $Path,
    [switch]   $SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Environment tokens that MUST NOT appear inside a replicated name.
$EnvTokens = @('DEV', 'DEMO', 'PROD', 'PRODUCTION', 'UAT', 'TEST', 'STAGING', 'STAGE', 'SANDBOX', 'QA')
# Codified legacy exception (naming doc DO-NOT-RENAME): the only live dev vault.
$VaultLegacyException = 'spaarke-spekvcert'
# Canonical vault form: sprk-{env}-kv
$CanonicalVaultRegex = '^sprk-[a-z0-9]+-kv$'

function Get-SecretNameTokens {
    param([string] $Text)
    # KV-reference SecretName=..., and quoted secret names in seeder/template/tokens.
    $names = New-Object System.Collections.Generic.HashSet[string]
    foreach ($m in [regex]::Matches($Text, 'SecretName=([A-Za-z0-9._-]+)')) {
        [void]$names.Add($m.Groups[1].Value)
    }
    foreach ($m in [regex]::Matches($Text, '(?i)(?:SecretName|-Name)\s*[:=]\s*["'']([A-Za-z0-9._-]+)["'']')) {
        [void]$names.Add($m.Groups[1].Value)
    }
    return $names
}

function Test-NamingConformance {
    param([hashtable] $FileToText)

    $violations = New-Object System.Collections.Generic.List[object]
    $seenByCanonical = @{}   # lowercased-name -> list of (actual, file)
    $vaultNames = New-Object System.Collections.Generic.HashSet[string]

    foreach ($file in $FileToText.Keys) {
        $text = $FileToText[$file]

        # R1 + collect for R2
        foreach ($name in (Get-SecretNameTokens -Text $text)) {
            # Split into words by BOTH delimiters (-_.) AND camelCase boundaries, then match a WHOLE
            # word against an env token. Catches kebab/snake (SPRK-DEV-URL) AND camelCase (DevDataverseUrl)
            # while NOT false-flagging substrings ("Development" -> word "Development" != "DEV").
            $words = [regex]::Matches($name, '[A-Z]+(?![a-z])|[A-Z][a-z]+|[a-z]+|[0-9]+') |
                     ForEach-Object { $_.Value.ToUpperInvariant() }
            $hit = $null
            foreach ($tok in $EnvTokens) {
                if ($words -contains $tok) { $hit = $tok; break }
            }
            if ($hit) {
                $violations.Add([pscustomobject]@{ Rule='R1 env-token-in-name'; Name=$name; File=$file; Detail="contains env token '$hit'" })
            }
            $key = $name.ToLowerInvariant()
            if (-not $seenByCanonical.ContainsKey($key)) { $seenByCanonical[$key] = New-Object System.Collections.Generic.List[object] }
            $seenByCanonical[$key].Add([pscustomobject]@{ Actual=$name; File=$file })
        }

        # R3 vault names
        foreach ($m in [regex]::Matches($text, 'VaultName=([A-Za-z0-9-]+)')) { [void]$vaultNames.Add($m.Groups[1].Value) }
        foreach ($m in [regex]::Matches($text, '(?i)vault[Nn]ame\s*[:=]\s*["'']([A-Za-z0-9-]+)["'']')) { [void]$vaultNames.Add($m.Groups[1].Value) }
    }

    # R2 casing-drift: same lowercased name under >1 distinct actual casing
    foreach ($key in $seenByCanonical.Keys) {
        $distinct = @($seenByCanonical[$key] | Select-Object -ExpandProperty Actual -Unique)
        if ($distinct.Count -gt 1) {
            $violations.Add([pscustomobject]@{ Rule='R2 casing-drift'; Name=($distinct -join ' | '); File='(multiple)'; Detail="one logical secret under $($distinct.Count) casings" })
        }
    }

    # R3 vault-name-drift
    foreach ($v in $vaultNames) {
        if ($v -eq $VaultLegacyException) { continue }
        if ($v -notmatch $CanonicalVaultRegex) {
            $violations.Add([pscustomobject]@{ Rule='R3 vault-name-drift'; Name=$v; File='(vault ref)'; Detail="not canonical 'sprk-{env}-kv' (and not the codified '$VaultLegacyException' dev exception)" })
        }
    }

    return $violations
}

if ($SelfTest) {
    $bad = @{
        'fixture-bad' = @'
Dataverse__Url = @Microsoft.KeyVault(VaultName=sprk-dev-kv;SecretName=SPRK-DEV-DATAVERSE-URL)
Bff__Secret    = @Microsoft.KeyVault(VaultName=kv-sdap-dev;SecretName=BFF-API-ClientSecret)
Bff__Secret2   = @Microsoft.KeyVault(VaultName=sprk-demo-kv;SecretName=bff-api-clientsecret)
Bff__Secret3   = @Microsoft.KeyVault(VaultName=sprk-prod-kv;SecretName=ProdBffSecret)
'@
    }
    $good = @{
        'fixture-good' = @'
Dataverse__Url = @Microsoft.KeyVault(VaultName=sprk-demo-kv;SecretName=Dataverse-ServiceUrl)
Bff__Secret    = @Microsoft.KeyVault(VaultName=spaarke-spekvcert;SecretName=BFF-API-ClientSecret)
'@
    }
    $badV  = @(Test-NamingConformance -FileToText $bad)
    $goodV = @(Test-NamingConformance -FileToText $good)
    $badHasR1    = @($badV | Where-Object Rule -like 'R1*').Count -ge 1  # SPRK-DEV-* delimited
    $badHasR1cc  = @($badV | Where-Object { $_.Rule -like 'R1*' -and $_.Name -eq 'ProdBffSecret' }).Count -ge 1  # camelCase
    $badHasR2    = @($badV | Where-Object Rule -like 'R2*').Count -ge 1  # BFF-API-ClientSecret vs bff-api-clientsecret
    $badHasR3    = @($badV | Where-Object Rule -like 'R3*').Count -ge 1  # kv-sdap-dev is non-canonical
    $goodClean   = $goodV.Count -eq 0
    Write-Host "SelfTest: bad->R1=$badHasR1 R1camelCase=$badHasR1cc R2=$badHasR2 R3=$badHasR3 (total $($badV.Count)); good->clean=$goodClean"
    if ($badHasR1 -and $badHasR1cc -and $badHasR2 -and $badHasR3 -and $goodClean) { Write-Host 'SelfTest PASSED' -ForegroundColor Green; exit 0 }
    Write-Host 'SelfTest FAILED' -ForegroundColor Red
    $badV | Format-Table -AutoSize | Out-String | Write-Host
    exit 2
}

if (-not $Path -or $Path.Count -eq 0) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $Path = @(
        (Join-Path $repoRoot 'src/server/api/Sprk.Bff.Api/appsettings.template.json'),
        (Join-Path $repoRoot 'src/server/api/Sprk.Bff.Api/appsettings.tokens.md'),
        (Join-Path $repoRoot 'scripts/Seed-ProductionKeyVault.ps1')
    ) | Where-Object { Test-Path $_ }
}

$fileToText = @{}
foreach ($p in $Path) {
    if (Test-Path $p) {
        $raw = Get-Content -Raw -Path $p
        # Get-Content -Raw on an empty file returns $null; coerce to '' so the regex engine
        # doesn't throw under StrictMode + ErrorActionPreference=Stop.
        $fileToText[$p] = if ($null -eq $raw) { '' } else { $raw }
    }
}

if ($fileToText.Count -eq 0) {
    # FAIL-CLOSED: a naming gate that finds none of its expected inputs must NOT silently greenlight —
    # a renamed/moved canonical file would otherwise make the gate pass forever without scanning anything.
    Write-Host 'naming-conformance-check: ERROR — no scannable files found. The canonical secret-name' -ForegroundColor Red
    Write-Host 'sources are missing or were renamed; the gate cannot verify conformance. Pass -Path explicitly' -ForegroundColor Red
    Write-Host 'or restore the default sources (appsettings.template.json / appsettings.tokens.md / Seed-ProductionKeyVault.ps1).' -ForegroundColor Red
    exit 1
}

$violations = @(Test-NamingConformance -FileToText $fileToText)

if ($violations.Count -eq 0) {
    Write-Host "naming-conformance-check: PASS — $($fileToText.Count) file(s) scanned, 0 violations." -ForegroundColor Green
    exit 0
}

Write-Host "naming-conformance-check: FAIL — $($violations.Count) violation(s):" -ForegroundColor Red
$violations | Format-Table -AutoSize | Out-String | Write-Host
Write-Host 'See docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md § "KV-Secret & Resource Naming Standard (Conformance-Gated)".'
Write-Host 'r3 owns the standard + this gate; customer-provisioning-orchestration-r1 owns applying canonical names + live-env remediation.'
exit 1
