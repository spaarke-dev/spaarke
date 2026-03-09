# Semantic Search Control - Final Deployment Plan

> **Created**: 2026-01-21
> **Purpose**: Track all items required for user-testing enablement
> **Status**: In Progress

---

## Executive Summary

| Category | Status | Items |
|----------|--------|-------|
| PCF Deployment | ✅ Complete | Solution imported to Dataverse |
| BFF API | ✅ Complete | Endpoint exists at `/api/ai/search` |
| Form Configuration | ⬜ Not Started | PCF needs to be added to Matter form |
| Auth Verification | ⬜ Not Started | MSAL config needs verification |
| Testing Tasks | ⬜ Pending | Tasks 041-045 (5 remaining) |

---

## Phase A: Enable Basic Testing (HIGH PRIORITY)

### A.1 Add PCF Control to Matter Form

- [ ] **A.1.1** Open Matter entity main form in Form Designer
  - Navigate to: `https://spaarkedev1.crm.dynamics.com` → Settings → Customizations → Entities → Matter → Forms
  - Or use make.powerapps.com → Solutions → Default Solution → Tables → Matter → Forms

- [ ] **A.1.2** Add new section for Semantic Search (or use existing section)
  - Recommended: Create "Document Search" section in main form

- [ ] **A.1.3** Insert Custom Component
  - Click "Components" → "Get more components" → Find "SemanticSearchControl"
  - Or: Insert → Custom control → SemanticSearchControl

- [ ] **A.1.4** Configure control properties (see Section A.2)

- [ ] **A.1.5** Save and publish form

