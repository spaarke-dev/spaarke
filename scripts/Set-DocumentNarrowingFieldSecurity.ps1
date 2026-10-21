#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Locks the two links the external document narrowing walks — sprk_document.sprk_relatedcommunication and
    sprk_document.sprk_parentdocument — with field-level security, so ONLY the BFF can set, change or clear them, while
    every user still reads them (unified-access-control-r2 task 188, D7 §6.3 "remaining hole", #2126). Dry run by
    default; -Apply writes; -Verify checks.

.DESCRIPTION
    WHY. Task 188's predicate (Infrastructure/ExternalAccess/ExternalDocumentNarrowing.cs) hides a document from partners
    when it comes from an internal-only or non-Open email (its own related communication or, on a bounded parent walk,
    its ancestor's) or when an ancestor is Restricted. The rule is derived at read time from these two links. On
    spaarkedev1 (2026-10-20, read-only) both columns are on 0 of 5 sprk_document forms and 0 of 11 views, but both are
    IsValidForUpdate and unsecured (0 fieldpermissions rows): any user with Write on a document could clear either one
    through the Web API and make an internal-only email's attachments visible to partners.

    Every writer of either column is the BFF, app-only, with a server-known value (task 188 notes, "Writers"): the email
    pipelines, the cross-path link, the Office worker and the email attachment processor. No client writes them.

    This is the named entry point; the ONE mechanism is scripts/Set-DocumentPointerFieldSecurity.ps1 -Target
    DocumentNarrowingLinks (the same two profiles of task 133, the same preconditions p1-p7, the same
    secure-then-grant-at-once steps and revert).

    TRADE-OFF (read the task 188 notes before -Apply). A secured column reads EMPTY for a user outside the reader
    profile. The assistant's dataverse.* tools read as the user, so for such a user the predicate would see "no link"
    and SHOW the document. (p2) therefore must pass (every business unit's default team on the reader profile), and a
    business unit created later needs Set-RecordCreatorPersonSchema.ps1 -Apply re-run, as for every other locked column.

    LIVE ORDER: deploy the BFF carrying task 188 → THIS SCRIPT (dry run; every precondition OK) → owner approval → -Apply
    → -Verify. Agents run the dry run only.

.PARAMETER EnvironmentUrl
    e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER BffApplicationIds
    Application (client) ids of the BFF's Dataverse application users. Per-customer input (D-13), never hard-coded.

.PARAMETER SolutionUniqueName
    The unmanaged solution carrying the profiles and sprk_document. Default SpaarkeCore.

.PARAMETER Apply
    Write mode. Without it the script never writes.

.PARAMETER Verify
    Read-only check with a pass/fail exit code (1 names each gap). Cannot be combined with -Apply.

.EXAMPLE
    .\Set-DocumentNarrowingFieldSecurity.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c
    Dry run: every precondition and step's state; zero writes.

.EXAMPLE
    .\Set-DocumentNarrowingFieldSecurity.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Apply

.NOTES
    unified-access-control-r2 task 188 (#2126). Live -Apply = an owner-approved main-session step (task 188 notes).
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EnvironmentUrl,
    [Parameter(Mandatory)][string[]]$BffApplicationIds,
    [string]$SolutionUniqueName = 'SpaarkeCore',
    [switch]$Apply,
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$engine = Join-Path $PSScriptRoot 'Set-DocumentPointerFieldSecurity.ps1'
$arguments = @{
    EnvironmentUrl     = $EnvironmentUrl
    BffApplicationIds  = $BffApplicationIds
    Target             = 'DocumentNarrowingLinks'
    SolutionUniqueName = $SolutionUniqueName
}
if ($Apply) { $arguments.Apply = $true }
if ($Verify) { $arguments.Verify = $true }

& $engine @arguments
exit $LASTEXITCODE
