# PLAN — Spaarke Compose R6: Render-on-Save Canonical Model & Word-Parity Fidelity

> **Source**: [`spec.md`](spec.md) · **Design**: [`design.md`](design.md)
> **Status**: Ready for task decomposition
> **Governing ADR**: [ADR-049](../../.claude/adr/ADR-049-compose-shadow-document.md) — **Path-B amendment (task 001)**
> **Hot-path**: BFF=Y · SpaarkeAi=Y · CI=Y · Skills=N (task 001 authors the ADR amendment — main-session only) · root-CLAUDE=N

---

## 1. Objective

Re-architect Compose around **render-on-save**: every save renders a fresh docx from a canonical document
model into a new immutable SPE version — no surgical byte-patch of inherited XML. This eliminates the
anchor-reconciliation 422 bug class *by construction*. Add PDF intake (first-class), Word template
part-merge, a Documents version-history open UX, and a round-trip fidelity CI harness seeded with the NDA.

## 2. Architecture Context

### Discovered Resources (project-pipeline Step 2)

**Applicable ADRs**
- **ADR-049** (Compose Shadow Document) — governing; **Path-B amendment** for the save path (I-4 + line-40
  "no re-derive on save" superseded *for save only*; I-7 satisfied trivially by rendering).
- **ADR-007** (SpeFileStore facade) — `Services/Compose/` stays `byte[]`-in/projection-out; no `Microsoft.Graph` above `SpeFileStore`.
- **ADR-013** (AI Architecture) — no AI-internal types in `Services/Compose/`; consume `Services/Ai/PublicContracts/` seams only, NO fork.
- **ADR-039** (Grounded Execution) — engine frozen; no new AI dispatch endpoint.
- **ADR-040** (Session Ledger) — persist canonical model / projection payload per existing pattern.
- **ADR-029 / ADR-010** (BFF publish hygiene / DI minimalism) — ≤60 MB ceiling; minimal DI.
- **ADR-038** (Testing Strategy) — seam + fidelity-harness DoD; banned mock/DI/ctor tests.

**Canonical implementations to reuse (do NOT rebuild)**
- `Services/Compose/ComposeDocumentRenderer.cs:102` `SynthesizeDocument` — the render-from-model precedent (already runs for born-in-editor docs); `:142` `AppendSection` (second author-not-patch precedent).
- `Services/Compose/ComposeService.cs:642` `SaveAsync` — Authored/Imported branch at `:714`; SPE persist via `ReplaceFileContentAsUserAsync` (auto-versions); Redis eTag stamp; stale-base re-anchor.
- `Services/Compose/ComposeDocxProjectionBuilder.cs:1357` `NumberingComputationEngine` (R4.5, deterministic `numId`-scoped).
- `Services/Compose/ComposeBaselineParaIdStamper.cs:113` — the count-gate (the 422 root; retired from save path).
- `Services/Ai/DocumentIntelligenceService.cs` + `DocumentParserRouter.cs` + `ITextExtractor` — PDF/DOCX → `ParsedDocument` (PDF intake reuse).
- `Services/Ai/Delivery/WordTemplateService.cs` + `ITemplateEngine` (Handlebars) + Dataverse `template` entity — template *storage/variable* rendering only (part-merge built in `Services/Compose`).
- `Api/ContainerItemEndpoints.cs:48` (admin version-list) + `DriveItemOperations.DownloadFileVersionAsUserAsync:842` (OBO primitive, unexposed) — version-history reuse.
- `tests/fixtures/compose-corpus/` — CIPO doc, Engagement Letter, 5 synthetic numbering exemplars, placeholder rows; `DocumentFormat.OpenXml` 3.5.1.

**Skills**: `code-review`, `adr-check`, `conflict-check` (before every BFF PR), `bff-deploy` + `code-page-deploy` (deploy BFF + `sprk_spaarkeai` together), `task-execute`, `test-diet` (wrap-up).

