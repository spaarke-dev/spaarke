# TASK-INDEX — `unified-access-control-r2`

> **58 tasks** across 6 phases · generated 2026-08-21 · `scripts/Validate-TaskPoml.ps1`: **PASS** (57 clean, 0 errors, 1 benign WARN on 090)
> Source: [`spec.md`](../spec.md) 32 FRs / 7 NFRs · [`plan.md`](../plan.md) · [`CLAUDE.md`](../CLAUDE.md)
> Status legend: 🔲 pending · 🔄 needs retry · ✅ complete

Number gaps (020–029, 045–049, 059, 070–079, 084–089) are intentional insertion room.

---

## ⚠️ Read before executing anything

| Rule | Why |
|---|---|
| **001 blocks every Phase 0 code task** | NFR-07 — the access-path test baseline is near-zero. Characterize before changing behaviour |
| **034 is a blocking merge gate for 036** | NFR-04 — if impersonation is inert the query silently returns org-wide rows. Equality between impersonated and app-only = fail |
| **008 (delegation) must ship before 063/065** | Otherwise the PCF "+ User" button is one-click privilege escalation on a confidential matter |
| **030 before 032 · 031 before 035/036 · 040 before 041** | ADR amendments sanction the shape; path B means the amendment merges before or alongside dependent code |
| **030 / 031 / 040 are main-session-only** | They edit `.claude/**`. Sub-agents cannot write there (root CLAUDE.md §3) — "Edit denied" is the boundary working |
| **`/conflict-check` before EVERY BFF PR** | Surface shared with shipped `SPA-external-access-platform-r1/r2` + `teams-app-r1`; draft `SPA-r3` must be notified |

---

## Phase 0 — Enforcement remediation (19 tasks)

| # | Task | FR / finding | Deps | Group | Safe | Tier | Effort |
|---|---|---|---|---|---|---|---|
| ✅ 001 | Access-path characterization + negative suite | NFR-07 | — | **P0-W0** | ✅ | sonnet | high |
| ✅ 002 | Authorize document download | FR-01 / A-1 | 001 | — | ❌ | sonnet | high |
| ✅ 003 | `OperationAccessPolicy` keys + completeness test | FR-03 / A-3,A-20 | 001 | — | ❌ | sonnet | high |
| ✅ 004 | `AuthorizationService` caller-scoped | FR-02 / A-2 | 001,003,014 | — | ❌ | **opus** | **xhigh** |
| ✅ 005 | Lift the Read ceiling | FR-04 / A-20 | 001,004 | — | ❌ | sonnet | high |
| ✅ 006 | Caller-scoped `PermissionsEndpoints` | FR-05 / A-4 | 001,004 | — | ✅ | sonnet | high |
| ✅ 007 | Enforce grant expiry in the read filter | FR-06 / A-5 | 001 | — | ❌ | sonnet | high |
| ✅ 008 | Delegation rule — Write-on-target | FR-07 / A-6 | 001 | — | ❌ | sonnet | high |
| 🔲 009 | Scope-check external To Do PATCH (+H-8a) | FR-08 / A-7 | 001 | — | ❌ | sonnet | high |
| ✅ 010 | Idempotent grant + revoke-all | FR-09 / A-11 | 001 | — | ❌ | **opus** | **xhigh** |
| 🔲 011 | Reject same-entity self-join | FR-10 / A-17 | 001 | — | ❌ | sonnet | high |
| 🔲 012 | Track or disable anonymous share links | FR-11 / A-14 | 002 | — | ❌ | sonnet | high |
| 🔲 013 | Workforce email `oid` no-hijack | FR-12 / A-18 | 001 | — | ❌ | sonnet | high |
| ✅ 014 | Cache key includes auth mode | FR-13 / A-19 | 001 | **P0-B** | ✅ | sonnet | high |
| 🔲 015 | Deterministic + complete membership paging | FR-14 / A-10 | 001 | — | ❌ | sonnet | high |
| 🔲 016 | Close-project cascade (contact + org) | FR-15 / A-12 | 001 | — | ❌ | sonnet | high |
| 🔲 017 | SPE revoke matcher + H-8b relic | FR-16 / A-13 | 001,010 | — | ❌ | sonnet | high |
| 🔲 018 | Remove dead filter + bound `in`-clause | FR-17 / A-15,A-16 | 001 | — | ❌ | sonnet | high |
| ✅ 019 | Fix `LookupUserMembership` `["*"]` | FR-17 / A-22 | 001 | **P0-B** | ✅ | sonnet | high |

**Critical path**: 001 → {003, 014} → 004 → {005, 006} · plus 001 → 010 → 017 · plus 002 → 012

