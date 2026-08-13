# Seeds the two Pillar-E reconciliation sprk_gridconfiguration records (task 059).
# Idempotent: skips a record if its id/name already exists. Reads the config JSON
# verbatim from the shared-lib source of truth so the seed can never drift from code.
$ErrorActionPreference = 'Stop'
$env = 'https://spaarkedev1.crm.dynamics.com'
$api = "$env/api/data/v9.2"
$token = az account get-access-token --resource $env --query accessToken -o tsv
$headers = @{
  Authorization    = "Bearer $token"
  'Content-Type'   = 'application/json'
  'OData-MaxVersion' = '4.0'
  'OData-Version'  = '4.0'
  Prefer           = 'return=representation'
}
$root = 'src/client/shared/Spaarke.Communication.Components/src/components/ReconciliationGrid'
$needsReviewId = '00000000-0000-4000-8000-000000005001'  # == NEEDS_REVIEW_CONFIG_ID in code

function Seed-Config($id, $name, $jsonPath) {
  # Existence check (by id when provided, else by name). Double apostrophes for OData.
  $nameEsc = $name -replace "'", "''"
  $filter = if ($id) { "sprk_gridconfigurationid eq $id" } else { "sprk_name eq '$nameEsc'" }
  $existing = Invoke-RestMethod -Method Get -Headers $headers -Uri "$api/sprk_gridconfigurations?`$filter=$filter&`$select=sprk_gridconfigurationid,sprk_name"
  if ($existing.value.Count -gt 0) {
    Write-Host "SKIP (exists): $name -> $($existing.value[0].sprk_gridconfigurationid)"
    return $existing.value[0].sprk_gridconfigurationid
  }
  $configJson = Get-Content -Raw -Path $jsonPath
  $body = @{
    sprk_name              = $name
    sprk_entitylogicalname = 'sprk_communication'
    sprk_configjson        = $configJson
  }
  if ($id) { $body['sprk_gridconfigurationid'] = $id }
  $bodyJson = $body | ConvertTo-Json -Depth 5
  $created = Invoke-RestMethod -Method Post -Headers $headers -Uri "$api/sprk_gridconfigurations" -Body $bodyJson
  Write-Host "CREATED: $name -> $($created.sprk_gridconfigurationid)"
  return $created.sprk_gridconfigurationid
}

Write-Host '--- Seeding reconciliation grid configs (spaarkedev1) ---'
$nr = Seed-Config $needsReviewId 'Communication Reconciliation - Needs Review' "$root/needs-review.gridconfiguration.json"
$pt = Seed-Config $null 'Communication Reconciliation - My Team''s Reviews' "$root/per-team.gridconfiguration.json"
Write-Host ''
Write-Host "NEEDS_REVIEW_CONFIG_ID = $nr"
Write-Host "PER_TEAM_CONFIG_ID     = $pt"
if ($nr -ne $needsReviewId) { Write-Warning "Needs-review id != code constant $needsReviewId — code update required!" }
