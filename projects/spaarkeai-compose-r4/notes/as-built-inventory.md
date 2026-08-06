# R4 As-Built Inventory — Kill List / Keep List (file:line ground truth)

> **Created**: 2026-07-22
> **Purpose**: The precise inventory of the current Compose translation/save layer, so `design.md` and the downstream WBS reference exact surfaces. "Code wins, docs lag" (root CLAUDE.md §2) — this is grounded against the merged build in `Sprk.Bff.Api/Services/Compose/` and `Spaarke.Compose.Components/`.
> **Verified**: 2026-07-22 against `work/spaarkeai-compose-r3` tip (Phase-1 + Bug-A deployed).

---

## KEEP + BUILD ON (assets R4 extends)

| Component | Path | Role in R4 |
|---|---|---|
| `ComposeDocxProjectionBuilder` | `Services/Compose/ComposeDocxProjectionBuilder.cs` | The custom `w:p`→HTML mapper that replaced mammoth (Phase 1). **Extend** to also emit the intra-paragraph offset-addressing table + opaque atoms for SDT/fields. |
| `ComposeDocxProjection` (record) | `Services/Compose/ComposeDocxProjection.cs` | Projection contract (status/canEdit/html/paraIdMap/warnings). Extend with addressing table. |
| `ParaIdPreParser` / `ExtractParaIds` | `Services/Compose/ParaIdPreParser.cs`; `AnnotationReanchorService.ExtractParaIds` | Document-order paraId extraction (incl. table cells, nested tables). Reused by the Patch Engine to resolve nodes O(1). |
| `ComposeBaselineParaIdStamper` | `Services/Compose/ComposeBaselineParaIdStamper.cs` | Mints + stamps paraIds onto id-less paragraphs. **Extend** to persist ids into the shadow package on ingest. |
| `AnnotationReanchorService` | `Services/Compose/AnnotationReanchorService.cs` | Fuzzy re-anchor with AUTO/REVIEW/ORPHAN bands + ambiguity guard + "never silently drop." Becomes the **last-resort** fuzzy layer for cross-Word-session paraId regen + stale-base saves. |
| `paraIdExtension` (client) | `Spaarke.Compose.Components/src/widgets/paraIdExtension.ts` | TipTap schema carries `data-paraid` as hidden node attr. Keep; operations resolve positions via it. |
| `stampParaIds` / `captureParaIdSnapshot` | `Spaarke.Compose.Components/src/utils/docxBridge.ts` | Server-owned load-time id carry + snapshot. Keep the id-carry; the snapshot's *purpose* changes (op log, not paragraph diff). |
| SPE facade + `SpeDocumentViewer` | `Services/SpeFileStore` (facade); viewer component | Store + open-to-web/desktop launch surface. Unchanged. |
| Native OOXML annotation edge-case wisdom | `DocxAnnotationWriter.cs` EDGE-1…4 comments | **Migrate** the wisdom (comment-before-trackchange ordering; `w:delText` not `w:t`; paragraph-mark deletion via `w:pPr/w:rPr/w:del`; monotonic revision-id seeding) into the Patch Engine. |

## RIP OUT (kill list — replaced by the operational Patch Engine)

| Component | Path | Why it dies | Replaced by |
|---|---|---|---|
| `DocxAnnotationWriter.LocateTarget` (whole-doc text-search) | `Services/Compose/DocxAnnotationWriter.cs:316-369` | Root cause of interior-location 422s. Text-search anchoring violates invariant I-7. | Patch Engine resolves node by paraId (O(1)) + applies at offset. |
| `DocxAnnotationWriter` (whole class, as the write path) | `Services/Compose/DocxAnnotationWriter.cs` | Text-anchored `DocxAnnotation` contract (`TargetText`). One of two save paths. | Unified `ComposeShadowPatchEngine`. Migrate EDGE-1…4 wisdom first. |
| `ComposeParagraphRedlineSynthesizer` (paragraph-diff save) | `Services/Compose/ComposeParagraphRedlineSynthesizer.cs` | Coarse paragraph-granularity; re-diffs runs; cannot do structural edits. | Step-level operations applied by the Patch Engine. |
| `collectEditedParagraphs` / paragraph `{paraId,text}` export | `Spaarke.Compose.Components/src/utils/docxBridge.ts:330-395` | Superseded by step-level operation capture. | ProseMirror step→operation interceptor. |
| `buildContentModel` full-render export (born-in-editor path) | `Spaarke.Compose.Components/src/utils/docxBridge.ts:397+` | Born-in-editor full render stays, but must feed the SAME operation/patch model, not a parallel path. | Reconcile into the unified model (design §decision). |
| Residual `mammoth` fallback mounts | `docxBridge.ts` (`docxToTipTapHtml`) + `ComposeEditor.tsx` fallback branch | Projection builder is the only mapper now. | Remove once no projection-less mount remains. |
| `DocxAnnotation.TargetText` text-anchor field | `DocxAnnotationWriter.cs:611-654`; wire contract in `Api/ComposeEndpoints.cs` | Text anchoring eliminated from write path. | `{paraId, offset|range}` operation anchor. |

## The two-path problem (the core defect being removed)

Today, on save, **two independent writers** touch the OOXML:
1. **AI/user redlines** → `collectEditedParagraphs` (`{paraId,text}`) → `ComposeParagraphRedlineSynthesizer` (paraId-keyed, position-based). *This path is sound but coarse.*
2. **Comments + anchored annotations** → `anchoredAnnotationsToDocxAnnotations` → `DocxAnnotationWriter.LocateTarget` (whole-doc **text-search**). *This path is the fragile one — the 422 source.*

Bug A (deployed in r3) filtered AI redlines OFF path 2 onto path 1 (comment-kind only remains on path 2). R4 **eliminates path 2's text-search** and **unifies both** into a single operational Patch Engine — one byte-author, one anchor model (paraId+offset), no text-search anywhere.

## Reference: known failure surfaces from R3 UAT (the acceptance targets)
- **Interior-location 422** ("a tracked change could not be located") — `LocateTarget` whole-doc text-search miss on tab/whitespace/typographic drift. → killed by ID-anchored patch.
- **eTag mismatch after create-on-save** (`InvalidOperationException` — resource changed since caller last read) — precondition goes stale when the create-on-save follow-up write advances the item eTag. → fixed by eTag sequencing (Phase 5).
- **Empty-paragraph drift** (mammoth `ignoreEmptyParagraphs` dropped 9 paragraphs on the CIPO doc, 48 vs 39) — already mitigated in Phase 1's projection builder; the corpus byte-diff harness guards against regressions.
