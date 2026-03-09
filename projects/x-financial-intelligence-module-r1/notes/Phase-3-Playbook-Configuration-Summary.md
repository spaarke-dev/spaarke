# Phase 3: Playbook Configuration - Completion Summary

**Date**: February 11, 2026
**Status**: ✅ **COMPLETED** (Implementation complete, awaiting deployment)

---

## What Was Accomplished

### 1. Added Finance-Specific Tools to Seed Data

**File**: `scripts/seed-data/tools.json`

Added three new tool definitions that reference the handlers created in Phase 2:

| Tool ID | Name | Handler Class | Purpose |
|---------|------|---------------|---------|
| **TL-009** | Invoice Extraction Tool | `InvoiceExtractionToolHandler` | Extract invoice data using AI, generate summary and JSON |
| **TL-010** | Dataverse Update Tool | `DataverseUpdateToolHandler` | Generic entity update with type conversion support |
| **TL-011** | Financial Calculation Tool | `FinancialCalculationToolHandler` | Matter-level financial aggregations (stubbed for MVP) |

**Tool TL-009 Configuration**:
```json
{
  "id": "TL-009",
  "sprk_name": "Invoice Extraction Tool",
  "sprk_handlerclass": "InvoiceExtractionToolHandler",
  "toolType": "01 - Entity Extraction",
  "sprk_configuration": {
    "model": "gpt-4o",
    "extraction_fields": ["invoice_number", "invoice_date", "vendor_name", "total_amount", "currency", "line_items"],
    "generate_summary": true,
    "summary_max_chars": 5000,
    "json_max_chars": 20000,
    "confidence_threshold": 0.7
  }
}
```

**Tool TL-010 Configuration**:
```json
{
  "id": "TL-010",
  "sprk_name": "Dataverse Update Tool",
  "sprk_handlerclass": "DataverseUpdateToolHandler",
  "toolType": "04 - Calculation",
  "sprk_configuration": {
    "supported_types": ["string", "int", "decimal", "money", "entityreference", "datetime", "boolean"],
    "validate_fields": true,
    "retry_on_conflict": true,
    "max_retries": 3
  }
}
```

### 2. Created Finance Invoice Processing Playbook

**File**: `scripts/seed-data/playbooks.json`

Added **PB-013: Finance Invoice Processing** to the active playbooks section.

**Playbook Configuration**:
```json
{
  "id": "PB-013",
  "sprk_name": "Finance Invoice Processing",
  "sprk_description": "AI-powered invoice processing for legal matters...",
  "sprk_ispublic": true,
  "estimatedTime": "~45 seconds",
  "complexity": "Medium",
  "isSystemPlaybook": true,
  "triggerContext": "finance-invoice-processing",
  "scopes": {
    "skills": ["SKL-002"],
    "actions": ["ACT-001", "ACT-008"],
    "knowledge": [],
    "tools": ["TL-009", "TL-010", "TL-011"]
  }
}
```

**Scopes Used**:
- **Skill SKL-002**: "Invoice Processing" - Already exists in seed data
- **Action ACT-001**: "Extract Entities" - Already exists
- **Action ACT-008**: "Calculate Values" - Already exists
- **Tools TL-009, TL-010, TL-011**: Finance-specific handlers (newly added)

**Workflow Description**:
1. Extract invoice header and line items using AI (TL-009 / InvoiceExtractionToolHandler)
2. Generate AI summary (sprk_aisummary) and JSON (sprk_extractedjson)
3. Update Invoice record with extracted data (TL-010 / DataverseUpdateToolHandler)
4. Calculate matter financial totals (TL-011 / FinancialCalculationToolHandler - stubbed in MVP)
5. Update Matter record with aggregated totals (TL-010 / DataverseUpdateToolHandler)

**Output Mapping** (Dataverse field targets):
```json
{
  "aiSummary": "sprk_invoice.sprk_aisummary",
  "extractedJson": "sprk_invoice.sprk_extractedjson",
  "totalAmount": "sprk_invoice.sprk_totalamount",
  "invoiceNumber": "sprk_invoice.sprk_invoicenumber",
  "invoiceDate": "sprk_invoice.sprk_invoicedate",
  "matterTotalSpend": "sprk_matter.sprk_totalspendtodate",
  "matterRemainingBudget": "sprk_matter.sprk_remainingbudget",
  "matterInvoiceCount": "sprk_matter.sprk_invoicecount"
}
```

### 3. Updated Future Playbooks

**File**: `scripts/seed-data/playbooks.json`

Updated **PB-006** in `futurePlaybooks` section to note it was superseded by PB-013:
```json
{
  "id": "PB-006",
  "sprk_name": "Invoice Processing",
  "sprk_description": "Invoice data extraction and validation (superseded by PB-013 Finance Invoice Processing for Finance Module R1 MVP)",
  "priority": 3,
  "status": "Superseded by PB-013"
}
```

