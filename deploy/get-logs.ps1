$user = '$spe-api-dev-67e2xz'
$pass = 'v12hPuy9aQ2BCNnJ9RMBcBimy8fzlthJWsoCwypt6QL8cRp2hjEJ8hpj37px'
$pair = "${user}:${pass}"
$bytes = [Text.Encoding]::ASCII.GetBytes($pair)
$base64 = [Convert]::ToBase64String($bytes)
$headers = @{ Authorization = "Basic $base64" }

# List the LogFiles directory for error-related logs
Write-Output "=== LogFiles Directory (filtered) ==="
$response = Invoke-RestMethod -Uri 'https://spe-api-dev-67e2xz.scm.azurewebsites.net/api/vfs/LogFiles/' -Headers $headers
$response | Where-Object { $_.name -like 'eventlog*' -or $_.name -like 'stderr*' -or $_.name -like '*error*' -or $_.name -like 'W3SVC*' -or $_.name -like 'DetailedErrors' } | Sort-Object mtime -Descending | Select-Object -First 10 name, size, mtime | Format-Table

# Get eventlog.xml if it exists
Write-Output "=== eventlog.xml ==="
try {
    $eventlog = Invoke-RestMethod -Uri 'https://spe-api-dev-67e2xz.scm.azurewebsites.net/api/vfs/LogFiles/eventlog.xml' -Headers $headers
    $eventlog | Select-String -Pattern 'Error|Exception|500\.30|failed' -Context 2 | Select-Object -Last 20
} catch {
    Write-Output "Could not fetch eventlog.xml: $($_.Exception.Message)"
}

# Check DetailedErrors directory
Write-Output "=== DetailedErrors ==="
try {
    $errors = Invoke-RestMethod -Uri 'https://spe-api-dev-67e2xz.scm.azurewebsites.net/api/vfs/LogFiles/DetailedErrors/' -Headers $headers
    $errors | Sort-Object mtime -Descending | Select-Object -First 5 name, size, mtime | Format-Table

    # Fetch most recent error
    if ($errors.Count -gt 0) {
        $latest = ($errors | Sort-Object mtime -Descending)[0]
        Write-Output "=== Latest Error File: $($latest.name) ==="
        $content = Invoke-RestMethod -Uri $latest.href -Headers $headers
        # Extract just the key error info
        $content -replace '<[^>]+>', '' | Select-String -Pattern 'Error|Exception|500|failed|asp' -Context 1 | Select-Object -Last 20
    }
} catch {
    Write-Output "Could not fetch DetailedErrors: $($_.Exception.Message)"
}

# Check if stdout logs exist at wwwroot/logs/
Write-Output "=== wwwroot/logs/ (stdout) ==="
try {
    $stdoutLogs = Invoke-RestMethod -Uri 'https://spe-api-dev-67e2xz.scm.azurewebsites.net/api/vfs/site/wwwroot/logs/' -Headers $headers
    $stdoutLogs | Sort-Object mtime -Descending | Select-Object -First 5 name, size, mtime | Format-Table
} catch {
    Write-Output "No stdout logs directory: $($_.Exception.Message)"
}

# Check if web.config is actually deployed
Write-Output "=== Deployed web.config ==="
try {
    $webconfig = Invoke-RestMethod -Uri 'https://spe-api-dev-67e2xz.scm.azurewebsites.net/api/vfs/site/wwwroot/web.config' -Headers $headers
    Write-Output $webconfig
} catch {
    Write-Output "Could not fetch web.config: $($_.Exception.Message)"
}
