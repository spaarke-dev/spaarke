# Deployment Checklist: Task 146 - Deploy Dataverse Schema Changes

> **Task**: R2-146 - Deploy Dataverse Schema Changes (Playbook Capabilities Field)
> **Status**: Preparation complete (code-only environment - no live Dataverse access)
> **Date**: 2026-02-26

---

## Pre-Deployment Verification

### Schema Definition Verified in Code

- [x] `PlaybookCapabilities.cs` exists at `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/PlaybookCapabilities.cs`
- [x] Capability constants defined: `search`, `analyze`, `write_back`, `reanalyze`, `selection_revise`, `web_search`, `summarize`
- [x] Option set integer codes documented (100000000 through 100000006)
- [x] `SprkChatAgentFactory.cs` references `sprk_capabilities` for tool resolution
- [ ] **Note**: No Dataverse solution XML files for `sprk_playbook` entity found in the repository -- schema change needs to be created or exported from a dev environment where it was originally authored

### Capability Values Reference

From `PlaybookCapabilities.cs`:

| Capability | String Value | Option Set Code | Tool Class |
|-----------|-------------|-----------------|------------|
| Search | `search` | 100000000 | DocumentSearchTools |
| Analyze | `analyze` | 100000001 | AnalysisQueryTools |
| Write Back | `write_back` | 100000002 | WorkingDocumentTools |
| Reanalyze | `reanalyze` | 100000003 | AnalysisExecutionTools |
| Selection Revise | `selection_revise` | 100000004 | TextRefinementTools |
| Web Search | `web_search` | 100000005 | WebSearchTools |
| Summarize | `summarize` | 100000006 | KnowledgeRetrievalTools |

### Dependencies Confirmed

| Dependency | Task | Status |
|-----------|------|--------|
| Playbook capability field schema definition | R2-046 | Completed |
| Capability filtering in agent factory | R2-047 | Completed |

---

## Deployment Steps (Requires Live Dataverse Environment)

### Step 1: Verify or Create the sprk_capabilities Field

The `sprk_capabilities` field needs to be added to the `sprk_playbook` entity. Since no Dataverse solution XML was found in the repository, this may have been:
- Created directly in the dev environment during task R2-046
- Needs to be exported as part of the Spaarke solution

**Field Definition:**

| Property | Value |
|----------|-------|
| Entity | `sprk_playbook` |
| Field Logical Name | `sprk_capabilities` |
| Field Display Name | Capabilities |
| Data Type | Multiple lines of text (multiline text) |
| Max Length | 2000 |
| Description | Comma-separated list of capability strings that control which AI tools and action menu items are available for this playbook |
| Required Level | Optional |

**If field does not exist, create it:**

**Option A: Power Apps Maker Portal**
1. Open https://make.powerapps.com
2. Navigate to Tables > Playbook (`sprk_playbook`)
3. Columns > New Column
4. Configure as above
5. Save

**Option B: Dataverse Web API**
```
POST https://spaarkedev1.crm.dynamics.com/api/data/v9.2/EntityDefinitions(LogicalName='sprk_playbook')/Attributes
{
    "@odata.type": "#Microsoft.Dynamics.CRM.MemoAttributeMetadata",
    "SchemaName": "sprk_capabilities",
    "DisplayName": { "@odata.type": "Microsoft.Dynamics.CRM.Label", "LocalizedLabels": [{ "Label": "Capabilities", "LanguageCode": 1033 }] },
    "Description": { "@odata.type": "Microsoft.Dynamics.CRM.Label", "LocalizedLabels": [{ "Label": "Comma-separated capability strings for AI tool filtering", "LanguageCode": 1033 }] },
    "RequiredLevel": { "Value": "None" },
    "MaxLength": 2000
}
```

### Step 2: Export Updated Solution (if field was created manually)

```powershell
pac auth create --url https://spaarkedev1.crm.dynamics.com
pac solution export --name SpaarkeCore --path SpaarkeCore_export.zip --overwrite
```

