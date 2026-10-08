# sprk_noaccessentry Entity Schema

> **Entity Purpose**: The FR-23 deny-list store — the ethical-wall and per-child-revocation mechanism.
> Each row is a **veto**, never a level: a matching active entry means the subject has **no access** to
> the object, full stop. This table is read by the BFF's `NoAccessListReader` (fail-closed) and feeds
> Slot 1 of `AccessibleRecordSetService.ApplyVetoPipeline` (wired by task 039 — this task delivers the
> store + reader only, per spec FR-23 / Placement table row 2: "Deny-list store | none found | New").
>
> **Schema Version**: 1.1 (task 143: a third subject, `sprk_subjectsystemuser`)
> **Created**: 2026-09-04 · **Updated**: 2026-10-07 (task 154)
> **Project**: `unified-access-control-r2` (task 038; task 143; task 154)
>
> **Task 143's `sprk_subjectsystemuser` is live** (read from spaarkedev1 metadata on 2026-10-07 by task 154). In a new
> environment it is created by `scripts/Set-NoAccessSystemUserSubjectSchema.ps1 -Apply` and verified with `-Verify`
> (exit 0) **BEFORE** a BFF carrying task 143 is deployed: that BFF selects `_sprk_subjectsystemuser_value` on every No
> Access read, and without the column every read 400s and fails CLOSED (every queried record denied, on the contact AND
> systemuser planes). See *Deployment → Task 143*.
>
> **The model-driven surface** (entry form and its library, the view, the NO ACCESS subgrids on the Organization and
> Contact forms, the site map entry, the access-administrator role) is in [`views-forms-schema.md`](views-forms-schema.md)
> (task 154).
> **Status**: ✅ **DEPLOYED**. This document and the live `spaarkedev1.crm.dynamics.com` environment are
> IN SYNC as of 2026-09-04 — every field below was applied to that environment on that date and then
> independently re-verified via `mcp__dataverse__describe('tables/sprk_noaccessentry')` (see Deployment
> section for the exact mechanism and a tooling gap it surfaced). A fresh environment (a new customer
> stamp, a rebuilt dev org, etc.) does NOT have this table until someone runs the equivalent creation
> steps there — this schema doc is not itself a deployment artifact for a new environment, only the
> record of what was done to this one.

---

## Entity Definition

| Property | Value |
|----------|-------|
| **Logical Name** | sprk_noaccessentry |
| **Collection Name** (plural, read via Web API) | sprk_noaccessentries |
| **Display Name** | No Access Entry |
| **Plural Display Name** | No Access Entries |
| **Primary Name Field** | sprk_name |
| **Ownership Type** | Organization |
| **Description** | Deny-list entry (FR-23): vetoes access for a subject (contact, organization or — since task 143 — internal user; exactly one populated) against an object (an organization reference — ethical wall — or a specific polymorphic record — per-child revocation; exactly one populated). A veto is never a level: a matching active entry REMOVES the subject's access rather than writing a low access value. |

**Why Organization-owned, not User-owned**: mirrors `sprk_externalrecordaccess` (the sibling grant table).
A deny entry is an administrative/governance record, not something a single user "owns" in the
security-role sense; Organization ownership avoids an unrelated owner-reassignment workflow for a
row nobody should be routinely reassigning.

---

## Fields

### Primary Field

| Logical Name | Display Name | Type | Required | Max Length | Description |
|--------------|--------------|------|----------|------------|--------------|
| sprk_noaccessentryid | No Access Entry | Uniqueidentifier | Auto | — | Primary key (auto-generated GUID) |
| sprk_name | Name | String | None (live metadata 2026-10-07; this row said "Application Required" before) | 850 (platform-widened from the 200 requested — see note) | Short human description of the entry, e.g. "Acme Corp — Ethical Wall" or "Jane Doe — Matter 12345 revoke". No plugin names it. Since task 154 the entry form suggests "{subject} – {object}" while the field is empty, and the user can change it. Views and subgrids show it, so it is also how a record object is recognised (the id column holds only the GUID). |

