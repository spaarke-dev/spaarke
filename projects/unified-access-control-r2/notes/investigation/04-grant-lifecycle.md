# Investigation 04 — External Grant/Entitlement Data Model & Lifecycle (grant → use → change → revoke → expire)

> **Worktree**: `c:/code_files/spaarke-wt-unified-access-control-r2` · **Date**: 2026-08-20 · READ-ONLY code investigation.
> All paths relative to worktree root unless absolute. Code is ground truth; the published schema doc
> (`src/solutions/SpaarkeCore/entities/sprk_externalrecordaccess/entity-schema.md`, v1.0 2026-03-16) is confirmed stale.

---

## Summary (security-critical first)

1. **Expiry is NOT enforced anywhere.** `sprk_expiresdate` is written on grant (`GrantExternalAccessEndpoint.cs:325`) but never filtered in any read query, never checked in memory, and no sweep/worker deactivates expired rows. An expired grant confers full access forever until someone manually deactivates the row.
2. **Project closure's Dataverse cascade is almost certainly broken at runtime.** `ProjectClosureEndpoint` `$select`s `_sprk_contactid_value` (`ProjectClosureEndpoint.cs:181`) — a column that does not exist (live-verified attribute is `sprk_contact` → `_sprk_contact_value`, `ExternalParticipationService.cs:402-406`, task-070 note). Dataverse 400s an unknown `$select` property; `DataverseWebApiClient.QueryAsync` throws (`DataverseWebApiClient.cs:190`), the helper rethrows (`ProjectClosureEndpoint.cs:198-204`), and `Handle` has no catch → the endpoint 500s and **revokes nothing**. The task-070 regression test guards only the `$filter` (`tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/PolymorphicGrantWriteTests.cs:364-374`), and task-070 notes admit close-project was "not live-smoke-tested" (`projects/spaarke-SPA-external-access-platform-r2/notes/task-070-deviations.md:97-99`).
3. **Anyone with a valid workforce JWT can grant/revoke/close/provision.** The management group is `RequireAuthorization()` only — no role, policy, or record-level check (`ExternalAccessEndpoints.cs:109-111`).
4. **/grant is not idempotent** — duplicate active rows accumulate; `/revoke` deactivates one row by id, so a duplicated grant survives revocation.
5. **Even when close-project is fixed**, org-grant rows (contact-empty) are filtered out of the cascade (`ProjectClosureEndpoint.cs:190`) and would stay active after closure.
6. The grant model itself is **fit for child-record access with zero schema change** — child visibility is already a pure read-composition (parent-lookup ∈ accessible-root-set) shipped for documents/invoices.

---

## 1. Live schema (code-truth field inventory)

Write form = `@odata.bind` navigation property (PascalCase, live-verified per task 070); read form = `_{logical}_value`.

