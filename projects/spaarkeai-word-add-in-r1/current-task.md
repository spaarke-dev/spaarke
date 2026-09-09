# Current Task

## Quick Recovery

| Field | Value |
|---|---|
| **Task** | 010 — FR-04: consolidate onto one Word adapter reached via HostAdapterFactory |
| **Task File** | `tasks/010-adapter-consolidation-word.poml` |
| **Phase** | 1 Foundation |
| **Status** | in-progress |
| **Started** | 2026-09-09 |
| **Rigor** | FULL · model-tier opus · effort xhigh · steps mode **PRESCRIPTIVE** |
| **Next Action** | POML step 2 — permanent unit tests in WordAdapter.test.ts. (was: step 1 — port `getCompressedFile()` from `word/WordHostAdapter.ts:171-220` into `shared/adapters/WordAdapter.ts`, replacing the `body.getOoxml()` path at `:221-270`.) |

## Critical Context

Finding F-e: FR-04 as written REGRESSES the .docx save. Order is load-bearing:
port extraction -> register -> route -> **verify Outlook** -> delete `word/WordHostAdapter.ts`.
Deleting before the Outlook verification is FORBIDDEN (removes the reference impl).

## Baselines measured at task start (2026-09-09, before any edit)

- `npx tsc --noEmit --skipLibCheck`: **289 total errors, 0 in production (non-test) files**. Must stay 0.
- `npx jest shared/adapters`: **2 suites failed, 10 tests failed / 19 passed / 29 total**.
  - `OutlookAdapter.test.ts` — **suite fails to RUN** at baseline: `TypeError: Cannot read properties of
    undefined (reading 'Importance')` at its line 36 (`Office.MailboxEnums.Importance.Normal`). Pre-existing
    Office.js mock gap (one of the two named in task 017's notes). NOT caused by this task.
  - `WordAdapter.test.ts` — 10 failures, all pre-existing: 9 caused by the suite-local `Word.run` mock
    (`async cb => { await cb(ctx); }`) discarding the callback's return value, so every `Word.run`-based
    adapter method resolves `undefined`; 1 (`insertLink` escape test) asserts `&#39;` in a string that
    contains no apostrophe.
- Known-red jest suite repo-wide: 12/21 suites (root-caused to a `useAnnounce.ts` production defect). Out of scope.
- 289 test-file typecheck errors are CONSCIOUSLY ACCEPTED (operator decision 2026-09-09). Do not clear.

## Completed Steps

- [x] 0 Rigor declared; read spec FR-04, plan F-e, project CLAUDE.md, module CLAUDE.md, both adapters
      end to end, HostAdapterFactory, IHostAdapter, types.ts, both taskpane entry points, the Office.js
      mock, and both adapter test files. ADRs 049/028/012/021 loaded.
- [x] 1 Ported getCompressedFile() + the :126-134 header comment into shared/adapters/WordAdapter.ts,
      replacing the body.getOoxml() ooxml branch. Production typecheck still 0 / total still 289.
- [x] 1.5 BYTE-IDENTICALITY PROVEN via a throwaway differential harness
      (shared/adapters/__tests__/zz-task010-parity.test.ts, DELETE AT STEP 6): 36/36 pass.
      26 real .docx from tests/fixtures/compose-corpus (all < 64 KB -> 1 slice) + 7 synthetic
      PK-prefixed multi-slice payloads (2, 5, 64, 65 slices; boundary +-1). Every case:
      byteLength equal to ref AND to source, Buffer.equals(ref)=true, sha256 equal, head = 50 4b 03 04,
      identical ascending slice order, identical getFileAsync args ('compressed', {sliceSize:65536}),
      handle closed exactly once. Plus a negative control proving equal-length is NOT proof.
- [x] 1.6 Full-suite baseline re-measured (excluding the harness): 12 failed / 9 passed / 21 suites;
      92 failed / 237 passed / 329 tests — identical to task 017's record.

## Files Modified


- `src/client/office-addins/shared/adapters/WordAdapter.ts` — ported .docx extraction
- `src/client/office-addins/shared/adapters/__tests__/zz-task010-parity.test.ts` — THROWAWAY, delete at step 6

## Decisions Made

(none yet)
