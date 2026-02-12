# Finance Intelligence Module R1 - Deployment Readiness Checklist

> **Created**: 2026-02-11
> **Purpose**: Track code TODOs and implementation gaps that must be resolved before production deployment
> **Status**: Phase 4 in progress - Address after Phase 4 completion, before final deployment

---

## Executive Summary

During Phases 1-3 implementation, 7 TODO placeholders were identified in the generated code. These placeholders exist because `IDataverseService` lacks methods for custom finance entities (`sprk_invoice`, `sprk_billingevent`, `sprk_spendsnapshot`, `sprk_spendsignal`).

**All code compiles and tests pass**, but the placeholders will prevent the feature from functioning in production.

**Strategy**: Complete Phase 4 (PCF controls), then address these TODOs as a focused cleanup wave before deployment.

---

## 🔴 BLOCKER Issues (Must Fix Before Deployment)

### ✅ TODO-001: BillingEvent Creation - RESOLVED (Task 049)

**Status**: ✅ **RESOLVED** - Generic `CreateAsync` method added to IDataverseService

**File**: `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/InvoiceExtractionJobHandler.cs`
**Line**: 459
**Severity**: **CRITICAL - Feature Non-Functional**

#### Current Code
```csharp
// TODO: Implement actual billing event creation when method is available
// await _dataverseService.CreateOrUpdateBillingEventAsync(fields, ct);
return Task.CompletedTask;
```

#### Impact
- **Entire analytics pipeline fails** - No BillingEvent records created
- SpendSnapshotService has no data to aggregate
- No financial metrics, gauges, or signals
- Feature is completely non-functional without this fix

#### Root Cause
`IDataverseService` lacks `UpsertBillingEventAsync()` method for creating/updating `sprk_billingevent` records via alternate key (`sprk_invoiceid` + `sprk_linesequence`).

#### Fix Required
1. Add method to `IDataverseService`:
   ```csharp
   /// <summary>
   /// Create or update a BillingEvent record using alternate key (invoiceId + lineSequence).
   /// </summary>
   Task UpsertBillingEventAsync(Dictionary<string, object?> fields, CancellationToken ct = default);
   ```

2. Implement in `DataverseServiceClientImpl` using OData upsert with alternate key:
   ```csharp
   // POST /sprk_billingevents(sprk_invoiceid=<guid>,sprk_linesequence=<int>)
   // PATCH if exists, INSERT if not
   ```

3. Replace placeholder in `InvoiceExtractionJobHandler.cs:459`:
   ```csharp
   await _dataverseService.UpsertBillingEventAsync(fields, ct);
   ```

#### Acceptance Criteria
- [ ] `IDataverseService.UpsertBillingEventAsync()` method added
- [ ] Method implemented in `DataverseServiceClientImpl`
- [ ] Placeholder code in `InvoiceExtractionJobHandler.cs:459` replaced with actual call
- [ ] Integration test verifies BillingEvent records created after extraction job
- [ ] SpendSnapshotService aggregates BillingEvents successfully

---

### ✅ TODO-002: Invoice Reviewer Corrections - RESOLVED (Task 049)

**Status**: ✅ **RESOLVED** - Generic `UpdateAsync` method added to IDataverseService

**File**: `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/InvoiceExtractionJobHandler.cs`
**Line**: 343
**Original Severity**: HIGH - Extraction Accuracy Degraded

#### Current Code
```csharp
// TODO: This needs actual implementation when sprk_invoice entity methods are added
return Task.FromResult<InvoiceRecord?>(new InvoiceRecord
{
    InvoiceId = invoiceId,
    MatterId = null, // Would be loaded from actual query
    VendorOrgId = null // Would be loaded from actual query
});
```

#### Impact
- **Reviewer corrections ignored** - Human overrides (matter, vendor) from confirm endpoint not passed to AI
- AI extraction lacks critical hints, reducing accuracy
- Defeats purpose of human-in-the-loop workflow

