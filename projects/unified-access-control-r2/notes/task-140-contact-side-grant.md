# Task 140 — contact-side Grant Access (owner C4 / Q1 / Q2, #1063)

Executed 2026-10-04 on `task/uac-r2-140-x1` from `integ/uac-r2-batch4` @ `6b685f0d4` (the verified, not-yet-pushed batch-4
integration tree: 132/133/135/136/137/138/139/141/142/143/146/149/157 merged). FULL rigor, opus.

Owner rules applied (session27 note): round 1 C4; round 2 item 3 + Q1 + Q2; round 3 (all 52 consolidated decisions accepted
as recommended — for this task **G1 (a)**, **G2 (i)(ii)(iii)**, **G3 (a)**); round 3b (A1 settled: the cap STAYS for manual
grants); round 13 item 4 (tri-state No Access — an Unverifiable answer fails closed and is reported as a fault).

## 1. What was built

| Owner rule | Where |
|---|---|
| Contact principal only; a workforce SYSTEMUSER (even with a linked contact id) → 403 `sdap.access.contact_grant.use_manage_access` | `ContactGrantorAuthorizationFilter.EvaluateGrantorAsync/EvaluateRevokerAsync` via `CallerPrincipal.IsContactPrincipal` (= `SystemUserId is null && ContactId != Empty`; the principal KIND decides, no plane branching — ADR-028 A3) |
| Grantor holds Collaborate/Full Access (effective post-veto level — G3 (a)); View Only and "no access" are one 403 `level_insufficient` | `CallerPrincipal.RightsOn(rootType, id)` → `ContactGrantorAuthorizationFilter.GrantingLevel` (task 139's `GrantCeilingFor`, View Only excluded) |
| Grantor has ≥1 active organization (403 `no_organization`); membership fault → 503 `membership_unreadable` | the ONE membership read `ExternalParticipationService.ReadOrganizationMembershipsAsync` (task 109 — CONFERRING set = statecode 0 + `sprk_startdate`/`sprk_enddate` current + active organization; `Unreadable` on any fault). **No extension was needed**: task 109 already made the reader date-bounded and fault-distinguishing, which is exactly what the POML asked `QueryActiveOrgIdsAsync` to become (that method was renamed by 109). |
| ONE active grantee in a shared conferring organization, resolved STRICTLY within the grantor's organizations (POML step 4; r-final §11): by id → one 422 `grantee_not_in_organization` worded "This person" for unknown / inactive / other-organization ids (no stored email echoed, the same reads for every id); by email → `ExternalParticipationService.FindConferringMembersByEmailAsync` (the junction rows of the grantor's organizations whose expanded contact is active and uses the email), >1 COLLEAGUE → 409 `grantee_ambiguous`, an outsider is never counted; self → 400 `self_grant`; read fault → 503 | `ContactGrantEndpoints.ResolveGranteeByIdAsync` (task 141's `IContactIdentityStore.GetContactAsync` + the membership read) / `ResolveGranteeByEmailAsync` |
| G1 (a): a person who is not yet an active contact of the grantor's organization is refused (422, the recommended message); **no route writes `sprk_contactorganization`, creates a contact or onboards** | `ResolveGranteeAsync`; pinned by `ContactGrantGuardTests` (source) + `AssertNoMembershipWrites` (behaviour) |
| No organization-grantee field; unknown body member → 400 (end-to-end) | `[JsonUnmappedMemberHandling(Disallow)]` on `ContactGrantRequest` / `ContactGrantRevokeRequest`; `ContactGrantRouteTests` through the host |
| Cap at the grantor's level (Q1, 3b) — REQUIRED ceiling through the ONE core (WP-1) | `GrantCeiling.FromContactGrantorRights` (new named factory) → `GrantExternalAccessEndpoint.CreateGrantAsync(..., contactIssuer)` |
| G2 (i): expiry = requested or today+90, cut back to the grantor's own DATED access (`expiryNarrowed`) | `ContactGrantEndpoints.ResolveGrantorExpiryCapAsync` → `ContactGrantIssuer.ExpiryCap` → the core |
| G2 (ii): no cascade | nothing cascades; issued rows stand, visible + revocable in Manage Access |
| G2 (iii): never change a row somebody else issued → 409 `managed_elsewhere`, row untouched; own row: raise ok, lower → 409 `would_lower_existing`, never shorten | the core's contact-issuer mode (`CheckGrantAsync` step 3a; match-path `expiryToWrite`) |
| Restricted: filter 403 first (principal None, task 135); core 422 `record_restricted` second | both asserted |
| Secure/Limited: only a DIRECT grant gives the grantor a level | asserted through the REAL CIAM strategy + REAL evaluator, both flags |
| No Access list: 422 `grantee_denied`; Unverifiable → 503 `no_access_unverifiable` | task 139's entry point `CheckGranteeNoAccessAsync` (tri-state), unchanged, inside the core |
| Contact-typed issuer `sprk_grantedbycontact`; `sprk_grantedby` empty | `BuildGrantPayload(..., grantedByContactId)`; `ExternalGrantRow.GrantedByContactId`; `RowSelect` |
| `[EXT-CONTACT-GRANT]` log: grantor, grantee, root, requested, granted, narrowed, expiry (+ cap) | `ContactGrantEndpoints.GrantAsync` |
| List: only what the caller issued | `ExternalGrantLifecycle.QueryActiveRowsIssuedByContactAsync` + in-memory re-check |
| Revoke: only the caller's own rows on the grant; absent / not-theirs one 404 `not_found`; faults are ProblemDetails with a message (`revoke_failed`); grantor no longer Collaborate → 403 | `EvaluateRevokerAsync` + `RevokeAsync` (`AccessRemainsFromOthers` when another issuer's row stays) |
| Deny-by-default for an unmapped request type | filter default branch → 403 `unmapped_request` |
| External SPA: Invite User for Collaborate + Full Access, levels ≤ caller's, new route, server messages verbatim, narrowed/expiry notices, issued-grants list with Revoke; no `/api/v1/external-access/*` call left | `useAccessLevel.canInvite/grantableLevels`, `ProjectPage` (inline FullAccess check removed), `InviteUserDialog` (rewritten), `IssuedGrantsList` (new), `bff-client` (dead calls removed; `grantAccessAsContact`/`listContactGrants`/`revokeContactGrant`/`problemMessage`), `mock-service` |
| Manage Access shows "Granted by {contact} (external contact)"; systemuser revokes via `/revoke` | `IAccessGrantRecord.grantedByContactName`, `AccessGrantModal`; TrackingFieldTrio **v1.0.35** reads `_sprk_grantedbycontact_value` (5 locations + bundle, `build:prod`) |

