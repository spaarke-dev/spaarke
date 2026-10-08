<#
.SYNOPSIS
    Deploy lint C (ISS-018, #1452, owner decision D-77): refuse a playbook definition whose FetchXML list conditions
    Dataverse would mis-read, or whose node config is a Playbook Designer canvas stub.

.DESCRIPTION
    Dot-source this file, then call Assert-PlaybookFetchXmlShape -Definition <parsed definition JSON>.
    Throws (after printing every problem) when any node fails; prints one OK line otherwise.

    The FetchXML rule is NOT re-implemented here. This script compiles the BFF's own checker,
    src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/FetchXmlShapeValidator.cs, with Add-Type and runs it in
    "authored template" mode - the same code the QueryDataverse executor runs on the rendered query and the repo
    regression test (tests/integration/regression/Ai/Issue1452_NotificationPlaybookFetchXmlShapeTests.cs) runs on
    every repo playbook. One owner for the rule (root CLAUDE.md section 11).

    Checks, per node:
      1. Every `fetchXml` string in the node config passes FetchXmlShapeValidator (authored mode): a list operator
         (in, not-in, between, ...) takes its values only from <value> children or one {{fetchInGuids path}}
         expression - never a value attribute (`in value="a,b"` leaves the list EMPTY and the query fails); joinIds
         is never allowed in FetchXML.
      2. The node config is not a canvas stub: a config that holds `__canvasNodeId` and nothing but `__actionType`
         is what a Playbook Designer save leaves behind (ISS-018c: it replaced the live Tasks Due Soon nodes and
         every run failed). Deploying one would overwrite a working node with an empty one.

.EXAMPLE
    . "$PSScriptRoot/common/Assert-PlaybookFetchXmlShape.ps1"
    Assert-PlaybookFetchXmlShape -Definition $definition -Source $DefinitionFile
#>

function Import-FetchXmlShapeValidator {
    if ('Sprk.Bff.Api.Services.Ai.Nodes.FetchXmlShapeValidator' -as [type]) { return }
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..' '..')).Path
    $source = Join-Path $repoRoot 'src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/FetchXmlShapeValidator.cs'
    if (-not (Test-Path -LiteralPath $source)) {
        throw "Lint C: the FetchXML shape checker was not found at $source."
    }
    Add-Type -Path $source
}

function Get-NodeConfigObject {
    param($Node)
    $config = if ($null -ne $Node.configJson) { $Node.configJson } elseif ($null -ne $Node.config) { $Node.config } else { $null }
    if ($config -is [string]) {
        if ([string]::IsNullOrWhiteSpace($config)) { return $null }
        try { return ($config | ConvertFrom-Json -Depth 64) } catch { return $null }
    }
    return $config
}

function Get-FetchXmlStrings {
    # Every string property named fetchXml, at any depth (a Designer "wrapper" config nests the real config as a
    # JSON string under `configJson`).
    param($Value)
    if ($null -eq $Value) { return }
    if ($Value -is [string]) {
        $trimmed = $Value.TrimStart()
        if ($trimmed.StartsWith('{')) {
            try { Get-FetchXmlStrings -Value ($Value | ConvertFrom-Json -Depth 64) } catch { }
        }
        return
    }
    if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [System.Management.Automation.PSCustomObject]) {
        foreach ($item in $Value) { Get-FetchXmlStrings -Value $item }
        return
    }
    if ($Value -is [System.Management.Automation.PSCustomObject]) {
        foreach ($property in $Value.PSObject.Properties) {
            if ($property.Name -eq 'fetchXml' -and $property.Value -is [string]) {
                $property.Value
            } else {
                Get-FetchXmlStrings -Value $property.Value
            }
        }
    }
}

function Assert-PlaybookFetchXmlShape {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)] $Definition,
        [string] $Source = '(definition)'
    )

    Import-FetchXmlShapeValidator
    $problems = New-Object System.Collections.Generic.List[string]
    $checkedQueries = 0

    foreach ($node in @($Definition.nodes)) {
        $name = if ($node.name) { $node.name } else { '(unnamed node)' }
        $config = Get-NodeConfigObject -Node $node

        if ($null -ne $config -and $config -is [System.Management.Automation.PSCustomObject]) {
            $keys = @($config.PSObject.Properties.Name)
            if ($keys -contains '__canvasNodeId' -and @($keys | Where-Object { $_ -notin @('__canvasNodeId', '__actionType') }).Count -eq 0) {
                $problems.Add("node '$name': its config is a Playbook Designer canvas stub (only __canvasNodeId/__actionType); deploying it would replace a working node with an empty one.")
            }
        }

        foreach ($fetchXml in @(Get-FetchXmlStrings -Value $config)) {
            $checkedQueries++
            foreach ($problem in [Sprk.Bff.Api.Services.Ai.Nodes.FetchXmlShapeValidator]::Validate($fetchXml, $true)) {
                $problems.Add("node '$name': $problem")
            }
        }
    }

    if ($problems.Count -gt 0) {
        Write-Host ''
        Write-Host "LINT C FAILED - FetchXML list shape / canvas stub ($Source)" -ForegroundColor Red
        foreach ($p in $problems) { Write-Host "  - $p" -ForegroundColor Red }
        Write-Host ''
        Write-Host 'Fix: write a GUID list as <condition attribute="..." operator="in">{{fetchInGuids myMatters.ids}}</condition>' -ForegroundColor Yellow
        Write-Host '     (one <value> per id; an empty list selects nothing). See docs/guides/PLAYBOOK-AUTHOR-GUIDE.md.' -ForegroundColor Yellow
        throw "Playbook lint C failed: $($problems.Count) problem(s) in $Source."
    }

    Write-Host "  Lint C  : OK - $checkedQueries FetchXML quer$(if ($checkedQueries -eq 1) { 'y' } else { 'ies' }) well-shaped; no canvas-stub configs (ISS-018)" -ForegroundColor Green
}
