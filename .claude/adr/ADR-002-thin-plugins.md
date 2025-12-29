# ADR-002: Thin Plugins (Concise)

> **Status**: Accepted
> **Domain**: Dataverse Plugins
> **Last Updated**: 2025-12-18

---

## Decision

Keep Dataverse plugins **thin**: validation, stamping, projection only. No orchestration or remote I/O in standard plugins.

**Rationale**: Heavy plugins cause long transactions, service-protection throttling, opaque failures, and limited observability.

---

## Constraints

### ✅ MUST

- **MUST** keep standard plugins < 200 lines, < 50ms p95
- **MUST** limit to validation, denormalization/projection, audit stamping
- **MUST** handle orchestration in BFF/BackgroundService workers
- **MUST** pass correlation IDs through Custom API Proxy calls

### ❌ MUST NOT

- **MUST NOT** make HTTP/Graph calls in standard plugins (ValidationPlugin, ProjectionPlugin)
- **MUST NOT** implement business logic in plugins
- **MUST NOT** call external services from Custom API Proxy (BFF only)

---

## Implementation Patterns

### Standard Plugin (Allowed)

```csharp
// ValidationPlugin - sync validation only
public class DocumentValidationPlugin : IPlugin
{
    public void Execute(IServiceProvider provider)
    {
        var target = context.GetParameterCollection<Entity>("Target");

        // ✅ Validation only
        if (string.IsNullOrEmpty(target.GetAttributeValue<string>("name")))
            throw new InvalidPluginExecutionException("Name required");

        // ✅ Stamping only
        target["modifiedby_stamp"] = DateTime.UtcNow;
    }
}
```

### Custom API Proxy (Exception - BFF calls only)

```csharp
// BaseProxyPlugin - HTTP to BFF only
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

**See**: [Plugin Structure Pattern](../patterns/dataverse/plugin-structure.md)

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-001](ADR-001-minimal-api.md) | Orchestration in BFF/workers |
| [ADR-004](ADR-004-job-contract.md) | Async work via job contract |

---

## Source Documentation

**Full ADR**: [docs/adr/ADR-002-no-heavy-plugins.md](../../docs/adr/ADR-002-no-heavy-plugins.md)

---

**Lines**: ~80
