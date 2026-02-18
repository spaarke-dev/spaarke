# Finance Intelligence Schema Corrections

> **Created**: 2026-02-12
> **Purpose**: Document actual vs. assumed schema names for deployment and code corrections

---

## Summary

During deployment preparation, discrepancies were found between assumed field names in documentation and actual Dataverse schema. This document provides the authoritative mapping for corrections.

**Primary Issue**: Lookup field naming convention was incorrectly assumed.

---

## Entity Names

| Documentation Assumption | Actual Schema | Status | Action Required |
|--------------------------|---------------|--------|-----------------|
| `sprk_budgetplan` | `sprk_budget` | ❌ INCORRECT | Update all references to `sprk_budget` |
| `sprk_budgetbucket` | `sprk_budgetbucket` | ✅ CORRECT | None |
| `sprk_invoice` | `sprk_invoice` | ✅ CORRECT | None |
| `sprk_billingevent` | `sprk_billingevent` | ✅ CORRECT | None |
| `sprk_spendsnapshot` | `sprk_spendsnapshot` | ✅ CORRECT | None |
| `sprk_spendsignal` | `sprk_spendsignal` | ✅ CORRECT | None |

---

## Lookup Field Naming Convention

**CRITICAL RULE**: Dataverse lookup fields do NOT include "id" suffix in the logical name.

### Correct Pattern
- **Lookup field logical name**: `sprk_matter` (no "id")
- **Primary key logical name**: `sprk_matterid` (has "Id" suffix, type: Uniqueidentifier)
- **Retrieved GUID value**: Accessed as `entity["sprk_matter"]` (returns EntityReference)

### Common Mistakes

| ❌ Incorrect (Assumed) | ✅ Correct (Actual) | Entity | Field Type |
|-------------------------|---------------------|--------|------------|
| `sprk_matterid` | `sprk_matter` | All entities | Lookup to `sprk_matter` |
| `sprk_projectid` | `sprk_project` | All entities | Lookup to `sprk_project` |
| `sprk_invoiceid` | `sprk_invoice` | BillingEvent, Document | Lookup to `sprk_invoice` |
| `sprk_documentid` | `sprk_document` | Invoice | Lookup to `sprk_document` |
| `sprk_budgetplanid` | `sprk_budget` | BudgetBucket | Lookup to `sprk_budget` |
| `sprk_vendorid` | `sprk_vendororg` | BillingEvent | Lookup to `sprk_organization` |

---

## Entity-by-Entity Field Reference

### 1. Invoice (sprk_invoice)

**Correct Field Names**:

| Field Logical Name | Type | Description |
|--------------------|------|-------------|
| `sprk_invoiceid` | Uniqueidentifier | Primary key (GUID) |
| `sprk_name` | Text | Primary name field |
| `sprk_document` | Lookup → sprk_document | Source attachment |
| `sprk_matter` | Lookup → sprk_matter | Parent matter |
| `sprk_project` | Lookup → sprk_project | Parent project |
| `sprk_invoicenumber` | Text | Invoice number |
| `sprk_invoicedate` | DateTime | Invoice date |
| `sprk_totalamount` | Currency | Invoice total |
| `sprk_confidence` | Decimal | Classification confidence |
| `sprk_invoicestatus` | Choice | ToReview \| Reviewed |
| `sprk_extractionstatus` | Choice | NotRun \| Extracted \| Failed |
| `sprk_correlationid` | Text | Job chain traceability |
| `sprk_currency` | Text | ISO 4217 currency code |
| `sprk_regardingrecordtype` | Lookup → sprk_recordtype_ref | Whether parent is Matter or Project |

**No alternate keys defined for Invoice.**

---

### 2. Billing Event (sprk_billingevent)

**Correct Field Names**:

| Field Logical Name | Type | Description |
|--------------------|------|-------------|
| `sprk_billingeventid` | Uniqueidentifier | Primary key (GUID) |
| `sprk_name` | Text | Primary name field |
| `sprk_invoice` | Lookup → sprk_invoice | Parent invoice |
| `sprk_matter` | Lookup → sprk_matter | Parent matter (denormalized) |
| `sprk_project` | Lookup → sprk_project | Parent project (denormalized) |
| `sprk_vendororg` | Lookup → sprk_organization | Source vendor (denormalized) |
| `sprk_linesequence` | Integer | 1-based line position |
| `sprk_description` | Multiline Text | Line item narrative |
| `sprk_amount` | Currency | Line amount |
| `sprk_quantity` | Decimal | Quantity |
| `sprk_rate` | Currency | Rate |
| `sprk_timekeeper` | Text | Lawyer/staff name |
| `sprk_timekeeperrole` | Choice | SeniorPartner \| Partner \| SeniorAssociate \| Associate \| Paralegal \| Specialist \| Other |
| `sprk_roleclass` | Text | Timekeeper role if extractable |
| `sprk_eventdate` | DateTime | Line date or invoice date fallback |
| `sprk_visibilitystate` | Choice | Invoiced \| InternalWIP \| PreBill \| Paid \| WrittenOff \| Approved |
| `sprk_costtype` | Choice | Fee \| Expense |
| `sprk_currency` | Text | ISO 4217 currency code |
| `sprk_correlationid` | Text | Job chain traceability |

