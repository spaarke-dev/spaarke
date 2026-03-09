# Dataverse Schema Deployment Checklist

> **Created**: 2026-02-12
> **Purpose**: Verified deployment checklist with correct entity and field names
> **Reference**: See `docs/data-model/sprk_financial-related-entities.md` for authoritative schema

---

## Pre-Deployment Verification

- [ ] Review schema export: `docs/data-model/sprk_financial-related-entities.md`
- [ ] Review corrections document: `docs/data-model/schema-corrections.md`
- [ ] Backup existing Dataverse environment (export solution)
- [ ] PAC CLI authenticated to target environment
- [ ] User has System Administrator or System Customizer role

---

## Deployment Steps

### Step 1: Deploy New Entities (6 Total)

#### 1.1 Invoice Entity (`sprk_invoice`)

**Entity Configuration**:
- Logical Name: `sprk_invoice`
- Display Name: Invoice
- Primary Name Field: `sprk_name` (Text, 850 chars)
- Ownership: Organization

**Key Fields**:
- [ ] `sprk_invoiceid` (Uniqueidentifier) - Primary key
- [ ] `sprk_name` (Text, 850) - Primary name
- [ ] `sprk_document` (Lookup → sprk_document) - Source attachment
- [ ] `sprk_matter` (Lookup → sprk_matter) - Parent matter
- [ ] `sprk_project` (Lookup → sprk_project) - Parent project
- [ ] `sprk_invoicenumber` (Text, 100)
- [ ] `sprk_invoicedate` (Date)
- [ ] `sprk_totalamount` (Currency)
- [ ] `sprk_confidence` (Decimal) - Classification confidence
- [ ] `sprk_invoicestatus` (Choice) - ToReview | Reviewed
- [ ] `sprk_extractionstatus` (Choice) - NotRun | Extracted | Failed
- [ ] `sprk_correlationid` (Text, 100)
- [ ] `sprk_currency` (Text, 10)
- [ ] `sprk_regardingrecordtype` (Lookup → sprk_recordtype_ref)

**Alternate Keys**: None

**Verification**:
```powershell
pac solution check --name SpaarkeFinance --entity sprk_invoice
```

---

#### 1.2 Billing Event Entity (`sprk_billingevent`)

**Entity Configuration**:
- Logical Name: `sprk_billingevent`
- Display Name: Billing Event
- Primary Name Field: `sprk_name` (Text, 850 chars)
- Ownership: Organization

**Key Fields**:
- [ ] `sprk_billingeventid` (Uniqueidentifier) - Primary key
- [ ] `sprk_name` (Text, 850) - Primary name
- [ ] `sprk_invoice` (Lookup → sprk_invoice) - Parent invoice ⚠️ **NOT** `sprk_invoiceid`
- [ ] `sprk_linesequence` (Integer, min: 1) - Line position
- [ ] `sprk_matter` (Lookup → sprk_matter) - Denormalized ⚠️ **NOT** `sprk_matterid`
- [ ] `sprk_project` (Lookup → sprk_project) - Denormalized ⚠️ **NOT** `sprk_projectid`
- [ ] `sprk_vendororg` (Lookup → sprk_organization) - Vendor
- [ ] `sprk_description` (Multiline Text, 2000)
- [ ] `sprk_amount` (Currency, required)
- [ ] `sprk_quantity` (Decimal)
- [ ] `sprk_rate` (Currency)
- [ ] `sprk_timekeeper` (Text, 100)
- [ ] `sprk_timekeeperrole` (Choice) - SeniorPartner | Partner | SeniorAssociate | Associate | Paralegal | Specialist | Other
- [ ] `sprk_roleclass` (Text, 100)
- [ ] `sprk_eventdate` (Date)
- [ ] `sprk_visibilitystate` (Choice) - Invoiced | InternalWIP | PreBill | Paid | WrittenOff | Approved
- [ ] `sprk_costtype` (Choice) - Fee | Expense
- [ ] `sprk_currency` (Text, 10)
- [ ] `sprk_correlationid` (Text, 100)

