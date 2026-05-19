# Dataverse → Azure AI Search sync patterns (2026-05)

> **Status**: Researched 2026-05-19 for `projects/ai-spaarke-insights-engine-r1/`.
> **Curator**: researcher subagent (auto-research, not yet senior-engineer-reviewed).
> **Refresh cadence**: monthly per [`knowledge/REFRESH-PROCEDURE.md`](../REFRESH-PROCEDURE.md).

The Insights Engine needs to project Dataverse changes (matters, parties, decisions, outcomes) into Azure AI Search (for vector + hybrid retrieval) and Cosmos Gremlin (for the graph layer). There is no built-in Microsoft connector for this exact projection: Azure AI Search has no Dataverse-native indexer (see "Gaps" below), and Microsoft 365 Copilot connectors (formerly Graph connectors) project into a *different* search surface (Microsoft Search / Copilot search) not our application's AI Search index. This means custom event-driven sync is the right architecture.

---

## Status as of 2026-05

There is **no first-party Dataverse → Azure AI Search indexer**. AI Search has built-in pull-mode indexers for Azure Blob, ADLS Gen2, Azure Table Storage, Cosmos DB NoSQL, Azure SQL, OneLake, and a (still-preview) SharePoint Online indexer — but not Dataverse. The supported event-driven path is: Dataverse plug-in or webhook → Service Bus or Function HTTP trigger → custom code that writes to AI Search using `Azure.Search.Documents`. **Microsoft 365 Copilot connectors** (formerly Graph connectors) sync content *into Microsoft Graph for Copilot/Microsoft Search* — useful for surfacing Dataverse rows in M365 Copilot, but they don't write to your own AI Search index, so they're orthogonal to the Insights Engine.

## Key URLs consulted (2026-05-19)

