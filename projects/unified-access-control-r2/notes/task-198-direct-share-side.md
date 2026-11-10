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

## §2e Fix round 1, item R6 (`Set-CustomerOwnerTeams.ps1`)

**Item 1, Direct mode, accents.** The Direct unit was found with `$units | Where-Object { $_.name -eq ... }`. PowerShell `-eq` ignores case but not accents; Dataverse's `name eq` ignores both. Two units that differ only by an accent passed `-Verify` while the BFF refused the name as ambiguous. Now Direct mode reads the unit with `businessunits?$filter=name eq '<Q name>'` (the same escaping as the customer-mode read) and decides by the ids returned: 0 → the existing "no business unit is named ... does not create" refusal; more than 1 → the existing "Expected exactly one business unit" refusal (exit 1, before any write). No name is compared in code on the Direct unit path.

**Item 2, customer mode, non-default Direct name.** The Direct unit was refused only when found by `-DirectMessageBusinessUnitName`. With a non-default name and the parameter omitted, `-Apply` would have created customer teams in it. Now customer mode also resolves the Direct role (`direct-message-owner-role.json`, read with `Read-DirectConfig`, `roleName` used only as a Dataverse `name eq` filter in the root), takes its copies by `_parentrootroleid_value eq <id>`, and reads the teams holding the root role or a copy. Any unit of such a team is refused as the customer unit (exit 1, "holds a team with the Direct-message owner role", before any write) and dropped from the other-units list. No role is matched by name in code. A Direct role that does not exist yet refuses nothing.

**Pester** (`tests/scripts/Set-CustomerOwnerTeams.Tests.ps1`, Pester 5.7.1): 27 before → 33 after. The test mock now folds accents and case in `name eq`, as Dataverse does.
- Fail-first, against the old script: 28 pass, 5 fail (the 5 must-fire tests below; a sixth new test passes both ways).
- Must-fire: accent-only pair → `-Verify` exits 1; the same pair with `-Apply` refuses with no write; non-default-name Direct unit refused by name (with `-Apply`, no write) and by id.
- Must-not-fire: ordinary customer unit plans (exit 0) and the Direct-role unit is not listed beside it; a lone unit that differs by case only still verifies (exit 0); the existing Direct-mode clean-unit tests unchanged.

**Class fix (coordinator follow-up, same branch).** The two remaining unit-name comparisons in code are gone: the Secure Record lookup (`$secureUnits`) and customer mode's `-CustomerBusinessUnitName` lookup now use Dataverse's `name eq` filter and decide by the ids returned (0 → the existing not-found handling; more than 1 → the existing ambiguous refusal, exit 1, before any write). Every business unit named by a parameter is now found that way. The by-id lookup is an id compare and stays.
- Pester: 33 → 36. Fail-first against the previous script: 34 pass, 2 fail (the two must-fire accent-pair tests, one for `-SecureBusinessUnitName`, one for `-CustomerBusinessUnitName` under `-Apply`, no write). Third new test, must-not-fire: `CUSTOMER ONE` (case only) resolves to the one unit, exit 0. Full run after: 36/36 (Pester 5.7.1).
- Audit of the script for `-eq`/`-ne`/`-like`/`-match`/`-contains`/`.Equals` on a unit, team or role name: none left on Dataverse-read data. Kept, with the reason:
  - `$privs[0].Name -cne $t.privilegeName` (privilege schema name against the config): deliberately case-SENSITIVE, a casing check of the config against metadata, not an identity lookup of a unit, team or role.
  - `Group-Object` on the config files' `teamName` / `roleName` and table names (duplicate declarations across the repo's own config files): config against config, never Dataverse data.
  - `Sort-Object name` and the names in messages: ordering and display only.
  Team and role lookups already use `name eq` filters, and their results are compared by id.

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
- ~~When it writes Restricted on a communication, or finds a carried communication already Restricted, it calls the bar's `RemoveBarredSharesAsync`.~~ Superseded by fix round 1 R3 (§7.3): it calls the bar on EVERY communication it examines whose own (filed under nothing) or inherited value is Restricted, bounded per run.
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

**Not covered here (corrected in fix round 1, R3, §7.3):** a communication created directly under an already-Restricted record, with shares granted at create, is not re-filed and is not written by the job (its value is already correct). ~~Today no create path grants a participant share on a filed communication: Part B grants only on Direct-team-owned (unfiled) messages.~~ **That premise was false** (adversarial review pass 1, finding 4): the record-anchored grant (`DirectThreadAccessService.GrantMessageAccessAsync`, the `AnchorRecordMembers` branch, via `ThreadMembershipDerivationService.DeriveAnchorGranteeUserIdsAsync`) grants the anchor record's members on a FILED communication at create, with no Restricted filter. R3 bars it at grant time and makes the job bar every Restricted communication it examines, so a legacy share is removed by the sweep. Part C's re-grant must not grant a barred user on a Restricted filing (use the same predicate).
## §6 Secure owner-team accent fix (Q6, F1)

**Rule:** whether a team can be the Secure Record owner team is decided by Dataverse's own `name` match, never by a name comparison in code. Dataverse's `name eq` ignores case and accents, so a code comparison (ordinal or `OrdinalIgnoreCase`) can answer "another name" for the team Dataverse matches.

