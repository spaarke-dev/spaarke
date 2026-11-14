# Find-ClientInvoiceFamilyWrite.ps1 - the CLIENT write detector for the invoice-family columns only the BFF may write
# (#2799, unified-access-control-r2). Dot-sourced by scripts/Set-DocumentPointerFieldSecurity.ps1 -Target InvoiceFamilyLinks
# (precondition p4: every DEPLOYED web resource) and run by tests/Spaarke.ArchTests/InvoiceFamilyFieldLockGuardTests.cs (every
# client source file and committed bundle), so the deployed scan and the source guard are ONE detector.
#
# TABLE-SCOPED. The locked column names (sprk_matter, sprk_project, sprk_invoice, sprk_regardingmatter, ...) are ordinary
# writable columns of many other tables (sprk_event, sprk_document, sprk_todo, ...), so a name-only match (the approach of
# Find-ClientPointerWrite.ps1, whose columns exist on one table only) would fire on every one of them. A hit here is a write
# whose TARGET is a family table (scripts/common/InvoiceFamilyLockedColumns.psd1), in one of these shapes:
#   (a) createRecord / updateRecord (Xrm.WebApi, or any wrapper of the same name) whose entity argument is a family table, as a
#       literal or through a name bound to one (const T = "sprk_invoice"), and whose payload sets a locked column of that table:
#       a key `col:` or its navigation `col@odata.bind:` (any case; the navigation property is the column's schema name).
#       FAIL CLOSED: a payload that is not an inline object literal (a variable, a call), or a literal with a spread or a
#       computed key, cannot be read statically and counts as a write of a locked column;
#   (b) a Web API request with a write method (fetch / request options `method: "POST" | "PATCH" | "PUT"`, or
#       XMLHttpRequest.open("PATCH", ...)) whose URL, written before the method, names a family ENTITY SET as a path segment
#       (/sprk_invoices( ... , /sprk_invoices" ...). Its body is read like (a) when it is JSON.stringify of an inline literal;
#       otherwise it counts (fail closed). A bind VALUE ("sprk_Invoice@odata.bind": "/sprk_invoices(...)") is a link TO an
#       invoice from another table, never the target, and is skipped. The BFF's own routes (/api/v1/child-records/sprk_invoice/...)
#       name the logical name, not the entity set, and never match;
#   (c) a $batch change-set line `POST|PATCH|PUT <url>/sprk_invoices...` (always counts: its body is text).
# Reads (retrieveRecord, retrieveMultipleRecords, GET requests, $select strings) and writes of other tables never match.
# NOT detectable statically: an entity name chosen at run time (a variable not bound to a family table literal, e.g. a
# generic wrapper's parameter), a name bound through an object key (config objects such as { entityType: "sprk_invoice" } are
# read-side metadata in every shipped bundle), a URL built where the method is not written after it, a form script's
# getAttribute(<col>).setValue (the form's table is not in the script). The field-level security lock is the control for
# those; this detector keeps the shapes above out of shipped clients and gates the lock on deployed ones.

$InvoiceFamilyLockedColumns = Import-PowerShellDataFile -LiteralPath (Join-Path $PSScriptRoot 'InvoiceFamilyLockedColumns.psd1')

# The window the arguments of one call, or the URL before a method, are read in (minified bundles put everything on one line).
$InvoiceFamilyArgumentWindow = 20000
$InvoiceFamilyUrlLookBack = 600

# Built once from the list: table and entity-set alternations, and the pre-filter (every shape names a family table or
# entity set literally, so a text that names none cannot hold a hit; the ArchTest applies the same filter before calling).
$InvoiceFamilyTableAlternation = ($InvoiceFamilyLockedColumns.Keys | Sort-Object | ForEach-Object { [regex]::Escape($_) }) -join '|'
$InvoiceFamilySetToTable = @{}
foreach ($t in $InvoiceFamilyLockedColumns.Keys) { $InvoiceFamilySetToTable[$InvoiceFamilyLockedColumns[$t].EntitySet.ToLowerInvariant()] = $t }
$InvoiceFamilySetAlternation = ($InvoiceFamilySetToTable.Keys | Sort-Object | ForEach-Object { [regex]::Escape($_) }) -join '|'
$InvoiceFamilyPrefilter = [regex]::new("(?i)(?:$InvoiceFamilyTableAlternation)", 'Compiled')

