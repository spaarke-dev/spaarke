<#
.SYNOPSIS
    After a dev BFF deploy: confirms which build is live and that it answers.

.DESCRIPTION
    Read-only. Prints the live Sprk.Bff.Api.dll ProductVersion (it carries the commit SHA; compare it with the SHA the
    owner approved), then the status codes of /healthz (expect 200), /healthz/dataverse (expect 200) and an anonymous
    /api/me (expect 401: auth is enforced). The access token is held in memory only and never printed.
    Procedure: docs/procedures/dev-bff-deploy-coordination.md.

.EXAMPLE
    pwsh -File scripts/Test-BffDevDeployReadback.ps1
#>
param(
    [string]$AppName = 'spaarke-bff-dev'
)
$ErrorActionPreference = 'Stop'

$tok = az account get-access-token --query accessToken -o tsv
$h = @{ Authorization = "Bearer $tok" }
$dll = Join-Path ([System.IO.Path]::GetTempPath()) "readback-$([guid]::NewGuid()).dll"
Invoke-WebRequest -Headers $h -Uri "https://$AppName.scm.azurewebsites.net/api/vfs/site/wwwroot/Sprk.Bff.Api.dll" -OutFile $dll
"LIVE ProductVersion: " + [System.Diagnostics.FileVersionInfo]::GetVersionInfo($dll).ProductVersion + "  at " +
    (Get-Date).ToUniversalTime().ToString('HH:mm:ssZ')
Remove-Item $dll

$b = "https://$AppName.azurewebsites.net"
"healthz " + (curl.exe -s -o NUL -w "%{http_code}" "$b/healthz")
"healthz/dataverse " + (curl.exe -s -o NUL -w "%{http_code}" "$b/healthz/dataverse")
"api/me anon " + (curl.exe -s -o NUL -w "%{http_code}" "$b/api/me")
