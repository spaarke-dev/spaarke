# Session Handoff — Code Quality & Assurance R3

> **Read this first** when opening a fresh Claude Code session in this worktree.
> **Written**: 2026-08-06 · **Branch**: `work/code-quality-and-assurance-r3` · **Worktree**: `C:\code_files\spaarke-wt-code-quality-and-assurance-r3`

## What this is

The **Code Quality & Assurance R3** program — a standing quality initiative whose north star is:
*"If a panel of senior architects/devs (web apps + Power Apps custom dev + enterprise) reviewed this codebase, it would earn an A+."*

Lineage: `code-quality-and-assurance-r1` (tooling/scorecard, C→B) → `r2` (17 tasks ✅, structural, B→A-) → **r3**. The r3 slot's March 2026 "last-5-points polish" draft was never executed and is stale; it was **resurrected + rewritten as this program** (original archived at `notes/design-r3-original-2026-03-15.md`).

## Operating model (owner-decided 2026-08-06 — do not re-litigate)

**SINGLE project, SINGLE worktree.** No separate per-surface projects/worktrees/branches. Each surface (BFF, shared client libs, shared server libs, PCF, Dataverse model, code pages) is a **workstream/phase** in ONE `TASK-INDEX.md`, executed as small PRs off THIS branch. Surface folders (e.g. `projects/bff-api-cleanup-remediation-r1/`) are semantic homes for design/findings only.

### The 5 locked decisions (design §15)
1. **Assessment engine = multi-agent Workflow** (build a reusable `quality-assessment` workflow; manual agent fan-out is fallback).
2. **Single project/worktree** (above).
3. **Portfolio: register under existing Epic #427 `[Epic]: Code Quality`** — one Project Issue; surfaces = workstreams (no per-surface Issues).
4. **Forcing-functions activate per-surface** (each surface flips its own gate on as its last step; no repo-wide big-bang).
5. **Grade authority = self-score** against the rubric (no external panel).

## Current state

- Design drafts committed + pushed on this branch (through `786b27c72`):
  - `projects/code-quality-and-assurance-r3/design.md` — the program design (READ FIRST: §4 structure, §4A coordination, §5 rubric D1–D11, §6 method, §7 surface sequence, §11 hot-path).
  - `projects/bff-api-cleanup-remediation-r1/design.md` — BFF surface workstream #1 (assessed).
- **No `/design-to-spec` or `/project-pipeline` run yet.** No spec.md, plan.md, tasks/, README, current-task.md.
- Portfolio registration + `projects/INDEX.md` row are **pending** — they happen automatically at `/project-pipeline` time (devops hooks). Parent = Epic **#427**.

## Hot-path (this program carries ONE declaration)

`bff=Y · spaarkeai=Y · ci-workflows=Y · skill-directives=Y · root-claude-md=N`. Net-negative code delta (mostly removal/consolidation). `/conflict-check` before EVERY remediation PR (19 other worktrees touch BFF). BFF publish baseline: 46.89 MB compressed (ceiling 60).

## One OUTSTANDING owner decision (gates BFF remediation)

BFF workstream §6 — `Api/Finance/FinanceRollupEndpoints.cs` has two `.AllowAnonymous()` endpoints that **write** to Dataverse. Options: (A) `.RequireAuthorization()`, (B) HMAC filter [recommended], (C) accept + fix the misleading comment. Owner must pick before the auth task executes.

## Next steps (run IN THIS worktree session)

1. Review the two design.md files (esp. the outstanding §6 decision above).
2. `/design-to-spec projects/code-quality-and-assurance-r3` → review spec → `/project-pipeline projects/code-quality-and-assurance-r3` (registers under Epic #427, adds INDEX.md row).
   - The pipeline should produce: the rubric (`docs/standards/CODE-QUALITY-RUBRIC.md`), `notes/SCORECARD.md`, the reusable `quality-assessment` workflow, and Phase-0 re-baseline tasks.
3. First surface after BFF: **shared client libs** (`src/client/shared/Spaarke.*`, 16 pkgs ~39k LOC — highest leverage). Read-only assessment can run anytime.

## Method (proven on the BFF pass — repeat per surface)

Multi-agent Workflow: fan-out per rubric dimension → **adversarial verification pass (mandatory)** → prioritized remediation design → tasks. The verification pass is non-negotiable — on BFF it caught 2 real prod bugs AND corrected 2 false-positive "dead code" claims that were actually load-bearing.

## Key BFF findings already banked (workstream #1)

Broken prod path (invoice totals: `IDataverseService as ServiceClient` always throws — `FinanceRollupService.cs` + `Services/Finance/Tools/FinancialCalculationToolHandler.cs`); dead `EmailToEmlConverter` builder half + dual registration; ~2.7k LOC dead code (Scopes folder, Safety cluster, orphaned RetryPolicies, archived files); 13 copy-pasted `IDataverseService`→`ServiceClient` downcasts; 4 AI-facade violations (Workspace injects `IActionResolver`/`IActionRunner`/`IPlaybookLookupService`); anon Dataverse-write endpoint (§6); `Endpoints/`→`Api/` migration unfinished; `CommunicationModule` 75-registration monolith; 127 MB build artifacts in source tree (2 tracked tarballs).