**🔑 Alternate Key (CRITICAL)**:
- [ ] Key Name: `BillingEvent_InvoiceLine_Key` (or similar)
- [ ] Fields: `sprk_invoice` + `sprk_linesequence`
- [ ] Purpose: Idempotent upsert during re-extraction (ADR-004)

**Verification**:
```powershell
# Verify alternate key exists
pac solution check --name SpaarkeFinance --entity sprk_billingevent --keys
```

---

#### 1.3 Budget Entity (`sprk_budget`)

⚠️ **IMPORTANT**: Entity is named `sprk_budget`, NOT `sprk_budgetplan`

**Entity Configuration**:
- Logical Name: `sprk_budget`
- Display Name: Budget
- Primary Name Field: `sprk_name` (Text, 850 chars)
- Ownership: Organization

**Key Fields**:
- [ ] `sprk_budgetid` (Uniqueidentifier) - Primary key
- [ ] `sprk_name` (Text, 850) - Primary name
- [ ] `sprk_matter` (Lookup → sprk_matter)
- [ ] `sprk_project` (Lookup → sprk_project)
- [ ] `sprk_budgetyear` (Text, 4)
- [ ] `sprk_budgetstartdate` (Date)
- [ ] `sprk_budgetenddate` (Date)
- [ ] `sprk_totalbudget` (Currency)
- [ ] `sprk_budgetstatus` (Choice) - Draft | Pending | Open | Completed | Closed | OnHold | Cancelled | Archived
- [ ] `sprk_budgetperiod` (Choice) - Annual | Quarter1 | Quarter2 | Quarter3 | Quarter4
- [ ] `sprk_budgetcategory` (Choice) - BudgetCategory0-4
- [ ] `sprk_currency` (Text, 10)

**Alternate Keys**: None

**Verification**:
```powershell
pac solution check --name SpaarkeFinance --entity sprk_budget
```

---

#### 1.4 Budget Bucket Entity (`sprk_budgetbucket`)

**Entity Configuration**:
- Logical Name: `sprk_budgetbucket`
- Display Name: Budget Bucket
- Primary Name Field: `sprk_name` (Text, 850 chars)
- Ownership: Organization

**Key Fields**:
- [ ] `sprk_budgetbucketid` (Uniqueidentifier) - Primary key
- [ ] `sprk_name` (Text, 850) - Primary name
- [ ] `sprk_budget` (Lookup → sprk_budget) - Parent budget ⚠️ **NOT** `sprk_budgetid`
- [ ] `sprk_amount` (Currency) - Budget allocation
- [ ] `sprk_bucketkey` (Text, 100) - TOTAL for MVP
- [ ] `sprk_budgetcategory` (Choice) - BudgetCategory0-4
- [ ] `sprk_periodstart` (Date) - Null for lifetime
- [ ] `sprk_periodend` (Date) - Null for lifetime

**Alternate Keys**: None

**Verification**:
```powershell
pac solution check --name SpaarkeFinance --entity sprk_budgetbucket
```

---

#### 1.5 Spend Snapshot Entity (`sprk_spendsnapshot`)

**Entity Configuration**:
- Logical Name: `sprk_spendsnapshot`
- Display Name: Spend Snapshot
- Primary Name Field: `sprk_name` (Text, 850 chars)
- Ownership: Organization

