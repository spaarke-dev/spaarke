# sdap-bff-api-remediation-fix — AI Context

> **Purpose**: This file provides context for Claude Code when working on the BFF API remediation project.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: **Phase 4 BLOCKED on 48h gate + G4 agreement** (Phase 0/1/2/3 + task 019 + task 023 done; BASELINE.md committed 2026-05-25; old Windows dev decommissioned)
- **Last Updated**: 2026-05-25
- **Current Task**: none — Phase 3 closed (8 of 9 tasks done; task 033 calendar gate runs to 2026-05-27 UTC); awaiting operator G4 facade adoption agreement with Insights Engine owner before Phase 4 task 046
- **Next Action**: WAIT for (a) 48h App Insights window closure (2026-05-27 UTC) → capture per `baseline/app-insights-baseline-start.md`, then (b) G4 agreement. When both clear, resume with `/task-execute projects/sdap-bff-api-remediation-fix/tasks/040-publish-linux-x64.poml`. See [`current-task.md`](current-task.md) Action 1/2/4 for full sequence.

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) — AI-optimized specification (308 lines; permanent reference)
- [`design.md`](design.md) — Full design document (594 lines; rationale + decisions)
- [`approach.md`](approach.md) — Upstream record (2026-05-19; preserved)
- [`README.md`](README.md) — Project overview + graduation criteria
- [`plan.md`](plan.md) — Implementation plan + WBS + **Discovered Resources** (§2)
- [`current-task.md`](current-task.md) — Active task state (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — Per-phase task tracker

### Project Metadata
- **Project Name**: sdap-bff-api-remediation-fix
- **Type**: Backend remediation (BFF API) + documentation codification
- **Complexity**: High — 5 outcomes × 7 phases × 63 tasks (revised 2026-05-24); 4–6 week calendar
- **Module touched**: `src/server/api/Sprk.Bff.Api/`
- **Sub-agent write boundary**: All `.claude/` updates (Phase 6) are main-session-only

---

## Context Loading Rules

When working on this project, Claude Code should:

1. **Always load this file first** when starting work on any task
2. **Check current-task.md** for active work state (especially after compaction/new session)
3. **Reference spec.md and design.md** for design decisions, requirements, and acceptance criteria — `design.md` has the "why", `spec.md` has the "what"
4. **Load the relevant task file** from `tasks/` based on current work
5. **Apply ADRs** listed below (loaded automatically via `adr-aware` + tag mapping)
6. **Load `.claude/constraints/bff-extensions.md`** — binding for every BFF-touching task per root CLAUDE.md §10

**Context Recovery**: If resuming work, see [Context Recovery Protocol](../../docs/procedures/context-recovery.md)

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

### Auto-Detection Rules (Trigger Phrases)

When you detect these phrases from the user, invoke task-execute skill:

| User Says | Required Action |
|-----------|-----------------|
| "work on task X" | Execute task X via task-execute |
| "continue" | Execute next pending task (check TASK-INDEX.md for next 🔲) |
| "continue with task X" | Execute task X via task-execute |
| "next task" | Execute next pending task via task-execute |
| "keep going" | Execute next pending task via task-execute |
| "resume task X" | Execute task X via task-execute |
| "pick up where we left off" | Load current-task.md, invoke task-execute |

**Implementation**: When user triggers task work, invoke Skill tool with `skill="task-execute"` and task file path.

### Why This Matters

The task-execute skill ensures:
- ✅ Knowledge files are loaded (ADRs, constraints, patterns)
- ✅ Context is properly tracked in current-task.md
- ✅ Proactive checkpointing occurs every 3 steps
- ✅ Quality gates run (code-review + adr-check) at Step 9.5 for FULL-rigor tasks
- ✅ Progress is recoverable after compaction

**Bypassing this skill leads to**:
- ❌ Missing ADR constraints (especially refined ADR-013 facade requirement)
- ❌ No checkpointing — lost progress after compaction
- ❌ Skipped quality gates on Phase 4 code changes

### Parallel Task Execution

When tasks can run in parallel (no dependencies), each task MUST still use task-execute. Send one message with multiple Skill tool invocations. For this project, **parallel candidates exist in Phase 0 (UQ coordination), Phase 1 (inventory commands), and Phase 4 Outcome E migrations (Group F)** — see TASK-INDEX.md "Parallel Execution Groups" table.

**Phase 6 exception**: All Phase 6 tasks touch `.claude/` paths and are `parallel-safe: false`. They MUST run sequentially in the main session per the sub-agent write boundary (root CLAUDE.md §3).

### 🚨 MUST: Multi-File Work Decomposition

For tasks modifying 4+ files (notably tasks 047–050 for facade migration), Claude Code MUST:

1. **Decompose into dependency graph**:
   - Group files by consumer module (Finance, Workspace, Jobs, Dataverse, Filters)
   - Identify shared dependencies (facade interfaces must exist before migrations)
   - Separate parallel-safe work from sequential

2. **Parallelize when**:
   - Files are in different consumer modules → CAN parallelize (Finance vs Workspace vs Dataverse)
   - Files have no shared interfaces beyond the facade → CAN parallelize after task 046 completes

3. **Serialize when**:
   - Task 046 (facade interfaces) MUST complete before tasks 047–050 can compile
   - Task 051 (handler relocation) MUST run after 047–050 are verified stable

See [task-execute SKILL.md Step 8.0](../../.claude/skills/task-execute/SKILL.md) for complete protocol.

---

## Key Technical Constraints

**Binding ADR rules** (see plan.md §2 for full table with binding-strength column):

*Technical invariants (system-enforced):*
- ✅ MUST keep Kiota packages version-matched (currently `1.21.2`) — chain-mismatch causes runtime errors
- ✅ MUST preserve `IJobHandler<T>` interface + JobType string dispatch (ADR-004)
- ❌ MUST NOT set `<PublishTrimmed>` or `<PublishAot>` — reflection-hostile to Graph SDK / Identity.Web / EF / DI / serializers

*Project disciplines (measurably enforced via tasks):*
- ✅ MUST route external CRUD→AI consumers through `Services/Ai/PublicContracts/` facade (refined **ADR-013**; FR-E1/FR-E2 enforce; FR-E2 grep acceptance at task 053)
- ✅ MUST stay within ADR-010 DI registration delta of +4 to +8 vs Phase 3 baseline (task 038 baseline; task 054 verification)
- ✅ MUST publish to `deploy/api-publish/` (NOT `/tmp` — anti-pattern #16)
- ✅ MUST use `Deploy-BffApi.ps1` for all BFF deploys (hash-verify + health-check + slot-swap rollback)
- ✅ MUST load [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) for any BFF-touching task

*Project-scope bindings (apply during this remediation only):*
- ❌ MUST NOT move BFF endpoints to Functions during remediation (would introduce second moving variable into Phase 4 bakes — post-project move is separate design)
- ❌ MUST NOT add new direct CRUD→AI dependencies (use facade)
- ❌ MUST NOT bump Kiota individually, Graph SDK, .NET TFM, or pre-release AI packages

*Operational:*
- ✅ Phase 1–4 work targets dev subscription only (`spe-api-dev-67e2xz`); demo/prod isolated per ADR-027(a)

**Code-State Deltas from Spec** (verified during pipeline pre-flight):

1. **CRUD→AI count is ~59 files / 148 occurrences**, not the spec's "20". Per-folder reality:
   - Finance: 3 files ✓ matches spec
   - Workspace: 2 files (spec said 4)
   - Jobs: 2 files (spec said 6)
   - Dataverse: 0 files (spec said 2)
   - Api/Filters: 1 file (spec said 5+)
   - Api/Endpoints: 6 additional files
   - Services/Ai/: 29 internal files (do NOT count — internal coupling is fine)
   - Infrastructure/DI: 4 modules

   **Resolution (PF-3)**: Outcome E task scope defers to Phase 1 inventory output. Tasks 047–050 reference "inventory-derived list" not spec's "20". `spec.md` left intact; Phase 1 is source of truth.

2. **`Services/Ai/Handlers/` already exists** with `GenericAnalysisHandler`. The new `Services/Ai/Jobs/` (FR-E3 target) is a **distinct sibling directory** — task 051 explicitly clarifies this distinction.

3. **4 `.map` files** in `wwwroot/playbook-builder/assets/`:
   - `flow-vendor-BHHmI87s.js.map`
   - `fluent-vendor-CmJVTK5h.js.map`
   - `index-BWeOj5bW.js.map`
   - `react-vendor-BWFb42Va.js.map`

4. **csproj currently has NO `<RuntimeIdentifier>`**, no `<PublishTrimmed>`, no `<PublishAot>` — FR-A1 cleanly applies as a NEW publish flag, not a modification.

5. **Pre-release pins exact match**: `Azure.AI.Projects 1.0.0-beta.8`, `Microsoft.Agents.AI 1.0.0-rc1`, `Azure.AI.OpenAI 2.8.0-beta.1`. Inline csproj rationale comments are current.

6. **Confirmed active vulnerability** (build-warning observed at pipeline pre-flight): `NU1903 — Microsoft.Kiota.Abstractions 1.21.2 has a known HIGH severity vulnerability` (GHSA-7j59-v9qr-6fq9). This is Outcome B target #1 — task 011 will enumerate the full scan; task 043 will be the patch task.

---

## Decisions Made

<!-- Log key architectural/implementation decisions here as project progresses -->
<!-- Format: Date, Decision, Rationale, Who -->

- **2026-05-20**: Keep AI in BFF (no extraction). Rationale: latency budgets <50ms/<100ms/<500ms; transactional Cosmos coupling; 100% streaming AI per extraction assessment. — Refined ADR-013 + assessment.
- **2026-05-20**: Outcome E uses small focused facade interfaces (`IBriefingAi`, `IInvoiceAi`, `IRecordMatchingAi`, `IWorkspacePrefillAi`) per UQ-07 default. Owner confirms in Phase 0 task 007.
- **2026-05-20**: Master pulled into work branch at pipeline pre-flight (PF-1 resolution). Build verified passing (0 errors, 17 warnings).
- **2026-05-20**: **Sequencing decision (owner)** — `spaarke-ai-platform-unification-r2` finishes initial test-feedback refinements FIRST; THEN this BFF remediation project starts Phase 0. Rationale: r2 just deployed and is in active refinement testing on the same dev environment (`spe-api-dev-67e2xz`); r2 touches many Outcome E target files (`Services/Ai/Chat/`, `Api/Ai/ChatEndpoints.cs`, etc.); Phase 3 48h App Insights baseline + Phase 4 24-48h bake windows need a quiet dev environment. Sequential avoids merge conflicts in Outcome E and ensures clean observation windows. Operator signals "r2 refinement complete" before invoking `/task-execute 001`.

- **2026-05-24**: **Senior architect review applied (8 Must items + dual-approver removal)**. Reviewer findings at `~/.claude/plans/merry-popping-meerkat.md`. Changes:
  - **NEW FR-C6** + new task 082: CI gate blocks direct CRUD→AI dependency injection (owner-binding per M5). Converts Outcome E from one-time refactor into permanent architectural boundary.
  - **NEW task 009** rollback drill (G5): wall-clock NFR-06 verification before Phase 4 begins; operator-only.
  - **Outcome E squash** (G9 owner-binding): tasks 046–051 committed individually but bundled into single atomic PR for clean rollback. See plan.md PR-2.
  - **Dependabot deferral rule** (G8): plan.md PR-1 — Dependabot PRs touching BFF csproj auto-deferred to weekly owner triage.
  - **G1 handler reconciliation**: FR-E3 6-handler list is non-binding; Phase 1 inventory produces authoritative list per "AI-coupled rule" (handler references `Services.Ai.*` AND not CRUD-coupled). Task 051 uses inventory output.
  - **G2 grep scope**: FR-E2 acceptance grep is production-scope-only (excludes tests, DI module special-cased). Task 053 updated.
  - **G3 PR template**: task 074 bundles `.github/pull_request_template.md` refresh — adds ADR-013/028/029, Justification section, label-gating instructions.
  - **G4 Insights Engine facade agreement**: task 005 produces written agreement for Insights Engine PRs post-task-046 to use facade. Belt-and-suspenders to FR-C6.
  - **M3 expanded BFF-coordination scope**: task 004 renamed + scope expanded to enumerate ALL active BFF-touching projects (not just perf-enhancement). Per `git worktree list` + memory.

- **2026-05-24**: **Operator-only approval model (NFR-08 revised; UQ-01 RESOLVED)**. Rationale: Spaarke uses AI-directed coding procedures (`task-execute` invokes `adr-check` + `code-review` at Step 9.5 FULL rigor) + mechanical CI gates (FR-C1–C6). Combined, these provide the "second pair of eyes" doing technical verification. The owner provides judgment + sign-off. Dual-approver enterprise pattern is unnecessary friction for single-owner operating model. Applied to spec.md NFR-08, plan.md PR-3, all Phase 4/5 task POMLs. Task 002 repurposed from "designate dual approver" to "document operator-only model."

- **2026-05-24**: **r3 sequencing dependency resolved — Phase 0 unblocked.** `work/spaarke-ai-platform-unification-r3` completed refinement testing (15 commits closing tasks 126-140: Calendar widget UX work) and merged to master at commit `8acf9bc7`. This worktree re-synced with new master; build verified (0 errors, 17 warnings — unchanged). The 2026-05-20 sequencing decision is satisfied. Phase 0 is now ready to begin when operator invokes `/task-execute projects/sdap-bff-api-remediation-fix/tasks/001-owner-signoff-resolved-decisions.poml`.

- **2026-05-24**: **`feature/production-environment-setup-r2` branch abandoned (housekeeping).** Encountered the branch during master-sync audit (1 stranded commit from 2026-04-24: docs consolidation of ENVIRONMENT-DEPLOYMENT-GUIDE + PRODUCTION-DEPLOYMENT-GUIDE → SPAARKE-DEPLOYMENT-GUIDE). Project itself was completed 2026-03-20; this was a post-completion docs cleanup that was never finished. Test merge revealed 4 conflicts including 2 modify/delete on the source guides (master updated them in past 4 weeks via R2 docs refactoring + auth-doc-drift). Effort to integrate: ~70 min of careful porting. Decision: abandon (delete branch + worktree). If consolidation still wanted, redo fresh against current master state. Not blocking this BFF project (docs-only). Remote branch deleted; worktree tracking pruned 2026-05-24.

---

### Phase 1 Inventory Findings (tasks 010–018 — completed 2026-05-24)

**6 critical findings** documented in [`inventory/INVENTORY.md`](./inventory/INVENTORY.md). Summary:

1. **🚨 dev/prod App Service OS mismatch** — dev (`spe-api-dev-67e2xz`) is **Windows** (`reserved: false`, .NET 8 on P2v3); prod (`spaarke-bff-prod`) is **Linux**. design.md §2.4 "App Service is unambiguously Linux" is wrong for dev. **FR-A1 (`--runtime linux-x64`) needs revision**: split per-env, consolidate dev to Linux, or accept multi-RID. Phase 2 candidate categorization MUST address this.

2. **🚨 Demo App Service does not exist** — Azure subscription enumeration found only dev + prod. No `spaarke-demo`. Phase 5 task 060 ("Deploy cumulative changeset to spaarke-demo") needs operator clarification: provision demo OR scope-down to dev→prod direct.

3. **🚨 HIGH Kiota CVE blocked by current scope** — Microsoft.Kiota.Abstractions 1.21.2 has GHSA-7j59-v9qr-6fq9 (HIGH). Latest is 2.0.0 (major bump); requires Microsoft.Graph 5.101.0 → 6.x. Spec §Out of Scope forbids both. Phase 2 must decide: revisit scope OR accept risk pending separate Graph SDK upgrade project. Also 2 HIGH on transitive `System.Security.Cryptography.Xml 8.0.1` may be fixable via IdentityModel bumps.

4. **FR-A3 is already a no-op** — Cosmos `ServiceInterop.dll` is NOT FOUND in current publish. Upstream Cosmos SDK 3.47.0 resolved the historical duplication. Phase 4 task 042 may drop entirely.

5. **All 3 pre-release pinning rationales remain valid** — Azure.AI.OpenAI, Microsoft.Agents.AI, Azure.AI.Projects chain pins all hold per FR-B3 re-verification.

6. **All 4 zero-static-usage packages confirmed live** — Microsoft.Agents.AI, Microsoft.Agents.Hosting.AspNetCore, Microsoft.Extensions.Http.Polly, OpenTelemetry verified via DI registration grep + presence in Sprk.Bff.Api.deps.json (526 entries). Phase 2 must mark all 4 KEEP regardless of static-grep zero finding.

**Key metrics (Phase 1 baseline)**:
- Compressed publish: 75.2 MB (target ≤ 60 MB; -15.2 MB over baseline; matches 2026-05-19 drift point exactly)
- Uncompressed publish: 212 MB (target ≤ 150 MB; -62 MB over)
- 44 direct packages / 526 deps.json entries / 287 files / 216 DLLs
- 4 sourcemap files in `wwwroot/playbook-builder/assets/` (FR-A2 target)
- 10 RIDs in `runtimes/` totaling ~77 MB native (FR-A1 trim opportunity: ~54-67 MB savings depending on target OS)

**Phase 2 input ready**: Phase 2 candidate categorization tasks (020-022) consume this inventory + 6 critical findings.

### Phase 0 Group A Decisions (tasks 002–007 — completed 2026-05-24)

- **Task 002 — UQ-01 RESOLVED**: Operator-only approval model documented. spec.md NFR-08 (revised) + UQ-01 (RESOLVED) verified accurate. Phase 0 checklist item 6 marked N/A via override. Verification rigor comes from: (a) AI-directed `adr-check` + `code-review` at task-execute Step 9.5, (b) CI guards FR-C1–C6 (mechanical), (c) owner judgment + sign-off authority.

- **Task 003 — UQ-02 = YES**: Canonical prod deploy process EXISTS at [`docs/procedures/production-release.md`](../../docs/procedures/production-release.md) (Last Updated 2026-04-06; Owner: Platform Operations). Drives 3 deployment tracks: Dataverse (SpaarkeMaster solution via `pac`), Azure (BFF API via `Deploy-BffApi.ps1` + Office Add-ins via `Deploy-OfficeAddins.ps1`), Reference Data (playbooks, chat context, Copilot agent). Sequential per-environment; gated by `Validate-DeployedEnvironment.ps1`; tagged via git tag at completion. Master orchestration: `scripts/Deploy-Release.ps1`. **Tasks 062 + 063 stay in scope.** Phase 5 includes prod deploy + 7-day observation as originally designed.

- **Task 004 — UQ-03 expanded (M3)**: 24 active worktree branches enumerated; ALL at 0 commits ahead of master at Phase 0 close. No unmerged BFF work exists. Dev environment (`spe-api-dev-67e2xz`) is effectively quiet for Phase 3 baseline capture. **Operator followup commitment (residual risk)**: New BFF-touching work CAN start in any branch during the 4-6 week project window. Operator commits to: (a) run `git log master..<branch> -- src/server/api/Sprk.Bff.Api/` weekly during Phase 3-4 to surface new entrants, (b) coordinate sequencing if any branch starts BFF-touching work mid-bake. Not a hard blocker for Phase 0 gate.

- **Task 005 — UQ-04 + G4**: Insights Engine project (`work/ai-spaarke-insights-engine-r1`) verified pre-implementation (0 commits ahead of master). **Baseline window decision**: capture Phase 3 baseline NOW (pre-integration) — Engine has not started integration so no contamination risk. **G4 facade adoption agreement**: PENDING WRITTEN AGREEMENT — operator must contact Engine owner to confirm any Engine PR merged after this project's Phase 4 task 046 (facade creation) uses `Services/Ai/PublicContracts/` facade rather than direct `IOpenAiClient`/`IPlaybookService` injection. Belt-and-suspenders to FR-C6 CI gate (lands in Phase 6 task 082). Flagged as operator action item before Phase 4 task 046 starts; not blocking Phase 0 close.

- **Task 006 — UQ-05 CI guard size ceiling = baseline + 10%** (design.md §3 default applied; owner ACK via task 001). Baseline captured in Phase 3 task 035; current pre-Outcome-A baseline is ~75.2 MB compressed (per 2026-05-24 smoke-test deploy). If Phase 3 baseline after Outcome A SAFE candidates is ~60 MB, ceiling = ~66 MB. Consumable by Phase 6 task 070 (`Deploy-BffApi.ps1` hard-fail size guard) and CI step (FR-C5). Owner reserves right to tighten to +5% after Outcome A measurable results.

- **Task 007 — UQ-06, UQ-07, PF-3, G1**:
  - **UQ-06**: `Spaarke.Core` + `Spaarke.Dataverse` package lists = **inventory-only** (default). No pruning in this project. Wider audit is a separate follow-up project.
  - **UQ-07**: Outcome E facade granularity = **small focused interfaces** per design default. Phase 4 task 046 creates `IBriefingAi`, `IInvoiceAi`, `IRecordMatchingAi`, `IWorkspacePrefillAi` (final list may evolve based on Phase 1 inventory).
  - **PF-3**: Outcome E task scope defers to Phase 1 inventory output (CRUD→AI consumers count ~59 files / 148 occurrences, not spec's 20). Tasks 047–050 use inventory-derived consumer list. Spec.md left intact; Phase 1 inventory is authoritative.
  - **G1 handler reconciliation**: AI-coupled rule defined per FR-E3 revised — a handler is AI-coupled if it references `Sprk.Bff.Api.Services.Ai.*` AND does NOT require `Spaarke.Dataverse` / `Microsoft.Xrm.Sdk` namespaces. Preliminary 6-handler list in initial design is **non-binding**. Task 051 commits to using Phase 1 inventory output (task 015 reflection probe + task 014 static usage map) as the authoritative handler list.

---

## Implementation Notes

<!-- Add notes about gotchas, workarounds, or important learnings during implementation -->

- **Sub-agent write boundary** (root CLAUDE.md §3): Sub-agents cannot edit `.claude/` paths. Phase 6 tasks (070–081) — except 070 (scripts/) and 071–075 (.github/) — are all main-session-only.
- **Distinct directories**: `Services/Ai/Handlers/` (existing, with `GenericAnalysisHandler`) ≠ `Services/Ai/Jobs/` (NEW per FR-E3). Task 051 documentation must make this clear in the commit message and the BFF CLAUDE.md update (task 081).
- **Phase 4 bake windows**: 24h dev / 48h demo / 7d prod. Operator-paced; not autonomous. Each Phase 4 candidate task includes the bake window in its acceptance criteria.
- **Active vulnerability discovered at pre-flight**: `Microsoft.Kiota.Abstractions 1.21.2` has HIGH NU1903. Pinning rationale (CLAUDE.md line on Kiota chain-lock) means task 043 must coordinate the upgrade with `Microsoft.Graph` SDK chain.
- **Dependabot PR coordination**: 15+ open Dependabot PRs touch `Sprk.Bff.Api/` (PR #289 Microsoft.Agents.AI, #266 DocumentFormat.OpenXml, #248 Azure.Security.KeyVault.Secrets, etc.). Phase 1 inventory should reconcile against these (task 011 — outdated/vuln scan) before patching to avoid PR conflicts.

---

## Resources

### Applicable ADRs

| ADR | Title | Relevance |
|-----|-------|-----------|
| [ADR-001](../../.claude/adr/ADR-001-minimal-api.md) | Minimal API + Workers | **Project-scope binding**: do not move endpoints to Functions during remediation (out-of-band Functions unaffected; post-project move is separate design) |
| [ADR-004](../../.claude/adr/ADR-004-job-contract.md) | Job Contract | **Technical invariant**: FR-E3 preserves `IJobHandler<T>` + JobType string dispatch |
| [ADR-007](../../.claude/adr/ADR-007-spefilestore.md) | SpeFileStore Facade | **Pattern reference**: canonical model for Outcome E facade (task 046) |
| [ADR-008](../../.claude/adr/ADR-008-endpoint-filters.md) | Endpoint Filters | **Preserved by no-change** (filter signatures unchanged in task 050) |
| [ADR-010](../../.claude/adr/ADR-010-di-minimalism.md) | DI Minimalism | **Measurable project binding**: known violation (99+ vs ≤15) is out of scope; task 038 captures baseline, task 054 verifies Phase 4 delta within expected +4 to +8 (Outcome E facade adds registrations; consumer-side dependencies go down separately) |
| [ADR-013](../../.claude/adr/ADR-013-ai-architecture.md) | AI Architecture (refined 2026-05-20) | **Technical binding for FR-E1/FR-E2 facade requirement** |
| [ADR-027 (a)](../../.claude/adr/ADR-027-subscription-isolation-managed-solutions.md) | Subscription Isolation | **Real, operational**: Phase 1–4 hits dev subscription only; demo/prod target their own subscriptions |
| [ADR-027 (b)](../../.claude/adr/ADR-027-subscription-isolation-managed-solutions.md) | Managed Solutions | **Conditionally applicable**: applies to Power Platform components (not this BFF App Service deploy directly); relevant only if canonical Phase 5 prod process bundles related Power Platform updates |
| [ADR-028](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) | Spaarke Auth v2 | **Preserved by no-change** |
| ADR-029 (forthcoming) | BFF Publish Hygiene | **Becomes binding when Phase 6 lands** (tasks 076/077) |

### Applicable Constraints

- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — **binding pre-merge checklist for all BFF additions**
- [`.claude/constraints/azure-deployment.md`](../../.claude/constraints/azure-deployment.md) — publish location, baseline size, stdout logging; FR-D2 adds Publish Hygiene
- [`.claude/constraints/api.md`](../../.claude/constraints/api.md), [`ai.md`](../../.claude/constraints/ai.md), [`jobs.md`](../../.claude/constraints/jobs.md) — tag-mapped per task

### Applicable Skills

- [`bff-deploy`](../../.claude/skills/bff-deploy/SKILL.md) — canonical BFF deploy (Phase 4/5 tasks); FR-D3 updates it
- [`task-execute`](../../.claude/skills/task-execute/SKILL.md) — invokes adr-aware + code-review + adr-check at Step 9.5
- [`adr-aware`](../../.claude/skills/adr-aware/SKILL.md), [`adr-check`](../../.claude/skills/adr-check/SKILL.md), [`code-review`](../../.claude/skills/code-review/SKILL.md)
- [`repo-cleanup`](../../.claude/skills/repo-cleanup/SKILL.md) — wrap-up task 090

### Knowledge / Patterns

- [`src/server/api/Sprk.Bff.Api/CLAUDE.md`](../../src/server/api/Sprk.Bff.Api/CLAUDE.md) — module guidance (FR-D7 updates it)
- [`src/server/api/Sprk.Bff.Api/Services/SpeFileStore.cs`](../../src/server/api/Sprk.Bff.Api/Services/SpeFileStore.cs) — **canonical facade-over-Graph-SDK pattern** (model for task 046)
- [`docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`](../../docs/assessments/bff-ai-extraction-assessment-2026-05-20.md) — evidence base
- [`docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md`](../../docs/architecture/INSIGHTS-ENGINE-ARCHITECTURE.md) §22.2 — extraction-trigger context
- [`docs/standards/ANTI-PATTERNS.md`](../../docs/standards/ANTI-PATTERNS.md) #16 — `/tmp` publish anti-pattern
- [`docs/guides/auth-deployment-setup.md`](../../docs/guides/auth-deployment-setup.md) §9 — endpoint smoke-test source list
- [`.claude/FAILURE-MODES.md`](../../.claude/FAILURE-MODES.md) — AP-1 / G-2 / G-3 (existing); FR-D4 adds new entry

### Related Projects

- `sdap-bff-api-and-performance-enhancement-r1` — active; coordinate to avoid in-flight BFF deploy during Phase 4 (UQ-03)
- `ai-spaarke-insights-engine-r1` — pre-implementation; coordinate baseline window (UQ-04)
- `spaarke-ai-platform-unification-r2` — active; potential file overlap on AI internals (informational)

### External Documentation

- Azure App Service Linux deployment: framework-dependent vs self-contained — see `docs/guides/auth-deployment-setup.md`
- `dotnet list package --vulnerable --include-transitive` — primary Outcome B tool
- `actionlint` — optional for FR-D6 verification
- GitHub Advisory database — referenced for CVE evidence in CANDIDATES.md

---

*This file should be kept updated throughout project lifecycle. Decisions and Implementation Notes sections grow during execution.*