> **Task 001 outcome (2026-08-21)**: 62 tests green at `tests/integration/auth/UnifiedAccessControl/`
> (the ADR-038 §2 security-auth KEEP path — **first backfill**; it had zero compiled files and was
> globbed by no csproj). **9 of 20 Phase 0 findings pinned, 1 partial, 10 not reachable offline.**
> Tasks 002/003/004/005/006/008/010/011/014 have their baseline and are unblocked. Tasks
> **007, 012, 013, 015, 016, 017, 018, 019** must supply their own coverage — see
> [`notes/task-001-untestable-findings.md`](../notes/task-001-untestable-findings.md) §2–3 for why and
> the recommended approach (extract a query-builder seam inside each fix task).
> ⚠️ Any task testing `/api/v1/external` MUST use `ExternalCollaborationTestFixture` — the shared
> fixtures make that group return 500, which silently turns "not 403" assertions into vacuous passes.

> **Wave P0-B outcome (2026-08-21)** — 014 + 019 executed **in parallel** (the only file-disjoint pair in
> Phase 0). No file overlap; post-wave build + full suite verified by the orchestrator.
> **014**: key is now `sdap:auth:access:{authMode}:{userId}:{resourceId}` (`sp`/`obo`), never the raw token.
> Escalation evaluated and correctly did NOT fire — `userId` IS the caller's `oid`, so two OBO callers
> already differ (verified independently). 3 characterizations flipped + 1 new test.
> **019**: `IncludeRelated` is now always `null`; `includeRelated: true` is a **logged-warning no-op**, not a
> silent one. Escalation FIRED and is resolved-but-open: the flag is visible in the Playbook Builder canvas
> and does nothing. **No playbook sets it today** (verified), so this is latent. 019 also corrected a
> pre-existing test that had pinned the buggy `Contain("*")` behaviour.
> Follow-ups filed: register **I-4** (no tenant segment in `sdap:auth:*` keys → design task 035's per-user
> cache tenant-aware from the start); stale "task 054 implements" comments remain in `MembershipEndpoints.cs`
> + `IMembershipResolverService.cs` → **task 015** owns that directory.

> **Task 003 outcome (2026-08-21)**: 4 keys registered; 15-test source-scanning completeness gate added
> (`OperationAccessPolicyCompletenessTests`). 8 task-001 characterizations flipped. Sweep **confirmed
> A-20's list complete** (22 `Add*Filter` extensions exist; only 7 consult the policy) and filed **A-23**
> (a second orphaned filter → task 018). Two new obligations recorded as POML constraints:
> **task 005 MUST map Dataverse `AppendToAccess`** (else `POST /api/office/save` is permanently 403 while
> *looking* fixed), and **task 018 deletes `AddOfficeDocumentAccessFilter`** alongside A-15. Rationale:
> [`notes/task-003-operation-rights-decisions.md`](../notes/task-003-operation-rights-decisions.md).

> **Task 004 outcome (2026-08-21)**: token rides on `AuthorizationContext.UserAccessToken`
> (**`required string?`** — forces every construction site to declare intent, so app-only is a visible
> `= null`, never a default; produced 7 compile errors across 11 sites). Missing token → DENY with
> `sdap.access.deny.no_caller_token`, data source **never consulted**. `IHttpContextAccessor` was rejected
> — `Spaarke.Core` has no ASP.NET Core dep and `LayerDependencyTests` guards that. POML **Step 3 was
> vacuous** (zero app-only consumers), not skipped.
> ⚠️ **FR-02's criterion is NOT closed by 004 alone** — `PermissionsEndpoints.cs:76,:159` still pass
> `userAccessToken: null` because they call `IAccessDataSource` **directly**, bypassing
> `AuthorizationService`. That is A-4 → **task 006**, which should route them THROUGH the service rather
> than re-plumb the token. Rationale: [`notes/task-004-caller-scoped-design.md`](../notes/task-004-caller-scoped-design.md).

