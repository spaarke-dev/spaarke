<#
.SYNOPSIS
    Cleans up local development environment caches to resolve authentication and performance issues.

.DESCRIPTION
    This script clears various cached credentials and artifacts that can cause issues on a development machine:
    - Azure CLI cached tokens and accounts (fixes multi-tenant/subscription issues)
    - Azure PowerShell context
    - NuGet package cache
    - npm cache
    - .NET workload cache
    - Git credential manager entries (optional)
    - Power Platform CLI auth (optional)

.PARAMETER All
    Run all cleanup operations (default behavior if no specific flags provided)

.PARAMETER AzureCli
    Clear Azure CLI cache and re-authenticate

.PARAMETER AzurePowerShell
    Clear Azure PowerShell context and re-authenticate

.PARAMETER NuGet
    Clear NuGet package cache

.PARAMETER Npm
    Clear npm cache

.PARAMETER DotNet
    Clear .NET workload cache

.PARAMETER Git
    Clear Git credential manager entries for GitHub

.PARAMETER Pac
    Clear Power Platform CLI authentication

.PARAMETER DryRun
    Show what would be cleaned without actually cleaning

.PARAMETER SkipReauth
    Skip re-authentication after clearing caches

.EXAMPLE
    .\Clean-DevEnvironment.ps1
    Runs all cleanup operations with re-authentication prompts

.EXAMPLE
    .\Clean-DevEnvironment.ps1 -AzureCli
    Only clears Azure CLI cache and re-authenticates

.EXAMPLE
    .\Clean-DevEnvironment.ps1 -All -DryRun
    Shows what would be cleaned without making changes

.EXAMPLE
    .\Clean-DevEnvironment.ps1 -NuGet -Npm -SkipReauth
    Clears package caches without any authentication prompts

.NOTES
    Author: Spaarke Development Team
    Last Updated: January 2026

    This script is also available as a Claude Code skill: /dev-cleanup
#>

[CmdletBinding()]
param(
    [switch]$All,
    [switch]$AzureCli,
    [switch]$AzurePowerShell,
    [switch]$NuGet,
    [switch]$Npm,
    [switch]$DotNet,
    [switch]$Git,
    [switch]$Pac,
    [switch]$DryRun,
    [switch]$SkipReauth
)

# If no specific flags, run all
if (-not ($AzureCli -or $AzurePowerShell -or $NuGet -or $Npm -or $DotNet -or $Git -or $Pac)) {
    $All = $true
}

$script:TotalSpaceRecovered = 0
$script:ErrorCount = 0

function Write-Header {
    param([string]$Title)
    Write-Host "`n" -NoNewline
    Write-Host "=" * 60 -ForegroundColor Cyan
    Write-Host "  $Title" -ForegroundColor Cyan
    Write-Host "=" * 60 -ForegroundColor Cyan
}

function Write-Step {
    param([string]$Message)
    Write-Host "  -> $Message" -ForegroundColor Yellow
}

function Write-Success {
    param([string]$Message)
    Write-Host "  [OK] $Message" -ForegroundColor Green
}

function Write-Error {
    param([string]$Message)
    Write-Host "  [ERROR] $Message" -ForegroundColor Red
    $script:ErrorCount++
}

function Write-Skip {
    param([string]$Message)
    Write-Host "  [SKIP] $Message" -ForegroundColor DarkGray
}

function Write-DryRun {
    param([string]$Message)
    Write-Host "  [DRY-RUN] Would: $Message" -ForegroundColor Magenta
}

function Get-FolderSize {
    param([string]$Path)
    if (Test-Path $Path) {
        $size = (Get-ChildItem -Path $Path -Recurse -Force -ErrorAction SilentlyContinue |
                 Measure-Object -Property Length -Sum -ErrorAction SilentlyContinue).Sum
        return [math]::Round($size / 1MB, 2)
    }
    return 0
}

function Remove-CacheFolder {
    param(
        [string]$Path,
        [string]$Description
    )

    if (Test-Path $Path) {
        $sizeMB = Get-FolderSize -Path $Path
        if ($DryRun) {
            Write-DryRun "Remove $Description ($sizeMB MB)"
        } else {
            try {
                Remove-Item -Path $Path -Recurse -Force -ErrorAction Stop
                Write-Success "Removed $Description ($sizeMB MB recovered)"
                $script:TotalSpaceRecovered += $sizeMB
            } catch {
                Write-Error "Failed to remove $Description : $_"
            }
        }
    } else {
        Write-Skip "$Description not found"
    }
}

function Remove-CacheFile {
    param(
        [string]$Path,
        [string]$Description
    )

    if (Test-Path $Path) {
        $sizeMB = [math]::Round((Get-Item $Path).Length / 1MB, 2)
        if ($DryRun) {
            Write-DryRun "Remove $Description ($sizeMB MB)"
        } else {
            try {
                Remove-Item -Path $Path -Force -ErrorAction Stop
                Write-Success "Removed $Description"
                $script:TotalSpaceRecovered += $sizeMB
            } catch {
                Write-Error "Failed to remove $Description : $_"
            }
        }
    }
}

