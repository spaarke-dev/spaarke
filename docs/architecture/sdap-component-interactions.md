# Component Interactions Guide

> **Purpose**: Help AI coding agents understand how Spaarke components interact, so changes to one component can be evaluated for impact on others.
> **Last Updated**: January 2026

---

## Quick Impact Reference

When modifying a component, check this table for potential downstream effects:

| If You Change... | Check Impact On... |
|------------------|-------------------|
| BFF API endpoints | PCF controls, tests, API documentation |
| BFF authentication | PCF auth config, Dataverse plugin auth |
| PCF control API calls | BFF endpoint contracts |
| Dataverse entity schema | BFF Dataverse queries, PCF form bindings |
| Bicep modules | Environment configs, deployment pipelines |
| Shared libraries | All consumers (search for ProjectReference) |
| Email processing options | Webhook handler, polling service, job handler |
| Email filter rules schema | EmailFilterService, EmailRuleSeedService |
| Webhook endpoint | Dataverse Service Endpoint registration |

---

## Component Interaction Diagram

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        USER INTERACTION LAYER                                │
│  ┌────────────────────────────────────────────────────────────────────────┐ │
│  │  Dataverse Model-Driven App (Browser)                                  │ │
│  │  └─ PCF Controls render in forms                                       │ │
│  └────────────────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                    ┌───────────────┼───────────────┐
                    │               │               │
                    ▼               ▼               ▼
┌──────────────────────┐ ┌──────────────────┐ ┌──────────────────────────────┐
│  UniversalQuickCreate│ │  SpeFileViewer   │ │  UniversalDatasetGrid       │
│  PCF Control         │ │  PCF Control     │ │  PCF Control                │
│  ──────────────────  │ │  ──────────────  │ │  ────────────────────────── │
│  • Upload documents  │ │  • Preview files │ │  • Display entity records   │
│  • Extract metadata  │ │  • Office Online │ │  • Custom grid rendering    │
│  • Create records    │ │  • Download      │ │                             │
└──────────────────────┘ └──────────────────┘ └──────────────────────────────┘
           │                      │                        │
           │   MSAL.js Token      │                        │
           │   (OBO flow)         │                        │
           ▼                      ▼                        ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                          SPRK.BFF.API                                        │
│  ┌─────────────────────────────────────────────────────────────────────────┐│
│  │  Endpoints (Api/)                                                       ││
│  │  • POST /upload/file, /upload/session    ← Document uploads             ││
│  │  • GET  /api/containers/{id}/children    ← File listing                 ││
│  │  • GET  /api/documents/{id}/preview-url  ← Preview URLs                 ││
│  │  • GET  /api/navmap/{entity}/lookup      ← Metadata discovery           ││
│  │  • POST /api/v1/emails/{id}/save-as-document  ← Email-to-document       ││
│  │  • GET  /api/v1/emails/{id}/document-status   ← Email status check      ││
│  └─────────────────────────────────────────────────────────────────────────┘│
│  ┌─────────────────────────────────────────────────────────────────────────┐│
│  │  Infrastructure Layer                                                   ││
│  │  • Auth/ — JWT validation, OBO token exchange                          ││
│  │  • Graph/ — ContainerOperations, DriveItemOperations, UploadManager    ││
│  │  • Dataverse/ — Web API client for metadata queries                    ││
│  │  • Resilience/ — Polly retry policies                                  ││
│  └─────────────────────────────────────────────────────────────────────────┘│
│  ┌─────────────────────────────────────────────────────────────────────────┐│
│  │  Services Layer                                                         ││
│  │  • Email/EmailToEmlConverter — RFC 5322 .eml generation with MimeKit   ││
│  │  • Email/IEmailToEmlConverter — Email conversion interface             ││
│  │  • Email/IEmailFilterService — Rule-based email filtering              ││
│  │  • Jobs/EmailToDocumentJobHandler — Async email processing             ││
│  │  • Jobs/EmailPollingBackupService — Backup polling for missed webhooks ││
│  │  • Ai/AnalysisOrchestrationService — Document analysis orchestration   ││
│  │  • Ai/AnalysisContextBuilder — Prompt construction for AI analysis     ││
│  │  • Ai/RagService — Hybrid vector search for knowledge retrieval        ││
│  │  • Ai/VisualizationService — Document relationship visualization (NEW) ││
│  │  • Ai/Export/ — DOCX, PDF, Email, Teams export services (R3)           ││
│  │  • Ai/OpenAiClient — Azure OpenAI with Polly resilience (R3)           ││
│  └─────────────────────────────────────────────────────────────────────────┘│
│  ┌─────────────────────────────────────────────────────────────────────────┐│
│  │  Infrastructure/Resilience (R3 Phase 4)                                 ││
│  │  • ResilientSearchClient — Polly circuit breaker for AI Search         ││
│  │  • CircuitBreakerRegistry — Named circuit breaker management           ││
│  └─────────────────────────────────────────────────────────────────────────┘│
│  ┌─────────────────────────────────────────────────────────────────────────┐│
│  │  Telemetry (R3 Phase 4)                                                 ││
│  │  • AiTelemetry.cs — Application Insights custom metrics for AI ops     ││
│  └─────────────────────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────────────────────┘
           │                                              │
           │  OBO Token                                   │  App Token
           │  (delegated)                                 │  (application)
           ▼                                              ▼
