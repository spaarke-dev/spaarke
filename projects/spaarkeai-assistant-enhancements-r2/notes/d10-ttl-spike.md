# FR-D10 Retention TTL — Feasibility Spike (task 033, step 1)

**Date**: 2026-08-06
**Author**: spaarkeai-assistant-enhancements-r2 (orchestrator spike)
**Status**: Spike COMPLETE — **conclusive**. Implementation NOT started (awaiting owner approval of the recommended path, per the spike-first constraint + §6 data-loss discipline).

---

## Question

FR-D10: a **filed** analysis's session (transcript + tabs + redline) must be retained **indefinitely**; an **unfiled** session should expire ~90 days. Owner-directed 2026-08-05: *prefer per-document Cosmos TTL extension on filing; fall back to removing the container-level TTL + an `expiresAt` field + a scheduled cleanup only if per-doc TTL is unavailable.*

**The pivotal feasibility question**: does the `sessions` Cosmos container's configuration support **per-item `ttl` override**?

---

## Finding — per-item TTL IS available (owner's preferred path is feasible)

Azure Cosmos DB rule: per-item `ttl` overrides the container default **only when the container's `DefaultTimeToLive` is non-null** (a positive N or `-1`). If the container `DefaultTimeToLive` is `null`, per-item `ttl` is **ignored**.

The `sessions` container has **`DefaultTimeToLive = 7776000` (90 days)** — a positive value → **per-item `ttl` override is enabled**.

| Evidence | Source |
|---|---|
| `sessions` container `defaultTtl: 7776000 // 90 days` | `infrastructure/bicep/modules/cosmos-db.bicep:80-95` (authoritative infra) |
| **LIVE dev confirms** — no drift | `az cosmosdb sql container show` → account `spe-cosmos-dev-ai` / db `spaarke-ai` / container `sessions` → `resource.defaultTtl = 7776000` |
| Container doc-comment "Retention: 90 days default (defined at container provisioning time)" | `StoredSession.cs:13` |
| **Existing per-item-TTL precedent in THIS codebase** | `cosmos-db.bicep:263-278` — the `memory-items` container uses `defaultTtl: -1` explicitly "to enable PER-ITEM ttl (retentionClass → ttl at task 052)". `audit`/`feedback` use `defaultTtl: -1` for permanence. Per-item TTL is an established, used pattern here. |

**Cosmos per-item `ttl` semantics** (with the container `DefaultTimeToLive = 7776000`):
- item `ttl` **absent / null** → item uses the container default → **expires at 90 days** (current unfiled behavior, unchanged).
- item `ttl = -1` → item **never expires** (indefinite retention) — this is the filed-session case.
- item `ttl = M` (positive) → item expires after M seconds (not needed here).

The BFF write path today sets **no** per-item `ttl` (`SessionPersistenceService.UpsertToCosmosAsync:884-892` does a plain `UpsertItemAsync(session, pk)`), so every session currently rides the 90-day container default — including filed ones, which is the bug FR-D10 fixes.

---

## Conclusion

**The owner's preferred path (per-doc TTL extension on filing) is FEASIBLE and is the correct, low-risk implementation.** The escalation trigger in the POML — *"if the spike is inconclusive on per-doc TTL, STOP and escalate the container-TTL-removal decision (data-retention blast radius)"* — **does NOT fire**: the spike is conclusive, so the risky fallback (removing the container TTL → all docs permanent + a scheduled cleanup job) is **not needed and should not be built**.

---

## Recommended implementation (SAFE — awaiting owner go-ahead; NOT yet implemented)

1. **`StoredSession.cs`** — add `[JsonPropertyName("ttl")] public int? Ttl { get; set; }`. Cosmos reads the `ttl` property natively; `null` = "use container default" (90 days), preserving exact current behavior for unfiled sessions. Additive + backward-compatible (older docs deserialize with `Ttl = null`).
2. **Filing hook** — in `ChatSessionManager.PromoteSessionToAnalysisAsync` (:520+, where a session is bound to a `sprk_analysis` and written through Redis+Cosmos via `UpdateSessionCacheAsync`), set the persisted doc's `ttl = -1` so the **filed** session's Cosmos doc becomes permanent. The "filed" signal is unambiguous (the promote flow sets `HostContext.EntityType = "sprk_analysisoutput"` + the Dataverse `sprk_analysis` FK).
3. **Unfiled sessions** — leave `ttl` null → the 90-day container default applies, purged natively by Cosmos. **No change to unfiled retention.**
4. **NOT needed** (vs the fallback): no container-TTL removal, no `expiresAt` field, **no scheduled cleanup job**. This is the key win — it eliminates the data-loss blast radius the fallback carried (a buggy cleanup job deleting the wrong docs) and the operational cost of a new background worker.

