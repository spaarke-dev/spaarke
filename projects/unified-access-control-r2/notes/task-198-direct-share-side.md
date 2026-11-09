# Task 198 — share side of Direct 1:1 messages (notes)

> Task: `tasks/198-direct-message-share-side.poml` (FULL). Branch `task/uac-198-direct-share-side`.
> This run: Steps 0, 1, 2 (Part A), 3 (Part B), 5 (Part D). Part C waits for fm 054a's lookup sha; Part E waits for ontology-r1's OK.
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
- No arch guard pins `direct-message-owner-role.json` (team and role names, Read/Basic, exact tables) the way `RestrictedContentOwnerRoleGuardTests` pins the customer files. The table list is TODO-fm; add the guard when fm names it.
- The customer mode does not know the Direct unit's name, so it would accept the Direct unit as a customer unit (it is a direct child of the root). Refusing it there needs the Direct unit's name in the customer mode (for example, a `-DirectMessageBusinessUnitName` exclusion), which changes the customer mode. That is the owner's call: **Q4**.

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

## §5 Part D — keep on filing (NOT DONE in this run)

- Ordinary target: the census (§1a) and fm's census agree that no path revokes the message or content-row shares, so the rule holds today. The pin test is not written.
- Secure target: the synchronizer decides. The pin test is not written.
- Restricted target: needs new code. `RestrictedExternalShareRemover.RemoveForRecordAsync` (`Infrastructure/ExternalAccess/RestrictedExternalShareRemover.cs:185`) works per ROOT. It takes a per-record lease and does a strict read-back, a cache clear and a `SyncRootAsync` child fan-out (secure only). Extending it to a communication filed under a Restricted root needs these decisions:
  - the hook: `SecureChildReconciler.AfterRefileCoreAsync` 1297-1339, or the remover's 5-minute job;
  - the lease key;
  - the content rows;
  - Direct only, or every communication.
  
  Not guessed. Handed back.

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

## Open questions

- **Q1:** After an ordinary record → unfiled move (owner back to the Direct team), should anchor-grantee shares be removed so the audience is exactly the participants? No current path removes them.
- **Q2:** Restricted target (Part D): extend `RestrictedExternalShareRemover` to communications filed under a Restricted root, or add a step in `SecureChildReconciler.AfterRefileCoreAsync`? Direct messages only, or every communication?
- **Q5:** `NotConfigured` is read as "not Direct". The coordinator's rule (2026-11-09) is that "a Refusal never settles not-Direct". Applied literally, every team-owned message (every record thread) gets no grant in any environment without `DirectMessage:*` settings, because `CannotBeDirectOwnerTeamAsync` answers false when unset. This needs a ruling before fm's fix sha changes the API.
- **Q6:** the Secure twin's accent bug (`SecureChildShareSynchronizer.CannotBeSecureOwnerTeamAsync` compared names in code). Fixed in §6 (branch `task/uac-198-secure-accent`); fm's two matches in `DirectMessageOwnerTeam.cs` are listed there for fm.
- **Q4:** Should the customer-unit mode refuse the Direct-message unit (it is a direct child of the root, so today it passes as a customer top unit)?
- **Q3:** The Read depth the internal roles hold on `sprk_communicationattachment` / `sprk_communicationparticipant` / `sprk_document` was not checked live (no live reads in this run). Part B grants the shares regardless.
