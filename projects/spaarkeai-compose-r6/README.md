# Spaarke Compose R6 — Render-on-Save Canonical Model & Word-Parity Fidelity

> **Status**: Initialized (scaffold complete; tasks decomposed; not yet executing)
> **Branch**: `work/spaarkeai-compose-r6` · **Created**: 2026-08-05
> **Governing ADR**: [ADR-049](../../.claude/adr/ADR-049-compose-shadow-document.md) — Path-B amendment (task 001)

## Overview

Every Compose save failure across R3→R5 was the same bug class — reconciling anchors between the TipTap
editor model and the server-authoritative OOXML, patched reactively one divergence at a time. R6 ends the
treadmill: **save renders a fresh docx from a canonical document model into a new immutable SPE version**,
never a surgical byte-patch. This **eliminates the 422 anchor bug class by construction**.

R6 also makes **PDF a first-class intake source**, adds **Word template part-merge** for house-style chrome,
a **Documents version-history open UX** (the safety net made real), and a **round-trip fidelity CI harness**
seeded with `AppligentNDA_Signed.docx` — moving divergence discovery from UAT to CI.

## Scope (locked)

| In R6 | Deferred (fast-follow) | Out (permanently / other project) |
|---|---|---|
| Render-on-save core (kill the 422) | PDF **export** (LibreOffice sidecar) | Word add-in / Office.js ("Option B") |
| Canonical model + near-term fidelity tier | Version **restore / branch-from** | Replacing TipTap |
| Template part-merge (in `Services/Compose`) | | Word-grade lossless round-trip (dropped) |
| **PDF intake** (Azure DI) | | Tactical/surgical NDA anchor fix (declined) |
| Version-history **open/view** (read-only) | | Page/line pagination (R4.5-deferred) |
| Round-trip fidelity CI harness + ADR-049 amendment | | |

## Graduation criteria (from spec Success Criteria)

1. [ ] Saving `AppligentNDA_Signed.docx` after edits succeeds (no 422), new version, edits land — no surgical-anchor code on the save path.
2. [ ] A PDF NDA opens in Compose, is edited, and saves as a docx version.
3. [ ] A document merged through a firm template carries that template's headers/footers/styles.
4. [ ] A user opens a prior version (v3 after v4) from the Documents surface and gets the exact bytes (read-only).
5. [ ] The round-trip fidelity harness runs in CI and gates the release.
6. [ ] Publish size ≤60 MB; no new HIGH CVE; BFF placement justified for every new component.

## Hot-path & coordination

BFF=Y · SpaarkeAi=Y · CI=Y · Skills=N · root-CLAUDE=N. `Services/Compose/` is the **most-contested surface
in the repo** — `parallel-safe:false` on all Compose tasks; `/conflict-check` before every BFF PR; deploy
BFF + `sprk_spaarkeai` together; NEVER delete `docxBridge.ts`. See [`CLAUDE.md`](CLAUDE.md) Coordination.

## Documents

- [`spec.md`](spec.md) — AI implementation specification (source of truth)
- [`design.md`](design.md) — hand-authored design + rationale (preserved verbatim)
- [`plan.md`](plan.md) — phased WBS
- [`CLAUDE.md`](CLAUDE.md) — project working context
- [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) — task registry, dependencies, parallel groups
- `current-task.md` — active task tracker (context recovery)

## Next step

Review the task plan in [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md), then begin execution with **task 001**
(the ADR-049 Path-B amendment — must merge before Phase-1 code) via the `task-execute` skill.
