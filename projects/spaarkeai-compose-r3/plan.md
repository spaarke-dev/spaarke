# Project Plan: Spaarke Compose R3 — Word-Feature Fidelity

> **Last Updated**: 2026-07-16
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md) · **Design**: [design.md](design.md)

---

## 1. Executive Summary

**Purpose**: Make the Compose Word round-trip faithful — a dirty save applies edits as a delta onto the retained load-time original OOXML instead of rebuilding the whole `.docx` from the editor's simplified view.

**Scope**:
- **E1** — Retained-original delta save (drop `docx.js`; adopt `Docxodus` `WmlComparer`).
- **E2** — `w14:paraId` identity substrate (server pre-parse/mint; TipTap carry; paraId-primary anchoring).
- **E3** — Grounding-tied confidence band + formatted AI insertions (additive `ComposeDraftPayload`).
- **Editing toolset** — find/replace, basic tables, sticky toolbar, one-line bubble menu, dismissible warning, styles pane, richer comment threads.
- **Import round-trip** — read existing Word `w:ins`/`w:del`/`w:comment` into the editor, preserved across save.

**Timeline**: ~5–7 weeks (fidelity core sequenced first; toolset + import parallel) | **Estimated Effort**: ~28 tasks

**All six pre-spec spikes (S1/S1b/S2/S3/S4/S5) passed** — no design pivots; no Phase-0 spike phase. The only pre-build residual is the NFR-09 real-template hardening re-run, which gates the E1 cutover (Phase 6).

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):
- **ADR-039** — no new AI dispatch endpoint; no new/changed catalog rows (engine frozen). E3 is server-derived.
- **ADR-040** — AI edit payloads remain ledger-first.
- **ADR-013 / NFR-05** — `Services/Compose/` injects no AI internals (`IOpenAiClient`/executor/routing); Tier-1 NetArchTest stays green.
- **ADR-007** — no `Microsoft.Graph` types above `SpeFileStore` (incl. the new version-content fetch).
- **ADR-005 / ADR-009 / ADR-015** — SPE storage; Redis-first for any baseline cache; Tier-3 isolation for document bytes.
- **ADR-021 / ADR-028** — Fluent v9 + dark mode; `@spaarke/auth` for fetches.
- **ADR-038** — integration-heavy testing; through-the-wire slice tests; banned mock classes.
- **ADR-029** — publish-size discipline (≤ 60 MB compressed).
- **ADR-032** — Null-Object kill-switch only if any R3 surface is feature-gated.

**From Spec**:
- MUST derive dirty-save output from the retained load-time original (FR-01); MUST NOT reconstruct the whole doc from TipTap.
- MUST exclude SkiaSharp assets when adding Docxodus; MUST NOT invoke Docxodus `HtmlToWml`/`FormattingAssembler`.
- MUST anchor primarily by `paraId`; MUST retain fuzzy re-anchor as cross-Word-session fallback.
- MUST derive E3 confidence server-side from grounding; MUST NOT auto-accept low-confidence edits.
- MUST NOT add any TipTap product feature or any AGPL code.

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| E1 = hybrid (retained-original + Docxodus redline + existing writer, D1) | Untouched paragraphs preserved; max reuse of R2 pipeline | Tracks A/B; FR-01..07 |
| Baseline = load-time SPE version by `versionId` (S4) | Authoritative, refresh-safe, zero new storage | FR-06; new `SpeFileStore` version fetch |
| paraId carried via MIT `@tiptap/extension-unique-id`, no custom plugin (S2) | Built-in split dedup; server owns load-time ids | FR-08..12 |
| E3 confidence = grounding-tied qualitative band, rationale-first (D3) | 2026 HCI research: numeric scores drive over-reliance | FR-13/14 |
| MVP = Approach A (WmlComparer re-serialized output) | Cosmetic-lossless; Approach B splice-back deferred | FR-07/NFR-07 |

### Discovered Resources

