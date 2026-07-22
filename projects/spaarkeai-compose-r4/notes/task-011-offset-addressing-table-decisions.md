# Task 011 — Intra-paragraph offset-addressing table decisions (FR-01)

> **Created**: 2026-07-22 by task 011 (`011-offset-addressing-table.poml`)
> **Artifacts**:
> - `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeDocxProjection.cs` — record extended with `OffsetAddressingTable` + the `ParaOffsetMap`/`RunBoundary`/`RunTrackChange`/`RunOffsetResolution` types.
> - `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeDocxProjectionBuilder.cs` — emits the table (per-paraId run-boundary map) in the identity pass; `BuildParaOffsetMap`/`CollectRunBoundaries`/`RunEditorLength`.
> - `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/ComposeDocxProjectionBuilderTests.cs` — 6 new tests (5 in-memory + 1 corpus theory over all 3 fixtures).

## What was built

The **intra-paragraph offset-addressing table** — the D2 FINE anchor's server-side resolver. Alongside the
paragraph-level `ParaIdMap` (coarse anchor), the projection now emits `OffsetAddressingTable`: one
`ParaOffsetMap` per body paragraph, index-aligned with `ParaIdMap` (same `Descendants<Paragraph>()` walk, same
order, same paraIds — computed in the SAME loop so it cannot drift). Given `(paraId, editor-offset)`, the map
resolves the exact `(runIndex, run-local-offset)` OOXML run split the Patch Engine (task 030) applies — with
zero DOM re-walk and zero text-search (I-3 / I-7).

## `<justification>` (root §11)

- **Existing**: `ComposeDocxProjection.ParaIdMap` is paragraph-LEVEL only — addresses whole paragraphs, no
  intra-paragraph run/offset resolution.
- **Extension**: YES — extend the record + builder. No new service, no new DI registration, no parallel
  projection type, no new package.
- **Cost-of-doing-nothing**: without the table, ops can only address WHOLE paragraphs — back to
  paragraph-granularity, defeating D2's fine anchor and reintroducing the coarse-delta / re-diff-runs failure
  the operational model exists to remove.

## KEY DECISION — the run-index space is the EDITOR-VISIBLE RUN FLATTEN (not `para.Elements<Run>()`)

The table's run sequence is the paragraph's **editor-visible run flatten** in document order — the SAME descent
the projection's `RenderInline` uses: into `w:hyperlink`, `w:ins` (`InsertedRun`), `w:del` (`DeletedRun`),
`w:sdt` (`SdtRun`/`SdtContentRun`). Rationale:

1. The table maps **editor offsets** — what the client measures over the projection. The projection's
   editor-visible text INCLUDES hyperlink text and, per F-02 revision flattening, pre-existing `w:ins`/`w:del`
   text (present as plain text). So an offset the client computes can land inside hyperlink/ins/del content;
   only a flatten that includes those runs can address it. A `para.Elements<Run>()`-only model (direct children)
   would leave any offset past a pre-existing tracked change unaddressable.
2. On a **track-changes-clean, hyperlink-free** paragraph the flatten COINCIDES with `para.Elements<Run>()` —
   the enumeration the task-005 applier spike used. So the model mirrors the spike on the corpus and generalizes
   correctly to nested runs. It carries `patch-engine-ab-decision.md` **finding #2** ("`w:ins` is not a `w:r`")
   forward: **task 030 must index this same flatten**, not raw `Elements<Run>()`.
3. Contrast with `AnnotationReanchorService.GetParagraphText`, which deliberately uses `Elements<Run>()`
   (direct-child, SETTLED text) because it measures drift against text Word treats as settled. Different job,
   different correct answer. The addressing table addresses the EDITOR offset space, so it includes ins/del.

`RunEditorLength` = the run's `w:t`/`w:delText` character length + 1 per `w:br`/`w:tab`/`w:noBreakHyphen` glyph
(each maps to one editor position, matching what `RenderRun` emits). This is the canonical bridge offset
semantics; task 020 (client capture) and task 030 (patch engine) adopt the same.

## Resolution contract (deterministic)

