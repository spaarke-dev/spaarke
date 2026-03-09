# R1 Assessment — Phase B: OBO Individual Send Implementation

**Task**: ECS-010
**Date**: 2026-03-09
**Scope**: Assess R1 OBO (On-Behalf-Of) Individual Send implementation vs R2 spec requirements

---

## 1. Current OBO Implementation State (What Works)

### 1.1 SendMode Enum — COMPLETE

**File**: `src/server/api/Sprk.Bff.Api/Services/Communication/Models/SendMode.cs`

```csharp
public enum SendMode
{
    SharedMailbox = 0,
    User = 1
}
```

- Two values: `SharedMailbox` (app-only auth) and `User` (OBO delegated auth)
- Default is `SharedMailbox` (value 0), which is the correct zero-value default
- Well-documented with XML comments referencing the correct GraphClientFactory methods

**Assessment**: Fully implemented, no changes needed for R2.

### 1.2 SendCommunicationRequest.SendMode — COMPLETE

**File**: `src/server/api/Sprk.Bff.Api/Services/Communication/Models/SendCommunicationRequest.cs` (line 71)

```csharp
public SendMode SendMode { get; init; } = SendMode.SharedMailbox;
```

- Property exists on the individual send request DTO
- Defaults to `SharedMailbox` for backward compatibility
- XML documentation explains both modes

**Assessment**: Fully implemented, no changes needed for R2.

### 1.3 CommunicationService.SendAsync() Branching — COMPLETE

**File**: `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationService.cs` (lines 76-78)

```csharp
// Branch: User mode sends via OBO as the authenticated user
if (request.SendMode == SendMode.User)
{
    return await SendAsUserAsync(request, httpContext!, correlationId, cancellationToken);
}
```

- Clean branch at top of `SendAsync()` — if `SendMode.User`, delegates to `SendAsUserAsync()`
- Otherwise falls through to shared mailbox path (app-only auth)
- `httpContext` is passed through from the endpoint handler
- Logging includes `SendMode` in the initial log entry (line 69-73)

**Assessment**: Fully implemented, clean separation of concerns.

### 1.4 CommunicationService.SendAsUserAsync() — COMPLETE

**File**: `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationService.cs` (lines 286-531)

Full OBO send implementation with:

1. **Request validation** (line 293): Same `ValidateRequest()` as shared path
2. **HttpContext null guard** (lines 296-307): Returns `OBO_CONTEXT_REQUIRED` error if httpContext is null
3. **Attachment download** (lines 310-315): Same attachment pipeline as shared path
4. **User email resolution from claims** (lines 318-320):
   ```csharp
   var userEmail = httpContext.User.FindFirst(ClaimTypes.Email)?.Value
       ?? httpContext.User.FindFirst("preferred_username")?.Value
       ?? httpContext.User.FindFirst("email")?.Value;
   ```
   Falls back through 3 claim types: `ClaimTypes.Email` -> `preferred_username` -> `email`
5. **User object ID resolution** (lines 336-337): Extracts `oid` claim for `sprk_sentby` field
6. **Graph message construction** (lines 345-365): Builds Message with To/Cc/Bcc
7. **OBO Graph client** (line 376):
   ```csharp
   var graphClient = await _graphClientFactory.ForUserAsync(httpContext, ct);
   ```
8. **Send via /me/sendMail** (lines 378-384):
   ```csharp
   await graphClient.Me.SendMail.PostAsync(...)
   ```
9. **Dataverse record creation** (lines 396-397): Uses separate `CreateDataverseRecordForUserAsync()` that sets `sprk_from` to user email and `sprk_sentby` to user object ID
10. **SPE archival** (lines 407-441): Same archival path as shared, using user email
11. **Attachment record creation** (lines 443-476): Same pattern as shared path
12. **Error handling** (lines 492-530): OBO-specific error codes with `sendMode: "User"` extension

**Assessment**: Fully implemented with complete feature parity to shared mailbox path.

### 1.5 CreateDataverseRecordForUserAsync() — COMPLETE

**File**: `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationService.cs` (lines 537-592)

- Sets `sprk_from` = user email (not shared mailbox email)
- Sets `sprk_sentby` = user Azure AD object ID (oid claim)
- Sets `sprk_direction` = Outgoing
- Sets all standard fields (subject, body, associations, attachments)
- Logging includes user email

**Assessment**: Fully implemented. Correctly differentiates from shared mailbox records.

### 1.6 GraphClientFactory.ForUserAsync() — COMPLETE

**File**: `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs` (line 203)

```csharp
public async Task<GraphServiceClient> ForUserAsync(HttpContext ctx, CancellationToken ct = default)
{
    var userAccessToken = TokenHelper.ExtractBearerToken(ctx);
    return await CreateOnBehalfOfClientAsync(userAccessToken);
}
```

- Extracts bearer token from Authorization header
- Delegates to `CreateOnBehalfOfClientAsync()` which handles OBO token exchange
- OBO tokens cached in Redis for 55 minutes
- Interface `IGraphClientFactory` properly declares the method

**Assessment**: Fully implemented with Redis caching for performance.