> **Note on MaxLength 850**: the create call requested `MaxLength=200`; Dataverse returned `NVARCHAR(850)`
> on the primary name attribute specifically (verified live). This is a known platform behavior for
> primary-name (`IsPrimaryName=true`) string attributes on some environments/versions and is **not** a
> data-entry risk (850 is more permissive than requested, not less) — documented here so a future reader
> is not surprised the live column doesn't match the literal request.

### Subject Fields — exactly ONE of the THREE populated (task 143)

| Logical Name | Display Name | Type | Target Entity | Description |
|--------------|--------------|------|----------------|-------------|
| sprk_subjectsystemuser | Subject User | Lookup | systemuser | **Task 143 (owner Q4, N1) — pending live gate G-1.** The denied INTERNAL user, named directly. Binds on SECURE records only (Q4): the user's direct POA shares on covered secure records are refused at write time and removed by the enforcer, and the record is hidden from that user on Teams/SPA. On a non-secure record a user-subject entry does nothing. A contact or organization entry ALSO binds an internal user — through every contact that represents them (the task-141 link `systemuser.sprk_primarycontact`, or a contact bound to their Entra oid) and those contacts' organizations — so a direct user subject is what makes the wall independent of the link (only 1 of 8 dev users was linked, session 27). |
| sprk_subjectcontact | Subject Contact | Lookup | contact | The denied contact. |
| sprk_subjectorganization | Subject Organization | Lookup | sprk_organization | The denied organization — denies every contact who is an ACTIVE member — `statecode`-active, with NO date or organization-state bound, so a member whose membership ended by date or has not yet started is still denied (owner D-2 part 2 / D-10). The caller resolves membership via the same `sprk_contactorganization` junction read `ExternalParticipationService.ReadOrganizationMembershipsAsync` performs (its `WallSubjectOrganizationIds` set, task 109); the reader is agnostic to how membership was resolved — see `<notes>`. |

### Object Fields — exactly ONE of {Object Organization} or {Object Record Type + Object Record Id} populated

| Logical Name | Display Name | Type | Target Entity | Description |
|--------------|--------------|------|----------------|-------------|
| sprk_objectorganization | Object Organization | Lookup | sprk_organization | **Ethical wall.** Denies every candidate record whose referenced-organization set contains this organization — **ANY** reference, including a non-conferring one (e.g. opposing counsel referenced on a matter). The over-match is the specified behavior (spec FR-23 / register B-10), not a bug. |
| sprk_objectrecordtype | Object Record Type | Lookup | sprk_recordtype_ref | **Per-child revocation.** The entity type of the specific denied record — the ADR-024 resolver-pair pattern (type + id), deliberately **without** an entity-specific lookup per possible type (the task's own constraint: "do NOT create one lookup per possible entity type" — the object here can be ANY record type, unlike the small closed parent-type sets ADR-024's dual-field strategy targets). |
| sprk_objectrecordid | Object Record Id | String(50) | — | GUID (as text) of the specific denied record. Paired with `sprk_objectrecordtype`. **Matching is by id alone** — `NoAccessListReader` does not additionally require the entity-logical-name to match, because Dataverse record ids are effectively globally unique (random v4 GUIDs assigned per row); the recordtype lookup exists for provenance/audit legibility, not as a second matching key. **Canonical form required (task 154):** the readers match this text by string equality against the lowercase hyphenated id, so a value with braces, in the 32-digit form, or with a leading space matches nothing. Such a row is MALFORMED (Business Rule 1). The entry form normalises the id before saving (and flags a stored non-canonical one on load, without rewriting an existing entry), and the record picker writes it in canonical form. The rule (`NoAccessListReader.TryParseObjectRecordId` / `FoldLikeDataverse`) accepts exactly what Dataverse's own comparison matches, as measured live on 2026-10-07: case, full-width forms, U+FEFF and the combining marks U+0300-U+036F are ignored, and trailing U+0020 / U+3000 are padding; every other combining mark (e.g. Arabic U+064B, Thai U+0E31), a leading space, braces, a trailing tab, NBSP, em space, LF or zero-width space are significant, so such a row is malformed. |

