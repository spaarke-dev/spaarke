# Find-ClientPointerWrite.ps1 — the CLIENT write detector for the columns only the BFF may write on sprk_document
# (unified-access-control-r2 task 166). Dot-sourced by scripts/Set-DocumentPointerFieldSecurity.ps1 (precondition p4a scans
# every deployed web resource with it). KEPT IDENTICAL to tests/Spaarke.ArchTests/ClientDocumentPointerWriteGuardTests.cs,
# which holds the source tree to the same rule in CI — and its test TheFlsScriptsDetector_AgreesWithTheGuard_OnEveryCase
# runs THIS file through pwsh over every write and read case of the guard, so the two cannot drift apart unseen
# (task 166 f1-v2, F-D: the 34-case harness of f1-v1 was never committed, and two write shapes were missed by both).
#
# The columns: sprk_graphdriveid / sprk_graphitemid (the SharePoint Embedded pointer the BFF follows as the application),
# sprk_relocationpending (the relocation ledger) and sprk_relocatedversions (the relocation's version record).
#
# A WRITE is: an object key; a computed key; a bracket or dotted assignment (also ??=, ||=, &&=); a form setValue through
# getAttribute / attributes.get or through getControl / controls.get(...).getAttribute(); Reflect.set / defineProperty —
# directly, or through a NAME bound to a column (const F = "sprk_graphitemid", { ITEM: 'sprk_graphitemid' }) or any
# alias of such a name (import { F as G }, export { F as G }, const G = F, const { F: G } = …), bound in the same text
# or, for source files, named in -SharedConstants. Reads ($select strings, property access, bracket lookups, comparisons)
# never match. A key built at run time cannot be detected statically: the field-level security lock is the control for it.

$PointerWriteColumn = 'sprk_(?:graph(?:item|drive)id|relocationpending|relocatedversions)'

# {C} = the column alternation.
$PointerWriteTemplates = @(
    '["'']?{C}["'']?\s*:'                                                                        # object key
    '[{,]\s*\[\s*["''`]{C}["''`]\s*\]\s*:'                                                       # computed key
    '\[\s*["''`]{C}["''`]\s*\]\s*(?:\?\?|\|\||&&)?=(?![=>])'                                     # bracket assignment
    '\.\s*{C}\s*(?:\?\?|\|\||&&)?=(?![=>])'                                                      # dotted assignment
    '(?:getAttribute|attributes\s*\.\s*get)\(\s*["''`]{C}["''`]\s*\)\s*\??\.\s*setValue'         # form setValue (attribute)
    '(?:getControl|controls\s*\.\s*get)\(\s*["''`]{C}["''`]\s*\)\s*\??\.\s*getAttribute\(\s*\)\s*\??\.\s*setValue' # form setValue (control)
    '(?:Reflect\s*\.\s*set|defineProperty)\(\s*[^,()]+,\s*["''`]{C}["''`]'                       # reflective set
)

# {P} = a (possibly member-qualified) name bound to a column.
$PointerWriteThroughTemplates = @(
    '[{,]\s*\[\s*{P}\s*\]\s*:'                                                                   # computed key
    '\[\s*{P}\s*\]\s*(?:\?\?|\|\||&&)?=(?![=>])'                                                 # bracket assignment
    '(?:getAttribute|attributes\s*\.\s*get)\(\s*{P}\s*\)\s*\??\.\s*setValue'                     # form setValue (attribute)
    '(?:getControl|controls\s*\.\s*get)\(\s*{P}\s*\)\s*\??\.\s*getAttribute\(\s*\)\s*\??\.\s*setValue' # form setValue (control)
    '(?:Reflect\s*\.\s*set|defineProperty)\(\s*[^,()]+,\s*{P}\s*[,)]'                           # reflective set
)

# A name bound to a column-name string.
$PointerWriteBindingTemplate = '(?<![\w$.])([A-Za-z_$][\w$]*)\s*[=:]\s*["''`]{C}["''`]'

# {K} = a name already known to hold a column; group 1 = a new name that holds it too.
$PointerWriteAliasTemplates = @(
    '(?<![\w$.]){K}\s+as\s+([A-Za-z_$][\w$]*)'                                                   # import / export rename
    '(?<![\w$.])([A-Za-z_$][\w$]*)\s*=(?![=>])\s*(?:[\w$]+\s*\.\s*)*{K}(?![\w$(.\[])'           # re-binding
    '(?:const|let|var)\s*\{[^{}]*?(?<![\w$.]){K}\s*:\s*([A-Za-z_$][\w$]*)'                      # destructuring rename
)

$PointerWritePath = '(?:[\w$]+\s*\.\s*)*{N}(?![\w$])'

function Get-PointerWriteNames {
    param([string]$Text, [string[]]$SharedConstants = @())
    $names = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($m in [regex]::Matches($Text, $PointerWriteBindingTemplate.Replace('{C}', $PointerWriteColumn))) { [void]$names.Add($m.Groups[1].Value) }
    foreach ($s in @($SharedConstants)) { if ($s) { [void]$names.Add($s) } }
    # Aliases, to a fixpoint (an alias of an alias holds the column too).
    $pending = [System.Collections.Generic.List[string]]::new()
    foreach ($n in $names) { $pending.Add($n) }
    for ($round = 0; $round -lt 10 -and $pending.Count -gt 0; $round++) {
        $next = [System.Collections.Generic.List[string]]::new()
        foreach ($known in $pending) {
            foreach ($template in $PointerWriteAliasTemplates) {
                foreach ($m in [regex]::Matches($Text, $template.Replace('{K}', [regex]::Escape($known)))) {
                    if ($names.Add($m.Groups[1].Value)) { $next.Add($m.Groups[1].Value) }
                }
            }
        }
        $pending = $next
    }
    return , @($names)
}

function Find-PointerWrite {
    param([string]$Text, [string[]]$SharedConstants = @())
    foreach ($template in $PointerWriteTemplates) {
        $m = [regex]::Match($Text, $template.Replace('{C}', $PointerWriteColumn))
        if ($m.Success) { return $m.Value }
    }
    foreach ($name in (Get-PointerWriteNames -Text $Text -SharedConstants $SharedConstants)) {
        $path = $PointerWritePath.Replace('{N}', [regex]::Escape($name))
        foreach ($template in $PointerWriteThroughTemplates) {
            $m = [regex]::Match($Text, $template.Replace('{P}', $path))
            if ($m.Success) { return $m.Value }
        }
    }
    return $null
}
