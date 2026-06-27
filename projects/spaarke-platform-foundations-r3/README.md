# Spaarke Platform Foundations (R3)

> **Status**: ✅ **Code-Complete** (2026-06-22 — 65/69 tasks ✅; 4 operator/human-gated follow-ups: 071 topic deploy, 073 topic smoke, 095 manual UAT, all transitively gated by 071)
> **Branch**: `work/spaarke-platform-foundations-r3`
> **Predecessor**: [`projects/spaarke-daily-update-service-r2/`](../spaarke-daily-update-service-r2/) (R2 — surfaced the gaps this project resolves)
> **Type**: Multi-part cross-cutting platform infrastructure (Part 1: User-record membership; Part 2: Background-job framework; Part 3: Playbook engine hardening)
> **Complexity**: High (3 distinct workstreams, ~50–70 tasks, touches BFF + Dataverse + Service Bus + PlaybookBuilder UI)
> **Rigor Level**: FULL (all FR/AC tasks — quality gates mandatory)

---

## Overview

R3 addresses three cross-cutting platform gaps surfaced during R2 UAT that affect every entity-aware UI surface, every Notification playbook, and every scheduled background process in Spaarke. All three ship together — same audience (BFF + playbook engine + builder), single ADR/doc update round.

### Part 1 — User-Record Membership Resolution

Replaces ad-hoc per-playbook FetchXML for "records this user is associated with" with a canonical, discovery-based `MembershipResolverService`. R3 scope (per owner decision 2026-06-20):
- **Phase 1A**: Discovery-based resolver + endpoint + admin endpoints + identity normalization + caches
- **Phase 1B**: `LookupUserMembership` playbook node executor (`ActionType = 52`) + `joinIds` Handlebars helper
- **Phase 1C**: Migrate broken playbooks (`notification-new-documents.json`, etc.)
- **Phase 1D**: Transitive memberships (`includeRelated=documents,events`, 1-hop max)
- **Phase 2**: Junction table `sprk_userentityassociation` + event-driven Service Bus sync (topic `sprk-membership-changes`) + real `MembershipReconciliationJob` + Redis pub/sub cache invalidation

### Part 2 — Background-Job Infrastructure (`Spaarke.Scheduling`)

Replaces 28 ad-hoc `BackgroundService` implementations with a shared framework:
- `Spaarke.Scheduling` library + `IScheduledJob` contract + `ScheduledJobHost`
- `sprk_backgroundjob` + `sprk_backgroundjobrun` Dataverse entities
- Cron parsing via `Cronos` NuGet
- `/api/admin/jobs/*` endpoints (list / status / history / trigger / enable / disable)
- TWO reference consumers ship in R3: `MembershipReconciliationJob` (Part 2 / Phase 2) + `PlaybookSchedulerService` migration (single row, preserves current behavior)
- Other 26 services migrate opportunistically; queue-consumers (`ServiceBusJobProcessor` family) out of scope

### Part 3 — Playbook Engine Hardening

Fixes eleven known pitfalls (G1–G11). R3 ships H1, H2, H3 workstreams (G4 = doc-only; G11/H4 deferred to R4 project `spaarke-playbook-rollout-mode-r4`):
- **H1**: `default` + `joinIds` Handlebars helpers + migrate `??`-broken playbooks + runtime unrendered-template warning
- **H2**: Builder UI affordances (OutputVariable rename guard, branch wiring auto-gen, edge perf hint) — extends existing `src/client/code-pages/PlaybookBuilder/` per-ActionType form pattern
- **H3**: Canvas-server mapping drift test in CI + `sprk_searchindexed` schema migration + node-executor authoring pattern doc

---

## Graduation Criteria

All criteria below must be met for R3 to graduate.

### Functional (51 FRs across Parts 1–3)

- [ ] **Part 1**: All 21 FRs (FR-1A.1 through FR-1D.3) deliver per [spec.md](spec.md)
- [ ] **Part 1 Phase 2**: All 9 FRs (FR-2P2.1 through FR-2P2.9) deliver per [spec.md](spec.md)
- [ ] **Part 2**: All 8 FRs (FR-2.1 through FR-2.8) deliver per [spec.md](spec.md)
- [ ] **Part 3**: All 13 FRs (FR-3H1.1 through FR-3G4) deliver per [spec.md](spec.md)

### Acceptance Criteria (40 ACs)

- [ ] All Part 1 ACs pass (AC-1A.1 through AC-1.Docs)
- [ ] All Phase 2 ACs pass (AC-1P2.1 through AC-1P2.8)
- [ ] All Part 2 ACs pass (AC-2.1 through AC-2.ADR)
- [ ] All Part 3 ACs pass (AC-H1.1 through AC-Docs)
- [ ] All cross-cutting ACs pass (AC-X.1 through AC-X.4)

### Non-Functional (8 NFRs)

- [ ] **NFR-01**: BFF publish-size delta ≤+1 MB per task; cumulative ≤60 MB (baseline ~45.65 MB; Cronos NuGet + new code estimated +1–2 MB total)
- [ ] **NFR-02**: No new HIGH-severity CVE
- [ ] **NFR-03**: Unit + integration tests per acceptance criteria
- [ ] **NFR-04**: Membership endpoint p95 ≤300ms (measured via App Insights server-side request telemetry)
- [ ] **NFR-05**: Membership cache hit ratio ≥90% steady-state
- [ ] **NFR-06**: Data-model docs (`docs/data-model/sprk_matter-related-tables.md`) reflect actual `sprk_matter` columns
- [ ] **NFR-07**: All new `IScheduledJob` implementations honor `CancellationToken`; host cancellation propagates within 30s
- [ ] **NFR-08**: Every `MembershipChangedEvent` + `sprk_backgroundjobrun` carries `correlationId`

