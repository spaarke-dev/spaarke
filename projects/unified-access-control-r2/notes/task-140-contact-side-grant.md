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
**Re-measured in r-final (§11)** after that round's BFF changes, the same way from fresh exported trees at SHORT paths
(`C:\wt140m2` = base `6b685f0d4`, `C:\wt140b2` = branch `fdf24ee4f`, the last commit touching `src/`): **base 46.00 MB, branch
46.03 MB, delta +0.03 MB, 212 = 212 files (4 PDBs each)**, Compress-Archive Optimal incl. PDBs. `dotnet list package --vulnerable
--include-transitive` (BFF): no vulnerable packages; no .NET package changed since the base.
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

## 11. Fix round r-final (2026-10-04, after the machine restart) — verifier r1 items 1–15

Branch `task/uac-r2-140-x1-v1c`, created from `wip/uac-r2-140-x1-v1-restart` @ `c1210b9da` (the previous agent's
"saved before a machine restart" commit, unverified). Session 27 rounds 1–36 read from `work/unified-access-control-r2`.

| # | Item | Outcome |
|---|---|---|
| 1 | The WIP commit | **Kept in full, after review.** It built clean, and its affected tests were green (252/252 over ContactGrant / RecordShareExpiry / BulkUpdateTransaction / GrantPolicy / GrantorCeiling / GrantLifecycle) before any change. What it carried: the scoped colleague-by-email read (items 5/13), the no-disclosure by-id path (items 4/14), the schema script on the shared membership helper (items 3/12), the `.NOTES` fix (item 7), the set-record-share-expiry take-over code (items 2/6) and `IGenericEntityService.BulkUpdateAsync` clearing a column on `DBNull.Value` (UpdateAsync's convention). What it lacked, added here: tests + seeds for the take-over, the core's systemuser stamp under test, the script pin, the by-id equal-reads rule, the SPA type errors, docs, this record. |
| 2 | Round 34 item 3 (BINDING) — internal take-over outside the core | **Closed.** `SetRecordShareExpiryEndpoint` adds, for every share whose `sprk_grantedbycontact` is set, `sprk_grantedbycontact = DBNull.Value` (clear) and `sprk_grantedby = EntityReference(systemuser, caller)` to that share's fields in the ONE `ExecuteTransactionRequest` (`ExternalGrantLifecycle.AddInternalTakeOverFields`); the caller's systemuser is read only when such a share exists; an unresolvable caller still clears the contact issuer (the core's "an audit field never blocks the write" rule). Writers enumerated (every BFF write to `sprk_externalrecordaccesses`): the core (take-over existed; its stamp now under test), set-record-share-expiry (added), and the DEACTIVATING writers `/revoke`, project closure and the reconciliation job — an inactive row is outside every contact path (the contact's grant, list and revoke change only ACTIVE rows it issued), so nothing there can be re-lengthened or revoked by the contact. Non-product writes: live read-only 2026-10-04, `prvWritesprk_ExternalRecordAccess` is held by System Administrator, System Customizer and Service Writer only — no Spaarke role — so no ordinary internal Write holder can change a grant row outside the BFF. Docs: uac-access-control.md, entity-schema.md (writer row + column note). |
| 3 | `-Verify` false negative (G-140-1 could never pass) | **Closed.** Step (d) decides through `scripts/common/DataverseSolutionMembership.ps1` (`Get-DvSolutionMembership` paged, `Test-DvInSolution -TableMetadataId`); the column and relationship carry their table's MetadataId. Live read-only probe (2026-10-04): SpaarkeCore 374 components over all pages, 87 tables with subcomponents; `sprk_externalrecordaccess` (fbeb1369-…) is a Direct row; a column of that table (the existing `sprk_grantedby`, f23aba4f-…) has NO own row and reads `ViaTable` — exactly the shape the new column will have. Dry run (read-only) re-run green on the helper: (b) OK, (d) both profiles OK. Pinned by `SchemaScriptSolutionMembershipGuardTests` (helper use) and the new `ContactGrantGuardTests.TheSchemaScriptCountsTheColumnAndRelationshipThroughTheirTable` (each type-2/10 component carries `TableId`, the call passes `-TableMetadataId`, no own `solutioncomponents` read). Also: a comma-joined `-BffApplicationIds` (pwsh `-File`) is split. |
| 4 | By-id path leaked another organization's contact email; existence oracle | **Closed.** Every by-id refusal (unknown id, inactive contact, active contact of another organization) is ONE 422 worded "This person …"; the stored email is never read into a message (only a VERIFIED colleague's address may label a later managed_elsewhere message). This round also makes the WORK identical: the contact lookup AND the membership read run for every id, so the reads done do not tell the caller whether the id names a contact. Tests: the 2-shape theory (other-organization, inactive) compares status, code and detail with an unknown id and asserts the secret email never appears and the same reads ran. |
| 5 | Email lookup was system-wide | **Closed.** `ExternalParticipationService.FindConferringMembersByEmailAsync`: ONE read of the `sprk_contactorganization` rows of the grantor's conferring organizations (chunks of 25, every chunk read, `@odata.nextLink` followed) whose expanded contact is active and uses the email; re-decided in code by `ProjectColleaguesByEmail` (the task-109 conferring rule, active contact, case-insensitive trimmed email). >1 COLLEAGUE → 409 "more than one person in your organization"; an outsider is never counted or mentioned. Query live-verified read-only on spaarkedev1 (the production `$filter`/`$select`/`$expand`, incl. an escaped quote). The DTO and bff-client comments now say what the code does. **On the wire**: new `ColleagueByEmailWireTests` (3) run the REAL service over an in-memory ASP.NET Core server (ADR-038 §7's B1 replacement, as `OrganizationMembershipReadTests` does): the server receives EXACTLY the builder's `$filter` (a term appended at the call site goes red), its `$select`/`$expand`/page-size preference, every page is read, the live row shape is projected, a faulted page is `Failed` (never "nobody"), and chunks carry their own exact filters. **The full ArchTests run found the WIP's new junction read tripping task 109's guard** (`ExternalAccessQueryIntegrityGuardTests.MembershipJunctionQueriesUseTheWallSafeFilterBuilder`: "a sprk_contactorganizations query builds its $filter inline"). Not an inline filter — a third direction the guard did not know — so the guard was EXTENDED, not bypassed: `BuildColleagueByEmailFilter` is accepted as the (organizations, email) → colleagues builder, must be found (not vacuous), and its body is held to the wall rule (names `WallMembershipStateClause`, no `sprk_startdate`/`sprk_enddate`). |
| 6 | Take-over did not cover every internal writer | = item 2. |
| 7 | `.NOTES` cited a non-existent test | **Closed** (`ContactGrantGuardTests.TheIssuerColumnAgreesWithTheSchemaScript`; the solution step → `SchemaScriptSolutionMembershipGuardTests`). |
| 8 | Six pre-existing `tsc --noEmit` errors | **Fixed, not filed.** `mock-data.ts` ×3: event mocks used `_sprk_projectid_value` (sprk_event's project lookup is `sprk_regardingproject`) → `_sprk_regardingproject_value`. `OutsideCounselDashboard.tsx` ×2: a REAL display defect — every Recent Activity / Upcoming item read that non-existent column and so named its project "Unknown Project"; items now carry the project id and the name comes from the project rows the page already loads (the page is not routed today, so it was latent); the inert `$select` corrected. Shared `EntityCreationService.ts` ×1: the SPA's ambient `@spaarke/sdap-client` shim lacked `DriveItem` (the shared service began importing it). `npm run typecheck` added; `tsc --noEmit` exits 0. Also fixed the one pre-existing lint WARNING (`DocumentLibrary` effect missing `projectId`): `npm run lint` now reports 0 problems. New vitest `OutsideCounselDashboard.projectName.test.tsx` (seeded red, restored green). |
| 9–11 | Verifier's green runs / seeds / compliance | Re-run here — see suite results below. |
| 12 | POML step 1(c) / G-140-1 could not pass | = item 3 (code defect closed). The operator `-Apply` + `-Verify` remain the pending manual gate (exact commands in §5). |
| 13 | POML step 4 email scope | = item 5. |
| 14 | No-oracle intent | = item 4. |
| 15 | Pending live gates | Unchanged and NOT counted as failures: G-140-1 (operator schema `-Apply` then `-Verify`, §5) and G-140-2 (criterion 21 manual live gate, §6). |

### r-final perturbation record (each seed alone, affected tests run, file restored + touched)
BFF (filter ContactGrant* + RecordShareExpiry* + BulkUpdateTransaction*, 119 tests; each seed alone; every one went red, each restored and touched; 119/119 after):
- **T1** set-record-share-expiry take-over call disabled → 3 red: `RecordShareExpiryTests.SetShareExpiry_TakesOverEveryContactIssuedShare_InTheSameTransaction_AndOnlyThose`, `…_WhenTheCallersSystemUserDoesNotResolve_StillClearsTheContactIssuer`, `ContactGrantAuthorizationTests.RecordShareExpiry_ByAnInternalWriteHolder_TakesTheContactIssuedRowOver_SoTheContactCanNeitherLengthenNorRevokeIt(True)`.
- **T2** take-over helper does not stamp `sprk_grantedby` → 2 red (TakesOver…, the cross-surface theory True).
- **T3** take-over helper does not clear `sprk_grantedbycontact` → 3 red (TakesOver…, StillClears…, cross-surface True).
- **T4** the core's take-over does not stamp the systemuser → 1 red: `CoreDefaultMode_ChangingAContactIssuedRow_StampsTheChangingSystemUser_InTheSameWrite`.
- **T5** `BuildBulkUpdateTransaction` skips a `DBNull.Value` instead of clearing → 2 red: `BulkUpdateTransactionTests.BuildBulkUpdateTransaction_WhenAFieldValueIsDBNull_ClearsThatColumnInTheSameTransaction`, `SetShareExpiry_TakesOver…` (its real-builder half).
- **S4a** by-id other-organization refusal echoes the contact's email → 1 red: `Grant_ById_OfANonColleague_IsTheSameAnswerAsAnUnknownId_AndNeverShowsTheirEmail("other-organization")`.
- **S4b** by-id inactive refusal echoes the stored email → 1 red: the same theory ("inactive").
- **S12** membership read skipped when the id names no active contact (the work-done oracle) → 2 red: the same theory, both shapes (85-test ContactGrant filter).
- **S5** the old system-wide email lookup reinstated in front of the scoped read → 2 red: `Grant_ByEmail_WhenAnOutsiderSharesTheColleaguesAddress_GrantsTheColleague`, `Grant_ByAnEmailTwoActiveColleaguesShare_Is409Ambiguous` (message no longer says "in your organization").
- **S6** projection keeps an inactive contact → 2 red: `Grant_ByEmail_AnInactiveContactAtTheSameAddress_IsNotCounted`, `ProjectColleaguesByEmail_KeepsOnlyActiveContactsAtTheAddressWithAConferringMembership`.
- **S7** projection ignores the email → 8 red (every by-email test, incl. the 2-plane new-person theory).
- **S8** only the first organization chunk read → 1 red: `Grant_ByEmail_ForAGrantorInManyOrganizations_ReadsEveryOrganization`.
- **S9** email literal not escaped → 1 red: `BuildColleagueByEmailFilter_NamesTheOrganizations_AndEscapesTheEmail`.
- **S10** a colleague-read fault folded into "nobody" → 1 red: `Grant_WhenTheGranteeLookupCannotBeRead_Is503("email")`.
- **S11** conferring-membership rule dropped from the projection → 1 red: `ProjectColleaguesByEmail_Keeps…`.
- **W1** a date term APPENDED after the builder call in `ReadColleagueMembershipRowsAsync` (the guard's documented blind spot) → 2 red: `ColleagueByEmailWireTests.TheRead_SendsExactlyTheBuiltFilter_ReadsEveryPage_AndProjectsTheLiveShape`, `…_IsReadInChunks_EachWithItsOwnExactFilter` (88-test filter incl. ContactGrant*).
- **W4** the read stops after the first page → 2 red: `TheRead_SendsExactlyTheBuiltFilter…`, `AFaultedPage_IsUnreadable_NeverNobody_EvenAfterAMatchOnAnEarlierPage`.

ArchTests (filter ContactGrantGuardTests + SchemaScriptSolutionMembershipGuardTests, 8 tests):
- **SC1** the script's own `solutioncomponents?` read reinstated → 2 red (the helper guard + `TheSchemaScriptCountsTheColumnAndRelationshipThroughTheirTable`).
- **SC2** the column component loses its `TableId` → 1 red (`TheSchemaScriptCountsTheColumnAndRelationshipThroughTheirTable`).
- **SC3** `GrantedByContactAttribute` misnamed → 1 red (`TheIssuerColumnAgreesWithTheSchemaScript`).
- **W2** the colleague read's `$filter` inlined at the call site → 1 red (`Task 109: membership-junction queries build their $filter, never inline it`).
- **W3** a date term added INSIDE `BuildColleagueByEmailFilter` → 1 red (the same guard, its new builder-body check).

External SPA (vitest): the dashboard's `withProjectName` removed → `OutsideCounselDashboard.projectName.test.tsx` red; restored → 13/13.

Seeds that change logic use a runtime-opaque condition (`Environment.GetEnvironmentVariable(...) is not null`), not a literal `if (false)` (CS0162 is an error here).

### r-final suite results
Affected first (each green before the full runs): ContactGrant* 85/85 (was 73 before the WIP + this round), ColleagueByEmailWireTests
3/3, RecordShareExpiry* 27/27, BulkUpdateTransaction* 7/7; ContactGrantGuardTests + SchemaScriptSolutionMembershipGuardTests +
ExternalAccessQueryIntegrityGuardTests 8/8 (filtered).

Full runs, once, at the end (other agents were running suites concurrently):
- **BFF unit suite** (`tests/unit/Sprk.Bff.Api.Tests`, compiles `tests/integration/auth/**`) on the final code (`76720ca78`):
  **15497 passed, 1 failed, 54 skipped (15552)**. The one failure, `SpeAdmin.SearchItemsTests.SearchItems_WithToken_WhitespaceQuery_Returns400`,
  is contention — that suite makes a real outbound call (session 27 round 16 item 5) — and its isolated re-run is **7/7 passed**.
  An earlier full run on `fdf24ee4f` (all BFF source final; before the 3 wire tests): **15495 passed, 0 failed, 54 skipped**.
- **ArchTests** (`tests/Spaarke.ArchTests`): **603/603** on the final code. (A first full run on `fdf24ee4f` was 602/1 — the task-109
  junction guard flagged the WIP's colleague read; fixed as recorded in item 5, re-run green.)
- **Sprk.Bff.Api.IntegrationTests**: **104/104**. **Spe.Integration.Tests**: **403 passed, 25 skipped, 0 failed (428)**.
- **External SPA**: `npm install --legacy-peer-deps --no-audit --no-fund` OK; `npm run typecheck` (tsc --noEmit) **0 errors**;
  `npm test` (vitest) **13/13**; `npm run lint` **0 problems** (0 errors, 0 warnings); `npm run build` (vite) green.
- Shared UI / TrackingFieldTrio: not touched this round (no rebuild needed).
- Publish size (CLAUDE.md §10) re-measured: base 46.00 MB → branch 46.03 MB, +0.03 MB, 212 = 212 files (§7). No vulnerable BFF package.

Live (read-only) this round: the colleague-by-email query run against spaarkedev1 with the production filter/select/expand; the
SpaarkeCore membership probe (§11 item 3); the privilege read on `prvWritesprk_ExternalRecordAccess`; the schema script's dry run.
No live write. The column is still absent live (dry run: "WOULD create").