### Discovered and decided in-scope (recorded, each tested)

1. **Systemuser take-over of a contact-issued row.** Without it, an internal user raising a colleague's contact-issued grant
   through `/grant` (match path, in-place update) would leave `sprk_grantedbycontact` = the contact, who could then REVOKE the
   internal user's decision — proxy revocation in reverse. The core (default mode) now clears the contact issuer and stamps
   `sprk_grantedby` when it CHANGES such a row; a no-op re-grant leaves it. /grant and /invite-and-grant behaviour on rows with no
   contact issuer is byte-identical (their suites unchanged and green). The Assigned-To rule raising a lower contact-issued row
   takes it over the same way. **Session 27 round 34 item 3 (BINDING) extends it to every internal writer OUTSIDE the core**:
   `POST set-record-share-expiry` takes each contact-issued share over inside its one transaction (r-final §11, item 2).
2. **Race collapse.** The post-create duplicate collapse, in contact-issuer mode, converges only over the caller's own rows, so a
   row another issuer raced onto the same key is never deactivated by a contact's write.
3. **Assigned-To ledger.** Contact grants/revokes do NOT call `MarkGrantAdoptedAsync` / `MarkGrantRevokedAsync`: those mark an
   OPERATOR's decision (adoption stops the rule; revocation sticks as Declined). A contact is not an operator — a contact's
   revoke must not permanently suppress an Assigned-To colleague's auto-grant, and the rule's own rows are never contact-issued
   (managed_elsewhere protects them).
4. **Expiry-cap rule (G2 (i)) — exact sources.** Qualifying rows: the grantor's own active unexpired rows at ≥ the granted level;
   on a Standard (not Secure/Limited) record also the org-wide rows of the grantor's conferring organizations; a direct row naming
   an INACTIVE firm confers nothing (ISS-026) and is excluded (firm state read; 404 = excluded; other fault = 503). None
   qualifying → if the root is not direct-only AND an undated term (standing / org expansion) ran for that entity type on the
   principal (`CallerPrincipal.UndatedAccessTermEntityTypes`, set by the workforce strategy from `AccessibleRecordSetSources`;
   always empty for CIAM) → no cap (+90); otherwise the principal's level is not held live → 403 `level_insufficient`.
   Over-approximation is in the safe direction only (a dated row caps even if an undated term also confers).
