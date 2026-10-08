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

## Owner answers, round 5 (2026-10-02). BINDING.

1. **Root-BU reach is a dev data artifact, not designed behaviour.** Owner, verbatim in substance: "In production we will NOT assign new users to the root Spaarke business unit; we will assign them to the customer's named child business unit. BUT the key is that when new records are created they have to be assigned to the creating user's business unit/team, including if created server side by the BFF API (whose Dataverse app registration will be assigned to the customer business unit)."
   - **How to apply.** In dev, take no action on the root default team's "Spaarke Basic User" link, the root-BU users, or the root-BU BFF application users. The census and NFR-05 clause-1 findings they cause are accepted dev findings. No census exception is coded: in production the census must flag any of them.
   - **Production invariants this rests on** (routed to customer-provisioning-orchestration-r1 in `handoffs/INCOMING-145-secure-setup-handler.md` §6):
     - users live in the customer's child BU;
     - the Secure Record BU is a direct child of the root and a sibling of the customer BU, never beneath it;
     - the BFF application user lives in the customer BU. **Gap today:** `DataverseWebApiAppUserCreator` creates it in the ROOT BU;
     - the root default team holds no Deep or Global read role.
   - **Ownership rule as implemented** (`RecordOwnershipResolver`, task 080 / write-path I-6):
     - **record-first:** a record filed under a parent is owned by the parent's business-unit default team;
     - otherwise it is owned by the caller's business-unit default team (for the BFF's app-only creates, the app user's BU);
     - a secure parent → the named "Secure Record Owners" team (C10 part 2).
     - With one customer BU per environment, "the creating user's BU" and "the parent's BU" are the same unit.

## Owner answers, round 6 (2026-10-02). BINDING.

1. **A work assignment (or project) filed under a SECURE matter or project is itself secure: "yes".** This is task 158. Such a record becomes a real secure root: `sprk_issecure`, the named-team owner, its own container and the creator share. The parent's sharees can see it. This supersedes task 155's interpretation (iii) for these records.
2. **Record-first ownership:** explained to the owner. The new record's owner is the parent's business-unit team; the creator's (or BFF app user's) unit team is used only when there is no parent; a secure parent's records go to the named team. With one customer BU per environment, it gives the same result as creator-first. It is kept as implemented, and the owner had no objection.
4. **Parent unsecured → its secured work assignments and projects STAY secure: "yes"** (asked 2026-10-02 with the recommendation "stay secure"). There is no unsecure cascade. Unsecuring a child is an explicit act by the people F3 allows. This is now a task 158 constraint.
   - **Clarification (same day): "but user can unsecure any related records."**
     - The parent's unsecure response lists the related records that stayed secure.
     - The same action can optionally unsecure any of them where the caller holds F3 rights.
     - Each can also be unsecured on its own later.
     - Interpretation (owner-reversible): a related record whose parent is still secure cannot be unsecured; it is refused with a message, because the round-6 rule would secure it again.
3. **Customer BU seeding:** the owner believes customer-provisioning-orchestration-r1 seeds the customer's business unit as a prerequisite. Verified in that project's design: §9.3 places both Dataverse application users in the **Root** BU, and no step creates a customer BU. #1094 stands as a design change for that project; its 2026-10-02 comment cites §9.3. The dev root-BU placement is a dev artifact (round 5).

## Owner answers, round 7 (2026-10-02). BINDING: "follow recommended" on 1-3, "yes apply" on 4.

1. **Task 137, external-access reconciliation job posture:**
   - Enable the schedule in **report-only** mode now. Enable writes only after the owner has reviewed one report.
   - Inactive contacts and inactive roots stay READ guards only, so reactivating one restores access with no data repair.
2. **Task 133, the persisted human creator for app-created secure rows** (Office quick-create, created by the BFF app identity), option (a):
   - A NEW server-stamped column records the actual person who made the record. Resume-provisioning shares to that person.
   - The column is written only by the BFF, as the persisted creator for that purpose.
   - The schema change runs as part of 133's live gate (approved by this decision).
3. **Task 146, AI tool handlers that create records as the user** (DataverseCreateRecordHandler, EmailDraftToolHandler): apply the **G5 pattern**.
   - Check the caller's rights AS THE USER (CallerRecordAccessProbe / RetrievePrincipalAccess pre-check).
   - Create AS THE APP, owned by the team (the named secure team under a secure parent; otherwise the RecordOwnershipResolver team).
   - Record the person in the table's Assigned-To / "for" column where one exists.
   - This supersedes those handlers' "User-OBO ONLY" rule (owner-approved §6.5 path B for that spec rule; cite it in the PR).
4. **Task 146, live role extension: yes, apply.**
   - The "Secure Record Owner" role goes from 9 to **26** tables, all Read at Basic, per 146's `config/secure-record-owner-role.json`.
   - Use 145's procedure: negative control → verbatim refusals → dry run → -Apply → §5.4 strip → -Verify → probes.
   - Apply it BEFORE deploying 146's code, because Dataverse refuses team ownership without Read.
5. **Owner follow-up question: do the child records of a secure record inherit the parent's user and contact access?** Confirmed by design:
   - **Internal users:** task 149. Each child's principals and rights equal the root's POA share set, kept in sync on create, on share/unshare (BFF and out-of-the-box MDA sharing) and on secure/unsecure, and never wider than the root's. Task 147 does the same for client writers.
   - **Contacts:** the external data plane scopes child rows through their root's accessible set. A contact's rights on a secure root, which come from direct named grants only (FR-22), extend to that root's children and no further. Task 136 makes the gate rights-based, and task 156 keeps the child→root link fresh.
   - Task 149 has not run yet; it is blocked on 146.

## Owner answers, round 8 (2026-10-03). BINDING. Task 156's three open decisions; each "(Recommended)" option chosen.

1. **The AI "update record" tool re-stamps INLINE (§6.5 path B), as round 7 item 3 did for the two AI create handlers.** `DataverseUpdateRecordHandler` calls a narrow, write-only re-stamp helper in the same operation, with no queued gap. This amends the tool's "User-OBO ONLY" spec rule for that one helper only; the user's own write stays OBO. Record the amendment in the handler's spec/notes and the POML. AC1 is then met with no exception.
2. **F-051-6: `TaskActionCore` writes the standard ADR-024 regarding pair** (id, type when a ref exists, name, url), as every other regarding builder does. That makes new rows detectable by the stamp job. New rows only; nothing to backfill. The AI/communication "create task" follow-ups will show the regarding link in the UI.
3. **A root column typed directly onto a row filed under another record: keep the rule AND lock the columns** (option 1 + 3). The pair decides, and the root columns on a filed row are a copy (option (b) stays whole in both directions). Make the four `sprk_regarding{core}` columns read-only on the to-do, event, communication and analysis forms, so the RegardingResolver picker is the only way to set a root. This is a form change; check first which forms expose the columns.

Also from task 156's verifier, filed as work rather than decisions (the owner's no-deferral rule):
- **#1098**: the events API (all 8 `/api/v1/events` routes) is sign-in-only over an app-only client, and `UpdateEventAsync` writes `sprk_regardingrecordtype` as an integer on a Lookup column.
- **156 item 13a**: mirror `IntermediateRootColumns` in the TypeScript `PolymorphicResolverService.deriveCoreAncestorStamps`, with a C# lock-step test.

## Owner answers, round 9 (2026-10-03). BINDING. The route authorization sweep (`notes/route-authorization-sweep-2026-10-02.md`); each "(Recommended)" option chosen.

**The finding:** 82 of 419 BFF routes let ANY signed-in caller act on records it has no rights to. The route checks only sign-in, then reads or writes as the BFF's own identity on a record the caller chooses. That is 17 critical, 29 high, 30 medium and 6 low; the events API is #1098.

1. **UAC-r2 fixes all 82**, including surfaces other projects own (Compose, communications, AI, SPE admin, Insights). One access-control pattern applies everywhere. File overlaps with active projects are coordinated through `/conflict-check` and GitHub issues, not handed off.
2. **Timing: now, in parallel with batch 4.** The owner chose "critical + high now, medium/low right after". Nearly every surface has high findings, so each surface task covers all of that surface's findings, and all of them start now. Nothing waits on batch 4.
3. **A build-time guard:** every BFF route must declare how it is authorized: a record-level check, an admin policy, or an explicit, reasoned waiver for a route that only touches the caller's own data. A new route with only a sign-in check fails the build. It extends task 074's `RouteAuthorizationGuardTests` to every route file. The 82 known findings start as Pending waivers owned by their fix tasks; the guard's stale-waiver rule then forces each fix to delete its waiver.

**Fix pattern (from the existing decisions, not new):**
- **Reads:** the caller's OWN Dataverse rights decide (D1, C9). Use OBO / the existing `DataverseImpersonation` helper, or a `CallerRecordAccessProbe` / `RetrievePrincipalAccess` pre-check on the exact record.
- **Writes:** a rights pre-check as the user, then an app-only write only where a server invariant needs it (the 130/146 G5 pattern).
- **Lists:** trimmed to what the caller can read.
- **Failure:** fail closed (ADR-003). An unknown id and a denied id get the same answer.

## Owner answers, round 10 (2026-10-03). BINDING. Each "(Recommended)" option chosen.

**From the route-sweep task authoring (tasks 159-169):**
1. **Routes with no caller: remove them if truly unused.** Delete a route when it has no caller in the repo AND is not in any published API description (e.g. the Copilot OpenAPI / plugin manifest); otherwise fix it. Candidates named: the internal Dataverse proxy (`/api/dataverse/fetch`, `/api/dataverse/record/...`), the AI prompt library (`/api/ai/prompts`), document `associate-record`, and the reporting module. Each removal is listed in its PR. Precedent: tasks 073/083.
2. **Task 168, analysis form: add the RegardingResolver picker, then lock** its root columns, the same as the to-do, event and communication forms.

**From batch 4 (all nine recommendations accepted):**
3. **132:** during a RetrievePrincipalAccess outage, the degraded probe-derived answer stays UNCACHED (as built). Correctness first; the load stays at the no-cache baseline.
4. **133, compensation's reverse Assign cascade:** coordinate with task 148's child-ownership logic, then snapshot and restore each re-owned child's own owner (options (c) then (a)).
5. **133, a kept-container record failing after its move:** as shipped. No automatic resume; the response names the Manage Access recovery.
6. **143:** reusing `IScheduledJobLease` as the No Access enforcer's per-record mutex is ACCEPTED as a documented §6.5 path A exception to ADR-036 A1-7 / ADR-052 §5 (design.md §9 row). The PR cites it.
7. **146:** moving a CHILD out of a secure root is an un-secure, so F3's limit applies: Full Access holders plus the creator.
8. **146 E1:** unfiled communications (inbound, chat, outbound naming no record) keep their creator as owner. Filed ones are routed secure-if-any. MessagingIngestor's Pending waiver resolves on this.
9. **150 F6 copy:** option A for rows 1, 3, 4, 5 and 6; the resume-neutral option D for row 2. Remove the DRAFT markers.
10. **150 §11.6:** `/provision-project` may secure an UNFLAGGED record only for its creator (`createdby` / `sprk_createdbyperson`); already-flagged rows stay on the Write gate. "Secure an existing record" belongs to task 148's surface.
11. **150 item 4:** invoices follow their matter. `sprk_invoice.sprk_issecure` is no longer a security input (remove it from the securable registry; the ancestor walk decides), and the column is locked like the roots.

**Decided by the main session under existing decisions (reversible; recorded for the owner):**
- **156:** the narrow write-only re-stamp helper ships as a sealed concrete class with one public member (`CoreAncestorAfterWriteRestamp`), which is ADR-010 compliant. It meets round 8 item 1's "narrow, write-only re-stamp helper".
- **157 residual 4:** the shared DataGrid's external-host jest test becomes a blocking CI check (`sdap-ci.yml`); this project's hot-path declaration becomes ci-workflows = Y.
- **133's interim app-only `sprk_createdbyperson` stamp** in `DataverseCreateRecordHandler`: superseded at integration by task 146's create-as-the-app (round 7 item 3), which writes the stamp in the create payload (133 note §13.8).

## Owner answers, round 11 (2026-10-03). BINDING. Each "(Recommended)" option chosen.

1. **142: ADR-034 Amendment A4 is ACCEPTED (§6.5 path B).** Assigned-To access for contacts is materialized as removable Collaborate grants (the substance is round 2 item 5 and Q5). The main session applies the concise `.claude/adr/ADR-034` edit with the 142 PR.
2. **149, decision 3:** a window of **at most 2 minutes** is accepted for MDA Share/Unshare and for NEW or RE-FILED children to pick up the root's sharees. The scheduled reconcile is the mechanism, and it ships with writes on. Dataverse's table-wide Share/Unshare/Reparent cascade is NOT enabled.
3. **149, decision 4:** ship gate. 146 and 149 may deploy, but no record is unsecured in a shared environment until task 148 (which re-owns the children, then calls `SyncRootAsync`) is deployed.
4. **149:** a child under TWO secure roots gets the INTERSECTION of their sharee sets (fail closed), and ShareAccess is NOT mirrored onto children.

**Live steps on dev for integrating batch 4: APPROVED, all of them,** each as dry run, then apply, then verify, recorded in the task's live-gate note:
- schema and security: 133 `sprk_createdbyperson`; 142's assigned-access ledger table plus the "Update Access" ribbon; 143's No Access column plus O2 (only an access-administrator role reads No Access entries); 146's role extension 9→26 (round 7) plus Read on `sprk_emailreviewlog` for the BU default teams (G146-2); 150's null-flag repair plus the FLS lock on `sprk_issecure`, invoice included;
- probes on TEST records only;
- BFF and external SPA deploys.

