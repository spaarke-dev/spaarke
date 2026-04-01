<#
.SYNOPSIS
    Update Dataverse entity and attribute descriptions to educate M365 Copilot
    about Spaarke's data model and vocabulary.

.DESCRIPTION
    M365 Copilot reads Dataverse table and column descriptions to understand
    what data exists and how to query it. This script sets rich, AI-optimized
    descriptions on all Spaarke entities so Copilot correctly maps user
    vocabulary like tasks, documents, and matters to Spaarke entities.

    CRITICAL: Without these descriptions, Copilot defaults to standard Dataverse
    entities like Task activity instead of Spaarke's custom entities.

.PARAMETER EnvironmentUrl
    Dataverse environment URL. Default: https://spaarkedev1.crm.dynamics.com

.EXAMPLE
    .\Update-CopilotEntityDescriptions.ps1
    .\Update-CopilotEntityDescriptions.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com"
#>

param(
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com"
)

$ErrorActionPreference = "Stop"

# ============================================================================
# AUTH
# ============================================================================

Write-Host "`n=== Spaarke Copilot Entity Description Updater ===" -ForegroundColor Cyan
Write-Host "Environment: $EnvironmentUrl`n"

$azCmd = "C:\Program Files\Microsoft SDKs\Azure\CLI2\wbin\az.cmd"
$token = & $azCmd account get-access-token --resource $EnvironmentUrl --query "accessToken" -o tsv
if (-not $token) {
    Write-Error "Failed to get access token. Run 'az login' first."
    return
}

$headers = @{
    "Authorization"    = "Bearer $token"
    "OData-MaxVersion" = "4.0"
    "OData-Version"    = "4.0"
    "Accept"           = "application/json"
    "Content-Type"     = "application/json; charset=utf-8"
}

$apiBase = "$EnvironmentUrl/api/data/v9.2"

# ============================================================================
# HELPER FUNCTIONS
# ============================================================================

function New-Label {
    param([string]$Text)
    return @{
        "@odata.type"     = "Microsoft.Dynamics.CRM.Label"
        "LocalizedLabels" = @(
            @{
                "@odata.type"  = "Microsoft.Dynamics.CRM.LocalizedLabel"
                "Label"        = $Text
                "LanguageCode" = 1033
            }
        )
    }
}

function Update-EntityDescription {
    param(
        [string]$LogicalName,
        [string]$Description,
        [string]$DisplayName = $null,
        [string]$DisplayCollectionName = $null
    )

    Write-Host "  Updating: $LogicalName" -NoNewline

    $body = @{
        "Description" = New-Label -Text $Description
    }

    if ($DisplayName) {
        $body["DisplayName"] = New-Label -Text $DisplayName
    }
    if ($DisplayCollectionName) {
        $body["DisplayCollectionName"] = New-Label -Text $DisplayCollectionName
    }

    $json = $body | ConvertTo-Json -Depth 10
    $uri = "$apiBase/EntityDefinitions(LogicalName='$LogicalName')"

    try {
        Invoke-RestMethod -Uri $uri -Method Put -Headers $headers -Body $json | Out-Null
        Write-Host " ... OK" -ForegroundColor Green
    }
    catch {
        $status = $_.Exception.Response.StatusCode
        Write-Host " ... FAILED ($status)" -ForegroundColor Red
        if ($_.Exception.Response) {
            $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
            $errorBody = $reader.ReadToEnd()
            Write-Host "    Error: $($errorBody.Substring(0, [Math]::Min(200, $errorBody.Length)))" -ForegroundColor Yellow
        }
    }
}

function Update-AttributeDescription {
    param(
        [string]$EntityLogicalName,
        [string]$AttributeLogicalName,
        [string]$Description,
        [string]$DisplayName = $null
    )

    Write-Host "    - $AttributeLogicalName" -NoNewline

    $body = @{
        "Description" = New-Label -Text $Description
    }

    if ($DisplayName) {
        $body["DisplayName"] = New-Label -Text $DisplayName
    }

    $json = $body | ConvertTo-Json -Depth 10
    $uri = "$apiBase/EntityDefinitions(LogicalName='$EntityLogicalName')/Attributes(LogicalName='$AttributeLogicalName')"

    try {
        Invoke-RestMethod -Uri $uri -Method Put -Headers $headers -Body $json | Out-Null
        Write-Host " ... OK" -ForegroundColor Green
    }
    catch {
        $status = $_.Exception.Response.StatusCode
        Write-Host " ... SKIP ($status)" -ForegroundColor Yellow
    }
}

# ============================================================================
# ENTITY DESCRIPTIONS
# Copilot reads these to understand what each table contains.
# Written in natural language optimized for AI comprehension.
# ============================================================================

