# Test diet report — spaarkeai-compose-fidelity-r4.5

**Run date**: 2026-07-28
**Branch**: `work/spaarkeai-compose-fidelity-r4.5` (merged to master `60780e86c`)
**Scope**: tests touched between `f1440ed1a` (pre-R4.5) and HEAD
**Classifier**: ADR-038 §7 build-vs-maintain (17 bans B1–B17)

## Summary

| Class | Count | Action |
|---|---|---|
| MAINTAIN (KEEP at canonical path) | 7 C# + 8 R4.5 client | confirmed — no action |
| SCAFFOLDING (DELETE candidate) | **0** | — |
| AMBIGUOUS (reviewer judgment) | **0** | — |
| PATH-VIOLATION (wrong KEEP path) | 0 (1 note) | see note |
| Out-of-scope (master-merge, not R4.5) | 3 | excluded |

**Net result: no deletions, no moves.** R4.5 authored tests against the ADR-038 integration-heavy pyramid throughout (seam / KEEP-path, real `.docx` fixtures, zero banned mocks) — verified per-task during execution and confirmed here.

## MAINTAIN — confirmed (no action)

### C# — seam tests (KEEP path: `tests/integration/seam/**`, ADR-038 vertical-slice-seam category E-40)
| File | Why MAINTAIN |
|---|---|
| `ComposeReadFidelityHarnessSeamTests.cs` | text-exactness (8/8 char-exact) + numbering-exactness golden theory (24 cases == Word) over real corpus docs — the NFR-01/NFR-02 acceptance gate. Behavioral, fails on real regression. |
| `ComposeUploadProjectionSeamTests.cs` | drives real `POST /api/compose/upload`; proves byte-identical projection to Load (one-reader) + fail-closed. |
| `ComposeProjectSeamTests.cs` | real `/api/compose/project`; proves statelessness (zero SPE/Dataverse invocations) + byte-identical to Load + 400/fail-closed. |
| `ComposeReferenceMapSessionLedgerSeamTests.cs` | reference-map survives real session reload + edit (unchanged paraIds keep numbers). |
| `ComposeNumberingRoundTripSeamTests.cs` | real `ComposeDocumentRenderer` author → real WS-3 read agreement; **caught DEF-03** (numId counter bug) — a genuine regression test. |
| `ComposeCitationResolverSeamTests.cs` | single/sub-item/range citation resolution over the corpus + negative cases. |

No banned constructs: no `Mock<HttpMessageHandler>` (B1), no DI-registration assertions (B3), no ctor-null tests (B4), no mirror/pass-through/coverage-filler.

### C# — unit (behavioral)
| File | Why MAINTAIN | Path note |
|---|---|---|
| `tests/unit/Sprk.Bff.Api.Tests/Services/Compose/ComposeDocxProjectionBuilderTests.cs` | golden numbering-formatter tests (decimal/letter/roman/legal/multi-level, z→aa, interrupted-no-restart, determinism) + FR-09 construct audit (alignment/ordered-list/w:sym/w:cr) — pure-domain behavioral over real in-memory `.docx`. | Lives at the **established R4 Compose unit-test location**, not `tests/unit/domain/**`. Follows existing convention — **no `git mv` recommended** (moving one project's file out of the established Compose suite would fragment it). Flag for the broader test-architecture sweep, not this project. |

### Client (co-located, behavioral) — R4.5-authored
`ndaClauseLocation.test.ts`, `composeNumberAtomExtension.test.ts`, `ComposeEditor.projection.test.tsx`, `ComposeEditor.numberAtom.test.tsx`, `ComposeEditor.indentAndWhitespace.test.tsx`, `ComposeWorkspace.browse.test.tsx`, `ComposeWorkspace.upload.test.tsx` — render/regression assertions (projection-mount, number-atom non-editable, indentation, browse/upload one-reader guards). Outside the C# 17-ban formal scope (jest, co-located per the client convention); all behavioral — KEEP.

### Client (repaired pre-existing) — modified, not new scaffolding
`ComposeEditor.advisoryComments.test.tsx`, `ComposeEditor.dirtyOnMount.test.tsx`, `ComposeEditor.paneToggleCrash.test.tsx`, `ComposeEditor.referenceOnly.test.tsx`, `ComposeWorkspace.redline-from-ledger.test.tsx`, `ComposeAiToolbar.test.tsx` — pre-existing R4 tests **adapted** by task 013 (mammoth-mount → `projection` prop). Existing behavioral tests kept working; not new surface. KEEP.

## SCAFFOLDING — DELETE candidates

**None.** No R4.5-authored test matches any of B1–B17.

## AMBIGUOUS — reviewer judgment

**None.**

## Out of scope (excluded — arrived via the master merge, not R4.5)
- `src/client/shared/Spaarke.UI.Components/src/components/ThreePaneLayout/__tests__/ThreePaneLayout.statePreserved.test.tsx`
- `src/solutions/SpaarkeAi/src/components/conversation/__tests__/NdaReviewProgressModal.test.tsx`
- `src/solutions/SpaarkeAi/src/components/workspace/__tests__/WorkspacePane.compose-multi-tab.test.tsx`

These are another project's tests folded in when master (18 commits) merged into the R4.5 branch — not R4.5's to reconcile.

## Count delta
- Tests added/modified by R4.5: 7 C# files + 13 client files (8 new/authored, 5 repaired).
- Classified MAINTAIN: all. SCAFFOLDING: 0. AMBIGUOUS: 0.
- **Net post-diet expected count: unchanged (0 deletions).**

## Industry citation
ADR-038 §7 (Beck "delete the scaffolding"; Feathers characterization-vs-behavior; integration-heavy pyramid). No scaffolding to delete — R4.5 built maintain-class tests by construction (seam/KEEP-path, golden-value, real fixtures).
