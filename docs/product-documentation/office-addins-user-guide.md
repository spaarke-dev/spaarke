# Spaarke Office Add-ins - User Guide

> **Version**: 1.0.0
>
> **Last Updated**: January 2026

## Overview

The Spaarke Office Add-ins integrate document management directly into Microsoft Outlook and Word. You can save emails, attachments, and documents to Spaarke, and share Spaarke documents via email - all without leaving your familiar Office applications.

### What You Can Do

| Feature | Outlook | Word |
|---------|---------|------|
| Save email (body + attachments) | Yes | - |
| Save attachments selectively | Yes | - |
| Save current document | - | Yes |
| Insert document links | Yes (Compose) | Yes |
| Attach document copies | Yes (Compose) | - |
| Create new Matters/Accounts | Yes | Yes |

### Supported Applications

| Application | Platform | Supported |
|-------------|----------|-----------|
| New Outlook | Windows / Mac | Yes |
| Outlook Web | Browser | Yes |
| Classic Outlook | Windows | Not supported |
| Word Desktop | Windows / Mac | Yes |
| Word Web | Browser | Yes |

---

## Getting Started

### Opening the Spaarke Panel

**In Outlook (Reading an Email):**

1. Open or select an email in your inbox
2. Look for the **Spaarke** button in the ribbon toolbar
3. Click the button to open the Spaarke panel on the right side

**In Outlook (Composing an Email):**

1. Start composing a new email or reply
2. Click the **Spaarke** button in the compose toolbar
3. The panel opens with sharing options

**In Word:**

1. Open your Word document
2. Click the **Spaarke** button in the Home ribbon
3. The Spaarke panel opens on the right side

### First-Time Sign In

The first time you use the add-in, you may be prompted to sign in:

1. A sign-in window will appear
2. Enter your organization email address
3. Complete authentication (may include multi-factor authentication)
4. The panel will load your Spaarke access

If you experience issues signing in, contact your IT administrator.

---

## Saving Emails to Spaarke

Save important emails and their attachments to Spaarke for organized document management.

### Save an Email (with or without Attachments)

1. **Open the email** you want to save
2. **Click Spaarke** in the ribbon to open the panel
3. Select **Save to Spaarke**

The panel shows your email details:
- Email subject
- Sender information
- List of attachments (if any)

4. **Choose attachments** (if the email has any):
   - All attachments are selected by default
   - Uncheck any attachments you do not want to save
   - Each attachment shows its name and size

5. **Select where to file it**:
   - Type in the search box to find a Matter, Project, Account, Contact, or Invoice
   - Results appear as you type
   - Click on the correct item to select it

   **Note**: You must select an association target. Saving without choosing a Matter, Project, Account, Contact, or Invoice is not allowed.

6. **Optional: Add processing options**
   - **Profile Summary**: Generate an AI summary of the content
   - **Index for Search**: Make the document searchable in Spaarke
   - These options may be pre-set by your organization

7. **Click Save**

### Watching the Save Progress

After clicking Save, you will see a progress indicator showing each stage:

| Stage | Description |
|-------|-------------|
| Uploading | Your content is being sent to Spaarke |
| Creating Records | Spaarke is creating the document records |
| Processing | AI analysis is running (if enabled) |
| Complete | Your document is saved and ready |

When complete, you will see:
- A green checkmark
- A link to open the document in Spaarke
- A link to view the associated record (Matter, Account, etc.)

### Saving Attachments Only

To save specific attachments without the email body:

1. Open the email
2. Click **Spaarke** and select **Save to Spaarke**
3. **Uncheck** the email body option (if shown)
4. Select only the attachments you want
5. Choose the association target
6. Click **Save**

Each attachment is saved as a separate document in Spaarke, all associated with your chosen Matter, Account, or other entity.

### Attachment Size Limits

| Limit | Value |
|-------|-------|
| Maximum per file | 25 MB |
| Maximum total (all attachments) | 100 MB |

If an attachment exceeds the limit, you will see an error message. Save the attachment locally first, then upload through the Spaarke web application.

---

## Saving Word Documents to Spaarke

Save your Word documents directly to Spaarke without leaving Word.

### Save Current Document

1. **Open your document** in Word
2. **Click Spaarke** in the Home ribbon
3. Select **Save to Spaarke**

The panel shows:
- Document name
- File size
- Last modified date

4. **Select where to file it**:
   - Search for a Matter, Project, Account, Contact, or Invoice
   - Click to select

5. **Optional: Choose processing options**

6. **Click Save**

### Version Management

If your document was originally opened from Spaarke:

- **Save New Version**: Updates the existing document with a new version
- The version history is preserved in Spaarke

If this is a new document (not from Spaarke):

- A new document record is created
- Future saves can create versions of this new document

---

## Sharing from Spaarke

Share documents stored in Spaarke by inserting links or attaching copies to your emails.

### Insert a Document Link

Insert a clickable link that opens the document in Spaarke.

