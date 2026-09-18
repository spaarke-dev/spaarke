# Defer / Issue Tracking — unified-access-control-r2

> **Source of truth** for deferred work + newly-discovered issues in this project.
> Each entry has a paired GitHub Issue. See `/project-defer-issue-tracking` skill for the protocol.
>
> **Rollup view**: `gh issue list --state all --label unified-access-control-r2`. The label was created
> 2026-09-15 and applied to every issue this file tracks (the Disposition table below). Before that the
> command returned nothing, because the label did not exist.
> **CLAUDE.md §11 rule**: every entry MUST name a concrete behavior or contract that fails without it.

## Disposition rule (owner, 2026-09-15)

> *"Issues should only go past the project if they are not material to the project or they are
> confirmed/validated as another project's subject matter."*

An entry **stays and is fixed here** unless (a) it is shown **not material** to this project, with the
evidence recorded below, or (b) another project's **ownership is confirmed** (named project + evidence).
When materiality is unclear, it stays. A hand-off is recorded here AND as a comment on the issue. Task 090
(wrap-up) cannot close the project while an entry material to it is open.

## Disposition (reviewed 2026-09-15)

| Entry | Issue | Disposition | Where / evidence |
|---|---|---|---|
| DEF-001 — FR-33 expiry | #961 | **In project** | Tasks 097 / 098 / 100 ✅; 099, 101, **107** open |
| ISS-001 — group double-count | #962 | Handed off — not material | The only consumer is `RagService` (AI search trimming — out of scope, finding A-21); `Services/Ai/` internals belong to `spaarke-ai-architecture-redesign-r2` (`projects/INDEX.md`) |
| ISS-002 — `$top=200` truncation | #963 | **In project** | Task **105** |
| ISS-003 — requester PATCH on SR to-dos | #964 | **In project** — blocked on the owner's product question | Task **054**, amended 2026-09-15 (its fourth-root premise contradicts task 028's ruling) |
| ISS-004 — org-revoke N+1 | #968 | ✅ Done | Task 024 |
| ISS-005 — `BulkUpdateAsync` not atomic | #970 | ✅ Done | Task 096 |
| ISS-006 — singleton `LastException` | #971 | Handed off — not material; **no confirmed owner** | No access-control path branches on exception text (grep of `ExternalAccess/**`, `Auth/**`, `PlaybookSharingService`); `AssociateAsync` callers are AI + Insights only; `BulkUpdateAsync` guarded by task 096 |
| ISS-007 — layout defaults | #972 | Handed off — not material | Workspace layouts, not access control |
| ISS-008 — re-grant 409 | #973 | **In project** | Task **106** |
| ISS-009 — non-BFF grants unbounded | #974 | **In project** | Task **107** |
| ISS-010 — H9 slot guard | #987 | **In project** — fixed `6149edecf` | Closes when #950 merges; real-ARM check before merge |
| ISS-011 — Service Bus accepts SAS | #988 | Handed off — not material; **no confirmed owner** | Every job handler uses `SubjectId` for logs / telemetry only — none acts as that user, so a forged message cannot change an access decision in this project's evaluator. A standing security risk; ADR-052 §6 blocks Function impersonation until it lands |
| ISS-012 — typed requester on `JobContract` | #989 | Handed off — not material | Needed only by a Function that impersonates; none is in scope. ADR-052 §6 makes it a prerequisite for the first project that builds one |
| ISS-013 — impersonation helper fail-closed | #990 | **In project** — ✅ implemented by task **104** on the branch; closes when #950 merges | Residuals recorded on #990: the service's `Guid?` still means impersonate-or-app-only for writes; `ApplyAsEntraUser` has no production caller yet |
| ISS-014 — TipTap 2.x advisory | #991 | Handed off — Compose confirmed as owner | LegalWorkspace gets TipTap only by transpiling `Spaarke.Compose.Components` (`composeEditor.registration.ts`); Compose owns the editor (ADR-049); next round `spaarkeai-compose-r7` |
| ISS-015 — xmldom / dompurify | #992 | Handed off — not material | Same versions on master before this project; `@xmldom/xmldom` arrives via `mammoth`, declared by six packages |
| ISS-016 — `DataverseWebApiClient` gets the container's `TokenCredential` | #993 | Handed off — not material; **no confirmed owner** | SpeAdmin audit/dashboard client (`SpeAdminModule.cs:71`), not access control. Equivalent credential when managed identity is on (deployed); differs only with it off (local/dev). Found by task 104 |
| ISS-017 — `PlaybookSharingService` reads bit 524288 as Share | #994 | Handed off — not material | That bit is **Assign**; Share is 262144 (SDK values). A display-only projection in `Services/Ai/**`, which the AI line owns; no access decision reads it. Found by task 063 |
| ISS-018 — unsecure cannot see a failed share read | #995 | **In project** | Task **108**. `RevokeAllSharesAsync`'s `catch` cannot fire for the failure it names — the soft read answers an EMPTY LIST on failure, so "0 shares revoked" reports as success. This project's own code (task 061), on an access path. Found by task 063 |
| ISS-019 — junction query failure removes the deny veto's org axis | #998 | **In project** | Task **109** (queued 2026-09-18). Pre-dates task 043; 043 briefly documented the property as HELD using a double that throws where the real path does not |
| ISS-020 — date-ended membership still confers | #999 | **In project** — 🔔 **question put to the owner 2026-09-18, UNDECIDED** | Task **110** (queued, BLOCKED on the answer). Fixing it narrows live org-grant inheritance, i.e. changes who has access today |
| ISS-021 — org-baseline N+1 on the hot path | #1000 | **In project** | Task **111** (queued 2026-09-18). N is currently 0 (no org holds a standing grant), so the shape is wrong but the cost is not yet paid |
| ISS-022 — a stale snapshot can shorten access | #1001 | **In project** | Task **112** (queued 2026-09-18). Task **106** Step 9.5 (code-review F5). The election key is now a MUTABLE column; V1's fix does not cover it, and the revoke-race half is pre-existing |
| ISS-023 — a duplicate collapse can lower the effective LEVEL | #1002 | **In project** — ✅ **owner decided 2026-09-18** | Task **113** (queued 2026-09-18). Task **106** Step 9.5 (code-review F10 / adr-check W3). **Pre-existing** (task 010): 106 cannot change level outcomes, because the requested level is written to whichever row is elected. Filed because 106's own argument about duration applies verbatim to amount |
| ISS-024 — external licensed user refused as "not internal" | #1003 | **In project** — ✅ **owner decided 2026-09-18** | Task **114** (queued 2026-09-18). Found by task 063; the owner has ruled an external **licensed** user is treated the same as an internal one. ⚠️ The stale surface is SHIPPED CODE plus an **asserting** contract test, not a POML |
| ISS-025 — the drift check cannot see an index row with no POML | #1004 | **In project** | No task yet. Found 2026-09-18 by creating the condition: six index rows with no POML, and `scripts/check-task-status-drift.ps1` printed both counts and still said "No drift". The mirror of a guard it already has |