# ============================================================
# AZURE CLI CLEANUP
# ============================================================
function Clear-AzureCliCache {
    Write-Header "Azure CLI Cache Cleanup"

    # Check if az is available
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
        Write-Skip "Azure CLI not installed"
        return
    }

    Write-Step "Logging out of all Azure CLI accounts..."
    if ($DryRun) {
        Write-DryRun "az logout --all"
    } else {
        try {
            az logout --all 2>$null
            Write-Success "Logged out of all accounts"
        } catch {
            Write-Skip "No accounts to log out"
        }
    }

    Write-Step "Clearing Azure CLI cache files..."
    $azureDir = "$env:USERPROFILE\.azure"

    Remove-CacheFile -Path "$azureDir\msal_token_cache.bin" -Description "MSAL token cache"
    Remove-CacheFile -Path "$azureDir\msal_token_cache.json" -Description "MSAL token cache (JSON)"
    Remove-CacheFile -Path "$azureDir\azureProfile.json" -Description "Azure profile"
    Remove-CacheFile -Path "$azureDir\az.sess" -Description "Azure session"
    Remove-CacheFile -Path "$azureDir\az.json" -Description "Azure config"
    Remove-CacheFile -Path "$azureDir\accessTokens.json" -Description "Access tokens (legacy)"
    Remove-CacheFolder -Path "$azureDir\commands" -Description "Azure CLI command cache"
    Remove-CacheFolder -Path "$azureDir\cliextensions" -Description "Azure CLI extension cache"

    if (-not $SkipReauth -and -not $DryRun) {
        Write-Step "Re-authenticating with Azure CLI..."
        Write-Host "`n  A browser window will open for authentication.`n" -ForegroundColor Cyan
        try {
            az login
            Write-Success "Azure CLI re-authenticated"

            # Show current account
            Write-Step "Current Azure CLI context:"
            az account show --output table
        } catch {
            Write-Error "Azure CLI re-authentication failed: $_"
        }
    }
}

# ============================================================
# AZURE POWERSHELL CLEANUP
# ============================================================
function Clear-AzurePowerShellCache {
    Write-Header "Azure PowerShell Cache Cleanup"

    # Check if Az module is available
    if (-not (Get-Module -ListAvailable -Name Az.Accounts -ErrorAction SilentlyContinue)) {
        Write-Skip "Azure PowerShell module not installed"
        return
    }

    Write-Step "Disconnecting Azure PowerShell accounts..."
    if ($DryRun) {
        Write-DryRun "Disconnect-AzAccount"
        Write-DryRun "Clear-AzContext -Force"
    } else {
        try {
            Disconnect-AzAccount -ErrorAction SilentlyContinue | Out-Null
            Clear-AzContext -Force -ErrorAction SilentlyContinue | Out-Null
            Write-Success "Azure PowerShell context cleared"
        } catch {
            Write-Skip "No Azure PowerShell context to clear"
        }
    }

    # Clear Azure PowerShell cache folder
    Remove-CacheFolder -Path "$env:USERPROFILE\.Azure" -Description "Azure PowerShell cache"

    if (-not $SkipReauth -and -not $DryRun) {
        Write-Step "Re-authenticating with Azure PowerShell..."
        try {
            Connect-AzAccount
            Write-Success "Azure PowerShell re-authenticated"
        } catch {
            Write-Error "Azure PowerShell re-authentication failed: $_"
        }
    }
}

# ============================================================
# NUGET CACHE CLEANUP
# ============================================================
function Clear-NuGetCache {
    Write-Header "NuGet Cache Cleanup"

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Skip ".NET SDK not installed"
        return
    }

    # Show current cache sizes
    Write-Step "Current NuGet cache sizes:"
    dotnet nuget locals all --list | ForEach-Object {
        $parts = $_ -split ": "
        if ($parts.Count -eq 2 -and (Test-Path $parts[1])) {
            $sizeMB = Get-FolderSize -Path $parts[1]
            Write-Host "     $($parts[0]): $sizeMB MB" -ForegroundColor DarkGray
        }
    }

    Write-Step "Clearing NuGet caches..."
    if ($DryRun) {
        Write-DryRun "dotnet nuget locals all --clear"
    } else {
        try {
            $output = dotnet nuget locals all --clear 2>&1
            Write-Success "NuGet caches cleared"
        } catch {
            Write-Error "Failed to clear NuGet cache: $_"
        }
    }
}

