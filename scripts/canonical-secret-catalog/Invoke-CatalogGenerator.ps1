<#
.SYNOPSIS
    Deterministic generator for the canonical KV secret-catalog manifest.
    Fans out ONE manifest.yaml to FOUR output artifacts.

.DESCRIPTION
    Reads scripts/canonical-secret-catalog/manifest.yaml (the single source of
    truth for Spaarke KV secrets per spec.md FR-36 + §7.9) and emits four
    generated artifacts into scripts/canonical-secret-catalog/generated/:

        1. Seed-CustomerKeyVault.generated.ps1
             — seeder that upserts every canonical secret to a target vault
        2. Configure-AppServiceSettings.generated.ps1
             — App Service app-settings script wiring Key Vault references
        3. appsettings.tokens.generated.md
             — operator-facing token / secret reference documentation
        4. kv-secrets.generated.bicep
             — Bicep module emitting Microsoft.KeyVault/vaults/secrets slots

    The generator is DETERMINISTIC: two invocations against the same manifest
    produce BYTE-IDENTICAL outputs (secrets sorted alphabetically by
    canonical-name, timestamps excluded from body, LF-normalized line endings,
    trailing newline present).

    Modes:
        (default)      Regenerate + overwrite generated/ files.
        -DryRun        Parse manifest + report planned outputs; write nothing.
        -Verify        Regenerate in memory + compare vs on-disk generated/;
                       exit 0 if byte-identical, non-zero (with unified diff)
                       if drift detected. Suitable for CI drift check.

    BINDING GUARD (spec.md MUST rules + r3 handoff §4a):
        The generator refuses to emit outputs when either
        `Dataverse-ClientSecret` or `BFF-API-ClientSecret` is missing from
        manifest.secrets OR carries never_delete=false. Fails loudly with a
        clear operator diagnostic. This mirrors the H4 handler's runtime
        BINDING pre-check — the failure mode must be visible at manifest-edit
        time, not at deployment time.

    ALIAS AWARENESS:
        Aliases listed on a canonical entry are drift spellings tracked for
        the alias-collapse workflow (task 085). They are enumerated in the
        generated tokens.md doc under an "Aliases / drift spellings" section
        but are NOT written as separate KV secrets by the seeder / Bicep
        modules — that would perpetuate the drift. Alias collapse (create
        canonical, migrate consumers, delete alias) is out-of-scope for the
        generator; it is a manual pre-checked operation.

.PARAMETER Manifest
    Path to manifest.yaml. Defaults to sibling manifest.yaml in this script's
    directory.

.PARAMETER OutputDir
    Path to the generated/ output directory. Defaults to sibling generated/
    in this script's directory.

.PARAMETER DryRun
    Parse manifest + report planned outputs; write nothing.

.PARAMETER Verify
    Regenerate in memory + compare vs on-disk generated/ files. Exit 0 if
    byte-identical; exit 2 (with per-file diff summary) if drift detected;
    exit 1 on validation / manifest-read failure. Suitable for CI.

.EXAMPLE
    pwsh scripts/canonical-secret-catalog/Invoke-CatalogGenerator.ps1
    # Regenerate all four artifacts into generated/.

.EXAMPLE
    pwsh scripts/canonical-secret-catalog/Invoke-CatalogGenerator.ps1 -DryRun
    # Print planned outputs; write nothing.

.EXAMPLE
    pwsh scripts/canonical-secret-catalog/Invoke-CatalogGenerator.ps1 -Verify
    # CI drift check: exit non-zero if generated/ has drifted from manifest.

.NOTES
    Authority:
        - spec.md FR-36 (Phase H canonical secret-catalog manifest)
        - spec.md §7.9 R1-R4 (canonical KV naming)
        - docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md
        - projects/customer-provisioning-orchestration-r1/notes/naming-exception-registry.md
        - projects/code-quality-and-assurance-r3/notes/task-063-naming-standard-r1-handoff.md

    Requires: PowerShell 7+ (pwsh), powershell-yaml (0.4.12+) — same as the
    existing seed-data / naming-conformance ecosystem.
#>

#Requires -Version 7.0

