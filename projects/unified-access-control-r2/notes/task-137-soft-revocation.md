# Task 137 (#1060) — C5 soft revocation: read guards and invalidation

**Status:** code complete; reconciliation posture **DECIDED** (owner round 7 item 1, applied in r3 — §6, §11); the
manual live gate, the real report-only job run and publish size remain pending (need the dev deploy / main session). Branch `task/uac-r2-137` (parents: `task/uac-r2-139-r1` + `task/uac-r2-141-f3`).
r1 fix round on `task/uac-r2-137-r1` (2026-10-02): verifier findings closed or recorded — §2 (job-scan before-state),
§6 (posture still pending after the relayed answers), §9 (observations, new tests, perturbations).
r2 fix round on `task/uac-r2-137-r1-r2` (2026-10-02): the production member-page read driven over the transport, the
junction guard widened to see it, a stale test reference fixed — §10; posture still pending — §6.
r3 fix round (2026-10-02). Begun on `task/uac-r2-137-b1`, which a usage limit cut off; that WIP commit was reviewed
and is UNVERIFIED. Finished and verified on `task/uac-r2-137-b2`, after merging `work/unified-access-control-r2` (merge
`adcfc03ac`, §12). Owner round 7 item 1 is applied: the schedule is ON and report-only. The three remaining verifier
findings are closed in tests: a 404 confers nothing, a returned Unreadable is not memoised, and a failed revoke with a
failed invalidation keeps its ProblemDetails. See §6, §11 and §12.

## 1. Ownership map (closes the "partially tracked" finding)

| Part | Owner | Evidence |
|---|---|---|
| Inactive contact — sign-in denial (C7, D3) | **task 141** (implemented), verified here | `CiamContactBindingTests.AnOidBoundToAnInactiveContact_Denies_EvenWhenAnActiveContactSharesTheEmail_NoEmailReadNoBindNoCreate`; `WorkforceEmailNoHijackTests.AnOidOnlyOnAnInactiveContact_DeniesWithItsOwnCode_AndNothingIsReCreated` (task-137 assertions added: no email read, the active contact sharing the email is never bound); strategy maps the code to 403 — `CallerPrincipalResolverTests.CiamStrategy_EachDeny_CarriesItsOwnReasonCode("inactive")` |
| Inactive contact — after sign-in (live, per composition) | **here** | `AccessibleRecordSetService.ComposeContactPlaneAsync` (both contact planes) + `ComposeForSystemUserAsync` (linked contact) over `ExternalParticipationService.ReadContactStateAsync` |
| Inactive root (project / matter / work assignment) | **here** | `RootRecordFlags.IsInactive` from `statecode` in the existing batched flag read; `ApplyVetoPipeline` Restricted slot (`RemovesContactSourcedAccess`) |
| CIAM-tenant invalidation miss | **here** | `ExternalParticipationService.InvalidateGrantSetsAsync` + `GrantCacheTenantIds` |
| Organization-grant invalidation (grant, revoke, close, expire) | **here** | same routine, organization → ACTIVE members paged to completion |
| #1006 / ISS-026 read half (inactive organization) | task 109 (merged) — verified on BOTH planes here | `OrganizationMembershipReadTests.Task109Guards_OnBothPlanes_ConferOnlyThroughCurrentMembershipsOfActiveOrganizations(ciam|workforce)` |
| #1006 write half (R2 deactivation) | task 117 (completed) | posture DECIDED (owner round 7 item 1): scheduled daily, report-only; writes after one reviewed report — §6 |
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

> **Correction (r3, 2026-10-02):** the trigger does NOT write a `sprk_backgroundjobrun` row. The BFF's job store is
> `InMemoryBackgroundJobStore` (`SchedulingModule`), so the run record is process-local and the report is the
> `[EXT-ACCESS-RECON]` heartbeat and before-state log lines (DEPLOY-CHECKLIST §4.1). A report-only run writes no
> Dataverse data. It is still a live gate, but only because it needs the deploy and a live call into dev.

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

