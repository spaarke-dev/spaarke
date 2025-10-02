# Task 1.2: Configuration & Deployment Setup

**Priority:** CRITICAL (Sprint 3, Phase 1)
**Estimated Effort:** 2-3 days
**Status:** 🔴 BLOCKS DEPLOYMENT
**Dependencies:** None

---

## Context & Problem Statement

The application requires numerous configuration values that are **not documented** and **not provided** in `appsettings.json`:

1. **GraphClientFactory demands missing config**: `UAMI_CLIENT_ID`, `TENANT_ID`, `API_APP_ID` - errors if missing
2. **No local development defaults**: `appsettings.Development.json` doesn't exist or is incomplete
3. **No deployment documentation**: Required app registrations, UAMI setup, and secrets not documented
4. **No startup validation**: Application fails at runtime when config is missing (not at startup)
5. **Environment bootstrap is brittle**: Deploying to new environment requires trial-and-error

This blocks all deployment scenarios and makes local development difficult.

---

## Goals & Outcomes

### Primary Goals
1. Document all required configuration values and their sources
2. Create complete `appsettings.Development.json` with local defaults
3. Add startup validation for required configuration
4. Document app registration and UAMI setup process
5. Create deployment checklists for each environment

### Success Criteria
- [ ] Application starts successfully in local dev (with defaults)
- [ ] Startup validation fails fast with clear errors for missing config
- [ ] Deployment guide documents all Azure resources needed
- [ ] `appsettings.Development.json` has working defaults for local dev
- [ ] Configuration schema is documented
- [ ] Secrets are properly managed (Key Vault for prod, user-secrets for dev)

### Non-Goals
- Automated infrastructure provisioning (IaC/Terraform - Sprint 4+)
- Configuration management tools (Sprint 4+)
- Multi-region deployment (Sprint 4+)

---

## Architecture & Design

### Configuration Hierarchy
```
┌─────────────────────────────────────┐
│ appsettings.json                    │ ← Base settings (committed)
└──────────────┬──────────────────────┘
               │
               v
┌─────────────────────────────────────┐
│ appsettings.Development.json        │ ← Local dev (committed, no secrets)
└──────────────┬──────────────────────┘
               │
               v
┌─────────────────────────────────────┐
│ User Secrets (dev)                  │ ← Dev secrets (not committed)
└──────────────┬──────────────────────┘
               │
               v
┌─────────────────────────────────────┐
│ Environment Variables               │ ← CI/CD variables
└──────────────┬──────────────────────┘
               │
               v
┌─────────────────────────────────────┐
│ Azure Key Vault (prod/staging)      │ ← Production secrets
└─────────────────────────────────────┘
```

### Required Configuration Sections

#### 1. Graph Client Configuration
```json
{
  "Graph": {
    "TenantId": "your-tenant-id",
    "ClientId": "your-app-client-id",
    "ClientSecret": "managed-via-keyvault",  // Production
    "Scopes": [ "https://graph.microsoft.com/.default" ],
    "ManagedIdentity": {
      "Enabled": true,  // Production
      "ClientId": "your-uami-client-id"
    }
  }
}
```

#### 2. Dataverse Configuration
```json
{
  "Dataverse": {
    "EnvironmentUrl": "https://your-env.crm.dynamics.com",
    "ClientId": "your-dataverse-app-id",
    "ClientSecret": "managed-via-keyvault",  // Production
    "TenantId": "your-tenant-id"
  }
}
```

#### 3. Service Bus Configuration
```json
{
  "ServiceBus": {
    "ConnectionString": "managed-via-keyvault",  // Production
    "QueueName": "sdap-jobs",
    "MaxConcurrentCalls": 5,
    "MaxAutoLockRenewalDuration": "00:05:00"
  }
}
```

#### 4. Redis Cache Configuration
```json
{
  "Redis": {
    "ConnectionString": "managed-via-keyvault",  // Production
    "InstanceName": "sdap:",
    "Enabled": true  // false for dev (uses in-memory)
  }
}
```

