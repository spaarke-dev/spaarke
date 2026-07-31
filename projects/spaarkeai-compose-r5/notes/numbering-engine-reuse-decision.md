# Numbering Engine Reuse Decision — G3 (task 005)

> **Task**: 005 (Phase 0 gate). **Gates**: task 011 (G3 heading/list applier).
> **Decision**: **(B) Reference in place — and NO visibility change is actually required.**
> **Method**: static code analysis only (no `dotnet build`/`test`/`publish` run — see §6 Verification below for rationale and the deviation this records).

---

## 1. Decision

**G3 (task 011) reuses `NumberingComputationEngine` by referencing it in place via its fully-qualified nested name** — `ComposeDocxProjectionBuilder.NumberingComputationEngine` — from `ComposeShadowPatchEngine.cs`. **Zero lines of `ComposeDocxProjectionBuilder.cs` change.**

This is a stronger form of option (B) than the task framing anticipated: the task's prompt assumed a visibility change (`internal` → `internal`-with-`InternalsVisibleTo`, or similar) would be needed to make the engine reachable. Code inspection shows that assumption does not hold — **the engine is already reachable, today, with no change at all.**

## 2. Why: the reachability premise in the task prompt is inaccurate

`NumberingComputationEngine` is declared:

```csharp
// ComposeDocxProjectionBuilder.cs:1357
internal sealed class NumberingComputationEngine
```

nested inside `public sealed class ComposeDocxProjectionBuilder` (`:65`), in namespace `Sprk.Bff.Api.Services.Compose`.

In C#, a nested type's `internal` accessibility modifier grants access to **the entire containing assembly**, not just the enclosing class — this is standard nested-type visibility, unrelated to `private` nested types (which genuinely would be enclosing-class-only). `internal` is exactly as permissive here as a top-level `internal class` in the same assembly would be; nesting only changes how you spell the name (`ComposeDocxProjectionBuilder.NumberingComputationEngine` instead of a bare name), not who can see it.

`ComposeShadowPatchEngine.cs` (`:88`, `public sealed class ComposeShadowPatchEngine`) lives in the **same namespace** (`Sprk.Bff.Api.Services.Compose`) and the **same project/assembly** (`Sprk.Bff.Api.csproj` — both files sit under `src/server/api/Sprk.Bff.Api/Services/Compose/`). No `InternalsVisibleTo` is needed (that mechanism is for cross-assembly access, e.g. test projects reaching into production internals — not needed for two classes compiled into the same DLL).

**Conclusion**: `ComposeShadowPatchEngine` can call `new ComposeDocxProjectionBuilder.NumberingComputationEngine(model).Compute(numberingRef)` today, using the fully-qualified nested name. The "NOT reachable" framing in the task prompt was based on the class being `internal`, without accounting for same-assembly nested-internal visibility. No `internal`→`public`, no `InternalsVisibleTo` attribute, no accessor shim is required.

## 3. What task 011 actually needs to write (the consumption contract)

Everything `NumberingComputationEngine` needs is exposed as `internal` nested types/statics on `ComposeDocxProjectionBuilder`, all likewise same-assembly-reachable without change:

| Member | Signature | Role |
|---|---|---|
| `ComposeDocxProjectionBuilder.BuildNumberingModel` | `internal static NumberingModel BuildNumberingModel(MainDocumentPart mainPart)` (`:1109`) | Parses `numbering.xml` + style-linked numbering from `styles.xml` into a `NumberingModel`. Pure, static, never throws (fail-open on a malformed numbering/styles part). |
| `ComposeDocxProjectionBuilder.NumberingModel` | `internal sealed class` (`:1066`) | The closed numbering model (`AbstractNumIdByNumId`, `Levels`, `Overrides`, `StyleLinkedNumbering`, `UnresolvedNumStyleLinkAbstractNumIds` + `ResolveLevel`/`ResolveStartOverride`). Immutable value-bag; no coupling to `ComposeDocxProjectionBuilder` instance state. |
| `ComposeDocxProjectionBuilder.ParagraphNumberingRef` | `internal sealed record ParagraphNumberingRef(int NumId, int Ilvl, bool StyleLinked, string? SourceStyleId)` (`:1060`) | One paragraph's resolved `(numId, ilvl)` numbering source. |
| `ComposeDocxProjectionBuilder.ResolveParagraphNumbering` | `internal static ParagraphNumberingRef? ResolveParagraphNumbering(Paragraph p, NumberingModel model)` (`:1294`) | Resolves a paragraph's direct `w:numPr`, else style-linked via `pStyle`. |
| **`ComposeDocxProjectionBuilder.NumberingComputationEngine`** | `internal sealed class` (`:1357`), ctor `NumberingComputationEngine(NumberingModel model)` (`:1381`) | **The engine itself.** Stateful **within one document walk** — carries running `(numId, level)` counters forward so an interrupted numbered run does not reset to 1. Create **one instance per document/apply-session**; call `Compute` once per numbered paragraph, **in document order**. |
| `ComposeDocxProjectionBuilder.NumberingComputationEngine.Compute` | `public NumberingComputationResult? Compute(ParagraphNumberingRef numbering)` (`:1393`) | Advances the counter for `(numbering.NumId, numbering.Ilvl)` and returns the computed label + ordinal chain, or `null` if the `numId` is unresolvable in the model (never fabricates a label). |
| `ComposeDocxProjectionBuilder.NumberingComputationResult` | `internal readonly record struct NumberingComputationResult(string Label, IReadOnlyList<int> ListPath)` (`:1332`) | `Label` = the displayed number (e.g. `"4.2"`); `ListPath` = the raw per-level ordinal chain (e.g. `[4, 2]`) the label's `%n` substitution drew from. |

**Coupling assessment (task step 2)**: the engine and its four supporting types (`NumberingModel`, `ParagraphNumberingRef`, `NumberingLevelDef`, `NumberingLevelOverrideDef`) form a **self-contained, side-effect-free unit**. `NumberingComputationEngine`'s constructor takes only a `NumberingModel` — it does **not** close over `ComposeDocxProjectionBuilder` instance fields (`_mint`, the paraId dictionaries, etc.), does not call back into the outer class, and holds no reference to it. The nesting is an **encapsulation choice** (keeping the read-side numbering machinery visually scoped next to its one caller), not a **structural dependency**. This is exactly the property that makes reference-in-place safe: there is no hidden coupling extraction would need to sever.

### Usage sketch for task 011 (illustrative only — not implemented by this task)

```csharp
// Inside ComposeShadowPatchEngine's setBlockAttr / renumber path, given the PatchSession's mainPart:
var model = ComposeDocxProjectionBuilder.BuildNumberingModel(mainPart);
var engine = new ComposeDocxProjectionBuilder.NumberingComputationEngine(model);

// In document order, for each paragraph needing a recomputed number after the structural edit:
var numberingRef = ComposeDocxProjectionBuilder.ResolveParagraphNumbering(paragraph, model);
if (numberingRef is { } r)
{
    var result = engine.Compute(r); // result?.Label, result?.ListPath
}
```

`ComposeShadowPatchEngine.PatchSession` already carries a `MainDocumentPart _mainPart` (`ComposeShadowPatchEngine.cs:184`/`192`) from the same `WordprocessingDocument` it patches, so `BuildNumberingModel(mainPart)` is a same-document, same-pass call — no second document open, no extra I/O.

**Ordering discipline task 011 must honor** (from the engine's own contract, `:1389`): `Compute` must be called **exactly once per numbered paragraph, in document order**, because the engine is a single running-counter replay, not a stateless pure function. For an edit-time renumber, this most likely means: rebuild the model once per apply-session (or reuse the session's existing walk if one already visits paragraphs in order), then call `Compute` for every numbered paragraph in document order — not just the paragraph(s) touched by the current op — so counters stay correct for paragraphs after the edit point. This ordering requirement is a **consumption contract**, not a coupling problem; extraction would not have relaxed it (the same discipline is already binding on today's sole caller in `Build`'s Pass-1 loop, `:207`).

