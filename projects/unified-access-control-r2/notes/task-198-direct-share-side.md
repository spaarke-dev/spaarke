# Task 198 — share side of Direct 1:1 messages (notes)

> Task: `tasks/198-direct-message-share-side.poml` (FULL). Branch `task/uac-198-direct-share-side`.
> Done: Steps 0, 1, 2 (Part A, Q4, role guard), 3 (Part B, Q5), 5 (Part D). Part C and Part E are pending upstream.
> Wording rule (public repo): rules only.

## §0 Conflict check (Step 0, 2026-11-09)

| Branch | Tip | Overlap with `DirectThreadAccessService.cs` / `GateDecisionRecordSharing.cs` / `Set-CustomerOwnerTeams.ps1` |
|---|---|---|
| `task/fm-054a` (local only, not on origin; worktree `C:/code_files/spaarke/.claude/worktrees/agent-ab621d3020489c22b`) | `b2aa45985c` | None. It touches `Services/Communication/Membership/*` (MembershipOptions, MembershipResolverService) and their tests only. No 054a lookup code yet. |
| `origin/task/ebilling-130-0` | — | `scripts/Set-CustomerOwnerTeams.ps1`, `tests/scripts/Set-CustomerOwnerTeams.Tests.ps1`, `tests/Spaarke.ArchTests/RestrictedContentOwnerRoleGuardTests.cs` (ebilling 089d/089e rewrite: adds `billing-rule-readers-role.json` to `$DeclaredRoleFiles`, `-AssignBillingRuleReader`, an `assignment` field, an org setting). |
| `origin/task/ebilling-130-0b` | — | Same three files (same rewrite). |

No open PR touches `DirectThreadAccessService.cs` or `GateDecisionRecordSharing.cs`.

**Merge risk:** HIGH on the setup script and its two test files (whichever merges second resolves). Mitigation in Part A: the Direct mode lives in its own parameter set, its own config reader (`Read-DirectConfig`), its own declared set (`$DirectMessageRoleFiles`) and its own refusal block; `Read-Config`, `$DeclaredRoleFiles` and the customer-unit checks are not edited, so the conflict is additive hunks, not rewrites of the same lines.

## §1 Census (Step 1, read-only)

**STOP verdict: neither stop condition fires.** (a) finds no ordinary-filing revoke of participant shares on `sprk_communication`; (b) finds no content row that reaches participants only through the message owner. Findings that later parts must cover are listed under each item.

### (a) Paths that revoke or reduce POA shares on `sprk_communication` on filing / unfiling / re-file

Every `RevokeAccessAsync` / `ModifyAccess` caller in `src/server/api/Sprk.Bff.Api`:

| Site | Scope | Reaches `sprk_communication`? |
|---|---|---|
| `Services/Access/SecureChildShareSynchronizer.cs:728` (`RemoveMirrorAsync`, owner check 660-735) | A child row that LEFT isolation (secure → not secure): every direct share is revoked. | Yes, only on a secure exit. `AfterRefileAsync` (417-513) calls it only when the row `wasIsolated`. |
| `SecureChildShareSynchronizer.cs:1099`, `:2328`, `:2332` | Secure mirror maintenance (Secure-team-owned rows). | Only rows owned by the Secure team. |
| `Services/Signals/GateDecisionRecordSharing.cs:350`, `:439`, `:481`; `ReownAfterFilingAsync` (`:172`) | Gate Decision Records (not the message). Re-own after filing revokes every share on the record (D-169). | No (gate records only). Part E item. |
| `Services/ExternalAccess/RestrictedExternalShareRemover.cs:388` (`RemoveForRecordAsync` 185; entity set from `ExternalGrantRoot.BindFor(rootType)`) | Root types only (project / matter / work assignment). | No. |
| `Services/ExternalAccess/NoAccessShareEnforcer.cs:1172`, `:1175` (filed roots 840-895; `SyncRootAsync` children 915-937, secure roots only) | Roots; children follow only for SECURE roots. | Only through the secure child mirror. |
| `SecureRootInheritance.cs:2220-2224`; `InternalShareEndpoints.cs:783`; `UnsecureProjectEndpoint.cs:1181`; `ProvisionProjectEndpoint.cs:3628`; `AssignedAccessMaterializer.cs:1878` | Roots only. | No. |

