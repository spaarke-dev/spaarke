# Output Orchestrator Design - Finance Intelligence Module R1 MVP

> **Created**: 2026-02-11
> **Purpose**: Design document for playbook-driven Dataverse updates via generic OutputOrchestrator
> **Status**: Design Complete - Ready for Implementation
> **Related**: Phase 4-7 Implementation Plan

---

## Executive Summary

**Problem**: Playbook `outputMapping` is defined but not executed. Job handlers have hardcoded orchestration logic. Business analysts cannot change behavior without code deployment.

**Solution**: Build generic `IOutputOrchestratorService` that reads `outputMapping` from playbook and applies updates to Dataverse. Remove TL-010 tool handler (no longer needed).

**Impact**:
- ✅ Business analysts can configure field mappings via Playbook Builder
- ✅ Generic solution works for ALL playbooks
- ✅ Cleaner architecture aligned with playbook-driven vision
- ✅ No code deployment needed to change field mappings

---

## Current State Analysis

### Playbook Definition (playbooks.json)

```json
// PB-013 Finance Invoice Processing
{
  "scopes": {
    "tools": ["TL-009", "TL-010", "TL-011"]
  },
  "workflow": {
    "steps": [
      "1. Extract invoice header (TL-009)",      // ⚠️ Descriptive text only!
      "2. Update Invoice record (TL-010)",       // ⚠️ Not executed by engine!
      "3. Calculate matter totals (TL-011)",
      "4. Update Matter record (TL-010)"
    ]
  },
  "outputMapping": {
    "aiSummary": "sprk_invoice.sprk_aisummary",        // ⚠️ Defined but ignored!
    "extractedJson": "sprk_invoice.sprk_extractedjson",
    "matterTotalSpend": "sprk_matter.sprk_totalspendtodate"
  }
}
```

### Current Execution (InvoiceExtractionJobHandler.cs)

```csharp
public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
{
    // 1. Extract (calls service directly - IGNORES playbook workflow!)
    var result = await _invoiceAnalysisService.ExtractInvoiceFactsAsync(...);

    // 2. Update invoice (HARDCODED - ignores playbook outputMapping!)
    await _dataverseService.UpdateDocumentFieldsAsync(invoiceId, new Dictionary<string, object?>
    {
        ["sprk_aisummary"] = aiSummary,
        ["sprk_extractedjson"] = extractedJson,
        ["sprk_totalamount"] = new Money(totalAmount)
    }, ct);

    // 3. Update matter (HARDCODED again!)
    await _dataverseService.UpdateDocumentFieldsAsync(matterId, ...);
}
```

### The Problems

| Issue | Impact |
|-------|--------|
| Playbook workflow is documentation, not execution | BA cannot change workflow order |
| outputMapping is ignored | BA cannot change field mappings |
| Job handler has hardcoded orchestration | Code deployment needed for changes |
| TL-010 tool exists but adds no value | Extra layer of indirection |

---

## Architecture Vision: Playbook-Driven Execution

### The Goal

Business analyst uses Playbook Builder UI to configure:
1. ✅ Select tools from catalog (TL-009, TL-011)
2. ✅ Define workflow execution order
3. ✅ Map tool outputs to Dataverse fields
4. ✅ Configure type conversions (Money, EntityReference)
5. ✅ Set concurrency mode (optimistic for matter totals)

**No code deployment needed** to change behavior.

### Execution Flow

```
Job Handler (Minimal)
    ↓
PlaybookOrchestrationService
    ↓
    ├─> Tool Handlers (TL-009, TL-011)
    │   ├─> InvoiceExtractionToolHandler → { aiSummary, extractedJson, totalAmount }
    │   └─> FinancialCalculationToolHandler → { totalSpend, invoiceCount }
    ↓
OutputOrchestratorService (NEW)
    ↓
    Reads playbook.outputMapping
    Resolves variable references (${extraction.aiSummary})
    Applies field mappings
    ↓
DataverseUpdateHandler (NEW)
    ↓
    Type conversions (Money, EntityReference)
    Optimistic concurrency handling
    Retry logic
    ↓
IDataverseService.UpdateRecordFieldsAsync()
```

