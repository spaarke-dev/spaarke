---
description: Clean up local development environment caches (Azure CLI, NuGet, npm, Git credentials) to resolve authentication and performance issues
tags: [maintenance, cleanup, cache, azure-cli, nuget, npm, git, dev-environment]
techStack: [powershell, azure-cli, dotnet, npm, git]
appliesTo: ["clean up dev", "clear caches", "fix auth issues", "azure cli issues", "dev environment cleanup"]
alwaysApply: false
exemplar: none-too-volatile
last-reviewed: 2026-05-16
---

# dev-cleanup

> **Category**: Operations / Maintenance
> **Last Reviewed**: 2026-05-16
> **Reviewed By**: ai-procedure-quality-r1 (Phase 2b Wave 2b-A)
> **Exemplar rationale**: Cleanup runs against per-machine ephemeral state; no canonical reference applies.
> **Inventory anomaly #6 RESOLVED**: Frontmatter was minimal (description + alwaysApply only) before 2026-05-16. Tags, techStack, and appliesTo now added.

---

## Purpose

Cleans up various cached credentials and artifacts on a local development machine that can cause issues over time:

- **Azure CLI** - Multi-tenant/subscription confusion, stale tokens
- **Azure PowerShell** - Cached context issues
- **NuGet** - Large cache sizes, corrupted packages
- **npm** - Cache bloat, dependency resolution issues
- **Git Credential Manager** - Stale GitHub credentials
- **Power Platform CLI** - Auth token issues

---

## Applies When

- User reports Azure CLI issues ("multiple tenants", "wrong subscription", auth failures)
- User reports authentication issues with any Azure/GitHub/Dataverse service
- User asks to "clean up dev environment", "clear caches", "fix auth issues"
- Periodic maintenance (recommend: monthly or when issues arise)
- Explicitly invoked with `/dev-cleanup`

**NOT applicable when:**
- User needs to troubleshoot a specific service (diagnose first)
- User is in the middle of a deployment (could interrupt auth)

---

## Quick Reference

### Run Full Cleanup (Interactive)

```powershell
.\scripts\maintenance\Clean-DevEnvironment.ps1
```

This will:
1. Clear all caches
2. Prompt for re-authentication (Azure CLI, Azure PowerShell)
3. Report space recovered

### Run Specific Cleanups

```powershell
# Azure CLI only (most common fix)
.\scripts\maintenance\Clean-DevEnvironment.ps1 -AzureCli

# Package caches only (no auth prompts)
.\scripts\maintenance\Clean-DevEnvironment.ps1 -NuGet -Npm -SkipReauth

# See what would be cleaned without making changes
.\scripts\maintenance\Clean-DevEnvironment.ps1 -DryRun
```

### Available Flags

| Flag | Description |
|------|-------------|
| `-All` | Run all cleanup operations (default) |
| `-AzureCli` | Clear Azure CLI cache and re-authenticate |
| `-AzurePowerShell` | Clear Azure PowerShell context |
| `-NuGet` | Clear NuGet package cache |
| `-Npm` | Clear npm cache |
| `-DotNet` | Clear .NET workload cache |
| `-Git` | Clear Git credential manager entries |
| `-Pac` | Clear Power Platform CLI auth |
| `-DryRun` | Show what would be cleaned without changes |
| `-SkipReauth` | Skip re-authentication prompts |

---

## Workflow

### Step 1: Diagnose the Issue

Before running cleanup, identify what's wrong:

```
IF user reports "multiple tenants" or "wrong subscription":
  → Focus on Azure CLI cleanup (-AzureCli)

IF user reports "authentication failed" for Azure:
  → Try Azure CLI cleanup first
  → If persists, also clear Azure PowerShell

IF user reports slow builds or disk space issues:
  → Focus on package caches (-NuGet -Npm)

IF user reports GitHub auth issues:
  → Focus on Git credentials (-Git)

IF user reports Dataverse/PAC issues:
  → Focus on PAC cleanup (-Pac)
```

### Step 2: Run Appropriate Cleanup

**Option A: Targeted Cleanup (Recommended)**

```powershell
# For Azure CLI issues (most common)
.\scripts\maintenance\Clean-DevEnvironment.ps1 -AzureCli
```

**Option B: Full Cleanup**

```powershell
# When multiple issues or periodic maintenance
.\scripts\maintenance\Clean-DevEnvironment.ps1
```

**Option C: Dry Run First**

```powershell
# See what would be cleaned
.\scripts\maintenance\Clean-DevEnvironment.ps1 -DryRun

# Then run for real if satisfied
.\scripts\maintenance\Clean-DevEnvironment.ps1
```

### Step 3: Verify Resolution

After cleanup:

```powershell
# Verify Azure CLI
az account show

# Verify Azure PowerShell (if used)
Get-AzContext

# Verify PAC (if used)
pac auth list
```

---

## Common Issues and Solutions

### Azure CLI: "Multiple tenants with same user"

**Symptoms:**
- `az` commands fail with tenant selection errors
- Wrong subscription selected by default
- Token refresh failures

**Solution:**
```powershell
.\scripts\maintenance\Clean-DevEnvironment.ps1 -AzureCli
```

After re-login, set your default subscription:
```powershell
az account set --subscription "your-subscription-name"
```

### NuGet: "Package restore failed" or large cache

