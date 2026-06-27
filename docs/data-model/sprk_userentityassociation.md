# sprk_userentityassociation — User-to-Record Membership Junction

> **Project**: spaarke-platform-foundations-r3 (Part 1 — Phase 2 junction-table materialization)
> **Task**: R3-070 (FR-2P2.1, AC-1P2.1)
> **Created**: 2026-06-21
> **Status**: Deployed to spaarkedev1
> **Schema script**: [`scripts/Create-UserEntityAssociation.ps1`](../../scripts/Create-UserEntityAssociation.ps1) (idempotent)

---

## Purpose

`sprk_userentityassociation` is the **materialized junction table** that backs
`GET /api/users/me/memberships/{entityType}` once Phase 2 of the user-record
membership architecture (ADR-034) is implemented.

Phase 1A (in-scope for R3) computes memberships **per request** via
`MembershipResolverService` discovery + FetchXML. Phase 2 strangles into a direct
junction-table query when sustained performance pressure warrants it (trigger
threshold: endpoint p95 > 500 ms — see ADR-034). **The endpoint contract is
identical between Phase 1A and Phase 2** — consumers see no change (strangler fig).

Once Phase 2 is live, two write paths feed this table (defense-in-depth):

1. **Event-driven (primary path)** — BFF mutation endpoints that change person /
   team / BU lookups on `sprk_matter` / `sprk_event` / `sprk_communication` / etc.
   publish a lightweight `MembershipChangedEvent` to Service Bus topic
   `sprk-membership-changes` (R3 task 071). The subscription
   `recon-junction-updater` is consumed by `MembershipJunctionUpdater` (R3 task 084),
   which upserts / deletes junction rows. Idempotent by the composite alternate key.
   Publish semantics are **fire-and-forget** (spec §FR-2P2.6) — the mutation succeeds
   even if the publish fails; the recon job is the backstop.
2. **Nightly reconciliation (backstop)** — `MembershipReconciliationJob`
   (R3 task 085) runs through the new `Spaarke.Scheduling` framework
   (R3 Part 2). Scans source-of-truth lookups for configured entities → compares
   to junction rows → upserts missing rows + removes orphans. Catches drift from
   mutations that bypass the BFF (e.g., direct maker-portal edits, plugin writes,
   Power Automate flows).

Cache invalidation: junction-row writes publish to Redis channel
`membership-cache-invalidate` carrying `{userId, entityType}` so BFF instances
evict matching cache entries (spec §FR-2P2.8).

### Why polymorphic via a tuple, not a Lookup?

The "person" side of the junction can be one of **six different table types**
— SystemUser, Contact, Team, BusinessUnit, Account, Organization
(`sprk_organization`). Dataverse polymorphic Lookup is awkward across that many
targets (UI limitations, query verbosity, schema brittleness if a new identity
type is added). We instead encode person identity as a **tuple**:

```
(sprk_personid: GUID-string, sprk_personidtype: OptionSet)
```

This pattern aligns with **ADR-024 polymorphic-resolver** guidance. The OptionSet
`sprk_userentityassociation_personidtype` disambiguates which person-side table
`sprk_personid` references.

### Naming-collision register

This is a **NEW concept** distinct from several similarly-named surfaces that
already exist in the org. They coexist; none should be overloaded:

| Existing surface | What it does | Why it's NOT this entity |
|---|---|---|
| `sprk_fieldmappingprofile` + `sprk_fieldmappingrule` | Configures **record-to-record value copy** at communication-ingest time (e.g., copy matter fields onto an inbound email). | Different concept — field-value copy config, not user-record membership. Architectural pattern (Profile + Rules) may be reusable if we ever surface membership config in Dataverse — design escape hatch noted in design.md Part 1 alternatives. |
| `AssociationResolver` PCF + `IncomingAssociationResolver` BFF service | Resolves **incoming-communication associations** (which matter / contact an inbound email belongs to). | Different scope — incoming-message routing, not user-record membership. |
| `sprk_matterteammember` (deprecated D-target) | Was attempted as a hand-maintained join. | This is the principled replacement, fed by event-driven + reconciliation. |

---

## Columns

Standard audit fields (`createdon`, `createdby`, `modifiedon`, `modifiedby`,
`ownerid`, `statecode`, `statuscode`, `versionnumber`) are auto-added by Dataverse
and are not listed below.

