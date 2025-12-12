# ADR-010: Dependency Injection minimalism and feature modules

| Field | Value |
|-------|-------|
| Status | **Accepted** |
| Date | 2025-09-27 |
| Updated | 2025-12-04 |
| Authors | Spaarke Engineering |

## Context

The BFF accumulated 20+ service registrations driven by interface sprawl, duplicate clients, and helper services. This hurts readability and invites drift.

## Decision

| Rule | Description |
|------|-------------|
| **Concretes by default** | Register concretes unless genuine seam exists |
| **Two core seams only** | `IAccessDataSource` and `IEnumerable<IAuthorizationRule>` |
| **Feature modules** | Group registrations via extension methods (`AddSpaarkeCore`, `AddDocumentsModule`, `AddWorkersModule`) |
| **One HttpClient per upstream** | Single typed `HttpClient` for Dataverse |
| **Graph client factory singleton** | `IGraphClientFactory` is singleton; creates `GraphServiceClient` with centralized resilience |
| **Options pattern** | `AddOptions<T>().Bind().ValidateOnStart()` |
| **Redis only** | `IDistributedCache` + small `RequestCache`; no hybrid cache services |

## Consequences

**Positive:**
- DI section drops to ~15 lines; wiring becomes obvious
- Easier for AI agents and reviewers to see the whole composition at a glance

**Negative:**
- Some unit tests may need refactoring to depend on the remaining seams

## Alternatives Considered

Registering interfaces for every service. **Rejected** as ceremony without payoff.

## Operationalization

### Removed Registrations

| Remove | Reason |
|--------|--------|
| `IUacService` | Replaced by `AuthorizationService` + rules |
| `IDataverseSecurityService` | Merged into `IAccessDataSource` |
| `IResourceStore` | Replaced by concrete `SpeFileStore` |
| Hybrid cache services | ADR-009 Redis-first |
| Duplicate HttpClients | Single typed client per upstream |

### Feature Module Extensions

| Extension | Contents |
|-----------|----------|
| `AddSpaarkeCore()` | Options, auth service, access data source, caching |
| `AddDocumentsModule()` | Authorization rules, SpeFileStore |
| `AddWorkersModule()` | BackgroundService, ServiceBusProcessor |

### Target State

```csharp
// Program.cs DI section (~15 lines)
builder.Services
    .AddSpaarkeCore(builder.Configuration)
    .AddDocumentsModule()
    .AddWorkersModule();
```

## Exceptions

Add interfaces only for:
- Process boundaries
- Multiple runtime implementations
- External extension points

Any new interface requires an ADR when doing so.

## Success Metrics

| Metric | Target |
|--------|--------|
| DI registrations | ≤ ~15 non-framework lines |
| Build | Passes |
| Tests | Pass |
| Workers | Start correctly |
| Graph injection | Never direct to controllers |

## Compliance

**Code review checklist:**
- [ ] New services registered as concrete (unless seam documented)
- [ ] No duplicate HttpClient registrations
- [ ] Options validated on start
- [ ] Feature module extension used (not inline registration)

## AI-Directed Coding Guidance

- Prefer adding services via feature modules (`AddSpaarkeCore`, `AddDocumentsModule`, `AddWorkersModule`) instead of inlining registrations.
- Do not inject `GraphServiceClient` into endpoints; use `SpeFileStore` or `IGraphClientFactory` inside the storage layer.
- Add a new interface only when it is a true seam (multiple implementations, extension point, or process boundary).