5. **External SPA lint gate was not runnable** (`npm run lint` named eslint; no eslint dependency/config — "'eslint' is not
   recognized"). Made runnable (eslint flat config mirroring `@spaarke/ui-components`, devDeps eslint / @eslint/js /
   typescript-eslint / eslint-plugin-react-hooks / globals), which surfaced 10 pre-existing `rules-of-hooks` ERRORS — fixed with no
   behaviour change: `AuthGuard` split into an outer guard + `MsalAccountGate` (hooks no longer after the mock/Teams early
   returns); `PlaybookLibraryPage`'s param guard moved below its hooks. `npm run lint` now: 0 errors, 1 pre-existing warning
   (`DocumentLibrary` exhaustive-deps).
6. **External SPA had no test runner.** Added vitest 2 + Testing Library + jsdom (devDeps only), `vitest.config.ts` (reuses
   `vite.config.ts`), `tests/setup.ts` (ResizeObserver no-op, cleanup) — required by the AC "asserted by rendering
   ProjectPage's Contacts tab".
7. `tsc --noEmit` on the external SPA reported 6 PRE-EXISTING type errors (`mocks/mock-data.ts` ×3, `OutsideCounselDashboard.tsx`
   ×2, shared `EntityCreationService.ts` ×1). **Fixed in r-final (§11, item 8)** rather than recorded (round-15 directive);
   `npm run typecheck` added so the gate is runnable; `tsc --noEmit` now exits 0.

## 2. Escalation triggers

| # | Status |
|---|---|
| 1 New-person invites | **Answered** — round 3 G1 (a): refuse. Implemented. |
| 2 Grantor-level source | **Answered** — round 3 G3 (a): effective post-veto level. Implemented. |
| 3 Lifetime / ownership | **Answered** — round 3 G2: (i) cap at the grantor's dated expiry, (ii) no cascade, (iii) refuse. All three implemented here (not deferred). |
| 4a 141 / 013 merged before the workforce contact-only path | **Did not fire.** 141 merged (batch 3; live gates G-1..G-6). 013's POML stays `pending` only because its criterion 7 waits on 141's live completion; the binding itself is 141's code: a workforce contact-only principal resolves only through an oid binding (`WorkforcePrincipalResolver` (b): email bind only for a configured-tenant MEMBER with exactly one active unbound match, then oid only). |
| 4 135/136 delivered | **Did not fire.** Both merged (`700763023`); their POMLs are `completed` / `pending` only on live gates (136 note §8). The Restricted / Secure / Limited tests here compose the grantor's level through the REAL CIAM strategy + evaluator. |
| 5 adr-check vs ADR-028/008 | **Did not fire** — path C (comply): routes on the one ExternalCollaboration group (A3), route-level filter (ADR-008), no plane branching, broker-only. |
| 6 Org memberships | **Answered** — round 3 G1 (a) (= NO). No code path writes `sprk_contactorganization` (asserted). |

## 3. Placement + justification (CLAUDE.md §10 / §11)