# PSScriptAnalyzer suppressions — intentional design choices, documented:
#
#   PSAvoidUsingWriteHost: Operator-facing CLI diagnostics use colored console
#     output (Yellow for status, Red for failure) — Write-Host is the correct
#     tool here; Write-Output would pollute the return-value pipeline.
#
#   PSUseShouldProcessForStateChangingFunctions: The New-*Artifact functions
#     are PURE — they return strings, they do NOT mutate anything. The
#     `New-` verb is idiomatic PowerShell for factory functions; this
#     analyzer rule is a false positive here. Only Write-Artifacts actually
#     writes to disk, and its caller path is gated by -DryRun / -Verify.
#
#   PSUseSingularNouns: These functions produce/process multiple secrets,
#     multiple artifacts. Plural is semantically correct.
#
#   PSUseBOMForUnicodeEncodedFile: Determinism contract requires UTF-8
#     without BOM (matches git default + cross-platform tooling).
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingWriteHost', '', Justification = 'Operator-facing CLI diagnostics use colored console output — Write-Host is intentional.')]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '', Justification = 'New-*Artifact functions are pure string factories; only Write-Artifacts touches disk (gated by -DryRun/-Verify at the caller).')]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseSingularNouns', '', Justification = 'Functions produce/process multiple secrets and multiple artifacts — plural is semantically correct.')]
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseBOMForUnicodeEncodedFile', '', Justification = 'Determinism contract requires UTF-8 without BOM (cross-platform + git default).')]
[CmdletBinding()]
param(
    [string]$Manifest,
    [string]$OutputDir,
    [switch]$DryRun,
    [switch]$Verify
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# Constants — do not vary between runs (determinism contract)
# ---------------------------------------------------------------------------

$script:GeneratorVersion    = '1.0.0'
$script:GeneratorHeaderMark = '# --- GENERATED FILE — DO NOT EDIT BY HAND ---'
$script:NewLine             = "`n"
$script:BindingNeverDelete  = @('Dataverse-ClientSecret', 'BFF-API-ClientSecret')
$script:RequiredSecretFields = @(
    'canonical_name', 'category', 'purpose', 'consumers', 'rotation_cadence',
    'never_delete', 'exception_note', 'aliases', 'value_source', 'app_settings', 'tags'
)
$script:AllowedValueSources = @('from-existing-kv', 'from-bicep-output', 'from-run-parameter', 'from-shared-service', 'generated')

# Task 201 — per_env_settings schema (H4b BulkAppSettings handler).
# Optional top-level list; when present, each entry MUST carry these fields.
# `literal_value` is conditionally required only when `per_env_source == literal`.
$script:RequiredPerEnvSettingFields = @('key', 'per_env_source', 'iOptionsModule', 'required')
# Regex mirrors design.md schema: `literal` OR `from-{handler}-{output|parameter}:{key}`.
# Handler segment: lowercase-alphanumeric (H0.5 handler-id "h0.5" allowed by
# escaping the dot). Key segment: identifier-shaped (letters/digits/underscore).
$script:PerEnvSourcePattern = '^(literal|from-[a-z0-9\.]+-(output|parameter):[a-zA-Z_][a-zA-Z0-9_]*)$'

# ---------------------------------------------------------------------------
# Path defaults
# ---------------------------------------------------------------------------

if (-not $Manifest) {
    $Manifest = Join-Path -Path $PSScriptRoot -ChildPath 'manifest.yaml'
}
if (-not $OutputDir) {
    $OutputDir = Join-Path -Path $PSScriptRoot -ChildPath 'generated'
}

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

function Write-Info {
    param([string]$Message)
    Write-Host $Message
}

function Write-Diag {
    param([string]$Message)
    Write-Host $Message -ForegroundColor Yellow
}

function Write-Fail {
    param([string]$Message)
    Write-Host $Message -ForegroundColor Red
}

function Import-YamlModule {
    # Same import strategy as scripts/seed-data/Invoke-SeedManifest.ps1.
    if (-not (Get-Module -ListAvailable -Name powershell-yaml)) {
        throw @"
powershell-yaml module is not installed.

Install it (one-time, current user scope):
    Install-Module -Name powershell-yaml -Scope CurrentUser

Then re-run: $($MyInvocation.MyCommand.Name)
"@
    }
    Import-Module powershell-yaml -ErrorAction Stop
}

function Read-Manifest {
    param([string]$Path)

    if (-not (Test-Path -Path $Path)) {
        throw "Manifest not found: $Path"
    }

    $raw = Get-Content -Path $Path -Raw -Encoding utf8
    try {
        $parsed = ConvertFrom-Yaml -Yaml $raw
    }
    catch {
        throw "Failed to parse manifest.yaml: $($_.Exception.Message)"
    }

    if ($null -eq $parsed) {
        throw "Manifest is empty or unreadable: $Path"
    }
    return $parsed
}

function Test-ManifestShape {
    param([hashtable]$Data)

    $topRequired = @('schema_version', 'vault_name_pattern', 'kv_reference_syntax', 'secrets')
    foreach ($field in $topRequired) {
        if (-not $Data.ContainsKey($field)) {
            throw "Manifest is missing required top-level field: $field"
        }
    }

    if ($Data.schema_version -ne 1) {
        throw "Unsupported manifest schema_version: $($Data.schema_version). This generator supports schema_version=1."
    }

    if (-not ($Data.secrets -is [System.Collections.IList]) -or $Data.secrets.Count -eq 0) {
        throw "Manifest field 'secrets' must be a non-empty list."
    }

    $seenNames = @{}
    foreach ($secret in $Data.secrets) {
        if (-not ($secret -is [hashtable])) {
            throw "Every 'secrets' entry must be a mapping. Found: $($secret.GetType().FullName)."
        }
        foreach ($field in $script:RequiredSecretFields) {
            if (-not $secret.ContainsKey($field)) {
                $name = if ($secret.ContainsKey('canonical_name')) { $secret.canonical_name } else { '<unnamed>' }
                throw "Secret '$name' is missing required field: $field"
            }
        }

        $canon = [string]$secret.canonical_name
        if ([string]::IsNullOrWhiteSpace($canon)) {
            throw "A 'secrets' entry has an empty canonical_name."
        }
        if ($seenNames.ContainsKey($canon)) {
            throw "Duplicate canonical_name detected in manifest: '$canon'"
        }
        $seenNames[$canon] = $true

        if ($secret.value_source -notin $script:AllowedValueSources) {
            throw "Secret '$canon' has invalid value_source '$($secret.value_source)'. Allowed: $($script:AllowedValueSources -join ', ')"
        }

        # Conditional field: `service_ref` is REQUIRED when value_source == 'from-shared-service'.
        # Format: '<type>:<az-resource-name>' (e.g. 'search:sprksharedprod-search',
        # 'cognitiveservices:sprksharedprod-openai'). Consumed at run time by H4-shared
        # (SdkSourceServiceKeyExtractor) to dispatch to the right Azure.ResourceManager SDK
        # extractor. Absent for all other value_source values (kept as optional field).
        if ($secret.value_source -eq 'from-shared-service') {
            if (-not $secret.ContainsKey('service_ref')) {
                throw "Secret '$canon' has value_source='from-shared-service' but is missing required conditional field 'service_ref'. Format: '<type>:<az-resource-name>' (e.g. 'search:sprksharedprod-search')."
            }
            $svcRef = [string]$secret.service_ref
            if ([string]::IsNullOrWhiteSpace($svcRef)) {
                throw "Secret '$canon' has value_source='from-shared-service' but 'service_ref' is empty. Format: '<type>:<az-resource-name>'."
            }
            if ($svcRef -notmatch '^[a-z][a-z0-9-]*:[A-Za-z0-9][A-Za-z0-9-]*$') {
                throw "Secret '$canon' has malformed service_ref '$svcRef'. Expected '<type>:<az-resource-name>' where type is lowercase (e.g. 'search:sprksharedprod-search')."
            }
        }

        # never_delete must be an actual bool — powershell-yaml maps `true`/`false` to [bool].
        $nd = $secret.never_delete
        if ($null -eq $nd -or ($nd -isnot [bool])) {
            throw "Secret '$canon' has non-boolean never_delete value: '$nd' (type: $($nd?.GetType().FullName))"
        }
    }
}

function Test-PerEnvSettingsShape {
    <#
        Task 201 — validates the optional `per_env_settings:` top-level list.
        Backwards-compatible: if the list is absent the manifest still validates
        (older manifests + tests do not require the new list).

        BINDING guard: refuses any entry whose `key` matches a never_delete
        secret canonical name from `secrets:` (Dataverse-ClientSecret or
        BFF-API-ClientSecret) — prevents accidentally writing a cleartext BFF
        client secret as an App Service app-setting literal. spec.md MUST rule.
    #>
    param([hashtable]$Data)

    # Absent list is fine (v1 backwards-compat).
    if (-not $Data.ContainsKey('per_env_settings')) { return }
    if ($null -eq $Data.per_env_settings) { return }
    if (-not ($Data.per_env_settings -is [System.Collections.IList])) {
        throw "Manifest field 'per_env_settings' must be a list (found: $($Data.per_env_settings.GetType().FullName))."
    }
    if ($Data.per_env_settings.Count -eq 0) { return }

    # Build BINDING never-delete guard set from the secrets: list PLUS the
    # hardcoded pair (belt-and-braces — the hardcoded list is authoritative
    # per Test-BindingNeverDeleteInvariant, but scanning secrets: catches any
    # future additions to the never-delete family).
    $bindingSet = @{}
    foreach ($n in $script:BindingNeverDelete) { $bindingSet[$n] = $true }
    foreach ($s in $Data.secrets) {
        if ([bool]$s.never_delete) {
            $bindingSet[[string]$s.canonical_name] = $true
        }
    }

    $seenKeys = @{}
    foreach ($entry in $Data.per_env_settings) {
        if (-not ($entry -is [hashtable])) {
            throw "Every 'per_env_settings' entry must be a mapping. Found: $($entry.GetType().FullName)."
        }

        foreach ($field in $script:RequiredPerEnvSettingFields) {
            if (-not $entry.ContainsKey($field)) {
                $k = if ($entry.ContainsKey('key')) { $entry.key } else { '<unnamed>' }
                throw "per_env_settings entry '$k' is missing required field: $field"
            }
        }

        $key = [string]$entry.key
        if ([string]::IsNullOrWhiteSpace($key)) {
            throw "A 'per_env_settings' entry has an empty key."
        }
        if ($seenKeys.ContainsKey($key)) {
            throw "Duplicate per_env_settings key detected: '$key'"
        }
        $seenKeys[$key] = $true

        # BINDING guard — key MUST NOT match a never_delete secret canonical.
        if ($bindingSet.ContainsKey($key)) {
            throw "BINDING violation: per_env_settings key '$key' collides with a never-delete secret canonical name. Writing this as a cleartext App Service literal would leak the BFF client secret. spec.md MUST rule + r3 handoff §4a. Remove from per_env_settings; keep as `secrets:` entry only."
        }

        $srcRaw = [string]$entry.per_env_source
        if ($srcRaw -notmatch $script:PerEnvSourcePattern) {
            throw "per_env_settings entry '$key' has malformed per_env_source '$srcRaw'. Expected: 'literal' OR 'from-{handler}-{output|parameter}:{key}' where handler is lowercase-alphanumeric and key is identifier-shaped."
        }

        # Conditional field: literal_value REQUIRED when per_env_source == literal.
        if ($srcRaw -eq 'literal') {
            if (-not $entry.ContainsKey('literal_value')) {
                throw "per_env_settings entry '$key' has per_env_source='literal' but missing required conditional field 'literal_value'."
            }
            $lv = [string]$entry.literal_value
            if ($null -eq $lv) {
                throw "per_env_settings entry '$key' has literal_value=null. Provide an explicit string value (empty string OK if intentional)."
            }
        } else {
            # literal_value MUST NOT be present when source is a handler-output/parameter
            # form — it would create an ambiguous authority (which wins?).
            if ($entry.ContainsKey('literal_value')) {
                throw "per_env_settings entry '$key' has per_env_source='$srcRaw' AND a literal_value — these are mutually exclusive. Remove literal_value OR change per_env_source to 'literal'."
            }
        }

        $iOpts = [string]$entry.iOptionsModule
        if ([string]::IsNullOrWhiteSpace($iOpts)) {
            throw "per_env_settings entry '$key' has empty iOptionsModule (documentation field — which BFF module consumes this)."
        }

        $req = $entry.required
        if ($null -eq $req -or ($req -isnot [bool])) {
            throw "per_env_settings entry '$key' has non-boolean 'required' value: '$req' (type: $($req?.GetType().FullName))"
        }
    }
}

function Get-SortedPerEnvSettings {
    param([hashtable]$Data)
    if (-not $Data.ContainsKey('per_env_settings')) { return @() }
    if ($null -eq $Data.per_env_settings) { return @() }
    if ($Data.per_env_settings.Count -eq 0) { return @() }
    # Deterministic alphabetical sort on `key` (ordinal / case-sensitive).
    return $Data.per_env_settings | Sort-Object -Property { [string]$_.key } -Culture 'en-US'
}

function Get-UniquePerEnvSources {
    <#
        Task 201 — extracts the UNIQUE (handler-tag, output|parameter, key)
        triples from per_env_settings entries with a non-literal per_env_source.
        These become one `[Parameter(Mandatory=$true)] [string] $<Var>`
        per unique source on the generated Configure-AppServiceSettings
        script's param(...) block. `literal` sources do NOT contribute
        parameters (their value is embedded verbatim).

        Returns objects: { Kind='literal' | 'from-*'; SourceKey; PsVarName }
        where PsVarName is the PascalCase transform of SourceKey (e.g.
        'kv_vault_uri' -> 'KvVaultUri', 'tenant_id' -> 'TenantId').
    #>
    param([System.Object[]]$SortedPerEnvSettings)

    $unique = [ordered]@{}
    foreach ($entry in $SortedPerEnvSettings) {
        $src = [string]$entry.per_env_source
        if ($src -eq 'literal') { continue }
        # Parse 'from-{handler}-{output|parameter}:{key}' -> key segment.
        $colon = $src.IndexOf(':')
        if ($colon -lt 0) { continue }  # already validated by Test-PerEnvSettingsShape
        $sourceKey = $src.Substring($colon + 1)
        if ([string]::IsNullOrWhiteSpace($sourceKey)) { continue }
        if ($unique.Contains($sourceKey)) { continue }

        $psVar = ConvertTo-PascalCase -SnakeOrKebabName $sourceKey
        $unique[$sourceKey] = [pscustomobject]@{
            SourceKey = $sourceKey
            PsVarName = $psVar
            RawSource = $src
        }
    }
    return @($unique.Values)
}

function ConvertTo-PascalCase {
    <#
        Deterministic snake_case / kebab-case -> PascalCase.
        'kv_vault_uri'    -> 'KvVaultUri'
        'tenant_id'       -> 'TenantId'
        'bff-app-cid'     -> 'BffAppCid'
        Ensures the leading char is a letter (prefixes 'P' if not) so the
        emitted PowerShell parameter name is always syntactically valid.
    #>
    param([string]$SnakeOrKebabName)

    if ([string]::IsNullOrWhiteSpace($SnakeOrKebabName)) { return 'P' }

    $parts = $SnakeOrKebabName -split '[_\-]+' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    $sb = [System.Text.StringBuilder]::new()
    foreach ($p in $parts) {
        if ($p.Length -eq 0) { continue }
        [void]$sb.Append($p.Substring(0, 1).ToUpperInvariant())
        if ($p.Length -gt 1) { [void]$sb.Append($p.Substring(1)) }
    }
    $result = $sb.ToString()
    if ([string]::IsNullOrEmpty($result)) { return 'P' }
    if ($result[0] -notmatch '[A-Za-z]') {
        return 'P' + $result
    }
    return $result
}

function Test-BindingNeverDeleteInvariant {
    <#
        BINDING guard per spec.md MUST rules + r3 handoff §4a. This is the
        Quarantine-required-class safety promised in the task prompt:
        the generator must fail LOUDLY if either binding secret is missing
        from the manifest or has never_delete=false.
    #>
    param([hashtable]$Data)

    $byName = @{}
    foreach ($s in $Data.secrets) {
        $byName[[string]$s.canonical_name] = $s
    }

    $violations = @()
    foreach ($required in $script:BindingNeverDelete) {
        if (-not $byName.ContainsKey($required)) {
            $violations += "MISSING: '$required' is a BINDING never-delete secret (spec.md MUST rule line 242 + r3 handoff §4a) but is absent from manifest.secrets."
            continue
        }
        $entry = $byName[$required]
        if (-not $entry.never_delete) {
            $violations += "INVARIANT: '$required' has never_delete=false. This secret is BINDING never-delete (spec.md MUST rule line 242 + r3 handoff §4a). Removing it CRASHES the BFF at startup (OBO + shared-lib Dataverse depend on it)."
        }
    }

    if ($violations.Count -gt 0) {
        $header = "BINDING never-delete invariant violated. Generator refuses to write outputs."
        $body   = ($violations -join "`n  - ")
        throw "$header`n  - $body`n`nSee: spec.md MUST rules · projects/customer-provisioning-orchestration-r1/notes/naming-exception-registry.md rows 2-3 · projects/code-quality-and-assurance-r3/notes/task-063-naming-standard-r1-handoff.md"
    }
}

function Test-E3ClosedNoAppSettingsInvariant {
    <#
        Bucket B HIGH#5 + MED#14 invariant (customer-provisioning-orchestration-r1
        SESSION 18, adversarial verify workflow wepdcb8we +
        .claude/constraints/provisioning.md § KV credential lifecycle rule 1):

        Any secret whose exception_note contains 'E-3 CLOSED' MUST have empty
        app_settings. Rationale: these entries are ROLLBACK SLOTS ONLY. The KV
        secret is retained during the auth-v4 soak window (through 2026-11-23)
        but NO live BFF app-setting may reference it — the setting was removed
        by E-3 closure on 2026-08-24. A future edit that puts an app_settings
        entry back on such a secret would regenerate the Configure-AppServiceSettings
        script pointing App Service KV-refs at a soft-deleted secret → silent 404
        → App Service hydrates the literal @KV(...) string → confidential-client
        init throws AADSTS7000215 per §9.1 opaque failure.

        Enforcement: this function throws BEFORE any artifact is written, so a
        regression is caught at generator time (author writes the manifest edit,
        runs the generator, generator refuses; author must fix manifest).
        Same shape as Test-BindingNeverDeleteInvariant + Test-DevExceptionInvariant.
    #>
    param([hashtable]$Data)

    $violations = @()
    foreach ($s in $Data.secrets) {
        $canonical = [string]$s.canonical_name
        $exceptionNote = if ($s.ContainsKey('exception_note')) { [string]$s.exception_note } else { '' }
        $appSettings = @()
        if ($s.ContainsKey('app_settings') -and $null -ne $s.app_settings) {
            $appSettings = @($s.app_settings)
        }

        $isE3Closed = $exceptionNote -match 'E-3 CLOSED' -or $exceptionNote -match 'DELETED from KV 2026-08-24'
        if ($isE3Closed -and $appSettings.Count -gt 0) {
            $violations += "'$canonical' has exception_note marking it E-3 CLOSED (auth-v4 task 033 removed the app setting 2026-08-24) BUT app_settings=[$(($appSettings) -join ', ')] is non-empty. Re-populating app_settings on an E-3-closed secret would regenerate Configure-AppServiceSettings to hydrate App Service KV-refs pointing at a soft-deleted secret — silent 404 at boot, AADSTS7000215 at first token request. Set app_settings: [] (rollback-slot-only) and document in exception_note."
        }
    }

    if ($violations.Count -gt 0) {
        $header = "E-3-closed app_settings invariant violated. Generator refuses to write outputs. Bucket B HIGH#5/MED#14 SESSION 18."
        $body   = ($violations -join "`n  - ")
        throw "$header`n  - $body`n`nSee: .claude/constraints/provisioning.md § KV credential lifecycle rule 1 · customer-provisioning-orchestration-r1 adversarial verify workflow wepdcb8we"
    }
}

