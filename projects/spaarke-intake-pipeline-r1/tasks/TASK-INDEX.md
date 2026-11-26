# Task Index — spaarke-intake-pipeline-r1

> **Created**: 2026-11-25 by `/project-pipeline` (task-create) · **Tasks**: 33 · **Spec**: `../spec.md` · **Plan**: `../plan.md`
> Status lives in two places (this table and each POML `<status>`). After any status write run `pwsh scripts/check-task-status-drift.ps1`.

## Tasks

| ID | Title | Phase | Status | Dependencies | Parallel | Tier / effort |
|----|-------|-------|--------|--------------|----------|---------------|
| 001 | Engine container image + ADR-004 A2 consumer entrypoint + golden-fixture test (FR-01) | 0 | 🔲 [open] | 007, 002, ext #3163 | P0-B | opus / high |
| 002 | Bicep: Container Apps env + engine Job (KEDA, UAMI, RBAC), staging container + 7-day lifecycle, engine queue (FR-01) | 0 | ✅ [done] | none | P0-A | sonnet / high |
| 003 | CI: engine image build → Trivy → ACR push (FR-01) | 0 | ✅ [done] | none | P0-A | sonnet / high |
| 004 | App-only agreement classify + edit-plan Action behind IAgreementAi facade (FR-02) | 0 | 🔲 [open] | 005, ext #3163 | P0-A2 | sonnet / high |
| 005 | Cleanup PR part 1: §10.1 items 1b, 2, 4, 5a (+ regression test), 6 (FR-03) | 0 | ✅ [done] | none | P0-A | sonnet / high |
| 006 | Cleanup PR part 2: item 7 threaded reply (createReply) + item 8 docs (FR-03) | 0 | ✅ [done] | 005 | P0-A2 | sonnet / high |
| 007 | Review and merge ADR-004 Amendment A2 (FR-04) — **main session only** (`.claude/`) | 0 | ✅ [done] | none | SERIAL | opus / high |
| 008 | Phase 0 dev deploy + engine-hosting spike report (latency, golden, Clean-copy finding) | 0 | 🔲 [open] | 001–006 | P0-C | sonnet / high |
| 010 | `sprk_servicerequest` intake columns (FR-05) | 1 | 🔲 [open] | none (coordinate ontology first) | P1-A | sonnet / high |
| 011 | `sprk_processingjob` run extension + `sprk_backgroundjob` description (FR-06) | 1 | ✅ [done] | none | P1-A | sonnet / high |
| 012 | `requestType` on RoutingContext + intake harness registry (FR-08) | 1 | ✅ [done] | 005 | P1-A | opus / high |
| 013 | Orchestrator IJobHandler: stages, resume, atomic claim, terminal-last, kill switches (FR-07) | 1 | 🔲 [open] | 004, 010, 011, 012 | P1-B | opus / high |
| 014 | Engine dispatch + completion: blob staging, ids-only dispatch, completion handler, timeout | 1 | 🔲 [open] | 013, 001, 002 | P1-C | sonnet / xhigh |
| 015 | NDA harness: outputs + clause table, check_outputs, version stamp, prior markup preserved (FR-09) | 1 | 🔲 [open] | 014, 004, 008 | P1-E | opus / high |
| 016 | Manual-triage path (FR-10) | 1 | 🔲 [open] | 013 | P1-D | sonnet / high |
| 017 | Approval: team Work Item, claim, one approve/reject endpoint, Decision Record (FR-11) | 1 | 🔲 [open] | 013, 010 | P1-C | opus / high |
| 018 | MDA Approve/Reject/Claim ribbon on `sprk_servicerequest` (FR-11) | 1 | 🔲 [open] | 017 | P1-D | sonnet / high |
| 019 | Console approval card on the Work Item (FR-11) | 1 | 🔲 [open] | 017 | P1-D | sonnet / high |
| 020 | Stage status on every step + opt-in status emails (FR-12) | 1 | 🔲 [open] | 013, 010, 006 | P1-E | sonnet / high |
| 021 | Front Door door: request + run on submit, Front Door return (FR-13) | 1 | 🔲 [open] | 013, 010 | P1-F | opus / high |
| 022 | Console "Submit as a request" + POST /api/intake/requests (FR-14) | 1 | 🔲 [open] | 013, 010 | P1-G | sonnet / high |
| 023 | Run admin endpoints + DLQ re-drive (FR-15, §10.1 item 3) | 1 | 🔲 [open] | 013, 011 | P1-D | sonnet / high |
| 024 | Run status API, internal + external (FR-16) | 1 | 🔲 [open] | 013 | P1-E | opus / high |
| 029 | Phase 1 dev deploy + end-to-end (owner OK per live action) | 1 | 🔲 [open] | 010–024 | P1-H | opus / high |
| 040 | Intake mailbox: intake flag + default type, any sender, per-sender cap (FR-17) | 2 | 🔲 [open] | 029 | P2-A | opus / high |
| 041 | Job B "Submit as request" proposal, suggest-only (FR-18) | 2 | 🔲 [open] | 029, 040 | P2-B | opus / high |
| 042 | AttachmentClassification producer at intake mailboxes (FR-19, #1920) | 2 | 🔲 [open] | 012, 040 | P2-C | sonnet / high |
| 043 | Email return: threaded reply from the shared legal mailbox (FR-20) | 2 | 🔲 [open] | 029, 006, 040 | P2-B | sonnet / high |
| 044 | MCP submit + request-status tools (FR-21) | 2 | 🔲 [open] | 029 | P2-A | sonnet / high |
| 049 | Phase 2 dev deploy + E2E | 2 | 🔲 [open] | 040–044 | P2-D | opus / high |
| 050 | Invoice harness — **HARD GATE: eBilling UAT sign-off** (FR-22) | 3 | 🔲 [open] | 029, ext UAT | P3-A | opus / high |
| 051 | Phase 3 regression (eBilling suite + E2E unchanged) + dev deploy | 3 | 🔲 [open] | 050 | P3-B | sonnet / high |
| 090 | Project wrap-up (code-review, adr-check incl. ADR-049 scope, repo-cleanup, lessons, test-diet, archive) | END | 🔲 [open] | all | END | opus / high |

## Critical Path

`005 → 004 (+ #3163) → 013 → 014 → 015 → 029 → 040 → 041/043 → 049`, with `007 → 001 → 014` joining at 014, and `008` gating 015 (the Clean-copy finding).

External gates:
- **PR #3163** (engines on master) blocks 001 and 004.
- **Ontology coordination** blocks 010 (spec Unresolved Q3).
- **eBilling UAT sign-off** (ebilling-r1 task 163) blocks 050.

## High-Risk Items

| Task | Risk | Mitigation |
|---|---|---|
| 001 / 014 | First Container Apps resource; engine runs up to 10 min exceed the 5-min maximum Service Bus lock | Wrapper uses pinned Azure SDKs with `AutoLockRenewer` and explicit dead-letter (decided 2026-11-25); CVE gate in 003; spike in 008 |
| 015 | Prior-markup preservation; Clean copy unresolved (spec Unresolved Q1) | Settled by the 008 spike with the accelerator; escalation trigger |
| 017 / 024 / 021 | Authorization (fail-closed approve, requester-scoped status, external door) | Opus tier; auth/contract integration tests |
| 040 | "Anyone" senders at intake mailboxes | Per-sender cap, kill switch, triage |
| 050 | Edits under the frozen `Services/Finance/Submission/` | Hard gate: owner confirms UAT sign-off first |

## Parallel Execution Plan

At most 6 agents per wave. Run the build check between waves.

**Phase 0**
- Wave 0-1 (parallel, 3 agents): 002, 003, 005. 007 runs in the **main session** alongside (it touches `.claude/`). — goal-eligible: NO (mixed infra/CI/cleanup; the cleanup includes deletions needing judgment)
- Wave 0-2 (parallel, 2 agents): 004, 006. Prereq: 005; 004 also needs #3163. — goal-eligible: NO (2-task wave)
- Wave 0-3 (1 agent): 001. Prereq: 007 merged, 002, #3163. — goal-eligible: NO (single task)
- Wave 0-4 (1 agent): 008. Prereq: 001–006. — goal-eligible: NO (deploy; live actions need owner OK)

**Phase 1**
- Wave 1-1 (parallel, 3 agents): 010, 011, 012. Prereq: 005 for 012; ontology coordination for 010. — goal-eligible: NO (Dataverse schema on dev needs owner OK)
- Wave 1-2 (1 agent): 013. Prereq: 004, 010–012. — goal-eligible: NO (single task, architectural)
- Wave 1-3 (parallel, 2 agents): 014, 017. Prereq: 013 (+001, 002 for 014). They touch different shared files. — goal-eligible: NO (auth)
- Wave 1-4 (parallel, 4 agents): 016, 023, 018, 019. Prereq: 013, 017. — goal-eligible: NO (023 admin authorization; 018/019 need UI verification)
- Wave 1-5 (parallel, 3 agents): 015, 020, 024. Prereq: 014, 008 (015); 006 (020). — goal-eligible: NO (auth in 024, judgment in 015)
- Wave 1-6 (1 agent): 021. Prereq: 013, 010. Serial: shares the intake DI module with 020/022. — goal-eligible: NO (single task)
- Wave 1-7 (1 agent): 022. Prereq: 021 done (shared files). — goal-eligible: NO (single task)
- Wave 1-8 (1 agent): 029. Prereq: 010–024. — goal-eligible: NO (deploy)

**Phase 2**
- Wave 2-1 (parallel, 2 agents): 040, 044. Prereq: 029. — goal-eligible: NO (2-task wave)
- Wave 2-2 (parallel, 2 agents): 041, 043. Prereq: 040. — goal-eligible: NO (2-task wave)
- Wave 2-3 (1 agent): 042. Prereq: 040 (shares `IncomingCommunicationProcessor.cs`). — goal-eligible: NO (single task)
- Wave 2-4 (1 agent): 049. Prereq: 040–044. — goal-eligible: NO (deploy)

**Phase 3** (after eBilling UAT sign-off)
- Wave 3-1: 050 → Wave 3-2: 051. — goal-eligible: NO (hard gate; deploy)

**Wrap-up**: 090 last. — goal-eligible: NO (single task)

## Parallel Execution Groups

Tasks in the same group can run simultaneously once prerequisites are met.

| Group | Tasks | Prerequisite | Files Touched | Safe to Parallelize |
|-------|-------|--------------|---------------|---------------------|
| P0-A | 002, 003, 005 | none | Bicep / workflow / BFF cleanup files | ✅ Yes |
| SERIAL | 007 | none | `.claude/adr`, `.claude/constraints`, `docs/adr` | ❌ Main session only |
| P0-A2 | 004, 006 | 005 | ConsumerTypes + facade / CommunicationService | ✅ Yes (disjoint) |
| P0-B | 001 | 007, 002, #3163 | `src/server/services/Sprk.Intake.EngineJob/` | — single |
| P0-C | 008 | 001–006 | deploy + spike report | — single |
| P1-A | 010, 011, 012 | (005 for 012) | different entities / routing | ✅ Yes |
| P1-B | 013 | 004, 010–012 | orchestrator | — single |
| P1-C | 014, 017 | 013 | JobProcessingModule vs EndpointMappingExtensions | ✅ Yes as a pair |
| P1-D | 016, 018, 019, 023 | 013, 017 | JobProcessingModule / ribbon / Console / EndpointMappingExtensions | ✅ Yes as a set |
| P1-E | 015, 020, 024 | 014, 008, 006 | harness / intake DI / EndpointMappingExtensions | ✅ Yes as a set |
| P1-F | 021 | 013, 010 | intake DI module, ExternalIntakeEndpoints | ❌ Serial (intake DI) |
| P1-G | 022 | 021 | EndpointMappingExtensions, intake DI, Console | ❌ Serial |
| P1-H | 029 | 010–024 | deploy | — single |
| P2-A | 040, 044 | 029 | IncomingCommunicationProcessor / Api/Mcp | ✅ Yes |
| P2-B | 041, 043 | 040 | proposal service / intake DI | ✅ Yes (check `<relevant-files>` at dispatch) |
| P2-C | 042 | 040 | IncomingCommunicationProcessor | ❌ Serial |
| P2-D | 049 | 040–044 | deploy | — single |
| P3-A / P3-B | 050 → 051 | 029 + UAT | Finance harness / deploy | ❌ Serial |
| END | 090 | all | project docs | — single |

**How to execute a group:**
1. Confirm all prerequisites are complete.
2. Invoke the Agent tool with one task-execute sub-agent per task, all in ONE message, each launched with model = the task's `<model-tier>`.
3. Wait for all of them to finish before starting the next group.

Shared files that serialize work: `JobProcessingModule.cs` (014, 016), `EndpointMappingExtensions.cs` (017, 022, 023, 024), the intake DI module (020, 021, 022, 040, 043), and `IncomingCommunicationProcessor.cs` (040, 042).
