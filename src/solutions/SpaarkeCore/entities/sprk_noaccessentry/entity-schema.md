# sprk_noaccessentry Entity Schema

> **Entity Purpose**: The FR-23 deny-list store — the ethical-wall and per-child-revocation mechanism.
> Each row is a **veto**, never a level: a matching active entry means the subject has **no access** to
> the object, full stop. This table is read by the BFF's `NoAccessListReader` (fail-closed) and feeds
> Slot 1 of `AccessibleRecordSetService.ApplyVetoPipeline` (wired by task 039 — this task delivers the
> store + reader only, per spec FR-23 / Placement table row 2: "Deny-list store | none found | New").
>
> **Schema Version**: 1.0
> **Created**: 2026-09-04
> **Project**: `unified-access-control-r2` (task 038)
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
| **Description** | Deny-list entry (FR-23): vetoes access for a subject (contact or organization — exactly one populated) against an object (an organization reference — ethical wall — or a specific polymorphic record — per-child revocation; exactly one populated). A veto is never a level: a matching active entry REMOVES the subject's access rather than writing a low access value. |

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
| sprk_name | Name | String | Application Required | 850 (platform-widened from the 200 requested — see note) | Short human description of the entry, e.g. "Acme Corp — Ethical Wall" or "Jane Doe — Matter 12345 revoke". **Entered manually** — no auto-naming plugin exists (out of scope for this task; see Business Rules). |

> **Note on MaxLength 850**: the create call requested `MaxLength=200`; Dataverse returned `NVARCHAR(850)`
> on the primary name attribute specifically (verified live). This is a known platform behavior for
> primary-name (`IsPrimaryName=true`) string attributes on some environments/versions and is **not** a
> data-entry risk (850 is more permissive than requested, not less) — documented here so a future reader
> is not surprised the live column doesn't match the literal request.

### Subject Fields — exactly ONE populated

| Logical Name | Display Name | Type | Target Entity | Description |
|--------------|--------------|------|----------------|-------------|
| sprk_subjectcontact | Subject Contact | Lookup | contact | The denied contact. |
| sprk_subjectorganization | Subject Organization | Lookup | sprk_organization | The denied organization — denies every contact who is an ACTIVE member (the caller resolves membership via the same `sprk_contactorganization` junction `ExternalParticipationService.QueryActiveOrgIdsAsync` already reads; the reader is agnostic to how membership was resolved — see `<notes>`). |

### Object Fields — exactly ONE of {Object Organization} or {Object Record Type + Object Record Id} populated

| Logical Name | Display Name | Type | Target Entity | Description |
|--------------|--------------|------|----------------|-------------|
| sprk_objectorganization | Object Organization | Lookup | sprk_organization | **Ethical wall.** Denies every candidate record whose referenced-organization set contains this organization — **ANY** reference, including a non-conferring one (e.g. opposing counsel referenced on a matter). The over-match is the specified behavior (spec FR-23 / register B-10), not a bug. |
| sprk_objectrecordtype | Object Record Type | Lookup | sprk_recordtype_ref | **Per-child revocation.** The entity type of the specific denied record — the ADR-024 resolver-pair pattern (type + id), deliberately **without** an entity-specific lookup per possible type (the task's own constraint: "do NOT create one lookup per possible entity type" — the object here can be ANY record type, unlike the small closed parent-type sets ADR-024's dual-field strategy targets). |
| sprk_objectrecordid | Object Record Id | String(50) | — | GUID (as text) of the specific denied record. Paired with `sprk_objectrecordtype`. **Matching is by id alone** — `NoAccessListReader` does not additionally require the entity-logical-name to match, because Dataverse record ids are effectively globally unique (random v4 GUIDs assigned per row); the recordtype lookup exists for provenance/audit legibility, not as a second matching key. |

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
| 2 | `sprk_subjectcontact` = Jane Doe | `sprk_objectrecordtype`/`sprk_objectrecordid` = (sprk_communication, `{guid}`) | **Per-child revocation.** Jane has broad access to Project 1 (and therefore its emails, to-dos, invoices by 1-hop inheritance), but one specific privileged email must be walled off from her alone. | Only that one communication record is denied to Jane; the parent project and every other child are unaffected. |
| 3 | `sprk_subjectorganization` = Beta LLP | `sprk_objectorganization` = Acme Corp | **Firm-wide ethical wall.** Every contact who is an active member of Beta LLP (per `sprk_contactorganization`) is denied on any record referencing Acme Corp. | Widest-blast-radius combination — denies an entire firm's roster, not one person. Used when the conflict is at the firm level, not the individual attorney level. |
| 4 | `sprk_subjectorganization` = Beta LLP | `sprk_objectrecordtype`/`sprk_objectrecordid` = (sprk_matter, `{guid}`) | **Firm-wide per-record revocation.** No Beta LLP member may see this one matter, even though the firm otherwise has standing access elsewhere. | Only the named record is denied to every Beta LLP member. |

---

## Business Rules

1. **Exactly-one-subject / exactly-one-object are NOT enforced at the schema level.** No pre-create
   plugin exists for this table (out of scope for task 038 — the task's declared outputs are the store +
   reader only). `NoAccessListReader` defends against a malformed row (neither or both subject fields
   populated; neither or both object fields populated) by **logging a warning and excluding that row from
   matching** — a malformed row denies nothing, rather than either being silently ignored without a trace
   or (the more dangerous alternative) denying an unbounded set because its intended scope is unknowable.
   This is a data-quality guard, distinct from the NFR-01 fail-closed behavior for a **faulted read** (see
   the reader's XML doc comments for the reasoning split).
2. **Deactivation = veto lifted.** Setting `statecode = Inactive` ends the denial — mirrors
   `sprk_externalrecordaccess`'s "deactivation = revocation" convention exactly, so an admin already
   familiar with that table needs no new mental model here.
3. **No expiry field.** Unlike `sprk_externalrecordaccess.sprk_expiresdate` (which the design register
   flags as write-only/unenforced — finding A-5), this table deliberately has **no** expiry column: an
   ethical wall or a per-child revocation is not the kind of grant that should silently lapse. Time-boxed
   denials, if ever needed, are a future extension — not a gap this task leaves open, a decision this task
   makes.
4. **A veto row is never read as "somewhat denied."** There is no partial-strength deny; a matching active
   row denies fully, and the calling evaluator (task 039) is expected to **remove** the candidate record's
   key from the composed rights map — never write a low `AccessRights` value (root CLAUDE.md §5 fact 5;
   binding rule 1 of this task).

---

## Relationships

### N:1 Relationships (Lookups — this table references)

| Logical Name | This Field | Parent Table | Delete Behavior |
|-------------|-----------|--------------|------------------|
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

### Solution packaging

```bash
# Pack and import solution (same procedure as every other SpaarkeCore entity)
pac solution pack --folder ./src/solutions/SpaarkeCore --zipfile SpaarkeCore.zip --managed false
pac solution import --path SpaarkeCore.zip --force-overwrite
```

---

*Schema version: 1.0 | Created: 2026-09-04 | Project: unified-access-control-r2 (task 038) | Deployed + verified live against `spaarkedev1.crm.dynamics.com` via `mcp__dataverse__describe`.*
