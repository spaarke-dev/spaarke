# Task Index — Spaarke Platform Foundations (R3)

> **Project**: `spaarke-platform-foundations-r3`
> **Last Updated**: 2026-06-22 (Wave 21 — task 081 closed; cross-cutting MembershipEventPublisher + Null peer)
> **Status**: 55 / 69 complete; 1 blocked-operator (071); 13 pending (operator/human-gated by 071 deploy)
> **Branch**: `work/spaarke-platform-foundations-r3`
> **Parallel-optimized**: Yes (per user directive 2026-06-20)

---

## Task Register

Status legend: 🔲 not-started · 🔄 in-progress · ✅ complete · ❌ blocked

| ID | Title | Phase | Status | Dependencies | Group | Parallel-Safe | Rigor |
|----|-------|-------|--------|--------------|-------|---------------|-------|
| 001 | Register `default` Handlebars helper | P1 | ✅ | none | — | true | FULL |
| 002 | Register `joinIds` Handlebars helper | P1 | ✅ | 001 | — | true | FULL |
| 003 | Migrate `notification-tasks-due-soon.json` from `??` | P1 | ✅ | 002 | **B** | true | FULL |
| 004 | Grep + migrate remaining `??` playbooks | P1 | ✅ | 002 | **B** | true | STANDARD |
| 005 | Unrendered-template runtime warning | P1 | ✅ | 004 | — | true | FULL |
| 010 | Scaffold `Spaarke.Scheduling` library | P2 | ✅ | none | **C-init** | false | FULL |
| 011 | Define `IScheduledJob` contract + records + enum | P2 | ✅ | 010 | **C** | true | FULL |
| 012 | `MembershipOptions` placeholder + appsettings binding | P2 | ✅ | 010 | **C** | true | STANDARD |
| 013 | Implement `ScheduledJobHost : BackgroundService` | P2 | ✅ | 010, 011 | **D** | true | FULL |
| 014 | Retry/backoff + idempotency in ScheduledJobHost | P2 | ✅ | 013 | **D** | true | FULL |
| 015 | Create `sprk_backgroundjob` entity | P2 | ✅ | 010 | **E** | true | FULL |
| 016 | Create `sprk_backgroundjobrun` entity | P2 | ✅ | 010 | **E** | true | FULL |
| 017 | Author ADR-036 — Background-job infrastructure | P2 | ✅ | 013, 014, 015, 016 | — main-only | false | FULL |
| 020 | `GET /api/admin/jobs` + status endpoint | P3 | ✅ | 013, 014, 015, 016 | **F** | true | FULL |
| 021 | `POST /api/admin/jobs/{jobId}/trigger` | P3 | ✅ | 013, 014, 015, 016 | **F** | true | FULL |
| 022 | `GET /history` + `POST /enable` + `POST /disable` | P3 | ✅ | 013, 014, 015, 016 | **F** | true | FULL |
| 023 | Migrate `PlaybookSchedulerService` to Spaarke.Scheduling | P3 | ✅ | 013, 020, 021 | — | true | FULL |
| 024 | Migrate `sprk_analysisplaybook.sprk_configjson` schedule | P3 | ✅ | 023 | — | true | STANDARD |
| 025 | Admin endpoints + scheduler integration tests | P3 | ✅ | 020-024 | — | true | STANDARD |
| 030 | `MembershipFieldDiscoveryService` | P4 | ✅ | 005, 012 | **G** | true | FULL |
| 031 | `IdentityNormalizationService` | P4 | ✅ | 012 | **G** | true | FULL |
| 032 | Define + implement `sprk_organization` user-mapping | P4 | ✅ | 012 | **G** | true | FULL |
| 033 | `MembershipResolverService` orchestration | P4 | ✅ | 030, 031, 032 | **H** | true | FULL |
| 034 | `MembershipResponse` DTO | P4 | ✅ | 030 | **H** | true | STANDARD |
| 035 | `GET /api/users/me/memberships/{entityType}` endpoint | P4 | ✅ | 033, 034 | **I** | true | FULL |
| 036 | Membership admin endpoints (discovered + refresh-metadata) | P4 | ✅ | 030 | **I** | true | FULL |
| 037 | Author ADR-034 — User-record membership pattern | P4 | ✅ | 030, 031, 032, 033 | **I** main-only | false | FULL |
| 040 | Add `LookupUserMembership = 52` to ActionType enum | P5 | ✅ | 035 | **J** | true | STANDARD |
| 041 | Implement `LookupUserMembershipNodeExecutor` | P5 | ✅ | 033, 040 | **J** | true | FULL |
| 042 | Canvas-server mapping update (client + server) | P5 | ✅ | 040, 041 | **K** | true | FULL |
| 043 | `LookupUserMembershipForm.tsx` in PlaybookBuilder | P5 | ✅ | 040 | **K** | true | FULL |
| 050 | Migrate `notification-new-documents.json` | P6 | ✅ | 041, 042 | **L** | true | FULL |
| 051 | Audit + migrate `notification-new-emails.json` | P6 | ✅ | 041, 042 | **L** | true | STANDARD |
| 052 | Audit + migrate `notification-new-events.json` | P6 | ✅ | 041, 042 | **L** | true | STANDARD |
| 053 | Migrated playbook integration tests | P6 | ✅ | 050, 051, 052 | — | true | STANDARD |
| 054 | `includeRelated` chained-discovery implementation | P6.5 | ✅ | 033, 035 | — | true | FULL |
| 055 | 1-hop max enforcement on `includeRelated` | P6.5 | ✅ | 054 | — | true | STANDARD |
| 056 | Transitive membership perf integration tests | P6.5 | ✅ | 054, 055 | — | true | STANDARD |
| 060 | P7.0 — `sprk_searchindexed` consumer inventory | P7.0 | ✅ | none | **M** main-only | false | STANDARD |
| 061 | `sprk_searchindexed` schema migration (dual-field) | P7.1 | ✅ | 060 | **N** | true | FULL |
| 062 | `DeliverToIndexNodeExecutor` dual-write | P7.1 | ✅ | 061 | **N** | true | FULL |
| 063 | Migrate UI tile consumers | P7.1 | ✅ | 061, 062 | **O** | true | STANDARD |
| 064 | Migrate FetchXML/OData query consumers | P7.1 | ✅ | 061, 062 | **O** | true | STANDARD |
| 065 | Canvas-server mapping drift integration test | P7.1 | ✅ | 042 | **O** | true | FULL |
| 066 | Author `.claude/patterns/ai/node-executor-authoring.md` | P7.1 | ✅ | 041 | — main-only | false | FULL |
| 070 | Create `sprk_userentityassociation` entity + indexes | P7.5 | ✅ | 010 | **P** | true | FULL |
| 071 | Provision Service Bus topic `sprk-membership-changes` | P7.5 | ❌ blocked-operator | 010 | **P** | true | FULL |
| 072 | `MembershipChangedEvent` payload contract | P7.5 | ✅ | 070, 071 | — | true | STANDARD | Wave 20 (2026-06-22): pure code-only contract definition (3 src + 1 test); 9/9 tests pass; publish 44.88 MB (-1.33 vs baseline). NOTE: topic-deploy task 071 still blocked-operator — Azure topic `sprk-membership-changes` does not yet exist. Publisher wiring deferred to tasks 081-083 once topic is provisioned. |
| 073 | Bicep deploy + topic/subscription smoke test | P7.5 | 🔲 | 071, 072 | — | true | STANDARD |
| 080 | P-event-1 — Event-source endpoint inventory | P8 | ✅ | 070, 071, 072 | **Q** main-only | false | STANDARD |
| 081 | Wire event-publishing into matter cluster | P8 | ✅ | 080 | **R** | true | FULL | Wave 21 (2026-06-22): authored cross-cutting IMembershipEventPublisher + real impl + NullMembershipEventPublisher (ADR-032 P2 Quiet no-op); SYMMETRIC DI registration (real when Membership:EventPublisher:Enabled=true; Null peer otherwise — default state until task 071 deploys topic). Matter cluster wired: Office QuickCreate matter endpoint publishes Added event for implicit ownerid (per inventory §3A — only BFF-side mutation site for sprk_matter). Fire-and-forget via discard (`_ = publisher.PublishAsync(...)`); endpoint succeeds even on publish failure (Q2). 10 unit tests pass. Build 0 errors / 16 warnings (no new). Publish 44.89 MB (+0.01 vs 44.88 Wave 20 baseline). Cross-cutting infrastructure ready for sibling tasks 082+083 reuse. |
| 082 | Wire event-publishing into document + event cluster | P8 | ✅ | 080, 081 | **R** | true | FULL | Wave 22 (2026-06-22): Wired MembershipChangedEvent publishing into Document + Event clusters per inventory §3B + §3C. REUSED task 081's IMembershipEventPublisher (no duplication). Document cluster: POST /api/v1/documents/ + POST /office/save (via OfficeService.SaveAsync) publish Added/ownerid; DELETE /api/v1/documents/{id} is intentional NO-PUBLISH per inventory §6.1 (DocumentEntity does not expose ownerid; nightly recon FR-2P2.7/task 085 is load-bearing path for orphan cleanup, 24h max staleness); PUT + associate-record are n/a per inventory (no identity-Lookup mutation). Event cluster: POST /api/v1/events/ publishes Added/ownerid; UPDATE/COMPLETE/CANCEL/DELETE are n/a per inventory. Q2 fire-and-forget enforced via `_ = publisher.PublishAsync(...)` discard. NFR-08 correlationId via HttpContext.TraceIdentifier on every event. ADR-032 P2 honored (Null peer when Enabled=false default). 6 new unit tests (payload-shape contracts) + 7530 total BFF tests pass / 0 failed. Build 0 errors / no new warnings. Publish ~45 MB (-1.22 vs 46.22 baseline). No new CVE. |
| 083 | Wire event-publishing into task + opportunity cluster | P8 | ✅ | 080, 081 | **R** | true | FULL | Wave 22 (2026-06-22): VERIFY-EMPTY close per task 080 inventory §3D + §3E + §6.2. Re-grep on 2026-06-22 confirmed 0 `sprk_task` + 0 `sprk_opportunity` mutation endpoints in src/server/api/Sprk.Bff.Api/ (4 grep passes, all 0 matches). No code changes; decision record at `projects/spaarke-platform-foundations-r3/notes/task-083-verify-empty.md`. FR-2P2.6 + AC-1P2.4 vacuously satisfied for empty clusters. Junction freshness for these entities handed off to task 085 (`MembershipReconciliationJob`, nightly cadence, 24h max staleness). No publish/test/CVE impact. |
| 084 | `MembershipJunctionUpdater` handler (subscription) | P8 | ✅ | 070, 071, 072 | **S** | true | FULL |
| 085 | `MembershipReconciliationJob` real logic | P8 | ✅ | 013, 070, 084 | **S** | true | FULL | Wave 22 (2026-06-22): authored MembershipReconciliationJob (IScheduledJob) + MembershipReconciliationOptions + bootstrap hosted service in MembershipModule (mirrors SchedulingModule.SchedulingBootstrapHostedService pattern). Algorithm: discover identity-Lookup fields via IMembershipFieldDiscoveryService → scan parents paginated (NotNull-OR filter) → dispatch Updated events per populated lookup → scan junction for orphans → dispatch Removed events. Lifetime Singleton + IServiceScopeFactory.CreateScope() per ExecuteAsync (mirrors PlaybookSchedulerJob task 023). REUSES task 084's IMembershipJunctionUpdater write path — no duplicated upsert logic. INDEPENDENT of task 071 topic-deploy (writes junction directly, does NOT publish). Cron `0 2 * * *` (02:00 UTC daily, configurable via `Membership:Reconciliation:CronSchedule`). 27 unit tests pass; full BFF sweep 7557 pass / 0 fail. Publish 46.23 MB (+0.01 vs 46.22 task 084 baseline). No new HIGH CVE. Load-bearing path for Q4 `sprk_assigned*` (matter), `sprk_task`, `sprk_opportunity` per inventory §3A/§3D/§3E. |
| 086 | Redis pub/sub cache invalidation | P8 | ✅ | 033, 084, 085 | — | true | FULL | Wave 23 (2026-06-22): authored MembershipCacheInvalidator + NullMembershipCacheInvalidator (ADR-032 P2 Quiet no-op) + MembershipCacheInvalidationSubscriber (IHostedService) + MembershipCacheInvalidationMessage payload record + MembershipCacheInvalidatorOptions. Channel: `membership-cache-invalidate` (configurable). Publisher wired into MembershipJunctionUpdater (constructor + post-switch fire-and-forget publish); recon path (task 085) reuses the same handler so invalidations fire from both paths automatically. Subscriber wired as dedicated HostedService (separate from MembershipResolverService for SRP) — SCAN/DEL matching cache keys `{InstanceName}membership:resolved:{personId:D}:{entityType}:*`. SYMMETRIC DI registration per bff-extensions.md §F.1: real impl wins only when `Membership:CacheInvalidator:Enabled=true` AND `IConnectionMultiplexer` resolvable; Null peer otherwise. Resilience: publisher catches RedisConnectionException + generic — logs Warning + returns (never throws); 5-min cache TTL is correctness backstop. 9 new tests (8 invalidator + 2 junction-updater coverage); full BFF sweep 7568 pass / 0 fail. Publish 46.24 MB (+0.01 vs 46.23 baseline). No new HIGH CVE. |
| 087 | Phase 2 E2E integration tests | P8 | ✅ | 081-086 | — | true | STANDARD | Wave 24 (2026-06-22): authored tests/integration/Sprk.Bff.Api.IntegrationTests/Membership/Phase2EndToEndFixture.cs + Phase2EndToEndTests.cs (12 tests — 10 in-memory + 2 live-mode scaffold) + notes/phase2-live-e2e-runbook.md. In-memory strategy per task 087 harness Option 1: CapturingMembershipEventPublisher forwards events synchronously to live MembershipJunctionUpdater via IServiceScopeFactory (simulates topic + subscription consumer in one in-process hop); SpyMembershipCacheInvalidator records invocations; Moq Loose IDataverseService backed by InMemoryDataverseState (junction store keyed on 5-tuple natural key, AAD-oid lookup, parent-entity recon scan store); StubMembershipResolverService projects from junction state for AC-1P2.6 contract assertion. AC mapping: 1P2.3 → inventory existence + ReconJob missing/orphan tests (3); 1P2.4 → OfficeQuickCreateMatter publishes (NOTE: direct publish call because MapQuickCreateEndpoints is commented out per OfficeEndpoints.cs:54-55 TODO task 026; HTTP-path TODO inline in test for swap-in when task 026 lands); 1P2.5 → handler writes + idempotent duplicate delivery (2); 1P2.6 → endpoint JSON shape unchanged from Phase 1A + ProcessedItems reported (2); 1P2.7 → invalidator invoked on junction write; 1P2.8 → Q2 fire-and-forget invariant (publish failure does NOT throw, no junction row, no invalidator call). Live-mode tests gated by SPAARKE_SB_NAMESPACE + SPAARKE_REDIS_CONNECTION env vars; auto-skip via early return when absent; runbook authored at notes/phase2-live-e2e-runbook.md for post-task-071 activation. Build: 0 errors / 0 new warnings. Tests: 12/12 pass for Phase2EndToEndTests; 78/78 full Sprk.Bff.Api.IntegrationTests suite (no regressions). Publish-size: N/A (test-only). |
| 090 | PlaybookBuilder pattern research | P9 | ✅ | 042, 043 | — main-only | false | STANDARD |
| 091 | `OutputVariable` rename guard | P9 | ✅ | 090 | **T** | true | FULL |
| 092 | Branch wiring auto-generation | P9 | ✅ | 090 | **T** | true | FULL |
| 093 | Edge perf hint advisory | P9 | ✅ | 090 | **T** | true | FULL |
| 094 | PlaybookBuilder component tests + snapshots | P9 | ✅ | 091, 092, 093 | — | true | STANDARD |
| 095 | Manual UAT — H2 scenarios in spaarkedev1 | P9 | 🔲 | 091-094 | — | true | STANDARD |
| 100 | ADR-034 final polish + INDEX update | P10 | 🔲 | 037, 087 | **U** main-only | false | STANDARD |
| 101 | ADR-036 final polish + INDEX update | P10 | ✅ | 017, 023, 025 | **U** main-only | false | STANDARD |
| 102 | Refresh `playbook-architecture.md` Known Pitfalls | P10 | ✅ | 005, 065, 066 | — | true | STANDARD |
| 103 | Refresh `sprk_matter-related-tables.md` data model doc | P10 | ✅ | 030 | — | true | STANDARD |
| 104 | Author `membership-resolution-pattern.md` architecture doc | P10 | 🔲 | 037, 087 | — | true | STANDARD |
| 110 | Project wrap-up (lessons-learned + code-review + cleanup) | P11 | 🔲 | all prior | — | false | FULL |

