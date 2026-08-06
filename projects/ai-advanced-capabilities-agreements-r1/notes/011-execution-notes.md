# Task 011 — Execution Notes: WS-4 anchoring — review notes + citations consume ComputedNumber / CitationResolver

> Rigor: FULL · Model tier: sonnet @ xhigh · Step mode: directional · Status: complete

## Step 0 — audit (how sectionRef flows today + what reference data is client-available)

Traced the full path before designing anything:

1. **`AdvisoryCommentInput`** (`ComposeEditor.tsx` ~:710-745) already carries an optional `sectionRef?: string`
   alongside `targetText`/`explanation`. `placeAdvisoryComments` (handle impl, ~:2615-2660 after this task's
   edits) is the sole consumer that materializes an anchored comment thread from these items.
2. **Today's resolution** (pre-task): `resolveAdvisoryAnchorSpan(editor, item.targetText)` — strict text match,
   then first-occurrence, then verbatim-prefix fallback (UAT round-3 S1). `sectionRef` was accepted as metadata
   passthrough to `createThread` (for the gutter card's citation display) but was **never used to resolve the
   anchor position** — exactly the gap this task closes.
3. **What reference data the client already has**: `ComposeEditor`'s `paraIdMap?: readonly ParaIdMapEntry[]` prop
   is populated end-to-end — `ComposeService.LoadAsync` (`Sprk.Bff.Api`) returns `LoadComposeDocumentResult.ParaIdMap`
   = `projection.ParaIdMap` (the WS-3/WS-4-extended `Services/Compose/ParaIdPreParser.ParaIdMapEntry` record:
   `Index, ParaId, IsMinted, ComputedNumber, NumberingLevel, ListPath, HeadingLevel`) → the wire
   `LoadComposeDocumentResponse` projects the SAME C# record type verbatim (`ComposeEndpoints.cs:2164`) → client
   `ComposeWorkspace.tsx` hydrates it into `state.paraIdMap` (:828/844) → passed as `paraIdMap={state.paraIdMap}`
   into `<ComposeEditor>` (:2759). **The payload already carries `computedNumber`/`listPath` end-to-end** — the
   only gap was that the CLIENT `ParaIdMapEntry` TypeScript interface (`compose-contracts.ts`) hadn't been
   extended to type those fields (they were present on the wire, untyped), and nothing on the client consumed
   them for resolution. `paraIdMap` was destructured in `ComposeEditor.tsx` but **not referenced anywhere in the
   function body** — a plumbed-but-unused prop, confirmed by grep.
4. **Escalation check** (per the POML trigger: "if the projection payload does not carry enough of the ReferenceMap
   client-side and a new BFF endpoint seems needed — STOP"): **not fired.** The payload already carries every
   field `CitationResolver.cs` needs (`ComputedNumber`, `ListPath`) via the existing `paraIdMap` prop. No new
   endpoint, no new network surface.
5. **paraId → live position primitive**: rather than writing a new doc-walk, reused `collectBlocks(editor)`
   (already exported from `importedRevisions.ts`, task 050/051 — returns `BlockInfo[]` with `{ from, to, paraId,
   text }` per paragraph/heading in document order, descending into table cells). This is the SAME primitive
   `applyImportedCommentAnchors`/`applyImportedRevisions` already use to resolve a `paraId` to a live span.

## Step 1 — design decision (client mirror vs. server call)

**Decision: a pure client-side mirror of `CitationResolver.cs`** (new file `composeCitationResolver.ts`), NOT a
server round-trip. Applied the CLAUDE.md §11 three-question test (documented in the new file's own header
JSDoc, reproduced here):

1. **Existing** — no client module parses a legal citation string into an ordinal path. `CitationResolver.cs`
   does this, but it is a static, pure C# function with zero I/O — there is no process boundary to "call" from a
   browser runtime without adding a new HTTP round trip per advisory-comment placement.
2. **Extension** — a new `POST /api/compose/.../resolve-citation` endpoint was considered and rejected: it is
   exactly the NEW-BFF-ENDPOINT path the task's escalation trigger warns against, and unnecessary, because the
   projection payload already carries every field the resolver needs (confirmed in Step 0.3). A network round
   trip would add per-finding latency for data already in hand client-side.
3. **Cost of doing nothing** — without this, `placeAdvisoryComments` keeps anchoring EVERY finding by fuzzy
   text/position match, including ones the review model precisely cited by section — the exact ambiguity class
   DEF-01 (task 012) diagnoses (a should-be-unique target matching more than one location).

Per the POML's explicit allowance ("mirror CitationResolver.cs's parsing semantics client-side ONLY if you
cannot reuse it server-side without new surface; if you mirror, add a seam test asserting parity... and document
the duplication + why"): the parity obligation is satisfied by `composeCitationResolver.test.ts`'s "structured
map" describe block, which ports the C# seam tests' in-memory-map cases **verbatim** (same maps, same citation
strings, same expected paraIds) from `tests/integration/seam/Compose/ComposeCitationResolverSeamTests.cs` — the
letter/roman sub-item + decoy-neighbor case, the section-prefix-tolerance case, and the bullet-exclusion case
(the cases that build an in-memory `ParaIdMapEntry[]` rather than requiring the server-only corpus-fixture
loader, so they translate directly).

**`clauseLocation.ts` audited, left UNCHANGED** — a deliberate scope decision, noted here per directional-mode
rules (adapt when the anticipated touch point turns out unnecessary). `clauseLocation.ts` answers "what
number/label does THIS ALREADY-RESOLVED position carry" (`computedNumberAt`, `findGoverningHeading`,
`deriveClauseLocationLabel`) — a different concern from "what paraId does THIS CITATION STRING name"
(`resolveCitation`). The two are architecturally distinct in the server twin too (`CitationResolver.cs` is its
own file, separate from `ComposeDocxProjectionBuilder.cs`/`NumberingComputationEngine`). The task's goal
statement ("Advisory-target resolution prefers CitationResolver(sectionRef)→paraId") scopes specifically to
`placeAdvisoryComments`'s targets; the SEPARATE `enrichedReviewFindings` memo (~ComposeEditor.tsx:2287-2304,
which drives the Review Summary panel's location LABEL/sort, not comment anchoring) still resolves its display
position via `resolveTargetSpans(quotedText,'strict')` — left untouched as genuinely out of this task's scope
(a future, symmetric improvement, not required by any acceptance criterion here).

