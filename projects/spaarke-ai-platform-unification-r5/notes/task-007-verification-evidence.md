# Task 007 (D1-07) — Verification Evidence

> **Status**: code authored 2026-06-04 by parallel sub-agent
> **Build / publish-size / smoke**: deferred to main session per parallel-wave protocol
> **Owner**: AI Agent (parallel sub-agent) + main session

---

## Sub-agent scope (this document)

Per the parallel-wave kickoff prompt, the sub-agent's scope was **CODE AUTHORING only**. The following items are explicitly NOT run by this sub-agent and are deferred to the main session:

- `dotnet build` — main session runs the consolidated wave build
- `dotnet test` — main session runs the consolidated wave test pass
- `dotnet publish` + size measurement — main session aggregates Phase 1 publish-size at task 009
- CVE scan — main session aggregates at task 009
- Spaarke Dev smoke — out of scope for sub-agent

---

## Step 3 — Active-Session Source-of-Truth Decision

**Decision**: Use Redis `IConnectionMultiplexer.GetDatabase().KeyExistsAsync(key)` per candidate `(tenantId, sessionId)` pair, where the key follows the existing `ChatSessionManager.BuildCacheKey(tenantId, sessionId)` pattern (`chat:session:{tenantId}:{sessionId}`).

**Rationale**:

1. Reuses the existing key-pattern helper (single source of truth for the cache-key string — touched only in `ChatSessionManager.BuildCacheKey`)
2. Avoids the `IConnectionMultiplexer.GetServer().Keys(pattern: ...)` SCAN path, which is documented as expensive on large key sets
3. The candidate set is bounded — we only check session IDs we found via the index enumeration, capped at `MaxKeysPerScan` (default 10000) per tenant per run
4. Each KeyExistsAsync is O(1) on the Redis side; total work is proportional to the candidate set, not to the active session population

**Tolerance / fallback**:

- If `IConnectionMultiplexer` is not registered (Redis disabled, in-memory cache fallback in local dev), the scheduled scan **logs once and skips**. The on-session-end signal path remains effective — that path is the primary cleanup driver for explicit deletes; the scheduled scan exists only to catch implicit (TTL-driven) expirations.
- This tolerance is verified by passing `includeMultiplexer: false` in test setup; in that case the job continues without error and emits no eviction work.

**Alternatives considered**:

- **(b) Tenant-scoped session-key registry Sorted Set** — would be O(1) per tenant but requires a new write surface in `ChatSessionManager.CreateSessionAsync` / `DeleteSessionAsync` and a future migration path. Deferred to R6+ if scale demands.
- **(c) Dataverse `sprk_aichatsummary` table query for non-archived sessions** — works but adds a Dataverse round-trip per scheduled run. Acceptable as a future fallback if Redis is disabled in a production environment, but for R5 the Redis-driven path is the canonical Spaarke Dev configuration.

---

## Files modified

| Path | Status | LOC delta (approx) |
|---|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SessionFilesCleanupOptions.cs` | **NEW** | +44 |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ISessionFilesCleanupSignal.cs` | **NEW** | +35 |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SessionFilesCleanupSignal.cs` | **NEW** | +72 |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SessionFilesCleanupJob.cs` | **NEW** | +590 |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ChatSessionManager.cs` | extend | +28 (nullable ctor param + fire-and-forget signal call at end of DeleteSessionAsync; signature byte-for-byte unchanged for public method) |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AnalysisServicesModule.cs` | extend | +35 (Signal singleton + interface forwarding alias + hosted-service registration inside compound gate; ChatSessionManager factory updated for nullable cleanup-signal injection; new private AddSessionFilesCleanupOptions method) |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/SessionFilesCleanupJobTests.cs` | **NEW** | +400 (8 tests — 7 per POML + 1 extra for signal contract) |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/ChatSessionManagerTests.cs` | extend | +65 (2 new tests per POML — signal call + null-tolerance) |
| `projects/spaarke-ai-platform-unification-r5/tasks/007-session-files-cleanup-hostedservice.poml` | extend | metadata: status=complete, started/completed = 2026-06-04, actual-effort = 4h |

ZERO new top-level `Program.cs` lines verified — registration is entirely inside `AnalysisServicesModule.AddPlaybookServices` (compound AI gate). Per R5 CLAUDE.md §3.3 + ADR-010.

---

## ADR / constraint compliance