| # | Column | Type | Required | Max length | Default | Purpose |
|---|---|---|---|---|---|---|
| — | `sprk_name` | Text | Optional | 100 | auto | **Primary name field.** Auto-numbered (`UEA-NNNNNN`). Junction rows are machine-managed; meaningful identity is the composite alternate key, not the primary name. |
| 1 | `sprk_personid` | Text (36) | **Required** | 36 | — | Resolved identity GUID of the person, stored as canonical 36-char lowercase hyphenated string (no braces). Disambiguated by `sprk_personidtype`. *See Implementation Note below re: stored-as-Text rationale.* |
| 2 | `sprk_personidtype` | OptionSet (local) | **Required** | — | — | Disambiguates which of 6 identity tables `sprk_personid` belongs to. See [OptionSet values](#optionset-sprk_userentityassociation_personidtype) below. |
| 3 | `sprk_entitylogicalname` | Text | **Required** | 100 | — | Logical name of the target entity (e.g., `"sprk_matter"`). Combined with `sprk_entityrecordid` identifies the target record. |
| 4 | `sprk_entityrecordid` | Text (36) | **Required** | 36 | — | GUID of the target record on the entity named in `sprk_entitylogicalname`, stored as canonical 36-char string. *Same rationale as `sprk_personid`.* |
| 5 | `sprk_role` | Text | Optional | 100 | — | Discovered role name for the person on the target record (e.g., `"assignedAttorney"`, `"paralegal"`). Derived by `MembershipFieldDiscoveryService` / per-entity overrides. Surfaced via `byRole` grouping in the endpoint response (FR-1A.3). |
| 6 | `sprk_sourcefield` | Text | Optional | 100 | — | Provenance: which lookup field on the target entity provided this association (e.g., `"sprk_assignedattorneyid"`). **Part of the composite alternate key** — distinct source fields for the same (person, record) pair produce distinct rows (e.g., an attorney both assigned to AND reviewing the same matter). |
| 7 | `sprk_lastsyncedon` | DateTime (UserLocal) | **Required** | — | — | Timestamp when this row was last reconciled by the event handler or the nightly recon job. Used for staleness audit + drift detection. |

**Count: 7 functional columns** (matches spec FR-2P2.1 exactly) + 1 primary name field.

### Implementation Note: GUID columns stored as Text(36)

Spec FR-2P2.1 lists `sprk_personid` and `sprk_entityrecordid` as `Uniqueidentifier`.
The Dataverse Web API rejects creating custom `UniqueIdentifierAttributeMetadata`
columns via the SDK:

> *"Attribute of type UniqueIdentifierAttributeMetadata cannot be created through the SDK."*

The native `Uniqueidentifier` type is reserved for system primary IDs and lookup-pair
internals. Custom GUID-typed columns must be stored as `Text(36)` and treated as
`Guid` strings at the application layer (canonical 36-char lowercase hyphenated, no
braces). There is **no semantic difference** for our access patterns: lookup,
equality match, alternate-key membership all work identically on Text(36). The
runtime upsert callers (`MembershipJunctionUpdater`, `MembershipReconciliationJob`)
serialize/deserialize as `Guid.ToString()` / `Guid.Parse()`.

### OptionSet: `sprk_userentityassociation_personidtype`

Local option set (scoped to this attribute) — values match the 6 identity types
enumerated in ADR-034.

| Value | Label | Underlying table |
|---|---|---|
| 1 | SystemUser | `systemuser` |
| 2 | Contact | `contact` |
| 3 | Team | `team` |
| 4 | BusinessUnit | `businessunit` |
| 5 | Account | `account` |
| 6 | Organization | `sprk_organization` |

The `Organization` value targets the Spaarke-custom `sprk_organization` entity
(Q4 owner clarification 2026-06-20 — NOT `contact`, contrary to an earlier
design.md draft).

---

## Composite Alternate Key (Idempotency + Index)

| Key SchemaName | Attributes (declaration order) | EntityKeyIndexStatus | Purpose |
|---|---|---|---|
| `sprk_uea_natural_key` | `(sprk_personid, sprk_personidtype, sprk_entitylogicalname, sprk_entityrecordid, sprk_sourcefield)` | `Active` | Enforces the upsert idempotency contract for both write paths AND provides the closest Dataverse-native equivalent to a composite index. |

### Why a 5-attribute composite (including `sprk_sourcefield`)?

The "natural identity" of a junction row is the **5-tuple**. Same person, same
entity record, same logical name → distinct rows if the *source field* that
produced the association differs. Example: an attorney who is both the *assigned*
attorney (`sprk_assignedattorneyid`) AND the *reviewing* attorney
(`sprk_reviewingattorneyid`) on the same matter generates **two** distinct rows.
Both must reconcile cleanly under repeated event delivery + nightly recon.

### Why no second alternate key for the reverse query path?

Spec FR-2P2.1 calls out **two query paths**:

1. **Forward**: `{sprk_personid, sprk_entitylogicalname}` — "find all matters
   user X is on" (the endpoint's primary workload)
2. **Reverse**: `{sprk_entitylogicalname, sprk_entityrecordid}` — "find all
   people associated with matter Y" (admin / audit / index-rebuild workload)

Dataverse exposes only **one indexing primitive via Web API for custom columns**:
the unique alternate-key tuple. Auto-created indexes back: (a) alternate-key
attributes in declaration order, (b) Lookup fields. Custom non-unique composite
B-tree indexes are NOT creatable via the public Web API or `pac` CLI.

**Forward query path** — covered by the alternate-key prefix
`(sprk_personid, sprk_personidtype, sprk_entitylogicalname, ...)`. Dataverse can
use the key index for prefix queries that filter on the leading columns.

**Reverse query path** — relies on the Dataverse query optimizer over the
underlying Cosmos-backed storage. At current data volumes this is sufficient;
if observed perf is insufficient at scale (Phase 2 tuning), options are:

- Add a non-unique composite index via an unmanaged solution containing an XML
  customization file that defines `<EntityIndex>` (admin path; not exposed via Web API).
- Hand-rolled secondary index entity if the query workload justifies the maintenance burden.

This nuance is intentional and explicitly captured here so future implementers
don't burn time looking for a "second alternate key" — that's not the answer.

---

## Solution Membership + Deployment

- **Solution**: To be added to the active unmanaged Spaarke solution (per ADR-027).
  The schema script creates the entity at the org root; solution placement is
  performed via the maker portal or `pac solution add-solution-component` as
  part of the broader R3 deployment.
- **Deployment**: Idempotent. Re-running
  [`Create-UserEntityAssociation.ps1`](../../scripts/Create-UserEntityAssociation.ps1)
  on an environment where the entity already exists adds only missing
  attributes / missing alternate key and is safe.
- **Dry-run**: Invoke with `-DryRun` to preview without modifying Dataverse.
- **Different env**: Invoke with `-EnvironmentUrl "https://<env>.crm.dynamics.com"`.
- **First deployed**: spaarkedev1 on 2026-06-21 (R3 task 070).

---

## Service Usage Map

Once R3 Phase 2 tasks (080 / 084 / 085) land, the following services consume this entity:

| Caller | Operation | File |
|---|---|---|
| `MembershipJunctionUpdater` | Upserts / deletes junction rows on `MembershipChangedEvent` receipt; keyed on the composite alternate key for idempotency | `src/server/api/Sprk.Bff.Api/Services/Membership/MembershipJunctionUpdater.cs` (R3 task 084) |
| `MembershipReconciliationJob` | Nightly recon: reads source-of-truth lookups → upserts missing rows + removes orphans; reports `ProcessedItems` per run | `src/server/api/Sprk.Bff.Api/Services/Membership/MembershipReconciliationJob.cs` (R3 task 085) |
| `MembershipResolverService` (Phase 2 strangle) | Reads the junction directly when Phase 2 is enabled; same endpoint contract as Phase 1A | `src/server/api/Sprk.Bff.Api/Services/Membership/MembershipResolverService.cs` (R3 task 030) |
| Redis pub/sub invalidator | Publishes `{userId, entityType}` to channel `membership-cache-invalidate` on junction-row write | (wired in 084/085 — see spec §FR-2P2.8) |

---

## ADR Compliance

| ADR | Compliance |
|---|---|
| **ADR-002** (Late-bound entities) | All Dataverse access is late-bound; no early-bound code generation. |
| **ADR-024** (Polymorphic resolver pattern) | Person identity modeled as `(id, type)` tuple instead of polymorphic Lookup — directly applies the ADR-024 pattern. |
| **ADR-027** (Unmanaged solution; `sprk_` prefix) | Entity uses `sprk_` prefix; will be added to the active unmanaged solution. |
| **ADR-029** (BFF publish hygiene) | This task is a Dataverse-only schema change — 0 MB BFF publish-size delta. |
| **ADR-034** (NEW — User-record membership resolution pattern) | This entity is the Phase 2 materialization the ADR describes. ADR-034 authored in R3 task 037. |

---

## References

- **Spec**: [`projects/spaarke-platform-foundations-r3/spec.md`](../../projects/spaarke-platform-foundations-r3/spec.md) §FR-2P2.1, §AC-1P2.1
- **Design**: [`projects/spaarke-platform-foundations-r3/design.md`](../../projects/spaarke-platform-foundations-r3/design.md) Part 1 §"Phase 2 — Junction table `sprk_userentityassociation`"
- **Task**: [`projects/spaarke-platform-foundations-r3/tasks/070-create-sprk-userentityassociation-entity.poml`](../../projects/spaarke-platform-foundations-r3/tasks/070-create-sprk-userentityassociation-entity.poml)
- **Pairs with**:
  - [task 071](../../projects/spaarke-platform-foundations-r3/tasks/071-provision-servicebus-topic.poml) — Service Bus topic `sprk-membership-changes`
  - [task 072](../../projects/spaarke-platform-foundations-r3/tasks/072-membership-changed-event-payload-contract.poml) — `MembershipChangedEvent` payload contract
  - task 080 — P-event-1 endpoint inventory (consumers of this entity logical name)
  - task 084 — `MembershipJunctionUpdater` subscription handler
  - task 085 — `MembershipReconciliationJob` real logic
  - task 037 — ADR-034 authorship (membership resolution pattern, includes this entity)
  - task 104 — Membership-resolution-pattern doc (operational guide for both phases)
- **Alternate-key pattern**: [`docs/data-model/schema-additions-alternate-keys.md`](schema-additions-alternate-keys.md)
- **Polymorphic-resolver pattern**: [`.claude/patterns/dataverse/polymorphic-resolver.md`](../../.claude/patterns/dataverse/polymorphic-resolver.md)
- **Sibling exemplar (R3 task 015)**: [`docs/data-model/sprk_backgroundjob.md`](sprk_backgroundjob.md)