#### Context
When a user confirms an invoice via `/api/finance/invoice-review/confirm`, they provide:
- `matterId` - Correct matter association
- `vendorOrgId` - Correct vendor

These are stored on the `sprk_invoice` record. The extraction job handler should load these and pass them as hints to `ExtractInvoiceFactsAsync()` to improve AI accuracy.

#### Root Cause
`IDataverseService` lacks `GetInvoiceAsync()` method to read `sprk_invoice` records.

#### Fix Required
1. Add method to `IDataverseService`:
   ```csharp
   /// <summary>
   /// Get an invoice record by ID.
   /// </summary>
   Task<InvoiceEntity?> GetInvoiceAsync(Guid invoiceId, CancellationToken ct = default);
   ```

2. Create `InvoiceEntity` DTO:
   ```csharp
   public class InvoiceEntity
   {
       public Guid InvoiceId { get; set; }
       public Guid? MatterId { get; set; }
       public Guid? VendorOrgId { get; set; }
       public string? InvoiceNumber { get; set; }
       public DateTime? InvoiceDate { get; set; }
       public decimal? TotalAmount { get; set; }
       public string? Currency { get; set; }
       public int? Status { get; set; }
       public int? ExtractionStatus { get; set; }
   }
   ```

3. Implement in `DataverseServiceClientImpl` using OData:
   ```csharp
   // GET /sprk_invoices(<guid>)?$select=sprk_matterid,sprk_vendororgid,...
   ```

4. Replace placeholder in `InvoiceExtractionJobHandler.cs:343`:
   ```csharp
   var invoice = await _dataverseService.GetInvoiceAsync(invoiceId, ct);
   if (invoice == null)
       return null;

   return new InvoiceRecord
   {
       InvoiceId = invoiceId,
       MatterId = invoice.MatterId,
       VendorOrgId = invoice.VendorOrgId
   };
   ```

#### Acceptance Criteria
- [ ] `IDataverseService.GetInvoiceAsync()` method added
- [ ] `InvoiceEntity` DTO created
- [ ] Method implemented in `DataverseServiceClientImpl`
- [ ] Placeholder code in `InvoiceExtractionJobHandler.cs:343` replaced
- [ ] Integration test verifies reviewer corrections passed to AI extraction

---

## 🟠 HIGH PRIORITY Issues (Severely Degrades Functionality)

### ❌ TODO-003: Invoice Search Index Missing Metadata

**File**: `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/InvoiceIndexingJobHandler.cs`
**Line**: 252
**Severity**: **HIGH - Search Functionality Degraded**

#### Current Code
```csharp
// TODO: Implement actual Dataverse query when sprk_invoice entity methods are added
_logger.LogWarning("LoadInvoiceRecordAsync not fully implemented - using placeholder");

return new InvoiceSearchRecord
{
    InvoiceId = invoiceId,
    InvoiceNumber = null, // All metadata fields null!
    InvoiceDate = null,
    TotalAmount = null,
    Currency = null,
    MatterId = null,
    MatterNumber = null,
    MatterName = null,
    VendorOrgId = null,
    VendorName = null,
    Confidence = null,
    ReviewStatus = null
};
```

#### Impact
- **Search index severely incomplete** - Only `invoiceId` populated
- Search results show no invoice numbers, dates, amounts, matter names, vendor info
- Users cannot effectively use semantic invoice search
- Search functionality essentially broken

#### Root Cause
Same as TODO-002 - `IDataverseService` lacks `GetInvoiceAsync()` method.

Additionally needs ability to expand lookups for matter and vendor names.

#### Fix Required
1. Leverage `GetInvoiceAsync()` from TODO-002 fix
2. Enhance to support lookup expansion:
   ```csharp
   /// <summary>
   /// Get an invoice record with optional lookup expansion.
   /// </summary>
   Task<InvoiceEntity?> GetInvoiceAsync(
       Guid invoiceId,
       bool expandLookups = false,
       CancellationToken ct = default);
   ```

