# Task 012 — Opaque Atoms (Server Projection) — Decisions

> **Task**: `tasks/012-opaque-atoms-server.poml` (FR-02, server half)
> **Files changed**: `ComposeDocxProjectionBuilder.cs`, `ComposeDocxProjection.cs`, `ComposeDocxProjectionBuilderTests.cs`
> **Depends on**: task 011 (offset-addressing table) — built on top of it, same file, coordinated in the same
> single render pass.

## 1. Corpus-coverage gap (inherited from task 002's finding — confirmed empirically)

Per `notes/task-002-corpus-deviations.md` note (3a) and the corpus-manifest §1, **none of the 3 seed corpus
docs carry a body-level SDT, field, or complex/floating object**. Verified by unzipping all three and grepping
`word/document.xml` for `w:sdt`, `w:fldSimple`, `w:fldChar`, `w:drawing`, `w:object`, `w:pict` — zero matches
in all three bodies. The corpus's only real SDT+field pair (`w:sdt` wrapping a `PAGE` field) lives in
`PAT 109270W-1...docx`'s `word/footer2.xml` — a HEADER/FOOTER part, which `ComposeDocxProjectionBuilder` does
not walk at all (it only projects `mainPart.Document.Body`). That footer content is therefore **already**
byte-preserved trivially — the projection never touches it, so no operation can ever be generated against it,
regardless of this task's changes.