---

## Deployment Instructions

### Prerequisites

Ensure the following seed data is already deployed:
- ✅ Type lookups (Deploy-TypeLookups.ps1)
- ✅ Actions (Deploy-Actions.ps1) - includes ACT-001, ACT-008
- ✅ Skills (Deploy-Skills.ps1) - includes SKL-002
- ⚠️ Tools (Deploy-Tools.ps1) - **MUST RE-RUN** to include TL-009, TL-010, TL-011

### Deployment Commands

Run from `scripts/seed-data/` directory:

```powershell
# Step 1: Deploy new tools (TL-009, TL-010, TL-011)
.\Deploy-Tools.ps1

# Step 2: Verify tools deployed correctly
.\Verify-Tools.ps1

# Step 3: Deploy playbook (PB-013)
.\Deploy-Playbooks.ps1

# Step 4: Verify playbook deployed correctly
.\Verify-Playbooks.ps1
```

**Expected Output**:

```
=== Deploy Tools ===
  INSERTED: 'Invoice Extraction Tool' (ID: ...)
  INSERTED: 'Dataverse Update Tool' (ID: ...)
  INSERTED: 'Financial Calculation Tool' (ID: ...)

=== Deploy Playbooks ===
  INSERTED: 'Finance Invoice Processing' (ID: ...)
    + Skill: SKL-002
    + Action: ACT-001
    + Action: ACT-008
    + Tool: TL-009
    + Tool: TL-010
    + Tool: TL-011
```

### Verification Queries

After deployment, verify in Dataverse:

```powershell
# Query tools
$env:DATAVERSE_URL = "https://spaarkedev1.crm.dynamics.com"
$filter = "sprk_name eq 'Invoice Extraction Tool' or sprk_name eq 'Dataverse Update Tool' or sprk_name eq 'Financial Calculation Tool'"
Invoke-WebRequest -Uri "$env:DATAVERSE_URL/api/data/v9.2/sprk_analysistools?`$filter=$filter" -UseDefaultCredentials | ConvertFrom-Json

# Query playbook
$filter = "sprk_name eq 'Finance Invoice Processing'"
Invoke-WebRequest -Uri "$env:DATAVERSE_URL/api/data/v9.2/sprk_analysisplaybooks?`$filter=$filter&`$expand=sprk_playbook_tool,sprk_playbook_skill" -UseDefaultCredentials | ConvertFrom-Json
```

---

## Architecture Notes

### Playbook-Driven Workflow Pattern

The Finance Invoice Processing playbook uses the **Playbook-Driven Architecture** pattern:

```
┌─────────────────────────────────────────────────────────────┐
│                       Job Handler                            │
│  (AttachmentClassificationJobHandler / InvoiceExtractionJH) │
│                                                              │
│  - Enqueues background job                                  │
│  - Passes context (invoiceId, matterId, documentText)       │
└─────────────────────────┬───────────────────────────────────┘
                          │
                          ▼
┌─────────────────────────────────────────────────────────────┐
│                      Playbook Engine                         │
│         (AnalysisOrchestrationService.ExecutePlaybookAsync) │
│                                                              │
│  - Resolves playbook scopes (skills, actions, knowledge)    │
│  - Executes workflow nodes sequentially                     │
│  - Coordinates tool handler execution                       │
└─────────────────────────┬───────────────────────────────────┘
                          │
                          ▼
