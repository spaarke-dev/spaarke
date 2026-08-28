# -----------------------------------------------------------------------------
# validate-recipe-authoring.ps1
#
# Bucket B HIGH#9 SESSION 18 (customer-provisioning-orchestration-r1 adversarial
# e2e verify workflow wepdcb8we) — forcing function that prevents the
# "exit-0-on-empty" recipe-author bug from silently re-appearing in prereqs.yaml.
#
# THE BUG THIS GUARDS AGAINST:
#   `az … --query "[0].appId" -o tsv` returns EMPTY STDOUT with exit code 0
#   when jmespath `[0]` misses. Downstream classifier reads exit != 0 for FAIL,
#   so an empty result silently PASSES. Multiple prereqs suffered this class
#   (PRQ-T-03/T-04/T-05/T-06/T-07, PRQ-S-04/S-05, PRQ-E-03/E-04/E-05/E-07/E-09/E-10)
#   before the SESSION 18 hardening added `[ -z "$result" ] && exit 1` guards.
#
# THE FORCING FUNCTION:
#   Any recipe body that references `-o tsv` OR `--query` MUST also contain
#   `[ -z ` OR `exit 1` — that is the recipe author asserting they handled the
#   empty-result case. A recipe that matches the first pattern but not either
#   emptiness pattern fails this validator, forcing the author to either add
#   the guard OR justify the exception in a comment.
#
# ACCEPTED EXCEPTIONS (rare — expand only with reviewer sign-off):
#   - Recipes whose body includes the literal substring `# NO-EXIT-1-GUARD-JUSTIFIED:`
#     followed by a reason (e.g., informational-only recipe whose empty-result
#     genuinely means PASS). PRQ-S-01 falls in this class (billing agreement
#     type is a read-only info check).
#
# CI USAGE:
#   Add to sdap-ci.yml:
#     - name: Validate prereqs recipe authoring
#       run: pwsh scripts/provisioning-prereqs/validate-recipe-authoring.ps1
#
# LOCAL USAGE:
#   pwsh scripts/provisioning-prereqs/validate-recipe-authoring.ps1
#   → exit 0 on clean; exit 1 on any violating recipe with the id + missing pattern
#
# PARSER PARITY:
#   Uses powershell-yaml (same as validate.ps1 + SKILL.md Step 0.5a) so any
#   recipe body that this script sees is the exact string bash -c would receive
#   at runtime.
# -----------------------------------------------------------------------------

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

# One-time install (idempotent).
if (-not (Get-Module -ListAvailable -Name powershell-yaml)) {
    Write-Host "  Installing powershell-yaml (one-time)..." -ForegroundColor Yellow
    Install-Module powershell-yaml -Scope CurrentUser -Force -Confirm:$false
}
Import-Module powershell-yaml

$repoRoot = git rev-parse --show-toplevel
if (-not $repoRoot) { throw "Not in a git repo — cannot resolve prereqs.yaml path." }
$manifestPath = Join-Path $repoRoot 'scripts/provisioning-prereqs/prereqs.yaml'
if (-not (Test-Path $manifestPath)) { throw "prereqs.yaml not found at '$manifestPath'." }

$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Yaml
$violations = @()

Write-Host ''
Write-Host '==================================================================' -ForegroundColor Cyan
Write-Host '  prereqs.yaml Recipe Authoring Validator (Bucket B HIGH#9)' -ForegroundColor Cyan
Write-Host '==================================================================' -ForegroundColor Cyan
Write-Host "  Manifest: $manifestPath"
Write-Host ''

foreach ($prereq in $manifest.prereqs) {
    if (-not $prereq.check_recipe -or -not $prereq.check_recipe.cli) { continue }

    $id = [string]$prereq.id
    $recipe = [string]$prereq.check_recipe.cli

    # Rule 1: any recipe using `-o tsv` OR `--query` MUST also contain an
    #         emptiness guard OR an explicit exit-1 branch.
    $usesTsvOrQuery = ($recipe -match '-o\s+tsv') -or ($recipe -match '--query')
    if (-not $usesTsvOrQuery) {
        # No jmespath / tsv output → not exposed to the exit-0-on-empty bug.
        # Multi-line recipes that use `az rest --uri ... 2>/dev/null | jq -e` are
        # also fine (jq -e sets exit code based on the boolean/existence result).
        Write-Host "  [SKIP] $id — no -o tsv / --query use; not exposed to exit-0-on-empty class" -ForegroundColor Gray
        continue
    }

    $hasEmptinessGuard = ($recipe -match '\[\s+-z\s+') -or ($recipe -match '\[\s+-n\s+')
    $hasExplicitExit1 = $recipe -match 'exit\s+1'
    $hasJqExists = $recipe -match 'jq\s+-e'
    $hasJustifiedException = $recipe -match '#\s*NO-EXIT-1-GUARD-JUSTIFIED:'

    if ($hasEmptinessGuard -or $hasExplicitExit1 -or $hasJqExists -or $hasJustifiedException) {
        $signal = if ($hasJustifiedException) { 'JUSTIFIED-EXCEPTION' }
                  elseif ($hasEmptinessGuard) { '[ -z guard' }
                  elseif ($hasJqExists) { 'jq -e' }
                  else { 'exit 1' }
        Write-Host "  [PASS] $id — has $signal" -ForegroundColor Green
    } else {
        Write-Host "  [FAIL] $id — uses -o tsv/--query but has NO emptiness guard, NO exit 1, NO jq -e, NO justified exception" -ForegroundColor Red
        $violations += @{
            Id = $id
            Recipe = $recipe.Substring(0, [Math]::Min(120, $recipe.Length))
        }
    }
}

Write-Host ''
Write-Host '==================================================================' -ForegroundColor Cyan

if ($violations.Count -eq 0) {
    Write-Host "  ALL RECIPES PASS — no exit-0-on-empty exposure detected" -ForegroundColor Green
    Write-Host '=================================================================='
    exit 0
}

Write-Host "  FAIL — $($violations.Count) recipe(s) exposed to the exit-0-on-empty silent-fail class:" -ForegroundColor Red
Write-Host ''
foreach ($v in $violations) {
    Write-Host "    $($v.Id):" -ForegroundColor Red
    Write-Host "      $($v.Recipe)" -ForegroundColor DarkYellow
}
Write-Host ''
Write-Host "  Fix pattern (add to each recipe):" -ForegroundColor Yellow
Write-Host '    result=$(az … --query "…" -o tsv 2>/dev/null)' -ForegroundColor Yellow
Write-Host '    [ -z "$result" ] && { echo "NOT_FOUND: <context>"; exit 1; }' -ForegroundColor Yellow
Write-Host '    echo "OK: $result"' -ForegroundColor Yellow
Write-Host ''
Write-Host "  Or for legitimately-informational recipes, add a marker comment:" -ForegroundColor Yellow
Write-Host '    # NO-EXIT-1-GUARD-JUSTIFIED: this recipe is read-only informational (see PRQ-S-01)' -ForegroundColor Yellow
Write-Host '=================================================================='
exit 1
