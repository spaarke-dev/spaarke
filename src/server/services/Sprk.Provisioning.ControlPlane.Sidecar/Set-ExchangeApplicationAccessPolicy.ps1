<#
.SYNOPSIS
    Applies + verifies Exchange Online ApplicationAccessPolicy entries for the
    BFF app-registration + UAMI using action-and-verify semantics (T4).
    Re-run safe.

    LINUX-SIDECAR AMENDED COPY of scripts/Set-ExchangeApplicationAccessPolicy.ps1.
    Diff vs the repo-root original (see .DIFF section below): ~15 lines added,
    to accept -Certificate as an X509Certificate2 object in addition to the
    original -CertificateThumbprint mode. The get-before-set idempotency
    structure (Get-ApplicationAccessPolicy -> conditional New- -> re-verify)
    is BYTE-IDENTICAL to the original. T4 action-and-verify semantics are
    preserved unchanged.

.DIFF
    Only the connect-block accepts a new auth mode. Everything else — the
    JSON output envelope, the get-before-set body, the exit-code convention,
    the 2+-present verify-only branch — is unchanged from the repo-root
    original (scripts/Set-ExchangeApplicationAccessPolicy.ps1).

    Amendment rationale: Linux containers have no Windows certificate store,
    so the original script's -CertificateThumbprint mode cannot resolve. The
    sidecar's Listener.ps1 fetches the PFX from platform Key Vault via the
    App Service MSI (same UAMI as the main site) at call time and passes an
    X509Certificate2 object here via -Certificate. -CertificateThumbprint is
    retained as a fallback (still works on Windows dev boxes running the
    script directly against Windows cert store — dev-loop parity).

.DESCRIPTION
    Implements the T4 silent-fail trap post-condition (spec.md FR-33 /
    customer-provisioning-orchestration-r1 task 073, H14a sub-handler):

      1. Connect to Exchange Online via app-only certificate auth (either
         -Certificate X509Certificate2 (sidecar path) or
         -CertificateThumbprint (Windows dev-loop parity path)).
      2. Get-ApplicationAccessPolicy, filtered to the 2 expected AppIds
         (BFF app-reg + UAMI).
      3. If 0 or 1 policies exist for the expected set, create the missing
         one(s) via New-ApplicationAccessPolicy -AccessRight RestrictAccess.
      4. Re-query. If the observed AppId set now equals the expected set,
         report "Applied". If it does NOT (drift — e.g. a stale/incorrect
         policy pre-exists for one of the AppIds), report "Drift" WITHOUT
         creating or modifying anything further — NO SILENT OVERWRITE.
      5. If 2+ policies already existed BEFORE step 3 and their AppIds do not
         match the expected set exactly, report "Drift" immediately (skip
         creation entirely — 2+ existing entries means an operator or a prior
         run already made a decision here; H14a's job is to VERIFY, not
         reconcile arbitrary pre-existing state).

    OUTPUT CONTRACT: emits exactly one stdout line prefixed
    "SPAARKE-H14A-RESULT-JSON:" followed by a compact JSON object:
        { "outcome": "Applied" | "Drift" | "Failure",
          "createdCount": <int>,
          "expectedAppIds": [...],
          "observedAppIds": [...],
          "diagnostic": "<string>" }
    The calling Listener.ps1 (sidecar) parses this single line and maps it
    onto the sidecar's HTTP response envelope (Success | AlreadyCompliant |
    Drift | Failure — see Listener.ps1 header for the mapping). Exit code is
    0 for BOTH conclusive Applied/Drift outcomes (the OUTCOME field, not exit
    code, carries the domain result) and non-zero ONLY for a genuine
    script/connect failure that prevented a conclusive result.

.PARAMETER TenantId
    Entra ID tenant ID. MANDATORY (§4D I1 — no hardcoded default tenant).

.PARAMETER ExpectedAppIds
    Comma-separated list of exactly 2 AppId GUIDs: BFF app-registration id +
    UAMI client id.

.PARAMETER PolicyScopeGroupId
    Mail-enabled security group id scoping the ApplicationAccessPolicy
    (New-ApplicationAccessPolicy -PolicyScopeGroupId). Customer-tenant
    specific; supplied as a run parameter by the H14a sub-handler.

.PARAMETER DescriptionPrefix
    Description prefix applied to newly created policies (greppability in
    Get-ApplicationAccessPolicy output). Full description is
    "{prefix}-{appId}".

.PARAMETER ExchangeAppId
    App-only auth: the Entra app registration id used to connect to Exchange
    Online. Defaults to the value of the EXCHANGE_CONNECT_APP_ID environment
    variable (App Service setting) so this parameter rarely needs to be
    passed explicitly.

.PARAMETER Certificate
    App-only auth (sidecar path): an X509Certificate2 object with the private
    key material. Preferred over -CertificateThumbprint on Linux (no Windows
    cert store). Mutually exclusive with -CertificateThumbprint — pass ONE.

