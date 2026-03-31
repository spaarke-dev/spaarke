<#
.SYNOPSIS
    Deploys all 7 notification playbooks to Dataverse.
.DESCRIPTION
    Calls Deploy-Playbook.ps1 for each notification playbook JSON definition.
.PARAMETER DryRun
    If set, shows what would be deployed without making changes.
.EXAMPLE
    .\Deploy-NotificationPlaybooks.ps1
    .\Deploy-NotificationPlaybooks.ps1 -DryRun
#>
[CmdletBinding()]
param(
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$playbookDir = Join-Path $scriptDir '..\projects\spaarke-daily-update-service\notes\playbooks'
$deployScript = Join-Path $scriptDir 'Deploy-Playbook.ps1'

$playbooks = @(
    'notification-tasks-overdue.json',
    'notification-tasks-due-soon.json',
    'notification-new-documents.json',
    'notification-new-emails.json',
    'notification-new-events.json',
    'notification-matter-activity.json',
    'notification-work-assignments.json'
)

Write-Host "`n=== Deploying Notification Playbooks ===" -ForegroundColor Cyan
Write-Host "  Source: $playbookDir" -ForegroundColor Gray
Write-Host "  Count:  $($playbooks.Count)" -ForegroundColor Gray
if ($DryRun) { Write-Host "  Mode:   DRY RUN" -ForegroundColor Yellow }
Write-Host ""

$succeeded = 0
$failed = 0

foreach ($file in $playbooks) {
    $path = Join-Path $playbookDir $file
    if (-not (Test-Path $path)) {
        Write-Host "  SKIP: $file (not found)" -ForegroundColor Yellow
        $failed++
        continue
    }

    Write-Host "  Deploying: $file" -ForegroundColor White
    try {
        $args = @('-PlaybookDefinitionPath', $path)
        if ($DryRun) { $args += '-DryRun' }
        & $deployScript @args
        $succeeded++
        Write-Host "  OK: $file" -ForegroundColor Green
    } catch {
        $failed++
        Write-Host "  FAILED: $file - $($_.Exception.Message)" -ForegroundColor Red
    }
    Write-Host ""
}

Write-Host "=== Deployment Complete ===" -ForegroundColor Cyan
Write-Host "  Succeeded: $succeeded / $($playbooks.Count)" -ForegroundColor $(if ($failed -eq 0) { 'Green' } else { 'Yellow' })
if ($failed -gt 0) {
    Write-Host "  Failed:    $failed" -ForegroundColor Red
}
