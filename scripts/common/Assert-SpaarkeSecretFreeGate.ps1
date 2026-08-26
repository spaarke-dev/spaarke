# scripts/common/Assert-SpaarkeSecretFreeGate.ps1
# ---------------------------------------------------------------------------
# Shared marker pre-check gate for auth-v4-retired credentials (A38c).
#
# Row: customer-provisioning-orchestration-r1 punch row A38c
#   (projects/customer-provisioning-orchestration-r1/notes/auth-v4-integration-draft-punch-rows.md)
#
# §11 justification (root CLAUDE.md — Existing / Extension / Cost-of-doing-nothing):
#   Existing  — the A43 `-CutoverBffSettings` gate (scripts/ai-search/Deploy-AllIndexes.ps1:610-670)
#               established the FAIL-LOUD refusal shape (Write-Error + non-zero exit) for a
#               credential that a migration retired; A38a (punch row A38a) is the single source
#               of truth for the secret-free migration marker (KV tag `spaarke-secret-free-identity`
#               + `sprk_dataverseenvironment.sprk_credentialmode` state field).
#   Extension — this file is a THIN, single shared implementation of that same marker check +
#               FAIL-LOUD shape, reused by 3 operator scripts (Rotate-Secrets.ps1,
#               Seed-ProductionKeyVault.ps1, Provision-Customer.ps1) at 5 call sites. A shared
#               function avoids 5 independently-drifting copies of the same marker-detection logic.
#   Cost-of-doing-nothing — without this gate, every rotation run / operator seed / legacy-orchestrator
#               run against a secret-free environment silently re-mints `ServiceBus-ConnectionString`
#               (and `AiSearch--AdminKey` in the seed script) while reporting success — reversing the
#               ADR-028 Amendment A4 / Exception E-3 migration (closed 2026-08-24) without any signal.
#
# Marker convention (MUST match A38a exactly — do NOT invent a parallel convention):
#   - KV resource tag `spaarke-secret-free-identity=true` on the target vault (primary signal;
#     directly checkable with the `az` CLI these operator scripts already depend on) — canonical
#     key AND value confirmed against A38a's landed
#     `ISecretFreeMarkerApplier.VaultTagName = "spaarke-secret-free-identity"` +
#     `ArmSecretFreeMarkerApplier` (writes the tag with value "true"), reconciled 2026-08-26 (A38a
#     landed concurrently with this row's execution — see the coordination note in the A38c punch-
#     list annotation), OR
#   - `sprk_dataverseenvironment.sprk_credentialmode` == 'secret-free' (secondary signal; these
#     operator scripts have no existing Dataverse Web API client, so this signal is accepted as an
#     optional caller-supplied `-CredentialMode` parameter rather than queried in-line here — a caller
#     that has already resolved the Dataverse state field, e.g. a future L2-aware wrapper, can pass it
#     through). # TODO(A38a-followup): wire a live Dataverse `sprk_credentialmode` read here instead
#     of the `-CredentialMode` pass-through, once these operator scripts gain Dataverse connectivity.
#
# Fleet consistency (Model 2, §10.3): callers pass the SPECIFIC vault name for the resource being
# written (platform vault OR the one per-customer `kv-{customerId}-{secretsVer}` vault in scope for
# that call) — this function makes no assumption about vault topology; it checks exactly the vault
# it is given, so it behaves identically whether called once (Model 1) or N times across N
# per-customer vaults (Model 2).
# ---------------------------------------------------------------------------