3. Implement OData `$expand` query:
   ```csharp
   // GET /sprk_invoices(<guid>)?$expand=sprk_Matter($select=sprk_matternumber,sprk_name),sprk_VendorOrg($select=name)
   ```

4. Replace placeholder in `InvoiceIndexingJobHandler.cs:252`:
   ```csharp
   var invoice = await _dataverseService.GetInvoiceAsync(invoiceId, expandLookups: true, ct);
   if (invoice == null)
       return null;

   return new InvoiceSearchRecord
   {
       InvoiceId = invoiceId,
       InvoiceNumber = invoice.InvoiceNumber,
       InvoiceDate = invoice.InvoiceDate,
       TotalAmount = invoice.TotalAmount,
       Currency = invoice.Currency,
       MatterId = invoice.MatterId,
       MatterNumber = invoice.MatterNumber, // From expanded lookup
       MatterName = invoice.MatterName,     // From expanded lookup
       VendorOrgId = invoice.VendorOrgId,
       VendorName = invoice.VendorName,     // From expanded lookup
       Confidence = invoice.ExtractionConfidence,
       ReviewStatus = invoice.Status
   };
   ```

#### Acceptance Criteria
- [ ] `GetInvoiceAsync()` supports lookup expansion
- [ ] `InvoiceEntity` includes expanded lookup properties (MatterNumber, MatterName, VendorName)
- [ ] Placeholder code in `InvoiceIndexingJobHandler.cs:252` replaced
- [ ] Integration test verifies AI Search index contains full invoice metadata
- [ ] Manual test: Search for invoice by vendor name, matter name, invoice number - all work

---

## 🟡 MEDIUM PRIORITY Issues (Degrades Quality)

### ❌ TODO-004: Extracted Text Not Indexed for Search

**File**: `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/InvoiceIndexingJobHandler.cs`
**Line**: 307
**Severity**: **MEDIUM - Search Quality Reduced**

#### Current Code
```csharp
ExtractedText = null, // TODO: Load from actual field when available
```

#### Impact
- **Search misses text-based queries** - Cannot search for phrases within invoice body
- Hybrid search still works via vector embeddings from structured fields
- Full-text search quality reduced

#### Root Cause
Unclear if `sprk_document` entity has `sprk_extractedtext` field, or if text extraction is stored elsewhere.

#### Investigation Required
1. Check `sprk_document` Dataverse schema - does `sprk_extractedtext` field exist?
2. Check Document Intelligence integration - where is OCR text stored?
3. Check if extraction happens during invoice processing or separately

#### Fix Options

**Option A**: If field exists on `sprk_document`
1. Update `DocumentEntity` DTO to include `ExtractedText` property
2. Update `GetDocumentAsync()` to select this field
3. Replace placeholder:
   ```csharp
   ExtractedText = document.ExtractedText,
   ```

**Option B**: If extraction happens on-demand
1. Add Document Intelligence extraction to `InvoiceIndexingJobHandler`
2. Extract text during indexing job
3. Store in search index but not Dataverse

**Option C**: Post-MVP enhancement
- Defer to future sprint - hybrid search works reasonably well without full text
- Prioritize after core functionality proven

#### Acceptance Criteria
- [ ] Investigation complete - determine source of extracted text
- [ ] If available: Update code to load and index extracted text
- [ ] If not available: Document as future enhancement, adjust search index schema if needed
- [ ] Test: Search for text phrase that appears in invoice body (e.g., "professional services rendered")

---

### ❌ TODO-005: Tenant Context Not Available

**File**: `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/InvoiceIndexingJobHandler.cs`
**Line**: 308
**Severity**: **MEDIUM - Multi-Tenant Filtering Broken**

#### Current Code
```csharp
TenantId = null, // TODO: Load from actual tenant context
```

#### Impact
- **Multi-tenant search filtering won't work** - All invoices in same index regardless of tenant
- Not critical for MVP (single-tenant dev environment)
- MUST fix before multi-tenant production deployment