| Attribute (logical) | Write form (payload key) | Read form ($select/$filter) | Evidence |
|---|---|---|---|
| `sprk_contact` → contact | `sprk_Contact@odata.bind` = `/contacts({id})` — **omitted for an org grant** | `_sprk_contact_value` | write `GrantExternalAccessEndpoint.cs:308-311`; read `ExternalParticipationService.cs:405-406` |
| `sprk_project` → sprk_project | `sprk_Project@odata.bind` = `/sprk_projects({id})` | `_sprk_project_value` | `ExternalGrantRoot.cs:35-41`; `ExternalParticipationService.cs:407` |
| `sprk_matter` → sprk_matter | `sprk_Matter@odata.bind` = `/sprk_matters({id})` | `_sprk_matter_value` | same |
| `sprk_workassignment` → sprk_workassignment | `sprk_WorkAssignment@odata.bind` = `/sprk_workassignments({id})` | `_sprk_workassignment_value` | same |
| `sprk_organization` → **sprk_organization (NOT account)** | `sprk_Organization@odata.bind` = `/sprk_organizations({id})` | `_sprk_organization_value` | `GrantExternalAccessEndpoint.cs:330-334`; `ExternalParticipationService.cs:509` |
| `sprk_grantedby` → systemuser | `sprk_GrantedBy@odata.bind` = `/systemusers({systemuserid})` (resolved from AAD oid; omitted if unresolved) | — | `GrantExternalAccessEndpoint.cs:313-319`, `:206-231` |
| `sprk_accesslevel` (choice) | int literal | `sprk_accesslevel` | `GrantExternalAccessEndpoint.cs:299`; enum `ExternalCallerContext.cs:119-124`: **ViewOnly=100000000, Collaborate=100000001, FullAccess=100000002** |
| `sprk_granteddate` | ISO-8601 string, UtcNow | — | `GrantExternalAccessEndpoint.cs:300` |
| `sprk_expiresdate` | ISO string when `ExpiryDate` supplied — **field is `sprk_expiresdate`, NOT `sprk_expirydate`** | **never read** | `GrantExternalAccessEndpoint.cs:321-326`; regression test `PolymorphicGrantWriteTests.cs:289-303` |
| `statecode`/`statuscode` | revoke writes `{statecode:1, statuscode:2}` | every read filters `statecode eq 0` | `RevokeExternalAccessEndpoint.cs:96`; `ExternalParticipationService.cs:406,511` |
| `sprk_invoice` → sprk_invoice | never written | **intentionally never read** | `ExternalParticipationService.cs:403-404`; `ExternalCallerContext.cs:92-95` |

**Access-level → rights mapping** (BFF-side): ViewOnly=Read; Collaborate=Read|Create|Write; FullAccess=+Delete (`ExternalCallerContext.cs:55-65`). SPE role map (dormant service): ViewOnly→reader, Collaborate/FullAccess→writer (`SpeContainerMembershipService.cs:24-29`).

### Divergences from the published schema doc (`entity-schema.md`)

| Doc claim (line) | Reality |
|---|---|
| `sprk_contactid` lookup, **required** (`:35`) | logical name is `sprk_contact`; **optional** — empty contact = org grant (`GrantExternalAccessEndpoint.cs:74-79`, `:303-311`) |
| `sprk_projectid` (`:36`), `sprk_matterid` (`:37`) | `sprk_project` / `sprk_matter` (task-070 deviations table) |
| `sprk_accountid` → account (`:38`) | **does not exist**; replaced by `sprk_organization` → `sprk_organization` (task-070 Bug 4) |
| `sprk_expirydate` (`:47`; also `views-schema.md:29` etc.) | `sprk_expiresdate` |
| `sprk_grantedby` required (`:39`) | optional; omitted when oid→systemuser resolution fails (`GrantExternalAccessEndpoint.cs:203-205`) |
| No `sprk_workassignment`, `sprk_organization`, `sprk_invoice` lookups listed | all three exist on the live table |
| Power Pages web-role parent chain / "Secure Project Participant" (`:186-197`) | retired model; current architecture is CIAM broker-only, no Power Pages; `/revoke` hardcodes `WebRoleRemoved: false` (`RevokeExternalAccessEndpoint.cs:182`) |
| Business rule 1: unique (Contact, Project) active pair (`:203`) | **not enforced** — no plugin/endpoint dedupe; read side dedupes by GroupBy (`ExternalParticipationService.cs:469-472`) |
| Business rule 4: "scheduled BFF worker checks sprk_expirydate and deactivates expired records" (`:209`) | **no such worker exists** (repo-wide grep: `sprk_expiresdate` appears only in the write payload + tests) |
| Business rule 3: deactivation triggers BFF to orchestrate Plane 2/3 (`:207`) | only via the `/revoke` endpoint itself, and only when the caller supplies `ContainerId`; MDA-side deactivation of a row triggers **nothing** |

---

## 2. Grant read path — `QueryGrantSetAsync` exact filters

