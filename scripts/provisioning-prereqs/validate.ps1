<#
.SYNOPSIS
  Forcing-function validator for scripts/provisioning-prereqs/prereqs.yaml.

.DESCRIPTION
  Uses the SAME parser as .claude/skills/provision-environment/SKILL.md Step 0.5a
  (powershell-yaml / ConvertFrom-Yaml), so 'parses here' == 'parses at runtime'.

  Validates:
    1. YAML parseability under powershell-yaml (parser parity with production consumer)
    2. Top-level shape (schema_version, manifest_version, last_updated, prereqs[])
    3. Per-prereq required fields (id, name, scope, owner, consequence_of_absence,
       check_recipe.cli, remediation) per SKILL Step 0.5b contract
    4. Scope enum (once_per_tenant | once_per_subscription | once_per_env | once_per_customer)
    5. Unique prereq ids
    6. SPE never_delete guard (PRQ-T-01, PRQ-T-02 must retain never_delete: true —
       owner directive 2026-08-24 BINDING; 24h SPE replication penalty)
    7. intake.schema.json is valid JSON

  Exits 0 on success, 1 on any failure with actionable per-issue diagnostics.

.NOTES
  Authored 2026-08-27 during customer-provisioning-orchestration-r1 SESSION 14.
  Response to the 18-defect regression discovered when Step 0.5 iteration first ran
  end-to-end. See notes/lessons-learned-2026-08-27-prereqs-yaml-parse-defect.md.

  Invoked from:
    - .github/workflows/provisioning-prereqs-validate.yml (CI gate on every PR/push)
    - .lintstagedrc.mjs (author-time relief on git commit)
    - Directly: pwsh -File scripts/provisioning-prereqs/validate.ps1
#>
[CmdletBinding()]
param(
  [string] $ManifestPath = (Join-Path $PSScriptRoot 'prereqs.yaml'),
  [string] $IntakeSchemaPath = (Join-Path $PSScriptRoot 'intake.schema.json')
)

$ErrorActionPreference = 'Stop'
$fail = @()

Write-Host "prereqs.yaml validator - manifest: $ManifestPath" -ForegroundColor Cyan

# --- 1. Parser parity: powershell-yaml MUST be able to parse ---
if (-not (Get-Module -ListAvailable -Name powershell-yaml)) {
  Write-Host "  Installing powershell-yaml (one-time; ~8s)..." -ForegroundColor Yellow
  Install-Module powershell-yaml -Scope CurrentUser -Force -Confirm:$false | Out-Null
}
Import-Module powershell-yaml

try {
  $m = Get-Content -Raw -Path $ManifestPath | ConvertFrom-Yaml -ErrorAction Stop
} catch {
  Write-Host "`nprereqs.yaml FAILED to parse under powershell-yaml (production parser):" -ForegroundColor Red
  Write-Host "  $($_.Exception.Message)" -ForegroundColor Red
  Write-Host "`nThis is the exact parser SKILL Step 0.5a uses at operator invocation." -ForegroundColor Red
  Write-Host "If this validator fails, the /provision-environment skill will HARD STOP for every operator." -ForegroundColor Red
  exit 1
}
Write-Host "  [PASS] Parse (powershell-yaml)" -ForegroundColor Green

# --- 2. Top-level shape ---
$requiredTop = @('schema_version','manifest_version','last_updated','prereqs')
foreach ($k in $requiredTop) {
  if (-not $m.ContainsKey($k)) { $fail += "Top-level: missing required key '$k'." }
}
if ($m.prereqs -isnot [System.Collections.IEnumerable] -or $m.prereqs.Count -lt 1) {
  $fail += 'Top-level: prereqs[] must be a non-empty list.'
}
if (-not $fail) { Write-Host "  [PASS] Top-level shape (schema_version=$($m.schema_version), $($m.prereqs.Count) prereqs)" -ForegroundColor Green }

