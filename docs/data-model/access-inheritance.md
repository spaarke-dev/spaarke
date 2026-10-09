# sprk_accessinheritance — what a filed work assignment's or project's access was derived from

> Owner: unified-access-control-r2 task 175 (owner rounds 84 and 87). Schema: `scripts/Set-AccessInheritanceSchema.ps1`.
> Writer: the BFF only (`SecureRootInheritance.FollowParentsAsync`). Never on a form.

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

## Backfill (existing rows)

None by script. The first time the job (every 5 minutes; every work assignment and project) or an inline follow sees a
record with no value, it applies the backfill rule against the current floor and writes the column:
- a value EQUAL to the floor is inherited;
- a value STRICTER than the floor is set on the record.

A parentless record's values are all its own. The parent's own `/unsecure-project` treats a flag below it as inherited,
because the parent was secure until that call.

## Writers and readers

- **Written by:** `FollowParentsAsync` — called by the job, by a parent's `/unsecure-project` (records below it), by the BFF
  re-file writers (`SecureAfterWriteAsync`), and right after a caller's Make Secure (own secure recorded at once).
- **Read by:** the same. No access decision reads it directly: enforcement uses max(own stored, ancestors) (task 174), and
  this column only decides what the stored values should be.
- **Display:** `GET /can-manage-access` reports `followsParents`, `floorSecure` and `floorAccessPermission`. The forms and
  Manage Access say "inherited from {parent}" or "set on this record" by comparing the value with the floor.
