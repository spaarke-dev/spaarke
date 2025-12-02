# Phase 2: Dataverse Custom API Foundation

**Status**: 🔲 Not Started
**Duration**: 1.5 days
**Prerequisites**: Phase 1 complete, Dataverse environment access

---

## Phase Objectives

Build the foundational infrastructure for Custom API Proxy including:
- Dataverse solution structure
- Base plugin class with common functionality
- Configuration entity for external services
- Audit logging entity
- Logging and telemetry infrastructure
- Unit tests for base functionality

---

## Context for AI Vibe Coding

### What We're Building
A reusable plugin framework that provides:
- Common authentication logic (Azure.Identity)
- Configuration management (external services)
- Audit logging (compliance and debugging)
- Error handling and retry logic
- Telemetry and monitoring

### Why This Matters
This foundation will be used by ALL proxy operations (download, delete, replace, upload). Building it correctly ensures consistency, maintainability, and extensibility.

### Key Patterns
- **Inheritance**: All operation-specific plugins inherit from `BaseProxyPlugin`
- **Composition**: BaseProxyPlugin uses helper classes for auth, logging, config
- **Dependency Injection**: Plugin gets services via IServiceProvider
- **Fail-Fast**: Validate early, throw meaningful exceptions

---

## Task Breakdown

### Task 2.1: Create Dataverse Solution Structure

**Objective**: Create a new Dataverse solution to contain all Custom API Proxy components.

**AI Instructions**:

1. Create solution directory:
```bash
mkdir -p src/dataverse/Spaarke.CustomApiProxy
cd src/dataverse/Spaarke.CustomApiProxy
```

2. Initialize Dataverse solution:
```bash
pac solution init \
  --publisher-name Spaarke \
  --publisher-prefix sprk \
  --outputDirectory .
```

3. Verify solution structure:
```bash
ls -la
# Should see: Other/ and solution.xml
```

**Expected Output**:
- Solution directory created at `src/dataverse/Spaarke.CustomApiProxy`
- `solution.xml` file present
- `Other/` directory for entities and plugins

**Validation**:
- Solution can be opened in Power Platform CLI
- Publisher prefix is "sprk"

---

### Task 2.2: Create Plugin Project

**Objective**: Create .NET class library project for plugin code.

**AI Instructions**:

1. Create plugin project:
```bash
cd src/dataverse/Spaarke.CustomApiProxy
mkdir Plugins
cd Plugins

dotnet new classlib -n Spaarke.Dataverse.CustomApiProxy -f net462
```

**Important**: Use `net462` (not .NET 6/8) because Dataverse plugins run in .NET Framework sandbox.

2. Add required NuGet packages:
```bash
cd Spaarke.Dataverse.CustomApiProxy

dotnet add package Microsoft.CrmSdk.CoreAssemblies --version 9.0.2.56
dotnet add package Azure.Identity --version 1.10.4
dotnet add package Azure.Core --version 1.36.0
dotnet add package Newtonsoft.Json --version 13.0.3
dotnet add package System.Net.Http --version 4.3.4
```

3. Create strong name key for signing:
```bash
# Navigate to project root
cd c:/code_files/spaarke

# Create keys directory if not exists
mkdir -p keys

# Generate strong name key
sn -k keys/spaarke.snk
```

4. Update `.csproj` to sign assembly:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net462</TargetFramework>
    <AssemblyOriginatorKeyFile>../../../../keys/spaarke.snk</AssemblyOriginatorKeyFile>
    <SignAssembly>true</SignAssembly>
    <RootNamespace>Spaarke.Dataverse.CustomApiProxy</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.CrmSdk.CoreAssemblies" Version="9.0.2.56" />
    <PackageReference Include="Azure.Identity" Version="1.10.4" />
    <PackageReference Include="Azure.Core" Version="1.36.0" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
    <PackageReference Include="System.Net.Http" Version="4.3.4" />
  </ItemGroup>
