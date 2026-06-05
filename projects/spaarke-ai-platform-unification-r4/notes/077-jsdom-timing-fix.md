# Task 077 — jsdom Timing/Isolation Test Fixes (B.7)

> **Task**: 077 (R4 Phase 6.5 — add-on cleanup)
> **Date**: 2026-05-27
> **Branch**: `work/spaarke-ai-platform-unification-r4`
> **Author**: Claude Code (task-execute STANDARD rigor)
> **Predecessor**: Task 071 (closed at 4 failures vs ≤10 target)

---

## Summary

Fixed all 4 remaining `useChatFileAttachment` + `SprkChat.attachments` test failures carried over from task 071. The pipeline now reports **1051 passed / 0 failed** (up from 1047/4 post-071). Production code untouched — only `__tests__/` files modified.

### Failure budget

| Stage | Failed | Passed | Total |
|---|---|---|---|
| Post-071 (carry-over) | 4 | 1047 | 1051 |
| **Post-077 (this work)** | **0** | **1051** | **1051** |

R4-critical tests now ALL green:
- **042 SprkChat.onAttachmentReady** — 6/6 ✅ (unchanged)
- **050 SprkChat.attachments** — 3/3 ✅ (previously 2/3; the `clearAll on stream completion` test now passes)
- **024 useChatFileAttachment** — 20/20 ✅ (previously 17/20)

A pre-existing CommandBar.test.tsx worker crash remains (unrelated TypeError in `useKeyboardShortcuts.ts` — verified pre-existing by stashing this work and reproducing the crash on clean master state). Out of scope for 077.

---

## 1. Per-failure root cause + fix

### Failure 1 — `useChatFileAttachment > FR-24 telemetry > does not throw when onExtractionError callback itself throws`

**Hypothesis in POML**: mammoth dynamic-import mock cache issue → use `jest.isolateModules()` or restructure mock setup.

**Actual root cause**: NOT a mock cache issue. The test was wrapped in `await expect(act(async () => { ... })).resolves.not.toThrow()`. Under jsdom + RTL v16 + React 19, the `expect.resolves.not.toThrow()` matcher consumes the awaited promise BEFORE `act()` flushes the async extraction pipeline. Net effect: `addFiles` returns immediately, `mockExtractRawText.mockRejectedValue(...)` is never invoked, `onExtractionError` is called 0 times.

**Diagnostic**: I authored two ephemeral test variants — VARIANT A (no `.resolves.not.toThrow()` wrapper) and VARIANT B (with the wrapper). In identical setup, VARIANT A: `mockExtractRawText` called 1 time, `onExtractionError` called 1 time. VARIANT B: both called 0 times. The wrapper IS the bug.

**Fix**: Removed the `expect(...).resolves.not.toThrow()` wrapper. A bare `await act(async () => { await addFiles([docx]); })` already verifies "does not throw" — if the hook propagated a telemetry exception, the `await` would reject and Jest would fail the test. The wrapper is redundant AND racy.

**File**: `useChatFileAttachment.test.ts:345-376` — replaced the wrapper, added explanatory comment.

### Failures 2-3 — `useChatFileAttachment > mutations > removeFile(0) ...` + `... clearAll empties chips, attachments, and errors`

**Hypothesis in POML**: React 19 commit-cycle interaction with leaked extraction-pipeline microtasks → add `afterEach(() => act(() => Promise.resolve()))` to flush microtasks.

**Actual root cause**: Cross-test pollution **caused by Failure 1's `.resolves.not.toThrow()` wrapper**. The un-flushed `act` work in test #1 leaks into test #2's `renderHook(...).result.current` — which returns `null` instead of the hook's return object. Once Failure 1 is fixed, Failures 2 and 3 auto-resolve.

**Diagnostic**: Ran two-test chain — TEST 1 = the broken pattern with `.resolves.not.toThrow()` wrapper, TEST 2 = a clean `renderHook(useChatFileAttachment())`. TEST 1 "passed" but TEST 2 saw `result.current === null`. Removing the wrapper from TEST 1 fixed TEST 2 with no `afterEach` flush needed.

