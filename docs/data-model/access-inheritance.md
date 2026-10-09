# sprk_accessinheritance — what a filed work assignment's or project's access was derived from

> Owner: unified-access-control-r2 task 175 (owner rounds 84 and 87). Schema: `scripts/Set-AccessInheritanceSchema.ps1`.
> Writer: the BFF only (`SecureRootInheritance.FollowParentsAsync`). Never on a form. Field-secured: only the BFF
> application users (task 133's "Spaarke BFF-Managed Field Writers" profile) can read or write it.

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
- a stored value that differs from max(own, last floor) is a user's edit;
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
  - a non-empty record read in the same pass or request;
  - the BFF user holds the System Administrator role;
  - one of its field security profiles grants Read on the column on both tables.

  Without proof, the record is undetermined (`access_record_hidden`) and nothing is written. An empty read the BFF cannot
  vouch for may hide a real record, so the backfill rule's guess must never overwrite it.

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