> **Superseded at merge `adcfc03ac` (r3, §12).** The integrated task 141 on `work/unified-access-control-r2` resolves
> the invitee ONCE with `ContactIdentityBinder.ResolveInviteContactAsync` and hands that resolution to onboarding. An
> ambiguous email, a contact owned by another sign-in, or an unreadable binding is a 409 refusal BEFORE the grant checks.
> The checks never see it as "no contact yet". `PeekInviteContactAsync` and its two tests are gone: the merge took
> 141's endpoint and tests, and the binder method had no caller left, so it was removed with them.

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
  `GrantCacheInvalidation`, `OrganizationMemberWalk`/`Page` — small types/members carrying the above.
  (`ContactIdentityBinder.PeekInviteContactAsync`, added at merge `6300dbbae`, was removed at merge `adcfc03ac` — §4.)

Complexity (§11.5): `ExternalParticipationService` grows by ~300 lines but stays one reason to change — the external
grant DATA (read, cache, invalidate); `AccessibleRecordSetService` +~45.

## 6. Reconciliation posture — ✅ DECIDED (owner round 7 item 1, 2026-10-02; applied in r3)

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

**r2 (2026-10-02): still pending — unchanged.** The answers relayed with r2 are the same seven (owner round 4); rounds 5
and 6 do not address it either. One input for the owner when answering (verifier r2): R3/R4 says the "background job is
only a safety net, running at ≤ 5 min". That sentence is scoped to tasks 142/143, but it bears on (b)/(c): this job's
current schedule is daily (`0 5 * * *`), so if the owner wants it to be the ≤ 5-minute safety net here too, the answer
should name the schedule as well as the mode.

**r3 (2026-10-02): DECIDED.** Owner round 7 item 1, verbatim ("follow recommended" — option (c); recorded in
`session27-owner-decisions-and-research.md`):

> - Enable the schedule in **report-only** mode now. Enable writes only after the owner has reviewed one report.
> - Inactive contacts and inactive roots stay READ guards only, so reactivating one restores access with no data repair.

Applied in one change (begun on `task/uac-r2-137-b1`, verified and committed on `task/uac-r2-137-b2`):

| Surface | Before | After |
|---|---|---|
| `ExternalAccessModule` registration | `AddScheduledJob<ExternalAccessReconciliationJob>(DefaultCronSchedule, enabled: false)` | `AddScheduledJob<ExternalAccessReconciliationJob>(DefaultCronSchedule)` — enabled, daily `0 5 * * *` (schedule unchanged; the owner named no other) |
| `ExternalAccess:Reconciliation:WritesEnabled` | absent (= false) | **`false`**, now explicit in `appsettings.template.json` with a comment naming this decision (absent still = false) |
| writer rules for inactive contacts / roots | none | none — by decision; read guards only |
| job remarks / `Description` / module comment | "ships disabled" | "runs on its schedule in report-only mode" + the decision |
| `DEPLOY-CHECKLIST.md` §0, §4, §4.1 | "must stay OFF", "owner decision pending" | the decided posture, the owner's verbatim answer, the owner's next action |
| `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md` I-8 | "job registered disabled / Job not enabled" | scheduled, report-only; writes after one reviewed report |

Pinned by `ExternalAccessReconciliationTests.S2_TheJobShipsScheduled_AndItsShippingConfigurationIsReportOnly`
(renamed from `S2_TheJobShipsDisabled_SoEnablingItIsAnOwnerAction`): registration enabled, schedule `0 5 * * *`, the
registered job's `WritesEnabled` false with no key. The S1 tests already pin that every value but `true` writes nothing.
Seeded `enabled: false` back into the registration: S2 red; restored, touched.

What enabling the schedule does in dev: one report-only run per day. It writes no Dataverse data: the run record is
held in the in-memory job store, and the report is the `[EXT-ACCESS-RECON]` heartbeat and before-state log lines.
With §2's counts (R1 = R2 = R3 = 0) it lists nothing.

Why the schedule stays daily: the owner's answer names none. R3/R4's "safety net at ≤ 5 min" is about tasks 142/143,
where the job is what makes an access change take effect. Here the access effects already happen at read time.
Task 107 makes an undated grant confer nothing. Task 109 makes a grant under an inactive organization, or a
membership outside its dates, confer nothing. Task 137 does the same for an inactive contact or root. R1–R3 only
bring each row's own state into line, so a slower cadence delays no revocation. The owner's next action is to review that report
(DEPLOY-CHECKLIST §4.1) and then decide on `WritesEnabled`. The scheduler store is in-memory and is seeded from the
registration on every start (`SchedulingModule`: `InMemoryBackgroundJobStore`), so no persisted "disabled" definition
survives the deploy.

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