#### 5. Authentication Configuration
```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "your-tenant-id",
    "ClientId": "your-api-app-id",
    "Audience": "api://your-api-app-id"
  }
}
```

---

## Implementation Steps

### Step 1: Create Configuration Models with Validation

**New File:** `src/api/Spe.Bff.Api/Configuration/GraphOptions.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace Spe.Bff.Api.Configuration;

public class GraphOptions
{
    public const string SectionName = "Graph";

    [Required(ErrorMessage = "Graph:TenantId is required")]
    public string TenantId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Graph:ClientId is required")]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Client secret for app-only authentication.
    /// Only required when ManagedIdentity.Enabled is false.
    /// </summary>
    public string? ClientSecret { get; set; }

    [Required(ErrorMessage = "Graph:Scopes is required")]
    [MinLength(1, ErrorMessage = "At least one scope is required")]
    public string[] Scopes { get; set; } = Array.Empty<string>();

    public ManagedIdentityOptions ManagedIdentity { get; set; } = new();
}

public class ManagedIdentityOptions
{
    /// <summary>
    /// Enable User-Assigned Managed Identity for production.
    /// Falls back to ClientSecret when false.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// UAMI Client ID. Required when Enabled is true.
    /// </summary>
    public string? ClientId { get; set; }
}
```

**New File:** `src/api/Spe.Bff.Api/Configuration/DataverseOptions.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace Spe.Bff.Api.Configuration;

public class DataverseOptions
{
    public const string SectionName = "Dataverse";

    [Required(ErrorMessage = "Dataverse:EnvironmentUrl is required")]
    [Url(ErrorMessage = "Dataverse:EnvironmentUrl must be a valid URL")]
    public string EnvironmentUrl { get; set; } = string.Empty;

    [Required(ErrorMessage = "Dataverse:ClientId is required")]
    public string ClientId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Dataverse:ClientSecret is required")]
    public string ClientSecret { get; set; } = string.Empty;

    [Required(ErrorMessage = "Dataverse:TenantId is required")]
    public string TenantId { get; set; } = string.Empty;
}
```

**New File:** `src/api/Spe.Bff.Api/Configuration/ServiceBusOptions.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace Spe.Bff.Api.Configuration;

public class ServiceBusOptions
{
    public const string SectionName = "ServiceBus";

    [Required(ErrorMessage = "ServiceBus:ConnectionString is required")]
    public string ConnectionString { get; set; } = string.Empty;

    [Required(ErrorMessage = "ServiceBus:QueueName is required")]
    public string QueueName { get; set; } = string.Empty;

    [Range(1, 100, ErrorMessage = "ServiceBus:MaxConcurrentCalls must be between 1 and 100")]
    public int MaxConcurrentCalls { get; set; } = 5;

    public TimeSpan MaxAutoLockRenewalDuration { get; set; } = TimeSpan.FromMinutes(5);
}
```

**New File:** `src/api/Spe.Bff.Api/Configuration/RedisOptions.cs`

```csharp
using System.ComponentModel.DataAnnotations;

namespace Spe.Bff.Api.Configuration;

public class RedisOptions
{
    public const string SectionName = "Redis";

    /// <summary>
    /// Enable Redis caching. When false, uses in-memory cache.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Redis connection string. Required when Enabled is true.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Redis instance name prefix.
    /// </summary>
    public string InstanceName { get; set; } = "sdap:";
}
```

---

### Step 2: Add Startup Validation

**File:** `src/api/Spe.Bff.Api/Program.cs` (add after builder.Services configuration)

```csharp
// ---- Configuration Validation ----

// Register and validate configuration options
builder.Services
    .AddOptions<GraphOptions>()
    .Bind(builder.Configuration.GetSection(GraphOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<DataverseOptions>()
    .Bind(builder.Configuration.GetSection(DataverseOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<ServiceBusOptions>()
    .Bind(builder.Configuration.GetSection(ServiceBusOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddOptions<RedisOptions>()
    .Bind(builder.Configuration.GetSection(RedisOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Custom validation for conditional requirements
builder.Services.AddSingleton<IValidateOptions<GraphOptions>, GraphOptionsValidator>();

// Add startup health check to validate configuration
builder.Services.AddHostedService<StartupValidationService>();
```

