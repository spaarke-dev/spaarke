# SDAP Production Requirements - Critical Issues

**Created:** 2025-10-08
**Status:** BLOCKING - Must be resolved before production deployment

---

## 🚨 CRITICAL: Authorization Policies Not Implemented

### Issue

The Spe.Bff.Api has **placeholder authorization policies** that always return `true`:

**File:** `src/api/Spe.Bff.Api/Program.cs:26-31`
```csharp
options.AddPolicy("canmanagecontainers", p => p.RequireAssertion(_ => true)); // TODO
options.AddPolicy("canwritefiles", p => p.RequireAssertion(_ => true)); // TODO
```

### Impact

- ❌ **Any authenticated user can manage containers** (create, delete)
- ❌ **Any authenticated user can write files** to any drive
- ❌ **No role-based access control** enforced
- ❌ **Production security risk**

### Affected Endpoints

**Container Management (MI):**
- `POST /api/containers` - Requires: `canmanagecontainers`
- `GET /api/containers?containerTypeId={id}` - Requires: `canmanagecontainers`
- `GET /api/containers/{id}/drive` - Requires: `canmanagecontainers`

**File Operations (MI):**
- `PUT /api/drives/{driveId}/upload` - Requires: `canwritefiles`
- `DELETE /api/drives/{driveId}/items/{itemId}` - Requires: `canwritefiles`

### Required Fix

Implement proper authorization based on:

**Option 1: Role-Based (Azure AD Roles)**
```csharp
options.AddPolicy("canmanagecontainers", p =>
    p.RequireRole("ContainerAdmin", "SystemAdmin"));

options.AddPolicy("canwritefiles", p =>
    p.RequireRole("FileWriter", "ContainerAdmin", "SystemAdmin"));
```

**Option 2: Claim-Based (Custom Claims)**
```csharp
options.AddPolicy("canmanagecontainers", p =>
    p.RequireClaim("permission", "containers.manage"));

options.AddPolicy("canwritefiles", p =>
    p.RequireClaim("permission", "files.write"));
```

**Option 3: Custom Policy Handler**
```csharp
options.AddPolicy("canmanagecontainers", p =>
    p.Requirements.Add(new ContainerManagementRequirement()));
```

### Priority

**🔴 CRITICAL** - Must be implemented before production deployment

---

## 🔧 REQUIRED: Admin Utility for Container Management

### Issue

There is **no admin tool** to:
- List available SPE containers
- Get Drive IDs from Container IDs
- Browse files and test operations
- Manage container lifecycle

### Current Workaround

Manually entering Drive IDs in forms, which is:
- ❌ **Not scalable**
- ❌ **Error-prone**
- ❌ **No validation**
- ❌ **No visibility into container structure**

### Required Solution

Build an **Admin Utility** with these features:

#### Must-Have Features

1. **Container Management**
   - List all containers for Container Type: `8a6ce34c-6055-4681-8f87-2f4f9f921c06`
   - View container details (name, description, created date)
   - Get Drive ID for each container
   - Create new containers (admin only)

2. **File Browser**
   - Browse files in selected container
   - View file metadata
   - Test upload/download
   - Delete files (admin only)

3. **Testing Tools**
   - Test API connectivity
   - Verify user permissions
   - Check authentication tokens
   - View API responses

4. **User Capabilities**
   - Show what user can do in each container
   - Display permission errors clearly
   - Help troubleshoot access issues

#### Implementation Options

**Option A: Power Apps Custom Page (React)**
- ✅ Full React app with rich UI
- ✅ Direct MSAL authentication
- ✅ Can call BFF API directly
- ✅ Easy to deploy and maintain
- **Recommended for:** Full-featured admin tool

**Option B: PCF Control (Admin Panel)**
- ✅ Embeddable in forms
- ✅ Reuses existing PCF infrastructure
- ✅ Can be added to any entity
- ❌ Limited screen real estate
- **Recommended for:** Quick inline admin tasks

**Option C: Model-Driven App**
- ✅ Standard Power Apps experience
- ✅ Can mix forms + custom pages
- ✅ Built-in navigation and security
- ❌ More complex to build
- **Recommended for:** Enterprise admin portal

### Priority

**🟡 HIGH** - Needed for testing and production support

---

## 🔀 DECISION REQUIRED: MI vs OBO for File Upload

### Current Situation

Quick Create PCF can use either authentication pattern:

#### Option A: Managed Identity (MI)

**Endpoint:** `PUT /api/drives/{driveId}/upload`

**Pros:**
- ✅ Simple - just needs Drive ID
- ✅ Works with existing Drive ID we have

**Cons:**
- ❌ Uses service account (not user context)
- ❌ No user permission checks
- ❌ Requires `canwritefiles` auth policy (currently placeholder)
- ❌ Files owned by service account, not user

#### Option B: On-Behalf-Of (OBO)

**Endpoint:** `PUT /api/obo/containers/{containerId}/files/{fileName}`

**Pros:**
- ✅ Preserves user identity
- ✅ User permission checks enforced
- ✅ Files owned by actual user
- ✅ Audit trail shows real user
- ✅ No placeholder auth policies needed

**Cons:**
- ❌ Requires Container ID (not Drive ID)
- ❌ Need admin tool or lookup logic to get Container ID

### Recommendation

**Use OBO for production** because:
1. Security: User permissions enforced
2. Compliance: Proper audit trail
3. Correctness: Files owned by user

**Use MI for initial testing** to verify:
1. PCF → BFF → SPE flow works
2. File upload mechanics work
3. Dataverse record creation works

### Action Items

- [ ] Test with MI endpoint first (quick validation)
- [ ] Build admin tool to get Container IDs
- [ ] Switch to OBO endpoint for production
- [ ] Document the decision and rationale

### Priority

**🟡 HIGH** - Impacts security and compliance

---

## 📊 Summary

| Issue | Priority | Impact | Blocker? |
|-------|----------|--------|----------|
| Authorization Policies | 🔴 CRITICAL | Security risk | YES |
| Admin Utility | 🟡 HIGH | Testing & support | NO (workaround exists) |
| MI vs OBO Decision | 🟡 HIGH | Security & compliance | NO (can start with MI) |

---

## 🎯 Recommended Action Plan

### Phase 1: Immediate (Testing)
1. ✅ Use MI endpoint with Drive ID for initial testing
2. ✅ Verify PCF → BFF → SPE flow works
3. ✅ Document test results

### Phase 2: Production Prep (Critical)
1. 🔴 **MUST DO:** Implement authorization policies
2. 🟡 Build admin utility for Container management
3. 🟡 Switch to OBO endpoint for user-context uploads
4. 🟡 End-to-end testing with real scenarios

### Phase 3: Production Deployment
1. Deploy with OBO endpoints only
2. Proper authorization enforced
3. Admin tool available for support
4. Complete documentation

---

## 📝 References

- **Code Review:** `docs/SDAP_Spe_Bff_Api_Code_Review.md`
- **API Endpoints:** Section 2 (lines 66-84)
- **Authorization:** Section 4 (lines 169-176)
- **End of Day Status:** `dev/projects/sdap_project/Sprint 7B Doc Quick Create/END-OF-DAY-STATUS-2025-10-08.md`

---

**Next Review:** After Phase 1 testing complete