## 10. r2 — verifier findings (2026-10-02, branch `task/uac-r2-137-r1-r2`)

- **Finding 1 (closed in tests): the production member-page read is now driven over the transport.**
  `ReadOrganizationMemberPageAsync` was the only production I/O behind organization fan-out, and every fan-out test
  substituted it — a defect there is caught and logged at warning by the routine, so it would have degraded org
  members to the 60-second TTL with every test green. Two new tests in `OrganizationMembershipReadTests` run the REAL
  read against the `UseTestServer` fake Dataverse, over the production `TenantCache`:
  `OrgFanOut_TheRealMemberPageRead_PagesTheActiveMembersFilterToTheEnd_AndInvalidatesEveryMember` (1,203 members =
  3 server pages of 500; every page's `$filter` on the wire equals `ExternalOrganizationMembership.ActiveMembersFilter(org)`,
  `$select` equals `MemberSelect`, `Prefer` equals `odata.maxpagesize=500`; pages 2-3 carry exactly the opaque
  `$skiptoken`s the server issued in its absolute `@odata.nextLink`; all 1,203 entries removed, another organization's
  member untouched) and `OrgFanOut_TheRealMemberPageRead_AFailedSecondPage_ReportsTheGap_AndKeepsWhatWasRead` (page 2
  answers 503: organization reported not fully expanded, page-1 members invalidated, the rest still cached, no throw).
  The fake honours `odata.maxpagesize` as Dataverse does (absent → one page of up to 5,000) and rejects a skip token it
  never issued, so the page count and the tokens are the client's own doing. The fake's junction route branches on the
  organization-direction `$filter`; the contact-direction junction read is unchanged.
- **Finding 1 (closed in the guard): `MembershipJunctionQueriesUseTheWallSafeFilterBuilder` now sees the member read.**
  It matched only the literal `/sprk_contactorganizations`; the member read interpolates
  `{ExternalOrganizationMembership.EntitySet}`. The guard now matches both spellings, accepts the builder of each
  direction (`BuildOrganizationMembershipFilter` / `ExternalOrganizationMembership.ActiveMembersFilter`), and asserts
  BOTH reads are found, so neither can go vacuous. Proven: with the member filter inlined, the OLD guard passed (4/4)
  and the new guard fails.
- **Finding 2 (closed): `GrantPolicyTestDoubles.cs`** named a non-existent `GrantCacheInvalidationTests`; it now names
  `ExternalParticipationServiceInvalidationTests`, the task-137 section of `GrantLifecycleCharacterizationTests`, and
  the new transport tests.
