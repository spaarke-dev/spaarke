# ISS-047 (#3042): the task-status drift check reads iss-prefixed and hyphen-suffixed task ids

Task: `projects/ebilling-r1/tasks/iss047-drift-check-id-grammar.poml`. Script: `scripts/check-task-status-drift.ps1`. Tests: `tests/scripts/Check-TaskStatusDrift.Tests.ps1` (Pester 5.7.1, the version `tests/scripts` declares).

## The faults

1. The status-first pattern `[^\s|]+ ... (\d{3}...)` took the letters of `iss028` as the status marker and `028` as the id. The `iss028` and `iss040` rows overwrote the real `028` and `040` rows (the map keeps the last match), which gave a phantom drift on both.
2. Ids with a letter prefix or a hyphenated suffix (`iss029`, `084h-1`, `095f-2e2a`, `130-0`) matched neither row pattern, so their POMLs read as "no index row".

## The grammar (one definition, in `Get-IndexMarkers`)

```
[letters]  3 digits  [.N]  [letter]  [-<digit>[letters/digits]]...
(?:[a-z]+)?\d{3}(?:\.\d+)?[a-z]*(?:-\d[a-z0-9]*)*
```

Examples that parse: `028`, `081.5`, `102a`, `iss028`, `iss034a`, `084h-1`, `095f-2e2a`, `130-0`, `130-0b`, `095d-2b-3`.

Used by both row layouts (status-first `$rowPattern`, id-first `$idFirstPattern`). Two guards keep an id whole:

- `(?<![a-z])` before the id in the status-first pattern: a lowercase letter right before the digits belongs to the id, never to the marker. This covers `| ✅iss028 |`.
- A first cell that is, on its own, a letter-prefixed or hyphenated id goes straight to the id-first layout, so the status-first pattern cannot split `084h-1` at the hyphen (the first cell is compared with `*` removed and trimmed, inner whitespace kept, case-sensitively; see the fix round). A plain id (`028`, `102a`, `081.5`) takes the old path, so its behaviour is unchanged (including a `| **001** | title | phase |` row with no status glyph, which the status-first path still counts).

Unchanged: what counts as a status, the exit codes, the output format, the parameters, RULE 1 and RULE 2, `ConvertTo-PairingKeys`.

One line was added to the row loop: a line that does not start with `|` is skipped before the id guard reads the first cell. The old patterns could never match such a line, so the result is the same; the line only avoids an out-of-range read on a blank line.

### Why the hyphenated segment must start with a digit

The first draft allowed any letters or digits after the hyphen. The whole-repo comparison then showed one difference that is neither fault: `email-communication-intelligence-r2` has an annotation row `| 090-note | wrap-up artifacts DONE ... |`, which the draft read as a task id. Every real hyphenated id in the repo has a digit-led segment (`-1`, `-2e2a`, `-0b`, `-2ui`), so each segment now starts with a digit. The `090-note` case is a must-not-fire test.

### The POML side

The POML id is the `<task id="...">` attribute, taken verbatim; the file name plays no part in pairing. It needs no second grammar and must not get one: 1288 of the 6483 POML ids in the repo (`MCI-001`, `R2-001`, `E2D-001`, `CICD-053a`) have an uppercase prefix and pair through `ConvertTo-PairingKeys`, which strips `^[A-Za-z]+-(?=\d)`. A POML `iss029` and an index `iss029` now pair because the index side keeps the whole id. Pairing is not changed.

## Whole-repo parse counts (`-All`, plus a per-project dump of both maps)

Measured on the tip of `work/ebilling-r1` (`b4746e24df`), 151 projects with a `tasks/` folder.

| | before | after |
|---|---|---|
| POML entries parsed | 6483 | 6483 |
| Index rows parsed | 5899 | 5946 (+47) |
| Projects with drift, unpaired entries or an unreadable index | 125 | 125 |
| Total disagreements (POML status vs index marker) | 1440 | 1444 (+4) |
| Total unpaired entries | 1233 | 1190 (-43) |

(These are the final figures after fix round 1; the first version of the change read 5945 and 1191, the difference being `132ui`.)

