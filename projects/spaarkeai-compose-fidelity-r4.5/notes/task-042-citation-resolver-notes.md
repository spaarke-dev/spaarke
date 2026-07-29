# Task 042 — WS-4: citation resolver (FR-18)

> Written by the task 042 sub-agent execution. Sub-agent write boundary: this file (under
> `projects/spaarkeai-compose-fidelity-r4.5/notes/`) is in-bounds; `TASK-INDEX.md` / `current-task.md` are
> owned by the main session and NOT touched here. No git commit/push performed.

## Summary

Delivers FR-18 — the WS-4 CITATION RESOLVER, the flagship read/analysis output of R4.5: a human legal
citation string ↔ the exact paragraph `w14:paraId`(s), for all three reference shapes the project's locked
WS-4 decision requires. Built ON TOP of the 040/041 reference map (`ParaIdMapEntry.ListPath` /
`ComputedNumber`) — it does NOT recompute numbering and does NOT duplicate the WS-3 engine. Pure static
utility in `Services/Compose/`; ADR-013 Tier-1 facade guard stays green (no AI-internal type reaches
`Services/Compose`).

## CONTRACT DECISION (spec Unresolved Question — "Citation-model API shape") — DOCUMENTED DEFAULT, awaiting human confirm

The spec flagged the consumer contract as UNRESOLVED (resolver vs flat map vs both). Per the executing
operator's explicit directive, the escalation was pre-resolved to a concrete, testable DEFAULT (not a stall):

**Adopted: BUILD BOTH.** The flat per-paragraph reference map already exists (040 projection payload + 041
session ledger). Task 042 adds a **pure resolver over it** — `CitationResolver`, a stateless static class:

- **Input**: a citation STRING + the reference map the tool already holds. TWO overloads, so both stores are
  first-class: `IReadOnlyList<ParaIdMapEntry>` (projection payload) and `IReadOnlyList<ParaReferenceMapEntry>`
  (session ledger). The tool calls it against whichever it has in hand — no rebuild.
- **Output**: `CitationResolution { string Query; CitationShape Shape; IReadOnlyList<CitationTarget> Matches }`
  where `CitationTarget = (ParaId, Index, ComputedNumber, ListPath)`. Convenience: `IsResolved` (bool) and
  `ParaIds` (the matched `paraId`(s), document order). An unresolvable citation returns **empty matches**
  (`IsResolved == false`) — an explicit not-found, NEVER a fabricated `paraId`, and the method NEVER throws.
- **NO new endpoint, NO DI injection, NO PublicContracts interface added.** The resolver takes the map as a
  parameter (not injected), so there is zero DI coupling and ADR-013 is satisfied structurally: WS-4 exposes
  DATA (the already-public `ComposeDocxProjection.ParaIdMap` / `ChatSession.ReferenceMap`) and the resolver is
  a pure function over that data. This is the §11 "extend existing over new service" choice — a new endpoint
  or facade interface would be unjustified surface for a stateless, pure string→id map.

**For human review**: confirm this pure-static-over-existing-map contract is what the analysis/citation tool
should consume. If the tool instead needs the resolver behind a `Services/Ai/PublicContracts/` interface (e.g.
for mockability at the AI-layer boundary), that is a thin wrapper to add later — the resolution logic itself is
unchanged. The default was chosen because it is minimal, ADR-013-clean, and immediately consumable.

## How each citation shape is parsed + matched

Parsing (`CitationParser`, internal): normalize whitespace → strip tolerated leading labels
(`Section(s)`/`Article(s)`/`Clause(s)`/`Paragraph(s)`/`Para`, `§`/`§§`, any case + spacing) → classify.

| Shape | Example | Parse | Match against the map |
|---|---|---|---|
| **Single** | `"Section 4.2"`, `"4.2"`, `"§ 4.2"` | dotted-decimal → ordinal path `[4,2]` | citable entry whose `ListPath == [4,2]` |
| **Sub-item** | `"4.2(b)(iii)"` | base `[4,2]` + `(b)`→2 (2nd lower-letter) + `(iii)`→3 (3rd lower-roman) = `[4,2,2,3]` | citable entry whose `ListPath == [4,2,2,3]` |
| **Range** | `"Sections 4–7"`, `"4-7"` | endpoints `4`,`7` (hyphen / en-dash / em-dash) | every citable entry whose **top-level** ordinal `ListPath[0] ∈ [4..7]`, ordered by document index |

