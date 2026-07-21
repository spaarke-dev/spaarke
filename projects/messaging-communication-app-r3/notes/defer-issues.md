# Deferred Work & Issues — messaging-communication-app-r3

> Source of truth for deferred/discovered items. Mirror to GitHub Issues via `/defer` before push (per project CLAUDE.md).
> Status: **not yet filed to GitHub** — file at PR/push time.

| ID | Type | Summary | Concrete failure without it | Discovered |
|----|------|---------|-----------------------------|------------|
| ISS-001 | ISS | `participant=` OData facet in `CommunicationThreadReadService` (~line 524) embeds the address value into the query string WITHOUT `Uri.EscapeDataString` (same class as task-003 W1, which was fixed for the new `ListThreadsAsync` search path only). | A participant address containing `&`, `#`, `+`, `%`, or a space (email `+`-tags are common) breaks out of the OData value → malformed query → Dataverse 400 → 500 to the caller. **Impersonation-contained** (no over-disclosure — any injected clause still runs under the caller's `MSCRMCallerID`), so it's robustness/correctness, not a security leak. Pre-existing (not introduced by R3). | 2026-07-20, task 003 Step 9.5 gate |

**Fix approach for ISS-001**: apply `Uri.EscapeDataString` to the participant literal after quote-doubling at the embed point (mirror the task-003 W1 fix), OR add a whole-value encoding contract to the `RetrieveMultipleImpersonatedAsync` seam so every caller is covered. Out of task-003 scope (task 003 fixed only its own new search path).

**Note for deploy/UAT** (not a defer — a verify item, tracked in `task-003-notes.md`): the composite-cursor GUID `lt` comparison (FR-16 paging) is validated against the seam mock; confirm real Dataverse OData GUID-ordering semantics during deploy/UAT (Phase 6 task 050).
