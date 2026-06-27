# Spaarke Platform Foundations (R3) — Design

> **Project**: spaarke-platform-foundations-r3
> **Status**: Design (draft for review — v2 after architectural sharpening 2026-06-20)
> **Predecessor**: [`spaarke-daily-update-service-r2`](../spaarke-daily-update-service-r2/) (R2 — Daily Briefing consumer + BFF /narrate; surfaced the gaps this project resolves)
> **Created**: 2026-06-20
> **Author**: Architecture review session, 2026-06-20

---

## Executive Summary

R3 addresses **three cross-cutting platform gaps** surfaced during R2 UAT that affect every entity-aware UI surface, every Notification playbook, and every scheduled background process in Spaarke — not just Daily Briefing:

1. **User-record membership resolution** — there is no canonical mechanism for "records this user is associated with, by entity type." The Daily Briefing's `notification-new-documents` playbook silently produced zero rows because its FetchXML joined through a non-existent `sprk_matterteammember` entity. Every "My X" feature is currently re-deriving membership in ad-hoc FetchXML per playbook / per UI surface, leading to silent breakage, inconsistent definitions, and configuration drift. R3 introduces a **discovery-based** `MembershipResolverService` with explicit per-entity overrides.

2. **Background-job infrastructure** — the BFF currently has **28 BackgroundService implementations**, none of which share a framework. Each invents its own `PeriodicTimer + IOptions<XOptions> + appsettings.json` pattern with no central registry, no admin trigger, no shared run-history audit. R3 introduces a small `Spaarke.Scheduling` library + `sprk_backgroundjob*` Dataverse entities + admin endpoints (`/api/admin/jobs/{jobId}/trigger|status|history`), ships two reference consumers (junction sync + migrated `PlaybookSchedulerService`), and leaves the other 26 services unchanged for opportunistic migration over time.

