# Ordinary moves on dev: ontology baseline before `OrdinaryMovesWritesEnabled` (2026-10-30)

uac-r2 turns on `SecureRoot__Inheritance__RootOwnerWritesEnabled` and `SecureChild__Reconciliation__OrdinaryMovesWritesEnabled` on
`spaarke-bff-dev` (owner approved in their session). Ten person-owned work assignments move from the root unit's team to the BU1 team;
their child rows follow through `SecureChildLineage`. The ontology review condition (current-task.md) is lifted on this evidence:

- Every write site sends the owner only (verified on master: `SecureChildReconciler.cs:869` `ReownOrdinaryRowCoreAsync`, `:1057`
  `ReownFiledRootAsync`, both If-Match; `:2974` `AssignAsync`, `:3018` restore).
- A report-only pass cannot show these rows' moves before the roots move (the root step writes only with both switches on, #2477).

## Rows that should move (read before the switch)

Cross-unit WAs (uac-r2's plan): 1bca3ac9, 2dec2df5, 425fa95a, 7c24864d, 84e8f01a, 8a39fc23, b10b7dab, b9aa61fa, cd7a4c2b, f8323f68.
Decision Records with a core in these WAs: **none**. Budget revisions: none (they hang off budgets and matters).

Open Signals, all: owner `09fbf21c-1872-f011-b4cb-7c1e52671ad0` (root unit "Spaarke"), `sprk_corerecordtype` Work Assignment,
`sprk_policyversion` `00000000-0000-4000-8000-000000006113`, `sprk_matter` empty, `sprk_signalstatus` Open, no Decision Record.

| Signal | Core WA | `sprk_factsnapshot` |
|---|---|---|
| 76b2c3ef-04c9-f111-a05c-0022482913fc | 84e8f01a-e862-f111-ab0c-000d3a4d8152 | `{"statecode":0,"sprk_responseduedate":"2026-06-09T00:00:00Z","sprk_respondedon":null}` |
| dab3cff5-04c9-f111-a05c-0022482913fc | 7c24864d-3660-f111-ab0b-70a8a59455f4 | `{"statecode":0,"sprk_responseduedate":"2026-06-08T00:00:00Z","sprk_respondedon":null}` |
| dcb3cff5-04c9-f111-a05c-0022482913fc | 8a39fc23-3c60-f111-ab0b-70a8a59455f4 | `{"statecode":0,"sprk_responseduedate":"2026-06-08T00:00:00Z","sprk_respondedon":null}` |
| e2b3cff5-04c9-f111-a05c-0022482913fc | cd7a4c2b-8c1e-f111-88b2-7c1e525abd8b | `{"statecode":0,"sprk_responseduedate":"2026-03-12T00:00:00Z","sprk_respondedon":null}` |
| e6b3cff5-04c9-f111-a05c-0022482913fc | 2dec2df5-551e-f111-88b3-7ced8d1dc988 | `{"statecode":0,"sprk_responseduedate":"2026-03-05T00:00:00Z","sprk_respondedon":null}` |
| e8b3cff5-04c9-f111-a05c-0022482913fc | f8323f68-741e-f111-88b3-7ced8d1dc988 | `{"statecode":0,"sprk_responseduedate":"2026-03-20T00:00:00Z","sprk_respondedon":null}` |
| f0b3cff5-04c9-f111-a05c-0022482913fc | 1bca3ac9-891e-f111-88b3-7ced8d1dc988 | `{"statecode":0,"sprk_responseduedate":"2026-03-12T00:00:00Z","sprk_respondedon":null}` |
| af32dbfb-04c9-f111-a05c-0022482913fc | b9aa61fa-6f96-f111-b8dc-7ced8ddc4a05 | `{"statecode":0,"sprk_responseduedate":"2026-08-12T00:00:00Z","sprk_respondedon":null}` |
| b332dbfb-04c9-f111-a05c-0022482913fc | b10b7dab-437b-f111-ab0e-7ced8ddc4cc6 | `{"statecode":0,"sprk_responseduedate":"2026-07-10T00:00:00Z","sprk_respondedon":null}` |

**Pass after the runs:** each of the nine is owned by the BU1 team (`cf15f587-baa0-f111-aaac-000d3a99d1d7`, the team behind the BU1 rows
on dev) or by whatever team its WA now has; every other column above is unchanged. Root-unit WA-core Decision Records
(3346333d, 22f8865a, 26f8865a, 8146c454; cores outside the ten) and the same-unit re-owns should keep their owning unit.

## After the runs

(to fill: time of uac-r2's notice, the re-read, pass or finding with ids)
