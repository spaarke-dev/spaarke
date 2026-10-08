# Task 154: No Access management on the entry, Organization and Contact forms

> Executor notes, 2026-10-07. Branch `feat/uac-r2-154-no-access-forms` (worktree `C:\wt154`, from `origin/master` 36ff14147).
> Rigor FULL, opus/high. Scope as reduced by the 2026-10-05 amendment (owner round 59, items 4 and 8).
> No live change was made by this task. Every live step ships as a script with dry run, `-Apply` and `-Verify`.

## Step 0: preconditions

- **143's server and schema work is merged and live.** `sprk_subjectsystemuser` is live (metadata read 2026-10-07; G-1
  Verify PASS in `batch4-live-gates-2026-10-06.md`). The reader's three-subject malformed-row rule is in
  `NoAccessListReader.SubjectKindOf`, so escalation (h) does not fire. 064 is no longer a dependency (amendment 4).
- **143's form library exists**: `src/solutions/webresources/sprk_noaccessentry_postsave.js`, web resource
  `sprk_/scripts/noaccessentry_postsave.js` (deployed, version 1.0.0, registered on no form). Escalation (g) does not
  fire; this task extends that file.
- **N5 outcome storage (escalation (e)).** 143 persists no enforcement outcome on the row, and owner round 59 CUT "the
  per-entry enforcement state" (064 row, batch-5 review). So there is no stored outcome to show on the form or the view;
  the outcome is the post-save notice. The notice now names EACH record an entry was not enforced on because the author
  lacks Write (it used to say only "some records"). That satisfies criterion 10's "in the notice" part without a BFF
  change (the report already carries `notEnforced[].recordType/recordId/reason`).
- **O2** (research note `session27-ux-research-ethical-wall-secure.md:74`): ACCEPTED as recommended on 2026-10-01; not
  revised in any later round I found (rounds 59-78 searched). Applied by `scripts/Set-NoAccessEntryRolePrivileges.ps1`.

## Step 1: live inventory (spaarkedev1, read-only, 2026-10-07)

| Item | Finding |
|---|---|
| `sprk_noaccessentry` | Organization-owned, type code 10990, **quick create already OFF** (`IsQuickCreateEnabled=false`, no quick-create form). 0 rows. `sprk_name` RequiredLevel **None** (the schema doc said "Application Required"; doc corrected). |
| Entry main form | One: "Information" `a0fc5699-417f-4d3c-b834-fbd95e80963d`, holds only Name, no library. |
| Apps | Only **Matter Management** (`sprk_MatterManagement`, `729afe6d-…`) lists `sprk_externalrecordaccess` and `sprk_organization` in its site map (group `group_admin`, subarea `subarea_bb0cb719`). Escalation (c) does not fire. `sprk_noaccessentry` is not an app component. |
| Organization form in the app | One main form: "Organization main form" `07000450-aa00-f111-8406-7c1e525abd8b`. General tab: SYSTEM ACCESS (`General_section_systemaccess`) and MEMBERS (`_section_12`, the `sprk_contactorganization` subgrid) in the 67% column. |
| Contact forms in the app | **Three** main forms (escalation (f) FIRES): "Contact main form" `1fed44d1-…` (Spaarke's), and two Power Pages portal forms, "Profile Web Form (Enhanced)" and "… - Japanese". The ORGANIZATIONS section (`summary_organizations`, the `sprk_contactorganization` subgrid) is on SUMMARY_TAB of the main form. |
| View | Generated default "Active No Access Entries" `1487543c-…` (statecode = 0; columns Name, Created On only). |
| Relationships | `sprk_sprk_organization_sprk_noaccessentry_objectorganization`, `…_subjectorganization`, `sprk_contact_sprk_noaccessentry_subjectcontact` (as the schema doc says). |
| Record types | `sprk_recordtype_ref`: Matter `e8547bb4-…`, Project `ca68b3bb-…`, Work Assignment `e8e1608c-…`. |
| Object types any deny reader evaluates | `sprk_project`, `sprk_matter`, `sprk_workassignment` only (`NoAccessShareEnforcer.SecureRootTypes`, `AccessibleRecordSetService` candidates, `ExternalParticipationService.IsFlagBearingRootType`). Escalation (d): the schema doc's per-child example 2 is stored and enforced nowhere; owner round 59 item 7 already made that a known limit; doc corrected. |
| Client readers of `sprk_noaccessentry` | None in `src/client` / `src/solutions` except the form library itself. Server reads are app-only (BFF app users hold System Administrator). The enforce route's gate asks the caller's table Write privilege, which the new role holds. Nothing breaks when core users lose Read. |
| Dataverse string equality | Case-insensitive, trailing spaces ignored, leading space and braces not matched (probe on `sprk_recordtype_refs`). This decided the canonical-id rule. |
| Subgrid render for a user without Read | NOT observable from here (no UI session). Live-gate item; see "No false empty" below. |

### Role privileges on `sprk_noaccessentry` (BEFORE; root roles; the planned AFTER)

| Role | Before | After (planned, O2) |
|---|---|---|
| System Administrator | C R W D A AT, Global | unchanged |
| System Customizer | C R W D A AT, Global | unchanged |
| Spaarke Core User (5 users) | R Global | **none** |
| Spaarke Access Administrator | (does not exist) | **new**, root BU, SpaarkeCore: C R W A AT Global, no Delete; AppendTo Global on contact, sprk_organization, systemuser, sprk_recordtype_ref |
| Service Reader | R Global | unchanged (owner decision) |
| Service Writer | C R W A AT Global | unchanged (owner decision) |
| Service Deleter | D Global | unchanged (owner decision) |
| Support User | R Basic | unchanged (owner decision) |

Service Reader/Writer/Deleter are held only by Microsoft application users (AI Builder, Power Pages, DataSync, …; no
person). Support User is held by the Microsoft "Support User" account only. This **fires the O2 escalation trigger**
("a non-administrator role other than Spaarke Core User holding Read or Write"): reported to the main session; the role
script refuses `-Apply` until `-AcceptOtherRoles` records the owner's answer. Recommendation: leave them (they are
Microsoft platform identities, not end users, and the BFF does not depend on them).

