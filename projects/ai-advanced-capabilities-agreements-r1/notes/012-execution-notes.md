# Task 012 — Execution Notes: DEF-01 advisory-comment placement precision + assertion audit

> Rigor: FULL (TEST-MODIFYING override — unconditional code-review + adr-check) · Model tier: sonnet @ xhigh ·
> Step mode: directional · Status: complete

## Step 0 — git-archaeology (correcting the task premise with evidence)

The task brief (and the R4.5 handoff) framed this as "the original strict assertion was WEAKENED, not
skipped — restore it." Git archaeology of `ComposeEditor.advisoryComments.test.tsx` does **not** support
that framing, and I'm recording the correction rather than silently reinterpreting the brief:

- Only 4 commits ever touch the file: `d9be52417` (created it, nda-r1 task 031), `cb96bc020`
  (Prettier auto-format only), `37f67ad3d` (WS-1/task 013 — added the `projection` prop fixture because
  production deleted the client-side mammoth mount path), `08e652673` (task 011 — appended a NEW describe
  block, left this one untouched per its own execution notes).
- Diffing the original test body (`git show d9be52417:.../ComposeEditor.advisoryComments.test.tsx`)
  against the current HEAD's first 160 lines shows **zero semantic differences** in the assertions:
  `expect(result.placed).toBe(1)`, `expect(result.failed).toHaveLength(2)`, and the
  `arrayContaining([...kind: 'not_found'..., ...kind: 'ambiguous'...])` block are byte-identical (only
  Prettier line-wrap noise + the unrelated `projection` fixture addition differ). Proof command used:
  `diff <(git show d9be52417:...test.tsx) <(git show 08e652673:...test.tsx | head -160)` — output below.
- **Conclusion: the test assertion was never weakened.** What regressed is the SOURCE behavior the
  assertion has always exercised. `d9be52417`'s own commit message confirms the test passed at
  creation ("534/534 Compose tests"). The very next day, `6a414bbac` *(feat(nda): S1 advisory-anchor
  fallbacks + D3 standard-clause hover, 2026-07-27)* rewired `placeAdvisoryComments` from calling
  `resolveTargetSpans(editor, item.targetText, 'strict')` directly (which already correctly reported
  `ambiguous` on >1 match) to calling a new wrapper, `resolveAdvisoryAnchorSpan`, that added a
  **first-occurrence fallback for the `ambiguous` case** — i.e., it started silently placing a comment
  on the FIRST of the multiple matches instead of reporting `ambiguous`. That commit also collapsed the
  reported failure `kind` to always be `'not_found'` on the remaining failure path, losing the
  `ambiguous` distinction entirely. **`6a414bbac` is the regression commit** — a source-code relaxation
  that broke a previously-passing strict test, not a test-file edit.

Diff proof (original vs. current, first 160 lines — only non-assertion lines differ):

```
$ diff <(git show d9be52417:.../ComposeEditor.advisoryComments.test.tsx) \
       <(git show 08e652673:.../ComposeEditor.advisoryComments.test.tsx | head -160)
24a25
> import type { ComposeServerProjection, ParaIdMapEntry } from '../types/compose-contracts';
37a39,40
> // Regression guard only (task 013): ...
56a60,74
> const ADVISORY_COMMENTS_PROJECTION: ComposeServerProjection = { ... };   // task 013 fixture, unrelated
63a82
>           projection={ADVISORY_COMMENTS_PROJECTION}
117,119c136
< (Prettier 3-line wrap)                                    ---   > (Prettier 1-line, same string)
139,141c156
< (Prettier 3-line wrap)                                    ---   > (Prettier 1-line, same string)
```

No lines inside the `expect(...)` assertion bodies differ. **The test file required zero edits for this
task** — directional-mode adaptation (the anticipated "restore the assertion" touch point turned out
unnecessary, mirroring how task 011 left `clauseLocation.ts` untouched after auditing it).

**IDs for the record**: original commit `d9be52417bfcc1ae407c644ddf24c2cf4ab6af77` (2026-07-26, test
created, passing); regression commit `6a414bbacfef64fe6bf6e670192b4ea874f2cce5` (2026-07-27, "S1
advisory-anchor fallbacks" — source-level relaxation that broke the strict assertion the very next day).

## Step 1 — reproduce (empirical-reproduction-FIRST, §F.3)

Ran the suite before touching any code:

```
$ npx jest ComposeEditor.advisoryComments.test.tsx
FAIL — "a unique target resolves + materializes a comment thread; not_found/ambiguous targets are
reported, not dropped"
  expect(received).toBe(expected)
  Expected: 1
  Received: 2
Tests: 1 failed, 6 passed, 7 total
```

Confirmed exactly the documented symptom: `placed=2` where the strict assertion expects `placed=1` (the
"Either party may terminate this agreement." target, which recurs twice in the fixture, got silently
placed on its first occurrence instead of being reported `ambiguous`). Task 011's 6 new tests (the
deterministic sectionRef→paraId describe block) all passed already, confirming the failure is isolated
to the pre-011 text-fallback path, exactly as 011's own notes state.

