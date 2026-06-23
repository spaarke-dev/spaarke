<#
.SYNOPSIS
    Seeds sprk_analysistool rows for the 8 R6 Pillar 2 typed handlers (FR-13 through FR-20).

.DESCRIPTION
    Wave-1 (pure deterministic):
      - DateExtractorHandler              (task 101, FR-17)
      - FinancialCalculatorHandler        (task 102, FR-18)
      - ClauseComparisonHandler           (task 103, FR-19)
      - FinancialCalculationToolHandler   (task 104, FR-20)

    Wave-2 (LLM-assisted):
      - EntityExtractorHandler            (task 105, FR-13)
      - ClauseAnalyzerHandler             (task 106, FR-14)
      - RiskDetectorHandler               (task 107, FR-15)
      - InvoiceExtractionToolHandler      (task 108, FR-16)

    Wave-7 (Q9 chat-tool batch migration — trivial group):
      - TextRefinementHandler             (3 rows via method-discriminator)
          TEXT-REFINE / TEXT-KEYPOINTS / TEXT-SUMMARY

    Wave-7c (citations + widget post-processing migration):
      - KnowledgeRetrievalHandler         (2 rows via method-discriminator)
          KNOWLEDGE-SOURCE-GET / KNOWLEDGE-BASE-SEARCH
      - VerifyCitationsHandler            (1 row, capability-gated via sprk_requiredcapability)
          CITATION-VERIFY (gated by 'verify_citations' capability)

    Source rows are JSON files in infra/dataverse/ (one per row, not per handler). This
    script reads each row, upserts to sprk_analysistools. Upsert key is sprk_toolcode with
    a safety filter requiring sprk_name to start with 'SYS-' (refined 2026-06-08 from the
    earlier handler-class key when Wave-7 introduced rows sharing one handler class via
    method-discriminator): toolcode is unique per row even when multiple rows share a
    handler class. The SYS- prefix prevents accidental PATCH of legacy non-R6 rows even
    if a toolcode collision occurs in the future. PATCH if drift, POST if missing.

    Wave-1, Wave-2, and Wave-7 tasks each contribute their own row JSON file to
    infra/dataverse/ and add an entry to the $RowFiles map below. Map keys are the
    sprk_toolcode (unique per row).

.PARAMETER DataverseUrl
    Dataverse environment URL. Defaults to $env:DATAVERSE_URL or Spaarke Dev.

.PARAMETER OnlyHandler
    Optional handler class name filter — seed only the row for the named handler. Useful
    when running from a single handler's task before sibling tasks have landed.

.PARAMETER WhatIf
    Preview-only mode — describes what would be created/updated without modifying Dataverse.

.EXAMPLE
    # Preview all rows
    .\Seed-TypedHandlers.ps1 -WhatIf

.EXAMPLE
    # Deploy only the FinancialCalculatorHandler row (R6 task 102)
    .\Seed-TypedHandlers.ps1 -OnlyHandler FinancialCalculatorHandler

.EXAMPLE
    # Deploy all currently-defined handler rows (idempotent — safe to re-run)
    .\Seed-TypedHandlers.ps1 -DataverseUrl "https://spaarkedev1.crm.dynamics.com"

.NOTES
    Project    : spaarke-ai-platform-unification-r6 (Pillar 2 — typed handler workstream)
    Tasks      : 101–108 (D-H-01..D-H-08)
    Pattern    : clone of scripts/Seed-AiPersonaDefault.ps1 (idempotent UPSERT exemplar)
    ADRs       : ADR-027 (sprk_ prefix), ADR-029 (BFF size unaffected — this is data-only seeding)
    Owner      : Wave-1 / Wave-2 handler PRs each add their JSON row file + map entry
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$DataverseUrl = ($env:DATAVERSE_URL ?? "https://spaarkedev1.crm.dynamics.com"),

    [Parameter(Mandatory = $false)]
    [string]$OnlyHandler,

    [Parameter(Mandatory = $false)]
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"

# R6 audit item 1 — dot-source the JSON Schema validator helper so seeds get
# write-time validation (admins catch malformed schemas immediately instead of
# silently passing them through to fail at LLM invocation).
. (Join-Path $PSScriptRoot "Test-AnalysisToolSchemaValid.ps1")

