# Task 109 — ONE junction read, two named sets

> **Status**: ✅ **IMPLEMENTED 2026-09-30** on branch `task/uac-r2-109` (session 27). Steps 0 and 1
> were measured live on **2026-09-21** (session 22, kept below unchanged) and **re-measured live on
> 2026-09-30** before any behaviour change — see "Step 0/1 re-measured". The implementation record
> starts at "§A. What shipped".
>
> ⚠️ **Environment: DEV.** The owner recorded (2026-09-17) that dev's records are TEST records.
> Everything below is true of dev and says **nothing** about a production tenant.

---

## Step 0 — live metadata verification (COMPLETE)

The task POML flagged both of these as **UNVERIFIED**, because the Dataverse MCP server was down
(`CONNECTION_CLOSED`) when the task was authored. The server is reachable again and both are now
verified against live metadata via `describe('tables/sprk_contactorganization')`:

| Column | Type | Consequence |
|---|---|---|
| `sprk_enddate` | **DATE ONLY** | ✅ The escalation trigger — *"if `sprk_enddate` is DateTime/UserLocal, a read-time date comparison is the wrong instrument"* — **does NOT fire**. A Date Only comparison is correct, and `ge` (not `gt`) is the right boundary, mirroring `ExpiryPredicate`. |
| `sprk_startdate` | **DATE ONLY** | ✅ Confirms task 117's finding and owner decision **D-10**'s mechanics: `le` not `lt`, because access holds **on** the start date. |

Neither escalation trigger fires. Task 109 may proceed on the date-comparison design as written.

Full column list also confirms the junction's shape used by the query: `sprk_contact` and
`sprk_organization` lookups, `statecode` STATE (Active 0 / Inactive 1), collection name
`sprk_contactorganizations`.

---

## Step 1 — before-state (COMPLETE): every category is ZERO, and each zero has a denominator

D-2 part 3 and D-10 both require counting and listing what loses access **before** any behaviour
ships. All three categories are **0**. That is only meaningful with the population size beside it —
this project's own task 047 records the lesson that a bare zero is indistinguishable from an empty
table, so the denominators are given.

| # | Category | Count | Denominator |
|---|---|---|---|
| 1 | **D-2 part 1** — `statecode=0` **and** `sprk_enddate` in the past | **0** | of **2** junction rows (both Active) |
| 2 | **D-10** — `statecode=0` **and** `sprk_startdate` in the future | **0** | of **2** junction rows |
| 3 | **ISS-026** — Active org-keyed grant whose `sprk_organization` is Inactive | **0** | of **5** org-keyed Active grants; **all 5** sit under Active organizations |

### The entire junction table — both rows, listed in full (D-2 part 3 asks for the list, not just a count)

| Id | `statecode` | `sprk_startdate` | `sprk_enddate` | Under D-2 + D-10 |
|---|---|---|---|---|
| `a00736f3-8f96-f111-b8db-0022482fb5a7` | 0 Active | **2026-08-12** (past) | null | **still confers** ✅ |
| `0f2cace5-5296-f111-b8db-3833c5e5d030` | 0 Active | **null** | null | **still confers** ✅ |

### 🔴 The finding that matters most here

**One of the two live rows has a NULL `sprk_startdate`** — so **half the live data exercises D-10's
`eq null` branch.** That branch is not a theoretical edge case being defended on principle. If an
executor "harmonises" the date columns by mirroring **task 107**'s null-branch inversion (107 made an
undated GRANT confer nothing), this row **loses access immediately** — and it is a perfectly ordinary,
currently-valid membership.

This is exactly the failure D-10's constraint and the null-start-date acceptance criterion exist to
prevent, and the live data says the trap is live, not hypothetical. **Pin the null case with a test.**

---

## Adjacent gate discharged in the same pass — task 107 §4.2a

Not task 109's, but measured while the connection was open, and it had been sitting **UNMET**:

| Query | Result |
|---|---|
| Active grants with **null** `sprk_expiresdate` | **0** |
| Active grants **already past** expiry | **0** |
| Active grants, total | **28** (all 28 carry an expiry date) |

**Task 107's pre-deploy COUNT gate PASSES in dev: 0 of 28 grants lose access on deploy.**

Owner decision **D-1**'s "blast radius zero" claim was explicitly recorded as *never re-verified*. It
is now verified — **against dev**. ⚠️ **The gate must be re-run against any production tenant before
deploying there.** A dev result does not discharge a production gate; the query is in
`docs/guides/EXTERNAL-ACCESS-ADMIN-SETUP.md` §4.2a.

---

## What was NOT done in session 22 (now done — see §A onward)

Everything else in task 109: the junction query change, the `ActiveOrgMemberships` extension carrying
two named sets, the ISS-019 fault-reporting fix, the ISS-026 read guard, the three retired-rule
artifact corrections, and all tests. **Steps 0 and 1 only.**

### Method note

Counts came from the Dataverse MCP `read_query` tool. Two cautions for whoever repeats this:

- `read_query` caps at **TOP 20**; a larger `TOP` is rejected outright rather than silently truncated.
- Category 1 and 2 were first measured with `COUNT(column)` (which counts non-nulls) and then
  **re-confirmed with an explicit `IS NULL` predicate**, because relying on `COUNT(col)` null
  semantics to discharge a deploy gate is the kind of shortcut that produces a confident wrong number.

---

## Step 0/1 re-measured live — 2026-09-30 (session 27), before any behaviour change

Read-only, spaarkedev1, via `EntityDefinitions` / collection GETs with the operator's own `az` token.

**Step 0 — stronger than session 22's check.** `describe` reports the FORMAT; the escalation trigger is
about BEHAVIOUR. Both are now verified at the attribute-metadata level:

| Attribute | `Format` | `DateTimeBehavior` |
|---|---|---|
| `sprk_contactorganization.sprk_enddate` | DateOnly | **DateOnly** |
| `sprk_contactorganization.sprk_startdate` | DateOnly | **DateOnly** |

Also verified: navigation property `sprk_Organization` (relationship
`sprk_contactorganization_Organization_sprk_organization`); `sprk_organization.statecode` Active(0) /
Inactive(1). **All three exact production query shapes return 200 live** (junction; contact grants;
org grants — each with `$expand=sprk_Organization($select=statecode)`), so escalation trigger 4 (the
ISS-026 guard needs a second read) does **not** fire. Captured live payload shapes, mirrored by the test
server: Date Only as `"2026-08-12"`; an unset lookup's expand as `"sprk_Organization": null`; a set one as
`{"statecode":0,"sprk_organizationid":"…"}`.

**Step 1 — unchanged from 2026-09-21.** Still **0 / 0 / 0**, plus a fourth category this task's
contact-grant guard adds (§A.3): active grants **of any kind** carrying an inactive organization — **0**.
Denominators: 2 junction rows (both Active, both organizations Active); 3 organizations (all Active, none
with `sprk_standinggrant`); 5 active org-carrying grants, all under the Active Morrison Foerster LLP — 4
org grants (`6f54531a-…`, `ec54e576-…`, `1967189b-…`, `9aed8ab9-…`) + **1 contact-keyed grant carrying
its firm** (`0452ab4b-…`). **Escalation trigger 3 does not fire. Removal on deploy in dev: zero rows.**

---

## §A. What shipped

