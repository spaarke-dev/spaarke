# Spaarke Document Intelligence – Analysis Feature
## Design Specification

> **Version**: 1.0  
> **Date**: December 11, 2025  
> **Status**: Draft for Review  
> **Owner**: Spaarke Product / Document Intelligence  
> **Related**: [Overview](Spaarke%20Document%20Intelligence%20AI%20overview.txt)

---

## Executive Summary

This specification defines the **Analysis** feature for Spaarke Document Intelligence, enabling users to execute AI-driven analyses on documents with configurable actions, scopes (Skills, Knowledge, Tools), and outputs. The feature extends the existing SDAP BFF API architecture and leverages the proven patterns established in the Document Summary MVP (v3.2.4).

**Key Capabilities:**
- Analysis Builder UI for configuring custom AI workflows
- Two-column Analysis Workspace (editable output + source preview)
- Conversational AI refinement within Analysis context
- Playbook system for reusable analysis configurations
- Multi-output formats (Document, Email, Teams, Notifications, Workflows)

---

## 1. Architecture Context

### 1.1 Existing Foundation

The Document Intelligence Analysis feature builds on proven components:

| Component | Status | Description |
|-----------|--------|-------------|
| **Sprk.Bff.Api** | ✅ Production | ASP.NET Core 8 Minimal API, orchestrates all backend services |
| **DocumentIntelligenceService** | ✅ Production | Text extraction (native, OCR, Document Intelligence) |
| **OpenAiClient** | ✅ Production | Azure OpenAI integration with streaming support |
| **TextExtractorService** | ✅ Production | Multi-format text extraction pipeline |
| **SpeFileStore** | ✅ Production | SharePoint Embedded file access facade |
| **SSE Streaming** | ✅ Production | Server-Sent Events for real-time AI responses |
| **Rate Limiting** | ✅ Production | Per-user throttling (10/min streaming, 20/min batch) |
| **Entity Extraction** | ✅ Production | Structured data extraction to Dataverse fields |

### 1.2 Architecture Alignment

This design strictly adheres to existing ADRs and patterns:

| Principle | Implementation |
|-----------|----------------|
| **ADR-001: Minimal APIs** | All new endpoints in `Api/Ai/AnalysisEndpoints.cs` |
| **ADR-003: Lean Authorization** | Extend existing `AiAuthorizationFilter` for Analysis entities |
| **ADR-007: SpeFileStore Facade** | File access exclusively through `SpeFileStore` |
| **ADR-008: Endpoint Filters** | `AnalysisAuthorizationFilter` for per-resource checks |
| **BFF Orchestration** | BFF coordinates Dataverse + SPE + Azure AI services |
| **OBO Token Flow** | User identity preserved through all service calls |

### 1.3 Component Interaction

