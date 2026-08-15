# Code Quality & Assurance R3 - AI Context

> **Purpose**: This file provides context for Claude Code when working on code-quality-and-assurance-r3.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Initialized (assessment-first; execution operator-gated)
- **Last Updated**: 2026-08-06
- **Current Task**: Not started
- **Next Action**: Register portfolio (Epic #427) + INDEX row; then Phase 0 task 001 (rubric)

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) - AI implementation specification (permanent reference)
- [`design.md`](design.md) - Program design (umbrella)
- [`workstreams/bff-api/design.md`](workstreams/bff-api/design.md) - BFF surface workstream #1 (assessed)
- [`README.md`](README.md) - Project overview and graduation criteria
- [`plan.md`](plan.md) - Implementation plan and WBS
- [`current-task.md`](current-task.md) - **Active task state** (for context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) - Task tracker + parallel groups
- [`notes/SESSION-HANDOFF.md`](notes/SESSION-HANDOFF.md) - Read-first session handoff

### Project Metadata
- **Project Name**: code-quality-and-assurance-r3
- **Type**: Standing quality program (multi-surface: BFF, shared libs, PCF, Dataverse, code pages, plugins)
- **Complexity**: High (program-scale; single worktree; surfaces = workstreams)

---

## Context Loading Rules

When working on this project, Claude Code should:

1. **Always load this file first** when starting work on any task
2. **Check current-task.md** for active work state (especially after compaction/new session)
3. **Reference spec.md + design.md** for the program method, rubric, and resolved decisions
4. **Load the relevant task file** from `tasks/` based on current work
5. **Apply ADRs** relevant to the surface (loaded automatically via adr-aware)

**Context Recovery**: If resuming work, see [Context Recovery Protocol](../../docs/procedures/context-recovery.md)

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

### Auto-Detection Rules (Trigger Phrases)

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

The task-execute skill ensures ADRs/constraints/patterns load, context is tracked in current-task.md, checkpointing occurs, and quality gates (code-review + adr-check) run at Step 9.5. Bypassing loses ADR constraints, checkpointing, and gates.

### 🚨 Program-specific execution rules

- **Assessments run via the `quality-assessment` Workflow** (Phase 0 deliverable) — the Workflow tool requires a **per-run operator opt-in** ("use a workflow"). Do NOT auto-launch a Workflow; the operator invokes each assessment turn explicitly. Manual agent fan-out is the fallback only.
- **Adversarial verification (Fable) is non-negotiable** on every assessment — it caught 2 real BFF bugs AND corrected 2 false-positive "dead code" claims that were load-bearing.
- **`/conflict-check` before EVERY remediation PR** — 19 active worktrees touch BFF. Assessments are read-only ⇒ conflict-free ⇒ run anytime.
- **BFF publish ≤ 60 MB compressed** (baseline 44.96 MB incl PDBs (net10 baseline; net8 was 46.89)); report absolute + delta on every BFF-touching task. **No new NuGet packages.**
- **Data-driven dispatch is not grep-provable** — Dataverse `sprk_analysistool.sprk_handlerclass` pre-check before any handler/tool rename or delete; never touch a `HandlerId` string.
- **Verify dead code against `src/` AND `tests/`** (BFF exposes internals via `InternalsVisibleTo` to 3 test assemblies) before deletion.
- **Surfaces 2–6 remediation is DEFERRED** — task-created only after each surface's Fable-verified assessment `design.md` exists.

### Parallel Task Execution

When tasks can run in parallel (no dependencies), each task MUST still use task-execute — one message, multiple Skill invocations. **Tasks touching `.claude/` paths MUST be sequential (main-session-only)** per root CLAUDE.md §3.

---

## Execution Model & Tiering (Sonnet-5)

- **Planning** (design-to-spec, project-pipeline Steps 0–3): Opus 4.8 / Fable 5.
- **Execution** (task-execute per task): default **Sonnet 5 @ effort `high`**; each POML carries `<model-tier>` + `<effort>`. Assessment-synthesis and adversarial-verify tasks use **Fable** per their POML.
- **Step modes**: `directional` default; `prescriptive` for migrations/deploys/irreversible.

---

## Key Technical Constraints

- All client→BFF auth via **`@spaarke/auth`** (ADR-028) — the Finance auth closure migrates the legacy web-resource caller; any `@spaarke/auth` web-resource gap is fixed here and elsewhere (security horizontal).
- CRUD→AI capability only via `Services/Ai/PublicContracts/` (ADR-013) — no direct `IActionResolver`/`IActionRunner`/`IPlaybookLookupService` injection.
- Preserve ADR-032 seams: `StubInsightGraph` (wired `InsightsModule.cs:53`), Todo `NullObject/`+`Placeholder/` factory pair (`TodoSyncModule.cs:84-98`).
- ADR-038 testing: KEEP categories, behavior over mocks, coverage = observation, `/test-diet` at wrap-up.
- ADR-010 DI minimalism: `CommunicationModule` decomposition adds helpers only, registrations identical.
- Behavior-preserving by default; delete > deprecate; small reviewable revertible PRs off the one branch.