- **Single + sub-item are one parse**: a dotted-decimal base followed by zero-or-more parenthesized sub-item
  tokens. `"1.1.1"` (a decimal sub-item at depth 3) resolves through the exact same path parse → `[1,1,1]`.
- **Sub-item token decode**: digits → the integer; a multi-char valid roman numeral (`ii`,`iii`,`iv`) → roman
  (validated by re-encoding, so `iiii`/`im` reject); a single alpha char → lower-letter ordinal (`a`=1..`z`=26,
  bijective base-26 for multi-char) PLUS, for a char that is ALSO a roman digit (`i,v,x,l,c,d,m`), the roman
  value as an ALTERNATE candidate — the resolver keeps whichever actually matches a paragraph (ties favor the
  letter reading). This makes the parent's `b`/`iii` example fully deterministic (`b`∉roman→2; `iii` multi-char
  roman→3) while staying robust for genuinely ambiguous single tokens like `(i)`.
- **Citability gate**: a paragraph is reachable by a numeric citation ONLY when its `ComputedNumber` begins
  with an ASCII digit. This honors the task-040 note — a bullet carries a non-numeric `ComputedNumber` (glyph)
  but a numeric `ListPath`; it must NOT be reachable by a section/range citation. Unnumbered paragraphs
  (null `ComputedNumber`) are likewise excluded.
- **Bidirectional (bonus)**: `ResolveCitation(paraId, map)` returns the canonical citation number (normalized
  `ComputedNumber`, trailing dots trimmed, e.g. `"4.2." → "4.2"`) or `null` for an unknown/uncitable paraId.
- **Range interpretation** = "all paragraphs whose top-level ordinal is in [lo..hi]" (parent directive) — so
  `"Sections 1-2"` on a multi-level doc returns the top-level sections AND their sub-items, in document order.
  Descending (`7–4`) normalizes to the ascending span.

## Placement Justification (root CLAUDE.md §10/§11, `.claude/constraints/bff-extensions.md`)

- **Existing**: the AI layer today anchors only on the opaque `paraId` + raw text + doc-order index
  (`ComposeService.cs`; `AnnotationReanchorService` reanchor by Levenshtein + position). Grep of
  `Services/Compose/` + `Services/Ai/` confirms NO human-citation resolution exists anywhere.
- **Extension**: No — the anchor layer resolves opaque `paraId` ↔ text/position, not human-citation ↔ `paraId`.
  A new resolver is genuinely required. It EXTENDS the 040/041 reference map (reads `ListPath`/`ComputedNumber`;
  recomputes nothing) rather than adding a parallel numbering path — one resolver capability, not a family of
  overlapping handlers (§11).
