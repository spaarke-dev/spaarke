# Playbook: Create New Matter Pre-Fill

## Overview

Extracts key Matter fields from uploaded legal documents to pre-fill the Create New Matter wizard. Returns a flat JSON with display names for matterTypeName, practiceAreaName, matterName, summary, assignedAttorneyName, assignedParalegalName, assignedOutsideCounselName, and confidence score. Frontend resolves display names to Dataverse lookup GUIDs via fuzzy matching.

## Node Graph

```
[Document Upload]
        |
        v (BFF extracts text)
        |
+------------------------+
| Extract Matter Fields  |  <- Single AIAnalysis node
| (ACT-008, gpt-4o)     |
| output: matterFields   |
+------------------------+
        |
        v (flat JSON)
        |
[Frontend fuzzy-match → pre-fill wizard form]
```

**Design rationale**: Single-node pipeline because:
- BFF handles document text extraction before calling the playbook
- Frontend handles lookup resolution (fuzzy matching display names to GUIDs)
- No UpdateRecord needed — frontend writes to Dataverse after user confirmation

## Nodes

| # | Name | Type | Action | Model | Output Variable |
|---|------|------|--------|-------|-----------------|
| 1 | Extract Matter Fields | AIAnalysis (100000000) | ACT-008 (General Legal Document Review) | gpt-4o | matterFields |

## Scope Assignments

### Playbook-Level Scopes

| Type | Code | Name |
|------|------|------|
| Action | ACT-008 | General Legal Document Review |
| Skill | SKL-003 | Summary Generation |
| Skill | SKL-005 | Party Identification |
| Tool | TL-001 | DocumentSearch |
| Tool | TL-008 | PartyExtractor |

### Node-Level Scopes (Extract Matter Fields)

| Type | Code | Name |
|------|------|------|
| Skill | SKL-003 | Summary Generation |
| Skill | SKL-005 | Party Identification |
| Tool | TL-001 | DocumentSearch |
| Tool | TL-008 | PartyExtractor |

## Model Assignments

| Node | Model | Reason |
|------|-------|--------|
| Extract Matter Fields | gpt-4o | Extraction task — accuracy critical for structured output |

## Output Contract

```json
{
  "matterTypeName": "Litigation",
  "practiceAreaName": "Employment Law",
  "matterName": "Smith v. Acme Corp - Wrongful Termination",
  "summary": "...",
  "assignedAttorneyName": "Jane Smith",
  "assignedParalegalName": "John Doe",
  "assignedOutsideCounselName": "Wilson & Partners LLP",
  "confidence": 0.85
}
```

- All values are **display names only** — never GUIDs
- Fields are **omitted** rather than guessed when extraction confidence is low
- Frontend fuzzy-matches against Dataverse lookup tables (`sprk_mattertype_ref`, `sprk_practicearea_ref`, contacts)

## Definition File

Path: `projects/ai-json-prompt-schema-system/notes/playbook-definitions/create-new-matter-pre-fill.json`

## Deployment Results

- **Playbook ID**: `59ea4320-bd18-f111-8343-7ced8d1dc988`
- **Node ID**: `b8585126-bd18-f111-8343-7ced8d1dc988`
- **Nodes created**: 1
- **Scope associations**: 5 (playbook-level) + 4 (node-level)
- **Canvas layout**: saved (1 node, 0 edges)
- **Environment**: dev (spaarkedev1.crm.dynamics.com)
- **Deployed**: 2026-03-05

## BFF Integration

**Endpoint**: `POST /api/workspace/matters/pre-fill`

**Configuration** (in `appsettings.json` or Dataverse config):
```json
{
  "MatterPreFill": {
    "PlaybookId": "59ea4320-bd18-f111-8343-7ced8d1dc988",
    "TimeoutSeconds": 45
  }
}
```

**Integration guide**: `projects/ai-json-prompt-schema-system/notes/playbook-pre-fill-integration-guide.md`

## Related

- Existing Matter playbook: `18cf3cc8-02ec-f011-8406-7c1e520aa4df` (older version)
- Integration guide: [playbook-pre-fill-integration-guide.md](playbook-pre-fill-integration-guide.md)