┌──────────────────────────────┐           ┌──────────────────────────────────┐
│  Microsoft Graph API         │           │  Dataverse Web API               │
│  ────────────────────────────│           │  ────────────────────────────────│
│  • FileStorageContainers     │           │  • sprk_document CRUD            │
│  • DriveItems (files)        │           │  • sprk_matter lookup            │
│  • Permissions               │           │  • Entity metadata               │
└──────────────────────────────┘           └──────────────────────────────────┘
           │                                              │
           ▼                                              ▼
┌──────────────────────────────┐           ┌──────────────────────────────────┐
│  SharePoint Embedded         │           │  Dataverse Tables                │
│  Container (SPE)             │           │  ──────────────────────────────  │
│  ────────────────────────────│           │  • sprk_document (metadata)      │
│  • File binary storage       │           │  • sprk_matter (parent record)   │
│  • Up to 250GB per container │           │  • sprk_documenttype (lookup)    │
│  • Versioning, permissions   │           │  • Custom entities               │
└──────────────────────────────┘           └──────────────────────────────────┘
```

---

## Interaction Patterns

### Pattern 1: Document Upload Flow

```
User → PCF (UniversalQuickCreate) → BFF API → Graph API → SPE Container
                                       │
                                       └──→ Dataverse API → sprk_document record
```

**Components involved:**
1. `src/client/pcf/UniversalQuickCreate/` — UI, file selection, metadata form
2. `src/server/api/Sprk.Bff.Api/Api/DocumentsEndpoints.cs` — Upload endpoints
3. `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/UploadSessionManager.cs` — Chunked uploads
4. `src/server/api/Sprk.Bff.Api/Infrastructure/Dataverse/` — Metadata record creation

**Change Impact:**
| Change | Impact |
|--------|--------|
| Modify upload endpoint signature | Update PCF API client |
| Add upload validation | Add corresponding PCF-side validation |
| Change file size limits | Update both BFF config and PCF UI messaging |

### Pattern 2: Authentication Flow (OBO)

```
Dataverse Session → PCF (MSAL.js) → BFF API (validate) → OBO Exchange → Graph/Dataverse
```

**Components involved:**
1. `src/client/pcf/*/services/auth/msalConfig.ts` — MSAL configuration
2. `src/client/pcf/*/services/auth/MsalAuthProvider.ts` — Token acquisition
3. `src/server/api/Sprk.Bff.Api/Infrastructure/Auth/` — JWT validation
4. `src/server/api/Sprk.Bff.Api/Program.cs` — Auth middleware registration

**Change Impact:**
| Change | Impact |
|--------|--------|
| Modify API scopes | Update PCF msalConfig AND Entra app registration |
| Change token validation | All PCF controls affected |
| Add new authorization policy | Update endpoint decorators |

### Pattern 3: File Preview Flow

```
User → PCF (SpeFileViewer) → BFF API → Graph API → Preview URL
                                │
                                └──→ Redis Cache (URL caching)
