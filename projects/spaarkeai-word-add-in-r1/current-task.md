# Current Task

## Quick Recovery

| Field | Value |
|---|---|
| **Task** | 010 — FR-04: consolidate onto one Word adapter reached via HostAdapterFactory |
| **Task File** | `tasks/010-adapter-consolidation-word.poml` |
| **Phase** | 1 Foundation |
| **Status** | complete — pending one operator decision (notes §6c) |
| **Started** | 2026-09-09 |
| **Rigor** | FULL · model-tier opus · effort xhigh · steps mode **PRESCRIPTIVE** |
| **Next Action** | 🔔 **OPERATOR DECISION REQUIRED** — host-detection option A / B / C, see `notes/010-adapter-consolidation.md` §6c. Implementation is complete and verified; both Step 9.5 gates passed (0 ADR violations) with this as the one open item. |

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

## Steps 2-8 complete (2026-09-09)

- [x] 2 Permanent .docx tests in `WordAdapter.test.ts` (8 new) + `setupWordCompressedFile` /
      `createMockDocxBytes` in `shared/__mocks__/office-js.ts` + `Office.FileType` and
      `Office.MailboxEnums` added to `jest.setup.js`. Also repaired two harness defects in
      `WordAdapter.test.ts` (the `Word.run` mock discarded the callback's return value; the
      `insertLink` escape test asserted an entity for a character absent from the input).
- [x] 3 `registerAdapter('word', WordAdapter)` / `registerAdapter('outlook', OutlookAdapter)` at the
      two entry points, before any `create()` call.
- [x] 4 Both panes now `await HostAdapterFactory.createAndInitialize()` with NO host argument, so
      `detectHostType()` is genuinely live (see Decisions).
- [x] 5 OUTLOOK VERIFIED UNREGRESSED -> gate passed, proceeded to delete.
- [x] 6 DELETED `word/WordHostAdapter.ts` + the throwaway parity harness. Zero code references remain.
- [x] 7 `src/client/office-addins/CLAUDE.md` "Host abstraction" row updated; plus stale pointers in the
      architecture doc, two e2e page objects, and `knowledge/sharepoint-embedded/NOTES.md`.
- [x] 8 typecheck 289 total / 0 production (EXACTLY baseline); `npm run build` (production, with the 4
      required env vars from `deploy-office-addins.yml`) exit 0; full jest run recorded.

## Result deltas vs baseline

| Metric | Baseline | After | Note |
|---|---|---|---|
| typecheck total | 289 | 289 | unchanged |
| typecheck production | 0 | 0 | unchanged |
| jest suites | 12F / 9P / 21 | 11F / 11P / 22 | WordAdapter F->P; +1 new factory suite (P) |
| jest tests | 92F / 237P / 329 | 100F / 279P / 379 | decomposed below |

Test delta fully decomposed (no unexplained movement):
- WordAdapter suite: 29 -> 36 tests (+7 new .docx tests), 10 failures -> 0 (-10).
- HostAdapterFactory suite: new, +14 tests, all passing.
- OutlookAdapter suite: +29 tests newly REACHABLE (18F / 11P). That suite previously crashed at import
  ("Cannot read properties of undefined (reading 'Importance')") and contributed 0 tests to the total.
- Failures: -10 + 18 = +8. Tests: +7 + 14 + 29 = +50. Both match the observed numbers exactly.
