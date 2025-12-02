# Generate a strong name key file
$keyPath = Join-Path $PSScriptRoot "SpaarkePlugins.snk"

# Create RSA key pair
$rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider(1024)
$keyPair = $rsa.ExportCspBlob($true)

# Write to SNK file
[System.IO.File]::WriteAllBytes($keyPath, $keyPair)

Write-Host "Strong name key generated at: $keyPath" -ForegroundColor Green