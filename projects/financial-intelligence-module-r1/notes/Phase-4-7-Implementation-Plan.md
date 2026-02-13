# Phases 4-7: Implementation Plan (Remaining Work)

**Date**: February 11, 2026
**Status**: 📋 PLANNED (Phase 3 complete, Phase 4-7 pending)

---

## Phases Completed

✅ **Phase 1**: Dataverse schema fields created
✅ **Phase 2**: Tool handlers implemented (InvoiceExtractionToolHandler, DataverseUpdateToolHandler, FinancialCalculationToolHandler)
✅ **Phase 3**: Playbook PB-013 deployed to Dataverse with tool associations

---

## Phase 4: Job Handler Updates

### 4.1 Simplify InvoiceExtractionJobHandler for MVP

**File**: `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/InvoiceExtractionJobHandler.cs`

**Current Implementation**:
- Lines 219-244: Creates BillingEvent records (one per line item)
- Lines 254-264: Enqueues SpendSnapshotGeneration job
- Lines 266-267: Enqueues InvoiceIndexing job

**MVP Changes Required**:

```csharp
// REMOVE: BillingEvent creation (lines 219-244)
// REMOVE: SpendSnapshot job enqueueing (lines 254-264)
// KEEP: InvoiceIndexing job enqueueing (lines 266-267) - still needed for AI Search

// ADD: Update invoice record with AI summary and JSON
// ADD: Update matter record with financial totals
```

**Implementation Approach**:

```csharp
// After AI extraction completes (line 217), instead of creating BillingEvents:

// 1. Serialize extraction result to JSON
var extractedJson = JsonSerializer.Serialize(aiExtractionResult, new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
});

// 2. Generate AI summary (reuse InvoiceExtractionToolHandler.GenerateAiSummary logic)
var aiSummary = GenerateAiSummary(aiExtractionResult);

// 3. Update invoice record
await UpdateInvoiceWithExtractionAsync(
    invoiceId,
    aiSummary,
    extractedJson,
    aiExtractionResult.Header.TotalAmount,
    aiExtractionResult.Header.InvoiceNumber,
    aiExtractionResult.Header.InvoiceDate,
    ct);

// 4. Update matter totals (if matter is linked)
if (invoice.MatterId.HasValue)
{
    await UpdateMatterFinancialsAsync(
        invoice.MatterId.Value,
        aiExtractionResult.Header.TotalAmount,
        ct);
}

// 5. Enqueue InvoiceIndexing job (keep existing code)
await EnqueueInvoiceIndexingJobAsync(invoiceId, documentId, job.CorrelationId, ct);
```

**New Methods to Add**:

