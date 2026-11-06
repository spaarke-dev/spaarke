<#
.SYNOPSIS
    Before a dev BFF deploy: records what is live and saves a restorable snapshot of the App Service's wwwroot.

.DESCRIPTION
    Read-only against Azure. Steps:
      1. Shows the signed-in az account, the App Service state and runtime.
      2. Lists the last three Kudu deployments and the live Sprk.Bff.Api.dll ProductVersion (it carries the commit SHA).
      3. Downloads wwwroot as a zip into <RepoRoot>/deploy/rollback/ (gitignored) and expands it to
         deploy/rollback/wwwroot-<UTC timestamp>, then prints its file count and the snapshot DLL's ProductVersion.
      4. Prints /healthz.

    Restore the snapshot with:
      scripts/Deploy-BffApi.ps1 -Environment dev -SkipBuild -PublishPath deploy/rollback/wwwroot-<timestamp>
    (a live change: owner approval first; procedure: docs/procedures/dev-bff-deploy-coordination.md).

    The access token is held in memory only and never printed.

.PARAMETER RepoRoot
    The worktree that keeps the snapshot. Default: the current git top-level. The snapshot stays with this worktree, so
    keep the worktree until the next deploy has passed its gate.

.EXAMPLE
    pwsh -File scripts/Save-BffDevRollbackSnapshot.ps1
#>
param(
    [string]$RepoRoot = (git rev-parse --show-toplevel),
    [string]$ResourceGroup = 'rg-spaarke-dev',
    [string]$AppName = 'spaarke-bff-dev'
)
$ErrorActionPreference = 'Stop'

az account show --query "{sub:name, tenant:tenantId}" -o table
az webapp show -g $ResourceGroup -n $AppName --query "{state:state, runtime:siteConfig.linuxFxVersion}" -o table

$tok = az account get-access-token --query accessToken -o tsv
$h = @{ Authorization = "Bearer $tok" }
$scm = "https://$AppName.scm.azurewebsites.net"

$d = Invoke-RestMethod -Headers $h -Uri "$scm/api/deployments"
$d | Sort-Object received_time -Descending | Select-Object -First 3 received_time, author, deployer,
    @{ n = 'msg'; e = { ($_.message -split "`n")[0] } } | Format-Table -AutoSize

$dll = Join-Path ([System.IO.Path]::GetTempPath()) "live-$([guid]::NewGuid()).dll"
Invoke-WebRequest -Headers $h -Uri "$scm/api/vfs/site/wwwroot/Sprk.Bff.Api.dll" -OutFile $dll
"LIVE ProductVersion: " + [System.Diagnostics.FileVersionInfo]::GetVersionInfo($dll).ProductVersion
Remove-Item $dll

$ts = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')
$zip = Join-Path $RepoRoot "deploy/rollback/$AppName-wwwroot-$ts.zip"
$dir = Join-Path $RepoRoot "deploy/rollback/wwwroot-$ts"
New-Item -ItemType Directory -Force (Split-Path $zip) | Out-Null
Invoke-WebRequest -Headers $h -Uri "$scm/api/zip/site/wwwroot/" -OutFile $zip
Expand-Archive -Path $zip -DestinationPath $dir
$files = Get-ChildItem $dir -Recurse -File
"SNAPSHOT $dir files=$($files.Count) MB=$([math]::Round(($files | Measure-Object Length -Sum).Sum / 1MB, 2))"
"SNAPSHOT DLL ProductVersion: " + [System.Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $dir 'Sprk.Bff.Api.dll')).ProductVersion
"healthz: " + (curl.exe -s -o NUL -w "%{http_code}" "https://$AppName.azurewebsites.net/healthz")
