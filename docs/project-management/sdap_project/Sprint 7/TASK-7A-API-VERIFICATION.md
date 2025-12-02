# Sprint 7A - SDAP BFF API Verification

**Date**: 2025-10-06
**Status**: ✅ READY FOR TESTING
**API URL**: `https://spe-api-dev-67e2xz.azurewebsites.net`

## Executive Summary

The SDAP BFF API is fully operational and ready for Sprint 7A Universal Dataset Grid testing. All critical components have been verified:

- ✅ API is running and responding
- ✅ Dataverse connection working (after ServiceClient fix)
- ✅ CORS configured for Power Apps domain
- ✅ Authentication/Authorization configured
- ✅ File operation endpoints deployed
- ✅ OBO (On-Behalf-Of) authentication ready

## Health Check Results

### 1. Service Status
```bash
GET https://spe-api-dev-67e2xz.azurewebsites.net/ping

Response:
{
  "service": "Spe.Bff.Api",
  "version": "1.0.0",
  "environment": "Development",
  "timestamp": "2025-10-06T14:19:24.8247807+00:00"
}
```
**Status**: ✅ OPERATIONAL

### 2. Dataverse Connection
```bash
GET https://spe-api-dev-67e2xz.azurewebsites.net/healthz/dataverse

Response:
{
  "status": "healthy",
  "message": "Dataverse connection successful"
}
```
**Status**: ✅ OPERATIONAL

### 3. General Health
```bash
GET https://spe-api-dev-67e2xz.azurewebsites.net/healthz

Response: Healthy
```
**Status**: ✅ OPERATIONAL

## API Configuration

### Authentication
- **Type**: Azure AD JWT Bearer Token (OAuth 2.0)
- **Authority**: `https://login.microsoftonline.com/a221a95e-6abc-4434-aecc-e48338a1b2f2`
- **Audience**: API App ID `170c98e1-d486-4355-bcbe-170454e0207c`
- **Required Scopes**: Based on operation (see Authorization Policies)

### CORS Configuration
**Allowed Origins**:
- `http://localhost:3000`
- `http://localhost:3001`
- `http://127.0.0.1:3000`
- `https://spaarkedev1.crm.dynamics.com` ✅
- `https://spaarkedev1.api.crm.dynamics.com` ✅

**Headers**:
- Authorization
- Content-Type
- Accept
- X-Requested-With

**Status**: ✅ Power Apps domain configured

### Dataverse Integration
- **Environment**: `https://spaarkedev1.api.crm.dynamics.com`
- **Auth Type**: Client Secret (S2S)
- **App Registration**: `170c98e1-d486-4355-bcbe-170454e0207c`
- **Application User**: "Spaarke DSM-SPE Dev 2"
- **Security Role**: System Administrator
- **Implementation**: `ServiceClient` with connection string
- **Status**: ✅ OPERATIONAL

### SharePoint Embedded Integration
- **Method**: On-Behalf-Of (OBO) authentication
- **Graph API**: `https://graph.microsoft.com/v1.0/`
- **Container ID (Test)**: `b!rAta3Ht_zEKl6AqiQObblUhqWZU646tBrEagKKMKiOcv-7Yo7739SKCuM2H-RPAy`
- **Status**: ✅ READY (requires user authentication)

## Available Endpoints for Sprint 7A Testing

### File Operations (Require User Authentication)

#### 1. Download File
```http
GET /api/drives/{driveId}/items/{itemId}/content
Authorization: Bearer {user_token}
```
**Used by**: Download button in Universal Dataset Grid

#### 2. Delete File
```http
DELETE /api/drives/{driveId}/items/{itemId}
Authorization: Bearer {user_token}
```
**Used by**: Delete button in Universal Dataset Grid

#### 3. Replace File (Upload)
```http
PUT /api/containers/{containerId}/files/{*path}
Authorization: Bearer {user_token}
Content-Type: application/octet-stream
Body: [file content]
```
**Used by**: Replace button in Universal Dataset Grid

#### 4. List Files in Container
```http
GET /api/drives/{driveId}/children
Authorization: Bearer {user_token}
```
**Used by**: Universal Dataset Grid data loading

### Document Metadata Operations

#### 5. Get Document
```http
GET /api/dataverse/documents/{id}
Authorization: Bearer {user_token}
```

#### 6. Update Document
```http
PATCH /api/dataverse/documents/{id}
Authorization: Bearer {user_token}
Content-Type: application/json
Body: {
  "name": "string",
  "fileName": "string",
  "fileSize": number,
  "graphItemId": "string",
  "graphDriveId": "string",
  "hasFile": boolean
}
```

## Authorization Policies

The API implements granular authorization policies. Each operation requires specific permissions checked against the user's Dataverse access rights:

| Operation | Policy | Dataverse Right Required |
|-----------|--------|-------------------------|
| Download | `candownloadfiles` | Read |
| Delete | `candeletefiles` | Delete |
| Upload/Replace | `canreplacefiles` | Write |
| List | `canlistchildren` | Read |
| Preview | `canpreviewfiles` | Read |

## Testing Strategy for Sprint 7A

### Prerequisites
1. ✅ API deployed and running
2. ✅ Dataverse connection operational
3. ✅ CORS configured for Power Apps
4. ✅ Universal Dataset Grid PCF control deployed
5. ⏭️ Test user authenticated in Power Apps
6. ⏭️ Test container with sample files

### Test Scenarios