```csharp
/// <summary>
/// Update invoice record with AI extraction results.
/// Updates: sprk_aisummary, sprk_extractedjson, sprk_totalamount, sprk_invoicenumber, sprk_invoicedate
/// </summary>
private async Task UpdateInvoiceWithExtractionAsync(
    Guid invoiceId,
    string aiSummary,
    string extractedJson,
    decimal totalAmount,
    string? invoiceNumber,
    string? invoiceDate,
    CancellationToken ct)
{
    var fields = new Dictionary<string, object?>
    {
        ["sprk_aisummary"] = aiSummary.Length > 5000 ? aiSummary.Substring(0, 5000) : aiSummary,
        ["sprk_extractedjson"] = extractedJson.Length > 20000 ? extractedJson.Substring(0, 20000) : extractedJson,
        ["sprk_totalamount"] = new Money(totalAmount), // Microsoft.Xrm.Sdk.Money
        ["sprk_invoicenumber"] = invoiceNumber,
        ["sprk_invoicedate"] = invoiceDate, // YYYY-MM-DD string
        ["sprk_extractionstatus"] = ExtractionStatusExtracted
    };

    // TODO: Need IDataverseService.UpdateRecordFieldsAsync for sprk_invoice entity
    // Current UpdateDocumentFieldsAsync is specific to sprk_document
    await _dataverseService.UpdateRecordFieldsAsync("sprk_invoice", invoiceId, fields, ct);
}

/// <summary>
/// Update matter record with new invoice totals.
/// Recalculates: sprk_totalspendtodate, sprk_invoicecount, sprk_averageinvoiceamount, sprk_remainingbudget
/// Uses optimistic concurrency with row version check.
/// </summary>
private async Task UpdateMatterFinancialsAsync(
    Guid matterId,
    decimal newInvoiceAmount,
    CancellationToken ct)
{
    const int MaxRetries = 3;
    var attempt = 0;

    while (attempt < MaxRetries)
    {
        attempt++;

        try
        {
            // Load current matter record
            var matterFields = await _dataverseService.RetrieveRecordFieldsAsync(
                "sprk_matter",
                matterId,
                new[] { "sprk_totalspendtodate", "sprk_invoicecount", "sprk_totalbudget", "versionnumber" },
                ct);

            var currentSpend = (decimal?)matterFields.GetValueOrDefault("sprk_totalspendtodate") ?? 0m;
            var currentInvoiceCount = (int?)matterFields.GetValueOrDefault("sprk_invoicecount") ?? 0;
            var totalBudget = (decimal?)matterFields.GetValueOrDefault("sprk_totalbudget") ?? 0m;
            var currentVersion = (long?)matterFields.GetValueOrDefault("versionnumber") ?? 0;

            // Calculate new totals
            var newTotalSpend = currentSpend + newInvoiceAmount;
            var newInvoiceCount = currentInvoiceCount + 1;
            var newAverageInvoiceAmount = newTotalSpend / newInvoiceCount;
            var newRemainingBudget = totalBudget - newTotalSpend;
            var newBudgetUtilizationPercent = totalBudget > 0 ? (newTotalSpend / totalBudget) * 100 : 0;

            // Update with optimistic concurrency check
            var updateFields = new Dictionary<string, object?>
            {
                ["sprk_totalspendtodate"] = new Money(newTotalSpend),
                ["sprk_invoicecount"] = newInvoiceCount,
                ["sprk_averageinvoiceamount"] = new Money(newAverageInvoiceAmount),
                ["sprk_remainingbudget"] = new Money(newRemainingBudget),
                ["sprk_budgetutilizationpercent"] = (double)newBudgetUtilizationPercent,
                ["versionnumber"] = currentVersion // Optimistic concurrency check
            };

            await _dataverseService.UpdateRecordFieldsAsync("sprk_matter", matterId, updateFields, ct);

            _logger.LogInformation(
                "Updated matter {MatterId} financials: TotalSpend={TotalSpend}, InvoiceCount={InvoiceCount}",
                matterId, newTotalSpend, newInvoiceCount);

            return; // Success
        }
        catch (Exception ex) when (IsConcurrencyException(ex) && attempt < MaxRetries)
        {
            _logger.LogWarning(
                "Concurrency conflict updating matter {MatterId} (attempt {Attempt}/{MaxRetries}). Retrying...",
                matterId, attempt, MaxRetries);

            // Exponential backoff
            await Task.Delay(TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt)), ct);
        }
    }

    // If we get here, all retries failed
    _logger.LogError(
        "Failed to update matter {MatterId} financials after {MaxRetries} attempts due to concurrency conflicts",
        matterId, MaxRetries);

    // Don't throw - invoice extraction succeeded, matter update can be fixed manually
}

private static bool IsConcurrencyException(Exception ex)
{
    return ex.Message.Contains("concurrency", StringComparison.OrdinalIgnoreCase) ||
           ex.Message.Contains("row version", StringComparison.OrdinalIgnoreCase) ||
           ex.Message.Contains("optimistic", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Generate AI summary from extraction result (copy from InvoiceExtractionToolHandler).
/// </summary>
private static string GenerateAiSummary(ExtractionResult extractionResult)
{
    // ... same implementation as InvoiceExtractionToolHandler.GenerateAiSummary ...
}
```

**Tasks**:
- [ ] Remove BillingEvent creation code (lines 219-244)
- [ ] Remove SpendSnapshot job enqueueing (lines 254-264)
- [ ] Add UpdateInvoiceWithExtractionAsync method
- [ ] Add UpdateMatterFinancialsAsync method with optimistic concurrency
- [ ] Add GenerateAiSummary helper method
- [ ] Update IDataverseService to add UpdateRecordFieldsAsync for custom entities
- [ ] Write unit tests for new methods

### 4.2 AttachmentClassificationJobHandler

**No changes needed for MVP** - Already using `IInvoiceAnalysisService.ClassifyAttachmentAsync()` which is playbook-driven.

---

## Phase 5: API Endpoints

### 5.1 Matter Overview Endpoint

**File**: `src/server/api/Sprk.Bff.Api/Api/Finance/MatterOverviewEndpoints.cs` (NEW)

**Purpose**: Get matter financial overview for UI visualization

**Endpoint**: `GET /api/finance/matters/{matterId}/overview`

**Response Model**:

