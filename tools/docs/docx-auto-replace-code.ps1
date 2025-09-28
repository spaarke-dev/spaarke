param(
    [Parameter(Mandatory=$true)]
    [string]$Path,

    [Parameter(Mandatory=$true)]
    [string]$SnippetDir,

    [Parameter(Mandatory=$true)]
    [string]$MappingJson,

    [Parameter(Mandatory=$false)]
    [ValidateSet("Annotate","InsertBelow","Replace")]
    [string]$Mode = "Annotate",

    [Parameter(Mandatory=$false)]
    [switch]$Backup
)

# Load mapping
$map = Get-Content -Raw -LiteralPath $MappingJson | ConvertFrom-Json

function Get-SnippetContent([string]$file) {
    $full = Join-Path $SnippetDir $file
    if (!(Test-Path $full)) { throw "Snippet not found: $full" }
    return [IO.File]::ReadAllText($full)
}

$word = New-Object -ComObject Word.Application
$word.Visible = $false

$docs = Get-ChildItem -Path $Path -Recurse -Include *.docx -File
foreach ($docFile in $docs) {
    Write-Host "Processing $($docFile.FullName)" -ForegroundColor Cyan
    if ($Backup) {
        Copy-Item -LiteralPath $docFile.FullName -Destination ($docFile.FullName + ".bak") -Force
    }

    $doc = $word.Documents.Open($docFile.FullName)
    $doc.TrackRevisions = $true

    # Iterate paragraphs and detect "code blocks"
    $paras = $doc.Paragraphs
    $i = 1
    while ($i -le $paras.Count) {
        $p = $paras.Item($i)
        $rng = $p.Range
        $style = ""
        try { $style = $rng.Style.NameLocal } catch {}
        $font = ""
        try { $font = $rng.Font.Name } catch {}
        $text = $rng.Text

        # Heuristic: a code paragraph has "Code" in the style, or a monospaced font, or contains common C# tokens
        $isCode = ($style -match "Code") -or ($font -match "Consolas|Courier") -or ($text -match "public |using |class |var |;|{")

        if (-not $isCode) { $i++; continue }

        # Accumulate contiguous code paragraphs
        $start = $p.Range.Start
        $end = $p.Range.End
        $blockText = $text
        $j = $i + 1
        while ($j -le $paras.Count) {
            $next = $paras.Item($j)
            $nText = $next.Range.Text
            $nStyle = ""; try { $nStyle = $next.Range.Style.NameLocal } catch {}
            $nFont = ""; try { $nFont = $next.Range.Font.Name } catch {}
            $nIsCode = ($nStyle -match "Code") -or ($nFont -match "Consolas|Courier") -or ($nText -match "public |using |class |var |;|{")
            if (-not $nIsCode) { break }
            $end = $next.Range.End
            $blockText += "`n" + $nText
            $j++
        }

        # Decide mapping based on patterns
        $matchedRule = $null
        foreach ($rule in $map.rules) {
            if ($blockText -match $rule.pattern) { $matchedRule = $rule; break }
        }

        if ($matchedRule -ne $null) {
            $range = $doc.Range($start, $end)
            $note = "Suggest replace with snippet(s): " + ($matchedRule.snippets -join ", ")
            # Add a comment at the start of the block
            $doc.Comments.Add($range, $note) | Out-Null

            if ($Mode -ne "Annotate") {
                $insertText = ""
                foreach ($sn in $matchedRule.snippets) {
                    $insertText += "<<BEGIN SNIPPET: $sn>>`r`n"
                    $insertText += (Get-SnippetContent $sn) + "`r`n"
                    $insertText += "<<END SNIPPET: $sn>>`r`n"
                }

                if ($Mode -eq "InsertBelow") {
                    $insertRange = $doc.Range($end, $end)
                    $insertRange.InsertParagraphAfter() | Out-Null
                    $insertRange = $doc.Range($insertRange.End, $insertRange.End)
                    $insertRange.Text = $insertText
                    # Apply monospace font
                    $insertRange.Font.Name = "Consolas"
                } elseif ($Mode -eq "Replace") {
                    $range.Text = $insertText
                    $range.Font.Name = "Consolas"
                }
            }
        }

        # Advance index to paragraph after the block
        $i = $j
    }

    $doc.Save()
    $doc.Close()
}

$word.Quit()
[System.Runtime.Interopservices.Marshal]::ReleaseComObject($word) | Out-Null
