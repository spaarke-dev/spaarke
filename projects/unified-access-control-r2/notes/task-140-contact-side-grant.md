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
target `contact`, `IsSecured` true, in SpaarkeCore. **Since round v1c-v2 (session 27 round 50 item 2) the same two commands
also create, secure and BACKFILL `sprk_grantedbycontactid`** (§13); read it back too
(`…/Attributes(LogicalName='sprk_grantedbycontactid')`: `AttributeType` String, MaxLength 100, `IsSecured` true, in SpaarkeCore),
and `-Verify`'s step (f) must report every row whose lookup names a contact as recording its id. ⚠️ **Merge gate:** this code selects the column on EVERY grant-row read —
it must not deploy to an environment where (a) has not run (POML step 1: "code that binds the new lookup must not merge before
(c) passes"). Since round v1c-v1 the reconciliation job's grant scan also selects the column (R1's contact-issued rule, §12.1):
before (a) runs, that scan fails every tick — fail-safe (the run is recorded failed and writes nothing) — the same gate.

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
| 2 | Round 34 item 3 (BINDING) — internal take-over outside the core | **Closed.** `SetRecordShareExpiryEndpoint` adds, for every share whose `sprk_grantedbycontact` is set, `sprk_grantedbycontact = DBNull.Value` (clear) and `sprk_grantedby = EntityReference(systemuser, caller)` to that share's fields in the ONE `ExecuteTransactionRequest` (`ExternalGrantLifecycle.AddInternalTakeOverFields`); the caller's systemuser is read only when such a share exists; an unresolvable caller still clears the contact issuer (the core's "an audit field never blocks the write" rule). Writers enumerated (every BFF write to `sprk_externalrecordaccesses`): the core (take-over existed; its stamp now under test), set-record-share-expiry (added), and the DEACTIVATING writers `/revoke`, project closure and the reconciliation job — an inactive row is outside every contact path (the contact's grant, list and revoke change only ACTIVE rows it issued), so nothing there can be re-lengthened or revoked by the contact. ⚠️ **Corrected in §12 (round v1c-v1):** the reconciliation job does NOT only deactivate — its rule R1 writes `sprk_expiresdate` onto an ACTIVE row that has none and leaves `sprk_grantedbycontact` in place (verifier v1c item 2). R1 is not an internal Write holder (round 34 item 3 is not engaged), and session 27 round 42 item 2 now decides what R1 does to a contact-issued row (§12). Non-product writes: live read-only 2026-10-04, `prvWritesprk_ExternalRecordAccess` is held by System Administrator, System Customizer and Service Writer only — no Spaarke role — so no ordinary internal Write holder can change a grant row outside the BFF. Docs: uac-access-control.md, entity-schema.md (writer row + column note). |
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

## 12. Fix round v1c-v1 (2026-10-05) — verifier v1c items 1–16, session 27 round 42

Branch `task/uac-r2-140-x1-v1c-v1`, created from `task/uac-r2-140-x1-v1c` @ `2c2bcb232`. Binding decisions read from
`work/unified-access-control-r2` @ `83460442d` (rounds 1–43); **round 42 is task 140's own** (the main session's answers to
this verification's two open findings, under the owner's round-15 standing directive) and decides items 2 and 3 below.

