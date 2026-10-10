# Unified Access Control (UAC) Architecture

> **Domain**: Authorization, Access Control, Permission Management
> **Status**: Verified (Production-Ready Internal; Design External)
> **Last Updated**: 2026-09-10 — corrected stale app-only/`userAccessToken: null` claims: `AuthorizationService` now fails closed and runs AS THE CALLER (task 004 / FR-02, finding A-2, code changed 2026-08-21, one day after this doc's prior correction pass)
> **Last Reviewed**: 2026-09-10
> **Reviewed By**: unified-access-control-r2 (drift correction); previously ai-procedure-refactoring-r2 (2026-04-05)
> **Source ADRs**: ADR-003, ADR-008, ADR-009, ADR-028 (Amendment A1 — broker-only external access)

> **Verification note (2026-04-05)**: All 6 referenced classes confirmed present in code — `AuthorizationService` (Spaarke.Core/Auth), `OperationAccessPolicy` (Spaarke.Core/Auth), `CachedAccessDataSource` (Sprk.Bff.Api/Infrastructure/Caching), `DocumentAuthorizationFilter`, `AiAuthorizationFilter` (Sprk.Bff.Api/Api/Filters), `DataverseAccessDataSource` (Spaarke.Dataverse).

> **Correction note (2026-08-20, `unified-access-control-r2`)**: This doc previously described several DESIGN-era behaviors that were never built (three-plane grant orchestration, Power Pages external access, `RetrievePrincipalAccess` app-only mode, SPE container roles per access level). Corrected against code; unbuilt behavior is now marked NOT IMPLEMENTED. External access is broker-only per ADR-028 A1: a grant writes one `sprk_externalrecordaccess` row and invalidates one Redis cache entry (`Api/ExternalAccess/GrantExternalAccessEndpoint.cs:98-117`).

---

## Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| Three-plane access model | Dataverse records (native security), SPE files, AI Search — each plane requires independent access management. A grant writes Plane 1 data only (one `sprk_externalrecordaccess` row); Plane 2 is broker-only (BFF app-only Graph — external users are never added to containers); Plane 3 external filtering is NOT IMPLEMENTED. See "Three-Plane Access Model" below |
| Fail-closed design | Any error, unknown operation, missing access data, or no rule decision → deny. Security boundary must never fail open |
| `OperationAccessPolicy` maps Graph ops to `AccessRights` | 66 operations mapped (56 canonical + 10 legacy aliases; `Spaarke.Core/Auth/OperationAccessPolicy.cs`); download requires `Write` (not just Read) for security compliance |
| Direct-query `DataverseAccessDataSource` | BOTH auth modes (app-only service principal AND OBO) use the same direct query (`GET sprk_documents({id})?$select=sprk_documentid`, `Spaarke.Dataverse/DataverseAccessDataSource.cs:323`) and grant at most `Read` (`:368-372`). `RetrievePrincipalAccess` has ZERO call sites in this path — it appears only in comments |
| `CachedAccessDataSource` decorator (ADR-009) | Cache permission **data**, not decisions; fail-open on Redis errors (falls through to Dataverse) |
| Endpoint filters, not global middleware (ADR-008) | 23 domain-specific filters apply authorization at endpoint level |
| Single `OperationAccessRule` | Dataverse's own row-level security (roles, teams, business units, record sharing) is enforced by the direct-query probe — the record is only retrievable if the probe identity can read it — so one rule is sufficient. NOTE (corrected 2026-09-10): `AuthorizationService` FAILS CLOSED (denies) when no caller token is present and otherwise forwards the caller's own bearer token — the probe runs AS THE CALLER, never as the service principal (`Spaarke.Core/Auth/AuthorizationService.cs:45-54` fail-closed check, `:74` "Evaluate AS THE CALLER", `:223-225` forwards the caller's token; task 004 / FR-02, finding A-2, "ZERO app-only consumers" verified 2026-08-21) |

---

## Three-Plane Access Model

| Plane | What It Controls | Internal Mechanism | External Mechanism |
|-------|-----------------|-------------------|-------------------|
| **Plane 1: Dataverse Records** | CRUD access to Dataverse rows | Security roles, teams, BU, record sharing | `sprk_externalrecordaccess` grant rows, read by the BFF (`Infrastructure/ExternalAccess/ExternalParticipationService.cs`). External callers are Static Web Apps + Entra External ID (CIAM) — Power Pages is retired |
| **Plane 2: SPE Files** | Read/write/delete files in SharePoint Embedded containers | BFF `AuthorizationService` → `OperationAccessPolicy` | Broker-only (ADR-028 A1): external users never authenticate to SPE; all external file access is app-only via the BFF. Contacts are NOT added to containers |
| **Plane 3: AI Search** | Query results from Azure AI Search | BFF constructs filter from user's accessible entities | NOT IMPLEMENTED — no CIAM/external route reaches AI Search |

**What a grant actually does** (corrected 2026-08-20 — the old "three-plane orchestration" was never built): granting external access = (1) create ONE `sprk_externalrecordaccess` record + (2) invalidate the contact's Redis participation cache (`Api/ExternalAccess/GrantExternalAccessEndpoint.cs:13-23, 98-117`). No web role is assigned (Power Pages retired), and no SPE container membership is written — `SpeContainerMembershipService.GrantMembershipAsync` exists but has NO production callers. Revoking = deactivate the record + invalidate the cache, plus a defensive removal of any stray SPE container permission when a `ContainerId` is supplied (`Api/ExternalAccess/RevokeExternalAccessEndpoint.cs:93-147`).

---

## Dual-Mode DataverseAccessDataSource

Two auth modes exist on `DataverseAccessDataSource.GetUserAccessAsync` (its `userAccessToken` parameter still defaults to `null`), but BOTH run the SAME direct-query check (`Spaarke.Dataverse/DataverseAccessDataSource.cs:323`) — `RetrievePrincipalAccess` is NOT called in either mode (zero call sites; corrected 2026-08-20):

| Auth Mode | When Used | Method |
|-----------|-----------|--------|
| **App-only** | NOT reachable via `AuthorizationService` (corrected 2026-09-10): `AuthorizationService.GetCallerAccessAsync` FAILS CLOSED (returns `AccessRights.None`, data source not consulted) when no caller token is present, rather than degrading to app-only (`Spaarke.Core/Auth/AuthorizationService.cs:45-54`, `:207-221`; task 004 / FR-02, finding A-2, "ZERO app-only consumers" verified 2026-08-21) | Direct query: `GET sprk_documents({id})?$select=sprk_documentid` with the service principal token, if reached by some other caller of the data source |
| **OBO** | `AuthorizationService.AuthorizeAsync` / `GetCallerAccessAsync` (mandatory caller token — `:74` "Evaluate AS THE CALLER", `:223-225` forwards it) and `AiAuthorizationService` | Same direct query with the OBO-exchanged user token |

Direct query pattern: query the document directly → 200 = grant `AccessRights.Read` (at most — Write/Delete etc. are never granted by this probe, `:368-372`); 403/404 = access denied (empty permission set). Via `AuthorizationService`, the probe always runs AS THE CALLER (OBO) — an absent caller token denies rather than degrading to app-only (corrected 2026-09-10; see the Key Design Decisions table above). See [sdap-auth-patterns.md Pattern 5](sdap-auth-patterns.md) for the OBO bugs that were fixed.

---

## Redis Caching TTLs (ADR-009)

Actual keys per `Infrastructure/Caching/CachedAccessDataSource.cs` — since unified-access-control-r2 task 132 (defect C12, ADR-009 path C) through `ITenantCache` under the caller's `tid` (the on-wire key carries the configured `InstanceName` in front):

| Data | Cache Key Pattern | TTL |
|------|-------------------|-----|
| Document access | `tenant:{tid}:auth-access:{authMode}:{userOid}:{documentId}:v2` | 60 sec |
| Record access | `tenant:{tid}:auth-record-access:{entitySet}:{userOid}:{recordId}:v2` | 60 sec |

The user-level role and team keys that used to be written here (2 min) were read by nothing and are deleted. A request with no `tid` is not cached at all.

**A fault is never cached (task 132).** `DataverseAccessDataSource` marks a snapshot `AccessSnapshot.Faulted` when it is not a complete Dataverse answer — an OBO failure, a user lookup that could not be completed, a RetrievePrincipalAccess or probe status other than an answer, a timeout, a failed team / role sub-read, or the DEGRADED probe-derived Read after RetrievePrincipalAccess gave no answer. The request receives exactly the rights it always did; the snapshot is not stored. An answer — RPA with no rights, a probe 403 / 404, a lookup that found no systemuser — is cached. Re-owns evict every user's snapshot of the record (`IMembershipCacheInvalidator.InvalidateRecordOwnerChangeAsync`), and so does every POA share write — `DataverseWebApiService` itself notifies its `IRecordShareWriteObserver` (the BFF's `IMembershipCacheInvalidator`, which runs `InvalidateRecordShareChangeAsync`) after each grant / rights change / revoke it makes, returned, thrown or cancelled, whoever called it (task 132; main-session round 55 moved it from the POA seam into the write; it also evicts every user's impersonated root set for a root type). A document's id is normalised in its key (v2) so the eviction reaches it however the request spelled it. A table an Assign cascade re-owns (`sharepointdocumentlocation`, `sharepointdocument`) is never cached.

Fail-open on Redis errors: falls through to Dataverse. Cache stores permission **data**, not decisions (allows rule changes without cache invalidation).

The membership-side caches (identity, membership resolution, impersonated root sets) are 2 minutes since task 132 (were 10 / 5 / 5) and are evicted by the BFF's own team, business-unit, owner and (root sets) share writes; the residual staleness table is in [`caching-architecture.md`](caching-architecture.md#access-cache-residual-staleness-unified-access-control-r2-task-132--defect-c12).

The EXTERNAL participation cache is separate and DOES use `ITenantCache`: tenant-scoped, resource `external-access-grant`, contact-id component, version 5 (`ExternalParticipationService.CacheVersion` in `Infrastructure/ExternalAccess/ExternalParticipationService.cs`, whose comment carries the version history), 60s TTL — invalidated by the grant/revoke/closure/expiry endpoints, which all reference that one constant. A set built over a failed read (the grant query, the organization-grant read, or the membership junction) is returned to its request and never cached (task 132). Each cached grant carries BOTH the effective level and the direct level (`DirectAccessLevel`, read by Secure suppression); v5 (unified-access-control-r2 task 131) added the direct level after its absence made a direct grant on a secure root resolve to no rights on every cache hit.

---

## Endpoint Filters (ADR-008)

23 domain-specific filters in `Api/Filters/` (plus `AccessibleRecordSetAuthorizationFilter` and `CallerPrincipalAuthorizationFilter` wiring for the external surface in `Api/ExternalAccess/`), each applied per-endpoint:

| Filter | Domain |
|--------|--------|
| `DocumentAuthorizationFilter` | General document access |
| `AiAuthorizationFilter` | AI analysis access |
| `AnalysisAuthorizationFilter` | Document analysis |
| `CommunicationAuthorizationFilter` | Email operations |
| `FinanceAuthorizationFilter` | Finance module |
| + 18 more | Various domains |

NOT all filters call `AuthorizeAsync` (corrected 2026-08-20). The `oid` → `AuthorizationContext`/`OperationAccessPolicy` → `AuthorizationService.AuthorizeAsync()` → 403-with-deny-code pattern is followed by 4 filters (`DocumentAuthorizationFilter:79`, `EntityAccessFilter:154`, `FinanceAuthorizationFilter` — for its document checks; since unified-access-control-r2 task 130 its matter/project/vendor-organization checks use the entity-generic `AuthorizationService.GetCallerRecordAccessAsync` + `OperationAccessPolicy.HasRequiredRights`, invoice confirm also asks the caller's Create privilege on `sprk_invoice` through `CallerRecordAccessProbe` (OBO), and each finance/scorecard route declares exactly which id it authorizes, `OfficeDocumentAccessFilter:132`); 3 more route through `IAiAuthorizationService.AuthorizeAsync` instead (`AiAuthorizationFilter:83`, `AnalysisAuthorizationFilter:140`, `VisualizationAuthorizationFilter:106`). The remaining filters apply domain-specific checks (job ownership, webhook signatures, rate limits, tenant scoping, caller-principal resolution, record∈accessible-set, etc.) without going through `AuthorizationService`.

---

## Fail-Closed Scenarios

| Scenario | Result |
|----------|--------|
| Dataverse query fails | **Deny** |
| No rule makes a decision | **Deny** |
| User has `AccessRights.None` | **Deny** |
| Unknown operation string | **Deny** |
| Any exception | **Deny** |
| External caller whose token cannot be resolved to a Contact | **Deny** (403 `sdap.access.deny.contact_not_found`, `Infrastructure/ExternalAccess/CallerPrincipalResolver.cs:312-313`) |
| External caller requesting a record outside their grant set | **Deny** (403 `sdap.access.deny.record_not_in_accessible_set`, `Api/ExternalAccess/AccessibleRecordSetAuthorizationFilter.cs:137-146`) |
| External caller with zero active grants, on list endpoints | **200 with empty results** — NOT a 403 (corrected 2026-08-20). A resolved Contact with no grants gets an empty grant set (`CallerPrincipalResolver.cs:319-341`); e.g. `/api/v1/external/me` returns an empty project list |

Deny codes follow pattern `{domain}.{area}.{action}.{reason}` (e.g., `sdap.access.deny.insufficient_rights`, `sdap.access.deny.unknown_operation`).

---

## External Caller Access Levels

Actual level → effective-rights mapping per `Infrastructure/ExternalAccess/CallerPrincipalResolver.cs:126-134` (corrected 2026-08-20). The former "SPE Container Role" column reflected `SpeContainerMembershipService`'s role map, which is NOT wired — external SPE access is broker-only and no container role is ever assigned:

| Access Level | Effective `AccessRights` (BFF) | SPE Container Role |
|-------------|-------------------------------|--------------------|
| View Only | Read | n/a — NOT IMPLEMENTED (broker-only) |
| Collaborate | Read + Create + Write | n/a — NOT IMPLEMENTED (broker-only) |
| Full Access | Read + Create + Write + Delete | n/a — NOT IMPLEMENTED (broker-only) |

---

## A Filed Child's Access Follows Its Parent (owner round 84, task 174)

**Rule (binding).** A work assignment or project that has a parent is enforced with its EFFECTIVE Secure flag and Access
Permission: the most restrictive of its own `sprk_issecure` / `sprk_accesspermission` and those of every matter or
project above it in the filing chain (secure-if-any; Restricted over Limited over Standard). A matter files under nothing
and is unchanged. Task 175 makes the STORED values cascade and locks them on the child; until a stored value catches up
(inheritance pending, Refused or Failed, or a cascade in flight) enforcement computes the value from the chain and fails
closed.

- **One walk.** `SecureRootInheritance.ReadSecureParentsAsync` / `ReadSecureParentsOfManyAsync` (#1410) climbs the chain
  (bounded by `MaxFilingDepth`, cycle-safe) and reads each ancestor's Access Permission in the same parent read as its
  Secure flag. `EffectiveRootFlags` folds that answer into the record's own `RootRecordFlags`.
- **Where it applies.** The read path (`AccessibleRecordSetService`: direct-only cancellation, Restricted, the systemuser
  plane's Restricted survivor — one walk per composition shared with the No Access veto) and every access caller of the
  flag read through `ExternalParticipationService.GetEffectiveRootRecordFlagsAsync`: the write-time grant policy, the
  grantor ceiling's counted rows, the internal-user Restricted bar and its listing marker, the share-link refusal, the
  Restricted share remover and the Assigned-To materializer. The No Access guard and enforcer treat a record with a secure
  ancestor as secure (Q4's "secure" is the record or any filing ancestor, round 82).
- **The contact plane honours a secure parent's No Access list (#1425).** The read-time contact veto and the write-time
  grantee check ask every secure ancestor's list, each carrying its own referenced organizations, in the same deny-list
  query.
- **Fails closed.** An unreadable filing row, pair type or ancestor flag, an EMPTY ancestor flag, or a chain past the bound
  makes the record `RootRecordFlags.Unreadable` (secure AND Restricted): removed on the read path, refused as "could not be
  read" at grant time.
- **Display.** Task 064's per-record read (`GET /api/v1/records/{table}/{id}/no-access`) reports the effective `secure`,
  `accessPermission` and `inheritedFrom`; Manage Access gates and marks "No effect" from the stricter of those and the
  record's stored values.
- **Not routed (own values on purpose).** `/unshare-user`'s last-reader rule (S5) follows the stored ownership.
- **Readers outside the flag read (#1478, task 175)** — provisioning's external-flagged creator / colleague rule, the
  secure-child share synchronizer's Restricted read, SPE container membership, Office edit — consult
  `EffectiveRootFlags.RestrictedThroughFilingAsync` beside the stored column, so they do not fail open while a stored value
  catches up; the Assigned-To job's Restricted sweep also visits the work assignments and projects filed below a Restricted
  matter or project.

### The parent sets a floor; a child may be stricter (owner rounds 84 and 87, task 175)

Round 84 REPLACES round 6 item 4 ("parent unsecured → children stay secure") and task 158's "no unsecure cascade"
constraint. Round 87 refines round 84: the parent is a FLOOR, not an equality.

- **The rule.** A work assignment or project filed under a matter or project stores max(own, floor):
  - the floor: Secure if any ancestor is (the 174 walk, `MaxFilingDepth`); the most restrictive Access Permission arriving
    through its DIRECT parents (each folded with what is above it);
  - own: what was set on the record by hand (Make Secure; a stricter Access Permission on the form), or held while parentless.
  An inherited value follows the parent both ways; an own value stays when the parent loosens. A re-file never loosens: what
  the record held beyond the new floor becomes its own. A parentless record keeps and edits its own values.
- **The access record.** `sprk_accessinheritance` (JSON, BFF-written; `docs/data-model/access-inheritance.md`) keeps the
  floor last applied, the parents it came from, and the own values — so a user's edit, a parent's change and a re-file are
  told apart. Existing rows: the backfill rule (equal to the floor = inherited; stricter = own), applied on first sight.
  An empty record never loosens anything. The column is field-secured (only the BFF application users read or write it),
  and every BFF writer refuses a caller-supplied write naming it (`sdap.access.access_record_server_only`): a forged
  "inherited" would otherwise let a parent's un-secure take a hand-set Secure away without F3 (verifier F1-1). No record is
  trusted while the column is not secured, and an EMPTY one is decided on only once the BFF's read of the column is proven.
  The generic BFF writers refuse `sprk_issecure` on a work assignment or project (`sdap.access.secure_flag_transition_only`):
  only Make Secure, Remove Secure and the cascade set it.
- **One invariant owner:** `SecureRootInheritance.FollowParentsAsync` (`SecureRootInheritance.Cascade.cs`), called by the
  parent's `/unsecure-project` (everything filed below it, top-down, bounded at 50, the rest left to the job), by the BFF
  re-file writers (`SecureAfterWriteAsync`), right after a caller's Make Secure on a work assignment or project (records the
  own secure at once), and by `SecureRootInheritanceJob` (every 5 minutes: every work assignment and project decided over
  one batched walk; only differing values written; a project changed in the run sends what is filed under it round again;
  25 un-secures and 500 other writes per run, the rest deferred with a cursor).
- **Un-secure is the endpoint's own steps.** `UnsecureProjectEndpoint.UnsecureInheritedAsync` runs task 158's sequence with
  no caller (no F3) and a TEAM owner — the business-unit team the ownership rule (`RecordOwnershipResolver`, record-first,
  D-11) gives a record filed under its now-ordinary parents; never the Secure Record Owners team. It runs only for an
  INHERITED Secure. Ownership is moved and read back first, related records leave isolation, the shares are revoked, the
  flag is cleared LAST. Existing documents stay in the record's former container (as for `/unsecure-project` on a parent);
  reads are broker-only either way.
- **Fail closed.** Order inside one record: a STRICTER Access Permission first, then the un-secure, then a LOOSER Access
  Permission, then the access record. A step that does not complete leaves the record at the more restrictive state (still
  flagged secure, permission not loosened, the access record still saying "secure applied"), reports it, and the next call
  or run completes it. An unreadable filing, parent, own flag or access record loosens nothing.
- **The floor lock.** Tightening is never refused (Make Secure on a child is allowed). `POST /unsecure-project` on a work
  assignment or project whose floor is secure answers **409 `sdap.access.access_follows_parent`** (extensions
  `parentRecordType`, `parentRecordId`, `parentName`); one whose secure is its own is un-secured by its F3 holder as a
  parentless record is. A BFF update of `sprk_accesspermission` below the floor, or of `sprk_issecure` to false under a secure
  floor, is refused with the same code through `SecureRootFilingGate`; an unreadable filing refuses
  (`record_owner_parent_undetermined`). `GET /can-manage-access` reports `followsParents`, `parentUnverifiable`,
  `floorSecure` and `floorAccessPermission`. The ribbon (`access_ribbon.js` 1.8.0) hides Remove Secure when the floor is
  secure (and both commands when the filing cannot be read). The form library (`sprk_accesspermission_inherited.js` 1.2.0)
  puts back a looser pick and says "inherited from" / "set on this record". Manage Access shows the parent's floor beside the
  Restricted / Secure banner and offers no Access Permission below the floor. Grants and shares on a child stay available.
  `/user-shares` marks a share a secure parent passed on (`inheritedFrom`), shown read-only.

## The Grant Model — Who May Grant, and Up To What (task 139, owner decision 2026-09-30)

**The gate is Write AND Share** (owner round 89, task 179; it replaced the Write-only rule of B-14 / task 118 D-1). `DelegationRuleFilter` admits a caller to every `/api/v1/external-access/*` route (grant, invite, revoke, share and unshare, the user-share list, expiry, close, Make Secure / Remove Secure, the Assigned-To list and dismiss, `/can-manage-access`) only when the caller holds **Write** on the record AND Dataverse's **Share** privilege on its table at a depth that reaches it (user, business unit, parent-child business unit or organization), evaluated as the caller over OBO. Both halves come from ONE `RetrievePrincipalAccess` answer (`WriteAccess` and `ShareAccess` in the same rights string), so the rule adds no round trip. No client mirrors it: `TrackingFieldTrio`, the Access flyout and the ribbon all ask `/can-manage-access`, so a role without Share no longer sees Manage Access, Update Access, Make Secure or Remove Secure. The record's Access Permission / Secure flags do not change the gate; they govern WHICH grant types apply, at write time (task 138).

- **Refusals:** no Write → **403** `sdap.access.deny.delegation_write_required`; Write but no Share → **403** `sdap.access.deny.delegation_share_required`; an answer that cannot be established (no caller token, a failed exchange or `RetrievePrincipalAccess`, an unparseable rights string) is a denial, never a pass (ADR-003). Any non-200 from `/can-manage-access` hides the affordance.
- **Two deliberate exceptions.** `/no-access/enforce` targets the No Access ENTRY, an organization-owned table: Dataverse refuses `RetrievePrincipalAccess` there, and such a table has **no Share privilege** to hold (on dev `sprk_noaccessentry` has Create, Read, Write, Delete, Append and AppendTo only), so it keeps the table Write privilege. `/assigned-access/sync` keeps Write alone: it applies the record's own Assigned-To columns, which the app-only reconciliation job applies within five minutes anyway, and the post-save script and every create wizard call it on each save.
- **System paths are unchanged.** The reconciliation jobs, the Assigned-To materializer run by the job, the secure-child cascade and every other app-identity path do not run as the caller and are not behind this filter.
- **Platform note.** Microsoft documents that Dataverse checks the privilege before record access, so a share cannot give rights the role does not allow. Whether `RetrievePrincipalAccess` reports a SHARED `ShareAccess` to a user whose roles grant no Share is not documented; the live gate in `projects/unified-access-control-r2/notes/task-179-share-privilege.md` checks it on dev.

**Roles: who can manage access.** Manage Access follows the security role, so grant Share on `sprk_project`, `sprk_matter` and `sprk_workassignment` only to roles whose holders should manage access, at the same depth as their Write. A role that should edit records but not manage access needs Write without Share. A role that holds Share through another role assigned to the user, or to a team the user belongs to, still manages access: Dataverse unions a user's roles. The dev inventory (2026-10-09) and the roles to change are in the task-179 notes.

**Internal (POA) share levels** — `Services/Access/RecordShareLevels.cs`, the ONE level-to-rights table, in Dataverse's own `AccessRights` numbers:

| Level | Rights | Stored mask | Legacy mask (read as this level) |
|---|---|---|---|
| View Only | Read | 1 | — |
| Collaborate | Read, Write, Append, AppendTo, **Share** | 262167 | 23 |
| Full Access | Collaborate + Delete | 327703 | 65559 |

No level carries Assign. Collaborate and Full Access carry Share so a Write-holder can also use the model-driven app's own **Share** command (Dataverse's native sharing rule still stops a sharer handing out a right they lack). Colleagues named at secure provisioning receive exactly the creator's rights (the Collaborate level). Shares written before 2026-09-30 still read as their level; `scripts/Upgrade-LegacyRecordShareMasks.ps1` (operator-run, dry-run by default) upgrades them.

**The grantor ceiling.** Every MANUAL grant is capped at the grantor's own level — `/grant` (contact and organization-wide), `/invite-and-grant`, and `/share-user`:

- The handler re-probes the caller's rights as the caller (`CallerRecordAccessProbe`), never trusting the filter's earlier answer. A probe that throws → **500** `sdap.access.grant.caller_rights_unreadable` (`/share-user`: `sdap.access.user_share.read_failed`); nothing is written or onboarded.
- `ExternalAccessLevels.GrantCeilingFor` maps the rights to a ceiling over Read / Write / Delete only: Full Access iff Read+Write+Delete; Collaborate iff Read+Write; View Only iff Read; otherwise none (→ **403** `sdap.access.grant.caller_cannot_grant`). Create, Append, AppendTo and Share are not consulted (RetrievePrincipalAccess need not report CreateAccess on an existing record).
- A request above the ceiling is **narrowed, not refused**: written at the ceiling, and the response says so (`grantedAccessLevel`, `narrowed: true`). `/share-user` intersects right by right (Dataverse's own rule) and reports its existing `narrowed` flag.
- **Never silently lower.** When the request was narrowed and the grantee already holds more (a higher active grant row, or share rights the narrowed mask lacks), the write is refused with **409** `sdap.access.grant.would_lower_existing` — the grant upsert updates levels in place and ModifyAccess replaces rights, so without this a "Full Access please" capped to Collaborate would lower someone else's Full Access grant. An explicit request for a lower level (not narrowed) is a deliberate downgrade and is applied.
- **A re-add over a lapsed grant is a SET (owner round 80, task 113).** On `/grant` and `/invite-and-grant` (the Manage Access re-adds send no date), a request with no expiry over a key on which no row still confers is restored at the PICKED level with today + 90; the 200 reports the level written. The ceiling, the contact-issuer expiry cap and never-lower still apply (a narrowed re-add over a lapsed HIGHER row is still 409 `would_lower_existing`). The Assigned-To rule does not opt in: a dateless request it sends over a lapsed key is refused and writes nothing.
- **No Access list at write time.** A contact grantee on the record's No Access list — directly, through one of its active organizations, or (org-wide grant) the organization itself — is refused with **422** `sdap.access.grant.grantee_denied`, from the same veto code the read path uses (`IAccessibleRecordSetService.CheckGranteeNoAccessAsync` → `ResolveDenyVetoAsync`). The check answers a **tri-state** (task 142 r4, owner round 13 item 4): Allowed, Denied (an entry), or Unverifiable — a read fault (Dataverse 5xx, throttling, a timeout, unreadable memberships or referenced organizations, a fail-closed deny-list read). Unverifiable refuses too (fail closed), but as a fault — **503** `sdap.access.grant.no_access_unverifiable` — never absorbed into `grantee_denied`. The read path removes both (unchanged). The internal-user No Access list on `/share-user` is task 143's.

**Where the checks live (WP-1).** The policy (task 138), ceiling, never-lower and No Access checks run inside the one grant-writing core, `GrantExternalAccessEndpoint.CreateGrantAsync`, which takes a REQUIRED `GrantCeiling`; `/invite-and-grant` runs the same `CheckGrantAsync` BEFORE onboarding (resolving an existing contact by email read-only), so a refusal leaves no Contact or CIAM account behind. The ArchTest `GrantCeilingGuardTests` pins that the core is the only writer of a grant's level and that every call supplies a ceiling. Assigned-To auto-grants (task 142) are uncapped Collaborate (owner rule 5) and supply their own named ceiling.

**A secure record always keeps someone who can see it** (owner round 3, S5): `/unshare-user` refuses to remove the last enabled user whose share can read a secure record (**409** `sdap.access.user_share.last_reader_on_secure_record`).

---

## Contact-Side Grant Access — Contacts Granting Colleagues (task 140, owner C4 / Q1 / Q2)

A **contact** holding **Collaborate or Full Access** on a project, matter or work assignment may grant **colleagues of its own organization**, **at or below its own level**, **never organization-wide** — from the external SPA (and its Teams tab). View Only may not. These are the first routes that let a principal that is NOT a Dataverse user mint access, so the authorization is derived entirely from the BFF's own principal model; there is no Dataverse backstop.

**Routes** (on the principal-agnostic `/api/v1/external` group, ADR-028 A3 — a contact can authenticate nowhere else; each carries `ContactGrantorAuthorizationFilter`, and each handler re-runs the same checks):

| Method | Route | Purpose |
|---|---|---|
| `POST` | `/api/v1/external/contact-grants` | Grant ONE colleague (by `granteeContactId` or `granteeEmail`) on `recordType` + `recordId` at `accessLevel`, optional `expiryDate`. The body is CLOSED: an unknown member (e.g. `organizationId`) is **400**. |
| `GET` | `/api/v1/external/contact-grants?recordType=…&recordId=…` | The grants the caller issued on that record (active rows). |
| `POST` | `/api/v1/external/contact-grants/revoke` | Revoke one grant the caller issued (`accessRecordId`). |

**Who may (the filter, in order):** a contact principal (CIAM contact or workforce contact-only) — a workforce **systemuser**, even one carrying a linked contact id, is **403** `sdap.access.contact_grant.use_manage_access` (internal users grant through Manage Access, gated on their own Write and Share). The caller's **effective, post-veto** level on the record (the evaluator's answer on `CallerPrincipal` — owner decision G3 (a)) must be Collaborate or Full Access; View Only and "no access at all" are the same **403** `sdap.access.contact_grant.level_insufficient`. On a **Secure or Limited** record that level comes from the contact's DIRECT grant only (FR-22, task 135's CIAM parity); on a **Restricted** record a contact holds nothing, so it is refused here first, and the grant core's **422** `sdap.access.grant.record_restricted` (task 138) is the second, independent refusal. To grant, the caller must hold an active organization membership (**403** `sdap.access.contact_grant.no_organization`); a membership read that could not be completed is **503** `sdap.access.contact_grant.membership_unreadable`, never "no organization". An unmapped request type is denied by default.

**Who can be granted:** ONE **active** contact holding a **conferring** membership (active junction row, current on both `sprk_startdate`/`sprk_enddate`, active organization — the conferring rule of `ExternalParticipationService.ProjectOrganizationMemberships`, task 109) in an organization the grantor also confers through. The grantee is resolved **strictly within the grantor's organizations** — nothing about anyone else is considered or disclosed (the route is not a contact oracle):

- **By `granteeContactId`** — the contact's own membership read (`ReadOrganizationMembershipsAsync`). An id that names no contact, an inactive contact and an active contact of ANOTHER organization are ONE answer: **422** `sdap.access.contact_grant.grantee_not_in_organization` worded "This person …" — the stored email of the contact a GUID names is never echoed, so a known GUID reveals neither whether the contact exists nor its address.
- **By `granteeEmail`** — ONE scoped read (`ExternalParticipationService.FindConferringMembersByEmailAsync`): the `sprk_contactorganization` rows of the grantor's organizations (chunked, every chunk read) whose expanded contact is active and uses that email, re-decided in code by the same conferring rule. No such colleague is **422** worded with the address the caller typed; MORE THAN ONE colleague is **409** `sdap.access.contact_grant.grantee_ambiguous` ("more than one person in your organization"). A person outside the grantor's organizations is never counted — a colleague is granted even when an outsider shares the address, and no answer says such an outsider exists.

A person who is not yet a colleague in the system is refused, never created, onboarded or added to an organization (owner decision G1 (a); **no route here writes `sprk_contactorganization`** — pinned by `ContactGrantGuardTests`). A read that could not be completed is **503** `sdap.access.contact_grant.membership_unreadable`, never "not a member". Self-grant is **400**; a No Access list entry is **422** `sdap.access.grant.grantee_denied` and an unverifiable list **503** `sdap.access.grant.no_access_unverifiable` (task 139's entry point, tri-state since 142 r4).

**What is written** — through the ONE grant core (`GrantExternalAccessEndpoint.CreateGrantAsync`) in its **contact-issuer mode**, with the contact's effective level as the REQUIRED ceiling (`GrantCeiling.FromContactGrantorRights`):

- **Capped level** (owner Q1, round 3b): a request above the grantor's level is narrowed and reported (`grantedAccessLevel`, `narrowed`).
- **Capped expiry** (owner decision G2 (i)): the requested expiry, or today + 90 days, is cut back to the latest date the grantor's own qualifying DATED grant lasts (their direct row, or — on a Standard record — their organization's org-wide row, at the granted level or above), reported as `expiryNarrowed`. A workforce contact whose level rests on an undated standing-grant / organization-expansion term issues the plain default.
- **Issuer:** the new contact-typed lookup **`sprk_grantedbycontact`** = the grantor; `sprk_grantedby` (systemuser) stays empty. Every write logs `[EXT-CONTACT-GRANT]`. In the SAME write the grantor's id is recorded as text in **`sprk_grantedbycontactid`** (session 27 round 50 item 2): the lookup's Delete cascade is RemoveLink, so deleting the contact empties the lookup — the provenance survives it, and every write that sets or clears the lookup sets or clears the provenance with it. "Recorded but the lookup is empty" therefore means exactly "the issuing contact was deleted".
- **No proxy change** (owner decision G2 (iii)): a colleague who already holds a row anybody ELSE issued is refused **409** `sdap.access.contact_grant.managed_elsewhere` and the row is left untouched. On the caller's OWN row a higher level raises it, a lower one is **409** `sdap.access.grant.would_lower_existing`, and an earlier expiry never shortens it.
- **No cascade** (owner decision G2 (ii)): an issued row stands on its own if the grantor later loses access; it stays visible and revocable in Manage Access ("Granted by {contact} (external contact)").
- **Revoke** deactivates only the caller's own rows on that grant; a missing row and somebody else's row are the same **404** `sdap.access.contact_grant.not_found`; the caller must still hold Collaborate or Full Access on the record. A fault is a ProblemDetails with a message (`sdap.access.contact_grant.revoke_failed`), never a bare 500.
- **An internal user changing a contact-issued row takes it over** (session 27 round 34 item 3): `sprk_grantedbycontact` and its provenance `sprk_grantedbycontactid` are cleared and `sprk_grantedby` set to the changing systemuser **in the same write** as the change, so the contact can neither revoke nor re-lengthen a decision somebody else made. A row whose issuing contact was deleted (provenance only) is contact-issued too and is taken over the same way (round 50 item 2) — otherwise the job below would later read the internal user's grant as a deleted contact's. The writers that change a grant row and leave it active both do it: `/grant` (and the Assigned-To rule) when it changes the level or expiry, and `POST /api/v1/external-access/set-record-share-expiry` (the record-wide Expiration) inside its one transaction. The systemuser stamp is audit — an unresolvable caller never blocks the write — but the contact issuer is cleared either way. The writers that only DEACTIVATE (`/revoke`, project closure, the reconciliation job's R2, the Assigned-To materializer's deactivations, the grant core's duplicate collapse) leave the issuer as the row's history: a contact's grant, list and revoke change only ACTIVE rows it issued, so an inactive row is out of its reach. ONE writer changes an ACTIVE contact-issued row and deliberately keeps the contact as issuer: the reconciliation job's **R1**, on a contact-issued row with NO expiry (possible only after an administrator cleared the date — the contact route always writes one). No internal person acts there, so nothing is taken over; instead the row may never outlive its issuer's own access (session 27 round 42 item 2): R1 stamps the EARLIER of today + 90 and the latest date the issuing contact's own grant on that record lasts at the row's level or above (`ExternalGrantLifecycle.ReadContactHeldGrantsAsync` — the same rule that caps the expiry the contact route issues), and DEACTIVATES the row when the issuing contact no longer holds such a grant there. When the issuing contact was **deleted** (`sprk_grantedbycontactid` set, the lookup empty — round 50 item 2), R1 deactivates the undated row with no read and reports it as `IssuerDeleted`, keeping the provenance; a DATED row whose issuer was deleted stands until its date, as any dated row whose issuer lost access does (G2 (ii): no cascade). Without the provenance such a row would look internally issued and be stamped +90 — a grant outliving its issuer. R1 writes only when the owner switch `ExternalAccess:Reconciliation:WritesEnabled` is on; report-only runs report each such row with the outcome it would get. No Spaarke security role holds Write on `sprk_externalrecordaccess` (only System Administrator / System Customizer / Service Writer, live 2026-10-04), so there is no other internal write path. A deliberate exclusion of a person (rather than a time bound) is the No Access list, which the contact route enforces at write time.
- **A take-over that races a contact's write wins** (session 27 round 42 item 1). A contact decides "this row is mine" from a read, so every contact-side write to such a row is CONDITIONAL on the version that read returned (`@odata.etag` sent as `If-Match`): the grant's PATCH of the caller's own row (`GrantExternalAccessEndpoint` contact-issuer mode, `DataverseWebApiClient.UpdateIfMatchAsync`), the revoke's deactivation and the contact-issuer mode's duplicate collapses (`ExternalGrantLifecycle.DeactivateIfUnchangedAsync`). If an internal user takes the row over in between, the write fails (HTTP 412) instead of lengthening or ending the internal decision: the answer is **409** `sdap.access.contact_grant.managed_elsewhere` with the same message as above, the row is re-read for the record, and the write is never retried. A revoke that had already ended another of the caller's own rows on the same grant says so ("The access you granted was ended.") instead of "Nothing was changed."; a duplicate collapse simply leaves a taken-over duplicate in place. A row read without a version is never written unconditionally — the route answers 500 with its message.

**Deploy order.** The BFF selects `sprk_grantedbycontact` and `sprk_grantedbycontactid` on every grant-row read, so `scripts/Deploy-ExternalRecordAccessContactGrantor.ps1` (dry run / `-Apply` / `-Verify`; both columns field-secured, writable only by the BFF; `-Apply` also backfills the provenance from the lookup, and `-Verify` fails while any row lacks it) must have run in an environment BEFORE a BFF or TrackingFieldTrio (v1.0.35) carrying task 140 is deployed there.

---

## Troubleshooting

| Issue | Cause | Solution |
|-------|-------|----------|
| "Access Denied" despite permissions | Cache staleness | Wait 60s–2min TTL |
| "Unknown operation" error | Operation not in policy | Use valid operation from `OperationAccessPolicy` |
| External user denied despite participation | Grant row inactive / wrong root lookup, or stale participation cache | Check the `sprk_externalrecordaccess` row is Active and its typed root lookup is bound; cache refreshes within 60s. (The former "BFF must add Contact to container via Graph" advice described unbuilt behavior — external SPE access is broker-only, no container membership exists) |
| AI Search returns no results for external | External AI Search filtering is NOT IMPLEMENTED — no CIAM route reaches AI Search | Expected behavior until an external search plane is built |

---

## Related Documentation

| Document | Purpose |
|----------|---------|
| [sdap-auth-patterns.md](sdap-auth-patterns.md) | OBO patterns including Pattern 5 (direct query fix) |
| [external-access-spa-architecture.md](external-access-spa-architecture.md) | External SPA three-plane access detail |
| `.claude/patterns/auth/uac-access-control.md` | Concise implementation guide |
| `.claude/constraints/auth.md` | MUST/MUST NOT rules |

---

*Last Updated: 2026-08-20 — corrected against code by the `unified-access-control-r2` investigation (three-plane orchestration, Power Pages retirement, direct-query-only access probe, cache keys, filter patterns, fail-closed semantics, access-level rights mapping).*
