# SpaarkeAI Assistant Enhancements R4 — AI Context

> **Purpose**: Context for Claude Code working on spaarkeai-assistant-enhancements-r4.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: All code tasks + 080 deploy DONE; owner UAT ongoing (fix-forward). Only **090** (wrap-up + `/test-diet`) remains.
- **Last Updated**: 2026-10-08
- **Current Task**: none active — see `current-task.md`
- **Next Action**: Fix-forward on owner UAT findings; run 090 when owner signs off UAT.

---

## Standing directives & gotchas

**Owner directives**
- (2026-08-17) **Fix, don't defer** — defer only when there is a known better owner; otherwise fix in-project.
- (2026-08-17) Commit/push only when asked; merge via auto-merge PR (`gh pr merge --auto --merge`) — master is protected.
- (2026-10-08) In the SpaarkeAi Assistant, playbook selection is NOT a user feature — SprkChat `enablePlaybookDiscovery` stays off (Assistant uses the ADR-039 closed catalog).

**Deploy gotchas**
- `sprk_spaarkeai` and the BFF are **last-write-wins** across all projects: ALWAYS fast-forward the worktree to current `origin/master` before building/deploying, or you roll back other projects' work.
- SpaarkeAi Vite resolves `@spaarke/*` to shared-lib **`src/`** (not `dist/`) — no shared-lib rebuild needed; clear `dist/ node_modules/.vite/ .vite/` and `npm run build`. Verify a change marker in `dist/spaarkeai.html` before deploying.
- `npm run build` ends with `build:ribbon`, which fails locally on `@spaarke/sdap-client` (no `dist/` in worktree) — environmental; the ribbon scripts are not part of `sprk_spaarkeai`, deploy proceeds.
- `npm install` churns `package-lock.json` (SpaarkeAi, AI.Widgets) — revert it before committing.
- `scripts/Deploy-AnalysisAction.ps1` cannot seed `infra/dataverse/actions/list-tasks.action.json` (single object, numeric `actionType`, no modelTier/temperature/allow-list mapping) — seed via REST PATCH from the authored JSON.
- Chat-loop projection: a non-empty `sprk_tooldescription` is the SOLE opt-in (`SelectTextProjectable` ignores `sprk_surfaces`) — tracked D-UAT-01 / issue #957. Projectable-catalog cache TTL = 5 min.
- BFF telemetry: App Insights appId `6a76b012-46d9-412f-b4ab-4905658a9559` (`az monitor app-insights query`) — use it for empirical UAT diagnosis before guessing.

**UAT learnings**
- Upload → `chat-classify` (all formats incl. PDF) → "Review an NDA" offered ONLY when `docType == "nda"`. Compose reads PDFs (compose-r7 task 050/051). The `nda_sample*.pdf` test files are NOT NDAs (employment agreement / consulting excerpt).
- A persisted `playbookId` (localStorage `sprk_ai2_playbookId`) suppresses Quick Start welcome cards; "New session" now clears it.

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) — AI-optimized spec (12 FR / 9 NFR / 3 ADR tensions) — permanent reference.
- [`design.md`](design.md) — the R4 design seed (grounded proactive assistant).
- [`plan.md`](plan.md) — WBS + parallel groups + hot-path coordination.
- [`current-task.md`](current-task.md) — **active task state** (context recovery).
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — task tracker + dependencies + parallel groups.
- [`notes/assistant-viewport-clipping-open-in-compose-handoff.md`](notes/assistant-viewport-clipping-open-in-compose-handoff.md) — D9 diagnosis + fix pattern.
- [`notes/behavior-gap-register.md`](notes/behavior-gap-register.md) — the standing behavior-gap register (P5).

### Project Metadata
- **Type**: BFF API (`Services/Ai` — advisory capability, per-Action opt-in, pre-filter, memory) + Frontend (SpaarkeAi `ConversationPane`/`SprkChat` + shared-lib registry/chips).
- **Complexity**: Medium (authoring + a new capability *tier* + the feedback loop; mostly reuse over existing machinery).
- **Predecessor**: spaarkeai-assistant-enhancements-r3 (shipped/deployed — do not reopen).

