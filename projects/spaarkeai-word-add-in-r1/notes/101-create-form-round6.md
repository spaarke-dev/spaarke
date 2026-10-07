# Task 101 — create form round 6 (UAT items 1-3)

FULL, sonnet/high, directional, 2026-10-06. Client-only; no BFF, no field added/removed.

## Changes
- `RelatedToPicker.tsx`: `handleTypeChange` while the form is open only switches `selectedType` (form stays open; query/results untouched). `CreateRecordForm` no longer keyed by type. `typeBeforeCreateRef` records the pill at "+ New"; Cancel restores it. `<Text as="h2">Create New Record</Text>` above the pills only while `showCreate`.
- `CreateRecordForm.tsx`: effect on `type` change clears Matter Type / Practice Area / Project Type, their errors and the create error. Name, Description, Assigned To (value, touched flag) carry over. Actions row: Cancel (secondary) left, Create (primary) right (`justifyContent: space-between`). Focus is untouched on a switch, so it stays on the pill.

## Tests
- `RelatedToPicker.layout.test.tsx` (+4): heading only while open and before pills; Matter→Project keeps Name/Description, drops Matter fields, focus stays on pill; required fields follow the type; Cancel after a switch restores pill + query + results.
- `CreateRecordForm.test.tsx` (+2): Cancel precedes Create; type change submits only carried values.
- No new suite files (nothing to add to ci-gated-suites.txt).

## Gates
16 suites / 167 tests green (RelatedToPicker*, CreateRecordForm, SaveFlow*). Lint and tsc: no findings in 101 files; see report for other agents' in-progress files.

## Open
Live check after deploy (type switch, heading, button order, Cancel).