> **Task 006 outcome (2026-08-21)**: ✅ **FR-02's criterion is now CLOSED** alongside FR-05 — a repo grep
> for `userAccessToken: null` returns **zero** production call-sites. `AuthorizationService` gained
> `GetCallerAccessAsync(userId, resourceId, userAccessToken, ct)` — **no default** on the token param,
> because A-4's root cause was the `= null` *default* on `IAccessDataSource.GetUserAccessAsync`, not a
> missing null check. `AuthorizeAsync` routes through it, so the service now has **exactly one** member
> touching `_accessDataSource` (verified by grep + a test pinning that both paths present identical
> arguments) — acceptance criterion 5 is structural, not asserted. Fourteen capabilities project from ONE
> snapshot rather than fourteen `AuthorizeAsync` calls (the batch route would otherwise be 1,400
> rule-chain evaluations per 100-doc request). No-access shape = **200 + all-false**, not 403.
> **Second disclosure found + closed**: the batch handler honoured a `UserId` from the request BODY.
> `DataverseAccessDataSource.cs:184-199` treats `userId` and `userAccessToken` as INDEPENDENT, so that
> would have queried a different principal under the caller's OBO token and written task 014's cache key
> under the **victim's** oid. `BatchPermissionsRequest.UserId` is removed (wire-compatible).
> Escalation trigger evaluated and correctly did NOT fire — **zero clients** call either route (two
> independent greps); the endpoint has been shipping a disclosure nothing consumed.
> ⚠️ Until **task 005** lifts the Read ceiling, eleven of the fourteen capabilities are false for
> everyone in production — the honest interim state, not a regression.
> Rationale: [`notes/task-006-capability-rights-mapping.md`](../notes/task-006-capability-rights-mapping.md).

> **Task 005 outcome (2026-08-21)**: the A-20 Read ceiling is gone. **Key discovery: the fix was mostly
> RECONNECTION, not new code** — `MapDataverseAccessRights` (all seven flags, `AppendToAccess` included)
> and `PrincipalAccessResponse` already existed in `DataverseAccessDataSource.cs` as **dead code**: the
> orphaned wiring of a `RetrievePrincipalAccess` implementation replaced long ago by a
> "can-I-retrieve-the-record → therefore Read" probe. Confirmed repo-wide: **`RetrievePrincipalAccess`
> had ZERO live call sites** (every reference was a doc comment).
> `RetrievePrincipalAccess` is now called first; **the old probe survives as a fallback** because the
> deleted comment's claim that it "may not be available" under OBO is unverified and unverifiable
> offline. The composition cannot regress — worst case is exactly today's behaviour — and failures log
> the **`RPA-FALLBACK`** marker so a silent re-cap at Read is visible.
> **Both escalation triggers evaluated, neither fired**: RPA *replaces* the probe (1 call, no extra round
> trip), and all six consumers of `AccessSnapshot.AccessRights` were enumerated — `AiAuthorizationService`
> checks Read only (unaffected), two are the intended beneficiaries, two read a different rights model
> entirely.
> ⚠️ **Corrected a mis-framed task-001 test**: `Characterization_WritePlusOperation_DeniedUnderReadCeiling`
> was doc-commented "FLIPPED BY: task 005" — **following that would have been a security regression**. It
> gives the rule a Read-only snapshot; denying Write+ there is permanently correct. The ceiling was never
> observable at the rule layer. Renamed + re-documented; real coverage moved to the endpoint suite.
> Task 003's `AppendTo` obligation and task 006's capability obligation are both **discharged**.
> ⚠️ **Untested boundary (deliberate, not hidden)**: no test exercises the `RetrievePrincipalAccess` HTTP
> call itself — mocking that transport is ADR-038 ban B1. Its URL form and OBO availability need a live
> tenant → folded into **task 034** / Phase 4 acceptance.
> Rationale: [`notes/task-005-rights-mapping.md`](../notes/task-005-rights-mapping.md).

> **Task 002 outcome (2026-08-22)**: **R1's January-2026 attack scenario is closed.**
> `GET /api/documents/{id}/download` now carries `AddDocumentAuthorizationFilter("read")`. The app-only
> SPE stream is deliberately UNCHANGED — files written by the MI are only readable by it (Writer-Identity
> Matching, auth constraints Pattern 4); the defect was the missing Dataverse-level gate, not the stream.
> ⚠️ **SCOPE WIDENED BY ONE ROUTE, deliberately**: `GET /api/documents/{id}/content` streams the SAME
> bytes from the SAME app-only path with the SAME missing gate. Closing `/download` alone would have left
> the attack fully intact behind a different URL, so both were closed together.
> **Operation key `"read"`, not `download_file` (Write)** — the sibling route
> `DataverseDocumentsEndpoints.cs` `GET /api/v1/documents/{id}/download` ALREADY uses `"read"`, as does
> eml-render; task 001's characterization pinned exactly that these routes *disagree*. Write would have
> recreated the inconsistency and newly denied download to every Read-only user on a live UI path.
> ⚠️ **OPEN FOR OWNER**: enforcement now says Read while task 006's `CanDownload` capability says Write.
> Benign in effect (UI hides a button that would work) but it is the divergence FR-05 criterion 5 exists
> to prevent. Two options documented — a product decision, not an implementation one.
> ⚠️ **FILED, NOT FIXED**: four more routes on this group (`preview-url`, `view-url`, `office`, `preview`)
> have no per-document filter. They mint URLs rather than stream bytes — a different blast radius and a
> separate decision. **Should be assessed as its own task.**
> Rationale: [`notes/task-002-download-authorization.md`](../notes/task-002-download-authorization.md).

