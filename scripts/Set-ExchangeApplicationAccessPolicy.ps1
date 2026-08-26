<#
.SYNOPSIS
    Applies + verifies Exchange Online ApplicationAccessPolicy entries for the
    BFF app-registration + UAMI using action-and-verify semantics (T4).
    Re-run safe.

.DESCRIPTION
    Implements the T4 silent-fail trap post-condition (spec.md FR-33 /
    customer-provisioning-orchestration-r1 task 073, H14a sub-handler):

      1. Connect to Exchange Online via app-only certificate auth.
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
    The calling C# applier (ExchangePolicyScriptApplier.cs) parses this single
    line. Exit code is 0 for BOTH conclusive Applied/Drift outcomes (the
    OUTCOME field, not exit code, carries the domain result) and non-zero ONLY
    for a genuine script/connect failure that prevented a conclusive result.

    Prerequisites:
      - ExchangeOnlineManagement PowerShell module installed on the L2 host.
      - An Entra app registration with the Exchange.ManageAsApp application
        permission (admin-consented) + a certificate credential registered on
        it, used for -AppId/-CertificateThumbprint app-only Connect-ExchangeOnline.
      - -TenantId REQUIRED (v3.3 tenant-isolation invariant I1 per r1
        design.md §4D / FR-28).

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

.PARAMETER ExchangeCertificateThumbprint
    App-only auth: certificate thumbprint registered on
    -ExchangeAppId. Defaults to the value of the
    EXCHANGE_CONNECT_CERT_THUMBPRINT environment variable.

.PARAMETER ExchangeOrganization
    App-only auth: the target tenant's initial domain (*.onmicrosoft.com) or
    -TenantId itself is usually sufficient for -Organization; kept as a
    separate parameter because Connect-ExchangeOnline's -Organization
    parameter historically prefers the onmicrosoft.com domain over the GUID
    tenant id in some module versions.

.EXAMPLE
    ./Set-ExchangeApplicationAccessPolicy.ps1 -TenantId $tenantId `
        -ExpectedAppIds "$bffAppId,$uamiClientId" `
        -PolicyScopeGroupId $groupId -DescriptionPrefix "Spaarke-Provisioning-AppAccessPolicy"
#>

[CmdletBinding()]
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

    [Parameter(Mandatory = $false)]
    [string]$ExchangeCertificateThumbprint = $env:EXCHANGE_CONNECT_CERT_THUMBPRINT,

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

if (-not $ExchangeAppId -or -not $ExchangeCertificateThumbprint) {
    Write-ResultJson -Outcome "Failure" -Diagnostic "Exchange app-only auth is not configured — EXCHANGE_CONNECT_APP_ID / EXCHANGE_CONNECT_CERT_THUMBPRINT (or -ExchangeAppId / -ExchangeCertificateThumbprint) must be set. See H4 KV secret 'Exchange-Connect-CertThumbprint' provisioning."
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
    Connect-ExchangeOnline `
        -AppId $ExchangeAppId `
        -CertificateThumbprint $ExchangeCertificateThumbprint `
        -Organization $ExchangeOrganization `
        -ShowBanner:$false `
        -ErrorAction Stop
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
