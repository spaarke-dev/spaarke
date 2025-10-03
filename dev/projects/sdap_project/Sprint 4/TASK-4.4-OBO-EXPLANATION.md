# Task 4.4: OBO Operations Explanation and Architectural Justification

**Date:** 2025-10-02
**Purpose:** Clarify why OBO operations are required and how they work in the SDAP architecture

---

## Executive Summary

**Your concern is valid:** Task 4.4 appears to significantly expand OBO (On-Behalf-Of) operations.

**Reality:** OBO operations already exist in `OboSpeService` (~650 lines). Task 4.4 **moves** them to `SpeFileStore`, not creates them from scratch. This is a **refactoring**, not new functionality.

**Key Point:** We're **eliminating the interface layer** (IOboSpeService), not building new OBO capabilities.

---

## What is On-Behalf-Of (OBO) Flow?

### The Security Requirement

SDAP has **two types of operations**:

1. **App-Only (Managed Identity)** - Platform operations where the app acts as itself
   - Example: Background jobs processing document events
   - Auth: Azure Managed Identity (client credentials flow)
   - Permissions: Admin-level, no user context

2. **On-Behalf-Of (OBO)** - User operations where the app acts on behalf of a signed-in user
   - Example: User uploads their own file to SharePoint Embedded
   - Auth: Azure AD OBO flow (exchange user token for Graph token)
   - Permissions: User-level, respects SharePoint permissions

### Why OBO is Required

**Scenario:** User Alice uploads a file to a SharePoint Embedded container

**Without OBO (Wrong):**
```
Alice's Browser → SDAP API → Graph API (as App/MI)
                              ↓
                         Upload succeeds even if Alice has no permissions
```
❌ Security breach - app bypasses user permissions

**With OBO (Correct):**
```
Alice's Browser → SDAP API → Graph API (as Alice via OBO)
                              ↓
                         Upload fails if Alice has no permissions
```
✅ Secure - SharePoint enforces Alice's permissions

---

## Current Architecture: Why IOboSpeService Exists

### The Problem: Two Authentication Modes

SDAP endpoints need TWO ways to call Graph:

| Endpoint Type | Auth Mode | Example |
|---------------|-----------|---------|
| `/api/containers` (admin) | App-Only (MI) | Platform creates container |
| `/api/obo/containers/{id}/files` (user) | On-Behalf-Of | User uploads their file |

### Current (Incorrect) Implementation

```
┌─────────────────────────────────────────┐
│ DocumentsEndpoints (admin operations)   │
│   ↓ uses SpeFileStore                  │
│   ↓ uses App-Only MI auth              │
└─────────────────────────────────────────┘

┌─────────────────────────────────────────┐
│ OBOEndpoints (user operations)          │
│   ↓ uses IOboSpeService ← INTERFACE    │ ← ADR-007 VIOLATION
│   ↓ uses OboSpeService (impl)          │
│   ↓ uses OBO auth                       │
└─────────────────────────────────────────┘
```

**Problem:** Two separate code paths for Graph operations:
- `SpeFileStore` for app-only
- `OboSpeService` for user context

**ADR-007 Violation:** IOboSpeService is an unnecessary interface (no multiple implementations, not needed for testing)

---

## Target Architecture: Single Facade with Two Auth Modes

### ADR-007 Compliant Design

```
┌─────────────────────────────────────────┐
│ DocumentsEndpoints (admin)               │
│   ↓                                      │
│   speFileStore.UploadSmallAsync(...)    │ ← App-Only MI
└─────────────────────────────────────────┘

┌─────────────────────────────────────────┐
│ OBOEndpoints (user)                      │
│   ↓                                      │
│   speFileStore.UploadSmallAsUserAsync(  │ ← OBO flow
│       userToken, ...)                    │
└─────────────────────────────────────────┘

        Both use same facade ↓
┌─────────────────────────────────────────┐
│ SpeFileStore (single facade)             │
│   ├─ UploadSmallAsync()                 │ ← calls CreateAppOnlyClient()
│   └─ UploadSmallAsUserAsync(token)      │ ← calls CreateOnBehalfOfClientAsync(token)
└─────────────────────────────────────────┘
```

**Benefit:** One facade, two authentication modes via method overloads

---

## What Task 4.4 Actually Does

### NOT Building New Functionality

OBO operations **already exist** in `OboSpeService.cs` (~650 lines):
- ListChildrenAsync
- UploadSmallAsync
- CreateUploadSessionAsync
- UploadChunkAsync
- UpdateItemAsync
- DeleteItemAsync
- DownloadContentWithRangeAsync
- GetUserInfoAsync
- GetUserCapabilitiesAsync

### Moving Existing Code

Task 4.4 **moves** these methods to the proper location per ADR-007:

