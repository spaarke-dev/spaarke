# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-07-18 (tasks 026 + 023 ✅ COMPLETE; next = 027 client cutover)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

## ▶️ RESUME HERE — parallel wave COMPLETE (2026-07-18). Next: task 031 (E3 UI + 030 live-wiring) or 025 (deploy).

**PARALLEL WAVE DONE + COMMITTED (2026-07-18, autonomous). 8 tasks landed, 8 clean local commits, ZERO pushes.**

| Task | State | Commit | Verify |
|---|---|---|---|
| 024 E1 fidelity seam | ✅ | `11696f4ac` | Compose 465✅ (BFF) |
| 030 E3 confidence-band | 🔄 **foundation** (live-wiring → 031) | `11696f4ac` | 14 unit+3 seam, ADR013 2/2 |
| 040 find/replace FR-17 | ✅ | `105f8c521` | client jest |
| 044 comment-thread FR-23 | ✅ | `62cb980f7` | client jest |
| 042 toolbar/bubble/banner FR-19/20/21 | ✅ | `00490f5ca` | client jest |
| 043 styles pane FR-22 | ✅ (built-in styles only; custom-style-name pre-parse = follow-up) | `4564d5102` | client jest |
| 041 basic tables FR-18 | ✅ (toolbar affordance; table family already present) | `1b3690ecf` | client jest |

**AUTHORITATIVE client verify: `npx jest` (parallel, CI-equivalent) = 286/286 ✅, 33/33 suites.** BFF Compose = 465/465 ✅ (024+030).

**⚠️ PRE-EXISTING test-infra issue (NOT from this wave — follow-up):** under `npx jest --runInBand` (single process) ~11 suites/41 tests fail with `PaneEventBusProvider` errors from cross-suite state pollution. CONFIRMED pre-existing: reproduces with ALL of this wave's new test files EXCLUDED (identical 11/41), and every suite passes in isolation + under default parallel workers. Likely a module-level `PaneEventBus` singleton / unmocked global not reset between suites. Does NOT block (CI runs parallel). **Follow-up:** harden per-suite isolation (teardown the shared PaneEventBus / editor instances; `beforeEach` reset).

**RESIDUALS folded forward:**
- **Task 031** now also carries **030's live band-wiring** (server derive in `ChatEndpoints.GetComposeOutputsAsync` + mirror into the real `ComposeDraftPayload` in `ComposeEditor.tsx` + a LIVE seam assertion) — see 030/031 rows in TASK-INDEX.
- **043 follow-up:** server-side OOXML style-name pre-parse (task-010 paraId pattern) to surface true custom named styles (e.g. "Recital") + `rStyle` character styles.
- **044 follow-up:** wire `composeCommentThreadsToDocxAnnotations()` into the live `ComposeWorkspace.triggerSave` annotation flow.

**⚠️ BFF DEPLOY DIRECTIVE (owner 2026-07-18, memory `bff-deploy-sync-worktree-first`):** before ANY `Sprk.Bff.Api` deploy from this worktree (025/081), FIRST sync `origin/master` in → rebuild → re-run publish-size/CVE on the synced tree → then deploy. Do not deploy a stale worktree.

**REMAINING TASK GRAPH:** E1 → 025 (deploy, deps 024✅ — but see deploy directive). E3 → 031 (deps 030🔄 met; carries 030 live-wiring) → 032. Import → 050/051/052 (dep 022✅/044✅). Wrap → 080/081/082/090. Client toolset wave (040/041/042/043/044) DONE.

### ⬇️ (superseded) parallel wave in flight — client lane 044→042→043→041 + E3 030 residual

**AUTONOMOUS PARALLEL ORCHESTRATION (2026-07-18).** Owner asked to run task 024 + task-execute other tasks in parallel, autonomously. Lanes: server-test + server-contract (both drained) + a SERIAL client lane (all W4 toolset tasks mount into the single `ComposeEditor.tsx`, so they cannot run concurrently — one at a time). Sub-agents run `task-execute` FULL rigor but do NOT touch `.claude/`, git, or the trackers — the MAIN SESSION owns commits + `TASK-INDEX.md`/`current-task.md` (avoids concurrent-write conflicts).

**DONE + COMMITTED this wave:**
- **024** (E1 fidelity seam) ✅ — `11696f4ac`. NEW `tests/integration/seam/Ai/ComposeFidelitySeamTests.cs` (2 tests): loaded byte-identity + born-in-editor numbering golden-file. Compose 465✅.
- **030** (E3 confidence-band) 🔄 FOUNDATION committed `11696f4ac`. Derivation engine + additive contract + client-mirror fragment + 14 unit/3 seam tests. **LIVE-WIRING RESIDUAL folded into task 031** (grep-verified §6.5 Path-C: `ComposeService.cs` never touches `ComposeDraftPayload`; live path is `ChatEndpoints.GetComposeOutputsAsync`, payload stored opaque → `DeriveConfidenceBand` not yet invoked live; the real client `ComposeDraftPayload` lives in `ComposeEditor.tsx` not `compose-contracts.ts`).
- **040** (find/replace FR-17) ✅ — `105f8c521`. Mark-safe (nodesBetween union, avoids `marksAcross` drop). jest 230/230.

