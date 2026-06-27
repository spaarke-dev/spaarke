# Audit Item 02 — Redis cache for chat tool list resolution (DEFERRED to R7)

**Status**: Deferred per user decision 2026-06-07.
**Origin**: Surfaced during R6 audit when task 011 (`SprkChatAgentFactory.ResolveTools()` wired to data-driven `sprk_analysistool` rows) was reviewed.

## What was deferred

`SprkChatAgentFactory.ResolveTools()` enumerates `sprk_analysistool` rows from Dataverse **at every chat-session start** (per-session query, not cached). Task 011's notes flagged this and documented a canonical key shape (`r6:chat-tools:{tenantId}`) for a future implementer, but explicitly deferred the cache work with the rationale *"don't over-engineer."*

## Why deferred (decision rule)

User decision rule: **measurement-first, optimize-after**.

Without traffic data, we cannot quantify whether the per-session Dataverse query is a real performance concern. Adding a cache pre-emptively introduces:
- Cache key + invalidation complexity (when an admin edits a row, all sessions must pick up the change within an acceptable lag)
- Per-tenant cache memory budget
- Test surface for cache-hit + cache-miss + cache-stale paths
- Operational tooling for cache flush (e.g., when a tool row is published/unpublished)

Premature optimization without baseline measurement = technical debt with no payoff signal.

## What to measure before re-evaluating

When R6 ships to a tenant with high chat-session volume, capture:
- p50 + p95 latency of `ResolveTools()` call (instrument from `SprkChatAgentFactory.CreateAgentAsync`)
- Dataverse query count per chat-session start
- Total Dataverse RU (or equivalent) consumed by tool-list resolution per tenant per day
- Chat-session start latency contribution from `ResolveTools()` vs other factors (persona resolution, knowledge scope, etc.)

If p95 of `ResolveTools()` exceeds an SLO threshold (e.g., 250 ms) OR Dataverse RU consumption on chat tool queries becomes a meaningful budget item, re-evaluate caching.

## What would the implementation look like (when justified)

Reference design — Redis hot tier with admin-edit invalidation:

- **Key**: `r6:chat-tools:{tenantId}` (per task 011's existing notes)
- **Value**: serialized list of `(toolCode, handlerClass, jsonSchema, configuration, availableInContexts)` records
- **TTL**: 5-10 minutes (balances cache hit rate vs staleness on admin edits)
- **Invalidation**: when an admin updates `sprk_analysistool` (via Power Apps form or Web API PATCH), publish an invalidation event to a Redis channel; chat factory subscribes and purges affected tenant keys. Pre-fill flow's existing cache invalidation pattern (R2 / R3 work) is the reference.
- **Fallback**: cache miss → Dataverse query → populate cache → return. Failures non-fatal.

Engineering estimate: ~3-4 days including invalidation wiring + tests.

## R7 backlog entry

When R7 planning begins, this item should be considered alongside other measured-after-R6-ships optimizations. Owner judgment at planning time on whether to lift this into R7 scope or further defer.

## Why this matters for the "ADRs Are Defaults" principle

This item is an example of a deferral being made **correctly**: there's a real architectural concern (per-session Dataverse query), but absent measurement we don't know whether the optimal answer is "add cache" or "leave as-is, the query is fine at scale." The right operating mode is: measure, then optimize. Adding a cache without a signal would be the same pattern as the silently-deferred items 1/3/4 — making a trade-off without surfacing it. Here we're explicitly choosing to surface the deferral with a measurement criterion attached.