Write-Host "`n[1/3] Updating entity (table) descriptions..." -ForegroundColor Cyan

# --- EVENTS (Tasks/Deadlines) ---
Update-EntityDescription -LogicalName "sprk_event" `
    -DisplayName "Event" `
    -DisplayCollectionName "Events" `
    -Description "Tasks, deadlines, action items, and appointments in Spaarke. When users ask about tasks, to-dos, deadlines, assignments, or due dates, query this table - NOT the standard Dataverse Task or Activity entities. Events have an assignee, due date, status, event type, and belong to a Matter or Project. Filter by status and ownerid to find a user's overdue or upcoming tasks."

# --- MATTERS ---
Update-EntityDescription -LogicalName "sprk_matter" `
    -DisplayName "Matter" `
    -DisplayCollectionName "Matters" `
    -Description "Legal matters and cases in Spaarke. A Matter is the primary organizing entity - it contains Projects, Events aka tasks and deadlines, Documents, and Parties. When users ask about 'matters', 'cases', 'litigation', or 'deals', query this table. Matters have a name, status, type such as litigation, transactional, or advisory, responsible attorney, and client. Related entities: sprk_project workstreams within a matter, sprk_event for tasks and deadlines, sprk_document for files and contracts."

# --- DOCUMENTS ---
Update-EntityDescription -LogicalName "sprk_document" `
    -DisplayName "Document" `
    -DisplayCollectionName "Documents" `
    -Description "Document records in Spaarke linked to files stored in SharePoint Embedded SPE. When users ask about 'documents', 'files', 'contracts', 'agreements', or 'attachments', query this table. Documents have a name, file name, document type from Document Profile AI classification: contract, lease, NDA, invoice, letter, etc., matter association, upload date, and page count. To search document CONTENT, use the Spaarke API semantic search function instead of querying this table directly."

# --- PROJECTS ---
Update-EntityDescription -LogicalName "sprk_project" `
    -DisplayName "Project" `
    -DisplayCollectionName "Projects" `
    -Description "Workstreams within a Matter in Spaarke. Projects group related tasks and documents under a matter such as Due Diligence, Contract Review, or Closing. When users ask about 'projects' or 'workstreams', query this table. Projects have a name, status, matter association, and related events and documents."

# --- ANALYSIS PLAYBOOKS ---
Update-EntityDescription -LogicalName "sprk_analysisplaybook" `
    -DisplayName "Analysis Playbook" `
    -DisplayCollectionName "Analysis Playbooks" `
    -Description "AI analysis playbook definitions in Spaarke. When users ask about 'playbooks', 'analysis tools', 'AI capabilities', or 'what can you analyze', query this table. Playbooks are reusable multi-step AI analysis workflows such as Lease Review, Risk Scan, NDA Analysis, and Contract Review. They have a name, description, category, applicable document types, and can be public or private."

# --- ANALYSIS OUTPUT ---
Update-EntityDescription -LogicalName "sprk_analysisoutput" `
    -DisplayName "Analysis Output" `
    -DisplayCollectionName "Analysis Outputs" `
    -Description "Results from AI playbook executions in Spaarke. Contains findings, risk flags, key terms, recommendations, and structured analysis output. When users ask about 'analysis results', 'findings', or 'what did the analysis show', query this table. Linked to a document and playbook."

# --- PARTIES ---
Update-EntityDescription -LogicalName "sprk_party" `
    -DisplayName "Party" `
    -DisplayCollectionName "Parties" `
    -Description "People and organizations associated with a Matter in a specific role. Parties have a role such as client, opposing counsel, outside counsel, witness, expert, or judge, contact information, and matter association. When users ask about 'counsel', 'parties', 'contacts on a matter', or 'who is involved', query this table."

# --- CONTAINERS ---
Update-EntityDescription -LogicalName "sprk_container" `
    -DisplayName "Container" `
    -DisplayCollectionName "Containers" `
    -Description "SharePoint Embedded storage containers for document files. Each Matter or Project has an associated container. This is an internal system table - users do not query it directly. Documents stored in containers are accessed through the sprk_document entity and the Spaarke BFF API."

# --- INVOICES ---
Update-EntityDescription -LogicalName "sprk_invoice" `
    -DisplayName "Invoice" `
    -DisplayCollectionName "Invoices" `
    -Description "Legal invoices and billing records associated with Matters. When users ask about 'invoices', 'bills', 'costs', or 'spending', query this table. Invoices have an amount, vendor, matter association, status, and date."

# --- COMMUNICATIONS ---
Update-EntityDescription -LogicalName "sprk_communication" `
    -DisplayName "Communication" `
    -DisplayCollectionName "Communications" `
    -Description "Email communications and correspondence related to Matters. When users ask about 'emails', 'correspondence', or 'communications', query this table. Communications have a subject, body, sender, recipients, matter association, and sent date."