**New File:** `src/api/Spe.Bff.Api/Configuration/GraphOptionsValidator.cs`

```csharp
using Microsoft.Extensions.Options;

namespace Spe.Bff.Api.Configuration;

public class GraphOptionsValidator : IValidateOptions<GraphOptions>
{
    public ValidateOptionsResult Validate(string? name, GraphOptions options)
    {
        var errors = new List<string>();

        // If ManagedIdentity is enabled, ClientId is required
        if (options.ManagedIdentity.Enabled && string.IsNullOrWhiteSpace(options.ManagedIdentity.ClientId))
        {
            errors.Add("Graph:ManagedIdentity:ClientId is required when ManagedIdentity is enabled");
        }

        // If ManagedIdentity is disabled, ClientSecret is required
        if (!options.ManagedIdentity.Enabled && string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            errors.Add("Graph:ClientSecret is required when ManagedIdentity is disabled");
        }

        return errors.Any()
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }
}
```

**New File:** `src/api/Spe.Bff.Api/Infrastructure/Startup/StartupValidationService.cs`

```csharp
using Microsoft.Extensions.Options;
using Spe.Bff.Api.Configuration;

namespace Spe.Bff.Api.Infrastructure.Startup;

/// <summary>
/// Validates configuration and dependencies at startup.
/// Fails fast if critical configuration is missing.
/// </summary>
public class StartupValidationService : IHostedService
{
    private readonly ILogger<StartupValidationService> _logger;
    private readonly IOptions<GraphOptions> _graphOptions;
    private readonly IOptions<DataverseOptions> _dataverseOptions;
    private readonly IOptions<ServiceBusOptions> _serviceBusOptions;
    private readonly IOptions<RedisOptions> _redisOptions;

    public StartupValidationService(
        ILogger<StartupValidationService> logger,
        IOptions<GraphOptions> graphOptions,
        IOptions<DataverseOptions> dataverseOptions,
        IOptions<ServiceBusOptions> serviceBusOptions,
        IOptions<RedisOptions> redisOptions)
    {
        _logger = logger;
        _graphOptions = graphOptions;
        _dataverseOptions = dataverseOptions;
        _serviceBusOptions = serviceBusOptions;
        _redisOptions = redisOptions;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting configuration validation...");

        try
        {
            // Access .Value to trigger validation
            _ = _graphOptions.Value;
            _ = _dataverseOptions.Value;
            _ = _serviceBusOptions.Value;
            _ = _redisOptions.Value;

            _logger.LogInformation("Configuration validation successful");
            LogConfigurationSummary();

            return Task.CompletedTask;
        }
        catch (OptionsValidationException ex)
        {
            _logger.LogCritical(ex, "Configuration validation failed. Application cannot start.");
            _logger.LogCritical("Validation errors:");
            foreach (var failure in ex.Failures)
            {
                _logger.LogCritical("  - {Error}", failure);
            }

            // Fail fast - stop application startup
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private void LogConfigurationSummary()
    {
        var graph = _graphOptions.Value;
        var dataverse = _dataverseOptions.Value;
        var serviceBus = _serviceBusOptions.Value;
        var redis = _redisOptions.Value;

        _logger.LogInformation("Configuration Summary:");
        _logger.LogInformation("  Graph: TenantId={TenantId}, ManagedIdentity={Enabled}",
            graph.TenantId, graph.ManagedIdentity.Enabled);
        _logger.LogInformation("  Dataverse: Environment={Url}",
            dataverse.EnvironmentUrl);
        _logger.LogInformation("  ServiceBus: Queue={QueueName}, MaxConcurrency={MaxConcurrency}",
            serviceBus.QueueName, serviceBus.MaxConcurrentCalls);
        _logger.LogInformation("  Redis: Enabled={Enabled}",
            redis.Enabled);
    }
}
```