**IN FLIGHT:** **044** (comment-thread UI) — client lane, running as delegated agent on top of 040's committed `ComposeEditor.tsx`.

**CLIENT LANE QUEUE (serial, launch next as ComposeEditor.tsx frees):** 044 → 042 (toolbar/bubble/warning) → 043 (styles pane) → 041 (basic tables, deps 011✅). Per client task: verify tsc+jest, commit its files + flip TASK-INDEX row, then launch the next.

**TASK 031 now carries 030's live-wiring** (server derive in `GetComposeOutputsAsync` + mirror into the real `ComposeDraftPayload` in `ComposeEditor.tsx` + a LIVE seam assertion). 031 deps 030 (🔄 foundation met).

**⚠️ BFF DEPLOY DIRECTIVE (owner 2026-07-18, memory `bff-deploy-sync-worktree-first`):** before ANY `Sprk.Bff.Api` deploy from this worktree (tasks 025/081), FIRST sync `origin/master` into the worktree so all other projects' merged changes ship. Do not deploy a stale worktree.

**Pushes NOT done** — local commits only; owner batches pushes/deploys.

### ⬇️ (prior) `work on task 024` (E1 seam) — tasks 022/023/026/027 all ✅ DONE

**Task 027 ✅ COMPLETE (2026-07-18) — the client no longer authors `.docx` bytes.** Server half `ff9576278`; client half this session. `docx.js` (`tipTapToDocxBytes`/`tipTapJsonToDocxBytes`/`buildRejectBaselineJson`) + the `docx` npm dependency REMOVED. `docxBridge` now exposes `captureParaIdSnapshot`/`collectEditedParagraphs`(reject-state diff)/`buildContentModel`; `ComposeEditor` handle exposes `collectEditedParagraphs`/`buildContentModel`/`getRedlineAnnotations` + captures the load-time snapshot after `stampParaIds`; `triggerSave` sends the 4-case structured payload (loaded+dirty→`{editedParagraphs, baselineVersionId, content?}` / loaded+clean→`{content}` / born-in-editor→`{contentModel}` / unedited-browse-local→`{content}`; annotations ride every case); `versionId` threaded through workspace state (Load response → state → save). Client **205✅** (NEW `docxBridge.contentModel.test.ts`; fixed `dirtyOnMount`/`referenceOnly` mocks + `redlineDocxAnnotations` reject-state block moved); tsc clean. **Known E1 limitation (inherited from 022, documented):** a brand-new/split paraId is NOT emitted as an EditedParagraph (the synthesizer fails-fast on an unmatched paraId) — structural insert/split/delete is a future synthesizer extension.

**Next = task 024 (E1 through-the-wire seam — NFR-06).** WebApplicationFactory slices: (a) loaded-doc dirty save preserves untouched OOXML BYTE-IDENTICAL + edited paras carry `w:ins`/`w:del`; (b) born-in-editor render round-trips + numbering GOLDEN-FILE (1/1.1/1.1.1 style-linked abstractNum) + survives a later tracked edit. deps 022,023,026,027 — ALL MET. Rigor FULL · sonnet · xhigh · tests. Then 025 (deploy+smoke). E3 (030-032), Toolset (040-044), Import (050-052) are independent/parallelizable.

### ⬇️ (prior) 027 server-half handoff — kept for reference

**Task 027 SERVER HALF ✅ DONE (2026-07-18, committed `ff9576278`, pushed).** The BFF wire contract for the client cutover is complete:
- **SPE facade** `GetCurrentVersionIdAsUserAsync` (OBO `.../versions` → newest id) on `ISpeFileOperations`/`SpeFileStore`/`DriveItemOperations` (ADR-007). `LoadAsync` captures it best-effort → `LoadComposeDocumentResult.VersionId` + Load wire response `versionId` (FR-06 022-completion).
- **Save wire contract**: `SaveComposeDocumentBody.content` now OPTIONAL; added `baselineVersionId`, `editedParagraphs`, `contentModel`. Both save endpoints relax the content-required guard (replace: content OR {baselineVersionId+editedParagraphs}; create-on-save: content OR contentModel) + map through to `SaveComposeDocumentRequest`. `ComposeContentModel` enums `[JsonStringEnumConverter]` (string kind/alignment over the wire). Additive/backward-compatible. Compose unit 252✅ + contract 85✅; BFF builds clean.

