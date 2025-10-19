# SharePoint Embedded Guide - Graph API Scopes Summary

**Date**: 2025-10-16
**Source**: `Set up the SharePoint Embedded App.md`
**Purpose**: Document ONLY the Microsoft Graph scopes used in the guide

---

## Microsoft Graph Scopes Defined in Guide

**Location**: Lines 330-335

```typescript
// Microsoft Graph scopes
export const GRAPH_USER_READ = 'User.Read';
export const GRAPH_USER_READ_ALL = 'User.Read.All';
export const GRAPH_FILES_READ_WRITE_ALL = 'Files.ReadWrite.All';
export const GRAPH_SITES_READ_ALL = 'Sites.Read.All';
export const GRAPH_OPENID_CONNECT_BASIC = ["openid", "profile", "offline_access"];
```

**SharePoint Embedded Scope** (Line 339):
```typescript
export const SPEMBEDDED_FILESTORAGECONTAINER_SELECTED = 'FileStorageContainer.Selected';
```

**Note**: This is a **Microsoft Graph** scope, NOT a SharePoint Online REST API scope.

---

## Where Scopes Are Used in the Guide

### 1. Client-Side Initial Authentication (Lines 391-397)

**Purpose**: Configure MSAL provider for React SPA

```typescript
Providers.globalProvider = new Msal2Provider({
  clientId: Constants.CLIENT_ENTRA_APP_CLIENT_ID,
  authority: Constants.CLIENT_ENTRA_APP_AUTHORITY,
  scopes: [
    ...Scopes.GRAPH_OPENID_CONNECT_BASIC,           // openid, profile, offline_access
    Scopes.GRAPH_USER_READ_ALL,                     // User.Read.All
    Scopes.GRAPH_FILES_READ_WRITE_ALL,              // Files.ReadWrite.All
    Scopes.GRAPH_SITES_READ_ALL,                    // Sites.Read.All
    Scopes.SPEMBEDDED_FILESTORAGECONTAINER_SELECTED // FileStorageContainer.Selected
  ]
});
```

**Context**: This is for the React SPA making **DIRECT** calls to Graph API (not via web API).

---

### 2. Server-Side OBO Token Exchange (Lines 519-522)

**Purpose**: Web API requests OBO token to call Microsoft Graph

```typescript
const graphTokenRequest = {
  oboAssertion: token,
  scopes: [
    Scopes.GRAPH_SITES_READ_ALL,                    // Sites.Read.All
    Scopes.SPEMBEDDED_FILESTORAGECONTAINER_SELECTED // FileStorageContainer.Selected
  ]
};
```

**Context**: This is the Node.js web API performing OBO exchange.

**Key Finding**: The guide uses **ONLY 2 SCOPES** for OBO:
1. `Sites.Read.All` (NOT `Sites.FullControl.All`)
2. `FileStorageContainer.Selected`

---

### 3. Server-Side Helper Function (Lines 567-570)

**Purpose**: Reusable OBO token acquisition function

```typescript
const graphTokenRequest = {
  oboAssertion: token,
  scopes: [
    Scopes.GRAPH_SITES_READ_ALL,                    // Sites.Read.All
    Scopes.SPEMBEDDED_FILESTORAGECONTAINER_SELECTED // FileStorageContainer.Selected
  ]
};
```

**Context**: Helper function `getGraphToken()` in `./server/auth.ts`

---

## Comparison: Guide vs Our Current Code

| Scope | Guide Uses (OBO) | Our Code Uses (OBO) | Match? |
|-------|------------------|---------------------|--------|
| `Sites.Read.All` | ✅ YES | ❌ NO | ❌ |
| `Sites.FullControl.All` | ❌ NO | ✅ YES | ❌ |
| `Files.ReadWrite.All` | ❌ NO (in OBO) | ✅ YES | ❌ |
| `FileStorageContainer.Selected` | ✅ YES | ❌ NO | ❌ |

---

## Critical Findings

### Finding 1: Guide Uses LESS Permissive Scope

**Guide**:
- `Sites.Read.All` - Read access to SharePoint sites

**Our Code**:
- `Sites.FullControl.All` - Full control of SharePoint sites

**Implication**: The guide demonstrates you DON'T need `FullControl` for SharePoint Embedded operations.

---

### Finding 2: Guide Does NOT Request Files.ReadWrite.All in OBO

**Guide OBO Scopes** (lines 519-522):
```typescript
scopes: [
  Scopes.GRAPH_SITES_READ_ALL,                    // Only this
  Scopes.SPEMBEDDED_FILESTORAGECONTAINER_SELECTED // And this
]
```

**Our Code OBO Scopes** (lines 150-154 in GraphClientFactory.cs):
```csharp
new[] {
  "https://graph.microsoft.com/Sites.FullControl.All",
  "https://graph.microsoft.com/Files.ReadWrite.All",    // Guide doesn't use this in OBO
  // FileStorageContainer.Selected is MISSING
}
```