---

## Enhanced Playbook Schema

### outputMapping Structure

```json
{
  "id": "PB-013",
  "scopes": {
    "tools": ["TL-009", "TL-011"]  // TL-010 removed!
  },

  "workflow": {
    "steps": [
      {
        "order": 1,
        "tool": "TL-009",
        "outputVariable": "extraction"
      },
      {
        "order": 2,
        "tool": "TL-011",
        "inputVariables": {
          "invoiceAmount": "${extraction.totalAmount}",
          "matterId": "${context.matterId}"
        },
        "outputVariable": "calculation"
      }
    ]
  },

  "outputMapping": {
    "updates": [
      {
        "entityType": "sprk_invoice",
        "recordIdSource": "${context.invoiceId}",
        "fields": {
          "sprk_aisummary": "${extraction.aiSummary}",
          "sprk_extractedjson": "${extraction.extractedJson}",
          "sprk_totalamount": {
            "type": "Money",
            "value": "${extraction.totalAmount}",
            "currency": "${extraction.currency}"
          },
          "sprk_invoicenumber": "${extraction.invoiceNumber}",
          "sprk_invoicedate": "${extraction.invoiceDate}",
          "sprk_extractionstatus": 100000001
        }
      },
      {
        "entityType": "sprk_matter",
        "recordIdSource": "${context.matterId}",
        "fields": {
          "sprk_totalspendtodate": {
            "type": "Money",
            "value": "${calculation.totalSpend}"
          },
          "sprk_invoicecount": "${calculation.invoiceCount}",
          "sprk_remainingbudget": {
            "type": "Money",
            "value": "${calculation.remainingBudget}"
          }
        },
        "concurrencyMode": "optimistic",
        "retryOnConflict": true,
        "maxRetries": 3
      }
    ]
  }
}
```

### Variable Resolution Rules

| Pattern | Example | Resolution |
|---------|---------|------------|
| `${context.*}` | `${context.invoiceId}` | From job payload / execution context |
| `${extraction.*}` | `${extraction.aiSummary}` | From TL-009 tool output |
| `${calculation.*}` | `${calculation.totalSpend}` | From TL-011 tool output |
| Constant | `100000001` | Used as-is |

### Type Conversion Specs

| Type | JSON Format | C# Conversion |
|------|-------------|---------------|
| **String** | `"value"` | Direct assignment |
| **Money** | `{ "type": "Money", "value": "${var}", "currency": "USD" }` | `new Money(decimal)` |
| **EntityReference** | `{ "type": "EntityReference", "entityType": "sprk_matter", "id": "${var}" }` | `new EntityReference(type, guid)` |
| **DateTime** | `"${var}"` (ISO format) | `DateTime.Parse(value)` |
| **Int** | `123` or `"${var}"` | `int.Parse(value)` |
| **Decimal** | `123.45` or `"${var}"` | `decimal.Parse(value)` |

---

## Component Design

### 1. IOutputOrchestratorService (NEW)

**File**: `src/server/api/Sprk.Bff.Api/Services/Ai/IOutputOrchestratorService.cs`