The BFF app users (`5967251e-…` "# mi-bff-api-dev", `1e40baad-…` "SDAP-BFF-SPE-API") hold System Administrator, so
their app-only deny reads are unaffected; the role script re-checks this every run.

## What was built

1. **Server half of the fail-open fix** (amendment 1; owner round 59 item 4). `NoAccessListReader.TryParseObjectRecordId`
   is the ONE canonical-id rule: 36-character hyphenated GUID, any case, trailing spaces allowed (exactly what the
   string-equality filters match), not the empty GUID. The reader's malformed-row rule and the enforcer's shape check
   both use it. Before, a braced id passed `Guid.TryParse`: the enforcer removed shares on save while the read-time veto
   never matched the row, and nothing reported it. Now such a row is malformed: 422 `entry_malformed` from the enforce
   route (message now names the id form), a warning from the reader and the job.
2. **Form library 1.1.0** (`sprk_noaccessentry_postsave.js`): OnSave shape check (preventDefault + form notification),
   id normalisation before save, record picker, object mutual exclusion, name suggestion, per-record N5 notice; 143's
   post-save enforcement unchanged. Registered from ONE OnLoad handler.
3. **`scripts/Deploy-NoAccessEntryForms.ps1`**: the entry form, the view, the Organization and Contact NO ACCESS
   sections, the site map entry and app component, quick-create check. Snapshot + `-RestoreFrom`.
4. **`scripts/Set-NoAccessEntryRolePrivileges.ps1`**: O2.
5. **Docs**: `entity-schema.md` (live column, `sprk_name` required level, canonical id, example 2 known limit, Business
   Rule 1 and 5, census, the organization-level section the owner asked for), new `views-forms-schema.md`, write-path
   registry row **I-15**.

## Deviations (for the main session)

1. **The record picker reuses the platform lookup dialog, not the RegardingResolver PCF or a PolymorphicPicker host.**
   Owner round 59 item 4 says "reuse the EXISTING polymorphic record picker (RegardingResolver / PolymorphicPicker)" and
   "do not build the ObjectRecordPicker PCF". Literal reuse is not possible:
   - `RegardingResolver` binds `sprk_regardingrecordtype` and writes `sprk_regardingrecordid/name/url`, an
     entity-specific `sprk_regarding{x}` lookup and the FR-26 core-ancestor stamp (`ResolverWriteHandler.ts`
     `applyRegardingSelection`, `:449-550`; `:631-632` hard-code the columns). None of those columns exist on
     `sprk_noaccessentry`, and a core-ancestor stamp on a deny entry would be wrong. Making it configurable would put a
     second, opposite write contract into a control deployed on many forms.
   - `PolymorphicPicker` is a React component; on a form it needs a PCF host, which is the cut PCF.
   The form library therefore drives the same `Xrm.Utility.lookupObjects` dialog that `PolymorphicPicker` delegates to:
   the Object Record Type lookup (filtered to the three evaluated roots) opens the record dialog for that table, and the
   picked id is written in canonical form. No GUID is typed, no new control or column is added. If the owner wants the
   chip-style control instead, that is the cut PCF (owner decision).