# ============================================================
# NPM CACHE CLEANUP
# ============================================================
function Clear-NpmCache {
    Write-Header "npm Cache Cleanup"

    if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
        Write-Skip "npm not installed"
        return
    }

    # Show current cache size
    $npmCachePath = npm config get cache 2>$null
    if ($npmCachePath -and (Test-Path $npmCachePath)) {
        $sizeMB = Get-FolderSize -Path $npmCachePath
        Write-Step "Current npm cache size: $sizeMB MB"
    }

    Write-Step "Clearing npm cache..."
    if ($DryRun) {
        Write-DryRun "npm cache clean --force"
    } else {
        try {
            npm cache clean --force 2>&1 | Out-Null
            Write-Success "npm cache cleared"
        } catch {
            Write-Error "Failed to clear npm cache: $_"
        }
    }
}

# ============================================================
# .NET WORKLOAD CLEANUP
# ============================================================
function Clear-DotNetWorkloadCache {
    Write-Header ".NET Workload Cache Cleanup"

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Skip ".NET SDK not installed"
        return
    }

    Write-Step "Cleaning .NET workloads..."
    if ($DryRun) {
        Write-DryRun "dotnet workload clean"
    } else {
        try {
            dotnet workload clean 2>&1 | Out-Null
            Write-Success ".NET workload cache cleaned"
        } catch {
            Write-Error "Failed to clean .NET workloads: $_"
        }
    }
}

# ============================================================
# GIT CREDENTIAL CLEANUP
# ============================================================
function Clear-GitCredentials {
    Write-Header "Git Credential Manager Cleanup"

    Write-Step "Listing Git credentials for GitHub..."
    $credentials = cmdkey /list 2>$null | Select-String "git:https://github.com"

    if ($credentials) {
        foreach ($cred in $credentials) {
            $target = ($cred -split "Target: ")[1]
            if ($target) {
                if ($DryRun) {
                    Write-DryRun "Remove credential: $target"
                } else {
                    try {
                        cmdkey /delete:$target 2>&1 | Out-Null
                        Write-Success "Removed credential: $target"
                    } catch {
                        Write-Error "Failed to remove credential: $target"
                    }
                }
            }
        }
    } else {
        Write-Skip "No GitHub credentials found in credential manager"
    }

    if (-not $SkipReauth -and -not $DryRun -and $credentials) {
        Write-Host "`n  Git will prompt for credentials on next GitHub operation.`n" -ForegroundColor Cyan
    }
}

# ============================================================
# POWER PLATFORM CLI CLEANUP
# ============================================================
function Clear-PacAuth {
    Write-Header "Power Platform CLI (PAC) Cleanup"

    if (-not (Get-Command pac -ErrorAction SilentlyContinue)) {
        Write-Skip "Power Platform CLI not installed"
        return
    }

    Write-Step "Clearing PAC authentication..."
    if ($DryRun) {
        Write-DryRun "pac auth clear"
    } else {
        try {
            pac auth clear 2>&1 | Out-Null
            Write-Success "PAC authentication cleared"
        } catch {
            Write-Error "Failed to clear PAC auth: $_"
        }
    }

    if (-not $SkipReauth -and -not $DryRun) {
        Write-Host "`n  Run 'pac auth create' to re-authenticate when needed.`n" -ForegroundColor Cyan
    }
}

# ============================================================
# MAIN EXECUTION
# ============================================================

Write-Host "`n"
Write-Host "====================================================" -ForegroundColor White
Write-Host "    Development Environment Cleanup Script" -ForegroundColor White
Write-Host "====================================================" -ForegroundColor White

if ($DryRun) {
    Write-Host "`n  [DRY-RUN MODE] No changes will be made`n" -ForegroundColor Magenta
}

# Execute requested cleanups
if ($All -or $AzureCli)       { Clear-AzureCliCache }
if ($All -or $AzurePowerShell) { Clear-AzurePowerShellCache }
if ($All -or $NuGet)          { Clear-NuGetCache }
if ($All -or $Npm)            { Clear-NpmCache }
if ($All -or $DotNet)         { Clear-DotNetWorkloadCache }
if ($All -or $Git)            { Clear-GitCredentials }
if ($All -or $Pac)            { Clear-PacAuth }

# Summary
Write-Host "`n"
Write-Host "====================================================" -ForegroundColor White
Write-Host "    Summary" -ForegroundColor White
Write-Host "====================================================" -ForegroundColor White

if ($DryRun) {
    Write-Host "`n  This was a dry run. No changes were made." -ForegroundColor Magenta
    Write-Host "  Run without -DryRun to perform the cleanup.`n" -ForegroundColor Magenta
} else {
    Write-Host "`n  Total space recovered: ~$script:TotalSpaceRecovered MB" -ForegroundColor Green

    if ($script:ErrorCount -gt 0) {
        Write-Host "  Errors encountered: $script:ErrorCount" -ForegroundColor Red
    } else {
        Write-Host "  All operations completed successfully!" -ForegroundColor Green
    }
    Write-Host ""
}
