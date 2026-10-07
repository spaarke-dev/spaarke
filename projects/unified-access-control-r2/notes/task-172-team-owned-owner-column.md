# Task 172 — The membership resolver binds a team-owned Owner column (refs GitHub #1011)

> **Branch**: `task/uac-r2-172` (from `origin/master` @ `fdfd4cec5e`) · **Rigor**: FULL · **Date**: 2026-10-06
> **Line numbers** below are on this branch unless marked *(master)*.

## 1. The defect, in one paragraph

`MembershipFieldDiscoveryService` gives every `AttributeTypeCode.Owner` column the synthetic targets
`{ "systemuser", "team" }` (`MembershipFieldDiscoveryService.cs:108`, used at `:563`). The classifier used to keep only
the **first** target found in `identityTypeByTable` and `break` (*(master)* `:288-300`). So `ownerid` was always ONE
`SystemUser` descriptor, bound to the caller's own `systemuserid`. On a team-owned record `ownerid` holds the team's id,
so the Owner column never matched. The master FetchXml for an Owner-column-only shape, captured from the pre-fix run:

```xml
<filter type='or'>
  <condition attribute='ownerid' operator='eq' value='{caller systemuserid}' />
  <condition attribute='sprk_assignedattorney1' operator='eq' value='{caller contactid}' />
</filter>
```

No team id. A record owned by a team the caller belongs to is never asked for.

## 2. Step 0 trace

### (a) Every caller of the resolver and the discovery service, and the surface it uses

| # | Caller | Evidence | Service / surface | Use of the result |
|---|---|---|---|---|
| 1 | `GET /api/users/me/memberships/{entityType}` | `Api/Membership/MembershipEndpoints.cs:99,109` → options `:256-261` → `ResolveAsync` `:269` | Resolver · **default** (caller-supplied `roles` / `identityTypes` / `includeRelated`) | Returns ids to the client — a scope |
| 2 | `LookupUserMembership` playbook node | `Services/Ai/Nodes/LookupUserMembershipNodeExecutor.cs:285-291` (options), `:318` | Resolver · **default**, or **People** when `targeting: "people"` (`:284`); `roles` from node config | Ids feed later nodes, e.g. an app-only `QueryDataverse` (`QueryDataverseNodeExecutor.cs:16-23`) — a scope used for an app-only read |
| 3 | Workforce plane of the external SPA (`/api/v1/external/**`) | `ExternalAccessEndpoints.cs:57` (`AddCallerPrincipalAuthorizationFilter`) → `CallerPrincipalResolver.cs:648-652` (`ComposeAsync` for project / matter / work assignment) → `AccessibleRecordSetService.cs:1227` → `ComposeForSystemUserAsync` `:1551`, resolver call `:1577-1582` | Resolver · **AccessConferringOnly** | An access **decision** (`IsRecordAccessibleAsync` / `IsOperationPermittedAsync`, `:1256-1300`) |
| 4 | Contact plane (workforce contact-only + CIAM contact) | `AccessibleRecordSetService.cs:1880`, `:1960` | Resolver · **ResolveByContactAsync** (`includePlatformOwnership: false`, `MembershipResolverService.cs:533`) | Access decision; ownership columns never admitted |
| 5 | Daily Briefing | `Services/Ai/Narrators/DailyBriefingCollector.cs:626-627` → `PeopleTargetedSet.cs:38-39,58,68` | Resolver · **People** | Attention set; rows read under caller security |
| 6 | Workspace portfolio / briefing | `Services/Workspace/PortfolioService.cs:386-387` → `PeopleTargetedSet` | Resolver · **People** | Same |
| 7 | Admin discovery report / refresh | `Api/Admin/MembershipAdminEndpoints.cs:58,120` (`DiscoverAsync`), `:77,168` (`InvalidateCacheAsync`) | Discovery only | Operator report — now lists `ownerid` twice (SystemUser, Team) |
| 8 | Membership reconciliation job | `Services/Ai/Membership/MembershipReconciliationJob.cs:350-360` | Discovery only | Junction rows; **de-duplicated per field here** (see §3) |
| 9 | Communication thread membership | `Services/Communication/Membership/ThreadMembershipDerivationService.cs:177,184` | Discovery only | Already `Distinct()`s fields; types each value by its own `EntityReference.LogicalName` (`:191-216`) — unaffected |

