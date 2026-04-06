# Field Mapping Reference

> **Last Updated**: April 5, 2026
> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: New
> **Solution**: Spaarke (Dataverse)

---

## Purpose

Consolidated quick-reference of custom field logical names, display names, types, max lengths, and option set values across all Spaarke entities. Organized by entity, focused on fields most commonly used in FetchXML/WebAPI queries.

For full entity metadata dumps, see the individual `sprk_*.md` files in this directory.

---

## Naming Conventions

| Pattern | Example | Notes |
|---------|---------|-------|
| Primary key | `sprk_invoiceid` | `Uniqueidentifier`, has "Id" suffix |
| Lookup field | `sprk_matter` | **No "Id" suffix** -- returns `EntityReference` |
| Virtual (name helper) | `sprk_budgetstatusName` | Read-only, auto-populated from Choice/Lookup |
| Base currency | `sprk_amount_Base` | Auto-calculated base currency equivalent |

**Critical rule**: Lookup fields do NOT include an "id" suffix. `sprk_matter` is the lookup to Matter, while `sprk_matterid` is Matter's own primary key. See `schema-corrections.md` for details.

---

## Core Domain

### Matter (`sprk_matter`)

| Logical Name | Display Name | Type | Max Length / Options |
|---|---|---|---|
| `sprk_matterid` | Matter | Uniqueidentifier | PK |
| `sprk_mattername` | Matter Name | Text | 100 |
| `sprk_matternumber` | Matter Number | Text | 100 |
| `sprk_mattertitle` | Matter Title | Text | 1000 |
| `sprk_matterdescription` | Matter Description | Multiline Text | 4000 |
| `sprk_mattertype` | Matter Type | Lookup | -> `sprk_mattertype_ref` |
| `sprk_containerid` | Container Id | Text | 100 |
| `sprk_assignedoutsidecounsel` | Assigned Outside Counsel | Lookup | -> `sprk_organization` |
| `sprk_chartdefinition` | Chart Definition | Lookup | -> `sprk_chartdefinition` |
| `sprk_regardingrecordtype` | Regarding Record Type | Lookup | -> `sprk_recordtype_ref` |
| `sprk_totalbudget` | Total Budget | Currency | Precision: 2 |
| `sprk_remainingbudget` | Remaining Budget | Currency | Precision: 2 |
| `sprk_totalspendtodate` | Total Spend to Date | Currency | Precision: 2 |
| `sprk_monthlyspendcurrent` | Monthly Spend Current | Currency | Precision: 2 |
| `sprk_monthlyspendtimeline` | Monthly Spend Timeline | Multiline Text | 10000 (JSON) |
| `sprk_monthovermonthvelocity` | Month Over Month Velocity | Decimal | Precision: 2 |
| `sprk_budgetutilizationpercent` | Budget Utilization Percent | Decimal | Precision: 2 |
| `sprk_averageinvoiceamount` | Average Invoice Amount | Currency | Precision: 2 |
| `sprk_invoicecount` | Invoice Count | Whole number | |
| `sprk_activesignalcount` | Active Signal Count | Whole number | |
| `sprk_budgetcompliancegrade_average` | Budget Compliance Grade Average | Decimal | |
| `sprk_budgetcompliancegrade_current` | Budget Compliance Grade Current | Decimal | |
| `sprk_guidelinecompliancegrade_average` | Guideline Compliance Grade Average | Decimal | |
| `sprk_guidelinecompliancegrade_current` | Guideline Compliance Grade Current | Decimal | |
| `sprk_outcomecompliancegrade_average` | Outcome Compliance Grade Average | Decimal | |
| `sprk_outcomecompliancegrade_current` | Outcome Compliance Grade Current | Decimal | |

### Project (`sprk_project`)