function Test-SpaarkeSecretFreeMarker {
    <#
    .SYNOPSIS
    Returns $true when the target Key Vault (or its associated Dataverse environment) carries the
    auth-v4 secret-free migration marker (A38a convention).

    .DESCRIPTION
    Checks, in order:
      1. `-CredentialMode` pass-through (if the caller has already resolved
         `sprk_dataverseenvironment.sprk_credentialmode` and supplies it here).
      2. The KV resource tag `spaarke-secret-free-identity` on `-KeyVaultName` (via `az keyvault show`).

    A vault that cannot be reached (not found / no access) is treated as NOT migrated — the function
    falls through to $false rather than blocking on an unrelated Azure CLI/connectivity failure. This
    preserves current (pre-gate) behavior for any environment this gate cannot positively identify as
    secret-free, per the backwards-compatibility constraint (row A38c).

    .PARAMETER KeyVaultName
    The specific Key Vault to check — platform vault (Model 1) or ONE per-customer
    `kv-{customerId}-{secretsVer}` vault (Model 2). Never assume a shared topology.

    .PARAMETER CustomerId
    Optional. Present for Model 2 call sites; used only for diagnostic/log context in the caller —
    this function does not branch on it.

    .PARAMETER CredentialMode
    Optional pass-through of an already-resolved `sprk_dataverseenvironment.sprk_credentialmode`
    value. When it equals 'secret-free' (case-insensitive), short-circuits to $true without an
    `az` call.

    .OUTPUTS
    [bool]
    #>
    [OutputType([bool])]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$KeyVaultName,

        [Parameter()]
        [string]$CustomerId,

        [Parameter()]
        [string]$CredentialMode
    )

    # Scoped to THIS function only (does not leak into the caller's scope via dot-sourcing —
    # PowerShell scopes Set-StrictMode to the block it executes in; a function's own scope is
    # popped on return, unlike a top-level statement in a dot-sourced file, which runs IN the
    # caller's scope and would silently upgrade strict-mode enforcement for the rest of that
    # caller's script — verified empirically during A38c code review, 2026-08-26).
    Set-StrictMode -Version Latest

    if ($CredentialMode -and $CredentialMode -ieq 'secret-free') {
        return $true
    }

    # Primary signal: KV resource tag `spaarke-secret-free-identity=true` — key AND value confirmed
    # against A38a's landed `ISecretFreeMarkerApplier`/`ArmSecretFreeMarkerApplier` (writes exactly
    # this key with value "true"; reconciled 2026-08-26).
    # NOTE: the query argument's embedded double-quotes MUST be escaped with a single PowerShell
    # backtick (`"), NOT backslash+backtick — a backslash there becomes a LITERAL character in the
    # string PowerShell hands to the `az` process, corrupting the JMESPath (`tags.\"...\"` is not
    # valid JMESPath) and making this primary signal silently never match in real Azure CLI usage
    # (caught + fixed during A38c code review, 2026-08-26 — verified via direct string-length /
    # char-code inspection, since the Pester suite mocks `az` and cannot catch a CLI-argument bug).
    $tagValue = az keyvault show --name $KeyVaultName --query "tags.`"spaarke-secret-free-identity`"" -o tsv 2>$null
    if ($LASTEXITCODE -ne 0) {
        # Vault not found / no access / az not logged in — do not block on an unrelated failure;
        # fall through to current (pre-gate) behavior.
        return $false
    }

    return ($tagValue -and $tagValue.Trim() -ieq 'true')
}

function Assert-SpaarkeSecretFreeGateNotTripped {
    <#
    .SYNOPSIS
    FAIL-LOUD refusal gate (A38c) — mirrors the A43 `-CutoverBffSettings` shape
    (scripts/ai-search/Deploy-AllIndexes.ps1:610-670): Write-Error naming the marker + linking the
    A38a omit contract, then a hard `exit 7` (distinct from A43's exit 6/8/9 range) so the refusal
    cannot be swallowed by a try/catch in the calling script.

    .DESCRIPTION
    Calls Test-SpaarkeSecretFreeMarker. If tripped, refuses: Write-Error with an actionable message
    + `exit 7`. If not tripped, returns silently (no output) — the caller's current fallback
    behavior runs UNCHANGED (backwards compatibility for pre-migration environments).

    Gate ONLY the auth-v4-retired credentials (`ServiceBus-ConnectionString`, `AiSearch--AdminKey`).
    Do NOT call this for `Dataverse-ClientSecret` or `BFF-API-ClientSecret` rotation/seed paths —
    `BFF-API-ClientSecret` is already a retired no-op / removed seed (auth-v4 task 033);
    `Dataverse-ClientSecret` remains protected under narrow Path A through 2026-11-23 (§6.5).

    .PARAMETER SecretName
    The retired credential's canonical KV secret name (e.g. "ServiceBus-ConnectionString",
    "AiSearch--AdminKey") — named in the refusal message.

    .PARAMETER KeyVaultName
    See Test-SpaarkeSecretFreeMarker.

    .PARAMETER CustomerId
    See Test-SpaarkeSecretFreeMarker.

    .PARAMETER CredentialMode
    See Test-SpaarkeSecretFreeMarker.

    .EXAMPLE
    Assert-SpaarkeSecretFreeGateNotTripped -SecretName 'ServiceBus-ConnectionString' -KeyVaultName $vaultName
    #>
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$SecretName,

        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$KeyVaultName,

        [Parameter()]
        [string]$CustomerId,

        [Parameter()]
        [string]$CredentialMode
    )

    # Scoped to this function only — see the matching note in Test-SpaarkeSecretFreeMarker above.
    Set-StrictMode -Version Latest

    $tripped = Test-SpaarkeSecretFreeMarker -KeyVaultName $KeyVaultName -CustomerId $CustomerId -CredentialMode $CredentialMode
    if (-not $tripped) {
        return
    }

    $customerNote = if ($CustomerId) { " (customer: $CustomerId)" } else { "" }
    $msg = @"
REFUSED: write to '$SecretName' blocked at Key Vault '$KeyVaultName'$customerNote.

This environment carries the auth-v4 secret-free migration marker (KV tag
'spaarke-secret-free-identity' and/or sprk_dataverseenvironment.sprk_credentialmode=secret-free).
'$SecretName' is an auth-v4-retired credential (ADR-028 Amendment A4 / Exception E-3, closed
2026-08-24) — re-minting it here would silently reverse that migration while this run reports
success (the exact §10.5 trap class this gate closes).

See the A38a omit contract:
  .claude/constraints/auth.md  (section: KV credential lifecycle)
  projects/customer-provisioning-orchestration-r1/notes/auth-v4-integration-draft-punch-rows.md
    (row A38a)

If this environment has NOT actually migrated, verify the KV tag / Dataverse state field before
retrying — do not remove this gate to "fix" the refusal.
"@

    Write-Error $msg
    exit 7
}
