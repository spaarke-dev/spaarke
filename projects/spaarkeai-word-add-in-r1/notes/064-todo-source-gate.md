# Task 064 — F3: gating `POST /office/todo`'s four source ids, and closing its existence oracle

**Finding**: `notes/fable-review-2026-09-21.md` §2, **F3 (MED-LOW)** · **Date**: 2026-09-21 ·
**Outcome**: closed by a new endpoint filter (`TodoSourceAccessFilter`) shaped like
`QuickCreateSourceAccessFilter`, with a **single constant deny body** that makes "denied" and "not found"
indistinguishable.

---

## 1. Placement Justification (CLAUDE.md §10 / `.claude/constraints/bff-extensions.md`)

All changes are in the BFF and belong there — this is a per-resource authorization decision on a BFF route,
which ADR-008 places in an endpoint filter next to the endpoint. **No new service, no new interface, no new
DI registration, no new package, no new endpoint, no new background work.** The filter is constructed
per-request from services already in the container (the same shape `AddQuickCreateSourceAccessFilter` and
`AddEntityAccessFilter` use), so nothing was registered.

The three-question test (CLAUDE.md §11), for the one new component:

1. **Existing** — `Api/Filters/QuickCreateSourceAccessFilter.cs` solves exactly this problem (caller-supplied
   source id, read-gated via `CallerRecordAccessProbe`) for the quick-create route. `EntityAccessFilter`
   solves the sibling problem for `/office/save`. Verified by `grep -rn "CallerRecordAccessProbe" src/server`
   — three consumers, no fourth.
2. **Extension** — **evaluated by reading it, and rejected; mirrored instead.** Three concrete reasons, in
   order of weight:
   - `QuickCreateSourceAccessFilter.InvokeAsync` reads `context.Arguments.OfType<QuickCreateRequest>()` and
     authorizes **one** id whose **type the caller names as a string**. `/todo` carries **four** ids across
     three carrier shapes, and two of those types (`sprk_document`, `sprk_communication`) are fixed by the
     request schema rather than named by the caller. Generalizing over both request shapes means a type
     switch whose only shared line is the probe call — which is *already* shared.
   - Quick-create emits **three different reason codes** (`entity_type_not_authorizable`,
     `access_check_failed`, `insufficient_rights`). That is fine there, because quick-create has no
     existence oracle to close. Here a reason code that varies with the record is the oracle rebuilt (§4),
     so this filter must emit exactly one. Merging the two would have forced quick-create's shape onto a
     route whose contract forbids it, or a per-route flag — a worse outcome than two small filters.
   - What is genuinely shared IS shared, and was not re-declared: the same `CallerRecordAccessProbe`, the
     same `read` key in `OperationAccessPolicy`, the same `OFFICE_009` code the pane's error map keys on,
     the same logical-name → entity-set table via `EntityAccessFilter.TryResolveEntitySet`, and — the
     important one — the same `OfficeService.TodoRegardingMap` that decides which ids get **written** (§3).
3. **Cost of doing nothing** — concrete, not abstract: any authenticated caller (a) confirms the existence
   of an arbitrary invoice / document / communication GUID from the 403-vs-201 split, (b) harvests that
   record's parent matter/project id by reading their own new `sprk_todo` back through `Xrm.WebApi`, and
   (c) attaches a To Do they authored to a record they cannot read, which FR-26 inheritance then surfaces
   to that record's members. (a) and (c) are demonstrated in §2 and demonstrated closed in §5.

**No AI-internal type crosses into CRUD code**; nothing bypasses `Services/Ai/PublicContracts/`.

**Hot path**: BFF **Y** · SpaarkeAi N · ci-workflows N · skill-directives N · root-CLAUDE N.

**No §6.5 ADR conflict arose.** ADR-008's default form (an endpoint filter) is exactly the right shape here,
unlike task 062's in-query trim; no ADR rule pushed this design toward a worse security outcome.

---

## 2. REPRODUCE-FIRST (acceptance criterion 1) — **both halves**, verbatim