> **Task 010 outcome (2026-08-22)**: A-11 (**ranked #1 of 13**) closed. `/grant` UPSERTS against a logical
> key; `/revoke` sweeps EVERY active row on that key. Grant→grant→revoke leaves zero.
> **Logical key = `(root) × (Contact XOR Organization)`**, and two details are load-bearing: (a) a row may
> carry BOTH a contact and an org — the org is the contact's **firm**, association metadata, NOT identity;
> contact wins, or a person grant and an org grant on the same root would collide and could revoke each
> other. (b) `_sprk_contact_value eq null` is what makes an org grant an org grant — drop it and revoking
> one firm's grant sweeps every member's personal grant. The key mirrors the READ side
> (`ExternalParticipationService.cs:511`) term for term: **write/read disagreement about "the same grant"
> IS A-11.**
> **Concurrent-grant race**: both racers MUST elect the same survivor or they deactivate each other and
> the grant vanishes — worse than the bug. Election is `OrderBy(id).First()`, stable and clock-independent
> (`createdon` can tie).
> **Underivable key → FAIL LOUDLY, deactivate nothing.** The POML flags this as an escalation, but the
> task's own ADR-003 constraint answers it: siblings that cannot be queried cannot be guaranteed absent,
> so reporting success is forbidden. All three escalation triggers evaluated; **none fired**.
> ⚠️ **A REAL DEFECT was caught by the full suite** (`ExternalAccessContractTests.InviteAndGrant_…`): the
> upsert adopted an unaddressable row (`Id == Guid.Empty`) as "the existing grant", aimed an update at
> `Guid.Empty`, and returned an empty id — a silent no-op reported as success. Fixed in **production**
> (discard rows with no usable id), not by adjusting the stub. Task 005's TRX-capture technique named it
> immediately; under `-v q` it would have been indistinguishable from the pre-existing flake.
> ⚠️ **Duplicates remain INVISIBLE** to the participation surface until Phase 1 replaces the read-side
> `GroupBy` collapse (scoped out by constraint). Task **017** edits the same file next and MUST NOT reduce
> the sweep back to a single row.
> Rationale: [`notes/task-010-grant-lifecycle.md`](../notes/task-010-grant-lifecycle.md).

> **Task 008 outcome (2026-08-22)**: A-6 closed — **FR-07's delegation gate is in place, so the PCF
> "+ User" button (task 065) is unblocked.** `AddDelegationRuleFilter()` on the `/api/v1/external-access`
> group enforces B-14 (Write on the target record, evaluated as the caller) across all SIX mutations.
> **Group-level, dispatching on the bound request TYPE with a default that DENIES** — a seventh route
> added later is gated from its first request rather than inheriting the hole. Route→target: grant /
> invite / invite-and-grant → the grant root; **revoke → the ROW's root, not the body's `projectId`**
> (checking the body would let a caller with Write on any project revoke grants on a matter they cannot
> touch); close/provision → the project.
> **The existing authorization path could NOT serve this.** `DataverseAccessDataSource` hard-codes
> `sprk_documents({id})` in BOTH its RPA target and its fallback probe, so it answers `None` for a
> project for EVERY caller — the filter would have denied universally. `IDataverseUserClient` is the
> right shape but is twice-gated (compound AI gate + `ToolFramework:Enabled`) → depending on it from
> six unconditional routes would be the §10 F.1 asymmetric-registration anti-pattern. Hence
> `CallerRecordAccessProbe`: OBO `WhoAmI()` → `RetrievePrincipalAccess`, entity-generic, fail-closed.
> **`WhoAmI` is not incidental** — RPA takes the principal as an ARGUMENT, so an app-only version would
> carry the caller's identity as *data* (a wrong id silently answers about the wrong person: the A-2
> shape). Under OBO the identity is the *credential*.
> **No read-probe fallback, deliberately**: a read proves Read, and Read is not licence to grant. So an
> RPA outage denies all six mutations rather than widening them — logged as `DELEGATION-RPA-UNAVAILABLE`,
> and **task 034 now owns live RPA verification for six mutation endpoints, not just the document read.**
> Both escalation triggers evaluated; **neither fired** — `provision-project`'s premise was false (the
> handler already requires the project to exist), and revoke's extra read duplicates one the handler
> already performs. ⚠️ **Residual owner decision**: provisioning creates a **business unit**; is
> Write-on-project the right gate, or should it need a privileged role?
> ⚠️ `/invite` now REQUIRES a resolvable root (it provisions a CIAM identity). The only first-party
> caller already sends `projectId` as required, so nothing breaks — but it is a contract narrowing.
> ⚠️ Two `ExternalAccessContractTests` needed an entitled caller in their fixture; the production rule
> was **not** weakened for them. ⚠️ Filed for triage: `EntityAccessFilter` passes
> `"{entityType}:{entityId}"` into that same document-only data source and can therefore only resolve
> `None` — the Office save path's entity check may be inert (same class as A-20, no Phase 0 owner).
> 🔔 **ADR-028 A4 needs an owner ruling**: a NEW `.WithClientSecret(...)` site (the 8th). E-3 covers
> transitional sites and does not license expansion; there is no `WithClientAssertion` anywhere in the
> repo, so complying would mean inventing MI-FIC plumbing inside an authorization filter. **Path A
> proposed.** ADR-003's "rules only / no new auth service layers" tension is the SAME one already routed
> to task 030's path-B amendment — cited, not re-escalated.
> Rationale: [`notes/task-008-delegation-rule.md`](../notes/task-008-delegation-rule.md).