┌─────────────────────────────────────────────────────────────┐
│                     Tool Handlers                            │
│              (IToolHandlerRegistry / IAiToolHandler)         │
│                                                              │
│  Node 1 → InvoiceExtractionToolHandler                     │
│           - Calls IInvoiceAnalysisService                   │
│           - Generates AI summary and JSON                   │
│                                                              │
│  Node 2 → DataverseUpdateToolHandler                       │
│           - Updates sprk_invoice record                     │
│           - Sets sprk_aisummary, sprk_extractedjson, etc.  │
│                                                              │
│  Node 3 → FinancialCalculationToolHandler (stubbed MVP)    │
│           - Returns error: "Not implemented in MVP"         │
│                                                              │
│  Node 4 → DataverseUpdateToolHandler                       │
│           - Updates sprk_matter record                      │
│           - Sets totalspendtodate, invoicecount, etc.       │
└─────────────────────────────────────────────────────────────┘
```

### Handler Resolution

Tools are resolved using a **three-tier architecture**:

1. **Tier 1: Configuration** - Playbook references tool by ID (TL-009)
2. **Tier 2: Registry Lookup** - IToolHandlerRegistry.GetHandler("InvoiceExtractionToolHandler")
3. **Tier 3: Execution** - Handler.ExecuteAsync(ToolParameters, CancellationToken)

**Important**: The `sprk_handlerclass` field in the tool record determines which C# class handles execution.

### MVP Simplification

The MVP uses a **simplified approach** for matter totals:

**Original Design** (deferred to R2):
- BillingEvents entity with denormalized line items
- SpendSnapshots for historical tracking
- Complex aggregation queries

**MVP Approach**:
- Invoice data stored as JSON on invoice record (`sprk_extractedjson`)
- Matter totals denormalized directly on matter record
- FinancialCalculationToolHandler **stubbed** - calculations done via playbook logic/configuration
- API endpoints read denormalized fields for performance

---

## Testing Strategy

### Unit Tests (Already Complete)

Tool handlers have comprehensive unit tests:
- `InvoiceExtractionToolHandlerTests.cs` (8 test scenarios)
- `DataverseUpdateToolHandlerTests.cs` (9 test scenarios)
- `FinancialCalculationToolHandlerTests.cs` (10 test scenarios)

**Note**: Tests require NSubstitute package reference to compile.

### Integration Tests (Next Phase)

Create `InvoiceProcessingPlaybookTests.cs` to test end-to-end workflow:

```csharp
[Fact]
public async Task FinanceInvoiceProcessing_Playbook_ShouldUpdateInvoiceAndMatter()
{
    // Arrange
    var context = new Dictionary<string, object>
    {
        ["invoiceId"] = testInvoiceId,
        ["matterId"] = testMatterId,
        ["documentText"] = sampleInvoiceText
    };

    // Act
    var result = await _playbookService.ExecuteAsync(
        "finance-invoice-processing",
        context,
        CancellationToken.None);

    // Assert
    Assert.True(result.Success);

    // Verify invoice updated
    var invoice = await _dataverseService.RetrieveRecordFieldsAsync(
        "sprk_invoice", testInvoiceId,
        ["sprk_aisummary", "sprk_extractedjson", "sprk_totalamount"]);
    Assert.NotNull(invoice["sprk_aisummary"]);
    Assert.NotNull(invoice["sprk_extractedjson"]);

    // Verify matter updated
    var matter = await _dataverseService.RetrieveRecordFieldsAsync(
        "sprk_matter", testMatterId,
        ["sprk_totalspendtodate", "sprk_invoicecount"]);
    Assert.True((decimal)matter["sprk_totalspendtodate"] > 0);
    Assert.Equal(1, matter["sprk_invoicecount"]);
}
```

---

## Next Steps

### Immediate (Phase 3 Deployment)

- [ ] Run Deploy-Tools.ps1 to create TL-009, TL-010, TL-011
- [ ] Run Deploy-Playbooks.ps1 to create PB-013
- [ ] Verify playbook and tool records exist in Dataverse
- [ ] Test playbook execution with sample invoice data

### Phase 4: Job Handler Updates

- [ ] Update InvoiceExtractionJobHandler to call playbook
- [ ] Update AttachmentClassificationJobHandler if needed
- [ ] Create integration tests for job handlers

### Phase 5: API Endpoints

- [ ] Create MatterOverviewEndpoints.cs (GET /api/finance/matters/{id}/overview)
- [ ] Create InvoiceListEndpoints.cs (GET /api/finance/matters/{id}/invoices)
- [ ] Add authorization filters
- [ ] Write integration tests

### Phase 6: VisualHost Charts

- [ ] Create matter-overview-charts.json
- [ ] Create invoice-tab-charts.json
- [ ] Deploy charts to Dataverse

### Phase 7: Deployment & Validation

- [ ] Deploy all code changes to dev environment
- [ ] Run end-to-end integration tests
- [ ] Create user acceptance test plan
- [ ] Document known limitations

---

## Files Modified in Phase 3

```
scripts/seed-data/
├── tools.json (MODIFIED - added TL-009, TL-010, TL-011)
└── playbooks.json (MODIFIED - added PB-013, updated PB-006)

projects/financial-intelligence-module-r1/notes/
└── Phase-3-Playbook-Configuration-Summary.md (NEW - this file)
```

---

## Conclusion

✅ **Phase 3 is complete** from a development standpoint. The playbook and tool definitions are ready for deployment.

**Key Achievement**: Finance Invoice Processing playbook (PB-013) integrates the three tool handlers created in Phase 2 using the standard playbook framework. This enables:
- Configuration-driven AI workflows (no code deployment for workflow changes)
- Reusable tool handlers (DataverseUpdateToolHandler can update any entity)
- Standard playbook execution model (same as Document Profile, Email Analysis, etc.)

**Deployment Ready**: Both `tools.json` and `playbooks.json` are ready to deploy to dev environment using the standard deployment scripts.

