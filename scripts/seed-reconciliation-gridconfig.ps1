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
$emailReviewAllId       = '00000000-0000-4000-8000-000000005002'  # == EMAIL_REVIEW_ALL_CONFIG_ID (UAT Fix #5)
$emailReviewCompletedId = '00000000-0000-4000-8000-000000005003'  # == EMAIL_REVIEW_COMPLETED_CONFIG_ID (UAT Fix #5)

function Seed-Config($id, $name, $jsonPath) {
  # Existence check (by id when provided, else by name). Double apostrophes for OData.
  $nameEsc = $name -replace "'", "''"
  $filter = if ($id) { "sprk_gridconfigurationid eq $id" } else { "sprk_name eq '$nameEsc'" }
  $existing = Invoke-RestMethod -Method Get -Headers $headers -Uri "$api/sprk_gridconfigurations?`$filter=$filter&`$select=sprk_gridconfigurationid,sprk_name"
  if ($existing.value.Count -gt 0) {
    # PATCH sprk_configjson so a re-seed always reflects the shared-lib source of
    # truth (the config JSON is read verbatim from code → can never drift).
    $existingId = $existing.value[0].sprk_gridconfigurationid
    $configJson = Get-Content -Raw -Path $jsonPath
    $patchBody = @{ sprk_configjson = $configJson } | ConvertTo-Json -Depth 5
    Invoke-RestMethod -Method Patch -Headers $headers -Uri "$api/sprk_gridconfigurations($existingId)" -Body $patchBody | Out-Null
    Write-Host "UPDATED (configjson): $name -> $existingId"
    return $existingId
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
$ra = Seed-Config $emailReviewAllId 'Communication Reconciliation - Email Review All' "$root/email-review-all.gridconfiguration.json"
$rc = Seed-Config $emailReviewCompletedId 'Communication Reconciliation - Email Review Completed' "$root/email-review-completed.gridconfiguration.json"
Write-Host ''
Write-Host "NEEDS_REVIEW_CONFIG_ID          = $nr"
Write-Host "PER_TEAM_CONFIG_ID              = $pt"
Write-Host "EMAIL_REVIEW_ALL_CONFIG_ID      = $ra"
Write-Host "EMAIL_REVIEW_COMPLETED_CONFIG_ID = $rc"
if ($nr -ne $needsReviewId) { Write-Warning "Needs-review id != code constant $needsReviewId — code update required!" }
if ($ra -ne $emailReviewAllId) { Write-Warning "Email-review-all id != code constant $emailReviewAllId — code update required!" }
if ($rc -ne $emailReviewCompletedId) { Write-Warning "Email-review-completed id != code constant $emailReviewCompletedId — code update required!" }
