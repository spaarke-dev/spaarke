# Assign-privilege audit of the Spaarke security roles (#3245), 2026-11-26

> Owner request in the uac-r2 session: "Start #3245 audit". The audit only reads data and changes no roles. Each change below is a **proposal** for the owner to review; no role is widened.
> Data source: dev (`spaarkedev1`), read 2026-11-26 through the Web API (`roleprivilegescollection` per role, 144 root-business-unit roles). The table shows how the roles are designed. It does not say which users or teams hold them.

## 1. What was measured

- **97** user/team-owned `sprk_*` tables, each with a `prvAssignsprk_*` privilege.
- **252** Assign grants across all roles. The Spaarke roles hold 69 of them; the rest are out-of-the-box roles (§4).
- **Repo vs dev: no drift.** The role files on `origin/master` match dev on Assign count (Core User 26, Office Add In User 9, AI Analysis Admin 18, AI Analysis User 4, Ontology Service 3). An earlier count of 27 for Core User came from a stale copy on this project's work branch. On master, Assign on `sprk_invoicecorrection` was already removed by ebilling (EB-D-249) and is pinned as a removal in `config/feature-role-privileges.json`. The Workforce role has no role file in the repo.

## 2. How a record's owner is set in Spaarke today

- **The BFF writes owners as the app (owner D-11, "team ownership, never user ownership").** Every `ownerid` write in the BFF runs under the app identity, through `RecordOwnershipResolver` (`RecordCreationService`, `DocumentCheckoutService`, `SignalEvaluationService`, `CommunicationEnrichmentService`, `ContactIdentityBinder`, the grant/provision/unsecure endpoints, `AssignCascadeChildOwners`, `SecureChildReconciler`). The caller's own Assign privilege is never used for these writes.
- **Impersonation (`MSCRMCallerID`) is used only for reads,** and for the owner's own share revoke. Never for an owner change.
- **No client code writes `ownerid`** (ts/tsx/js). The client tests assert that it doesn't.
- **Shares never carry Assign.** `RecordShareLevels.cs`: "Assign is at no level".
- **The native model-driven-app Assign command** is hidden on the 23 mapping-target tables (field-mapping task 051, FR-24). It is still visible on the other user-owned tables. Hiding the command is not a guarantee: a user who holds Assign can still change the owner through the owner field, a bulk edit or the Web API.
- **The reconciler does not repair a user-owned row.** `SecureChildReconciler` *reports* a user-owned row (or one owned by a non-default team) and leaves it as it is (`SecureChildReconciler.cs:601`, `:2801`). So an Assign a user makes outside the BFF stays in place until someone acts on it.

## 3. The Spaarke roles: each grant and its legitimate use