# -----------------------------------------------------------------------------
# Row map — each entry maps the handler class name to its JSON seed file.
# Wave-1 / Wave-2 sibling tasks ADD their entries here as they land.
# -----------------------------------------------------------------------------
$RepoRoot = (Resolve-Path "$PSScriptRoot/..").Path

$RowFiles = @{
    # Wave 1 — pure deterministic
    "DateExtractorHandler"             = "$RepoRoot/infra/dataverse/sprk_analysistool-date-extractor-row.json"
    "FinancialCalculatorHandler"       = "$RepoRoot/infra/dataverse/sprk_analysistool-financial-calculator-row.json"
    "ClauseComparisonHandler"          = "$RepoRoot/infra/dataverse/sprk_analysistool-clause-comparison-row.json"
    "FinancialCalculationToolHandler"  = "$RepoRoot/infra/dataverse/sprk_analysistool-financial-calculation-row.json"
    # Wave 2 — LLM-assisted
    "EntityExtractorHandler"           = "$RepoRoot/infra/dataverse/sprk_analysistool-entity-extractor-row.json"
    "ClauseAnalyzerHandler"            = "$RepoRoot/infra/dataverse/sprk_analysistool-clause-analyzer-row.json"
    "RiskDetectorHandler"              = "$RepoRoot/infra/dataverse/sprk_analysistool-risk-detector-row.json"
    "InvoiceExtractionToolHandler"     = "$RepoRoot/infra/dataverse/sprk_analysistool-invoice-extractor-row.json"
    # Wave 7 — chat-tool migration (legacy hardcoded tool → typed handler)
    "AnalysisQueryHandler"             = "$RepoRoot/infra/dataverse/sprk_analysistool-analysis-query-row.json"
    # Wave 7 — TextRefinementHandler serves 3 rows via the method-discriminator in
    # sprk_configuration (refine / keypoints / summary). Because the handler class is
    # the same for all three, the upsert key MUST be sprk_toolcode (not handler-class)
    # for these rows — see Find-ExistingRow's $ToolCode parameter (added 2026-06-08).
    # Map keys are the unique sprk_toolcode values; iteration variable is just a label.
    "TEXT-REFINE"                      = "$RepoRoot/infra/dataverse/sprk_analysistool-text-refine-row.json"
    "TEXT-KEYPOINTS"                   = "$RepoRoot/infra/dataverse/sprk_analysistool-text-keypoints-row.json"
    "TEXT-SUMMARY"                     = "$RepoRoot/infra/dataverse/sprk_analysistool-text-summary-row.json"
    # Wave 7c — KnowledgeRetrievalHandler serves 2 rows via the method discriminator in
    # sprk_configuration (GetKnowledgeSource / SearchKnowledgeBase). Same multi-row-per-
    # handler pattern as TextRefinementHandler — upsert disambiguated by sprk_toolcode.
    "KNOWLEDGE-SOURCE-GET"             = "$RepoRoot/infra/dataverse/sprk_analysistool-knowledge-source-get-row.json"
    "KNOWLEDGE-BASE-SEARCH"            = "$RepoRoot/infra/dataverse/sprk_analysistool-knowledge-base-search-row.json"
    # Wave 7c — VerifyCitationsHandler: single row, capability-gated via
    # sprk_requiredcapability = 'verify_citations'. The data-driven block's
    # IsCapabilityGateSatisfied filter in SprkChatAgentFactory.ResolveTools replaces the
    # hardcoded `if (capabilities.Contains(PlaybookCapabilities.VerifyCitations))` gate.
    "CITATION-VERIFY"                  = "$RepoRoot/infra/dataverse/sprk_analysistool-citation-verify-row.json"
    # Wave 8 — DocumentSearchHandler serves 2 rows via the method discriminator in
    # sprk_configuration (SearchDocuments / SearchDiscovery). Both rows are always-available
    # (sprk_requiredcapability = null); runtime gating comes from the handler's IRagService
    # DI dependency.
    "DOCUMENT-SEARCH"                  = "$RepoRoot/infra/dataverse/sprk_analysistool-document-search-row.json"
    "DOCUMENT-DISCOVERY"               = "$RepoRoot/infra/dataverse/sprk_analysistool-document-discovery-row.json"
    # Wave 8 — WebSearchHandler: single row, capability-gated via sprk_requiredcapability =
    # 'web_search'. Replaces the hardcoded `if (capabilities.Contains(PlaybookCapabilities.WebSearch))`
    # gate. Behavior preserved verbatim (static SemaphoreSlim(2,2), 5s HTTP timeout, mock fallback,
    # FR-10 scope guidance).
    "WEB-SEARCH"                       = "$RepoRoot/infra/dataverse/sprk_analysistool-web-search-row.json"
    # Wave 8 — CodeInterpreterHandler serves 2 rows via the method discriminator. Both
    # capability-gated via sprk_requiredcapability = 'code_interpreter'. ADR-018 kill switch
    # + ADR-016 rate limiting + ADR-015 data governance preserved by the handler.
    "CODE-ANALYZE"                     = "$RepoRoot/infra/dataverse/sprk_analysistool-code-analyze-row.json"
    "CODE-CHART"                       = "$RepoRoot/infra/dataverse/sprk_analysistool-code-chart-row.json"
    # Wave 8 — LegalResearchHandler serves 2 rows via the method discriminator. Both
    # capability-gated via sprk_requiredcapability = 'legal_research'. ADR-015 PII sanitization
    # + ADR-018 kill switch + ADR-015 telemetry hygiene preserved by the handler.
    "LEGAL-RESEARCH"                   = "$RepoRoot/infra/dataverse/sprk_analysistool-legal-research-row.json"
    "LEGAL-CASE-LOOKUP"                = "$RepoRoot/infra/dataverse/sprk_analysistool-legal-case-lookup-row.json"
    # Wave 9 — WorkingDocumentHandler serves 3 rows via the method discriminator. All 3
    # capability-gated via sprk_requiredcapability = 'write_back'. Implements ADR-033 (the
    # first invocation of the ADRs-Are-Defaults operating principle): handler reads
    # ChatInvocationContext.DocumentStreamWriter for the streaming methods (EditWorkingDocument
    # + AppendSection) and ChatInvocationContext.AnalysisId for the persistence target
    # (WriteBackToWorkingDocument). Closes Q9 chat-tool migration at 10/10.
    "WORKING-DOC-EDIT"                 = "$RepoRoot/infra/dataverse/sprk_analysistool-working-doc-edit-row.json"
    "WORKING-DOC-APPEND-SECTION"       = "$RepoRoot/infra/dataverse/sprk_analysistool-working-doc-append-section-row.json"
    "WORKING-DOC-WRITE-BACK"           = "$RepoRoot/infra/dataverse/sprk_analysistool-working-doc-write-back-row.json"
    # R6 Pillar 3 / Q11 / task 021 — InvokePlaybookHandler: single row exposing the generic
    # invoke_playbook(playbookId, parameters) chat tool. Dispatches to ANY tenant-accessible
    # playbook via the IInvokePlaybookAi facade (task 020). Replaces the specialized
    # InvokeSummarizePlaybookTool + InvokeInsightsQueryTool bridges (deleted in Wave 10 /
    # task 023). sprk_requiredcapability = null (intentional — generic dispatcher available
    # to all playbooks; per-playbook authorization enforced inside the facade + the handler's
    # tenant-visibility check via IPlaybookService). sprk_availableincontexts = Chat (100000001).
    "INVOKE-PLAYBOOK"                  = "$RepoRoot/infra/dataverse/sprk_analysistool-invoke-playbook-row.json"
    # R6 Pillar 6b / D-C-05 / task 054 — SendWorkspaceArtifactHandler: single row exposing the
    # send_workspace_artifact(widgetType, title, widgetData, matterId?) chat tool. Constructs
    # a WorkspaceTab + persists via IWorkspaceStateService.UpsertTabAsync (Pillar 6a infra
    # landed in task 053). sprk_requiredcapability = null (intentional — sending an artifact
    # is a default chat affordance; the closed 4-variant widgetType enum is the authorization
    # surface). sprk_availableincontexts = Chat (100000001) — playbook nodes write to
    # sprk_analysisoutput, not the chat-session workspace tab list.
    "SEND-WORKSPACE-ARTIFACT"          = "$RepoRoot/infra/dataverse/sprk_analysistool-send-workspace-artifact-row.json"
    # R6 Pillar 6b / D-C-06 / task 055 — UpdateWorkspaceTabHandler: single row exposing the
    # update_workspace_tab(tabId, widgetData, expectedLastUserEditAt?) chat tool. Mutates an
    # existing WorkspaceTab via IWorkspaceStateService.UpsertTabAsync with Q8 USER-WINS
    # conflict resolution (refuses with structured 'stale_read' response when the stored
    # LastUserEditAt is later than the LLM-supplied timestamp). sprk_requiredcapability = null
    # (intentional — default user affordance available in every chat session; handler-side
    # gates supply the authorization surface). sprk_availableincontexts = Chat (100000001).
    "UPDATE-WORKSPACE-TAB"             = "$RepoRoot/infra/dataverse/sprk_analysistool-update-workspace-tab-row.json"
    # R6 Pillar 7 / D-C-23 / task 069 — ManagePinnedContextHandler: single row exposing the
    # manage_pinned_context(action, pinType, title, content?) chat tool. Creates or deletes
    # PinnedContextItem rows via IPinnedContextRepository. Voice command surface (FR-47):
    # 'remember X' → create user-preference; 'always X' → create system-rule;
    # 'forget X' → delete by (pinType, title) case-insensitive match. CapabilityRouter Layer 0
    # voice command pre-pass (added in task 069) recognises the three patterns and biases the
    # LLM toward this tool. sprk_requiredcapability = null (intentional — default voice
    # affordance available in every chat session). sprk_availableincontexts = Chat (100000001).
    # ADR-015 BINDING: handler logs title length + content presence only — never the bodies.
    "MANAGE-PINNED-CONTEXT"            = "$RepoRoot/infra/dataverse/sprk_analysistool-manage-pinned-context-row.json"
    # chat-routing-redesign-r1 / Phase 4 WP5 / task 085 — RecallSessionFileHandler: single row
    # exposing the recall_session_file(fileId, purpose, query, scope, maxTokens?, requireCitations?)
    # chat tool. Load-bearing T2+T5 retrieval tool for the legal-domain trust framing — the
    # requireCitations: true default + the persona instruction injected by TrustFrameInstructionInjector
    # (task 077) ensure the agent uses citation-bearing recall rather than quoting the precomputed
    # (NOT authoritative) summary. Reads ONLY from the spaarke-session-files Azure AI Search index
    # (architecture §5.2.1 BINDING) — session-scoped RagService route, with tenantId + sessionId
    # AND-clause enforcement (ADR-014). sprk_requiredcapability = null (intentional — always-on
    # when the session has uploaded files per architecture §8.2). sprk_availableincontexts =
    # Chat (100000001) — playbook nodes do not read from ChatSession.UploadedFiles.
    # MVP-cut scope (chat-routing-redesign-r1 Q5b): this is the ONE retrieval handler shipping in
    # MVP; tasks 083/084/086/087/088/089/090 (list_session_files / get_file_manifest /
    # write_session_memory / retrieve_matter_memory / promote_to_matter_memory /
    # get_user_preferences / get_org_templates) are DEFERRED.
    "RECALL-SESSION-FILE"              = "$RepoRoot/infra/dataverse/sprk_analysistool-recall-session-file-row.json"
}

