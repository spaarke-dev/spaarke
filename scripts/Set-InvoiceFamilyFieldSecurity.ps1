#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Locks the invoice family's core-record lookups and invoice links with field-level security, so ONLY the BFF
    application user can set, change or clear them, while every user still reads them (#2799, unified-access-control-r2).
    Dry run by default; -Apply writes; -Verify checks.

.DESCRIPTION
    WHY. What an invoice-family row is filed under (its matter, project or work assignment) and which invoice it belongs to
    decide who can see it and which rules apply to it. The BFF checks every write of these columns AS THE PERSON and writes
    them AS THE APPLICATION (#2799 chunks 1-3, InvoiceFamilyLockedWrite). Without the lock a user with Write on a row could
    still re-file it through a form or the Web API and skip those checks.

    THE COLUMNS (24, six tables): scripts/common/InvoiceFamilyLockedColumns.psd1 — the one list, pinned both ways to the
    BFF's InvoiceFamilyLockedWrite.LockedColumns by tests/Spaarke.ArchTests/InvoiceFamilyFieldLockGuardTests.cs.

    This is the named entry point; the ONE mechanism is scripts/Set-DocumentPointerFieldSecurity.ps1 -Target
    InvoiceFamilyLinks: task 133's two profiles ("Spaarke BFF-Managed Field Readers" on every business unit's default team,
    "Spaarke BFF-Managed Field Writers" on the BFF application users only), the same preconditions, the same
    secure-then-grant-at-once steps and revert. Its (p4a) scans every DEPLOYED web resource with the table-scoped detector
    scripts/common/Find-ClientInvoiceFamilyWrite.ps1 and refuses -Apply while any still writes a locked column on a family
    table; its (p4b) needs -BffWritesInvoiceFamilyLinksAsApp.

    LIVE ORDER: deploy the BFF with #2799 chunks 1-3 → rebuild and deploy every web resource (p4a) names → THIS SCRIPT (dry
    run; every precondition OK) → owner approval → -BffWritesInvoiceFamilyLinksAsApp -Apply → -Verify. Agents run the dry
    run and -Verify only. After every later SpaarkeMaster import, run -Verify.

.PARAMETER EnvironmentUrl
    e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER BffApplicationIds
    Application (client) ids of the BFF's Dataverse application users. Per-customer input (D-13), never hard-coded.

.PARAMETER SolutionUniqueName
    The unmanaged solution carrying the profiles and the family tables. Default SpaarkeMaster.

.PARAMETER BffWritesInvoiceFamilyLinksAsApp
    The operator's confirmation that the BFF build with #2799 chunks 1-3 is deployed. Required with -Apply.

.PARAMETER Apply
    Write mode. Without it the script never writes.

.PARAMETER Verify
    Read-only check with a pass/fail exit code (1 names each gap). Cannot be combined with -Apply.

.EXAMPLE
    .\Set-InvoiceFamilyFieldSecurity.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c
    Dry run: every precondition (including the deployed-web-resource scan) and step's state; zero writes.

.EXAMPLE
    .\Set-InvoiceFamilyFieldSecurity.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Verify

.NOTES
    #2799 (unified-access-control-r2), task note projects/unified-access-control-r2/notes/task-2799-invoice-family-field-lock.md
    §5. Live -Apply = an owner-approved main-session step. Auth: the operator's own az CLI identity. No secrets.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EnvironmentUrl,
    [Parameter(Mandatory)][string[]]$BffApplicationIds,
    [string]$SolutionUniqueName = 'SpaarkeMaster',
    [switch]$BffWritesInvoiceFamilyLinksAsApp,
    [switch]$Apply,
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$engine = Join-Path $PSScriptRoot 'Set-DocumentPointerFieldSecurity.ps1'
$arguments = @{
    EnvironmentUrl     = $EnvironmentUrl
    BffApplicationIds  = $BffApplicationIds
    Target             = 'InvoiceFamilyLinks'
    SolutionUniqueName = $SolutionUniqueName
}
if ($BffWritesInvoiceFamilyLinksAsApp) { $arguments.BffWritesInvoiceFamilyLinksAsApp = $true }
if ($Apply) { $arguments.Apply = $true }
if ($Verify) { $arguments.Verify = $true }

& $engine @arguments
exit $LASTEXITCODE