**Implication**: `Files.ReadWrite.All` might not be necessary for SharePoint Embedded file operations when you have `FileStorageContainer.Selected`.

---

### Finding 3: FileStorageContainer.Selected is REQUIRED

**Guide consistently shows** (3 different code locations):
- Line 396: Client-side MSAL provider requests it
- Line 521: Server-side OBO exchange requests it
- Line 569: Helper function OBO exchange requests it

**Our Code**: Does NOT request it in OBO exchange

**Implication**: This is the missing scope causing our HTTP 500 error.

---

## Recommendation for Our Code

### Minimal Fix (Add Only What's Missing)

```csharp
// GraphClientFactory.cs - Line 150-154
var result = await _cca.AcquireTokenOnBehalfOf(
    new[] {
        "https://graph.microsoft.com/Sites.FullControl.All",
        "https://graph.microsoft.com/Files.ReadWrite.All",
        "https://graph.microsoft.com/FileStorageContainer.Selected"  // ADD THIS
    },
    new UserAssertion(userAccessToken)
).ExecuteAsync();
```

**Risk**: LOW - Just adds missing scope
**Expected Result**: File upload succeeds

---

### Follow Guide Exactly (Match Guide Scopes)

```csharp
// GraphClientFactory.cs - Line 150-154
var result = await _cca.AcquireTokenOnBehalfOf(
    new[] {
        "https://graph.microsoft.com/Sites.Read.All",               // Change from FullControl to Read
        "https://graph.microsoft.com/FileStorageContainer.Selected"  // ADD THIS
        // REMOVE Files.ReadWrite.All
    },
    new UserAssertion(userAccessToken)
).ExecuteAsync();
```

**Risk**: MEDIUM - Changes existing scopes
**Benefit**: Follows least-privilege principle
**Testing Required**: Verify all operations still work with reduced permissions

---

## Architecture Difference: Guide vs SDAP

### Guide Architecture

```
React SPA ──Token for Web API──> Node.js API (list/create containers)
    │
    └──Token for Graph API──> Microsoft Graph (upload files DIRECTLY)
```

**Client makes direct Graph calls for file uploads** using token with:
- `Files.ReadWrite.All`
- `FileStorageContainer.Selected`
- Other Graph scopes

### SDAP Architecture

```
PCF Control ──Token for BFF API──> BFF API ──OBO Token──> Microsoft Graph (ALL operations)
```

**BFF API makes ALL Graph calls** using OBO token with:
- Currently: `Sites.FullControl.All`, `Files.ReadWrite.All`
- Should have: `FileStorageContainer.Selected` (at minimum)

---

## Why Sites.Read.All vs Sites.FullControl.All?

### Guide's Approach (Least Privilege)

**Uses**: `Sites.Read.All` for OBO
**Rationale**: SharePoint Embedded operations don't require site modification
- `FileStorageContainer.Selected` handles container file operations
- `Sites.Read.All` provides metadata access if needed
- Principle of least privilege

### Our Approach (Broader Permission)

**Uses**: `Sites.FullControl.All` for OBO
**Rationale**: Intended to "bypass container restrictions" (per code comment)
**Reality**: Doesn't work - still need `FileStorageContainer.Selected`

---

## Answer to User's Question

> "in the 'guide' what Graph API scopes are specifically listed"

**For OBO Token Exchange (Server-Side)**:
1. ✅ `Sites.Read.All` (Microsoft Graph)
2. ✅ `FileStorageContainer.Selected` (Microsoft Graph)

**NOT Used in OBO**:
- ❌ `Files.ReadWrite.All` (only used client-side for direct Graph calls)
- ❌ `Sites.FullControl.All` (not used anywhere in guide)

**Key Takeaway**: The guide uses **MINIMAL scopes** - only what's necessary for SharePoint Embedded.

---

## Important Clarification

### SharePoint Online Scopes (NOT in this analysis)

**Lines 55-68 in guide** show SharePoint Online permissions:
- Resource ID: `00000003-0000-0ff1-ce00-000000000000`
- Scope: `FileStorageContainer.Selected` (SharePoint Online version)
- **Purpose**: Only for Container Type registration via REST API
- **NOT used in runtime Graph API calls**

**User's Note**:
> "SPO is only used in very limited context of creating and registering ContainerTypes which is NOT currently in scope for this SDAP"

This is correct - SPO scopes are only for administrative operations, not file operations.

---

**Document Created**: 2025-10-16 05:45 AM
**Conclusion**: Guide uses `Sites.Read.All` + `FileStorageContainer.Selected` for OBO. Our code is missing `FileStorageContainer.Selected` entirely.
