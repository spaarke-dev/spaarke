# Invoice Extraction Prompt Tuning Guide

> **Task**: 046 - Tune Extraction Prompts with Real Invoice Samples
> **Created**: 2026-02-12
> **Status**: Ready for empirical testing with real invoices

## Purpose

This guide provides the methodology for empirically tuning invoice extraction prompts using a test invoice set. The goal is to optimize billing event extraction accuracy across diverse invoice formats (legal, consulting, expense reports, international).

## Background

The invoice extraction handler (`InvoiceExtractionJobHandler` from Task 016) uses GPT-4o with structured output (`GetStructuredCompletionAsync<ExtractionResult>`) to extract billing events from invoice documents. The extraction prompt is stored in a Dataverse `sprk_playbook` record and loaded via `PlaybookService`.

**Key Extraction Challenges**:
- **Multi-format line items**: Tabular, narrative, mixed formats
- **Role class identification**: Partner, Associate, Paralegal, Other from description text
- **Date disambiguation**: Invoice date vs. billing period vs. service date
- **International currency formats**: USD vs. EUR vs. GBP variations ($1,234.56 / USD 1234.56 / 1.234,56 EUR)

## Target Accuracy

| Metric | Target | Rationale |
|--------|--------|-----------|
| **Line Item Extraction** | >= 95% | Must capture all billable items to avoid revenue leakage |
| **Role Class Accuracy** | >= 90% | Critical for rate verification and spend analysis |
| **Date Accuracy** | 100% | Incorrect dates cause budgeting and accrual errors |
| **Currency/Amount Parsing** | 100% | Financial accuracy is non-negotiable |

## Test Invoice Set Requirements

Prepare a representative test set covering:

### 1. Legal Invoices (3+ documents)

**Why**: Primary use case for Spaarke finance module

- **Big Law firm invoice**: AmLaw 100 firm, tabular format, multiple timekeepers
- **Boutique firm invoice**: Smaller firm, narrative line items
- **Mixed format invoice**: Some tabular, some narrative descriptions

**Expected Fields**:
- Invoice number, date, total amount
- Matter/client reference
- Line items: date, timekeeper, role, hours, rate, amount, description
- Billing period (if specified)

### 2. Consulting Invoices (2+ documents)

**Why**: Common alternative billing format for legal spend

- **Hourly consulting invoice**: Similar to legal (hours × rate)
- **Fixed-fee invoice**: Single line item or milestone-based

**Expected Fields**:
- Invoice number, date, total
- Project/matter reference
- Line items: date, consultant, description, amount

### 3. Expense Reports (2+ documents)

**Why**: Embedded in legal invoices, different format

- **Travel expenses**: Flights, hotels, meals
- **Case expenses**: Court fees, filing costs, expert fees

**Expected Fields**:
- Expense type, date, amount, description

### 4. International Invoices (1+ documents)

**Why**: Test currency format variations

- **European invoice**: EUR currency, comma decimal separator (1.234,56 EUR)
- **UK invoice**: GBP currency (£1,234.56)

**Expected Fields**:
- Currency code (EUR, GBP, etc.)
- Amounts with correct decimal separator handling

## Testing Procedure

### Step 1: Prepare Test Environment

1. **Ensure extraction pipeline is deployed**:
   - `InvoiceExtractionJobHandler` operational (Task 016)
   - Azure OpenAI GPT-4o model deployed and configured
   - Playbook service loads extraction prompts from Dataverse

2. **Prepare test invoices**:
   - Create folder: `projects/financial-intelligence-module-r1/test-data/extraction-tuning/`
   - Organize subfolders: `legal/`, `consulting/`, `expense-reports/`, `international/`
   - Add test PDF files to each folder

3. **Create tracking spreadsheet**:
   ```
   | Document Name | Type | Line Items (Actual) | Line Items (Extracted) | Accuracy | Issues |
   |---------------|------|---------------------|------------------------|----------|--------|
   | invoice-biglaw-1.pdf | Legal | 25 | 24 | 96% | Missed 1 narrative item |
   | invoice-consulting-1.pdf | Consulting | 8 | 8 | 100% | None |
   ...
   ```

### Step 2: Run Extraction Tests

**Option A: Via Invoice Confirmation Pipeline (Full Flow)**

