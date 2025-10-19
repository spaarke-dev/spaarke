# Test OBO Upload After Registration

## Registration Complete ✅

The BFF API has been successfully registered as a guest app with container type `8a6ce34c-6055-4681-8f87-2f4f9f921c06`.

**Registered Applications:**
- **Owning App** (170c98e1-d486-4355-bcbe-170454e0207c): Full delegated & app-only permissions
- **BFF API** (1e40baad-e065-4aea-a8d4-4b7ab273458c): ReadContent & WriteContent delegated permissions

**BFF API Restart**: ✅ Completed (MSAL token cache cleared)

## Test the OBO Upload Endpoint

The 403 Forbidden error should now be resolved. Test with:

### 1. Get a User Token (Token A)

Use the PCF control to authenticate and get a token for the BFF API.

```javascript
// In your PCF control
const tokenA = await context.webAPI.getAccessToken();
```

### 2. Test File Upload via OBO

```http
PUT https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/containers/{containerId}/files/test.txt
Authorization: Bearer {tokenA}
Content-Type: text/plain

Hello World - Testing OBO Upload After Registration
```

### 3. Expected Result

**Before Registration**: HTTP 403 Forbidden
```json
{
  "error": {
    "code": "accessDenied",
    "message": "Access denied..."
  }
}
```

**After Registration**: HTTP 200 OK
```json
{
  "id": "...",
  "name": "test.txt",
  "size": 51,
  "webUrl": "https://..."
}
```

## What Changed?

### Root Cause (Discovered)

SharePoint Embedded container type management APIs require **certificate authentication**, not client secret. All previous registration attempts failed because we were using client secret to get the token.

### Solution Applied

1. **Downloaded certificate** from Azure Key Vault (`spaarke-spekvcert/spe-app-cert`)
2. **Imported certificate** to local certificate store
3. **Created JWT assertion** signed with certificate private key
4. **Acquired token** using certificate-based client credentials flow
5. **Registered applications** using PUT request to SharePoint API

### Why This Fixes the 403 Error

The 403 error occurred because:
- BFF API was NOT registered as a guest app with the container type
- When BFF API exchanged Token A for Token B (via OBO), Token B had `appid: BFF-API`
- SharePoint checked: "Is BFF-API registered with this container type?"
- Answer was NO → 403 Forbidden

Now that BFF API IS registered:
- Same OBO flow: Token B has `appid: BFF-API`
- SharePoint checks: "Is BFF-API registered with this container type?"
- Answer is YES with ReadContent & WriteContent permissions → 200 OK

## Troubleshooting

### If you still get 403 after registration:

1. **Verify restart completed**:
   ```bash
   az webapp show --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2 --query state
   ```
   Should show "Running"

2. **Check BFF API logs**:
   ```bash
   az webapp log tail --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2
   ```
   Look for MSAL token acquisition logs

3. **Test with fresh user token**:
   - Log out of PCF control
   - Log back in
   - Get new Token A
   - Try upload again

4. **Verify container ID**:
   Make sure you're testing with a container that belongs to this container type:
   ```bash
   GET https://graph.microsoft.com/v1.0/storage/fileStorage/containers/{containerId}
   ```
   Check that `containerTypeId` matches `8a6ce34c-6055-4681-8f87-2f4f9f921c06`

## Technical Details

### Certificate Authentication Flow

```
1. Load certificate from local store (thumbprint: 269691A5A60536050FA76C0163BD4A942ECD724D)
2. Create JWT assertion:
   - Header: { alg: "RS256", typ: "JWT", x5t: "<thumbprint-base64>" }
   - Payload: { aud: "login.microsoftonline.com", iss: "<app-id>", sub: "<app-id>", ... }
   - Signature: Sign with certificate private key using RS256
3. Exchange JWT for access token:
   POST https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token
   - client_id: 170c98e1-d486-4355-bcbe-170454e0207c
   - client_assertion_type: urn:ietf:params:oauth:client-assertion-type:jwt-bearer
   - client_assertion: {signed-jwt}
   - scope: https://spaarke.sharepoint.com/.default
   - grant_type: client_credentials
4. Use token to call SharePoint API:
   PUT https://spaarke.sharepoint.com/_api/v2.1/storageContainerTypes/{id}/applicationPermissions
```

### Registration API Request

```json
PUT https://spaarke.sharepoint.com/_api/v2.1/storageContainerTypes/8a6ce34c-6055-4681-8f87-2f4f9f921c06/applicationPermissions
Authorization: Bearer {certificate-based-token}
Content-Type: application/json

{
  "value": [
    {
      "appId": "170c98e1-d486-4355-bcbe-170454e0207c",
      "delegated": ["full"],
      "appOnly": ["full"]
    },
    {
      "appId": "1e40baad-e065-4aea-a8d4-4b7ab273458c",
      "delegated": ["ReadContent", "WriteContent"],
      "appOnly": ["none"]
    }
  ]
}
```

## Success Criteria

✅ Registration API returned HTTP 200 OK
✅ BFF API restarted successfully
⏳ OBO upload returns HTTP 200 OK (test this now!)

## Next Steps

1. Test the OBO upload endpoint with your PCF control
2. If successful, the 403 issue is RESOLVED
3. You can now implement full file upload/download functionality via OBO
4. Consider adding error handling for token refresh scenarios