**Key Fields**:
- [ ] `sprk_spendsnapshotid` (Uniqueidentifier) - Primary key
- [ ] `sprk_name` (Text, 850) - Primary name
- [ ] `sprk_matter` (Lookup → sprk_matter)
- [ ] `sprk_project` (Lookup → sprk_project)
- [ ] `sprk_snapshotperiod` (Choice) - Month | ToDate
- [ ] `sprk_periodvalue` (Text, 20) - e.g., "2026-02"
- [ ] `sprk_generatedat` (DateTime) - Computation timestamp
- [ ] `sprk_invoicedamount` (Currency) - Sum of BillingEvents
- [ ] `sprk_allocatedamount` (Currency) - From BudgetBucket
- [ ] `sprk_budgetamount` (Currency) - From BudgetBucket
- [ ] `sprk_budgetvariance` (Currency) - Budget - Invoiced
- [ ] `sprk_budgetvariancepct` (Decimal) - Variance / Budget * 100
- [ ] `sprk_momvelocity` (Decimal) - Month-over-month growth
- [ ] `sprk_bucketkey` (Text, 100) - TOTAL for MVP
- [ ] `sprk_correlationid` (Text, 100)

**🔑 Alternate Key (CRITICAL)**:
- [ ] Key Name: `SpendSnapshot_Period_Key` (or similar)
- [ ] Fields: `sprk_matter` + `sprk_project` + `sprk_snapshotperiod` + `sprk_periodvalue` + `sprk_generatedat`
- [ ] Purpose: Idempotent upsert during snapshot re-generation
- [ ] **Note**: All 5 fields required (matter/project can be null but still in key)

**Verification**:
```powershell
# Verify alternate key with all 5 fields
pac solution check --name SpaarkeFinance --entity sprk_spendsnapshot --keys
```

---

#### 1.6 Spend Signal Entity (`sprk_spendsignal`)

**Entity Configuration**:
- Logical Name: `sprk_spendsignal`
- Display Name: Spend Signal
- Primary Name Field: `sprk_name` (Text, 850 chars)
- Ownership: Organization

**Key Fields**:
- [ ] `sprk_spendsignalid` (Uniqueidentifier) - Primary key
- [ ] `sprk_name` (Text, 850) - Primary name
- [ ] `sprk_matter` (Lookup → sprk_matter)
- [ ] `sprk_project` (Lookup → sprk_project)
- [ ] `sprk_snapshot` (Lookup → sprk_spendsnapshot) - Source snapshot
- [ ] `sprk_signaltype` (Choice) - BudgetExceeded | BudgetWarning | VelocitySpike | AnomalyDetected
- [ ] `sprk_severity` (Choice) - Info | Warning | Critical
- [ ] `sprk_message` (Text, 500) - Human-readable description
- [ ] `sprk_generatedat` (DateTime) - Signal detection timestamp
- [ ] `sprk_isactive` (Boolean) - Active until resolved
- [ ] `sprk_spendsignalstatus` (Choice) - Active | Acknowledged | Resolved | AutoResolved
- [ ] `sprk_resolutionnotes` (Multiline Text, 5000)

**Alternate Keys**: None

**Verification**:
```powershell
pac solution check --name SpaarkeFinance --entity sprk_spendsignal
```

---

### Step 2: Extend Document Entity (`sprk_document`)

Add 13 new fields to existing `sprk_document` entity:

#### Classification Fields (4):
- [ ] `sprk_classification` (Choice) - InvoiceCandidate | NotInvoice | Unknown
- [ ] `sprk_classificationconfidence` (Decimal, precision: 4) - 0.0 to 1.0
- [ ] `sprk_classificationdate` (DateTime)
- [ ] `sprk_classificationsource` (Text, 100) - Playbook name

#### Invoice Hint Fields (4):
- [ ] `sprk_invoicenumberhint` (Text, 100)
- [ ] `sprk_invoicedatehint` (Date)
- [ ] `sprk_invoiceamounthint` (Currency)
- [ ] `sprk_invoicevendornamehint` (Text, 200)

#### Review Workflow Fields (5):
- [ ] `sprk_reviewstatus` (Choice) - PendingReview | Reviewed | Skipped
- [ ] `sprk_reviewedby` (Lookup → systemuser)
- [ ] `sprk_revieweddate` (DateTime)
- [ ] `sprk_reviewnotes` (Multiline Text)
- [ ] `sprk_invoice` (Lookup → sprk_invoice) - Link to confirmed invoice

