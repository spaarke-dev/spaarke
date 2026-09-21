# Task 063 — F2: tenant binding + per-document write authorization on `POST /api/ai/rag/send-to-index`

**Finding**: `notes/fable-review-2026-09-21.md` §2, **F2** · **Date**: 2026-09-21 · **Outcome**: closed by a
**filter-level tenant binding + a handler-level per-document `Write` check**, with a defined partial-permission
contract.

---

## 1. Placement Justification (CLAUDE.md §10 / `.claude/constraints/bff-extensions.md`)

All changes are in the BFF and belong there. **No new endpoint, no new service, no new interface, no new DI
registration, no new package, no new background work.** The three-question test (CLAUDE.md §11):

1. **Existing** — both mechanisms already exist and were already reachable from this route.
   `TenantAuthorizationFilter` is already attached to `/send-to-index` and already binds tenant for four
   other request types in the same `switch`. `AuthorizationService.GetCallerRecordAccessAsync` is the entity-generic,
   caller-evaluated (OBO) access evaluator already consumed by `RecordSearchEndpoints:284`,
   `SemanticSearchEndpoints:569`, `SemanticSearchAuthorizationFilter:497` and
   `ContainerDocumentAuthorizationFilter:299`; it is registered unconditionally
   (`SpaarkeCore.cs:26`). `TenantResolution.ResolveTenantId` is the BFF's single answer to "which tenant is
   this caller in?".
2. **Extension** — yes, taken on both. One new `case` in the filter's existing match list; one call to the
   existing evaluator in the existing handler. The only new file is the test file, new only because no
   authorization test covered this route.
3. **Cost of doing nothing** — demonstrated in §2 and demonstrated closed in §5: a caller wrote index chunks
   into a tenant partition of their choosing, and stamped `sprk_searchindex*` onto a Dataverse row they held
   no Write on.

**No AI-internal type crosses into CRUD code.** This is AI-surface code consuming `Spaarke.Core`'s
authorization evaluator; nothing bypasses `Services/Ai/PublicContracts/`.

**No `.claude/` file, no ADR and no shared fixture was modified.** No §6.5 ADR conflict arose — see §9.

---

## 2. REPRODUCE-FIRST (acceptance criterion 1) — both halves were real

No deployed environment is in this worktree's loop, so per the task's own fallback these are tests that
**fail against the code as it stood**. The probe is the full route booted through `WebApplicationFactory`:
the real `Program`, the real filter chain, the real handler. Run against the unmodified source:

```
Failed!  - Failed:     6, Passed:     2, Skipped:     0, Total:     8, Duration: 2 s - Sprk.Bff.Api.Tests.dll (net10.0)
```

### (a) A caller indexed under a tenant partition that is not their `tid`

```
Failed Sprk.Bff.Api.Tests.Api.Ai.SendToIndexAuthorizationContractTests.TenantIdThatIsNotTheCallersTid_IsRejected_AndNothingIsIndexedOrStamped
  Error Message:
   Expected _fixture.CapturedIndexRequests.Select(r => r.TenantId) to be empty because nothing may be written to ANY
partition on the refusing path, and above all not to a partition the caller named in the body, but found
{"victim-tenant-partition-not-mine"}.
```

The caller's token carried `tid = 63333333-aaaa-5555-bbbb-7777cccc7777`. The string
`victim-tenant-partition-not-mine` is what the body said, and it is what reached
`FileIndexRequest.TenantId` — i.e. the AI Search partition key. **HTTP 200.**

### (b) A caller stamped a document they cannot write

```
Failed Sprk.Bff.Api.Tests.Api.Ai.SendToIndexAuthorizationContractTests.CallerWithReadButNotWrite_IsRefused_AndTheRowIsNotStamped
  Error Message:
   Moq.MockException : the sprk_searchindex* stamp is the write this task exists to authorize
Expected invocation on the mock should never have been performed, but was 1 times: d =>
d.UpdateDocumentAsync(It.IsAny<string>(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>())
```

The caller held `AccessRights.Read` and nothing else on that document. The row was read app-only, indexed,
and **written** app-only. **HTTP 200.**

### What a caller needed, corrected

The finding says the gate was "a valid token plus a document GUID whose SPE container they can read". That
is right, and worth stating precisely: the SPE download at `FileIndexingService.cs:69` is the ONLY OBO step
on the route. Everything else — the Dataverse row read (`:638`), the row write (`:748`) — ran as the
application. So the effective gate was container-level, and Dataverse row-level rights were never consulted
at all.

---

## 3. Mechanism, and why this one (acceptance criteria 2 + 3)

### 3a. Tenant — **the filter AND the handler**, deliberately both

Criterion 2 offers a choice ("matched by the filter — or the handler ignores the body field"). **Both were
done**, and that is not belt-and-braces padding; the two halves answer different questions.

