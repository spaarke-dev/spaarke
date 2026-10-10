# sprk_externalrecordaccess Entity Schema

> **Entity Purpose**: Junction table linking external Contacts (or Organizations — org-wide grants omit the Contact) to polymorphic grant roots (Project / Matter / Work Assignment) with a specific access level. This is the single source of truth for "who can access what" in the external access module, read by the BFF's external authorization layer (`ExternalParticipationService` / `CallerPrincipalResolver`). The Power Pages access model originally described here is RETIRED — external callers are Static Web Apps + Entra External ID (CIAM), broker-only through the BFF (ADR-028 A1).
>
> **Schema Version**: 1.2
> **Created**: 2026-03-16
> **Project**: sdap-secure-project-module
> **Corrected**: 2026-08-20 by the `unified-access-control-r2` investigation — field logical names, expiry field name, organization lookup, and never-built features (expiry worker, Power Pages chain, three-plane revocation) verified against `Api/ExternalAccess/GrantExternalAccessEndpoint.cs` + `Infrastructure/ExternalAccess/ExternalParticipationService.cs`; unbuilt behavior is marked NOT IMPLEMENTED below
> **Corrected 2026-10-07** (`unified-access-control-r2` task 101, against live attribute metadata and `savedqueries` on spaarkedev1):
> - The **Views** section described views that were never applied. See [`views-schema.md`](views-schema.md) "Live views".
> - **`sprk_approvedby` / `sprk_approveddate` do not exist** on this table.
> - **`sprk_name` is not computed.** No write path sets it, so it is empty on every row ([#1394](https://github.com/spaarke-dev/spaarke/issues/1394)).
> - Two lookups exist live that this file did not list:
>   - `sprk_invoice` (→ `sprk_invoice`). Never written, and never read by the evaluator (`ExternalParticipationService.cs:1373`).
>   - `sprk_recordtype` (→ `sprk_recordtype_ref`).

## Entity Definition

| Property | Value |
|----------|-------|
| **Logical Name** | sprk_externalrecordaccess |
| **Display Name** | External Record Access |
| **Plural Display Name** | External Record Accesses |
| **Primary Name Field** | sprk_name |
| **Ownership Type** | Organization |
| **Description** | Tracks external user (Contact) and Organization access grants to Spaarke records (Projects/Matters/Work Assignments). Drives BFF API external authorization. (Power Pages table permission chain: RETIRED.) |

---

## Fields

### Primary Fields

| Logical Name | Display Name | Type | Required | Max Length | Description |
|--------------|--------------|------|----------|------------|-------------|
| sprk_externalrecordaccessid | External Record Access | Uniqueidentifier | Auto | — | Primary key (auto-generated GUID) |
| sprk_name | Name | String | No | 200 | ⚠️ **Never written** (2026-10-07: empty on 92 of 92 dev rows; [#1394](https://github.com/spaarke-dev/spaarke/issues/1394)). The original design meant it to be an auto-generated display name (e.g. "Jane Smith → Acme Litigation"); that was never built |

### Core Lookup Fields

> **Corrected 2026-08-20** — the original `*id`-suffixed logical names were wrong and 400'd every grant. Live names verified via `$metadata` (task 070): schema (write) names are PascalCase `@odata.bind` navigation properties (`GrantExternalAccessEndpoint.cs:290-334`); read form is `_sprk_{name}_value` (`ExternalParticipationService.cs:399-407`).

| Logical Name | Schema Name (write) | Display Name | Type | Required | Target Entity | Description |
|--------------|--------------------|--------------|------|----------|---------------|-------------|
| sprk_contact (read: `_sprk_contact_value`) | `sprk_Contact@odata.bind` | Contact | Lookup | No* | contact | External user granted access. *OMITTED for an Organization grant — a row with no Contact + a bound Organization grants every ACTIVE org member at check time (`GrantExternalAccessEndpoint.cs:303-311`) |
| sprk_project (read: `_sprk_project_value`) | `sprk_Project@odata.bind` | Project | Lookup | One-of | sprk_project | Grant root — exactly ONE typed root lookup is bound per record (project / matter / workassignment; `GrantExternalAccessEndpoint.cs:286-298`) |
| sprk_matter (read: `_sprk_matter_value`) | `sprk_Matter@odata.bind` | Matter | Lookup | One-of | sprk_matter | Grant root (see above) |
| sprk_workassignment (read: `_sprk_workassignment_value`) | `sprk_WorkAssignment@odata.bind` | Work Assignment | Lookup | One-of | sprk_workassignment | Grant root (see above) |
| sprk_organization (read: `_sprk_organization_value`) | `sprk_Organization@odata.bind` | Organization | Lookup | No | sprk_organization | Firm/org association — targets the custom `sprk_organization` table, NOT the OOB `account` (owner steer 2026-08-11; `GrantExternalAccessEndpoint.cs:330-334`). Supersedes the originally documented `sprk_organization` → account |
| sprk_grantedby | `sprk_GrantedBy@odata.bind` | Granted By | Lookup | No | systemuser | Core User who created the grant (audit field — resolved from the caller's AAD oid and OMITTED when unresolvable rather than failing the grant; `GrantExternalAccessEndpoint.cs:199-231`) |
| sprk_grantedbycontact (read: `_sprk_grantedbycontact_value`) | `sprk_GrantedByContact@odata.bind` | Granted By (Contact) | Lookup | No | contact | *Added by unified-access-control-r2 task 140 (#1063).* The CONTACT who issued the grant from the external SPA (contact-side Grant Access, owner C4 / Q2). Written by the BFF only — **field-secured** (`Spaarke BFF-Managed Field Writers` = the BFF application users; `Spaarke BFF-Managed Field Readers` = every business-unit default team, so everyone still reads it). `sprk_grantedby` stays EMPTY on a contact-issued row. A contact may change or revoke only rows whose value is themselves; when an internal user (or the Assigned-To rule) CHANGES a contact-issued row — through the grant core, or through `POST /api/v1/external-access/set-record-share-expiry` (the record-wide Expiration, in the same transaction as the date; session 27 round 34 item 3) — this is cleared and `sprk_grantedby` set to the changing systemuser — the row becomes theirs. A contact's own write to a row it issued is conditional on the row version it read (`If-Match`), so a take-over in between wins and the contact gets 409 `managed_elsewhere` (round 42 item 1). The reconciliation job's R1 is the one writer that changes an active contact-issued row and KEEPS this value (no internal person acts): on a row with no expiry it stamps the earlier of today + 90 and the issuing contact's own grant on the record, or deactivates the row when the issuer holds none there (round 42 item 2). Created by `scripts/Deploy-ExternalRecordAccessContactGrantor.ps1` (dry run / `-Apply` / `-Verify`); relationship `sprk_contact_sprk_externalrecordaccess_grantedbycontact` (NoCascade; Delete RemoveLink — deleting the contact EMPTIES this lookup, which is why `sprk_grantedbycontactid` exists). ⚠️ Deploy order: the column must exist before a BFF carrying task 140 is deployed — the BFF selects it on every grant-row read. |
| sprk_grantedbycontactid | `sprk_grantedbycontactid` | Granted By (Contact) Id | Single Line of Text (100) | No | — | *Added by unified-access-control-r2 task 140, session 27 round 50 item 2.* The issuing contact's id as TEXT (lower-case, hyphenated) — the grant's provenance, which survives the contact's deletion. Set and cleared by the BFF in the SAME write as `sprk_grantedbycontact`, every time: set on a contact grant's create; cleared (with the lookup) when an internal user takes the row over — including a row whose issuing contact was already deleted. So "set while `sprk_grantedbycontact` is empty" means exactly "the issuing contact was deleted": the reconciliation job's **R1** DEACTIVATES such a row when it has no expiry, keeps this value and reports it (`IssuerDeleted`); a dated one stands until its date (owner G2 (ii): no cascade). **Field-secured** with the same two profiles as the lookup (a forged value would end an undated grant; an erased one would let a deleted issuer's grant be stamped +90). Created and BACKFILLED from the lookup by the same script (`-Apply` step f, conditional on each row's version; `-Verify` fails while any row lacks it). ⚠️ Same deploy-order gate: the BFF selects it on every grant-row read. |

### Access Control Fields

| Logical Name | Display Name | Type | Required | Description |
|--------------|--------------|------|----------|-------------|
| sprk_accesslevel | Access Level | Choice | Yes | Determines the BFF's effective rights: View Only / Collaborate / Full Access |
| sprk_granteddate | Granted Date | DateTime (DateOnly) | Yes | Date access was granted (set to UTC now on create; `GrantExternalAccessEndpoint.cs:300`) |
| sprk_expiresdate | Expires Date | DateTime (Format DateOnly, Behavior **TimeZoneIndependent**) | No | The grant's expiry. **Corrected 2026-08-20**: the live field is `sprk_expiresdate`, NOT the originally documented `sprk_expiresdate` (verified live, task 070 — the old name 400'd any grant carrying an expiry). **Enforced on read** since tasks 007/107: a row confers access only while `sprk_expiresdate ≥ today`, and a row with NO date confers nothing (see Business Rules §4 below — the old "NOT ENFORCED anywhere" note here was stale and is removed). Every row the BFF writes carries one (task 097: absent → today + 90). ⚠️ **Wire shape** *(task 140, live 2026-10-05)*: because the behaviour is TimeZoneIndependent, the Web API returns the value as a timestamp — `"2026-12-10T00:00:00Z"` — not `yyyy-MM-dd`; the calendar date is the leading ten characters as written. The BFF reads it through `DataverseDateOnlyJsonConverter` (`ExternalGrantRow.ExpiresDate`); System.Text.Json's own `DateOnly` converter throws on that shape. Every read also returns `@odata.etag` (`W/"<versionnumber>"`), which the contact-side writes send back as `If-Match` (session 27 round 42 item 1). |

### Approval Fields (Document/File Access) — ⚠️ NOT PRESENT LIVE

> **2026-10-07 (task 101)**: neither column exists on `sprk_externalrecordaccess` (live attribute metadata, spaarkedev1). This is the original design only. Do not select either column; Dataverse answers 400 to a `$select` naming an unknown attribute.

| Logical Name | Display Name | Type | Required | Description |
|--------------|--------------|------|----------|-------------|
| sprk_approvedby | Approved By | Lookup → SystemUser | No | Core User who approved document/file access (Plane 2) |
| sprk_approveddate | Approved Date | DateTime (DateOnly) | No | Date document/file access was approved |

### System Fields

| Logical Name | Display Name | Type | Description |
|--------------|--------------|------|-------------|
| statecode | Status | State | Active (0) / Inactive (1) — deactivating this record revokes the grant (the BFF's participation query reads `statecode eq 0` only; `ExternalParticipationService.cs:406`) |
| statuscode | Status Reason | Status | Active: Active (1) / Inactive: Inactive (2) |
| createdon | Created On | DateTime | Record creation timestamp |
| modifiedon | Modified On | DateTime | Last modification timestamp |
| createdby | Created By | Lookup → SystemUser | User who created the record |
| modifiedby | Modified By | Lookup → SystemUser | User who last modified the record |

---

## Choice Values

### sprk_accesslevel (Access Level)

> **Corrected 2026-08-20** — the original Plane 2/Plane 3 columns described NOT-IMPLEMENTED behavior (no SPE container role is ever assigned — external SPE access is broker-only per ADR-028 A1 — and no external AI Search filter exists). The actual enforcement is the BFF effective-rights mapping (`Infrastructure/ExternalAccess/CallerPrincipalResolver.cs:126-134`):

| Value | Label | Effective `AccessRights` (BFF) | SPE / AI Search |
|-------|-------|-------------------------------|-----------------|
| 100000000 | View Only | Read | NOT IMPLEMENTED (broker-only; no search plane) |
| 100000001 | Collaborate | Read + Create + Write | NOT IMPLEMENTED |
| 100000002 | Full Access | Read + Create + Write + Delete | NOT IMPLEMENTED |

---

## Form Layout

> **Note (2026-08-20)**: field references below corrected to the live logical names (`sprk_contact`, `sprk_project`, `sprk_organization`, `sprk_expiresdate`); the layout itself is the original design and has not been re-verified against the deployed form.

### Main Form: Information

**Header Section**
- sprk_name (External Record Access — computed)
- sprk_contact (Contact)
- sprk_project (Project)
- sprk_accesslevel (Access Level)
- statecode (Status)

**Access Details Section**
- sprk_organization (Organization)
- sprk_matter (Matter)
- sprk_grantedby (Granted By)
- sprk_granteddate (Granted Date)
- sprk_expiresdate (Expires Date)

**File Access Approval Section**
- ~~sprk_approvedby (Approved By)~~ (not on the table; see Approval Fields)
- ~~sprk_approveddate (Approved Date)~~

**System Section**
- createdon, modifiedon, createdby, modifiedby

---

## Views

> **⚠️ Never applied (verified 2026-10-07, task 101).** None of the four views below exists as described:
> - The live default is "Active External Record Accesses". Its columns are Contact, Name, Record Type, Access Level, Expires Date and Created On, and it sorts by Name.
> - "Access by Project" does not exist.
> - "Expiring Access" does not exist, and the filter given for it ("≤ next 30 days") is not one FetchXML can express. Task 101's "External Shares Expiring in 30 Days" replaces it.
>
> The live views and the two task-101 expiry views are in [`views-schema.md`](views-schema.md) "Live views". What follows is the original design record only.

### Active External Record Access (Default View) — design, not live

| Column | Width | Sort |
|--------|-------|------|
| sprk_name | 200 | 1 (ASC) |
| sprk_contact | 150 | — |
| sprk_project | 150 | — |
| sprk_accesslevel | 120 | — |
| sprk_granteddate | 110 | — |
| sprk_expiresdate | 110 | — |
| sprk_grantedby | 150 | — |

**Filter**: statecode = Active

### All External Record Access

Same columns as above, no filter.

### Access by Project (Subgrid View) — design, not live

| Column | Width | Sort |
|--------|-------|------|
| sprk_contact | 180 | 1 (ASC) |
| sprk_accesslevel | 120 | — |
| sprk_organization | 150 | — |
| sprk_granteddate | 110 | — |
| sprk_expiresdate | 110 | — |
| sprk_grantedby | 150 | — |

**Filter**: statecode = Active
**Default View Name**: Access by Project
**Used On**: sprk_project form — External Participants subgrid

### Expiring Access (System View) — design, not live; superseded by task 101

| Column | Width | Sort |
|--------|-------|------|
| sprk_name | 200 | — |
| sprk_contact | 150 | — |
| sprk_project | 150 | — |
| sprk_accesslevel | 120 | — |
| sprk_expiresdate | 110 | 1 (ASC) |

**Filter**: statecode = Active AND sprk_expiresdate ≤ [next 30 days]

---

## Relationships

### N:1 Relationships (Lookups — this table references)

> **Note (2026-08-20)**: relationship schema names below are the ORIGINAL design names and have NOT been re-verified against live metadata (the live field logical names differ from the design — see Core Lookup Fields). "This Field" column corrected; the organization lookup targets `sprk_organization`, not `account`. A `sprk_workassignment` root lookup also exists live (task 028) and is not in this original list.

| Relationship (design name — unverified) | This Field | Parent Table | Behavior |
|-------------|-----------|--------------|----------|
| sprk_externalrecordaccess_contactid_contact | sprk_contact | contact | Restrict (do not delete Contact if active access records exist) |
| sprk_externalrecordaccess_projectid_sprk_project | sprk_project | sprk_project | Cascade — deactivate all access when project is deactivated |
| sprk_externalrecordaccess_matterid_sprk_matter | sprk_matter | sprk_matter | Cascade — deactivate all access when matter is deactivated |
| (organization lookup, added task 070) | sprk_organization | sprk_organization | Referential (no cascade) |
| sprk_externalrecordaccess_grantedby_systemuser | sprk_grantedby | systemuser | Referential (no cascade) |
| sprk_contact_sprk_externalrecordaccess_grantedbycontact *(task 140; name set by the schema script)* | sprk_grantedbycontact | contact | Referential — NoCascade for Assign/Share/Unshare/Reparent/Merge, Delete RemoveLink |
| ~~sprk_externalrecordaccess_approvedby_systemuser~~ | ~~sprk_approvedby~~ | systemuser | NOT PRESENT live (2026-10-07): the column does not exist |

---

## Security Roles

| Role | Create | Read | Write | Delete |
|------|--------|------|-------|--------|
| System Administrator | Yes | Yes | Yes | Yes |
| System Customizer | Yes | Yes | Yes | Yes |
| SDAP Admin | Yes | Yes | Yes | Yes |
| SDAP User (Core) | Yes | Yes | Yes | No |
| Basic User | No | No | No | No |

**Note**: External Contacts never read this table directly — the BFF reads it app-only on their behalf (broker-only, ADR-028 A1). The original "Power Pages table permissions (Contact scope)" model is RETIRED.

### ⚠️ `SDAP User (Core)` → `Create` is NO LONGER REQUIRED BY ANY SPAARKE CODE *(task 118, 2026-09-21)*

The table above records the CURRENT state of the roles, which is unchanged. What changed is that nothing in the product depends on the `Create` cell any more, so it is now a candidate for removal:

- **What used to depend on it.** The Manage Access affordance in the `TrackingFieldTrio` PCF gated on `context.utils.hasEntityPrivilege('sprk_externalrecordaccess', Create, Global)` — a table-level privilege used as a proxy for "may this person grant access". Task 118 (owner decision D-1 option C) re-pointed it onto the rule the server actually enforces: **Write on the target record**, asked via `GET /api/v1/external-access/can-manage-access` and enforced by `DelegationRuleFilter`.
- **What the privilege never did.** It never authorized a write. Every grant row is written by the BFF **app-only**, behind that delegation filter. Removing `Create` from a human role therefore removes no capability the product uses.
- **Verified 2026-09-21** (task 118 escalation trigger 1): `hasEntityPrivilege` has **zero** call sites left in the repo; `src/dataverse/**` does not reference this table; no solution XML, ribbon rule or flow definition in the repo references it; and no client code calls `createRecord` on it. The only remaining client references are **reads**.
- 🔴 **Ordering is binding, and the wrong order is silent.** The code above must be **DEPLOYED** before `Create` is removed from any human role. Reversed, the old client still gates on the privilege, `computeCanGrantAccess` returns `false` for every user, and Manage Access disappears for everybody — while the BFF's app-only writes keep succeeding, so nothing fails loudly.
- **What removal DOES change** — deliberately, and see business rule 4: a person can no longer hand-create a grant row through a form, the Web API, a flow or an import. That is the path by which undated rows (which, post-task-107, confer nothing) got into the table in the first place.
- **The operator step** is in [`projects/unified-access-control-r2/notes/task-118-manage-access-gate-write-on-record.md`](../../../../../projects/unified-access-control-r2/notes/task-118-manage-access-gate-write-on-record.md). Task 118 deliberately performs **no** role change: `SDAP User (Core)` exists only in Dataverse, not in any file in this repo.

---

## Power Pages Table Permission Configuration — ⚠️ RETIRED / NOT IMPLEMENTED

> **Corrected 2026-08-20**: Power Pages is retired. External access is served by Static Web Apps + Entra External ID (CIAM); the BFF resolves the caller's Contact and reads this table app-only (`Infrastructure/ExternalAccess/CallerPrincipalResolver.cs`, `ExternalParticipationService.cs`). No Power Pages table permission chain or web role exists. The original design is preserved below for lineage only.

```
(retired design)
Level 0: sprk_externalrecordaccess
         Scope: Contact
         Relationship: sprk_contact
         CRUD: Read only
         Web Role: "Secure Project Participant"
         → Unlocks parent chain to sprk_project and its children
```

---

## Business Rules

1. **Unique participation grant**: Only one active record per (Contact, Project) pair. If a second grant is attempted, update the existing one instead.

2. **Computed name — ⚠️ THE PRESCRIPTION IS RETIRED AND NOTHING REPLACED IT** *(corrected 2026-09-21 by task 118)*.

   This rule used to read: *"Auto-generate `sprk_name` as `{ContactFullName} → {ProjectName}` via pre-create plugin (thin validation only, per ADR-002)."* Two separate things are wrong with it.

   - **The mechanism is banned.** Owner decision D-1 (2026-09-19) ruled Dataverse plugins out **repo-wide**; `src/dataverse/**` contains no code for this table and none may be added. This was the last doc in the repo still prescribing a plugin here — and a plugin is exactly the shape a reader reaches for on noticing a column that something must populate. (The same decision is why task 107 closed the undated-grant hole from the **read** side and task 117 with a **scheduled job**, rather than with a Dataverse-side default.)
   - **Nothing composes the name.** Verified 2026-09-21: `GrantExternalAccessEndpoint.BuildGrantPayload` sets the root `@odata.bind`, `sprk_accesslevel`, `sprk_granteddate`, and optionally `sprk_Contact`, `sprk_GrantedBy` and `sprk_expiresdate` — **not `sprk_name`**. No other writer in the repo sets it either. So rows are created without a composed display name, and this rule has described an intention rather than a behaviour for as long as it has existed.

   **Impact is cosmetic, which is why it survived**: `sprk_name` is the primary name column, so it is what an MDA grid, lookup or audit entry shows for a grant row. No code path reads it — the BFF resolves grants by lookup, never by name. Recorded rather than fixed because composing it is a write-path change outside task 118's scope; it belongs with whoever next touches `BuildGrantPayload`.

   *(Also corrected from live metadata, 2026-09-21: `sprk_name` is `NVARCHAR(850)`, not the 200 this document states in its field table and index tables above.)*

3. **Deactivation = revocation** *(corrected 2026-08-20 — the original "full three-plane revocation" was never built)*: Setting statecode = Inactive revokes the grant because the BFF's participation query only reads Active rows (`ExternalParticipationService.cs:406`). The revoke endpoint deactivates the row + invalidates the Redis participation cache, plus a defensive SPE permission cleanup only when a `ContainerId` is supplied (`Api/ExternalAccess/RevokeExternalAccessEndpoint.cs:93-147`). There is no web-role removal (Power Pages retired) and no search-filter exclusion (no external search plane exists).

4. **Expiry enforcement — IMPLEMENTED, read-side, fail-CLOSED on a missing date** *(corrected 2026-09-21 by task 107, ISS-009 / #974 — this entry was stale since task 007)*: There is still no scheduled worker that deactivates expired rows; `statecode` is untouched by expiry. Enforcement is instead server-side on every conferring read: `ExternalParticipationService.ExpiryPredicate` excludes any row whose `sprk_expiresdate` is in the past **or absent** (task 007 closed "in the past"; task 107 closed "absent", per owner decision D-1, 2026-09-19 — "we do not use Dataverse plugins", so the gap left by non-BFF writers omitting the column is closed from the read side, not by a Dataverse-side default). A row with no `sprk_expiresdate` therefore confers **NOTHING**, not "forever" — undated is fail-CLOSED. The BFF itself never writes an undated grant (task 097, FR-33: absent expiry defaults to today + 90 days), so this matters only for rows created outside the BFF (a form, the Web API, a flow, or an import — all reachable because users hold Create on this table). ⚠️ *Task 118 note (2026-09-21): that `Create` privilege is now a removal candidate — see the Security Roles section. Removing it closes this ingress rather than weakening the guard, so the read-side predicate stays exactly as task 107 left it; it simply has fewer undated rows to catch.* The in-memory mirror `ExternalParticipationService.ConfersAccessOn` answers the identical question for the write path (`/grant`, `/set-record-share-expiry`) and is pinned to agree with the read predicate by `GrantExpiryCharacterizationTests.ExpiryPredicateAndConfersAccessOn_AgreeOnTheNullCase`. See [`docs/guides/EXTERNAL-ACCESS-ADMIN-SETUP.md` §4.2a](../../../../../docs/guides/EXTERNAL-ACCESS-ADMIN-SETUP.md) for the pre-deploy operator COUNT this change requires.

---

## BFF API Integration

This table is queried by:

| BFF Component | Query | Purpose |
|--------------|-------|---------|
| `ExternalParticipationService` (via `CallerPrincipalAuthorizationFilter` / `CallerPrincipalResolver`) | Active records where `_sprk_contact_value` = resolved Contact (plus org-grant rows with no Contact for the caller's active orgs) | Determine the caller's grant set (projects/matters/work assignments) |
| `GrantExternalAccessEndpoint` | Create new record + invalidate participation cache | Grant external access (ONE Dataverse row — no SPE/web-role/search orchestration; broker-only) |
| `RevokeExternalAccessEndpoint` | Deactivate record by ID + invalidate cache (+ defensive SPE cleanup when `ContainerId` supplied) | Revoke external access |
| `ProjectClosureEndpoint` | Deactivate all records for project | Cascade revocation on project close |
| `SetRecordShareExpiryEndpoint` | ONE `ExecuteTransactionRequest` (SDK `BulkUpdateAsync`, logical names) over every active row of the record | The record-wide Expiration (FR-33). A contact-issued row is taken over in the same write: `sprk_grantedbycontact` and `sprk_grantedbycontactid` cleared, `sprk_grantedby` = the caller's systemuser *(task 140; session 27 rounds 34 item 3 and 50 item 2 — a row whose issuing contact was deleted is taken over too)* |
| `ExternalAccessReconciliationJob` *(task 117; scheduled, report-only until `ExternalAccess:Reconciliation:WritesEnabled`)* | One FetchXML scan of active rows with no expiry or with an organization; SDK `BulkUpdateAsync` in chunks | R1 stamps today + 90 on an active row with no expiry — on a CONTACT-issued row, the EARLIER of that and the issuing contact's own grant on the record (or the row is deactivated when the issuer holds none there), keeping `sprk_grantedbycontact` *(task 140; session 27 round 42 item 2)*; an undated row whose issuing contact was DELETED (`sprk_grantedbycontactid` set, the lookup empty) is deactivated and reported *(round 50 item 2)*; R2 deactivates a row whose organization is inactive |
| `ExternalUserContextEndpoint` | Resolved principal's grant set | Return user's project membership to SPA |
| `ContactGrantEndpoints` *(task 140)* — `POST/GET /api/v1/external/contact-grants`, `POST …/contact-grants/revoke` | Through the grant core in contact-issuer mode; the list reads active rows where `_sprk_grantedbycontact_value` = the caller | A Collaborate/Full Access contact grants colleagues of its own organization (capped at its level and its own expiry), lists and revokes the grants it issued. Never writes `sprk_contactorganization`. |

**Redis Cache Key** *(corrected 2026-08-20; version updated 2026-09-30)*: tenant-scoped `ITenantCache` entry — resource `external-access-grant`, contact-id component, version 5 (`ExternalParticipationService.CacheVersion`), 60s TTL (per ADR-009). Each cached grant holds the record id, the effective `sprk_accesslevel` and the direct-only level. The old flat `sdap:external:access:{contactId}` key is no longer accurate.

---

## Deployment

Add to **SpaarkeCore** solution. Deploy via PAC CLI:

```bash
# Pack and import solution
pac solution pack --folder ./src/solutions/SpaarkeCore --zipfile SpaarkeCore.zip --managed false
pac solution import --path SpaarkeCore.zip --force-overwrite
```

---

*Schema version: 1.2 | Created: 2026-03-16 | Project: sdap-secure-project-module | Corrected: 2026-08-20 by `unified-access-control-r2` (field names, org lookup, expiry field + unenforced expiry, retired Power Pages model, actual grant/revoke behavior); 2026-10-07 by task 101 (views never applied, approval fields absent, sprk_name never written, sprk_invoice / sprk_recordtype listed)*