**Applicable ADRs**: ADR-039, ADR-040, ADR-013, ADR-007, ADR-005/009/015, ADR-021, ADR-028, ADR-038, ADR-029, ADR-032.

**Applicable Skills**:
- `.claude/skills/task-execute` — execution protocol for every task.
- `.claude/skills/code-review` + `.claude/skills/adr-check` — Step 9.5 gates (FULL / TEST-MODIFYING rigor).
- `.claude/skills/bff-deploy` — BFF publish + deploy.
- `.claude/skills/code-page-deploy` — SpaarkeAi / shared-lib client build/deploy.
- `.claude/skills/fluent-v9-component` — toolset UI (styles pane, comment threads, toolbar).
- `.claude/skills/ui-test` — flagship gate browser verification.

**Knowledge / Constraints**:
- `.claude/constraints/bff-extensions.md` — binding pre-merge checklist (every BFF task).
- `.claude/constraints/azure-deployment.md` — publish-size per-task rule (NFR-01).
- `docs/adr/ADR-038-testing-strategy.md` — E2E DoD; `tests/integration/seam/**` category.
- `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md`, `docs/standards/MODAL-DECISION-CRITERIA.md`.

**Reusable Code (design §10 Component Reuse Map)**:
- `Services/Compose/DocxAnnotationWriter.cs` / `DocxAnnotationReader.cs` — reused verbatim (redlines/comments; import reader).
- `Services/Compose/AnnotationReanchorService.cs` — fuzzy re-anchor retained as cross-Word-session fallback.
- `Services/Compose/ComposeService.cs` — `SaveAsync` (`:315`), `LoadAsync` (`:168`), `PushAnnotationsAsync` (`:1107`) delta reference.
- `SpeFileStore` / `ISpeFileOperations` — `DownloadFileAsUserAsync`, `ReplaceFileContentAsUserAsync` (If-Match).
- Client: `Spaarke.Compose.Components/src/widgets/ComposeWorkspace.tsx`, `ComposeEditor.tsx`, `utils/docxBridge.ts`, `widgets/hooks/usePendingRedline.ts`, `marks/InsertionMark.ts`/`DeletionMark.ts`, `types/compose-contracts.ts`.

---

## 3. Implementation Approach

### Phase Structure

```
Phase 0: Packaging + baseline foundations (front-loaded, small)
└─ Docxodus + OpenXml 3.5.1 (SkiaSharp excluded); publish-size/CVE baseline
└─ Confirm SpeFileStore version-content fetch capability (FR-06 de-risk)

Phase 1: E2 — w14:paraId identity substrate (Track B)   ◄── the substrate E1 needs
└─ Server pre-parse/mint; explicit load-time carry; UniqueID split-minting; paraId-primary anchoring

Phase 2: E1 — retained-original delta save (Track A)     ◄── the keystone; depends on Phase 1
└─ Baseline retrieval; edited-paragraph rebuild+splice; Docxodus WmlComparer redline; drop docx.js; run-level fidelity; slice test

Phase 3: E3 — grounding-tied confidence + formatted insertions (Track C)   ◄── parallel after E2 contract shape
└─ Server-derived confidence band; rationale-first UI; formatted new_text; paraId+offsets anchor

Phase 4: Editing toolset (Track D)                        ◄── largely parallel/independent
└─ find/replace; basic tables; sticky toolbar; one-line bubble menu; dismissible warning; styles pane; comment-thread UI

Phase 5: Import round-trip (Track E)                      ◄── depends on E1/E2
└─ Import existing revisions; import existing comments (feeds FR-23); imported anchors survive save

Phase 6: Hardening + wrap-up
└─ NFR-09 real-template hardening gate (GATES E1 cutover); publish-size/CVE/NetArch; flagship gate G-R3; deploy; wrap-up
```

### Critical Path

