# Outlook Add-in Critical Fixes Assessment

> **Created**: 2026-01-23
> **Last Updated**: 2026-01-23
> **Status**: Implementation In Progress
> **Context**: User testing identified 6 critical issues with the Outlook Add-in V1

---

## Quick Reference

| # | Issue | Severity | Status | Fix Location |
|---|-------|----------|--------|--------------|
| 1 | CORS error on Save | **Critical** | **COMPLETED** | Azure App Service config |
| 2 | Login required every time | **High** | **COMPLETED** | AuthService.ts |
| 3 | Pin add-in pane | **Medium** | **COMPLETED** | manifest-working.xml v1.0.14.0 (nested V1.0/V1.1) |
| 4 | Drag resize pane | **Low** | Deferred | Office limitation |
| 5 | Associate with not working | **High** | **COMPLETED** | useEntitySearch.ts, SaveFlow.tsx |
| 6 | Create new record | **Medium** | Pending | New component + API |

---

## Issue 1: CORS Error on Save (COMPLETED)

### Symptom
```
Access to fetch at 'https://spe-api-dev-67e2xz.azurewebsites.net/office/save'
from origin 'https://agreeable-hill-00bcc911e-preview.westus2.1.azurestaticapps.net'
has been blocked by CORS policy
```

### Root Cause
The BFF API's CORS configuration only allowed Dataverse origins. The Static Web App origin was not included.

### Fix Applied (2026-01-23)
Added application setting in Azure Portal:

| Setting | Value |
|---------|-------|
| App Service | `spe-api-dev-67e2xz` |
| Setting Name | `Cors__AllowedOrigins__2` |
| Value | `https://agreeable-hill-00bcc911e-preview.westus2.1.azurestaticapps.net` |

**Note**: Restart the App Service after adding the setting for changes to take effect.

### Verification
Test the Save operation in the add-in. The CORS error should no longer appear.

---

## Issue 2: Login Required Every Time (COMPLETED)

### Symptom
User must sign in each time they select a new email or reopen the add-in pane.

### Root Cause
The AuthService stored tokens in memory only. When Office recreates the add-in iframe (e.g., selecting a different email), in-memory state is lost.

### Fix Applied (2026-01-23)
Implemented sessionStorage persistence in `AuthService.ts`:

**Changes made:**

1. **Added storage key constants** (line 48):
```typescript
const TOKEN_STORAGE_KEY = 'spaarke-auth-token';
const TOKEN_EXPIRY_KEY = 'spaarke-auth-expiry';
const ACCOUNT_STORAGE_KEY = 'spaarke-auth-account';
```

2. **Added `saveToStorage()` method** - Persists token, expiry, and account to sessionStorage after successful auth

3. **Added `loadFromStorage()` method** - Restores cached auth state on initialize, validates token expiry with 5-minute buffer

4. **Added `clearStorage()` method** - Clears all auth state on sign out

5. **Updated `initialize()`** - Now calls `loadFromStorage()` before checking MSAL accounts

6. **Updated `signOut()`** - Now calls `clearStorage()` to clear persisted state

7. **Updated dialog success handler** - Now calls `saveToStorage()` after receiving token

### File Modified
- [AuthService.ts](src/client/office-addins/shared/services/AuthService.ts)

### Verification
1. Sign in to the add-in
2. Switch to a different email
3. User should remain signed in (no login prompt)
4. Close and reopen the add-in pane
5. User should remain signed in (token restored from sessionStorage)

### Deployment Required
Build and deploy the updated Office Add-ins:
```bash
cd src/client/office-addins
npm run build
# Then deploy to Azure Static Web App or push to GitHub for CI/CD
```

---

## Issue 3: Pin Add-in Pane (COMPLETED)

### Symptom
User wants to keep the add-in pane open while moving between emails without manually reopening it.

### Root Cause
The manifest didn't include the `SupportsPinning` attribute and required a **nested** VersionOverrides structure.

### Fix Applied (2026-01-23)

**Key insight from Microsoft documentation:**
> Your manifest must include VersionOverrides elements for **both v1.0 and v1.1 versions** (nested).

Previous attempts failed because we were **replacing** V1.0 with V1.1 instead of **nesting** them.

**Working structure (v1.0.14.0):**
```xml
<VersionOverrides xmlns="..." xsi:type="VersionOverridesV1_0">
  <!-- V1.0 content for older clients (no SupportsPinning) -->
  ...

  <!-- Nested V1.1 for newer clients WITH SupportsPinning -->
  <VersionOverrides xmlns=".../1.1" xsi:type="VersionOverridesV1_1">
    <Action xsi:type="ShowTaskpane">
      <SourceLocation resid="Taskpane.Url"/>
      <SupportsPinning>true</SupportsPinning>
    </Action>
  </VersionOverrides>
</VersionOverrides>
```

**Critical details:**
- Outer V1.0 provides backward compatibility
- Nested V1.1 requires Mailbox 1.5 and includes SupportsPinning
- V1.1 elements need unique IDs (e.g., `msgReadCmdGroupV11`)

### Word Add-in Note
**SupportsPinning is Outlook-specific** - it does not apply to Word/Excel/PowerPoint add-ins.

### Files Modified
- `src/client/office-addins/outlook/manifest-working.xml` (v1.0.14.0)

### Verification
1. ✅ Sideload manifest-working.xml v1.0.14.0
2. ✅ Add-in installs successfully
3. ✅ Pin icon appears in task pane header
4. ✅ Pane stays open when navigating between emails

