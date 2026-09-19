# Task 055 — design record: the collision prompt must name its target, and must not silently discard the selected record

> Fixes [#1005](https://github.com/spaarke-dev/spaarke/issues/1005) / ISS-006. Diagnosis chain:
> [`042-uat-findings-2026-09-18.md`](042-uat-findings-2026-09-18.md).
>
> **§1 is written BEFORE any server code, as the POML's escalation trigger (b) requires.** Every file:line
> below was read in this session against the working tree, not recalled.

---

## 1. Escalation trigger (b) — the authorization answer

> **The trigger, verbatim:** *"If returning the colliding document's NAME or ASSOCIATION in the 409 would
> disclose something the caller cannot already read — STOP and escalate. Note the id is ALREADY returned
> today, so the question is narrowly whether name/association are a FURTHER disclosure."*

### 1.1 What is disclosed today, and on what authority

`ResolveNameCollisionAsync` (`OfficeService.cs:1100-1200`) resolves the colliding row via
`FindDocumentIdByLocationAsync(driveId, fileName, ct)` (`OfficeDocumentPersistence.cs:401-435`) and returns
its **GUID** on the refusal (`:1198`), which reaches the wire as the `existingDocumentId` ProblemDetails
extension (`OfficeEndpoints.cs:558-570`).

**That lookup performs no authorization of any kind.** It is a `QueryExpression` over
`sprk_graphdriveid` + `sprk_filename` through `IGenericEntityService` (`:417-424`) — the app-only generic
seam. Nothing asks whether the caller may see the row it finds.

So today's disclosure to any authenticated caller who can trigger a collision is:

1. **that a document with this exact filename exists in this container** — which is *unavoidable*, because
   the refusal itself is that fact. `OFFICE_020` cannot both be honest and conceal it.
2. **that document's opaque GUID** — avoidable, and currently ungated.

### 1.2 Is name + association a FURTHER disclosure? **Yes — materially.**

A GUID is non-semantic: it identifies without describing. A document **name** and the **record it is filed
to** are the opposite — they are the description. `Project Falcon — Termination Letter`, filed to
`Matter PAT-191111 (Acme v. Beta)`, tells a caller with no rights on that document that the matter exists,
who it concerns, and what is being done on it. On a secure matter the name is frequently *the* sensitive
fact — the same argument `RecordSearchAuthorizationFilter` already makes in its own remarks, and the reason
task 025's POML flagged this body as an enumeration concern in the first place.

The gap is real today and not hypothetical: `OfficeVersionSaveAuthorizationFilter` (`:50-91`) requires
**`write`** on the target before a version save may land, so a caller without rights is already refused the
*action* — but they are still **offered** it and **shown** the id. Enforcement and disclosure are out of step.

**Verdict: this is a further disclosure. Per trigger (b) it may not ship ungated.** The trigger does not
fire as a STOP, because a gate exists that removes the disclosure rather than accepting it — §1.3. If that
gate turns out to be unavailable at implementation time, the trigger fires and the task escalates.

### 1.3 The gate

Return the display fields **only when the caller holds `Read` on the colliding document.** Absent that,
return the refusal carrying `fileName` **only** — no name, no association, and **no `existingDocumentId`**.

Dropping the id when read is absent is not scope creep; it closes the pre-existing half of the same leak,
and it costs the caller nothing they could have used: without `write` they would be refused the retry by
`OfficeVersionSaveAuthorizationFilter` anyway. The pane already renders exactly this state correctly —
"only Keep both when the server resolved no owning document" is pinned by
`SaveFlowCollision.test.tsx:194-207` and `errorMessages.collision.test.ts` ("omits
collisionExistingDocumentId…"). **The unauthorized case therefore needs no new client behaviour at all**;
it reuses a shipped, tested path.

**The seam is caller-scoped, verified in code** (this mattered — see §5):
`AuthorizationService.GetCallerAccessAsync(userId, resourceId, userAccessToken, ct)` forwards the caller's
bearer token *"so DataverseAccessDataSource takes its OBO path and queries Dataverse AS THE USER rather
than as the app"* (`AuthorizationService.cs:223-225`), and fails **closed** with
`sdap.access.deny.no_caller_token` when no token is present (`:54-72`), explicitly refusing to degrade to
app-only. `AccessRights` is `[Flags]` — `None=0, Read=1<<0, Write=1<<1, Delete=1<<2`
(`IAccessDataSource.cs:122-136`).

For the **association** (a matter/project/invoice/work-assignment name, a *different* record from the
document) the sibling seam applies: `GetCallerRecordAccessAsync(userId, entitySetName, recordId, token, ct)`
(`AuthorizationService.cs:255-283`). Read on the document does not by itself authorize naming its parent —
`defer-issues.md` D-032-1 is the record of this project getting that exact inference wrong once already.

---

## 2. Where the gate lives — endpoint, not service

**Decision: the caller-scoped check runs in the `SaveAsync` endpoint handler, not inside `OfficeService`.**

`OfficeService`'s injected dependencies (`OfficeService.cs:37-71`) are job status, processing jobs, email
enrichment, persistence, queue, uploader, options, container resolver, core ancestors, membership events,
email capture, Dataverse client, generic entity service, record creation, profile dispatcher, logger.
**There is no `AuthorizationService` and no authorization concern of any kind.**

- **ADR-008** puts resource authorization on the endpoint, precisely because that is where the request
  context lives; `.claude/constraints/auth.md` adds *"MUST NOT create new service layers for auth."*
- Every working precedent in this codebase is handler-side: `ChatDocumentEndpoints.cs:998-1011` and
  `RecordSearchEndpoints.cs:136-145` both pair `CallerResolution.ResolveObjectId(httpContext.User)` with
  `TokenHelper.ExtractBearerTokenOrNull(httpContext)`, call `AuthorizeAsync` with `Operation = "read"`, and
  answer `ProblemDetailsHelper.Forbidden`.
- Injecting `AuthorizationService` into `OfficeService` would give a class with fifteen non-authorization
  dependencies a sixteenth with an entirely different reason to change (CLAUDE.md §11.5).

**Rejected alternative — fetch the display data at the endpoint after the auth check.** It reads cleaner
(nothing unauthorized ever leaves the service) but requires a **second Dataverse lookup**, which the POML
forbids and CLAUDE.md §11 argues against. The accepted shape widens the ONE existing projection and carries
the result in-process, where it never crosses a trust boundary; the endpoint strips it before it reaches the
wire.

**The invariant this rests on, stated so a reviewer can check it:** *the widened fields exist only inside
the process between `ResolveNameCollisionAsync` and `MapSaveErrorToProblem`, and the only code that can put
them on the wire is the mapper, which is gated.* `MapSaveErrorToProblem` (`OfficeEndpoints.cs:512-582`) is
`private static` with no `HttpContext`, so the gate sits in the handler immediately before it — or the
mapper gains the already-resolved decision as a parameter. Either keeps one gated exit.

---

## 3. Half (1) mechanics — one projection, no new lookup

`FindDocumentIdByLocationAsync` (`OfficeDocumentPersistence.cs:401-435`) is the whole data fetch. Its
projection is a single line:

```csharp
ColumnSet = new ColumnSet(DocumentIdAttribute),   // :419
```

Widen that `ColumnSet` to add `sprk_documentname` and the **four direct association lookups**
(`sprk_matter`, `sprk_project`, `sprk_invoice`, `sprk_workassignment` — the closed set from
`026-slot-scope-decision.md` §1, in that precedence order), and change the return from `Guid?` to a small
record. The two match criteria (`:422-423`) are unchanged.

Task 026's `ResolveRelatedRecordDisplayAsync` is reused for turning a lookup into a display name + number —
it already handles the primary-name trap (a Matter's lookup name IS its number), so this task does not
write a second display resolver.

**The method's fail-open contract is preserved** (`:397-399`, `:427-434`): a failed lookup answers `null`,
the pane still offers "Keep both", and it never fabricates an id. Widening the projection must not turn a
best-effort read into a hard failure.

---

## 4. Half (2) — the offer is withheld unless the target is already filed where the user is filing

The POML named two shapes; a third is better and is what the criteria actually demand.

**Chosen: offer "Save as new version" only when the colliding document's association MATCHES the user's
selected Related-to record.** Otherwise the pane shows the refusal (naming the document, if authorized) with
**"Keep both" only**.

Why this and not the two named alternatives:

- It targets the **observed** failure exactly. The 2026-09-18 orphan had **no association at all** — so a
  test phrased as "the association *contradicts* the selection" would not have caught it. "Matches, or it
  isn't offered" covers both the contradicting case and the absent-association case that actually happened.
- Nothing is silently discarded, because on the offered path the target is *already* filed to the record the
  user chose — so `targetEntity` being omitted (task 023 D-4/D-5, unchanged) discards nothing.
- It requires **no change to the version-save write path**, so escalation trigger (a) — never re-associate —
  is satisfied by construction rather than by care.
- **Rejected: carry `targetEntity` and refuse server-side.** It adds contract surface and a new refusal
  state to reach the same end, and it would let the pane offer an action the server is guaranteed to reject.

---

## 5. Doc drift found while verifying §1.3 (recorded, not fixed here)

`.claude/constraints/auth.md:239-249` states that `AuthorizationService` *"always passes
`userAccessToken: null`… it answers 'can the application see this record', not 'can this user see it'"* and
instructs: *"Do not rely on `AuthorizationService` for caller-scoped access decisions."*

**That is stale.** It describes the pre-`unified-access-control-r2`-task-004 state. The code now fails
closed without a caller token (`AuthorizationService.cs:54-72`) and queries as the user (`:223-225`), and
`notes/defer-issues.md` D-032-1 (2026-09-10 — later than the constraint file's 2026-08-20 correction)
already records the remediation.

This matters beyond bookkeeping: taken at face value, that sentence says the gate in §1.3 **cannot work**,
and the honest response would have been to escalate trigger (b) as unfixable. CLAUDE.md §2 ("code wins,
docs lag") is what resolved it. Not fixed inside task 055 — `.claude/constraints/auth.md` is a shared
constraint file on `unified-access-control-r2`'s surface, and editing it as a side effect of a save-path
task is the kind of silent scope widening CLAUDE.md §11 forbids. **Surfaced to the owner for routing.**

---

## 6. Explicitly NOT in scope

- **Re-associating on a version save** — escalation trigger (a). It re-files another user's document onto
  the caller's record; worse than the defect.
- **`Untitled Document.docx` as the default upload name** — the multiplier that makes this reachable
  (`WordAdapter.getSubject()` → `properties.title || 'Untitled Document'`). Open question 8 of
  `025-residual-collision-surface.md`; owner's call.
- **The live dev row** holding another document's content/profile/index chunks. Operator confirmed
  2026-09-19 that dev-only data can be ignored.
- **The five unparseable POMLs** (005/010/018/028/053) found while fixing the validator — separate finding.
