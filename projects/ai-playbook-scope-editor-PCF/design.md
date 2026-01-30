# AI Playbook Scope Editor PCF Control - Design Document

> **Project**: Unified PCF Control for Scope Configuration Editing
> **Created**: 2026-01-29
> **Status**: Design Phase

---

## Executive Summary

Create a unified PCF control that provides rich editing and validation for all AI playbook scope configurations (Tools, Skills, Knowledge, Actions). The control adapts its editor interface based on the entity type, provides real-time validation against backend capabilities, and enables configuration-driven AI feature development without code deployment.

**Key Goals:**
1. Validate handler class names against registered backend handlers
2. Provide adaptive editors for different scope types (JSON, Markdown, POML fragments)
3. Enable discovery of available handlers and configuration options
4. Support both simple text fallback and rich editing experiences
5. Reusable across all scope entity forms (sprk_analysistool, sprk_promptfragment, sprk_systemprompt, sprk_content)

---

## Business Problem

**Current State:**
- Scope configuration fields (HandlerClass, Configuration, PromptFragment, SystemPrompt, Content) are plain text fields
- No validation that handler class names exist in backend
- No discovery mechanism for available handlers
- Easy to misconfigure scopes leading to runtime failures
- User expectation: "I added a new Analysis Tool and assigned a Tool Type, it should work" → **Currently breaks if handler doesn't exist**

**Consequences:**
- Dead-letter queue errors with cryptic messages
- Manual trial-and-error to find correct handler names
- Lack of discoverability for configuration options
- Poor user experience for Dataverse admins

**Desired State:**
- Real-time validation of handler class names
- Dropdown/autocomplete for available handlers
- Contextual help showing handler capabilities and configuration schema
- Rich editing for complex configurations (JSON with schema validation, Markdown for prompts)
- Consistent UX across all scope entity forms

---

## Solution Overview

### Unified PCF Control Architecture

**Control Name:** `ScopeConfigEditorPCF`

**Adaptive Behavior:**
The control detects which entity type it's bound to and adapts its interface accordingly:

| Entity | Primary Field | Editor Type | Validation | Discovery |
|--------|--------------|-------------|------------|-----------|
| `sprk_analysistool` | `sprk_handlerclass` | Dropdown + JSON editor | Handler registry API | GET /api/ai/handlers |
| `sprk_promptfragment` | `sprk_promptfragment` | Markdown editor | N/A | N/A |
| `sprk_systemprompt` | `sprk_systemprompt` | Markdown editor | N/A | N/A |
| `sprk_content` | `sprk_content` | Adaptive (Inline: Markdown, RAG: JSON) | Knowledge type-specific | N/A |

### Component Architecture

```
┌─────────────────────────────────────────────────────────────┐
│  ScopeConfigEditorPCF (Main Component)                      │
├─────────────────────────────────────────────────────────────┤
│  ┌─────────────────────────────────────────────────────┐    │
│  │  EntityTypeDetector                                 │    │
│  │  - Reads PCF context.parameters.entity              │    │
│  │  - Determines adaptive behavior                     │    │
│  └─────────────────────────────────────────────────────┘    │
│                          ↓                                   │
│  ┌─────────────────────────────────────────────────────┐    │
│  │  EditorSelector (Switch Component)                  │    │
│  │  - sprk_analysistool → ToolConfigEditor             │    │
│  │  - sprk_promptfragment → MarkdownEditor             │    │
│  │  - sprk_systemprompt → MarkdownEditor               │    │
│  │  - sprk_content → KnowledgeContentEditor            │    │
│  └─────────────────────────────────────────────────────┘    │
│                          ↓                                   │
│  ┌─────────────────────────────────────────────────────┐    │
│  │  Editor Components                                  │    │
│  │  ┌─────────────────────────────────────────────┐    │    │
│  │  │  ToolConfigEditor                            │    │    │
│  │  │  - HandlerSelector (Dropdown)                │    │    │
│  │  │  - HandlerMetadataDisplay (Info panel)       │    │    │
│  │  │  - JSONConfigEditor (Monaco or CodeMirror)   │    │    │
│  │  │  - ValidationDisplay (Real-time feedback)    │    │    │
│  │  └─────────────────────────────────────────────┘    │    │
│  │  ┌─────────────────────────────────────────────┐    │    │
│  │  │  MarkdownEditor                              │    │    │
│  │  │  - Rich text editor with Markdown support    │    │    │
│  │  │  - Preview pane                              │    │    │
│  │  │  - Placeholder variables helper              │    │    │
│  │  └─────────────────────────────────────────────┘    │    │
│  │  ┌─────────────────────────────────────────────┐    │    │
│  │  │  KnowledgeContentEditor                      │    │    │
│  │  │  - Type selector (Inline vs RAG)             │    │    │
│  │  │  - Markdown (Inline) or JSON (RAG config)    │    │    │
│  │  └─────────────────────────────────────────────┘    │    │
│  └─────────────────────────────────────────────────────┘    │
│                          ↓                                   │
│  ┌─────────────────────────────────────────────────────┐    │
│  │  ValidationService                                  │    │
│  │  - API client for GET /api/ai/handlers              │    │
│  │  - JSON schema validation                           │    │
│  │  - Real-time feedback                               │    │
│  └─────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────┘
```

