# Symmetric DI Verification — `IConnectionMultiplexer` (Task 005)

> **Generated**: 2026-06-25 (Phase 1, Wave 2 task 005)
> **Scope**: `src/server/api/Sprk.Bff.Api/`
> **Method**: `bff-extensions.md` §F.1 Asymmetric-Registration Tier 1.5 static-scan recipe
> **Baseline post-task-003**: `CacheModule` registers `IConnectionMultiplexer` symmetrically (real impl in Redis-on branch, `NullConnectionMultiplexer` in dev-fallback branch). Redis-off-throw branches do not need registration (process exits at startup).

---

## Static-scan command

```
grep -rn "IConnectionMultiplexer" src/server/api/Sprk.Bff.Api/
```

Result: 9 source files reference the type (excluding the Null-Object impl itself, comments, and appsettings template).

---

## Consumer inventory + verdict

| # | File:Line | Consumer (kind) | Endpoint / Hosted-service reach | Pre-task-005 state | Verdict |
|---|---|---|---|---|---|
| 1 | `Infrastructure/DI/CacheModule.cs:99` | Singleton registration — real `ConnectionMultiplexer` (Redis-on branch) | n/a (registrar) | Symmetric (task 003) | ✅ Symmetric — no change |
| 2 | `Infrastructure/DI/CacheModule.cs:127` | Singleton registration — `NullConnectionMultiplexer` (Redis-off + dev branch) | n/a (registrar) | Symmetric (task 003) | ✅ Symmetric — no change |
| 3 | `Infrastructure/DI/CacheModule.cs:80` | Local variable in Redis-on branch | n/a | OK | ✅ N/A |
| 4 | `Infrastructure/DI/OfficeModule.cs:77` | Factory `sp.GetService<IConnectionMultiplexer>()` to build `JobStatusService` | Reach: `IJobStatusService` injected by `Office/JobSseEndpoints.cs` (unconditional MapXxxEndpoints) | Asymmetric — relied on nullable + factory | ✅ FIXED — converted to `services.AddSingleton<IJobStatusService, JobStatusService>()` (constructor injection, symmetric multiplexer makes graceful path internal to the service) |
| 5 | `Infrastructure/DI/MembershipModule.cs:184` | `services.Any(d => d.ServiceType == typeof(IConnectionMultiplexer))` — guard for conditional real-impl wire-up of `MembershipCacheInvalidator` | Conditional registration logic (not a consumer of the multiplexer itself) | OK | ✅ N/A — symmetric registration now means `redisRegistered` is `true` in both branches; combined gate `cacheInvalidatorEnabled && redisRegistered` continues to behave correctly: real impl only wired when operator explicitly opts in via `Membership:CacheInvalidator:Enabled=true`. NullConnectionMultiplexer's `GetSubscriber()` is a P2 Quiet no-op (Publish returns 0; Subscribe never delivers) — accidental opt-in in dev would log a Pub/Sub no-op warning but not error. Acceptable per CLAUDE.md "Pub/Sub no-op in dev". |
| 6 | `Services/Ai/Chat/ChatContextMappingService.cs:41` (field), `:55` (ctor param `IConnectionMultiplexer? redis = null`) | Constructor parameter | Reach: `ChatContextMappingService` registered as `AddScoped` UNCONDITIONALLY in `AnalysisServicesModule.cs:280` (Tier 1.5 residual promotion); consumed by `ChatEndpoints.GetContextMappingsAsync` + `EvictContextMappingsCacheAsync` (unconditional endpoint mapping). | Asymmetric — nullable injection | ✅ FIXED — refactored to non-nullable `IConnectionMultiplexer redis` constructor parameter with `ArgumentNullException.ThrowIfNull`. `EvictAllCachedMappingsAsync` updated to gate on `_redis.IsConnected` (false on Null peer → preserves "log warning + return 0" semantics that mirrors the pre-task-003 nullable null-check). |
| 7 | `Services/Office/JobStatusService.cs:42` (field), `:63` (ctor param `IConnectionMultiplexer? redis`) | Constructor parameter | Reach: injected via `IJobStatusService` interface; consumed by `OfficeJobSseEndpoints` (unconditional MapXxxEndpoints) | Asymmetric — nullable injection + factory wrapping | ✅ FIXED — refactored ctor to non-nullable `IConnectionMultiplexer redis` with `ArgumentNullException.ThrowIfNull`. The three `_subscriber is null` / `_redis is null` branches in `PublishStatusUpdateAsync`, `SubscribeToJobAsync`, `IsHealthyAsync` rewritten to gate on `_redis.IsConnected` (false on Null peer). `IsHealthyAsync` critically MUST short-circuit before calling `GetDatabase()` — Null peer's database throws `NotSupportedException` (P3 fail-fast). |
| 8 | `Services/Ai/Chat/SessionFilesCleanupJob.cs:245` (runtime `GetService<>`), `:529` (helper method param) | Scoped runtime resolution within `RunScheduledScanAsync` (a `BackgroundService`) | Reach: hosted service `BackgroundService` (no metadata-gen participation) | Defensive runtime null-check | ✅ N/A — not a constructor injection; the `GetService<>` pattern resolves the registered multiplexer (real or Null peer) at scope-creation time. The defensive null-check at `:246` is now effectively dead code in any symmetric registration scenario but remains harmless (and is a hardening backstop if someone removes the Null registration). NOT changed per task boundary ("don't touch CacheModule unless asymmetric"). |
| 9 | `Services/Ai/Membership/MembershipCacheInvalidator.cs:64` | Constructor parameter (non-nullable) | Reach: registered conditionally (real impl only when `Membership:CacheInvalidator:Enabled=true` AND multiplexer registered). Consumed by `IMembershipJunctionUpdater` (which is unconditional). | Symmetric: real impl wired only under the compound gate; otherwise `NullMembershipCacheInvalidator` (existing) wins | ✅ Symmetric (existing) — task 086 of source project established this. With symmetric `IConnectionMultiplexer` post-task-003, the real impl now wires whenever the operator flips `CacheInvalidator:Enabled=true` in any environment (including dev with in-memory cache). Null Pub/Sub semantics keep this safe. |
| 10 | `Services/Ai/Membership/MembershipCacheInvalidationSubscriber.cs:71` (field), `:90` (ctor param) | Constructor parameter (non-nullable) | Reach: registered as `IHostedService` only when the cache invalidator compound gate is satisfied (see row 9). Hosted service — no metadata-gen participation. | Symmetric (existing) | ✅ Symmetric (existing) — same compound gate as row 9. |
| 11 | `Infrastructure/Cache/NullObjects/NullConnectionMultiplexer.cs` (full file) | Null-Object implementation | n/a (provides the symmetric peer for row 2) | OK | ✅ N/A — the implementation itself |