```csharp
public record MatterOverviewResponse
{
    public required string MatterId { get; init; }
    public required string MatterName { get; init; }
    public required string MatterNumber { get; init; }

    public decimal TotalBudget { get; init; }
    public decimal TotalSpendToDate { get; init; }
    public decimal RemainingBudget { get; init; }
    public double BudgetUtilizationPercent { get; init; }

    public int InvoiceCount { get; init; }
    public decimal AverageInvoiceAmount { get; init; }

    public required string Currency { get; init; } // Default: "USD"
}
```

**Implementation**:

```csharp
public static class MatterOverviewEndpoints
{
    public static IEndpointRouteBuilder MapMatterOverviewEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/finance/matters")
            .WithTags("Finance")
            .AddEndpointFilter<FinanceAuthorizationFilter>();

        group.MapGet("/{matterId:guid}/overview", GetMatterOverviewAsync)
            .WithName("GetMatterOverview")
            .WithDescription("Get financial overview for a matter");

        return app;
    }

    private static async Task<IResult> GetMatterOverviewAsync(
        Guid matterId,
        [FromServices] IDataverseService dataverseService,
        CancellationToken ct)
    {
        // Load matter fields
        var matterFields = await dataverseService.RetrieveRecordFieldsAsync(
            "sprk_matter",
            matterId,
            new[]
            {
                "sprk_name",
                "sprk_matternumber",
                "sprk_totalbudget",
                "sprk_totalspendtodate",
                "sprk_remainingbudget",
                "sprk_budgetutilizationpercent",
                "sprk_invoicecount",
                "sprk_averageinvoiceamount"
            },
            ct);

        if (matterFields == null)
        {
            return Results.NotFound(new { error = "Matter not found" });
        }

        var response = new MatterOverviewResponse
        {
            MatterId = matterId.ToString(),
            MatterName = (string)matterFields["sprk_name"],
            MatterNumber = (string)matterFields["sprk_matternumber"],
            TotalBudget = GetMoneyValue(matterFields, "sprk_totalbudget"),
            TotalSpendToDate = GetMoneyValue(matterFields, "sprk_totalspendtodate"),
            RemainingBudget = GetMoneyValue(matterFields, "sprk_remainingbudget"),
            BudgetUtilizationPercent = (double)(matterFields.GetValueOrDefault("sprk_budgetutilizationpercent") ?? 0.0),
            InvoiceCount = (int)(matterFields.GetValueOrDefault("sprk_invoicecount") ?? 0),
            AverageInvoiceAmount = GetMoneyValue(matterFields, "sprk_averageinvoiceamount"),
            Currency = "USD"
        };

        return Results.Ok(response);
    }

    private static decimal GetMoneyValue(Dictionary<string, object?> fields, string fieldName)
    {
        var value = fields.GetValueOrDefault(fieldName);
        return value is Money money ? money.Value : 0m;
    }
}
```

**Tasks**:
- [ ] Create MatterOverviewEndpoints.cs
- [ ] Register endpoints in Program.cs
- [ ] Write integration tests

### 5.2 Invoice List Endpoint

**File**: `src/server/api/Sprk.Bff.Api/Api/Finance/InvoiceListEndpoints.cs` (NEW)

**Purpose**: Get list of invoices for a matter

**Endpoint**: `GET /api/finance/matters/{matterId}/invoices`

**Query Parameters**:
- `pageSize` (default: 50)
- `pageNumber` (default: 1)
- `sortBy` (default: "invoiceDate")
- `sortOrder` (default: "desc")

**Response Model**:

```csharp
public record InvoiceListResponse
{
    public required List<InvoiceSummary> Invoices { get; init; }
    public int TotalCount { get; init; }
    public int PageNumber { get; init; }
    public int PageSize { get; init; }
}

public record InvoiceSummary
{
    public required string InvoiceId { get; init; }
    public required string InvoiceNumber { get; init; }
    public required string InvoiceDate { get; init; } // YYYY-MM-DD
    public required string VendorName { get; init; }
    public decimal TotalAmount { get; init; }
    public required string Currency { get; init; }
    public required string AiSummary { get; init; } // Truncated to 200 chars
}
```

**Implementation**: Similar pattern to MatterOverviewEndpoints with FetchXML query for pagination.

**Tasks**:
- [ ] Create InvoiceListEndpoints.cs
- [ ] Implement FetchXML query for pagination
- [ ] Register endpoints in Program.cs
- [ ] Write integration tests

### 5.3 Finance Authorization Filter

**File**: `src/server/api/Sprk.Bff.Api/Api/Filters/FinanceAuthorizationFilter.cs` (ALREADY EXISTS)