**Resource References:**
- [DEPLOYMENT.md - Adding Control to Forms](DEPLOYMENT.md#adding-control-to-formspages)
- [ControlManifest.Input.xml](../../src/client/pcf/SemanticSearchControl/SemanticSearchControl/ControlManifest.Input.xml) - Property definitions

---

### A.2 Configure Control Properties

When adding the control to the form, configure these properties:

| Property | Value | Binding |
|----------|-------|---------|
| `apiBaseUrl` | `https://spe-api-dev-67e2xz.azurewebsites.net` | Static |
| `tenantId` | `a221a95e-6abc-4434-aecc-e48338a1b2f2` | Static |
| `searchScope` | `entity` | Static |
| `scopeId` | Bind to `spe_matterid` | **Field Binding** |
| `showFilters` | `true` | Static |
| `resultsLimit` | `25` | Static |
| `placeholder` | `Search documents...` | Static |
| `compactMode` | `true` | Static |

- [ ] **A.2.1** Set `apiBaseUrl` to BFF API endpoint
- [ ] **A.2.2** Set `tenantId` to Azure AD tenant
- [ ] **A.2.3** Set `searchScope` to `entity` (NOT `all` - not supported in R1)
- [ ] **A.2.4** Bind `scopeId` to Matter ID field (`spe_matterid`)
- [ ] **A.2.5** Configure display options (showFilters, compactMode)

**Important Notes:**
- `searchScope: "all"` is NOT supported in R1 API
- Supported scopes: `entity`, `documentIds`
- `scopeId` must be dynamically bound to the Matter record's ID field

**Resource References:**
- [DEPLOYMENT.md - Control Configuration](DEPLOYMENT.md#control-configuration)
- [spec.md - Control Properties](spec.md#pcf-control-properties)

---

### A.3 Verify Authentication Configuration

The control has hardcoded MSAL configuration that needs verification:

| Setting | Current Value | Status |
|---------|---------------|--------|
| Client ID | `170c98e1-d486-4355-bcbe-170454e0207c` | ⬜ Verify |
| Tenant ID | `a221a95e-6abc-4434-aecc-e48338a1b2f2` | ⬜ Verify |
| API Scope | `api://170c98e1.../access_as_user` | ⬜ Verify |

- [ ] **A.3.1** Verify Client ID matches Azure AD app registration for dev
- [ ] **A.3.2** Verify Tenant ID matches Spaarke Azure AD tenant
- [ ] **A.3.3** Verify API scope exists and is configured correctly
- [ ] **A.3.4** Test user has access to the app registration

**Resource References:**
- [msalConfig.ts](../../src/client/pcf/SemanticSearchControl/SemanticSearchControl/services/msalConfig.ts) - MSAL configuration
- [authService.ts](../../src/client/pcf/SemanticSearchControl/SemanticSearchControl/services/authService.ts) - Auth implementation

---

### A.4 Verify Test Prerequisites

- [ ] **A.4.1** Test Matter exists with indexed documents
  - Matter must have documents that are indexed in AI Search

- [ ] **A.4.2** Test user has required access
  - Dataverse access to Matter entity
  - BFF API access (Azure AD)

- [ ] **A.4.3** Documents are in AI Search index
  - Verify via Azure Portal or API query

- [ ] **A.4.4** Network allows access to BFF API
  - No firewall/VPN blocking `spe-api-dev-67e2xz.azurewebsites.net`

---

### A.5 Perform Initial Testing

- [ ] **A.5.1** Navigate to test Matter record
- [ ] **A.5.2** Verify control renders in form section
- [ ] **A.5.3** Verify search input accepts text
- [ ] **A.5.4** Perform test search query
- [ ] **A.5.5** Verify results display with similarity scores
- [ ] **A.5.6** Verify infinite scroll loads more results
- [ ] **A.5.7** Test filter panel (if showFilters=true)
- [ ] **A.5.8** Verify document click opens document

---

## Phase B: Complete Testing Tasks (MEDIUM PRIORITY)

These are the remaining project tasks for unit/integration testing:

| Task | Title | Status | File |
|------|-------|--------|------|
| 041 | Unit tests for hooks | ⬜ | [041-unit-tests-hooks.poml](tasks/041-unit-tests-hooks.poml) |
| 042 | Unit tests for services | ⬜ | [042-unit-tests-services.poml](tasks/042-unit-tests-services.poml) |
| 043 | Unit tests for components | ⬜ | [043-unit-tests-components.poml](tasks/043-unit-tests-components.poml) |
| 044 | Integration test setup | ⬜ | [044-integration-test-setup.poml](tasks/044-integration-test-setup.poml) |
| 045 | E2E test scenarios | ⬜ | [045-e2e-test-scenarios.poml](tasks/045-e2e-test-scenarios.poml) |

- [ ] **B.1** Execute Task 041 - Unit tests for hooks
- [ ] **B.2** Execute Task 042 - Unit tests for services
- [ ] **B.3** Execute Task 043 - Unit tests for components
- [ ] **B.4** Execute Task 044 - Integration test setup
- [ ] **B.5** Execute Task 045 - E2E test scenarios

**Note**: User testing can proceed in parallel with these tasks.

**Resource References:**
- [TASK-INDEX.md](tasks/TASK-INDEX.md) - Full task status
- [Test files location](../../src/client/pcf/SemanticSearchControl/SemanticSearchControl/__tests__/)

---

## Phase C: Production Readiness (FUTURE)

Items for production deployment (not required for user testing):

- [ ] **C.1** Create Custom Page for standalone search experience
- [ ] **C.2** Add command bar button for dialog launch (use ribbon-edit skill)
- [ ] **C.3** Update MSAL config to use environment variables instead of hardcoded values
- [ ] **C.4** Add production domain to external allowlist in ControlManifest
- [ ] **C.5** Update solution version for production release
- [ ] **C.6** Create managed solution for production

---

## Completed Items

### Infrastructure & API ✅

| Item | Status | Details |
|------|--------|---------|
| BFF API Deployed | ✅ | `https://spe-api-dev-67e2xz.azurewebsites.net` |
| Search Endpoint | ✅ | `POST /api/ai/search` |
| Azure OpenAI | ✅ | Embeddings service configured |
| AI Search Index | ✅ | Document index available |

### PCF Solution Deployment ✅

| Item | Status | Details |
|------|--------|---------|
| Solution Created | ✅ | `SpaarkeSemanticSearch_v1.0.0.zip` |
| Publisher | ✅ | Spaarke (prefix: `sprk`) |
| Solution Type | ✅ | Unmanaged (`Managed=0`) |
| Import to Dev | ✅ | `spaarkedev1.crm.dynamics.com` |
| Customizations Published | ✅ | Completed 2026-01-21 |

### External Domain Allowlist ✅

Already configured in ControlManifest.Input.xml:
```xml
<external-service-usage enabled="true">
  <domain>spe-api-dev-67e2xz.azurewebsites.net</domain>
  <domain>login.microsoftonline.com</domain>
</external-service-usage>
```

---

## Quick Reference

### Key Endpoints

| Service | URL |
|---------|-----|
| BFF API | `https://spe-api-dev-67e2xz.azurewebsites.net` |
| Search Endpoint | `POST /api/ai/search` |
| Dataverse Dev | `https://spaarkedev1.crm.dynamics.com` |

### Key Commands

```powershell
# Verify solution is deployed
pac solution list | Select-String "SpaarkeSemanticSearch"

# Re-publish customizations
pac solution publish

# Check PAC CLI auth
pac auth list

# Rebuild and repackage (if needed)
cd src/client/pcf/SemanticSearchControl
npm run build:prod
cd Solution && powershell -File pack.ps1

# Re-import solution
pac solution import --path "bin/SpaarkeSemanticSearch_v1.0.0.zip" --publish-changes
```

### Key Files

| Purpose | Path |
|---------|------|
| Control Source | `src/client/pcf/SemanticSearchControl/SemanticSearchControl/` |
| MSAL Config | `src/client/pcf/SemanticSearchControl/SemanticSearchControl/services/msalConfig.ts` |
| Search Service | `src/client/pcf/SemanticSearchControl/SemanticSearchControl/services/searchService.ts` |
| Solution Files | `src/client/pcf/SemanticSearchControl/Solution/` |
| Deployment Guide | `projects/ai-semantic-search-ui-r2/DEPLOYMENT.md` |
| Project Spec | `projects/ai-semantic-search-ui-r2/spec.md` |

---

## API Correction Note

**Important**: The original spec referenced `/api/ai/search/semantic` but the actual BFF API endpoint is:

```
POST /api/ai/search
```

The PCF control's searchService.ts uses the correct endpoint. Supported request body:

```json
{
  "query": "search terms",
  "scope": "entity",
  "scopeId": "matter-guid",
  "filters": { ... },
  "options": {
    "limit": 25,
    "offset": 0,
    "includeHighlights": true
  }
}
```

**Scope Limitations (R1)**:
- ✅ `entity` - Search within entity context (e.g., Matter)
- ✅ `documentIds` - Search specific documents
- ❌ `all` - NOT supported in R1

---

*Document created: 2026-01-21*
*Last updated: 2026-01-21*
