# Quick Reference: SharePoint Embedded Certificate Registration

## One-Line Command (All Steps)

```powershell
cd c:\code_files\spaarke\scripts
.\Import-And-Register.ps1
```

This will:
1. Download certificate from Azure Key Vault
2. Import to local certificate store
3. Register BFF API with container type using certificate auth
4. Display success confirmation

## Manual Steps (If Needed)

### 1. Download Certificate
```bash
az keyvault secret download \
  --vault-name spaarke-spekvcert \
  --name spe-app-cert \
  --file C:\temp\spe-app-cert.pfx \
  --encoding base64
```

### 2. Get Certificate Password
```bash
az keyvault secret show \
  --vault-name spaarke-spekvcert \
  --name spe-app-cert-pass \
  --query value \
  --output tsv
```

### 3. Import Certificate
```powershell
$password = ConvertTo-SecureString -String "<password>" -AsPlainText -Force
Import-PfxCertificate `
  -FilePath C:\temp\spe-app-cert.pfx `
  -CertStoreLocation Cert:\CurrentUser\My `
  -Password $password `
  -Exportable
```

### 4. Run Registration
```powershell
.\Register-BffApi-WithCertificate-Direct.ps1
```

### 5. Restart BFF API
```bash
az webapp restart \
  --name spe-api-dev-67e2xz \
  --resource-group spe-infrastructure-westus2
```

## Certificate Details

| Property | Value |
|----------|-------|
| Key Vault | spaarke-spekvcert |
| Certificate Name | spe-app-cert |
| Password Secret | spe-app-cert-pass |
| Thumbprint | 269691A5A60536050FA76C0163BD4A942ECD724D |
| Expires | 2027-09-22 |

## Application Details

| App | App ID | Role |
|-----|--------|------|
| PCF App (Spaarke DSM-SPE Dev 2) | 170c98e1-d486-4355-bcbe-170454e0207c | Owning App |
| BFF API (SPE-BFF-API) | 1e40baad-e065-4aea-a8d4-4b7ab273458c | Guest App |

## Container Type Details

| Property | Value |
|----------|-------|
| Container Type ID | 8a6ce34c-6055-4681-8f87-2f4f9f921c06 |
| Name | Spaarke PAYGO 1 |
| Owning App | 170c98e1-d486-4355-bcbe-170454e0207c |

## When to Re-Run Registration

1. **Adding new permissions**: Modify script to include additional permissions
2. **Adding new guest apps**: Add to the `value` array in registration body
3. **Certificate renewal**: Run after updating certificate in Key Vault
4. **New environment**: Run in each new tenant/environment

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Certificate not found | Re-run Import-And-Register.ps1 |
| 401 "invalidToken" | Using client secret instead of certificate |
| 400 "invalidRequest" | Check JSON format, ensure `appOnly: ["none"]` not `[]` |
| 403 after registration | Restart BFF API, wait 1-2 minutes for cache clear |
| 404 "Container type doesn't exist" | Verify container type ID is correct |

## API Endpoint

```
PUT https://spaarke.sharepoint.com/_api/v2.1/storageContainerTypes/{containerTypeId}/applicationPermissions
```

**Authentication**: Certificate-based client credentials (NOT client secret)
**Method**: PUT (not PATCH, not POST)
**Content-Type**: application/json

## Registration Body Format

```json
{
  "value": [
    {
      "appId": "<owning-app-id>",
      "delegated": ["full"],
      "appOnly": ["full"]
    },
    {
      "appId": "<guest-app-id>",
      "delegated": ["ReadContent", "WriteContent"],
      "appOnly": ["none"]
    }
  ]
}
```

**Important**: Use `"appOnly": ["none"]` not `"appOnly": []`

## Available Permissions

| Permission | Description |
|------------|-------------|
| None | No permissions |
| ReadContent | Read files in containers |
| WriteContent | Write files (requires ReadContent) |
| Create | Create containers |
| Delete | Delete containers |
| Read | Read container metadata |
| Write | Update container metadata |
| EnumeratePermissions | List container members |
| AddPermissions | Add members to container |
| UpdatePermissions | Change member roles |
| DeletePermissions | Remove other members |
| DeleteOwnPermissions | Remove own membership |
| ManagePermissions | Full permission management |
| Full | All permissions |

## Certificate Authentication Flow

```
1. Load certificate from store (by thumbprint)
2. Create JWT assertion:
   - Header: { alg: "RS256", typ: "JWT", x5t: "<thumbprint>" }
   - Payload: { aud: "login.microsoftonline.com", iss: "<app>", ... }
   - Sign with certificate private key
3. Exchange JWT for access token:
   POST /oauth2/v2.0/token
   - client_assertion_type: jwt-bearer
   - client_assertion: <signed-jwt>
4. Call SharePoint API with token
```

## Files

| File | Purpose |
|------|---------|
| Import-And-Register.ps1 | All-in-one registration script |
| Register-BffApi-WithCertificate-Direct.ps1 | Core registration logic |
| IMPORT-CERTIFICATE-AND-REGISTER.md | Detailed guide |
| REGISTRATION-SUCCESS-SUMMARY.md | What was done and why |
| Test-OBO-Upload-After-Registration.md | Testing procedures |

## Links

- [Microsoft Docs: Container Type Registration](../docs/KM-SPE-WEB-APPLICATION-CONTAINER-TYPE-REGISTRATION.md)
- [Microsoft Docs: Certificate-Based Auth](https://learn.microsoft.com/entra/identity-platform/v2-oauth2-client-creds-grant-flow#second-case-access-token-request-with-a-certificate)
- [SharePoint Embedded Overview](https://learn.microsoft.com/sharepoint/dev/embedded/overview)