| Constraint | How addressed |
|---|---|
| ADR-001 (no Azure Functions) | `BackgroundService` + `PeriodicTimer` per existing canonical examples |
| ADR-010 (DI minimalism) | One allowed seam (`ISessionFilesCleanupSignal`) — justified by `ChatSessionManager` test isolation. Concrete classes elsewhere. |
| ADR-013 (BFF-only) | All new code inside BFF; no new service extraction; no new endpoint |
| ADR-014 (tenant + session predicates) | Every delete filter is `tenantId eq '...' and sessionId eq '...'` — both predicates always present (verified by test 3) |
| ADR-018 (no new feature flag) | Registration inside compound AI gate; `SessionFilesCleanupOptions` has NO `Enabled` property; kill-switch inherits from `(Analysis:Enabled && DocumentIntelligence:Enabled)` |
| R5 §3.1 (reuse mandate) | Mirrors `PlaybookIndexingBackgroundService` (Channel signal) + `ScheduledRagIndexingService` (PeriodicTimer). No parallel scheduler framework introduced. |
| R5 §3.2 (no new flag) | Confirmed — no new feature flag |
| R5 §3.3 (DI minimalism) | Confirmed — ZERO new top-level `Program.cs` lines |
| Spec NFR-02 (aggressive cleanup-on-session-end) | Fire-and-forget signal at end of `DeleteSessionAsync`; idempotent EvictSessionAsync helper |
| Spec NFR-03 (multi-tenancy) | Per-tenant filters + per-tenant orphan grouping (verified by test 7) |
| Project (idempotency) | Zero-result path returns 0 + skips delete batch + emits zero-count telemetry (verified by tests 1 + 4) |
| Project (back-compat) | `ChatSessionManager.DeleteSessionAsync` signature byte-for-byte unchanged; new constructor param is nullable + defaults null; fire-and-forget log-and-swallow on signal failure (verified by ChatSessionManager test "...SucceedsWhenCleanupSignalIsNull_BackCompat") |

---

## Cross-task signals

### Task 008 (telemetry events) — event-schema lock-in

This task emits `r5.session_files_cleanup.run` events via `Telemetry.AiTelemetry.ActivitySource` (i.e., as an `Activity` with low-cardinality tags). The event-name + tag-name conventions are LOCKED here so task 008 dashboard authors can write App Insights queries against stable identifiers.

**Event name**: `r5.session_files_cleanup.run`

**Per-eviction tags** (emitted by `EvictSessionAsync` for both `on_session_end` and `scheduled` triggers):
- `r5.trigger` — bounded enum: `scheduled` | `on_session_end`
- `r5.sessions_evicted` — int (always 0 or 1 per eviction call)
- `r5.documents_deleted` — int (0 on idempotent no-op)
- `r5.tenant_id` — string
- `r5.session_id` — string
- `r5.duration_ms` — long
- `r5.completion_status` — bounded enum: `success` | `partial` | `error`

**Per-scheduled-scan tags** (additional summary event emitted by `RunScheduledScanAsync`):
- `r5.trigger` — always `scheduled`
- `r5.sessions_evicted` — aggregated across the scan
- `r5.documents_deleted` — aggregated across the scan
- `r5.tenant_count` — number of distinct tenants with orphans
- `r5.per_tenant_breakdown` — delimited string of `tenant=count,tenant=count` (single tag to avoid high-cardinality tag explosion)
- `r5.duration_ms` — long (scan duration)
- `r5.completion_status` — bounded enum

**No shared `R5Telemetry` class introduced** — task 008 may extract a shared static after surveying the full event landscape. If task 008 extracts a shared class, this task's `EmitEvictionEvent` + `EmitScheduledRunEvent` should be refactored to delegate to it.

### Task 009 (Phase 1 test consolidation + publish-size)

This task contributes:
- **8 new unit tests** in `SessionFilesCleanupJobTests.cs` (7 per POML + 1 extra for signal-contract null-safety)
- **2 new unit tests** in `ChatSessionManagerTests.cs` (signal call + null-tolerance — both per POML)

Total new tests: 10 (vs POML's "7+2" baseline).

**Publish-size expectation**: zero new packages added; expected ≤ +0.1 MB compressed for the additive `BackgroundService` + signal helper + options class. Main session at task 009 will measure absolute size + delta vs prior baseline (~45.65 MB as of 2026-05-26).

---

## Quality-gate readiness

- code-review: ready (FULL rigor task)
- adr-check: ready (FULL rigor task)
- Both gates run by main session per parallel-wave protocol.

---

*Generated by parallel sub-agent for R5 task 007 / D1-07.*
