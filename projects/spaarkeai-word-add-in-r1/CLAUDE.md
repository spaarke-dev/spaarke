# Spaarke Office Add-in (Word + Outlook) r1 — AI Context

> **Purpose**: Context for Claude Code when working on `spaarkeai-word-add-in-r1`.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Phase 0 complete; Phase 1 nearly complete (21 of 43 tasks ✅ as of 2026-09-11 — 012 identity resolver live on dev, Spike-1 GREEN; 013 + 016 done; 011 awaits the 1.0.8 manifest re-upload; 014 not started)
- **Last Updated**: 2026-09-11
- **Current Task**: see [`current-task.md`](current-task.md) — the authoritative live state; this block is a summary only
- **Next Action**: see `current-task.md` Quick Recovery

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
- **Complexity**: High — 43 tasks (34 at generation; the rest added during execution — see `tasks/TASK-INDEX.md`), 5 phases, 4 gating spikes
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
| **F-g** | `sprk_event` is absent from `sprk_document` yet shipped code authorizes + writes it | 026, 035 · fix owned by UAC-r2 |

### 🔴 `sprk_document` association slots — read this before touching any association code

Verified live 2026-09-04 (MCP + maker-portal Columns view). **Two families, sixteen lookups:**

- **Direct (4)** — `sprk_matter`, `sprk_project`, `sprk_invoice`, `sprk_workassignment`. **There is no `sprk_event`.**
- **Related (12)** — `sprk_relatedagreement`, `sprk_relatedcommunication`, `sprk_relatedcontact`, `sprk_relatedevent`, `sprk_relatedinvoice`, `sprk_relatedmatter`, `sprk_relatedorganization`, `sprk_relatedproject`, `sprk_relatedservicerequest`, `sprk_relatedtodo`, `sprk_relatedvendororg`, `sprk_relatedworkassignment`.

The Office save path writes **only the direct family**, so a card reading only `sprk_related*` is empty on every document the add-in created. Read `sprk_relatedevent` for Event — `sprk_event` will throw.

**Do not trust these three claims**, all of which appear in shipped code comments and in [`coordination-document-association-map-from-email-r2-2026-09-04.md`](coordination-document-association-map-from-email-r2-2026-09-04.md) §3/§4.1 and are false against live schema: that a direct `sprk_event` column exists; that `sprk_todo` is "unmappable / needs a schema change first" (`sprk_relatedtodo` exists); that there is no contact lookup (`sprk_relatedcontact` exists). Only "no `account` lookup" is correct. See that doc's §0 correction.

---

## Decisions Made