function Test-DevExceptionInvariant {
    <#
        The `spaarke-spekvcert` DO-NOT-RENAME exception per §7.9 R3 lives in
        naming-exception-registry.md. This manifest surfaces it via
        vault_name_exceptions.dev. If it goes missing, downstream consumers
        (naming-conformance-check, seeder, Bicep param default) can silently
        drift to the canonical form and break the live dev environment.
    #>
    param([hashtable]$Data)

    if (-not $Data.ContainsKey('vault_name_exceptions')) {
        throw "Manifest is missing vault_name_exceptions block. `dev: spaarke-spekvcert` MUST be present per §7.9 R3 + naming-exception-registry.md."
    }
    $ex = $Data.vault_name_exceptions
    if (-not ($ex -is [hashtable])) {
        throw "vault_name_exceptions must be a mapping (env -> vault-name)."
    }
    if (-not $ex.ContainsKey('dev') -or [string]$ex.dev -ne 'spaarke-spekvcert') {
        throw "vault_name_exceptions.dev MUST equal 'spaarke-spekvcert' per §7.9 R3 + naming-exception-registry.md. Rename would break dev-env cert-based flows + KV references + Dataverse-persisted config."
    }
}

function Get-SortedSecrets {
    param([hashtable]$Data)
    # Deterministic alphabetical sort on canonical_name (case-sensitive).
    return $Data.secrets | Sort-Object -Property { [string]$_.canonical_name } -Culture 'en-US'
}