| Logical Name | Display Name | Type | Max Length / Options |
|---|---|---|---|
| `sprk_projectid` | Project | Uniqueidentifier | PK |
| `sprk_projectnumber` | Project Number | Text | 850 |
| `sprk_containerid` | Container Id | Text | 100 |
| `sprk_totalbudget` | Total Budget | Currency | Precision: 2 |
| `sprk_remainingbudget` | Remaining Budget | Currency | Precision: 2 |
| `sprk_totalspendtodate` | Total Spend to Date | Currency | Precision: 2 |
| `sprk_monthlyspendcurrent` | Monthly Spend Current | Currency | Precision: 2 |
| `sprk_monthlyspendtimeline` | Monthly Spend Timeline | Multiline Text | 10000 (JSON) |
| `sprk_monthovermonthvelocity` | Month Over Month Velocity | Decimal | Precision: 2 |
| `sprk_budgetutilizationpercent` | Budget Utilization Percent | Decimal | Precision: 2 |
| `sprk_averageinvoiceamount` | Average Invoice Amount | Currency | Precision: 2 |
| `sprk_invoicecount` | Invoice Count | Whole number | |
| `sprk_activesignalcount` | Active Signal Count | Whole number | |

### Matter Type (`sprk_mattertype_ref`)

| Logical Name | Display Name | Type | Max Length / Options |
|---|---|---|---|
| `sprk_mattertype_refid` | Matter Type | Uniqueidentifier | PK |
| `sprk_mattertypecode` | Matter Type Code | Text | |
| `sprk_mattertypename` | Matter Type Name | Text | |

### Matter Subtype (`sprk_mattersubtype_ref`)

| Logical Name | Display Name | Type | Max Length / Options |
|---|---|---|---|
| `sprk_mattersubtype_refid` | Matter Subtype | Uniqueidentifier | PK |
| `sprk_mattersubtypecode` | Matter Subtype Code | Text | |
| `sprk_mattersubtypename` | Matter Subtype Name | Text | |

---

## Document Domain

### Document (`sprk_document`)

| Logical Name | Display Name | Type | Max Length / Options |
|---|---|---|---|
| `sprk_documentid` | Document | Uniqueidentifier | PK |
| `sprk_documentname` | Document Name | Text | 850 |
| `sprk_documentdescription` | Document Description | Multiline Text | 4000 |
| `sprk_documentstatus` | Document Status | Choice | 0: Draft, 1: Working, 2: In Review, 3: Approved Final, 4: Rejected Final, 5: Replaced Final, 6: Archived |
| `sprk_documenttype` | Document Type | Choice | 100000000: Contract, 100000001: Invoice, 100000002: Proposal, 100000003: Report, 100000004: Letter, 100000005: Memo, 100000006: Email, 100000007: Agreement, 100000008: Statement, 100000009: Other |
| `sprk_driveitemid` | Drive Item Id | Text | 1000 |
| `sprk_containerid` | Container Id | Text | 500 |
| `sprk_containername` | Container Name | Lookup | -> `sprk_container` |
| `sprk_currentversionid` | Current Version Id | Lookup | -> `sprk_fileversion` |
| `sprk_createddatetime` | Created Date Time | DateTime | DateAndTime |
| `sprk_attachments` | Attachments | Multiline Text | 10000 (JSON) |
| `sprk_classification` | Classification | Choice | 100000000: InvoiceCandidate, 100000001: NotInvoice, 100000002: Unknown |
| `sprk_classificationconfidence` | Classification Confidence | Decimal | Precision: 4, range 0..1 |
| `sprk_classificationdate` | Classification Date | DateTime | DateAndTime |
| `sprk_classificationsource` | Classification Source | Text | 100 |
| `sprk_emailfrom` | Email From | Text | 500 |
| `sprk_emailto` | Email To | Text | 500 |
| `sprk_emailsubject` | Email Subject | Text | 1000 |
| `sprk_emaildate` | Email Date | DateTime | DateAndTime |
| `sprk_emaildirection` | Email Direction | Choice | 100000000: Received, 100000001: Sent |
| `sprk_emailbody` | Email Body | Multiline Text | 10000 |
| `sprk_emailmessageid` | Email Message Id | Text | 1000 |
| `sprk_emailparentid` | Email Parent Id | Text | 1000 |
| `sprk_emailconversationindex` | Email Conversation Index | Text | 1000 |
| `sprk_checkedoutby` | Checked Out By | Lookup | -> `systemuser` |
| `sprk_checkedoutdate` | Checked Out Date | DateTime | DateAndTime |
| `sprk_checkedinby` | Checked In By | Lookup | -> `systemuser` |
| `sprk_checkedindate` | Checked In Date | DateTime | DateAndTime |