**Verification**:
```powershell
pac solution check --name SpaarkeFinance --entity sprk_document
```

---

### Step 3: Add Finance Fields to Matter/Project Entities

Add 6 denormalized fields to **both** `sprk_matter` and `sprk_project`:

- [ ] `sprk_totalspendtodate` (Currency) - Cached total spend
- [ ] `sprk_budgetutilizationpercent` (Decimal) - % of budget used
- [ ] `sprk_activesignalcount` (Integer) - Count of active alerts
- [ ] `sprk_monthlyspendcurrent` (Currency) - Current month spend
- [ ] `sprk_momvelocity` (Decimal) - Month-over-month growth
- [ ] `sprk_monthlyspendtimeline` (Multiline Text) - JSON for 12-month history

**Verification**:
```powershell
pac solution check --name SpaarkeFinance --entity sprk_matter
pac solution check --name SpaarkeFinance --entity sprk_project
```

---

### Step 4: Create Dataverse Views (2 Total)

#### View 1: Invoice Review Queue

**Configuration**:
- Entity: `sprk_invoice`
- View Name: Invoice Review Queue
- View Type: Public
- Filter: `sprk_invoicestatus` = ToReview (value: 100000000)

**Columns** (suggested order):
1. Invoice Number (`sprk_invoicenumber`)
2. Document Name (via `sprk_document` lookup)
3. Confidence (`sprk_confidence`)
4. Matter (`sprk_matter`)
5. Total Amount (`sprk_totalamount`)
6. Invoice Date (`sprk_invoicedate`)
7. Created On

**Sort**: Created On (descending)

**Verification**:
- [ ] View displays in navigation
- [ ] Filter shows only ToReview invoices
- [ ] Columns display correctly

---

#### View 2: Active Invoices

**Configuration**:
- Entity: `sprk_invoice`
- View Name: Active Invoices
- View Type: Public
- Filter: `sprk_invoicestatus` = Reviewed (value: 100000001)

**Columns** (suggested order):
1. Invoice Number (`sprk_invoicenumber`)
2. Matter (`sprk_matter`)
3. Total Amount (`sprk_totalamount`)
4. Invoice Date (`sprk_invoicedate`)
5. Extraction Status (`sprk_extractionstatus`)
6. Modified On

**Sort**: Invoice Date (descending)

**Verification**:
- [ ] View displays in navigation
- [ ] Filter shows only Reviewed invoices
- [ ] Columns display correctly

---

## Post-Deployment Verification

### Alternate Key Verification (CRITICAL)

**BillingEvent Alternate Key**:
```powershell
# Test upsert with same invoice + linesequence twice
# Should update existing record, not create duplicate
$invoiceGuid = [Guid]::NewGuid()
$entity1 = New-CrmRecord -EntityLogicalName "sprk_billingevent" -Fields @{
    "sprk_invoice" = New-CrmEntityReference -EntityLogicalName "sprk_invoice" -Id $invoiceGuid
    "sprk_linesequence" = 1
    "sprk_amount" = 100.00
    "sprk_description" = "Test line 1"
}

$entity2 = New-CrmRecord -EntityLogicalName "sprk_billingevent" -Fields @{
    "sprk_invoice" = New-CrmEntityReference -EntityLogicalName "sprk_invoice" -Id $invoiceGuid
    "sprk_linesequence" = 1
    "sprk_amount" = 150.00  # Updated amount
    "sprk_description" = "Test line 1 updated"
}

# Verify: Query returns 1 record with amount = 150.00
```

