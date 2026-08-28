# Issue #863 — session ownership: what was actually wrong, and what is now in the tree

> **Status**: production code COMPLETE and building · guard test GREEN and proven non-vacuous ·
> **11 test failures remain**, all named below · **one open decision needs the owner** (§5).
> **Date**: 2026-08-28 · **Project**: `spaarkeai-compose-r8` · **Origin**: task 059 §6a.

---

## 1. The recorded severity was wrong, in the same direction as 059's was

`notes/059-tenant-header-decisions.md` §6a recorded the cross-user DELETE gap and bounded its risk
this way:

> *"Residual risk is bounded by session IDs being `Guid.NewGuid().ToString("N")` — 128-bit random,
> not enumerable — so exploitation requires a leaked session ID."*

**That did not hold.** `GET /api/ai/chat/sessions` — the History dropdown — ran:

```sql
SELECT TOP @limit c.id, c.sessionId, c.lastActivity, c.conversationSummary,
       c.entityRefs, c.messages[0].content AS firstMessage, c.title,
       ARRAY_LENGTH(c.messages) AS messageCount, c.tabs
FROM c WHERE c.tenantId = @tenantId ORDER BY c.lastActivity DESC
```

No user predicate. So every authenticated user was **handed** every other user's session ids in
their own tenant, together with each session's **title** and a **content preview**
(`conversationSummary ?? firstMessage`). The ids were not leaked-by-accident; they were published by
design, to everyone, on the History surface.

So the gap was not "delete needs a leaked id". It was:

1. a **disclosure** — titles and content previews of colleagues' conversations, listed on open; and
2. a **delivery mechanism** — that same list supplied the ids for the unchecked
   `DELETE /api/ai/chat/sessions/{id}`, and for read, rename, context-switch and post-message on
   ~28 further session-scoped routes.

This is the second time in this project that the *filed* defect was the milder one and the unfiled
neighbour was live (059 §3 was the first). The pattern is the same both times: the noticed mechanism
was the one with a name.

## 2. The comment that kept anyone from looking

`Api/Filters/AiAuthorizationFilter.cs`, on the branch taken by every session-scoped route:

> *"If no document IDs are present (e.g. session-scoped endpoints where the session ID acts as the
> authorization scope), pass through to the next filter — **the endpoint handler performs its own
> tenant/session ownership checks**."*

The handlers checked **tenant**. No handler anywhere checked ownership, because no session had an
owner to check. The sentence describes a division of responsibility that does not exist, and it sits
at exactly the point a reader would go looking. Corrected in place, with the history, rather than
deleted — this is the **seventh** stale comment found in this project and every one of them was
load-bearing for someone's decision not to look closer.

## 3. What was implemented

**Schema.** `ChatSession.OwnerOid` (init property — the file's established additive convention, so
every existing positional call site keeps compiling) and `StoredSession.OwnerOid`
(`[JsonPropertyName("ownerOid")]`). Redis rides `ChatSession`'s JSON, so the hot tier needs no
change. Both directions of the Cosmos mapping updated — dropping either half would make a
Redis-evicted session inaccessible **to its own owner**, which is an outage that gets blamed on the
cache rather than on a mapping.

**Population.** `CreateSessionAsync` takes `ownerOid` as a **required positional** parameter, second,
with `ArgumentException.ThrowIfNullOrWhiteSpace`. Deliberately not an optional trailing parameter: an
owner a call site can forget is the same defect shape as the 21 duplicated tenant-resolution sites
059 deleted. All five production call sites broke loudly at compile time, which is the point.

**Identity.** `CallerResolution.ResolveObjectId` — the Entra `oid`, never `sub` (pairwise, joins
nothing) and never a Dataverse `systemuserid`. `AgentEndpoints` needed care: its `ExtractUserId()`
returns the literal `"unknown"` when the oid is absent, which is fine for a log line and catastrophic
as an ownership key, because every unidentified caller would then own — and therefore share — one
bucket of sessions.

**Enforcement.** `AddSessionOwnershipFilter()` (ADR-008 endpoint filter) on all **28** routes whose
template carries `{sessionId}`. Not a per-handler check: ~15 routes and 44 `GetSessionAsync` call
sites is precisely the surface that decays, because the twenty-ninth route gets written by copying
the twenty-eighth.

**404, never 403.** Missing, unowned and owned-by-another are one answer. 403 would confirm that a
given session id is real, turning any id a caller can guess or overhear into an existence oracle —
and existence alone leaks that a colleague has a conversation about a given matter.