Single entry: `ExternalParticipationService.GetGrantSetAsync` (Redis 60s TTL, key `tenant:{tid}:external-access-grant:{contactId}:v3`; `ExternalParticipationService.cs:18-34,86-124`) → on miss `QueryGrantSetAsync` (`:392-490`):

- **Per-contact query** (`:405-407`):
  `sprk_externalrecordaccesses?$filter=_sprk_contact_value eq {contactId} and statecode eq 0&$select=_sprk_project_value,_sprk_matter_value,_sprk_workassignment_value,sprk_accesslevel`
- Rows partitioned into `ExternalGrantSet { Projects(+level), Matters, WorkAssignments }` (`:428-443`); project grants deduped keeping the **highest** level (`:469-472`).
- **Org-grant union** (Term 3, `:450-464`, see §4).
- Faults → `ExternalGrantSet.Empty` (fail-closed, `:485-489`). No-tenant-claim requests skip the cache entirely (`:93-94,118-121`).
- Consumers: `AccessibleRecordSetService.ComposeForContactAsync` (Term 1, `AccessibleRecordSetService.cs:257-268`) and `ComposeForSystemUserAsync`'s own-contact union (`:203-226`). Authorization *decisions* are never cached — composition runs per request; only the grant *data* is cached 60s.

## 3. Expiry-enforcement verdict: **NOT ENFORCED AT ALL** (security-critical)

- The only filters, verbatim, are `statecode eq 0` + grantee/org (`ExternalParticipationService.cs:406` per-contact; `:511` org; `ProjectClosureEndpoint.cs:167` closure). `sprk_expiresdate` is **not in any `$filter`**, **not in any `$select`**, and there is **no in-memory check** anywhere in `Infrastructure/ExternalAccess/**` or `Api/ExternalAccess/**`.
- Repo-wide grep for `sprk_expiresdate|sprk_expirydate`: the only production hit is the write at `GrantExternalAccessEndpoint.cs:325`. No background job reads it (the only expiry-filtered read seam in the BFF is the unrelated notifications outbox, `Services/Notifications/OutboxService.cs:194-195`).
- The schema doc's promised expiry worker (`entity-schema.md:209`) was never built.
- Consequently: **an expired grant is fully live** on every plane until its row is manually deactivated. The `sprk_expiresdate` value is write-only decoration. (Contrast: the retired `IThreadPrivateGrantProvider` contract explicitly demands expiry exclusion — `Services/Communication/Access/IThreadPrivateGrantProvider.cs:23-24,43` — showing the intended pattern exists elsewhere but was never applied here.)
- Related myth: unit test `ExternalAccessEndpointTests.cs:434-441` ("NullExpiryDate_DefaultsTo30Days") computes the default inside the test; the handler has no 30-day default — a null `ExpiryDate` writes **no** expiry.

## 4. Organization grants (contact-empty + org-bound rows)

- **Write**: `ContactId == Guid.Empty` + `OrganizationId` required (`GrantExternalAccessEndpoint.cs:74-79`); payload omits `sprk_Contact`, binds `sprk_Organization` (`:303-311,330-334`). The contact-empty row IS the org-grant marker — load-bearing, not cosmetic (`:303-307`).
- **Read** (`ExternalParticipationService.cs:499-583`): two hops, both fail-closed to empty on any fault:
  1. Junction: `sprk_contactorganizations?$filter=_sprk_contact_value eq {contactId} and statecode eq 0` (`:552-554`).
  2. Grants: `sprk_externalrecordaccesses?$filter=({_sprk_organization_value eq {orgId} or …}) and _sprk_contact_value eq null and statecode eq 0` (`:509-512`).
  Org rows union into the same buckets; direct-vs-org project duplicates keep the higher level (`:469-472`).