```
┌────────────────────────────────────────────────────────────────────────┐
│                       Dataverse Model-Driven App                       │
│  ┌─────────────────────┐          ┌──────────────────────────────────┐ │
│  │  Document Form      │          │  Analysis Workspace (Custom Page)│ │
│  │  ┌───────────────┐  │          │  ┌────────────┬──────────────┐  │ │
│  │  │ Analysis Tab  │──┼──────────┼─▶│ Working    │ Source       │  │ │
│  │  │ (grid + cmd)  │  │          │  │ Document   │ Preview      │  │ │
│  │  └───────────────┘  │          │  │ (editable) │ (read-only)  │  │ │
│  └─────────────────────┘          │  └────────────┴──────────────┘  │ │
│                                   │  │   AI Chat (refinement)       │  │ │
│  ┌─────────────────────┐          │  └──────────────────────────────┘  │ │
│  │  Analysis Builder   │          └──────────────────────────────────┘ │
│  │  (modal)            │          │                                      │
│  │  • Action selector  │          │                                      │
│  │  • Scope config     │          │                                      │
│  │  • Output options   │          │                                      │
│  └─────────────────────┘          │                                      │
└────────────────────────────────────────────────────────────────────────┘
                    │
                    │ HTTPS + Bearer Token (Entra ID)
                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│                          Sprk.Bff.Api                                   │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │  /api/ai/analysis/*  (NEW)                                       │  │
│  │  • POST /execute - Execute analysis with streaming               │  │
│  │  • POST /continue - Continue analysis via chat                   │  │
│  │  • POST /save - Save working document to SPE                     │  │
│  │  • GET /{id} - Retrieve analysis history                         │  │
│  └──────────────────────────────────────────────────────────────────┘  │
│                                                                         │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │  Services (NEW)                                                  │  │
│  │  • AnalysisOrchestrationService - Coordinates analysis execution│  │
│  │  • ScopeResolverService - Loads Skills, Knowledge, Tools        │  │
│  │  • AnalysisContextBuilder - Builds prompts from scopes          │  │
│  │  • WorkingDocumentService - Manages editable output state       │  │
│  └──────────────────────────────────────────────────────────────────┘  │
│                                                                         │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │  Existing Services (REUSE)                                       │  │
│  │  • IOpenAiClient - Azure OpenAI streaming/completions           │  │
│  │  • ITextExtractor - Text extraction pipeline                    │  │
│  │  • SpeFileStore - File access and storage                       │  │
│  │  • IDataverseService - Entity CRUD operations                   │  │
│  └──────────────────────────────────────────────────────────────────┘  │
└────────────────────────────────────────────────────────────────────────┘
                    │
                    │ OBO Token Exchange
                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│                      Azure Services                                     │
│  ┌──────────────┐  ┌──────────────┐  ┌────────────┐  ┌─────────────┐  │
│  │ Azure OpenAI │  │ Doc Intel    │  │ Dataverse  │  │ SPE         │  │
│  │ gpt-4o-mini  │  │ Text Extract │  │ Entities   │  │ Files       │  │
│  └──────────────┘  └──────────────┘  └────────────┘  └─────────────┘  │
└────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Data Model

### 2.1 New Dataverse Entities

#### **sprk_analysis** (Primary Entity)

The Analysis entity represents a single AI-executed analysis on a Document.

| Display Name | Schema Name | Type | Required | Description |
|--------------|-------------|------|----------|-------------|
| Name | `sprk_name` | Text (200) | ✅ | Analysis title/name |
| Document | `sprk_documentid` | Lookup (sprk_document) | ✅ | Parent document |
| Action | `sprk_actionid` | Lookup (sprk_analysisaction) | ✅ | Analysis action definition |
| Status | `sprk_status` | Choice | ✅ | NotStarted, InProgress, Completed, Failed |
| Status Reason | `statuscode` | Status | ✅ | Standard status reason |
| Working Document | `sprk_workingdocument` | Multiline Text | ❌ | Current working output (Markdown) |
| Final Output | `sprk_finaloutput` | Multiline Text | ❌ | Completed analysis output |
| Output File | `sprk_outputfileid` | Lookup (sprk_document) | ❌ | Saved output as new Document |
| Started On | `sprk_startedon` | DateTime | ❌ | Analysis start timestamp |
| Completed On | `sprk_completedon` | DateTime | ❌ | Analysis completion timestamp |
| Error Message | `sprk_errormessage` | Multiline Text | ❌ | Error details if failed |
| Input Tokens | `sprk_inputtokens` | Whole Number | ❌ | Token usage (input) |
| Output Tokens | `sprk_outputtokens` | Whole Number | ❌ | Token usage (output) |

**Relationships:**
- N:1 to `sprk_document` (parent)
- N:1 to `sprk_analysisaction`
- 1:N to `sprk_analysischatmessage` (chat history)
- N:N to `sprk_analysisskill` (via `sprk_analysis_skill`)
- N:N to `sprk_analysisknowledge` (via `sprk_analysis_knowledge`)
- N:N to `sprk_analysistool` (via `sprk_analysis_tool`)

#### **sprk_analysisaction** (Action Definitions)

Defines what the AI should do (e.g., "Summarize", "Review Agreement", "Prepare Response").

| Display Name | Schema Name | Type | Required | Description |
|--------------|-------------|------|----------|-------------|
| Name | `sprk_name` | Text (200) | ✅ | Action name (e.g., "Summarize Document") |
| Description | `sprk_description` | Multiline Text | ❌ | User-facing description |
| System Prompt | `sprk_systemprompt` | Multiline Text | ✅ | Base prompt template |
| Is Active | `statecode` | State | ✅ | Active/Inactive |
| Sort Order | `sprk_sortorder` | Whole Number | ❌ | Display order in UI |

#### **sprk_analysisskill** (Skills - How to Work)

Defines behavioral instructions (e.g., "Write concisely", "Use legal terminology").

| Display Name | Schema Name | Type | Required | Description |
|--------------|-------------|------|----------|-------------|
| Name | `sprk_name` | Text (200) | ✅ | Skill name |
| Description | `sprk_description` | Multiline Text | ❌ | User-facing description |
| Prompt Fragment | `sprk_promptfragment` | Multiline Text | ✅ | Instruction to add to prompt |
| Category | `sprk_category` | Choice | ❌ | Tone, Style, Format, Expertise |
| Is Active | `statecode` | State | ✅ | Active/Inactive |

#### **sprk_analysisknowledge** (Knowledge - Grounding Sources)

Defines knowledge sources for RAG (rules, policies, templates, prior work).

| Display Name | Schema Name | Type | Required | Description |
|--------------|-------------|------|----------|-------------|
| Name | `sprk_name` | Text (200) | ✅ | Knowledge source name |
| Description | `sprk_description` | Multiline Text | ❌ | User-facing description |
| Type | `sprk_type` | Choice | ✅ | Document, Rule, Template, RAG_Index |
| Content | `sprk_content` | Multiline Text | ❌ | Inline content (for rules/templates) |
| Document | `sprk_documentid` | Lookup (sprk_document) | ❌ | Reference document |
| RAG Index Name | `sprk_ragindexname` | Text (100) | ❌ | Azure AI Search index name |
| Is Active | `statecode` | State | ✅ | Active/Inactive |

#### **sprk_analysistool** (Tools - Function Helpers)

Defines reusable AI tools (extractors, analyzers, generators).

| Display Name | Schema Name | Type | Required | Description |
|--------------|-------------|------|----------|-------------|
| Name | `sprk_name` | Text (200) | ✅ | Tool name |
| Description | `sprk_description` | Multiline Text | ❌ | User-facing description |
| Tool Type | `sprk_tooltype` | Choice | ✅ | Extractor, Analyzer, Generator, Validator |
| Handler Class | `sprk_handlerclass` | Text (200) | ✅ | C# class implementing tool |
| Configuration | `sprk_configuration` | Multiline Text | ❌ | JSON config for tool |
| Is Active | `statecode` | State | ✅ | Active/Inactive |

#### **sprk_analysisplaybook** (Playbooks - Saved Configurations)

Reusable combinations of Action + Scopes + Output settings.

| Display Name | Schema Name | Type | Required | Description |
|--------------|-------------|------|----------|-------------|
| Name | `sprk_name` | Text (200) | ✅ | Playbook name |
| Description | `sprk_description` | Multiline Text | ❌ | User-facing description |
| Action | `sprk_actionid` | Lookup (sprk_analysisaction) | ✅ | Default action |
| Output Type | `sprk_outputtype` | Choice | ✅ | Document, Email, Teams, Notification, Workflow |
| Is Public | `sprk_ispublic` | Two Options | ✅ | Visible to all users |
| Is Active | `statecode` | State | ✅ | Active/Inactive |

**Relationships:**
- N:N to `sprk_analysisskill`
- N:N to `sprk_analysisknowledge`
- N:N to `sprk_analysistool`

#### **sprk_analysischatmessage** (Chat History)

Stores conversational refinement within an Analysis.

| Display Name | Schema Name | Type | Required | Description |
|--------------|-------------|------|----------|-------------|
| Analysis | `sprk_analysisid` | Lookup (sprk_analysis) | ✅ | Parent analysis |
| Role | `sprk_role` | Choice | ✅ | User, Assistant, System |
| Content | `sprk_content` | Multiline Text | ✅ | Message content |
| Created On | `createdon` | DateTime | ✅ | Message timestamp |
| Token Count | `sprk_tokencount` | Whole Number | ❌ | Tokens in this message |

### 2.2 Entity Relationship Diagram

```
┌──────────────────────┐
│  sprk_document       │
│  (EXISTING)          │
└──────────┬───────────┘
           │ 1:N
           │
           ▼
