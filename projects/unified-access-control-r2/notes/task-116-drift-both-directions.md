# Task 116 — the drift check now compares POMLs and index rows as SETS (ISS-025 / #1004 + ISS-029 / #1009)

> **Executed 2026-09-21.** One file changed: `scripts/check-task-status-drift.ps1`.
> Both gating callers (`task-execute` Step 10, `push-to-github` Step 1.65) invoke it unchanged —
> no new parameter, no skill-file edit.
>
> **Result on this project**: 116 POMLs / 116 index rows, `rc=0`. Unchanged, as required.

---

## 1. The defect, and why the verdict was *narrow* rather than wrong

`Invoke-ProjectCheck` reconciled by iterating the POML side and looking up each id's index row:

```powershell
foreach ($id in ($poml.Keys | Sort-Object)) {
    if (-not $idx.ContainsKey($id)) { continue }   # no row to compare against
```

That `continue` is the whole defect. The loop is keyed on the POML side, so **an index row with no
POML was never visited**, and **a POML with no row was explicitly skipped**. The check answered
*"do the tasks present on both sides agree?"* and presented the answer as *"no drift"*.

Reproduced 2026-09-18 while queuing tasks 109–114: six index rows added with no task files, and the
script printed `task POMLs parsed : 105`, `index rows parsed : 111`, then **"No drift"**, `rc=0`.
Both numbers were on screen. The instrument had the evidence and discarded it.

This is the same class as the `$unparseable` guard one step in — FAILURE-MODES AP-12. That guard
exists precisely so a parser that reads *nothing* cannot launder itself into a green check. An
orphan row is a parser that read *most things* doing the same thing on a smaller scale.

---

## 2. 🔴 The parser decision, which was the real work

The register estimated *"two or three lines"*. That was a floor, not a spec — and the POML said so.
A set comparison is only as good as the id sets it diffs, and the existing row pattern does **not**
produce a clean id set.

### Measured at HEAD, 2026-09-21

`unified-access-control-r2`: the row regex matched **124 lines** for **116 distinct ids**. Eight ids
matched twice, and `$map[$id] = ...` keeps the **last** match:

| id | real status row | second match (wins) | source |
|---|---|---|---|
| 036 | L600 `🔲 [open]` | L807 `**` | accuracy-audit table |
| 064 | L637 `🔲 [open]` | L789 `055,` | dependency table |
| 082 | L195 `🔲 [open]` | L805 `**` | accuracy-audit table |
| 088 | L660 `🔲 [open]` | L806 `**087,` | accuracy-audit table |
| 093 | L203 `🔲 [open]` | L804 `**` | accuracy-audit table |
| 094 | L204 `🔲 [open]` | L809 `**` | accuracy-audit table |
| 095 | L205 `🔲 [open]` | L808 `**` | accuracy-audit table |
| 107 | L724 `🔲 [open]` | L803 `**` | accuracy-audit table |

### 🔴 This was a LIVE latent defect, not a theoretical one — demonstrated

Nothing went red **only because all eight happened to be open**, and a marker of `**` carries no
done token and no done emoji, so it evaluated as open. By luck, not by construction.

Proven by seeding the condition: set task **036** to `completed` on **both** sides (POML `<status>`
and a real `✅ [done]` index marker) and ask each parser.

| parser | marker it resolved for 036 | verdict |
|---|---|---|
| **old** (single rule) | `**` — the audit-table row | **PHANTOM DRIFT**: "INDEX is behind" |
| **new** (two rules) | `✅ [done]` — the status row | clean, `rc=0` ✅ |

⚠️ **Task 107 is in Wave 1.** Had 116 not run first, the very next Wave-1 completion would have
reddened the gate on a correct index — and the natural remediation ("the marker must be wrong, fix
the index") would have been editing a correct file to satisfy a broken parser.

### The two rules, and the two rules that were MEASURED AND REJECTED

**RULE 1 — exactly one task id in the first cell.** A cell naming two tasks (`055, 064`,
`**087, 088**`) is a dependency list, not one task's status row. Width-independent, unconditionally
correct.

**RULE 2 — an adaptive width floor.** Where an index contains tables of differing widths, the status
table is the wider one; the narrow 2–3 column tables are reference tables. Floor = 4 cells, which is
the narrowest real status row observed in any wide index (uac-r2 task **090**, at 4 cells).

> ❌ **REJECTED — a marker whitelist** ("first cell holds a known status glyph or `[token]`"). This
> is the obvious rule and it is wrong. `ai-advanced-capabilities-analysis-hub-r1` writes its real
> status rows as `| **001** | title | phase | rigor | … |` with **no marker glyph at all** — the
> status lives in a later column. Measured: the whitelist silently dropped **~41 legitimate rows**
> repo-wide. The same measurement kills "reject a bold-only first cell", which would have been the
> tempting one-liner for the audit table.
>
> ❌ **REJECTED — a FIXED width floor of 4.** Measured first, and it took **12 projects** from some
> rows to **zero** rows — which trips `$unparseable` and exits 1 on them. Hence adaptive: where an
> index has no table wider than 3 columns, its narrow tables **are** its status tables and no width
> filter applies. The floor engages only when the index demonstrably has a wider table to distinguish
> from.

### Repo-wide marker vocabulary, for the next person

Across all 151 project indexes the first-cell pattern yields 34 distinct "marker" strings. Only seven
are genuine status markers (`✅ [done]` ×76, `✅` ×60, `🔲 [open]` ×36, `⚠️ [escalated]` ×3, `⏭️` ×3,
`🔲` ×1, `🟡 [blocked]` ×1). The rest are artifacts: bold markup (`**` ×73), **task-id prefixes from
other projects' id schemes** (`AIPL-` ×38, `VHVU-` ×19, `PH-` ×8, `R4-` ×8 — these are *real* status
rows whose first cell is just the id), and dependency-list fragments. **Any future matcher must
survive all four shapes.**

