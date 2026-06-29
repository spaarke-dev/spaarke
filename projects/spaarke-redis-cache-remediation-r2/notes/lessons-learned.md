# Lessons Learned — spaarke-redis-cache-remediation-r2

> **Project status**: ✅ Local complete (PR pending operator-driven Phase 4 live deploy + KQL verification)
> **Author**: task-execute / task 032 wrap-up
> **Date**: 2026-06-26

---

## What worked

### 1. Meter consolidation (FR-02) — surfaced a hidden cleanup opportunity

The spec assumed ~3-5 consumers of the `CacheMetrics` instance class. The grep-audit during task 002 found **6 consumers**: `EmbeddingCache`, `GraphTokenCache`, `GraphMetadataCache`, `CachedAccessDataSource`, `AnalysisRagProcessor`, `TextExtractorService`. Each had a nullable `CacheMetrics? _metrics` field, a ctor param with `null` default, and `_metrics?.RecordHit(...)` call sites scattered through their methods. Promoting `CacheMetrics` to a static class let us:

- Remove `services.AddSingleton<CacheMetrics>()` from `DocumentsModule.cs`
- Remove the `CacheMetrics? metrics = null` ctor param from all 6 consumers
- Replace `_metrics?.RecordHit(...)` with direct `CacheMetrics.RecordHit(...)` calls
- Drop the null-check overhead at every cache call

Net effect: **6 ctor signatures simplified, 1 DI registration removed, ~30 null-conditional operator call sites eliminated**. This is exactly the cleanup ADR-010 ("concretes over interfaces", "DI minimalism") was designed to enable, and it was invisible until we audited the surface. The static-class form satisfied ADR-010 without sacrificing the centralized seam — there's still one canonical owner of the Meter and instruments, but it lives in compiled code rather than DI scope.

### 2. Parallel execution via sub-agents averaged ~3-5 tasks per wave

The TASK-INDEX.md parallel-execution plan (Wave 0 → Wave A → Wave B → Waves C/D in parallel) translated cleanly to actual sub-agent dispatch. Wave A (tasks 002 + 004 + 006) ran in parallel against distinct files (`Telemetry/CacheMetrics.cs` + `infrastructure/bicep/alerts.bicep` + `Program.cs`). Wave C-cont (tasks 011 + 013 + 014) was 3-wide on distinct workflow / runbook / bicep files. The hard sequencing point (task 002 blocks 003 + 005) was correctly identified at planning time, so no parallel work had to be unwound.

Lesson: spend the planning time to identify the file-overlap-free parallel groups; the dispatch then runs itself.

### 3. §F.2 Fixture-Config-FIRST caught a stale break before it broke integration tests

While building the integration test for FR-05 (task 005), the inspector found that `RecallSessionFileHandlerResolvableTests.cs` was still injecting `IDistributedCache` where `ITenantCache` was now required (pre-existing break from `chat-routing-redesign-r1` task 091 that hadn't surfaced because the test was Skip'd). The §F.2 protocol from `.claude/constraints/bff-extensions.md` flagged the right diagnosis — "before suspecting DI, inspect the fixture config" — and the fix was a 9-line surgical change rather than a DI re-design. Without §F.2, the natural reflex would have been "the integration test fixture is broken, let me add a special-case registration." That would have shipped a fix-the-symptom-not-the-cause patch.

Lesson: the procedure works. Trust it on the next test-DI puzzle, too.

### 4. Static class outcome satisfied ADR-010 "concretes over interfaces" without losing centralization

ADR-010 sometimes gets read as "always register concretes in DI" — but the deeper intent is "don't add a class to DI just because it's been a class historically." `CacheMetrics` as an instance class added zero value: there was no per-request state, no async lifecycle, no testability seam (the consumers tested cache behavior, not metric emission). Promoting to static eliminated the DI registration AND clarified ownership of the canonical Meter. The diff is net-positive on all three axes ADR-010 cares about: surface area down, indirection down, lifetime confusion down.

---

## What surprised us

### The publish-size measurement methodology trap

**The story**: At task 030 we ran `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish-r2/ + Compress-Archive` on the worktree's `work/spaarke-redis-cache-remediation-r2` branch state. The result was **49.66 MB compressed**, vs R1 close-out baseline of **46.67 MB**. That's a **+2.99 MB delta — 6× the NFR-04 ceiling of +0.5 MB.**

**The reflexive (wrong) reaction**: "R2 must have bloated something. Audit the new code for unintended package transitives."

**The actual cause**: While R2 was in flight, the `spaarke-ai-azure-setup-dev-r1` project landed **50 commits to master** that included AI-search canonicalization work (consolidated schema files, deleted `Create-PlaybookEmbeddingsIndex.ps1`, `Deploy-IndexSchemas.ps1`, several deprecated test files). Those commits net-reduced the BFF publish artifact by ~3 MB. Measuring R2's HEAD against the R1 close-out baseline counted R2 against a now-larger denominator.