```

**Components involved:**
1. `src/client/pcf/SpeFileViewer/` — Preview UI component
2. `src/server/api/Sprk.Bff.Api/Api/DocumentsEndpoints.cs` — Preview endpoint
3. `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/DriveItemOperations.cs` — Graph calls

**Change Impact:**
| Change | Impact |
|--------|--------|
| Modify preview URL structure | Update SpeFileViewer iframe handling |
| Change caching TTL | Consider Graph API rate limits |
| Add new preview types | Update both BFF and PCF |

### Pattern 4: Analysis Flow (AI Document Intelligence)

```
User → PCF (AnalysisWorkspace v1.2.7) → BFF API (AnalysisEndpoints) → Azure OpenAI
                │                                │
                │  Resume Dialog                 ├──→ SpeFileStore → SPE Container (document text)
                │  (ADR-023)                     ├──→ ScopeResolver → Dataverse (playbooks, skills)
                │                                ├──→ AnalysisContextBuilder → Prompt construction
                ▼                                └──→ Dataverse → sprk_analysis (chat history)
        Chat History
        Persistence
```

**Components involved:**
1. `src/client/pcf/AnalysisWorkspace/` — Analysis UI (v1.2.7), SSE streaming, chat interface
   - `AnalysisWorkspaceApp.tsx` — Main app with resume dialog (ADR-023), chat history ref
   - `SourceDocumentViewer.tsx` — Document preview with URL normalization
   - `useSseStream.ts` — SSE streaming hook for chat
2. `src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs` — Execute, Continue, Export endpoints
3. `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs` — Orchestration
4. `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisContextBuilder.cs` — Prompt construction
5. `src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs` — Playbook/skill resolution

**R3 Additions (v1.2.7):**
- Resume/Fresh session choice dialog (ADR-023 pattern)
- Chat history persistence to `sprk_analysis.sprk_chathistory` (JSON)
- Working document auto-save to `sprk_analysis.sprk_workingdocument`
- `chatMessagesRef` pattern to avoid stale closures in auto-save

**Change Impact:**
| Change | Impact |
|--------|--------|
| Modify analysis endpoint signature | Update PCF AnalysisWorkspace API client |
| Change prompt structure | Update AnalysisContextBuilder methods |
| Add new export format | Update ExportServiceRegistry and PCF export UI |
| Modify playbook resolution | Update ScopeResolverService and Dataverse entities |
| Change chat history schema | Update PCF `IChatMessage` interface |

### Pattern 5: Email-to-Document Conversion Flow

```
Dataverse Email → Webhook → BFF API → Filter Rules → Job Queue → SPE + Dataverse
                    │
                    └─→ (Backup) Polling Service → Job Queue
```

**Components involved:**
1. `Dataverse Service Endpoint` — Webhook registration for email.Create
2. `src/server/api/Sprk.Bff.Api/Api/EmailEndpoints.cs` — Webhook receiver, manual save
3. `src/server/api/Sprk.Bff.Api/Services/Email/` — Converter, filter service, seed service
4. `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/EmailToDocumentJobHandler.cs` — Async processing
5. `src/server/api/Sprk.Bff.Api/Services/Jobs/EmailPollingBackupService.cs` — Backup polling

**Change Impact:**
| Change | Impact |
|--------|--------|
| Modify webhook endpoint URL | Update Dataverse Service Endpoint registration |
| Change job payload schema | Update both webhook handler and job handler |
| Modify filter rule schema | Update EmailFilterService, seed service, Dataverse entity |
| Change default container | Update EmailProcessingOptions configuration |
| Modify EmailEndpoints signature | Update any UI callers (ribbon button, PCF) |
| Add email fields to sprk_document | Update DataverseWebApiService mappings |
| Change attachment filtering rules | Update EmailProcessingOptions config |

**Key Files:**
- `EmailToEmlConverter.cs` — Uses MimeKit for RFC 5322 compliant .eml generation
- `EmailEndpoints.cs` — POST /api/v1/emails/{emailId}/save-as-document, webhook receiver
- `EmailProcessingOptions.cs` — Attachment size limits, blocked extensions, signature patterns

### Pattern 6: Analysis Export Flow (R3 Phase 3)

```
User → PCF (AnalysisWorkspace) → BFF API (POST /api/ai/analysis/{id}/export)
                                              │
                                              ├──→ ExportServiceRegistry → Select export service
                                              │
                                              ├──→ DocxExportService → Open XML SDK → .docx file
                                              ├──→ PdfExportService → QuestPDF → .pdf file
                                              ├──→ EmailExportService → Microsoft Graph → Email sent
                                              └──→ TeamsExportService → Graph API → Adaptive Card
