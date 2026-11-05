<#
.SYNOPSIS
  After make_kit.py: open every generated .docx in Word desktop (validity check: revisions and comments
  counted), convert the PDF-only batch items (b14-b16) and remove their .docx so only the PDF is uploaded.
#>
$ErrorActionPreference = 'Stop'
$poc = Split-Path -Parent $PSScriptRoot
$word = New-Object -ComObject Word.Application
$word.Visible = $false
$word.DisplayAlerts = 0
try {
  $files = Get-ChildItem -Recurse -File -Filter *.docx (Join-Path $poc 'kit'), (Join-Path $poc 'kit-held')
  foreach ($f in $files) {
    $d = $word.Documents.Open($f.FullName, $false, $true)   # read-only
    $rev = $d.Revisions.Count; $com = $d.Comments.Count; $pages = $d.ComputeStatistics(2)
    $rel = $f.FullName.Substring($poc.Length + 1)
    "{0,-110} pages={1} revisions={2} comments={3}" -f $rel, $pages, $rev, $com
    if ($f.Name -match '^b1[456] ') {
      $pdf = [IO.Path]::ChangeExtension($f.FullName, '.pdf')
      $d.SaveAs2([ref]$pdf, [ref]17)
      $d.Close([ref]0)
      Remove-Item -LiteralPath $f.FullName
      "  -> PDF: $([IO.Path]::GetFileName($pdf))"
    } else {
      $d.Close([ref]0)
    }
  }
} finally {
  $word.Quit()
  [void][Runtime.InteropServices.Marshal]::ReleaseComObject($word)
}