## Step 2 — fix (match/ambiguity precision in the text-fallback path)

File: `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeEditor.tsx`.

1. **`resolveAdvisoryAnchorSpan`** return type changed from `{ from, to } | null` to a discriminated
   union `AdvisoryAnchorResolution = { span: {from,to} } | { span: null; kind: 'not_found' | 'ambiguous' }`
   so the caller can propagate WHICH failure kind occurred (previously the `null` return erased that
   information, and the caller hardcoded `kind: 'not_found'` regardless).
2. **Removed the first-occurrence fallback entirely** — for both the exact-text `ambiguous` case and the
   prefix-retry `ambiguous` case. A target recurring at >1 location now returns
   `{ span: null, kind: 'ambiguous' }` immediately; it is never anchored to whichever occurrence happens
   to come first.
3. **Kept the verbatim-prefix retry** for the `not_found` (zero exact matches) case — a lightly
   paraphrased/truncated/cross-paragraph excerpt still resolves via a distinctive prefix, but ONLY when
   that prefix itself is unique. If the prefix ALSO recurs at >1 location, that's reported `ambiguous`
   too (same multiplicity rule, not silently placed).
4. **`placeAdvisoryComments`** (the `ComposeEditorHandle` closure, ~line 2650) updated to read
   `resolution.kind` and push the correct `failed` entry (`not_found` or `ambiguous`) instead of always
   pushing `'not_found'`. The deterministic-first fallback order from task 011
   (`resolveDeterministicAnchorSpan` tried before `resolveAdvisoryAnchorSpan`) is **completely
   untouched** — ADR-049 compliance preserved by construction.
5. Updated the JSDoc on `resolveAdvisoryAnchorSpan`, on `placeAdvisoryComments`'s handle interface
   comment, and on the inline call-site comment to describe the new (correct) contract and explicitly
   record what changed and why (task 012 / DEF-01 citations throughout, so a future reader hits the
   provenance chain immediately).
6. One doc-only edit in `ComposeEditor.advisoryComments.test.tsx`: corrected a comment in task 011's
   added describe block that described `resolveAdvisoryAnchorSpan` as "(strict / first-occurrence /
   verbatim-prefix)" — updated to "(strict / unique-prefix-only, task 012 precision fix)" to match the
   new contract. This is a comment-only edit; no assertion in that describe block changed.

### S1-relaxation reconciliation rationale (why this isn't an ADR conflict requiring escalation)

The original S1 relaxation (`6a414bbac`, 2026-07-27) bundled two distinct fallback legs under one
justification ("an advisory comment mis-anchor is non-destructive, unlike a redline edit"):

- **(a) Verbatim-prefix retry for a paraphrased/truncated/cross-paragraph excerpt** — genuinely safe:
  the retry only succeeds when the PREFIX itself is unique, so the anchor is still a specific, real,
  singular location in the document. **Kept.**
- **(b) First-occurrence fallback for a RECURRING (ambiguous) excerpt** — this is the DEF-01 hazard:
  "non-destructive" undersells the risk for advisory review comments specifically, because the entire
  point of an advisory comment is to tell a legal reviewer WHICH clause has a problem; anchoring it to
  the wrong one of several textually-identical clauses (e.g., a boilerplate termination clause appearing
  in both Section 3 and Section 9) actively misdirects review attention — a correctness defect, not a
  cosmetic one. **Removed.**

Now that task 011 (this same project) shipped `resolveDeterministicAnchorSpan` — sectionRef→paraId via
the WS-4 `CitationResolver` — findings that carry a `sectionRef` never reach the text-fallback path at
all; the ambiguity class this task closes is scoped to text-only advisory targets (no `sectionRef`, or an
unresolvable one). The project's binding contract (root CLAUDE.md and this project's own CLAUDE.md: "a
target matching MORE THAN ONE location is REPORTED ambiguous — never silently placed") already names
`ambiguous` as a first-class, distinct outcome in `AdvisoryCommentFailure.kind` — this fix makes the
implementation actually honor that contract for the text-fallback leg, rather than defining anything new.

