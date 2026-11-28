# ADR-008: Authorization execution model — endpoint filters over global middleware

| Field | Value |
|-------|-------|
| Status | **Accepted** |
| Date | 2025-09-27 |
| Updated | 2026-11-25 (Amendment A1) |
| Authors | Spaarke Engineering |

---

## Related AI Context

**AI-Optimized Versions** (load these for efficient context):
- [ADR-008 Concise](../../.claude/adr/ADR-008-endpoint-filters.md) - ~85 lines, decision + constraints + patterns
- [API Constraints](../../.claude/constraints/api.md) - MUST/MUST NOT rules for API development
- [Endpoint Filters Pattern](../../.claude/patterns/api/endpoint-filters.md) - Authorization filter code examples

**When to load this full ADR**: Authorization pattern decisions, middleware ordering details

---

## Context

Multiple authorization middlewares (`DataverseSecurityContext`, `DocumentAuthorization`, `UacAuthorization`) complicated the pipeline and ran before routing had resolved route values (e.g., `documentId`). Resource-based authorization is cleaner when enforced at the endpoint level.

## Decision

| Rule | Description |
|------|-------------|
| **Minimal global middleware** | Keep global middleware limited to cross-cutting concerns (security headers, exception handling, correlation/telemetry) |
| **Endpoint-level auth** | Resource-based authorization via endpoint filters (Minimal API) or policy/handlers (Controllers) |
| **Remove global auth** | No global middlewares performing resource checks |

## Consequences

**Positive:**
- Authorization logic sits next to endpoints, sees route values and DTOs, easy to reason about
- Simpler pipeline; fewer unexpected interactions

**Negative:**
- Slightly more boilerplate at endpoint definition (one filter per protected route)

## Alternatives Considered

Monolithic `DocumentSecurityMiddleware`. **Rejected** because it lacks route/body context and becomes a god-middleware.

## Operationalization

### Middleware Pipeline

| Order | Middleware | Purpose |
|-------|------------|---------|
| 1 | Standard ASP.NET (auth, routing) | Framework |
| 2 | Exception handling + security headers | Consistent RFC7807 errors + hardening |
| 3 | Endpoint executes | With endpoint authorization filter(s) |

### Authorization Patterns

| Pattern | Implementation |
|---------|----------------|
| Single resource | Endpoint filter: `DocumentAuthorizationFilter(Operation.Read)` |
| AI analysis | Endpoint filters via `AnalysisAuthorizationFilterExtensions` helpers |
| Composite checks | Handler calls `AuthorizationService` directly |
| Bulk operations | Handler calls `AuthorizationService` for each item |
| List endpoints | Authorization at query construction (scoped to caller) |

## Exceptions

Bulk/list endpoints apply authorization at query construction time inside handlers (scoped to caller).

## Success Metrics

| Metric | Target |
|--------|--------|
| Auth-related defects | Fewer |
| 401/403 mapping | Clear |
| Pipeline length | Short |
| Endpoint security | Readable |

## Compliance

**Code review checklist:**
- [ ] No global authorization middleware performing resource checks
- [ ] Protected endpoints have explicit authorization filter
- [ ] Bulk handlers authorize each item
- [ ] List queries scoped to caller's permissions

## AI-Directed Coding Guidance

- Default: `.RequireAuthorization()` on the route group + add explicit endpoint filters for resource checks.
- Use existing filters/helpers:
	- `DocumentAuthorizationFilter` for document operations
	- `AnalysisAuthorizationFilterExtensions` for analysis routes
- Do not add new global middleware to perform resource authorization; it will not have the right route/body context and will be harder to audit.