---

### Step 3: Create appsettings.Development.json

**File:** `src/api/Spe.Bff.Api/appsettings.Development.json`

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.Graph": "Information",
      "Spe.Bff.Api": "Debug"
    }
  },

  "Graph": {
    "TenantId": "common",
    "ClientId": "your-dev-app-client-id",
    "ClientSecret": "use-user-secrets-or-env-var",
    "Scopes": [ "https://graph.microsoft.com/.default" ],
    "ManagedIdentity": {
      "Enabled": false
    }
  },

  "Dataverse": {
    "EnvironmentUrl": "https://your-dev-env.crm.dynamics.com",
    "ClientId": "your-dev-dataverse-app-id",
    "ClientSecret": "use-user-secrets-or-env-var",
    "TenantId": "your-tenant-id"
  },

  "ServiceBus": {
    "ConnectionString": "use-user-secrets-or-env-var",
    "QueueName": "sdap-jobs-dev",
    "MaxConcurrentCalls": 2,
    "MaxAutoLockRenewalDuration": "00:05:00"
  },

  "Redis": {
    "Enabled": false,
    "InstanceName": "sdap-dev:"
  },

  "Cors": {
    "AllowedOrigins": "http://localhost:3000,http://localhost:5173"
  },

  "Authorization": {
    "Enabled": false
  }
}
```

---

### Step 4: Setup User Secrets for Local Development

**File:** `src/api/Spe.Bff.Api/README-Secrets.md` (new documentation)

```markdown
# Local Development Secrets Setup

## Initialize User Secrets

Run from the `src/api/Spe.Bff.Api` directory:

```bash
dotnet user-secrets init
```

## Set Required Secrets

### Graph API
```bash
dotnet user-secrets set "Graph:ClientSecret" "your-app-client-secret"
```

### Dataverse
```bash
dotnet user-secrets set "Dataverse:ClientSecret" "your-dataverse-client-secret"
```

### Service Bus
```bash
dotnet user-secrets set "ServiceBus:ConnectionString" "Endpoint=sb://your-servicebus.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=your-key"
```

### (Optional) Redis - Only if Redis:Enabled = true
```bash
dotnet user-secrets set "Redis:ConnectionString" "localhost:6379"
```

## Verify Secrets

```bash
dotnet user-secrets list
```

## Alternative: Environment Variables

You can also set these via environment variables (useful for Docker):

```bash
export Graph__ClientSecret="your-secret"
export Dataverse__ClientSecret="your-secret"
export ServiceBus__ConnectionString="your-connection-string"
```

Note: Use double underscores `__` for nested configuration in env vars.
```

---

### Step 5: Create Deployment Documentation

**File:** `src/api/Spe.Bff.Api/DEPLOYMENT.md`

```markdown
# Deployment Guide - SDAP BFF API

## Prerequisites

### 1. Azure Resources Required

- **Resource Group**: `rg-sdap-{environment}`
- **App Service**: `app-sdap-bff-{environment}`
- **Service Bus Namespace**: `sb-sdap-{environment}`
- **Service Bus Queue**: `sdap-jobs`
- **Azure Cache for Redis**: `redis-sdap-{environment}` (Standard tier recommended)
- **User-Assigned Managed Identity**: `mi-sdap-{environment}`
- **Key Vault**: `kv-sdap-{environment}`
- **Application Insights**: `ai-sdap-{environment}`

### 2. App Registrations Required

#### A. BFF API App Registration
- **Name**: `SDAP-BFF-API-{environment}`
- **Redirect URIs**: Not required (backend API)
- **API Permissions**:
  - Microsoft Graph: `Files.Read.All`, `Files.ReadWrite.All` (Application)
  - Dynamics CRM: `user_impersonation` (Delegated)
- **Expose an API**: `api://sdap-bff-api`
- **App Roles**: Define custom roles if needed
- **Secrets**: Create client secret, store in Key Vault

