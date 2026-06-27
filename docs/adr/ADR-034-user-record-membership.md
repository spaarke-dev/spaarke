# ADR-034: User-Record Membership Resolution Pattern

| Field | Value |
|-------|-------|
| Status | **Accepted** |
| Date | 2026-06-21 |
| Authors | Spaarke Engineering, R3 project |
| Source project | `spaarke-platform-foundations-r3` Part 1 |
| Supersedes | n/a (closes a gap — there was no prior canonical mechanism) |
| Cross-references | extends ADR-013 (AI architecture); reinforces ADR-009 (Redis caching), ADR-010 (DI minimalism), ADR-024 (polymorphic resolver pattern), ADR-028 (Spaarke Auth v2). |

---

## Context

R2 UAT (2026-06-19/20) surfaced a concrete defect: the `notification-new-documents` playbook silently produced ZERO rows in production. Root cause: its FetchXML joined through `sprk_matterteammember`, an entity that **does not exist in the Spaarke data model**. A grep across the repo confirmed the only references to `sprk_matterteammember` were in the playbook config itself.

This was a symptom of a broader pattern. "Records this user is associated with, by entity type" is needed across Spaarke:

- **Briefing widget**: which records have notifications for me?
- **Dashboard tiles**: My Matters, My Documents, My Tasks, My Events
- **Search refinement**: limit semantic search to records I'm on
- **Permission heuristics**: auto-share with people already on a related record
- **Notification routing**: notify everyone on this matter when a key event fires
- **AI Chat context**: scope conversation to entities the user is involved with

Today, **each consumer re-derives membership independently**. There is no canonical definition, no identity normalization across `systemuserid` ↔ `contactid` ↔ `email`, no indexed lookup, no shared cache.

The result: silent breakage (the A1 defect), inconsistent definitions across UI surfaces, and configuration drift — what one playbook calls "owner" another calls "assignedTo".

