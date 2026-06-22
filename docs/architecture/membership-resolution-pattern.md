# Membership Resolution Pattern

> **Last Updated**: 2026-06-22
> **Last Reviewed**: 2026-06-22
> **Reviewed By**: spaarke-platform-foundations-r3 task 104
> **Status**: Verified
> **Parent**: [AI-ARCHITECTURE.md](AI-ARCHITECTURE.md) (Spaarke AI platform overview)
> **Source**: [ADR-034 concise](../../.claude/adr/ADR-034-user-record-membership.md) · [ADR-034 full](../adr/ADR-034-user-record-membership.md) · [R3 spec](../../projects/spaarke-platform-foundations-r3/spec.md) FR-1A.* / FR-1B.* / FR-1D.* / FR-2P2.*

---

## Purpose

Spaarke ships ONE canonical mechanism for answering **"which records of entity type T is this user associated with, and in what role?"** — replacing the ad-hoc per-playbook FetchXML pattern that silently broke at R2 UAT (the `notification-new-documents.json` playbook joined through a non-existent `sprk_matterteammember` entity and produced zero rows). The pattern is consumed by AI playbooks (e.g., `LookupUserMembership` node), workspace UI surfaces ("My Matters", "My Events"), and any future code that needs membership semantics. Storage is split into Phase 1A (live per-request FetchXml) and Phase 2 (materialized junction `sprk_userentityassociation` + event-driven sync). The endpoint contract is byte-identical across phases — strangler-fig migration.

---

## ⚠️ Naming-Collision Register

The Spaarke codebase contains **two distinct concepts** whose names sound similar but solve different problems. Both are in production and neither is being retired. Always disambiguate by the noun.

| Concept | Type | Purpose | Where it lives |
|---|---|---|---|
| **`AssociationResolver` PCF** | UI control (PCF) | Record-to-record FieldMapping — when an Event's Regarding lookup is set to a Matter, copy configured fields from the Matter onto the Event per `sprk_fieldmappingprofile` + `sprk_fieldmappingrule` config | `src/client/pcf/AssociationResolver/` |
| **Membership Resolution** (this doc) | BFF service + endpoint | User-record membership — given a user, return the records of entity type T they are associated with (by Lookup field), grouped by role | `src/server/api/Sprk.Bff.Api/Services/Ai/Membership/` + `src/server/api/Sprk.Bff.Api/Api/Membership/` |

**Disambiguation rule** (binding, from ADR-034 + spec.md line 59 + line 428):

- Use the noun **"Membership"** for user-to-record (e.g., `MembershipResolverService`, `/api/users/me/memberships/{entityType}`, `sprk_userentityassociation`, topic `sprk-membership-changes`).
- Use the noun **"Association"** / **"FieldMapping"** for record-to-record value-copy (e.g., `AssociationResolver` PCF, `sprk_fieldmappingprofile`, `sprk_fieldmappingrule`).
- **Do NOT** rename either — both nouns are stable and consumer-facing.
- **Do NOT** add membership semantics to the FieldMapping framework, or vice-versa. They are independent surfaces with no shared interfaces.

> **History**: The R3 design team initially considered reusing the `sprk_fieldmappingprofile` / `sprk_fieldmappingrule` schema for membership configuration. ADR-034 "Alternatives Considered" rejected this: "DIFFERENT concept (record-to-record value copy vs user-record membership); schema fields don't fit." The architectural Profile + Rules pattern is reusable if a future R4 ever moves membership config from `appsettings.json` into Dataverse (`sprk_membershipconfig`), but the existing tables are not.

---

## Contract

### User-facing endpoint (Spaarke Auth v2 OBO)

```
GET /api/users/me/memberships/{entityType}
  ?roles=owner,assignedAttorney             (optional CSV; case-insensitive)
  ?identityTypes=SystemUser,Contact         (optional CSV; case-insensitive)
  ?includeRelated=documents,events          (optional CSV; 1-hop max per Q3)
  ?limit=500&continuationToken={token}      (paging)
```