**Blocking dependencies:**
- Phase 1 (E2) **BLOCKS** Phase 2 (E1) — paraId is the splice key (design §4.2/§5).
- Phase 2 (E1) **BLOCKS** Phase 5 (import) — imported marks anchor by paraId and must survive the retained-original save.
- Phase 0 (SpeFileStore version fetch) **BLOCKS** FR-06 baseline retrieval in Phase 2.
- E3 offsets (FR-16) **BLOCK ON** E2 (offsets meaningful only against stable paraId).
- **NFR-09 hardening gate (Phase 6) GATES the E1 delta-save cutover** — re-run S1/S1b on real firm templates before flipping the default save path.

**Parallelizable:**
- Phase 4 (toolset) is largely independent of the fidelity core — most tasks can run alongside Phases 1–3 (exception: FR-18 tables depend on paraId FR-08/10).
- Phase 3 (E3) can parallelize with Phase 4 once the E2 anchor shape lands.

**High-risk items:**
- Docxodus on real firm templates (nested tables, deep numbering, cross-refs) — Mitigation: NFR-09 gate.
- SkiaSharp exclusion holding at publish time — Mitigation: measure per BFF task; never touch `HtmlToWml`/`FormattingAssembler`.
- Hot-path overlap with `spaarkeai-compose-r2` + `spaarke-ai-architecture-redesign-r2` — Mitigation: consume `PublicContracts` seams, no fork; `/conflict-check` before each BFF PR.

---

## 4. Phase Breakdown

### Phase 0: Packaging + Baseline Foundations

**Objectives:**
1. Add `Docxodus` 7.1.0 (SkiaSharp assets excluded) + bump `DocumentFormat.OpenXml` 3.4.1 → 3.5.1; establish publish-size + CVE baseline.
2. Confirm `SpeFileStore` can fetch a specific driveItem version's content (FR-06 de-risk) — validate the Graph route.

**Deliverables:**
- [ ] Docxodus + OpenXml 3.5.1 referenced; SkiaSharp excluded; publish-size measured vs ~49.63 MB baseline; no new HIGH CVE.
- [ ] `SpeFileStore` version-content fetch validated (or Redis-cache fallback decision recorded).

**Outputs**: `Sprk.Bff.Api.csproj`, a `SpeFileStore` version-content method (or spike note), Phase-0 measurement note.

### Phase 1: E2 — `w14:paraId` Identity Substrate (Track B)

**Objectives:**
1. Server pre-parse collects/mints OOXML-valid paraIds on Load (FR-08).
2. Explicit load-time carry as hidden ProseMirror node attr (FR-09).
3. Split/merge minting via `@tiptap/extension-unique-id` (FR-10).
4. paraId-primary anchoring + fuzzy fallback (FR-11); paraId as splice key (FR-12).

**Deliverables:**
- [ ] Every body paragraph (incl. table cells) has a unique paraId after Load; ids set explicitly, not rendered to DOM.
- [ ] Split yields two distinct paraIds (one equal to original); untouched ids survive edits elsewhere.
- [ ] Anchors resolve by paraId within our round-trip; fuzzy match after external Word edit.

**Outputs**: server pre-parse component in `Services/Compose/`, `LoadAsync` extension, client `@tiptap/extension-unique-id` config + `docxBridge.ts` paraId carry, `AnnotationReanchorService` paraId-primary path, `compose-contracts.ts` additive `paraId`.

### Phase 2: E1 — Retained-Original Delta Save (Track A) — THE keystone

**Objectives:**
1. Baseline = load-time SPE version by `versionId`; client fast-path + Redis fallback (FR-06).
2. Rebuild only edited paragraphs' OOXML, splice into a copy of the original by paraId (FR-02).
3. Docxodus `WmlComparer` synthesizes minimal `w:ins`/`w:del` incl. format-change (FR-03, FR-05).
4. Invert the save baseline; drop `docx.js` from export (FR-01). AI redlines reuse existing writer (FR-04).
5. Through-the-wire fidelity slice test — untouched OOXML preserved (FR-07 / NFR-06).

