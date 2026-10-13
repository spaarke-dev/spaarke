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
    8. context-defaults.{dev,prod}.json parse as JSON (ISH-09)
    9. Every {token} referenced in prereqs.yaml has a documented entry in
       context-defaults.dev.json tokenMap (ISH-09 author-time cross-check) —
       missing tokens would eventually surface as unresolved-placeholder
       [skill-config] errors at Step 0.5b runtime; catching them here shortens
       the feedback loop for authors adding new recipes.

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
  [string] $IntakeSchemaPath = (Join-Path $PSScriptRoot 'intake.schema.json'),
  # ISH-09 (Wave 5 punchlist, 2026-08-27): maintainer-facing companion doc
  # of the SKILL.md Step 0.5b substitution map + spaarke-constants.yaml.
  [string] $ContextDefaultsDevPath  = (Join-Path $PSScriptRoot 'context-defaults.dev.json'),
  [string] $ContextDefaultsProdPath = (Join-Path $PSScriptRoot 'context-defaults.prod.json')
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

  # Retired entries keep their id so references do not dangle, but carry no check_recipe
  # (Step 0.5b skips status: retired). They must say when and by whom they were retired.
  $isRetired = $p.ContainsKey('status') -and $p.status -eq 'retired'
  if ($p.ContainsKey('status') -and -not $isRetired) {
    $fail += "${id}: status '$($p.status)' is not recognised (the only status value is 'retired')."
  }
  $fieldsForThis = if ($isRetired) {
    @($requiredFields | Where-Object { $_ -ne 'check_recipe' }) + @('retired_on', 'retired_by')
  } else { $requiredFields }

  foreach ($f in $fieldsForThis) {
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

# --- 5. context-defaults.{dev,prod}.json parse (ISH-09) ---
$contextDefaultsByEnv = @{}
foreach ($ctx in @(
  @{ Env = 'dev';  Path = $ContextDefaultsDevPath  },
  @{ Env = 'prod'; Path = $ContextDefaultsProdPath }
)) {
  if (-not (Test-Path $ctx.Path)) {
    Write-Host "  [SKIP] context-defaults.$($ctx.Env).json not found at $($ctx.Path) (not fatal — only enforced when present)" -ForegroundColor Yellow
    continue
  }
  try {
    $contextDefaultsByEnv[$ctx.Env] = Get-Content -Raw -Path $ctx.Path | ConvertFrom-Json -Depth 16
    Write-Host "  [PASS] context-defaults.$($ctx.Env).json is valid JSON" -ForegroundColor Green
  } catch {
    $fail += "context-defaults.$($ctx.Env).json is not valid JSON: $($_.Exception.Message)"
  }
}

# --- 6. Cross-check: every {token} in prereqs.yaml recipes is documented in
# context-defaults.dev.json tokenMap (ISH-09 author-time diagnostic).
# The DEV map is treated as the authoritative token-shape reference; PROD is
# expected to mirror shape (same keys, per-env source values only). A token
# missing from dev's tokenMap would eventually surface as an unresolved-
# placeholder [skill-config] error at Step 0.5b runtime — catching it here
# shortens the loop for authors adding new recipes.
if ($contextDefaultsByEnv.ContainsKey('dev') -and $m -and $m.prereqs) {
  $documentedTokens = @{}
  foreach ($prop in $contextDefaultsByEnv['dev'].tokenMap.PSObject.Properties) {
    $documentedTokens[$prop.Name] = $true
  }
  $referencedTokens = @{}
  foreach ($p in $m.prereqs) {
    if ($p.check_recipe -and $p.check_recipe.cli) {
      $matches = [regex]::Matches([string]$p.check_recipe.cli, '\{([a-zA-Z_][a-zA-Z0-9_]*)\}')
      foreach ($mm in $matches) {
        $referencedTokens[$mm.Groups[1].Value] = $true
      }
    }
  }
  $missing = @()
  foreach ($tokenName in $referencedTokens.Keys) {
    if (-not $documentedTokens.ContainsKey($tokenName)) {
      $missing += $tokenName
    }
  }
  if ($missing.Count -gt 0) {
    foreach ($token in $missing) {
      $fail += "context-defaults.dev.json tokenMap missing entry for '{$token}' — referenced by prereqs.yaml. Add a documentation entry (source, shape, populatedBy) so maintainers know how to derive it, then extend SKILL.md Step 0.5b substitution chain accordingly."
    }
  } else {
    Write-Host "  [PASS] All $($referencedTokens.Count) prereqs.yaml {tokens} documented in context-defaults.dev.json" -ForegroundColor Green
  }
}

# --- 7. Recipe lints (T206 + T207) --------------------------------------------------------------
# These check what a recipe WOULD do at Step 0.5b / the customer pass, without running it:
#   a. token availability   - a {token} must resolve at the step that runs the recipe's scope
#   b. undefined shell vars - the recipe runs in a fresh `bash -c` with no variables defined
#   c. recipe contract      - an explicit `exit 1`; no PowerShell cmdlets under bash
#   d. Windows az.cmd       - a parenthesis in a double-quoted argument of an `az` call breaks az.cmd
#   e. grep -i with -F      - aborts (exit 134) in the grep that ships with Git for Windows
# Retired entries carry no recipe and are skipped.
$scopeRank = @{ once_per_tenant = 0; once_per_subscription = 0; once_per_env = 1; once_per_customer = 2 }
$availRank = @{ always = 0; env = 1; customer = 2 }
$tokenAvail = @{}
if ($contextDefaultsByEnv.ContainsKey('dev')) {
  foreach ($prop in $contextDefaultsByEnv['dev'].tokenMap.PSObject.Properties) {
    $tokenAvail[$prop.Name] = [string]$prop.Value.availability
  }
}
$failBeforeLints = $fail.Count
$availReported = @{}
if ($m -and $m.prereqs) {
  foreach ($p in $m.prereqs) {
    if (-not $p.check_recipe -or -not $p.check_recipe.cli) { continue }
    $id = [string]$p.id
    $cli = [string]$p.check_recipe.cli
    $scope = [string]$p.scope

    # a. token availability
    foreach ($mm in [regex]::Matches($cli, '\{([a-zA-Z_][a-zA-Z0-9_]*)\}')) {
      $t = $mm.Groups[1].Value
      if (-not $tokenAvail.ContainsKey($t)) { continue }   # undocumented tokens are reported by check 6
      if (-not $availRank.ContainsKey($tokenAvail[$t])) {
        $bad = "availability:$t"
        if (-not $availReported.ContainsKey($bad)) { $availReported[$bad] = $true; $fail += "context-defaults.dev.json tokenMap '$t': availability '$($tokenAvail[$t])' is not one of always | env | customer." }
        continue
      }
      if ($scopeRank.ContainsKey($scope) -and $availRank[$tokenAvail[$t]] -gt $scopeRank[$scope]) {
        $fail += "${id}: recipe uses {$t}, which resolves only from the '$($tokenAvail[$t])' pass, but the prereq is scope '$scope' - the step that runs this scope cannot resolve it. Re-scope the prereq or derive the value inside the recipe."
      }
    }

    # b. undefined shell variables
    $noSingle = [regex]::Replace($cli, "'[^'\r\n]*'", "''")                  # single-quoted text is never expanded
    $noComment = [regex]::Replace($noSingle, '(?m)(^|\s)#.*$', '$1')
    $defined = @{}
    foreach ($d in [regex]::Matches($noComment, '(?m)(?:^|[\s;{(&|])([A-Za-z_][A-Za-z0-9_]*)=')) { $defined[$d.Groups[1].Value] = $true }
    foreach ($d in [regex]::Matches($noComment, '\bfor\s+([A-Za-z_][A-Za-z0-9_]*)\s+in\b')) { $defined[$d.Groups[1].Value] = $true }
    foreach ($d in [regex]::Matches($noComment, '\bread\s+(?:-\w+\s+)*([A-Za-z_][A-Za-z0-9_]*)')) { $defined[$d.Groups[1].Value] = $true }
    foreach ($v in [regex]::Matches($noComment, '(?<!\\)\$\{?([A-Za-z_][A-Za-z0-9_]*)')) {
      $name = $v.Groups[1].Value
      if (-not $defined.ContainsKey($name)) {
        $fail += "${id}: recipe references `$$name but never defines it - bash -c starts with no variables, so it expands to empty. Define it in the recipe, or escape it with a backslash when it is a URL/OData parameter."
        $defined[$name] = $true   # report once per recipe
      }
    }

    # c. recipe contract
    if ($cli -notmatch '\bexit\s+1\b') {
      $fail += "${id}: recipe has no explicit 'exit 1' - SKILL Step 0.5b trusts the exit code, so a failure condition must exit 1 itself."
    }
    foreach ($line in ($cli -split "`n")) {
      if ($line -match '\bpwsh\b') { continue }   # PowerShell inside `pwsh -Command` is intended
      if ($line -cmatch '\b(Select-String|Where-Object|ForEach-Object|Out-String|Write-Host|Get-[A-Z][A-Za-z]+)\b') {
        $fail += "${id}: recipe runs under bash -c but uses the PowerShell cmdlet '$($Matches[1])' (command not found, exit 127, silently)."
        break
      }
    }

    # e. GNU grep 3.0 as shipped in Git for Windows aborts (exit 134) on -i combined with -F
    if ($cli -match '\bgrep\s+-[a-zA-Z]*(i[a-zA-Z]*F|F[a-zA-Z]*i)') {
      $fail += "${id}: recipe uses 'grep' with both -i and -F - the grep in Git for Windows aborts (exit 134) on that combination. Use a regex (-i without -F) or drop -i."
    }

    # d. Windows az.cmd breaks on a parenthesis inside an argument
    foreach ($line in ($cli -split "`n")) {
      if ($line -notmatch '(^|[\s(`])az\s') { continue }
      foreach ($q in [regex]::Matches($line, '--[a-z][a-z-]*\s+"([^"]*)"')) {
        $seg = $q.Groups[1].Value
        $segNoSubst = [regex]::Replace($seg, '\$\([^)]*\)', '')
        if ($segNoSubst -match '[()]') {
          $fail += "${id}: an az argument contains a parenthesis ($($q.Value)) - the Windows az.cmd wrapper breaks on it. Filter in bash instead of JMESPath functions, or call the REST endpoint with curl."
          break
        }
      }
    }
  }
  if ($fail.Count -eq $failBeforeLints) {
    Write-Host "  [PASS] Recipe lints (token availability, undefined shell vars, exit-1 contract, no PowerShell under bash, no parenthesis in az args)" -ForegroundColor Green
  }
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
