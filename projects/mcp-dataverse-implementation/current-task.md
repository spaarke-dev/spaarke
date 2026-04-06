# Current Task State - MCP Dataverse Implementation

> **Last Updated**: 2026-04-06 02:00 (by context-handoff)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | MCP Dataverse Implementation — Phase 1 + Phase 2 |
| **Step** | COMPLETE |
| **Status** | ✅ Phase 1 (MCP server) + Phase 2 (Dataverse Skills) both complete |
| **Next Action** | Start using MCP tools + Skills in daily workflow. Phase 3 (skill integration updates) can happen organically. |

### Files Modified This Session
- `.mcp.json` — NEW: MCP server config (stdio proxy via `@microsoft/dataverse` npm package)
- `projects/mcp-dataverse-implementation/notes/reference-dataverse-mcp.md` — updated Configuration section with correct stdio proxy setup

### Critical Context
- **Configuration uses stdio local proxy**, NOT direct HTTP. Command: `npx -y @microsoft/dataverse mcp https://spaarkedev1.crm.dynamics.com`
- Pre-registered app ID `0c412cc3-0dd6-449b-987f-05b053db9457` (Dataverse MCP CLI) enabled in Power Platform admin
- Auth profile created for `ralph.schroeder@spaarke.com` via `npx -y @microsoft/dataverse auth create --environment ...`
- **Required**: `az ad sp create --id 00000015-0000-0000-c000-000000000000` (Microsoft Dynamics ERP SP) — without this, auth fails with AADSTS650052
- Tenant ID: `a221a95e-6abc-4434-aecc-e48338a1b2f2`

### Phase 1 Validation Results (2026-04-06)
- `list_tables` — returned all tables including all `sprk_*` custom tables
- `describe_table sprk_matter` — returned full T-SQL schema with all columns, lookups, and option sets
- Both GA and Preview MCP endpoints validated successfully

### Key Finding: Direct HTTP Won't Work
Claude Code uses Dynamic Client Registration (DCR) for HTTP MCP servers, but Azure AD doesn't support DCR. Microsoft's recommended approach for non-Microsoft clients is the stdio local proxy (`@microsoft/dataverse` npm package).

---

## Massive Session Summary (2026-04-03 to 2026-04-06)

This session covered an enormous amount of work across multiple subsystems. Key highlights:

### Auth Architecture Overhaul
- **SessionStorageStrategy** — new #2 in 6-strategy cascade; shared across all same-origin iframes
- **tokenBridge full frame walk** — walks parent→grandparent→top (was only 1 level)
- **loginHint on ssoSilent** — all 8 auth files across shared lib, PCFs, Code Pages
- **clearCache fix** — stopped clearCache from wiping shared sessionStorage (was causing cascade login prompts)

### BFF URL `/api` Fix (Comprehensive)
- **buildBffApiUrl()** helper in `@spaarke/auth` + PCF shared utils — idempotent, prevents missing/duplicated `/api`
- **authenticatedFetch safety net** — resolveUrl routes through helper for relative URLs
- **4 legacy JS web resources** normalized: DocumentOperations, communication_send, emailactions, analysis_commands
- **2 production bugs fixed**: NextStepsStep.tsx (RAG indexing), matterService.ts (AI summary)
- **Documentation**: constraint, pattern, and architecture docs all updated

### Find Similar Wizard MVP
- BFF: `POST /api/ai/visualization/related-from-content` (text→embed→temp AI Search entry)
- Frontend: FindSimilarCodePage with document lookup + file upload + @spaarke/auth bootstrap
- Deployed BFF + code page

### PCF Updates
- **RelatedDocumentCount** v1.21.0: badge removal, /api fix, auth, viewer title, buildBffApiUrl migration
- **SemanticSearch** v1.1.31: doc counter, Associated Only toggle, toolbar layout, auth fix
- **VisualHost** v1.3.6: "No data available for this measure" for null source fields

### DocumentRelationshipViewer
- "Similar Documents" title in FindSimilarDialog
- Grid column overlap fix (Fluent DataGrid role-based selectors)
- Graph default positions (source upper-left, hubs upper-right with vertical spacing)

### Upload Pipeline Fix
- Missing `tenantId` in RAG indexing request → documents never indexed → fixed
- Removed dead `triggerDocumentProfile` endpoint (legacy, replaced by playbook system)

### Ribbon Fix
- "Send to Index" double `/api/api/` in sprk_DocumentOperations.js → normalized

### Document Form Fix
- Diagnosed RelatedDocumentCount PCF unbound field issue (form save error)

### MCP Dataverse Project (just completed)
- design.md → GO decision with official MCP server
- spec.md → expanded with 2 tracks
- 2 reference guides created

### All Commits (this session, master)
~20+ commits covering auth, /api, PCFs, code pages, viewer, upload, ribbon, MCP docs