**Deliverables:**
- [ ] A save after editing a formatted doc no longer routes through `tipTapJsonToDocxBytes`; untouched paragraphs preserved structurally/semantically.
- [ ] Given N-paragraph doc with K edited, exactly K paragraphs differ pre-comparer; 3 edited → minimal ins/del.
- [ ] Bolding a word yields `rPr`/`pPrChange`, not full-run del+ins.
- [ ] `WebApplicationFactory` slice test asserts untouched OOXML preserved on a dirty save.

**Outputs**: NEW paraId-splice orchestration + Docxodus adapter in `Services/Compose/`, `ComposeService.SaveAsync` inversion, `docxBridge.ts` (docx.js export removed), seam slice test under `tests/integration/seam/**`.

### Phase 3: E3 — Grounding-Tied Confidence + Formatted Insertions (Track C)

**Objectives:**
1. Server-derived coarse `confidence_band` on `ComposeDraftPayload` (additive; no catalog change) (FR-13).
2. Rationale-first, anti-rubber-stamp accept/reject surface (FR-14).
3. Formatted AI insertions — enrich `new_text` to carry marks (FR-15).
4. Explicit `paraId` + offsets on the anchor (rides E2) (FR-16).

**Deliverables:**
- [ ] Grounded suggestion renders `high`; ungrounded renders `low`; no catalog row modified.
- [ ] "Accept all" excludes low-band edits without explicit confirmation.
- [ ] An AI suggestion including bold renders bold in the inserted redline.

**Outputs**: additive `ComposeDraftPayload` (client mirror + `ComposeDraftPayload.cs`), server confidence derivation, `ComposeEditor.tsx` accept/reject UI, `buildInsertionHtml` marks.

### Phase 4: Editing Toolset (Track D)

**Objectives:** find/replace (FR-17), basic tables (FR-18), sticky toolbar (FR-19), one-line bubble menu (FR-20), dismissible simplification warning (FR-21), styles pane — apply existing only (FR-22), richer comment-thread UI (FR-23).

**Deliverables:**
- [ ] find/replace interoperates with tracked-changes marks without corrupting them.
- [ ] table edit round-trips fidelity (S1b); cell paraIds preserved.
- [ ] styles pane applies existing `pStyle`; **no style-authoring UI**.
- [ ] comment thread shows author+timestamp, supports reply, persists as `w:comment`.

**Outputs**: `@tiptap/extension-table` config, find/replace component, styles pane, comment-thread UI, toolbar/bubble-menu/banner fixes in `Spaarke.Compose.Components/src/`.

### Phase 5: Import Round-Trip (Track E)

**Objectives:** import existing revisions (FR-24), import existing comments feeding FR-23 (FR-25), imported anchors survive save (FR-26).

**Deliverables:**
- [ ] A doc redlined in Word opens with revisions visible + accept/reject-able (not flattened).
- [ ] A Word-commented doc opens with comment threads intact.
- [ ] open→save→reopen preserves imported revisions/comments (anchored by paraId).

**Outputs**: `LoadAsync` projection of `DocxAnnotationReader` `RecoveredRevision`/`RecoveredComment`, in-editor render, save-preservation wiring.

### Phase 6: Hardening + Wrap-Up

**Objectives:** NFR-09 real-template hardening gate (re-run S1/S1b on 2–3 real firm templates — **gates the E1 cutover**); publish-size ≤60 MB + CVE + NetArchTest facade verification (NFR-01/02/05); flagship gate G-R3 (browser-verified on spaarkedev1); deploy; project wrap-up.

**Deliverables:**
- [ ] Real-template hardening report passes before delta-save cutover.
- [ ] Publish ≤ 60 MB; no new HIGH CVE; ADR-013 NetArchTest green.
- [ ] G-R3 flagship round-trip + toolset demo verified in browser.
- [ ] `/repo-cleanup`, `/test-diet`, lessons-learned, README status → Complete.