```

**Components involved:**
1. `src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs` — Export endpoint
2. `src/server/api/Sprk.Bff.Api/Services/Ai/Export/IExportService.cs` — Export interface
3. `src/server/api/Sprk.Bff.Api/Services/Ai/Export/DocxExportService.cs` — Word export
4. `src/server/api/Sprk.Bff.Api/Services/Ai/Export/PdfExportService.cs` — PDF export
5. `src/server/api/Sprk.Bff.Api/Services/Ai/Export/EmailExportService.cs` — Email via Graph
6. `src/server/api/Sprk.Bff.Api/Services/Ai/Export/TeamsExportService.cs` — Teams adaptive cards

**Export Formats:**

| Format | Library | Output | Config Option |
|--------|---------|--------|---------------|
| DOCX | Open XML SDK | File download | `AnalysisOptions.EnableDocxExport` |
| PDF | QuestPDF | File download | `AnalysisOptions.EnablePdfExport` |
| Email | Microsoft Graph | Send from user mailbox | `AnalysisOptions.EnableEmailExport` |
| Teams | Microsoft Graph | Post adaptive card | `AnalysisOptions.EnableTeamsExport` |

**Change Impact:**
| Change | Impact |
|--------|--------|
| Add new export format | Create new `IExportService`, register in DI |
| Modify export branding | Update `AnalysisOptions.ExportBranding` |
| Change email template | Update `EmailExportService.BuildHtmlEmailBody()` |
| Change PDF layout | Update `PdfExportService` QuestPDF document definition |

### Pattern 7: AI Authorization Flow (OBO for Dataverse)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ 1. User clicks "AI Summary" in PCF AnalysisWorkspace                        │
│    PCF acquires user's bearer token via MSAL.js                             │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼ Authorization: Bearer {userToken}
┌─────────────────────────────────────────────────────────────────────────────┐
│ 2. BFF API Endpoint Filter (AnalysisAuthorizationFilter)                    │
│    src/server/api/Sprk.Bff.Api/Api/Filters/AnalysisAuthorizationFilter.cs  │
│    • Extracts documentIds from request body (AnalysisExecuteRequest)        │
│    • Extracts user claims (oid, objectidentifier)                           │
│    • Calls IAiAuthorizationService.AuthorizeAsync()                         │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│ 3. AiAuthorizationService                                                   │
│    src/server/api/Sprk.Bff.Api/Services/Ai/AiAuthorizationService.cs       │
│    • Extracts bearer token from HttpContext via TokenHelper                 │
│    • Iterates through documentIds to authorize                              │
│    • Calls IAccessDataSource.GetUserAccessAsync(userAccessToken)            │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼ userAccessToken (from PCF)
┌─────────────────────────────────────────────────────────────────────────────┐
│ 4. DataverseAccessDataSource                                                │
│    src/server/shared/Spaarke.Dataverse/DataverseAccessDataSource.cs        │
│                                                                              │
│    STEP 4A: MSAL OBO Token Exchange                                         │
│    • ConfidentialClientApplicationBuilder (MSAL)                            │
│    • AcquireTokenOnBehalfOf(userToken)                                      │
│    • Scope: "https://org.crm.dynamics.com/.default"                         │
│    • Result: Dataverse-scoped token WITH user permissions                   │
│                                                                              │
│    STEP 4B: Set Authorization Header (CRITICAL FIX #1)                      │
│    • _httpClient.DefaultRequestHeaders.Authorization =                      │
│        new AuthenticationHeaderValue("Bearer", dataverseToken)              │
│    • This was MISSING in original implementation → caused 401 errors        │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼ Authorization: Bearer {dataverseToken}
┌─────────────────────────────────────────────────────────────────────────────┐
│ 5. Lookup Dataverse User                                                    │
│    DataverseAccessDataSource.LookupDataverseUserIdAsync()                   │
│    • GET /api/data/v9.2/systemusers?                                        │
│      $filter=azureactivedirectoryobjectid eq {userOid}                      │
│    • Uses OBO token (set in Step 4B)                                        │
│    • Maps Azure AD user → Dataverse systemuserid                            │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│ 6. Direct Query Authorization (CRITICAL FIX #2)                             │
│    DataverseAccessDataSource.QueryUserPermissionsAsync()                    │
│                                                                              │
│    ORIGINAL APPROACH (FAILED):                                              │
│    • POST /api/data/v9.2/RetrievePrincipalAccess                            │
│    • Returned 404 "Resource not found" with OBO tokens                      │
│    • RetrievePrincipalAccess doesn't support delegated auth                 │
│                                                                              │
│    NEW APPROACH (WORKING):                                                  │
│    • GET /api/data/v9.2/sprk_documents({documentId})?$select=sprk_documentid│
│    • Uses OBO token (user context)                                          │
│    • If 200 OK → User has Read access (Dataverse enforces this)            │
│    • If 403/404 → User doesn't have access                                  │
│    • Return PermissionRecord with AccessRights.Read                         │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│ 7. Authorization Decision                                                   │
│    AiAuthorizationService consolidates results                              │
│    • ALL documents authorized → AuthorizationResult.Allowed()               │
│    • ANY document denied → AuthorizationResult.Denied(reason)               │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│ 8. Filter Response                                                          │
│    AnalysisAuthorizationFilter.InvokeAsync()                                │
│    • If authorized → await next(context) (continue to endpoint)             │
│    • If denied → Results.Problem(403, "Forbidden", reason)                  │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│ 9. AI Analysis Proceeds                                                     │
│    AnalysisEndpoints.ExecuteAnalysisAsync()                                 │
│    • AnalysisOrchestrationService retrieves document content                │
│    • Builds prompt with AnalysisContextBuilder                              │
│    • Calls Azure OpenAI for analysis                                        │
│    • Streams response back to PCF via SSE                                   │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Components involved:**

| Component | Responsibility |
|-----------|---------------|
| `AnalysisAuthorizationFilter` | Endpoint filter - extracts documentIds, triggers authorization |
| `AiAuthorizationService` | Orchestration - extracts bearer token, calls access data source |
| `TokenHelper` | Utility - extracts bearer token from HttpContext |
| `DataverseAccessDataSource` | OBO implementation - MSAL token exchange, Dataverse queries |
| `IAccessDataSource` | Abstraction - pluggable authorization backends (Dataverse, SPE, Azure AD) |

**Key OBO Token Characteristics:**

| Aspect | User's PCF Token (Input) | Dataverse OBO Token (Output) |
|--------|--------------------------|------------------------------|
| **Audience (aud)** | `api://{bff-app-id}` | `https://{org}.crm.dynamics.com` |
| **Scopes** | `user_impersonation` | `.default` (all granted permissions) |
| **Permissions** | Grants access to BFF API | User's Dataverse permissions |
| **Identity (oid)** | User's Azure AD object ID | Same user (preserved) |
| **Usage** | Sent from PCF to BFF | Sent from BFF to Dataverse |

