# Invoice Classification Threshold Tuning Guide

> **Task**: 045 - Tune Classification Confidence Thresholds
> **Created**: 2026-02-11
> **Status**: Ready for empirical testing with real documents

## Purpose

This guide provides the methodology for empirically tuning invoice classification confidence thresholds using a test document set. The goal is to optimize the InvoiceCandidate/NotInvoice/Unknown classification boundaries to minimize false positives and false negatives.

## Background

The invoice classification handler (`AttachmentClassificationJobHandler` from Task 011) uses GPT-4o-mini with structured output to classify email attachments. The model returns:

- **Classification**: InvoiceCandidate, NotInvoice, or Unknown
- **Confidence Score**: 0.0 - 1.0 representing model confidence

Current thresholds (defaults from implementation) need empirical validation with real document types.

## Target Thresholds

Based on typical LLM confidence calibration patterns, we're targeting:

| Classification | Threshold Range | Rationale |
|---------------|-----------------|-----------|
| **InvoiceCandidate** | Confidence >= **0.85** | High confidence minimizes false positives (non-invoices misclassified as invoices) |
| **NotInvoice** | Confidence <= **0.30** | Low confidence minimizes false negatives (invoices misclassified as non-invoices) |
| **Unknown** | **0.30 - 0.85** | Ambiguous zone routed to human review |

**Note**: These are starting points. Empirical testing may shift these values.

## Test Document Set Requirements

Prepare a representative test set covering:

### 1. Confirmed Invoices (5+ documents)

Variety of formats to ensure threshold works across invoice types:

- **Standard business invoice**: Itemized line items, clear header (Invoice #, Date, Total)
- **Professional services invoice**: Hourly billing, legal/consulting format
- **Utility/subscription invoice**: Recurring service billing (internet, software licenses)
- **International invoice**: Non-US format (VAT invoices, different currency)
- **Simple invoice**: Minimal formatting, small vendor

**Expected Classification**: All should be **InvoiceCandidate** with confidence >= 0.85

### 2. Non-Invoice Documents (5+ documents)

Documents that should NOT be classified as invoices:

- **Contract/Agreement**: Legal document with no billing information
- **Business letter**: Correspondence without financial transaction
- **Receipt**: Point-of-sale receipt (differs from invoice semantically)
- **Statement**: Account statement or summary (not a billing request)
- **Marketing material**: Brochure, flyer, advertisement

**Expected Classification**: All should be **NotInvoice** with confidence <= 0.30

### 3. Edge Cases (3+ documents)

Ambiguous documents that should land in Unknown zone:

- **Cover page**: Invoice cover sheet with summary but no details
- **Partial invoice**: Incomplete scan, missing header or footer
- **Scanned document**: Poor OCR quality, text extraction issues
- **Quote/Estimate**: Pre-invoice proposal (not a billing request yet)

**Expected Classification**: **Unknown** with confidence 0.30 - 0.85

## Testing Procedure

### Step 1: Prepare Test Environment

1. **Ensure classification pipeline is deployed**:
   - `AttachmentClassificationJobHandler` operational (Task 011)
   - `EmailProcessingOptions.AutoClassifyAttachments = true`
   - Azure OpenAI connection configured

2. **Prepare test documents**:
   - Create folder: `projects/financial-intelligence-module-r1/test-data/classification-tuning/`
   - Organize subfolders: `invoices/`, `non-invoices/`, `edge-cases/`
   - Add test PDF/image files to each folder

3. **Create tracking spreadsheet**:
   ```
   | Document Name | Type | Expected | Actual | Confidence | Notes |
   |---------------|------|----------|--------|------------|-------|
   | invoice-1.pdf | Invoice | InvoiceCandidate | ? | ? | Standard format |
   | contract-1.pdf | Non-Invoice | NotInvoice | ? | ? | Legal agreement |
   ...
   ```

### Step 2: Run Classification Tests

**Option A: Via Email Ingestion (Full Pipeline)**

1. Send test documents as email attachments to the system
2. Wait for classification job to process
3. Query `sprk_document` records for results:
   ```sql
   SELECT
     sprk_name,
     sprk_classification,
     sprk_classificationconfidence,
     sprk_invoicevendornamehint,
     sprk_invoicenumberhint,
     sprk_invoicetotalhint
   FROM sprk_document
   WHERE createdon >= DATEADD(hour, -1, GETDATE())
   ORDER BY createdon DESC
   ```

**Option B: Direct API Testing (Faster Iteration)**

If available, use a test endpoint or script to call the classification service directly:

```csharp
// Pseudocode for test script
var testDocuments = Directory.GetFiles("test-data/classification-tuning", "*.pdf", SearchOption.AllDirectories);

foreach (var filePath in testDocuments)
{
    var text = await TextExtractorService.ExtractTextAsync(filePath);
    var result = await InvoiceAnalysisService.ClassifyDocumentAsync(text);

    Console.WriteLine($"{Path.GetFileName(filePath)}: {result.Classification} ({result.Confidence:F2})");

    // Log to results file
    results.Add(new {
        FileName = Path.GetFileName(filePath),
        Classification = result.Classification,
        Confidence = result.Confidence
    });
}

// Save results to JSON or CSV for analysis
File.WriteAllText("classification-test-results.json", JsonSerializer.Serialize(results));
```

**Important**: Per ADR-015, do NOT log document content. Log only:
- Document filename
- Classification result
- Confidence score
- Invoice hints (vendor, number, total)

### Step 3: Analyze Results

#### 3.1 Calculate Accuracy Metrics

```
True Positives (TP): Invoices correctly classified as InvoiceCandidate
False Positives (FP): Non-invoices incorrectly classified as InvoiceCandidate
True Negatives (TN): Non-invoices correctly classified as NotInvoice
False Negatives (FN): Invoices incorrectly classified as NotInvoice

Precision = TP / (TP + FP)  // % of InvoiceCandidate predictions that were correct
Recall = TP / (TP + FN)     // % of actual invoices that were detected
F1 Score = 2 * (Precision * Recall) / (Precision + Recall)
```

**Target**: F1 Score >= 0.95 (excellent classification performance)

#### 3.2 Plot Confidence Distribution

Create a histogram or box plot showing confidence score distribution for each document type:

```
Invoices:        [========|===]           (High confidence cluster 0.85-1.0)
Non-Invoices: [===|========]              (Low confidence cluster 0.0-0.30)
Edge Cases:      [====|====|====]          (Mid-range 0.30-0.85)
              0.0   0.3   0.5   0.85  1.0
```

#### 3.3 Identify Threshold Boundaries

Look for natural gaps in the distribution:

- **Invoice Cluster**: Where do invoice confidence scores bottom out? (This is your InvoiceCandidate threshold)
- **Non-Invoice Cluster**: Where do non-invoice scores top out? (This is your NotInvoice threshold)
- **Unknown Zone**: What range has mixed document types? (This is the human review zone)

### Step 4: Adjust Thresholds

Based on empirical results, update thresholds in `FinanceOptions`:

```csharp
// Before (defaults)
public class FinanceOptions
{
    public double InvoiceCandidateThreshold { get; set; } = 0.85;
    public double NotInvoiceThreshold { get; set; } = 0.30;
}

// After (tuned values - example)
public class FinanceOptions
{
    /// <summary>
    /// Minimum confidence threshold for classifying a document as InvoiceCandidate.
    /// Tuned empirically on 2026-02-11 using 15 test documents.
    /// Target: Minimize false positives (non-invoices classified as invoices).
    /// </summary>
    public double InvoiceCandidateThreshold { get; set; } = 0.88;  // Raised from 0.85

    /// <summary>
    /// Maximum confidence threshold for classifying a document as NotInvoice.
    /// Tuned empirically on 2026-02-11 using 15 test documents.
    /// Target: Minimize false negatives (invoices classified as non-invoices).
    /// </summary>
    public double NotInvoiceThreshold { get; set; } = 0.25;  // Lowered from 0.30
}
```

**Rationale for Example Adjustments**:
- If invoices had min confidence 0.88, raise InvoiceCandidate threshold to 0.88
- If non-invoices had max confidence 0.25, lower NotInvoice threshold to 0.25
- Wider Unknown zone (0.25 - 0.88) captures more edge cases for human review

### Step 5: Validate with Re-Run

After updating thresholds:

1. Re-run classification on the same test set
2. Verify:
   - All invoices → InvoiceCandidate (no false negatives)
   - All non-invoices → NotInvoice (no false positives)
   - Edge cases → Unknown (appropriate for human review)
3. Calculate new F1 score - should improve or stay >= 0.95

## Configuration Update Location

Update thresholds in:

**File**: `src/server/api/Sprk.Bff.Api/Configuration/FinanceOptions.cs`

```csharp
public class FinanceOptions
{
    // ... other options ...

    /// <summary>
    /// Minimum confidence threshold for InvoiceCandidate classification.
    /// Documents with confidence >= this value are classified as likely invoices.
    /// Default: 0.85 (85% confidence)
    /// Tuned: [INSERT TUNED VALUE] on [INSERT DATE]
    /// </summary>
    public double InvoiceCandidateThreshold { get; set; } = 0.85;

    /// <summary>
    /// Maximum confidence threshold for NotInvoice classification.
    /// Documents with confidence <= this value are classified as likely NOT invoices.
    /// Default: 0.30 (30% confidence)
    /// Tuned: [INSERT TUNED VALUE] on [INSERT DATE]
    /// </summary>
    public double NotInvoiceThreshold { get; set; } = 0.30;

    /// <summary>
    /// Documents with confidence between NotInvoiceThreshold and InvoiceCandidateThreshold
    /// are classified as Unknown and routed to human review.
    /// </summary>
    public double UnknownZoneSize => InvoiceCandidateThreshold - NotInvoiceThreshold;
}
```

## Expected Results Documentation Template

After completing tuning, document results:

```markdown
# Classification Threshold Tuning Results

**Date**: [INSERT DATE]
**Tester**: [INSERT NAME]
**Test Set Size**: [N invoices, M non-invoices, P edge cases]

## Tuned Thresholds

- **InvoiceCandidate**: >= 0.XX (was 0.85)
- **NotInvoice**: <= 0.XX (was 0.30)
- **Unknown**: 0.XX - 0.XX

## Accuracy Metrics

| Metric | Value |
|--------|-------|
| Precision | 0.XX (XX%) |
| Recall | 0.XX (XX%) |
| F1 Score | 0.XX (XX%) |
| False Positives | X out of Y non-invoices |
| False Negatives | X out of Y invoices |

## Confidence Score Distribution

### Invoices (N=X)
- Min: 0.XX
- Max: 0.XX
- Mean: 0.XX
- Median: 0.XX

### Non-Invoices (N=X)
- Min: 0.XX
- Max: 0.XX
- Mean: 0.XX
- Median: 0.XX

### Edge Cases (N=X)
- Min: 0.XX
- Max: 0.XX
- Mean: 0.XX
- Median: 0.XX

## Observations

- [Document type X] consistently scored [high/low] confidence
- [Edge case Y] correctly routed to Unknown for review
- [Any surprising results or patterns noted]

## Recommendations

- [Threshold changes applied]
- [Future monitoring suggestions]
- [Known limitations or document types to watch]
```

## Monitoring Post-Deployment

After deploying tuned thresholds to production:

### 1. Track Classification Metrics

Query Dataverse to monitor classification distribution:

```sql
-- Classification distribution over last 30 days
SELECT
    sprk_classification,
    COUNT(*) as Count,
    AVG(sprk_classificationconfidence) as AvgConfidence,
    MIN(sprk_classificationconfidence) as MinConfidence,
    MAX(sprk_classificationconfidence) as MaxConfidence
FROM sprk_document
WHERE
    sprk_documenttype = 100000007  -- Email Attachment
    AND createdon >= DATEADD(day, -30, GETDATE())
    AND sprk_classification IS NOT NULL
GROUP BY sprk_classification
ORDER BY Count DESC
```

**Expected Production Distribution**:
- InvoiceCandidate: ~40-60% (depends on email volume)
- NotInvoice: ~30-50%
- Unknown: ~10-20% (should be minority - too high indicates thresholds too conservative)

### 2. Review Queue Health

Monitor the Invoice Review Queue (Task 047) size:
- **Queue size growing**: Too many Unknown classifications → thresholds may need tightening
- **High reject rate**: Reviewers rejecting many InvoiceCandidate → threshold too low (false positives)
- **Missed invoices**: Invoices showing up as NotInvoice → threshold too high (false negatives)

### 3. Feedback Loop

Create a feedback mechanism:
- When reviewers CONFIRM an invoice that was classified as Unknown → log the original confidence score
- When reviewers REJECT an InvoiceCandidate → log the original confidence score
- Quarterly review: analyze feedback data and re-tune if patterns emerge

## Troubleshooting

### Issue: All Documents Classified as Unknown

**Symptom**: Confidence scores cluster in the 0.30-0.85 range

**Possible Causes**:
1. Model is genuinely uncertain (poor document quality, OCR issues)
2. Prompt needs refinement (see Task 046 for prompt tuning)
3. Thresholds set too conservatively

**Resolution**:
- Review text extraction quality (check `TextExtractorService` output)
- Inspect classification prompt for clarity
- Consider widening thresholds if model confidence is well-calibrated

### Issue: Too Many False Positives

**Symptom**: Non-invoices classified as InvoiceCandidate

**Possible Causes**:
1. InvoiceCandidate threshold too low
2. Model misinterpreting document type

**Resolution**:
- Raise InvoiceCandidate threshold (e.g., from 0.85 to 0.90)
- Review false positive documents to identify common patterns
- Enhance classification prompt with negative examples (Task 046)

### Issue: Too Many False Negatives

**Symptom**: Invoices classified as NotInvoice

**Possible Causes**:
1. NotInvoice threshold too high
2. Invoice format not recognized by model

**Resolution**:
- Lower NotInvoice threshold (e.g., from 0.30 to 0.20)
- Add diverse invoice formats to prompt examples (Task 046)
- Verify text extraction quality for problematic invoice types

## Related Tasks

- **Task 011**: AttachmentClassificationJobHandler implementation
- **Task 046**: Tune Extraction Prompts with Real Invoice Samples
- **Task 047**: Invoice Review Queue (where Unknown documents land)

## References

- **Spec**: `projects/financial-intelligence-module-r1/spec.md` (FR-02: Classification requirements)
- **ADR-015**: Data governance constraints (no document content in logs)
- **FinanceOptions**: `src/server/api/Sprk.Bff.Api/Configuration/FinanceOptions.cs`

---

*This guide provides the methodology. Actual threshold tuning requires real test documents and a deployed classification pipeline.*