**Outputs**: hardening report, verification note, deploy artifacts, `notes/lessons-learned.md`, updated README/TASK-INDEX.

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| `Docxodus` 7.1.0 (NuGet, MIT) | GA | Low | SkiaSharp excluded (S3); Codeuctivity fork = fallback |
| `@tiptap/extension-unique-id` 3.28.0 (npm, MIT) | GA | Low | Verify resolves to `@tiptap/extension-*`, not `@tiptap-pro/*` |
| SPE driveItem version-content fetch (Graph) | GA | Low | Validate route Phase 0; Redis-cache fallback |
| `.NET 10` SDK | Ready | Low | Verified S1 |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| Compose service/endpoints, native-OOXML writer/reader | `src/server/api/Sprk.Bff.Api/Services/Compose/` | Merged (R1+R2) |
| Slice-safe redline marks | `Spaarke.Compose.Components/src/marks/` | Merged (R2) |
| `spaarkeai-compose-r2` (predecessor satellite) | `work/spaarkeai-compose-r2` | Confirm merged/frozen before E1 cutover |
| `spaarke-ai-architecture-redesign-r2` (owns `Services/Ai/` internals) | `work/spaarke-ai-architecture-redesign-r2` | Consume `PublicContracts` seams; no fork; `/conflict-check` |

---

## 6. Testing Strategy

**Integration-heavy (ADR-038):**
- **Through-the-wire seam slice tests** (`tests/integration/seam/**`) — the E2E DoD for every save/load/dispatch change: edit-formatted-doc → assert untouched OOXML preserved + edits applied. Unit-green ≠ done. (NFR-06)
- Mock only at the `SpeFileStore` facade boundary; **no** `Mock<HttpMessageHandler>`, no DI-registration/ctor-null tests.

**Unit:**
- Every new/changed `Services/Compose/` service ships matching tests in `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/`.
- paraId minting/collision, WmlComparer adapter behavior, confidence derivation.

**Real-template hardening (NFR-09):** re-run the S1/S1b harness on 2–3 real firm templates before the E1 cutover.

**Flagship E2E (G-R3):** browser-verified full round-trip + toolset demo on spaarkedev1.

---

## 7. Acceptance Criteria

**Fidelity core (Phases 1–2):**
- [ ] Dirty save derives from retained original; untouched paragraphs preserved (text + paraId + styles + numbering + headers/footers + footnotes + nested tables).
- [ ] Slice test proves untouched OOXML preserved on a dirty save (FR-01/07).

**E3 + toolset + import (Phases 3–5):**
- [ ] Grounding-tied confidence band renders; low-band never auto-accepted.
- [ ] find/replace + table edit work without corrupting tracked-changes marks.
- [ ] Existing Word revisions/comments import and survive save.

**Non-functional (Phase 6):**
- [ ] Publish ≤ 60 MB; no new HIGH CVE; ADR-013 NetArchTest green.
- [ ] NFR-09 hardening passed before cutover.
- [ ] G-R3 flagship verified in browser.

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | WmlComparer fails on real firm templates | Med | High | NFR-09 hardening gate before cutover |
| R2 | Docxodus pulls SkiaSharp into publish | Low | Med | Exclude assets; never call HtmlToWml/FormattingAssembler; measure per task |
| R3 | SPE version-content fetch unavailable | Low | Med | Validate Phase 0; Redis-cache fallback |
| R4 | BFF hot-path collision with compose-r2 / ai-redesign-r2 | Med | Med | Consume PublicContracts seams, no fork; `/conflict-check` per PR |
| R5 | E3 confidence band read as false precision | Low | Med | Rationale-first; coarse band only; no auto-accept low-band |

---

## 9. Next Steps

1. **Review this plan.md** and TASK-INDEX with the owner.
2. **Confirm** `spaarkeai-compose-r2` is merged/frozen before starting the E1 cutover (Phase 2).
3. **Begin** Phase 0 (packaging + baseline foundations) via `task-execute`.

---

**Status**: Ready for Tasks
**Next Action**: Execute task 001 (Phase 0) — but only after owner confirms compose-r2 coordination.

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks. Every BFF-touching task runs the `.claude/constraints/bff-extensions.md` checklist + reports publish-size delta.*