**Alternate Key**: `sprk_invoice` + `sprk_linesequence` (idempotency for re-extraction)

---

### 3. Budget (sprk_budget)

**IMPORTANT**: Entity is named `sprk_budget`, NOT `sprk_budgetplan`.

**Correct Field Names**:

| Field Logical Name | Type | Description |
|--------------------|------|-------------|
| `sprk_budgetid` | Uniqueidentifier | Primary key (GUID) |
| `sprk_name` | Text | Primary name field |
| `sprk_matter` | Lookup → sprk_matter | Parent matter |
| `sprk_project` | Lookup → sprk_project | Parent project |
| `sprk_budgetyear` | Text | Budget year (4 chars) |
| `sprk_budgetstartdate` | DateTime | Budget start date |
| `sprk_budgetenddate` | DateTime | Budget end date |
| `sprk_totalbudget` | Currency | Overall budget amount |
| `sprk_budgetstatus` | Choice | Draft \| Pending \| Open \| Completed \| Closed \| OnHold \| Cancelled \| Archived |
| `sprk_budgetperiod` | Choice | Annual \| Quarter1 \| Quarter2 \| Quarter3 \| Quarter4 |
| `sprk_budgetcategory` | Choice | BudgetCategory0-4 |
| `sprk_currency` | Text | ISO 4217 currency code |

**No alternate keys defined for Budget.**

---

### 4. Budget Bucket (sprk_budgetbucket)

**Correct Field Names**:

| Field Logical Name | Type | Description |
|--------------------|------|-------------|
| `sprk_budgetbucketid` | Uniqueidentifier | Primary key (GUID) |
| `sprk_name` | Text | Primary name field |
| `sprk_budget` | Lookup → sprk_budget | Parent budget plan |
| `sprk_amount` | Currency | Budget allocation for this bucket |
| `sprk_bucketkey` | Text | TOTAL for MVP (max 100 chars) |
| `sprk_budgetcategory` | Choice | BudgetCategory0-4 |
| `sprk_periodstart` | DateTime | Bucket period start (null for lifetime) |
| `sprk_periodend` | DateTime | Bucket period end (null for lifetime) |

**No alternate keys defined for Budget Bucket.**

---

### 5. Spend Snapshot (sprk_spendsnapshot)

**Correct Field Names**:

| Field Logical Name | Type | Description |
|--------------------|------|-------------|
| `sprk_spendsnapshotid` | Uniqueidentifier | Primary key (GUID) |
| `sprk_name` | Text | Primary name field |
| `sprk_matter` | Lookup → sprk_matter | Parent matter |
| `sprk_project` | Lookup → sprk_project | Parent project |
| `sprk_snapshotperiod` | Choice | Month \| ToDate |
| `sprk_periodvalue` | Text | Period value (e.g., "2026-02" for month) |
| `sprk_generatedat` | DateTime | Snapshot computation timestamp |
| `sprk_invoicedamount` | Currency | Sum of BillingEvents (VisibilityState = Invoiced) |
| `sprk_allocatedamount` | Currency | From BudgetBucket |
| `sprk_budgetamount` | Currency | From matching BudgetBucket |
| `sprk_budgetvariance` | Currency | Budget - Invoiced |
| `sprk_budgetvariancepct` | Decimal | Variance / Budget * 100 |
| `sprk_momvelocity` | Decimal | Month-over-month growth rate |
| `sprk_bucketkey` | Text | TOTAL for MVP |
| `sprk_correlationid` | Text | Triggering job chain traceability |

**Alternate Key**: `sprk_matter` + `sprk_project` + `sprk_snapshotperiod` + `sprk_periodvalue` + `sprk_generatedat` (idempotency for re-generation)

**Note**: Alternate key includes ALL 5 fields (matter and project can be null but still part of key).

---

### 6. Spend Signal (sprk_spendsignal)

**Correct Field Names**:

| Field Logical Name | Type | Description |
|--------------------|------|-------------|
| `sprk_spendsignalid` | Uniqueidentifier | Primary key (GUID) |
| `sprk_name` | Text | Primary name field |
| `sprk_matter` | Lookup → sprk_matter | Parent matter |
| `sprk_project` | Lookup → sprk_project | Parent project |
| `sprk_snapshot` | Lookup → sprk_spendsnapshot | Source snapshot that triggered this signal |
| `sprk_signaltype` | Choice | BudgetExceeded \| BudgetWarning \| VelocitySpike \| AnomalyDetected |
| `sprk_severity` | Choice | Info \| Warning \| Critical |
| `sprk_message` | Text | Human-readable signal description (max 500 chars) |
| `sprk_generatedat` | DateTime | Signal detection timestamp |
| `sprk_isactive` | Boolean | Active until resolved/superseded |
| `sprk_spendsignalstatus` | Choice | Active \| Acknowledged \| Resolved \| AutoResolved |
| `sprk_resolutionnotes` | Multiline Text | Resolution notes (max 5000 chars) |

**No alternate keys defined for Spend Signal.**

