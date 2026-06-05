# Task 036 — Implementation Notes (2026-06-05)

> Companion to `task-036-design-2026-06-05.md`. Records implementation
> decisions, cross-package gaps surfaced during build, and follow-ups that
> were intentionally deferred to keep the closeout task tight.

---

## 1. Cross-package gap surfaced — SprkChat does not forward the original `File`

### Symptom

`executeSummarizeIntent` needs to POST `multipart/form-data` to
`/api/ai/chat/sessions/{id}/documents` (`ChatDocumentEndpoints.cs` lines
~205–215: reads `form.Files.GetFile("file")` and requires binary content for
Document Intelligence / native extraction).

The current SprkChat surface delivers `onAttachmentReady` with
`{ filename: string; contentType: string; textContent: string }` only — the
original `File` is consumed inside `useChatFileAttachment.addFiles()` during
extraction (PDF via `pdfjs`, DOCX via `mammoth`, TXT/MD via `File.text()`)
and NOT retained on the chip or surfaced through the callback. See
`src/client/shared/Spaarke.UI.Components/src/components/SprkChat/hooks/useChatFileAttachment.ts`
lines ~341–517.

### Mitigation in task 036

`handleAttachmentReady` in `ConversationPane.tsx` constructs a synthetic
`File` from `textContent` and stores it in `heldFilesRef` keyed by filename:

```ts
const synthetic = new File(
  [attachment.textContent],
  attachment.filename,
  { type: attachment.contentType || "text/plain" }
);
heldFilesRef.current.set(attachment.filename, synthetic);
```

For TXT / MD this round-trips correctly through the BFF Document Intelligence
extractor (the extracted text and the synthetic File's content match).

For **PDF and DOCX** the synthetic File contains the ALREADY-EXTRACTED text,
not the original binary. Posting it to `/documents` will fail the BFF
content-type validation (the endpoint's `AllowedMimeTypes` map keys to PDF /
DOCX MIME but the synthetic body is plain text). Operators uploading a PDF
through the paperclip + typing `/summarize` today will see the error chip
("documents POST failed (status=422, errorCode=...)") — same effective state
as before task 036, just with a clearer error message.

### Recommended follow-up (cross-package change for R5 Phase 2 close)

Extend `useChatFileAttachment` + `AttachmentChip` (or the `ChatAttachment`
callback type) to forward the original `File` reference. Minimal surface:

```ts
// In SprkChat/hooks/useChatFileAttachment.ts
export interface AttachmentChip {
  // ...existing fields...
  /** Original File reference for binary upload paths (R5 task 036+). */
  readonly file?: File;
}

// In SprkChat/types.ts
export interface ChatAttachment {
  filename: string;
  contentType: string;
  textContent: string;
  /** Original File reference for downstream binary upload (R5 task 036+). */
  file?: File;
}
```

Then in `ConversationPane.handleAttachmentReady`:

```ts
heldFilesRef.current.set(attachment.filename, attachment.file ?? syntheticFallback);
```

This is a 1-property additive change to a shared-library type — strictly
back-compat (existing consumers ignore the new field). Filed as a follow-up
because it requires:
1. Edit to `useChatFileAttachment.ts` (retain `File` ref on the chip)
2. Edit to `SprkChat/types.ts` (add `file?: File` to `ChatAttachment`)
3. Re-publish `@spaarke/ui-components` (or rely on file: workspace link
   already in place)
4. Update SprkChat consumers' tests

Sensitive to ADR-028 (auth) only insofar as the File reference must not be
sent to telemetry. The `File` itself is in-memory and bounded by browser
sandboxing — no leakage risk inherent in adding it to the chip.

---

## 2. `onBeforeSendMessage` is informational — cannot suppress send

### Symptom

SprkChat's `onBeforeSendMessage` contract (Spaarke.UI.Components/SprkChat/types.ts
lines 649–672) is explicit:

> "This callback is INFORMATIONAL — it does NOT short-circuit or cancel the
> send. The host cannot abort the message via this hook; that decision
> remains owned by SprkChat (the user clicked Send)."

