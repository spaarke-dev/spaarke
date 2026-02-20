$source = 'c:\code_files\spaarke-wt-home-corporate-workspace-r1\deploy\api-publish'
$dest = 'c:\code_files\spaarke-wt-home-corporate-workspace-r1\deploy\publish.zip'

if (Test-Path $dest) { Remove-Item $dest -Force }

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($source, $dest, [System.IO.Compression.CompressionLevel]::Optimal, $false)

$fileInfo = Get-Item $dest
Write-Output "Created: $dest"
Write-Output "Size: $([math]::Round($fileInfo.Length / 1MB, 1)) MB"

# Verify critical files
$zip = [System.IO.Compression.ZipFile]::OpenRead($dest)
Write-Output "Total entries: $($zip.Entries.Count)"

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

# Also copy to the location the user expects
$altDest = 'c:\code_files\spaarke-wt-home-corporate-workspace-r1\src\server\api\Sprk.Bff.Api\publish.zip'
Copy-Item $dest $altDest -Force
Write-Output "Also copied to: $altDest"
