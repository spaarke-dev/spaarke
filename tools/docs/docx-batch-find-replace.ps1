param(
    [Parameter(Mandatory=$true)]
    [string]$Path,
    [Parameter(Mandatory=$true)]
    [string]$MappingJson,
    [Parameter(Mandatory=$false)]
    [switch]$DryRun
)

$map = Get-Content -Raw -LiteralPath $MappingJson | ConvertFrom-Json
$word = New-Object -ComObject Word.Application
$word.Visible = $false

Get-ChildItem -Path $Path -Recurse -Include *.docx -File | ForEach-Object {
    $file = $_.FullName
    Write-Host "Updating: $file" -ForegroundColor Cyan
    $doc = $word.Documents.Open($file)
    $doc.TrackRevisions = $true

    # Pull full doc text once for DRY counting
    $docText = $doc.Content.Text

    foreach ($rule in $map.rules) {
        $findText = [string]$rule.find
        $replaceText = [string]$rule.replace

        if ($DryRun) {
            $count = ([regex]::Matches($docText, [regex]::Escape($findText))).Count
            Write-Host ("  [DRY] '{0}' -> '{1}' : {2} matches" -f $findText, $replaceText, $count)
        } else {
            $range = $doc.Content
            $find = $range.Find
            $find.ClearFormatting()
            $find.Replacement.ClearFormatting()
            $find.Text = $findText
            $find.Replacement.Text = $replaceText

            # By-ref positional args for COM call:
            # Execute(FindText, MatchCase, MatchWholeWord, MatchWildcards, MatchSoundsLike, MatchAllWordForms,
            #         Forward, Wrap, Format, ReplaceWith, Replace)
            $matchCase     = $false
            $matchWhole    = $false
            $matchWild     = $false
            $matchSounds   = $false
            $matchAllForms = $false
            $forward       = $true
            $wrap          = 1      # wdFindContinue
            $format        = $false
            $replaceWith   = $replaceText
            $replace       = 2      # wdReplaceAll

            $null = $find.Execute([ref]$findText,
                                  [ref]$matchCase,
                                  [ref]$matchWhole,
                                  [ref]$matchWild,
                                  [ref]$matchSounds,
                                  [ref]$matchAllForms,
                                  [ref]$forward,
                                  [ref]$wrap,
                                  [ref]$format,
                                  [ref]$replaceWith,
                                  [ref]$replace)
            Write-Host ("  Replaced '{0}' -> '{1}'" -f $findText, $replaceText)
        }
    }

    if (-not $DryRun) { $doc.Save() }
    $doc.Close()
}

$word.Quit()
[void][System.Runtime.InteropServices.Marshal]::ReleaseComObject($word)