**Total: 69 tasks** (was estimated 50–70).

---

## Parallel Execution Groups

Tasks in the same group can run **simultaneously** once prerequisites are met. Send ONE message containing MULTIPLE `task-execute` Skill invocations — one per task in the group.

**Max concurrency: 6 agents per wave** (hard limit per pipeline §5).

**Sub-agent boundary**: Tasks touching `.claude/` paths MUST run in the **main session only** (not via Agent tool sub-agents) — pre-marked `parallel-safe: false`. Main session picks them up sequentially.

| Group | Tasks | Prerequisite | Files Touched | Safe | Concurrency |
|-------|-------|--------------|---------------|------|-------------|
| **B** | 003, 004 | 002 ✅ | Two playbook JSON migrations (disjoint files) | ✅ | 2 |
| **C-init** | 010 | none | `Spaarke.Scheduling/` new project | ❌ (blocks C, D, E) | 1 |
| **C** | 011, 012 | 010 ✅ | New contract files in `Spaarke.Scheduling/` (disjoint) + `MembershipOptions.cs` in BFF | ✅ | 2 |
| **D** | 013, 014 | 010, 011 ✅ | `ScheduledJobHost.cs` (013) + `JobRetryPolicy.cs` (014 extends 013's host) — partial overlap, careful split | ⚠️ Partial — prefer serial if uncertain | 1-2 |
| **E** | 015, 016 | 010 ✅ | Two new Dataverse entities (disjoint schema ops) | ✅ | 2 |
| **F** | 020, 021, 022 | 013, 014, 015, 016 ✅ | All in `JobsEndpoints.cs` — **file overlap risk**; mitigate by clearly-separated endpoint registration blocks | ⚠️ Serialize-or-careful | 1-3 |
| **G** | 030, 031, 032 | 005, 012 ✅ | 3 new disjoint service files in `Services/Ai/Membership/` | ✅ | 3 |
| **H** | 033, 034 | 030, 031, 032 ✅ | `MembershipResolverService.cs` + `MembershipResponse.cs` DTO (disjoint) | ✅ | 2 |
| **I** | 035, 036, 037 | 033, 034 ✅ (037 also waits on 030, 031, 032) | `MembershipEndpoints.cs` + `MembershipAdminEndpoints.cs` + ADR-034 (.claude/ — main-only) | ⚠️ 037 sequential; 035+036 parallel-safe (disjoint endpoint files) | 1-2 |
| **J** | 040, 041 | 035 ✅, 033 ✅ | `ActionType` enum + new node executor (disjoint) | ✅ | 2 |
| **K** | 042, 043 | 040, 041 ✅ | `playbookNodeSync.ts` (client) + new `LookupUserMembershipForm.tsx` (disjoint) | ✅ | 2 |
| **L** | 050, 051, 052 | 041, 042 ✅ | 3 disjoint playbook JSON files | ✅ | 3 |
| **M** | 060 | none | Discovery (read-only grep + new notes doc) | ❌ main-only | 1 |
| **N** | 061, 062 | 060 ✅ | Dataverse schema migration (061) + `DeliverToIndexNodeExecutor.cs` (062) — disjoint | ✅ | 2 |
| **O** | 063, 064, 065 | 061, 062 ✅ (065 also needs 042) | Disjoint consumer migrations + new integration test | ✅ | 3 |
| **P** | 070, 071 | 010 ✅ | New Dataverse entity + Bicep infrastructure (disjoint) | ✅ | 2 |
| **Q** | 080 | 070, 071, 072 ✅ | Discovery (read-only + new notes doc) | ❌ main-only | 1 |
| **R** | 081, 082, 083 | 080 ✅ (081 first; 082, 083 wait on 081 for `MembershipEventPublisher` creation, then parallel) | 3 disjoint endpoint cluster hookups | ✅ (after 081) | 2 (082+083 parallel after 081) |
| **S** | 084, 085 | 070, 071, 072, 084-prereqs ✅ | Junction handler + recon job (disjoint files) | ✅ | 2 |
| **T** | 091, 092, 093 | 090 ✅ | Disjoint PlaybookBuilder UI affordances (rename guard touches NodePropertiesForm + canvasValidation; branch wiring touches edges/ + ConditionEditor; perf hint touches NodeValidationBadge + canvasValidation) — `canvasValidation.ts` is shared but each rule is its own block | ⚠️ Coordinate canvasValidation.ts edits via clear region split | 2-3 |
| **U** | 100, 101 | 037, 087, 017, 023, 025 ✅ | Two ADR polish (.claude/ + docs/) | ❌ main-only sequential | 1 |

---

## Sub-Agent Write Boundary (CRITICAL)

**Tasks tagged `parallel-safe: false` due to `.claude/` write boundary** (per CLAUDE.md §3 — sub-agents CANNOT write to `.claude/` paths):

- **017** — ADR-036 author (.claude/adr/, docs/adr/)
- **037** — ADR-034 author (.claude/adr/, docs/adr/)
- **066** — node-executor-authoring pattern doc (.claude/patterns/ai/)
- **100** — ADR-034 polish (.claude/adr/)
- **101** — ADR-036 polish (.claude/adr/)

These run in main session only. Main session picks them up sequentially. If a parallel agent is accidentally dispatched to a `.claude/` task, it will fail with "Edit denied" — this is expected boundary behavior, not a bug.

**Other sequential-only tasks** (main-session, not because of `.claude/` but because of singularity):
- **010** — scaffolds the new shared lib that everything else in P2 depends on
- **060** — discovery task that produces inventory used by P7.1
- **080** — discovery task that produces inventory used by P8
- **090** — research task that informs P9 implementation
- **110** — final wrap-up; must run after everything else

---

## Critical Path

The longest dependency chain through the project:

```
001 → 002 → 003 → 005 → 030 → 033 → 035 → 040 → 041 → 042 → 050 → 053
                                                    ↓
                                                   043 → 091 → 094 → 095
                                                    ↓
                                                   054 → 055 → 056
                              030 → 037 (main)
010 → 013 → 014 → 020 → 023 → 025
                  ↓
                  015/016 → 070 → 080 → 081 → 084 → 086 → 087
                              ↓
                              071 → 072
                                          → 085 → 086
                          → 110 (final)
```

**Effective critical path** (with parallel exploitation): roughly Phase 1 (1-2 days) → Phase 2 (2-3 days) → Phase 3+4 (parallel, 3-4 days) → Phase 5+6+6.5 (parallel, 2-3 days) → Phase 7.0/7.1 + 7.5+8 (parallel, 4-6 days) → Phase 9 (2 days) → Phase 10 (1 day) → Phase 11 (1 day). **~14-21 days with parallel execution**.

**Without parallelism**: 25–35 days.

---

## High-Risk Items (See plan.md §8)

- **R1**: Phase 2 scope (P7.5 + P8) — junction + topic + handlers + recon is meaty. Phase-gate via Phase 1A.
- **R2**: `sprk_organization` mapping mechanism (task 032 defines it).
- **R3**: PlaybookBuilder UI regression (P9 — task 090 research mandatory).
- **R9**: Parallel-agent file conflicts — `relevant-files` declared per task; build verify between waves.

---

## How to Execute Parallel Groups

1. **Check all prerequisites are complete** (✅ in Status column)
2. **Send ONE message containing MULTIPLE `Skill(task-execute, ...)` invocations** — one per task in the group
3. **Each `task-execute` runs in its own subagent** with full context loading
4. **Wait for all in the group to complete**
5. **Verify build still passes** (`dotnet build` after `.cs` changes; `npm run build` after `.ts/.tsx` changes)
6. **Update statuses in this index** (🔲 → ✅)
7. **Proceed to next wave** whose dependencies are now satisfied

### Failure Isolation
- One agent failing does NOT abort the wave
- Collect all outcomes; mark failed tasks `🔄 needs retry`
- Main session decides retry-sequential vs report-and-stop

### Build Verification Between Waves (MANDATORY per pipeline §5)
After each wave:
- `.cs` modified → `dotnet build src/server/api/Sprk.Bff.Api/`
- `.ts/.tsx` modified → `npm run build` in affected package
- Build fails → STOP. Do not dispatch next wave.

---

*Generated by `/project-pipeline projects/spaarke-platform-foundations-r3 --parallel-optimized` on 2026-06-20. Updated by `task-execute` as tasks complete.*