Write-Host ""

# ============================================================================
# KEY ATTRIBUTE DESCRIPTIONS
# ============================================================================

Write-Host "[2/3] Updating key attribute descriptions..." -ForegroundColor Cyan

# sprk_event attributes
Update-AttributeDescription -EntityLogicalName "sprk_event" -AttributeLogicalName "sprk_name" `
    -Description "The title or name of this task, deadline, or appointment"

Update-AttributeDescription -EntityLogicalName "sprk_event" -AttributeLogicalName "sprk_duedate" `
    -Description "When this task or deadline is due. Compare with today's date to find overdue items." `
    -DisplayName "Due Date"

Update-AttributeDescription -EntityLogicalName "sprk_event" -AttributeLogicalName "sprk_status" `
    -Description "Current status: Active - not yet done, Completed - done, Cancelled, or Overdue - past due date and not completed"

Update-AttributeDescription -EntityLogicalName "sprk_event" -AttributeLogicalName "sprk_eventtype" `
    -Description "Type of event: Task - action item, Deadline - hard due date, Appointment - scheduled meeting, Milestone - project milestone" `
    -DisplayName "Event Type"

Update-AttributeDescription -EntityLogicalName "sprk_event" -AttributeLogicalName "sprk_matterid" `
    -Description "The Matter or case this task belongs to" `
    -DisplayName "Matter"

# sprk_matter attributes
Update-AttributeDescription -EntityLogicalName "sprk_matter" -AttributeLogicalName "sprk_name" `
    -Description "The name or title of this legal matter or case"

Update-AttributeDescription -EntityLogicalName "sprk_matter" -AttributeLogicalName "sprk_status" `
    -Description "Current status of the matter: Active, Closed, On Hold, Pending"

Update-AttributeDescription -EntityLogicalName "sprk_matter" -AttributeLogicalName "sprk_mattertype" `
    -Description "Type of legal matter: Litigation, Transactional, Advisory, Regulatory, IP" `
    -DisplayName "Matter Type"

# sprk_document attributes
Update-AttributeDescription -EntityLogicalName "sprk_document" -AttributeLogicalName "sprk_name" `
    -Description "The display name of this document"

Update-AttributeDescription -EntityLogicalName "sprk_document" -AttributeLogicalName "sprk_documenttype" `
    -Description "Classification of this document: Contract, Lease, NDA, Invoice, Letter, Memo, Court Filing, etc. Set by Document Profile AI classification." `
    -DisplayName "Document Type"

Update-AttributeDescription -EntityLogicalName "sprk_document" -AttributeLogicalName "sprk_matterid" `
    -Description "The Matter or case this document belongs to" `
    -DisplayName "Matter"

Write-Host ""

# ============================================================================
# PUBLISH CHANGES
# ============================================================================

Write-Host "[3/3] Publishing customizations..." -ForegroundColor Cyan

$publishBody = @{
    "ParameterXml" = "<importexportxml><entities><entity>sprk_event</entity><entity>sprk_matter</entity><entity>sprk_document</entity><entity>sprk_project</entity><entity>sprk_analysisplaybook</entity><entity>sprk_analysisoutput</entity><entity>sprk_party</entity><entity>sprk_container</entity><entity>sprk_invoice</entity><entity>sprk_communication</entity></entities></importexportxml>"
} | ConvertTo-Json

try {
    Invoke-RestMethod -Uri "$apiBase/PublishXml" -Method Post -Headers $headers -Body $publishBody | Out-Null
    Write-Host "  Published successfully" -ForegroundColor Green
}
catch {
    Write-Host "  Publish failed - changes saved but may require manual publish" -ForegroundColor Yellow
}

# ============================================================================
# SUMMARY
# ============================================================================

Write-Host "`n=== Complete ===" -ForegroundColor Cyan
Write-Host "Updated descriptions for 10 entities and 12 key attributes."
Write-Host "M365 Copilot will now understand Spaarke's data model."
Write-Host ""
Write-Host "Key vocabulary mappings now in Dataverse metadata:"
Write-Host "  'tasks/to-dos/deadlines'  -> sprk_event NOT Dataverse Task"
Write-Host "  'matters/cases'           -> sprk_matter"
Write-Host "  'documents/files'         -> sprk_document"
Write-Host "  'playbooks/analysis tools' -> sprk_analysisplaybook"
Write-Host "  'invoices/bills'          -> sprk_invoice"
Write-Host "  'emails/communications'   -> sprk_communication"
Write-Host ""
Write-Host "Note: Copilot may take 15-30 minutes to pick up the new descriptions."
Write-Host ""
