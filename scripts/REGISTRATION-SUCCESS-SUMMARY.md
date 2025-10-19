# BFF API Container Type Registration - SUCCESS ✅

**Date**: 2025-10-09
**Status**: REGISTRATION COMPLETE

## What Was Done

Successfully registered the BFF API (`1e40baad-e065-4aea-a8d4-4b7ab273458c`) as a guest application with SharePoint Embedded container type `8a6ce34c-6055-4681-8f87-2f4f9f921c06`.

## Key Discovery

**Root Cause of All Previous Failures**: SharePoint Embedded container type management APIs **require certificate-based authentication**, not client secret.

From Microsoft documentation:
> "You will need to use the client credentials grant flow and **request a token with a certificate**"

This explained why:
- Permissions looked correct (Container.Selected was present)
- Admin consent was granted
- Tokens acquired successfully with client secret
- But SharePoint API still returned 401 "invalidToken"

The token itself was being rejected because it wasn't acquired using certificate authentication.

## Solution Applied

### 1. Downloaded Certificate from Azure Key Vault
```bash
az keyvault secret download \
  --vault-name spaarke-spekvcert \
  --name spe-app-cert \
  --file spe-app-cert.pfx \
  --encoding base64
```

**Certificate Details**:
- Thumbprint: `269691A5A60536050FA76C0163BD4A942ECD724D`
- Subject: `CN=SPE Certificate 22Sept2025_1`
- Expires: 09/22/2027

### 2. Imported Certificate to Local Store
```powershell
Import-PfxCertificate \
  -FilePath spe-app-cert.pfx \
  -CertStoreLocation Cert:\CurrentUser\My \
  -Password (ConvertTo-SecureString -String $password -AsPlainText -Force) \
  -Exportable
```

### 3. Created JWT Assertion Signed with Certificate
```powershell
# Header
{
  "alg": "RS256",
  "typ": "JWT",
  "x5t": "<base64url-encoded-thumbprint>"
}

# Payload
{
  "aud": "https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token",
  "iss": "170c98e1-d486-4355-bcbe-170454e0207c",
  "sub": "170c98e1-d486-4355-bcbe-170454e0207c",
  "exp": <timestamp>,
  "nbf": <timestamp>,
  "jti": "<guid>"
}

# Signature: RS256 signed with certificate private key
```

### 4. Acquired Token Using Certificate Authentication
```http
POST https://login.microsoftonline.com/a221a95e-6abc-4434-aecc-e48338a1b2f2/oauth2/v2.0/token

client_id=170c98e1-d486-4355-bcbe-170454e0207c
client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer
client_assertion=<signed-jwt>
scope=https://spaarke.sharepoint.com/.default
grant_type=client_credentials
```

**Result**: ✅ Token acquired successfully

### 5. Registered Applications with Container Type
```http
PUT https://spaarke.sharepoint.com/_api/v2.1/storageContainerTypes/8a6ce34c-6055-4681-8f87-2f4f9f921c06/applicationPermissions
Authorization: Bearer <certificate-based-token>
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

**Result**: ✅ HTTP 200 OK - Registration successful

### 6. Restarted BFF API
```bash
az webapp restart --name spe-api-dev-67e2xz --resource-group spe-infrastructure-westus2
```

**Result**: ✅ Restart completed (clears MSAL token cache)

## Registered Applications

| Application | App ID | Delegated Permissions | App-Only Permissions |
|-------------|--------|----------------------|---------------------|
| **PCF App** (Owning) | 170c98e1-d486-4355-bcbe-170454e0207c | full | full |
| **BFF API** (Guest) | 1e40baad-e065-4aea-a8d4-4b7ab273458c | ReadContent, WriteContent | none |

## Why This Fixes the 403 Error

### Before Registration
```
User → PCF Control → Token A → BFF API
                                  ↓
                              OBO Exchange
                                  ↓
                             Token B (appid: BFF-API)
                                  ↓
                         Microsoft Graph/SharePoint
                                  ↓
                          Check: Is BFF-API registered?
                                  ↓
                              NO → 403 FORBIDDEN
```

### After Registration
```
User → PCF Control → Token A → BFF API
                                  ↓
                              OBO Exchange
                                  ↓
                             Token B (appid: BFF-API)
                                  ↓
                         Microsoft Graph/SharePoint
                                  ↓
                          Check: Is BFF-API registered?
                                  ↓
                    YES (ReadContent, WriteContent) → 200 OK