| Role | Assign on | Depth | Legitimate use found | Proposal |
|---|---|---|---|---|
| Spaarke Core User | `sprk_Event` | Deep | **Yes.** `POST /api/communications/{id}/create-task` and `.../proposals/{id}/create-task/apply` ask for the caller's `prvAssignsprk_Event` when the task names someone else as its owner (`CommunicationRecordAuthorizationFilter.cs:161,721-733`). | **Keep**, with this reason. |
| Spaarke Core User | the other 25: Agreement, Analysis, BillingExport, Budget, Communication, Document, ExternalIdentifier, Invoice, InvoiceApproval, InvoiceSubmission, KPIAssessment, Matter, Memo, NavItem, NotificationOutbox, Project, Report, ServiceRequest, Todo, WorkAssignment (all Deep); CommunicationAccount, CommunicationAttachment, CommunicationChannelRef, CommunicationParticipant, CommunicationThread (Basic) | Deep ×20, Basic ×5 | **None.** No BFF route asks for these privileges, and no client writes the owner. Their only effect is that a user can move a record off its team owner by hand, which D-11 forbids and the reconciler does not repair. | **Remove** (one reviewed role change). Keep `sprk_NavItem` only if the side-pane project wants users to hand personal nav items to someone else (§5 Q2). |
| Spaarke Office Add In User | AttachmentArtifact, EmailArtifact, ProcessingJob (Basic); Event, Invoice, Matter, Memo, Project, WorkAssignment (Deep) | Basic ×3, Deep ×6 | **None.** The `/api/office/*` routes ask for no Assign privilege, and their owner writes run as the app. The To Do's "Assigned To" is a contact lookup, not `ownerid`. | **Remove all 9.** word-add-in-r1 confirmed on 2026-11-26 that nothing in Office breaks. Assign on `sprk_Event` for the communication create-task route comes from Core User (Deep, kept). The removal assumes every user of that route holds Core User; the live check after import covers this (§6). |
| Spaarke Internal Contact User (Workforce) | Agreement, Analysis, Communication, CommunicationAccount, CommunicationAttachment, CommunicationChannelRef, CommunicationParticipant, CommunicationThread | Basic ×6, Deep ×2 | **None, and inert.** A contact is not a Dataverse principal. The BFF reads this role only as a ceiling map, which drops Assign because Assign has no `AccessRights` bit (`UserPrivilegeChecker.BuildWorkforceCeilingMap`; `WorkforceRoleCeilingTests.cs:64`). | **Remove all 8** (tidy-up; no behaviour change). |
| Spaarke AI Analysis User | `sprk_analysis`, `sprk_analysischatmessage`, `sprk_analysisemailmetadata`, `sprk_analysisworkingversion` | Basic | **None.** The analysis routes create and save as the app and ask for no Assign. | **Remove all 4.** Confirm with the AI owner. |
| Spaarke AI Analysis Admin | 18 AI tables (playbook, action, skill, tool, knowledge, output and the AI type/catalog tables, plus analysis, chat message, email metadata, working version) | Global | **Admin reassignment.** Round 124 made the role admin-trusted (Global Write and Assign on `sprk_analysisplaybook`). Its Global reach, including Read on analyses from Secure matters, is a pinned accepted exception (round 127, #2322). | **Keep**, citing rounds 124/127. No change. |
| Spaarke Ontology Service (the ontology writer's app identity) | `sprk_DecisionRecord` | Global | **Yes.** `OntologyWriterDataverseClient.AssignAsync` (SDK `AssignRequest`; D-33c, the writer holds no Write on the table) and `GateDecisionRecordSharing`. | **Keep.** |
| Spaarke Ontology Service | `sprk_BudgetRevision` | Global | **Yes.** `ReviseBudgetExecutor` sets the owner that `RecordOwnershipResolver` decides, so the revision does not land in the root business unit outside a Secure matter's wall (D-33/D-38, `DecisionActionExecutors.cs:383-385`). | **Keep.** |
| Spaarke Ontology Service | `sprk_Signal` | Global | **Yes** (ontology-r1 confirmed 2026-11-26). Two writer paths use it. (1) `SignalWriter` writes `ownerid` in the create, set to the team `IRecordOwnershipResolver` answers (D-134). (2) The no-core re-own path moves an existing Signal to its item's current owner team through `OntologyWriterDataverseClient`'s `AssignRequest` (D-130/D-169). Recorded as required in ontology's `notes/security-roles.md`. | **Keep.** |
| Spaarke Ontology Administrator | `sprk_Signal` | Deep | **None.** ontology-r1 confirmed: no shipped flow assigns a Signal as the caller, and a manual reassignment would disagree with the resolver's owner. It is held only by owner decision D-160 (2026-10-17: "not a risk; we can remove later"). | **Remove**, but only on the owner's go, because D-160 was the owner's call (§5 Q4). |

## 4. Out-of-the-box roles (no change proposed)

System Administrator (63 tables), Service Writer (60) and System Customizer (60) hold Assign at Global, as the platform ships them. Admin data scripts that set owners (`Backfill-RecordOwnership.ps1`, `Migrate-SecureRecordsToNamedOwnerTeam.ps1`, `Apply-DoLanePolicies.ps1`, `ebilling/Seed-BillingSystemPolicy.ps1`, `seed-uat-communication-corpus.ps1`) run as an administrator and rely on these roles, not on a Spaarke role.

## 5. Questions for the owners

- **Q1 (word-add-in-r1, answered 2026-11-26):** yes. No `/api/office/*` route asks for Assign; Office saves, finalization, To Do and quick-create write as the app.
- **Q2 (side-pane-navigation-history):** `sprk_NavItem` is a personal per-user row. Should a user be able to hand one to someone else? Default: no, so remove it.
- **Q3 (ebilling-r1, answered 2026-11-26):** nothing to do. Master already omits Assign on `sprk_invoicecorrection`; it was a stale-branch reading.
- **Q4 (ontology-r1, answered 2026-11-26):** the Ontology Service needs `sprk_Signal` Assign (keep). The Ontology Administrator's grant has no use. Removing it reverses owner decision D-160, so it goes to the owner. No human role holds Write, Delete or Assign on `sprk_decisionrecord`.

## 6. How the change ships (when the owner agrees)

- One PR edits the role XML files in `src/dataverse/solutions/SpaarkeMaster/Roles/`, removing grants only. It's imported with `scripts/Import-SolutionScoped.ps1` after the owner's go.
- **Enforcement (ladder):** list each removed grant under "removals" in `config/feature-role-privileges.json`, which `FeatureRolePrivilegeGuardTests` already enforces (ebilling uses it for `sprk_invoicecorrection`). Also consider a pinned allow-list of the Assign grants kept above. Any new `prvAssignsprk_*` in a Spaarke role file then fails the build unless it is added to the list with a reason. Must-fire control: add a grant and the test fails. Must-not-fire control: the current kept set passes.
- **Live check after import:** re-run this audit's query. Spaarke roles should hold only the kept grants, the create-task route with another owner should still succeed for a Core User, and an Office save should be unchanged.
