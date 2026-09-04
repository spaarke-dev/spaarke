# Spaarke Office Add-in (Word + Outlook) r1 — AI Context

> **Purpose**: Context for Claude Code when working on `spaarkeai-word-add-in-r1`.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Initialized — tasks generated, none started
- **Last Updated**: 2026-09-04
- **Current Task**: none
- **Next Action**: Execute task 001 (`npm install` + real typecheck baseline) via `task-execute`

---

## Quick Reference

### Key Files

- [`spec.md`](spec.md) — the contract (20 FRs, 11 NFRs, closed acceptance set)
- [`design.md`](design.md) — original design + owner decisions
- [`README.md`](README.md) — overview + graduation criteria
- [`plan.md`](plan.md) — WBS, findings, risk register
- [`current-task.md`](current-task.md) — **active task state** (context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — task tracker + wave groups
- [`ADDIN-CONTEXT-FROM-EMAIL-R2.md`](ADDIN-CONTEXT-FROM-EMAIL-R2.md) · [`DEDUP-AND-SAVE-BACK-IDENTITY.md`](DEDUP-AND-SAVE-BACK-IDENTITY.md) — handoffs

### Project Metadata

- **Project Name**: `spaarkeai-word-add-in-r1`
- **Branch**: `work/spaarkeai-word-add-in-r1`
- **Type**: Office Add-in (client) + BFF endpoints + Dataverse
- **Complexity**: High — 34 tasks, 5 phases, 4 gating spikes
- **Hot paths**: BFF=Y · SpaarkeAi=N · ci-workflows=Y · skill-directives=N · root-CLAUDE=N

### Read before touching add-in code

- [`docs/architecture/office-outlook-teams-integration-architecture.md`](../../docs/architecture/office-outlook-teams-integration-architecture.md) — as-built map
- [`src/client/office-addins/CLAUDE.md`](../../src/client/office-addins/CLAUDE.md) — module pointer + six load-bearing facts

---

## Context Loading Rules

1. Load this file first.
2. Check [`current-task.md`](current-task.md) for active state (especially after compaction).
3. Reference [`spec.md`](spec.md) for requirements and acceptance criteria.
4. Load the task POML from `tasks/`.
5. Apply the ADRs in the task's `<constraints>`.

**Context Recovery**: [Context Recovery Protocol](../../docs/procedures/context-recovery.md)

---

## 🚨 MANDATORY: Task Execution Protocol

**ABSOLUTE RULE**: All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

| User Says | Required Action |
|---|---|
| "work on task X" | Execute task X via `task-execute` |
| "continue" / "keep going" / "next task" | Read `TASK-INDEX.md`, find first 🔲, invoke `task-execute` |
| "continue with task X" / "resume task X" | Execute task X via `task-execute` |
| "pick up where we left off" | Load `current-task.md`, invoke `task-execute` |

Bypassing loses ADR constraints, checkpointing, and the Step 9.5 quality gates.

### Parallel task execution

Tasks in the same wave still each use `task-execute` — ONE message with MULTIPLE Skill invocations. Max 6 concurrent. Dispatch each subagent at its POML's `<model-tier>` and `<effort>`.

### Multi-file decomposition

For tasks modifying 4+ files: group by module, parallelize where files are independent, serialize where coupled. See [task-execute Step 8.0](../../.claude/skills/task-execute/SKILL.md).

---

## Execution Model & Tiering

- **Planning** (this pipeline run): Opus 4.8 / Fable 5.
- **Execution**: default **Sonnet 5 @ effort `high`**. Each POML carries `<model-tier>` and `<effort>`; `opus` + `xhigh` are reserved for the identity resolver, the version-save server change, the creation service, and the authorization hardening.
- **Step modes**: `directional` by default; `prescriptive` for task 010 (adapter consolidation order is load-bearing) and the deploy tasks.
- **Escalation**: tasks with judgment boundaries carry `<escalation><trigger>`. Firing one is a legitimate stop, not improvisation.

---

## Key Technical Constraints

### MUST

- Canonicalize every Dataverse GUID via `cleanGuid` at every boundary — **ADR-044**. Never interpolate a raw GUID into an OData key predicate.
- Use endpoint filters for authorization; `.RequireAuthorization()` on the group — **ADR-008**.
- Keep Graph SDK types behind `SpeFileStore` — **ADR-007**.
- Use NAA via `@spaarke/auth` `OfficeNaaStrategy`. **No MSAL construction in the add-in package** — **ADR-028**, arch-test enforced.
- Use infinite lazy-scroll for Find results — **ADR-051**.
- Measure BFF publish size on every BFF-touching task, **against a fresh build of master**, not the recorded baseline — **ADR-029** + root CLAUDE.md §10.
- Pass `documentId` on every index call, or chunks land as orphans and tracking fields cannot be written — [`ai/indexing-pipeline.md`](../../.claude/patterns/ai/indexing-pipeline.md).
- Add a contract test for every new or modified endpoint — **ADR-038**.

### MUST NOT

- ❌ `.WithClientSecret` — **ADR-028**, arch-test enforced
- ❌ Import `Xrm`-bound components into the add-in — **NFR-03**. `Xrm.*` does not exist in an Office host; recreate layouts (the r2 precedent).
- ❌ Relax `sprk_graphitemid_uk` — **NFR-07**. Compose's transient-key dedup and promote-idempotency both rest on it.
- ❌ Use the immutable suppress path for editable documents — **NFR-08**. A Word document is editable; suppress-forever on a hash hit collapses two distinct drafts into one record. Mirror `ComposeService.PromoteIfEphemeralAsync`.
- ❌ Add a pager to any list — **ADR-051**
- ❌ Inject `IOpenAiClient` / `IPlaybookService` into CRUD code — use `Services/Ai/PublicContracts/` — **ADR-013**
- ❌ Edit `ci-router.yml`, `ci-tier1-blocking.yml`, `ci-tier2-advisory.yml` — **frozen** under the shadow-comparison window (open 2026-08-27)

### Gotchas verified during discovery

- **There is no `build:prod` script** in `src/client/office-addins` — `npm run build` *is* the production build. `src/client/office-addins/CLAUDE.md:39` names a script that does not exist.
- **There is no concise `ADR-038`** in `.claude/adr/` — point at [`docs/adr/ADR-038-testing-strategy.md`](../../docs/adr/ADR-038-testing-strategy.md).
- **`ADR-049`** (Compose Shadow Document) governs the other `.docx` write path and was missing from the spec's ADR table. Read it before touching any `.docx` save path.
- **`deploy-office-addins.yml` does not trigger on this branch** (only `master` and `work/SDAP-outlook-office-add-in`) — use `workflow_dispatch` or add the trigger.
- **`npm install` needs `--legacy-peer-deps --no-audit --no-fund`** — a bare install fails with ERESOLVE (`@testing-library/react@14` peer-requires React 18; the project is on React 19).
- **Deploy is CI-only** — never run the workflow as an agent. Push, then `gh run list --workflow=deploy-office-addins.yml`.

---

## Findings that modify the spec (bound to tasks)

Discovery verified six spec assumptions as false or mis-sized. Full detail in [`plan.md`](plan.md) §3. Do not silently absorb these.

| ID | One-line | Owning task |
|---|---|---|
| **F-a** | FR-12's shipped collision handling is on a different upload path than the add-in uses | 005 (spike) → 025 |
| **F-b** | FR-16's similarity engine has **no per-row authorization** — the UAC-r2 failure mode | 032 (gates 033) |
| **F-c** | No single endpoint returns similar documents *and* records | 034 |
| **F-d** | FR-11's `ExistingDocumentId` hook is inert on both sides | 023, 024 |
| **F-e** | FR-04 as written would regress the `.docx` save; `HostAdapterFactory` is dead | 010 |
| **F-f** | `POST /api/office/save` has zero executing contract coverage | 016 |

---

## Decisions Made

| Date | Decision | Rationale |
|---|---|---|
| 2026-09-04 | **FR-02 stamping is forward-only** — no retroactive rewrite of existing `sprk_document` bytes | Owner decision at pipeline time. FR-01's Graph + alternate-key path already identifies pre-existing Spaarke documents; the stamp is a fallback for round-tripped files. Retroactive stamping would mean rewriting stored bytes for every existing document. |
| 2026-09-04 | **Run scope: initialize only** — no task auto-execution from the pipeline | Operator reviews 34 tasks before execution begins. |
| 2026-09-04 | **ADR-012 → Path A** (project-scoped exception): the add-in keeps thin views under `shared/taskpane/`, consuming `@spaarke/auth` only | `@spaarke/ui-components` assumes React 19 and some components are Xrm-bound; importing them would break at runtime (NFR-03). r2 established the recreate-layouts precedent. |
| 2026-09-04 | **ADR-050 → Path C** (comply in spirit): the Office Dialog is host chrome, outside ADR-050's scope | ADR-050 governs Spaarke-rendered modals. Any Spaarke UI rendered *inside* the dialog still follows ADR-050 + ADR-021. |

---

## Implementation Notes

*No notes yet — populated during execution.*

---

## Deferrals & Issues — tracking obligation

Deferred work and newly-discovered issues go in **both** `notes/defer-issues.md` (source of truth) **and** GitHub Issues (visibility). Invoke `/project-defer-issue-tracking` (alias `/defer`) — it writes both in one step.

Never add an entry only to `notes/defer-issues.md`. `push-to-github` blocks push on entries without GitHub URLs.

CLAUDE.md §11 applies: every entry must name a concrete behavior or contract that fails without the work. "Future flexibility" / "separation of concerns" is not a reason.

---

## Resources

### Applicable ADRs

**From spec** — ADR-001 (Minimal API + BackgroundService) · ADR-007 (`SpeFileStore` facade) · ADR-008 (endpoint-filter authorization) · ADR-010 (DI minimalism) · ADR-012 (shared component library — Path A exception) · ADR-021 (Fluent v9 + dark mode) · ADR-028 (Auth v2, secret-free, NAA) · ADR-029 (publish hygiene + size ratchet) · ADR-038 (testing strategy — **`docs/adr/` only**) · ADR-044 (`cleanGuid`) · ADR-050 (modal shell — Path C) · ADR-051 (infinite lazy-scroll)

**Added during discovery** — **ADR-049** (Compose Shadow Document — the Word/OOXML ADR) · ADR-013 (`PublicContracts` facade) · ADR-004 / ADR-036 (job contract) · ADR-019 (ProblemDetails) · ADR-024 (polymorphic regarding)

### Patterns

[`ai/indexing-pipeline.md`](../../.claude/patterns/ai/indexing-pipeline.md) · [`auth/spaarke-sso-binding.md`](../../.claude/patterns/auth/spaarke-sso-binding.md) · [`auth/spe-writer-identity-matching.md`](../../.claude/patterns/auth/spe-writer-identity-matching.md) · [`ui/fluent-v9-host-visual-fit.md`](../../.claude/patterns/ui/fluent-v9-host-visual-fit.md) · [`ui/infinite-scroll-list.md`](../../.claude/patterns/ui/infinite-scroll-list.md) · [`ui/thin-scrollbar.md`](../../.claude/patterns/ui/thin-scrollbar.md) · [`api/endpoint-filters.md`](../../.claude/patterns/api/endpoint-filters.md) · [`dataverse/polymorphic-resolver.md`](../../.claude/patterns/dataverse/polymorphic-resolver.md)

### Constraints

[`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — binding pre-merge checklist for every BFF addition

### Related projects

| Project | Relationship |
|---|---|
| `email-communication-intelligence-r2` | Shipped the add-in's current state + content-dedup layer. Two handoff docs in this folder. |
| `unified-access-control-r2` | Tasks 094 (collision pre-flight) + 095 (two-slot association). **Do not duplicate.** |
| `spaarkeai-compose-r8` | The other `.docx` write path. `parallel-safe:false` across the Compose spine. `/conflict-check` before every BFF PR. |
| `spaarkeai-word-native-r1` | Owns MCP + the declarative agent. FR-20 only *launches* it. |

### External documentation

- Office Add-ins unified manifest (`outlook/manifest.json` is the in-repo precedent)
- Office Dialog API — Spike-2 subject
- Microsoft Graph `/shares/u!{base64url}/driveItem` — FR-01 identity path

---

*Keep this file updated throughout the project lifecycle.*
