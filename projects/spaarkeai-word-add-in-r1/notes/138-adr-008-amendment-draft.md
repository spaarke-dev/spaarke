# ADR-008 Amendment A1 — wording APPROVED by the owner (2026-11-25)

> Path B (root CLAUDE.md §6.5), chosen by the owner 2026-11-25 for task 138; wording approved the same day.
> This is the text that goes into both ADR files. It merges with task 138's code, in the same PR.
> Peer review (uac-r2, field-mapping-r1) may correct facts; any change of meaning goes back to the owner.

---

## Part 1 — full ADR (`docs/adr/ADR-008-authorization-endpoint-filters.md`), appended section

## Amendment A1 (2026-11-25): a resource the service finds is checked where it is found

> **Status**: Accepted (resolution path **B — amendment**, per root CLAUDE.md §6.5; owner-approved 2026-11-25).
> **Driver**: `spaarkeai-word-add-in-r1` task 138 (#2209). It also covers task 121, which shipped the same pattern earlier.

### Why

ADR-008 moved resource checks out of global middleware because middleware runs before routing and cannot see the
resource. Endpoint filters fix that for every resource the **request names**: a route id, a body field.

Some requests act on a resource the request does **not** name. The service finds it with a lookup only it can do:
- **Email re-save (task 138).** The save matches an earlier save by idempotency key, then adds the attachments the
  first save left out, as children of that earlier `.eml` document.
- **Email reconcile (task 121).** The save matches an existing unfiled communication by message id, then files it to
  the picked record.

A filter runs before the service, so it can check such a resource only by repeating the service's lookup. Then two
pieces of code each decide which resource the request is about. If they ever disagree, the filter approves one
resource while the service changes another. Checking the resource where the lookup finds it rules that out.

### The rule

A resource check MAY run inside the service, instead of in an endpoint filter, only when **all** of these hold:

1. **The request does not name the resource.** Only a lookup inside the service identifies it. Anything the request
   names is still checked by the route's filters, as before.
2. **It runs as the caller**, with the caller's own token or principal, never as the app alone. (The shared
   document helper's app-only narrowing step is allowed: it only removes documents.)
3. **It runs before any read of the resource's content and before any write**, including a job or queue record
   the action would create. Only the lookup that identifies the resource may come first.
4. **It checks the exact id the lookup returned, and the action uses that same id.** There is no second lookup
   that could pick a different resource. Re-reading the same id, for example just before a write, is allowed.
5. **It fails closed.** A denial, an unknown answer or a fault refuses, or falls back to the request's ordinary
   answer that changes nothing. It never goes ahead.
6. **It uses the shared check**: `InternalPlaneDocumentNarrowing.AuthorizeDocumentsAsync` for a document;
   `CallerRecordAccessProbe` for any other record when the caller is a system user; for a contact caller, the
   external plane's evaluator (`AccessibleRecordSetService`). It never adds a private check.
7. **It is tested and recorded.** Tests show the denial path and the fault path, each writing nothing. The
   route's entry in `RouteAuthorizationGuardTests.Ledger.cs` names the service method that makes the check.

### What A1 does not change

- Every resource the request names is still checked by an endpoint filter. A1 adds no exception for those.
- Global middleware still makes no resource checks.
- Field-mapping-r1's project exception (Path A) for checks that depend on the request body —
  `CallerWriteAuthorization`'s create checks and the caller-Read probe of a mapping source
  (`MappingSourceReadProbe`) on create, push and the send-time fill — is a different case and stays as it is.
  Those are resources the request names. A1 neither widens nor replaces it.

### Instances at adoption (known)

| Where | Resource the service finds | Check | On refusal |
|---|---|---|---|
| `OfficeService` email re-save add (task 138) | the earlier save's `.eml` document | Write via `AuthorizeDocumentsAsync`, before the planner reads anything and before the add job's row | 403 `OFFICE_009`; nothing read, queued or written |
| `ReconciledEmailFiling` (task 121) | the matched unfiled communication | Write on it via `CallerRecordAccessProbe` with the caller's token, before the filing write. AppendTo on the save's record is the route's `EntityAccessFilter` (`entity.associate_document`) | the save still completes; the communication is left unfiled |

This list is not complete. Other in-service checks cite A1 as their owners review them; many check a resource the
route already named, as a second check, and are not A1 cases.

### Enforcement

- **Condition 6, for documents**: ArchTest
  `ExternalDocumentNarrowingGuardTests.EveryDocumentResourceGrant_GoesThroughTheNarrowingHelper`
  (`tests/Spaarke.ArchTests/ExternalDocumentNarrowingGuardTests.cs`) fails any document grant that bypasses the
  shared helper.
- **Conditions 3–5**: each instance's contract tests (deny and fault each write nothing). Task 138:
  `OfficeEmailReSaveFailClosedTests`.
- **Condition 7**: a reviewer checks the ledger entry. This stays in prose because the ledger's descriptions
  are free text. A mechanical check would need a map from services to routes, and none exists.

---

## Part 2 — concise ADR (`.claude/adr/ADR-008-endpoint-filters.md`)

Under **✅ MUST**, change the first bullet to:

- **MUST** use endpoint filters for resource authorization of every resource the request names
  (Amendment A1: a resource only the service's own lookup identifies is checked in the service, under A1's seven
  conditions)

Add a section before "Integration with Other ADRs":

> ### Amendment A1 (2026-11-25): a resource the service finds
> When the request does not name the resource and only a lookup inside the service identifies it (for example an
> idempotency match or a message-id match), check it in the service, where it is found. Do not repeat the lookup in
> a filter: two lookups can disagree. Conditions: run as the caller; run before any content read or write (a job
> row counts as a write); check the exact id the action then uses; fail closed; use the shared check
> (`AuthorizeDocumentsAsync` for documents, `CallerRecordAccessProbe` for other records with a system-user caller,
> the external evaluator for a contact); test the deny and fault paths; name it in the route's ledger entry.
> Full text: [ADR-008 A1](../../docs/adr/ADR-008-authorization-endpoint-filters.md#amendment-a1-2026-11-25-a-resource-the-service-finds-is-checked-where-it-is-found).

---

## Part 3 — the code side, in the same PR

- `RouteAuthorizationGuardTests.Ledger.cs`, the `Api/Office/OfficeEndpoints.cs` entry: add one sentence naming
  both in-service checks (`OfficeService.AuthorizeEmailAttachmentAddAsync`, `ReconciledEmailFiling`) under A1.
  This is a shared file, so whoever lands second rebases.
- `OfficeEndpoints.cs`: change the comment about where the save's authorization sits so it cites A1.
- The 138 design note: record ADR-008 as resolved by A1, replacing "open with the owner".
- `spec.md` ADR Tensions: add an ADR-008 row, "Path B, Amendment A1 (owner 2026-11-25)". This is a spec
  change, covered by the owner's go on this draft.
- uac-r2 and field-mapping-r1 get the final text for review before merge. Their comments count as review, not
  approval.