No other `src/` type depends on `IMembershipResolverService`, `IMembershipFieldDiscoveryService` or
`IAccessibleRecordSetService.ComposeAsync` (grep of all three plus `PeopleTargetedSet`). The only `ComposeAsync`
call sites are `CallerPrincipalResolver.cs:648-652`. The other `IAccessibleRecordSetService` consumers call only
`CheckGranteeNoAccessAsync` (`GrantExternalAccessEndpoint.cs:719`, `AssignedAccessMaterializer.cs:994`), which does not
use the resolver.

### (b) #1011's open question: does A1.1 pull a NON-registry entity's Owner column into the access-conferring surface?

**Yes, in code.** `FilterToAccessConferringRoles` (`MembershipResolverService.cs:686-801`) does not return early
for an entity with no registry entry. `columns ??= new List<AccessConferringColumn>()` (`:702`) leaves the registry
empty, and the platform-ownership check runs **before** the registry lookup (`:760`,
`includePlatformOwnership && PlatformOwnershipColumns.Contains(d.Field.Trim())`). So `ownerid` / `owningteam` /
`owningbusinessunit` confer on **every** entity, registered or not. Test
`AccessConferringOnly_TeamOwnedRow_CallerInTheOwningTeam_ResolvesUnderOwner(entity: "sprk_document")` proves it.
`sprk_document` has no registry entry (`MembershipOptions.cs`, `CanonicalAccessConferringRegistry`).

**In practice the blast radius is set by who calls that surface.** The only `AccessConferringOnly` caller is row 3,
for `sprk_project` / `sprk_matter` / `sprk_workassignment` only (`CallerPrincipalResolver.cs:648-652`). Nothing asks
the access-conferring surface about `sprk_document`, `sprk_communication` or `sprk_externalrecordaccess` today.

**"Resolves to nobody" was true of the Owner column, not of the record, wherever `owningteam` is discovered.** On a
team-owned row Dataverse maintains `owningteam` = `ownerid`. `owningteam` is a plain `team` lookup and is not in
`GlobalFieldExclusions` (§c), so on both the default and the access-conferring surface the team-owned row was already
returned through `owningteam` → `Team` → `identity.TeamIds`. Task 043 §15.3 says so
(`notes/task-043-org-expansion-term.md:610-619`). What #1011 actually broke:

1. **Any caller that narrows by role or identity type.** Examples: `roles=ownerid` on the memberships endpoint, a
   playbook node with `roles: ["ownerid"]`, or `identityTypes=Team`. `owningteam` is a different role
   (`owningteam`), so the Owner column was the only term left, and it bound the caller's own id.
2. **Any entity where `owningteam` is not discovered.** An `EntityOverrides.ExcludedFields` entry would do it. The
   seeded configuration has none.
3. **The Owner column's own role** (`ownerid`). It carried team-owned rows only by accident. `MaterializeResults`
   credits a row to every role whose column is populated, without comparing the value (`:1290-1298`, `RowMatchesDescriptor` `:1325`) — see §6 D5.

### (c) Is `owningteam` discovered today for `sprk_document`, `sprk_todo`, `sprk_communication`, `sprk_matter`?

**Yes for all four, by construction. It was not verified live (no live actions in this task).**

