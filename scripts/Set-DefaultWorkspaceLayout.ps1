<#
.SYNOPSIS
  Switch which SYSTEM sprk_workspacelayout row is the Console's default landing workspace: the Worklist or the Daily Briefing.

.DESCRIPTION
  spaarke-ontology-platform-r1 task 064 (BR-4). The Console opens on the BFF's default layout
  (WorkspaceLayoutService.GetDefaultLayoutAsync step 2: the system row with sprk_isdefault = true, lowest sprk_sortorder first).
  Which tab a user lands on is therefore DATA, not code: this script is the cutover switch AND the no-deploy revert.

    -To Worklist        the worklist (Decide above Do, narrative on) is the landing tab      (the cutover)
    -To 'Daily Briefing' the old Briefing is the landing tab again                           (the revert)

  Both rows and both sections stay installed (`worklist` and `daily-briefing` are registered in the LegalWorkspace section registry),
  so nothing is redeployed either way.

  What it writes: ONLY sprk_isdefault on system sprk_workspacelayout rows (a Boolean). The target is set to true FIRST, then every
  other system row that is still default is set to false, so there is never a moment with no default (and if the run stops between the
  two writes the Daily Briefing, sortOrder 0, still wins the BFF's ordering until the second write lands). It reads the result back and
  fails unless exactly one system row is default and it is the target. Idempotent: running it twice is a no-change.

  User-owned defaults (a person's own "make this my default", BFF step 1) are not touched and still win for that person.

  What a user sees after the switch (WorkspacePane default-install effect, useWorkspaceLayouts cache):
    - The first reload in the same browser tab still opens the cached Daily Briefing (the layout list is cached in sessionStorage and
      the default is installed once per mount, before the revalidation lands); the next reload opens the Worklist.
    - A restored or pinned Daily Briefing tab does NOT stop the Worklist being opened: the install only skips when the new default
      itself is already open or pinned. The Briefing tab stays open beside it until the user closes it.

  -WhatIf performs the reads and prints every write it would make. Nothing is written.

.PARAMETER To
  'Worklist' or 'Daily Briefing'.

.PARAMETER DataverseUrl
  Default: https://spaarkedev1.crm.dynamics.com

.EXAMPLE
  ./scripts/Set-DefaultWorkspaceLayout.ps1 -To Worklist -WhatIf

.EXAMPLE
  ./scripts/Set-DefaultWorkspaceLayout.ps1 -To 'Daily Briefing'   # revert, no deploy
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
  [Parameter(Mandatory = $true)][ValidateSet('Worklist', 'Daily Briefing')][string]$To,
  [string]$DataverseUrl = 'https://spaarkedev1.crm.dynamics.com'
)
$ErrorActionPreference = 'Stop'
$api = "$DataverseUrl/api/data/v9.2"
$token = az account get-access-token --resource $DataverseUrl --query accessToken -o tsv
if (-not $token) { throw 'No Dataverse token: run az login.' }
$headers = @{
  Authorization      = "Bearer $token"
  'Content-Type'     = 'application/json'
  'OData-MaxVersion' = '4.0'
  'OData-Version'    = '4.0'
}

function Read-SystemLayout {
  (Invoke-RestMethod -Method Get -Headers $headers -Uri "$api/sprk_workspacelayouts?`$filter=sprk_issystem eq true and statecode eq 0&`$select=sprk_workspacelayoutid,sprk_name,sprk_isdefault,sprk_sortorder").value
}

$rows = @(Read-SystemLayout)
$target = @($rows | Where-Object { $_.sprk_name -eq $To })
if ($target.Count -ne 1) {
  throw "Expected exactly one active system layout named '$To', found $($target.Count). Seed it first (scripts/seed-worklist-workspace-layout.ps1 for the Worklist). Nothing was changed."
}
$target = $target[0]
$others = @($rows | Where-Object { $_.sprk_workspacelayoutid -ne $target.sprk_workspacelayoutid -and $_.sprk_isdefault })

Write-Host "Target: '$To' ($($target.sprk_workspacelayoutid)), currently default: $($target.sprk_isdefault)"
foreach ($o in $others) { Write-Host "Currently also default: '$($o.sprk_name)' ($($o.sprk_workspacelayoutid))" }

# Target first, then the others (see DESCRIPTION).
$changes = @()
if (-not $target.sprk_isdefault) { $changes += [pscustomobject]@{ Id = $target.sprk_workspacelayoutid; Name = $target.sprk_name; Value = $true } }
foreach ($o in $others) { $changes += [pscustomobject]@{ Id = $o.sprk_workspacelayoutid; Name = $o.sprk_name; Value = $false } }
if ($target.sprk_isdefault -and $others.Count -eq 0) {
  Write-Host "NO CHANGE: '$To' is already the only system default."
  return
}

$written = 0
foreach ($c in $changes) {
  if ($PSCmdlet.ShouldProcess("$DataverseUrl sprk_workspacelayouts($($c.Id))", "Set sprk_isdefault = $($c.Value) on '$($c.Name)'")) {
    # If-Match '*' makes the PATCH update-only: a row that vanished between the read and the write is an error, never an upsert.
    $patchHeaders = $headers + @{ 'If-Match' = '*' }
    Invoke-RestMethod -Method Patch -Headers $patchHeaders -Uri "$api/sprk_workspacelayouts($($c.Id))" -Body (@{ sprk_isdefault = $c.Value } | ConvertTo-Json) | Out-Null
    $written++
    Write-Host "PATCHED: '$($c.Name)' sprk_isdefault = $($c.Value)"
  }
  elseif ($WhatIfPreference) {
    Write-Host "WHATIF (would PATCH): '$($c.Name)' sprk_isdefault = $($c.Value)"
  }
}

if ($WhatIfPreference) { return }
if ($written -eq 0) {
  Write-Host 'Cancelled, nothing written.'
  return
}
if ($written -lt $changes.Count) {
  Write-Host "Cancelled part-way: $written of $($changes.Count) writes were made. Run the script again to finish; until then both rows may be default and the one with the lower sortOrder wins."
  return
}

$after = @(Read-SystemLayout | Where-Object { $_.sprk_isdefault })
if ($after.Count -ne 1 -or $after[0].sprk_name -ne $To) {
  throw "Verification failed: system defaults are [$(($after | ForEach-Object { $_.sprk_name }) -join ', ')], expected only '$To'."
}
Write-Host "VERIFIED: the only system default is '$To' ($($after[0].sprk_workspacelayoutid))."
