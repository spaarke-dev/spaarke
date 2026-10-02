# Task 137 (#1060) — C5 soft revocation: read guards and invalidation

**Status:** code complete; **two parts stopped on escalation** (reconciliation posture = owner decision; manual live
gate = needs live writes). Branch `task/uac-r2-137` (parents: `task/uac-r2-139-r1` + `task/uac-r2-141-f3`).
r1 fix round on `task/uac-r2-137-r1` (2026-10-02): verifier findings closed or recorded — §2 (job-scan before-state),
§6 (posture still pending after the relayed answers), §9 (observations, new tests, perturbations).

## 1. Ownership map (closes the "partially tracked" finding)

| Part | Owner | Evidence |
|---|---|---|
| Inactive contact — sign-in denial (C7, D3) | **task 141** (implemented), verified here | `CiamContactBindingTests.AnOidBoundToAnInactiveContact_Denies_EvenWhenAnActiveContactSharesTheEmail_NoEmailReadNoBindNoCreate`; `WorkforceEmailNoHijackTests.AnOidOnlyOnAnInactiveContact_DeniesWithItsOwnCode_AndNothingIsReCreated` (task-137 assertions added: no email read, the active contact sharing the email is never bound); strategy maps the code to 403 — `CallerPrincipalResolverTests.CiamStrategy_EachDeny_CarriesItsOwnReasonCode("inactive")` |
| Inactive contact — after sign-in (live, per composition) | **here** | `AccessibleRecordSetService.ComposeContactPlaneAsync` (both contact planes) + `ComposeForSystemUserAsync` (linked contact) over `ExternalParticipationService.ReadContactStateAsync` |
| Inactive root (project / matter / work assignment) | **here** | `RootRecordFlags.IsInactive` from `statecode` in the existing batched flag read; `ApplyVetoPipeline` Restricted slot (`RemovesContactSourcedAccess`) |
| CIAM-tenant invalidation miss | **here** | `ExternalParticipationService.InvalidateGrantSetsAsync` + `GrantCacheTenantIds` |
| Organization-grant invalidation (grant, revoke, close, expire) | **here** | same routine, organization → ACTIVE members paged to completion |
| #1006 / ISS-026 read half (inactive organization) | task 109 (merged) — verified on BOTH planes here | `OrganizationMembershipReadTests.Task109Guards_OnBothPlanes_ConferOnlyThroughCurrentMembershipsOfActiveOrganizations(ciam|workforce)` |
| #1006 write half (R2 deactivation) | task 117 (completed, shipped disabled) | posture → §6 (owner decision pending) |
| #999 / ISS-020 end date, D-10 start date | task 109 (implementation), task 110 (closure) — verified on BOTH planes here | same theory (ended + not-yet-started memberships confer nothing on CIAM and workforce); wall still binds a former member on CIAM: `Task109Guards_OnTheCiamPlane_TheOrgKeyedWallStillBindsAFormerMember(ended|not-yet-started|inactive-organization)`; workforce: the existing `ComposeAsync_OrgKeyedDenyRow_StillMatchesAMemberWhoseMembershipNoLongerConfers` |

## 2. Before-state (dev `spaarkedev1`, READ-ONLY, 2026-10-02, Dataverse MCP SQL)

- Active `sprk_externalrecordaccess`: **31** — 27 contact grants (contacts: Ralph Schroeder ×2 contact rows, CIAM Test
  User, Eyal Iffergan, Jane Doe, John James Murphy, Sarah Chen, Test User 1) and 4 organization grants (all organization
  `67577f8c-4301-f111-8407-7ced8d1dc988`).
- Active grants whose **contact is inactive: 0** (every grantee contact `statecode = 0`).
- Active grants whose **root is inactive: 0** (joined to `sprk_matter`, `sprk_project`, `sprk_workassignment` with
  `statecode = 1`: no rows on any).
