# Service Decomposition Guide

> **Last Updated**: 2026-04-05
> **Source**: Code Quality and Assurance R2 project
> **Applies To**: Backend .NET services in Sprk.Bff.Api

---

## When to Decompose

A service is a candidate for decomposition when it exhibits **two or more** of these indicators:

| Indicator | Threshold | Why It Matters |
|-----------|-----------|----------------|
| Line count | > 800 lines | Merge-conflict magnet, hard to navigate |
| Constructor dependencies | > 12 parameters | Violates ADR-010, indicates too many responsibilities |
| Distinct responsibility groups | > 4 groups | Single Responsibility Principle violation |
| Test file complexity | Tests require > 10 mocks | Indicates tight coupling |

**Important**: Line count alone is insufficient. A 1,000-line service with one responsibility (e.g., complex orchestration) is better than three 300-line services with blurred boundaries. Measure **responsibility count**, not just lines.

---

## Strategy: Zero-Breaking-Change Decomposition

The R2 project established a pattern for decomposing services without breaking existing consumers or tests.

### Step 1: Identify Responsibility Groups

Map each public method to a responsibility group:

```
OfficeService.cs (2,907 lines) → 6 responsibility groups:
  1. Email enrichment (Graph + MimeKit)     → Extract
  2. Document persistence (Dataverse CRUD)   → Extract
  3. Job queuing (Service Bus)               → Extract
  4. Storage upload (SPE)                    → Extract
  5. Search + share + recent + streaming     → Keep (orchestrator core)
  6. SimulateJobProgressAsync                → Delete (dead code)
```

### Step 2: Extract Focused Services

Create a new service for each extracted responsibility group:

```csharp
// Before: OfficeService handled everything
public class OfficeService
{
    // 20+ constructor dependencies
    // Email, persistence, queuing, upload, search, share...
}

// After: Focused services with single responsibility
public class OfficeEmailEnricher      { /* Graph + MimeKit only */ }
public class OfficeDocumentPersistence { /* Dataverse CRUD only */ }
public class OfficeJobQueue           { /* Service Bus only */ }
public class OfficeStorageUploader    { /* SPE upload only */ }

// OfficeService becomes thin orchestrator injecting the 4 focused services
public class OfficeService(
    OfficeEmailEnricher emailEnricher,
    OfficeDocumentPersistence persistence,
    OfficeJobQueue jobQueue,
    OfficeStorageUploader uploader,
    // ... remaining deps for orchestrator-level work
) { }
```

### Step 3: Register in Feature Module

Follow ADR-010 — register new services in the existing feature module:

```csharp
// In OfficeModule.cs
public static IServiceCollection AddOfficeServices(this IServiceCollection services)
{
    services.AddScoped<OfficeEmailEnricher>();
    services.AddScoped<OfficeDocumentPersistence>();
    services.AddScoped<OfficeJobQueue>();
    services.AddScoped<OfficeStorageUploader>();
    services.AddScoped<OfficeService>();  // Now a thin orchestrator
    return services;
}
```

### Step 4: Update Tests

Inventory all test files that construct the decomposed service directly. Constructor signature changes break these tests.

**Checklist**:
- Search for `new {ServiceName}(` in test files
- Update constructor calls with new parameter list
- Add mocks for newly injected focused services
- Verify all tests pass

---

## R2 Decomposition Results

### OfficeService.cs

| Extracted Service | Responsibility | Registration |
|-------------------|---------------|--------------|
| OfficeEmailEnricher | Graph email fetch + MimeKit EML construction | Scoped |
| OfficeDocumentPersistence | Dataverse CRUD for documents + jobs | Scoped |
| OfficeJobQueue | Service Bus queuing | Scoped |
| OfficeStorageUploader | SPE upload operations | Scoped |

**Result**: 2,907 → 1,951 lines (33% reduction). Remaining code is orchestrator-level work.

### AnalysisOrchestrationService.cs

| Extracted Service | Responsibility | Registration |
|-------------------|---------------|--------------|
| AnalysisDocumentLoader | Text extraction, document reload, caching | Scoped |
| AnalysisRagProcessor | RAG search, cache key computation, tenant resolution | Scoped |
| AnalysisResultPersistence | Output storage, RAG indexing, working doc finalization | Scoped |

**Result**: Constructor dependencies reduced from 21 → 10 (52% reduction).

---

## Setting Realistic Targets

For future decomposition work, use this formula:

1. Count distinct responsibilities (method groups) remaining after extraction
2. Estimate 50-100 lines per orchestration responsibility
3. **Target = (remaining responsibilities x 75 lines) + overhead**
4. Do not set arbitrary targets (e.g., "< 500 lines") without analyzing the code first

The "irreducible core" is the orchestration logic that legitimately belongs in a coordinating service. Measure the right thing: **number of responsibilities**, not raw line count.

---

## Related

- [Interface Segregation Guide](INTERFACE-SEGREGATION-GUIDE.md) — Companion guide for interface decomposition
- [ADR-010: DI Minimalism](../../.claude/adr/ADR-010.md) — ≤15 non-framework DI registrations per module
- [Service Registration Pattern](../../.claude/patterns/api/service-registration.md) — Feature module and DI patterns