**MSAL OBO Configuration:**

```csharp
// src/server/shared/Spaarke.Dataverse/DataverseAccessDataSource.cs
var cca = ConfidentialClientApplicationBuilder
    .Create(clientId)                              // BFF App Registration ID
    .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
    .WithClientSecret(clientSecret)                // API_CLIENT_SECRET
    .Build();

var result = await cca.AcquireTokenOnBehalfOf(
    scopes: new[] { $"{dataverseUri}/.default" },  // Dataverse audience
    userAssertion: new UserAssertion(userToken)    // PCF user token
).ExecuteAsync(ct);

var dataverseToken = result.AccessToken;           // OBO token for Dataverse
```

**Critical Fixes Implemented:**

| Issue | Symptom | Root Cause | Fix |
|-------|---------|------------|-----|
| **Bug #1** | 401 Unauthorized from Dataverse API | OBO token obtained but never set on HttpClient headers | Set `_httpClient.DefaultRequestHeaders.Authorization` immediately after MSAL exchange |
| **Bug #2** | 404 "Resource not found for RetrievePrincipalAccess" | RetrievePrincipalAccess API doesn't support delegated (OBO) tokens | Changed to direct GET query: if document query succeeds, user has Read access |

**Azure AD Configuration Requirements:**

```
BFF App Registration (1e40baad-e065-4aea-a8d4-4b7ab273458c):
  API Permissions:
    • Dynamics CRM.user_impersonation (Delegated)  ✅ Required for OBO
    • Dynamics CRM.user (Delegated)                ✅ Required for OBO

  Certificates & Secrets:
    • Client Secret: API_CLIENT_SECRET
      Created: 2025-12-18
      Expires: 2027-12-18
      First chars: l8b8Q~J
      Used by: GraphClientFactory, DataverseAccessDataSource, PlaybookService
```

