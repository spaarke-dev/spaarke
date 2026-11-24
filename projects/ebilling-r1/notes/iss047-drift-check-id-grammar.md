# ISS-047 (#3042): the task-status drift check reads iss-prefixed and hyphen-suffixed task ids

Task: `projects/ebilling-r1/tasks/iss047-drift-check-id-grammar.poml`. Script: `scripts/check-task-status-drift.ps1`. Tests: `tests/scripts/Check-TaskStatusDrift.Tests.ps1` (Pester 5.7.1, the version `tests/scripts` declares).

## The faults

1. The status-first pattern `[^\s|]+ ... (\d{3}...)` took the letters of `iss028` as the status marker and `028` as the id. The `iss028` and `iss040` rows overwrote the real `028` and `040` rows (the map keeps the last match), which gave a phantom drift on both.
2. Ids with a letter prefix or a hyphenated suffix (`iss029`, `084h-1`, `095f-2e2a`, `130-0`) matched neither row pattern, so their POMLs read as "no index row".

## The grammar (one definition, in `Get-IndexMarkers`)

```
[letters]  3 digits  [.N]  [letter]  [-<digit>[letters/digits]]...
(?:[a-z]+)?\d{3}(?:\.\d+)?[a-z]?(?:-\d[a-z0-9]*)*
```

Examples that parse: `028`, `081.5`, `102a`, `iss028`, `iss034a`, `084h-1`, `095f-2e2a`, `130-0`, `130-0b`, `095d-2b-3`.

Used by both row layouts (status-first `$rowPattern`, id-first `$idFirstPattern`). Two guards keep an id whole:

- `(?<![a-z])` before the id in the status-first pattern: a lowercase letter right before the digits belongs to the id, never to the marker. This covers `| ✅iss028 |`.
- A first cell that is, on its own, a letter-prefixed or hyphenated id goes straight to the id-first layout, so the status-first pattern cannot split `084h-100` at the hyphen. A plain id (`028`, `102a`, `081.5`) takes the old path, so its behaviour is unchanged (including a `| **001** | title | phase |` row with no status glyph, which the status-first path still counts).

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
| Index rows parsed | 5899 | 5945 (+46) |
| Projects with drift, unpaired entries or an unreadable index | 125 | 125 |
| Total disagreements (POML status vs index marker) | 1440 | 1444 (+4) |
| Total unpaired entries | 1233 | 1191 (-42) |

Per-project differences: exactly one project changes, `ebilling-r1` (idx 170 → 216, drift 4 → 8, unpaired 71 → 29). All other 150 projects have identical POML and index maps, entry for entry (a diff of the dump files lists only `ebilling-r1`). The POML map is identical everywhere.

Every difference on `ebilling-r1` is one of the two faults:

- Fault 1 (iss rows no longer overwrite): 18 plain keys were filled from iss rows (`027 028 029 030 031 032 034a 034b 034c 034d 040 041 042 043 046 047 048 050`). After the change: ten of them (`027 028 029 040 041 042 043 046 047 048`) hold their own real row, eight (`030 031 032 034a 034b 034c 034d 050`) were phantom keys with no real row and disappear, and 18 `iss*` keys exist (`iss027` through `iss050`).
- Fault 2 (ids that did not parse), 36 keys: `084d-1 .. 084d-5b` (8), `084h-1 .. 084h-5` (5), `095c-5`, `095d-2b-3`, `095f-2a .. 095f-2ui` and `095f-2e2` (11), `130-0`, `130-0b`, `130e-1`, `130e-2`, `151c-1a`, `151c-1b`, `151c-2`, `165b-2 .. 165b-4`.
- Net on the project: 18 lines removed, 64 keys added (10 + 18 + 36), which is the 82 lines the dump diff reports.

## Remaining on `work/ebilling-r1` after the change

`pwsh scripts/check-task-status-drift.ps1 -Project ebilling-r1` → exit 1: 195 POMLs, 216 index rows, 29 unpaired, 8 status drifts. The `028` and `040` phantoms are gone, and so are the `041` and `047` phantoms (the iss041 and iss047 rows were overwriting them). TASK-INDEX.md and POML statuses were not edited.

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

Unpaired (29):

- POML with no index row (4): `095d-1`, `095d-2` (the index has `095d`), `132ui` (see limits), `133` (see limits).
- Index row with no POML (25): `084d-1 084d-2a 084d-2b 084d-3 084d-4 084d-5 084d-5a 084d-5b` (sub-task rows of the split `084d`, one POML `084d-impact-and-assign.poml`), `084h`, `095e`, `095f`, `095f-2e2`, `095g`, `097a 097b 097c 097d`, `113b`, `130a 130b 130c 130d`, `151c`, `151d`, `iss040`. These are index rows for work that has no task file of its own (umbrella rows, rows for parts delivered under another task, `iss040` which is the second row for task 040). Whether to add POMLs or drop the rows is an index decision, not a parser one.

## Known limits (not changed; outside the task's two faults)

- `132ui` (POML id with a two-letter suffix) still does not pair: the specified grammar has a one-letter suffix, and widening it would be a third change. The index row `| 132ui | ✅ ...` parses as no id. One-line fix if wanted: `[a-z]?` → `[a-z]*` after the digits in `$idGrammar` (and `$plainIdPattern` stays as is).
- `133`: the index marker is `⏭️ [withdrawn ...]`, which is not a status token or glyph the script knows, so the row is "not a status row" and reads as no row. Same rule as before; changing what counts as a status is out of scope.
- An uppercase letter right before the digits in a status-first row (`| T001 |`) is still read as marker `T` plus id `001`, as before; the new lookbehind is lowercase only so that other projects' indexes parse exactly as they did. No index in the repo has that shape in a way that changed (the whole-repo comparison shows no difference outside `ebilling-r1`).
- The new Pester file is not listed in `.github/workflows/ci-standalone-checks.yml`, which names its Pester files one by one; adding it there is a separate workflow edit. Run it with `Import-Module Pester -RequiredVersion 5.7.1; Invoke-Pester tests/scripts/Check-TaskStatusDrift.Tests.ps1`.

## Tests

`tests/scripts/Check-TaskStatusDrift.Tests.ps1`, 20 cases. Before the change (old script copied back): 12 fail, 8 pass (the 8 are the must-not-fire controls that describe unchanged behaviour). After: 20 pass.

Must fire: `| iss028 |` is `iss028`, not `028`; an `iss028` row does not overwrite a `028` row; glued and spaced status-first forms (`| ✅iss028 |`, `| ✅ [done] iss028 |`); `iss034a`; `084h-1`, `095f-2e2a`, `130-0`, `130-0b`, `095d-2b-3`; `084h-100` is not split; a bold `**084h-1**`; a real drift (index done, POML not-started) is reported for a prefixed id and for a hyphenated id (exit 1, "POML is behind"); a hyphenated POML with no row is still unpaired.
Must not fire: a plain `028` row is still `028`; `102a` and `081.5`; a status-first row with a plain id; a bold plain id with the status in a later cell; a dependency list (`| 055, 064 |`); `| 090-note |`; a prose line; a clean pair exits 0.
