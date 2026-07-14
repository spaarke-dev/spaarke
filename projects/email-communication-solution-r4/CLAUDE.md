# Email Communication Solution R4 — AI Context

> **Purpose**: Context for Claude Code when working on email-communication-solution-r4.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: Implementation — ready for Wave 0 (task 001)
- **Last Updated**: 2026-07-14
- **Current Task**: None active (pipeline complete)
- **Next Action**: Run `task-execute` on task 001 to begin Wave 0

---

## Quick Reference

### Key Files
- [`spec.md`](spec.md) — AI spec (27 FRs, 8 NFRs) — implementation source of truth
- [`design.md`](design.md) — Human design (rev 2) — rationale + hot-path declaration
- [`plan.md`](plan.md) — Wave WBS + critical path + coordination gating
- [`current-task.md`](current-task.md) — Active task state (context recovery)
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — All tasks + status + parallel groups + dependency graph
- [`reference/r3-send-side-design.md`](reference/r3-send-side-design.md) — Absorbed R3 send-side detail (self-contained)
- Absorbed R3 task POMLs (reuse for W2/W6): [`../x-email-communication-solution-r3/tasks/`](../x-email-communication-solution-r3/tasks/)

### Project Metadata
- **Type**: Mixed — server (C#) engine/enrichment/intelligence + client (TS) composer/Code Page + Dataverse schema + AI + hardening + docs
- **Complexity**: High (cross-cuts BFF `Services/Communication` + `Services/Ai` + `Api/Office`, shared lib, Code Page, Dataverse, Outlook add-in, 4 docs)
- **Branch**: `work/email-communication-solution-r4`
- **Absorbs**: R3 (`x-email-communication-solution-r3`, SUPERSEDED — designed, never executed)
- **Parent**: R2 (`email-communication-solution-r2`, server foundation, completed 2026-03)

---

## 🚨 MANDATORY: Task Execution Protocol

All task work MUST use the `task-execute` skill. DO NOT read POML files directly and implement manually.

| User Says | Required Action |
|---|---|
| "work on task X" | Execute task X via task-execute |
| "continue" / "next task" | Read `TASK-INDEX.md`, find first 🔲, invoke task-execute |
| "resume task X" | Execute task X via task-execute |
| "pick up where we left off" | Load `current-task.md`, invoke task-execute |

**Sub-Agent Write Boundary**: Sub-agents CANNOT write to `.claude/` paths. Tasks touching `.claude/` are `parallel-safe: false` and run from the main session. Affected: **005** (ADR-045). See root CLAUDE.md §3.

**Max concurrency**: 6 agents per wave. Dispatch each task's subagent with its `<model-tier>` + `<effort>` (default `sonnet` @ `high`).

---

## 🚨 BINDING: Hot-Path Coordination (Services/Ai ownership)

Per [`projects/INDEX.md`](../INDEX.md), **`spaarke-ai-architecture-redesign-r2` is the sole owner of `Services/Ai/` internals** and publishes seams under `Services/Ai/PublicContracts/`.

- **W5 (Responsive Intelligence) is GATED.** Task **050** is a coordination gate: run `/conflict-check`, confirm OutputRouter `record`/`notification` disposition ownership with r2-core, and consume the `PublicContracts` seams — **do NOT fork `Services/Ai/` internals.** Tasks 051–054 MUST NOT start until 050 clears.
- Also touching `Services/Ai/`: `spaarke-daily-update-service-r5` (`UpdateRecordNodeExecutor`, `Narrators/`), `spaarkeai-compose-r2`, `chat-routing-redesign-r1`, `spaarke-ai-platform-unification-r6`.
- **Every BFF-touching task runs `/conflict-check` before opening a PR.**

---

## 🚨 BINDING: BFF Hygiene (root CLAUDE.md §10)

This project heavily touches `Sprk.Bff.Api`. For every BFF-touching task:
1. Load [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) before designing the addition.
2. State the **Placement Justification** in the PR/design (even when "in BFF").
3. Use the `Services/Ai/PublicContracts/` facade for CRUD→AI needs; do NOT inject AI-internal types directly.
4. **Verify publish-size** on every BFF task: `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`; report absolute + delta vs baseline (~49.63 MB incl. PDBs, 2026-07-08). Ceiling ≤60 MB. **Retiring `Services/Email/` (task 007) should REDUCE size — report the delta.**
5. Verify no new HIGH CVE (`dotnet list package --vulnerable --include-transitive`).
6. Update tests in `tests/unit/Sprk.Bff.Api.Tests/`; feature-gated services use ADR-032 Null-Object.

---

## Key Technical Constraints (from spec — binding)

- ✅ MUST operate the Association Engine over the **normalized envelope**, never `Microsoft.Graph.Message`.
- ✅ MUST invoke enrichment from **both** inbound and outbound (direction symmetry).
- ✅ MUST ship **auto-file ON for deterministic rungs 0–3 ≥0.85** (ADR-018 kill-switch, per-tenant); **AI rungs (4–5) never auto-file** — always `Suggested`/`Ambiguous`. *(Owner override of design DEC-4 — see spec ADR Tensions Path A.)*
- ✅ MUST verify new option-set integers via Dataverse MCP before assignment (task 002).
- ✅ MUST correct the org target to **`sprk_organization`** (not `account`/OOB `organization`).
- ✅ MUST inject central `TokenCredential` + `IGraphClientFactory` + canonical Dataverse interfaces (ADR-028); client uses `@spaarke/auth` only + `OfficeNaaStrategy` for the add-in.
- ❌ MUST NOT add a new regarding mechanism — extend `RegardingResolver`/`RegardingLookupMap`/`TODO_REGARDING_CATALOG`.
- ❌ MUST NOT `new` a credential or `ConfidentialClientApplication` anywhere.
- ❌ MUST NOT build Teams/Slack/Gmail/SMS channel code — seams only (email impl).
- ❌ MUST NOT re-introduce SSS or OOB `email` activity dependencies.

---

## Owner Clarifications (from design-to-spec, 2026-07-14)

| Topic | Decision |
|---|---|
| Communication ADR number | **ADR-045** (design's "ADR-033" is a stale R3 carryover; ADR-033 already taken) |
| Add-in auth scope | **R4 owns the last-mile wiring** (W7) — `@spaarke/auth`/`OfficeNaaStrategy` foundation already ships on `master`; add-in still on deprecated services + Office endpoints filters stubbed (`// TODO: Task 033`). No external blocker. |
| Auto-file default | **Auto-file ON for deterministic ≥0.85 at launch** (overrides design DEC-4 conservative default); ADR-018 kill-switch; AI rungs never auto-file. |

---

## Empirical Findings (R3 pre-flight, verified 2026-06-05 — carry forward for W2/W6)

1. `EmailComposer/` + Code Page absent → build from scratch.
2. `communicationApi.ts` missing `SendCommunicationError` + `attachmentDriveItemIds` → additive.
3. `SendCommunicationRequest.cs` only `AttachmentDocumentIds` → non-breaking alias.
4. `CommunicationService.cs` no `Internet-Message-Id` capture.
5. Only `CreateMatter/SendEmailStep.tsx` is a true LegalWorkspace fork.
6. `sprk_communication_send.js` ~1,150 LOC × 2 copies.
7. `WorkAssignmentWizardDialog.tsx:31` cross-package import to resolve.
8. Code Page exemplar: `src/client/code-pages/DocumentRelationshipViewer/`.

---

## Decisions Made
*Appended by `task-execute` during execution.*

## Implementation Notes
*No notes yet — first task starts in Wave 0.*