# -----------------------------------------------------------------------------
# Filter to a single handler if requested.
# -----------------------------------------------------------------------------
if ($OnlyHandler) {
    if (-not $RowFiles.ContainsKey($OnlyHandler)) {
        Write-Error "Handler '$OnlyHandler' is not registered in `$RowFiles. Known handlers: $($RowFiles.Keys -join ', ')"
        exit 1
    }
    $RowFiles = @{ $OnlyHandler = $RowFiles[$OnlyHandler] }
}

# -----------------------------------------------------------------------------
# Acquire token via az CLI (matches Seed-AiPersonaDefault.ps1 pattern).
# -----------------------------------------------------------------------------
function Get-DataverseAccessToken {
    param([string]$ResourceUrl)
    Write-Verbose "Acquiring access token for $ResourceUrl via az CLI"
    $tokenJson = az account get-access-token --resource $ResourceUrl --output json 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to acquire access token. Ensure 'az login' has been run. Output: $tokenJson"
    }
    return ($tokenJson | ConvertFrom-Json).accessToken
}

# -----------------------------------------------------------------------------
# Find existing row by sprk_handlerclass (idempotency key) WITH sprk_name 'SYS-%'
# safety filter, optionally disambiguated by sprk_toolcode.
#
# Why sprk_handlerclass + (optional) sprk_toolcode?
#   Per R6 audit item 4 consolidation (2026-06-07), sprk_handlerclass = nameof(handler)
#   is the stable runtime routing key. For 1:1 handler→row rows (Wave 1, Wave 2)
#   handler-class alone is unique. For multi-row-per-handler rows (Wave 7
#   TextRefinementHandler — 3 rows via method-discriminator, 2026-06-08), the
#   $ToolCode parameter must be supplied to disambiguate. Caller behavior:
#     - If only $HandlerClass given → match by handler-class (existing behavior).
#     - If both given → match by handler-class AND toolcode (Wave 7 path).
#
# Why the 'SYS-%' name filter?
#   Pre-R6 legacy seed-data (`scripts/seed-data/Deploy-Tools.ps1`) created
#   `Clause Analyzer`/`Date Extractor`/etc. rows with the same sprk_handlerclass
#   values but no SYS- prefix. The audit item 4 consolidation PATCHed those into
#   `SYS-*` canonical rows. The startswith filter ensures future runs only touch
#   the R6 canonical row even if some other system pathway reintroduces a
#   non-SYS handler-class collision.
# -----------------------------------------------------------------------------
function Find-ExistingRow {
    param(
        [string]$BaseUrl,
        [string]$Token,
        [string]$HandlerClass,
        [string]$ToolCode
    )
    $headers = @{
        "Authorization"      = "Bearer $Token"
        "OData-MaxVersion"   = "4.0"
        "OData-Version"      = "4.0"
        "Accept"             = "application/json"
        "Prefer"             = "odata.include-annotations=*"
    }
    $filter = "sprk_handlerclass eq '$HandlerClass' and startswith(sprk_name,'SYS-')"
    if (-not [string]::IsNullOrWhiteSpace($ToolCode)) {
        $filter = "$filter and sprk_toolcode eq '$ToolCode'"
    }
    $query = "$BaseUrl/api/data/v9.2/sprk_analysistools?`$filter=$filter&`$select=sprk_analysistoolid,sprk_name,sprk_handlerclass,sprk_toolcode"
    try {
        $response = Invoke-RestMethod -Uri $query -Headers $headers -Method Get -ErrorAction Stop
        if ($response.value -and $response.value.Count -gt 0) {
            return $response.value[0]
        }
        return $null
    }
    catch {
        Write-Warning "Query for existing row failed: $_"
        return $null
    }
}