**Symptoms:**
- Build failures with package errors
- `obj` folder corruption
- Disk space warnings

**Solution:**
```powershell
.\scripts\maintenance\Clean-DevEnvironment.ps1 -NuGet

# Then restore packages
dotnet restore
```

### Git: "Authentication failed for GitHub"

**Symptoms:**
- Push/pull fails with 401/403
- Credential prompt loops

**Solution:**
```powershell
.\scripts\maintenance\Clean-DevEnvironment.ps1 -Git

# Next git operation will prompt for new credentials
git fetch origin
```

### PAC: "Auth profile expired"

**Symptoms:**
- `pac` commands fail with auth errors
- Token refresh failures

**Solution:**
```powershell
.\scripts\maintenance\Clean-DevEnvironment.ps1 -Pac

# Re-authenticate
pac auth create --environment "https://yourorg.crm.dynamics.com"
```

---

## What Gets Cleaned

### Azure CLI (`-AzureCli`)

| Item | Path | Purpose |
|------|------|---------|
| MSAL token cache | `~/.azure/msal_token_cache.*` | Cached auth tokens |
| Azure profile | `~/.azure/azureProfile.json` | Account/subscription list |
| Session file | `~/.azure/az.sess` | Session state |
| Config file | `~/.azure/az.json` | CLI configuration |
| Command cache | `~/.azure/commands/` | Cached command metadata |

### NuGet (`-NuGet`)

| Cache | Typical Size | Purpose |
|-------|--------------|---------|
| http-cache | 100-500 MB | Downloaded package metadata |
| global-packages | 1-10 GB | Extracted NuGet packages |
| temp | 10-100 MB | Temporary download files |
| plugins-cache | 1-10 MB | NuGet plugin cache |

### npm (`-Npm`)

| Cache | Typical Size | Purpose |
|-------|--------------|---------|
| npm cache | 500 MB - 2 GB | Downloaded packages and metadata |

### Git (`-Git`)

| Item | Location | Purpose |
|------|----------|---------|
| GitHub credentials | Windows Credential Manager | Stored auth tokens |

---

## Periodic Maintenance Schedule

| Frequency | Recommended Cleanup |
|-----------|---------------------|
| Weekly | None (only when issues arise) |
| Monthly | `-NuGet -Npm` (package caches) |
| Quarterly | Full cleanup (`-All`) |
| When issues occur | Targeted cleanup based on symptoms |

---

## Manual Cleanup Commands

If the script isn't available, here are the manual commands:

### Azure CLI
```powershell
az logout --all
Remove-Item -Path "$env:USERPROFILE\.azure\msal_token_cache.*" -Force -ErrorAction SilentlyContinue
Remove-Item -Path "$env:USERPROFILE\.azure\azureProfile.json" -Force -ErrorAction SilentlyContinue
az login
az account set --subscription "your-subscription"
```

### NuGet
```powershell
dotnet nuget locals all --clear
```

### npm
```powershell
npm cache clean --force
```

### Git Credentials
```powershell
# List credentials
cmdkey /list | Select-String "git"

# Delete specific entry
cmdkey /delete:git:https://github.com
```

### Power Platform CLI
```powershell
pac auth clear
pac auth create --environment "https://yourorg.crm.dynamics.com"
```

---

## Related Skills

- **azure-deploy** - May need cleanup before deployment if auth issues
- **dataverse-deploy** - PAC cleanup helps with Dataverse auth issues
- **push-to-github** - Git cleanup helps with GitHub auth issues

---

## Tips for AI

- Always ask what specific issue the user is experiencing before running full cleanup
- Recommend `-DryRun` first if user is unsure
- After Azure CLI cleanup, remind user to set their default subscription
- Package cache cleanup (`-NuGet -Npm`) is safe and doesn't require re-auth
- Warn user that cleanup will close active Azure sessions

---

## Failure Modes & Recovery

| Failure | Cause | Prevention / Recovery |
|---|---|---|
| Azure CLI cleanup completed but `az login` still fails | Active Azure session in another shell window held a lock OR token cache directory permissions broken | Close ALL shells using Azure CLI before cleanup. If permissions broken: `rm -rf ~/.azure` (Linux/macOS) or `Remove-Item -Recurse -Force $env:USERPROFILE\.azure` (Windows), then `az login` fresh. |
| NuGet cleanup wiped restore caches; `dotnet build` now takes 10 minutes | Expected behavior — cache cleared means re-download. First build pays the cost; subsequent builds are fast | Don't run `-NuGet` cleanup before time-sensitive builds. For incremental cleanup use `dotnet nuget locals temp -c` only (not all caches). |
| npm cleanup didn't fix the issue — same error returns | Issue isn't in npm cache; it's in `node_modules/` or `package-lock.json` drift | Stop running broader cache cleanups; instead do `rm -rf node_modules package-lock.json && npm install --legacy-peer-deps` (per root CLAUDE.md Vite-solutions note). |
| Git credential cache cleanup logged user out of every repo | Expected behavior — credential manager wipes all stored creds | Re-authenticate to each remote individually. Document this expectation up front to user when running `-Git`. |
| User runs cleanup but root cause was upstream (e.g., expired Azure AD password) | Cleanup masked the real issue temporarily | Always diagnose ROOT CAUSE first ("what specific error are you seeing?") before recommending cleanup. Cleanup is a tool, not a diagnosis. |
