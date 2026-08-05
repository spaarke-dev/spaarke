# TASK-INDEX — Spaarke Compose R6 (Render-on-Save)

> **Project**: `spaarkeai-compose-r6` · **Branch**: `work/spaarkeai-compose-r6`
> **Source**: [`../plan.md`](../plan.md) · [`../spec.md`](../spec.md) · Governing ADR-049 (Path-B amendment = task 001)
> **Status**: 30 tasks / 8 phases (0–7) — scaffolded, not started. Execute via `task-execute` (root CLAUDE.md §4).

## Legend
🔲 not-started · 🔄 in-progress/needs-retry · ✅ complete · ⛔ blocked
Rigor: FULL / STANDARD / MINIMAL · Tier: model/effort (Sonnet 5 @ high default; opus/xhigh where flagged).

## ⚠️ Coordination (binding)
`Services/Compose/` is the **most-contested surface in the repo** — **every Compose task is `parallel-safe:false`**.
Run **`/conflict-check` before EVERY BFF PR**. Deploy **BFF + `sprk_spaarkeai` together** (anti-clobber). **NEVER delete `docxBridge.ts`.**
Overlapping worktrees: `spaarkeai-compose-r5` (active on `ComposeService.cs`/`ComposeWorkspace.tsx`), `spaarkeai-compose-fidelity-r4.5`, `spaarkeai-compose-r1/r2/r3`, `spaarke-ai-architecture-redesign-r2` (sole owner of `Services/Ai/` — consume `PublicContracts/`, NO fork), `ai-advanced-capabilities-agreements-r1`, `analysis-hub-r1`.

## Registry

| ID | Title | Phase | Deps | Parallel group | Parallel-safe | Rigor | Tier | Status |
|---|---|---|---|---|---|---|---|---|
| 001 | Draft ADR-049 Path-B amendment (render-on-save save path) | 0 | none | none | false | FULL | opus/high | 🔲 |
| 002 | Verify SPE versioning append-only + inventory Documents version APIs | 0 | none | phase0-foundations | true | STANDARD | sonnet/high | 🔲 |
| 003 | Measure BFF publish-size baseline (re-confirm ~49.63 MB) | 0 | none | phase0-foundations | true | MINIMAL | sonnet/medium | 🔲 |
| 004 | Move AppligentNDA_Signed.docx → tests/fixtures/compose-corpus/ (LFS) + manifest row | 0 | none | phase0-foundations | true | STANDARD | sonnet/high | 🔲 |
| 010 | Route Imported docs through render-from-model in SaveAsync; drop count-gate | 1 | 001, 004 | none | false | FULL | opus/xhigh | 🔲 |
| 011 | Generalize ComposeDocumentRenderer.SynthesizeDocument for canonical-model input | 1 | 001, 010 | none | false | FULL | opus/high | 🔲 |
| 012 | Retire ComposeShadowPatchEngine + ComposeBaselineParaIdStamper from save path | 1 | 001, 011 | none | false | FULL | sonnet/high | 🔲 |
| 013 | Seam + regression tests: NDA saves (no 422), edits land, new version | 1 | 004, 012 | none | false | FULL | sonnet/high | 🔲 |
| 014 | Deploy + UAT gate (Phase 1) — BFF + sprk_spaarkeai together | 1 | 013 | none | false | STANDARD | sonnet/high | 🔲 |
| 020 | Canonical document model — generalize ComposeContentModel/projection as the hub | 2 | 014 | none | false | FULL | opus/high | 🔲 |
| 021 | Numbering/lists through the model (reuse NumberingComputationEngine) | 2 | 020 | none | false | FULL | sonnet/high | 🔲 |
| 022 | Tables through the model (reuse R5 tracked-table work) | 2 | 020 | none | false | FULL | sonnet/high | 🔲 |
| 023 | Headers/footers + page breaks through the model | 2 | 020 | none | false | STANDARD | sonnet/high | 🔲 |
| 024 | Hyperlinks + comments through the model | 2 | 020 | none | false | STANDARD | sonnet/high | 🔲 |
| 025 | Tracked-changes (redlines) through the model | 2 | 020 | none | false | FULL | sonnet/high | 🔲 |
| 026 | Hard-tier graceful degradation (text boxes/drawings/fields/content controls → accept-flatten + warning) | 2 | 020 | none | false | FULL | sonnet/high | 🔲 |
| 027 | Fidelity seam tests across the corpus | 2 | 021,022,023,024,025,026 | none | false | FULL | sonnet/high | 🔲 |
| 030 | Part-merge engine in Services/Compose (body → firm/matter .dotx) | 3 | 020 | P3 | false | FULL | opus/high | 🔲 |
| 031 | Template storage/variable rendering reuse (template entity + ITemplateEngine) | 3 | 030 | P3 | false | STANDARD | sonnet/high | 🔲 |
| 032 | Endpoint + client wiring for template selection/merge | 3 | 031 | P3 | false | FULL | sonnet/high | 🔲 |
| 033 | Part-merge tests + Placement/Component Justification | 3 | 032 | P3 | false | STANDARD | sonnet/high | 🔲 |
| 040 | PDF → canonical model via DocumentIntelligenceService/DocumentParserRouter | 4 | 020 | P4 | false | FULL | opus/high | 🔲 |
| 041 | Open PDF in Compose → edit → save as docx version (client wiring) | 4 | 040 | P4 | false | FULL | sonnet/high | 🔲 |
| 042 | PDF intake tests + lossiness-expectation UX | 4 | 041 | P4 | false | STANDARD | sonnet/high | 🔲 |
| 050 | New OBO list-versions + open-prior-version (read-only) endpoint | 5 | 002 | P5 | false | FULL | opus/high | 🔲 |
| 051 | Documents surface version-history entry point (AllDocuments) | 5 | 050 | P5 | false | FULL | sonnet/high | 🔲 |
| 052 | Version-history tests — open v3 after v4 = exact bytes (+ authz negative) | 5 | 050, 051 | P5 | false | FULL | sonnet/high | 🔲 |
| 060 | Round-trip fidelity harness — fail on hard-fail/regression | 6 | 027, 004 | none | false | FULL | sonnet/high | 🔲 |
| 061 | Wire fidelity harness into CI as a release gate | 6 | 060 | none | false | FULL | sonnet/high | 🔲 |
| 090 | Project wrap-up — anti-clobber deploy, ADR-049 amendment, /test-diet, 6 criteria | 7 | 014,027,033,042,052,061 | none | false | STANDARD | sonnet/high | 🔲 |

