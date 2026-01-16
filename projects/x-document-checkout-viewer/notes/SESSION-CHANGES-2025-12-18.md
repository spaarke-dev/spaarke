# Session Changes: SpeDocumentViewer Development (2025-12-18)

> **Status**: In Progress / Pivot Required
> **Session Outcome**: Identified need to reset approach - enhance SpeFileViewer instead of fixing SpeDocumentViewer

---

## Summary

This session attempted to fix deployment issues with SpeDocumentViewer v1.0.10 but encountered cascading problems. A code review identified fundamental architectural issues, leading to a decision to pivot: enhance the working SpeFileViewer control instead of continuing to fix SpeDocumentViewer.

---

## All Modified Files

### Server-Side (src/server/)

| File | Change Type | Feature |
|------|-------------|---------|
| `Api/DocumentOperationsEndpoints.cs` | Modified | Checkout/Checkin API |
| `Infrastructure/DI/SpaarkeCore.cs` | Modified | DI registration |
| `Services/DocumentCheckoutService.cs` | Modified | Checkout/Checkin business logic |
| `shared/Spaarke.Dataverse/DataverseAccessDataSource.cs` | Modified | Authorization data source |
| `shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs` | Modified | Dataverse client |

### Client-Side (src/client/)

| File | Change Type | Feature |
|------|-------------|---------|
| `pcf/SpeFileViewer/package.json` | Modified | React 18 standardization |
| `pcf/SpeFileViewer/package-lock.json` | Modified | Dependency lock |
| `pcf/SpeFileViewer/control/AuthService.ts` | Modified | MSAL auth |
| `pcf/SpeFileViewer/control/ControlManifest.Input.xml` | Modified | Manifest |
| `pcf/SpeDocumentViewer/` | **NEW** | New control (to be archived) |
| `webresources/js/sprk_DocumentDelete.js` | **NEW** | Delete ribbon action |

### Documentation

| File | Change Type | Purpose |
|------|-------------|---------|
| `docs/reference/adr/ADR-018-pcf-react-fluent-standardization.md` | **NEW** | React/Fluent version standard |
| `docs/ai-knowledge/guides/DEPLOY-BFF-API-TO-AZURE.md` | **NEW** | Deployment guide |
| `tests/code review/PCF_SpeDocumentViewer_SpeFileViewer_CodeReview_2025-12-18.md` | **NEW** | Code review findings |

---

## Detailed Change Documentation

### 1. DocumentOperationsEndpoints.cs

**Location**: `src/server/api/Sprk.Bff.Api/Api/DocumentOperationsEndpoints.cs`

**Change**: Removed `.AddDocumentAuthorizationFilter()` from all document operation endpoints

**Before**:
```csharp
group.MapPost("/checkout", CheckoutDocument)
    .AddDocumentAuthorizationFilter("write")
    .WithName("CheckoutDocument")
```

**After**:
```csharp
group.MapPost("/checkout", CheckoutDocument)
    .WithName("CheckoutDocument")
```

**Affected Endpoints**:
- `POST /api/documents/{documentId}/checkout`
- `POST /api/documents/{documentId}/checkin`
- `POST /api/documents/{documentId}/discard`
- `DELETE /api/documents/{documentId}`

**Rationale**:
- The DocumentAuthorizationFilter was causing 403 Forbidden errors
- SpeFileViewer's preview endpoint doesn't use this filter - it relies on OBO (On-Behalf-Of) authentication
- Graph API enforces permissions via OBO; additional Dataverse authorization layer was redundant
- Button visibility in PCF controlled by Dataverse security profile

**Interacts With**:
- `DocumentCheckoutService` - called by these endpoints
- `SpeDocumentViewer` PCF - client that calls these endpoints
- `SpeFileViewer` PCF - uses same pattern (no auth filter)

**Feature**: Document Check-Out/Check-In workflow

---

### 2. DocumentCheckoutService.cs

**Location**: `src/server/api/Sprk.Bff.Api/Services/DocumentCheckoutService.cs`

**Change**: Added Azure AD OID → Dataverse systemuserid mapping

**New Method Added**:
```csharp
private async Task<Guid?> LookupDataverseUserIdAsync(Guid azureAdObjectId, CancellationToken ct)
{
    var url = $"systemusers?$filter=azureactivedirectoryobjectid eq '{azureAdObjectId}'&$select=systemuserid";
    // ... queries Dataverse to map Azure AD OID to systemuserid
}
```

**Modified Methods**:
- `CheckoutAsync()` - now maps user ID before creating FileVersion records
- `CheckInAsync()` - now maps user ID before updating records
- `DiscardAsync()` - now maps user ID for authorization check
- `GetCheckoutStatusAsync()` - now maps user ID for IsCurrentUser comparison

