# R5 No-Regression + Publish-Size Hardening Report (task 041)

> **Generated**: 2026-07-30 · Branch `work/spaarkeai-compose-r5` @ `d5bad935c` (post 022/030/031/032/033/040).
> **Verdict**: ✅ **PASS — deploy gate cleared** (neither hard-stop trigger fired). Task 042 may proceed
> (deploy still HOLDS for operator coordination, per project decision — this gate is the technical clearance).

## Gate results (ordered, prescriptive)

| # | Gate | Baseline | Result | Verdict |
|---|------|----------|--------|---------|
| 1 | **Corpus byte-diff harness (NFR-01)** | R4 24/24 | **24/24** (`ComposeShadowPatchEngineByteDiffSeamTests`) | ✅ no regression |
| 2 | **Full Compose seam + unit suite** | 739 (task 001) | **821/821** (0 failed) | ✅ green (+82 across R5) |
| 3 | **Full BFF test suite (cross-cutting)** | — | **9319 passed / 0 failed / 101 skipped** | ✅ green (5 pre-existing Communication fails resolved 2026-07-30) |
| 4 | **ADR gates (Tier-1 + ADR-049)** | 3 pre-existing ArchTest fails | **3 pre-existing** (ADR-007, ADR-010 ×2); **Tier-1 no-AI PASSES** | ✅ zero new |
| 5 | **BFF publish size (NFR-04)** | ~46.11–46.75 MB compressed | **48.13 MB compressed (incl PDBs)** | ✅ ≤60 ceiling |
| 6 | **No new runtime package (NFR-03)** | — | **zero** new NuGet/npm dep; no `@tiptap-pro/*` | ✅ confirmed |

## Detail

### 1. Byte-diff (NFR-01) — 24/24
`ComposeShadowPatchEngineByteDiffSeamTests` runs every fixture under `tests/fixtures/compose-corpus/`:
no-op Apply returns byte-identical; a real interior edit leaves every untouched package part + document.xml
subtree byte-identical. **24/24** — the R4 byte-surgical guarantee holds across all R5 editing-completeness
work (G1–G5, G7–G12 engine appliers, op catalog, save/load, renderer).

### 2. Compose suite — 821/821
Includes the R5 additions: G7 transient-key dedup (3 seam), G8 webhook receiver (4 seam), G5 hyperlinks
(5 byte-author), G10 refresh-profile (2 seam), G11/G3/G4/G12 appliers, clean-apply, origin routing, +
the R4.5 numbering/citation/projection seams (non-regression). 0 failed.

### 3. Full BFF suite — now 0 failed (the 5 pre-existing Communication fails were resolved)
Initial run: `Failed: 5, Passed: 9314` — all 5 in `Services.Communication.*`
(`CommunicationThreadReadServiceTests`, `CommunicationByRegardingReadTests`,
`CommunicationFilteredQueryTests` — sender-identity/read projection), proven unrelated to R5 (zero
Communication files in the R5 session diff).

**Resolved 2026-07-30 (operator-requested, pre-deploy)**: root cause was **stale tests**, not a
production bug. The read service was deliberately changed (messaging-r3, 2026-07-22) to read the sender
display name from the `_sprk_sentby_value@OData.Community.Display.V1.FormattedValue` lookup annotation
instead of the denormalized `sprk_sentbyname` column (broken in the env — `IsValidODataAttribute=false`,
400s the whole read). The three test `MessageRow` helpers still populated the old `sprk_sentbyname` key,
and one `$select` assertion still expected it. Fix (TEST-ONLY — production untouched): the helpers now set
the `_sprk_sentby_value@…FormattedValue` annotation, and the assertion now asserts `sprk_sentbyname` is NOT
selected. Re-run: **9319 passed / 0 failed / 101 skipped**.

### 4. ADR gates
- **ArchTests**: 3 failures — ADR-007 (Graph isolation), ADR-010 (concrete services), ADR-010 (Options) —
  all **proven pre-existing** (identical on the clean tree across tasks 021/022/030/032/033/040). Zero new.
- **ADR-013 Tier-1 NetArchTest**: **PASSES** — no AI-internal type in `Services/Compose/` (the hyperlink
  renderer/engine + the profile-trigger path use only `byte[]`/OOXML + the ADR-013-safe `IDocumentProfileAi`
  facade).
- **ADR-049 invariants**: byte-diff 24/24 confirms I-1/I-2/I-4 (server-authoritative, untouched subtrees
  byte-identical); the write-path text-search audit (`ComposeWritePathTextSearchAuditTests`, in the 821)
  confirms I-7. Two-byte-author split (renderer clean / engine tracked) preserved (G5 respected it).
- **Per-task Step 9.5**: code-review + adr-check applied + recorded in each `notes/task-0NN-deviations.md`.

### 5. Publish size (NFR-04)
`dotnet publish -c Release` → compressed (Compress-Archive) = **48.13 MB incl PDBs**.
- Delta vs the ~46.11 MB spec baseline: **+2.02 MB** across ALL of R5 (G7 dedup, G8 seam, G5 hyperlinks,
  G10 profile — pure C# branches + one Dataverse column read/write + a few DTOs; no new package).
- Under every threshold: < +5 MB (no single-task justification needed), < 55 MB (no architecture review),
  < 60 MB (no hard stop). **PDB convention: measured INCLUDING PDBs** (consistent with tasks 021/030/033/040).

### 6. No new runtime package (NFR-03)
Zero NuGet added (all C# used existing `DocumentFormat.OpenXml` + BCL). Zero npm runtime dep added (client
used existing Fluent v9 + `@tiptap/*` MIT base + `@tiptap/extension-link` already present). No `@tiptap-pro/*`.
The only external addition across R5 is the operator-created Dataverse column `sprk_composetransientkey`
(schema, not a package).

## Escalation triggers — neither fired
- Byte-diff < 24/24 → **NO** (24/24).
- Publish ≥ 55 MB (arch review) / ≥ 60 MB (hard stop) → **NO** (48.13 MB).

**Deploy gate: CLEARED.** (Deploy execution — task 042 — HOLDS for operator coordination on the shared
`sprk_spaarkeai` + `spaarke-bff-dev`, last-deploy-wins.)