# --- 3. Per-prereq required fields (matches SKILL Step 0.5b) ---
$allowedScopes = @('once_per_tenant','once_per_subscription','once_per_env','once_per_customer')
$requiredFields = @('id','name','scope','owner','consequence_of_absence','check_recipe','remediation')
# SPE container-type itself — owner directive 2026-08-24 BINDING (24h replication penalty).
# NOTE: only PRQ-T-01 (the container-type artifact itself) is protected. PRQ-T-02 (app permissions
# on the container-type) is re-grantable and does NOT carry the 24h penalty, so it is intentionally
# excluded from this guard.
$speNeverDeleteIds = @('PRQ-T-01')
$seenIds = @{}
$scopeCounts = @{}

foreach ($p in $m.prereqs) {
  $id = if ($p.id) { $p.id } else { '<missing-id>' }

  foreach ($f in $requiredFields) {
    if (-not $p.ContainsKey($f) -or $null -eq $p[$f] -or ($p[$f] -is [string] -and [string]::IsNullOrWhiteSpace($p[$f]))) {
      $fail += "$id (line ~$($p.line)): missing required field '$f'."
    }
  }

  # Scope enum
  if ($p.scope -and ($p.scope -notin $allowedScopes)) {
    $fail += "${id}: scope '$($p.scope)' not in [$($allowedScopes -join ', ')]. If you need to annotate, use a YAML comment: 'scope: once_per_env  # your-note'"
  }
  if ($p.scope -in $allowedScopes) { $scopeCounts[$p.scope] = ($scopeCounts[$p.scope] + 1) }

  # check_recipe.cli must exist
  if ($p.check_recipe -and (-not $p.check_recipe.ContainsKey('cli') -or [string]::IsNullOrWhiteSpace([string]$p.check_recipe.cli))) {
    $fail += "${id}: check_recipe.cli is empty (Step 0.5b will fail to invoke)."
  }

  # Unique id
  if ($p.id) {
    if ($seenIds.ContainsKey($p.id)) { $fail += "Duplicate prereq id: $($p.id)." }
    $seenIds[$p.id] = $true
  }

  # SPE never_delete guard
  if ($p.id -in $speNeverDeleteIds -and $p.never_delete -ne $true) {
    $fail += "$($p.id): must retain 'never_delete: true' (SPE 24h replication penalty; BINDING owner directive 2026-08-24)."
  }
}

if (-not $fail) {
  Write-Host "  [PASS] Per-prereq shape ($($seenIds.Count) unique ids, SPE never_delete preserved on $($speNeverDeleteIds -join '+'))" -ForegroundColor Green
  Write-Host "         By scope: $(($scopeCounts.GetEnumerator() | Sort-Object Key | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ', ')" -ForegroundColor Gray
}

# --- 4. intake.schema.json is valid JSON ---
if (Test-Path $IntakeSchemaPath) {
  try {
    Get-Content -Raw -Path $IntakeSchemaPath | ConvertFrom-Json -Depth 32 | Out-Null
    Write-Host "  [PASS] intake.schema.json is valid JSON" -ForegroundColor Green
  } catch {
    $fail += "intake.schema.json is not valid JSON: $($_.Exception.Message)"
  }
} else {
  Write-Host "  [SKIP] intake.schema.json not found at $IntakeSchemaPath (not fatal — only enforced when present)" -ForegroundColor Yellow
}

# --- Report ---
if ($fail.Count -gt 0) {
  Write-Host "`nprereqs.yaml validation FAILED ($($fail.Count) issue$(if($fail.Count -ne 1){'s'})):`n" -ForegroundColor Red
  $fail | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
  Write-Host "`nSee .claude/skills/provision-environment/SKILL.md Step 0.5b for the recipe author contract." -ForegroundColor Red
  exit 1
}

Write-Host "`nprereqs.yaml OK ($($m.prereqs.Count) prereqs, schema v$($m.schema_version), manifest v$($m.manifest_version))." -ForegroundColor Green
exit 0
