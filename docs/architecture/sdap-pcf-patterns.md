# SDAP PCF Control Patterns

> **Source**: SDAP-ARCHITECTURE-GUIDE.md (Component Architecture section)
> **Last Updated**: March 9, 2026
> **Applies To**: PCF control development, upload/preview UI changes

---

## TL;DR

Two PCF controls: **UniversalQuickCreate** (v2.3.0) for form-embedded file upload, **SpeFileViewer** (v1.0.6) for preview/edit. Both use MSAL.js auth, Fluent UI, React. Upload control handles multi-file with progress; viewer embeds Office Online.

> **Migration Note (March 2026)**: The primary document upload experience is migrating to the **DocumentUploadWizard Code Page** (`sprk_documentuploadwizard`) per ADR-006. The Custom Page + UniversalQuickCreate PCF wrapper pattern is **deprecated** — standalone dialogs must use Code Pages, not Custom Pages wrapping PCF controls. The UniversalQuickCreate PCF remains available for form-embedded upload only. Upload services (`MultiFileUploadService`, `DocumentRecordService`) have been extracted to `@spaarke/ui-components` for reuse.

---

## ADR-006: PCF vs Code Page Decision

**Field-bound controls on a form → PCF. Standalone dialogs/pages → Code Page (never Custom Page + PCF wrapper).**

The Custom Page + PCF wrapper pattern is explicitly deprecated. It required two build steps, custom page packaging, and unnecessary complexity. Code Pages (`src/solutions/DocumentUploadWizard/`) provide a direct path: build once, deploy as a single web resource, open via `Xrm.Navigation.navigateTo`.

---

## Control 1: UniversalQuickCreate (Upload)

**Purpose**: Form-embedded file upload for Matters, Projects, and other entities with SPE containers.

**Entity Configuration**: Each supported entity is configured via `EntityDocumentConfig.ts` with its entity name, lookup field name, relationship schema name (exact Dataverse name — case matters), container ID field, display name field, and entity set name. Adding a new entity requires adding a config entry, ensuring the entity has a `sprk_containerid` field and a 1:N relationship to `sprk_document`, then rebuilding and deploying.

**Upload Flow**:
1. Get entity config from registry
2. Acquire auth token via MSAL.js (`api://...user_impersonation` scope)
3. Get navigation property name via NavMapClient (Phase 7 — prevents case mismatches)
4. Upload file to SPE container via BFF API
5. Create `sprk_document` record in Dataverse with OData binding to parent entity

**Common Mistakes**:
- Wrong `relationshipSchemaName` (exact name with correct casing required) → NavMap API returns 404
- Missing `sprk_containerid` on parent entity → upload fails
- Hardcoded navigation property → wrong case causes 400; always use NavMapClient

---

## Control 2: SpeFileViewer (Preview/Edit)

**Purpose**: Preview and edit documents via Office Online embedded in a PCF iframe.

**Preview Flow**: Get auth token → get preview URL from BFF API (`/api/documents/{id}/preview`) → render iframe with `?action=embedview&nb=true` (hides SharePoint header).

**Editor Flow**: Get office URL from BFF API (`/api/documents/{id}/office-url`) → check permissions (`canEdit`) → if denied, show read-only dialog → switch iframe to editor URL.

**Supported file types**:
- Office files (Word, Excel, PowerPoint): preview + edit
- PDF, images, text files: preview only

---

## Deployment

```bash
# Build and deploy
cd src/controls/UniversalQuickCreate
npm run build
pac pcf push --publisher-prefix sprk
```

---

## Debugging

| Issue | Cause | Fix |
|-------|-------|-----|
| 401 on upload | Token expired | Refresh page |
| 404 on NavMap | Wrong relationship name in config | Check exact name in Dataverse relationships |
| 400 on record create | Navigation property case mismatch | Use Phase 7 NavMapClient |
| Double `/api` in URL | NavMapClient strips trailing `/api` | Don't add `/api` to base URL |

---

## Related Articles

- [sdap-overview.md](sdap-overview.md) - System architecture
- [sdap-auth-patterns.md](sdap-auth-patterns.md) - MSAL.js token handling
- [sdap-bff-api-patterns.md](sdap-bff-api-patterns.md) - Backend endpoints