- **Cost-of-doing-nothing**: without it the analysis/citation tool cannot cite a section by number nor resolve
  "Section 4.2" to the exact clause — the single most valuable R4.5 output left undelivered (success criterion
  #4 Referenceable).
- **`Services/Compose/` stays pure** (ADR-007/013): `byte[]`/map-in, pure-record-out — no `Microsoft.Graph`, no
  `IOpenAiClient`, no router, no node executor. The one cross-namespace type it touches
  (`Models.Ai.Chat.ParaReferenceMapEntry`, for the session-ledger overload) is a dependency direction task 041
  already established and the ADR-013 Compose facade ArchTest already permits. Verified: `ADR013_ComposeFacadeTests` GREEN.
- **No new endpoint / DI registration / package** — nothing to register; the resolver is a static utility.
- **`/conflict-check`** must be run by the MAIN SESSION before the PR (subagent does not commit/PR):
  `Services/Compose/` overlaps `spaarkeai-compose-r1/r2/r3/r4` + `spaarke-ai-architecture-redesign-r2`. This
  task's only new file is `CitationResolver.cs` (net-new; no edit to shared files), plus one new seam test file.

## Tests

`tests/integration/seam/Compose/ComposeCitationResolverSeamTests.cs` (KEEP path `tests/integration/seam/**`,
ADR-038; no `Mock<HttpMessageHandler>`/DI-registration/ctor-null shapes). **32 tests, all green.** Two evidence
bases, both driving the REAL `CitationResolver`:

1. **REAL CORPUS end-to-end** (source bytes → `ComposeDocxProjectionBuilder.Build()` → resolver → paraId):
   - Single label "Section 4.2" (+ `§`, `Article`, whitespace, bare) → the `[4,2]` Heading2 in
     `heading-style-numbering.docx` (the FR-12 clause); "Section 4" → the `[4]` Heading1.
   - Decimal sub-item depth "1.1.1" → the `[1,1,1]` "History" clause in `multilevel-1-1-1.docx` (the deep
     clause, not the top-level "1").
   - Range "Sections 4–7"/"4-7"/"§§ 4-7"/"7–4" → clauses 4,5,6 (ordinals 12,13,14) of
     `nda-interrupted-clauses.docx`, document order; "Sections 1-2" on the multilevel doc → the 7 numbered
     items incl. sub-items.
   - Negatives: "Section 99" / "Sections 20-30" / null / "" / "Section abc" / "4.2(" / "4.2 xyz" → explicit
     not-found, never a throw. Bullet paragraph unreachable by numeric citation.
   - Reverse: paraId → "4.2" / "1.1.1"; round-trips with forward resolution; un-numbered/unknown paraId → null.
2. **LETTER/ROMAN sub-item "4.2(b)(iii)" → `[4,2,2,3]`** — proven against a small in-memory `ParaIdMap` whose
   `ListPath` chains are the exact structured form such a paragraph carries, because the corpus manifest
   (§1.5 + §2 placeholders) documents that NO corpus fixture has lower-letter/lower-roman sub-item numbering
   (deepest real chain is decimal `[1,1,1]`); fabricating one is forbidden by root CLAUDE.md §11. The map
   carries a decoy `[4,2,2,4]` ("(iv)") to prove the parse lands on EXACTLY `[4,2,2,3]`. Constructing
   `ParaIdMapEntry` input to a pure function is the resolver's real data contract — not a mocked collaborator.

**Corpus-fixture gap flagged for the owner** (not a defect): a real lower-letter/lower-roman sub-item fixture
(e.g. `4.2(b)(iii)`) would let the seam prove the letter/roman path over REAL bytes; today the letter/roman
DECODE is proven at unit granularity + against structured chains. Candidate for a §2 owner-supplied intake row.

## Verification

- `dotnet build src/server/api/Sprk.Bff.Api/` (Debug + Release) — **0 errors** (23 pre-existing warnings,
  unchanged set). No `.csproj` change (git diff empty for `*.csproj`).
- `dotnet test --filter "FullyQualifiedName~CitationResolver"` — **32 passed / 0 skipped / 0 failed**.
- `dotnet test --filter "FullyQualifiedName~Compose"` — **739 passed / 0 skipped / 0 failed** (041 baseline
  707; +32 = this task's new seam tests).
- `dotnet test --filter "FullyQualifiedName~TextExactness|FullyQualifiedName~NumberingExactness"` —
  **32 passed**, unchanged — numbering computation untouched.
- `Spaarke.ArchTests` `ADR013_ComposeFacadeTests` — **passed** (Services/Compose stays free of AI internals).
- Publish size (BFF Hygiene §10): compressed **47.53 MB** (`Compress-Archive`, same method as 030/031/032/040/041)
  vs 041's **47.52 MB** → **delta +0.01 MB** (rounding; effectively 0). No new package — pure additive static
  class over the already-referenced numbering map. Well under the ≤60 MB ceiling and the ~49.63 MB baseline.

## Files changed

- `src/server/api/Sprk.Bff.Api/Services/Compose/CitationResolver.cs` — NEW. The resolver
  (`CitationResolver` static class + `CitationResolution`/`CitationTarget`/`CitationShape` public types +
  internal `CitationParser`). Pure; two source-map overloads (`ParaIdMapEntry` + `ParaReferenceMapEntry`);
  forward (citation→paraId(s)) + reverse (paraId→citation) resolution.
- `tests/integration/seam/Compose/ComposeCitationResolverSeamTests.cs` — NEW seam file, 32 tests
  (KEEP path, no banned test shapes).

## Escalation

Did NOT hard-block. The consumer-contract Unresolved Question was pre-resolved to a documented, testable
DEFAULT per the operator directive (see CONTRACT DECISION above) — surfaced here for human confirmation at
review, not silently improvised.