#### Root Cause
Current architecture doesn't provide tenant context to service layer.

#### Investigation Required
1. Check if `sprk_document` has tenant field
2. Check if tenant info available in HttpContext or claims
3. Review multi-tenancy strategy for Spaarke

#### Fix Options

**Option A**: Tenant field on document
```csharp
var document = await _dataverseService.GetDocumentAsync(documentId, ct);
TenantId = document.TenantId;
```

**Option B**: Tenant from user claims
```csharp
// Add ITenantContextProvider service
var tenantId = _tenantContextProvider.GetCurrentTenantId();
TenantId = tenantId;
```

**Option C**: Per-tenant indexes (recommended)
- Create separate search index per tenant: `spaarke-invoices-{tenantId}`
- TenantId not needed in index - isolation at index level
- Update `InvoiceSearchService` to target correct index

#### Acceptance Criteria
- [ ] Multi-tenancy strategy documented
- [ ] If per-tenant indexes: Update deployment script to support tenant parameter
- [ ] If tenant field: Update code to load and index tenant ID
- [ ] Test: Two users from different tenants cannot see each other's invoices in search

---

### ❌ TODO-006: Project Association Not Loaded

**File**: `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/InvoiceIndexingJobHandler.cs`
**Line**: 309
**Severity**: **MEDIUM - Project-Based Search Limited**

#### Current Code
```csharp
ProjectId = null // TODO: Load if document has project association
```

#### Impact
- **Cannot filter invoices by project** - If Spaarke uses project-level billing
- May not be applicable if invoices are always matter-scoped
- Nice-to-have for project-based filtering

#### Investigation Required
1. Check Spaarke data model - are invoices associated with projects?
2. Check `sprk_document` schema - is there a `sprk_project` lookup?
3. Check business requirements - is project-level invoice search needed?

#### Fix Required (if applicable)
1. Update `DocumentEntity` to include `ProjectId`
2. Update `GetDocumentAsync()` to select project lookup
3. Replace placeholder:
   ```csharp
   ProjectId = document.ProjectId
   ```

#### Acceptance Criteria
- [ ] Investigation complete - determine if project association applicable
- [ ] If applicable: Update code to load and index project ID
- [ ] If not applicable: Remove field from search index schema
- [ ] Test: If applicable, filter invoices by project ID

---

## 🟢 LOW PRIORITY Issues (Nice-to-Have)

### ❌ TODO-007: Reviewer Audit Trail Incomplete

**File**: `src/server/api/Sprk.Bff.Api/Services/Finance/InvoiceReviewService.cs`
**Line**: 391
**Severity**: **LOW - Audit Enhancement**

#### Current Code
```csharp
// TODO: Add DocInvoiceReviewedBy when user context is available
// [DocInvoiceReviewedBy] = currentUserId
```

#### Impact
- **Audit trail missing reviewer identity** - Only has review timestamp
- Cannot answer "WHO rejected this invoice?"
- Not a functional blocker - feature still works

#### Root Cause
User context (current user ID) not available in service layer.

#### Fix Required
1. Add user context provider service:
   ```csharp
   public interface IUserContextProvider
   {
       Guid GetCurrentUserId();
       string GetCurrentUserName();
   }
   ```

2. Implement using HttpContext claims or Dataverse WhoAmI
3. Inject into `InvoiceReviewService`
4. Replace placeholder:
   ```csharp
   [DocInvoiceReviewedBy] = _userContextProvider.GetCurrentUserId()
   ```

#### Acceptance Criteria
- [ ] `IUserContextProvider` created
- [ ] Service registered in DI
- [ ] Placeholder code in `InvoiceReviewService.cs:391` replaced
- [ ] Same pattern applied to confirm endpoint (line ~220)
- [ ] Test: Reject invoice, verify `sprk_invoicereviewedby` populated

---

## Root Cause Analysis

All TODOs stem from **missing IDataverseService methods for finance entities**:

### Current State (IDataverseService)
✅ Document operations (`CreateDocumentAsync`, `GetDocumentAsync`, `UpdateDocumentFieldsAsync`)
✅ Analysis operations
✅ Email operations
✅ Metadata operations (`GetEntitySetNameAsync`, `GetLookupNavigationAsync`)

### Required for Finance Module
❌ `sprk_invoice` entity operations:
- `CreateInvoiceAsync()` - Create invoice record
- `GetInvoiceAsync()` - Read invoice with lookups expanded
- `UpdateInvoiceAsync()` - Update invoice status/fields

❌ `sprk_billingevent` entity operations:
- `UpsertBillingEventAsync()` - Create/update via alternate key

❌ `sprk_spendsnapshot` entity operations:
- `QuerySpendSnapshotsAsync()` - Query by matter/period
- `UpsertSpendSnapshotAsync()` - Create/update via 5-field alternate key

❌ `sprk_spendsignal` entity operations:
- `UpsertSpendSignalAsync()` - Create/update signals

---

## Implementation Plan

### Step 1: Extend IDataverseService Interface
**File**: `src/server/shared/Spaarke.Dataverse/IDataverseService.cs`

Add methods for finance entities:

```csharp
// ========================================
// Finance Intelligence Module Operations
// ========================================

/// <summary>
/// Create a new invoice record.
/// </summary>
Task<Guid> CreateInvoiceAsync(Dictionary<string, object?> fields, CancellationToken ct = default);

/// <summary>
/// Get an invoice record by ID with optional lookup expansion.
/// </summary>
Task<InvoiceEntity?> GetInvoiceAsync(Guid invoiceId, bool expandLookups = false, CancellationToken ct = default);

/// <summary>
/// Update invoice record fields.
/// </summary>
Task UpdateInvoiceAsync(Guid invoiceId, Dictionary<string, object?> fields, CancellationToken ct = default);

/// <summary>
/// Create or update a BillingEvent record using alternate key (invoiceId + lineSequence).
/// Implements idempotent upsert pattern.
/// </summary>
Task UpsertBillingEventAsync(Dictionary<string, object?> fields, CancellationToken ct = default);

/// <summary>
/// Query SpendSnapshot records for a matter and period.
/// </summary>
Task<IEnumerable<SpendSnapshotEntity>> QuerySpendSnapshotsAsync(
    Guid matterId,
    string? period = null,
    CancellationToken ct = default);

/// <summary>
/// Create or update a SpendSnapshot record using 5-field alternate key.
/// Alternate key: matterId + period + bucket + year + month
/// </summary>
Task UpsertSpendSnapshotAsync(Dictionary<string, object?> fields, CancellationToken ct = default);

/// <summary>
/// Create or update a SpendSignal record.
/// </summary>
Task UpsertSpendSignalAsync(Dictionary<string, object?> fields, CancellationToken ct = default);
```

### Step 2: Create Entity DTOs
**File**: `src/server/shared/Spaarke.Dataverse/Entities/InvoiceEntity.cs` (new)

```csharp
namespace Spaarke.Dataverse.Entities;

public class InvoiceEntity
{
    public Guid InvoiceId { get; set; }
    public Guid? DocumentId { get; set; }
    public Guid? MatterId { get; set; }
    public string? MatterNumber { get; set; } // From expanded lookup
    public string? MatterName { get; set; }   // From expanded lookup
    public Guid? VendorOrgId { get; set; }
    public string? VendorName { get; set; }   // From expanded lookup
    public string? InvoiceNumber { get; set; }
    public DateTime? InvoiceDate { get; set; }
    public decimal? TotalAmount { get; set; }
    public string? Currency { get; set; }
    public int? Status { get; set; }
    public int? ExtractionStatus { get; set; }
    public double? ExtractionConfidence { get; set; }
    public DateTime? CreatedOn { get; set; }
}
```

Create similar DTOs for `SpendSnapshotEntity`, `SpendSignalEntity`, `BillingEventEntity`.

### Step 3: Implement in DataverseServiceClientImpl
**File**: `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs`