- **Auth**: `RequireAuthorization()` default JWT policy — any authenticated user can query their OWN memberships (no admin gate). Caller's `oid` claim is cross-referenced to `systemuserid` via `azureactivedirectoryobjectid` (ADR-028). 10-min Redis cache on the AAD-oid → systemuserid lookup.
- **Response**: `200 OK` + `MembershipResponse` (camelCase JSON locked at the type level).
- **Errors**: `400` malformed query params (incl. `transitive-chain-too-deep` for `includeRelated` > 1 hop); `401` unauthenticated; `500` ProblemDetails on Dataverse failure.

### Admin endpoints (`SystemAdmin` policy)

| Method | Path | Purpose | Reference |
|---|---|---|---|
| `GET` | `/api/admin/membership/discovered/{entityType}` | Operator audit — returns the full classification: discovered fields, role overrides, excluded fields, ignored fields (with reasons) | FR-1A.10 |
| `POST` | `/api/admin/membership/refresh-metadata` | Invalidate metadata cache — force re-query of Dataverse `EntityDefinitions` on next call | FR-1A.11 |

Both use existing `SystemAdmin` policy (`AuthorizationModule.cs:241`) per Q6 owner clarification. No new `PlatformAdmin` policy.

### DTOs (camelCase JSON)

| DTO | File | Notes |
|---|---|---|
| `MembershipResponse` | `Services/Ai/Membership/Models/MembershipResponse.cs` | `{entityType, personIdentity, ids[], byRole, count, cacheExpiresAt, continuationToken?}` |
| `PersonIdentity` | `Services/Ai/Membership/Models/PersonIdentity.cs` | `{systemUserId, contactId?, primaryEmail?, teamIds[]?, businessUnitId?, accountId?, organizationIds[]?}` |
| `MembershipDescriptor` | `Services/Ai/Membership/Models/MembershipDescriptor.cs` | Discovery output — per-field classification with `field`, `role`, `identityType`, `target`, `source` (auto/override/include) |
| `MembershipResolveOptions` | `Services/Ai/Membership/IMembershipResolverService.cs` | `{roles?, identityTypes?, includeRelated?, limit, continuationToken?}` — `IncludeRelated` capped at 1 hop |

---

## Discovery Model (metadata-driven, convention-over-configuration)

`MembershipFieldDiscoveryService` (`Services/Ai/Membership/MembershipFieldDiscoveryService.cs`) queries Dataverse `EntityDefinitions` at runtime for any entity type, automatically discovers Lookup attributes whose targets are one of the 6 configured identity tables, and derives a role name from the field's logical name. Per-entity overrides cover edge cases.

### Algorithm (5 steps; see `MembershipFieldDiscoveryService.cs:12-23`)

1. **Cache lookup** — Redis key `membership:discovery:{entityType}`, TTL `MembershipOptions.MetadataCacheTtlMinutes` (default 60 min, ADR-009).
2. **Fetch metadata** on cache miss via `MetadataService.RetrieveEntityRequest` (`EntityFilters.Attributes`).
3. **Classify** each Lookup attribute:
   - `Targets[]` intersects `IncludedIdentityTables` → **kept** as descriptor
   - matches `GlobalFieldExclusions` → **ExcludedField** (reason `global-exclusion`), unless per-entity `IncludedFields` force-includes (reason `override`)
   - matches per-entity `ExcludedFields` → **ExcludedField** (reason `per-entity-exclusion`)
   - target not in identity list → **IgnoredField** (reason `target-table-not-in-identity-list`, carries `Target` name)
4. **Derive role name** via CamelCase strategy (strip `sprk_` prefix → strip trailing digits → camelCase), OR use `FieldRoleOverrides` verbatim when configured (e.g., both `sprk_assignedlawfirm1` and `sprk_assignedlawfirm2` → role `assignedLawFirm`).
5. **Derive identity type** by looking up `Target` in `IncludedIdentityTables` (e.g., `sprk_organization` → `Organization` per Q4).

### Six identity tables (default `MembershipOptions.IncludedIdentityTables`)

`systemuser`, `contact`, `team`, `businessunit`, `account`, `sprk_organization`

### Four global field exclusions

`createdby`, `modifiedby`, `createdonbehalfby`, `modifiedonbehalfby` — these are touch-history, not association.

---

## Identity Normalization (6-path, fail-isolated)

`IdentityNormalizationService` (`Services/Ai/Membership/IdentityNormalizationService.cs`) resolves a `systemuserid` into the full `PersonIdentity` by querying 6 paths. Each path is independent: failure on one does NOT fail the others (per-path try/catch + warning log). Result cached in Redis for 10 minutes per ADR-009.

