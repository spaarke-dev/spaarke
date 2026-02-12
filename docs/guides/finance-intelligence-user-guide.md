# Finance Intelligence User Guide

> **Document Version**: 1.0
> **Last Updated**: 2026-02-12
> **Module**: Finance Intelligence R1
> **Audience**: Business Users, Legal Operations, Finance Teams

---

## Table of Contents

1. [Introduction](#introduction)
2. [Getting Started](#getting-started)
3. [System Configuration](#system-configuration)
4. [Business Process Flow](#business-process-flow)
5. [User Workflows](#user-workflows)
6. [Finance Intelligence Panel](#finance-intelligence-panel)
7. [Understanding Metrics and Signals](#understanding-metrics-and-signals)
8. [Important User Considerations](#important-user-considerations)
9. [Troubleshooting](#troubleshooting)
10. [Glossary](#glossary)

---

## Introduction

### What is Finance Intelligence?

The Finance Intelligence module automates legal invoice processing and provides real-time spend visibility. It eliminates manual invoice intake, automates billing fact extraction, and surfaces actionable spend insights directly within your matter management workflow.

### Key Capabilities

- **Automated Invoice Classification**: AI analyzes email attachments and identifies invoice candidates with 95%+ accuracy
- **Human Review Queue**: Review and confirm invoice candidates before processing
- **Structured Data Extraction**: AI extracts line items, dates, amounts, and billing details from confirmed invoices
- **Pre-Computed Spend Analytics**: Month-to-date and monthly spend snapshots with budget variance tracking
- **Threshold-Based Alerts**: Automated signals for budget exceedance, velocity spikes, and anomalies
- **Semantic Invoice Search**: Find invoices using natural language queries
- **Embedded Finance Dashboard**: Budget gauges, spend timelines, and signal alerts on Matter forms

### Who Uses This System?

| Role | Use Cases |
|------|-----------|
| **Legal Operations** | Monitor invoice flow, review queue, budget compliance |
| **Finance Teams** | Track legal spend, validate billing, analyze trends |
| **Matter Managers** | Review matter-specific spend, respond to budget alerts |
| **Administrators** | Configure budgets, manage thresholds, tune AI models |

---

## Getting Started

### Prerequisites

Before using Finance Intelligence, ensure:

1. **Email Integration Active**: Email-to-Document pipeline must be operational (emails create Document records with SPE file attachments)
2. **User Permissions**: Users need read/write access to:
   - `sprk_document` (Document Profile entity)
   - `sprk_invoice` (Invoice entity)
   - `sprk_billingevent` (Billing Event entity)
   - `sprk_matter` or `sprk_project` (Matter/Project entities)
   - `sprk_budgetplan` and `sprk_budgetbucket` (if configuring budgets)
3. **Feature Flag Enabled**: `AutoClassifyAttachments` feature flag must be ON for automatic classification
4. **Budgets Configured**: At least one Budget Plan and Budget Bucket assigned to relevant matters for spend tracking

### First-Time Setup Checklist

- [ ] Verify email ingestion creates Document records
- [ ] Confirm Auto-Classification feature flag is enabled
- [ ] Create Budget Plans for matters (or import from existing system)
- [ ] Assign Budget Buckets to Budget Plans
- [ ] Link Budget Plans to Matters or Projects
- [ ] Configure signal thresholds (or use defaults)
- [ ] Add "Invoice Review Queue" Dataverse view to navigation
- [ ] Add Finance Intelligence panel to Matter form (VisualHost charts)

### Accessing Key Features

| Feature | Access Point |
|---------|--------------|
| **Invoice Review Queue** | Navigation menu → "Invoice Review Queue" view |
| **Finance Dashboard** | Matter form → Finance Intelligence section (budget gauge + timeline) |
| **Invoice Search** | BFF API endpoint: `POST /api/finance/invoice-search` |
| **Spend Summary** | BFF API endpoint: `GET /api/finance/summary?matterId={id}` |

---

## System Configuration

### Budget Configuration

**Budget Plans** define overall spend limits and tracking periods.

**To create a Budget Plan**:
1. Navigate to Budget Plans in Dataverse
2. Click **+ New**
3. Fill in required fields:
   - **Name**: Descriptive name (e.g., "FY2026 Outside Counsel Budget")
   - **Fiscal Year**: Year for tracking
   - **Total Budget**: Total authorized spend amount
   - **Start Date** and **End Date**: Budget period
4. **Save**

**Budget Buckets** allocate portions of the Budget Plan to specific categories or matters.

**To create Budget Buckets**:
1. Open a Budget Plan
2. Go to **Budget Buckets** related tab
3. Click **+ New Budget Bucket**
4. Fill in:
   - **Name**: Category or matter name
   - **Allocated Amount**: Portion of total budget
   - **Matter or Project**: Link to specific matter (optional)
5. **Save**

**Linking Budgets to Matters**:
- Open a Matter record
- Set **Budget Plan** lookup field to the appropriate Budget Plan
- Save

### AI Model Configuration

**Classification Playbook (Playbook A)**:
- **Model**: gpt-4o-mini
- **Purpose**: Classifies attachments as Invoice Candidate, Unknown, or Not Invoice
- **Location**: Dataverse `sprk_aiplaybook` record with name "Invoice Classification"
- **Default Prompt**: Analyzes filename, sender, content patterns

**Extraction Playbook (Playbook B)**:
- **Model**: gpt-4o
- **Purpose**: Extracts structured billing data (line items, amounts, dates)
- **Location**: Dataverse `sprk_aiplaybook` record with name "Invoice Extraction"
- **Default Prompt**: Produces `InvoiceExtractionResult` with typed fields

**Tuning Prompts**:
- Administrators can update prompt templates in Dataverse playbook records
- See `projects/financial-intelligence-module-r1/notes/extraction-prompt-tuning-guide.md` for tuning methodology
- Recommend testing with sample invoices before deploying prompt changes

### Signal Thresholds

**Default thresholds** (configured in `FinanceOptions` or app settings):

| Signal Type | Default Threshold |
|-------------|-------------------|
| **Budget Exceeded** | 100% of allocated amount |
| **Budget Warning** | 80% of allocated amount |
| **Velocity Spike** | 50% MoM increase |
| **Anomaly Detection** | 2 standard deviations from mean |

**Customizing Thresholds**:
- Edit `appsettings.json` or Azure App Service Configuration
- Restart BFF API after changes
- Signal evaluation runs automatically after each snapshot generation

### Feature Flags

| Flag | Default | Purpose |
|------|---------|---------|
| `AutoClassifyAttachments` | ON | Automatically enqueue classification jobs when email attachments are created |
| `EnableInvoiceSearch` | ON | Enable semantic search endpoint |
| `EnableFinanceSummary` | ON | Enable finance summary endpoint with Redis caching |

**Toggling Feature Flags**:
- Managed via configuration provider (app settings or Azure App Configuration)
- Requires API restart if changed in `appsettings.json`

---

## Business Process Flow

### Overview

The Finance Intelligence pipeline consists of 7 stages that execute automatically once an invoice arrives via email.

```
Email Arrives
    ↓
[1] Email-to-Document Pipeline (existing)
    ↓
[2] AI Classification (Playbook A)
    ↓
[3] Human Review Queue
    ↓
[4] Confirmation/Rejection
    ↓
[5] AI Extraction (Playbook B)
    ↓
[6] Spend Snapshot Generation
    ↓
[7] Signal Evaluation & Indexing
    ↓
Finance Intelligence Panel (VisualHost)
```

### Stage 1: Email Ingestion

**What Happens**:
- Email arrives at configured inbox
- Email-to-Document pipeline processes email
- Creates `sprk_document` record with document type = Email
- Uploads email attachments to SPE (SharePoint Embedded)
- Creates child `sprk_document` records for each attachment (type = EmailAttachment)

**User Action**: None required (fully automated)

### Stage 2: AI Classification

**What Happens**:
- Background job (`AttachmentClassificationJobHandler`) picks up new EmailAttachment documents
- Sends document to gpt-4o-mini via Playbook A (Invoice Classification)
- AI returns:
  - **Classification**: InvoiceCandidate, Unknown, or NotInvoice
  - **Confidence**: 0.0 to 1.0 (0-100%)
  - **Invoice Hints**: Extracted metadata (vendor name, invoice number, amount, date)

**Populated Fields on `sprk_document`**:
- `sprk_classification` (choice field)
- `sprk_classificationconfidence` (decimal)
- `sprk_invoicevendorname` (text)
- `sprk_invoicenumber` (text)
- `sprk_invoiceamount` (money)
- `sprk_invoicedate` (date)

**User Action**: None required (fully automated)

**Typical Duration**: < 10 seconds per attachment

### Stage 3: Human Review Queue

**What Happens**:
- If classification = InvoiceCandidate or Unknown → Creates `sprk_invoice` record with status = ToReview
- Invoice appears in "Invoice Review Queue" Dataverse view
- Entity matching runs in background to suggest Matter and Vendor associations

**User Action**: Review candidates in queue (see [Reviewing Invoices](#reviewing-invoices) workflow)

### Stage 4: Confirmation or Rejection

**What Happens**:
- User clicks **Confirm** or **Reject** in review queue
- BFF API endpoint processes confirmation:
  - **Confirm**: Updates invoice status to Confirmed, enqueues extraction job
  - **Reject**: Updates invoice status to Rejected, retains record (never deleted)

**User Action**: Click Confirm or Reject button

**Typical Duration**: < 1 second

### Stage 5: AI Extraction

**What Happens**:
- Background job (`InvoiceExtractionJobHandler`) picks up newly confirmed invoices
- Sends invoice document to gpt-4o via Playbook B (Invoice Extraction)
- AI returns structured `InvoiceExtractionResult` with:
  - Invoice metadata (number, date, due date, amount, vendor)
  - Line items (description, quantity, rate, amount, timekeeper)
- Creates `sprk_billingevent` records for each line item
- Sets `VisibilityState = Invoiced` (deterministic, never AI-generated)

**User Action**: None required (fully automated)

**Typical Duration**: < 15 seconds per invoice

### Stage 6: Spend Snapshot Generation

**What Happens**:
- Background job (`SpendSnapshotGenerationJobHandler`) aggregates billing events
- Computes snapshots for each Matter/Project:
  - **Month**: Current month spend
  - **ToDate**: Year-to-date or project-to-date spend
- Calculates:
  - **Budget Variance**: Actual vs. allocated
  - **MoM Velocity**: Month-over-month growth rate
- Creates or updates `sprk_spendsnapshot` records (idempotent via alternate key)

**User Action**: None required (fully automated)

**Typical Duration**: < 5 seconds per matter

### Stage 7: Signal Evaluation & Indexing

**What Happens**:
- **Signal Evaluation**: `SpendSignalEvaluationService` checks snapshots against thresholds
  - Budget exceeded/warning
  - Velocity spike
  - Anomaly detection
  - Creates `sprk_spendsignal` records for breaches
- **Invoice Indexing**: `InvoiceIndexingJobHandler` indexes confirmed invoices in Azure AI Search
  - Contextual metadata enrichment (prepends Matter, Vendor, Budget context before vectorization)
  - Enables semantic search with "what I meant, not what I said" capability

**User Action**: None required (fully automated)

**Typical Duration**: < 3 seconds per signal evaluation, < 5 seconds per invoice indexing

### End-to-End Timeline

| Stage | Duration | Cumulative |
|-------|----------|------------|
| Email Ingestion | ~5s | 5s |
| Classification | ~10s | 15s |
| Human Review | Variable (hours to days) | — |
| Confirmation | ~1s | 16s |
| Extraction | ~15s | 31s |
| Snapshot Gen | ~5s | 36s |
| Signal Eval + Indexing | ~8s | 44s |

**Total automated processing time**: ~44 seconds (excluding human review)

---

## User Workflows

### Reviewing Invoices

**Accessing the Review Queue**:
1. Navigate to **Invoice Review Queue** view in Dataverse
2. View shows invoices with status = ToReview
3. Columns display:
   - Document Name (filename)
   - Classification Confidence
   - Suggested Matter (from entity matching)
   - Suggested Vendor (from entity matching)
   - Invoice Hints (vendor, number, amount, date)

**Reviewing an Invoice Candidate**:
1. Click on an invoice record to open
2. Review:
   - **Document Preview**: View attachment content (if available)
   - **Classification Hints**: AI-extracted metadata
   - **Suggested Associations**: Matter and Vendor matches
   - **Confidence Score**: AI classification confidence (0-100%)
3. Verify the document is actually an invoice
4. Confirm or correct Matter and Vendor associations

**Confirming an Invoice**:
1. Open invoice record in review queue
2. Click **Confirm Invoice** button (custom command or BFF API call)
3. Optionally adjust Matter and Vendor lookups
4. Confirm triggers extraction pipeline
5. Invoice status changes to Confirmed
6. Invoice disappears from review queue

**Rejecting a False Positive**:
1. Open invoice record in review queue
2. Click **Reject Invoice** button
3. Optionally add rejection reason (comment field)
4. Invoice status changes to Rejected
5. Invoice disappears from review queue but **remains in system** (not deleted)

**Best Practices**:
- Review queue daily to prevent backlog
- Prioritize high-confidence candidates (> 80%) for faster processing
- Use batch review for obvious invoices
- Flag systematic misclassifications for prompt tuning

### Using the Finance Intelligence Panel

**Accessing the Panel**:
1. Open any Matter or Project record
2. Scroll to **Finance Intelligence** section (typically in a tab or below main form)
3. Panel displays:
   - **Budget Utilization Gauge**: Visual indicator of % budget consumed
   - **Monthly Spend Timeline**: Bar chart showing spend by month
   - **Active Signals**: Alerts for budget breaches or velocity spikes

**Budget Utilization Gauge**:
- **Green (0-80%)**: On track
- **Yellow (80-100%)**: Warning threshold
- **Red (> 100%)**: Budget exceeded
- Hover for exact values (spent / allocated)

**Monthly Spend Timeline**:
- X-axis: Months (last 12 months)
- Y-axis: Spend amount
- Bar color: Green (under budget) / Yellow (approaching) / Red (over budget)
- Click bar for drill-down (if configured)

**Active Signals**:
- Listed below charts
- Shows:
  - Signal Type (Budget Exceeded, Velocity Spike, etc.)
  - Severity (High, Medium, Low)
  - Triggered Date
  - Description
- Click signal to view details

**Refreshing Data**:
- Panel data refreshes automatically when form loads
- Backend caches summary for 5 minutes (Redis)
- Manual refresh: Close and reopen Matter form

### Searching Invoices

**Using Semantic Search** (via BFF API):

**Endpoint**: `POST /api/finance/invoice-search`

**Example Request**:
```json
{
  "query": "Find all invoices from Smith & Associates over $10,000 in Q1 2026",
  "matterId": "d42f3a1b-8c7e-4f56-a3d1-9e2b8c7a6d5e",
  "top": 20
}
```

**Example Response**:
```json
{
  "results": [
    {
      "invoiceId": "...",
      "invoiceNumber": "INV-2026-001",
      "vendorName": "Smith & Associates",
      "totalAmount": 12500.00,
      "invoiceDate": "2026-01-15",
      "matterName": "Project Alpha Litigation",
      "relevanceScore": 0.92
    },
    ...
  ],
  "totalCount": 3
}
```

**Search Capabilities**:
- **Keyword Search**: Exact matches on invoice number, vendor name, matter name
- **Semantic Search**: Natural language queries ("invoices from last month", "find billing over budget")
- **Hybrid Search**: Combines keyword + vector + semantic ranking for best results
- **Contextual Metadata**: Results enriched with matter and budget context

**Search Tips**:
- Use natural language ("Find patent invoices over $50k")
- Reference time periods ("Q1 2026", "last 3 months")
- Include vendor names, matter names, or invoice numbers for better results
- Combine criteria ("Invoices from Vendor X on Matter Y over $10k")

### Viewing Spend Summary

**Using Finance Summary Endpoint** (via BFF API):

**Endpoint**: `GET /api/finance/summary?matterId={id}`

**Example Response**:
```json
{
  "matterId": "...",
  "budgetPlan": {
    "name": "FY2026 Outside Counsel Budget",
    "totalBudget": 500000.00,
    "allocated": 450000.00,
    "spent": 387250.00
  },
  "currentMonth": {
    "period": "2026-02",
    "spent": 42300.00,
    "budgetVariance": -8700.00,
    "percentOfBudget": 94.2
  },
  "toDate": {
    "spent": 387250.00,
    "budgetVariance": -62750.00,
    "percentOfBudget": 86.1
  },
  "velocity": {
    "monthOverMonth": 0.15
  },
  "activeSignals": [
    {
      "type": "BudgetWarning",
      "severity": "Medium",
      "description": "Matter approaching budget limit (86% utilized)"
    }
  ]
}
```

**Cached Data**:
- Summary data cached in Redis for 5 minutes
- Target response time: < 200ms from cache
- First request after cache expiration: ~1-2 seconds (recomputed)

---

## Finance Intelligence Panel

### Panel Components

The Finance Intelligence panel on Matter forms consists of 3 main components:

#### 1. Budget Utilization Gauge

**What It Shows**:
- Visual gauge displaying % of budget consumed
- Color-coded by threshold:
  - **Green (0-80%)**: Healthy spend
  - **Yellow (80-99%)**: Approaching limit
  - **Red (≥100%)**: Budget exceeded

**Data Source**:
- Denormalized field on Matter: `sprk_budgetutilizationpercent`
- Updated by SpendSnapshotGenerationJobHandler
- Reflects ToDate snapshot (year-to-date or project-to-date)

**When Updated**:
- After each invoice extraction completes
- When snapshot generation job runs
- Typically updates within 30 seconds of invoice confirmation

#### 2. Monthly Spend Timeline

**What It Shows**:
- Bar chart with last 12 months of spend
- X-axis: Month labels (e.g., "Jan 2026", "Feb 2026")
- Y-axis: Spend amount in currency
- Bars color-coded by budget status

**Data Source**:
- Month snapshots from `sprk_spendsnapshot` entity
- Aggregated via BFF API endpoint
- Denormalized snapshot on Matter: `sprk_monthlyspendtimeline` (JSON)

**When Updated**:
- After snapshot generation completes
- Recalculated when new billing events are created

#### 3. Active Signals

**What It Shows**:
- List of current spend alerts
- Signal types: Budget Exceeded, Budget Warning, Velocity Spike, Anomaly
- Severity indicators: High, Medium, Low
- Triggered date and description

**Data Source**:
- `sprk_spendsignal` entity filtered by active status
- Count denormalized on Matter: `sprk_activesignalcount`

**When Updated**:
- After signal evaluation runs (triggered by snapshot generation)
- Signal status can be manually resolved or auto-resolved on next evaluation

### Configuration

**Adding Panel to Matter Form**:
1. Open Form Editor for Matter entity
2. Add new section: "Finance Intelligence"
3. Insert 3 VisualHost controls:
   - **Control 1**: Budget Utilization Gauge
     - Data Source: `sprk_budgetutilizationpercent` (decimal field)
     - Visualization: Gauge chart
   - **Control 2**: Monthly Spend Timeline
     - Data Source: `sprk_monthlyspendtimeline` (JSON field)
     - Visualization: Column chart
   - **Control 3**: Active Signals
     - Data Source: Related `sprk_spendsignal` records
     - Visualization: List or grid
4. Save and publish form

**Denormalized Fields on Matter**:
| Field | Type | Purpose |
|-------|------|---------|
| `sprk_totalspendtodate` | Money | Total spent to date |
| `sprk_budgetutilizationpercent` | Decimal | % of budget used |
| `sprk_activesignalcount` | Integer | Count of active alerts |
| `sprk_monthlyspendcurrent` | Money | Current month spend |
| `sprk_momvelocity` | Decimal | Month-over-month growth rate |
| `sprk_monthlyspendtimeline` | Text (JSON) | 12-month history |

**Performance Considerations**:
- Denormalized fields provide instant form load (no API calls)
- Updated asynchronously by job handlers
- Trade-off: Slight delay (30s) vs. fast form rendering
- BFF API available for real-time queries if needed

---

## Understanding Metrics and Signals

### Spend Metrics

#### Budget Variance

**Definition**: Difference between actual spend and allocated budget

**Calculation**:
```
Budget Variance = Allocated Amount - Actual Spend
```

**Interpretation**:
- **Positive variance**: Under budget (good)
- **Negative variance**: Over budget (alert)
- **Zero variance**: Exactly on budget

**Example**:
- Allocated: $100,000
- Spent: $87,500
- Variance: $12,500 (12.5% under budget)

#### Month-over-Month Velocity

**Definition**: Rate of change in spend from previous month to current month

**Calculation**:
```
MoM Velocity = (Current Month Spend - Previous Month Spend) / Previous Month Spend
```

**Interpretation**:
- **Positive velocity**: Spend increasing
- **Negative velocity**: Spend decreasing
- **> 0.5 (50%)**: Potential velocity spike signal

**Example**:
- Previous Month: $20,000
- Current Month: $35,000
- Velocity: +75% (spike alert)

#### Budget Utilization Percentage

**Definition**: Percentage of allocated budget consumed

**Calculation**:
```
Utilization % = (Actual Spend / Allocated Amount) × 100
```

**Thresholds**:
- **0-80%**: Healthy (green)
- **80-99%**: Warning (yellow)
- **≥100%**: Exceeded (red)

### Signal Types

#### 1. Budget Exceeded

**Trigger**: Actual spend ≥ 100% of allocated budget

**Severity**: High

**Action Required**:
- Review recent invoices for accuracy
- Request budget increase if justified
- Implement spend controls on matter
- Communicate with stakeholders

**Example Signal**:
```
Signal Type: Budget Exceeded
Severity: High
Description: Matter "Project Alpha" has exceeded budget by $12,500 (112% utilized)
Triggered: 2026-02-15
Status: Active
```

#### 2. Budget Warning

**Trigger**: Actual spend ≥ 80% of allocated budget (but < 100%)

**Severity**: Medium

**Action Required**:
- Monitor spend closely
- Forecast remaining work cost
- Prepare budget adjustment request if needed
- Alert matter manager

**Example Signal**:
```
Signal Type: Budget Warning
Severity: Medium
Description: Matter "Project Beta" approaching budget limit (87% utilized)
Triggered: 2026-02-10
Status: Active
```

#### 3. Velocity Spike

**Trigger**: Month-over-month spend increase ≥ 50%

**Severity**: Medium

**Action Required**:
- Investigate cause of spike (litigation phase change, increased activity)
- Verify invoices are legitimate
- Forecast impact on budget
- Communicate to finance team

**Example Signal**:
```
Signal Type: Velocity Spike
Severity: Medium
Description: Matter "Project Gamma" spend increased 75% MoM ($20k → $35k)
Triggered: 2026-02-01
Status: Active
```

#### 4. Anomaly Detection

**Trigger**: Spend > 2 standard deviations from rolling mean

**Severity**: Low to Medium

**Action Required**:
- Review for data entry errors
- Validate invoice amounts
- Check for duplicate billing events
- Investigate if legitimate spike

**Example Signal**:
```
Signal Type: Anomaly
Severity: Low
Description: Matter "Project Delta" spend significantly above historical average
Triggered: 2026-02-05
Status: Active
```

### Signal Lifecycle

**States**:
1. **Active**: Signal triggered and unresolved
2. **Acknowledged**: User acknowledged but not resolved
3. **Resolved**: Condition corrected (spend reduced, budget increased)
4. **Auto-Resolved**: Next evaluation shows condition cleared

**Automatic Resolution**:
- Signal evaluation runs after each snapshot generation
- If trigger condition no longer met → Signal status = Auto-Resolved

**Manual Resolution**:
- User can manually mark signal as Acknowledged or Resolved
- Add resolution notes (e.g., "Budget increase approved")

---

## Important User Considerations

### Classification Confidence Thresholds

**Understanding Confidence Scores**:
- AI returns confidence value: 0.0 to 1.0 (0-100%)
- Higher confidence = higher certainty of classification

**Confidence Bands**:
| Confidence | Band | Interpretation | Action |
|------------|------|----------------|--------|
| 90-100% | Very High | Almost certainly correct | Fast-track confirmation |
| 75-89% | High | Likely correct | Standard review |
| 50-74% | Medium | Uncertain | Detailed review required |
| 0-49% | Low | Likely incorrect | Mark as Unknown or reject |

**When to Tune Prompts**:
- If many high-confidence false positives → Prompt too aggressive
- If many low-confidence true positives → Prompt too conservative
- Target: ≥80% confidence for 90%+ of invoices

### Review Queue Best Practices

**Daily Review**:
- Check queue daily to prevent backlog
- Aim for < 24-hour turnaround on invoice confirmation

**Batch Processing**:
- Sort by confidence score (descending)
- Process high-confidence invoices first
- Flag uncertain cases for manual review

**Entity Matching**:
- Entity matching suggestions are probabilistic (not 100% accurate)
- Always verify Matter and Vendor associations
- Override suggestions if incorrect

**Rejection Handling**:
- Rejected invoices are **never deleted** (audit trail)
- Review rejection reasons periodically to identify systematic issues
- Use rejection patterns to improve classification prompts

### Budget Planning Requirements

**For Spend Tracking to Work**:
1. **Budget Plan must exist**: Create Budget Plan record
2. **Budget Buckets must be allocated**: At least one bucket with allocated amount
3. **Budget Plan linked to Matter**: Matter.BudgetPlan lookup must be set
4. **Budget amounts > 0**: Cannot track variance without baseline

**Without Budget**:
- Spend snapshots still generated (actual spend tracked)
- Budget variance = null
- Budget utilization = null
- No budget-related signals

**Partial Budgets**:
- If Budget Plan exists but no buckets → Total budget used
- If some matters have budgets, others don't → Only budgeted matters get variance tracking

### Visibility State Meanings

`VisibilityState` is a choice field on `sprk_billingevent` that indicates invoice workflow state.

| Value | Meaning | Set By |
|-------|---------|--------|
| **Invoiced** | Invoice received and extracted | Extraction job (deterministic) |
| **Approved** | Invoice approved for payment | Future enhancement |
| **Paid** | Invoice paid by finance | Future enhancement |

**Important**:
- VisibilityState is **NEVER set by AI** (prevents hallucination)
- Always set deterministically in code
- Currently only "Invoiced" is used in R1

**Out of Scope (R1)**:
- InternalWIP, PreBill (firm-side states)
- Approved, Paid (payment processing states)

### Data Retention and Archival

**Retention Policies**:
- **Invoices**: Retained indefinitely (legal/audit requirements)
- **Billing Events**: Retained indefinitely
- **Snapshots**: Retained for 24 months (configurable)
- **Signals**: Active signals retained indefinitely; resolved signals archived after 12 months
- **Documents**: Retained per Email-to-Document policy

**Deleting Invoices**:
- Rejected invoices are **never deleted** (audit trail)
- Confirmed invoices **cannot be deleted** after extraction (billing events exist)
- To remove an invoice: Must delete associated billing events first (admin operation)

---

## Troubleshooting

### Common Issues

#### Issue 1: Invoices Not Appearing in Review Queue

**Symptoms**:
- Email arrives with invoice attachment
- No invoice candidate appears in review queue

**Possible Causes**:
1. `AutoClassifyAttachments` feature flag is OFF
2. Attachment classified as NotInvoice (too low confidence)
3. Classification job failed (check logs)
4. Email-to-Document pipeline not creating attachments

**Troubleshooting Steps**:
1. Check feature flag: `AutoClassifyAttachments = true`
2. Check `sprk_document` record for attachment:
   - Document Type = EmailAttachment
   - Classification field populated
   - If Classification = NotInvoice → Expected behavior (not added to queue)
3. Check BFF API logs for classification job execution
4. Verify Email-to-Document pipeline is operational

**Resolution**:
- If feature flag OFF: Enable in configuration, restart API
- If classification incorrect: Review classification prompt (Playbook A) for tuning
- If job failed: Check logs for errors (API timeout, Azure OpenAI throttling, etc.)

#### Issue 2: Extraction Fails After Confirmation

**Symptoms**:
- User confirms invoice
- Invoice status = Confirmed
- No billing events created

**Possible Causes**:
1. Extraction job failed (AI timeout, throttling, prompt error)
2. Document file not accessible (SPE permission issue)
3. Extraction prompt error (JSON schema mismatch)

**Troubleshooting Steps**:
1. Check BFF API logs for `InvoiceExtractionJobHandler` errors
2. Check Azure OpenAI logs for throttling (429 errors)
3. Verify document file is accessible (SPE FileStore)
4. Check if invoice is actually in a parseable format (PDF with text, not scanned image)

**Resolution**:
- If AI throttled: Implement retry with exponential backoff (or increase quota)
- If document inaccessible: Check SPE permissions, verify container exists
- If prompt error: Review extraction prompt schema vs. `InvoiceExtractionResult` C# type
- Manual workaround: Re-enqueue extraction job via admin operation

#### Issue 3: Finance Panel Not Updating

**Symptoms**:
- Invoice confirmed and extracted
- Finance panel still shows old spend data

**Possible Causes**:
1. Snapshot generation job not triggered
2. Denormalized fields on Matter not updated
3. Redis cache stale (summary endpoint)
4. Form not refreshed after update

**Troubleshooting Steps**:
1. Check if snapshot generation job executed (logs)
2. Check `sprk_spendsnapshot` records for Matter (should have updated `ToDate` snapshot)
3. Check denormalized fields on Matter record (`sprk_totalspendtodate`, `sprk_budgetutilizationpercent`)
4. Close and reopen Matter form (force refresh)
5. Check Redis cache TTL (default 5 minutes)

**Resolution**:
- If job didn't run: Check job handler registration, background service running
- If snapshots exist but fields not updated: Check Matter update logic in snapshot job handler
- If cache stale: Wait 5 minutes for expiration, or manually flush Redis cache
- Manual workaround: Trigger snapshot regeneration via admin endpoint

#### Issue 4: Budget Variance Incorrect

**Symptoms**:
- Budget variance calculation seems wrong
- Utilization percentage doesn't match expected value

**Possible Causes**:
1. Budget Plan or Budget Bucket amounts incorrect
2. Multiple Budget Buckets assigned to same Matter (conflicting allocations)
3. Billing events associated with wrong Matter
4. Snapshot aggregation logic error

**Troubleshooting Steps**:
1. Verify Budget Plan and Budget Bucket amounts:
   - Budget Plan.TotalBudget
   - Sum of all Budget Bucket.AllocatedAmount
2. Check if multiple buckets linked to same Matter (should only be one)
3. Verify billing events:
   - Check `sprk_billingevent.sprk_matterid` or `sprk_projectid`
   - Sum amounts manually and compare to snapshot
4. Check snapshot record:
   - `sprk_spendsnapshot.sprk_actualspend`
   - `sprk_spendsnapshot.sprk_budgetvariance`
   - `sprk_spendsnapshot.sprk_allocatedamount`

**Resolution**:
- If budget amounts wrong: Correct Budget Plan/Bucket records, regenerate snapshots
- If multiple buckets: Remove duplicates, keep only one bucket per Matter
- If billing events wrong: Correct Matter associations, regenerate snapshots
- Manual workaround: Trigger snapshot regeneration after fixing data

#### Issue 5: Signals Not Triggering

**Symptoms**:
- Spend exceeds budget but no Budget Exceeded signal
- Spend velocity spike but no Velocity Spike signal

**Possible Causes**:
1. Signal evaluation job not running
2. Signal thresholds misconfigured
3. Snapshot data incomplete (no previous month for velocity)

**Troubleshooting Steps**:
1. Check if `SpendSignalEvaluationService` executed (logs)
2. Check signal thresholds in configuration (`FinanceOptions`)
3. Verify snapshot records exist:
   - Current Month snapshot
   - ToDate snapshot
   - Previous Month snapshot (for velocity)
4. Check `sprk_spendsignal` table for existing signals

**Resolution**:
- If job not running: Check registration, background service status
- If thresholds wrong: Update configuration, restart API
- If snapshot data incomplete: Wait for next month cycle, or regenerate historical snapshots
- Manual workaround: Manually create signal record (admin operation)

---

## Glossary

### Terms

| Term | Definition |
|------|------------|
| **Alternate Key** | Unique composite key on Dataverse entity used for idempotent upsert operations |
| **Billing Event** | A single line item from an invoice (time entry, expense, fee) |
| **Budget Bucket** | Allocation of budget to a specific category or matter |
| **Budget Plan** | Overall spend plan with total budget and fiscal period |
| **Budget Variance** | Difference between allocated budget and actual spend |
| **Classification** | AI categorization of attachment (Invoice Candidate, Unknown, Not Invoice) |
| **Confidence Score** | AI certainty level (0.0 to 1.0) for classification |
| **Contextual Metadata Enrichment** | Prepending Matter/Vendor/Budget context to text before vectorization for better semantic search |
| **Denormalized Fields** | Pre-computed fields stored directly on entity for fast form rendering |
| **Entity Matching** | Probabilistic matching of invoices to Matters and Vendors using multiple signals |
| **Extraction** | AI-powered parsing of invoice into structured data (line items, amounts, dates) |
| **Idempotency** | Property where repeated operations produce same result (prevents duplicates) |
| **Invoice Candidate** | Attachment classified as likely invoice, pending human review |
| **Invoice Hints** | AI-extracted metadata during classification (vendor, number, amount, date) |
| **MoM Velocity** | Month-over-month spend growth rate |
| **Playbook** | AI prompt template stored in Dataverse with model configuration |
| **Semantic Search** | Natural language search that understands intent, not just keywords |
| **Snapshot** | Pre-computed spend summary for a time period (Month or ToDate) |
| **SPE** | SharePoint Embedded (document storage for email attachments) |
| **Spend Signal** | Automated alert triggered by threshold breach (budget, velocity, anomaly) |
| **Structured Output** | AI response format constrained to JSON schema via `response_format: json_schema` |
| **VisibilityState** | Workflow state of billing event (Invoiced, Approved, Paid) |
| **VisualHost** | Power Platform native charting framework using Dataverse data |

### Entity Reference

| Entity | Logical Name | Purpose |
|--------|--------------|---------|
| **Invoice** | `sprk_invoice` | Invoice record (metadata + document link) |
| **Billing Event** | `sprk_billingevent` | Individual line item from invoice |
| **Budget Plan** | `sprk_budgetplan` | Overall budget with fiscal period |
| **Budget Bucket** | `sprk_budgetbucket` | Allocation of budget to category/matter |
| **Spend Snapshot** | `sprk_spendsnapshot` | Pre-computed spend summary |
| **Spend Signal** | `sprk_spendsignal` | Threshold breach alert |
| **Document** | `sprk_document` | Email or attachment with SPE file |
| **Matter** | `sprk_matter` | Legal matter (client-specific) |
| **Project** | `sprk_project` | Internal project (no client) |

### Field Reference (sprk_document)

Classification-related fields added in Task 002:

| Field | Logical Name | Type | Purpose |
|-------|--------------|------|---------|
| **Classification** | `sprk_classification` | Choice | Invoice Candidate / Unknown / Not Invoice |
| **Classification Confidence** | `sprk_classificationconfidence` | Decimal | AI confidence (0.0 to 1.0) |
| **Classification Date** | `sprk_classificationdate` | DateTime | When classification occurred |
| **Classification Source** | `sprk_classificationsource` | Text | Which AI playbook was used |
| **Invoice Vendor Name** | `sprk_invoicevendorname` | Text | Extracted vendor name |
| **Invoice Number** | `sprk_invoicenumber` | Text | Extracted invoice number |
| **Invoice Amount** | `sprk_invoiceamount` | Money | Extracted total amount |
| **Invoice Date** | `sprk_invoicedate` | Date | Extracted invoice date |
| **Review Status** | `sprk_reviewstatus` | Choice | Pending Review / Reviewed / Skipped |
| **Reviewed By** | `sprk_reviewedby` | Lookup(User) | User who reviewed |
| **Reviewed Date** | `sprk_revieweddate` | DateTime | When review occurred |
| **Review Notes** | `sprk_reviewnotes` | Text (multiline) | User notes from review |
| **Invoice ID** | `sprk_invoiceid` | Lookup(Invoice) | Link to confirmed invoice |

### API Endpoint Reference

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/finance/invoice-confirm` | POST | Confirm invoice candidate |
| `/api/finance/invoice-reject` | POST | Reject invoice candidate |
| `/api/finance/summary` | GET | Get cached spend summary for matter |
| `/api/finance/invoice-search` | POST | Semantic search for invoices |

---

## Appendices

### Appendix A: Sample Playbook Prompts

**Classification Prompt (Playbook A - gpt-4o-mini)**:

```
You are an expert legal invoice classifier. Analyze the following document and determine if it is an invoice.

**Document**: {filename}
**Sender**: {sender_email}
**Received**: {received_date}

**Instructions**:
1. Classify the document as one of:
   - InvoiceCandidate: Likely a legal invoice (law firm bill, legal services invoice)
   - Unknown: Cannot determine (unclear, ambiguous)
   - NotInvoice: Definitely not an invoice (correspondence, contract, memo)
2. Provide a confidence score (0.0 to 1.0)
3. If InvoiceCandidate or Unknown, extract these hints:
   - Vendor name
   - Invoice number
   - Total amount
   - Invoice date

**Response Format** (JSON):
{
  "classification": "InvoiceCandidate",
  "confidence": 0.95,
  "invoiceHints": {
    "vendorName": "Smith & Associates LLP",
    "invoiceNumber": "INV-2026-001",
    "totalAmount": 12500.00,
    "invoiceDate": "2026-01-15"
  }
}
```

**Extraction Prompt (Playbook B - gpt-4o)**:

```
You are an expert legal invoice parser. Extract structured billing data from the following invoice.

**Invoice Document**: {document_content}

**Instructions**:
1. Extract invoice metadata: number, date, due date, total amount, vendor
2. Extract all line items with: description, quantity, rate, amount, timekeeper
3. Identify billing period start and end dates
4. Normalize all monetary values to decimal format

**Response Format** (JSON, constrained by schema):
{
  "invoiceNumber": "INV-2026-001",
  "invoiceDate": "2026-01-15",
  "dueDate": "2026-02-14",
  "totalAmount": 12500.00,
  "vendorName": "Smith & Associates LLP",
  "billingPeriodStart": "2026-01-01",
  "billingPeriodEnd": "2026-01-31",
  "lineItems": [
    {
      "description": "Legal research - patent prior art analysis",
      "quantity": 5.0,
      "rate": 450.00,
      "amount": 2250.00,
      "timekeeper": "John Doe, Partner"
    },
    ...
  ]
}
```

### Appendix B: Default Signal Thresholds

Configured in `appsettings.json` under `FinanceOptions`:

```json
{
  "FinanceOptions": {
    "SignalThresholds": {
      "BudgetExceededPercent": 100,
      "BudgetWarningPercent": 80,
      "VelocitySpikePercent": 50,
      "AnomalyStdDevMultiplier": 2.0
    },
    "SnapshotRetentionMonths": 24,
    "SignalRetentionMonths": 12,
    "CacheDurationMinutes": 5
  }
}
```

### Appendix C: VisibilityState Values

| Value | Display Name | Description | Set By |
|-------|--------------|-------------|--------|
| **Invoiced** | Invoiced | Invoice received and extracted into billing events | Extraction job |
| **Approved** | Approved for Payment | Invoice approved by authorized user (future) | Manual or workflow |
| **Paid** | Paid | Invoice payment processed by finance (future) | Integration with AP system |

**Future Enhancements (Out of Scope for R1)**:
- InternalWIP: Firm's work in progress (not yet billed)
- PreBill: Draft invoice pending review
- Approval workflow integration
- Payment processing integration

---

**End of Finance Intelligence User Guide**

For technical architecture details, see `docs/architecture/finance-intelligence-architecture.md`.

For implementation details, see `projects/financial-intelligence-module-r1/README.md`.
