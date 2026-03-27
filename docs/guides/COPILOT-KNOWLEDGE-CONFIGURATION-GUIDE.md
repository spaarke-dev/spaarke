# Copilot Knowledge Configuration Guide

> **Last Updated**: 2026-03-27
> **Purpose**: Configure M365 Copilot to understand Spaarke's data model, vocabulary, and domain concepts
> **When**: Run after every Dataverse schema change, new entity, or vocabulary update

---

## Why This Matters

M365 Copilot in model-driven apps reads Dataverse metadata to understand your data. Without configuration, Copilot defaults to standard Dataverse entities -- when a user says "tasks", Copilot queries the standard Task activity entity instead of Spaarke's `sprk_event` table.

Three mechanisms teach Copilot your data model:

| Mechanism | What It Does | Where Configured |
|-----------|-------------|-----------------|
| **Entity Descriptions** | Tells Copilot what each table contains | Dataverse table metadata |
| **Glossary Terms** | Maps domain vocabulary to tables/columns | Copilot Studio or Dataverse API |
| **Synonyms** | Maps column schema names to user-friendly terms | Copilot Studio or Dataverse API |

---

## Automated Configuration

### Scripts

| Script | Purpose | When to Run |
|--------|---------|------------|
| `scripts/Update-CopilotEntityDescriptions.ps1` | Sets AI-optimized descriptions on all Spaarke entities | After creating/modifying entities |
| `scripts/Configure-CopilotKnowledge.ps1` | Adds glossary terms and column synonyms | After schema changes or vocabulary updates |

### Running the Scripts

```powershell
# Prerequisites
az login
az account set --subscription "484bc857-3802-427f-9ea5-ca47b43db0f0"

# Step 1: Update entity descriptions
.\scripts\Update-CopilotEntityDescriptions.ps1

# Step 2: Configure glossary terms and synonyms
.\scripts\Configure-CopilotKnowledge.ps1

# Note: Changes take 15-30 minutes to take effect in Copilot
```

---

## Manual Configuration via Copilot Studio

For settings that cannot be scripted, configure in Copilot Studio:

### Step 1: Open Copilot Studio for Your App

1. Open your model-driven app in **App Designer**
2. Click **"..." > "Configure in Copilot Studio"**
3. This opens the dedicated Copilot Studio agent for your app

### Step 2: Add Dataverse Knowledge Sources

1. Go to **Knowledge** page
2. Click **Add Knowledge** > Select **Dataverse**
3. Add these tables (up to 15 per knowledge source):

| Table | Why |
|-------|-----|
| `sprk_event` | Tasks, deadlines, assignments |
| `sprk_matter` | Legal matters and cases |
| `sprk_document` | Document records |
| `sprk_project` | Workstreams within matters |
| `sprk_analysisplaybook` | AI analysis playbooks |
| `sprk_analysisoutput` | Analysis results and findings |
| `sprk_invoice` | Billing and invoices |
| `sprk_communication` | Email correspondence |
| `sprk_container` | SPE storage containers |
| `sprk_party` | People and orgs on matters |

4. For each knowledge source, write a detailed description

### Step 3: Add Glossary Terms

In Copilot Studio > Knowledge > Glossary:

| Term | Definition |
|------|-----------|
| task | A record in the sprk_event table. Spaarke uses Events for tasks and action items. Do NOT use the standard Dataverse Task entity. |
| overdue task | A sprk_event record where status is Active and due date is earlier than today. |
| deadline | A sprk_event record with event type Deadline. |
| assignment | A sprk_event record owned by a specific user. Filter by ownerid. |
| matter | A sprk_matter record - a legal matter or case. |
| case | Same as matter - a sprk_matter record. |
| document | A sprk_document record linked to a file in SharePoint Embedded. |
| contract | A sprk_document record where document type is Contract. |
| playbook | A sprk_analysisplaybook record - an AI analysis workflow. |
| analysis tools | Refers to sprk_analysisplaybook records. |
| invoice | A sprk_invoice record - a billing record associated with a matter. |
| outside counsel | A sprk_party record with role Outside Counsel. |
| project | A sprk_project record - a workstream within a matter. |
| analysis results | A sprk_analysisoutput record with findings from a playbook execution. |

### Step 4: Add Column Synonyms

In Copilot Studio > Knowledge > select a table > Synonyms:

**sprk_event columns:**
| Column | Synonyms |
|--------|----------|
| sprk_duedate | due date, deadline, due by |
| sprk_name | task name, event name, title |
| sprk_eventtype | event type, task type |
| sprk_status | status, task status |
| ownerid | assignee, assigned to, owner |

**sprk_matter columns:**
| Column | Synonyms |
|--------|----------|
| sprk_name | matter name, case name |
| sprk_mattertype | matter type, case type |
| sprk_status | status, matter status |