---

### 7. Document (sprk_document) - Extended Fields

**New Classification Fields**:

| Field Logical Name | Type | Description |
|--------------------|------|-------------|
| `sprk_classification` | Choice | InvoiceCandidate \| NotInvoice \| Unknown |
| `sprk_classificationconfidence` | Decimal | AI confidence score (0..1) |
| `sprk_classificationdate` | DateTime | Classification timestamp |
| `sprk_classificationsource` | Text | Playbook name (max 100 chars) |
| `sprk_invoice` | Lookup → sprk_invoice | Link to confirmed invoice |

**Invoice Hint Fields** (extracted during classification):

| Field Logical Name | Type | Description |
|--------------------|------|-------------|
| `sprk_invoicenumberhint` | Text | Extracted invoice number |
| `sprk_invoicedatehint` | DateTime | Extracted invoice date |
| `sprk_invoiceamounthint` | Currency | Extracted total amount |
| `sprk_invoicevendornamehint` | Text | Extracted vendor name |

**Review Workflow Fields**:

| Field Logical Name | Type | Description |
|--------------------|------|-------------|
| `sprk_reviewstatus` | Choice | PendingReview \| Reviewed \| Skipped |
| `sprk_reviewedby` | Lookup → systemuser | User who reviewed |
| `sprk_revieweddate` | DateTime | Review timestamp |
| `sprk_reviewnotes` | Multiline Text | User notes from review |

---

## Code Correction Checklist

### Files Requiring Updates

Based on grep search, these files may contain incorrect field names:

1. ✅ `src/server/api/Sprk.Bff.Api/Services/Finance/SpendSnapshotService.cs`
2. ⚠️ `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/InvoiceIndexingJobHandler.cs`
3. ⚠️ `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/InvoiceExtractionJobHandler.cs`
4. ⚠️ `src/server/api/Sprk.Bff.Api/Services/RecordMatching/DataverseIndexSyncService.cs`
5. ⚠️ `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/BulkRagIndexingJobHandler.cs`
6. ⚠️ `src/server/api/Sprk.Bff.Api/Services/Email/EmailAssociationService.cs`

### Search and Replace Pattern

**Find**: `sprk_(matter|project|invoice|document|budget)id` (when used as lookup field)
**Replace**: `sprk_$1` (remove "id" suffix)

**EXCEPTION**: Primary key fields (Uniqueidentifier type) KEEP the "id" suffix:
- `sprk_invoiceid` (primary key of Invoice) - ✅ KEEP
- `sprk_matterid` (primary key of Matter) - ✅ KEEP
- BUT: `sprk_matter` (lookup to Matter from Invoice) - ❌ NO "id"

### Verification Query

After corrections, search for potential issues:

```powershell
# Search for lookup field usage with "id" suffix (likely incorrect)
grep -r "sprk_.*id.*EntityReference" src/server/api/

# Search for BudgetPlan references (should be Budget)
grep -r "BudgetPlan\|budgetplan" src/server/api/
```

---

## Documentation Updates Required

1. **docs/architecture/finance-intelligence-architecture.md**
   - Update entity names (BudgetPlan → Budget)
   - Update all lookup field names (remove "id" suffix)

2. **docs/guides/finance-intelligence-user-guide.md**
   - Update entity references
   - Update field names in examples

3. **projects/financial-intelligence-module-r1/notes/** (all guides)
   - Extraction prompt tuning guide
   - Integration test guide
   - Verification results

4. **Deployment checklist** (this conversation)
   - Correct entity and field names

---

## Deployment Validation

After deploying schema and updating code, validate with these queries:

```csharp
// Test Invoice lookup fields
var invoice = await dataverseService.RetrieveAsync("sprk_invoice", invoiceId,
    new[] { "sprk_matter", "sprk_project", "sprk_document" });

// Test BillingEvent alternate key
var billingEvent = new Entity("sprk_billingevent");
billingEvent["sprk_invoice"] = new EntityReference("sprk_invoice", invoiceGuid);
billingEvent["sprk_linesequence"] = 1;
// Should upsert via alternate key

// Test SpendSnapshot alternate key
var snapshot = new Entity("sprk_spendsnapshot");
snapshot["sprk_matter"] = new EntityReference("sprk_matter", matterGuid);
snapshot["sprk_project"] = null;
snapshot["sprk_snapshotperiod"] = new OptionSetValue(100000000); // Month
snapshot["sprk_periodvalue"] = "2026-02";
snapshot["sprk_generatedat"] = DateTime.UtcNow;
// Should upsert via alternate key
```

---

## Summary of Corrections Needed

| Component | Correction |
|-----------|------------|
| **Code** | Remove "id" suffix from lookup field references |
| **Documentation** | Update all entity/field names to match schema |
| **Deployment Checklist** | Use correct entity and field names |
| **Integration Tests** | Update mock data with correct field names |

**Total Estimated Effort**: 2-3 hours to search, correct, test, and verify all usages.

---

**Next Steps**:
1. Review this document
2. Run code search to identify all incorrect usages
3. Perform global search/replace with verification
4. Update all documentation
5. Re-run deployment checklist with correct names