```csharp
namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Orchestrates Dataverse updates based on playbook outputMapping configuration.
/// Reads outputMapping from playbook, resolves variable references, and applies updates.
/// </summary>
public interface IOutputOrchestratorService
{
    /// <summary>
    /// Apply outputMapping from playbook to Dataverse entities.
    /// </summary>
    /// <param name="playbookId">Playbook containing outputMapping</param>
    /// <param name="context">Execution context with tool output variables</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Result containing success/failure and updated record IDs</returns>
    Task<OutputMappingResult> ApplyOutputMappingAsync(
        Guid playbookId,
        PlaybookExecutionContext context,
        CancellationToken ct);
}

/// <summary>
/// Execution context for playbook runs. Stores variables from job payload and tool outputs.
/// </summary>
public class PlaybookExecutionContext
{
    /// <summary>
    /// Variable storage. Keys use dot notation (e.g., "context.invoiceId", "extraction.aiSummary").
    /// </summary>
    public Dictionary<string, object?> Variables { get; init; } = new();

    /// <summary>
    /// Get variable value with type conversion.
    /// </summary>
    public T? GetVariable<T>(string key)
    {
        if (Variables.TryGetValue(key, out var value))
        {
            return (T?)Convert.ChangeType(value, typeof(T));
        }
        return default;
    }

    /// <summary>
    /// Set variable value.
    /// </summary>
    public void SetVariable(string key, object? value)
    {
        Variables[key] = value;
    }
}

/// <summary>
/// Result from applying output mappings.
/// </summary>
public record OutputMappingResult
{
    public bool Success { get; init; }
    public List<EntityUpdateResult> Updates { get; init; } = new();
    public string? ErrorMessage { get; init; }

    public static OutputMappingResult SuccessResult(List<EntityUpdateResult> updates) =>
        new() { Success = true, Updates = updates };

    public static OutputMappingResult FailureResult(string error) =>
        new() { Success = false, ErrorMessage = error };
}

/// <summary>
/// Result from updating a single entity.
/// </summary>
public record EntityUpdateResult
{
    public string EntityType { get; init; } = null!;
    public Guid RecordId { get; init; }
    public bool Success { get; init; }
    public int FieldsUpdated { get; init; }
    public string? ErrorMessage { get; init; }
}
```

**File**: `src/server/api/Sprk.Bff.Api/Services/Ai/OutputOrchestratorService.cs`