# -----------------------------------------------------------------------------
# Build Dataverse PATCH/POST payload from the JSON row file (strip _comment_* keys).
# -----------------------------------------------------------------------------
function Get-PayloadFromRowJson {
    param([string]$JsonFilePath)

    $raw = Get-Content -Raw -Path $JsonFilePath
    $obj = $raw | ConvertFrom-Json

    $payload = [ordered]@{}
    foreach ($prop in $obj.PSObject.Properties) {
        if ($prop.Name.StartsWith("_comment")) { continue }
        # sprk_jsonschema + sprk_configuration are persisted as serialized strings.
        if ($prop.Name -in @("sprk_jsonschema", "sprk_configuration")) {
            $payload[$prop.Name] = ($prop.Value | ConvertTo-Json -Depth 50 -Compress)
        }
        else {
            $payload[$prop.Name] = $prop.Value
        }
    }
    return $payload
}

# -----------------------------------------------------------------------------
# Main upsert loop.
# -----------------------------------------------------------------------------
Write-Host ""
Write-Host "Seeding R6 Pillar 2 typed handler sprk_analysistool rows"
Write-Host "  Environment : $DataverseUrl"
Write-Host "  Rows        : $($RowFiles.Keys -join ', ')"
Write-Host "  Preview     : $WhatIf"
Write-Host ""

