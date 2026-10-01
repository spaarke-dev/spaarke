# Session 27: what exists for the ethical wall (No Access) and Secure records, and what the owner asked for (2026-10-01)

This note records what exists today for ethical-wall (No Access) and secure-record visibility and management, against the owner's UX requests of round 3b ([session27-owner-decisions-and-research.md](session27-owner-decisions-and-research.md)). The findings come from a read-only research agent and were checked live against spaarkedev1.

## Owner requests (round 3b)

1. **Ethical wall:**
   - a red banner on any record subject to a No Access restriction, linking to the No Access list;
   - a way to ADD users, contacts or organizations to the No Access list of a record or an organization;
   - a way to SEE who is on it;
   - an answer on how No Access works at the ORGANIZATION level.
2. **Secure records:**
   - a red banner showing the record is secure, linking to Share or Manage Access;
   - a ribbon button to make a standard record secure.

## What exists today

### No Access (`sprk_noaccessentry`)

- **No banner anywhere.** There are zero `setFormNotification` calls in `src/`. No client code reads `sprk_noaccessentry`; only the server reader, `AccessibleRecordSetService`, `ExternalParticipationService` and the DI wiring do.
- **Nobody can author entries in practice.**
  - Only System Administrator, System Customizer and Service Writer hold Create/Write on the table.
  - The table is not in any site map, and no form has a No Access subgrid.
  - No BFF endpoint writes it. There are **0 entries** in dev.
  - The main form ("Information") holds **only the Name field**, so subject, object and reason cannot be entered. `sprk_objectrecordid` is free text, which is impractical to type in MDA.
- **Seeing entries needs Advanced Find or a URL.**
  - The views are the generated defaults (Name, Created On).
  - "Spaarke Core User" holds **Global Read** on the table, so every core user can read every entry, **including the Reason text**. That conflicts with task 143's rule that a refusal message must not reveal the reason.
- **The Organization form** has SYSTEM ACCESS (Access Permission Grant, Standing Grant), MEMBERS (contact memberships) and a hidden To Dos tab, and **no No Access components**.
  - Neither organization-as-subject (deny all active member contacts) nor organization-as-object (an ethical wall over every record that references the org) shows to users. Both exist in server code only.
  - Both lookups exist (`sprk_subjectorganization`, `sprk_objectorganization`), so standard subgrids are buildable. A subject-contact subgrid on Contact is buildable too, plus a subject-user one after 143.
- **Planned so far:**
  - **064:** add/remove endpoints, plus the user subject per its S27 amendment. Writes require Write on the record.
  - **067:** a No Access section in the Manage Access modal with a blast-radius confirmation and a user picker.
  - **143:** a user subject column, an enforce endpoint, a post-save script, and a job running at 5 min or less.
  - **Gap:** 064 has NO endpoint that lists a record's entries (record-scoped). The existing reader answers only "which of these records is this subject denied".

### Secure (`sprk_issecure`)

- **Not on any live form or view.** Only the default "Attributes" view references it.
  - **This corrects task 150,** whose step "make the form control read-only" has nothing to act on. The field-level security lock still applies.
  - The only on-form signals today are the header Owner (the Secure team) and a line inside the Manage Access modal (`AccessGrantModal.tsx:1170-1178`). The external SPA shows a "Secure Project" badge (`ProjectPage.tsx:508-511`).
- **Securing an existing record:** there is NO supported path.
  - Projects can be made secure only at creation, through the wizard toggle → `/provision-project` (`projectService.ts:280-284`, `CreateProjectWizard.tsx:691`).
  - Matters and work assignments have no path at all.
  - No client calls `/unsecure-project`.
  - Planned: 144 extends the endpoints to matters and work assignments, and 150 adds the ribbon button (enabled together with 148).

### Patterns to copy

- **Ribbon:**
  - `infrastructure/dataverse/ribbon/ProjectRibbons/Entities/sprk_Project/RibbonDiff.xml` ("Push Updates": an async enable rule plus a JS action that calls the BFF);
  - `src/client/webresources/js/sprk_fieldmapping_push.js` (cached async enable rule, confirm, BFF call, toast);
  - `sprk_registrationribbon.js` (confirm → BFF → notification → form refresh).
  - For a "Write only" enable rule, reuse `GET /api/v1/external-access/can-manage-access`.
  - Guides: `.claude/skills/ribbon-edit/SKILL.md`, `docs/guides/RIBBON-WORKBENCH-HOW-TO-ADD-BUTTON.md`, `docs/architecture/SPAARKE-SIDE-PANE-NAVIGATION.md` (an enable rule on a control that never renders never fires).
  - The work-assignment form ribbon is not in source control, so export it first.
- **Banner:** there is no form-notification precedent. The closest on-load and post-save scripts are the Matter form libraries and `sprk_kpiassessment_quickcreate.js` (`Spaarke.BffAuth`). The in-component banner precedent is the modal's Restricted and deny message bars (`AccessGrantModal.tsx:1145-1168`).
- **Opening the OOB Share dialog from a banner is NOT recommended.** As far as known (not repo-verified) there is no supported API, and it would bypass task 139's cap and 143's guard. Link to **Manage Access** instead.
  - The modal is hosted only inside the TrackingFieldTrio PCF (person icon, `TrackingFieldTrio.tsx:253-272`).
  - The standard `setFormNotification` banner is **text-only**, with no link (platform limit, not repo-verified).

