# sprk_accessinheritance — what a filed work assignment's, project's or document's access was derived from

> Owner: unified-access-control-r2 task 175 (owner rounds 84 and 87). Schema: `scripts/Set-AccessInheritanceSchema.ps1`.
> Writer: the BFF only (`SecureRootInheritance.FollowParentsAsync`). Never on a form. Field-secured: only the BFF
> application users (task 133's "Spaarke BFF-Managed Field Writers" profile) can read or write it.
>
> **Documents (task 189, D7 T7a):** the same column and format on `sprk_document`, NOT field-secured, schema
> `scripts/Set-DocumentAccessPermissionSchema.ps1`. See [Documents](#documents-task-189) — the sections above it are about
> work assignments and projects.

## The rule it serves (owner round 87)

- A work assignment or project filed under a matter or project inherits a FLOOR: Secure if any ancestor is secure; the
  most restrictive Access Permission through its parents (task 174's walk).
- Its stored `sprk_issecure` / `sprk_accesspermission` = max(own, floor). It is never looser than the floor.
- A user may make it stricter by hand (Make Secure; a stricter Access Permission on the form). That value is the record's
  OWN and stays when the parent later loosens. Only the INHERITED part follows the parent down.
- A re-file never loosens: what the record held beyond the new parents' floor becomes its own.

## The column

| | |
|---|---|
| Tables | `sprk_workassignment`, `sprk_project` |
| Logical name | `sprk_accessinheritance` (SchemaName `sprk_AccessInheritance`) |
| Type | Multiple lines of text, MaxLength 4000, not required, audited |
| Field security | Secured (created secured). "Spaarke BFF-Managed Field Writers" read/create/update — its members are the BFF application users only. No reader profile: no user, form or client reads it. System Administrator keeps full access (platform rule, owner decision F4). |
| Solution | SpaarkeCore |
| Content | versioned JSON (below); empty = not recorded yet |

```json
{
  "v": 1,
  "parents": ["sprk_matter:3f1c…"],
  "floorSecure": true,
  "floorPermission": 100000002,
  "ownSecure": false,
  "ownPermission": null
}
```

| Field | Meaning |
|---|---|
| `parents` | The DIRECT parents (`table:id`) the stored values were last derived from. A different set = a re-file. |
| `floorSecure`, `floorPermission` | The floor last applied. A stored value different from max(own, this floor) = a user's edit. |
| `ownSecure` | The Secure designation was set on this record (Make Secure, or secure while parentless). |
| `ownPermission` | The Access Permission set on this record (Standard 100000000 / Limited 100000001 / Restricted 100000002); `null` = none (it follows the floor). |

## Why one JSON column and not a marker per value

A per-value "own" marker alone cannot tell, from the stored values, a user's edit from a parent's change. A stored
Restricted above a Standard floor means either "the parent just loosened" or "a user just tightened it". The marker also
cannot tell a re-file from a parent loosening, which is verifier F1's class of bug. The last floor and the last parents
make each case decidable:
- a stored value that differs from max(own, last floor) is a user's edit. The edit is the record's own when it is
  stricter than the last floor OR the live one; otherwise it is put back to the live floor and the own value is cleared,
  unless the own value is no looser than the edit (task 175 writes the value first and the record last, so a failed
  record write leaves max(own, live floor)). Task 189's fix round added the live floor: compared with the last floor
  alone, a user's Limited set after a Restricted parent widened to Standard was put back to Standard (`AccessInheritance.OwnPermissionOf`);
- a different parent set is a re-file, which never loosens;
- a floor that moved under the same parents is the parent's change, which inherited values follow.

Five facts in one column: the relocation ledger (`sprk_relocationpending`) is the precedent.

## Why it is locked (task 175 fix round, verifier F1-1)

The record decides whether a Secure designation is inherited (it follows its parent out of secure) or the record's own (it
stays). A user who could write it could mark a child someone secured by hand as "inherited" and have the BFF un-secure it
without F3. So:
- **Dataverse field-level security**: only the BFF application users can read or write it
  (`scripts/Set-AccessInheritanceSchema.ps1`, which creates it already secured and grants the writer profile at once);
- **every BFF writer refuses** a caller-supplied write that names it — update and create, any spelling
  (`SecureRootFilingGate`, `CheckRefileAsync`, `PlanCreateAsync`: `409`/refusal `sdap.access.access_record_server_only`);
- **no maker configuration targets it**: the script's (p6) check over `sprk_fieldmappingrule`, `sprk_aitopicregistry` and
  `sprk_emailupdatefield`, the same three channels task 150 checks for `sprk_issecure`.

## Empty never loosens

An empty value — not written yet, or one the BFF cannot read because field security hides it — is read by the backfill
rule, which only ever raises. A secured column the BFF cannot read answers empty, exactly like a row with no record, so the
two cannot be told apart on a read; they are told apart on a WRITE: the cascade reads the record back after writing it,
and an empty read-back fails the job run (`sdap.inherit.access_record_hidden`) and stops the pass writing further records.
A record that is not valid JSON is reported (`sdap.inherit.access_record_unreadable`), loosens nothing and is not
overwritten.

## When a record is trusted (fix round 2)

- **The column must be field-secured.** Before trusting any record, the cascade reads the column's metadata (`IsSecured`
  on both tables), once per job pass or request. If the column is not secured, or the metadata cannot be read, no record
  is trusted: every record is undetermined, nothing is written, and the job run fails with
  `sdap.inherit.access_record_not_secured`.
- **An EMPTY record is decided on only once the BFF's read is proven.** Proof, once per job pass or request, is any one of:
  - the BFF user holds the System Administrator role, matched by its role template id
    (`627090ff-40a3-4053-8790-584edc5be201`, carried by every business unit's copy; never by its name), directly or
    through a team it is a member of;
  - a field security profile it holds, directly or through a team it is a member of, grants Read (`canread` = 4) on the
    column on that table (each table is proven separately: a profile granting Read on one table proves only that one).
    A fault in any of these reads is no proof (fail closed).

  A non-empty record read on a table is not proof: Dataverse can grant a secured field on one record (a record-level field
  share), so one readable record says nothing about the empty ones (UAC 2026-10-23: a record-level field share must not
  prove a column).

  Without proof, the record is undetermined (`access_record_hidden`) and nothing is written. An empty read the BFF cannot
  vouch for may hide a real record, so the backfill rule's guess must never overwrite it.

- **Provisioning dependency.** Stamps give the BFF app user System Administrator directly (H10 step 9), which proves
  both tables. If the BFF app user is ever moved off System Administrator (least privilege), that change must add a field
  security profile with `canread` = 4 on `sprk_accessinheritance` for both `sprk_project` and `sprk_workassignment`;
  otherwise every empty access record becomes undetermined and the inheritance backfill stops (fails closed).

## Backfill (existing rows)

None by script. A record secured by inheritance gets its record at once (inherited). The first time the job (every 5
minutes; every work assignment and project) or an inline follow sees any other record with no value, it applies the
backfill rule against the current floor and writes the column:
- a value EQUAL to the floor is inherited;
- a value STRICTER than the floor is set on the record.

A parentless record's values are all its own. A matter or project's own `/unsecure-project` first records every record
below it that has no record yet, while it is still secure. Those records are recorded as inherited and follow it out. A
record whose parent was un-secured outside the BFF before the job first saw it stays secure (fail closed), and its F3 holder
can remove the designation, as on a parentless record.

## Writers and readers

- **Written by:** `FollowParentsAsync` — called by the job, by a parent's `/unsecure-project` (records below it), by the BFF
  re-file writers (`SecureAfterWriteAsync`), and right after a caller's Make Secure (own secure recorded at once).
- **Each write** is made only if the column still holds what the decision read (re-read and compared just before the write;
  a user's concurrent edit is never overwritten — `sdap.inherit.changed_concurrently`, decided again next run), and is
  read back.
- **Read by:** the same. No access decision reads it directly: enforcement uses max(own stored, ancestors) (task 174), and
  this column only decides what the stored values should be.
- **Display:** `GET /can-manage-access` reports `followsParents`, `floorSecure` and `floorAccessPermission`. The forms and
  Manage Access say "inherited from {parent}" or "set on this record" by comparing the value with the floor.

## Documents (task 189)

`sprk_document.sprk_accessinheritance` records a document's OWN narrowing of `sprk_accesspermission` (D7: a document's own
Restricted hides it from partners). Rule and writers: [child-access-permission.md](child-access-permission.md#documents-their-own-narrowing-task-189).

| | Work assignment / project (task 175) | Document (task 189) |
|---|---|---|
| Values recorded | `sprk_issecure` and `sprk_accesspermission` | `sprk_accesspermission` only (`floorSecure` and `ownSecure` are always `false`) |
| `floorPermission` | the floor through task 174's walk | the value task 173's rule gives it: the most restrictive of the TOPS of its filing |
| A re-file | never loosens (what it held beyond the new floor becomes its own) | follows the new parents (task 173's contract) for a row without an own narrowing; an own narrowing stays |
| Field security | secured; the BFF's read proven before an empty record is trusted | **not secured** (decision D1): the inherited part is always recomputed from the parents, never read from the record, so an edit of the record can only remove the user's own narrowing or add one. Securing would add no protection and would add the hidden-read widening risk above |
| Caller-supplied writes | refused (`sdap.access.access_record_server_only`) | a user's write is not refused (the user could write the unsecured column directly); the Office fallback writes `AccessInheritance.OwnNarrowing(Restricted)` on purpose. An app-only generic writer (a playbook's Update Record node or output mapping) is refused, the record and the value alike (`sdap.access.document_access_server_only`, fix round C K5); the same writers refuse the D7 link columns (`sdap.access.document_link_server_only`) and the BFF-owned document columns (`sdap.access.document_bff_column_server_only`, #2240), and on every table the owner (`sdap.access.owner_server_only`) and creator (`sprk_createdbyperson`, `sprk_createdbycontact`, `sprk_requestedby`; `sdap.access.creator_server_only`) columns (#2247; Repair asks only over its mapped columns, never its own owner decision); #2251: the same writers refuse a table whose rows are access whole (`InheritedAccessPermission.AccessTables`: grants, organisation membership, ethical walls, the Assigned-To ledger, team membership; `sdap.access.access_table_server_only`) and, on a secure root, `sprk_accessinheritance` with the secure flag, Access Permission, security business unit and container (`InheritedAccessPermission.SecureRootColumns`, #2220's create-side set plus the container; `sdap.access.secure_root_server_only`); #2253 fix round 1: a table that is not a bare identifier after trimming (`^[A-Za-z][A-Za-z0-9_]*$`) is refused first, for every writer, with `sdap.access.table_name_invalid` (a name such as `sprk_externalrecordaccess')?$select=EntitySetName#` reached `EntityDefinitions(LogicalName='{name}')` as `sprk_externalrecordaccess`); every Spaarke.Dataverse URL builder refuses such a name too (`DataverseIdentifier`). `fieldpermission` joins `AccessTables`. A non-root table's access columns are refused with `sdap.access.access_column_server_only` (`InheritedAccessPermission.AccessColumns`: on `contact` the identity binding and invite columns and the standing grant; on `sprk_organization` the standing grant; on `systemuser` the home link and its mirrors, `sprk_primarycontact` and `sprk_isexternal`; on `businessunit` `sprk_containerid`). The field-mapping push runs as its caller and asks as `WriteIdentity.Caller`: the name check, the document codes, owner and creator only; access tables, secure-root columns and access columns are left to the caller's own privileges and field security (a matter's `sprk_accesspermission` stays editable by a Write holder). #2253 fix round 2: `AccessTables` also holds the platform tables that grant or scope access (`InheritedAccessPermission.PlatformSecurityTables`: `role`, `roleprivileges`, `systemuserroles`, `teamroles`, `fieldsecurityprofile`, `systemuserprofiles`, `teamprofiles`, `principalobjectaccess`, `teamtemplate`, `hierarchysecurityconfiguration`, `position`), and `AccessColumns` also holds `systemuser.businessunitid`, `azureactivedirectoryobjectid`, `domainname`, `internalemailaddress`, `isdisabled`, `accessmode`, `applicationid`, `parentsystemuserid`, `positionid`; `team.businessunitid`, `name`, `teamtype`, `isdefault` (the owner-team predicates); and `businessunit.name` (the Secure Record business unit is found by name). |
| Writers | `FollowParentsAsync` | `ChildAccessPermissionReconciler` (in `SecureChildReconciliationJob`, every 2 minutes) and `CoreAncestorResolver.RefreshInheritedAccessPermissionAsync` (the child-records create and re-file), both through `InheritedAccessPermission.DecideDocument` |
| Own vs inherited | `AccessInheritance.OwnPermissionOf` — ONE copy of the classification, shared | the same |
| An unreadable record, or an unknown stored level | undetermined, nothing written | an unknown stored level: undetermined, nothing written, carried. An unreadable record (or no record column in the environment): the value only raised to the floor, never lowered, the record untouched; an unreadable one is also undetermined and carried (fix round C) |
| No record yet | the backfill rule (stricter than the floor is own) | the same; every parented row is then given a record, the Standard majority too (fix round C, F1) |
| Each write | re-read first; value and record compared | the same (the job); value and record in ONE update |
