# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-07-17 (task-execute — task 020 COMPLETE, all gates PASS, awaiting commit)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | 020 — ✅ COMPLETE (FR-02 edited-paragraph rebuild + paraId-keyed splice). Uncommitted. |
| **Step** | 4 of 4 DONE + Step 9.5 gates PASS (0 Critical, 0 blocking ADR). |
| **Status** | completed — awaiting commit + operator go for next task |
| **Next Action** | Commit task 020 (3 code/DI/test files + 3 tracking). Next startable: **021** (FR-02 Docxodus WmlComparer adapter — consumes 020's spliced-edited doc; adds Docxodus NuGet → publish-size watch), **030** (E3 band, deps 012✅). Task **022** (SaveAsync inversion) needs 021. Run `/conflict-check` before any BFF PR. Resume: `work on task 021` / `continue`. |

### Files Modified This Session (task 020)
- `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeParagraphSpliceService.cs` — NEW pure splice service + `ComposeEditedParagraph` DTO + `ComposeSpliceException`.
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/ComposeModule.cs` — register `ComposeParagraphSpliceService` (unconditional singleton).
- `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/ComposeParagraphSpliceServiceTests.cs` — NEW 10 tests.

### Files Modified — task 012 (committed 5aed0f2d5, pushed)
- `src/server/api/Sprk.Bff.Api/Services/Compose/AnnotationReanchorService.cs` — FR-11: `Reanchor` optional `currentParaIds` param, paraId-first (Auto/1.0) → retained fuzzy fallback; `ExtractParaIds` + `ReadParagraphs` (open-once) + `ResolveByParaId`; `ComputeAndPersistAsync` passes ids.
- `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeParaIdSpliceMap.cs` — NEW FR-12 splice-key resolver (`BuildParagraphIndex` paraId→w:p, `Resolve` matched/unmatched). Placed as pure helper (not ComposeService.cs — SRP + 012/020 boundary; directional deviation from POML output location, noted).
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeReanchor.types.ts` — `PriorAnchorInput.paraId?` (mirror).
- `src/client/shared/Spaarke.Compose.Components/src/widgets/useComposeWordShuttle.ts` — `anchoredAnnotationsToPriorAnchors` passes `paraId`.
- (Step 1, committed c9f80b61d) compose-contracts.ts + ChatSession.cs + AnnotationReanchorService.PriorAnchor `paraId`.

### Critical Context
All six pre-spec spikes (S1/S1b/S2/S3/S4/S5) passed — no design pivots. The fidelity core sequences E2 (paraId substrate) → E1 (delta save); toolset + E3 parallelize; import depends on E1/E2. The NFR-09 real-template hardening gate (Phase 6) gates the E1 delta-save cutover.

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | none |
| **Task File** | — |
| **Title** | — |
| **Phase** | — |
| **Status** | none |
| **Started** | — |

---

## Progress

### Completed Steps
*No steps completed yet*

### Files Modified (All Task)
*No files modified yet*

### Decisions Made
- 2026-07-16: Seed README moved to `notes/seed-README.md`; canonical README generated (operator chose "regenerate canonical"). — Reason: preserve lineage while giving a standard project overview.
- 2026-07-16: Pipeline stopped at "ready to execute" (operator chose "generate + stop"); task 001 NOT auto-started. — Reason: FULL-rigor BFF blast radius + hot-path overlap with compose-r2 warrants owner coordination first.
- 2026-07-16: Owner confirmed `spaarkeai-compose-r2` completed/closed + all work on master. — E1-cutover coordination gate (task 022 pre-condition) CLEARED. Residual gate before any BFF PR: run `/conflict-check` for `Services/Compose/` hot-path.
- 2026-07-16: **Task 001 COMPLETE.** §6.5 Path-C: adopted Docxodus **6.4.0** (net8.0 line) instead of spec-named 7.1.0 — 7.x is net10.0-only (NU1202), 6.4.0 is same MIT fork + engine + pulls OpenXml 3.5.1. SkiaSharp×2 (managed + Linux native pkg) excluded runtime;native → 0 SkiaSharp in publish, no runtimes/. Publish 47.26 MB incl PDBs (+0.60 MB vs fresh 46.66 MB baseline). No new HIGH CVE (only pre-existing Kiota, accepted per ADR-029). **DOC-RECONCILE**: design §12.3 + tasks 010/020/021/022 say "7.1.0" → 6.4.0. **OPEN**: confirm 6.4.0/net8 acceptable (or plan net10 migration → 7.1.0).
- 2026-07-17: **Task 020 COMPLETE** (E1 splice leg — FR-02 paraId-keyed edited-paragraph rebuild+splice). NEW pure `ComposeParagraphSpliceService.SpliceEditedParagraphs(retainedOriginal, ComposeEditedParagraph[])`: opens editable copy, resolves ALL edited paraIds via task-012 `ComposeParaIdSpliceMap` BEFORE mutation (fail-fast — unmatched/duplicate → `ComposeSpliceException`, no partial write), rebuilds each matched paragraph preserving paraId+pPr+rPr, untouched pass through. Produces spliced-edited doc for task 021's WmlComparer. Does NOT run comparer (021) or invert SaveAsync (022). DI: unconditional singleton in ComposeModule. §11: verified no overlap with text-level ComposeEditBatch/EditValidator. Tests: NEW 10 (exactly-K-differ, paraId-exact, table-cell, NFR-07 preservation, fail-fast). Compose suite 417✅. Publish 45.94 MB (0 pkg delta); 0 CVE; ADR013_ComposeFacade PASS. §9.5 gates PASS. PR MUST include §10 Placement Justification. **NFR-06 seam obligation → task 022** (SaveAsync inversion).
- 2026-07-17: **Task 012 COMPLETE** (E2 anchoring half — FR-11 paraId-primary + FR-12 splice key). `AnnotationReanchorService.Reanchor` +optional `currentParaIds` → exact paraId hit = AUTO/1.0 (definitive, drift-robust), else RETAINED fuzzy scorer (design §5.2 external-Word-edit fallback); `ExtractParaIds`+`ReadParagraphs` single-walk refactor. NEW pure `ComposeParaIdSpliceMap` (BuildParagraphIndex paraId→w:p incl table/nested cells; Resolve matched/unmatched) = splice KEY task 020 consumes. **DIRECTIONAL DEVIATION**: splice-key in dedicated helper NOT ComposeService.cs (SRP + 012/020 boundary; avoids speculative dead method) — noted in POML `<notes>`. Client mirror: `PriorAnchorInput.paraId?` + mapper. Tests: +6 reanchor paraId-primary, NEW ComposeParaIdSpliceMapTests(7), NEW seam ComposeParaIdReanchorSeamTests (NFR-06). Unit 27✅, Compose 407✅, client 10✅. Publish 45.94 MB compressed incl PDBs (−0.72 vs 46.66; 0 pkg delta); 0 CVE; ADR013_ComposeFacade PASS; ADR007 pre-existing RED = Services.Communication only (not us). §9.5 gates PASS. PR MUST cite ADR Tension Path A. **DOC-RECONCILE**: design §5 unique-id "3.28.0"→2.27.2; §12.3 Docxodus "7.1.0"→6.4.0.
- 2026-07-17: **Task 011 COMPLETE** (E2 client paraId carry). NEW `src/widgets/paraIdExtension.ts` (`generateOoxmlParaId` CSPRNG 8-hex `0<x<0x80000000` + `COMPOSE_R3_PARAID` = `@tiptap/extension-unique-id` `.extend`(renderHTML→{} = OFF DOM, FR-09)`.configure`(types paragraph+heading, attributeName paraId, generateID)); `stampParaIds` in docxBridge (explicit tr after setContent, doc-order, addToHistory:false); `ParaIdMapEntry` mirror in compose-contracts; `paraIdMap` prop + stamp-call wired in ComposeEditor. **§6.5 Path-C**: unique-id **2.27.2** (v2-latest MIT, NOT 3.28.0/v3, NOT @tiptap-pro ✅); whole `@tiptap/*` stack bumped 2.10.3→2.27.2 uniformly — **accepted** (TipTap's supported lockstep model), validated by **201/201 suite green** + build green. **Directional refinement (code-review-found correctness fix)**: extension `types` + stamp cover `paragraph` AND `heading` — server `body.Descendants<Paragraph>()` counts headings (OOXML `<w:p>`), so paragraph-only stamping misaligned every id after the first heading. **Module extraction**: paraIdExtension split out (from literal "config in ComposeEditor.tsx") so the headless FR-09/FR-10 tests don't drag the auth/toolbar graph (which needs a SharedLibs dist build). 12 new paraId tests; 2 existing docxBridge mocks got no-op `stampParaIds`. adr-check + code-review PASS (0 crit). **META (spike-hygiene, already noted)**: this is the 2nd platform pivot (Docxodus net10→net8, now TipTap v3→v2) tracing to spikes run on newer majors than the codebase. **DOC-RECONCILE**: design §5 / task-012 prose say unique-id "3.28.0" → 2.27.2.
- 2026-07-17: **Task 010 COMPLETE** (E2 substrate). NEW `ParaIdPreParser` (OpenXML `body.Descendants<Paragraph>()` covers table-cell/nested recursively; collect verbatim + mint `0<x<0x80000000` collision-checked) → additive `ParaIdMap` on LoadComposeDocumentResult, projected best-effort in LoadAsync; DI singleton. 8 tests green; 47.28 MB (+0.02); CVE clean; ADR013 ComposeFacade PASS. **§6.5 Path-C**: HTTP through-the-wire seam rides task 024 (map assertion covered at ComposeService seam now). **PRE-EXISTING FINDING (not compose-r3)**: `ADR007_GraphIsolationTests` RED on branch — violators are Services.Communication (GraphAttachmentAdapter/GraphMessageToEmlConverter) + Api/Office/Errors, all pre-existing on master. Out of scope; surfaced to owner.
- 2026-07-17: **Merged origin/master into branch** (merge aaf45f7cd; process-gap fix — pipeline should have synced at init). Brought 26 assistant-r1 commits + **Kiota 1.21.2→1.22.0** (CVE-2026-44503 fix). ZERO conflicts; my 001/002 changes preserved; merged base builds green; **CVE scan now fully clean (0 vulnerable)** — task-001's pre-existing Kiota HIGH is gone. 001+002 committed as 857d06099.
- 2026-07-16: **Task 002 COMPLETE.** Added `DownloadFileVersionAsUserAsync` to SPE facade (ISpeFileOperations + SpeFileStore + DriveItemOperations) via Graph v5 `/versions/{id}/content` OBO; 404→null; ADR-007 clean; build green. **§6.5 Path-C: unit test DEFERRED to 022/024 seam** — Graph v5 unmockable at DriveItemOperations level (all 5 existing SpeFileStoreTests are `[Fact(Skip)]`); ADR-038 bans facade-mock scaffolding + mandates seam tests; POML says seam rides 022/024. **SYSTEMIC OPEN**: confirm defer-to-seam doctrine for all R3 BFF-IO tasks (010/020/021/022/023), or require unit tests. **TRACKING**: task 024 seam MUST assert baseline-by-versionId retrieval.

---

## Next Action

**Next Step**: Execute task 001 (Phase 0 — Docxodus packaging + publish-size/CVE baseline).

**Pre-conditions**:
- ✅ Owner confirmed `spaarkeai-compose-r2` completed/closed + on master (2026-07-16) — `Services/Compose/` collision risk on the E1 cutover cleared.
- `/conflict-check` run for BFF hot-path (still recommended before opening any BFF PR).

**Key Context**:
- Load [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) before any BFF task; report publish-size delta vs ~49.63 MB baseline.
- Docxodus MUST exclude SkiaSharp assets; never call `HtmlToWml`/`FormattingAssembler`.

**Expected Output**: Docxodus + OpenXml 3.5.1 referenced (SkiaSharp excluded), publish-size + CVE baseline recorded.

---

## Blockers

**Status**: None. Prior soft gate (compose-r2 coordination) CLEARED 2026-07-16 — R2 completed/closed + on master.

---

## Session Notes

### Current Session
- Started: 2026-07-16
- Focus: Pipeline initialization (artifacts + task decomposition) — complete; awaiting execution kickoff.

### Key Learnings
- Engine frozen (ADR-039): E3 is server-derived, NOT a new Action output — no catalog rows change.

### Handoff Notes — Task 012 (paraId-primary anchoring + splice key) — IN PROGRESS 2026-07-17

**Rigor**: FULL · opus @ high · directional · BFF+client (hot-path, parallel-safe=false). Deps 010✅,011✅.
**Model gate**: needs opus — session must be Opus/Fable (was Opus 4.8). **Hot-path /conflict-check: ✅ CLEAR** — only open PR touching `Services/Compose` is our own #656; compose-r2 done/on-master; ai-redesign-r2 owns `Services/Ai` (no overlap).

**✅ Step 1 DONE (additive mirror-first `paraId`, BFF builds 0 err)**:
- Client `AnchoredAnnotationAnchor` (`compose-contracts.ts:108-124`) — added `paraId?: string`.
- Server `AnchoredAnnotationAnchor` (`Models/Ai/Chat/ChatSession.cs:342`) — added `public string? ParaId { get; init; }`.
- `PriorAnchor` (`AnnotationReanchorService.cs:451`) — added trailing `[JsonPropertyName("paraId")] string? ParaId = null`.
- All OPTIONAL/nullable (ADR Tension Path A additive; existing consumers unaffected).

**Seams located for Steps 2-6**:
- **Step 2 (FR-11 paraId-primary)** — `AnnotationReanchorService.cs`: scorer at line ~111 (`FindBestParagraphMatch(anchor.TextPattern, currentParagraphs)` + `StructuralProximity(bestIdx, anchor.ParagraphHint)`); bands `ReanchorBand` (Auto/Review/Orphan, Orphan threshold <0.6 @ line ~441). **Plan**: in the resolve loop, try `anchor.ParaId` exact-match against the current doc's paraId map FIRST → if found, band=Auto (confidence 1.0, matched paragraph = that paraId's index); ONLY when ParaId is null/not-found, delegate to the existing textPattern+Levenshtein+ParagraphHint scorer (RETAIN it — Word regen caveat, Open-XML-SDK #925). Need the current-doc paraId list as input — reuse task-010 `ParaIdPreParser` (inject or call) to get `{index → paraId}` for `currentParagraphs`. Read full ResolveAsync signature + how `currentParagraphs` is built (lines ~80-140).
- **Step 3 (FR-12 splice key)** — `ComposeService.cs` (1747 LoC): find the SAVE path (`SaveComposeAnnotationsAsync` referenced at ChatSession.cs:304; also the dirty-save/`SaveAsync`). Establish the `paraId → original-OOXML-paragraph` map that task 020's FR-02 splice consumes. Grep `SaveAsync`, `paraId`, `splice`. Likely additive: expose the map (already produced by ParaIdPreParser on Load — task 010 `LoadComposeDocumentResult.ParaIdMap`) so save maps edited editor paragraphs (carrying paraId from client) → original OOXML paragraph by paraId. Task 020 does the actual Docxodus patch; 012 just makes the KEY available/wired.
- **Step 4 unit tests** — `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/`: (a) anchor resolves by paraId within round-trip; (b) SAME anchor re-resolves via fuzzy matcher after external edit that removed/changed paraId (null paraId → fuzzy path); (c) edit to para P maps to original OOXML para with matching paraId; (d) additive-contract (existing consumers unaffected). Mirror `ParaIdPreParserTests`/`CrossVersionSessionPersistenceTests` harness. Real docx via `WordprocessingDocument.Create`; NO transport mocks (ADR-038).
- **Step 5 seam slice** — `tests/integration/seam/**` `WebApplicationFactory`: load → anchor → save → reload, anchor round-trips to correct paragraph by paraId (NFR-06). This ALSO discharges the task-010 §6.5 deferral (010's HTTP through-the-wire map assertion) + task-002 versionId seam if co-located. Check existing `tests/integration/seam/` structure first.
- **Step 6** — `dotnet build` BFF + shared-lib build; run tests; **publish-size** `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` report absolute compressed + delta vs baseline (worktree fresh baseline **46.66 MB** incl PDBs per task 001; NOT the CLAUDE.md ~49.63 — rebased past ai-redesign-r1 deletions); ≤60 MB ceiling; `dotnet list package --vulnerable --include-transitive` no new HIGH; NetArchTest `ADR013_ComposeFacade` + `ADR007_GraphIsolation` (NOTE: ADR007 is PRE-EXISTING RED on branch from Services.Communication — not us; confirm ComposeFacade green).

**Constraints reminder**: ADR-007 (no Graph types above SpeFileStore in Services/Compose), ADR-013 (no AI internals in Services/Compose — Tier-1 NetArchTest), NFR-06 seam test mandatory, mirror-first (client↔server in sync — DONE Step 1). PR desc MUST cite ADR Tension Path A (AnchoredAnnotation gains paraId, stays Compose-domain, never `memory.*`).

**Resume**: `work on task 012` (or `continue`). Step 1 committed? NO — uncommitted WIP (client + 2 server files). Batch-commit 012 when green, or commit Step-1 contracts separately if desired.

---
### Handoff Notes — Task 011 (client paraId carry) analysis + plan (2026-07-17)

**Rigor**: FULL · sonnet @ high · directional · client-only (no BFF/publish). Deps 010 ✅.

**🔔 §6.5 Path-C — TipTap version pivot (VERIFIED, non-blocking, same as Docxodus)**: POML/S2 name
`@tiptap/extension-unique-id` **3.28.0**, but that's a **v3** extension (peerDeps `@tiptap/core: 3.28.0`)
and the ComposeEditor is **TipTap v2.10.3** (package.json: core/react/starter-kit/all extensions ^2.10.3).
Use **`@tiptap/extension-unique-id@^2.27.2`** — the MIT **v2-latest** line (peerDeps `@tiptap/core: ^2.7.0`
✓ compatible; name is `@tiptap/extension-unique-id` NOT `@tiptap-pro/*` ✓ NFR-03; deps: uuid only; same
`attributeName`/`generateID`/`types` config surface). Avoids a TipTap v2→v3 editor migration (the "big
decision", like net10). **package.json already edited to ^2.27.2; `npm install --legacy-peer-deps` running.**

**META-FINDING for the procedure/spike team**: BOTH platform pivots this project (Docxodus net10→net8
6.4.0, TipTap v3→v2 2.27.2) trace to **spikes run on newer MAJOR versions than the actual codebase**.
S2 used headless TipTap v3; S1/S3 used Docxodus net10. The spikes validated the recipe but not
consumability against the shipped major. Spike-hygiene gap worth a checklist item ("run the spike against
the target project's actual dependency majors"). Parallel to the earlier POML-template-drift finding.

**Integration points located**:
- `ComposeEditor.tsx`: `LOCKED_EXTENSIONS` array @ line 182 (+ additive arrays `COMPOSE_R2_MARKS`,
  `COMPOSE_R2_QA_HIGHLIGHT`); `useEditor` @ 1027, `extensions: [...LOCKED, ...MARKS, ...QA]` @ 1030.
  DOCX import effect @ 1091-1180: `docxToTipTapHtml(docxBytes).then(({html}) => editor.commands.setContent(html))`
  @ 1140-1143 (docx path) + `initialHtml` seed path @ 1103. `docxBytes: ArrayBuffer|null` prop @ 334.
- `docxBridge.ts`: `docxToTipTapHtml` @ 91 (mammoth convert → {html, messages}); setContent is called by
  ComposeEditor, NOT here. Add a `stampParaIds(editor, map)` helper here (testable) called after setContent.
- `compose-contracts.ts`: has AnchoredAnnotation etc. but **NO Load-response type** — add a `ParaIdMapEntry`
  mirror `{ index:number; paraId:string; isMinted:boolean }` (matches server ParaIdMapEntry JSON) here.

**⚠️ Install side-effect (DECIDE before build)**: `npm install` resolved `@tiptap/extension-unique-id`
**2.27.2** (MIT, no Pro ✅) BUT also bumped the whole `@tiptap/*` v2 stack **2.10.3 → 2.27.2**
(package.json declares `^2.10.3`, so npm took latest-satisfying 2.x). Semver-minor within v2 — safe in
principle, validated by build + existing ComposeEditor tests. OPTION to minimize churn: add
`"overrides": { "@tiptap/core": "2.10.3", ... }` so unique-id (peer `^2.7.0`) runs against the pinned
2.10.3 stack. Owner/reviewer call: accept the 2.27.x bump (simpler) vs pin back (minimal blast radius).
package-lock.json + node_modules currently reflect the 2.27.2 bump (uncommitted).

**Remaining steps**: (1) install DONE — resolved 2.27.2 MIT, no @tiptap-pro (verified); (2) add
`ParaIdMapEntry` to compose-contracts.ts; (3) `stampParaIds` helper in docxBridge (explicit tr over
doc, set each paragraph node's `paraId` attr from map in doc order); (4) ComposeEditor: add `paraIdMap?`
prop + call stampParaIds after both setContent sites; add `UniqueID.configure({ types:['paragraph'],
attributeName:'paraId', generateID: <8-hex <0x80000000> })` to extensions with `renderHTML:()=>({})` so it
stays OFF the DOM (FR-09); (5) Jest tests (ids-after-mount, ids-absent-from-DOM, split re-mints one/keeps
one, resolved dep is @tiptap/extension-unique-id MIT not pro); (6) `npm run build` (tsc). Then 011→012.

**Resume**: `work on task 011` (or `continue`). package.json already has the dep; install running/done.

---
### Handoff Notes — Task 010 (E2 paraId pre-parse) — COMPLETE 2026-07-17 (kept for reference)

**Rigor**: FULL · opus @ xhigh · directional. Deps 001 ✅ (OpenXml 3.5.1 present).

**Seams located**:
- `ComposeService.LoadAsync` = `Services/Compose/ComposeService.cs:168`. Content is buffered to
  `ReadOnlyMemory<byte> content` (line 203-209) BEFORE the return — the pre-parse input is `content`.
  Return DTO `LoadComposeDocumentResult` is built at line 257+ (has Content/ETag/FileName/
  AnchoredAnnotations/DefinedTermsTracking/ActionHistory). **Add an additive `ParaIdMap` field here.**
- Walk pattern to mirror: `Services/Compose/DocxAnnotationReader.cs` — opens via
  `WordprocessingDocument.Open(stream, isEditable:false)`, uses `Body.Descendants()` (document-order,
  flattened across paragraph boundaries incl. table cells / nested tables — EDGE-R4). **Key insight:
  `body.Descendants<Paragraph>()` already covers table-cell + nested-table paragraphs recursively —
  no manual table descent needed.** `Paragraph.ParagraphId` is the `w14:paraId` (HexBinaryValue string).
- DocxAnnotationReader is pure `byte[]`-in / record-out, NOT DI-registered (constructed per call). But
  task 010 POML says register the pre-parser unconditionally in the Compose DI module (ADR-010 §10 F.1
  symmetric registration) — find the Compose DI module (`Infrastructure/DI/ComposeModule.cs` per master
  ls-tree) and add `services.AddSingleton<ParaIdPreParser>()` (stateless, thread-safe).

**Design decisions**:
- Output = ordered map only (NOT mutate/persist the docx) — literal POML scope, avoids scope creep
  (§11). Result shape: ordered list of `{ index, paraId, isMinted }` (document order). Task 020/022
  apply the map when they need ids physically in the OOXML for the splice.
- Mint = random 32-bit `0 < x < 0x80000000`, format as 8-hex `ST_LongHexNumber`; collision-check
  against a seen-set of ALL existing ids (collected in the same single pass — NFR-08); reject+retry.
- Mirror the map DTO to the client contract shape task 011 consumes
  (`Spaarke.Compose.Components/src/types/compose-contracts.ts`) — do NOT invent a parallel schema.

**Remaining steps** (task-execute steps 1-6): (1) design ParaIdMap DTO + additive Load-response field
+ client-contract mirror; (2) implement `Services/Compose/ParaIdPreParser.cs`; (3) wire into LoadAsync
+ DI register; (4) unit tests `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/ParaIdPreParserTests.cs`
(uniqueness, OOXML-range, verbatim-preserve, forced-collision retry, table-cell coverage); (5) NFR-06
seam test in `tests/integration/seam/**` (Load route carries complete paraId map); (6) build + publish-
size/CVE + NetArchTest facade check. Then transition 010→011→012.

**Resume**: `work on task 010` (or `continue`).

---

## Quick Reference

### Project Context
- **Project**: spaarkeai-compose-r3
- **Project CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md)

### Applicable ADRs
- ADR-039 (frozen engine / closed catalogs), ADR-040 (ledger), ADR-013 (AI facade), ADR-007 (Graph isolation), ADR-005/009/015 (SPE/Redis/Tier-3), ADR-021/028 (Fluent v9 / auth), ADR-038 (testing), ADR-029 (publish hygiene), ADR-032 (Null-Object, if gated).

---

## Recovery Instructions

1. **Quick Recovery**: Read the "Quick Recovery" section above.
2. **If more context needed**: Read Active Task and Progress sections.
3. **Load task file**: `tasks/{task-id}-*.poml`.
4. **Load knowledge files**: From task's `<knowledge>` section.
5. **Resume**: From the "Next Action" section.

**Commands**: `/project-continue` · `/context-handoff` · "where was I?"

---

*This file is the primary source of truth for active work state. Keep it updated.*
