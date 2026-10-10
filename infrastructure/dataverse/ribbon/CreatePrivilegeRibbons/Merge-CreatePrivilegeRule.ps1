<#
.SYNOPSIS
    Adds the Create-privilege display rule to every create / wizard command in one or more ribbon XML files.

.DESCRIPTION
    unified-access-control-r2 task 180 (owner round 89 item 2): a custom ribbon button that creates a record, or opens
    a wizard that does, hides for a user who lacks the Create privilege on the table it creates. This is Dataverse's own
    declarative rule; no script runs:

        <DisplayRule Id="sprk.CreatePrivilege.<table>.DisplayRule">
          <EntityPrivilegeRule EntityName="<table>" PrivilegeType="Create" PrivilegeDepth="Basic" />
        </DisplayRule>

    PrivilegeDepth Basic means "holds Create at ANY depth" (user, business unit, parent: child BU or organization).

    Which commands: a CommandDefinition whose Actions call a function listed in create-launchers.json ("launchers",
    function -> table). The function decides the table, not the command id, so a new command calling an existing
    launcher is ruled without a code change. A command calling launchers for two different tables is refused (ambiguous).

    Pure file transformation (no Dataverse call). Works on a bare RibbonDiff.xml (root <RibbonDiffXml>), on an export's
    customizations.xml, and on the checked-in snippets (<ImportExportXml> holding one or more <RibbonDiffXml>): each
    <RibbonDiffXml> gets the rule definitions its own commands reference. Idempotent: a command already carrying the rule
    is left alone, and an existing rule definition is rewritten to the canonical one. Every other node is kept; the file
    is written only when something changed, with its original encoding (BOM or not) and line endings.

    Throws when a command would be lost (the CommandDefinition ids before and after must be equal).

.PARAMETER Path
    One or more XML files to transform in place.

.PARAMETER Check
    Change nothing. Report each create command and whether it carries its rule; the result's Missing count is what
    the caller fails on.

.OUTPUTS
    One object per create command: File, Command, Function, Table, Status (added | present | missing).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string[]] $Path,
    [switch] $Check
)

$ErrorActionPreference = 'Stop'

$config = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'create-launchers.json') | ConvertFrom-Json
$launchers = @{}
$config.launchers.PSObject.Properties | ForEach-Object { $launchers[$_.Name] = $_.Value }

function Get-RuleId([string] $table) { [string]::Format($config.ruleIdFormat, $table) }

function Get-Children([System.Xml.XmlNode] $node, [string] $name) {
    @($node.ChildNodes | Where-Object { $_.NodeType -eq 'Element' -and $_.LocalName -eq $name })
}

function Get-LeadingWhitespace([System.Xml.XmlNode] $node) {
    $ws = $node.PreviousSibling
    if ($ws -and $ws.NodeType -in 'Whitespace', 'SignificantWhitespace') { return $ws.Value }
    return $null
}

# The file's indentation step, read from $node and its parent (falls back to two spaces).
function Get-IndentUnit([System.Xml.XmlNode] $node) {
    $own = Get-LeadingWhitespace $node
    $parentLead = if ($node.ParentNode) { Get-LeadingWhitespace $node.ParentNode } else { $null }
    if ($own -and $parentLead -and $own.Length -gt $parentLead.Length -and $own.StartsWith($parentLead)) {
        return $own.Substring($parentLead.Length)
    }
    return '  '
}

# Inserts $child into $parent keeping the file's indentation: after $after (or as the last element when $after is
# null), preceded by the same whitespace as the element before it. An empty parent is expanded onto its own lines.
function Add-Formatted([System.Xml.XmlNode] $parent, [System.Xml.XmlNode] $child, [System.Xml.XmlNode] $after) {
    $doc = $parent.OwnerDocument
    $elements = @($parent.ChildNodes | Where-Object { $_.NodeType -eq 'Element' })
    if ($elements.Count -eq 0) {
        # Expand <X /> or <X></X>: indentation from the parent's own leading whitespace.
        $indent = Get-LeadingWhitespace $parent
        if ($indent) {
            while ($parent.HasChildNodes) { $parent.RemoveChild($parent.FirstChild) | Out-Null }   # whitespace only
            $parent.AppendChild($doc.CreateWhitespace($indent + (Get-IndentUnit $parent))) | Out-Null
            $parent.AppendChild($child) | Out-Null
            $parent.AppendChild($doc.CreateWhitespace($indent)) | Out-Null
        }
        else {
            $parent.AppendChild($child) | Out-Null
        }
        return
    }
    $anchor = if ($after) { $after } else { $elements[-1] }
    $ws = $anchor.PreviousSibling
    if ($ws -and $ws.NodeType -in 'Whitespace', 'SignificantWhitespace') {
        $parent.InsertAfter($child, $anchor) | Out-Null
        $parent.InsertAfter($doc.CreateWhitespace($ws.Value), $anchor) | Out-Null
    }
    else {
        $parent.InsertAfter($child, $anchor) | Out-Null
    }
}

