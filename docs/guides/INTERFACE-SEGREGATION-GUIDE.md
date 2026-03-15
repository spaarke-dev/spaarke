# Interface Segregation Guide

> **Last Updated**: March 2026
> **Source**: Code Quality and Assurance R2 project
> **Applies To**: Shared .NET libraries, service interfaces

---

## When to Segregate

An interface is a candidate for segregation when:

| Indicator | Example |
|-----------|---------|
| > 20 methods in one interface | `IDataverseService` had 63 methods |
| Consumers use < 30% of the interface | A service needing only document ops injects all 63 methods |
| Method groups serve distinct domains | Document CRUD, analysis records, event management are separate concerns |
| Constructor parameter lists growing | Services inject the fat interface even when they need one method |

---

## Strategy: Composite Interface Pattern

The R2 project established a zero-breaking-change approach using composite interfaces.

### Step 1: Identify Domain Groups

Map each interface method to a domain:

```
IDataverseService (63 methods) → 9 domains:
  1. Document CRUD + profiles        → IDocumentDataverseService
  2. Analysis records + scope        → IAnalysisDataverseService
  3. Generic entity CRUD + search    → IGenericEntityService
  4. Job lifecycle                   → IProcessingJobService
  5. Event + todo operations         → IEventDataverseService
  6. Field mapping configuration     → IFieldMappingDataverseService
  7. KPI metrics + scoring           → IKpiDataverseService
  8. Email + communication records   → ICommunicationDataverseService
  9. Health check operations         → IDataverseHealthService
```

### Step 2: Create Focused Interfaces

```csharp
// Each interface contains only methods for its domain
public interface IDocumentDataverseService
{
    Task<DocumentProfile> GetDocumentProfileAsync(Guid documentId);
    Task<Guid> CreateDocumentAsync(DocumentCreateModel model);
    Task UpdateDocumentAsync(Guid id, DocumentUpdateModel model);
    // ... document-specific methods only
}

public interface IAnalysisDataverseService
{
    Task<AnalysisRecord> GetAnalysisAsync(Guid analysisId);
    Task<ScopeResolution> ResolveScopeAsync(Guid entityId, ScopeType type);
    // ... analysis-specific methods only
}
```

### Step 3: Create Composite Interface

The original interface becomes a composite that inherits all focused interfaces:

```csharp
// Zero breaking changes — existing consumers continue to work
public interface IDataverseService :
    IDocumentDataverseService,
    IAnalysisDataverseService,
    IGenericEntityService,
    IProcessingJobService,
    IEventDataverseService,
    IFieldMappingDataverseService,
    IKpiDataverseService,
    ICommunicationDataverseService,
    IDataverseHealthService
{
    // No additional methods — composite only
}
```

### Step 4: Update Implementations

Both implementations implement the composite (which satisfies all focused interfaces):

```csharp
// Implementation doesn't change — it already implements all methods
public class DataverseServiceClientImpl : IDataverseService { /* unchanged */ }
public class DataverseWebApiService : IDataverseService { /* unchanged */ }
```

### Step 5: Migrate Consumers (Gradual)

Update consumers to inject the narrowest applicable interface:

```csharp
// Before: Fat interface injection
public class DocumentUploadService(IDataverseService dataverse) { }

// After: Narrow interface injection
public class DocumentUploadService(IDocumentDataverseService dataverse) { }
```

**This migration is optional and gradual.** Consumers using `IDataverseService` continue to work because it inherits all focused interfaces. Migrate as you touch each consumer.

### Step 6: Register DI

Register the implementation once; it satisfies all interface requests:

```csharp
services.AddScoped<DataverseServiceClientImpl>();
services.AddScoped<IDataverseService>(sp => sp.GetRequiredService<DataverseServiceClientImpl>());
services.AddScoped<IDocumentDataverseService>(sp => sp.GetRequiredService<DataverseServiceClientImpl>());
services.AddScoped<IAnalysisDataverseService>(sp => sp.GetRequiredService<DataverseServiceClientImpl>());
// ... one line per focused interface
```

---

## R2 Results

| Metric | Before | After |
|--------|--------|-------|
| Interface count | 1 (63 methods) | 9 focused + 1 composite |
| Average methods per interface | 63 | 7 |
| Consumer injection surface | Always 63 methods | Only methods needed |
| Breaking changes | — | Zero |
| dotnet test regressions | — | Zero |

---

## Related

- [Service Decomposition Guide](SERVICE-DECOMPOSITION-GUIDE.md) — Companion guide for service decomposition
- [ADR-010: DI Minimalism](../../.claude/adr/ADR-010.md) — DI registration patterns
- [Service Registration Pattern](../../.claude/patterns/api/service-registration.md) — Feature module patterns
