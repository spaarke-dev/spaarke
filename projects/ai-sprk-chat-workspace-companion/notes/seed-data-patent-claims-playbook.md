# Seed Data: Patent Claims Analysis Playbook

> **Task**: 022 — Seed Analysis Playbook Test Data and Endpoint Tests
> **Spec Reference**: FR-06 — Patent-claims playbook shows extract-claims and prior-art-search actions
> **Status**: Documentation only — manual Dataverse record creation required

---

## Overview

The patent-claims analysis playbook demonstrates the `sprk_playbookcapabilities` multi-select field
and its mapping to inline action chips in the SprkChat Analysis Workspace companion.

The `AnalysisChatContextResolver.CapabilityToActionMap` static dictionary converts Dataverse option
set integer values to `InlineActionInfo` descriptors. Creating this record in Dataverse (or verifying
it in the dev environment) validates the end-to-end mapping from playbook record → API response →
QuickActionChips rendered in SprkChat.

---

## Dataverse Record to Create

### Entity: `sprk_analysisplaybook`

| Field | Value | Notes |
|-------|-------|-------|
| `sprk_name` | `Patent Claims Analysis` | Display name shown in playbook selector UI |
| `sprk_description` | `Analyzes patent claims documents: extracts claims, searches prior art, and supports targeted text revision.` | Tooltip / help text |
| `sprk_playbookcapabilities` | `[100000000, 100000001, 100000004]` | Multi-select option set — see values below |
| Status (statecode) | Active (0) | Must be active to appear in context resolution |

### `sprk_playbookcapabilities` Option Set Values

| Integer Value | String Constant | Mapped Inline Action | ActionType |
|---------------|-----------------|----------------------|------------|
| `100000000` | `search` | "Search" — prior-art-search query | `chat` |
| `100000001` | `analyze` | "Analyze" — extract-claims analysis | `chat` |
| `100000004` | `selection_revise` | "Revise Selection" — edit claim text with diff view | `diff` |

**Why these three values?**

- `search (100000000)` → powers prior-art-search inline action (FR-06)
- `analyze (100000001)` → powers extract-claims inline action (FR-06)
- `selection_revise (100000004)` → enables targeted revision of specific claim text with diff preview
  (this is the only `diff`-type action — it opens `DiffReviewPanel`, not a chat stream)

---

## Capability-to-Action Mapping Reference

Full mapping from `AnalysisChatContextResolver.CapabilityToActionMap`:

| Dataverse Int | Id (string) | Label | ActionType | Description |
|---------------|-------------|-------|------------|-------------|
| 100000000 | `search` | Search | `chat` | Search knowledge sources |
| 100000001 | `analyze` | Analyze | `chat` | Analyze with AI |
| 100000002 | `write_back` | Write Back | `chat` | Write AI content to document |
| 100000003 | `reanalyze` | Re-Analyze | `chat` | Re-run analysis |
| 100000004 | `selection_revise` | Revise Selection | `diff` | Revise selected text and show diff |
| 100000005 | `web_search` | Web Search | `chat` | Search the web |
| 100000006 | `summarize` | Summarize | `chat` | Summarize content |

---

## Manual Setup Instructions

### Option A: Power Apps / Dataverse Studio

1. Navigate to `make.powerapps.com` → select the **Dev** environment (`spaarkedev1`)
2. Go to **Tables** → search for `sprk_analysisplaybook`
3. Click **+ New row**
4. Set:
   - **Name** (`sprk_name`): `Patent Claims Analysis`
   - **Description** (`sprk_description`): see value above
   - **Playbook Capabilities** (`sprk_playbookcapabilities`): select `Search`, `Analyze`, `Revise Selection`
5. **Save**

### Option B: PAC CLI (if available)

```bash
# Export the current solution to get the entity schema
pac solution export --path temp-export.zip --name SpaarkeAI --managed false

# Alternatively, use the Data CLI if available
# spaarke-data import --entity sprk_analysisplaybook --file seed-data-patent-claims-playbook.json
```

### Option C: Direct OData POST (Dev environment)

```http
POST https://spaarkedev1.crm.dynamics.com/api/data/v9.2/sprk_analysisplaybooks
Authorization: Bearer {token}
Content-Type: application/json

{
  "sprk_name": "Patent Claims Analysis",
  "sprk_description": "Analyzes patent claims documents: extracts claims, searches prior art, and supports targeted text revision.",
  "sprk_playbookcapabilities": "100000000,100000001,100000004"
}
```

> **Note**: Multi-select option set values in OData Web API are passed as a comma-separated string
> of integer values. Verify the exact serialisation format by inspecting an existing record first.

---

## Verification

After creating the record, verify the API response:

```http
GET https://spe-api-dev-67e2xz.azurewebsites.net/api/ai/chat/context-mappings/analysis/{analysisId}
Authorization: Bearer {token}
```

Expected response structure (once Dataverse integration is complete in task 021):

```json
{
  "defaultPlaybookId": "{playbook-guid}",
  "defaultPlaybookName": "Patent Claims Analysis",
  "availablePlaybooks": [
    {
      "id": "{playbook-guid}",
      "name": "Patent Claims Analysis",
      "description": "Analyzes patent claims documents..."
    }
  ],
  "inlineActions": [
    { "id": "search",           "label": "Search",           "actionType": "chat", "description": "Search knowledge sources" },
    { "id": "analyze",          "label": "Analyze",          "actionType": "chat", "description": "Analyze with AI" },
    { "id": "selection_revise", "label": "Revise Selection", "actionType": "diff", "description": "Revise selected text and show diff" }
  ],
  "knowledgeSources": [],
  "analysisContext": {
    "analysisId": "{analysisId}",
    "analysisType": null,
    "matterType": null,
    "practiceArea": null,
    "sourceFileId": null,
    "sourceContainerId": null
  }
}
```

---

## Acceptance Criteria (from task 022, spec FR-06)

- [x] Documented entity name: `sprk_analysisplaybook`
- [x] Documented record name: `Patent Claims Analysis`
- [x] Documented `sprk_playbookcapabilities` values: `[100000000, 100000001, 100000004]`
- [x] `search (100000000)` → prior-art-search inline action (actionType=`chat`)
- [x] `analyze (100000001)` → extract-claims inline action (actionType=`chat`)
- [x] `selection_revise (100000004)` → diff-type inline action (actionType=`diff`, opens DiffReviewPanel)

---

*Created: 2026-03-16 | Task: 022 | Project: ai-sprk-chat-workspace-companion*