---

## Financial Domain

### Invoice (`sprk_invoice`)

| Logical Name | Display Name | Type | Max Length / Options |
|---|---|---|---|
| `sprk_invoiceid` | Invoice | Uniqueidentifier | PK |
| `sprk_name` | Name | Text | 850 |
| `sprk_invoicenumber` | Invoice Number | Text | 100 |
| `sprk_invoicedate` | Invoice Date | DateTime | DateOnly |
| `sprk_totalamount` | Total Amount | Currency | Precision: 2, max 1B |
| `sprk_matter` | Matter | Lookup | -> `sprk_matter` |
| `sprk_project` | Project | Lookup | -> `sprk_project` |
| `sprk_document` | Source Document | Lookup | -> `sprk_document` |
| `sprk_vendororg` | Vendor Organization | Lookup | -> `sprk_organization` |
| `sprk_regardingrecordtype` | Regarding Record Type | Lookup | -> `sprk_recordtype_ref` |
| `sprk_invoicestatus` | Invoice Status | Choice | 100000000: ToReview, 100000001: Reviewed |
| `sprk_extractionstatus` | Extraction Status | Choice | 100000000: NotRun, 100000001: Extracted, 100000002: Failed |
| `sprk_extractedjson` | Extracted JSON | Multiline Text | 20000 (JSON) |
| `sprk_aisummary` | AI Summary | Multiline Text | 5000 |
| `sprk_confidence` | Confidence | Decimal | Precision: 2 |
| `sprk_currency` | Currency | Text | 10 (ISO 4217) |
| `sprk_correlationid` | Correlation ID | Text | 100 |
| `sprk_visibilitystate` | Visibility State | Choice | 100000000: Invoiced, 100000001: InternalWIP, 100000002: PreBill, 100000003: Paid, 100000004: WrittenOff, 100000005: Approved |

### Billing Event (`sprk_billingevent`)

| Logical Name | Display Name | Type | Max Length / Options |
|---|---|---|---|
| `sprk_billingeventid` | Billing Event | Uniqueidentifier | PK |
| `sprk_name` | Name | Text | 850 |
| `sprk_invoice` | Invoice | Lookup | -> `sprk_invoice` |
| `sprk_matter` | Matter | Lookup | -> `sprk_matter` (denormalized) |
| `sprk_project` | Project | Lookup | -> `sprk_project` (denormalized) |
| `sprk_vendororg` | Vendor Organization | Lookup | -> `sprk_organization` (denormalized) |
| `sprk_linesequence` | Line Sequence | Whole number | Min: 1 |
| `sprk_description` | Description | Multiline Text | 2000 |
| `sprk_amount` | Amount | Currency | Precision: 2, max 1B |
| `sprk_quantity` | Quantity | Decimal | Precision: 2 |
| `sprk_rate` | Rate | Currency | Precision: 2 |
| `sprk_timekeeper` | Timekeeper | Text | 100 |
| `sprk_timekeeperrole` | Timekeeper Role | Choice | 100000000: Senior Partner, 100000001: Partner, 100000002: Senior Associate, 100000003: Associate, 100000004: Paralegal, 100000005: Specialist, 100000006: Other |
| `sprk_roleclass` | Role Class | Text | 100 |
| `sprk_eventdate` | Event Date | DateTime | DateOnly |
| `sprk_costtype` | Cost Type | Choice | 100000000: Fee, 100000001: Expense |
| `sprk_visibilitystate` | Visibility State | Choice | (same as Invoice) |
| `sprk_currency` | Currency | Text | 10 (ISO 4217) |
| `sprk_correlationid` | Correlation ID | Text | 100 |

### Budget (`sprk_budget`)

