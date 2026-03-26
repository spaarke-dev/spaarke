# Spaarke AI in Microsoft 365 Copilot — User Guide

> **Last Updated**: March 26, 2026
> **Purpose**: How to use Spaarke AI capabilities from within M365 Copilot in Power Apps model-driven apps.
> **Audience**: End users, legal staff, analysts

---

## Table of Contents

- [What Is Spaarke AI in Copilot?](#what-is-spaarke-ai-in-copilot)
- [Accessing Spaarke AI](#accessing-spaarke-ai)
- [Document Search](#document-search)
- [Running a Playbook Analysis](#running-a-playbook-analysis)
- [Browsing Available Playbooks](#browsing-available-playbooks)
- [Matter and Task Queries](#matter-and-task-queries)
- [Email Drafting](#email-drafting)
- [Opening the Analysis Workspace](#opening-the-analysis-workspace)
- [Document Upload](#document-upload)
- [Tips and Best Practices](#tips-and-best-practices)
- [What Copilot Cannot Do](#what-copilot-cannot-do)
- [Frequently Asked Questions](#frequently-asked-questions)

---

## What Is Spaarke AI in Copilot?

Spaarke AI in M365 Copilot brings your document intelligence, playbook analysis, matter management, and communication tools directly into the Copilot side pane within your Power Apps model-driven app. Instead of switching between screens, you can ask Copilot to search documents, run analysis playbooks, check your tasks, draft emails, and more — all from the same conversation.

### Key Capabilities

| Capability | What You Can Do |
|------------|-----------------|
| **Document Search** | Find documents by name, type, or matter ("Find the NDA for Acme") |
| **Playbook Analysis** | Run AI-powered analysis on documents (lease review, risk assessment, etc.) |
| **Matter Queries** | Ask about your tasks, deadlines, and matter status |
| **Email Drafting** | Draft contextual emails based on matter activity |
| **Analysis Workspace Handoff** | Open a document in the full Analysis Workspace for deep analysis |
| **Document Upload** | Launch the upload wizard with pre-filled context |

### Spaarke AI in Copilot vs. SprkChat

| Feature | Copilot (general use) | SprkChat (Analysis Workspace) |
|---------|----------------------|-------------------------------|
| Available on | All model-driven app pages | Analysis Workspace only |
| Best for | Quick lookups, playbook runs, task queries, email drafts | Deep document analysis, inline editing, streaming results |
| Document editing | Not supported — use handoff | Full inline editing and write-back |
| Streaming results | Final result cards only | Real-time token-by-token streaming |

When you need to go deeper, Copilot provides a one-click handoff to the Analysis Workspace where SprkChat takes over.

---

## Accessing Spaarke AI

1. Open the **Spaarke** model-driven app in your browser.
2. Click the **Copilot** icon in the top-right corner of the page to open the side pane.
3. You will see the Spaarke AI agent with suggested conversation starters.

[Screenshot: Copilot side pane open in model-driven app showing Spaarke AI agent with conversation starters]

### Conversation Starters

When you first open the Copilot pane, you will see suggested prompts such as:

- "Find documents for [matter name]"
- "What are my overdue tasks?"
- "What analysis tools are available?"
- "Draft an update to outside counsel"

Click any conversation starter or type your own question to begin.

---

## Document Search

Search for documents stored in your SharePoint Embedded containers directly from Copilot.

### How to Search

1. Open the Copilot side pane.
2. Type a natural language query describing the document you need.
3. Copilot searches your authorized documents and returns a results card.

**Example prompts:**
- "Find the NDA for Acme Corporation"
- "Show me the lease agreement for the Smith matter"
- "What documents are in the Johnson project?"

[Screenshot: Document search results card showing document name, type, matter, and action buttons]

### Understanding Search Results

Copilot returns an Adaptive Card with matching documents. Each result shows:

- **Document name** and file type
- **Associated matter or project**
- **Last modified date**
- **Action buttons**: View details, run a playbook, or open in the Analysis Workspace

### Selecting a Document for Analysis

When search results appear, you can:

1. Click a document name to view more details.
2. Click **Run Analysis** to see available playbooks for that document type.
3. Click **Open in Workspace** to open the document in the full Analysis Workspace.

> **Note**: You will only see documents you are authorized to access. Documents in matters or projects you do not have permissions for will not appear.

---

## Running a Playbook Analysis

Playbooks are pre-built AI analysis workflows tailored to specific document types (e.g., Lease Review, NDA Risk Assessment, Contract Summary).

### Step-by-Step: Run a Playbook

1. **Find your document** — Search for the document using a query like "Find the NDA for Acme."
2. **Select a playbook** — Copilot shows available playbooks for the document type. Click the playbook you want to run.
3. **View results** — For quick analyses, results appear as a structured card in the Copilot pane. For longer analyses, you receive a link to view results in the Analysis Workspace.

[Screenshot: Playbook selection card showing available analysis options as buttons]

[Screenshot: Playbook results card showing findings, risk level, and key clauses]

### Quick vs. Long-Running Playbooks

| Type | Behavior | Example |
|------|----------|---------|
| **Quick** | Results appear directly in the Copilot pane as an Adaptive Card | Contract Summary, Key Dates |
| **Long-running** | Copilot provides a link to the Analysis Workspace where you can track progress | Full Lease Review, Comprehensive Risk Assessment |

For long-running playbooks, click the **Open in Analysis Workspace** link to view progress and results in the full interface.

### Example Conversation

> **You**: "I need to review the Smith lease agreement"
>
> **Copilot**: *Returns document card with the Smith lease and playbook options*
>
> **You**: *Clicks "Lease Review"*
>
> **Copilot**: *Returns findings card with key terms, risk flags, and renewal dates*

---

## Browsing Available Playbooks

You can explore all available analysis tools without a specific document in mind.

1. Ask Copilot: "What analysis tools are available?" or "Show me available playbooks."
2. Copilot returns a categorized list of playbooks with descriptions.

[Screenshot: Playbook library card showing categories and available analyses]

Each playbook entry shows:

- **Playbook name** and description
- **Document types** it applies to
- **Estimated run time** (quick or extended)

---

## Matter and Task Queries

Ask Copilot questions about your matters, tasks, events, and projects.

### Example Queries

| What You Ask | What You Get |
|-------------|--------------|
| "What are my overdue tasks?" | Task list with due dates and matter links |
| "Show me the status of the Acme matter" | Matter summary card with key details |
| "What events are coming up this week?" | Event list with dates, times, and related matters |
| "List my active projects" | Project summary cards |

[Screenshot: Task list card showing overdue tasks with due dates and matter names]

### Understanding Result Cards

Query results appear as Adaptive Cards with:

- **Clickable links** to open the full record in the model-driven app
- **Status indicators** (overdue, due soon, on track)
- **Key dates** and assigned parties

---

## Email Drafting

Draft emails directly from Copilot, pre-filled with matter context, recent activity, and relevant document details.

### How to Draft an Email

1. Ask Copilot to draft an email with context:
   - "Draft an update to outside counsel on the Smith matter"
   - "Write an email to the client about the contract review findings"
2. Copilot generates an email preview card with subject line, recipients, and body text.
3. Review the draft and use the action buttons:
   - **Edit** — Modify the draft before sending
   - **Send** — Send the email through the platform
   - **Cancel** — Discard the draft

[Screenshot: Email preview card showing subject, recipients, and draft body with Edit and Send buttons]

### Tips for Better Drafts

- **Be specific about the recipient** — "Draft an email to outside counsel" works better than "Draft an email."
- **Mention the matter** — Copilot uses matter context to include relevant details.
- **Specify the purpose** — "status update," "follow-up on review," or "request for documents" helps Copilot tailor the tone and content.

---

## Opening the Analysis Workspace

When your work requires deep analysis, inline editing, or streaming AI results, Copilot provides a handoff to the Analysis Workspace.

### When to Use Handoff

- You need to edit a document with AI assistance
- The playbook results require detailed review and annotation
- You want to use SprkChat's streaming, multi-turn analysis capabilities
- The analysis is too complex for the Copilot card format

### How Handoff Works

1. When Copilot determines a task is better suited for the Analysis Workspace, it presents a handoff card.
2. Click **Open in Analysis Workspace**.
3. The Analysis Workspace opens with your document loaded and SprkChat ready with the full context of your Copilot conversation.

[Screenshot: Handoff card with "Open in Analysis Workspace" button and context summary]

You can also explicitly request a handoff:
- "Open the Smith NDA in the Analysis Workspace"
- "I want to do a deep review of this document"

### Other Wizard Links

Copilot can also deep-link to specialized wizards:

| Wizard | When Copilot Links to It |
|--------|--------------------------|
| Document Upload Wizard | "Upload a document to the Acme matter" |
| Summarize Files Wizard | "Summarize the documents in this project" |
| Create Matter Wizard | "Create a new matter for Acme" |
| Create Event Wizard | "Schedule a review event" |

---

## Document Upload

Copilot can help you start the document upload process with pre-filled context.

1. Tell Copilot you want to upload a document: "Upload a document to the Acme matter."
2. Copilot provides a link to the Document Upload Wizard with the matter and container pre-selected.
3. Complete the upload in the wizard.
4. Return to Copilot and search for the document by name to run analysis.

[Screenshot: Upload link card with matter name and "Open Upload Wizard" button]

> **Note**: Copilot cannot directly accept file attachments for analysis. Use the upload wizard or search for already-uploaded documents by name.

---

## Tips and Best Practices

### Writing Effective Prompts

- **Be specific**: "Find the 2025 NDA for Acme Corp" works better than "Find a document."
- **Name the matter**: Including the matter name helps Copilot resolve the right context.
- **Use natural language**: Copilot understands conversational requests — no special syntax needed.
- **Ask follow-up questions**: After a search or analysis, you can ask follow-up questions in the same conversation.

### Getting the Most from Playbooks

- Start with a document search, then select a playbook from the results card.
- For quick checks, use Copilot. For deep analysis, accept the handoff to the Analysis Workspace.
- Ask "What analysis tools are available?" if you are not sure which playbook to use.

### Multi-Turn Conversations

Copilot remembers context within a conversation. You can:

1. Search for a document.
2. Ask a follow-up question about the results.
3. Select a playbook to run.
4. Ask about specific findings in the results.

Start a new conversation if you want to switch to a completely different topic.

---

## What Copilot Cannot Do

The following capabilities are available only in the Analysis Workspace via SprkChat:

| Limitation | What to Do Instead |
|-----------|-------------------|
| **Streaming results** — Copilot shows final result cards, not token-by-token streaming | Use the Analysis Workspace for real-time streaming analysis |
| **Inline document editing** — Copilot cannot edit documents directly | Use "Open in Analysis Workspace" for editor integration |
| **Write-back to documents** — Copilot cannot modify document content | Use SprkChat's inline toolbar in the Analysis Workspace |
| **File attachments** — Copilot cannot accept drag-and-drop file uploads for analysis | Upload via the Document Upload Wizard, then search by name |
| **Complex multi-step workflows** — Extended analysis with multiple rounds of refinement | Use the Analysis Workspace for iterative deep analysis |

When you hit these limits, ask Copilot to hand off to the Analysis Workspace — it will transfer your context automatically.

---

## Frequently Asked Questions

### Why don't I see the Spaarke AI agent in Copilot?

The Spaarke AI agent must be deployed to your organization's app catalog by an administrator. Contact your IT administrator to verify the agent is installed. See the [Admin Guide](./M365-COPILOT-ADMIN-GUIDE.md) for deployment steps.

### Why can't I find a document I know exists?

Copilot searches only the documents you are authorized to access. If a document is in a matter or project you do not have permissions for, it will not appear. Contact the matter owner to request access.

### Can I attach a file from my desktop for analysis?

Not directly in Copilot. Use the Document Upload Wizard to upload the file first, then search for it by name in Copilot to run analysis.

### How long do playbook analyses take?

Quick playbooks (summaries, key dates) return results in seconds. Complex playbooks (full lease review, comprehensive risk assessment) may take longer and will redirect you to the Analysis Workspace to track progress.

### Can I use Copilot in Teams or Outlook?

The current release supports Copilot in Power Apps model-driven apps only. Teams and Outlook integration is planned for a future release.

### Where do drafted emails go?

Drafted emails are created as Communication records in the platform. You can review, edit, and send them from the Communication form, or use the action buttons on the email preview card.

### Can I undo a playbook analysis?

Playbook analyses are read-only — they do not modify your documents. You can run a different playbook at any time.

---

*For deployment and configuration, see the [M365 Copilot Admin Guide](./M365-COPILOT-ADMIN-GUIDE.md).*