- `Services/Communication/**` contains no revoke call.
- All relationship cascades are NoCascade (live, 2026-10-02; `Services/Access/SecureChildLineage.cs:16`), so an owner change on the message does not re-share or un-share anything.
- Order after every re-file: `SecureChildReconciler.AfterRefileCoreAsync` (1297-1339, `RefileStepsAsync` 1436+): thread reconcile, then the secure mirror (`_shares.AfterRefileAsync`), then `GateDecisionRecordSharing.ReownAfterFilingAsync` for communications (1325-1336).

**Outcomes per filing direction:**

- Direct team → ordinary record: no message share is revoked; the synchronizer does nothing because the row was not isolated. Gate Decision Records are re-owned and lose their shares (D-169; Part E).
- Direct team → secure record: the synchronizer owns the outcome (unchanged).
- Secure → ordinary record or unfiled: `RemoveMirrorAsync` revokes every direct share on the message. On an unfile back to the Direct team the participants then hold no share. **Part C must re-grant after unfile and after a secure exit.**
- Ordinary record → unfiled (back to the Direct team): the anchor-record grantees' shares stay on the message, but the audience of a Direct-team-owned message is exactly the participants. **Open question Q1.**

### (b) How a participant reads a Direct message's content rows

Content tables under `sprk_communication` (`SecureChildLineage.cs:206-264`): `sprk_communicationattachment.sprk_communication`, `sprk_communicationparticipant.sprk_communication`, `sprk_document.sprk_relatedcommunication`, `sprk_emailreviewlog`, `sprk_decisionrecord.sprk_regardingcommunication`.

| Row | Owner while unfiled | Read path | Needs a share after 054a? |
|---|---|---|---|
| Message | Creating identity: `CommunicationService.ResolveOutboundOwnerAsync` (2646-2689) keeps the creator for unfiled rows (`UnfiledOwnership.KeepCreator`). | Participant POA share (`DirectThreadAccessService.GrantMessageAccessAsync` 195-230). | Yes. Part B confirms both participants are granted. |
| Attachment (`sprk_communicationattachment`) | `ResolveContentOwnerAsync` (2738-2745), `ContentOf("sprk_communication")`, i.e. the creator while unfiled. Created by `CreateAttachmentRecordsAsync` (3284-3327) on the email path only. | Impersonated read: `CommunicationThreadReadService.LoadAttachmentsAsync` (1357-1394); `CommunicationAttachmentTextService` (169-240). No cascade, no BFF owner check. | Yes, when owned by the Direct team (Part B). |
| Participant row (`sprk_communicationparticipant`) | Same content owner (`CommunicationParticipantIndexer.cs:93-126`). | Impersonated, only for the `participant=` facet; explicitly not the access gate (`CommunicationThreadReadService.cs:1005-1059`, query 1042). | Yes, for the facet (Part B). |
| Document (`sprk_document.sprk_relatedcommunication`: `.eml`, attachment documents) | Same content owner (`CommunicationService.cs:3053`, `:3388`). | Impersonated (`CommunicationAttachmentTextService`). | Yes, when owned by the Direct team (Part B). |
| Gate Decision Record | Own row; `GateDecisionRecordSharing.MirrorCommunicationSharesAsync` (143-162) mirrors the message's shares, and skips team-owned communications (228-235). `ShareAtWriteAsync` re-owns when team-owned. | Own POA share. | Yes. Part E (ontology-owned file). |
| `sprk_emailreviewlog` | Email review path only. | Not on the Direct message path. | No. |

- No content row reaches a participant through the message's owner: there is no cascade and no BFF check that compares the reader with the message owner. Under the impersonated read a participant reads a content row through its own Read (role depth or a share).
- **Ordering:** on every live path the content rows are created AFTER the message grant. Outbound: thread resolve and grant at `CommunicationService.cs:980` / `:1538`, participant index at `:1010`. Inbound: `MessagingIngestor.cs:131` grant, participants at `:170`. Explicit thread: `AssignExplicitThreadAsync` (1561-1608), grant at 1595. Email attachments: `CreateAttachmentRecordsAsync` calls at 2117 / 2458 follow the thread resolve at 2014 / 2356. **So Part B's content-row grant covers only rows that exist when it runs; Part C's re-grant and backstop must also cover content rows created after the grant.**
- `MessageAttachmentMaterializer` has no production caller (DI only, `CommunicationModule.cs:249`): Message-channel messages create no attachment rows on any live path today.

