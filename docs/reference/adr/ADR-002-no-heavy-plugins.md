# ADR-002: Keep Dataverse plugins thin; no orchestration in plugins

| Field | Value |
|-------|-------|
| Status | **Accepted** |
| Date | 2025-09-27 |
| Updated | 2025-12-04 |
| Authors | Spaarke Engineering |

## Context

Heavy business logic and remote I/O in Dataverse plugins leads to long transactions, service-protection throttling, opaque failures, and limited observability. We need platform guardrails, not workflow engines, inside Dataverse.

## Decision

| Rule | Description |
|------|-------------|
| **Validation only** | Plugins perform only synchronous validation, denormalization/projection, and audit stamping |
| **No remote I/O** | No HTTP/Graph calls or long-running logic inside standard plugins |
| **BFF orchestration** | Orchestration resides in the BFF/API (Minimal API) and BackgroundService workers |

## Consequences

**Positive:**
- Short, reliable transactions and fewer service-protection issues
- Unified retries, telemetry, correlation, and error handling in the BFF/workers

**Negative:**
- Slightly more code in the BFF to coordinate multi-step operations

## Alternatives Considered

Complex plugins and custom workflow activities. **Rejected** due to observability, scale, and ISV deployment risks.

## Operationalization

### Standard Plugins (ValidationPlugin, ProjectionPlugin)

| Constraint | Value |
|------------|-------|
| Max LoC | ~200 lines each |
| Execution p95 | < 50 ms |
| Remote I/O | ❌ Not allowed |
| External data | Emit Service Bus command → worker completes via API |

### Custom API Proxy Plugins (BaseProxyPlugin)

| Constraint | Value |
|------------|-------|
| HTTP calls | ✅ Allowed to **internal BFF API only** |
| External services | ❌ Not allowed (BFF handles external calls) |
| Timeout | 30s default |
| Requirements | Correlation IDs, audit logging, graceful timeout handling |

## Exceptions

1. **Atomic multi-row writes** without external calls may use a thin Dataverse Custom API invoked by the BFF.

2. **Custom API Proxy plugins** (`BaseProxyPlugin`, `GetFilePreviewUrlPlugin`) may make HTTP calls to the BFF API. These bridge Dataverse UX to BFF services and must:
   - Target only internal BFF API endpoints (not external services)
   - Include correlation IDs and audit logging
   - Handle timeouts gracefully (default 30s)
   - Contain no business logic beyond parameter mapping

## Success Metrics

| Metric | Target |
|--------|--------|
| Plugin-originated remote I/O | Zero (except Custom API Proxy to BFF) |
| Service-protection errors | Zero plugin-originated |
| Plugin execution p95 | < 50 ms |

## Compliance

**Architecture tests:** `ADR002_PluginTests.cs` validates no prohibited dependencies.

**Code review checklist:**
- [ ] No `HttpClient` in ValidationPlugin/ProjectionPlugin
- [ ] Custom API Proxy targets BFF only
- [ ] Correlation ID passed through