.PARAMETER CertificateThumbprint
    App-only auth (Windows dev-loop parity path): certificate thumbprint
    registered on -ExchangeAppId and installed in LocalMachine\My or
    CurrentUser\My. Defaults to the value of the EXCHANGE_CONNECT_CERT_THUMBPRINT
    environment variable. Mutually exclusive with -Certificate — pass ONE.

.PARAMETER ExchangeOrganization
    App-only auth: the target tenant's initial domain (*.onmicrosoft.com) or
    -TenantId itself is usually sufficient for -Organization; kept as a
    separate parameter because Connect-ExchangeOnline's -Organization
    parameter historically prefers the onmicrosoft.com domain over the GUID
    tenant id in some module versions.

.EXAMPLE
    # Sidecar path (X509 object from KV fetch):
    ./Set-ExchangeApplicationAccessPolicy.ps1 -TenantId $tenantId `
        -ExpectedAppIds "$bffAppId,$uamiClientId" `
        -PolicyScopeGroupId $groupId `
        -Certificate $certObject `
        -ExchangeAppId $exchAppId `
        -DescriptionPrefix "Spaarke-Provisioning-AppAccessPolicy"

.EXAMPLE
    # Windows dev-loop parity (thumbprint from local cert store):
    ./Set-ExchangeApplicationAccessPolicy.ps1 -TenantId $tenantId `
        -ExpectedAppIds "$bffAppId,$uamiClientId" `
        -PolicyScopeGroupId $groupId `
        -CertificateThumbprint $thumbprint `
        -ExchangeAppId $exchAppId `
        -DescriptionPrefix "Spaarke-Provisioning-AppAccessPolicy"
#>

[CmdletBinding(DefaultParameterSetName = "Certificate")]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$TenantId,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$ExpectedAppIds,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$PolicyScopeGroupId,

    [Parameter(Mandatory = $false)]
    [string]$DescriptionPrefix = "Spaarke-Provisioning-AppAccessPolicy",

    [Parameter(Mandatory = $false)]
    [string]$ExchangeAppId = $env:EXCHANGE_CONNECT_APP_ID,

    # Sidecar path (Linux): X509Certificate2 object constructed from PFX bytes
    # fetched via App Service MSI + Key Vault by the listener.
    [Parameter(Mandatory = $true, ParameterSetName = "Certificate")]
    [System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate,

    # Windows dev-loop parity path: thumbprint referencing the local cert store.
    [Parameter(Mandatory = $true, ParameterSetName = "Thumbprint")]
    [string]$CertificateThumbprint,

    [Parameter(Mandatory = $false)]
    [string]$ExchangeOrganization = $TenantId
)

$ErrorActionPreference = "Stop"

function Write-ResultJson {
    param(
        [Parameter(Mandatory = $true)][string]$Outcome,
        [int]$CreatedCount = 0,
        [string[]]$ExpectedAppIdsList = @(),
        [string[]]$ObservedAppIdsList = @(),
        [string]$Diagnostic = ""
    )
    $result = [ordered]@{
        outcome        = $Outcome
        createdCount   = $CreatedCount
        expectedAppIds = @($ExpectedAppIdsList)
        observedAppIds = @($ObservedAppIdsList)
        diagnostic     = $Diagnostic
    }
    $json = $result | ConvertTo-Json -Compress
    Write-Output "SPAARKE-H14A-RESULT-JSON:$json"
}

$expected = @($ExpectedAppIds -split "," | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne "" })
if ($expected.Count -ne 2) {
    Write-ResultJson -Outcome "Failure" -Diagnostic "ExpectedAppIds must contain exactly 2 GUIDs (got $($expected.Count)): '$ExpectedAppIds'."
    exit 0
}

if (-not $ExchangeAppId) {
    Write-ResultJson -Outcome "Failure" -Diagnostic "Exchange app-only auth is not configured — EXCHANGE_CONNECT_APP_ID (or -ExchangeAppId) must be set. See H4 KV secret 'Exchange-Connect-CertThumbprint' provisioning."
    exit 0
}

# Parameter-set-driven credential validation. -Certificate (sidecar path) and
# -CertificateThumbprint (Windows dev-loop parity) are mutually exclusive; the
# CmdletBinding DefaultParameterSetName + [Parameter(ParameterSetName=...)]
# attributes on the params above enforce this at parse time.
if ($PSCmdlet.ParameterSetName -eq "Thumbprint" -and -not $CertificateThumbprint) {
    Write-ResultJson -Outcome "Failure" -Diagnostic "Thumbprint parameter set selected but -CertificateThumbprint is empty. Provide a thumbprint (Windows path) or use -Certificate (Linux/sidecar path)."
    exit 0
}

try {
    Import-Module ExchangeOnlineManagement -ErrorAction Stop
}
catch {
    Write-ResultJson -Outcome "Failure" -Diagnostic "ExchangeOnlineManagement module not available: $($_.Exception.Message)"
    exit 1
}