| # | Source field type | Resolves via | Returned field | File reference |
|---|---|---|---|---|
| 1 | `Lookup → systemuser` | Direct row read | `systemUserId`, `businessUnitId`, `primaryEmail`, `azureActiveDirectoryObjectId` | `IdentityNormalizationService.cs:7-10` |
| 2 | `Lookup → contact` | Cross-ref via `azureactivedirectoryobjectid` (ADR-028) | `contactId` | `IdentityNormalizationService.cs:9` |
| 3 | `Lookup → team` | Expand `teammembership` to user's teams | `teamIds[]` (cached) | `IdentityNormalizationService.cs:10` |
| 4 | `Lookup → businessunit` | User's BU; descendants configurable per role | `businessUnitId` | `IdentityNormalizationService.cs:13` |
| 5 | `Lookup → account` | User's contact's `parentcustomerid` (if account) | `accountId` (when applicable) | `IdentityNormalizationService.cs:14` |
| 6 | `Lookup → sprk_organization` | Delegated to `IIdentityOrganizationResolver` (configurable user→org Lookup field; see `OrganizationMembershipResolver.cs:6-18` + `notes/sprk-organization-mapping-decision.md`) | `organizationIds[]` | `OrganizationMembershipResolver.cs:38-51` |

Steps 1–3 run in parallel via `Task.WhenAll`. Steps 4–5 are sequential after the contact lookup. Failure-soft fallback on path 6 returns empty list + Info log when `Membership:OrganizationLookup:UserLookupField` is unset (operator setup pending is not an error).

**Text matching**: substring `like` on `primaryEmail` is supported as a documented separate code path. **Free-text display-name matching is forbidden** — too fuzzy, brittle (ADR-034 MUST NOT).

---

## Orchestration

`MembershipResolverService` (`Services/Ai/Membership/MembershipResolverService.cs`) combines discovery + normalization + a single OR-joined FetchXml query against the target entity. Pipeline (`MembershipResolverService.cs:5-22`):

1. Cache key `membership:resolved:{systemUserId:D}:{entityType}:{optionsHash}`, 5-min TTL (Phase 1A, FR-1A.8).
2. On miss: discover descriptors → filter by `options.Roles` + `options.IdentityTypes` → resolve identity → build single `<filter type="or">` FetchXml with one `<condition>` per (descriptor, identity value) pair.
3. Execute via `IGenericEntityService.RetrieveMultipleAsync(FetchExpression)`.
4. Materialize: dedupe ids, sort ascending, build `byRole` map by re-classifying each result row against descriptors.
5. Apply paging via opaque `continuationToken` (deterministic sort + skip/take).
6. Cache + return `MembershipResponse`.

