# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-07-16
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | 011 — FR-09/FR-10 client load-time paraId carry + split-minting (next; CLIENT) |
| **Step** | — (not started) |
| **Status** | not-started |
| **Next Action** | ✅ Task 010 COMPLETE (ParaIdPreParser + LoadAsync map projection; 8 tests green; 47.28 MB). Run `task-execute` on `tasks/011-client-paraid-carry-and-minting.poml` (client TS/Jest, `@tiptap/extension-unique-id` per S2). |

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

### Handoff Notes — Task 010 (E2 paraId pre-parse) analysis + plan (2026-07-17)

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