### Governance Field

| Logical Name | Display Name | Type | Required | Max Length | Description |
|--------------|--------------|------|----------|------------|-------------|
| sprk_reason | Reason | Memo (Multiline Text) | No | 2000 | Free-text rationale for the deny entry (audit/governance context). **Not enforced at schema level** — recommended, not required, so an urgent ethical-wall entry is never blocked on prose. |

### System Fields

| Logical Name | Display Name | Type | Description |
|--------------|--------------|------|-------------|
| statecode | Status | State | Active (0) / Inactive (1). **Deactivating a row lifts the veto** — `NoAccessListReader` reads active entries only (`statecode eq 0`), mirroring `ContactStandingGrantReader` / `ExternalParticipationService`'s convention. |
| statuscode | Status Reason | Status | Active: Active (1) / Inactive: Inactive (2) |
| createdon / modifiedon / createdby / modifiedby | — | DateTime / Lookup → systemuser | Standard audit fields |

**No SPE/AI-search columns**: unlike `sprk_externalrecordaccess`, this table confers nothing — it is a pure
veto and has no effective-rights mapping, no container role, no search-filter surface. There is nothing to
map to "None" because absence (a removed key) is the only representation of denial (root CLAUDE.md §5 fact 5;
`AccessibleRecordSet.Rights` doc comment).

---

## The Four Key Combinations (worked examples)

| # | Subject | Object | Scenario | Effect |
|---|---------|--------|----------|--------|
| 1 | `sprk_subjectcontact` = Jane Doe | `sprk_objectorganization` = Acme Corp | **Ethical wall.** Jane Doe (opposing counsel's own attorney, now conflicted off a matter) is on the No Access List for Acme Corp. | Every record that references Acme Corp in ANY organization slot — even a matter where Acme Corp is merely "opposing counsel", not the client — is denied to Jane, regardless of any Full Access grant she holds. |
| 2 | `sprk_subjectcontact` = Jane Doe | `sprk_objectrecordtype`/`sprk_objectrecordid` = (sprk_communication, `{guid}`) | **Per-child revocation: NOT ENFORCED ANYWHERE (known limit, owner round 59 item 7).** The intent was to wall one privileged email off from Jane while she keeps Project 1. | The row is stored, but no deny reader evaluates a child record: deny candidates are built only for the three roots (project, matter, work assignment; `AccessibleRecordSetService`, `NoAccessShareEnforcer.SecureRootTypes`). Children inherit their root's access through the core-ancestor stamp, so the email stays visible to Jane. The entry form refuses a record type other than the three roots (task 154). |
| 3 | `sprk_subjectorganization` = Beta LLP | `sprk_objectorganization` = Acme Corp | **Firm-wide ethical wall.** Every contact who is an active member of Beta LLP (per `sprk_contactorganization`) is denied on any record referencing Acme Corp. | Widest-blast-radius combination — denies an entire firm's roster, not one person. Used when the conflict is at the firm level, not the individual attorney level. |
| 4 | `sprk_subjectorganization` = Beta LLP | `sprk_objectrecordtype`/`sprk_objectrecordid` = (sprk_matter, `{guid}`) | **Firm-wide per-record revocation.** No Beta LLP member may see this one matter, even though the firm otherwise has standing access elsewhere. | Only the named record is denied to every Beta LLP member. |
| 5 | `sprk_subjectsystemuser` = Pat Attorney (an internal user) | `sprk_objectrecordtype`/`sprk_objectrecordid` = (sprk_project, `{guid}` of a SECURE project) | **Internal ethical wall (task 143).** An internal attorney conflicted off one secure project. | `/share-user` and secure provisioning refuse a share to Pat on that project; Pat's existing direct share is removed on save (if the entry's author holds Write on the project) and by the 5-minute job; the project is hidden from Pat on Teams/SPA. Access Pat holds through a team, a role or the business unit is REPORTED as "not enforceable in Dataverse", never revoked (owner N2). On a non-secure project the entry does nothing (Q4). |

