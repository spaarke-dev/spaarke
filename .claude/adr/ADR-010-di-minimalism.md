# ADR-010: DI Minimalism (Concise)

> **Status**: Accepted
> **Domain**: API Architecture
> **Last Updated**: 2025-12-18

---

## Decision

Keep dependency injection **minimal and concrete**. Register concretes unless a genuine seam exists.

**Rationale**: Excessive interfaces and registrations hurt readability and invite drift. ~15 non-framework DI lines keeps composition obvious for developers and AI agents.

### Phase 5 baseline (2026-05-26)

The `sdap-bff-api-remediation-fix` project measured **265 DI registrations** at Phase 3 baseline (task 038) — far above the "≤15 non-framework lines" principle. Phase 4 Outcome E added **4 facade registrations** via `AddPublicContractsFacade` (within the expected +4/+8 delta per `plan.md`). The +N elevation is a known violation accepted by the project; the principle of "feature-module extensions" (one module = one `Add*()` call from `Program.cs`) is preserved. See `projects/sdap-bff-api-remediation-fix/EXECUTION-LOG.md` Phase 3 + Phase 4 entries for evidence. Reducing the 265 baseline back toward the principle is OUT OF SCOPE for this project — flagged as a separate architectural follow-up.

---

## Constraints

### ✅ MUST

- **MUST** register concretes by default (not interfaces)
- **MUST** use feature module extensions (`AddSpaarkeCore`, `AddDocumentsModule`, `AddWorkersModule`)
- **MUST** use Options pattern with `ValidateOnStart()`
- **MUST** keep DI registrations ≤15 non-framework lines
- **MUST** use single typed `HttpClient` per upstream service

### ❌ MUST NOT

- **MUST NOT** create interfaces without genuine seam requirement
- **MUST NOT** inject `GraphServiceClient` directly into endpoints
- **MUST NOT** register duplicate HttpClients
- **MUST NOT** inline registrations (use feature modules)

---

## Implementation Patterns

### Feature Module Registration

```csharp
// Program.cs - Clean, ~15 lines
builder.Services
    .AddSpaarkeCore(builder.Configuration)
    .AddDocumentsModule()
    .AddWorkersModule();
```

**See**: [Service Registration Pattern](../patterns/api/service-registration.md)

### Concrete Registration

```csharp
// ✅ DO: Register concrete
services.AddSingleton<SpeFileStore>();
services.AddSingleton<AuthorizationService>();

// ❌ DON'T: Interface without seam
services.AddSingleton<IResourceStore, SpeFileStore>();  // No other implementations
```

### Allowed Seams (Only Two)

```csharp
// These are the ONLY interfaces with multiple implementations
services.AddSingleton<IAccessDataSource, DataverseAccessDataSource>();
services.AddSingleton<IEnumerable<IAuthorizationRule>>(/* rules */);
```

---

## Anti-Patterns

```csharp
// ❌ DON'T: Inject Graph directly
public class DocumentController(GraphServiceClient graph) { }

// ✅ DO: Use facade
public class DocumentController(SpeFileStore store) { }

// ❌ DON'T: Inline registration sprawl
services.AddSingleton<ServiceA>();
services.AddSingleton<ServiceB>();
// ... 20+ lines

// ✅ DO: Feature module
services.AddDocumentsModule();
```

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-001](ADR-001-minimal-api.md) | Single runtime, single composition root |
| [ADR-007](ADR-007-spefilestore.md) | SpeFileStore as concrete facade |
| [ADR-009](ADR-009-redis-caching.md) | Redis-only, no hybrid cache services |

---

## Source Documentation

**Full ADR**: [docs/adr/ADR-010-di-minimalism.md](../../docs/adr/ADR-010-di-minimalism.md)

For detailed context including:
- Removed registrations list
- Feature module contents
- Exception scenarios for new interfaces

---

**Lines**: ~95
**Pattern Files**: DI patterns in `patterns/api/service-registration.md`