| Date | Decision | Rationale |
|---|---|---|
| 2026-09-04 | **FR-02 stamping is forward-only** — no retroactive rewrite of existing `sprk_document` bytes | Owner decision at pipeline time. FR-01's Graph + alternate-key path already identifies pre-existing Spaarke documents; the stamp is a fallback for round-tripped files. Retroactive stamping would mean rewriting stored bytes for every existing document. |
| 2026-09-04 | **Run scope: initialize only** — no task auto-execution from the pipeline | Operator reviews 34 tasks before execution begins. |
| 2026-09-04 | **ADR-012 → Path A** (project-scoped exception): the add-in keeps thin views under `shared/taskpane/`, consuming `@spaarke/auth` only | `@spaarke/ui-components` assumes React 19 and some components are Xrm-bound; importing them would break at runtime (NFR-03). r2 established the recreate-layouts precedent. |
| 2026-09-04 | **ADR-050 → Path C** (comply in spirit): the Office Dialog is host chrome, outside ADR-050's scope | ADR-050 governs Spaarke-rendered modals. Any Spaarke UI rendered *inside* the dialog still follows ADR-050 + ADR-021. |
| 2026-09-09 | **FR-18 = production types only** (A1 + B1). Acceptance narrows to the `office-addins` package; foreign files pulled in by a path alias do not count | 88 production errors were achievable and unblock Phase 1; 296 of 384 sat in test files entangled with a separate red-harness defect. Production is now **0**. |
| 2026-09-09 | **The 289 test-file typecheck errors are CONSCIOUSLY ACCEPTED, not deferred-and-forgotten.** Trigger to revisit: *when a build surfaces them.* | Verified inert: **no CI job typechecks `office-addins`** (`sdap-ci.yml` runs `tsc --noEmit` for `Spaarke.AI.Widgets` only); `npm run build` is webpack and test files are not in its graph — which is why the build is green today *with* all 289 present; and `ts-jest isolatedModules` is transpile-only so they cannot fail a test run. They surface ONLY on a manual `npm run typecheck`. Informed-consent note: a NEW test-file error would be invisible against this background, but **production is at 0 so new production errors stand out** — which is the benefit FR-18 was actually for. This is a measured, documented accept, not a burial. |
| 2026-09-09 | **Task 017 closed: RTL bump landed, but `useAnnounce`'s failure is a production-code defect, not the RTL-peer-mismatch 009 hypothesized** | RTL 14→16 bump (+ `@testing-library/dom` peer dependency, discovered mid-task and matched to the sibling pin) left the suite suite-for-suite identical to 009's baseline (12/21 failing, 92/329 failing tests). `useAnnounce.test.ts` still fails 16/16 with the same `NotFoundError` after the bump — root cause is `useAnnounce.ts` appending/removing DOM nodes directly on `document.body`, outside React's tracked tree, which collides with React 19's unmount bookkeeping regardless of RTL version. Fixing it needs a production-source change (e.g. a React-owned portal target), out of this task's scope. See `notes/017-testing-library-alignment.md`. |
| 2026-09-09 | **React 19 is the standard for code components; `@testing-library/react` aligns to it (v16), not the reverse** | Repo survey: PlaybookBuilder (RTL ^16.0.0) and SemanticSearch (RTL ^16.1.0) already run RTL 16 against React 19. `office-addins` was the ONLY React-19 package still on RTL ^14.2.1 — an outlier, not an architectural fork. PCF controls sit in their own internally-consistent React 16.14 / RTL 12 lane and are NOT React-19 compliant; `external-spa` is React 18. So this was never a two-sided major-version decision. Task **017** aligns it. |
| 2026-09-09 | **Task 010 closed: one Word adapter, and `HostAdapterFactory` is now LIVE on both panes.** `word/WordHostAdapter.ts` deleted; `shared/adapters/WordAdapter.ts` carries the ported `getFileAsync(Compressed)` extraction | Byte-identicality was PROVEN by executing both implementations side by side over the same bytes while the reference still existed (26 real .docx + 7 synthetic multi-slice cases up to 65 slices; `Buffer.equals`=true, SHA-256 equal, PK signature, identical slice order in every case). The corpus alone was NOT sufficient — every corpus file is < 64 KB and therefore single-slice, leaving multi-slice assembly, the actual risk, untested. See `notes/010-adapter-consolidation.md` §2. |
| 2026-09-09 | 🔴 **`createAndInitialize()` is called with NO host argument, so `detectHostType()` reads `Office.context.host` on both panes’ bootstrap — a property never previously exercised in production. UNCLOSED: must be verified live on Word AND Outlook, desktop AND web, at task 042.** | Passing an explicit host type would have been safer but would have left detection dead and silently dodged plan.md R-4 / escalation trigger 3. Unit tests can only pin the failure SHAPE (undefined host -> typed INVALID_HOST, rendered visibly — not a silently wrong adapter). **If Outlook fails there, the fix is NOT a host-sniffing fallback** (forbidden by trigger 3); the ADR-clean options are routing detection through the factory’s existing `waitForOfficeReady()` (which uses `Office.onReady`’s `info.host`, the signal both panes already await and discard) or passing the explicit host type as a deliberate decision. See `notes/010-adapter-consolidation.md` §6. |
| 2026-09-09 | **ADR-038 §2 → Path A (project-scoped exception): `src/client/office-addins/**/__tests__/**` is treated as KEEP-equivalent for `/test-diet` at project close.** `/test-diet` MUST classify these by CONTENT, not by path. | Surfaced by task 010’s adr-check (W-2). All eight ADR-038 KEEP paths are repo-root .NET paths (`tests/integration/**`, `tests/unit/domain/**`, `tests/Spaarke.ArchTests/**`); no KEEP category covers front-end jest at all, so a naive path check would flag the entire office-addins suite — including task 010’s `.docx` regression guard, the single artifact pinning the 2026-09-03 UAT defect — as scaffolding to delete. Relocating them is not viable (jest `roots: [‘<rootDir>/shared’]` + the `@shared/*` alias bind the suites to the package). **A Path B ADR-038 amendment adding a front-end KEEP category is the structurally correct fix and should be filed separately** — the precedent is Amendment A1, which added `tests/Spaarke.ArchTests/**` for exactly this “prescribed by practice, protected by nothing” reason. Not smuggled into task 010’s PR. |

| 2026-09-11 | **Matter numbering moves OUT of r1 into a separate project** (owner decision). The number must come from a **server-side record-numbering component that triggers on record create**; it need not be shown in the add-in. **Do NOT build a Dataverse plugin — Spaarke does not use plugins.** Task 030 keeps owner + BU defaults + create-time field mapping and must never write `sprk_matternumber`; FR-13's "number populated" acceptance and 030's AC2/AC3 transfer to that project. Hand-off: `notes/030-numbering-handoff.md` | Verified the component does not exist anywhere: repo, `origin/master` `e0a6f87c4`, and dev Dataverse (no autonumber on `sprk_matternumber`; no custom API / env var / PCF / web resource; no custom plugin step or flow on `sprk_matter` Create). The only type-code numbering today is the client-side random generator in the two `matterService.ts` wizards. |
| 2026-09-11 | **Matter Type is required on pane quick-create and must always be sent — but the server does NOT reject when it is missing** (owner decision). The pane gains a required matter-type field (new client task); the BFF accepts an optional `matterTypeId` and creates the record regardless. | Rejecting would break the shipped pane until the client change lands; requiring it in the UI guarantees it in practice. |
| 2026-09-11 | **ADR-044 local `cleanGuid` in the add-in (`shared/taskpane/utils/cleanGuid.ts`) — continuation of the ADR-012 Path A exception**, not a fresh violation | The canonical `cleanGuid` lives in `@spaarke/ui-components`, which this package may not import (Path A, 2026-09-04); `todoChoices.ts` is the approved precedent. Task 013 D-2. |

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