- **Classifier rule.** A lookup is discovered when a target is in `IncludedIdentityTables` and the field is not
  excluded (`MembershipFieldDiscoveryService.cs:268-362`). `team` → `Team` is seeded
  (`MembershipOptions.cs`, `CanonicalIdentityTables`; same in `appsettings.Development.json.template:20-27`).
  `GlobalFieldExclusions` holds only `createdby`, `modifiedby`, `createdonbehalfby`, `modifiedonbehalfby`
  (`MembershipOptions.cs` `CanonicalAuditFieldExclusions`; template `:28-33`). The only `EntityOverrides` entry is
  `sprk_matter`, with empty `ExcludedFields` (template `:36-45`). Neither `appsettings.template.json` nor
  `appsettings.Production.json.template` binds a `Membership` section, so the seeded defaults apply.
- **`owningteam` exists on each table as a `Lookup → team`:**
  - **`sprk_matter`**: `docs/data-model/sprk_matter-related-tables.md:98`. The ADR-034 AC-1A.1 discovered list names
    `owningteam` (`docs/adr/ADR-034-user-record-membership.md:215`).
  - **`sprk_communication`**: `docs/data-model/sprk_communication.md:60` ("Targets: team").
  - **`sprk_document`**: queried on `owningteam` by `SecureChildReconciliationJob.cs:658,673`. It is team-owned on the
    Office save path (`OfficeService.PublishSavedDocumentOwnerAsync`, task 152 note item 7).
  - **`sprk_todo`**: user/team-owned. Task 152 resolves team-owned to-dos on `ownerid` and `owningteam`
    (`MembershipResolverPeopleTargetingTests` `TodoShapes`), and word-add-in-r1 D-11 team-owns it. Every user/team-owned
    Dataverse table carries `ownerid` (Owner), `owninguser`, `owningteam` and `owningbusinessunit`.
- **Check on dev, later:** `GET /api/admin/membership/discovered/{entity}` for the four tables should list `owningteam`
  (`Team`) and, after this change, `ownerid` twice.

### (d) Office routes (Word / Outlook add-in) that reach the resolver

The set of routes the add-ins call was taken from `src/client/office-addins/**` (every `/api/...` literal):
`/api/office/*`, `/api/office/communications/*`, `/api/upload`, `/api/v1/documents/*`, `/api/documents/resolve-identity`,
`/api/ai/search/records`, `/api/ai/visualization/related/*`, `/api/ai/rag/send-to-index`, `/api/communications/send`.
Every handler and filter on those routes was checked for the resolver closure (`IMembershipResolverService`,
`IMembershipFieldDiscoveryService`, `IAccessibleRecordSetService.ComposeAsync`, `PeopleTargetedSet`). **None uses it.**
Office uses `IIdentityNormalizationService` (`OfficeService.cs:92,2105`, `RecordCreationService.cs:302`) and the
membership **event** publisher. Neither is the resolver.