| Logical Name | Display Name | Type | Max Length / Options |
|---|---|---|---|
| `sprk_budgetid` | Budget | Uniqueidentifier | PK |
| `sprk_name` | Name | Text | 850 |
| `sprk_matter` | Matter | Lookup | -> `sprk_matter` |
| `sprk_project` | Project | Lookup | -> `sprk_project` |
| `sprk_totalbudget` | Total Budget | Currency | Precision: 2, max 1B |
| `sprk_budgetyear` | Budget Year | Text | 4 |
| `sprk_budgetstartdate` | Budget Start Date | DateTime | DateOnly |
| `sprk_budgetenddate` | Budget End Date | DateTime | DateOnly |
| `sprk_budgetstatus` | Budget Status | Choice | 0: Draft, 1: Pending, 2: Open, 3: Completed, 4: Closed, 5: On Hold, 6: Cancelled, 7: Archived |
| `sprk_budgetperiod` | Budget Period | Choice | 0: Annual, 1: Quarter 1, 2: Quarter 2, 3: Quarter 3, 4: Quarter 4 |
| `sprk_budgetcategory` | Budget Category | Choice | 100000000-100000004: Budget Category 0-4 |
| `sprk_currency` | Currency | Text | 10 (ISO 4217) |

### Budget Bucket (`sprk_budgetbucket`)

| Logical Name | Display Name | Type | Max Length / Options |
|---|---|---|---|
| `sprk_budgetbucketid` | Budget Bucket | Uniqueidentifier | PK |
| `sprk_name` | Name | Text | 850 |
| `sprk_budget` | Budget | Lookup | -> `sprk_budget` |
| `sprk_amount` | Amount | Currency | Precision: 2, max 1B |
| `sprk_bucketkey` | Bucket Key | Text | 100 |
| `sprk_budgetcategory` | Budget Category | Choice | (same as Budget) |
| `sprk_periodstart` | Period Start | DateTime | DateOnly |
| `sprk_periodend` | Period End | DateTime | DateOnly |

### KPI Assessment (`sprk_kpiassessment`)

| Logical Name | Display Name | Type | Max Length / Options |
|---|---|---|---|
| `sprk_kpiassessmentid` | KPI Assessment | Uniqueidentifier | PK |
| `sprk_kpiname` | KPI Name | Text | 850 |
| `sprk_matter` | Matter | Lookup | -> `sprk_matter` |
| `sprk_project` | Project | Lookup | -> `sprk_project` |
| `sprk_kpigradescore` | Grade | Choice | 100000000: A+, 100000001: A, 100000002: B+, 100000003: B, 100000004: C+, 100000005: C, 100000006: D+, 100000007: D, 100000008: F, 100000009: No Grade |
| `sprk_performancearea` | Performance Area | Choice | 100000000: Guideline Compliance, 100000001: Budget Compliance, 100000002: Outcomes Achievement |
| `sprk_assessmentcriteria` | Assessment Criteria | Multiline Text | 10000 |
| `sprk_assessmentnotes` | Assessment Notes | Multiline Text | 10000 |

### Spend Snapshot (`sprk_spendsnapshot`)

| Logical Name | Display Name | Type | Max Length / Options |
|---|---|---|---|
| `sprk_spendsnapshotid` | Spend Snapshot | Uniqueidentifier | PK |
| `sprk_name` | Name | Text | 850 |
| `sprk_matter` | Matter | Lookup | -> `sprk_matter` |
| `sprk_project` | Project | Lookup | -> `sprk_project` |
| `sprk_snapshotperiod` | Snapshot Period | Choice | Month, ToDate |
| `sprk_periodvalue` | Period Value | Text | e.g. "2026-02" |
| `sprk_generatedat` | Generated At | DateTime | |
| `sprk_invoicedamount` | Invoiced Amount | Currency | |
| `sprk_allocatedamount` | Allocated Amount | Currency | |
| `sprk_budgetamount` | Budget Amount | Currency | |
| `sprk_budgetvariance` | Budget Variance | Currency | |
| `sprk_budgetvariancepct` | Budget Variance Pct | Decimal | |
| `sprk_momvelocity` | MoM Velocity | Decimal | |
| `sprk_bucketkey` | Bucket Key | Text | |
| `sprk_correlationid` | Correlation ID | Text | |