**Body-scoped routes** — three routes take the session id in the payload, so a route-value filter
cannot see them. Each checks ownership in its handler and is **enumerated** in
`SessionOwnershipGuardTests.BodyScopedSessionRoutes` so the exception set stays closed:
`ComposeService.LoadAsync` (resume hint), `AnalysisEndpoints` fork (copies the prior's messages),
`AgentEndpoints` (`ConversationReference`). Adding an `ownerOid` FIELD to any of those requests would
have recreated exactly the defect 059 removed — a caller naming its own identity.

**The list.** `ListRecentSessionsAsync(tenantId, ownerOid, limit, ct)` — `ownerOid` required —
filtering on `c.ownerOid = @ownerOid`.

## 4. The migration decision (made, and it has a cost)

Sessions created before this field have `OwnerOid == null`. They **fail closed**: invisible to
History, inaccessible on every session-scoped route, matching no caller.

The alternative — `OR NOT IS_DEFINED(c.ownerOid)` — would have preserved the disclosure on precisely
the sessions most likely to still be live, and on the largest population. It is refused explicitly,
in a comment at the query and in an assertion in `Rule2_TheHistoryListIsFilteredByOwner`, because it
is the shortcut that looks like a kindness.

**What it costs, stated plainly**: conversations that predate deploy stop being resumable in the UI.
Bounded by the Redis hot copy's 24h sliding TTL, and nothing is destroyed — the Dataverse transcript
is retained as an audit trail regardless (archive-not-delete). **A user with an in-flight Compose
document on a pre-deploy session will lose the ability to resume that session** and will get a fresh
one; `LoadAsync` degrades to minting rather than erroring, so they get a working document, not a
failure.

## 5. 🔔 OPEN — needs the owner before merge

**The filter's 404 body replaces documented per-route error codes.** `ComposeDocSessionDispatchSeamTests`
caught it: the dispatch route answers a *stable* ADR-019 `errorCode` of `dispatch.session-not-found`,
and the ownership filter now returns its own generic `{ "error": "Session … not found" }` **before**
the handler runs. So a client matching on `dispatch.session-not-found` stops seeing it.

This is a wire-contract change, not a test to fix, and the filter cannot know each route's code.
Three options:

| | Approach | Cost |
|---|---|---|
| **A** | Filter emits ProblemDetails with ONE stable code (`session.not-found-or-not-owned`) | Clients matching per-route codes must be updated; one honest code thereafter |
| **B** | Filter re-uses each route's documented code via endpoint metadata | More machinery; the code has to be declared at registration, which is another thing to forget |
| **C** | Filter passes through on not-found and only blocks on wrong-owner | ✗ **Rejected** — reintroduces the existence oracle §3 avoids |

**Recommendation: A**, and update the two dispatch tests plus any client matching. Not taken
unilaterally because it changes an on-the-wire contract.

## 6. Test state — honest

Production code builds. `SessionOwnershipGuardTests` — 5/5, and **proven non-vacuous**: removing one
`.AddSessionOwnershipFilter()` line turns Rule 1 red; restoring it turns it green.

Compose suites: **1772 passed / 11 failed** (was 30 failed at first run). The repairs so far were all
**fixture** repairs per `bff-extensions.md` §F.2, not assertion relaxations:

- Six fake auth handlers minted `Guid.NewGuid()` as the oid **per request**. Entra never does that —
  an `oid` is stable per user per tenant, which is the whole property that makes it an ownership key.
  Those suites had been silently exercising "created by one user, read by another" on *every call*,
  and nothing noticed because nothing checked. Replaced with `TestSessionOwner.Oid`.
- 16 files passed a bare `new DefaultHttpContext()` — an anonymous principal, a request shape no
  `RequireAuthorization()` route can produce. Replaced with `TestHttpContexts.Authenticated()`.
  **This is why `LoadAsync` could not tell whose session it was resuming: the tests it was written
  against never had a caller identity, so the code was never written to need one.**
- 41 files constructed `ChatSession` with no owner; seeded with `TestSessionOwner.Oid`.

**The 11 that remain**, with cause:

| Suite | n | Cause |
|---|---|---|
| `ComposeDispatchEndpointContractTests` | 6 | §5 — the errorCode contract. Blocked on that decision. |
| `ComposeDocSessionDispatchSeamTests` | 2 | §5, same. |
| `ComposeSupersedeEndpointContractTests.Supersede_WhenSessionUnknown_Returns404` | 1 | 2-minute client-abort/timeout, NOT an ownership assertion — needs its own look; may pre-date this change. |
| `ComposeMemoryResumeEndpointContractTests.SaveAnnotations_WhenSessionUnknown_Returns404` | 1 | same shape as above. |
| `ComposeCreateOnSaveEndpointContractTests.CreateOnSave_WhenSpeCreateSucceeds…` | 1 | not yet diagnosed. |

**Not yet written**: the behaviour tests that prove denial — a second user cannot read, delete or
list the first user's session — under `tests/integration/auth/Ai/`. The guard test proves the filter
is *attached*; it does not prove it *denies*. Those must be observed to fail against the pre-fix code
first, per the 059 discipline. **This is the largest remaining gap and it is the half that matters.**

## 7. Coordination

`unified-access-control-r2` maintains the caller-identity census and owns access control
(`CallerIdentityGuardTests` allowlist, rows 2–3). This adds **no fourth resolver** — it consumes the
existing `CallerResolution` primitive — so it is not the coordination event that census names. They
should still see it: it adds an authorization filter over ~28 routes, and #858 already has them
working inside `ComposeService.cs`.