> Portfolio board: issues are not on it — the `gh` token lacks the `project` scope
> (`gh auth refresh -s read:project,project`).

---

## Open (in priority order)

### DEF-001 — External grant expiry becomes mandatory, bounded and renewable (spec FR-33)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-09-08 |
| **Source** | Owner reframe of task 023's escalation — *"from a data control should we make expireDate mandatory and with a limit (similar to app reg certs/keys)? is this a security feature, not a bug?"* Accepted in principle the same day. |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/961 |

**Description**

Task 023 fixed the *bug* — the grant upsert silently failed to write `sprk_expiresdate`, so re-granting
to bound access was a no-op that returned 200. It did **not** change the *policy*: expiry remains
optional, so an unbounded external grant is still a legal, one-request outcome.

FR-33 makes expiry **mandatory** on external grants, bounded by a **tenant-configurable maximum**, with
the granting internal user notified before lapse. Expiry should be **matter-bound first** (the closure
cascade already deactivates a project's grants) with the calendar cap as the backstop — that is what
keeps renewal volume survivable.

**Concrete failure mode this prevents** (CLAUDE.md §11): an external grantee — a client contact,
opposing counsel, a vendor — retains read access to a matter indefinitely after the engagement ends,
because revocation requires someone to act on a **non-event**. Nothing fires when access stops being
needed. Their affiliation can change invisibly to us (they leave the firm, the firm is acquired, they
move to the other side of the matter) and no signal reaches Spaarke. Today a single `/grant` with no
`expiryDate` produces exactly that, and nothing in the system ever revisits it.

🔴 **Sequencing constraint**: the renewal notification is a **precondition**, not an enhancement. An
expired app credential breaks a *system* — loud, monitored, fixed in minutes. An expired human grant
breaks a *person*, silently, at a moment they did not choose, discovered through a channel they may no
longer have. Shipping the mandate before the notification converts a silent over-permission problem
into a silent lockout problem. **Do not ship the cap before the notification.**

**Entry-points**

- `projects/unified-access-control-r2/notes/decisions/external-grant-expiry-mandatory.md` — the full decision record, maintenance-debt analysis, and 5 open scoping questions
- `projects/unified-access-control-r2/spec.md` — FR-33 (the requirement) and FR-09 (amended by task 023)
- `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/GrantExternalAccessEndpoint.cs` — the write path; `FormatDateOnly` + the match-path expiry write landed in task 023 (`6ce9c52c2`)
- `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ProjectClosureEndpoint.cs` — the existing closure cascade that already deactivates a project's grants (the matter-bound half)
- `src/server/api/Sprk.Bff.Api/Services/Notifications/` — `OutboxService`, `SignalRDeliveryService` (delivery exists)
- `IScheduledJob` (ADR-004) — the sweep contract, as used by `PlaybookSchedulerJob`
- `projects/unified-access-control-r2/notes/task-023-grant-upsert-expiry.md` §4 — the escalation FR-33 supersedes

**Suggested fix** (if known)

Four pieces, in this order: (1) a scheduled `IScheduledJob` sweep that finds grants nearing expiry and
emits notifications through the existing outbox — **ship this first**; (2) tenant configuration for the
maximum lifetime; (3) `/grant` validation rejecting a missing or over-cap expiry; (4) a staged backfill
plan for existing unbounded grants. Renewal must extend from **today**, not from the previous expiry,
or repeated renewals drift.

**Estimated effort**: unknown — needs a spike. The four pieces are individually small, but the backfill
and the notification-heartbeat design are the risk, not the code.

**Blockers**:
- Five open scoping questions in the decision record §7 — cap default, scope (external only vs internal POA shares too), backfill strategy, renewal authority, notification lead time. **All are owner decisions.**

**Related**:
- Supersedes the open escalation in task **023** (`completed-with-escalation`). With expiry mandatory, `null` unambiguously means "leave unchanged" and there is no clear-operation to design — which is why 023 is NOT being closed with a contract patch that FR-33 would remove.
- Interacts with task **007** (`ExpiryPredicate`, the read-side enforcement this relies on) and task **010** (grant lifecycle / idempotency).
- ADR-003 (fail closed — a grant that confers nothing must not report success), ADR-004 (`IScheduledJob`).

### ISS-001 — PrivilegeGroupResolver double-counts the first page of Graph group memberships

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | someday |
| **Filed** | 2026-09-09 |
| **Source** | Uncovered while designing task 024 (SPE Graph paging) — it is the canonical in-repo `PageIterator` example, so it will get copied |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/962 |

**Description**

`PrivilegeGroupResolver.ResolveGroupMembershipsAsync` collects page 1 in a `foreach` over
`memberOfResponse.Value`, then hands **that same response** to `PageIterator.CreatePageIterator`.
The iterator iterates the page it is given before following `@odata.nextLink`, so every page-1 group
id lands in `groupIds` **twice**.

**Concrete failure mode**: `groupIds` is a `List<string>`, so duplicates reach the caller. Today
likely harmless — the result appears to be used as a membership test, where a duplicate changes no
decision. It is filed because any future consumer that COUNTS, pages or logs groups gets wrong
numbers, and because this is the file task 024 must copy for its own paging. **NOT this project's
surface** (`Services/Ai/Security/` is AI-owned); two-line fix.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Services/Ai/Security/PrivilegeGroupResolver.cs:190` — the pre-loop
- `src/server/api/Sprk.Bff.Api/Services/Ai/Security/PrivilegeGroupResolver.cs:202` — `CreatePageIterator` over the same response

**Suggested fix**: delete the pre-loop; let the iterator callback be the single collection point.

**Estimated effort**: <1h
**Blockers**: none
**Related**: task 024 design note §2.2 — `notes/decisions/spe-paging-and-revoke-honesty-design.md`

### ISS-002 — The external data plane silently truncates at `$top=200` (no `@odata.nextLink`)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-09-09 |
| **Source** | Task 029 code review (Step 9.5). Pre-existing; 029 widened one of the four call sites to two more roots and declined to widen it silently. |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/963 |

**Description**

`ExternalDataService.GetCollectionAsync` returns `result?.Value` and never reads `@odata.nextLink`.
Four call sites pin `$top=200` — `sprk_documents` (`:213`), `sprk_todos` (`:545`), `sprk_events`
(`:564`), `sprk_externalrecordaccesses` (`:1034`). A root with more than 200 children returns exactly
200 and the caller cannot distinguish that from a complete list.

**Concrete failure mode**: a matter with 250 to-dos renders 200 in the external SPA with no "load
more" and no truncation signal — a list that looks complete and is not. Not a disclosure; the mirror
image of one. Same **honesty** class as task 024 (`container_not_cleared` on a multi-page container),
but a different plane — 024 is SPE, this is Dataverse, and fixing one does not touch the other.

⚠️ `:1034` first: it feeds an authorization-adjacent read, where a truncated set can mean a
participant is not seen.

**Entry-points**

- `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalDataService.cs` — `GetCollectionAsync` (~`:1040`) + the four call sites
- [`notes/decisions/spe-paging-and-revoke-honesty-design.md`](decisions/spe-paging-and-revoke-honesty-design.md) — the honesty rule to reuse: an unverified set is never reported as clean
- `Services/Ai/Security/PrivilegeGroupResolver.cs:202` — the in-repo paging precedent, **which carries its own double-count bug** (ISS-001 / #962). Read that first.

**Suggested fix**: follow `@odata.nextLink` behind an explicit max-pages cap, OR return a truncation
flag the caller must handle. **Do not just raise `$top`** — that moves the cliff without removing it.

**Estimated effort**: unknown — the code is small; the client-contract decision (flag vs transparent
paging) is the real work, and these call sites feed shipped SPA views.
**Blockers**: none technical; needs the contract decision.
**Related**: task 024 (same honesty class, different plane) · ISS-001 (#962, the precedent's bug).

### ISS-003 — Workforce caller cannot PATCH a to-do parented to their own service request

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | someday (latent — zero service requests exist in dev) |
| **Filed** | 2026-09-09 |
| **Source** | Task 028, after the owner ruled service requests internal-only |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/964 |

**Description**

`PATCH /api/v1/external/todos/{id}` serves BOTH planes. `GetTodoRootAsync` projects only project,
matter and work assignment, so a to-do parented via `sprk_regardingservicerequest` resolves to
`None` and is denied for **everyone** — including the workforce user who submitted that service
request and can see it in their own Service Requests widget.

**Concrete failure mode**: correct for partners (service requests are never externally grantable),
wrong for the requester, who legitimately holds the record — the `service-requests` module already
scopes exactly that (`sprk_requestedby == caller`, workforce plane only).

**Why 028 did not fix it**: (1) it needs a different rights shape — the other three roots answer
*"is this id in my set?"* synchronously from `CallerPrincipal`; a service request answers *"is
`sprk_requestedby` me?"*, a Dataverse read, and putting I/O behind `RightsForRoot` would quietly
change a contract that is pure for every other root. (2) **What a requester may DO to their own
service request's to-dos is an unanswered product question**, and inventing one to close a task is
how the level asymmetry entered task 009.

**Entry-points**

- `ExternalDataService.GetTodoRootAsync` + `TodoRootBinding`/`RootBindings` (task 029)
- `ExternalProjectDataEndpoints.RightsForRoot` — shared by list/create/update since 029
- `ExternalAccessModule.cs:270-279` — the `service-requests` requester predicate to reuse
- [`notes/task-028-service-request-root.md`](task-028-service-request-root.md) §6

**Suggested fix**: answer the product question first, then decide whether the requester check belongs
inside `RightsForRoot` or as a branch resolving before it. 🔴 **Do NOT add
`AccessibleServiceRequestIds` to `CallerPrincipal`** — note §5 explains why that is actively wrong.

**Estimated effort**: small once the product question is answered; the question is the work.
**Blockers**: the product question (owner).
**Related**: task 028 (this decision) · task 029 (the three-root parity it completes).

---

### ISS-004 — Org-revoke N+1 on container permissions, amplified by task 024's paging

| Field | Value |
|---|---|
| **Status** | ✅ **CLOSED 2026-09-09** — same day, same task, at owner direction ("we need to fix all of these"). |
| **Urgency** | ~~next-round~~ done |
| **Filed** | 2026-09-09 |
| **Source** | Task 024. Introduced by task 020, which deliberately declined it; 024's paging made it worse. |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/968 |

**Resolution**

`SpeContainerMembershipService.RemoveMembershipsAsync(containerId, emails, ct)` — ONE paged read, then
match every email locally and delete by permission id. Returns one `SpeContainerMembershipResult` per
distinct email, keyed case-insensitively, so `RevokeExternalAccessEndpoint` classifies each member with
the **same** logic it already used and the blast radius stays at the loop.

The endpoint now resolves every member's email FIRST, then makes a single call. Read count is **1 per
revoke** instead of 1 per member — and, since task 024 made reads paged, it no longer multiplies by
page count either.

Preserved deliberately: per-member failure never aborts the sweep (tasks 016/017 — stopping early
leaves strictly MORE access in place); a read failure marks EVERY member failed rather than absent; an
incomplete enumeration flows through `ClassifyRevokeResult`, so an unseen member is never reported
"genuinely absent"; `RevokeMembershipAsync` is untouched because the per-CONTACT revoke still uses it.

⚠️ **One behaviour change worth knowing**: results are keyed by EMAIL, so two contact rows sharing one
address both report the outcome of the single permission that address owns. Previously the second call
found it already deleted and reported `NoPermissionFound` — which read as "this person never had
access". Keying by identity is the more truthful of the two.

**Perturbation**: re-reading the container inside the per-member loop (i.e. restoring the N+1) fails
**3** tests, one of which asserts the read count directly.

**Description**

`RevokeExternalAccessEndpoint`'s organization sweep calls `RevokeMembershipAsync` once per member, and
each call reads the container's **entire** permission collection. N members = N full reads.

**Concrete failure mode**: task 024 made each of those reads **paged**, so the cost went from N reads to
**N × pages**. Negligible at today's 1 member/org; real at the 200-member `MaxMembersPerSweep` bound.

🔴 **This is a COST defect, not a correctness one.** Each member's read is individually paged and
individually honest, and an incomplete enumeration correctly yields `Failed` for that member. The answer
is right; the number of round trips producing it is not.

**Entry-points**

- `RevokeExternalAccessEndpoint.RemoveOrganizationSpePermissionsAsync` — the sweep loop
- `SpeContainerMembershipService.ReadPermissionsAsync` / `PermissionReadResult` (new in 024) — the primitive to reuse

**Suggested fix**: ONE paged read → match all member emails locally → delete by permission id.
⚠️ **Do NOT use `RemoveAllExternalMembersAsync` as the paged primitive** — it removes EVERY external
member, not just the target org's (task 020's warning, still binding). A new service method is needed:
the endpoint reports per-member arithmetic (`SpeOrgMemberCleanupSummary`) that `SpeBulkRemovalResult`
cannot express.

**Estimated effort**: small–medium.
**Blockers**: none — deferred on regression risk, not on a dependency.
**Related**: task 020 (introduced) · task 024 (amplified, filed) · design §2.1 row 3.

---

### ISS-005 — `BulkUpdateAsync` claims transactional behaviour that `ExecuteMultiple` does not provide

| Field | Value |
|---|---|
| **Status** | ✅ **Closed 2026-09-10** — fixed by task 096 (`3570d24e4` + review follow-up); #970 closed |
| **Urgency** | next-round |
| **Filed** | 2026-09-10 |
| **Source** | FR-33 redesign (record-level expiry). Pre-existing; found while choosing an atomic write for it. |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/970 |

**Description**

`DataverseServiceClientImpl.BulkUpdateAsync` (`:2324-2329`) uses `ExecuteMultipleRequest` with
`ContinueOnError = false` and the comment *"Stop on first error for transactional behavior"*.
**`ExecuteMultiple` is not transactional** — stopping at the first failure leaves every earlier request
committed, with no rollback. The all-or-nothing mechanism is `ExecuteTransactionRequest` (or a Web API
`$batch` changeset).

**Concrete failure mode**: the helper sits on the shared `IGenericEntityService` interface and its name
plus comment make it look like the tool for "update these N rows together". This project nearly used it
to set `sprk_expiresdate` across every grant on a record — where a partial failure while **shortening**
an expiry leaves some grants at the later date: **fail-open on an access control**, behind a comment
saying it cannot happen. Today's two callers (`CommunicationAccountService`, `WorkspaceLayoutService`)
are low-stakes; the risk is the next reuse.

**Entry-points**

- `src/server/shared/Spaarke.Dataverse/DataverseServiceClientImpl.cs:2324` — the helper
- `IGenericEntityService.cs:41` — the shared interface that exposes it

**Suggested fix**: switch to `ExecuteTransactionRequest` (preferred — both callers very likely want
all-or-nothing), or delete "transactional" from the comment and rename so it no longer reads as atomic.

**Estimated effort**: small.
**Blockers**: none.

🔴 **REVISED 2026-09-10 — this is no longer "filed for someone else".** The owner challenged the original
plan (FR-33 building its own atomic changeset beside this helper) as a §11 violation, and was right. The
fix is now **FR-33's first task**: switch `BulkUpdateAsync` to `ExecuteTransactionRequest` once,
system-wide, and have FR-33 **reuse** it via the `IDataverseService` ExternalAccess already injects. Both
existing callers want all-or-nothing, so the change helps them too. Runs `/conflict-check` first —
`Spaarke.Dataverse` is a shared hot-path surface. See `notes/decisions/external-grant-expiry-mandatory.md` §12.2.
**Related**: FR-33 redesign — `notes/decisions/external-grant-expiry-mandatory.md` §11.

✅ **RESOLVED 2026-09-10 by task 096** (`3570d24e4` + its review follow-up). `BulkUpdateAsync` now sends ONE
`ExecuteTransactionRequest`; signature unchanged; the failure message states only what is known. The review
surfaced two adjacent defects, filed below as **ISS-006 (#971)** and **ISS-007 (#972)**. Details:
`notes/task-096-bulkupdate-transactional.md`.

---

### ISS-006 — Singleton `ServiceClient` throws the client-wide `LastException` (concurrent requests can surface each other's errors)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-09-10 |
| **Source** | Task 096 code review; verified against the `Microsoft.PowerPlatform.Dataverse.Client` 1.1.32 source. Not reproduced live. |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/971 |

**Description**

`ServiceClient`'s async request paths end with `if (resp == null) throw LastException;`. `LastException`
is a plain property on the client's single logger (not thread- or async-local), set by any failed call and
cleared at the start of every call. The BFF registers `IDataverseService` as a **singleton**
(`Infrastructure/DI/GraphModule.cs:47`), so a failed call can throw a **different concurrent request's
exception**, or `throw null`.

**Concrete failure mode**: wherever `DataverseServiceClientImpl` branches on exception text, a foreign
exception changes behaviour. `AssociateAsync` treats "duplicate" / "already exists" as **idempotent
success** — another request's duplicate error can turn a genuine association failure into a silent success.

**Entry-points**

- Every `DataverseServiceClientImpl` method; most acute at `AssociateAsync` (the duplicate/already-exists
  filter) and `IsAlternateKeyDuplicate`.

**Suggested fix**: check whether a later SDK release stores the failure per call (then bump); otherwise use
a per-call client for paths that branch on exception content, or stop branching on exception text.

**Estimated effort**: medium. **Blockers**: none.
**Related**: ISS-005 / task 096 — worked around for `BulkUpdateAsync` only (it claims "no update was
applied" only for an `ExecuteTransactionFault` naming an in-range request).

---

### ISS-007 — `WorkspaceLayoutService` writes a new default layout even when clearing the old defaults failed

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | low |
| **Filed** | 2026-09-10 |
| **Source** | Task 096 code review. |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/972 |

**Description**

`ClearUserDefaultsAsync` (`Services/Workspace/WorkspaceLayoutService.cs:944-953`) catches any exception
from `BulkUpdateAsync`, logs it, and proceeds to write the new default. Since task 096 the clear is
all-or-nothing, so a failure leaves **every** old default set — and the new default is written anyway:
the user still has two defaults. Atomicity fixed the helper, not this caller's policy. (The FR-33 decision
note §12.2 had cited "two defaults" as something the fix would resolve — corrected there.)

**Suggested fix**: do not write the new default when the clear fails (propagate the error), or perform the
clear and the new default in one transaction.

**Estimated effort**: small. **Blockers**: none. **Related**: ISS-005 / task 096.

---

### ISS-008 — Re-grant over an EXPIRED survivor returns 409 without looking at live duplicates on the same key

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-09-10 |
| **Source** | Task 097 code review. Pre-existing (task 023). |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/973 |

**Description**

In `GrantExternalAccessEndpoint.CreateGrantAsync`'s match path, when the elected survivor (lowest id) is
**expired** and the request carries no new expiry, it returns the task-023 warning (`/grant` → 409
`sdap.grant.expired_not_restored`, "still confers no access") **before** `CollapseDuplicatesAsync`. A duplicate
active row on the same key with a later or null expiry is still live, so the grantee **does** have access
while the 409 says not. Pre-existing duplicates are real in dev (task 097 notes: one contact, five rows, one
matter).

**Suggested fix**: judge "confers access" across all active rows on the key, or elect the survivor with the
latest effective expiry. NOT "collapse first" — collapsing onto an expired survivor would revoke live access.

**Estimated effort**: small–medium. **Blockers**: none. **Related**: task 023, task 010, task 097.

---

### ISS-009 — `sprk_externalrecordaccess` rows created outside the BFF can still have no expiry

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-09-10 |
| **Source** | Task 097 code review. |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/974 |

**Description**

Since task 097 the BFF never writes a grant without `sprk_expiresdate`, but the column is optional in
Dataverse and users hold Create on the table (the `TrackingFieldTrio` PCF checks for it). A row created
through a form, the Web API directly, a flow or an import can still be unbounded — and the read filter
treats null as never-expiring. FR-33 therefore holds for BFF writes only.

**Suggested fix**: a Dataverse-side guard — make the column required (after the dev backfill no active row is
null) or default it on create via a plugin / business rule. Schema changes are code + docs; the live change
is an operator step (owner directive 2026-09-04). Once no null can arrive from any path, the read filter's
`eq null` clause could flip to fail-closed — still the owner's call (097 escalation trigger 2).

**Estimated effort**: small (+ operator step). **Blockers**: none. **Related**: task 097, FR-33.

---

### ISS-010 — H9 provisioning deploy does not set the scheduled-jobs slot guard

| Field | Value |
|---|---|
| **Status** | Fixed on the branch (`6149edecf`) — closes when PR #950 merges |
| **Urgency** | now |
| **Filed** | 2026-09-14 (entered here 2026-09-15) |
| **Source** | Task 103 Step 9.5 code review |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/987 |

H9 Kudu-deployed to the staging slot without `Scheduling__RunScheduledJobs=false`, so the staging slot ran production's cron jobs. Owner (2026-09-15): *"do not defer"* — fixed here: H9 sets the guard slot-sticky, fail-closed, before the deploy; `SchedulingSlotGuardKeyTests` pins the key across all four slot-deploy paths. **Before merge**: verified against a fake ARM transport only — needs one real-ARM check.

---

### ISS-011 — The Service Bus namespace still accepts SAS keys

| Field | Value |
|---|---|
| **Status** | Open — handed off (not material); **no confirmed owner** |
| **Urgency** | next-round |
| **Filed** | 2026-09-15 |
| **Source** | Function-impersonation research (session 11); prerequisite P1 of ADR-052 §6 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/988 |

A namespace-wide Send+Listen rule (`SpaarkeAppAccess`) and a connection-string fallback in `ServiceBusClientFactory`: anyone holding the string can enqueue any job. **Why not material here** (verified 2026-09-15): every job handler uses `JobContract.SubjectId` only for logs and telemetry — none acts as that user — so a forged message cannot change an access decision in this project's evaluator. A standing platform risk that needs an owner; ADR-052 §6 blocks Function impersonation until it lands. Full body on the issue.

---

### ISS-012 — `JobContract` has no typed, BFF-set requester field

| Field | Value |
|---|---|
| **Status** | Open — handed off (not material) |
| **Urgency** | someday (until a Function needs to impersonate) |
| **Filed** | 2026-09-15 |
| **Source** | Prerequisite P2 of ADR-052 §6 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/989 |

A handler that impersonates the requester needs the JWT-validated `oid` + `tid`, written only by the BFF. **Why not material here**: needed only by an Azure Function that impersonates; none is in this project's scope. ADR-052 §6 makes it a prerequisite for the first project that builds one.

---

### ISS-013 — `DataverseImpersonation` degrades to app-only on an empty id

| Field | Value |
|---|---|
| **Status** | ✅ **Implemented by task 104** on the branch (2026-09-15): `6be320e82` + `134bdb73e` (Step 9.5 fixes) + `43ac00a18` (ADR-028 A5). #990 closes when PR #950 merges. Residuals are recorded on #990 and in `notes/task-104-impersonation-helper-fail-closed.md` §5 |
| **Urgency** | now |
| **Filed** | 2026-09-15 |
| **Source** | Prerequisite P3 of ADR-052 §6; ADR-028 A5 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/990 |

`Apply(null / Empty)` adds no header and returns, so a call site that bypasses `RetrieveMultipleImpersonatedAsync` silently runs an app-only, unscoped query. Material: the evaluator's impersonated root sets (task 036) rely on this helper. Entry-points and criteria: `tasks/104-iss013-impersonation-helper-fail-closed.poml`.

---

### ISS-014 — `@tiptap/core` 2.x carries GHSA-cp6q-959q-f8rh (fixed only in 3.30.4)

| Field | Value |
|---|---|
| **Status** | Open — handed off to Compose (ownership confirmed) |
| **Urgency** | next-round |
| **Filed** | 2026-09-15 |
| **Source** | Trivy code scanning on PR #950 (the LegalWorkspace build repair, `9edbb011c`, declared the TipTap packages) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/991 |

MEDIUM: `mergeAttributes()` turns an own `__proto__` key into inherited executable DOM attributes. 2.27.2 on master (`Spaarke.Compose.Components`, `SpaarkeAi`), 2.27.3 on this branch (`LegalWorkspace`). **Owner confirmed**: LegalWorkspace gets TipTap only by transpiling Compose (`composeEditor.registration.ts`); Compose owns the editor (ADR-049). The fix is a TipTap 3 migration across all three packages together.

---

### ISS-015 — `@xmldom/xmldom` 0.8.13 and `dompurify` 3.4.7 in front-end lockfiles

| Field | Value |
|---|---|
| **Status** | Open — handed off (not material) |
| **Urgency** | next-round (8 HIGH) |
| **Filed** | 2026-09-15 |
| **Source** | Trivy code scanning on PR #950 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/992 |

8 HIGH + 2 MEDIUM on xmldom (via `mammoth`; fixed 0.8.15), 2 MEDIUM + 3 LOW on dompurify (fixed 3.4.13). **Why not material here**: the same versions are on master in the same lockfile — PR #950 only edited it; `mammoth` is declared by six packages repo-wide. First question for the owner: is `mammoth` still needed at all (Compose R4.5 recorded it deleted)?

---

### ISS-016 — `DataverseWebApiClient` receives the container's singleton `TokenCredential`

| Field | Value |
|---|---|
| **Status** | Open — handed off (not material); **no confirmed owner** |
| **Urgency** | someday |
| **Filed** | 2026-09-15 |
| **Source** | Task 104: its Step 9.5 review of the sibling `DataverseWebApiService` test seam |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/993 |

`DataverseWebApiClient` has a public optional `TokenCredential? credential = null` constructor parameter that bypasses credential selection. It is registered as `AddSingleton<DataverseWebApiClient>()` (`SpeAdminModule.cs:71`), and the container holds a singleton `TokenCredential` (`Program.cs:48`). DI fills optional parameters from registered services, so the client always gets that credential.

**Why not material here**: it is SpeAdmin's audit and dashboard REST client, not an access-control path. The injected credential (a `DefaultAzureCredential` pinned to the configured managed identity and tenant) is equivalent in deployed environments, where managed identity is on. It differs only where managed identity is off (local/dev).

**Suggested fix**: register it through a factory lambda, or make the credential constructor protected, as task 104 did for `DataverseWebApiService`.

---

### ISS-017 — `PlaybookSharingService` reads AccessRights bit 524288 as Share

| Field | Value |
|---|---|
| **Status** | Open — handed off (not material) |
| **Urgency** | someday |
| **Filed** | 2026-09-15 |
| **Source** | Task 063: establishing the Dataverse AccessRights values for the FR-29 internal share endpoints |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/994 |

`MapFromDataverseAccessRights` (`Services/Ai/PlaybookSharingService.cs:461`) maps POA `accessrightsmask` bit **524288** to `PlaybookAccessRights.Share`, commented `// ShareAccess`. That bit is **AssignAccess**; `ShareAccess` is **262144**. Values read by reflection from `Microsoft.Crm.Sdk.Proxy` 1.2.26: Read 1, Write 2, Append 4, AppendTo 16, Create 32, Delete 65536, Share 262144, Assign 524288.

Wrong in both directions: a playbook shared with Assign is displayed as re-shareable, and a genuine Share right is not displayed at all.

**Why not material here**: the projection is display-only and lives in `Services/Ai/**`, the AI line's surface (`projects/INDEX.md`); no access decision in this project's evaluator reads it. This project's own level→rights table (`Services/Access/RecordShareLevels.cs`, task 063) holds the correct values and pins them in tests as literals.

**Suggested fix**: `262144` for Share; add `524288` as Assign only if that right is meant to be surfaced at all.

---

### ISS-018 — unsecure-project cannot see a failed share read, and reports "0 shares revoked" as success

| Field | Value |
|---|---|
| **Status** | Open — **in project**; task 108 |
| **Urgency** | next-round |
| **Filed** | 2026-09-15 |
| **Source** | Task 063: adding the strict share read |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/995 |

`UnsecureProjectEndpoint.RevokeAllSharesAsync` (`:301-340`) reads the project's POA shares inside a `try/catch` whose `catch` logs *"Could not read the shares on project {ProjectId}; none were revoked"*. **That catch cannot fire for the failure it names**: `GetPrincipalAccessAsync` fails SOFT — a non-success status, or an unreadable object type code, returns an EMPTY LIST. So a failed read is indistinguishable from "this project has no shares": nothing is revoked, the endpoint answers success with `sharesRevoked: 0`, and the warning is never written. Unsecuring can leave every POA share in place, silently. Ownership has already moved by then, so the record is reachable regardless — but the stale access path the code's own comment promises to report is not reported.

**Why it stays in the project** (owner rule 2026-09-15): this is the project's own code (task 061), on an access-control path.

**Fix**: the strict read task 063 added (`GetPrincipalAccessOrThrowAsync`), so the existing `catch` starts working — plus a report the caller can see, not only a log line. Two behaviour changes need review, which is why 063 did not apply it as a drive-by: the strict read also refuses a record whose shares span more than one page (>5000 rows) or that carries an unreadable row, where the soft read would have revoked the rows it could see. For a "revoke everything" sweep, partial progress may be preferable to none. Task **108** carries that decision and an escalation trigger for it.

---

### ISS-019 — a junction *query* failure silently removes the deny veto's ORGANISATION axis

| Field | Value |
|---|---|
| **Status** | Open — **in project** |
| **Urgency** | next-round |
| **Filed** | 2026-09-17 |
| **Source** | Task 043 Step 9.5 code-review finding H-2, while hoisting the junction read for the org-expansion term |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/998 |

`ExternalParticipationService.QueryActiveOrgIdsAsync`'s **private** overload catches query faults and
non-success statuses and returns an **empty list** (`:1078-1084`, `:1093-1097`). Only the public
overload's token/API-url acquisition can throw. So an HTTP 500/403 or a timeout on the
`sprk_contactorganization` query reaches `AccessibleRecordSetService.ReadActiveOrgMembershipsAsync` as a
*successful* read of zero organisations — `Unreadable: false` — which the FR-23 deny veto cannot
distinguish from "this subject belongs to no organisation". Every deny row keyed on an organisation the
subject belongs to stops matching. Contact-keyed rows (contact×record, contact×org) still apply, so the
ethical wall **narrows rather than vanishes** — but the narrowing is silent and is exactly the case the
veto's own documentation claims to cover.

**The hole pre-dates task 043.** What 043 briefly did was worse than leaving it alone: it documented and
unit-tested the property as *held*, using a test double that throws where the real path does not. A
confident comment plus a green test is how a gap stops being looked for. Both `ActiveOrgMemberships`'
and `ResolveDenyVetoAsync`'s remarks were corrected in `62e5d5d0c` to state the exact scope and name
this residual.

**Why it was not fixed in 043**: the correct fix is one layer down — surface an outcome from the junction
query itself — and that same query also serves the **additive** org-grant term, whose caller
*deliberately* wants empty-on-fault so a read failure cannot GRANT access. That is the same
inverted-fail-direction problem 043 solved at the evaluator, recursing into
`ExternalParticipationService`. It needs its own change with its own tests, not a drive-by.

**Suggested fix**: a `QueryActiveOrgIdsOutcomeAsync` (ids + `Unreadable`) consumed by the veto path,
leaving the additive callers on the existing empty-on-fault overload.

---

### ISS-020 — 🔔 the org-membership read bounds on `statecode` only, so a date-ended membership still confers

| Field | Value |
|---|---|
| **Status** | Open — **in project**; 🔔 **question formally put to the owner 2026-09-18, still UNDECIDED** |
| **Urgency** | next-round |
| **Filed** | 2026-09-17 |
| **Source** | Filed onto task 043 by task 020; decided-and-recorded in 043 (notes §4.1), sharpened by 043's code-review finding H-3 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/999 |

The `sprk_contactorganization` junction carries `sprk_enddate`, but `QueryActiveOrgIdsAsync` filters
`statecode eq 0` alone (`:1067-1069`). **A membership ended by date but never deactivated still confers
inherited access** — via the org-grant term today, and via task 043's org-expansion term from now on.

Task 020 deliberately declined to change it and filed the decision here with the instruction *"do not
inherit the revoke's shape by default, because its fail direction is inverted relative to yours"*.
Task 043's decision: **leave the query shape unchanged**, because one shape cannot be right for both
callers — `QueryActiveOrgIdsAsync` serves an **additive** term (over-inclusion = over-GRANT, so a date
bound is a tightening) *and* the **deny-veto subject** (over-inclusion = a stricter wall, so the same
date bound makes the wall match FEWER subjects — a fail-OPEN change to a veto).

**Why this needs the owner and not another implementer decision**: the hedge that made 043 safe is a
**data state, not a control** — exposure is nil only because **zero organisations hold a standing grant
today** (task 042 §3). One administrator ticking `sprk_standinggrant` on one organisation arms an
over-grant to every stale member of that firm. And fixing it properly *changes who has access today*
(it narrows live org-grant inheritance), which is beyond any single task's remit.

**Suggested fix**: a date-bounded variant for the additive callers only, leaving the veto subject on
`statecode`-only; plus a decision on whether to backfill/deactivate date-ended junction rows.

---

### ISS-021 — the org-baseline read is an unbounded N+1 on the authorization hot path

| Field | Value |
|---|---|
| **Status** | Open — **in project** |
| **Urgency** | someday |
| **Filed** | 2026-09-17 |
| **Source** | Task 043 Step 9.5 code-review finding M-3 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1000 |

`AccessibleRecordSetService.ComposeForContactAsync` awaits one
`ISubjectStandingGrantReader.ReadForOrganizationAsync` per distinct active organisation, serially, and
`QueryActiveOrgIdsAsync` applies no `$top` — so N is unbounded by construction. `IsRecordAccessibleAsync`
and `IsOperationPermittedAsync` call `ComposeAsync` once per authorization check with no
composition-level cache, so the cost multiplies per decision.

**Not urgent, and the reason is measurable**: zero organisations hold a standing grant, so N is currently
0 and the loop never executes. The shape is still wrong for a hot path.

**Suggested fix**: a batched org-baseline read (one query for N organisation ids), or an explicit bound
with `Capped` surfaced per NFR-03 — the same treatment the membership walk already gets.

---

### ISS-022 — a stale snapshot of the grant rows can SHORTEN access on the collapse

| Field | Value |
|---|---|
| **Status** | Open — **in project** |
| **Urgency** | next-round |
| **Filed** | 2026-09-18 |
| **Source** | Task 106 Step 9.5 code-review finding F5 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1001 |

`GrantExternalAccessEndpoint.CreateGrantAsync` reads the key's active rows, elects a survivor, then
deactivates the rest — with **no ETag or conditional update** between the read and the write. Task 106
changed the election key from an **immutable** `sprk_externalrecordaccessid` to a **mutable**
`sprk_expiresdate` read at T₀, which makes one new outcome reachable.

Rows r₁ (expires today+10, lower id) and r₂ (today+200). A date-less grant reads both and elects r₂.
Concurrently `/set-record-share-expiry` moves r₂ to today+1. A then collapses onto r₂ — now the shorter
row — and deactivates r₁. The union conferred access until +10 before the call and until +1 after:
**shortened by a stale read**. Pre-106 the same interleaving elected r₁ and kept +10.

**Honest scope split** (the reviewer's, and it is the reason this is `next-round` rather than urgent):
only the *shortening* shape is new. The **revoke-race** variant — a racer deactivates the elected row,
then this collapse removes the others, leaving zero active rows — is **pre-existing and symmetric**, and
pre-106 it fired whenever the revoked row happened to be the lowest id. **V1's fix does not address
either**: V1 removed request-dependence from the ranking; this is about the ranked column being mutable.

**Suggested fix**: a conditional update on the deactivation (`If-Match` on the row's ETag) so a collapse
cannot apply to a row whose state changed since the read — the read-then-write pair currently has no
optimistic concurrency at all. A re-read immediately before the collapse narrows but does not close it.

**Estimated effort**: medium. **Blockers**: none. **Related**: tasks 010, 023, 098, 106.

---

### ISS-023 — 🔔 a duplicate collapse can lower the effective access LEVEL

| Field | Value |
|---|---|
| **Status** | Open — **in project**; ✅ **owner decision received 2026-09-18** (below) |
| **Urgency** | next-round |
| **Filed** | 2026-09-18 |
| **Source** | Task 106 Step 9.5 — code-review finding F10, adr-check W3 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1002 |

The read path composes effective access as `GroupBy(root).Max(level)` over active, unexpired rows
(`ExternalParticipationService`, `DedupeByHighestLevel`). The write path's `CollapseDuplicatesAsync`
writes the **requested** level to the survivor and deactivates every other row. So a re-grant at
ViewOnly over a key that holds a FullAccess duplicate reduces effective access from FullAccess to
ViewOnly — on a request whose caller may have meant only to renew.

**This is PRE-EXISTING (task 010's convergence semantics), and task 106 does not make it worse.** The
review verified the stronger statement: the final level is **invariant** under the election change,
because `requestedLevel` is written to whichever row is elected — so 106 cannot alter level outcomes in
either direction. It is filed now because task 106's own argument, that a deduplication must not decide
**how long** access lasts, applies verbatim to **how much** access it confers; 106 made "rank on
duration, ignore level" an explicit ranking choice for the first time, so the asymmetry deserves a
register entry rather than a paragraph in a task note.

### ✅ Owner decision (2026-09-18)

**"When access is renewed the user should have the same level as previous access period."**

So a **renewal must not lower the effective level**. The prior period's level carries forward, rather
than the requested level overwriting it.

**The open design question this leaves** — to be surfaced at implementation, not guessed: the request
**always** carries an `AccessLevel`, so a *renewal* has to be distinguished from an *explicit level
change*. An explicit downgrade MUST remain possible: task 063's `share-user` path exists to set a
system user to View Only, and the real-Dataverse DOWNGRADE check before merge of PR #950 is specifically
about that working. So the rule cannot be "always take `max(requested, existing)`" — that would make
downgrades impossible. Candidate discriminators (an explicit intent flag, a dedicated renew route, or
treating "same level as one of the existing rows" as a renewal) are the task's first decision, and it
carries an escalation trigger for it.

**Estimated effort**: small once decided. **Blockers**: the owner's product answer. **Related**: tasks
010, 106.

---

### ISS-024 — ✅ an external *licensed* system user is refused as "not internal"

| Field | Value |
|---|---|
| **Status** | Open — **in project**; ✅ **owner decision received 2026-09-18** |
| **Urgency** | next-round |
| **Filed** | 2026-09-18 (the finding itself dates from task 063, session 13) |
| **Source** | Task 063; raised as an open owner question in every handoff since |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1003 |

`POST /api/v1/external-access/share-user` refuses a system user flagged as external with HTTP 422
`sdap.access.user_share.user_not_internal` (`InternalShareEndpoints.cs:80`), and
`SystemUserIdentityResolver.cs:57` carries a doc comment justifying the refusal.

### ✅ Owner decision (2026-09-18)

**"For task 063 an 'external' licensed user is treated the same as an internal licensed user."**

So **licence, not the external flag, is the discriminator.** A licensed system user is a Dataverse
security principal and can hold a POA share; whether the directory marks them external is irrelevant to
that. The refusal should go.

### ⚠️ Why this is riskier than it looks

The old rule is **asserted by a contract test**:
`tests/integration/contract/Api/ExternalAccess/InternalUserShareContractTests.cs:94` asserts the 422 and
its reason code. So the test encodes the retired rule and **must change in the same commit as the code** —
a green suite after changing only the endpoint would mean the test was never exercising the path, and a
green suite after changing only the test would mean nothing at all.

**What must NOT change**: a *contact* is still not a security principal (project CLAUDE.md fact 4) and
still cannot be a POA share target. This decision is about **licensed system users** only, and the
contact plane must keep computing access rather than storing it. Whoever implements this should confirm
what `isExternal` on a systemuser actually denotes in live metadata before deleting the branch that reads
it — the guard may be load-bearing for some *other* distinction.

**Estimated effort**: small. **Blockers**: none. **Related**: tasks 063, 065 (the "+ User" picker),
069 (Phase-4 seam tests).

---

### ISS-025 — the status-drift check cannot see an index row that has no POML

| Field | Value |
|---|---|
| **Status** | Open — **in project** |
| **Urgency** | next-round |
| **Filed** | 2026-09-18 |
| **Source** | Found by accident while queuing tasks 109–114: I created the condition and the check passed |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/1004 |

`scripts/check-task-status-drift.ps1` reconciles by iterating the **POMLs**
(`foreach ($id in ($poml.Keys | Sort-Object))`) and looking up each one's index marker. An index row with
**no corresponding POML is therefore never examined.**

Demonstrated, not theorised: after adding six `🔲 [open]` rows (109–114) with no task files, the script
printed **`task POMLs parsed : 105`, `index rows parsed : 111`** and then **"No drift: every task POML
status agrees with its index marker."** `rc=0`. Both numbers were on screen and the verdict ignored the
difference.

**Why it matters.** This script exists because completion is recorded in two places and nothing enforced
agreement — a 2026-09-03 audit found 17 disagreements across 92 tasks. It closes the *POML-stale* and
*index-stale* directions for paired tasks, but **an orphan index row is a third direction it does not
cover**: the index can advertise work that no task file backs, `task-execute <id>` then fails to find a
file, and `push-to-github` Step 1.65 passes on the way there. It already guards the inverse case (POMLs
present but zero index rows parsed, i.e. a format change) — this is the mirror of that guard.

**Suggested fix**: after the POML loop, assert the row set and the POML set are equal, and report
unpaired **index** rows as drift with their ids. Two or three lines. Keep the existing message shape so
the failure is legible.

⚠️ **Note the verdict was not wrong, it was narrow** — true about what it measured and silent about what
had changed. Same class as this project's recurring root error: *an observation taken outside the thing
being observed.* The 105/111 mismatch was printed and I nearly accepted the green over it.

**Estimated effort**: small. **Blockers**: none. **Related**: task 106 Step 10; the 2026-09-03 drift audit.

---

## Closed

*(none yet)*