## Critical path
`001 → 010 → 011 → 012 → 013 → 014 → 020 → 027 → 060 → 061 → 090`

Task **001 (ADR-049 Path-B amendment) gates Phase 1** — it MUST merge with or before tasks 010/011/012 (their `<gate>` depends on the merged amendment).

## Dependency notes
- **Phase 0** (001–004): 001 (amendment) and 004 (NDA fixture) are Phase-1 prerequisites. 002 (SPE versioning verify + Documents version-API inventory) gates Phase 5. 003 (publish baseline) informs every BFF task's size delta.
- **Phase 1** (010–014): strict chain; the 422-kill cutover.
- **Phase 2** (020–027): 020 anchors; 021–026 fan off 020; 027 depends on all six.
- **Phases 3 / 4 / 5** are cross-phase-independent once their prerequisites land (Phase 2 / task 020 / task 002), but each still **serializes its own Compose-file edits** (`parallel-safe:false`). Groups `P3`, `P4`, `P5` mark this intra-phase-only structure — they are NOT concurrent-across-groups permissions.
- **Phase 6** (060–061): harness depends on Phase 2 (027) + NDA fixture (004).
- **Phase 7** (090): wrap-up gates on the terminal task of each phase.

## Parallel Execution Groups
Because `Services/Compose/` is the most-contested surface, **there are no concurrent multi-agent waves within a phase.** Tasks execute largely sequentially, each running `/conflict-check` before its BFF PR. The only genuine parallelism is **cross-phase** (Phase 3 vs 4 vs 5) once prerequisites are met — and even then each phase serializes its own shared-file edits. Do NOT dispatch 2+ Compose tasks to concurrent agents.

| Group | Tasks | Prerequisite | Notes |
|---|---|---|---|
| phase0-foundations | 002, 003, 004 | none | non-Compose (SPE inventory / publish measure / fixture) — `parallel-safe:true`; 001 runs main-session-only (`.claude/adr/`) |
| (Phase 1) | 010→011→012→013→014 | 001, 004 | strict serial chain |
| (Phase 2) | 020 → {021..026} → 027 | 014 | 021–026 serialize Compose edits despite fanning off 020 |
| P3 | 030→031→032→033 | 020 | template part-merge; serial |
| P4 | 040→041→042 | 020 | PDF intake; serial; may proceed in parallel with P3/P5 at the phase level (not file level) |
| P5 | 050→051→052 | 002 | version-history open UX; serial |
| (Phase 6) | 060→061 | 027, 004 | fidelity harness → CI gate |

## High-risk items
- **001 → Phase 1 ordering**: the ADR-049 amendment must land before the render-on-save code merges (§6.5 Path B).
- **010/011 (the pivot)**: opus/xhigh; the core rip-out of the surgical save path. Escalate per §6/§6.5 if the render-from-model path can't reproduce a fidelity feature the surgical path preserved.
- **026 (hard-tier)**: MUST accept-flatten + warn, never 422 — these are the exact NDA breakers.
- **Shared-surface contention**: every Compose PR risks collision with `spaarkeai-compose-r5` and siblings — `/conflict-check` is mandatory, not advisory.