- **Leaving a firm**: correct revocable cascade **iff** the junction row is deactivated/deleted — membership is resolved live per read, staleness bounded by the 60s participation cache. Gaps:
  - **Inactive organization not checked**: only the *junction's* statecode is filtered; `sprk_organization.statecode` is never consulted — deactivating a firm does NOT kill its grant rows (they must be individually deactivated).
  - **Junction schema assumption**: code self-flags that the junction lookup names (`sprk_contact`/`sprk_organization`) are assumed, "confirm against the created junction schema" (`:543-546`). No junction schema doc exists in `src/solutions/` (grep: no `sprk_contactorganization` entity folder).
  - **Cache fan-out**: org-grant create/revoke deliberately does NOT invalidate members' caches — up to 60s window (`GrantExternalAccessEndpoint.cs:158-166`; `RevokeExternalAccessEndpoint.cs:153-160`).
  - **Closure misses org rows**: `ProjectClosureEndpoint.cs:190` requires a non-null contact, so org-grant rows on a closed project are never deactivated (in addition to the §7 select bug).
  - No expiry on org grants either (§3 applies).

## 5. Standing grant (`contact.sprk_standinggrant`) + FLS dependency

- **Read**: app-only via `IDataverseService.RetrieveAsync("contact", id, ["sprk_standinggrant"])` (`ContactStandingGrantReader.cs:81-83`), live per composition — never cached (`ExternalParticipationService.cs:138-141`).
- **FLS behavior — verified fail-closed**: the field is `IsSecured=true` (`projects/teams-app-r1/notes/050-standing-grant-field-schema.md` §1). When the BFF app user lacks FLS read, Dataverse silently strips the attribute from an otherwise-successful retrieve; `GetAttributeValue<bool>` then returns `false` (`ContactStandingGrantReader.cs:104` — `?? false`), and the absent-attribute case is logged at WARNING (`:94-102`). Exceptions also return `false` (`:115-127`). **Nothing anywhere treats null/absent as true** — the briefing's "reads dark, fail-closed" claim is confirmed. Consumption gate: `AccessibleRecordSetService.cs:274-284` — no flag ⇒ grants only, never automatic membership.
- **Per-environment operator dependency** (the whole feature is dark without it): the BFF Dataverse Application User must be a member of the "Standing Grant Administrators" Field Security Profile (`f4be217b-b38f-f111-b8db-7ced8ddc4a05`) — read suffices (050 note §5.4). Per the 050 note, the *schema* was applied to `spaarkedev1` only (§7), the FSP had **zero members** (§2), and the BFF FLS grant was explicitly NOT applied by task 051 (§5.4) — detection: grep BFF logs for `[WF-STANDING] … secured attribute … absent`. The briefing's "applied only to spaarkedev1 so far" is imprecise: what is applied to spaarkedev1 is the field/FSP schema; the FLS read grant itself was still outstanding as of the 050/051 notes.

## 6. Write path + revocation

**`POST /api/v1/external-access/grant`** (`GrantExternalAccessEndpoint.cs`):
- Writes ONE `sprk_externalrecordaccesses` row (create, `:146-147`); exactly one typed root bound (fail-closed root resolution `:255-279`); audited `sprk_GrantedBy` resolved oid→systemuserid, omitted on failure (`:206-231`).
- **Authorization**: group-level `RequireAuthorization()` only (`ExternalAccessEndpoints.cs:109-111`) — default (workforce) scheme, **no role/admin policy, no check that the caller has any relationship to the target record**. A CIAM token does not pass (it validates only under the "Ciam" scheme, accepted only by the `ExternalCollaboration` policy on `/api/v1/external`, `ExternalAccessEndpoints.cs:54-57`), and a caller cannot normally grant *themselves* access (grantee is a contact; caller is workforce) — but **any workforce user can grant any contact anything**, and a contact-backed workforce user could grant their own contact. The file header's "endpoint filter for internal caller check" (`:22`) describes a filter that does not exist.
- **Idempotency: none.** No existing-active-grant check → duplicate rows per (contact, root). Read side masks this (GroupBy), but `/revoke` deactivates a single `AccessRecordId` — with duplicates, revocation silently leaves access in place.
- **Cache**: per-contact Redis entry removed on success (`:167-171`), version-bound to `ExternalParticipationService.CacheVersion` (the task-073 fix for the v1-vs-v3 drift, `:29-36`); org grants rely on the 60s TTL (`:158-166`); no tenant claim ⇒ skip (`:173-178`). Invalidation failure is non-fatal (TTL fallback).
- **Notification: none.** Onboarding email fires only on first CIAM provisioning (`InviteExternalUserEndpoint.cs:127-156`); a grant to an already-provisioned contact is silent (known R3 gap 6A).
- **SPE: untouched** — broker-only, response hardcodes `SpeContainerMembershipGranted: false` (`:116-117`).

