$user = '$spe-api-dev-67e2xz'
$pass = 'v12hPuy9aQ2BCNnJ9RMBcBimy8fzlthJWsoCwypt6QL8cRp2hjEJ8hpj37px'
$pair = "${user}:${pass}"
$bytes = [Text.Encoding]::ASCII.GetBytes($pair)
$base64 = [Convert]::ToBase64String($bytes)
$headers = @{ Authorization = "Basic $base64" }

Write-Output "=== Most Recent stdout log (stdout_20260218175200_8640.log) ==="
$content = Invoke-RestMethod -Uri 'https://spe-api-dev-67e2xz.scm.azurewebsites.net/api/vfs/site/wwwroot/logs/stdout_20260218175200_8640.log' -Headers $headers
Write-Output $content