A naming-collision concern: an existing PCF named `AssociationResolver` ([`src/client/pcf/AssociationResolver/`](../../src/client/pcf/AssociationResolver/)) handles a DIFFERENT concept (record-to-record FieldMapping for copying values when an Event's Regarding lookup is set). Any new membership service must be disambiguated to prevent operator confusion. R3 uses **"Membership"** terminology throughout (`MembershipResolverService`, `/api/users/me/memberships/{entityType}`, this ADR).

---

## Decision

Ship ONE canonical "Membership" mechanism with a 6-component design.

### 1. Discovery-based field discovery

`MembershipFieldDiscoveryService` queries Dataverse `EntityDefinitions` metadata for any entity type. Filters to Lookup attributes whose `Targets[]` includes one of 6 configured identity tables (`systemuser`, `contact`, `team`, `businessunit`, `account`, `sprk_organization`). Applies:

- **Global field exclusions** (`createdby`, `modifiedby`, `createdonbehalfby`, `modifiedonbehalfby` — touch-history, not association)
- **Per-entity overrides** in `appsettings.json`: `ExcludedFields`, `IncludedFields` (force-include even if globally excluded), `FieldRoleOverrides`

Derives **role name** via a CamelCase strategy: strip `sprk_` prefix → strip trailing numeric digits → camelCase. Example: `sprk_AssignedAttorney1` → `assignedAttorney`. Per-entity `FieldRoleOverrides` collapse paired fields: `sprk_assignedlawfirm1` + `sprk_assignedlawfirm2` both map to role `"assignedLawFirm"`.

Caches descriptor list per entity type in Redis with 1h TTL (configurable via `Membership:MetadataCacheTtlMinutes`). Operators force-refresh via `POST /api/admin/membership/refresh-metadata`.

**Why discovery over explicit enumeration**:

| | Explicit enumeration (rejected) | Discovery + overrides (chosen) |
|---|---|---|
| Maintenance when entity adds field | Manual config update (D5 drift root cause) | Automatic |
| Config size | ~10 entries × N entities | Tiny defaults + small per-entity overrides |
| Drift risk | High (the D5 / A1 root cause) | Low (auto-discovers) |
| Surprise factor | Low | Medium — mitigated by Discovery Report endpoint (`GET /api/admin/membership/discovered/{entityType}`) |
| Edge case handling | Easy (configure everything) | Possible via overrides |
| Performance | Zero overhead | Metadata cache + filtering per first request per entity (negligible after cache) |

### 2. Identity normalization

`IdentityNormalizationService` resolves a `systemuserid` into a full `PersonIdentity` record. Each of the 6 identity-type paths is INDEPENDENT — failure of one (e.g., user without contact) does NOT fail others. Per-path try/catch + warning log. Cancellation is re-thrown explicitly (not swallowed). Cache read/write failures are fail-open (re-resolve from Dataverse — no functional impact).

| Source field type | Resolves via | Match value |
|---|---|---|
| `Lookup → systemuser` | Direct | `systemUserId` |
| `Lookup → contact` | Direct; cross-referenced to `systemUserId` via `azureactivedirectoryobjectid` (per ADR-028) | `contactId` |
| `Lookup → team` | Expand `teammembership` to systemusers | `teamIds[]` (cached) |
| `Lookup → businessunit` | User's BU + any descendant BUs (configurable per role) | `businessUnitId` |
| `Lookup → account` | User's primary contact's `parentcustomerid` | `accountId` (when applicable) |
| `Lookup → sprk_organization` | Configured user-organization mapping (Option (b) per R3 task 032) | `organizationIds[]` |
| Text (email) | Substring `like` | `primaryEmail` |
| Text (display name) | **NOT supported** (too fuzzy) | — |

Cached per user in Redis (key `membership:identity:{systemUserId:D}`, 10-min TTL). Namespace prefix `membership:` aligns with the Phase 2 pub/sub invalidation channel (FR-2P2.8).

### 3. Organization mapping mechanism (Q4 + task 032 decision)

`sprk_assignedlawfirm1/2` Lookup targets **`sprk_organization`** (NOT `Contact` as design.md's Discovery Report example incorrectly showed — Q4 owner clarification). Identity-type for those fields is `Organization`.

Per task 032 decision: chose **Option (b)** — config-driven Lookup field on `sprk_organization` pointing to systemuser. Operators set `Membership:OrganizationLookup:UserLookupField` to a Lookup column on `sprk_organization` that targets systemuser (e.g., `sprk_owneruser`). When unset, the resolver returns an empty list and logs Info once per process (fail-soft default). Per task 032 decision note: this avoids requiring a Dataverse N:N schema change; the interface contract is identical to future Options (a) (N:N) or (c) (team-based) so the resolver is swappable.

### 4. Orchestration + endpoint

`MembershipResolverService` orchestrates discovery + identity normalization + Dataverse query. Per-user Redis cache (5-min TTL Phase 1A; Phase 2 lengthens TTL + adds pub/sub invalidation per FR-2P2.8). Pagination via base64url-encoded skip-count `continuationToken` (NOT FetchXML page cookies — stateless skip-count is simpler).

```
GET /api/users/me/memberships/{entityType}
  ?roles=owner,assignedAttorney             (optional; default: all discovered roles)
  ?identityTypes=SystemUser,Contact         (optional; default: all configured types)
  ?includeRelated=documents,events          (optional; transitive memberships — 1-hop max per Q3)
  ?limit=500                                (max 5000)
  ?continuationToken={token}

Authentication: standard Spaarke Auth v2 OBO (ADR-028)

Response (200 OK):
{
  "entityType": "sprk_matter",
  "personIdentity": { "systemUserId": "...", "contactId": "...", "primaryEmail": "...", "teamIds": ["..."], "businessUnitId": "..." },
  "ids": ["matter-guid-1", "matter-guid-2", ...],
  "byRole": {
    "owner": ["matter-guid-1"],
    "owningTeam": ["matter-guid-1", "matter-guid-2"],
    "assignedAttorney": ["matter-guid-3"],
    "assignedParalegal": ["matter-guid-2"],
    "assignedLawFirm": []
  },
  "count": 47,
  "cacheExpiresAt": "2026-06-20T15:34:00Z",
  "continuationToken": null
}
```

### 5. Phase 1B — Playbook node executor + Handlebars helper

New playbook node executor `LookupUserMembership` (`ActionType = 52`, slots into the Dataverse-data-ops group between `QueryDataverse=51` and `AgentService=60`) calls `MembershipResolverService` IN-PROCESS (NOT HTTP round-trip) and binds resolved IDs to the node's `OutputVariable` for downstream Query nodes:

```json
{
  "__actionType": 52,
  "entityType": "sprk_matter",
  "roles": ["owner", "assignedAttorney", "assignedParalegal"],
  "outputVariable": "myMatters"
}
```

Downstream Query nodes consume via a new `joinIds` Handlebars helper (registered in `TemplateEngine.cs`) that produces a comma-separated list for FetchXML's `operator='in'`:

```xml
<condition attribute="sprk_matter" operator="in" value="{{joinIds myMatters.ids}}"/>
```

### 6. Phase 2 — Junction table + event-driven sync (firm in-scope per owner 2026-06-20)

Materialized junction `sprk_userentityassociation` (7 columns + composite alternate key for upsert idempotency + 6-value OptionSet `personidtype`). Polymorphic "person" via `(personId, personIdType)` tuple per ADR-024 (NOT a polymorphic Lookup — design rationale: 6 different target tables make Lookup polymorphism awkward).

**Schema** (`sprk_userentityassociation`):

| Field | Type | Purpose |
|---|---|---|
| `sprk_personid` | Uniqueidentifier-shaped Text | The person (resolved identity GUID) |
| `sprk_personidtype` | OptionSet (SystemUser=1, Contact=2, Team=3, BusinessUnit=4, Account=5, Organization=6) | Disambiguator |
| `sprk_entitylogicalname` | Text | E.g., "sprk_matter" |
| `sprk_entityrecordid` | Uniqueidentifier-shaped Text | Record GUID |
| `sprk_role` | Text | Discovered role name (e.g., "assignedAttorney") |
| `sprk_sourcefield` | Text | Provenance: which field provided the association |
| `sprk_lastsyncedon` | DateTime | For staleness audit |

**Composite alternate key** `sprk_uea_natural_key`: `(sprk_personid, sprk_personidtype, sprk_entitylogicalname, sprk_entityrecordid, sprk_sourcefield)` — natural idempotency key for upsert.

**Synchronization** (TWO mechanisms — defense-in-depth):

- **(a) Event-driven via Service Bus topic** `sprk-membership-changes` (D3 owner clarification — topic + subscription-per-consumer, NOT queue, NOT reuse `ServiceBusJobProcessor` queue). BFF endpoints that mutate matter/document/event/etc. lookups publish `MembershipChangedEvent` (fire-and-forget per Q2 — mutation succeeds even if publish fails; log warnings). A background handler `MembershipJunctionUpdater` consumes the `recon-junction-updater` subscription and upserts/deletes junction rows (idempotent on duplicate delivery).
- **(b) Nightly reconciliation**: `MembershipReconciliationJob` (scheduled via the new `Spaarke.Scheduling` framework — see ADR-036) scans source lookups for configured entities → compares to junction rows → upserts missing, removes orphans. Catches drift from mutations that bypass BFF (e.g., maker portal direct edits) AND failed event publishes (the Q2 fail-soft mode).

**Cache invalidation** (FR-2P2.8): junction-row writes (by either handler or recon job) publish to Redis channel `membership-cache-invalidate` carrying `{userId, entityType}`. Subscribers (BFF instances) evict matching cache entries.

**Strangler-fig migration**: the endpoint contract is BYTE-IDENTICAL between Phase 1A (per-request FetchXML) and Phase 2 (junction-table query). Consumers see no change when storage swaps. `MembershipResolverService` internally chooses the source based on configuration / migration phase.

### 7. Phase 1D — Transitive memberships (firm in-scope per owner 2026-06-20)

`includeRelated=documents,events` query parameter on the endpoint returns transitive memberships (e.g., documents on matters I'm on, events on matters I'm on). **Max chain depth: 1 hop** per Q3 owner clarification — multi-hop requests return `400 BadRequest` with `ProblemDetails type="transitive-chain-too-deep"`. Performance budget: AC-1A.5 (p95 ≤300ms) still holds; if a chained query exceeds budget, the per-call returns 400 with a `Retry-After`-style hint.

---

## Consequences

### Positive

- **A1 / D5 root cause closed**: no more ad-hoc per-playbook FetchXML; one canonical service.
- **Consistent role names across surfaces**: discovery + per-entity overrides ensure "owner" means the same thing in playbooks, dashboards, search, and notifications.
- **Auto-discovery prevents drift**: when a new sprk_assigned* field is added to sprk_matter, the resolver finds it automatically — no per-consumer config update needed.
- **Identity-type path independence** handles real-world identity edge cases (user without contact, contact without systemuser, multi-team users) without failing the whole resolution.
- **Strangler-fig Phase 1A → Phase 2** means consumers can start using Phase 1A today and get Phase 2 performance + freshness for free.
- **`LookupUserMembership` playbook node + `joinIds` helper** make this pattern usable from JPS playbooks without writing custom node executors.
- **Defense-in-depth Phase 2 sync** (event-driven + nightly recon) survives transient Service Bus failures, BFF-bypassing mutations (maker portal), and event publish failures (Q2 fire-and-forget mode).

### Negative

- **Discovery is "magic" to operators** until they run `GET /api/admin/membership/discovered/{entityType}` to see what was discovered. Mitigation: surface the Discovery Report endpoint; operators run it against each new entity before production.
- **Metadata cache staleness** after entity schema change: 1h default TTL means up to 1h delay before new fields appear. Mitigation: `POST /api/admin/membership/refresh-metadata` for immediate refresh.
- **Two parallel tracking concepts** in Dataverse: `sprk_processingjob` (Office-scoped) vs `sprk_backgroundjobrun` (scheduled jobs). The `MembershipReconciliationJob` uses the latter; not a confusion source for membership-specific consumers, but operators must learn both families.
- **`(personId, personIdType)` tuple** on `sprk_userentityassociation` is harder to query in advanced-find than a polymorphic Lookup would be. Mitigation: alternate key + composite index handles the common access patterns; operator-side filtering uses the composite key.

### Neutral

- BFF publish-size impact is small (~+0.5 MB for all Membership services + DTOs + endpoints together; tracked per-task per NFR-01).
- `IMembershipResolverService` + `IMembershipFieldDiscoveryService` + `IIdentityNormalizationService` + `IOrganizationMembershipResolver` interfaces allowed under ADR-010 (each has a real testing seam; concrete impls + Null-Object alternative if any ever becomes feature-gated).

---

## Acceptance criteria (R3)

See spec.md AC-1A.1 through AC-1.Docs + AC-1B.* + AC-1C.* + AC-1D.* + AC-1P2.*. Highlights:

- ✅ AC-1A.1: discovery for `sprk_matter` finds expected fields (owner, owningteam, owningbusinessunit, sprk_assignedattorney1/2, sprk_assignedparalegal1/2, sprk_assignedlawfirm1/2, sprk_assignedtointernal, sprk_assignedtoexternal); excludes system fields; excludes custom-entity lookups.
- ✅ AC-1A.2: `GET /api/admin/membership/discovered/sprk_matter` returns descriptor list with `source: "auto"` or `"override"`.
- ⏳ AC-1A.3: `GET /api/users/me/memberships/sprk_matter` returns expected IDs for seeded test user (deferred to P4 wrap-up UAT against spaarkedev1).
- ⏳ AC-1A.4: identity normalization resolves test user with separate systemuser + contact records (deferred to P4 UAT).
- ⏳ AC-1A.5: p95 ≤300ms (deferred to P4 UAT — measured via App Insights server-side request telemetry per NFR-04).
- ⏳ AC-1A.7: metadata cache invalidates on `POST /refresh-metadata` (covered at unit level; full E2E in P4 UAT).
- ✅ AC-1B.1: `LookupUserMembership` node executor handles ActionType=52 (ships in R3 task 041).
- ✅ AC-1B.2: `joinIds` Handlebars helper produces correct comma-separated lists (task 002).
- ✅ AC-1C.1: `notification-new-documents.json` migrated to use `LookupUserMembership` node (ships in R3 task 050).
- ✅ AC-1.ADR: this document.
- ⏳ AC-1.Docs: architecture page `docs/architecture/membership-resolution-pattern.md` (task 104).
- 🚧 AC-1D.1, 1D.2: Phase 1D transitive memberships (tasks 054-056, in flight).
- 🚧 AC-1P2.1 through 1P2.8: Phase 2 junction + event sync + recon (tasks 070, 072, 080-087, in flight).

---

## Open questions resolved (during R3 design)

1. **Q3 (resolved 2026-06-20)**: `includeRelated` max chain depth — 1 hop only or arbitrary depth?
   - **Answer**: 1 hop max. Multi-hop returns 400 BadRequest.
2. **Q4 (resolved 2026-06-20)**: `sprk_assignedlawfirm1/2` Lookup target — Contact (per design.md example) or something else?
   - **Answer**: `sprk_organization` (NOT Contact — design.md's Discovery Report example was wrong). Identity-type for those fields is `Organization`.
3. **Q5 (resolved 2026-06-20)**: PlaybookBuilder Builder UI affordances — invent new patterns or align with existing?
   - **Answer**: Align with existing per-ActionType form pattern + reuse `VariableReferencePanel.tsx` + `canvasValidation.ts` + `NodePropertiesDialog.tsx`. New `LookupUserMembershipForm.tsx` follows existing `CreateNotificationForm.tsx` pattern.
4. **Q6 (resolved 2026-06-20)**: Admin policy — new "PlatformAdmin" or existing "SystemAdmin"?
   - **Answer**: Existing `SystemAdmin` at `AuthorizationModule.cs:241`. Precedent: `RagEndpoints.cs:157`.
5. **D3 (resolved 2026-06-20)**: Phase 2 event-driven sync transport — single queue, topic-per-consumer, or reuse existing `ServiceBusJobProcessor` queue?
   - **Answer**: Service Bus **topic** `sprk-membership-changes` with subscription-per-consumer. Allows future consumers (cache warmers, downstream indexers, Teams-notify, VIP cache invalidator) without infra migration. ~5-10% per-message cost premium (pennies/month at expected volume).
6. **Q2 (resolved 2026-06-20)**: Phase 2 event-publishing semantics — fire-and-forget or transactional outbox?
   - **Answer**: Fire-and-forget. Publish best-effort; mutation succeeds even on publish failure. Nightly `MembershipReconciliationJob` is the defense-in-depth backstop. Log failures as structured warnings (correlationId-tagged) for diagnostic visibility.
7. **Task 032 mechanism choice (2026-06-21)**: How does the BFF resolve user → sprk_organization mappings — N:N relationship, configurable lookup field, or team-based?
   - **Answer**: Option (b) config-driven Lookup field. Operators set `Membership:OrganizationLookup:UserLookupField` to point at a Lookup column on `sprk_organization` that targets systemuser. Fail-soft empty when unset.

---

## See Also

- Concise (AI-context-loaded) version: [`.claude/adr/ADR-034-user-record-membership.md`](../../.claude/adr/ADR-034-user-record-membership.md)
- Architecture page: [`docs/architecture/membership-resolution-pattern.md`](../architecture/membership-resolution-pattern.md) (created in task 104)
- Spec: [`projects/spaarke-platform-foundations-r3/spec.md`](../../projects/spaarke-platform-foundations-r3/spec.md) Part 1
- Design: [`projects/spaarke-platform-foundations-r3/design.md`](../../projects/spaarke-platform-foundations-r3/design.md) Part 1
- Data model: [`docs/data-model/sprk_userentityassociation.md`](../data-model/sprk_userentityassociation.md), `docs/data-model/sprk_matter-related-tables.md` (task 103 refresh)
- Decision note: [`projects/spaarke-platform-foundations-r3/notes/sprk-organization-mapping-decision.md`](../../projects/spaarke-platform-foundations-r3/notes/sprk-organization-mapping-decision.md)
- Code: `src/server/api/Sprk.Bff.Api/Services/Ai/Membership/`, `src/server/api/Sprk.Bff.Api/Api/Membership/`, `src/server/api/Sprk.Bff.Api/Api/Admin/MembershipAdminEndpoints.cs`, `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/LookupUserMembershipNodeExecutor.cs` (task 041)
- Constraints: [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) §§A, F.1
- Naming-collision: distinct from [`src/client/pcf/AssociationResolver/`](../../src/client/pcf/AssociationResolver/) PCF (record-to-record FieldMapping) + `sprk_fieldmappingprofile` / `sprk_fieldmappingrule` Dataverse entities
- Related ADRs: ADR-009 (Redis), ADR-010 (DI minimalism), ADR-013 (AI architecture), ADR-024 (polymorphic resolver), ADR-028 (Spaarke Auth v2 — `azureactivedirectoryobjectid` cross-ref), ADR-029 (BFF publish hygiene), ADR-036 (Spaarke.Scheduling — `MembershipReconciliationJob` is the 2nd reference consumer)
