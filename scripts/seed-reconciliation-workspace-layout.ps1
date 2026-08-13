<#
.SYNOPSIS
  Seed the "Reconciliation" system sprk_workspacelayout row so it appears as a system
  widget in the SpaarkeAi workspace dropdown (like Matter / Project / Calendar / Email).

.DESCRIPTION
  UAT Fix #6 (email-communication-intelligence-r2). The workspace dropdown ("the library")
  lists sprk_workspacelayout rows, NOT the widget registry. Each single-widget system layout
  (sprk_issystem=true, single-column) mounts ONE LegalWorkspace section by id via its
  sprk_sectionsjson. This creates a "Reconciliation" layout that mounts the `reconciliation`
  section (reconciliation.registration.ts). Mirrors the shipped "Email"/"Messages" rows.

  Idempotent by sprk_name (skips if the row exists). Requires the `reconciliation`
  LegalWorkspace section to be deployed (SpaarkeAi/LegalWorkspace bundle) for the layout to
  render content — the row can be seeded independently.

.PARAMETER DataverseUrl
  Default: https://spaarkedev1.crm.dynamics.com
#>
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

$name = 'Reconciliation'
$existing = Invoke-RestMethod -Method Get -Headers $headers -Uri "$api/sprk_workspacelayouts?`$filter=sprk_name eq '$name'&`$select=sprk_workspacelayoutid,sprk_name"
if ($existing.value.Count -gt 0) {
  Write-Host "SKIP (exists): $name -> $($existing.value[0].sprk_workspacelayoutid)"
  return
}

# Single-widget layout: row-1 mounts the `reconciliation` section; rows 2-4 empty (matches
# the shipped Email/Messages/Calendar system layouts exactly).
$sectionsJson = @{
  scope         = 'my'
  schemaVersion = 1
  rows          = @(
    @{ id = 'row-1'; columns = '1fr'; columnsSmall = '1fr'; sections = @('reconciliation') }
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
  sprk_sortorder        = 7   # after Messages (6); before Email (10)
  sprk_sectionsjson     = $sectionsJson
} | ConvertTo-Json -Depth 4

$created = Invoke-RestMethod -Method Post -Headers $headers -Uri "$api/sprk_workspacelayouts" -Body $body
Write-Host "CREATED: $name -> $($created.sprk_workspacelayoutid)"