</Project>
```

5. Delete default `Class1.cs`

6. Build to verify:
```bash
dotnet build
```

**Expected Output**:
- Project builds successfully
- Assembly is signed with strong name key
- All NuGet packages restored

**Validation**:
```bash
# Verify assembly is signed
sn -v bin/Debug/net462/Spaarke.Dataverse.CustomApiProxy.dll
```

---

### Task 2.3: Implement Base Plugin Class

**Objective**: Create abstract base plugin class with common functionality.

**AI Instructions**:

Create file: `src/dataverse/Spaarke.CustomApiProxy/Plugins/Spaarke.Dataverse.CustomApiProxy/BaseProxyPlugin.cs`

**Implementation Requirements**:

1. **Implement IPlugin interface**
2. **Provide protected properties**: TracingService, OrganizationService, ExecutionContext
3. **Implement Execute() method** with:
   - Service provider initialization
   - Request validation
   - Request logging (correlation ID)
   - Call to abstract ExecuteProxy() method
   - Response logging
   - Comprehensive error handling
4. **Implement GetServiceConfig()** to retrieve external service configuration
5. **Implement CreateAuthenticatedHttpClient()** with:
   - Support for ClientCredentials authentication
   - Support for ManagedIdentity authentication
   - Support for ApiKey authentication
   - Token acquisition using Azure.Identity
6. **Implement retry logic** with exponential backoff
7. **Implement audit logging** for request/response

**Code Template**:

```csharp
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Azure.Identity;
using Azure.Core;
using Newtonsoft.Json;

namespace Spaarke.Dataverse.CustomApiProxy
{
    /// <summary>
    /// Base plugin class providing common functionality for all Custom API Proxy operations.
    /// Handles authentication, configuration retrieval, audit logging, and error handling.
    /// </summary>
    public abstract class BaseProxyPlugin : IPlugin
    {
        protected ITracingService TracingService { get; private set; }
        protected IOrganizationService OrganizationService { get; private set; }
        protected IPluginExecutionContext ExecutionContext { get; private set; }

        private readonly string _pluginName;

        protected BaseProxyPlugin(string pluginName)
        {
            if (string.IsNullOrWhiteSpace(pluginName))
                throw new ArgumentNullException(nameof(pluginName));

            _pluginName = pluginName;
        }

        public void Execute(IServiceProvider serviceProvider)
        {
            // TODO: Initialize services from serviceProvider
            // TODO: Validate request
            // TODO: Log request with correlation ID
            // TODO: Call ExecuteProxy (derived class)
            // TODO: Log response
            // TODO: Handle errors
        }

        /// <summary>
        /// Derived classes implement proxy logic here.
        /// </summary>
        protected abstract void ExecuteProxy(IServiceProvider serviceProvider, string correlationId);

        /// <summary>
        /// Validate request parameters. Can be overridden by derived classes.
        /// </summary>
        protected virtual void ValidateRequest()
        {
            // Base validation
        }

        /// <summary>
        /// Retrieve external service configuration from Dataverse.
        /// </summary>
        protected ExternalServiceConfig GetServiceConfig(string serviceName)
        {
            // TODO: Query sprk_externalserviceconfig entity
            // TODO: Validate service is enabled
            // TODO: Return configuration object
            throw new NotImplementedException();
        }

        /// <summary>
        /// Create HttpClient with authentication configured based on service config.
        /// </summary>
        protected HttpClient CreateAuthenticatedHttpClient(ExternalServiceConfig config)
        {
            // TODO: Create HttpClient with base URL and timeout
            // TODO: Acquire access token based on auth type
            // TODO: Add Authorization header
            throw new NotImplementedException();
        }

        private string GetClientCredentialsToken(ExternalServiceConfig config)
        {
            // TODO: Use Azure.Identity.ClientSecretCredential
            // TODO: Acquire token for specified scope
            throw new NotImplementedException();
        }

