# Dataverse Plugin Constraints

> **Domain**: Dataverse Extensibility
> **Source ADRs**: ADR-002
> **Last Updated**: 2026-01-05

---

## When to Load This File

Load when:
- Creating new Dataverse plugins
- Modifying existing plugin logic
- Reviewing plugin code
- Deciding where to place business logic

---

## Core Principle

**Dataverse plugins are NOT an execution runtime.**

Plugins exist only to protect data integrity—not to run the system. All orchestration, business logic, integrations, and AI-driven processing belong in APIs and workers.

---

## MUST NOT Rules (Prohibited)

### In ALL Plugins (ADR-002)

- ❌ **MUST NOT** implement business logic or workflow orchestration
- ❌ **MUST NOT** make HTTP, Graph, AI, or any remote I/O calls
- ❌ **MUST NOT** implement multi-entity coordination or side effects
- ❌ **MUST NOT** use retries, polling, or long-running execution
- ❌ **MUST NOT** depend on external state or services

**Low-code plugins are treated the same as C# plugins** — no exceptions.

---

## MUST Rules (When Plugins Are Used)

### Plugin Design (ADR-002)

- ✅ **MUST** keep plugins < 200 lines and < 50ms p95
- ✅ **MUST** limit to: validation, invariant enforcement, denormalization/projection, audit stamping
- ✅ **MUST** defer all side effects to APIs or workers
- ✅ **MUST** pass correlation IDs through API boundaries
- ✅ **MUST** require explicit ADR exception approval for any plugin use

---

## Restricted Use (Exception-Only)

Plugins **MAY** be used *only* when ALL of the following are true:

- Execution is synchronous and deterministic
- Work completes in < 50 ms p95
- Logic is limited to: validation, invariant enforcement, denormalization/projection, audit stamping
- No external calls of any kind
- No orchestration or branching logic

**Use of plugins requires explicit ADR exception approval.**

---

## Quick Reference Patterns

### Allowed: Validation/Stamping Plugin

```csharp
// ValidationPlugin — invariant enforcement only
public sealed class DocumentValidationPlugin : IPlugin
{
    public void Execute(IServiceProvider serviceProvider)
    {
        var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
        var target = context.InputParameters["Target"] as Entity;

        // ✅ Validation only
        if (string.IsNullOrWhiteSpace(target.GetAttributeValue<string>("sprk_name")))
            throw new InvalidPluginExecutionException("Document name is required.");

        // ✅ Stamping only
        target["sprk_validatedon"] = DateTime.UtcNow;
    }
}
```

### Where Business Logic Belongs

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

## Pattern Files (Complete Examples)

- [Plugin Structure](../patterns/dataverse/plugin-structure.md) - Thin plugin patterns

---

## Source ADRs (Full Context)

| ADR | Focus | When to Load |
|-----|-------|--------------|
| [ADR-002](../adr/ADR-002-thin-plugins.md) | Plugins not an execution runtime | Exception approval, architecture review |
| [ADR-001](../adr/ADR-001-minimal-api.md) | APIs and workers as primary runtime | When deciding where logic belongs |
| [ADR-004](../adr/ADR-004-job-contract.md) | Async job contracts | When deferring work from plugins |

---

**Lines**: ~100
**Purpose**: Single-file reference for all Dataverse plugin constraints
