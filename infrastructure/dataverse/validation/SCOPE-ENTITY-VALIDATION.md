# Scope Entity Schema Reference

> **Version**: 1.1
> **Last Updated**: December 15, 2025
> **Task**: 073 - Add Validation Rules for Scope Entities

Reference guide for AI Analysis scope entity schemas. Validation is enforced at the **Dataverse column level** - no additional scripts or business rules required.

---

## Entity: sprk_analysisaction

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| sprk_name | Text (100) | Yes | Action display name |
| sprk_description | Text (500) | No | Optional description |
| sprk_systemprompt | Multiline Text | Yes | The system prompt sent to AI |
| sprk_sortorder | Whole Number | Yes | Display order (default: 10) |

---

## Entity: sprk_analysisskill

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| sprk_name | Text (100) | Yes | Skill display name |
| sprk_description | Text (500) | No | Optional description |
| sprk_promptfragment | Multiline Text | Yes | Prompt text appended to instructions |
| sprk_category | OptionSet | No | Tone (0), Style (1), Format (2), Expertise (3) |

---

## Entity: sprk_analysisknowledge

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| sprk_name | Text (200) | Yes | Knowledge source name |
| sprk_description | Text (500) | No | Optional description |
| sprk_knowledgetype | OptionSet | Yes | Document (0), Rule (1), Template (2), RagIndex (3) |
| sprk_content | Multiline Text | No | Inline content for Rule/Template types |
| sprk_knowledgedeploymentid | Lookup | No | Required for RagIndex type |
| sprk_containerid | Text (100) | No | SPE container for RAG |
| sprk_filterpath | Text (200) | No | Path filter for RAG indexing |

### Knowledge Type Values

| Value | Label | Usage |
|-------|-------|-------|
| 717820000 | Document | Reference document with extracted content |
| 717820001 | Rule | Business rule (inline content) |
| 717820002 | Template | Template document (inline content) |
| 717820003 | RagIndex | RAG index (requires deployment + async search) |

---

## Entity: sprk_analysistool

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| sprk_name | Text (100) | Yes | Tool display name |
| sprk_description | Text (500) | No | Optional description |
| sprk_tooltype | OptionSet | Yes | EntityExtractor (0), ClauseAnalyzer (1), Custom (2) |
| sprk_handlerclass | Text (200) | No | Required for Custom type |
| sprk_configuration | Multiline Text | No | JSON configuration |

---

## Entity: sprk_analysisplaybook

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| sprk_name | Text (200) | Yes | Playbook display name |
| sprk_description | Text (500) | No | Optional description |
| sprk_actionid | Lookup | Yes | The action this playbook uses |
| sprk_ispublic | Boolean | No | Whether playbook is shared (default: false) |

### N:N Relationships

- `sprk_playbook_skill` → Skills included in playbook
- `sprk_playbook_knowledge` → Knowledge sources included
- `sprk_playbook_tool` → Tools included

---

## Entity: sprk_knowledgedeployment

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| sprk_name | Text (200) | Yes | Deployment name |
| sprk_deploymentmodel | OptionSet | Yes | SharedIndex (0), DedicatedIndex (1) |
| sprk_indexname | Text (100) | Yes | Azure AI Search index name |
| sprk_searchendpoint | Text (500) | No | Override endpoint (null = use default) |

---

## Notes

- Required fields are enforced at the Dataverse column level
- Lookups enforce referential integrity automatically
- Option sets constrain valid values
- No JavaScript web resources or Business Rules needed (per ADR-006)
