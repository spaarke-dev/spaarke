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

## 5. RESOLVED - the errorCode decision (owner-approved 2026-08-28, option A)

The filter runs before the handler, so its 404 body replaces documented per-route codes. Approved
resolution: **the filter answers with ONE stable code**, `session.not-found-or-not-owned`, for all
three denial reasons. Distinguishing missing / unowned / someone-else's on the wire is the existence
oracle the 404-not-403 choice exists to prevent, so they must not be told apart. The operator can
still tell them apart - in the log line, which records `owned=true|false` and the correlationId.

Known casualty: `dispatch.session-not-found`. **The client is unaffected** -
`dispatchConsumer.mapDispatchHttpError` reads the `errorCode` extension generically and never
branched on the string, so nothing client-side needed changing. Verified, not assumed.

For a missing `tid` the filter answers **401 + `auth.tid-missing`** - reusing the code
`SummarizeSessionEndpoint` and `DispatchSessionEndpoint` already publish rather than inventing a
third spelling. Running ahead of the handlers makes this uniform across every `{sessionId}` route;
several previously answered 400, which is the less accurate reading (a principal whose tenant cannot
be established is unidentifiable, not malformed - the same doctrine as `CallerResolution`).

**An existing assertion caught a real defect in the filter's first draft**: it interpolated the
session id into the ProblemDetails `detail` string, which `DispatchSessionEndpointContractTests`
already forbade under ADR-019. On this route the rule is doubly load-bearing - echoing the id hands
the caller confirmation of the very id they probed with. Fixed; the assertion kept, with a note
saying what it caught.

## 6. The denial tests exist now - `tests/integration/auth/Ai/SessionOwnershipTests.cs`

**8 tests, all green.** The guard proves the filter is *attached*; these prove it *denies*:

| Test | What it pins |
|---|---|
| `Evaluate_ForADifferentUserInTheSameTenant_Denies404AndLeavesTheSessionIntact` | The core denial, asserted about the VICTIM's session so a check that merely reports the right owner cannot satisfy it |
| `Evaluate_ForADifferentUser_UsesTheSameAnswerAsAMissingSession` | The two are indistinguishable - if they ever diverge the route becomes an existence oracle |
| `Evaluate_ForAPreIssue863SessionWithNoOwner_DeniesEveryone` | The migration decision, executable |
| `Evaluate_ForACallerWithNoObjectIdClaim_Answers401NotAnUnfilteredPass` | An identity check that falls open when identity is absent is not a check |
| `Evaluate_ForTheRightOidInTheWrongTenant_Denies` | Ownership layers on top of tenant isolation, it does not replace it |
| `Evaluate_ForTheOwner_Allows` | **The positive control** - a filter that denied everyone would pass every other test here |
| `ListRecentSessions_ForAnUnidentifiableCaller_ReturnsNothingRatherThanTheTenantList` | Fail-closed: the pre-#863 shape returned the whole tenant |
| `CreateSession_WithoutAnOwner_IsRefusedRatherThanMintedUnowned` | An unowned session is broken on arrival, not a lax default |

**Proven non-vacuous in both directions.** Removing the ownership comparison from `EvaluateAsync`
turns 2 of them red; restoring it turns them green. Removing one `.AddSessionOwnershipFilter()` line
turns `SessionOwnershipGuardTests.Rule1` red.

The decision was extracted into `EvaluateAsync` so the tests exercise the shipping code rather than a
copy of its branch - the same reason `ChatEndpoints.DeleteSessionAsync` was made `internal` for the
059 tenant tests.

**Deliberately not tested, and said so rather than left silent**: the Cosmos owner predicate (needs
an emulator; covered structurally by guard Rule 2, which also asserts the `NOT IS_DEFINED` escape
hatch has not returned) and the warm-tier round-trip (the obvious test needs reflection over private
mappers, which ADR-038 B8 bans; covered by guard Rule 3).

## 7. Test state - honest

**BFF suite: 11,448 passing / 22 failing** (85 -> 59 -> 27 -> 22 across the repair passes).

Every repair so far has been a **fixture** repair per `bff-extensions.md` SS F.2, never an assertion
relaxation. What the fixtures were doing, and why it matters beyond this change:

- Eight fake auth handlers minted a fresh oid **per request** (`Guid.NewGuid()`), or fell back to one
  when a test header was absent. Entra never does that - stability per (user, tenant) is the entire
  property that makes an oid an ownership key. Those suites had been exercising "created by one user,
  read by another" on *every call*, invisibly, because nothing checked.
- 16 files passed a bare `new DefaultHttpContext()` - an anonymous principal, a request shape no
  `RequireAuthorization()` route can produce. **This is why `LoadAsync` could not tell whose session
  it was resuming: the tests it was written against never had a caller identity, so the code was
  never written to need one.**
- 54 files constructed a `ChatSession` with no owner (two syntactic shapes - `new ChatSession(` and
  target-typed `new(`; the second was missed on the first sweep).
- `ChatAckEndpointsContractTests`' minimal host mapped a `{sessionId}` route without registering
  `ChatSessionManager` - the same unconditional-registration rule as RB-T028-03..06 (root SS10
  bullet 6). The filter makes that latent gap loud, which is the rule working.

**The 22 remaining are one family**, and none is an ownership-logic failure: fixtures that leave
`Session = null` (or seed an unowned one) and therefore stop at the filter instead of reaching the
branch under assertion. Three shapes:

| Shape | Example | Repair |
|---|---|---|
| Validation test with no seeded session | `DispatchSessionEndpointContractTests.Post_MissingBindingId_Returns400` | Seed an owned session - a real request to a session route always resolves one |
| Session-not-found tests asserting the old per-route code | `ComposeMemoryResumeEndpointContractTests.SaveAnnotations_WhenSessionUnknown_Returns404` | Assert the filter's code (SS5) |
| Malformed `{sessionId}` expecting 400 | `...Post_InvalidGuidSessionId_Returns400` | Now 404 - truthful (an id that cannot be a GUID cannot name a session) and it keeps ONE answer for every id the caller does not own |

The `SummarizeSessionEndpointContractTests` cluster was repaired this way and is now 12/12; the
remaining suites (ReviewMemo x6, AgreementReview seam x7, Dispatch x3, ChatDocument x2, others x4)
take the identical treatment.

**One unrelated pre-existing failure** is in the list and should not be attributed here:
`AccessControl.DocumentDestroyAuthorizationTests.CheckoutFamilyRoute...(route: "checkin")` touches no
session route.

## 8. Coordination

`unified-access-control-r2` maintains the caller-identity census and owns access control
(`CallerIdentityGuardTests` allowlist rows 2-3). This adds **no fourth resolver** - it consumes the
existing `CallerResolution` primitive - so it is not the coordination event that census names. They
should still see it: it adds an authorization filter over 28 routes, and #858 already has them
working inside `ComposeService.cs`.
