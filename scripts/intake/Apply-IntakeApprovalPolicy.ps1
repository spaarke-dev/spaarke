<#
.SYNOPSIS
    Creates or verifies the intake approval policy (sprk_policy + its published sprk_policyversion) of spaarke-intake-pipeline-r1
    task 017: a Decide-lane rule over the service request that holds while its intake stage is Awaiting approval.

.DESCRIPTION
    Source of truth: config/intake-approval.policies.json. This script is a thin wrapper: it runs scripts/Apply-DoLanePolicies.ps1
    with that definition, so the intake policy is linted, upserted, ended and read back by exactly the same code as every other
    Ontology policy (no second copy of the upsert rules). See that script's help for the full behaviour; in short:
      - the policy and its version have FIXED ids, so a re-run is an upsert and changes nothing when the rows are in sync;
      - the policy is created with sprk_enabled = No and the script never changes sprk_enabled afterwards (the owner turns it on);
      - a published version is immutable (FR-49, D-14): a changed body, plan or template is a NEW version entry in the definition;
      - the lint (kind 'intake-approval-policies') accepts only the approve-intake and reject-intake actions, no Next steps, and
        allows the work type to be empty (the Console shows the item under Other until task 019 adds an approval work type).
    The rule body, template and plan are also checked offline by IntakeApprovalPolicyDefinitionTests, which read the same file.

    RUNNING this against an environment is a data change and needs the owner's go. Run -WhatIf first; -Verify never writes.

.PARAMETER DataverseUrl
    Target environment. Defaults to $env:DATAVERSE_URL. Required.

.PARAMETER Verify
    READ-ONLY: reports the policy and version as ABSENT, IN SYNC or DRIFT.

.PARAMETER RequireRows
    With -Verify: an ABSENT row is a failure.

.PARAMETER CustomerBusinessUnit
    OPTIONAL. The customer's top business unit (id or name): created rows are owned by its restricted-content owner team (D-197).
    See scripts/Apply-DoLanePolicies.ps1.

.EXAMPLE
    pwsh scripts/intake/Apply-IntakeApprovalPolicy.ps1 -DataverseUrl https://<env>.crm.dynamics.com -Verify
    pwsh scripts/intake/Apply-IntakeApprovalPolicy.ps1 -DataverseUrl https://<env>.crm.dynamics.com -WhatIf
    pwsh scripts/intake/Apply-IntakeApprovalPolicy.ps1 -DataverseUrl https://<env>.crm.dynamics.com
#>
#Requires -Version 7.0
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$DataverseUrl = $env:DATAVERSE_URL,
    [switch]$Verify,
    [switch]$RequireRows,
    [string]$CustomerBusinessUnit
)

$ErrorActionPreference = 'Stop'

$applyScript = Join-Path $PSScriptRoot '..\Apply-DoLanePolicies.ps1'
$definition = Join-Path $PSScriptRoot '..\..\config\intake-approval.policies.json'
if (-not (Test-Path -LiteralPath $applyScript)) { throw "Not found: $applyScript" }
if (-not (Test-Path -LiteralPath $definition)) { throw "Not found: $definition" }

# Forward exactly what was passed (an explicitly passed but empty -CustomerBusinessUnit is refused by the applied script itself).
$forward = @{ DataverseUrl = $DataverseUrl; DefinitionPath = $definition }
if ($Verify) { $forward.Verify = $true }
if ($RequireRows) { $forward.RequireRows = $true }
if ($PSBoundParameters.ContainsKey('CustomerBusinessUnit')) { $forward.CustomerBusinessUnit = $CustomerBusinessUnit }
if ($WhatIfPreference) { $forward.WhatIf = $true }

& $applyScript @forward
if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