### Documentation

- [ ] **ADR-034** — User-record membership resolution pattern (merged to `.claude/adr/` + `docs/adr/`)
- [ ] **ADR-036** — Background-job infrastructure / Spaarke.Scheduling (merged)
- [ ] **`.claude/patterns/ai/node-executor-authoring.md`** — Singleton/Scoped DI pattern doc
- [ ] **`docs/architecture/playbook-architecture.md`** — Known Pitfalls section refreshed (G1–G11 status updated; G4 added explicitly)
- [ ] **`docs/data-model/sprk_matter-related-tables.md`** — Refreshed with actual columns
- [ ] **`docs/architecture/`** — New page describing membership pattern + naming-collision disambiguation from existing `AssociationResolver` PCF
- [ ] **`notes/lessons-learned.md`** — Authored at project wrap

### Operational

- [ ] All Dataverse schema deployed to `spaarkedev1` (test) + UAT path defined for prod
- [ ] Service Bus topic `sprk-membership-changes` + subscription `recon-junction-updater` provisioned via Bicep
- [ ] `/api/admin/jobs/*` endpoints behind existing `SystemAdmin` policy (per [`AuthorizationModule.cs:241`](../../src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs#L241)) — non-admin returns 403
- [ ] Migrated playbooks (`notification-new-documents.json`, etc.) produce non-zero notifications for seeded test fixtures
- [ ] `PlaybookSchedulerService` migrated to `Spaarke.Scheduling`; all 7 notification playbooks fan out from single `notification-playbook-scheduler` job

---

## Key Files

| File | Purpose |
|---|---|
| [`spec.md`](spec.md) | AI-optimized specification (51 FRs, 8 NFRs, 40 ACs, all owner clarifications resolved) |
| [`design.md`](design.md) | Human-friendly design doc (problem statements + solution approach + alternatives considered) |
| [`README.md`](README.md) | This file (project overview + graduation criteria) |
| [`plan.md`](plan.md) | Implementation plan with phase breakdown + parallel groups |
| [`CLAUDE.md`](CLAUDE.md) | AI context loaded on every task |
| [`current-task.md`](current-task.md) | Active task state (recovery point after compaction) |
| [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) | Task registry + dependencies + parallel execution groups |
| `tasks/*.poml` | Individual task files (POML format, executed via `task-execute` skill) |

---

## How to Work on This Project

### Starting Fresh / Resuming

```
"continue with project spaarke-platform-foundations-r3"
or
"work on task 001"
```

Claude Code will auto-invoke `task-execute` per the protocol in [CLAUDE.md](CLAUDE.md).

### Parallel Execution

Tasks in the same Parallel Group (see [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md)) can run concurrently via separate `task-execute` invocations in a single message. Max concurrency: **6 agents per wave**.

### Quality Gates

Every FULL-rigor task runs:
1. `code-review` at Step 9.5 of `task-execute`
2. `adr-check` at Step 9.5 of `task-execute`
3. Build verification between waves (mandatory per pipeline)
4. BFF publish-size measurement on every BFF-touching task

### Branch Strategy

Single branch `work/spaarke-platform-foundations-r3`. Incremental commits per task; PR opens once Part 1 (Phases 1A+1B+1C) is functionally complete and demonstrable.

---

## Risks & Mitigations

| Risk | Probability | Impact | Mitigation |
|---|---|---|---|
| **Phase 2 scope creep** — junction table + event-driven sync is meaty | High | High | Phase-gated approach: Phase 1A ships independently (no junction dependency); Phase 2 is additive |
| **`sprk_organization` mapping** — Q4 confirmed `sprk_assignedlawfirm*` targets `sprk_organization`, but the user-to-organization mapping mechanism isn't defined yet | Medium | Medium | First Phase 1A task includes operator-side mapping definition; spec.md Assumption flagged |
| **PlaybookBuilder UI work (H2)** — risk of regressing existing builder | Medium | Medium | Owner directed "align with existing componentry — research first, don't invent"; H2 tasks load research before changes |
| **BFF publish-size** — adding Cronos NuGet + new services may push past 60 MB ceiling | Low | High | Per-task measurement enforced (NFR-01); Cronos ~50KB is well within budget |
| **`sprk_searchindexed` consumer migration** — unknown blast radius | Medium | Medium | P7.0 discovery task produces full inventory before migration (per owner decision) |
| **Service Bus topic provisioning** — new infra needs Bicep + deployment | Low | Low | Standard infra-as-code pattern; existing Service Bus namespace |
| **Identity normalization edge cases** — users without contacts, contacts without systemusers | Medium | Low | Each identity type resolved independently per ADR-034 contract |
| **Concurrent R2.2 hotfix work** — PR #405 ships in parallel on different branch | Low | Low | No file overlap; R2.2 is daily-briefing UX only |

---

## References

- **Design doc**: [`design.md`](design.md) (697 lines, v2 architectural sharpening)
- **Spec**: [`spec.md`](spec.md) (51 FRs, 8 NFRs, 40 ACs, all unresolved questions resolved 2026-06-20)
- **R2 predecessor**: [`projects/spaarke-daily-update-service-r2/`](../spaarke-daily-update-service-r2/) (UAT surfaced these gaps)
- **R4 successor seeds**: G11/H4 rollout-mode → `spaarke-playbook-rollout-mode-r4` (ADR-035 reserved)
- **Repo CLAUDE.md §10**: [BFF Hygiene — Binding Governance](../../CLAUDE.md#10-bff-hygiene--binding-governance-read-before-adding-to-sprkbffapi) (binding for every BFF-touching task)

---

*Initialized 2026-06-20 via `/project-pipeline projects/spaarke-platform-foundations-r3 --parallel-optimized`.*
