# JSON Field Schemas

> **Last Updated**: April 5, 2026
> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: New
> **Solution**: Spaarke (Dataverse)
> **Replaces**: `sprk_event-json-fields.md` (scope expanded to all entities)

---

## Purpose

Documents the JSON schema contracts for all Dataverse Multiline Text fields that store structured JSON data. These fields are parsed by BFF API services and/or client-side TypeScript code -- the schema must remain stable across both sides.

---

## Summary of JSON Fields by Entity

| Entity | Field | Max Length | Service That Parses |
|---|---|---|---|
| `sprk_invoice` | `sprk_extractedjson` | 20,000 | `InvoiceExtractionJobHandler` |
| `sprk_analysis` | `sprk_chathistory` | 1,048,576 | `AnalysisResultPersistence`, `WorkingDocumentService`, `SprkChat` component |
| `sprk_analysis` | `sprk_workingdocument` | 100,000 | `WorkingDocumentTools`, `AnalysisOrchestrationService` |
| `sprk_analysis` | `sprk_finaloutput` | 100,000 | `AnalysisOrchestrationService` |
| `sprk_analysisaction` | `sprk_outputschemajson` | 1,048,576 | `AnalysisOrchestrationService` |
| `sprk_analysisplaybook` | `sprk_canvaslayoutjson` | 1,048,576 | `PlaybookBuilder` (client code page) |
| `sprk_analysisplaybook` | `sprk_triggerconfigjson` | 100,000 | `AnalysisOrchestrationService` |
| `sprk_analysistool` | `sprk_configuration` | 100,000 | `AnalysisOrchestrationService` (tool dispatch) |
| `sprk_matter` / `sprk_project` | `sprk_monthlyspendtimeline` | 10,000 | `FinanceRollupService` |
| `sprk_communicationaccount` | `sprk_processingrules` | 10,000 | `DataverseWebApiService` (rule evaluation) |
| `sprk_document` | `sprk_attachments` | 10,000 | `DataverseWebApiService`, `DataverseServiceClientImpl` |
| `sprk_workassignment` | `sprk_searchprofile` | 2,000 | `DocumentProfileFieldMapper` |
| `sprk_kpiassessment` | `sprk_assessmentcriteria` | 10,000 | KPI quick-create form (plain text, not JSON per se) |
| `sprk_kpiassessment` | `sprk_assessmentnotes` | 10,000 | KPI quick-create form (plain text, not JSON per se) |
| `sprk_gridconfiguration` | `sprk_configjson` | 1,048,576 | `ConfigurationService`, `ViewService`, `useSavedSearches`, `useSearchViewDefinitions` |
| `sprk_eventtype_ref` | `sprk_fieldconfigjson` | (varies) | `useFormConfig` (EventDetailSidePane) |

---

## Schema Definitions

### 1. Invoice Extracted JSON (`sprk_invoice.sprk_extractedjson`)

**Max Length**: 20,000 chars
**Written by**: `InvoiceExtractionJobHandler` (AI extraction pipeline)
**Read by**: Invoice review UI, BFF finance endpoints

Stores the raw AI-extracted invoice data before human review. Schema:

```json
{
  "invoiceNumber": "INV-2026-001",
  "invoiceDate": "2026-02-15",
  "vendorName": "Smith & Partners LLP",
  "totalAmount": 15250.00,
  "currency": "USD",
  "lineItems": [
    {
      "sequence": 1,
      "description": "Legal research - patent filing",
      "amount": 5000.00,
      "quantity": 10.0,
      "rate": 500.00,
      "timekeeper": "J. Smith",
      "timekeeperRole": "Partner",
      "eventDate": "2026-02-01",
      "costType": "Fee"
    }
  ],
  "extractionConfidence": 0.92,
  "extractionModel": "gpt-4o",
  "extractionTimestamp": "2026-02-15T10:30:00Z"
}
```

---

### 2. Analysis Chat History (`sprk_analysis.sprk_chathistory`)

