# Membership Resolution Pattern

> **Last Updated**: 2026-06-22
> **Last Reviewed**: 2026-06-22 (Wave 28 W28C update — Daily Briefing wiring gap closed)
> **Reviewed By**: spaarke-platform-foundations-r3 task 104 (initial author) + AS-BUILT refresh (Wave 26) + Daily Briefing wiring closeout (Wave 28 / GitHub #229)
> **Status**: Verified against shipped code (Wave 28 — Daily Briefing now consumes the resolver)
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

`MembershipFieldDiscoveryService` (`Services/Ai/Membership/MembershipFieldDiscoveryService.cs:59`) queries Dataverse `EntityDefinitions` at runtime for any entity type, automatically discovers Lookup attributes whose targets are one of the 6 configured identity tables, and derives a role name from the field's logical name. Per-entity overrides cover edge cases.

### Algorithm (5 steps; see `MembershipFieldDiscoveryService.cs:12-23` header comment)

1. **Cache lookup** — Redis key `membership:discovery:{entityType}` (`CacheKeyPrefix` at line 69), TTL `MembershipOptions.MetadataCacheTtlMinutes` (default 60 min, ADR-009).
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

`IdentityNormalizationService` (`Services/Ai/Membership/IdentityNormalizationService.cs:40`) resolves a `systemuserid` into the full `PersonIdentity` by querying 6 paths. Each path is independent: failure on one does NOT fail the others (per-path try/catch + warning log). Result cached in Redis (`CacheKeyPrefix = "membership:identity:"` at line 48) for 10 minutes per ADR-009.

| # | Source field type | Resolves via | Returned field |
|---|---|---|---|
| 1 | `Lookup → systemuser` | Direct row read | `systemUserId`, `businessUnitId`, `primaryEmail`, `azureActiveDirectoryObjectId` |
| 2 | `Lookup → contact` | Cross-ref via `azureactivedirectoryobjectid` (ADR-028) | `contactId` |
| 3 | `Lookup → team` | Expand `teammembership` to user's teams | `teamIds[]` (cached) |
| 4 | `Lookup → businessunit` | User's BU; descendants configurable per role | `businessUnitId` |
| 5 | `Lookup → account` | User's contact's `parentcustomerid` (if account) | `accountId` (when applicable) |
| 6 | `Lookup → sprk_organization` | Delegated to `IIdentityOrganizationResolver` (configurable user→org Lookup field; see `OrganizationMembershipResolver.cs:53-54` + `notes/sprk-organization-mapping-decision.md`) | `organizationIds[]` |

Steps 1–3 run in parallel via `Task.WhenAll`. Steps 4–5 are sequential after the contact lookup. Failure-soft fallback on path 6 returns empty list + Info log when `Membership:OrganizationLookup:UserLookupField` is unset (operator setup pending is not an error).

**Text matching**: substring `like` on `primaryEmail` is supported as a documented separate code path. **Free-text display-name matching is forbidden** — too fuzzy, brittle (ADR-034 MUST NOT).

---

## Orchestration

`MembershipResolverService` (`Services/Ai/Membership/MembershipResolverService.cs:64`) combines discovery + normalization + a single OR-joined FetchXml query against the target entity. Pipeline (algorithm doc-comment at `MembershipResolverService.cs:5-22`; cache key prefix at `:76`; TTL at `:79`):

1. Cache key `membership:resolved:{systemUserId:D}:{entityType}:{optionsHash}`, 5-min TTL (Phase 1A, FR-1A.8).
2. On miss: discover descriptors → filter by `options.Roles` + `options.IdentityTypes` → resolve identity → build single `<filter type="or">` FetchXml with one `<condition>` per (descriptor, identity value) pair.
3. Execute via `IGenericEntityService.RetrieveMultipleAsync(FetchExpression)`.
4. Materialize: dedupe ids, sort ascending, build `byRole` map by re-classifying each result row against descriptors.
5. Apply paging via opaque `continuationToken` (deterministic sort + skip/take).
6. Cache + return `MembershipResponse`.

Phase 1D `includeRelated` is pre-validated at `MembershipResolverService.cs:142-159` — explicit chain syntax (`documents.events`, `documents/events`) is rejected with `MembershipDepthExceededException` before any I/O.

Failure isolation:
- `DiscoverAsync` throws → propagate (caller's input is invalid).
- `IdentityNormalizationService` per-path failures → empty fields, never throws (except OperationCanceled).
- Fetch query failure → propagate to endpoint layer (`MembershipEndpoints.cs`) → ProblemDetails 500.
- Cache read/write failures fail-open (warn + continue).

---

## Phase 2 Junction Architecture (shipped in R3; operator-gated)

Phase 2 ships a materialized junction `sprk_userentityassociation` plus event-driven sync via Service Bus topic `sprk-membership-changes`, with a nightly reconciliation backstop and Redis pub/sub cache invalidation. **Endpoint contract is unchanged from Phase 1A** — consumers see no API difference.

### Components (verified AS-BUILT, Wave 17-26)

| Component | File | Purpose | Default state |
|---|---|---|---|
| Junction entity schema | [`docs/data-model/sprk_userentityassociation.md`](../data-model/sprk_userentityassociation.md) | 7 cols + composite alternate key `sprk_uea_natural_key` on 5-tuple `{personId, personIdType, entityLogicalName, entityRecordId, sourceField}` | Deployed via task 070 |
| Wire-format event | `Services/Ai/Membership/Events/MembershipChangedEvent.cs:68` | Enum-as-string serialization for schema-version stability; `CorrelationId` is `required` (NFR-08); `OccurredOnUtc` for forensics | n/a |
| Mutation type enum | `Services/Ai/Membership/Events/MembershipMutationType.cs` | Added / Updated / Removed | n/a |
| Person identity type enum | `Services/Ai/Membership/Events/PersonIdentityType.cs` | User / Contact / Team / Organization (pinned ints 1..4 matching the Dataverse OptionSet) | n/a |
| Topic publisher (real) | `Services/Ai/Membership/Events/MembershipEventPublisher.cs:32` | Publishes to topic configured by `MembershipEventPublisherOptions.TopicName` (default `sprk-membership-changes`); fire-and-forget per Q2 | Gated by `Membership:EventPublisher:Enabled` (default **false**) |
| Topic publisher (Null peer) | `Services/Ai/Membership/Events/NullMembershipEventPublisher.cs:29` | ADR-032 P2 Quiet no-op — logs Info + returns; no Service Bus interaction | **Active by default** |
| Subscription host (real) | `Services/Ai/Membership/MembershipJunctionUpdaterHost.cs` | `BackgroundService` consuming the `recon-junction-updater` subscription via `DefaultAzureCredential` (ADR-028); 30s drain on stop (NFR-07) | Gated by `Membership:JunctionUpdater:Enabled` (default **false**) |
| Subscription host (Null peer) | `Services/Ai/Membership/NullMembershipJunctionUpdaterHost.cs:47` | ADR-032 hosted-service peer — no `ServiceBusClient` constructed; logs Info on start | **Active by default** |
| Junction handler | `Services/Ai/Membership/MembershipJunctionUpdater.cs:76` | Idempotent retrieve-by-alternate-key + create/update/delete per FR-2P2.4; Scoped lifetime; **ALWAYS registered** (no kill-switch — reused by both subscription host AND recon job) | Always active |
| Reconciliation backstop | `Services/Ai/Membership/MembershipReconciliationJob.cs:113` (algorithm header `:14-46`; topic-independence rationale `:51-60`) | Nightly recon scan of source-of-truth Lookups → synthesizes events → dispatches DIRECTLY to `IMembershipJunctionUpdater` (no topic dependency) | `Membership:Reconciliation:Enabled` defaults **true**; cron `0 2 * * *` daily 02:00 UTC |
| Cache invalidator (real) | `Services/Ai/Membership/MembershipCacheInvalidator.cs:38` | Redis pub/sub publisher to channel `membership-cache-invalidate` (FR-2P2.8); mirrors `JobStatusService` convention | Gated by `Membership:CacheInvalidator:Enabled` (default **false**) **AND** `IConnectionMultiplexer` registered (Redis enabled) |
| Cache subscriber | `Services/Ai/Membership/MembershipCacheInvalidationSubscriber.cs` | Hosted service that subscribes on `StartAsync`, evicts matching `membership:resolved:{personId:D}:{entityLogicalName}:*` entries via Redis SCAN+DEL | Registered alongside real invalidator |
| Cache invalidator (Null peer) | `Services/Ai/Membership/NullMembershipCacheInvalidator.cs:28` | ADR-032 P2 — logs once at construction; debug-only per-call log | **Active by default** |
| Cache invalidation message | `Services/Ai/Membership/MembershipCacheInvalidationMessage.cs` | Wire payload for the Redis channel | n/a |
| DI module | `Infrastructure/DI/MembershipModule.cs:65` (`AddMembership(services, configuration)` at `:73-75`; bootstrap hosted service at `:313`) | Unconditional resolver/discovery/identity registrations; SYMMETRIC kill-switched registrations for publisher/host/invalidator; recon job seeds `BackgroundJobDefinition` row | n/a |

### Phase 1A Read Path Flow

```
HTTP GET /api/users/me/memberships/{entityType}?roles=...&limit=...
    │
    │ Spaarke Auth v2 OBO — JWT validated, oid claim extracted
    ▼
MembershipEndpoints.GetMyMembershipsAsync (MembershipEndpoints.cs:138)
    │
    │ 1. ExtractAadObjectId(User)                         (:344)
    │ 2. ResolveSystemUserIdAsync(oid, ...)               (:377)
    │     ├── Redis hit  → cached systemuserid
    │     └── Redis miss → systemuser.azureactivedirectoryobjectid=oid
    │                       (10-min TTL, ADR-028)
    │ 3. Build MembershipResolveOptions from query CSV   (:243)
    ▼
IMembershipResolverService.ResolveAsync(systemUserId, entityType, options, ct)
    │ MembershipResolverService.cs:64
    │
    │ Cache key: membership:resolved:{systemUserId:D}:{entityType}:{optionsHash}
    │  └── 5-min TTL (Phase 1A, FR-1A.8)
    │
    ▼ Cache MISS:
    ├─→ IMembershipFieldDiscoveryService.DiscoverAsync(entityType)
    │     │ MembershipFieldDiscoveryService.cs:59
    │     │ Cache key: membership:discovery:{entityType}  (60-min TTL)
    │     │ On miss: MetadataService.RetrieveEntityRequest → classify Lookups
    │     ▼
    │   IReadOnlyList<MembershipDescriptor>
    │
    ├─→ IIdentityNormalizationService.ResolveAsync(systemUserId)
    │     │ IdentityNormalizationService.cs:40
    │     │ Cache key: membership:identity:{systemUserId}  (10-min TTL)
    │     │ 6 paths in parallel/sequential per the contract
    │     ▼
    │   PersonIdentity { systemUserId, contactId?, teamIds[]?, BU, accountId?, orgIds[]? }
    │
    └─→ Build single OR-FetchXml against entityType
          One <condition> per (descriptor, identity-value) pair
          IGenericEntityService.RetrieveMultipleAsync(FetchExpression)
          Materialize: dedupe ids, sort, build byRole map
          Apply paging (continuationToken if matches > limit)
          ▼
        MembershipResponse {entityType, personIdentity, ids[], byRole, count, cacheExpiresAt, continuationToken?}
          ▼
        Cache write (5-min TTL) — failure is fail-open
          ▼
        HTTP 200 OK (camelCase JSON locked at type level)
```

### Phase 2 Mutation + Sync Flow

```
BFF mutation endpoint
  (DataverseDocumentsEndpoints.cs:31 POST /api/v1/documents
   OfficeEndpoints.cs:1149 POST QuickCreate (matter)
   EventEndpoints.cs:329 POST event-create
   OfficeService.cs:38 used by save-document Office add-in path)
    │
    │ 1. Write to Dataverse (matter / document / event / etc.)
    │ 2. Construct MembershipChangedEvent  (CorrelationId = HttpContext.TraceIdentifier)
    │ 3. Fire-and-forget: _ = membershipEventPublisher.PublishAsync(evt, ct);
    ▼
IMembershipEventPublisher (SYMMETRIC registration per MembershipModule.cs:127-163)
    ├── Enabled=true  → MembershipEventPublisher.cs:32
    │                    Serialize → ServiceBusSender → topic
    └── Enabled=false → NullMembershipEventPublisher.cs:29 (DEFAULT)
                        Logs Info; returns Task.CompletedTask
    │
    ▼ (Enabled=true path only)
Azure Service Bus topic: sprk-membership-changes  (Bicep task 071; operator-deploy gated)
    │
    ├── subscription: recon-junction-updater
    │     │
    │     ▼
    │   MembershipJunctionUpdaterHost (BackgroundService — only when Enabled=true)
    │     │ Resolves IServiceScopeFactory.CreateScope() per message
    │     ▼
    │   MembershipJunctionUpdater.HandleAsync(event)   (MembershipJunctionUpdater.cs:76)
    │     │  RetrieveByAlternateKey(sprk_uea_natural_key)
    │     │   ├── hit  → Added/Updated → Update; Removed → Delete
    │     │   └── miss → Added/Updated → Create;  Removed → no-op (idempotent)
    │     ▼
    │   sprk_userentityassociation (junction row written)
    │     │
    │     ▼
    │   IMembershipCacheInvalidator.PublishInvalidationAsync(...)
    │       ├── real → Redis pub/sub channel `membership-cache-invalidate`
    │       │            │
    │       │            ▼
    │       │   MembershipCacheInvalidationSubscriber (every BFF instance)
    │       │            │
    │       │            ▼
    │       │   SCAN + DEL `{instanceName}membership:resolved:{personId:D}:{entity}:*`
    │       │            (next read repopulates from junction / FetchXml)
    │       └── Null (default) → debug-log no-op (5-min TTL is correctness backstop)
    │
    └── (Future subscriptions: cache warmers, Teams notifiers, etc. — none shipped in R3)


Nightly backstop (LOAD-BEARING — see "Q4 nightly recon" below):
    │
    ▼
Spaarke.Scheduling triggers MembershipReconciliationJob (cron 0 2 * * *, Enabled=true by default)
    │  For each entity in MembershipReconciliationOptions.EntityTypes:
    │   1. Discover identity-Lookup descriptors (cached)
    │   2. Scan parent rows; synthesize Updated events
    │   3. Scan junction rows; synthesize Removed events for orphans
    │
    ▼  Dispatch DIRECTLY to IMembershipJunctionUpdater  (NO topic involvement)
    │   (Handler is registered unconditionally — recon ships safe before topic operator-deploy)
    ▼
sprk_userentityassociation reconciled to source-of-truth (24h max staleness)
```

### Strangler-fig migration (Phase 1A → Phase 2)

`MembershipResolverService` internally chooses the source. Phase 1A (currently active in all environments): per-request FetchXml against target entity. Phase 2: junction-table query (Phase 2 read-path swap is **not yet shipped** — task 086 ships the *invalidation* infrastructure; the resolver's read-path swap to query the junction table is a future R4 task gated on operator deploy of the topic and a sustained-load trigger). The cache key + response shape are identical. Consumers calling `IMembershipResolverService.ResolveAsync(...)` see no change when storage swaps.

**Phase 2 trigger threshold** from design: `p95 > 500ms for the endpoint sustained`. R3 shipped the Phase 2 write-path + recon + invalidation preemptively to lock in durability + scale ceiling before consumer count grows.

### Q4 nightly recon is LOAD-BEARING

Task 080's inventory finding §3A surfaced: the 8 `sprk_assigned*` Lookups on `sprk_matter` (plus 2 on `sprk_task`, 2 on `sprk_opportunity`) are **NOT mutated by any BFF endpoint** — they're exclusively maker-portal / Power Automate / plugin edits. Real-time event publishing (tasks 081–083) therefore covers only a tiny subset of identity-Lookup mutations. The nightly `MembershipReconciliationJob` is the load-bearing path for keeping the junction table fresh against those source-of-truth Lookups. Max staleness = 24h. The recon job ships **enabled by default** (`MembershipReconciliationOptions.Enabled = true`) and is independent of the topic deploy — it reuses `IMembershipJunctionUpdater` directly via `IServiceScopeFactory.CreateScope()`.

---

## Wiring + Consumer Inventory (AS-BUILT)

This section enumerates every BFF surface that currently consumes the membership feature. Each entry is verified via grep on the interface name across `src/server/`.

### Consumers of `IMembershipResolverService` (Phase 1A read path)

| Consumer | File | Status | Notes |
|---|---|---|---|
| `LookupUserMembershipNodeExecutor` | `Services/Ai/Nodes/LookupUserMembershipNodeExecutor.cs:70` | **Shipped — production wired** | Playbook node executor for `ActionType=52` (added task 040). Singleton-with-Scoped DI pattern via `IServiceScopeFactory`. Binds `{ids[], byRole, count, continuationToken, cacheExpiresAt}` to the node's `OutputVariable` for Handlebars consumption (e.g., `{{joinIds myMatters.ids}}`). |
| `GET /api/users/me/memberships/{entityType}` | `Api/Membership/MembershipEndpoints.cs:102` | **Shipped — production wired** | Single user-facing HTTP endpoint. Auth: `RequireAuthorization()` default JWT (line 93). |
| `BriefingService.GetTopPriorityMatterAsync` (`GET /api/workspace/briefing`) | `Services/Workspace/BriefingService.cs:172` | **Shipped — production wired (Wave 28 / GitHub #229 closeout, 2026-06-22)** | Replaces the prior STUB that returned hardcoded mock matter data. Resolves AAD `oid` → `systemuserid` via the same `systemuser.azureactivedirectoryobjectid` cross-reference algorithm as `MembershipEndpoints.ResolveSystemUserIdAsync` (10-min Redis cache under sibling prefix `membership:briefing-currentuser:`), calls `IMembershipResolverService.ResolveAsync(systemUserId, "sprk_matter", options: null, ct)`, queries Dataverse for matter detail rows by resolved IDs, applies the deterministic heuristic (max overdue events; tie-break = highest utilization; final tie-break = matter name). Failure-soft: any AAD-oid/resolver/Dataverse failure returns `null` TopMatter (briefing remains fully populated). Non-Guid `oid` short-circuits to `null` without I/O. Unit tests: `tests/unit/Sprk.Bff.Api.Tests/Services/Workspace/BriefingServiceTests.cs` (7 scenarios). |

> **Notes**: `Services/Ai/NodeService.cs:983` and `Services/Ai/Nodes/INodeExecutor.cs:139` contain only documentation references in comments — neither consumes the resolver.

### Migrated playbooks (consume `IMembershipResolverService` indirectly via the `LookupUserMembership` node)

All three notification playbooks were migrated in R3 Waves 9-10 (tasks 050-052) from the broken `sprk_matterteammember` FetchXML pattern (A1 / D5 root cause) to the new `LookupUserMembership` node + `{{joinIds}}` Handlebars helper.

| Playbook | File | Migration task | Verified |
|---|---|---|---|
| `notification-new-documents.json` (NP-003) | `projects/spaarke-daily-update-service/notes/playbooks/notification-new-documents.json` | Task 050 | Node `LookupUserMembership` present; resolved IDs bound to `myMatters.ids` (line 64) |
| `notification-new-emails.json` | `projects/spaarke-daily-update-service/notes/playbooks/notification-new-emails.json` | Task 051 | Same pattern |
| `notification-new-events.json` | `projects/spaarke-daily-update-service/notes/playbooks/notification-new-events.json` | Task 052 | Same pattern |

Integration coverage: `tests/integration/Sprk.Bff.Api.IntegrationTests/Playbooks/MigratedPlaybookTests.cs` + `MigratedPlaybookFixture.cs` (task 053).

### Consumers of `IMembershipEventPublisher` (Phase 2 publish path)

| Mutation site | File | Trigger | Notes |
|---|---|---|---|
| `POST /api/v1/documents` (Create Document) | `Api/DataverseDocumentsEndpoints.cs:34` | Implicit `ownerid` Lookup on document Create | Per event-source inventory §3B; task 082 |
| `POST /api/office/quick-create/{entityType}` (matter cluster) | `Api/Office/OfficeEndpoints.cs:1153` | Matter Create from Office add-in — implicit `ownerid` | Only BFF-side mutation site for `sprk_matter` per inventory §3A; task 081 |
| `POST /api/events` (Create Event) | `Api/Events/EventEndpoints.cs:332` | Event Create — implicit `ownerid` | Task 083 |
| `OfficeService` (save-document path) | `Services/Office/OfficeService.cs:38` (field) + `:52` (ctor) | Implicit `ownerid` on Office-initiated document creates | Task 081/082 |

All four sites use fire-and-forget semantics (Q2) — the mutation succeeds even when publish fails. Default state: publisher is the `NullMembershipEventPublisher` peer (per `Membership:EventPublisher:Enabled=false`); calls are logged at Info but no Service Bus interaction.

### Consumers of `IMembershipJunctionUpdater` (Phase 2 write path — internal)

| Consumer | File | Notes |
|---|---|---|
| `MembershipJunctionUpdaterHost` (Service Bus subscription pump) | `Services/Ai/Membership/MembershipJunctionUpdaterHost.cs` | Resolves Scoped handler per message via `IServiceScopeFactory.CreateScope()` |
| `MembershipReconciliationJob` (nightly recon) | `Services/Ai/Membership/MembershipReconciliationJob.cs:113` | Same pattern; bypasses the topic entirely |

Handler is registered unconditionally (no kill-switch) at `MembershipModule.cs:220`.

### Wiring Gaps (flagged — confirmed by grep)

| Surface | Expected to consume? | Reality | Severity |
|---|---|---|---|
| **SprkChat / AI Assistant** (`Services/Ai/Chat/*`) | Possibly — depending on whether host-context "my matters" filtering uses membership semantics | **Does NOT consume `IMembershipResolverService`** (grep confirmed: `Services/Ai/Chat` contains zero references). Chat scoping is currently driven by `HostContext` entity binding + `RagService` filters, not by user-record membership. | **Acceptable for current scope** — Chat operates on a specific bound entity, not on "show me my X". If a future chat surface needs "what matters do I own", route via `IMembershipResolverService`. |
| **Workspace UI surfaces ("My Matters", "My Events")** | YES — per R3 design's reference-consumer list | **Not yet shipped** — R4 work. UI calls the user-facing endpoint via `@spaarke/auth` `authenticatedFetch` with entity-scoped session caching. | Tracked as R4 scope, not an R3 gap. |
| **Cache-warming subscriber** | Future Phase 2 enhancement | Not implemented in R3. Topic subscription beside `recon-junction-updater` is the natural extension. | R4+ scope. |

> **Closed (Wave 28 / GitHub #229, 2026-06-22)**: The Daily Briefing endpoint (`BriefingService.GetTopPriorityMatterAsync`) previously returned mock data via an in-process STUB. It now consumes `IMembershipResolverService` per ADR-034 — see the "Confirmed Consumers" table above (row 3). Operator follow-up: verify the briefing card's "top priority matter" reflects the caller's actual highest-overdue matter post-deploy (the existing integration tests assert only the response shape, not the matter identity, because the test fixture's `oid` is a non-Guid sentinel that intentionally degrades to `null` TopMatter via the failure-soft path).

---

## Deployment Status (AS-BUILT, 2026-06-22)

This table reflects what is **shipped in the BFF binary today** vs what requires additional operator action.

| Component | Shipped in BFF | Spaarkedev1 (Dataverse + Azure) | Production | Notes |
|---|---|---|---|---|
| BFF endpoint `GET /api/users/me/memberships/{entityType}` | ✅ | ✅ Live | ⏸ Pending Phase 1A enable | `MembershipEndpoints.MapMembershipApi()` wired in `EndpointMappingExtensions.cs:275` |
| BFF endpoint `GET /api/admin/membership/discovered/{entityType}` | ✅ | ✅ Live (SystemAdmin only) | ⏸ Pending | `MembershipAdminEndpoints.MapAdminMembershipEndpoints()` at `EndpointMappingExtensions.cs:283` |
| BFF endpoint `POST /api/admin/membership/refresh-metadata` | ✅ | ✅ Live (SystemAdmin only) | ⏸ Pending | Same group |
| `MembershipResolverService` + `MembershipFieldDiscoveryService` + `IdentityNormalizationService` + `OrganizationMembershipResolver` | ✅ Unconditional DI (`MembershipModule.cs:73-119`) | ✅ Resolvable | ⏸ Pending | Always registered as singletons (ADR-010) |
| Dataverse entity `sprk_userentityassociation` (junction) | n/a | ✅ Deployed (task 070, scripts/Create-UserEntityAssociation.ps1) | ⏸ Pending | Composite alternate key `sprk_uea_natural_key` on 5-tuple |
| Azure Service Bus topic `sprk-membership-changes` + subscription `recon-junction-updater` | n/a (Bicep authored in task 071) | ❌ **NOT deployed** — operator follow-up gated per `notes/operator-followup-task071.md` | ❌ Not deployed | Gates the publisher/host real-impl flags |
| `MembershipEventPublisher` (real impl) | ✅ Code present | Null peer active (publisher disabled) | Null peer active | Flip `Membership:EventPublisher:Enabled=true` after topic deploy |
| `MembershipJunctionUpdaterHost` (real impl) | ✅ Code present | Null peer active (host disabled) | Null peer active | Flip `Membership:JunctionUpdater:Enabled=true` after topic deploy |
| `MembershipJunctionUpdater` (handler) | ✅ Always registered | ✅ Resolvable (used by recon job) | ✅ Resolvable | No kill-switch |
| `MembershipReconciliationJob` + bootstrap | ✅ Always registered | ✅ Running (`Enabled=true` default; cron `0 2 * * *`) | ✅ Running | Independent of topic deploy |
| `MembershipCacheInvalidator` (real impl) | ✅ Code present | Null peer active (default `Enabled=false`) | Null peer active | Requires BOTH `Membership:CacheInvalidator:Enabled=true` AND Redis registered |
| `MembershipCacheInvalidationSubscriber` (hosted service) | ✅ Code present | Not running (Null invalidator path) | Not running | Registered only when invalidator real impl wins |

### Feature flag matrix

| Flag | Default | Options-class | Effect when `true` | Effect when `false` (default) |
|---|---|---|---|---|
| `Membership:EventPublisher:Enabled` | `false` | `MembershipEventPublisherOptions.cs:38` | `MembershipEventPublisher` registered; publishes to Service Bus topic | `NullMembershipEventPublisher` registered; logs Info, no SB interaction |
| `Membership:JunctionUpdater:Enabled` | `false` | `MembershipJunctionUpdaterOptions.cs:65` | `MembershipJunctionUpdaterHost` BackgroundService runs; consumes subscription | `NullMembershipJunctionUpdaterHost` runs; logs once on start, no SB interaction |
| `Membership:CacheInvalidator:Enabled` | `false` (also requires `IConnectionMultiplexer` registered) | `MembershipCacheInvalidatorOptions.cs:47` | `MembershipCacheInvalidator` + `MembershipCacheInvalidationSubscriber` registered | `NullMembershipCacheInvalidator` registered; debug-only logs, no Redis interaction |
| `Membership:Reconciliation:Enabled` | **`true`** | `MembershipReconciliationOptions.cs:73` | Job runs on configured cron (default `0 2 * * *` daily 02:00 UTC) | Job seeded but disabled in `BackgroundJobDefinition` row; can be re-enabled via admin endpoint |
| `Membership:OrganizationLookup:UserLookupField` | (empty) | `MembershipOptions.cs` | `OrganizationMembershipResolver` queries `sprk_organization` filtered by the configured field | Returns empty `organizationIds[]` + Info log ONCE per process |

**Operator runbook** for activating Phase 2 sync (post-topic-deploy):
1. Deploy topic via task 071 Bicep (`notes/operator-followup-task071.md`).
2. Set `Membership:EventPublisher:ServiceBusNamespace`, `Membership:JunctionUpdater:ServiceBusNamespace` in App Service config.
3. Flip `Membership:EventPublisher:Enabled=true`.
4. Flip `Membership:JunctionUpdater:Enabled=true`.
5. (Optional) Flip `Membership:CacheInvalidator:Enabled=true` only after Redis is confirmed enabled.
6. Restart App Service — Null peers are replaced with real impls at startup.

---

## Performance Targets

- **p95 ≤ 300ms** for `/api/users/me/memberships/{entityType}` (spec NFR-04 / AC-1A.5).
- **Measurement**: App Insights server-side request telemetry (owner clarification 2026-06-20 — NOT synthetic load test).
- **In-process canary**: Task 056's perf test runs in CI against a mocked Dataverse; current measurement p95 = 1 ms (well under budget — the budget exists for production Dataverse latency).
- **Cache hit ratios**: discovery cache 60-min TTL; identity cache 10-min TTL; resolved cache 5-min TTL Phase 1A (longer + pub/sub-invalidated Phase 2 when read-path swap ships).

---

## Failure Modes & Recovery

| Failure | Recovery |
|---|---|
| Event publish failure (Service Bus down) | Q2 fire-and-forget — mutation succeeds; structured Warning log with correlationId (NFR-08). Nightly `MembershipReconciliationJob` is the backstop (max 24h staleness). |
| Topic unavailable (operator hasn't deployed task 071) | ADR-032 Null-Object peers — `NullMembershipEventPublisher` + `NullMembershipJunctionUpdaterHost` + `NullMembershipCacheInvalidator` register when feature flags off. BFF still ships. Phase 1A endpoint unaffected. |
| Junction row drift (event lost, mid-edit failure) | Recon job dispatches `Updated` events directly to `IMembershipJunctionUpdater` (no topic). Handler is idempotent (retrieve-by-alternate-key + create/update/delete) — duplicate dispatch is safe. |
| Cache stale after junction write | `MembershipCacheInvalidator` publishes to Redis channel `membership-cache-invalidate`. Subscriber clears `membership:resolved:{personId:D}:{entity}:*` via SCAN+DEL. If pub/sub fails, 5-min TTL is the correctness backstop — pub/sub is latency optimization, not correctness. |
| Redis unavailable | Cache read/write failures fail-open (warn + continue). Discovery + identity + resolved paths all re-execute against Dataverse. Endpoint stays available (degraded latency). |
| Org-membership unset (`OrganizationLookup:UserLookupField` empty) | `OrganizationMembershipResolver` returns empty list + Info log ONCE per process (operator setup pending; not an error). |
| `includeRelated` > 1 hop | `400 BadRequest` with ProblemDetails `type="transitive-chain-too-deep"` (Q3 cap). Pre-validated at `MembershipResolverService.cs:142-159` before any I/O. |
| Caller authenticated but not provisioned as systemuser | `401 Unauthorized` with ProblemDetails (`MembershipEndpoints.cs:212-220`). Logged at Warning with caller oid. |
| Disabled systemuser | Excluded from the AAD-oid → systemuserid lookup (`isdisabled=false` filter at `MembershipEndpoints.cs:425-428`) — same 401 outcome. |

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
    },

    "EventPublisher": {
      "Enabled": false,
      "TopicName": "sprk-membership-changes",
      "ServiceBusNamespace": ""
    },
    "JunctionUpdater": {
      "Enabled": false,
      "TopicName": "sprk-membership-changes",
      "SubscriptionName": "recon-junction-updater",
      "ServiceBusNamespace": "",
      "MaxConcurrentCalls": 4
    },
    "CacheInvalidator": {
      "Enabled": false,
      "Channel": "membership-cache-invalidate"
    },
    "Reconciliation": {
      "Enabled": true,
      "CronSchedule": "0 2 * * *",
      "EntityTypes": ["sprk_matter", "sprk_document", "sprk_event", "sprk_task", "sprk_opportunity"]
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

## Code Entry Points (verified AS-BUILT, 2026-06-22)

All paths relative to `src/server/api/Sprk.Bff.Api/` unless noted. Every line citation verified by reading the file before publishing this revision.

### Phase 1A — endpoint + orchestration + discovery + normalization

| Component | File | Lines (key sections) |
|---|---|---|
| User endpoint group | `Api/Membership/MembershipEndpoints.cs` | `:89` `MapMembershipEndpoints`; `:92-94` group + `RequireAuthorization()`; `:102` `MapGet("/{entityType}", ...)`; `:138` `GetMyMembershipsAsync` handler; `:344` `ExtractAadObjectId`; `:377` `ResolveSystemUserIdAsync` (AAD-oid → systemuserid + 10-min cache) |
| Admin endpoint group | `Api/Admin/MembershipAdminEndpoints.cs` | `:42` `MapAdminMembershipEndpoints`; `:44-46` group + `RequireAuthorization("SystemAdmin")`; `:58` GET discovered; `:77` POST refresh-metadata; `:102` `DiscoverEntityAsync`; `:161` `RefreshMetadataAsync` |
| Endpoint mapping wire-up | `Infrastructure/DI/EndpointMappingExtensions.cs` | `:275` `MapMembershipApi()`; `:283` `MapAdminMembershipEndpoints()` |
| Orchestrator | `Services/Ai/Membership/MembershipResolverService.cs` | `:64` type; `:76` cache key prefix; `:79` 5-min `CacheTtl`; algorithm doc-comment `:5-22`; `:118` `ResolveAsync`; 1-hop depth pre-validation `:142-159` |
| Discovery service | `Services/Ai/Membership/MembershipFieldDiscoveryService.cs` | `:59` type; `:69` cache key prefix (`"membership:discovery:"`); algorithm `:12-23` (header comment); `:79-81` trailing-digits regex |
| Identity normalization | `Services/Ai/Membership/IdentityNormalizationService.cs` | `:40` type; `:48` cache key prefix (`"membership:identity:"`); `:50` 10-min TTL; 6-path contract `:7-15` (header comment) |
| Organization-lookup resolver | `Services/Ai/Membership/OrganizationMembershipResolver.cs` | `:53-54` type (dual-interface impl); `:57` `OrganizationEntityLogicalName`; failure-soft latch `:70` |
| Options | `Services/Ai/Membership/MembershipOptions.cs` | binds `Membership:*` section |
| DTOs | `Services/Ai/Membership/Models/MembershipResponse.cs` · `Models/PersonIdentity.cs` · `Models/MembershipDescriptor.cs` | camelCase JSON locked at type level |
| 1-hop depth error type | `Services/Ai/Membership/MembershipDepthExceededException.cs` | Carries `OffendingEntry` + `ReasonTag` for structured 400 |
| DI module | `Infrastructure/DI/MembershipModule.cs` | `:65` `MembershipModule` type; `:73-75` `AddMembership(services, configuration)`; unconditional: `:90-94` org-resolver; `:103` identity; `:111` discovery; `:119` resolver; SYMMETRIC publisher branch `:127-163`; SYMMETRIC cache-invalidator branch `:165-203`; handler always-on `:220`; SYMMETRIC host branch `:228-252`; recon job + bootstrap `:254-277`; bootstrap hosted service class `:313` |

### Phase 2 — event-driven sync + recon + cache invalidation

| Component | File | Lines (key sections) |
|---|---|---|
| Wire-format event | `Services/Ai/Membership/Events/MembershipChangedEvent.cs` | `:68` type; `:76` `SerializerOptions` (camelCase + enum-as-string); `:142` `CorrelationId` (required, NFR-08); `:161` `OccurredOnUtc` (optional) |
| Mutation type enum | `Services/Ai/Membership/Events/MembershipMutationType.cs` | Added/Updated/Removed |
| Person identity type enum | `Services/Ai/Membership/Events/PersonIdentityType.cs` | User/Contact/Team/Organization (pinned ints 1..4 to match Dataverse OptionSet) |
| Publisher options | `Services/Ai/Membership/Events/MembershipEventPublisherOptions.cs` | `:38` `Enabled` default `false` |
| Topic publisher (real) | `Services/Ai/Membership/Events/MembershipEventPublisher.cs` | `:32` type; `:62` `PublishAsync` (fire-and-forget per Q2; never throws) |
| Topic publisher (Null peer) | `Services/Ai/Membership/Events/NullMembershipEventPublisher.cs` | `:29` type; `:47` `PublishAsync` (P2 Quiet — logs Info + returns) |
| Subscription host (real) | `Services/Ai/Membership/MembershipJunctionUpdaterHost.cs` | Header `:1-56`; 30s drain on stop (NFR-07) |
| Subscription host (Null peer) | `Services/Ai/Membership/NullMembershipJunctionUpdaterHost.cs` | `:47` type; `:57` `ExecuteAsync` (logs once) |
| Junction handler options | `Services/Ai/Membership/MembershipJunctionUpdaterOptions.cs` | `:65` `Enabled` default `false` |
| Junction handler | `Services/Ai/Membership/MembershipJunctionUpdater.cs` | `:76` type; `:82` `JunctionEntityLogicalName`; algorithm `:1-62` (header) |
| Recon job | `Services/Ai/Membership/MembershipReconciliationJob.cs` | `:113` type; algorithm `:14-46` (header); topic-independence rationale `:51-60` (header) |
| Recon options | `Services/Ai/Membership/MembershipReconciliationOptions.cs` | `:73` `Enabled` default **`true`** |
| Cache invalidator (real) | `Services/Ai/Membership/MembershipCacheInvalidator.cs` | `:38` type; mirrors `JobStatusService` Redis pub/sub convention |
| Cache invalidator options | `Services/Ai/Membership/MembershipCacheInvalidatorOptions.cs` | `:32` `DefaultChannel = "membership-cache-invalidate"`; `:47` `Enabled` default `false`; `:54` `Channel` |
| Cache subscriber | `Services/Ai/Membership/MembershipCacheInvalidationSubscriber.cs` | Hosted service; SCAN+DEL eviction strategy (header `:13-22`) |
| Cache invalidation message | `Services/Ai/Membership/MembershipCacheInvalidationMessage.cs` | Wire payload (`personId`, `entityLogicalName`, `correlationId`, `publishedAtUtc`) |
| Cache invalidator (Null peer) | `Services/Ai/Membership/NullMembershipCacheInvalidator.cs` | `:28` type; `:35-38` once-at-construction Info log |

### Mutation-site publishers (consumers of `IMembershipEventPublisher`)

| Site | File | Line |
|---|---|---|
| `POST /api/v1/documents` | `Api/DataverseDocumentsEndpoints.cs` | `:34` (DI inject); `:64-77` event construct + fire-and-forget |
| `POST /api/office/quick-create/{entityType}` (matter cluster) | `Api/Office/OfficeEndpoints.cs` | `:1153` (DI inject); rationale in `:1140-1148` (comment) |
| `POST /api/events` | `Api/Events/EventEndpoints.cs` | `:332` (DI inject) |
| `OfficeService` (save-document) | `Services/Office/OfficeService.cs` | `:38` (field); `:52` (ctor inject) |

### Reference consumers (Phase 1A read path)

| Consumer | File | Line |
|---|---|---|
| Playbook node executor (`ActionType=52`) | `Services/Ai/Nodes/LookupUserMembershipNodeExecutor.cs` | `:70` type |
| Junction data-model doc | [`docs/data-model/sprk_userentityassociation.md`](../data-model/sprk_userentityassociation.md) | — |
| Org-lookup decision record | `projects/spaarke-platform-foundations-r3/notes/sprk-organization-mapping-decision.md` | — |

---

## Known Pitfalls

| Pitfall | Mitigation |
|---|---|
| **Confusing `AssociationResolver` (PCF) with Membership resolution (BFF)** | See Naming-Collision Register above. Different surfaces, different concepts, both stable. |
| **Re-deriving membership in ad-hoc FetchXML** | This is the R2-UAT root cause (A1 / D5). Always go through `IMembershipResolverService`. (Historical: Daily Briefing's `BriefingService.GetTopPriorityMatterAsync` STUB was the last known offender; closed Wave 28 / 2026-06-22 — see Confirmed Consumers table.) |
| **Joining through `sprk_matterteammember` or other non-existent entity** | The R2-broken `notification-new-documents.json` playbook was migrated in R3 task 050 to use `LookupUserMembership` node + `joinIds` Handlebars helper. Same for `notification-new-emails.json` (task 051) and `notification-new-events.json` (task 052). |
| **Assuming Phase 1A FetchXml scales forever** | Monitor AC-1A.5 p95. Phase 2 junction-write path + recon + invalidation already shipped in R3; the read-path swap (resolver queries junction table instead of FetchXml) is the R4 escape hatch when sustained p95 > 500ms. |
| **Creating new `PlatformAdmin` policy** | Forbidden (Q6). Reuse existing `SystemAdmin` policy at `AuthorizationModule.cs:241`. |
| **`includeRelated` chains > 1 hop** | Reject with 400 before any Dataverse query (Q3). Pre-validated at `MembershipResolverService.cs:142-159`. |
| **Mapping `sprk_assignedlawfirm*` to `Contact`** | Wrong (was an error in design.md). They are Lookup → `sprk_organization` → `identityType="Organization"` per Q4. Handled by per-entity `FieldRoleOverrides`. |
| **Synchronous wait on event publish** | Q2 forbids it. Mutation succeeds independent of publish (`_ = membershipEventPublisher.PublishAsync(...)`). Recon job is the backstop. |
| **Reusing `ServiceBusJobProcessor` queue for membership events** | Forbidden (D3). Use the new topic `sprk-membership-changes` with subscription-per-consumer. |
| **Flipping `CacheInvalidator:Enabled=true` without Redis** | The DI module guards this: real impl only wins when BOTH the flag AND `IConnectionMultiplexer` is registered (`MembershipModule.cs:180-186`). Null peer wins otherwise — flip is silently ineffective. |
| **Asymmetric registration** | Forbidden per `bff-extensions.md` §F.1. All three kill-switched services use SYMMETRIC registration (exactly one impl always bound) so minimal-API endpoints unconditionally inject the interface. |

---

## See Also

- [ADR-034 concise](../../.claude/adr/ADR-034-user-record-membership.md) — binding constraints
- [ADR-034 full](../adr/ADR-034-user-record-membership.md) — alternatives considered, full history
- [`docs/data-model/sprk_userentityassociation.md`](../data-model/sprk_userentityassociation.md) — junction schema
- [`projects/spaarke-platform-foundations-r3/spec.md`](../../projects/spaarke-platform-foundations-r3/spec.md) Part 1 — FR-1A.* / FR-1B.* / FR-1C.* / FR-1D.* / FR-2P2.* / AC-1A.* / AC-1B.* / AC-1C.* / AC-1D.* / AC-1P2.*
- [`projects/spaarke-platform-foundations-r3/notes/sprk-organization-mapping-decision.md`](../../projects/spaarke-platform-foundations-r3/notes/sprk-organization-mapping-decision.md) — Q4 sprk_organization mapping decision
- [`projects/spaarke-platform-foundations-r3/notes/operator-followup-task071.md`](../../projects/spaarke-platform-foundations-r3/notes/operator-followup-task071.md) — Service Bus topic operator-deploy runbook
- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — BFF pre-merge checklist (§§A, F.1)
- [background-workers-architecture.md](background-workers-architecture.md) — full BFF BackgroundService inventory (recon job + junction host live here)
- [playbook-architecture.md](playbook-architecture.md) — `LookupUserMembership` node (ActionType=52) sits in the node-executor framework
- [caching-architecture.md](caching-architecture.md) — Redis cache tiers used by membership (5/10/60-min TTLs)
- [AI-ARCHITECTURE.md](AI-ARCHITECTURE.md) — Spaarke AI platform overview (parent)