### Documentation
- [Microsoft: Pin a task pane](https://learn.microsoft.com/en-us/office/dev/add-ins/outlook/pinnable-taskpane)
- [GitHub: office-js-docs-pr/pinnable-taskpane.md](https://github.com/OfficeDev/office-js-docs-pr/blob/main/docs/outlook/pinnable-taskpane.md)

---

## Issue 4: Drag Resize Pane (Not Applicable)

### Symptom
User cannot drag to resize the add-in pane width.

### Analysis
**Draggable width is NOT possible in Outlook add-ins** - this is an Outlook platform limitation.

However, draggable pane width **IS available** out of the box for other Office apps (e.g., Word add-in task panes can be resized).

### Conclusion
This is expected behavior for Outlook and cannot be changed by the add-in. No fix required.

### Status: Not Applicable (Outlook limitation)

---

## Issue 5: "Associate with" Not Working (COMPLETED)

### Symptom
The "Associate with" entity picker doesn't return results when searching.

### Root Cause
The `useEntitySearch` hook was using **mock data** instead of calling the actual BFF API.

### Fix Applied (2026-01-23)

**Changes made:**

1. **useEntitySearch.ts** - Updated `performSearch` to call real API when `apiBaseUrl` and `getAccessToken` are provided:
   ```typescript
   if (apiBaseUrl && getAccessToken) {
     const token = await getAccessToken();
     const response = await fetch(
       `${apiBaseUrl}/office/search/entities?q=${encodeURIComponent(searchQuery)}&type=${filter.join(',')}&top=${maxResults}`,
       { headers: { 'Authorization': `Bearer ${token}` } }
     );
     // ... process response
   } else {
     // Fallback to mock data if API not configured
     console.warn('[useEntitySearch] API not configured, using mock data');
   }
   ```

2. **SaveFlow.tsx** - Pass `apiBaseUrl` and `getAccessToken` to EntityPicker:
   ```tsx
   <EntityPicker
     searchOptions={{
       apiBaseUrl,
       getAccessToken,
     }}
   />
   ```

### Files Modified
- `src/client/office-addins/shared/taskpane/hooks/useEntitySearch.ts`
- `src/client/office-addins/shared/taskpane/components/SaveFlow.tsx`

### API Endpoint
BFF already has `GET /office/search/entities` endpoint at `OfficeEndpoints.cs:692`

### Verification
1. Build and deploy the Office Add-in
2. Sign in to the add-in
3. Type 2+ characters in the "Associate With" search box
4. Should see real Dataverse entities (Matters, Projects, Accounts, Contacts)

---

## Issue 6: Create New Record Feature (Pending)

### Symptom
User needs ability to create a new Matter/Project/Account/Contact when the desired association doesn't exist.

### Current State
The EntityPicker has stub support for Quick Create via `onQuickCreate` callback, but the callback is not implemented.

### Fix Required

1. **Create QuickCreateDialog component** - Modal dialog with minimal fields
2. **Integrate into SaveFlow** - State management and handlers
3. **Create BFF API endpoint** - POST /office/entities

### Design Considerations
- Quick Create = minimal fields (name only for V1)
- Entity-specific fields can be added later
- User must have create permission for the entity type
- Auto-select newly created entity after success

### Alternative Approach
Instead of a dialog, create a dedicated "Create" tab in the add-in for full entity creation forms.

### Files to Create/Modify
- NEW: `QuickCreateDialog.tsx`
- `SaveFlow.tsx` - Integration
- BFF API - POST /office/entities endpoint

---

## Implementation Status

### Completed
- [x] Issue 1: CORS - Azure Portal configuration added
- [x] Issue 2: Login persistence - AuthService.ts updated with sessionStorage, deployed to Azure
- [x] Issue 3: Pin add-in - SupportsPinning added to manifests (requires re-sideload)

### Next Steps
1. **Re-sideload manifest** - User must sideload updated manifest for pin feature
2. **Test Issues 1, 2 & 3** - Verify CORS, login persistence, and pinning work
3. **Implement Issue 5** - Replace mock data with API calls

### Deferred
- Issue 4: Resize pane - Office platform limitation
- Issue 6: Create new record - Lower priority, needs API work

---

## Testing Checklist

### Issue 1: CORS (Ready to Test)
- [ ] Save operation completes without network error
- [ ] Console shows no CORS-related errors

### Issue 2: Login Persistence (Deployed - Ready to Test)
- [ ] Token persists when switching emails
- [ ] Token persists when closing/reopening add-in pane
- [ ] Token expires correctly after ~1 hour
- [ ] Sign out clears all stored auth state

### Issue 3: Pin (Requires Re-sideload)
- [ ] Re-sideload manifest-working.xml in Outlook
- [ ] Pin button appears in add-in header
- [ ] Pane stays open when navigating between emails

### Issue 5: Associate With (After Implementation)
- [ ] Search returns real Dataverse entities
- [ ] Type filter chips filter results correctly
- [ ] Recent entities are displayed
- [ ] Loading state shows during search

---

## Related Files

| File | Purpose |
|------|---------|
| [AuthService.ts](src/client/office-addins/shared/services/AuthService.ts) | Authentication + token persistence |
| [useEntitySearch.ts](src/client/office-addins/shared/taskpane/hooks/useEntitySearch.ts) | Entity search (currently mock) |
| [useSaveFlow.ts](src/client/office-addins/shared/taskpane/hooks/useSaveFlow.ts) | Save workflow |
| [SaveFlow.tsx](src/client/office-addins/shared/taskpane/components/SaveFlow.tsx) | Save UI component |
| [EntityPicker.tsx](src/client/office-addins/shared/taskpane/components/EntityPicker.tsx) | Entity selector |
| [manifest-working.xml](src/client/office-addins/outlook/manifest-working.xml) | Outlook manifest |
| [Program.cs](src/server/api/Sprk.Bff.Api/Program.cs) | BFF API configuration |

---

*Last updated: 2026-01-23*
