# WS-5 spike (task 050) — local, one-time desktop-Word ground-truth PDF export for measurement ONLY.
# NOT a production automation path. Not linked into BFF. Not a server automation service.
# Uses the dev machine's already-licensed local Word install to export each corpus .docx to PDF,
# exactly as a human tester opening Word and choosing "Save As PDF" would do, so the LibreOffice
# headless PDF can be compared against Word's own pagination. Script exits and quits Word regardless
# of outcome (try/finally) to avoid leaving an orphaned WINWORD.EXE process.

$corpusDir = "C:\code_files\spaarke-wt-spaarkeai-compose-fidelity-r4.5\tests\fixtures\compose-corpus"
$outDir = "C:\code_files\spaarke-wt-spaarkeai-compose-fidelity-r4.5\projects\spaarkeai-compose-fidelity-r4.5\notes\ws5-prototype\pdf-out-word"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$word = $null
try {
    $word = New-Object -ComObject Word.Application
    $word.Visible = $false
    $word.DisplayAlerts = 0  # wdAlertsNone
    $word.AutomationSecurity = 3  # msoAutomationSecurityForceDisable — no macros

    $files = Get-ChildItem -Path $corpusDir -Filter "*.docx"
    foreach ($f in $files) {
        $outPath = Join-Path $outDir ($f.BaseName + ".pdf")
        Write-Output "=== $($f.Name) ==="
        try {
            $doc = $word.Documents.Open($f.FullName, $false, $true, $false)  # ReadOnly, ConfirmConversions=false
            # wdExportFormatPDF = 17
            $doc.ExportAsFixedFormat($outPath, 17)
            $pageCount = $doc.ComputeStatistics(2)  # wdStatisticPages = 2
            Write-Output "PAGES=$pageCount FILE=$($f.Name)"
            $doc.Close([ref]0)  # wdDoNotSaveChanges
        } catch {
            Write-Output "ERROR converting $($f.Name): $_"
        }
    }
} catch {
    Write-Output "FATAL: $_"
} finally {
    if ($word) {
        try { $word.Quit([ref]0) } catch {}
    }
    # Belt-and-suspenders: kill any orphaned WINWORD process spawned by this script
    Get-Process WINWORD -ErrorAction SilentlyContinue | Where-Object { $_.StartTime -gt (Get-Date).AddMinutes(-5) } | ForEach-Object {
        try { $_.CloseMainWindow() | Out-Null } catch {}
    }
}
Write-Output "DONE"