| Office route | Authorization path | Reaches resolver? | Can its access result change? |
|---|---|---|---|
| `POST /api/office/save` | `OfficeAuthFilter`, `EntityAccessFilter` (`CallerRecordAccessProbe`, OBO `RetrievePrincipalAccess`), `OfficeVersionSaveAuthorizationFilter` (`AuthorizationService`) — `OfficeEndpoints.cs:185-194` | No | **No** |
| `GET /api/office/jobs/{id}`, `/stream` | `OfficeAuthFilter`, `JobOwnershipFilter` (`IOfficeService`) — `:729-753` | No | **No** |
| `GET /api/office/search/entities`, `/search/{list}` | `OfficeAuthFilter` — `:1051-1078` | No | **No** |
| `POST /api/office/quickcreate/{entityType}`, `GET /quickcreate/defaults` | `OfficeAuthFilter`, `QuickCreateSourceAccessFilter` (`CallerRecordAccessProbe`) — `:1368-1396` | No | **No** |
| `POST /api/office/todo` | `OfficeAuthFilter`, `TodoSourceAccessFilter` (`CallerRecordAccessProbe`) — `:1411-1418` | No | **No** |
| `POST /api/office/documents/{id}/generate-profile` | `OfficeAuthFilter`, `DocumentAuthorizationFilter("write")` (`AuthorizationService`) — `:1578-1603` | No (filters). The handler runs the Document Profile pipeline | **No.** Access is decided by the filters. The profile playbook's content lives in Dataverse and was not read (no live actions). A `LookupUserMembership` node in it would see changed ids only if it narrows by role or identity type (§a row 2), and needs a run user (`LookupUserMembershipNodeExecutor.cs:241-252`) |
| `GET /api/office/communications/by-message-id/*`, `/{commId}/linked-todos` | `RequireAuthorization` + handler checks — `CommunicationsEndpoints.cs:82-128` | No | **No** |
| `/api/v1/documents/*`, `/api/upload`, `/api/documents/resolve-identity` | `ContainerDocumentAuthorizationFilter` / `DocumentAuthorizationFilter` / `DocumentUrlIdentityFilter` — no `ComposeAsync` (only `CallerPrincipalResolver` composes) | No | **No** |
| `/api/ai/search/records`, `/api/ai/visualization/related/*`, `/api/ai/rag/send-to-index` | `SemanticSearchAuthorizationFilter.TryResolveAuthorizableEntitySet` (static map) / AI authorization filters — no `ComposeAsync` | No | **No** |
| `POST /api/communications/send` | Communication filters — no resolver | No | **No** |

**No Office route's access result changes.** Escalation trigger 3 does not fire.

### Escalation trigger 2 (the resolver used as an access GRANT) — evaluated, does not fire

- **Row 3 is a grant** (it decides access). On the three tables it composes, the Team descriptor of `ownerid`
  selects exactly the rows `owningteam` already selects, because both columns hold the owning team. **The composed
  id set and rights are unchanged.** Only the `ownerid` role bucket gains team-owned rows. `WalkMembershipPagesAsync`
  reads only `response.Ids` (`AccessibleRecordSetService.cs:1470`), so role buckets do not reach the decision.
- **Row 2 is a scope used for an app-only read.** A node that narrows to `roles: ["ownerid"]` now receives team-owned
  rows of the caller's teams. Those are the same rows the same node already receives unfiltered, through
  `owningteam`. ADR-034 A3 already documents that the default surface fans team ownership out to the team
  (`docs/adr/ADR-034-user-record-membership.md:366`), and `LookupUserMembershipNodeExecutor.cs:281-283` tells
  notification playbooks to use `targeting: "people"`. Nothing becomes reachable that was not already reachable.
