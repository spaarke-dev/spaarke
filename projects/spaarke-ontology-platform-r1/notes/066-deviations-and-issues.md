# Task 066: deviations, review and D-106 issues

## Deviations from the POML
1. The Dataverse solution export (`src/dataverse/solutions/SpaarkeMaster`) still names the column (entity, form placement, 11 views, 5 web-resource
   copies). POML scope forbids changing the column, form placements and views; the guard test excludes that folder. The 6 filtering views are an owner
   decision (see the inventory, V-1 to V-6).
2. ADR-038 "paired with a live describe": the tests pin client constants to the data-model statuscode row (the existing task-097 pattern); that row was
   re-checked against a live `describe` of `sprk_event` on 2026-10-09 and matches. No test calls Dataverse.
3. Ribbon form commands write the status through `Xrm.WebApi.updateRecord` after the form save, instead of a form attribute.
4. The wire field `status` of the create-task apply and ad-hoc endpoints was RENAMED `statusCode` (round 2); a body still carrying `status` gets 422 STATUS_FIELD_RETIRED.
5. No UI tests run (no deployed environment, no Chrome); the ribbon is covered by node tests that load the real web resource, the BFF by unit/seam/contract tests.

## Review (Step 9.5, self-review of the diff; no F-class finding open)
- F-class found and fixed: none after tests. K4 noted: `IsEventActive` degrades to statecode when statuscode is not on the form (owner decision 2).
- ADR check: no new DI, endpoint, package or file surface in the BFF (one internal static helper on an existing service).