---

## Feature Matrix

### Phase 1: Minimal (Manual with Backend Validation)

| Feature | sprk_analysistool | sprk_promptfragment | sprk_systemprompt | sprk_content |
|---------|-------------------|---------------------|-------------------|--------------|
| Plain text field | ✅ | ✅ | ✅ | ✅ |
| Backend validation on submit | ✅ (handler exists) | ❌ | ❌ | ❌ |
| Error message lists available handlers | ✅ | N/A | N/A | N/A |
| Fallback to GenericAnalysisHandler | ✅ | N/A | N/A | N/A |

**Phase 1 is COMPLETE** - see Part 1 implementation above.

### Phase 2: PCF with Validation

| Feature | sprk_analysistool | sprk_promptfragment | sprk_systemprompt | sprk_content |
|---------|-------------------|---------------------|-------------------|--------------|
| Dropdown for handler selection | ✅ | N/A | N/A | N/A |
| Handler metadata display | ✅ | N/A | N/A | N/A |
| Real-time validation | ✅ | ❌ | ❌ | ❌ |
| JSON editor with syntax highlighting | ✅ | ❌ | ❌ | ❌ |
| Configuration schema validation | ✅ | ❌ | ❌ | ❌ |

### Phase 3: Rich Editing

| Feature | sprk_analysistool | sprk_promptfragment | sprk_systemprompt | sprk_content |
|---------|-------------------|---------------------|-------------------|--------------|
| All Phase 2 features | ✅ | ✅ | ✅ | ✅ |
| Markdown editor with preview | ❌ | ✅ | ✅ | ✅ (Inline) |
| Placeholder variable helper | ❌ | ✅ | ✅ | ❌ |
| POML syntax highlighting | ❌ | ✅ (future) | ❌ | ❌ |
| Adaptive interface by KnowledgeType | N/A | N/A | N/A | ✅ |

### Phase 4: Advanced Features

| Feature | sprk_analysistool | sprk_promptfragment | sprk_systemprompt | sprk_content |
|---------|-------------------|---------------------|-------------------|--------------|
| Configuration templates | ✅ | ✅ | ✅ | ✅ |
| Version history | ✅ | ✅ | ✅ | ✅ |
| AI-assisted prompt writing | ❌ | ✅ | ✅ | ✅ (Inline) |
| RAG index browser | N/A | N/A | N/A | ✅ (RAG) |

---

## Backend API Requirements

### New API Endpoint: GET /api/ai/handlers

**Purpose:** Provide metadata about all registered tool handlers for frontend discovery and validation.

