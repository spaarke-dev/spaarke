# CORS Configuration Strategy for Multi-Environment Deployment

**Created:** 2025-09-30
**Purpose:** Document CORS configuration approach for DEV → UAT → PROD deployments

---

## 🎯 Requirements

1. **Dynamic CORS origins** per environment (no hardcoded values)
2. **Power Platform domains** must be allowed for JavaScript → API calls
3. **Configuration through Key Vault** or App Settings (not in code)
4. **Environment-specific** values (DEV, UAT, PROD)

---

## 📋 Current Implementation

**File:** `src/api/Spe.Bff.Api/Program.cs` (Lines 60-72)

```csharp
// CORS for SPA
var allowed = builder.Configuration.GetValue<string>("Cors:AllowedOrigins") ?? "";
builder.Services.AddCors(o =>
{
    o.AddPolicy("spa", p =>
    {
        if (!string.IsNullOrWhiteSpace(allowed))
            p.WithOrigins(allowed.Split(',', StringSplitOptions.RemoveEmptyEntries));
        else
            p.AllowAnyOrigin(); // dev fallback
        p.AllowAnyHeader().AllowAnyMethod();
        p.WithExposedHeaders("request-id", "client-request-id", "traceparent");
    });
});
```

**Current Behavior:**
- ✅ Already reads from configuration
- ✅ Supports comma-separated origins
- ✅ Has dev fallback (AllowAnyOrigin)
- ⚠️ Configuration key is not yet populated

---

## 🔧 Recommended Configuration Approach

### Option 1: App Settings (Recommended for Sprint 2)

**Structure:**
```json
// appsettings.Development.json
{
  "Cors": {
    "AllowedOrigins": "https://spaarkedev1.crm.dynamics.com,https://localhost:5173"
  }
}

// appsettings.Production.json
{
  "Cors": {
    "AllowedOrigins": "@Microsoft.KeyVault(SecretUri=https://spaarke-kv-prod.vault.azure.net/secrets/CORS-AllowedOrigins)"
  }
}
```

**Advantages:**
- ✅ Simple to implement
- ✅ Already uses Key Vault for production
- ✅ No code changes required
- ✅ Per-environment configuration files

**Implementation Steps:**

1. **Add to appsettings.Development.json:**
```json
{
  "Cors": {
    "AllowedOrigins": "https://spaarkedev1.crm.dynamics.com"
  }
}
```

2. **Add to appsettings.json (for UAT/PROD via Key Vault):**
```json
{
  "Cors": {
    "AllowedOrigins": "@Microsoft.KeyVault(SecretUri=https://spaarke-spevcert.vault.azure.net/secrets/CORS-AllowedOrigins)"
  }
}
```

3. **Create Key Vault Secrets:**
```bash
# DEV (if needed)
az keyvault secret set --vault-name spaarke-spevcert --name "CORS-AllowedOrigins" --value "https://spaarkedev1.crm.dynamics.com"

# UAT (when ready)
az keyvault secret set --vault-name spaarke-kv-uat --name "CORS-AllowedOrigins" --value "https://spaarkeuat.crm.dynamics.com"

# PROD (when ready)
az keyvault secret set --vault-name spaarke-kv-prod --name "CORS-AllowedOrigins" --value "https://spaarkeprod.crm.dynamics.com"
```

### Option 2: Azure App Service Configuration (Alternative)

**Structure:**
Set in Azure Portal → App Service → Configuration → Application Settings

```
Name: Cors__AllowedOrigins
Value: https://spaarkedev1.crm.dynamics.com
```

**Advantages:**
- ✅ No appsettings.json changes needed
- ✅ Can override per-environment
- ✅ Managed through Azure Portal/ARM templates

**Disadvantages:**
- ⚠️ Requires Azure deployment
- ⚠️ Not visible in code repository

---

## 🌍 Environment-Specific Domains

### Development
```
Dataverse URL: https://spaarkedev1.crm.dynamics.com
API URL: https://spaarke-bff-dev.azurewebsites.net (or localhost:5073 for local)
CORS Origin: https://spaarkedev1.crm.dynamics.com
```

### UAT (When Ready)
```
Dataverse URL: https://spaarkeuat.crm.dynamics.com
API URL: https://spaarke-bff-uat.azurewebsites.net
CORS Origin: https://spaarkeuat.crm.dynamics.com
```

### Production (When Ready)
```
Dataverse URL: https://spaarkeprod.crm.dynamics.com (or custom domain)
API URL: https://spaarke-bff-prod.azurewebsites.net
CORS Origin: https://spaarkeprod.crm.dynamics.com
```

---

## 🔒 Security Considerations

### 1. Never Use Wildcard Origins in Production
```csharp
// ❌ NEVER DO THIS IN PRODUCTION
p.AllowAnyOrigin();

// ✅ ALWAYS USE SPECIFIC ORIGINS
p.WithOrigins("https://spaarkedev1.crm.dynamics.com");
```

### 2. Limit Exposed Headers
Current configuration exposes:
- `request-id`
- `client-request-id`
- `traceparent`

This is ✅ SAFE and appropriate for debugging.

### 3. Use HTTPS Only
All origins should use HTTPS (never HTTP in UAT/PROD).