| Change | Where |
|---|---|
| The junction read projects `sprk_startdate`, `sprk_enddate`, its own `statecode`, and the parent organization's `statecode` (by `$expand`) — **one** query | `ExternalParticipationService.QueryOrganizationMembershipsAsync` (`:1354`) |
| Junction `$filter` = the WALL's: statecode only, null counted active | `BuildOrganizationMembershipFilter` (`:187`) |
| The two NAMED sets, in memory, from those rows | `ProjectOrganizationMemberships` (`:273`) |
| Date bound — inclusive both ends, null = unbounded | `MembershipConfersOn` (`:217`) |
| A query fault is reported (`Unreadable`), never swallowed | non-success / timeout / exception → `ActiveOrgMemberships.Failed` |
| `ActiveOrgMemberships` **extended** (not replaced): `ConferringOrganizationIds` + `WallSubjectOrganizationIds` + `Unreadable` | `AccessibleRecordSetService.cs:228` — moved from private-nested to namespace `internal` because the reader now returns it |
| Org expansion → conferring set; deny veto → wall set | `AccessibleRecordSetService.cs:1222` / `:647` |
| Org-grant term → conferring set | `QueryOrganizationGrantRowsAsync` (`:1212`) |
| ISS-026 read guard on GRANT rows: an inactive (or unreturned) organization confers nothing | `GrantOrganizationConfers` (`:252`) + `WithoutInactiveOrganizations` (`:1280`), both grant reads |
| Evaluator entry renamed `QueryActiveOrgIdsAsync` → `ReadOrganizationMembershipsAsync` (`internal virtual`, returns the outcome) | `:1317`; five test doubles updated |

### §A.1 One READ, not one FILTER

The `$filter` defines the wall; the conferring set is narrowed in memory from the same rows. A date term in
the `$filter` would date-bound the WALL — a fail-OPEN change to a veto. Pinned three ways, each covering a
different edit:

| Instrument | Catches | Does NOT catch |
|---|---|---|
| `BuildOrganizationMembershipFilter_IsStatecodeOnly_WithNoDateTerm` | a date term inside the BUILDER | anything at the call site |
| ArchTest `MembershipJunctionQueriesUseTheWallSafeFilterBuilder` | a junction query built WITHOUT the builder (inlined) | a term APPENDED after the builder call — the builder's name is still in the window |
| `AssertEveryJunctionReadSentExactlyTheWallFilter` (wire; over-match theory + one-read test) | ANY edit that changes what the junction request sends — appended, inlined, on any line | a date term inside the builder (the wire then equals the altered builder — the builder test covers it) |

⚠️ **Corrected 2026-10-01 (verifier round 1).** The first version of this section said the ArchTest pinned
the call site, and the ArchTest's own remarks claimed it prevented a date-bounded wall filter. An appended
term — `$"?$filter={BuildOrganizationMembershipFilter(contactId)} and (sprk_enddate eq null or sprk_enddate
ge {TodayUtc:yyyy-MM-dd})"` — passed it and all 158 affected unit tests, because the test server never
evaluates `$filter`. The wire assertion closes it (perturbation P10); the ArchTest's remarks now state its
real scope.

### §A.2 Null semantics

| Value | Meaning | Why |
|---|---|---|
| `sprk_startdate` / `sprk_enddate` null | **Unbounded** — confers | D-2 / D-10; task 107's grant-expiry inversion deliberately NOT mirrored (live row `0f2cace5-…` above is exactly this case) |
| junction `statecode` null | active | `ExternalGrantRow.IsActive` + task 117's scan; filter is `(statecode eq 0 or statecode eq null)` |
| organization `statecode` null (expand present) | active | same rule |
| organization expand ABSENT while the lookup is set | **confers nothing**; still a wall subject | unknown ≠ active on a conferring path (ADR-003). Task 117's WRITER acts only on a CONFIRMED inactive — the opposite, correct for a writer |

### §A.3 ISS-026 on grant rows — contact-keyed grants included

The criterion scopes "unaffected" to *"a contact-keyed grant with no organization lookup"*, so a
contact-keyed grant WITH a lookup to an inactive organization IS affected. That lookup is the `/grant`
writer's firm association (`GrantExternalAccessEndpoint.cs:699-705`), and task 117's R2 deactivates exactly
that population (every active grant whose `sprk_organization` is inactive, contact or not). The read guard
therefore covers precisely what the writer later makes permanent. Live count: 0.