**Response Schema:**
```json
{
  "handlers": [
    {
      "handlerId": "EntityExtractorHandler",
      "name": "Entity Extractor",
      "description": "Extracts structured entities from document text using AI",
      "version": "1.0.0",
      "supportedToolTypes": ["EntityExtractor"],
      "supportedInputTypes": ["text/plain", "application/pdf"],
      "parameters": [
        {
          "name": "entityTypes",
          "description": "Types of entities to extract (e.g., Person, Organization, Date)",
          "type": "Array",
          "required": true,
          "defaultValue": null
        },
        {
          "name": "confidenceThreshold",
          "description": "Minimum confidence score for extraction (0.0-1.0)",
          "type": "Decimal",
          "required": false,
          "defaultValue": 0.7
        }
      ],
      "configurationSchema": {
        "$schema": "http://json-schema.org/draft-07/schema#",
        "type": "object",
        "properties": {
          "entityTypes": {
            "type": "array",
            "items": { "type": "string" },
            "minItems": 1
          },
          "confidenceThreshold": {
            "type": "number",
            "minimum": 0.0,
            "maximum": 1.0
          }
        },
        "required": ["entityTypes"]
      },
      "isEnabled": true
    },
    {
      "handlerId": "GenericAnalysisHandler",
      "name": "Generic Analysis Handler",
      "description": "Executes custom tools defined via configuration without code deployment",
      "version": "1.0.0",
      "supportedToolTypes": ["Custom"],
      "supportedInputTypes": ["text/plain", "application/pdf"],
      "parameters": [
        {
          "name": "operation",
          "description": "Operation type: extract, classify, validate, generate, transform, analyze",
          "type": "String",
          "required": true,
          "defaultValue": null
        },
        {
          "name": "prompt_template",
          "description": "Custom prompt with {document} and {parameters} placeholders",
          "type": "String",
          "required": false,
          "defaultValue": null
        }
      ],
      "configurationSchema": {
        "$schema": "http://json-schema.org/draft-07/schema#",
        "type": "object",
        "properties": {
          "operation": {
            "type": "string",
            "enum": ["extract", "classify", "validate", "generate", "transform", "analyze"]
          },
          "prompt_template": { "type": "string" },
          "output_schema": { "type": "object" },
          "parameters": { "type": "object" }
        },
        "required": ["operation"]
      },
      "isEnabled": true
    }
  ]
}
```

**Implementation Location:** `src/server/api/Sprk.Bff.Api/Endpoints/AiEndpoints.cs`

**Endpoint Pattern:**
```csharp
app.MapGet("/api/ai/handlers", async (IToolHandlerRegistry registry) =>
{
    var handlers = registry.GetAllHandlerInfo();
    return Results.Ok(new { handlers });
})
.WithName("GetToolHandlers")
.WithTags("AI")
.Produces<object>(200);
```

---

## PCF Control Implementation

### Technology Stack

- **Framework:** React 18 (compatible with PCF platform constraints - ADR-022)
- **UI Library:** Fluent UI v9 (ADR-021)
- **Code Editor:** Monaco Editor or CodeMirror (for JSON/Markdown)
- **Validation:** JSON Schema validation library (ajv)
- **API Client:** Fetch API with retry logic

### PCF Manifest Configuration

**Control Namespace:** `Spaarke.Controls.ScopeConfigEditor`

**Input Parameters:**
- `configField` (Multiple Lines of Text) - The primary configuration field (HandlerClass, Configuration, PromptFragment, etc.)
- `entityLogicalName` (SingleLine.Text, optional) - Entity type for adaptive behavior (auto-detected if not provided)

**Output Parameters:**
- `validatedConfig` (Multiple Lines of Text) - The validated configuration value

**Bound Entities:**
- `sprk_analysistool`
- `sprk_promptfragment`
- `sprk_systemprompt`
- `sprk_content`

### Form Configuration

**Add to Main Form for each entity:**
1. Replace default text field with PCF control
2. For `sprk_analysistool`:
   - Bind to `sprk_handlerclass` (primary) and `sprk_configuration` (secondary)
   - Control adapts to show handler selector + JSON editor
3. For `sprk_promptfragment`:
   - Bind to `sprk_promptfragment`
   - Control shows Markdown editor
4. For `sprk_systemprompt`:
   - Bind to `sprk_systemprompt`
   - Control shows Markdown editor
5. For `sprk_content`:
   - Bind to `sprk_content`
   - Control adapts based on `sprk_knowledgetypeid` (Inline vs RAG)

---

## User Experience Flows

### Flow 1: Creating a New Analysis Tool with Custom Handler

1. **User opens** sprk_analysistool form, clicks "New"
2. **User fills** basic fields (Name, Description, Tool Type)
3. **User clicks** Handler Class field → **PCF control activates**
4. **Control displays**:
   - Dropdown pre-populated with available handlers from GET /api/ai/handlers
   - "GenericAnalysisHandler" highlighted as recommended for custom tools
   - Info panel shows handler description and supported operations
5. **User selects** "EntityExtractorHandler" from dropdown
6. **Control displays**:
   - Handler metadata (description, version, supported types)
   - JSON editor for configuration field
   - Example configuration template
   - Real-time validation against handler's configuration schema
7. **User enters** configuration JSON:
   ```json
   {
     "entityTypes": ["Person", "Organization", "Date"],
     "confidenceThreshold": 0.8
   }
   ```
8. **Control validates**:
   - JSON syntax ✅
   - Schema compliance ✅
   - Shows green checkmark
9. **User saves** → Configuration stored in Dataverse
10. **Backend resolves** handler correctly → Analysis executes successfully

