# Task 005 — lifting the `AccessRights.Read` ceiling: the mapping table and the design

> **Date**: 2026-08-21 · **Spec**: FR-04 · **Finding**: A-20 (Read-ceiling half)
> Binding obligations discharged: task 003 (`AppendToAccess`) · task 006 (capabilities light up).

---

## 1. What was wrong

`DataverseAccessDataSource.QueryUserPermissionsAsync` probed one question — *can this principal
retrieve the record?* — and answered a different one:

```csharp
// Grant Read access - if user needs Write/Delete/etc., Dataverse will enforce that separately
new PermissionRecord(userId, resourceId, AccessRights.Read)
```

**That comment was the defect.** On the MDA, Dataverse does enforce Write/Delete natively. On the
SPA/Teams surface — the surface this project exists for — the BFF filter *is* the enforcement point, so
nothing enforced them. The snapshot could never carry more than `Read`, and
`OperationAccessRule` computes `(userRights & required) == required`, so **every** policy above Read was
unsatisfiable for **every** caller regardless of privilege:

| Operation | Required | Result before task 005 |
|---|---|---|
| `upload_file` / `driveitem.content.upload` | Write \| Create | always deny |
| `create_container` | Create \| Write | always deny |
| `download_file` / `driveitem.content.download` | Write | always deny |
| `delete_file` / `driveitem.delete` | Delete | always deny |
| `share_document` / `driveitem.createlink` | Share | always deny |
| `entity.associate_document` | AppendTo | always deny |

## 2. The rights-mapping table

Dataverse `RetrievePrincipalAccess` → `AccessRights`. This is the table the POML asks for; it lives in
code at [`DataverseAccessRightsMapper.cs`](../../../src/server/shared/Spaarke.Dataverse/DataverseAccessRightsMapper.cs).

| Dataverse right | `AccessRights` flag | Value | Gates |
|---|---|---|---|
| `ReadAccess` | `Read` | 1 | preview, get, versions.list, search, thumbnails |
| `WriteAccess` | `Write` | 2 | **download**, update, replace, checkin/checkout, versions.restore |
| `DeleteAccess` | `Delete` | 4 | delete, permanentdelete, (with Write) move |
| `CreateAccess` | `Create` | 8 | (with Write) upload, create.folder, container.create; (with Read) copy |
| `AppendAccess` | `Append` | 16 | — no operation requires it today |
| `AppendToAccess` | `AppendTo` | 32 | **`entity.associate_document`** → `POST /api/office/save` |
| `ShareAccess` | `Share` | 64 | createlink, permissions.add/delete, container.permissions.* |
| *(unrecognised)* | `None` | 0 | contributes nothing — see §5 |

**`Write` gating download is deliberate**, not an oversight: `OperationAccessPolicy.cs:37` records it as
a Spaarke security policy ("download requires Write, not just Read"). Task 005 does not revisit it.

## 3. The fix: reconnect wiring that was already there

The discovery that shaped this task: **`MapDataverseAccessRights` and `PrincipalAccessResponse` already
existed in the file and were dead code.** The mapper already handled all seven flags — including
`AppendToAccess` — and the DTO already had the `AccessRights` string property that
`RetrievePrincipalAccess` returns. Neither had a single caller.

They are the orphaned wiring of an earlier `RetrievePrincipalAccess` implementation that was replaced by
the direct-query probe. Confirmed repo-wide: **`RetrievePrincipalAccess` has zero live call sites** —
every remaining reference is a doc comment describing intent, in `AiAuthorizationFilter`,
`AnalysisAuthorizationFilter`, `VisualizationAuthorizationFilter`, `AiAuthorizationService`,
`CommunicationModule` and `SpaarkeCore`. [`.claude/constraints/auth.md`](../../../.claude/constraints/auth.md)
already carried the 2026-08-20 correction recording this.

So task 005 is mostly **reconnection**, not new code:

```
QueryUserPermissionsAsync
  ├─ TryRetrievePrincipalAccessAsync   ← NEW: the call that was always intended
  │     GET systemusers({systemuserid})/Microsoft.Dynamics.CRM.RetrievePrincipalAccess(Target=@p1)
  │         ?@p1={"@odata.id":"sprk_documents({recordid})"}
  │     → MapDataverseAccessRights  ← was dead, now live
  │     → full flag set
  └─ QueryReadAccessByProbeAsync    ← the ORIGINAL probe, retained as fallback (Read only)
```

### Why the probe survives as a fallback

The comment being deleted claimed `RetrievePrincipalAccess` "may not be available" with delegated (OBO)
tokens. That claim is **unverified** — the function has zero call sites, so nothing ever exercised it —
and it cannot be settled offline against a real tenant.

Rather than bet the fix on it being wrong, any `RetrievePrincipalAccess` failure degrades to the original
probe. This composition cannot regress:

| Case | Before | After |
|---|---|---|
| RPA works | — | full rights (the fix) |
| RPA fails, principal can read | Read | Read (identical) |
| RPA fails, principal cannot read | None | None (identical) |
| RPA answers "no rights" | (probe) | None — authoritative |

The worst case is exactly today's behaviour. Failures log the **`RPA-FALLBACK`** marker with the status
code and body, so a systematic outage is visible rather than silently re-capping everyone at Read.

`null` vs `AccessRights.None` carries that distinction in the code: `None` is an authoritative "Dataverse
says no rights", `null` is "no answer — fall back". Collapsing them would make a permissions outage
indistinguishable from a legitimate denial.

### The app-only path (POML step 3)

The constraint reads: *"On the app-only path, rights above Read MUST NOT be granted **by inference from
app visibility**."* Satisfied, and the qualifier matters — `RetrievePrincipalAccess` names the
**principal** in the URL, so the answer is that user's rights no matter which credential makes the call.
Nothing is inferred from what the application can see.

This actually **tightens** the app-only path. The old probe read the document *as the app*: if the app
could see it, the user was granted Read — even if the user could not see it. That is finding A-2's
app-scoped defect, and it is now gone on this path too. The tightening is theoretical in practice: after
task 004, `AuthorizationService` refuses to consult the data source without a caller token, and both
other consumers always pass one — so the app-only branch is currently unreachable from every consumer.

## 4. Escalation triggers — both evaluated, neither fired

The POML carries two. Both were checked with evidence rather than waved past.

### Trigger 1 — "extra Dataverse round trips beyond the existing per-request budget"

**Does not fire.** `RetrievePrincipalAccess` **replaces** the probe's `GET sprk_documents({id})`; it does
not add to it. Success path: 1 call, exactly as before. Only the fallback path costs 2, and only when RPA
is failing — a state the `RPA-FALLBACK` marker makes visible. The pre-existing
`QueryUserTeamMembershipsAsync` / `QueryUserRolesAsync` calls are untouched.

### Trigger 2 — "any existing consumer DEPENDS on the Read ceiling (would newly grant Write somewhere unaudited)"

**Does not fire.** The trigger asks for the list, so here it is — every consumer of `IAccessDataSource`
and every reader of `AccessSnapshot.AccessRights` in `src/`:

| Consumer | Reads | Effect of wider rights |
|---|---|---|
| `AiAuthorizationService:183` | `HasFlag(AccessRights.Read)` **only** | **None.** Read stays Read; a wider set cannot change a Read check |
| `OperationAccessRule:49,65` (via `AuthorizationService`) | `HasRequiredRights` | **Intended** — this IS FR-04 |
| `PermissionsEndpoints:256` (via task 006's `GetCallerAccessAsync`) | full flag set → capabilities | **Intended** — task 006's binding constraint |
| `CachedAccessDataSource:248` | serializes the flags | Pass-through only |
| `ExternalProjectDataEndpoints:282` | `rights.HasFlag(Create)` | **Not this rights source.** Reads `callerContext.GetEffectiveRights(id)` — the external-access grant-level model (`sprk_accesspermissiongrant`), unrelated to `AccessSnapshot` |
| `PlaybookSharingService:109,253` | `request.AccessRights`, `sharedTeam.AccessRights` | **Not this rights source.** POA sharing model |

No consumer newly grants anything unaudited. The only behavioural changes are the two the spec asked for,
plus the app-only tightening in §3 — which is a restriction, not a grant.

## 5. Design detail: unrecognised rights map to `None`, and never throw

Dataverse can add right names we do not model. An unknown name contributes nothing and does not throw —
on an authorization path a parse exception would convert "may this user do X?" into a 500, and a
`_ => throw` would make a future Dataverse release an outage. Pinned by
`FromAccessRightsString_WithUnrecognisedRight_IgnoresItWithoutThrowing`.

## 6. Test coverage — and an honest boundary

### The seam that had to be created, and why it is not scope creep

FR-04's acceptance criterion 5 reads: *"The snapshot never exceeds the caller's actual Dataverse rights
(**asserted by test with a mocked Dataverse answer**)."* That was not satisfiable as the code stood: the
mapping was a `private` method whose only entry point is an HTTP call. Mocking the transport is **ADR-038
ban B1**; reflecting into a private member is **ban B8**. The criterion *requires* a seam.

So the pure function moved to `internal static DataverseAccessRightsMapper`, exposed via
`InternalsVisibleTo("Sprk.Bff.Api.Tests")` — the convention already used across `Sprk.Bff.Api`
("internal, not private, so the test assembly can exercise it — no reflection"). Pure logic, no I/O, no
DI, no time ⇒ `tests/unit/domain/**`, a KEEP path.

§11 three questions: **Existing** — the private `MapDataverseAccessRights`; this IS that logic, moved.
**Extension** — yes, extraction rather than a new abstraction; the instance method remains as a
logging wrapper with one call site. **Cost of doing nothing** — criterion 5 stays unverified on the most
security-sensitive mapping in the change, and specifically the `AppendAccess`/`AppendToAccess`
transposition (adjacent names, one character apart) would ship silently.

### What each suite proves — and what it does not

| Suite | Proves | Does NOT prove |
|---|---|---|
| `DataverseAccessRightsMapperTests` (15, `tests/unit/domain/Dataverse/`) | A Dataverse answer maps to exactly the named rights — asserted over **all 128 subsets** of the seven rights, so any edit that ORs in an extra flag fails | That `RetrievePrincipalAccess` is called, or that its URL is correct |
| `PermissionsEndpointCallerScopedTests` (+3, KEEP path) | The full chain snapshot → policy → capability carries Write+ rights end to end; nothing downstream caps or amplifies them | Same — these use a data-source double |
| `OperationPolicyCharacterizationTests` (+2) | `entity.associate_document` is genuinely gated on `AppendTo` (task 003's obligation), and denied to a caller holding every OTHER right | — |

**The boundary, stated plainly: no test exercises the `RetrievePrincipalAccess` HTTP call itself.** Its
URL construction and the OBO-availability question can only be settled against a real Dataverse tenant.
Verifying it is **live-dev/UAT work**, and it belongs with the NFR-04 negative canary (task 034), which
already needs a live tenant. Recorded in §8 rather than implied away by a green test count.

### Non-vacuity, verified empirically

Transposing `"AppendToAccess" => AccessRights.Append` — precisely the failure task 003 predicted — makes
**4 of the 15** mapper tests fail. Reverted; 15/15.

### A mis-framed test from task 001, corrected

`Characterization_WritePlusOperation_DeniedUnderReadCeiling` was doc-commented *"CURRENT (BROKEN)
BEHAVIOR … FLIPPED BY: task 005"*. **Following that instruction would have been a security regression.**
The test hands `OperationAccessRule` a **Read-only snapshot** and asserts Write+ operations are denied —
which is permanently correct. Task 005 changed what the **data source produces**, not what the **rule
decides**; "flipping" it would have meant allowing upload to a read-only caller.

The A-20 ceiling was never observable at the rule layer at all. Renamed to
`WritePlusOperation_WithReadOnlyRights_DeniedForInsufficientRights`, re-documented as a permanent
negative, and the real coverage pointed at the endpoint suite. Also fixed: two "all rights" constants in
that file omitted `AppendTo` and would have mis-reported an AppendTo-gated operation as broken — the same
omission caught once already in task 003.

## 7. Obligations discharged

| From | Obligation | Discharged by |
|---|---|---|
| **task 003** | MUST map `AppendToAccess` → `AccessRights.AppendTo` (and `AppendAccess` → `Append`), plus a test asserting an AppendTo holder is ALLOWED `entity.associate_document` | Mapper handles both; `AssociateDocument_WithAppendToRights_IsAllowed` + the non-vacuous `_WithEveryRightExceptAppendTo_IsDenied` |
| **task 006** | Verify the eleven Write+ capabilities light up — "a fix that does not surface in the capabilities response means the snapshot widened somewhere the endpoint does not read" | `GetPermissions_ForCallerWithEveryRight_ReportsEveryCapabilityTrue` asserts all fourteen true; `_ForPartialRights_` asserts no amplification |

## 8. Follow-on obligations created

| # | Obligation | Owner |
|---|---|---|
| 1 | **Verify `RetrievePrincipalAccess` against a real tenant** — the URL form and whether it works under OBO. If it fails, the `RPA-FALLBACK` log marker will show it and the ceiling silently returns. Pairs naturally with the NFR-04 canary, which already needs a live tenant | **task 034** / Phase 4 live-dev acceptance |
| 2 | Confirm the BFF application user holds the privileges `RetrievePrincipalAccess` needs on the app-only path (read `systemuser` + the target). Belongs with the `prvActOnBehalfOfAnotherUser` grant, which no runbook records today | Phase 4 prerequisites |
| 3 | The evaluator spine replaces this rights derivation. It MUST keep deriving from Dataverse's per-principal answer — not from app visibility — and MUST keep `AppendTo` mapped | **task 032** |
| 4 | **The `Target` is hard-coded to `sprk_documents({id})`.** `IAccessDataSource` is document-scoped today and the probe it replaced hard-coded the same entity set, so this is not a regression — but Phase 1's evaluator answers `(recordId → rights)` for *any* entity, and will need the entity-set name threaded through the seam. Flagged now because it is invisible until the first non-document caller silently resolves against the wrong set | **task 032** |

## 9. Code-review notes (Step 9.5)

| Finding | Severity | Disposition |
|---|---|---|
| `Target` hard-coded to `sprk_documents` | Suggestion | Recorded as follow-on #4 above. Not a regression — the replaced probe had the identical limitation |
| Failure path logs the Dataverse response body (`ResponseBody={ResponseBody}`) | Suggestion (low) | **Unchanged deliberately** — the pre-existing probe logs the same field the same way, and Dataverse error bodies carry messages and record ids, not credentials. Diverging here would make the two paths inconsistent for no security gain |
| `MapDataverseAccessRights` is now a thin logging wrapper with one call site | Observation | Accepted. The mapper is deliberately pure (no `ILogger`) so it can live under `tests/unit/domain/**`; the wrapper keeps the log line at the layer that has the logger |
| `DataverseAccessDataSource.cs` grew ~561 → ~700 lines | Observation | No decomposition flagged. Per [`COMPONENT-COMPLEXITY.md`](../../../docs/standards/COMPONENT-COMPLEXITY.md) the instrument is cohesion, not LOC: the file retains one responsibility (query Dataverse for access data), and this change **removed** one (the pure mapping moved out). Direction is neutral-to-improving |

### Suite health

The first full-suite run after this change reported **1 failure**; the next three were clean
(10,656+1 → 10,657 → 10,657 → 10,657, and 6/6 clean on the five touched suites, 79 tests each). The
identity was not captured — `-v q` suppresses the failing test name. This matches the pre-existing,
previously-unreproducible flake recorded in `current-task.md`; **it is not attributed to this change, and
not exonerated either.**

One latent race WAS found and fixed while investigating: the batch-spoof assertion enumerated the
double's `Calls` list without holding its lock, which can throw "collection was modified" if the endpoint
appends concurrently. Assertions now go through a locked `Snapshot()`.

**Technique for next time:** run the full suite with `--logger "trx;LogFileName=full.trx"` and parse
`outcome="Failed"` out of the TRX — that captures the identity even when console verbosity does not.
