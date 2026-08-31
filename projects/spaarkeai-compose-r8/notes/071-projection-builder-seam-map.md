# Task 071 — `ComposeDocxProjectionBuilder.cs` seam map

> **Analysed**: 2026-08-31 · **File**: `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeDocxProjectionBuilder.cs`
> **Size at analysis**: **3,593 lines** (POML says 3,085 — it grew during Track A, same drift as 070)

## Binding criterion (NOT the POML's)

Same correction task 070 made, for the same reason — recorded again here so a reader of this file alone
is not misled:

- ~~"under 2,000 lines"~~ — the LOC ratchet was **retired 2026-08-20** (commit `866f9c101`, root
  CLAUDE.md §11.5, `docs/standards/COMPONENT-COMPLEXITY.md`). Size is a prompt to look, not a verdict.
- ~~"DELETE its waiver entry from `GodClassGuardTests.cs`"~~ — **that file does not exist** (deleted by
  the same commit). There is no waiver to delete.

Binding instead: **extract each cluster that has its own reason to change, and state that reason per
unit.** A large *cohesive* remainder is a legitimate outcome under §11.5.

**This decomposition is warranted on the standard's own terms, not merely on line count** — see §2.
Everything else in the POML still binds, in particular the equivalence proof (§1), ADR-010 (internal
collaborators, no new DI registration), behaviour-preserving-only, and the two NEGATIVE criteria
(`ProjectRun` capture not widened; no `body.Descendants<Paragraph>()` walk introduced).

---

## 1. The equivalence oracle — built and validated FIRST

The POML makes the proof mandatory and empirical: *"Argument is not proof for this property."* The
instrument is `tests/integration/seam/Compose/ComposeProjectionEquivalenceOracle.cs` — **temporary
scaffolding, deleted when 071 lands** (a stored-snapshot equality test over 25 real documents would fail
on every legitimate future fidelity change; ADR-038 §7 B12 bans that shape, and Track A is still widening
the projection).

It captures **both** public entry points — `Build()` (HTML) and `BuildContentModel()` (canonical model) —
for all **25** corpus documents, as fully-serialised JSON rather than a hash, so a difference localises to
a block/run/warning instead of merely announcing itself.

**An instrument used as proof needs its own proof.** Four controls, all run before any code moved:

| Control | Result |
|---|---|
| **Deterministic** — two runs of identical code | ✅ byte-identical (a monotonic mint is injected via the existing internal ctor seam; the production CSPRNG mint would make any diff meaningless) |
| **Non-vacuous** — the captures contain real content | ✅ 50 projections: 33 `Success`, 17 `Partial`, **zero `Failed`**; 29,627 lines captured |
| **Sensitive — HTML path** — seeded `FormatPt` `"0.##"`→`"0.#"` | ✅ detected, and **only** in `.html.json` |
| **Sensitive — content-model path** — seeded `MapAlignment` Center→Right | ✅ detected in 3 documents, and **only** in `.model.json` |
| **Restores** — seed reverted | ✅ reproduces the baseline byte-for-byte |

Both seeds were reverted; the tree was confirmed clean via `git status` before the baseline was frozen.

> The two sensitivity seeds are deliberately in *different* pipelines. A single seed would have proven
> only that the oracle sees *something*; the projection has two independent outputs, and a control that
> exercises one says nothing about the other.

**Usage** (the proof is the diff, and it must be empty):

```
COMPOSE_ORACLE_OUT=<dir>/before  dotnet test --filter ComposeProjectionEquivalenceOracle
...decompose...
COMPOSE_ORACLE_OUT=<dir>/after   dotnet test --filter ComposeProjectionEquivalenceOracle
diff -r <dir>/before <dir>/after        # MUST be empty
```

Unset `COMPOSE_ORACLE_OUT` ⇒ the test is inert, so it never runs in CI and never gates a build during the
window it exists.

---

## 2. Why this file is a genuine decomposition candidate (not just a big file)

