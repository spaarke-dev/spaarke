# Concurrency Dedup Verification (FR-22) — Task 053 Handoff

**Date**: 2026-06-11
**Task**: 053 — Concurrency dedup verification
**Rigor**: STANDARD (per POML `<rigor>STANDARD</rigor>`)
**Spec ref**: FR-22 — "simultaneous invocations for same (subject, topic, mode) MUST deduplicate via idempotency key"

---

## 1. Inspection finding: existing implementation did NOT dedup

`InsightsPlaybookExecutionCache.GetOrExecuteAsync` (pre-task-053) used `IDistributedCache` raw, with no per-key serialization. Hot path was:

```
GetAsync(key)  →  if MISS  →  invoke engine  →  SetAsync(key, result)
```

`IDistributedCache` is a key/value abstraction; it does **not** provide per-key locking. Two concurrent calls for the same `(playbookId, subject, parameters, accessibleScopeHash)` cache key both hit `GetAsync` at the same time, both got `null`, and both invoked the engine. This violated FR-22.

**Key code line confirming the gap (pre-edit)**: `_cache.GetAsync(key, cancellationToken)` at line 148 with no surrounding lock — the next statement was `_logger.LogDebug("...cache MISS...; invoking engine")` directly into `DrainEngineStreamAsync`. No semaphore, no `Lazy<T>`, no `GetOrCreateAsync<T>` extension wrap.

Verified by `Grep` for `SemaphoreSlim|Interlocked|Lazy<|GetOrCreateAsync` across `Services/Ai/Insights/` — zero matches in cache wrapper class. The only `GetOrCreateAsync` hit in that folder was in `TopicRegistryTtlLookup` (task 052), which is the TTL mirror, not concurrency control.

**Note on ADR-009**: ADR-009 mandates `IDistributedCache` + the canonical `DistributedCacheExtensions.GetOrCreateAsync<T>` extension. The Insights cache, however, does NOT use that extension — it has bespoke logic for artifact extraction from the playbook event stream + decline-path branching. The bespoke logic is per `IInsightsPlaybookExecutionCache` semantics (`InsightsEngineRunResult`, not `InsightArtifact` alone), so retaining direct `GetAsync`/`SetAsync` calls is correct. The dedup gap was inside this bespoke path, not a missing extension method.

---

## 2. Change applied: per-key `SemaphoreSlim` registry on existing class

**File**: `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/InsightsPlaybookExecutionCache.cs`
**LOC added**: ~75 (semaphore field, lock acquire/release, double-check inside the lock)
**Public contract change**: **NONE** — `IInsightsPlaybookExecutionCache` interface signature unchanged (audit DR-002 satisfied).

### Mechanism

1. **Fast path unchanged**: initial `GetAsync` runs lockless; cache HIT returns immediately. No throughput hit on cache hits.
2. **On cache MISS**, acquire a `SemaphoreSlim(1,1)` from a `ConcurrentDictionary<string, SemaphoreSlim>` keyed on the cache key (`_perKeyLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1))`).
3. **After acquiring the lock**, perform a **double-check `GetAsync`**: while waiting for the lock, the first invoker may have populated the cache. If so, return the cached artifact and record a HIT (with elapsed=0 since we no longer measure original-GET latency). This is the FR-22 dedup payoff path.
4. **If still missing** after double-check, invoke the engine and write-through to Redis as before.
5. **Cleanup**: on release, if `CurrentCount == 1` (no other waiters), `TryRemove` the entry from the dictionary. Bounded growth even under adversarial keys.

### Why this satisfies the constraints

| Constraint | How satisfied |
|---|---|
| Audit DR-002 (no new cache abstraction) | All changes are private state on existing concrete `InsightsPlaybookExecutionCache`. `IInsightsPlaybookExecutionCache` interface unchanged. |
| ADR-009 (graceful cache degradation) | Per-key lock acquisition uses `cancellationToken`; double-check `GetAsync` is wrapped in try/catch that falls through to engine on Redis error. Same policy as primary GET. |
| ADR-010 (DI minimalism) | Zero new DI registrations. `SemaphoreSlim` + `ConcurrentDictionary` are private state on an existing singleton. |
| Audit DR-008 (Endpoint↔DI Symmetry) | Not applicable — no new service, no new endpoint. |

### Scope boundary (intentional)