```csharp
namespace Sprk.Bff.Api.Services.Ai;

public class OutputOrchestratorService : IOutputOrchestratorService
{
    private readonly IPlaybookService _playbookService;
    private readonly IDataverseUpdateHandler _dataverseUpdateHandler;
    private readonly ILogger<OutputOrchestratorService> _logger;

    public OutputOrchestratorService(
        IPlaybookService playbookService,
        IDataverseUpdateHandler dataverseUpdateHandler,
        ILogger<OutputOrchestratorService> logger)
    {
        _playbookService = playbookService;
        _dataverseUpdateHandler = dataverseUpdateHandler;
        _logger = logger;
    }

    public async Task<OutputMappingResult> ApplyOutputMappingAsync(
        Guid playbookId,
        PlaybookExecutionContext context,
        CancellationToken ct)
    {
        try
        {
            // 1. Load playbook
            var playbook = await _playbookService.GetByIdAsync(playbookId, ct);
            if (playbook == null)
            {
                return OutputMappingResult.FailureResult($"Playbook {playbookId} not found");
            }

            // 2. Parse outputMapping from playbook configuration
            var outputMapping = ParseOutputMapping(playbook);
            if (outputMapping == null || outputMapping.Updates.Count == 0)
            {
                _logger.LogWarning("Playbook {PlaybookId} has no outputMapping defined", playbookId);
                return OutputMappingResult.SuccessResult(new List<EntityUpdateResult>());
            }

            // 3. Apply each entity update
            var results = new List<EntityUpdateResult>();
            foreach (var update in outputMapping.Updates)
            {
                var result = await ApplyEntityUpdateAsync(update, context, ct);
                results.Add(result);
            }

            // 4. Check if all succeeded
            var allSucceeded = results.All(r => r.Success);
            return allSucceeded
                ? OutputMappingResult.SuccessResult(results)
                : OutputMappingResult.FailureResult("Some updates failed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply output mapping for playbook {PlaybookId}", playbookId);
            return OutputMappingResult.FailureResult(ex.Message);
        }
    }

    private async Task<EntityUpdateResult> ApplyEntityUpdateAsync(
        EntityUpdateConfig update,
        PlaybookExecutionContext context,
        CancellationToken ct)
    {
        try
        {
            // 1. Resolve recordId
            var recordIdStr = ResolveVariable(update.RecordIdSource, context);
            if (!Guid.TryParse(recordIdStr, out var recordId))
            {
                return new EntityUpdateResult
                {
                    EntityType = update.EntityType,
                    Success = false,
                    ErrorMessage = $"Invalid recordId: {recordIdStr}"
                };
            }

            // 2. Build field dictionary with resolved values
            var fields = new Dictionary<string, object?>();
            foreach (var fieldMapping in update.Fields)
            {
                var value = ResolveFieldValue(fieldMapping.Value, context);
                fields[fieldMapping.Key] = value;
            }

            // 3. Delegate to DataverseUpdateHandler
            await _dataverseUpdateHandler.UpdateAsync(
                update.EntityType,
                recordId,
                fields,
                update.ConcurrencyMode ?? ConcurrencyMode.None,
                update.MaxRetries ?? 3,
                ct);

            _logger.LogInformation(
                "Updated {EntityType} {RecordId} with {FieldCount} fields",
                update.EntityType, recordId, fields.Count);

            return new EntityUpdateResult
            {
                EntityType = update.EntityType,
                RecordId = recordId,
                Success = true,
                FieldsUpdated = fields.Count
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update {EntityType}", update.EntityType);
            return new EntityUpdateResult
            {
                EntityType = update.EntityType,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private string? ResolveVariable(string expression, PlaybookExecutionContext context)
    {
        if (!expression.StartsWith("${") || !expression.EndsWith("}"))
        {
            return expression; // Constant value
        }

        var varName = expression[2..^1]; // Remove ${ and }
        return context.GetVariable<string>(varName);
    }

    private object? ResolveFieldValue(object valueSpec, PlaybookExecutionContext context)
    {
        // Handle simple string value (variable reference or constant)
        if (valueSpec is string strValue)
        {
            return ResolveVariable(strValue, context);
        }

        // Handle complex type specification (Money, EntityReference, etc.)
        if (valueSpec is JsonElement jsonElement)
        {
            return ResolveComplexValue(jsonElement, context);
        }

        return valueSpec;
    }

    private object? ResolveComplexValue(JsonElement jsonElement, PlaybookExecutionContext context)
    {
        if (jsonElement.TryGetProperty("type", out var typeProperty))
        {
            var type = typeProperty.GetString();
            return type?.ToLowerInvariant() switch
            {
                "money" => ResolveMoney(jsonElement, context),
                "entityreference" => ResolveEntityReference(jsonElement, context),
                "datetime" => ResolveDateTime(jsonElement, context),
                _ => jsonElement.GetRawText()
            };
        }

        return jsonElement.GetRawText();
    }

    private Money ResolveMoney(JsonElement jsonElement, PlaybookExecutionContext context)
    {
        var valueStr = jsonElement.GetProperty("value").GetString() ?? "0";
        var resolvedValue = ResolveVariable(valueStr, context);
        var amount = decimal.Parse(resolvedValue ?? "0");
        return new Money(amount);
    }

    private EntityReference ResolveEntityReference(JsonElement jsonElement, PlaybookExecutionContext context)
    {
        var entityType = jsonElement.GetProperty("entityType").GetString() ?? "";
        var idStr = jsonElement.GetProperty("id").GetString() ?? "";
        var resolvedId = ResolveVariable(idStr, context);
        var id = Guid.Parse(resolvedId ?? Guid.Empty.ToString());
        return new EntityReference(entityType, id);
    }

    private DateTime ResolveDateTime(JsonElement jsonElement, PlaybookExecutionContext context)
    {
        var valueStr = jsonElement.GetProperty("value").GetString() ?? "";
        var resolvedValue = ResolveVariable(valueStr, context);
        return DateTime.Parse(resolvedValue ?? DateTime.UtcNow.ToString("o"));
    }

    private OutputMappingConfig? ParseOutputMapping(PlaybookDto playbook)
    {
        // Parse from playbook.ConfigJson or a dedicated outputMapping field
        // For MVP, assume it's in a dedicated field
        // Production: Parse from JSON configuration
        return null; // TODO: Implement parsing
    }
}

// Configuration models
public record OutputMappingConfig
{
    public List<EntityUpdateConfig> Updates { get; init; } = new();
}

public record EntityUpdateConfig
{
    public string EntityType { get; init; } = null!;
    public string RecordIdSource { get; init; } = null!;
    public Dictionary<string, object> Fields { get; init; } = new();
    public ConcurrencyMode? ConcurrencyMode { get; init; }
    public bool RetryOnConflict { get; init; }
    public int? MaxRetries { get; init; }
}

public enum ConcurrencyMode
{
    None,
    Optimistic
}
```

