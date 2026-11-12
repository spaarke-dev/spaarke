#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Locks the access markers with field-level security — sprk_accesspermission on every table that carries it,
    sprk_communication.sprk_isinternalonly and sprk_communicationthread.sprk_privacystate — so every business unit's
    default team READS them, and only the INTERNAL units' default teams and the BFF WRITE them (unified-access-control-r2
    task 191f, #2179 design §3.6, owner Q1 = a, 2026-11-11). Dry run by default; -Apply writes; -Verify checks.

.DESCRIPTION
    WHY. A flagged external user (systemuser.sprk_isexternal = true) who holds Write on a record could lower its marker —
    Restricted to Standard, internal-only to false, a thread's privacy to Open — and so widen who sees it (spike S1
    finding F1). Owner Q4 = a: Restricted is internal-only, and Secure is how a record is shared externally. Owner Q5 = a:
    the whole environment.

    WHO WRITES. Internal users keep editing the markers (forms, the Web API, the BFF routes that write as the caller):
    their unit's default team holds the writer profile. The BFF's app-only writers (create stamps, inheritance and
    reconciliation, the email pipelines) hold it as application users. The External Collaborators unit's default team
    (task 191d) holds the READER only, so a flagged user placed there (task 191e) reads a marker but cannot change it.
    Every writer and its identity: projects/unified-access-control-r2/notes/task-191f-access-marker-field-security.md.

    This is the named entry point; the ONE mechanism is scripts/Set-DocumentPointerFieldSecurity.ps1 -Target
    AccessMarkerColumns (the reader profile of task 133, its own writer profile "Spaarke Access Marker Writers", the
    same preconditions p1-p7 plus p8-p10, the same secure-then-grant-at-once steps and revert).

    LIVE ORDER (task 191i, owner go): Set-ExternalCollaboratorRole.ps1 -Apply (the External Collaborators unit and role)
    → Set-RecordCreatorPersonSchema.ps1 -Apply (the reader on the new unit's default team) → deploy the BFF whose caller
    creates no longer name a marker → THIS SCRIPT (dry run; every precondition OK) → -BffOmitsMarkersFromCallerCreates
    -Apply → -Verify. Agents run the dry run and -Verify only.

    IMPORTS. The SpaarkeMaster solution source does not carry IsSecured for these columns: run -Verify after every
    import into an environment where they are locked, and -Apply again if it reports a column unsecured. Do not export the
    lock into the source until provisioning runs this script: a profile's team members never travel with a solution, so
    a new environment would receive the columns secured with nobody reading them.

.PARAMETER EnvironmentUrl
    e.g. https://spaarkedev1.crm.dynamics.com

.PARAMETER BffApplicationIds
    Application (client) ids of the BFF's Dataverse application users. Per-customer input (D-13), never hard-coded.

.PARAMETER SolutionUniqueName
    The unmanaged solution carrying the profiles and the tables. Default SpaarkeMaster: on spaarkedev1 (2026-11-11)
    SpaarkeCore lacks sprk_agreement and carries sprk_todo / sprk_communicationthread without all subcomponents, so
    (p6) fails there; SpaarkeMaster carries all nine tables and the reader profile.

.PARAMETER ExternalBusinessUnitName
    The External Collaborators business unit's name. Default: businessUnit.name in -ExternalCollaboratorConfigPath.

.PARAMETER ExternalCollaboratorConfigPath
    The External Collaborators definition (task 191d). Default config/external-collaborator-role.json.

.PARAMETER BffOmitsMarkersFromCallerCreates
    The operator's confirmation, required with -Apply, that the BFF build whose caller-identity creates no longer NAME a
    marker is deployed. Until it is, the assistant's email.draft names the inherited Access Permission on every draft
    filed under a matter, project or work assignment, and CallerWriteAuthorization refuses a caller create that names a
    field-secured column, for internal users too (task 191f notes, finding R1).

.PARAMETER Apply
    Write mode. Without it the script never writes.

.PARAMETER Verify
    Read-only check with a pass/fail exit code (1 names each gap). Cannot be combined with -Apply.

.EXAMPLE
    .\Set-AccessMarkerFieldSecurity.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c
    Dry run: every precondition and step's state, the columns per table and the profile members it would add; zero writes.

.EXAMPLE
    .\Set-AccessMarkerFieldSecurity.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
        -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Verify

.NOTES
    unified-access-control-r2 task 191f. Live -Apply = task 191i, an owner-approved main-session step.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EnvironmentUrl,
    [Parameter(Mandatory)][string[]]$BffApplicationIds,
    [string]$SolutionUniqueName = 'SpaarkeMaster',
    [string]$ExternalBusinessUnitName,
    [string]$ExternalCollaboratorConfigPath,
    [switch]$BffOmitsMarkersFromCallerCreates,
    [switch]$Apply,
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$engine = Join-Path $PSScriptRoot 'Set-DocumentPointerFieldSecurity.ps1'
$arguments = @{
    EnvironmentUrl     = $EnvironmentUrl
    BffApplicationIds  = $BffApplicationIds
    Target             = 'AccessMarkerColumns'
    SolutionUniqueName = $SolutionUniqueName
}
if ($ExternalBusinessUnitName) { $arguments.ExternalBusinessUnitName = $ExternalBusinessUnitName }
if ($ExternalCollaboratorConfigPath) { $arguments.ExternalCollaboratorConfigPath = $ExternalCollaboratorConfigPath }
if ($BffOmitsMarkersFromCallerCreates) { $arguments.BffOmitsMarkersFromCallerCreates = $true }
if ($Apply) { $arguments.Apply = $true }
if ($Verify) { $arguments.Verify = $true }

& $engine @arguments
exit $LASTEXITCODE
