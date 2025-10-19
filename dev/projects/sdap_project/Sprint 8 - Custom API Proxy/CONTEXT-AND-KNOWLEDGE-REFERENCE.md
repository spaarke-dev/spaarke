# Sprint 8: Context and Knowledge Reference

**Purpose**: This document provides comprehensive context and knowledge resources for AI-directed implementation of the Custom API Proxy. Use this as the "source of truth" for maintaining accurate context across all phases.

**Last Updated**: 2025-10-06

---

## Project Context

### SDAP Project Overview

**SDAP (SharePoint Document Access Proxy)** is a comprehensive solution enabling Dataverse-based Power Apps to manage files in SharePoint Embedded containers.

**Key Components**:
1. **Spe.Bff.Api**: .NET 8 Backend-for-Frontend API providing file operations against SharePoint Embedded
2. **Universal Dataset Grid**: PCF control (React 18 + Fluent UI v9) for document management in model-driven apps
3. **Custom API Proxy**: Dataverse server-side proxy enabling PCF controls to call external APIs
4. **Dataverse Entities**: Document metadata storage (sprk_document, sprk_container)

### Sprint 8 Context

**Problem Solved**: PCF controls cannot directly call external APIs because they don't have access to user authentication tokens by design in Power Apps model-driven apps.

**Solution**: Custom API Proxy infrastructure that:
- Runs on Dataverse server-side with system privileges
- Uses implicit authentication from user's Dataverse session
- Acquires service-to-service tokens for external APIs
- Proxies requests/responses between PCF and external APIs

**Why This Matters**:
- Critical prerequisite for Dataverse-to-SharePointEmbedded service
- Reusable pattern for ANY PCF-to-external-API integration
- Production-ready with security, monitoring, and error handling

---

## Architecture Quick Reference

### Component Layers

```
┌─────────────────────────────────────────────────────────┐
│ Presentation Layer                                      │
│                                                          │
│  Universal Dataset Grid (PCF Control)                   │
│  - React 18.2.0 + Fluent UI v9                         │
│  - CustomApiClient.ts                                   │
│  - Calls context.webAPI.execute()                       │
└─────────────────────────────────────────────────────────┘
                          │
                          │ Implicit Auth (Dataverse session)
                          ▼
┌─────────────────────────────────────────────────────────┐
│ Proxy Layer (Dataverse Server-Side)                    │
│                                                          │
│  Custom APIs:                                           │
│  - sprk_ProxyDownloadFile                              │
│  - sprk_ProxyDeleteFile                                │
│  - sprk_ProxyReplaceFile                               │
│  - sprk_ProxyUploadFile                                │
│                                                          │
│  Plugin Implementation:                                 │
│  - BaseProxyPlugin (abstract)                          │
│    - Authentication logic (Azure.Identity)             │
│    - Configuration management                          │
│    - Audit logging                                     │
│    - Error handling & retry logic                     │
│  - Operation-specific plugins (inherit base)           │
└─────────────────────────────────────────────────────────┘
                          │
                          │ Service-to-Service Auth (OAuth2)
                          ▼
┌─────────────────────────────────────────────────────────┐
│ External API Layer                                      │
│                                                          │
│  Spe.Bff.Api (.NET 8)                                  │
│  - JWT Bearer authentication                            │
│  - File operations (CRUD)                              │
│  - Integrates with SharePoint Embedded via Graph       │
└─────────────────────────────────────────────────────────┘
                          │
                          │ Microsoft Graph API
                          ▼
┌─────────────────────────────────────────────────────────┐
│ SharePoint Embedded                                     │
│                                                          │
│  File storage in containers                             │
└─────────────────────────────────────────────────────────┘
```

### Data Model Quick Reference

**External Service Configuration** (sprk_externalserviceconfig):
- Stores connection details for external APIs
- Includes auth settings (Client ID, Secret, Scope)
- Operational settings (timeout, retry count)
- Status tracking (enabled, health status)

**Proxy Audit Log** (sprk_proxyauditlog):
- Tracks all proxy operations
- Captures correlation IDs for distributed tracing
- Records request/response (with sensitive data redacted)
- Stores performance metrics (duration, status)

---

## Technology Stack