**`POST /api/v1/external-access/revoke`** (`RevokeExternalAccessEndpoint.cs`):
1. Deactivate row `{statecode:1, statuscode:2}` by `AccessRecordId` — authoritative, root- and grantee-agnostic (`:93-118`); 404 if missing.
2. SPE removal ONLY if caller supplies `ContainerId` (`:121-147`), non-fatal. **The matcher is broken**: it looks for a container permission whose UPN **contains the contact GUID** (`:222-235`) — grants (when the dormant service was used) keyed permissions by *email* (`SpeContainerMembershipService.cs:63-79`), so this practically never matches; not-found returns `true` ("may already be removed", `:237-244`).
3. Redis invalidation when `ContactId` supplied (`:161-166`); org revoke → TTL (`:153-159`).
- `WebRoleRemoved: false` always (`:182`); no notification; **not idempotent-safe against duplicates** (see above).

**Revocation latency & completeness (per plane)**

| Plane | Mechanism | Latency after /revoke | Stale-state risk |
|---|---|---|---|
| BFF Dataverse read seam (SPA/Teams: `/me`, module fetch, record checks) | per-request composition over grant set; grant data cached 60s | **0s** when ContactId+tid present (cache invalidated); else **≤60s** TTL (`ExternalParticipationService.cs:18`) | duplicate grant rows (revoke misses siblings) — indefinite; org revoke → ≤60s |
| SPE file access (external users) | broker-only: all file streams flow through BFF authz-before-stream on the same accessible set | same as row above | none beyond the read seam |
| SPE container permissions (direct Graph plane) | grant never writes them; revoke's per-user removal effectively never matches (`RevokeExternalAccessEndpoint.cs:230-232`) | **∞** for any historical/manually-created per-user permission — only `close-project` + `ContainerId` sweeps them | **orphan risk — see §7** |
| AI search | **no grant-gated external AI-search surface exists** in this worktree — `GetAccessibleProjectIds()`'s "(for AI search filter construction)" comment (`ExternalCallerContext.cs:68-71`) is aspirational; no search endpoint under `Api/ExternalAccess/` (grep: zero hits) | n/a | none today; becomes a design obligation the moment search is exposed externally |
| Expiry-based "revocation" | none (§3) | **∞** | every time-limited grant |

## 7. SPE plane coupling + orphan-access risk