**Max Length**: 1,048,576 chars (1 MB)
**Written by**: `WorkingDocumentService.SaveChatHistoryAsync()`
**Read by**: `DataverseServiceClientImpl`, `DataverseWebApiService`, `SprkChat` UI component (via `useChatSession` hook)

Stores the full conversation history for an analysis session. Schema is an array of chat messages:

```json
[
  {
    "role": "user",
    "content": "Analyze this invoice for compliance issues",
    "timestamp": "2026-02-15T10:30:00Z"
  },
  {
    "role": "assistant",
    "content": "I've identified 3 potential compliance issues...",
    "timestamp": "2026-02-15T10:30:05Z",
    "tokenCount": 450
  },
  {
    "role": "system",
    "content": "Tool call: write_working_document completed",
    "timestamp": "2026-02-15T10:30:06Z"
  }
]
```

**Roles**: `user`, `assistant`, `system`

---

### 3. Analysis Working Document (`sprk_analysis.sprk_workingdocument`)

**Max Length**: 100,000 chars
**Written by**: `WorkingDocumentTools.WriteBackWorkingDocument()` -- targets `sprk_analysisoutput.sprk_workingdocument`
**Read by**: `DataverseServiceClientImpl`, `DataverseWebApiService`, analysis viewer UI

Stores the current working output of an AI analysis session in **Markdown format**. This is not JSON but structured Markdown that the AI generates and updates iteratively.

```markdown
# Invoice Analysis Report

## Summary
The invoice from Smith & Partners LLP dated 2026-02-15...

## Compliance Issues
1. **Rate cap exceeded** - Partner rate of $500/hr exceeds guideline cap of $450/hr
2. ...

## Recommendations
- Request rate adjustment for line items 1, 3, 5
```

Note: The `sprk_analysis.sprk_workingdocument` field is the analysis-level snapshot. The primary write-back target is `sprk_analysisoutput.sprk_workingdocument`.

---

### 4. Analysis Action Output Schema (`sprk_analysisaction.sprk_outputschemajson`)

**Max Length**: 1,048,576 chars
**Written by**: Playbook configuration (admin)
**Read by**: `AnalysisOrchestrationService` (validates AI output against schema)

Stores a JSON Schema definition that the AI output must conform to:

```json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "type": "object",
  "required": ["summary", "issues"],
  "properties": {
    "summary": { "type": "string" },
    "issues": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "severity": { "type": "string", "enum": ["high", "medium", "low"] },
          "description": { "type": "string" },
          "lineReference": { "type": "integer" }
        }
      }
    }
  }
}
```

---

### 5. Playbook Canvas Layout (`sprk_analysisplaybook.sprk_canvaslayoutjson`)

**Max Length**: 1,048,576 chars
**Written by**: `PlaybookBuilder` code page (React Flow canvas)
**Read by**: `PlaybookBuilder` code page, `AnalysisOrchestrationService` (node-based execution)

Stores the visual layout and node definitions for the playbook builder canvas:

```json
{
  "nodes": [
    {
      "id": "node-1",
      "type": "action",
      "position": { "x": 100, "y": 200 },
      "data": {
        "label": "Extract Invoice Data",
        "actionId": "abc-123",
        "configJson": "{...}"
      }
    },
    {
      "id": "node-2",
      "type": "condition",
      "position": { "x": 300, "y": 200 },
      "data": { "label": "Confidence > 0.8?" }
    }
  ],
  "edges": [
    { "source": "node-1", "target": "node-2" }
  ],
  "viewport": { "x": 0, "y": 0, "zoom": 1.0 }
}
```

---

### 6. Playbook Trigger Config (`sprk_analysisplaybook.sprk_triggerconfigjson`)

**Max Length**: 100,000 chars
**Written by**: Playbook configuration (admin)
**Read by**: `AnalysisOrchestrationService`

Defines how a playbook is triggered (for non-manual trigger types):

