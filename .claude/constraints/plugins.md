# Dataverse Plugin Constraints

> **Domain**: Dataverse Plugins
> **Source ADRs**: ADR-002
> **Last Updated**: 2025-12-18

---

## When to Load This File

Load when:
- Creating new Dataverse plugins
- Modifying existing plugin logic
- Reviewing plugin code
- Deciding where to place business logic

---

## MUST Rules

### Plugin Design (ADR-002)

- ✅ **MUST** keep standard plugins < 200 lines, < 50ms p95
- ✅ **MUST** limit standard plugins to: validation, denormalization/projection, audit stamping
- ✅ **MUST** handle orchestration in BFF/BackgroundService workers
- ✅ **MUST** pass correlation IDs through Custom API Proxy calls

---

## MUST NOT Rules

### Plugin Design (ADR-002)

- ❌ **MUST NOT** make HTTP/Graph calls in standard plugins (ValidationPlugin, ProjectionPlugin)
- ❌ **MUST NOT** implement business logic in plugins
- ❌ **MUST NOT** call external services from Custom API Proxy (BFF only)

---

## Quick Reference Patterns

### Standard Plugin (Validation/Stamping)

```csharp
public class DocumentValidationPlugin : IPlugin
{
    public void Execute(IServiceProvider provider)
    {
        var tracingService = (ITracingService)provider.GetService(typeof(ITracingService));
        var context = (IPluginExecutionContext)provider.GetService(typeof(IPluginExecutionContext));
        var target = (Entity)context.InputParameters["Target"];

        // ✅ Validation only
        if (string.IsNullOrEmpty(target.GetAttributeValue<string>("sprk_name")))
            throw new InvalidPluginExecutionException("Name required");

        // ✅ Stamping only
        target["modifiedby_stamp"] = DateTime.UtcNow;
    }
}
```

**See**: [Plugin Structure Pattern](../patterns/dataverse/plugin-structure.md)

### Custom API Proxy (BFF Calls Only)

```csharp
public class GetPreviewUrlPlugin : BaseProxyPlugin
{
    protected override async Task<object> ExecuteAsync(...)
    {
        // ✅ BFF call only (no external services)
        var response = await _httpClient.GetAsync(
            $"{_bffBaseUrl}/api/preview/{documentId}",
            correlationId);
        return MapResponse(response);
    }
}
```

### Plugin Type Decision Matrix

| Plugin Type | Allowed Operations | HTTP Calls |
|------------|-------------------|------------|
| ValidationPlugin | Field validation, required checks | ❌ Never |
| ProjectionPlugin | Denormalization, calculated fields | ❌ Never |
| AuditPlugin | Timestamp stamping, user stamping | ❌ Never |
| Custom API Proxy | BFF delegation only | ✅ BFF only |

---

## Pattern Files (Complete Examples)

- [Plugin Structure](../patterns/dataverse/plugin-structure.md) - BaseProxyPlugin, service extraction
- [Entity Operations](../patterns/dataverse/entity-operations.md) - Late-bound CRUD patterns
- [Web API Client](../patterns/dataverse/web-api-client.md) - BFF-side Dataverse access

---

## Source ADRs (Full Context)

| ADR | Focus | When to Load |
|-----|-------|--------------|
| [ADR-002](../adr/ADR-002-thin-plugins.md) | Thin plugin philosophy | Exception approval, architecture review |

---

**Lines**: ~95
**Purpose**: Single-file reference for all Dataverse plugin constraints

