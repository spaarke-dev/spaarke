# ADR-002: Dataverse Plugins Are Not an Execution Runtime

| Field | Value |
|-------|-------|
| Status | **Accepted** |
| Date | 2025-09-27 |
| Updated | 2026-01-05 |
| Authors | Spaarke Engineering |

## Context

Dataverse plugins execute **inside the database transaction boundary** and are therefore fundamentally ill-suited for:

- Long-running or asynchronous operations
- External service calls (Microsoft Graph, SharePoint Embedded, AI services, HTTP APIs)
- Complex orchestration or multi-step workflows
- Robust observability, retries, and fault isolation
- Scalable, testable, CI/CD-driven SaaS architectures

At product scale, plugins introduce:

- Transaction contention and service-protection throttling
- Opaque failures with limited diagnostics
- High operational risk and deployment friction
- Tight coupling between persistence and execution

Heavy business logic and remote I/O in Dataverse plugins leads to long transactions, service-protection throttling, opaque failures, and limited observability.

## Decision

Dataverse plugins (C# or low-code) are **not used as an application runtime**.

All orchestration, business logic, integrations, and AI-driven processing **MUST** be implemented via:

- API endpoints (BFF / Dataverse Custom APIs)
- Asynchronous workers and job contracts
- Explicit service orchestration outside the Dataverse transaction pipeline

Dataverse plugins, if used at all, are **strictly limited** to minimal, in-transaction safeguards.

**Spaarke treats Dataverse as a data platform, not as an execution engine.**
All non-trivial behavior belongs in explicit, testable, observable services.

## Policy

### ❌ Prohibited

The following are **explicitly disallowed** in Dataverse plugins (including low-code plugins):

- Business logic or workflow orchestration
- HTTP, Graph, AI, or other remote I/O
- Multi-entity coordination or side effects
- Retries, polling, or long-running execution
- Dependencies on external state or services

**Low-code plugins are treated the same as C# plugins and are not an exception to this policy.**

### ⚠️ Restricted (Exception-Only)

Plugins **MAY** be used *only* when all of the following are true:

- Execution is synchronous and deterministic
- Work completes in < 50 ms p95
- Logic is limited to:
  - Validation
  - Invariant enforcement
  - Denormalization / projection
  - Audit or telemetry stamping
- No external calls of any kind
- No orchestration or branching logic

**Use of plugins requires explicit ADR exception approval.**

### ✅ Preferred Patterns

| Concern | Required Mechanism |
|---------|-------------------|
| Business logic | BFF / Custom API |
| Orchestration | API + async workers |
| External services | BackgroundService / Azure Functions |
| Long-running work | Job contracts + queues |
| Observability | Application Insights |
| Retries & idempotency | Worker infrastructure |
| Authorization | Endpoint-level filters |

## Constraints

### MUST

- Plugins must remain < 200 LOC and < 50 ms p95
- Plugins may only inspect and mutate in-transaction data
- All side effects must be deferred to APIs or workers
- Correlation IDs must flow through API boundaries

### MUST NOT

- No HTTP or Graph calls
- No AI calls or file operations
- No retries or polling
- No orchestration logic

## Example (Allowed)

```csharp
// ValidationPlugin — invariant enforcement only
public sealed class DocumentValidationPlugin : IPlugin
{
    public void Execute(IServiceProvider serviceProvider)
    {
        var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
        var target = context.InputParameters["Target"] as Entity;

        if (string.IsNullOrWhiteSpace(target.GetAttributeValue<string>("sprk_name")))
            throw new InvalidPluginExecutionException("Document name is required.");

        target["sprk_validatedon"] = DateTime.UtcNow;
    }
}
```

## Explicit Non-Goals

This architecture does **not** optimize for:

- Citizen-developer extensibility
- Inline business logic customization
- Power Automate-style composition inside Dataverse

Those concerns are addressed through **configuration, APIs, and workflows**, not plugins.

## Consequences

### Positive

- Clear execution boundaries
- Predictable performance characteristics
- Full observability and debuggability
- CI/CD-friendly deployment model
- AI- and integration-ready architecture

### Trade-offs

- Less Dataverse-native extensibility
- Higher reliance on pro-code services
- Stronger architectural discipline required

**These trade-offs are intentional.**

## Success Metrics

| Metric | Target |
|--------|--------|
| Plugin-originated remote I/O | Zero |
| Service-protection errors | Zero plugin-originated |
| Plugin execution p95 | < 50 ms |

## Compliance

**Architecture tests:** `tests/Spaarke.ArchTests/ADR002_PluginTests.cs` validates no prohibited dependencies.

**Code review checklist:**
- [ ] No `HttpClient` in any plugin
- [ ] No external service calls
- [ ] Correlation ID passed through
- [ ] Plugin logic is validation/projection only

## AI-Directed Coding Guidance

- If you need orchestration, retries, external calls, or long-running work: implement it in the BFF (`src/server/api/Sprk.Bff.Api/`) and/or a worker (ADR-004), not in a plugin.
- Keep plugins strictly synchronous and local: validation, stamping, projection/denormalization only.
- Treat Dataverse as a data platform—all execution logic belongs in explicit services.

---

## Related ADRs

| ADR | Relationship |
|-----|-------------|
| ADR-001 | APIs and workers as primary runtime |
| ADR-004 | Uniform async job contracts |
| ADR-008 | Endpoint-level authorization |

---

## Summary

Dataverse plugins are treated as **guardrails**, not engines.

Spaarke's execution model is **API-first, async-by-default, and AI-forward**.
Plugins exist only to protect data integrity—not to run the system.

---

## Related AI Context

**AI-Optimized Versions** (load these for efficient context):
- [ADR-002 Concise](../../.claude/adr/ADR-002-thin-plugins.md) - ~95 lines
- [Plugins Constraints](../../.claude/constraints/plugins.md) - MUST/MUST NOT rules

**When to load this full ADR**: Historical context, exceptions policy, compliance checklist details.