- `SpeContainerMembershipService.GrantMembershipAsync` / `RevokeMembershipAsync` / `ListExternalMembersAsync` have **zero production callers** (grep `src/**`: only `RemoveAllExternalMembersAsync` is called, from `ProjectClosureEndpoint.cs:127`). The per-user SPE membership model is **dormant R1 code**; the live model is broker-only (ADR-028 A1: external users never authenticate to SPE; `GrantExternalAccessEndpoint.cs:17-19`).
- **Transactionality: none, by design and by accident.** On revoke, Dataverse deactivation commits first; a Graph failure logs and still returns 200 (`RevokeExternalAccessEndpoint.cs:133-140`). On closure, `RemoveAllExternalMembersAsync` continues past per-member failures and just reports a count (`SpeContainerMembershipService.cs:276-297`). The reverse failure order (Graph ok / Dataverse fail) cannot occur on revoke (Dataverse runs first) but can on closure (per-record deactivation loop is also continue-on-failure, `ProjectClosureEndpoint.cs:226-248`).
- **No reconciliation path exists** — nothing compares active grants against actual container permissions on any schedule.
- **Orphan-access verdict**: under the current broker-only model the practical orphan surface is (a) any *legacy/manually created* per-user SPE container permission — invisible to `/revoke` (broken matcher) and cleaned only by a closure call that happens to pass `ContainerId`; and (b) duplicate active grant rows post-revoke on the Dataverse plane. If UAC-r2 (or a Teams/native surface) ever re-activates per-user SPE membership, the missing transactional coupling and missing reconciliation become first-order security issues.

## 8. Project closure / provisioning

**`/close-project`** (`ProjectClosureEndpoint.cs:72-148`): query active project grants → deactivate each → optional SPE sweep (`ContainerId` caller-supplied) → invalidate affected contacts' caches.
- **HIGH — likely 500s today**: `$select=sprk_externalrecordaccessid,_sprk_contactid_value` (`:181`) uses the pre-task-070 stale contact column; Dataverse rejects unknown `$select` properties → `EnsureSuccessStatusCode` throws (`DataverseWebApiClient.cs:190`) → rethrow (`:198-204`) → unhandled in `Handle` → 500, **zero rows revoked**. Task 070 fixed only the `$filter` (`:166-167`) and the test guards only the filter (`PolymorphicGrantWriteTests.cs:364-374`); close-project was never live-smoke-tested (task-070 note `:97-99`).
- Even fixed: org-grant rows excluded (`:190` requires contact non-null); matter/WA grants out of scope (project-rooted filter only — arguably by design); SPE sweep skipped when caller omits `ContainerId` (`:125-133`); closure sends no notification.
- Client callers exist (`src/solutions/LegalWorkspace/src/components/CreateProject/closureService.ts`, `Spaarke.UI.Components/.../CreateProjectWizard/closureService.ts`) — so this is a user-reachable path.