- Report-only R1/R2/R3 (computed with the rules' own predicates — the job itself is registered disabled and cannot be run
  read-only from here):
  - R1 (active grant, no `sprk_expiresdate`): **0**
  - R2 (active grant under an inactive `sprk_organization`): **0**
  - R3 (active `sprk_contactorganization` past `sprk_enddate`): **0** — 2 active memberships, both `sprk_enddate` null
    (CIAM Test User → `67577f8c…` from 2026-08-12; Ralph Schroeder → `a5999fcc…`).
- Systemusers with a linked contact (`sprk_primarycontact`): 1 (Ralph Schroeder → contact `8e9918a9…`, **active**) —
  escalation trigger 6 (an inactive contact bound to a current systemuser) does not fire, and by construction the
  guard never touches a systemuser's own membership.

**Access this task removes in dev today: none.** Escalation trigger 2 (real access removed) does not fire.

**r1 (verifier finding 7) — re-taken with the JOB'S OWN SCAN, 2026-10-02, still read-only.** Step 1 prescribes a
report-only run of `ExternalAccessReconciliationJob`. That run is not read-only end to end: the manual dispatch
(`POST /api/admin/jobs/external-access-reconciliation/trigger`, `SystemAdmin`) writes a `sprk_backgroundjobrun` row, and
it needs this branch deployed — so it stays a **pending manual gate** (below). What CAN be done read-only was redone with
the job's exact scan shape instead of the rules' paraphrase: `BuildGrantScanFetchXml` = active-or-NULL statecode AND
(`sprk_expiresdate` null OR `sprk_organization` not null), OUTER join to `sprk_organization.statecode`;
`BuildMembershipScanFetchXml` = active-or-NULL statecode AND `sprk_enddate lt '2026-10-02'`. Then each returned row was
classified by `PlanGrantChange` / `PlanMembershipChange` by hand (R2 before R1).

| Job scan | Scanned | R1 planned | R2 planned | R3 planned |
|---|---|---|---|---|
| grants (R1 + R2) | **5** — all five carry an organization (`6f54531a`, `0452ab4b` [also a contact: `394fda9f`, the firm recorded beside the person], `ec54e576`, `1967189b`, `9aed8ab9`), every one has `sprk_expiresdate` 2026-12-10 and organization `67577f8c…` statecode **0** | **0** | **0** | — |
| memberships (R3) | **0** | — | — | **0** |

Same answer as the first pass (R1 = R2 = R3 = 0). Correction to the line above: 31 active grants = 26 contact-only + 5
carrying an organization, of which one (`0452ab4b`) also names a contact — the earlier "27 + 4" counted that row as a
contact grant.

Pending manual gate (main session, after the dev deploy; writes ONE run-history row, no data change while
`ExternalAccess:Reconciliation:WritesEnabled` is absent):

```bash
TOKEN=$(az account get-access-token --resource api://<bff-app-id> --query accessToken -o tsv)   # a SystemAdmin caller
curl -s -X POST -H "Authorization: Bearer $TOKEN" https://<dev-bff-host>/api/admin/jobs/external-access-reconciliation/trigger
curl -s -H "Authorization: Bearer $TOKEN" https://<dev-bff-host>/api/admin/jobs/external-access-reconciliation/status
# expect RecentRuns[0].ResultJson: mode "report-only", writesEnabled false, R1/R2/R3 planned 0, changed 0
```

## 3. What changed

