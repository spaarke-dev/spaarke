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

## §3 Part B — participant grants on a Direct-team-owned message

(filled in below as built)

## §5 Part D — keep on filing

(filled in below as built)

## Open questions

- **Q1:** After an ordinary record → unfiled move (owner back to the Direct team), should anchor-grantee shares be removed so the audience is exactly the participants? No current path removes them.
- **Q2:** Restricted target (Part D): extend `RestrictedExternalShareRemover` to communications filed under a Restricted root, or add a step in `SecureChildReconciler.AfterRefileCoreAsync`? Direct messages only, or every communication?
- **Q4:** Should the customer-unit mode refuse the Direct-message unit (it is a direct child of the root, so today it passes as a customer top unit)?
- **Q3:** The Read depth the internal roles hold on `sprk_communicationattachment` / `sprk_communicationparticipant` / `sprk_document` was not checked live (no live reads in this run). Part B grants the shares regardless.