### 4. Validate Origins in Key Vault
When setting Key Vault secrets, validate URLs:
- Must start with `https://`
- Must be valid Dataverse domain
- No trailing slashes

---

## 📝 Implementation Checklist

### For Sprint 2 (Development)

- [ ] Update `appsettings.Development.json` with DEV Dataverse domain
- [ ] Test JavaScript calls from Power Platform to API
- [ ] Verify CORS headers in browser DevTools
- [ ] Document successful test

### For UAT Deployment

- [ ] Create UAT Key Vault secret for CORS origins
- [ ] Update Key Vault reference in appsettings.json or ARM template
- [ ] Deploy to UAT App Service
- [ ] Test UAT Power Platform → API calls
- [ ] Verify CORS configuration

### For Production Deployment

- [ ] Create PROD Key Vault secret for CORS origins
- [ ] Update Key Vault reference for production
- [ ] Follow change management process
- [ ] Deploy to PROD App Service
- [ ] Test PROD Power Platform → API calls
- [ ] Monitor for CORS errors

---

## 🧪 Testing CORS Configuration

### Test 1: Browser DevTools
1. Open document form in Power Platform
2. Open browser DevTools (F12) → Network tab
3. Trigger JavaScript API call
4. Check response headers for:
   ```
   Access-Control-Allow-Origin: https://spaarkedev1.crm.dynamics.com
   Access-Control-Allow-Methods: GET, POST, PUT, DELETE, OPTIONS
   Access-Control-Allow-Headers: *
   ```

### Test 2: Preflight Request
CORS preflight (OPTIONS) should succeed:
```http
OPTIONS https://spaarke-bff-dev.azurewebsites.net/api/v1/documents HTTP/1.1
Origin: https://spaarkedev1.crm.dynamics.com
Access-Control-Request-Method: POST
Access-Control-Request-Headers: content-type,authorization

Response:
HTTP/1.1 204 No Content
Access-Control-Allow-Origin: https://spaarkedev1.crm.dynamics.com
Access-Control-Allow-Methods: GET, POST, PUT, DELETE, OPTIONS
Access-Control-Allow-Headers: content-type,authorization
```

### Test 3: Actual Request
POST/GET/PUT/DELETE should include CORS headers:
```http
POST https://spaarke-bff-dev.azurewebsites.net/api/v1/documents HTTP/1.1
Origin: https://spaarkedev1.crm.dynamics.com
Authorization: Bearer <token>

Response:
HTTP/1.1 201 Created
Access-Control-Allow-Origin: https://spaarkedev1.crm.dynamics.com
```

---

## 🚨 Troubleshooting

### Issue: "CORS policy: No 'Access-Control-Allow-Origin' header"

**Cause:** Origin not in allowed list

**Fix:**
1. Check `appsettings.Development.json` has correct domain
2. Verify Key Vault secret value (if using)
3. Restart API after config change
4. Check for typos (trailing slashes, http vs https)

### Issue: "CORS policy: Request header field authorization is not allowed"

**Cause:** Authorization header not in AllowedHeaders

**Fix:**
Current code already uses `AllowAnyHeader()` so this shouldn't occur. If it does, explicitly add:
```csharp
p.WithHeaders("authorization", "content-type");
```

### Issue: Preflight request fails with 401 Unauthorized

**Cause:** Authentication middleware runs before CORS

**Fix:**
Ensure CORS middleware is registered BEFORE authentication:
```csharp
app.UseCors("spa");  // ✅ FIRST
app.UseAuthentication();
app.UseAuthorization();
```

Current code (Program.cs:113-116) already has correct order ✅

---

## 📦 Deployment Configuration

### Azure App Service Configuration

**For DEV:**
```bash
az webapp config appsettings set \
  --resource-group spaarke-rg-dev \
  --name spaarke-bff-dev \
  --settings Cors__AllowedOrigins="https://spaarkedev1.crm.dynamics.com"
```

**For UAT:**
```bash
az webapp config appsettings set \
  --resource-group spaarke-rg-uat \
  --name spaarke-bff-uat \
  --settings Cors__AllowedOrigins="@Microsoft.KeyVault(SecretUri=https://spaarke-kv-uat.vault.azure.net/secrets/CORS-AllowedOrigins)"
```

**For PROD:**
```bash
az webapp config appsettings set \
  --resource-group spaarke-rg-prod \
  --name spaarke-bff-prod \
  --settings Cors__AllowedOrigins="@Microsoft.KeyVault(SecretUri=https://spaarke-kv-prod.vault.azure.net/secrets/CORS-AllowedOrigins)"
```

---

## 📋 Summary Recommendation

**For Sprint 2 (Development):**
1. ✅ Update `appsettings.Development.json` with DEV Dataverse URL
2. ✅ No code changes required - already dynamic
3. ✅ Test JavaScript → API calls from Power Platform

**For UAT/PROD:**
1. ✅ Use Key Vault secrets for CORS origins
2. ✅ Reference in `appsettings.json` or App Service settings
3. ✅ Follow deployment pipeline with environment-specific configs

**Configuration is already set up correctly for multi-environment deployment!** 🎉