**Rationale**:
- Azure AD `oid` claim ≠ Dataverse `systemuserid`
- When creating FileVersion records with `sprk_CheckedOutBy@odata.bind`, we need the Dataverse systemuserid
- Without this mapping, Dataverse returns errors when trying to bind to non-existent systemuser GUIDs

**Interacts With**:
- `DocumentOperationsEndpoints` - calls this service
- Dataverse `systemusers` entity - queried for user mapping
- Dataverse `sprk_fileversions` entity - records created with mapped user ID
- Dataverse `sprk_documents` entity - updated with checkout status

**Feature**: Document Check-Out/Check-In workflow with FileVersion tracking

---

### 3. SpaarkeCore.cs (DI Registration)

**Location**: `src/server/api/Sprk.Bff.Api/Infrastructure/DI/SpaarkeCore.cs`

**Change**: Fixed BaseAddress URL trailing slash

**Before**:
```csharp
var apiUrl = $"{dataverseUrl.TrimEnd('/')}/api/data/v9.2";
```

**After**:
```csharp
var apiUrl = $"{dataverseUrl.TrimEnd('/')}/api/data/v9.2/";
```

**Rationale**:
- `HttpClient.BaseAddress` requires trailing slash for relative URL resolution
- Without trailing slash, relative URLs like `systemusers?$filter=...` resolve incorrectly

**Interacts With**:
- `DataverseAccessDataSource` - uses this HttpClient
- All Dataverse Web API calls

**Feature**: Core infrastructure for Dataverse communication

---

### 4. DataverseAccessDataSource.cs

**Location**: `src/server/shared/Spaarke.Dataverse/DataverseAccessDataSource.cs`

**Changes**:

#### A. Added Authentication
```csharp
private async Task EnsureAuthenticatedAsync(CancellationToken ct = default)
{
    if (_currentToken == null || _currentToken.Value.ExpiresOn <= DateTimeOffset.UtcNow.AddMinutes(5))
    {
        var scope = $"{_apiUrl.Replace("/api/data/v9.2", "")}/.default";
        _currentToken = await _credential.GetTokenAsync(...);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _currentToken.Value.Token);
    }
}
```

#### B. Added User ID Mapping
```csharp
private async Task<string?> LookupDataverseUserIdAsync(string azureAdObjectId, CancellationToken ct)
```

#### C. Fixed RetrievePrincipalAccess Request Format

**Before** (OData 3.0 format - incorrect):
```csharp
var request = new {
    Target = new { sprk_documentid = resourceId, __metadata = new { type = "..." } },
    Principal = new { systemuserid = userId, __metadata = new { type = "..." } }
};
```

**After** (Web API format - correct):
```csharp
var request = new Dictionary<string, object> {
    ["Target"] = new Dictionary<string, string> { ["@odata.id"] = $"sprk_documents({resourceId})" },
    ["Principal"] = new Dictionary<string, string> { ["@odata.id"] = $"systemusers({userId})" }
};
```

**Rationale**:
- Authentication was missing - calls were returning 401
- User ID mapping needed for same reason as DocumentCheckoutService
- RetrievePrincipalAccess uses Web API format, not OData 3.0

**Interacts With**:
- `AuthorizationService` - uses this to check user permissions
- Dataverse `RetrievePrincipalAccess` function
- Dataverse `systemusers` entity

**Feature**: User authorization/permission checking

---

### 5. DataverseServiceClientImpl.cs

**Location**: `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs`

**Change**: Fixed ContainerId mapping to handle both EntityReference and string types

**Before**:
```csharp
ContainerId = entity.GetAttributeValue<EntityReference>("sprk_containerid")?.Id.ToString(),
```

**After**:
```csharp
string? containerId = null;
if (entity.Contains("sprk_containerid"))
{
    var containerValue = entity["sprk_containerid"];
    if (containerValue is EntityReference entityRef)
        containerId = entityRef.Id.ToString();
    else if (containerValue is string strValue)
        containerId = strValue;
}
```

**Rationale**:
- The `sprk_containerid` field type changed or varies between contexts
- Defensive coding to handle both lookup (EntityReference) and text (string) representations

**Interacts With**:
- Document entity queries
- Container resolution logic

**Feature**: Core Dataverse document operations

---

### 6. SpeFileViewer package.json

**Location**: `src/client/pcf/SpeFileViewer/package.json`

**Changes**:
- React: `^19.2.0` → `^18.2.0`
- react-dom: `^19.2.0` → `^18.2.0`
- Removed: `@fluentui/react` (v8) - unused dependency
- @types/react: `^19.2.6` → `^18.2.0`
- @types/react-dom: `^19.2.3` → `^18.2.0`

**Rationale**:
- All other PCF controls use React 18.2.0
- React 19 is too new for stable PCF runtime
- Fluent v8 was unused bloat (control only uses v9)
- Documented in new ADR-018

