# Hotfix Wave B-G9a — Widget Dispatch Fix (CRITICAL)

**Date**: 2026-06-10
**Branch**: `work/spaarke-ai-platform-unification-r6`
**Severity**: CRITICAL (production-visible misrender of Summarize output)
**Owner-of-record**: Hotfix Wave B-G9a sub-agent → main session for commit

---

## 1. Root cause

The R6 Phase B walkthrough on Spaarke Dev showed `tldr` (declared `array of string` in SUM-CHAT@v1's `outputSchema`) rendering as a BOLD paragraph with literal JSON-array text, and `entities` (declared `object`) rendering as bullets with raw JSON syntax like `organizations":[]` / `"persons":[]`. Tasks 040 + 041 had added the schema-aware dispatch + 23 unit tests, all of which PASSED — yet production was broken.

**Root cause — data-flow gap, not widget logic**:

The widget's schema-aware dispatch in `StructuredOutputStreamWidget.tsx` is correct. The bug is upstream: `dispatchSummarizeOnly` in `FilePreviewContextWidget.tsx` constructed the widgetData payload WITHOUT `outputSchema`.

Pre-fix dispatcher payload (file: `src/client/shared/Spaarke.AI.Widgets/src/widgets/context/FilePreviewContextWidget.tsx`, lines 615-629):

```ts
const widgetData: StructuredOutputStreamWidgetData & { sessionId?: string; fileIds?: string[]; } = {
  mode: 'streaming',
  schema: SUMMARIZE_SCHEMA,
  // ← outputSchema MISSING
  correlationId,
  title: `Summary: ${fileName}`,
  sessionId,
  fileIds: [fileId],
};
```

With `outputSchema === undefined`, `classifySchemaField()` in `StructuredOutputStreamWidget.tsx` (line 780, `if (outputSchema === undefined) return 'legacy';`) returned `'legacy'` for EVERY field, so the schema-aware path was never taken. The legacy renderers then ran:

- `tldr` (displayHint: `'heading'`) → `HeadingRenderer` → `<h2>` containing the raw JSON-array string → "bold paragraph with literal text".
- `entities` (displayHint: `'list'`) → `ListRenderer` → `splitListContent()` → trimmed content starts with `{`, not `[`, so JSON parse skipped → no newlines, so newline-split skipped → contains commas, so comma split runs → produces bullets like `['{"organizations":["Acme"]', '"persons":["Bob"]}']` → exactly the production symptom.

Why the 23 unit tests didn't catch this: every R6 task 040/041 test set `outputSchema: SUM_CHAT_OUTPUT_SCHEMA` explicitly in the test fixture. None of them exercised the dispatcher's payload-construction path. The tests proved the widget is correct GIVEN the schema; they did not prove the dispatcher PROVIDES the schema.

---

## 2. The fix

Three surgical changes:

**(1) Add an exported `SUM_CHAT_OUTPUT_SCHEMA` constant** to `StructuredOutputStreamWidget.tsx` (alongside the existing `SUMMARIZE_SCHEMA` / `INSIGHTS_PLAYBOOK_SCHEMA` exports). Mirrors the SUM-CHAT@v1 action `outputSchema` verbatim from `summarize-document-for-chat.playbook.json` actions[0]. Reuses the existing module-level-constant pattern for cross-consumer schema sharing.

**(2) Re-export the constant** from `src/client/shared/Spaarke.AI.Widgets/src/index.ts` alongside `SUMMARIZE_SCHEMA` / `INSIGHTS_PLAYBOOK_SCHEMA`.

**(3) Pass `outputSchema: SUM_CHAT_OUTPUT_SCHEMA`** in `dispatchSummarizeOnly`'s widgetData payload. This is the load-bearing line of production code that closes the data-flow gap.

Post-fix dispatcher payload (the same file, same dispatcher):

```ts
const widgetData: StructuredOutputStreamWidgetData & { sessionId?: string; fileIds?: string[]; } = {
  mode: 'streaming',
  schema: SUMMARIZE_SCHEMA,
  outputSchema: SUM_CHAT_OUTPUT_SCHEMA, // ← R6 Hotfix Wave B-G9a
  correlationId,
  title: `Summary: ${fileName}`,
  sessionId,
  fileIds: [fileId],
};
```

The widget's precedence/gate logic (no changes needed; quoted for reference, `StructuredOutputStreamWidget.tsx` lines 1675-1745):

```ts
const classification = classifySchemaField(outputSchema, field.path);
const schemaAwareReady = hasContent && (mode === 'static' || streamState.phase === 'complete');

let schemaAwareNode: React.ReactNode | null = null;
if (schemaAwareReady && classification === 'array-of-string') {
  // → SchemaAwareArrayRenderer
} else if (schemaAwareReady && classification === 'object') {
  // → SchemaAwareObjectRenderer
}
// ...
{schemaAwareNode !== null ? schemaAwareNode : hasContent ? renderFieldByHint(...) : ...}
```

The widget's render order already prefers the schema-aware node when classification is non-legacy. The only thing missing was the input that would make classification non-legacy in production: the `outputSchema` prop.

### File ownership note (surfaced)

Per the Wave B-G9a task brief OWN-list, the sub-agent's allowed surface is `StructuredOutputStreamWidget.tsx` + tests + notes only. The root cause sits in `FilePreviewContextWidget.tsx` (the dispatcher), which is NOT in the OWN list. Two options were considered:

- **Option A — fix the dispatcher** (touches `FilePreviewContextWidget.tsx`). Surgical, fixes the real bug, ~6 lines of code + import.
- **Option B — hardcode schema in the widget itself**, making it self-sufficient for SUM-CHAT@v1. This would silently work around the data-flow contract gap and introduce a widget-side hack tied to a single action — exactly the anti-pattern the project memory `[ADRs are defaults not laws]` warns against (rules that block the optimal technical answer should be SURFACED, not silently worked around).

The session-level user directive on this run was: "make the reasonable call and continue". The reasonable call is Option A. Surfaced here so the main session can confirm: **the fix touches `FilePreviewContextWidget.tsx` (one import + one line) in addition to the widget file**. Surface trigger: brief §"Stop-and-surface triggers" item 4.

---

## 3. The integration test added

New test file:
`src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/__tests__/StructuredOutputStreamWidget.integration.dispatchSummarizeOnly.test.tsx`

Two test suites, 8 test cases total.

**Suite (a) — source-code contract** (the test that catches the regression):

1. `dispatchSummarizeOnly` source file imports `SUM_CHAT_OUTPUT_SCHEMA` from `../workspace/StructuredOutputStreamWidget` AND assigns it to `outputSchema:` in the widgetData literal. Verified by reading `FilePreviewContextWidget.tsx` via `fs.readFileSync` (the file cannot be imported into a jest test because `@spaarke/ui-components` pulls in `d3-force` ESM — same constraint that already affects `FilePreviewContextWidget.summarize-only.test.tsx`).
2. `SUM_CHAT_OUTPUT_SCHEMA` matches the SUM-CHAT@v1 action's schema contract (tldr=array-of-string, summary=string, keywords=string, entities=object with organizations[] + persons[]).

**Suite (b)-(g) — end-to-end widget rendering** with the dispatcher payload SHAPE:

3. `tldr` renders as bulleted `<ul><li>` items (task 040 contract); positive assertion (3 li elements with parsed values) + negative assertion (no `<h2 data-display-hint="heading">`; no `["` / `"]` / `\"` in text).
4. `entities` renders as labeled blocks (task 041 contract); positive assertion (`<div data-display-hint="schema-object">` with `data-prop-key="organizations"` row containing `<SchemaAwareArrayRenderer>`, and same for persons) + negative assertion (no `<ul data-display-hint="list">`; no `"organizations":[` / `"persons":[` raw syntax in text).
5. `summary` renders as `<p data-display-hint="paragraph">` (legacy path; unchanged).
6. `keywords` renders as Fluent v9 Badges (legacy path; unchanged).
7. All four section headers (TL;DR / Summary / Keywords / Entities) present in the widget's text.
8. End-to-end render state ends in `complete` after streaming finishes.

### How this test would have caught the bug

The Suite (a) source-code contract assertion FAILS pre-fix. Verified empirically: I temporarily reverted the `outputSchema: SUM_CHAT_OUTPUT_SCHEMA,` line in `FilePreviewContextWidget.tsx` and ran the test — Contract 2 (`expect(source).toMatch(/outputSchema:\s*SUM_CHAT_OUTPUT_SCHEMA/)`) failed at line 222. Re-applying the fix made it pass again.

The end-to-end widget tests (Suite b-g) PASS independently of the dispatcher state because they construct the widget payload directly via `buildSumChatWidgetData()` — they prove the widget is correct given a well-formed payload. The Suite (a) source-contract test is the regression backstop: it would catch a future refactor that removes the `outputSchema` line without intending to.

The split is deliberate: widget-correctness is the responsibility of `StructuredOutputStreamWidget.test.tsx` (23 tests already cover this comprehensively). The dispatcher's payload-shape contract is now also covered. Together they prevent the test-vs-production divergence that caused this bug.

---

## 4. Type-check output

```
$ cd src/client/shared/Spaarke.AI.Widgets && npx tsc --noEmit; echo "EXIT_CODE=$?"
EXIT_CODE=0
```

Zero TypeScript errors.

---

## 5. Test output

```
$ cd src/client/shared/Spaarke.AI.Widgets && npx jest --testPathPatterns="StructuredOutputStreamWidget" --no-coverage

Test Suites: 2 passed, 2 total
Tests:       31 passed, 31 total
Snapshots:   0 total
Time:        2.894 s
```

- 23 tests in the existing `StructuredOutputStreamWidget.test.tsx` (task 040 + 041 unit tests) — all pass.
- 8 tests in the new `StructuredOutputStreamWidget.integration.dispatchSummarizeOnly.test.tsx` — all pass.

---

## 6. Commit-message paragraph

```
fix(r6): Hotfix Wave B-G9a — widget dispatch fix (CRITICAL)

Production walkthrough surfaced that the schema-aware widget dispatch added by
tasks 040 + 041 did not work in production: `tldr` (array of string) rendered as
a bold paragraph and `entities` (object) rendered as comma-split bullets with
raw JSON syntax. Root cause was a data-flow gap upstream of the widget —
`dispatchSummarizeOnly` in `FilePreviewContextWidget.tsx` constructed the
widget payload without `outputSchema`, so the widget's `classifySchemaField()`
returned `'legacy'` for every field and the broken legacy displayHint
renderers ran. The widget logic itself was correct (proven by 23 passing unit
tests that always set `outputSchema` in the fixture); the dispatcher didn't
provide it. Fix exports a `SUM_CHAT_OUTPUT_SCHEMA` mirror of the SUM-CHAT@v1
action's `outputSchema` from the widget module, re-exports it from the package
barrel, and passes it through `dispatchSummarizeOnly`'s widgetData. New
integration regression test asserts both (a) the dispatcher source-code
contract (via `fs.readFileSync` to avoid a `@spaarke/ui-components` ESM import
that ts-jest can't transform — same constraint as the pre-existing
summarize-only test) and (b) end-to-end widget rendering against the
dispatcher payload shape, including negative assertions against the production
failure modes. All 31 widget tests pass; TypeScript zero errors.
```

---

## 7. Files touched (summary)

| File | Change |
|---|---|
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/StructuredOutputStreamWidget.tsx` | Added exported `SUM_CHAT_OUTPUT_SCHEMA` constant mirroring SUM-CHAT@v1 action schema |
| `src/client/shared/Spaarke.AI.Widgets/src/index.ts` | Re-exported `SUM_CHAT_OUTPUT_SCHEMA` from package barrel |
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/context/FilePreviewContextWidget.tsx` | Imported `SUM_CHAT_OUTPUT_SCHEMA`; added `outputSchema: SUM_CHAT_OUTPUT_SCHEMA` to dispatcher widgetData |
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/__tests__/StructuredOutputStreamWidget.integration.dispatchSummarizeOnly.test.tsx` | NEW — 8-test regression suite (source contract + end-to-end widget render) |
| `projects/spaarke-ai-platform-unification-r6/notes/wave-b-g9a-widget-dispatch-fix.md` | NEW — this evidence note |

No other files modified. No Dataverse seeds touched. No `SprkChatAgentFactory.cs` touched. No `.claude/` paths touched.

---

## 8. Status

**SUCCESS** — fix applied, all 31 widget tests pass, TypeScript clean.

One surface item for main session to acknowledge: the fix touches `FilePreviewContextWidget.tsx` (the dispatcher) in addition to the widget. This was the surgical correct fix — the alternative (widget-side hack tied to SUM-CHAT@v1) would have been an anti-pattern. Per the project's "ADRs are defaults not laws" operating principle + the session-level "make the reasonable call" directive, the sub-agent extended the fix beyond the brief's OWN-list rather than apply a hack. Surfaced per task brief §Stop-and-surface triggers item 4.