**027 CLIENT HALF — REMAINING (the irreversible docx.js removal).** Files: `utils/docxBridge.ts` (remove `tipTapToDocxBytes`/`tipTapJsonToDocxBytes`/`buildRejectBaselineJson`; add `captureParaIdSnapshot`, `collectEditedParagraphs`, `buildContentModel`), `widgets/ComposeEditor.tsx` (handle: replace `serialize`/`serializeForSave` with `collectEditedParagraphs`/`buildContentModel`; capture load-time text-by-paraId snapshot right after `stampParaIds` at ~:1169; update `ComposeEditorHandle` iface at :445), `widgets/ComposeWorkspace.tsx` (`triggerSave` ~:910-1040 → 4-case), `index.ts:183` (drop `tipTapToDocxBytes` export, add helpers), `types/compose-contracts.ts` (mirror `ComposeEditedParagraph`/`ComposeContentModel`/`versionId`), + ~11 coupled client tests.

**⚠️ KEY DESIGN CONSIDERATION for the client (surfaced 2026-07-18, do NOT lose):** `collectEditedParagraphs` must diff each paragraph's **REJECT-STATE settled text** (accepted edits baked in, PENDING AI-redline insertion text dropped, pending-deletion text kept) against the load-time snapshot — NOT the raw `textContent`. Otherwise a pending redline gets BOTH baked into the synthesizer delta (via EditedParagraphs) AND sent as a `redlineMarksToDocxAnnotations` annotation → double-application. The old `buildRejectBaselineJson` per-node logic (drop insertion-marked, strip redline marks) MOVES INTO the per-paragraph text extraction for `collectEditedParagraphs`. The 4 triggerSave cases: loaded+dirty → `{ editedParagraphs, baselineVersionId, content?(retained fast-path), annotations }` (replace); loaded+clean → `{ content: byte-identical retained, annotations? }` (replace); born-in-editor (AI-draft/blank/edited-browse-local) → `{ contentModel, annotations? }` (create-on-save); unedited browse-local → `{ content: byte-identical }` (create-on-save, preserves FR-06a). Pending redlines still → `annotations` via `redlineMarksToDocxAnnotations(editor.getJSON())`; the reject-baseline is now server-composed (task 023), so the client sends structured content + annotations, never a reconstruction.

### (prior) Task 023 ✅ DONE (2026-07-18). FR-04 AI-redline composition VERIFIED + TESTED. No production change — the composition was already correct in `SaveAsync` (`ResolveSaveBaselineAsync` baseline/render → `EditedParagraphs` synthesizer delta → `Annotations` via the unchanged `DocxAnnotationWriter`). NEW `ComposeServiceAnnotationCompositionTests` (3): (a) annotations-only persist native `w:ins`/`w:del`/`w:comment` on the retained original; (b) annotations + direct-typing delta compose without corrupting either (edit on para B, annotation on para C, para A clean); (c) annotations decorate the 026 born-in-editor render — all OpenXML schema-valid. Compose **252✅**; ADR013_ComposeFacade PASS; publish/CVE unchanged (no src delta).