### 2. IDataverseUpdateHandler (NEW)

**File**: `src/server/api/Sprk.Bff.Api/Services/Dataverse/IDataverseUpdateHandler.cs`

```csharp
namespace Sprk.Bff.Api.Services.Dataverse;

/// <summary>
/// Handles Dataverse entity updates with type conversion, optimistic concurrency, and retry logic.
/// NOT a tool handler - this is a code component called by OutputOrchestrator.
/// </summary>
public interface IDataverseUpdateHandler
{
    /// <summary>
    /// Update a Dataverse entity with field values.
    /// </summary>
    /// <param name="entityLogicalName">Entity logical name (e.g., "sprk_invoice")</param>
    /// <param name="recordId">Record ID to update</param>
    /// <param name="fields">Field name → value dictionary</param>
    /// <param name="concurrencyMode">Concurrency mode (None or Optimistic)</param>
    /// <param name="maxRetries">Max retries for optimistic concurrency conflicts</param>
    /// <param name="ct">Cancellation token</param>
    Task UpdateAsync(
        string entityLogicalName,
        Guid recordId,
        Dictionary<string, object?> fields,
        ConcurrencyMode concurrencyMode,
        int maxRetries,
        CancellationToken ct);
}
```

**File**: `src/server/api/Sprk.Bff.Api/Services/Dataverse/DataverseUpdateHandler.cs`

```csharp
namespace Sprk.Bff.Api.Services.Dataverse;

public class DataverseUpdateHandler : IDataverseUpdateHandler
{
    private readonly IDataverseService _dataverseService;
    private readonly ILogger<DataverseUpdateHandler> _logger;

    public DataverseUpdateHandler(
        IDataverseService dataverseService,
        ILogger<DataverseUpdateHandler> logger)
    {
        _dataverseService = dataverseService;
        _logger = logger;
    }

    public async Task UpdateAsync(
        string entityLogicalName,
        Guid recordId,
        Dictionary<string, object?> fields,
        ConcurrencyMode concurrencyMode,
        int maxRetries,
        CancellationToken ct)
    {
        if (concurrencyMode == ConcurrencyMode.Optimistic)
        {
            await UpdateWithOptimisticConcurrencyAsync(
                entityLogicalName, recordId, fields, maxRetries, ct);
        }
        else
        {
            await _dataverseService.UpdateRecordFieldsAsync(
                entityLogicalName, recordId, fields, ct);
        }
    }

    private async Task UpdateWithOptimisticConcurrencyAsync(
        string entityLogicalName,
        Guid recordId,
        Dictionary<string, object?> fields,
        int maxRetries,
        CancellationToken ct)
    {
        var attempt = 0;
        while (attempt < maxRetries)
        {
            try
            {
                // 1. Read current record to get row version
                var currentRecord = await _dataverseService.RetrieveAsync(
                    entityLogicalName, recordId, new[] { "versionnumber" }, ct);

                var currentVersion = currentRecord.GetAttributeValue<long>("versionnumber");

                // 2. Add row version to update request
                var fieldsWithVersion = new Dictionary<string, object?>(fields)
                {
                    ["versionnumber"] = currentVersion
                };

                // 3. Update with version check
                await _dataverseService.UpdateRecordFieldsAsync(
                    entityLogicalName, recordId, fieldsWithVersion, ct);

                _logger.LogInformation(
                    "Updated {EntityType} {RecordId} with optimistic concurrency (version {Version})",
                    entityLogicalName, recordId, currentVersion);

                return; // Success
            }
            catch (Exception ex) when (IsConcurrencyException(ex))
            {
                attempt++;
                if (attempt >= maxRetries)
                {
                    _logger.LogError(
                        "Optimistic concurrency failed after {MaxRetries} attempts for {EntityType} {RecordId}",
                        maxRetries, entityLogicalName, recordId);
                    throw;
                }

                _logger.LogWarning(
                    "Concurrency conflict on {EntityType} {RecordId}, retrying (attempt {Attempt}/{MaxRetries})",
                    entityLogicalName, recordId, attempt, maxRetries);

                // Exponential backoff
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Pow(2, attempt) * 100), ct);
            }
        }
    }

    private static bool IsConcurrencyException(Exception ex)
    {
        // Check for Dataverse concurrency error codes
        return ex.Message.Contains("0x80060882") || // Concurrency violation
               ex.Message.Contains("ConcurrencyVersionMismatch");
    }
}
```

