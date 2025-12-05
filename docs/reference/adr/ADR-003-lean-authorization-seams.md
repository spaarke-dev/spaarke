# ADR-003: Lean authorization with two seams (UAC data and file storage)

| Field | Value |
|-------|-------|
| Status | **Accepted** |
| Date | 2025-09-27 |
| Updated | 2025-12-04 |
| Authors | Spaarke Engineering |

## Context

We need flexibility to enforce Dataverse-backed Unified Access Control (UAC) and integrate with SharePoint Embedded (SPE), without proliferating service interfaces. Over-abstracted layers impede clarity and testability.

## Decision

| Rule | Description |
|------|-------------|
| **Concrete AuthorizationService** | Evaluates ordered set of small `IAuthorizationRule` policies |
| **One UAC seam** | `IAccessDataSource` → `DataverseAccessDataSource` returns coarse, per-request snapshots |
| **One storage seam** | `SpeFileStore` encapsulates Graph/SPE operations (no generic `IResourceStore`) |
| **Policy separation** | Rules contain policy only; SDK/HTTP usage remains in adapters |

## Consequences

**Positive:**
- Fewer classes, clearer responsibilities, faster unit tests, simple extension via new rules
- No leakage of provider details to higher layers

**Negative:**
- Slightly less generic than a policy engine, but far less boilerplate

## Alternatives Considered

Multiple service interfaces per concern and generic policy engines. **Rejected** as premature complexity and harder for AI-generated code to follow consistently.

## Operationalization

### Authorization Flow

| Step | Component |
|------|-----------|
| 1. Call AuthorizationService | Before any `SpeFileStore` operation |
| 2. Evaluate rules | Ordered rule chain |
| 3. Return decision | With machine-readable reason code |

### Initial Rules

| Rule | Purpose |
|------|---------|
| `ExplicitDenyRule` | Check explicit deny entries |
| `ExplicitGrantRule` | Check explicit grant entries |
| `TeamMembershipRule` | Verify team membership |
| `RoleScopeRule` | Check role-based scope |
| `LinkTokenRule` | Validate share links |

### Data Access

| Pattern | Implementation |
|---------|----------------|
| Snapshots | Fetched via `IAccessDataSource`, cached per request |
| Deny codes | Machine-readable (e.g., `sdap.access.deny.team_mismatch`) |

## Exceptions

Tenant-specific policies should be delivered as additional `IAuthorizationRule` implementations registered via DI, not new service layers.

## Success Metrics

| Metric | Target |
|--------|--------|
| Service/interface count | Reduced |
| Access check defects | Lower rate |
| Query performance | Stable |
| Authorization behavior | Predictable |

## Compliance

**Architecture tests:** `ADR003_AuthorizationTests.cs` validates seam boundaries.

**Code review checklist:**
- [ ] New auth logic implemented as `IAuthorizationRule`
- [ ] No direct Graph/SPE calls outside `SpeFileStore`
- [ ] Snapshots cached per request (not per call)
- [ ] Deny results include reason codes