- **Inactive contact, live.** `ExternalParticipationService.ReadContactStateAsync` reads `contacts({id})?$select=statecode`
  app-only, never cached across requests (remembered in `HttpContext.Items` for the rest of ONE request so the CIAM
  principal's three compositions read it once; a fault is not remembered). Only `statecode 0` is Active; 404 → Inactive;
  any other failure or a null state → Unreadable. `ComposeContactPlaneAsync` reads it FIRST and returns the empty set for
  anything but Active (so a deactivated contact costs no grant read, walk or flag read). `ComposeForSystemUserAsync`
  reads it only when the linked contact's grants could contribute (NFR-02), drops the grant term when not Active, keeps
  the contact as the deny-veto subject, and leaves the membership term alone.
- **Inactive root.** `statecode` joins `sprk_issecure,sprk_accesspermission` in the batched flag read
  (`RootFlagColumns`); `FlagsFrom(..., stateCode)` maps non-zero OR null to `IsInactive` (fail closed);
  `RootRecordFlags.Unreadable` now also carries `IsInactive`. `ApplyVetoPipeline`'s Restricted slot keys on
  `RemovesContactSourcedAccess` (Restricted OR inactive) — same survivor rule.
- **One invalidation routine.** `ExternalParticipationService.InvalidateGrantSetsAsync(contactIds, organizationIds)`:
  expands each organization to its ACTIVE members (same filter as the revoke path's SPE sweep,
  `ExternalOrganizationMembership.ActiveMembersFilter`; `@odata.nextLink` followed, 500 per page, backstop 200 pages;
  an incomplete walk is logged at warning with the count and the 60 s bound), then removes the key under every tenant
  (`GrantCacheTenantIds`: request `tid`, explicit, `Ciam:TenantId`, `AzureAd:TenantId`, every
  `WorkforceIdentity:CustomerTenantIds`, plus the lower-case "D" form of any GUID). Never throws (each removal and each
  page is caught; the whole body is wrapped). `/grant` + `/invite-and-grant` (through `CreateGrantAsync`), `/revoke`,
  `/close-project`, `/set-record-share-expiry` all call it; their private `cache.RemoveAsync` copies, `ExtractTenantId`
  copies and `ITenantCache` parameters are deleted. `/revoke` now keys invalidation on the REVOKED ROW's grantee (works
  with no `ContactId` on the request). The pre-existing `InvalidateAsync(contactId, tenantId)` delegates to it.