**Fix** (`Services/Access/SecureChildShareSynchronizer.cs`, `CannotBeSecureOwnerTeamAsync`):
- Read 1: `team` with `teamid eq <id>` AND `name eq <SecureRecord:OwnerTeamName>` (QueryExpression condition, so Dataverse binds the value; no string building), selecting `isdefault`, `teamtype`. A row: ruled out only when it is a default team or not an Owner team; otherwise `false` (the full resolution compares ids).
- No row: read 2 by id. The row exists → `true` (Dataverse says another name). Not found → `false`.
- The name is no longer selected or compared in code. Callers (`DirectThreadAccessService.ReadMessageOwnershipAsync`, `ThreadResolver.ThreadOwnerMayBeSecureAsync`, the synchronizer's scoped run) are unchanged: `false` still means "resolve and compare ids".

**Grep** (Grep tool over `src/`; patterns: `OwnerTeamName\(|BusinessUnitName\(|*NameConfigKey`; the owner-team literals `Restricted Content Owners|Customer Configuration Owners|Direct Message Owners|Secure Record Owners|"Secure Record"`; `Equals(...name...)` / `==` on bu/team/owner names; `GetAttributeValue<string>("name")`; TS/JS/PS `===` on team/BU names):

| Match | Owner | Kind | Action |
|---|---|---|---|
| `Services/Access/SecureChildShareSynchronizer.cs:1360` `string.Equals(name.Trim(), OwnerTeamName(...), OrdinalIgnoreCase)` | uac | in-code team-name compare; fails open (answers "cannot be Secure" for the real Secure team) | **Fixed** (above) |
| `Infrastructure/Dataverse/DirectMessageOwnerTeam.cs:140` `CannotBeDirectOwnerTeamAsync`: `string.Equals(name.Trim(), teamName, OrdinalIgnoreCase)` | fm | the same pattern for the Direct team: an accent variant answers "not the Direct team" for the real Direct team (participants then get the ordinary grant path, not the Direct one) | **Not edited (fm's file).** Reported to fm. |
| `Infrastructure/Dataverse/DirectMessageOwnerTeam.cs:83` `string.Equals(buName, SecureRecordOwnerTeam.BusinessUnitName(...), OrdinalIgnoreCase)` | fm | in-code BU-name compare; an accent variant of the Secure unit's name escapes the `bu_is_secure_record` refusal (the later lookup would then resolve the Secure unit as the Direct unit unless another check refuses) | **Not edited (fm's file).** Reported to fm; fix = compare resolved BU ids. |
| `Infrastructure/Dataverse/SecureRecordOwnerTeam.cs` | fm | no in-code compare: BU and team are found with `name eq` (OData-escaped via `EscapeODataStringLiteral`) and `ResolveSecureBusinessUnitAsync` uses a `name` condition | none |
| `Infrastructure/Dataverse/SecureBuRoleDepthAssertion.cs:178` (Secure BU by name) and `:834` (owner team by name), fed by `Services/ExternalAccess/SecureRecordIsolationCensus.cs:69-70` | not in the uac-owned list (Infrastructure/Dataverse) | census: reads every BU/team, then picks the Secure unit/team by `OrdinalIgnoreCase` in code. An accent variant finds no Secure unit, so the census reports it missing (fails loud, not open) | **Not edited** (outside the listed paths). Listed for the owner. |
| `Services/Dataverse/RecordOwnershipResolver.cs:4409-4418`, `Infrastructure/Dataverse/RestrictedContentOwnerTeam.cs:109` | uac / shared | `name` is a QueryExpression condition; ids compared after | none |
| `Services/Registration/RegistrationDataverseService.cs:955`, `Infrastructure/ExternalAccess/ContactIdentityBinder.cs:1263`, `Api/ExternalAccess/ExternalRegistrationEndpoints.cs:201` | uac / registration | Secure BU found by a Dataverse name filter, then compared by id | none |
| `src/server/services/Sprk.Provisioning.ControlPlane.Core/.../CustomerBusinessUnitIntake.cs:68` | control plane | intake refuses a customer unit named like the Secure unit, `OrdinalIgnoreCase` | **Not edited** (another service). Listed. |
| `Customer Configuration Owners` | — | no match in `src/` | none |
| TS / JS / PS1 under `src/` | — | no name comparison (only comments and test fixtures) | none |

**Tests** (`tests/unit/Sprk.Bff.Api.Tests/`):
- New `Services/Access/SecureOwnerTeamNameMatchTests.cs` (11): a Dataverse double that evaluates `team` / `businessunit` conditions with a case- and accent-insensitive collation and returns only the selected columns. Must fire: three accent variants of the team setting are not ruled out; accent variants of the team and/or BU setting resolve the real team id; the name is a query condition, not a selected column. Must not fire: another team is ruled out (also under an accent-variant setting); a setting that names no team rules out the Secure team and the resolver finds none; a default team and an Access team are ruled out; a team not found is not ruled out.
- New `DirectThreadAccessServiceTests.ASecureMessage_WithAnAccentVariantOfTheSecureTeamSetting_GrantsNothing` (caller level).
- Updated mocks: `DirectThreadAccessServiceTests.MessageOwnedByOwnerTeam` / `MessageOwnedByOrdinaryTeam` answered every `team` read with one row, whatever its `name` condition. They now evaluate the conditions through the same helper the new caller test uses. No assertion was changed.
- Fail-first, against the previous synchronizer: 7 of the 12 new tests fail (6 in `SecureOwnerTeamNameMatchTests`, plus the caller test). The 5 must-not-fire tests pass. Affected suites on the old code (filter `ExternalAccess|.Access.|Communication|SecureChild|RecordOwnership|DirectMessageOwnerTeam`): 8913 pass, 7 fail (exactly the new tests), 2 skipped.
- After: the same filter gives 8920 pass, 0 fail, 2 skipped. ArchTests (`SecureRecordOwnerRoleSetParity`, `RouteAuthorizationGuard`, `RecordOwnerAssignmentCensus`, `PoaShareClientSingletonGuard`, `DocumentAccessPermissionWriterGuard`): 191/191. BFF build: 0 warnings, 0 errors.
- A full unconfined run of `Sprk.Bff.Api.Tests` on this machine hung in an HTTP contract test after about 35 minutes, and was stopped. Its web-host contract failures (Office, Insights and others) do not touch this change; the filtered suites above are the evidence.

### §6.1 K1 — Secure settings that name nothing (fm's review of the twin)

**Rule:** Secure settings that are PRESENT but name a unit or team Dataverse does not find, or a read that faults, never answer "cannot be Secure". On the grant path (and the thread-owner move) that is "unknown", and the caller fails closed.

**Settings present** = `SecureRecord:BusinessUnitName` or `SecureRecord:OwnerTeamName` is set and not blank (`SecureChildShareSynchronizer.SecureSettingsConfigured`). A value equal to the compiled default still counts as present. `appsettings.template.json` sets both, so an environment deployed from the template counts as configured.

**`CannotBeSecureOwnerTeamAsync`** (when the `teamid` + `name eq` read finds no row):
- Settings present: `true` only when a non-default Owner team carries the configured team name (`name eq` + `teamtype` Owner + `isdefault eq false`, one read). No team carries it → `false`.
  - The unit setting is not read here. The real Secure team must carry the configured team name, so a team without it is not the Secure team, whichever unit is configured.
  - The first version of this fix resolved the whole Secure team (unit included) at this point. That broke task 149 F7 (`SecureChildShareMirrorTests.ShareUser_OnAnOrdinaryRecord_IsNotRefused_WhenTheSecureRecordTeamCannotBeResolved`): an ordinary record's share was refused while the Secure unit was ambiguous, and the unit was read. The narrower proof keeps F7.
  - A unit setting that finds nothing is caught by `IsSecureOwnerTeamAsync` (unknown → fail closed) for the real Secure team, whose name matches.
  - Residual edge: a team setting that is wrong but happens to name some other existing Owner team. No name-based check can detect that.
- Settings absent (compiled defaults): unchanged, by-id read; the team exists → `true`, not found → `false`.
- Any fault → `false` (cancellation propagates). Before this, a fault propagated.

**New `IsSecureOwnerTeamAsync`** (`true` / `false` / `null` + reason): `CannotBe…` first, then `ResolveSecureOwnerTeamAsync` and an id compare. When the resolver finds no team: settings absent → `false`; settings present → `null`.
- A fault in the resolution propagates (the callers' catch grants nothing).
- §11: (1) *Existing:* `DirectThreadAccessService` and `ThreadResolver` each composed `CannotBe…` + resolve + compare by hand. (2) *Extension:* it IS that composition, moved beside the two helpers it composes; both copies now call it. (3) *Cost of doing nothing:* each copy must re-implement "present but not found = unknown", and both copies already read "no team" as "not Secure".

**Callers changed:**
- `DirectThreadAccessService.ReadMessageOwnershipAsync`: `null` → no grant (TODO Part C: re-grant queue).
- `ThreadResolver.ThreadOwnerMayBeSecureAsync`: `null` → "may be Secure", so the thread is not moved. This file belongs to communication task 129; the edit is the one call, and its doc already says it uses DirectThreadAccessService's detection.
- The synchronizer's scoped run (`:1250`) is unchanged: with wrong settings it answers NotApplicable, and it grants nothing.

**Settings absent — the behaviour kept (item 2):** the compiled defaults ('Secure Record' / 'Secure Record Owners') are looked up. When the environment has no unit or team by those names, no team is the Secure team, a team-owned message is ordinary, and its participants are granted, as before.

**Item 3 — `SecureRecordOwnerTeam.ResolveSecureBusinessUnitAsync` (`:359`, not found → `(null, false)`):** left unchanged (it is also fm's file).
- Grant path: its only route to a participant grant is through `ResolveSecureOwnerTeamAsync` → `IsSecureOwnerTeamAsync`, which is now fail closed.
- Direct callers: `RecordOwnershipResolver` (filing owner), `SecureChildReconciler` (re-file guards; with no Secure team the mirror grants nothing), `DocumentContainerRelocator` (container placement), `TodoAssigneeOwnerReconciliationJob` (owner), `DecisionParameterSources` (budget currency), `SecureRootInheritance.Cascade`. None of them writes a participant share.
- Filing does NOT fully fail closed today: `:359` documents the fail-open edge (before guide §4.3 step 4, a child filed to a secure record can land on the Secure unit's default team). That edge was accepted by the task-144 verifier and is not changed here.
- **Not fixed, needs an owner decision:** other consumers read `ResolveSecureOwnerTeamAsync`'s "no team" as "no record is secure". `BillingFactLoader.SecureRootsAsync:723` decides whether Duplicate shows prior detail (a disclosure). `SignalSuppressionService.ResolveSecureTeamAsync:296` partitions signals. Making the resolver itself refuse when settings are present would close them all, but would also turn every reconcile, re-file and secure-create into a refusal in any environment that sets the keys without a Secure unit. That is a global contract change, so it is not done here.

**Tests:**
- `SecureOwnerTeamNameMatchTests` gains 10 and replaces 1:
  - Item 4: the test "a setting that names no team rules out the Secure team" asserted the fail-open answer. It is replaced by `WrongSecureSettings_DoNotRuleOutTheRealSecureTeam_AndIsSecureIsUnknown` (3 cases: wrong team, wrong unit, both).
  - Added: wrong settings do not rule out an ordinary team either; a wrong unit setting with the right team name rules an ordinary team out by name with no unit read, while the real Secure team is unknown; a read fault → not ruled out; `IsSecureOwnerTeamAsync` fault propagates; correct settings + an ordinary team → ruled out and not Secure, the Secure team → Secure; settings absent + no Secure unit → an ordinary team is ruled out and not Secure; the `SecureSettingsConfigured` truth table.
- `DirectThreadAccessServiceTests` gains `ASecureMessage_WithSecureSettingsThatFindNothing_GrantsNothing` (2 cases) and the must-not-fire `AnOrdinaryTeamMessage_WithCorrectSecureSettings_KeepsTheParticipantGrant`.
- Fail-first: HEAD (`e75cabb0c5`) synchronizer, `DirectThreadAccessService` and `ThreadResolver`, with the two new helpers spliced in unchanged so the tests compile (old `CannotBe…` and old caller logic). Filter `DirectThreadAccessServiceTests|SecureOwnerTeamNameMatchTests|ThreadResolverTests|SecureChildShareMirrorTests`: 6 of 283 fail (the 3 synchronizer-level must-fire tests, 4 cases, and both caller-level cases); 277 pass. After the fix: 283/283.
- Scoped filter (`ExternalAccess|.Access.|Communication|SecureChild|RecordOwnership|DirectMessageOwnerTeam`): 8932 pass, 0 fail, 2 skipped. ArchTests (same five classes): 191/191. BFF build: 0 warnings, 0 errors.

## §7 Part C — durable re-grant, backstop, unfile

**Rules:**
- The audience of a message owned by the Direct-message owner team is exactly its thread's participants: the thread's user owner and its systemuser sharees. They hold Read on the message and on the content rows the Direct team owns (attachment, participant, email review log, document). Nobody else holds a direct user share on the message (team shares are untouched).
- Every unknown grants nothing and leaves a durable trace that the share job retries until the grant lands. The unknowns are: a faulted grant or share read; the Direct team refused or faulted; the Secure check undecided; an unconfirmed revoke; a capped or faulted content-row read; a user-flag read fault; a participant the flag read does not return. The trace is the Dataverse row itself (a Direct-team-owned message that lacks a share), which the job lists, plus the job's carry. No owner-writable marker is used.
- Secure true: nothing is granted here; the synchronizer decides. Secure unknown: nothing is granted; retried.
- The Restricted bar applies to every grant path (the live grant, the re-file step and the job): `InternalShareEndpoints.IsBarredOnRestricted(isExternal, restricted)` against the message's own `sprk_accesspermission` or the value it inherits through its filing (the same predicate and inherited rule as Part D). Restricted undecidable: a user flagged external is barred.
- K3: the Direct-team identity matches only a resolved, non-empty id equal to a non-empty owner (`DirectMessageParticipantShares.Matches`). A refusal carries no id and never matches, including owner `Guid.Empty`. The one Direct-team check (`IsDirectOwnerTeamAsync`) is shared by the live grant, the re-file step and the job.
- Idempotent: a participant who already holds the Read bit is not granted again, so a run after a complete one writes nothing.

**How the audience is re-derived without an owner-writable column:**
1. Direct-team ownership: only the BFF assigns it (fm 054a). The team is memberless, so no user writes through it.
2. The thread is the message's own `sprk_communicationthread` lookup. On a Direct-team-owned message, a participant holds Read only, so no participant can repoint it.
3. The participants are the thread's user owner plus its POA systemuser sharees, read with the strict share read. A fault grants and revokes nothing.
4. `sprk_threadtype` (must be Direct) and `sprk_isdefaultthread` (must not be the per-user master thread) only narrow. A thread that fails either grants nothing and revokes nothing.

**Where:**
- `Services/Access/DirectMessageParticipantShares.cs` (new, internal): the one reconcile of one message (grant the missing, revoke non-participants on the message with a strict read-back), the K3 helper, and the one Direct-team check.
- `Services/Access/RestrictedFilingShareBar.cs`: new `EligibleForGrantAsync`. `TargetIsRestrictedAsync` gains `includeOwnValue`; Part D's call is unchanged (false).
- `Services/Communication/Access/DirectThreadAccessService.cs`: the live grant now clears its participants through `EligibleForGrantAsync` before writing. The two "TODO Part C" unknown branches now log that the share job retries. Its private Direct check delegates to the shared one. New optional ctor params: `DataverseWebApiClient? webApi`, `CoreAncestorResolver? coreAncestors` (both already registered singletons).
- `Services/Access/SecureChildReconciler.cs`: `AfterRefileCoreAsync` runs the reconcile after the Part D bar, on every communication re-file. A message that is now Direct-team-owned (an unfile or a secure exit back to the Direct team) has its participants granted and the anchor-record grantees revoked. Any other message is left as it is. New optional ctor param: `DirectMessageOwnerTeam? directMessageOwnerTeam` (an already-registered singleton).
- `Services/Access/SecureChildShareReconciliationJob.cs` (the existing 2-minute ADR-036 `IScheduledJob`): a Direct pass after `ReconcileAllAsync`.
  - Each run reconciles: the carried ids; then the Direct-team-owned messages changed since the watermark, plus the messages whose content rows changed since then (500 per table); then a rolling id-ordered sweep slice of 100.
  - Each run handles at most 200 messages. A capped run logs a warning and carries the rest (carry cap 1000; an overflow holds the watermark).
  - The watermark moves only after a clean listing. The sweep cursor resets after a full pass.
  - The Direct team is resolved by id. NotConfigured means the pass is skipped (Ok). Any other refusal or fault: nothing is listed or granted, the carry is kept, and the run reports not Ok.
  - The heartbeat gains the direct fields, and `ResultJson` gains a `directMessages` object. The run's `Success` is false while any Direct message is incomplete.
  - The services are resolved with `GetService`; a missing one reports "unavailable".
- Writes: the host job has no report-only switch (it always writes), so none was added. No new app setting.

**§11:**
- `DirectMessageParticipantShares`:
  1. *Existing:* the live grant (`DirectThreadAccessService.GrantDirectTeamMessageAsync`), the synchronizer (Secure only) and the Part D bar (removes only).
  2. *Extension:* the live grant runs inside a send with the join in hand, while the re-grant must re-derive the audience from Dataverse with no join and must also revoke. One class shared by the re-file step and the job keeps them identical. The Direct check and K3 helper moved there, and the live grant calls them. No DI registration: callers construct it from what they hold.
  3. *Cost of doing nothing:* every unknown branch of Part B ends with participants who are never granted, and an unfile leaves the anchor grantees reading a Direct message (Q1 ruling).
- `RestrictedFilingShareBar.EligibleForGrantAsync`:
  1. *Existing:* `RemoveBarredSharesAsync` (removes after the fact).
  2. *Extension:* it is the same predicate, user read and lineage walk, applied before a grant. It lives in the same class.
  3. *Cost of doing nothing:* ruling 6 (no grant to a barred user) has no check on the grant paths.
- The job's Direct pass:
  1. *Existing:* `SecureChildShareReconciliationJob`.
  2. *Extension:* extended rather than replaced. `MembershipReconcileJob` was rejected: it is a Service Bus handler for ACS membership; an enqueue can itself fail, and it dead-letters after max attempts, so it is not "until it lands".
  3. *Cost of doing nothing:* no retry of any unknown, and no backstop for content rows created after the grant.
- The optional ctor params: pure extension of existing classes with already-registered singletons. Optional so the test compositions compile; the host container injects them.

**Decisions:**
- The durable trace is the row, not a queue: a queue entry can be lost at enqueue, while the row cannot.
- The job re-reads each listed message's owner and compares it by id with the resolved team, so a message re-owned between listing and reconcile is left alone.
- Revoke enforcement runs in the job too. On the message row, the audience is exactly the participants, which also catches a share added by hand. It applies only to direct systemuser shares; team shares are untouched. Content rows are only granted, never revoked.
- One Secure answer per team per instance (one instance per job run or per re-file call).
- Content rows: 250 per table, as in Part B/D. A capped read leaves the message incomplete, so it is retried.

**Tests:**
- New `tests/integration/data-mutation/ExternalAccess/DirectMessageParticipantSharesTests.cs` (21), over the real job, the real reconciler, the real `DirectMessageOwnerTeam` and `SecureChildShareWorld`:
  - K3 (2): a null-id refusal never matches, including owner `Guid.Empty`; a refused lookup answers unknown for a team and "no" for an empty owner.
  - Must-fire: the backstop grants message + 4 content rows; a missing content-row share is the only write; a failed grant is retried and lands, and the third run writes nothing; Direct lookup fault → recovers → granted; Direct refusal → clears (after the cache lifetime) → granted; Secure unknown → known ordinary → granted; a capped run (201 messages) says so and carries 1, which the next run grants; unfile from an ordinary record revokes the anchor grantee and keeps the participants; a secure exit back to the Direct team leaves exactly the participants.
  - Must-not-fire: a non-participant is never granted; an external participant under a Restricted inherited filing (event under a Restricted matter, two runs) and under the message's own Restricted value is never granted; a Secure-owned message (the Direct lookup itself naming the Secure team) is never granted.
  - AC6 pins: the per-user master thread, a record-anchored thread, user-owned messages (pre-054a Direct and unfiled inbound email), a re-file to an ordinary record, and a user-owned re-file are all unchanged; no Direct team configured → not-configured, no writes.
- `DirectThreadAccessServiceTests` (+2, live path): an external participant is not granted when Restricted is undecidable; a user-flag fault grants nothing. `DirectTeamSut` now passes a user directory with the test users internal (the Part B assertions are unchanged).
- Test doubles: `UserFlagDirectoryDouble` (shared, moved out of `RestrictedFilingShareBarTests`); `DirectMessageOwnerTeamDouble.Lookup(TimeProvider?)` and `WithoutUsersInDirectUnit()`; `SecureChildShareWorld.ReconcilerOver(..., directMessageOwnerTeam)`.
- Fail-first, on commit `2d991e661d` with the four call sites disabled (the re-file step, the job pass, the live bar assignment, K3 reverted to `TeamId == owner`): 16 of the 23 new tests fail, and the 7 pins and must-not-fire-only tests pass. Restored: 71/71 (`DirectMessageParticipantShares|DirectThreadAccess|RestrictedFilingShareBar`).
- Counts: see §7.1.

### §7.1 Part C counts

| Run | Before (`2ca6340051`) | After (`2d991e661d`) |
|---|---|---|
| Scoped filter `DirectMessageOwnerTeam\|DirectThreadAccess\|RestrictedFilingShareBar\|SecureChild\|SecureOwnerTeamNameMatch\|ThreadResolver\|ChildAccessPermission\|MembershipReconcile` | 2884/2884 | 2886/2886 (+2 live-path tests) |
| The same filter plus `DirectMessageParticipantShares` (the job tests are in that class and in `SecureChildShareMirrorTests`) | — | 2907/2907 |
| `tests/Spaarke.ArchTests` (full) | 2119/2119 (brief) | 2119/2119 |
| BFF build | 0 warnings | 0 warnings, 0 errors |

The brief's baseline of 2870 was measured at an earlier tip. 2884 was re-measured at `2ca6340051` in a detached worktree.

### §7.2 Fix round 1, batch 1 (R1, R4, R5)

Adversarial review pass 1; coordinator rulings 2026-11-09. R2 (the participant source) waits for fm's choice between a stamped recipient column (A) and the Direct team owning the thread (C). R3 runs after this batch, and R6 (the script) runs in another worktree.

**The participant seam.** `DirectMessageParticipantShares.DeriveParticipantsAsync(dataverse, shares, communicationId, threadId, …)` is the ONE derivation. The live grant (`DirectThreadAccessService.GrantDirectTeamMessageAsync`), the re-file step and the share job all call it. Its source is unchanged in this batch: the thread's user owner and its systemuser sharees, read strictly.
- R2 swaps only this method. `communicationId` is already a parameter, for the new source.
- One consequence in the live grant: a thread-share read that faults now derives nobody (nothing granted; the job retries). It used to grant the owner only.
- The pre-198 path for user-owned and ordinary messages still uses `ReadDirectParticipantsForGrantAsync` (R1: unchanged).

| Finding | Change | Tests (must-fire unless marked) |
|---|---|---|
| **R1** An unknown Direct-team answer cancelled the record-anchored grant (AC6 regression). | (a) `IsDirectOwnerTeamAsync` now takes `IGenericEntityService` and reads the owner team's own row first (`teamid`, selecting `isdefault` and `teamtype`). A default team, or a team type other than Owner, is "not Direct" with no lookup read; a fault in that read lets the lookup decide. (b) `GrantMessageAccessAsync`: only `case null when audience == DirectParticipants` returns early. A record-anchored join whose owner is unknown runs the pre-198 anchor grant. Only a PROVEN Direct owner takes the Direct branch. | `DirectThreadAccessServiceTests`: a default-team message with the lookup faulting → anchor grantee granted; a non-default Owner team with the lookup faulting → granted; with the lookup refusing (configured team missing) → granted. The existing `DirectTeamMessage_WhenTheLookupFaults_GrantsNothing` (Direct join, unknown → no participant grant) still passes. |
| **R5** Race: the message re-owned between the owner read and the writes. | `OwnerUnchangedAsync` re-reads `owningteam` right before the message row's grant and again before its revokes. In the job and the re-file step, a change or a fault means nothing more is written and the outcome is Retry (status Unknown). The live Direct grant re-reads before its message grant. | World: an owner that changes after the first read → nothing written, Unknown, not complete. Moq live: an owner that changes between the ownership read and the grant → no grant at all. |
| **R4** Starvation and the capped listing. | Listings are ordered by `modifiedon` then id, and select `modifiedon`. A capped listing (500) moves the watermark to its last row's `modifiedon` (the earliest across capped tables). If that does not move past the window start, it steps one tick and the sweep covers rows sharing that instant; the step is logged. `DirectSweepReserve = 50` of every run's 200 is the sweep's (the priority budget is 150). Per carried id, failures in a row are counted; at `MaxDirectAttempts = 3` the id is backed off for `DirectBackoff = 30 min` with an Error log, kept in the carry and skipped by every source until then. `DirectMessageRun.Ok` is false while a listing is capped or any id is backed off; `ResultJson` adds `listingCapped` and `backedOff`. | 520 changed messages drain over several runs, and the first run reports `listingCapped` and not Ok. With 250 messages re-changed before every run, a never-changed message is still reached by the sweep. 200 messages that fail every run (smaller ids) do not starve 20 others. A message incomplete 3 runs is backed off (`backedOff:1`, `examined:0`) and tried again after the backoff. The capped-run test is updated for the reserve. |

**Test world:** `SecureChildShareWorld.Answer` now honours a query's `Orders` (stable, with the id order as the tie-break; a missing value sorts first), as Dataverse does. Before, it always returned rows in id order.

**Fail-first** (on commit `f4c83ad083`): R1(a) off, R1(b) reverted to `case null:`, `OwnerUnchangedAsync` always true, `DirectSweepReserve = 0`, `MaxDirectAttempts = int.MaxValue`, and a capped listing holding the watermark. All 9 must-fire tests failed: R1 ×3, R5 ×2, and R4 ×4 (the drain, the sweep, starvation and the backoff). The source was restored to the commit afterwards.
- The backoff test first looped `MaxDirectAttempts` times, which hung under the perturbation and would let a raised constant pass. It now pins the three runs its name states.
- The starvation test is composite: it passes with either the sweep reserve or the backoff. The backoff test pins the backoff alone.

**Counts** (on the batch 1 tip):
- Scoped filter including `DirectMessageParticipantShares`: 2916 passed, up from 2907 (+9 new).
- Job and Direct-message tests (`SecureChildShareReconciliationJob|DirectMessageParticipantShares`): 26 passed.
- Full `Spaarke.ArchTests`: 2119 passed.
- BFF build: 0 warnings, 0 errors.

### §7.3 Fix round 1, R3 (the Restricted bar's durability and the record-anchored grant)

Adversarial review pass 1, findings 3, 4 and 9; coordinator rulings 2026-11-09.

**Findings:**
- **3 (F):** a re-file whose bar did not finish was never retried. `SecureChildReconciler.AfterRefileCoreAsync` discarded `AfterFilingAsync`'s outcome, and the re-file writer stamps Restricted BEFORE the after-filing step, so the job found the stored value equal to the inherited one and barred only ids it carried itself (in memory, lost on a restart).
- **4 (F):** the record-anchored grant (`AnchorRecordMembers`) granted anchor members with no Restricted filter. A message created under an already-Restricted record is stamped at create, never re-filed, and its value never changes, so no bar ever ran. (The §5 premise "no create path grants a participant share on a filed communication" was false; corrected there.)
- **9 (K, covered here):** a message whose OWN value becomes Restricted (filed under nothing) was never barred: the job skips parentless rows.

**Ruling 1 — the job is the durable backstop.** `ChildAccessPermissionReconciler` runs the bar (`RestrictedFilingShareBar.RemoveBarredSharesAsync`) on EVERY communication a writing run examines whose value is Restricted: inherited through its filing (the same `InheritedAccessPermission.ResolveAsync` over the run's `ParentLineageWalk` the bar uses; written now, already stored, deferred by the write cap, or a failed write), or its own stored value when it is filed under nothing. Changed-since rows, their descendants, carried rows and sweep rows alike. Undetermined rows are not barred (they are carried, as before). A row the bar does not finish is carried; after a restart the new instance's sweep finds it again.
- "Own" means a parentless row's stored value. A filed communication's value is the copy of what it inherits (round 81), so a filed row whose stored value is Restricted but whose inheritance is not is NOT barred (the job writes its inherited value).
- **Bounds (kept, and applied to the bar):** at most `maxWrites` bar calls per run (`SecureChild:Reconciliation:MaxAccessPermissionWritesPerRun`, default 500), counted apart from the writes. Rows are taken in this order: written to Restricted this run (never more than `maxWrites`, so never capped), carried, listed as changed, from the sweep, then rows listed only because something above them changed. Past the budget: a warning log and a problem line; capped rows of the first four kinds are carried while the carry stays under `MaxCarriedRows` (carried ones always); the last kind, and any past the carry cap, are counted as `left` and are barred at their next listing (their own change, a parent's change, or the sweep a restart begins). The bar never pushes the carry over its cap by itself, so it never holds the watermark; this avoids a livelock in which a large Restricted matter that keeps changing re-lists the same descendants every run. `ResultJson.accessPermission.restrictedBar` reports `ran`, `incomplete`, `capped`, `left`, `capReached`; the run log line has the same fields. The run's `Success` is unchanged by the bar.
- `SecureChildReconciler.AfterRefileCoreAsync` now logs a Warning when the re-file's bar does not finish (it still does not retry; the job does).

**Ruling 2 — the grant-time bar (coordinator ruling, a deliberate narrowing of AC6).** In `DirectThreadAccessService.GrantMessageAccessAsync`, the `AnchorRecordMembers` branch clears the derived grantees through `ClearAnchorGranteesAsync`:
- New `RestrictedFilingShareBar.RestrictedForGrantAsync` (the private `TargetIsRestrictedAsync` with `includeOwnValue: true`, the same rule `EligibleForGrantAsync` applies). `false` → every grantee, exactly as before, and no user is read.
- `true` or `null` (undecidable) → `RestrictedFilingShareBar.EligibleForGrantAsync` (the one predicate, `InternalShareEndpoints.IsBarredOnRestricted`): a user flagged external is not granted; internal and blank-flag users are. Undecidable: flagged-external not granted, internal granted.
- The Restricted invariant wins over AC6 here (owner rounds 67/76). Every other record-anchored behaviour is unchanged: the Direct branch, the per-user master thread, secure messages, the team skip, the gate-record mirror.
- **Residual, reported:** `EligibleForGrantAsync` is fail closed on the user read. On a Restricted (or undecidable) filing, a user-flag read that faults grants NOBODY, and a grantee the read does not return (for example a disabled user, if the read filters them) is not granted. No job re-grants record-anchored members, so that internal user misses the share until something else grants it. This is limited to Restricted or undecidable filings under a read fault; on any other filing nothing is read and nothing changes.
- Test compositions that build `DirectThreadAccessService` by hand now pass the user reader and the core-ancestor resolver the host always injects (`DirectThreadAccessServiceTests.BuildSut`/`RealDerivationSut`, `SecureChildShareMirrorTests.MessageAccess`, `SecureChildOwnershipAiToolTests.RefileThreadCaller`). No assertion changed. Without the probe the filing is undecidable, and without the reader nobody can be cleared, so those compositions granted nobody (19 failures) until they matched the host.

**§11:** `RestrictedForGrantAsync` is new surface in an existing class. (1) *Existing:* `EligibleForGrantAsync` and the private `TargetIsRestrictedAsync`. (2) *Extension:* it exposes the private check with the own value included, so the anchor grant can skip the user read on a non-Restricted filing; `EligibleForGrantAsync` reads users first, which would change behaviour for internal users on non-Restricted filings under a read fault. (3) *Cost of doing nothing:* the anchor grant could not keep AC6 exact on non-Restricted filings. The run fields and the result-JSON object extend existing reports.

**Tests:**
- `RestrictedFilingShareBarTests` (through the real re-file step and the real `SecureChildReconciliationJob`, a NEW instance per call = a restart):
  - must-fire: re-file with a user-read fault → nothing removed → restart → the sweep removes it (value already stored, no write); an unread user left at the re-file → restart → removed once it reads external (ruling 3: both "next pass" tests now drive the real job); a message under an already-Restricted record → the external anchor member is not granted (inherited, and own value stamped at create); a legacy external share under an already-Restricted record → removed by the sweep.
  - must-not-fire: internal and blank-flag users keep everything on a Restricted filing (job); a Standard filing grants every anchor member, the external one included, and reads no user (AC6); a Secure message under a Standard root: the bar does not run.
  - fail closed: the filing cannot be read → the external member is not granted, the internal one is.
- `ChildAccessPermissionReconcileTests.Task198`: finding 9 (a parentless message with its own value Restricted → removed by the sweep, nothing written); must-not-fire (parentless Standard keeps the external share); bounds (write cap 2, five Restricted messages: 2 barred, 3 capped and carried with a problem line, then 2, then 1; every share removed; nothing written).
- **Fail-first** (on the tests commit `191a85e7e3`, with the two new call sites disabled: the reconciler's bar selection reverted to "written or carried" and `ClearAnchorGranteesAsync` returning the grantees unchanged): 9 of the 10 new or changed R3 tests failed. These were all 8 must-fire tests (restart after a user-read fault, restart after an unread user, the legacy share removed by the sweep, grant time inherited, grant time own value, grant time undecidable, finding 9, bounds), plus the job's internal-keep test, which also asserts that the bar ran once. The must-not-fire pins passed both ways: AC6 with no user read, a Secure Standard message not barred, and a parentless Standard message kept. All Part D tests passed too. The source was restored to the commit.

**Coordinator review of R3 (2026-11-09).** The bar phase, its order and bounds, and the use of `RestrictedForGrantAsync`/`EligibleForGrantAsync` are approved.
- **R3-1 (F): a user-read fault at the anchor grant loses an internal user's share for good. STOPPED, no durable re-grant path exists.** The ruling was to queue an incomplete `ClearAnchorGranteesAsync` result to the existing path that re-applies the `AnchorRecordMembers` grant, or to stop if none can carry it without new surface. Paths checked:
  - `MembershipReconcileJob` (Service Bus, `sdap-jobs`): its payload is `ThreadId`/`Trigger`/`Actor` and it reconciles ACS chat membership only (`IMembershipReconciler`). It grants no Dataverse share on a message. Using it would need a new payload field and a new grant step. It also dead-letters after max attempts, so it is not "until it lands" (Part C rejected it for the same reason).
  - `CommunicationThreadFilingSweepJob`: registered disabled and dry-run by default. Its candidates are only filed emails whose thread is missing or not anchored to a record they are filed to, so a message already in its record's thread (the case here) is never a candidate.
  - `ThreadResolver` filing reconcile (`MoveToRecordDefaultThreadAsync`, `ResolveMissingThreadAsync`): grants the anchor's members only when a re-file moves the message or a threadless email joins a thread. It is event-driven and is not re-run for a missed grant.
  - `SecureChildShareReconciliationJob`'s Direct pass: Direct-team-owned messages only (`DirectMessageParticipantShares`).
  - `ChildAccessPermissionReconciler` and `RestrictedFilingShareBar`: they only remove shares.
  - The send, ingest and upload callers (`CommunicationService`, `MessagingIngestor`, `EmailUploadCaptureService`) grant once, inline.
  Conclusion: nothing re-applies the anchor grant after the first attempt. Before task 198 this was already true for any failed grant (best-effort, NFR-02); R3 adds a new way to miss one (a user-read fault on a Restricted or undecidable filing). The code is unchanged (still fail closed). Waiting for a ruling.
- **R3-2 (K, accepted): a filed communication stored Restricted while it inherits a lower value is not barred.** The communication reconcile is exact inheritance (round 81, task 173; this behaviour predates task 198). The job rewrites the stored value to the inherited one, after which the row is not Restricted, so there is nothing to bar. "Own value" applies only to a row filed under nothing.
- **R3-3 (K, accepted): rows `left` by a capped bar.** They are counted and logged (NFR-03: `restrictedBar.left`, a Warning, a problem line) and are barred at their next listing (their own change, a parent's change) or by the sweep after a restart.

**Counts** (base `fba722b868` → after):

| Run | Before | After |
|---|---|---|
| Brief filter `DirectMessageOwnerTeam\|DirectThreadAccess\|RestrictedFilingShareBar\|SecureChild\|SecureOwnerTeamNameMatch\|ThreadResolver\|ChildAccessPermission\|DirectMessageParticipantShares` | 2902 (derived: after − 10 new) | 2912/2912 |
| The same plus `MembershipReconcile` (the §7.1/§7.2 filter; the brief's "2916") | 2916 | 2926/2926 |
| `tests/Spaarke.ArchTests` (full) | 2119 | 2119/2119 |
| BFF build | 0 warnings | 0 warnings, 0 errors |

Before the test compositions matched the host, the brief filter showed 19 failures. All were record-anchored grants in hand-built compositions with no probe and no user reader (see ruling 2).

### §7.4 Fix round 1, R2 (the participant source)

Adversarial review pass 1, finding R2 (F, security): the participants were derived from the THREAD's user owner and its POA systemuser sharees, which is not a record of the pair that only the BFF writes. Owner-approved fix: option A, a BFF-stamped recipient column on the message. Coordinator rulings 2026-11-09 (binding).

**Live metadata (read-only, dev):** `sprk_communication.sprk_sentby` (schema `sprk_SentBy`) and `sprk_communication.sprk_directrecipient` (schema `sprk_DirectRecipient`) are both `Lookup` attributes with `Targets = [systemuser]`, requirement level None. The sender premise holds.

**Before:** `DirectMessageParticipantShares.DeriveParticipantsAsync(dataverse, shares, communicationId, threadId, …)` read the thread's `ownerid` and its share list (strict). The reconcile read the thread with `ownerid`.

**After:** the participant pair is `{sprk_sentby, sprk_directrecipient}` read from the MESSAGE row.
- **One rule, `ParticipantsOf(Entity message)`.** Valid only as exactly two distinct, non-empty `systemuser` ids. Anything else is `ParticipantDerivation.None(reason)`: one or both not stamped (absent from the row, null or an empty id), both the same user, or a lookup to another table. There is no fallback to the thread's owner or shares.
- **The reconcile (re-file step and share job)** reads `MessageColumns = [owningteam, sprk_communicationthread, sprk_sentby, sprk_directrecipient]` by name in its one step-1 row read, and applies `ParticipantsOf` to that row. An invalid pair is `Outcome.Settled(NoAudience, reason)`: `Complete = true`, nothing granted, nothing revoked, logged at Information. The job does not carry it or fail the run for it. A later stamp (a repair, fm 054c's backfill) moves `modifiedon`, and the Direct pass lists the message again. A read fault is still `Retry` (status Unknown, incomplete).
- **The live grant** (`DirectThreadAccessService.GrantDirectTeamMessageAsync`) calls `DeriveParticipantsAsync(dataverse, communicationId, logger, ct)`. That reads the same `MessageColumns` and applies the same `ParticipantsOf`. An invalid pair grants nobody (Information). A fault, or a message not found, grants nobody (Warning), and the job retries it.
- **The thread only narrows.** The message must have a thread, the thread must be Direct (`sprk_threadtype` = 100000001), and it must not be the per-user master thread. The thread read now selects `sprk_threadtype` and `sprk_isdefaultthread` only (`ownerid` was removed). The thread's share list is no longer read anywhere for the audience.
- **Disabled participants.** `RestrictedFilingShareBar.EligibleForGrantAsync` has an overload with `excludeDisabled`. It reuses the ONE user read (`RestrictedExternalShareRemover.ReadUsersAsync`, whose `SystemUserSelect` already takes `isdisabled`), so no second client is added. With the flag, a user with `isdisabled = true` is not eligible, and that is settled (`Complete` unchanged). A user whose `isdisabled` did not come back is not eligible and the answer is incomplete (retried). A user-read fault still makes nobody eligible (incomplete). Both Direct callers pass `excludeDisabled: true`. The anchor grant keeps the 3-argument form, unchanged.
- **Revoke** is unchanged: every user other than the pair, on the message. An invalid pair revokes nothing.
- Unchanged: the Restricted bar, Secure handling, the Direct-team check (K3), and R1/R3–R6.

**Rule 7, the pre-move path:** `ReadDirectParticipantsForGrantAsync` (a USER-owned Direct message, before fm 054a: the thread's owner and shares) is master behaviour and is not changed. Recommendation for later: move it to the columns only once fm 054c has backfilled `sprk_directrecipient` on user-owned Direct messages. Until then, switching it would grant nobody on legacy rows. That path also does not run for Direct-team-owned messages.

**§11:** `SentByColumn`, `DirectRecipientColumn`, `MessageColumns`, `ParticipantsOf` and `ParticipantDerivation.None` replace the old derivation's surface, inside the same class. The `EligibleForGrantAsync(…, excludeDisabled, …)` overload is new surface in an existing class. (1) *Existing:* the 3-argument `EligibleForGrantAsync` and its user read, which already selects `isdisabled`. (2) *Extension:* it extends that method with a flag, because changing the default would alter the anchor grant (out of scope). (3) *Cost of doing nothing:* rule 4 cannot be met without a second user read.

**Tests** (`DirectMessageParticipantSharesTests` over the real share job / re-file step; `DirectThreadAccessServiceTests` for the live grant). `UserFlagDirectoryDouble` gained `Disabled`: unlisted users are enabled, `null` omits the column. It also gained `Selects`. The Direct fixtures stamp `Caller → Other`, the same two users as the thread's two-party list, so every earlier test is unchanged.
- **Must-fire (job/re-file):** the stamped pair is granted on the message and content rows, and a third user's share is revoked (real job). The thread's sharee is not granted when outside the pair, and the thread's owner is not either (the old source is gone). The step-1 read selects `owningteam`, the thread, `sprk_sentby` and `sprk_directrecipient`. With no recipient, the real job grants and revokes nothing: `incomplete:0`, `carried:0`, Success. Once stamped and modified, the next run grants the pair. No recipient → `NoAudience`, Complete, detail names the recipient. Invalid pair ×5 (no sender, neither, equal, recipient a contact, sender a team) → NoAudience, Complete, no writes. A pair absent from the row (real job) → no grant, no revoke, the thread's users not granted. A disabled recipient → the sender is granted on every row and the recipient on none; Complete; the user read selects `isdisabled`. `isdisabled` not returned → not granted, incomplete. User-read fault → nothing granted, incomplete. Message-read fault → Unknown, incomplete, no writes.
- **Must-fire (live):** the pair is granted and the thread's sharee is not. The read selects both columns. No recipient → no grant. Sender = recipient → no grant. A disabled recipient → the sender only. Pair-read fault → no grant.
- **Must-not-fire:** a valid pair already holding exactly its shares → zero writes (real job). A non-Direct thread with a valid pair → NoAudience "its thread is not a Direct thread", no writes. The earlier pins (master thread, record-anchored thread, user-owned, Secure) still pass.

**Fail-first** (tests commit `37df738d14`, run against the unchanged source): 20 failed and 2930 passed (scoped filter + `MembershipReconcile`, 2950 total). The 4 new tests that pass both ways are fault/narrowing pins whose outcome the old code shared: a user-read fault, a message-read fault, a live pair-read fault, and a non-Direct thread. An earlier fail-first attempt raced with the first source edit (the reconcile select test passed because `MessageColumns` had already landed). It was discarded and rerun cleanly.

**Counts** (base `740df14540` → after):

| Run | Before | After |
|---|---|---|
| Brief filter (§7.3, without `MembershipReconcile`) | 2912 | 2936/2936 (+24 new) |
| The same plus `MembershipReconcile` | 2926 | 2950/2950 |
| `tests/Spaarke.ArchTests` (full) | 2119 | 2119/2119 |
| BFF build | 0 warnings | 0 warnings, 0 errors |

**Known limits (K):**
- **K-R2-1:** field security on `sprk_sentby`, `sprk_directrecipient` and `sprk_communicationthread` is fm 054b's and is not live yet. Until then, a principal with Write on the message row could change the pair. The Direct team has no members and the participants hold Read only, so the exposure is limited to roles with broad Write on `sprk_communication`. It closes with 054b.
- **K-R2-2:** a participant whose `isdisabled` is not returned is retried. Dataverse always returns the selected non-null column, so this should not occur. If it does, the R4 backoff bounds the retries.
- **K-R2-3:** a disabled participant's EXISTING share is kept: they are in the pair, so it is not revoked. Rule 4 says "not granted", not "revoked". A disabled user cannot sign in.
- **K-R2-4:** the live grant adds one message-row read per Direct-team message (the pair). The ownership read is unchanged.
- **C-c (Part C open) narrows:** a writer who changes a filed message's thread lookup before an unfile can no longer choose the audience. At most they decide whether the message's own stamped pair is granted (a Direct thread) or nobody is (any other thread).

## Open questions

- **Q1:** After an ordinary record → unfiled move (owner back to the Direct team), should anchor-grantee shares be removed so the audience is exactly the participants? No current path removes them.
- **Q3:** The Read depth the internal roles hold on `sprk_communicationattachment` / `sprk_communicationparticipant` / `sprk_document` was not checked live (no live reads in this run). Part B grants the shares regardless.
- **Q6:** resolved. The Secure twin's accent fix and K1 (§6, §6.1) are merged into this branch.
- **Q1 ruling (coordinator, 2026-11-09):** yes. An unfile back to the Direct team revokes the anchor-record grantees (not the participants). Part C.
- **Open (owner):** `BillingFactLoader.SecureRootsAsync:723` and `SignalSuppressionService.ResolveSecureTeamAsync:296` read "no Secure team" as "nothing is secure" when the settings are present but find nothing (§6.1 item 3). Misconfiguration only; reported to the owning sessions.

**Resolved:**
- **Q2:** a step in `AfterRefileCoreAsync` plus the existing job, for every communication (§5).
- **Q4:** yes, by id (§2c).
- **Q5:** `NotConfigured` = not Direct; everything else unknown (§3b).

**Part C open (coordinator):**
- **C-a:** a secure exit to an ORDINARY record (not back to the Direct team) is not re-granted to the participants. Re-deriving them there would trust the message's thread lookup on a row the record team's members can write. Only an unfile or a secure exit back to the Direct team is covered.
- **C-b (rule 6 vs AC6): resolved by coordinator ruling (fix round 1, R3, §7.3).** The earlier text said the record-anchored grant path was left without the Restricted bar for a ruling, and that Part D covered it on filing and on the job's Restricted pass. The second half was wrong: a message created under an already-Restricted record is never re-filed and its value never changes, so neither Part D path reached it (finding 4). Ruling: the record-anchored grant applies the bar AT GRANT TIME (a user flagged external is not granted on a Restricted filing, own or inherited; undecidable: flagged-external not granted, internal granted as before). This is a deliberate narrowing of AC6: the Restricted invariant wins (owner rounds 67/76). Every other record-anchored behaviour is unchanged.
- **C-c (residual):** a writer of a FILED message could change its thread lookup before an unfile; after the unfile, that thread's participants are granted. This sits in fm's owner rule area (who may write a filed message's thread lookup).
- **C-d (throughput):** roughly 6–10 reads per message; at most 200 messages per run (2-minute schedule). A large backlog drains over several runs, each logged.

**Pending upstream:** Part E (Gate Decision Records, ontology-owned).

**R3-1 coordinator ruling (2026-11-09):** K, filed as #2647 and reported to the operator. The record-anchored grant has had no durable retry since before 198 (best-effort, NFR-02); R3's fail-closed user-flag read only adds one more way to lose it. Fail-closed stays. Making the Restricted bar phase grant as well as remove is rejected as new behaviour in a removal-only phase (scope; §11). The retry is #2647's.