- **The filter** (`TenantAuthorizationFilter.ExtractTenantId`, new `SendToIndexRequest` case) is where
  ADR-008 says a whole-request resource decision belongs, and it is where the four sibling RAG request types
  are already bound. Adding the missing case is the smallest correct change and it makes the *rejection*
  canonical: a body tenant that disagrees with `tid` now gets the same 403 those four already got.
- **The handler** resolves the partition from `TenantResolution.ResolveTenantId(httpContext.User)` and
  rejects a mismatch itself, then passes **the token's value** to `FileIndexRequest.TenantId`. The body field
  is never read for the partition again.

The reason for the second half is that the first half is *detachable*. `AddTenantAuthorizationFilter()` is
one line on a route registration; removing it is exactly how this hole existed for the other four types'
neighbours. With the handler binding in place, detaching the filter degrades the error (the tenant test still
fails, see §4) but cannot re-open a caller-chosen partition.

**Rejected, never silently overridden** (criterion 3). Silently substituting `tid` would tell a caller their
chunks landed in the partition they named when they landed somewhere else — a lie that is worse than a
refusal, because the caller has no way to detect it. The refusal is `403` + ProblemDetails with
`code = SEND_TO_INDEX_TENANT_MISMATCH`.

### 3b. Per-document — **per-row authorization, the opposite of task 062's choice, and here is why**

Task 062 faced the same fork on `/office/search/entities` and chose an **impersonated Dataverse query** over
per-row post-trim, recording three reasons. All three are absent here, and this is the comparison the brief
asked to see rather than a restatement of the 062 note:

| 062's reason to reject per-row | Does it apply to `send-to-index`? |
|---|---|
| **Cost**: up to 250 sequential `RetrievePrincipalAccess` calls on a **keystroke-driven typeahead** with a 500 ms written contract | **No.** The id list here is explicit and caller-authored — the pane sends **one**, the ribbon sends the user's grid selection. And the route ALREADY spends, per document, a Dataverse read + an SPE download + text extraction + embedding + an index write + a Dataverse write. One access check is a small fraction of that, not a new order of magnitude |
| **Coverage**: post-trim can only trim rows already fetched, so a narrowly-entitled caller gets a short page indistinguishable from "no matches" | **No.** There is no ranking and no page. The caller names the ids; every named id gets an explicit per-id verdict in the response |
| **Types**: the shared entity-set allow-list has no `account`/`contact` entry | **No.** One entity type, `sprk_documents`, hard-coded at the call site |

And one reason that points the other way: **impersonation cannot express this check at all**. The subject is
not "which rows may I see" — it is "may I perform a WRITE to this specific row", asked *before* an app-only
write. There is no query whose result set answers it.

**Why `Write` and not `Read`.** This route mutates the row (`sprk_searchindexed`, `sprk_searchindexedon`,
`sprk_searchindexcompletedon`, `sprk_searchindexname`). Read access is not consent to be written to. The
distinction is asserted directly: `CallerWithReadButNotWrite_IsRefused_AndTheRowIsNotStamped` would pass
against a `Read` gate.

**Why the handler and not a filter.** A filter can only allow or deny the whole request. This route's
response contract is a per-document result list, and the partial-permission behaviour below is only
expressible where those results are built. This is the same filter/endpoint **pair** `RouteAuthorizationGuardTests`
Rule B was widened to accept in UAC-r2 task 077, and the same split task 032 used on the visualization route.

**A fail-closed consequence, stated rather than buried.** `DataverseAccessDataSource.GetRecordAccessAsync`
falls back to a retrieval probe when `RetrievePrincipalAccess` gives no answer, and that probe **grants at
most `Read`** (`:436-449`). So during an RPA outage this route refuses every caller with a 403. That is the
correct direction for a write — but it is a visible outage, not a degradation, and an operator seeing
`SEND_TO_INDEX_FORBIDDEN` for everyone should suspect RPA before suspecting permissions.

> Correction for a future reader: `.claude/constraints/auth.md` (§ "Authorization Check Pattern", dated
> 2026-08-20) states that `RetrievePrincipalAccess` "has zero call sites in the repository" and that both
> modes "grant at most `AccessRights.Read`". **That is no longer true** — `TryRetrievePrincipalAccessAsync`
> is live on both `GetUserAccessAsync` and `GetRecordAccessAsync`, and returns Dataverse's real rights
> including `Write`. This task depends on that, so it is recorded here. The constraint file is `.claude/`
> and out of this task's write boundary; flagged, not edited.

---

## 4. The partial-permission contract, stated exactly (acceptance criterion 5)

Authorization for **every** requested id is decided **first, in one pass, before any Dataverse read or file
download**. Then:

| Case | HTTP | Response | Row state |
|---|---|---|---|
| Caller may write **none** of the ids | **403** `SEND_TO_INDEX_FORBIDDEN` | ProblemDetails, no per-document list | **No row is read.** Nothing indexed, nothing stamped |
| Caller may write **some** | **200** | `SuccessCount`/`FailedCount` split. Each denied id is its own result: `Success=false`, `ChunksIndexed=0`, a denial `ErrorMessage`, and **`ParentEntityType`/`ParentEntityId` null** | Permitted rows indexed + stamped as before. **Denied rows are never read, never indexed, never stamped** — not partially stamped |
| Caller may write **all** | **200** | unchanged from before this task | unchanged |