### Dataverse Plugin Development
- **Framework**: .NET Framework 4.6.2 (Plugin sandbox requirement)
- **SDK**: Microsoft.CrmSdk.CoreAssemblies 9.0.2.56
- **Authentication**: Azure.Identity 1.10.4
- **Serialization**: Newtonsoft.Json 13.0.3
- **Testing**: FakeXrmEasy 3.4.3

### PCF Control Development
- **Framework**: React 18.2.0
- **UI Library**: Fluent UI React v9
- **Language**: TypeScript 4.x
- **Build Tool**: PCF CLI (pac)
- **Bundler**: Webpack (via PCF tooling)

### Deployment
- **CLI**: Power Platform CLI (pac)
- **Source Control**: Git
- **Package Management**: Central package management (Directory.Packages.props)
  - ⚠️ Must disable before PCF deployment
  - ✅ Re-enable after deployment

---

## Key Concepts

### 1. Dataverse Custom APIs

**What**: Server-side API endpoints in Dataverse that can be called from client-side code (PCF, plugins, workflows, external apps).

**Why Use**:
- Implicit authentication (no token management in client)
- Run with system privileges
- Can execute complex server-side logic
- Integrate with external systems

**How Called from PCF**:
```typescript
const response = await context.webAPI.execute({
    getMetadata: () => ({
        boundParameter: null,
        operationType: 0, // Action
        operationName: 'sprk_ProxyDownloadFile',
        parameterTypes: {}
    }),
    DocumentId: 'doc-123',
    FileId: 'file-456'
});
```

### 2. Plugin Development

**Plugin Lifecycle**:
1. Event occurs (Custom API called)
2. Dataverse creates plugin sandbox
3. Plugin.Execute() called with IServiceProvider
4. Plugin executes logic
5. Plugin sets output parameters
6. Dataverse returns response to caller

**Plugin Context Services**:
- `ITracingService`: Logging (appears in Plugin Trace Log)
- `IOrganizationService`: CRUD operations on Dataverse entities
- `IPluginExecutionContext`: Input/output parameters, execution metadata

**Security Context**:
- Plugins run with system privileges by default
- Can impersonate users if needed
- Must manually validate user permissions when needed

### 3. Azure.Identity

**Purpose**: Unified authentication library for Azure services

**Common Credential Types**:
- `ClientSecretCredential`: App registration with client secret (service-to-service)
- `ManagedIdentityCredential`: Azure-managed identity (no secrets in code)
- `DefaultAzureCredential`: Tries multiple methods (dev + prod)

**Usage Pattern**:
```csharp
var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
var tokenRequestContext = new TokenRequestContext(new[] { scope });
var token = credential.GetToken(tokenRequestContext, default);
// token.Token contains JWT bearer token
```

### 4. Base64 Encoding for File Transfer

**Why**: Dataverse Custom API parameters are strings. Binary data (files) must be encoded as Base64 to transfer as strings.

**Overhead**: Base64 encoding increases size by ~33%
- 1 MB file → ~1.33 MB Base64 string
- Impacts memory and performance

**PCF Encoding**:
```typescript
const fileToBase64 = (file: File): Promise<string> => {
    return new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = () => {
            const dataUrl = reader.result as string;
            const base64 = dataUrl.split(',')[1]; // Remove "data:...;base64," prefix
            resolve(base64);
        };
        reader.readAsDataURL(file);
    });
};
```

**Plugin Decoding**:
```csharp
byte[] fileBytes = Convert.FromBase64String(base64Content);
```

### 5. PCF Context.webAPI

**Purpose**: PCF Framework API for calling Dataverse operations

**Key Methods**:
- `retrieveMultipleRecords()`: Query entity records
- `retrieveRecord()`: Get single record
- `createRecord()`: Create new record
- `updateRecord()`: Update existing record
- `deleteRecord()`: Delete record
- `execute()`: Call Custom API or action

**Authentication**: Implicit - uses user's Dataverse session

---

## File Locations and Paths

### Source Code

**Dataverse Plugin Project**:
```
src/dataverse/Spaarke.CustomApiProxy/
├── Plugins/
│   └── Spaarke.Dataverse.CustomApiProxy/
│       ├── BaseProxyPlugin.cs
│       ├── ProxyDownloadFilePlugin.cs
│       ├── ProxyDeleteFilePlugin.cs
│       ├── ProxyReplaceFilePlugin.cs
│       ├── ProxyUploadFilePlugin.cs
│       ├── TelemetryHelper.cs
│       └── Spaarke.Dataverse.CustomApiProxy.csproj
└── solution.xml
```