```json
{
  "schedule": {
    "cronExpression": "0 0 8 * * MON-FRI",
    "timezone": "America/New_York"
  },
  "filter": {
    "entityLogicalName": "sprk_document",
    "conditions": [
      { "field": "sprk_classification", "operator": "eq", "value": 100000000 }
    ]
  }
}
```

---

### 7. Analysis Tool Configuration (`sprk_analysistool.sprk_configuration`)

**Max Length**: 100,000 chars
**Written by**: Playbook configuration (admin)
**Read by**: `AnalysisOrchestrationService` (tool dispatch)

Tool-specific configuration passed to the `IAiToolHandler` implementation:

```json
{
  "searchIndex": "spaarke-documents-index",
  "topK": 10,
  "semanticConfig": "default",
  "filters": {
    "containerIds": ["container-guid-1", "container-guid-2"]
  }
}
```

---

### 8. Monthly Spend Timeline (`sprk_matter.sprk_monthlyspendtimeline` / `sprk_project.sprk_monthlyspendtimeline`)

**Max Length**: 10,000 chars
**Written by**: `FinanceRollupService.RecalculateAsync()`
**Read by**: Financial dashboard charts, `RecalculateFinanceResponse.MonthlySpendTimeline`

JSON array of monthly spend data points for trend visualization:

```json
[
  { "period": "2025-10", "amount": 12500.00 },
  { "period": "2025-11", "amount": 15000.00 },
  { "period": "2025-12", "amount": 8750.00 },
  { "period": "2026-01", "amount": 22000.00 },
  { "period": "2026-02", "amount": 18500.00 }
]
```

---

### 9. Communication Processing Rules (`sprk_communicationaccount.sprk_processingrules`)

**Max Length**: 10,000 chars
**Written by**: Admin configuration
**Read by**: `DataverseWebApiService` (rule evaluation at line 1872)

JSON array of processing rules for inbound email handling:

```json
[
  {
    "name": "Auto-classify invoices",
    "condition": {
      "field": "subject",
      "operator": "contains",
      "value": "invoice"
    },
    "action": {
      "type": "classify",
      "targetClassification": 100000000
    }
  },
  {
    "name": "Route to matter",
    "condition": {
      "field": "from",
      "operator": "endsWith",
      "value": "@lawfirm.com"
    },
    "action": {
      "type": "associate",
      "targetEntity": "sprk_matter"
    }
  }
]
```

Each rule is deserialized as `Dictionary<string, JsonElement>` in `DataverseWebApiService`.

---

### 10. Document Attachments (`sprk_document.sprk_attachments`)

**Max Length**: 10,000 chars
**Written by**: `DataverseWebApiService`, `DataverseServiceClientImpl` (during document creation)
**Read by**: Document viewer UI

JSON array of attachment metadata:

```json
[
  {
    "name": "contract-appendix-a.pdf",
    "driveItemId": "drive-item-guid",
    "size": 245000,
    "mimeType": "application/pdf"
  }
]
```

---

### 11. Work Assignment Search Profile (`sprk_workassignment.sprk_searchprofile`)

**Max Length**: 2,000 chars
**Written by**: `DocumentProfileFieldMapper` (AI document profiling)
**Read by**: `DocumentProfileFieldMapper` (field mapping at line 37, 121)

Search parameters for document discovery within a work assignment:

```json
{
  "keywords": ["patent", "filing", "USPTO"],
  "dateRange": {
    "from": "2025-01-01",
    "to": "2026-12-31"
  },
  "documentTypes": ["Contract", "Letter"],
  "containerIds": ["container-guid"]
}
```

---

### 12. Grid Configuration JSON (`sprk_gridconfiguration.sprk_configjson`)

**Max Length**: 1,048,576 chars
**Written by**: Admin configuration, `useSavedSearches` hook (for saved searches)
**Read by**: `ConfigurationService`, `ViewService`, `useSearchViewDefinitions`, `useSavedSearches`

Multi-purpose configuration field with a `_type` discriminator for polymorphic usage:

**Grid view configuration**:
```json
{
  "features": {
    "enableSelection": true,
    "enableInlineEdit": false
  },
  "columns": [
    {
      "logicalName": "sprk_name",
      "width": 200,
      "sortable": true
    }
  ]
}
```

**Saved search configuration** (discriminated by `_type`):
```json
{
  "_type": "semanticSearch",
  "query": "contract amendments 2026",
  "filters": {
    "documentType": ["Contract"],
    "dateRange": { "from": "2026-01-01" }
  },
  "resultColumns": ["sprk_documentname", "sprk_createddatetime"]
}
```

---

### 13. Event Type Field Config (`sprk_eventtype_ref.sprk_fieldconfigjson`)

**Written by**: Admin configuration (Dataverse)
**Read by**: `useFormConfig` hook in `EventDetailSidePane`

Defines which fields are visible/required for each event type's side pane form:

```json
{
  "sections": [
    {
      "name": "Status",
      "fields": [
        { "logicalName": "sprk_duedate", "visible": true, "required": true },
        { "logicalName": "sprk_finalduedate", "visible": true, "required": false },
        { "logicalName": "sprk_completeddate", "visible": true, "required": false }
      ]
    },
    {
      "name": "Priority",
      "fields": [
        { "logicalName": "sprk_priority", "visible": true, "required": true },
        { "logicalName": "sprk_effort", "visible": true, "required": false }
      ]
    }
  ]
}
```

Parsed in `EventDetailSidePane/src/types/FormConfig.ts` (line 186) and `EntityConfigurationService.ts` (line 24).

---

### 14. Workspace Layout JSON

**Written by**: `WorkspaceLayoutWizard` code page
**Read by**: `LegalWorkspace` (via `layoutCache.ts`)

Parsed at `WorkspaceLayoutWizard/src/App.tsx` line 143 as `LayoutJson`:

```json
{
  "panels": [
    {
      "id": "panel-1",
      "type": "grid",
      "entity": "sprk_event",
      "viewId": "b836398f-6900-f111-8407-7c1e520aa4df",
      "position": { "row": 0, "col": 0, "width": 6 }
    },
    {
      "id": "panel-2",
      "type": "chart",
      "chartType": "spend-trend",
      "position": { "row": 0, "col": 6, "width": 6 }
    }
  ],
  "settings": {
    "theme": "auto",
    "refreshInterval": 300
  }
}
```

Cached via `layoutCache.ts` in `sessionStorage` with `JSON.parse()`.

---

### 15. Daily Briefing Preferences

**Written by**: `DailyBriefing` code page
**Read by**: `preferencesService.ts` (line 83)

Not a Dataverse field -- stored client-side, but included for completeness:

```json
{
  "enabledSections": ["events", "invoices", "signals"],
  "timezone": "America/New_York",
  "digestFrequency": "daily"
}
```

---

## Parsing Patterns in Code

### C# / BFF API

```csharp
// Standard pattern: JsonSerializer with Dictionary<string, JsonElement>
var ruleDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(ruleJson.GetRawText());

// Field write-back pattern
entity["sprk_workingdocument"] = content;
entity["sprk_chathistory"] = chatHistoryJson;
entity["sprk_monthlyspendtimeline"] = timelineJson;
```

### TypeScript / Client

```typescript
// Standard pattern: JSON.parse with type assertion
const parsed = JSON.parse(jsonString) as LayoutJson;
const config = JSON.parse(record.sprk_configjson) as IGridConfigJson;

// Safe parsing with fallback
const state = stored ? (JSON.parse(stored) as WorkspaceLayoutDto) : null;
```

---

## Related Documentation

| Document | Path |
|---|---|
| Field Mapping Reference | `docs/data-model/field-mapping-reference.md` |
| Entity Relationship Model | `docs/data-model/entity-relationship-model.md` |
| AI Architecture | `docs/architecture/AI-ARCHITECTURE.md` |
| Playbook Architecture | `docs/architecture/playbook-architecture.md` |
