# BFF Event-Source Endpoint Inventory (P-event-1 — FR-2P2.5)

> **Task**: R3-080 (P-event-1 discovery)
> **Authored**: 2026-06-22
> **Status**: Final — ready for P8 dispatch (tasks 081 / 082 / 083)
> **Spec reference**: FR-2P2.5 (this discovery), FR-2P2.6 (hookup), AC-1P2.3 (acceptance gate)
> **Predecessor reference inventory**: [`sprk-searchindexed-consumer-inventory.md`](sprk-searchindexed-consumer-inventory.md) (task 060) — section structure mirrored here

---

## 1. Discovery method

- **Tool**: `Grep` (ripgrep) over `src/server/api/Sprk.Bff.Api/`.
- **Primary regex**: `\.(MapPost|MapPut|MapPatch|MapDelete)\(` — captured **151 mutation endpoint registrations** (POST/PUT/PATCH/DELETE). Full list saved to `tool-results/toolu_011WcJRkSkpzFfSqNE3cHiYm.txt` (35.9 KB; loaded once for triage).
- **Secondary regex** (Lookup-targeting identity tables): `ownerid|sprk_assigned|EntityReference\(.systemuser|EntityReference\(.sprk_matter|primarycontact` over `src/server/api/Sprk.Bff.Api/`. Surfaced **all** explicit BFF-side identity-Lookup mutations.
- **Tertiary regex** (DTO inspection): `CreateDocumentRequest`, `UpdateDocumentRequest`, `CreateEventRequest`, `UpdateEventRequest`, `QuickCreateRequest`, `CreatePrecedentApiRequest`, `CreateWorkAssignmentRequest`, `ExternalTodoDto` — read every request-body shape to confirm no hidden Lookup-field mutation reaches Dataverse via a wider DTO surface.
- **Files scanned**: every `.cs` under `src/server/api/Sprk.Bff.Api/Api/`, `src/server/api/Sprk.Bff.Api/Endpoints/`, `src/server/api/Sprk.Bff.Api/Services/`, and `src/server/shared/Spaarke.Dataverse/`.
- **Cross-check against entity-Lookup mapping**: [`MembershipFieldDiscoveryService.cs`](../../../src/server/api/Sprk.Bff.Api/Services/Ai/Membership/MembershipFieldDiscoveryService.cs) (the runtime classifier) + [`docs/data-model/sprk_matter-related-tables.md`](../../../docs/data-model/sprk_matter-related-tables.md) §1.1 (the 8 sprk_matter `sprk_assigned*` columns).

**Result headline**: 151 BFF mutation endpoints total. Of those, **only 2** explicitly set an identity-Lookup (systemuser / team / businessunit / contact / sprk_organization) on a membership-served entity. The remaining 149 mutations either (a) target out-of-scope entities, (b) mutate non-Lookup fields, or (c) mutate Lookups whose targets are non-identity tables (containers, projects, invoices, matters-as-FK, etc.).

---

## 2. Membership-served entity set (spec)

Per [`spec.md`](../spec.md) FR-2P2.5 line 134, the membership endpoint serves mutation events on:

| Entity logical name | In-repo evidence | Notes |
|---|---|---|
| `sprk_matter` | [`MembershipFieldDiscoveryServiceTests.cs:77-86`](../../../tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Membership/MembershipFieldDiscoveryServiceTests.cs), [`sprk_matter-related-tables.md`](../../../docs/data-model/sprk_matter-related-tables.md) §1.1 | Eight `sprk_assigned*` Lookups + ownerid/owningteam/owningbusinessunit |
| `sprk_document` | [`Models.cs:6-11`](../../../src/server/shared/Spaarke.Dataverse/Models.cs), [`DataverseDocumentsEndpoints.cs:26-243`](../../../src/server/api/Sprk.Bff.Api/Api/DataverseDocumentsEndpoints.cs) | Standard ownerid + system/audit lookups only — no `sprk_assigned*` |
| `sprk_event` | [`Models.cs:452-602`](../../../src/server/shared/Spaarke.Dataverse/Models.cs), [`EventEndpoints.cs:55-102`](../../../src/server/api/Sprk.Bff.Api/Api/Events/EventEndpoints.cs) | Standard ownerid + 8 `sprk_regarding*` (NOT identity Lookups) |
| `sprk_task` | inferred from spec line 134 — no in-repo CRUD endpoint surface exists; OOTB `task` is mutated by [`CreateTaskNodeExecutor`](../../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/CreateTaskNodeExecutor.cs) but that targets the OOTB schema, not `sprk_task` (custom). See **Risks §6.1**. |
| `sprk_opportunity` | inferred from spec line 134 — no in-repo BFF endpoint mutates `sprk_opportunity`. See **Risks §6.1**. |

