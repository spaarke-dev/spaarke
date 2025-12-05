```markdown
# Authentication Security Boundaries

> **Source**: AUTHENTICATION-ARCHITECTURE.md
> **Last Updated**: December 4, 2025
> **Applies To**: Security reviews, compliance audits, threat modeling

---

## TL;DR

SDAP has 6 security trust boundaries: Browser↔Dataverse, Dataverse↔PCF, PCF↔BFF, BFF↔Azure AD, BFF↔Graph, BFF↔Dataverse. Each boundary requires specific token validation. User identity crosses 4 boundaries via delegated tokens; app identity used only for Dataverse metadata.

---

## Applies When

- Security/compliance review of authentication architecture
- Threat modeling for new features
- Understanding where tokens are validated
- Debugging "access denied" at specific boundaries
- Evaluating attack surface

---

## Security Zones

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              TRUST ZONES                                    │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌─────────────┐    ┌─────────────┐    ┌─────────────┐    ┌─────────────┐  │
│  │   BROWSER   │    │  DATAVERSE  │    │   AZURE     │    │  MICROSOFT  │  │
│  │   (User)    │    │   (M365)    │    │   (BFF)     │    │   (Graph)   │  │
│  │             │    │             │    │             │    │             │  │
│  │ Untrusted   │    │ Trusted     │    │ Trusted     │    │ Trusted     │  │
│  │ Client      │    │ Platform    │    │ Middleware  │    │ Backend     │  │
│  └─────────────┘    └─────────────┘    └─────────────┘    └─────────────┘  │
│         │                  │                  │                  │          │
│         └──────────────────┴──────────────────┴──────────────────┘          │
│                           Token Chain                                       │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Six Trust Boundaries

### Boundary 1: Browser ↔ Dataverse
| Aspect | Detail |
|--------|--------|
| **Protocol** | HTTPS + Entra ID SSO |
| **Authentication** | Handled by Dataverse platform |
| **Validation** | Microsoft's responsibility |
| **Risk** | Session hijacking, XSS |

### Boundary 2: Dataverse ↔ PCF Control
| Aspect | Detail |
|--------|--------|
| **Protocol** | Iframe isolation |
| **Authentication** | Dataverse context passed to PCF |
| **Validation** | PCF SDK validates context |
| **Risk** | Context spoofing (mitigated by SDK) |

### Boundary 3: PCF ↔ BFF API
| Aspect | Detail |
|--------|--------|
| **Protocol** | HTTPS + Bearer Token |
| **Authentication** | MSAL.js acquires token |
| **Validation** | BFF validates JWT (aud, iss, exp) |
| **Risk** | Token theft, MITM (mitigated by HTTPS) |

### Boundary 4: BFF ↔ Azure AD (OBO)
| Aspect | Detail |
|--------|--------|
| **Protocol** | OAuth 2.0 OBO Flow |
| **Authentication** | Client secret + user assertion |
| **Validation** | Azure AD validates both |
| **Risk** | Secret exposure (mitigated by Key Vault) |

### Boundary 5: BFF ↔ Graph API
| Aspect | Detail |
|--------|--------|
| **Protocol** | HTTPS + Bearer Token (OBO result) |
| **Authentication** | Delegated token (user context) |
| **Validation** | Graph validates token + user permissions |
| **Risk** | Over-scoped permissions |

### Boundary 6: BFF ↔ Dataverse
| Aspect | Detail |
|--------|--------|
| **Protocol** | HTTPS + Client Credentials |
| **Authentication** | App-only (ClientSecret) |
| **Validation** | Dataverse Application User |
| **Risk** | App has elevated permissions |

---

## Permission Model

### Delegated Permissions (User Context Preserved)

```
User → PCF → BFF → Graph
       ↓
       Token carries user's identity
       Graph enforces user's SharePoint permissions
```

**Used For**: File operations (upload, download, list)  
**Why**: User's permissions on SharePoint sites must be enforced

### Application Permissions (No User Context)

```
BFF → Dataverse
↓
App identity only
Dataverse Application User permissions apply
```

**Used For**: Metadata queries, entity operations  
**Why**: No user-specific Dataverse data accessed; simpler auth

---

## Token Validation Requirements

| Boundary | Token Type | Must Validate |
|----------|------------|---------------|
| PCF→BFF | User JWT | `aud`, `iss`, `exp`, signature |
| BFF→Graph | OBO JWT | Azure AD validates |
| BFF→Dataverse | App Token | Dataverse validates |

### BFF JWT Validation (Boundary 3)

```csharp
// Program.cs - JWT Bearer validation
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
        options.Audience = $"api://{apiAppId}";
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true
        };
    });
```

---

## Security Decision Tree

```
Which boundary is failing?
├─ PCF can't get token → Check MSAL config, redirect URIs
├─ BFF returns 401 → Check audience, issuer in JWT
├─ OBO fails (AADSTS*) → See oauth-obo-errors.md
├─ Graph returns 403 → User lacks SharePoint permissions
└─ Dataverse returns 401 → Check Application User exists
```

---

## Threat Mitigations

| Threat | Boundary | Mitigation |
|--------|----------|------------|
| Token theft | 3 (PCF→BFF) | Short-lived tokens (1hr), HTTPS only |
| Secret exposure | 4 (OBO) | Key Vault, no hardcoding |
| Privilege escalation | 5 (Graph) | Delegated perms only, no app perms |
| Lateral movement | 6 (Dataverse) | Scoped Application User role |
| Session hijacking | 1 (Browser) | Dataverse platform handles |

---

## Related Articles

- [sdap-auth-patterns.md](sdap-auth-patterns.md) - Auth flow implementation
- [oauth-obo-errors.md](oauth-obo-errors.md) - OBO error codes
- [auth-azure-resources.md](auth-azure-resources.md) - Infrastructure details

---

*Extracted from AUTHENTICATION-ARCHITECTURE.md security boundaries section*
```
