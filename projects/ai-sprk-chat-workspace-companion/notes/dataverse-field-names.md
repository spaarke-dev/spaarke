# Dataverse Field Names — ai-sprk-chat-workspace-companion

> **Created**: 2026-03-16 (task 020)
> **Purpose**: Document Dataverse entity and field names discovered during implementation.
> Keeps field names out of code comments and provides a single reference for task 021.

---

## Confirmed Entity and Field Names (from existing codebase)

These names were confirmed by reading existing repository code during task 020.

### `sprk_analysisplaybook` — Analysis Playbook entity

| Field | Type | Notes |
|-------|------|-------|
| `sprk_analysisplaybookid` | Guid (PK) | Primary key |
| `sprk_name` | string | Display name |
| `sprk_description` | string? | Optional description |
| `sprk_playbookcapabilities` | OptionSetValueCollection | Multi-select option set — values 100000000–100000006 |

**Source**: `ChatContextMappingService.cs` line 274 (linked entity alias `pb`, columns `sprk_name`, `sprk_description`)
and `PlaybookCapabilities.cs` (capability string constants + integer value comments).

### `sprk_aichatcontextmapping` — Context mapping entity (Phase 1, existing)

| Field | Type | Notes |
|-------|------|-------|
| `sprk_aichatcontextmappingid` | Guid (PK) | Primary key |
| `sprk_playbookid` | EntityReference | Lookup to `sprk_analysisplaybook` |
| `sprk_entitytype` | string | Entity logical name (e.g. "sprk_matter") |
| `sprk_pagetype` | string | Page type (e.g. "main", "any") |
| `sprk_isdefault` | bool | Whether this is the default playbook for the context |
| `sprk_sortorder` | int | Sort order for playbook list |
| `statecode` | int | 0 = Active |

**Source**: `ChatContextMappingService.cs` QueryExpression column set.

### `sprk_aichatsummary` — Chat session summary entity (Phase 1, existing)

| Field | Type | Notes |
|-------|------|-------|
| `sprk_sessionid` | string | Session identifier |
| `sprk_tenantid` | string | Tenant ID |
| `sprk_playbookid` | string | Playbook ID (stored as string, not EntityReference) |
| `sprk_documentid` | string | Document ID |
| `sprk_messagecount` | int | Message count |
| `sprk_isarchived` | bool | Archive flag |

**Source**: `ChatDataverseRepository.cs` CreateSessionAsync.

### `sprk_aichatmessage` — Chat message entity (Phase 1, existing)

| Field | Type | Notes |
|-------|------|-------|
| `sprk_sessionid` | string | Session identifier (text field, NOT a lookup) |
| `sprk_role` | OptionSetValue | Role enum value |
| `sprk_content` | string | Message content |
| `sprk_tokencount` | int | Token count |
| `sprk_sequencenumber` | int | Message sequence number |

**Source**: `ChatDataverseRepository.cs` AddMessageAsync.

---

## Assumed/Unconfirmed Entity and Field Names (task 020 stub — confirm in task 021)

These are assumed field names for `sprk_analysisoutput`. They must be verified against
the deployed Dataverse schema before replacing the task 020 stub in `ResolveFromDataverseAsync`.

### `sprk_analysisoutput` — Analysis output entity (assumed)

| Field | Type | Assumed Name | Notes |
|-------|------|--------------|-------|
| Primary key | Guid | `sprk_analysisoutputid` | Standard Dataverse pattern |
| Playbook lookup | EntityReference | `sprk_playbookid` | Lookup to `sprk_analysisplaybook` |
| Analysis type | string or OptionSetValue | `sprk_analysistype` | May be option set or free text |
| Source file | EntityReference or string | `sprk_spefileid` or `sprk_sourcedocumentid` | SPE file reference |
| Source container | string | `sprk_containerid` | SPE container ID |
| Matter lookup | EntityReference | `sprk_matterid` | Lookup to matter entity (TBD entity name) |
| State code | int | `statecode` | 0 = Active (standard Dataverse field) |

### Matter entity (for matterType and practiceArea resolution)

| Field | Type | Assumed Name | Notes |
|-------|------|--------------|-------|
| Matter entity name | — | `sprk_matter` | Standard Spaarke naming |
| Matter type | OptionSetValue | `sprk_mattertype` | Likely option set |
| Practice area | OptionSetValue or string | `sprk_practicearea` | Likely option set |

---

## `sprk_playbookcapabilities` Option Set Integer Values

These values are confirmed in `PlaybookCapabilities.cs` and are the basis for
`AnalysisChatContextResolver.CapabilityToActionMap`.

| Integer Value | String Constant | Label | ActionType |
|--------------|-----------------|-------|-----------|
| 100000000 | `search` | Search | `chat` |
| 100000001 | `analyze` | Analyze | `chat` |
| 100000002 | `write_back` | Write Back | `chat` |
| 100000003 | `reanalyze` | Re-Analyze | `chat` |
| 100000004 | `selection_revise` | Revise Selection | `diff` |
| 100000005 | `web_search` | Web Search | `chat` |
| 100000006 | `summarize` | Summarize | `chat` |

**Note**: `selection_revise` (100000004) is the only `diff`-type action. All others are `chat`-type.

---

## Action Items for Task 021

When implementing the `GET /api/ai/chat/context-mappings/analysis/{analysisId}` endpoint:

1. Confirm `sprk_analysisoutput` entity name (check Dataverse schema or existing code references)
2. Confirm the primary key field name (likely `sprk_analysisoutputid`)
3. Confirm `sprk_playbookid` lookup field name on `sprk_analysisoutput`
4. Confirm source file and container field names
5. Confirm matter lookup field name and matter entity name
6. Confirm matter type and practice area field names on the matter entity
7. Replace stub in `AnalysisChatContextResolver.ResolveFromDataverseAsync` with actual
   `QueryExpression` following the `ChatContextMappingService.QueryMappingsAsync` pattern
8. Update this document with confirmed field names