### (c) Restricted / NoAccess removers and `sprk_communication`

- `RestrictedExternalShareRemover` and `NoAccessShareEnforcer` act on root types only; children follow only for SECURE roots via `SyncRootAsync`. Neither covers `sprk_communication` on an ordinary Restricted root.
- The one eligibility predicate: `InternalShareEndpoints.IsBarredOnRestricted(bool? isExternal, bool? rootIsRestricted) => isExternal == true && rootIsRestricted is not false` (`Api/ExternalAccess/InternalShareEndpoints.cs:1427`).
- The internal thread read applies `CommunicationAccessFilter` (internal-only) under impersonation; it does not apply the Restricted bar to a participant flagged external.
- **Consequence for Part D:** "Restricted target → barred participant loses the message shares" needs new code (extend a remover, or a step in the re-file path). Where it hooks, and whether it applies only to Direct messages, is a design decision this brief does not answer. **Not implemented; open question Q2.**

### (d) "Leave conversation" path for Direct threads

None in Dataverse. `Services/Communication/Membership/MembershipReconciler.cs:129` removes ACS chat participants only; `Membership/` has no POA revoke.

## §2 Part A — Direct-BU mode of `scripts/Set-CustomerOwnerTeams.ps1`

**Built:**
- Parameter set `Direct`: `-DirectMessageBusinessUnitName` (mandatory there).
- Its own declared set, `$DirectMessageRoleFiles = @('direct-message-owner-role.json')`, and its own reader, `Read-DirectConfig`, which accepts `businessUnit: "direct"` only. `Read-Config` (customer mode) still accepts `customer` only, so neither mode reads the other's file. `$DeclaredRoleFiles` and `RestrictedContentOwnerRoleGuardTests` are unchanged.
- `config/direct-message-owner-role.json`: team 'Spaarke Direct Message Owners', role 'Direct Message Owner', Read at Basic on `sprk_communication` (`prvReadsprk_Communication`) only. A top-level `todo` field marks TODO-fm: fm 054a sends the final table list.

**Refusals (all exit 1, before any write):**
- Shared with the customer mode, unchanged: the root, Secure Record, a unit that is not a direct child of the root, an ambiguous unit name.
- Direct-only: a missing unit (the script never creates it); any user homed in the unit (`systemusers?$filter=_businessunitid_value eq <unit>`).
- Handled as for a customer unit: a team member and other owner-decision drift (the dry run exits 1, `-Apply` refuses, `-Verify` fails); an ambiguous team or role (throw).
- In Direct mode `$customerTops` is the Direct unit alone, so a copy of the Direct role on a team in any other unit is `ROLE ASSIGNED ELSEWHERE`.
- `-Apply` keeps the existing rules: it never removes a member (except members the create itself added to a team the same run created), never moves a team, and never takes a role off another principal.

**Tests** (`tests/scripts/Set-CustomerOwnerTeams.Tests.ps1`, Pester 5.7.1, mocked Dataverse; any write fails the test):
- 22 pass.
- Fail-first: against the previous script, 16 fail (every Direct test) and the 6 customer-mode tests pass (the 4 existing, plus "customer mode never plans the Direct team" and "customer mode refuses a direct config").
- Arch guards (`RestrictedContentOwnerRoleGuardTests`, `SignalWriterRoleGuardTests`, `SpeAdminOperatorEnvironmentMarkerGuardTests`): 73/73.

**Dev dry run** (read-only, spaarkedev1, 2026-11-09, operator az identity, `-DirectMessageBusinessUnitName 'Spaarke Direct Messages'`): exit 1, "no business unit is named 'Spaarke Direct Messages' … does not create the Direct-message business unit". This is expected because the unit does not exist yet. No `-Apply` was run.