> **Task 008 follow-ups (2026-08-23, owner-authorised)** — all three review items closed; details in
> [`notes/task-008-delegation-rule.md` §10](../notes/task-008-delegation-rule.md).
> **(a) `EntityAccessFilter` was inert — CONFIRMED, then FIXED.** The characterization suite was written
> and run FIRST against unchanged code: 7 of 9 failed, `ProbedTargets` empty, and a caller holding
> `AppendToAccess` on the target matter got **403**. `POST /api/office/save` carrying a `targetEntity`
> was refused for every caller — filing an email/document to a matter from the Outlook/Word add-in could
> not succeed, while filing WITHOUT a matter worked (the filter short-circuits on a null target), which
> is why it read as flakiness. Fixed by resolving the target's own collection and asking
> `CallerRecordAccessProbe`; `OperationAccessPolicy` still decides WHICH right the operation costs, so
> there is one policy and only the rights SOURCE changed. Perturbation: point it back at
> `sprk_documents` → 6 of 9 fail. Should fold back into `AuthorizationService` when **task 032**
> generalizes the seam (noted in code).
> **(b) `provision-project`: Write is correct; the real exposure was idempotency.** `Create` cannot be
> used — `CreateAccess` is an entity-level privilege, not a right held ON an existing record, so
> `RetrievePrincipalAccess` does not report it and requiring it would deny everyone. Write is also
> exactly what the endpoint needs: its final act is an `UpdateAsync` stamping three fields on the
> project. (The BU is created by the APP-ONLY identity, so the caller's own BU-create privilege is never
> consulted regardless.) The damage the question was worried about is now removed by a **409
> idempotency guard**: nothing on the path deduped — not the client, not the route (no
> `IdempotencyFilter`, unlike `/office/save`), not Dataverse — so a retry after a timeout-that-actually-
> succeeded created a second BU + container + account and repointed the project at empty infrastructure.
> EITHER stamped reference triggers the refusal, because half-provisioned is where a blind re-run does
> the most damage. Perturbation: disable the guard → 4 of 5 fail.
> **(c) Replication-lag 403 fixed.** The wizard provisions seconds after creating the project, so the
> delegation check could ask about an unreplicated record. `NotFoundRetryDelay` retries 400 ms then
> 1200 ms on not-found. ⚠️ Documented tradeoff: under OBO, Dataverse cannot distinguish "not replicated"
> from "you cannot see it", so this also slows genuine denials by ~1.6 s — acceptable only because these
> are low-volume admin mutations and a caller who CAN see the record gets a 200 (no retry). Pure
> `internal static` so it is testable without a transport mock (ban B1).
> **(d) `tests/integration/data-mutation/**` BACKFILLED** — it was the last of the seven ADR-038 KEEP
> paths with no csproj glob, so a write-path test placed there compiled nowhere and ran never. **All
> seven KEEP paths now compile.**
> **(e) ADR-028 A4 exception ACCEPTED** by the owner → recorded in [`design.md` §9](../design.md).

