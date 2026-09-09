# Defer / Issue Tracking — unified-access-control-r2

> **Source of truth** for deferred work + newly-discovered issues in this project.
> Each entry has a paired GitHub Issue. See `/project-defer-issue-tracking` skill for the protocol.
>
> **Rollup view**: ~~`gh issue list --label unified-access-control-r2`~~ ⚠️ **That label does not
> exist in this repo**, so the command silently returns nothing — and every issue this file has filed
> (#961, #962, #963) was created unlabelled, so it would find none of them even if it did. Use the
> explicit list instead: `gh issue view 961 962 963`. Creating the label and back-applying it is a
> two-minute fix nobody has done; recorded 2026-09-09 by task 029 rather than left as a command that
> looks like it works.
> **CLAUDE.md §11 rule**: every entry MUST name a concrete behavior or contract that fails without it.

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

## Closed

*(none yet)*