#### Scenario 1: View Files in Grid
**Steps**:
1. Open Power Apps form with Universal Dataset Grid control
2. Grid loads documents from Dataverse
3. For each document with `hasFile=true`, display file info
4. Verify SharePoint link is clickable and opens in new tab

**Expected Result**: Grid displays all documents with file metadata

#### Scenario 2: Download File
**Steps**:
1. Click Download button on a document row
2. API calls `/api/drives/{driveId}/items/{itemId}/content` with user token (OBO)
3. File downloads to browser

**Expected Result**: File downloads successfully

#### Scenario 3: Delete File
**Steps**:
1. Click Delete button on a document row
2. Confirmation dialog appears
3. Click Confirm
4. API calls `/api/drives/{driveId}/items/{itemId}` DELETE with user token (OBO)
5. Dataverse document updated: `hasFile=false`, file fields cleared
6. Grid refreshes

**Expected Result**: File deleted from SPE, document metadata updated

#### Scenario 4: Replace File
**Steps**:
1. Click Replace button on a document row
2. File picker dialog appears
3. Select new file
4. API calls `/api/containers/{containerId}/files/{path}` PUT with user token (OBO)
5. New file uploaded to SPE
6. Dataverse document updated with new file metadata
7. Grid refreshes

**Expected Result**: Old file replaced, new file metadata in Dataverse

### Why Direct API Testing is Limited

**All file operation endpoints require user authentication (OBO pattern)**:

1. **User Authentication Required**: The API uses On-Behalf-Of (OBO) flow to call SharePoint Embedded on behalf of the authenticated user
2. **Token Exchange**: User's Power Apps token → API exchanges for Graph API token → Calls SPE
3. **Permission Checks**: API validates user has appropriate Dataverse permissions before allowing operations

**This is by design** - file operations must be performed by authenticated users with proper permissions, not by the API service principal directly.

### Testing via PCF Control (Recommended)

The Universal Dataset Grid PCF control is the correct testing mechanism because:

1. ✅ Runs in authenticated Power Apps context
2. ✅ Has user's authentication token
3. ✅ Calls API with proper Authorization header
4. ✅ Respects user permissions
5. ✅ Tests the complete end-to-end flow

## Deployment Information

### Latest Deployment
- **Date**: 2025-10-06
- **Deployment ID**: Latest (after ServiceClient fix)
- **Build**: Release mode
- **Bundle Size**: ~50 MB (includes ServiceClient SDK)

### Key Changes in This Deployment
1. ✅ Switched from `DataverseWebApiService` (custom HttpClient) to `DataverseServiceClientImpl` (Microsoft SDK)
2. ✅ Added `Microsoft.PowerPlatform.Dataverse.Client` NuGet package
3. ✅ Updated DI registration in `Program.cs`
4. ✅ Fixed Dataverse authentication (now uses ServiceClient connection string)
5. ✅ All CORS domains configured
6. ✅ Client secret properly configured

## Next Steps for Sprint 7A Completion

1. ⏭️ **Create test data in Dataverse**
   - Create Document records linked to container `b!rAta3Ht_zEKl6AqiQObblUhqWZU646tBrEagKKMKiOcv-7Yo7739SKCuM2H-RPAy`
   - Upload files to SharePoint Embedded container
   - Populate document records with file metadata

2. ⏭️ **Test PCF Control in Power Apps**
   - Open form with Universal Dataset Grid
   - Verify grid loads document data
   - Test Download button
   - Test Delete button (with confirmation)
   - Test Replace button (with file picker)

3. ⏭️ **Verify Dataverse Updates**
   - After file operations, check document records updated correctly
   - Verify `hasFile`, `fileName`, `fileSize`, `graphItemId`, etc.

4. ⏭️ **Document Test Results**
   - Screenshot of working grid
   - Test each button operation
   - Verify error handling

## Troubleshooting

### If file operations fail:

1. **Check user authentication**
   ```javascript
   // In PCF control
   console.log('User token:', context.webAPI.token);
   ```

2. **Check CORS in browser console**
   - Should see API responses, not CORS errors
   - If CORS error: verify Power Apps domain in API CORS config

3. **Check API logs**
   ```bash
   az webapp log tail --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2
   ```

4. **Check Dataverse document metadata**
   - Verify `graphdriveid` and `graphitemid` fields populated
   - Verify container ID matches

5. **Check user permissions**
   - User must have appropriate Dataverse permissions on Document table
   - Security role must allow Read/Write/Delete as needed

## References

- [Sprint 7A Dataverse Auth Fix](./TASK-7A-DATAVERSE-AUTH-FIX.md)
- [Universal Dataset Grid Implementation](../../src/controls/UniversalDatasetGrid/)
- [SDAP BFF API Source](../../src/api/Spe.Bff.Api/)
- [Dataverse Service Implementation](../../src/shared/Spaarke.Dataverse/)

## Status Summary

| Component | Status | Notes |
|-----------|--------|-------|
| API Service | ✅ Operational | Running on Azure App Service |
| Dataverse Connection | ✅ Operational | ServiceClient with client secret |
| Health Endpoints | ✅ Passing | All checks return healthy |
| Authentication | ✅ Configured | Azure AD JWT Bearer |
| Authorization | ✅ Configured | Policy-based with Dataverse checks |
| CORS | ✅ Configured | Power Apps domain allowed |
| File Operations | ✅ Ready | Require user authentication |
| PCF Control | ✅ Deployed | Ready for testing |

**Overall Status**: ✅ **READY FOR SPRINT 7A TESTING**
