# Certificate-Based Authentication for Power Platform JavaScript

**Created:** 2025-09-30
**Purpose:** Document certificate authentication approach for Task 3.2 JavaScript → API calls

---

## 📋 Certificate Information

**Certificate Details:**
- **Description:** SPECertificate_22Sept2025_1
- **Thumbprint:** `269691A5A60536050FA76C0163BD4A942ECD724D`
- **Certificate ID:** `d49a1e6b-a45f-47e2-8532-e8f0791f5273`
- **Expiry:** September 22, 2025
- **App Registration:** Spaarke DSM-SPE Dev 2 (170c98e1-d486-4355-bcbe-170454e0207c)

---

## 🔐 Authentication Flow

### Power Platform JavaScript → BFF API

```
┌─────────────────┐
│  Power Platform │
│  Model-Driven   │
│      App        │
└────────┬────────┘
         │
         │ 1. User opens document form
         │
         ▼
┌─────────────────┐
│   JavaScript    │
│  (sprk_*.js)    │
└────────┬────────┘
         │
         │ 2. Get access token using
         │    Xrm.WebApi.online.execute()
         │    OR certificate-based token
         │
         ▼
┌─────────────────┐
│  Entra ID/      │
│  Azure AD       │
└────────┬────────┘
         │
         │ 3. Return bearer token
         │
         ▼
┌─────────────────┐
│   JavaScript    │
│   Adds token    │
│   to header     │
└────────┬────────┘
         │
         │ 4. Call BFF API with
         │    Authorization: Bearer <token>
         │
         ▼
┌─────────────────┐
│    BFF API      │
│ (Spe.Bff.Api)   │
└─────────────────┘
```

---

## 🎯 Recommended Approach for Task 3.2

### Option 1: Dataverse User Context (Recommended for Sprint 2)

**Method:** Use the logged-in user's token from Power Platform

**Advantages:**
- ✅ No certificate handling in JavaScript
- ✅ Uses user's existing authentication
- ✅ Inherits user's permissions
- ✅ Simpler implementation

**Implementation:**

```javascript
// Get token for current user context
async function getAccessToken() {
    // Power Platform provides the user's context automatically
    // Use Dataverse Web API to make authenticated calls

    // For external API calls, use the user's token
    const userToken = await parent.Xrm.Utility.getGlobalContext()
        .userSettings.userId; // This gets user context

    // Alternative: Use fetch with credentials
    const response = await fetch(apiUrl, {
        method: 'GET',
        credentials: 'include', // Include authentication cookies
        headers: {
            'Accept': 'application/json'
        }
    });
}
```

**Configuration Needed:**
- BFF API must accept tokens issued to users (not just app-to-app)
- App Registration needs delegated permissions

---

### Option 2: Service Principal with Certificate (Advanced)

**Method:** JavaScript acquires token using certificate credentials

**⚠️ Security Risk:** Certificates should NOT be stored in JavaScript/browser

**Better Alternative:** Use Azure Function as proxy

```
JavaScript → Azure Function → BFF API
            (uses certificate)
```

**Why This is Better:**
- ✅ Certificate stays server-side
- ✅ No exposure in browser/JavaScript
- ✅ Proper secret management
- ⚠️ Adds complexity

---

## 🔧 Recommended Implementation for Sprint 2

### Approach: On-Behalf-Of (OBO) Flow

This is the **standard pattern** for Power Platform → Custom API integration.

**Flow:**
1. User authenticates to Power Platform (Dataverse)
2. JavaScript calls BFF API with user's token
3. BFF API validates user token
4. BFF API uses OBO flow to get Graph API token if needed

**Benefits:**
- ✅ Secure - no secrets in JavaScript
- ✅ User-based permissions
- ✅ Audit trail with user identity
- ✅ Industry standard pattern

**BFF API Configuration:**

The API needs to:
1. Accept user tokens (delegated permissions)
2. Validate audience and issuer
3. Use OBO flow for downstream services

---

## 📝 Certificate Storage Guidelines

### ❌ DO NOT Store Certificates In:
- JavaScript files
- Web resources
- Browser localStorage
- Client-side code
- Source control (except encrypted in Key Vault)

### ✅ DO Store Certificates In:
- **Azure Key Vault** (Primary)
- **App Registration** (Certificate already uploaded ✅)
- **Azure App Service Certificate Store**
- **Managed Identity** (if applicable)

### Your Current Setup:

**Already Configured:** ✅
- Certificate uploaded to App Registration
- Certificate ID: d49a1e6b-a45f-47e2-8532-e8f0791f5273
- Thumbprint: 269691A5A60536050FA76C0163BD4A942ECD724D

**For PEM/PFX Files:**
- **DO NOT** commit to source control
- **DO** store in Azure Key Vault as certificate
- **DO** reference from Key Vault in application

---

## 🔑 Key Vault Storage (If Needed)

If you need to store the certificate files for deployment automation:

### Directory Structure:
```
C:\code_files\spaarke\
├── .gitignore                     # Add *.pfx, *.pem, *.key
├── docs\
│   └── configuration\
│       └── Certificate-Authentication-JavaScript.md  # This file
└── infrastructure\
    └── certificates\
        └── .gitkeep               # Keep folder, ignore contents
```

### .gitignore Addition:
```gitignore
# Certificates - Never commit these
*.pfx
*.pem
*.key
*.cer
*.crt
infrastructure/certificates/*
!infrastructure/certificates/.gitkeep
```

### Upload to Key Vault:
```bash
# Upload PFX certificate to Key Vault
az keyvault certificate import \
  --vault-name spaarke-spevcert \
  --name SPECertificate-22Sept2025 \
  --file path/to/certificate.pfx \
  --password "<pfx-password>"

# Verify upload
az keyvault certificate show \
  --vault-name spaarke-spevcert \
  --name SPECertificate-22Sept2025
```

---

## 🧪 Testing Authentication

### Test 1: User Token from Power Platform

```javascript
// In browser console (F12) on Power Platform page
async function testAuth() {
    try {
        // Get API URL from environment config
        const apiUrl = 'https://spaarke-bff-dev.azurewebsites.net/ping';

        // Make authenticated request
        const response = await fetch(apiUrl, {
            method: 'GET',
            credentials: 'include',
            headers: {
                'Accept': 'application/json'
            }
        });

        if (response.ok) {
            const data = await response.json();
            console.log('✅ Authentication successful:', data);
        } else {
            console.error('❌ Authentication failed:', response.status);
        }
    } catch (error) {
        console.error('❌ Error:', error);
    }
}

testAuth();
```

### Test 2: CORS + Auth

```javascript
// Test CORS and authentication together
async function testCorsAuth() {
    const apiUrl = 'https://spaarke-bff-dev.azurewebsites.net/api/v1/documents';

    try {
        const response = await fetch(apiUrl, {
            method: 'GET',
            credentials: 'include',
            headers: {
                'Accept': 'application/json',
                'Content-Type': 'application/json'
            }
        });

        console.log('Response status:', response.status);
        console.log('CORS headers:', {
            'access-control-allow-origin': response.headers.get('access-control-allow-origin'),
            'access-control-allow-credentials': response.headers.get('access-control-allow-credentials')
        });

        if (response.ok) {
            const data = await response.json();
            console.log('✅ Success:', data);
        }
    } catch (error) {
        console.error('❌ Error:', error);
    }
}

testCorsAuth();
```

---

## 📋 Task 3.2 Implementation Checklist

### Authentication Setup

- [ ] **Verify App Registration permissions**
  - Delegated permissions for user context
  - API permissions configured
  - Admin consent granted

- [ ] **Configure BFF API for user tokens**
  - Accept tokens issued to users
  - Validate audience and issuer
  - Enable OBO flow if needed for Graph API

- [ ] **Test authentication flow**
  - User can log in to Power Platform
  - JavaScript can call API with user context
  - API validates user token correctly

### Certificate Management (If Needed)

- [ ] **Store certificate in Key Vault** (not in code)
- [ ] **Update .gitignore** to exclude certificate files
- [ ] **Document certificate rotation process**
- [ ] **Set up expiry alerts** (expires Sept 2025)

### CORS Configuration

- [x] **CORS configured** in appsettings.Development.json ✅
- [ ] **Test CORS** from Power Platform domain
- [ ] **Verify preflight** requests succeed
- [ ] **Check CORS headers** in browser DevTools

---

## 🚨 Security Reminders

1. **Never commit certificates** to source control
2. **Use Key Vault** for certificate storage
3. **Rotate certificates** before expiry (Sept 2025)
4. **Use user context** when possible (not service principal)
5. **Audit API access** with user identity
6. **Test with least-privilege** user accounts

---

## 📖 Additional Resources

### Microsoft Documentation
- [Use authentication with API calls](https://docs.microsoft.com/power-apps/developer/model-driven-apps/clientapi/authentication)
- [On-Behalf-Of flow](https://docs.microsoft.com/azure/active-directory/develop/v2-oauth2-on-behalf-of-flow)
- [Certificate credentials](https://docs.microsoft.com/azure/active-directory/develop/active-directory-certificate-credentials)

### Task References
- Task 3.2: JavaScript File Management Integration
- CORS Configuration Strategy
- App Registration: Spaarke DSM-SPE Dev 2

---

## ✅ Recommendation for Task 3.2

**Use User Context Authentication:**
- Simpler implementation
- More secure (no secrets in JavaScript)
- Better audit trail
- Industry standard for Power Platform integrations

**Do NOT use certificate in JavaScript:**
- Security risk
- Not necessary for user-initiated actions
- Certificate best used for server-to-server

**Certificate Purpose:**
- Server-side authentication (BFF API → Graph API)
- Service-to-service calls
- Background jobs without user context

The certificate is already correctly configured in the App Registration and will be used by the BFF API for Graph API calls, not by JavaScript.