Three decisions inside that table are choices, not defaults:

- **All-denied is 403, not a 200 with `SuccessCount=0`.** A 200 reporting "0 of 1 succeeded" is
  indistinguishable from an indexing outage, and both in-repo callers render it as "try again" —
  `FindView.tsx:320-328` treats `!result.success` as a retryable failure, and the ribbon shows a generic
  result message. A 403 with a readable `detail` is the honest signal and the pane already surfaces it.
- **Partial is 200, not 403.** One unauthorized id in a ribbon multi-select must not deny service to the
  rest of a legitimate batch. The route's per-document result list already exists to carry mixed outcomes
  (not-found, no-file, index-error); an access denial is the fourth member of that set.
- **A denied document discloses no parent.** On success this route returns the row's parent
  matter/project/invoice id (`:760-761`), which the finding names explicitly. A denied result carries neither
  the type nor the id, and — because authorization runs before the read — the handler never learns them
  either. It also means "document not found" vs "document has no file" is no longer an existence oracle over
  ids the caller may not touch.
- **Denied and non-existent became indistinguishable, and that is a gain, not a side effect.** Dataverse
  answers `AccessRights.None` for a record the caller cannot see *and* for a record that is not there, so a
  single-id request now returns the same 403 either way. Previously the two were distinguishable: an id the
  caller had no rights on still reached `GetDocumentAsync` and came back with `"Document not found"` or
  `"Document does not have an associated file"` — an existence-and-shape oracle over arbitrary GUIDs. This is
  the same property task 064 is closing on `/office/todo`'s 403-vs-201 split.
- **An id that is not a parseable, non-empty GUID is denied**, not probed. Previously it fell through to
  `GetDocumentAsync` and came back "Document not found". There is no record to evaluate, and "no answer" must
  not resolve to "proceed".

---

## 5. The tests, and the seed in BOTH directions for BOTH mechanisms (criteria 4 + 6)

`tests/integration/contract/Api/Ai/SendToIndexAuthorizationContractTests.cs` — **8 tests**.

Module boundaries substituted, and only these: `IDocumentDataverseService` (the row read + the stamping
write), `IFileIndexingService` (the OBO download → extract → chunk → embed → write pipeline),
`IGenericEntityService` (the index-name resolver's Dataverse reads), and `IAccessDataSource` (what Dataverse
would answer, **deny-by-default**). Everything between is shipped code: the real route, the real
`TenantAuthorizationFilter`, the real `AuthorizationService`, the real `AccessRights.Write` comparison, the
real handler. No `Mock<HttpMessageHandler>`, no DI-registration assertion, no ctor null-check (ADR-038
B1/B16/B17).

| Test | Proves |
|---|---|
| `TenantIdThatIsNotTheCallersTid_IsRejected_AndNothingIsIndexedOrStamped` | **GATE 1.** 403 · no partition written · no row stamped |
| `TheIndexedPartitionIsTheTokensTid_NotTheBodyValue` | the partition key is the token's exact value (body is uppercased; accepted, but not used) |
| `CallerWithReadButNotWrite_IsRefused_AndTheRowIsNotStamped` | **GATE 2.** the gate is `Write`, not "holds any right" |
| `WhenNoRequestedDocumentIsWritable_Returns403_AndNeverReadsAnyRow` | fail-closed, and closed before the first read — no existence oracle |
| `PartialPermission_IndexesAndStampsOnlyThePermittedDocument` | the whole §4 table: the split, the row state, the withheld parent id |
| `AccessIsEvaluatedAsTheCaller_AgainstTheDocumentRecord_WithTheirBearerToken` | OBO as the caller, against `sprk_documents`, with the caller's token — asserted as observed calls |
| `TheSameDocumentTwice_IsAuthorizedOnce` | memoization, asserted as observed calls |
| `RunIndex_ForAnAuthorizedUserInTheirOwnTenant_StillIndexesAndStamps` | the shipped pane surface still works (criterion 7) |

**Test-scope justification (criterion 10)**: the task names the tenant binding, the per-document check, the
partial-permission case and the seed directions. Six of the eight are literally those. The two beyond —
`AccessIsEvaluatedAsTheCaller…` and `TheSameDocumentTwice…` — pin the *mechanism* rather than the outcome:
the first is the difference between a caller-scoped check and an app-only one that answers "yes" for
everyone (i.e. the difference between a fix and a no-op), and the second guards the one performance property
the mechanism choice in §3b rests on.

### The four observations

**1. GREEN before seeding** — 8 new + the 4 pre-existing `send-to-index` contract tests:

```
Passed!  - Failed:     0, Passed:    12, Skipped:     0, Total:    12, Duration: 4 s - Sprk.Bff.Api.Tests.dll (net10.0)
```

**2. SEED A — tenant binding disabled, RED.** Both halves seeded, because seeding only one would prove less:
the `SendToIndexRequest` case removed from `ExtractTenantId`, **and** the handler reverted to
`callerTenantId = request.TenantId` with its mismatch rejection bypassed.

```
Failed!  - Failed:     2, Passed:     6, Skipped:     0, Total:     8, Duration: 2 s - Sprk.Bff.Api.Tests.dll (net10.0)
```

```
Failed … TenantIdThatIsNotTheCallersTid_IsRejected_AndNothingIsIndexedOrStamped
   Expected _fixture.CapturedIndexRequests.Select(r => r.TenantId) to be empty because nothing may be written to ANY
partition on the refusing path, and above all not to a partition the caller named in the body, but found
{"victim-tenant-partition-not-mine"}.

Failed … TheIndexedPartitionIsTheTokensTid_NotTheBodyValue
   Expected _fixture.CapturedIndexRequests to be "63333333-aaaa-5555-bbbb-7777cccc7777" because the partition key
handed to the indexing pipeline must be derived from the 'tid' claim … but
"63333333-AAAA-5555-BBBB-7777CCCC7777" differs near "AAA" (index 9).
```

**2 of 8, not 8 of 8 — stated rather than glossed.** The other six exercise the per-document mechanism, which
is a different mechanism and is correctly unaffected.

> 🔴 **The seed caught a vacuous test, which is the whole reason for doing it.** On the first seeded run only
> **one** test went red. `TheIndexedPartitionIsTheTokensTid_NotTheBodyValue` uppercases the body's tenant to
> prove the partition came from the token — but the fixture's tenant constant was
> `63333333-4444-5555-6666-777777777777`, **all digits**, so `ToUpperInvariant()` was a no-op and the test
> passed for the wrong reason in both directions. The constant now contains hex letters
> (`63333333-aaaa-5555-bbbb-7777cccc7777`) and the test falsifies. This is task 056's lesson arriving on
> schedule: the test had never been seen to fail, and it could not have.

**3. SEED B — per-document write check disabled, RED.** The pre-pass left in place but forced to permit
everything.

```
Failed!  - Failed:     5, Passed:     3, Skipped:     0, Total:     8, Duration: 2 s - Sprk.Bff.Api.Tests.dll (net10.0)
```

```
Failed … CallerWithReadButNotWrite_IsRefused_AndTheRowIsNotStamped
   Moq.MockException : the sprk_searchindex* stamp is the write this task exists to authorize
Expected invocation on the mock should never have been performed, but was 1 times: d =>
d.UpdateDocumentAsync(It.IsAny<string>(), It.IsAny<UpdateDocumentRequest>(), It.IsAny<CancellationToken>())

Failed … PartialPermission_IndexesAndStampsOnlyThePermittedDocument
   Expected body.SuccessCount to be 1, but found 2.

Failed … WhenNoRequestedDocumentIsWritable_Returns403_AndNeverReadsAnyRow
   Expected response.StatusCode to be HttpStatusCode.Forbidden {value: 403} …, but found HttpStatusCode.OK {value: 200}.

Failed … AccessIsEvaluatedAsTheCaller_AgainstTheDocumentRecord_WithTheirBearerToken
   Expected _fixture.Access.RecordChecks to contain a single item, but the collection is empty.

Failed … TheSameDocumentTwice_IsAuthorizedOnce
   Expected _fixture.Access.RecordChecks to contain 1 item(s) …, but found 0: {empty}.
```

**5 of 8.** The three that still pass are the two tenant tests and the shipped-surface test — correctly
unaffected.

**4. RESTORED GREEN** — both seeds reverted from backups, `grep SEED-` returns 0 in both production files:

```
Passed!  - Failed:     0, Passed:    12, Skipped:     0, Total:    12, Duration: 2 s - Sprk.Bff.Api.Tests.dll (net10.0)
```