**sprk_document columns:**
| Column | Synonyms |
|--------|----------|
| sprk_name | document name, file name |
| sprk_documenttype | document type, file type |
| sprk_matterid | matter, case |

### Step 5: Publish

1. Click **Publish** in Copilot Studio
2. Wait 15-30 minutes for changes to propagate
3. Test in MDA Copilot

---

## Prerequisites

### Dataverse Search Must Be Enabled

Copilot Studio agents require Dataverse Search:

1. **Power Platform Admin Center** > Environment > Settings
2. **Search** section > **Dataverse Search** = **ON**
3. Configure searchable columns for each entity

### Environment Features

1. **Power Platform Admin Center** > Environment > Settings > Features
2. Ensure **"Copilot"** is ON
3. Ensure **"Allow AI-powered Copilot features"** is ON

---

## Development Process Integration

### When to Update Copilot Knowledge

| Trigger | Action |
|---------|--------|
| New Dataverse entity created | Run `Update-CopilotEntityDescriptions.ps1`, add to knowledge sources |
| Entity renamed or repurposed | Update entity description, update glossary terms |
| New columns added | Add synonyms for columns with schema-prefixed names |
| New domain vocabulary | Add glossary terms mapping user language to entities |
| New playbook created | No action needed - Copilot queries sprk_analysisplaybook dynamically |
| New environment deployed | Run both scripts, configure Copilot Studio knowledge sources |
| BYOK customer deployment | Include Copilot knowledge config in deployment runbook |

### Checklist for New Entity

When adding a new Dataverse entity to Spaarke:

- [ ] Add entity description in `Update-CopilotEntityDescriptions.ps1`
- [ ] Add glossary terms in `Configure-CopilotKnowledge.ps1`
- [ ] Add column synonyms in `Configure-CopilotKnowledge.ps1`
- [ ] Add as Dataverse knowledge source in Copilot Studio
- [ ] Run scripts against dev environment
- [ ] Test Copilot queries against the new entity
- [ ] Include in deployment runbook for other environments

### Checklist for Deployment to New Environment

- [ ] Run `Update-CopilotEntityDescriptions.ps1 -EnvironmentUrl <url>`
- [ ] Run `Configure-CopilotKnowledge.ps1 -EnvironmentUrl <url>`
- [ ] Enable Dataverse Search in the environment
- [ ] Enable Copilot features in the environment
- [ ] Configure Copilot Studio knowledge sources for the MDA app
- [ ] Add glossary terms and synonyms in Copilot Studio
- [ ] Publish Copilot Studio agent
- [ ] Wait 15-30 minutes, then test

---

## Declarative Agent Instructions

In addition to Dataverse knowledge configuration, the Declarative Agent's instructions in `declarativeAgent.json` provide explicit guidance:

- **Vocabulary mapping table** - maps user terms to API functions
- **CRITICAL routing rule** - always use API Plugin functions, never native Dataverse
- **Domain concepts** - explains Spaarke's entity model

See `src/solutions/CopilotAgent/declarativeAgent.json` for the current instructions.

**Limit**: Instructions are capped at 8000 characters. Keep them focused on routing rules and vocabulary, not detailed entity descriptions -- those belong in Dataverse metadata.

---

## Troubleshooting

| Problem | Cause | Fix |
|---------|-------|-----|
| Copilot queries standard Task entity | Missing glossary term for "task" | Add glossary: "task" = sprk_event |
| Copilot gives generic answers | API Plugin not invoked | Check webApplicationInfo in manifest, verify auth |
| Copilot says "I can't find that" | Dataverse Search not enabled | Enable in PP Admin Center |
| Changes not reflected | Propagation delay | Wait 15-30 minutes after changes |
| Copilot ignores custom tables | Tables not in knowledge sources | Add via Copilot Studio |
| Column names confusing Copilot | Schema-prefixed names | Add synonyms mapping to friendly names |

---

## Reference Links

- [Improve copilot responses from Dataverse](https://learn.microsoft.com/en-us/power-apps/maker/data-platform/data-platform-copilot) - synonyms + glossary
- [Customize Copilot chat in model-driven apps](https://learn.microsoft.com/en-us/power-apps/maker/model-driven-apps/customize-copilot-chat) - Copilot Studio workflow
- [Add Dataverse tables as knowledge source](https://learn.microsoft.com/en-us/microsoft-copilot-studio/knowledge-add-dataverse) - step-by-step
- [CopilotSynonyms table reference](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/reference/entities/copilotsynonyms) - API reference
- [Configure Dataverse search](https://learn.microsoft.com/en-us/power-platform/admin/configure-relevance-search-organization) - prerequisite

---

*This guide should be followed for every new Spaarke environment deployment and after any Dataverse schema changes.*