**Placement: BFF**, on the existing `/api/v1/external` group — `CallerPrincipal`, the grant core and the membership/identity
readers all live there (bff-extensions criteria: per-request authorization that must run before an app-only write; no other host).
No new DI registration (every injected service is already registered unconditionally: `DataverseWebApiClient`,
`ExternalParticipationService`, `IAccessibleRecordSetService`, `IContactIdentityStore`, `TimeProvider`), no package, no
background work, no plugin (ADR-002).

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| 3 routes `ContactGrantEndpoints` | `/grant`, `/invite-and-grant`, `/revoke` on `/api/v1/external-access` (grep `MapPost` in ExternalAccess) | Cannot join that group: workforce-only scheme + `DelegationRuleFilter`'s OBO RetrievePrincipalAccess, which a contact can never pass; making it dual-scheme exposes every admin mutation to CIAM | Owner-mandated capability absent: "Invite User" 401s (CIAM cannot reach the group) and `/invite` writes no grant |
| `ContactGrantorAuthorizationFilter` | `DelegationRuleFilter` (asks Dataverse OBO for Write) | Shape copied (type dispatch + default deny); its question cannot be asked for a contact | No route-level gate (ADR-008); a systemuser or View Only contact could mint grants |
| `ContactGrantDtos` (closed records) | `GrantAccessRequest` (has `OrganizationId`, open to unknown members) | Reusing it would admit org-wide grants | Org-wide grant by a contact possible (Q2 violated) |
| `GrantCeiling.FromContactGrantorRights`, `ContactGrantIssuer`, core contact-issuer mode | `CreateGrantAsync` / `CheckGrantAsync` (task 139 WP-1) | EXTENDED — optional trailing parameter; no second writer (`GrantCeilingGuardTests` still green) | A second writer would bypass policy/ceiling/No Access; managed_elsewhere / never-shorten / expiry cap would not exist |
| `CallerPrincipal.RightsOn`, `IsContactPrincipal`, `UndatedAccessTermEntityTypes` | `GetEffectiveRights/GetMatterRights/GetWorkAssignmentRights`; `AccessibleRecordSetSources` | Added to the existing class; the strategy copies Sources it already computes | Per-handler root-type switches; G2 (i) cannot tell an undated level from a stale one |
| `ExternalGrantRow.GrantedByContactId/GrantedBySystemUserId`, `QueryActiveRowsIssuedByContactAsync` | `RowSelect`, `ActiveRowsForRootFilter` | Extended the one row shape + filter | "Revoke only what you issued" cannot be enforced |
| Column `sprk_externalrecordaccess.sprk_grantedbycontact` (FLS: BFF-written) | `sprk_grantedby` — SYSTEMUSER lookup (live metadata 2026-10-04, read-only check) | Cannot reference a contact | No record of a contact issuer; issuer-scoped revoke impossible; audit trail empty |
| `scripts/Deploy-ExternalRecordAccessContactGrantor.ps1` | `Set-RecordCreatorPersonSchema.ps1` (task 133: other tables) | Same shape (copied helpers + profiles); different table | Schema step not operator-runnable (AP-13 forbids MCP table writes) |
| SPA `IssuedGrantsList`, `grantableLevels` | none — no list of issued grants in the SPA | — | "Revoke" (AC) has no surface |
| SPA devDeps vitest/@testing-library/jsdom + eslint set | none in external-spa | dev-only; no bundle impact | AC "rendered" assertion impossible; `npm run lint` cannot run |

## 4. Tests (all KEEP paths)

- `tests/integration/auth/UnifiedAccessControl/ContactGrantAuthorizationTests.cs` — 62 tests: handler-level (filter → handler
  through `DefaultEndpointFilterInvocationContext`) for every AC rule with positive twins, plus `ContactGrantRouteTests`
  through the HOST (ExternalCollaboration group, caller-principal filter with the resolver substituted at its interface, route
  filter attachment, closed-DTO 400, systemuser 403 on all three routes, unauthenticated 401). Doubles: an in-memory grant table
  that interprets the production `$filter`s; `FlagStubParticipationService` (+ per-contact membership rows projected through the
  PRODUCTION `ProjectOrganizationMemberships`, per-contact faults, seeded grant sets for the REAL CIAM composition);
  `InMemoryContactIdentityStore`; the REAL `AccessibleRecordSetService` + `NoAccessListReader` behind `QueryChunkAsync`.
- `tests/Spaarke.ArchTests/ContactGrantGuardTests.cs` (3) — no onboarding/contact-create/membership path on the surface;
  script ↔ code agreement for the issuer column. `RouteAuthorizationGuardTests`: `ContactGrantEndpoints.cs` governed
  (RouteLevelGate), census 122 → 123.
- `src/client/external-spa/tests/ProjectPage.contactGrant.test.tsx` (12, vitest) — renders ProjectPage's
  Contacts tab: Collaborate/Full show Invite User + list, View Only hides both; dialog levels capped; posts to the contact route
  with no organization member; narrowed + expiry notices; server refusal text verbatim (grant + revoke); revoke by id; source scan
  for `/api/v1/external-access`.
- `AccessGrantModal.contactIssuer.test.tsx` (2, jest) — "(external contact)" provenance + `/revoke` of a contact-issued row.