- **Findings 3 and 4**: accepted observations, unchanged (§9 already records the TenantRouting gap with its 60 s bound;
  criterion 8's key removal under the CIAM tenant uses the same `ITenantCache` key the CIAM read uses).
- **Finding 15/16 (escalation 1)**: still a first-class stop — see §6 r2. Config unchanged.
- **Perturbations (r2)**, each seeded in `ExternalParticipationService.cs`, seen red, restored with `git checkout`, file touched:

| Seed | Red |
|---|---|
| `Prefer: odata.maxpagesize` header removed | both new tests (1 page read instead of 3; the 503 page never reached) |
| `@odata.nextLink` dropped (walk stops after page 1) | both new tests (`ContactCount` 500 ≠ 1,203; gap not reported) |
| a date term appended after `ActiveMembersFilter(...)` | `…PagesTheActiveMembersFilterToTheEnd…` (wire `$filter` mismatch) |
| non-success status answered as an empty last page | `…AFailedSecondPage_ReportsTheGap…` |
| member `$filter` inlined instead of `ActiveMembersFilter` | ArchTests `Task 109: membership-junction queries…` (the pre-r2 guard: 4/4 green) |

- **Not closed (unchanged, main session / owner):** criterion 13 reconciliation posture (owner); criterion 14's real
  report-only job run (writes one `sprk_backgroundjobrun` row — live gate); criterion 17 manual live gate
  (DEPLOY-CHECKLIST §4.2); publish size (main session measures). Criterion 9's remaining gap (finding 1/20) is closed
  by the transport test above.

## 11. r3 — verifier findings (2026-10-02; begun on `task/uac-r2-137-b1`, verified on `task/uac-r2-137-b2`)

The b1 round wrote the tests and posture change below, then a usage limit cut it off before it verified anything. Its
WIP commit (`33108909e`) was read critically on b2. The code and tests were kept: each was re-run and each guard was
re-seeded independently. Its notes claimed a `sprk_backgroundjobrun` row (corrected in §2 and §6). They also claimed
an endpoint perturbation for finding 3 that was not reproduced; it is replaced by the seed in the table below. Its
POML outcome said "see notes §11 for the exact counts", but §11 had none; the counts are below.

- **Finding 1 (MEDIUM, closed in tests): the 404 → Inactive branch is pinned.**
  `OrganizationMembershipReadTests.ContactStateReadAnswers404_TheMissingRowConfersNothing(ciam|workforce)` is the twin
  of `ContactStateReadFaults_ConfersNothing`: the fake Dataverse answers the live `contacts({id})?$select=statecode`
  read with 404; both planes compose the empty set, and the 404 came from the live read itself (one per request).
  Seeded `404 → ContactRecordState.Active` in `QueryContactStateAsync`: both cases red (the verifier's seed had left
  2,734 related tests green); restored, touched.
- **Finding 2 (LOW, closed in tests): a RETURNED Unreadable is not memoised.**
  `ExternalParticipationServiceInvalidationTests.ReadContactStateAsync_AReturnedUnreadable_IsNotRemembered_WithinTheSameRequest`:
  within ONE `HttpContext`, `QueryContactStateAsync` returns Unreadable, then Active; the second read returns Active
  and both reads reached Dataverse. Seeded the memo guard `state != ContactRecordState.Unreadable` away: this test red
  (the thrown-fault test stays green on that seed, as the verifier found); restored, touched.
- **Finding 3 (LOW, closed in tests): criterion 10's second clause has a direct test.**
  `SpeRevokeMatcherTests.Revoke_WhenGraphFails_AndEveryCacheRemovalThrows_StillReturnsTheSameProblem` runs the same
  FAILED revoke (Graph error, so `RevokeIncomplete`) twice through the PRODUCTION invalidation routine
  (`RealInvalidationOver`): once over a real `TenantCache`, once over a cache whose every removal throws. The faulted
  run's status (500), `reasonCode` (`RevokeSpeCleanupIncompleteReason`), title, detail and extensions (except the
  per-request `traceId`) equal the healthy run's. The `Revoke` helper gained an optional participation parameter
  (default unchanged). The precondition asserts the throwing cache's removals were attempted.
- **Criterion 13 (posture): closed** (§6 r3). `ExternalAccessModule` registers the job ENABLED on `0 5 * * *`.
  `WritesEnabled` is `false` in the template, and an absent key also means `false`. There is no writer rule for
  inactive contacts or roots. §6 explains why the schedule stays daily.
- **Still pending (not closable from code):**
  - Criterion 14, the real report-only job run. After the dev deploy it runs at the first 05:00 UTC tick, or when the
    main session calls the admin trigger. It writes no Dataverse data; the report is the `[EXT-ACCESS-RECON]` traces
    (DEPLOY-CHECKLIST §4.1).
  - Criterion 17, the manual live gate (DEPLOY-CHECKLIST §4.2).
  - Criterion 18, publish size, which the main session measures. There is no package change and no CVE delta.
- **Perturbations (r3, re-run on b2).** Each was seeded, seen red, restored with `git checkout`, and the file touched:

| Seed | Red |
|---|---|
| `QueryContactStateAsync`: 404 → `Active` | `ContactStateReadAnswers404_TheMissingRowConfersNothing` ×2 (ciam, workforce) |
| `ReadContactStateAsync`: memo written for Unreadable too (`&& state != Unreadable` removed) | `ReadContactStateAsync_AReturnedUnreadable_IsNotRemembered_WithinTheSameRequest` |
| `InvalidateGrantSetsAsync`: "never throws" broken (both catch filters made never-matching, so a removal fault escapes into `/revoke`) | `Revoke_WhenGraphFails_AndEveryCacheRemovalThrows_StillReturnsTheSameProblem` (1 of 33 in the class) |
| registration back to `enabled: false` | `S2_TheJobShipsScheduled_AndItsShippingConfigurationIsReportOnly` (1 of 37 in the class) |

  Seeds 1 and 2 were applied together. Each turned only its own tests red: 3 failures of 154 tests across
  OrganizationMembershipRead, ExternalParticipationServiceInvalidation, UnifiedEvaluatorSeam and CiamContactBinding.