## Step 2 — implementation

1. **`compose-contracts.ts`** — extended the client `ParaIdMapEntry` interface with the four optional WS-4
   fields (`computedNumber?`, `numberingLevel?`, `listPath?`, `headingLevel?`), mirroring the server record.
   Purely additive — every existing caller (old Load responses, tests that construct `{index, paraId,
   isMinted}` only) is unaffected.
2. **`composeCitationResolver.ts`** (new) — the client `CitationResolver` mirror: `resolveCitation(citation,
   referenceMap): CitationResolution`. Ports, function-for-function, the server's `CitationParser.Parse`
   (leading-label strip, range parse, ordinal-path parse with letter/roman sub-item expansion) and
   `CitationResolver.ResolveCore`/`IsCitable`. Forward resolution only (citation → paraId(s)); the reverse
   (`ResolveCitation`, paraId → canonical number) was NOT ported — nothing in this project's acceptance criteria
   needs it.
3. **`ComposeEditor.tsx`**:
   - Imported `collectBlocks`/`BlockInfo` (added to the existing `importedRevisions` import) and
     `resolveCitation` (new import from `./composeCitationResolver`).
   - Added `resolveDeterministicAnchorSpan(blocks, sectionRef, referenceMap)` — resolves `sectionRef` via
     `resolveCitation`, then anchors via `collectBlocks`-derived `paraId → {from,to}`. A RANGE citation
     ("Sections 4–7") spans a single comment from the FIRST matched paragraph's start to the LAST matched
     paragraph's end (both `resolution.matches` and `blocks` preserve document order). Returns `null` — never a
     guess — for every "unresolvable" case: no `sectionRef`, no/empty `paraIdMap`, unparseable citation, zero
     matches, or a resolved paraId no longer present in the live document.
   - Rewired `placeAdvisoryComments`: `const span = resolveDeterministicAnchorSpan(blocks, item.sectionRef,
     paraIdMap) ?? resolveAdvisoryAnchorSpan(editor, item.targetText);` — the FIXED, binding fallback order
     (deterministic first; legacy text ONLY when the deterministic path returns `null`; never the reverse).
     `blocks = collectBlocks(editor)` is computed ONCE per `placeAdvisoryComments` call (a single doc walk), not
     per item.
   - Added `paraIdMap` to the `useImperativeHandle` dependency array (a doc reload that changes the reference
     map now rebuilds the closure instead of reading a stale array).
   - Updated the `placeAdvisoryComments` JSDoc (both on the `ComposeEditorHandle` interface and inline at the
     call site) to document the two-tier resolution order.

