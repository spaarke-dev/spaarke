# Knowledge Source Deployment Status

> **Task**: AIRA-010
> **Date**: 2026-03-04
> **Script**: `projects/ai-spaarke-platform-enhancements-r1/scripts/Create-KnowledgeSourceRecords.ps1`

---

## 1. Script Review Results

### Overview

The script creates 10 system Knowledge Source records (KNW-001 through KNW-010) in the `sprk_analysisknowledges` Dataverse entity collection. It reads markdown content from `notes/design/knowledge-sources/KNW-*.md` files and stores them in `sprk_contenttext` for RAG retrieval via Azure AI Search.

### Fields Populated Per Record

| Field | Purpose | Example Value |
|-------|---------|---------------|
| `sprk_name` | Display name | "Common Contract Terms Glossary" |
| `sprk_externalid` | Alternate key for idempotent upsert | "KNW-001" |
| `sprk_contenttext` | Full markdown content body | (loaded from KNW-*.md file) |
| `sprk_contenttype` | Content classification | "Reference" |
| `sprk_isactive` | Active flag | `true` |
| `sprk_issystem` | System-level flag | `true` |
| `sprk_tenantid` | Tenant scope (ADR-014) | "system" |

### Idempotency

The script is idempotent. Before creating each record, it checks for an existing record with the same `sprk_externalid` using the alternate key lookup `sprk_analysisknowledges(sprk_externalid='KNW-XXX')`. If found, the record is skipped.

### Prerequisites

- Azure CLI installed and authenticated (`az login`)
- Access to `spaarkedev1.crm.dynamics.com` (System Customizer or System Administrator role)
- PowerShell 7+ (`pwsh`) -- Windows PowerShell 5.1 fails due to UTF-8 encoding issues with em-dash characters in the script

### ADR Compliance

- **ADR-002**: Records created via data import script, no plugin processing involved
- **ADR-014**: `tenantId="system"` for all system-level knowledge sources (shared across tenants)

### N:N Relationship (sprk_playbook_knowledge)

**NOT included in this script.** The script only creates the `sprk_analysisknowledge` entity records. The N:N relationship between playbooks and knowledge sources (`sprk_playbook_knowledge`) is not set up by this script. Associating knowledge sources to playbooks must be done separately -- either via a dedicated association script, manual Dataverse configuration, or through the Scope Config Editor PCF.

---

## 2. Content Completeness Check

All 10 KNW content files exist and contain substantial, well-structured legal reference content.

| File | Title | Word Count | Status |
|------|-------|------------|--------|
| KNW-001-contract-terms-glossary.md | Common Contract Terms Glossary | ~1,855 | Complete |
| KNW-002-nda-checklist.md | NDA Review Checklist | ~1,844 | Complete |
| KNW-003-lease-agreement-standards.md | Lease Agreement Standards | ~1,740 | Complete |
| KNW-004-invoice-processing-guide.md | Invoice Processing Guide | ~1,635 | Complete |
| KNW-005-sla-metrics-reference.md | SLA Metrics Reference | ~1,560 | Complete |
| KNW-006-employment-law-quick-reference.md | Employment Law Quick Reference | ~1,617 | Complete |
| KNW-007-ip-assignment-clause-library.md | IP Assignment Clause Library | ~1,918 | Complete |
| KNW-008-termination-and-remedy-provisions.md | Termination and Remedy Provisions | ~1,786 | Complete |
| KNW-009-governing-law-and-jurisdiction-guide.md | Governing Law and Jurisdiction Guide | ~1,722 | Complete |
| KNW-010-legal-red-flags-catalog.md | Legal Document Red Flags Catalog | ~2,792 | Complete |

**Total**: ~18,469 words across 2,239 lines. All files follow a consistent structure with metadata headers, overview sections, detailed content, and review checklists/tables.

---

## 3. Deployment Status

### Execution Attempt (2026-03-04)

| Step | Result | Details |
|------|--------|---------|
| Authentication | SUCCESS | Azure CLI token obtained for spaarkedev1.crm.dynamics.com |
| Query existing records | FAILED (400) | Entity/collection `sprk_analysisknowledges` not found or schema mismatch |
| Content file loading | SUCCESS | All 10 files loaded successfully |
| Record creation (all 10) | FAILED (400) | Same entity/collection issue |
| Verification | FAILED (400) | Same entity/collection issue |

**Root Cause**: The `sprk_analysisknowledges` entity collection does not appear to exist in the Dataverse environment yet, or the collection name in the script does not match the actual entity plural name. The 400 Bad Request response on all API calls (including queries) confirms this is a schema-level issue, not an auth or data issue.

### Encoding Issue

**Windows PowerShell 5.1 cannot parse this script** due to UTF-8 em-dash characters on line 155 (`"(none --- safe to create all 10)"`). This causes cascading parse errors. The script must be run with **PowerShell 7+ (`pwsh`)**.

### Manual Steps Required Before Deployment

1. **Verify entity existence**: Confirm that the `sprk_analysisknowledge` entity has been created in Dataverse (this may be a prerequisite from an earlier task like AIPL-001 or entity schema setup)
2. **Verify collection name**: Check the actual OData collection name -- it may be `sprk_analysisknowledges` or something else depending on how the entity was registered. The script uses `sprk_analysisknowledges` on line 79
3. **Verify alternate key**: Ensure `sprk_externalid` is configured as an alternate key on the entity
4. **Verify field schema names**: Confirm all field logical names match: `sprk_name`, `sprk_externalid`, `sprk_contenttext`, `sprk_contenttype`, `sprk_isactive`, `sprk_issystem`, `sprk_tenantid`
5. **Re-run with pwsh**: `pwsh -File projects/ai-spaarke-platform-enhancements-r1/scripts/Create-KnowledgeSourceRecords.ps1`

### Post-Deployment Steps (After Records Created)

1. **AI Search Indexing** (requires AIPL-018 KnowledgeBaseEndpoints.cs deployment):
   ```
   POST /api/ai/knowledge/index/batch
   Body: { "tenantId": "system", "filter": "system" }
   ```
2. **Verify indexing**:
   ```
   GET /api/ai/knowledge/test-search?query=force+majeure&tenantId=system&topK=3
   ```
3. **N:N Playbook Associations**: Associate knowledge sources to playbooks via `sprk_playbook_knowledge` relationship (separate task/script needed)

---

## 4. Recommendations

1. **Fix encoding**: Replace the em-dash on line 155 with a plain ASCII dash so the script works with both PowerShell 5.1 and 7+
2. **Add error detail logging**: The `Invoke-DataverseApi` function catches errors but the stream reader approach for extracting response body details may not work in PowerShell 7 (different exception types). Consider using `$_.ErrorDetails.Message` instead
3. **Create association script**: A separate script or manual step is needed to create `sprk_playbook_knowledge` N:N associations after both playbook and knowledge source records exist
4. **Confirm entity schema**: Before re-running, verify the entity plural name and alternate key configuration in Dataverse
