# Matter header parity baseline — task 002

> ## ⚠️ THE PARITY TARGET IS **v1.0.21**, NOT v1.0.20
>
> Task 002 was written to capture v1.0.20. **v1.0.20 is no longer in the environment.** `pac solution list`
> on `spaarkedev1` (2026-08-31) reports `MatterHeaderPcf 1.0.21.0` — task 040's RS-1 hotfix was built AND
> deployed. This is **escalation path (a)** in the task's own `<escalation>` block, which anticipated exactly
> this and directs: *"record in the baseline doc that the parity target became v1.0.21 = v1.0.20 minus the
> broken `$select` entry."* Done here. **Task 080 diffs against v1.0.21.**
>
> Consequence: the RS-1 HTTP 400 is already fixed in the deployed control, so the runtime capture should
> succeed rather than being blocked. The second escalation trigger (control missing from the form) did
> **not** fire — the form still binds `Spaarke.Records.MatterHeader`, verified below.

> **Status**: code half ✅ · form half ✅ · **runtime half ⏳ needs an operator browser session**
> **Environment**: `spaarkedev1` only — never `spaarke-model1-prod`
> **Captured**: 2026-08-31, read-only (`pac solution list`, `pac org fetch`, source read). No source, solution,
> or form was modified.

---

## Source ≡ deployed (why the code half is trustworthy)

`ControlManifest.Input.xml` declares `version="1.0.21"`; the environment has `1.0.21.0`. The working tree
**is** the deployed build, so the code-derived facts below describe what users actually see.

This is what makes the baseline recoverable despite R2 having changed `MatterHeader/**` — task 002 step 0
tells you to stop if that happened, but its own context (updated 2026-08-27) resolves the tension: the
baseline is of the **deployed control**, and the deployed control matches this source.

---

## Code half — from `MatterHeaderView.tsx` + `ControlManifest.Input.xml`

**Control identity**: namespace `Spaarke.Records`, constructor `MatterHeader`, version **1.0.21**
**Platform libraries**: React 16.14.0, Fluent 9.46.2

### The 5-field layout, in render order

| # | logical name | span | renderer |
|---|---|---:|---|
| 1 | `sprk_matternumber` | 1 | TextField — **required** |
| 2 | `sprk_mattername` | 2 | TextField |
| 3 | `sprk_mattertype` (reads `_sprk_mattertype_value`) | 1 | **editable** `components/LookupField` |
| 4 | `sprk_practicearea` (reads `_sprk_practicearea_value`) | 1 | **editable** `components/LookupField` |
| 5 | `sprk_matterdescription` | 3 | TextareaField |

Spans are hand-rolled `gridColumn` wrapper divs — the shipped `LookupField` had no `span` prop at the time.

### `$select` list (post-RS-1)

```
sprk_matternumber, sprk_mattername, _sprk_mattertype_value,
_sprk_practicearea_value, sprk_matterdescription, RECORDSUMMARY_FIELD
```

**The defective `sprk_mattersummary` entry is GONE** — that is the whole of the v1.0.20 → v1.0.21 delta.
The summary column is now reached only through the shared `RECORDSUMMARY_FIELD` constant.

### `LOOKUP_META` — still present, and that is expected

`MatterHeaderView.tsx:86–94` still hard-codes the two lookup triples:

- `sprk_mattertype` → `sprk_mattertype_ref` / `sprk_mattertype_refid` / `sprk_mattertypename`
- `sprk_practicearea` → `sprk_practicearea_ref` / `sprk_practicearea_refid` / `sprk_practiceareaname`

Spec criterion 16 ("grep for `LOOKUP_META` returns nothing") is satisfied when **task 081 deletes this
control**, not by task 080. The new header resolves these from metadata instead. Do not treat the constant's
presence here as a task-080 failure.

---

## Form half — live-verified, not assumed

Form: **Matter main form** `4fa382f2-c273-f011-b4cb-6045bdd6a665` (never the legacy *Information* form).

| property | value |
|---|---|
| control | `Spaarke.Records.MatterHeader` ✅ still bound |
| `boundField` | **`sprk_matternumber`** (SingleLine.Text) — matches the manifest comment |
| `title` | `MATTER INFORMATION` (static) |
| `showVersion` | `true` — so the footer is visible for the swap check |
| form factors | **3** copies, params identical across all three |

Task 080 must re-enter all three of these properties and edit all three form factors.

---

## ⏳ Runtime half — what still needs capturing (operator)

Open a populated Matter record on the main form in `spaarkedev1`. Save screenshots under
`projects/record-header-and-notepad-r2/notes/baseline/`.

**Capture each in BOTH light and dark** (parity criterion 15 is assessed in both):

| # | state | filename |
|---|---|---|
| 1 | full header, all 5 fields populated | `matter-header-{light,dark}.png` |
| 2 | sparkle popover open | `sparkle-popover-{light,dark}.png` |
| 3 | To Do + Notepad badge states | `toolbar-badges-{light,dark}.png` |
| 4 | Matter Type lookup mid-interaction | `lookup-typeahead-{light,dark}.png` |
| 5 | inline text edit, form dirty, no flash | `inline-edit-dirty-{light,dark}.png` |
| 6 | version footer reading **v1.0.21** | `version-footer-{light,dark}.png` |

Also record: the **record GUID** used (080 must diff on the same record), and any console errors verbatim.
Dark-theme token/contrast defects are **part of the baseline** — record them, do not fix them.

### 🚨 The known lookup delta — record it, do not "fix" it

The deployed v1.0.21 bundles the **pre-2026-08-27** shared `LookupField`. Its lookups therefore have **no
browse magnifier, no overlaying dropdown, and no pinned Advanced footer**. The new header has all three.

That difference comes from a **shared-library upgrade, not from the migration**. Per owner decision
2026-08-27, do **not** rebuild MatterHeaderPcf to erase it — a rebuilt baseline would describe a build that
never shipped, which is a worse "before", not a better one. Task 080 classifies this as
**expected-and-explained**.

Note the FR-15a exclusion in the original task text is **withdrawn** (2026-08-27): both controls now render
the same shared inline `LookupField`, so 080 assesses lookups **unqualified**, with only the delta above
carved out.

---

## Acceptance criteria — current state

| criterion | status |
|---|---|
| 5-field layout, spans, renderers from code | ✅ |
| `boundField` from the form definition, not assumed | ✅ `sprk_matternumber` |
| Footer version, full `$select`, toolbar inventory | ✅ (v1.0.21; `$select` above; sparkle + To Do + Notepad) |
| Light **and** dark screenshots of every runtime state | ⏳ **operator** |
| Lookup interaction documented + delta explained in writing | ✅ |
| Negative: only `notes/` changed | ✅ read-only capture |