**PCF Control**:
```
src/controls/UniversalDatasetGrid/
├── UniversalDatasetGrid/
│   ├── components/
│   │   ├── UniversalDatasetGridRoot.tsx
│   │   ├── CommandBar.tsx
│   │   └── ErrorNotification.tsx
│   ├── services/
│   │   ├── CustomApiClient.ts
│   │   └── SdapApiClientFactory.ts.deprecated
│   ├── types/
│   │   ├── index.ts
│   │   └── customApi.ts
│   ├── index.ts
│   └── ControlManifest.Input.xml
├── package.json
└── tsconfig.json
```

**Documentation**:
```
dev/projects/sdap_project/Sprint 8 - Custom API Proxy/
├── SPRINT-8-OVERVIEW.md
├── CUSTOM-API-PROXY-ARCHITECTURE.md
├── PHASE-1-ARCHITECTURE-AND-DESIGN.md
├── PHASE-2-DATAVERSE-FOUNDATION.md
├── PHASE-3-PROXY-IMPLEMENTATION.md
├── PHASE-4-PCF-INTEGRATION.md
├── PHASE-5-DEPLOYMENT-AND-TESTING.md
├── PHASE-6-DOCUMENTATION-AND-HANDOFF.md
├── DEPLOYMENT-RUNBOOK.md
├── EXTENSIBILITY-GUIDE.md
├── TROUBLESHOOTING-GUIDE.md
└── CONTEXT-AND-KNOWLEDGE-REFERENCE.md (this file)
```

### Configuration Files

**Azure Key Vault Secrets**:
- `API-APP-ID`: Client ID for Spe.Bff.Api
- `Dataverse--ClientSecret`: Client secret for Dataverse-to-API auth
- Vault: `spaarke-dev-kv`

**Dataverse Entities** (spaarkedev1.crm.dynamics.com):
- `sprk_externalserviceconfig`: External API configurations
- `sprk_proxyauditlog`: Audit logs for proxy operations
- `sprk_document`: Document metadata
- `sprk_container`: Container metadata

---

## Environment Details

### Development Environment

**Dataverse**:
- URL: https://spaarkedev1.crm.dynamics.com
- API URL: https://spaarkedev1.api.crm.dynamics.com/api/data/v9.2/
- Organization ID: [Available in environment]

**Spe.Bff.Api**:
- URL: https://spe-api-dev-67e2xz.azurewebsites.net
- Authentication: JWT Bearer (OAuth2 Client Credentials)
- Scope: api://[API_APP_ID]/.default

**Azure**:
- Subscription: [Subscription name]
- Resource Group: [Resource group]
- Key Vault: spaarke-dev-kv
- Tenant ID: [Available via `az account show`]

### Local Development

**Prerequisites**:
- Node.js 18.x (for PCF)
- .NET Framework 4.6.2 SDK (for plugins)
- Power Platform CLI (`pac`)
- Plugin Registration Tool
- Visual Studio or VS Code

**Build Commands**:
```bash
# PCF Control
cd src/controls/UniversalDatasetGrid
npm install
npm run build          # Development build
npm run build:prod     # Production build (minified)

# Dataverse Plugin
cd src/dataverse/Spaarke.CustomApiProxy/Plugins/Spaarke.Dataverse.CustomApiProxy
dotnet build           # Debug build
dotnet build -c Release # Release build (for deployment)
```

---

## Authentication Flows

### Flow 1: User → PCF → Custom API

```
1. User authenticated to Power Apps (Azure AD SSO)
2. User session established in Dataverse
3. PCF control calls context.webAPI.execute()
4. Dataverse validates user session (implicit)
5. Dataverse executes Custom API plugin
6. Plugin runs with system privileges
7. Response returned to PCF
```

**Key Point**: PCF never handles authentication tokens

### Flow 2: Custom API → External API

```
1. Plugin retrieves external service config from Dataverse
2. Plugin creates ClientSecretCredential with service principal
3. Plugin calls credential.GetToken(scope)
4. Azure AD validates service principal, issues token
5. Plugin adds token to HTTP Authorization header
6. Plugin calls external API
7. External API validates token
8. Response returned to plugin
```

**Key Point**: Service-to-service authentication, no user context

### Flow 3: Spe.Bff.Api → SharePoint Embedded