**The corrected measurement**: After `git merge origin/master`, the apples-to-apples delta was **+0.01 MB (~10 KB)** — easily within NFR-04.

**The methodology lesson**:

> NFR-04 publish-size measurements MUST occur AFTER syncing master, NOT on the worktree's branched state in isolation. Otherwise the measurement conflates R's actual delta with the cumulative drift of every project that's merged to master since R's branch point.

This generalizes beyond Redis remediation. Any project that measures cumulative quantities (publish size, dependency count, NuGet transitives, test-count baselines) MUST sync master before measuring, or it will misattribute master's drift to its own delta.

**Recommendation**: add this to `.claude/constraints/azure-deployment.md` as a binding rule for future NFR-04 verification. See Recommendations §4 below.

---

## Recommendations for R3 / future Redis observability work

### 1. Revisit DEF-002 (Pub/Sub separation in prod) after 30 days of `cache.failures` + `cache.hits.by_resource` data

R2 closed DEF-002 as "out of scope" because we had no data showing Pub/Sub on the BFF's cache instance was causing measurable load. After R2's instruments deploy, we'll have:

- `cache.failures` Counter dimensioned by `op` + `outcome` — surfaces connection-class failures (timeouts, RedisConnectionException, SocketException) per code path
- `cache.hits.by_resource` / `cache.misses.by_resource` — surfaces which logical resources (graph, embedding, auth-access, knowledge-source) consume cache bandwidth

If after 30 days of production data we see `outcome=connection` spikes correlated with Pub/Sub-using consumers (e.g., real-time chat), reopen DEF-002 with evidence. If we don't, formally close DEF-002 as a "would-have-been-premature" decision and document it for future audits.

### 2. Re-evaluate `resource` cardinality only if observation exceeds 30 distinct values

NFR-06 says trust code-driven natural bounding (expected ~10-20 resource names). DO NOT pre-emptively add a soft cap on the `cache.hits.by_resource` / `cache.misses.by_resource` dimension. If a future App Insights query shows `cardinality(resource) > 30`, that's the trigger to revisit — at which point we'd add a cardinality budget at the wrapper layer (e.g., bucket unknown resources to `"other"`). Pre-emptive caps add complexity for a problem we haven't measured.

### 3. Operationalize the `RedisKeyRotation` missed-rotation alert by establishing a baseline event per env

The Bicep `redis-cache-rotation-missed-${environment}` scheduledQueryRule fires when no successful rotation has been recorded in 100 days. With an empty App Insights workspace, the alert sees `isnull(LastRotation)` and behaves correctly per the fallback arm — but a real "missed rotation" event will be indistinguishable from the never-rotated bootstrap state until at least one successful rotation lands.

**Action post-merge**: use `gh workflow run redis-key-rotation.yml -f environment=dev -f mode=dry-run` (or a real first rotation) **once per env** to establish the baseline `RedisKeyRotation` custom event. After that, the missed-rotation alert has a real signal to compare against. Document this in the runbook §6 + the operator post-deploy checklist.

### 4. Add a master-sync step to NFR-04 publish-size verification procedure

Per "What surprised us" above. Concrete proposal:

**Update** `.claude/constraints/azure-deployment.md` "BFF Publish-Size Per-Task Verification Rule (NFR-01)" section with a new sub-bullet:

> **Step 0 (NEW — added by spaarke-redis-cache-remediation-r2 lessons-learned, 2026-06-26)**: Before measuring publish size on a worktree branch, MUST run `git fetch origin && git merge origin/master --no-edit` (or rebase, if that's the project's policy). Measuring on a branched state without master-sync conflates the current project's delta with the cumulative drift of every project merged to master since the branch point. R2 hit this trap: pre-sync measurement showed +2.99 MB (6× NFR-04 ceiling); post-sync measurement showed +0.01 MB. The pre-sync number was misleading and would have triggered a false bloat investigation if acted upon.

Filed as Issue #485 for the future maintainer to action (no R2 task touches `.claude/` paths per CLAUDE.md §3 sub-agent write boundary + NFR-08 ADR-immutability — this is operator follow-up).

---

## Cross-references

- `spec.md` — full FR/NFR list
- `design.md` — locked decisions + Placement Justification
- `notes/post-deploy-verification.md` — methodology + publish-size apples-to-apples evidence
- `notes/customer-bicep-what-if.md` — Theme C live-customer audit evidence
- `notes/managed-redis-decision.md` — DEF-005 closure rationale (informs future managed-Redis re-evaluation cadence)
- Issues #483 / #484 / #485 — operator follow-up from task 031
- R1 `notes/r7-backlog.md` §S5/S6/S7 — source items R2 closed

---

*This file should be cited by future Redis-observability or BFF-hardening projects as prior art. R3 (if filed) should reference §What surprised us methodology + §Recommendations 1-4 directly.*
