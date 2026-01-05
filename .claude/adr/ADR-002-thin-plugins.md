# ADR-002: Dataverse Plugins Are Not an Execution Runtime (Concise)

> **Status**: Accepted
> **Domain**: Dataverse Extensibility
> **Last Updated**: 2026-01-05

---

## Decision

Dataverse plugins (C# or low-code) are **not used as an application runtime**.

All orchestration, business logic, integrations, and AI-driven processing **MUST** be implemented via:
- API endpoints (BFF / Dataverse Custom APIs)
- Asynchronous workers and job contracts
- Explicit service orchestration outside the Dataverse transaction pipeline

Plugins, if used at all, are **strictly limited** to minimal, in-transaction safeguards.

---

## Constraints

### ❌ MUST NOT (Prohibited in Plugins)

- **MUST NOT** implement business logic or workflow orchestration
- **MUST NOT** make HTTP, Graph, AI, or any remote I/O calls
- **MUST NOT** implement multi-entity coordination or side effects
- **MUST NOT** use retries, polling, or long-running execution
- **MUST NOT** depend on external state or services

**Low-code plugins are treated the same as C# plugins** — no exceptions.

### ⚠️ Restricted (Exception-Only)

Plugins **MAY** be used *only* when ALL of the following are true:
- Execution is synchronous and deterministic
- Work completes in < 50 ms p95
- Logic is limited to: validation, invariant enforcement, denormalization/projection, audit stamping
- No external calls of any kind
- No orchestration or branching logic

**Use of plugins requires explicit ADR exception approval.**

### ✅ MUST (When Plugins Are Used)

- **MUST** keep plugins < 200 LoC and < 50 ms p95
- **MUST** limit to in-transaction data inspection/mutation only
- **MUST** defer all side effects to APIs or workers
- **MUST** pass correlation IDs through API boundaries

---

## Preferred Patterns

| Concern | Required Mechanism |
|---------|-------------------|
| Business logic | BFF / Custom API |
| Orchestration | API + async workers |
| External services | BackgroundService / Azure Functions |
| Long-running work | Job contracts + queues (ADR-004) |
| Observability | Application Insights |
| Retries & idempotency | Worker infrastructure |
| Authorization | Endpoint-level filters (ADR-008) |

---

## Example (Allowed)

```csharp
// ValidationPlugin — invariant enforcement only
public sealed class DocumentValidationPlugin : IPlugin
{
    public void Execute(IServiceProvider serviceProvider)
    {
        var target = context.GetTarget<Entity>();

        if (string.IsNullOrWhiteSpace(target.GetAttributeValue<string>("sprk_name")))
            throw new InvalidPluginExecutionException("Document name is required.");

        target["sprk_validatedon"] = DateTime.UtcNow;
    }
}
```

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-001](ADR-001-minimal-api.md) | APIs and workers as primary runtime |
| [ADR-004](ADR-004-job-contract.md) | Uniform async job contracts |
| [ADR-008](ADR-008-endpoint-filters.md) | Endpoint-level authorization |

---

## Source Documentation

**Full ADR**: [docs/adr/ADR-002-no-heavy-plugins.md](../../docs/adr/ADR-002-no-heavy-plugins.md)

---

**Lines**: ~95