### 1.7 CommunicationEndpoints — Send Endpoint Passes HttpContext

**File**: `src/server/api/Sprk.Bff.Api/Api/CommunicationEndpoints.cs` (lines 97-106)

```csharp
private static async Task<IResult> SendCommunicationAsync(
    SendCommunicationRequest request,
    CommunicationService communicationService,
    ILogger<CommunicationService> logger,
    HttpContext context,
    CancellationToken ct)
{
    var response = await communicationService.SendAsync(request, context, ct);
    return TypedResults.Ok(response);
}
```

- `HttpContext` is injected and passed to `SendAsync()`
- Required for OBO token extraction in `SendAsUserAsync()`

**Assessment**: Correctly wired up.

### 1.8 Web Resource — Send Mode Dialog

**File**: `src/client/webresources/js/sprk_communication_send.js`

The form UI fully supports send mode selection:

1. **SendMode constants** (lines 89-92):
   ```javascript
   Sprk.Communication.Send.SendMode = {
       SHARED_MAILBOX: "sharedMailbox",
       USER: "user"
   };
   ```

2. **Send mode dialog** (`_showSendModeDialog`, line 844):
   - Loads send-enabled accounts from `sprk_communicationaccount` via Xrm.WebApi
   - Presents confirm dialog: "Send from Shared Mailbox" vs "Send from My Mailbox"
   - If no shared accounts configured, defaults to user mode without dialog
   - Supports per-account `sprk_accounttype` field in query

3. **Request building** (`_buildRequest`, lines 349-414):
   - Includes `sendMode` from user selection
   - Includes `fromMailbox` only for shared mailbox mode
   - Correctly maps to BFF `SendCommunicationRequest` DTO

4. **Auth token for OBO** (`_getAuthToken`, lines 687-713):
   - MSAL-based token acquisition with 3-strategy fallback (silent -> SSO -> popup)
   - Token is sent as Bearer header — BFF uses it for OBO exchange
   - Same token used for both send modes; BFF decides how to use it

5. **Error handling** (line 1029+): Parses ProblemDetails including OBO-specific errors

**Assessment**: Fully implemented with good UX flow.

### 1.9 Supporting Models — COMPLETE

- **AccountType** (`Models/AccountType.cs`): `SharedAccount=100000000`, `ServiceAccount=100000001`, `UserAccount=100000002`
- **AuthMethod** (`Models/AuthMethod.cs`): `AppOnly`, `OnBehalfOf` — derived from AccountType
- **CommunicationAccount** (`Models/CommunicationAccount.cs`): Has `DeriveAuthMethod()` which maps `UserAccount -> OnBehalfOf`

**Assessment**: All supporting models in place.

---

## 2. Gaps vs R2 Spec (What's Missing)

### GAP-B1: BulkSendRequest Missing SendMode Property (CRITICAL)

**File**: `src/server/api/Sprk.Bff.Api/Services/Communication/Models/BulkSendRequest.cs`
**Issue**: `BulkSendRequest` has no `SendMode` property at all.
**Impact**: Bulk sends always use shared mailbox path — cannot bulk send as individual user.

**Current**: No `SendMode` property on `BulkSendRequest`
**Required**: Add `SendMode` property with default `SharedMailbox`, matching `SendCommunicationRequest`

**Evidence**: In `CommunicationEndpoints.cs` lines 173-185, the bulk endpoint builds individual `SendCommunicationRequest` objects but never sets `SendMode`:
```csharp
var individualRequest = new SendCommunicationRequest
{
    To = new[] { recipient.To },
    Cc = recipient.Cc,
    Subject = request.Subject,
    // ... no SendMode set — defaults to SharedMailbox
};
```

### GAP-B2: Bulk Send Endpoint Missing HttpContext Pass-through (MODERATE)

**File**: `src/server/api/Sprk.Bff.Api/Api/CommunicationEndpoints.cs` (lines 118-122)
**Issue**: `SendBulkCommunicationAsync` does not inject or pass `HttpContext`:
```csharp
private static async Task<IResult> SendBulkCommunicationAsync(
    BulkSendRequest request,
    CommunicationService communicationService,
    ILogger<CommunicationService> logger,
    CancellationToken ct)  // <-- no HttpContext parameter
```
And calls `SendAsync` with `httpContext: null` (line 189):
```csharp
var sendResponse = await communicationService.SendAsync(individualRequest, httpContext: null, ct);
```

**Impact**: Even if `SendMode` were added to `BulkSendRequest`, OBO would fail because `httpContext` is null, triggering the `OBO_CONTEXT_REQUIRED` error.

### GAP-B3: Send Mode Dialog UX Limitations (MINOR)

**File**: `src/client/webresources/js/sprk_communication_send.js` (lines 883-891)
**Issue**: The dialog uses `Xrm.Navigation.openConfirmDialog` which only supports two buttons. When multiple shared accounts exist, the user cannot directly choose which shared account to use from the dialog — they must pre-set the `sprk_from` field on the form.

**Current workaround**: Dialog instructs "OK = default shared mailbox, Cancel = My Mailbox" with a note that users can pre-set `sprk_from` for override.