#### B. Dataverse App Registration
- **Name**: `SDAP-Dataverse-Client-{environment}`
- **API Permissions**:
  - Dynamics CRM: `user_impersonation` (Delegated)
- **Secrets**: Create client secret, store in Key Vault

## Step-by-Step Deployment

### Step 1: Create Azure Resources

```bash
# Set variables
ENVIRONMENT="dev"  # or staging, prod
LOCATION="eastus"
RG_NAME="rg-sdap-$ENVIRONMENT"

# Create resource group
az group create --name $RG_NAME --location $LOCATION

# Create Service Bus
az servicebus namespace create --name "sb-sdap-$ENVIRONMENT" --resource-group $RG_NAME --location $LOCATION --sku Standard
az servicebus queue create --name "sdap-jobs" --namespace-name "sb-sdap-$ENVIRONMENT" --resource-group $RG_NAME

# Create Redis
az redis create --name "redis-sdap-$ENVIRONMENT" --resource-group $RG_NAME --location $LOCATION --sku Standard --vm-size C1

# Create User-Assigned Managed Identity
az identity create --name "mi-sdap-$ENVIRONMENT" --resource-group $RG_NAME

# Get Managed Identity details (save these)
MI_CLIENT_ID=$(az identity show --name "mi-sdap-$ENVIRONMENT" --resource-group $RG_NAME --query clientId -o tsv)
MI_PRINCIPAL_ID=$(az identity show --name "mi-sdap-$ENVIRONMENT" --resource-group $RG_NAME --query principalId -o tsv)

# Create Key Vault
az keyvault create --name "kv-sdap-$ENVIRONMENT" --resource-group $RG_NAME --location $LOCATION

# Grant Managed Identity access to Key Vault
az keyvault set-policy --name "kv-sdap-$ENVIRONMENT" --object-id $MI_PRINCIPAL_ID --secret-permissions get list

# Create App Service
az appservice plan create --name "plan-sdap-$ENVIRONMENT" --resource-group $RG_NAME --sku B1 --is-linux
az webapp create --name "app-sdap-bff-$ENVIRONMENT" --resource-group $RG_NAME --plan "plan-sdap-$ENVIRONMENT" --runtime "DOTNETCORE:8.0"

# Assign Managed Identity to App Service
az webapp identity assign --name "app-sdap-bff-$ENVIRONMENT" --resource-group $RG_NAME --identities "/subscriptions/{subscription-id}/resourcegroups/$RG_NAME/providers/Microsoft.ManagedIdentity/userAssignedIdentities/mi-sdap-$ENVIRONMENT"
```

### Step 2: Configure App Registrations

1. **Create BFF API App Registration**:
   - Portal: Azure AD > App Registrations > New Registration
   - Name: `SDAP-BFF-API-{environment}`
   - Create client secret, copy value
   - Configure API permissions (Graph, Dataverse)

2. **Create Dataverse App Registration**:
   - Same process as above
   - Focus on Dataverse permissions only

### Step 3: Store Secrets in Key Vault

```bash
# BFF API secrets
az keyvault secret set --vault-name "kv-sdap-$ENVIRONMENT" --name "Graph-ClientSecret" --value "{your-bff-api-secret}"
az keyvault secret set --vault-name "kv-sdap-$ENVIRONMENT" --name "Dataverse-ClientSecret" --value "{your-dataverse-secret}"

# Service Bus connection string
SB_CONN_STRING=$(az servicebus namespace authorization-rule keys list --resource-group $RG_NAME --namespace-name "sb-sdap-$ENVIRONMENT" --name RootManageSharedAccessKey --query primaryConnectionString -o tsv)
az keyvault secret set --vault-name "kv-sdap-$ENVIRONMENT" --name "ServiceBus-ConnectionString" --value "$SB_CONN_STRING"

# Redis connection string
REDIS_CONN_STRING=$(az redis list-keys --name "redis-sdap-$ENVIRONMENT" --resource-group $RG_NAME --query primaryKey -o tsv)
az keyvault secret set --vault-name "kv-sdap-$ENVIRONMENT" --name "Redis-ConnectionString" --value "redis-sdap-$ENVIRONMENT.redis.cache.windows.net:6380,password=$REDIS_CONN_STRING,ssl=True,abortConnect=False"
```