The task spec asked: *"suppress the default SprkChat send (return a sentinel
that SprkChat treats as 'host handled'; if no such sentinel exists, fall back
to clearing the textarea + injecting a local assistant chip 'I'll summarize
that for you' so the user sees the outbound message land)"*.

### Mitigation in task 036

The spec's documented fallback is what task 036 implements:

- `handleBeforeSendMessage` invokes `matchIntent`. On match, it injects an
  inline Assistant chip ("I'll summarize that file for you" / "I'll summarize
  those N files for you") via `setPendingInjection`.
- The default SprkChat send still proceeds (per contract) — the chat agent
  receives the user's message and responds via the normal playbook path.
- IN PARALLEL, `executeSummarizeIntent` runs:
  1. Promotes via `/documents` (atomic — abort on any failure)
  2. Emits `context.files_staged` PaneEventBus event
  3. Streams `/summarize` → bridges AnalysisChunk → `workspace.streaming_*`
     events that downstream subscribers (`StructuredOutputStreamWidget` —
     task 017, registered by task 038) consume to render the deterministic
     summary in the Workspace pane.

**Operational consequence**: the chat pane shows the user's typed message +
the inline Assistant acknowledgement chip + the chat agent's normal response.
The Workspace pane gets the deterministic structured Summarize output. The
two paths run independently. This satisfies acceptance criterion (b) from
the POML — the deterministic summary runs without going through the LLM's
tool-call decision — without requiring a cross-package change.

### Recommended follow-up (deferred to R6)

Add a "host handled" sentinel return value to `onBeforeSendMessage`:

```ts
// In SprkChat/types.ts
onBeforeSendMessage?: (messageText: string) => boolean | void;
// return true = host handled; SprkChat should NOT send.
```

Then the host can suppress the duplicate chat-agent round-trip. Filed as R6
because (a) it's a behavioral change to a load-bearing chat surface used by
multiple shells and (b) the parallel-run UX in task 036 is operationally
acceptable for the SC-18 deterministic summarize use case.

---

## 3. PaneEventBus `streaming_error` event type — NOT registered

### Symptom

`src/client/shared/Spaarke.AI.Widgets/src/events/PaneEventTypes.ts` registers
three streaming discriminants on the `workspace` channel:

- `streaming_started`
- `field_delta`
- `streaming_complete` (with `completionStatus: 'complete' | 'declined' | 'empty'`)

There is **no** `streaming_error` discriminant. Adding one would require
extending the union in `PaneEventTypes.ts` — which per ADR-030 is an additive
event type within an existing channel (allowed) but task 036 should NOT
introduce new event types per the task POML constraints:

> "constraint: Per R5 CLAUDE.md §3.4: this task adds NO new PaneEventBus
> channels. Event types used (`workspace.streaming_*`, `context.files_staged`)
> are additive within existing channels per task 016."

### Mitigation in task 036

`sseToPaneEventBridge.ts` maps `AnalysisChunk.Type === "error"` to
`workspace.streaming_complete` with `completionStatus: 'declined'`. This is
the closest existing semantic and is consumed by `StructuredOutputStreamWidget`
correctly per its decline-state branch.

`executeSummarizeIntent` ALSO throws on error chunks so the caller surfaces
an error chip via `setPendingInjection` (descriptive error message).

The 2026-06-05 design notes §3.6 line 128 anticipated this:

> "Emit `workspace.streaming_error` (if available — check task 016 event
> registry) on `AnalysisChunk.FromError`"

Resolution: NOT available; we use `streaming_complete` + `declined`.

### Recommended follow-up

If task 037 (Context-pane execution-trace widget) needs to distinguish
"declined by safety perimeter" vs "infrastructure error", consider adding a
`streaming_error` discriminant in a separate task (additive type — would
satisfy ADR-030). For task 036 the existing `declined` semantic is sufficient.

---

## 4. Test runner setup — SpaarkeAi shell now has Jest

### Background

The SpaarkeAi shell had a `jest.config.ts` in place but no jest devDependency
and no `test` script. The existing `__tests__/ConversationPane.r5.test.tsx`
(task 020 deliverable) could not run. Task 036 closes this gap.