**Change Impact:**
| Change | Impact |
|--------|--------|
| Modify IAiAuthorizationService signature | Update all authorization filters (Analysis, Ai) |
| Change OBO token acquisition | Update MSAL configuration, test all AI operations |
| Modify direct query authorization logic | Update DataverseAccessDataSource, verify access checks |
| Change API_CLIENT_SECRET | Update Key Vault, App Settings, test OBO flow |
| Add new authorization data source | Implement IAccessDataSource, register in DI |

**Debugging with Application Insights:**

```kql
// Trace complete OBO authorization flow
traces
| where timestamp > ago(1h)
| where message contains "UAC-DIAG" or message contains "AI-AUTH" or message contains "OBO-DIAG"
| project timestamp, message, severityLevel
| order by timestamp desc

// Find OBO token exchange errors
exceptions
| where timestamp > ago(1h)
| where outerMessage contains "AcquireTokenOnBehalfOf" or outerMessage contains "RetrievePrincipalAccess"
| project timestamp, outerMessage, problemId, severityLevel
```

**Security Notes:**
- **Fail-closed**: Authorization errors return `AccessRights.None` (deny access)
- **No token caching**: Each request performs fresh OBO exchange (future: add Redis caching with 55-min TTL)
- **No token leakage**: User tokens never logged, only hashed identifiers in diagnostics
- **Audit trail**: All authorization decisions logged with correlation IDs

### Pattern 8: Document Relationship Visualization Flow (2026-01-12)

```
User → PCF (DocumentRelationshipViewer) → BFF API (VisualizationEndpoints) → Azure AI Search
                │                                      │
                │  React Flow                          ├──→ VisualizationService → Vector similarity search
                │  d3-force layout                     │      (documentVector3072 - 3072 dim)
                │                                      │
                ▼                                      └──→ VisualizationAuthorizationFilter → Dataverse
        Interactive Graph
        (nodes = documents,
         edges = similarity)
```

**Components involved:**
1. `src/client/pcf/DocumentRelationshipViewer/` — React Flow canvas with d3-force layout
   - `DocumentRelationshipViewer.tsx` — Main control with Fluent UI v9
   - `components/DocumentGraph.tsx` — React Flow graph with force-directed layout
   - `components/DocumentNode.tsx` — Custom node with file type icons, similarity badges
   - `components/DocumentEdge.tsx` — Similarity-based edge styling
   - `components/ControlPanel.tsx` — Threshold, depth, node limit filters
   - `components/NodeActionBar.tsx` — Open Document Record, View in SPE actions
   - `services/VisualizationApiService.ts` — API client with type mapping
   - `hooks/useVisualizationApi.ts` — Data fetching hook
2. `src/server/api/Sprk.Bff.Api/Api/Ai/VisualizationEndpoints.cs` — GET /api/ai/visualization/related/{documentId}
3. `src/server/api/Sprk.Bff.Api/Services/Ai/VisualizationService.cs` — Document similarity via vector search
4. `src/server/api/Sprk.Bff.Api/Api/Filters/VisualizationAuthorizationFilter.cs` — Resource-based auth