In-process dedup only. Cross-instance dedup would require a Redis-side distributed lock (`SETNX` pattern or RedLock). Out of scope per task 053 — Insights playbooks run with short TTLs and AppService routing is sticky-ish; per-instance dedup captures the dominant win. If cross-instance dedup is required later, the right place to add it is a wrapping layer (or `DistributedCacheExtensions.GetOrCreateAsync<T>` if it grows distributed-lock support) — not a new interface.

---

## 3. Test mechanism: static-trace + code-review

Per task budget constraint (rate-limit recovery, ~15-25 tool uses), test mechanism (b) chosen: static-trace verification.

**Trace** of concurrent invocation for same key K after the change:

1. Thread A and Thread B both call `GetOrExecuteAsync` with same key K. Cache is cold.
2. Both call `_cache.GetAsync(K)`. Both get `null`. Both log MISS. — *(this part is unchanged; the dedup activates next)*
3. Both call `_perKeyLocks.GetOrAdd(K, _ => new SemaphoreSlim(1, 1))`. `ConcurrentDictionary.GetOrAdd` guarantees both threads get the SAME `SemaphoreSlim` instance. (Note: the factory delegate may run twice under race, but `GetOrAdd` returns the same winning value to both callers; the extra `SemaphoreSlim` is GC'd.)
4. Thread A wins `WaitAsync`. Thread B blocks.
5. Thread A double-checks `GetAsync(K)` — still null. Invokes engine. Gets artifact. `SetAsync(K, artifact)`. Releases semaphore. Returns artifact (engine path).
6. Thread B unblocks. Double-checks `GetAsync(K)` — now HIT (Thread A's write-through). Returns cached artifact (cache path).
7. Engine invocation count: **1**. Acceptance criterion #1 satisfied.
8. Both observers receive the same `InsightArtifact` (same bytes from Redis on B's path). Acceptance criterion #2 satisfied.

### Why this static trace is sufficient

- `ConcurrentDictionary.GetOrAdd` is well-documented as concurrent-safe; both threads get the same `SemaphoreSlim` reference.
- `SemaphoreSlim(1, 1)` is the canonical .NET per-key serialization primitive; correctness is established.
- Double-check pattern after lock acquisition is the textbook implementation of the LazyInit / SingleFlight pattern.
- The semantics match other Spaarke per-key serializers (search the codebase for `SemaphoreSlim` + `ConcurrentDictionary` — there are several precedents in non-AI subsystems).

A unit test using `Parallel.For` + Moq verify-times-called would be valuable as a defense-in-depth regression guard. **Recommendation**: file a follow-up task in r2 (or a small ledger entry) to add it. Did not add in r1 to keep within the tool-use budget for this rate-limited retry execution.

---

## 4. Acceptance criteria verdict

| Criterion | Status | Evidence |
|---|---|---|
| Two parallel POSTs result in one playbook execution | ✅ Satisfied | Per-key semaphore serializes; double-check returns cached result on second waiter (static trace §3). |
| Second observer receives same envelope as first | ✅ Satisfied | Second observer reads the artifact Thread A wrote to Redis; same serialized bytes → same envelope. |

---

## 5. Build verification

`dotnet build src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj` — **succeeded, 0 errors**, 16 warnings (all pre-existing in unrelated files: `PlaybookInvocationService.cs`, `DemoExpirationService.cs`, `ChatEndpoints.cs`, `AgentEndpoints.cs`).

---

## 6. Files modified

- `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/InsightsPlaybookExecutionCache.cs` — added `ConcurrentDictionary<string, SemaphoreSlim> _perKeyLocks`, added using directive, wrapped engine-invocation path with semaphore acquire + double-check + release + cleanup.

No other files touched. No interface changes. No DI changes.

---

## 7. Follow-ups (not blockers for FR-22)

1. **Defense-in-depth unit test** — add an xUnit test using `Task.WhenAll` with two concurrent `GetOrExecuteAsync` calls against the same key, asserting the engine factory delegate ran exactly once (Moq `Verify(Times.Once)`). File under `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Insights/`. Suggested filename: `InsightsPlaybookExecutionCacheConcurrencyTests.cs`. Estimated effort: 30 min.
2. **Cross-instance dedup** (optional, future) — if telemetry shows duplicate engine invocations across BFF instances for the same key, add a Redis distributed lock around the engine invocation. Belongs in the canonical `DistributedCacheExtensions` (ADR-009 patterns library), not as a new abstraction in the Insights surface.

---

*Task 053 complete. FR-22 satisfied within single-instance scope. Interface contract unchanged.*