`COMPONENT-COMPLEXITY.md` asks whether the component changes for more than one reason. It does — and the
strongest evidence is not the line count but a **measured** cross-reference of every shared helper against
the two pipelines' line ranges:

**The file contains two entirely independent output pipelines** that share only a set of OOXML-reading
primitives:

- `Build()` → `ComposeDocxProjection` (paraId-tagged HTML for the editor)
- `BuildContentModel()` → `ComposeCanonicalModelProjection` (the canonical model the renderer consumes)

They produce different shapes for different consumers, change for different reasons, and touch each other
nowhere. That is the low-cohesion signal the standard names ("do its methods cluster into groups that
barely touch each other? *Those clusters are the seams*").

Two structural facts confirm the split is real rather than cosmetic:

- **`_mint` is used ONLY by the HTML pipeline** (lines 176, 246 — both inside `Build()`). The
  content-model walk is entirely stateless, so it can be extracted without carrying any instance state.
- **`BuildContext` already receives minting as a `Func<HashSet<string>, string>` delegate**, so the HTML
  emitter is already decoupled from the builder instance.

## 3. The five clusters

| # | Cluster | Reason to change | Members | ~LOC |
|---|---|---|---|---|
| 1 | **Numbering subsystem** | Word's numbering semantics + deterministic label computation (must match Word exactly — R4.5 WS-3/WS-4) | `NumberingLevelDef` · `NumberingLevelOverrideDef` · `ParagraphNumberingRef` · `NumberingModel` · `BuildNumberingModel` · `ToLevelDef` · `ResolveNumStyleLinkTarget` · `ResolveStyleNumbering` · `ResolveParagraphNumbering` · `NumberingComputationResult` · `NumberingComputationEngine` | ~575 |
| 2 | **Shared OOXML reading primitives** | How a construct is *read out of* OOXML — independent of either output shape | `TryAdvanceFieldScan` · `FieldScanState` · `IsComplexObjectRun` · `ExtractRunsDisplayText` · `ExtractAtomDisplayText` · `IsSpecialSdtControl` · `RubyBaseRuns` · `HeadingLevel` · `ResolveOrdered` · `ResolveHyperlinkHref` · `ResolveSymbolGlyph` · `IsOn` | ~400 |
| 3 | **Offset-addressing table** | The D2 fine-anchor resolver contract (`(paraId, offset)` → run split) | `BuildParaOffsetMap` · `CollectRunBoundaries` · `RunEditorLength` | ~120 |
| 4 | **HTML projection** | The editor's HTML/TipTap contract | `Build` · `RenderBlockChildren` · `RenderParagraph` · `RenderTable` · `EmitBlockAtom` · `RenderInline` · `RenderRun` · `RenderHyperlink` · `ListInfo` · `AppendParagraphStyle` · `AppendIndentDeclarations` · `TwipsToPoints` · `FormatPt` · `MintUnique` · `BuildContext` | ~1,000 |
| 5 | **Content-model projection** | The canonical content model / render-on-save hub | `BuildContentModel` · `ProjectBlockChildren` · `ProjectParagraph` · `ProjectTable`/`ProjectTableFacts`/`ProjectCellFacts`/borders/width · `ProjectInline` · `ProjectRun` · `ProjectComments` · `TryCarryField` · `TryCarryEmbeddedObjects` · `CaptureRunFormatChange` · `MapAlignment` · `ModelWalkContext` | ~1,500 |

**Measured shared-vs-owned split** (reference counts per pipeline line-range) — this is what decided
cluster 2's membership, rather than a guess about which helpers "feel" shared:

| Helper | HTML | Model | → |
|---|---|---|---|
| `ExtractRunsDisplayText` | 8 | 6 | shared |
| `IsSpecialSdtControl` | 6 | 3 | shared |
| `ExtractAtomDisplayText` | 5 | 3 | shared |
| `IsOn` | 5 | 10 | shared |
| `ResolveSymbolGlyph` | 4 | 2 | shared |
| `HeadingLevel` | 3 | 2 | shared |
| `ListInfo` | 6 | **0** | HTML-only |
| `CollectRunBoundaries` | 9 | **0** | offset table |
| `RunEditorLength` | 5 | **0** | offset table |
| `TwipsToPoints` / `FormatPt` | 4 / 4 | **0** | HTML-only |
| `FieldAtomDataAttributes` | 3 | **0** | HTML-only |

> The POML guessed the seams as *"block projection, run projection, atom identification, table
> projection, numbering, part traversal"*. Numbering and atom identification survive that guess; the rest
> does not. Block/run/table projection each exist **twice** — once per pipeline — so cutting along them
> would split each of the two pipelines in half and leave both halves coupled to the other pipeline's
> half. The dominant seam is the pipeline boundary, which the POML did not name.

## 4. Blast radius — the constraint that shapes the mechanism

The numbering types are `internal` **nested** types of a public class, so moving them **renames** them.
They are referenced from outside the file:

- **Production**: `ComposeShadowPatchEngine.cs` — `ComposeDocxProjectionBuilder.NumberingModel` (:231,
  :1080) and `.BuildNumberingModel(...)` (:1089, :1094). Live code (074 closed that engine as
  DO-NOT-DELETE), so this is a real, not incidental, dependency.
- **Tests**: ~15 call sites across `ComposeDocxProjectionBuilderTests.cs` and
  `ComposeHeadingListApplierSeamTests.cs`, including `new ComposeDocxProjectionBuilder.NumberingComputationEngine(model)`.
- **Doc comments**: many `<see cref>` / prose references, server *and* client TS.

Updating a call site's *name* is not weakening a test — every assertion stays byte-identical, which is
what ADR-038's "existing tests pass unchanged" protects.

**Mechanism: top-level `internal` collaborators — NOT a partial class.** A partial-class split would cost
zero call-site churn, which is exactly why it is tempting and exactly why it is wrong here: it reduces
*file* size while leaving every member reachable from every other, so it reduces no coupling and
separates no responsibility. `COMPONENT-COMPLEXITY.md` names that ("arbitrary partial-class slicing …
splitting to satisfy a number") as the anti-pattern this standard replaced the ratchet to stop. Since the
LOC gate is gone, a mechanism whose only benefit is a smaller number has no remaining justification.

**No new DI registration** (ADR-010): the collaborators are constructed internally / are static; the
public `ComposeDocxProjectionBuilder` keeps both entry points as its façade, so its DI registration and
public surface are unchanged. Verified by an empty `git diff` on `Program.cs` + `Infrastructure/DI/`.

## 5. Extraction order — lowest risk first, oracle after EACH

Per POML step 3, the equivalence comparison runs after **each** extraction, not once at the end. Task 070
learned the corresponding lesson from the other direction: a survivor under a narrow filter died on the
full suite, so **the full Compose suite is the escalation, not the filtered one**.

1. **Numbering** — self-contained, already has its own nested types; the only cluster with a production
   consumer outside this file, so it is done first while the file is otherwise untouched.
2. **Offset-addressing table** — three static members, no state.
3. **Shared OOXML primitives** — static; unblocks 4 and 5 being independent.
4. **Content-model projection** — stateless (no `_mint`), so it moves whole.
5. **HTML projection** — carries `MintUnique` + `BuildContext`; done last because it is the only cluster
   with instance state.

After 1–3 the remaining file is the two pipelines plus the façade; after 4–5 it is the façade.

## 6. Defects found while decomposing

Behaviour-preserving only. Anything found here is **recorded against its owning task, never fixed inside
the restructure** — the POML makes that an explicit NEGATIVE acceptance criterion, and a silent fix would
also destroy the equivalence proof by making a real output difference indistinguishable from a
refactoring slip.

| # | Finding | Owning task | Status |
|---|---|---|---|
| _(none yet)_ | | | |
