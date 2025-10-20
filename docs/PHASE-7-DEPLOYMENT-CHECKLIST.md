# Phase 7 Deployment Checklist

> **STATUS: ✅ COMPLETE** - Phase 7 deployed and operational.
>
> This checklist has been superseded by [PHASE-7-DEPLOYMENT-STATUS.md](./PHASE-7-DEPLOYMENT-STATUS.md).
> Retained for historical reference.

**Phase**: Navigation Property Metadata Discovery
**Status**: ✅ **COMPLETE** - Deployed and Working
**Last Updated**: October 20, 2025

---

## Deployment Summary

### What Was Deployed

**Phase 7 Implementation** (Tasks 7.1-7.5):
- ✅ Task 7.1: Extend IDataverseService with metadata methods
- ✅ Task 7.2: Create NavMapEndpoints REST API in BFF
- ✅ Task 7.3: Create NavMapClient TypeScript service
- ✅ Task 7.4: Integrate NavMapClient in UniversalQuickCreate PCF
- ✅ Task 7.5: Deploy PCF to Dataverse (with OAuth + URL fixes)

**Git Commits**:
- `cdcb49f` - Task 7.4 initial integration
- `a4196a1` - OAuth scope fix
- `f4654ae` - URL path fix

**Deployment Targets**:
- BFF API: spe-api-dev-67e2xz.azurewebsites.net (deployed Oct 15)
- PCF Control: SPAARKE DEV 1 Dataverse (deployed Oct 20)

---

## Current Status: ✅ COMPLETE

### Resolution Summary

Phase 7 code is **100% complete and working**. All infrastructure issues have been resolved.

**Initial Error**: `500 Internal Server Error` from NavMap API
**Root Cause**: Authentication method issue (ManagedIdentity → Connection String)
**Solution**: Changed to connection string authentication (ClientSecretCredential)
**Result**: Phase 7 operational and validated with Matter and Project entities

### Browser Test Results

✅ **OAuth Authentication**: Working correctly
✅ **PCF → BFF API Communication**: Working correctly
✅ **API URL Path**: Fixed (was `/api/api/`, now `/api/navmap/`)
✅ **BFF → Dataverse Connection**: Working correctly (connection string authentication)

### Console Output (Browser)

### Initial Errors (RESOLVED)

```
❌ [MsalAuthProvider] Using cached token (expires in 65 minutes)
❌ [NavMapClient] Getting lookup navigation {childEntity: 'sprk_document', relationship: 'sprk_matter_document'}
❌ GET https://spe-api-dev-67e2xz.azurewebsites.net/api/navmap/sprk_document/sprk_matter_document/lookup 500 (Internal Server Error)
❌ [NavMapClient] Failed to get lookup navigation Error: Server error occurred while querying metadata
```

### Current Output (SUCCESS)

```
✅ [MsalAuthProvider] Using cached token (expires in 65 minutes)
✅ [NavMapClient] Getting lookup navigation {childEntity: 'sprk_document', relationship: 'sprk_matter_document'}
✅ GET /api/navmap/sprk_document/sprk_matter_document/lookup 200 OK
✅ [Phase 7] Using navigation property: sprk_Matter (source: cache)
✅ [DocumentRecordService] Document created successfully
```

### Server Error (RESOLVED)

**Initial Error**:
```
Microsoft.PowerPlatform.Dataverse.Client.Utils.DataverseConnectionException: Failed to connect to Dataverse
 ---> System.Exception: ExternalTokenManagement Authentication Requested but not configured correctly. 003
 ---> Azure.Identity.AuthenticationFailedException: ManagedIdentityCredential authentication failed
```

**Solution Applied**: Changed authentication to connection string method (see [DataverseServiceClientImpl.cs](../src/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs))
```csharp
var connectionString = $"AuthType=ClientSecret;Url={dataverseUrl};ClientId={clientId};ClientSecret={clientSecret}";
_serviceClient = new ServiceClient(connectionString);
```

---

## ~~Required Action: Register Managed Identity~~ ✅ RESOLVED

**NOTE**: This section is obsolete. The solution did NOT require Managed Identity registration.

**Actual Solution Implemented**:
1. Changed authentication method to **connection string** (ClientSecretCredential)
2. Added **Dynamics CRM API permission** to BFF API App Registration
3. Created **Application User** in Dataverse with App ID `1e40baad-e065-4aea-a8d4-4b7ab273458c`
4. Granted admin consent for API permission

**See**: [PHASE-7-DEPLOYMENT-STATUS.md](./PHASE-7-DEPLOYMENT-STATUS.md) for actual implementation details.

<details>
<summary>Original Managed Identity Instructions (NOT USED - Retained for Reference)</summary>

1. **Verify Managed Identity** (already exists):
   - Principal ID: `56ae2188-c978-4734-ad16-0bc288973f20`
   - Web App: `spe-api-dev-67e2xz`

2. **Get Application ID** from Azure AD Enterprise Applications:
   - Search for Principal ID in Managed Identities
   - Copy the Application ID

3. **Create Application User in Dataverse**:
   - Power Platform Admin Center → SPAARKE DEV 1
   - Settings → Application users → New app user
   - Add the Managed Identity using Application ID

4. **Assign Security Roles**:
   - **Dev/Test**: System Administrator (simplest)
   - **Production**: Custom role with specific permissions

</details>

---

## Post-Registration Testing

Once Managed Identity is registered, test Phase 7 end-to-end:

### Test 1: API Direct Test

```bash
# Should return 200 OK with JSON response
curl -i "https://spe-api-dev-67e2xz.azurewebsites.net/api/navmap/sprk_document/sprk_matter_document/lookup"
```