| # | Item | Outcome |
|---|---|---|
| 1 | Summary | Both LOW findings closed in code, tests and docs (items 2, 3). A third, pre-existing defect on the same read path was found and fixed (below: **the TZI wire shape**). |
| 2 / 14 | The take-over writer list said the reconciliation job only DEACTIVATES grant rows | **Closed, both halves.** (a) The three statements are corrected — `docs/architecture/uac-access-control.md` (the take-over bullet now names R1 as the one writer that changes an ACTIVE contact-issued row and keeps the contact issuer), §11 item 2 above (correction note) and the POML's second `<outcome>` (bracketed correction) — and the same fact is now in `entity-schema.md` (writer table + the `sprk_grantedbycontact` column note) and the job's class doc. (b) **Round 42 item 2 implemented** (neither skip, take-over nor plain stamp): R1, on a CONTACT-issued row with no expiry, stamps the EARLIER of today + 90 and the latest date the issuing contact's own grant on the same record lasts at the row's level or above, DEACTIVATES the row when the issuer holds no such grant there, keeps `sprk_grantedbycontact` (no issuer column is written — no internal person acted), and reports each such row (per-row log line + `contactIssued` block in the R1 entry of `ResultJson` + heartbeat counts). Details below. |
| 3 | Race between the contact-mode check and its write | **Closed per round 42 item 1 (option A).** Every contact-path write to a row it checked was its own is CONDITIONAL on the version that check read: `@odata.etag` (now read on `ExternalGrantRow.ETag`) sent as `If-Match` through the new `DataverseWebApiClient.UpdateIfMatchAsync`. Sites: the core's contact-issuer PATCH of the caller's own row; the contact revoke's deactivation; and — the same race class, closed with it — the contact-issuer mode's two duplicate collapses (match path and post-create), via `ExternalGrantLifecycle.DeactivateIfUnchangedAsync`. A 412 → **409 `managed_elsewhere` with the existing copy**; the row is re-read (`ReReadChangedRowsAsync`, logged; carried on `GrantUpsertOutcome.ChangedRowNow`); **no retry**. A revoke that had already ended another of the caller's own rows on the grant (a duplicate pair) answers the same 409 with a detail that does not claim "Nothing was changed" (`PartlyManagedElsewhereDetail`). A collapse leaves a taken-over duplicate in place (the caller's grant still succeeds on the survivor). A row read without a version is never written unconditionally (`ArgumentException` → the route's 500 with its message). The 409's ProblemDetails title for `managed_elsewhere` is now "Access is managed by someone else" (it read "Existing access is higher", which is the would-lower title). The SPA's issued-grants list re-reads after any refused revoke, so a row that changed hands drops out instead of offering a Revoke that can only 404. |
| 4–13 | Verifier confirmations (WIP commit, round 34 item 3, `-Verify`, by-id, email scope, `.NOTES`, SPA, suites, seeds, compliance) | Re-verified by re-running every suite on the final code (below); no behaviour they describe was changed except where items 2/3 required. Item 13's publish size is RE-MEASURED below because this round changes BFF source. |
| 15 | G-140-1 (operator schema `-Apply` / `-Verify` / read-back) | **Pending manual gate — unchanged** (§5, exact commands). |
| 16 | G-140-2 (criterion 21 live gate) | **Pending manual gate.** Prerequisites now also include this branch's BFF: without the TZI fix below, steps (b), (e), (f) and (h) fail live (every read of a dated grant row threw). |

### Discovered: every read of a dated grant row threw (the TZI wire shape) — fixed

- **Fact (read-only, live, spaarkedev1, 2026-10-05).** `sprk_externalrecordaccess.sprk_expiresdate` is `Format = DateOnly`,
  `DateTimeBehavior = TimeZoneIndependent` (metadata read; task 098 §2.3 had it), and the Web API returns a TZI value as a
  timestamp: `"sprk_expiresdate":"2026-12-10T00:00:00Z"` (row read). Every row also carries `"@odata.etag":"W/\"25734128\""`.
  `sprk_contactorganization.sprk_startdate/sprk_enddate` are DateOnly BEHAVIOUR (`"yyyy-MM-dd"`) — unaffected; task 142's ledger
  column `sprk_grantedexpiry` is DateOnly behaviour in its schema script — unaffected.
- **Defect.** `ExternalGrantRow.ExpiresDate` is `DateOnly?`; System.Text.Json's DateOnly converter accepts only `yyyy-MM-dd`
  and throws `JsonException: The JSON value could not be converted to System.Nullable[DateOnly]. Path:
  $.value[0].sprk_expiresdate` — reproduced through the REAL `DataverseWebApiClient` over the wire (seed X1 below). Since task
  023 (2026-09-08; on master too) every read of a row carrying an expiry — i.e. every row the BFF has written since task 097 —
  failed: `/grant` re-grant (match path) 500; `/revoke` (reads the row by id) 500; `set-record-share-expiry`; the Assigned-To
  materializer's reads; and on this task's routes the grantor's expiry-cap read (so EVERY contact grant answered 503
  `grantor_access_unreadable`), the list (503) and the revoke (503). A new grant's create path survived only because its
  post-create duplicate check swallows the throw. No offline test could see it: every double serialized rows in a shape the
  test chose (`yyyy-MM-dd`).
- **Fix.** `DataverseDateOnlyJsonConverter` on `ExternalGrantRow.ExpiresDate`: reads `yyyy-MM-dd` and `yyyy-MM-ddT…` (the
  calendar date is the leading ten characters as written — a TZI value is not converted), writes `yyyy-MM-dd`, throws on anything
  else. Pinned by `GrantRowWireTests`, which serve the live response verbatim (ids aside) to the real client.
- **Escalation note for the main session.** This is a production defect on master, independent of task 140; it ships with this
  branch. A FAILURE-MODES entry is recommended (`.claude/**` is main-session-only — exact text in §12.7).

### 12.1 R1 on a contact-issued undated row — the rule as built (`ExternalAccessReconciliationJob.ResolveContactIssuedAsync`)

- **One definition of "the issuer's own grant".** The contact route's expiry-cap read was extracted (moved, not rewritten) into
  `ExternalGrantLifecycle.ReadContactHeldGrantsAsync`: the contact's own active rows on the record and, unless the record is
  Secure/Limited (or its flags are unreadable — fail closed), the org-wide rows of its CONFERRING organizations (task 109's
  set), at the given level or above, a direct row naming an inactive or deleted firm excluded (ISS-026). The route calls it with
  "confers today"; R1 calls it with the row's own level and also keeps undated rows (this run may stamp them).
- **Judged as this run leaves the table.** An issuer's own undated row that plain R1 stamps counts at the default date; one R2
  ends this run counts for nothing; one that is itself contact-issued and undated is decided first (dependency order); rows that
  only vouch for each other (a cycle with no outside source) are decided together, each counting the others for nothing — the
  fail-closed reading, which can end a row early but never lets one outlive its issuer.