2. **`sprk_objectrecordname` is not created** (round 59 item 4: only if the picker cannot show the record without it). The
   form shows the picked record's name in a notification and puts it into the suggested Name, which every view and
   subgrid shows. The Object Record Id column in views/subgrids still shows the GUID text.
3. **One view** (round 59): the default "Active No Access Entries" is completed and used by all three subgrids, so the
   subgrids show the same columns (the original per-subgrid column sets were cut with the extra views). Reason is never
   a column. "Incomplete Entries", the lookup-view change and the other views are cut.
4. **Contact placement**: the NO ACCESS section goes after ORGANIZATIONS on the Summary tab of "Contact main form" only.
   The two Power Pages profile forms the app also exposes are not edited (escalation (f), below).
5. **Site map subarea declares `Privilege Read`** up front, instead of waiting for the live gate to show whether MDA
   hides it on its own (constraint "site map": "set its privilege requirement explicitly rather than leaving it").
6. **BFF change**: the POML's "no BFF change" constraint predates amendment 1, which puts the reader's canonical-id rule
   in scope. Publish size: 35.339 MB compressed on both a fresh `origin/master` build and this branch (delta < 0.01 MB);
   no package change, so no new CVE.

## Escalations fired (main session decides; none blocks the merge)

- **(f) several Contact main forms in the app.** Recommendation: edit only "Contact main form" (done in the script, by
  name); the Power Pages profile forms are portal forms and must not show No Access entries. Owner may also want them
  removed from the Matter Management app's form list (pre-existing configuration, not a defect of this task).
- **O2 trigger: platform roles hold Read/Write.** See the table above. Recommendation: leave them; run the role script
  with `-AcceptOtherRoles` once the owner agrees.
- **(e) no persisted N5 outcome.** Owner round 59 cut the stored state; the per-record notice is the outcome. No action
  recommended.

## Live steps (main session, after merge), in order

1. Deploy the BFF (the reader/enforcer canonical-id rule) by the standard BFF deploy.
2. `scripts/Deploy-WebResourceInline.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com -WebResourceName sprk_/scripts/noaccessentry_postsave.js -FilePath src/solutions/webresources/sprk_noaccessentry_postsave.js -WebResourceType 3`, then read back and compare.
3. `pwsh -File scripts/Deploy-NoAccessEntryForms.ps1` (dry run; `-PlanOut <dir>` saves the XML), then `-Apply`, then `-Verify` (exit 0).
4. Owner answers the O2 platform-role question, then `pwsh -File scripts/Set-NoAccessEntryRolePrivileges.ps1` (dry run), `-Apply -AcceptOtherRoles -AssignToUserPrincipalName <admin test user>`, `-Verify` (exit 0). Re-probe a user's access 3 times (privilege cache).
5. The manual live gate (criterion 11) below, then 143's gate 14.

Dry runs of both scripts were executed read-only against spaarkedev1 on 2026-10-07 (the form script with its library
version check relaxed, since 1.1.0 is not deployed yet): 6 changes planned (view, entry form, Organization form, Contact
form, site map, app component); quick create already off; role plan as in the table above.

## Manual live gate (criteria 3-11) - to run

| # | Check | Expected |
|---|---|---|
| a | As the access administrator: Organization, "+ New" on "Ethical walls on this organization" | Main form (not quick create), Object Organization filled; pick a Subject Contact; save; 143's notice; row in the subgrid |
| b | That contact on the SPA plane, on a matter referencing the organization (`sprk_assignedlawfirm1/2`) | Denied |
| c | "+ New" on "This organization denied"; Record Type = Project; pick a project in the dialog | Only Project / Matter / Work Assignment offered; the id is lowercase without braces; Object Organization cleared |
| d | A contact entry through the picker for record R; that contact on SPA/Teams | Denied on R (criterion 3: the stored id matches the reader) |
| e | Contact form, "+ New" on "This contact denied" | Subject Contact filled |
| f | Malformed: two subjects; no object; type without record; a typed `{GUID}` (normalised, saves); `abc` as id | Each refused with a notification naming the problem, except the braced GUID, which is normalised and saved |
| g | Site map "No Access Entries" | Opens "Active No Access Entries"; entries listed with names |
| h | Non-administrator (Core User only): open the Organization; Web API `GET sprk_noaccessentries` | No entries; **no empty NO ACCESS grid** (hidden or a permission message); the Web API read is a privilege error, not 200 [] ; no site map entry |
| i | Access administrator: create, update, deactivate an entry; try Delete | All work except Delete |
| j | BFF deny read after the role change | A walled contact is still denied on SPA |
| k | N5: an organization wall whose author lacks Write on some covered secure records | The notice names exactly those records |
| l | Dark mode | Form notifications and native controls adapt (no custom UI in this task) |