Per-project differences: exactly one project changes, `ebilling-r1` (idx 170 → 217, drift 4 → 8, unpaired 71 → 28). All other 150 projects have identical POML and index maps, entry for entry (a diff of the dump files lists only `ebilling-r1`). The POML map is identical everywhere.

Every difference on `ebilling-r1` is one of the two faults:

- Fault 1 (iss rows no longer overwrite): 18 plain keys were filled from iss rows (`027 028 029 030 031 032 034a 034b 034c 034d 040 041 042 043 046 047 048 050`). After the change: ten of them (`027 028 029 040 041 042 043 046 047 048`) hold their own real row, eight (`030 031 032 034a 034b 034c 034d 050`) were phantom keys with no real row and disappear, and 18 `iss*` keys exist (`iss027` through `iss050`).
- Fault 2 (ids that did not parse), 37 keys: `084d-1 .. 084d-5b` (8), `084h-1 .. 084h-5` (5), `095c-5`, `095d-2b-3`, `095f-2a .. 095f-2ui` and `095f-2e2` (11), `130-0`, `130-0b`, `130e-1`, `130e-2`, `132ui` (multi-letter suffix, from fix round 1), `151c-1a`, `151c-1b`, `151c-2`, `165b-2 .. 165b-4`.
- Net on the project: 18 lines removed, 65 keys added (10 + 18 + 37), which is the 83 lines the dump diff reports.

## Remaining on `work/ebilling-r1` after the change

`pwsh scripts/check-task-status-drift.ps1 -Project ebilling-r1` → exit 1: 195 POMLs, 217 index rows, 28 unpaired, 8 status drifts. The `028` and `040` phantoms are gone, and so are the `041` and `047` phantoms (the iss041 and iss047 rows were overwriting them). TASK-INDEX.md and POML statuses were not edited.

Status drifts (8, all "POML is behind": the index says done, the POML says otherwise; they were hidden before because their ids did not parse):

| id | POML status | index |
|---|---|---|
| `084h-1` | built-awaiting-review | ✅ merged |
| `095c-5` | not-started | ✅ done 2026-11-04 |
| `130-0` | in-progress | ✅ done |
| `130-0b` | open | ✅ done |
| `130e-1` | open | ✅ done |
| `151c-1a` | not-started | ✅ done |
| `151c-1b` | not-started | ✅ done |
| `151c-2` | done | ✅ done (the POML word `done` is not a `complete*` status; the check reads it as open) |

These are real disagreements for the project owner to resolve from evidence (git completion commits); the check is now correct to report them. They are not fixed here (the task forbids editing a POML status or the index to make the check pass).

Unpaired (28):

- POML with no index row (3): `095d-1`, `095d-2` (the index has `095d`), `133` (see limits).
- Index row with no POML (25): `084d-1 084d-2a 084d-2b 084d-3 084d-4 084d-5 084d-5a 084d-5b` (sub-task rows of the split `084d`, one POML `084d-impact-and-assign.poml`), `084h`, `095e`, `095f`, `095f-2e2`, `095g`, `097a 097b 097c 097d`, `113b`, `130a 130b 130c 130d`, `151c`, `151d`, `iss040`. These are index rows for work that has no task file of its own (umbrella rows, rows for parts delivered under another task, `iss040` which is the second row for task 040). Whether to add POMLs or drop the rows is an index decision, not a parser one.

## Known limits (not changed; outside the task's two faults)

- `132ui` did not pair in the first version (one-letter suffix); fixed in fix round 1 (K1).
- `133`: the index marker is `⏭️ [withdrawn ...]`, which is not a status token or glyph the script knows, so the row is "not a status row" and reads as no row. Same rule as before; changing what counts as a status is out of scope.
- An uppercase letter right before the digits in a status-first row (`| T001 |`) is read as marker `T` plus id `001`, as before. The first version of this change did NOT keep that: it stripped all whitespace from the first cell and matched case-insensitively, so `| T001 |`, `| note 001 |`, `| done 001 |` and `| Phase 100 |` were routed to the id-first path and dropped. The claim in this section that `T001` was unchanged was therefore wrong for that version; it is true after fix round 1 (below). The lookbehind is lowercase only on purpose.
- `001-2` (a reference row with a single-digit hyphen tail) reads as an id; it has the same shape as `130-0` (fix round 1, K2). Without an owning POML or a status cell it is skipped, so it only shows when its row carries a status glyph.
- The new Pester file is not listed in `.github/workflows/ci-standalone-checks.yml`, which names its Pester files one by one; adding it there is a separate workflow edit. Run it with `Import-Module Pester -RequiredVersion 5.7.1; Invoke-Pester tests/scripts/Check-TaskStatusDrift.Tests.ps1`.