- [Use Webhooks to Create External Handlers for Server Events](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/use-webhooks) — last updated 2026-04-01
- [Azure Service Bus Integration for Dataverse](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/azure-integration) — last updated 2026-04-01
- [Microsoft 365 Copilot connectors overview](https://learn.microsoft.com/en-us/microsoftsearch/connectors-overview)
- [Microsoft Search API in Microsoft Graph overview](https://learn.microsoft.com/en-us/graph/search-concept-overview)
- [Azure AI Search Data Sources Gallery](https://learn.microsoft.com/en-us/azure/search/search-data-sources-gallery)
- [Microsoft Graph search connector sample](https://github.com/microsoftgraph/msgraph-search-connector-sample)
- [Dataverse change tracking](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/use-change-tracking-synchronize-data-external-systems) (consulted but not deep-fetched; standard pattern)

## Findings

### 1. Dataverse capabilities for sync triggering

| Mechanism | Latency | Reliability | Throughput | Best for |
|---|---|---|---|---|
| **Plug-in (in-process, sync or async)** | Real-time (sync) or seconds (async) | High (transactional with the originating operation) | Subject to plug-in sandbox limits | Custom pre/post-event logic; not the recommended primary path for sync (couples Dataverse to downstream) |
| **Webhook** | Real-time, 60s timeout | Best-effort, 2xx required; on 502/503/504, one retry; otherwise message lost | Limited by the receiving service throughput | Simple direct-to-Function sync, single subscriber |
| **Service Bus (via plug-in registration → service endpoint)** | Real-time write to bus, then consumer-paced | High; persistent queue/topic, dead-lettering, async-retry by Dataverse | Excellent; multi-subscriber fan-out via topic | **Recommended** for the Insights Engine — durable, multi-subscriber, observable |
| **Event Hubs (via plug-in registration)** | Real-time | High | Highest (designed for streaming) | Overkill for matter-update-rate workloads; use SB topic |
| **Power Automate flow** | Seconds-to-minutes | Medium (depends on flow) | Limited by flow API limits | Citizen-dev integrations; not appropriate for production sync |
| **Change tracking + scheduled pull** | Minutes (depends on schedule) | High (deterministic; resumable via change tokens) | Bounded by pull rate | **Recommended** as the backfill / reconciliation path alongside the real-time stream |

The combination Spaarke wants is **Service Bus topic (real-time) + scheduled change-tracking pull (reconciliation)**. This is the canonical "event-driven plus periodic reconcile" pattern. Real-time keeps the index fresh; the scheduled pull recovers from any dropped events.

### 2. Microsoft Graph Connectors vs. custom Function-based sync

**They're solving different problems.** Graph connectors (now branded *Microsoft 365 Copilot connectors*) index content into Microsoft Graph so it shows up in:
- Microsoft Search (the m365.cloud.microsoft search surface).
- Microsoft 365 Copilot's grounding (without you having to build retrieval).
- BizChat answers.

Connectors do *not* write to your application's Azure AI Search index. There's no "send these indexed items to my AI Search service" pipeline.

| Capability | Graph Connectors | Custom Function-based sync |
|---|---|---|
| **Target index** | Microsoft Graph / M365 Copilot grounding | Your Azure AI Search index |
| **Built-in Dataverse source** | No (would need a custom connector via Graph connector API) | N/A — you write the source code |
| **Application-side retrieval** | Via Microsoft Search API or BizChat — no SQL-like query control | Via `Azure.Search.Documents` — full filter/vector/semantic control |
| **ACL / security trimming** | Inherits via M365 entitlements + per-item ACL fields | You control entirely (your filter / your fields) |
| **Time-to-deploy for Dataverse** | High (custom connector dev) | Medium (BFF/Function dev) |

**For the Insights Engine, only the custom Function-based path applies.** Graph connectors might later be useful for surfacing matter info in user-facing Copilot experiences (separate workstream), but they don't replace the Insights Engine's own retrieval index.

### 3. Idempotent indexing patterns

The sync must tolerate duplicate events without producing duplicate index entries. Three patterns:

1. **Deterministic document ID from Dataverse record ID.**
   ```csharp
   var docId = $"{tenantId}_{entityLogicalName}_{recordId}";
   ```
   AI Search uses `id` as the primary key. `MergeOrUpload` action (instead of `Upload`) is then idempotent — repeated events for the same record overwrite the existing document.

2. **Use Dataverse `modifiedon` as the version token.** When the sync handler dequeues an event, it (a) fetches the current Dataverse record (b) compares `modifiedon` to the indexed document's `modifiedon` (c) skips if indexed version ≥ event version. This prevents out-of-order events from regressing the index.

3. **Compensating delete on cascade.** When a record is deleted in Dataverse, you receive a Delete event. The handler issues `DeleteDocuments` on AI Search and a graph removal on Cosmos. For *cascaded* deletes (parent removed → children should go), do the cascade at the projection layer, not by trusting Dataverse to fire Delete events for every child (it may not, depending on cascade behavior).

### 4. Schema evolution

Dataverse schemas evolve (new columns added, option sets extended, columns renamed). The Insights Engine sync must handle this without breaking.

Patterns:

- **Project schema, don't mirror it.** Map only the Dataverse columns that the Insights Engine cares about into the AI Search index. New columns appear as "ignored at projection time" until you add a mapping. This decouples downstream schema from Dataverse schema churn.
- **Add new AI Search fields with `nullable: true`.** New fields don't require reindex of existing documents — they're just null where absent.
- **Renamed Dataverse columns**: the sync handler reads from `attributes[oldName]` if present, else `attributes[newName]`. Keep this back-compat for one release cycle.
- **Option set value changes**: if a Dataverse option set value's *label* changes, the projection (which usually stores the label) needs a backfill. Run the scheduled reconcile pull to refresh.
- **For breaking schema changes**: spin up a new AI Search index version (`insights-observations-v2`), run a parallel backfill, atomically swap the BFF's query alias, then drop v1. This is the "index versioning" pattern. AI Search has no native alias, so implement the alias as a config setting in BFF that names the active index.

### 5. Backfill strategies

Two flavors required:

- **Initial population** (new tenant onboarding, or first deployment of Insights Engine for an existing tenant): a one-shot orchestration that pages through all relevant Dataverse tables via the Dataverse Web API with `$skiptoken` pagination, projects each record, and writes batches of 1,000 to AI Search via the bulk upload action. Use a separate Function (or Durable Function orchestrator) for this — different scale characteristics than the real-time sync.
- **Reconciliation pull** (periodic, recovery from dropped events): use Dataverse **change tracking** (entity-level setting; once enabled, the Web API supports `Prefer: odata.track-changes` with a continuation token). Run a Timer-triggered Function every 15 minutes that pulls the changeset since the last token, applies the same projection logic as the real-time path, and persists the new token. Idempotent by design (same dedupe rules as section 3).

Caveats:
- Change tracking must be enabled on each Dataverse table you care about. It's an environment-level admin action.
- Change tracking tokens expire after a few hours of inactivity — your reconcile loop must run frequently enough to keep the token live.
- Initial backfill and reconcile share the same projection code. Don't fork; they must produce identical AI Search documents for the same Dataverse input.

## Implications for Spaarke Insights Engine

1. **Service Bus topic as the real-time backbone.** Dataverse plug-in registration → service endpoint → topic → Function consumer per tenant. Survives Function outages, allows multi-subscriber fan-out later.
2. **Scheduled reconcile Function (Timer trigger, 15-min cadence) using Dataverse change tracking.** Recovers from any dropped events. Idempotent through deterministic doc IDs + `modifiedon` version comparison.
3. **Initial backfill = Durable Function orchestrator.** Page-and-project the entire Dataverse change-tracked table set on tenant onboarding. Share the projection layer with real-time and reconcile.
4. **Don't use Microsoft 365 Copilot connectors for this.** They target a different index and don't satisfy the application's retrieval needs.
5. **Webhook payload is a trigger, not a source of truth.** The handler always re-fetches the current Dataverse record before projecting. Webhook events can be truncated past 256 KB and dropping properties; webhooks can also arrive out of order under retry. Re-fetch eliminates both classes of bug.
6. **Index schema versioning is a BFF-config concern.** When we need a breaking schema change, build the new index, backfill in parallel, atomic-swap via config setting, drop the old index. Plan this in the design.

## Open questions

- **Plug-in vs. webhook vs. Service Bus from Dataverse — which is the lowest-friction trigger?** Service Bus integration requires the Plug-in Registration Tool to register a service endpoint; webhooks are slightly simpler. Worth a short spike to confirm the SB registration works against the target Dataverse environment.
- **Throughput characteristics of Dataverse Web API for backfill.** Pagination throughput varies by table size and environment SKU. Worth a measured spike on the largest expected matter table before committing to a Phase 1 timeline.
- **Service Bus with Microsoft Entra-based auth from Dataverse** — supported in 2026 but newer than SAS. Should we use Entra-based auth (preferred for credential rotation) or SAS (more battle-tested)?
- **Whether change tracking covers all Spaarke entity types** — including `sprk_*` custom entities. Custom entities support change tracking, but it must be enabled per-entity.
- **What's the worst-case sync latency the Insights Engine can tolerate?** If <5 seconds, real-time-only with no batching. If <30 seconds, micro-batched writes to AI Search are an option (better cost, slightly higher latency). Need a Phase 0 spec answer.