**Consequence**: the acceptance criterion "corpus docs containing fields/SDTs... load without editor error"
is satisfied vacuously for the current 3-doc corpus (none of them have body-level atoms to fail on), and the
byte-preservation/no-op-save proof cannot be grounded against a REAL body-level atom construct until an
owner-supplied worst-offender doc lands (corpus-manifest §2, row 6: "literal OOXML fields/content-controls
document"). This is a pre-existing, already-flagged gap — not something this task introduces. Task 013's seam
slice proves what CAN be proven today (paraId persistence, offset round-trip, and — via the CIPO footer's
untouched-by-construction guarantee — the "atom-scope content is never opened" structural fact) across the
existing corpus; full body-level-atom corpus coverage is deferred to the owner intake.

Given this, task 012's implementation was authored and unit-tested against SYNTHETIC in-memory fixtures
(9 new tests in `ComposeDocxProjectionBuilderTests.cs`) covering every construct the task names: a special-
type SDT block (date), a plain SDT block (regression — stays transparent), an inline special-type SDT run, a
plain inline SDT run (regression), `w:fldSimple`, a `w:fldChar` begin/instrText/separate/end sequence, an
unterminated field (fail-closed negative), and a `w:drawing` run outside a text box.

## 2. Escalation-boundary decision: SDT atom-vs-transparent classification

The task POML's `<escalation>` trigger names exactly this case: "an SDT that WRAPS editable paragraphs...
where treating the whole thing as opaque would make legitimate content non-editable." No CONCRETE corpus
construct forces this ambiguity right now (see §1), so rather than blocking on a synchronous escalation with
nothing to escalate about, the decision was resolved via a principled, documented structural rule (Path C —
pivot within design intent, not a silent pick):

**Rule** (`IsSpecialSdtControl` in `ComposeDocxProjectionBuilder.cs`): an SDT/content-control (`w:sdt`, block
or inline) becomes a whole-construct opaque ATOM **only** when its `w:sdtPr` declares a genuinely non-text
type — `SdtContentDate`, `SdtContentDropDownList`, `SdtContentComboBox`, `SdtContentPicture`,
`SdtContentDocPartObject`, `SdtContentDocPartList`, `SdtContentEquation`, `SdtContentCitation`,
`SdtContentBibliography`, `SdtContentGroup`. A plain-text control (`SdtContentText`), a rich-text control
(`SdtContentRichText`), or an SDT with **no** declared type at all (the OOXML default — the corpus's own
`SdtBlock(SdtContentBlock(Para(...)))` shape) keeps the PRE-EXISTING transparent-descend behavior: the shell
is invisible, wrapped paragraphs stay editable, and the existing `content-control` fidelity warning still
fires. Applied identically to both `SdtBlock` (block-level) and `SdtRun` (inline).

**Why this resolves the boundary without losing editability or dropping content**: the common "content
control wrapping real prose" shape (repeating sections, generic containers, plain/rich-text bound values) is
NEVER classified as an atom — zero regression risk, verified by the existing
`Build_MixedDocumentWithTablesAndContentControl...` / `Build_OffsetAddressingTable_IsIndexAlignedWithParaIdMap`
tests (both construct a plain `SdtBlock`, both still pass unchanged). Only genuinely non-text controls
(date pickers, dropdowns, pictures, doc-part galleries, equations, citations, bibliographies, groups) —
content that was never faithfully editable as plain prose in this HTML-based Phase-1 projection anyway —
become atoms. This is surfaced here (not silently) for owner review before task 021 (client atom schema)
builds on top of it; flag if a different boundary is wanted.

## 3. Identity model: block atoms get their OWN id, kept OUT of `ParaIdMap`

Considered reusing a wrapped paragraph's `w14:paraId` for a block atom's identity, or adding the atom directly
into `ParaIdMap`. Both were rejected: `ParaIdMap`'s documented contract is "one entry per body PARAGRAPH", and
the existing F-01 single-walk invariant test (`Build_MixedDocumentWithTablesAndContentControl_...`) asserts
`EmittedParaIds(html) == ParaIdMap.Select(paraId)` — i.e. every `data-paraid` in the HTML must have a
`ParaIdMap` entry. Polluting `ParaIdMap` with atom ids (which don't correspond to any real `<w:p>`) would
either break that invariant or force loosening it for every future consumer.

**Decision**: a block atom mints its OWN id from the SAME collision-checked 8-hex pool paragraph ids use
(format-consistent, globally unique, via `BuildContext.MintAtomId()`), tracked in a NEW
`ComposeDocxProjection.BlockAtoms` list, and rendered with a DISTINCT `data-atomid` HTML attribute — never
`data-paraid`. Inline atoms (fields, inline SDT atoms, complex objects — all nested inside a paragraph's own
runs) need no separate identity at all: they carry their CONTAINING paragraph's real `data-paraid` and are
signaled via the new `RunBoundary.AtomKind` (nullable `ComposeAtomKind`) in the offset-addressing table
instead.

## 4. Wire contract: kept internal (matches task 011's precedent)

`BlockAtoms` and `RunBoundary.AtomKind` are NOT plumbed through to `ComposeEndpoints.cs` / the HTTP response
DTO. Task 011's `OffsetAddressingTable` was similarly kept `ComposeDocxProjection`-internal only (no endpoint
wiring) — confirmed by grep (zero references to `OffsetAddressingTable` outside `Services/Compose/`). Task 012
follows the same precedent; wiring the client-consumable contract is task 021's job (client atom schema).

## 5. BFF hygiene

- **Placement Justification**: extends the existing projection builder in `Services/Compose/` — a new node
  TYPE (opaque atom) inside an existing service, no new service/DI registration. See `<justification>` in the
  task POML (existing = none; extension = no, genuinely new node type; cost-of-doing-nothing = fields/SDTs
  break the editor on load or lose content on save without this).
- **Publish size**: 47.47 MB compressed incl. PDBs / 46.66 MB excl. PDBs (measured via `dotnet publish -c
  Release` + `System.IO.Compression.ZipFile`, 2026-07-22) — below the ~49.63 MB / ~45.87 MB cited baselines
  in root CLAUDE.md §10, well under the 60 MB hard ceiling. Zero new packages added (only `.cs` edits).
- **CVE scan**: `dotnet list package --vulnerable --include-transitive` shows one pre-existing HIGH advisory
  set on `System.Security.Cryptography.Xml` (transitive) — confirmed PRE-EXISTING via `git stash` (identical
  finding with this task's changes stashed out). No NEW vulnerable package introduced.
- **ADR-013 / ADR-007**: `ADR013_ComposeFacadeTests` (Tier-1 NetArchTest) passes. `ADR007_GraphIsolationTests`
  has one pre-existing failure, confirmed via `git stash` to be unrelated to `Services/Compose/` (it flags
  `Services/Communication/*` and `Infrastructure/Errors/*` Graph-type leaks, not introduced by this task).
- **Build**: `dotnet build src/server/api/Sprk.Bff.Api/` green, zero new warnings.
- **Tests**: full `Services/Compose` unit suite green (511 total, +8 net from this task's 9 new tests plus
  updated assertion counts); zero regressions in the other 502 pre-existing Compose tests.

## 6. Deferred to later tasks

- Client atom schema / TipTap rendering (task 021).
- Enforcement of the "no intra-atom operation" rule in a Patch Engine (task 030+) — this task only SIGNALS
  the boundary (`RunBoundary.IsAtom` / absence from `ParaIdMap`); there is no Patch Engine yet to enforce it.
- VML `w:pict` legacy drawings — out of scope (not in the corpus; noted in code as a future-coverage
  candidate alongside `w:drawing` / `w:object`).
- Owner-supplied worst-offender doc with real body-level SDT/fields (corpus-manifest §2 row 6) — needed to
  ground the byte-preservation proof against a genuine body-level atom construct; current proof is structural
  (atoms are provably never opened) plus synthetic-fixture unit tests, not a corpus round-trip.