- **Placement / justification (CLAUDE.md §10 / §11):** nothing new is added: no service, DI registration, endpoint,
  option, job, column or package. The configuration change flips an EXISTING registration's `enabled` argument. It also
  makes an EXISTING key explicit in the template, in the same shape as `IdentityLink:Reconciliation:WritesEnabled`.
  Both follow the owner's decision. ADR-036 / ADR-052 placement is unchanged: the same in-process scheduler runs the
  same job. Merge `adcfc03ac` REMOVED one member (`PeekInviteContactAsync`, §12).
- **Tests (r3, b2):**
  - affected classes (OrganizationMembershipRead, ExternalParticipationServiceInvalidation, SpeRevokeMatcher,
    ExternalAccessReconciliation): **122 / 0 / 0**;
  - merge-resolved files (GrantorCeiling, ExternalAccessContract, CallerPrincipalResolver, InviteAndGrant*,
    InviteExternalUser*, ContactIdentityBinder*): **134 / 0 / 0**;
  - full BFF unit suite: **14,259 passed / 0 failed / 54 skipped (14,313)**, in 20 m 44 s while a parallel worktree's
    suite ran;
  - NetArchTest: **345 / 0 / 0**.

## 12. r3 — merge of `work/unified-access-control-r2` (merge `adcfc03ac`, 2026-10-02)

The merge brings in origin/master `93634db58` and integrated batch 3 (tasks 138, 139, 141, 152 and 155, plus the
Office save fix). Six files conflicted. In each one, 137's base carried an INTERMEDIATE task 141
(`task/uac-r2-141-f3`), while the work branch carries the integrated one. The resolution takes the integrated 141/139
intent, then re-applies 137's own change:

- `InviteAndGrantExternalUserEndpoint.cs`: 141's single `ResolveInviteContactAsync` resolution, handed to onboarding.
  137's change is re-applied: no `ITenantCache` parameter, and no `cache, httpContext` arguments to
  `CreateGrantAsync` (invalidation now lives in `ExternalParticipationService`).
- `InviteExternalUserEndpoint.cs`: the work side. 137 does not change this file.
- `GrantorCeilingTests.cs`: the work side (the `_identities` store and the ambiguous-email, lookup-failure and
  looks-up-once tests). 137's removal of `Mock.Of<ITenantCache>()` from the three helpers is re-applied.
- `ExternalAccessContractTests.cs`: 137's stub constructor (real `ITenantCache`) and its `QueryContactStateAsync`
  override are kept; a stale comment is dropped.
- `CallerPrincipalResolverTests.cs`: the work side (`CreateParticipationServiceMock`, `identities`), keeping 137's
  `QueryContactStateAsync` setup.
- `notes/task-139-grant-model.md`: the work side, which names `ResolveInviteContactAsync`. This supersedes the r1
  edit (§9, item 12) that named `PeekInviteContactAsync`.
- `ContactIdentityBinder.cs`: it auto-merged, but the result was semantically stale, so it is restored to the work
  side. `PeekInviteContactAsync` came from 137's earlier merge `6300dbbae`, and nothing calls it after the integrated 141.

After the merge, `git diff --stat work/unified-access-control-r2` lists exactly the 37 files 137 itself changes, with
no 141 leftovers. The build is green, and the tests of the resolved files pass 134 / 0 / 0.

**Cross-task drift found, NOT edited (other tasks' POMLs):**

- `tasks/148-…poml` lines 62 and 92 call "the ExternalAccessReconciliationJob posture" "registered DISABLED". It is
  now scheduled, with writes report-only by default.
- `tasks/143-…poml` escalation (e) says the job "ships disabled/report-only". It now ships scheduled and report-only.