**§11 (new surface: one script parameter set, one config file):**
1. *Existing:* `Set-CustomerOwnerTeams.ps1` already sets up memberless Basic-Read owner teams; `config/secure-record-owner-role.json` covers the Secure team.
2. *Extension:* this IS the extension, a mode of the existing script. A new script was not written. The customer mode's `businessUnit: customer` rule and its declared-set guard must stay exact, so the Direct file needs its own declared set rather than an entry in `$DeclaredRoleFiles`.
3. *Cost of doing nothing:* fm 054a assigns unfiled Direct messages to the Direct team, and Dataverse refuses that assignment while no team in the Direct unit holds Read on `sprk_communication` ('Read Privilege Check For Owner failed').

**Not done (follow-ups):**
- Done since: the role file guard (§2d).
- The customer mode does not know the Direct unit's name, so it would accept the Direct unit as a customer unit (it is a direct child of the root). Refusing it there needs the Direct unit's name in the customer mode (for example, a `-DirectMessageBusinessUnitName` exclusion), which changes the customer mode. Resolved: **Q4** (§2c).

## §2b Part A role file (fm confirmed the tables 2026-11-09)

`config/direct-message-owner-role.json` holds Read at Basic on exactly six tables: `sprk_communication`, `sprk_communicationattachment`, `sprk_communicationparticipant`, `sprk_emailreviewlog`, `sprk_document` and `sprk_decisionrecord`. Privilege names come from `config/secure-record-owner-role.json`. Pester: 22/22.

## §3 Part B — participant grants on a Direct-team-owned message

**Merged:** fm 054a lookup `6c1ad9cb16` (`git merge --no-ff`, merge `5e4eeed44f`). fm's fix sha, when it comes, merges on top.

**The one check:** `DirectThreadAccessService.IsDirectMessageOwnerTeamAsync`:
1. `CannotBeDirectOwnerTeamAsync` returns true → not Direct.
2. Otherwise resolve: Resolved → Direct exactly when the ids match.
3. `NotConfigured` (no fault) → not Direct. This is **Q5**, see below.
4. Any other refusal, or a fault → unknown → grant nothing (TODO Part C: enqueue a re-grant).
5. A fault in the one-row check propagates to the method's catch, which also grants nothing.

**When the message is Direct-team-owned:**
- The audience must be `DirectParticipants`; any other join grants nothing.
- The participants (thread owner ∪ thread POA systemuser sharees, so the sender is included) get Read on the message.
- They also get Read on the rows of `sprk_communicationattachment`, `sprk_communicationparticipant`, `sprk_emailreviewlog` (lookup `sprk_communication`) and `sprk_document` (lookup `sprk_relatedcommunication`) whose `owningteam` is the Direct team. Each read is capped at 250 rows per table, and a capped read is logged.
- `sprk_decisionrecord` is excluded: it is Part E's (`GateDecisionRecordSharing`).
- Every grant is best-effort, per row and per principal.

**Tests** (`DirectThreadAccessServiceTests`, real lookup over `TestInfrastructure/DirectMessageOwnerTeamDouble.cs`):
- 35 pass.
- Fail-first: against the pre-Part-B service, 6 fail: content rows, a join that is not Direct, no team or non-participant, best-effort, the lookup faults, refused.
- The must-not-fire tests pass both before and after: another team, a lookalike in another unit, user-owned (the lookup faults if asked), secure, not configured.
- Other constructors (`SecureChildShareMirrorTests`, `SecureChildOwnershipAiToolTests.RefileThreadCaller`) pass the unconfigured lookup.
- Affected suites (`Services.Communication` + `ExternalAccess` + `DirectMessageOwnerTeam` filter): 6409/6409. BFF build: 0 warnings.

**§11 (new surface):**
1. *Existing:* `GrantMessageAccessAsync` already grants a message's participants; the new parts are the Direct-team branch and the content-row grant inside the same class.
2. *Extension:* this is an extension of the existing grant method. Nothing else shares Direct content rows: `SecureChildShareSynchronizer` covers only Secure-team rows, and there is no cascade (NoCascade).
3. *Cost of doing nothing:* after fm 054a a Direct-team-owned message and its content rows are readable by no participant, because the team has no members.

New surface:
- Test double `DirectMessageOwnerTeamDouble`. It reuses fm's fake pattern, shared because three test classes construct the service.
- Constructor parameter `DirectMessageOwnerTeam`, already a DI singleton (`CommunicationModule.cs:725`).