1. Upload test invoices as email attachments to the system
2. Wait for classification job (should classify as InvoiceCandidate)
3. Use Invoice Review Confirm endpoint to create sprk_invoice records
4. Wait for extraction job to process
5. Query `sprk_billingevent` records for results:
   ```sql
   SELECT
     sprk_name,
     sprk_invoiceid,
     sprk_linedate,
     sprk_timekeeperrollclass,
     sprk_hours,
     sprk_rate,
     sprk_amount,
     sprk_description
   FROM sprk_billingevent
   WHERE sprk_invoiceid IN (SELECT sprk_invoiceid FROM sprk_invoice WHERE createdon >= DATEADD(hour, -1, GETDATE()))
   ORDER BY sprk_invoiceid, sprk_linedate
   ```

**Option B: Direct API Testing (Faster Iteration)**

If available, use a test endpoint or script to call the extraction service directly:

```csharp
// Pseudocode for test script
var testDocuments = Directory.GetFiles("test-data/extraction-tuning", "*.pdf", SearchOption.AllDirectories);

foreach (var filePath in testDocuments)
{
    var text = await TextExtractorService.ExtractTextAsync(filePath);
    var result = await InvoiceAnalysisService.ExtractBillingDetailsAsync(text);

    Console.WriteLine($"{Path.GetFileName(filePath)}: {result.LineItems.Count} line items extracted");

    // Log to results file
    results.Add(new {
        FileName = Path.GetFileName(filePath),
        ActualLineItems = GetActualLineItemCount(filePath), // Manual count from document
        ExtractedLineItems = result.LineItems.Count,
        RoleClasses = result.LineItems.Select(li => li.RoleClass).Distinct().ToList(),
        Dates = result.Header.InvoiceDate,
        Currency = result.Header.Currency,
        Total = result.Header.TotalAmount
    });
}

// Save results to JSON for analysis
File.WriteAllText("extraction-test-results.json", JsonSerializer.Serialize(results));
```

**Important**: Per ADR-015, do NOT log document content or extracted text. Log only:
- Document filename
- Extraction results (counts, field values)
- Accuracy metrics

### Step 3: Analyze Results

#### 3.1 Line Item Accuracy

```
True Positives (TP): Line items correctly extracted
False Positives (FP): Extracted items that don't exist in invoice
False Negatives (FN): Actual line items not extracted

Precision = TP / (TP + FP)  // % of extracted items that are correct
Recall = TP / (TP + FN)     // % of actual items that were extracted
F1 Score = 2 * (Precision * Recall) / (Precision + Recall)
```

**Target**: F1 Score >= 0.95 (excellent extraction performance)

**Common Failure Modes**:
- **Narrative line items missed**: Invoice has descriptive paragraphs instead of table rows
- **Subtotal confusion**: Extracted subtotals as line items (should be skipped)
- **Multi-page continuation**: Line items spanning multiple pages not merged

#### 3.2 Role Class Accuracy

Compare extracted role classes against actual timekeeper roles:

```
Actual Roles: Partner (5 items), Associate (12 items), Paralegal (8 items)
Extracted Roles: Partner (5), Associate (11), Paralegal (7), Unknown (2)

Role Class Accuracy = Correct Classifications / Total Line Items
                    = (5 + 11 + 7) / 25 = 23/25 = 92%
```

**Target**: >= 90% accuracy

**Common Failure Modes**:
- **Ambiguous titles**: "Counsel" could be Partner or Associate
- **Abbreviations**: "Assoc." vs "Associate" vs "Asst."
- **International roles**: "Solicitor" (UK) vs "Attorney" (US)
- **Non-legal roles**: Consultants, experts, investigators

#### 3.3 Date Fallback Chain

Verify the date selection logic:

| Invoice | Invoice Date | Billing Period End | Document Date | Selected Date | Correct? |
|---------|--------------|-------------------|---------------|---------------|----------|
| invoice-1.pdf | 2026-01-31 | 2026-01-31 | 2026-02-01 | 2026-01-31 | ✅ (used invoice date) |
| invoice-2.pdf | (missing) | 2025-12-31 | 2026-01-05 | 2025-12-31 | ✅ (used billing period) |
| invoice-3.pdf | (missing) | (missing) | 2026-01-15 | 2026-01-15 | ✅ (used document date) |

**Target**: 100% correct date selection

**Fallback Chain** (from spec.md FR-06):
1. **Invoice Date** (explicit "Invoice Date:", "Date:", etc.)
2. **Billing Period End** (if range like "12/1/2025 - 12/31/2025", use end date)
3. **Document Date** (extracted by Document Intelligence as fallback)

#### 3.4 Currency and Amount Parsing

Test various currency formats:

| Format | Example | Expected Parsing | Result |
|--------|---------|------------------|--------|
| **USD Standard** | $1,234.56 | Currency: USD, Amount: 1234.56 | ✅ |
| **USD Explicit** | USD 1234.56 | Currency: USD, Amount: 1234.56 | ✅ |
| **EUR Comma Decimal** | 1.234,56 EUR | Currency: EUR, Amount: 1234.56 | ✅ |
| **GBP Symbol** | £1,234.56 | Currency: GBP, Amount: 1234.56 | ✅ |
| **No Currency** | 1234.56 | Currency: USD (default), Amount: 1234.56 | ✅ |

**Target**: 100% correct currency and amount parsing

**Common Failure Modes**:
- **Decimal separator confusion**: European format (1.234,56) parsed as 1.234 instead of 1234.56
- **Currency symbol placement**: "EUR 123" vs "123 EUR"
- **Multi-currency invoices**: Expenses in different currencies (USD travel, EUR local fees)

### Step 4: Identify Prompt Improvements

Based on test results, common prompt adjustments include:

#### 4a. Line Item Extraction Improvements

**Issue**: Narrative line items missed (precision low)

**Solution**: Add explicit examples to prompt
```
Example narrative format:
"Legal research and case preparation by John Doe, Associate, on January 15, 2026 (8.5 hours @ $350/hour) - $2,975.00"

Extract as:
- Date: 2026-01-15
- Timekeeper: John Doe
- Role: Associate
- Hours: 8.5
- Rate: 350.00
- Amount: 2975.00
```

**Issue**: Subtotals extracted as line items (precision low)

**Solution**: Add exclusion instruction
```
EXCLUDE from line items:
- Subtotals (e.g., "Subtotal for Associate work: $12,500")
- Grand totals
- Tax line items (extract to header only)
- Prior balance / payments
```

#### 4b. Role Class Extraction Improvements

**Issue**: Ambiguous titles misclassified

**Solution**: Add title mapping guidance
```
Role Class Mapping:
- Partner: Partner, Senior Partner, Managing Partner, Equity Partner
- Associate: Associate, Senior Associate, Junior Associate, Attorney, Lawyer
- Paralegal: Paralegal, Legal Assistant, Case Manager
- Other: Consultant, Expert, Investigator, Clerk, Admin

For ambiguous titles like "Counsel":
- If rate >= $500/hour → Partner
- If rate < $500/hour → Associate
```

**Issue**: International role variations

**Solution**: Add international role mapping
```
International Roles:
- Solicitor (UK) → Associate (US equivalent)
- Barrister (UK) → Partner (US equivalent)
- Trainee (UK) → Paralegal (US equivalent)
```

#### 4c. Date Fallback Improvements

**Issue**: Invoice date not detected

**Solution**: Add date pattern examples
```
Invoice Date Patterns:
- "Invoice Date: 01/31/2026"
- "Date: January 31, 2026"
- "Invoice #12345 dated 31-Jan-2026"
- "Billing Date: 2026-01-31"

Billing Period Patterns:
- "For period: 12/1/2025 - 12/31/2025" → Use 12/31/2025
- "Billing Period: December 2025" → Use 12/31/2025
```

#### 4d. Currency/Amount Parsing Improvements

**Issue**: Decimal separator confusion

**Solution**: Add format detection logic
```
Currency Format Detection:
- If "EUR" or "€" present AND comma before last 2 digits → Use comma as decimal separator
  Example: "1.234,56 EUR" → 1234.56
- If "$" or "USD" or no currency AND period before last 2 digits → Use period as decimal separator
  Example: "$1,234.56" → 1234.56
```

### Step 5: Update Extraction Prompts in Dataverse

**Critical**: Prompts are stored in Dataverse `sprk_playbook` records, NOT in source code.

#### Locate Existing Playbook Record

Query Dataverse for the extraction playbook:

```sql
SELECT
  sprk_playbookid,
  sprk_name,
  sprk_version,
  sprk_playbook, -- The prompt text
  sprk_enabled,
  sprk_modeldeployment,
  modifiedon
FROM sprk_playbook
WHERE sprk_name = 'Invoice Extraction Playbook'
ORDER BY sprk_version DESC
```

**Expected Result**:
- `sprk_name`: "Invoice Extraction Playbook"
- `sprk_version`: e.g., "1.0.0"
- `sprk_playbook`: The prompt text (long string)
- `sprk_modeldeployment`: "gpt-4o" (not gpt-4o-mini)

#### Update Prompt and Version