### Step 4: Configure App Service Settings

```bash
# Set Key Vault reference configuration
az webapp config appsettings set --name "app-sdap-bff-$ENVIRONMENT" --resource-group $RG_NAME --settings \
  "Graph__TenantId={your-tenant-id}" \
  "Graph__ClientId={bff-api-client-id}" \
  "Graph__ClientSecret=@Microsoft.KeyVault(SecretUri=https://kv-sdap-$ENVIRONMENT.vault.azure.net/secrets/Graph-ClientSecret/)" \
  "Graph__ManagedIdentity__Enabled=true" \
  "Graph__ManagedIdentity__ClientId=$MI_CLIENT_ID" \
  "Graph__Scopes__0=https://graph.microsoft.com/.default" \
  "Dataverse__EnvironmentUrl=https://your-env.crm.dynamics.com" \
  "Dataverse__ClientId={dataverse-client-id}" \
  "Dataverse__ClientSecret=@Microsoft.KeyVault(SecretUri=https://kv-sdap-$ENVIRONMENT.vault.azure.net/secrets/Dataverse-ClientSecret/)" \
  "Dataverse__TenantId={your-tenant-id}" \
  "ServiceBus__ConnectionString=@Microsoft.KeyVault(SecretUri=https://kv-sdap-$ENVIRONMENT.vault.azure.net/secrets/ServiceBus-ConnectionString/)" \
  "ServiceBus__QueueName=sdap-jobs" \
  "ServiceBus__MaxConcurrentCalls=5" \
  "Redis__Enabled=true" \
  "Redis__ConnectionString=@Microsoft.KeyVault(SecretUri=https://kv-sdap-$ENVIRONMENT.vault.azure.net/secrets/Redis-ConnectionString/)" \
  "Redis__InstanceName=sdap:" \
  "Authorization__Enabled=true"
```

### Step 5: Grant Graph API Permissions to Managed Identity

```powershell
# PowerShell script to grant Graph permissions to Managed Identity
Connect-MgGraph -Scopes "Application.ReadWrite.All", "AppRoleAssignment.ReadWrite.All"

$graphAppId = "00000003-0000-0000-c000-000000000000"  # Microsoft Graph
$miObjectId = "{managed-identity-principal-id}"

$graphServicePrincipal = Get-MgServicePrincipal -Filter "appId eq '$graphAppId'"

# Grant Files.Read.All
$appRole = $graphServicePrincipal.AppRoles | Where-Object { $_.Value -eq "Files.Read.All" }
New-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $miObjectId -PrincipalId $miObjectId -AppRoleId $appRole.Id -ResourceId $graphServicePrincipal.Id

# Grant Files.ReadWrite.All
$appRole = $graphServicePrincipal.AppRoles | Where-Object { $_.Value -eq "Files.ReadWrite.All" }
New-MgServicePrincipalAppRoleAssignment -ServicePrincipalId $miObjectId -PrincipalId $miObjectId -AppRoleId $appRole.Id -ResourceId $graphServicePrincipal.Id
```

### Step 6: Deploy Application

```bash
# Build and publish
dotnet publish -c Release -o ./publish

# Deploy to App Service
az webapp deployment source config-zip --name "app-sdap-bff-$ENVIRONMENT" --resource-group $RG_NAME --src ./publish.zip
```

### Step 7: Verify Deployment

1. **Check application logs**:
   ```bash
   az webapp log tail --name "app-sdap-bff-$ENVIRONMENT" --resource-group $RG_NAME
   ```

2. **Look for startup validation success**:
   ```
   [Information] Starting configuration validation...
   [Information] Configuration validation successful
   ```

3. **Test health endpoint**:
   ```bash
   curl https://app-sdap-bff-{environment}.azurewebsites.net/health
   ```

## Environment-Specific Configuration

