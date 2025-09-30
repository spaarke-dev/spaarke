
param(
  [string]$RepoRoot = "."
)

$ErrorActionPreference = "Stop"
$fail = $false

Write-Host "Running ADR policy checks..."

# 1) No Azure Functions/Durable Functions
$functionsHits = Get-ChildItem -Path $RepoRoot -Recurse -Include *.csproj | `
  Select-String -Pattern "Microsoft\.Azure\.WebJobs|DurableTask|Microsoft\.Azure\.Functions"

if ($functionsHits) {
  Write-Error "ADR-001 violation: Azure Functions/Durable packages detected."
  $fail = $true
}

# 2) Graph types outside SpeFileStore or Infrastructure/Graph
$graphHits = Get-ChildItem -Path $RepoRoot -Recurse -Include *.cs | `
  Where-Object { $_.FullName -notmatch "Infrastructure\\Graph|SpeFileStore" } | `
  Select-String -Pattern "Microsoft\.Graph"

if ($graphHits) {
  Write-Error "ADR-007 violation: Graph types found outside SpeFileStore/Infrastructure/Graph."
  $graphHits | ForEach-Object { Write-Host $_.Path ":" $_.Line }
  $fail = $true
}

# 3) IMemoryCache used across requests (basic heuristic)
$imemHits = Get-ChildItem -Path $RepoRoot -Recurse -Include *.cs | Select-String -Pattern "IMemoryCache"
if ($imemHits) {
  Write-Warning "Check ADR-009: IMemoryCache references found. Ensure it's NOT used for cross-request caching."
}

# 4) Protected endpoints without endpoint filters (heuristic)
$endpointFiles = Get-ChildItem -Path $RepoRoot -Recurse -Include *Endpoints.cs
foreach ($f in $endpointFiles) {
  $txt = Get-Content $f.FullName -Raw
  if ($txt -match "Map(Get|Post|Put|Delete)\(" -and $txt -notmatch "AddEndpointFilter") {
    Write-Warning "ADR-008 check: $($f.Name) has mapped endpoints; ensure filters are applied."
  }
}

if ($fail) { exit 1 } else { Write-Host "ADR policy checks passed." }