**`/provision-project`** (`ProvisionProjectEndpoint.cs`): infrastructure only — validates `sprk_issecure` (`:124-129`), creates/reuses child BU (`SP-{ProjectRef}`), SPE container via `SpeFileStore` (ADR-007), external-access **Account** (OOB account, a different concept from the grant table's `sprk_organization` — task-070 note Bug 4), stores `sprk_securitybuid`/`sprk_specontainerid`/`sprk_externalaccountid` on the project (`:446-456`). **Touches no access state** — writes no grant rows, no SPE member permissions. Rollback is best-effort BU delete on container/account failure (`:535-552`); a failed reference-store is non-fatal (`:462-471`) leaving provisioned infra unlinked (manual repair).

## 9. Fitness for child-record access (UAC-r2's "member of parent P sees P's children")

**Verdict: no schema change to the grant model is needed — this is purely a read-composition extension.**
- Grants live only at the three roots (project/matter/WA — `AccessibleRecordSetService.cs:99-104`); children already derive: each external module declares `ScopeDimension`s = (child parent-lookup attribute, accessible-root-id-set), OR'd into the caller's FetchXML + re-filtered in memory (`ExternalAccessModule.cs:154-190` — `documents`: `sprk_project|sprk_matter|sprk_workassignment`; `invoices`: `sprk_matter|sprk_project`). Adding `sprk_event`/`sprk_communication`/`sprk_todo` = new descriptors with `sprk_regarding{root}` attributes — no grant-table change, no new mechanism.
- Root sets already compose from all three sources (direct grants ∪ org grants ∪ standing-grant membership — `ExternalParticipationService.cs:450-472`, `AccessibleRecordSetService.cs:252-284`), so child derivation inherits org-derived and standing-derived parent access for free.
- **`sprk_invoice` lookup implication**: the grant table carries an invoice lookup that the read path deliberately ignores (`ExternalParticipationService.cs:403-404`; `ExternalCallerContext.cs:92-95`). Its existence shows a prior design iteration contemplated *direct child-level grants* and the platform then consciously rejected them in favor of child-derives-from-root (design §6). It is a dormant seam UAC-r2 could reuse for per-child *exception* grants — but doing so reverses a documented decision and should go through §6.5 if wanted. Do not read it casually.
- What UAC-r2 **should** change while in here (robustness, not schema): enforce `sprk_expiresdate` in the two read filters (one-line OData addition: `and (sprk_expiresdate eq null or sprk_expiresdate gt {utcnow})`) or build the promised sweep worker; add an admin authorization policy on the management group; make /grant upsert-or-conflict on an existing active (grantee, root) pair; fix `ProjectClosureEndpoint`'s select + org-row handling.

## 10. Doc corrections (briefing `spa-external-access-model-briefing.md` §5 / §10)

**Confirmed accurate** (spot-verified in this worktree):
- Entire §5 live-field-name table incl. `sprk_expiresdate` (not `sprk_expirydate`), org-grant = contact-empty + org-bound, `sprk_Organization` targets `sprk_organization` not `account`, access-level values, `statecode eq 0` everywhere, deactivation = revocation, `sprk_invoice` intentionally unread.
- §5 read-path shape and line refs (`QueryGrantSetAsync :392-490`, org `:499-537`, standing `:69-128`) — line numbers match this worktree.
- §5 write/notification: `/grant` = row + cache invalidation + **no** notification; onboarding email only on first provisioning (idempotency gate `InviteExternalUserEndpoint.cs:127-135`).
- Standing grant fail-closed / reads-dark mechanics (§5) — verified precisely (see §5 above).
- §10.6 "small and additive external-plane extension" — consistent with §9 findings.

**Wrong / stale / imprecise:**
1. **§5 omission (material)**: the briefing never states that **`sprk_expiresdate` is unenforced** — the single most important lifecycle fact about this table. (§3 above.)
2. **§5 standing-grant FLS**: "applied only to `spaarkedev1` so far" — imprecise. The field/FSP **schema** is applied to spaarkedev1 (050 note §7); the **BFF app user's FLS read grant was explicitly NOT applied** by task 051 and the FSP had zero members (050 note §2, §5.4). Unless applied later out-of-band, the feature is dark even in spaarkedev1.
3. **§5 org-grant "leaving a firm drops inherited access"**: true for junction-row deactivation, but incomplete — deactivating the **organization itself** does not drop anything (org statecode never checked, `ExternalParticipationService.cs:552-554` filters only the junction), and org-grant revocation/close-project have the gaps in §4/§8.
4. **§0 table "compute model: nothing to go stale"**: overstated — the 60s participation cache, org-grant TTL windows, and duplicate-grant-row survival after revoke are all stale-able state (bounded, but not "nothing").
5. **Briefing does not mention**: management endpoints are bare `RequireAuthorization()` (no admin gate); `/grant` non-idempotent; `close-project` `$select` bug (likely total cascade failure); `/revoke`'s SPE matcher matches contact-GUID-in-UPN and effectively never fires.
6. **Published schema doc** (`entity-schema.md` + `views-schema.md`): stale on 10+ points — full divergence table in §1. The briefing correctly flags it stale; the specific corrections above supersede it.
7. Minor: e2e/unit artifacts perpetuate myths — `_sprk_contactid_value`/`_sprk_projectid_value` in `tests/e2e/specs/secure-project/revocation.spec.ts:173-174,826` (stale field names) and the "default 30-day expiry" claim (`ExternalAccessEndpointTests.cs:434-441`, `invitation-onboarding.spec.ts:92`) — no such default exists in the handler.
