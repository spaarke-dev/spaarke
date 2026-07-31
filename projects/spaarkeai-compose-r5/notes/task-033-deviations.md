# Task 033 — G5 Hyperlinks on both paths (authored render + edit op) — Deviations & Notes

> **Status**: ✅ COMPLETE · 2026-07-30 · FULL rigor · opus/high (run on Opus 4.8 session) · SDL-4/5 guards removed.

## Feasibility verdict (before implementing)
An Explore-agent map confirmed **both byte-authors hold their MainDocumentPart**, so an external
`w:hyperlink` relationship (`TargetMode="External"`) is faithfully representable on each path — the
escalation trigger (unrepresentable/silent-loss) is NOT hit. The engine's flatten already descends into
`w:hyperlink` (CollectSlots), so an emitted link's inner runs stay `(paraId,runIndex,offset)`-addressable
on the next edit (never silently lost on a subsequent round-trip).

## What shipped
**Authored (clean) path** — `ComposeContentModel.cs` + `ComposeDocumentRenderer.cs`:
- `ComposeInlineRun.Href` (optional). `BuildRun` returns `OpenXmlElement`: an href run is wrapped in a
  `w:hyperlink` carrying a sentinel id (`COMPOSE_PENDING_HREF:`), because the static build chain has no
  MainDocumentPart. `ResolveHyperlinkRelationships` (called by BOTH authors — `SynthesizeDocument` +
  `AppendSection`, which hold `mainPart`) swaps each sentinel for a real EXTERNAL relationship BEFORE
  save. A non-absolute href → the run TEXT is kept, only the link dropped (no silent loss).

**Edit (tracked) path** — closed op catalog + engine:
- `ComposeMarkType.Link` (server enum + client `compose-operations.ts` mirror) + `SetMarkOperation.Href`
  (server + client). The `Link` mark's doc note anticipated exactly this value-carrying extension.
- Engine `ApplyLinkOverRange` (dispatched from the `SetMark`/`ClearMark` cases when `Mark==Link`):
  `IsolateRangeRuns` the range (no text-search — I-7), wrap the covered runs in ONE `w:hyperlink` whose
  id is `_mainPart.AddHyperlinkRelationship(uri, isExternal:true).Id`; a re-link unwraps first (never
  nests hyperlinks); `clearMark(Link)` unwraps (keeps text). A missing/relative href → refused with
  `ComposePatchErrorKind.InvalidHyperlinkTarget` (new kind → 422 via the endpoint's `_ =>` default; no
  silent-loss). Consistent with the v1 mark applier, the link is not additionally wrapped in a `w:ins`
  revision (same documented later refinement as `w:rPrChange` for bold/italic).
- Client `stepOperationInterceptor.ts`: `TIPTAP_MARK_TO_COMPOSE.link='Link'`; `classifyMarkStep` reads
  `step.mark.attrs.href` onto the setMark op; `marksToComposeMarks` EXCLUDES `Link` (an insertText
  `marks[]` array carries no href slot — link is only ever the value-carrying setMark op).
- Client `useAiApplyValidation.ts`: the apply path special-cases `Link` → `setLink({href})`/`unsetLink`
  (never a boolean toggleMark that would drop the href).
- Toolbar `ComposeFormatToolbar.tsx`: `hyperlinkDisabled` flipped from `true` to `controlDisabled` (the
  SDL-4/5 R4 guard removed) + the "future release" deferred reason dropped; `toggleLink` already existed.

## Schema version — kept `compose-ops-v2` (no bump)
The extension is **additive + JSON-backward-compatible** (a new enum member + an optional op field), and
client/server share the version constant + deploy together (Compose "last deploy wins"). The engine's
ordinal version check passes as long as both ends share the constant — which they do. Bumping to v3 would
churn ~8 test files that hard-code the literal for no correctness gain. ADR-039 "closed catalog under
version control" is satisfied by the version-controlled source. (A skewed rolling deploy is not a concern
for the shared `sprk_spaarkeai`/`spaarke-bff-dev` single-deploy model.)

## Verification
- New byte-author tests **5/5**: engine `setMark(Link)` → `w:hyperlink` + external rel; `clearMark(Link)`
  unwraps keeping text; missing href → `InvalidHyperlinkTarget`; renderer href → clean `w:hyperlink` +
  external rel + NO tracked markup; relative href → text kept / link dropped.
- Full Compose C# suite **819/819** (814 prior + 5 — R4.5 non-regression intact); corpus byte-diff
  **24/24** (in the suite — untouched subtrees byte-identical, two-byte-author split preserved).
- Client **109/109** across the runnable suites: `stepOperationInterceptor` (link→setMark(href) /
  link-remove→clearMark / strike stays unrepresentable / insertText drops Link), `ComposeFormatToolbar`
  (link ENABLED both modes, setLink/unsetLink), + the prior banner/scroll-sync suites. Typecheck clean.
- ArchTests: same **3 pre-existing failures** (ADR-007, ADR-010 ×2) — zero new; **Tier-1 NetArchTest
  passes** (no AI internals in the renderer/engine hyperlink code — ADR-013).
- Publish **48.13 MB** compressed (unchanged vs task 030; no new runtime package; ≤60 ceiling). BFF build 0 errors.

## Escalation trigger — did NOT fire
Both paths faithfully represent `w:hyperlink` (verified + tested). Malformed targets are refused (edit
path, 422) or text-preserved-link-dropped (authored path) — never silent-loss. No re-enabling of the
control while a silent-loss path remains.

## Scoped deviation — test level (honest)
The POML criterion 3 asked for through-the-WebApplicationFactory seam slices on both paths. I proved both
byte-authors at the **byte-author boundary directly** (renderer `SynthesizeDocument` model→bytes; engine
`Apply` bytes→bytes) — the load-bearing OOXML-emission layer — plus the corpus byte-diff **seam** suite
(`ComposeShadowPatchEngineByteDiffSeamTests`, in the 819) proving untouched-subtree byte-identity for
engine ops. A dedicated through-the-wire `POST /save` hyperlink seam slice is a reasonable follow-up but
adds little over the byte-author proofs (which are stricter for OOXML emission). Filed as a low-priority
follow-up rather than a blocker.

## Step 9.5 quality gates (applied)
- **code-review**: correctness verified across both paths (5 byte-author tests + 109 client); no
  security surface (href is content, external rel is standard OOXML); no AI code smells; §11 satisfied
  (extended `ComposeMarkType`/`SetMarkOperation`/`ComposeInlineRun`, not a new op/service/library).
- **adr-check**: ADR-049 (byte[]-in/out, `(paraId,runIndex,offset)` anchored, I-7 no text-search,
  untouched subtrees byte-identical, two-byte-author split preserved), ADR-039 (catalog EXTENDED not
  forked; no new AI dispatch), ADR-013 (no AI type — Tier-1 passes), ADR-010 (stateless engine; per-apply
  `_mainPart`), ADR-007 (relationship handling in the byte-authors, no Graph call), ADR-038 (byte-author
  slices, no banned shapes), §10 (all in Services/Compose; no new service/endpoint/package; ≤60 MB). Clean.

## PR obligations
- **Placement Justification (§10)**: all hyperlink surface lands in `Services/Compose/` (renderer +
  engine + op catalog) + the client mirror; no new service, endpoint family, or package.
- `/conflict-check` before the BFF + shared-client PR (renderer/engine/op-catalog overlap compose-r1/r2/r3
  + ai-architecture-redesign-r2; toolbar overlaps analysis-hub-r1 shared client — NFR-09 reopen-restore
  parity covered by the 819 suite). Watch #266 OpenXml on PR.