```

## Files Created

1. **Import-And-Register.ps1** - All-in-one script to download cert and register
2. **Register-BffApi-WithCertificate-Direct.ps1** - Certificate-based registration (no MSAL.PS dependency)
3. **IMPORT-CERTIFICATE-AND-REGISTER.md** - Step-by-step guide
4. **Test-OBO-Upload-After-Registration.md** - Testing instructions

## Testing Required

The 403 Forbidden error should now be resolved. Test with:

```http
PUT https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/containers/{containerId}/files/test.txt
Authorization: Bearer {user-token-from-pcf}
Content-Type: text/plain

Test file content
```

**Expected**: HTTP 200 OK (not 403 Forbidden)

## Lessons Learned

1. **Certificate authentication is mandatory** for SharePoint Embedded container type management APIs
2. **Client secret authentication** is rejected by SharePoint API even if permissions are correct
3. **Error messages were misleading**: "invalidToken" didn't explain that the issue was the authentication method, not the token content
4. **Documentation is critical**: The requirement was buried in the docs but not enforced by Azure Portal
5. **Token validation is different**: Just because a token has the right claims doesn't mean SharePoint will accept it

## Journey Timeline

1. Initial 403 error on OBO upload
2. Hypothesis: BFF API not registered with container type
3. Discovery: PCF app IS the container type owner
4. Attempted registration with client secret → 401 "invalidToken"
5. Admin consent granted → Still 401 "invalidToken"
6. Token analysis showed correct claims → Still 401 "invalidToken"
7. **Breakthrough**: Found documentation requiring certificate authentication
8. Implemented certificate-based authentication → ✅ SUCCESS

## Critical Files and Scripts

### Production Scripts
- **[Import-And-Register.ps1](Import-And-Register.ps1)** - Primary script for future registrations
- **[Register-BffApi-WithCertificate-Direct.ps1](Register-BffApi-WithCertificate-Direct.ps1)** - Core registration logic

### Documentation
- **[IMPORT-CERTIFICATE-AND-REGISTER.md](IMPORT-CERTIFICATE-AND-REGISTER.md)** - Complete guide
- **[Test-OBO-Upload-After-Registration.md](Test-OBO-Upload-After-Registration.md)** - Testing procedures
- **[docs/KM-SPE-WEB-APPLICATION-CONTAINER-TYPE-REGISTRATION.md](../docs/KM-SPE-WEB-APPLICATION-CONTAINER-TYPE-REGISTRATION.md)** - Microsoft documentation

### Obsolete Scripts (Client Secret - Won't Work)
- ~~Register-BffApiWithContainerType.ps1~~ (uses client secret)
- ~~Test-SharePointToken.ps1~~ (uses client secret)
- ~~Debug-RegistrationFailure.ps1~~ (diagnostic only)

## Environment Details

- **Tenant ID**: a221a95e-6abc-4434-aecc-e48338a1b2f2
- **SharePoint Domain**: spaarke.sharepoint.com
- **Container Type ID**: 8a6ce34c-6055-4681-8f87-2f4f9f921c06
- **Container Type Name**: Spaarke PAYGO 1
- **BFF API URL**: https://spe-api-dev-67e2xz.azurewebsites.net
- **Resource Group**: spe-infrastructure-westus2

## Next Steps

1. ✅ Registration complete
2. ✅ BFF API restarted
3. ⏳ **TEST OBO UPLOAD** - Verify 403 is resolved
4. ⏳ Remove temporary debug logging from GraphClientFactory.cs (if present)
5. ⏳ Update documentation with certificate authentication requirement
6. ⏳ Consider automation for certificate renewal (expires 2027-09-22)

## Success Metrics

- ✅ Certificate downloaded and imported
- ✅ JWT assertion created and signed
- ✅ Token acquired with certificate authentication
- ✅ Registration API returned HTTP 200 OK
- ✅ BFF API restarted successfully
- ⏳ OBO upload returns HTTP 200 OK (pending user test)

---

**Status**: READY FOR TESTING

The technical implementation is complete. The 403 Forbidden error should be resolved. Please test the OBO upload endpoint with your PCF control to confirm.