### Development
- Redis: Disabled (in-memory cache)
- Service Bus: Lower concurrency (2)
- Authorization: Can be disabled for testing
- Logging: Debug level

### Staging
- Redis: Enabled
- Service Bus: Medium concurrency (5)
- Authorization: Enabled
- Logging: Information level

### Production
- Redis: Enabled (Standard tier)
- Service Bus: Higher concurrency (10+)
- Authorization: Enabled (strict)
- Logging: Warning level (structured)
- Managed Identity: Required

## Troubleshooting

### Configuration Validation Fails
- Check application logs for specific validation errors
- Verify all Key Vault references are correct
- Ensure Managed Identity has access to Key Vault

### Graph API Errors
- Verify Managed Identity has Graph permissions
- Check Graph client configuration
- Verify UAMI Client ID is correct

### Dataverse Connection Fails
- Verify Dataverse app registration permissions
- Check Dataverse URL is correct
- Verify client secret is valid

## Rollback Plan

1. Stop App Service
2. Revert to previous deployment slot
3. Verify health endpoint
4. Resume traffic
```

---

## AI Coding Prompts

### Prompt 1: Create Configuration Models
```
Create strongly-typed configuration models with validation:

Context:
- Need configuration classes for Graph, Dataverse, ServiceBus, Redis
- Use Data Annotations for validation
- Support conditional validation (e.g., ClientSecret required when ManagedIdentity disabled)

Requirements:
1. Create GraphOptions, DataverseOptions, ServiceBusOptions, RedisOptions
2. Add DataAnnotations validation attributes ([Required], [Url], [Range])
3. Create custom validator for GraphOptions conditional logic
4. Add XML doc comments explaining each property
5. Use const string SectionName for each options class

Code Quality:
- Senior C# developer standards
- Immutable where possible (init-only setters)
- Clear validation error messages
- Follow naming conventions

Files to Create:
- src/api/Spe.Bff.Api/Configuration/GraphOptions.cs
- src/api/Spe.Bff.Api/Configuration/DataverseOptions.cs
- src/api/Spe.Bff.Api/Configuration/ServiceBusOptions.cs
- src/api/Spe.Bff.Api/Configuration/RedisOptions.cs
- src/api/Spe.Bff.Api/Configuration/GraphOptionsValidator.cs
```

### Prompt 2: Add Startup Validation
```
Create startup validation service that fails fast on configuration errors:

Context:
- Need to validate all configuration at startup
- Should log clear error messages
- Should prevent application from starting if config is invalid

Requirements:
1. Create StartupValidationService : IHostedService
2. Inject all IOptions<T> configuration
3. Access .Value in StartAsync to trigger validation
4. Catch OptionsValidationException and log all failures
5. Log configuration summary on success
6. Fail fast (throw) on validation errors

Code Quality:
- Senior C# developer standards
- Comprehensive logging (Information for success, Critical for failures)
- Clear error messages for operators
- Follow IHostedService pattern

Files to Create:
- src/api/Spe.Bff.Api/Infrastructure/Startup/StartupValidationService.cs

Files to Modify:
- src/api/Spe.Bff.Api/Program.cs (register validation)
```

---

## Validation Checklist

Before marking this task complete, verify:

- [ ] All configuration models created with validation
- [ ] StartupValidationService implemented and registered
- [ ] Application fails fast with clear errors when config is missing
- [ ] appsettings.Development.json created with local defaults
- [ ] User secrets documentation created
- [ ] Deployment guide created and tested
- [ ] App registration setup documented
- [ ] Managed Identity setup documented
- [ ] Key Vault integration documented
- [ ] Configuration validated in all environments (dev, staging, prod)

---

## Completion Criteria

Task is complete when:
1. Application starts successfully with valid configuration
2. Application fails fast with clear errors when configuration is invalid
3. Local development works with user secrets or environment variables
4. Deployment guide successfully used to deploy to Azure
5. All configuration properly managed (secrets in Key Vault)
6. Documentation reviewed and validated

**Estimated Completion: 2-3 days**