## Fix round 1 (adversarial review)

1. **F1, a real regression in the first version.** The id-first guard built `$firstCell` by deleting all whitespace and matched with `-match` (case-insensitive), so `| T001 | ✅ [done] |`, `| note 001 | ... |`, `| done 001 | ... |` and `| Phase 100 | ... |` looked like a whole id and were routed to the id-first path, which dropped them although the old script read id `001` / `100`. Now `$firstCell = (cell -replace '\*','').Trim()` (inner whitespace kept) and the grammar test uses `-cmatch` / `-cnotmatch`, so only a first cell that is exactly a lowercase-grammar id takes the guard. The status-first pattern was already case-sensitive (`[regex]::Match`). Must-not-fire tests added for `note 001`, `done 002`, `Phase 100` and `T001`.
2. **K1 fixed.** The letter suffix is now `[a-z]*`, so `132ui` (and `132ui-1`) parse and pair. Repo-wide effect: `132ui` in `ebilling-r1` only.
3. **K2 fixed as far as the shape allows.** RULE 1 (exactly one three-digit run in the first cell) now also applies on the id-first path, to the matched id. `| 100-200 | Wave | ✅ |`, `| **100-200** |` and `| 100a-200 |` have two runs and are no longer ids. This also means an id such as `084h-100` (a second three-digit segment) is not an id: no such id exists in the repo; the earlier "not split" test now only asserts it is not read as `100`. **Limit:** `001-2` has one three-digit run and the same shape as the real `130-0`, so it still reads as an id; a rule cannot tell them apart. A test records this (`KNOWN LIMIT`). `130-0`, `084h-1`, `095f-2e2a` still parse.
4. Left alone as asked: K3 (the Pester file is not in the CI workflow) and K4 (`151c-2`'s POML status `done`; the coordinator fixes the POML). The status vocabulary is unchanged.

Re-run against `b4746e24df` (the pre-change script): POML maps identical in all projects; the index maps differ in `ebilling-r1` only; versus the first version of this change (`16dd7925f8`) the only difference in the whole repo is the added `132ui` row. No change in any other project from F1 or K2 (no other index has a first cell of the shapes involved).

## Tests

`tests/scripts/Check-TaskStatusDrift.Tests.ps1`, 28 cases (20 in the first version, 8 added in fix round 1). Pester 5.7.1: 28 pass. Run against the first version of the script, the fix-round cases fail (6 fail, 22 pass): multi-letter suffix (2), `note 001` / `Phase 100`, `T001`, `100-200`, `100a-200`. Against the pre-change script the original 12 must-fire cases fail and the must-not-fire cases pass (first version: 12 fail, 8 pass of 20).

Must fire: `| iss028 |` is `iss028`, not `028`; an `iss028` row does not overwrite a `028` row; glued and spaced status-first forms (`| ✅iss028 |`, `| ✅ [done] iss028 |`); `iss034a`; `084h-1`, `095f-2e2a`, `130-0`, `130-0b`, `095d-2b-3`; `084h-100` is not split; a bold `**084h-1**`; a real drift (index done, POML not-started) is reported for a prefixed id and for a hyphenated id (exit 1, "POML is behind"); a hyphenated POML with no row is still unpaired.
Must not fire: a plain `028` row is still `028`; `102a` and `081.5`; a status-first row with a plain id; a bold plain id with the status in a later cell; a dependency list (`| 055, 064 |`); `| 090-note |`; a prose line; a clean pair exits 0.