No deployed environment is in this worktree's loop, so — as in task 062 — the reproduction is a test that
**fails against the code as it stood**. The doubles are module boundaries only: `CallerRecordAccessProbe`
(its `virtual` method is the ADR-038 §4 seam; the double *models Dataverse* — deny by default, and "no such
record" reported identically to "you may not see it") and `IDataverseService` (the app-only write + the
core-ancestor read). The real route, the real filter chain, the real `OfficeService` and the real
`CoreAncestorResolver` are all shipped code.

Run of `tests/integration/contract/Api/Office/OfficeTodoSourceAuthorizationContractTests.cs` against
`OfficeEndpoints.cs` / `OfficeService.cs` **before any change**:

```
Failed!  - Failed:     6, Passed:     2, Skipped:     0, Total:     8, Duration: 35 s - Sprk.Bff.Api.Tests.dll (net10.0)
```

### (a) A To Do created against a document the caller cannot read

```
Sprk.Bff.Api.Tests.Api.Office.OfficeTodoSourceAuthorizationContractTests.Post_Todo_WhenCallerCannotReadTheDocumentCarrier_IsRefused_AndCreatesNoRow [FAIL]
  Error Message:
   Expected response.StatusCode to be HttpStatusCode.Forbidden {value: 403} because a To Do must not be written against a document the caller cannot read, but found HttpStatusCode.Created {value: 201}.
```

**201 Created**, and `factory.CreatedEntities` held the row: a `sprk_todo` owned by the caller carrying
`sprk_regardingdocument` → a `sprk_document` the caller holds no rights on whatsoever. The same shape
reproduced for `sprk_communication`, the regarding record, and the assignee contact — four of four.

### (b) The 403-vs-201 split confirms existence

Two requests differing in **exactly one respect** — whether the invoice row exists:

```
Sprk.Bff.Api.Tests.Api.Office.OfficeTodoSourceAuthorizationContractTests.Post_Todo_DeniedRecordAndNonExistentRecord_ProduceIndistinguishableResponses [FAIL]
  Error Message:
   Expected deniedResponse.StatusCode to be HttpStatusCode.Forbidden {value: 403} because a caller must not learn from the status code whether the record exists, but found HttpStatusCode.Created {value: 201}.
```

Read the assertion the way FluentAssertions rendered it: the **expected** value is
`notFoundResponse.StatusCode`, which was **403**; the **actual** is `deniedResponse.StatusCode`, which was
**201**. Same caller, same rights (none), same body except the GUID — *the row that existed returned 201 and
the row that did not returned 403.* That is the oracle, stated as a test.

Why Invoice and not Matter: `CoreAncestorResolver` treats `sprk_matter` / `sprk_project` as **CORE** and
stamps them with **no read at all**, so those two never leaked. `sprk_invoice`, `sprk_document` and
`sprk_communication` are **CHILD**-class and are read app-only — which is precisely the set the finding
names.

**2 of 8 passed pre-fix, not 0 — stated rather than glossed**, because "all 8 failed" would have been the
wrong answer and worth suspecting. The two are
`Post_Todo_WhenEverySourceIsReadable_StillCreatesTheTodo_WithResolverFieldsStamped` and
`Post_Todo_WithNoSourceIdsAtAll_StillCreates_AndProbesNothing` — the happy paths, which an absent gate
correctly does not affect.

---

## 3. What changed

### `Api/Filters/TodoSourceAccessFilter.cs` (new, ~250 lines incl. remarks)

An `IEndpointFilter` + its `AddTodoSourceAccessFilter()` extension, mirroring
`QuickCreateSourceAccessFilter`'s shape line for line: resolve the request from `context.Arguments`, resolve
each named record's entity set, ask `CallerRecordAccessProbe.GetCallerRightsAsync` (OBO
`RetrievePrincipalAccess`), and require `OperationAccessPolicy.HasRequiredRights(rights, "read")`.

**All four ids** (criterion 2): `RegardingRecordId`, `DocumentId`, `CommunicationId`,
`AssignedToContactId`.

**The drift forcing function.** `CollectSourceRecords` reads the regarding types out of
`OfficeService.TodoRegardingMap` — *the same table that decides which lookups get written*. It is not a copy
and not a parallel list: adding a row there automatically gates the new type, and a row that disappears
un-gates a write that also stops happening. Two tables would have drifted, and the drift's failure direction
is an ungated write. (This is why `_todoRegardingMap` became `internal static TodoRegardingMap` — the only
change to `OfficeService.cs` besides the three call sites, all inside `CreateTodoAsync`, well clear of the
stub generators `unified-access-control-r2` holds.)