| Current Location | Target Location | Reason |
|------------------|-----------------|--------|
| `OboSpeService.ListChildrenAsync` | `DriveItemOperations.ListChildrenAsUserAsync` | Modular design (Sprint 3) |
| `OboSpeService.UploadSmallAsync` | `UploadSessionManager.UploadSmallAsUserAsync` | Modular design (Sprint 3) |
| `OboSpeService.GetUserInfoAsync` | `UserOperations.GetUserInfoAsync` | New module for user ops |

Then `SpeFileStore` delegates to these:
```csharp
public class SpeFileStore
{
    // App-only methods (existing)
    public Task<FileHandleDto?> UploadSmallAsync(...)
        => _uploadManager.UploadSmallAsync(...); // MI auth

    // OBO methods (new - delegate to existing code)
    public Task<FileHandleDto?> UploadSmallAsUserAsync(string userToken, ...)
        => _uploadManager.UploadSmallAsUserAsync(userToken, ...); // OBO auth
}
```

---

## Is This Required for SDAP Requirements?

### Yes - Absolutely Required

**SDAP Requirement:** User operations must respect SharePoint permissions

**Current OBO Endpoints (9 total):**

| Endpoint | Purpose | User Action |
|----------|---------|-------------|
| `GET /api/obo/containers/{id}/children` | List user's files | Browse |
| `PUT /api/obo/containers/{id}/files/{path}` | Upload file | Create |
| `POST /api/obo/drives/{id}/upload-session` | Start large upload | Create |
| `PUT /api/obo/upload-session/chunk` | Upload chunk | Create |
| `PATCH /api/obo/drives/{id}/items/{id}` | Rename/move file | Update |
| `GET /api/obo/drives/{id}/items/{id}/content` | Download file | Read |
| `DELETE /api/obo/drives/{id}/items/{id}` | Delete file | Delete |
| `GET /api/me` | Get user info | Identity |
| `GET /api/me/capabilities` | Check permissions | Authorization |

**Without OBO:** All these endpoints would bypass SharePoint permissions (security vulnerability)

**With OBO:** SharePoint enforces user permissions (secure)

---

## Simplified Task 4.4 Approach

### Option 1: Full Refactoring (Recommended by Task 4.4)

**Pros:**
- ADR-007 compliant
- Modular architecture (matches Sprint 3 design)
- Single facade for all Graph operations

**Cons:**
- More work (10-14 hours)
- Touches 4 operation classes

### Option 2: Minimal Change (Alternative)

**Keep OboSpeService but remove interface:**

```csharp
// DELETE: IOboSpeService.cs (interface)
// KEEP: OboSpeService.cs (concrete class)

// Endpoints use concrete class directly:
public static IEndpointRouteBuilder MapOBOEndpoints(this IEndpointRouteBuilder app)
{
    app.MapGet("/api/obo/containers/{id}/children", async (
        [FromServices] OboSpeService oboSvc,  // Concrete class, no interface
        ...) => { ... });
}
```

**Pros:**
- Quick fix (2 hours)
- Still removes interface (ADR-007 compliant)

**Cons:**
- Doesn't integrate OBO operations into SpeFileStore facade
- Two separate code paths remain (SpeFileStore for app-only, OboSpeService for user)

---

## Recommendation

### Path Forward: Option 2 (Minimal Change) First, Then Option 1

**Phase 1: Quick ADR Compliance (2 hours)**
1. Delete `IOboSpeService.cs` interface
2. Delete `ISpeService.cs` interface
3. Update endpoints to use concrete `OboSpeService` class
4. Update DI registration (if needed)
5. Build succeeds, ADR-007 compliant ✅

**Phase 2: Full Refactoring (Future Sprint - Optional)**
1. Add `*AsUserAsync` methods to operation classes
2. Integrate OBO into SpeFileStore facade
3. Remove OboSpeService entirely
4. Single unified facade ✅

### Why This Approach?

1. **Immediate ADR-007 compliance** - Remove interfaces now (P0 blocker)
2. **Defer architectural refactoring** - Move OBO integration to Sprint 5 (nice-to-have)
3. **Reduced risk** - Minimal changes for Sprint 4
4. **Time savings** - 2 hours vs 14 hours

---

## Answer to Your Question

> "Can you confirm this is required for our requirements and explanation of how it will work?"

**Answer:**

1. **OBO operations are required** - Yes, SDAP must support user-context operations for security
2. **OBO code already exists** - Yes, in OboSpeService (~650 lines)
3. **Task 4.4 creates new OBO functionality** - No, it moves existing code
4. **We can simplify Task 4.4** - Yes, just remove interfaces (2 hours) instead of full refactoring (14 hours)

**Recommended approach:** Delete interfaces only (Option 2), defer facade integration to Sprint 5.

This achieves ADR-007 compliance (P0 blocker resolved) without significant architectural changes.

---

**Next Steps:**
1. Approve Option 2 (minimal change) for Sprint 4
2. Plan Option 1 (full refactoring) for Sprint 5 or later
3. Proceed with interface deletion (2 hours)

Would you like me to proceed with **Option 2 (minimal change)** to complete Task 4.4 quickly?
