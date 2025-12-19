# ADR-003: Lean Authorization Seams (Concise)

> **Status**: Accepted
> **Domain**: Authorization
> **Last Updated**: 2025-12-18

---

## Decision

Use **two seams only**: `IAccessDataSource` for UAC data, `SpeFileStore` for storage. Implement authorization via ordered `IAuthorizationRule` chain.

**Rationale**: Over-abstracted layers impede clarity. Fewer interfaces = faster tests and clearer responsibilities.

---

## Constraints

### ✅ MUST

- **MUST** implement new auth logic as `IAuthorizationRule`
- **MUST** call authorization before `SpeFileStore` operations
- **MUST** cache UAC snapshots per-request only (not across requests)
- **MUST** include machine-readable deny codes

### ❌ MUST NOT

- **MUST NOT** create new service layers for auth (use rules)
- **MUST NOT** make direct Graph/SPE calls outside `SpeFileStore`
- **MUST NOT** cache authorization decisions (cache data only)
- **MUST NOT** reuse UAC snapshots across requests/jobs

---

## Implementation Patterns

### Authorization Flow

```csharp
// 1. Resolve authorization before storage operations
var decision = await _authService.AuthorizeAsync(userId, resourceId, Operation.Read);

if (!decision.IsAuthorized)
    return Results.Problem(statusCode: 403, extensions: new {
        errorCode = decision.DenyCode  // e.g., "sdap.access.deny.team_mismatch"
    });

// 2. Proceed with storage
var file = await _speFileStore.GetFileAsync(resourceId);
```

### Authorization Rule

```csharp
public class TeamMembershipRule : IAuthorizationRule
{
    public int Order => 30;

    public Task<AuthDecision> EvaluateAsync(AuthContext ctx)
    {
        if (!ctx.Snapshot.Teams.Contains(ctx.ResourceTeamId))
            return Task.FromResult(AuthDecision.Deny("sdap.access.deny.team_mismatch"));

        return Task.FromResult(AuthDecision.Continue());
    }
}
```

**See**: [Authorization Pattern](../patterns/auth/authorization-service.md)

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-007](ADR-007-spefilestore.md) | SpeFileStore as storage seam |
| [ADR-008](ADR-008-endpoint-filters.md) | Endpoint filters call auth |
| [ADR-009](ADR-009-redis-caching.md) | Cache data, not decisions |

---

## Source Documentation

**Full ADR**: [docs/adr/ADR-003-lean-authorization-seams.md](../../docs/adr/ADR-003-lean-authorization-seams.md)

---

**Lines**: ~85
