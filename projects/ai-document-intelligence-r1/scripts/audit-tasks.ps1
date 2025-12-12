[CmdletBinding()]
param(
    [Parameter()]
    [string]$IndexPath = "projects/ai-document-intelligence-r1/tasks/TASK-INDEX.md",

    [Parameter()]
    [string]$TasksDir = "projects/ai-document-intelligence-r1/tasks",

    [Parameter()]
    [switch]$ShowIds,

    [Parameter()]
    [ValidateRange(1, 500)]
    [int]$MaxIds = 50
)

$ErrorActionPreference = 'Stop'

function Get-StatusKey {
    param([string]$statusCell)

    if ($statusCell -match '✅') { return 'complete' }
    if ($statusCell -match '🔄') { return 'in-progress' }
    if ($statusCell -match '🔲') { return 'not-started' }
    if ($statusCell -match '⏸') { return 'blocked' }
    if ($statusCell -match '❌') { return 'cancelled' }

    # Fallback for cases where the file was viewed with a broken encoding and emoji became junk.
    if ($statusCell -match 'Complete|Completed') { return 'complete' }
    if ($statusCell -match 'In Progress') { return 'in-progress' }
    if ($statusCell -match 'Not Started') { return 'not-started' }
    if ($statusCell -match 'Blocked') { return 'blocked' }
    if ($statusCell -match 'Cancelled|Canceled') { return 'cancelled' }

    return 'unknown'
}

$indexFullPath = Resolve-Path -Path $IndexPath
$tasksFullPath = Resolve-Path -Path $TasksDir

$rows = @()
foreach ($line in (Get-Content -Path $indexFullPath)) {
    if ($line -match '^\|\s*(\d{3})\s*\|\s*([^|]+?)\s*\|\s*([^|]+?)\s*\|') {
        $id = $Matches[1]
        $task = $Matches[2].Trim()
        $statusCell = $Matches[3].Trim()
        $rows += [pscustomobject]@{
            Id = $id
            Task = $task
            StatusCell = $statusCell
            StatusKey = (Get-StatusKey $statusCell)
        }
    }
}

$files = Get-ChildItem -Path $tasksFullPath -Filter '*.poml' -File

$filesById = @{}
foreach ($f in $files) {
    if ($f.Name -match '^(\d{3})-') {
        $id = $Matches[1]
        if (-not $filesById.ContainsKey($id)) { $filesById[$id] = @() }
        $filesById[$id] += $f
    }
}

$missing = @()
$duplicates = @()
$incompleteNonCompliant = @()

foreach ($r in $rows) {
    if (-not $filesById.ContainsKey($r.Id)) {
        $missing += $r.Id
        continue
    }

    $candidates = $filesById[$r.Id]
    if ($candidates.Count -gt 1) {
        $duplicates += $r.Id
    }

    if ($r.StatusKey -ne 'complete') {
        $txt = Get-Content -Raw -Path $candidates[0].FullName
        $isNewFormat = $txt -match ('<task\s+id="' + $r.Id + '"')
        $hasInputs = $txt -match '<inputs[\s>]' 
        $hasKnowledge = $txt -match '<knowledge[\s>]' 
        $hasSteps = $txt -match '<steps[\s>]' 
        $hasAcceptance = $txt -match '<acceptance-criteria[\s>]' 

        if (-not ($isNewFormat -and $hasInputs -and $hasKnowledge -and $hasSteps -and $hasAcceptance)) {
            $incompleteNonCompliant += $r.Id
        }
    }
}

$byStatus = $rows | Group-Object StatusKey | Sort-Object Name

$summary = [pscustomobject]@{
    IndexRows = $rows.Count
    TaskFiles = $files.Count
    MissingFiles = $missing.Count
    DuplicateIds = $duplicates.Count
    IncompleteNonCompliant = $incompleteNonCompliant.Count
    StatusCounts = ($byStatus | ForEach-Object { "{0}={1}" -f $_.Name, $_.Count }) -join '; '
}

$summary | Format-List

if ($ShowIds) {
    "---" | Write-Output
    if ($missing.Count -gt 0) {
        "Missing IDs (first $MaxIds):" | Write-Output
        $missing | Select-Object -First $MaxIds | ForEach-Object { "- $_" }
    }
    if ($duplicates.Count -gt 0) {
        "Duplicate IDs (first $MaxIds):" | Write-Output
        $duplicates | Select-Object -First $MaxIds | ForEach-Object { "- $_" }
    }
    if ($incompleteNonCompliant.Count -gt 0) {
        "Incomplete non-compliant IDs (first $MaxIds):" | Write-Output
        $incompleteNonCompliant | Select-Object -First $MaxIds | ForEach-Object { "- $_" }
    }
}
