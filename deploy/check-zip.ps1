Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead('c:\code_files\spaarke-wt-home-corporate-workspace-r1\deploy\publish.zip')
foreach ($entry in $zip.Entries) {
    if ($entry.FullName -like 'Sprk*' -or $entry.FullName -eq 'web.config' -or $entry.FullName -like '*.runtimeconfig*' -or $entry.FullName -like '*.deps*') {
        Write-Output "$($entry.FullName) ($($entry.Length) bytes)"
    }
}
$zip.Dispose()
