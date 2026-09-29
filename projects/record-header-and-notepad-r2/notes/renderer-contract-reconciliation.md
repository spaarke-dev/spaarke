# Renderer contract reconciliation — task 015 (FR-10)

> **Task**: `015-renderer-barrel-and-contract-tests`
> **Date**: 2026-08-25
> **Suite**: `src/client/shared/Spaarke.UI.Components/src/components/RecordHeader/__tests__/rendererContract.test.tsx`
> **Result**: **91/91 green on the first run.** Two prop-shape conformance fixes were required; **zero behavioral violations** were found.

---

## 1. Verdict

The shared FR-10 contract suite runs its identical assertion set against all **six** editable
RecordHeader renderers — `TextField`, `TextareaField`, `OptionSetField`, `DateField`,
`NumberField`, `BooleanField`. No adapter is skipped and no assertion is weakened per-renderer.

**Behavioral conformance: clean.** Every renderer already obeyed the TextField contract verbatim —
self-applied `gridColumn`, the `null`/`undefined`/`''` em-dash triple, the
`onSave && !disabled` editable gate, exactly-one-`onSave`-per-commit, Escape-cancels-with-zero-saves,
blur-commits, and the load-bearing negative: **a rejected save reverts the draft AND leaves the
component IN edit mode with a spinner while pending.** Nothing had to be renegotiated, and the
escalation triggers in the POML never fired.

**Prop-shape conformance: two fixes.** See §2.

---

## 2. Conformance fixes applied

Both are the same fix in two files, and both are **behaviorally inert** — an optional prop is
declared, never destructured, and never rendered. No existing consumer changes behavior; no shipped
public behavior was altered. (Had a fix required changing shipped behavior, the POML escalation
trigger would have applied — it did not.)

| # | File | Change | Catching assertion |
|---|---|---|---|
| 1 | `src/client/shared/Spaarke.UI.Components/src/components/RecordHeader/fields/TextareaField.tsx` | Added `required?: boolean` to `ITextareaFieldProps`, documented as accepted-but-inert per D-10 | `renders the "*" required marker only for TextField (D-10)` |
| 2 | `src/client/shared/Spaarke.UI.Components/src/components/RecordHeader/fields/OptionSetField.tsx` | Added `required?: boolean` to `IOptionSetFieldProps`, same D-10 wording | same |

**Why this was a real gap, not test convenience.** FR-10 names *props shape* as part of the
contract, and D-10 fixes the `*` marker as TextField-only — which is a statement about all six
renderers, not four. `DateField`, `NumberField`, and `BooleanField` (R2 tasks 010–012) each already
declared `required?: boolean` for exactly this parity reason and documented it as inert. The two R1
renderers had simply never been brought forward. Without the prop, the D-10 negative could only be
asserted for four of six renderers, or asserted through a cast that lies about the contract. The
suite now asserts it honestly for all six.

---

## 3. Documented per-renderer allowances (suite parameters, never skips)

These are the **only** permitted deviations. Each is a difference in *gesture*, not in *semantics*,
and each is encoded as an adapter parameter — no test is skipped for any renderer.

