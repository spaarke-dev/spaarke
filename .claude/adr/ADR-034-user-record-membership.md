# ADR-034: User-Record Membership Resolution Pattern (Concise)

> **Status**: Accepted
> **Domain**: BFF API / Dataverse Membership / Identity Normalization
> **Last Updated**: 2026-06-21
> **Source project**: `spaarke-platform-foundations-r3` Part 1 (closes the "no canonical mechanism for records this user is associated with" gap surfaced during R2 UAT — notification-new-documents.json silently produced zero rows because its FetchXML joined through a non-existent `sprk_matterteammember` entity).
> **Cross-references**: extends ADR-013 (AI architecture); reinforces ADR-009 (Redis caching), ADR-010 (DI minimalism), ADR-024 (polymorphic resolver pattern), ADR-028 (Spaarke Auth v2 cross-ref via `azureactivedirectoryobjectid`); aligns with CLAUDE.md §10.

---

## Decision

Spaarke ships ONE canonical mechanism for "records this user is associated with, by entity type" — replacing the ad-hoc per-playbook / per-UI-surface FetchXML pattern that silently breaks (A1 / D5 root cause from R2 UAT).

**Discovery-based** (convention over configuration): `MembershipFieldDiscoveryService` queries Dataverse `EntityDefinitions` metadata for any entity type, automatically discovers Lookup fields whose targets are one of 6 configured identity tables (`systemuser`, `contact`, `team`, `businessunit`, `account`, `sprk_organization`), and derives role names via a CamelCase strategy (strip `sprk_` prefix + strip trailing numeric digits + camelCase). Per-entity overrides in `appsettings.json` cover edge cases (excluded fields, role-name overrides, force-include).

**Identity normalization** (`IdentityNormalizationService`) resolves a `systemuserid` into the full `PersonIdentity` record (contactId, teamIds, businessUnitId, accountId, organizationIds, primary email). Each of the 6 identity-type paths is independent and fail-isolated. Redis cache 10-min TTL per user.

**Orchestration** (`MembershipResolverService`) combines discovery + normalization + an OR-joined FetchXML query against the target entity. Per-user Redis cache (5-min TTL in Phase 1A; Phase 2 lengthens TTL with Redis pub/sub invalidation on junction-row writes).

**Endpoint contract**:
```
GET /api/users/me/memberships/{entityType}
  ?roles=owner,assignedAttorney             (optional)
  ?identityTypes=SystemUser,Contact         (optional)
  ?includeRelated=documents,events          (optional; 1-hop max per Q3)
  ?limit=500&continuationToken={token}
```

Standard Spaarke Auth v2 OBO. Response shape per design.md endpoint contract: `{entityType, personIdentity, ids[], byRole, count, cacheExpiresAt, continuationToken?}`.

**Phase 2** (firm in-scope for R3 per owner decision 2026-06-20): materialized junction table `sprk_userentityassociation` (7 cols + composite alternate key) + event-driven Service Bus sync via topic `sprk-membership-changes` (D3 owner decision: topic + subscription-per-consumer, NOT queue, NOT reuse `ServiceBusJobProcessor` queue) + nightly `MembershipReconciliationJob` (defense-in-depth backstop per Q2 fire-and-forget publishing) + Redis pub/sub cache invalidation (FR-2P2.8). The Phase 1A endpoint contract is unchanged when Phase 2 swaps in — strangler-fig pattern.

