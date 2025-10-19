# Custom API Proxy Architecture

## Executive Summary

This document defines the architecture for a **reusable Custom API Proxy infrastructure** that enables PCF controls in Power Apps model-driven apps to securely call external APIs using Dataverse's implicit authentication model.

**Key Design Principles:**
- **Separation of Concerns**: Custom API Proxy is a separate, reusable infrastructure component
- **Not Tightly Coupled**: Can proxy to ANY external API, not just Spe.Bff.Api
- **Security First**: Leverages Dataverse authentication + service-to-service patterns
- **Production Ready**: Comprehensive error handling, monitoring, and observability

---

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Component Design](#component-design)
3. [Authentication Flow](#authentication-flow)
4. [Data Model](#data-model)
5. [Implementation Guidance](#implementation-guidance)
6. [Security Considerations](#security-considerations)
7. [Deployment Strategy](#deployment-strategy)
8. [Testing Approach](#testing-approach)
9. [Extensibility](#extensibility)
10. [Monitoring & Observability](#monitoring--observability)

---

## Architecture Overview

### Current Problem

```
┌───────────────────────────────────────────────────────────────┐
│ Power Apps Model-Driven App                                   │
│                                                                 │
│  ┌────────────────────────────────────────┐                   │
│  │ PCF Control (Universal Dataset Grid)   │                   │
│  │                                         │                   │
│  │  ❌ No access to user Azure AD token   │                   │
│  │  ❌ Cannot call external APIs directly │                   │
│  └────────────────────────────────────────┘                   │
│                                                                 │
└───────────────────────────────────────────────────────────────┘
                 │
                 │ BLOCKED - No Authentication
                 ▼
          ┌────────────────┐
          │  Spe.Bff.Api   │  (Requires JWT Bearer token)
          │  External API  │
          └────────────────┘
```

### Solution: Custom API Proxy

```
┌────────────────────────────────────────────────────────────────────┐
│ Power Apps Model-Driven App                                        │
│                                                                      │
│  ┌───────────────────────────────────────────┐                     │
│  │ PCF Control (Universal Dataset Grid)      │                     │
│  │                                            │                     │
│  │  context.webAPI.execute(                  │                     │
│  │    "sprk_ProxyDownloadFile",              │                     │
│  │    { documentId: "123" }                  │                     │
│  │  )                                         │                     │
│  │                                            │                     │
│  │  ✅ Implicit authentication via context   │                     │
│  └───────────────────────────────────────────┘                     │
│                 │                                                    │
│                 │ Authenticated call (Dataverse handles auth)       │
│                 ▼                                                    │
│  ┌───────────────────────────────────────────┐                     │
│  │ Dataverse Environment                      │                     │
│  │                                            │                     │
│  │  ┌──────────────────────────────────────┐ │                     │
│  │  │ Custom API: sprk_ProxyDownloadFile   │ │                     │
│  │  │                                       │ │                     │
│  │  │ Plugin Implementation:                │ │                     │
│  │  │  1. Validate request                  │ │                     │
│  │  │  2. Get external service config       │ │                     │
│  │  │  3. Acquire service credential        │ │                     │
│  │  │  4. Call external API                 │ │                     │
│  │  │  5. Return response                   │ │                     │
│  │  └──────────────────────────────────────┘ │                     │
│  │                                            │                     │
│  └───────────────────────────────────────────┘                     │
└────────────────────────────────────────────────────────────────────┘
                 │
                 │ Service-to-service authentication
                 │ (Managed Identity or Client Credentials)
                 ▼
          ┌────────────────┐
          │  Spe.Bff.Api   │
          │  External API  │
          └────────────────┘
```

### Key Benefits

1. **Security**: Dataverse handles authentication implicitly - PCF control never sees tokens
2. **Reusability**: Same infrastructure can proxy to multiple external services
3. **Maintainability**: External service configurations managed in Dataverse, not code
4. **Auditability**: All proxy operations logged in Dataverse
5. **Production Ready**: Built-in error handling, retry logic, monitoring

---

## Component Design

### 1. Dataverse Custom API Layer

The Custom API Proxy is implemented as a set of Dataverse Custom APIs, each representing a specific operation.

#### Base Custom API Structure

```
Custom API: sprk_ProxyExecute (Base/Generic Proxy)
├── Input Parameters
│   ├── ServiceName (string, required) - e.g., "SpeBffApi"
│   ├── Operation (string, required) - e.g., "DownloadFile"
│   ├── RequestPayload (string, required) - JSON payload
│   └── Options (string, optional) - Additional options
│
├── Output Parameters
│   ├── StatusCode (int)
│   ├── ResponsePayload (string) - JSON response
│   └── ErrorMessage (string, optional)
│
└── Plugin Implementation
    └── ProxyExecutePlugin.cs
```

#### Operation-Specific Custom APIs

For better type safety and clarity, we also create operation-specific Custom APIs:

```
Custom API: sprk_ProxyDownloadFile
├── Input Parameters
│   ├── DocumentId (string, required)
│   ├── FileId (string, optional)
│   └── DownloadUrl (string, optional)
│
├── Output Parameters
│   ├── FileContent (string) - Base64 encoded
│   ├── FileName (string)
│   ├── ContentType (string)
│   └── StatusCode (int)
│
└── Plugin Implementation
    └── ProxyDownloadFilePlugin.cs (inherits from BaseProxyPlugin)

Custom API: sprk_ProxyDeleteFile
├── Input Parameters
│   ├── DocumentId (string, required)
│   └── FileId (string, required)
│
├── Output Parameters
│   ├── Success (bool)
│   ├── StatusCode (int)
│   └── ErrorMessage (string, optional)
│
└── Plugin Implementation
    └── ProxyDeleteFilePlugin.cs (inherits from BaseProxyPlugin)

Custom API: sprk_ProxyReplaceFile
├── Input Parameters
│   ├── DocumentId (string, required)
│   ├── FileId (string, required)
│   ├── FileContent (string) - Base64 encoded
│   ├── FileName (string)
│   └── ContentType (string)
│
├── Output Parameters
│   ├── Success (bool)
│   ├── NewFileId (string, optional)
│   ├── StatusCode (int)
│   └── ErrorMessage (string, optional)
│
└── Plugin Implementation
    └── ProxyReplaceFilePlugin.cs (inherits from BaseProxyPlugin)

Custom API: sprk_ProxyUploadFile
├── Input Parameters
│   ├── DocumentId (string, required)
│   ├── FileContent (string) - Base64 encoded
│   ├── FileName (string)
│   └── ContentType (string)
│
├── Output Parameters
│   ├── FileId (string)
│   ├── DownloadUrl (string)
│   ├── StatusCode (int)
│   └── ErrorMessage (string, optional)
│
└── Plugin Implementation
    └── ProxyUploadFilePlugin.cs (inherits from BaseProxyPlugin)
```

### 2. Plugin Class Hierarchy

```csharp
// Base plugin with common functionality
public abstract class BaseProxyPlugin : IPlugin
{
    protected ITracingService TracingService { get; private set; }
    protected IOrganizationService OrganizationService { get; private set; }

    public void Execute(IServiceProvider serviceProvider)
    {
        // Common setup
        TracingService = GetTracingService(serviceProvider);
        OrganizationService = GetOrganizationService(serviceProvider);

        try
        {
            // Common pre-processing
            ValidateRequest();
            LogRequest();

            // Call derived class implementation
            ExecuteProxy(serviceProvider);

            // Common post-processing
            LogResponse();
        }
        catch (Exception ex)
        {
            HandleException(ex);
            throw;
        }
    }

    protected abstract void ExecuteProxy(IServiceProvider serviceProvider);

    // Common helper methods
    protected ExternalServiceConfig GetServiceConfig(string serviceName);
    protected HttpClient CreateAuthenticatedHttpClient(ExternalServiceConfig config);
    protected void LogAuditRecord(string operation, string details);
}

// Generic proxy plugin
public class ProxyExecutePlugin : BaseProxyPlugin
{
    protected override void ExecuteProxy(IServiceProvider serviceProvider)
    {
        var context = GetExecutionContext(serviceProvider);

        // Get input parameters
        var serviceName = (string)context.InputParameters["ServiceName"];
        var operation = (string)context.InputParameters["Operation"];
        var requestPayload = (string)context.InputParameters["RequestPayload"];

        // Get service configuration
        var serviceConfig = GetServiceConfig(serviceName);

        // Create authenticated HTTP client
        using var httpClient = CreateAuthenticatedHttpClient(serviceConfig);

        // Execute request
        var response = ExecuteHttpRequest(httpClient, serviceConfig, operation, requestPayload);

        // Set output parameters
        context.OutputParameters["StatusCode"] = response.StatusCode;
        context.OutputParameters["ResponsePayload"] = response.Content;
    }
}

// Operation-specific plugins
public class ProxyDownloadFilePlugin : BaseProxyPlugin
{
    protected override void ExecuteProxy(IServiceProvider serviceProvider)
    {
        var context = GetExecutionContext(serviceProvider);

        // Get input parameters
        var documentId = (string)context.InputParameters["DocumentId"];
        var fileId = (string)context.InputParameters["FileId"];
        var downloadUrl = (string)context.InputParameters["DownloadUrl"];

        // Get service configuration for Spe.Bff.Api
        var serviceConfig = GetServiceConfig("SpeBffApi");

        // Create authenticated HTTP client
        using var httpClient = CreateAuthenticatedHttpClient(serviceConfig);

        // Build request
        var requestUrl = $"{serviceConfig.BaseUrl}/api/files/{fileId}/download";
        if (!string.IsNullOrEmpty(downloadUrl))
        {
            requestUrl = downloadUrl;
        }

        // Execute download
        var response = httpClient.GetAsync(requestUrl).Result;
        response.EnsureSuccessStatusCode();

        var fileContent = response.Content.ReadAsByteArrayAsync().Result;
        var fileName = GetFileNameFromResponse(response);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";

        // Set output parameters
        context.OutputParameters["FileContent"] = Convert.ToBase64String(fileContent);
        context.OutputParameters["FileName"] = fileName;
        context.OutputParameters["ContentType"] = contentType;
        context.OutputParameters["StatusCode"] = (int)response.StatusCode;
    }
}

// Similar implementations for ProxyDeleteFilePlugin, ProxyReplaceFilePlugin, ProxyUploadFilePlugin
```

### 3. External Service Configuration Entity

Store external service configurations in Dataverse for flexibility and maintainability.

```
Entity: sprk_externalserviceconfig
├── Primary Fields
│   ├── sprk_name (string, required) - e.g., "SpeBffApi"
│   ├── sprk_displayname (string) - e.g., "SPE BFF API"
│   ├── sprk_baseurl (string, required) - e.g., "https://spe-api-dev-67e2xz.azurewebsites.net"
│   └── sprk_description (string, optional)
│
├── Authentication Fields
│   ├── sprk_authtype (OptionSet) - "None", "ClientCredentials", "ManagedIdentity", "ApiKey"
│   ├── sprk_tenantid (string, optional)
│   ├── sprk_clientid (string, optional)
│   ├── sprk_clientsecret (string, optional, secured)
│   ├── sprk_scope (string, optional) - e.g., "api://spe-bff-api/.default"
│   ├── sprk_apikey (string, optional, secured)
│   └── sprk_apikeyheader (string, optional) - e.g., "X-API-Key"
│
├── Configuration Fields
│   ├── sprk_timeout (int) - Request timeout in seconds
│   ├── sprk_retrycount (int) - Number of retries
│   ├── sprk_retrydelay (int) - Delay between retries in ms
│   ├── sprk_circuitbreakerthreshold (int) - Circuit breaker threshold
│   └── sprk_enablelogging (bool) - Enable detailed logging
│
├── Status Fields
│   ├── sprk_isenabled (bool)
│   ├── sprk_lasthealthcheck (datetime)
│   ├── sprk_healthstatus (OptionSet) - "Healthy", "Degraded", "Unhealthy"
│   └── sprk_errorcount (int) - Count of recent errors
│
└── Audit Fields
    ├── createdby (Lookup)
    ├── createdon (DateTime)
    ├── modifiedby (Lookup)
    └── modifiedon (DateTime)
```

### 4. Audit/Logging Entity

Track all proxy operations for security, debugging, and compliance.

```
Entity: sprk_proxyauditlog
├── Primary Fields
│   ├── sprk_operation (string) - e.g., "DownloadFile"
│   ├── sprk_servicename (string) - e.g., "SpeBffApi"
│   ├── sprk_executiontime (datetime)
│   └── sprk_correlationid (string) - For tracing
│
├── Request Fields
│   ├── sprk_requestpayload (string) - JSON (sensitive data redacted)
│   ├── sprk_requestsize (int) - Bytes
│   └── sprk_userid (Lookup) - User who initiated
│
├── Response Fields
│   ├── sprk_statuscode (int)
│   ├── sprk_responsepayload (string) - JSON (sensitive data redacted)
│   ├── sprk_responsesize (int) - Bytes
│   └── sprk_duration (int) - Milliseconds
│
├── Status Fields
│   ├── sprk_success (bool)
│   ├── sprk_errormessage (string, optional)
│   └── sprk_errorstacktrace (string, optional)
│
└── Metadata Fields
    ├── sprk_clientip (string)
    ├── sprk_useragent (string)
    └── createdon (DateTime)
```

---

## Authentication Flow

### Step-by-Step Flow

```
1. User Action in PCF Control
   ┌─────────────────────────────────────┐
   │ User clicks "Download" button       │
   │ in Universal Dataset Grid           │
   └─────────────────────────────────────┘
                  │
                  ▼
2. PCF Control Calls context.webAPI
   ┌─────────────────────────────────────┐
   │ context.webAPI.execute(             │
   │   "sprk_ProxyDownloadFile",         │
   │   {                                 │
   │     DocumentId: "doc-123",          │
   │     FileId: "file-456"              │
   │   }                                 │
   │ )                                   │
   │                                     │
   │ ✅ User authentication implicit     │
   │    (Dataverse session)              │
   └─────────────────────────────────────┘
                  │
                  ▼
3. Dataverse Receives Request
   ┌─────────────────────────────────────┐
   │ Dataverse validates user session    │
   │ User has valid Dataverse auth       │
   │ User has permission to call Custom  │
   │ API                                 │
   └─────────────────────────────────────┘
                  │
                  ▼
4. Custom API Plugin Executes
   ┌─────────────────────────────────────┐
   │ ProxyDownloadFilePlugin.Execute()   │
   │                                     │
   │ Running in Dataverse sandbox        │
   │ With system privileges              │
   └─────────────────────────────────────┘
                  │
                  ▼
5. Plugin Retrieves Service Configuration
   ┌─────────────────────────────────────┐
   │ Query sprk_externalserviceconfig    │
   │ for "SpeBffApi"                     │
   │                                     │
   │ Gets:                               │
   │  - Base URL                         │
   │  - Auth type (ClientCredentials)    │
   │  - Client ID                        │
   │  - Client Secret (from secure      │
   │    field or Key Vault)              │
   │  - Scope                            │
   └─────────────────────────────────────┘
                  │
                  ▼
6. Plugin Acquires Service Token
   ┌─────────────────────────────────────┐
   │ Using Azure.Identity                │
   │                                     │
   │ var credential =                    │
   │   new ClientSecretCredential(       │
   │     tenantId,                       │
   │     clientId,                       │
   │     clientSecret                    │
   │   );                                │
   │                                     │
   │ var token = credential              │
   │   .GetTokenAsync(                   │
   │     new TokenRequestContext(        │
   │       new[] { scope }               │
   │     )                               │
   │   ).Result;                         │
   │                                     │
   │ ✅ Service-to-service token         │
   └─────────────────────────────────────┘
                  │
                  ▼
7. Plugin Calls External API
   ┌─────────────────────────────────────┐
   │ var httpClient = new HttpClient();  │
   │ httpClient.DefaultRequestHeaders    │
   │   .Authorization =                  │
   │     new AuthenticationHeaderValue(  │
   │       "Bearer",                     │
   │       token.Token                   │
   │     );                              │
   │                                     │
   │ var response = httpClient           │
   │   .GetAsync(                        │
   │     "https://spe-api.../download"   │
   │   ).Result;                         │
   └─────────────────────────────────────┘
                  │
                  ▼
8. External API (Spe.Bff.Api) Processes
   ┌─────────────────────────────────────┐
   │ Validates bearer token              │
   │ (JWT from Azure AD)                 │
   │                                     │
   │ Calls SharePoint Embedded API       │
   │ (using OBO if needed)               │
   │                                     │
   │ Returns file content                │
   └─────────────────────────────────────┘
                  │
                  ▼
9. Plugin Processes Response
   ┌─────────────────────────────────────┐
   │ Read file content bytes             │
   │ Convert to Base64                   │
   │ Extract file name, content type     │
   │                                     │
   │ Log audit record                    │
   │                                     │
   │ Set Custom API output parameters    │
   └─────────────────────────────────────┘
                  │
                  ▼
10. Dataverse Returns Response to PCF
   ┌─────────────────────────────────────┐
   │ context.webAPI.execute() resolves   │
   │ with response object:               │
   │ {                                   │
   │   FileContent: "base64...",         │
   │   FileName: "doc.pdf",              │
   │   ContentType: "application/pdf",   │
   │   StatusCode: 200                   │
   │ }                                   │
   └─────────────────────────────────────┘
                  │
                  ▼
11. PCF Control Processes Response
   ┌─────────────────────────────────────┐
   │ Decode Base64 to bytes              │
   │ Create Blob                         │
   │ Trigger browser download            │
   │                                     │
   │ ✅ File downloaded successfully     │
   └─────────────────────────────────────┘
```

### Authentication Actors

| Actor | Authentication Method | Privileges |
|-------|----------------------|------------|
| **User** | Azure AD (via Power Apps session) | Dataverse user permissions |
| **PCF Control** | Implicit (Dataverse session context) | Cannot access tokens directly |
| **Custom API Plugin** | Runs as system | Full Dataverse access + can acquire service tokens |
| **External API (Spe.Bff.Api)** | Service-to-service (Client Credentials) | API permissions defined in Azure AD |

---

## Data Model

### Entity Relationship Diagram

```
┌─────────────────────────────────────┐
│ sprk_externalserviceconfig          │
│─────────────────────────────────────│
│ PK: sprk_externalserviceconfigid    │
│ sprk_name (unique)                  │
│ sprk_baseurl                        │
│ sprk_authtype                       │
│ sprk_clientid                       │
│ sprk_clientsecret                   │
│ sprk_scope                          │
│ ...                                 │
└─────────────────────────────────────┘
                  │
                  │ 1:N
                  ▼
┌─────────────────────────────────────┐
│ sprk_proxyauditlog                  │
│─────────────────────────────────────│
│ PK: sprk_proxyauditlogid            │
│ FK: sprk_serviceconfig              │──┐
│ sprk_operation                      │  │ References
│ sprk_correlationid                  │  │ service config
│ sprk_requestpayload                 │  │
│ sprk_responsepayload                │  │
│ sprk_statuscode                     │  │
│ sprk_duration                       │  │
│ ...                                 │  │
└─────────────────────────────────────┘  │
                                          │
                  ┌───────────────────────┘
                  │
┌─────────────────────────────────────┐
│ sprk_document                       │
│─────────────────────────────────────│
│ (Existing entity from Sprint 3)    │
│ Used by Custom API operations       │
│ to validate document access         │
└─────────────────────────────────────┘
```

### Security Model

#### Entity-Level Security

- **sprk_externalserviceconfig**: System Administrator only
- **sprk_proxyauditlog**: Read-only for auditors, no user access to create/update

#### Field-Level Security

- **sprk_clientsecret**: Secured field, encrypted at rest
- **sprk_apikey**: Secured field, encrypted at rest

#### Custom API Security

Custom APIs can be configured with specific security roles:

```xml
<CustomAPI>
  <Name>sprk_ProxyDownloadFile</Name>
  <AllowedCustomProcessingStepType>AsyncOnly</AllowedCustomProcessingStepType>
  <BindingType>Global</BindingType>
  <ExecutePrivilegeName>prvReadsprk_document</ExecutePrivilegeName>
  <!-- Only users with Read privilege on sprk_document can call this -->
</CustomAPI>
```

---

## Implementation Guidance

### Phase 1: Foundation Setup

#### 1.1 Create Dataverse Solution

```bash
# Create new solution
pac solution init --publisher-name Spaarke --publisher-prefix sprk --outputDirectory src/dataverse/CustomApiProxy

cd src/dataverse/CustomApiProxy
```

#### 1.2 Create Plugin Project

```bash
# Create plugin project
dotnet new classlib -n Spaarke.Dataverse.CustomApiProxy
cd Spaarke.Dataverse.CustomApiProxy

# Add required packages
dotnet add package Microsoft.CrmSdk.CoreAssemblies
dotnet add package Azure.Identity
dotnet add package System.Net.Http
dotnet add package Newtonsoft.Json
```

#### 1.3 Create Base Plugin Class

```csharp
// File: BaseProxyPlugin.cs
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Xrm.Sdk;
using Azure.Identity;
using Azure.Core;
using Newtonsoft.Json;

namespace Spaarke.Dataverse.CustomApiProxy
{
    public abstract class BaseProxyPlugin : IPlugin
    {
        protected ITracingService TracingService { get; private set; }
        protected IOrganizationService OrganizationService { get; private set; }
        protected IPluginExecutionContext ExecutionContext { get; private set; }

        private readonly string _pluginName;

        protected BaseProxyPlugin(string pluginName)
        {
            _pluginName = pluginName;
        }

        public void Execute(IServiceProvider serviceProvider)
        {
            // Get services
            TracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            ExecutionContext = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            OrganizationService = serviceFactory.CreateOrganizationService(ExecutionContext.UserId);

            TracingService.Trace($"{_pluginName}: Starting execution");

            try
            {
                // Validate request
                ValidateRequest();

                // Log request
                var correlationId = LogRequest();

                // Execute derived class logic
                ExecuteProxy(serviceProvider, correlationId);

                // Log successful response
                LogResponse(correlationId, true, null);

                TracingService.Trace($"{_pluginName}: Completed successfully");
            }
            catch (Exception ex)
            {
                TracingService.Trace($"{_pluginName}: Error - {ex.Message}");
                LogResponse(Guid.NewGuid().ToString(), false, ex);
                throw new InvalidPluginExecutionException($"{_pluginName} failed: {ex.Message}", ex);
            }
        }

        protected abstract void ExecuteProxy(IServiceProvider serviceProvider, string correlationId);

        protected virtual void ValidateRequest()
        {
            // Base validation - can be overridden
            if (ExecutionContext == null)
                throw new InvalidPluginExecutionException("Execution context is null");
        }

        protected ExternalServiceConfig GetServiceConfig(string serviceName)
        {
            TracingService.Trace($"Retrieving config for service: {serviceName}");

            var query = new Microsoft.Xrm.Sdk.Query.QueryExpression("sprk_externalserviceconfig");
            query.ColumnSet = new Microsoft.Xrm.Sdk.Query.ColumnSet(true);
            query.Criteria.AddCondition("sprk_name", Microsoft.Xrm.Sdk.Query.ConditionOperator.Equal, serviceName);
            query.Criteria.AddCondition("sprk_isenabled", Microsoft.Xrm.Sdk.Query.ConditionOperator.Equal, true);

            var results = OrganizationService.RetrieveMultiple(query);

            if (results.Entities.Count == 0)
                throw new InvalidPluginExecutionException($"External service config not found: {serviceName}");

            var entity = results.Entities[0];

            return new ExternalServiceConfig
            {
                Name = entity.GetAttributeValue<string>("sprk_name"),
                BaseUrl = entity.GetAttributeValue<string>("sprk_baseurl"),
                AuthType = entity.GetAttributeValue<Microsoft.Xrm.Sdk.OptionSetValue>("sprk_authtype")?.Value ?? 0,
                TenantId = entity.GetAttributeValue<string>("sprk_tenantid"),
                ClientId = entity.GetAttributeValue<string>("sprk_clientid"),
                ClientSecret = entity.GetAttributeValue<string>("sprk_clientsecret"),
                Scope = entity.GetAttributeValue<string>("sprk_scope"),
                Timeout = entity.GetAttributeValue<int>("sprk_timeout"),
                RetryCount = entity.GetAttributeValue<int>("sprk_retrycount"),
                RetryDelay = entity.GetAttributeValue<int>("sprk_retrydelay")
            };
        }

        protected HttpClient CreateAuthenticatedHttpClient(ExternalServiceConfig config)
        {
            var httpClient = new HttpClient
            {
                BaseAddress = new Uri(config.BaseUrl),
                Timeout = TimeSpan.FromSeconds(config.Timeout > 0 ? config.Timeout : 300)
            };

            // Get access token based on auth type
            string accessToken = null;

            switch (config.AuthType)
            {
                case 1: // ClientCredentials
                    accessToken = GetClientCredentialsToken(config);
                    break;
                case 2: // ManagedIdentity
                    accessToken = GetManagedIdentityToken(config);
                    break;
                case 3: // ApiKey
                    httpClient.DefaultRequestHeaders.Add(config.ApiKeyHeader ?? "X-API-Key", config.ApiKey);
                    return httpClient;
                default:
                    // No authentication
                    return httpClient;
            }

            if (!string.IsNullOrEmpty(accessToken))
            {
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }

            return httpClient;
        }

        private string GetClientCredentialsToken(ExternalServiceConfig config)
        {
            TracingService.Trace("Acquiring token using ClientSecretCredential");

            var credential = new ClientSecretCredential(
                config.TenantId,
                config.ClientId,
                config.ClientSecret
            );

            var tokenRequestContext = new TokenRequestContext(new[] { config.Scope });
            var token = credential.GetToken(tokenRequestContext, default);

            TracingService.Trace("Token acquired successfully");
            return token.Token;
        }

        private string GetManagedIdentityToken(ExternalServiceConfig config)
        {
            TracingService.Trace("Acquiring token using ManagedIdentityCredential");

            var credential = new ManagedIdentityCredential(config.ClientId);
            var tokenRequestContext = new TokenRequestContext(new[] { config.Scope });
            var token = credential.GetToken(tokenRequestContext, default);

            TracingService.Trace("Token acquired successfully");
            return token.Token;
        }

        private string LogRequest()
        {
            var correlationId = Guid.NewGuid().ToString();

            try
            {
                var auditLog = new Entity("sprk_proxyauditlog");
                auditLog["sprk_operation"] = _pluginName;
                auditLog["sprk_correlationid"] = correlationId;
                auditLog["sprk_executiontime"] = DateTime.UtcNow;
                auditLog["sprk_userid"] = new EntityReference("systemuser", ExecutionContext.UserId);
                auditLog["sprk_requestpayload"] = JsonConvert.SerializeObject(ExecutionContext.InputParameters);

                OrganizationService.Create(auditLog);
            }
            catch (Exception ex)
            {
                TracingService.Trace($"Failed to log request: {ex.Message}");
            }

            return correlationId;
        }

        private void LogResponse(string correlationId, bool success, Exception error)
        {
            try
            {
                var query = new Microsoft.Xrm.Sdk.Query.QueryExpression("sprk_proxyauditlog");
                query.ColumnSet = new Microsoft.Xrm.Sdk.Query.ColumnSet(true);
                query.Criteria.AddCondition("sprk_correlationid", Microsoft.Xrm.Sdk.Query.ConditionOperator.Equal, correlationId);

                var results = OrganizationService.RetrieveMultiple(query);

                if (results.Entities.Count > 0)
                {
                    var auditLog = results.Entities[0];
                    auditLog["sprk_success"] = success;
                    auditLog["sprk_responsepayload"] = success ? JsonConvert.SerializeObject(ExecutionContext.OutputParameters) : null;
                    auditLog["sprk_errormessage"] = error?.Message;
                    auditLog["sprk_duration"] = (int)(DateTime.UtcNow - auditLog.GetAttributeValue<DateTime>("sprk_executiontime")).TotalMilliseconds;

                    OrganizationService.Update(auditLog);
                }
            }
            catch (Exception ex)
            {
                TracingService.Trace($"Failed to log response: {ex.Message}");
            }
        }
    }

    // Configuration model
    public class ExternalServiceConfig
    {
        public string Name { get; set; }
        public string BaseUrl { get; set; }
        public int AuthType { get; set; }
        public string TenantId { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string Scope { get; set; }
        public string ApiKey { get; set; }
        public string ApiKeyHeader { get; set; }
        public int Timeout { get; set; }
        public int RetryCount { get; set; }
        public int RetryDelay { get; set; }
    }
}
```

### Phase 2: Implement Operation-Specific Plugins

#### 2.1 ProxyDownloadFilePlugin

```csharp
// File: ProxyDownloadFilePlugin.cs
using System;
using System.Net.Http;
using Microsoft.Xrm.Sdk;

namespace Spaarke.Dataverse.CustomApiProxy
{
    public class ProxyDownloadFilePlugin : BaseProxyPlugin
    {
        public ProxyDownloadFilePlugin() : base("ProxyDownloadFile") { }

        protected override void ExecuteProxy(IServiceProvider serviceProvider, string correlationId)
        {
            // Get input parameters
            var documentId = ExecutionContext.InputParameters.Contains("DocumentId")
                ? (string)ExecutionContext.InputParameters["DocumentId"]
                : null;
            var fileId = ExecutionContext.InputParameters.Contains("FileId")
                ? (string)ExecutionContext.InputParameters["FileId"]
                : null;
            var downloadUrl = ExecutionContext.InputParameters.Contains("DownloadUrl")
                ? (string)ExecutionContext.InputParameters["DownloadUrl"]
                : null;

            if (string.IsNullOrEmpty(documentId))
                throw new InvalidPluginExecutionException("DocumentId is required");

            TracingService.Trace($"Downloading file for document: {documentId}");

            // Validate user has access to document
            ValidateDocumentAccess(documentId);

            // Get service configuration
            var serviceConfig = GetServiceConfig("SpeBffApi");

            // Create authenticated HTTP client
            using (var httpClient = CreateAuthenticatedHttpClient(serviceConfig))
            {
                // Build request URL
                string requestUrl;
                if (!string.IsNullOrEmpty(downloadUrl))
                {
                    requestUrl = downloadUrl;
                }
                else if (!string.IsNullOrEmpty(fileId))
                {
                    requestUrl = $"{serviceConfig.BaseUrl}/api/files/{fileId}/download";
                }
                else
                {
                    throw new InvalidPluginExecutionException("Either FileId or DownloadUrl must be provided");
                }

                TracingService.Trace($"Calling external API: {requestUrl}");

                // Execute download with retry logic
                var response = ExecuteWithRetry(() => httpClient.GetAsync(requestUrl).Result, serviceConfig);

                response.EnsureSuccessStatusCode();

                // Read response
                var fileContent = response.Content.ReadAsByteArrayAsync().Result;
                var fileName = GetFileNameFromResponse(response) ?? $"document-{documentId}.bin";
                var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";

                TracingService.Trace($"Downloaded file: {fileName}, Size: {fileContent.Length} bytes");

                // Set output parameters
                ExecutionContext.OutputParameters["FileContent"] = Convert.ToBase64String(fileContent);
                ExecutionContext.OutputParameters["FileName"] = fileName;
                ExecutionContext.OutputParameters["ContentType"] = contentType;
                ExecutionContext.OutputParameters["StatusCode"] = (int)response.StatusCode;
            }
        }

        private void ValidateDocumentAccess(string documentId)
        {
            TracingService.Trace($"Validating access to document: {documentId}");

            try
            {
                var document = OrganizationService.Retrieve("sprk_document", new Guid(documentId), new Microsoft.Xrm.Sdk.Query.ColumnSet("sprk_documentid"));
                TracingService.Trace("Document access validated");
            }
            catch (Exception ex)
            {
                throw new InvalidPluginExecutionException($"User does not have access to document {documentId}: {ex.Message}");
            }
        }

        private string GetFileNameFromResponse(HttpResponseMessage response)
        {
            if (response.Content.Headers.ContentDisposition != null)
            {
                return response.Content.Headers.ContentDisposition.FileName?.Trim('"');
            }
            return null;
        }

        private T ExecuteWithRetry<T>(Func<T> action, ExternalServiceConfig config)
        {
            int retryCount = config.RetryCount > 0 ? config.RetryCount : 3;
            int retryDelay = config.RetryDelay > 0 ? config.RetryDelay : 1000;

            for (int i = 0; i < retryCount; i++)
            {
                try
                {
                    return action();
                }
                catch (Exception ex)
                {
                    if (i == retryCount - 1)
                        throw;

                    TracingService.Trace($"Retry {i + 1}/{retryCount} after error: {ex.Message}");
                    System.Threading.Thread.Sleep(retryDelay * (i + 1));
                }
            }

            throw new InvalidPluginExecutionException("Max retries exceeded");
        }
    }
}
```

#### 2.2 ProxyDeleteFilePlugin

```csharp
// File: ProxyDeleteFilePlugin.cs
using System;
using System.Net.Http;
using Microsoft.Xrm.Sdk;

namespace Spaarke.Dataverse.CustomApiProxy
{
    public class ProxyDeleteFilePlugin : BaseProxyPlugin
    {
        public ProxyDeleteFilePlugin() : base("ProxyDeleteFile") { }

        protected override void ExecuteProxy(IServiceProvider serviceProvider, string correlationId)
        {
            var documentId = (string)ExecutionContext.InputParameters["DocumentId"];
            var fileId = (string)ExecutionContext.InputParameters["FileId"];

            TracingService.Trace($"Deleting file {fileId} for document {documentId}");

            // Validate access
            ValidateDocumentAccess(documentId);

            // Get service config
            var serviceConfig = GetServiceConfig("SpeBffApi");

            using (var httpClient = CreateAuthenticatedHttpClient(serviceConfig))
            {
                var requestUrl = $"{serviceConfig.BaseUrl}/api/files/{fileId}";

                TracingService.Trace($"Calling DELETE: {requestUrl}");

                var response = httpClient.DeleteAsync(requestUrl).Result;
                response.EnsureSuccessStatusCode();

                ExecutionContext.OutputParameters["Success"] = true;
                ExecutionContext.OutputParameters["StatusCode"] = (int)response.StatusCode;

                TracingService.Trace("File deleted successfully");
            }
        }

        private void ValidateDocumentAccess(string documentId)
        {
            try
            {
                var document = OrganizationService.Retrieve("sprk_document", new Guid(documentId), new Microsoft.Xrm.Sdk.Query.ColumnSet("sprk_documentid"));
            }
            catch (Exception ex)
            {
                throw new InvalidPluginExecutionException($"User does not have access to document {documentId}: {ex.Message}");
            }
        }
    }
}
```

#### 2.3 ProxyReplaceFilePlugin and ProxyUploadFilePlugin

Similar implementations following the same pattern as above.

### Phase 3: Create Custom API Definitions

Use the Power Platform CLI or solution file to define Custom APIs:

```xml
<!-- File: CustomApis/sprk_ProxyDownloadFile.xml -->
<CustomAPI>
  <UniqueName>sprk_ProxyDownloadFile</UniqueName>
  <DisplayName>Proxy Download File</DisplayName>
  <Description>Download file through external API proxy</Description>
  <BindingType>Global</BindingType>
  <IsFunction>false</IsFunction>
  <IsPrivate>false</IsPrivate>
  <ExecutePrivilegeName>prvReadsprk_document</ExecutePrivilegeName>

  <CustomAPIRequestParameters>
    <CustomAPIRequestParameter>
      <UniqueName>DocumentId</UniqueName>
      <Name>Document ID</Name>
      <Type>String</Type>
      <IsOptional>false</IsOptional>
    </CustomAPIRequestParameter>
    <CustomAPIRequestParameter>
      <UniqueName>FileId</UniqueName>
      <Name>File ID</Name>
      <Type>String</Type>
      <IsOptional>true</IsOptional>
    </CustomAPIRequestParameter>
    <CustomAPIRequestParameter>
      <UniqueName>DownloadUrl</UniqueName>
      <Name>Download URL</Name>
      <Type>String</Type>
      <IsOptional>true</IsOptional>
    </CustomAPIRequestParameter>
  </CustomAPIRequestParameters>

  <CustomAPIResponseProperties>
    <CustomAPIResponseProperty>
      <UniqueName>FileContent</UniqueName>
      <Name>File Content</Name>
      <Type>String</Type>
    </CustomAPIResponseProperty>
    <CustomAPIResponseProperty>
      <UniqueName>FileName</UniqueName>
      <Name>File Name</Name>
      <Type>String</Type>
    </CustomAPIResponseProperty>
    <CustomAPIResponseProperty>
      <UniqueName>ContentType</UniqueName>
      <Name>Content Type</Name>
      <Type>String</Type>
    </CustomAPIResponseProperty>
    <CustomAPIResponseProperty>
      <UniqueName>StatusCode</UniqueName>
      <Name>Status Code</Name>
      <Type>Integer</Type>
    </CustomAPIResponseProperty>
  </CustomAPIResponseProperties>
</CustomAPI>
```

### Phase 4: Update PCF Control

Update the PCF control to use Custom API Proxy instead of direct API calls:

```typescript
// File: src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/SdapApiClient.ts

export class SdapApiClient {
    private context: ComponentFramework.Context<any>;

    constructor(context: ComponentFramework.Context<any>) {
        this.context = context;
    }

    /**
     * Download file using Custom API Proxy
     */
    async downloadFile(documentId: string, fileId: string, downloadUrl?: string): Promise<Blob> {
        try {
            console.log('[SdapApiClient] Downloading file via Custom API Proxy', { documentId, fileId });

            const request = {
                DocumentId: documentId,
                FileId: fileId,
                DownloadUrl: downloadUrl
            };

            // Call Custom API through context.webAPI
            const response = await this.context.webAPI.execute({
                name: 'sprk_ProxyDownloadFile',
                parameters: request
            });

            // Parse response
            const fileContent = response.FileContent; // Base64 string
            const fileName = response.FileName;
            const contentType = response.ContentType;

            // Convert Base64 to Blob
            const byteCharacters = atob(fileContent);
            const byteNumbers = new Array(byteCharacters.length);
            for (let i = 0; i < byteCharacters.length; i++) {
                byteNumbers[i] = byteCharacters.charCodeAt(i);
            }
            const byteArray = new Uint8Array(byteNumbers);
            const blob = new Blob([byteArray], { type: contentType });

            console.log('[SdapApiClient] File downloaded successfully', { fileName, size: blob.size });

            return blob;
        } catch (error) {
            console.error('[SdapApiClient] Download failed', error);
            throw new Error(`Failed to download file: ${error.message}`);
        }
    }

    /**
     * Delete file using Custom API Proxy
     */
    async deleteFile(documentId: string, fileId: string): Promise<void> {
        try {
            console.log('[SdapApiClient] Deleting file via Custom API Proxy', { documentId, fileId });

            const request = {
                DocumentId: documentId,
                FileId: fileId
            };

            const response = await this.context.webAPI.execute({
                name: 'sprk_ProxyDeleteFile',
                parameters: request
            });

            if (!response.Success) {
                throw new Error(response.ErrorMessage || 'Delete failed');
            }

            console.log('[SdapApiClient] File deleted successfully');
        } catch (error) {
            console.error('[SdapApiClient] Delete failed', error);
            throw new Error(`Failed to delete file: ${error.message}`);
        }
    }

    /**
     * Replace file using Custom API Proxy
     */
    async replaceFile(documentId: string, fileId: string, file: File): Promise<void> {
        try {
            console.log('[SdapApiClient] Replacing file via Custom API Proxy', { documentId, fileId, fileName: file.name });

            // Convert File to Base64
            const fileContent = await this.fileToBase64(file);

            const request = {
                DocumentId: documentId,
                FileId: fileId,
                FileContent: fileContent,
                FileName: file.name,
                ContentType: file.type
            };

            const response = await this.context.webAPI.execute({
                name: 'sprk_ProxyReplaceFile',
                parameters: request
            });

            if (!response.Success) {
                throw new Error(response.ErrorMessage || 'Replace failed');
            }

            console.log('[SdapApiClient] File replaced successfully');
        } catch (error) {
            console.error('[SdapApiClient] Replace failed', error);
            throw new Error(`Failed to replace file: ${error.message}`);
        }
    }

    /**
     * Upload file using Custom API Proxy
     */
    async uploadFile(documentId: string, file: File): Promise<{ fileId: string; downloadUrl: string }> {
        try {
            console.log('[SdapApiClient] Uploading file via Custom API Proxy', { documentId, fileName: file.name });

            // Convert File to Base64
            const fileContent = await this.fileToBase64(file);

            const request = {
                DocumentId: documentId,
                FileContent: fileContent,
                FileName: file.name,
                ContentType: file.type
            };

            const response = await this.context.webAPI.execute({
                name: 'sprk_ProxyUploadFile',
                parameters: request
            });

            console.log('[SdapApiClient] File uploaded successfully', { fileId: response.FileId });

            return {
                fileId: response.FileId,
                downloadUrl: response.DownloadUrl
            };
        } catch (error) {
            console.error('[SdapApiClient] Upload failed', error);
            throw new Error(`Failed to upload file: ${error.message}`);
        }
    }

    private fileToBase64(file: File): Promise<string> {
        return new Promise((resolve, reject) => {
            const reader = new FileReader();
            reader.onload = () => {
                const base64 = (reader.result as string).split(',')[1];
                resolve(base64);
            };
            reader.onerror = reject;
            reader.readAsDataURL(file);
        });
    }
}
```

---

## Security Considerations

### 1. Authentication Security

- **Never expose secrets in PCF control**: Secrets stay in Dataverse secured fields
- **Service-to-service authentication**: Custom API uses dedicated service principal
- **Least privilege**: Service principal has minimal permissions needed
- **Token caching**: Implement token caching in plugin to reduce auth calls

### 2. Authorization Security

- **User validation**: Custom API validates user has access to document before proxying
- **Row-level security**: Leverage Dataverse security roles for document access
- **API permissions**: External API (Spe.Bff.Api) must also validate requests

### 3. Data Security

- **Sensitive data redaction**: Audit logs must redact sensitive content (file content, secrets)
- **Encryption at rest**: Use Dataverse field-level encryption for secrets
- **Encryption in transit**: All API calls over HTTPS only

### 4. Operational Security

- **Rate limiting**: Implement rate limiting per user to prevent abuse
- **Circuit breaker**: Stop calling failing external services to prevent cascade failures
- **Audit logging**: Log all operations for security and compliance

---

## Deployment Strategy

### Development Environment

1. Create Dataverse solution: `SpaarkeCustomApiProxy`
2. Deploy entities: `sprk_externalserviceconfig`, `sprk_proxyauditlog`
3. Build and sign plugin assembly
4. Register plugins with Plugin Registration Tool
5. Create Custom API definitions
6. Configure external service (SpeBffApi) in `sprk_externalserviceconfig`
7. Update PCF control and deploy
8. Test end-to-end

### Production Environment

1. Export managed solution from development
2. Import managed solution to production
3. Configure external service connections (different URLs, secrets for prod)
4. Test with limited users first
5. Monitor for errors and performance issues
6. Gradual rollout to all users

### CI/CD Pipeline

```yaml
# Azure DevOps pipeline example
stages:
  - stage: Build
    jobs:
      - job: BuildPlugin
        steps:
          - task: DotNetCoreCLI@2
            inputs:
              command: 'build'
              projects: 'src/dataverse/CustomApiProxy/**/*.csproj'

          - task: SnToolTask@1
            displayName: 'Sign Assembly'
            inputs:
              assemblyPath: '$(Build.SourcesDirectory)/bin/**/*.dll'
              snKeyPath: '$(Build.SourcesDirectory)/keys/spaarke.snk'

  - stage: DeployDev
    jobs:
      - job: DeployToDev
        steps:
          - task: PowerPlatformToolInstaller@2
          - task: PowerPlatformPackSolution@2
            inputs:
              SolutionSourceFolder: 'src/dataverse/CustomApiProxy'
              SolutionOutputFile: '$(Build.ArtifactStagingDirectory)/CustomApiProxy.zip'
          - task: PowerPlatformImportSolution@2
            inputs:
              authenticationType: 'PowerPlatformSPN'
              PowerPlatformSPN: '$(DevServiceConnection)'
              SolutionInputFile: '$(Build.ArtifactStagingDirectory)/CustomApiProxy.zip'
```

---

## Testing Approach

### Unit Tests

Test plugin logic in isolation using fakes:

```csharp
[TestClass]
public class ProxyDownloadFilePluginTests
{
    [TestMethod]
    public void Execute_ValidRequest_ReturnsFileContent()
    {
        // Arrange
        var context = new FakePluginExecutionContext();
        context.InputParameters["DocumentId"] = "test-doc-id";
        context.InputParameters["FileId"] = "test-file-id";

        var serviceProvider = new FakeServiceProvider(context);
        var plugin = new ProxyDownloadFilePlugin();

        // Act
        plugin.Execute(serviceProvider);

        // Assert
        Assert.IsTrue(context.OutputParameters.Contains("FileContent"));
        Assert.IsNotNull(context.OutputParameters["FileName"]);
    }
}
```

### Integration Tests

Test end-to-end flow with real Dataverse environment:

```typescript
describe('Custom API Proxy Integration', () => {
    it('should download file through proxy', async () => {
        const client = new SdapApiClient(mockContext);

        const blob = await client.downloadFile('doc-123', 'file-456');

        expect(blob).toBeDefined();
        expect(blob.size).toBeGreaterThan(0);
    });

    it('should handle authentication errors', async () => {
        const client = new SdapApiClient(mockContext);

        await expect(client.downloadFile('invalid-doc', 'invalid-file'))
            .rejects
            .toThrow('Failed to download file');
    });
});
```

### Performance Tests

Test latency and throughput:

```csharp
[TestMethod]
public void PerformanceTest_DownloadFile_UnderTwoSeconds()
{
    var stopwatch = Stopwatch.StartNew();

    // Execute download
    plugin.Execute(serviceProvider);

    stopwatch.Stop();
    Assert.IsTrue(stopwatch.ElapsedMilliseconds < 2000, $"Download took {stopwatch.ElapsedMilliseconds}ms");
}
```

---

## Extensibility

### Adding New External Services

To add a new external service (e.g., Azure Blob Storage API):

1. **Create service configuration** in `sprk_externalserviceconfig`:
   ```
   Name: AzureBlobStorage
   BaseUrl: https://mystorageaccount.blob.core.windows.net
   AuthType: ManagedIdentity
   Scope: https://storage.azure.com/.default
   ```

2. **Create Custom API** for new operations:
   ```xml
   <CustomAPI>
     <UniqueName>sprk_ProxyUploadBlob</UniqueName>
     ...
   </CustomAPI>
   ```

3. **Implement plugin** inheriting from `BaseProxyPlugin`:
   ```csharp
   public class ProxyUploadBlobPlugin : BaseProxyPlugin
   {
       protected override void ExecuteProxy(...)
       {
           var serviceConfig = GetServiceConfig("AzureBlobStorage");
           using var httpClient = CreateAuthenticatedHttpClient(serviceConfig);
           // ... upload logic
       }
   }
   ```

4. **Update PCF control** with new API client method:
   ```typescript
   async uploadToBlob(containerName: string, blob: Blob): Promise<string> {
       const response = await this.context.webAPI.execute({
           name: 'sprk_ProxyUploadBlob',
           parameters: { ... }
       });
       return response.BlobUrl;
   }
   ```

### Extensibility Points

The Custom API Proxy architecture provides several extensibility points:

1. **BaseProxyPlugin.ValidateRequest()**: Override to add custom validation
2. **BaseProxyPlugin.LogRequest()**: Override to add custom logging
3. **External service config**: Add new fields for service-specific configuration
4. **HTTP interceptors**: Add middleware for custom headers, retries, etc.

---

## Monitoring & Observability

### Application Insights Integration

Add Application Insights to plugin for telemetry:

```csharp
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;

public abstract class BaseProxyPlugin : IPlugin
{
    private static TelemetryClient _telemetryClient;

    static BaseProxyPlugin()
    {
        _telemetryClient = new TelemetryClient(
            new TelemetryConfiguration
            {
                InstrumentationKey = "YOUR_APP_INSIGHTS_KEY"
            }
        );
    }

    protected void TrackDependency(string serviceName, string operation, TimeSpan duration, bool success)
    {
        var dependency = new DependencyTelemetry
        {
            Name = $"{serviceName}.{operation}",
            Duration = duration,
            Success = success,
            Type = "HTTP"
        };

        _telemetryClient.TrackDependency(dependency);
    }
}
```

### Key Metrics to Monitor

1. **Latency**
   - P50, P95, P99 latency for each operation
   - External API call latency
   - Total end-to-end latency

2. **Availability**
   - Success rate per operation
   - External API availability
   - Circuit breaker status

3. **Throughput**
   - Requests per second
   - Data transfer volume
   - Concurrent operations

4. **Errors**
   - Error rate by type
   - Failed authentication attempts
   - Rate limit violations

### Monitoring Dashboard

Create Azure Dashboard or Power BI report showing:

- Real-time operation status
- Error trends over time
- Top users by API usage
- External service health
- Cost analysis (API calls vs Dataverse API limits)

---

## Conclusion

The Custom API Proxy architecture provides a **secure, reusable, and production-ready** solution for enabling PCF controls to call external APIs while maintaining Power Platform security best practices.

**Key Takeaways:**

✅ **Separation of Concerns**: Proxy is a standalone infrastructure component
✅ **Reusability**: Easy to add new external services without code changes
✅ **Security**: Leverages Dataverse implicit auth + service-to-service patterns
✅ **Production Ready**: Comprehensive error handling, monitoring, and observability
✅ **Maintainability**: Clear architecture, well-documented, testable

This architecture will serve as the foundation for the SDAP Dataverse-to-SharePointEmbedded service and all future PCF-to-external-API integrations.
