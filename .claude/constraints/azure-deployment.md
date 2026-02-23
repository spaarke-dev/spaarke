# Azure Deployment Constraints

> **Domain**: Azure App Service Configuration, Deployment Safety
> **Last Updated**: 2026-02-18

---

## When to Load This File

Load when:
- Deploying to Azure App Service
- Modifying appsettings or configuration
- Troubleshooting 500.30 startup errors
- Setting up new environments

---

## MUST Rules

### Deployment Safety

- **MUST** configure all required settings in Azure App Settings (not in deployed files)
- **MUST** exclude `appsettings.template.json` from publish output (already configured in .csproj)
- **MUST** verify required settings exist before deployment
- **MUST** use Key Vault references for secrets (`@Microsoft.KeyVault(SecretUri=...)`)

### CORS Configuration (CRITICAL)

- **MUST** configure `Cors__AllowedOrigins__N` in Azure App Settings for Production environments
- **MUST** include both `.crm.dynamics.com` and `.api.crm.dynamics.com` origins

---

### Publish & Packaging

- **MUST** publish to `deploy/api-publish/` (outside the project source tree) to avoid recursive artifact nesting
- **MUST** set `stdoutLogEnabled="true"` in the published `web.config` before packaging (dotnet publish resets it to false)
- **MUST** verify zip entry count (~240) and size (~60 MB) before deploying — oversized zips indicate stale publish dirs in source
- **MUST** use `az webapp deploy --type zip` or Kudu zipdeploy API for deployment (ensures atomic replacement)

### Minimal API Endpoints

- **MUST** use MapPost/MapPut/MapPatch for endpoints that accept body parameters (complex types)
- **MUST NOT** use MapGet/MapDelete with handler parameters that would be inferred as body — this compiles but crashes at startup

### BackgroundService Dependencies

- **MUST** use `IServiceProvider` for lazy resolution of external-connecting singletons (Dataverse, OpenAI) in BackgroundService constructors
- **MUST NOT** inject eagerly-connecting singletons directly into BackgroundService constructors — a connection failure kills the host

## MUST NOT Rules

- **MUST NOT** deploy `appsettings.json` files with configuration values
- **MUST NOT** deploy `appsettings.template.json` (contains unresolved placeholders)
- **MUST NOT** hardcode secrets in any deployed files
- **MUST NOT** use `ASPNETCORE_ENVIRONMENT=Production` without CORS settings
- **MUST NOT** publish to a directory inside the project source tree (causes recursive nesting in subsequent publishes)

---

## Required Azure App Settings

These settings MUST exist for the app to start in Production:

### Core Settings

| Setting | Example Value | Notes |
|---------|---------------|-------|
| `ASPNETCORE_ENVIRONMENT` | `Development` or `Production` | Production requires CORS |
| `TENANT_ID` | `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx` | Azure AD tenant |
| `API_APP_ID` | `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx` | BFF API app registration |

### CORS Settings (Required for Production)

| Setting | Value |
|---------|-------|
| `Cors__AllowedOrigins__0` | `https://{org}.crm.dynamics.com` |
| `Cors__AllowedOrigins__1` | `https://{org}.api.crm.dynamics.com` |

**Dev Environment (`spe-api-dev-67e2xz`):**
```
Cors__AllowedOrigins__0 = https://spaarkedev1.crm.dynamics.com
Cors__AllowedOrigins__1 = https://spaarkedev1.api.crm.dynamics.com
```

### Connection Strings (Key Vault References)

| Setting | Format |
|---------|--------|
| `ConnectionStrings__ServiceBus` | `@Microsoft.KeyVault(SecretUri=https://{vault}.vault.azure.net/secrets/ServiceBus-ConnectionString)` |
| `ConnectionStrings__Redis` | `@Microsoft.KeyVault(SecretUri=https://{vault}.vault.azure.net/secrets/Redis-ConnectionString)` |

### AI Services (Optional)

| Setting | Purpose |
|---------|---------|
| `DocumentIntelligence__Enabled` | Enable/disable AI features |
| `DocumentIntelligence__OpenAiEndpoint` | Azure OpenAI endpoint |
| `DocumentIntelligence__OpenAiKey` | Key Vault reference |

---

## Startup Failure Modes

The app will fail to start (HTTP 500.30) if:

1. **CORS missing in Production**: `Cors:AllowedOrigins` empty when `ASPNETCORE_ENVIRONMENT != Development`
2. **ServiceBus missing**: `ConnectionStrings:ServiceBus` is null or empty
3. **Wildcard CORS**: `Cors:AllowedOrigins` contains `*`
4. **GET endpoint with body parameter**: A Minimal API GET handler accepts a complex type that gets inferred as a body parameter. Compiles but crashes at startup during endpoint metadata build. Fix: use MapPost, or restructure as query parameters.
5. **BackgroundService with eager singleton**: `AddHostedService<T>` resolves constructor deps at `IHost.StartAsync()`. If a dep (e.g., `DataverseServiceClientImpl`) connects eagerly and fails, the host crashes. Fix: inject `IServiceProvider` and resolve lazily in `ExecuteAsync()`.

---

## Deployment Verification Checklist

Before deploying:

- [ ] Azure App Settings include all required CORS origins
- [ ] `ASPNETCORE_ENVIRONMENT` matches target environment
- [ ] Connection strings reference Key Vault (not plain text)
- [ ] Publish output does NOT contain appsettings.json files

After deploying:

- [ ] Health check passes: `GET /healthz` returns 200
- [ ] Ping endpoint works: `GET /ping` returns `pong`
- [ ] Stdout log shows `Configuration validation successful` (not a startup exception)
- [ ] No `500.30` on initial page load in browser

---

## Environment Reference

| Environment | App Service | Dataverse Org | CORS Origins |
|-------------|-------------|---------------|--------------|
| Dev | `spe-api-dev-67e2xz` | `spaarkedev1` | `https://spaarkedev1.crm.dynamics.com`, `https://spaarkedev1.api.crm.dynamics.com` |

---

## Source Code References

- CORS validation: [Program.cs:731-736](src/server/api/Sprk.Bff.Api/Program.cs#L731)
- ServiceBus check: [Program.cs:593-600](src/server/api/Sprk.Bff.Api/Program.cs#L593)
- Publish exclusion: [Sprk.Bff.Api.csproj](src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj)

---

**Lines**: ~100
**Purpose**: Prevent deployment failures from missing Azure configuration
