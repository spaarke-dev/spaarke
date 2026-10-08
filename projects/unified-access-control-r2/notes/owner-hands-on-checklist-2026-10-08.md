# Owner hands-on checklist (dev), 2026-10-08

These are the live checks that need a person in the app, Teams/SPA, Word, or a second account. Agents run everything else and read back the results. When you're done, tell the main session which items passed or failed (by id), and it records them and closes the tasks.

- Compiled from each task's POML and notes, and from the batch 4/5 live-gate records.
- Dev app: https://spaarkedev1.crm.dynamics.com → Matter Management.
- External SPA: https://green-dune-0c4f1221e.7.azurestaticapps.net.
- Where a step says "the agent", tell the main session when you reach it, so the agent can seed or read back.

## 1. Matter Management app (forms)

**154: No Access entry forms.** A No Access entry is one row in the "No Access Entries" table: "this person/organization is walled off from this record". The forms are where you create those rows: from an Organization, from a Contact, or from the site-map list.
- (a) Open an Organization. On "Ethical walls on this organization", click + New. **PASS:** the full form opens with Object Organization filled. Pick a Subject Contact and save. A notice appears and the row shows in the subgrid.
- (c) On "This organization denied", click + New, set Record Type = Project and pick a project. **PASS:** only Project, Matter and Work Assignment are offered.
- (e) Open a Contact. On "This contact denied", click + New. **PASS:** Subject Contact is filled.
- (f) Try to save bad entries: two subjects; no object; a type without a record; `abc` as the id. **PASS:** each is refused with a message naming the problem.
- (g) Find "No Access Entries" in the left nav. **PASS:** it opens "Active No Access Entries". If it's missing, add the table to the app in the app designer; the script couldn't add it.
- (i) Create, update and deactivate an entry, then try Delete. **PASS:** all work except Delete.
- (l) Switch to dark mode. **PASS:** the form adapts.
- (m) Check the three NO ACCESS subgrids. **PASS:** no "Add Existing" button; "+ New" is present.
- (n) Save an entry, then change its Record Type. **PASS:** the lookup opens once, and you see one notice.
- (o) Open the braced-id entry the agent seeded (ask for the id). **PASS:** a warning shows, the form is not dirty, and Modified By is unchanged after 60 s.
- (p) Open a normal entry and wait 60 s. **PASS:** nothing is saved.

**143 gate 14: No Access for internal users.** Use the throwaway secure project the agent provisions; U = testuser1.
- (i) Manage Access → "+ User" testuser1. **PASS:** shared.
- (ii) Create a No Access entry naming testuser1 on that project. **PASS:** the share is gone from Manage Access.
- (iii) "+ User" testuser1 again. **PASS:** refused, with a message naming the No Access list.
- (iv) Deactivate the entry. **PASS:** the share does not come back by itself.
- (v) With a second active entry, use the Share dialog on the project to give testuser1 access. **PASS:** after the next job run, the agent confirms testuser1 is denied. (Or accept an agent-made Web API share as the stand-in, as round 68 did for G149-2.)

**142: Assigned-To auto-grants.** Use a throwaway non-secure matter.
- (i) Set Assigned Attorney 1 to an external contact and save. **PASS:** Manage Access lists the contact at Collaborate, expiring +90 days.
- (ii) On a matter testuser1 can't read, set Assigned to testuser1's contact and save. **PASS:** the agent confirms the share.
- (iii) Remove the auto-grant in Manage Access, re-save, and wait one job run. **PASS:** it is not re-created.
- (iv) Clear the field and save. **PASS:** the grant is revoked.
- On a secure project, set Assigned Paralegal 1. **PASS:** a suggestion row with Grant/Dismiss appears, and nothing is granted until you click Grant.
- (v) Create Matter wizard with an Assigned Attorney. **PASS:** the grant exists immediately.

**150: Make Secure / Remove Secure.** Use throwaway records.
- Open the Access flyout on a secure project. **PASS:** Update Access and Remove Secure are listed; Make Secure is not.
- Make Secure on a non-secure matter that has a file. **PASS:** the record reads Secure, and the file moves to the matter's own container.
- Check dark mode.

**147 / 168 / 169: filing children (to-do, event, report card).** On to-do, event and communication forms:
- pick a matter;
- pick a communication filed under a matter;
- "+ New" from the Matter, Communication and Invoice To Do subgrids;
- SmartTodo "New task";
- re-file a saved record in and out (select, then clear).
- **PASS:** each save succeeds, and the agent reads back the matter link (and the owner on secure records).
- After the agent applies the 147 ribbon: on secure and ordinary records, the "+ New" commands behave as specified.

**114: Restricted records.**
- Open a Restricted project, matter and work assignment, then select one in a grid. **PASS:** Share is hidden.
- Right after the agent seeds an external-flagged share, open Manage Access. **PASS:** the "External user — no access" row shows.

## 2. Wizards, Console and Compose

- **162 (h):** run, as a normal user:
  - the Document Upload wizard AI summary;
  - the DocumentEmailWizard summary;
  - the SemanticSearch combined summary;
  - HistoryOverlay "set related";
  - promote a reviewed document.

  **PASS:** each one completes.
- **162 (l):** Playbook Library / Analysis Builder → "create analysis". **PASS:** completes.
- **163 (d):** upload through the Document Upload Wizard on a matter. **PASS:** the agent sees it indexed.
- **164:**
  - (f) a Compose session gets a reply;
  - (g) the document AI summary and the DocumentEmailWizard summarize step load;
  - (l) a Compose toolbar action works on an unsaved document;
  - (m) the Console works on a contact record.
- **166 #27:** run the Document Upload wizard and one Create wizard as a non-admin. **PASS:** the document gets its file.
- **171 J1:** upload a file with the same name twice. **PASS:** only "Keep both" is offered.

## 3. Word / Outlook (task 171)

- **B1:** open a document in Word for the web, then in desktop Word. **PASS:** both are editable.
- **G:** Word add-in "save new version" on a secure-project document. **PASS:** a new version is saved. Then Outlook "save attachment to project". **PASS:** a document is created.
- **H:** open a document in Compose, edit and save it in desktop Word. **PASS:** the change is detected.

## 4. External SPA (CIAM sign-in)

- **137:** signed in as the CIAM test user, while the agent deactivates or reactivates a matter, the contact and an org grant. **PASS:** access disappears or returns on the next request, and a deactivated contact's next sign-in is refused.
- **140 (a)–(h):** contact-side Grant Access. Needs 4 CIAM identities: a Collaborate contact, a colleague in the same organization, a contact in another organization, and a View Only contact.
- **142 (i):** the contact from 142 (i) above can open the matter.
- **157:** in Teams, open Projects, Documents, Invoices, Work Assignments and Matters. **PASS:** no view selector, columns shown, sorting and filters work.
- **171 E:** a contact with a grant on the secure project can upload and view; a contact without one is refused.
- **105:** the agent's 250-document project shows no "truncated" notice.

## 5. Admin actions only you can take

- **Test containers:** run `notes/Remove-TestContainers.ps1` after the agent confirms all 21 ids are unreferenced.
- **165:** grant the SPE Admin app role to testuser1 and approve the write probes on throwaway data. Then **165 (d)** in SpeAdminApp: list, open and edit your config, and start a bulk delete on a throwaway container.
- **171 D3/B2/J4:** create, enable and disable a test user W; provide an external-flagged licensed user's session; self-register a demo user.