Implement each method using OData API patterns:

```csharp
public async Task<InvoiceEntity?> GetInvoiceAsync(Guid invoiceId, bool expandLookups = false, CancellationToken ct = default)
{
    var entitySetName = await GetEntitySetNameAsync("sprk_invoice", ct);

    var selectFields = "sprk_invoiceid,sprk_invoicenumber,sprk_invoicedate,sprk_totalamount," +
                      "sprk_currency,sprk_status,sprk_extractionstatus,sprk_extractionconfidence";

    var expandClause = expandLookups
        ? "&$expand=sprk_Matter($select=sprk_matternumber,sprk_name),sprk_VendorOrg($select=name)"
        : "";

    var uri = $"{_baseUrl}/api/data/v9.2/{entitySetName}({invoiceId})?$select={selectFields}{expandClause}";

    // ... HTTP GET, deserialize to InvoiceEntity
}

public async Task UpsertBillingEventAsync(Dictionary<string, object?> fields, CancellationToken ct = default)
{
    var invoiceId = fields["sprk_invoiceid"]; // Lookup value
    var lineSequence = fields["sprk_linesequence"];

    // OData alternate key syntax
    var entitySetName = await GetEntitySetNameAsync("sprk_billingevent", ct);
    var uri = $"{_baseUrl}/api/data/v9.2/{entitySetName}(sprk_invoiceid={invoiceId},sprk_linesequence={lineSequence})";

    // PATCH to upsert
    var content = new StringContent(
        JsonSerializer.Serialize(fields, _jsonOptions),
        Encoding.UTF8,
        "application/json");

    var response = await _httpClient.PatchAsync(uri, content, ct);
    // ... handle response
}
```

### Step 4: Update Finance Service Code
Replace all 7 TODO placeholders with actual method calls.

### Step 5: Integration Testing
Create integration tests that verify end-to-end:
1. Confirm invoice → Creates `sprk_invoice` record
2. Extract invoice → Creates `sprk_billingevent` records
3. Snapshot generation → Creates `sprk_spendsnapshot` records
4. Signal evaluation → Creates `sprk_spendsignal` records
5. Invoice indexing → AI Search index contains full metadata

---

## Pre-Deployment Checklist

### Dataverse Schema Prerequisites
- [ ] `sprk_invoice` entity deployed with all fields
- [ ] `sprk_billingevent` entity deployed with all fields
- [ ] `sprk_spendsnapshot` entity deployed with all fields
- [ ] `sprk_spendsignal` entity deployed with all fields
- [ ] Alternate keys created:
  - [ ] `sprk_invoice`: (none - use GUID)
  - [ ] `sprk_billingevent`: `sprk_invoiceid` + `sprk_linesequence`
  - [ ] `sprk_spendsnapshot`: `sprk_matterid` + `sprk_period` + `sprk_bucket` + `sprk_year` + `sprk_month`
  - [ ] `sprk_spendsignal`: (none - use deterministic GUID from signal type + matter + date)

### Code Fixes
- [ ] TODO-001: BillingEvent creation implemented (**BLOCKER**)
- [ ] TODO-002: Invoice reviewer corrections loaded (**BLOCKER**)
- [ ] TODO-003: Invoice search index metadata populated (**HIGH**)
- [ ] TODO-004: Extracted text indexed (if available) (**MEDIUM**)
- [ ] TODO-005: Tenant context handled (**MEDIUM** - critical for multi-tenant)
- [ ] TODO-006: Project association loaded (if applicable) (**MEDIUM**)
- [ ] TODO-007: Reviewer audit trail complete (**LOW**)

### Testing
- [ ] Unit tests: All existing tests still pass
- [ ] Integration tests: End-to-end pipeline verified
- [ ] Manual test: Confirm invoice → Extract → Snapshot → Search
- [ ] Manual test: Verify financial gauge shows correct data
- [ ] Manual test: Verify signals appear when thresholds exceeded
- [ ] Manual test: Semantic invoice search returns relevant results