```
1. Spe.Bff.Api receives request with bearer token
2. API validates token (JWT signature, claims)
3. API uses token for Microsoft Graph API call (OBO if needed)
4. Graph API accesses SharePoint Embedded container
5. File operation executed
6. Response returned through layers
```

---

## Common Commands Reference

### Power Platform CLI

```bash
# Authentication
pac auth create --name <profile> --url <environment-url>
pac auth list
pac auth select --name <profile>

# Solutions
pac solution init --publisher-name <name> --publisher-prefix <prefix>
pac solution pack --zipfile <output.zip> --packagetype Managed
pac solution import --path <solution.zip> --async
pac solution export --name <solution-name> --path <output.zip>
pac solution list

# PCF Controls
pac pcf init --namespace <ns> --name <name> --template dataset
pac pcf push --publisher-prefix <prefix>

# Tools
pac tool prt  # Launch Plugin Registration Tool
```

### Azure CLI

```bash
# Key Vault
az keyvault secret show --vault-name <vault> --name <secret-name> --query value -o tsv
az keyvault secret set --vault-name <vault> --name <secret-name> --value <value>

# Account Info
az account show
az account show --query tenantId -o tsv
```

### Git

```bash
# Feature branch workflow
git checkout -b sprint-8-phase-2
git add .
git commit -m "feat: Implement BaseProxyPlugin with Azure.Identity support"
git push origin sprint-8-phase-2

# Tagging releases
git tag -a v2.1.0 -m "Sprint 8: Custom API Proxy complete"
git push origin v2.1.0
```

---

## Testing Strategies

### Unit Testing (Plugins)

**Framework**: MSTest + FakeXrmEasy

**Pattern**:
```csharp
[TestClass]
public class ProxyDownloadFilePluginTests
{
    private XrmFakedContext _context;

    [TestInitialize]
    public void Setup()
    {
        _context = new XrmFakedContext();
    }

    [TestMethod]
    public void Execute_ValidRequest_ReturnsFileContent()
    {
        // Arrange
        var plugin = new ProxyDownloadFilePlugin();
        var pluginContext = _context.GetDefaultPluginContext();
        pluginContext.InputParameters["DocumentId"] = "test-id";

        // Act
        _context.ExecutePluginWith(pluginContext, plugin);

        // Assert
        Assert.IsTrue(pluginContext.OutputParameters.Contains("FileContent"));
    }
}
```

### Integration Testing (Custom APIs)

**Tool**: Plugin Registration Tool or OrganizationService

**Pattern**:
```csharp
var request = new OrganizationRequest("sprk_ProxyDownloadFile");
request["DocumentId"] = "doc-123";
request["FileId"] = "file-456";

var response = serviceClient.Execute(request);
var fileContent = response["FileContent"] as string;
```

### End-to-End Testing (PCF)

**Manual Testing**:
1. Open model-driven app
2. Navigate to Documents view with Universal Dataset Grid
3. Test all operations (Download, Delete, Replace, Upload)
4. Verify browser console for errors
5. Check audit logs in Dataverse

**Validation**:
- Operation succeeds
- User feedback appropriate
- Data updated correctly
- Performance acceptable

---

## Debugging Techniques

### Debugging Plugins

**1. Plugin Trace Logs**:
- Settings → System → Administration → System Settings
- Customization tab → Enable logging: All
- View: Settings → Customizations → Plugin Trace Log

**2. Remote Debugging**:
- Use Plugin Registration Tool → Debug
- Register profiler step
- Download profile log
- Open in Visual Studio with symbols

**3. Tracing**:
```csharp
TracingService.Trace("Debug message: {0}", value);
// View in Plugin Trace Log
```

### Debugging PCF Controls

**1. Browser DevTools**:
- F12 → Console tab
- Filter by "CustomApiClient" or component name
- Network tab for API calls

**2. React DevTools**:
- Install React DevTools extension
- Inspect component hierarchy
- View props and state

**3. Console Logging**:
```typescript
console.log('[CustomApiClient]', 'Operation', { details });
// Use consistent prefixes for easy filtering
```

### Debugging Authentication

**1. Test Token Acquisition**:
```bash
curl -X POST https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token \
  -d "client_id={client-id}" \
  -d "client_secret={secret}" \
  -d "scope=api://{app-id}/.default" \
  -d "grant_type=client_credentials"
```

**2. Decode JWT Token**:
- Use https://jwt.ms to decode token
- Verify claims (aud, iss, exp, roles)