### Flow 2: Creating a Prompt Fragment Skill

1. **User opens** sprk_promptfragment form, clicks "New"
2. **User fills** basic fields (Name, Description, Skill Type)
3. **User clicks** Prompt Fragment field → **PCF control activates**
4. **Control displays**:
   - Markdown editor with syntax highlighting
   - Preview pane showing rendered Markdown
   - Placeholder helper (lists common variables: {document}, {parameters}, etc.)
5. **User writes** prompt fragment:
   ```markdown
   Focus on identifying legal obligations, rights, and potential liabilities.

   Key areas to analyze:
   - Termination clauses
   - Liability limitations
   - Payment terms
   ```
6. **User toggles** preview pane → sees formatted output
7. **User saves** → Prompt fragment stored in Dataverse
8. **Playbook uses** fragment successfully when skill is activated

### Flow 3: Handling Invalid Handler Name (Fallback Scenario)

**Pre-PCF (Manual Entry):**

1. **User manually types** "CustomRiskHandler" in sprk_handlerclass field
2. **User saves** → No validation at form level
3. **Playbook executes** → Handler not found
4. **Backend logs** warning:
   ```
   Custom handler 'CustomRiskHandler' not found for tool 'Risk Analysis Tool'.
   Available handlers: [EntityExtractorHandler, GenericAnalysisHandler, SummaryHandler, ...].
   Falling back to GenericAnalysisHandler.
   ```
5. **Tool executes** with GenericAnalysisHandler → Success (but not ideal)

**With PCF:**

1. **User types** "CustomRiskHandler" in dropdown
2. **Control shows** red error:
   ```
   Handler 'CustomRiskHandler' not found.
   Available handlers:
   - EntityExtractorHandler
   - GenericAnalysisHandler
   - SummaryHandler
   - ClauseAnalyzerHandler
   - DocumentClassifierHandler
   - RiskDetectorHandler
   - ...
   ```
3. **User corrects** to "RiskDetectorHandler"
4. **Control validates** ✅ → Green checkmark
5. **User saves** → Tool executes correctly

---

## Data Model

### sprk_analysistool (Analysis Tool)

| Field | Type | PCF Editor | Validation |
|-------|------|------------|------------|
| `sprk_name` | String(200) | Standard text | Required |
| `sprk_description` | Memo(2000) | Standard text | Optional |
| `sprk_tooltypeid` | Lookup → sprk_aitooltype | Standard lookup | Required |
| `sprk_handlerclass` | String(200) | **PCF Dropdown** | Handler registry check |
| `sprk_configuration` | Memo(100K) | **PCF JSON Editor** | JSON schema validation |

### sprk_promptfragment (Skill)

| Field | Type | PCF Editor | Validation |
|-------|------|------------|------------|
| `sprk_name` | String(200) | Standard text | Required |
| `sprk_description` | Memo(2000) | Standard text | Optional |
| `sprk_skilltypeid` | Lookup → sprk_aiskilltype | Standard lookup | Required |
| `sprk_promptfragment` | Memo(100K) | **PCF Markdown Editor** | None |

### sprk_systemprompt (Action)

| Field | Type | PCF Editor | Validation |
|-------|------|------------|------------|
| `sprk_name` | String(200) | Standard text | Required |
| `sprk_description` | Memo(2000) | Standard text | Optional |
| `sprk_actiontypeid` | Lookup → sprk_analysisactiontype | Standard lookup | Required |
| `sprk_systemprompt` | Memo(100K) | **PCF Markdown Editor** | None |

### sprk_content (Knowledge)

| Field | Type | PCF Editor | Validation |
|-------|------|------------|------------|
| `sprk_name` | String(200) | Standard text | Required |
| `sprk_description` | Memo(2000) | Standard text | Optional |
| `sprk_knowledgetypeid` | Lookup → sprk_aiknowledgetype | Standard lookup | Required |
| `sprk_content` | Memo(100K) | **PCF Adaptive Editor** | Type-specific |
| `sprk_deploymentid` | Lookup (optional) | Standard lookup | RAG only |

---

## Implementation Phases

### Phase 1: Backend Validation (COMPLETE)

**Duration:** Immediate (already implemented in Part 1)

**Deliverables:**
- ✅ Enhanced handler resolution in AppOnlyAnalysisService
- ✅ Enhanced handler resolution in AnalysisOrchestrationService
- ✅ Enhanced handler resolution in AiAnalysisNodeExecutor
- ✅ Fallback to GenericAnalysisHandler
- ✅ Logging of available handlers on error