### Infrastructure
- [ ] Invoice search index deployed (`Deploy-InvoiceSearchIndex.ps1`)
- [ ] App Service configuration updated (if needed)
- [ ] Redis cache available for summary caching

### Documentation
- [ ] Update deployment guide with Dataverse entity deployment steps
- [ ] Document alternate key creation process
- [ ] Update README with feature status

---

## Timeline Estimate

| Activity | Estimated Hours | Dependencies |
|----------|----------------|--------------|
| Complete Phase 4 (PCF controls) | 20-25 | Task 041-044 |
| IDataverseService extension | 8-10 | None |
| Entity DTOs creation | 2-3 | None |
| DataverseServiceClientImpl methods | 10-12 | DTOs |
| Replace TODOs in handlers | 4-6 | IDataverseService methods |
| Integration testing | 6-8 | Code fixes complete |
| Manual QA testing | 4-6 | Code fixes complete |
| Documentation updates | 2-3 | Testing complete |
| **TOTAL** | **56-73 hours** | **~7-9 days** |

---

## Risk Assessment

| Risk | Probability | Impact | Mitigation |
|------|------------|--------|------------|
| OData upsert via alternate key fails | Medium | High | Test thoroughly in dev; fallback to query-then-update |
| Lookup expansion syntax incorrect | Low | Medium | Reference existing code patterns; verify with small test |
| Performance issues with expanded lookups | Low | Medium | Benchmark queries; add caching if needed |
| Tenant strategy requires architecture change | Low | High | Engage architect early if multi-tenant gaps found |
| Extracted text not available | Medium | Low | Feature degrades gracefully; defer to post-MVP |

---

## Success Criteria

**Before marking deployment-ready, ALL of the following must be true:**

1. ✅ All 7 TODOs resolved (BLOCKER and HIGH priority mandatory, others assessed)
2. ✅ `dotnet build` succeeds with 0 errors, 0 warnings
3. ✅ `dotnet test` passes with 0 failures
4. ✅ Integration test: Full invoice pipeline executes end-to-end
5. ✅ Manual test: PCF control displays correct financial gauge data
6. ✅ Manual test: Semantic search returns invoices with full metadata
7. ✅ Manual test: Signals appear when budget thresholds exceeded
8. ✅ Code review: No placeholder code in production paths
9. ✅ Security review: No PII logged, no content leakage
10. ✅ Performance review: P95 latency < 2s for summary endpoint

---

## Appendix: Files Requiring Changes

### 🔴 BLOCKER Files
- `src/server/shared/Spaarke.Dataverse/IDataverseService.cs` - Add methods
- `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs` - Implement methods
- `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/InvoiceExtractionJobHandler.cs` - Lines 343, 459

### 🟠 HIGH Priority Files
- `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/InvoiceIndexingJobHandler.cs` - Line 252

### 🟡 MEDIUM Priority Files
- `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/InvoiceIndexingJobHandler.cs` - Lines 307, 308, 309

### 🟢 LOW Priority Files
- `src/server/api/Sprk.Bff.Api/Services/Finance/InvoiceReviewService.cs` - Line 391

### New Files to Create
- `src/server/shared/Spaarke.Dataverse/Entities/InvoiceEntity.cs`
- `src/server/shared/Spaarke.Dataverse/Entities/BillingEventEntity.cs`
- `src/server/shared/Spaarke.Dataverse/Entities/SpendSnapshotEntity.cs`
- `src/server/shared/Spaarke.Dataverse/Entities/SpendSignalEntity.cs`
- `tests/integration/FinanceModule.IntegrationTests/InvoicePipelineTests.cs`

---

## Contact & Questions

For questions about this checklist or implementation guidance:
- Review `.claude/constraints/dataverse.md` for OData patterns
- Review `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs` existing methods for reference
- Check Dataverse schema documentation in `docs/data-model/`

**Last Updated**: 2026-02-11
**Next Review**: After Phase 4 completion
