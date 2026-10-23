# Publish-size delta vs a fresh master build (root CLAUDE.md section 10) + CVE check.
# Short C:\tmp paths avoid MAX_PATH. Usage: pwsh publish-delta.ps1 -wt <worktree> -MasterSha <sha> -BranchSha <sha>
param([Parameter(Mandatory)][string]$wt, [Parameter(Mandatory)][string]$MasterSha, [Parameter(Mandatory)][string]$BranchSha)
$ErrorActionPreference = 'Stop'
git -C $wt worktree add --detach C:\tmp\hpdm $MasterSha 2>&1 | Out-Null
git -C $wt worktree add --detach C:\tmp\hpdb $BranchSha 2>&1 | Out-Null
$r = @{}
foreach ($side in 'hpdm', 'hpdb') {
  $out = "C:\tmp\$side-pub"
  if (Test-Path $out) { Remove-Item -Recurse -Force $out }
  dotnet publish "C:\tmp\$side\src\server\api\Sprk.Bff.Api\Sprk.Bff.Api.csproj" -c Release -o $out -nologo -v q 2>&1 | Select-String ' error |Build succeeded' | Select-Object -First 3
  $zip = "C:\tmp\$side-pub.zip"
  if (Test-Path $zip) { Remove-Item -Force $zip }
  Compress-Archive -Path "$out\*" -DestinationPath $zip -CompressionLevel Optimal
  $files = (Get-ChildItem $out -Recurse -File).Count
  $bytes = (Get-Item $zip).Length
  $r[$side] = $bytes
  "$side zip=$bytes files=$files commit=$(git -C C:\tmp\$side rev-parse --short HEAD)"
}
"delta=$($r['hpdb'] - $r['hpdm']) bytes; total MB=$([math]::Round($r['hpdb']/1MB,2))"
dotnet list "C:\tmp\hpdb\src\server\api\Sprk.Bff.Api\Sprk.Bff.Api.csproj" package --vulnerable --include-transitive 2>&1 | Select-String 'vulnerable|High|Critical'
foreach ($side in 'hpdm','hpdb') { Remove-Item -Recurse -Force "C:\tmp\$side-pub", "C:\tmp\$side-pub.zip" -ErrorAction SilentlyContinue; & (Join-Path $wt "scripts\worktree\Remove-WorktreeSafe.ps1") -Path "C:\tmp\$side" 2>&1 | Select-Object -Last 1 }

