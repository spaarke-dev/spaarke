# Task 021 — predicate compiler: progress (task-execute checkpoint)

> Kept here, NOT in `current-task.md` (main session uses that as the orchestrator checkpoint).

| Field | Value |
|---|---|
| Rigor | FULL (POML-declared; bff-api tag; new .cs; project risk item) |
| Tier | opus @ xhigh · steps mode directional |
| Status | **completed** (POML `<status>` + `<completion>` set). TASK-INDEX + drift check + commit = coordinator |

## Completed
- [x] Steps 0–9: see `notes/pathb-fetchxml-reference.md` (queries, real-data results, deviations, review disposition §6)
- [x] Coordinator finding (task 006): read-depth gate kept
- [x] Step 9.5: independent code-review + adr-check — no Critical, no §6.5 conflict; F1 (High) fixed; disposition §6
- [x] Post-fix: compiler re-emitted and re-run verbatim on spaarkedev1 — identical result sets
- [x] Full BFF unit suite (uncontended, after fixes):
      `Passed! - Failed: 0, Passed: 14355, Skipped: 54, Total: 14409, Duration: 26 m 35 s`
- [x] POML `<status>completed</status>` + `<completion>`; `Validate-TaskPoml.ps1`: PASS

## Caveats
- Publish size (+8,189 B) was measured BEFORE the Step 9.5 fixes; the fixes add only code to the same two `src`
  files (no package, no file-count change). Not re-measured, to avoid holding the machine for another ~30 min.
- Live seam test written, not run (workstation token); writer-principal results not observed.
