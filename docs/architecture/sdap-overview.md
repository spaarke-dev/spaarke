# SDAP System Overview

> **Source**: SDAP-ARCHITECTURE-GUIDE.md (3000 lines) → Condensed overview
> **Last Updated**: December 3, 2025
> **Applies To**: Any work involving the SDAP document management system

---

## TL;DR

SDAP (SharePoint Document Access Platform) is an enterprise document management solution integrating Dataverse with SharePoint Embedded (SPE). Files stored in SPE containers (up to 250GB), metadata in Dataverse. Uses BFF pattern with Redis caching, PCF controls for UI.

---

## Applies When

- Building features that upload/download documents
- Adding document support to a new Dataverse entity
- Troubleshooting document-related issues
- Understanding the authentication flow for document operations
- Working with any `sprk_document` related code

---

## System Components

### Architecture Layers

```
┌─────────────────────────────────────────────────────────────────┐
│  Dataverse Model-Driven App                                     │
│  └─ PCF Controls (UniversalQuickCreate, SpeFileViewer)         │
└─────────────────────────────────────────────────────────────────┘
                         │
                         ↓ HTTPS + Bearer Token
┌─────────────────────────────────────────────────────────────────┐
│  BFF API (ASP.NET Core 8.0)                                    │
│  └─ spe-api-dev-67e2xz.azurewebsites.net                       │
└─────────────────────────────────────────────────────────────────┘
           │                              │
           ↓ OBO Token                    ↓ ClientSecret
┌──────────────────────┐    ┌──────────────────────────────┐
│  Microsoft Graph API │    │  Dataverse Web API           │
│  (SharePoint Embedded)    │  (Metadata queries)          │
└──────────────────────┘    └──────────────────────────────┘
           │                              │
           ↓                              ↓
┌──────────────────────┐    ┌──────────────────────────────┐
│  SPE Container       │    │  Dataverse Tables            │
│  (File Storage)      │    │  (sprk_document, sprk_matter)│
└──────────────────────┘    └──────────────────────────────┘
```

### Key Components

| Component | Technology | Purpose |
|-----------|------------|---------|
| **UniversalQuickCreate PCF** | TypeScript, React, Fluent UI | Upload UI in Dataverse forms |
| **SpeFileViewer PCF** | TypeScript, React | File preview/edit in forms |
| **BFF API** | ASP.NET Core 8.0 | Backend proxy, token exchange |
| **Redis Cache** | Azure Redis | Metadata caching (15-min TTL) |

### Important Endpoints

```
BFF API (https://spe-api-dev-67e2xz.azurewebsites.net):
├─ POST /upload/file              → Single file upload (<4MB)
├─ POST /upload/session           → Large file upload (>4MB)
├─ GET  /api/navmap/{entity}/{relationship}/lookup  → Metadata discovery
├─ GET  /api/documents/{id}/preview-url  → File preview URL
├─ GET  /api/documents/{id}/office       → Office Online editor URL
└─ GET  /healthz                  → Health check
```

---

## Data Model

### Core Tables

**sprk_document** (child - holds file references)
```
sprk_documentid     GUID        Primary key
sprk_documentname   Text        Display name
sprk_filename       Text        Original file name
sprk_filesize       Int         File size in bytes
sprk_graphitemid    Text        SPE DriveItem ID ← Links to SharePoint
sprk_graphdriveid   Text        SPE Container Drive ID
sprk_matter         Lookup      → sprk_matter (1:N relationship)
sprk_project        Lookup      → sprk_project (1:N relationship)
```

**sprk_matter / sprk_project** (parents - own the container)
```
sprk_containerid    Text        SPE Container Drive ID
```

### Relationship Pattern

```
sprk_matter (1) ───── sprk_matter_document_1n ────→ (N) sprk_document
                      ↑
                      Navigation Property: "sprk_Matter" (capital M!)
```

---

## Key Patterns

### 1. Phase 7 Dynamic Metadata Discovery

Instead of hardcoding navigation property names, query at runtime:

```typescript
// PCF calls BFF API
GET /api/navmap/sprk_document/sprk_matter_document_1n/lookup

// Response (from cache or Dataverse)
{
  "navigationPropertyName": "sprk_Matter",  // Correct casing!
  "source": "cache"  // or "dataverse" on first call
}
```

### 2. Document Record Creation with @odata.bind

```typescript
// Use discovered navigation property name
const payload = {
  "sprk_documentname": fileName,
  "sprk_graphitemid": driveItemId,
  "sprk_graphdriveid": containerId,
  "sprk_Matter@odata.bind": `/sprk_matters(${matterId})`  // Correct case from metadata
};
```

### 3. Container ID Inheritance

Parent entities own containers, documents inherit:
- Matter has `sprk_containerid` field
- When uploading, read parent's container ID
- File stored in that container
- Document record links to same container

---

## Configuration Lookup

**App Registrations**:
- PCF Control: `5175798e-f23e-41c3-b09b-7a90b9218189`
- BFF API: `1e40baad-e065-4aea-a8d4-4b7ab273458c`

**Key Files**:
```
src/api/Spe.Bff.Api/                    → BFF API
src/controls/UniversalQuickCreate/      → Upload PCF
src/controls/SpeFileViewer/             → Preview PCF
src/shared/Spaarke.Dataverse/           → Dataverse client
```

**Entity Config Location**:
```
src/controls/UniversalQuickCreate/config/EntityDocumentConfig.ts
```

---

## Common Mistakes

| Mistake | Why It Fails | Correct Approach |
|---------|--------------|------------------|
| Hardcoding `sprk_matter@odata.bind` | Case-sensitive, varies by relationship | Use Phase 7 metadata discovery |
| Using friendly scope `api://spe-bff-api/...` | Azure AD requires full URI | Use `api://1e40baad-.../user_impersonation` |
| Assuming relationship name | Dataverse uses exact schema name | Look up in Dataverse or use NavMap API |
| Storing file in Dataverse | File size limits, cost | Store in SPE, reference in Dataverse |

---

## Related Articles

- [sdap-auth-patterns.md](sdap-auth-patterns.md) - Authentication flows and token exchange
- [sdap-pcf-patterns.md](sdap-pcf-patterns.md) - PCF control implementation details
- [sdap-bff-api-patterns.md](sdap-bff-api-patterns.md) - BFF API endpoints and services
- [sdap-troubleshooting.md](sdap-troubleshooting.md) - Common issues and resolutions

---

*Condensed from ~3000 line architecture guide for AI coding context efficiency*
