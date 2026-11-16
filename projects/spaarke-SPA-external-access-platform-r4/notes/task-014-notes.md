# Task 014 — BFF Event forms (FR-35, FR-36, FR-37)

## Rigor
FULL, opus @ high, directional. Two adversarial passes (external write route).

## Gates
1. PR #2847 (#2845) merged: **done**, master `ce6c241964`, merged into the branch at `5eb257a40d` (2026-11-16).
2. uac-r2 UAC review of the proposal: **sent 2026-11-16** (proposal A–F, questions Q1–Q6, below). No BFF code before the answer.

## Conflict check (2026-11-16)
39 open PRs checked; none touches `Api/ExternalAccess/`, `Infrastructure/ExternalAccess/`, `ExternalAccessModule.cs` or `external-spa/src/`.

## Proposal sent to uac-r2 (summary)
- **A.** `creatableEventTypes`: every active type row.
  - DataverseConfirmsWrites callers are filtered by a batched AppendTo probe, memoized under `event-type-appendto:{principal}:{sortedTypeIds}`.
  - Contacts get every active row.
  - Config `ExternalAccess:EventTypes:Allowed` can narrow the set.
  - 400 `sdap.event.type_not_allowed`. `"task"` is kept for one release.
- **B.** Create adds `eventTypeId` (required), `sprk_priority`, `sprk_description` and `assignedToContactId` (400 `sdap.event.assignee_not_allowed`).
- **C.** New readable columns: type id and name, description, priority, assignee name, creator name. Plus `canEdit`, `canChangeStatus` and `allowedStatuses` per row.
- **D.** `GET /{root}/{id}/assignable-people`: the only route that returns contact ids. The gate is the create or edit right.
- **E.** `GET /events/{id}`: requires read on every parent; otherwise the uniform 404.
- **F.** `PATCH /events/{id}`:
  - partial fields;
  - any status, with `statecode` paired;
  - the type cannot change (400 `sdap.event.type_immutable`);
  - the right is Collaborate or Full on every parent (403 `sdap.access.deny.event_update`).

Questions:
- **Q1:** which access resolution decides the edit right, plus a Dataverse Write probe for systemusers?
- **Q2:** does the assignable set include organisation grants? Are deny-list subjects removed? Which reader?
- **Q3:** is "all active types" acceptable for CIAM contacts?
- **Q4:** must PATCH clear the memo after an assignee write?
- **Q5:** may the creator name be exposed?
- **Q6:** any route-ledger pins?

## uac-r2 review
(pending)