### Perturbation record (every new guard seeded, run, restored; files touched after restore)
BFF (filter `ContactGrant*`; counts are distinct red test methods, a Theory counted once): P1 managed_elsewhere off → 1 (the
3-issuer Theory); P2 contact never-lower off → 1; P3 never-shorten off → 1; P4 expiry cap off → 4; P5 systemuser take-over off → 1;
P6 principal-kind check removed → 2 (incl. the host route test); P7 View Only admitted → 2 (grant + revoke); P8 org intersection
off → 2 (other-org + the 4-shape membership Theory); P9 `JsonUnmappedMemberHandling` removed → 1 (host route 400); P10
default-deny → next → 1; P11 revoke issuer check removed → 1; P12 revoke all rows → 1; P13 inactive-firm row counted → 1; P14
no-organization off → 1; P15 membership fault folded into "none" → 1 (the 4-case fault Theory); P16 undated always → 2; P17
self-grant off → 1; P18 ambiguity off → 1; P19 issuer stamp off → 3; P20 list issuer (query + in-memory, both layers) off → 1;
P21 race collapse over all rows → 1; P22 org rows counted on a Secure root → 1. Each went red; restored → 62/62. (A first pass
with literal `if (false)` seeds hit CS0162-as-error; re-seeded with runtime-opaque conditions.)
SPA (12 vitest): S1 canInvite Full-only → 6 red; S2 ProjectPage inline Full-only → 5; S3 dialog Full-only guard → 3; S4 levels
not capped → 5; S5 a call to the management group → 4 (incl. the source scan); S6 server message not parsed → 2. Restored → 12/12
(×3 runs).
Modal: provenance branch disabled → 1 red; restored → 2/2.

### Suite results (2026-10-04, full runs once at the end; other agents running suites concurrently)
- **BFF unit suite** (`tests/unit/Sprk.Bff.Api.Tests`, which compiles `tests/integration/auth/**`): first full run 15462 passed /
  18 failed / 54 skipped (29 m 42 s). 14 were `RecordShareExpiryTests` — its fake rejects any `$select` column not in its
  live-column list, and `RowSelect` now names `_sprk_grantedbycontact_value`: the deploy-order hazard by design; the list gained
  the column (comment points at the schema script). The other 4 — `ContactGrantRouteTests.Grant_ThroughTheRoute_ByAQualifyingContact`
  (client aborted after 3 m 13 s), `ComposePatchEngineSaveSeamTests.Save_SplitParagraph_RoundTrips_ThroughTheWire`,
  `SearchItemsTests` ×2 (round 16 item 5: that suite still makes a real outbound Dataverse call) — are contention: the isolated
  re-run of every failing class plus all `ContactGrant*`, `GrantorCeiling*`, `GrantLifecycle*`, `AssignedAccess*`,
  `RecordShareExpiry*`, `ComposePatchEngineSaveSeam*`, `SearchItems*` tests: **521 passed, 0 failed**.
- **ArchTests**: first run 596/2 — `ExternalSpaGridViewSelectorGuardTests` scans every file under `external-spa/src`, and the
  SPA test then lived in `src/pages/__tests__` (a `MemoryRouter`, `node:fs`). Tests moved to `external-spa/tests/` (outside the
  shipped source the guard governs; vitest + eslint repointed). Re-run: **598/598**.