### Hot-path coordination (from `projects/INDEX.md`)
`Services/Compose/` is the **most-contested BFF surface**. Active overlapping worktrees:
`spaarkeai-compose-r5` (active on `ComposeService.cs`/`ComposeWorkspace.tsx`), `spaarkeai-compose-fidelity-r4.5`,
`spaarkeai-compose-r1/r2/r3`, `spaarke-ai-architecture-redesign-r2` (**sole owner of `Services/Ai/`**),
`ai-advanced-capabilities-agreements-r1` + `analysis-hub-r1` (touch `Services/Compose` + `ComposeWorkspace`).
→ **`parallel-safe:false` on ALL Compose tasks; `/conflict-check` before EVERY BFF PR; NEVER delete `docxBridge.ts`.**

## 3. Owner Clarifications (locked — see spec)
- **Scope** = full 7 phases, **PDF intake only** (export deferred), **version-history open-only** (restore/branch deferred).
- **Template-merge** = **part-merge-in-Compose** (native `documenttemplate` rejected; §3-Q3 investigation dropped).
- **Corpus** = proceed on NDA + synthetic; owner worst-offender slots stay open.
- **PDF-export licensing** = deferred to the export fast-follow (2 R4.5 sign-offs carried forward).

## 4. ADR Tensions
| ADR | Rule | Path | Note |
|---|---|---|---|
| ADR-049 | I-4 (byte-identical untouched subtrees) + line-40 (no re-derive on save) | **B — amendment** | Save path only; **must merge with/before dependent code** (task 001 gates Phase 1). |
| BFF §10 | placement | **A — documented exception** | PDF intake (extend Azure DI) + template-merge (extend Services/Compose); Placement Justification per task. |

---

## 5. Phase Breakdown (WBS)

Numbering uses 10-gaps for insertions. Every task carries the canonical POML field set (rigor, model-tier,
effort, parallel-group/safe, escalation, justification for new surface). Default execution tier: **Sonnet 5 @ high**;
`opus`/`xhigh` flagged where noted. **All Compose-touching tasks are `parallel-safe:false`** (shared-file contention).

> **⚠️ RE-SEQUENCED 2026-08-05 (execution order ≠ phase-number order).** A Step-2 code trace on task 010 found a
> dependency inversion: the render-on-save cutover (010/011/012) needs the canonical-model hub + OOXML→model
> projection + hard-tier accept-flatten that Phase 2 (020–026) builds — otherwise it re-ships the fixed UAT #1A SEV-1
> regression (imported docs rendered from the thin model drop headers/footers/text-boxes; `ComposeWorkspace.tsx:1432`).
> **Corrected execution order: 020 → {011, 021–026} → 010 → 012 → {013, 027} → 014.** The phase-number labels below are
> retained (stable task IDs); the binding order is the **§6 critical path**. Owner-authorized re-sequence; see
> [`notes/task-010-resequence-decision.md`](../notes/task-010-resequence-decision.md). ADR-049 amendment (001) unchanged.

### Phase 0 — Foundations & gates (human/verify) — *blocks Phase 1*
| Task | Title | Tags | Rigor | Tier | Notes |
|---|---|---|---|---|---|
| **001** | Draft ADR-049 Path-B amendment (render-on-save save path) | adr, docs | FULL | **opus** | main-session only (`.claude/adr/`); §6.5 Path B; codifies the 4 amendment points; **must merge with/before Phase 1 code** |
| **002** | Verify SPE versioning append-only + inventory Documents version APIs | verify, spe | STANDARD | sonnet | Q1 gate; confirm against live Documents surface; output → `notes/spe-versioning-verify.md` |
| **003** | Measure BFF publish-size baseline (re-confirm ~49.63 MB) | verify, bff | MINIMAL | sonnet | `dotnet publish -c Release`; record incl./excl. PDB; sets the per-task delta reference |
| **004** | Move `AppligentNDA_Signed.docx` → `tests/fixtures/compose-corpus/` (LFS) + manifest row | test-fixture | STANDARD | sonnet | Git-LFS; add feature-coverage row to `corpus-manifest.md` (text-boxes / `mc:AlternateContent` / duplicate paraIds) |