> **Task 007 outcome (2026-08-23)**: A-5 closed — `sprk_expiresdate` was written at grant time and read
> NOWHERE (no `$filter`, no `$select`, no sweep job), so an expired grant conferred full access forever
> while the UI presented expiry as a working control. Now enforced server-side on every conferring read.
> **Closed read-path list (acceptance criterion 5)** — 3 paths gained the predicate
> (`QueryGrantSetAsync`, `QueryOrganizationGrantRowsAsync`, and the `GetProjectContactIdsAsync` display
> list whose contract says "active access"); **2 paths deliberately did NOT** —
> `ExternalGrantLifecycle` (grant upsert + revoke sweep) and `ProjectClosureEndpoint`'s cascade. Adding
> expiry there would make **expired grants unrevokable**, skipping exactly the rows an operator is
> cleaning up. "Add it everywhere" was the obvious reading and would have introduced a new defect.
> ⚠️ **`sprk_expiresdate` is DATE ONLY** — verified against LIVE Dataverse metadata (the escalation
> trigger required checking, not trusting docs; the name matched so the trigger did not fire, but the
> TYPE was new information). Two consequences: the comparison must be a bare `yyyy-MM-dd` (a datetime
> literal risks a 400, and a 400 here returns an EMPTY grant set — a silent total access outage), and
> **`ge` not `gt`**, deviating from the POML's prescribed `gt {utcNow}`. `gt` against a date-only column
> kills a grant at 00:00 ON its own expiry date, silently shortening every dated grant in the system by
> a day; "access until 30 June" means 30 June works. FR-06's acceptance is about an expiry IN THE PAST,
> which `ge` satisfies.
> ⚠️ **The `eq null` branch is load-bearing**: OData `ge` excludes nulls, and most grants have no expiry
> — without it the predicate would revoke every open-ended grant, an outage rather than an expiry bug.
> Per task-001's obligation the query builders were EXTRACTED as pure `internal static` members first
> (task 001 could not pin A-5 because the queries were inline before `SendAsync`, and mocking transport
> is ban B1). Perturbations: drop the predicate → 2/11 fail; drop `eq null` → 1/11; `ge`→`gt` → 1/11;
> ungroup the org disjunction → 1/11 (without brackets the AND terms bind only to the LAST org and every
> other org's grants leak through). ⚠️ **Honest limit**: these assert the QUERY, not Dataverse's
> evaluation of it — end-to-end needs the tenant, filed on **task 034**.
> Rationale: [`notes/task-007-grant-expiry.md`](../notes/task-007-grant-expiry.md).

## Phase 1 — One evaluator (10 tasks)

| # | Task | FR | Deps | Group | Safe | Tier | Effort |
|---|---|---|---|---|---|---|---|
| 🔲 030 | ADR-003 amendment — two-surface authorization | FR-19 sanction | — | — | ❌ *main-session* | opus | high |
| 🔲 031 | ADR-028 A2 amendment — impersonated derivation | FR-20 sanction | — | — | ❌ *main-session* | opus | high |
| 🔲 032 | Evaluator spine — `(recordId→rights)` + max + veto seams | FR-19 | 030 | — | ❌ | **opus** | **xhigh** |
| 🔲 033 | Consumer propagation · **delete the `Collaborate` stamp** | FR-19 | 032 (+009 soft) | — | ❌ | opus | high |
| 🔲 034 | **Negative canary — NFR-04 merge gate** | FR-20 | — | **P1-A** | ✅ | sonnet | **xhigh** |
| 🔲 035 | `ImpersonatedRootSetSource` + per-user cache | FR-20 | 031 | — | ❌ | opus | high |
| 🔲 036 | Flag-gated swap + truncation + runbook | FR-20, NFR-02/03/04 | 032, **034**, 035 | — | ❌ | **opus** | **xhigh** |
| 🔲 037 | Restricted veto + Secure pre-max suppression | FR-21, FR-22 | 032 | — | ❌ | sonnet | high |
| 🔲 038 | Deny-list store — schema + fail-closed reader | FR-23 | — | **P1-A** | ✅ | sonnet | high |
| 🔲 039 | Deny veto wiring + ordered-pipeline tests | FR-23, FR-19 | 032,037,038 | — | ❌ | sonnet | high |

## Phase 2 — One definition of member (5 tasks)

| # | Task | FR | Deps | Group | Safe | Tier | Effort |
|---|---|---|---|---|---|---|---|
| 🔲 040 | ADR-034 amendment — registry first-class | FR-24 sanction | — | — | ❌ *main-session* | opus | high |
| 🔲 041 | Access-conferring column registry (contact **+ org**) | FR-24 | 040 | **P2-A** | ✅ | sonnet | high |
| 🔲 042 | Standing-grant baseline levels (contact + org) | FR-25 | 032 | — | ❌ | sonnet | high |
| 🔲 043 | Org-expansion term + fallback registry filter | FR-24/25/22 | 037,041,042 | — | ❌ | sonnet | high |
| 🔲 044 | Unified-evaluator seam suite (Phase 1–2 contract) | FR-19…25 | 039,041–043 | **P2-A** | ✅ | sonnet | high |

## Phase 3 — Child inheritance (9 tasks)

| # | Task | FR | Deps | Group | Safe | Tier | Effort |
|---|---|---|---|---|---|---|---|
| 🔲 050 | Core-ancestor derivation in the shared resolver | FR-26 | — | **P3-W1** | ✅ | **opus** | **xhigh** |
| 🔲 051 | `RegardingResolver` re-stamp on set/reparent/clear | FR-26 | 050 | **P3-W2** | ✅ | opus | high |
| 🔲 052 | Server-writer audit + C# `CoreAncestorResolver` | FR-26 | 050 soft | **P3-W1** | ✅ | sonnet | high |
| 🔲 053 | Ancestor-stamp backfill script | FR-26 | 050,052 | **P3-W2** | ✅ | sonnet | medium |
| 🔲 054 | Root-set generalization (`sprk_servicerequest` 4th root) | FR-27 | 032,035,036 | — | ❌ | sonnet | high |
| 🔲 055 | Evaluator child-inheritance term | FR-27 | 054,032,037,038 | — | ❌ | sonnet | high |
| 🔲 056 | Child-module registration (todo/event/communication) | FR-27 | 055,**009**,**018** | — | ❌ | sonnet | high |
| 🔲 057 | Phase-3 seam tests | FR-26/27 | 052,055,056 | **P3-W6** | ✅ | sonnet | high |
| 🔲 058 | Taxonomy + inheritance docs (**Matter ≠ Project**) | FR-26/27 | 056 | **P3-W6** | ✅ | sonnet | medium |

## Phase 4 — Secure Project · Manage Access · wizard (10 tasks)

| # | Task | FR | Deps | Group | Safe | Tier | Effort |
|---|---|---|---|---|---|---|---|
| 🔲 060 | POA seam consolidation (2→1, +revoke) | FR-28/29 pre | **010** | — | ❌ | **opus** | **xhigh** |
| 🔲 061 | Secure provisioning rework — svc-acct owner, share-only | FR-28 | 060,**008** | **P4-W2** | ❌ | sonnet | high |
| 🔲 062 | **NFR-05 role-depth standing assertion** | FR-28 | 034 | **P4-W2** | ✅ | sonnet | high |
| 🔲 063 | Internal system-user share endpoints (delegation-gated) | FR-29 | 060,**008**,010 | **P4-W3** | ❌ | sonnet | high |
| 🔲 064 | Provenance read + deny-list endpoints | FR-30, FR-23 | 063,060,032,038,041,042 | **P4-W4** | ❌ | sonnet | high |
| 🔲 065 | `AccessGrantModal` "+ User" picker | FR-29 | **063** | **P4-W4** | ✅ | sonnet | high |
| 🔲 066 | Modal provenance rows + suppressed rendering | FR-30 | 064,065 | — | ❌ | sonnet | high |
| 🔲 067 | Modal deny-list UI + standing-grant levels | FR-23/25 UI | 064,066 | — | ❌ | sonnet | high |
| 🔲 068 | Wizard Secure step + copy fixes (Power Pages) | FR-31 | 061 | **P4-W3** | ✅ | sonnet | medium |
| 🔲 069 | Phase-4 seam tests | FR-28/29/30 | 061,063,064 | — | ✅ | sonnet | high |

## Phase 5 — Attestation (4 tasks)

| # | Task | FR | Deps | Group | Safe | Tier | Effort |
|---|---|---|---|---|---|---|---|
| 🔲 080 | `sprk_accessevent` schema + data-model doc | FR-32 | 032,038 | — | ✅ | sonnet | medium |
| 🔲 081 | Append hooks at every grant/deny choke point | FR-32 | 080,060,063,064 | — | ❌ | sonnet | high |
| 🔲 082 | Evaluator versioning + point-in-time replay | FR-32 | 081,032 | — | ❌ | sonnet | **xhigh** |
| 🔲 083 | Attestation seam tests + docs | FR-32 | 081,082 | — | ✅ | sonnet | medium |

## Wrap-up

| # | Task | Deps | Safe |
|---|---|---|---|
| 🔲 090 | `/test-diet` · H-8a/H-8b closeout · lessons-learned · README → Complete | all | ❌ |

---

## Parallel execution groups

| Group | Tasks | Prerequisite | File-disjointness |
|---|---|---|---|
| **P0-W0** | 001 | — | Tests only (`tests/AccessControl/**`, `Spaarke.Core.Tests/Auth/**`). Blocks all Phase 0 code work |
| **P0-B** | 014, 019 | 001 | `Infrastructure/Caching/CachedAccessDataSource.cs` vs `Services/Ai/Nodes/LookupUserMembershipNodeExecutor.cs` — the only two Phase 0 code tasks outside all four contended directories |
| **P1-A** | 034, 038 | — | `tests/integration/auth/**` (new) vs new deny-list schema/reader + an append-only DI block |
| **P2-A** | 041, 044 | 040 / 039 | `Services/Ai/Membership/**` vs `tests/integration/seam` (new files only) |
| **P3-W1** | 050, 052 | — | TS shared lib (`Spaarke.UI.Components/src/services/`) vs C# `Services/Communication/**` |
| **P3-W2** | 051, 053 | 050, 052 | `RegardingResolver` PCF vs `scripts/` |
| **P3-W6** | 057, 058 | 055, 056 | `tests/integration/seam` vs `docs/architecture` |
| **P4-W2** | 061, 062 | 060, 008, 034 | `src/**` provisioning vs `tests/integration` |
| **P4-W3** | 063, 068 | 060, 008, 061 | `Api/ExternalAccess/**` vs `CreateProjectWizard/**` |
| **P4-W4** | 064, 065 | 063 | `Api/ExternalAccess/**` + Infrastructure vs `AccessGrantModal` + `TrackingFieldTrio` |

**Max concurrency 6 agents/wave.** Build verification between waves is mandatory: `dotnet build src/server/api/Sprk.Bff.Api/` after any `.cs` change; `npm run build:prod` for PCF (**never** `npm run build` — root CLAUDE.md §12).

### Honest note on parallelism

**Phase 0 barely parallelizes**, and that is a property of the codebase, not a planning shortcut. Seventeen of its nineteen tasks cluster in four contended directories — `Api/ExternalAccess/**`, `Infrastructure/ExternalAccess/**`, `Spaarke.Core/Auth/**`, `Spaarke.Dataverse/DataverseWebApiService.cs`. Only `{014, 019}` are genuinely file-disjoint. Task 006 is disjoint and safe but has no co-schedulable partner in its wave.

Two agents editing an authorization path concurrently produces a silent merge mess, so these run in dependency-ordered waves and merge serially with `/conflict-check`. Phases 3 and 4 parallelize better because PCF, scripts, docs and BFF work are genuinely separable.

### Cross-phase collision audit (verified 2026-08-21, not assumed)

| Potential collision | Verdict |
|---|---|
| 044 / 057 / 069 / 083 all write `tests/integration/seam` and are all `safe:true` | **Benign** — serialized by phase dependencies, and each creates distinct files. Preserve this ordering if phases are ever resequenced |
| 015 (P0) and 041 (P2) both touch `MembershipResolverService.cs` | **Safe** — 015 is `parallel-safe:false`, so it never co-runs |
| 038 (`safe:true`) shares `ExternalAccessModule.cs` with 035/036/042/056 | **Safe** — those are all `safe:false`; 038's only partner is 034 (tests-only) |
| 065 touches `TrackingFieldTrio`; 050 touches `Spaarke.UI.Components` | **Disjoint** — `components/AccessGrantModal/` vs `src/services/`; no Phase 0 task touches either |
| 065 / 066 / 067 all edit `AccessGrantModal.tsx` | **Serialized** by the 065→066→067 dependency chain |

## Escalation triggers (legitimate stops, not failures)

Tasks carrying `<escalation><trigger>` for genuine judgment boundaries — a task that stops here is behaving correctly (root CLAUDE.md §6 / §6.5):

- **018** — spec FR-17's "bound the in-clause per FR-25" cross-reference is ambiguous (FR-25 is Phase 2); does not guess
- **042** — default when a subject's `sprk_accesspermissiongrant` baseline is empty but the standing flag is set
- **043** — level for a non-standing derived term ("default-on" names no level source)
- **037** — matter / work-assignment lack `sprk_issecure` / `sprk_accesspermission` columns
- **038** — deny-list subgrid storage shape

## Deferred / out of scope

**FR-18 (BU restructure) has no tasks** — reclassified to UAT/environment work. Tasks 061/062 fail closed when the topology is unconfigured and loud-skip pre-UAT; live-dev acceptance is recorded in `notes/phase4-uat-acceptance.md`. Also out: AI-search trimming for contacts (A-21), field-level visibility, break-glass, organization-hierarchy cascade, GDPR erasure.