---

## Implementation Tasks

### Phase 1: Core Infrastructure (2-3 hours)

- [ ] **Task 1.1**: Create `IOutputOrchestratorService.cs` interface
  - File: `src/server/api/Sprk.Bff.Api/Services/Ai/IOutputOrchestratorService.cs`
  - Define interface with `ApplyOutputMappingAsync` method
  - Define `PlaybookExecutionContext`, `OutputMappingResult`, `EntityUpdateResult` models

- [ ] **Task 1.2**: Create `OutputOrchestratorService.cs` implementation
  - File: `src/server/api/Sprk.Bff.Api/Services/Ai/OutputOrchestratorService.cs`
  - Implement variable resolution (`${context.invoiceId}` → value)
  - Implement type conversions (Money, EntityReference, DateTime)
  - Implement field mapping application

- [ ] **Task 1.3**: Create `IDataverseUpdateHandler.cs` interface
  - File: `src/server/api/Sprk.Bff.Api/Services/Dataverse/IDataverseUpdateHandler.cs`
  - Define interface with `UpdateAsync` method

- [ ] **Task 1.4**: Create `DataverseUpdateHandler.cs` implementation
  - File: `src/server/api/Sprk.Bff.Api/Services/Dataverse/DataverseUpdateHandler.cs`
  - Implement optimistic concurrency with retry logic
  - Implement exponential backoff

- [ ] **Task 1.5**: Register services in DI
  - File: `src/server/api/Sprk.Bff.Api/Infrastructure/DI/FinanceModule.cs`
  - Add `services.AddScoped<IOutputOrchestratorService, OutputOrchestratorService>()`
  - Add `services.AddScoped<IDataverseUpdateHandler, DataverseUpdateHandler>()`

### Phase 2: Update Playbook Schema (1 hour)

- [ ] **Task 2.1**: Update `playbooks.json` with enhanced outputMapping
  - File: `scripts/seed-data/playbooks.json`
  - Add `updates` array to PB-013 outputMapping
  - Define invoice update config
  - Define matter update config with optimistic concurrency
  - Remove TL-010 from scopes.tools

- [ ] **Task 2.2**: Remove TL-010 from tools.json
  - File: `scripts/seed-data/tools.json`
  - Delete TL-010 (DataverseUpdateToolHandler) entry
  - Update deployment notes

### Phase 3: Update Job Handler (1-2 hours)

- [ ] **Task 3.1**: Simplify `InvoiceExtractionJobHandler.cs`
  - File: `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/InvoiceExtractionJobHandler.cs`
  - Remove manual Dataverse update code (lines 219-264: BillingEvent creation, matter updates)
  - Build `PlaybookExecutionContext` with variables
  - Call `IOutputOrchestratorService.ApplyOutputMappingAsync()`
  - Keep InvoiceIndexing job enqueueing (lines 266-267)

- [ ] **Task 3.2**: Update method signature to inject OutputOrchestrator
  - Add `IOutputOrchestratorService _outputOrchestrator` constructor parameter
  - Store in field

### Phase 4: IDataverseService Extensions (1 hour)