## 4. Option comparison

| | **(A) Extract to standalone type** | **(B) Reference in place (chosen)** |
|---|---|---|
| Change to `ComposeDocxProjectionBuilder.cs` | Move ~330 lines (engine + `NumberingModel` + `ParagraphNumberingRef` + `NumberingLevelDef`/`NumberingLevelOverrideDef` + `BuildNumberingModel` + `ResolveParagraphNumbering` + helpers) out of the class | **None** |
| Byte-risk to the projection path (I-4 / NFR-01) | Nonzero — even a "pure" move risks a copy/paste slip, a changed `using`, or an accidental behavior nuance in the extraction diff. Must be verified byte-identical via the numbering round-trip seam test (§6) before merge. | **Zero** — no code in the write/projection path changes at all |
| Reachability from `ComposeShadowPatchEngine` | Yes, trivially (would be a top-level or namespace-level internal type) | **Yes, already, today** — no visibility change needed (§2) |
| R5-D4 compliance (reuse, don't fork) | Satisfied | **Satisfied** |
| Effort for task 011 | Extraction PR (file move + reference update in `ComposeDocxProjectionBuilder.cs` + new file) THEN the consumption code in `ComposeShadowPatchEngine.cs` | Consumption code only — call the fully-qualified nested name |
| Encapsulation cost | Improves discoverability (top-level file, findable without knowing it's nested) — a real but soft benefit | Slightly less discoverable (must know to look inside `ComposeDocxProjectionBuilder.cs`) — mitigated by this note + the inline XML-doc already present at `:1314-1322`, which explicitly documents that R5 G3 is the second caller |
| Future 3rd caller | Marginally better positioned | Still fine — nested-internal reuse from N callers in the same assembly is not a C# limitation |

**Net**: (A) buys a small discoverability improvement at a nonzero byte-risk to a projection path that is explicitly under an I-4/NFR-01 byte-identity invariant, in exchange for reachability that (B) already has for free. There is no scenario here where (A) is required to satisfy R5-D4 — the "must extract to reuse" premise in the task prompt does not hold.

## 5. Escalation-trigger check (per task POML `<escalation>`)

> "If a prototype extraction changes ANY projection byte or breaks the numbering round-trip golden, STOP and revert to reference-in-place... If neither option cleanly enables reuse without byte drift, escalate."

Not triggered, and not reached: no extraction was prototyped because reference-in-place cleanly enables reuse **with zero byte-risk by construction** (no lines of `ComposeDocxProjectionBuilder.cs` change under option B). The escalation clause's premise — "if neither option cleanly enables reuse" — does not apply; (B) cleanly enables reuse today.

## 6. Verification / pre-refactor baseline

**Seam test**: [`tests/integration/seam/Compose/ComposeNumberingRoundTripSeamTests.cs`](../../../tests/integration/seam/Compose/ComposeNumberingRoundTripSeamTests.cs) — the ADR-038 vertical-slice seam guarding numbering agreement. It drives the two REAL production components end-to-end: `ComposeDocumentRenderer.SynthesizeDocument` (write-side author) → real `.docx` bytes → `ComposeDocxProjectionBuilder().Build(docx)` (read-side, which internally constructs and drives `NumberingComputationEngine`). No `Mock<HttpMessageHandler>`, no DI-registration test, no ctor-null test. Static inspection confirms **no `[Fact(Skip=...)]` / `[Theory(Skip=...)]` markers** are present in the file — all cases are active.

**This is the correct guard for task 011's edit-time renumber work** — because option (B) makes zero change to `ComposeDocxProjectionBuilder.cs`, this seam test's read-side behavior is unaffected by task 005 (this task) by construction. Its role for task 011 is: (a) pre-existing pass/fail baseline for the read-side numbering computation, unrelated to the reuse mechanism, and (b) the pattern to follow when task 011 adds its own edit-time seam coverage (a companion "edit → renumber → re-project → labels agree" seam test belongs alongside it, per NFR-06 seam-DoD — task 011's concern, not this task's).

**Deviation recorded (per CLAUDE.md §6.5 path-C spirit / task step-mode `directional`)**: the calling session's orchestrator directive for this Wave-0 parallel batch (tasks 001/003/004/005/006 running concurrently) explicitly instructed **no `dotnet build`/`test`/`publish`** for this task, to avoid `obj`/`bin` contention with task 001 (which owns the build in this wave). Task step 6 ("confirm the current numbering round-trip seam test passes") was therefore satisfied by **static verification only**: (1) the test file exists at the KEEP path `tests/integration/seam/Compose/**` (vertical-slice-seam category, ADR-038), (2) it exercises the exact production types this decision concerns (`ComposeDocumentRenderer`, `ComposeDocxProjectionBuilder`, and transitively `NumberingComputationEngine`), (3) no test case carries a `Skip` trait. This is not a substitute for an actual green run — **task 011 (or the wave's build-verify step) MUST run `dotnet test --filter "FullyQualifiedName~ComposeNumberingRoundTripSeamTests"` before relying on this as its pre-refactor/pre-consumption baseline**, since no code changed here to invalidate a prior green run, but no fresh run was performed in this task either.

## 7. R5-D4 / no-fork compliance

Both the decision and the fallback path comply with R5-D4 ("G3 edit-side numbering reuses R4.5's `NumberingComputationEngine` — do NOT re-implement"): option (B) calls the existing engine unmodified; the never-triggered escalation path would also have reverted to (B) rather than green-lighting a fork. **No re-implementation of the numbering algorithm is proposed anywhere in this decision.**

## 8. Placement Justification (CLAUDE.md §10 — BFF decision)

No new component is introduced. The consuming call task 011 will add lives in `ComposeShadowPatchEngine.cs`, already in `Services/Compose/`; the reused engine stays exactly where it is, in `Services/Compose/ComposeDocxProjectionBuilder.cs`. `NumberingComputationEngine` remains pure OOXML computation (`byte[]`/OpenXml-DOM in, label/chain out) — no `IOpenAiClient`/executor/routing type, no `Microsoft.Graph` dependency (ADR-013 Tier-1 / ADR-007 preserved). Stateless-per-call-graph (a fresh `NumberingComputationEngine` instance per document/apply-session, matching the existing `Build()` caller's pattern) is consistent with the Patch Engine's stateless-singleton model (ADR-010) — the engine instance itself is transient/per-walk state owned by the caller, not a DI-registered singleton.

## 9. Hand-off to task 011

- Do **not** extract or move any code in `ComposeDocxProjectionBuilder.cs`.
- Reference `ComposeDocxProjectionBuilder.NumberingComputationEngine`, `.BuildNumberingModel(mainPart)`, `.ResolveParagraphNumbering(paragraph, model)`, `.ParagraphNumberingRef`, `.NumberingComputationResult` by their fully-qualified nested names from `ComposeShadowPatchEngine.cs` (§3 usage sketch).
- Honor the **document-order, once-per-numbered-paragraph** call discipline (§3) — a fresh `NumberingComputationEngine` per apply-session, driven across every numbered paragraph in document order, not just the edited one.
- Run the numbering round-trip seam test **before** starting (baseline) and **after** wiring the edit-time renumber (regression guard), plus author task 011's own edit-time seam coverage per NFR-06.
- No visibility change, no `InternalsVisibleTo`, no new file — the only new code is the call sites inside `ComposeShadowPatchEngine.cs`'s `setBlockAttr` applier.