---

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
| `OfficeService.PlanNamedEmailAttachmentAddAsync`, the named add (`email.savedDocumentId`, word-add-in-r1 task 141; the add that names no record, task 143) | none beyond the named `.eml`: the request names it, so Write on it is the route's endpoint filter, as for any named resource. The service adds the filing rule and the container guard below | One "filed" rule (task 143; owner 2026-11-27: an email's `.eml` and its communication are always filed to the same record), the rule `resolve-email-identity` reports to the pane: the `.eml` is filed by its OWN record links when it has any (a non-empty link in any `sprk_document` record-link column other than its communication, `sprk_relatedcommunication`, and its email activity, `sprk_email`); with none, to the record its `sprk_communication` is filed to (its first populated `sprk_regarding*` lookup, read by the identity route's own reader, task 132); unfiled = neither. With a record: the `.eml` must be filed to that record by the rule (with own links, every own link of that record's type points at it), and the added attachment documents are filed to that record. With no record (`targetEntity` absent): the `.eml` must be unfiled by the rule. Then the container guard: the destination container this request resolves (the record's, or with no record the caller's) must be the `.eml`'s own container. All before any job row or queue message | 403 `OFFICE_009`; nothing queued or written. A read of the `.eml`, or of its communication when the rule needs it, that faults refuses with the retryable 502 `OFFICE_014`, also writing nothing |
| `DocumentEmailIdentityFilter.ReadSavedAttachmentNamesAsync` (`resolve-email-identity`'s `savedAttachmentNames`, word-add-in-r1 task 141; moved from `FileAccessEndpoints` and widened by task 143) | the saved `.eml`'s child documents (its saved attachments), and (task 143) the email's standalone attachment documents: not the archive, no `.eml` parent, linked to the `sprk_communication` found by the email's Message-ID. On `not_saved` (no `.eml`, or one the caller may not read) only the standalone ones, the same lookup in both branches | Read on each via `AuthorizeDocumentsAsync` (root grants, then the external narrowing), before any name is returned: one call over all of them first (it allows only when every one is allowed), and only when it denies, one call per document; no content is read | the document's name is left out; a read that throws, or a check (of all, or of one) that answers the system-failure deny (the authorization service reports a fault that way, not by throwing), omits the whole field (the identity answer itself is unchanged; a `not_saved` answer with no readable standalone attachment is byte for byte the old one) |
| `OfficeEndpoints.WithholdContentDuplicateIdentityIfUnauthorizedAsync` (the content-duplicate answer of `POST /office/save`, word-add-in-r1 task 143, owner A1) | the existing document an Attachment save's bytes are identical to: the service's content de-dup finds it app-only by hash (`ContentDedupDetector`), and reads its name (`sprk_documentname`) app-only. Nothing is created, written to it or re-filed in any branch; the check governs only whether the answer names it | Decided at the ENDPOINT, not in the service, by the helper the name-collision refusal uses (`CallerMaySeeDocumentAsync`): Read on that exact id via `GetCallerAccessAsync` with the caller's token, then THE external document narrowing (`InternalPlaneDocumentNarrowing.ForCallerAsync`) | `existingDocument` (id + name) is omitted and `contentDuplicate: true` stays, so the pane says only "Already in Spaarke". Fail closed: no Read, a document the narrowing hides, a narrowing fault or an access check that throws all withhold |
| `OfficeEndpoints.WithholdContentDuplicateJobIdentityIfUnauthorizedAsync` (the Office job status, polling `GET` and the SSE stream, word-add-in-r1 task 143, owner A1) | the existing document a save resolved to through content de-dup (job phase `DeduplicatedToExisting`, `OfficeService.ContentDuplicatePhase`): the job result's `artifact` (id, webUrl, speFileId, containerId) names it, found app-only by hash | The same shared helper (`CallerMaySeeDocumentAsync`): Read on that id with the caller's token, then THE external document narrowing; applied on the polling read and to every status the stream reads from the job record | `artifact` is omitted and the job's status stays; the pane shows "already in Spaarke, nothing new was saved" with no link. Fail closed: no caller id, no Read, a document the narrowing hides, a narrowing fault or a throwing check all withhold. Jobs that created their own document are unchanged, with no access check |
| `ReconciledEmailFiling` (task 121; confirm, task 140) | the matched unfiled communication; or (task 140) the matched communication already filed to the save's record as its only regarding, whose status the save confirms | Write on it via `CallerRecordAccessProbe` with the caller's token, before the filing write or the confirm (status-only) write. AppendTo on the save's record is the route's `EntityAccessFilter` (`entity.associate_document`) | the save still completes; the communication is left unfiled, or filed but unconfirmed |

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