| Renderer | Allowance | Rationale |
|---|---|---|
| `TextareaField` | Commits on **Ctrl/Cmd+Enter**; plain Enter inserts a newline | Shipped R1 behavior; changing it would break multiline composition. POML names it explicitly. |
| `OptionSetField` | Commits on **option-selection** in the Dropdown | POML-documented. Consequence: it is the suite's only *immediate-commit* adapter — its draft-change gesture and commit gesture are the same act, so no separable pending draft exists. See §4. |
| `DateField` | Driven in `format="datetime"` so the time-of-day input supplies a genuinely pending draft | Calendar-day selection commits immediately by design (task 010's form-buffer-dirty-on-selection decision); that path stays covered by `DateField.test.tsx`. Using the time input lets DateField satisfy the full explicit Enter/Escape/blur set without weakening anything. |
| `BooleanField` | Stages its draft by toggling the `Switch`, commits on Enter/blur | Toggling *is* this renderer's "typing". |
| All six | Accept `required`; only `TextField` renders `*` | D-10. Asserted as a negative for the other five. |

---

## 4. The one branch in the suite (and why it is not a skip)

`OptionSetField` is the only adapter with `stageDraft: undefined`. Because selecting an option
commits immediately, a blur can never carry a pending draft for that renderer — asserting
"blur commits a changed draft" there would be asserting something the contract does not, and cannot,
promise.

The blur test therefore branches: five renderers assert *blur commits the staged draft with the
expected payload*; `OptionSetField` asserts *blur is wired to commit and a no-change blur exits edit
mode cleanly without firing a spurious save*. Both branches assert blur for their renderer — the
test exists and runs for all six. This is the "suite parameter, not skipped test" shape the task
required.

---

## 5. Deliberate scope exclusion

The RecordHeader `LookupField` (`fields/LookupField.tsx`, barrel-aliased `RecordHeaderLookupField`)
is **out of this suite's scope**: it is a display-only navigation renderer with a non-scalar value
shape (`ILookupFieldValue`) and no `onSave`, so it has no edit contract to assert. Its behavior stays
covered by `fields.test.tsx`. This is stated in the suite's header comment. Not to be confused with
the unrelated editable `components/LookupField/`.

---

## 6. Barrel wiring

- `fields/index.ts` — appended `BooleanField`, `DateField`, `NumberField` (+ `IBooleanFieldProps`,
  `IDateFieldProps`, `INumberFieldProps`, `NumberFieldKind`) in the file's documented
  one-export-line-plus-one-type-line-per-renderer shape, alphabetically. No existing line reordered
  or restructured.
- `components/RecordHeader/index.ts` — explicit named re-exports for the same six symbols. **No
  `export *` introduced.** A repo-wide grep of every `index.ts` under `src/` confirmed none of the
  six names collides with an existing export, so all three re-export **un-aliased** — no second
  `RecordHeaderLookupField`-style alias was needed.

### Non-blocking observation (not fixed — out of scope)

`IOptionSetFieldOption` (task 013's option-list type) is still **not** exported from either barrel,
so a consumer typing an `options` array by name must deep-import from `../fields/OptionSetField`.
Adding it would mean editing an existing export line, which this task's barrel constraint forbids
("do not reorder or restructure existing export lines"). Flagged for whichever resolver task first
needs the named type. The contract suite works around it with an inline literal.

---

## 7. Verification evidence

| Check | Result |
|---|---|
| `rendererContract.test.tsx` | **91 passed / 91**, 0 skipped |
| All 9 `RecordHeader/__tests__` suites | **232 passed / 232** (141 pre-existing + 91 new) |
| `npx tsc --noEmit` (shared lib) | exit 0 |
| `npm run build` (shared lib) | exit 0 |
| `__tests__/fields.test.tsx` | untouched — empty `git diff` |
| `package.json` (NFR-08 / DEF-06) | untouched — empty `git diff`; no `exports` map added; `moduleResolution` unchanged; `sideEffects: false` left intact |
| MatterHeader PCF `bundle.js` (NFR-02) | **62,114 bytes** — 25% of the 250 KB ceiling; `react-datepicker-compat` occurrences in bundle: **0** (fully tree-shaken) |

### NFR-02 bundle note

Adding `DateField` to the barrel was measured pre-task at **353,250 bytes** (+448%) because the
shared lib declared no `sideEffects` field, so webpack could not prune the unreachable
`@fluentui/react-datepicker-compat` re-export. With `"sideEffects": false` in place (see
`notes/decisions/wave1-sideeffects-tree-shaking.md`) the same build lands at **62,114 bytes** —
*below* the 64,422-byte pre-barrel baseline. The `sideEffects` field was neither removed nor
altered by this task. `grep -c react-datepicker-compat out/controls/control/bundle.js` returns 0,
which is the direct evidence that the barrel re-export is being pruned rather than merely compressed.

---

## 8. Pre-existing red test, unrelated to this task

`src/client/shared/Spaarke.UI.Components/src/__tests__/recordHeader.integration.test.tsx` fails
(8 of 10 cases) on `getByRole('button', { name: 'AI Summary' })`. **This predates this project.**

Evidence: the sparkle slot was retired from `useRecordHeaderToolbarActions` at v1.0.10, and
`git show HEAD:...useRecordHeaderToolbarActions.ts | grep -c aiSummary` returns **0** — the hook has
not produced a `toolbarProps.aiSummary` since then, while `HeaderToolbar.tsx:157` gates the
"AI Summary" button on exactly that prop. The integration test also still destructures
`sparklePopoverOpen` / `sparklePopoverContent`, which the hook's own JSDoc documents as removed at
v1.0.10. The test was never updated for that contract change.

Not fixed here — it is outside task 015's scope and the file is in the blast radius of task 024,
which is concurrently rewriting that hook (FR-16 slot auto-hide + FR-24 agreement parents). Worth a
deferral entry.

### Full-suite caveat

The acceptance criterion "the FULL `npm test` exits 0" could not be met on this branch, for two
reasons that are both independent of this task:

1. the pre-existing `recordHeader.integration.test.tsx` failure documented above; and
2. two other agents (tasks 021 and 024) were writing to this same worktree during execution —
   `useRecordHeaderToolbarActions.ts`, `toolbarLaunchDefaults.ts`, `xrmContext.ts`,
   `FieldMappingHandler.ts`, and `MatterHeaderView.tsx` all carried uncommitted in-flight edits.
   Two consecutive full-suite runs six minutes apart failed **different** suite sets, which is the
   signature of concurrent mutation plus timeout flake under load, not of a stable regression.

Task 015's own scope is fully verified green: 232/232 across every `RecordHeader` suite, clean
`tsc`, clean `npm run build`. The full-suite gate should be re-run by the main session once tasks
021/024 have landed and the pre-existing integration-test failure is triaged.
