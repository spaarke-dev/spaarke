# CLAUDE.md — Spaarke Compose R4 (project context)

> Loads when working in this project. Extends root `CLAUDE.md`; does not override it.

## What this project is

Rip-and-replace of the Compose translation/save layer with a **Shadow Document Architecture**. OOXML is server-authoritative; TipTap is a lossy view + controller; edits are `(paraId, runIndex, run-local-offset)`-anchored operations applied surgically by ONE `ComposeShadowPatchEngine`. **No text-search in the write path.** MISSION CRITICAL, hard-replace cutover gated by a Phase 0 proof.

Source of truth: [`spec.md`](spec.md). Rationale + locked decisions: [`design.md`](design.md). Execution: [`plan.md`](plan.md) + [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md).

## 🚨 MANDATORY: Task Execution Protocol

When working a task here, **invoke the `task-execute` skill** — do NOT read POML files and implement manually. `task-execute` loads ADRs/constraints/patterns, tracks `current-task.md`, checkpoints every 3 steps, and runs the Step 9.5 quality gates. See root `CLAUDE.md` §4.

Trigger phrases: "continue" / "next task" → read `tasks/TASK-INDEX.md`, find first 🔲, invoke `task-execute`. "work on task NNN" → invoke `task-execute` with that POML.

## Binding rules for THIS project

### The 7 invariants (I-1…I-7) — every task inherits
1. One authoritative model = the real OOXML package (never wholesale-regenerated for a loaded doc).
2. Server-authoritative — the client never authors `.docx` bytes.
3. Stable addressing — every editable node carries `w14:paraId`; never text-search, never absolute position.
4. Edits are operations, applied surgically; untouched XML subtrees byte-identical after save.
5. One byte-author — a single Patch Engine writes the package.
6. Client is a view + controller — TipTap renders the projection and emits operations.
7. No text-search anchoring in the write path (fuzzy = below-threshold surface-as-comment last resort only).

### Locked decisions (D1–D5) — binding, do not re-litigate
- **D1** step-level operational deltas (not `getHTML`/paragraph-diff).
- **D2** anchor `(paraId, runIndex, run-local-offset)` — never run-ids, never absolute positions.
- **D3** `docx` end-to-end now; pdf/xlsx/pptx are LATER phases.
- **D4** SPE = store + open-in-Office launch; versioning/lock/423 in scope; WOPI-embed out.
- **D5** one unified `ComposeShadowPatchEngine` replaces both legacy writers.

### Phase 0 gate is a HARD prerequisite
No old-path deletion (tasks 023, 032, 060) may proceed until task **006** (Phase 0 gate) is green: operation schema + applier spike on the CIPO doc + corpus byte-diff harness all passing. This is the hard-replace safety net (NFR-08). Silent bypass is a §6 escalation.

## BFF Hygiene (root §10) — this project is BFF=Y

Every BFF-touching task MUST:
- State the **Placement Justification** in the PR (cite `.claude/constraints/bff-extensions.md`). Save/patch orchestration stays in `Services/Compose/`.
- Keep `Services/Compose/` **pure** — no `IOpenAiClient`/executor/routing type (ADR-013 Tier-1 NetArchTest); no `Microsoft.Graph` type above `SpeFileStore` (ADR-007). Patch Engine is `byte[]`-in/`byte[]`-out.
- **Verify publish size** ≤60 MB compressed; report absolute + delta vs ~49.63 MB baseline (incl. PDBs). Zero new runtime package expected (Docxodus only if the Phase-0 A/B selects it → verify delta).
- Add/update **seam slices** in `tests/integration/seam/**` for every save/load/dispatch change (ADR-038; NO `Mock<HttpMessageHandler>`, DI-registration, or ctor-null tests).
- Run **`/conflict-check` before every BFF PR** — `Services/Compose/` overlaps `spaarkeai-compose-r1/r2/r3` and `spaarke-ai-architecture-redesign-r2`.

## Coordination

- **Consume `Services/Ai/PublicContracts/` seams only — NO fork of `Services/Ai/`** (`spaarke-ai-architecture-redesign-r2` is sole owner). AI redline path stays envelope-only; **no new AI dispatch endpoint, engine frozen** (ADR-039).
- **`spaarkeai-compose-r3`** is the deployed base R4 extends (Phase-1 + Bug-A). Its `ComposeParagraphRedlineSynthesizer` is the thing R4 retires — coordinate the `Services/Compose/` cutover.
- Resolve the **notifications build break** on tip-of-master before any SpaarkeAi deploy from tip.

## Applicable ADRs (quick reference)

| ADR | Rule |
|---|---|
| ADR-013 | `.claude/adr/ADR-013-ai-architecture.md` — AI facade; no AI internals in `Services/Compose/` |
| ADR-007 | `.claude/adr/ADR-007-spefilestore.md` — no `Microsoft.Graph` above `SpeFileStore` |
| ADR-009 | `.claude/adr/ADR-009-redis-caching.md` — version/re-anchor state via `IDistributedCache` |
| ADR-010 | `.claude/adr/ADR-010-di-minimalism.md` — Patch Engine = stateless concrete singleton |
| ADR-028 | `.claude/adr/ADR-028-spaarke-auth-architecture.md` — client fetches via `@spaarke/auth` |
| ADR-038 | `docs/adr/ADR-038-testing-strategy.md` — seam DoD; banned mock/DI/ctor tests |
| ADR-039/040 | `.claude/adr/ADR-039-grounded-execution-closed-catalogs.md`, `ADR-040-session-ledger.md` — engine frozen; no new dispatch |

## Licensing (NFR-03) — HARD

MIT/permissive only. No commercial / per-seat / AGPL. **No TipTap Pro** (`@tiptap-pro/*` forbidden) — MIT base + `@tiptap/extension-*` only. **No runtime dependency on EigenPal** (official repo is a closed facade; the frozen Apache-2.0 fork is study-reference only).

## Entry points

| Surface | Start here |
|---|---|
| Compose services | `src/server/api/Sprk.Bff.Api/Services/Compose/` |
| Compose endpoints | `src/server/api/Sprk.Bff.Api/Api/ComposeEndpoints.cs` |
| Compose client | `src/client/shared/Spaarke.Compose.Components/src/` |
| Seam tests | `tests/integration/seam/` · unit: `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/` |