function ConvertTo-JoinedString {
    param([object]$Value, [string]$Separator = ', ')
    if ($null -eq $Value) { return '' }
    if ($Value -is [string]) { return $Value }
    return ($Value | ForEach-Object { [string]$_ }) -join $Separator
}

function Convert-ToSingleLine {
    <#
        Some manifest fields (purpose, exception_note) allow multi-line
        text via the YAML `>-` folded scalar; downstream artifacts want
        a single line. Collapses whitespace runs to single spaces + trims.
    #>
    param([string]$Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return '' }
    return ($Text -replace '\s+', ' ').Trim()
}

# ---------------------------------------------------------------------------
# Output artifact renderers
# ---------------------------------------------------------------------------

function New-SeederArtifact {
    <#
        Emits Seed-CustomerKeyVault.generated.ps1 — the seeder that upserts
        every canonical secret to a target Key Vault. Uses parameterized
        vault name (§7.9 R3); operator passes the vault (canonical
        `sprk-{env}-kv` or the codified `spaarke-spekvcert` dev exception).
    #>
    param([System.Object[]]$Sorted)

    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.Append(@"
$script:GeneratorHeaderMark
# Generated by scripts/canonical-secret-catalog/Invoke-CatalogGenerator.ps1 v$script:GeneratorVersion
# Source of truth: scripts/canonical-secret-catalog/manifest.yaml
# Regenerate: pwsh scripts/canonical-secret-catalog/Invoke-CatalogGenerator.ps1
# Verify (CI): pwsh scripts/canonical-secret-catalog/Invoke-CatalogGenerator.ps1 -Verify

<#
.SYNOPSIS
    Seed a customer Key Vault with the canonical secret catalog (Phase H).

.DESCRIPTION
    Idempotent seeder emitting every canonical KV secret per manifest.yaml.
    Value provenance is external: this script pre-creates the secret SLOT
    with a placeholder value if -SeedPlaceholders is set (default OFF), and
    otherwise SKIPS entries whose values are supplied downstream (Bicep
    outputs, run parameters, existing KV, or in-place generation).

    Callers:
      - L2 H4 handler (Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation)
      - Manual operator seeding for maintenance windows / migration

.PARAMETER VaultName
    Target Key Vault. Canonical: sprk-{env}-kv. Dev DO-NOT-RENAME exception:
    spaarke-spekvcert (see naming-exception-registry.md).

.PARAMETER SkipExisting
    Skip secrets that already exist on the target vault. Default: true.

.PARAMETER SeedPlaceholders
    Populate placeholder values for slots whose value_source is not
    from-existing-kv. USE FOR NEW-ENVIRONMENT SETUP ONLY — real values MUST
    replace placeholders before production use.

.EXAMPLE
    pwsh Seed-CustomerKeyVault.generated.ps1 -VaultName sprk-demo-kv -SeedPlaceholders

.EXAMPLE
    pwsh Seed-CustomerKeyVault.generated.ps1 -VaultName spaarke-spekvcert -SkipExisting:`$true
#>

#Requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory = `$true)]
    [string]`$VaultName,

    [bool]`$SkipExisting = `$true,

    [switch]`$SeedPlaceholders
)

Set-StrictMode -Version Latest
`$ErrorActionPreference = 'Stop'

# BINDING never-delete pair — same list as manifest guard.
`$script:BindingNeverDelete = @('Dataverse-ClientSecret', 'BFF-API-ClientSecret')

function Set-VaultSecret {
    param(
        [string]`$Name,
        [string]`$Value,
        [string]`$Description,
        [string]`$Category
    )
    # ---------------------------------------------------------------------
    # Bucket B HIGH#11 guard (customer-provisioning-orchestration-r1 SESSION 18,
    # adversarial e2e verify workflow wepdcb8we) + .claude/constraints/provisioning.md
    # § KV credential lifecycle rule 1:
    #
    # BindingNeverDelete secrets (BFF-API-ClientSecret, Dataverse-ClientSecret) are
    # retained as ROLLBACK SLOTS ONLY on their canonical platform vault. This seeder
    # MUST NEVER CREATE them anywhere. Prior to this guard, if the target vault did
    # NOT already contain the secret, the `-SkipExisting` early-out at line 66 fell
    # through and the placeholder Set-VaultSecret call at line 703 (from the
    # from-existing-kv emission branch) would seed placeholder-value-source-is-existing-kv
    # into a secret-free customer vault — a silent contract violation.
    #
    # The guard is written as fail-loud REFUSAL (not silent skip) so an operator
    # running the seeder against the wrong vault gets a diagnostic pointing them
    # at the rollback runbook, not a phantom success. Enforcement is defense-in-depth
    # — the emission branches also skip these secrets in most modes — but a single
    # code path centralizes the invariant.
    # ---------------------------------------------------------------------
    if (`$script:BindingNeverDelete -contains `$Name) {
        `$existing = az keyvault secret show --vault-name `$VaultName --name `$Name --query 'name' --output tsv 2>`$null
        if (-not `$existing) {
            Write-Host "  REFUSED: `$Name (BINDING never-delete; not present in target vault '`$VaultName' — this seeder MUST NOT create it. Auth-v4 task 033 (2026-08-24) deleted both KV copies of BFF-API-ClientSecret; Dataverse-ClientSecret is retained ONLY on its canonical platform vault as rollback per auth-v4 §10. If a rollback genuinely requires re-seeding, use the auth-v4 rollback runbook — NOT this generator-emitted seeder.)" -ForegroundColor Yellow
            return
        }
        Write-Host "  SKIP: `$Name (BINDING never-delete; already present in vault, live value preserved)" -ForegroundColor Gray
        return
    }

    if (`$SkipExisting) {
        `$existing = az keyvault secret show --vault-name `$VaultName --name `$Name --query 'name' --output tsv 2>`$null
        if (`$existing) {
            Write-Host "  SKIP: `$Name (already exists)" -ForegroundColor Gray
            return
        }
    }
    az keyvault secret set --vault-name `$VaultName --name `$Name --value `$Value --description `$Description --tags "source=canonical-catalog" "category=`$Category" --output none 2>&1 | Out-Null
    if (`$LASTEXITCODE -ne 0) {
        Write-Host "  FAILED: `$Name" -ForegroundColor Red
        throw "Failed to set KV secret `$Name on vault `$VaultName"
    }
    Write-Host "  SET: `$Name" -ForegroundColor Green
}