---

## Context Loading Rules

1. Always load this file first.
2. Check `current-task.md` for active work state (especially after compaction).
3. Reference `spec.md` for FRs, acceptance criteria, ADR tensions, MUST rules.
4. Load the relevant task file from `tasks/`.
5. Apply ADRs via adr-aware.

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

| User Says | Required Action |
|---|---|
| "work on task X" | Execute task X via task-execute |
| "continue" / "keep going" / "next task" | Execute next 🔲 in TASK-INDEX.md via task-execute |
| "continue with task X" / "resume task X" | Execute task X via task-execute |
| "pick up where we left off" | Load current-task.md, invoke task-execute |

**Parallel execution**: each task still uses task-execute (one message, multiple Skill invocations). **This project's contention rule**: the `Services/Ai` catalog/pre-filter (010/011/012) and `SprkChat`/`ConversationPane` (021/023/040) are sequential spines → `parallel-safe:false` among themselves. Memory files (030/031/032) are R4-owned (redesign-r2 closed) but coordinate BFF PRs via `/conflict-check`.

---

## Execution Model & Tiering (Sonnet-5)

- **Planning** (this pipeline): Opus 4.8 / Fable 5.
- **Execution**: default **Sonnet 5 @ high**.
- **opus / xhigh escalations**: **011** (ADR-039 pre-filter boundary — bounded allow-list must not become a second decider), **032** (injection-defense boundary — preference steering).
- **opus / high**: **010** (new catalog field), **012** (advisory Action authoring), **030** (memory enum + governance), **031** (feedback→memory governance).
- **Coverage-first gates**: code-review + adr-check unconditional on FULL-rigor + test-modifying tasks; orchestrator filters findings.

---

## Key Technical Constraints