### Phase 1 — Render-on-save cutover (kill the 422) — *RUNS AFTER 020+026 (re-sequenced); 010 deps 011+026, not 001/004 directly*
| Task | Title | Tags | Rigor | Tier | Notes |
|---|---|---|---|---|---|
| **010** | **Finalize** the Imported save-path cutover through render-from-model; drop count-gate | bff, compose | FULL | **opus** | *deps 011+026.* Makes render-from-model the DEFAULT for `Imported` + removes the count-gate — builds on 020's projection + render-out wiring and 026's hard-tier accept-flatten (so the NDA degrades, never 422s) |
| **011** | Generalize `ComposeDocumentRenderer.SynthesizeDocument` for imported/canonical model input | bff, compose | FULL | **opus** | *deps 020.* Render from the canonical model, not just born-in-editor blocks; pairs with 020's model build |
| **012** | Retire `ComposeShadowPatchEngine` + `ComposeBaselineParaIdStamper` from the save path | bff, compose | FULL | sonnet | *deps 010.* Retain surgical engine only for transitional clean-apply per the amendment; NEVER delete `docxBridge.ts` |
| **013** | Seam + regression tests: NDA saves (no 422), edits land, new SPE version produced | testing, seam | FULL | sonnet | *deps 004, 012.* `tests/integration/seam/Compose/**` + regression (NDA 422 regression) |
| **014** | Deploy + UAT gate (render-on-save + fidelity) — BFF + `sprk_spaarkeai` together | deploy | STANDARD | sonnet | *deps 013, 027.* Anti-clobber verify; `/conflict-check`; manual NDA UAT (ships cutover + fidelity together) |

### Phase 2 — Canonical model + fidelity widening (near-term tier) — *RUNS FIRST (model-first re-sequence); 020 deps 001+004*
| Task | Title | Tags | Rigor | Tier | Notes |
|---|---|---|---|---|---|
| **020** | Canonical document model — generalize `ComposeContentModel`/projection as the single hub | bff, compose | FULL | **opus** | *deps 001, 004 (was 014 — inversion fixed).* THE project anchor: docx→canonical-model projection (the imported-doc "source") + render-out wiring; justification (extend, not new) |
| **021** | Numbering/lists through the model (reuse `NumberingComputationEngine`) | bff, compose | FULL | sonnet | golden-label parity vs corpus exemplars |
| **022** | Tables through the model (reuse R5 tracked-table work) | bff, compose | FULL | sonnet | |
| **023** | Headers/footers + page breaks through the model | bff, compose | STANDARD | sonnet | |
| **024** | Hyperlinks + comments through the model | bff, compose | STANDARD | sonnet | |
| **025** | Tracked-changes (redlines) through the model | bff, compose | FULL | sonnet | |
| **026** | Hard-tier graceful degradation (text boxes/drawings/fields/content controls → accept-flatten + warning) | bff, compose | FULL | sonnet | the exact NDA breakers; MUST NOT 422 |
| **027** | Fidelity seam tests across the corpus | testing, seam | FULL | sonnet | *deps 021–026, 012.* Per-feature round-trip; hard-tier warns-not-fails; runs after the cutover so it tests the shipped path |

### Phase 3 — Template part-merge — *depends on Phase 2*
| Task | Title | Tags | Rigor | Tier | Notes |
|---|---|---|---|---|---|
| **030** | Part-merge engine in `Services/Compose` (body → firm/matter `.dotx`: styles/numbering/theme/headers/footers/`sectPr`) | bff, compose | FULL | **opus** | direct OOXML part-merge, NOT `altChunk`; **NEW component** → §11 justification |
| **031** | Template storage/variable rendering reuse (`template` entity + `ITemplateEngine`) | bff | STANDARD | sonnet | no parallel subsystem |
| **032** | Endpoint + client wiring for template selection/merge | bff, compose, frontend | FULL | sonnet | |
| **033** | Part-merge tests + Placement/Component Justification | testing, seam | STANDARD | sonnet | assert template chrome provenance |