### Spend Signal (`sprk_spendsignal`)

| Logical Name | Display Name | Type | Max Length / Options |
|---|---|---|---|
| `sprk_spendsignalid` | Spend Signal | Uniqueidentifier | PK |
| `sprk_name` | Name | Text | 850 |
| `sprk_matter` | Matter | Lookup | -> `sprk_matter` |
| `sprk_project` | Project | Lookup | -> `sprk_project` |
| `sprk_snapshot` | Snapshot | Lookup | -> `sprk_spendsnapshot` |
| `sprk_signaltype` | Signal Type | Choice | BudgetExceeded, BudgetWarning, VelocitySpike, AnomalyDetected |
| `sprk_severity` | Severity | Choice | Info, Warning, Critical |
| `sprk_message` | Message | Text | 500 |
| `sprk_isactive` | Is Active | Boolean | |
| `sprk_spendsignalstatus` | Status | Choice | Active, Acknowledged, Resolved, AutoResolved |
| `sprk_resolutionnotes` | Resolution Notes | Multiline Text | 5000 |

---

## Events & Activities Domain

### Event (`sprk_event`)

All event types share the same entity with a `sprk_eventtype_ref` lookup discriminating type. Common fields across all event types:

| Logical Name | Display Name | Type | Max Length / Options |
|---|---|---|---|
| `sprk_eventid` | Event | Uniqueidentifier | PK |
| `sprk_name` / `sprk_eventname` | Name | Text | |
| `sprk_description` | Description | Text | |
| `statecode` | Status | State | 0: Active, 1: Inactive |
| `statuscode` | Status Reason | Status | 1: Draft, 659490001: Open, 659490002: Completed, 659490003: Cancelled, 659490004: Closed, 659490005: Transferred, 659490006: On Hold, 659490007: Reassigned, 2: No Further Action |
| `sprk_assignedto` | Assigned To | Lookup | -> `contact` |
| `sprk_assignedattorney` | Assigned Attorney | Lookup | -> `contact` |
| `sprk_assignedparalegal` | Assigned Paralegal | Lookup | -> `contact` |
| `sprk_completeddate` | Completed Date | DateTime | DateOnly |
| `sprk_compledby` | Completed By | Lookup | -> `contact` |

**Task/Milestone-specific fields**:

| Logical Name | Display Name | Type | Options |
|---|---|---|---|
| `sprk_duedate` | Due Date | DateTime | DateOnly |
| `sprk_finalduedate` | Final Due Date | DateTime | DateOnly |
| `sprk_priority` | Priority | Choice | 100000000: Low, 100000001: Normal, 100000002: High, 100000003: Urgent |
| `sprk_effort` | Effort | Choice | 100000000: Low, 100000001: Medium, 100000002: High |

**Meeting-specific fields**:

| Logical Name | Display Name | Type | Options |
|---|---|---|---|
| `sprk_meetingtype` | Meeting Type | Choice | 100000000: In Person, 100000001: Conference, 100000002: Video, 100000003: Other |
| `sprk_meetingdate` | Meeting Date | DateTime | |
| `sprk_meetinglink` | Meeting Link | URL | |

**Email-specific fields**:

| Logical Name | Display Name | Type |
|---|---|---|
| `sprk_regardingemail` | Regarding Email | Lookup -> email |
| `sprk_emaildate` | Email Date | Text |
| `sprk_emailfrom` | From | Text |
| `sprk_emailto` | To | Text |

**Approval-specific fields**:

| Logical Name | Display Name | Type |
|---|---|---|
| `sprk_approveddate` | Approved Date | DateTime |
| `sprk_approvedby` | Approved By | Lookup -> contact |

### Work Assignment (`sprk_workassignment`)