- Valid closed offset domain is `[0, TotalLength]`. `TotalLength` (sum of run lengths) addresses the point after
  the last character (paragraph end).
- **Interior**: the first run whose half-open span `[Start, Start+Length)` strictly contains the offset →
  `(runIndex, offset - Start)`. Zero-length runs are present (they preserve run-index alignment with the patch
  engine's enumeration) but never contain an offset.
- **Interior boundary** (offset == end of run K == start of run K+1): LEFT-biased onto K+1's start `(K+1, 0)` —
  the same physical split point as `(K, len(K))`.
- **Terminal** (offset == `TotalLength`): the last non-empty run's end.
- **Negative (FR-01)**: offset `< 0` or `> TotalLength` is **REJECTED** — `TryResolve` returns `false`;
  `Resolve` throws `ArgumentOutOfRangeException`. **Never clamped.**

## Escalation trigger — split-run-over-existing-tracked-changes: NOT fired (with rationale)

The POML `<escalation>` fires only if pre-existing `w:ins`/`w:del` make the editor-offset → run partition
**ambiguous** ("more than one defensible run split for a given offset"). It does **not** fire here:

1. **No ambiguity in the table.** With the flatten model + left-biased boundary rule + reject-out-of-range,
   EVERY offset resolves to exactly ONE `(runIndex, run-local-offset)`. The table records a deterministic
   structural fact; it does not decide the tracked-change MERGE semantic (whether a new edit at a settled/deleted
   boundary lands inside vs. before an existing `w:del`). That semantic is the Patch Engine's concern (task 030),
   and it is a decision about what to DO at a resolved point, not about WHERE the point is. `RunBoundary` carries
   a `RunTrackChange` tag (`None`/`Inserted`/`Deleted`) so task 030 has the context it needs — the table
   "accounts for" tracked changes by including and tagging their runs, honestly, without guessing.
2. **No ambiguous corpus case exists to surface.** Per `corpus-manifest.md` note (1a) the CIPO doc is
   track-changes-clean as saved (zero `w:ins`/`w:del`), and docs 2–3 carry none. The live-tracked-changes
   fixture is placeholder **row 4 — not yet supplied**. The trigger says to surface "the specific corpus case";
   there is none. If/when the owner supplies a live-redline doc (row 4), task 030's apply-time tracked-change
   merge semantic should be revisited against it — the table addressing is already defined and deterministic.

## Placement Justification (root §10)

Extends KEEP assets in `Services/Compose/` (the projection record + builder) — no new service, no new DI, no new
endpoint, no new package. Pure: bytes/records in, records out; no `Microsoft.Graph` (ADR-007), no AI-internal
type (ADR-013 Tier-1). Privacy: the table carries integers only (run indices/offsets/lengths + the paraId
already on the HTML) — **no document text** (Tier-1 safe). Per `.claude/constraints/bff-extensions.md` it belongs
in-process alongside the existing `Services/Compose/*`.

## Verification

- BFF build: **green** (0 errors; warnings all pre-existing/unrelated).
- Unit tests: **ComposeDocxProjectionBuilderTests all pass** (20 cases incl. the new 5 in-memory + corpus
  theory over all 3 fixtures — determinism across two builds, contiguous gap-free run spans, every in-domain
  offset resolves, past-end rejected; on CIPO's 108 paragraphs + Engagement Letter's mid-run `w:br`).
- Tier-1 NetArchTest `ADR013_ComposeFacadeTests`: **green**.
- Publish size: **46.11 MB compressed** (incl. PDBs) — **0 delta** vs the task-003 baseline; ≤60 MB ceiling;
  zero new runtime package (no `.csproj` change).
- CVE: no NEW HIGH CVE (zero package refs added). Pre-existing transitive
  `System.Security.Cryptography.Xml 8.0.3` HIGH advisories are baseline debt, unrelated to this task.

## Coordination

- Shared-file task with **012** (opaque atoms) — sequenced 011 BEFORE 012; changes are additive and do NOT touch
  SDT/opaque handling, so 012 can layer on cleanly.
- Task 010 edits `ComposeBaselineParaIdStamper.cs` (different file) — no overlap.
- Through-the-wire seam proof is **task 013**.
