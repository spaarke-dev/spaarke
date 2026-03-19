# Task 001 — Dataverse Schema Field Names

> Created: 2026-03-17
> Purpose: Confirmed logical names for downstream tasks (R2-005 seed data, R2-006 deployment, R2-019, R2-020, R2-021)

## sprk_scope Entity

| Logical Name | Display Name | Type | Details |
|---|---|---|---|
| `sprk_capabilities` | Capabilities | MultiSelectPicklist | Scope-level tool/action declarations |
| `sprk_searchguidance` | Search Guidance | Memo (multiline text) | Max 4000 chars; free-text AI web search guidance |

### sprk_capabilities Option Set Values

| Value | Label | Description |
|---|---|---|
| 100000000 | search | Document/knowledge search capability |
| 100000001 | analyze | Analysis execution capability |
| 100000002 | web_search | Web search synthesis capability |
| 100000003 | write_back | Write-back to editor capability |
| 100000004 | summarize | Content summarization capability |

## sprk_analysisplaybook Entity

| Logical Name | Display Name | Type | Details |
|---|---|---|---|
| `sprk_triggerphrases` | Trigger Phrases | Memo (multiline text) | Max 4000 chars; newline-delimited phrases for semantic matching |
| `sprk_recordtype` | Record Type | String (single line) | Max 100 chars; e.g., "matter", "project", "event" |
| `sprk_entitytype` | Entity Type | String (single line) | Max 100 chars; Dataverse logical name e.g., "sprk_analysisoutput" |
| `sprk_tags` | Tags | Memo (multiline text) | Max 2000 chars; comma-delimited tags for grouping/filtering |

## Web API Query Examples

```
# Query scope capabilities
GET /api/data/v9.2/sprk_scopes?$select=sprk_capabilities,sprk_searchguidance

# Query playbook trigger metadata
GET /api/data/v9.2/sprk_analysisplaybooks?$select=sprk_triggerphrases,sprk_recordtype,sprk_entitytype,sprk_tags

# Verify field metadata
GET /api/data/v9.2/EntityDefinitions(LogicalName='sprk_scope')/Attributes(LogicalName='sprk_capabilities')
GET /api/data/v9.2/EntityDefinitions(LogicalName='sprk_scope')/Attributes(LogicalName='sprk_searchguidance')
GET /api/data/v9.2/EntityDefinitions(LogicalName='sprk_analysisplaybook')/Attributes(LogicalName='sprk_triggerphrases')
GET /api/data/v9.2/EntityDefinitions(LogicalName='sprk_analysisplaybook')/Attributes(LogicalName='sprk_recordtype')
GET /api/data/v9.2/EntityDefinitions(LogicalName='sprk_analysisplaybook')/Attributes(LogicalName='sprk_entitytype')
GET /api/data/v9.2/EntityDefinitions(LogicalName='sprk_analysisplaybook')/Attributes(LogicalName='sprk_tags')
```

## Deployment Scripts

| Script | Entity | Fields |
|---|---|---|
| `scripts/Create-ScopeCapabilityFields.ps1` | sprk_scope | sprk_capabilities, sprk_searchguidance |
| `scripts/Create-PlaybookTriggerFields.ps1` | sprk_analysisplaybook | sprk_triggerphrases, sprk_recordtype, sprk_entitytype, sprk_tags |

## Notes

- All field logical names use lowercase (Dataverse convention)
- The `sprk_capabilities` multi-select picklist returns as a collection of OptionSetValue integers via the Web API
- Follow the existing pattern in `ChatDataverseRepository.cs` for reading `sprk_playbookcapabilities` when adding code to read `sprk_capabilities`
- Publisher prefix: `sprk_`
- Solution: `spaarke_core`
