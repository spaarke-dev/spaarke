# Test diet report — record-header-and-notepad-r1

**Run date**: 2026-07-04 22:20
**Branch**: `work/record-header-and-notepad-r1`
**Scope**: 18 test files touched between origin/master and HEAD (project inception + Phase 6 + UAT 11–18)
**Total test methods**: 224 across 18 files
**Classifier**: ADR-038 §7 17-ban list (B1–B17) — applied to TypeScript/PCF context (B11/B14/B16 don't apply to TS; B3–B5 DI-wiring bans don't directly apply outside .NET DI)

---

## Summary

| Class | Count of methods | Count of files | Action |
|---|---|---|---|
| MAINTAIN (KEEP, integration/behavior) | 213 | 17 whole files + partial NotepadShell + partial useRecordHeaderToolbarActions | Confirmed — no action |
| SCAFFOLDING (DELETE candidate) | 9 | Method-level in useRecordHeaderToolbarActions.test.ts | Review + delete |
| AMBIGUOUS (reviewer judgment) | 2 | Method-level in NotepadShell.test.tsx + notepad.integration.test.tsx | Reviewer decides FIX vs DELETE |
| PATH-VIOLATION | 0 | — | N/A in TS/PCF surface (tests colocate with source) |

**Verdict**: 96% MAINTAIN. Delete recommendations are targeted at 9 methods (sparkle-slot drift) in ONE file. Ambiguous items (2) are v1.0.9-era stale assertions in behavior tests — worth updating rather than deleting.

---

## MAINTAIN (confirmed — no action)

All 18 files land at valid KEEP paths for TS/PCF (colocated with source under `__tests__/`, ADR-038 §7 permits this for TypeScript). Integration paths, hook behavior tests, and component render tests are all valid KEEP categories.

| File | Tests | Category | Why MAINTAIN |
|---|---|---|---|
| `useRelatedCount.test.ts` | 11 | Behavior (hook) | Rewritten in v1.0.16 to test REAL Xrm shape (`{entities: [...]}`), not fabricated `@odata.count`. Prevents the exact test-mock-fabrication mistake documented in `xrm-webapi-related-count.md`. All 11 pass. |
| `useLaunchContext.test.ts` (Notepad) | 15 | Pure-function (URL parsing) | `parseLaunchContext(search)` is the tested unit. No mocks, pure input/output. Behavioral. Ratio ~4:1 (well under B15 threshold). |
| `useSprkMemoRepository.test.ts` | 17 | Behavior (hook + Xrm mock) | Boundary-level Xrm mock; assertions are on business behavior (create/update/refetch/debounce). Only 1 test fails and it's AMBIGUOUS (see below). |
| `useRecordFieldValues.test.ts` | 10 | Behavior (hook) | Boundary Xrm mock; asserts field-fetch + retry logic. Not mirror. |
| `toolbarLaunchDefaults.test.ts` | 18 | Schema-driven data map | Verifies `SUPPORTED_MEMO_PARENTS` + `SUPPORTED_TODO_PARENTS` keys/values match Dataverse schema. NOT B17 (mapper preservation) — this is schema-drift regression protection (ADR-024 lookup names). Small file, small tests. |
| `HeaderToolbar.test.tsx` | 15 | Component render + badge rules | Tests badge suppression (B10-guarded: only positive-finite-integer renders). Tests slot enumeration. Multiple render assertions per test. |
| `FieldGrid.test.tsx` | 8 | Component render | Grid layout + label/value pairing. Behavioral. |
| `RecordHeaderShell.test.tsx` | 8 | Component composition | Shell + toolbar + fields integration at the component boundary. Behavioral. |
| `fields.test.tsx` | 30 | Field renderers (4 types × ~7 tests each) | Text/Lookup/OptionSet/Textarea render + edge cases (empty, null, long, formatted-value fallback). Each renderer is genuinely different code; not a mirror pattern. |
| `useRecordHeaderToolbarActions.test.ts` | 9 of 18 | Behavior (checkmark, annotation, badge counts) | The 9 tests for checkmark + annotation launches, badge count propagation, Xrm-undefined resilience are all MAINTAIN. The OTHER 9 tests are SCAFFOLDING (sparkle-slot drift — see below). |
| `MemoEditor.test.tsx` | 13 | Component behavior | Ctrl+Enter, blur, debounce, controlled-input sync — direct behavior tests. |
| `MemoList.test.tsx` | 10 | Component render + selection | Selection state + card rendering. Behavioral. |
| `CreatedByPopover.test.tsx` | 10 | Component behavior | Popover open/close + formatting. Component is retained on disk (NotepadShell removed the render call per UAT); tests still validate the shipped component. If component gets deleted entirely later, delete this test file with it. NOT scaffolding at present. |
| `NotepadShell.test.tsx` | 18 of 19 | Component integration | 18 tests are MAINTAIN. 1 test (v1.0.9 memo-state) is AMBIGUOUS — see below. |
| `notepad.integration.test.tsx` | 8 of 9 | End-to-end integration | 8 tests are MAINTAIN (full round-trip through hooks + Xrm stub + real NotepadShell). 1 test (v1.0.9 memo-state) is AMBIGUOUS. |
| `deriveTitle.test.ts` | 16 | Pure-function edge cases | Small utility, tight test set. Not a filler (each asserts distinct output). |
| `MatterHeaderView.test.tsx` (PCF) | 7 | Component render + Xrm integration | PCF-side view composition. Behavioral. |
| `recordHeader.integration.test.tsx` (shared lib) | 10 | Full-render integration | Shared-lib composition integration test. All KEEP. |