**Fix**: Cascade-fixed by removing the wrapper in Failure 1's test. No additional changes needed for Failures 2 + 3.

**Files**: No additional file changes — Failure 1's fix cascade-resolved these two.

### Failure 4 — `SprkChat.attachments > calls clearAll on successful stream completion`

**Hypothesis in POML**: SSE reader microtask doesn't flush within `waitFor`'s 3000ms timeout under jsdom + RTL v16 act-batching → refactor to mock the SSE reader output directly (return a pre-resolved iterator yielding `done` immediately).

**Actual root cause**: TWO compounding jsdom globals gaps, NOT a timing issue:
1. `emptySseResponse()` used `new TextEncoder().encode(...)` — `TextEncoder` is **undefined** in jest-environment-jsdom v30. The error was caught by `useSseStream`'s outer try/catch and rendered as a chat error banner ("TextEncoder is not defined"), so the `done` event never reached `processEvent → onDone → setIsDone(true)`.
2. `emptySseResponse()` used `new ReadableStream({ start(controller) { ... } })` — `ReadableStream` is **also undefined** in the same env. Same outcome: outer try/catch swallows it.
3. Additionally, `useSseStream.ts:423` (production code) uses `new TextDecoder()` to decode the stream bytes — `TextDecoder` is also undefined. This one CAN'T be fixed in test code; it needs a polyfill at the global scope of the test environment.

**Diagnostic**: Added `console.log(errorBanner?.textContent)` to the test mid-flow. The banner cycled through "TextEncoder is not defined" → "ReadableStream is not defined" → "TextDecoder is not defined" as I patched each gap.