---

## Business Rules

1. **Exactly-one-subject (of THREE, since task 143) / exactly-one-object are NOT enforced at the schema level.** No
   pre-create plugin exists for this table (ADR-002: no plugins). `NoAccessListReader` defends against a malformed row
   (none, or more than one, of the three subject fields populated; neither or both object fields populated; a record
   type without a record id or the reverse; since task 154, a record id not in the canonical form,
   `NoAccessListReader.TryParseObjectRecordId`) by **logging a warning and excluding that row from
   matching** — a malformed row denies nothing, rather than either being silently ignored without a trace
   or (the more dangerous alternative) denying an unbounded set because its intended scope is unknowable.
   This is a data-quality guard, distinct from the NFR-01 fail-closed behavior for a **faulted read** (see
   the reader's XML doc comments for the reasoning split). `NoAccessShareEnforcer` applies the SAME rule
   (`SubjectKindOf`, `TryParseObjectRecordId`), so the enforce route answers 422 `sdap.access.no_access.entry_malformed`
   and the 5-minute job logs the entry, instead of removing shares for a wall the read-time veto never matches. The
   entry form's OnSave check refuses every one of these shapes before the save. It is a preview: these server rules
   own the shape (write-path registry, `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md` section 5).
2. **Deactivation = veto lifted — but removed shares are NOT re-created.** Setting `statecode = Inactive` ends the
   denial — mirrors `sprk_externalrecordaccess`'s "deactivation = revocation" convention exactly. Since task 143 an
   active entry on a secure record also REMOVES the walled users' direct shares; deactivating the entry does not put
   them back. Removal is not reversible by design: access returns only through a deliberate re-grant (Manage Access
   "+ User"). A task-142 Assigned-To auto share is the exception — 142's own invariant restores it.
3. **No expiry field.** Unlike `sprk_externalrecordaccess.sprk_expiresdate` (which the design register
   flags as write-only/unenforced — finding A-5), this table deliberately has **no** expiry column: an
   ethical wall or a per-child revocation is not the kind of grant that should silently lapse. Time-boxed
   denials, if ever needed, are a future extension — not a gap this task leaves open, a decision this task
   makes.
4. **A veto row is never read as "somewhat denied."** There is no partial-strength deny; a matching active
   row denies fully, and the calling evaluator (task 039) is expected to **remove** the candidate record's
   key from the composed rights map — never write a low `AccessRights` value (root CLAUDE.md §5 fact 5;
   binding rule 1 of this task).
5. **An entry is destructive on secure records, so its author's authority counts (task 143, owner N5).** The enforcer
   removes shares on a record only when the entry's LAST MODIFIER (`modifiedby`) is an enabled person holding Write on
   that record; otherwise it reports "not enforced: author lacks Write on record" and removes nothing there (the
   read-time veto still applies). Who may author entries at all is owner O2 (accepted 2026-10-01): the
   "Spaarke Access Administrator" role holds Create/Read/Write/Append/AppendTo (no Delete), and "Spaarke Core User" loses
   its Global Read, so the Reason is no longer readable by every core user. Applied by
   `scripts/Set-NoAccessEntryRolePrivileges.ps1` (task 154); see [`views-forms-schema.md`](views-forms-schema.md).
6. **Never the last person (owner S5).** Enforcement never removes the last enabled person with a share that can read a
   secure record; it keeps that share and reports the record.

---

## How No Access works at the organization level (owner round 3b question, answered by task 154)

`sprk_organization` carries no No Access column of its own. An organization takes part in two ways, through this
table's two organization lookups, and both are managed in the Organization form's NO ACCESS section.

1. **The organization as the SUBJECT ("This organization denied", `sprk_subjectorganization`).** Every contact who is an
   ACTIVE member of the organization (an active `sprk_contactorganization` row, with no date or organization-state
   bound; owner D-2 / D-10) is denied the entry's object. Since task 143 it also reaches internal users: a systemuser
   linked to such a contact (`systemuser.sprk_primarycontact`, or a contact bound to the user's Entra oid) is checked
   with that contact's organizations, so the user is denied too.
2. **The organization as the OBJECT ("Ethical walls on this organization", `sprk_objectorganization`).** The subject is
   denied every project, matter and work assignment that references the organization in any organization lookup,
   including a non-conferring one such as opposing counsel (spec FR-23 / register B-10). Today the referencing lookups
   are `sprk_assignedlawfirm1` and `sprk_assignedlawfirm2` (`ExternalParticipationService`). Children (documents,
   events, to-dos, communications, invoices, analyses) follow their root through the core-ancestor stamp; a child is
   never an object on its own (example 2 above).

How it takes effect:
- **The read-time veto, on the Teams/SPA planes.** Every read the BFF serves removes a denied record outright, whatever
  grant or membership would otherwise reach it (`AccessibleRecordSetService.ApplyVetoPipeline` slot 1). Deactivating the
  entry lifts the veto on the next request (no cache).
- **Enforcement on SECURE records, per covered record (owner N5).** When an entry is saved, and every 5 minutes after,
  the enforcer removes the walled users' DIRECT shares on each covered secure record, but only on records where the
  entry's author (its last modifier) holds Write. On every other covered record the entry is "not enforced": the
  read-time veto still hides the record on Teams/SPA, but the model-driven app keeps the share. For an organization
  wall this is decided record by record, and the post-save notice names each record it was not enforced on. Access
  through a team, a role or the business unit is reported, never revoked (N2), and the last person who can open a
  secure record keeps access (S5).
- **Write-time refusal.** A new share to a walled user on a covered secure record is refused (`SecureShareNoAccessGuard`).

---

## Relationships

### N:1 Relationships (Lookups — this table references)

| Logical Name | This Field | Parent Table | Delete Behavior |
|-------------|-----------|--------------|------------------|
| sprk_systemuser_sprk_noaccessentry_subjectsystemuser | sprk_subjectsystemuser | systemuser | RemoveLink (task 143; same reasoning — deleting a user clears the lookup, the row becomes malformed and denies nothing). Nothing else cascades. **Pending live gate G-1.** |
| sprk_contact_sprk_noaccessentry_subjectcontact | sprk_subjectcontact | contact | RemoveLink — deleting the Contact clears the lookup rather than blocking the delete or cascading. A no-longer-populated subject makes the row malformed (see Business Rule 1), which the reader treats as "denies nothing," not as an error. Deliberately **not** Restrict: a security-adjacent list should not be able to block an unrelated contact-deletion workflow elsewhere in the system. |
| sprk_sprk_organization_sprk_noaccessentry_subjectorganization | sprk_subjectorganization | sprk_organization | RemoveLink (same reasoning) |
| sprk_sprk_organization_sprk_noaccessentry_objectorganization | sprk_objectorganization | sprk_organization | RemoveLink (same reasoning) |
| sprk_sprk_recordtype_ref_sprk_noaccessentry_objectrecordtype | sprk_objectrecordtype | sprk_recordtype_ref | RemoveLink (same reasoning) |

> **Cosmetic note**: the relationship **SchemaNames** above carry a doubled `sprk_` segment (e.g.
> `sprk_sprk_organization_...`) because the deployment script's naming formula prepends `sprk_` to a
> `ReferencedEntity` that is itself already `sprk_organization`/`sprk_recordtype_ref`-prefixed. This is
> **cosmetic only** — relationship SchemaNames are not read by any application code (only the attribute
> **logical names**, e.g. `sprk_subjectorganization`, are consumed by `NoAccessListReader`, and those are
> correctly named with no double prefix). Left as-is rather than risk a rename operation on a
> freshly-created relationship for a purely cosmetic fix.

---

## BFF API Integration

| BFF Component | Query | Purpose |
|--------------|-------|---------|
| `NoAccessListReader` (`Infrastructure/ExternalAccess/NoAccessListReader.cs`) | Active (`statecode eq 0`) rows matching (subject = the caller's contact id OR any of the caller's active organization ids) AND (object organization ∈ candidate referenced-org ids OR object record id ∈ candidate record ids) | Answers "which of these candidate records are denied for this principal's identities" — bounded, batched, chunked per NFR-02. Fail-closed per NFR-01: a faulted read denies every candidate queried in the failed chunk, never an empty ("nobody denied") result. |
| `AccessibleRecordSetService.ApplyVetoPipeline` Slot 1 (task 039, **not this task**) | Consumes `NoAccessListReader`'s denied-id set and **removes** those keys from the composed rights map | The wiring point. Runs FIRST in the veto pipeline (before Restricted), so a denied record can never be "downgraded" into a survivable Restricted outcome. |
| `AccessibleRecordSetService` — systemuser plane (task 143) | The three subjects (the user, its linked contact, that contact's organizations), with the subject kind of each denial | SECURE record (or flags unreadable): any matching entry removes the record whole, membership included (Q4, N2). NON-secure record: a user-subject entry removes nothing; a contact/organization entry removes only the linked contact's contribution (owner N3). A fault removes the candidate. |
| `SecureShareNoAccessGuard` (task 143) | The same three subjects × the record and its referenced organizations | The ONE write-time check: `/share-user` (403 `sdap.access.user_share.subject_no_access`) and secure provisioning (creator refused before the move; walled colleagues skipped) ask it before any share write. Fails closed. |
| `NoAccessShareEnforcer` + `POST /api/v1/external-access/no-access/enforce` + `NoAccessShareReconciliationJob` (task 143) | One entry by id (`statecode`, subjects, objects, `modifiedby`); the secure roots an organization object covers; the users a contact/organization subject reaches | Removes the walled users' DIRECT shares on covered secure records (strict read, revoke, read-back, root-set cache cleared); reports team/role/BU access as not enforceable (never revoked); honours N5 and S5. The route is gated on Write on the ENTRY; the job runs every 5 minutes. |

### ⚠️ Provisioning dependency — read before deploying task 039 to any environment

**Today (this task), a missing table has ZERO runtime impact** — `NoAccessListReader` is registered in DI
but nothing calls `GetDeniedRecordsAsync` yet; `ApplyVetoPipeline` Slot 1 is still a documented no-op.

**Once task 039 ships**, this changes: if `sprk_noaccessentry` does not exist in an environment (a fresh
customer stamp, a rebuilt dev org, an environment that predates this table), every query
`NoAccessListReader` issues will get a non-success response from Dataverse (the entity set
`sprk_noaccessentries` won't resolve). Per this reader's OWN fail-closed design (NFR-01), that is
indistinguishable from any other read fault — it produces a deny-all-queried result for EVERY evaluation.
Combined with task 039's wiring (`composed.Remove(recordId)` for every denied id), the practical effect on
a fresh environment lacking this table would be **every record denied to every contact-sourced principal**
— not a security hole (fail-closed is doing exactly what it's designed to do), but a total-outage-shaped
availability problem that would be confusing to diagnose without this note.

**Action for whoever deploys task 039 (or this table) to a new environment**: create `sprk_noaccessentry`
(via the Deployment section below, or an equivalent schema-deployment script) BEFORE or ALONGSIDE the code
that wires the veto in — never after. This is a genuine environment-provisioning prerequisite, not an
optional nice-to-have; consider adding it to the customer-provisioning handler catalog
(`docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`) if UAC-r2 as a whole ships to new customer
environments before that guide is otherwise updated for this project's schema additions.

---

## Deployment

### What actually happened (transparency note)

The first creation attempt used `mcp__dataverse__create_table`, which has **no solution/publisher
parameter** — it silently created the table under the environment's DEFAULT publisher
(`cr140_noaccessentry`), not the `Spaarke` publisher (customization prefix `sprk`) this codebase uses
everywhere else. That stray `cr140_noaccessentry` table has been flagged to the project owner for deletion
(requires explicit human consent — `mcp__dataverse__delete_table` will not proceed without it, and a
non-interactive subagent cannot supply that consent itself). It carries zero data and zero code references.

The **correct** `sprk_noaccessentry` table (documented above) was created via the raw Dataverse Web API,
following the exact pattern `scripts/Deploy-PrecedentEntity.ps1` already uses in this repo: explicit
`SchemaName` values prefixed `sprk_`, POSTed to `EntityDefinitions` / `.../Attributes` /
`RelationshipDefinitions`, each carrying the `MSCRM.SolutionUniqueName: SpaarkeCore` header so the
components land in the **Spaarke** publisher (confirmed live: `customizationprefix = sprk`,
publisherid `6aeef721-ba73-f011-b4cb-6045bdd6a665`) and the **SpaarkeCore** unmanaged solution (confirmed
live: `solutionid fbfef485-e2a8-4b04-a795-7fa607402903`, `ismanaged: false`, per ADR-022). Customizations
were published (`PublishXml`) after creation. **Every column in this document was independently
re-verified via `mcp__dataverse__describe('tables/sprk_noaccessentry')` after deployment** — this document
reflects the live read-back, not the request payload.

**Lesson for future schema tasks**: verify the resulting entity's prefix with `describe()` immediately
after any `mcp__dataverse__create_table` call, before adding further columns — the tool does not let you
choose the target publisher/solution.

### Task 143 — `sprk_subjectsystemuser` (deployment record; PENDING)

| Step | Command | State |
|------|---------|-------|
| Dry run (read-only) | `scripts/Set-NoAccessSystemUserSubjectSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com` | ✅ run 2026-10-02 by task 143 (read-only): table present, column absent — "WOULD create … (lookup -> systemuser, relationship sprk_systemuser_sprk_noaccessentry_subjectsystemuser)" |
| G-1 Apply | `… -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Apply` | ✅ done (the column is live: `sprk_subjectsystemuser`, Lookup, read from metadata 2026-10-07 by task 154) |
| G-1 Verify | `… -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Verify` → exit 0; then `mcp__dataverse__describe('tables/sprk_noaccessentry')` shows `sprk_subjectsystemuser` (Lookup → systemuser) | ✅ column present (see the row above) |
| Deploy the BFF | only after Verify exits 0 | ✅ done: G-1 Verify PASS and the batch-4 BFF deployed to dev on 2026-10-06 (`projects/unified-access-control-r2/notes/batch4-live-gates-2026-10-06.md`) |

The order is binding: column → verify → BFF. Live privilege census (read-only, 2026-10-02, re-read unchanged by task 154
on 2026-10-07): Create/Write on the table — System Administrator, System Customizer, Service Writer (Global); Read —
those plus Spaarke Core User (Global), Service Reader (Global), Support User (Basic). 0 entries exist in dev. Owner O2
changes Spaarke Core User and adds the access-administrator role (task 154, `scripts/Set-NoAccessEntryRolePrivileges.ps1`).
The Microsoft platform roles (Service Reader/Writer/Deleter, Support User) are held only by Microsoft application users
and the Microsoft Support User account, and are left as they are unless the owner decides otherwise.

### Solution packaging

```bash
# Pack and import solution (same procedure as every other SpaarkeCore entity)
pac solution pack --folder ./src/solutions/SpaarkeCore --zipfile SpaarkeCore.zip --managed false
pac solution import --path SpaarkeCore.zip --force-overwrite
```

---

*Schema version: 1.0 | Created: 2026-09-04 | Project: unified-access-control-r2 (task 038) | Deployed + verified live against `spaarkedev1.crm.dynamics.com` via `mcp__dataverse__describe`.*