**3. Test API with Token**:
```bash
curl -X GET https://spe-api.../api/files/{id}/download \
  -H "Authorization: Bearer {token}"
```

---

## Known Issues and Workarounds

### Issue 1: Directory.Packages.props Blocks PCF Deployment

**Symptom**: PCF deployment fails or changes don't appear

**Workaround**:
```bash
mv Directory.Packages.props Directory.Packages.props.disabled
pac pcf push --publisher-prefix sprk
mv Directory.Packages.props.disabled Directory.Packages.props
```

### Issue 2: Base64 Size Limits

**Symptom**: Large file operations fail with timeout or memory errors

**Limitation**: Dataverse Custom API parameters ~5-10 MB practical limit

**Workaround**:
- Recommend file size limits in UI
- For larger files, use chunked upload pattern
- Or upload directly to SPE, notify Dataverse after

### Issue 3: Browser Cache Issues

**Symptom**: PCF control changes not visible after deployment

**Workaround**:
- Hard refresh (Ctrl+Shift+R)
- Incognito mode
- Clear site data in DevTools

---

## Security Considerations

### Secrets Management

**DO**:
- ✅ Store secrets in Azure Key Vault
- ✅ Use Dataverse secured fields for config
- ✅ Rotate secrets regularly
- ✅ Use Managed Identity when possible

**DON'T**:
- ❌ Store secrets in code
- ❌ Log secrets or tokens
- ❌ Expose secrets in error messages
- ❌ Hard-code credentials

### Authorization

**Pattern**: Always validate user permissions in plugin

```csharp
// Validate user can access document
try {
    var document = OrganizationService.Retrieve(
        "sprk_document",
        new Guid(documentId),
        new ColumnSet("sprk_documentid")
    );
} catch {
    throw new InvalidPluginExecutionException("Access denied");
}
```

### Audit Logging

**Pattern**: Redact sensitive data

```csharp
// DON'T log this:
auditLog["sprk_requestpayload"] = JsonConvert.SerializeObject(request);

// DO this:
var sanitized = RedactSensitiveData(request);
auditLog["sprk_requestpayload"] = JsonConvert.SerializeObject(sanitized);
```

---

## Performance Optimization

### Plugin Performance

**Tips**:
- Minimize OrganizationService calls
- Use ColumnSet to retrieve only needed fields
- Cache configuration data
- Use early-bound types for better performance

**Monitoring**:
- Track duration in audit logs
- Use Application Insights for telemetry
- Set alerting on slow operations (>5 seconds)

### PCF Performance

**Tips**:
- Minimize re-renders (React.memo, useMemo)
- Lazy load components
- Debounce user input
- Optimize bundle size

**Monitoring**:
- Use React DevTools Profiler
- Monitor bundle size (`npm run build:prod`)
- Test with large datasets

---

## Version History

| Version | Date | Changes | Phase |
|---------|------|---------|-------|
| 2.0.7 | Sprint 7A | Initial PCF deployment (pre-authentication fix) | N/A |
| 2.0.8 | Sprint 7A | Debug version with test banners | N/A |
| 2.0.9 | Sprint 7A | Final attempt with direct API (blocked by auth) | N/A |
| 2.1.0 | Sprint 8 | Custom API Proxy integration | Phase 4 |

---

## Critical Learning Resources

### Microsoft Learn

**Dataverse**:
- [Custom API Documentation](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/custom-api)
- [Plugin Development Guide](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/plug-ins)
- [Best Practices](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/best-practices/)