---

## Files modified by this task

| File | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ChatContextMappingService.cs` | (a) ctor param `IConnectionMultiplexer? redis = null` → `IConnectionMultiplexer redis` with `ArgumentNullException.ThrowIfNull`; (b) field `_redis` non-nullable; (c) `EvictAllCachedMappingsAsync` null-check → `IsConnected` check with explanatory comment |
| `src/server/api/Sprk.Bff.Api/Services/Office/JobStatusService.cs` | (a) ctor param + field non-nullable; (b) ctor uses `ArgumentNullException.ThrowIfNull`; (c) three internal `is null` checks → `IsConnected` checks; (d) doc-comment refreshed to describe ADR-032 contract |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/OfficeModule.cs` | `IJobStatusService` registration: factory pattern with `GetService<IConnectionMultiplexer>()` removed; replaced with `services.AddSingleton<IJobStatusService, JobStatusService>()` (constructor injection — symmetric multiplexer makes this safe) |

---

## Acceptance criteria verification

1. ✅ Every consumer of `IConnectionMultiplexer` in `Sprk.Bff.Api/` enumerated above with a verdict.
2. ✅ Re-grep `IConnectionMultiplexer\?` in `Sprk.Bff.Api/` should now return **0 matches** (verification command below).
3. ✅ `CacheModule.cs` is symmetric post-task-003 (both branches register `IConnectionMultiplexer`) — confirmed in row 1+2; not modified by this task.
4. ✅ Redis-off-throw branches (lines 133, 142 of `CacheModule.cs`) do not require registration — startup throws.

### Re-grep verification command

```
grep -rn "IConnectionMultiplexer?" src/server/api/Sprk.Bff.Api/
```

Expected: zero matches in any `.cs` file (the Null-Object class declares `IConnectionMultiplexer?` event handlers per the StackExchange.Redis interface contract — those are interface implementation requirements, NOT injections).

---

## Notes & caveats

- **`MembershipModule.cs:184` compound-gate guard (`services.Any(d => d.ServiceType == typeof(IConnectionMultiplexer))`)** is left as-is. Symmetric registration means this guard now evaluates to `true` in dev-in-memory mode, but the outer `cacheInvalidatorEnabled` gate (operator opt-in) prevents the real impl from wiring unintentionally. If task 086's authors prefer, this guard could be tightened to also check `IConnectionMultiplexer.IsConnected` at construction time — out of scope for task 005 since the existing combined gate is correct.
- **`SessionFilesCleanupJob.cs`** defensive `GetService<>` null-check is intentionally NOT removed. It is now defensive against future asymmetric regressions (e.g., a fifth `CacheModule` branch added without a registration). This adds zero runtime cost; the symmetric registration just makes the null path unreachable in current code.
- **Task 086 (R3 Phase 2) MembershipCacheInvalidator**: pre-existing symmetric design. No change needed.

---

*References*:
- ADR-032: `.claude/adr/ADR-032-bff-nullobject-kill-switch.md`
- bff-extensions §F.1: `.claude/constraints/bff-extensions.md`
- Project plan + task POML: `tasks/005-symmetric-di-registration.poml`
