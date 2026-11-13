# Task 008 notes — BFF levels on /me, event type "task", isTask

## Gate: uac-r2 UAC review (2026-11-13) — PASS WITH CHANGES

Proposal sent 2026-11-13. It covered: (A) `/me` `records[]`, (B) `eventType` closed set {"task"}, (C) `isTask` on the event lists.

uac-r2's conditions, all folded into the POML constraints:
- **A — source.** The principal's own `MatterAccess` / `WorkAssignmentAccess`, filtered to Read. This is the same derived view as `AccessibleMatterIds` / `AccessibleWorkAssignmentIds`. Write it once as a helper next to `ReadableProjects` and project each entry through `ExternalAccessLevels.ToDisplayLevel`.
  - Why this source: `RightsForRoot` reads exactly those dictionaries, so the label equals what the routes admit, by construction.
  - **A1.** Parity test, including a None key and a vetoed key; their ids must be absent from the response body.
  - **A2.** For `WriteAccessPerRecord` callers the label is the composed READ view. The SPA never gates write actions on it. Add a test.
  - **A3.** Additive only. `projects` is unchanged, and `/me` makes no new reads.
- **B — eventType.** Closed set; any other value is 400 `sdap.event.type_not_allowed`. The Task type id lives in one shared constant.
  - **B1.** For a DataverseConfirmsWrites caller (a systemuser), probe AppendTo on the Task type row first (app-only, naming the systemuser). No → 403 with the existing refusal; unanswered → 503.
  - For a contact, the closed set is the authorization.
  - Test both paths.
- **C — isTask.** Read the lookup server-side only. Emit only the boolean. Add an FR-18 ReadableColumns entry with its review note.
- **D — to-do routes.** Under uac-r2 round 10 item 1, a route with no caller is deleted, not left mapped. The external SPA is the only caller of the external to-do routes, so uac-r2 wants them deleted (ledger and route count updated) in the same change that removes the SPA's last call. Keeping them for a rollback window is an owner call.
  - **This conflicts with the owner directive of 2026-11-12:** the to-do BFF routes stay until a filed retirement.
  - **Owner decision pending.** Until it arrives, the directive stands: the routes stay and a retirement issue is filed.
