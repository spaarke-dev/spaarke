# Task 009: engine changes (skip-and-report, reproducible .docx, reason codes)

> 2026-11-26. Built by the accelerator session as PR #3237 (branch `work/accelerator-redline-skip-unplaceable`, head `a3c93d54d2`, base master `4c80b81ed4`). This project did not re-implement it; task 009 verified it against its acceptance criteria.

## Contract as built

| Item | Behaviour |
|---|---|
| `ooxml_redline.py ... --skip-unplaceable REPORT.json` | Applies every edit that fits. A refused edit's paragraph is restored from a snapshot. Exit 0 = outputs written (even if every edit is refused; read REPORT.json); 2 = bad input; 3 = plan breaks the contract (no REPORT.json written; an existing one is deleted first). Stdout gains `"refused"`. Skip-mode stderr is empty for refusals. `--dry-run` writes only the report |
| REPORT.json | `{"engine":"ooxml_redline","report_version":1,"edits_total","edits_applied","refused":[{"edit":i,"code":c}],"bookmarks_refused":[{"bookmark":name,"code":c}]}`; 0-based indexes, codes only |
| Closed codes | `para_not_found`, `para_not_unique`, `not_found`, `not_unique`, `in_existing_markup`, `word_boundary`, `old_not_in_find`, `crosses_markup`, `crosses_field_or_drawing`, `in_field_result`, `nothing_after_heading`, `bookmark_exists`. **Message the accelerator session before adding to or changing this set** (their skill docs list it) |
| `summary_table.py --refused REPORT.json` | Refused edit's row says "Not applied"; adds a "Changes to make by hand" table (internal summary only) |
| `check_outputs.py --refused REPORT.json` | Validates the report (index range, repeats, known codes, integer counts, string codes); expects revision text/comments for applied edits only |
| Default-mode exit 3 | One JSON line on stderr: `{"error","code","edit"}` (`"bookmark"` instead of `"edit"` for a bookmark failure). No `"code"` when the plan breaks the contract. `"error"` text unchanged from master. Our wrapper never logs `"error"` (it can contain plan text) |
| Blank fields | Blank `para`/`find`/`old`/`after` now fail `validate_plan` (exit 3, no code) in both modes; such plans could never be placed |
| Reproducible .docx | `safe_docx.write_parts`: `[Content_Types].xml` first, then parts sorted by name, fixed date 1980-01-01. Byte-identical **within one zlib build**: compare whole-file sha256 only between runs on the same image |

## Verification (2026-11-26, independent of the accelerator's own review)

From a `git archive` of the PR head and base, Python 3.13 + lxml 6.1.3 (`py -3.11` on this machine has no lxml; the container image is the 3.11 reference).

| Criterion | Result | Evidence |
|---|---|---|
| Skip mode applies the rest, reports the refused edit, leaves its paragraph unchanged | PASS | nda-01 golden plan, edit 6 → not_found, edit 2 → not_unique: exit 0, 18/19 applied, report codes correct, refused paragraph c14n-identical; unit test `test_a_refused_edit_leaves_its_paragraph_exactly_as_it_was` |
| `summary_table --refused` / `check_outputs --refused` incl. bad reports | PASS | by-hand table present; out-of-range, repeated index and unknown code each fail the `refusals` check |
| Two runs → identical sha256 | PASS | 9/9 .docx identical; `test_outputs_are_byte_identical_across_runs_and_clocks` |
| Default mode unchanged | PASS | base 49 tests pass on new code; new suite 64 (later 82 with build tests per the accelerator); nda-01/02/03 every part's bytes identical, only entry order/dates differ; exit-3 `"error"` byte-identical, keys added |
| Plan-shape error exits 3 in both modes; no clause text in report/stderr | PASS, K-class gap | `test_applies_what_fits_and_reports_the_rest_by_code_only` asserts empty stderr and no para/find/replace text in the report. **Gap:** no test that skip-mode *exit-3* stderr is free of clause text (today's messages name plan keys only) |
| No fuzzy matching; stdlib + lxml only | PASS | diff grep for difflib/fuzz/closest/approx/ratio: none |

No F-class defect found. `nda-review/SKILL.md` not checked for an engine-options list (interactive flow stays default mode).

## What changes for this project once #3237 is on master

- Task 001: turn on skip mode in the `redline` operation, map exit 3 `code`/`edit` from the stderr JSON (never log `error`), and tighten in-image parity to whole-file sha256.
- Task 015: by-hand rows and the refused count on the approval Work Item come from REPORT.json.