**Expected Response**:
```json
{
  "navigationPropertyName": "sprk_Matter",
  "targetEntity": "sprk_matter",
  "logicalName": "sprk_matter",
  "schemaName": "sprk_Matter",
  "source": "metadata_query",
  "childEntity": "sprk_document",
  "relationship": "sprk_matter_document"
}
```

### Test 2: PCF Browser Test

1. Navigate to a Matter record in Dataverse
2. Open Document Upload custom page
3. Upload a test document
4. **Expected Console Output**:
   ```
   ✅ [Phase 7] Querying navigation metadata for sprk_matter
   ✅ [Phase 7] Using navigation property: sprk_Matter (source: metadata_query)
   ✅ [DocumentRecordService] Document created successfully
   ✅ [DocumentUploadForm] Record creation complete {successCount: 1, failureCount: 0}
   ```
5. Verify document appears in Matter's Documents grid
6. Verify document is linked correctly to Matter record

### Test 3: Caching Test

1. Upload first document (queries BFF API)
2. Upload second document within 15 minutes
3. **Expected**: Second upload uses cached metadata (no API call)
4. Check Network tab: Should see only ONE API call to `/api/navmap/...`

### Test 4: Multi-Entity Test (Future)

Once configured for other entities:
- Test document upload on Project record
- Test document upload on Invoice record
- Verify dynamic metadata discovery works for all entities

---

## Production Deployment

After successful DEV testing, deploy to production:

### Prerequisites

1. ✅ Phase 7 validated in DEV
2. ✅ All browser tests passing
3. ✅ Performance acceptable (cache working)
4. ✅ No errors in Azure Application Insights

### Production Steps

1. **Register Production Managed Identity**:
   - Follow same steps as DEV
   - Use **custom security role** (NOT System Administrator)
   - Test API endpoint before PCF deployment

2. **Deploy PCF to Production Dataverse**:
   ```bash
   # Authenticate to production environment
   pac auth create --name ProdDeploy --url https://yourorg.crm.dynamics.com

   # Deploy PCF control
   cd src/controls/UniversalQuickCreate
   pac pcf push --publisher-prefix sprk
   ```

3. **Validate Production**:
   - Test with real user accounts
   - Monitor Application Insights for errors
   - Verify cache metrics

---

## Rollback Plan

If issues occur in production:

### Option 1: Disable Phase 7 Feature (Quick)

1. Modify PCF control to use hardcoded navigation properties (revert to pre-Phase 7)
2. Redeploy PCF control
3. No BFF API changes needed (backward compatible)

### Option 2: Revert BFF API Deployment

1. Redeploy previous version without NavMapEndpoints
2. PCF will gracefully handle API 404 (has fallback)

---

## Known Limitations

### Current Implementation

1. **Managed Identity Required**: BFF API requires Azure Managed Identity for Dataverse auth
2. **No Fallback Auth**: If Managed Identity fails, entire Dataverse connection fails
3. **Environment-Specific**: Managed Identity must be registered in EACH environment

### Future Enhancements

1. **Multiple Auth Methods**: Add connection string fallback for local development
2. **User-Assigned MI**: Support user-assigned Managed Identities for shared services
3. **Certificate Auth**: Alternative to Managed Identity for on-premises scenarios

---

## Monitoring & Alerts

### Recommended Application Insights Queries

**Phase 7 API Errors**:
```kusto
requests
| where url contains "/api/navmap/"
| where resultCode >= 400
| summarize count() by resultCode, url
| order by count_ desc
```

**Managed Identity Auth Failures**:
```kusto
traces
| where message contains "ManagedIdentityCredential" or message contains "Failed to connect to Dataverse"
| project timestamp, severityLevel, message
| order by timestamp desc
```

**Cache Performance**:
```kusto
dependencies
| where name contains "NavMap" or name contains "Dataverse"
| summarize avg(duration), count() by name
```

---

## Support Contacts

| Role | Contact | Responsibility |
|------|---------|----------------|
| **Azure Administrator** | IT Ops Team | Managed Identity setup |
| **Dataverse Administrator** | Power Platform Team | Application User registration |
| **Development Team** | Ralph Schroeder | Phase 7 code issues |
| **DevOps** | CI/CD Team | Deployment automation |

---

## Related Documentation

- [KM-REGISTER-MANAGED-IDENTITY-DATAVERSE.md](./KM-REGISTER-MANAGED-IDENTITY-DATAVERSE.md) - Setup guide
- [Phase 7 Implementation Notes](../dev/projects/sdap_V2/tasks/phase-7-pcf-meta-data-bindings/) - Technical details
- [ADR-008: Authentication Strategy](../ADRs/ADR-008-Authentication.md) - Architecture decision
- [Managed Identity Troubleshooting](https://learn.microsoft.com/en-us/azure/active-directory/managed-identities-azure-resources/managed-identities-faq)

---

## Change Log

| Date | Change | Notes |
|------|--------|-------|
| 2025-10-20 | Initial deployment checklist | Phase 7 code complete, blocked on MI setup |
| 2025-10-20 | Added browser test results | Confirmed OAuth + URL fixes working |
| 2025-10-20 | Created MI registration guide | [KM-REGISTER-MANAGED-IDENTITY-DATAVERSE.md](./KM-REGISTER-MANAGED-IDENTITY-DATAVERSE.md) |

---

**Status**: ✅ **COMPLETE** - Phase 7 deployed and operational

**See Current Status**: [PHASE-7-DEPLOYMENT-STATUS.md](./PHASE-7-DEPLOYMENT-STATUS.md)
