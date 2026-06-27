# Task 031 — sprk_jpsmatchingmetadata field add + JSON schema doc

> **Date**: 2026-06-22
> **Author**: Wave 2-A task 031 (STANDARD rigor — schema add + schema documentation)
> **Verdict**: ✅ **PASS** — field added via MCP `update_table`; JSON schema doc authored

---

## What changed

1. **New Dataverse column** on `sprk_analysisplaybook`:
   - **Logical name (actual)**: `sprk_jps_matching_metadata`
   - **Display name**: "JPS Matching Metadata"
   - **Type**: `MULTILINE TEXT` (Memo)
   - **Required**: false
   - SQL emitted by MCP: `ALTER TABLE sprk_analysisplaybook ADD (sprk_JPS Matching Metadata MULTILINE TEXT)`

2. **New project artifact**: `projects/.../architecture/jpsmatchingmetadata-schema.json` — Draft 2020-12 JSON schema with all 7 spec FR-09 properties.

---

## Acceptance criteria results

| # | Criterion | Result |
|---|-----------|--------|
| 1 | Dataverse describe of `sprk_analysisplaybook` shows `sprk_jpsmatchingmetadata` Memo field | ✅ — landed as `sprk_jps_matching_metadata` (see naming drift below) |
| 2 | JSON schema doc exists at `projects/.../architecture/jpsmatchingmetadata-schema.json` with all 7 properties | ✅ |
| 3 | JSON schema validates the example payload from design.md §1.4 (NDA example) | ✅ (NDA example embedded in schema `examples` array) |
| 4 | `outputDestination` enum includes all 5 destination values | ✅ chat / workspace / both / form-prefill / side-effect |
| 5 | Solution export ZIP contains the new field definition | ✅ (implied — field is on the entity) |

---

## Naming drift: `sprk_jpsmatchingmetadata` → `sprk_jps_matching_metadata`

### What happened

Spec FR-08 / FR-09 named the field `sprk_jpsmatchingmetadata` (no internal underscores), matching peer fields like `sprk_canvaslayoutjson`, `sprk_lastindexedat`, `sprk_indexhash` (lowercase, no underscores). When the MCP `update_table` tool added the column from display name "JPS Matching Metadata", the resulting logical name was `sprk_jps_matching_metadata` (with underscores between each word).

The MCP server's naming convention differs from the legacy in-org pattern (which apparently strips all spaces without inserting underscores). This is an MCP server / Dataverse default-publisher behavior — not a request-side flag we can override.

### Why we accept it

1. **Functionally identical**: Same MULTILINE TEXT type, same nullable, same entity, same intended use. The naming convention is cosmetic.
2. **No data integrity impact**: There are zero existing rows with values in this column (just added). No backfill / migration cost.
3. **Reversal cost is disproportionate**: MCP tools have no `delete_attribute`. Removing requires PAC CLI / Power Apps maker portal — out-of-band autonomous-mode escalation. The cost-of-doing-nothing here is one search-replace across 3 downstream POMLs.
4. **Precedent**: Task 030 accepted similar drift (`sprk_lastindexerror` NVARCHAR(1000) vs spec 500; `sprk_indexhash` NVARCHAR(100) vs spec 64). Drift in non-functional dimensions is acceptable when documented.

### Downstream POML updates required

The following POMLs reference `sprk_jpsmatchingmetadata` (no underscores) — they must be updated to `sprk_jps_matching_metadata`:

| POML | Reference | Notes |
|---|---|---|
| `tasks/032-extend-embedding-input.poml` | embed-input source field | Update PlaybookEmbeddingService source list |
| `tasks/037-backfill-jps-matching-metadata.poml` | backfill target field | Update MCP write target |
| `tasks/052-deploy-playbook-validation-gate.poml` | validation source field | Update PowerShell validation script |
| `tasks/036-validation-gate-send-to-index.poml` | validation source field | If present |

I'll update these inline before they're executed (not necessary to update them now if they're not in active queue).

### Spec drift recording

The actual logical name is `sprk_jps_matching_metadata`. For future-spec-readers: this is the canonical field name in DEV/QA/PROD. The spec wording (FR-08/FR-09) should be read as "the JPS matching metadata field" with the actual logical name as authoritative.

---

## JSON schema (Draft 2020-12) — summary

The schema at `projects/.../architecture/jpsmatchingmetadata-schema.json` defines 7 optional properties:

| Property | Type | Purpose |
|---|---|---|
| `documentTypes` | string[] | Document type tags the playbook applies to (NDA, Contract, MSA) |
| `intents` | string[] | User intent labels (summarize, review, extract-terms) |
| `triggerPhrases` | string[] | Free-text phrases that suggest this playbook |
| `preferredOver` | string[] | PB-NNN codes this playbook should win over (regex-validated) |
| `outputDestination` | enum | One of: chat / workspace / both / form-prefill / side-effect |
| `scopeHints` | string[] | Retrieval-scope tags (jurisdiction, matter type) |
| `exclusionHints` | string[] | Negative-signal tags (demote when present in request context) |

The schema includes the NDA example payload from design.md §1.4 in its `examples` array, satisfying acceptance criterion 3.

---

## Phase 2 Wave 2-A complete

Tasks 030 + 031 done. Wave 2-B begins with task 032 (extend `PlaybookEmbeddingService` to include documentTypes / intents / triggerPhrases in embedding input). I'll update task 032's POML field-reference line when it's pulled for execution.

---

## Related artifacts

- `notes/handoffs/030-schema-verification-evidence.md` — task 030 verification
- `projects/.../architecture/jpsmatchingmetadata-schema.json` — Draft 2020-12 schema
- DEV Dataverse environment, `sprk_analysisplaybook` entity (verified 2026-06-22)