1. **Compose a new email** in Outlook (or reply/forward)
2. **Click Spaarke** and select **Share from Spaarke**
3. **Search for the document** you want to share
4. Select the document from results
5. Click **Insert Link**

The link is inserted at your cursor position. Recipients who have Spaarke access can click to open the document.

### Attach a Document Copy

Attach a copy of a Spaarke document to your email.

1. **Compose an email** in Outlook
2. **Click Spaarke** and select **Share from Spaarke**
3. **Search and select** your document
4. Click **Attach Copy**

The document is attached to your email as a file. Recipients receive the attachment directly.

**Note**: For large files (over 25 MB), consider using Insert Link instead.

### Sharing Multiple Documents

To share multiple documents:

1. Search and select your first document
2. Click **Add to selection**
3. Repeat for additional documents
4. Once all documents are selected, choose:
   - **Insert Links** for all
   - **Attach Copies** for all

### Granting Access to External Recipients

When sharing with people outside your organization:

1. Select your document(s)
2. Check **Grant access to recipients**
3. Choose the access level:
   - **View Only**: Recipients can view but not download
   - **Download**: Recipients can download a copy
4. Complete the share action

**Note**: External access requires the External Portal feature to be configured by your administrator.

---

## Quick Create: Adding New Records

Cannot find the Matter, Account, or Contact you need? Create one directly from the add-in.

### Create a New Matter

1. In the association picker, click **Quick Create**
2. Select **Matter**
3. Fill in the required fields:
   - **Name**: The matter name (required)
   - **Description**: Brief description (optional)
   - **Client**: Search and select a client account (optional)
4. Click **Create**

The new Matter is created and automatically selected for your save.

### Create a New Project

1. Click **Quick Create** and select **Project**
2. Fill in:
   - **Name**: Project name (required)
   - **Description**: Brief description (optional)
3. Click **Create**

### Create a New Account

1. Click **Quick Create** and select **Account**
2. Fill in:
   - **Name**: Company/organization name (required)
   - **Description**: Brief description (optional)
   - **Industry**: Industry type (optional)
   - **City**: Location (optional)
3. Click **Create**

### Create a New Contact

1. Click **Quick Create** and select **Contact**
2. Fill in:
   - **First Name**: (required)
   - **Last Name**: (required)
   - **Email**: Email address (optional)
   - **Account**: Search and select a company (optional)
3. Click **Create**

### Create a New Invoice

1. Click **Quick Create** and select **Invoice**
2. Fill in:
   - **Name**: Invoice identifier (required)
   - **Description**: Brief description (optional)
3. Click **Create**

---

## Understanding Job Status

When you save content to Spaarke, the job goes through several stages.

### Status Stages

| Icon | Stage | Meaning |
|------|-------|---------|
| Circle | Queued | Waiting to start |
| Spinning | Running | Currently processing |
| Check | Completed | Successfully finished |
| Dash | Skipped | Not applicable or disabled |
| X | Failed | An error occurred |

### Typical Save Job Stages

1. **Records Created**: Document record created in Spaarke
2. **File Uploaded**: File stored in secure storage
3. **Profile Summary**: AI summary generated (if enabled)
4. **Indexed**: Document indexed for search (if enabled)
5. **Deep Analysis**: Detailed analysis (if enabled)

### What Happens in the Background

- The initial save completes quickly (within 3 seconds)
- Processing stages run in the background
- You can close the panel and continue working
- Reopen the panel to check status anytime

---

## Recent Items

The add-in remembers your recently used items for quick access.

### Recent Associations

When selecting where to file a document:
- Your recently used Matters, Projects, Accounts, Contacts, and Invoices appear first
- Click on a recent item to select it immediately
- Or type to search for something different

### Recent Documents (Sharing)

When sharing documents:
- Recently shared documents appear at the top
- Quickly re-share frequently used documents

---

## Troubleshooting

### Common Issues and Solutions

#### The Spaarke button is not visible

**Possible causes:**
- The add-in may not be installed
- You may be using Classic Outlook (not supported)

**Solutions:**
- Ask your IT administrator to install the Spaarke add-in
- Switch to New Outlook or Outlook Web

#### Sign-in fails or keeps prompting

**Possible causes:**
- Session expired
- Browser cookies blocked

**Solutions:**
1. Close and reopen the Spaarke panel
2. Try signing in again
3. Ensure third-party cookies are enabled in your browser (for Outlook Web)
4. Contact your IT administrator if issues persist

#### Save button is disabled (grayed out)

**Possible causes:**
- No association target selected

**Solutions:**
- Search for and select a Matter, Project, Account, Contact, or Invoice
- Use Quick Create if the record does not exist

#### "Attachment too large" error

**Possible causes:**
- Single attachment exceeds 25 MB
- Total attachments exceed 100 MB

**Solutions:**
- Save the attachment locally first
- Upload through the Spaarke web application
- Or share via link instead of attaching

#### "Access denied" error