# Inserts a new element named $name into $parent before the first of $beforeNames present (else last), formatted.
function Add-ElementInOrder([System.Xml.XmlNode] $parent, [string] $name, [string[]] $beforeNames) {
    $doc = $parent.OwnerDocument
    $element = $doc.CreateElement($name, $parent.NamespaceURI)
    $before = $null
    foreach ($n in $beforeNames) { $before = (Get-Children $parent $n | Select-Object -First 1); if ($before) { break } }
    if ($before) {
        $ws = $before.PreviousSibling
        $parent.InsertBefore($element, $before) | Out-Null
        if ($ws -and $ws.NodeType -in 'Whitespace', 'SignificantWhitespace') {
            $parent.InsertBefore($doc.CreateWhitespace($ws.Value), $before) | Out-Null
        }
    }
    else {
        Add-Formatted $parent $element $null
    }
    return $element
}

function New-RuleDefinition([System.Xml.XmlDocument] $doc, [string] $ns, [string] $table) {
    $rule = $doc.CreateElement('DisplayRule', $ns)
    $rule.SetAttribute('Id', (Get-RuleId $table))
    $privilege = $doc.CreateElement('EntityPrivilegeRule', $ns)
    $privilege.SetAttribute('EntityName', $table)
    $privilege.SetAttribute('PrivilegeType', 'Create')
    $privilege.SetAttribute('PrivilegeDepth', 'Basic')
    $rule.AppendChild($privilege) | Out-Null
    return $rule
}

function Test-CanonicalRule([System.Xml.XmlNode] $rule, [string] $table) {
    $children = @($rule.ChildNodes | Where-Object { $_.NodeType -eq 'Element' })
    return $children.Count -eq 1 -and $children[0].LocalName -eq 'EntityPrivilegeRule' -and
        $children[0].GetAttribute('EntityName') -eq $table -and $children[0].GetAttribute('PrivilegeType') -eq 'Create' -and
        $children[0].GetAttribute('PrivilegeDepth') -eq 'Basic' -and -not $children[0].HasAttribute('InvertResult')
}

# Ensures <RuleDefinitions><DisplayRules> of one RibbonDiffXml holds the canonical rule for $table. Returns $true when
# it changed something.
function Set-RuleDefinition([System.Xml.XmlNode] $ribbonDiff, [string] $table) {
    $doc = $ribbonDiff.OwnerDocument
    $ns = $ribbonDiff.NamespaceURI
    $definitions = Get-Children $ribbonDiff 'RuleDefinitions' | Select-Object -First 1
    if (-not $definitions) { $definitions = Add-ElementInOrder $ribbonDiff 'RuleDefinitions' @('LocLabels') }
    $displayRules = Get-Children $definitions 'DisplayRules' | Select-Object -First 1
    if (-not $displayRules) { $displayRules = Add-ElementInOrder $definitions 'DisplayRules' @('EnableRules') }

    $ruleId = Get-RuleId $table
    $existing = Get-Children $displayRules 'DisplayRule' | Where-Object { $_.GetAttribute('Id') -eq $ruleId } | Select-Object -First 1
    if ($existing -and (Test-CanonicalRule $existing $table)) { return $false }

    $rule = New-RuleDefinition $doc $ns $table
    if ($existing) {
        $displayRules.ReplaceChild($rule, $existing) | Out-Null
    }
    else {
        Add-Formatted $displayRules $rule $null
    }
    # Indent the rule's inner line like the file (nothing to do in a file written on one line).
    $indent = Get-LeadingWhitespace $rule
    if ($indent) {
        $privilege = $rule.FirstChild
        $rule.InsertBefore($doc.CreateWhitespace($indent + (Get-IndentUnit $rule)), $privilege) | Out-Null
        $rule.AppendChild($doc.CreateWhitespace($indent)) | Out-Null
    }
    return $true
}

function Get-CommandIds([System.Xml.XmlDocument] $doc) {
    @($doc.SelectNodes('//*[local-name()="CommandDefinition"]') | ForEach-Object { $_.GetAttribute('Id') }) | Sort-Object
}

