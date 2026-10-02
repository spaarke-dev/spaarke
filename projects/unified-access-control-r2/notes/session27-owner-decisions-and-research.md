# Session 27: owner answers on C4/C7/C9/C10 and supporting research (2026-09-30)

This picks up from [session26-uac-defects-and-synopsis.md](session26-uac-defects-and-synopsis.md). The owner's words are quoted; everything else comes from three read-only research agents and live Dataverse queries (spaarkedev1), and every claim was checked against code.

## Owner answers, 2026-09-30, verbatim in substance

- **C4.** "if the user has write access, then they can share (OOB functionality) and grant access to users/contacts/organizations". "No re-share" and "grant what you hold" apply only to a contact (or organization) holding **View** by virtue of a grant. "A contact user with collaborate or full access (or systemuser with write access) should not be prevented from using the 'grant access' feature." Apply the secure-record rule to regular access too: Collaborate and Full Access carry grant-access ability. A Write-holder changing `sprk_accesspermission` and then adding external users is acceptable.
- **C7.** The owner asked for an explanation (below). The owner's model: internal workers are matched automatically, external users arrive by invitation, and contact-based users get SPA/Teams only, never MDA.
- **C9.** "if a systemuser has access in dataverse then they have access in Teams/SPA; if the contact user has access by virtue of the 'grant access' function, then the contact user only has access to those records to which granted access; and the privileges should be the same as set in the 'grant access'. We do not have customers so do the full fix however that needs to be done."
- **C10.** The owner understands the Secure Record BU as a no-user BU where access is granted only explicitly. Part 2 is **CRITICAL**: documents, communications, memos, events and to-dos must be secure with the same contact access as the main record, via a server-side function.
- **#1038.** The Related-To lookup must show a systemuser the secure projects they can access. The document inherits secure settings by association. **Add the resolver hardening.**
- **#1037.** **Option A**: show the record disabled, with the reason.
- **Notifications.** Low priority. Team-owned records must not notify all team members; use another surface (e.g. Daily Briefing). This needs a design discussion.
- **All 12 defects.** The owner asked how every one is addressed (plan below).

## Owner answers, round 2 (2026-09-30), BINDING