- **Sprk.Bff.Api.IntegrationTests**: 104/104. **Spe.Integration.Tests**: 403 passed, 25 skipped, 0 failed.
- External SPA: `npm test` 12/12 (×3), `npm run lint` 0 errors (1 pre-existing warning), `npm run build` green.
- Shared UI: `jest src/components/AccessGrantModal` 96/96 (a first cold run had 2 timing failures in the pre-existing
  `userShare` suite; re-run green). TrackingFieldTrio `npm run build:prod` green (after building `Spaarke.Auth`,
  `Spaarke.SdapClient`, `@spaarke/ui-components` locally — the PCF's `ensure-dist-fresh` step needs their dist).

## 5. Schema step (AP-13) — operator gate G-140-1 (PENDING)

Dry run (read-only, 2026-10-04, operator identity via az, spaarkedev1):
```
(a) WOULD create sprk_externalrecordaccess.sprk_grantedbycontact (lookup -> contact, relationship sprk_contact_sprk_externalrecordaccess_grantedbycontact)
(b) OK profiles 'Spaarke BFF-Managed Field Readers' / 'Writers'; writers = mi-bff-api-dev + SDAP-BFF-SPE-API only; readers = all 6 BU default teams
(c) WOULD secure the column and grant both profiles
(d) OK both profiles in SpaarkeCore
DRY RUN complete — nothing was written.
```
Live metadata read (read-only): `sprk_externalrecordaccess` lookups today = contact, grantedby(systemuser), invoice, matter,
organization, project, recordtype, workassignment (+ system) — no contact-typed issuer.

**Main session — run, in order, BEFORE deploying this BFF or TrackingFieldTrio v1.0.35 anywhere:**
```powershell
.\scripts\Deploy-ExternalRecordAccessContactGrantor.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
    -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Apply
.\scripts\Deploy-ExternalRecordAccessContactGrantor.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com `
    -BffApplicationIds 5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c -Verify   # must exit 0
```
(From pwsh, pass the ids as an array if invoking with `-File`.) Then read the column back
(`EntityDefinitions(LogicalName='sprk_externalrecordaccess')/Attributes(LogicalName='sprk_grantedbycontact')`): prefix `sprk_`,
target `contact`, `IsSecured` true, in SpaarkeCore. ⚠️ **Merge gate:** this code selects the column on EVERY grant-row read —
it must not deploy to an environment where (a) has not run (POML step 1: "code that binds the new lookup must not merge before
(c) passes").

## 6. Manual live gate G-140-2 (PENDING — main session; criterion 20)

Prereqs: G-140-1 PASS; BFF deployed from this branch; external SPA deployed (deploy-external-spa workflow / `npm run build` →
SWA); TrackingFieldTrio v1.0.35 imported (`Solution/pack.ps1` → `pac solution import`). Existing test identities only (a CIAM
contact with Collaborate on a Standard project, a colleague contact in the same organization, a contact of another organization,
a View Only contact); no user relocation. Steps (a)–(h) exactly as the POML criterion lists; record in this note. Note for (e):
the colleague's grant cache is invalidated under every tenant (task 137), so removal is immediate (≤60 s worst case).

## 7. Publish size (CLAUDE.md §10) and CVE

Fresh trees exported with `git archive` to SHORT paths (`C:\wt140m` = base `6b685f0d4`, `C:\wt140b` = branch `1a8953d4a`), each
`dotnet publish -c Release`, zipped with PowerShell `Compress-Archive -CompressionLevel Optimal` over `deploy\api-publish\*`
(incl. PDBs): **base 46.00 MB, branch 46.03 MB, delta +0.03 MB, 212 = 212 files.** ≤ 60 MB. No package added to the BFF.
`dotnet list package --vulnerable --include-transitive` (BFF): no vulnerable packages. External SPA `npm audit --omit=dev`:
4 moderate, no high (prod dependencies unchanged by this task; only devDependencies added).

## 8. Quality gates (Step 9.5)

- **code-review** — no Critical. W1 deploy-order hazard (RowSelect names the new column on every grant read) — mitigated by the
  operator gate, the script's `-Verify`, the entity-schema / doc warnings and this note. W2 `managed_elsewhere` also fires on an
  EXPIRED row somebody else issued (over-refusal — the safe direction; the record's team can renew or remove it). W3 the
  grantor's membership is read twice per grant (filter + handler re-validation — deliberate). S1 `ContactGrantEndpoints.cs`
  ~590 lines, one feature (three routes + the cap rule), cohesive. S2 the expiry cap over-approximates when a dated and an
  undated source both confer (safe direction).
- **adr-check** — compliant: ADR-001 (Minimal API, `Map{Feature}` extension), 002 (no plugin; one server-side writer), 003 (fail
  closed everywhere; faults are 503s, never "no"), 007 (no Graph), 008 (route-level filter + handler re-check), 009 (cache via
  the one invalidation routine), 010 (no new interface/registration), 013 (no AI), 021 (Fluent v9 tokens), 028 A1/A3/A4 (one
  ExternalCollaboration group, no plane branching, no OBO, no secret), 038 (no `Mock<HttpMessageHandler>`, no DI/ctor tests,
  positive twins). Trigger 5 did not fire.

## 9. Interactions recorded for other tasks

- **C5 / task 137:** invalidation goes through `InvalidateGrantSetsAsync`, which clears every tenant a grant set can be cached
  under — a CIAM grantor's grant to a workforce-plane colleague is invalidated correctly (the POML's concern is closed by 137).
- **064 / 066 (open):** provenance is carried today on the current row model (`IAccessGrantRecord.grantedByContactName`, read by
  the host from `_sprk_grantedbycontact_value`). When 064's record-access report and 066's row model land, they must carry the
  contact issuer as a field (no second provenance mechanism). **Main session: amend 066** to include `sprk_grantedbycontact`.
- **ExternalGrantRow.RowSelect** now includes `_sprk_grantedby_value` + `_sprk_grantedbycontact_value`; every consumer
  (SetRecordShareExpiry, ProjectClosure, the materializer, revoke) reads it — same deploy-order gate.

## 10. `.claude/` edits needed (main-session only)

None required by this task.