**Key Features (AI Search & Visualization Module):**
- **3072-dim document vectors**: `documentVector3072` field for whole-document similarity
- **Orphan file support**: Files without Dataverse records (`documentId` nullable, `speFileId` required)
- **Force-directed layout**: Edge distance = `200 * (1 - similarity)` for natural clustering
- **Real-time filtering**: Similarity threshold, depth limit, max nodes per level
- **Dark mode**: Full Fluent UI v9 token support (ADR-021)

**Change Impact:**
| Change | Impact |
|--------|--------|
| Modify VisualizationService vector search | Update test mocks, verify similarity calculations |
| Change graph data model | Update PCF TypeScript types, VisualizationApiService |
| Add new filter options | Update ControlPanel UI, endpoint query parameters |
| Modify node/edge styling | Update DocumentNode/DocumentEdge components |
| Change authorization rules | Update VisualizationAuthorizationFilter |

**Key Files:**
- `VisualizationEndpoints.cs` — GET /api/ai/visualization/related/{documentId}?tenantId={tenantId}
- `VisualizationService.cs` — Uses `IRagService` for vector search with `documentVector3072`
- `IVisualizationService.cs` — Interface with `DocumentNodeData`, `DocumentEdgeData` models
- PCF bundle: 6.65 MB (React 16, Fluent UI v9 externalized via platform-library)

---

## Shared Dependencies

### Shared Configuration

| Config Key | Used By | Location |
|------------|---------|----------|
| `BffApiBaseUrl` | All PCF controls | PCF environment config |
| `ContainerTypeId` | Upload, listing endpoints | appsettings.json |
| `Redis:InstanceName` | BFF caching | appsettings.json |
| `AzureAd:*` | BFF auth, PCF MSAL | appsettings.json, msalConfig.ts |
| `EmailProcessing:*` | Webhook, polling, job handler | appsettings.json |

### Shared Types/Contracts

| Contract | Producer | Consumers |
|----------|----------|-----------|
| `ContainerDto` | BFF API | PCF controls (TypeScript interface) |
| `FileHandleDto` | BFF API | SpeFileViewer, UniversalQuickCreate |
| `UploadSessionDto` | BFF API | UniversalQuickCreate |
| Policy names (`graph-write`, etc.) | BFF Program.cs | BFF endpoints |

**Rule:** When modifying a DTO in the BFF, update the corresponding TypeScript interface in PCF controls.

---

## Cross-Cutting Concerns

### Error Handling Chain

```
Graph ODataError → BFF ProblemDetailsHelper → HTTP Response → PCF Error Handler
```

| Layer | Error Handling |
|-------|----------------|
| BFF Infrastructure | Catch `ODataError`, map to `ProblemDetails` |
| BFF Endpoints | Return `IResult` with proper status codes |
| PCF Controls | Parse `ProblemDetails`, show user-friendly message |

**Change Impact:** Modifying `ProblemDetailsHelper` affects all PCF error displays.

### Telemetry/Logging

```
PCF (console) → BFF (Serilog + App Insights) → Azure Monitor
```

| Layer | Telemetry |
|-------|-----------|
| PCF Controls | Browser console, optional App Insights |
| BFF API | Serilog structured logging, OpenTelemetry metrics |
| Infrastructure | Azure Monitor, Log Analytics |

**Change Impact:** Adding new telemetry dimensions requires BFF and Azure configuration.

---

## Dependency Direction Rules

### Allowed Dependencies

```
PCF Controls ──────────→ BFF API (HTTP)
BFF API ───────────────→ Graph API (SDK)
BFF API ───────────────→ Dataverse API (HTTP)
BFF API ───────────────→ Azure Services (SDK)
Tests ─────────────────→ Any src/ code
```

### Prohibited Dependencies

```
BFF API ──────────✗────→ PCF Controls (no reverse dependency)
Graph Infrastructure ─✗─→ Dataverse Infrastructure (isolated)
src/ ─────────────✗────→ tests/ (no test code in production)
```

---

## Change Checklist by Component

### BFF API Endpoint Changes

- [ ] Update endpoint in `Api/*.cs`
- [ ] Update tests in `tests/unit/Sprk.Bff.Api.Tests/`
- [ ] Update PCF API client if contract changed
- [ ] Update API documentation/comments
- [ ] Verify rate limiting policies still apply

