# ADR-034: User-Record Membership Resolution Pattern

| Field | Value |
|-------|-------|
| Status | **Accepted, as amended** |
| Date | 2026-06-21 |
| Updated | 2026-10-08 (correction note in §5: FetchXML list helper, ISS-018 #1452 — no rule change); 2026-10-03 (Amendment A4 accepted; A3 2026-10-02; A1 2026-09-04) |
| Authors | Spaarke Engineering, R3 project |
| Source project | `spaarke-platform-foundations-r3` Part 1 |
| Supersedes | n/a (closes a gap — there was no prior canonical mechanism) |
| Cross-references | extends ADR-013 (AI architecture); reinforces ADR-009 (Redis caching), ADR-010 (DI minimalism), ADR-024 (polymorphic resolver pattern), ADR-028 (Spaarke Auth v2). |

> ⚠️ **[Amendment A1](#amendment-a1-2026-09-04-the-access-conferring-allow-list-becomes-first-class-and-per-surface) adds a consumption-surface distinction that this ADR did not originally make.**
> Discovery over the 6 identity tables remains **correct for AI scoping** and is **over-inclusive for
> authorization**. Nothing below is retired — A1 *narrows one consumer*, it does not change the
> mechanism. The 1-hop cap, the event semantics, the canonical-resolver rule and the
> non-existent-entity ban are all unchanged.
>
> ⚠️ **[Amendment A3](#amendment-a3-2026-10-02-accepted-the-people-targeting-surface--who-a-record-is-for) (accepted by the owner, round 3 D1; task 152) adds a THIRD surface — people targeting —**
> for "which records are FOR this person" (briefing, notifications): a human Created By, the user-valued owner, and
> the "Assigned *" contacts through the linked contact; never team / business-unit ownership. It admits `createdby` on
> that surface only, and states the event semantics: an owner event names the row's REAL owner (team or user) in
> Dataverse ids.
>
> ⚠️ **[Amendment A4](#amendment-a4-2026-10-03-accepted-assigned-to-access-for-contacts-is-materialized-as-removable-grants) (ACCEPTED by the owner, round 11, 2026-10-03 — §6.5 path B; task 142) gives the registry a second, WRITE-time consumer:**
> the "Assigned *" contacts and organizations receive Collaborate as explicit, removable grants (or a POA share for a
> linked internal user), maintained by one invariant owner with a provenance ledger. The read-time terms stay.

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

Downstream Query nodes consume the ids through the `fetchInGuids` Handlebars helper (registered in `TemplateEngine.cs`), which writes one `<value>` child per distinct GUID inside a FetchXML `operator="in"` condition:

```xml
<condition attribute="sprk_matter" operator="in">{{fetchInGuids myMatters.ids}}</condition>
```

> **Correction note (corrected 2026-10-08, ISS-018 #1452) — implementation detail only; membership semantics are unchanged.**
> As originally written, this section said a new `joinIds` helper "produces a comma-separated list for FetchXML's
> `operator='in'`", used as `operator="in" value="{{joinIds myMatters.ids}}"`. That premise was false: Dataverse
> ignores the `value` attribute on a FetchXML list operator and reads values only from `<value>` child elements, so
> that condition had zero values and every query failed ("The value passed for ConditionOperator.In is empty") —
> for real id lists as well as empty ones. All seven notification playbooks failed in dev as a result. An `in` with
> zero `<value>` children is an error, not "matches zero rows". `fetchInGuids` fails closed: an empty, null or
> unresolved list, or any non-GUID element, writes the single impossible match
> `<value>00000000-0000-0000-0000-000000000000</value>`, so the condition stays valid and selects nothing.
> `joinIds` remains registered but is not for FetchXML (its comma shape suits an Azure AI Search `search.in` filter);
> `FetchXmlShapeValidator` rejects it in FetchXML at deploy lint, in the `QueryDataverse` executor and in the repo
> regression test.

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
- **`LookupUserMembership` playbook node + `fetchInGuids` helper** (originally `joinIds`; corrected 2026-10-08, ISS-018 #1452) make this pattern usable from JPS playbooks without writing custom node executors.
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
- ✅ AC-1B.2: `joinIds` Handlebars helper produces correct comma-separated lists (task 002). *(Corrected 2026-10-08, ISS-018 #1452: a comma list is not a valid FetchXML `in` list, so this criterion did not make the playbooks work; FetchXML now uses `fetchInGuids` — see §5's correction note.)*
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

## Amendment A1 (2026-09-04): The access-conferring allow-list becomes first-class and per-surface

> **Status**: Accepted (resolution path **B — amendment**, per root CLAUDE.md §6.5).
> **Driver project**: `unified-access-control-r2` (spec ADR Tensions row 2; FR-24; register B-12 / H-5).
> **Evidence**: [`projects/unified-access-control-r2/notes/investigation/02-membership-spine.md`](../../projects/unified-access-control-r2/notes/investigation/02-membership-spine.md) §4.2, §8, §10.4.
> **Sequencing**: merges **before** task 041 builds the registry it sanctions.

### Why

This ADR made discovery **convention-based** on purpose, and for its original consumer — AI scoping —
that is still the right answer: when a playbook asks "which matters is this user associated with?",
being generous is correct, because the answer feeds retrieval, not permission.

**Authorization is a different question with a different failure mode.** The same discovered set, used
as an access answer, is **over-inclusive** — and over-inclusive means disclosure. Two concrete defects:

1. **The `sprk_assigned*` prefix convention is not a policy.** It *silently admits*
   `sprk_assignedmonitor` (a watcher, who should confer nothing) and *silently denies*
   `sprk_leadcontact` (who should confer access). Nobody chose either outcome; a naming convention did.
   A convention cannot express intent, and worse, it changes behaviour when a column is **renamed** —
   so an access grant can appear or vanish through a schema edit that no reviewer would read as a
   security change.
2. **Org-typed lookups confer too.** M4 already resolves `sprk_assignedlawfirm1/2` to `Organization` —
   the conferring precedent exists — but the *filter* today covers contact-typed lookups only
   (`FilterToAccessConferringContactRoles`). Unfiltered org expansion confers access from **any**
   organization named on a record, **including opposing counsel**.

### The per-surface policy (new)

| Consumption surface | Which descriptors apply | Rationale |
|---|---|---|
| **AI scoping** (playbook nodes, briefing collectors, `/api/users/me/memberships/*`) | **All discovered descriptors** — unchanged | Generosity is correct for retrieval; the caller already has access to what they are shown |
| **Authorization** (the evaluator's derived-member and org-expansion terms) | **Registry-listed columns ONLY** | Over-inclusion here is a disclosure, not a nuisance |

**One mechanism, two policies.** The registry lives **inside** the canonical resolver as a filter over
its output. It is an *extension* of M1, not a parallel mechanism — a second membership engine would be
a violation of this ADR, and A1 does not create one (investigation 02 §8).

### New MUST rules

- **MUST** derive the authorization surface's conferring columns from an **explicit registry**, not
  from the `sprk_assigned*` prefix convention. The registry names columns; the convention guesses.
- **MUST** cover **contact-typed AND organization-typed** lookups in that registry. Org-typed conferral
  is real (M4's `sprk_assignedlawfirm1/2`) and currently unfiltered.
- **MUST** treat adding a conferring column as a **registry edit** — a reviewable change whose subject
  is access. **Renaming a column MUST NOT grant or revoke access** (FR-24 acceptance). This is the
  property the prefix convention could not provide.
- **MUST** keep the registry inside the canonical resolver (M1). A parallel membership mechanism
  remains forbidden.

### Explicitly NOT amended

- **The 1-hop cap (M7 / N4) stands unchanged, and needs no exception.** FR-26 denormalizes the **core
  ancestor** onto each child record, so every child→core chain is **one hop by construction**. There is
  no multi-hop request to permit — the data model removed the need, rather than the rule being relaxed.
  Requests deeper than 1 hop still return `400` with `transitive-chain-too-deep`.
- **M8 / M9 event semantics** (topic not queue; fire-and-forget with the nightly reconciliation
  backstop) — unchanged.
- **N2, the non-existent-entity ban** — unchanged. Precision worth recording: the ban is on joining
  through entities that do not exist (e.g. `sprk_matterteammember`). Real `teammembership` **is**
  legitimately used and always was; the ban is narrower than some project docs assert (investigation
  02 §10.4).
- **M1, the canonical resolver** — reinforced, not weakened.

### A sanctioned future extension (not built here)

A **record → members** inverse read ("who can see this record?", for the Manage Access UI and
attestation) is a **future extension of this same canonical mechanism** — the resolver answering the
existing question in the opposite direction. It is explicitly **not** a licence for a second resolver
(investigation 02 §8 direction note).

### Live-consumer check (done before amending)

Codifying a per-surface split is only safe if nothing outside the authorization surface currently
depends on unfiltered systemuser-plane descriptors *as an access answer*. Every consumer of
`IMembershipResolverService` was enumerated and classified:

| Consumer | Surface |
|---|---|
| `AccessibleRecordSetService` | Authorization — the one this project rehomes (Phase 1) |
| `MembershipEndpoints` (`/api/users/me/memberships/*`) | Scoping — returns the caller's **own** memberships under OBO; a self-query, not a decision about another principal |
| `DailyBriefingCollector`, `BriefingService` | AI scoping |
| `LookupUserMembershipNodeExecutor`, `NodeService` | AI scoping (playbook node) |
| `IThreadPrivateGrantProvider` | **Not a consumer** — a doc-comment cross-reference only; no code dependency |

**No consumer outside `AccessibleRecordSetService` uses these descriptors as an access answer**, so the
narrowing changes no other surface's behaviour contract.

---

## Amendment A3 (2026-10-02, accepted): The people-targeting surface — who a record is FOR

> **Status**: **Accepted** (resolution path **B — amendment**, per root CLAUDE.md §6.5). The owner approved it in
> round 3 (2026-09-30), consolidated decision **D1** — "Path B for both" ADR-034 amendments, of which (2) is "let a
> human 'Created By' count as membership for briefings and notifications only, not for access or AI scoping" — accepted
> as recommended and clarified ("Created By decides who a record is FOR; it never decides who can OPEN it").
> Source: `projects/unified-access-control-r2/notes/session27-owner-decisions-and-research.md` round 3, and
> `notes/raw/session27-owner-questions.json` (D1). The concise version in `.claude/adr/ADR-034-user-record-membership.md`
> is applied by the main session (sub-agents cannot write `.claude/`) with, or before, the task 152 PR.
> **Numbering**: A3 is the next free number at drafting time (A1 and A1.1 exist; task 036 has claimed
> "Amendment 2"). If task 142's ADR-034 amendment merges first and takes A3, renumber this one at merge.
> **Driver project**: `unified-access-control-r2` task 152 (GitHub #1073; membership half of #1044).
> **Owner decisions**: round 2 item 9 ("target Created By and Assigned To. A team-owned record does not fan out to
> team members") and Q8 (the "for" person is `sprk_todo.sprk_assignedto` plus Created By); round 3 D1 ("Created By
> decides who a record is FOR — never who can open it; the briefing only lists records the user can access");
> round 3 A7 (Office quick-create names its maker in the internal Assigned-To); round 3 S1 (BFF-created child rows
> name their person in Assigned To). Recorded in
> `projects/unified-access-control-r2/notes/session27-owner-decisions-and-research.md`.

### Why

A1 split the resolver's output into two surfaces: **AI scoping** (generous, every discovered descriptor) and
**authorization** (the registry + platform ownership). The Daily Briefing, the Workspace top-priority matter and every
notification playbook's `LookupUserMembership` node were filed under AI scoping — but they do not retrieve context for
an answer, they decide **whose attention a record deserves**. Used for that, both existing surfaces are wrong:

1. **Team and business-unit ownership fan out to the whole unit.** `owningteam` binds every team the caller is in, and
   a business unit's **default** team contains every user in the unit (`teammembership` carries no `isdefault`
   exclusion); `owningbusinessunit` binds the caller's own unit. A team- or BU-owned matter therefore surfaced in the
   briefing of every user in that unit. That is correct for **access** (A1.1, unchanged) and wrong for **attention**.
2. **The person who made a record is invisible.** M3 excludes `createdby` as touch-history on every surface. The owner
   requires Created By to make a record "for" its maker.
3. **The person a record names is reachable only through a contact.** The "Assigned *" columns are CONTACT lookups; an
   internal user matches them only through a reliable systemuser↔contact link — built by task 141.

No compliant shape exists (§6.5 path C rejected): with `createdby` excluded there is no column that says "the person
who made it", and `createdonbehalfby` is also excluded and empty for app-only creates. A project-scoped exception
(path A) was rejected because the briefing and notification surfaces are repo-wide and permanent.

### The third surface (new)

| Consumption surface | Option | Which descriptors bind |
|---|---|---|
| AI scoping (unchanged) | `options: null` | All discovered descriptors |
| Authorization (A1 / A1.1, unchanged) | `AccessConferringOnly: true` | Registry Contact/Organization columns + `ownerid` / `owningteam` / `owningbusinessunit` |
| **People targeting (A3)** | `PeopleTargeting: true` | A **human** `createdby` = the caller; the user-valued owner (`ownerid` / `owninguser`) = the caller; registry **Contact**-typed "Assigned *" columns = the caller's **linked** contact. **Nothing else.** |

**One mechanism, three policies.** People targeting is a filter inside `MembershipResolverService`, beside
`AccessConferringOnly` — not a second resolver.

### New MUST / MUST NOT rules

- **MUST** select records for a person's briefing, notifications and attention surfaces through the people-targeting
  surface — never through the AI-scoping default, never through ad-hoc `createdby` / `owninguser` conditions in a
  consumer (the A1/D5 anti-pattern).
- **MUST** admit `createdby` **on the people-targeting surface only**, and **only when the caller is a HUMAN
  systemuser** (`systemuser.applicationid` null). An application user (e.g. the BFF's own identity, Created By of every
  BFF-created row) binds no `createdby` condition; an unreadable systemuser row is treated the same way. The M3
  global exclusion of `createdby` **stays** for AI scoping and authorization.
- **MUST** bind the "Assigned *" term only through the caller's linked contact (`PersonIdentity.ContactId`, task 141).
  **MUST NOT** fall back to an email, UPN or display-name match (the C7 hijack path). No link → the term binds
  nothing, with a structured warning.
- **MUST NOT** let `owningteam`, `owningbusinessunit`, a team-valued `ownerid`, or any Team-, BusinessUnit-,
  Organization- or Account-typed descriptor select anything on this surface. A team-owned record never fans out to the
  team's members. Team/BU ownership keeps conferring ACCESS (A1.1).
- **MUST** reject `PeopleTargeting` together with `AccessConferringOnly` (`ArgumentException`) — the two surfaces
  answer different questions and are never merged.
- **MUST** include `PeopleTargeting` in the resolver's options hash, so a people-targeted call and an AI-scoping call
  for the same user and entity never share a cache entry (the task-043 hazard, in the opposite direction).
- **MUST** read every row a people-targeted consumer SHOWS under the caller's Dataverse security
  (`IImpersonatedCommunicationQuery`, MSCRMCallerID = the caller). Selecting is not authorizing (owner D1). A failed
  caller-context read is reported as failed — never answered app-only, never shown as "nothing to report".
- **MUST**, in the **Daily Briefing and Workspace consumers** (`DailyBriefingCollector`, `PortfolioService` and,
  through it, `BriefingService`'s top-priority matter), read the people-targeted set to completion
  (`PeopleTargetedSet`: the resolver's 5,000-row ceiling plus one confirmation read). The resolver pages in
  primary-id order, so a default 500-row page is an arbitrary subset; a set larger than the ceiling is reported
  failed / unavailable — never a silently truncated list (task 152 verifier round 1).
  **Scope note — the `LookupUserMembership` node is NOT covered by this rule.** With `"targeting": "people"` it reads
  ONE page at `MembershipResolveOptions.DefaultLimit` (500) and exposes `continuationToken`, exactly as it did before
  A3 (pre-existing paging, unchanged). It is not routed through `PeopleTargetedSet` because the notification playbooks
  interpolate `myMatters.ids` into a downstream FetchXML `in` condition, and a 5,000-value `in` list is not a safe
  query (Dataverse passes `in` values as SQL parameters; SQL Server refuses more than 2,100). A person with more than
  500 people-targeted matters can therefore get notifications for a subset only. Closing that needs the downstream
  query to page or chunk its `in` list — a separate change to the node/query contract.
- **MUST** make server-created records name a person in an "Assigned *" column, because their Created By is the
  application user: the to-do and task writers fill `sprk_assignedto` (a supplied assignee is kept; else the
  triggering person's linked contact; else the regarding parent's `sprk_assignedtointernal`, then
  `sprk_assignedattorney1`; else blank + `todo_unassigned`; never a team). Office quick-create fills the matter /
  project `sprk_assignedtointernal` with its maker (A7).

### Event semantics (M8 / M9 — clarified, wire contract unchanged)

- A `MembershipChangedEvent` describes the row's **actual owner after the write**: `PersonIdType = Team, PersonId =
  teamid` for a team-owned row; `PersonIdType = User, PersonId = systemuserid` for a user-owned row. The polymorphic
  owner is typed from the value (`EntityReference.LogicalName`), never from the descriptor (which is always SystemUser
  for an Owner column).
- Both junction writers — the create-time publishers and `MembershipReconciliationJob` — key the junction in the
  **same identity space** (Dataverse ids, never the AAD oid) and treat an **application-user** owner identically (no
  event, no row; an unreadable check never deletes an existing row).
- `createdby` produces **no** events: the junction stores association, and the people-targeting surface resolves
  Created By live.
- `schemaVersion` stays 1 and the closed enums / property names are unchanged; the only consumer
  (`MembershipJunctionUpdater`) writes `PersonId` verbatim and no consumer parses the old caller-oid semantics.
  Rows written under the old semantics (caller oid as User) are orphans and the reconciliation orphan scan removes them.

### Explicitly NOT amended

- **A1 / A1.1** — `AccessConferringOnly: true` returns exactly what it did; `AccessibleRecordSetService` is untouched.
  The BU over-grant on the authorization plane is task 036's.
- **The AI-scoping default** — byte-identical (descriptors, FetchXML, cache key) for every caller that does not opt in.
- **The 1-hop cap, M1, N2** — unchanged.

### Owner confirmations (all answered — binding owner decisions, session27 rounds 2 and 3)

- The amendment itself: **round 3 D1** — path B for both ADR-034 amendments (this one is D1 (2)); accepted as
  recommended and clarified (Created By decides who a record is FOR, never who can open it).
- (a) Personal user ownership (`ownerid` / `owninguser` = the caller) as a third person term: **round 3 B1, option
  (1) — keep it**, accepted as recommended. It names exactly one person and covers records reassigned to the caller in
  MDA.
- (b) High Priority = the flagged records in the caller's people-targeted set that the caller can read: **answered by
  round 2 item 9** (`raw/session27-owner-questions.json` `droppedAsAnswered`, entry "152(b)": "Show only flagged
  records in the user's people-targeted set that they can read").
- (c) App-only creates: answered by the owner's round 3 **S1** (events default Assigned To to the acting user's
  contact) and **A7** (Office quick-create defaults the internal Assigned-To to the maker).
- (d) External-portal to-do Assigned To and parent grants: **round 3 A6, option (a)** — a child-entity Assigned field
  confers no root grant.
- Deployment order with task 146: **round 3 B2, option (1)** — if 152 cannot deploy with or before 146, an interim
  drop-out of some to-dos from the Daily Briefing is accepted, announced and recorded in the PR. (This is the
  round 3 consolidated B2, not round 4's "B2" for the `sprk_externalobjectid` alternate key.)

### Live-consumer check (done before amending)

| Consumer | Surface after A3 |
|---|---|
| `DailyBriefingCollector` (render + email + High Priority) | People targeting |
| `PortfolioService` (Workspace portfolio / health metrics) and, through it, `BriefingService.GetTopPriorityMatterAsync` | People targeting (was an app-only ad-hoc `ownerid` = caller filter; fixed in task 152 verifier round 1) |
| `LookupUserMembershipNodeExecutor` with `"targeting": "people"` (every notification playbook) | People targeting (one 500-row page + `continuationToken`, pre-existing paging; see the completeness MUST's scope note) |
| `LookupUserMembershipNodeExecutor` without `targeting` | AI scoping (unchanged) |
| `MembershipEndpoints` (`/api/users/me/memberships/*`) | AI scoping (unchanged) |
| `AccessibleRecordSetService` | Authorization (unchanged) |

---

## Amendment A4 (2026-10-03, accepted): Assigned-To access for contacts is materialized as removable grants

> **Status**: **ACCEPTED** (owner round 11, 2026-10-03) — resolution path **B — amendment**, per root CLAUDE.md §6.5.
> The owner accepted it explicitly in round 11, item 1: "142: ADR-034 Amendment A4 is ACCEPTED (§6.5 path B).
> Assigned-To access for contacts is materialized as removable Collaborate grants (the substance is round 2 item 5 and
> Q5). The main session applies the concise `.claude/adr/ADR-034` edit with the 142 PR." (Round 3 D1 had accepted path B
> for the 036 and 152 amendments only; this acceptance is A4's own.) The SUBSTANCE is owner-decided and binding:
> round 2 item 5 ("The 'Assigned To *' auto grants were one of the core
> reasons for the UAC — this needs to continue being a feature") and **Q5** ("the 'Assigned *' contacts are automatically
> granted Collaborate by a function that ADDS the contact to the grant-access list, so an operator can remove it");
> round 3 **A1** (Collaborate, no grantor cap), **A2 reversed** (standing grants and organization access STAY; Assigned-To
> grants are ADDED), **A3–A6, A8** accepted as recommended, **R3/R4** (minutes, not hourly). Source:
> `projects/unified-access-control-r2/notes/session27-owner-decisions-and-research.md` and
> `notes/raw/session27-owner-questions.json`.
> **Driver**: `unified-access-control-r2` task 142 (GitHub #1065). Full record:
> `projects/unified-access-control-r2/notes/task-142-assigned-field-auto-grants.md`.
> **Concise version**: `.claude/adr/ADR-034-user-record-membership.md` is applied by the MAIN session (sub-agents cannot
> write `.claude/`), with the task 142 PR. **Merge rule**: the task 142 code merges with this amendment accepted (it is,
> round 11) and with that concise edit in the same PR, never before.

### Why

A1 made the access-conferring registry first-class and gave it one consumer: the evaluator's **read-time** terms. For a
CONTACT, a registry column confers nothing by itself today — the standing-grant term (FR-25) and organization expansion
(FR-24) confer only for a contact or organization that already holds a standing grant. The owner requires the opposite
shape for Assigned-To access: a contact named in an "Assigned *" column receives **Collaborate** as an **entry on the
record's grant-access list**, which an operator can see and **remove** in Manage Access.

A read-time term cannot carry that: it is invisible on the model-driven app (so MDA and Teams/SPA would disagree — C9),
and it cannot be removed without a veto. The only veto is the No Access List, which is a stronger, different statement
("this subject must never see this record") — an operator who removes an auto grant has not walled the contact, and may
grant them again by hand.

**Alternatives considered.** (A) A project-scoped exception — rejected: the rule being changed ("MUST NOT materialize
derived access into grant rows", spec.md / FR-32 / design §7) is repo-wide via this ADR, and every later reader would
meet the contradiction. (C) Keep the read-time term and add a per-record "declined" veto — rejected: a second deny
mechanism beside the No Access List (CLAUDE.md §11), still invisible on MDA (C9).

### The decision

The A1 registry gains a **second consumer: a write-time invariant owner**. For each registry-listed Contact- or
Organization-typed column on a project, matter or work assignment, `AssignedAccessMaterializer`
(`Services/ExternalAccess/`) maintains Collaborate access for the named subject as **ordinary, explicit, removable
access**: a `sprk_externalrecordaccess` grant (contact or organization), or a POA share when the contact is linked
(task 141) to an eligible internal user. Its provenance — per (root, source field, subject) — lives in the
`sprk_assignedaccess` ledger. The read-time terms are **kept** (A2 reversed).

| Consumer of the registry | What it does with a registry column |
|---|---|
| Authorization, read time (A1 — **unchanged**) | Derived-member and org-expansion terms, standing-grant-gated for contacts/organizations |
| People targeting (A3 — **unchanged**) | Contact columns bind the caller's linked contact (attention, not access) |
| **Assigned-To materialization (A4 — new)** | Writes and maintains explicit grants/shares for the named subjects, with a ledger |

### New MUST / MUST NOT rules

- **MUST** materialize Assigned-To access only through the ONE invariant owner (`AssignedAccessMaterializer`), called
  by every trigger: L1 inline after each BFF writer of the columns, the sync route
  (`POST /api/v1/external-access/assigned-access/sync` — form post-save, client wizards, "Update Access"), and L4
  `AssignedAccessReconciliationJob`. **MUST NOT** add a second writer, a plugin, a flow or a service-endpoint step
  (ADR-002, D-1).
- **MUST** read the conferring columns from the bound registry (`MembershipOptions.AccessConferringRoles`) — the same
  registry A1 governs, so adding a conferring column remains a reviewed registry edit and renaming a column grants or
  revokes nothing. **MUST NOT** materialize from a child-entity registry entry (event, invoice, to-do, analysis): a
  contact assigned to one event is not assigned to the matter (owner A6).
- **MUST** write at Collaborate through the existing write cores — grants through `GrantExternalAccessEndpoint.CreateGrantAsync`
  with the documented ceiling `GrantCeiling.AssignedToRule` (uncapped by the saving user's level, owner A1; an absent
  expiry becomes today + 90 through `DefaultExpiry`), shares through `IDataverseRecordShareService` with the
  `RecordShareLevels` Collaborate mask and the strict read + read-back discipline. **MUST NOT** write
  `sprk_externalrecordaccess` directly or introduce a new level constant.
- **MUST NOT** lower existing access: an equal or higher grant/share that confers access is left untouched
  (`CoveredByExisting`); a lower one is raised and put back — its level AND its date — when the assignment ends. (An
  expired grant confers nothing and is not existing access: the rule gives Collaborate over it, even over an expired
  Full Access level, which it cannot write back — task 142 notes §6.)
- **MUST** record an operator's removal (Manage Access revoke/unshare, Dismiss of a suggestion, or a removal outside the
  BFF that no known cause explains) as **`Declined`**, and **MUST NOT** re-create a Declined entry while the assignment
  persists. Declined is **not** a veto: a manual grant of the same subject still succeeds (and is recorded `Adopted`).
  Known causes are not declines: an inactive root, an inactive organization (R2), task 143's No Access enforcer
  (re-created once the wall is lifted).
- **MUST**, when the column is changed or cleared, remove only the owner's own **unmodified** access (owner A4) — never
  an `Adopted` or `Declined` entry, never while another registry column on the same root still names the subject.
- **MUST** apply the record's policy before writing (round 2 item 3; owner A3/A8): Restricted → no contact or
  organization grant (a linked internal user's share is unaffected); Secure or Limited → no organization grant; Secure →
  contact grants and shares are **suggested** (`PendingConfirmation`, Grant/Dismiss in Manage Access), not written, and
  an auto grant that existed before the record became secure is kept; the No Access List for contacts everywhere and,
  via task 143's guard, for internal users on secure records. **MUST** write nothing when a flag set, deny list, link
  or ledger cannot be read (ADR-003 / WP-6).
- **MUST**, when an operator removes an auto grant from a subject that still reaches the record through a kept read-time
  term (standing grant, organization expansion), **say so, naming the term**, before and after the removal — an
  operator is never shown "removed" while access silently remains.

### Spec / design amendment text (applied with this amendment)

- **spec.md, MUST NOT list** — "❌ MUST NOT materialize derived access into grant rows" becomes: "❌ MUST NOT materialize
  derived access into grant rows — **except Assigned-To access (ADR-034 A4)**, which is materialized as explicit,
  removable grants or POA shares by its one invariant owner, with provenance in `sprk_assignedaccess`."
- **spec.md FR-32** — the acceptance clause "derived access is **not** materialized into rows" becomes: "derived access
  is not materialized into rows, **other than Assigned-To access (ADR-034 A4)**: those grants are ordinary grant state
  changes, written through `CreateGrantAsync` and logged like any other grant."
- **spec.md FR-25** — **unchanged** (owner A2 reversed: standing grants keep contributing at their baseline).
- **design.md §7 Attestation** — after "Do **not** materialize derived access into rows", add: "The one exception is
  Assigned-To access (ADR-034 A4, task 142): the owner requires it to be a removable entry on the grant-access list, so
  it is written as ordinary grants/shares, each grant change captured by the FR-32 event log; the ledger
  `sprk_assignedaccess` records why each exists."

### Explicitly NOT amended

- **A1 / A1.1** — the registry, its per-surface policy and the read-time evaluator terms are unchanged.
  `AccessibleRecordSetService` is untouched by this amendment.
- **A3** — people targeting is unchanged.
- **FR-24 / FR-25** read-time standing and organization-expansion terms — kept (A2 reversed).
- **The 1-hop cap, M1, N2, M8/M9 event semantics** — unchanged.

### Residual stated with the amendment

A non-product write of an "Assigned *" column (grid edit, import, flow) is materialized at the next job tick (≤ 5 min).
In the removal direction the job is report-only until `ExternalAccess:AssignedAccess:JobRevokeOnChangeEnabled` is
turned on (owner R3/(g): turned on in dev after the live gate) — until then a field cleared OUTSIDE the product leaves its
auto grant in place (fail-open in that direction only). The sync route and the L1 writers always remove.

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
