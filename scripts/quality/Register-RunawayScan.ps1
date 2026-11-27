<#
.SYNOPSIS
    Register (or remove) the scheduled runaway-process scan for the current user.

.DESCRIPTION
    Runs `runaway-processes.py notify` every -IntervalMinutes while you are signed in. Each run scans for processes that
    Claude Code sessions left running and shows a Windows notification once for each new spinning tree (safe-to-stop,
    likely-leaked, check). It never stops anything: stopping stays a `kill` on the user's yes. The same scan refreshes
    the report the SessionStart banner shows, and scans at least 15 minutes apart let it catch processes that are hot
    now but have a low lifetime average (.claude/FAILURE-MODES.md G-26).

    The task runs pythonw.exe (no console window) as you, only while you are signed in, at most one run at a time,
    each limited to 5 minutes. By default it runs the scanner from the repository's main checkout (the first
    `git worktree list` entry), so removing a worktree never breaks it; that checkout needs the script (master).

.PARAMETER IntervalMinutes
    Minutes between scans (5-120, default 15).

.PARAMETER ScriptPath
    The runaway-processes.py to run. Default: scripts\quality\runaway-processes.py in the main checkout.

.PARAMETER Unregister
    Remove the task.

.EXAMPLE
    .\scripts\quality\Register-RunawayScan.ps1
.EXAMPLE
    .\scripts\quality\Register-RunawayScan.ps1 -Unregister
#>
[CmdletBinding()]
param(
    [ValidateRange(5, 120)] [int] $IntervalMinutes = 15,
    [string] $ScriptPath,
    [switch] $Unregister
)

$ErrorActionPreference = 'Stop'
$TaskPath = '\Spaarke\'
$TaskName = 'RunawayProcessScan'

if ($Unregister) {
    if (Get-ScheduledTask -TaskPath $TaskPath -TaskName $TaskName -ErrorAction SilentlyContinue) {
        Unregister-ScheduledTask -TaskPath $TaskPath -TaskName $TaskName -Confirm:$false
        Write-Host "Removed scheduled task $TaskPath$TaskName."
    } else {
        Write-Host "Scheduled task $TaskPath$TaskName is not registered."
    }
    return
}

if (-not $ScriptPath) {
    $main = (git -C $PSScriptRoot worktree list --porcelain | Select-Object -First 1) -replace '^worktree\s+', ''
    if (-not $main) { throw "Could not find the main checkout with 'git worktree list'; pass -ScriptPath." }
    $ScriptPath = Join-Path $main 'scripts\quality\runaway-processes.py'
}
$ScriptPath = [IO.Path]::GetFullPath($ScriptPath)
if (-not (Test-Path -LiteralPath $ScriptPath)) {
    throw "$ScriptPath does not exist. Update that checkout to master first, or pass -ScriptPath."
}
if (-not (Select-String -LiteralPath $ScriptPath -Pattern 'def cmd_notify' -Quiet)) {
    throw "$ScriptPath has no 'notify' command (an older version). Update that checkout to master first, or pass -ScriptPath."
}

# The real interpreter's pythonw.exe, not the Microsoft Store alias in WindowsApps (that stub can hang; G-26).
$python = Get-Command python -CommandType Application -ErrorAction SilentlyContinue |
    Where-Object { $_.Source -notmatch '\\WindowsApps\\' } | Select-Object -First 1
if (-not $python) { throw 'No python.exe found on PATH outside WindowsApps.' }
$pythonw = & $python.Source -c "import os, sys; print(os.path.join(os.path.dirname(sys.executable), 'pythonw.exe'))"
if (-not (Test-Path -LiteralPath $pythonw)) { throw "pythonw.exe not found next to $($python.Source)." }

$action = New-ScheduledTaskAction -Execute $pythonw -Argument "`"$ScriptPath`" notify" `
    -WorkingDirectory (Split-Path (Split-Path (Split-Path $ScriptPath)))
$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) `
    -RepetitionInterval (New-TimeSpan -Minutes $IntervalMinutes)
$settings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 5) `
    -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -Hidden
$principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited

Register-ScheduledTask -TaskPath $TaskPath -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings `
    -Principal $principal -Description 'Scans for processes Claude Code sessions left running and notifies once per new spinning tree. Never stops anything. Source: scripts/quality/Register-RunawayScan.ps1' `
    -Force | Out-Null

Write-Host "Registered $TaskPath$TaskName every $IntervalMinutes min (first run in about 1 min)."
Write-Host "  runs: $pythonw `"$ScriptPath`" notify"
Write-Host "  remove: .\scripts\quality\Register-RunawayScan.ps1 -Unregister"
