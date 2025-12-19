# Plugin Structure Pattern

> **Domain**: Dataverse Plugins
> **Last Validated**: 2025-12-19
> **Source ADRs**: ADR-002

---

## Canonical Implementations

| File | Purpose |
|------|---------|
| `src/dataverse/plugins/Spaarke.CustomApiProxy/Plugins/Spaarke.Dataverse.CustomApiProxy/BaseProxyPlugin.cs` | Abstract base class |
| `src/dataverse/plugins/Spaarke.CustomApiProxy/Plugins/Spaarke.Dataverse.CustomApiProxy/GetFilePreviewUrlPlugin.cs` | Custom API proxy |
| `tests/unit/Spaarke.Plugins.Tests/ValidationPluginTests.cs` | Test patterns |

---

## Standard Plugin Structure (ADR-002 Compliant)

```csharp
public class DocumentValidationPlugin : IPlugin
{
    public void Execute(IServiceProvider serviceProvider)
    {
        // 1. Extract services
        var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
        var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
        var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
        var orgService = serviceFactory.CreateOrganizationService(context.UserId);

        try
        {
            // 2. Get target entity
            var target = (Entity)context.InputParameters["Target"];

            // 3. Validation only (no HTTP calls)
            ValidateRequiredFields(target);

            // 4. Audit stamping only
            target["modifiedby_stamp"] = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            throw new InvalidPluginExecutionException($"Validation failed: {ex.Message}", ex);
        }
    }
}
```

---

## BaseProxyPlugin Pattern (Custom API Proxy)

For plugins that must call BFF API (Custom API scenarios only):

```csharp
// BaseProxyPlugin.cs (lines 15-78)
public abstract class BaseProxyPlugin : IPlugin
{
    protected ITracingService TracingService { get; private set; }
    protected IOrganizationService OrganizationService { get; private set; }
    protected IPluginExecutionContext ExecutionContext { get; private set; }

    public void Execute(IServiceProvider serviceProvider)
    {
        // Extract services (lines 34-37)
        TracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
        var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
        ExecutionContext = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
        OrganizationService = serviceFactory.CreateOrganizationService(ExecutionContext.UserId);

        string correlationId = null;
        try
        {
            ValidateRequest();
            correlationId = LogRequest();
            ExecuteProxy(serviceProvider, correlationId);
            LogResponse(correlationId, true, null, duration);
        }
        catch (Exception ex)
        {
            LogResponse(correlationId, false, ex, duration);
            throw new InvalidPluginExecutionException($"{_pluginName} failed: {ex.Message}", ex);
        }
    }

    protected abstract void ExecuteProxy(IServiceProvider serviceProvider, string correlationId);
}
```

---

## Service Configuration Pattern

Retrieve external service config from Dataverse (lines 95-132):

```csharp
protected ExternalServiceConfig GetServiceConfig(string serviceName)
{
    var query = new QueryExpression("sprk_externalserviceconfig")
    {
        ColumnSet = new ColumnSet(true)
    };
    query.Criteria.AddCondition("sprk_name", ConditionOperator.Equal, serviceName);
    query.Criteria.AddCondition("sprk_isenabled", ConditionOperator.Equal, true);

    var results = OrganizationService.RetrieveMultiple(query);

    if (results.Entities.Count == 0)
        throw new InvalidPluginExecutionException($"External service config not found: {serviceName}");

    var entity = results.Entities[0];

    return new ExternalServiceConfig
    {
        BaseUrl = entity.GetAttributeValue<string>("sprk_baseurl"),
        AuthType = entity.GetAttributeValue<OptionSetValue>("sprk_authtype")?.Value ?? 0,
        // ... other config values
    };
}
```

---

## Audit Logging Pattern

Request/response logging with correlation ID (lines 205-275):

```csharp
private string LogRequest()
{
    var correlationId = Guid.NewGuid().ToString();

    var auditLog = new Entity("sprk_proxyauditlog");
    auditLog["sprk_operation"] = _pluginName;
    auditLog["sprk_correlationid"] = correlationId;
    auditLog["sprk_executiontime"] = DateTime.UtcNow;
    auditLog["sprk_userid"] = new EntityReference("systemuser", ExecutionContext.UserId);

    // Redact sensitive data before logging
    var sanitizedParams = RedactSensitiveData(ExecutionContext.InputParameters);
    auditLog["sprk_requestpayload"] = JsonConvert.SerializeObject(sanitizedParams);

    OrganizationService.Create(auditLog);
    return correlationId;
}
```

---

## Retry Pattern with Exponential Backoff

For transient errors in Custom API Proxy (lines 306-342):

```csharp
protected T ExecuteWithRetry<T>(Func<T> action, ExternalServiceConfig config)
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
            if (i == retryCount - 1 || !IsTransientError(ex))
                throw;

            // Exponential backoff
            var delay = retryDelay * (i + 1);
            System.Threading.Thread.Sleep(delay);
        }
    }
    throw new InvalidPluginExecutionException("Max retries exceeded");
}
```

---

## Key Points

1. **< 200 lines, < 50ms p95** - Standard plugins must be thin
2. **No HTTP in standard plugins** - Only Custom API Proxy can call BFF
3. **Correlation IDs** - Always pass through for tracing
4. **Redact sensitive data** - Before logging request/response
5. **Late-bound entities** - No early-bound code generation

---

## Related Patterns

- [Entity Operations](entity-operations.md) - CRUD with late-bound entities
- [Web API Client](web-api-client.md) - BFF-side Dataverse access

---

**Lines**: ~130