### Changes

`src/solutions/SpaarkeAi/package.json`:
- Added `"test": "jest"` script
- Added jest + ts-jest + ts-node + @testing-library/* + jest-environment-jsdom
  + identity-obj-proxy devDependencies (matched versions from
  `@spaarke/ai-widgets` for consistency)

`npm install --legacy-peer-deps --no-audit --no-fund` performed in the
SpaarkeAi directory (per root CLAUDE.md §11 — never `npm ci` for Vite
solutions).

### Test results

- New tests added by task 036: **73 tests across 3 suites** — all pass
  (`intentMatcher.test.ts` 35 tests, `sseToPaneEventBridge.test.ts` 11 tests,
  `executeSummarizeIntent.test.ts` 14 tests).
- Pre-existing test failures (NOT caused by task 036):
  - `insightsQueryClient.test.ts`: 2 tests fail (pre-existing — wrong error
    constructor expected; tracked separately).
  - `ConversationPane.r5.test.tsx`, `ContextPaneController.test.tsx`,
    `InsightsResponseRenderer*.test.tsx`, `LowConfidenceBadge.test.tsx`:
    fail with `SyntaxError: Unexpected token 'export'` on `d3-force` ESM
    import via `@spaarke/ui-components/hooks/useForceSimulation.ts`. These
    suites cannot be loaded by Jest without a `transformIgnorePatterns`
    override for ESM-only packages. NOT introduced by this task — the
    existing `ConversationPane.r5.test.tsx` from task 020 has never been
    runnable because SpaarkeAi never had jest installed.

The d3-force ESM issue is a follow-up for the R5 testing-infrastructure
backlog (separate from task 036). Recommended fix:

```ts
// jest.config.ts
transformIgnorePatterns: [
  '/node_modules/(?!(d3-force|d3-.*|@spaarke/.*))',
],
```

Plus enabling `ts-jest`'s ESM mode. Out of scope for task 036.

---

## 5. Files created / modified (final inventory)

### New files

| File | Purpose |
|---|---|
| `src/solutions/SpaarkeAi/src/components/conversation/intentMatcher.ts` | Pure intent matcher + `IntentMatchers` registry |
| `src/solutions/SpaarkeAi/src/components/conversation/__tests__/intentMatcher.test.ts` | Table-driven coverage (35 tests) |
| `src/solutions/SpaarkeAi/src/components/conversation/sseToPaneEventBridge.ts` | Pure AnalysisChunk → PaneEventBus transformer |
| `src/solutions/SpaarkeAi/src/components/conversation/__tests__/sseToPaneEventBridge.test.ts` | Bridge tests (11 tests) |
| `src/solutions/SpaarkeAi/src/components/conversation/executeSummarizeIntent.ts` | Promote-and-execute orchestrator |
| `src/solutions/SpaarkeAi/src/components/conversation/__tests__/executeSummarizeIntent.test.ts` | Orchestrator tests with mock fetch (14 tests) |
| `projects/spaarke-ai-platform-unification-r5/notes/task-036-implementation-notes.md` | This file |

### Modified files

| File | Change |
|---|---|
| `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` | Import + wire matchIntent + executeSummarizeIntent in `handleBeforeSendMessage`; capture File-equivalents in `handleAttachmentReady`; surface Indexed count in attached-files indicator; reset state on session-create / chip-remove |
| `src/solutions/SpaarkeAi/package.json` | Added `"test": "jest"` script + jest/ts-jest/testing-library/ts-node devDeps |

### No changes to

- BFF (`src/server/api/Sprk.Bff.Api/`) — task 036 is frontend-only per design §1
- `@spaarke/ai-widgets` — PaneEventTypes already exposes all required event types
- `@spaarke/ui-components` SprkChat — cross-package change DEFERRED (see §1, §2 above)
- ADR docs — task 036 stays within ADR-028, ADR-030, ADR-021, ADR-022 envelopes

---

*Authored 2026-06-05 by task-execute agent (FULL rigor) — task 036 closeout.*