function Get-InvoiceFamilyCallArgumentList {
    # The top-level arguments of the call whose '(' ends just before $Start, up to its matching ')'. Strings and template
    # literals are skipped whole; a call that is not closed within the window returns what it read.
    param([string]$Text, [int]$Start)
    $result = [System.Collections.Generic.List[string]]::new()
    $depth = 0; $i = $Start; $argStart = $Start
    $end = [Math]::Min($Text.Length, $Start + $InvoiceFamilyArgumentWindow)
    while ($i -lt $end) {
        $ch = $Text[$i]
        if ($ch -eq [char]'"' -or $ch -eq [char]"'" -or $ch -eq [char]'`') {
            $quote = $ch; $i++
            while ($i -lt $end -and $Text[$i] -ne $quote) { if ($Text[$i] -eq [char]'\') { $i++ }; $i++ }
        }
        elseif ($ch -eq [char]'(' -or $ch -eq [char]'[' -or $ch -eq [char]'{') { $depth++ }
        elseif ($ch -eq [char]')' -or $ch -eq [char]']' -or $ch -eq [char]'}') {
            if ($depth -eq 0) { $result.Add($Text.Substring($argStart, $i - $argStart).Trim()); return , $result }
            $depth--
        }
        elseif ($ch -eq [char]',' -and $depth -eq 0) { $result.Add($Text.Substring($argStart, $i - $argStart).Trim()); $argStart = $i + 1 }
        $i++
    }
    return , $result
}

function Get-InvoiceFamilyPayloadVerdict {
    # Whether $Payload (one argument's text) sets a locked column of $Table. $null = it does not; otherwise the reason.
    param([string]$Table, [string]$Payload, [hashtable]$NavigationNames = @{})
    if (-not $Payload.StartsWith('{')) { return "payload '$((Get-InvoiceFamilySnippet $Payload 60))' is not an inline object literal (fail closed)" }
    $names = @($InvoiceFamilyLockedColumns[$Table].Columns) + @($NavigationNames[$Table] | Where-Object { $_ })
    $alternation = ($names | Select-Object -Unique | ForEach-Object { [regex]::Escape($_) }) -join '|'
    $key = [regex]::Match($Payload, "(?i)(?<![\w$])[""'``]?(?:$alternation)(?:@odata\.bind)?[""'``]?\s*:")
    if ($key.Success) { return "sets '$($key.Value.TrimEnd(':').Trim())'" }
    if ($Payload -match '(?<![\w$.])\.\.\.') { return 'payload literal has a spread (fail closed)' }
    if ($Payload -match '[{,]\s*\[') { return 'payload literal has a computed key (fail closed)' }
    return $null
}

function Get-InvoiceFamilySnippet([string]$Text, [int]$Max = 120) {
    $flat = ($Text -replace '\s+', ' ').Trim()
    if ($flat.Length -le $Max) { return $flat }
    return $flat.Substring(0, $Max) + '...'
}

function Find-InvoiceFamilyWrite {
    # Every client write of a locked invoice-family column in $Text, as objects { Table; Shape; Detail; Snippet }.
    # -NavigationNames: per table, navigation property names besides the column names (the script passes live metadata).
    param([string]$Text, [hashtable]$NavigationNames = @{})
    $hits = [System.Collections.Generic.List[object]]::new()
    if (-not $Text -or -not $InvoiceFamilyPrefilter.IsMatch($Text)) { return , $hits }
    $tableAlt = $InvoiceFamilyTableAlternation
    $setToTable = $InvoiceFamilySetToTable
    $setAlt = $InvoiceFamilySetAlternation

    # (a) createRecord / updateRecord of a family table: literal, or through a name bound to a family table literal.
    $bound = @{}
    foreach ($m in [regex]::Matches($Text, "(?:\b(?:const|let|var)\s+|[,;(]\s*)([A-Za-z_$][\w$]*)\s*=(?![=>])\s*[""'``]($tableAlt)[""'``]")) {
        $bound[$m.Groups[1].Value] = $m.Groups[2].Value
    }
    $entityArg = "[""'``](?<t>$tableAlt)[""'``]"
    if ($bound.Count -gt 0) { $entityArg = "(?:$entityArg|(?<n>$(($bound.Keys | ForEach-Object { [regex]::Escape($_) }) -join '|'))(?![\w$]))" }
    foreach ($m in [regex]::Matches($Text, "(?<![\w$])(?<fn>createRecord|updateRecord)\s*\(\s*$entityArg\s*,")) {
        $table = if ($m.Groups['t'].Success) { $m.Groups['t'].Value } else { $bound[$m.Groups['n'].Value] }
        $arguments = Get-InvoiceFamilyCallArgumentList -Text $Text -Start ($m.Index + $m.Value.IndexOf('(') + 1)
        $payloadIndex = if ($m.Groups['fn'].Value -eq 'createRecord') { 1 } else { 2 }
        if ($arguments.Count -le $payloadIndex) { continue }
        $verdict = Get-InvoiceFamilyPayloadVerdict -Table $table -Payload $arguments[$payloadIndex] -NavigationNames $NavigationNames
        if ($verdict) {
            $hits.Add([pscustomobject]@{ Table = $table; Shape = $m.Groups['fn'].Value; Detail = $verdict; Snippet = Get-InvoiceFamilySnippet $Text.Substring($m.Index, [Math]::Min(160, $Text.Length - $m.Index)) })
        }
    }

    # (b) a Web API request with a write method whose URL (before the method) names a family entity set as a path segment.
    $methods = @(
        [regex]::Matches($Text, "(?i)\bmethod\s*:\s*[""'``](?<m>POST|PATCH|PUT)[""'``]")
        [regex]::Matches($Text, "(?i)\.open\(\s*[""'``](?<m>POST|PATCH|PUT)[""'``]\s*,")
    )
    foreach ($mm in $methods) {
        $isXhr = $mm.Value -match '^\.open'
        $from = [Math]::Max(0, $mm.Index - $InvoiceFamilyUrlLookBack)
        $before = if ($isXhr) { $Text.Substring($mm.Index, [Math]::Min($InvoiceFamilyUrlLookBack, $Text.Length - $mm.Index)) } else { $Text.Substring($from, $mm.Index - $from) }
        $urls = @([regex]::Matches($before, "(?i)/(?<s>$setAlt)(?=\s*(?:\(|[""'``?/]|\$\{))") | Where-Object {
                $lead = $before.Substring([Math]::Max(0, $_.Index - 40), [Math]::Min(40, $_.Index))
                $lead -notmatch '(?i)@odata\.(?:bind|id)[''"`]?\s*[:=,]\s*[''"`]?[^''"`]*$'
            })
        if ($urls.Count -eq 0) { continue }
        $url = $urls[-1]
        $table = $setToTable[$url.Groups['s'].Value.ToLowerInvariant()]
        $after = $Text.Substring($mm.Index, [Math]::Min(800, $Text.Length - $mm.Index))
        $body = [regex]::Match($after, '\bbody\s*:\s*JSON\s*\.\s*stringify\s*\(')
        $verdict = if ($body.Success) {
            $arguments = Get-InvoiceFamilyCallArgumentList -Text $Text -Start ($mm.Index + $body.Index + $body.Length)
            if ($arguments.Count -ge 1) { Get-InvoiceFamilyPayloadVerdict -Table $table -Payload $arguments[0] -NavigationNames $NavigationNames } else { 'body not readable (fail closed)' }
        } else { 'body is not JSON.stringify of an inline literal (fail closed)' }
        if ($verdict) {
            $hits.Add([pscustomobject]@{ Table = $table; Shape = "request $($mm.Groups['m'].Value.ToUpperInvariant())"; Detail = $verdict; Snippet = Get-InvoiceFamilySnippet ($before.Substring([Math]::Max(0, $url.Index - 40)) + $mm.Value) })
        }
    }

    # (c) a $batch change-set line.
    foreach ($m in [regex]::Matches($Text, "(?i)\b(?<m>POST|PATCH|PUT)\s+[^\s""'``]*?/(?<s>$setAlt)(?=[(\s?""'``]|\$\{)")) {
        $hits.Add([pscustomobject]@{ Table = $setToTable[$m.Groups['s'].Value.ToLowerInvariant()]; Shape = "batch $($m.Groups['m'].Value.ToUpperInvariant())"; Detail = 'change-set body is text (fail closed)'; Snippet = Get-InvoiceFamilySnippet $m.Value })
    }
    return , $hits
}
