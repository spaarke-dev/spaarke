# Spaarke Compose R6 — Render-on-Save Canonical Model & Word-Parity Fidelity

> **Status**: ✅ **Complete** (100% — 30/30 tasks; closed 2026-08-13)
> **Branch**: `work/spaarkeai-compose-r6` · **Created**: 2026-08-05 · **Closed**: 2026-08-13
> **Governing ADR**: [ADR-049](../../.claude/adr/ADR-049-compose-shadow-document.md) — R6 Path-B amendment (task 001, merged to master with the code)
> **Deployed**: full surface live on dev since 2026-08-07 (atomic BFF + `sprk_spaarkeai` window; verified by live-bundle markers + route probes)

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

## Graduation criteria (from spec Success Criteria) — ALL MET at close (2026-08-13)

1. [x] NDA saves succeed (no 422), new version, edits land, no surgical-anchor on the save path — **operator UAT PASS 2026-08-06 on the REAL signed Corteva NDA** (repeated saves, redlines in Word, 7+ SPE versions) + `tests/integration/regression/NdaSaveNo422RegressionTests.cs` + task-012 static reachability proof (surgical engine reachable only from the transitional op-log path per the ADR-049 amendment).
2. [x] PDF NDA opens → edits → saves as a docx version — `ComposePdfIntakeRoundTripSeamTests` (5 facts: full round trip w/ ADR-024 association inheritance, never-replace-the-.pdf, 503/422 honesty) + live on dev (banner markers verified in the deployed bundle).
3. [x] Template merge carries headers/footers/styles — `ComposeTemplateChromeProvenanceSeamTests` + `ComposeApplyTemplateSeamTests` on real corpus bytes (task 033; chrome-provenance seam).
4. [x] Prior version opens read-only with exact bytes — `SpeVersionHistoryOboSeamTests` 7/7 (byte-equality v3-while-v4-latest, mutation-never, restore-route-absent, 401/403 negatives) + operator UAT (opened the 11-min-ago version, exact prior state).
5. [x] Fidelity harness gates CI — merge-blocking `compose-fidelity-gate` job in `.github/workflows/sdap-ci.yml:750` running `ComposeFidelityGateHarnessTests` over the corpus (tasks 060/061; red-on-regression proven at authoring).
6. [x] Publish 47.06 MB ≤ 60 MB ceiling (incl. PDBs; Δ +0.06 vs baseline) · CVE scan clean (`dotnet list package --vulnerable --include-transitive`) · Placement Justifications recorded for every new component (part-merge engine, PDF-intake facade, version endpoints — notes §21–23 + PR descriptions).

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

## Changelog

- **2026-08-05** — Project initialized; spec + 30-task plan; ADR-049 R6 Path-B amendment drafted (001). Critical-path re-sequenced same day (model-first: 020+026 before the 010/012 cutover) after a code trace found the dependency inversion.
- **2026-08-06** — Phases 1+2 complete (canonical model, fidelity tiers, THE CUTOVER: imported saves render from the model; surgical path retired to transitional-only). Deployed atomically + operator UAT PASS on the real Corteva NDA (PR #745). Defects D1–D7 triaged to register, none hot-patched.
- **2026-08-07** — Phases 3+4+5+6 complete: template part-merge (030–033), PDF intake (040–042, incl. B-MED-3 association inheritance), version-history OBO endpoints + AllDocuments UX (050–052), fidelity CI gate (060–061). Merged via PR #747 + #748; full surface deployed atomically 17:25/17:26Z.
- **2026-08-13** — Wrap-up (090): ADR-049 amendment verified on master; anti-clobber verify confirmed live artifacts a strict superset (r2 layered on top — no redeploy); /test-diet: 281 tests, ZERO scaffolding ([test-diet-report](notes/test-diet-report.md)); cross-slice close-out review; all 6 Success Criteria verified; defer register published to `projects/spaarkeai-compose-r7/notes/`; [lessons-learned](notes/lessons-learned.md). **Project closed.**

## Post-close pointers

- **Defer register (D1–D9 + ledger + wideners)** → `projects/spaarkeai-compose-r7/notes/r6-defer-register-consolidated.md`
- **D9 handoff** → `projects/spaarkeai-assistant-enhancements-r3/notes/assistant-viewport-clipping-open-in-compose-handoff.md`
- **Open operator decision** → Corteva NDA confidentiality sign-off for corpus row 4 (file untracked until then)
- **Telemetry trigger** → when the `TRANSITIONAL op-log save shape` Warning decays to zero: delete `ComposeShadowPatchEngine` + count-gate (+ their tests). NEVER delete `docxBridge.ts`.