**Testing:**
- Create tool with invalid handler name → Verify fallback works
- Check logs for available handler list
- Verify GenericAnalysisHandler executes successfully

### Phase 2: Handler Discovery API

**Duration:** 2-3 days

**Tasks:**
1. Create GET /api/ai/handlers endpoint in AiEndpoints.cs
2. Map IToolHandlerRegistry.GetAllHandlerInfo() to response DTO
3. Add caching (Redis, 5-minute TTL)
4. Write unit tests for endpoint
5. Document API in Swagger

**Deliverables:**
- GET /api/ai/handlers endpoint live
- Swagger documentation
- Unit tests (>80% coverage)

### Phase 3: PCF Control - Basic (Tool Handler Dropdown)

**Duration:** 5-7 days

**Tasks:**
1. Create PCF project in `src/client/pcf/ScopeConfigEditor/`
2. Implement entity type detection
3. Build handler dropdown component (Fluent UI Dropdown)
4. Integrate with GET /api/ai/handlers API
5. Add handler metadata display panel
6. Bind to sprk_analysistool form
7. Test on Dataverse form

**Deliverables:**
- Working PCF control for sprk_analysistool
- Handler dropdown with real-time API data
- Metadata display panel

### Phase 4: JSON Configuration Editor

**Duration:** 3-4 days

**Tasks:**
1. Integrate Monaco Editor or CodeMirror
2. Add JSON syntax highlighting
3. Implement schema validation (ajv library)
4. Add configuration templates
5. Real-time validation feedback
6. Bind to sprk_configuration field

**Deliverables:**
- Rich JSON editor with validation
- Configuration templates for common handlers
- Schema-based intellisense

### Phase 5: Markdown Editors (Skills, Actions, Knowledge)

**Duration:** 4-5 days

**Tasks:**
1. Extend PCF to support sprk_promptfragment
2. Extend PCF to support sprk_systemprompt
3. Extend PCF to support sprk_content (Inline type)
4. Build Markdown editor component
5. Add preview pane
6. Add placeholder variable helper
7. Deploy to all scope entity forms

**Deliverables:**
- Unified PCF control works across all scope types
- Markdown editor with preview
- Placeholder helper for variables

### Phase 6: Advanced Features

**Duration:** 5-7 days (optional)

**Tasks:**
1. Configuration templates library
2. Version history integration (Dataverse audit)
3. AI-assisted prompt writing (call GPT-4 for suggestions)
4. RAG index browser (for Knowledge RAG type)
5. Dark mode support (Fluent UI tokens)

**Deliverables:**
- Enhanced productivity features
- AI assistance for prompt authoring

---

## Technical Constraints

### ADR Compliance

| ADR | Constraint | Impact on Design |
|-----|------------|------------------|
| ADR-021 | Fluent UI v9 only | Use @fluentui/react-components for all UI |
| ADR-022 | React 16 APIs for PCF | No React 18 hooks like useId, useSyncExternalStore |
| ADR-012 | Shared component library | Extract reusable components to @spaarke/ui-components |

### PCF Platform Limitations

