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

For the **association** the first draft of this note proposed the sibling seam
`GetCallerRecordAccessAsync(userId, entitySetName, recordId, token, ct)` (`AuthorizationService.cs:255-283`),
on the correct principle that read on a document does not by itself authorize naming its parent —
`defer-issues.md` D-032-1 is the record of this project getting that exact inference wrong once already.

**Refined 2026-09-19, and the refinement is strictly less disclosive.** Do not put a non-matching
association's NAME on the wire at all. Return:

- the **document's name**, gated on document read (§1.3); and
- whether the colliding document's association **matches the caller's own selected target record** — a
  boolean comparison, not a disclosure.

When it matches, naming the record reveals nothing: it is the record the caller just chose from the
authorized picker, so they can already read it. When it does not match, the pane says only that the document
is filed elsewhere, and withholds the retry (§4). A caller therefore never learns the name of a record they
did not already name themselves.

Two consequences worth stating: it removes the need for a second authorization call on the refusal path —
which is also the latency concern escalation trigger (d) names — and it removes the need to model
`GetRecordAccessAsync` in the test world, whose `IAccessDataSource` double
(`OfficeEndpointsContractTests.cs:1843-1848`) sets up **only** `GetUserAccessAsync`. That is a fixture fact
that would otherwise have had to change to accommodate a design choice, which is the wrong way round.

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

## 5A. Three fixture gaps this change exposed (found by running, not by reading)

`OfficeVersionSaveWorld` had modelled none of what the refusal now reads. All three were invisible while
nothing read them, and all three surfaced the moment something did.

| Gap | What the fake did | Why it was invisible | Fixed by |
|---|---|---|---|
| **The `ColumnSet` was ignored** | `RetrieveMultiple`'s collision branch returned `new Entity(name, id)` — an id and no attributes | Nothing had ever asked for more than the id | `ProjectDocument(row, columnSet)` |
| **The association was discarded** | `ApplyUpdate` dropped `MatterLookup`, so every save-created row was filed **nowhere** | Nothing had ever read a document's association | `row.MatterId = update.MatterLookup ?? row.MatterId` |
| **The display name was conflated** | `CreateDocument` recorded the request's `Name` only as `FileName` | `sprk_documentname` vs `sprk_filename` never mattered to a test before | `DocumentName = request.Name` |

**The second one is the instructive failure.** Three *pre-existing* collision tests went red the instant the
refusal began consulting the association — each asserting `existingDocumentId` for a document their own save
had filed to the target matter. In production that assertion is correct: `DocumentAssociationMap.TryApply`
(`Spaarke.Dataverse/Models.cs:78-110`) puts the lookup on the UPDATE and `DataverseServiceClientImpl:922-931`
writes `sprk_matter`, so those documents genuinely *are* filed there.

The tempting move — relax the three assertions so they match the fake — would have left them green while
deleting the guarantee the version retry depends on: **that the target is filed where the caller is filing.**
That is precisely the property #1005 is about. The failure was the fake's, and the fix belongs in the fake.

Worth stating as a general lesson, since this project has now hit it three times in one task: a fake that
models only what was previously read will fail *for the wrong reason* the first time something new reads it,
and the red will point at the new code rather than at the model. All three gaps here were caught only because
the reproduce-first discipline demanded a specific expected failure and these did not match it.

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

---

## 7. Verification (2026-09-19 — task-execute Steps 9 and 9.5)

| Gate | Result |
|---|---|
| BFF `Sprk.Bff.Api.Tests` | **12,379 passed / 0 failed / 56 skipped** (12,435 total) |
| ArchTests | **191/191, 0 failed** — the log shows it rebuilt `Sprk.Bff.Api.dll`, so this is not a stale-binary green |
| Gated jest (46 suites, by path) | **46/46 suites · 540 tests** — the 537 baseline plus this task's 3 |
| BFF publish size | branch **45.430 MB** vs a fresh `origin/master` @ `e0a6f87c4` at **45.355 MB** = **+0.075 MB**. Release, framework-dependent linux-x64, `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, **PDBs included** (4 each). Ceiling ≤60 MB; escalation at +5 MB. Both sides built in isolated worktrees, per CLAUDE.md §10 |
| CVE | `dotnet list package --vulnerable --include-transitive` → *"no vulnerable packages"* |
| `/code-review` + `/adr-check` | **0 critical, 0 ADR violations.** ADR-008 ✅ · ADR-010 ✅ · ADR-012 Path A ✅ · ADR-021 ✅ · ADR-038 ✅ |

**Two things the gates checked rather than assumed**, because either could have made the fix only half-real:

- **The gate has no bypass.** `MapSaveErrorToProblem` has exactly ONE call site in the codebase, and it is the
  gated one. A collision cannot leave by an ungated path.
- **`AuthorizationService` is registered unconditionally** (`SpaarkeCore.cs:26`), not behind a feature flag.
  The handler takes it as a parameter, so a conditional registration would have failed at **runtime**, not at
  build — the asymmetric-registration rule in CLAUDE.md §10 bullet 6 (RB-T028).

**Two findings, both fixed, neither a logic defect:**

1. `OfficeEndpoints.cs` — the new method had been inserted *between* `MapSaveErrorToProblem`'s doc comment and
   the method it documents, so the new method carried **two** `<summary>` tags and `MapSaveErrorToProblem` had
   none. The build never complained because `GenerateDocumentationFile` is not set for this project, so CS1571
   cannot fire despite repo-wide `TreatWarningsAsErrors`. Comment restored; rebuild clean (0 warnings, 0 errors).
2. §5A above was headed *"Two fixture gaps"* over a table listing **three**, with the prose still reading
   "neither half" / "Both" / "twice". A third row had been added without updating the surrounding text.

**One false alarm, recorded so nobody re-investigates it.** The first gated-jest pass reported
`1 failed, 45 passed of 46` — `SaveFlow.matterTypeQuickCreate.test.tsx` (task 038, untouched by this task)
timing out at 60 s, its suite taking 386 s. Re-run alone it passes **3/3 in 21.9 s**, the timed-out test itself
in **7.9 s** — a ~17× margin. It had been sharing the machine with an ArchTests build and a full Release
publish. Two lessons worth keeping: that pass's shell **exit code was 0 despite the failure**, because the
command piped `jest` through `tail` and a pipeline reports the *last* command's status — read the summary line
or use `${PIPESTATUS[0]}`; and heavy parallelism can manufacture a red in a 60 s-timeout React suite, so
isolate before believing one.