---

## Decisions Made

<!-- Format: Date, Decision, Rationale, Who -->

- 2026-08-06: **Finance auth via `@spaarke/auth` (ADR-028)**, not HMAC — owner directive; migrate the `sprk_subgrid_parent_rollup.js` caller + fix any `@spaarke/auth` gap here and elsewhere. Overrides the BFF design's HMAC recommendation. (Owner)
- 2026-08-06: **Assessment-first** — run the full Fable-verified assessment across all surfaces FIRST as the gating deliverable; surfaces 2–6 remediation task-created after their designs exist. (Owner)
- 2026-08-06: **Single project / single worktree** — surfaces are workstreams/phases in one TASK-INDEX on one branch. (Owner, design §4/§15)
- 2026-08-06: **All three BFF extras approved** — delete StubLiveFactResolver, OBO/User `.RequireAuthorization()`, include optional Phase-5 helpers. (Owner)
- 2026-08-06: **NG1 (two-Dataverse-stacks unification) filed as an Idea** now. (Owner)
- 2026-08-13: **Accepted the `customer-provisioning-orchestration-r1` deployment-complexity ask** into r3 (4 refactors). Grounding showed the code is ahead of the ask (#3 credential half already landed via AUTHV2-042 Phase C; #2 partially done). (Owner)
- 2026-08-13: **#1 KV federation → assess-first** (task 017 → `workstreams/config-deployment/design.md`); remediation task-created from the verified design. **#2 + #4 → r3 owns with ArchTest+CI enforcement** (r1 drops its Phase E absorption). **#3 → split** (after Fable grounding, owner 2026-08-13): **#3a** = task 060, drop the *vestigial separate* Dataverse S2S app-reg (scripts/docs/KV, zero code consumers); **#3b** = the shared-lib `ClientSecret`→MI migration (the BFF's own Dataverse path is still secret-based — my 2026-08-12 "credential half done" was wrong for that camp) → folded into the NG1/task-011 track (identity-attribution change, entangled with the access stacks). No ADR-028 amendment (ADR-028 §24 already mandates MI — the secret paths are violations to fix). (Owner)
- 2026-08-13: **NG1 reframed from "deferred out of scope" → "assess-then-decide" track** owned by task 011 — now covering both the access-stack unification AND the #3b shared-lib credential migration (same 2 files, same identity question); task 060 is the #3a app-reg drop only; remediation decided on 011's verified design. Corrected my earlier conservative "defer NG1" recommendation. (Owner challenge accepted)
- 2026-08-06: **Initialize-only** — pipeline generates artifacts + tasks; execution is operator-gated (no auto-run). (Owner)

---

## Implementation Notes

- **2026-08-14: Merged net10 master into this worktree.** BFF is now **.NET 10** (TFM `net10.0`, `global.json` 10.0.100; SDK 10.0.101 machine-wide). `dotnet build -c Release` = clean (0 errors, 21 pre-existing warnings). Per the `dotnet-10-upgrade-r1` handback (its INDEX row + `projects/dotnet-10-upgrade-r1/notes/publish-size-rebaseline.md`):
  - **Publish baseline moved 49.63/46.89 → 44.96 MB incl PDBs (net10 SHRANK it −4.67 MB)**; ceiling 60 unchanged. All r3 BFF-task references updated to 44.96.
  - **Do NOT re-pin the superseded CVE packages** — net10 (Graph 5→6.5 / Kiota 1→2, task 033) already closed the Kiota HIGH CVE (GHSA-7j59-v9qr-6fq9) + removed FR-04 pins. Task **032** (dependency/CVE) must build on this, not re-introduce pins.
  - **Build on the net10 fixes** (H1 `BackgroundService.ExecuteAsync` threading, H2 dev-boot DI validation, H3 X509CertificateLoader) — don't undo them.
  - **`DataverseServiceClientImpl.cs` was changed on master** — the #3b/auth-map/BFF-verification file:line references (grounded against the 08-06 base) should be **re-verified at head** before those tasks execute (the tasks already instruct re-grounding).
  - Root `CLAUDE.md` §10 + `.claude/constraints/azure-deployment.md` baseline already updated to 44.96 by the net10 project (main-session writes) — no r3 action needed there.
- **2026-08-14: Full net10 handoff (`notes/r3-handoff.md`, from dotnet-10-upgrade-r1 FR-17) — additional binding items:**
  - **CVE pins (task 032)**: `dotnet list --vulnerable --include-transitive` = **zero** across the graph — keep it. Do NOT re-pin `System.Text.Json`/`System.Formats.Asn1`/`System.Text.RegularExpressions` (net10 framework-provided). KEEP the deliberate **shared-lib pins `System.Security.Cryptography.Xml` + `Pkcs` at 10.0.11** (the 3 non-web libs don't get the web shared framework). Do NOT re-add `NoWarn=NU1903` (deleted; it masked the now-fixed S.S.C.Xml HIGH).
  - **r3 OWNS the deferred-package-majors backlog → GitHub issue #772** (`notes/deferred-package-upgrades.md`): Azure.Search.Documents v12, PowerBI.Api v5, Azure.AI.Projects v2 GA, Microsoft.Agents.AI GA, Http.Polly→Http.Resilience, JsonSchema.Net v9, coupled Microsoft.Extensions.AI/OpenAI 10.3→10.9 (drags OpenAI ≥2.12). **Each needs its own spec+test — do NOT batch.** Owned by task 032.
  - **HELD (licensing — do NOT bump)**: `FluentAssertions` v8 (paid Xceed license — hold 6.x), `QuestPDF` 2026.x (revenue-gated).
  - **H2 guard test `DiGraphValidationTests`** is MAINTAIN/KEEP — do NOT delete/weaken it, and do NOT disable `ValidateOnBuild`/`ValidateScopes`. Any DI change (task 026 CommunicationModule decompose) MUST keep the DI graph clean + not reintroduce captive deps (45→0 fixed by net10 H2).
  - **ArchTests state**: ADR-010 1:1-interface ceiling is now **153** (re-armed from 76 — legit seams); `IsRecordType` detects record structs. Task 040's rules must coexist with this. Test-suite green on net10 (BFF 10,415/0/101).
  - **Graph v6 / Kiota 2.0 is DONE** — `ODataError` (not `ServiceException`) is the Kiota throw type; no r3 task adds Graph calls, but any r3 edit touching Graph catch blocks must use `ODataError` (`ResponseStatusCode` int, dict `ResponseHeaders`). Ref `dotnet-10-upgrade-r1/notes/graph6-kiota2-break-assessment.md`.
  - **Only `spaarke-dev` is live** — demo/prod decommissioned for budget. Auth verification steps that say "verify MI as Dataverse Application User in BOTH spaarkedev1 + spaarke-demo" (tasks 060/062, #3b) are **dev-only for now**; demo/prod re-verify is DEFERRED until re-provisioned (net10 slot-swap cutover is a separate follow-on, not r3's job).
  - Worktree migration tooling exists (`/worktree-net10-migrate`); this worktree already merged manually (verified net10 + clean build). **Keep VS/Rider closed during any future master merge** (open IDE can revert csproj to net8).

---

## Deferrals & Issues — tracking obligation (read this)

This project tracks deferred work + newly-discovered issues in TWO places, kept in sync:

1. **`notes/defer-issues.md`** — source of truth
2. **GitHub Issues** on the portfolio board (Epic #427)

Invoke `/project-defer-issue-tracking` (alias `/defer`) — it writes to BOTH in one step. NG1 (Dataverse-stack unification) is the first deferral → file as an **Idea** at pipeline time. CLAUDE.md §11 rule applies (name a concrete failing behavior/contract; no "flexibility" reasons).

---

## Resources

### Applicable ADRs
- **ADR-028** (auth v2 / `@spaarke/auth`), **ADR-013** (AI facade), **ADR-032** (null-object kill-switch), **ADR-038** (testing), **ADR-010** (DI minimalism), **ADR-022** (PCF platform libs), **ADR-002** (plugins)

### Related Projects
- BFF surface workstream #1 — design at [`workstreams/bff-api/design.md`](workstreams/bff-api/design.md) (executes in THIS worktree; relocated 2026-08-06 from the standalone `bff-api-cleanup-remediation-r1/` folder)
- `code-quality-and-assurance-r1` / `-r2` — predecessors (system → structural → program)
- `ci-cd-unit-test-remediation-r1` — owns `.github/workflows` edits (coordinate); ADR-038 authority
- NG1 → separate Idea (two-Dataverse-stacks unification, needs its own ADR)

### External Documentation
- `.claude/constraints/bff-extensions.md` (binding BFF governance)
- `docs/assessments/bff-ai-extraction-assessment-2026-05-20.md` (evidence base)
- `docs/standards/TEST-ARCHITECTURE.md`, `docs/adr/ADR-038-testing-strategy.md`

---

*This file should be kept updated throughout project lifecycle*