Verify the exported solution XML includes the `sprk_capabilities` attribute definition on the `sprk_playbook` entity.

### Step 3: Import Solution to Dev Environment

If importing from another environment:

```powershell
pac solution import --path SpaarkeCore_updated.zip --force-overwrite --publish-changes
```

Monitor import logs for errors or warnings.

### Step 4: Verify Field on Playbook Entity

1. Open Dataverse admin center or solution explorer
2. Navigate to `sprk_playbook` entity > Columns
3. Verify `sprk_capabilities` field exists with correct data type (Multiline Text)

### Step 5: Add Field to Playbook Form

1. Open `make.powerapps.com` > Tables > Playbook > Forms
2. Open the Main form in the form designer
3. Add the `sprk_capabilities` field to an appropriate section
4. Set label: "Capabilities"
5. Set control: Multi-line text input
6. Save and Publish the form

### Step 6: Configure Capabilities for Test Playbooks

Update each existing playbook in the dev environment with appropriate capability values:

| Playbook | Capabilities Value |
|----------|-------------------|
| **Legal Analysis** | `search,analyze,write_back,reanalyze,summarize` |
| **Research** | `search,analyze,summarize,web_search` |
| **General Chat** | `search,summarize` |
| **Document Review** | `search,analyze,write_back,selection_revise,summarize` |
| **Financial Intelligence** | `search,analyze,write_back,reanalyze,summarize,web_search` |

**Via Dataverse Web API:**

```bash
# Update Legal Analysis playbook
curl -X PATCH https://spaarkedev1.crm.dynamics.com/api/data/v9.2/sprk_playbooks({playbook-id}) \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{"sprk_capabilities": "search,analyze,write_back,reanalyze,summarize"}'
```

**Via Power Apps:**
1. Open each Playbook record
2. Fill in the Capabilities field with the comma-separated values
3. Save each record

### Step 7: Verify Capabilities Are Readable via API

```bash
# Via BFF API actions endpoint (filtered by playbook capabilities)
curl https://spe-api-dev-67e2xz.azurewebsites.net/api/ai/scopes/actions \
  -H "Authorization: Bearer {token}"

# Verify: Response contains actions filtered by the active playbook's capabilities
```

```bash
# Via Dataverse Web API (direct field read)
curl "https://spaarkedev1.crm.dynamics.com/api/data/v9.2/sprk_playbooks?$select=sprk_name,sprk_capabilities" \
  -H "Authorization: Bearer {token}"

# Verify: sprk_capabilities field returns comma-separated capability strings
```

### Step 8: Publish All Customizations

```powershell
pac solution publish
```

---

## Key Files Reference

| File | Purpose |
|------|---------|
| `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/PlaybookCapabilities.cs` | Capability constants and validation |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` | Capability-based tool resolution |
| `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/ChatActionsResponse.cs` | Actions response filtered by capabilities |

---

## Validation Checklist

After deployment, verify:

- [ ] `sprk_capabilities` field exists on `sprk_playbook` entity in Dataverse
- [ ] Field appears and is editable on the Playbook form
- [ ] At least 2 playbooks are configured with capability values
- [ ] Capability values are readable via Dataverse Web API
- [ ] BFF API `/api/ai/scopes/actions` endpoint respects capability filtering
- [ ] `SprkChatAgentFactory.ResolveTools()` filters tools based on playbook capabilities
- [ ] All customizations are published

---

## Notes

- **Actual deployment requires live Dataverse environment access** which is not available in this code-only session
- No Dataverse solution XML files for the `sprk_playbook` entity exist in the repository -- the field may have been created directly in the dev environment or needs to be created
- The capability field uses a simple comma-separated text format rather than a multi-select option set, based on the `PlaybookCapabilities.cs` implementation
- The `PlaybookCapabilities.cs` file documents both string values and option set integer codes -- the multiline text approach uses the string values
- The `sprk_capabilities` field drives both action menu filtering (task 045) and tool resolution (task 047)
