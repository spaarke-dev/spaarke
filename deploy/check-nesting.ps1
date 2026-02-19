$base = 'C:\code_files\spaarke-wt-home-corporate-workspace-r1\src\solutions\LegalWorkspace\src'
foreach ($d in @('hooks','services','contexts','types','utils')) {
    $p = Join-Path $base $d
    $subdirs = Get-ChildItem $p -Directory -ErrorAction SilentlyContinue
    foreach ($sub in $subdirs) {
        Write-Output "$d/$($sub.Name)"
    }
}
