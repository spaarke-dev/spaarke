# Communication Module User Guide

> **Last Updated**: February 21, 2026
> **Purpose**: User-facing guide for the Communication module — creating, sending, and tracking email communications within the Spaarke platform.
> **Audience**: End users, legal staff, administrators

---

## Table of Contents

- [Overview](#overview)
- [Accessing Communications](#accessing-communications)
- [Creating a Communication](#creating-a-communication)
- [Sending a Communication](#sending-a-communication)
- [Communication Status Lifecycle](#communication-status-lifecycle)
- [Working with Associations](#working-with-associations)
- [Working with Attachments](#working-with-attachments)
- [Bulk Sending](#bulk-sending)
- [Communication Views](#communication-views)
- [Communication Subgrids](#communication-subgrids)
- [Frequently Asked Questions](#frequently-asked-questions)
- [Error Messages Reference](#error-messages-reference)

---

## Overview

The Communication module enables you to send emails directly from the Spaarke platform. Communications are tracked as records linked to matters, projects, organizations, and other entities, providing a complete audit trail of client and internal correspondence.

### Key Features

- Send emails to one or multiple recipients
- Link communications to matters, projects, invoices, and other records
- Attach documents from the document management system
- Archive sent emails as .eml files for compliance
- Track delivery status (Draft → Send → Delivered)
- View communication history on related records via subgrids

### How It Works

When you click **Send** on a Communication form:

1. The system validates your email (recipients, subject, body)
2. The email is sent via Microsoft 365 from an approved shared mailbox
3. A tracking record is created in the system
4. Optionally, the email is archived as an .eml file
5. The Communication status updates from **Draft** to **Send**

---

## Accessing Communications

### From the Site Map

1. Open the **Spaarke** model-driven app
2. In the left navigation pane, click **Communications**
3. You will see the list of all Communications (default view: **Active Communications**)

### From a Related Record

Communications linked to a specific record (Matter, Project, etc.) appear in the **Communications** subgrid on that record's form. See [Communication Subgrids](#communication-subgrids) for details.

---

## Creating a Communication

### Step-by-Step

1. Navigate to **Communications** in the site map
2. Click **+ New** in the command bar
3. Fill in the required fields:

| Field | Required | Description |
|-------|----------|-------------|
| **To** | Yes | Recipient email address(es). Separate multiple addresses with semicolons (`;`) or commas (`,`). |
| **Subject** | Yes | Email subject line |
| **Body** | Yes | Email message content (supports HTML formatting) |
| **CC** | No | CC recipients (semicolon or comma separated) |
| **Communication Type** | No | Defaults to **Email** |
| **From Mailbox** | No | Sender mailbox. Leave blank to use the default shared mailbox. |

4. **Link to a record** (optional but recommended):
   - Set one or more of the **Regarding** lookup fields to associate this communication with a Matter, Project, Organization, Person, etc.
   - See [Working with Associations](#working-with-associations)

5. Click **Save** to create the record in **Draft** status

### Tips

- The **Name** field is auto-generated from the subject line (format: "Email: {subject}")
- Draft communications can be edited freely before sending
- You can save and return to a Draft communication later

---

## Sending a Communication

### Prerequisites

- The Communication must be in **Draft** status
- All required fields must be filled (To, Subject, Body)
- Your user account must have appropriate security permissions

### Step-by-Step

1. Open a Communication record in **Draft** status
2. Verify the recipient(s), subject, and body are correct
3. Click the **Send** button in the command bar

   > The Send button displays the Fluent UI "Send" icon and is only enabled when the record is in Draft status.

4. You will see a progress notification: **"Sending communication..."**
5. On success:
   - A green notification appears: **"Communication sent successfully."**
   - The **Status** changes from **Draft** to **Send**
   - The **Sent At** timestamp is populated
   - The **From** field shows the sender mailbox used
   - The **Send** button becomes disabled (cannot re-send)

### What Happens Behind the Scenes

- The email is sent via Microsoft 365 Graph API
- A Dataverse tracking record captures the full email details
- If archival is enabled, an .eml copy is stored in SharePoint Embedded
- All associated entity links are preserved on the record

---

## Communication Status Lifecycle

| Status | Description | Send Button |
|--------|-------------|-------------|
| **Draft** | New communication, not yet sent | Enabled |
| **Queued** | Queued for delivery (future use) | Disabled |
| **Send** | Successfully sent via email | Disabled |
| **Delivered** | Delivery confirmed (future use) | Disabled |
| **Failed** | Send attempt failed | Disabled |
| **Bounded** | Email bounced back | Disabled |
| **Recalled** | Recalled by sender (future use) | Disabled |

### Status Flow

```
Draft ──Send──> Send ──(delivery tracking)──> Delivered
  │                                              │
  │                                              └──> Bounded (if bounced)
  │
  └──(send fails)──> Failed
```

- Only **Draft** records can be sent
- Once sent, the status cannot be changed back to Draft
- Failed sends can be investigated using the error details and correlation ID

---

## Working with Associations

Communications can be linked to multiple entity types through **Regarding** lookup fields on the form.

### Supported Entity Types

| Regarding Field | Entity | Example |
|----------------|--------|---------|
| Regarding Matter | sprk_matter | "Smith v. Jones" |
| Regarding Project | sprk_project | "Annual Compliance Review" |
| Regarding Organization | sprk_organization | "Acme Corp" |
| Regarding Person | contact | "John Smith" |
| Regarding Analysis | sprk_analysis | "Financial Analysis Q4" |
| Regarding Budget | sprk_budget | "2026 Operating Budget" |
| Regarding Invoice | sprk_invoice | "INV-2026-001" |
| Regarding Work Assignment | sprk_workassignment | "Due Diligence Review" |

### How Associations Work

- The **primary association** (first one set) determines the regarding record displayed in views and subgrids
- The primary association's name, ID, and URL are stored on the Communication record for quick reference
- Multiple associations can be set simultaneously to cross-link the communication across entities
- Associations enable the communication to appear in subgrids on the related record's form

### Setting Associations

1. On the Communication form, find the **Regarding** section
2. Click the lookup field for the entity type you want to link
3. Search for and select the record
4. Save the Communication

> **Tip**: Set the most important association first — it becomes the "primary" association displayed in list views.

---

## Working with Attachments

### Attaching Documents

Documents from the Spaarke document management system (SharePoint Embedded) can be attached to communications. Attached documents are downloaded from SPE and included in the email as file attachments.

### Attachment Limits

| Limit | Value |
|-------|-------|
| Maximum number of attachments | 150 per email |
| Maximum total attachment size | 35 MB |

### Supported File Types

All common document types are supported, including:
- Documents: PDF, DOCX, XLSX, PPTX, TXT, CSV
- Images: PNG, JPG, GIF, BMP, SVG
- Archives: ZIP
- Emails: EML, MSG
- Other: HTML, XML, JSON

### Attachment Records

After a communication is sent, each attachment is recorded as a separate **Communication Attachment** record linked to both the Communication and the source Document. This provides a full audit trail of which documents were sent to whom.

---

## Bulk Sending

The Communication module supports sending the same email content to multiple recipients, where each recipient receives their own individual email and tracking record.

### API-Based (Administrator/Developer Use)

Bulk sending is available via the BFF API endpoint `POST /api/communications/send-bulk`:

- Maximum 50 recipients per bulk request
- Each recipient gets their own `sprk_communication` record
- Shared subject, body, attachments, and associations across all recipients
- Per-recipient CC addresses supported
- Results returned for each recipient (success or failure)

### Partial Failure Handling

If some recipients fail while others succeed:
- Successful sends are tracked normally
- Failed sends are reported with error details
- The overall response uses HTTP 207 (Multi-Status) to indicate partial success

---

## Communication Views

### Available Views

| View Name | Description | Default |
|-----------|-------------|---------|
| **Active Communications** | All communications with Active state | Yes |
| **My Draft Communications** | Draft communications owned by you | No |
| **Sent Communications** | Communications with Send/Delivered status | No |
| **All Communications** | All communications regardless of status | No |
| **Communications by Matter** | Grouped by Regarding Matter | No |

### Sorting and Filtering

- Click any column header to sort
- Use the filter icon on columns to filter by specific values
- Use the search bar to search across Communication records
- Switch views using the view selector dropdown

---

## Communication Subgrids

Communication records appear as subgrids on related entity forms, allowing you to see all communications linked to a specific Matter, Project, Organization, etc.

### Where Subgrids Appear

| Entity Form | Subgrid Shows |
|-------------|---------------|
| Matter | Communications linked via Regarding Matter |
| Project | Communications linked via Regarding Project |
| Organization | Communications linked via Regarding Organization |
| Contact (Person) | Communications linked via Regarding Person |
| Analysis | Communications linked via Regarding Analysis |
| Budget | Communications linked via Regarding Budget |
| Invoice | Communications linked via Regarding Invoice |
| Work Assignment | Communications linked via Regarding Work Assignment |

### Using Subgrids

- The subgrid shows the most recent communications first
- Click a row to open the Communication record
- Use **+ New Communication** in the subgrid to create a communication pre-linked to the current record
- The subgrid displays: Name, To, Status, Sent At, Communication Type

---

## Frequently Asked Questions

### Q: Who sends the email?

The email is sent from an **approved shared mailbox** (e.g., `noreply@spaarke.com`). Individual user mailboxes are not used unless specifically configured and approved by an administrator.

### Q: Can I specify a different sender?

Yes, if multiple approved senders are configured. Use the **From Mailbox** field to select a different sender. Only mailboxes in the approved senders list can be used — arbitrary email addresses are not allowed.

### Q: Can I edit a sent communication?

No. Once a communication is sent, the status changes to **Send** and the record becomes read-only for key fields (To, Subject, Body). This preserves the audit trail.

### Q: Can I resend a communication?

No. Each send creates a unique tracking record. To resend the same content, create a new Communication record and copy the details.

### Q: Where can I see communications for a specific matter?

Open the Matter record and scroll to the **Communications** subgrid. This shows all communications linked to that matter. You can also use the **Communications by Matter** view.

### Q: What happens if the send fails?

If the email fails to send:
- An error message appears on the form with details
- The Communication remains in **Draft** status
- You can fix the issue (e.g., correct the email address) and try again
- The error includes an error code and correlation ID for support reference

### Q: Are BCC recipients visible on the record?

BCC recipients are stored on the Communication record for internal tracking but are not typically displayed on the default form layout. They are not visible to email recipients.

### Q: Is the email archived?

If archival is enabled for the send request, a copy of the email is saved as an `.eml` file in SharePoint Embedded. A linked Document record is created for easy access.

---

## Error Messages Reference

| Error Message | Meaning | Resolution |
|---------------|---------|------------|
| "Required fields are missing: To, Subject, Body" | One or more required fields are empty | Fill in all required fields and try again |
| "Invalid Sender: {email} is not in the approved senders list" | The specified From mailbox is not approved | Leave From Mailbox blank to use the default, or contact an administrator to add the mailbox |
| "Email Send Failed: Graph API error: {details}" | The email service encountered an error | Check the recipient email addresses and try again. If persistent, contact support with the correlation ID |
| "Network error: Unable to reach the communication service" | The BFF API is unreachable | Check your network connection. If the issue persists, the service may be temporarily unavailable |
| "Too Many Attachments: Maximum 150 attachments allowed" | Exceeded the attachment count limit | Reduce the number of attachments and try again |
| "Attachments Too Large: Total attachment size exceeds 35MB limit" | Exceeded the total attachment size limit | Remove large attachments or compress files before attaching |
| "Attachment Not Found: Document '{id}' was not found" | A referenced attachment document doesn't exist | Verify the document exists in the document management system |

### Understanding Error Details

Error notifications on the form include:
- **Title**: Short description of the error type
- **Detail**: Specific explanation of what went wrong
- **Error Code**: Technical code for support reference (e.g., `INVALID_SENDER`, `GRAPH_SEND_FAILED`)
- **Correlation ID**: Unique reference for tracking in system logs (format: `[Ref: abc123]`)

When contacting support, always provide the **Error Code** and **Correlation ID**.

---

*User guide for the Email Communication Solution R1. See also: [Architecture](../architecture/communication-service-architecture.md) | [Deployment Guide](COMMUNICATION-DEPLOYMENT-GUIDE.md)*