1. **The create-document hole** (`POST /api/v1/documents` owner/id): word-add-in-r1 fixes it (#1043, PR #1045).
2. **`sprk_issecure` is LOCKED and managed through the endpoint.** Field-level security plus endpoint-only writes.
3. **C4:** agree with the recommendations. Clarified model, confirmed:
   - **Restricted** is a grant-access level on ANY record: no contact-based (non-systemuser) access at all; internal users unaffected.
   - **Secure** is a separate flag: a secure record MAY have contact access as long as it is not Restricted. For contacts, only named, direct grants count, which is why Secure implies Limited for contacts.
4. **C7:** build the proposal. Link by Entra oid stored in `sprk_externalobjectid`. A first-sign-in email bind happens only for tenant members (not guests) with exactly one match; after that, oid only. Otherwise create the contact keyed by oid. A collision is refused and flagged. This completes SPA-r2 FR-11 / task 023.
5. **C9:** Do NOT lose (a) the No Access List or (b) the "Assigned To *" access. "The 'Assigned To *' auto grants were one of the core reasons for the UAC — this needs to continue being a feature."
6. **C10:** add the Part 1 enforcement (named non-default owner team). Part 2 is extended across EVERY "what's missing" item (all writers, client-created children, owner-role privileges, backfill and transitions, secure matters and work assignments, internal sharees' access to children, contact coverage for children).
7. **#1038:** yes, harden the resolver.
8. **#1037:** Option A, show disabled with the reason. The word-add-in-r1 issue is theirs to implement.
9. **Notifications:** target **Created By** and **Assigned To**. A team-owned record does not fan out to team members.
10. Answers to the questions:
    - **Q1:** cap every grant at the grantor's own level.
    - **Q2:** a contact grants only to contacts of their own organization, at or below their own level, never organization-wide.
    - **Q3:** first-sign-in link approved.
    - **Q4:** the No Access List applies to internal users too, on secure records.
    - **Q5:** the "Assigned *" contacts are **automatically granted Collaborate** by a function that adds the contact to the grant-access list, so an operator can remove it.
    - **Q6:** communications inherit the parent's access permission, and their own field is retired.
    - **Q7:** proceed with tasks, issues, re-opens and the parallel batch.
    - **Q8:** use the "for" person, which is the existing `sprk_todo.sprk_assignedto` (a CONTACT lookup, verified live), plus Created By.
11. **The Secure Record Owner role's missing child-table privileges must be ACTIVELY TRACKED** as a UAC-r2 issue and task, not left in the peer's notes.

Live fact relevant to Q8, Q4 and Q5: only **1 of 8** active interactive systemusers in dev has `sprk_primarycontact` set. Anything that matches an internal user to a contact (Assigned To in the briefing, No Access for internal users, auto-grants to internal staff named in contact fields) needs a reliable user↔contact link, built in the C7 identity work: `systemuser.azureactivedirectoryobjectid` ↔ `contact.sprk_externalobjectid`.

## Owner answers, round 3 (2026-09-30) — consolidated decision list. BINDING.

**All 52 consolidated decisions** (`raw/session27-owner-questions.json`, plus D2) are ACCEPTED as recommended, EXCEPT the items below.

- **A1.** Rule 5 governs Assigned-To auto-grants: they are ALWAYS Collaborate, with no grantor-level cap.
  - The owner questioned whether rule 1 ("cap every grant at the grantor's level") was ever their rule.
  - Record: round 2 Q1 was answered "cap grants and grantor level". That answer was given to the main session's question "can a Collaborate holder grant Full Access? recommend no".
  - Whether that cap stays for MANUAL Grant Access is **awaiting the owner's confirmation**. Until then, 139/140 treat it as an escalation, not settled.
- **T2.** The live test identity is `test.user@demo.spaarke.com`. Verified 2026-09-30: an Entra **Member**, enabled, **no licences**, no systemuser, no contact. A correct Type-2 test.
- **D1, clarified:**
  - (1) A user with access in the MDA has access in Teams/SPA. This works because Dataverse's own answer decides for systemusers when the FR-20 flag is on. Nobody needs a grant to see what Dataverse already lets them see. Task 036 amendment 2 was rewritten: flag-off is unchanged, so there is no under-grant.
  - (2) "Created By" decides who a record is FOR (briefing, notifications). It never decides who can OPEN it; access stays Dataverse's answer.
- **R3 / R4.** Access changes must take effect in MINUTES, never hourly:
  - immediate on save (the form calls the BFF);
  - an **"Update Access" ribbon button** on project, matter and work assignment forms that re-syncs that record on demand;
  - the background job is only a safety net, running at ≤5 min.
  - This applies to Assigned-To grants (142) and No Access enforcement (143).
- **S5, REVERSED.** Never create a record nobody can see. The owner's rule: **a secure record MUST always have at least one user who can see it.**
  - Inbound email whose secure parent can't be determined is HELD: retried, then left unprocessed in the ingestion queue, with an administrator alerted. Nothing is created until it is resolved.
  - The same invariant binds unshare (the last person cannot be removed), No Access enforcement (it may not remove the last person) and provisioning (task 133).
- **A2, REVERSED.** Standing grants and organization access STAY, as they work today, on both sign-in types. Assigned-To grants are ADDED alongside them. Nothing is retired.
- **F11.** Only one account holds "Spaarke Basic User": the owner's guest account `ralph.schroeder_hotmail.com#EXT#`, in the "Spaarke" BU (verified 2026-09-30). That is why it reads secure records. It is the owner's dev/testing account, so the reach is not structural.
- **G5.** Clarification only: "it" is the **sprk_invoice record** created when a user confirms AI-extracted invoice data. It is created as the user.
- **A7, REVERSED.** Office quick-create defaults the matter/project "Assigned To (internal)" to the maker's contact, and the field stays editable. The maker already has access through BU-team ownership; this makes the record "for" them in the briefing and issues the Assigned-To share.
- **N2 / N5 / N6.** The owner did not follow the "wall" wording (= a No Access List entry). These are re-explained and await the owner.
- **F3.** The owner answered "a systemuser with update rights". The main session flagged that this includes every Collaborate colleague. **Awaiting confirmation.**

## Owner answers, round 3b (2026-10-01). BINDING.

- **A1:** the grantor-level cap STAYS for MANUAL Grant Access ("as you've presented it, then yes that should be the cap"). A Collaborate holder cannot grant Full Access. Assigned-To auto-grants follow rule 5: always Collaborate, uncapped.
- **G5, refined.** Confirming an invoice works like this:
  - the BFF checks AS THE USER: Create on sprk_invoice, AppendTo on the matter and vendor, Write+Append on the document;
  - then the APP creates the invoice, OWNED BY THE TEAM (RecordOwnershipResolver, record-first from the matter), never user-owned. This is the owner's point: "same rule that assigned to the bu/team not individual".
- **N2:** hide the record in Teams/SPA. **N5:** as recommended (enforce only when the entry author has Write on the record; otherwise "not enforced" plus a notice). **N6:** as recommended (refuse to provision, with a message).
- **F3:** as recommended. Removing Secure is limited to Full Access holders plus the creator, enforced server-side in the unsecure endpoint. Securing stays open to Write-holders.
- **F5, superseded by the owner's ask:** a **ribbon button** changes a standard record to secure.
- **NEW owner UX requirements** (research running; tasks to be assigned):
  1. **Ethical wall (No Access):**
     - a red notification banner on any record subject to a No Access restriction, linking to a No Access list modal;
     - a way to ADD users/contacts/organizations to the No Access list of a record or an organization;
     - a way to SEE who is on it.
     - The owner also asks how No Access is enforced and surfaced at the ORGANIZATION level (does sprk_organization carry the components?).
  2. **Secure records:**
     - a red notification banner showing a record is secure, linking to Share/Manage Access if possible;
     - a ribbon button to change a standard record to secure.

**Relayed by word-add-in-r1, 2026-10-01** (owner's words quoted by the peer; the UAC owner round 3 had already accepted I1 directly):
- **F1 DONE live.** All 32 privileges outside `config/secure-record-owner-role.json` were removed, taking the role from 40 to 8 privileges, all Read at Basic.
  - Proven by 16 Secure-team-owned creates (project, matter, work assignment, document), all successful, plus an `sprk_invoice` control that was refused each poll.
  - The before-snapshot is saved for reversal. `-Verify` exits 0.
  - ⚠️ `AddPrivilegesRole` RE-INJECTS the SharePoint four, so any `-Apply` (tasks 145/146 extending the set) must re-run the guide §5.4 strip afterwards.
- **F11:** the owner will change the hotmail #EXT# account themselves ("this is in dev and we'll change this"). There is NO census exception; clause 1 must pass once the owner changes it.
- **#1037** is word-add-in-r1 task 084 (option A).

## Owner answers, round 4 (2026-10-01). BINDING.

These answer the seven questions raised after batch 2 (tasks 141, 144, 145, 155, 134). They are recorded verbatim in substance.

1. **Root-BU users with Deep read (144 escalation b; F11's premise).** "Let's not worry about these users. This is dev and these are test users. If you need other test users at different access levels, we can create them."
   - **How to apply:**
     - No role, depth or BU change is made for Chelsea Friez, Lori Witkin or the hotmail guest.
     - The census job and the NFR-05 live test will keep REPORTING them. In dev that is an accepted, known finding, not a failure to chase. The census code still treats it as a finding everywhere; no exception is coded.
     - When a live gate needs a user at a specific access level, ask the owner to create one rather than reusing these.
   - **Mechanics, for the record:** "Spaarke" is the ROOT business unit. Secure Record, Spaarke Business Unit 1, Demo, Dev 1 and Test 1 are its children, so Deep read held at the root reaches the Secure Record BU. The owner places users by hand.
2. **Project 65a3fab2 "Test New Matter via Workspace" (144 escalation c).** "It is just a test record, but you can provision it to test the secure project capability."
   - Provision it into isolation as the live fixture for 144.
3. **Assign cascade (144 escalation a).** Accepted: team, sharepointdocumentlocation and sharepointdocument. The migration passes `-AcceptedAssignCascade team,sharepointdocumentlocation,sharepointdocument`.
4. **Alternate key vs FLS on contact.sprk_externalobjectid (141 notes §9).** **B2**:
   - FLS stays on `sprk_externalobjectid`.
   - The alternate key moves to a new unsecured mirror column, `sprk_externalobjectidkey`, written with the same oid in the same request as every bind and create.
   - Every read keeps using the secured column.
5. **Stale core-ancestor stamp on a child filed under another child (155 escalation trigger 2).** **(b)**: re-stamp the children whenever the intermediate record is re-filed. This is a server-side cascade in the re-parent paths, plus fix-up for writes outside the BFF (ADR-002 WP-5). It also closes the access over-grant from the same stale copy (task 051 §1). The fail-closed refusal (c) remains the holding position only until (b) lands. Scope includes the Office to-do "carrier" shape (a direct core link plus a document/communication regarding) and the agreement, budget and report-card intermediates.
6. **Live steps on dev.** "Yes, can run it." Approved for 141, 144 and 145's live gates:
   - **141:** schema, FLS, the acct optional claim on the BFF app registration, the workforce tenant setting, and the reconciliation job.
   - **144:** create the named team, migrate, and provision 65a3fab2.
   - **145:** the role apply, the §5.4 strip, and the probes.
   Each step runs its dry run and its verify mode.
7. **External grids (134 open item D1).** Yes: set `showViewSelector=false` on the external SPA grids, so the column allow-lists can shrink to what the grids show.

## Live facts verified this session

- `sprk_accesspermission` is **Standard 100000000 / Limited 100000001 / Restricted 100000002** on sprk_project, and identical on sprk_matter and sprk_workassignment.
- **No field-level security** covers `sprk_accesspermission` or `sprk_issecure` (0 `fieldpermission` rows). Anyone with Write can change either.
- The Share privilege on project, matter and work assignment is held at **Deep** by "Spaarke Core User" and "Spaarke Office Add In User".
- `prvActOnBehalfOfAnotherUser` is held via **System Administrator**, which the BFF app users `# mi-bff-api-dev`, `SDAP-BFF-SPE-API` and `# Spaarke DMS-SPE Dev 1` hold. No Spaarke role holds it. Checked by role, not by a live call.
- dev has 1 secure project with its own container, and **0** documents linked to any secure project, matter or work assignment, so #1038 has had no data impact in dev.
- 🔴 **Master and the dev BFF: `POST /api/v1/documents` accepts a client-chosen owner team and record id.** Evidence: `Spaarke.Dataverse/Models.cs:29,49` has no `[JsonIgnore]`, and `DataverseServiceClientImpl.cs:303-306` writes the team as owner. The endpoint is auth-only. It is fixed only on word-add-in-r1 `5d870b898`. The peer was told 2026-09-30.

## C4 research

- **Only systemusers can grant today.** The gate is `DelegationRuleFilter`, which checks caller-OBO **Write** (`:86`, `:170-179`). ShareAccess is never consulted (`DelegationRuleCharacterizationTests.cs:43-51`). This already matches the owner's rule for systemusers.
- **Contacts cannot grant at all.**
  - CIAM tokens cannot reach `/api/v1/external-access` (`AuthorizationModule.cs:46-71,334-338`).
  - `/api/v1/external` has no grant route.
  - The portal's Full-Access "Invite User" button (`ProjectPage.tsx:279-307`) posts to a route the CIAM token cannot authenticate to, and `/invite` writes no grant anyway.
  - The owner's rule therefore needs a **new contact-side grant path**.
- **"Grant what you hold" exists only on `/share-user`**, as an intersect-and-narrow (`InternalShareEndpoints.cs:261-311`). `/grant` and `/invite-and-grant` have no cap and allow org-wide grants (`GrantExternalAccessEndpoint.cs:84-87,95-97`).
- **Share levels:**
  - POA levels carry no ShareAccess: View=Read, Collaborate=R|W|Append|AppendTo, Full=+Delete (`RecordShareLevels.cs:19-22,51-53`).
  - Only the secure-provisioning **creator** gets ShareAccess (`ProvisionProjectEndpoint.cs:169`); colleagues don't (`:181`).
  - The rationale comments (`:164-174`), UAT U-3, and "No re-share at any level" all state the **opposite** of the owner's new rule and must be rewritten.
  - For OOB (MDA) sharing to follow the owner's rule, Collaborate and Full Access need ShareAccess added.
- **Access-permission misalignments:**
  1. **Limited does nothing anywhere.** No server code uses 100000001, and the modal only checks restricted.
  2. **Limited's intended meaning is what FR-22 Secure suppression already does.** That meaning (teams-app-r1 spec:63/180 "Option A") is direct named grants only, no standing or organization grants.
  3. **Restricted disables "+ User" in the modal** (`AccessGrantModal.tsx:1197..1307`), although its banner says system users may have access and the server keeps it.
  4. **Restricted is not checked at grant-write time, and is not applied on CIAM** (C1).
  5. **The modal allows "+ Organization" on secure records.** It is suppressed on the workforce plane but honoured on CIAM.
  6. **The Manage Access gate ignores both flags** (`RecordAccessGateEndpoint.cs:105-107`).
  7. **`sprk_communication` has its own `sprk_accesspermission`, which the BFF never reads** (`ExternalParticipationService.cs:549-553`).
  8. **The access-permission pill is never disabled** (`TrackingFieldTrio.tsx:335-367`).

## C7 research

- **The user:** a customer **employee without a Power Apps licence (Type 2)**, signing in with company SSO.
  - Required by owner decisions: teams-app-r1 D11 / "Option B"; SPA-r2 Legal Front Door; UAC-r2 B-4; ADR-028 A2.
  - SPA/Teams only. They can never use MDA.
- **The owner's internal/external split was decided in SPA-r2.**
  - Internal unlicensed staff use company SSO with no invitation, and modules come from Entra App Roles.
  - A contact is created lazily, **keyed by Entra oid** (SPA-r2 FR-11, task 023).
  - Externals use the CIAM invitation.
- **SPA-r2 task 023 was never built.** What's live is the teams-app-r1 shortcut instead: an unverified email claim matched against `contact.emailaddress1`.
  - **In dev**, `contact.azureactivedirectoryobjectid` does not exist, so every Type-2 employee gets 403.
  - **Where the column exists but is empty**, the email match is a hijack path.
- **The CIAM invite already binds by oid**, written to `contact.sprk_externalobjectid` at invite time. SPA-r2 already approved widening that column to hold workforce oids too.
  - Caveats: one binding per contact, so an employee who is also an external user collides (task 023 escalation). The CIAM email fallback uses `$top=1` with no ambiguity check (`ExternalParticipationService.cs:423`).

## C10 research

- **`RecordOwnershipResolver`** was built by **word-add-in-r1 task 080** for invariant **I-6**: server-created rows were owned by the app user in the root BU and were invisible to child-BU users.
  - It resolves the **target record's BU default owner team**, falls back to the acting user's, and refuses when a named target can't be resolved.
  - Record-first resolution sends a child filed to a secure project to the Secure Record team (`RecordOwnershipResolver.cs:161-172`).
  - There are **zero callers here**. It is wired only on the peer branch, for Office paths.
- **Gaps before children of secure records are secure:**
  1. **About 20 writers are unwired** (#1034): external-portal document, event and to-do creates, all communication writers, `/api/v1/events`, `TaskActionCore`, `TodoGenerationService`, `sprk_analysis`.
  2. **Client `Xrm.WebApi` creates** (wizards, Notepad memos) are user-owned, and no fix-up channel exists.
  3. **"Secure Record Owner" has Read on root tables only.** Dataverse refuses the assign on child tables (`SECURE-PROJECT-ENVIRONMENT-SETUP.md:161-168`).
  4. **No backfill** at provision or unsecure time.
  5. **Internal sharees of the parent lose sight of re-owned children** unless shares cascade or the one-hop inheritance (tasks 055/056) is built.
  6. **Only the root tables carry `sprk_issecure`,** so a child's securability must come from its parent lookup. `sprk_document` has two project lookups.
- **Secure matters and work assignments are never re-owned.** Only the project provisioning path does it.
- **Contacts reach children via app-only reads keyed on the parent**, so Dataverse ownership doesn't affect them. Coverage:
  - documents: project, matter, work assignment;
  - invoices: yes;
  - to-dos: yes;
  - events: **project only**;
  - communications, memos, analyses: **none** (task 056 open).
- **`CoreAncestorResolver`** (tasks 050/052) stamps the `sprk_regarding{core}` link used for inheritance. It is not ownership.

## Peer follow-through (word-add-in-r1, 2026-09-30)

- The documents owner/id hole is filed as **#1043**.
- The Daily Briefing team-owned to-do drop-out is filed as **#1044**. It carries an owner decision: caller-owned to-dos, a "for" user column, or accept the gap until the UAC "who is notified" design lands. `sprk_todo` has no other user-typed "for whom" field.
- Both fixes ship in **PR #1045** (word-add-in-r1 → master), described as a security merge for #1038 and #1043. The PR's "Before merging" section asks the owner to decide #1044 first.
- The Secure Record Owner child-privilege gap is recorded in their note §6.12. It fails closed until UAC C10 lands.
- The membership-widening half of the briefing issue stays with UAC (workstream G).
- **The Secure Record Owner child-privilege gap is now OWNED by word-add-in-r1 task 082 / GitHub #1046** (owner instruction to that session, 2026-09-30).
  - **Both projects' notes had pointed at the other,** so nobody owned it; both notes are now corrected.
  - **The live role (e4ebabd9-…) holds 36 privileges, not the guide's 3:**
    - sprk_document: all 8, at Basic;
    - **sprk_todo / sprk_communication / sprk_event / sprk_memo: NONE;**
    - project / matter / work assignment: all 8;
    - the SharePoint four: at Global.
    - This is drift from the setup guide §5.1/§5.4, and UAC-r2 has no record of adding it; removal is an owner call.
  - **Agreed split:**
    - 082 grants on the existing role and edits the guide's privilege sections, codifying the child-table set in ONE place.
    - **UAC-r2 owns `SecureBuRoleDepthAssertion`**: task 144 rewrites clause 2 for the named team, and UAC adds the "role lacks Read on a codified table" clause.
    - UAC task 146 extends the codified set to every child table it re-owns.
  - **To-do "for whom" matching (#1044), agreed 2026-09-30:**
    - **Created By is the APP for every BFF-created to-do.** Evidence: "Invoice Pending: INV-005" createdby = `# mi-bff-api-dev`; the Office CreateTodoAsync is app-only (`OfficeService.cs:2951`).
    - **word-add-in-r1 task 083** defaults `sprk_assignedto` to the caller's contact when no assignee is chosen (after UAC 141 provides the link).
    - **UAC task 152:**
      - matches Assigned To = the user's contact;
      - counts Created By only when createdby is a HUMAN systemuser (`applicationid` null);
      - makes server generators (TodoGenerationService, TaskActionCore, other #1034 to-do writers) set Assigned To: the triggering user's contact, else the parent's responsible internal contact.
    - Impersonated creates were rejected, because they would widen roles.
    - 083 is the Office path ONLY. TaskActionCore (AI playbook code) and the other generators stay with UAC 152.
  - **PR #1045 is merging** (merge commit), which brings RecordOwnershipResolver wiring, `DocumentAssociationMap.ToLogicalName` and the #1038/#1043 fixes to master.
    - UAC must merge origin/master into this branch before starting 151 (resolver hardening) or 146.
    - 082 (role grant plus guide) follows on a new PR, and will send the codified child-table set's file path.
  - **082 is LIVE in dev (2026-09-30).** The Secure Record Owner role went from 36 to 40 privileges: +Read at Basic on todo, communication, event and memo, with 0 removed. The negative refusals were captured first, then a positive probe created a secure-team-owned to-do (probes deleted).
    - **The codified set is `config/secure-record-owner-role.json`** (schemaVersion 1, lands after #1045): `tables[]` holds `{logicalName, privilegeName, kind root|child, reason, evidence, addedBy}`.
    - **UAC's NFR-05 clause** reads `tables[].privilegeName` and asserts depth mask 1, the same rule as `scripts/Set-SecureRecordOwnerRolePrivileges.ps1 -Verify`.
    - **UAC 146 extends the set** via its `howToExtend` block, recording the refusal first.
    - The guide's privilege rows were updated by 082, and UAC 144/150 rebase onto them.
  - 🔴 **The NFR-05 live census run by the peer (2026-09-30):**
    - clause 2 PASS (0 members); clause 3 PASS (1 holder);
    - **clause 1 FAIL:** the human account `ralph.schroeder_hotmail.com#EXT#` holds "Spaarke Basic User", whose prvRead on sprk_project/sprk_matter reaches the Secure Record BU by depth. So that account reads secure projects and matters. The finding is pre-existing and outside 082's changes.
    - Also: plain DefaultAzureCredential resolved only EnvironmentCredential there; setting `AZURE_TOKEN_CREDENTIALS=AzureCliCredential` made the census run. This is a runbook fix for the census.
  - **UAC task 145 is therefore reshaped:** a cross-project dependency on 082/#1046, plus the NFR-05 clause, plus verification for the named team. It is no longer a duplicate privilege grant.

## Notifications research

- **`MembershipChangedEvent` does not produce notifications.**
  - Its consumer (`MembershipJunctionUpdaterHost`, off by default) maintains `sprk_userentityassociation` rows.
  - The publisher is off unless `Membership:EventPublisher:Enabled`.
- **Notifications are explicit, single-user `appnotification` rows** (`NotificationService.cs:55-81`).
  - Nothing notifies on document or to-do creation, and nothing expands a team into its members, so **a team-owned record notifies nobody**.
- **Daily Briefing:**
  - The **to-do channel filters `owninguser = caller`** (`DailyBriefingCollector.cs:1027`), so team-owned to-dos vanish. The peer was told.
  - Matters, projects and events come through the membership resolver's `owningteam` term. A BU default team contains the whole BU, so these would surface for everyone in the BU.