**Fix** (combines POML option (b) "mock the SSE reader output directly" with a polyfill):
1. **Polyfill `TextDecoder` + `TextEncoder` at the top of the test file** using `import { TextDecoder, TextEncoder } from 'util'` (Node's canonical pattern). Conditional assignment to `globalThis` only if missing. No `jest.setup.js` change required — scoped to this one test file.
2. **Replace `new ReadableStream(...)` with a hand-rolled reader** that satisfies the minimal `response.body.getReader()` contract used by `useSseStream`:
   ```ts
   const reader = {
     read: jest.fn(() => readCount++ === 0
       ? Promise.resolve({ done: false, value: sseBytes })
       : Promise.resolve({ done: true, value: undefined })),
     cancel: jest.fn().mockResolvedValue(undefined),
     releaseLock: jest.fn(),
   };
   return { ok: true, status: 200, body: { getReader: () => reader }, ... };
   ```
   This bypasses jsdom's missing `ReadableStream` AND completes synchronously per `await reader.read()` tick — removing the timing fragility entirely.

**File**: `SprkChat.attachments.test.tsx:1-25` (polyfill block), `:82-141` (hand-rolled reader in `emptySseResponse()`).

---

## 2. Files modified (test files only)

```
src/client/shared/Spaarke.UI.Components/src/components/SprkChat/__tests__/
├── useChatFileAttachment.test.ts        (-9/+22 — removed expect.resolves wrapper, added explanation)
└── SprkChat.attachments.test.tsx        (-17/+82 — polyfill TextDecoder/TextEncoder, hand-rolled SSE reader)
```

`git diff --stat` proof of no production-code change:

```
$ git diff --stat src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChat.tsx
(empty — no changes)

$ git diff --stat src/client/shared/Spaarke.UI.Components/src/components/SprkChat/hooks/
(empty — no changes)

$ git diff --stat src/client/shared/Spaarke.UI.Components/src/hooks/useSseStream.ts
(empty — no changes)

$ git diff --stat src/client/shared/Spaarke.UI.Components/src/components/SprkChat/
 .../__tests__/SprkChat.attachments.test.tsx        | 102 ++++++++++++++++++---
 .../__tests__/useChatFileAttachment.test.ts        |  23 +++--
 2 files changed, 105 insertions(+), 20 deletions(-)
```

---

## 3. Reusable patterns for future SprkChat tests

The next "Spaarke AI Chat Assistant" project will touch SprkChat heavily. These patterns prevent the four failure modes above from resurfacing:

### Pattern 1 — Async "does-not-throw" assertions

**Anti-pattern** (broke Failures 1, 2, 3):
```ts
await expect(act(async () => {
  await result.current.someAsyncOp();
})).resolves.not.toThrow();
```

**Use instead**:
```ts
// If it throws, await act() rejects and Jest fails the test. That IS the assertion.
await act(async () => {
  await result.current.someAsyncOp();
});
```

**Why**: `expect.resolves.not.toThrow()` consumes the promise BEFORE `act()` flushes async microtasks. The hook's catch handler never runs, mock setup doesn't propagate, and leftover microtasks corrupt the next test's `renderHook` state.

### Pattern 2 — SSE / streaming-response mocks

**Anti-pattern** (broke Failure 4):
```ts
function emptySseResponse(): Response {
  const stream = new ReadableStream({  // undefined in jsdom v30
    start(controller) {
      controller.enqueue(new TextEncoder().encode('...'));  // also undefined
    },
  });
  return { ok: true, body: stream, ... } as unknown as Response;
}
```

**Use instead — hand-rolled minimal reader**:
```ts
function emptySseResponse(): Response {
  const sseBytes = Uint8Array.from(Buffer.from('data: {"type":"done","content":null}\n\n', 'utf-8'));
  let readCount = 0;
  const reader = {
    read: jest.fn(() => {
      readCount += 1;
      return readCount === 1
        ? Promise.resolve({ done: false, value: sseBytes })
        : Promise.resolve({ done: true, value: undefined });
    }),
    cancel: jest.fn().mockResolvedValue(undefined),
    releaseLock: jest.fn(),
  };
  return { ok: true, status: 200, body: { getReader: () => reader }, ... } as unknown as Response;
}
```

**Why**: jsdom v30 omits `ReadableStream`, `TextEncoder`, `TextDecoder`, and `fetch` from the global scope. The hand-rolled reader satisfies the `response.body.getReader()` contract used by `useSseStream` and completes deterministically per microtask tick — no fragile timing dependencies.

### Pattern 3 — TextDecoder polyfill for SSE-touching tests

Any test that exercises `useSseStream` end-to-end (where the production code calls `new TextDecoder()` to decode stream bytes) MUST polyfill `TextDecoder` in the global scope. Place this at the very top of the test file, before any imports:

```ts
import { TextDecoder as NodeTextDecoder, TextEncoder as NodeTextEncoder } from 'util';
if (typeof (globalThis as { TextDecoder?: unknown }).TextDecoder === 'undefined') {
  (globalThis as { TextDecoder: unknown }).TextDecoder = NodeTextDecoder;
}
if (typeof (globalThis as { TextEncoder?: unknown }).TextEncoder === 'undefined') {
  (globalThis as { TextEncoder: unknown }).TextEncoder = NodeTextEncoder;
}
```

**Why**: jsdom v30 leaves both globals undefined. Without the polyfill, `useSseStream`'s `fetchStream()` outer try/catch swallows the constructor error and surfaces it as an error banner — silently failing the streamDone → effect cascade.

**Better future state**: lift this polyfill into `jest.setup.js` so every test gets it. The R4 task 077 scope was test-file-only; that lift would be a clean follow-up (1-line move, no risk).

### Pattern 4 — Microtask flush AFTER user interaction, BEFORE waitFor

When asserting on side-effects of an SSE / fetch-driven action (e.g., `mockClearAll`):

```ts
await act(async () => { await userEvent.click(sendButton); });

// Production code calls fetchStream() WITHOUT awaiting — it's intentionally
// fire-and-forget so the UI doesn't block. Need to give React a chance to
// process the cascade: reader.read → setIsDone(true) → re-render → effect.
await act(async () => {
  await new Promise(r => setTimeout(r, 10));  // 10ms is enough with hand-rolled reader
});

await waitFor(() => expect(mockClearAll).toHaveBeenCalled(), { timeout: 3000 });
```

**Why**: production-correct SSE wiring uses `void fetchStream()` (fire-and-forget). The test must manually flush the cascade before `waitFor` polls.

---

## 4. Recommendation: which tests should eventually migrate to ui-test (Chrome integration)

None of the four R4 task 077 fixes are migration candidates — they're all clean unit tests now. However, the broader question of "which SprkChat tests should run in a real browser" arises:

| Test class | Recommendation |
|---|---|
| `useChatFileAttachment.test.ts` (all 20) | **Keep in Jest**. Pure hook logic — no DOM dependencies beyond `File`/`Blob`, which jsdom polyfills (per jest.setup.js task 071 work). |
| `SprkChat.attachments.test.tsx` (boundary tests 1-2) | **Keep in Jest**. Pure POST-body assertions via `mockFetch`. No real streaming required. |
| `SprkChat.attachments.test.tsx > calls clearAll on stream completion` | **Keep in Jest** with the hand-rolled reader + polyfill pattern from this fix. The pattern is more deterministic than a real browser would be, and avoids the cost of launching Chrome. |
| **Future SSE tests** that need realistic backpressure, large streams, or partial-event boundaries | **Migrate to ui-test (Chrome integration)**. jsdom's ReadableStream gap + jest's act-batching make real-stream behaviors near-impossible to fake. Use `ui-test` skill once the test surface grows beyond the canonical `done`-event flow. |

**Trigger for migration**: if a future SSE test needs to assert behavior across multiple `reader.read()` ticks with non-trivial bytes (e.g., a multi-event stream where token order matters), or needs to assert SSE error recovery under realistic network conditions, switch to `ui-test`. For single-event happy-path completion (the task 077 case), Jest + hand-rolled reader is fine.

---

## 5. Constraints honored

- **ADR-022**: React 19 patterns only. No regression to React 16 mocks. ✅
- **No production code modification**: ✅ Verified via `git diff --stat src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChat.tsx src/client/shared/Spaarke.UI.Components/src/components/SprkChat/hooks/ src/client/shared/Spaarke.UI.Components/src/hooks/useSseStream.ts` (all empty).
- **Test-content fixes only** (no escalations to production bugs identified): ✅ Both root causes were jsdom-globals gaps + a Jest matcher interaction with `act`. NOT production bugs — `useSseStream` works correctly under real browsers (where `TextDecoder`, `ReadableStream`, `TextEncoder`, and `fetch` are all defined).
- **No `.claude/`, no current-task.md, no TASK-INDEX.md updates**: ✅ Per operator directive.
- **Acceptance criteria**: ≤2 remaining failures was the target; achieved **0**. ✅

---

## 6. Acceptance vs target

| Acceptance criterion | Target | Result |
|---|---|---|
| `npm test` exits with ≤2 failures | ≤2 | **0** ✅ |
| 042 SprkChat.onAttachmentReady passes 6/6 | ✅ | 6/6 ✅ |
| 050 SprkChat.attachments boundary 3/3 | ✅ | 3/3 ✅ |
| 024 useChatFileAttachment all green | (implicit) | 20/20 ✅ |
| No production code modified | ✅ | ✅ (git diff --stat verified) |
| Reusable patterns documented | ✅ | This document §3 (4 patterns) |

---

## 7. Out-of-scope follow-up: lift polyfills to jest.setup.js

The `TextDecoder` / `TextEncoder` polyfill is currently inline at the top of `SprkChat.attachments.test.tsx`. Lifting it to `jest.setup.js` would benefit every test (including future SprkChat / SSE tests) but was out of scope for task 077 (test-file-only directive).

**Suggested follow-up** (1-line move):

```js
// In jest.setup.js — add to existing polyfill block (task 071 added File/Blob polyfills there):
const { TextDecoder, TextEncoder } = require('util');
if (typeof globalThis.TextDecoder === 'undefined') globalThis.TextDecoder = TextDecoder;
if (typeof globalThis.TextEncoder === 'undefined') globalThis.TextEncoder = TextEncoder;
```

Then remove the inline block from `SprkChat.attachments.test.tsx:16-25` and the explanatory comment block. Estimated risk: zero (additive polyfill; no existing test depends on the globals being undefined).

Defer to the next chat project or to a dedicated build-hygiene task.

---

*End of memo.*
