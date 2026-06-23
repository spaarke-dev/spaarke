# Spaarke Platform Foundations (R3) — Lessons Learned

> **Project**: `spaarke-platform-foundations-r3`
> **Period**: 2026-06-20 (initialization) → 2026-06-22 (code-complete)
> **Final Status**: 65/69 tasks complete (~94%); 4 operator/human-gated tasks (071, 073, 095) deferred to post-deploy follow-up
> **Author**: spaarke-dev (with Claude Code as autonomous-wave executor)

---

## What Worked

### Autonomous parallel-wave execution
- **17 successful waves** (Waves 10-26) over 2 calendar days delivered 38 tasks at 3 agents per wave (sweet spot — 5 was too many, validated when Wave 5 was interrupted by user)
- **Sub-agent boundary** (sub-agents cannot write to `.claude/`) created a clean separation: sub-agents do BFF/test/code work in parallel; main session handles ADRs + patterns sequentially. This was a feature, not a constraint.
- **Dependency-graph-driven dispatch**: each wave dispatched ONLY tasks whose deps were ✅. The TASK-INDEX `parallel-safe` + `parallel-group` columns proved load-bearing.

### POML + completion-notes format
- Each POML's `<completion-notes>` block captured "what was actually built vs spec" — invaluable for downstream tasks that re-targeted off this evidence (e.g., task 062 re-targeted from `DeliverToIndexNodeExecutor` to `Spaarke.Dataverse` mapping layer per task 060+061 evidence).
- POMLs that started with explicit anti-pattern lists (e.g., 091/092/093 referenced task 090's research doc which listed 10 anti-patterns) yielded code that strictly mirrored existing PlaybookBuilder componentry per Q5 owner directive.

### Drift / inventory tasks ran FIRST in their phase
- Task 060 (sprk_searchindexed consumer inventory) BEFORE 061/062/063/064 → discovered zero in-repo readers → tasks 063/064 closed as verify-empty (saved ~4-6h of redundant work)
- Task 080 (event-source endpoint inventory) BEFORE 081/082/083 → discovered only 11/151 mutation endpoints touch identity Lookups → task 083 closed as verify-empty
- Task 090 (PlaybookBuilder pattern research) BEFORE 091/092/093 → 091/092/093 each had pre-mapped extension seams; no "invent new patterns" anti-pattern violations
- **Pattern**: every multi-task phase should start with a discovery task that gates the rest

### ADR-032 Null-Object Kill-Switch for operator-gated infrastructure
- Tasks 081 (publisher), 084 (junction updater), 086 (cache invalidator) each shipped real + Null peers behind feature flags defaulting OFF
- Code is operational TODAY without operator-deployed topic (071 ❌); operator flips flags after deploy
- **Zero deployment race**, zero NullReference, full test coverage of both branches per `bff-extensions.md` §F.1
- This pattern is now the canonical way to ship code-before-infrastructure in BFF

### Drift CI test caught pre-existing R2 defects on first run
- Task 065 (`CanvasServerMappingDriftTests`) discovered `createNotification` missing server arms — pre-existing R2 defect, not introduced by R3 — fixed in-scope (2-line patch in `NodeService.cs`)
- The drift test exists EXACTLY to catch this category; it earned its value on first run

### Background sub-agents for main-session-orthogonal work
- Tasks 102 + 104 were dispatched as background sub-agents while main session worked on `.claude/`-write tasks (101 + 100) in parallel
- True parallel execution without sub-agent boundary collision

---

## What Didn't Work

### Wave 5 rejection (5 agents at once)
- User rejected the 5-agent Wave 5 dispatch; settled on 3-agent waves going forward
- **Lesson**: 3 is the sweet spot for this codebase's complexity; coordination overhead at 5+ outweighs parallelism gain

### Pre-commit hook missed prettier dependency
- First commit attempt for Wave 11 (Wave 11 included 9 .ts/.tsx files — first wave with extensive frontend) failed because `prettier` not in PATH (root `node_modules` not installed)
- **Fix**: `npm install` at root; documented in CLAUDE.md / dev-setup guide as a one-shot
- **Lesson**: CI environment assumptions should be verified at project start, not at first commit failure

### Linter racing with main-session edits
- TASK-INDEX.md was touched by a linter several times AFTER the main session edited it, causing "file modified since read" errors on subsequent edits
- Each occurrence cost a re-read + re-edit cycle (~30s)
- **Lesson**: For high-churn shared files (TASK-INDEX.md, current-task.md), consider explicit linter coordination or smaller edit granularity

### POML naming-vs-content drift in 062
- POML named `DeliverToIndexNodeExecutor.cs` but the actual write path lived in the Spaarke.Dataverse mapping layer + RagIndexingJobHandler + RagEndpoints
- Task 060 inventory caught this; task 062 successfully re-targeted
- **Lesson**: POMLs are authored ahead of inventory; trust inventory over POML when they diverge

### 091's RenameGuardDialog.tsx + canvasStore.ts work happened pre-compact
- The first session before compaction partially completed task 091; the POML wasn't updated to ✅
- Post-compact, the code existed but the POML was still 🔲, so we re-dispatched 091 — but the code was already in place
- Resolved by treating the existing code as the result of an earlier wave and just closing the POML + INDEX
- **Lesson**: Always commit + update POML status atomically; partial state across compaction is brittle

### Manual UAT (task 095) deferred indefinitely
- Manual UAT requires human in spaarkedev1; we cannot run it autonomously
- Listed as 🔲 throughout, no autonomous path to ✅
- **Lesson**: Project plans should distinguish "code-complete" gates from "human-validation" gates; both are valid graduation criteria, but they ship on different timelines

---

## What to Do Differently Next Time

1. **`npm install` at project init** for any project that touches `.ts/.tsx` (root package.json must be installed so lint-staged hooks resolve)
2. **Inventory-first phase pattern**: every multi-task phase should start with a discovery/research task; the rest of the phase tasks reference its evidence
3. **ADR-032 Null-Object pattern as the default** for any code shipping ahead of infrastructure deployment (don't pause on operator deploy; ship code + kill-switch)
4. **Smaller wave sizes (3)** are the sweet spot for this codebase complexity
5. **Use background sub-agents** for main-session-orthogonal work (.claude/ + docs/ in parallel)
6. **POML reality check**: every POML's named-files claim should be validated against the actual code surface during context-loading; if mismatch, re-target before implementing
7. **Manual UAT as separate graduation milestone** — distinguish "code-complete" from "validated in test environment" in project plans

---

## Key Architectural Decisions Locked In

| Decision | Source | Status |
|---|---|---|
| Service Bus topic + subscription-per-consumer (NOT queue, NOT reuse) | D3 owner clarification | Bicep authored; deploy gated |
| Fire-and-forget event publishing + nightly recon backstop | Q2 owner clarification | Shipped (tasks 081/082 + 085) |
| 1-hop max on `includeRelated` (4xx on multi-hop) | Q3 owner clarification | Shipped (task 054 + 055 verify-only) |
| `sprk_assignedlawfirm1/2` → `sprk_organization` (NOT Contact as design.md showed) | Q4 owner clarification | Shipped (tasks 032 + 103) |
| Extend PlaybookBuilder componentry (NOT new patterns) | Q5 owner clarification | Shipped (tasks 091/092/093 + 090 research) |
| Existing `SystemAdmin` policy (NOT new "PlatformAdmin") | Q6 owner clarification | Shipped (all admin endpoints) |
| Fresh `correlationId` per child playbook | Q1 owner clarification | Shipped (PlaybookSchedulerJob) |
| Phase 1D transitive memberships in-scope for R3 | Owner promoted from design-only | Shipped (task 054) |
| Phase 2 full junction + topic + recon in-scope for R3 | Owner promoted from design-only | Code-complete; runtime gated |
| Option (b) config-driven Lookup for `sprk_organization` user mapping | task-032 decision | Shipped |
| ADR-032 Null-peer pattern for operator-gated infrastructure | New canonical pattern from R3 | 3 reference implementations (tasks 081 + 084 + 086) |

---

## Critical Findings to Preserve

1. **A1-class over-disclosure defect in playbooks 051/052**: two existing playbooks had ZERO membership filter — would have returned tenant-wide records on dispatch. Both migrated in R3. R4 SHOULD audit all solution-exported playbooks for this class.

2. **Zero in-repo readers of `sprk_searchindexed`** (task 060 inventory): all readers are maker-side. Escalated as operator follow-up. Document the maker-side cleanup BEFORE removing the legacy field in a future R-iteration.

3. **G6 drift defect** (`createNotification` missing server arms): pre-existing R2 issue caught by task 065 drift test on first run. Fixed in-scope. The drift test is now the binding regression guard for canvas↔server mapping.

4. **The 8 Q4 `sprk_assigned*` fields are NOT mutated by ANY BFF endpoint** (task 080 inventory): they're exclusively maker-portal edits. The nightly `MembershipReconciliationJob` (task 085) IS the freshness path for matter-assignment junctions — NOT real-time events. This was a load-bearing discovery; the recon job was correctly elevated from "backstop" to "primary" for these fields.

5. **Task 071 Service Bus topic Bicep** is the project's critical path: until operator deploys, 4 tasks remain operator/human-gated (071, 073, 095) and full E2E live verification is deferred. All other R3 code ships behind ADR-032 kill-switches (default OFF) so the BFF runs cleanly today.

---

## Operator Follow-Up Checklist (post-R3 merge)

- [ ] Deploy `infrastructure/bicep/modules/membership-topic.bicep` per `notes/operator-followup-task071.md`
- [ ] Verify topic + subscription provisioned + BFF MI Sender/Receiver RBAC
- [ ] Flip 3 ADR-032 kill-switch flags to `true`:
  - [ ] `Membership:EventPublisher:Enabled = true`
  - [ ] `Membership:JunctionUpdater:Enabled = true`
  - [ ] `Membership:CacheInvalidator:Enabled = true` (if Redis ConnectionString configured)
- [ ] Mark task 071 ✅ in TASK-INDEX (currently `❌ blocked-operator`)
- [ ] Run task 073 (topic/subscription smoke test) per its POML
- [ ] Run task 095 (manual UAT — H2 scenarios in spaarkedev1)
- [ ] Audit maker-side Dataverse artifacts (forms/views/flows) for `sprk_searchindexed` consumers per task 063/064 escalation
- [ ] Audit solution-exported playbooks for A1-class membership-filter omissions per tasks 051/052 finding

---

*Authored 2026-06-22 by task 110 wrap-up. Predecessor reference: `projects/spaarke-daily-update-service-r2/notes/lessons-learned.md`.*
