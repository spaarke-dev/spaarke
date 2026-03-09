# Finance Intelligence Module R1 - MVP Implementation Guide

> **Version**: 1.0
> **Date**: February 11, 2025
> **Status**: Ready for Implementation

---

## Table of Contents

1. [MVP Overview](#mvp-overview)
2. [Architecture Summary](#architecture-summary)
3. [Implementation Phases](#implementation-phases)
4. [Phase 1: Dataverse Schema Updates](#phase-1-dataverse-schema-updates)
5. [Phase 2: Tool Handlers Implementation](#phase-2-tool-handlers-implementation)
6. [Phase 3: Playbook Configuration](#phase-3-playbook-configuration)
7. [Phase 4: Job Handler Updates](#phase-4-job-handler-updates)
8. [Phase 5: API Endpoints](#phase-5-api-endpoints)
9. [Phase 6: VisualHost Charts](#phase-6-visualhost-charts)
10. [Phase 7: Deployment & Validation](#phase-7-deployment--validation)
11. [Appendix: Reference Materials](#appendix-reference-materials)

---

## MVP Overview

### Business Requirements

The Finance Intelligence Module MVP must support three key scenarios:

#### **Scenario 1: Matter Overview Metrics** (Matter/Project form header)
Display three metric cards:
- Total Amount Billed to Date
- Total Budget Amount
- Remaining Budget Amount

#### **Scenario 2: Invoice Tab - Invoice List**
Show grid of invoices with columns:
- Invoice Number
- Invoice Name/Title
- AI-generated Summary
- Total Amount
- Link to Document (Dataverse record)
- Link to Invoice File (PDF in SPE)

#### **Scenario 3: Invoice Tab - Simple Metrics**
Display financial metrics:
- Budget Utilization % (percentage of budget used)
- Remaining Budget
- Average Invoice Amount
- Invoice Count

---

## Architecture Summary

### Simplified Data Model

```
sprk_matter (denormalized totals)
  ├─ sprk_totalbudget (Currency)
  ├─ sprk_totalamountbilled (Currency) ← Updated by API
  ├─ sprk_remainingbudget (Currency) ← Updated by API
  ├─ sprk_budgetutilizationpercent (Decimal) ← Updated by API
  ├─ sprk_invoicecount (Integer) ← Updated by API
  └─ sprk_averageinvoiceamount (Currency) ← Updated by API

sprk_invoice (header + JSON extraction)
  ├─ sprk_totalamount (Currency)
  ├─ sprk_aisummary (Multiline Text, 2000 chars) ← AI-generated summary
  ├─ sprk_extractedjson (Multiline Text, 10000 chars) ← Full extraction result
  └─ sprk_matter (Lookup)
```

**Entities NOT Used in MVP**:
- ~~sprk_billingevent~~ (deferred - line items stored as JSON)
- ~~sprk_spendsnapshot~~ (deferred - totals stored on matter)
- ~~sprk_spendsignal~~ (deferred - post-MVP analytics)

### Playbook-Driven Architecture

```
InvoiceExtractionJobHandler (thin wrapper)
  ↓
Playbook: "FinanceInvoiceProcessing"
  ↓
  ├─ InvoiceExtractionToolHandler (AI extraction)
  ├─ DataverseUpdateToolHandler (update invoice)
  ├─ FinancialCalculationToolHandler (calculate matter totals)
  └─ DataverseUpdateToolHandler (update matter)
```

**Key Principles**:
- ✅ Job Handler delegates to Playbook
- ✅ Playbook orchestrates Tool Handlers
- ✅ Tool Handlers contain business logic
- ✅ All matter fields updated by API (not calculated fields, business rules, or Power Automate)
- ✅ VisualHost reads stored Dataverse fields

---

## Implementation Phases

| Phase | Description | Duration | Dependencies |
|-------|-------------|----------|--------------|
| 1 | Dataverse Schema Updates | 2 hours | None |
| 2 | Tool Handlers Implementation | 1 day | Phase 1 |
| 3 | Playbook Configuration | 4 hours | Phase 2 |
| 4 | Job Handler Updates | 4 hours | Phase 3 |
| 5 | API Endpoints | 4 hours | Phase 4 |
| 6 | VisualHost Charts | 4 hours | Phase 5 |
| 7 | Deployment & Validation | 1 day | Phase 6 |

**Total Estimated Time**: 3-4 days

---

## Phase 1: Dataverse Schema Updates

### 1.1 Update sprk_invoice Entity

**Add New Fields**:

```xml
<!-- AI-generated summary field -->
<attribute name="sprk_aisummary" type="memo">
  <DisplayName>AI Summary</DisplayName>
  <Description>AI-generated invoice summary for quick review</Description>
  <MaxLength>2000</MaxLength>
  <Format>text</Format>
  <RequiredLevel>none</RequiredLevel>
</attribute>

<!-- Full extraction result as JSON -->
<attribute name="sprk_extractedjson" type="memo">
  <DisplayName>Extraction JSON</DisplayName>
  <Description>Full AI extraction result including line items (JSON format)</Description>
  <MaxLength>10000</MaxLength>
  <Format>text</Format>
  <RequiredLevel>none</RequiredLevel>
</attribute>
```

**Tasks**:
- [ ] Add `sprk_aisummary` field to sprk_invoice entity
- [ ] Add `sprk_extractedjson` field to sprk_invoice entity
- [ ] Update sprk_invoice form to include new fields (optional - for debugging)
- [ ] Publish customizations

### 1.2 Update sprk_matter Entity

**Add New Fields**:

```xml
<!-- Budget tracking fields -->
<attribute name="sprk_totalbudget" type="money">
  <DisplayName>Total Budget</DisplayName>
  <Description>Total budget amount for this matter</Description>
  <RequiredLevel>none</RequiredLevel>
  <Precision>2</Precision>
</attribute>

<attribute name="sprk_totalamountbilled" type="money">
  <DisplayName>Total Amount Billed</DisplayName>
  <Description>Sum of all extracted invoice amounts (updated by API)</Description>
  <RequiredLevel>none</RequiredLevel>
  <Precision>2</Precision>
</attribute>

<attribute name="sprk_remainingbudget" type="money">
  <DisplayName>Remaining Budget</DisplayName>
  <Description>Budget minus billed amount (updated by API, NOT calculated)</Description>
  <RequiredLevel>none</RequiredLevel>
  <Precision>2</Precision>
</attribute>

<attribute name="sprk_budgetutilizationpercent" type="decimal">
  <DisplayName>Budget Utilization %</DisplayName>
  <Description>Percentage of budget used (updated by API, NOT calculated)</Description>
  <RequiredLevel>none</RequiredLevel>
  <Precision>2</Precision>
  <MinValue>0</MinValue>
  <MaxValue>999.99</MaxValue>
</attribute>

<attribute name="sprk_invoicecount" type="integer">
  <DisplayName>Invoice Count</DisplayName>
  <Description>Count of extracted invoices (updated by API)</Description>
  <RequiredLevel>none</RequiredLevel>
  <MinValue>0</MinValue>
  <MaxValue>999999</MaxValue>
</attribute>

<attribute name="sprk_averageinvoiceamount" type="money">
  <DisplayName>Average Invoice Amount</DisplayName>
  <Description>Average invoice amount (updated by API, NOT calculated)</Description>
  <RequiredLevel>none</RequiredLevel>
  <Precision>2</Precision>
</attribute>
```

**Tasks**:
- [ ] Add `sprk_totalbudget` field to sprk_matter entity
- [ ] Add `sprk_totalamountbilled` field to sprk_matter entity
- [ ] Add `sprk_remainingbudget` field to sprk_matter entity
- [ ] Add `sprk_budgetutilizationpercent` field to sprk_matter entity
- [ ] Add `sprk_invoicecount` field to sprk_matter entity
- [ ] Add `sprk_averageinvoiceamount` field to sprk_matter entity
- [ ] Update sprk_matter form to display new fields (in Finance section)
- [ ] Publish customizations

### 1.3 Deployment Script

**Create PowerShell script** to add fields:

```powershell
# scripts/Add-FinanceMVPFields.ps1
<#
.SYNOPSIS
    Adds Finance Intelligence MVP fields to sprk_invoice and sprk_matter entities.
#>

param(
    [Parameter(Mandatory = $false)]
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com"
)

# Field definitions...
# (Full script in scripts/ directory)
```

**Tasks**:
- [ ] Create `scripts/Add-FinanceMVPFields.ps1`
- [ ] Test script in dev environment
- [ ] Document field additions in schema documentation

---

## Phase 2: Tool Handlers Implementation

### 2.1 FinancialCalculationToolHandler

**Purpose**: Calculate matter totals when a new invoice is extracted.

**File**: `src/server/api/Sprk.Bff.Api/Services/Finance/Tools/FinancialCalculationToolHandler.cs`

```csharp
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Services.Finance.Tools;

/// <summary>
/// Tool handler for calculating matter financial totals.
/// Updates denormalized budget tracking fields on sprk_matter.
/// </summary>
public class FinancialCalculationToolHandler : IAiToolHandler
{
    private readonly IDataverseService _dataverseService;
    private readonly ILogger<FinancialCalculationToolHandler> _logger;

    public string ToolName => "FinancialCalculation";

    public FinancialCalculationToolHandler(
        IDataverseService dataverseService,
        ILogger<FinancialCalculationToolHandler> logger)
    {
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ToolResult> ExecuteAsync(ToolParameters parameters, CancellationToken ct)
    {
        var matterId = parameters.GetGuid("matterId");
        var invoiceAmount = parameters.GetDecimal("invoiceAmount");

        _logger.LogInformation(
            "Calculating matter totals for matter {MatterId}, new invoice amount {Amount}",
            matterId, invoiceAmount);

        // Get current matter totals with concurrency retry
        int maxRetries = 3;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var matter = await _dataverseService.GetRecordAsync(
                    "sprk_matter",
                    matterId,
                    new[] {
                        "sprk_totalbudget",
                        "sprk_totalamountbilled",
                        "sprk_invoicecount"
                    },
                    ct);

                var budget = matter.GetMoney("sprk_totalbudget") ?? 0m;
                var currentBilled = matter.GetMoney("sprk_totalamountbilled") ?? 0m;
                var currentCount = matter.GetInt("sprk_invoicecount") ?? 0;

                // Calculate new values
                var newBilled = currentBilled + invoiceAmount;
                var newCount = currentCount + 1;
                var newRemaining = budget - newBilled;
                var newUtilization = budget > 0 ? (newBilled / budget) * 100 : 0m;
                var newAverage = newCount > 0 ? newBilled / newCount : 0m;

                var result = new MatterTotals
                {
                    TotalAmountBilled = newBilled,
                    RemainingBudget = newRemaining,
                    BudgetUtilizationPercent = Math.Round(newUtilization, 2),
                    InvoiceCount = newCount,
                    AverageInvoiceAmount = newAverage
                };

                _logger.LogInformation(
                    "Matter {MatterId} totals calculated: Billed={Billed}, Remaining={Remaining}, Utilization={Utilization}%, Count={Count}",
                    matterId, newBilled, newRemaining, newUtilization, newCount);

                return ToolResult.Success(result);
            }
            catch (ConcurrencyException ex)
            {
                _logger.LogWarning(
                    "Concurrency conflict calculating matter {MatterId}, attempt {Attempt}/{MaxRetries}",
                    matterId, attempt, maxRetries);

                if (attempt == maxRetries)
                {
                    throw;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), ct);
            }
        }

        throw new InvalidOperationException("Failed to calculate matter totals after retries");
    }
}

public class MatterTotals
{
    public decimal TotalAmountBilled { get; set; }
    public decimal RemainingBudget { get; set; }
    public decimal BudgetUtilizationPercent { get; set; }
    public int InvoiceCount { get; set; }
    public decimal AverageInvoiceAmount { get; set; }
}
```

**Tasks**:
- [ ] Create `FinancialCalculationToolHandler.cs`
- [ ] Create `MatterTotals.cs` model
- [ ] Add unit tests for calculation logic
- [ ] Add concurrency conflict tests
- [ ] Register in DI (FinanceModule.cs)

### 2.2 DataverseUpdateToolHandler

**Purpose**: Generic entity update from playbook nodes.

**File**: `src/server/api/Sprk.Bff.Api/Services/Ai/Tools/DataverseUpdateToolHandler.cs`

```csharp
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Services.Ai.Tools;

/// <summary>
/// Generic tool handler for updating Dataverse entity records from playbooks.
/// Allows playbooks to update any entity without custom tool implementations.
/// </summary>
public class DataverseUpdateToolHandler : IAiToolHandler
{
    private readonly IDataverseService _dataverseService;
    private readonly ILogger<DataverseUpdateToolHandler> _logger;

    public string ToolName => "DataverseUpdate";

    public DataverseUpdateToolHandler(
        IDataverseService dataverseService,
        ILogger<DataverseUpdateToolHandler> logger)
    {
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ToolResult> ExecuteAsync(ToolParameters parameters, CancellationToken ct)
    {
        var entityName = parameters.GetString("entity");
        var recordId = parameters.GetGuid("recordId");
        var fields = parameters.GetDictionary("fields");

        if (string.IsNullOrWhiteSpace(entityName))
        {
            return ToolResult.Error("Entity name is required");
        }

        if (recordId == Guid.Empty)
        {
            return ToolResult.Error("Record ID is required and must be a valid GUID");
        }

        if (fields == null || fields.Count == 0)
        {
            return ToolResult.Error("Fields dictionary is required and must contain at least one field");
        }

        _logger.LogInformation(
            "Updating {EntityName} record {RecordId} with {FieldCount} fields",
            entityName, recordId, fields.Count);

        try
        {
            await _dataverseService.UpdateRecordFieldsAsync(entityName, recordId, fields, ct);

            _logger.LogInformation(
                "Successfully updated {EntityName} record {RecordId}",
                entityName, recordId);

            return ToolResult.Success($"Updated {entityName} record {recordId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to update {EntityName} record {RecordId}: {Error}",
                entityName, recordId, ex.Message);

            return ToolResult.Error($"Failed to update {entityName}: {ex.Message}");
        }
    }
}
```

**Tasks**:
- [ ] Create `DataverseUpdateToolHandler.cs`
- [ ] Add unit tests with mocked IDataverseService
- [ ] Add validation tests (empty entity name, invalid GUID, etc.)
- [ ] Register in DI (Infrastructure/DI module)

### 2.3 InvoiceExtractionToolHandler

**Purpose**: Wrap existing InvoiceAnalysisService for playbook integration.

**File**: `src/server/api/Sprk.Bff.Api/Services/Finance/Tools/InvoiceExtractionToolHandler.cs`

```csharp
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Finance.Models;

namespace Sprk.Bff.Api.Services.Finance.Tools;

/// <summary>
/// Tool handler for AI-powered invoice extraction.
/// Wraps InvoiceAnalysisService to expose extraction as a playbook tool.
/// </summary>
public class InvoiceExtractionToolHandler : IAiToolHandler
{
    private readonly IInvoiceAnalysisService _invoiceAnalysisService;
    private readonly ILogger<InvoiceExtractionToolHandler> _logger;

    public string ToolName => "InvoiceExtraction";

    public InvoiceExtractionToolHandler(
        IInvoiceAnalysisService invoiceAnalysisService,
        ILogger<InvoiceExtractionToolHandler> logger)
    {
        _invoiceAnalysisService = invoiceAnalysisService ?? throw new ArgumentNullException(nameof(invoiceAnalysisService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ToolResult> ExecuteAsync(ToolParameters parameters, CancellationToken ct)
    {
        var documentText = parameters.GetString("documentText");
        var reviewerHints = parameters.GetObject<InvoiceHints>("reviewerHints");

        if (string.IsNullOrWhiteSpace(documentText))
        {
            return ToolResult.Error("Document text is required");
        }

        _logger.LogInformation(
            "Extracting invoice facts from document (text length: {Length})",
            documentText.Length);

        try
        {
            var extractionResult = await _invoiceAnalysisService.ExtractInvoiceFactsAsync(
                documentText,
                reviewerHints,
                ct);

            // Generate summary from extraction
            var summary = GenerateSummary(extractionResult);

            // Enhance result with summary
            var enhancedResult = new
            {
                extractionResult.InvoiceNumber,
                extractionResult.InvoiceDate,
                extractionResult.TotalAmount,
                extractionResult.Currency,
                extractionResult.VendorName,
                extractionResult.LineItems,
                extractionResult.ExtractionConfidence,
                Summary = summary
            };

            _logger.LogInformation(
                "Invoice extraction completed: {LineItemCount} line items, confidence {Confidence}",
                extractionResult.LineItems.Length, extractionResult.ExtractionConfidence);

            return ToolResult.Success(enhancedResult);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Invoice extraction failed: {Error}", ex.Message);
            return ToolResult.Error($"Extraction failed: {ex.Message}");
        }
    }

    private static string GenerateSummary(ExtractionResult extraction)
    {
        var lineCount = extraction.LineItems?.Length ?? 0;
        var amount = extraction.TotalAmount?.ToString("C") ?? "unknown";
        var vendor = extraction.VendorName ?? "vendor";

        return $"Invoice from {vendor} for {amount} with {lineCount} line items";
    }
}
```

**Tasks**:
- [ ] Create `InvoiceExtractionToolHandler.cs`
- [ ] Implement `GenerateSummary()` helper method
- [ ] Add unit tests
- [ ] Register in DI (FinanceModule.cs)

### 2.4 Tool Handler Registration

**File**: `src/server/api/Sprk.Bff.Api/Infrastructure/DI/FinanceModule.cs`

```csharp
// Add to RegisterServices method
public static IServiceCollection RegisterFinanceServices(this IServiceCollection services)
{
    // ... existing registrations ...

    // Tool Handlers
    services.AddSingleton<IAiToolHandler, FinancialCalculationToolHandler>();
    services.AddSingleton<IAiToolHandler, InvoiceExtractionToolHandler>();
    services.AddSingleton<IAiToolHandler, DataverseUpdateToolHandler>();

    return services;
}
```

**Tasks**:
- [ ] Add tool handler registrations to FinanceModule.cs
- [ ] Verify IToolHandlerRegistry picks up new handlers
- [ ] Test handler discovery with integration test

---

## Phase 3: Playbook Configuration

✅ **STATUS**: COMPLETED - Playbook added to seed data

### 3.1 Deploy Playbook via Seed Data

The Finance Invoice Processing playbook (PB-013) has been added to the seed data files:

**Updated Files**:
- `scripts/seed-data/tools.json` - Added TL-009, TL-010, TL-011 (finance tool handlers)
- `scripts/seed-data/playbooks.json` - Added PB-013 Finance Invoice Processing playbook

**Playbook Definition**:
- **ID**: PB-013
- **Name**: Finance Invoice Processing
- **Trigger**: finance-invoice-processing
- **Skills**: SKL-002 (Invoice Processing)
- **Actions**: ACT-001 (Extract Entities), ACT-008 (Calculate Values)
- **Tools**: TL-009 (Invoice Extraction), TL-010 (Dataverse Update), TL-011 (Financial Calculation)

**Workflow**:
1. Extract invoice header and line items using AI (TL-009 / InvoiceExtractionToolHandler)
2. Generate AI summary (sprk_aisummary) and JSON (sprk_extractedjson)
3. Update Invoice record with extracted data (TL-010 / DataverseUpdateToolHandler)
4. Calculate matter financial totals (TL-011 / FinancialCalculationToolHandler - stubbed)
5. Update Matter record with aggregated totals (TL-010 / DataverseUpdateToolHandler)

**Deploy to Dataverse**:

```powershell
# Deploy tools (includes TL-009, TL-010, TL-011)
cd scripts/seed-data
.\Deploy-Tools.ps1

# Deploy playbook (includes PB-013)
.\Deploy-Playbooks.ps1

param(
    [Parameter(Mandatory = $false)]
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com"
)

$ErrorActionPreference = "Stop"

# Acquire token
$tenantId = "a221a95e-6abc-4434-aecc-e48338a1b2f2"
$tokenJson = az account get-access-token --resource "$EnvironmentUrl" --tenant $tenantId 2>&1 | Out-String
$tokenObj = $tokenJson | ConvertFrom-Json
$bearerToken = $tokenObj.accessToken

$headers = @{
    "Authorization" = "Bearer $bearerToken"
    "Content-Type"  = "application/json"
    "Accept"        = "application/json"
}

$apiUrl = "$EnvironmentUrl/api/data/v9.2"

# Playbook workflow definition
$workflowJson = @{
    nodes = @(
        @{
            id = "1"
            name = "Extract Invoice Facts"
            tool = "InvoiceExtraction"
            inputs = @{
                documentText = "{{context.documentText}}"
                reviewerHints = "{{context.reviewerHints}}"
            }
            outputs = @{
                extractionResult = "result"
            }
        },
        @{
            id = "2"
            name = "Update Invoice Record"
            tool = "DataverseUpdate"
            inputs = @{
                entity = "sprk_invoice"
                recordId = "{{context.invoiceId}}"
                fields = @{
                    sprk_totalamount = "{{nodes.1.outputs.extractionResult.totalAmount}}"
                    sprk_invoicenumber = "{{nodes.1.outputs.extractionResult.invoiceNumber}}"
                    sprk_invoicedate = "{{nodes.1.outputs.extractionResult.invoiceDate}}"
                    sprk_currency = "{{nodes.1.outputs.extractionResult.currency}}"
                    sprk_aisummary = "{{nodes.1.outputs.extractionResult.summary}}"
                    sprk_extractedjson = "{{nodes.1.outputs.extractionResult | toJson}}"
                    sprk_extractionstatus = 100000001
                }
            }
        },
        @{
            id = "3"
            name = "Calculate Matter Totals"
            tool = "FinancialCalculation"
            inputs = @{
                matterId = "{{context.matterId}}"
                invoiceAmount = "{{nodes.1.outputs.extractionResult.totalAmount}}"
            }
            outputs = @{
                matterTotals = "result"
            }
        },
        @{
            id = "4"
            name = "Update Matter Record"
            tool = "DataverseUpdate"
            inputs = @{
                entity = "sprk_matter"
                recordId = "{{context.matterId}}"
                fields = @{
                    sprk_totalamountbilled = "{{nodes.3.outputs.matterTotals.totalAmountBilled}}"
                    sprk_remainingbudget = "{{nodes.3.outputs.matterTotals.remainingBudget}}"
                    sprk_budgetutilizationpercent = "{{nodes.3.outputs.matterTotals.budgetUtilizationPercent}}"
                    sprk_invoicecount = "{{nodes.3.outputs.matterTotals.invoiceCount}}"
                    sprk_averageinvoiceamount = "{{nodes.3.outputs.matterTotals.averageInvoiceAmount}}"
                }
            }
        }
    )
} | ConvertTo-Json -Depth 10

# Create playbook record
$playbook = @{
    "sprk_name" = "Finance Invoice Processing"
    "sprk_playbookkey" = "finance-invoice-processing"
    "sprk_description" = "Complete invoice extraction and financial calculation workflow for Finance Intelligence Module MVP"
    "sprk_isactive" = $true
    "sprk_canvaslayoutjson" = $workflowJson
} | ConvertTo-Json -Depth 10

try {
    $response = Invoke-RestMethod -Uri "$apiUrl/sprk_analysisplaybooks" -Method Post -Headers $headers -Body $playbook
    Write-Host "✅ Created playbook: Finance Invoice Processing" -ForegroundColor Green
    Write-Host "   ID: $($response.sprk_analysisplaybookid)" -ForegroundColor Green
}
catch {
    Write-Error "Failed to create playbook: $_"
}
```

**Tasks**:
- [ ] Create `scripts/Create-InvoiceProcessingPlaybook.ps1`
- [ ] Execute script to create playbook in dev environment
- [ ] Verify playbook record in Dataverse
- [ ] Test playbook execution with sample data

### 3.2 Playbook Testing

**Create test harness**:

**File**: `tests/integration/Sprk.Bff.Api.Tests/Services/Finance/InvoiceProcessingPlaybookTests.cs`

```csharp
[Fact]
public async Task InvoiceProcessingPlaybook_ShouldUpdateInvoiceAndMatter()
{
    // Arrange
    var matterId = Guid.NewGuid();
    var invoiceId = Guid.NewGuid();
    var documentText = "Sample invoice text...";

    var context = new Dictionary<string, object>
    {
        ["matterId"] = matterId,
        ["invoiceId"] = invoiceId,
        ["documentText"] = documentText,
        ["reviewerHints"] = new InvoiceHints()
    };

    // Act
    var result = await _playbookService.ExecuteAsync(
        "finance-invoice-processing",
        context,
        CancellationToken.None);

    // Assert
    Assert.True(result.Success);

    // Verify invoice was updated
    var invoice = await _dataverseService.GetRecordAsync("sprk_invoice", invoiceId);
    Assert.NotNull(invoice.GetString("sprk_aisummary"));
    Assert.NotNull(invoice.GetString("sprk_extractedjson"));

    // Verify matter was updated
    var matter = await _dataverseService.GetRecordAsync("sprk_matter", matterId);
    Assert.True(matter.GetMoney("sprk_totalamountbilled") > 0);
    Assert.Equal(1, matter.GetInt("sprk_invoicecount"));
}
```

**Tasks**:
- [ ] Create `InvoiceProcessingPlaybookTests.cs`
- [ ] Add test cases for success scenarios
- [ ] Add test cases for failure scenarios (missing context, AI errors, etc.)
- [ ] Run integration tests against dev environment

---

## Phase 4: Job Handler Updates

### 4.1 Update InvoiceExtractionJobHandler

**File**: `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/InvoiceExtractionJobHandler.cs`

**Simplified implementation** (delegates to playbook):

```csharp
using System.Diagnostics;
using System.Text.Json;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Jobs.Handlers;

/// <summary>
/// Job handler for invoice extraction jobs.
/// Delegates extraction workflow to "FinanceInvoiceProcessing" playbook.
/// </summary>
public class InvoiceExtractionJobHandler : IJobHandler
{
    private readonly IAppOnlyAnalysisService _analysisService;
    private readonly IDataverseService _dataverseService;
    private readonly ISpeFileOperations _speFileOperations;
    private readonly TextExtractorService _textExtractor;
    private readonly FinanceTelemetry _telemetry;
    private readonly ILogger<InvoiceExtractionJobHandler> _logger;

    public const string JobTypeName = "InvoiceExtraction";
    public string JobType => JobTypeName;

    public InvoiceExtractionJobHandler(
        IAppOnlyAnalysisService analysisService,
        IDataverseService dataverseService,
        ISpeFileOperations speFileOperations,
        TextExtractorService textExtractor,
        FinanceTelemetry telemetry,
        ILogger<InvoiceExtractionJobHandler> logger)
    {
        _analysisService = analysisService ?? throw new ArgumentNullException(nameof(analysisService));
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _speFileOperations = speFileOperations ?? throw new ArgumentNullException(nameof(speFileOperations));
        _textExtractor = textExtractor ?? throw new ArgumentNullException(nameof(textExtractor));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        using var activity = _telemetry.StartActivity("InvoiceExtraction.ProcessJob", correlationId: job.CorrelationId);

        try
        {
            _logger.LogInformation(
                "Processing invoice extraction job {JobId} for subject {SubjectId}, CorrelationId {CorrelationId}",
                job.JobId, job.SubjectId, job.CorrelationId);

            // 1. Parse payload
            var payload = ParsePayload(job.Payload);
            if (payload == null || payload.InvoiceId == Guid.Empty || payload.DocumentId == Guid.Empty)
            {
                _logger.LogError("Invalid payload for invoice extraction job {JobId}", job.JobId);
                return JobOutcome.Poisoned(job.JobId, JobType, "Invalid job payload", job.Attempt, stopwatch.Elapsed);
            }

            var invoiceId = payload.InvoiceId;
            var documentId = payload.DocumentId;

            // 2. Load invoice (to get matterId)
            var invoice = await LoadInvoiceAsync(invoiceId, ct);
            if (invoice == null)
            {
                _logger.LogError("Invoice {InvoiceId} not found", invoiceId);
                return JobOutcome.Poisoned(job.JobId, JobType, "Invoice not found", job.Attempt, stopwatch.Elapsed);
            }

            if (!invoice.MatterId.HasValue)
            {
                _logger.LogWarning("Invoice {InvoiceId} has no matter association", invoiceId);
                // Continue anyway - will skip matter updates in playbook
            }

            // 3. Load document (to get SPE file info)
            var document = await LoadDocumentAsync(documentId, ct);
            if (document == null)
            {
                _logger.LogError("Document {DocumentId} not found", documentId);
                return JobOutcome.Poisoned(job.JobId, JobType, "Document not found", job.Attempt, stopwatch.Elapsed);
            }

            // 4. Download file from SPE
            var fileStream = await _speFileOperations.DownloadFileAsync(
                document.GraphDriveId,
                document.GraphItemId,
                ct);

            if (fileStream == null)
            {
                _logger.LogError("Failed to download document {DocumentId} from SPE", documentId);
                await UpdateInvoiceExtractionStatusAsync(invoiceId, ExtractionStatusFailed, ct);
                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed); // Don't retry
            }

            // 5. Extract text from document
            string documentText;
            try
            {
                using (fileStream)
                {
                    var textResult = await _textExtractor.ExtractAsync(fileStream, document.FileName, ct);
                    if (!textResult.Success || string.IsNullOrWhiteSpace(textResult.Text))
                    {
                        _logger.LogError("Text extraction failed for document {DocumentId}", documentId);
                        await UpdateInvoiceExtractionStatusAsync(invoiceId, ExtractionStatusFailed, ct);
                        return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed); // Don't retry
                    }
                    documentText = textResult.Text;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting text from document {DocumentId}", documentId);
                await UpdateInvoiceExtractionStatusAsync(invoiceId, ExtractionStatusFailed, ct);
                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed); // Don't retry
            }

            // 6. Execute playbook-driven workflow
            var playbookContext = new Dictionary<string, object>
            {
                ["invoiceId"] = invoiceId,
                ["documentId"] = documentId,
                ["matterId"] = invoice.MatterId ?? Guid.Empty,
                ["documentText"] = documentText,
                ["reviewerHints"] = new InvoiceHints() // Could load from invoice if reviewer provided hints
            };

            var result = await _analysisService.ExecutePlaybookAsync(
                playbookName: "FinanceInvoiceProcessing",
                context: playbookContext,
                ct);

            if (!result.Success)
            {
                _logger.LogError("Playbook execution failed for invoice {InvoiceId}: {Error}",
                    invoiceId, result.ErrorMessage);
                await UpdateInvoiceExtractionStatusAsync(invoiceId, ExtractionStatusFailed, ct);
                return JobOutcome.Failure(job.JobId, JobType, result.ErrorMessage, job.Attempt, stopwatch.Elapsed);
            }

            _logger.LogInformation(
                "Invoice extraction completed via playbook. Invoice {InvoiceId}, Matter {MatterId}, Duration {Duration}ms",
                invoiceId, invoice.MatterId, stopwatch.ElapsedMilliseconds);

            return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Invoice extraction job {JobId} failed: {Error}", job.JobId, ex.Message);

            var isRetryable = IsRetryableException(ex);
            var isLastAttempt = job.Attempt >= job.MaxAttempts;

            if (isRetryable && !isLastAttempt)
            {
                return JobOutcome.Failure(job.JobId, JobType, ex.Message, job.Attempt, stopwatch.Elapsed);
            }

            return JobOutcome.Poisoned(job.JobId, JobType, ex.Message, job.Attempt, stopwatch.Elapsed);
        }
    }

    // ... helper methods (ParsePayload, LoadInvoiceAsync, etc.) ...
}
```

**Tasks**:
- [ ] Refactor `InvoiceExtractionJobHandler.cs` to delegate to playbook
- [ ] Remove BillingEvent creation loop (deferred to post-MVP)
- [ ] Remove SpendSnapshot job enqueueing (deferred to post-MVP)
- [ ] Keep text extraction and file download logic
- [ ] Update unit tests
- [ ] Update integration tests

---

## Phase 5: API Endpoints

### 5.1 Matter Overview Endpoint

**File**: `src/server/api/Sprk.Bff.Api/Api/Finance/MatterOverviewEndpoints.cs`

```csharp
using Microsoft.AspNetCore.Mvc;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Api.Finance;

/// <summary>
/// API endpoints for matter financial overview.
/// </summary>
public static class MatterOverviewEndpoints
{
    public static void MapMatterOverviewEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/finance/matters")
            .WithTags("Finance")
            .RequireAuthorization();

        group.MapGet("/{matterId:guid}/overview", GetMatterOverview)
            .WithName("GetMatterOverview")
            .WithOpenApi();
    }

    private static async Task<IResult> GetMatterOverview(
        [FromRoute] Guid matterId,
        [FromServices] IDataverseService dataverseService,
        CancellationToken ct)
    {
        var matter = await dataverseService.GetRecordAsync(
            "sprk_matter",
            matterId,
            new[]
            {
                "sprk_totalbudget",
                "sprk_totalamountbilled",
                "sprk_remainingbudget",
                "sprk_budgetutilizationpercent",
                "sprk_invoicecount",
                "sprk_averageinvoiceamount"
            },
            ct);

        if (matter == null)
        {
            return Results.NotFound($"Matter {matterId} not found");
        }

        var overview = new MatterOverviewDto
        {
            MatterId = matterId,
            TotalBudget = matter.GetMoney("sprk_totalbudget") ?? 0m,
            TotalAmountBilled = matter.GetMoney("sprk_totalamountbilled") ?? 0m,
            RemainingBudget = matter.GetMoney("sprk_remainingbudget") ?? 0m,
            BudgetUtilizationPercent = matter.GetDecimal("sprk_budgetutilizationpercent") ?? 0m,
            InvoiceCount = matter.GetInt("sprk_invoicecount") ?? 0,
            AverageInvoiceAmount = matter.GetMoney("sprk_averageinvoiceamount") ?? 0m
        };

        return Results.Ok(overview);
    }
}

public class MatterOverviewDto
{
    public Guid MatterId { get; set; }
    public decimal TotalBudget { get; set; }
    public decimal TotalAmountBilled { get; set; }
    public decimal RemainingBudget { get; set; }
    public decimal BudgetUtilizationPercent { get; set; }
    public int InvoiceCount { get; set; }
    public decimal AverageInvoiceAmount { get; set; }
}
```

**Tasks**:
- [ ] Create `MatterOverviewEndpoints.cs`
- [ ] Create `MatterOverviewDto.cs`
- [ ] Register endpoints in Program.cs
- [ ] Add authorization filter
- [ ] Add unit tests
- [ ] Add integration tests

### 5.2 Invoice List Endpoint

**File**: `src/server/api/Sprk.Bff.Api/Api/Finance/InvoiceListEndpoints.cs`

```csharp
using Microsoft.AspNetCore.Mvc;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Api.Finance;

/// <summary>
/// API endpoints for invoice list queries.
/// </summary>
public static class InvoiceListEndpoints
{
    public static void MapInvoiceListEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/finance/invoices")
            .WithTags("Finance")
            .RequireAuthorization();

        group.MapGet("/", GetInvoices)
            .WithName("GetInvoices")
            .WithOpenApi();

        group.MapGet("/{invoiceId:guid}", GetInvoice)
            .WithName("GetInvoice")
            .WithOpenApi();
    }

    private static async Task<IResult> GetInvoices(
        [FromQuery] Guid? matterId,
        [FromServices] IDataverseService dataverseService,
        CancellationToken ct)
    {
        // Query invoices with filters
        var filter = matterId.HasValue
            ? $"_sprk_matter_value eq {matterId.Value}"
            : null;

        var invoices = await dataverseService.QueryRecordsAsync(
            "sprk_invoice",
            new[]
            {
                "sprk_invoiceid",
                "sprk_name",
                "sprk_invoicenumber",
                "sprk_invoicedate",
                "sprk_totalamount",
                "sprk_currency",
                "sprk_aisummary",
                "_sprk_document_value",
                "_sprk_matter_value"
            },
            filter,
            orderBy: "sprk_invoicedate desc",
            ct);

        var dtos = invoices.Select(inv => new InvoiceListItemDto
        {
            InvoiceId = inv.Id,
            InvoiceName = inv.GetString("sprk_name"),
            InvoiceNumber = inv.GetString("sprk_invoicenumber"),
            InvoiceDate = inv.GetDateTime("sprk_invoicedate"),
            TotalAmount = inv.GetMoney("sprk_totalamount") ?? 0m,
            Currency = inv.GetString("sprk_currency"),
            AiSummary = inv.GetString("sprk_aisummary"),
            DocumentId = inv.GetLookupId("sprk_document"),
            MatterId = inv.GetLookupId("sprk_matter")
        }).ToList();

        return Results.Ok(dtos);
    }

    private static async Task<IResult> GetInvoice(
        [FromRoute] Guid invoiceId,
        [FromServices] IDataverseService dataverseService,
        CancellationToken ct)
    {
        var invoice = await dataverseService.GetRecordAsync(
            "sprk_invoice",
            invoiceId,
            new[]
            {
                "sprk_name",
                "sprk_invoicenumber",
                "sprk_invoicedate",
                "sprk_totalamount",
                "sprk_currency",
                "sprk_aisummary",
                "sprk_extractedjson",
                "_sprk_document_value",
                "_sprk_matter_value",
                "_sprk_vendororg_value"
            },
            ct);

        if (invoice == null)
        {
            return Results.NotFound($"Invoice {invoiceId} not found");
        }

        var dto = new InvoiceDetailDto
        {
            InvoiceId = invoice.Id,
            InvoiceName = invoice.GetString("sprk_name"),
            InvoiceNumber = invoice.GetString("sprk_invoicenumber"),
            InvoiceDate = invoice.GetDateTime("sprk_invoicedate"),
            TotalAmount = invoice.GetMoney("sprk_totalamount") ?? 0m,
            Currency = invoice.GetString("sprk_currency"),
            AiSummary = invoice.GetString("sprk_aisummary"),
            ExtractionJson = invoice.GetString("sprk_extractedjson"),
            DocumentId = invoice.GetLookupId("sprk_document"),
            MatterId = invoice.GetLookupId("sprk_matter"),
            VendorOrgId = invoice.GetLookupId("sprk_vendororg")
        };

        return Results.Ok(dto);
    }
}

public class InvoiceListItemDto
{
    public Guid InvoiceId { get; set; }
    public string? InvoiceName { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateTime? InvoiceDate { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Currency { get; set; }
    public string? AiSummary { get; set; }
    public Guid? DocumentId { get; set; }
    public Guid? MatterId { get; set; }
}

public class InvoiceDetailDto : InvoiceListItemDto
{
    public string? ExtractionJson { get; set; }
    public Guid? VendorOrgId { get; set; }
}
```

**Tasks**:
- [ ] Create `InvoiceListEndpoints.cs`
- [ ] Create DTOs
- [ ] Register endpoints in Program.cs
- [ ] Add authorization filter
- [ ] Add unit tests
- [ ] Add integration tests
- [ ] Test with Postman/Swagger

---

## Phase 6: VisualHost Charts

### 6.1 Matter Overview Chart Definition

**File**: `infrastructure/visualhost/matter-overview-charts.json`

```json
{
  "chartDefinitionKey": "matter-overview-finance",
  "title": "Financial Overview",
  "description": "Finance Intelligence metrics for matter/project overview",
  "version": "1.0",
  "charts": [
    {
      "id": "budget-utilization-card",
      "type": "metric-card",
      "entity": "sprk_matter",
      "field": "sprk_budgetutilizationpercent",
      "label": "Budget Utilized",
      "description": "Percentage of total budget that has been billed",
      "format": "percentage",
      "icon": "PieChart",
      "thresholds": [
        {
          "max": 75,
          "color": "green",
          "label": "On Track"
        },
        {
          "min": 75,
          "max": 90,
          "color": "yellow",
          "label": "Warning"
        },
        {
          "min": 90,
          "color": "red",
          "label": "Over Budget"
        }
      ]
    },
    {
      "id": "total-billed-card",
      "type": "metric-card",
      "entity": "sprk_matter",
      "field": "sprk_totalamountbilled",
      "label": "Total Billed",
      "description": "Total amount billed across all invoices",
      "format": "currency",
      "icon": "Money"
    },
    {
      "id": "remaining-budget-card",
      "type": "metric-card",
      "entity": "sprk_matter",
      "field": "sprk_remainingbudget",
      "label": "Remaining Budget",
      "description": "Budget amount remaining",
      "format": "currency",
      "icon": "Calculator",
      "thresholds": [
        {
          "max": 0,
          "color": "red",
          "label": "Over Budget"
        },
        {
          "min": 0,
          "color": "green",
          "label": "Available"
        }
      ]
    }
  ]
}
```

**Tasks**:
- [ ] Create `matter-overview-charts.json`
- [ ] Import chart definition to Dataverse
- [ ] Test rendering in Finance Panel PCF
- [ ] Adjust thresholds/colors based on user feedback

### 6.2 Invoice Tab Chart Definition

**File**: `infrastructure/visualhost/invoice-tab-charts.json`

```json
{
  "chartDefinitionKey": "invoice-tab-finance",
  "title": "Invoice Analytics",
  "description": "Invoice list and metrics for Finance Intelligence",
  "version": "1.0",
  "charts": [
    {
      "id": "invoice-list-grid",
      "type": "grid",
      "entity": "sprk_invoice",
      "filter": "_sprk_matter_value eq @matterId",
      "orderBy": "sprk_invoicedate desc",
      "columns": [
        {
          "field": "sprk_invoicenumber",
          "label": "Invoice #",
          "width": 120
        },
        {
          "field": "sprk_name",
          "label": "Invoice Name",
          "width": 200
        },
        {
          "field": "sprk_aisummary",
          "label": "Summary",
          "width": 300
        },
        {
          "field": "sprk_totalamount",
          "label": "Amount",
          "format": "currency",
          "width": 120
        },
        {
          "field": "sprk_invoicedate",
          "label": "Date",
          "format": "date",
          "width": 100
        }
      ],
      "actions": [
        {
          "id": "view-document",
          "label": "View Document",
          "icon": "Document",
          "navigate": "/main.aspx?pagetype=entityrecord&etn=sprk_document&id={sprk_document}"
        },
        {
          "id": "view-invoice-details",
          "label": "View Details",
          "icon": "View",
          "navigate": "/main.aspx?pagetype=entityrecord&etn=sprk_invoice&id={sprk_invoiceid}"
        }
      ]
    },
    {
      "id": "invoice-count-card",
      "type": "metric-card",
      "entity": "sprk_matter",
      "field": "sprk_invoicecount",
      "label": "Total Invoices",
      "description": "Number of extracted invoices",
      "format": "number",
      "icon": "Receipt"
    },
    {
      "id": "average-invoice-card",
      "type": "metric-card",
      "entity": "sprk_matter",
      "field": "sprk_averageinvoiceamount",
      "label": "Average Invoice",
      "description": "Average invoice amount",
      "format": "currency",
      "icon": "BarChart"
    }
  ]
}
```

**Tasks**:
- [ ] Create `invoice-tab-charts.json`
- [ ] Import chart definition to Dataverse
- [ ] Test grid rendering with sample invoices
- [ ] Test navigation actions (view document, view details)

### 6.3 Chart Import Script

**File**: `infrastructure/visualhost/Import-ChartDefinitions.ps1`

```powershell
<#
.SYNOPSIS
    Imports VisualHost chart definitions to Dataverse.
#>

param(
    [Parameter(Mandatory = $false)]
    [string]$EnvironmentUrl = "https://spaarkedev1.crm.dynamics.com"
)

$ErrorActionPreference = "Stop"

# Acquire token
$tenantId = "a221a95e-6abc-4434-aecc-e48338a1b2f2"
$tokenJson = az account get-access-token --resource "$EnvironmentUrl" --tenant $tenantId 2>&1 | Out-String
$tokenObj = $tokenJson | ConvertFrom-Json
$bearerToken = $tokenObj.accessToken

$headers = @{
    "Authorization" = "Bearer $bearerToken"
    "Content-Type"  = "application/json"
    "Accept"        = "application/json"
}

$apiUrl = "$EnvironmentUrl/api/data/v9.2"

# Import matter overview charts
$matterOverviewJson = Get-Content "matter-overview-charts.json" -Raw
$matterOverview = @{
    "sprk_name" = "Matter Overview - Finance"
    "sprk_chartdefinitionkey" = "matter-overview-finance"
    "sprk_chartdefinitionjson" = $matterOverviewJson
} | ConvertTo-Json

try {
    Invoke-RestMethod -Uri "$apiUrl/sprk_chartdefinitions" -Method Post -Headers $headers -Body $matterOverview
    Write-Host "✅ Imported: Matter Overview charts" -ForegroundColor Green
}
catch {
    Write-Warning "Failed to import Matter Overview charts: $_"
}

# Import invoice tab charts
$invoiceTabJson = Get-Content "invoice-tab-charts.json" -Raw
$invoiceTab = @{
    "sprk_name" = "Invoice Tab - Finance"
    "sprk_chartdefinitionkey" = "invoice-tab-finance"
    "sprk_chartdefinitionjson" = $invoiceTabJson
} | ConvertTo-Json

try {
    Invoke-RestMethod -Uri "$apiUrl/sprk_chartdefinitions" -Method Post -Headers $headers -Body $invoiceTab
    Write-Host "✅ Imported: Invoice Tab charts" -ForegroundColor Green
}
catch {
    Write-Warning "Failed to import Invoice Tab charts: $_"
}

Write-Host ""
Write-Host "Chart import complete." -ForegroundColor Cyan
```

**Tasks**:
- [ ] Create `Import-ChartDefinitions.ps1`
- [ ] Execute import script
- [ ] Verify chart definitions in Dataverse
- [ ] Update Finance Panel PCF to load charts

---

## Phase 7: Deployment & Validation

### 7.1 Pre-Deployment Checklist

**Code Validation**:
- [ ] All unit tests pass (`dotnet test`)
- [ ] All integration tests pass
- [ ] Code review completed
- [ ] ADR compliance verified (`/adr-check`)
- [ ] No compiler warnings

**Schema Validation**:
- [ ] Invoice fields added to dev environment
- [ ] Matter fields added to dev environment
- [ ] Fields visible on forms
- [ ] Customizations published

**Playbook Validation**:
- [ ] Playbook record created in Dataverse
- [ ] Tool handlers registered in DI
- [ ] Playbook execution tested with sample data

**API Validation**:
- [ ] Matter overview endpoint tested
- [ ] Invoice list endpoint tested
- [ ] Endpoints return expected data structure
- [ ] Authorization working correctly

### 7.2 Deployment Steps

#### **Step 1: Deploy BFF API to App Service**

```bash
# Build release configuration
dotnet publish src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj -c Release -o ./publish

# Deploy to Azure App Service
az webapp deploy \
  --resource-group spe-infrastructure-westus2 \
  --name spe-api-dev-67e2xz \
  --src-path ./publish \
  --type zip
```

**Tasks**:
- [ ] Build release configuration
- [ ] Deploy to dev App Service
- [ ] Verify health check (`GET /healthz`)
- [ ] Verify API endpoints accessible
- [ ] Check Application Insights for errors

#### **Step 2: Verify Dataverse Integration**

```powershell
# Test invoice extraction job
$jobPayload = @{
    invoiceId = "{test-invoice-guid}"
    documentId = "{test-document-guid}"
} | ConvertTo-Json

Invoke-RestMethod `
  -Uri "https://spe-api-dev-67e2xz.azurewebsites.net/api/finance/jobs/invoice-extraction" `
  -Method Post `
  -Body $jobPayload `
  -ContentType "application/json"
```

**Tasks**:
- [ ] Test invoice extraction job end-to-end
- [ ] Verify invoice record updated with AI summary
- [ ] Verify matter totals updated
- [ ] Verify playbook execution logged correctly

#### **Step 3: UI Testing**

**Finance Panel PCF**:
- [ ] Load Matter Overview tab
- [ ] Verify 3 metric cards display (Budget Utilized, Total Billed, Remaining Budget)
- [ ] Verify metrics update after invoice extraction
- [ ] Load Invoice tab
- [ ] Verify invoice grid displays with AI summaries
- [ ] Verify invoice count and average metrics display
- [ ] Test navigation to document record
- [ ] Test navigation to invoice detail

### 7.3 Post-Deployment Validation

**Functional Tests**:

| Test Scenario | Expected Outcome | Status |
|---------------|------------------|--------|
| Extract first invoice for matter | Matter totals initialized correctly | [ ] |
| Extract second invoice for same matter | Matter totals incremented correctly | [ ] |
| Extract invoice with $0 amount | No divide-by-zero errors | [ ] |
| Extract invoice for matter with no budget | Budget utilization = 0% | [ ] |
| Extract concurrent invoices | No race conditions, totals accurate | [ ] |
| View matter with no invoices | Metrics show $0 / 0 count | [ ] |
| View matter with 10 invoices | Grid displays all invoices | [ ] |

**Performance Tests**:

| Metric | Target | Actual | Status |
|--------|--------|--------|--------|
| Invoice extraction job (end-to-end) | < 30 seconds | _____ | [ ] |
| Matter overview API response | < 200ms | _____ | [ ] |
| Invoice list API response (10 records) | < 500ms | _____ | [ ] |
| VisualHost chart render (3 cards) | < 1 second | _____ | [ ] |

**Error Handling Tests**:

| Error Scenario | Expected Behavior | Status |
|----------------|-------------------|--------|
| Text extraction fails | Invoice marked as Failed, no matter update | [ ] |
| AI extraction fails | Invoice marked as Failed, logged with correlation ID | [ ] |
| Matter update fails | Job retries with exponential backoff | [ ] |
| Concurrency conflict | Retry successful, totals accurate | [ ] |

### 7.4 Known Limitations (MVP)

Document these limitations for post-MVP enhancement:

1. **No line-item granularity**: Line items stored as JSON, not queryable
2. **No budget bucket matching**: All invoices aggregate to matter-level total
3. **No time-series analysis**: No monthly trend tracking (MoM velocity)
4. **No spend signals**: No automated threshold alerts
5. **No AI Search integration**: Invoice indexing deferred
6. **Manual budget entry**: Budgets not auto-populated from budget records

### 7.5 Rollback Plan

**If deployment fails or critical issues found**:

1. **Code rollback**:
   ```bash
   # Revert to previous deployment slot
   az webapp deployment slot swap \
     --resource-group spe-infrastructure-westus2 \
     --name spe-api-dev-67e2xz \
     --slot staging \
     --action swap
   ```

2. **Schema rollback** (not recommended - schema changes are additive):
   - Fields can remain in Dataverse (no data loss)
   - Remove fields from forms if needed
   - Republish customizations

3. **Playbook rollback**:
   - Deactivate playbook record (`sprk_isactive = false`)
   - Revert InvoiceExtractionJobHandler to previous version

---

## Appendix: Reference Materials

### A.1 Architecture Diagrams

**Data Flow**:
```
Email → Classification → Document flagged as invoice
                              ↓
User reviews → Confirms invoice → Invoice record created
                              ↓
InvoiceExtractionJobHandler → Downloads file → Extracts text
                              ↓
Playbook: FinanceInvoiceProcessing
  ├─ InvoiceExtractionTool (AI)
  ├─ DataverseUpdateTool (invoice)
  ├─ FinancialCalculationTool (matter totals)
  └─ DataverseUpdateTool (matter)
                              ↓
Finance Panel → VisualHost → Displays metrics
```

**Component Dependencies**:
```
InvoiceExtractionJobHandler
  └─ IAppOnlyAnalysisService (playbook execution)
      └─ IPlaybookService (load "FinanceInvoiceProcessing")
          └─ IToolHandlerRegistry
              ├─ InvoiceExtractionToolHandler
              │   └─ IInvoiceAnalysisService
              │       └─ IOpenAiClient
              ├─ FinancialCalculationToolHandler
              │   └─ IDataverseService
              └─ DataverseUpdateToolHandler
                  └─ IDataverseService
```

### A.2 Key Files Modified

| File | Type | Purpose |
|------|------|---------|
| `Sprk.Bff.Api/Services/Finance/Tools/FinancialCalculationToolHandler.cs` | New | Calculate matter totals |
| `Sprk.Bff.Api/Services/Ai/Tools/DataverseUpdateToolHandler.cs` | New | Generic entity update |
| `Sprk.Bff.Api/Services/Finance/Tools/InvoiceExtractionToolHandler.cs` | New | Wrap AI extraction as tool |
| `Sprk.Bff.Api/Services/Jobs/Handlers/InvoiceExtractionJobHandler.cs` | Modified | Simplified to delegate to playbook |
| `Sprk.Bff.Api/Api/Finance/MatterOverviewEndpoints.cs` | New | Matter metrics API |
| `Sprk.Bff.Api/Api/Finance/InvoiceListEndpoints.cs` | New | Invoice list API |
| `Sprk.Bff.Api/Infrastructure/DI/FinanceModule.cs` | Modified | Register tool handlers |

### A.3 Testing Strategy

**Unit Tests** (80% coverage target):
- FinancialCalculationToolHandler logic tests
- DataverseUpdateToolHandler validation tests
- InvoiceExtractionToolHandler extraction tests
- API endpoint response tests

**Integration Tests**:
- Playbook execution end-to-end
- Job handler → playbook → tool handlers
- API endpoints with real Dataverse queries

**Manual Tests**:
- Finance Panel UI rendering
- Invoice extraction workflow
- Concurrent invoice processing
- Error scenarios

### A.4 Performance Benchmarks

**Target Performance**:
- Invoice extraction (AI call): 5-10 seconds
- Matter total calculation: < 100ms
- Dataverse updates (2 entities): < 500ms
- API response (matter overview): < 200ms
- VisualHost chart render: < 1 second

**Total end-to-end**: Invoice confirmation → Metrics updated: < 30 seconds

### A.5 Support & Troubleshooting

**Common Issues**:

| Issue | Cause | Resolution |
|-------|-------|------------|
| Matter totals not updating | Playbook execution failed | Check Application Insights for playbook errors |
| AI summary missing | InvoiceExtractionToolHandler error | Check OpenAI API quota and model deployment |
| Metrics show $0 | Matter fields not initialized | Manually set sprk_totalbudget on matter |
| Concurrent updates incorrect | Race condition | Verify concurrency retry logic in FinancialCalculationToolHandler |

**Logging & Diagnostics**:
- Application Insights query: `traces | where message contains "FinancialCalculation"`
- Correlation ID tracking: All logs include job.CorrelationId
- Telemetry activities: `InvoiceExtraction.ProcessJob`, `PlaybookExecution`

---

## Summary

This guide provides a complete roadmap for implementing the Finance Intelligence Module MVP with a simplified, playbook-driven architecture.

**Key Benefits**:
- ✅ Faster implementation (3-4 days vs. 2+ weeks)
- ✅ Simpler data model (no BillingEvents or SpendSnapshots)
- ✅ Playbook-driven flexibility (change workflow without code deployment)
- ✅ VisualHost integration (no custom PCF development needed)
- ✅ API-updated fields (works in all contexts: jobs, API, UI)

**Next Steps**:
1. Review this guide with team
2. Assign tasks from each phase
3. Begin with Phase 1 (Dataverse schema updates)
4. Progress through phases sequentially
5. Deploy and validate in dev environment
6. Iterate based on user feedback

**Questions or Issues?**
- Refer to existing documentation in `docs/` and `.claude/`
- Check ADRs for architectural guidance
- Review related task files in `projects/financial-intelligence-module-r1/tasks/`
