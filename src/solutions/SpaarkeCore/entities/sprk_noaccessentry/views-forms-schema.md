# sprk_noaccessentry: form, view, subgrids and site map

> **Purpose**: the model-driven surface for authoring and seeing No Access entries: the entry main form and its library,
> the one view, the NO ACCESS subgrids on the Organization and Contact forms, the site map entry, and who can use them.
> **Project**: `unified-access-control-r2` task 154 (owner round 3b; reduced by owner round 59).
> **Created**: 2026-10-07.
> **Deployed by**: `scripts/Deploy-NoAccessEntryForms.ps1` (form, view, subgrids, site map) and
> `scripts/Set-NoAccessEntryRolePrivileges.ps1` (roles, owner decision O2). Both are dry run by default, with `-Apply`
> and `-Verify`. **Live state:** not applied when this file was written; run the scripts, then check with `-Verify`.
> Table definition: [`entity-schema.md`](entity-schema.md).

---

## Who sees this surface (owner O2, accepted 2026-10-01)

| Role | sprk_noaccessentry | What they see |
|---|---|---|
| Spaarke Access Administrator (new, assigned in addition to a user's other roles) | Create, Read, Write, Append, AppendTo at Organization depth; **no Delete** (deactivating an entry lifts the wall). Also AppendTo on contact, sprk_organization, systemuser and sprk_recordtype_ref, so the lookups can be set. | The entry form, the NO ACCESS subgrids, the site map entry. |
| Spaarke Core User | **nothing** (its Global Read is removed) | No subgrid rows, no site map entry. Record-level status comes from task 153's banner; Write-holders see a record's entries through Manage Access (tasks 064/067). |
| System Administrator, System Customizer | unchanged (full) | Everything. |
| BFF application users | System Administrator | The deny reads are app-only, so removing Core User's Read does not affect them. |

Microsoft platform roles that also hold privileges on the table (Service Reader, Service Writer, Service Deleter,
Support User) are held only by Microsoft application users and the Microsoft Support User account. The role script
lists them and never changes them; leaving them is an owner decision (task 154 escalation O2).

---

## The entry main form ("Information")

One main form. Quick create is **off**, so every "+ New" opens this form, which carries the shape check and the
post-save enforcement.

| Section | Fields | Notes |
|---|---|---|
| (header) | Status, Author (last modified by), Modified On | Read-only. The author is the user whose Write decides enforcement per record (owner N5). |
| (untitled) | Name | Filled as "{subject} - {object}" while it is empty or still holds the suggestion; never replaces a typed name. |
| SUBJECT - WHO IS DENIED (EXACTLY ONE) | Contact, Organization, User | `sprk_subjectcontact`, `sprk_subjectorganization`, `sprk_subjectsystemuser`. |
| OBJECT - AN ORGANIZATION, OR ONE RECORD | Organization, Record Type (opens the record picker), Record Id | `sprk_objectorganization`, `sprk_objectrecordtype`, `sprk_objectrecordid`. |
| REASON | Reason | `sprk_reason`. On this form only, never a view or subgrid column; readable only by roles holding Read (O2). |

**Library**: `sprk_/scripts/bff_auth.js`, then `sprk_/scripts/noaccessentry_postsave.js`
(source `src/solutions/webresources/sprk_noaccessentry_postsave.js`, version 1.1.0 or later). ONE form event:
OnLoad `Spaarke.NoAccessEntry.onLoad` (pass execution context), which registers everything else, each handler exactly
once (Unified Interface fires OnLoad again after a save). On load, a stored record id the access checks do not match
(braces, a leading space, blank) is corrected on the form and flagged "walls nothing until it is saved". The record
picker is self-contained (`Spaarke.NoAccessEntry.Picker`, one `register` call), so it can be replaced by a control.

| Handler | What it does |
|---|---|
| OnSave (shape check) | Refuses the save, with a form notification naming the problem, when the entry has zero or several subjects, no object, both an organization and a record, a half record pair, a record id that is not a record id, or a record type no deny reader evaluates. The record id is normalised to the canonical form (lowercase, no braces) before the save. **Deactivate is never blocked** (it lifts a wall and retires a malformed entry). This is a **preview** (write-path rule WP-2); the server rule below owns the shape. |
| Record picker (OnChange of Record Type) | The Record Type lookup offers only Project, Matter and Work Assignment (the roots the deny readers evaluate). Choosing one opens the platform lookup dialog for that table (the dialog the shared `PolymorphicPicker` uses). The picked id is written in canonical form and the object organization is cleared. Clearing the type clears the id. To choose another record, choose the record type again. |
| OnChange of Record Id (typed by hand) | Normalises at once; a value that is not a record id, or a record that does not exist, gets a field notification, which blocks the save. On load, a record that no longer exists gets a non-blocking warning instead. |
| OnChange of Organization (object) | Clears the record type and id, so exactly one object survives. |
| OnPostSave (task 143) | Only after a SUCCESSFUL save: `POST /api/v1/external-access/no-access/enforce {entryId}` as the user. If the sign-in helper is missing, a notice says the entry is enforced within 5 minutes. The notice names each record the entry was not enforced on because the author lacks Write (owner N5), and reports team/role access that cannot be removed per person (N2) and the last-person rule (S5). |

**Why no display-name column and no dedicated picker control.** Owner round 59 item 4: reuse the existing picker, do
not build the ObjectRecordPicker PCF, and keep `sprk_objectrecordname` only if the picker cannot show the record
without it. `RegardingResolver` cannot be bound here (it writes `sprk_regarding*` columns, an entity-specific lookup
and the core-ancestor stamp), and `PolymorphicPicker` is a React component that needs a PCF host. The form library
drives the same platform lookup dialog instead. The picked record's name is shown in a form notification and goes into
the suggested Name, which is what views and subgrids show, so no new column is needed.

---

## View: "Active No Access Entries" (the default public view)

The table's generated default view, completed. It is the only view this task changes, and every subgrid and the site
map entry open it.

| Property | Value |
|---|---|
| Filter | `statecode eq 0` (active entries only) |
| Sort | Created On, newest first |
| Columns | Name, Subject Contact, Subject Organization, Subject User, Object Organization, Object Record Type, Object Record Id, Created By, Created On |
| Not a column | Reason (O2) |

The generated "Inactive No Access Entries", lookup, quick find and associated views are unchanged. Owner round 59 cut
the other views (including "Incomplete Entries"). A malformed entry is reported by the enforce route (422
`sdap.access.no_access.entry_malformed`) and logged by the reader and the 5-minute job.

---

## NO ACCESS subgrids

All three use the view above, 5 rows, no view picker. "+ New" opens the main form with the relationship's lookup
filled in by the platform. **"Add Existing" is hidden** on every `sprk_noaccessentry` subgrid (HideCustomAction on
`Mscrm.SubGrid.sprk_noaccessentry.AddExistingStandard` / `.AddExistingAssoc`; the ribbon-only solution
`infrastructure/dataverse/ribbon/NoAccessEntryRibbons`, imported by `scripts/Deploy-NoAccessEntryRibbon.ps1`): it
re-points an existing entry's subject or object lookup without the form, so it would skip the shape check and the
enforcement and could leave a two-subject entry that walls nothing while the subgrid still lists it.

**Known limit (accepted):** the grid's bulk Edit dialog, the grid Activate command, an Excel or data import and the
Web API also write without the form. A malformed row they leave denies nothing and is logged (and the enforce route
answers 422 for it); opening it in the form corrects a non-canonical id and names the problem.

| Form (the one the Matter Management app exposes) | Placement | Subgrid | Relationship | "+ New" fills |
|---|---|---|---|---|
| Organization main form | General tab (SYSTEM ACCESS / MEMBERS), new section **NO ACCESS** after MEMBERS | Ethical walls on this organization | `sprk_sprk_organization_sprk_noaccessentry_objectorganization` | Object Organization |
| Organization main form | same section | This organization denied | `sprk_sprk_organization_sprk_noaccessentry_subjectorganization` | Subject Organization |
| Contact main form | Summary tab, new section **NO ACCESS** after ORGANIZATIONS (the contact's memberships) | This contact denied | `sprk_contact_sprk_noaccessentry_subjectcontact` | Subject Contact |

The Matter Management app also exposes two Power Pages profile forms for Contact ("Profile Web Form (Enhanced)" and its
Japanese variant). They are portal forms and get no NO ACCESS section (task 154 escalation (f)).

**A user without Read on the table.** After O2, a core user opening an Organization or Contact must not see an empty
grid that reads as "nobody is denied". The live gate checks how the platform renders these subgrids for such a user. If
it shows an empty list instead of hiding the grid or showing a permission message, a small OnLoad script that hides the
section unless a one-row read of the table succeeds is the follow-up (task 154 constraint "no false empty").

---

## Site map

| App | Group | Entry | Opens | Visible to |
|---|---|---|---|---|
| Matter Management (`sprk_MatterManagement`), the only app whose site map lists Access Permission Grants and Organizations | the administration group, after "Access Permission Grants" | **No Access Entries** (`subarea_sprk_noaccessentry`) | the default view, "Active No Access Entries" | users holding Read on `sprk_noaccessentry` (the subarea declares `<Privilege Entity="sprk_noaccessentry" Privilege="Read" />`) |

The table is added to the app's components so the entry resolves.

---

## Deployment (run by the main session after the PR merges)

```powershell
# 1. The form library (version 1.1.0); read it back and compare before continuing
pwsh -File scripts/Deploy-WebResourceInline.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com `
  -WebResourceName sprk_/scripts/noaccessentry_postsave.js `
  -FilePath src/solutions/webresources/sprk_noaccessentry_postsave.js -WebResourceType 3

# 2. Form, view, subgrids, site map: dry run, apply, verify
pwsh -File scripts/Deploy-NoAccessEntryForms.ps1
pwsh -File scripts/Deploy-NoAccessEntryForms.ps1 -Apply      # snapshot in scripts/logs; undo with -RestoreFrom (same environment only)

# 3. Hide Add Existing on the subgrids (ribbon-only solution import): dry run, apply (verifies itself)
pwsh -File scripts/Deploy-NoAccessEntryRibbon.ps1
pwsh -File scripts/Deploy-NoAccessEntryRibbon.ps1 -Apply
pwsh -File scripts/Deploy-NoAccessEntryForms.ps1 -Verify      # also checks Add Existing is hidden

# 4. Roles (O2): dry run, apply, verify
pwsh -File scripts/Set-NoAccessEntryRolePrivileges.ps1
pwsh -File scripts/Set-NoAccessEntryRolePrivileges.ps1 -Apply -AcceptOtherRoles -AssignToUserPrincipalName <admin UPN>
pwsh -File scripts/Set-NoAccessEntryRolePrivileges.ps1 -Verify
```

The order matters: the form script refuses to register a library older than 1.1.0, and the roles go last so that
administrators already have the form when core users lose Read.