foreach ($file in $Path) {
    $full = (Resolve-Path -LiteralPath $file).Path
    $bytes = [System.IO.File]::ReadAllBytes($full)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $text = [System.Text.Encoding]::UTF8.GetString($bytes, $(if ($hasBom) { 3 } else { 0 }), $bytes.Length - $(if ($hasBom) { 3 } else { 0 }))
    $newline = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }

    $doc = New-Object System.Xml.XmlDocument
    $doc.PreserveWhitespace = $true
    try { $doc.LoadXml($text) } catch { throw "${full}: not well-formed XML ($($_.Exception.InnerException.Message ?? $_.Exception.Message))" }
    $before = Get-CommandIds $doc
    $changed = $false

    foreach ($ribbonDiff in @($doc.SelectNodes('//*[local-name()="RibbonDiffXml"]'))) {
        $neededTables = New-Object System.Collections.Generic.HashSet[string]
        foreach ($command in @($ribbonDiff.SelectNodes('.//*[local-name()="CommandDefinition"]'))) {
            $functions = @($command.SelectNodes('./*[local-name()="Actions"]//*[local-name()="JavaScriptFunction"]') |
                ForEach-Object { $_.GetAttribute('FunctionName') })
            $tables = @($functions | Where-Object { $launchers.ContainsKey($_) } | ForEach-Object { $launchers[$_] } | Select-Object -Unique)
            if ($tables.Count -eq 0) { continue }
            $commandId = $command.GetAttribute('Id')
            if ($tables.Count -gt 1) { throw "${full}: $commandId calls create launchers for several tables ($($tables -join ', ')); rule it by hand." }
            $table = $tables[0]
            $ruleId = Get-RuleId $table
            $function = @($functions | Where-Object { $launchers.ContainsKey($_) })[0]

            $displayRules = Get-Children $command 'DisplayRules' | Select-Object -First 1
            $present = $displayRules -and (Get-Children $displayRules 'DisplayRule' | Where-Object { $_.GetAttribute('Id') -eq $ruleId })
            $status = if ($present) { 'present' } elseif ($Check) { 'missing' } else { 'added' }
            if (-not $present -and -not $Check) {
                if (-not $displayRules) { $displayRules = Add-ElementInOrder $command 'DisplayRules' @('Actions') }
                $reference = $doc.CreateElement('DisplayRule', $command.NamespaceURI)
                $reference.SetAttribute('Id', $ruleId)
                Add-Formatted $displayRules $reference $null
                $changed = $true
            }
            [void] $neededTables.Add($table)
            [pscustomobject] @{ File = $file; Command = $commandId; Function = $function; Table = $table; Status = $status }
        }
        foreach ($table in $neededTables) {
            if ($Check) {
                $definitions = Get-Children $ribbonDiff 'RuleDefinitions' | Select-Object -First 1
                $rule = if ($definitions) {
                    $definitions.SelectSingleNode("./*[local-name()='DisplayRules']/*[local-name()='DisplayRule' and @Id='$(Get-RuleId $table)']")
                }
                if (-not $rule -or -not (Test-CanonicalRule $rule $table)) {
                    [pscustomobject] @{ File = $file; Command = '(rule definition)'; Function = ''; Table = $table; Status = 'missing' }
                }
            }
            elseif (Set-RuleDefinition $ribbonDiff $table) { $changed = $true }
        }
    }

    if ($changed) {
        $after = Get-CommandIds $doc
        if (($before -join '|') -ne ($after -join '|')) { throw "${full}: the command list changed during the merge; nothing was written." }
        $settings = New-Object System.Xml.XmlWriterSettings
        $settings.Encoding = New-Object System.Text.UTF8Encoding($hasBom)
        $settings.NewLineChars = $newline
        $settings.NewLineHandling = [System.Xml.NewLineHandling]::Replace
        $settings.OmitXmlDeclaration = -not $text.TrimStart().StartsWith('<?xml')
        $stream = New-Object System.IO.MemoryStream
        $writer = [System.Xml.XmlWriter]::Create($stream, $settings)
        $doc.Save($writer)
        $writer.Dispose()
        $out = $stream.ToArray()
        # Keep a trailing newline exactly as the original had it.
        $outText = [System.Text.Encoding]::UTF8.GetString($out, $(if ($hasBom) { 3 } else { 0 }), $out.Length - $(if ($hasBom) { 3 } else { 0 }))
        $trail = if ($text.EndsWith($newline)) { $newline } else { '' }
        $outText = $outText.TrimEnd("`r", "`n") + $trail
        $preamble = if ($hasBom) { [byte[]] (0xEF, 0xBB, 0xBF) } else { [byte[]] @() }
        [System.IO.File]::WriteAllBytes($full, $preamble + [System.Text.Encoding]::UTF8.GetBytes($outText))
    }
}