> ⚠️ **A worktree hazard worth recording, because it nearly produced a false result.** Restoring the seeded
> files with `Copy-Item` preserved the *backup's* timestamp, which was OLDER than the build output. MSBuild's
> up-to-date check therefore skipped the rebuild, reported `Build succeeded`, and the test run executed the
> **seeded** binaries against restored source — a "restored GREEN" that would have been a lie. Caught because
> the run was still red after a restore that `grep` said was complete. `LastWriteTime` was set forward and the
> build repeated.
>
> The same trap has a **second, worse form**, hit later in this task: `dotnet build` of the test project
> **failed** (`MSB3027` — the output dll was locked by a sibling agent's `testhost`), and the `dotnet test
> --no-build` that followed ran the **previous** assembly and printed `Passed! - Failed: 0, Passed: 12`. The
> two commands are independent; the build's failure does not stop the test run, and the green result carries
> no indication that it describes stale code. **A green `--no-build` result is evidence only if the
> immediately preceding build is confirmed to have succeeded.** Three sibling agents share this worktree's
> `bin/`, so a concurrent build can also overwrite the assembly between your build and your test run; one
> earlier failure in this task was exactly that and vanished on re-run. Both directions occur: a false RED
> and a false GREEN.

### 5a. Four concurrency hazards in a shared worktree, and the one that matters most

Recorded because this task and task 064 hit all four within an hour, each one produced a commit that
misdescribed its own contents, and none of them is obvious from the tooling. Task 064's note §7.1 carries the
same list from the other side.

1. **The `.git/index` is shared, not just the working tree.** `git add` then `git commit` is **not atomic
   between agents**: whichever agent calls `commit` first commits whatever is in the index, including another
   agent's staged files. Both of us built careful single-line blobs for `TASK-INDEX.md` and both were defeated
   by simply not being the one who called `commit`.
2. **`.husky/pre-commit` runs `npx lint-staged`**, which stashes and restores unstaged changes around the
   formatters. This is the likeliest route by which files that were *not* in the index at `commit` time still
   appeared in the commit — the index race alone does not account for untracked files arriving.
3. **`git commit --only -- <paths>` and `git commit -- <paths>` commit the WORKTREE content of those paths,
   not the staged content.** This is right for files only one agent touches and wrong for a shared file: an
   amend intended to remove another agent's `TASK-INDEX.md` row put it straight back, because the worktree had
   both rows. The `--stat` line said `2 +-` and looked correct.
4. **`git commit --allow-empty` with no pathspec is not empty** — it commits the shared index.

**The lesson that generalises**: `--stat` and `--name-only` tell you *which files* a commit touched, not
*whose content* is in them. On a shared file, verify that the content you did **not** intend to change is
identical to its parent — `git diff <parent> HEAD -- <path>` should touch only your own lines — rather than
confirming that the file you did intend to change appears.

> **Corrected after the fact, because the first correction was itself wrong.** This section originally
> prescribed *"compare the byte length of the specific line against `HEAD~1`"* as the check that beats a
> `--stat` line. That check then failed on its very first use: task 064 and this task measured the same two
> unchanged rows and produced **three different figures, of which two were wrong** — and it took two rounds
> of correction between the agents to establish which.
>
> | Figure (063 / 064) | Whose | Verdict |
> |---|---|---|
> | 3,386 / 3,062 | task 064 | ❌ `awk -F'\|' '{print length($3)}'` returns the third **pipe-delimited field**; these rows contain eight internal `\|` characters, so it truncated at the first inline table or code span |
> | 3,397 / 3,088 | task 063 | ✅ correct — as a **character** count |
> | 3,437 / 3,113 | task 064 | ❌ `wc -c` **including the trailing newline** |
> | **3,436 / 3,112** | both, independently | ✅ the actual **UTF-8 byte** lengths |
>
> Characters and bytes diverge here because the rows carry 21 and 13 non-ASCII characters (`—`, `✅`, `🔴`,
> `⚠️`), worth 39 and 24 extra bytes. **Nothing had changed in the content at any point.**
>
> A first attempt to explain the gap blamed CRLF conversion. That cannot be right, and the refutation needed
> no tooling: line-ending conversion moves a line by exactly **one** byte, while the observed deltas were 11
> and 26 **and differed from each other**. Accepting a mechanism without checking whether its magnitude was
> even the right order is the actual error, and it is a cheaper one to catch than any of the three above.
>
> **So the durable rule is about the KIND of check, not the units.** A derived scalar — a count, a length, a
> `--stat` figure — fails silently and plausibly, and produces a number that still looks like evidence. A
> content diff either matches or it does not, cannot be truncated by the data it is measuring, and has no
> units to get wrong. The uncomfortable part is the symmetry: having replaced the `--stat` trap with a
> scalar, both agents then reached for a plausible-sounding cause (CRLF) instead of testing it — the same
> failure mode one layer up, and the reason this paragraph exists rather than a quiet edit.

---

## 6. What changed

### `src/server/api/Sprk.Bff.Api/Api/Filters/TenantAuthorizationFilter.cs`
- One new `case SendToIndexRequest` in `ExtractTenantId`'s existing match list, with the reason in code.
  Nothing else in the filter changed — the 401/403 behaviour it already had now applies to this type.

### `src/server/api/Sprk.Bff.Api/Api/Ai/RagEndpoints.cs`
- Route registration: `.ProducesProblem(403)` and a comment recording that authorization here is a
  filter/handler **pair** and why.
- `SendToIndex` takes `Spaarke.Core.Auth.AuthorizationService` (already registered; already a handler
  parameter on `RecordSearchEndpoints`).
- Tenant: resolve from `tid`; 401 if the principal carries no tenant claim; 403 on a body mismatch;
  `FileIndexRequest.TenantId = callerTenantId`.
- Caller context: 401 if there is no resolvable `oid` or no bearer token — access could then only be
  evaluated app-only, which is the hole rather than the fix.
- A single authorization pre-pass producing one verdict per requested **position**, memoized by the **parsed**
  record id. Keyed on the parsed id rather than the raw string so that `{ABC-…}` and `abc-…` cost one round
  trip rather than two, and so a null or malformed entry never becomes a dictionary key — it is denied
  without being used as one. Then the existing per-document loop, with a refusal branch at its head.
- Method remarks document the mechanism choice, the partial-permission contract and the cost model, because
  the next person to touch this route needs them there and not only here.

### `src/server/api/Sprk.Bff.Api/Services/Ai/FileIndexingService.cs` — **not modified**
The POML lists it `role="edit"`. It needed no change: it consumes `FileIndexRequest.TenantId`, and fixing the
*source* of that value at the handler fixes the partition end to end. Editing the service to re-derive tenant
would have put `HttpContext`-derived identity into the service layer for no gain. Stated rather than silently
skipped.

### `tests/integration/contract/Api/Ai/RagSendToIndexIndexNameContractTests.cs` and `…ReplaceStaleChunksContractTests.cs` — **modified, and not in the POML file list**
Not scope creep: the new gate correctly 403'd four pre-existing tests whose fixtures grant no access. Each
fixture now registers the same deny-by-default `ProgrammableRecordAccessSource` and **grants only its own
document, only the rights the route needs** — an explicit narrow grant rather than a blanket allow, so those
files cannot silently mask a future authorization regression. Same class of deviation task 032 §7 recorded.
Neither file is owned by a concurrently-active sibling agent (064 holds `OfficeEndpoints.cs` /
`OfficeService.cs` / `TodoSourceAccessFilter.cs`; 066 holds `CommunicationsEndpoints.cs`).

### Not changed, deliberately
- **`tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.cs`** — contested with another project and owned by
  the deferred task 061. See §8.1.
- **`SendToIndexRequest.TenantId` is still `required`.** Both in-repo callers send it; making it optional is a
  contract change that buys nothing while the value must equal `tid` anyway.
- **No client change.** See §7.
- **`.claude/constraints/auth.md`** — carries a stale claim this task disproves (§3b). Out of the sub-agent
  write boundary; flagged for the main session.

---

## 7. The Find view's Run Index still works (acceptance criterion 7)

| Check | Result |
|---|---|
| Server half — the pane's exact request shape (one lowercased brace-free id + the MSAL account tenant) indexes and stamps | ✅ `RunIndex_ForAnAuthorizedUserInTheirOwnTenant_StillIndexesAndStamps`: 200, `SuccessCount=1`, `ChunksIndexed=3`, `IndexName` set, `SearchIndexed=true` + `SearchIndexCompletedOn` stamped |
| The tenant the pane sends is the token's | ✅ verified read-only: `FindView.tsx:305` uses `authService.getAccount()?.tenantId` — the MSAL account's tenant, i.e. the `tid` in the very token on the same request. The binding cannot reject it |
| The other in-repo caller | ✅ `sprk_DocumentOperations.js:2080-2081` uses the MSAL account's `tenantId`, falling back to the `sprk_TenantId` environment variable — the same AAD tenant. Unaffected |
| Response contract unchanged | ✅ same `SendToIndexResponse`, same fields, same per-document result shape. Only the *contents* of a denied result differ, in the direction of disclosing less |
| Client handles the new 403 | ✅ **no client change needed**, verified read-only: `ApiClient.ts:150-167` parses the ProblemDetails body and throws `ApiClientError` carrying `detail`; `FindView.tsx:340-348` renders `err.error.detail`. A refused Run Index shows "You do not have permission to index any of the requested documents." rather than a generic failure |

**Not verified**: the pane has **not** been exercised against a live BFF + Dataverse from this worktree — no
deployed environment is in the loop. In particular, whether a typical add-in user's Dataverse role actually
grants `Write` on `sprk_document` is **unproven here**, and if it does not, Run Index returns 403 for
everyone. See §8.2 — this is the largest residual on this task, and task 066's live findings make it a real
question rather than a formality.

---

## 8. Recorded honestly — what is NOT met, and what is left open

1. **🔴 Acceptance criterion 8 ("Task 061's guard no longer flags this route") is NOT met, and cannot be met
   by this task.** The POML gates 063 on 061; 061 is deliberately deferred by the orchestrator because
   `RouteAuthorizationGuardTests.cs` is contested with another project, and this task was instructed not to
   touch it. Verified read-only: `GovernedFiles` contains `Api/Ai/SemanticSearchEndpoints.cs`,
   `Api/Ai/RecordSearchEndpoints.cs` and `Api/Ai/VisualizationEndpoints.cs` — **`Api/Ai/RagEndpoints.cs` is
   absent**, so the guard does not govern this route at all today and there is nothing to un-flag. When 061
   lands it must classify `Api/Ai/RagEndpoints.cs` and record `/send-to-index` as the **filter + handler
   pair** (tenant gate in the filter, per-document check in the handler) — the same distinction Rule B was
   widened for in UAC-r2 task 077, and the same one task 062 must have recorded for Office. **A real open
   item, not a formality**: until 061 lands, nothing would notice `AddTenantAuthorizationFilter()` being
   detached from this route.
2. **🔴 No live verification, and one specific question behind it.** The gate is `AccessRights.Write` on
   `sprk_document`, evaluated via `RetrievePrincipalAccess` as the caller. Whether the add-in's users hold
   Write on the documents they index in dev is **unknown to this task**. Task 066 established live that
   `Spaarke Office Add In User` grants no read on `sprk_communication` or `sprk_todo` at any depth and that
   `Spaarke Basic User` carries the real depth — so "the role does not grant what the code assumes" is a
   demonstrated failure mode in this environment, not a hypothetical. **Someone must confirm Write on
   `sprk_document` for a representative add-in user before or with deployment.** If it is missing, Run Index
   403s — fail-closed and loud, which is the correct direction, but it is a visible outage on a shipped
   surface. The same check should cover the ribbon's users, who may hold different roles.
3. **RPA outage now degrades this route to a 403 for everyone**, because the fallback probe grants at most
   `Read` (§3b). Correct for a write, but an operator seeing a fleet-wide `SEND_TO_INDEX_FORBIDDEN` should
   suspect `RetrievePrincipalAccess` before suspecting permissions. → worth a line in a runbook.
4. **The other three RAG routes on this group are unexamined.** `/index`, `/index/batch` and `/index-file`
   are bound by the tenant filter (their request types ARE matched), but none of them authorizes a document
   before writing to the tenant's partition, and `/index-file` takes a caller-supplied `DriveId`/`ItemId`.
   That is **not finding F2** and widening scope to it silently is what CLAUDE.md §11 forbids. →
   **filed for `notes/defer-issues.md`.**
5. **`POST /api/ai/rag/enqueue-indexing` is a different trust model and was not touched.** It authenticates
   with the `RagApiKey` scheme, carries no `tid`, and takes its tenant from the body by design
   (`TenantResolution`'s own remarks call this out as the one legitimate principal with no tenant claim). The
   binding added here is on the JWT route only and does not reach it.
6. **No out-of-repo caller was found that needs a body tenant ≠ its token** (escalation trigger did not
   fire). Every in-repo HTTP caller was enumerated before the contract changed: `FindView.tsx:313` and
   `sprk_DocumentOperations.js:2106` are the only two, and both source the tenant from MSAL. Two near-misses
   worth naming so a later reader does not re-derive them: `useDocumentActions.sendToIndex`
   (`Spaarke.DocumentOperations`) posts `/api/documents/{id}/analyze`, a different route despite the name;
   and the SemanticSearch code page reaches this route only through that hook. Background indexing goes
   through `RagIndexingJobHandler` / `PostUploadIndexingEnqueuer` in-process, never over HTTP. **A caller
   outside this repo would still break** — the tightening is a genuine behaviour change, and the 403's
   `code` is machine-readable so such a caller fails loudly.

---

## 9. Verification (real output, not assertion)

| Check | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | `Build succeeded. 0 Warning(s) 0 Error(s)` |
| New authorization contract tests | `Passed! - Failed: 0, Passed: 8` |
| Reproduce-first, pre-change (control) | `Failed! - Failed: 6, Passed: 2` |
| Seed A — tenant binding disabled (control) | `Failed! - Failed: 2, Passed: 6` |
| Seed B — per-document check disabled (control) | `Failed! - Failed: 5, Passed: 3` |
| Restored | `Passed! - Failed: 0, Passed: 12` (8 new + 4 pre-existing send-to-index) |
| `tests/Spaarke.ArchTests` | `Passed! - Failed: 0, Passed: 191, Skipped: 0, Total: 191` |
| Full BFF suite (`tests/unit/Sprk.Bff.Api.Tests` — includes `contract/**`, `auth/**`, `tenant/**`, `seam/**`, `regression/**`, `data-mutation/**`) | `Passed! - Failed: 0, Passed: 12403, Skipped: 56, Total: 12459, Duration: 10 m 54 s` |
| **Test-count reconciliation** | Post-062 baseline **12,387 / 0 / 56** → **12,403 / 0 / 56**. **Δ = +16, and +16 is correct — only +8 is this task's.** Three sibling agents share this worktree and their work is uncommitted in the same tree, so the other **+8** is task 064's `OfficeTodoSourceAuthorizationContractTests.cs`. This task's own contribution is exactly the **8** tests in `SendToIndexAuthorizationContractTests.cs`. Independently corroborated: task 064 measured the same 12,403 total from its own run and agreed the reconciliation is `12,387 + 8 (064) + 8 (063)`. **Neither agent can reconcile to "baseline + my own delta" while both changesets are uncommitted** — stated rather than quietly claiming +16. No test was deleted, skipped or silently repaired; skipped stays 56 |
| `dotnet list package --vulnerable --include-transitive` | `The given project 'Sprk.Bff.Api' has no vulnerable packages given the current sources.` — no new HIGH/CRITICAL. No package was added or upgraded |

### Publish size — fresh build of `origin/master`, not the recorded number (CLAUDE.md §10 bullet 4)

| Field | Value |
|---|---|
| Command | `dotnet publish -c Release -o <out>` on `Sprk.Bff.Api.csproj` |
| RID / mode | framework-dependent **linux-x64** (from the csproj), not self-contained |
| Compression | PowerShell **`Compress-Archive -CompressionLevel Optimal`** (the method `scripts/Deploy-BffApi.ps1` uses) |
| PDBs | **included** (4 `.pdb` in both publishes) |
| `origin/master` @ `99cdfe2ea`, freshly built + zipped in a detached worktree today | **45.46 MB** |
| This branch | **45.54 MB** |
| **Delta** | **+0.079 MB** |

Far under the **+5 MB** single-task escalation threshold and the **60 MB** ceiling. Two honesty notes:
(a) the recorded 2026-09-02 baseline (45.42 MB @ `a826cf347`) was **not** used — master was rebuilt today,
which is what the rule requires; (b) the branch figure is the **whole branch** vs master, including this
project's earlier tasks and three sibling agents' concurrent uncommitted BFF edits in this worktree. It is
therefore an **upper bound** on this task's own contribution, not an under-count — task 062 measured the
identical `45.46 / 45.54` pair before this task's code existed, which places this task's own contribution
below the resolution of the measurement. No package and no file was added to the publish.

---

## 10. Step 9.5 quality gates

| Gate | Result |
|---|---|
| `adr-check` | **0 violations.** ADR-001 (Minimal API, no new endpoint) ✓ · ADR-003 (fail-closed, deny by default; no new service layer — an existing `IAuthorizationRule`-backed evaluator is consumed) ✓ · ADR-004/ADR-028 (no new credential; the check rides the caller's existing bearer token through the existing OBO seam; no `.WithClientSecret`) ✓ · ADR-007 (no `Microsoft.Graph` outside Infrastructure) ✓ · **ADR-008** (the whole-request tenant decision is in an endpoint filter; the per-resource decision that cannot be expressed in a filter is in the handler, documented in code — the sanctioned filter/endpoint pair, not a workaround; group keeps `RequireAuthorization()`; no global middleware) ✓ · ADR-009/010 (no new DI registration, no new interface, no new cache; the access cache already decorates the seam) ✓ · ADR-013 (no AI-internal type injected into CRUD code) ✓ · **ADR-016** (tenant data isolation — this is the ADR the filter exists for) ✓ · ADR-019 (`Results.Problem` ProblemDetails with a machine-readable `code`, matching the file's existing error shape) ✓ · ADR-029 (publish size measured against a fresh master, +0.079 MB) ✓ · **ADR-038** (new tests at the `tests/integration/contract/**` KEEP path; no `Mock<HttpMessageHandler>`, no DI-registration test, no ctor null-check; mocks only at module boundaries) ✓ · ADR-044 (ids typed `Guid` via `Guid.TryParse`; no raw GUID interpolated into an OData predicate) ✓ |
| `code-review` | **0 critical.** No secrets · no swallowed exception (the new refusals are explicit and logged) · no sync-over-async · no catch-log-rethrow · no code-restating comments · fail-closed on every new branch. **1 observation accepted**: `SendToIndex` grew by ~110 lines, roughly half of it the remarks that make the mechanism choice and the partial-permission contract reviewable. Per CLAUDE.md §11.5 / `COMPONENT-COMPLEXITY.md` this is evaluated on cohesion, not LOC: it is one responsibility (index the documents this caller is entitled to index, into this caller's tenant) and no second reason-to-change was introduced. No decomposition warranted. **1 observation acted on**: the added Dataverse round trip per distinct id is an N+1 by construction — sized in §3b against the route's existing per-document cost, memoized, and pinned by `TheSameDocumentTwice_IsAuthorizedOnce` rather than asserted |
| Lint / build (`-warnaserror` is on — the seed's `if (false)` was rejected as CS0162) | `Build succeeded. 0 Warning(s) 0 Error(s)` |

**No §6.5 ADR conflict arose.** The one place the design could have drifted is ADR-008's default shape —
putting the per-document check in a filter. That shape cannot express a per-document result list, so it would
have forced an all-or-nothing refusal and, with it, a worse contract for the ribbon's multi-select. The
sanctioned filter/endpoint pair was used instead, which is what the ADR's own integration with UAC-r2 task
077 provides for. No ADR rule pushed this design toward a worse security outcome, so neither a project-scoped
exception (path A) nor an amendment (path B) was needed.