- **ADR-039 (the defining constraint)**: exactly ONE probabilistic decider (the Text-path agent turn). The grounded-recommend tier adds **NO** classifier / second intent-detection / routing surface. The per-Action bounded tool allow-list is **deterministic pre-filtering only**. Advisory mode already sanctions grounded reasoning + recommendations (2026-07-25 amendment) — every factual claim MUST be cited to a tool result.
- **Per-Action opt-in**: mirror `sprk_allowsknowledge` (catalog data); do NOT gate tools by hardcoded tool-name lists.
- **ADR-016**: the advisory task-agenda Action runs on the Reasoning tier at temp ~0.2–0.3.
- **E3 memory ownership**: redesign-r2 is **closed** — R4 owns `Services/Ai/Memory` changes directly. Still bind CRUD-side consumers to `PublicContracts/MemoryItem.cs` v1; keep AI-internal types out of CRUD code (ADR-013); preserve ADR-042 deferred hard-governance (#616; trustLevel inert).
- **Preference bounds (FR-09)**: a preference maps ONLY to a closed allow-list of named directives → pre-turn tool **hints**; never grants a capability or alters a fact; the guillemet DATA-guard + ADR-039 preference-only rule hold (the stated profile never feeds `AgentToolFilterContext` except through the sanctioned bounded hint).
- **E2 target**: gate the free-string `SprkChatSuggestions` (the P2 dead-end); `ConsumerChips` are already capability-backed — do not rebuild them.
- **ADR-047**: reactive card surface stays distinct from the notification spine (no new push channel).
- **D9 (E4)**: host-proof flex chain, NO fixed/measured heights; confirm live-DOM repro first (partial fix `messageList min-height:0` already on master).
- **BFF §10**: publish ≤60 MB. Measure per BFF-touching task. `/conflict-check` before `Services/Ai`/`ConversationPane`/`SprkChat` PRs.
- **🆕 Runtime = .NET 10 (dev on net10 as of 2026-08-14)**: `global.json` pins SDK 10.0.100 (10.0.1xx installed machine-wide). BFF/server projects are `net10.0`. Every `dotnet` command in a BFF task needs SDK ≥10.0.100 — a "compatible .NET SDK was not found" error means a stale shell (open a fresh terminal), NOT a code problem. **NEVER deploy the BFF from a net8 tree — a net8 deploy to the net10 dev runtime 503s on startup.** This worktree is already net10 (merged net10 master 2026-08-14; `dotnet build -c Release` verified clean, 0 errors). The **~49.63 MB publish baseline was the net8 figure — RE-BASELINE fresh under net10** (net10 net-reduces surface per `dotnet-10-upgrade-r1`); measure the current net10 publish size when reporting §10. Client-only tasks (E2/E4 Vite/tsc) are .NET-runtime-independent — unaffected (use `npm run build`, not `build:prod` which is PCF-only). Health check: `curl https://spaarke-bff-dev.azurewebsites.net/healthz` → 200.

---

## Decisions Made (owner 2026-08-13)

- Build approach = **reuse the existing decider** (advisory mode + pre-filter bounded tools); no new executor.
- Preference steering = **narrow closed allow-list → tool hints only**.
- Agenda surfaces = **Tasks only + inline grounded summary + Briefing/Smart-To-Do follow-on cards if not already open**.
- Operator promotion queue = **out of system scope** (CX/product-owner exercise).
- E3 memory = **owned entirely in R4** (redesign-r2 closed).
- Advisory tier = **ADR-016 Reasoning tier, temp ~0.2–0.3**.

---

## ADR Correction (carry into tasks)

The design draft §7 attributed the advisory/injection rules to **ADR-015** — incorrect. ADR-015 is data-governance/tiers only. The binding rules are **ADR-039** (advisory amendment + preference-only) and **ADR-042** (memory two-scope; hard-governance deferred to #616).

---

## Deferrals & Issues — tracking obligation

Track deferred work + new issues in BOTH `notes/behavior-gap-register.md` (behavior gaps) / a `notes/defer-issues.md` (defects) AND GitHub Issues via `/project-defer-issue-tracking` (alias `/defer`). §11 rule: every entry names a concrete behavior/contract that fails without the work.

### Eval-case obligation (FR-10 infra — task 001)

Every FR-01/04/06/09 behavior change MUST land with a golden-utterance eval case (ADR-039 + ADR-038). The convention + current harness location are documented in [`notes/behavior-gap-register.md`](notes/behavior-gap-register.md) under **"Eval-case harness convention"**: the suite lives at `tests/integration/contract/Eval/`; R4 owns a dedicated net-new family (`assistant-r4-eval-cases.json`, seeded with template case `AR4-001` for FR-01) mirroring the `assistant-r1-eval-cases.json` / `AssistantEnhancementsR1EvalTests.cs` precedent — one project-level harness (`AssistantEnhancementsR4EvalTests.cs`, authored by the FR-01 task) all four behavior-change FRs add cases to, not four separate harnesses. The register's P1–P4 rows are the paper trail each eval case cites back to.

---

## Resources

### Applicable ADRs
ADR-039 (closed catalogs + advisory amendment), ADR-042 (memory governance), ADR-016 (reasoning tier), ADR-013 (AI facade), ADR-015 (data governance), ADR-047 (spine — keep distinct), ADR-021/ADR-012 (Fluent v9 tokens / host-agnostic — E4), ADR-028 (auth/OBO), ADR-038 (testing).

### Related Projects (coordinate)
- `spaarke-ai-architecture-redesign-r2` — **closed** (memory files now R4-owned; still bind PublicContracts).
- `spaarkeai-assistant-enhancements-r3` — predecessor (shipped/deployed; re-base on it).
- `spaarkeai-compose-r5/r6` — `ConversationPane`/`SprkChat` (D9); merge-order coordination.
- `spaarke-notification-spine-r1` — ADR-047 spine (keep distinct).

### External Documentation
`docs/architecture/ASSISTANT-SURFACE-LAUNCH-MECHANISM.md`, `docs/standards/ASSISTANT-UI-ELEMENT-CRITERIA.md`, `.claude/adr/ADR-039-grounded-execution-closed-catalogs.md`, `.claude/adr/ADR-042-memory-architecture-governance.md`, `.claude/constraints/bff-extensions.md`.

---

*Keep this file updated throughout the project lifecycle.*
