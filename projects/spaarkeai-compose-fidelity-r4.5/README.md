# Spaarke Compose — Legal Fidelity (R4.5)

> **Status**: 🟡 In Progress (initialized 2026-07-28 via `/project-pipeline`)
> **Branch**: `work/spaarkeai-compose-fidelity-r4.5`
> **Relationship**: Priority interstitial between R4 (Shadow Document Architecture — shipped, master `a58c0b5cc` 2026-07-24) and R5 (Editing Completeness — backlog). Absorbs R5 **G6**.

## What this project delivers

Finishes R4's **read + reference** promise for legal-grade fidelity. R4 built the high-fidelity server projection but wired it into only one doorway (stored-doc Load), left uploads/browse on the lossy `mammoth` client reader, and never computed or persisted the displayed numbering or a `paraId → legal-number` reference. R4.5 closes those gaps:

- **WS-1 — One reader everywhere** (absorbs G6): every entry path (upload / browse / open-in-compose) renders through the server projection; delete the `mammoth` fallback.
- **WS-2 — Harden the read**: fix silent drops (`w:sym`, `w:cr`), emit indentation, warn-don't-drop, construct audit.
- **WS-3 — Deterministic numbering** (flagship): compute clause/section/heading/list numbers 100% from the OOXML numbering model; render exactly as Word does.
- **WS-4 — Reference / citation layer**: persist + expose `paraId → {computed number, level}`; resolve "Section 4.2" / "4.2(b)(iii)" / "Sections 4–7".
- **WS-5 — Page/line numbering spike**: LibreOffice-vs-Word-service evaluation, measured divergence, licensing path → ship-or-defer decision.

**Scope boundary (one sentence):** R4.5 is about *reading* a legal document with perfect fidelity and making it *referenceable*; R5 is about *editing* it with full formatting fidelity.

## Graduation criteria

The project is **Complete** when all six spec success criteria pass (see [`spec.md`](spec.md) §Success Criteria):

1. ✅ One reader — `mammoth` zero Compose call sites (grep-proven).
2. ✅ Text-exact — character-for-character; `w:sym`/`w:cr` represented-or-warned; indentation preserved.
3. ✅ Numbering-exact — identical to Word (golden harness); interrupted / multi-level / style-linked all 100%.
4. ✅ Referenceable — `paraId → {computed number, level}` resolves single/sub-item/range citations; survives edits.
5. ✅ Page/line honest — WS-5 decision + measured divergence; no over-claim.
6. ✅ Hygiene — build + Compose suite + fidelity harness green; publish ≤60 MB; NetArch green; no new HIGH CVE; `/conflict-check` clean.

## Out of scope (stays in R5)

G1/G2 (authored-doc lifecycle + clean apply), **G3** (`setBlockAttr` edit-path — *couples to WS-3's numbering model*), G4/G5 (table/hyperlink ops), G7 (Save-Version UX), G8 (external-change banner), G9 (comment scroll-sync), G10 (Document Profile re-run). Also: pagination *implementation* (WS-5 is decision-only), edit-time live renumber (R5 G3), byte-authoring changes.

## Artifacts

| File | Purpose |
|---|---|
| [`design.md`](design.md) | Human design — rationale, code-grounded evidence, locked decisions |
| [`spec.md`](spec.md) | AI-optimized spec — 19 FRs, 6 NFRs, ADR Tensions, success criteria |
| [`plan.md`](plan.md) | Phased WBS + parallel strategy + discovered resources |
| [`CLAUDE.md`](CLAUDE.md) | Project context loaded per session |
| [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) | Task registry, dependencies, parallel groups |
| `current-task.md` | Active-task state (context recovery) |

## Coordination (hot-path)

BFF=Y (`Services/Compose/**`) + SpaarkeAi=Y (`Spaarke.Compose.Components`). Overlaps `spaarkeai-compose-r1/r2/r3/r4` + `spaarke-ai-architecture-redesign-r2`. **`parallel-safe: false` on all `Services/Compose/` tasks; run `/conflict-check` before every BFF PR.** Consume `Services/Ai/PublicContracts/` seams — **no fork**. Watch PRs #690 (LFS corpus) + #266 (OpenXml bump).

## How to execute

Say **"continue"** or **"work on task 001"** — the `task-execute` skill loads ADRs/constraints/patterns, tracks `current-task.md`, checkpoints, and runs the Step 9.5 quality gates. Do **not** implement POML files manually.