1. **Increment version** per semantic versioning:
   - **Patch** (1.0.0 → 1.0.1): Minor wording tweaks, examples added
   - **Minor** (1.0.0 → 1.1.0): Significant logic changes (new role mappings, date fallback updates)
   - **Major** (1.0.0 → 2.0.0): Complete prompt rewrite, schema changes

2. **Create new playbook record** (recommended) OR update existing:
   ```sql
   -- Option A: Create new version (recommended for rollback capability)
   INSERT INTO sprk_playbook (
     sprk_name,
     sprk_version,
     sprk_playbook,
     sprk_enabled,
     sprk_modeldeployment,
     sprk_playbooktype
   ) VALUES (
     'Invoice Extraction Playbook',
     '1.1.0', -- Incremented version
     '<updated prompt text here>',
     1, -- Enabled
     'gpt-4o',
     100000001 -- ExtractionPlaybook enum value
   )

   -- Option B: Update existing (simpler but no rollback)
   UPDATE sprk_playbook
   SET
     sprk_playbook = '<updated prompt text here>',
     sprk_version = '1.1.0'
   WHERE sprk_name = 'Invoice Extraction Playbook'
   AND sprk_version = '1.0.0'
   ```

3. **Document changes** in playbook description:
   ```
   sprk_description:
   "Version 1.1.0 - Added narrative line item examples and role class mapping for UK solicitors. Improved date fallback detection for billing period end dates. Tested with 8 invoice samples (95% line item accuracy, 92% role accuracy)."
   ```

#### Prompt Update Example

**Original Prompt (v1.0.0)**:
```
You are an expert invoice analyst. Extract all billing line items from the provided invoice text.

For each line item, extract:
- Date
- Timekeeper name
- Role class (Partner, Associate, Paralegal, Other)
- Hours
- Hourly rate
- Total amount
- Description

Return results as structured JSON.
```

**Tuned Prompt (v1.1.0)**:
```
You are an expert invoice analyst specializing in legal and consulting invoices. Extract all billing line items from the provided invoice text.

BILLING LINE ITEM FORMATS:

Tabular Format (most common):
Date | Timekeeper | Hours | Rate | Amount | Description
01/15/2026 | John Doe, Assoc. | 8.5 | $350 | $2,975 | Legal research

Narrative Format:
"Legal research and case preparation by John Doe, Associate, on January 15, 2026 (8.5 hours @ $350/hour) - $2,975.00"

For each line item, extract:
- Date: Service date (mm/dd/yyyy)
- Timekeeper: Full name
- Role Class: Use mapping below
- Hours: Decimal hours (e.g., 8.5)
- Rate: Hourly rate in invoice currency
- Amount: Line item total
- Description: Work description

ROLE CLASS MAPPING:
- Partner: Partner, Senior Partner, Managing Partner, Equity Partner, Barrister (UK)
- Associate: Associate, Senior Associate, Attorney, Lawyer, Counsel, Solicitor (UK)
- Paralegal: Paralegal, Legal Assistant, Case Manager, Trainee (UK)
- Other: Consultant, Expert, Investigator, Clerk, Admin

EXCLUDE from line items:
- Subtotals ("Subtotal for Associate work: $X")
- Grand totals
- Tax line items
- Prior balance or payments

Return results as structured JSON matching the provided schema.
```

### Step 6: Validate Prompt Updates

After updating the playbook in Dataverse:

1. **Re-run extraction on test set**:
   - Use the same test invoices from Step 2
   - Verify extraction uses the new playbook version (check `sprk_playbookversion` on extracted records)

2. **Compare results**:
   | Metric | Before (v1.0.0) | After (v1.1.0) | Improvement |
   |--------|-----------------|----------------|-------------|
   | Line Item Accuracy (F1) | 89% | 97% | +8% |
   | Role Class Accuracy | 85% | 93% | +8% |
   | Date Accuracy | 95% | 100% | +5% |
   | Currency/Amount Parsing | 100% | 100% | - |

3. **Verify no regressions**:
   - Ensure previously working invoice formats still extract correctly
   - Check for new false positives (extracting non-existent line items)

### Step 7: Document Results

Create a summary document for audit and future tuning:

```markdown
# Extraction Prompt Tuning Results — v1.1.0

**Date**: 2026-02-12
**Tester**: [Name]
**Test Set**: 8 invoices (3 legal, 2 consulting, 2 expense reports, 1 international)

## Changes from v1.0.0

1. Added narrative line item format examples
2. Added role class mapping for UK legal roles (Solicitor → Associate, Barrister → Partner)
3. Improved date fallback detection instructions
4. Added explicit exclusions for subtotals and tax line items

## Accuracy Metrics

| Metric | v1.0.0 | v1.1.0 | Target | Status |
|--------|--------|--------|--------|--------|
| Line Item F1 Score | 89% | 97% | >= 95% | ✅ PASS |
| Role Class Accuracy | 85% | 93% | >= 90% | ✅ PASS |
| Date Accuracy | 95% | 100% | 100% | ✅ PASS |
| Currency/Amount Parsing | 100% | 100% | 100% | ✅ PASS |

## Test Results by Invoice Type

### Legal Invoices (3 documents)

**invoice-biglaw-1.pdf** (25 line items):
- Line items: 24/25 extracted (96% - missed 1 narrative item in v1.0.0, fixed in v1.1.0)
- Role classes: 100% accurate (5 Partner, 12 Associate, 8 Paralegal)
- Date: Correct (used invoice date)
- Currency: USD, amounts correct

**invoice-boutique-1.pdf** (12 line items):
- Line items: 12/12 extracted (100%)
- Role classes: 100% accurate
- Date: Correct (used billing period end, no explicit invoice date)
- Currency: USD, amounts correct

**invoice-uk-1.pdf** (18 line items):
- Line items: 18/18 extracted (100%)
- Role classes: 100% accurate after UK role mapping (3 Barristers → Partner, 10 Solicitors → Associate, 5 Trainees → Paralegal)
- Date: Correct (used invoice date)
- Currency: GBP, amounts correct (£ symbol handled)

### Consulting Invoices (2 documents)

**invoice-consulting-1.pdf** (8 line items):
- Line items: 8/8 extracted (100%)
- Role classes: N/A (no roles in consulting invoices)
- Date: Correct
- Currency: USD

**invoice-consulting-fixedfee.pdf** (1 line item):
- Line items: 1/1 extracted (100%)
- Role classes: N/A
- Date: Correct
- Currency: USD

### Expense Reports (2 documents)

**expenses-travel-1.pdf** (7 line items):
- Line items: 7/7 extracted (100%)
- Role classes: N/A
- Date: Correct (used transaction dates)
- Currency: USD

**expenses-mixed-1.pdf** (5 line items):
- Line items: 5/5 extracted (100%)
- Role classes: N/A
- Date: Correct
- Currency: Mixed (USD + EUR) - both parsed correctly

### International Invoice (1 document)

**invoice-eu-1.pdf** (14 line items):
- Line items: 14/14 extracted (100%)
- Role classes: 100% accurate
- Date: Correct
- Currency: EUR, amounts correct (comma decimal separator handled: "1.234,56 EUR" → 1234.56)

## Observations

### What Worked Well
- Narrative line item examples significantly improved recall (+8%)
- UK role mapping eliminated confusion (Solicitor/Barrister now correctly classified)
- Explicit subtotal exclusion reduced false positives
- Currency format detection handles both US and EU formats correctly

### Remaining Challenges
- **Multi-page invoices**: Some line items spanning page breaks occasionally missed (1 case in 8 invoices)
- **Handwritten notes**: Scanned invoices with handwritten margin notes sometimes extracted as line items
- **Summary sections**: Invoices with executive summaries at top occasionally extract summary as line items

### Recommendations
1. **Multi-page handling**: Add instruction to look for continuation markers ("continued on next page")
2. **Handwriting detection**: Exclude text with low OCR confidence scores
3. **Summary section detection**: Add heuristic to skip text before first actual date/amount pair

## Deployment Status

- ✅ Playbook v1.1.0 deployed to Dataverse on 2026-02-12
- ✅ All extraction jobs now use updated prompt
- ✅ Backward compatibility verified (v1.0.0 invoices still extract correctly)

## Rollback Plan

If issues arise:
1. Query Dataverse for previous version: `WHERE sprk_version = '1.0.0'`
2. Update active playbook to point to v1.0.0
3. OR create new playbook record from v1.0.0 template

## Next Steps

- Monitor extraction accuracy in production for 2 weeks
- Collect edge cases from reviewers for next tuning iteration
- Consider v1.2.0 with multi-page and summary detection improvements
```

## Monitoring Post-Deployment

After deploying tuned prompts:

### 1. Track Extraction Metrics

Query Dataverse to monitor extraction success rate:

```sql
-- Extraction success rate over last 30 days
SELECT
    COUNT(*) as TotalExtractions,
    SUM(CASE WHEN sprk_extractionstatus = 100000001 THEN 1 ELSE 0 END) as Successful,
    SUM(CASE WHEN sprk_extractionstatus = 100000002 THEN 1 ELSE 0 END) as Failed,
    AVG(sprk_billingeventcount) as AvgLineItemsExtracted
FROM sprk_invoice
WHERE
    sprk_status IN (100000001, 100000002) -- ToExtract, Extracted
    AND createdon >= DATEADD(day, -30, GETDATE())
```

**Expected Metrics**:
- Success rate: >= 95%
- Avg line items per invoice: 10-30 (varies by firm)
- Failures: < 5%

### 2. Review Queue Monitoring

Monitor the Invoice Review Queue for classification errors that become extraction errors:

```sql
-- Documents in review queue with extraction issues
SELECT
    d.sprk_name,
    d.sprk_classification,
    d.sprk_classificationconfidence,
    i.sprk_invoicenumber,
    i.sprk_extractionstatus,
    i.sprk_extractionmessage
FROM sprk_document d
INNER JOIN sprk_invoice i ON d.sprk_documentid = i.sprk_documentid
WHERE
    i.sprk_extractionstatus = 100000002 -- Failed
    AND d.createdon >= DATEADD(day, -7, GETDATE())
ORDER BY d.createdon DESC
```

### 3. Feedback Loop

Create a feedback mechanism for reviewers:
- When reviewers manually correct extracted billing events → log the original extraction
- Track common correction patterns (e.g., role class corrections, date changes)
- Quarterly review: analyze feedback data and tune prompts for next version

## Troubleshooting

### Issue: All Extractions Failing

**Symptom**: `sprk_extractionstatus = Failed` for all invoices

**Possible Causes**:
1. Playbook not found or disabled
2. Wrong model deployment (using gpt-4o-mini instead of gpt-4o)
3. Prompt syntax error causing JSON schema mismatch

**Resolution**:
```sql
-- Check playbook status
SELECT
    sprk_name,
    sprk_version,
    sprk_enabled,
    sprk_modeldeployment
FROM sprk_playbook
WHERE sprk_name = 'Invoice Extraction Playbook'
ORDER BY modifiedon DESC
```

- Verify `sprk_enabled = 1`
- Verify `sprk_modeldeployment = 'gpt-4o'` (NOT gpt-4o-mini)
- Test prompt manually via Azure OpenAI Playground

### Issue: Line Items Missing

**Symptom**: Extraction succeeds but line item count is low

**Possible Causes**:
1. Narrative format not recognized
2. Subtotals/tax items excluded (correct behavior)
3. Multi-page continuation issues

**Resolution**:
- Review `sprk_extractionmessage` for hints
- Test manually with same invoice
- Add narrative format examples to prompt
- Check if missing items are actually subtotals (expected exclusion)

### Issue: Role Classes All "Other"

**Symptom**: Role class field populated but all values are "Other"

**Possible Causes**:
1. Role mapping not in prompt
2. International roles not mapped
3. Timekeeper names without titles

**Resolution**:
- Add role class mapping to prompt (see Step 4b above)
- Add international role equivalents
- If no role in text, use rate-based heuristic (>= $500/hr → Partner)

### Issue: Date Fallback Not Working

**Symptom**: `sprk_invoicedate` is null even when date exists in document

**Possible Causes**:
1. Date format not recognized
2. Date field label variations not covered
3. Fallback chain not implemented in prompt

**Resolution**:
- Add date pattern examples to prompt (see Step 4c)
- Verify Document Intelligence is extracting document date as fallback
- Check `sprk_extractionmessage` for date parsing errors

## Related Tasks

- **Task 007**: Write Classification Prompt Template (Playbook A)
- **Task 008**: Write Extraction Prompt Template (Playbook B) — initial version
- **Task 016**: Implement InvoiceExtractionJobHandler — uses extraction playbook
- **Task 045**: Tune Classification Confidence Thresholds — similar empirical tuning
- **Task 048**: Integration Tests — validates extraction pipeline end-to-end

## References

- **Spec**: `projects/financial-intelligence-module-r1/spec.md` (FR-06: Extraction requirements)
- **ADR-013**: AI Architecture (playbook system design)
- **ADR-015**: Data governance (no document content in logs)
- **Playbook Records**: Dataverse `sprk_playbook` entity

---

*This guide provides the methodology for prompt tuning. Actual tuning requires real test invoices and a deployed extraction pipeline.*