### Phase 4 — PDF intake — *depends on Phase 2 (canonical model)*
| Task | Title | Tags | Rigor | Tier | Notes |
|---|---|---|---|---|---|
| **040** | PDF → canonical model via `DocumentIntelligenceService`/`DocumentParserRouter` | bff | FULL | **opus** | extend existing Azure DI; Placement Justification (managed service) |
| **041** | Open PDF in Compose → edit → save as docx version (client wiring) | frontend, compose | FULL | sonnet | honest lossiness UX copy |
| **042** | PDF intake tests + lossiness-expectation UX | testing, seam | STANDARD | sonnet | end-to-end PDF NDA |

### Phase 5 — Documents version-history open UX (read-only) — *depends on 002*
| Task | Title | Tags | Rigor | Tier | Notes |
|---|---|---|---|---|---|
| **050** | New **OBO** list-versions + open-prior-version (read-only) endpoint | bff, auth | FULL | **opus** | reuse `DownloadFileVersionAsUserAsync:842`; **NEW endpoint** → §11 justification; OBO auth path |
| **051** | Documents surface version-history entry point (`AllDocuments`) | frontend | FULL | sonnet | add affordance; ui-tests |
| **052** | Version-history tests (open v3 after v4 = exact bytes) | testing, seam | STANDARD | sonnet | |

### Phase 6 — Round-trip fidelity harness (CI release gate) — *depends on Phases 1–2*
| Task | Title | Tags | Rigor | Tier | Notes |
|---|---|---|---|---|---|
| **060** | Fidelity harness — round-trip corpus; fail on hard-fail/regression | testing, ci | FULL | sonnet | seeded with the NDA fixture (004) |
| **061** | Wire harness into CI as a release gate | ci | STANDARD | sonnet | coordinate `sdap-ci.yml` (see INDEX.md CI overlap) |

### Phase 7 — Wrap-up
| Task | Title | Tags | Rigor | Tier | Notes |
|---|---|---|---|---|---|
| **090** | Project wrap-up: anti-clobber deploy, ADR-049 amendment merged, `/test-diet`, lessons-learned, README complete | wrapup, deploy | STANDARD | sonnet | Step 11 `/test-diet` gate; verify all 6 success criteria |

**Task count**: ~30 across 8 phases (0–7).

## 6. Critical Path (RE-SEQUENCED 2026-08-05 — binding execution order)
`001 (ADR amendment) → 020 (canonical model hub + docx→model projection) → {011 renderer, 021–026 fidelity + hard-tier}
→ 010 (Imported cutover + drop count-gate) → 012 (retire surgical from save path) → {013 regression, 027 fidelity seam}
→ 014 (deploy render-on-save + fidelity) → 060 → 061 → 090`

**Why 020 precedes 010** (the correction): render-from-model needs a *faithful canonical-model source* for imported
docs (020's docx→model projection) and *hard-tier accept-flatten* (026) so the NDA degrades instead of 422-ing or
silently dropping text-boxes. Building the hub first is the only order that doesn't re-ship the UAT #1A SEV-1
regression. See [`notes/task-010-resequence-decision.md`](../notes/task-010-resequence-decision.md).

Phase 3 (template-merge), Phase 4 (PDF intake), and Phase 5 (version UX) branch off after their prerequisites
(task 020 / task 002) and rejoin at the harness + wrap-up. They are **not** on the 422-kill critical path.

## 7. Parallel Execution Note
Because `Services/Compose/` is the most-contested repo surface and nearly every task edits shared Compose
files, **`parallel-safe:false` is the default** — tasks run largely sequentially within a phase, and every
BFF PR runs `/conflict-check` first. Cross-phase independence (Phase 3 vs 4 vs 5) is the main parallelism
opportunity once their prerequisites land, but each still serializes its own Compose-file edits.

## 8. References
- [`spec.md`](spec.md) · [`design.md`](design.md) · [ADR-049](../../.claude/adr/ADR-049-compose-shadow-document.md)
- [`docs/architecture/COMPOSE-READ-REFERENCE-FIDELITY.md`](../../docs/architecture/COMPOSE-READ-REFERENCE-FIDELITY.md)
- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) · [`projects/INDEX.md`](../INDEX.md)
- [`tests/fixtures/compose-corpus/corpus-manifest.md`](../../tests/fixtures/compose-corpus/corpus-manifest.md)