Write-Host "  Upsert key  : sprk_handlerclass + sprk_toolcode (when multi-row-per-handler) with sprk_name LIKE 'SYS-%' safety filter"
Write-Host ""

if (-not $WhatIf) {
    $token = Get-DataverseAccessToken -ResourceUrl $DataverseUrl
}

foreach ($rowKey in $RowFiles.Keys) {
    $jsonPath = $RowFiles[$rowKey]
    if (-not (Test-Path $jsonPath)) {
        Write-Warning "Row JSON file missing for $rowKey at $jsonPath — skipping."
        continue
    }

    $payload = Get-PayloadFromRowJson -JsonFilePath $jsonPath
    $toolCode = $payload["sprk_toolcode"]
    # NOTE: read handler class from the payload (not the map key) because Wave 7
    # rows use toolcode as the map key, since one handler class serves multiple rows.
    $handlerClass = $payload["sprk_handlerclass"]

    Write-Host "--- $rowKey ($toolCode → $handlerClass) ---"

    # R6 audit item 1: catalog-write-time JSON Schema validation. We refuse to
    # seed a row whose sprk_jsonschema is structurally invalid — admins see the
    # error here rather than at LLM invocation. The BFF is still the authoritative
    # validator at chat-session start; this is fast-feedback defense-in-depth.
    if ($payload.Contains("sprk_jsonschema") -and -not [string]::IsNullOrWhiteSpace($payload["sprk_jsonschema"])) {
        $schemaOk = Test-AnalysisToolSchemaValid -SchemaJson $payload["sprk_jsonschema"] -ToolName $toolCode
        if (-not $schemaOk) {
            Write-Error "[$toolCode] sprk_jsonschema failed structural validation. Fix the JSON in $jsonPath before re-running. (See warnings above.)"
            continue
        }
    }

    if ($WhatIf) {
        Write-Host "  [WhatIf] Would UPSERT row from $jsonPath"
        Write-Host "  sprk_name         : $($payload["sprk_name"])"
        Write-Host "  sprk_handlerclass : $($payload["sprk_handlerclass"])"
        Write-Host "  sprk_toolcode     : $($payload["sprk_toolcode"])"
        continue
    }

    # When multiple rows share a handler class (Wave 7 TextRefinementHandler),
    # pass the toolcode to disambiguate the upsert lookup.
    $existing = Find-ExistingRow -BaseUrl $DataverseUrl -Token $token -HandlerClass $handlerClass -ToolCode $toolCode

    $headers = @{
        "Authorization"      = "Bearer $token"
        "OData-MaxVersion"   = "4.0"
        "OData-Version"      = "4.0"
        "Accept"             = "application/json"
        "Content-Type"       = "application/json; charset=utf-8"
        "Prefer"             = "return=representation"
    }
    $payloadJson = ($payload | ConvertTo-Json -Depth 50 -Compress)

    if ($null -eq $existing) {
        Write-Host "  No existing row — POSTing new sprk_analysistool"
        $createUrl = "$DataverseUrl/api/data/v9.2/sprk_analysistools"
        $response = Invoke-RestMethod -Uri $createUrl -Headers $headers -Method Post -Body $payloadJson -ErrorAction Stop
        Write-Host "  Created with sprk_analysistoolid = $($response.sprk_analysistoolid)"
    }
    else {
        $existingId = $existing.sprk_analysistoolid
        Write-Host "  Existing row found (sprk_analysistoolid = $existingId) — PATCHing"
        $patchUrl = "$DataverseUrl/api/data/v9.2/sprk_analysistools($existingId)"
        Invoke-RestMethod -Uri $patchUrl -Headers $headers -Method Patch -Body $payloadJson -ErrorAction Stop | Out-Null
        Write-Host "  Patched."
    }
}

Write-Host ""
Write-Host "Done."