**Tasks**:
- [x] Filter already created in Phase 2
- [ ] Write unit tests
- [ ] Apply to finance endpoints

---

## Phase 6: VisualHost Charts

### 6.1 Matter Overview Charts

**File**: `infrastructure/visualhost/matter-overview-charts.json` (NEW)

**Charts Needed**:
1. Budget vs Spend (Bar chart)
2. Budget Utilization (Gauge chart)
3. Invoice Count (KPI card)
4. Average Invoice Amount (KPI card)

**Example Chart Definition**:

```json
{
  "charts": [
    {
      "chartId": "matter-budget-vs-spend",
      "chartType": "bar",
      "title": "Budget vs Spend",
      "dataSource": "api",
      "apiEndpoint": "/api/finance/matters/{matterId}/overview",
      "series": [
        {
          "name": "Budget",
          "field": "totalBudget",
          "color": "#0078D4"
        },
        {
          "name": "Spent",
          "field": "totalSpendToDate",
          "color": "#107C10"
        },
        {
          "name": "Remaining",
          "field": "remainingBudget",
          "color": "#D13438"
        }
      ]
    }
  ]
}
```

**Tasks**:
- [ ] Create matter-overview-charts.json
- [ ] Deploy charts to Dataverse (sprk_chartdefinition entity)
- [ ] Test in model-driven app

### 6.2 Invoice Tab Charts

**File**: `infrastructure/visualhost/invoice-tab-charts.json` (NEW)

**Charts Needed**:
1. Invoice list (Data grid)
2. Total Invoiced (KPI card)
3. Invoice trend over time (Line chart)

**Tasks**:
- [ ] Create invoice-tab-charts.json
- [ ] Deploy charts to Dataverse
- [ ] Test in model-driven app

---

## Phase 7: Deployment & Validation

### 7.1 Code Deployment

**Tasks**:
- [ ] Build solution (`dotnet build`)
- [ ] Run tests (`dotnet test`)
- [ ] Deploy BFF API to Azure App Service
- [ ] Verify health check (`GET /healthz`)

### 7.2 Integration Testing

**Test Scenarios**:

1. **End-to-End Invoice Processing**:
   - Upload invoice PDF to matter
   - Verify AttachmentClassificationJobHandler classifies as InvoiceCandidate
   - Approve invoice in review form
   - Verify InvoiceExtractionJobHandler extracts and updates invoice + matter
   - Verify matter totals updated correctly

2. **Matter Overview API**:
   - Call `GET /api/finance/matters/{matterId}/overview`
   - Verify response matches expected values

3. **Invoice List API**:
   - Call `GET /api/finance/matters/{matterId}/invoices`
   - Verify pagination works
   - Verify invoices sorted by date

4. **VisualHost Charts**:
   - Open matter form in model-driven app
   - Verify Budget vs Spend chart renders
   - Open Invoice tab
   - Verify invoice list loads

**Tasks**:
- [ ] Write integration test suite
- [ ] Run tests against dev environment
- [ ] Document test results

### 7.3 Known Limitations (MVP)

Document these limitations for R2:

1. **No BillingEvents** - Invoice line items stored as JSON, not individual records
2. **No SpendSnapshots** - No historical spend tracking by month
3. **Simplified Matter Totals** - No breakdown by cost type (Fee vs Expense)
4. **No Multi-Currency Support** - All amounts assumed USD
5. **No Invoice Approval Workflow** - Simple review/reject only
6. **FinancialCalculationToolHandler Stubbed** - Calculations done in job handler, not via playbook

**Tasks**:
- [ ] Document limitations in README.md
- [ ] Create R2 feature list
- [ ] Update project wrap-up notes

---

## Summary of Remaining Work

| Phase | Status | Estimated Effort |
|-------|--------|------------------|
| Phase 4: Job Handler Updates | 🔄 In Progress | 4-6 hours |
| Phase 5: API Endpoints | ⏳ Pending | 3-4 hours |
| Phase 6: VisualHost Charts | ⏳ Pending | 2-3 hours |
| Phase 7: Deployment & Testing | ⏳ Pending | 2-3 hours |

**Total Remaining**: ~11-16 hours of development work

---

## Next Steps

1. Continue with Phase 4: Update `InvoiceExtractionJobHandler.cs`
2. Add `UpdateRecordFieldsAsync` to IDataverseService for custom entities
3. Implement matter financials update with optimistic concurrency
4. Test updated job handler with sample invoice
5. Proceed to Phase 5 (API endpoints)