3. **Playbook engine hardening** — eleven known pitfalls in the JPS playbook engine + builder, ranging from silent template-engine breakage (Handlebars `??` operator not supported, used in 2 of 7 active playbooks) to missing builder validation (renaming a node's `OutputVariable` silently breaks all downstream `{{x.output}}` references).

All three share the same audience (BFF + playbook engine + builder), the same review surface, and benefit from a single round of ADR/doc updates. They ship together as R3.

### Naming collision register

An existing PCF named `AssociationResolver` ([`src/client/pcf/AssociationResolver/`](../../src/client/pcf/AssociationResolver/)) handles **record-to-record FieldMapping** (copying values when an Event's Regarding lookup is set). This is a DIFFERENT concept from R3's user-record membership. To avoid confusion, R3 uses **"Membership"** terminology throughout (`MembershipResolverService`, `/api/users/me/memberships/{entityType}`, ADR-034 "User-record membership resolution pattern"). Any code or doc work in this area should explicitly disambiguate.

### What's NOT in R3 (deferred to R2.2 hotfix on the current branch)

A small R2.2 UX hotfix ships separately and FAST on the current `work/spaarke-daily-update-service-r2-hotfix` branch (1–2 days, not part of R3):
- TL;DR rendered as 2-3 sentences + key takeaways bullets (was 5-7 sentences)
- Due date rendered per item in `NarrativeBullet`
- `AddToTodo` sets a sensible default due date + shows a confirmation toast
- **Interim FetchXML OR fix** for `notification-new-documents` and `notification-new-emails` playbooks — uses the actual lookup fields on each entity so "My Documents" / "My Emails" unblocks immediately, **before** R3's canonical resolver ships. R3's Part 1 supersedes this interim fix.

---

## Part 1 — User-Record Membership Resolution

### Problem Statement

#### What's broken today

**Specific failures verified during R2 UAT (2026-06-19)**:

| # | Defect | Where |
|---|---|---|
| **A1** | `notification-new-documents.json` playbook produces zero notifications. Its FetchXML joins `sprk_document → sprk_matter → sprk_matterteammember[sprk_user = currentuser]`. The `sprk_matterteammember` entity does not exist in the Spaarke data model. Grep confirms the only repo reference is the playbook config itself. | [`projects/spaarke-daily-update-service/notes/playbooks/notification-new-documents.json`](../../projects/spaarke-daily-update-service/notes/playbooks/notification-new-documents.json) |
| **A2** | `notification-tasks-due-soon.json` uses `{{userPreferences.timeWindow ?? '24h'}}` — Handlebars.NET does not support `??`. Templated FetchXML receives empty substitution and the query either errors or returns 0 rows. | [`projects/spaarke-daily-update-service/notes/playbooks/notification-tasks-due-soon.json`](../../projects/spaarke-daily-update-service/notes/playbooks/notification-tasks-due-soon.json) (covered in Part 3 §1) |
| **A3** | The data-model docs (`sprk_matter-related-tables.md`) do NOT list the Assigned Attorney/Paralegal/LawFirm columns that exist in the live `spaarkedev1` environment (verified by maker portal screenshot 2026-06-20). Documentation drift affecting any AI-generated code that consults the docs. | `docs/data-model/sprk_matter-related-tables.md` |

#### The generalized pattern

"Records this user is associated with" is needed across Spaarke:

- **Briefing widget**: which records have notifications for me?
- **Dashboard tiles**: My Matters, My Documents, My Tasks, My Events
- **Search refinement**: limit semantic search to records I'm on
- **Permission heuristics**: auto-share with people already on a related record
- **Notification routing**: notify everyone on this matter when a key event fires
- **AI Chat context**: scope conversation to entities the user is involved with

Today, each consumer re-derives membership independently. There is no canonical definition, no identity normalization across `systemuserid` ↔ `contactid` ↔ `email`, no indexed lookup, no shared cache.

### Proposed Solution

#### Phase 1A — Discovery-based MembershipResolverService (R3 implementation)

**Design philosophy**: convention-over-configuration via Dataverse metadata discovery. **Default behavior is auto-discovery** of all Lookup fields targeting person/organization tables; **explicit overrides** for system field exclusions, role-name customization, and edge cases.

##### Discovery algorithm

At startup (or first request per entity type, cached for 1 hour):

1. Query Dataverse metadata for the requested entity:
   ```http
   GET /api/data/v9.2/EntityDefinitions(LogicalName='sprk_matter')?$expand=Attributes($filter=AttributeType eq Microsoft.Dynamics.CRM.AttributeTypeCode'Lookup';$select=LogicalName,SchemaName,DisplayName,Targets)
   ```
2. Keep only Lookup attributes whose `Targets[]` includes ONE of the configured identity tables: `{systemuser, contact, team, businessunit, account, sprk_organization}`
3. Apply **global exclusions** (`createdby`, `modifiedby`, `createdonbehalfby`, `modifiedonbehalfby` — these are touch-history, not association)
4. Apply **per-entity overrides** (excluded fields, role-name overrides, force-include for globally excluded fields if needed)
5. Derive **role name** per field using `CamelCase` strategy: strip `sprk_` prefix + strip trailing numeric digits + camelCase (e.g., `sprk_AssignedAttorney1` → `"assignedAttorney"`)
6. Derive **identity type** from the lookup's target table
7. Cache the runtime descriptor list per entity type

##### Configuration shape (appsettings.json)

```json
"Membership": {
  "IncludedIdentityTables": [
    { "table": "systemuser",       "identityType": "SystemUser" },
    { "table": "contact",          "identityType": "Contact" },
    { "table": "team",             "identityType": "Team" },
    { "table": "businessunit",     "identityType": "BusinessUnit" },
    { "table": "account",          "identityType": "Account" },
    { "table": "sprk_organization","identityType": "Organization" }
  ],
  "GlobalFieldExclusions": [
    "createdby", "modifiedby", "createdonbehalfby", "modifiedonbehalfby"
  ],
  "RoleNameStrategy": "CamelCase",
  "MetadataCacheTtlMinutes": 60,
  "EntityOverrides": {
    "sprk_matter": {
      "ExcludedFields": [],
      "IncludedFields": [],
      "FieldRoleOverrides": {
        "sprk_assignedlawfirm1": "assignedLawFirm",
        "sprk_assignedlawfirm2": "assignedLawFirm"
      }
    }
  }
}
```

##### Why discovery over explicit enumeration

| | Explicit enumeration (rejected) | Discovery + overrides (chosen) |
|---|---|---|
| Maintenance when entity adds field | Manual config update (drift risk — D5 root cause) | Automatic |
| Config size | ~10 entries × N entities | Tiny defaults + small per-entity overrides |
| Drift risk | High (the D5 / A1 root cause) | Low (auto-discovers) |
| Surprise factor | Low | Medium — but mitigated by Discovery Report endpoint |
| Edge case handling | Easy (configure everything) | Possible via overrides |
| Performance | Zero overhead | Metadata cache + filtering per first request per entity (negligible after cache) |

##### Discovery Report endpoint (operator audit)

```http
GET /api/admin/membership/discovered/{entityType}
RequireAuthorization("PlatformAdmin")

Response:
{
  "entityType": "sprk_matter",
  "discoveredAt": "2026-06-20T14:30:00Z",
  "discoveredFields": [
    { "field": "ownerid", "role": "owner", "identityType": "SystemUser", "source": "auto" },
    { "field": "owningteam", "role": "owningTeam", "identityType": "Team", "source": "auto" },
    { "field": "sprk_assignedattorney1", "role": "assignedAttorney", "identityType": "Contact", "source": "auto" },
    { "field": "sprk_assignedlawfirm1", "role": "assignedLawFirm", "identityType": "Contact", "source": "override" }
  ],
  "excludedFields": [
    { "field": "createdby", "reason": "global-exclusion" },
    { "field": "modifiedby", "reason": "global-exclusion" }
  ],
  "ignoredFields": [
    { "field": "sprk_chartdefinition", "reason": "target-table-not-in-identity-list", "target": "sprk_chartdefinition" }
  ]
}
```

Operators run this against each new entity to confirm discovery did the right thing before turning the entity loose in production.

##### Endpoint contract

```http
GET /api/users/me/memberships/{entityType}
  ?roles=owner,assignedAttorney             (optional; default: all discovered roles)
  ?identityTypes=SystemUser,Contact         (optional; default: all configured types)
  ?includeRelated=documents,events          (optional; transitive memberships — Phase 1D)
  ?limit=500
  ?continuationToken={token}

Authentication: standard Spaarke Auth v2 OBO (ADR-028)

Response (200 OK):
{
  "entityType": "sprk_matter",
  "personIdentity": {
    "systemUserId": "...",
    "contactId": "...",
    "primaryEmail": "...",
    "teamIds": ["..."],
    "businessUnitId": "..."
  },
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

##### Identity normalization contract

Codified in **ADR-034**. Cached per user (Redis, 10-min TTL).

| Source field type | Resolves via | Match value |
|---|---|---|
| `Lookup → systemuser` | Direct | `systemUserId` |
| `Lookup → contact` | Direct; cross-referenced to `systemUserId` via `azureactivedirectoryobjectid` (per ADR-028) | `contactId` |
| `Lookup → team` | Expand `teammembership` to systemusers | `teamIds[]` (cached) |
| `Lookup → businessunit` | User's BU + any descendant BUs (configurable per role) | `businessUnitId` |
| `Lookup → account` | User's primary `parentcustomerid` (if contact) | `accountId` (when applicable) |
| `Lookup → sprk_organization` | Configured user-organization mapping | `organizationIds[]` |
| Text (email) | Substring `like` | `primaryEmail` |
| Text (display name) | NOT supported (too fuzzy) | — |

##### Implementation outline

```
Services/Ai/Membership/
  IMembershipResolverService.cs        — public contract
  MembershipResolverService.cs         — orchestrates discovery + identity resolution + FetchXML query
  IMembershipFieldDiscoveryService.cs  — metadata discovery + caching
  MembershipFieldDiscoveryService.cs   — implementation
  IIdentityNormalizationService.cs     — resolves systemuser → {contactId, email, teamIds, BU}
  IdentityNormalizationService.cs      — implementation (Redis cache)
  MembershipOptions.cs                 — appsettings binding class
  Models/MembershipDescriptor.cs       — runtime descriptor (field + role + identityType)
  Models/MembershipResponse.cs         — endpoint DTO
Api/Membership/
  MembershipEndpoints.cs               — endpoint group + auth
  MembershipAdminEndpoints.cs          — discovery report + cache refresh
```

#### Phase 1B — Playbook node executor (R3 implementation)

A new playbook node executor `LookupUserMembership` (proposed `ActionType = 52`) calls the BFF endpoint internally and binds the result into a template variable for downstream nodes:

```json
{
  "__actionType": 52,
  "entityType": "sprk_matter",
  "roles": ["owner", "assignedAttorney", "assignedParalegal"],
  "outputVariable": "myMatters"
}
```

Downstream Query nodes consume via a new Handlebars helper `{{joinIds arr}}` that produces a comma-separated list suitable for FetchXML's `operator='in'`:

```xml
<condition attribute="sprk_matter" operator="in" value="{{joinIds myMatters.ids}}"/>
```

#### Phase 1C — Migration of broken playbooks (R3 implementation)

After §1A + §1B land:
1. Update `notification-new-documents.json` to use a `LookupUserMembership` node for `sprk_matter` (replacing the broken `sprk_matterteammember` join).
2. Update `notification-new-emails.json` and `notification-new-events.json` similarly (audit needed).
3. Add integration test: run each notification playbook end-to-end in a controlled spaarkedev1 user fixture and assert `count > 0` against seeded data.

#### Phase 1D — Transitive membership (R3 design; partial implementation if budget allows)

`includeRelated=documents,events` query parameter returns memberships through related entities (e.g., documents on matters I'm on). Implementation: chained discovery + Dataverse joins. Design completed in R3; implementation deferred to R4 unless trivial after Phase 1A.

#### Phase 2 — Junction table `sprk_userentityassociation` (design only in R3; implementation deferred)

When Phase 1A's per-request FetchXML approach hits performance limits (target: monitor latency in production; trigger Phase 2 if p95 > 500ms for the endpoint), materialize a junction table behind the endpoint. **The endpoint contract is identical between Phase 1A and Phase 2** — consumers see no change (strangler fig).

**Schema** (`sprk_userentityassociation`):

| Field | Type | Purpose |
|---|---|---|
| `sprk_personid` | Uniqueidentifier | The person (resolved identity GUID) |
| `sprk_personidtype` | OptionSet (SystemUser=1, Contact=2, Team=3, BusinessUnit=4, Account=5, Organization=6) | Disambiguator |
| `sprk_entitylogicalname` | Text | E.g. "sprk_matter" |
| `sprk_entityrecordid` | Uniqueidentifier | Record GUID |
| `sprk_role` | Text | Discovered role name (e.g. "assignedAttorney") |
| `sprk_sourcefield` | Text | Provenance: which field provided the association |
| `sprk_lastsyncedon` | DateTime | For staleness audit |

**Synchronization** (TWO mechanisms — defense-in-depth):

- **(a) Event-driven via Service Bus**: BFF endpoints that mutate matter/document/event/etc. lookups publish a lightweight `MembershipChangedEvent`; a background handler upserts/removes junction rows. This is the primary write path.
- **(b) Nightly reconciliation**: scheduled `MembershipReconciliationJob` runs through Spaarke.Scheduling framework (Part 2). Catches drift from mutations that bypass BFF (e.g., direct Dataverse edits via maker UI). **Includes "Run Now" via admin endpoint per Q7.**

**Cache invalidation**: junction-row writes invalidate the corresponding `{userId, entityType}` cache entries via Redis pub/sub.

**Why Phase 2 is design-now, build-later**:
- Phase 1A's per-request approach is sufficient for current data volumes
- Implementing Phase 2 now adds a maintenance surface before there's evidence of need
- The endpoint contract is identical → consumers don't change
- ADR-034 commits to the eventual migration path so Phase 2 can ship without re-design
- The Spaarke.Scheduling framework (Part 2) makes Phase 2's reconciliation job trivial

#### Phase 3 — AI Search integration (design only in R3; implementation triggered by need)

If/when an existing AI Search index for an entity is augmented with semantic search, add an `associatedPersons[]` array field to each indexed document for combined semantic + membership filtering. This is essentially free if the index already exists; do NOT build a standalone "My X" search index. NOT a primary mechanism — too heavy for what's a relational membership query.

### Alternatives Considered (Part 1)

| Alternative | Why rejected |
|---|---|
| **Explicit enumeration per entity** (originally proposed) | Drift-prone (D5 root cause); verbose; maintenance burden when fields added |
| **Denormalized text column** `sprk_associatedpersonsindex` on each record, queried via `LIKE` | `LIKE` on text columns doesn't use indexes; maintenance burden (must rebuild on lookup change); identity heterogeneity unsolved; stale on contact rename/merge; text-field-size limits at scale |
| **Junction table FIRST** (Phase 2 only, skip Phase 1A) | Premature optimization; locks in derived data store before consumer count justifies it; harder to evolve schema mid-flight |
| **Dedicated AI Search index for "My X" only** | Wrong tool for the problem (membership, not search); duplicates source-of-truth; index maintenance overhead exceeds benefit at current scale |
| **Cosmos DB** as primary store | Premature at current data volumes; introduces new data store; Dataverse + BFF caching handles current load |
| **Dataverse calculated/rollup fields** | Calculated fields are per-row evaluation, not indexed; rollups are aggregations, not membership |
| **Reuse `sprk_fieldmappingprofile` + `sprk_fieldmappingrule`** | Different concept (record-to-record value copy vs user-record membership); schema fields don't fit. Architectural pattern (Profile+Rules) reusable IF we ever move config to Dataverse — design for that escape hatch via R4 if makers ask. |
| **Power Automate flows** to maintain junction table | Race conditions, throttling, harder to test, latency; user constraint: no plugins/flows for this category |

---

## Part 2 — Background-Job Infrastructure (`Spaarke.Scheduling`)

### Problem Statement

The BFF currently has **28 BackgroundService implementations** with NO shared framework:

| Category | Count | Pattern | Examples |
|---|---|---|---|
| Queue consumers (Service Bus driven) | 5 | `ServiceBusJobProcessor` consumes queue, dispatches to handlers | `ServiceBusJobProcessor`, `CommunicationJobProcessor`, `IndexingWorkerHostedService`, `UploadFinalizationWorker`, `ProfileSummaryWorker` |
| Periodic timers (scheduled) | 7 | `PeriodicTimer + IOptions<XOptions>` per service; no shared base | `ScheduledRagIndexingService`, `PlaybookSchedulerService`, `DemoExpirationService`, `DailySendCountResetService`, `InboundPollingBackupService`, `ManifestRefreshService`, `GraphSubscriptionManager` |
| One-shot startup | 5 | `IHostedService` runs once during host start | `StartupValidationService`, `CapabilityManifestInitializer`, `EmbeddingMigrationService`, `DocumentVectorBackfillService`, `PlaybookIndexingBackgroundService` |
| Domain workers | 11 | Mixed responsibilities | `TodoGenerationService`, `SpeDashboardSyncService`, `BulkOperationService`, `SessionFilesCleanupJob`, `RecordSyncJob`, etc. |

**Confirmed gaps**:
- ❌ No central `IScheduledJobRegistry`
- ❌ No admin endpoint to list / trigger / inspect any scheduled job
- ❌ Each service hardcodes enabled flag + interval in its own Options class
- ❌ No shared run-history audit
- ✅ `PlaybookSchedulerService` is the only one with Dataverse-driven schedule (in `sprk_analysisplaybook.sprk_configjson`)

**Existing `sprk_processingjob` entity** is scoped to Office document operations (JobTypes: DocumentSave / EmailSave / ShareLinks / QuickCreate / ProfileSummary / Indexing / DeepAnalysis). Tracks individual job RUN instances with stages/progress/idempotency/correlation. **NOT a fit for general scheduled-job tracking** — overloading it would couple all scheduled jobs to Office domain. R3 introduces a parallel `sprk_backgroundjobrun` for the new framework; both can coexist.

**Existing manual triggers for indexing** (`POST /api/ai/rag/index`, `/index/batch`, `/index-file`) are **per-document** manual triggers. They remain — they have legitimate per-record granularity. R3 ADDS a generic `/api/admin/jobs/{jobId}/trigger` for "run the full scheduled job NOW" (bulk semantics). Two different use cases, both retained.

### Proposed Solution

#### Library — `Spaarke.Scheduling`

Lives in `src/server/shared/Spaarke.Scheduling/` (NEW). Depends only on `Spaarke.Core`. Used by BFF and any future service.

**Contract**:

```csharp
public interface IScheduledJob
{
    string JobId { get; }                   // unique key, e.g., "membership-reconciliation"
    string DisplayName { get; }
    string Description { get; }
    Task<JobRunResult> ExecuteAsync(JobRunContext context, CancellationToken ct);
}

public record JobRunContext(
    Guid RunId,
    string CorrelationId,
    JobRunTrigger Trigger,           // Scheduled | ManualAdmin | OnStartup
    IDictionary<string, object> Parameters);

public record JobRunResult(
    bool Success,
    string? ErrorMessage,
    int? ProcessedItems,
    TimeSpan Duration);

public class ScheduledJobHost : BackgroundService
{
    // Reads sprk_backgroundjob rows on startup (and refreshes hourly)
    // For each enabled job, manages a per-job PeriodicTimer using cron schedule
    // (cron parsing via Cronos NuGet — D4)
    // Calls IScheduledJob.ExecuteAsync, records sprk_backgroundjobrun
    // Handles failure, retry (with backoff), idempotency
}
```

**Admin endpoints** (`/api/admin/jobs/*`, `RequireAuthorization("PlatformAdmin")`):

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/admin/jobs` | List all registered jobs + status (last run, next scheduled) |
| GET | `/api/admin/jobs/{jobId}/status` | Detailed status + last 10 runs |
| GET | `/api/admin/jobs/{jobId}/history?limit=50` | Run history |
| POST | `/api/admin/jobs/{jobId}/trigger` | Run job NOW (returns `{ runId, status, startedAt }`) |
| POST | `/api/admin/jobs/{jobId}/enable` | Enable scheduled execution |
| POST | `/api/admin/jobs/{jobId}/disable` | Disable scheduled execution (without removing) |

#### Dataverse entities (NEW)

**`sprk_backgroundjob`** (job definition):

| Field | Type | Purpose |
|---|---|---|
| `sprk_jobid` | Text (unique key) | E.g. "membership-reconciliation" |
| `sprk_displayname` | Text | Human-readable |
| `sprk_description` | Multiline | What the job does |
| `sprk_handlertype` | Text | Fully-qualified C# class name; resolved at startup |
| `sprk_enabled` | Boolean | Master enable/disable |
| `sprk_cronschedule` | Text | Standard cron (e.g., "0 2 * * *") |
| `sprk_configjson` | Multiline | Handler-specific config |
| `sprk_lastrunstartedon` | DateTime | (from latest run) |
| `sprk_lastruncompletedon` | DateTime | (from latest run) |
| `sprk_lastrunstatus` | OptionSet | Success / Failed / Running / Cancelled |
| `sprk_lastrunerror` | Multiline | Last error message |

**`sprk_backgroundjobrun`** (run instance):

| Field | Type | Purpose |
|---|---|---|
| `sprk_backgroundjob` | Lookup → sprk_backgroundjob | Parent definition |
| `sprk_runid` | Uniqueidentifier | Unique per run |
| `sprk_trigger` | OptionSet | Scheduled / ManualAdmin / OnStartup |
| `sprk_correlationid` | Text | For distributed tracing |
| `sprk_startedon` | DateTime | |
| `sprk_completedon` | DateTime | |
| `sprk_status` | OptionSet | Running / Success / Failed / Cancelled |
| `sprk_errormessage` | Multiline | |
| `sprk_processeditems` | Whole number | Optional metric |
| `sprk_resultjson` | Multiline | Handler-specific output |

#### Reference consumers (R3 ships TWO)

1. **`MembershipReconciliationJob`** (NEW — used by Part 1 Phase 2 design)
   - Validates `sprk_userentityassociation` table matches source-of-truth lookups
   - Schedules: nightly (default)
   - Until Phase 2 is implemented, this job is a no-op marker that proves the framework
2. **`PlaybookSchedulerService` migration** (REFACTOR existing) — per **D2** below
   - The scheduler becomes a single `sprk_backgroundjob` row ("notification-playbook-scheduler") that internally fans out across the 7 active playbooks (preserves current behavior 1:1)
   - Operators get "Run Now" for the whole scheduler (closes a longstanding gap — today operators wait up to 1 hour to test a playbook change)
   - Per-playbook "Run Now" deferred to a follow-up if operators need finer granularity
   - Existing `sprk_analysisplaybook.sprk_configjson` schedule fields are migrated; the playbook scheduler becomes a thin adapter on `Spaarke.Scheduling`

#### NOT in R3

- ❌ Migration of the other 26 BackgroundService implementations to the framework — they work today; touching them is risk. **Opportunistic migration over time** as those services get touched.
- ❌ The 5 queue-consumer services (`ServiceBusJobProcessor` family) — they have a different shape (event-driven, not schedule-driven). Out of scope for R3.

### Alternatives Considered (Part 2)

| Alternative | Why rejected |
|---|---|
| **Extend `sprk_processingjob` with new JobType values** | Overloads entity beyond Office domain intent; `DocumentId` lookup is meaningless for non-Office jobs; couples scheduled jobs to Office |
| **Treat scheduled jobs as playbooks** (use `PlaybookSchedulerService`) | Wrong abstraction — junction sync isn't an AI playbook; awkward for non-playbook jobs (cache warming, reconciliation, etc.) |
| **Hardcoded C# constants + appsettings.json** (current state) | What we're trying to upgrade; no maker control, no Run Now, no central registry |
| **Migrate all 28 services in R3** | High risk; some services have subtle behaviors; opportunistic migration is safer |
| **Hangfire / Quartz.NET / external scheduler** | Adds dependency; existing pattern (`BackgroundService + PeriodicTimer`) works fine; ADR-001 prefers in-process |

---

## Part 3 — Playbook Engine Hardening

### Problem Statement

Eleven known pitfalls in the JPS playbook engine + builder. Documented in [`docs/architecture/playbook-architecture.md` §Known Pitfalls](../../docs/architecture/playbook-architecture.md#known-pitfalls) but not enforced. Produce silent breakage modes.

| # | Pitfall | Severity | Detected in | Affects |
|---|---|---|---|---|
| **G1** | Handlebars `??` operator not supported; renders as literal text | **HIGH** (silent breakage) | R2 UAT 2026-06-20 | 2 of 7 active playbooks |
| **G2** | Renaming a node's `OutputVariable` silently breaks all downstream `{{x.output}}` references | **HIGH** (silent breakage) | R3 design review | All playbooks |
| **G3** | Condition node's `selectedBranch` only skips downstream nodes if explicit branch metadata is wired; without it, false branches execute | **MEDIUM** | Documented (doc pitfall #5) | Playbooks with conditional branching |
| **G4** | `CreateNotification` idempotency dedupes per UNREAD only; after user reads, duplicate notifications can be created | **LOW** (intentional, surprising) | R3 design review | All notification playbooks |
| **G6** | Canvas-to-Dataverse mapping drift between `playbookNodeSync.ts` (client) and `NodeService.cs` (server) causes silent fallthrough to `AIAnalysis` action | **HIGH** | Documented (doc pitfall #1) | Builder + executor |
| **G7** | `AiAnalysisNodeExecutor` is Singleton but `IToolHandlerRegistry` is Scoped — requires `IServiceProvider.CreateScope()` per execution; future executors that ignore this pattern fail at runtime | **MEDIUM** | Documented (doc pitfall #2) | Future node executors |
| **G8** | `sprk_searchindexed = true` means "enqueued," not "indexing completed" — misleading status | **MEDIUM** (operational confusion) | Documented (doc pitfall #3) | RAG indexing observability |
| **G9** | Unnecessary `DependsOn` edges between nodes force sequential execution, killing parallelism | **LOW** (perf only) | Documented (doc pitfall #4) | Playbook authoring |
| **G10** | Template variable resolution renders missing `{{x}}` as empty string; raw `{{x}}` substrings leak into output when upstream node failed/skipped | **MEDIUM** | Documented (doc pitfall #5) | All template-using nodes |
| **G11** | Scheduler activates new notification playbooks for ALL users immediately on deploy — no opt-in, no pilot rollout | **HIGH** (operational risk) | Documented (doc pitfall #6) | Any new notification playbook deploy |

(Numbering skips G5; G5 = `sprk_matterteammember`, addressed in Part 1.)

### Proposed Solution

R3 groups the eleven pitfalls into four work-streams.

#### Workstream H1 — Template engine + runtime detection (G1 + G10)

1. **Register `default` Handlebars helper** in [`TemplateEngine.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/TemplateEngine.cs):
   ```csharp
   _handlebars.RegisterHelper("default", (writer, ctx, args) =>
       writer.WriteSafeString(
           args.Length > 1 && !string.IsNullOrEmpty(args[0]?.ToString())
               ? args[0].ToString()
               : args.ElementAtOrDefault(1)?.ToString() ?? ""));
   ```
2. **Register `joinIds` helper** (per Q3 — supports Part 1B):
   ```csharp
   _handlebars.RegisterHelper("joinIds", (writer, ctx, args) =>
       writer.WriteSafeString(string.Join(",", (args[0] as IEnumerable<object>) ?? Array.Empty<object>())));
   ```
3. **Migrate 2 known-broken playbooks** from `{{X ?? 'Y'}}` to `{{default X 'Y'}}`
4. **Unit tests** for both helpers
5. **Runtime unrendered-template warning** ([`PlaybookOrchestrationService.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs)): after each node executes, if any string field in the output contains `{{`, log structured warning + emit `PlaybookStreamEvent` `unrendered-template-detected`

#### Workstream H2 — Builder validation + UI affordances (G2 + G3 + G9)

1. **OutputVariable rename guard** (builder UI): when edited, scan other nodes for `{{<oldName>.output*}}` references. If found, dialog: "(a) Auto-rename; (b) Keep old name; (c) Continue and break (advanced)." Default to (a).
2. **Branch wiring auto-generation** (builder UI): when an edge connects a Condition node to a downstream node, prompt for branch (true/false/both); persist in `DependsOn` branch metadata; visualize edges differently per branch.
3. **Edge perf hint** (builder UI): when an edge connects two nodes whose configs don't reference each other's `OutputVariable`, show non-blocking warning: "This edge forces sequential execution. Confirm or remove?"

#### Workstream H3 — Schema + DI hardening (G6 + G7 + G8)

1. **Canvas-server mapping drift test**: integration test in `tests/integration/PlaybookBuilder.Tests/` asserts every canvas type in [`playbookNodeSync.ts`](../../src/client/code-pages/PlaybookBuilder/src/services/playbookNodeSync.ts) has a corresponding entry in [`NodeService.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/NodeService.cs). Fails the build on drift.
2. **`sprk_searchindexed` rename + dual flag** (schema migration):
   - Rename `sprk_searchindexed` (bool) → `sprk_searchindexqueuedon` (datetime, set when job enqueued)
   - Add `sprk_searchindexcompletedon` (datetime, set when indexing completes per AI Search confirmation)
   - Migrate all consumers (UI tiles, queries, OData filters) to new fields
   - Update [`DeliverToIndexNodeExecutor.cs`](../../src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/DeliverToIndexNodeExecutor.cs) to write new fields
3. **Singleton/Scoped DI checklist**: new pattern doc `.claude/patterns/ai/node-executor-authoring.md` documenting `IServiceProvider.CreateScope()` requirement when Singleton executor depends on Scoped service. Existing `AiAnalysisNodeExecutor` remains worked example.

#### G4 — Doc-only (idempotency-on-unread)

Document explicitly in `docs/architecture/playbook-architecture.md` Known Pitfalls. ~5-min doc PR; included in R3 docs sweep.

#### G11 — DEFERRED to R4

Rollout-mode (Disabled / PilotUsers / AllUsers) for notification playbooks is a substantial schema migration + scheduler refactor + builder UI. With the Spaarke.Scheduling framework + Part 1 + Workstreams H1-H3, R3 is already substantial. Filed as separate **R4 project: `spaarke-playbook-rollout-mode-r4`**. ADR-035 deferred to R4.

### Alternatives Considered (Part 3)

| Alternative | Why rejected |
|---|---|
| **Replace Handlebars.NET with custom mini-engine that supports `??`** | High maintenance; security surface; Handlebars is the right primitive — add helpers, don't replace |
| **CI script to lint playbook configs for known broken syntax** | Useful additional check; included as a follow-up but doesn't fix runtime — the `default` helper is the primary fix |
| **Force re-author of all playbooks via builder UI** | Doesn't address future drift; the engine should be safe against author error |
| **Ship H4 rollout-mode in R3** | Scope risk; cleaner as R4 with its own ADR |

---

## Cross-Cutting Acceptance Criteria

### Part 1 (User-Record Membership)

- **AC-1A.1** Discovery finds all expected lookup fields on `sprk_matter` (owner, owningteam, owningbusinessunit, assignedattorney1/2, assignedparalegal1/2, assignedlawfirm1/2, assignedtointernal, assignedtoexternal). System fields (createdby etc.) excluded. Custom-entity lookups (e.g., `sprk_chartdefinition`) excluded.
- **AC-1A.2** `GET /api/admin/membership/discovered/sprk_matter` returns the expected descriptor list including source ("auto" or "override").
- **AC-1A.3** `GET /api/users/me/memberships/sprk_matter` returns expected IDs for a seeded test user with associations across all discovered fields.
- **AC-1A.4** Identity normalization correctly resolves a test user whose `systemuserid` and `contactid` exist as separate records linked by `azureactivedirectoryobjectid`. Both lookup-field types produce a match.
- **AC-1A.5** Endpoint response time (p95) under 300ms for a user with ≤500 memberships on a 50K-row entity (measured against `spaarkedev1`).
- **AC-1A.6** Endpoint cache hit ratio ≥90% under steady-state load.
- **AC-1A.7** Metadata cache invalidates correctly on `POST /api/admin/membership/refresh-metadata`.
- **AC-1B.1** `LookupUserMembership` playbook node (action 52) executes end-to-end and writes resolved IDs to its `OutputVariable`.
- **AC-1B.2** `joinIds` Handlebars helper produces correct comma-separated lists for FetchXML `IN` clauses.
- **AC-1C.1** `notification-new-documents.json` migrated to use the new node + produces non-zero notifications for a seeded test fixture.
- **AC-1C.2** Any other playbook using broken `sprk_matterteammember` join similarly migrated.
- **AC-1.ADR** **ADR-034** "User-record membership resolution pattern" merged into `.claude/adr/` and `docs/adr/`. Documents the contract, identity normalization, discovery model, Phase 2 deferred design.
- **AC-1.Docs** `docs/architecture/` has new page describing the pattern with code-entry pointers + naming-collision disambiguation from existing `AssociationResolver` PCF.
- **AC-1.Phase2** Phase 2 (junction table) design in design.md is complete enough that R4 can implement without re-design.

### Part 2 (Background-Job Infrastructure)

- **AC-2.1** `Spaarke.Scheduling` library compiles + has unit tests for `ScheduledJobHost`, cron schedule parsing, run-history recording.
- **AC-2.2** `sprk_backgroundjob` + `sprk_backgroundjobrun` entities deployed; migrations work in dev/test environments.
- **AC-2.3** `MembershipReconciliationJob` registered + visible in `GET /api/admin/jobs` + triggerable via `POST /api/admin/jobs/membership-reconciliation/trigger` (no-op success in R3; full implementation in Phase 2 / R4).
- **AC-2.4** `PlaybookSchedulerService` migrated to `Spaarke.Scheduling`. All 7 notification playbooks visible in admin endpoint + triggerable on demand. Existing scheduled behavior preserved (operators see same notification cadence).
- **AC-2.5** Admin endpoints behind `RequireAuthorization("PlatformAdmin")` — non-admin users receive 403.
- **AC-2.6** Job runs recorded in `sprk_backgroundjobrun` with correlation ID, trigger source (Scheduled / ManualAdmin), status, duration.
- **AC-2.7** Failed jobs surface in `GET /api/admin/jobs/{jobId}/status` with last error.
- **AC-2.ADR** **ADR-036** "Background-job infrastructure" merged.

### Part 3 (Playbook Engine Hardening)

- **AC-H1.1** `{{default X 'Y'}}` + `{{joinIds arr}}` Handlebars helpers registered + 2 broken playbooks migrated + unit tests in place.
- **AC-H1.2** Unrendered `{{` runtime warning logs structured event + appears in playbook stream telemetry.
- **AC-H2.1** Builder UI prevents silent OutputVariable rename breakage.
- **AC-H2.2** Builder UI guides authors to specify branch on Condition→downstream edges.
- **AC-H2.3** Builder UI flags non-referencing edges as perf-impacting (advisory only).
- **AC-H3.1** Canvas-server mapping drift test passes in CI; fails build on drift.
- **AC-H3.2** `sprk_searchindexqueuedon` + `sprk_searchindexcompletedon` schema migration deployed; all consumers updated.
- **AC-H3.3** `.claude/patterns/ai/node-executor-authoring.md` published.
- **AC-Docs** `docs/architecture/playbook-architecture.md` Known Pitfalls section refreshed.

### Cross-cutting

- **AC-X.1** No new HIGH-severity CVE (`dotnet list package --vulnerable --include-transitive`).
- **AC-X.2** BFF publish-size delta within ≤+1 MB per BFF-touching task; cumulative ≤60 MB.
- **AC-X.3** Test coverage: every new BFF service has unit tests; integration tests exist for the endpoint + playbook node + reference scheduled job.
- **AC-X.4** Data-model docs (`docs/data-model/sprk_matter-related-tables.md`) updated to reflect the actual `sprk_matter` columns (Assigned Attorney/Paralegal/LawFirm 1/2, AssignedToInternal/External). Doc-drift finding from this design surfaced and resolved.

---

## Scope Boundaries

### In scope (R3)

- **Part 1** Phases 1A, 1B, 1C (discovery-based resolver + node executor + migration)
- **Part 1** Phase 1D (transitive memberships) design-complete; implementation if budget allows
- **Part 1** Phase 2 (junction table) **design only**
- **Part 1** Phase 3 (AI Search integration) **design only**
- **ADR-034** ("User-record membership resolution pattern")
- **Part 2** `Spaarke.Scheduling` library + admin endpoints + entities + TWO reference consumers
- **ADR-036** ("Background-job infrastructure")
- **Part 3** Workstreams H1, H2, H3 + G4 doc update
- Doc updates: `playbook-architecture.md` Known Pitfalls; new pattern doc for node-executor authoring; data-model docs refresh for `sprk_matter` columns
- Integration tests for the endpoint + node executor + each migrated playbook + scheduled job framework

### Out of scope (deferred)

- R2.2 hotfix UX work (TL;DR bullets, due dates in bullets, AddToTodo improvements) — ships independently on current R2 hotfix branch
- Phase 2 junction-table IMPLEMENTATION (design-only in R3; build in R4 if perf demands or via the `MembershipReconciliationJob` evolution)
- Phase 3 AI Search integration (design-only in R3; build when consumer entity gets a search index)
- Identity matching for free-text display-name fields (explicitly NOT supported)
- Migration of consumer UI surfaces (Dashboard tiles, list filters, etc.) to use the new endpoint — separate adoption project
- Migration of remaining 26 BackgroundService implementations to `Spaarke.Scheduling` — opportunistic over time
- Migration of queue-consumer services (`ServiceBusJobProcessor` family) — different shape, out of scope
- **G11 rollout-mode** → new R4 project (`spaarke-playbook-rollout-mode-r4`) with ADR-035
- Producer-layer playbook additions (new notification categories, new entity sources)
- Dataverse-stored membership config (`sprk_membershipconfig` entity) — appsettings.json sufficient at R3 scale; revisit in R4 if makers ask for runtime control

### Explicitly NOT changing

- Daily Briefing widget / standalone code page UX surface (R2 / R2.2 territory)
- BFF `/narrate` endpoint contract
- `appnotification` schema
- `sprk_processingjob` schema (kept Office-scoped)
- Pattern D dual-use conventions (per ADR-012)
- Existing `POST /api/ai/rag/index*` per-document manual triggers (retained alongside new `/api/admin/jobs/*` framework)
- Existing FieldMapping framework (`sprk_fieldmappingprofile/rule` + `AssociationResolver` PCF) — different concept, name disambiguated via "Membership" terminology

---

## Implementation Phases

Suggested ordering for `task-create`:

| Phase | Workstream | Sequencing | Estimated tasks |
|---|---|---|---|
| **P1** | Part 3 H1 — Template engine + runtime detection (G1 + G10) | Earliest; small, unblocks playbook migration | 3–5 |
| **P2** | Part 2 — `Spaarke.Scheduling` library + entities + ADR-036 | Early; needed for Membership reconciliation + playbook scheduler migration | 6–8 |
| **P3** | Part 2 — Admin endpoints + `PlaybookSchedulerService` migration (reference consumer) | After P2 | 4–6 |
| **P4** | Part 1A — `MembershipResolverService` discovery + identity normalization + endpoint + ADR-034 | After P1; independent of P2/P3 | 6–8 |
| **P5** | Part 1B — `LookupUserMembership` node executor | After P4 (consumes the endpoint) | 2–3 |
| **P6** | Part 1C — Playbook migration + integration tests | After P5 | 2–3 |
| **P7** | Part 3 H3 — Schema + DI hardening (G6 + G7 + G8) | Parallel to P4-P6 | 4–6 |
| **P8** | Part 3 H2 — Builder validation + UI affordances (G2 + G3 + G9) | After P7 (uses mapping-drift test infra) | 4–6 |
| **P9** | Docs sweep + data-model doc refresh + Phase 2/3 design write-up + G4 doc note | Last; depends on all above | 3–5 |
| **P10** | Wrap-up: lessons-learned, code-review, adr-check | Last | 1 |

**Estimated total: 35–51 tasks.** Manageable for R3 without H4 (rollout-mode, deferred to R4).

---

## Risks + Open Questions

### Risks

| Risk | Mitigation |
|---|---|
| **Discovery surprise** — auto-discovery includes a field the team didn't realize was a person lookup | Discovery Report endpoint (`/api/admin/membership/discovered/{entityType}`) exposes what was discovered; operators audit + add overrides as needed. Integration test seeds known fixtures + asserts discovered descriptors match expectations. |
| **Identity normalization edge cases** — users without contact, contacts without systemuser, users with multiple email aliases | Explicit in contract: each identity type resolved independently; consumers see all matches across all identity types. External-email matches are separate code path (substring `like`, not GUID lookup). Documented in ADR-034. |
| **FetchXML OR with 10+ conditions on large table** — performance degradation on 100K+ row entities | Phase 1A monitoring (AC-1A.5 p95 target); Phase 2 junction table is the escape hatch. |
| **Metadata cache staleness** after entity schema change | 1-hour TTL by default + manual refresh endpoint (`POST /api/admin/membership/refresh-metadata`). Documented in ADR-034. |
| **Cache invalidation correctness** — stale membership cache after user updates a Matter's Assigned Attorney | Phase 1A: short TTL (5 min, no write invalidation, accept staleness). Phase 2: junction writes invalidate via Redis pub/sub. |
| **Builder UI changes regress existing playbooks** — H2 touches canvas + properties dialog | Comprehensive snapshot tests in Builder; feature flag for new validators if needed |
| **Scheduling framework regression to existing `PlaybookSchedulerService`** — migration could change cadence or behavior | Migration preserves existing schedule semantics; staging soak test before prod cutover; rollback = revert single PR |
| **Naming collision with existing `AssociationResolver` PCF** | Disambiguated via "Membership" terminology + explicit Naming Collision Register in design.md and ADR-034 |

### Decisions Resolved During Design (2026-06-20)

1. **D1**: `MembershipReconciliationJob` ships in R3 as a **no-op marker** — registered in the framework, visible in `/api/admin/jobs`, triggerable on demand (proves the framework end-to-end). Real reconciliation logic ships with Phase 2 junction-table implementation (R4 or later).
2. **D2**: When migrating `PlaybookSchedulerService` to `Spaarke.Scheduling`, the scheduler becomes **a single `sprk_backgroundjob` row** ("notification-playbook-scheduler") that internally fans out across the 7 active playbooks. Preserves current behavior 1:1. Per-playbook rows (and per-playbook "Run Now") are deferred — operators can trigger the whole scheduler now and get all 7 playbooks; per-playbook triggers will be revisited if needed.
3. **D3**: Phase 2 event-driven sync transport (Service Bus topics-per-consumer vs single queue) is **deferred to Phase 2 implementation**. R3 design.md documents the pattern; the specific transport decision lives with the Phase 2 PR so it can be informed by then-current load and consumer count.
4. **D4**: Cron parsing uses the **`Cronos` NuGet package** (mature, ~50KB, MIT-licensed). Allows full cron expression syntax for `sprk_cronschedule`. Avoids reinventing parsing; avoids constraining `sprk_cronschedule` to a small vocabulary that would limit future use.

---

## Resources

### Applicable ADRs (existing)

- **ADR-001** — Minimal API + BackgroundService (Spaarke.Scheduling stays in-process per ADR-001; no Azure Functions)
- **ADR-008** — Endpoint-filter auth (Membership + Admin endpoints follow convention)
- **ADR-010** — DI minimalism (new services injected as concretes; `IScheduledJob` interface allowed as testing seam)
- **ADR-012** — Shared component library (`Spaarke.Scheduling` is a new shared .NET library)
- **ADR-013** — AI architecture (`LookupUserMembership` node executor extends existing framework)
- **ADR-016** — Rate-limit handling (membership endpoint cache + retry pattern)
- **ADR-028** — Spaarke Auth v2 (endpoint uses OBO; identity resolution uses standard `@spaarke/auth` contract)
- **ADR-024** — sprk_todo 11-entity regarding (informs identity normalization patterns)

### New ADRs (R3 deliverable)

- **ADR-034** — "User-record membership resolution pattern" (Part 1)
- **ADR-036** — "Background-job infrastructure (Spaarke.Scheduling)" (Part 2)
- (ADR-035 deferred to R4 — playbook rollout-mode)

### Binding constraints

- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — Sections A, F, F.1, F.2, F.3 (binding for all BFF-touching tasks)
- [`.claude/patterns/api/endpoint-definition.md`](../../.claude/patterns/api/endpoint-definition.md)
- [`.claude/patterns/dataverse/web-api-client.md`](../../.claude/patterns/dataverse/web-api-client.md)
- [`docs/architecture/playbook-architecture.md`](../../docs/architecture/playbook-architecture.md)

### Naming collision register

| Existing concept | New R3 concept |
|---|---|
| `src/client/pcf/AssociationResolver/` — PCF for record-to-record FieldMapping (copy values when Regarding set) | `src/server/api/Sprk.Bff.Api/Services/Ai/Membership/MembershipResolverService.cs` — BFF service for user-record membership resolution |
| `sprk_fieldmappingprofile` / `sprk_fieldmappingrule` — Dataverse entities for field-copy rules | `Membership` appsettings.json section + (potentially R4) `sprk_membershipconfig` if Dataverse-stored config needed |
| `sprk_processingjob` — job-instance tracker scoped to Office document operations | `sprk_backgroundjob` (definition) + `sprk_backgroundjobrun` (instance) — scheduled-job framework, distinct from Office processing |
| `PlaybookSchedulerService` — AI playbook scheduler (specific) | `ScheduledJobHost` (Spaarke.Scheduling — generic) — `PlaybookSchedulerService` becomes a consumer of this |
| `POST /api/ai/rag/index*` — per-document manual indexing triggers (existing) | `POST /api/admin/jobs/{jobId}/trigger` — bulk job manual triggers (new, additive) |

### Predecessor evidence

- [`projects/spaarke-daily-update-service-r2/current-task.md`](../spaarke-daily-update-service-r2/current-task.md) — R2 checkpoint state
- UAT findings from this design session (2026-06-19 → 2026-06-20)
- Maker portal screenshot of `sprk_matter` Columns view (verifies actual schema vs documented schema)
- Code audit: 28 existing BackgroundService implementations identified by Grep 2026-06-20

---

*Design v2 — reflects architectural sharpening from 2026-06-20 session: discovery-based membership, Spaarke.Scheduling framework, naming disambiguation from existing AssociationResolver PCF. After approval, run `project-pipeline` to scaffold README + plan + CLAUDE.md + tasks.*