Failure isolation:
- `DiscoverAsync` throws → propagate (caller's input is invalid).
- `IdentityNormalizationService` per-path failures → empty fields, never throws (except OperationCanceled).
- Fetch query failure → propagate to endpoint layer (`MembershipEndpoints.cs`) → ProblemDetails 500.
- Cache read/write failures fail-open (warn + continue).

---

## Phase 2 Junction Architecture (firm in-scope for R3)

Phase 2 ships a materialized junction `sprk_userentityassociation` plus event-driven sync via Service Bus topic `sprk-membership-changes`, with a nightly reconciliation backstop and Redis pub/sub cache invalidation. **Endpoint contract is unchanged from Phase 1A** — consumers see no API difference.

### Components

| Component | File | Purpose |
|---|---|---|
| Junction entity schema | [`docs/data-model/sprk_userentityassociation.md`](../data-model/sprk_userentityassociation.md) | 7 cols + composite alternate key `sprk_uea_natural_key` on 5-tuple `{personId, personIdType, entityLogicalName, entityRecordId, sourceField}` |
| Wire-format event | `Services/Ai/Membership/Events/MembershipChangedEvent.cs` | Enum-as-string serialization for schema-version stability; `CorrelationId` is `required` (NFR-08); `OccurredOnUtc` for forensics |
| Topic publisher | `Services/Ai/Membership/Events/MembershipEventPublisher.cs` | Publishes to topic `sprk-membership-changes` (D3); fire-and-forget per Q2 (mutation succeeds even if publish fails) |
| Subscription host | `Services/Ai/Membership/MembershipJunctionUpdaterHost.cs` | Service Bus consumer for `recon-junction-updater` subscription |
| Junction handler | `Services/Ai/Membership/MembershipJunctionUpdater.cs` | Idempotent retrieve-by-alternate-key + create/update/delete per FR-2P2.4 |
| Reconciliation backstop | `Services/Ai/Membership/MembershipReconciliationJob.cs` | Nightly recon scan of source-of-truth Lookups → synthesizes events → dispatches directly to `IMembershipJunctionUpdater` (no topic dependency, see `MembershipReconciliationJob.cs:50-60`) |
| Cache invalidator | `Services/Ai/Membership/MembershipCacheInvalidator.cs` | Redis pub/sub publisher to channel `membership-cache-invalidate` (FR-2P2.8) |
| Cache subscriber | `Services/Ai/Membership/MembershipCacheInvalidationSubscriber.cs` | Listens on channel; clears matching `membership:resolved:*` entries |

### Event flow

```
BFF mutation endpoint (e.g., POST /api/matters/{id})
    │
    │ 1. write to Dataverse (matter row)
    │ 2. fire-and-forget publish (Q2 semantics)
    ▼
MembershipEventPublisher.PublishAsync(MembershipChangedEvent)
    │
    ▼
Service Bus topic: sprk-membership-changes (D3 owner decision)
    │
    ├── subscription: recon-junction-updater
    │     │
    │     ▼
    │   MembershipJunctionUpdaterHost (BackgroundService)
    │     │
    │     ▼
    │   MembershipJunctionUpdater.HandleAsync(event)
    │     │  RetrieveByAlternateKey(sprk_uea_natural_key)
    │     │   ├── hit  → Update OR Delete
    │     │   └── miss → Create
    │     ▼
    │   sprk_userentityassociation (junction row)
    │
    └── (future subscriptions: cache warmers, Teams notifiers, etc.)

                Independently, after junction write:
                    │
                    ▼
        MembershipCacheInvalidator.PublishAsync(...)
                    │
                    ▼  Redis pub/sub channel: membership-cache-invalidate
                    │
                    ▼
        Subscribers clear `membership:resolved:{userId}:*`

                Nightly backstop (Q2 fire-and-forget needs this):
                    │
                    ▼
        Spaarke.Scheduling triggers MembershipReconciliationJob (24h)
                    │  Scan source-of-truth Lookups on FR-2P2.5 entity set
                    │  Synthesize MembershipChangedEvent per (parent, field, identity)
                    ▼  Dispatch DIRECTLY to IMembershipJunctionUpdater
                    │  (NO topic — recon ships safe before topic operator-deploy)
                    ▼
        sprk_userentityassociation reconciled to source-of-truth
```

### Strangler-fig migration (Phase 1A → Phase 2)

`MembershipResolverService` internally chooses the source. Phase 1A: per-request FetchXml against target entity. Phase 2: junction-table query. The cache key + response shape are identical. Consumers calling `IMembershipResolverService.ResolveAsync(...)` see no change when storage swaps.

**Phase 2 trigger threshold** from design: `p95 > 500ms for the endpoint sustained`. R3 ships Phase 2 preemptively to lock in the durability + scale ceiling before consumer count grows.

### Q4 nightly recon is LOAD-BEARING

Task 080's inventory finding §3A surfaced: the 8 `sprk_assigned*` Lookups on `sprk_matter` (plus 2 on `sprk_task`, 2 on `sprk_opportunity`) are **NOT mutated by any BFF endpoint** — they're exclusively maker-portal / Power Automate / plugin edits. Real-time event publishing (tasks 081–083) therefore covers only a tiny subset of identity-Lookup mutations. The nightly `MembershipReconciliationJob` is the load-bearing path for keeping the junction table fresh against those source-of-truth Lookups. Max staleness = 24h.

---

## Reference Consumers

| Consumer | File | Pattern |
|---|---|---|
| `LookupUserMembershipNodeExecutor` | `Services/Ai/Nodes/LookupUserMembershipNodeExecutor.cs` (R3 task 041) | In-process playbook node executor (`ActionType=52`); injects `IMembershipResolverService`; exposes `ids[]`, `byRole`, `count` to downstream nodes via Handlebars |
| `MembershipReconciliationJob` | `Services/Ai/Membership/MembershipReconciliationJob.cs` (R3 task 085) | Nightly recon (24h max staleness); second reference consumer of `Spaarke.Scheduling` (ADR-036) |
| Workspace UI surfaces ("My Matters", etc.) | (R4) | Call user-facing endpoint via `@spaarke/auth` `authenticatedFetch`; entity-scoped session caching |
| Future: cache-warming subscriber | (R4) | Topic subscription beside `recon-junction-updater` — warms `membership:resolved:*` proactively |

---

## Performance Targets

- **p95 ≤ 300ms** for `/api/users/me/memberships/{entityType}` (spec NFR-04 / AC-1A.5).
- **Measurement**: App Insights server-side request telemetry (owner clarification 2026-06-20 — NOT synthetic load test).
- **In-process canary**: Task 056's perf test runs in CI against a mocked Dataverse; current measurement p95 = 1 ms (well under budget — the budget exists for production Dataverse latency).
- **Cache hit ratios**: discovery cache 60-min TTL; identity cache 10-min TTL; resolved cache 5-min TTL Phase 1A (longer + pub/sub-invalidated Phase 2).

---

## Failure Modes & Recovery

| Failure | Recovery |
|---|---|
| Event publish failure (Service Bus down) | Q2 fire-and-forget — mutation succeeds; structured Warning log with correlationId (NFR-08). Nightly `MembershipReconciliationJob` is the backstop (max 24h staleness). |
| Topic unavailable (operator hasn't deployed task 071) | ADR-032 Null-Object peers — `NullMembershipEventPublisher` + `NullMembershipJunctionUpdaterHost` + `NullMembershipCacheInvalidator` register when feature flag off. BFF still ships. Phase 1A endpoint unaffected. |
| Junction row drift (event lost, mid-edit failure) | Recon job dispatches `Updated` events directly to `IMembershipJunctionUpdater` (no topic). Handler is idempotent (retrieve-by-alternate-key + create/update/delete) — duplicate dispatch is safe. |
| Cache stale after junction write | `MembershipCacheInvalidator` publishes to Redis channel `membership-cache-invalidate`. Subscriber clears `membership:resolved:{userId}:*`. If pub/sub fails, 5-min TTL is the correctness backstop — pub/sub is latency optimization, not correctness. |
| Redis unavailable | Cache read/write failures fail-open (warn + continue). Discovery + identity + resolved paths all re-execute against Dataverse. Endpoint stays available (degraded latency). |
| Org-membership unset (`OrganizationLookup:UserLookupField` empty) | `OrganizationMembershipResolver` returns empty list + Info log ONCE per process (operator setup pending; not an error). |
| `includeRelated` > 1 hop | `400 BadRequest` with ProblemDetails `type="transitive-chain-too-deep"` (Q3 cap). |

---

## Configuration (`appsettings.json`)

```json
{
  "Membership": {
    "MetadataCacheTtlMinutes": 60,
    "IncludedIdentityTables": ["systemuser", "contact", "team", "businessunit", "account", "sprk_organization"],
    "GlobalFieldExclusions": ["createdby", "modifiedby", "createdonbehalfby", "modifiedonbehalfby"],
    "OrganizationLookup": {
      "UserLookupField": "sprk_owneruser"
    },
    "EntityOverrides": {
      "sprk_matter": {
        "FieldRoleOverrides": {
          "sprk_assignedlawfirm1": "assignedLawFirm",
          "sprk_assignedlawfirm2": "assignedLawFirm"
        },
        "ExcludedFields": [],
        "IncludedFields": []
      }
    }
  }
}
```

Per-entity overrides are sparse — only edge cases require entries. The CamelCase strategy + 6-identity-table filter handles the common case automatically.

---

## Related ADRs / Patterns

| ADR / Pattern | Relationship |
|---|---|
| [ADR-034 concise](../../.claude/adr/ADR-034-user-record-membership.md) | This pattern's binding ADR (MUST / MUST NOT) |
| [ADR-034 full](../adr/ADR-034-user-record-membership.md) | Full ADR history + alternatives considered |
| [ADR-009 Redis caching](../../.claude/adr/ADR-009-redis-caching.md) | Per-user membership cache; metadata cache; pub/sub invalidation |
| [ADR-010 DI minimalism](../../.claude/adr/ADR-010-di-minimalism.md) | Interface seams allowed for testing (`IMembershipResolverService`, etc.) |
| [ADR-013 AI architecture](../../.claude/adr/ADR-013-ai-architecture.md) | Membership services live under `Services/Ai/Membership/` |
| [ADR-024 polymorphic resolver](../../.claude/adr/ADR-024-polymorphic-resolver-pattern.md) | `(personId, personIdType)` tuple instead of polymorphic Lookup |
| [ADR-028 Spaarke Auth v2](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) | OBO on user endpoint; cross-ref via `azureactivedirectoryobjectid` |
| [ADR-032 Null-Object kill-switch](../../.claude/adr/ADR-032-bff-nullobject-kill-switch.md) | Applied to publisher / junction-updater-host / cache-invalidator |
| [ADR-036 background-job infrastructure](../../.claude/adr/ADR-036-background-job-infrastructure.md) | `MembershipReconciliationJob` is the second reference consumer of `Spaarke.Scheduling` |
| [Pattern: node-executor-authoring](../../.claude/patterns/ai/node-executor-authoring.md) | Used by `LookupUserMembershipNodeExecutor` |

---

## Code Entry Points

All paths relative to `src/server/api/Sprk.Bff.Api/` unless noted.

### Phase 1A — endpoint + orchestration + discovery + normalization

| Component | File | Lines (key sections) |
|---|---|---|
| User endpoint | `Api/Membership/MembershipEndpoints.cs` | `:93` `RequireAuthorization()`; `:102` `MapGet("/{entityType}", ...)` |
| Admin endpoints | `Api/Admin/MembershipAdminEndpoints.cs` | `:45-46` `MapGroup("/api/admin/membership").RequireAuthorization("SystemAdmin")`; `:58` GET discovered; `:77` POST refresh-metadata |
| Orchestrator | `Services/Ai/Membership/MembershipResolverService.cs` | `:64-80` type + cache prefix; pipeline doc-comment `:1-41` |
| Discovery service | `Services/Ai/Membership/MembershipFieldDiscoveryService.cs` | `:59` type; `:69` cache prefix; algorithm `:12-23` |
| Identity normalization | `Services/Ai/Membership/IdentityNormalizationService.cs` | `:40` type; `:48` cache prefix; 6 paths `:7-15` |
| Organization-lookup resolver | `Services/Ai/Membership/OrganizationMembershipResolver.cs` | `:38-51` type + dual-interface implementation |
| Options | `Services/Ai/Membership/MembershipOptions.cs` | binds `Membership:*` section |
| DTOs | `Services/Ai/Membership/Models/MembershipResponse.cs` · `Models/PersonIdentity.cs` · `Models/MembershipDescriptor.cs` | camelCase JSON locked at type level |
| DI module | `Infrastructure/DI/MembershipModule.cs` | `:65` `MembershipModule.AddMembership(IServiceCollection)` — unconditional registration |

### Phase 2 — event-driven sync + recon + cache invalidation

| Component | File | Lines |
|---|---|---|
| Wire-format event | `Services/Ai/Membership/Events/MembershipChangedEvent.cs` | `:46-50` type; contract notes `:10-30` |
| Mutation type enum | `Services/Ai/Membership/Events/MembershipMutationType.cs` | Added/Updated/Removed |
| Person identity type enum | `Services/Ai/Membership/Events/PersonIdentityType.cs` | User/Contact/Team/Organization (pinned ints 1..4) |
| Topic publisher | `Services/Ai/Membership/Events/MembershipEventPublisher.cs` | fire-and-forget per Q2 |
| Null publisher (kill-switch) | `Services/Ai/Membership/Events/NullMembershipEventPublisher.cs` | ADR-032 peer |
| Subscription host | `Services/Ai/Membership/MembershipJunctionUpdaterHost.cs` | Service Bus consumer (Phase 2) |
| Null subscription host | `Services/Ai/Membership/NullMembershipJunctionUpdaterHost.cs` | ADR-032 peer |
| Junction handler | `Services/Ai/Membership/MembershipJunctionUpdater.cs` | `:5-50` algorithm; idempotent retrieve+update/create/delete |
| Recon backstop | `Services/Ai/Membership/MembershipReconciliationJob.cs` | `:14-46` algorithm; `:50-60` topic-independence rationale |
| Cache invalidator | `Services/Ai/Membership/MembershipCacheInvalidator.cs` | `:38-50` type; mirrors `JobStatusService` Redis pub/sub convention |
| Cache subscriber | `Services/Ai/Membership/MembershipCacheInvalidationSubscriber.cs` | Channel listener |
| Null cache invalidator | `Services/Ai/Membership/NullMembershipCacheInvalidator.cs` | ADR-032 peer |

### Reference consumers

| Consumer | File |
|---|---|
| Playbook node executor (`ActionType=52`) | `Services/Ai/Nodes/LookupUserMembershipNodeExecutor.cs` |
| Junction data-model doc | [`docs/data-model/sprk_userentityassociation.md`](../data-model/sprk_userentityassociation.md) |
| Org-lookup decision record | `projects/spaarke-platform-foundations-r3/notes/sprk-organization-mapping-decision.md` |

---

## Known Pitfalls

| Pitfall | Mitigation |
|---|---|
| **Confusing `AssociationResolver` (PCF) with Membership resolution (BFF)** | See Naming-Collision Register above. Different surfaces, different concepts, both stable. |
| **Re-deriving membership in ad-hoc FetchXML** | This is the R2-UAT root cause (A1 / D5). Always go through `IMembershipResolverService`. |
| **Joining through `sprk_matterteammember` or other non-existent entity** | The R2-broken `notification-new-documents.json` playbook was migrated in R3 task 050 to use `LookupUserMembership` node + `joinIds` Handlebars helper. |
| **Assuming Phase 1A FetchXml scales forever** | Monitor AC-1A.5 p95. Phase 2 junction table is the escape hatch — but R3 already shipped it, so the escape is in-process. |
| **Creating new `PlatformAdmin` policy** | Forbidden (Q6). Reuse existing `SystemAdmin` policy at `AuthorizationModule.cs:241`. |
| **`includeRelated` chains > 1 hop** | Reject with 400 before any Dataverse query (Q3). |
| **Mapping `sprk_assignedlawfirm*` to `Contact`** | Wrong (was an error in design.md). They are Lookup → `sprk_organization` → `identityType="Organization"` per Q4. Handled by per-entity `FieldRoleOverrides`. |
| **Synchronous wait on event publish** | Q2 forbids it. Mutation succeeds independent of publish. Recon job is the backstop. |
| **Reusing `ServiceBusJobProcessor` queue for membership events** | Forbidden (D3). Use the new topic `sprk-membership-changes` with subscription-per-consumer. |

---

## See Also

- [ADR-034 concise](../../.claude/adr/ADR-034-user-record-membership.md) — binding constraints
- [ADR-034 full](../adr/ADR-034-user-record-membership.md) — alternatives considered, full history
- [`docs/data-model/sprk_userentityassociation.md`](../data-model/sprk_userentityassociation.md) — junction schema
- [`projects/spaarke-platform-foundations-r3/spec.md`](../../projects/spaarke-platform-foundations-r3/spec.md) Part 1 — FR-1A.* / FR-1B.* / FR-1C.* / FR-1D.* / FR-2P2.* / AC-1A.* / AC-1B.* / AC-1C.* / AC-1D.* / AC-1P2.*
- [`projects/spaarke-platform-foundations-r3/notes/sprk-organization-mapping-decision.md`](../../projects/spaarke-platform-foundations-r3/notes/sprk-organization-mapping-decision.md) — Q4 sprk_organization mapping decision
- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — BFF pre-merge checklist (§§A, F.1)
- [background-workers-architecture.md](background-workers-architecture.md) — full BFF BackgroundService inventory (recon job + junction host live here)
- [playbook-architecture.md](playbook-architecture.md) — `LookupUserMembership` node (ActionType=52) sits in the node-executor framework
- [caching-architecture.md](caching-architecture.md) — Redis cache tiers used by membership (5/10/60-min TTLs)
- [AI-ARCHITECTURE.md](AI-ARCHITECTURE.md) — Spaarke AI platform overview (parent)