- **Default-surface consumers can see a larger result when they filter by role or identity type** (rows 1 and 2).
  This is the intended fix (#1011).

## 3. The change

| File | Change |
|---|---|
| `Services/Ai/Membership/MembershipFieldDiscoveryService.cs` | Classifier collects **every** target that maps to a configured identity type, in target order, de-duplicated by identity type (`:296-330`), and emits one descriptor per match (`:354-362`): same field, role and source. Stable `OrderBy(Field)` replaces the unstable `List.Sort` (`:365-372`), so `ownerid` is always SystemUser then Team (FR-14). `CacheVersion` 1 → 2 (`:76-87`, AC4). |
| `Services/Ai/Membership/IMembershipFieldDiscoveryService.cs` | `DiscoveredFields` doc: a field is no longer unique. Consumers keyed by field must de-duplicate or select by type. |
| `Services/Ai/Membership/MembershipResolverService.cs` | **Registry type check** (`:777-787`): for a polymorphic column, a descriptor whose type differs from the registered type is dropped quietly when a sibling descriptor of the same field carries the registered type. Only the registered type confers, and there is no false "stale entry" warning. `CacheVersion` 5 → 6 (`:93-98,122`, AC4 — query semantics changed under an unchanged options hash). Stale remarks corrected (`PlatformOwnershipColumns`, `PersonOwnershipColumns`). **No change** to `BuildFetchXml`: the existing `Team` branch (`:1148-1152`) emits `ownerid eq {teamId}` for each team; `projectAttributes` (`:1086`) projects the column once; `byRoleAccum` (`:1276`) merges both descriptors into one role. **No change** to People: `PersonOwnershipColumns` admits only `IdentityType == "SystemUser"` (`:894-899`), so the Team descriptor selects nothing. **No change** to the C-1 allowance: it stays keyed on the three column NAMES (`:760`). |
| `Services/Ai/Membership/MembershipReconciliationJob.cs` | Keeps one descriptor per field (`:350-360`). Without this, `ToDictionary(d => d.Field)` throws on the duplicate key, and every Owner value would be dispatched twice. The job's output is byte-identical to before: values are typed from their own `EntityReference.LogicalName`. |

Not changed (§11): no new service, interface or DI registration. `IdentityNormalizationService` is untouched.

**Fail-closed note (ADR-003).** The `teammembership` read fails soft to an empty `TeamIds`, marks the identity faulted,
and is not cached (`IdentityNormalizationService.cs:394-446`, ISS-019 shape). On a team-owned row, a failed team read
therefore looks like **no access**: the Team leg binds nothing and only `ownerid eq {caller}` is emitted. This is the
safe direction. Pinned by `Default_CallerWithNoTeams_OwnerColumnBindsOnlyTheCaller`.

## 4. Results — which surfaces change

| Surface | Before | After |
|---|---|---|
| Default, unfiltered, table with `owningteam` discovered | Team-owned row returned via `owningteam` (and credited to `ownerid` by the value-blind role attribution) | Same id set. `ownerid` now selects it by value |
| Default, `roles=ownerid` / `identityTypes=Team` / Owner column only | Team-owned row **missing** | Returned under `ownerid` (**the fix**) |
| AccessConferringOnly (workforce plane, `/api/v1/external/**`) | Id set and rights via `owningteam` | **Unchanged** id set and rights |
| People (Daily Briefing, Workspace, `targeting: "people"`) | User-owned only | **Byte-identical**: one `ownerid eq {caller}` condition, no team id |
| Contact plane (`ResolveByContactAsync`) | No ownership terms | **Unchanged** |
| Admin discovery report | `ownerid` × 1 | `ownerid` × 2 (SystemUser, Team); Customer columns × 2 when `account` and `contact` are both configured |
| Reconciliation job | One event per owner value | **Unchanged** (and no longer throws) |

| Office route | Access result can change? |
|---|---|
| All `/api/office/**` and the other add-in routes in §2(d) | **No** |

## 5. Tests

New file `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Membership/MembershipResolverTeamOwnedOwnerTests.cs`. It runs
**real** discovery, including the Owner synthesis through `ProjectLookupAttributeRows`, into the **real** resolver.
The Dataverse fake evaluates the emitted `eq` conditions, so a row comes back only when the query names its value.
One new test was added to `MembershipReconciliationJobTests.cs`.

| POML test | Test | Pre-fix (master code + these tests) |
|---|---|---|
| (a) | `Default_TeamOwnedRow_CallerInTheOwningTeam_ResolvesUnderOwner` | **FAILED** — "Expected result.Ids to be equal to {aaaaaaaa-…-0001}, but found empty collection" |
| (a) | `Default_OwnerRoleFilter_FullPlatformShape_TeamOwnedRowResolvesUnderOwner` | **FAILED** — only the user-owned row |
| (b) | `AccessConferringOnly_TeamOwnedRow_…(sprk_matter)` and `(sprk_document)` | **FAILED** ×2 — empty |
| (c) | `UserOwnedRow_StillResolvesUnderOwner_OnDefaultAndAccessConferring` ×2 | passed (unchanged behaviour) |
| (d) | `People_TeamOwnedRowTheCallersTeamOwns_IsNotReturned_UserOwnedRowUnchanged` | passed (unchanged behaviour) |
| (e) | `Default_CallerInNoOwningTeam_TeamOwnedRowIsNotReturned` | **FAILED** — no team-leg condition at all (master FetchXml quoted in §1) |
| (e) | `Default_CallerWithNoTeams_OwnerColumnBindsOnlyTheCaller` | passed (unchanged behaviour) |
| (f) | `Discovery_SingleTargetMakerLookups_ClassifyExactlyAsBefore` | passed (unchanged behaviour) |
| (f) | `Discovery_OwnerColumn_YieldsSystemUserThenTeam_SameFieldRoleAndSource` | **FAILED** — 1 descriptor |
| (g) | `Discovery_CustomerColumn_OnlyContactConfigured_ClassifiesUnchanged` | passed (unchanged behaviour) |
| (g) | `Discovery_CustomerColumn_AccountAndContactConfigured_YieldsBothInTargetOrder` | **FAILED** — Account only |
| FR-14 | `EmittedFetchXml_IsDeterministic_SystemUserLegBeforeTeamLegs` | **FAILED** — no team leg |
| AC4 | `DiscoveryCache_PreChangeSingleDescriptorEntry_IsNotServed` | **FAILED** — the v1 entry was served (`FetchCount` 0) |
| AC4 | `ResolverCache_PreChangeEntryUnderTheSameId_IsNotServed` | **FAILED** — version 5 |
| job | `ExecuteAsync_PolymorphicOwnerDiscoveredAsUserAndTeam_DispatchesOncePerValue` | **FAILED** — `result.Success` false (duplicate-key throw) |

Pre-fix run: **11 failed, 6 passed, 17 total** (worktree `C:/wt172x` = `origin/master` + the two test files only).
Post-fix: **17/17 pass**.

Added after the pre-fix run, so it has no pre-fix result:
`AccessConferringOnly_PolymorphicRegistryColumn_OnlyTheRegisteredTypeConfers_NoStaleEntryWarning`. It covers the
registry type-check change: a Customer column registered as Contact confers through Contact only, the Account id is
never bound, and no "stale entry" warning is logged. On master this column would have discovered as Account only,
been logged as a stale entry, and conferred nothing.

**Pre-existing tests changed: one.** None is an A3 or C-1 test.
- `tests/integration/auth/UnifiedAccessControl/AccessCacheFaultCachingTests.cs`
  `Membership_AnEntryCachedUnderThePreFixVersion_IsNotServed` (task 132, C12) asserted
  `MembershipResolverService.CacheVersion == 5`. This task bumps it to 6 (§3), so the assertion is now `>= 5`. The
  test's purpose is unchanged: its v4 seed is still unreachable, and the control still serves the current version.
  The suite run found this as the only failure: 18111 passed, 1 failed (this test), 54 skipped.

**Unmodified:** every `MembershipResolverPeopleTargetingTests` test (A3), and
`ResolveAsync_AccessConferringOnly_MakerAuthoredSystemUserLookup_ConfersNothing` (C-1). Existing resolver tests mock
discovery, so the classifier change does not reach them. Existing discovery tests use single-target `ownerid` rows.

## 6. Defects and behaviour notes found during the trace

Every defect found is listed here, whatever its origin (owner rule, 2026-10-06).

| # | Defect | Where | Failure scenario | Status |
|---|---|---|---|---|
| D1 | Team-owned Owner column binds nobody (#1011) | `MembershipFieldDiscoveryService.cs` classifier *(master :282-300)* | `roles=ownerid` (or Owner column only) misses every team-owned record | **In scope (fixed)** |
| D2 | Reconciliation job keyed descriptors by field with `ToDictionary` | `MembershipReconciliationJob.cs:398` | With D1 fixed, the job would throw `ArgumentException` (duplicate key `ownerid`) for every entity and dispatch each owner value twice | **In scope (fixed)** — `:350-360` |
| D3 | Discovery sorted with the unstable `List.Sort` | `MembershipFieldDiscoveryService.cs` *(master :342)* | Once a field has two descriptors, their order (and so the FetchXml condition order) could vary run to run (FR-14) | **In scope (fixed)** — stable `OrderBy` |
| D4 | Registry type check warned on a polymorphic column's other type | `MembershipResolverService.cs:777-787` | A registered Customer/polymorphic column would log a false "stale registry entry" warning on every resolve | **In scope (fixed)** |
| D5 | Role attribution is value-blind | `MembershipResolverService.cs:1290-1298`, `RowMatchesDescriptor` `:1325-1338` | A row matched only through `sprk_assignedattorney1` (the caller is the assigned attorney; someone else owns it) is ALSO listed under `byRole["ownerid"]`, because `ownerid` is always populated. Wrong `byRole` output from `GET /api/users/me/memberships/{entity}` and the `LookupUserMembership` node's `byRole`. Access decisions read `Ids` only, so no grant is affected | **Out of scope (needs filing).** Pre-existing; the fix changes `byRole` for every role and consumer and needs its own tests |
| D6 | Role names in client configs and docs do not match what discovery derives | Discovery derives `ownerid` / `assignedattorney` (`DeriveRoleNameCamelCase`; lowercase logical names). Consumers say `owner` / `assignedAttorney`: `src/client/shared/Spaarke.Communication.Components/src/components/ReconciliationGrid/per-team.gridconfiguration.json:38` (`roles: ["owner"], identityTypes: ["team"]`), `src/client/shared/Spaarke.UI.Components/src/services/membership.ts:30,70`, `projects/spaarke-daily-update-service/notes/playbooks/actions/sys-lookup-membership.action.json:100`, `docs/guides/MEMBERSHIP-RESOLUTION-GUIDE.md:47,114,190`, `docs/guides/PLAYBOOK-AUTHOR-GUIDE.md:568` | Any `roles` filter using those names matches no descriptor, so the result is empty. The per-team reconciliation grid (team-owned communications, the D-11 case) would show nothing once its membership filter is wired. With this task's fix, `roles: ["ownerid"], identityTypes: ["team"]` would return exactly the team-owned rows | **Out of scope (needs filing).** Client config and docs owned by other projects (communication / workspace); task 152 noted the mismatch first |
| D7 | Client doc suggests identity TABLE names for `identityTypes` | `src/client/shared/Spaarke.UI.Components/src/services/membership.ts:32` (`['systemuser', 'contact']`) | The BFF compares against identity-type LABELS, ignoring case (`FilterDescriptors`). `systemuser` / `contact` / `team` happen to match, but `sprk_organization` would not match `Organization`, so an org filter returns nothing | **Out of scope (needs filing)** |

Behaviour notes (intended, not defects):
- **N1 — Customer columns now bind both types on the default surface.** Account and Contact, when both are configured
  (the seeded default), per the POML's general rule. A Customer column holding the caller's linked contact now
  selects the row on the default surface. The registry surface admits only a registered type; no registry entry
  names a Customer column today.
- **N2 — Configuration that excludes `owningteam` but not `ownerid`.** If an operator added `owningteam` to
  `EntityOverrides.ExcludedFields` and left `ownerid`, team-owned rows would come back through `ownerid`'s Team
  descriptor. No such configuration exists in the repo templates or seeds. Excluding team ownership would require
  excluding both columns.
- **N3 — Fail-closed team read** (§3): a failed `teammembership` read binds no team ids, so the result is "no access".
- **Live check (owner, after deploy).** Use a **child-BU** user (Test User 1, `Spaarke Business Unit 1`) per #1011's
  note: `GET /api/users/me/memberships/sprk_matter?roles=ownerid&identityTypes=Team` should list matters owned by
  that BU's default team. A root-BU account cannot tell a working fix from a no-op.

## 7. Build, publish size, CVE

See the PR description for the measured numbers (both absolute sizes, the zip tool, file counts on both sides) and
the CVE check output.
