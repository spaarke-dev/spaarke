[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [switch]$Apply
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function ConvertTo-TaskSlug {
    param([Parameter(Mandatory)][string]$Text)

    $slug = $Text.ToLowerInvariant()
    $slug = [regex]::Replace($slug, '[^a-z0-9]+', '-')
    $slug = [regex]::Replace($slug, '-{2,}', '-')
    $slug = $slug.Trim('-')

    if ([string]::IsNullOrWhiteSpace($slug)) {
        return 'task'
    }

    if ($slug.Length -gt 80) {
        return $slug.Substring(0, 80).Trim('-')
    }

    return $slug
}

function ConvertTo-TaskStatus {
    param([Parameter(Mandatory)][string]$StatusCell)

    if ($StatusCell -match '✅') { return 'completed' }
    if ($StatusCell -match '🔄') { return 'in-progress' }
    if ($StatusCell -match '⏸️|⏸') { return 'blocked' }
    if ($StatusCell -match '❌') { return 'cancelled' }
    return 'not-started'
}

function Add-TextElement {
    param(
        [Parameter(Mandatory)][System.Xml.XmlDocument]$Doc,
        [Parameter(Mandatory)][System.Xml.XmlElement]$Parent,
        [Parameter(Mandatory)][string]$Name,
        [Parameter()][string]$Text
    )

    $el = $Doc.CreateElement($Name)
    if ($null -ne $Text) {
        $el.InnerText = $Text
    }
    [void]$Parent.AppendChild($el)
    return $el
}

function Get-ExistingTaskTags {
    param([Parameter(Mandatory)][string]$Path)

    try {
        [xml]$xml = Get-Content -Raw -Path $Path -Encoding UTF8

        # Newer format tasks (like 001/010) use <metadata><tags> sometimes not present.
        $tagsAttr = $xml.task.metadata.SelectSingleNode('tags')
        if ($null -ne $tagsAttr -and -not [string]::IsNullOrWhiteSpace($tagsAttr.InnerText)) {
            return $tagsAttr.InnerText.Trim()
        }

        # Older placeholder format uses <metadata><tags> as well.
        $tagsOld = $xml.task.metadata.tags
        if ($null -ne $tagsOld -and -not [string]::IsNullOrWhiteSpace($tagsOld.InnerText)) {
            return $tagsOld.InnerText.Trim()
        }

        return ''
    }
    catch {
        return ''
    }
}

function Get-InferredTaskTags {
    param(
        [Parameter(Mandatory)][string]$Title,
        [Parameter()][string]$ExistingTags
    )

    if (-not [string]::IsNullOrWhiteSpace($ExistingTags)) {
        return $ExistingTags
    }

    $tags = New-Object System.Collections.Generic.HashSet[string]

    if ($Title -match 'PCF|Fluent|Monaco|Custom Page|Workspace|Builder|React') {
        [void]$tags.Add('pcf')
        [void]$tags.Add('react')
        [void]$tags.Add('frontend')
        [void]$tags.Add('typescript')
    }

    if ($Title -match 'Dataverse|Entity|Table|Solution|Form|Model-Driven|Security Role') {
        [void]$tags.Add('dataverse')
        [void]$tags.Add('solution')
    }

    if ($Title -match 'BFF|API|Endpoint|Minimal API|SSE|Service|Authorization') {
        [void]$tags.Add('bff-api')
        [void]$tags.Add('api')
        [void]$tags.Add('minimal-api')
    }

    if ($Title -match 'Bicep|Azure|Deploy|App Service|Key Vault') {
        [void]$tags.Add('azure')
        [void]$tags.Add('deploy')
    }

    if ($Title -match 'Redis|Caching|Cache') {
        [void]$tags.Add('redis')
        [void]$tags.Add('caching')
    }

    if ($Title -match 'AI Search|RAG|Embedding|Prompt|Foundry|OpenAI|Document Intelligence') {
        [void]$tags.Add('azure-ai')
    }

    if ($Title -match 'Test|Testing|Unit|Integration') {
        [void]$tags.Add('testing')
    }

    return ($tags | Sort-Object) -join ','
}

function Get-KnowledgeFiles {
    param(
        [Parameter(Mandatory)][string]$ProjectName,
        [Parameter(Mandatory)][string]$Tags
    )

    $repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')

    $files = New-Object System.Collections.Generic.HashSet[string]

    # Always
    [void]$files.Add("projects/$ProjectName/SPEC.md")
    [void]$files.Add("projects/$ProjectName/PLAN.md")
    [void]$files.Add("projects/$ProjectName/tasks/TASK-INDEX.md")

    $tagList = @()
    if (-not [string]::IsNullOrWhiteSpace($Tags)) {
        $tagList = $Tags.Split(',') | ForEach-Object { $_.Trim().ToLowerInvariant() } | Where-Object { $_ -ne '' }
    }

    $map = @{
        'pcf'       = @('src/client/pcf/CLAUDE.md', 'docs/ai-knowledge/guides/PCF-V9-PACKAGING.md', '.claude/skills/dataverse-deploy/SKILL.md', 'docs/reference/adr/ADR-006-prefer-pcf-over-webresources.md', 'docs/reference/adr/ADR-012-shared-component-library.md')
        'react'     = @('src/client/pcf/CLAUDE.md', 'docs/ai-knowledge/guides/PCF-V9-PACKAGING.md')
        'frontend'  = @('src/client/pcf/CLAUDE.md', 'docs/ai-knowledge/guides/PCF-V9-PACKAGING.md')
        'dataverse' = @('.claude/skills/dataverse-deploy/SKILL.md', 'docs/reference/adr/ADR-002-no-heavy-plugins.md')
        'solution'  = @('.claude/skills/dataverse-deploy/SKILL.md')
        'deploy'    = @('.claude/skills/dataverse-deploy/SKILL.md', 'docs/ai-knowledge/guides/PCF-V9-PACKAGING.md')
        'bff-api'   = @('docs/reference/adr/ADR-001-minimal-api-and-workers.md', 'docs/reference/adr/ADR-008-authorization-endpoint-filters.md', 'docs/reference/adr/ADR-010-di-minimalism.md', 'docs/reference/adr/ADR-019-api-errors-and-problemdetails.md')
        'api'       = @('docs/reference/adr/ADR-001-minimal-api-and-workers.md', 'docs/reference/adr/ADR-020-versioning-strategy-apis-jobs-client-packages.md')
        'azure'     = @('docs/reference/adr/ADR-013-ai-architecture.md')
        'azure-ai'  = @('docs/reference/adr/ADR-013-ai-architecture.md', 'docs/reference/adr/ADR-016-ai-cost-rate-limit-and-backpressure.md', 'docs/reference/adr/ADR-015-ai-data-governance.md')
        'redis'     = @('docs/reference/adr/ADR-009-caching-redis-first.md')
        'caching'   = @('docs/reference/adr/ADR-009-caching-redis-first.md', 'docs/reference/adr/ADR-014-ai-caching-and-reuse-policy.md')
        'testing'   = @('tests/CLAUDE.md')
    }

    foreach ($tag in $tagList) {
        if ($map.ContainsKey($tag)) {
            foreach ($p in $map[$tag]) {
                $abs = Join-Path $repoRoot $p
                if (Test-Path $abs) {
                    [void]$files.Add($p)
                }
            }
        }
    }

    return ($files | Sort-Object)
}

function Get-RoleText {
    param([Parameter(Mandatory)][string]$Tags)

    $t = $Tags.ToLowerInvariant()
    if ($t -match 'pcf|react|frontend') {
        return 'PCF control developer with expertise in React, TypeScript, and Fluent UI v9. Follow PCF packaging/versioning guidance strictly.'
    }
    if ($t -match 'dataverse|solution') {
        return 'Senior Power Platform developer with Dataverse solution and model-driven app experience.'
    }
    if ($t -match 'bff-api|minimal-api|api') {
        return 'Senior .NET developer familiar with ASP.NET Core Minimal APIs, endpoint filters, and DI minimalism.'
    }
    if ($t -match 'azure|azure-ai') {
        return 'Azure developer familiar with Bicep, App Service configuration, and Azure AI service provisioning.'
    }

    return 'SPAARKE platform developer. Follow applicable ADRs and repo guidance.'
}

function Build-TaskXml {
    param(
        [Parameter(Mandatory)][string]$ProjectName,
        [Parameter(Mandatory)][pscustomobject]$Task
    )

    $doc = New-Object System.Xml.XmlDocument
    $decl = $doc.CreateXmlDeclaration('1.0', 'UTF-8', $null)
    [void]$doc.AppendChild($decl)

    $root = $doc.CreateElement('task')
    [void]$root.SetAttribute('id', $Task.Id)
    [void]$root.SetAttribute('project', $ProjectName)
    [void]$doc.AppendChild($root)

    $metadata = $doc.CreateElement('metadata')
    [void]$root.AppendChild($metadata)

    Add-TextElement -Doc $doc -Parent $metadata -Name 'title' -Text $Task.Title | Out-Null
    Add-TextElement -Doc $doc -Parent $metadata -Name 'phase' -Text ("Phase {0}: {1}" -f $Task.PhaseNumber, $Task.PhaseName) | Out-Null
    Add-TextElement -Doc $doc -Parent $metadata -Name 'status' -Text $Task.Status | Out-Null
    Add-TextElement -Doc $doc -Parent $metadata -Name 'estimated-hours' -Text ([string]$Task.EstimatedHours) | Out-Null
    Add-TextElement -Doc $doc -Parent $metadata -Name 'dependencies' -Text $Task.Dependencies | Out-Null
    Add-TextElement -Doc $doc -Parent $metadata -Name 'blocks' -Text 'none' | Out-Null
    Add-TextElement -Doc $doc -Parent $metadata -Name 'tags' -Text $Task.Tags | Out-Null

    Add-TextElement -Doc $doc -Parent $root -Name 'prompt' -Text ("Implement: {0}. Follow SPEC.md/PLAN.md and ADRs; update tests and docs as appropriate." -f $Task.Title) | Out-Null
    Add-TextElement -Doc $doc -Parent $root -Name 'role' -Text (Get-RoleText -Tags $Task.Tags) | Out-Null
    Add-TextElement -Doc $doc -Parent $root -Name 'goal' -Text ("A complete, reviewed implementation of '{0}' with verification steps completed." -f $Task.Title) | Out-Null

    $inputs = $doc.CreateElement('inputs')
    [void]$root.AppendChild($inputs)
    $spec = $doc.CreateElement('file'); [void]$spec.SetAttribute('purpose', 'design-spec'); $spec.InnerText = "projects/$ProjectName/SPEC.md"; [void]$inputs.AppendChild($spec)
    $plan = $doc.CreateElement('file'); [void]$plan.SetAttribute('purpose', 'project-plan'); $plan.InnerText = "projects/$ProjectName/PLAN.md"; [void]$inputs.AppendChild($plan)
    $index = $doc.CreateElement('file'); [void]$index.SetAttribute('purpose', 'task-index'); $index.InnerText = "projects/$ProjectName/tasks/TASK-INDEX.md"; [void]$inputs.AppendChild($index)

    $constraints = $doc.CreateElement('constraints')
    [void]$root.AppendChild($constraints)
    $c1 = $doc.CreateElement('constraint'); [void]$c1.SetAttribute('source', 'project'); $c1.InnerText = 'Follow applicable ADRs under docs/reference/adr/ and module CLAUDE.md guidance.'; [void]$constraints.AppendChild($c1)

    $knowledge = $doc.CreateElement('knowledge')
    [void]$root.AppendChild($knowledge)
    Add-TextElement -Doc $doc -Parent $knowledge -Name 'topic' -Text ($Task.Tags) | Out-Null
    $filesEl = $doc.CreateElement('files')
    [void]$knowledge.AppendChild($filesEl)

    foreach ($f in (Get-KnowledgeFiles -ProjectName $ProjectName -Tags $Task.Tags)) {
        $fe = $doc.CreateElement('file')
        $fe.InnerText = $f
        [void]$filesEl.AppendChild($fe)
    }

    $context = $doc.CreateElement('context')
    [void]$root.AppendChild($context)
    Add-TextElement -Doc $doc -Parent $context -Name 'background' -Text ("Phase {0}: {1}. See TASK-INDEX.md for dependencies and ordering." -f $Task.PhaseNumber, $Task.PhaseName) | Out-Null

    $depsEl = $doc.CreateElement('dependencies')
    [void]$context.AppendChild($depsEl)
    if (-not [string]::IsNullOrWhiteSpace($Task.Dependencies) -and $Task.Dependencies -ne 'none') {
        $dep = $doc.CreateElement('dependency')
        [void]$dep.SetAttribute('task', $Task.Dependencies)
        [void]$dep.SetAttribute('status', 'unknown')
        $dep.InnerText = 'Complete dependencies before starting this task.'
        [void]$depsEl.AppendChild($dep)
    }

    $steps = $doc.CreateElement('steps')
    [void]$root.AppendChild($steps)

    $step0 = $doc.CreateElement('step'); [void]$step0.SetAttribute('order', '0'); [void]$step0.SetAttribute('name', 'Context Check'); $step0.InnerText = 'If context > 70%, create a handoff summary and start a new chat.'; [void]$steps.AppendChild($step0)
    $step1 = $doc.CreateElement('step'); [void]$step1.SetAttribute('order', '1'); [void]$step1.SetAttribute('name', 'Review'); $step1.InnerText = 'Read SPEC.md, PLAN.md, TASK-INDEX.md; verify dependencies.'; [void]$steps.AppendChild($step1)
    $step2 = $doc.CreateElement('step'); [void]$step2.SetAttribute('order', '2'); [void]$step2.SetAttribute('name', 'Gather'); $step2.InnerText = 'Read all files in <inputs> and <knowledge>; extract ADR constraints.'; [void]$steps.AppendChild($step2)
    $step3 = $doc.CreateElement('step'); [void]$step3.SetAttribute('order', '3'); [void]$step3.SetAttribute('name', 'Plan'); $step3.InnerText = 'List files to change, outline approach, and define verification.'; [void]$steps.AppendChild($step3)
    $step4 = $doc.CreateElement('step'); [void]$step4.SetAttribute('order', '4'); [void]$step4.SetAttribute('name', 'Implement'); $step4.InnerText = 'Implement the change with minimal scope; add/update tests.'; [void]$steps.AppendChild($step4)
    $step5 = $doc.CreateElement('step'); [void]$step5.SetAttribute('order', '5'); [void]$step5.SetAttribute('name', 'Verify'); $step5.InnerText = 'Run relevant builds/tests; ensure acceptance criteria met.'; [void]$steps.AppendChild($step5)
    $step6 = $doc.CreateElement('step'); [void]$step6.SetAttribute('order', '6'); [void]$step6.SetAttribute('name', 'Document'); $step6.InnerText = 'Update TASK-INDEX.md status; document any decisions or follow-ups.'; [void]$steps.AppendChild($step6)

    $outputs = $doc.CreateElement('outputs')
    [void]$root.AppendChild($outputs)
    Add-TextElement -Doc $doc -Parent $outputs -Name 'output' -Text 'Code and/or configuration changes per task goal.' | Out-Null

    $ac = $doc.CreateElement('acceptance-criteria')
    [void]$root.AppendChild($ac)
    Add-TextElement -Doc $doc -Parent $ac -Name 'criterion' -Text 'Implementation matches SPEC/PLAN intent.' | Out-Null
    Add-TextElement -Doc $doc -Parent $ac -Name 'criterion' -Text 'Relevant tests/builds pass.' | Out-Null
    Add-TextElement -Doc $doc -Parent $ac -Name 'criterion' -Text 'Documentation and TASK-INDEX updated as needed.' | Out-Null

    return $doc
}

$projectName = Split-Path (Resolve-Path (Join-Path $PSScriptRoot '..')) -Leaf
$projectRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$tasksDir = Join-Path $projectRoot 'tasks'
$taskIndexPath = Join-Path $tasksDir 'TASK-INDEX.md'

if (-not (Test-Path $taskIndexPath)) {
    throw "TASK-INDEX.md not found at: $taskIndexPath"
}

$raw = Get-Content -Raw -Path $taskIndexPath -Encoding UTF8
$lines = $raw -split "`r?`n"

$currentPhaseNumber = $null
$currentPhaseName = $null
$tasks = New-Object System.Collections.Generic.List[object]

foreach ($line in $lines) {
    if ($line -match '^##\s+Phase\s+(\d+)\s*:\s*(.+)\s*$') {
        $currentPhaseNumber = [int]$Matches[1]
        $currentPhaseName = $Matches[2].Trim()
        continue
    }

    if ($line -match '^\|\s*(\d{3})\s*\|\s*(.*?)\s*\|\s*(.*?)\s*\|\s*(.*?)\s*\|\s*(.*?)\s*\|\s*$') {
        $id = $Matches[1]
        $title = $Matches[2].Trim()
        $statusCell = $Matches[3].Trim()
        $deps = $Matches[4].Trim()
        $hoursCell = $Matches[5].Trim()

        if ([string]::IsNullOrWhiteSpace($title)) { continue }

        $hours = $null
        if ($hoursCell -match '^(\d+)\s*h$') { $hours = [int]$Matches[1] }

        $tasks.Add([pscustomobject]@{
                Id = $id
                Title = $title
                PhaseNumber = $currentPhaseNumber
                PhaseName = $currentPhaseName
                Status = (ConvertTo-TaskStatus -StatusCell $statusCell)
                Dependencies = $deps
                EstimatedHours = $hours
            })
    }
}

if ($tasks.Count -eq 0) {
    throw 'No tasks parsed from TASK-INDEX.md; check formatting.'
}

Write-Host ("Parsed {0} tasks from TASK-INDEX.md" -f $tasks.Count)

$changed = 0

foreach ($t in $tasks) {
    $existing = Get-ChildItem -Path $tasksDir -Filter ("{0}-*.poml" -f $t.Id) -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $existing) {
        Write-Warning "No task file found for ID $($t.Id); skipping."
        continue
    }

    # Respect TASK-INDEX as source of truth; don't churn completed/cancelled tasks.
    if ($t.Status -in @('completed', 'cancelled')) {
        continue
    }

    $existingTags = Get-ExistingTaskTags -Path $existing.FullName
    $t | Add-Member -NotePropertyName Tags -NotePropertyValue (Get-InferredTaskTags -Title $t.Title -ExistingTags $existingTags) -Force

    # Detect if already in task-create style: root <task> with id attribute.
    $isAlreadyCompliant = $false
    try {
        [xml]$x = Get-Content -Raw -Path $existing.FullName -Encoding UTF8
        if ($null -ne $x.task -and $x.task.GetAttribute('id')) {
            # Heuristic: compliant tasks typically have <role> and <goal>
            if ($x.task.SelectSingleNode('role') -and $x.task.SelectSingleNode('goal')) {
                $isAlreadyCompliant = $true
            }
        }
    }
    catch { }

    $slug = ConvertTo-TaskSlug -Text $t.Title
    $targetName = ("{0}-{1}.poml" -f $t.Id, $slug)
    $targetPath = Join-Path $tasksDir $targetName

    if (-not $isAlreadyCompliant) {
        $doc = Build-TaskXml -ProjectName $projectName -Task $t

        if ($PSCmdlet.ShouldProcess($existing.Name, "Rewrite to task-create POML format")) {
            if ($Apply) {
                $settings = New-Object System.Xml.XmlWriterSettings
                $settings.Indent = $true
                $settings.IndentChars = '  '
                $settings.OmitXmlDeclaration = $false
                $settings.Encoding = New-Object System.Text.UTF8Encoding($false)

                $writer = [System.Xml.XmlWriter]::Create($existing.FullName, $settings)
                $doc.Save($writer)
                $writer.Close()

                $changed++
            }
        }
    }

    # Rename ONLY generic placeholder filenames (e.g., 044-task.poml) to a slugged name.
    # Avoid renaming non-placeholder tasks to reduce churn.
    $isGenericName = $existing.Name -match '^[0-9]{3}-task\.poml$'
    if ($isGenericName -and $existing.FullName -ne $targetPath) {
        if ($PSCmdlet.ShouldProcess($existing.Name, "Rename to $targetName")) {
            if ($Apply) {
                if (Test-Path $targetPath) {
                    Write-Warning "Target exists, not renaming: $targetName"
                }
                else {
                    Move-Item -Path $existing.FullName -Destination $targetPath
                    $changed++
                }
            }
        }
    }
}

if ($Apply) {
    Write-Host ("Done. Updated/renamed {0} files." -f $changed)
}
else {
    Write-Host 'Dry run complete. Re-run with -Apply to write changes.'
}