### Escalation trigger — evaluated, did NOT fire

The POML's trigger fires if status rows *cannot* be distinguished from reference tables without
changing `TASK-INDEX.md`'s format. They can, by the two structural rules above. **No index file was
reformatted, and none needed to be.**

---

## 3. Contracts preserved

| Contract | State |
|---|---|
| Per-project gate exits 1 on drift, 0 when clean | ✅ verified both ways |
| `-All` is NON-BLOCKING, exits 0 unconditionally | ✅ `rc=0` across 151 projects incl. unpaired + unparseable |
| `$unparseable` fires first, prints its own message, exits 1 | ✅ and **is not restated as N orphans** — see below |
| Both callers invoke with no arguments, read only the exit code | ✅ no new parameter; no skill file touched |
| G-16 — no matcher depends on a glyph above U+FFFF | ✅ proven by perturbation (§4, C6) |

### 🔴 One correction made during verification

The first implementation computed the set diff **unconditionally**. On `-All` this reported
**5,412 unpaired entries** repo-wide, because an unparseable index yields zero rows and therefore
restates every one of its POMLs as an orphan — burying a single unknown-format diagnosis under
hundreds of false orphans. That is exactly what the AP-12 constraint and acceptance criterion 7
forbid (*"not replaced by, or reported as, a list of 111 orphan POMLs"*). The set diff is now
**suppressed when `$unparseable`**: the guard owns that case and reports it in its own words.

---

## 4. Verification — every criterion exercised, positives paired with negatives

| # | Check | Result |
|---|---|---|
| P0 | clean baseline | 116/116, `rc=0` ✅ |
| C2 | **index row with no POML** (the exact ISS-025 case) | `rc=1`, names `775`/`777` — "NO POML backs this index row" ✅ |
| C1 | **POML with no index row** (the `continue`d case) | `rc=1`, names `778` — "INDEX has no row for this POML" ✅ |
| C3a | status disagreement, **POML is behind** | `rc=1`, `036 POML='pending' INDEX='✅ [done]'` ✅ |
| C3b | status disagreement, **INDEX is behind** | `rc=1`, `036 POML='completed' INDEX='🔲 [open]'` ✅ |
| C3c | terminal vocabulary is NOT drift | 012 `completed-with-escalation`/`⚠️ [escalated]`, 034 `blocked-shipped`/`🟡 [blocked]`, 071 — all silent in a clean run ✅ |
| C4 | set equality on this project | 116 = 116, `rc=0` ✅ |
| C5 | `-All` non-blocking | `rc=0` with unpaired + unparseable present ✅ |
| C6 | **emoji not load-bearing** | orphan row written `\| X [open] 776 \|` — 🔲 replaced by ASCII `X` — still detected ✅ |
| C7 | `$unparseable` | 116 POMLs / 0 rows → its own message, `rc=1`, **no orphan list emitted** ✅ |
| C8 | gating callers unchanged | no-arg invocation, clean → `rc=0`; drift → `rc=1` ✅ |
| C9 | the eight double-matched ids | all excluded as reference rows; 116 lines → 116 distinct ids, **zero duplicates**; phantom demonstrated against the old parser and absent under the new ✅ |
| C10 | **mandatory perturbation** | assertion disabled on a temp copy → seeded orphan `775` goes **green, `rc=0`** (the old blind spot, reproduced); assertion restored → **`rc=1` naming 775** ✅ |

All perturbations reverted; `git status` shows only `scripts/check-task-status-drift.ps1`, and the
index is byte-identical to its committed state.

---

## 5. Out of scope, deliberately

- **`scripts/README.md` does not list this script** (it never did). Adding an entry is outside this
  task's declared scope (`the script, its notes file, its own index row`) — recorded here so it is a
  decision rather than an oversight.
- **No `TASK-INDEX.md` reformatting.** The constraint named this as the escalation path, not a
  licence, and the two structural rules made it unnecessary.
- **The eight audit-table rows were left exactly as they are.** They are legitimate prose; the
  parser was the thing that was wrong.

---

## 6. What this closes

**ISS-025 / #1004** — the set comparison, in both directions.
**ISS-029 / #1009** — the eight mis-parsed ids, which shared the same root cause and would have made
any set comparison report phantom orphans the first time the audit table named a task with no POML.

Task 090's disposition gate must account for both.

---

## 7. The shape worth remembering

Same class as this project's recurring root error: **an observation taken outside the thing being
observed.** The check reported on the intersection of two sets and named its verdict after the union.
The counts it needed were already in its own output.

And the second lesson, from the parser: **the obvious discriminator was wrong twice.** Both a marker
whitelist and a fixed width floor looked principled, were easy to write, and were refuted only by
measuring against all 151 indexes rather than the one in front of me.