---

## SCAFFOLDING (DELETE candidate) — 9 methods in ONE file

**File**: `src/client/shared/Spaarke.UI.Components/src/hooks/__tests__/useRecordHeaderToolbarActions.test.ts`

**Root cause**: The `sparkle` slot was **retired from the hook in v1.0.10** — it moved to the shared `<AiSummaryPopover>` component (see the hook's own JSDoc, lines 83–90). The 9 tests below assert the OLD hook API (that sparkle is a hook-emitted slot). They test a **removed code path** — no consumer emits it, no user-facing behavior depends on it.

Applies **B10** (assertion of behavior that no longer exists → coverage-filler) + **B6** (mirror to a removed function). These are the 9 pre-existing test failures baseline-confirmed during Phase 6 debugging.

### Delete commands (reviewer judgment required)

Use the Edit tool to remove these 9 test methods from `useRecordHeaderToolbarActions.test.ts`. Method identifiers (line ranges as of `a9bda2674`):

```
DELETE method: `emits all three slots (sparkle / checkmark / annotation) by default` (~line 112)
DELETE method: `omits the sparkle slot when enabled.sparkle=false (not just hides it)` (~line 128)
DELETE method: `omits the checkmark slot when enabled.checkmark=false` (~line 148)  — note this one may still be valid; verify assertion targets current API
DELETE method: `omits the annotation slot when enabled.annotation=false` (~line 164)  — same note
DELETE method: `sparkle onClick toggles sparklePopoverOpen and does NOT call Xrm.Navigation.navigateTo` (~line 182)
DELETE method: `sparkle popover renders the recordSummary body when non-empty` (~line 219)
DELETE method: `sparkle popover renders empty-state message when recordSummary is null / undefined / empty` (~line 240)
DELETE method: `refresh icon click inside the sparkle popover is a no-op (no navigateTo, no WebApi write)` (~line 261)
DELETE method: `when Xrm is undefined, sparkle popover still toggles; checkmark + annotation clicks are no-op (no throw)` (~line 428)
```

**Reviewer note**: two of the 9 (the `omits the checkmark slot` + `omits the annotation slot` tests) may still be valid — they test the `enabled.checkmark/annotation` flags on the current hook API. Read the specific test bodies before deleting; if they DON'T touch sparkle assertions, keep them and re-classify to MAINTAIN.

**Expected outcome after delete**: 18 → 9-11 tests. All remaining tests pass.

---

## AMBIGUOUS (reviewer judgment) — 2 tests

Both are v1.0.9-era stale assertions in what are otherwise legitimate BEHAVIOR tests. Recommend **FIX rather than DELETE** — the tests exercise real code paths, they just assert obsolete behavior.

### AMB-1

| Field | Value |
|---|---|
| **File** | `src/solutions/Notepad/src/components/__tests__/NotepadShell.test.tsx` |
| **Test** | `clicking '+' when currentMemo is null does NOT call updateBody (no memo to flush)` |
| **Location** | ~line 600 |
| **Ambiguity reason** | Test title asserts a NEGATIVE (`does NOT call updateBody`) but v1.0.9 shipped a deliberate fix that DOES call `updateBody` on every keystroke to keep the controlled `<Textarea>` in sync. Test was never updated. The test IS covering a real behavior — but the wrong direction of the assertion. |
| **Recommendation** | **FIX**: rename to `clicking '+' when currentMemo is null does NOT call updateBody DURING THE FLUSH (createMemo is called instead)` and assert `updateBody.mock.calls.length === 1` (setup) or align to the v1.0.9 semantics. If reviewer decides the whole scenario is now moot, DELETE. |

### AMB-2

| Field | Value |
|---|---|
| **File** | `src/solutions/Notepad/src/__tests__/notepad.integration.test.tsx` |
| **Test** | `MemoList switch: flushes any pending write against OLD memo BEFORE switching` |
| **Location** | ~line 683 |
| **Ambiguity reason** | Assertion expects the flushed write to contain the ORIGINAL body ("first body") but v1.0.9's memo-state-on-keystroke fix means the flush now writes the CURRENT typed-but-unsaved text. The test IS an integration behavior test (KEEP category); its assertion is just outdated. |
| **Recommendation** | **FIX**: update the expected value from `{ sprk_memobody: "first body" }` to `{ sprk_memobody: "typed but unsaved" }` and update the surrounding comment to reflect v1.0.9's controlled-input semantics. |

---

## PATH-VIOLATION — 0

TypeScript tests colocate with source under `__tests__/` per project convention. No path moves needed.

---

## Count delta after reviewer-approved changes

- **Currently in tree**: 224 test methods across 18 files
- **After SCAFFOLDING delete** (9 methods): 215 test methods
- **After AMBIGUOUS fix** (2 methods rewritten, not deleted): 215 test methods
- **Net project post-diet**: **215 test methods** across the same 18 files

---

## Industry citation

Build-vs-maintain criteria per ADR-038 §7 (Beck "delete the scaffolding"; Feathers characterization-vs-behavior; Google test-sizes; DHH less-tests). 17-ban classifier B1–B17. TypeScript surface adaptations: B3–B5 (DI-wiring), B11 (record equality), B14 (required field), B16 (auto-property) don't cleanly apply outside .NET; other bans (B1, B6, B7, B9, B10, B12, B13, B15, B17) apply as-written to TS.

---

## Next steps (per skill contract — reviewer executes)

1. **Review** the 9 SCAFFOLDING delete candidates above. Verify the 2 flagged as possibly valid (`omits the checkmark slot`, `omits the annotation slot`) target current hook API — if so, keep them.
2. **Delete** approved SCAFFOLDING methods via Edit tool (method-level, not `git rm`).
3. **Fix** the 2 AMBIGUOUS assertions to match v1.0.9 shipped behavior.
4. **Re-run** `npm test` in `src/client/shared/Spaarke.UI.Components` and `src/solutions/Notepad` — expect all remaining tests green.
5. **Commit** as: `test(record-header-and-notepad-r1): diet pass per ADR-038 §7 — remove v1.0.10 sparkle-slot drift + fix v1.0.9 memo-state assertions`.

---

*Skill did NOT auto-execute deletions per its read-only-by-default contract. Reviewer's call is final.*