**PCF**:
- [PCF Overview](https://learn.microsoft.com/en-us/power-apps/developer/component-framework/overview)
- [Dataset Component](https://learn.microsoft.com/en-us/power-apps/developer/component-framework/sample-controls/data-set-grid-control)
- [WebAPI Reference](https://learn.microsoft.com/en-us/power-apps/developer/component-framework/reference/webapi)

**Azure**:
- [Azure.Identity Library](https://learn.microsoft.com/en-us/dotnet/api/azure.identity)
- [OAuth 2.0 Client Credentials Flow](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-client-creds-grant-flow)

### Internal Documentation

**Sprint History**:
- Sprint 3: Dataverse entities and data model
- Sprint 4: Spe.Bff.Api implementation (initially with DefaultAzureCredential, later fixed with ServiceClient)
- Sprint 5: Initial PCF control development
- Sprint 6: PCF control deployment and UI testing
- Sprint 7: Download/Delete/Replace operations (blocked by authentication)
- Sprint 8: Custom API Proxy (this sprint)

**Key Documents**:
- [DATAVERSE-AUTHENTICATION-GUIDE.md](../../../docs/DATAVERSE-AUTHENTICATION-GUIDE.md) - Comprehensive Dataverse auth patterns
- [KM-PCF-CONTROL-STANDARDS.md](../../../docs/KM-PCF-CONTROL-STANDARDS.md) - PCF development standards
- [Spaarke Codebase Assessment](../../../docs/spaarke-codebase-assessment.md) - Overall project architecture

---

## Contact and Support

### Team Roles

- **Technical Lead**: [Name]
- **Architect**: [Name]
- **Dataverse Lead**: [Name]
- **PCF Lead**: [Name]
- **Security Lead**: [Name]

### Getting Help

**For implementation questions**:
1. Check this context document
2. Check phase-specific documentation
3. Check troubleshooting guide
4. Check Microsoft Learn documentation
5. Contact team lead

**For bugs or issues**:
1. Check troubleshooting guide first
2. Collect diagnostic information
3. Create GitHub issue (if applicable)
4. Contact support

---

## AI Vibe Coding Guidelines

### Context Maintenance

**When starting new implementation**:
1. Read this context document first
2. Read relevant phase documentation
3. Check related sprint documentation
4. Review code in referenced file paths

**When encountering issues**:
1. Check troubleshooting guide
2. Review authentication flows
3. Check known issues and workarounds
4. Consult external documentation

**When making changes**:
1. Update phase documentation
2. Update this context doc if architecture changes
3. Update troubleshooting guide with new issues
4. Document decisions in phase docs

### Code Quality Standards

**C# (Plugins)**:
- Follow Microsoft C# coding conventions
- Use XML documentation comments
- Comprehensive error handling with meaningful messages
- Log at key points with TracingService
- Unit test coverage >80%

**TypeScript (PCF)**:
- Use TypeScript strict mode
- Define interfaces for all data structures
- Use async/await for asynchronous operations
- Handle errors at call site
- Console logging with consistent prefixes

**Documentation**:
- Clear headings and structure
- Code examples for complex concepts
- Link to related documentation
- Keep up-to-date with implementation

---

## Appendix: Key Code Patterns

### Pattern 1: BaseProxyPlugin Structure

```csharp
public abstract class BaseProxyPlugin : IPlugin
{
    protected ITracingService TracingService { get; private set; }
    protected IOrganizationService OrganizationService { get; private set; }
    protected IPluginExecutionContext ExecutionContext { get; private set; }

    public void Execute(IServiceProvider serviceProvider)
    {
        // 1. Initialize services
        // 2. Validate request
        // 3. Log request (get correlation ID)
        // 4. Call ExecuteProxy (derived class)
        // 5. Log response
        // 6. Handle errors
    }

    protected abstract void ExecuteProxy(IServiceProvider serviceProvider, string correlationId);
    protected ExternalServiceConfig GetServiceConfig(string serviceName) { }
    protected HttpClient CreateAuthenticatedHttpClient(ExternalServiceConfig config) { }
}
```

### Pattern 2: Custom API Client Usage

```typescript
const apiClient = new CustomApiClient(context);

// Download file
const blob = await apiClient.downloadFile({
    DocumentId: 'doc-123',
    FileId: 'file-456'
});

// Upload file
const file = /* File object from input */;
const base64 = await CustomApiClient.fileToBase64(file);
const result = await apiClient.uploadFile({
    DocumentId: 'doc-123',
    FileContent: base64,
    FileName: file.name,
    ContentType: file.type
});
```

### Pattern 3: Error Handling

```csharp
// Plugin
try
{
    // Execute operation
}
catch (Exception ex)
{
    TracingService.Trace($"Error: {ex.Message}");
    throw new InvalidPluginExecutionException(
        "User-friendly error message",
        ex
    );
}
```

```typescript
// PCF
try {
    const result = await apiClient.operation();
} catch (error: any) {
    console.error('[Component] Operation failed', error);
    setErrorMessage(error.message || 'Operation failed');
}
```

---

**End of Context and Knowledge Reference**

This document should be treated as the "source of truth" for Sprint 8 implementation. Update as needed to maintain accuracy.