## §3b Q5 applied (owner ruling 2026-11-09)

`NotConfigured` with no fault = not Direct. Every other refusal, and any fault (the one-row check's included), = unknown: nothing is granted (fail closed) and the message is left for Part C's re-grant queue (`TODO(task 198 Part C)` at the caller). The decision lives in ONE private method, `DirectThreadAccessService.IsDirectMessageOwnerTeamAsync`, so fm's identity-only API change edits one place.

Test: `DirectTeamMessage_WhenTheOneRowCheckFaults_IsUnknown_ReachesTheOneFailClosedBranch` (a one-row-check fault reaches the unknown branch, grants nothing, and is not logged as a non-fatal lookup failure). `DirectThreadAccessServiceTests`: 36/36.

## §2c Q4 applied — customer mode refuses the Direct unit

- `-DirectMessageBusinessUnitName` (default `'Spaarke Direct Messages'`) is now in all three parameter sets; it stays mandatory in `Direct`, which is the default set.
- Customer mode reads the units with that name ONCE and decides by id: the customer unit whose id is among them is refused (exit 1, before any write), and those ids are dropped from `$customerTops`. There is no in-code name compare.
- The change is additive (one block after `$customerTops`, one parameter attribute set, one help paragraph), so the ebilling 130-0/130-0b rewrite merges as separate hunks.
- Pester: 27/27. Fail-first: against the previous script, 22 pass and 5 fail (the 5 new ones). Must-fire: refused by name, by id with `-Apply`, and under a non-default name. Must-not-fire: an ordinary customer unit plans (exit 0, Direct unit absent from the plan), and a name no unit has changes nothing.

## §2d Role file guard

`tests/Spaarke.ArchTests/DirectMessageOwnerRoleGuardTests.cs` pins `config/direct-message-owner-role.json`:
- schemaVersion 1, `businessUnit: "direct"`, the team and role names;
- Read at Basic on exactly the six tables, with the Read privilege name `UserPrivilegeChecker.ReadPrivilegeEntity` derives;
- no extra entry fields; reason and evidence present;
- the Direct team and role differ from the customer and Secure Record ones.

Must-fire: an 11-case theory (each mutation of the live file yields its named finding). The `todo` field is gone.

## §5 Part D — keep on filing, and the Restricted bar

**Rules:**
- Ordinary target: every participant keeps the shares on the message and its content rows.
- Secure target: the synchronizer decides (the message mirrors the root's set).
- Restricted target, its own value (100000002) or inherited through its filing (`InheritedAccessPermission`): every system user holding a direct share on the message or a content row (attachment, participant, email review log, document) for whom `InternalShareEndpoints.IsBarredOnRestricted(isExternal, rootIsRestricted: true)` is true loses those shares. It applies to every communication, not only Direct ones.

**Where:** `Services/Access/RestrictedFilingShareBar.cs`, called from two places:
- `SecureChildReconciler.AfterRefileCoreAsync`, the step every re-file writer runs, after the mirror and the gate re-own;
- the job that revisits filed communications (below).

**Fail closed:** nothing is ever granted.
- A target that cannot be decided (no probe, a fault, an undeterminable lineage) removes nothing.
- A user is barred only on a stored `sprk_isexternal = true`; a blank flag is internal (owner round 67).
- A user-flag read fault removes nothing.
- A user the read does not return is logged and left as is.
- A share read fault leaves that row as it is.
- Each revoke is confirmed by a strict read-back.

**§11:**
1. *Existing:* `RestrictedExternalShareRemover` applies the same rule to Restricted ROOTS (and secure children via `SyncRootAsync`); `NoAccessShareEnforcer` works per No Access entry. Neither covers a communication under an ordinary Restricted root.
2. *Extension:* the bar reuses the remover's predicate (`IsBarredOnRestricted`) and its user-flag read (`RestrictedExternalShareRemover.ReadUsersAsync`, now `internal static` over the client, with the instance method delegating), plus the one inherited Access Permission rule. It is not a remover method, because the remover's unit is a root under a per-record lease with a last-reader report and root-set cache eviction, none of which applies to a message. No DI registration: both callers construct it from what they already hold.
3. *Cost of doing nothing:* the rule "a user flagged external holds no share on a Restricted record's content" has no writer for communications filed under an ordinary record.

**Decisions recorded:**
- No lease: the lease serializes last-reader decisions on roots, and no last-reader rule applies to a message.
- Gate Decision Records are excluded: `ReownAfterFilingAsync` revokes their shares on every communication re-file.
- An external OWNER of a content row is not a share, so its revoke fails; that is logged and the outcome is incomplete.
- Content rows are capped at 250 per table; a capped read is logged and the outcome is incomplete.

**The existing-job question: yes.** `ChildAccessPermissionReconciler`, inside `SecureChildReconciliationJob` (2-minute schedule), already keeps a communication's `sprk_accesspermission` equal to what it inherits, so it sees a root becoming Restricted. It was extended rather than a new job built:
- When it writes Restricted on a communication, or finds a carried communication already Restricted, it calls the bar's `RemoveBarredSharesAsync`.
- An incomplete result carries the row to the next run and adds a problem line.
- It never runs in a report-only run.

No follow-up job is needed.

**Tests:**
- `RestrictedFilingShareBarTests` (7) through the real reconciler:
  - pin, ordinary target: all kept, and users never read;
  - pin, Secure target: the mirror;
  - must-fire: an external user on a Restricted target loses the message and all four content rows;
  - must-fire, inherited: event under a Restricted matter;
  - must-not-fire: internal and blank-flag users keep everything;
  - fail closed: a user-read fault removes nothing;
  - fail closed: an unread user is left while the external one is removed.
- `ChildAccessPermissionReconcileTests.Task198` (3) through the job:
  - must-fire: a matter turning Restricted removes the external user's shares, content row included;
  - must-not-fire: Limited keeps all;
  - fail closed: an unread user is left and the row is carried, then barred on the next run once readable.
- Fail-first, with the two call sites disabled: the 5 must-fire / fail-closed-removal tests fail and the 5 must-not-fire tests pass.
- Affected slices, all green:
  - access (SecureChild, ChildAccessPermission, RestrictedFiling, RecordOwnership, DocumentContainer, RestrictedExternal): 3892;
  - Services.Communication, DirectMessageOwnerTeam, DecisionRecord and Signals: 5737 (2 skipped);
  - ExternalAccess: 4923;
  - Services.Office and ReconciledEmailFiling: 162.
- BFF build: 0 warnings.
- ArchTests: 2118/2119. The one failure is "045a guard 1" on `CommunicationService.cs` `CopyRegardingFromSourceAsync`. It predates this task (commits 045c on that file, not touched here).
- Test doubles extended:
  - `FakeRecordShareTable` maps `sprk_communicationparticipants` and `sprk_emailreviewlogs`;
  - the job harness registers the share seam, as the host does;
  - `LinkColumnSecurity` answers the user-flag read.

**Not covered here:** a communication created directly under an already-Restricted record, with shares granted at create, is not re-filed and is not written by the job (its value is already correct). Today no create path grants a participant share on a filed communication: Part B grants only on Direct-team-owned (unfiled) messages. Part C's re-grant must not grant a barred user on a Restricted filing (use the same predicate).

## Open questions

- **Q1:** After an ordinary record → unfiled move (owner back to the Direct team), should anchor-grantee shares be removed so the audience is exactly the participants? No current path removes them.
- **Q3:** The Read depth the internal roles hold on `sprk_communicationattachment` / `sprk_communicationparticipant` / `sprk_document` was not checked live (no live reads in this run). Part B grants the shares regardless.
- **Q6:** the Secure twin's accent fix (`SecureChildShareSynchronizer.CannotBeSecureOwnerTeamAsync`) runs on a separate branch; not touched here.

**Resolved:**
- **Q2:** a step in `AfterRefileCoreAsync` plus the existing job, for every communication (§5).
- **Q4:** yes, by id (§2c).
- **Q5:** `NotConfigured` = not Direct; everything else unknown (§3b).

**Pending upstream:** Part C (re-grant queue: unfile, secure exit, content rows created after the grant, the unknown branch) and Part E (Gate Decision Records, ontology-owned).