## Disposition (main session, 2026-10-01)

| # | Addition | Carried by |
|---|---|---|
| 1 | A record-scoped No Access read: a record's entries, plus organization walls over its referenced organizations, with a summary for the banner. | **Amend 064** |
| 2 | Open the Manage Access No Access section directly, and show enforced or not per entry. | **Amend 067** |
| 3 | The access-status indicator: a red banner for Secure and No Access on project, matter and work-assignment forms, and a No Access banner on Organization and Contact forms. | **NEW task 153** |
| 4 | One shared "Access" ribbon group on the three root forms: Update Access (142) and Make Secure / Remove Secure (150). One ribbon file and one script, sequenced: 150 rebases on 142. | **Amend 142 + 150** |
| 5 | No Access management on the Organization and Contact forms (subgrids next to SYSTEM ACCESS/MEMBERS), plus a usable entry form, real views and a site map entry. | **NEW task 154** (absorbs 143's form dependency) |

**Owner answers, 2026-10-01:**
- **O2: ACCEPTED as recommended.** Remove Read on `sprk_noaccessentry` from Spaarke Core User. A dedicated access-administrator role holds Read/Write. Everyone else sees the banner, and Write-holders see a record's entries through Manage Access (the BFF reads on their behalf).
- **Q1 (blank Secure flags), DECIDED 2026-10-01:** a one-time cleanup sets every NULL `sprk_issecure` to No (live: 9 projects, 18 matters, 11 work assignments), and the column default becomes No. The cleanup is an operator script that records the before/after counts and the record ids, ships in the solution so new environments get the default, and runs before the banner publishes (task 153).
- **Q2 (banner on organization records), DECIDED 2026-10-01: option (A).** The banner's summary route alone passes a Read-only `sprk_organization` → `sprk_organizations` resolution to its filter, with a comment citing both invariants. This is recorded as a project-scoped §6.5 path-A exception in the design and the PR. The shared EntityAccessFilter map is never widened.
- **O1: FINAL (2026-10-01).** This supersedes the "O1 decided" block below.
  - The owner: "a text-only banner is sufficient; no banner in the PCF; no app level banner."
  - **Banner:** the standard red `setFormNotification` text banner on the five forms.
  - **The access-permission pill** shows "Secure" in red, for both secure and secure+Restricted. "Secure-Restricted" would not fit the pill width without changing the trio spacing.
  - **The Manage Access modal's message bar** shows "Secure – Restricted" plus the explanation, as it does for Restricted today.
  - **No "No Access (n)" tag. No clickable indicator in the PCF.**
  - Live 2026-10-01: NULL `sprk_issecure` on 9 projects, 18 matters and 11 work assignments. The backfill is put to the owner (153 escalation g).
- **O1, earlier "DECIDED" (superseded):** The owner rules out a full-width banner ("not a full width"), and NO app-level `addGlobalNotification` banner.
  - **Where it lives:** the indicator lives INSIDE the access control (TrackingFieldTrio), at the top of the right-hand 1/3 column of the Overview tab (section `overview_tracking`, verified live on the Matter and Project main forms).
  - **(i) Compact red banner:** a red Fluent MessageBar at the top of the control, reading "Secure record · Open Manage Access" or "No Access restrictions apply · View list". The link opens the Manage Access modal at the right section.
  - **(ii) Access-permission tag:** on a secure record it shows **"Secure"** in red, or **"Secure · Restricted"**. On a secure record its only choices are those two, because Secure already limits contacts to named grants. Securing and unsecuring stays with the ribbon button (task 150).
  - **(iii) No Access tag:** a red **"No Access (n)"** tag, shown only when entries apply.
  - **Organization and Contact forms** get the same compact control in their new No Access section (task 154).
  - **Data:** comes from 064's record-scoped read plus the secure flag. Fail closed: if the read fails, show "Access status unavailable", never "not restricted".
  - The researcher's findings on the alternatives (`addGlobalNotification` is app-wide and persists across navigation, the bell is a per-user inbox, `Notify()` has no links, `setFormNotification` is text-only) are in researcher memory `mda-clickable-form-banner-options-2026-09-30.md`.
- **O1, superseded detail:**
  - The owner believes OOB notifications can carry links (the `appnotification` table, or a "notify" command). Under research: `Xrm.App.addGlobalNotification` with an action, field `control.addNotification` RECOMMENDATION actions, `appnotification` actions, and Power Fx `Notify`.
  - The Manage Access PCF (AccessGrantModal inside TrackingFieldTrio) is the host for the No Access list (task 067). It needs modification to be openable directly from a banner and an indicator (tasks 153 and 067).

**Original open questions (kept for the record):**
- **O1.** Must the banner itself be clickable? A clickable banner means it is rendered inside the TrackingFieldTrio control rather than the standard text-only form banner. Recommended: both, the standard banner at the top naming the action, plus a clickable indicator in TrackingFieldTrio that opens Manage Access.
- **O2.** Should the No Access list (including Reason) stay readable by every core user (Global Read today)? Recommended: no. Remove Read from Spaarke Core User. Grant Read/Write to a dedicated access-administrator role. Everyone else sees the banner, and Write-holders see the entries through Manage Access (the BFF reads on their behalf).
- Organization-level wall authorship is already settled by N5, applied per record.