Write-Host ''
Write-Host '=================================================================='
Write-Host '  Canonical KV Seeder — GENERATED from manifest.yaml'
Write-Host '=================================================================='
Write-Host "  Vault:                `$VaultName"
Write-Host "  SkipExisting:         `$SkipExisting"
Write-Host "  SeedPlaceholders:     `$(`$SeedPlaceholders.IsPresent)"
Write-Host "  BINDING never-delete: `$(`$script:BindingNeverDelete -join ', ')"
Write-Host '=================================================================='
Write-Host ''

"@)

    foreach ($secret in $Sorted) {
        $canon    = [string]$secret.canonical_name
        $category = [string]$secret.category
        $purpose  = Convert-ToSingleLine ([string]$secret.purpose)
        $source   = [string]$secret.value_source
        $isNeverDelete = [bool]$secret.never_delete

        [void]$sb.Append("`n# ---- $canon ($category) ----`n")
        [void]$sb.Append("# Purpose: $purpose`n")
        [void]$sb.Append("# Value source: $source`n")
        if ($isNeverDelete) {
            [void]$sb.Append("# BINDING never-delete — do NOT remove from any environment.`n")
        }

        # Emit either an unconditional seed (for from-existing-kv - never
        # overwrite live value; require -SeedPlaceholders explicitly for
        # placeholder creation), a from-shared-service handler-populated
        # marker (NO placeholder — H4-shared owns the write), or a conditional
        # placeholder seed for the other value_sources.
        if ($source -eq 'from-existing-kv') {
            [void]$sb.Append("if (`$SeedPlaceholders -and -not `$SkipExisting) {`n")
            [void]$sb.Append("    Write-Host '  REFUSED: $canon (from-existing-kv; refusing to overwrite; set SkipExisting=`$true or remove -SeedPlaceholders)' -ForegroundColor Yellow`n")
            [void]$sb.Append("} else {`n")
            [void]$sb.Append("    Set-VaultSecret -Name '$canon' -Value 'placeholder-value-source-is-existing-kv' -Description '$($purpose -replace "'","''") [BINDING never-delete: skip in seed]' -Category '$category'`n")
            [void]$sb.Append("}`n")
        } elseif ($source -eq 'from-shared-service') {
            # Handler-populated (H4-shared): value extracted from source service at run time.
            # The seeder deliberately does NOT write a placeholder — the value is owned by
            # H4SharedKvSecretsPopulationHandler which extracts fresh from the source Azure
            # service via Azure.ResourceManager SDK and writes idempotently to shared KV.
            # A placeholder here would be overwritten anyway and would trigger BFF fail-fast
            # if the handler run were delayed.
            $serviceRef = if ($secret.ContainsKey('service_ref')) { [string]$secret.service_ref } else { '<unspecified>' }
            [void]$sb.Append("Write-Host '  SKIP: $canon (value_source=from-shared-service; handler-populated by H4-shared at run time from source $serviceRef)' -ForegroundColor Gray`n")
        } else {
            [void]$sb.Append("if (`$SeedPlaceholders) {`n")
            [void]$sb.Append("    Set-VaultSecret -Name '$canon' -Value 'placeholder-$($source)' -Description '$($purpose -replace "'","''")' -Category '$category'`n")
            [void]$sb.Append("} else {`n")
            [void]$sb.Append("    Write-Host '  SKIP: $canon (value_source=$source; supplied downstream)' -ForegroundColor Gray`n")
            [void]$sb.Append("}`n")
        }
    }

    [void]$sb.Append(@"

Write-Host ''
Write-Host '=================================================================='
Write-Host '  Seeder complete.'
Write-Host '=================================================================='
"@)

    # LF-normalize + trailing newline for byte-determinism.
    return (($sb.ToString() -replace "`r`n", "`n").TrimEnd() + "`n")
}

function New-ConfigureArtifact {
    <#
        Emits Configure-AppServiceSettings.generated.ps1 — sets App Service
        app settings that reference the vault via KV references AND (task 201)
        emits per-env-literal lines from the sibling `per_env_settings:` list.
        Both classes of lines accumulate into the SAME `$settings` array so a
        single batched `az webapp config appsettings set --settings @settings`
        call per slot is preserved (ONE restart cycle regardless of setting
        count).
    #>
    param(
        [System.Object[]]$Sorted,
        [System.Object[]]$SortedPerEnv = @()
    )

    # Task 201 — extract unique non-literal sources so we can emit one
    # [Parameter(Mandatory=$true)] per source on the generated script's
    # param(...) block. Deterministic order (sorted by SourceKey ordinal).
    $perEnvSources = @(Get-UniquePerEnvSources -SortedPerEnvSettings $SortedPerEnv | Sort-Object -Property SourceKey -Culture 'en-US')

    # Build a lookup: SourceKey -> PsVarName so the emit loop can insert
    # `$<PsVarName>` verbatim (values are cleartext at RUNTIME only — never at
    # generator time; the generated script's param block accepts them).
    $sourceToVar = @{}
    foreach ($s in $perEnvSources) { $sourceToVar[$s.SourceKey] = $s.PsVarName }

    # Compose the parameter block. Fixed params (RG / AppService / VaultName /
    # IncludeSlots) plus one per unique per-env source.
    $paramLines = [System.Collections.Generic.List[string]]::new()
    [void]$paramLines.Add("    [Parameter(Mandatory = `$true)]")
    [void]$paramLines.Add("    [string]`$ResourceGroupName,")
    [void]$paramLines.Add("")
    [void]$paramLines.Add("    [Parameter(Mandatory = `$true)]")
    [void]$paramLines.Add("    [string]`$AppServiceName,")
    [void]$paramLines.Add("")
    [void]$paramLines.Add("    [Parameter(Mandatory = `$true)]")
    [void]$paramLines.Add("    [string]`$VaultName,")
    foreach ($src in $perEnvSources) {
        [void]$paramLines.Add("")
        [void]$paramLines.Add("    [Parameter(Mandatory = `$true)]")
        [void]$paramLines.Add("    [string]`$$($src.PsVarName),")
    }
    [void]$paramLines.Add("")
    [void]$paramLines.Add("    [bool]`$IncludeSlots = `$true")
    $paramBlock = ($paramLines -join "`n")

    # Alphabetically-merged $settings array — KV-ref lines expand from each
    # secret's app_settings list; per-env-literal lines expand from per_env_settings.
    # BOTH classes emit `"<key>=<value-expression>"` strings that go into the
    # SAME array so ONE batched write is preserved. Sort by app-setting key
    # ordinal for determinism.
    $settingLines = [System.Collections.Generic.List[pscustomobject]]::new()
    foreach ($secret in $Sorted) {
        $canon = [string]$secret.canonical_name
        $appSettings = @()
        if ($secret.app_settings -is [System.Collections.IList]) {
            $appSettings = @(@($secret.app_settings | ForEach-Object { [string]$_ }))
        }
        foreach ($k in $appSettings) {
            [void]$settingLines.Add([pscustomobject]@{
                Key   = $k
                Line  = "`"$k=`$(Format-KvRef '$canon')`""
            })
        }
    }
    foreach ($entry in $SortedPerEnv) {
        $key = [string]$entry.key
        $src = [string]$entry.per_env_source
        if ($src -eq 'literal') {
            # Embed literal_value verbatim; ' is escaped for single-quoted string.
            $lit = [string]$entry.literal_value
            $litEscaped = $lit -replace "'", "''"
            [void]$settingLines.Add([pscustomobject]@{
                Key  = $key
                Line = "`"$key=$litEscaped`""
            })
        } else {
            # per_env_source parsed form: pass through the PS variable that
            # the operator supplies via -<PsVarName> on the generated script's
            # invocation. Value is cleartext at RUNTIME only.
            $colon = $src.IndexOf(':')
            $sourceKey = $src.Substring($colon + 1)
            $var = $sourceToVar[$sourceKey]
            [void]$settingLines.Add([pscustomobject]@{
                Key  = $key
                Line = "`"$key=`$$var`""
            })
        }
    }
    $settingLines = @($settingLines | Sort-Object -Property Key -Culture 'en-US')

    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.Append(@"
$script:GeneratorHeaderMark
# Generated by scripts/canonical-secret-catalog/Invoke-CatalogGenerator.ps1 v$script:GeneratorVersion
# Source of truth: scripts/canonical-secret-catalog/manifest.yaml
# Regenerate: pwsh scripts/canonical-secret-catalog/Invoke-CatalogGenerator.ps1

<#
.SYNOPSIS
    Configure App Service app settings from the canonical KV secret catalog
    (Phase H) PLUS the sibling per_env_settings list (task 201 — H4b). ONE
    batched call per slot preserves single-restart semantics.

.DESCRIPTION
    Sets every canonical app-setting key from manifest.yaml as a Key Vault
    reference (@Microsoft.KeyVault(VaultName=...;SecretName=...)) AND every
    per-env-literal app-setting key from manifest.per_env_settings as a cleartext
    literal (URIs, public GUIDs, generated signing keys — things not
    appropriate as KV secrets OR needed to bootstrap KV access). ONE
    ``az webapp config appsettings set --settings @settings`` call per slot
    triggers a SINGLE App Service restart cycle regardless of setting count.

.PARAMETER ResourceGroupName
    Resource group.

.PARAMETER AppServiceName
    App Service name (e.g. spaarke-bff-demo).

.PARAMETER VaultName
    Key Vault name (canonical sprk-{env}-kv, or spaarke-spekvcert dev exception).

.PARAMETER IncludeSlots
    Also configure staging slot. Default: `$true.
#>

#Requires -Version 7.0

[CmdletBinding()]
param(
$paramBlock
)

Set-StrictMode -Version Latest
`$ErrorActionPreference = 'Stop'

function Format-KvRef {
    param([string]`$Secret)
    # Canonical single-form KV reference syntax.
    return "@Microsoft.KeyVault(VaultName=`$VaultName;SecretName=`$Secret)"
}

`$settings = @(
"@)

    for ($i = 0; $i -lt $settingLines.Count; $i++) {
        $suffix = if ($i -lt $settingLines.Count - 1) { ',' } else { '' }
        [void]$sb.Append("`n    $($settingLines[$i].Line)$suffix")
    }

    [void]$sb.Append(@"

)

Write-Host ''
Write-Host '=================================================================='
Write-Host '  Configure App Service Settings — GENERATED from manifest.yaml'
Write-Host '=================================================================='
Write-Host "  App Service:  `$AppServiceName"
Write-Host "  Key Vault:    `$VaultName"
Write-Host "  Setting count: `$(`$settings.Count)"
Write-Host '=================================================================='
Write-Host ''

az webapp config appsettings set ``
    --resource-group `$ResourceGroupName ``
    --name `$AppServiceName ``
    --settings @settings ``
    --output none 2>&1 | Out-Null
if (`$LASTEXITCODE -ne 0) { throw 'Failed to set production-slot app settings.' }
Write-Host "  Production slot: `$(`$settings.Count) settings configured." -ForegroundColor Green

if (`$IncludeSlots) {
    az webapp config appsettings set ``
        --resource-group `$ResourceGroupName ``
        --name `$AppServiceName ``
        --slot staging ``
        --settings @settings ``
        --output none 2>&1 | Out-Null
    if (`$LASTEXITCODE -ne 0) { throw 'Failed to set staging-slot app settings.' }
    Write-Host "  Staging slot:    `$(`$settings.Count) settings configured." -ForegroundColor Green
}

Write-Host ''
Write-Host '  Verify with:'
Write-Host "    az webapp config appsettings list --resource-group `$ResourceGroupName --name `$AppServiceName --output table"
Write-Host ''
"@)

    return (($sb.ToString() -replace "`r`n", "`n").TrimEnd() + "`n")
}

function New-TokensDocArtifact {
    <#
        Emits appsettings.tokens.generated.md — the operator-facing reference
        doc for every canonical KV secret + its app-setting bindings + alias
        map. Sorted alphabetically for determinism.
    #>
    param([hashtable]$Data, [System.Object[]]$Sorted)

    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.Append(@"
<!-- $script:GeneratorHeaderMark -->
<!-- Generated by scripts/canonical-secret-catalog/Invoke-CatalogGenerator.ps1 v$script:GeneratorVersion -->
<!-- Source of truth: scripts/canonical-secret-catalog/manifest.yaml -->
<!-- Regenerate: pwsh scripts/canonical-secret-catalog/Invoke-CatalogGenerator.ps1 -->

# Canonical KV Secret Reference

**Authority**: spec.md FR-36 + §7.9 R1-R4 · [AZURE-RESOURCE-NAMING-CONVENTION.md](../../docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md) · [naming-exception-registry.md](../../projects/customer-provisioning-orchestration-r1/notes/naming-exception-registry.md)

This document is **generated** from ``scripts/canonical-secret-catalog/manifest.yaml``. Do NOT edit by hand — any local edit is destroyed by the next generator run.

## Vault-name policy

- **Canonical**: ``$($Data.vault_name_pattern)``
- **KV reference syntax (single form)**: ``$($Data.kv_reference_syntax)``

### Codified vault-name exceptions

"@)

    if ($Data.ContainsKey('vault_name_exceptions')) {
        $ex = $Data.vault_name_exceptions
        # Iterate keys deterministically (alphabetically).
        $keys = @($ex.Keys | ForEach-Object { [string]$_ }) | Sort-Object
        foreach ($k in $keys) {
            [void]$sb.Append("- **``$k``** -> ``$($ex[$k])`` (see naming-exception-registry.md)`n")
        }
    }

    [void]$sb.Append(@"

## Secret catalog

| Canonical Name | Category | Rotation | Never-Delete | Value Source |
|---|---|---|:---:|---|
"@)
    [void]$sb.Append("`n")

    foreach ($secret in $Sorted) {
        $canon    = [string]$secret.canonical_name
        $category = [string]$secret.category
        $rot      = [string]$secret.rotation_cadence
        $nd       = if ([bool]$secret.never_delete) { 'YES' } else { '-' }
        $source   = [string]$secret.value_source
        [void]$sb.Append("| ``$canon`` | $category | $rot | $nd | $source |`n")
    }

    [void]$sb.Append("`n## Per-secret detail`n")

    foreach ($secret in $Sorted) {
        $canon    = [string]$secret.canonical_name
        $category = [string]$secret.category
        $purpose  = Convert-ToSingleLine ([string]$secret.purpose)
        $rot      = [string]$secret.rotation_cadence
        $source   = [string]$secret.value_source
        $nd       = if ([bool]$secret.never_delete) { 'YES (BINDING)' } else { 'no' }
        $note     = Convert-ToSingleLine ([string]$secret.exception_note)

        $consumers = @()
        if ($secret.consumers -is [System.Collections.IList]) {
            $consumers = @(@($secret.consumers | ForEach-Object { [string]$_ }) | Sort-Object)
        }

        $appSettings = @()
        if ($secret.app_settings -is [System.Collections.IList]) {
            $appSettings = @(@($secret.app_settings | ForEach-Object { [string]$_ }) | Sort-Object)
        }

        $aliases = @()
        if ($secret.aliases -is [System.Collections.IList]) {
            $aliases = @(@($secret.aliases | ForEach-Object { [string]$_ }) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object)
        }

        $tags = @()
        if ($secret.tags -is [System.Collections.IList]) {
            $tags = @(@($secret.tags | ForEach-Object { [string]$_ }) | Sort-Object)
        }

        [void]$sb.Append("`n### ``$canon```n")
        [void]$sb.Append("`n- **Category**: $category`n")
        [void]$sb.Append("- **Purpose**: $purpose`n")
        [void]$sb.Append("- **Rotation cadence**: $rot`n")
        [void]$sb.Append("- **Never-delete (BINDING)**: $nd`n")
        [void]$sb.Append("- **Value source**: $source`n")
        if ($tags.Count -gt 0) {
            [void]$sb.Append("- **Tags**: $($tags -join ', ')`n")
        }
        if (-not [string]::IsNullOrWhiteSpace($note)) {
            [void]$sb.Append("- **Exception note**: $note`n")
        }
        if ($consumers.Count -gt 0) {
            [void]$sb.Append("- **Consumers**:`n")
            foreach ($c in $consumers) {
                [void]$sb.Append("  - $c`n")
            }
        }
        if ($appSettings.Count -gt 0) {
            [void]$sb.Append("- **App-setting keys**:`n")
            foreach ($k in $appSettings) {
                [void]$sb.Append("  - ``$k```n")
            }
        }
        if ($aliases.Count -gt 0) {
            [void]$sb.Append("- **Aliases / drift spellings (alias-collapse targets — do NOT reintroduce)**:`n")
            foreach ($a in $aliases) {
                [void]$sb.Append("  - ``$a```n")
            }
        }
    }

    [void]$sb.Append(@"

## Aliases index (alias-collapse targets)

The following drift spellings are documented as aliases on canonical entries. Task 085 (Phase H alias collapse) removes them from live environments AFTER the BINDING pre-check (LIVE App-Service + KV + Dataverse config check per §7.9 R4).

| Alias (drift) | Canonical | Notes |
|---|---|---|
"@)
    [void]$sb.Append("`n")

    # Alias index — deterministic: sort by alias then by canonical.
    $rows = @()
    foreach ($secret in $Sorted) {
        $canon = [string]$secret.canonical_name
        if ($secret.aliases -is [System.Collections.IList]) {
            foreach ($alias in $secret.aliases) {
                $aStr = [string]$alias
                if (-not [string]::IsNullOrWhiteSpace($aStr)) {
                    $rows += [pscustomobject]@{
                        Alias     = $aStr
                        Canonical = $canon
                        Category  = [string]$secret.category
                    }
                }
            }
        }
    }
    $rows = $rows | Sort-Object -Property @{Expression = 'Alias'; Ascending = $true}, @{Expression = 'Canonical'; Ascending = $true}
    if ($rows.Count -eq 0) {
        [void]$sb.Append("| _(none)_ | _(none)_ | _(no aliases in current manifest)_ |`n")
    } else {
        foreach ($r in $rows) {
            [void]$sb.Append("| ``$($r.Alias)`` | ``$($r.Canonical)`` | $($r.Category) |`n")
        }
    }

    return (($sb.ToString() -replace "`r`n", "`n").TrimEnd() + "`n")
}

function New-BicepArtifact {
    <#
        Emits kv-secrets.generated.bicep — a Bicep module declaring every
        canonical secret slot as a `Microsoft.KeyVault/vaults/secrets`
        resource. Values are ALWAYS supplied by the caller (either as
        module params or via `existing` resource references) — this file
        never embeds a cleartext value. Alphabetical order enforced.
    #>
    param([System.Object[]]$Sorted)

    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.Append(@"
// $script:GeneratorHeaderMark
// Generated by scripts/canonical-secret-catalog/Invoke-CatalogGenerator.ps1 v$script:GeneratorVersion
// Source of truth: scripts/canonical-secret-catalog/manifest.yaml
// Regenerate: pwsh scripts/canonical-secret-catalog/Invoke-CatalogGenerator.ps1
//
// This module declares the CANONICAL secret slot per manifest.yaml. It
// intentionally does NOT emit cleartext values (secretValues remain the
// caller's responsibility, resolved from module params, Bicep outputs of
// upstream resource modules, or existing KV resource references).
//
// Consumers (task 086 — IaC alignment): reference this module from
// customer.bicep / model2-full.bicep / model1-shared.bicep instead of
// declaring KV secrets inline. This is the single canonical source.
//
// SKIP-IF-ABSENT semantics (G-8 Batch 1 defect #15): a secret resource is
// deployed ONLY when the caller supplies a real value via secretValues —
// every resource carries an `if (contains(secretValues, '<name>'))` guard
// and NO placeholder value is ever written. Secrets absent from the map are
// NOT TOUCHED on (re-)deploy, so H3-written ClientId/Audience, H8-written
// SPE ids, generated webhook keys, and operator-populated values survive
// every redeploy. (The previous unconditional-ternary emission reset every
// unsupplied secret to 'placeholder-populate-out-of-band' on re-deploy —
// a BINDING never-delete violation.)
//
// BINDING guard: the two never-delete secrets (Dataverse-ClientSecret,
// BFF-API-ClientSecret) are NOT declared as writable ARM resources AT ALL —
// ARM cannot express "create only if missing" for KV secrets, so their
// slot pre-creation + population is exclusively out-of-band (data-plane
// seeder Seed-CustomerKeyVault.generated.ps1 with -SkipExisting, the H4
// handler, or the operator) per spec.md MUST rules + r3 handoff §4a.

@description('Target Key Vault name (canonical sprk-{env}-kv per §7.9 R3; dev exception spaarke-spekvcert).')
param keyVaultName string

@description('Per-secret value map (secret name -> value). ONLY secrets present in this map are deployed; absent secrets are left untouched (skip-if-absent — no placeholders are ever written).')
@secure()
param secretValues object = {}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

// -------------------------------------------------------------------------
// Canonical secrets (alphabetically-sorted, generator-enforced)
// -------------------------------------------------------------------------
"@)

    foreach ($secret in $Sorted) {
        $canon   = [string]$secret.canonical_name
        $purpose = Convert-ToSingleLine ([string]$secret.purpose)
        $nd      = [bool]$secret.never_delete
        $rot     = [string]$secret.rotation_cadence
        $source  = [string]$secret.value_source

        # Bicep resource identifiers must be alphanumeric — derive a
        # deterministic camel-cased id from the canonical name.
        $id = ($canon -replace '[^A-Za-z0-9]', '_')
        # Ensure it starts with a lowercase letter for Bicep symbol style.
        if ($id.Length -gt 0) {
            $id = $id.Substring(0, 1).ToLowerInvariant() + $id.Substring(1)
        }

        $ndTag = if ($nd) { 'true' } else { 'false' }

        [void]$sb.Append("`n// $canon — $purpose`n")
        if ($nd) {
            # BINDING never-delete secrets are NOT emitted as ARM resources at
            # all (G-8 Batch 1 defect #15): ARM cannot express skip-if-exists
            # for KV secrets, so any writable declaration risks clobbering the
            # live value on re-deploy. Slot creation + population is exclusively
            # out-of-band (seeder -SkipExisting / H4 handler / operator).
            [void]$sb.Append("// BINDING never-delete (spec.md MUST rule + r3 handoff §4a) — NOT declared as an ARM`n")
            [void]$sb.Append("// resource: ARM cannot skip-if-exists, so this secret is managed exclusively out-of-band`n")
            [void]$sb.Append("// (Seed-CustomerKeyVault.generated.ps1 -SkipExisting / H4 handler / operator). A re-deploy`n")
            [void]$sb.Append("// of this module can therefore NEVER touch the live value.`n")
            continue
        }
        [void]$sb.Append("resource kv_$id 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (contains(secretValues, '$canon')) {`n")
        [void]$sb.Append("  parent: keyVault`n")
        [void]$sb.Append("  name: '$canon'`n")
        [void]$sb.Append("  properties: {`n")
        [void]$sb.Append("    value: secretValues['$canon']`n")
        [void]$sb.Append("    attributes: {`n")
        [void]$sb.Append("      enabled: true`n")
        [void]$sb.Append("    }`n")
        [void]$sb.Append("    contentType: '$source'`n")
        [void]$sb.Append("  }`n")
        [void]$sb.Append("  tags: {`n")
        [void]$sb.Append("    canonicalName: '$canon'`n")
        [void]$sb.Append("    category: '$([string]$secret.category)'`n")
        [void]$sb.Append("    rotation: '$rot'`n")
        [void]$sb.Append("    neverDelete: '$ndTag'`n")
        [void]$sb.Append("    managedBy: 'canonical-secret-catalog-generator'`n")
        [void]$sb.Append("  }`n")
        [void]$sb.Append("}`n")
    }

    [void]$sb.Append(@"

// -------------------------------------------------------------------------
// Outputs — canonical secret name index (for downstream module wiring)
// -------------------------------------------------------------------------

output canonicalSecretNames array = [
"@)
    foreach ($secret in $Sorted) {
        [void]$sb.Append("`n  '$([string]$secret.canonical_name)'")
    }
    [void]$sb.Append("`n]`n")

    return (($sb.ToString() -replace "`r`n", "`n").TrimEnd() + "`n")
}

# ---------------------------------------------------------------------------
# Output orchestration
# ---------------------------------------------------------------------------

function New-AllArtifacts {
    param([hashtable]$Data)

    $sorted = Get-SortedSecrets -Data $Data
    # Task 201 — pre-sorted per_env_settings list (empty array when absent).
    $sortedPerEnv = @(Get-SortedPerEnvSettings -Data $Data)

    # Deterministic, alphabetically-sorted output map.
    $outputs = [ordered]@{
        'Configure-AppServiceSettings.generated.ps1' = (New-ConfigureArtifact -Sorted $sorted -SortedPerEnv $sortedPerEnv)
        'Seed-CustomerKeyVault.generated.ps1'        = (New-SeederArtifact    -Sorted $sorted)
        'appsettings.tokens.generated.md'            = (New-TokensDocArtifact -Data $Data -Sorted $sorted)
        'kv-secrets.generated.bicep'                 = (New-BicepArtifact     -Sorted $sorted)
    }
    return $outputs
}

function Write-Artifacts {
    param([string]$Dir, [System.Collections.Specialized.OrderedDictionary]$Outputs)

    if (-not (Test-Path -Path $Dir)) {
        [void](New-Item -ItemType Directory -Path $Dir -Force)
    }

    foreach ($name in $Outputs.Keys) {
        $path    = Join-Path -Path $Dir -ChildPath $name
        $content = $Outputs[$name]
        # Encoding: UTF-8 without BOM (deterministic across platforms).
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllText($path, $content, $utf8NoBom)
        Write-Info "  WROTE: $name ($([string]::Format('{0:N0}', $content.Length)) bytes)"
    }
}

function Compare-Artifacts {
    param([string]$Dir, [System.Collections.Specialized.OrderedDictionary]$Outputs)

    $drift = @()
    foreach ($name in $Outputs.Keys) {
        $path = Join-Path -Path $Dir -ChildPath $name
        $expected = $Outputs[$name]
        if (-not (Test-Path -Path $path)) {
            $drift += [pscustomobject]@{
                File   = $name
                Reason = 'MISSING on disk'
                Diff   = "<file absent>"
            }
            continue
        }
        # Read as raw bytes, decode as UTF-8, LF-normalize for comparison
        # (defends against CRLF sneaking in via git autocrlf on Windows).
        $actualRaw = Get-Content -Path $path -Raw -Encoding utf8
        $actual    = $actualRaw -replace "`r`n", "`n"

        if ($actual -ne $expected) {
            # Produce a compact line-level diff for operator triage.
            $expectedLines = $expected -split "`n"
            $actualLines   = $actual -split "`n"
            $diffLines = @()
            $max = [Math]::Max($expectedLines.Length, $actualLines.Length)
            $mismatchesShown = 0
            for ($i = 0; $i -lt $max; $i++) {
                $e = if ($i -lt $expectedLines.Length) { $expectedLines[$i] } else { '<eof>' }
                $a = if ($i -lt $actualLines.Length) { $actualLines[$i] } else { '<eof>' }
                if ($e -ne $a) {
                    $diffLines += ("  line $($i + 1):")
                    $diffLines += ("    -on-disk : $a")
                    $diffLines += ("    +expected: $e")
                    $mismatchesShown++
                    if ($mismatchesShown -ge 10) {
                        $diffLines += "  ... (further mismatches truncated at 10)"
                        break
                    }
                }
            }
            $drift += [pscustomobject]@{
                File   = $name
                Reason = 'DRIFT (bytes differ from manifest projection)'
                Diff   = ($diffLines -join "`n")
            }
        }
    }
    return $drift
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------

try {
    Import-YamlModule
    Write-Info ''
    Write-Info '=================================================================='
    Write-Info "  Canonical Secret-Catalog Generator v$script:GeneratorVersion"
    Write-Info '=================================================================='
    Write-Info "  Manifest:     $Manifest"
    Write-Info "  Output dir:   $OutputDir"
    $mode = if ($Verify) { 'VERIFY' } elseif ($DryRun) { 'DRY-RUN' } else { 'WRITE' }
    Write-Info "  Mode:         $mode"
    Write-Info '=================================================================='
    Write-Info ''

    $data = Read-Manifest -Path $Manifest
    Test-ManifestShape -Data $data
    Test-BindingNeverDeleteInvariant -Data $data
    Test-E3ClosedNoAppSettingsInvariant -Data $data
    Test-DevExceptionInvariant -Data $data
    Test-PerEnvSettingsShape -Data $data

    $perEnvCount = if ($data.ContainsKey('per_env_settings') -and $null -ne $data.per_env_settings) { $data.per_env_settings.Count } else { 0 }
    Write-Info "  Manifest shape:    OK ($($data.secrets.Count) secrets, $perEnvCount per_env_settings)"
    Write-Info "  BINDING never-delete guard: OK ($($script:BindingNeverDelete -join ', '))"
    Write-Info "  E-3-closed no-app-settings guard: OK"
    Write-Info "  Dev exception guard:        OK (spaarke-spekvcert)"
    Write-Info ''

    $outputs = New-AllArtifacts -Data $data

    if ($Verify) {
        $drift = @(Compare-Artifacts -Dir $OutputDir -Outputs $outputs)
        if ($drift.Count -eq 0) {
            Write-Info '  VERIFY: OK — generated/ is in sync with manifest.yaml.'
            Write-Info ''
            exit 0
        }
        Write-Fail ''
        Write-Fail '  VERIFY: DRIFT DETECTED'
        Write-Fail '  ------------------------------------------------------------'
        foreach ($d in $drift) {
            Write-Fail ("  {0} : {1}" -f $d.File, $d.Reason)
            if (-not [string]::IsNullOrWhiteSpace($d.Diff)) {
                Write-Fail $d.Diff
            }
            Write-Fail ''
        }
        Write-Fail '  Regenerate: pwsh scripts/canonical-secret-catalog/Invoke-CatalogGenerator.ps1'
        exit 2
    }

    if ($DryRun) {
        Write-Info '  DRY-RUN — planned outputs (no files written):'
        foreach ($name in $outputs.Keys) {
            $bytes = [System.Text.Encoding]::UTF8.GetByteCount($outputs[$name])
            Write-Info ("    - {0}  ({1:N0} bytes, {2} lines)" -f $name, $bytes, (($outputs[$name] -split "`n").Count))
        }
        Write-Info ''
        Write-Info '  Re-run without -DryRun to write files.'
        exit 0
    }

    Write-Info '  Writing generated artifacts:'
    Write-Artifacts -Dir $OutputDir -Outputs $outputs
    Write-Info ''
    Write-Info '  DONE. Re-run with -Verify to confirm determinism.'
    exit 0
}
catch {
    Write-Fail ''
    Write-Fail '  GENERATOR FAILED'
    Write-Fail '  ------------------------------------------------------------'
    Write-Fail "  $($_.Exception.Message)"
    if ($env:CATALOG_GENERATOR_TRACE -eq '1') {
        Write-Fail ''
        Write-Fail '  Stack:'
        Write-Fail "  $($_.ScriptStackTrace)"
    }
    Write-Fail ''
    exit 1
}