- **File move:** `ExternalOrganizationMembership` + `OrganizationMemberSet` moved unchanged from
  `Api/ExternalAccess/RevokeExternalAccessEndpoint.cs` to `Infrastructure/ExternalAccess/ExternalOrganizationMembership.cs`
  (Infrastructure must not reference the Api namespace; the type's own remarks already called this a pure file move).
- **ADR-009 tension avoided (path C, comply):** the cache key shape is unchanged (no tenant-agnostic key, no
  `CacheVersion` bump) — removal under every configured tenant instead. Escalation trigger 4 does not fire.

## 4. Merge of the parents (recorded because it changed behaviour)

`task/uac-r2-141-f3` into `task/uac-r2-139-r1` conflicted in both invite endpoints and the contract-test stub. 139's
read-only pre-onboarding contact match (raw top-1 query over contacts of ANY state) was replaced by
`ContactIdentityBinder.PeekInviteContactAsync` — 141's active-contact invite decision minus the collision-flag writes. A
refused email reads as "no existing contact" so onboarding still owns the 409 and its flag; an unreadable lookup answers
141's 503 + reason code. Pinned by two new `GrantorCeilingTests` (both seeded and seen red). Merge commit `6300dbbae`.

## 5. Placement + justification (CLAUDE.md §10 / §11)

Placement: in the BFF — every change extends the existing authorization read path (`ExternalParticipationService`,
`AccessibleRecordSetService`) and the existing grant-write endpoints (`.claude/constraints/bff-extensions.md`: it is
request-path authorization and cache hygiene of a BFF-owned cache; no AI types, no new package, no new endpoint, no new
DI registration, no background work).

New members (no new service / registration / endpoint / option / job / package):

- `InvalidateGrantSetsAsync` — *Existing:* `InvalidateAsync` (single tenant, single contact, no callers in `src/`) and
  four endpoint-private `RemoveAsync` copies. *Extension:* yes — this IS the extension; `InvalidateAsync` now delegates to
  it and the copies are deleted. *Cost of doing nothing:* a revoked CIAM contact keeps cached access for 60 s; an
  organization revoke clears no member.
- `ReadContactStateAsync` / `QueryContactStateAsync` — *Existing:* `ContactIdentityStore.GetContactAsync` (reads the row
  incl. the field-secured binding) and `SubjectStandingGrantReader.ReadForContactAsync` (workforce standing term only).
  *Extension:* the standing reader is not called on CIAM and may be retired (task 142 (d)); the identity store would add
  a fifth evaluator dependency and put the binding read on every authorization composition. One `$select=statecode`
  read on the grant-data service the evaluator already holds is smaller. *Cost of doing nothing:* a contact deactivated
  after sign-in keeps every grant for up to 10 minutes (workforce identity cache) and indefinitely while its token lives
  (no state check anywhere on the read path).
- `ReadOrganizationMemberPageAsync` — *Existing:* `ExternalOrganizationMembership.QueryActiveMembersAsync` (bounded 200,
  `DataverseWebApiClient.QueryAsync` discards `@odata.nextLink`). *Extension:* reuses its filter; the bounded reader
  stays for the SPE sweep, whose refusal-over-bound semantics differ. *Cost of doing nothing:* an organization over 200
  members is silently skipped (the old share-expiry behaviour).
- `RootRecordFlags.IsInactive` / `RemovesContactSourcedAccess`, `RootFlagColumns`, `ContactRecordState`,
  `GrantCacheInvalidation`, `OrganizationMemberWalk`/`Page`, `ContactIdentityBinder.PeekInviteContactAsync` (merge) —
  small types/members carrying the above.

Complexity (§11.5): `ExternalParticipationService` grows by ~300 lines but stays one reason to change — the external
grant DATA (read, cache, invalidate); `AccessibleRecordSetService` +~45.

## 6. Reconciliation posture — ⏳ ESCALATED (owner decision pending)

Not answered by owner rounds 1–4 (round 4 item 6's "reconciliation job" is task 141's identity-link job). Registration
(`enabled: false`) and `ExternalAccess:Reconciliation:WritesEnabled` (absent → false) are **unchanged**.
Recommendation: inactive contacts and inactive roots stay **read guards only** (no writer rules — reactivation then
restores access with no repair); enable the schedule in **report-only** first; enable writes only after the owner has
reviewed one report. With today's counts (§2: R1 = R2 = R3 = 0) enabling report-only changes nothing in dev.

**r1 (2026-10-02): still pending.** The answers relayed with the r1 run ("1. let's not worry about these users … 7. yes")
are owner round 4 item for item (root-BU users; provision `65a3fab2`; assign cascade; B2; 155 (b); "yes can run it" for
141/144/145's live steps; `showViewSelector=false`), already recorded in `session27-owner-decisions-and-research.md`.
None of the seven names `ExternalAccessReconciliationJob`, its schedule, or `ExternalAccess:Reconciliation:WritesEnabled`;
item 6's approval is scoped to 141/144/145, and 141's "reconciliation job" is `IdentityLinkReconciliationJob`. So
escalation trigger 1 stays a first-class stop: the registration (`enabled: false`) and the key (absent) are unchanged,
and the question to the owner is unchanged — *"External-access reconciliation job: (a) keep it disabled; (b) enable the
daily schedule report-only; (c) enable report-only, then writes after one reviewed report?"* (recommended: c). When
answered, record it verbatim here and in DEPLOY-CHECKLIST §4.1 and change `ExternalAccessModule`'s registration and the
key together in one commit.

## 7. Tests (ADR-038 KEEP paths; no `Mock<HttpMessageHandler>`, no DI-registration or ctor null-check tests)

New: `OrganizationMembershipReadTests` task-137 section (real transport: 109 guards on both planes, CIAM wall binds a
former member, inactive contact after sign-in with WARM grant cache, contact-state fault, inactive project +
reactivation, `statecode` in the flag `$select`); `UnifiedEvaluatorSeamTests` task-137 section (inactive root per root
type on CIAM / workforce contact / systemuser survivor, reactivation, unreadable root, inactive/unreadable/throwing
contact on both planes, inactive linked contact of a systemuser, the check does not ride the standing reader);
`GrantLifecycleCharacterizationTests` task-137 section (revoke clears the CIAM-tenant entry over a real `TenantCache`,
org revoke past 200 members paged, invalidation failure leaves the response unchanged, `/grant` asks for exactly the
reached grantees); `ProjectClosureCascadeTests.CloseProject_WithAnOrganizationGrant_InvalidatesEveryActiveMember` (205
members); `ExternalParticipationServiceInvalidationTests` (tenant enumeration, page fault mid-walk, state mapping,
once-per-request read); `PolymorphicGrantWriteTests.FlagsFrom_TheRootsOwnStateCode_…`; 141 verification assertions
(§1). Existing tests re-pointed: every grant-write handler call passes a participation double instead of
`ITenantCache`; ProjectClosure/RecordShareExpiry/contract tests run the REAL routine over their cache.

Perturbations (each seeded, seen red, restored, files touched):

| Seed | Red |
|---|---|
| contact-state check removed (contact plane) | 8 (seam ×4, transport ×4) |
| linked-contact check removed (systemuser plane) | `InactiveLinkedContact_OfASystemUser_…` |
| `statecode` removed from the flag read | `FlagsFrom_TheRootsOwnStateCode_…` ×3, `InactiveProject_…` ×2 |
| `Ciam:TenantId` dropped from the tenant set | `Revoke_ByAWorkforceAdmin_ClearsTheGranteesCiamTenantEntry…`, `GrantCacheTenantIds_…` |
| organization fan-out removed | revoke-org, close-org, share-expiry members, page-fault (4) |
| 141: inactive oid falls through to email | `CiamContactBindingTests` ×2, `WorkforceEmailNoHijackTests` ×1 |

Results (2026-10-02, after the Step 9.5 fix): full BFF unit suite **13,977 passed / 0 failed / 54 skipped**
(14,031); NetArchTest **345 / 0 / 0**; `Sprk.Bff.Api.IntegrationTests` builds. No package changes (no CVE delta).
Publish size not measured (instructed; main session measures).

Step 9.5 (code-review + adr-check): one fix — key removals were sequential; a large organization × several tenants would
hold the write's response, so they now run with bounded parallelism (16); `RecordShareExpiryTests` collected removals in a
`List` from a Moq callback and now uses a concurrent queue. Accepted: one extra live GET per request for contact
compositions (memoized per request); granting on an INACTIVE root is still allowed at write time (the grant confers
nothing until reactivation — read-time rule by design; worth noting for tasks 140/142). No ADR violation (ADR-003
fail-closed everywhere new; ADR-009 key unchanged; ADR-010 no registration; ADR-038 real TestServer transport and real
`TenantCache`, no `Mock<HttpMessageHandler>`).

`check-task-status-drift.ps1 -Project unified-access-control-r2` reports 137 (and 138, 139) as "INDEX is behind" —
expected: TASK-INDEX is updated by the main session, not by this task.

Test-scope note: the `GrantCacheTenantIds` lower-case duplicate and the once-per-request memo tests go one step past the
named contract — both pin behaviour a wrong edit would silently break (a case-mismatched config tenant clears nothing; a
cross-request memo would hide a deactivation).

## 8. Manual live gate — ⏳ PENDING (needs live writes; not run by this task)

Steps and records: `DEPLOY-CHECKLIST.md` §4.2. Evidence to be appended here by the main session.

## 9. r1 — verifier findings and observations (2026-10-02, branch `task/uac-r2-137-r1`)

- **TenantRouting tenants are not in the removal set (finding 9).** `GrantCacheTenantIds` enumerates the request `tid`,
  an explicit tenant, `Ciam:TenantId`, `AzureAd:TenantId` and `WorkforceIdentity:CustomerTenantIds` — the sets the POML
  named. It does NOT enumerate `TenantRouting:Tenants[]`. A workforce caller whose `tid` is accepted only through
  TenantRouting (neither `AzureAd:TenantId` nor a CustomerTenantId — possible for an existing oid binding, which 141
  still resolves) caches its grant set under that tid; a write made from ANOTHER tenant does not clear it, so that entry
  waits out the 60-second TTL. A write made from that same tenant clears it (request `tid`). Within the owner's R3/R4
  "minutes" bound; not a criterion failure. Closing it means adding the TenantRouting tenant ids to `GrantCacheTenantIds`
  — a one-line follow-up if a deployment ever routes a customer tenant only through TenantRouting.
- **A partial revoke skips invalidation (finding 10).** When `/revoke`'s Step 1 fails part-way it returns 500 before
  Step 3, so rows already deactivated keep a stale cache entry for up to 60 s. Unchanged from before this task; the
  client is told the revoke failed and retries; accepted.
- **For tasks 140 and 142 (finding 11).** Granting on an INACTIVE root is still allowed at write time — the read-time
  rule makes the grant confer nothing until the root is reactivated. 140 (contact-side grant) and 142 (Assigned-To
  auto-grants) inherit this: neither should treat a 200 from the grant core as "the grantee can now see the record" when
  the root is inactive. 140's invalidation must go through `InvalidateGrantSetsAsync` (already the only route).
- **Doc fixes.** `ProjectClosureCascadeTests`: the closure-cache test's `<summary>` was stranded above the new
  organization test (two summaries on one, none on the other) — moved back. `task-139-grant-model.md` lines 19 and 81 named
  the `FindContactByEmailAsync` that merge `6300dbbae` removed — now name `ContactIdentityBinder.PeekInviteContactAsync`.
- **New tests (r1).** `WorkforceEmailNoHijackTests.AnOidOnlyOnAnInactiveContact_ThroughTheWorkforceStrategy_Is403WithTheContactInactiveCode`
  (criterion 1 on the WORKFORCE plane at the HTTP boundary: real resolver → real `WorkforcePrincipalStrategy` → executed
  ProblemDetails is 403 with `sdap.access.deny.contact_inactive`, never `principal_not_resolved`, evaluator Strict and
  untouched); `GrantLifecycleCharacterizationTests.Grant_OfAnOrganizationGrant_ClearsEveryMember_EvenPastTheTwoHundredMemberBound`
  (/grant through the real core and the REAL routine over the production `TenantCache`, 251 members in 3 pages);
  `RecordShareExpiryTests.SetShareExpiry_OnAnOrganizationOverTheTwoHundredMemberBound_InvalidatesEveryActiveMember`
  (251 active members from the fake junction by the production filter, former member untouched, 3 pages). The
  `MemberPagingParticipationService` double gained an opt-in `RootFlags` answer so the /grant policy check runs without
  Dataverse; unset, the production read is unchanged.
- **Perturbations (r1)**, each seeded, seen red, restored, file touched:

| Seed | Red |
|---|---|
| `WorkforcePrincipalStrategy` maps every deny to `principal_not_resolved` | `…ThroughTheWorkforceStrategy_Is403WithTheContactInactiveCode` |
| `/grant` passes no organization to the routine | `Grant_OfAnOrganizationGrant_ClearsEveryMember…`, `Grant_InvalidatesTheOrganizationOnAnOrgGrant…` |
| member walk stops after page 1 (the silent cap) | `Grant_OfAnOrganizationGrant…`, `SetShareExpiry_OnAnOrganizationOverTheTwoHundredMemberBound…`, `Revoke_OfAnOrganizationGrant…` |

Results (r1, 2026-10-02): affected classes (ProjectClosureCascade, WorkforceEmailNoHijack, GrantLifecycleCharacterization,
RecordShareExpiry, CallerPrincipalResolver) **149 / 0 / 0**; full BFF unit suite **13,980 passed / 0 failed / 54 skipped**
(14,034; +3 new); NetArchTest **345 / 0 / 0**. No `src/` change, no package change. Publish size not measured (main session).