- **No direct Dataverse SDK:** Use Web API via fetch
- **Manifest size limits:** Keep bundle under 1MB
- **React version:** PCF runtime provides React 16 (can't upgrade)
- **Authentication:** Use parent form's authentication context
- **Network:** CORS restrictions require API to allow Dataverse origin

---

## Security Considerations

### API Security

- **GET /api/ai/handlers:**
  - Requires authentication (inherited from main API auth)
  - No sensitive data exposed (only handler metadata)
  - Rate limiting: 100 requests/minute per user

### PCF Security

- **Client-side validation only:** Backend must re-validate on save
- **No secrets in configuration:** JSON configs should not contain API keys
- **XSS prevention:** Sanitize Markdown before rendering in preview pane
- **CSP compliance:** Follow Dataverse Content Security Policy

---

## Testing Strategy

### Unit Tests

- **Backend:**
  - GET /api/ai/handlers endpoint response structure
  - Handler metadata serialization
  - Registry lookup logic

- **PCF:**
  - Entity type detection
  - Handler selection logic
  - JSON validation against schema
  - Markdown sanitization

### Integration Tests

- **API Integration:**
  - PCF → GET /api/ai/handlers (mock API response)
  - Handler dropdown populated correctly
  - Configuration validation works

- **Dataverse Integration:**
  - PCF bound to form field
  - Value updates on save
  - Form validation errors displayed

### End-to-End Tests

1. **Create tool with valid handler:**
   - Open sprk_analysistool form
   - Select handler from dropdown
   - Enter valid JSON configuration
   - Save → Verify no errors
   - Execute playbook → Verify tool runs

2. **Create tool with invalid configuration:**
   - Open sprk_analysistool form
   - Select handler
   - Enter invalid JSON
   - Verify validation error shown
   - Correct JSON → Error clears
   - Save → Verify backend accepts

3. **Create skill with Markdown prompt:**
   - Open sprk_promptfragment form
   - Enter Markdown with placeholders
   - Toggle preview → Verify rendering
   - Save → Verify stored correctly
   - Execute playbook → Verify skill applied

---

## Success Metrics

### User Experience

- **Handler discovery:** 100% of users can find available handlers without documentation
- **Configuration errors:** 90% reduction in invalid tool configurations
- **Time to configure:** 50% reduction in time to create a new tool
- **Dead-letter errors:** 80% reduction in "handler not found" errors

### Technical

- **API response time:** GET /api/ai/handlers < 200ms (p95)
- **PCF load time:** Control initializes in < 1 second
- **Validation latency:** Real-time validation feedback < 300ms
- **Cache hit rate:** 95% for handler metadata API calls

### Adoption

- **PCF deployed:** All scope entity forms use the control (4 entities)
- **Fallback usage:** < 10% of tool executions use fallback handler (indicates good UX)
- **User training:** Zero training required (self-explanatory UI)

---

## Risks and Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| PCF performance issues with Monaco Editor | High | Medium | Use CodeMirror (lighter), lazy-load editor on demand |
| API rate limiting for handler metadata | Medium | Low | Implement aggressive caching (Redis + local storage) |
| React 16 compatibility issues | High | Low | Avoid React 18 features, test thoroughly in PCF runtime |
| Schema validation library bundle size | Medium | Medium | Use tree-shaking, consider lightweight alternative (joi) |
| User confusion with adaptive UI | Medium | Low | Clear labels, consistent UX patterns across entities |
| Backend handler registration changes | Low | Medium | Version API, cache invalidation strategy |

---

## Future Enhancements

### Version 2.0

- **AI-assisted configuration:** GPT-4 generates JSON config from natural language description
- **Configuration marketplace:** Share/import tool configurations across environments
- **Visual prompt builder:** Drag-and-drop prompt composition (similar to POML builder)
- **Testing sandbox:** Test tool configurations without saving to Dataverse
- **Handler templates:** Pre-built configurations for common use cases

### Version 3.0

- **Custom handler upload:** Allow admins to upload custom handlers as plugins (security review required)
- **Multi-language support:** Localized UI for handler metadata
- **Advanced debugging:** Step-through execution with breakpoints for playbook testing
- **Performance profiling:** Show token usage and latency for each handler

---

## Appendices

### Appendix A: Handler Registry API Response Example

See Backend API Requirements section above for full schema.

### Appendix B: PCF Manifest XML

```xml
<?xml version="1.0" encoding="utf-8" ?>
<manifest>
  <control namespace="Spaarke.Controls" constructor="ScopeConfigEditor" version="1.0.0" display-name-key="ScopeConfigEditor_Display_Key" description-key="ScopeConfigEditor_Desc_Key" control-type="standard">
    <property name="configField" display-name-key="ConfigField_Display_Key" description-key="ConfigField_Desc_Key" of-type="Multiple" usage="bound" required="true" />
    <property name="entityLogicalName" display-name-key="EntityLogicalName_Display_Key" description-key="EntityLogicalName_Desc_Key" of-type="SingleLine.Text" usage="input" required="false" />
    <resources>
      <code path="index.ts" order="1"/>
      <css path="css/ScopeConfigEditor.css" order="1" />
    </resources>
    <feature-usage>
      <uses-feature name="WebAPI" required="true" />
    </feature-usage>
  </control>
</manifest>
```

### Appendix C: Example Configurations

**EntityExtractorHandler:**
```json
{
  "entityTypes": ["Person", "Organization", "Date", "MonetaryValue"],
  "confidenceThreshold": 0.7,
  "includeContext": true
}
```

**GenericAnalysisHandler (Custom Extraction):**
```json
{
  "operation": "extract",
  "prompt_template": "Extract all technical requirements from the document...",
  "output_schema": {
    "type": "object",
    "properties": {
      "requirements": {
        "type": "array",
        "items": { "type": "string" }
      }
    }
  },
  "temperature": 0.2
}
```

---

**End of Design Document**