        private string GetManagedIdentityToken(ExternalServiceConfig config)
        {
            // TODO: Use Azure.Identity.ManagedIdentityCredential
            // TODO: Acquire token for specified scope
            throw new NotImplementedException();
        }

        private string LogRequest()
        {
            // TODO: Create sprk_proxyauditlog record
            // TODO: Log correlation ID, operation, user, timestamp
            // TODO: Return correlation ID
            throw new NotImplementedException();
        }

        private void LogResponse(string correlationId, bool success, Exception error)
        {
            // TODO: Update sprk_proxyauditlog record
            // TODO: Log response payload, status, duration, errors
            throw new NotImplementedException();
        }

        /// <summary>
        /// Execute action with retry logic and exponential backoff.
        /// </summary>
        protected T ExecuteWithRetry<T>(Func<T> action, ExternalServiceConfig config)
        {
            // TODO: Implement retry logic with exponential backoff
            // TODO: Use config.RetryCount and config.RetryDelay
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// External service configuration model.
    /// </summary>
    public class ExternalServiceConfig
    {
        public string Name { get; set; }
        public string BaseUrl { get; set; }
        public int AuthType { get; set; } // 0=None, 1=ClientCredentials, 2=ManagedIdentity, 3=ApiKey
        public string TenantId { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string Scope { get; set; }
        public string ApiKey { get; set; }
        public string ApiKeyHeader { get; set; }
        public int Timeout { get; set; } // Seconds
        public int RetryCount { get; set; }
        public int RetryDelay { get; set; } // Milliseconds
    }
}
```

**Implementation Steps**:

1. **Complete Execute() method**:
   - Get services from serviceProvider
   - Try-catch block with InvalidPluginExecutionException
   - Trace at key points

2. **Complete GetServiceConfig()**:
   - Query `sprk_externalserviceconfig` by name
   - Check `sprk_isenabled` field
   - Map entity fields to ExternalServiceConfig object

3. **Complete CreateAuthenticatedHttpClient()**:
   - Create HttpClient with BaseAddress and Timeout
   - Switch on AuthType to get token
   - Add Authorization header if token acquired

4. **Complete token acquisition methods**:
   - Use Azure.Identity credentials
   - Create TokenRequestContext with scope
   - Call GetToken() synchronously
   - Return token.Token

5. **Complete audit logging**:
   - Create/update `sprk_proxyauditlog` entity
   - Redact sensitive data from payloads
   - Handle logging errors gracefully (don't fail proxy operation)

6. **Complete retry logic**:
   - Loop up to RetryCount times
   - Catch exceptions and retry
   - Exponential backoff: delay * (attempt + 1)
   - Re-throw on final attempt

**Validation**:
- Code compiles without errors
- BaseProxyPlugin is abstract
- All TODO comments resolved
- XML documentation comments added

---

### Task 2.4: Create Configuration Entity

**Objective**: Create `sprk_externalserviceconfig` entity in Dataverse.

**AI Instructions**:

**Option A: Using Power Platform CLI (Recommended)**

1. Create entity definition file: `src/dataverse/Spaarke.CustomApiProxy/Entities/sprk_externalserviceconfig.xml`

```xml
<?xml version="1.0" encoding="utf-8"?>
<Entity Name="sprk_externalserviceconfig">
  <EntityInfo>
    <entity Name="sprk_externalserviceconfig">
      <LocalizedNames>
        <LocalizedName description="External Service Configuration" languagecode="1033" />
      </LocalizedNames>
      <Descriptions>
        <Description description="Configuration for external APIs accessed via Custom API Proxy" languagecode="1033" />
      </Descriptions>
      <attributes>
        <!-- Primary Field -->
        <attribute PhysicalName="sprk_name">
          <Type>string</Type>
          <Name>sprk_name</Name>
          <LogicalName>sprk_name</LogicalName>
          <RequiredLevel>SystemRequired</RequiredLevel>
          <DisplayMask>ValidForAdvancedFind|ValidForForm|ValidForGrid</DisplayMask>
          <ImeMode>auto</ImeMode>
          <ValidForUpdateApi>1</ValidForUpdateApi>
          <MaxLength>100</MaxLength>
          <IsLocalizable>0</IsLocalizable>
          <LocalizedNames>
            <LocalizedName description="Name" languagecode="1033" />
          </LocalizedNames>
          <Descriptions>
            <Description description="Unique name for the external service (e.g., SpeBffApi)" languagecode="1033" />
          </Descriptions>
        </attribute>

        <attribute PhysicalName="sprk_displayname">
          <Type>string</Type>
          <Name>sprk_displayname</Name>
          <MaxLength>200</MaxLength>
          <LocalizedNames>
            <LocalizedName description="Display Name" languagecode="1033" />
          </LocalizedNames>
        </attribute>

        <attribute PhysicalName="sprk_baseurl">
          <Type>string</Type>
          <Name>sprk_baseurl</Name>
          <RequiredLevel>SystemRequired</RequiredLevel>
          <MaxLength>500</MaxLength>
          <LocalizedNames>
            <LocalizedName description="Base URL" languagecode="1033" />
          </LocalizedNames>
          <Descriptions>
            <Description description="Base URL of the external API" languagecode="1033" />
          </Descriptions>
        </attribute>

        <attribute PhysicalName="sprk_description">
          <Type>memo</Type>
          <Name>sprk_description</Name>
          <MaxLength>2000</MaxLength>
          <LocalizedNames>
            <LocalizedName description="Description" languagecode="1033" />
          </LocalizedNames>
        </attribute>

        <!-- Authentication Fields -->
        <attribute PhysicalName="sprk_authtype">
          <Type>picklist</Type>
          <Name>sprk_authtype</Name>
          <RequiredLevel>SystemRequired</RequiredLevel>
          <LocalizedNames>
            <LocalizedName description="Authentication Type" languagecode="1033" />
          </LocalizedNames>
          <optionset Name="sprk_authtype">
            <Options>
              <option value="0" label="None" />
              <option value="1" label="Client Credentials" />
              <option value="2" label="Managed Identity" />
              <option value="3" label="API Key" />
            </Options>
          </optionset>
        </attribute>

        <attribute PhysicalName="sprk_tenantid">
          <Type>string</Type>
          <Name>sprk_tenantid</Name>
          <MaxLength>100</MaxLength>
          <LocalizedNames>
            <LocalizedName description="Tenant ID" languagecode="1033" />
          </LocalizedNames>
        </attribute>

        <attribute PhysicalName="sprk_clientid">
          <Type>string</Type>
          <Name>sprk_clientid</Name>
          <MaxLength>100</MaxLength>
          <LocalizedNames>
            <LocalizedName description="Client ID" languagecode="1033" />
          </LocalizedNames>
        </attribute>

        <attribute PhysicalName="sprk_clientsecret">
          <Type>string</Type>
          <Name>sprk_clientsecret</Name>
          <MaxLength>500</MaxLength>
          <IsSecured>true</IsSecured>
          <LocalizedNames>
            <LocalizedName description="Client Secret" languagecode="1033" />
          </LocalizedNames>
        </attribute>

        <attribute PhysicalName="sprk_scope">
          <Type>string</Type>
          <Name>sprk_scope</Name>
          <MaxLength>200</MaxLength>
          <LocalizedNames>
            <LocalizedName description="Scope" languagecode="1033" />
          </LocalizedNames>
          <Descriptions>
            <Description description="OAuth scope (e.g., api://app-id/.default)" languagecode="1033" />
          </Descriptions>
        </attribute>

        <attribute PhysicalName="sprk_apikey">
          <Type>string</Type>
          <Name>sprk_apikey</Name>
          <MaxLength>500</MaxLength>
          <IsSecured>true</IsSecured>
          <LocalizedNames>
            <LocalizedName description="API Key" languagecode="1033" />
          </LocalizedNames>
        </attribute>

        <attribute PhysicalName="sprk_apikeyheader">
          <Type>string</Type>
          <Name>sprk_apikeyheader</Name>
          <MaxLength>100</MaxLength>
          <LocalizedNames>
            <LocalizedName description="API Key Header" languagecode="1033" />
          </LocalizedNames>
          <Descriptions>
            <Description description="HTTP header name for API key (e.g., X-API-Key)" languagecode="1033" />
          </Descriptions>
        </attribute>

        <!-- Configuration Fields -->
        <attribute PhysicalName="sprk_timeout">
          <Type>integer</Type>
          <Name>sprk_timeout</Name>
          <MinValue>1</MinValue>
          <MaxValue>600</MaxValue>
          <LocalizedNames>
            <LocalizedName description="Timeout (seconds)" languagecode="1033" />
          </LocalizedNames>
        </attribute>

        <attribute PhysicalName="sprk_retrycount">
          <Type>integer</Type>
          <Name>sprk_retrycount</Name>
          <MinValue>0</MinValue>
          <MaxValue>10</MaxValue>
          <LocalizedNames>
            <LocalizedName description="Retry Count" languagecode="1033" />
          </LocalizedNames>
        </attribute>

        <attribute PhysicalName="sprk_retrydelay">
          <Type>integer</Type>
          <Name>sprk_retrydelay</Name>
          <MinValue>0</MinValue>
          <MaxValue>30000</MaxValue>
          <LocalizedNames>
            <LocalizedName description="Retry Delay (ms)" languagecode="1033" />
          </LocalizedNames>
        </attribute>

        <!-- Status Fields -->
        <attribute PhysicalName="sprk_isenabled">
          <Type>boolean</Type>
          <Name>sprk_isenabled</Name>
          <LocalizedNames>
            <LocalizedName description="Is Enabled" languagecode="1033" />
          </LocalizedNames>
        </attribute>

        <attribute PhysicalName="sprk_healthstatus">
          <Type>picklist</Type>
          <Name>sprk_healthstatus</Name>
          <LocalizedNames>
            <LocalizedName description="Health Status" languagecode="1033" />
          </LocalizedNames>
          <optionset Name="sprk_healthstatus">
            <Options>
              <option value="0" label="Healthy" />
              <option value="1" label="Degraded" />
              <option value="2" label="Unhealthy" />
            </Options>
          </optionset>
        </attribute>

        <attribute PhysicalName="sprk_lasthealthcheck">
          <Type>datetime</Type>
          <Name>sprk_lasthealthcheck</Name>
          <Format>DateAndTime</Format>
          <Behavior>UserLocal</Behavior>
          <LocalizedNames>
            <LocalizedName description="Last Health Check" languagecode="1033" />
          </LocalizedNames>
        </attribute>

        <attribute PhysicalName="sprk_errorcount">
          <Type>integer</Type>
          <Name>sprk_errorcount</Name>
          <MinValue>0</MinValue>
          <LocalizedNames>
            <LocalizedName description="Error Count" languagecode="1033" />
          </LocalizedNames>
        </attribute>
      </attributes>
    </entity>
  </EntityInfo>
</Entity>
```

**Option B: Using Power Apps Maker Portal (Alternative)**

If XML approach fails, create entity manually:

1. Navigate to https://make.powerapps.com
2. Select environment: spaarkedev1
3. Go to Dataverse → Tables → New table
4. Set:
   - Display name: External Service Configuration
   - Plural name: External Service Configurations
   - Schema name: sprk_externalserviceconfig
5. Create fields as listed in XML above
6. Enable "Audit changes" in Advanced options
7. Save and publish

**Validation**:
- Entity exists in Dataverse
- All fields created with correct data types
- Secured fields (`sprk_clientsecret`, `sprk_apikey`) are encrypted
- Entity appears in model-driven apps

---

### Task 2.5: Create Audit Log Entity

**Objective**: Create `sprk_proxyauditlog` entity in Dataverse.

**AI Instructions**:

Create entity with these fields:

| Field Name | Type | Max Length | Description |
|------------|------|------------|-------------|
| sprk_operation | String | 100 | Operation name (e.g., "DownloadFile") |
| sprk_servicename | String | 100 | External service name |
| sprk_correlationid | String | 100 | Correlation ID for tracing |
| sprk_executiontime | DateTime | - | When operation started |
| sprk_requestpayload | Memo | 10000 | JSON request (redacted) |
| sprk_requestsize | Integer | - | Request size in bytes |
| sprk_userid | Lookup | - | User who initiated (systemuser) |
| sprk_statuscode | Integer | - | HTTP status code |
| sprk_responsepayload | Memo | 10000 | JSON response (redacted) |
| sprk_responsesize | Integer | - | Response size in bytes |
| sprk_duration | Integer | - | Duration in milliseconds |
| sprk_success | Boolean | - | Operation succeeded |
| sprk_errormessage | Memo | 5000 | Error message if failed |
| sprk_clientip | String | 50 | Client IP address |
| sprk_useragent | String | 500 | User agent string |

**Security Settings**:
- Read-only for all users (only plugins can create/update)
- Enable auditing
- Retention policy: 90 days (configure in Dataverse)

**Validation**:
- Entity created successfully
- Lookup to systemuser configured
- Entity is read-only for users (via security roles)

---

### Task 2.6: Add Logging and Telemetry

**Objective**: Add Application Insights integration for advanced telemetry.

**AI Instructions**:

1. Add NuGet package:
```bash
cd src/dataverse/Spaarke.CustomApiProxy/Plugins/Spaarke.Dataverse.CustomApiProxy
dotnet add package Microsoft.ApplicationInsights --version 2.21.0
```

2. Create telemetry helper class: `TelemetryHelper.cs`

```csharp
using System;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace Spaarke.Dataverse.CustomApiProxy
{
    /// <summary>
    /// Helper class for Application Insights telemetry.
    /// </summary>
    public static class TelemetryHelper
    {
        private static TelemetryClient _telemetryClient;
        private static readonly object _lock = new object();

        /// <summary>
        /// Initialize telemetry client with instrumentation key from environment variable.
        /// </summary>
        public static void Initialize(string instrumentationKey)
        {
            if (_telemetryClient == null)
            {
                lock (_lock)
                {
                    if (_telemetryClient == null)
                    {
                        var config = new TelemetryConfiguration
                        {
                            InstrumentationKey = instrumentationKey
                        };
                        _telemetryClient = new TelemetryClient(config);
                    }
                }
            }
        }

        /// <summary>
        /// Track a dependency call (external API).
        /// </summary>
        public static void TrackDependency(string serviceName, string operation, TimeSpan duration, bool success, string correlationId = null)
        {
            if (_telemetryClient == null) return;

            var dependency = new DependencyTelemetry
            {
                Name = $"{serviceName}.{operation}",
                Duration = duration,
                Success = success,
                Type = "HTTP",
                Id = correlationId
            };

            _telemetryClient.TrackDependency(dependency);
        }

        /// <summary>
        /// Track a custom event.
        /// </summary>
        public static void TrackEvent(string eventName, string correlationId = null)
        {
            if (_telemetryClient == null) return;

            var eventTelemetry = new EventTelemetry(eventName);
            if (!string.IsNullOrEmpty(correlationId))
            {
                eventTelemetry.Properties["CorrelationId"] = correlationId;
            }

            _telemetryClient.TrackEvent(eventTelemetry);
        }

        /// <summary>
        /// Track an exception.
        /// </summary>
        public static void TrackException(Exception exception, string correlationId = null)
        {
            if (_telemetryClient == null) return;

            var exceptionTelemetry = new ExceptionTelemetry(exception);
            if (!string.IsNullOrEmpty(correlationId))
            {
                exceptionTelemetry.Properties["CorrelationId"] = correlationId;
            }

            _telemetryClient.TrackException(exceptionTelemetry);
        }

        /// <summary>
        /// Flush telemetry buffer.
        /// </summary>
        public static void Flush()
        {
            _telemetryClient?.Flush();
        }
    }
}
```

3. Update `BaseProxyPlugin.Execute()` to use telemetry:

```csharp
public void Execute(IServiceProvider serviceProvider)
{
    var startTime = DateTime.UtcNow;
    string correlationId = null;

    try
    {
        // Initialize services...

        // Initialize telemetry if key available
        var instrumentationKey = Environment.GetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY");
        if (!string.IsNullOrEmpty(instrumentationKey))
        {
            TelemetryHelper.Initialize(instrumentationKey);
        }

        // Rest of Execute logic...

        // Track success
        var duration = DateTime.UtcNow - startTime;
        TelemetryHelper.TrackDependency("CustomApiProxy", _pluginName, duration, true, correlationId);
    }
    catch (Exception ex)
    {
        var duration = DateTime.UtcNow - startTime;
        TelemetryHelper.TrackDependency("CustomApiProxy", _pluginName, duration, false, correlationId);
        TelemetryHelper.TrackException(ex, correlationId);
        throw;
    }
    finally
    {
        TelemetryHelper.Flush();
    }
}
```

**Validation**:
- Code compiles
- TelemetryHelper can be initialized
- Telemetry tracked for success and failure cases

---

### Task 2.7: Write Unit Tests

**Objective**: Create unit tests for BaseProxyPlugin using fakes.

**AI Instructions**:

1. Create test project:
```bash
cd src/dataverse/Spaarke.CustomApiProxy/Plugins
dotnet new mstest -n Spaarke.Dataverse.CustomApiProxy.Tests -f net462
cd Spaarke.Dataverse.CustomApiProxy.Tests
```

2. Add project reference:
```bash
dotnet add reference ../Spaarke.Dataverse.CustomApiProxy/Spaarke.Dataverse.CustomApiProxy.csproj
```

3. Add test packages:
```bash
dotnet add package FakeXrmEasy.Core --version 3.4.3
dotnet add package FakeXrmEasy.Messages --version 3.4.3
dotnet add package FakeXrmEasy.Plugins --version 3.4.3
```

4. Create test class: `BaseProxyPluginTests.cs`

```csharp
using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using FakeXrmEasy;
using FakeXrmEasy.Plugins;
using Microsoft.Xrm.Sdk;

namespace Spaarke.Dataverse.CustomApiProxy.Tests
{
    [TestClass]
    public class BaseProxyPluginTests
    {
        private XrmFakedContext _context;

        [TestInitialize]
        public void Setup()
        {
            _context = new XrmFakedContext();
        }

        [TestMethod]
        public void Execute_ValidRequest_CallsExecuteProxy()
        {
            // Arrange
            var plugin = new TestProxyPlugin();
            var pluginContext = _context.GetDefaultPluginContext();

            // Act
            _context.ExecutePluginWith(pluginContext, plugin);

            // Assert
            Assert.IsTrue(((TestProxyPlugin)plugin).ExecuteProxyCalled);
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidPluginExecutionException))]
        public void Execute_NullContext_ThrowsException()
        {
            // Arrange
            var plugin = new TestProxyPlugin();
            var pluginContext = _context.GetDefaultPluginContext();
            pluginContext.InputParameters.Clear();

            // Act
            _context.ExecutePluginWith(pluginContext, plugin);

            // Assert - expects exception
        }

        // Test implementation of BaseProxyPlugin for testing
        private class TestProxyPlugin : BaseProxyPlugin
        {
            public bool ExecuteProxyCalled { get; private set; }

            public TestProxyPlugin() : base("TestProxy") { }

            protected override void ExecuteProxy(IServiceProvider serviceProvider, string correlationId)
            {
                ExecuteProxyCalled = true;
            }
        }
    }
}
```

5. Run tests:
```bash
dotnet test
```

**Validation**:
- All tests pass
- Code coverage > 80% for BaseProxyPlugin
- Tests verify error handling, logging, retry logic

---

## Deliverables

✅ Dataverse solution created: `Spaarke.CustomApiProxy`
✅ Plugin project created and configured
✅ BaseProxyPlugin implemented with:
  - Common Execute() logic
  - Authentication support (ClientCredentials, ManagedIdentity, ApiKey)
  - Configuration retrieval
  - Audit logging
  - Retry logic with exponential backoff
  - Telemetry integration
✅ Configuration entity created: `sprk_externalserviceconfig`
✅ Audit entity created: `sprk_proxyauditlog`
✅ TelemetryHelper implemented
✅ Unit tests written and passing

---

## Validation Checklist

Before proceeding to Phase 3:

- [ ] Solution builds without errors
- [ ] Assembly is signed with strong name key
- [ ] BaseProxyPlugin compiles and is abstract
- [ ] All authentication types implemented (ClientCredentials, ManagedIdentity, ApiKey)
- [ ] Configuration entity exists in Dataverse with all fields
- [ ] Audit entity exists in Dataverse with all fields
- [ ] Unit tests pass with >80% coverage
- [ ] Telemetry helper compiles and can be initialized
- [ ] Code follows C# coding conventions
- [ ] XML documentation comments added to all public members

---

## Next Steps

Proceed to **Phase 3: Spe.Bff.Api Proxy Implementation**

**Phase 3 will**:
- Implement operation-specific plugins (Download, Delete, Replace, Upload)
- Create Custom API definitions in Dataverse
- Register plugins with Plugin Registration Tool
- Add integration tests

---

## Knowledge Resources

### Internal Documentation
- [Phase 1 Architecture](./PHASE-1-ARCHITECTURE-AND-DESIGN.md)
- [Custom API Proxy Architecture](./CUSTOM-API-PROXY-ARCHITECTURE.md)
- [Dataverse Authentication Guide](../../../docs/DATAVERSE-AUTHENTICATION-GUIDE.md)

### External Resources
- [Dataverse Plugin Development](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/plug-ins)
- [Plugin Registration Tool](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/download-tools-nuget)
- [Azure.Identity Library](https://learn.microsoft.com/en-us/dotnet/api/azure.identity)
- [FakeXrmEasy Documentation](https://dynamicsvalue.github.io/fake-xrm-easy-docs/)

### Code References
- BaseProxyPlugin implementation: See Task 2.3 above
- Entity definitions: See Tasks 2.4 and 2.5 above
- Unit test examples: See Task 2.7 above

---

## Notes for AI Vibe Coding

**Key Implementation Details**:

1. **Plugin Sandbox Limitations**:
   - Plugins run in partial trust sandbox
   - Cannot use System.IO, System.Net (except HttpClient via Azure.Core)
   - Must use synchronous code (no async/await in plugin Execute)
   - Must complete within 2 minutes

2. **Authentication Best Practices**:
   - Cache tokens when possible (Azure.Identity handles this)
   - Use TokenRequestContext with specific scope
   - Handle token acquisition errors gracefully
   - Log authentication attempts for debugging

3. **Error Handling**:
   - Always throw InvalidPluginExecutionException for user-facing errors
   - Include meaningful error messages
   - Log full exception details for debugging
   - Don't expose internal implementation details in error messages

4. **Audit Logging**:
   - Redact sensitive data (secrets, tokens, file content)
   - Log correlation IDs for distributed tracing
   - Handle logging errors gracefully (don't fail operation)
   - Keep payload sizes reasonable (< 10KB)

5. **Testing**:
   - Use FakeXrmEasy for unit tests
   - Test both success and failure paths
   - Test retry logic with transient errors
   - Mock external HTTP calls