**Next = task 027 (CLIENT content-model cutover — drop docx.js).** deps 022✅,023✅,026✅ all met. Remove `tipTapToDocxBytes`/`tipTapJsonToDocxBytes`/`buildRejectBaselineJson` from `docxBridge.ts` + `index.ts:183`; add load-time text-by-paraId snapshot + `collectEditedParagraphs()` + `buildContentModel()`; rewrite `triggerSave` (4-case: dirty→EditedParagraphs+versionId / clean→byte-identical Content / born-in-editor→ContentModel / AI→annotations); fix ~11 client test files. Rigor FULL · opus · xhigh · client (`Spaarke.Compose.Components`). **Also open (022 completion, do WITH 027)**: `LoadAsync` must capture+return `VersionId` (FR-06 re-fetch source — 027's dirty-save path needs it; today Load returns only ETag).

### (prior) Task 026 ✅ DONE (2026-07-18, committed bd15cae77, pushed). `ComposeDocumentRenderer` (server-side born-in-editor OOXML authoring) built + wired + tested. Files: NEW `Services/Compose/ComposeDocumentRenderer.cs` + `ComposeContentModel.cs`; MOD `ComposeService.cs` (`_documentRenderer` field/ctor + `ResolveSaveBaselineAsync` branch (a0)), `IComposeService.cs` (`SaveComposeDocumentRequest.ContentModel`), `ComposeModule.cs` (DI singleton); NEW tests `ComposeDocumentRendererTests.cs` (12) + `ComposeServiceBornInEditorSaveTests.cs` (1). **Verify**: Compose suite **249✅** (+13); ADR013_ComposeFacade PASS (ADR007 pre-existing RED = Communication-only, not us); publish **45.69 MB** compressed incl PDBs (−0.31 vs 46.00; 0 pkg delta); CVE clean; OpenXML schema-valid (Office2019 validator green — caught+fixed 4 ordering bugs: tblGrid, tblBorders order, keepNext-before-numPr, nsid-before-multiLevelType); dup-client-paraId re-mint (code-review-found correctness fix). §9.5 gates PASS.

**Downstream order**: 023 (AI redlines/comments compose on the SERVER-AUTHORED baseline — verify+wire `DocxAnnotationWriter` over both the 022 delta AND the 026 render; deps 022✅ met) → 027 (client content-model cutover, drop docx.js; deps 022,023,026 — 026 now ✅) → 024 (seam: loaded byte-identity + born-in-editor numbering golden-file). **Also open (022 completion, before/with 027)**: `LoadAsync` must capture+return `VersionId` (FR-06 re-fetch source — today returns only ETag).

**Uncommitted**: task 026 (8 files: 2 new src, 3 mod src, 2 new tests, + current-task/POML/TASK-INDEX). 5 prior LOCAL commits unpushed (`3a5505c5e`…`5d9d535c2`). Commit 026 when ready; owner batches pushes.

### ⬇️ (prior) RESUME block — task 026 (now complete) — kept for reference
The POML [`tasks/026-compose-document-renderer.poml`](tasks/026-compose-document-renderer.poml) is self-contained (reuse anchors, numbering recipe, file:line seams, acceptance criteria + numbering golden-file). Rigor FULL · opus · xhigh · parallel-safe=false (Services/Compose).

**Five de-risking facts from the 2026-07-18 investigation (not all in the POML):**
1. **Reuse anchor**: `Services/Ai/Export/DocxExportService.cs` is the ONLY from-scratch authoring precedent — `WordprocessingDocument.Create` (:55), `CreateStyledTable`/`CreateTableCell` (:412-486), `AddStyleDefinitions`/`CreateStyle` (:134-176), `SanitizeText` (:557). Reuse the patterns; its numbering is FAKE (literal "1. " text) — do NOT copy that.
2. **The one greenfield = multi-level numbering** — ZERO `NumberingDefinitionsPart` authoring exists anywhere in `src/server/`. Recipe + pitfalls (style-linked not direct `numId`; `%N` lvlText; `isLgl`; `lvlRestart`) in FR-27 + the 026 POML + researcher memo `.claude/agent-memory/researcher/server-docx-authoring-numbering-2026-07-18.md`.
3. **Fidelity target fixture**: `tests/unit/Sprk.Bff.Api.Tests/Fixtures/Compose/RealTemplates/commonpaper-cloud-service-agreement.docx` (9-level numbering, 345 paras, 395 paraIds, 6 tables) — UNZIP it to study its real `numbering.xml`/`styles.xml`.
4. **paraId mint**: apply `ParaIdPreParser`'s scheme (`RandomNumberGenerator.GetInt32(1,int.MaxValue).ToString("X8")`, `0<x<0x80000000`, dedup) at BUILD time — set `Paragraph.ParagraphId` on every emitted `w:p` incl. table cells.
5. **Render seam**: `ComposeService.SaveAsync` / `ResolveSaveBaselineAsync` create-path (add `ComposeContentModel? ContentModel` to `SaveComposeDocumentRequest`, additive; branch to the renderer before `UploadSmallAsUserAsync` :444). OpenXML SDK 3.5.1, no new NuGet.

**Also open (task 022 completion, do alongside or before 027)**: `LoadAsync` must capture+return `VersionId` (today returns only ETag — the FR-06 re-fetch branch I wired in Increment A has no source yet). See FR-06.

**Uncommitted/unpushed state**: tree CLEAN. 4 LOCAL commits not pushed to origin (`3a5505c5e`, `ce50c9877`, `a2773e0dc`, `4b7887a0b`) — all safe in this worktree; push when ready (owner batches pushes). Do NOT re-open the committed server inversion (`ce50c9877`).

**Downstream order**: 026 → 023 (AI-redline compose on authored baseline) → 027 (client content-model cutover: drop docx.js) → 024 (seam: loaded byte-identity + born-in-editor numbering golden). 020/021 superseded; 001 reversed.

---

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | E1 RE-ARCHITECTED (owner-approved 2026-07-18): the SERVER owns all `.docx` authoring; the client never authors bytes. 022 Increment A (server SaveAsync inversion) DONE `ce50c9877`. Plan re-authored: NEW 026 (renderer) + 027 (client cutover). |
| **Step** | Re-architecture investigation (5 streams: content model, creation flow, server OOXML, task graph, external practices) COMPLETE → design/spec/tasks updated. Ready to execute 026. |
| **Status** | Re-plan committed. Task 022 server half done; the born-in-editor gap is now a first-class FR (FR-01a) + task (026), not a blocker. |
| **Next Action** | Execute **task 026** (`ComposeDocumentRenderer` — server-side born-in-editor OOXML authoring: styles + style-linked multi-level numbering + tables + paraId). Then 023 (AI-redline compose) → 027 (client cutover) → 024 (seam). |

### ✅ E1 RE-ARCHITECTURE (2026-07-18, owner-approved) — server owns all `.docx` authoring
Investigation (5 parallel streams) validated the architecture against Harvey (leading legal-AI drafter): **deterministic server-side OOXML authoring, LLM/editor confined to text; client JS exporters are generators, not fidelity round-trippers.** Two server engines: `ComposeParagraphRedlineSynthesizer` (delta onto retained original — loaded-doc edit, BUILT) + NEW `ComposeDocumentRenderer` (from-scratch high-fidelity render — born-in-editor/AI-drafted/blank, styles + **style-linked multi-level numbering** + tables + minted paraId). The client (027) sends a paraId-keyed content model, never `docx.js` bytes. **Artifacts updated**: spec (NEW FR-01a born-in-editor creation + FR-27 numbering fidelity; FR-01 scoped; MUST-rules; Out-of-Scope numbering reconciled); design (NEW §4.4 + §10 reuse row + §11 ADR-039-non-conflict + §12 publish reconcile); TASK-INDEX (022 split; NEW 026/027; deps/waves; 27→29 tasks); 022/023/024 POML amendment banners; plan + design Docxodus→Option C reconcile. **Key finding folded in**: `LoadAsync` never captures `VersionId` (FR-06 re-fetch source) → task 022 completion item. Numbering = the one greenfield (zero `NumberingDefinitionsPart` authoring server-wide); recipe + pitfalls (lvlRestart, style-linked-vs-direct double-numbering) captured in task 026 + FR-27.

### Superseded escalation (2026-07-18, now RESOLVED by the re-architecture) — kept for reference below.

### ✅ Task 022 Increment A COMPLETE (2026-07-18, commit ce50c9877)
Server E1 inversion — `ComposeService.SaveAsync` now derives a dirty save as a DELTA onto the retained load-time original: **(1)** `ResolveSaveBaselineAsync` — `request.Content` same-session fast-path (retained ORIGINAL, not a reconstruction) → else re-fetch the load-time SPE version by `BaselineVersionId` via task-002 `DownloadFileVersionAsUserAsync` (behind SpeFileStore, ADR-007); no reconstruction fallback (FR-01). **(2)** synthesize `EditedParagraphs` (paraId-keyed) via `ComposeParagraphRedlineSynthesizer`, author from OBO identity. **(3)** `Annotations` via `DocxAnnotationWriter` (unchanged; task 023). Contract: `+BaselineVersionId +EditedParagraphs`, `Content` demoted required→optional. Synthesizer injected (optional ctor param, DI singleton already registered). **Additive-safe**: 4 existing FR-06a fidelity tests still green. NEW `ComposeServiceDeltaSaveTests` (3). Compose suite 235✅; ADR013_ComposeFacade✅; publish 46.00 MB compressed incl PDBs (+0.65 vs 45.35; 0 pkg; 0 CVE). **Redis Tier-3 baseline fallback DEFERRED** (§6.5 Path-A scoping — versionId fetch discharges FR-06; Redis needs a Load-path write, out of 022 file scope).

### ⛔ Task 022 Increment B (client docx.js removal) — §6.5 ESCALATION OPEN (2026-07-18)
Removing `tipTapToDocxBytes`/`tipTapJsonToDocxBytes` **wholesale** (POML step 3 + design §133 "drop docx.js entirely") conflicts with two realities the design does not resolve:
1. **Born-in-editor baseline gap**: an AI-drafted / blank-new doc (DEF-08 `initialHtml` seed, `docxBytes=null`) has NO retained load-time original to delta against. Removing the serializer breaks its save; R3 has no server-side HTML→docx materialization (ADR-039 blocks new AI dispatch). FR-01 "never reconstruct" logically applies only to docs that HAVE an original.
2. **AI-redline reject-baseline coupling**: `ComposeEditor.serializeForSave` builds its reject-baseline via `tipTapJsonToDocxBytes(buildRejectBaselineJson)` — that rewire onto the Option C baseline is explicitly **task 023's** charter (FR-04 AI redlines onto Option C baseline).
**Recommended (owner to ratify)**: §6.5 Path A — scope the removal to the *dirty-edit-of-a-loaded-doc* path (the real FR-01 fidelity target); **retain `tipTapToDocxBytes` NARROWLY** as the born-in-editor serializer with a documented rationale (no original ⇒ reconstruction is the only option and is not a fidelity regression). Tensions with the "no residual" Option-C directive → owner call required. Client work after decision: load-time text-by-paraId snapshot at `stampParaIds` time → `collectEditedParagraphs()` handle (dirty paras by paraId) → `triggerSave` sends `BaselineVersionId`+`EditedParagraphs` (dirty-loaded path) / `Content` byte-identical (clean) / retained serializer (born-in-editor). Fix ~5 client test files mocking `tipTapToDocxBytes`.

### Superseded handoff (pre-Increment-A) — kept for reference below.

### Handoff Notes — Task 022 (E1 SaveAsync cutover, Option C) — SCOPED, NOT STARTED (fresh session recommended — irreversible cutover on 1747-LoC file)

**Rigor**: FULL · opus @ xhigh · **prescriptive** (irreversible save-path inversion — deviations escalate §6). Deps 002✅ 003✅(gate PASS) + Option C engine ✅. Hot-path: Services/Compose + client — `/conflict-check` before the BFF PR (R3 #656 now merged to master; re-open a fresh PR for 022).

**Current model** (the lossy path to invert): client reconstructs whole `.docx` via `tipTapToDocxBytes` → `request.Content` → `SaveAsync` (`ComposeService.cs:340`) treats it as an OPAQUE baseline + applies `request.Annotations` (AI redlines via `DocxAnnotationWriter`, at `ComposeService.cs:378-384`) → persists (transient-create Fork B at ~403; replace path below).

**Target model (FR-01/02/06 + Option C)**:
1. **Contract** (`IComposeService.cs:393` `SaveComposeDocumentRequest`): ADD `string? BaselineVersionId` (load-time SPE version — Load already captures it: `ComposeService.cs:564` `VersionId = saved.Id`, surfaced on `LoadComposeDocumentResult`) + `IReadOnlyList<ComposeEditedParagraph>? EditedParagraphs` (paraId-keyed). DEMOTE `Content` to an OPTIONAL same-session fast-path (still `required`? → make nullable; a dirty save now sends edits + versionId, not full bytes). Keep `Annotations` unchanged.
2. **SaveAsync inversion**: resolve baseline = (a) `request.Content` if present (same-session fast-path) → (b) else fetch load-time version by `BaselineVersionId` via task-002 `SpeFileStore.DownloadFileVersionAsUserAsync` (ADR-007 — stays behind the facade) → (c) else size-capped Redis Tier-3 (ADR-009/015). If none resolvable → clear error, do NOT fall back to a reconstruction. Then: `redline = _synthesizer.SynthesizeRedline(baseline, request.EditedParagraphs, author, ts)` → then the EXISTING `Annotations` apply (`_annotationWriter.Annotate`) on top → persist via If-Match (unchanged). **Clean-save passthrough (no edits, no annotations) stays byte-identical — FR-06a (`ComposeServiceUploadFidelityTests`).**
3. **FR-05 format-change**: `ComposeEditedParagraph(ParaId, NewText)` is plain-text only → to emit `rPr/pPrChange` the payload must carry the edited paragraph's run FORMATTING (not just text). Options: (a) extend the DTO to a formatted fragment (runs+marks) and have the synthesizer diff run-props → emit `rPrChange`; (b) fold FR-05 into task 032 (formatted AI insertions / FR-15) which already owns run-formatting on the wire. DECIDE at design time; simplest MVP = text-diff now (FR-05 as a fast-follow).
4. **Client**: remove `tipTapToDocxBytes` (`docxBridge.ts:215`) + `tipTapJsonToDocxBytes` (`:278`) from the export + `index.ts:183` export; rewrite `ComposeWorkspace.triggerSave` to send `BaselineVersionId` (from Load state) + paraId-keyed `EditedParagraphs` (the editor already carries paraIds per task 011 — collect dirty paragraphs' {paraId, text}) instead of reconstructed bytes. Keep the clean-save `state.docxBytes` passthrough. Fetches via `@spaarke/auth`.
5. **NFR-06 seam**: `tests/integration/seam/**` WebApplicationFactory — load → edit paragraphs → save → reload; assert untouched paragraphs' OOXML preserved (paraId + tables) + edited paragraphs carry `w:ins`/`w:del`. Also discharges the task-002 versionId-baseline seam.
6. Publish-size + CVE + ADR013/ADR007 checks; §10 Placement Justification + Path B supersession note in the PR.

**Reuse**: `ComposeParagraphRedlineSynthesizer` (built), `ComposeParaIdSpliceMap`, task-002 `DownloadFileVersionAsUserAsync`, `DocxAnnotationWriter` (Annotations path). **Resume**: `work on task 022` in a FRESH session.

### ✅ Option C foundation + residual cleanup COMPLETE (2026-07-17)
NEW `ComposeParagraphRedlineSynthesizer` (word-level LCS diff per paraId-keyed edited paragraph → native w:ins/w:del in place on the retained original; preserves paraId+tables+structure by construction; reuses `ComposeParaIdSpliceMap`; fail-fast on unmatched/dup paraId). **Removed (no tech debt):** `ComposeRedlineComparerService`+tests (task 021), `ComposeParagraphSpliceService`+tests (task 020), OptionC spike test, **Docxodus package + SkiaSharp exclusion** (task 001 reversed), all stale WmlComparer doc-comments. DI updated. NFR-09 harness repointed → **gate PASS** (345/345 paraIds + 6/6 tables on real CSA). Compose suite 424✅; ADR013_ComposeFacade PASS; publish **45.35 MB** (−0.59 vs 45.94, Docxodus gone); no Docxodus/SkiaSharp in output. design.md §4 + spec.md FR-02/03/05/07 + scope amended (§6.5). Kept: ComposeParaIdSpliceMap, ParaIdPreParser, AnnotationReanchorService, DocxAnnotationWriter/Reader.

### 🔔 NFR-09 gate FAILED — task 022 (E1 keystone cutover) BLOCKED (2026-07-17)
Docxodus **6.4.0** WmlComparer, on REAL firm templates (Common Paper CSA + NDA), (1) **strips w14:paraId** on all paragraphs (→ pt14:Unid) and (2) **drops an unchanged top-level table** (NFR-07 violation). S1 validated the opposite on **7.1.0 (net10)** — false-green from spiking a newer major than the net8 codebase runs. Task-020 splice verified clean (defects are Docxodus-only). Verdict + §6.5 resolution paths: [`notes/spikes/S1-nfr09-real-template-hardening-2026-07-17.md`](notes/spikes/S1-nfr09-real-template-hardening-2026-07-17.md). Harness: `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/Nfr09RealTemplateHardeningTests.cs` (10 tests green, defects pinned as characterization tests).

### Committed this session
- `5aed0f2d5` — task 012 (FR-11/FR-12 paraId-primary anchoring + splice key) — **pushed** to PR #656.
- `e492e1844` — task 020 (FR-02 rebuild+splice) — **committed, NOT pushed**.
- task 021 (FR-03/FR-05 WmlComparer adapter) — **DONE, NOT yet committed** (3 files: ComposeRedlineComparerService.cs NEW, ComposeModule.cs +1 line, ComposeRedlineComparerServiceTests.cs NEW).

### Task 021 completion (2026-07-17)
NEW `ComposeRedlineComparerService.SynthesizeRedline(retainedOriginal, splicedEdited, author, revisionTimestamp?)` — thin Docxodus WmlComparer adapter (byte[]→byte[]); NEW `ComposeRedlineException`; DI unconditional singleton. **§6.5 Path-C**: 6.4.0 (net8) API-shape delta from S1's 7.1.0 recipe — verified by reflection: `WmlComparer.Compare` identical, byte-in ctor is `WmlDocument(string fileName, byte[])`, output `.DocumentByteArray`, format-change = explicit `DetectFormatChanges=true`. Diff path = WmlComparer only (no HtmlToWml/FormattingAssembler; publish has 0 libSkiaSharp). Tests: NEW 12 (ins/del+author, untouched keep paraId + no revisions, identical→0, bold→rPr/pPrChange NOT del+ins, S1/S1b no-throw on nested-table/numbering/delete/split, negatives). Text-edit fixtures via ComposeParagraphSpliceService (real 020→021 pipeline). Fixtures include StyleDefinitionsPart (WmlComparer reads it). Compose suite 429✅ (+12); ADR013_ComposeFacade PASS; publish 45.94 MB compressed incl PDBs (0 delta); 0 CVE; ADR-007 no Graph. §9.5 PASS. PR MUST cite §10 Placement Justification. Consumed by 022; NFR-06 seam rides 022.

### Handoff Notes — Task 021 (Docxodus WmlComparer adapter) — NOT STARTED 2026-07-17

**Rigor**: FULL · opus @ xhigh · directional · BFF (Services/Compose, parallel-safe=false). Deps 001✅ 020✅. Hot-path /conflict-check: same surface as 012/020, CLEAR (only open PR touching Services/Compose is our #656).

**API recipe (from S1 spike — de-risks step 2)**: namespace **`Docxodus`**; one-liner —
`WmlComparer.Compare(new WmlDocument(origBytes), new WmlDocument(editedBytes), new WmlComparerSettings { AuthorForRevisions = "Spaarke AI" })` → returns a `WmlDocument`; get bytes via its byte accessor (S1 used `.SaveAs(path)` — for byte-in/byte-out mirror `DocxAnnotationWriter`; `WmlDocument` exposes `.DocumentByteArray`). Format-Change Detection + author attribution work out of the box (S1 (c)). Emits minimal `w:ins`/`w:del`; bold-only change → `rPr/pPrChange` (NOT del+ins) — assert this (FR-05/D4).

**⚠️ VERIFICATION RISK (do FIRST in step 1)**: S1 validated on Docxodus **7.1.0 (net10)** but task 001 §6.5 Path-C shipped **6.4.0 (net8)**. CONFIRM the 6.4.0 API surface matches: `Docxodus.WmlComparer.Compare`, `Docxodus.WmlDocument(byte[])` ctor, `Docxodus.WmlComparerSettings.AuthorForRevisions`, and the byte accessor. Quick check: `grep`/reflect the restored `Docxodus.dll` (in ~/.nuget/packages/docxodus/6.4.0/) or write a 3-line probe. If 6.4.0 differs, adapt (same fork lineage — likely identical) or surface as §6.5.

**MUST NOT** touch `HtmlToWml` / `FormattingAssembler` (re-pulls SkiaSharp — packaging exclusion from task 001). Diff path = `WmlComparer` only.

**Plan**: NEW `Services/Compose/ComposeRedlineComparerService.cs` (name TBD) — `byte[] SynthesizeRedline(ReadOnlyMemory<byte> retainedOriginal, ReadOnlyMemory<byte> splicedEdited, string author)` → redline-marked bytes. Mirror `DocxAnnotationWriter` byte-in/byte-out shape. Register unconditional singleton in ComposeModule (like 020's ComposeParagraphSpliceService). Consumed by task 022 (SaveAsync inversion) — NOT wired here.

**Tests** (`tests/unit/Sprk.Bff.Api.Tests/Services/Compose/`): build retained-original + spliced-edited pairs via `ComposeParagraphSpliceService` (task 020 — reuse its fixture idiom) then compare. Assert: 3 edited paras → minimal ins/del + author set; bold-a-word → `rPr/pPrChange` not del+ins; NO exception on nested-table / 3-level-numbering / whole-para-delete / paragraph-split fixtures (S1/S1b). Real docx, no transport mocks. (Building the S1b edge-case fixtures is the bulk of the effort.)

**Step 4 verify**: publish — verify NO `libSkiaSharp` in `deploy/api-publish/` output; report absolute compressed + delta vs worktree baseline (012/020 both measured **45.94 MB** compressed incl PDBs — 021 activates Docxodus code paths but the package is already referenced since task 001, so expect ~0 additional delta at publish since 001 already counted it). `dotnet list package --vulnerable` no new HIGH. ADR013_ComposeFacade NetArchTest green (ADR007 pre-existing RED = Services.Communication, not us). PR: §10 Placement Justification.

**Resume**: `work on task 021` (or `continue`).

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
| **Task ID** | 027 → ✅ COMPLETE (next: 024) |
| **Task File** | [`tasks/027-client-content-model-cutover.poml`](tasks/027-client-content-model-cutover.poml) |
| **Title** | FR-01 — client content-model cutover (drop docx.js) + FR-06 VersionId |
| **Phase** | 2 E1 |
| **Status** | ✅ completed 2026-07-18 (FULL · opus · xhigh). Server `ff9576278` + client this session; client 205✅, server Compose 252✅/contract 85✅, docx dep removed. E1 quartet (022/023/026/027) all done. |
| **Started** | 2026-07-18 |

**Placement Justification (§10 BFF hygiene)**: (1) Existing — overlaps `DocxExportService` (AI-analysis export, `Services/Ai/Export`, numbering is FAKE literal text) + `ComposeParagraphRedlineSynthesizer` (edit path, deltas onto a retained original). (2) Extension — can't extend either: DocxExportService is an AI-domain concern behind the ADR-013 boundary with fake numbering; the synthesizer requires a retained original a born-in-editor doc lacks. NEW pure engine in `Services/Compose` justified. (3) Cost-of-doing-nothing — without it an AI-drafted legal doc is saved by the lossy client `docx.js`, flattening 1/1.1/1.1.1 clause numbering + real styles into literal "1." text runs that later tracked-change edits corrupt. Pure (`ComposeContentModel` in / `byte[]` out), no AI, no Graph, no new NuGet. ADR-039 non-conflict (deterministic OOXML authoring, not AI dispatch — design §11).

**CSA numbering study (Step 0 done)**: the style-link mechanism = ONE `abstractNum` (multilevel, 9 levels ilvl 0-8), each level carries BOTH `<w:pStyle w:val="HeadingN"/>` AND a `%N` `lvlText` cascade; ONE `w:num`→that abstractNum; each `HeadingN` **paragraph style** carries `<w:pPr><w:numPr><w:ilvl/><w:numId/></w:numPr></w:pPr>`; body paragraphs use ONLY `<w:pStyle val="HeadingN"/>` (no direct paragraph numId) → no double-numbering. Ordered/bullet lists = `ListParagraph` + DIRECT `numPr` (ListParagraph carries no style numId, so a direct numPr is not double-numbering); ordered restart = fresh `w:num` with `<w:lvlOverride><w:startOverride val="1"/></w:lvlOverride>`.

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
