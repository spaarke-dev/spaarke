Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipPath = 'c:\code_files\spaarke-wt-home-corporate-workspace-r1\deploy\publish.zip'
$fileInfo = Get-Item $zipPath
Write-Output "Zip size: $([math]::Round($fileInfo.Length / 1MB, 1)) MB"

$zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
$totalEntries = $zip.Entries.Count
Write-Output "Total entries: $totalEntries"

# Check for critical files
$critical = @('Sprk.Bff.Api.dll', 'Sprk.Bff.Api.deps.json', 'Sprk.Bff.Api.runtimeconfig.json', 'web.config')
foreach ($name in $critical) {
    $found = $zip.Entries | Where-Object { $_.FullName -eq $name }
    if ($found) {
        Write-Output "OK: $name ($($found.Length) bytes)"
    } else {
        Write-Output "MISSING: $name"
    }
}

# Check for nested publish directories (should be NONE)
$nested = $zip.Entries | Where-Object { $_.FullName -like 'publish*' -or $_.FullName -like 'publish-output*' }
if ($nested) {
    Write-Output "WARNING: Found $($nested.Count) nested publish entries"
} else {
    Write-Output "OK: No nested publish artifacts"
}

$zip.Dispose()