- [ ] **Task 4.1**: Add `RetrieveAsync` method to IDataverseService
  - File: `src/server/shared/Spaarke.Dataverse/IDataverseService.cs`
  - Add method: `Task<Entity> RetrieveAsync(string entityLogicalName, Guid id, string[] columns, CancellationToken ct)`

- [ ] **Task 4.2**: Implement `RetrieveAsync` in DataverseServiceClientImpl
  - File: `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs`
  - Use `serviceClient.Retrieve(entityName, id, new ColumnSet(columns))`

- [ ] **Task 4.3**: Update `UpdateRecordFieldsAsync` to support entity types
  - Current method signature: `UpdateDocumentFieldsAsync(string documentId, ...)`
  - Add generic method: `UpdateRecordFieldsAsync(string entityLogicalName, Guid recordId, ...)`

### Phase 5: Testing (2-3 hours)

- [ ] **Task 5.1**: Unit tests for OutputOrchestratorService
  - File: `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/OutputOrchestratorServiceTests.cs`
  - Test variable resolution
  - Test type conversions (Money, EntityReference)
  - Test field mapping application

- [ ] **Task 5.2**: Unit tests for DataverseUpdateHandler
  - File: `tests/unit/Sprk.Bff.Api.Tests/Services/Dataverse/DataverseUpdateHandlerTests.cs`
  - Test optimistic concurrency with retry
  - Test exponential backoff
  - Test concurrency exception detection

- [ ] **Task 5.3**: Integration test for end-to-end flow
  - File: `tests/integration/Sprk.Bff.Api.Tests/Services/Jobs/InvoiceExtractionJobHandlerTests.cs`
  - Test full job execution
  - Verify invoice record updated
  - Verify matter record updated with optimistic concurrency

### Phase 6: Deployment (1 hour)

- [ ] **Task 6.1**: Redeploy playbooks.json with updated PB-013
  - Run: `scripts/seed-data/Deploy-Playbooks.ps1`
  - Verify PB-013 no longer references TL-010

- [ ] **Task 6.2**: Remove TL-010 from Dataverse
  - Delete sprk_analysistool record for TL-010
  - Verify no playbook dependencies remain

---

## Success Criteria

- [ ] Business analyst can change field mappings in playbooks.json without code deployment
- [ ] OutputOrchestratorService reads outputMapping and applies updates
- [ ] Invoice record updated with AI summary, extracted JSON, total amount
- [ ] Matter record updated with optimistic concurrency (total spend, invoice count)
- [ ] TL-010 removed from codebase and Dataverse
- [ ] All unit tests pass
- [ ] Integration test demonstrates end-to-end flow

---

## Rollback Plan

If implementation fails or takes too long:
1. Revert to Phase 4-7 plan with TL-010 tool handler
2. Keep InvoiceExtractionJobHandler with hardcoded updates
3. Build OutputOrchestrator post-MVP as refactoring task

---

## Post-MVP Enhancements

- [ ] **Visual Playbook Builder**: UI for configuring outputMapping
- [ ] **Validation**: Schema validation for outputMapping config
- [ ] **Error Handling**: Partial success handling (some updates succeed, others fail)
- [ ] **Audit Trail**: Log all field changes for compliance
- [ ] **Bulk Updates**: Batch multiple records in single transaction
- [ ] **Formula Support**: `"${extraction.totalAmount} * 0.1"` for calculated fields
- [ ] **Conditional Updates**: Update field only if condition met

---

## Related Files

| File | Purpose |
|------|---------|
| `playbooks.json` | Enhanced outputMapping definition |
| `tools.json` | Remove TL-010 entry |
| `InvoiceExtractionJobHandler.cs` | Simplified to use OutputOrchestrator |
| `IDataverseService.cs` | Add RetrieveAsync for version check |
| `DataverseServiceClientImpl.cs` | Implement RetrieveAsync |
| `FinanceModule.cs` | Register OutputOrchestrator and DataverseUpdateHandler |

---

*Design complete. Ready for implementation.*
