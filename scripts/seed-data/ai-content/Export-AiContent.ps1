<#
.SYNOPSIS
    Exports the shipped AI content set (tools, skills, knowledge, actions, playbooks, nodes, their N:N links,
    playbook canvases and routing rows) from the authoring environment into source-controlled seed files that
    keep every row's primary key.

.DESCRIPTION
    L56 / data-setup plan D3 / demo-environment plan W2. A new customer stamp must receive the AI content the
    product calls, with the SAME primary keys as the authoring environment: clients hard-code playbook ids
    (Document Profile, Summarize File), routing rows are the Binding ids chips and the ledger key on (ADR-040),
    and coded workflows find child actions by code. This script is the one-way bridge:

        spaarkedev1 (read-only GET)  ->  scripts/seed-data/ai-content/*.json  ->  reviewed PR
        ->  embedded in the L2 Worker  ->  H12a AI content seeder (create-if-missing by id)

    Provisioning NEVER reads dev. Re-running this script is a deliberate act whose diff the owner reviews. The
    output is deterministic: rows sorted by primary key, columns by name, pairs by (source, target), LF line
    endings, no BOM. Multi-line text (prompts, schemas, knowledge) is written as an array of lines so the diff
    shows the changed line; the seeder joins the lines with "\n".

    SAFETY: only HTTP GET requests are issued, with the operator's own `az account get-access-token`. Nothing is
    written to Dataverse. The L2 control plane never runs this script.

    WHAT IS EXPORTED
      - Playbooks: exactly the ids in $Playbooks below (owner decision 2, 2026-11-26), whatever their state.
      - Nodes: the active nodes of those playbooks.
      - Tools, skills, knowledge, actions: every ACTIVE row. The tool catalog is closed (handler classes), coded
        workflows find child actions by code, and JPS prompts resolve skills and knowledge by name, so a subset
        would break callers that grep cannot see.
      - Routing rows (sprk_playbookconsumer): every ACTIVE row — the single AI routing surface (ADR-039); the
        BFF reconciles consumer types against ConsumerTypes.cs at boot.
      - Type rows (tool / skill / knowledge / output types): only the rows a shipped row points at.
      - N:N links: playbook <-> action / skill / knowledge / tool and node <-> skill / knowledge / tool, for the
        shipped playbooks and nodes.
      - Canvas: each shipped playbook's sprk_canvaslayoutjson, in its own file (written last by the seeder).

    WHAT IS LEFT OUT, AND WHY (environment-specific values must not travel to a stamp)
      - sprk_modeldeploymentid on actions and nodes: sprk_aimodeldeployment rows are created by H12c with
        stamp-generated ids, after H12a. An empty pin means the configured default deployment:
        AiCompletionNodeExecutor passes it as the deployment name and null falls back to the configured model
        (OpenAiClient), and AiAnalysisNodeExecutor only copies it into ToolExecutionContext, which no tool reads.
        Every action pin is empty. The copies of the pin inside canvas JSON and node sprk_configjson
        ("modelDeploymentId") are blanked for the same reason.
      - sprk_analysisid (tools, skills, knowledge, actions): a link to a runtime analysis record.
      - sprk_documentid (knowledge): a link to a document record of the authoring environment.
      - Playbook run and index state: sprk_containerid, sprk_indexhash, sprk_indexstatus, sprk_lastindexedat,
        sprk_lastindexerror, sprk_lastrundate.
      - Playbook N:N to matter types (customer taxonomy) and to analysis outputs (runtime records).
      - System, audit, owner and state/status columns. Every row is created Active on the stamp.
    A lookup that is neither exported nor listed as left out stops the export when it holds a value.

    WHAT IS CORRECTED ON EXPORT
      - $ContentCorrections: a dev row that still binds a retired contract is rewritten to the current one (each
        entry says which and why). The dev row itself is fixed separately, with the owner's OK.

.PARAMETER EnvironmentUrl
    Dataverse environment to read. Default: https://spaarkedev1.crm.dynamics.com

.PARAMETER OutputDirectory
    Where the JSON files are written. Default: this script's folder.

.EXAMPLE
    pwsh scripts/seed-data/ai-content/Export-AiContent.ps1

.NOTES
    Requires `az login` as a user who can read the tables. PowerShell 7+.
#>
[CmdletBinding()]
param(
    [string]$EnvironmentUrl = 'https://spaarkedev1.crm.dynamics.com',
    [string]$OutputDirectory = $PSScriptRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------------------------------------------
# The shipped playbooks (owner decision 2, 2026-11-26: "the set the product code calls today, plus the demo-story
# playbooks"). Each entry says how the product reaches it. Adding one is a reviewed change to this list.
# ---------------------------------------------------------------------------------------------------------------
$Playbooks = [ordered]@{
    # Called by product code today
    '18cf3cc8-02ec-f011-8406-7c1e520aa4df' = 'PB-002 Document Profile: by name (AppOnlyAnalysisService, ProfileSummary job), routing rows document-profile + ai-summary, id in useAiSummary.ts'
    'bc71facf-6af1-f011-8406-7ced8d1dc988' = 'PB-003 Email Analysis: routing row email-analysis (AppOnlyAnalysisService)'
    '2d660cad-d418-f111-8343-7ced8d1dc988' = 'PB-008 Create New Matter Pre-Fill: routing row matter-pre-fill (MatterPreFillService)'
    'fc343e9c-3460-f111-ab0b-7c1e521b425f' = 'Create New Project Pre-Fill: routing row project-pre-fill (ProjectPreFillService)'
    '4a72f99c-a119-f111-8343-7ced8d1dc988' = 'PB-015 Summarize File: id in DocumentEmailWizard, SemanticSearchControl, DocumentRelationshipViewer'
    'a0d49d0d-4a65-f111-ab0c-70a8a590c51c' = 'matter-health-single: routing rows insights-ask (InsightEndpoints), matter form insight card'
    '47686eb1-9916-f111-8343-7c1e520aa4df' = 'PB-012 Document Summary: target of the shipped compose-summarize routing row'
    # Named by the owner (invoice extraction and chat)
    '1e657651-9308-f111-8407-7c1e520aa4df' = 'PB-013 Finance Invoice Processing: invoice extraction (INVEXT tool); offered in the playbook picker'
    '5ece14f7-8a17-f111-8343-7ced8d1dc988' = 'PB-014 SprkChat Document Assistant: chat; offered in the playbook picker'
    # Demo story (the demo documents: NDA, patent application and office action, purchase and loan agreements)
    '86c243fe-2eda-f011-8406-7ced8d1dc988' = 'PB-009 Summarize a Non-Disclosure Agreement: demo NDA'
    'c546a6cc-c2f5-f011-8406-7ced8d1dc988' = 'PB-010 U.S. Patent Office Action Review: demo office action'
    'ea4d2ecc-4aea-f011-8406-7ced8d1dc988' = 'PB-004 Full Contract Analysis: demo purchase and loan agreements'
    'fe3e5acc-4aea-f011-8406-7c1e520aa4df' = 'PB-007 Risk Scan: demo agreements'
}

# Table specs, in the seeder's dependency order. Lookups: column -> target table (exported). LeaveOut: lookups or
# columns dropped with a reason above. MatchKeys: alternative keys (each a list of columns) that identify a row
# already in an environment under another id; Types match by name only.
$Specs = [ordered]@{
    sprk_aitooltype       = @{ Kind = 'type';      MatchKeys = @(, @('sprk_name')) }
    sprk_aiskilltype      = @{ Kind = 'type';      MatchKeys = @(, @('sprk_name')) }
    sprk_aiknowledgetype  = @{ Kind = 'type';      MatchKeys = @(, @('sprk_name')) }
    sprk_aioutputtype     = @{ Kind = 'type';      MatchKeys = @(, @('sprk_name')) }
    sprk_analysistool     = @{ Kind = 'active';    MatchKeys = @(@('sprk_toolcode'), @('sprk_name'));      Lookups = @{ sprk_tooltypeid = 'sprk_aitooltype' };           LeaveOut = @('sprk_analysisid') }
    sprk_analysisskill    = @{ Kind = 'active';    MatchKeys = @(@('sprk_skillcode'), @('sprk_name'));     Lookups = @{ sprk_skilltypeid = 'sprk_aiskilltype' };         LeaveOut = @('sprk_analysisid') }
    sprk_analysisknowledge = @{ Kind = 'active';   MatchKeys = @(@('sprk_knowledgecode'), @('sprk_name')); Lookups = @{ sprk_knowledgetypeid = 'sprk_aiknowledgetype' }; LeaveOut = @('sprk_analysisid', 'sprk_documentid') }
    sprk_analysisaction   = @{ Kind = 'active';    MatchKeys = @(@('sprk_actioncode'), @('sprk_name'));                                                                   LeaveOut = @('sprk_analysisid', 'sprk_modeldeploymentid') }
    sprk_analysisplaybook = @{ Kind = 'playbooks'; MatchKeys = @(@('sprk_playbookid'), @('sprk_name'));    Lookups = @{ sprk_outputtypeid = 'sprk_aioutputtype' };
                             LeaveOut = @('sprk_canvaslayoutjson', 'sprk_containerid', 'sprk_indexhash', 'sprk_indexstatus', 'sprk_lastindexedat', 'sprk_lastindexerror', 'sprk_lastrundate') }
    sprk_playbooknode     = @{ Kind = 'nodes';     MatchKeys = @();                                         Lookups = @{ sprk_playbookid = 'sprk_analysisplaybook'; sprk_actionid = 'sprk_analysisaction' }; LeaveOut = @('sprk_modeldeploymentid') }
    sprk_playbookconsumer = @{ Kind = 'active';    MatchKeys = @(, @('sprk_consumertype', 'sprk_consumercode', 'sprk_environment')); Lookups = @{ sprk_playbook = 'sprk_analysisplaybook'; sprk_action = 'sprk_analysisaction' } }
}

# N:N relationships exported (source side is the owning playbook / node).
$Associations = @(
    @{ Relationship = 'sprk_analysisplaybook_action'; Source = 'sprk_analysisplaybook'; Target = 'sprk_analysisaction' },
    @{ Relationship = 'sprk_playbook_skill';          Source = 'sprk_analysisplaybook'; Target = 'sprk_analysisskill' },
    @{ Relationship = 'sprk_playbook_knowledge';      Source = 'sprk_analysisplaybook'; Target = 'sprk_analysisknowledge' },
    @{ Relationship = 'sprk_playbook_tool';           Source = 'sprk_analysisplaybook'; Target = 'sprk_analysistool' },
    @{ Relationship = 'sprk_playbooknode_skill';      Source = 'sprk_playbooknode';     Target = 'sprk_analysisskill' },
    @{ Relationship = 'sprk_playbooknode_knowledge';  Source = 'sprk_playbooknode';     Target = 'sprk_analysisknowledge' },
    @{ Relationship = 'sprk_playbooknode_tool';       Source = 'sprk_playbooknode';     Target = 'sprk_analysistool' }
)

$baseUrl = $EnvironmentUrl.TrimEnd('/')
$api = "$baseUrl/api/data/v9.2"
$token = az account get-access-token --resource $baseUrl --query accessToken -o tsv
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($token)) {
    throw "az account get-access-token failed for $baseUrl. Run 'az login' first."
}
$headers = @{ Authorization = "Bearer $token"; Accept = 'application/json'; 'OData-Version' = '4.0'; 'OData-MaxVersion' = '4.0' }

function Get-DataverseJson([string]$Uri) {
    # READ-ONLY: every call in this script is a GET.
    return Invoke-RestMethod -Method Get -Headers $headers -Uri $Uri
}

function Get-AllRows([string]$Uri) {
    $rows = New-Object System.Collections.Generic.List[object]
    while ($Uri) {
        $page = Get-DataverseJson $Uri
        foreach ($row in $page.value) { $rows.Add($row) }
        $Uri = $page.PSObject.Properties['@odata.nextLink'] ? $page.'@odata.nextLink' : $null
    }
    return , $rows
}

$jsonOptions = [System.Text.Json.JsonSerializerOptions]::new()
$jsonOptions.Encoder = [System.Text.Encodings.Web.JavaScriptEncoder]::UnsafeRelaxedJsonEscaping

function ConvertTo-JsonString([string]$Text) { return [System.Text.Json.JsonSerializer]::Serialize($Text, $jsonOptions) }

# One value as JSON at the given indent. Text with line breaks becomes an array of lines (LF-normalised).
function ConvertTo-JsonValue($Value, [string]$Indent) {
    if ($null -eq $Value) { return 'null' }
    if ($Value -is [bool]) { return $(if ($Value) { 'true' } else { 'false' }) }
    if ($Value -is [int] -or $Value -is [long] -or $Value -is [double] -or $Value -is [decimal]) {
        return [System.Convert]::ToString($Value, [System.Globalization.CultureInfo]::InvariantCulture)
    }
    $text = ([string]$Value) -replace "`r`n", "`n"
    if (-not $text.Contains("`n")) { return ConvertTo-JsonString $text }
    $lines = $text.Split("`n") | ForEach-Object { "$Indent  $(ConvertTo-JsonString $_)" }
    return "[`n" + ($lines -join ",`n") + "`n$Indent]"
}

function Write-Utf8Lf([string]$Path, [string]$Text) {
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($Path)) | Out-Null
    [System.IO.File]::WriteAllText($Path, $Text, [System.Text.UTF8Encoding]::new($false))
}

# Blanks the authoring environment's model-deployment id inside canvas / node-config JSON text (see "left out").
# An empty pin means the configured default deployment.
function Clear-ModelPin([string]$Json) {
    return [regex]::Replace($Json, '"modelDeploymentId"\s*:\s*"[0-9a-fA-F-]{36}"', '"modelDeploymentId":null')
}

# Content corrections: dev rows that still carry a retired contract, rewritten on export so a stamp gets the current one.
# Each entry names the row, the column, the JPS output field and the corrected $choices binding; the pattern must match
# exactly once (a miss stops the export, so a correction never silently stops applying). Remove an entry once dev
# itself is corrected (then the rewrite is a no-op and the re-export diff is empty).
#   - ACT-004 Invoice Processing: invoiceStatus bound to the retired invoice-status choice column; since ebilling-r1
#     D-41 the invoice status is statuscode (FinanceBaseValues.InvoiceStatusCodeColumn). The downstream node's
#     fieldMappings supply the statuscode options (PromptSchemaRenderer.ResolveChoices).
$ContentCorrections = @(
    @{ Table = 'sprk_analysisaction'; Id = '38c9ecf2-cb10-f111-8342-7ced8d1dc988'; Column = 'sprk_systemprompt'
       Field = 'invoiceStatus'; Choices = 'downstream:update_doc.statuscode' }
)

function Set-CorrectedChoices([string]$Table, [string]$Id, [string]$Column, [string]$Value) {
    foreach ($c in $ContentCorrections | Where-Object { $_.Table -eq $Table -and $_.Id -eq $Id -and $_.Column -eq $Column }) {
        $pattern = '("name"\s*:\s*"' + [regex]::Escape($c.Field) + '"\s*,[^{}]*?"\$choices"\s*:\s*")[^"]*(")'
        $hits = [regex]::Matches($Value, $pattern).Count
        if ($hits -ne 1) { throw "Content correction for $Table/$Id ($($c.Field)) matched $hits time(s); expected exactly 1." }
        $Value = [regex]::Replace($Value, $pattern, { param($m) $m.Groups[1].Value + $c.Choices + $m.Groups[2].Value })
    }
    return $Value
}

function Format-Guid($Value) { if ($null -eq $Value) { return $null } return ([string]$Value).ToLowerInvariant() }

# ---------------------------------------------------------------------------------------------------------------
# 1. Metadata
# ---------------------------------------------------------------------------------------------------------------
$meta = @{}
foreach ($table in $Specs.Keys) {
    $m = Get-DataverseJson ("$api/EntityDefinitions(LogicalName='$table')" +
        '?$select=LogicalName,EntitySetName,PrimaryIdAttribute,PrimaryNameAttribute' +
        '&$expand=Attributes($select=LogicalName,IsCustomAttribute,IsValidForCreate,AttributeOf,AttributeType,AttributeTypeName),' +
        'ManyToOneRelationships($select=ReferencingAttribute,ReferencedEntity,ReferencingEntityNavigationPropertyName)')
    $meta[$table] = $m
}

$spec = $null
$scalarColumns = @{}
$lookupColumns = @{}
foreach ($table in $Specs.Keys) {
    $spec = $Specs[$table]
    $m = $meta[$table]
    $leaveOut = @($spec['LeaveOut'] ?? @())
    $lookups = $spec['Lookups'] ?? @{}
    $scalarColumns[$table] = @(
        $m.Attributes | Where-Object {
            $_.IsCustomAttribute -and $_.IsValidForCreate -and -not $_.AttributeOf -and $_.LogicalName -ne $m.PrimaryIdAttribute -and
            $_.LogicalName -notin $leaveOut -and
            ($_.AttributeType -notin @('Lookup', 'Owner', 'Customer', 'Virtual', 'State', 'Status') -or
             ($_.AttributeType -eq 'Virtual' -and $_.AttributeTypeName.Value -eq 'MultiSelectPicklistType'))
        } | ForEach-Object { $_.LogicalName } | Sort-Object { $_ } -CaseSensitive)
    $lookupColumns[$table] = @(
        $m.Attributes | Where-Object {
            $_.IsCustomAttribute -and $_.IsValidForCreate -and -not $_.AttributeOf -and $_.AttributeType -eq 'Lookup' -and $_.LogicalName -notin $leaveOut
        } | ForEach-Object { $_.LogicalName } | Sort-Object { $_ } -CaseSensitive)
    foreach ($lookup in $lookupColumns[$table]) {
        if (-not $lookups.ContainsKey($lookup)) { $lookupColumns[$table] = @($lookupColumns[$table] | Where-Object { $_ -ne $lookup }); $spec['Unmapped'] = @($spec['Unmapped'] ?? @()) + $lookup }
    }
}

# ---------------------------------------------------------------------------------------------------------------
# 2. Rows
# ---------------------------------------------------------------------------------------------------------------
$rows = @{}
$playbookIds = @($Playbooks.Keys | ForEach-Object { $_.ToLowerInvariant() })
foreach ($table in $Specs.Keys) {
    $spec = $Specs[$table]
    if ($spec.Kind -eq 'type') { continue } # filled from references below
    $m = $meta[$table]
    $pk = $m.PrimaryIdAttribute
    $select = (@($pk) + $scalarColumns[$table] + @($lookupColumns[$table] | ForEach-Object { "_$($_)_value" }) + @($spec['Unmapped'] ?? @() | ForEach-Object { "_$($_)_value" }) + @('statecode')) -join ','
    $all = Get-AllRows "$api/$($m.EntitySetName)?`$select=$select"
    $picked = switch ($spec.Kind) {
        'active' { @($all | Where-Object { $_.statecode -eq 0 }) }
        'playbooks' { @($all | Where-Object { (Format-Guid $_.$pk) -in $playbookIds }) }
        'nodes' { @($all | Where-Object { $_.statecode -eq 0 -and (Format-Guid $_._sprk_playbookid_value) -in $playbookIds }) }
    }
    if ($spec.Kind -eq 'playbooks' -and $picked.Count -ne $playbookIds.Count) {
        $found = @($picked | ForEach-Object { Format-Guid $_.$pk })
        throw "Playbooks not found in ${EnvironmentUrl}: $(($playbookIds | Where-Object { $_ -notin $found }) -join ', ')"
    }
    foreach ($row in $picked) {
        foreach ($unmapped in @($spec['Unmapped'] ?? @())) {
            if ($null -ne $row."_$($unmapped)_value") {
                throw "$table row $($row.$pk) has a value in lookup '$unmapped', which this export neither carries nor leaves out. Decide and list it in `$Specs."
            }
        }
    }
    $rows[$table] = $picked
}

# Type rows: exactly the rows a shipped row points at.
foreach ($table in $Specs.Keys) {
    if ($Specs[$table].Kind -ne 'type') { continue }
    $referenced = New-Object System.Collections.Generic.HashSet[string]
    foreach ($source in $Specs.Keys) {
        $lookups = $Specs[$source]['Lookups'] ?? @{}
        foreach ($column in $lookups.Keys) {
            if ($lookups[$column] -ne $table) { continue }
            foreach ($row in @($rows[$source])) { $v = Format-Guid $row."_$($column)_value"; if ($v) { [void]$referenced.Add($v) } }
        }
    }
    $m = $meta[$table]
    $pk = $m.PrimaryIdAttribute
    $select = (@($pk) + $scalarColumns[$table] + @('statecode')) -join ','
    $all = Get-AllRows "$api/$($m.EntitySetName)?`$select=$select"
    $rows[$table] = @($all | Where-Object { (Format-Guid $_.$pk) -in $referenced })
}

# Every lookup must point at an exported row (an inactive or unexported target would leave a dangling reference).
foreach ($table in $Specs.Keys) {
    $lookups = $Specs[$table]['Lookups'] ?? @{}
    foreach ($column in $lookups.Keys) {
        $target = $lookups[$column]
        $targetIds = @($rows[$target] | ForEach-Object { Format-Guid $_.($meta[$target].PrimaryIdAttribute) })
        foreach ($row in @($rows[$table])) {
            $v = Format-Guid $row."_$($column)_value"
            if ($v -and $v -notin $targetIds) {
                throw "$table row $($row.($meta[$table].PrimaryIdAttribute)) '$($row.sprk_name)' points at $target $v through '$column', which is not exported (inactive or filtered out)."
            }
        }
    }
}

# ---------------------------------------------------------------------------------------------------------------
# 3. Table files
# ---------------------------------------------------------------------------------------------------------------
foreach ($table in $Specs.Keys) {
    $spec = $Specs[$table]
    $m = $meta[$table]
    $pk = $m.PrimaryIdAttribute
    $lookups = $spec['Lookups'] ?? @{}
    $navigation = @{}
    foreach ($rel in $m.ManyToOneRelationships) { $navigation[$rel.ReferencingAttribute] = $rel.ReferencingEntityNavigationPropertyName }

    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.Append("{`n")
    [void]$sb.Append("  `"table`": $(ConvertTo-JsonString $table),`n")
    [void]$sb.Append("  `"entitySet`": $(ConvertTo-JsonString $m.EntitySetName),`n")
    [void]$sb.Append("  `"primaryKey`": $(ConvertTo-JsonString $pk),`n")
    [void]$sb.Append("  `"nameColumn`": $(ConvertTo-JsonString $m.PrimaryNameAttribute),`n")
    [void]$sb.Append("  `"match`": $(ConvertTo-JsonString $(if ($spec.Kind -eq 'type') { 'naturalKey' } else { 'fixedId' })),`n")
    $keyTexts = @($spec.MatchKeys | ForEach-Object { '[' + ((@($_) | ForEach-Object { ConvertTo-JsonString $_ }) -join ', ') + ']' })
    [void]$sb.Append("  `"matchKeys`": [$($keyTexts -join ', ')],`n")
    [void]$sb.Append("  `"columns`": [`n" + (($scalarColumns[$table] | ForEach-Object { "    $(ConvertTo-JsonString $_)" }) -join ",`n") + "`n  ],`n")
    $lookupTexts = foreach ($column in @($lookupColumns[$table])) {
        "    { `"column`": $(ConvertTo-JsonString $column), `"navigationProperty`": $(ConvertTo-JsonString $navigation[$column]), `"target`": $(ConvertTo-JsonString $lookups[$column]) }"
    }
    [void]$sb.Append("  `"lookups`": [" + $(if ($lookupTexts) { "`n" + ($lookupTexts -join ",`n") + "`n  " } else { '' }) + "],`n")
    [void]$sb.Append("  `"rows`": [`n")
    $sorted = @($rows[$table] | Sort-Object { Format-Guid $_.$pk } -CaseSensitive)
    $rowTexts = foreach ($source in $sorted) {
        $lines = New-Object System.Collections.Generic.List[string]
        $lines.Add("      $(ConvertTo-JsonString $pk): $(ConvertTo-JsonString (Format-Guid $source.$pk))")
        $merged = @($scalarColumns[$table]) + @($lookupColumns[$table]) | Sort-Object { $_ } -CaseSensitive
        foreach ($column in $merged) {
            if ($column -in $lookupColumns[$table]) {
                $v = Format-Guid $source."_$($column)_value"
                $lines.Add("      $(ConvertTo-JsonString $column): $(if ($v) { ConvertTo-JsonString $v } else { 'null' })")
            }
            else {
                $value = $source.$column
                if ($table -eq 'sprk_playbooknode' -and $column -eq 'sprk_configjson' -and $value) { $value = Clear-ModelPin $value }
                if ($value -is [string]) { $value = Set-CorrectedChoices $table (Format-Guid $source.$pk) $column $value }
                $lines.Add("      $(ConvertTo-JsonString $column): $(ConvertTo-JsonValue $value '      ')")
            }
        }
        "    {`n" + ($lines -join ",`n") + "`n    }"
    }
    [void]$sb.Append(($rowTexts -join ",`n"))
    [void]$sb.Append($(if ($sorted.Count) { "`n" } else { '' }) + "  ]`n}`n")
    Write-Utf8Lf (Join-Path $OutputDirectory "$table.json") $sb.ToString()
    Write-Host ("{0,-24} {1,3} row(s), {2} column(s), {3} lookup(s)" -f $table, $sorted.Count, $scalarColumns[$table].Count, @($lookupColumns[$table]).Count)
}

# ---------------------------------------------------------------------------------------------------------------
# 4. N:N files
# ---------------------------------------------------------------------------------------------------------------
foreach ($assoc in $Associations) {
    $rel = Get-DataverseJson ("$api/RelationshipDefinitions(SchemaName='$($assoc.Relationship)')/Microsoft.Dynamics.CRM.ManyToManyRelationshipMetadata" +
        '?$select=SchemaName,Entity1LogicalName,Entity2LogicalName,Entity1NavigationPropertyName,Entity2NavigationPropertyName')
    $nav = if ($rel.Entity1LogicalName -eq $assoc.Source) { $rel.Entity1NavigationPropertyName } else { $rel.Entity2NavigationPropertyName }
    $sourceMeta = $meta[$assoc.Source]
    $targetPk = $meta[$assoc.Target].PrimaryIdAttribute
    $targetIds = @($rows[$assoc.Target] | ForEach-Object { Format-Guid $_.$targetPk })
    $pairs = New-Object System.Collections.Generic.List[object]
    foreach ($row in @($rows[$assoc.Source])) {
        $sourceId = Format-Guid $row.($sourceMeta.PrimaryIdAttribute)
        $linked = Get-AllRows "$api/$($sourceMeta.EntitySetName)($sourceId)/$($nav)?`$select=$targetPk"
        foreach ($link in $linked) {
            $targetId = Format-Guid $link.$targetPk
            if ($targetId -notin $targetIds) {
                throw "$($assoc.Relationship): $($assoc.Source) $sourceId '$($row.sprk_name)' is linked to $($assoc.Target) $targetId, which is not exported (inactive)."
            }
            $pairs.Add([pscustomobject]@{ Source = $sourceId; Target = $targetId })
        }
    }
    $sorted = @($pairs | Sort-Object -Property @{ Expression = 'Source' }, @{ Expression = 'Target' } -CaseSensitive)
    $pairTexts = $sorted | ForEach-Object { "    { `"source`": $(ConvertTo-JsonString $_.Source), `"target`": $(ConvertTo-JsonString $_.Target) }" }
    $text = "{`n" +
        "  `"relationship`": $(ConvertTo-JsonString $assoc.Relationship),`n" +
        "  `"source`": $(ConvertTo-JsonString $assoc.Source),`n" +
        "  `"navigationProperty`": $(ConvertTo-JsonString $nav),`n" +
        "  `"target`": $(ConvertTo-JsonString $assoc.Target),`n" +
        "  `"pairs`": [" + $(if ($sorted.Count) { "`n" + ($pairTexts -join ",`n") + "`n  " } else { '' }) + "]`n}`n"
    Write-Utf8Lf (Join-Path $OutputDirectory "associations/$($assoc.Relationship).json") $text
    Write-Host ("{0,-28} {1,3} link(s)" -f $assoc.Relationship, $sorted.Count)
}

# ---------------------------------------------------------------------------------------------------------------
# 5. Canvas file (playbook canvas layouts, written by the seeder after the nodes)
# ---------------------------------------------------------------------------------------------------------------
$canvasRows = Get-AllRows "$api/sprk_analysisplaybooks?`$select=sprk_analysisplaybookid,sprk_canvaslayoutjson"
$canvasTexts = foreach ($row in @($canvasRows | Where-Object { (Format-Guid $_.sprk_analysisplaybookid) -in $playbookIds -and $_.sprk_canvaslayoutjson } |
        Sort-Object { Format-Guid $_.sprk_analysisplaybookid } -CaseSensitive)) {
    # The canvas copy of the model pin is an authoring-environment id (see "left out" above).
    $canvas = Clear-ModelPin $row.sprk_canvaslayoutjson
    "    {`n      `"sprk_analysisplaybookid`": $(ConvertTo-JsonString (Format-Guid $row.sprk_analysisplaybookid)),`n" +
    "      `"sprk_canvaslayoutjson`": $(ConvertTo-JsonValue $canvas '      ')`n    }"
}
Write-Utf8Lf (Join-Path $OutputDirectory 'sprk_analysisplaybook.canvas.json') ("{`n  `"table`": `"sprk_analysisplaybook`",`n  `"column`": `"sprk_canvaslayoutjson`",`n  `"rows`": [`n" + ($canvasTexts -join ",`n") + "`n  ]`n}`n")
Write-Host ("{0,-28} {1,3} canvas(es)" -f 'sprk_analysisplaybook.canvas', @($canvasTexts).Count)
