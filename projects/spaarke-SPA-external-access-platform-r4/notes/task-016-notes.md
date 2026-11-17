# Task 016: Event forms deploy + owner E2E (FR-35, FR-36, FR-37)

**Rigor:** STANDARD (live deploy and E2E, no code). Steps prescriptive.

## Step 1 — order
- 014 merged (#2852, #2869, #2872 → `3c9a2ff4e1`); 015 merged (#2886 → `242696586f`).
- Dev BFF `e46b6b453b` (uac-r2 deploy, live since 2026-11-17T03:40Z) already carries 014. BFF rollback stays `be0420d763` (uac-r2).

## Step 2 — SPA deploy (owner go 2026-11-17: "Yes, deploy")
- Workflow `deploy-external-spa.yml`, environment dev, ref master `ed3a5c2752` (includes 015). Run 44153057447, success (2026-11-17 ~18:56Z). Build-output check passed.
- Also shipped with it: master changes since `c1eb6e4a8e` in the shared UI library (field-mapping Create Event wizard, ontology email fixes, reuse-governance item 4). None are SPA screens; master CI built the SPA green.
- **Rollback:** rerun the workflow on `c1eb6e4a8e`. The previous SPA already ran against BFF `e46b6b453b`, so no paired BFF rollback is needed for an SPA-only rollback.
- **Bundle check** (live `app.js`): contains `assignable-people`, `creatableEventTypes`, `eventTypeId`, `allowedStatuses`, `canChangeStatus`; `canCreateTask` (old Event/Task radio rule) is absent. The `event:` deep-link token is built at run time, so it has no literal.

## Step 3 — owner retest (pending)
Use a Collaborate user and a View Only user on a matter, a project and a work assignment.

| # | Check | Result |
|---|---|---|
| 1 | Add event: Type is required and lists only allowed types; Due Date becomes required for a type that needs it | |
| 2 | Priority, Assigned To and Description are saved and shown on the new row | |
| 3 | Assigned To lists only people with access to the record, "Me" first | |
| 4 | An event row opens the event over the record; Close returns to the list | |
| 5 | Copy link → the link opens the event on its own; a user without access gets "no longer available" | |
| 6 | Collaborate: Edit (type read-only) and Change status (any status) work and the list shows the new values | |
| 7 | View Only: no Add event, Edit, Change status or Delete | |
| 8 | uac-r2's post-deploy gate shows no new 5xx | |