| Logical Name | Display Name | Type | Max Length / Options |
|---|---|---|---|
| `sprk_workassignmentid` | Work Assignment | Uniqueidentifier | PK |
| `sprk_name` | Name | Text | 850 |
| `sprk_workassignmentnumber` | Work Assignment Number | Text | 100 |
| `sprk_description` | Description | Multiline Text | 5000 |
| `sprk_assignedto` | Assigned To | Lookup | -> `contact` |
| `sprk_assignedattorney1` | Assigned Attorney 1 | Lookup | -> `contact` |
| `sprk_assignedattorney2` | Assigned Attorney 2 | Lookup | -> `contact` |
| `sprk_assignedparalegal1` | Assigned Paralegal 1 | Lookup | -> `contact` |
| `sprk_assignedparalegal2` | Assigned Paralegal 2 | Lookup | -> `contact` |
| `sprk_assignedlawfirm1` | Assigned Law Firm 1 | Lookup | -> `sprk_organization` |
| `sprk_assignedlawfirm2` | Assigned Law Firm 2 | Lookup | -> `sprk_organization` |
| `sprk_assignedlawfirmattorney1` | Assigned Law Firm Attorney 1 | Lookup | -> `contact` |
| `sprk_mattertype` | Matter Type | Lookup | -> `sprk_mattertype_ref` |
| `sprk_practicearea` | Practice Area | Lookup | -> `sprk_practicearea_ref` |
| `sprk_priority` | Priority | Choice | 100000000: Low (#FFFFB4), 100000001: Normal (#a0c8ff), 100000002: High (#fa7d7d), 100000003: Urgent (#000000) |
| `sprk_containerid` | Container Id | Text | 100 |
| `sprk_responseduedate` | Response Due Date | DateTime | DateOnly |
| `sprk_searchprofile` | Search Profile | Multiline Text | 2000 (JSON) |

---

## Communication Domain

### Communication (`sprk_communication`)

| Logical Name | Display Name | Type | Max Length / Options |
|---|---|---|---|
| `sprk_communicationid` | Communication | Uniqueidentifier | PK |
| `sprk_name` | Name | Text | 850 |
| `sprk_subject` | Subject | Text | 2000 |
| `sprk_body` | Body | Multiline Text | 100000 |
| `sprk_bodyformat` | Body Format | Choice | 100000000: PlainText, 100000001: HTML |
| `sprk_communicationtype` | Communication Type | Choice | 100000000: Email, 100000001: Teams Message, 100000002: SMS, 100000003: Notification |
| `sprk_direction` | Direction | Choice | 100000000: Incoming, 100000001: Outgoing |
| `sprk_from` | From | Text | 1000 |
| `sprk_to` | To | Text | 1000 |
| `sprk_cc` | CC | Text | 1000 |
| `sprk_bcc` | BCC | Text | 1000 |
| `sprk_sentat` | Sent At | DateTime | DateAndTime |
| `sprk_sentby` | Sent By | Lookup | -> `systemuser` |
| `sprk_graphmessageid` | Graph Message Id | Text | 1000 |
| `sprk_containerid` | Container Id | Text | 100 |
| `sprk_correlationid` | Correlation Id | Text | 100 |
| `sprk_errormessage` | Error Message | Multiline Text | 4000 |
| `sprk_retrycount` | Retry Count | Whole number | |
| `sprk_associationcount` | Association Count | Whole number | |
| `statuscode` | Status Reason | Status | 1: Draft, 2: Deleted, 659490001: Queued, 659490002: Send, 659490003: Delivered, 659490004: Failed, 659490005: Bounded, 659490006: Recalled |

### Communication Account (`sprk_communicationaccount`)

| Logical Name | Display Name | Type | Max Length / Options |
|---|---|---|---|
| `sprk_communicationaccountid` | Communication Account | Uniqueidentifier | PK |
| `sprk_name` | Name | Text | 850 |
| `sprk_displayname` | Display Name | Text | 100 |
| `sprk_emailaddress` | Email Address | Text | 100 |
| `sprk_accounttype` | Account Type | Choice | 100000000: Shared Account, 100000001: Service Account, 100000002: User Account, 100000003: Distribution List |
| `sprk_authmethod` | Auth Method | Choice | 100000000: App-Only (Client Credentials), 100000001: OBO (On Behalf Of) |
| `sprk_sendenabled` | Send Enabled | Boolean | |
| `sprk_receiveenabled` | Receive Enabled | Boolean | |
| `sprk_isdefaultsender` | Is Default Sender | Boolean | |
| `sprk_autocreaterecords` | Auto Create Records | Boolean | |
| `sprk_dailysendlimit` | Daily Send Limit | Whole number | |
| `sprk_sendstoday` | Sends Today | Whole number | |
| `sprk_monitorfolder` | Monitor Folder | Text | 100 |
| `sprk_processingrules` | Processing Rules | Multiline Text | 10000 (JSON) |
| `sprk_graphsubscriptionid` | Graph Subscription Id | Text | 1000 |
| `sprk_subscriptionid` | Subscription Id | Text | 100 |
| `sprk_subscriptionexpiry` | Subscription Expiry | DateTime | |
| `sprk_subscriptionstatus` | Subscription Status | Choice | 100000: Active, 100000001: Expired, 100000002: Failed, 100000003: Not Configured |
| `sprk_verificationstatus` | Verification Status | Choice | 100000000: Verified, 100000001: Failed, 100000002: Pending, 100000003: Not Checked |
| `sprk_verificationmessage` | Verification Message | Multiline Text | 4000 |
| `sprk_securitygroupid` | Security Group Id | Text | 100 |
| `sprk_securitygroupname` | Security Group Name | Text | 100 |
| `sprk_desscription` | Description | Text | 2000 |

---

## AI / Analysis Domain

### Analysis (`sprk_analysis`)

| Logical Name | Display Name | Type | Max Length / Options |
|---|---|---|---|
| `sprk_analysisid` | Analysis | Uniqueidentifier | PK |
| `sprk_name` | Name | Text | 200 |
| `sprk_documentid` | Document | Lookup | -> `sprk_document` |
| `sprk_actionid` | Action | Lookup | -> `sprk_analysisaction` |
| `sprk_playbook` | Playbook | Lookup | -> `sprk_analysisplaybook` |
| `sprk_outputfileid` | Output File | Lookup | -> `sprk_document` |
| `sprk_analysisstatus` | Analysis Status | Choice | 0: Draft, 1: In Progress, 2: Completed, 3: Closed, 4: On Hold, 5: Cancelled, 6: Archived |
| `sprk_workingdocument` | Working Document | Multiline Text | 100000 (Markdown) |
| `sprk_chathistory` | Chat History | Multiline Text | 1048576 (JSON) |
| `sprk_finaloutput` | Final Output | Multiline Text | 100000 |
| `sprk_errormessage` | Error Message | Multiline Text | 2000 |
| `sprk_sessionid` | Session ID | Text | 50 |
| `sprk_inputtokens` | Input Tokens | Whole number | Min: 0 |
| `sprk_outputtokens` | Output Tokens | Whole number | Min: 0 |
| `sprk_startedon` | Started On | DateTime | DateAndTime |
| `sprk_completedon` | Completed On | DateTime | DateAndTime |

### Analysis Action (`sprk_analysisaction`)

| Logical Name | Display Name | Type | Max Length / Options |
|---|---|---|---|
| `sprk_analysisactionid` | Analysis Action | Uniqueidentifier | PK |
| `sprk_name` | Name | Text | 200 |
| `sprk_description` | Description | Multiline Text | 4000 |
| `sprk_analysisid` | Analysis | Lookup | -> `sprk_analysis` |
| `sprk_actiontypeid` | Action Type | Lookup | -> `sprk_analysisactiontype` |
| `sprk_modeldeploymentid` | Default Model | Lookup | -> `sprk_aimodeldeployment` |
| `sprk_systemprompt` | System Prompt | Multiline Text | 100000 |
| `sprk_outputschemajson` | Output Schema | Multiline Text | 1048576 (JSON) |
| `sprk_outputformat` | Output Format | Choice | 0: JSON, 1: Markdown, 2: PlainText |
| `sprk_sortorder` | Sort Order | Whole number | 0-10000 |
| `sprk_allowsknowledge` | Allows Knowledge | Boolean | Default: True |
| `sprk_allowsskills` | Allows Skills | Boolean | Default: True |
| `sprk_allowstools` | Allows Tools | Boolean | Default: True |
| `sprk_allowsdelivery` | Allows Delivery | Boolean | Default: False |

### Analysis Playbook (`sprk_analysisplaybook`)

| Logical Name | Display Name | Type | Max Length / Options |
|---|---|---|---|
| `sprk_analysisplaybookid` | Analysis Playbook | Uniqueidentifier | PK |
| `sprk_name` | Name | Text | 200 |
| `sprk_description` | Description | Multiline Text | 4000 |
| `sprk_playbookcode` | Playbook Code | Text | 50 (Alternate Key, format: `PB-{NNN}`) |
| `sprk_playbooktype` | Playbook Type | Choice | 0: AiAnalysis, 1: Workflow, 2: Hybrid |
| `sprk_playbookmode` | Playbook Mode | Choice | 0: Legacy, 1: NodeBased |
| `sprk_triggertype` | Trigger Type | Choice | 0: Manual, 1: Scheduled, 2: RecordCreated, 3: RecordUpdated |
| `sprk_triggerconfigjson` | Trigger Config | Multiline Text | 100000 (JSON) |
| `sprk_canvaslayoutjson` | Canvas Layout | Multiline Text | 1048576 (JSON) |
| `sprk_outputtypeid` | Output Type | Lookup | -> `sprk_aioutputtype` |
| `sprk_version` | Version | Whole number | |
| `sprk_maxparallelnodes` | Max Parallel Nodes | Whole number | |
| `sprk_ispublic` | Is Public | Boolean | Default: False |
| `sprk_istemplate` | Is Template | Boolean | Default: False |
| `sprk_continueonerror` | Continue On Error | Boolean | Default: False |

### AI Model Deployment (`sprk_aimodeldeployment`)

| Logical Name | Display Name | Type | Max Length / Options |
|---|---|---|---|
| `sprk_aimodeldeploymentid` | AI Model Deployment | Uniqueidentifier | PK |
| `sprk_name` | Name | Text | 200 |
| `sprk_modelid` | Model ID | Text | 100 |
| `sprk_endpoint` | Endpoint | Text | 500 |
| `sprk_provider` | Provider | Choice | 0: AzureOpenAI, 1: OpenAI, 2: Anthropic |
| `sprk_capability` | Capability | Choice | 0: Chat, 1: Completion, 2: Embedding |
| `sprk_contextwindow` | Context Window | Whole number | |
| `sprk_isactive` | Is Active | Boolean | Default: True |
| `sprk_isdefault` | Is Default | Boolean | Default: False |

### AI Retrieval Mode (`sprk_airetrievalmode`)

| Logical Name | Display Name | Type | Max Length / Options |
|---|---|---|---|
| `sprk_airetrievalmodeid` | AI Retrieval Mode | Uniqueidentifier | PK |
| `sprk_name` | Name | Text | 850 |
| `sprk_code` | Code | Text | 100 |
| `sprk_description` | Description | Multiline Text | 4000 |
| `sprk_executiontype` | Execution Type | Choice | 100000000: RAG, 100000001: Structured, 100000002: Rules, 100000003: Graph, 100000004: Event |
| `sprk_defaultazureservice` | Default Azure Service | Choice | 100000000: Azure AI Search, 100000001: Dataverse, 100000002: Azure Functions, 100000003: CosmosDB |
| `sprk_isdeterministic` | Is Deterministic | Boolean | Default: False |
| `sprk_supportscitations` | Supports Citations | Boolean | Default: False |
| `sprk_supportsiteration` | Supports Iteration | Boolean | Default: False |
| `sprk_version` | Version | Decimal | Precision: 2 |

---

## Related Documentation

| Document | Path |
|---|---|
| Entity Relationship Model | `docs/data-model/entity-relationship-model.md` |
| Schema Corrections | `docs/data-model/schema-corrections.md` |
| Alternate Keys | `docs/data-model/schema-additions-alternate-keys.md` |
| JSON Field Schemas | `docs/data-model/json-field-schemas.md` |
| Event Form GUIDs | `docs/data-model/sprk_event-forms-guids.md` |
| Event View GUIDs | `docs/data-model/sprk_event-views-guids.md` |
