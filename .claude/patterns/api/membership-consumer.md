# Membership-Consumer Pattern (`IMembershipResolverService`)

> **Last Reviewed**: 2026-06-22
> **Reviewed By**: spaarke-platform-foundations-r3 Wave 28
> **Status**: Verified
> **Source**: ADR-034 · R3 spec Part 1

## When
Any BFF endpoint, background job, AI tool, or playbook node that needs to answer "**which records of entity X is THIS user a member of**" — e.g., "my matters", "my documents", "events on matters I'm assigned to". Use `IMembershipResolverService` (Phase 1A contract); do NOT re-derive membership in ad-hoc FetchXML — that pattern is the A1/D5 root cause this contract exists to prevent.

## Read These Files
1. `src/server/api/Sprk.Bff.Api/Services/Ai/Membership/IMembershipResolverService.cs` — the contract
2. `src/server/api/Sprk.Bff.Api/Services/Ai/Membership/MembershipResolverService.cs` — implementation
3. `src/server/api/Sprk.Bff.Api/Services/Ai/Membership/Models/MembershipResponse.cs` — response shape
4. `src/server/api/Sprk.Bff.Api/Services/Ai/Membership/Models/PersonIdentity.cs` — caller-identity record
5. **Reference consumers** (mirror these exactly):
   - `src/server/api/Sprk.Bff.Api/Services/Workspace/BriefingService.cs` — Daily Briefing's wiring (Wave 28 P0 fix)
   - `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/LookupUserMembershipNodeExecutor.cs` — playbook-node wiring
   - `src/server/api/Sprk.Bff.Api/Api/Membership/MembershipEndpoints.cs` — endpoint wiring + AAD oid → systemuserid cross-ref
6. `.claude/adr/ADR-034-user-record-membership.md` — binding rules
7. `docs/guides/MEMBERSHIP-RESOLUTION-GUIDE.md` — operator-facing concepts (helpful for naming + identity-type semantics)

## Constraints
- **ADR-034**: Use `MembershipResolverService` for every "records this user is associated with" query. Re-deriving via custom FetchXML is the canonical anti-pattern.
- **ADR-010**: Inject `IMembershipResolverService` directly (it's registered Singleton; safe to inject into Scoped or Singleton consumers)
- **Q4** (binding): `sprk_assignedlawfirm1/2` resolves to `identityType="Organization"`. The resolver handles this; consumers should NOT special-case it
- **Q3** (binding): `IncludeRelated` is capped at 1 hop max. Multi-hop requests return 400 BadRequest with `ProblemDetails` from the endpoint. Consumers downstream don't need to validate.
- **NFR-08**: Pass through the request `correlationId` for trace correlation
- **bff-extensions.md §A**: Pre-merge checklist applies

## Key Rules

### Identity resolution (caller → PersonIdentity)
- Endpoint consumers: read claims to get AAD `oid` → cross-reference to `systemuser.azureactivedirectoryobjectid` → construct PersonIdentity (mirror `MembershipEndpoints.ResolveSystemUserIdAsync` exactly)
- Background-job consumers: the job's principal context (e.g., system identity) — typically PersonIdentity for a service user OR null if recon-style
- Playbook-node consumers: `NodeExecutionContext.UserId` (mirror `QueryDataverseNodeExecutor.ResolveUserId` exactly); fallback to previous-output `userId` StructuredData property
- **Never invent a new identity-resolution path** — pick one of the 3 above

### Call shape
```csharp
var response = await resolver.ResolveAsync(
    systemUserId,
    entityType: "sprk_matter",            // or sprk_document, sprk_event, etc.
    options: new MembershipResolveOptions(
        Roles: new[] { "assignedAttorney", "owner" },  // optional; default = all roles
        IdentityTypes: null,                           // optional; default = all 6
        IncludeRelated: null,                          // optional; 1-hop max per Q3
        Limit: 500,
        ContinuationToken: null),
    ct);

// response.Ids — flat list of matching matter IDs
// response.ByRole — { "owner": [...guid], "assignedAttorney": [...guid] }
// response.Count — total
// response.CacheExpiresAt — when this response was cached until
```

### Failure semantics
- Resolver exceptions: caller catches per its own resilience model
  - Endpoints: rethrow → ProblemDetails 500
  - Daily Briefing-style consumers: **failure-soft** (log Warning, return null/empty briefing) — mirror `BriefingService.GetTopPriorityMatterAsync` exactly
  - Playbook-node consumers: return ValidationFailed → orchestrator continues per playbook policy
- No memberships: response with empty `Ids` + empty `ByRole` (NOT an error)
- Caller cancellation: pass `CancellationToken` through (resolver honors it)

### Caching (don't fight it)
- Resolver caches per-user response 5-min TTL automatically
- Junction-row writes invalidate via Redis pub/sub (when topic-deploy + Redis enabled — see ADR-032 Null peer behavior)
- Consumers do NOT need their own membership cache layer; if you find yourself adding one, you're wrong

### Tests (mandatory)
- `ConsumerMethod_HappyPath_CallsResolverOncePerEntity` — mock `IMembershipResolverService`; verify the call shape
- `ConsumerMethod_EmptyMemberships_HandlesGracefully` — mock returns empty `Ids`
- `ConsumerMethod_ResolverThrows_FailsoftOrRethrows` — match your resilience model
- `ConsumerMethod_PreservesCorrelationId` (NFR-08)
- `ConsumerMethod_RespectsAAD_Oid_Resolution_Edge_Cases` (non-Guid oid; unprovisioned systemuser)

## When NOT to Use This Pattern
- **AI chat scoping** (SprkChat): use `HostContext` + `RagService` filters (entity-bound, not "my X" semantics)
- **Workspace UI ("My Matters", "My Events")**: deferred to R4 scope
- **Admin discovery endpoint** (`/api/admin/membership/discovered/{entityType}`): direct injection of `IMembershipFieldDiscoveryService`, NOT the resolver

## Anti-Patterns to Avoid
- ❌ Hand-rolled FetchXML for "my X" — that's the A1/D5 defect this pattern exists to prevent
- ❌ Injecting `IMembershipFieldDiscoveryService` + `IDataverseService` separately and reassembling — use the orchestrator (`IMembershipResolverService`)
- ❌ Caching responses in your own service layer — the resolver already caches
- ❌ Looking up systemuserid by display-name (too fuzzy, brittle) — use AAD oid cross-ref
- ❌ Multi-hop `IncludeRelated` (>1 hop) — the resolver rejects with 400; don't try to bypass

## Companion Pattern Docs
- [`../ai/node-executor-authoring.md`](../ai/node-executor-authoring.md) — for playbook-node consumers specifically
- [`error-handling.md`](error-handling.md) — failure-soft vs rethrow decision
- [`service-registration.md`](service-registration.md) — DI registration conventions
- [`scheduled-jobs.md`](scheduled-jobs.md) — when consuming from a `MembershipReconciliationJob`-style recon path
