# Project: File Access OBO Refactor

**Project ID**: `spe-file-viewer-obo-refactor`
**Status**: Ready for Implementation
**Priority**: High
**Estimated Time**: 3-4 hours
**Owner**: [Your Name]
**Created**: 2025-01-25

---

## 🎯 Project Goals

Fix FileAccessEndpoints authentication to use **On-Behalf-Of (OBO) flow** instead of app-only authentication, enabling:

1. ✅ **Scalable** - No manual container permission grants
2. ✅ **Secure** - User-level access control
3. ✅ **Observable** - RFC 7807 error responses with correlation IDs
4. ✅ **Validated** - SPE pointer validation before Graph API calls

---

## 📋 Problem Statement

FileAccessEndpoints currently use **service principal (app-only)** authentication, causing:
- ❌ **403 Access Denied** errors from Graph API
- ❌ **Not scalable** - Requires manual PowerShell grants per container
- ❌ **Poor error messages** - Generic 500 errors without correlation IDs
- ❌ **No validation** - Missing SPE pointer format checks

**Error**: `Microsoft.Graph.Models.ODataErrors.ODataError: Access denied`
**Correlation ID**: `947113c6-c645-4d32-aa81-8afcb1ae056d`

---

## 🔧 Solution Overview

**Switch to OBO (On-Behalf-Of) authentication:**

```
User → PCF → BFF API → OBO Exchange → Graph API (with user context)
```

**Key Changes:**
1. Extract user token from `Authorization` header
2. Validate SPE pointers (driveId, itemId)
3. Exchange user token for Graph token (OBO)
4. Call Graph API with user permissions
5. Return RFC 7807 Problem Details on errors

---

## 📦 Deliverables

### Code Changes

1. **SdapProblemException** (new)
   - Custom exception for validation errors
   - Maps to RFC 7807 Problem Details

2. **Global Exception Handler** (Program.cs)
   - Converts exceptions to structured errors
   - Includes correlation IDs

3. **IGraphClientFactory** (interface update)
   - `ForUserAsync(HttpContext)` - OBO
   - `ForApp()` - App-only for background tasks

4. **FileAccessEndpoints.cs** (4 endpoints refactored)
   - SPE pointer validation
   - OBO authentication
   - Structured responses

5. **SpeFileStore.cs** (methods updated)
   - Accept `HttpContext` parameter
   - Use OBO via `ForUserAsync()`

6. **DocumentStorageResolver** (validation added)
   - Throw on missing/invalid SPE pointers

### Documentation

- ✅ Technical Review (FILE-ACCESS-OBO-REFACTOR-REVIEW.md)
- Testing strategy
- Configuration checklist
- Rollback plan

---

## 🗂️ Task Breakdown

| # | Task | Est. Time | Status |
|---|------|-----------|--------|
| 01 | Create SdapProblemException class | 15 min | Not Started |
| 02 | Add Global Exception Handler | 20 min | Not Started |
| 03 | Update IGraphClientFactory interface | 10 min | Not Started |
| 04 | Implement ForUserAsync/ForApp | 15 min | Not Started |
| 05 | Refactor FileAccessEndpoints (4 endpoints) | 45 min | Not Started |
| 06 | Update SpeFileStore methods | 30 min | Not Started |
| 07 | Add DocumentStorageResolver validation | 20 min | Not Started |
| 08 | Update Program.cs DI registration | 10 min | Not Started |
| 09 | Build and test locally | 30 min | Not Started |
| 10 | Deploy to Azure and verify | 30 min | Not Started |
| **Total** | | **~3.5 hours** | |

---

## 📚 Key Documents

- [Technical Review](./FILE-ACCESS-OBO-REFACTOR-REVIEW.md) - Comprehensive implementation guide
- [SDAP Architecture Guide](../../../docs/architecture/SDAP-ARCHITECTURE-GUIDE-10-20-2025.md) - System architecture
- [OBO Implementation](../../api/Spe.Bff.Api/Api/OBOEndpoints.cs) - Working OBO example

---

## ✅ Success Criteria

### Build Success
- [ ] No compilation errors
- [ ] All unit tests pass
- [ ] Deployment succeeds

### Functional Success
- [ ] User with container access gets preview URL (200 OK)
- [ ] User without access gets 403 Forbidden (expected)
- [ ] Invalid document ID → 400 `"invalid_id"`
- [ ] Missing driveId → 409 `"mapping_missing_drive"`
- [ ] Missing itemId → 409 `"mapping_missing_item"`

### Performance Success
- [ ] Request latency < 300ms (with cache)
- [ ] OBO token cache hit rate > 90%

### Business Success
- [ ] PCF file preview works end-to-end
- [ ] Zero manual container grants needed
- [ ] Clear error messages with correlation IDs

---

## 🚨 Risks

### High Risk
1. **PCF sending wrong token audience** - Must be BFF token, not Graph
2. **Missing SPE pointers in Dataverse** - Test documents must have fields populated

### Medium Risk
3. **OBO token exchange failures** - Verify delegated permissions
4. **Performance impact** - Minimal (~5ms cached, ~200ms uncached)

---

## 🔄 Rollback Plan

1. Revert Git commits
2. Rebuild and redeploy previous version
3. Time to rollback: ~10 minutes

---

## 📞 Support

- **Technical Questions**: Review [Technical Review](./FILE-ACCESS-OBO-REFACTOR-REVIEW.md)
- **Architecture Questions**: See [SDAP Architecture Guide](../../../docs/architecture/SDAP-ARCHITECTURE-GUIDE-10-20-2025.md)
- **OBO Examples**: Refer to [OBOEndpoints.cs](../../../src/api/Spe.Bff.Api/Api/OBOEndpoints.cs)

---

## 📝 Notes

- All tasks should be completed in order
- Test each component before proceeding
- Update this overview as tasks are completed
- Document any deviations from the plan

---

**Next Step**: Begin with [01-TASK-SdapProblemException.md](./01-TASK-SdapProblemException.md)