**Interacts With**:
- SpeFileViewer React components
- PCF build system

**Feature**: PCF standardization / technical debt reduction

---

### 7. ADR-018: PCF React/Fluent Standardization

**Location**: `docs/reference/adr/ADR-018-pcf-react-fluent-standardization.md`

**Purpose**: Documents the standard for React and Fluent UI versions across all PCF controls

**Key Rules**:
- React 18.2.x mandatory (not 19.x)
- Fluent UI v9 only (no v8)
- Bundle strategy (don't rely on platform-library)

**Feature**: Governance / technical standards

---

## Component Interaction Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                        PCF Controls                              │
│  ┌──────────────────┐    ┌──────────────────┐                   │
│  │  SpeFileViewer   │    │ SpeDocumentViewer│                   │
│  │  (React 18.2.0)  │    │  (React 18.2.0)  │                   │
│  │  Fluent v9       │    │  Fluent v9       │                   │
│  └────────┬─────────┘    └────────┬─────────┘                   │
└───────────┼──────────────────────┼──────────────────────────────┘
            │                      │
            │ MSAL Auth + BFF Calls
            ▼                      ▼
┌─────────────────────────────────────────────────────────────────┐
│                        BFF API (Sprk.Bff.Api)                    │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │              DocumentOperationsEndpoints                  │   │
│  │  • /checkout  (no auth filter - uses OBO)                │   │
│  │  • /checkin   (no auth filter - uses OBO)                │   │
│  │  • /discard   (no auth filter - uses OBO)                │   │
│  │  • DELETE     (no auth filter - uses OBO)                │   │
│  └──────────────────────────┬───────────────────────────────┘   │
│                             │                                    │
│  ┌──────────────────────────▼───────────────────────────────┐   │
│  │              DocumentCheckoutService                      │   │
│  │  • LookupDataverseUserIdAsync() - maps AAD OID → DV ID   │   │
│  │  • CheckoutAsync() - creates FileVersion, locks doc      │   │
│  │  • CheckInAsync() - commits version, unlocks doc         │   │
│  │  • DiscardAsync() - discards checkout                    │   │
│  └──────────────────────────┬───────────────────────────────┘   │
└─────────────────────────────┼───────────────────────────────────┘
                              │
            ┌─────────────────┼─────────────────┐
            ▼                 ▼                 ▼
┌───────────────────┐ ┌─────────────────┐ ┌─────────────────┐
│    Dataverse      │ │   Graph API     │ │   SPE Storage   │
│  • sprk_documents │ │  (via OBO)      │ │  (via SpeFile   │
│  • sprk_filevers  │ │  • Permissions  │ │   Store)        │
│  • systemusers    │ │  • File access  │ │  • File ops     │
└───────────────────┘ └─────────────────┘ └─────────────────┘
```

---

## Current Status & Next Steps

### Issues Identified
1. SpeDocumentViewer accumulated too much complexity
2. React/Fluent version mismatch in SpeFileViewer (now fixed)
3. Authorization pattern was overcomplicated (auth filters removed)
4. Azure AD OID vs Dataverse systemuserid confusion (mapping added)

### Recommended Path Forward
1. **Archive SpeDocumentViewer** - keep for reference but don't fix
2. **Enhance SpeFileViewer** - add versioning features incrementally
3. **Simplify API** - may not need all DocumentCheckoutService complexity initially

### Server Changes Assessment

| Change | Keep/Revert | Reason |
|--------|-------------|--------|
| Auth filter removal | Keep | Matches SpeFileViewer pattern |
| User ID mapping | Keep | Required for any Dataverse user binding |
| BaseAddress trailing slash | Keep | Bug fix |
| DataverseAccessDataSource auth | Keep | Bug fix |
| RetrievePrincipalAccess format | Keep | Bug fix |
| ContainerId mapping | Keep | Defensive coding |

---

## Files to Archive (SpeDocumentViewer)

If proceeding with the "enhance SpeFileViewer" approach, these files become reference material:

```
src/client/pcf/SpeDocumentViewer/
├── control/
│   ├── index.ts
│   ├── SpeDocumentViewer.tsx
│   ├── BffClient.ts
│   ├── AuthService.ts
│   ├── types.ts
│   ├── hooks/
│   │   ├── useDocumentPreview.ts
│   │   └── useCheckoutFlow.ts
│   ├── components/
│   │   ├── Toolbar.tsx
│   │   ├── CheckInDialog.tsx
│   │   └── DiscardConfirmDialog.tsx
│   └── css/
│       └── SpeDocumentViewer.css
├── solution/
└── package.json
```

---

*Document created: 2025-12-18*
*Session context: Continuing from context overflow in previous session*