**No false empty (h).** If the platform renders an EMPTY list for a user without Read (rather than hiding the subgrid or
showing a permission message), the follow-up is the small `sprk_noaccess_sections.js` OnLoad probe from the POML
(hide the section unless a one-row read succeeds). Not built now: the POML builds it only on that observation.

## Tests

- BFF: 16 new cases (reader canonical-id theory x11, braced id denies nothing, upper case still denies; enforcer: braced
  and 32-digit ids are malformed and remove nothing, upper case is enforced). The table test double now models
  Dataverse's string equality (case-insensitive, trailing spaces) instead of `Guid.TryParse`, which had matched braced
  ids the real filter never returns.
- Form library: `src/client/shared/Spaarke.UI.Components/src/__tests__/noAccessEntryForm.test.ts`, 38 cases, the real
  script injected into jsdom (the `accessRibbon.*.test.ts` precedent): normalisation, every malformed shape, OnSave
  prevent/normalise, the picker (type filter, allowed type opens the dialog and writes the canonical id, a disallowed
  or unreadable type opens nothing), mutual exclusion, the name suggestion, OnLoad registration, the N5 notice.
- Justification for a test beyond the stated contract: the type-filter test pins the three evaluated roots, because an
  extra type would let an administrator save a wall no reader evaluates.

## Quality gates (task-execute Step 9.5)

Code review (own pass over every changed file; the full adversarial verifier is the main session's):

| Finding | Class | Resolution |
|---|---|---|
| The OnSave shape check also ran on **Deactivate** (save mode 5): a malformed entry (e.g. written by the Web API) could not be deactivated from its form, and deactivation is how a wall is lifted | F1 | Fixed: save mode 5 is never blocked; Reactivate is still checked. Tests added. |
| On LOAD, an entry whose object record no longer exists got a blocking field notification, which could stop the admin deactivating or correcting it | F1 | Fixed: a non-blocking WARNING on load ("walls nothing"); blocking only for an id typed by hand. Tests added. |
| `-Verify` exited 2 ("REFUSED … nothing changed") when the library was missing or old, instead of naming a gap | F2 | Fixed: reported as a gap (exit 1). Checked live. |
| The quick-create PUT path is unexercised on dev (already off there) | K2 | Hardened (OData annotations stripped before the PUT); the path only runs when quick create is found ON. |
| The `TableNoAccessListReader` test double matched with `Guid.TryParse`, so it returned braced rows the real filter never returns | F2 (found in passing, test-only) | Fixed: it now models Dataverse string equality. |
| Object Record Id column in views/subgrids shows the GUID text | K-class (accepted by owner round 59: no display column unless the picker needs it) | The suggested Name carries the record's name. |

ADR check: ADR-002 (no plugin/flow/business rule), ADR-003 (canonical-id rule fails toward "malformed, reported"; Deactivate
never blocked), ADR-006 (the web resource holds form event handlers only; the picker is the platform dialog, not custom
UI), ADR-010 (no DI change), ADR-021 (no custom-rendered UI), ADR-038 (doubles override wire seams; no
`Mock<HttpMessageHandler>`; jest runs the real script). No violation.

## CLAUDE.md section 11 (new surface)

- `NoAccessListReader.TryParseObjectRecordId` (internal static). Existing: `Guid.TryParse` at the two call sites.
  Extension: it IS the extension of the existing malformed-row rule, shared so reader and enforcer agree. Cost of doing
  nothing: a braced id is enforced on save while the veto never matches it, a wall that walls nothing on Teams/SPA.
- Two scripts. Existing: none covers this table's form, view, site map or roles (precedents copied:
  `Deploy-TodoSubgridsToElevenParentForms.ps1`, `Add-RegardingFilingPickerToForms.ps1`,
  `Set-SecureRecordOwnerRolePrivileges.ps1`). Cost of doing nothing: the owner's round-3b "add / see" requirement and O2
  stay unapplied.
- No new column, PCF, endpoint, DI registration or package.