┌──────────────────────┐         ┌──────────────────────┐
│  sprk_analysis       │   N:1   │  sprk_analysisaction │
│  ──────────────────  │────────▶│  (Action Definition) │
│  • Working Document  │         └──────────────────────┘
│  • Final Output      │
│  • Status            │         ┌──────────────────────┐
│  • Token Usage       │   N:N   │  sprk_analysisskill  │
└──────────┬───────────┘◀───────▶│  (How to work)       │
           │                     └──────────────────────┘
           │ 1:N
           │                     ┌──────────────────────┐
           ▼                N:N  │ sprk_analysisknowledge│
┌──────────────────────┐◀───────▶│  (Grounding)         │
│sprk_analysischatmsg  │         └──────────────────────┘
│  (Chat History)      │
└──────────────────────┘         ┌──────────────────────┐
                            N:N  │  sprk_analysistool   │
                          ◀──────│  (Function helpers)  │
                                 └──────────────────────┘

┌──────────────────────┐
│sprk_analysisplaybook │         (Connects to Action + Scopes)
│  (Saved Config)      │◀───N:N───▶(Skills, Knowledge, Tools)
└──────────────────────┘
```

---

## 3. API Design

### 3.1 New Endpoints

All endpoints follow `POST /api/ai/analysis/*` pattern with JWT authentication and rate limiting.

#### **POST /api/ai/analysis/execute**

Execute a new analysis with Server-Sent Events streaming.

**Request:**
```json
{
  "documentId": "guid",
  "actionId": "guid",
  "skillIds": ["guid", "guid"],
  "knowledgeIds": ["guid"],
  "toolIds": ["guid"],
  "outputType": "document", // document | email | teams | notification | workflow
  "playbookId": "guid" // optional - pre-populates scopes
}
```

**Response:** SSE stream with chunks:
```
data: {"type":"metadata","analysisId":"guid","documentName":"contract.pdf"}

data: {"type":"chunk","content":"## Executive Summary\n\nThis agreement..."}

data: {"type":"chunk","content":" establishes terms between parties..."}

data: {"type":"done","analysisId":"guid","tokenUsage":{"input":1500,"output":800}}
```

**Error Codes:**
- `400` - Invalid request (missing required fields)
- `404` - Document/Action/Playbook not found
- `429` - Rate limit exceeded (10/min)
- `503` - AI service unavailable

#### **POST /api/ai/analysis/{analysisId}/continue**

Continue an existing analysis via conversational chat.

**Request:**
```json
{
  "message": "Make this more concise and focus on liability clauses"
}
```

**Response:** SSE stream with updated working document chunks

#### **POST /api/ai/analysis/{analysisId}/save**

Save working document to SharePoint Embedded and create new Document record.

**Request:**
```json
{
  "fileName": "Agreement Summary.docx",
  "format": "docx" // docx | pdf | md | txt
}
```

**Response:**
```json
{
  "documentId": "guid",
  "driveId": "b!...",
  "itemId": "01ABC...",
  "webUrl": "https://..."
}
```

#### **GET /api/ai/analysis/{analysisId}**

Retrieve analysis record with chat history.

**Response:**
```json
{
  "id": "guid",
  "documentId": "guid",
  "documentName": "contract.pdf",
  "action": { "id": "guid", "name": "Summarize" },
  "status": "completed",
  "workingDocument": "## Summary\n...",
  "finalOutput": "## Summary\n...",
  "chatHistory": [
    { "role": "user", "content": "Analyze this document", "timestamp": "2025-12-11T10:00:00Z" },
    { "role": "assistant", "content": "Here's the analysis...", "timestamp": "2025-12-11T10:00:15Z" }
  ],
  "tokenUsage": { "input": 2000, "output": 1200 },
  "startedOn": "2025-12-11T10:00:00Z",
  "completedOn": "2025-12-11T10:02:30Z"
}
```

#### **POST /api/ai/analysis/{analysisId}/export**

Export analysis output in various formats.

**Request:**
```json
{
  "format": "email", // email | teams | pdf | docx
  "options": {
    "emailTo": ["user@example.com"],
    "emailSubject": "Agreement Analysis Results",
    "includeSourceLink": true
  }
}
```

**Response:**
```json
{
  "exportType": "email",
  "success": true,
  "details": {
    "messageId": "...",
    "sentAt": "2025-12-11T10:05:00Z"
  }
}
```

### 3.2 Rate Limiting

Reuse existing policies from Document Summary:

| Endpoint Pattern | Policy | Limit |
|------------------|--------|-------|
| `/execute`, `/continue` | `ai-stream` | 10 requests/minute per user |
| `/save`, `/export` | `ai-batch` | 20 requests/minute per user |
| `/playbooks/*` (config) | None | No limit (read-heavy) |

### 3.3 Authorization

Extend existing `AiAuthorizationFilter`:

```csharp
public class AnalysisAuthorizationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context, 
        EndpointFilterDelegate next)
    {
        var analysisId = context.GetArgument<Guid>(0);
        var dataverseService = context.HttpContext.RequestServices
            .GetRequiredService<IDataverseService>();
        var userId = context.HttpContext.User.GetUserId();

        // Check: User has read access to parent Document
        var analysis = await dataverseService.GetAnalysisAsync(analysisId);
        var hasAccess = await dataverseService.CheckDocumentAccessAsync(
            analysis.DocumentId, userId);

        if (!hasAccess)
            return Results.Problem("Forbidden", statusCode: 403);

        return await next(context);
    }
}
```

---

## 4. Service Layer

### 4.1 New Services

#### **IAnalysisOrchestrationService**

Coordinates analysis execution across multiple services.

```csharp
public interface IAnalysisOrchestrationService
{
    /// <summary>
    /// Execute a new analysis with streaming results.
    /// Creates Analysis record in Dataverse and orchestrates:
    /// 1. Scope resolution (Skills, Knowledge, Tools)
    /// 2. Context building (prompt construction)
    /// 3. File extraction (via ITextExtractor)
    /// 4. AI execution (via IOpenAiClient)
    /// 5. Working document updates
    /// </summary>
    IAsyncEnumerable<AnalysisChunk> ExecuteAnalysisAsync(
        AnalysisExecutionRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Continue existing analysis via chat.
    /// Loads analysis context + chat history and streams updated output.
    /// </summary>
    IAsyncEnumerable<AnalysisChunk> ContinueAnalysisAsync(
        Guid analysisId,
        string userMessage,
        CancellationToken cancellationToken);

    /// <summary>
    /// Save working document to SPE and create Document record.
    /// </summary>
    Task<SavedDocumentResult> SaveWorkingDocumentAsync(
        Guid analysisId,
        SaveDocumentRequest request,
        CancellationToken cancellationToken);
}
```

#### **IScopeResolverService**

Loads and resolves Skills, Knowledge, and Tools.

```csharp
public interface IScopeResolverService
{
    /// <summary>
    /// Load scope definitions from Dataverse.
    /// </summary>
    Task<ResolvedScopes> ResolveScopesAsync(
        Guid[] skillIds,
        Guid[] knowledgeIds,
        Guid[] toolIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Load scopes from a Playbook.
    /// </summary>
    Task<ResolvedScopes> ResolvePlaybookScopesAsync(
        Guid playbookId,
        CancellationToken cancellationToken);
}

public record ResolvedScopes(
    AnalysisSkill[] Skills,
    AnalysisKnowledge[] Knowledge,
    AnalysisTool[] Tools);
```

#### **IAnalysisContextBuilder**

Builds prompts by combining Action + Scopes + Document content.

```csharp
public interface IAnalysisContextBuilder
{
    /// <summary>
    /// Build system prompt from Action and Skills.
    /// </summary>
    string BuildSystemPrompt(
        AnalysisAction action,
        AnalysisSkill[] skills);

    /// <summary>
    /// Build user prompt with document content and Knowledge grounding.
    /// </summary>
    Task<string> BuildUserPromptAsync(
        string documentText,
        AnalysisKnowledge[] knowledge,
        CancellationToken cancellationToken);

    /// <summary>
    /// Build continuation prompt with chat history.
    /// </summary>
    string BuildContinuationPrompt(
        ChatMessage[] history,
        string userMessage,
        string currentWorkingDocument);
}
```

#### **IWorkingDocumentService**

Manages transient working document state during analysis refinement.

```csharp
public interface IWorkingDocumentService
{
    /// <summary>
    /// Update working document in Dataverse as chunks stream in.
    /// Uses optimistic concurrency to avoid conflicts.
    /// </summary>
    Task UpdateWorkingDocumentAsync(
        Guid analysisId,
        string content,
        CancellationToken cancellationToken);

    /// <summary>
    /// Mark analysis as completed and copy working document to final output.
    /// </summary>
    Task FinalizeAnalysisAsync(
        Guid analysisId,
        int inputTokens,
        int outputTokens,
        CancellationToken cancellationToken);
}
```

### 4.2 Reused Services

The following services are used as-is from the Document Summary implementation:

| Service | Usage in Analysis |
|---------|-------------------|
| `IOpenAiClient` | Stream AI completions for analysis and chat continuation |
| `ITextExtractor` | Extract text from source document |
| `SpeFileStore` | Read source files, save output files |
| `IDataverseService` | CRUD operations on Analysis entities |

---

## 5. UI Components

### 5.1 Document Form - Analysis Tab

**Location:** Extends existing `sprk_document` main form

**Components:**
- **Analysis Grid** - Shows all analyses for this document
  - Columns: Name, Action, Status, Started On, Completed On
  - Click to open Analysis Workspace
- **Command Bar**
  - "+ New Analysis" button → Opens Analysis Builder modal

**Implementation:** Standard Dataverse form customization, no custom PCF required.

### 5.2 Analysis Builder (Modal)

**Purpose:** Configure a new analysis before execution.

**UI Structure:**
```
┌─────────────────────────────────────────────────────┐
│  New Analysis                                  [X]  │
├─────────────────────────────────────────────────────┤
│                                                     │
│  Document: contract-2025.pdf                        │
│                                                     │
│  ┌──────────────────────────────────────────────┐  │
│  │ 1. Choose Action                             │  │
│  │    ○ Summarize Document                      │  │
│  │    ○ Review Agreement                        │  │
│  │    ○ Prepare Response to Email               │  │
│  │    ○ Extract Key Terms                       │  │
│  └──────────────────────────────────────────────┘  │
│                                                     │
│  ┌──────────────────────────────────────────────┐  │
│  │ 2. Configure Scopes (Optional)               │  │
│  │                                              │  │
│  │  Skills:                                     │  │
│  │    ☑ Concise writing                         │  │
│  │    ☐ Legal terminology                       │  │
│  │    ☐ Executive-level language                │  │
│  │                                              │  │
│  │  Knowledge:                                  │  │
│  │    ☑ Company policies                        │  │
│  │    ☐ Prior agreements (RAG)                  │  │
│  │                                              │  │
│  │  Tools:                                      │  │
│  │    ☐ Entity extractor                        │  │
│  │    ☐ Clause analyzer                         │  │
│  └──────────────────────────────────────────────┘  │
│                                                     │
│  ┌──────────────────────────────────────────────┐  │
│  │ 3. Output Options                            │  │
│  │    ● Working Document (default)              │  │
│  │    ○ Email draft                             │  │
│  │    ○ Teams message                           │  │
│  │    ○ Workflow trigger                        │  │
│  └──────────────────────────────────────────────┘  │
│                                                     │
│  Or use a Playbook:                                │
│  [Select Playbook ▼]                               │
│                                                     │
│                     [Cancel]  [Start Analysis]     │
└─────────────────────────────────────────────────────┘
```

**Implementation:** 
- Power Apps Canvas component embedded in Custom Page
- Calls `/api/ai/analysis/execute` on submit
- Redirects to Analysis Workspace on success

### 5.3 Analysis Workspace (Custom Page)

**Purpose:** Interactive workspace for viewing, editing, and refining analysis output.

**UI Structure:**
```
┌──────────────────────────────────────────────────────────────────────┐
│  Analysis: Agreement Summary - contract-2025.pdf            [Save ▼] │
├───────────────────────────────┬──────────────────────────────────────┤
│  Working Document             │  Source Document                    │
│  (Editable)                   │  (Read-only Preview)                │
│ ┌───────────────────────────┐ │ ┌────────────────────────────────┐ │
│ │ ## Executive Summary      │ │ │                                │ │
│ │                           │ │ │   [PDF/DOCX Preview]           │ │
│ │ This Service Agreement    │ │ │                                │ │
│ │ between ABC Corp and...   │ │ │   (via SpeFileViewer PCF)      │ │
│ │                           │ │ │                                │ │
│ │ ## Key Terms              │ │ │                                │ │
│ │ - Term: 12 months         │ │ │                                │ │
│ │ - Fees: $50,000           │ │ │                                │ │
│ │                           │ │ │                                │ │
│ │ ## Risk Assessment        │ │ │                                │ │
│ │ [User can edit here]      │ │ │                                │ │
│ │                           │ │ │                                │ │
│ └───────────────────────────┘ │ └────────────────────────────────┘ │
├───────────────────────────────┴──────────────────────────────────────┤
│  AI Assistant                                                        │
│ ┌────────────────────────────────────────────────────────────────┐  │
│ │ You: Make this more concise and focus on liability              │  │
│ │                                                                  │  │
│ │ AI: I'll revise the summary to be more concise...              │  │
│ │     [Streaming response appears in real-time]                  │  │
│ │                                                                  │  │
│ │ [Type your message...                              ] [Send →]   │  │
│ └────────────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────────┘
```

**Key Features:**
1. **Two-Column Layout**
   - Left: Monaco editor (or rich text) for working document
   - Right: SpeFileViewer PCF showing source document preview
   - Resizable split pane

2. **AI Chat Panel**
   - Conversational refinement interface
   - SSE streaming for real-time responses
   - Working document updates in left pane as AI responds

3. **Save Options**
   - Save as new Document (DOCX/PDF/MD)
   - Email draft
   - Teams message
   - Export to workflow

**Implementation:**
- Power Apps Custom Page
- Reuse existing `SpeFileViewer` PCF for source preview
- New `AnalysisWorkspace` PCF (React) for working document + chat
- Calls `/api/ai/analysis/{id}/continue` for chat
- Calls `/api/ai/analysis/{id}/save` for export

---

## 6. Prompt Engineering

### 6.1 System Prompt Construction

The system prompt combines Action + Skills:

```
{Action.SystemPrompt}

## Instructions

{foreach Skill in Skills}
- {Skill.PromptFragment}
{/foreach}

## Output Format

Provide your analysis in Markdown format with appropriate headings and structure.
```

**Example Result:**
```
You are an AI assistant helping legal professionals analyze documents.

## Instructions

- Write in a concise, professional tone suitable for executive review
- Use legal terminology when appropriate but explain complex terms
- Structure your analysis with clear headings and bullet points
- Focus on identifying key obligations, deadlines, and risk factors

## Output Format

Provide your analysis in Markdown format with appropriate headings and structure.
```

### 6.2 User Prompt with Knowledge Grounding

```
# Document to Analyze

{DocumentText}

{if Knowledge contains RAG indexes}
# Related Context

I've reviewed similar documents and found these relevant points:
{RAG search results}
{/if}

{if Knowledge contains inline rules/templates}
# Reference Materials

{foreach Knowledge item}
## {Knowledge.Name}
{Knowledge.Content}
{/foreach}
{/if}

---

Please analyze the document above according to the instructions.
```

### 6.3 Continuation Prompt

```
# Current Analysis

{WorkingDocument}

# Conversation History

{foreach message in ChatHistory}
{message.Role}: {message.Content}
{/foreach}

# New Request

User: {UserMessage}

Please update the analysis based on this feedback. Provide the complete updated analysis, not just the changes.
```

---

## 7. Implementation Phases

### Phase 1: Core Infrastructure (Week 1-2)

**Goal:** Establish data model and basic API endpoints

**Tasks:**
1. Create Dataverse entities (Analysis, Action, Skill, Knowledge, Tool)
2. Implement `AnalysisEndpoints.cs` with `/execute`, `/continue`, `/save`
3. Implement `AnalysisOrchestrationService` (basic flow)
4. Implement `ScopeResolverService`
5. Implement `AnalysisContextBuilder`
6. Add unit tests

**Acceptance Criteria:**
- API endpoints return 200/202/404/400 correctly
- Analysis records created in Dataverse
- SSE streaming works for `/execute`

### Phase 2: UI Components (Week 3-4)

**Goal:** Build user-facing UI for Analysis creation and workspace

**Tasks:**
1. Extend Document form with Analysis tab + grid
2. Build Analysis Builder modal (Canvas component)
3. Build Analysis Workspace custom page
4. Build `AnalysisWorkspace` PCF component (React)
5. Integrate with `/api/ai/analysis/*` endpoints
6. Add error handling and loading states

**Acceptance Criteria:**
- Users can create Analysis from Document form
- Analysis Builder shows Actions, Skills, Knowledge
- Analysis Workspace displays two-column layout
- Chat interface streams responses in real-time

### Phase 3: Scope System (Week 5-6)

**Goal:** Implement Skills, Knowledge, Tools configuration

**Tasks:**
1. Build admin UI for managing Actions, Skills, Knowledge, Tools
2. Implement Knowledge RAG integration (Azure AI Search)
3. Implement Tool handler framework
4. Build sample Skills and Knowledge entries
5. Test prompt construction with various scope combinations

**Acceptance Criteria:**
- Admins can create/edit Skills, Knowledge, Tools
- RAG Knowledge sources retrieve relevant context
- Prompt templates correctly combine scopes
- Tools execute successfully when called

### Phase 4: Playbooks & Export (Week 7-8)

**Goal:** Reusable configurations and multi-format output

**Tasks:**
1. Implement Playbook entity and associations
2. Add Playbook selector to Analysis Builder
3. Implement export formats (DOCX, PDF, Email, Teams)
4. Build email composition integration
5. Build Teams message integration
6. Add workflow trigger support

**Acceptance Criteria:**
- Users can save and load Playbooks
- Export to DOCX/PDF creates valid files
- Email integration pre-populates composition window
- Teams integration posts to channels

### Phase 5: Polish & Optimization (Week 9-10)

**Goal:** Production readiness

**Tasks:**
1. Implement Redis caching for Scopes
2. Add telemetry and Application Insights tracking
3. Optimize token usage (caching, compression)
4. Add comprehensive error handling
5. Performance testing (load, stress)
6. Security review (authorization, data protection)
7. Documentation and training materials

**Acceptance Criteria:**
- System handles 100+ concurrent analyses
- Average response time < 2s for streaming start
- All endpoints have proper error handling
- Security review completed with no critical issues

---

## 8. Non-Functional Requirements

### 8.1 Performance

| Metric | Target | Measurement |
|--------|--------|-------------|
| SSE stream start latency | < 2 seconds | 95th percentile |
| Token throughput | > 50 tokens/second | Average |
| Concurrent analyses per user | 3 simultaneous | Hard limit |
| Working document save | < 500ms | 95th percentile |
| Analysis history load | < 1 second | 95th percentile |

### 8.2 Scalability

| Dimension | Limit | Notes |
|-----------|-------|-------|
| Analyses per Document | Unlimited | Pagination in UI |
| Chat messages per Analysis | 1000 | Soft limit, oldest pruned |
| Working document size | 100KB | Markdown text |
| Knowledge sources per Analysis | 10 | Hard limit (context window) |
| Skills per Analysis | 5 | Hard limit (prompt clarity) |

### 8.3 Security

| Requirement | Implementation |
|-------------|----------------|
| Authentication | Entra ID JWT tokens (existing) |
| Authorization | `AnalysisAuthorizationFilter` checks Document access |
| Data isolation | Multi-tenant via Dataverse security roles |
| Token protection | Azure Key Vault for API keys |
| Audit logging | All Analysis operations logged to App Insights |
| PII handling | No PII in telemetry; content stays in Dataverse/SPE |

### 8.4 Monitoring

**Key Metrics:**
- Analysis execution success rate (target: > 95%)
- Average token usage per analysis
- Rate limit hit rate (target: < 5% of requests)
- OpenAI API error rate (target: < 1%)
- Working document save failures (target: < 0.1%)

**Alerts:**
- Circuit breaker open for OpenAI API
- Analysis failure rate > 10% over 5 minutes
- Rate limit rejections > 20% over 1 minute
- Average response time > 5 seconds

---

## 9. Risk Assessment

### 9.1 Technical Risks

| Risk | Severity | Mitigation |
|------|----------|------------|
| **Context Window Limits** | High | Implement Knowledge pruning, summarize long chat histories |
| **Token Costs** | Medium | Monitor usage, implement budget alerts, cache common prompts |
| **RAG Performance** | Medium | Pre-index documents, use hybrid search, cache embeddings |
| **UI Complexity** | Medium | Phased rollout, extensive user testing, fallback to simple mode |
| **Tool Handler Errors** | Low | Graceful degradation, try-catch all tool calls, log failures |

### 9.2 User Experience Risks

| Risk | Severity | Mitigation |
|------|----------|------------|
| **Prompt Confusion** | Medium | Provide clear examples, in-app tooltips, sample Playbooks |
| **Output Quality** | High | Test extensively with real documents, tune prompts iteratively |
| **Overwhelming Options** | Medium | Start with 3-5 pre-built Playbooks, hide advanced options initially |
| **Slow Responses** | Medium | Set expectations (progress indicators), optimize backend |

### 9.3 Business Risks

| Risk | Severity | Mitigation |
|------|----------|------------|
| **Adoption Challenges** | High | Change management, training, clear value proposition |
| **Cost Overruns** | Medium | Implement cost tracking, per-customer budgets, alerts |
| **Compliance Concerns** | High | Legal review of AI usage, data residency compliance, audit trail |

---

## 10. Testing Strategy

### 10.1 Unit Tests

**Services:**
- `AnalysisOrchestrationService` - Mock all dependencies
- `ScopeResolverService` - Test scope loading and caching
- `AnalysisContextBuilder` - Verify prompt construction
- `WorkingDocumentService` - Test concurrency handling

**Target:** 80% code coverage

### 10.2 Integration Tests

**API Endpoints:**
- `/execute` - End-to-end with test document and scopes
- `/continue` - Chat continuation with history
- `/save` - File creation in SPE test container
- Authorization filters - Access control scenarios

**Target:** All happy path + error scenarios covered

### 10.3 E2E Tests

**User Scenarios:**
1. Create Analysis from Document form
2. Execute Analysis with default settings
3. Refine Analysis via chat
4. Save output as new Document
5. Export Analysis to Email
6. Use Playbook for quick Analysis

**Target:** All critical user journeys automated

### 10.4 Performance Tests

**Load Tests:**
- 50 concurrent analysis executions
- 100 chat messages over 1 minute
- 500 analysis history loads

**Stress Tests:**
- Large documents (10MB PDF)
- Long chat histories (100+ messages)
- Complex scopes (5 Skills + 10 Knowledge sources)

---

## 11. Open Questions

1. **Knowledge RAG Integration:** Do we provision a new Azure AI Search index per customer, or use a shared index with tenant filters?
   - **Recommendation:** Shared index with `customerId` filter for cost efficiency

2. **Working Document Versioning:** Should we keep versions of working document as user refines?
   - **Recommendation:** Phase 2 feature - store snapshots after each chat interaction

3. **Tool Handler Security:** How do we sandbox custom Tool handlers to prevent malicious code?
   - **Recommendation:** Phase 1 uses pre-built tools only; Phase 3 explores sandboxing

4. **Email Integration:** Power Apps email dialog or direct Graph API send?
   - **Recommendation:** Start with Graph API for programmatic control

5. **Multi-Document Analyses:** Should Phase 1 support analyzing multiple documents together?
   - **Recommendation:** No, explicitly out of scope for MVP

---

## 12. Success Criteria

### 12.1 Technical Success

- [ ] All API endpoints operational with < 2s P95 latency
- [ ] SSE streaming works reliably across browsers
- [ ] Analysis records persist correctly in Dataverse
- [ ] File export works for DOCX, PDF, Email
- [ ] Rate limiting prevents abuse
- [ ] Authorization prevents unauthorized access

### 12.2 User Success

- [ ] Users can create Analysis in < 5 clicks
- [ ] Analysis Workspace loads in < 3 seconds
- [ ] Chat refinement produces improved outputs
- [ ] Playbooks reduce configuration time by 80%
- [ ] Export options meet 90% of use cases

### 12.3 Business Success

- [ ] 50% of documents have at least one Analysis within 30 days
- [ ] 80% of Analyses reach "Completed" status
- [ ] Token costs stay within $0.10/document budget
- [ ] User satisfaction score > 4/5
- [ ] No critical security incidents

---

## 13. Appendices

### Appendix A: Glossary

| Term | Definition |
|------|------------|
| **Analysis** | A single AI-executed action on a Document |
| **Action** | Defines what the AI should do (e.g., Summarize) |
| **Skill** | Defines how the AI should work (e.g., tone, style) |
| **Knowledge** | Grounding sources for RAG (rules, templates, prior work) |
| **Tool** | Function-style helper for extraction/analysis |
| **Playbook** | Reusable configuration of Action + Scopes + Output |
| **Working Document** | Editable in-progress analysis output |
| **Final Output** | Completed analysis result |
| **Scope** | Collective term for Skills, Knowledge, and Tools |

### Appendix B: Reference Architecture Documents

| Document | Purpose |
|----------|---------|
| [SPAARKE-AI-ARCHITECTURE.md](../docs/ai-knowledge/guides/SPAARKE-AI-ARCHITECTURE.md) | Overall AI architecture principles |
| [sdap-bff-api-patterns.md](../docs/ai-knowledge/architecture/sdap-bff-api-patterns.md) | BFF API patterns and conventions |
| [sdap-component-interactions.md](../docs/ai-knowledge/architecture/sdap-component-interactions.md) | Component interaction patterns |
| [auth-AI-azure-resources.md](../docs/ai-knowledge/architecture/auth-AI-azure-resources.md) | Azure AI service configuration |
| [ai-document-summary.md](../docs/guides/ai-document-summary.md) | Document Summary API reference |
| [ai-troubleshooting.md](../docs/guides/ai-troubleshooting.md) | AI feature troubleshooting guide |

### Appendix C: Related ADRs

| ADR | Title | Relevance |
|-----|-------|-----------|
| ADR-001 | Minimal APIs + BackgroundService | Endpoint implementation pattern |
| ADR-003 | Lean Authorization Seams | Authorization filter design |
| ADR-007 | SpeFileStore Facade | File access abstraction |
| ADR-008 | Endpoint Filters | Per-resource authorization |
| ADR-009 | Redis-First Caching | Scope caching strategy |
| ADR-013 | AI Architecture | AI feature architecture principles |

---

**Document Status:** Draft for Review  
**Next Steps:** Review with engineering team, validate feasibility, refine estimates  
**Target Review Date:** December 13, 2025
