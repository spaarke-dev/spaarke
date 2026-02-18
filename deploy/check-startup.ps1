$user = '$spe-api-dev-67e2xz'
$pass = 'v12hPuy9aQ2BCNnJ9RMBcBimy8fzlthJWsoCwypt6QL8cRp2hjEJ8hpj37px'
$pair = "${user}:${pass}"
$bytes = [Text.Encoding]::ASCII.GetBytes($pair)
$base64 = [Convert]::ToBase64String($bytes)
$headers = @{ Authorization = "Basic $base64" }

# List most recent stdout logs
$logFiles = Invoke-RestMethod -Uri 'https://spe-api-dev-67e2xz.scm.azurewebsites.net/api/vfs/site/wwwroot/logs/' -Headers $headers
$latest = ($logFiles | Sort-Object mtime -Descending)[0]
Write-Output "=== Latest stdout: $($latest.name) (size=$($latest.size)) ==="

$content = Invoke-RestMethod -Uri $latest.href -Headers $headers
# Show last 60 lines
$lines = $content -split "`n"
$start = [Math]::Max(0, $lines.Count - 60)
$lines[$start..($lines.Count - 1)] | ForEach-Object { Write-Output $_ }
