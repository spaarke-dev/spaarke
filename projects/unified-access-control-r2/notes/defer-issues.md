# Defer / Issue Tracking — unified-access-control-r2

> **Source of truth** for deferred work + newly-discovered issues in this project.
> Each entry has a paired GitHub Issue. See `/project-defer-issue-tracking` skill for the protocol.
>
> **Rollup view**: `gh issue list --label unified-access-control-r2`
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

---

## Closed

*(none yet)*