### §A.4 Faults — both directions, from one outcome

| Fault | Before | Now |
|---|---|---|
| Junction query non-success / timeout / exception | empty list → veto read "belongs to no organization" → wall's org axis silently gone (ISS-019) | `Failed` → veto denies every candidate; additive terms get an empty conferring set |
| Token / API-url acquisition (entry throws) | `Failed` (deny-all) | unchanged — now pinned on the REAL entry (§I.2) |
| Caller cancellation | junction swallowed; evaluator rethrew | query reports `Failed`; the evaluator's entry rethrows (§A.5) |

The `:1060-1069` constraint ("do not unify the two fail directions") holds: what is now shared is the FACT
of a fault; each consumer still applies its own direction.

### §A.5 Code-review fix W3 — a regression this change would otherwise have introduced

The first draft rethrew caller cancellation from inside the junction query. That query also runs inside
`QueryGrantSetAsync`, whose catch-all turns any escaping exception into an EMPTY grant set (direct grants
included) and caches it — a client abort mid-read would cache "no access at all" for 60 s where previously
only the org-grant term was lost. Fixed: the query reports cancellation as `Failed`; only
`ReadOrganizationMembershipsAsync` rethrows. Pinned by
`GetGrantSetAsync_CallerCancelsDuringTheJunctionRead_KeepsTheDirectGrants` (perturbation P9).

> **Superseded by task 132 r1 (2026-10-03).** Task 132 made `QueryGrantSetAsync` rethrow the caller's cancellation
> and stopped any faulted grant set from being cached, which removed W3's premise. The junction query now rethrows the
> caller's cancellation too (task 132 criterion 3), and this test was rewritten as
> `GetGrantSetAsync_CallerCancelsDuringTheJunctionRead_PropagatesTheCancellation_AndCachesNothing` — W3's real concern,
> never caching "no access at all" on a client abort, is kept. See `notes/task-132-access-cache-faults-and-staleness.md` §2 / §15.

### §A.6 No `CacheVersion` bump

The cached grant-set shape is unchanged (filtering happens before caching). An entry written under the old
rule survives at most one TTL (≤ 60 s) after deploy — the documented staleness bound for any revocation.

## §B. Read accounting (NFR-02 / task 043)

| Read | Count | Note |
|---|---|---|
| Evaluator junction read | **1 per composition** | unchanged from 043; now projects both sets. Asserted on the wire by `ComposeAsync_OneJunctionReadPerResolution_ServesTheConferringAndTheWallSets` |
| Grant set (cached 60 s) | 1 on a miss | its org-grant term has its OWN junction read — pre-existing since task 073, counted under "grant set" in task 043 §6 — now using the same query + projection (conferring set) |

🔶 **Residual, recorded rather than hidden**: on a grant-set cache MISS a composition sees two junction
requests (the org-grant term's, cached with the grant set, and the evaluator's). Same query, filter and
projection — they can differ only by time, and the grant set's view is bounded by the 60 s TTL anyway.
Unifying them means threading the evaluator's outcome into the grant-set miss path, which changes
`GetGrantSetAsync`'s signature (six overriding test doubles) and the CIAM `/me` surface. Task 132 is
restructuring exactly that path's fault handling and is the natural owner. Also handed to 132: the
`Unreadable` signal inside the org-grant term is logged but not carried on the grant set, so a grant set
built over a faulted junction read is still cached for one TTL (`auth.md` C12 rule — 132 owns it).

⚠️ **The first hand-off is NOT yet recorded where 132 would see it** (verifier round 1, 2026-10-01). Task
132's POML names only the second item (its criterion 4 consumes 109's `Unreadable` to avoid caching a
faulted grant set); it says nothing about the cache-MISS second junction read. A sub-agent may not edit
another task's POML or the shared register, so this is a **main-session action** — add to
`tasks/132-c12-access-caches-fault-and-staleness.poml` (`<notes>`, or a `<dependency task="109">` line), or
to `notes/defer-issues.md`:

> **From task 109 (§B residual).** On a grant-set cache MISS, one composition issues TWO
> `sprk_contactorganizations` requests: the org-grant term's own read inside `QueryGrantSetAsync`
> (`ExternalParticipationService.QueryOrganizationGrantRowsAsync` → `QueryOrganizationMembershipsAsync`)
> and the evaluator's `ReadOrganizationMembershipsAsync`. Same query, filter and projection; they can
> differ only by time, and the grant set's copy is bounded by the 60 s TTL. Pre-existing since task 073,
> counted under "grant set" by task 043 §6, so task 043's one-read criterion is met in its own sense — but
> it is a second snapshot on the miss path. Unifying it means threading the evaluator's outcome into the
> grant-set miss path (changes `GetGrantSetAsync`'s signature — six overriding test doubles — and the CIAM
> `/me` surface). Decide in 132: unify, or record it as an accepted residual with that reason.

## §C. The three retired-rule artifacts — corrected

1. "a former member is a deactivated row and is excluded" (HEAD `:1107-1108`; POML cited `:1081-1082`) —
   **removed**; the replacement says the opposite, correctly (a former member can be statecode-active).
2. The stale "confirm against the created junction schema" caveat (HEAD `:1109-1111`) — **removed**; cites
   the 2026-08-26 live verification and this task's 2026-09-30 one.
3. `ExternalAccessQueryIntegrityGuardTests.cs:54-57` — no longer says "a membership row has no expiry"; the
   stale `:1068` reference is replaced by the method name so it cannot drift again.