**Entity-set resolution asks the shared table first.** `EntityAccessFilter.TryResolveEntitySet` covers
`sprk_matter` / `sprk_project` / `sprk_invoice` / `contact` — four of the six types this route can name. It
does **not** cover `sprk_document` or `sprk_communication`, and **they were deliberately not added to it**:
that table does double duty as `EntityAccessFilter`'s ALLOW-LIST of legal `/office/save` association targets
(its own remarks refuse `sprk_todo` on exactly that ground), so adding two entries to serve this route would
silently widen which types a document may be filed against on a *different* route — and on one contested
with `unified-access-control-r2`. A two-entry `CarrierEntitySets` supplement, consulted only after the shared
table misses, is the smaller cost. **This is the one place a §11 "don't add a fourth map" instinct points
the wrong way**, and the reasoning is recorded in the code, not only here.

**A miss denies.** A type whose per-record access this codebase cannot evaluate is a type whose id it must
not write onto a caller-owned row — the `RecordRouteAccessAuthorizationFilter` posture.

**Fail open only where nothing is read.** No body, no ids, an empty GUID, or an unrecognized
`RegardingEntityType` → pass. In every one of those cases `OfficeService.CreateTodoAsync` writes nothing and
reads nothing, so there is nothing to authorize; refusing would turn a silently-ignored field into a 403.
This mirrors `QuickCreateSourceAccessFilter`'s stated posture rather than inventing a stricter one.

**Cancellation is not a denial.** `OperationCanceledException` under `RequestAborted` rethrows; only the
authorization decision is wrapped in `try`, never `next()`, so downstream faults are not relabelled.

### `Api/Office/OfficeEndpoints.cs`

`.AddTodoSourceAccessFilter()` appended after `.AddOfficeAuthFilter()` on `POST /office/todo` (so it runs
innermost, immediately before the handler — the same position quick-create puts its filter in), plus the
route comment explaining what is gated and why.

### `Services/Office/OfficeService.cs`

`_todoRegardingMap` → `internal static readonly … TodoRegardingMap` + doc paragraph stating why it is no
longer private. **No behavioural change**, and nothing touched outside `CreateTodoAsync`'s own region.

### `tests/integration/contract/Api/Office/OfficeTodoSourceAuthorizationContractTests.cs` (new)

8 tests. See §5.

### Not changed, deliberately

- **`tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.cs`** — contested with another project and owned
  by deferred task 061. See §8.
- **`src/client/office-addins/**`** — no change is needed (§6), and a sibling agent holds edits there.
- **`EntityAccessFilter.EntitySetByType`** — see above; widening it would widen `/office/save`.
- **The handler's existing `OFFICE_010` 403** (service returned null, e.g. a core-ancestor stamp failure).
  Left as-is: after the gate it is reachable only for a record the caller has already proved they can read,
  so it discloses nothing they did not already know. Unifying it with the filter's body would have coupled
  an infrastructure failure to an authorization refusal for no security gain.

---

## 4. The response, and why it is indistinguishable (criterion 3)

**Chosen response: HTTP 403 `Forbidden`, ProblemDetails, `errorCode: OFFICE_009`,
`reasonCode: source_record_inaccessible`, one constant `detail` sentence — identical for every refusal.**

**One-line justification**: one answer has to cover both cases, and 403 is the one that asserts nothing
about existence — a 404 on a POST would additionally be ambiguous with "no such route", and the pane already
treats this route's 403 as an ordinary create failure, so no client change is needed.

Three mechanisms make the two cases identical rather than merely similar:

| | Mechanism |
|---|---|
| **Status** | Every refusal path in the filter returns the same 403 |
| **Body** | `Deny()` **takes no reason and no record**. There is one detail string, one reason code, one error code. Every caller-visible field except `correlationId` is a compile-time constant, so a future edit cannot reintroduce a per-record branch without deleting a parameterless method first. The record id and type go to the **log**, never the response |
| **Underlying answer** | `CallerRecordAccessProbe` already collapses "no such record", "you may not see it", "the probe threw" and "no rights" to `AccessRights.None` — Dataverse itself reports the first two identically under OBO, by design (the probe's own remarks say so). The filter inherits that conflation rather than re-deriving it |

**Timing class**: both halves of the pair make `RetrievePrincipalAccess` answer 404, which the probe retries
on its replication-lag schedule (400 ms + 1200 ms) before denying. So a non-existent record and an invisible
one share a timing class as well as a body. What *is* distinguishable by timing is a record the caller can
partially see but lacks Read on (a fast 403) versus one that is absent-or-invisible (a slow one) — that
discloses no existence, because absent and invisible remain on the same side of the line.

**Residual side channel, stated**: probes are sequential and fail fast, so a caller can time *which of their
own four ids* was refused. They supplied all four; this discloses nothing they did not already know.

**The filter-order question a reviewer will ask, checked rather than assumed.** `AddIdempotencyFilter()` is
registered *before* `AddOfficeAuthFilter()` and therefore runs OUTSIDE the gate — so a cached replay returns
its stored response without re-authorizing. That would be a bypass if the cache were shared across callers.
It is not: `IdempotencyFilter` scopes every key to the caller, both for a client-supplied key
(`:188` → `$"{userId}:{clientProvidedKey}"`) and for a generated one (`:391` → a hash over
`$"{userId}:{path}:{canonicalBody}"`). A caller can therefore only replay *their own* previously-authorized
create. Ordering left unchanged — it is identical to the quick-create route's shipped arrangement.

**What the test asserts**: same status, and the full response body byte-for-byte equal after normalizing
`correlationId` (which varies per request by design and is not record-derived).

---

## 5. The tests, and the seed in both directions (criteria 5, 8)

`tests/integration/contract/Api/Office/OfficeTodoSourceAuthorizationContractTests.cs` — **8 tests, all
passing.** No `Mock<HttpMessageHandler>`, no DI-registration assertion, no ctor null-check (ADR-038 bans
B1/B16/B17). KEEP path: `tests/integration/contract/**`.

| Test | Proves |
|---|---|
| `…WhenCallerCannotReadTheDocumentCarrier_IsRefused_AndCreatesNoRow` | finding F3's headline case — refused, **and no row written** |
| `…WhenCallerCannotReadTheCommunicationCarrier_IsRefused_AndCreatesNoRow` | the Outlook counterpart — gating one carrier and not the other would be the gap |
| `…WhenCallerCannotReadTheRegardingRecord_IsRefused_AndCreatesNoRow` | the id whose parent core-record would otherwise be stamped onto a caller-owned row |
| `…WhenCallerCannotReadTheAssigneeContact_IsRefused_AndCreatesNoRow` | the fourth id — the easiest to forget, because it is an assignment rather than a regarding |
| `…DeniedRecordAndNonExistentRecord_ProduceIndistinguishableResponses` | **THE ORACLE.** Same status, same body (correlationId normalized), no row either way |
| `…WhenEverySourceIsReadable_StillCreatesTheTodo_WithResolverFieldsStamped` | happy path: 201, both lookups, `sprk_assignedto`, and the ADR-024 resolver fields still written |
| `…WithNoSourceIdsAtAll_StillCreates_AndProbesNothing` | fail-open-where-nothing-is-read: a standalone To Do still creates, **and no access question is asked** |
| `…ProbesAllFourCallerSuppliedIds_NotASubset` | all four entity sets probed in ONE request — "three of four is a gap", asserted directly |

**Test-scope justification (criterion 8)**: the eight cover exactly the four gated ids, the
indistinguishability, the unchanged happy path and the seed directions. The two beyond that literal list are
`…WithNoSourceIdsAtAll…` and `…ProbesAllFourCallerSuppliedIds…`: the first pins the one behaviour a gate is
most likely to break by over-reaching (refusing a request that names nothing), the second is the only test
that fails if a future edit gates three ids instead of four — the exact regression this task exists to
prevent.

### Seeded RED (criterion 5) — the filter disabled

`.AddTodoSourceAccessFilter()` commented out on the route, everything else intact:

```
Failed!  - Failed:     6, Passed:     2, Skipped:     0, Total:     8, Duration: 36 s - Sprk.Bff.Api.Tests.dll (net10.0)
```

```
Post_Todo_WhenCallerCannotReadTheDocumentCarrier_IsRefused_AndCreatesNoRow [FAIL]
Post_Todo_DeniedRecordAndNonExistentRecord_ProduceIndistinguishableResponses [FAIL]
Post_Todo_ProbesAllFourCallerSuppliedIds_NotASubset [FAIL]
Post_Todo_WhenCallerCannotReadTheCommunicationCarrier_IsRefused_AndCreatesNoRow [FAIL]
Post_Todo_WhenCallerCannotReadTheRegardingRecord_IsRefused_AndCreatesNoRow [FAIL]
Post_Todo_WhenCallerCannotReadTheAssigneeContact_IsRefused_AndCreatesNoRow [FAIL]
```

Identical failure set to the pre-change run in §2 — the same six, the same two controls surviving. That
agreement is the point: it shows the six are failing because the **gate** is absent, not because some other
part of the change is missing.

### Restored GREEN

```
Passed!  - Failed:     0, Passed:     8, Skipped:     0, Total:     8, Duration: 16 s - Sprk.Bff.Api.Tests.dll (net10.0)
```

`grep -c SEED src/server/api/Sprk.Bff.Api/Api/Office/OfficeEndpoints.cs` → `0`.

---

## 6. Pane compatibility — checked BEFORE the response shape was decided (criterion 4)

**The escalation trigger did NOT fire, and this is why.** `handleCreateTodo`
(`src/client/office-addins/shared/taskpane/App.tsx:403-405`) is the route's only consumer:

```ts
if (!res.ok) {
  return { ok: false, error: `Create failed (${res.status}).` };
}
```

It **never reads the response body**, never consults `ERROR_CODE_MAP`, and does not branch on the status —
403, 404 and 500 are one path. So the pane cannot today distinguish "denied" from "not found", which means
no shipped client behaviour depends on the split this task closes. The trigger's premise ("if making them
indistinguishable would break a shipped client behaviour") is false, checked rather than assumed.

| Check | Result |
|---|---|
| Client change required | **None.** 403 is unchanged as the refusal status; `.ProducesProblem(403)` was already declared on the route |
| `OFFICE_009` resolvable if the pane ever *does* map the body | ✅ `errorMessages.ts:147` — "Access Denied" / "You do not have permission to perform this action." Generic, names no record — consistent with the indistinguishability requirement |
| Happy path, both hosts | ✅ Word's shape (Matter + `documentId` + `assignedToContactId`) and Outlook's shape (Matter + `communicationId`) both covered by tests; the pane's ids come from surfaces that already required access — `documentId` from task 013's resolver, `communicationId` from filing the email, `assignedToContactId` from `/office/search/entities`, which task 062 made caller-trimmed |
| Added cost on the happy path | 2–3 OBO probes per create (vs quick-create's 1) on a low-frequency inline action. Not measured live — see §8 |

**Not verified**: the pane has **not** been exercised against a live Dataverse from this worktree — no
deployed environment is in the loop. The Create To Do tab's server contract is proven by test in both host
shapes; the end-to-end click is not. Called out again in §8.

---

## 7. Verification (real output, not assertion)

| Check | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | `Build succeeded. 0 Warning(s) 0 Error(s)` |
| New authorization contract tests | `Passed! - Failed: 0, Passed: 8` |
| Same tests, gate disabled (control) | `Failed! - Failed: 6, Passed: 2` |
| Reproduce-first, pre-change (control) | `Failed! - Failed: 6, Passed: 2` |
| Pre-existing `OfficeTodoRegardingContractTests` (task 035's 6) | `Passed!` — unaffected; the shared `OfficeTestWebAppFactory` already substitutes a permissive `CallerRecordAccessProbe` (`OfficeEndpointsContractTests.cs:810-811`), so the new filter passes there exactly as `EntityAccessFilter` already did on the save route |
| Full BFF suite | `Passed! - Failed: 0, Passed: 12403, Skipped: 56, Total: 12459, Duration: 9 m 17 s` |
| **Test-count reconciliation** | post-062 baseline **12,387 / 0 / 56** → **12,403 / 0 / 56**. **Δ = +16 = this task's 8 + task-063's 8** (`SendToIndexAuthorizationContractTests.cs`, `grep -c "\[Fact\]"` = 8). We share this worktree and both changesets are uncommitted, so **neither task can reconcile to "baseline + its own delta" alone** — +16 is the joint figure and is stated as such. No test was deleted, skipped or silently repaired |
| `tests/Spaarke.ArchTests` | `Passed! - Failed: 0, Passed: 191, Skipped: 0, Total: 191` |
| `dotnet list package --vulnerable --include-transitive` | `The given project 'Sprk.Bff.Api' has no vulnerable packages given the current sources.` — no new HIGH/CRITICAL. **No NuGet package was added or upgraded by this task** |

### Publish size — fresh build of `origin/master`, not the recorded number (CLAUDE.md §10 bullet 4)

| Field | Value |
|---|---|
| Command | `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o <out>` |
| RID / mode | framework-dependent **linux-x64** (from the csproj), not self-contained |
| Configuration | **Release** |
| Compression | PowerShell **`Compress-Archive -CompressionLevel Optimal`** over the publish folder's contents (the method `scripts/Deploy-BffApi.ps1` uses) |
| PDBs | **included** (4 `.pdb` in both publishes) |
| `origin/master` @ `99cdfe2ea`, freshly built + zipped today in a throwaway worktree | **45.46 MB** |
| This branch | **45.54 MB** |
| **Delta** | **+0.08 MB** |

Well under the **+5 MB** single-task escalation threshold and the **60 MB** ceiling. Honesty notes:
(a) the recorded 2026-09-02 baseline of 45.42 MB @ `a826cf347` was **not** used for the diff — master was
rebuilt from scratch at its current SHA, exactly as the rule requires;
(b) the branch figure is the **whole branch** vs master, including this project's earlier tasks and two
sibling agents' uncommitted BFF edits present in this worktree, so **+0.08 MB is an upper bound on this
task's own contribution, not an under-count**. This task adds one ~250-line source file and no package; its
own contribution is a few KB of IL. It is the same figure task 062 measured against the same master SHA
today, which is consistent with both tasks contributing ~nothing.

### 7.1 How that number was obtained — four runs, and why only the fourth counts

This worktree was shared with two other agents throughout, which made the suite result unreliable in two
distinct ways. Recorded because the *procedure* is the evidence, and because a green `--no-build` run is
not self-certifying.

| Run | Result | What it actually measured |
|---|---|---|
| 1 | `0 failed, 12403 passed` | Started while task-063's `// SEED-A` was live in `Api/Ai/RagEndpoints.cs` (an `if (false)` yielding `error CS0162`). The number reconciled, but the run straddled someone else's seed window |
| 2 | **`5 failed`, 12398 passed** | Started after SEED-A cleared — straight into task-063's **SEED-B** window (their per-document write check disabled). Their control for that seed is `Failed: 5, Passed: 3` in `SendToIndexAuthorizationContractTests` |
| 3 | **no result at all** | Collided with task-063's concurrent run over `tests/unit/Sprk.Bff.Api.Tests/bin` — `error MSB3027: … the file is locked by "testhost"`. The build never produced a binary |
| **4 (the one quoted above)** | `0 failed, 12403 passed, 9 m 17 s` | **Serialized** (task-063 held off), preceded by `touch` on all four changed files and an explicit `dotnet build … 0 Warning(s) 0 Error(s)`, then `dotnet test --no-build` |

Two traps this dodged, both surfaced by task-063 having been bitten by them first:

1. **`Copy-Item` / `cp` restore preserves the backup's timestamp**, which can be *older* than the build
   output — MSBuild's up-to-date check then silently skips the rebuild, prints `Build succeeded`, and the
   run executes **seeded binaries against restored source**, with `grep SEED` returning 0 the whole time.
   This task restored its own seed with `cp`, so it was exposed; run 4's `touch` closes it.
2. **`dotnet test --no-build` runs the previous assembly if the build failed** — build and test are separate
   commands and the first's failure does not stop the second, so a green `--no-build` proves nothing on its
   own. Run 4's build output is quoted above precisely so this one is checkable rather than assumed.

**Stated honestly: run 2's five failing test NAMES were never captured** (run 3 died before it could list
them). task-063's account — that they are its SEED-B control — is consistent with every observation
available (the count matches their recorded control exactly, their own suite was green immediately before
and after, and run 4 is green on an unchanged tree). It is corroborated, **not verified**, and it is
recorded as their account rather than as this task's finding.

---

## 8. Recorded honestly — what is NOT met, and what is left open

1. **🔴 Acceptance criterion 7 ("Task 061's guard no longer flags this route") is NOT met, and cannot be met
   by this task.** The POML gates 064 on 061; 061 is **deliberately deferred** by the orchestrator because
   `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.cs` is contested with another project, and this
   task was instructed not to touch it. As it stands that guard governs **no Office route at all**
   (`grep Office` over the file returns nothing), so it does not flag this route today and there is nothing
   to un-flag. When 061 lands and builds the census it must classify `POST /office/todo` as
   **route-level gated by `TodoSourceAccessFilter`** — the same classification the quick-create route gets
   from `QuickCreateSourceAccessFilter`. **A real open item, not a formality**, and identical in shape to
   task 062's item 1.
2. **🔴 No live verification.** The gate has not been exercised against a real Dataverse from this worktree.
   Specifically unproven end-to-end: that `RetrievePrincipalAccess` answers usefully for `sprk_documents`
   and `sprk_communications` under an OBO token for an ordinary user. If it does not, the failure direction
   is **fail-closed** (`AccessRights.None` → 403), which is correct but would be a visible Create-To-Do
   outage rather than a silent one. Two mitigations worth stating: the same probe + the same function
   already serve `/office/save` and six external-access mutations in production, and the entity set is the
   only thing that differs per call. **Someone should exercise the Create To Do tab in both hosts against
   dev before this ships.**
3. **Latency**: the happy path now costs 2–3 sequential OBO `RetrievePrincipalAccess` round trips instead of
   0. At the codebase's own recorded 20–50 ms/call that is ~60–150 ms on a low-frequency inline create that
   already performs a Dataverse write; no p95 budget is written for this route. **Not measured live.** If it
   ever matters, the four probes are independent and parallelize trivially — but doing so would blur the
   fail-fast ordering for no present benefit.
4. **`CarrierEntitySets` is a supplement to a shared table, which is a §11 cost paid deliberately.** If
   `EntityAccessFilter.EntitySetByType` is ever split into its two present jobs (entity-set naming vs
   `/office/save`'s association allow-list), these two entries should move into the naming half and the
   supplement should be deleted. Recorded here because that refactor is the right home for it and does not
   belong inside a security fix.
5. **Field-level security is not applied by a record-level Read check.** An app-only core-ancestor read
   applies no column masking, so the stamp still copies a parent id the caller holds only record-level Read
   on the child for. That is the intended FR-26 semantics, not a gap — noted because `QuickCreateSourceAccessFilter`
   records the same residual and a reader comparing the two should find it in both.
6. **F2 and F4 are unaffected** and have their own tasks. F1 (closed by task 062) was the bulk GUID source
   that made F3 cheap to exploit; closing F3 does not close them.
7. **This task's TASK-INDEX row is committed inside task-063's commit `2a9cc7a43`**, not in this task's own
   commit. That is a consequence of §10 below, not a mistake in either task's work, and it is recorded here
   so a later reader following commit SHAs is not confused by it.

---

## 10. Shared-worktree git hazards — two, stacked, and they cost real time

Three agents committed to this branch in one worktree during this task. **A git worktree shares one
`.git/index` as well as one working tree**, so "stage, then commit" is *not atomic between agents*. What
happened, recorded because the next agent here will otherwise rediscover it:

1. This task staged its six files plus a hand-built `TASK-INDEX.md` blob (HEAD + only the 064 row, via
   `git hash-object -w` + `git update-index --cacheinfo`, specifically to avoid sweeping a sibling's row).
   **Care in building the blob bought nothing**: task-063 called `commit` first, and its commit took the
   whole index — both changesets, under its message alone.
2. task-063 `reset --soft HEAD~1` to undo that. In the gap, this task ran `git commit --allow-empty` with
   **no pathspec** — which was not empty, because task-063 had re-staged by then, and so committed *its*
   nine files under *this* task's message. The inverse of the first failure, two minutes later.
3. Resolved without rewriting history below the tip: task-063 amended the tip's message back to its own
   (`2a9cc7a43`), and this task committed its six files with a **pathspec** commit.

**The rule, stated so it can be followed rather than inferred**: in a shared worktree use
`git commit -m … -- <paths>` and never bare `git add` + `git commit`. Pathspec mode commits the **working
tree** content of exactly the named paths and ignores the index entirely, so a sibling's staged work cannot
ride along. Its one sharp edge is a file BOTH agents edit (here `TASK-INDEX.md`): pathspec mode would commit
the worktree copy carrying both rows, so that file has to be coordinated, not automated.

**The second hazard, found by task-063 and worth more than the first**: `.husky/pre-commit` runs
`npx lint-staged`, which **stashes and restores unstaged changes** around the formatters. That is an
independent route for files that are not in the index to end up inside a commit, and it explains what the
index race alone does not. Two hazards stacked, not one.

**A third, from the same session, about believing a green test run**: `Copy-Item`/`cp` restore preserves the
backup's timestamp, which can be older than the build output — MSBuild then skips the rebuild, prints
`Build succeeded`, and `dotnet test --no-build` runs the *previous* assembly and reports green, while `grep`
says the source is clean. Build and test are separate commands and the first's failure does not stop the
second. `touch` the restored files and check the build's own output before believing a suite result. See
§7.1 run 4.

---

## 9. Step 9.5 quality gates

| Gate | Result |
|---|---|
| `adr-check` | **0 violations.** **ADR-008** — resource authorization is an endpoint filter on the route, added after `RequireAuthorization()`/`AddOfficeAuthFilter()`, no global middleware, one concern per filter ✓ · ADR-001 (Minimal API; no new endpoint) ✓ · ADR-003 (fail closed, deny by default; no new auth service layer — the existing probe + `OperationAccessPolicy` decide) ✓ · ADR-004/ADR-028 (no new credential; the probe's OBO exchange is unchanged, no `.WithClientSecret`) ✓ · ADR-010 (no new interface, no new DI registration; the filter is constructed per-request from existing services, as both sibling filters are) ✓ · ADR-013 (no AI-internal type in CRUD code) ✓ · ADR-019 (`Results.Problem` with `errorCode` + `correlationId`, matching the route's existing error shape) ✓ · **ADR-024** — resolver-field semantics untouched; this changes *who may create*, not *what is written*, asserted by the happy-path test ✓ · ADR-029 (publish size measured against a fresh master, +0.08 MB) ✓ · ADR-032 (no feature-gated registration introduced; the filter depends only on unconditionally-registered services) ✓ · **ADR-038** (new tests at the `tests/integration/contract/**` KEEP path; doubles are module boundaries only; no banned test shapes) ✓ · ADR-044 (ids typed `Guid` throughout; no GUID interpolated into an OData predicate) ✓ |
| `code-review` | **0 critical.** No secrets · no new input-validation surface (the filter reads ids the model already binds and validates) · no N+1 (bounded at ≤4 probes, one per caller-supplied id) · no sync-over-async · **no swallowed exception that hides a failure** — the probe's throw is logged at Error and denied, and only the authorization decision is inside the `try`, never `next()` · cancellation rethrown rather than reported as a denial · no code-restating comments. **1 observation accepted**: `TodoSourceAccessFilter.cs` is ~250 lines of which roughly half is the remarks block. Per CLAUDE.md §11.5 / `COMPONENT-COMPLEXITY.md` this is evaluated on cohesion, not LOC: one responsibility (authorize this route's source records), one reason to change, three ctor deps → two. The remarks carry the two decisions a later reader cannot re-derive from the code — why this is a sibling of `QuickCreateSourceAccessFilter` rather than an extension of it, and why `Deny()` takes no parameters. No decomposition warranted |
| Lint / build | `Build succeeded. 0 Warning(s) 0 Error(s)` |