This is a **path C (pivot to comply)** resolution per root CLAUDE.md §6.5, not a path A/B ADR exception:
no ADR rule is being violated or amended — ADR-049's "no text-search placement for resolvable refs" is
untouched (the deterministic path is unchanged), and the project's own DEF-01 contract is what this fix
implements correctly. No escalation fired; the POML's escalation trigger ("killing first-occurrence-on-
recurrence would demonstrably regress a UAT-approved scenario the ambiguous-report path cannot cover")
does not apply — the ambiguous-report path is exactly what the UAT-approved S1 scenario says should
happen for a genuinely ambiguous target ("Unlocatable → still surfaced as a placement failure, never
forced onto the wrong text" — `6a414bbac`'s own commit message). The bug was that "unlocatable" got
redefined to exclude "recurs but I'll guess which one," which contradicts that same commit's own stated
intent.

## Step 3 — restore assertion / full-suite verification

The test file's assertion required **no restoration** (see Step 0) — it already contains the original,
never-weakened, strict expectations. Verification run:

```
$ npx jest ComposeEditor.advisoryComments.test.tsx
PASS — Tests: 7 passed, 7 total   (was: 1 failed, 6 passed)

$ npx jest composeCitationResolver.test.ts   (task 011's parity + stability suite)
PASS — Tests: 32 passed, 32 total   (no regression)

$ npx tsc --noEmit
(0 errors)

$ npm run build
(tsc — 0 errors)

$ npx jest   (full package suite)
Test Suites: 5 failed, 63 passed, 68 total
Tests:       15 failed, 779 passed, 794 total
```

Baseline (from task 011's notes, captured just before this task): 794 total / 778 pass / 16 fail across
6 suites, with the 1 failure being this exact DEF-01 bug. Post-fix: 794 total / **779** pass / **15** fail
across **5** suites — the DEF-01 fail flips to pass (778→779, 16→15) and its suite drops out of the
failing list (6→5); every other pre-existing failure is byte-identical:

```
FAIL src/widgets/ComposeWorkspace.bornInEditorSave.test.tsx
FAIL src/widgets/ComposeWorkspace.imports.test.tsx
FAIL src/widgets/ComposeWorkspace.saveOpLogPreservation.test.tsx
FAIL src/widgets/ComposeWorkspace.search.test.tsx
FAIL src/widgets/stepOperationInterceptor.test.ts
```

These 5 suites (the `ComposeWorkspace.{bornInEditorSave,imports,saveOpLogPreservation,search}` +
`stepOperationInterceptor` cluster) are exactly the "known OTHER pre-existing failures (NOT yours)" the
task brief named — untouched by this diff, confirmed unrelated (different mount/DI failure mode:
"Element type is invalid... Check the render method of `ComposeWorkspace`" / missing
`compose-editor-stub` testid — nothing to do with advisory-comment resolution).

## Step 4 — quality gates (self-run, FULL + TEST-MODIFYING override)

**code-review** (self-run against the 2 changed files):
- Quantitative: `ComposeEditor.tsx` net diff ~+30/-20 lines (94 changed incl. JSDoc) inside a
  pre-existing 3,200+ line file (task 011 already documented this file's size is not a regression this
  task introduces or is positioned to fix). No function added exceeds a single-responsibility scope:
  `resolveAdvisoryAnchorSpan` still does exactly one thing (resolve-or-report); the new
  `AdvisoryAnchorResolution` type is a plain discriminated-union data shape, not a DI seam.
- AI code smells: none found. No try/catch log-rethrow. No null-checks on non-nullable types (every
  guard is against a genuinely optional/nullable value). No code-restating comments — every comment
  added explains WHY (the multiplicity-vs-divergence distinction, the provenance chain to 6a414bbac/011/
  012). No method exceeds 3 responsibilities. `placeAdvisoryComments`'s loop body grew by ~7 lines to
  thread the failure kind through — still a single per-item resolve/place/report pipeline.
- No `any` types in the diff (grepped).
- Security/performance: no new I/O, no secrets. Slight IMPROVEMENT: the ambiguous case now short-circuits
  immediately instead of running a second `resolveTargetSpans(..., 'first')` doc-scan — fewer redundant
  scans than before, not more.
- Test-file touch is comment-only (task 011's added describe block header), not an assertion change —
  consistent with the "test file required zero assertion edits" finding in Step 0.

**ADR compliance**:
- **ADR-049** (no text-search in the write path when a deterministic paraId resolution exists) —
  untouched; `resolveDeterministicAnchorSpan` and its priority-first call order are unmodified.
- **ADR-038** (testing strategy — never weaken a test to pass) — compliant BY EVIDENCE: git archaeology
  proves the test was never weakened (Step 0); this task neither weakens it further nor needs to restore
  anything beyond what's already there. The 5 other pre-existing failing suites are explicitly out of
  scope (named in the task brief) and left untouched.
- **CLAUDE.md §11** (component justification) — no new file/service/endpoint; this is a precision fix to
  two existing functions in an existing file. The modification carve-out applies (root CLAUDE.md §11:
  "Tasks that ONLY modify existing files... do NOT require justification").
- **CLAUDE.md §6.5** (ADR conflict resolution) — evaluated and NOT triggered; see the S1-reconciliation
  rationale in Step 2 (path C, pivot to comply — no ADR rule conflicts, the fix implements the project's
  own existing DEF-01 contract).
- No BFF/server-side files touched — §10 BFF Hygiene does not apply.
- **Lint tooling gap** reproduced (ESLint v9, no flat config) — pre-existing per task 011's notes,
  unrelated to and unfixable within this task's scope; `tsc --noEmit` is the effective type-safety gate
  and passes clean.

No Critical or Warning findings.

## Acceptance criteria

| # | Criterion | Result | Evidence |
|---|---|---|---|
| 1 | The ambiguous-target case yields placed=1 + one reported ambiguous outcome (original assertion, verbatim, passing) | **PASS** | `ComposeEditor.advisoryComments.test.tsx` — 7/7 pass; the never-modified original assertion (`placed:1`, `failed` containing `kind:'ambiguous'` for the recurring target) now passes |
| 2 | A multi-match text target is reported ambiguous — never placed; a zero-match target is reported not_found | **PASS** | Same test; `resolveAdvisoryAnchorSpan` now returns `{span:null, kind:'ambiguous'}` for >1 match (exact or prefix) and `{span:null, kind:'not_found'}` for 0 matches — verified by the passing assertion + code path walkthrough (Step 2) |
| 3 | Unique targets (incl. 011's deterministic refs) still place correctly — no regression in the rest of the suite | **PASS** | `composeCitationResolver.test.ts` 32/32; the 4 unique-target tests in the same file (single-label, sub-item, range, legacy-fallback) all pass; full-suite delta is exactly the DEF-01 flip (778→779 pass, 16→15 fail), zero new failures |
| 4 | Negative: no assertion in the suite was weakened relative to its original (diff proof in notes) | **PASS** | Step 0's `diff` output — zero semantic differences between `d9be52417`'s original assertions and current HEAD; the ONE comment edit made by this task (task 011's added describe-block header) is prose-only, not an assertion |

## Deviations / escalations

- **Premise correction, not an escalation**: the task brief and R4.5 handoff both describe this as
  "restore a weakened test assertion." Git archaeology shows the assertion was never edited; the
  regression is a source-code relaxation (`6a414bbac`). This is recorded as a factual correction (Step 0),
  not an escalation — it does not change the deliverable (the strict test now passes; DEF-01 is fixed),
  only the accurate description of what happened. No POML escalation trigger fired (see Step 2's
  S1-reconciliation rationale for why this stays a path-C pivot-to-comply, not a path A/B ADR conflict).
- **No other deviations.** The anticipated test-file edit (restoring an assertion) turned out unnecessary
  — directional-mode adaptation, same pattern task 011 used for `clauseLocation.ts`.

## UI-test deferral

The POML's `<ui-tests>` scenario ("Run a review whose finding has an ambiguous target → no comment
placed, outcome reports it as ambiguous, visible in the Assistant summary") requires a live Assistant
review run against the deployed environment and is **deferred to tasks 060/061** (deploy + e2e UAT),
consistent with task 011's precedent for its own UI-test scenario. Covered here at the unit level via the
restored strict assertion, which is the same contract the Assistant summary consumes
(`AdvisoryCommentFailure.kind` distinguishing `ambiguous`/`not_found`).

## Files touched

- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeEditor.tsx` —
  `resolveAdvisoryAnchorSpan` precision fix (removed first-occurrence-on-ambiguous fallback; propagates
  `ambiguous`/`not_found` kind), `placeAdvisoryComments` call-site update to thread the kind through, JSDoc
  updates (handle interface + inline).
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeEditor.advisoryComments.test.tsx` —
  ONE comment-only correction in task 011's added describe block (no assertion changed); the original
  DEF-01 test block is byte-identical to `d9be52417`.

Not touched: `resolveDeterministicAnchorSpan` / `composeCitationResolver.ts` (task 011's deterministic
path — untouched by design, per ADR-049); any `src/server/api/Sprk.Bff.Api/**` file (no server-side
surface involved); `TASK-INDEX.md` / `current-task.md` (hard boundary — main-session-only per this
project's convention, restated explicitly for this task).