Also corrected because they named the renamed method or restated the retired rule:
`RevokeExternalAccessEndpoint.cs` remarks (comments only; the revoke sweep stays statecode-only — now a
strict SUPERSET of the read side's conferring set, the safe direction for a revoke),
`IMembershipResolverService.cs:172`, `src/solutions/SpaarkeCore/entities/sprk_noaccessentry/entity-schema.md:61`.

## §D. Tests

New: `tests/integration/auth/UnifiedAccessControl/OrganizationMembershipReadTests.cs` (KEEP `auth/`), 21 tests
at first commit; **23** after verifier round 1 (§I).

| Test | Pins |
|---|---|
| `MembershipConfersOn_NullStartOrNullEnd_IsUnboundedAndConfers` | null start / end unbounded — the explicit null-start pin |
| `MembershipConfersOn_StartTodayOrEndToday_Confers` | inclusive boundaries |
| `MembershipConfersOn_EndDatePassed_ConfersNothing` | D-2 part 1 |
| `MembershipConfersOn_StartDateInTheFuture_ConfersNothing` | D-10 |
| `ProjectOrganizationMemberships_EndedByDateButActive_IsAWallSubjectButConfersNothing` | D-2 parts 1 + 2 |
| `ProjectOrganizationMemberships_StartDateInTheFuture_IsAWallSubjectButConfersNothing` | D-10 both halves; start TODAY confers |
| `ProjectOrganizationMemberships_InactiveOrganization_IsAWallSubjectButConfersNothing` | ISS-026 for memberships; active organization unaffected |
| `ProjectOrganizationMemberships_NullStatecodes_AreActive` | null statecode = active (junction + organization) |
| `ProjectOrganizationMemberships_OrganizationStateDidNotComeBack_…` | unknown organization state confers nothing |
| `ProjectOrganizationMemberships_InactiveJunctionRow_IsInNeitherSet` | statecode re-decided in code |
| `BuildOrganizationMembershipFilter_IsStatecodeOnly_WithNoDateTerm` | the wall is not date-bounded server-side |
| `GrantOrganizationConfers_…` | ISS-026 on grant rows; no-org contact grant unaffected |
| `GetGrantSetAsync_OrgGrantTerm_ConfersOnlyThroughCurrentMembershipsOfActiveOrganizations` | **real transport**: org-grant term (no standing gate) bounded by D-2 / D-10 / ISS-026; contact grant with inactive firm drops; plain contact grant unaffected |
| `ComposeAsync_OrgKeyedDenyRow_StillMatchesAMemberWhoseMembershipNoLongerConfers` ×3 | **real transport**: veto over-match — ended / not-yet-started / inactive-organization |
| `ComposeAsync_JunctionQueryFaults_DeniesEveryCandidateAndTheAdditiveTermsContributeNothing` ×3 | **real transport**: ISS-019 — 500 / 403 / timeout, both directions from one fault, with a healthy control |
| `ComposeAsync_JunctionEntryCannotAcquireItsTokenOrApiUrl_DeniesEveryCandidate` ×2 | **real entry** (added §I.2): the pre-existing token / API-url acquisition fault still denies every candidate |
| `GetGrantSetAsync_CallerCancelsDuringTheJunctionRead_KeepsTheDirectGrants` | review fix W3 — **superseded by task 132 r1**: now `…_PropagatesTheCancellation_AndCachesNothing` (§A.5 note) |
| `ComposeAsync_OneJunctionReadPerResolution_ServesTheConferringAndTheWallSets` | one read on the wire serves both sets; its `$filter` is exactly the wall's (§I.1) |

**Instrument.** The fault and over-match claims run the REAL `ExternalParticipationService` and
`AccessibleRecordSetService` over an in-memory ASP.NET Core server (`UseTestServer`) standing in for the
Dataverse Web API — deliberately: ISS-019 shipped because task 043's double THREW where the real query
returned an empty list; a double here would assert the fake again. A test server is ADR-038 §7's named
replacement for ban B1 — not a `Mock<HttpMessageHandler>`. Membership resolution, standing grants and the
No Access reader are substituted at their module-boundary interfaces.

**Beyond the closed set, justified**: the caller-cancellation test guards a regression this task's own
change would otherwise have introduced (§A.5); the `inactive-organization` over-match case pins that the
ISS-026 guard did not leak into the wall; the new ArchTest makes the "no date in the wall's filter" rule
bind the call site, not only the builder.

**Updated**: five test doubles now override `ReadOrganizationMembershipsAsync`. The pre-existing
entry-throws test is renamed `ComposeAsync_WhenTheJunctionEntryThrows_DeniesEveryCandidateAndDoesNotThrow`
and now names the token/API-url path it covers.

### Perturbations — each seeded, observed, restored

| # | Seeded violation | Result |
|---|---|---|
| P1 | fault reporting reverted (non-success + exception → `None`) | **3 red** — all three fault cases |
| P2 | end-date bound removed | **4 red** |
| P3a | veto fed the CONFERRING set (date-bounded wall) | **4 red** — three over-match + one-read |
| P3b | date term added to the junction `$filter` builder | **1 red** — the filter test (E2E cannot see OData, by design) |
| P3c | date-bounded `$filter` inlined at the call site | **1 red** — the new ArchTest guard |
| P4 | start-date bound removed | **4 red** |
| P5 | null-start branch inverted (task 107's inversion) | **13 red**, incl. the explicit null-start pin |
| P6a+b | ISS-026 guard removed (projection + grant rows) | **4 red** |
| P6b alone | grant-row guard removed | **1 red** — the inactive-firm grant reappears |
| P7 | a second junction read for the veto (ISS-019's rejected suggestion) | **1 red** — the one-read test |
| P8 | token-path catch weakened to `None` | **2 red** — entry-throws unit + seam tests |
| P9 | caller cancellation rethrown from the query (pre-W3) | **1 red** — the cancellation test |
| P10 | date term APPENDED after the builder at the junction call site (verifier's exact seeding) | **4 red** — three over-match cases + the one-read test (wire filter ≠ builder). ArchTest stays green, as its corrected remarks now say |
| P11a | REAL entry swallows token + API-url acquisition into `ActiveOrgMemberships.None` (verifier's exact seeding) | **2 red** — both acquisition cases, on the main assertion (candidates `9d…01`, `9c…03` survive) |
| P11b | real entry swallows the token fault only | **1 red** — `token` case |
| P11c | real entry swallows the API-url fault only | **1 red** — `api-url` case |

All restored; `grep PERTURBATION src/` is empty. P1-P9 were taken on the first commit; P10-P11 on
`task/uac-r2-109-f1` (affected set: 156 tests in the BFF assembly + 4 in `ExternalAccessQueryIntegrityGuardTests`).

**Instrument check for §I.2's confinement** (scratch test, deleted): under P11a, an UNCONFINED fault — a
credential that throws on every request — left the composition empty both with a cold grant-set cache
(grant read fails to an empty set → no candidates) and a warm one (the flag read and the org-reference read
each fail closed on the same token fault → every candidate removed). Both passed under the weakening. That
is why the committed test arms ONE acquisition and asserts it struck only the junction entry.

## §E. Pending manual gates (no live writes were made)

- **Re-take the before-state in any non-dev environment before deploying there** (read-only):
  ```
  GET {org}/api/data/v9.2/sprk_contactorganizations?$select=sprk_contactorganizationid,_sprk_contact_value,_sprk_organization_value,sprk_startdate,sprk_enddate,statecode&$expand=sprk_Organization($select=statecode)
  GET {org}/api/data/v9.2/sprk_externalrecordaccesses?$filter=(statecode eq 0 or statecode eq null) and _sprk_organization_value ne null&$select=sprk_externalrecordaccessid,_sprk_contact_value,_sprk_organization_value,_sprk_project_value,_sprk_matter_value,_sprk_workassignment_value,statecode&$expand=sprk_Organization($select=statecode)
  ```
  Count junction rows active with `sprk_enddate` < today, active with `sprk_startdate` > today, and grant /
  junction rows whose expanded `sprk_Organization.statecode` = 1. More than a handful → owner sign-off.
- **Publish size** — skipped by instruction (the main session measures after merge). No package changes.

## §F. ISS-020 / task 110 verification map

| Task 110 criterion | Evidence |
|---|---|
| null or ≥-today end date still confers | `MembershipConfersOn_NullStartOrNullEnd_…`, `…_StartTodayOrEndToday_Confers`, grant-set E2E (OrgCurrent) |
| passed end date confers nothing while active | `MembershipConfersOn_EndDatePassed_…`, `ProjectOrganizationMemberships_EndedByDate…`, grant-set E2E |
| D-10: future start nothing; today / null confers; veto not start-bounded | `…_StartDateInTheFuture_…` (both), `…_NullStartOrNullEnd_…`, over-match theory `not-yet-started` |
| org-keyed deny still matches an ended membership | over-match theory `ended` |
| conferring + wall from ONE read, read-count test | `ComposeAsync_OneJunctionReadPerResolution_…` (+ P7) |
| before-state recorded (count + list) | Step 1 (2026-09-21) + re-measure (2026-09-30), above |
| no second junction query / outcome record / date-bounding path | grep: `/sprk_contactorganizations` occurs ONCE in `src/server` (`ExternalParticipationService.cs:1359`); the other junction readers are pre-existing and deliberately separate (task 020's inverse revoke reader, task 117's writer scan); `ActiveOrgMemberships` is the only outcome record |

Closing **#999** and the defer-issues disposition rows are task 110's / the main session's — not done here.

## §G. Premise checks (the code won again)

| POML claim | Reality at HEAD `a099fe394` |
|---|---|
| `ExpiryPredicate (:99-100)` is `(sprk_expiresdate eq null or … ge …)` | **Stale** — task 107 already dropped the `eq null` branch: it is `sprk_expiresdate ge {today}` (`:114-115`). The mechanics to mirror (`ge`, Date Only) are unchanged; the null branch is the inversion NOT to mirror |
| asymmetry `:1060-1069`; false comment `:1081-1082`; caveat `:1083-1085`; junction query `:1092-1094` | drifted to `:1086-1095`, `:1107-1108`, `:1109-1111`, `:1118-1120` (task 131 added lines) |
| task 110 background: org expansion "`ExternalParticipationService.cs:1209-1221`" | wrong file — it is `AccessibleRecordSetService.cs:1209-1221` |

## §H. CLAUDE.md §10 / §11

**Placement**: BFF, inside the existing `Infrastructure/ExternalAccess` evaluator path — a read-path
correctness change to two existing classes. No new service, DI registration, endpoint, option, job or
package. **§11**: no new component — `ActiveOrgMemberships` extended (existing: task 043's outcome;
extension: yes; cost-of-doing-nothing: without a carried fault and two named sets a junction fault
silently removes the wall's organization axis, ISS-019). New `internal static` members are pure and
extracted to be assertable (ADR-038 A2); `ContactOrgRow` / `OrganizationStateRow` widened from private for
the same reason. **CVE**: `dotnet list package --vulnerable --include-transitive` → none.

## §I. Verifier round 1 (2026-10-01) — closures, branch `task/uac-r2-109-f1`

Test-only round: **no production source changed** (`git diff -- src/` is empty). Results: affected set
156/156; full BFF unit suite **13212 passed / 0 failed / 54 skipped** (13266; the +2 are the new theory cases);
NetArchTest **338/338**.

### §I.1 GAP 1 — the wall's server filter was not pinned at the call site (verifier items 4 + 9)

Closed. `AssertEveryJunctionReadSentExactlyTheWallFilter` asserts that every `sprk_contactorganizations`
request the test server received carried a `$filter` EQUAL to `BuildOrganizationMembershipFilter(ContactId)`
(the decoded query value round-trips exactly). Called from the over-match theory — so the criterion "date-bounding
the VETO subject reddens the over-match test" now holds for the appended form — and from the one-read test.
Perturbation P10 (the verifier's exact seeding): 4 red. The ArchTest's remarks, which claimed it prevented this,
now state what it catches and what it does not (§A.1 table).

### §I.2 GAP 2 — the token/API-url fail-closed path was pinned only through a double (verifier items 5 + 10)

Closed. `ComposeAsync_JunctionEntryCannotAcquireItsTokenOrApiUrl_DeniesEveryCandidate` (token / api-url) runs
the REAL `ExternalParticipationService` entry through the REAL evaluator, with the fault armed for exactly one
acquisition (`ArmableTokenCredential` — tokens under the 5-minute refresh margin so the entry's acquisition
reaches it; `ArmableServiceUrl` — an `IConfigurationSource` whose next `Dataverse:ServiceUrl` read is missing,
with a cached long-lived token so the entry's only config read is `GetDataverseApiUrl`'s). Preconditions prove
the fault fired once, struck before the junction query was sent, and left the later flag read healthy; the grant
set comes from the production cache so the candidates exist regardless. P11a/b/c: each weakening reddens its case.

Not a `Mock<HttpMessageHandler>` (the transport is the same `UseTestServer` stand-in); no DI-registration or
ctor-null test; no new production surface.

### §I.3 Residual — cache-miss second junction read (verifier item 6)

Not a defect in this task (043's accounting; disclosed in §B). The hand-off was not recorded anywhere 132 reads;
the paste-ready text is now in §B. Recording it in 132's POML or `defer-issues.md` is a **main-session** action.

### §I.4 Publish size (verifier items 8 + 11)

Still PENDING — skipped by run instruction; the main session measures after merge (fresh master vs branch, short
path, Compress-Archive, equal file counts). This round changed no production source and no package.