try {
    # SIDECAR AMENDMENT: pass -Certificate <X509Certificate2> when we have the
    # cert object (sidecar path from KV fetch), else fall back to
    # -CertificateThumbprint (Windows dev-loop parity). Same Connect-ExchangeOnline
    # cmdlet, same app-only auth flow — only the credential-material shape
    # differs.
    if ($PSCmdlet.ParameterSetName -eq "Certificate") {
        Connect-ExchangeOnline `
            -AppId $ExchangeAppId `
            -Certificate $Certificate `
            -Organization $ExchangeOrganization `
            -ShowBanner:$false `
            -ErrorAction Stop
    }
    else {
        Connect-ExchangeOnline `
            -AppId $ExchangeAppId `
            -CertificateThumbprint $CertificateThumbprint `
            -Organization $ExchangeOrganization `
            -ShowBanner:$false `
            -ErrorAction Stop
    }
}
catch {
    Write-ResultJson -Outcome "Failure" -Diagnostic "Connect-ExchangeOnline failed: $($_.Exception.Message)"
    exit 1
}

try {
    # (1) List existing policies for the expected AppId set.
    $allPolicies = @(Get-ApplicationAccessPolicy -ErrorAction Stop)
    $existingForExpected = @($allPolicies | Where-Object { $expected -contains $_.AppId })
    $existingAppIds = @($existingForExpected | Select-Object -ExpandProperty AppId -Unique)

    if ($existingAppIds.Count -ge 2) {
        # 2+ already present BEFORE any action this run — VERIFY ONLY, per T4:
        # no silent overwrite. Compare sets (order-independent).
        $expectedSorted = @($expected | Sort-Object)
        $observedSorted = @($existingAppIds | Sort-Object)
        $setsMatch = ($expectedSorted.Count -eq $observedSorted.Count) -and
                     (-not (Compare-Object $expectedSorted $observedSorted))

        if ($setsMatch) {
            Write-ResultJson -Outcome "Applied" -CreatedCount 0 -ExpectedAppIdsList $expected -ObservedAppIdsList $existingAppIds `
                -Diagnostic "Both expected policies already present; verified, no changes made."
        }
        else {
            Write-ResultJson -Outcome "Drift" -ExpectedAppIdsList $expected -ObservedAppIdsList $existingAppIds `
                -Diagnostic "T4 drift: 2+ ApplicationAccessPolicy entries exist for the expected AppId set but observed AppIds do not match expected. No policy was created or modified."
        }
    }
    else {
        # (2) 0 or 1 present — create the missing ones.
        $missing = @($expected | Where-Object { $existingAppIds -notcontains $_ })
        $createdCount = 0
        foreach ($appId in $missing) {
            New-ApplicationAccessPolicy `
                -AccessRight RestrictAccess `
                -AppId $appId `
                -PolicyScopeGroupId $PolicyScopeGroupId `
                -Description "$DescriptionPrefix-$appId" `
                -ErrorAction Stop | Out-Null
            $createdCount++
        }

        # (3) Re-verify post-create.
        $reVerifyPolicies = @(Get-ApplicationAccessPolicy -ErrorAction Stop)
        $reVerifyForExpected = @($reVerifyPolicies | Where-Object { $expected -contains $_.AppId })
        $reVerifyAppIds = @($reVerifyForExpected | Select-Object -ExpandProperty AppId -Unique)

        $expectedSorted = @($expected | Sort-Object)
        $observedSorted = @($reVerifyAppIds | Sort-Object)
        $setsMatch = ($expectedSorted.Count -eq $observedSorted.Count) -and
                     (-not (Compare-Object $expectedSorted $observedSorted))

        if ($setsMatch) {
            Write-ResultJson -Outcome "Applied" -CreatedCount $createdCount -ExpectedAppIdsList $expected -ObservedAppIdsList $reVerifyAppIds `
                -Diagnostic "Created $createdCount missing polic$(if ($createdCount -eq 1) { 'y' } else { 'ies' }); post-create verification confirms both expected AppIds present."
        }
        else {
            Write-ResultJson -Outcome "Drift" -ExpectedAppIdsList $expected -ObservedAppIdsList $reVerifyAppIds `
                -Diagnostic "Created $createdCount missing polic$(if ($createdCount -eq 1) { 'y' } else { 'ies' }) but post-create verification STILL does not match the expected set — possible EXO replication delay or a concurrent external change."
        }
    }
}
catch {
    Write-ResultJson -Outcome "Failure" -Diagnostic "Get/New-ApplicationAccessPolicy failed: $($_.Exception.Message)"
    Disconnect-ExchangeOnline -Confirm:$false -ErrorAction SilentlyContinue | Out-Null
    exit 1
}

Disconnect-ExchangeOnline -Confirm:$false -ErrorAction SilentlyContinue | Out-Null
exit 0