**Possible causes:**
- You do not have permission to the selected Matter, Project, or other entity
- Your Spaarke license may have expired

**Solutions:**
- Select a different association target
- Contact your administrator for access
- Verify your Spaarke account is active

#### Job stuck on "Processing"

**Possible causes:**
- Background processing is taking longer than usual
- System may be experiencing high load

**Solutions:**
- Wait a few minutes and check again
- The document is likely saved; only AI processing may be delayed
- Contact support if it remains stuck for more than 30 minutes

#### Panel loads slowly or appears blank

**Possible causes:**
- Network connectivity issues
- Browser cache issues

**Solutions:**
1. Check your internet connection
2. Refresh the panel (close and reopen)
3. Clear browser cache (for Outlook Web)
4. Try a different browser

### Error Codes

If you see an error code, here is what it means:

| Code | Meaning | What to Do |
|------|---------|------------|
| OFFICE_001 | Invalid source type | Contact support |
| OFFICE_002 | Invalid association type | Contact support |
| OFFICE_003 | Association required | Select a Matter, Project, Account, etc. |
| OFFICE_004 | Attachment too large | Save attachment locally and upload via web |
| OFFICE_005 | Total size exceeded | Remove some attachments and try again |
| OFFICE_006 | Blocked file type | This file type cannot be saved |
| OFFICE_007 | Target not found | The selected record may have been deleted |
| OFFICE_008 | Job not found | Refresh and try again |
| OFFICE_009 | Access denied | Contact your administrator for access |
| OFFICE_010 | Cannot create entity | You do not have permission to create records |
| OFFICE_011 | Duplicate document | This was already saved (see link provided) |
| OFFICE_012 | Upload failed | Try again; contact support if repeated |
| OFFICE_013 | Service error | Try again later |
| OFFICE_014 | Dataverse error | Try again; contact support if repeated |
| OFFICE_015 | Processing unavailable | Background processing is offline; try later |

---

## Frequently Asked Questions

### General Questions

**Q: Which Outlook versions are supported?**
A: New Outlook (Windows and Mac) and Outlook on the web. Classic Outlook is not supported.

**Q: Which Word versions are supported?**
A: Word Desktop (Windows and Mac) and Word on the web.

**Q: Do I need an internet connection?**
A: Yes, the add-in requires an internet connection to communicate with Spaarke.

**Q: Can I use the add-in offline?**
A: No, an internet connection is required.

### Saving Questions

**Q: Can I save an email without choosing a Matter or Account?**
A: No, you must select an association target (Matter, Project, Invoice, Account, or Contact) for all saves.

**Q: What happens if I save the same email twice?**
A: Spaarke detects duplicates. You will see a message that the item was previously saved, with a link to the existing document.

**Q: Are email attachments saved automatically?**
A: All attachments are selected by default, but you can uncheck any you do not want to save.

**Q: Can I save just the attachments without the email body?**
A: Yes, uncheck the email body option and select only the attachments you want.

**Q: What file types can I save?**
A: Most document types are supported (Word, Excel, PDF, images, etc.). Executable files (.exe, .bat, etc.) are blocked for security.

### Sharing Questions

**Q: What is the difference between Insert Link and Attach Copy?**
A: Insert Link adds a clickable link that opens in Spaarke. Attach Copy attaches the actual file to your email.

**Q: Can external recipients access shared links?**
A: Only if you check "Grant access to recipients" and your organization has configured external sharing.

**Q: Is there a limit on how many documents I can share?**
A: No hard limit, but consider email size limits when attaching copies.

### Technical Questions

**Q: Where is my data stored?**
A: Documents are stored in Spaarke's secure cloud storage (SharePoint Embedded). Metadata is stored in Dataverse.

**Q: Is my data encrypted?**
A: Yes, all data is encrypted in transit and at rest.

**Q: How long does processing take?**
A: Initial save completes within 3 seconds. AI processing stages may take additional time in the background.

---

## Accessibility

The Spaarke Office Add-ins are designed to be accessible to all users.

### Keyboard Navigation

| Key | Action |
|-----|--------|
| Tab | Move to next element |
| Shift+Tab | Move to previous element |
| Enter | Activate button or select item |
| Space | Toggle checkbox |
| Escape | Close dialog or cancel |
| Arrow keys | Navigate within lists |

### Screen Reader Support

- All buttons and controls have descriptive labels
- Status updates are announced automatically
- Error messages are read aloud
- Form fields have associated labels

### Visual Accessibility

- High contrast mode is supported
- Dark mode is fully supported
- Focus indicators are clearly visible
- Text meets minimum contrast requirements

---

## Getting Help

If you need additional assistance:

1. **Contact your IT administrator** for installation or access issues
2. **Check error codes** in the Troubleshooting section above
3. **Note the correlation ID** shown in error messages when contacting support

---

*Spaarke Office Add-ins are part of the Spaarke Document Management System. For more information about Spaarke features, contact your administrator.*