## Step 3 — tests

**`composeCitationResolver.test.ts`** (new, 26 tests in the parity describe block + 1 stability test = tests
below):
- Parity ported verbatim from `ComposeCitationResolverSeamTests.cs`: letter/roman sub-item + decoy neighbor,
  section-prefix tolerance, bullet-paragraph exclusion.
- Synthetic-but-structurally-equivalent analogues of the corpus-driven cases (single label incl. 7 citation-text
  variants, top-level-vs-sub-heading precision, decimal sub-item depth, contiguous range incl. descending-range
  normalization, whole-document range with sub-items).
- Negative table: nonexistent section, empty range, 10 malformed/null/undefined inputs (never throws), empty
  reference map.
- **paraId-stability test** (acceptance criterion #4): headless `@tiptap/core` `Editor` +
  `COMPOSE_R3_PARAID` + `stampParaIds` (the exact convention `ComposeEditor.paraId.test.tsx` established).
  Resolves "Section 4.2" → paraId, captures its block span via `collectBlocks`, inserts a NEW unnumbered
  paragraph ABOVE it (`editor.chain().insertContentAt(0, ...)`), then proves (a) the target's position
  genuinely shifted, (b) the SAME paraId still carries the SAME clause text at its new position, and (c) a
  second `resolveCitation` call for the SAME `sectionRef` still resolves to the SAME paraId — the resolver is
  paraId-keyed, not position-keyed, so it is structurally immune to the exact failure mode the criterion names.

**`ComposeEditor.advisoryComments.test.tsx`** (extended, +6 tests): a new describe block
`ComposeEditor.placeAdvisoryComments — deterministic sectionRef→paraId resolution (task 011)`. Every item sets
`targetText` to a string that provably cannot resolve via the legacy path
(`'§§§ THIS TARGET TEXT DOES NOT APPEAR ANYWHERE IN THE DOCUMENT §§§'`) — so a successfully-placed, correctly-
anchored comment is empirical proof the deterministic path resolved it, not a text search (acceptance
criterion #1's "assert no text-search used", proven behaviorally rather than by mocking):
- single-label `sectionRef` ("Section 4.2") → anchors exactly clause 4.2's full paragraph text.
- sub-item `sectionRef` ("4.2(b)(iii)") → anchors the sub-item paragraph, NOT its [4,2] parent.
- range `sectionRef` ("Sections 4–7") → ONE comment spanning first-to-last matched clause in document order.
- unresolvable `sectionRef` ("Section 99") + a real, unique `targetText` → falls back to the legacy path,
  behavior unchanged for legacy inputs.
- negative: unresolvable `sectionRef` AND unresolvable `targetText` → `placed: 0`, `failed: [{kind:
  'not_found'}]`, zero `commentAnchor` marks in the document (feeds task 012's DEF-01 contract).

The pre-existing DEF-01 test in this same file (`ComposeEditor.placeAdvisoryComments — NDA-REVIEW advisory
comments (task 031)`) is **untouched and still fails identically** (`placed` expected 1, received 2) — none of
its items carry a `sectionRef`, so the deterministic path never engages for that test; the failure is
orthogonal to this task and remains task 012's to fix, per the explicit instruction not to touch it.

### Build/test results (exact)

- `npx tsc --noEmit` (Spaarke.Compose.Components): **0 errors.**
- `composeCitationResolver.test.ts`: **32/32 pass.**
- `ComposeEditor.advisoryComments.test.tsx`: **6/7 pass** — the 1 failure is the pre-existing, documented DEF-01
  bug (unrelated to this diff — verified below).
- **Full package suite** (`npx jest`): **794 tests total, 778 pass, 16 fail (across 6 suites).**
- **Stash A/B verification** (mirrors the R4.5 handoff's own verification convention): stashed all 3 modified
  tracked files (`compose-contracts.ts`, `ComposeEditor.tsx`, `ComposeEditor.advisoryComments.test.tsx`) and
  moved the 2 new files aside, then reran the full suite against the untouched pre-task code: **757 tests total,
  741 pass, 16 fail — the IDENTICAL 16 failures, same 6 suites** (`ComposeEditor.advisoryComments.test.tsx`
  DEF-01; `ComposeWorkspace.bornInEditorSave/imports/saveOpLogPreservation/search.test.tsx`; plus
  `classifyStep`/`RebasedOperationLog`/`resolveRunAnchor`/`stepOperationInterceptor.test.ts` — op-log/step-
  interceptor suites this task never touches). Confirms **zero regressions**; this task's diff adds exactly 37
  new tests (794 − 757), all passing (778 − 741 = 37).
- Un-stashed and restored the diff; re-ran the full suite once more to confirm the restored state matches
  (794/778/16, identical).
- **Lint**: `npm run lint` / `npx eslint` fails repo-wide in this package with "ESLint couldn't find an
  eslint.config.(js|mjs|cjs) file" — the installed ESLint is v9 but the package has no flat-config file. This is
  a **pre-existing environment/tooling gap**, reproduced identically with my changes stashed; not caused by or
  fixable within this task's scope. `tsc --noEmit` is the effective type-safety gate and passes clean.
- No `dotnet` build/test run: **no server-side (`Services/Compose/**`) file was touched** — the deterministic
  path is entirely client-side per the Step 1 design decision, so the "if you touch C# seam tests" condition in
  the task brief never triggers. `tests/integration/seam/Compose/ComposeCitationResolverSeamTests.cs` and
  `ComposeNumberingRoundTripSeamTests.cs` are unmodified (extended in spirit via the ported parity cases on the
  client side, not duplicated on the server side).

## Step 4 — paraId stability

Covered at the unit level (see `composeCitationResolver.test.ts`'s dedicated describe block, detailed above) —
this is the mechanism-level proof that `resolveCitation` (paraId-keyed) + `collectBlocks` (always re-walks the
live doc) are structurally immune to a position shift from an edit inserted above the target. The POML's
`<ui-tests>` "Anchor survives edit" scenario (live UI, numbered-agreement click-through) is **deferred to UAT
tasks 060/061** per the task brief's own instruction ("UI-TESTS in the POML cannot run in this session — cover
both at the unit/seam level and note the UAT deferral to 060/061").

## Quality gates (Step 9.5 — self-run, FULL + TEST-MODIFYING override)

**code-review** (self-run against the 5 changed/new files):
- Quantitative metrics: `composeCitationResolver.ts` 383 lines / 1 exported function + 14 internal helpers (each
  <25 lines, single-purpose, direct 1:1 ports of the equally-small C# private helpers) — the file's length is
  dominated by JSDoc (the reuse-first justification + per-function rationale), not code density. No function
  exceeds the complexity warning threshold. `ComposeEditor.tsx` is a pre-existing 3,200+ line file (already
  well past every size threshold before this task); this diff adds ~70 net lines to it (one new function + a
  ~10-line rewiring of `placeAdvisoryComments` + JSDoc) — not a size regression this task introduces or is
  positioned to fix.
- AI code smells: none found. No new interfaces wrap a single DI-injected service (the 3 new interfaces —
  `CitationShape`/`CitationTarget`/`CitationResolution` — are plain data shapes, not DI seams). No try/catch
  log-rethrow. No null-checks on non-nullable types (every guard in the new code is against a genuinely
  optional/nullable type — `sectionRef?`, `paraIdMap?`, `roman: number | null`). No code-restating comments —
  every comment explains a WHY (reuse-first rationale, fallback-order rule, parity-test provenance) not a WHAT.
  No method exceeds 3 responsibilities (`resolveCitation`'s single pipeline: validate → parse → map → filter →
  sort mirrors the C# `ResolveCore` 1:1).
- **No `any` types** in any new/touched code (grepped — only prose occurrences of the word "any" inside
  comments).
- Security/performance: no secrets, no I/O, no new network surface. `collectBlocks` runs once per
  `placeAdvisoryComments` call (not per item) — no N+1 pattern introduced.

**ADR compliance**:
- **ADR-049** (no text-search in the write path when a deterministic paraId resolution exists) — this task's
  entire purpose; the deterministic path is checked FIRST and is documented as binding in both the handle JSDoc
  and the inline call-site comment. Compliant by construction.
- **ADR-038** (testing strategy) — the 7 KEEP-path categories govern `tests/**` (.NET); these are client
  TypeScript/Jest tests co-located with source per this package's established convention (every sibling
  `*.test.ts(x)` in `widgets/`) — out of ADR-038's `tests/**` scope, not a violation.
- **CLAUDE.md §11** (component justification) — `composeCitationResolver.ts` is the one new file this task
  adds; its header JSDoc carries the explicit three-question justification (reproduced in Step 1 above).
- No BFF/server-side files touched — §10 BFF Hygiene and the Placement Justification obligation do not apply.

No Critical or Warning findings. No ADR conflict to escalate per §6.5 — no path A/B/C decision needed.

## Acceptance criteria

| # | Criterion | Result | Evidence |
|---|---|---|---|
| 1 | "Section 4.2" resolves via ComputedNumber/CitationResolver to the exact paraId; no text-search used | **PASS** | `ComposeEditor.advisoryComments.test.tsx` new test 1 — garbage `targetText` (legacy path would fail) still places a correctly-anchored comment, proving the deterministic path resolved it |
| 2 | Sub-item ("4.2(b)(iii)") and range ("Sections 4–7") resolve per CitationResolver semantics | **PASS** | new tests 2 (sub-item, anchors the exact sub-clause not its parent) + 3 (range, spans first-to-last matched clause); parity-proven against ported C# seam-test cases in `composeCitationResolver.test.ts` |
| 3 | Unresolvable sectionRef falls back to legacy path; behavior unchanged for legacy inputs | **PASS** | new test 4 (unresolvable sectionRef + real unique targetText → legacy path places it); pre-existing DEF-01 test (no sectionRef at all) is byte-identical before/after this diff |
| 4 | Anchors survive an edit inserted above the target (paraId stability) | **PASS (unit-level)** | `composeCitationResolver.test.ts` stability test — proves paraId-keyed resolution + live re-walk are immune to the position shift. UI-level click-through **deferred to UAT tasks 060/061** per task brief |
| 5 | Negative: sectionRef matching nothing does NOT place a comment; build + tests green | **PASS** | new test 5 (`placed: 0`, `failed: [{kind:'not_found'}]`, zero DOM anchor marks); `tsc --noEmit` 0 errors; full suite 794/778/16 with the 16 confirmed pre-existing via stash A/B |

## Deviations / escalations

- **`clauseLocation.ts` left unchanged** (documented as a scope decision in Step 1, not a violation of the
  POML's anticipated file list — directional-mode adaptation).
- **No escalation fired.** The POML's escalation trigger (new BFF endpoint needed because the projection payload
  doesn't carry enough of the ReferenceMap) did not apply — the payload already carries `computedNumber`/
  `listPath` end-to-end; only the client TS type needed extending (Step 0.3).
- **Lint tooling gap** (ESLint v9 flat-config missing repo-wide in this package) is pre-existing, reproduced via
  stash A/B, not introduced or fixable within this task's scope — noted, not fixed.

## Files touched

- `src/client/shared/Spaarke.Compose.Components/src/types/compose-contracts.ts` — extended `ParaIdMapEntry` with
  4 optional WS-4 fields (`computedNumber`, `numberingLevel`, `listPath`, `headingLevel`).
- `src/client/shared/Spaarke.Compose.Components/src/widgets/composeCitationResolver.ts` (new) — the client
  `CitationResolver` mirror (`resolveCitation`).
- `src/client/shared/Spaarke.Compose.Components/src/widgets/composeCitationResolver.test.ts` (new) — parity +
  paraId-stability tests.
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeEditor.tsx` — `resolveDeterministicAnchorSpan`
  + `placeAdvisoryComments` fixed-order rewiring + JSDoc + import additions + handle dependency-array update.
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeEditor.advisoryComments.test.tsx` — new
  describe block (6 tests) + a small `escapeRegExp` test helper (a fixture citation label contains literal
  parens).

Not touched: `clauseLocation.ts` (audited, out of scope — see Step 1); any `src/server/api/Sprk.Bff.Api/**` file
(no server-side surface needed); `tests/integration/seam/Compose/**` (no server-side change to extend);
`current-task.md` / `TASK-INDEX.md` (hard boundary — main-session-only per project convention).
