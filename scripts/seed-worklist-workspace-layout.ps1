<#
.SYNOPSIS
  Seed the "Worklist" system sprk_workspacelayout row so it appears as a system widget in the
  SpaarkeAi (Console) workspace dropdown (like Matter / Project / Calendar / Email / Reconciliation).

.DESCRIPTION
  spaarke-ontology-platform-r1 task 156. The `worklist` direct widget is registered but no Console
  path opened it: the workspace dropdown ("the library") lists sprk_workspacelayout rows, NOT the
  widget registry. Each single-widget system layout (sprk_issystem=true, single-column) mounts ONE
  LegalWorkspace section by id via its sprk_sectionsjson. This creates a "Worklist" layout that
  mounts the `worklist` section (worklist.registration.ts). Copy of
  seed-reconciliation-workspace-layout.ps1 (same row shape, same system flags).

  Idempotent by sprk_name AND sprk_issystem=true (skips if the system row exists; a user-owned layout that
  happens to be named "Worklist" does not satisfy it, same filter as Deploy-SystemWorkspaceLayouts.ps1). Requires the `worklist` LegalWorkspace
  section to be deployed (SpaarkeAi/LegalWorkspace bundle) for the layout to render content -- the
  row can be seeded independently.

  Supports -WhatIf: the existence check (a read) still runs, so -WhatIf reports exactly what a real
  run would do ("would create" when the row is absent, "no change" when it exists) and writes nothing.

.PARAMETER DataverseUrl
  Default: https://spaarkedev1.crm.dynamics.com

.EXAMPLE
  ./scripts/seed-worklist-workspace-layout.ps1 -WhatIf
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param([string]$DataverseUrl = 'https://spaarkedev1.crm.dynamics.com')
$ErrorActionPreference = 'Stop'
$api = "$DataverseUrl/api/data/v9.2"
$token = az account get-access-token --resource $DataverseUrl --query accessToken -o tsv
$headers = @{
  Authorization      = "Bearer $token"
  'Content-Type'     = 'application/json'
  'OData-MaxVersion' = '4.0'
  'OData-Version'    = '4.0'
  Prefer             = 'return=representation'
}

$name = 'Worklist'
$existing = Invoke-RestMethod -Method Get -Headers $headers -Uri "$api/sprk_workspacelayouts?`$filter=sprk_name eq '$name' and sprk_issystem eq true&`$select=sprk_workspacelayoutid,sprk_name"
if ($existing.value.Count -gt 0) {
  Write-Host "SKIP (exists): $name -> $($existing.value[0].sprk_workspacelayoutid)"
  return
}

# Single-widget layout: row-1 mounts the `worklist` section; rows 2-4 empty (matches the
# shipped Email/Messages/Calendar/Reconciliation system layouts exactly).
$sectionsJson = @{
  scope         = 'my'
  schemaVersion = 1
  rows          = @(
    @{ id = 'row-1'; columns = '1fr'; columnsSmall = '1fr'; sections = @('worklist') }
    @{ id = 'row-2'; columns = '1fr'; columnsSmall = '1fr'; sections = @('') }
    @{ id = 'row-3'; columns = '1fr'; columnsSmall = '1fr'; sections = @('') }
    @{ id = 'row-4'; columns = '1fr'; columnsSmall = '1fr'; sections = @('') }
  )
} | ConvertTo-Json -Depth 6 -Compress

$body = @{
  sprk_name             = $name
  sprk_issystem         = $true
  sprk_isdefault        = $false
  sprk_layouttemplateid = 'single-column'
  sprk_sortorder        = 13   # after Matters (12), the last shipped system row; no existing row is renumbered
  sprk_sectionsjson     = $sectionsJson
} | ConvertTo-Json -Depth 4

if ($PSCmdlet.ShouldProcess("$DataverseUrl (sprk_workspacelayouts)", "Create '$name' system layout, sections [""worklist""]")) {
  $created = Invoke-RestMethod -Method Post -Headers $headers -Uri "$api/sprk_workspacelayouts" -Body $body
  Write-Host "CREATED: $name -> $($created.sprk_workspacelayoutid)"
}
else {
  Write-Host "WHATIF (would create): $name (sprk_issystem=true, single-column, sprk_sortorder=13, sections: [""worklist""])"
  Write-Host "  sprk_sectionsjson = $sectionsJson"
}