### Risk assessment — MINIMAL
- Additive `Ttl` field + one write-path change on the filing hook. No container reconfiguration. Unfiled-session expiry is unchanged (Cosmos-native, same 90 days). Filed sessions opt OUT of expiry via `ttl = -1`. There is **no path that deletes a filed session** and **no new deletion mechanism** at all.
- One guard to add in implementation: ensure EVERY write-through of a filed session re-asserts `ttl = -1` (a plain upsert that omits `ttl` would reset the doc to the 90-day default). Simplest: derive `Ttl` from the session's filed-state at map-to-StoredSession time, so it's always correct on every upsert — not a one-shot set. Tests must cover a post-filing message turn (upsert must keep `ttl = -1`).

### Test plan (for the implementation task)
- Filed session's persisted doc carries `ttl = -1` (permanent) — and STILL `-1` after a subsequent message turn's upsert (the re-assert guard).
- Unfiled session's persisted doc carries `ttl = null` (rides 90-day default).
- Promotion sets `ttl = -1`; an already-filed re-persist keeps it.
- (No cleanup-job tests — no cleanup job.)

---

## Decision requested

The spike resolves to the **safe per-doc path** — no data-loss-risk change, no cleanup job, no container reconfiguration. **Owner: approve implementing the per-doc `ttl = -1`-on-filing path?** (This supersedes the fallback described in the POML step 3, which is now unnecessary.)

---

## IMPLEMENTATION (2026-08-06 — owner approved; safe per-doc path built)

Owner approved the safe path ("continue"). Implemented exactly as recommended, with **one durability fix surfaced by code review** (below).

**Files changed** (additive only; no `.csproj`/package/dependency change → publish-size delta = 0; absolute 52.37 MB compressed, well under the 60 MB ceiling):
1. `StoredSession.cs` — `public const int NeverExpireTtl = -1;` + `[JsonPropertyName("ttl")] [JsonIgnore(WhenWritingNull)] public int? Ttl` + **`[JsonPropertyName("hostContext")] public ChatHostContext? HostContext`** (the fix — see below).
2. `ChatSessionManager.MapChatSessionToStoredSession` — `Ttl` DERIVED from `HostContext?.EntityType == "sprk_analysisoutput"` (filed → -1, unfiled → null), re-asserted on every write-through; **`HostContext = session.HostContext`** persisted to the warm tier.
3. `ChatSessionManager.MapStoredSessionToChatSession` — **`HostContext = stored.HostContext`** restored on Cosmos warm reload.
4. `ChatSessionManagerTests.cs` — 5 new tests (unfiled→null, filed later-turn→-1, promote→-1, **warm-reload→still -1**, STJ round-trip incl. omit-when-null).

### The durability fix (code-review Critical — caught + closed)
The spike's guard ("re-assert ttl on every upsert, derived from filed-state") was correct but **incomplete**: filed-state lives in `ChatSession.HostContext`, which `StoredSession` did NOT persist and the Cosmos→ChatSession warm-restore mapper did NOT restore. So the original vector: filed session (ttl=-1) → Redis evicts after 24h → reopen hits the Cosmos **warm** tier (checked before Dataverse) → restored `ChatSession` had `HostContext = null` → next message turn re-derived `ttl = null` → **overwrote the persisted `ttl = -1` with the 90-day default** → the filed analysis would expire ~90 days later (the exact FR-D10 bug). The `SessionPersistenceService` RMW writers (tabs/uploads/summary/message) were never the vector — they load+re-upsert and round-trip `ttl`; the clobber was exclusively the ChatSessionManager message-turn re-derivation after a warm reload.

**Fix**: persist `HostContext` through the warm tier (both mapper directions), the same "must survive warm-store restore" class as document references (ADR-040). Now filed-state survives eviction+reload, so re-derivation is correct on every turn. Regression test `GetSessionAsync_FiledSession_RestoredFromCosmos_NextTurnUpsert_KeepsNeverExpireTtl` reproduces the original failure and proves it closed. This is a strictly-more-correct behavior generally (a reopened filed session is now recognized as filed for all purposes, not just ttl).

### Acceptance-criteria mapping (safe path vs the POML's fallback-worded criteria)
- "Filed session resumable after >90 days" → ttl=-1 (Cosmos never expires it) — filed + promote + warm-reload tests. ✓
- "Unfiled session purged after expiresAt" → **adapted**: unfiled rides the Cosmos-native 90-day container default (no `expiresAt` field — that was the fallback). ✓ (unfiled→null test)
- "Cleanup idempotent / only deletes past-due unfiled" → **N/A / superseded**: the safe path has NO cleanup job; unfiled expiry is Cosmos-native. This is the key data-loss-blast-radius elimination vs the fallback. ✓
- "A filed session is never deleted" → structural: ttl=-1 = never-expire; no ttl-driven deletion mechanism exists (DeleteSessionAsync is explicit GDPR erasure only). ✓
- "Spike findings recorded / Publish ≤60 MB / Tests pass" → this doc / 52.37 MB / 744 Chat+Sessions+persistence+restore unit tests green. ✓