**Test user:** the main session creates ONE Entra test user plus its Dataverse user in "Spaarke Business Unit 1" (Spaarke Core User + Basic User, no shares on secure records). The credentials go to the owner, never into the repo.
- **Created 2026-10-03:** `uac.child.user@demo.spaarke.com`, display name "UAC Child BU Test User", Entra oid `94047962-97fb-4588-ab69-79c7d6d226ec`, licence `POWERAPPS_VIRAL` (the same as the other dev test users), usage location US. Dataverse systemuser `d6f8f439-40bf-f111-a05b-3833c5e9614d`, added through the Power Platform admin API, moved to **Spaarke Business Unit 1** (`cb15f587-baa0-f111-aaac-000d3a99d1d7`), roles **Spaarke Basic User** + **Spaarke Core User** (that BU's copies), no shares. `RetrievePrincipalAccess` on secure project `65a3fab2` = **None**. The password was given to the owner in session; it is not stored anywhere in the repo.

## Owner answers, round 12 (2026-10-03). BINDING. Task 167's nine verifier questions; "Accept all 9 (Recommended)".

1. `GET /healthz`, `/healthz/catalog`, `/ping` (anonymous) get **rate limiting**; the strict AnonymousByDesign rule (a compensating control is mandatory) is kept.
2. ReferenceData "takes no record id" means **"selects nothing by a record id"** (catalog keys, the static model-deployment id, NDA `clauseRef`, the context-mapping cache key, the saved-query lookup).
3. A HandlerDecision body may include **same-type helpers it calls directly, one level deep**.
4. Routes gated only by an owner comparison on a caller-chosen id get a new **OWNER-COMPARISON** basis. `DELETE /api/memory/pins/{pinId}` must answer a **uniform 404** for unknown and not-yours (no 404/403 oracle): task 166.
5. Daily briefing render/email: **CallerScopedOnly**, as recorded.
6. Playbook lists: **CallerScopedOnly**, and the oid-vs-systemuserid mismatch in the user-list `_ownerid_value` filter (and `PlaybookAuthorizationFilter` if it shares it) is **fixed**: task 164.
7. `POST /api/communications/threads/direct`: **CallerScopedOnly**, as recorded.
8. Non-sweep findings named in the 161/166 amendments **stay owned by 161/166**.
9. Credited routes 166 must fix (revoke `ContainerId`, office to-do Create, close-project) get **Pending InsufficientDecision waivers owned by 166** so the guard tracks them until 166 resolves them.

## Owner answers, round 13 (2026-10-03). BINDING. Batch 4c's open verifier questions; each "(Recommended)" option chosen.

1. **133 / 148:** when a record is UNSECURED, its `sharepointdocumentlocation` / `sharepointdocument` rows are re-owned by **the same rule task 146 applies to any non-secure child**, not left with the new root owner by the Assign cascade. One ownership invariant everywhere.
2. **133 gate (e)** (replaying the provisioning compensation on a throwaway TEST project) is **covered** by round 11's approval.
3. **146 gates G146-3** (hold-alert recipients) **and G146-5** (default-team roles on Dev1/Test1) are **covered** by round 11's "all of them".
4. **142 R-13: fix now.** The deny-veto check gets a **tri-state answer** (allowed / denied / unverifiable) so a read fault (Dataverse 5xx, throttling, unreadable memberships) is reported as a fault, not absorbed into "denied". It stays fail closed. The `AccessibleRecordSetService` interface change is accepted (139 and 140 consume it). Runs as a 142 r4 fix round.
5. **142:** a 143 wall-guard **Unverifiable** answer on the internal-user share path **fails the run**, exactly like the deny-list fault.
6. **146 / 150 F3:** the shared helper's behaviour is kept (a missing creator column → 403 `permission_unverifiable`; a Full Access holder is admitted when the creator read fails).
7. **146:** the update tool's re-file step is **folded into ADR path B**, as round 8 did for 156's re-stamp.
8. **146:** a playbook that impersonates a user is **checked under F3 as that user**; only truly person-less writers are refused.
9. **146:** children the BFF creates as the application **record the person who asked**: `sprk_createdbyperson` is added to the child tables (schema step, dry run / apply / verify, approved as a dev live step) and stamped by every app-create writer, so F3's "or the creator" branch works for them.
10. **150:** the new user-facing copy uses **option B** wording throughout (F6 rows 7-11).
11. **157:** `datagrid-external-host-gate` lands **advisory** and becomes blocking after N green runs on the CI runner (the repo's standard).
12. **157 / CI router:** the pre-existing `docs_only` skip belongs to `ci-cd-unit-test-remediation-r1`; the main session **files a GitHub issue** there. No router change in this project.

## Owner answers, round 14 (2026-10-03). BINDING. Each "(Recommended)" option chosen.

1. **167, health probes:** `GET /healthz`, `/healthz/catalog` and `/ping` use a **dedicated, looser `health-probe` rate-limit policy** (e.g. 120 per minute per client IP), not the shared `anonymous` policy (10/min). The platform health check, the slot-swap warm-up ping and the deploy workflow's poll loop must never see 429. Round 12 item 1 still holds: the probes are rate limited.
2. **167, routes with neither RequireAuthorization nor AllowAnonymous:** set an authorization **FallbackPolicy requiring an authenticated user** (fail closed at runtime) **and** a guard rule that fails the build on any route lacking `RequireAuthorization*` or `AllowAnonymous` on its route or group chain, with negative and positive controls. Every intentionally anonymous endpoint carries an explicit `AllowAnonymous`.
3. **133:** `container_ownership_unreadable` and `resume_creator_unavailable` (creatorState unreadable) classify **401/403 as Refused** (an administrator must act), as the cascade reads already do; 503/429 stay retryable.

## Owner directive, round 15 (2026-10-03). BINDING. Task 162's anchorless analyses.

Owner: *"we need this to be properly fixed so that we do not have the issue — it cannot defer or just sideline the issue/solution — fix it in the correct way."* "Accept the 404", "row-Read fallback" and "backfill only" are each REJECTED as partial. The complete fix, owned by task 162:

1. **Classify** every anchorless active `sprk_analysis` row (221 of 994 on dev, 2026-10-03) by WHY it has no anchor: (i) created in a record's context by a writer that failed to record the anchor; (ii) genuinely standalone (created with no record in context). Record the counts per writer and per class in the note.
2. **Fix every writer at the source** (ADR-002 WP-1: one server-side invariant owner): an analysis created in a record's context ALWAYS records its anchor. Find the writers (start with the 65 rows created by the BFF application identity; latest 2026-10-02) and fix each, with a test that fails if the anchor is dropped.
3. **Backfill** class (i) rows whose anchor is derivable from existing data (linked document / output / session / regarding pair), with a script in the repo's schema/data-script pattern: dry run → `-Apply` → `-Verify`. Do NOT run `-Apply` yourself; the main session runs it on dev.
4. **Standalone analyses (class ii) are PERSONAL:** readable only by the person who created them — matched by Dataverse systemuserid (never the Entra oid; never a business-unit-depth row Read, which could expose one user's analysis to colleagues). Anything that cannot be verified fails closed with the uniform 404.
5. Tests and seeds for each part; the published Copilot `getAnalysis` keeps working for anchored and for the creator's own standalone analyses.

## Round 16 (2026-10-03). BINDING. Main-session decisions under the owner's standing directive "fix it in the correct way; never defer or sideline" (round 15). Escalations of sweep tasks 163, 164, 165.

1. **163 `/api/insights/ask` (trigger 3).** The defect is that a Read-only caller can cause an app-only WRITE to the subject. Rule: when the bound playbook contains a node that writes to the subject (UpdateRecord / create on it, e.g. `matter-health-single` → `persistEnvelope` → `sprk_matter.sprk_performancesummary`), the caller must hold **Write on the subject** (as-user pre-check, then the app-only write — the round 9 pattern); Read suffices only for non-persisting playbooks. Caller parameters go through the shared parameter policy (item 3). Update `projects/ai-spaarke-insights-engine-r1/notes/phase-1-live-smoke-runbook.md` to the new subject contract (trigger 5).
2. **164 host-type / document-id vocabulary (trigger 3): option (a)** — authorize by kind, no new map: matter / project / work assignment / invoice through the existing set resolver; a `sprk_document` host as a document Read; `sprk_analysis` and the `sprk_analysisoutput` sentinel through one `sprk_analysises` constant; any other type → the host context is DROPPED from the session; an SPE-drive-item-id document → decided by the caller's own SPE read (OBO). Then complete the stopped chat family (F0-F3, F12).
3. **164 playbook parameters (trigger 4, URGENT — FetchXML injection).** Fix the ROOT CAUSE first: every caller value substituted into FetchXML (or any query/OData text) is type-validated and XML-/OData-escaped at the substitution point, so no parameter can inject regardless of key. Then ONE shared playbook-parameter policy, owned by 164 and consumed by `/execute`, `/run-playbook` and 163's `/ask`: 400 for server-owned keys (`userId`, `tenantId`, `run.*`, `start.*`, `userPreferences`); identifier keys mapped and authorized (`matterId` → `sprk_matter` Read, plus Write when a node writes to it); `timeWindowHours` / `todayUtc` type-checked; a declared allow-list of tuning keys with types; any other GUID-valued parameter refused; `run.userId` set server-side from the caller on the HTTP path. No "refuse all parameters" interim.
4. **165 environment routes (stopped item): option (a)** — `sprk_speenvironment` is shared tenant infrastructure: only an admin whose own business unit is the ROOT may write environments; a leaf-unit admin may read only environments linked by a config they can reach; config POST/PUT may link only an environment the caller can read. Implement it (it was not implemented). No new column.
5. **165 trigger 5:** fix the three `SearchItemsTests` properly — seed an in-scope config through `FakeDataverseTables` so the empty/whitespace cases still assert the handler's 400, change the not-found case to assert the uniform 404 (`spe.admin.deny.config_out_of_scope`), and remove the suite's real outbound Dataverse call. Never restore fail-open.
6. **167:** the passed branch `task/uac-r2-167-r2` predates round 14; round 14 items 1-2 (dedicated `health-probe` policy; runtime FallbackPolicy + guard rule) are still owed by 167.
7. Integration order for the sweep: 162 before 164 (164 builds on it), 164 before 163's parameter consumer.

## Round 17 (2026-10-03). BINDING. Main-session decisions under the owner's standing directive (round 15). Task 150's open questions.

1. A missing `sprk_createdbyperson` column (400) in the provisioning creator rule answers **unverifiable** (403 `permission_unverifiable`), aligned with task 146's F3 helper — not `not_record_creator`.
2. An anomalous unflagged RESUME (record already owned by the Secure Record owner team) gets **resume-neutral copy** for row 8 and for task 133's `creator_unresolved` client string, in the manner of row 2's option D. Each string pinned by a test.
3. The app-only readers that map an EMPTY `sprk_issecure` to "not secure" (ExternalParticipationService, ExternalDataService, InternalShareEndpoints, ExternalGrantLifecycle, SubjectStandingGrantReader, RecordOwnershipResolver) **fail closed** on an absent flag, exactly as `RecordContainerResolver` does. Owned by task 150; tests + seeds per reader.
4. Task 150 also closes its two LOW items: the stale "still DRAFT" note sentence, and a committed regression check for `scripts/Repair-SecureFlagNulls.ps1`'s after-count.

## Round 18 (2026-10-03). BINDING. Main-session decision under the owner's standing directive (round 15). Task 142 R-14.

1. `NoAccessListReader.GetDeniedRecordsAsync` must **evaluate any subject set**: chunk organization and contact ids within the safe query bound, one query per chunk, union the results. The deterministic "too large → FailedClosed" state is removed (it made a grant "try again" forever and kept the Assigned-To job red permanently). A genuine read fault in any chunk still fails the whole answer closed (Unverifiable). Tests for 26+ organizations / 6+ contacts and a fault in one chunk; the admin-guide limit sentence updated.
2. Task 142 also adds the new 503 no-access-unverifiable code to the Manage Access client's `GRANT_POLICY_REASON_CODES`, and corrects the stale grantee_denied quote in `notes/task-139-grant-model.md`.

## Round 19 (2026-10-04). BINDING. Main-session decisions under the owner's standing directive (round 15). Task 168's escalations.

1. **Trigger 1 — option (a):** the three type-2 forms with visible, editable root controls (`sprk_event` 90d2eff7 "Event modal form", `sprk_event` 835b8ee8 "Event Assign Work main form", `sprk_communication` b58ec3d8 "Message main form") get the filing picker (RegardingResolver) with hidden cells for the pair and lookups and the presave registration, THEN the root columns are locked. Form changes as dry-run/apply/verify scripts with snapshots.
2. **Trigger 3:** fix the pre-existing defect on `sprk_event` eaf22dcb "Event main form": register `Spaarke.SmartTodo.RegardingPreSave` and add hidden cells for the pair and the lookups, so a CREATE stages the chosen lookup and the stamps.
3. **Trigger 5:** neither "disable editing" nor "accept". The `sprk_event` and `sprk_analysis` editable home grids get an **OnRowLoad handler that disables the root columns** in the grid (inline editing of every other column keeps working). Test the handler; deploy is a main-session live step.
4. **Amendment blocker — option (a):** add `sprk_regardingrecordurl` to `sprk_analysis` with a schema script (dry run / `-Apply` / `-Verify`, in the repo's schema-script pattern including the rootcomponentbehavior-0 solution check), so every child table carries the full ADR-024 pair; then the analysis form gets the picker, hidden cells and presave, then the lock.
5. Live steps (presave v1.4.0 deploy, form scripts, the schema script, the grid handler) are main-session manual gates on dev, run dry → apply → verify.

## Round 20 (2026-10-04). BINDING. Main-session decision under the owner's standing directive (round 15). Task 165, Model 1 cross-customer containers.

The cause is that no authoritative container → business-unit binding exists, so routes bound only to a container TYPE let one customer's leaf admin reach another customer's containers of the same type. Fix:
1. Every SPE container is stamped with its owning business unit at creation (a `fileStorageContainer` custom property, e.g. `spaarkeBusinessUnitId`, written by the BFF's container-creation path); a backfill script (dry run / `-Apply` / `-Verify`) stamps existing containers from their authoritative records and lists any whose owner cannot be derived.
2. The container, item, permission and bulk routes authorize PER CONTAINER: a leaf-unit admin only containers bound to its own unit or a descendant; an unbound container only a ROOT-unit admin; an unreadable binding fails closed.
3. Configs of different customers may then share a `containerTypeId` / owning app (Model 1): the app-identity duplicate check is narrowed accordingly.
4. 165's round-16 environment scoping is folded into the EXISTING SPE-admin scope filter (CLAUDE.md §11: no new filter class).

## Round 21 (2026-10-04). BINDING. Main-session decisions under the owner's standing directive (round 15). Task 166's escalations.

1. **F0 second door (trigger 1): BOTH (a) and (b).** Field-level security on `sprk_graphdriveid` and `sprk_graphitemid`, writable only by the BFF identity (the task-150 FLS script pattern, dry run / `-Apply` / `-Verify`, with the rootcomponentbehavior-0 solution check), AND a server-side check before every app-only download that the drive item belongs to the record's own container (fail closed). Defence in depth: neither alone is sufficient (FLS does not cover rows forged before the lock; the check does not stop the write).
2. **Reporting (trigger 5): option (A), completed.** The catalog row id is the contract, read AS THE CALLER; the Power BI report/workspace ids are derived server-side from the row; the client is fixed to match; the update verb is aligned (the BFF maps the verb the client sends); and the row-level-security effective identity (business unit) is computed server-side from the caller's systemuser and put in the embed token, so the `businessunit`/`bu` RLS claim is actually produced. Tests per route; the seven reporting Pending waivers resolve.
3. **Field-mapping push (trigger 4): option (a).** Resolve the target's parent lookup from relationship metadata (the lookup on the target that references the source entity), so every profile works — including "Matter to Invoice (Attorney Matrix)" via `sprk_invoice.sprk_matter` — with no per-table naming convention, no schema change and no deactivated profile. Ambiguous metadata (several lookups to the source) fails closed with a clear error.

## Round 22 (2026-10-04). BINDING. Main-session decision under the owner's standing directive (round 15). Task 148.

On UNSECURE, a root sharee's share is removed from a child ONLY if that child was owned by the Secure Record owner team when the pass began (it was isolated, so the share is 149's mirror). A share on an ordinary, never-isolated child (e.g. a user-owned document shared manually) is the user's own intent and is kept. The "owned by the secure team" set is snapshotted at the start of the pass, before any re-own.

## Round 23 (2026-10-04). BINDING. Main-session decisions under the owner's standing directive (round 15). Task 166.

1. **Interim document-pointer check** (until round 21 item 1's BFF pointer-attach path, legacy migration of the 447 dev files, FLS apply and strict derived-container check land in the follow-up round): verify the ITEM — the driveItem's createdBy equals the document row's creator (createdby when human, else `sprk_createdbyperson`), or the BFF identity for BFF-created rows — AND accept only containers in the document owner's business unit or its customer's subtree (archive container only on the archive path); unverifiable fails closed. **Residual exposure until the strict check:** a pointer to another legitimately-uploaded item by the same creator within the owner's own subtree.
2. **Reporting save-as:** the `pbiReportId` save-as registration path is removed (view-only embed tokens cannot create a report; it only aliased shared reports); the server-side clone stays. DELETE removes the Power BI report only for an `iscustom` row no other catalog row references.

## Round 24 (2026-10-04). BINDING. Main-session decisions under the owner's standing directive (round 15). Task 148.

1. **Exact dry run (option a):** the ONE `IRecordOwnershipResolver` gains a planned-owner overlay input so report-only iterates to the same fixpoint as `-Apply`; the dry-run plan equals what apply would do (grandchildren included). Tests: dry-run plan == applied result on a 3-level tree.
2. **No rule-driven widening (option b):** the sweep job and Write-gated provisioning never release an isolated child to its business unit without an F3 holder's act; such rows are reported as `needs-f3` and stay isolated (consistent with round 6 "never auto-unsecure" and round 10 item 7).

## Round 25 (2026-10-04). BINDING. Main-session decisions under the owner's standing directive (round 15). The sweep's remaining open questions.

1. **161:** deploys with (or after) task 146 — the same release. The child-BU archive/confirm 403 on app-owned communications is a DEV artifact of the BFF app user sitting in the root BU (production places it in the customer BU, #1094); live gate (d) uses a communication the test user can AppendTo/Write. D1 (the caller's own rights decide) is unchanged.
2. **162:** round 15 stands (complete fix; the "accept 404" recommendation is rejected); the writer creating anchorless analyses is found and fixed at the source (round 15 item 2); and `POST /api/ai/analysis/create` adopts **G5** — Create on `sprk_analysis` plus `analysis.attach` (Read and AppendTo) on the document — matching promote.
3. **163:** round 16 item 1 stands (Write on the subject for a persisting playbook; parameters through 164's shared policy — build on `task/uac-r2-164-r1`).
4. **164:** option (a) as built — a chat on an analysis host is decided by task 162's analysis-read rule (one shared declaration), never a business-unit-depth row Read; this refines round 16 item 2's wording. Round 16 items 2-3 as built are confirmed.
5. **165:** (b) integrate as ONE unit once round 20 items 1-3 are verified; the dashboard's per-config storage split is part of task 165 now (including the ADR-052 migration of `SpeDashboardSyncService` if the ratchet requires it); the two unproven guards (`GetReachableConfigIdsAsync` full-page guard; the handler-side uniform-404 wiring) get tests + seeds.
6. **166:** the `sprk_report` pointer columns (`sprk_pbi_reportid`, `sprk_workspaceid`, `sprk_datasetid`, `sprk_iscustom`) get BOTH FLS (writable only by the BFF identity) AND a server-side allowed-workspace check before embed, export and delete; a ROOT-owned document's accepted containers are only those stamped by the root unit itself (no cross-customer re-pointing under Model 1); `ExportReportAsync`'s RLS identity gets a test that executes the real export request builder; round 21 item 1 steps (i)-(iii) are owed in the follow-up.
7. **167:** round 14 stands (dedicated `health-probe` policy, runtime FallbackPolicy + guard rule).
8. **168:** (a) hide every raw pair / non-root `sprk_regarding*` lookup control on the round-19 target forms (the picker's host excepted; `-Verify` fails on a visible one); the grid lock extends the EXISTING `src/client/pcf/SpaarkeGridCustomizer` (cellEditorOverrides cancel editing for the four root columns; cellRendererOverrides mark them read-only) set on the `sprk_event` / `sprk_analysis` grid configuration by a dry-run/apply/verify script — no new PCF, no web-resource handler (ADR-006); the To Do main form gets its missing `sprk_regardingservicerequest` hidden cell via the picker script; the `PICKER_WITHOUT_PRESAVE` guard's fail-open probe is closed and seeded.

## Round 26 (2026-10-04). BINDING. Main-session decisions under the owner's standing directive (round 15). Tasks 150 and 166, from 150's final verification.

1. **150 reason code (round 17 item 1):** keep `403 sdap.provision.record_creator_unverifiable`, with `creatorState = column-missing` and `creatorColumn`. 146's `permission_unverifiable` belongs to the unsecure endpoint's namespace, and the meaning is the same: unverifiable, 403, fail closed, no retry offered. This is the contract the client maps.
2. **150's remaining items are completed on the integration branch** (142, 146, 148 and 133 are merged there):
   - (a) A test drives the REAL `ExternalParticipationService.GetRootRecordFlagsAsync` wire path (the `OrganizationMembershipReadTests` TestServer) with `sprk_issecure` omitted and with it null. It asserts `Unreadable` and that `AccessibleRecordSetService` suppresses the record. Seeded.
   - (b) `RecordOwnershipResolver.ReadParentAsync` makes an empty flag on a secure-flagged root fail closed, as 150 note §19.1 specifies, with its twin test and seed.
   - (c) Guide §7c step 0, note §9 G-0 and the "masked window" paragraph state the FULL effect of shipping 150's BFF before G-0: every NULL-flag root reads Unreadable/Restricted, so external participants lose every contact-sourced right on it, grants answer 503 `policy_unreadable`, the last-reader rule applies, and uploads answer 503.
   - (d) The ribbon: Make Secure and Remove Secure go in 142's ONE Access group, ONE ribbon file and ONE command script. Acceptance (a)–(f) and the four UI tests come from 150's UX amendment.
3. **Make Secure on an EXISTING record moves its existing files** into the record's own container, as part of the transition.
   - **Why:** leaving them in the shared container keeps secure content in a container whose access cannot be narrowed (SPE permissions are additive-only). 166's strict derived-container rule would also refuse every download of them.
   - **One BFF relocation service, `DocumentContainerRelocator`, with two callers:** the Make Secure transition (provisioning, after 148's child pass) and task 166's legacy migration (round 21 item 1 (ii)). The 166 script only triggers the BFF and reads its reports (148's `Invoke-SecureChildBackfill.ps1` precedent); no Graph logic lives in PowerShell.
   - **For each file:** copy it to the target container through the BFF identity, verify it (size, and hash where Graph returns one), re-point the `sprk_document` through 166's pointer-attach path, then delete the source. Every step is logged with before/after ids. The source is never deleted before the copy is verified.
   - **When incomplete:** answer 500 with counts (`sdap.provision.files_incomplete`); the record stays flagged and provisioned, and a repeat call completes it (the `childrenOnly` re-entry branch also relocates files). Un-moved files meanwhile fail closed under the strict rule.
   - **Ownership:** 166 builds the service if its follow-up reaches it first; otherwise the 150 integration lane builds it and 166's script calls it. Never two mechanisms.
4. **Make Secure confirmation copy** is owner-authored (150 escalation trigger 5). It is asked in the next owner question round with a recommended draft. Until then the command is built and tested with the recommended draft as ONE constant, and it ships only with the owner's answer.

## Owner round 27 (2026-10-04). BINDING. The Make Secure confirmation copy (answers round 26 item 4)

The owner, verbatim: "Accept your recommended wording so that the task does not get delayed (we can adjust in UAT if necessary)". The copy below is ACCEPTED. It ships as ONE constant, pinned verbatim by the UI test.

**`{record}`** is `project`, `matter` or `work assignment`, from the form's table.

- **Title:** Make this {record} secure?
- **Body, paragraph 1:** Only the person who created this {record} and the people it is shared with will keep access. Everyone else in your organization loses access, and external contacts keep only access granted to them directly.
- **Body, paragraph 2:** Its existing documents, events, to-dos and other related records become secure too, for the same people, and its files move to the {record}'s own secure storage. This can take a few minutes.
- **Body, paragraph 3:** To remove the secure designation later, ask someone with Full Access to the {record}, or the person who created it.
- **Buttons:** **Make Secure** (primary) · **Cancel**

## Round 28 (2026-10-04). BINDING. Main-session decisions under the owner's standing directive (round 15). Task 147's E1 and E2.

1. **E1 — browser-initiated child creates through the BFF use A1, the existing G5 pattern (round 7 item 3, b2).**
   - **Pre-check as the caller:** Create and Append on the table, AppendTo on every record named, and the payload names no owner and no field-secured column. That last check is the server-side column policy, so an app-only create cannot bypass FLS.
   - **The create:** made by the APPLICATION, owned by the team `RecordOwnershipResolver` names (`OwnedChildWrite`), with `sprk_createdbyperson` stamped as the caller (146's 17 tables). `createdby` is the app; the person lives in `sprk_createdbyperson`. Every reader of "who created it" uses `RecordCreatorPerson` (createdbyperson, else createdby).
     - Task 152's briefing "Created By" matching is updated to it in the same integration (an item on 152's code, tests + seed).
   - **Rejected:** A2, because Assign into the Secure Record BU is likely refused for users. A3, because it leaves a user-owned window of one request, which the owner has never accepted.
   - **One create endpoint per table.** 147 builds the routes in this BFF on the existing cores. Re-files go through the existing families: events and communications through 159/161's routes, and Compose's document association through `PUT /api/v1/documents/{id}`.
     - The sibling `field-mapping-server-write-path-r1` (design-only, also app-only by its Q2) extends these routes instead of adding its own. This is recorded in 147's note for that project.
2. **E2 — close the in-product paths; the job covers only the out-of-product ones.**
   - **In-product native creates of a child under a SECURE parent** (subgrid "+ New", quick create, a form New with the parent prefilled) are replaced by BFF-backed commands. These are the existing ribbon command-script pattern (142/150), with enable rules that read `sprk_issecure` through `Xrm.WebApi`. A read failure hides the native command and shows the BFF one (fail closed). Every such surface comes from 147's census plus a live form/subgrid inventory (read-only), as dry-run/-Apply/-Verify scripts.
   - **Writes outside the product** (imports, flows, direct API) are ADR-002 WP-5 territory. `SecureChildReconciliationJob`'s recent-changes pass runs every 2 minutes with writes ON in every environment where 148 is deployed, with a standing report of each correction.
   - The model-driven Create privilege on child tables is NOT removed globally: it would break ordinary records, and the native-command replacement closes the in-product path without it.
3. **Merge order:** task 169 rebases onto 147, which adds `sprk_memo` to the CHILD taxonomy under owner round 2 item 6. 169's "byte-for-byte unchanged" criterion then holds against that base.

## Round 29 (2026-10-04). BINDING. Main-session decision under round 15, applying owner round 27's stance ("accept the recommended wording … adjust in UAT"). Client copy for task 143's five provisioning codes.

These codes are emitted by `ProvisionProjectEndpoint` but have no client entry, so users saw a generic error (found by the 150 merge). They are added to `provisioningService.ts` and to its emitted-codes list (32 → 37), each pinned verbatim. The style is resume-neutral ("could not be finished"), as in rounds 10 and 17.

| Code | Status | Retry | Copy |
|---|---|---|---|
| `sdap.provision.creator_no_access` | 403 | No | You are on this project's No Access list, so you cannot secure it. Nothing about the project changed. |
| `sdap.provision.creator_no_access_unverifiable` | 500 | Yes | Whether you may access this project could not be checked, so securing it could not be finished. Nothing about the project changed. |
| `sdap.provision.resume_creator_no_access` | 409 | No | Securing the project could not be finished, because the person who created it is on its No Access list. Nothing about the project changed. An administrator needs to review the project's access. |
| `sdap.provision.resume_creator_no_access` | 500 | Yes | Securing the project could not be finished, because the access of the person who created it could not be checked. Nothing about the project changed. |
| `sdap.provision.principal_no_access` | per-person warning | — | {name} is on this project's No Access list, so the project was not shared with them. |
| `sdap.provision.principal_no_access_unverifiable` | per-person warning | — | Whether {name} may access this project could not be checked, so the project was not shared with them. You can share it with them later from Manage Access. |

## Round 30 (2026-10-04). BINDING. Main-session decision under round 15. Task 158 Q1.

1. **A sharee removed from a secure parent is also removed from the secure records filed under it, but ONLY where their access there came from the parent.**
   - **Option (c), provenance.** (a) add-only leaves a former sharee with access they should have lost. (b) a blind revoke on the parent's unshare would also remove people shared DIRECTLY on the filed record.
   - **No new table: the provenance extends task 142's `sprk_assignedaccess` ledger** (CLAUDE.md §11).
     - Each share 158 passes from a secure parent to a filed secure root is recorded as a ledger row on that root, with source `inherited:{parentTable}:{parentId}` and the subject (user, or team — add a `sprk_subjectteam` lookup to the ledger schema script: dry-run/-Apply/-Verify, through the solution-membership helper).
     - It carries the mask written, under A4's rules: never lower existing access; on the parent's unshare, remove only the inherited share that is still UNMODIFIED; an operator's removal on the filed record is recorded `Declined` and is never re-added while the parent share persists; a share that is also direct (an independent row, or a raised mask) is kept.
     - Fan-out happens in the same place 158 already calls `relatedRoots.PassSharesOnAsync`, and in the reverse direction on unshare. Failures report through `children_incomplete`, as 149's do. The L4 job also reconciles provenance.
   - Tests + seeds per path (share, unshare-inherited, unshare-also-direct, operator-removed-then-parent-reshared, fault).

## Round 31 (2026-10-04). BINDING. Main-session decisions under round 15. Task 158, from its first verification.

1. **The creator of a record that inherits security honours BOTH No Access lists: the record's own and every secure PARENT's.** It is the same rule the sharee mirror already applies (owner N6, round 3b: refuse to provision). The check runs on the inherited path as well, BEFORE any write; it must not depend on the flag being set first. A walled creator, or an unverifiable answer, refuses the create, with the existing No Access reason codes and round 29's copy.
2. **A record created under a secure parent is created INTO isolation, with no business-unit-visible window** (round 28 E1 rejected such windows).
   - **The create:** made by the application, owned by the named Secure Record Owners team (G5: as-caller pre-check first), with `sprk_createdbyperson` = the caller and the flag set in the create.
   - **Then:** the creator's share is added and read back (133's share-first rule); then the container. A creator share that fails deletes the just-created row (read back) and refuses — never a row nobody can open, never a BU-visible row.
   - Container or child steps that do not complete leave a PROVISIONED-but-incomplete record, which the existing re-entry branch completes.
   - The out-of-band path (rows written outside the BFF) stays with the job, which secures them (ADR-002 WP-5).

## Owner round 32 (2026-10-04). BINDING. Task 158 Q2.

The owner chose **§6.5 path B, secure inline** (AskUserQuestion, 2026-10-04: "Path B: secure inline (Recommended)").
- Amendment A-UAC146 (spaarke-ai-architecture-redesign-r1 spec) is EXTENDED: when the user-OBO `dataverse.update_record` re-files a record under a secure parent, it runs provisioning's app-only steps inline, right after the caller's own write.
- Record the amendment text beside round 8 item 1 / round 13 item 7 in that spec. Mark 158's remarks ACCEPTED (no longer PROPOSED), and cite this round in the PR's §6.5 block.

## Round 33 (2026-10-04). BINDING. Main-session decisions under round 15. Task 150's integration lane (R-150-1 to R-150-5).

1. **R-150-1: Make Secure stays open to every Write holder.**
   - Round 9 item 10 limits `/provision-project` on an UNFLAGGED record to its creator, and says "secure an existing record belongs to task 148's surface". Owner round 3b says that surface (the Make Secure ribbon) stays open to Write holders.
   - So the request names the Make Secure transition (an explicit field, e.g. `transition: "make-secure"`), and the endpoint holds that path to the Write gate. The wizard's create-then-secure path keeps the creator rule. Both are tested, and the guard bites by seeding.
   - The non-creator's own access after securing is exactly what owner round 27's copy says.
2. **R-150-2: the Remove Secure confirmation.** Recommended wording, applying owner round 27's stance (adjust in UAT). ONE constant, pinned verbatim:
   - **Title:** Remove the secure designation from this {record}?
   - **Paragraph 1:** The {record} and its related records return to normal access: people who can see records in its business unit will be able to see them, and the individual sharing set up while it was secure is removed.
   - **Paragraph 2:** To secure it again later, use Make Secure.
   - **Buttons:** **Remove Secure** (primary) · **Cancel**
3. **R-150-3:** the success lines ("This {record} is now secure." / "This {record} is no longer secure.") and the status-only fallback are accepted as written, adjustable in UAT.
4. **R-150-4:** external-spa eslint and vitest come with task 140's merge, which adds their configs and a vitest runner. Re-run lint and vitest after 140 merges into the integration branch.
5. **R-150-5:** an unknown skipped-principal reason code shows a generic per-person warning ("{name} was not given access to this project.") as well as the log entry. Never silent.

## Round 34 (2026-10-04). BINDING. Main-session decisions under round 15. The follow-up round's verifier questions.

1. **162 F-162-f1-1: deleting a document deletes its AI analyses and their outputs.**
   - `sprk_document_analysis_document` and `sprk_analysis_analysisoutput` go to `Delete = Cascade`, through a dry-run/-Apply/-Verify schema script that uses the solution-membership helper.
   - Probe with a non-admin test user that deleting a test document removes its analyses and outputs.
   - Analyses with no document (personal, round 15) are untouched.
   - Work for the integration lane after 162 merges; the live `-Apply` is a manual gate.
2. **162 §14.5: ratified.** A rights-query FAULT on promote's session-document check answers 403 `sdap.access.error.system_failure`, the same body the filter gives for the same fault on a body document. A missing right or a missing row still answers `insufficient_rights`. A fault is not a deny (ADR-003, still fail closed).
3. **140: an internal Write holder who changes a contact-issued grant row OUTSIDE the grant core takes it over,** exactly as `/grant` does.
   - This includes `POST set-record-share-expiry`'s bulk expiry update.
   - "Takes it over" means: clear `sprk_grantedbycontact` and stamp `sprk_grantedby` with the changing systemuser, in the same write, so the contact can no longer revoke an internal decision.
4. **167 ledger, retired routes:** an entry whose route key is absent passes only when `ResolvedBy` is set AND `ProofTest` names a test that pins the route's absence. This covers the nine routes deleted under round 10 item 1 and 164's deletions. Anything else absent still fails ("do not drop or re-key").
5. **167 ledger, admin policy:** `Credit.AdminOnly` is accepted for a sweep entry whose route is in a pinned `AdminOnlyRoutes` set, each resolved by `RequireAuthorization("SystemAdmin")` or the SPE admin policy (owner round 9 item 3). It is no longer limited to `/api/spe/`. The set is pinned by a test that fails on a route added to it without that policy.
6. **163's CreateEventWizard defect** (`sprk_Event@odata.bind` on `sprk_document`, which has no such column; its event lookup is `sprk_relatedevent` / `sprk_RelatedEvent`) is fixed by **task 147**, which moves that writer onto the BFF (round 28). The BFF create binds `sprk_relatedevent`, with a test.
7. **167 `/healthz/catalog`:** the HealthCheckResult is memoized for 30 seconds (one result shared across callers; a fault is not cached beyond the same 30 s), as well as the `health-probe` rate limit. One anonymous IP can then drive at most one catalog read set per 30 seconds. Tested with a fake TimeProvider.

## Round 35 (2026-10-04). BINDING. Main-session decisions under round 15. Task 165's five verifier questions, and task 147's Q2.

1. **165 Q1: every container-creation path stamps, not only the BFF's.**
   - The control-plane H8 handler (`GraphContainerTypeProvisioner`), `scripts/New-BusinessUnitContainer.ps1` and `scripts/Provision-Customer.ps1` stamp the container they create with round 20's property and the same rule: read back, and remove the container if the stamp does not read back.
   - The property name is ONE constant on each side (C# and PowerShell). A guard test fails on a container-create call site in `src/` or `scripts/` that does not stamp.
   - Task 165 owns all three. The H8 change is recorded in `customer-provisioning-orchestration-r1`'s notes for that project.
2. **165 Q2: an UNBOUND container is reachable by no admin route.** This amends round 20 item 2 ("an unbound container only a ROOT-unit admin").
   - **Why:** under Model 1, the root admin of ANY environment whose config names a shared type would otherwise reach another customer's unbound containers.
   - **The rule:** the per-container routes answer the uniform 404 for an unbound container, and log reason `unbound`.
   - **Binding:** binding happens only through creation stamping (item 1) and the backfill script. The backfill gains an explicit `-Bind <containerId>=<businessUnitId>` input for containers whose owner cannot be derived, and its `-Verify` lists every container still unbound.
   - **Gates:** backfill `-Apply` plus `-Verify` exit 0 in every environment is a manual gate BEFORE 165's BFF is deployed. The same gate applies before a further environment is onboarded onto a shared type, and it is written into the onboarding guide.
3. **165 Q3: `sprk_keyvaultsecretname` is allow-listed.**
   - Allowed names use ONE pinned prefix, taken from the existing configs' naming (read the live configs read-only to choose it).
   - Config POST/PUT answer 400 for any other name. At read, the BFF refuses to resolve a non-conforming name: it fails closed with its own reason code and never reads the secret.
   - A `-Verify` script lists the live configs that do not conform; renaming them is a manual gate.
4. **165 Q4: the security-alerts and secure-score routes are platform-operator-only.** They require a ROOT-unit admin, through the same check as the environment write rule (round 16 item 4). Tests + seeds.
5. **165 Q5: the container-type permission and consumer READS get the Write rule.** `GET /containertypes/{typeId}/permissions` and `/consumers` require every config of the type to be reachable by the caller.
   - **Why:** these routes list every customer's consuming app.
   - The test `ALeafAdmin_ReadingItsOwnSharedContainerType_ReachesTheHandler` is replaced by its refusing counterpart, plus a positive test for an admin who reaches every config of the type.
6. **147 Q2:** answered by round 28 item 1 as written. Every browser-initiated child create through the BFF is owned by the team `RecordOwnershipResolver` names. For a child of a NON-secure parent, that is the business-unit default team (I-6). Children are no longer user-owned on that path.

## Round 36 (2026-10-04). BINDING. Main-session decision under round 15. Event re-files after task 159 deleted `PUT /api/v1/events/{id}`.

1. **The re-file gets its own route in 159's events family.** Round 28 routes event re-files "through 159/161's routes; add the route there if the family lacks it; never a second one". 159 deleted the general PUT (round 10 item 1: no caller), so task 147 adds ONE route to `Api/Events/EventEndpoints.cs`, for example `PUT /api/v1/events/{id}/regarding`.
   - **It changes only the filing:** the regarding lookups and their ADR-024 pair.
   - **What it carries:** 159's as-caller checks (Write on the event; AppendTo on the new parent). It also restores the two checks that went with the deleted PUT: 146's re-file F3 gate and 156's re-stamp. Then 148/149's child pass runs when the event moves under or out of a secure parent.
   - **Absence pins:** 159's pins keep proving that the general PUT/DELETE/cancel/logs routes stay deleted. They are narrowed so they do not match the new route.
   - **Bookkeeping:** the new route gets its route-ledger row for 167.
   - **Base:** 147 merges `integ/uac-r2-batch4`, which now carries 159–164, before it builds the route.
2. **Communications** keep 161's routes. If 161's family lacks a re-file route, the same rule applies.

## Round 37 (2026-10-04). BINDING. Main-session decisions under round 15. Task 166's relocation questions (verify of `task/uac-r2-166-f1`).

1. **A relocated file is re-indexed as part of the relocation (option a).**
   - After each re-point, `DocumentContainerRelocator` calls ONE `Services/Ai/PublicContracts` facade method, which:
     - enqueues RAG indexing for the new item;
     - deletes the old item's chunks.
   - **No new facade if one fits:** extend an existing PublicContracts indexing facade if there is one (CLAUDE.md §11). Add a new method only if none fits; never inject AI internals (ADR-013).
   - **Failure:** an indexing failure does not undo the move. It is reported per file in the relocation report as `index-pending` and counts as incomplete, so the repeat call retries it.
   - **Every reference to the old item is re-keyed in the same step** (finding F4). Examples: a child attachment's `sprk_parentgraphitemid`, and any other column or index that holds the drive/item id. The note lists each one.
   - The "interim alternative" (re-index by hand after gate 24) is rejected.
2. **Several `sprk_document` rows pointing at the file being moved (option b, refined):**
   - Every referencing row inside the secure record's subtree is re-pointed to the copy, through the pointer-attach path.
   - A row OUTSIDE that subtree keeps the source. The source is then that other record's file, not the secure record's, so the secure record's transition is COMPLETE.
   - The report lists each source kept for another record (`SourceKeptForOtherRecords`, with the row ids). The source is deleted only when no row references it any more.
   - **Re-entry (F2):** a repeat call must recognise a row that is already re-pointed and settle it. It never loops on, and never reports incomplete for, a source that is kept for another record.
   - Option (c), delete anyway, is rejected.
3. **Interim pointer rule versus relocation (option a, F1):**
   - The interim rule ALSO accepts an item uploaded by the BFF identity, but only when its pointer passes the strict derived-container test. That keeps the interim rule never weaker than the strict rule.
   - Relocated files are therefore served before the strict flip (gate 25).
   - Tests: a relocated file is served under the interim rule; a BFF-identity item in the wrong container is refused. Seeded.

## Round 38 (2026-10-04). BINDING. Main-session decision under round 15. Task 168, grid lock coverage (note §12.6).

1. **The grid lock covers the same columns the forms hide.** Round 25 item 8 named only the four root columns for the grid. But a person can add the raw filing columns to the `sprk_event` and `sprk_analysis` grids with Edit columns: the ADR-024 pair and the non-root `sprk_regarding*` lookups. Those columns would then be inline-editable, which reopens the door the form lock closes.
   - `SpaarkeGridCustomizer`'s RootColumnLock covers that full set, BY NAME and from ONE shared list. The list is the one the form scripts use, never a second copy.
   - It uses the same cell editor and cell renderer overrides as the root columns.
   - Version v1.1.1, bumped in all four places. Jest cases, plus a seed proving each override bites.
   - The grid configuration script is unchanged.

## Round 39 (2026-10-05). BINDING. Main-session decisions under round 15. Task 158, from its second verification.

1. **Unsecuring a parent ends what it passed on (option b).** Interpretation xiii in note §6 is reversed.
   - Unsecure Step 4 revokes each explicit share on the parent. For each sharee it revokes, round 30's reverse rule then runs on the secure records filed under the parent.
   - **Ended:** only the UNMODIFIED inherited share.
   - **Kept:**
     - a direct share;
     - a raised mask;
     - a share another secure parent still justifies;
     - the record's last reader. Owner round 3 S5: never strand a record.
   - Failures report through `children_incomplete`, as round 30's do.
   - **Why:** after the unsecure, the filed records are still secure. A sharee's access to them came only from the parent share that was just revoked. Round 30 calls that "a former sharee with access they should have lost". Seeing the now-ordinary parent through its business unit gives no access to a still-secure filed record, so option (c)'s exception does not apply.
   - Tests and seeds per kept/ended case, plus the fault case.
2. **DIRECT shares on a filed secure record honour every secure parent's No Access list too (option b).** This extends round 31 item 1, which covered the creator, to every share.
   - It applies to `/share-user`, and to colleagues named in `/provision-project` on a filed root.
   - The check runs against the record's own list AND every secure parent's list. It refuses with the existing No Access codes and round 29's copy, and an unverifiable list fails closed.
   - Use ONE guard entry point (`SecureShareNoAccessGuard`), never a second copy of the parent walk.
   - Tests and seeds.

## Round 40 (2026-10-05). BINDING. Main-session decisions under round 15. Task 150's integration lane, from its verification.

1. **R-150-6: a Make Secure that fails after the flag is written can always be finished (option 1, completed).**
   - **The ribbon:** it also shows Make Secure when `sprk_issecure` is true and `sprk_containerid` is empty. It reads both in the same `retrieveRecord`, and a read failure follows the existing fail-closed enable rule. Acceptance (e) becomes "hidden on a PROVISIONED secure record".
   - **Retryable failures after Step 7** (`files_incomplete`, `children_incomplete`): the alert becomes a confirm dialog showing the server's detail, with **Make Secure** / **Cancel**. That re-entry is the repeat call round 26 promises.
   - **Every post-flag failure code is covered.** The note gives a table of the five codes, and for each names which closes it:
     - the ribbon's retry;
     - a scheduled job, by name and schedule (148's `SecureChildReconciliationJob` for children; for files, 166's relocator re-entry);
     - both.
     A code with neither is a defect to fix.
   - Tests and seeds.
2. **A non-creator who runs Make Secure keeps access, on BOTH paths (option a, made symmetric).**
   - The caller is shared at the creator's level on the forward path (share first, move, prove). The unflagged RESUME path now does the same; the asymmetry is removed.
   - The caller is then one of "the people it is shared with" in owner round 27's copy, so the copy holds as written.
   - Option (b) is rejected: a Write holder who secures a record would lose it.
   - Tests for both paths, with seeds.
3. **`sdap.provision.principal_share_failed` copy accepted as recommended** (owner round 27's stance: adjust in UAT). In the wizard: "{name} was not given access to this project. You can share it with them later from Manage Access." The ribbon uses the same sentence with {record}, and round 29's two sentences are generalized to {record}. Each is pinned verbatim.
   - An empty or unresolvable name falls back to "Someone" (finding: `personName("")` produced an empty name). Never an empty name, never silent.

## Round 41 (2026-10-05). BINDING. Main-session decisions under round 15. Task 165, from its verification of `task/uac-r2-165-f2`.

1. **Round 35 item 1 includes H8's replication-pending path, and task 165 fixes it.** It is not handed to another project.
   - Today, on that path, a container is created and then never stamped and never removed.
   - It must be stamped and read back before anything durable consumes the container (Key Vault write, H7 hand-off, `CompletedPhase`), or removed.
   - The H8 code lives in this repository, and round 35 assigned it to 165.
   - Correct the guide and the note D19 to match the code.
2. **Unbound containers leave every dashboard view (option b, D18).** That includes the platform operator's aggregate `unattributedContainerCount` and storage.
   - Under Model 1, the root admin of ANY environment is that environment's platform operator. An aggregate over unbound containers of a shared type therefore counts other customers' containers.
   - The alarm is the backfill's `-Verify`, which lists every unbound container to the operator who runs it, plus the pre-deploy/onboarding gate (round 35 item 2).
3. **The three dev test containers whose owner cannot be derived** (`b!DcvT…`, `b!rAta…`, `b!c8YR…`) are BOUND to the root unit `06fbf21c` ("Spaarke") with `-Bind`, as a manual live step.
   - Binding is reversible and deleting is not, so nothing is deleted.
4. **Dev config `68f9a952` stores `null` as its secret name.** Its owning app's secret is stored under a conforming name (for example `spe-owning-app-model1-owner`), and the config is PATCHed to that name.
   - Deliver a script (dry run / `-Apply` / `-Verify`) and a manual gate. Never delete the config.
   - If the read-only investigation proves the config is used only by the control plane, say so in the note with the evidence, and still make it conforming.
5. **The verifier's LOW items are closed in the same round:**
   - a test that bites for the platform-operator `TotalCount` guard;
   - the creation guard's two holes: a URI held in a variable, and the other seeded shape;
   - the allow-list regex anchored with `\z` (not `$`), with a trailing-newline test;
   - the H5/H8 root-business-unit read, checked in live gate (d).

## Round 42 (2026-10-05). BINDING. Main-session decisions under round 15. Task 140, from its verification.

1. **The race between a contact's write and an internal take-over is closed (option A).**
   - The contact-mode grant PATCH and the contact revoke's deactivation both send the row's ETag as `If-Match`, using the ETag from the read that checked "issuer == caller".
   - A 412 means the row changed in between. The answer is then 409 `managed_elsewhere`, with the existing copy, and the row is re-read for the response. There is no blind retry.
   - Option (B), re-read only, narrows the window but does not close it. Option (C) accepts a known hole.
   - Tests: a take-over between the check and the write, on both the grant and the revoke path. Seeded.
2. **The reconciliation job (R1) and a contact-issued row with no expiry: the contact's cap applies (neither A, B nor C).**
   - A contact-issued grant may never outlive the issuing contact's own access (task 140's rule: capped at its level and its own expiry).
   - So when R1 writes are on and it finds a contact-issued row with a null expiry:
     - It re-stamps the expiry to the EARLIER of +90 days and the issuing contact's own active grant expiry on that record.
     - If the issuing contact no longer holds an active grant there, it deactivates the row.
     - It keeps the contact as issuer, because no internal person acted.
     - It reports each such row.
   - Option (A), skip, leaves an open-ended contact grant. Option (B), take over with no person to stamp, invents an issuer.
   - Correct the three places that say the job only DEACTIVATES grant rows.
   - Tests and seeds per case: re-stamped to +90, capped by the contact's expiry, issuer contact gone.

## Round 43 (2026-10-05). BINDING. Main-session decision under round 15. Task 167, from its verification of `task/uac-r2-167-f2`.

1. **Every AnonymousByDesign route's compensating control is enforced by the build (option A).** Round 12 item 1 keeps the strict rule. Under round 15, a mandatory control that only a waiver's text names is not enforced.
   - For each explicitly anonymous route, the guard pins the control the waiver names, extending the pattern `HealthProbeRoutes` already uses for `/healthz`, `/healthz/catalog` and `/ping`. That covers all 13 others, among them `/healthz/dataverse`, `/status` and `/api/config`. A control is one of:
     - a named rate-limit policy;
     - `RequireWebhookSignature`;
     - an `IsDevelopment`-only mapping.
   - The test fails when the control is removed, swapped for a looser one, or applied in a form the guard cannot see (for example by attribute). Seed each kind.
   - The waiver text and the pinned control must agree: a test fails when they differ.
2. **The time-bound observation is closed too:** `MemoizedHealthCheck`'s shared evaluation gets an upper time bound, a registration `Timeout`, or both. A hung evaluation ends as a cached fault for no longer than the memo window (round 34 item 7), never as a permanently stuck state. Tested with the fake `TimeProvider`.

## Round 44 (2026-10-05). BINDING. Main-session decision under round 15. Task 168, from its re-verification.

1. **The grid script requires SpaarkeGridCustomizer v1.1.1 (option a).** Round 38's "the grid configuration script is unchanged" means the CONFIGURATION it writes is unchanged.
   - Raising `$MinCustomizerVersion` to 1.1.1 is required: with v1.1.0, `-Verify` would pass while the pair and the intermediate lookups stay editable (ADR-003, fail closed).
   - **Integration work (with the 168 merge):**
     - A lock-step test pins `$MinCustomizerVersion` to the PCF manifest version.
     - A C# lock-step test ties `config/regarding-filing-columns.json` `rootColumns` to `CoreAncestorResolver.CoreAncestorLookups`.
     - The picker derives `$RequiredPairColumns` from the shared list without the hard-coded exclusion, or fails with a clear message naming the column a table lacks.

## Round 45 (2026-10-05). BINDING. Main-session decisions under round 15. Task 166 relocation, from its re-verification.

1. **A moved file keeps its version history (option a, completed).**
   - **Replay:** the relocator replays the source's prior versions into the copy through the BFF identity, oldest first, with the current content last.
     - It lists versions app-only with the existing `SpeFileStore.ListFileVersionsAsync`.
     - It adds ONE app-only facade method, `DownloadFileVersionAsync`, to the existing SpeFileStore facade.
     - Each upload's size is checked.
   - **Failure:** a failed replay deletes the copy and leaves the row and the source untouched (Failed, retried).
   - **Original authorship:** Graph cannot set a version's author or date. So the relocation ledger records each replayed version's ORIGINAL author, date and size against the new version id. `GET /api/documents/{id}/versions` reports the original author and date from that record, so the history a user sees is unchanged.
   - **Version limit:** when the target container's version limit is lower than the source's count, the relocator reports `versions-truncated` with counts. That is a stated outcome, never silent.
   - Tests and seeds: replay order, a failure that deletes the copy, author/date mapping, truncation.
2. **Each in-subtree row gets its OWN copy (as built; ratified).** Round 37 item 2 said "re-pointed to the copy". One shared copy is impossible while the unique key `sprk_graphitemid_uk` (on `sprk_graphitemid` alone) is active, and the key is correct. The note states this.
3. **A `sprk_communicationattachment` row that names the moved file but is not linked to the document is classified by subtree (option a).** The rule follows the subtree of the record its communication is regarding:
   - inside the secure record's subtree: re-key it to the copy, or report it pending;
   - outside: keep it (the source stays for it);
   - undecidable: pending.

   This is the same split as round 37 item 2.
4. **A repeat call deletes the source using the witness recorded at verification (option a).**
   - The relocation ledger records the source's size and quickXorHash when the copy is verified. A later repeat call deletes the source only if it still matches that witness.
   - If the source was edited after the move, it no longer matches. That is reported as `source-changed-after-move`, with the row id, and the source is not deleted.
   - That case closes through the relocator's own re-entry: re-copy, verify, re-point. Never by a manual admin step.
5. **The verifier's other items are closed in the same round:**
   - Guards V6/F-C: real tests that fail when each guard is removed.
   - The two client pointer-write shapes the guard and the FLS p4a scan miss (F-D).
   - The gate name in the deploy-order text (F-E, 23a).
   - **Integration note:** `IRelocatedFileIndexing` takes the last free slot under the ADR010 1:1-interface ceiling. At integration, register it without a 1:1 interface if ADR-010 allows; otherwise record the ceiling change in the PR's ADR block.

## Round 46 (2026-10-05). BINDING. Main-session decisions under round 15. Task 150's integration lane, from its re-verification.

1. **The caller's share after Make Secure is floored on their EFFECTIVE rights before the call (option b).**
   - Round 40 item 2 promises the caller keeps access. A4 says "never lower existing access". F3 decides Full Access from effective rights.
   - So a caller who held Full Access by ownership or role keeps Delete, through an explicit share at Full Access, exactly as one who held it by share.
   - The floor is read in the same pre-write step as WhoAmI. A read failure fails closed with the existing unverifiable code.
   - The caller never gets more than they held, and never less than Collaborate. Assign is never shared.
   - Correct the note and the guide to say this precisely.
   - Tests and seeds: share-held, ownership-held and role-held Full Access, Collaborate-only, and the read fault.
2. **The scheduled backstop for `files_incomplete`.**
   - The backstop is 147's `SecureChildReconciliationJob` (every 2 minutes). It is not a new job.
   - Each run, it also settles the PENDING Make Secure relocations recorded in the relocation ledger, through the ONE `DocumentContainerRelocator`, with a per-run cap and a report.
   - `-SecureTransitionDeployed` is gated on that job being registered with writes on.
   - Round 40 named 166's migration job, which is registered disabled, so it cannot be the backstop.
   - This is wired at integration, after 147, 150 and 166 are all on the integration branch.
3. **§23.4 confirmed (the broader reading).**
   - A Make Secure resume runs the full forward rule set before any write: WhoAmI, the caller's No Access check, the creator read, then the caller's proven share, with the creator in the colleague step.
   - An administrator who finishes a record through the ribbon is shared to like any caller.
   - A direct API call with no `transition` keeps the creator rule (F8, round 9 item 10 / round 33 item 1).
4. **§23.1: the client can tell a non-default owner team apart.**
   - Case: a secure root reassigned outside Spaarke to a non-default team.
   - `can-manage-access` returns the owning team id, and whether it is the Secure Record Owners team.
   - The ribbon offers Make Secure ("finish") when a flagged root has a container but is NOT owned by that team, just as it does when the root is user-owned.
   - The retry's forward path re-owns the root to the Secure team. That also settles a legacy record provisioned before task 133's owner move; test that path explicitly.
   - Accepting this as a documented edge is rejected.
5. **The untested fail-open branch:** the RESUME's unverifiable No Access refusal gets a test that bites when seeded.

## Round 47 (2026-10-05). BINDING. Main-session decisions under round 15. Task 158, from its re-verification of `task/uac-r2-158-r1c-v1`.

0. **Round 39 is still owed in full by task 158's next fix round.** That round's code predated it, so the verifier found round 39 not implemented. Correct the I-11 registry row and note §6 xiii to match it.
1. **E-158-v1-1: the complete fix in note §15.1 is applied, in task 158's lane.** 142 is merged into 158's branch and into the integration branch, and the change is small and local.
   - **(1)** 158's reverse rule counts an Assigned-To row as justification ONLY when it is `Shared` or `Adopted`, never `CoveredByExisting`.
   - **(2)** When 158 then removes the share:
     - 142's `CoveredByExisting` row becomes `Skipped`, with reason `covering-share-ended`.
     - 142's materializer runs at once. On a secure record, the assignee is suggested, not shared (owner rules).
   - **(3)** 142's `EndAssignmentAsync` calls the inheritance's sharee-only pass for a filed secure root.
   - Tests and seeds per step. Accepting the over-retention is rejected.
2. **E-158-v1-2: the operator-unshare race is closed.**
   - **Now, in task 158:** the `Declined` marker is written BEFORE the revoke.
   - **At integration, after 140 merges:** every pass's ledger update sends `If-Match`, with the ETag it decided on. A mismatch re-reads and re-decides; there is no blind write.
     - Use task 140's `If-Match` support on the Dataverse client (round 42). Never a second mechanism.
     - It is an integration-checklist item with tests and seeds.
3. **Seeding claims must be accurate.** The verifier's seeds X03, X07, X09 and X17 survived; X09 is confirmed over-retention. Each gets a test that bites.
4. **The copy defect** (a doubled period in the Office "removed" refusal) is fixed.

## Round 48 (2026-10-05). BINDING. Main-session decision under round 15. Task 132, the share-write eviction guard (re-verification of `task/uac-r2-132-f1-v1c-v1`).

1. **Close the bypass CLASSES, not one shape at a time.** Each round, the verifier found one more way to reach `DataverseWebApiService`'s POA share writes around the evicting seam. The guard must instead forbid the mechanisms such a bypass needs, and the claims must state exactly what is covered.
   - **(a) No `[UnsafeAccessor]` anywhere** in `src/server/**`. Spaarke has no legitimate use for it; it reaches private members regardless of visibility. An ArchTest (an IL scan for `UnsafeAccessorAttribute`) fails on any use. Seeded with seed U.
   - **(b) No reflection over the Dataverse service types** in the BFF: `GetMethod(s)`, `GetMember(s)` or `InvokeMember` on `DataverseWebApiService` / `IDataverseService` types (or on a `Type` obtained from them), and no `MethodInfo.Invoke` or `CreateDelegate` against them. This uses the existing IL-scan infrastructure (`IlCallScan`). Seeded with seed R.
   - **(c) The doc comments, the guard header and note §16.6/16.7** are rewritten to state exactly what is enforced: every compiled call path, plus a ban on `UnsafeAccessor` and on reflection over those types. Never "by construction" beyond that.
   - **(d) The gate script's `ReassignMatter -Apply`** never overwrites a recorded `OriginalOwner`: a second run keeps the first original. Tested.

## Round 49 (2026-10-05). BINDING. Main-session decisions under round 15. Task 165, from its re-verification of `task/uac-r2-165-f2-v1`.

1. **Routes that reach the whole tenant or the whole container type are for Spaarke's own operator environment only (option a).**
   - **Which routes:**
     - security alerts and secure score (round 35 item 4);
     - container-type permissions and consumers (round 35 item 5);
     - any other route whose answer spans the shared SPE tenant or a shared container type.
   - **Why:** under Model 1, every customer environment's root admin is a "platform operator" in their own environment (round 41 item 2). A root-unit check judged against one environment's own hierarchy therefore cannot confine these routes.
   - **The marker is a BFF DEPLOYMENT setting,** for example `SpeAdmin:PlatformOperatorEnvironment`. It is not a Dataverse column, because a customer admin can edit a column.
     - It is validated at startup, and its default is `false`, so a missing setting fails closed.
     - It is set `true` only for Spaarke-operated environments (dev, and Spaarke's own operator environment), in their App Service configuration through the deployment scripts and Bicep.
   - **Who may call them:** only a root-unit admin in an environment with the marker. Every other caller gets the uniform 403/404.
   - **Docs:** the topology doc and the customer deployment guide record that customer environments never carry the marker.
   - **Rejected:**
     - (b) needs a per-config "shared" column that a customer admin could clear;
     - (c) relies on an operational promise that cannot be enforced.
   - Tests and seeds: the marker off with a root admin; the marker on with a leaf admin; the marker missing.
2. **The verifier's criteria are owed in the same round:**
   - **H8 resume must survive the production Cosmos serializer.**
     - The resume record (root container id, container type id) is persisted as typed fields. Never as `JsonElement` evidence: Newtonsoft writes that as `{"valueKind":1}`.
     - The fake repository round-trips through the production serializer, so the tests see what production sees.
   - **A non-OData fault after the container type exists** still reports and records the type and any root container (no orphan type, no unbound root).
   - **The docs that claim "never creates a second container"** are corrected to match the fixed code.
   - **The creation guard's five surviving shapes** each get a bite: an absolute-URL raw POST, a class-level constant URL, a generic `PostAsJsonAsync<T>`, a cross-file PowerShell URL variable, and `-Meth Post`. D30's "fails closed on what it cannot judge" is made true, or it is reworded to exactly what is enforced.

## Round 50 (2026-10-05). BINDING. Main-session decisions under round 15. Task 140, from its re-verification of `task/uac-r2-140-x1-v1c-v1`.

1. **The grant-row expiry read defect is fixed on master NOW, as its own PR (option B).**
   - The defect: since task 023, `/revoke` and re-grants over dated rows throw. `sprk_expiresdate` is DateOnly / TimeZoneIndependent, and the read does not handle that wire shape.
   - The fix (`DataverseDateOnlyJsonConverter` plus `GrantRowWireTests`) has no schema dependency. It goes to master on a branch off master, through a PR, merged when the Router is green.
   - Task 140 then merges on top: the same code, no conflict.
   - Holding a live production defect behind 140's deploy gates is rejected.
2. **A deleted issuing contact: the grant's provenance survives the delete, and the grant ends.** None of the options as framed.
   - **Why not the framed options:**
     - (C) `Restrict` would block deleting a contact, including privacy deletions.
     - A cascade is ruled out by owner G2 (ii).
     - (A) and (B) leave an undated grant that has outlived its issuer.
   - **The fix, in task 140's schema script** (dry run / `-Apply` / `-Verify`, through the solution-membership helper):
     - Add `sprk_grantedbycontactid`, a text column holding the issuing contact's GUID.
     - Every write that sets or clears `sprk_grantedbycontact` sets or clears it in the same write (grant core, take-over, collapse).
     - `-Apply` backfills it from the existing lookups.
   - **R1:** a row whose `sprk_grantedbycontactid` is set while `sprk_grantedbycontact` is empty means the issuer was deleted. R1 deactivates the row and reports it.
   - **Justification (CLAUDE.md §11):**
     - *Existing:* the lookup, which `RemoveLink` erases.
     - *Extension:* the lookup cannot outlive the row it points to.
     - *Cost of doing nothing:* a deleted contact's grant becomes an indefinite, issuer-less grant.
   - Tests and seeds: issuer deleted, take-over clears both, backfill.
3. **The two unpinned guards get tests that bite:** V6, R2-ended in `Effective()`; and V11, the `Unknown()` guard for an unplanned issuer row.

## Round 51 (2026-10-05). BINDING. Main-session decision under round 15. Task 147, from its re-verification (`task/uac-r2-147-r1c-v1`, ready to merge).

1. **Descendants released on a move out: ratified as implemented (option a).**
   - A descendant leaves isolation only under the re-filed row's own F3 act: Full Access on the record it left, or the row's creator.
   - A row that was never isolated releases nothing.
   - This is round 24 item 2 ("never release without an F3 holder's act") applied to a re-file. The re-filed row's F3 holder acts for its subtree, as the unsecuring F3 holder does for a root's children (148).
   - **Rejected:**
     - (b) a per-descendant check: a descendant's isolation is inherited, not its own;
     - (c) never release: that strands a re-filed subtree.
2. **The side pane's partial-save rollback is fixed at integration.** When the filing saves and the other fields fail, rollback reverts only the fields that failed, never the persisted filing. Test it.

## Round 52 (2026-10-05). BINDING. Main-session decisions under round 15. Task 167, from its re-verification of `task/uac-r2-167-f2-v1`. The same principle as round 48: verify BEHAVIOUR against the real application wherever a guard claims behaviour. Source-text heuristics remain only with exactly stated limits.

1. **Sign-in is proven at run time.** This replaces trust in the `.Succeed(` text heuristic.
   - A test builds the REAL BFF authorization services (the real `Program` DI, as the existing real-Program tests do).
   - It evaluates an anonymous `ClaimsPrincipal` against EVERY registered policy, and every policy that endpoints reference, through the real `IAuthorizationService`. Every one must fail, except the pinned AnonymousByDesign routes, which use no policy.
   - This catches shadowed lambdas, reflection and any custom handler, whatever its shape.
   - Seed it with the verifier's handler that succeeds every requirement.
   - Keep the text rule as a fast first check, and state its limits.
2. **The IsDevelopment-only control is proven at run time.**
   - The real app boots with `EnvironmentName = Production`, and the test asserts from `EndpointDataSource` that no development-only route (the anonymous save-debug route and every other) is mapped.
   - Add an IL-scan ban on any write to `IHostEnvironment.EnvironmentName` / `IWebHostEnvironment.EnvironmentName` in `src/server/**`.
   - Seed both.
3. **Retired-route absence is judged at run time** (refines round 34 item 4).
   - A ledger entry whose route key is absent passes only when `ResolvedBy` is set AND the route is absent from the booted real app's `EndpointDataSource` (method plus pattern), checked by the guard itself.
   - `ProofTest` stays as the named regression test, but the guard no longer trusts its text. Unreachable assertions therefore cannot satisfy it.
   - Seed: a deleted route re-mapped turns the guard red.
4. **Presence evidence:** an `Authorization = null` assignment never counts as a bearer. Better still, presence is judged at run time from `EndpointDataSource` where the rule allows it. State the remaining heuristic's limits exactly.
5. Note 19.2 and the guard headers state exactly what is enforced at run time and what by text.

## Round 53 (2026-10-05). BINDING. Main-session decisions under round 15. Task 150's integration lane, from its second re-verification of `task/uac-r2-150-integ-c-v2`.

1. **The caller-rights read fault gets a provisioning code (option b).**
   - The code is `500 sdap.provision.caller_rights_unverifiable`, retryable, with the detail the worker wrote: "Which access you hold on this {record} could not be read, so securing it could not make sure you keep that access. Nothing was changed; you may try again."
   - It is added to `provisioningService.ts` and to the emitted-codes list, pinned verbatim. The ribbon gets the same copy with `{record}`.
   - **Why:** round 26 item 1 keeps codes namespaced by endpoint. F3's `sdap.unsecure.permission_unverifiable` belongs to the unsecure endpoint.
2. **A flagged root with a container, owned by another team INSIDE the Secure Record business unit, is already isolated (option b).**
   - Example: the retired default team, before task 144's migration.
   - **The behaviour:**
     - `can-manage-access` (`includeOwner`) reports it as owned by "another team inside the Secure business unit".
     - The ribbon HIDES Make Secure there: the record is secure and isolated, and the user has nothing to finish.
     - Moving the record to the named team is task 144's migration (script/job), which stays out of provisioning, as 144 decided.
     - The server's 409 `owned_by_other_secure_team` stays as the API answer.
   - **Integration checklist:** 144's migration `-Apply`/`-Verify` is a live gate before 150's ribbon ships.
   - Tests: the ribbon is hidden for that case; the server refusal is unchanged.
3. **The test gaps are closed:**
   - The ribbon's "who owns it cannot be told" table uses a fetch mock that returns a real Promise, so each case runs the guard it names: record-echo, null answer, and the others. Seeds C2 and C4 bite.
   - The ownership-held and role-held Full Access cases run DIFFERENT fixture paths, so a fake can tell them apart.

## Round 54 (2026-10-05). BINDING. Main-session decisions under round 15. Task 166 relocation, from its second re-verification of `task/uac-r2-166-f1-v2`.

1. **A post-move edit becomes current only if its author may still write (option a, ratified and tightened).**
   - Every post-move edit is carried into the history.
   - The LAST edit becomes current only when its author holds Write on the document NOW.
   - **That Write check is read FRESH, never from the 60-second `CachedAccessDataSource`.** A Write answer cached just before Make Secure must not make a non-writer's edit current.
   - An app-only, unknown or faulted answer means "not current" (fail closed).
   - Test: a stale cached Write answer cannot make an edit current. Seeded.
2. **The relocation lock lasts as long as the move.**
   - A move now replays every prior version, so a fixed 10-minute TTL can expire mid-move.
   - The lock is RENEWED while the relocation runs (a heartbeat with a bounded renewal interval). A move whose renewal fails stops before its next destructive step (re-point or source delete) and reports `lock-lost`. It never continues unlocked.
   - Test: a second relocator cannot start a move while the first still holds a renewed lock. Seeded.
3. **No intermediate edit is dropped.**
   - When the witness has no version id, or an id is not numeric, the relocator does NOT fall back to "current only". It orders the post-move versions by their timestamps.
   - If the order cannot be decided, it reports `history-undecidable` for that file and leaves the row and the source untouched (retried). It never silently carries only the latest.
4. **Every fail-closed branch gets a test that bites:** Sk, Sq, Si, Sm, Sj and Sw; the version-list paging (Sd: a long history is never cut at one page); and the Unknown → never-delete witness branch.
   - Note §22's "each new check seeded" / "every seed bit" claims are corrected to what is true.
   - The test header comment that still says "unless it is byte-identical" (F-3) is corrected.

## Round 55 (2026-10-05). BINDING. Main-session decision under round 15. Task 132, the share-write eviction, from its second re-verification of `task/uac-r2-132-f1-v1c-v2`.

1. **Move the eviction INTO the share-write primitive.**
   - **Why this, and not more bans:** three verification rounds found a new way around the guard each time (UnsafeAccessor; reflection; a VB late binder; a metadata token plus a compiled expression tree). Banning mechanisms one at a time cannot end, because any caller-side guard can be bypassed by a caller. The root-cause fix makes eviction a property of the write itself, so every route evicts, including routes no guard foresaw.
   - **The design:**
     - `Spaarke.Dataverse` gets ONE small hook interface, e.g. `IRecordShareWriteObserver`.
     - `DataverseWebApiService`'s POA share writes (GrantAccess, ModifyAccess, RevokeAccess, and any other) call that observer after the write, in a `finally`, on `CancellationToken.None`. An observer fault is logged and never fails the write, the same contract as the seam.
     - The BFF implements the observer with `IMembershipCacheInvalidator.InvalidateRecordShareChangeAsync`. It is registered unconditionally, with a Null-Object for hosts without the cache (ADR-032 P-pattern, and §10 asymmetric-registration rule F.1).
     - The current seam (`DataverseRecordShareService`) stops evicting separately, so no write evicts twice; or it becomes the observer itself. Choose whichever leaves ONE eviction path.
   - **Justification (CLAUDE.md §11):**
     - *Existing:* the seam evicts, but only for its own callers.
     - *Extension:* the observer extends the primitive the seam already wraps.
     - *Cost of doing nothing:* any non-seam route leaves stale access in the cache.
   - **Tests:**
     - A behaviour test proves an eviction for a write reached WITHOUT the seam: the verifier's seeds V and M, adapted as real tests calling the primitive by reflection and by expression.
     - An observer fault does not fail the write.
     - Exactly one eviction per write.
     - Seed: remove the observer call, and the tests go red.
2. **The IL guards stay as defence in depth,** but every comment, the guard header and note §16.8 now say what is TRUE:
   - eviction is guaranteed by the primitive for every route that goes through `DataverseWebApiService`;
   - the guards additionally stop a raw HTTP call to the share actions that bypasses that class. Enumerate exactly the shapes they check.
   - Drop "no method chosen or invoked by reflection" and every other claim the guards cannot back.
3. **The Unicode-escape `[UnsafeAccessor]` outside the scan set** (seed U5) is caught by scanning the compiled IL of EVERY `src/server` project, not the source text.

## Owner round 56 (2026-10-05). BINDING. The bar for finishing a lane, and over-engineering. REPLACES round 15's "never defer, never sideline" standard from now on.

The owner, verbatim: "it is important that we address issues that impact the quality, performance, and maintainability and extendability of the solution … However, the other risk is 'overengineering' and this is also a risk because it introduces complexity and unnecessary cost. This project has already exceeded the budget by 10x." The owner adopted the rule below and the round cap from now on, and chose to KEEP all work already decided or built (rounds 1–55 stand; nothing is reverted or simplified retroactively).

1. **Every finding is classified first.**
   - **FIX in the lane:**
     - **(a) Runtime defects:** wrong behaviour that a real user, operator or job path can trigger. That includes security gaps, fail-open, data loss and cross-customer exposure.
     - **(b) Maintainability defects that compound:** duplicate mechanisms, docs or comments that contradict the code, important behaviour with no behaviour test, fragile coupling.
     - **(c) Performance problems** with real, measurable impact.
   - **RECORD as a known limit** (one line each in the task note's "Known limits" section and the PR description; no fix round):
     - **(d)** guard or static-analysis bypasses that only deliberately adversarial code in our own repository can reach (reflection, late binding, unsafe accessors, unusual spellings);
     - **(e)** rare edges that already fail closed and have no realistic trigger;
     - **(f)** requests to seed-prove minor, non-security branches.
2. **The over-engineering check.** Any NEW column, job, setting, abstraction, service or guard must pass CLAUDE.md §11's three questions against a realistic failure. A fix is never bigger than the problem it solves. When two fixes work, choose the simpler one. Verifiers report over-engineering as a finding.
3. **The round cap.** From now on, each lane gets at most ONE more fix round after its current one. Whatever remains at the cap is classified: (a)–(c) items are escalated to the main session, and (d)–(f) items go to Known limits.
4. **Process.** Verifiers classify each finding (a)–(f) and lead with (a)–(c). Fixers fix (a)–(c) and record (d)–(f) as known limits, without building new machinery for them.
5. **Batch 5:** scope review before starting (owner, 2026-10-05). For each of the 29 tasks: what it delivers, its risk if dropped, and keep / merge / cut. The owner decides.

## Round 57 (2026-10-05). BINDING. Main-session decisions under owner round 56. Task 165, from its final re-verification of `task/uac-r2-165-f2-v2`.

1. **Only dev carries the operator marker for now (option c).**
   - Spaarke's production operator environment does not exist yet: `spaarke-bff-prod` / "demo" is stopped.
   - When it is stood up, the marker is added in the same change that stands it up (registry key, `.bicepparam`, the guard's list, the App Service setting).
   - Until then, every other environment refuses the 8 routes. This is the smallest fail-closed choice.
2. **D38 accepted (option a).** The DELEGATED container-type routes stay outside the marker.
   - Graph authorizes them with the caller's OWN token, and the BFF lends no identity, so the caller's SharePoint Embedded administrator role is the boundary.
   - Gating them would only remove legitimate screens.
3. **Fixed in 165's final round** (owner round 56: class a or b):
   - **The deploy script's marker parse fails open.** `[bool]"false"` is true. `Deploy-BffApi.ps1` accepts only a JSON boolean and fails on any other type. The registry ArchTest rejects string values.
   - **Security fail-closed branches with no biting test.** These get tests (seeds VB5, VB6, VB7 and VL7 must turn red):
     - the container-type rule's Unverifiable → 503 path;
     - the scope read-fault path;
     - the 5000-row truncation path;
     - `QuarantineClearedSince`'s time clause.
4. **Known limit** (class d, no fix): the creation guard does not see a `System.Net.WebClient` upload written by deliberately unusual script code.

## Round 58 (2026-10-05). BINDING. Main-session decisions under owner round 56. Task 158, from its final re-verification of `task/uac-r2-158-r1c-v2`.

1. **A No Access entry added LATER also ends the walled person's direct shares on secure records filed under that parent (option a; class a, security).**
   - Task 143's `NoAccessShareEnforcer` already removes the person's share on the walled record and syncs its children.
   - It now also reaches secure work assignments and projects filed under that record, using the ONE existing parent walk (`SecureRootInheritance.ReadSecureParentsAsync`) and the existing revoke.
   - It never strands the last reader (S5). Failures report as children-incomplete.
   - Round 39 item 2 says every share honours every secure parent's list, and a share-time check alone would leave a wall that does not wall. No new mechanism.
2. **Fixed in 158's final round** (class a and b):
   - **(a) The failed-revoke regression from round 47 item 2.** If the revoke after the `Declined` marker fails or is not confirmed, the marker is reverted, in the same request (`finally`). A share the operator did not actually remove is never marked declined, and the parent's unshare still ends it.
   - **(a) Step 4.5's by-parent ledger read** counts only rows still in force. It excludes Revoked and ended rows in the query, so a large provenance history cannot wedge the unsecure. Truncation of rows still in force still fails closed.
   - **(b) The already-ordinary-path 500 copy** no longer says "Calling again completes it" where a repeat call cannot. State what the operator must do instead.
3. **Known limit** (class f): the Revoked filter in `EndWhatAParentPassedOnAsync` (seed Z28) is equivalent by construction. No test is added.

## Owner round 59 (2026-10-05). BINDING. Batch 5 scope (review: `notes/batch5-scope-review-2026-10-05.md`).

**The owner, on the standard:** "The key to this review is making sure we are not over-engineering; but if the work is required then we need to do it — this isn't meant to take short cuts or remove important functionality." There is no fixed budget: "we need this functionality to be robust and comprehensive because it is what makes the system broadly usable." Owner round 56's bar stands, read with this clarification: required functionality is built fully; machinery that answers no realistic failure is not.

1. **036 is built (owner).** At least what is needed so that internal users in Teams or the external app put core records at no risk.
   - **Read set:** Dataverse decides which records an internal user sees, through the impersonated root set (FR-20), behind the flag.
   - **Writes:** EVERY write route (create to-do, upload, create event, every other write in the external/Teams surface) checks the caller's real Dataverse rights on the target record, through the existing `CallerRecordAccessProbe`, before writing. No write relies on the fixed Read|Write|Create grant.
   - Truncation reuses the existing `Capped` / `CapCeiling`. The runbook goes in `SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`.
   - The ADR-034 amendment (§6.5 path B) is put to the owner with the task.
2. **Deferred to a new project (owner): 056, 088, 089, 094, 095.**
   - Each is filed as a GitHub issue.
   - FR-32 point-in-time replay stays formally unmet in this project. Dataverse audit keeps accumulating, so no data is lost.
3. **The promised features are kept (owner): 153, 101, 067, and 064's single per-record read that feeds them; also 099.**
   - 153 covers project, matter and work assignment only. The Organization/Contact banners are cut: walls there are visible in 154's subgrids.
   - 067 is a read-only No Access section plus suppressed and vetoed rows (066 is folded in). Authoring stays in 154's form, so there is ONE place to author.
4. **154 keeps a record picker (main session, applying this round's "no removal of important functionality").**
   - Pasting record GUIDs is not usable, and it is how the silent-wall defect arises.
   - Reuse the EXISTING polymorphic record picker (the RegardingResolver / PolymorphicPicker the filing forms use). Do not build the new ObjectRecordPicker PCF.
   - The save-time id check, and the reader rejecting a non-canonical id, are kept as well.
   - The display-name column `sprk_objectrecordname` is kept ONLY if the picker cannot show the record without it.
5. **Kept as reviewed:**
   - 105 (paging);
   - 113 (ISS-028);
   - 114 (with its owner decision on the licence proxy and Assigned-To);
   - 136 (live session only);
   - 090 (trimmed; it absorbs 058, 082's paragraph, 087's doc correction and 089's known-gap line).
6. **Merged as reviewed:**
   - 003 → 130's live gate;
   - 013 → 141 G-8;
   - 037 and 039 → 136's CIAM session;
   - 047 → the batch-4 deploy gates;
   - 058 → 090;
   - 066 → 067.
7. **Cut as reviewed (each closed with its reason):**
   - 054: known limit (fails closed); close #964.
   - 055: the stamp already does it; the per-child block is a known limit.
   - 057 and 069: already tested, or nothing left to test.
   - 082: PR #840's guard exists.
   - 087: Dataverse audit is the source.
   - 110: met by 109; close #999.
   - 111: no measurable impact.
   - 112: rare and toward less access; with 140's If-Match now available it can be revisited cheaply if it ever occurs.
   - 170: rename only. The duplicate record filter it exposed goes to the new project as a class (b) item.
8. **Order:**
   - 154 (security fix first);
   - 113, 114, 105 and 101 in parallel;
   - 064, then 153 and 067, then 099 (one PCF deploy);
   - 036 once batch 4 is deployed (it can start alongside);
   - 090 last.

## Round 60 (2026-10-05). BINDING. Main-session decisions under owner rounds 56/59. Task 150's integration lane, from its final verification (`task/uac-r2-150-integ-d`, ready to merge).

1. **Wizard copy for `sdap.provision.caller_rights_unverifiable` stays as written (option a).**
   - The wizard reads: "Which access you hold on this project could not be read, so securing it could not make sure you keep that access. Nothing was changed."
   - The retry is offered by the host action, under the FR-31 copy rule (the message never advises trying again).
   - The server and the ribbon keep round 53's full sentence.
   - The wizards send no transition, so they do not receive this code today.
2. **The release order for 150's ribbon versus task 144's migration (option b).**
   - **The ribbon ships once the DEFAULT-TEAM part of 144's migration is complete:**
     - the dry-run plan has no MIGRATE rows;
     - the retired default team no longer holds the Secure Record Owner role.
   - **The full `-Verify` exit 0 is run AFTER that,** once the ribbon's Make Secure "finish" has settled the NOT-ISOLATED rows. These are user-owned legacy records, records owned by a team outside the business unit, and flagged records left before the owner move.
   - **Why:** the ribbon is the tool that finishes those rows. Gating it on their absence would require settling them by hand first, which is a circular order.
   - **Docs that follow this answer:** the README, guide §7d.1, `Set-AccessRibbon.ps1` help and note §9 G-11. Updated at integration.

## Round 61 (2026-10-05). BINDING. Main-session decisions under owner rounds 56/59. Task 158, from its final verification (`task/uac-r2-158-h`, ready to merge).

1. **A No Access entry on a secure parent reaches EVERY secure record filed below it, at any depth (option b, transitive).** This is class (a), security.
   - Example: a work assignment filed under a project that is filed under the matter. Projects and work assignments can be filed under each other (task 158), so chains deeper than one level exist. A one-level reach leaves a walled person's direct share on the grandchild.
   - **The same rule at share time:** `CheckRecordAndSecureParentsAsync` checks EVERY secure ancestor's list, not only the direct parent's. This is round 39 item 2, read as "every secure parent" up the chain.
   - **Implementation:**
     - The ONE existing walk (`SecureRootInheritance.ReadSecureParentsAsync` upward and `ListFiledRootsAsync` downward) iterates level by level.
     - It is cycle-safe (a visited set) and bounded by a small maximum depth. Reaching the bound reports `children-incomplete` / Unverifiable, which fails closed.
     - No second walk, no new service.
   - **Flapping:** a record is enforced once per run (the existing reached set), so the job does not flap.
   - **Tests:** a grandchild direct share removed by the matter's entry; a grandchild share refused at share time; a cycle; the depth bound.
   - **Done at integration** as part of the 158 merge, not as another lane round.
2. **Interpretation xxxiv is ratified (option a):** N5 for filed records requires the entry author's Write on BOTH the walled parent and each filed record. An entry never strips access on a record its author cannot change.
3. **Known limits** (class e/f): the verifier's surviving seeds V01, V07, V08 and V09, and the two LOW items. They are recorded in 158's note, and the 5-minute job covers each one.

## Round 62 (2026-10-05). BINDING. Main-session decisions under owner rounds 56/59. Task 165, from its final verification (`task/uac-r2-165-h`, at the round cap).

1. **The operator marker is bound to the App Service it is deployed to (option A; class a, fail-open).**
   - **The defect:** `Deploy-BffApi.ps1` reads the marker by the `-Environment` LABEL, which defaults to `dev`. A deploy that names another App Service without `-Environment` would set `SpeAdmin__PlatformOperatorEnvironment=true` on it.
   - **The fix:** when the marker is declared true, the script refuses unless `-AppServiceName` and `-ResourceGroupName` equal that registry entry's `appServiceName` / `resourceGroup`. Put this in the existing block or in `scripts/common/SpeAdminOperatorMarker.ps1`.
   - **Test:** a seedable ArchTest case through the existing PowerShell driver (the marker is declared true, the App Service differs, the deploy refuses).
   - **Rejected:** option (B), making `-Environment` mandatory, changes every existing dev invocation for no extra safety once (A) is in place. Option (C) leaves a fail-open path.
2. **The stand-up recipe is corrected (class b).** Guide §6.5.4 and the comment on the `demo` entry in `config/environments.json` must say that the stand-up change also makes that registry entry reachable through `-Environment`, since `demo` is not in the `ValidateSet`. The same mismatch exists in the CORS comment and is fixed there too.
3. **Known limits** (165 note §15.5):
   - Class (d): a case-variant spelling of the registry key. The parse is case-insensitive and the ArchTest is case-sensitive.
   - Class (f): no test proves the deploy exits on an invalid declaration. It fails closed anyway.
4. **Done at integration** as part of the 165 merge (the lane is at its cap).

## Round 63 (2026-10-05). BINDING. Main-session decisions under owner rounds 56/59. Task 132, from its final verification (`task/uac-r2-132-g`, ready to merge).

1. **Known limit (class f, option A):** the observer's `CancellationToken.None` contract is not pinned by a test. The current invalidators ignore the token, so there is no runtime effect. Add one line to 132's note "Known limits" at PR time; no code change.
2. **The L2 guard gap is filed with its owner (option A): #1310.** `CosmosProvisioningSecretGuardTests` resolves DLLs by directory name and skips the L2 Api assembly. It belongs to the customer-provisioning lane, and is not fixed here.
3. **Integration note (148 × 132):**
   - Since round 55, every share write made through `DataverseWebApiService` evicts by itself. 148's child share mirroring and removal are therefore covered without any change.
   - Child OWNER changes (re-own on secure/unsecure, the job) still call `InvalidateRecordOwnerChangeAsync`, using `CancellationToken.None`. This is checked and tested at the 132 merge (the "148 × 132 child evictions" item).

## Round 64 (2026-10-05). BINDING. Main-session decisions under owner rounds 56/59. The 150 + 166 merges and the Make Secure relocation wiring (integ `9fec61fec`).

1. **`PUT /api/v1/documents/{id}` is KEPT,** although 166 deleted it as having no caller.
   - **Why:** round 28 makes it Compose's document re-file route (147).
   - **What it keeps:** 146's AppendTo check. Any body that names a storage-pointer field is refused with 400 `sdap.documents.pointer_field_refused` before any write (round 21's F0).
   - **PR text:** it is not in the "routes the sweep deleted" list.
2. **Documents whose file cannot be found never block Make Secure.**
   - They are reported as `filesUnresolvable` and are never moved, re-pointed or deleted. That includes archive rows holding a document GUID, word-add-in-r1's archive bug.
   - Otherwise a record with one such row would answer `files_incomplete` forever.
3. **The backstop also settles isolated documents whose file is still in a shared business-unit container,** as well as those with an open ledger step. A failed copy is never written to the ledger, and this is the only way it gets finished (round 46 item 2's intent).
4. **Known limit (class e):** a repeat call whose only work was an owed source delete answers 409 "already provisioned". The work is done; the message does not say so.

## Round 65 (2026-10-05). BINDING. Main-session decisions under owner rounds 56/59. Integration of 165 and 167.

1. **165's secret-name allow-list after master `bb8ba7251` (option A, carried to its conclusion).**
   - Master moved SPE Admin onto the BFF's own managed identity. It deleted `SpeAdminTokenProvider` and every Key Vault secret read, and made `keyVaultSecretName` optional. What round 35 item 3 and round 41 item 4 guarded no longer exists.
   - **The fix:**
     - REMOVE the read guard and the filter's 409. Kept, they would block the Model 1 config that `bb8ba7251` made work.
     - CHECK whether any code still READS `sprk_keyvaultsecretname`.
   - **If NOTHING reads it** (class b, a dead mechanism), also remove:
     - the 400 validation on config POST/PUT;
     - `Test-SpeConfigSecretName.ps1` / `Repair-SpeConfigSecretName.ps1` and their gate;
     - round 41 item 4's dev-config repair live step.

     Record that SPE Admin authenticates as the BFF identity, and that the column is retained only as data, in the topology doc and 165's note.
   - **If something still reads it:** keep ONLY the 400 validation of a supplied name.
   - **Resolving the merge:** take master's managed-identity design in `SpeAdminGraphService` / `SpeAdminTokenProvider` (modify/delete → delete). Re-apply 165's other work (container binding, the operator marker, type routes) on top. Then do round 62 items 1–3.
2. **The five sweep entries fixed in code but still Pending** (S-24, S-18, S-78, S-64, S-65): ONE generic ledger credit, not five new scanner vocabularies.
   - **The credit (`ProvenByTest`):** a present route's entry is resolved when `ResolvedBy` is set AND `ProofTest` names a test that drives the REAL app (WebApplicationFactory / the booted BFF) and asserts the route's authorization outcome. That means a refusal for an unauthorized caller and success for an authorized one.
   - **Enforcement:** the guard checks that the named test exists, runs (is not skipped) and references the route.
   - **The cap:** this is the only new vocabulary. It does not loosen any existing rule.
   - Use it for those five, and for any later fix shape the scanner cannot see.

## Round 66 (2026-10-05). BINDING. OWNER decision. Task 148's open escalation (r2 verifier item 3).

1. **The double-fault stranded mirror on unsecure is ACCEPTED as a known limit (option C).** The owner chose "Accept and document" over the write-ahead marker (A) and the schema column (B).
   - **The case:** on unsecure, a released child's mirror revoke fails, AND putting it back on the Secure team also fails. The call answers 500 `children_incomplete` and logs Critical once, naming the row and the manual revoke. A second `/unsecure-project` call then treats the child as never isolated (round 22) and completes. The record's former sharees keep their share on that one child.
   - **Why accepted (round 56):** it takes two consecutive write failures on the same row, it is bounded to the record's own former sharees, and it is logged with the row id. Option A would amend round 22, add a marker principal and a standing live invariant (a memberless team in every environment). That is a fix bigger than the problem.
   - **Recorded:** as a known limit in 148's note, the PR description, and the deployment guide's residual-risk section (the operator's manual revoke). No code change.

## Round 67 (2026-10-06). BINDING. OWNER decision. Task 114 (external licensed users) rescoped with Restricted.

Replaces the 2026-09-18 ruling's "the external flag is irrelevant to sharing" reading. The flag stays and is load-bearing for Restricted.

1. **What Restricted means.** A Restricted record (`sprk_accesspermission` = Restricted 100000002) is for internal use only. *Internal* = a licensed system user who is **not** flagged `sprk_isexternal = true`. Contact-based access never reaches a Restricted record, even an internal person's work contact. This is already the behaviour: `RootRecordFlags.RemovesContactSourcedAccess`.
2. **Sharing rule** ("+ User" `/share-user` and Assigned-To auto-grants, task 142's materializer; ONE rule in `ClassifyEligibility`):

   | Record | `sprk_isexternal` false or blank | `sprk_isexternal` true |
   |---|---|---|
   | Not Restricted | share | share (2026-09-18 ruling) |
   | Restricted | share | **refuse** |

   The standard Share dialog can only share with licensed users anyway, so the BFF adds no licence check: enabled + person + the table above.
3. **Blank = not external, everywhere** (owner: "sprk_isexternal means they're external"). `SystemUserIdentityResolver.IsExternalAsync` (internal-only messages) changes to match: blank is internal, not fail-closed external. One-off data step per environment: set B2B guests (`#EXT#` in the user name) to `sprk_isexternal = true`, with a dry-run listing first; nothing else is changed.
4. **Becoming Restricted removes existing shares held by users with `sprk_isexternal = true`.** No other shares are removed.
5. **The standard (OOB) Share dialog on Restricted records:**
   - **(a) Primary:** hide the OOB Share command on Restricted records with a ribbon rule, so sharing goes through Manage Access "+ User", which applies the rule.
   - **(b) and (c) Backups**, "if they don't create too much work; unlikely these scenarios will arise":
     - (b) the reconciliation job removes a share held by a user flagged external on a Restricted record;
     - (c) Manage Access shows such a share as "External user — no access" until it is removed.

## Round 68 (2026-10-06). BINDING. OWNER decisions on the batch-4 live-gate findings.

1. **G149-2:** a Web API `GrantAccess`/`RevokeAccess` is an acceptable stand-in for the model-driven Share dialog. Task 149 completes on the G149-1/G149-2 evidence.
2. **Create-then-open on secure records:** the Events API and Office to-do routes create rows with no inline share, so the creator can't open a new row for up to 2 minutes, until the share-mirror job runs. ACCEPTED (inside the existing window).
3. **Empty SPE containers from the gate runs:** delete them. None is referenced by any Dataverse project, matter, work assignment, business unit or document (checked 2026-10-06). The BFF has no container-delete route and the admin's Graph token is refused, so deletion is an operator step (SharePoint admin). The 15 ids:
   - `b!1jQ90fRNREqIkel3wEl2txG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN`
   - `b!4ltjdXaQMEGKlLr47q0m6RG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN`
   - `b!8qdU0gEubUSu29rQVtpvehG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN`
   - `b!DuRySBK9FkKk2wSDxRigwhG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN`
   - `b!ECpwsE3P2Em14S3B84uYOxG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN`
   - `b!HBRbokLXnUGzaDLSTdNFvM5RFHtaaUZCi0Jm-xs-hDQV_6QuLuKmR4jrMdC6UgMm`
   - `b!HkqHE0XETUCPMjmQJXU3YBG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN`
   - `b!IRjcuov6dU-QV2n8wNvGgxG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN`
   - `b!Q93-JrZ7fUmviHObJDHmyhG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN`
   - `b!UhVkyYyZrEKopsCjLi3dfxG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN`
   - `b!UhxDf-Qp9UKCLyeglKKvdhG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN`
   - `b!ZxwACqn6EEKP99-lPgOiBhG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN`
   - `b!_0H4eQgAXkm7AAo66_apUBG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN`
   - `b!_yZXxbMiOESIRAX0EKNSlhG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN`
   - `b!qZ9Qj5EIfkKIBIESyeRL1xG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN`
4. **Event `b52562e5`** has a record type and no regarding link, so its two to-dos stay unstampable. That is fine.
5. **Make Secure on a record that already has documents** strands those documents for every OBO read path, until the broker-only document task lands. The owner chose NOT to add a temporary guard ("it just risks a regression and somehow missing the reversion later"). Recorded as a known risk.
6. **Broker-only document gap (upload403 investigation):** a new task is ADDED to the front of batch 5. Its scope depends on the Office-edit option, which is pending.

## Round 69 (2026-10-06). BINDING. OWNER decision. SPE document access: broker-only everywhere, with JIT Office edit. New task 171.

1. **Broker-only for ALL containers** (environment/root, business-unit, per-record secure), for system users and contacts alike: the BFF checks Dataverse, then reads and writes bytes app-only. Access follows Dataverse privileges with no membership to manage. Owner: "this is critical otherwise SPE wouldn't work for any users!"
2. **Office edit:** just-in-time permission for a system user with Write on the record (Dataverse decides, as the caller), the narrowest kind SPE supports. A reconciliation pass removes it when Write goes away. This is the ONE exception to the 2026-08-25 "no user is ever granted a container permission" rule.
3. **Contacts** (external and workforce-contact) are included in broker-only and verified on secure containers. They never receive an SPE permission (SPE accepts only Entra members or guests), so a contact edits by downloading and uploading a new version.
4. **Why not membership as the model:** a container member can open every file in that container directly through Office or SharePoint, whatever Dataverse says about the record the file belongs to. That cannot honour per-record privileges, even with one container per customer environment, and per-record secure containers would multiply the lists to keep in sync.
5. **Task 171** owns it, first in batch 5. It escalates if Office edit needs a container-level role, because of the exposure on business-unit containers.

## Round 70 (2026-10-06). BINDING. OWNER decision. Office edit and container membership (task 171).

- **Premise (owner):** SPE access is container-level, not per file.
- **Option (c) chosen:** internal system users (`sprk_isexternal` not true) are standing WRITERS on the environment/BU container, kept in sync automatically (created, enabled, moved, disabled, flagged external). Content narrower than all internal users is made Secure, which gives it its own container. Owner: "this is e.g., why we have secure containers".
- **Secure containers:** no standing members. JIT membership for Office edit is granted to users with Write on the record and removed when Write goes away.
- **Users flagged external** are never members of an environment/BU container. Every non-edit byte path stays broker-only (app-only after the Dataverse check) on all containers.
- **Accepted consequence:** for non-secure documents, Dataverse privileges finer than "internal user" are not enforced on a direct SharePoint/Office path.

## Round 71 (2026-10-06). BINDING. OWNER decision. Grants whose record is gone.

- **Problem:** deleting a root record leaves its contact grants (`sprk_externalrecordaccess`, including Assigned-To auto-grants) ACTIVE with an empty record lookup. The lookup can only clear its link on delete, because of the Dataverse one-cascade-parent limit.
- **Owner: "yes add this check/deactivation".** The existing `ExternalAccessReconciliationJob` gains rule R4: it deactivates an active grant whose record is gone, through the same lifecycle path as R1-R3. Ledger rows get the equivalent cleanup in `AssignedAccessReconciliationJob` if that is a small extension.
- **The rule honours the job's mode.** On dev it only reports until the owner decides `ExternalAccess:Reconciliation:WritesEnabled` (task 137).

## Round 72 (2026-10-06). BINDING. OWNER decisions on task 171's open items.

1. **F4: ADD the locked item-id copy.** A field-security-locked `sprk_graphitemidbound` ("Spaarke BFF-Managed Field Writers" profile, so only the BFF writes it), written wherever the BFF sets a pointer. The pointer check requires `sprk_graphitemid` to equal it. A one-off backfill copies the current value (the pre-lock residual is accepted, as it was for the drive id).
2. **F10: REFUSE share-link** for documents on secure or Restricted records (clear message). Standard documents keep it.
3. **"Modified by" = the BFF app for app-only writes: ACCEPTED.** Owner: "if it is technically convoluted then ok". Graph's driveItem `createdBy`/`lastModifiedBy` are system-set and read-only; the SharePoint `ValidateUpdateListItem` author/editor rewrite is not supported on SPE containers. Spaarke records the person, and Office/Word desktop edits show the real user.
4. **Delete the 20 empty test SPE containers** (ids in `notes/batch4-live-gates-2026-10-06.md` and round 68). The BFF has no delete route and the admin Graph token is refused, so the owner runs it as a SharePoint admin step.
5. **The owner is available for the screen session now** (2026-10-06).

## Round 73 (2026-10-06): #1011 reassigned to this project

- **From** spaarkeai-word-add-in-r1 (pasted by the owner): GitHub #1011 reopened and assigned to unified-access-control-r2 (owner decision 2026-10-06). GitHub had auto-closed it on 2026-09-30 when #960 merged, reading commit `34beafe78`'s "do not close #1011" as a closing keyword.
- **Defect:** `MembershipFieldDiscoveryService` synthesizes Owner targets `{systemuser, team}` and keeps only the first match, so `ownerid` is always one SystemUser descriptor. A team-owned record (the D-11 convention) resolves to nobody through its Owner column. Task 043 routed this project's access checks around it via `owningteam`; the resolver itself was never repaired.
- **Action:** task 172 created (`tasks/172-membership-resolver-team-owned-owner-column.poml`). Bind every matched identity type of a polymorphic lookup; the people-targeting surface (A3, task 152) stays byte-identical; the C-1 name-keyed allowance stays. Regression test: a team-owned record resolves to that team's members. PR text uses "refs #1011" only.
- **Owed to spaarkeai-word-add-in-r1:** whether any Office-route access result changes (their saves create team-owned `sprk_document` / `sprk_todo`). Answered from task 172's step-0 trace.

## Round 74 (2026-10-06): BINDING. OWNER correction of the review round limits

- **Withdrawn:** "at most 2 fix rounds; escalate any F1 still open instead of starting round 3" (the 2026-10-06 hygiene rule, CLAUDE.md standing directives, repo task-execute Step 9.5). The owner: "we should never allow known broken code to not be fixed just because it takes more than 2 rounds ... the policy change was to avoid unnecessary testing checks and pseudo fixes."
- **In force:** limits cut review ceremony, never fixing. That means fix-diff-scoped re-checks, one full verifier pass (two for auth/security), no speculative findings and no pseudo-fixes. Fixing continues until no F-class finding remains. Escalate on non-convergence or an owner-only decision.
- **Also binding:** every defect found is fixed directly or surfaced to the owner, "whether or not it was directly or indirectly caused by the current work or only uncovered or found in the course of the work ... this is critical."
- **Applied:** project CLAUDE.md; project memory; repo-wide PR #1336.
- **Consequence for task 171:** the round-2 re-verify findings V1–V7 are all fixed in a further round, then a fix-diff-scoped check.

## Round 75 (2026-10-06): BINDING. OWNER delegation; 036's ADR-034 amendment APPROVED

- **The owner:** "for 036 if an ADR decision is needed and it is consistent with objectives of this project then approve it."
- **What is approved:** the ADR-034 section 6.5 path B amendment in task 036's tension block, as reworded per owner D1 (2026-09-30).
  - For systemuser ACCESS on the SPA/Teams plane, Dataverse's own answer (the FR-20 impersonated read) replaces the membership rules.
  - So BU and role-depth access is honoured exactly as in the MDA, and BU-ownership matching no longer over-grants.
  - The membership walk stays for non-access scoping and for the contact plane.
- **Why it is consistent:**
  - It is objective D1 itself: a user with access in the MDA has access in Teams/SPA.
  - Path A was rejected because the BU guess is wrong in general.
  - Path C was rejected because no compliant shape makes a pattern match equal Dataverse's answer.
- **Unchanged:**
  - The concise `.claude/adr/ADR-034` text and the full `docs/adr` text merge with or before 036's PR, and the PR cites this round.
  - The flag stays off until 142 and 143 are complete.
- **Standing:** future ADR decisions inside 036's scope are delegated on the same test (consistent with the project's objectives). Each one is still recorded as a round and cited in the PR. A decision that is NOT clearly consistent comes back to the owner.

## Round 76 (2026-10-06): BINDING. OWNER decisions on task 114's findings

1. **Restricted wins over the last-reader rule.**
   - On a Restricted record, a user flagged `sprk_isexternal = true` keeps no share, even as the record's last reader.
   - The removal reports `no-internal-reader` so an administrator can share the record with an internal user. Admins still see it.
   - The same rule applies in provisioning: an external-flagged creator or Make Secure caller gets no share on a Restricted record.
   - The skip text is "{name} is flagged as an external user and can't be given access to a Restricted record."
2. **The 'Spaarke Demo' team holds System Administrator: not deliberate.** Filed as #1343 (least-privilege role for demo users).
- **Also filed under the round-74 rule** (found in passing during 114):
  - #1344: CA2024 breaks `Spaarke.sln -warnaserror`.
  - #1345: three ui-components jest suites fail on master.
- **Earlier, from task 172:** #1339 (byRole attribution) and #1340 (membership role/identity vocabulary).

## Round 77 (2026-10-07): BINDING. OWNER decision: accept the business-unit read gap on non-secure Restricted records

- **The gap:** a licensed user flagged `sprk_isexternal = true` can still read a NON-secure Restricted record in their own business unit through the model-driven app. Dataverse's business-unit read privilege applies whatever the shares are, so removing shares can't stop it.
- **Decision:** accept it as a known limit. The rejected alternatives were a separate business unit for external-flagged users, and making Restricted imply secure.
- **Still enforced (task 114):**
  - no share is given or kept;
  - the OOB Share is hidden;
  - the external SPA/Teams plane vetoes Restricted records for external-flagged system users (added after the 114 verifier's K1);
  - secure Restricted records are fully closed.
- **Recorded in:** task 114's note and PR as a known limit.

## Round 78 (2026-10-07): BINDING. OWNER decision: secure-only records may be shared with external-flagged system users

- **The question:** the owner's suggested refusal text (test feedback on TrackingFieldTrio 1.0.36) said "Restricted and secure records cannot be shared with external users", but the rule (round 67) bars external-flagged users only on Restricted records.
- **Decision (owner, verbatim):** "we should allow external systemusers (e.g., outside counsel who have Spaarke licenses should have access)".
- **So:** `sprk_isexternal` is consulted only on Restricted records, Secure – Restricted included. A secure record that is not Restricted can be shared with an external-flagged, licensed system user. This is the rule as built by task 114, so nothing changes.
- **Copy:** the refusal reads "... Restricted records cannot be shared with external users." (PR #1369); it does not mention secure.

## Round 79 (2026-10-07/08): BINDING. OWNER decisions from the Manage Access test rounds

- **Modal standard:** `SprkModal` is the canonical modal. Manage Access stays on it; WizardShell is for step flows only, and Manage Access has no steps. Not moved to a code-page dialog.
- **Lookup over a modal:** "the lookup should always be on top of the parent modal." Applied to Manage Access and the email composer (#1371) through `SprkModal` `sidePaneLayering` (PR #1389). Docking left is only the fallback when the pane cannot be layered above the modal.
- **Reconciliation settings on dev:** approved by the owner ("if it is just a bff ... that's fine").
  - `ExternalAccess__Reconciliation__WritesEnabled=true`: the report-only run first showed 0 planned changes across R1–R4 (9 grants scanned), and the first write-mode run changed 0.
  - `Communication__OwnershipHoldAlertUserIds__0` set to ralph.schroeder@spaarke.com (`1d02f31c…`).
  - Set 2026-10-08 ~00:51Z; the BFF restarted and is healthy.
- **Test containers:** the owner approved the cleanup of the 21 empty test containers; the owner runs `Remove-TestContainers.ps1` as SharePoint admin.
- **Restricted share:** the owner confirmed it works (the "+ User" filter and the named refusal).

## Round 80 (2026-10-08): BINDING. OWNER decision: re-adding a contact whose grant has lapsed restores it at the picked level

- **The case:** Manage Access "+ Contact" re-adds a contact whose grant has lapsed. `/invite-and-grant` sends no expiry.
  - After task 113's pre-write check, the server wrote nothing yet answered 200 "granted <level>".
  - A later expiry renewal would revive the OLD, possibly higher, level.
- **Decision ("Restore at picked level"):** a re-add is an explicit SET. It restores the grant at the PICKED level with the default 90-day expiry; the server supplies that expiry when the existing row has lapsed.
  - The grantor ceiling and the contact-issuer cap still bound the result.
  - A ledger entry is adopted only when the grant is written.
- **Rejected:** refusing with a 409, because the Expiration toolbar would then restore the old level; and shipping 113 as it was.
- **Applied in:** task 113, PR #1406.

## Round 81 (2026-10-08): BINDING. OWNER decision: child records show their parent's access permission; a parentless child keeps its own

- **Trigger:** UAT. A To Do created through the Word web add-in on a document under Restricted matter PAT-176903 showed `sprk_accesspermission` = Standard. The peer word-add-in-r1 traced it: ownership was correct (`CoreAncestorResolver.StampAsync` fails closed; `RecordOwnershipResolver` applies secure-if-any), but no BFF path writes the child's `sprk_accesspermission` (Office, the wizards, `ChildRecordEndpoints.cs`), so it kept the default. Effective access was already right: Restricted bars external users through the root.
- **Decision:**
  - **A child WITH a parent inherits** the parent's `sprk_accesspermission`: To Do, Event, Communication and Document. The owner added `sprk_accesspermission` (the global choice) to `sprk_document` live on 2026-10-08; it must be brought into source.
  - **A child WITHOUT a parent keeps its own value**, which the user sets.
  - **Parentless value is recorded only; no new enforcement** (owner, 2026-10-08: "This is fine (BUT in future we might need to revisit)"). A parented child's protection keeps coming from the parent (every `sprk_regarding*` lookup, ownership to the Secure team, mirrored shares, Restricted bars external users).
  - **Control:** a permission control on these child records, but NOT TrackingFieldTrio on Communication (email).
- **Implementation shape (adopted from the peer's advice):** write the value ONCE in the shared stamp/ownership path (`CoreAncestorResolver` / `RecordOwnershipResolver`), never an Office-only write; the ≤2-minute reconcile keeps it in step on re-file or a parent change; the field is locked with "inherited from X" while the record has a parent.
- **Rejected:** the peer's "retire or lock the column" (communication model): the owner wants the value visible.
- **Doc drift to fix in the task:** `docs/data-model/sprk_communication.md:12` (child carries no permission of its own) and `unified-access-control-cascade.md:37` (column not on `sprk_todo`).
- **Revisit later (owner):** whether a parentless child's own value should be enforced.

## Round 82 (2026-10-08): BINDING. OWNER decision: the parent's permissions control a filed child, whatever the child's own secure flag says

- **The case:** a work assignment or project filed under a secure matter or project, not yet flagged `sprk_issecure` (inheritance pending, Refused or Failed). The #1410 fix (PR #1419) walked secure ancestors only for children flagged secure, so a person walled off the parent could still see such a child in Teams/SPA. Inheritance can end Refused or Failed, so the gap could be permanent.
- **Decision (owner):** "It should be that the parent permissions control." A secure parent's No Access entries (and its access) govern a filed child regardless of the child's own flag.
- **Applied in:**
  - PR #1419 (#1410): the read-time veto walks every work assignment or project that has a secure ancestor, not only those flagged secure. The verifier gives the fix shape and cost; `fix1410` implements it.
  - PR #1411 (task 064): the per-record report counts an entry reached through a secure parent as in force even when the filed record's flag is false, and even when the same entry is also on the direct path (pass-2 F3).
- **Already consistent:** the share-time guard (`SecureShareNoAccessGuard.CheckRecordAndSecureParentsAsync`) checks parents for such a record.
- **Relationship to Q4:** Q4 ("No Access applies to internal users on SECURE records") is read with "secure" meaning the record or any filing ancestor.

## Round 83 (2026-10-08): BINDING. OWNER decisions on the open list ("follow recommendation" unless noted)

1. **No Access record picker:** follow-up task: RegardingResolver in a "link only" mode (no core-ancestor stamp) plus `sprk_objectrecordname`.
2. **Task 101 views:** keep overdue shares in "External Shares Expiring in 30 Days"; add Granted By beside Created By.
3. **Task 105:** seed 250 test documents on dev for the paging live gate; delete them afterwards.
4. **#1396:** add the secure-ownership layer for grant rows (`sprk_externalrecordaccess` on secure records owned like the record), as a small task.
5. **Task 064:** the No Access Reason stays hidden from Write holders (as built).
6. **#1350:** replace the share-link with an open-record link (SharePoint refuses sharing links on CSP container sites).
7. **Hook cap:** raise `.claude/hooks/reinject-project-state.ps1`'s 15,000-character cap to about 20,000 (repo-wide).
8. **"My events":** moot. Master already shows events the caller created or is assigned to (task 097, owner decision B; `EventEndpoints.cs:518-520`). Batch-4 O-1 is closed.
9. **Copilot agent in dev:** "yes I think so". Task 164 (j) checks it live and records "not available" if it is not.
10. **#1425:** round 82 applies to external contacts too: a contact walled on a secure parent is denied the records filed under it, at read time and at grant time.
11. **Task 153 O1:** BOTH: a non-clickable red form banner plus a clickable TrackingFieldTrio indicator that opens Manage Access.
- **TrackingFieldTrio 1.0.39:** dark mode PASS (owner). Lookup-pane placement accepted as the SprkModal standard; no further check.
- **Hands-on checks** for 171/114/batch 4 will be done by the owner in the SPA and the apps (owner, 2026-10-08).

## Round 84 (2026-10-08): BINDING. OWNER decision: a child's access always follows its parent, both ways, and is locked while it has a parent

- **The question:** found by the task 067 verifier. The server applied Secure/Limited/Restricted from a record's OWN flags only, so a work assignment filed under a secure matter but not yet flagged secure (inheritance pending, Refused or Failed) kept its org-wide and standing contact grants (#1442).
- **Owner (verbatim):** "Child access should always follow parent; if parent changes, then child changes. A protection is that if a child has a parent then the access cannot be changed manually (e.g. its locked)."
- **Follow-ups answered the same day:**
  - **Un-securing cascades ("Child follows"):** when a parent goes from Secure back to not secure, its filed children become not secure too. This REPLACES round 6 item 4 ("parent unsecured → children stay secure; no unsecure cascade").
  - **Scope ("all children"):** work assignments and projects filed under a parent take the parent's `sprk_issecure` AND `sprk_accesspermission`. To Do, Event, Communication and Document take `sprk_accesspermission` (round 81 / task 173).
- **Rule:**
  - A record WITH a parent: effective Secure and Access Permission are the parent's (the most restrictive across several parents). They change when the parent changes. Users cannot set them on the child: the fields and the Make Secure / Remove Secure / Update Access commands are locked or hidden while a parent exists.
  - A record WITHOUT a parent keeps and edits its own values (round 81 for the four child types: recorded only).
  - Enforcement uses the effective (parent-derived) values on every path:
    - the read-time veto and cancellation;
    - grant time;
    - the No Access enforcer;
    - share mirroring;
    - the Manage Access display.
  - Until the stored flag catches up, the server computes the value from the filing chain and fails closed.
- **Applied in:** PR #1419 (No Access via the parent, round 82); #1442 (cancellation and flags follow the parent); #1425 (contact plane); task 173 (the four child types' Access Permission); a new task for work assignments and projects (flag cascade both ways, lock, effective flags in enforcement); task 067's modal reads the effective value once the server exposes it.
- **Superseded:** round 6 item 4 (no unsecure cascade) and its task 158 constraint. Also any rule that a filed child's Secure can be removed by an explicit act while it still has a parent (F3 now applies only to parentless records).

## Peer report: #1081 decided (word-add-in-r2, relayed by the owner 2026-10-02)

- **Owner decision on #1081:** the dev root team "Spaarke" now holds Spaarke Basic User, verified live; Office creates owned by it work again. Root-BU users are a dev-only artifact. In production, users sit in the customer's child BU and the BFF app user sits in the customer BU, so **nothing is codified for the root team**. This matches round 5.
- **For the census:** at the root, Basic User's read reaches the whole org, Secure Record included, for every root-team member (171 members per the peer's note). Under round 5 this is an accepted dev finding, not a production exposure. The census keeps reporting it; no exception is coded.
- **Also observed by the peer:** the "Spaarke Demo" BU's team holds **System Administrator**, so every member of that team bypasses all record security, secure isolation included. This is another dev artifact under round 5. No action is taken in dev; the census must flag any team holding System Administrator in production.
- **Dev BFF state, checked 2026-10-02 (Kudu deployment list):** the peer deployed master `5e39f2bea` at 03:35Z. Our `bca0941f6` (batches 1+2) followed at 03:41Z and is the active deployment. `bca0941f6` contains `5e39f2bea`, so neither deploy undid the other. The peer's 12:23Z work was restarts, not a deploy.

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