**Naming-collision register** (binding): "Membership" terminology is used throughout to disambiguate from the existing `AssociationResolver` PCF (a DIFFERENT concept — record-to-record FieldMapping for copying values when an Event's Regarding lookup is set). Do not confuse the two surfaces.

---

## Three Patterns

| Pattern | When to use | Behavior |
|---|---|---|
| **Discovery-based per-entity overrides** | Default — any new entity that surfaces user-record memberships | Auto-discovers all 6-identity-type Lookups; overrides only for edge cases (role-name mapping, force-include for globally-excluded fields) |
| **Identity-type path independence** | Users with partial identity coverage (e.g., user without contact, contact without systemuser) | Each path resolves independently; failure of one does NOT fail others; per-path try/catch + warning log |
| **Phase 1A → Phase 2 strangler-fig** | Performance evolution | Phase 1A per-request FetchXML works to 100K-row entities; Phase 2 junction-table + event sync ships when AC-1A.5 (p95 ≤300ms) margin shrinks. **Endpoint contract is byte-identical between phases** — consumers see no change. |

---

## Constraints

### ✅ MUST

- **MUST** use `MembershipResolverService` (via `IMembershipResolverService` DI) for any "records this user is associated with" query. Do NOT re-derive membership in ad-hoc FetchXML — that pattern is the A1 / D5 root cause this ADR exists to prevent.
- **MUST** include all 6 identity tables in `Membership:IncludedIdentityTables`: `systemuser`, `contact`, `team`, `businessunit`, `account`, `sprk_organization`.
- **MUST** apply the 4 global field exclusions (`createdby`, `modifiedby`, `createdonbehalfby`, `modifiedonbehalfby`) — these are touch-history, not association.
- **MUST** resolve `sprk_assignedlawfirm1/2` to `identityType="Organization"` (NOT "Contact" — Q4 corrects an error in design.md Discovery Report example). The 2 fields are Lookup → `sprk_organization`; per-entity override in `Membership:EntityOverrides.sprk_matter.FieldRoleOverrides` maps both to role `"assignedLawFirm"`.
- **MUST** use the existing `SystemAdmin` policy for admin endpoints (`/api/admin/membership/discovered/{entityType}`, `/api/admin/membership/refresh-metadata`) — Q6 owner clarification. Do NOT create a new "PlatformAdmin" policy. Precedent: `RagEndpoints.cs:157`.
- **MUST** use Standard Spaarke Auth v2 OBO on `/api/users/me/memberships/{entityType}` — any authenticated user can query their OWN memberships; no role restriction on the user-facing endpoint.
- **MUST** cap `includeRelated` (Phase 1D transitive memberships) at **1 hop** per Q3 owner clarification. Multi-hop requests return `400 BadRequest` with `ProblemDetails type="transitive-chain-too-deep"`.
- **MUST** publish `MembershipChangedEvent` to the Service Bus **topic** `sprk-membership-changes` (NOT queue, NOT reuse `ServiceBusJobProcessor` queue) — D3 owner clarification. Subscriptions per consumer.
- **MUST** use **fire-and-forget** event-publishing semantics per Q2 owner clarification: publish best-effort; mutation succeeds even if publish fails. Nightly `MembershipReconciliationJob` is the defense-in-depth backstop. Log publish failures as structured warnings with correlationId (NFR-08).
- **MUST** keep Phase 1A → Phase 2 endpoint contract identical (strangler-fig). Consumers see byRole map, ids[], count regardless of internal storage mechanism.

### ❌ MUST NOT

- **MUST NOT** match identity by free-text display-name fields (too fuzzy, brittle). Email substring matching is supported only as a separate code path documented in the identity normalization contract.
- **MUST NOT** join through `sprk_matterteammember` or any other non-existent entity. The R2-UAT-broken `notification-new-documents.json` playbook is migrated in R3 task 050 to use the new `LookupUserMembership` node (ActionType=52) + `joinIds` Handlebars helper.
- **MUST NOT** introduce a new "PlatformAdmin" policy — Q6 mandates reuse of existing `SystemAdmin`.
- **MUST NOT** extend a transitive-membership query beyond 1 hop. Reject deeper chains with 400 before any Dataverse query (Q3).
- **MUST NOT** assume the Phase 1A per-request FetchXML approach is sufficient for all future scale. Monitor AC-1A.5 (p95 ≤300ms); Phase 2 junction table is the escape hatch when margin shrinks.
- **MUST NOT** confuse the new "Membership" terminology with the existing `AssociationResolver` PCF (record-to-record FieldMapping). The naming-collision register is binding.

---

## Key Types

```csharp
namespace Sprk.Bff.Api.Services.Ai.Membership;

public interface IMembershipResolverService
{
    Task<MembershipResponse> ResolveAsync(
        Guid systemUserId,
        string entityType,
        MembershipResolveOptions? options,
        CancellationToken ct);
}

public sealed record MembershipResolveOptions(
    IReadOnlyList<string>? Roles = null,
    IReadOnlyList<string>? IdentityTypes = null,
    IReadOnlyList<string>? IncludeRelated = null,  // 1-hop max per Q3
    int Limit = 500,
    string? ContinuationToken = null);

public sealed record MembershipResponse(
    [property: JsonPropertyName("entityType")] string EntityType,
    [property: JsonPropertyName("personIdentity")] PersonIdentity PersonIdentity,
    [property: JsonPropertyName("ids")] IReadOnlyList<Guid> Ids,
    [property: JsonPropertyName("byRole")] IReadOnlyDictionary<string, IReadOnlyList<Guid>> ByRole,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("cacheExpiresAt")] DateTimeOffset CacheExpiresAt,
    [property: JsonPropertyName("continuationToken")] string? ContinuationToken = null);

public sealed record PersonIdentity(
    Guid SystemUserId,
    Guid? ContactId = null,
    string? PrimaryEmail = null,
    IReadOnlyList<Guid>? TeamIds = null,
    Guid? BusinessUnitId = null,
    Guid? AccountId = null,
    IReadOnlyList<Guid>? OrganizationIds = null);
```

---

## Identity Normalization Contract

| Source field type | Resolves via | Match value |
|---|---|---|
| `Lookup → systemuser` | Direct | `systemUserId` |
| `Lookup → contact` | Direct; cross-referenced to `systemUserId` via `azureactivedirectoryobjectid` (ADR-028) | `contactId` |
| `Lookup → team` | Expand `teammembership` to systemusers | `teamIds[]` (cached) |
| `Lookup → businessunit` | User's BU + any descendant BUs (configurable per role) | `businessUnitId` |
| `Lookup → account` | User's primary contact's `parentcustomerid` (if contact) | `accountId` (when applicable) |
| `Lookup → sprk_organization` | Configured `Membership:OrganizationLookup:UserLookupField` (R3 chose Option (b) config-driven per task 032 decision; default empty = fail-soft empty result) | `organizationIds[]` |
| Text (email) | Substring `like` | `primaryEmail` |
| Text (display name) | NOT supported (too fuzzy) | — |

---

## Phase 1A → Phase 2 Migration

**Phase 1A** (R3): per-request FetchXML against target entity. Membership cache 5-min TTL per `{userId, entityType, optionsHash}`. Sufficient for current data volumes.

**Phase 2** (R3 — firm in-scope per owner 2026-06-20): materialized junction table `sprk_userentityassociation` + event-driven sync via topic `sprk-membership-changes` (D3) + nightly `MembershipReconciliationJob` + Redis pub/sub cache invalidation. Phase 2 trigger threshold from design: `p95 > 500ms for the endpoint sustained`, but Phase 2 ships preemptively in R3 to lock in the durability + scale ceiling before consumer count grows.

**Endpoint contract unchanged across phases** — consumers see no change when storage swaps from per-request FetchXML to junction-table query. Strangler-fig: `MembershipResolverService` internally chooses the source.

---

## Admin Endpoints

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/admin/membership/discovered/{entityType}` | Operator audit — what was auto-discovered vs override vs excluded vs ignored |
| POST | `/api/admin/membership/refresh-metadata` | Invalidate metadata cache (force re-query Dataverse on next call) |

All require `SystemAdmin` policy (Q6).

---

## Alternatives Considered

| Alternative | Why rejected |
|---|---|
| **Explicit enumeration per entity** (originally proposed) | Drift-prone (D5 root cause); verbose; maintenance burden when fields added |
| **Denormalized text column** `sprk_associatedpersonsindex` on each record, queried via `LIKE` | `LIKE` on text doesn't use indexes; identity heterogeneity unsolved; stale on contact rename/merge; text-field-size limits |
| **Junction table FIRST** (skip Phase 1A, go straight to Phase 2) | Premature optimization at original spec time; locks in derived data store before consumer count justifies it. NOTE: R3 ended up shipping Phase 2 anyway per owner decision — but Phase 1A's contract-first approach kept the migration path clean |
| **Dedicated AI Search index for "My X" only** | Wrong tool for the problem (membership, not search); duplicates source-of-truth; index maintenance overhead exceeds benefit |
| **Cosmos DB** as primary store | Premature at current data volumes; introduces new data store; Dataverse + caching handles current load |
| **Dataverse calculated/rollup fields** | Calculated fields are per-row evaluation, not indexed; rollups are aggregations, not membership |
| **Reuse `sprk_fieldmappingprofile` + `sprk_fieldmappingrule`** | DIFFERENT concept (record-to-record value copy vs user-record membership); schema fields don't fit. Architectural pattern (Profile+Rules) reusable IF we ever move membership config to Dataverse — design for that escape hatch via R4 if makers ask. |
| **Power Automate flows** to maintain junction table | Race conditions, throttling, harder to test, latency; user constraint: no plugins/flows for this category |
| **Polymorphic Lookup on `sprk_userentityassociation`** for "person" | 6 different target tables make polymorphic Lookup awkward; design uses `(personId, personIdType)` tuple per ADR-024 polymorphic resolver pattern instead |

---

## Integration with Other ADRs

| ADR | Relationship |
|---|---|
| [ADR-009](ADR-009-redis-caching.md) | Per-user membership cache (5-min Phase 1A; longer + pub/sub invalidation Phase 2); metadata cache 1h |
| [ADR-010](ADR-010-di-minimalism.md) | `IMembershipResolverService` + `IMembershipFieldDiscoveryService` + `IIdentityNormalizationService` + `IOrganizationMembershipResolver` allowed as testing seams (concrete impls injected) |
| [ADR-013](ADR-013-ai-architecture.md) | Membership services live under `Services/Ai/Membership/`; `LookupUserMembership` node (ActionType=52) extends existing node-executor framework |
| [ADR-024](ADR-024-polymorphic-resolver-pattern.md) | Informs identity normalization (`Lookup→contact` cross-ref); `(personId, personIdType)` tuple in `sprk_userentityassociation` instead of polymorphic Lookup |
| [ADR-028](ADR-028-spaarke-auth-architecture.md) | Endpoint uses OBO; identity resolution cross-references via `azureactivedirectoryobjectid` |
| [ADR-029](ADR-029-bff-publish-hygiene.md) | NFR-01 publish-size measured per task |
| [ADR-036](ADR-036-background-job-infrastructure.md) | `MembershipReconciliationJob` is the second reference consumer of Spaarke.Scheduling |

---

## See Also

- Full ADR: [`docs/adr/ADR-034-user-record-membership.md`](../../docs/adr/ADR-034-user-record-membership.md)
- Architecture page: [`docs/architecture/membership-resolution-pattern.md`](../../docs/architecture/membership-resolution-pattern.md) (created in task 104)
- Spec: [`projects/spaarke-platform-foundations-r3/spec.md`](../../projects/spaarke-platform-foundations-r3/spec.md) Part 1 (FR-1A.* + FR-1B.* + FR-1C.* + FR-1D.* + FR-2P2.* + AC-1A.* + AC-1B.* + AC-1C.* + AC-1D.* + AC-1P2.*)
- Data model: [`docs/data-model/sprk_userentityassociation.md`](../../docs/data-model/sprk_userentityassociation.md)
- Decision note: [`projects/spaarke-platform-foundations-r3/notes/sprk-organization-mapping-decision.md`](../../projects/spaarke-platform-foundations-r3/notes/sprk-organization-mapping-decision.md)
- Constraints: [`.claude/constraints/bff-extensions.md`](../constraints/bff-extensions.md) §§A, F.1
