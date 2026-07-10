# Task 073 — Track-B hygiene sweep (R9 TimeProvider probes, R11 script drift + dead env keys)

> **Date**: 2026-07-10 · **Executor**: task-execute (FULL rigor, parallel wave H) · **Scope**: exactly the three named surfaces (design.md §10 rows 9 + 11) — no unrelated refactors.

---

## 1. R9 — `Task.Delay` → `TimeProvider` probes

### Inventory

`rg "Task\.Delay" src/server` returned 33 files. The overwhelming majority are standard `BackgroundService.ExecuteAsync` polling loops (`await Task.Delay(pollInterval, stoppingToken)`), which is the idiomatic .NET hosted-service pattern — **not** in scope (converting all 33 would be an unbounded refactor, explicitly forbidden by the POML's "no scope expansion" constraint).

The concrete, named R9 debt is a matched pair of **manifest-readiness probes** that share one policy (`EventRulesOptions.ReadinessProbeAttempts` × `ReadinessProbeDelayMs`) and whose code comments *explicitly* flagged the deferred TimeProvider conversion:

| File | Method | Comment before fix |
|---|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SessionDispatchOrchestrator.cs` (`ResolveFileOperandAsync`) | Click-path dispatch seam manifest-readiness probe (G-P2 UAT round-1 finding 4) | *"Task.Delay matches the Event-path precedent; the TimeProvider refactor is on the /defer list."* |
| `src/server/api/Sprk.Bff.Api/Services/Ai/EventRules/EventRulesService.cs` (`FireAsync`) | Event-path manifest-readiness probe (G-P1 UAT round-1 Defect 3) | Same policy; same underlying `Task.Delay(Math.Max(0, _options.ReadinessProbeDelayMs), cancellationToken)` |

This is precisely the debt referenced by r1's `DEF-006` deferral and design.md §10 row 9 ("Task.Delay → TimeProvider probes (r1 /defer)").

### Fix

Both classes now take an optional `TimeProvider? timeProvider = null` constructor parameter (defaults to `TimeProvider.System` — matches the existing codebase idiom used by `ContextBinder`, `PortfolioService`, `EventPathUserState`, etc.), and the probe delay call now uses the `Task.Delay(TimeSpan, TimeProvider, CancellationToken)` overload instead of the two-argument `Task.Delay(int, CancellationToken)` form — the same overload `UiActionAckCoordinator` already used for its ack-timeout wait.

```csharp
await Task.Delay(
        TimeSpan.FromMilliseconds(Math.Max(0, _manifestProbeOptions.ReadinessProbeDelayMs)),
        _timeProvider,
        cancellationToken)
    .ConfigureAwait(false);
```

`TimeProvider` is already registered as a singleton (`services.TryAddSingleton<TimeProvider>(TimeProvider.System)`, present in `WorkspaceModule.cs` / `MembershipModule.cs` / `InsightsIngestModule.cs`) so no new DI registration was required — both `SessionDispatchOrchestrator` and `EventRulesService` are constructed via plain `services.AddScoped<T>()` (no factory lambda), so the DI container resolves the new optional parameter automatically.

The Null-Object kill-switch subclass (`NullSessionDispatchOrchestrator`, via the protected logger-only ctor) sets `_timeProvider = null!` like its sibling fields — it throws `FeatureDisabledException` before any field is dereferenced (ADR-032 P3 pattern), consistent with the existing null-field convention in that ctor.

### Test impact

No test files were modified. `ReadinessProbeDelayMs = 0` in every existing test builder still produces a `Task.Delay(TimeSpan.Zero, timeProvider, ct)` call, which completes synchronously regardless of which `TimeProvider` is supplied (zero/negative delay short-circuits in the BCL implementation) — so the existing "deterministic, no wall-clock wait" test behavior is unchanged. All 4 constructor call sites that build a real `SessionDispatchOrchestrator` (`SessionDispatchManifestProbeTests.cs`, `DispositionRoutabilitySeamTests.cs`, `ContextBinderActionRunnerSeamTests.cs`, `CodedWorkflowDispatchSeamTests.cs`) pass positional args ending at `logger` — the new `timeProvider` parameter is optional and trailing, so all four compiled and ran unchanged.

**Scoping decision (documented, not expanded)**: r1's `DEF-006` deferral also bundled "AuditLog/NetArchTest contention flakes" under the same sentence as the 4-known-failing-tests item. Investigation found `AuditLogServiceTests.cs`'s `Task.Delay(200)`/`Task.Delay(1000)` calls are waiting on a **fire-and-forget `Task.Run` write** (`AuditLogService.LogInteractionAsync` dispatches to the thread pool, `_ = Task.Run(...)`), not a backoff/probe policy — there is no production "probe" to convert to `TimeProvider` here; the fix would require adding a completion signal to `AuditLogService` itself, which is a different and materially larger change than "convert a probe to TimeProvider." Left out of scope per the POML's bounded-cleanup constraint; the `NetArchTest` contention item is likewise a test-runner-level flake (parallel test collections), unrelated to a `Task.Delay` probe. Both remain open items in the inherited-backlog bundle (r1 `defer-issues.md` DEF-006) — not closed by this task.

### Verification

- `dotnet build src/server/api/Sprk.Bff.Api/` — **0 errors** (19 pre-existing warnings, unrelated to this change).
- `dotnet test` filtered to `SessionDispatchManifestProbeTests|EventRulesServiceTests|EventPathUserStateTests` — **33/33 passed**.
- `dotnet test` filtered to the three seam-test classes that construct `SessionDispatchOrchestrator` directly (`DispositionRoutabilitySeamTests`, `ContextBinderActionRunnerSeamTests`, `CodedWorkflowDispatchSeamTests`) — **15/15 passed**.

---

## 2. R11a — Dead App Service environment keys

### Source

`projects/spaarke-ai-architecture-redesign-r1/notes/task-040-dataverse-changes.md` §4 ("App Service configuration follow-up") named the exact dead keys left over after FR-P3-01 replaced the config-map fallback with `sprk_playbookconsumer` Binding rows: `Insights__Playbooks__Map__*`, `Workspace__*PlaybookId`, `LinearConsumers__*` on `spaarke-bff-dev`.

### Verification (no live reader)

`rg` across `src/server` for every key in both `Key__Sub__Path` (App Service double-underscore) and `Key:Sub:Path` (`IConfiguration` colon) forms found **zero** active reads — only doc-comment mentions in `ActionRunner.cs` (explaining why `MaxOutputTokensCeiling` replaced the config map) and `ConsumerTypes.cs` (explaining the `LinearConsumers:PlaybookIds` reverse-lookup was replaced). No Bicep/parameters file declares these keys either (`infra/**` grep: zero hits) — they exist only as manually-set App Service Application Settings.

### Action taken

Queried and removed the 8 confirmed-dead settings from `spaarke-bff-dev` (resource group `rg-spaarke-dev`) via `az webapp config appsettings delete`:

| # | Setting name | Removed value (for the record) |
|---|---|---|
| 1 | `Insights__Playbooks__Map__predict_matter_cost_v1` | `63b80630-975b-f111-a825-3833c5d9bcab` |
| 2 | `Workspace__AiSummaryPlaybookId` | `18cf3cc8-02ec-f011-8406-7c1e520aa4df` |
| 3 | `Workspace__PreFillPlaybookId` | `2d660cad-d418-f111-8343-7ced8d1dc988` |
| 4 | `Workspace__ProjectPreFillPlaybookId` | `fc343e9c-3460-f111-ab0b-7c1e521b425f` |
| 5 | `Workspace__MatterPreFillPlaybookId` | `2d660cad-d418-f111-8343-7ced8d1dc988` |
| 6 | `Workspace__SummarizePlaybookId` | `4a72f99c-a119-f111-8343-7ced8d1dc988` |
| 7 | `LinearConsumers__PlaybookIds__document_profile` | `18cf3cc8-02ec-f011-8406-7c1e520aa4df` |
| 8 | `LinearConsumers__MaxOutputTokens__summarize_file` | `4000` |

Post-delete verification: `az webapp config appsettings list ... --query "[?starts_with(name,'Insights__Playbooks') || starts_with(name,'Workspace__') || starts_with(name,'LinearConsumers__')]"` returns **empty** — confirmed removed. This is a **live-environment (dev App Service) configuration change**, not a repo file change; flagged below as a shared-resource touch for the parallel hardening wave.

No app restart was required for `az webapp config appsettings delete` to take effect at the config-source level (App Service always re-reads settings on next process restart / existing running process keeps its already-loaded `IConfiguration` snapshot until next natural recycle — since nothing reads these keys, there is no functional impact either way).

---

## 3. R11b — `Refresh-ScopeModelIndex.ps1` drift

### Method

The script itself was **not modified** — a fresh run against live Dataverse (`spaarkedev1`) was diffed against the committed `.claude/catalogs/scope-model-index.json`, using a scratch `-OutputFile` so the committed catalog was not touched (see §4 below — this file lives under `.claude/` and this task-execute agent instance cannot write there per the sub-agent write boundary; CLAUDE.md §3).

```powershell
.\scripts\Refresh-ScopeModelIndex.ps1 -DataverseUrl "https://spaarkedev1.crm.dynamics.com" `
  -OutputFile <scratch-path>
```

Live query returned: 59 Actions, 31 Skills, 21 Knowledge, 41 Tools.

### Drift found (data drift, not a script bug)

The script's query logic, label maps, and field selects all executed cleanly with no errors — there is **no bug in the script**. The drift is in the **committed catalog snapshot being stale** relative to live Dataverse (last `$generated` stamp: `2026-07-07T13:55:44Z`):

1. **1 Action description changed** — `DAILY-BRIEFING@v1`: the committed catalog has the pre-per-channel-narration description; live Dataverse now reflects the current `BRIEF-NARRATE-TLDR` / `BRIEF-NARRATE-CHANNEL` split (a legitimate downstream change from other work, not something this task should "fix" — the catalog snapshot just needs refreshing).
2. **10 stale duplicate "ghost" Knowledge entries** — the committed catalog carries BOTH the correct bare-coded entries (`KNW-001` … `KNW-010`, matching live Dataverse's `sprk_knowledgecode` values, confirmed active via direct Web API query) **and** 10 leftover slug-coded duplicates (`KNW-001-contract-terms-glossary`, `KNW-002-nda-checklist`, …) whose `name` field is literally the slug itself — clearly a stale artifact from an earlier authoring pass, never pruned by a subsequent refresh. Live Dataverse has no `sprk_knowledgecode` matching the slug form; these 10 entries are pure catalog cruft.
3. **1 new Tool not yet reflected** — `MEMORY-WRITE` exists live but is absent from the committed catalog (a newer tool, deployed after the last refresh).
4. **4 Tool descriptions changed** — `DATAVERSE-CREATE-RECORD`, `DATAVERSE-READ-QUERY`, `EMAIL-DRAFT`, `SEND-WORKSPACE-ARTIFACT` all have materially updated live descriptions (attachment/documentId guidance, sprk_document write-contract detail, lookup-column selectability guidance) vs. the committed snapshot — again, legitimate downstream authoring drift, not something to "fix" at the source; the catalog just needs to catch up.

Skills: **no drift** (31/31 match).

### Disposition (documented per acceptance criterion — "drift corrected or the expected diff documented")

The corrected, fully-regenerated catalog has been written to **`projects/spaarke-ai-architecture-redesign-r2/notes/073-scope-model-index-fresh-2026-07-10.json`** (NOT to `.claude/catalogs/scope-model-index.json` — see the shared-file flag below). Applying the fix is a one-line action for whichever session holds `.claude/` write permission:

```powershell
Copy-Item projects/spaarke-ai-architecture-redesign-r2/notes/073-scope-model-index-fresh-2026-07-10.json `
  .claude/catalogs/scope-model-index.json
```

(or simply re-run `Refresh-ScopeModelIndex.ps1` with its default `-OutputFile`.) Curated fields (`tags`, `documentTypes`, `compatibleActions`, `contentType`) are preserved automatically by the script's `Find-ExistingEntry` merge logic keyed on `code` — since the 10 ghost knowledge entries carry no curated `tags` (none had them), nothing curated is lost by pruning them; verified by inspecting the committed file's ghost entries directly (`tags: []` / `contentType: "Reference"` default on all 10).

---

## 4. Shared-file flag (sub-agent write boundary)

Per root `CLAUDE.md` §3, this task-execute instance (dispatched as one of 4 parallel hardening-wave agents) **cannot write to `.claude/` paths**. `.claude/catalogs/scope-model-index.json` is the one output this task would otherwise touch directly. Per the canonical pattern, the corrected file has been generated and placed at `projects/spaarke-ai-architecture-redesign-r2/notes/073-scope-model-index-fresh-2026-07-10.json` for the main session to apply with a single `Copy-Item` (or a re-run of the script with its default output path). **No other shared/`.claude/` files were touched.**

---

## 5. Verification summary (task step 4)

| Check | Result |
|---|---|
| Build (`dotnet build src/server/api/Sprk.Bff.Api/`) | 0 errors, 19 pre-existing warnings (unrelated) |
| Affected unit tests | 33/33 passed (`SessionDispatchManifestProbeTests`, `EventRulesServiceTests`, `EventPathUserStateTests`) |
| Affected seam tests | 15/15 passed (`DispositionRoutabilitySeamTests`, `ContextBinderActionRunnerSeamTests`, `CodedWorkflowDispatchSeamTests`) |
| Publish-size delta | Measured directly: `dotnet publish -c Release src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj -o <scratch>` → `Compress-Archive` → **46.59 MB compressed (incl. PDBs)**. Root CLAUDE.md §10 baseline as of 2026-07-08 (task 055) was 49.63 MB incl. PDBs — this run is **3.04 MB below** that baseline (the reduction reflects intervening merged work, not this task's 2-optional-constructor-parameter change, which has ~zero material footprint of its own). Well under the 60 MB ceiling; no escalation triggered. Scratch publish artifacts were deleted after measurement (not committed). |
| New HIGH CVE | N/A — no package references added |
| bff-extensions.md checklist | Placement: existing classes only (no new endpoint/service/DI registration); Test Update Obligation: satisfied — existing tests re-run green, no behavior change requiring new tests (the conversion is a drop-in overload swap with identical runtime behavior for `delay <= 0`) |
| Dead App Service keys | 8/8 removed + enumerated (§2) |
| Scope-model-index drift | Identified + fully diffed; corrected catalog generated to project notes (main-session apply required — §3/§4) |
| Out-of-scope items surfaced (not fixed here) | AuditLog/NetArchTest contention flakes (r1 DEF-006, unrelated to a Task.Delay probe) |