- **"Holds an active grant there" = a grant ROW** (round 42's words). Access resting only on an undated term (a workforce
  contact's standing-grant / organization-expansion composition) is not a grant on the record and carries no date; such an
  issuer's undated row is ended — the safe direction; the issuer can grant again from the SPA, where the route evaluates the
  term. A row naming no record is ended (its issuer holds no grant "there").
- **Faults never write.** Unreadable memberships, a failed grant read, an issuer undated row this run did not plan (appeared
  after the scan / beyond a truncated one) or a dependency on such a row → the row is LEFT UNCHANGED, reported `Unresolved`, and
  the run is reported partial (`Success = false`) so tomorrow's tick retries it.
- **Report-only mode** decides and reports exactly what a write pass would do (it only reads); writes still wait for the owner
  switch. Nothing else in R1–R3 changed; a run with no contact-issued row resolves nothing and reads nothing extra.

### 12.2 Placement + justification (CLAUDE.md §10 / §11) — new surface this round

Placement: BFF (+ one method on the shared `Spaarke.Dataverse` client the BFF's grant code already uses). **No new endpoint, DI
registration, option, job, column, PCF or package.** The job resolves the already-registered `DataverseWebApiClient` and
`ExternalParticipationService` from its run scope, only when a contact-issued undated row exists.

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `DataverseWebApiClient.UpdateIfMatchAsync` | `UpdateAsync` (unconditional; a PATCH without If-Match UPSERTS); `DataverseWebApiService.UpdateRecordFieldsIfUnchangedAsync` (conditional, but on another client keyed by logical name + a metadata lookup, with a `long` version); `ContactIdentityStore`'s private If-Match writer (contacts only) | `UpdateAsync` cannot gain a parameter without breaking every test double that overrides it, and its upsert default is relied on; the grant code reads and writes through this client and holds the exact ETag string it read. Same exception contract as `UpdateRecordFieldsIfUnchangedAsync` (412 → `DBConcurrencyException`, 404 → `KeyNotFoundException`) | Round 42 item 1 unimplementable: a contact's write lands over an internal take-over |
| `DataverseDateOnlyJsonConverter` | System.Text.Json's DateOnly converter (the defect) | Not configurable for the timestamp shape; `DateTime` would change every consumer of a date | Every read of a dated grant row throws (above) |
| `ExternalGrantRow.ETag` | the one row shape | extended | No version to send |
| `ExternalGrantLifecycle.DeactivateIfUnchangedAsync` + `ConditionalDeactivation`; `ReReadChangedRowsAsync` | `DeactivateAsync` (unconditional) | `DeactivateAsync`'s callers (`/revoke`, the default-mode collapse, the materializer, closure) are internal decisions and stay unconditional; a flag would mix two contracts; one sibling beside it | The contact revoke / collapse race stays open |
| `ExternalGrantLifecycle.ReadContactHeldGrantsAsync` + `ContactHeldGrants` | `ContactGrantEndpoints.ResolveGrantorExpiryCapAsync`'s inline read | EXTRACTED from it (the route now calls it) | Two definitions of "the contact's own grant" (route vs job) would drift |
| R1's contact-issued resolution (`ResolveContactIssuedAsync`, `ContactIssuedUndated`/`Outcome`/`Report`/`Row`, `PlannedChange.ContactIssued`, `RuleCounts.ContactIssued`, three attribute constants) | R1 in the same job | It IS R1 (no new rule entry, schedule or job) | Round 42 item 2: an R1 write would let a contact-issued grant outlive its issuer |
| `GrantUpsertOutcome.ChangedConcurrently` / `ChangedRowNow`, `ConcurrentChangeRefusalAsync`, `ContactGrantEndpoints.PartlyManagedElsewhereDetail` | the managed_elsewhere refusal | reused (same code, same copy) | — (the refusal path of item 3) |

§11.5 (complexity, not LOC): `ExternalAccessReconciliationJob.cs` grows 871 → 1313 lines (≈45 % doc comments). The addition is
R1's own decision for one row class, behind the job's one reason to change (a grant row's own state as the truth) — cohesive, not
a second responsibility; its I/O is the shared reader. It would be extracted if a second consumer of the decision appeared.

### 12.3 Tests (KEEP paths) and what each pins
- `ContactGrantAuthorizationTests` +12 (each a one-input twin pair: the internal change landed in the window or not):
  grant PATCH race (409, existing copy, the take-over's date/stamp/level stand, one refused conditional write, re-read, nothing
  after); revoke race (409, existing copy, row stays active and the internal user's); revoke of a duplicate pair with one taken
  over (the other ended, invalidation, partial copy); match-path collapse leaves a taken-over duplicate; post-create collapse leaves
  a taken-over raced row; a row read without a version sends nothing (grant / revoke → 500 with message). The in-memory table now
  versions every row (`@odata.etag` bumps on every write) and judges `If-Match` like Dataverse.
- `GrantRowWireTests` (new, 10): the REAL `DataverseWebApiClient` over a loopback server (it builds its own `HttpClient`, so a
  loopback port, not a TestServer — ADR-038 §7's B1 replacement): the live grant-row response decodes (TZI expiry, ETag) for a
  query and a by-id read; a bare date and a null still read; `If-Match` carries the version verbatim; 412 → `DBConcurrencyException`
  with no retry; 404 → `KeyNotFoundException`; no version → nothing sent; `DeactivateIfUnchangedAsync` sends each row's own version
  and reports the changed one.
- `ExternalAccessReconciliationTests` +16: +90 when the issuer holds longer; capped at the issuer's date; deactivated for five
  "holds no grant there" shapes (revoked, expired, lower level, none, another record) — issuer kept and only the state columns
  written in every case; unresolved on unreadable memberships / grants (row unchanged, run partial); the issuer's own undated row
  judged as this run leaves it (plain R1 → +90 / R2 → ended); a chain decided in dependency order; a cycle ended; the issuer's
  firm's org-wide grant counts on a Standard record and not on a Secure one; report-only decides and reports, writes nothing.
- `ContactGrantGuardTests` +1 theory (2): the contact-side surface sends no unconditional grant-row write.
- External SPA vitest +1: a refused revoke shows the message verbatim and re-reads the list.

### 12.4 Perturbation record (each seed alone, affected tests run, file restored and touched)

Seeds that change logic use a runtime-opaque condition (`Environment.GetEnvironmentVariable("SEED_NEVER_SET_140") is null`).
Filters: CG = `ContactGrant*` + `GrantRowWireTests` (107 tests); RJ = `ExternalAccessReconciliationTests` (53); both (160).
Every seed went red; every file was restored and touched; the tree was verified identical to the commit afterwards.

Item 3 — the race (CG):
- **S1** the contact-issuer PATCH unconditional again (`UpdateAsync`) → 3 red: the grant race theory (both twins — the
  conditional write is asserted in each) and the no-version grant case.
- **S2** no re-read after the refused PATCH → 1 red (grant race, take-over twin).
- **S3** a blind retry (unconditional PATCH) after the 412 → 1 red (grant race, take-over twin).
- **S4** the revoke deactivates unconditionally (`DeactivateAsync`) → 4 red (revoke race both twins, duplicate pair, no-version
  revoke). **A1** the same seed against the ArchTests → 1 red (`TheContactSideSurfaceWritesGrantRowsOnlyConditionally`, the
  endpoints file).
- **S5** the revoke ignores a changed row (answers 200) → 2 red (revoke race, duplicate pair).
- **S6** the partial-revoke detail replaced by the "Nothing was changed" copy → 1 red (duplicate pair).
- **S7** the revoke's re-read skipped → 2 red (revoke race, duplicate pair).
- **S8** the contact-issuer collapses unconditional → 3 red (match-path collapse; post-create collapse, both twins).
- **S9** `DeactivateIfUnchangedAsync` counts a changed row as deactivated → 3 red (revoke race, duplicate pair, the wire test).
- **S10** `UpdateIfMatchAsync` sends no `If-Match` → 2 red (wire: the header test and `DeactivateIfUnchanged…`).
- **S11** 412 not mapped to `DBConcurrencyException` → 2 red (wire: the 412 test and `DeactivateIfUnchanged…`).
- **S12** an empty / whitespace version still sent → 2 red (wire: both no-version cases).
- **S14** the colleague's cache not invalidated when a deactivation committed beside a conflict → 1 red (duplicate pair).
- **S13** (SPA vitest) the issued-grants list not re-read after a refused revoke → 1 red (the new vitest).

Item 2 — R1 on a contact-issued undated row (RJ):
- **J1** no cap (always +90) → 4 red (capped, chain, firm's org-wide grant on a Standard record, report-only).
- **J2** (re-seeded as **J2b** after the first form failed to compile on definite assignment) stamp the default instead of
  deactivating → 9 red (all five "holds no grant there" shapes, the Secure firm case, the R2 twin, the cycle, report-only).
- **J3** the contact issuer cleared in the same write → 5 red (every stamping case asserts the issuer kept and only
  `sprk_expiresdate` written).
- **J4** plain-R1 stamps not anticipated → 1 red (the issuer's own undated row twin).
- **J5** R2 deactivations not anticipated → 1 red (the R2 twin — the issuer's row reaches the decision as unknown).
- **J6** no dependency order (a contact-issued issuer row decided as "nothing") → 1 red (chain).
- **J7** a read fault treated as "no grant" (ended) → 2 red (both unreadable cases).
- **J8** the row's level ignored → 1 red ("lower-level").
- **J9** resolution skipped in report-only mode → 1 red (report-only).
- **J10** contact-issued rows planned as plain R1 → 16 red (every new R1 test).

The shared reader (both):
- **R1** a direct row naming an inactive firm counted → 1 red (the route's firm theory) — the extraction kept the guard.
- **R2** Secure/Limited ignored (org-wide rows read) → 2 red (the route's Secure cap test AND the job's Secure firm case — one
  definition, both consumers).

The wire shape (CG):
- **X1** the converter removed → 3 red (both live-shape reads and `DeactivateIfUnchanged…`): the REAL client throws
  `JsonException: The JSON value could not be converted to System.Nullable[DateOnly]. Path: $.value[0].sprk_expiresdate` on the
  live response — the production defect, reproduced offline.

### 12.4b Suite results (the final code; full runs once, at the end; other agents were running suites concurrently)

Affected first (each green before the full runs): `ContactGrant*` + `RecordShareExpiry*` + `BulkUpdateTransaction*` +
`ColleagueByEmail*` **134/134**; `GrantRowWireTests` **10/10**; `ExternalAccessReconciliationTests` **53/53**; the whole
access-control / external-access / grant set (`Sprk.Bff.Api.Tests.AccessControl` + `ExternalAccess` + `Grant`) **3059/3059**;
`ContactGrantGuardTests` **6/6**.

Full runs, once, on the final code (BFF source = `ddef8dd04`; the final commit adds only the note and the POML):
- **BFF unit suite** (`tests/unit/Sprk.Bff.Api.Tests`, which compiles `tests/integration/auth/**`): **15536 passed, 0 failed,
  54 skipped (15590)** — +38 over r-final's 15552, exactly this round's new tests (12 + 10 + 16).
- **ArchTests** (`tests/Spaarke.ArchTests`): **605/605** (+2: the new theory).
- **Sprk.Bff.Api.IntegrationTests**: **104/104**. **Spe.Integration.Tests**: **403 passed, 25 skipped, 0 failed (428)**.
- **External SPA**: `npm install --legacy-peer-deps --no-audit --no-fund` OK; `npm run typecheck` (tsc --noEmit) **0 errors**;
  `npx vitest run` **14/14**; `npm run lint` **0 problems**; `npm run build` (vite) green (the pre-existing >500 kB chunk
  warning only).

### 12.5 Publish size (CLAUDE.md §10 item 4) and CVE

Fresh trees exported from the commits (BFF + shared + config + root build files) to SHORT paths, each `dotnet publish -c
Release`, zipped with PowerShell `Compress-Archive -CompressionLevel Optimal` over `deploy\api-publish\*` (incl. PDBs):

| Tree | Commit | Files (PDBs) | Bytes | MB |
|---|---|---|---|---|
| `C:\wt140m3` — task base | `6b685f0d4` (integ/uac-r2-batch4) | 212 (4) | 48,236,151 | 46.00 |
| `C:\wt140p3` — this round's base | `2c2bcb232` | 212 (4) | 48,270,036 | 46.03 |
| `C:\wt140b3` — this round | `ddef8dd04` (the BFF source of the final commit) | 212 (4) | 48,282,230 | 46.05 |

This round: **+0.01 MB** (+12,194 bytes); the whole task: **+0.04 MB** (+46,079 bytes); 212 = 212 = 212 files. ≤ 60 MB. No
package added or changed (no `*.csproj` / `Directory.Packages.props` change this round). `dotnet list package --vulnerable
--include-transitive` (BFF): **no vulnerable packages**. The task base matches the earlier rounds' measurement (46.00 MB).

### 12.6 Quality gates (Step 9.5, FULL rigor) — this round's diff

- **code-review** — no Critical. W1: `ExternalAccessReconciliationJob.cs` 871 → 1313 lines — evaluated per §11.5 (12.2): one
  rule's decision, cohesive; noted for the PR. W2: R1's contact-issued reads run per such row (memberships cached per issuer);
  the population is admin-cleared contact-issued rows only. W3: an issuer with a long dated grant AND an undated row the run did
  not plan is reported `Unresolved` although the answer could not change — the conservative direction (unchanged, retried
  tomorrow). W4 (**deploy order, extends §5's merge gate**): the job's grant scan now selects `sprk_grantedbycontact`, so in an
  environment where G-140-1 has not run the scan fails every tick — fail-safe (the run is recorded failed, nothing is written),
  and the same gate as the BFF's `RowSelect`. W5: the contact writes depend on `@odata.etag` being present — pinned by the live
  shape in `GrantRowWireTests`; a missing version fails closed (500 with a message), never an unconditional write.
- **adr-check** — compliant: ADR-001 (no new route), 002 (no plugin), 003 (fail closed: a 412 refuses, a missing version
  refuses, a read fault never writes, report-only by default), 007 (no Graph), 008 (filters unchanged; the handler still
  re-validates), 009 (the ONE invalidation routine, also after a partial revoke), 010 (no new registration), 013 (no AI), 028
  (no plane branching, no secret), 036 (the job still never throws from `ExecuteAsync`; per-row faults are reported, not
  swallowed — Error log + partial status), 038 (no `Mock<HttpMessageHandler>`: a loopback server for the client that builds its
  own `HttpClient`; no DI or ctor tests; one-input twins), 052 (the job's placement is unchanged).

### 12.7 `.claude/` edit recommended (main session only)

Add to `.claude/FAILURE-MODES.md` (next free AP number), verbatim:

> **AP-xx: A Dataverse DateOnly-FORMAT column with TimeZoneIndependent BEHAVIOUR comes back from the Web API as a timestamp.**
> `"sprk_expiresdate":"2026-12-10T00:00:00Z"`, not `"2026-12-10"` (only DateOnly *behaviour* returns a bare date).
> System.Text.Json's `DateOnly` converter throws on the timestamp, so a `DateOnly?` property bound to such a column fails every
> read that carries a value — and in-memory doubles that serialize the shape the test chose never show it. Before typing a
> Web API row property as `DateOnly`, read the column's `DateTimeBehavior`; for TimeZoneIndependent use a converter that takes the
> leading `yyyy-MM-dd` as written (`DataverseDateOnlyJsonConverter`, task 140), and pin it with a wire test that serves the live
> response. Found by unified-access-control-r2 task 140 (round v1c-v1, 2026-10-05) after it had broken every dated grant-row read
> since task 023.

Live (read-only) this round: the `sprk_externalrecordaccesses` row read (wire shape + ETag, with and without the BFF's
formatted-value Prefer header), three attribute-metadata reads (`sprk_expiresdate`; `sprk_contactorganization.sprk_startdate/
sprk_enddate`), and an attempted metadata read of task 142's `sprk_assignedaccess.sprk_grantedexpiry` (the table does not exist
live yet; its schema script declares DateOnly behaviour). No live write.

## 13. Fix round v1c-v2 (2026-10-05) — re-verification of `task/uac-r2-140-x1-v1c-v1`; session 27 round 50

Branch `task/uac-r2-140-x1-v1c-v2`, created from `task/uac-r2-140-x1-v1c-v1` @ `55d84c474`. Binding decisions read from
`work/unified-access-control-r2` @ `2cc4f3d2b` (rounds 1–52). **Round 50 is task 140's own** (the main session's answers to
this re-verification): item 1 (the TZI read fix on master as its own PR) is another lane's; **item 2 (provenance survives a
deleted issuer) and item 3 (the two unpinned guards) are closed here**.

| # | Item | Outcome |
|---|---|---|
| 1 | V6 — the R2 check in `Effective()` was pinned by no test (its twin seeded the issuer's R2-ended row UNDATED, which counts for nothing whatever R2 does) | **Closed.** `R1_AnIssuersOwnRow_IsJudgedAsThisRunLeavesIt` is now a 3-shape theory: `stamped-by-R1`, `ended-by-R2-undated` (kept: it pins `Unknown()`'s R2 exclusion — the earlier J5), and the new **`ended-by-R2-dated`**: the issuer's own row carries +200 and R2 ends it this run (the issuer reader sees the firm active, as the test already set up). Only knowing R2 ends it decides "deactivate" over "+90". **Seed V6** (the check made runtime-false) → **1 red** (exactly that case); restored. |
| 2 | V11 — the `Unknown()` guard was pinned by no test | **Closed.** New theory `R1_AnIssuersUndatedRowThisRunDidNotPlan_LeavesTheRowItIssuedUnchanged_AndTheRunPartial` (`created-after-the-scan`; `beyond-a-truncated-scan`): the issuer's only grant is an undated row the issuer reader sees but the scan never returned. Asserted: no write for the row, no expiry, still active, `unresolved` 1 / `deactivated` 0, outcome `Unresolved`, the "whose own date could not be decided" log line, `Success=false`, the "left unchanged" message, heartbeat `partial`; an ordinary undated row in the same run is still stamped. Positive twin: `stamped-by-R1` above (the same row, planned). **Seed V11** (`|| Unknown(r)` made runtime-false; deleting it outright is CS8321 — an unused local function — so the opaque form is the seed) → **2 red** (both shapes); restored. |
| 3–16 | Verifier confirmations (seeds V1–V5, V7–V10, V12, V13, the SPA seed; no tautological race tests) | No behaviour they describe was changed; every suite re-run below. |
| 17–21 | Live facts (TZI wire shape, optimistic concurrency, DateOnly-behaviour DTOs unaffected, no live write, the column still absent) | Unchanged. Re-confirmed read-only this round through the schema script's dry run and `-Verify` (§13.1) — `sprk_grantedbycontact` and `sprk_grantedbycontactid` both absent live (no name collision for the new column). |
| 22–28 | Suites / publish of the previous round | Re-run on this round's final code (§13.5, §13.6). |
| 29–42 | POML well-formed; round 42 items 1 and 2 as implemented; round 34 item 3; earlier verifier items 3–8 | Unchanged and still met (the round-42 behaviour is extended, not altered — see round 50 item 2). |
| 43 | "Tests and seeds per case" not met for the R2-ended check and the `Unknown()` guard | **Met** — items 1 and 2. |
| R50.2 | **A deleted issuing contact: the grant's provenance survives the delete, and the grant ends** (BINDING) | **Implemented** — §13.1. |

### 13.1 Round 50 item 2 — `sprk_grantedbycontactid` (the rule as built)

- **The column.** `sprk_externalrecordaccess.sprk_grantedbycontactid` — single-line text (100), the issuing contact's id in ONE
  text form (`ExternalGrantLifecycle.ContactIssuerProvenance` = `Guid.ToString("D")`, lower-case). Field-secured with the same two
  profiles as the lookup (a forged value would end an undated grant; an erased one would let a deleted issuer's grant be stamped).
  Selected on every grant-row read (`RowSelect`) and by the job's scan — **the same deploy-order gate G-140-1**.
- **Every write that sets or clears the lookup sets or clears it in the same write.** Set: the contact grant's create
  (`BuildGrantPayload`). Cleared: the grant core's internal take-over (now `ExternalGrantLifecycle.AddInternalTakeOverBinds`,
  moved beside the SDK twin `AddInternalTakeOverFields`, which `set-record-share-expiry` uses) — and a row whose issuing contact
  was already deleted (provenance only, `ExternalGrantRow.IsContactIssued`) is taken over the same way, so a later R1 never reads
  an internal user's grant as a deleted contact's. **The collapses write neither column** (they only deactivate), so nothing
  there pairs; the contact-mode PATCH of the caller's own row writes neither. Pinned behaviourally per writer and, for any NEW
  writer, by `ContactGrantGuardTests.EveryWriterOfTheIssuerLookupAlsoWritesItsProvenance` (text, per file, with its limits stated
  in the test: four key spellings; the writer set is exactly the two files, so a regex that stopped matching or a writer added
  elsewhere fails).
- **R1.** An UNDATED active row whose provenance is set while the lookup is empty — only a deletion (RemoveLink) leaves that shape
  — is DEACTIVATED with no read and reported `IssuerDeleted` (count in `contactIssued.issuerDeleted`, per-row line, heartbeat
  `r1ContactIssuedIssuerDeleted`); its provenance is kept; it counts for nothing as another row's issuer grant and is part of the
  run's decided set (a row its grantee issued is ended in the same run, not left Unresolved). A provenance that is not even a GUID
  ends the row too (fail closed: provenance without its lookup is never read as "no contact issuer"). A DATED row whose issuer was
  deleted stands until its date — owner G2 (ii), no cascade; R1 decides undated rows only. Report-only decides and reports it.
- **The schema script** (`scripts/Deploy-ExternalRecordAccessContactGrantor.ps1`): new step **(a2)** creates the column (prefix
  assertion shared with (a) — AP-13), **(c)** secures BOTH columns, **(d)** adds it to SpaarkeCore through the membership helper
  with its `TableId`, new step **(f)** BACKFILLS it from the lookup — `-Apply` writes each missing/different value with `If-Match`
  on the row's version (a 412 is reported and left for a re-run; a row read without a version is refused), the dry run counts,
  `-Verify` FAILS while any row lacks it, and rows recording a deleted issuer are reported for information. The decision and the
  write rule live in `scripts/common/GrantProvenanceBackfill.ps1` (dot-sourced) so they run offline.
- **Live, read-only, this round** (spaarkedev1, operator az identity): the dry run (with `-BffApplicationIds`) reports (a) WOULD,
  (a2) WOULD create `sprk_grantedbycontactid (text, MaxLength 100)`, (b) all OK, (c) WOULD for both columns, (d) both profiles
  OK, (f) WOULD — "nothing was written"; `-Verify` exits 1 listing the six gaps (both columns, the relationship, both locks, the
  backfill). No live write.

### 13.2 Placement + justification (CLAUDE.md §10 / §11) — new surface this round

Placement: BFF + the schema script that already owns this table's task-140 columns. No new endpoint, DI registration, option,
job, PCF or package. **One new COLUMN** (owner-decided, round 50 item 2).

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| Column `sprk_grantedbycontactid` | the lookup `sprk_grantedbycontact` | The lookup cannot outlive the row it points to (RemoveLink empties it; Restrict blocks privacy deletions; a cascade is ruled out by G2 (ii)) — round 50's own justification | A deleted contact's undated grant looks internally issued and R1 stamps it +90 — an indefinite, issuer-less grant |
| `ExternalGrantRow.GrantedByContactProvenance` + `IsContactIssued`; `ExternalGrantLifecycle.GrantedByContactIdAttribute`, `ContactIssuerProvenance` | the one row shape; `GrantedByContactAttribute` | extended in place | The column cannot be read, written or compared in one text form |
| `ExternalGrantLifecycle.AddInternalTakeOverBinds` | the core's inline Web API take-over binds | MOVED from the core, beside the SDK twin, so both pairings live in one file (the guard pins the writer set) | The core's take-over clears the lookup and leaves the provenance; R1 later ends an internal user's grant as a "deleted issuer's" |
| `ContactIssuedUndated.IssuerDeleted`, `ContactIssuedOutcome.IssuerDeleted`, the report/heartbeat count | R1's contact-issued resolution | It IS R1 (no new rule, schedule or job) | Round 50 item 2 unimplemented |
| `scripts/common/GrantProvenanceBackfill.ps1` | the schema script; `DataverseSolutionMembership.ps1` (another concern) | Part of the script, extracted only so its decision and write rule can be exercised offline (the script needs a live token end to end) | The backfill would first run untested at a live `-Apply` |

§11.5: `ExternalAccessReconciliationJob.cs` grows ~65 lines of R1's own decision (one more outcome for one row class) — cohesive,
same reason to change.

### 13.3 Tests (KEEP paths) and what each pins

- `ExternalAccessReconciliationTests` +8 cases: the dated R2 twin (V6); the unplanned-issuer-row theory ×2 (V11); the
  deleted-issuer theory ×3 (`issuer-exists` twin, `issuer-deleted`, `issuer-deleted-unparseable`); a dated deleted-issuer row left
  unchanged; a row issued on the strength of a deleted issuer's row ends in the same run; report-only also reports `IssuerDeleted`.
  The fake now serves each row **projected to the FetchXML's `<attribute>` list**, as Dataverse does — so the scan's select is
  pinned by behaviour (a column it stops selecting is absent and the rule goes red: seed P7).
- `ContactGrantAuthorizationTests` +2 and 3 extended: the create records the provenance in the same payload; the core take-over
  clears both; a deleted issuer's row is taken over (provenance cleared, systemuser stamped, the empty lookup not re-sent); a
  contact's grant over a deleted issuer's row is 409 `managed_elsewhere`, row untouched. The in-memory table models the column on
  create, PATCH and SDK bulk writes (and throws on an unmodelled shape).
- `RecordShareExpiryTests` +1 and 2 extended: the transaction carries both clears (through the REAL transaction builder); a
  deleted issuer's share is taken over.
- `GrantProvenanceBackfillScriptTests` (new, 4): runs `GrantProvenanceBackfill.ps1` in `pwsh` over rows shaped as the Web API
  serves them, with a recording PATCH: which rows (missing / different / other casing; a recorded one and an empty-lookup one
  untouched; the text form equals the BFF's `ContactIssuerProvenance`); `If-Match` = each row's version, one PATCH per row, body
  = the provenance only, a 412 counted and not retried; any other failure stops it; no version → nothing sent.
- `ContactGrantGuardTests` +2 facts, 1 updated: provenance column ↔ script ↔ `RowSelect`; every writer of the lookup writes the
  provenance (census of writers); the script's subcomponents are now 3 (lookup, relationship, provenance column).

### 13.4 Perturbation record (each seed alone, affected tests run, file restored and touched; trees verified byte-identical)

Filter for BFF seeds: `ExternalAccessReconciliationTests` + `ContactGrant*` + `GrantRowWire*` + `RecordShareExpiry*` (190 before
round 50 item 2's tests, 198 after).
- **V6** the R2 check in `Effective()` runtime-false → 1 red (`ended-by-R2-dated`).
- **V11** `|| Unknown(r)` runtime-false → 2 red (both shapes of the new theory).
- **P1** the create stops writing the provenance → 1 red (the create test).
- **P2** the Web API take-over stops clearing it → 3 red (the three core take-over tests).
- **P3** the core's take-over condition back to "lookup set" → 1 red (the deleted issuer's take-over).
- **P4** the SDK take-over stops clearing it → 4 red (three `RecordShareExpiryTests` + the cross-surface theory's take-over twin).
- **P5** `set-record-share-expiry` takes over only rows whose lookup is set → 1 red (the deleted issuer's share).
- **P6** R1 ignores the provenance → 4 red (deleted theory ×2, the chain, report-only).
- **P7** the scan stops selecting the provenance → 4 red (the same four, through the projecting fake).
- **P8** deleted-issuer rows left out of the run's decided set → 1 red (the chain: the dependent row would be left Unresolved).
- **P9** reported as plain `Deactivated` → 4 red.
- **P10** an unparseable provenance read as "no issuer" → 1 red (`issuer-deleted-unparseable`).
- **P11** R1 also ends a DATED deleted-issuer row → 1 red (the dated test).

Backfill helper (filter `GrantProvenanceBackfill*`, 4): **B1** recorded rows not skipped → 1; **B2** text form not normalised → 1;
**B3** case-insensitive compare → 1; **B4** `If-Match` dropped → 1; **B5** every failure counted as "changed" → 1; **B6** the
no-version refusal removed → 1; **B7** empty-lookup rows not skipped → 1.

ArchTests (filter `ContactGrantGuardTests`, 8): **A1** the SDK provenance clear removed → 1 (the pairing guard); **A2** a third
writer of the lookup added in `SetRecordShareExpiryEndpoint` → 1 (the census); **A3** the script's column renamed → 1 (the
agreement fact); **A4** the script's provenance component dropped from (d) → 1 (the subcomponent count).

### 13.5 Suite results (the final code; full runs once, at the end; other agents were running suites concurrently)

Affected first: the 212-test set (`ExternalAccessReconciliationTests`, `ContactGrant*`, `GrantRowWire*`, `RecordShareExpiry*`,
`GrantProvenanceBackfill*`, `ColleagueByEmail*`, `BulkUpdateTransaction*`) **212/212**; `ContactGrantGuardTests` +
`SchemaScriptSolutionMembership*` **12/12**.

Full runs, once, on the final code (BFF source = `6f85d67cb`; the final commit adds only the note, the POML and docs):
- **BFF unit suite** (`tests/unit/Sprk.Bff.Api.Tests`, which compiles `tests/integration/auth/**`): **15550 passed, 1 failed,
  54 skipped (15605)** — +15 over v1c-v1's 15590, exactly this round's new tests (8 + 2 + 1 + 4). The one failure,
  `StandaloneChatContextEndpointsTests.GetStandaloneContext_WithSprkDocument_Returns200_WithEmptyContextFields`, is contention:
  the request was aborted by the client after 2 m 47 s (`TaskCanceledException` / "The client aborted the request") while a
  publish and three other suites ran on the machine; code this round does not touch. Its isolated re-run (the whole class):
  **19/19 passed**.
- **ArchTests** (`tests/Spaarke.ArchTests`): **607/607** (+2: the new facts).
- **Sprk.Bff.Api.IntegrationTests**: **104/104**. **Spe.Integration.Tests**: **403 passed, 25 skipped, 0 failed (428)**.
- No client code changed this round (the external SPA and the PCF are untouched).

### 13.6 Publish size (CLAUDE.md §10 item 4) and CVE

Fresh trees exported from the commits (BFF + shared + config + root build files) to SHORT paths, `dotnet publish -c Release`,
zipped with PowerShell `Compress-Archive -CompressionLevel Optimal` over `deploy\api-publish\*` (incl. PDBs):

| Tree | Commit | Files (PDBs) | Bytes | MB |
|---|---|---|---|---|
| `C:\wt140v2m` — this round's base | `55d84c474` | 212 (4) | 48,282,235 | 46.05 |
| `C:\wt140v2b` — this round | `6f85d67cb` (the BFF source of the final commit) | 212 (4) | 48,283,101 | 46.05 |

This round: **+866 bytes (+0.00 MB)**; 212 = 212 files. With §12.5, the whole task: 46.00 → 46.05 MB (+0.05 MB). ≤ 60 MB. No
package added or changed. `dotnet list package --vulnerable --include-transitive` (BFF): **no vulnerable packages**. Both trees
removed after measuring.

### 13.7 Quality gates (Step 9.5, FULL rigor) — this round's diff

- **code-review** — no Critical. W1 (deploy order, extends §5): `RowSelect` and the job's scan now also name
  `sprk_grantedbycontactid` — the same gate G-140-1, whose `-Apply` creates it and `-Verify` checks it. W2: the pwsh-driven test
  needs PowerShell 7 on the PATH (present on the windows-latest CI runners); its absence fails, never skips. W3: the Web API
  take-over sends the lookup's unbind only when the lookup is still bound (a deleted issuer's is already empty) — a no-op unbind
  is avoided rather than relied on. W4: a System Administrator who clears only the lookup by hand makes R1 end that undated row —
  the fail-closed direction; the column pair is field-secured against everyone else.
- **adr-check** — compliant: ADR-002 (no plugin; the invariant lives in the BFF's writers and the job), 003 (fail closed: an
  unparseable provenance ends the row, the backfill refuses a versionless write and stops on any non-412 fault, R1 still never
  writes on a fault), 010 (no new registration), 036 (the job's shape unchanged; no throw from `ExecuteAsync`), 038 (no
  `Mock<HttpMessageHandler>`, no DI/ctor tests; one-input twins; the script test runs the real helper), 052 (placement unchanged),
  AP-13 (the column is created by the script under the sprk publisher, never by MCP).

### 13.8 Manual gates (unchanged in kind; G-140-1's content grows)

- **G-140-1** — the same two commands as §5 (`-Apply`, then `-Verify` exiting 0) now also create, secure and backfill
  `sprk_grantedbycontactid`; read both columns back as §5 says.
- **G-140-2** — unchanged (§6), after G-140-1 and this branch's BFF.

### 13.9 `.claude/` edits

None required by this round (§12.7's FAILURE-MODES recommendation still stands, unchanged).