The five-entity set ships in R3 as named in FR-2P2.5 (matter, document, event, task, opportunity). Other entities (precedent, work-assignment-as-source, todo, workspacelayout, communication, etc.) are **out of FR-2P2.5 scope** — they're tracked under **Other** for follow-up consideration but are NOT slated for hookup in tasks 081–083.

---

## 3. Inventory by cluster

> **Convention**: "MembershipMutationType" = the `mutationType` field on `MembershipChangedEvent` (per FR-2P2.2):
> - `Added` — Lookup was null → value
> - `Removed` — Lookup was value → null
> - `Updated` — Lookup was value₁ → value₂

### 3A. Matter cluster (target task 081)

| File:Line | Verb + path | Mutation | Identity Lookup field(s) set by endpoint? | MembershipMutationType derivation |
|---|---|---|---|---|
| [`Office/OfficeEndpoints.cs:1119`](../../../src/server/api/Sprk.Bff.Api/Api/Office/OfficeEndpoints.cs#L1119) | `POST /office/quickcreate/{entityType}` (when `entityType=matter`) | Create | **NONE explicit.** The DTO ([`QuickCreateRequest.cs`](../../../src/server/api/Sprk.Bff.Api/Models/Office/QuickCreateRequest.cs)) exposes Name / Description / ClientId / Industry / City — `ClientId` is the account Lookup (NOT an identity table per the membership-served set). Dataverse defaults `ownerid` to the OBO caller on insert. | **Added** for the implicit `ownerid → callerSystemUserId` mutation only |

**Matter cluster total**: **1 endpoint** (BFF surface only). 

**The eight `sprk_assigned*` Lookups (attorney1/2, paralegal1/2, lawfirm1/2, tointernal, toexternal) are NOT mutated by any BFF endpoint** — they are exclusively maker-portal-edited today (or set by external integrations). This is the biggest finding of this inventory (see §6.1 / §6.2). The nightly `MembershipReconciliationJob` (FR-2P2.7) becomes the load-bearing path for keeping matter junctions current; the BFF event-publishing in task 081 will cover only the QuickCreate `ownerid` default case.

### 3B. Document cluster (target task 082, sub-A)

| File:Line | Verb + path | Mutation | Identity Lookup field(s) set by endpoint? | MembershipMutationType derivation |
|---|---|---|---|---|
| [`DataverseDocumentsEndpoints.cs:26`](../../../src/server/api/Sprk.Bff.Api/Api/DataverseDocumentsEndpoints.cs#L26) | `POST /api/v1/documents/` | Create | **NONE explicit.** `CreateDocumentRequest` exposes Name / ContainerId / Description only. Dataverse defaults `ownerid` to OBO caller. | **Added** for implicit `ownerid` |
| [`DataverseDocumentsEndpoints.cs:129`](../../../src/server/api/Sprk.Bff.Api/Api/DataverseDocumentsEndpoints.cs#L129) | `PUT /api/v1/documents/{id}` | Update | **NONE explicit.** `UpdateDocumentRequest` exposes 50+ fields — Matter / Project / Invoice / ParentDocument / EmailLookup are the Lookups, but their targets (`sprk_matter`, `sprk_project`, `sprk_invoice`, `sprk_document`, `sprk_email`) are NOT in the identity-table set (systemuser, contact, team, sprk_organization, businessunit). | n/a (no identity-Lookup mutation) |
| [`DataverseDocumentsEndpoints.cs:196`](../../../src/server/api/Sprk.Bff.Api/Api/DataverseDocumentsEndpoints.cs#L196) | `DELETE /api/v1/documents/{id}` | Delete | n/a — hard delete removes all membership rows. | **Removed** for every Lookup populated on the deleted row at delete time |
| [`Office/OfficeEndpoints.cs:166`](../../../src/server/api/Sprk.Bff.Api/Api/Office/OfficeEndpoints.cs#L166) | `POST /office/save` | Create | Office add-in save creates a `sprk_document`. Ownerid defaulted by Dataverse. | **Added** for implicit `ownerid` |
| [`Ai/RecordMatchEndpoints.cs:29`](../../../src/server/api/Sprk.Bff.Api/Api/Ai/RecordMatchEndpoints.cs#L29) | `POST /api/ai/document-intelligence/associate-record` | Update | Associates a document with a Matter / Project / Invoice — the Lookup target is the **doc-side `sprk_matter` field** (lookup of matter, target=`sprk_matter`). This is NOT an identity-Lookup mutation. | n/a (no identity-Lookup mutation) |

**Document cluster total**: **5 endpoints**. Only `ownerid` (implicit, via OBO) is in scope. No `sprk_assigned*` analogs on `sprk_document`.

### 3C. Event cluster (target task 082, sub-B)

| File:Line | Verb + path | Mutation | Identity Lookup field(s) set by endpoint? | MembershipMutationType derivation |
|---|---|---|---|---|
| [`Events/EventEndpoints.cs:55`](../../../src/server/api/Sprk.Bff.Api/Api/Events/EventEndpoints.cs#L55) | `DELETE /api/v1/events/{id}` | Soft-delete (status → Cancelled/Deleted) | n/a — soft delete preserves all Lookup values; ownerid unchanged. | n/a (no Lookup mutation; soft-delete keeps ownerid populated). **Open question**: should "status=Deleted" be treated as a `Removed` event for matter-side junction-row cleanup? Recommend handling in 082 + spec follow-up. |
| [`Events/EventEndpoints.cs:66`](../../../src/server/api/Sprk.Bff.Api/Api/Events/EventEndpoints.cs#L66) | `POST /api/v1/events/` | Create | **NONE explicit.** `CreateEventRequest` exposes Name / Description / EventTypeId / dates / Priority / RegardingRecord*. Ownerid defaulted by Dataverse to OBO caller. RegardingRecord lookups target sprk_matter/sprk_project/etc. (NOT identity tables). | **Added** for implicit `ownerid` |
| [`Events/EventEndpoints.cs:77`](../../../src/server/api/Sprk.Bff.Api/Api/Events/EventEndpoints.cs#L77) | `PUT /api/v1/events/{id}` | Update | **NONE.** `UpdateEventRequest` has no ownerid / no identity Lookups. | n/a (no identity-Lookup mutation) |
| [`Events/EventEndpoints.cs:89`](../../../src/server/api/Sprk.Bff.Api/Api/Events/EventEndpoints.cs#L89) | `POST /api/v1/events/{id}/complete` | Update status | n/a — no Lookup mutation. | n/a |
| [`Events/EventEndpoints.cs:102`](../../../src/server/api/Sprk.Bff.Api/Api/Events/EventEndpoints.cs#L102) | `POST /api/v1/events/{id}/cancel` | Update status | n/a — no Lookup mutation. | n/a |

**Event cluster total**: **5 endpoints**, only Create (implicit ownerid) produces a membership event.

### 3D. Task cluster (target task 083, sub-A)

| File:Line | Verb + path | Mutation | Identity Lookup field(s) set by endpoint? | MembershipMutationType derivation |
|---|---|---|---|---|
| _(none — no in-repo BFF mutation endpoints on `sprk_task`)_ | — | — | — | — |

**Task cluster total**: **0 endpoints.** See **Risks §6.1** — there is no in-repo BFF endpoint that creates/updates/deletes `sprk_task`. The closest analog is [`CreateTaskNodeExecutor`](../../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/CreateTaskNodeExecutor.cs) which writes to OOTB `task` (NOT `sprk_task`) via direct Web API PATCH — out of scope for FR-2P2.5. Task 083 sub-A should be **marked empty for in-repo code; escalate maker-side task-creation surfaces (Power Automate, plugins) to ops audit** before relying on hookup-event coverage alone. Recon job (FR-2P2.7) covers any maker-side drift.

### 3E. Opportunity cluster (target task 083, sub-B)

| File:Line | Verb + path | Mutation | Identity Lookup field(s) set by endpoint? | MembershipMutationType derivation |
|---|---|---|---|---|
| _(none — no in-repo BFF mutation endpoints on `sprk_opportunity`)_ | — | — | — | — |

**Opportunity cluster total**: **0 endpoints.** Same finding + caveat as §3D.

### 3F. Other (NOT in FR-2P2.5 scope — informational only)

These BFF mutation endpoints DO set identity Lookups but on entities outside the FR-2P2.5 membership-served set. They are NOT slated for hookup in tasks 081–083 but are listed here for completeness in case scope expands post-R3.

| File:Line | Verb + path | Entity | Identity Lookup field(s) set | Why excluded from R3 hookup |
|---|---|---|---|---|
| [`WorkAssignmentEndpoints.cs:29`](../../../src/server/api/Sprk.Bff.Api/Api/WorkAssignmentEndpoints.cs#L29) | `POST /api/v1/work-assignments/` | `sprk_workassignment` | `ownerid = systemuser` (explicit, [line 73](../../../src/server/api/Sprk.Bff.Api/Api/WorkAssignmentEndpoints.cs#L73)); `sprk_matterid = sprk_matter` (line 79 — non-identity FK only) | `sprk_workassignment` is not in FR-2P2.5 set. Relevance: indirectly affects matter memberships if the workassignment-creating user is later resolved to be a matter member by some downstream flow — but R3 does not chain it. |
| [`Insights/PrecedentAdminEndpoints.cs:52`](../../../src/server/api/Sprk.Bff.Api/Api/Insights/PrecedentAdminEndpoints.cs#L52) | `POST /api/insights/admin/precedents` | `sprk_precedent` | `sprk_reviewerby = systemuser` ([`DataversePrecedentBoard.cs:94`](../../../src/server/api/Sprk.Bff.Api/Services/Insights/Precedents/DataversePrecedentBoard.cs#L94)) + N:N assoc to `sprk_matter` | `sprk_precedent` is not in FR-2P2.5 set. |
| [`Insights/PrecedentAdminEndpoints.cs:67`](../../../src/server/api/Sprk.Bff.Api/Api/Insights/PrecedentAdminEndpoints.cs#L67) | `POST /api/insights/admin/precedents/{id}/confirm` | `sprk_precedent` | May rewrite `sprk_reviewerby = systemuser` ([`DataversePrecedentBoard.cs:238`](../../../src/server/api/Sprk.Bff.Api/Services/Insights/Precedents/DataversePrecedentBoard.cs#L238)) | Same. |
| [`ExternalAccess/ExternalProjectDataEndpoints.cs:74`](../../../src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ExternalProjectDataEndpoints.cs#L74) | `POST /api/v1/external/projects/{id}/todos` | `sprk_todo` | ownerid defaults to OBO external caller (a contact-backed identity) | `sprk_todo` is not in FR-2P2.5 set. Spaarke To Do is a sibling system per [`docs/architecture/spaarke-todo-architecture.md`](../../../docs/architecture/spaarke-todo-architecture.md). |
| [`ExternalAccess/ExternalProjectDataEndpoints.cs:102`](../../../src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ExternalProjectDataEndpoints.cs#L102) | `PATCH /api/v1/external/todos/{id}` | `sprk_todo` | n/a (PATCH typically does not rewrite ownerid via this endpoint) | Same. |
| [`ExternalAccess/GrantExternalAccessEndpoint.cs:36`](../../../src/server/api/Sprk.Bff.Api/Api/ExternalAccess/GrantExternalAccessEndpoint.cs#L36) | `POST /api/v1/external/grant` | `sprk_externalaccess` (grant tracker) | `sprk_grantedby = systemuser` ([line 174](../../../src/server/api/Sprk.Bff.Api/Api/ExternalAccess/GrantExternalAccessEndpoint.cs#L174)) | `sprk_externalaccess` is not in FR-2P2.5 set. |
| [`CommunicationEndpoints.cs:50`](../../../src/server/api/Sprk.Bff.Api/Api/CommunicationEndpoints.cs#L50) | `POST /api/communications/send` (+ `/send-bulk`) | `sprk_communication` | `sprk_sentby = systemuser` ([`CommunicationService.cs:747`](../../../src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationService.cs#L747)) | `sprk_communication` is not in FR-2P2.5 set. |
| [`Memory/PinnedMemoryEndpoints.cs:157`](../../../src/server/api/Sprk.Bff.Api/Api/Memory/PinnedMemoryEndpoints.cs#L157) | `POST /api/memory/pins` | `sprk_pinnedmemory` | ownerid defaults to OBO caller | Not in scope. |
| [`Workspace/WorkspaceLayoutEndpoints.cs:66`](../../../src/server/api/Sprk.Bff.Api/Api/Workspace/WorkspaceLayoutEndpoints.cs#L66) | `POST /api/workspace/layouts` (+ PUT / DELETE) | `sprk_workspacelayout` | ownerid defaults to OBO caller; `WorkspaceLayoutService` filters by ownerid for user isolation | Not in scope. |
| (notification node executor) — [`CreateNotificationNodeExecutor.cs:488`](../../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/CreateNotificationNodeExecutor.cs#L488) | n/a (in-process playbook node) | `sprk_notification` | `ownerid@odata.bind = /systemusers({recipientId})` (explicit) | Not in scope; not an HTTP endpoint. |
| (task node executor) — [`CreateTaskNodeExecutor.cs:151`](../../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/CreateTaskNodeExecutor.cs#L151) | n/a (in-process playbook node) | OOTB `task` | `ownerid@odata.bind = /systemusers({ownerGuid})` (explicit) | OOTB `task` is not the FR-2P2.5 `sprk_task`. |

**Other cluster total** (informational): **10+ touchpoints** outside FR-2P2.5 scope. Document them here so the next person who reads this knows they were considered + intentionally excluded.

---

## 4. Cluster effort estimate (for 081/082/083 wave planning)

| Cluster | Endpoints | Task | Estimated effort | Notes |
|---|---|---|---|---|
| **Matter** | 1 (QuickCreate) | **081** | **~1.5h** | Authors the shared `MembershipEventPublisher` helper + wires QuickCreate. Shared infrastructure is the bulk of the work here, not the single hookup site. |
| **Document** | 5 (3 endpoints + Office/save + RecordMatch — but only 2-3 produce identity-Lookup events) | **082** sub-A | ~1h | Reuse `MembershipEventPublisher` from 081. Hookup: DataverseDocuments create + delete + Office save + the implicit `ownerid` on each. |
| **Event** | 5 (only Create produces an event) | **082** sub-B | ~0.5h | Reuse 081 helper. Single hookup (Create). |
| **Task** | 0 | **083** sub-A | ~0.25h | **Empty — file a no-op decision record + reference task 085 recon coverage**. |
| **Opportunity** | 0 | **083** sub-B | ~0.25h | Same as Task. |
| **Other** | 10+ | — | — | Out of R3 scope. |

**Aggregate**: tasks 081 + 082 + 083 combined ≈ **3.5h of actual hookup work**, dominated by 081's authoring of `MembershipEventPublisher`. After 081 ships the helper, 082+083 are mostly mechanical.

---

## 5. Cross-cutting infrastructure recommendation

Task **081** MUST author a shared `MembershipEventPublisher` helper at `src/server/api/Sprk.Bff.Api/Services/Ai/Membership/Events/MembershipEventPublisher.cs` (sibling to the existing `MembershipChangedEvent.cs` per task 072). Required surface:

- `Task PublishAddedAsync(personId, personIdType, entityLogicalName, entityRecordId, sourceField, role, correlationId, ct)`
- `Task PublishRemovedAsync(...)` — same signature, different `mutationType`
- `Task PublishUpdatedAsync(personId, ..., previousPersonId, ...)` — emits Removed+Added pair OR a single Updated event per FR-2P2.2 (settle in 081 design)
- Internal: `ServiceBusSender` to topic `sprk-membership-changes` (per FR-2P2.3); fire-and-forget semantics per FR-2P2.6 + Q2 owner decision; log publish failure as structured warning tagged with `correlationId`.
- DI: Singleton (matches existing `ServiceBusJobProcessor` lifetime).
- Tests: unit tests assert message envelope shape + failure logging path.

Tasks **082 + 083 reuse this helper without modification**. If any cluster needs cluster-specific logic, it lives in the per-endpoint hookup, not the helper.

This recommendation is binding for the wave-plan ordering: **081 MUST complete before 082 and 083 start** (which is what `TASK-INDEX.md` already reflects via the dependency edges `082 ← 081` and `083 ← 081`).

---

## 6. Risks / nuances

### 6.1 The `sprk_assigned*` mutation gap (BIG ONE)

The eight Q4-scoped membership Lookups on `sprk_matter` (`sprk_assignedattorney1/2`, `sprk_assignedparalegal1/2`, `sprk_assignedlawfirm1/2`, `sprk_assignedtointernal`, `sprk_assignedtoexternal`) are **NOT mutated by ANY BFF endpoint or BFF-side service** today. Verified via:

- Repo-wide grep for each field name returned only **read-side consumers** (`MatterLiveFactResolver`, `MembershipFieldDiscoveryService`, test fixtures, appsettings overrides, docs).
- No `UpdateMatter` endpoint exists at all (no `sprk_matter` PUT/PATCH endpoint registered).
- `QuickCreate` for matter (the only BFF write path) does not expose any of these Lookups in `QuickCreateRequest`.

**Implication**: the live event-publishing in tasks 081–083 will NOT keep the junction table up to date for the most important Q4 membership fields. **All matter-assignment changes flow through the maker portal (Power Apps form), Power Automate flows, or plugins — none of which are wired into BFF event publishing.** The defense-in-depth backstop is the nightly `MembershipReconciliationJob` (FR-2P2.7, task 085), which becomes the **load-bearing path** for matter-assignment freshness.

**Recommended action**: the spec's FR-2P2.6 description "Each mutation endpoint publishes…" reads accurate for what BFF mutates, but the *practical* coverage for Q4 fields is recon-only. Surface this in the wave plan + ADR-034 to set operator expectations. Junction freshness for `sprk_assigned*` fields = **24h max** (nightly recon cadence), not real-time.

### 6.2 Task and Opportunity clusters are empty for in-repo BFF code

No BFF endpoint mutates `sprk_task` or `sprk_opportunity` in the in-repo code. Same recon-only situation as §6.1. Tasks 083 sub-A and 083 sub-B should record an explicit "verified empty for in-repo BFF surface; relies on FR-2P2.7 recon for coverage" decision rather than hunting for endpoints that don't exist.

### 6.3 Office QuickCreate matter is the ONE explicit hookup for matter cluster

The single matter-cluster hookup site is `POST /office/quickcreate/{entityType=matter}`. This endpoint:
- Does NOT explicitly set `ownerid` — Dataverse defaults it to the OBO caller during insert.
- Idempotency is enforced via `IdempotencyFilter` — duplicate calls must NOT double-publish the membership event.
- The endpoint is rate-limited 5/min per user — event volume is low.

Task 081 implementation should:
- Capture the inserted record's `ownerid` value via either the OData response or a follow-up GET (since Dataverse populates ownerid server-side and the BFF doesn't know it pre-insert).
- Publish `MembershipChangedEvent { mutationType: "Added", sourceField: "ownerid", role: "owner", personId: <ownerSystemUserId>, entityLogicalName: "sprk_matter", entityRecordId: <newMatterId> }`.

### 6.4 Soft-delete semantics for Events

`DELETE /api/v1/events/{id}` is a soft-delete (sets `statuscode = Deleted`, not a Dataverse delete). The ownerid Lookup is preserved on the record. **Decision needed (defer to 082)**: does a soft-delete fire `Removed` events for the ownerid (and recon would then re-add them since the row still exists)? Recommendation: **do NOT fire on soft-delete** — junction rows mirror lookup state, not statecode. The recon job FR-2P2.7 will skip soft-deleted matters/events on its source-of-truth scan (filter `statecode = 0` Active only), so junction rows for soft-deleted entities will be cleaned up by recon's orphan-removal pass.

### 6.5 Bulk operations

Two BFF mutation endpoints are bulk:
- [`SpeAdmin/BulkOperationEndpoints.cs:50`](../../../src/server/api/Sprk.Bff.Api/Api/SpeAdmin/BulkOperationEndpoints.cs#L50) `POST /api/spe/bulk/delete` — bulk SPE document delete. Out of scope (SPE, not Dataverse).
- [`SpeAdmin/BulkOperationEndpoints.cs:65`](../../../src/server/api/Sprk.Bff.Api/Api/SpeAdmin/BulkOperationEndpoints.cs#L65) `POST /api/spe/bulk/permissions` — bulk SPE permission grant. Out of scope.

No bulk-update endpoints on `sprk_matter` / `sprk_document` / `sprk_event` exist in BFF. If maker-side bulk imports are run against `sprk_matter` (changing ownerid en masse), the recon job (FR-2P2.7) is the only catch — confirmed safe per the recon design.

### 6.6 Cross-cutting: ADR-032 (Null-Object Kill-Switch) applicability

The new `MembershipEventPublisher` service is conditionally registered when the Service Bus topic `sprk-membership-changes` is provisioned (task 071 blocked-operator). Until 071 completes, the helper SHOULD register a Null-Object implementation that no-ops at the publish call (per ADR-032 + `.claude/constraints/bff-extensions.md` §F.1). Task 081 must apply ADR-032 explicitly in its DI registration to avoid the asymmetric-registration anti-pattern. Recommend P1 (always register) + flag-gated implementation swap per ADR-032 pattern.

---

## 7. Acceptance

This inventory satisfies:

- ✅ **FR-2P2.5** (spec line 134): "Discovery task P-event-1 — inventory all BFF mutation endpoints touching person/team/BU lookups on matter / document / event / task / opportunity entities. Produces a checklist; subsequent FRs hook each endpoint."
- ✅ **AC-1P2.3** (spec line 277): "Event-source inventory (P-event-1) produces complete checklist of mutation endpoints. Verify: checklist exists in `projects/spaarke-platform-foundations-r3/notes/` or task notes." — this file lives at the canonical location.

Downstream tasks 081, 082, 083 can dispatch in parallel after 081 ships the shared `MembershipEventPublisher` helper, per §4 and §5.

**Operator-deploy gap acknowledged**: the topic `sprk-membership-changes` (FR-2P2.3) is provisioned by task 071 (`❌ blocked-operator`). Tasks 081/082/083 cannot complete their integration tests until 071 lands. The discovery work in THIS task (080) does NOT depend on 071 — the inventory is independent of the deployment state, and the recommendation in §6.6 lets 081's hookup code merge ahead of 071 via Null-Object DI per ADR-032.