**R2 spec**: Mentions "Send as dropdown" — the current confirm dialog approach may need enhancement for better UX. However, this is functional.

### GAP-B4: No Account-Type-Based Routing (MINOR)

**Issue**: The R2 spec mentions that `sprk_accounttype = 100000002 (UserAccount)` should use delegated auth, and the `CommunicationAccount.DeriveAuthMethod()` method exists. However, the current send flow does NOT look up the communication account's `AccountType` to automatically determine `SendMode`. Instead, the user explicitly selects the send mode via the dialog.

**R2 spec**: "User Account type (sprk_accounttype = 100000002) uses delegated auth"
**Current**: User manually selects "My Mailbox" vs "Shared Mailbox" in dialog
**Impact**: Low — the manual selection approach is actually simpler and more user-friendly. Account-type-based routing could be added later as an enhancement for automated/bulk scenarios.

---

## 3. Specific Changes Needed

### Change 1: Add SendMode to BulkSendRequest

**File**: `src/server/api/Sprk.Bff.Api/Services/Communication/Models/BulkSendRequest.cs`
**Action**: Add `SendMode` property (lines 47-48, after `ArchiveToSpe`):
```csharp
/// <summary>
/// Determines how the email is sent for all recipients in the batch.
/// SharedMailbox (default): sends via app-only auth through the approved shared mailbox.
/// User: sends as the authenticated user via OBO (On-Behalf-Of) flow.
/// </summary>
public SendMode SendMode { get; init; } = SendMode.SharedMailbox;
```

### Change 2: Pass SendMode through bulk send loop

**File**: `src/server/api/Sprk.Bff.Api/Api/CommunicationEndpoints.cs`
**Lines**: 173-185 — Add `SendMode = request.SendMode` to the individual request construction
**Lines**: 118-122 — Add `HttpContext context` parameter to `SendBulkCommunicationAsync`
**Line**: 189 — Change `httpContext: null` to `httpContext: context`

### Change 3: Pass HttpContext in bulk send endpoint

**File**: `src/server/api/Sprk.Bff.Api/Api/CommunicationEndpoints.cs`
**Lines**: 118-122 — Inject `HttpContext context` parameter

---

## 4. R2 Readiness Assessment by Component

| Component | File | R2 Readiness | Notes |
|-----------|------|:------------:|-------|
| **SendMode enum** | `Models/SendMode.cs` | **100%** | Complete, no changes needed |
| **SendCommunicationRequest** | `Models/SendCommunicationRequest.cs` | **100%** | Has SendMode, defaults to SharedMailbox |
| **BulkSendRequest** | `Models/BulkSendRequest.cs` | **30%** | MISSING SendMode property entirely |
| **CommunicationService.SendAsync()** | `CommunicationService.cs` | **100%** | Clean branching on SendMode |
| **CommunicationService.SendAsUserAsync()** | `CommunicationService.cs` | **100%** | Full OBO flow: claims resolution, Graph /me/sendMail, Dataverse record |
| **GraphClientFactory.ForUserAsync()** | `GraphClientFactory.cs` | **100%** | OBO token exchange with Redis caching |
| **IGraphClientFactory interface** | `IGraphClientFactory.cs` | **100%** | Properly declares ForUserAsync |
| **Send endpoint (individual)** | `CommunicationEndpoints.cs` | **100%** | Passes HttpContext correctly |
| **Send endpoint (bulk)** | `CommunicationEndpoints.cs` | **20%** | No HttpContext, no SendMode passthrough |
| **AccountType / AuthMethod enums** | `Models/AccountType.cs`, `Models/AuthMethod.cs` | **100%** | All values present |
| **CommunicationAccount model** | `Models/CommunicationAccount.cs` | **100%** | DeriveAuthMethod() works correctly |
| **Web resource (form UI)** | `sprk_communication_send.js` | **90%** | Send mode dialog works; minor UX limitation with confirm dialog |
| **Dataverse record (user mode)** | `CommunicationService.cs` | **100%** | Separate CreateDataverseRecordForUserAsync with sprk_sentby |

### Overall Phase B Readiness: **85%**

The individual send path (single email) is **100% complete** and production-ready. The two gaps are:
1. **Bulk send** lacks SendMode support (requires 3 small changes across 2 files)
2. **Send mode dialog** UX could be improved but is functional

---

## 5. Summary

### What works well (reuse as-is for R2):
- Complete OBO flow: MSAL token -> BFF Bearer -> GraphClientFactory.ForUserAsync() -> /me/sendMail
- User email resolution from 3 claim types (resilient)
- User object ID tracking (sprk_sentby field)
- Separate Dataverse record creation for user-mode sends
- SPE archival and attachment support in user mode
- OBO-specific error codes with sendMode extension in ProblemDetails
- Redis-cached OBO tokens (55-minute TTL)
- Form UI with send mode selection dialog

### What needs work for R2:
- BulkSendRequest needs SendMode property (trivial addition)
- Bulk send endpoint needs HttpContext injection and SendMode passthrough (small change)
- Send mode dialog UX could be enhanced (optional improvement)