**SpendSnapshot Alternate Key**:
```powershell
# Test upsert with same matter + period + generatedat twice
# Should update existing record, not create duplicate
$matterGuid = [Guid]::NewGuid()
$timestamp = Get-Date

$snapshot1 = New-CrmRecord -EntityLogicalName "sprk_spendsnapshot" -Fields @{
    "sprk_matter" = New-CrmEntityReference -EntityLogicalName "sprk_matter" -Id $matterGuid
    "sprk_snapshotperiod" = 100000000  # Month
    "sprk_periodvalue" = "2026-02"
    "sprk_generatedat" = $timestamp
    "sprk_invoicedamount" = 1000.00
}

$snapshot2 = New-CrmRecord -EntityLogicalName "sprk_spendsnapshot" -Fields @{
    "sprk_matter" = New-CrmEntityReference -EntityLogicalName "sprk_matter" -Id $matterGuid
    "sprk_snapshotperiod" = 100000000  # Month
    "sprk_periodvalue" = "2026-02"
    "sprk_generatedat" = $timestamp
    "sprk_invoicedamount" = 1500.00  # Updated amount
}

# Verify: Query returns 1 record with invoicedamount = 1500.00
```

---

### Entity Relationship Verification

**Lookup Field Tests**:
```powershell
# Create invoice with matter lookup
$invoiceFields = @{
    "sprk_name" = "Test Invoice"
    "sprk_matter" = New-CrmEntityReference -EntityLogicalName "sprk_matter" -Id $matterGuid
    "sprk_invoicenumber" = "INV-2026-001"
}
$invoiceId = New-CrmRecord -EntityLogicalName "sprk_invoice" -Fields $invoiceFields

# Retrieve and verify lookup
$invoice = Get-CrmRecord -EntityLogicalName "sprk_invoice" -Id $invoiceId -Fields "sprk_matter"
# Verify: $invoice.sprk_matter.Id equals $matterGuid
```

---

## Rollback Plan

If deployment fails or issues discovered:

1. **Export Current State**:
   ```powershell
   pac solution export --name SpaarkeFinance --path ./backup/finance-pre-rollback.zip
   ```

2. **Delete Entities** (in reverse order of creation):
   - SpendSignal
   - SpendSnapshot
   - BudgetBucket
   - Budget
   - BillingEvent
   - Invoice

3. **Remove Fields** from Document, Matter, Project

4. **Delete Views**

5. **Restore from Backup** (if needed):
   ```powershell
   pac solution import --path ./backup/finance-pre-deployment.zip
   ```

---

## Deployment Commands

### Using PAC CLI

**Export Solution** (before deployment):
```powershell
pac solution export --name SpaarkeFinance --path ./backup/finance-pre-deployment.zip --managed false
```

**Import Solution** (after creating in dev):
```powershell
pac solution import --path ./output/SpaarkeFinance.zip --publish-changes --activate-plugins
```

**Verify Deployment**:
```powershell
pac solution list | Where-Object { $_.Name -like "*Finance*" }
```

---

## Completion Checklist

- [ ] All 6 entities created and verified
- [ ] All 13 fields added to Document entity
- [ ] All 6 fields added to Matter entity
- [ ] All 6 fields added to Project entity
- [ ] Both views created and tested
- [ ] 2 alternate keys configured and verified (BillingEvent, SpendSnapshot)
- [ ] Lookup relationships tested
- [ ] Solution exported and backed up
- [ ] Deployment documented in deployment log

---

## Next Steps After Dataverse Deployment

1. 🔲 Deploy Azure AI Search invoice index
2. 🔲 Create playbook records (classification + extraction prompts)
3. 🔲 Deploy BFF API code to App Service
4. 🔲 Import VisualHost chart definitions
5. 🔲 Enable feature flag: `AutoClassifyAttachments`
6. 🔲 Run post-deployment validation
7. 🔲 Validate performance criteria
8. 🔲 Complete Task 090 (Project Wrap-up)

---

**Deployment Date**: _________________
**Deployed By**: _________________
**Environment**: _________________
**Status**: _________________