### PCF Control Changes

- [ ] Update control source in `src/client/pcf/{Control}/`
- [ ] Run `npm run build` in pcf directory
- [ ] Test in Dataverse environment
- [ ] Update control manifest if properties changed
- [ ] Increment version in `ControlManifest.Input.xml`

### Infrastructure (Bicep) Changes

- [ ] Update module in `infrastructure/bicep/modules/`
- [ ] Update stack references in `infrastructure/bicep/stacks/`
- [ ] Update parameter files if new parameters added
- [ ] Test with `az deployment group what-if`
- [ ] Update `AZURE-RESOURCE-NAMING-CONVENTION.md` if naming affected

### Dataverse Schema Changes

- [ ] Update solution in `src/dataverse/solutions/`
- [ ] Update BFF Dataverse queries if fields changed
- [ ] Update PCF form bindings if bound fields changed
- [ ] Update any related documentation

---

## Quick Lookup: Component Locations

| Component | Primary Location | Test Location |
|-----------|------------------|---------------|
| BFF Endpoints | `src/server/api/Sprk.Bff.Api/Api/` | `tests/unit/Sprk.Bff.Api.Tests/` |
| BFF Graph Operations | `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/` | Same test project |
| BFF Auth | `src/server/api/Sprk.Bff.Api/Infrastructure/Auth/` | Same test project |
| **BFF Email Services** | `src/server/api/Sprk.Bff.Api/Services/Email/` | `tests/unit/Sprk.Bff.Api.Tests/Services/Email/` |
| **Email Models** | `src/server/api/Sprk.Bff.Api/Models/Email/` | — |
| **BFF AI Services** | `src/server/api/Sprk.Bff.Api/Services/Ai/` | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/` |
| **AI Export Services** | `src/server/api/Sprk.Bff.Api/Services/Ai/Export/` | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/` |
| **AI Endpoints** | `src/server/api/Sprk.Bff.Api/Api/Ai/` | Same test project |
| **AI Models** | `src/server/api/Sprk.Bff.Api/Models/Ai/` | — |
| **AI Resilience** | `src/server/api/Sprk.Bff.Api/Infrastructure/Resilience/` | Same test project |
| **AI Telemetry** | `src/server/api/Sprk.Bff.Api/Telemetry/` | — |
| **AI Filters** | `src/server/api/Sprk.Bff.Api/Api/Filters/` | Same test project |
| PCF UniversalQuickCreate | `src/client/pcf/UniversalQuickCreate/` | Manual testing |
| PCF SpeFileViewer | `src/client/pcf/SpeFileViewer/` | Manual testing |
| PCF AnalysisWorkspace | `src/client/pcf/AnalysisWorkspace/` (v1.2.7) | Manual testing |
| **PCF DocumentRelationshipViewer** | `src/client/pcf/DocumentRelationshipViewer/` (v1.0.18) | `__tests__/` (40 component tests) |
| **Visualization Endpoints** | `src/server/api/Sprk.Bff.Api/Api/Ai/VisualizationEndpoints.cs` | Same test project |
| **Visualization Service** | `src/server/api/Sprk.Bff.Api/Services/Ai/VisualizationService.cs` | Same test project (27 tests) |
| PCF Shared Auth | `src/client/pcf/*/services/auth/` | — |
| Bicep Modules | `infrastructure/bicep/modules/` | `what-if` validation |
| Bicep AI Modules | `infrastructure/bicep/modules/dashboard.bicep`, `alerts.bicep` | `what-if` validation |
| Dataverse Plugins | `src/dataverse/plugins/` | `tests/unit/` |
| Load Test Scripts | `scripts/load-tests/` | — |

---

## See Also

- `/docs/ai-knowledge/architecture/sdap-overview.md` — System overview
- `/docs/ai-knowledge/architecture/sdap-bff-api-patterns.md` — BFF patterns
- `/docs/ai-knowledge/architecture/sdap-pcf-patterns.md` — PCF patterns
- `/docs/reference/architecture/SPAARKE-REPOSITORY-ARCHITECTURE.md` — Full repo structure
