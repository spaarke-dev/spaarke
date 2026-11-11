# #2179 — Flagged-external systemusers reach no internal-only record, in Dataverse or the SPA (design study, task 191)

> **v2, 2026-11-11.** Code cited at **origin/master `df93d2029c`** (tasks 188, 189, 194b, 198 and ebilling merged).
> Owner D-13 (2026-11-11, "Build in uac-r2 now") reopened the build. v1 (2026-10-21, master `1cc7b39ff3`) recommended a
> per-user child mirror. The owner then chose a different shape (rounds 114–115), approved and ran the T191a spikes
> (round 121), and left two items open (F1, S4). This revision folds those decisions into one design, re-checks the
> inventory against current master and lists what is still open. The v1 decision record is kept verbatim in Appendix A.
> Read-only throughout: no schema, role, field-security, share or setting change was made for this note.
> Status: **design checkpoint** (POML `<design-checkpoint>`). Nothing here is built.

---

## 0. Summary

**The rule (owner, round 113).** A systemuser with `sprk_isexternal = true` reaches no internal-only record, through
Dataverse (Web API, model-driven app, OBO AI tools) or through any BFF, SPA or Teams surface. Internal-only means:
(1) Access Permission Restricted (Limited stays visible); (2) a communication marked internal-only, and the documents
attached to or derived from it (up to 3 hops), plus, by the owner's Q3 answer, every record filed under or about a hidden
record; (3) every Procedure file. Invoice and export files stay reachable by the related external user.

**Why shares alone cannot do it today.** A flagged-external user sits in a customer business unit. Role depth (Deep Read
in the Spaarke roles) and that unit's default owner team (which owns the unit's ordinary rows, I-6) give Read on every
non-secure row in the subtree. Dataverse security is additive, with no deny and no GA row filter (§2), so removing a
share or securing a column cannot hide a row that depth reaches.

**The design (owner rounds 114–115, refined here).**
1. **Isolate the user.** Every flagged-external systemuser lives in one **External Collaborators** business unit that
   owns no rows. They hold one role, **Spaarke External Collaborator**, at Basic depth, with no Share or Assign, and no
   privilege at all on the "never" tables (Procedure, rule, hard-deny and communication-content tables).
2. **Grant by one team share.** A BFF job shares every lineage row that is **not** secure, Restricted or internal-only with
   the External Collaborators unit's **default team**, and withdraws the share the moment a row becomes any of those.
   Secure records keep their per-user shares, filtered by the same rule. The model-driven app and the SPA then agree by
   construction, because the SPA's 036 read set asks Dataverse.
3. **One predicate decides "externally visible".** `ExternalVisibility` composes the rules the SPA already uses (the
   effective Restricted bar, the 065 communication rule, the D7 document narrowing, the multi-root rule, ebilling (a′))
   with a taint closure over the one lineage registry. Each registry table declares its own marker, so a new table cannot
   be added without a decision.
4. **Writes follow.** A hidden row is never shared, so it cannot be written, unlinked or re-parented. What remains is
   lowering a marker on a row the user can still write (spike F1); §3.6 closes it with field security keyed on the
   business unit.
5. **A scheduled BFF job moves a newly flagged user** into the unit within about 2 minutes. It is one-way, acts only on a
   flag read as definitely true, and alerts only on a failed move.

**Still open:** seven owner questions (§4), two of them carried from 2026-10-22 (F1, S4). **Build:** 9 tasks, L/XL,
about 11–15 agent-days including two security review passes (§3.18). **Live steps:** 8, each needing an owner go (§3.19).

---

## 1. Inventory: how a flagged-external systemuser reaches an internal-only record today

### 1.1 What "internal-only" is, per table

| # | Internal-only when | Tables and columns | BFF decision today | Dataverse today |
|---|---|---|---|---|
| 1 | **Access Permission = Restricted** (100000002). Limited stays visible (round 111 item 4). | Roots `sprk_matter`, `sprk_project`, `sprk_workassignment`: `sprk_accesspermission` (unsecured; `Set-SecureFlagFieldSecurity.ps1:47` leaves it editable by Write holders, an owner-accepted choice). Filed project or work assignment: stored max(own, floor) with `sprk_accessinheritance` (I-17). Children (`sprk_todo`, `sprk_event`, `sprk_communication`, `sprk_document`) carry an I-16 copy. | Root veto `InternalShareEndpoints.IsBarredOnRestricted` (`InternalShareEndpoints.cs:1427`), applied to the 036 read set (`AccessibleRecordSetService.cs:696`). Effective through filing: `EffectiveRootFlags.RestrictedThroughFilingAsync` (`EffectiveRootFlags.cs:164`). | Only direct POA shares are barred (I-14, write-path `:145`). Depth still reads a non-secure Restricted root and every child. |
| 1b | A **document's own** Restricted, or a Restricted ancestor on its `sprk_parentdocument` chain | `sprk_document.sprk_accesspermission` + `sprk_accessinheritance`, stored = max(own, inherited) (task 189, both unsecured by design, `task-189-own-narrowing.md` §2.4) | `ExternalDocumentNarrowing` (`ExternalDocumentNarrowing.cs:64`, `MaxHops = 3` at `:91`) | none |
| 2 | **Internal-only communication** under the 065 rule: `sprk_isinternalonly` reads false AND a thread is set AND that thread is Open now. True, blank, no thread, a Private thread or an unreadable value all hide. | `sprk_communication.sprk_isinternalonly`; `sprk_communicationthread.sprk_privacystate`. Writers: outbound create writes it explicitly (`CommunicationService.cs:1140-1171`), a Private or unreadable thread raises it (`:1267-1284`), posts are never internal-only (`:2051`), the only lowering is `CommunicationInternalOnlyFloor.LowerWhenSafeAsync` (`:131`). | `CommunicationOpenThreadGate.ReadVisibleCommunicationIdsAsync` (`:132`); AI tools `ExternalCallerCommunicationGuard` (refused content tables `:83`) | none: any Read holder reads the row and its body |
| 2b | **Derived documents**: attached to or derived from a hidden communication, through `sprk_relatedcommunication` and `sprk_parentdocument`, up to 3 hops | `sprk_document` link columns: field-secured by task 188 (`Set-DocumentNarrowingFieldSecurity.ps1`; only the BFF app users write them) | `ExternalDocumentNarrowing` (rule 3 walks the chain) | none for Read |
| 2c | **Records about a hidden record** (owner Q3, round 114): to-dos, events, memos, analyses, signals, decision records whose regarding lookup names a hidden communication or document; an event built from an email also copies its subject, sender and recipients | Lineage lookups in `SecureChildLineage.Children` (`SecureChildLineage.cs:179-343`) | AI tools: `ExternalCallerCommunicationGuard.DocumentDerivedTables` (`:141`) for some tables. The SPA's to-do and event lists apply only the multi-root rule and the live-parent check (#2189). | none |
| 3 | **Procedure files**, always | New model: `sprk_procedure`, `sprk_procedureversion`, `sprk_storedfile` rows under a procedure version (on master: `ProcedureService`, `StoredFileService`; neither table is in the lineage registry). Legacy: `sprk_document` rows in the Procedure Library container. | ebilling F1-e: the file route refuses flagged-external callers for Procedure files. `ProcedureRolePrivilegeGuardTests` pins the role privileges. | Whatever role privilege a user holds on those tables. ebilling 088 and round 131 already require none for the external role. |
| — | **Not internal-only:** an invoice and export of the related firm, unless ebilling (a′) hides it (its Submit Invoice source is internal-only or Restricted, round 116); spend and budget columns (owner 2026-10-22) | `sprk_invoice` family (registry `:276-301`), `sprk_storedfile` under an invoice or export | SPA firm route: 036 read set → I-14 veto → task 187 rule → (a′) → file gate. Round 116 F1-4: a flagged-external systemuser sees only invoices of their linked contact's firm. | Depth reaches every invoice in the subtree |

### 1.2 Access paths

Each row names the mechanism and how it reaches each category: **R** = Restricted, **C** = internal-only communication
and its derived or "about" records, **P** = Procedure files.

| Path | Mechanism | Evidence | R | C | P |
|---|---|---|---|---|---|
| **Role depth in the user's BU** | `Spaarke Basic User`: Read Deep on matter, project, work assignment, document, event, to-do, invoice, memo; Read Basic on communication and thread. `Spaarke Core User`: full CRUD + Share + Assign at Deep on every core table. `Spaarke Office Add In User`: Write, Assign at Deep on matter, project, work assignment, event, invoice, memo; Write Deep on document. Deep is anchored at the user's BU and reaches every descendant BU (task 062 finding A). | `src/dataverse/solutions/SpaarkeMaster/Roles/*.xml` | **Yes**, every non-secure Restricted root and its children | **Yes** for derived documents, to-dos, events and memos. Communications themselves only through Core User (Deep) or the paths below. | Only if a held role grants a Procedure or Stored File privilege (none of these three do) |
| **Matrix role assignment** | A role can be assigned from any BU (`EnableOwnershipAcrossBusinessUnits = true`, I-18). A Deep role assigned from an internal BU re-opens that subtree wherever the user sits. | I-18 | Yes, if present | Yes, if present | as above |
| **Default owner team** | Ordinary rows are owned by the BU's default team (I-6; `RecordOwnershipResolver.UnfiledOwnership` `:194-210`, default `ActingUserTeam` `:321`). Every BU user is a member, and team ownership gives access "at any access level ... regardless of the business unit". | write-path I-6; Learn *how-record-access-determined* | **Yes, even at Basic depth** | **Yes**, communications included | No |
| **Root POA shares** | `/share-user`, Assigned-To materializer (I-12). Both refuse a flagged user on a Restricted root (`ClassifyEligibility` `InternalShareEndpoints.cs:1404`); the remover takes away out-of-band shares within 5 minutes (`AssignedAccessReconciliationJob.cs:84`). Allowed on secure non-Restricted roots (round 78). | I-12, I-14 | No (≤ 5-min window for an out-of-band share) | Root only | No |
| **Secure-child mirror** | `SecureChildShareSynchronizer` gives every Secure-team-owned child its root's sharees (2-min job `SecureChildShareReconciliationJob` plus inline). It bars flagged users only when the filed secure root is Restricted (`:148`, `:161`). | I-2, I-13 | No | **Yes**: on a secure, non-Restricted root shared with a flagged user, every child is mirrored, internal-only communications and their documents included | No (no Procedure table is a lineage child) |
| **Filed-communication bar** | `RestrictedFilingShareBar` (`:49`) removes flagged users' shares on a communication filed under a Restricted root. | — | Closes it for communications | No: it checks Restricted only | No |
| **Direct-message participant shares** (task 198) | `DirectMessageParticipantShares` (`:54`) grants Read to both participants on a Direct-team-owned message and its content rows (`:618`). Eligibility checks only the Restricted bar. | task 198 | Barred | **Yes**, if a Direct message to a flagged participant is or becomes internal-only (thread made Private, flag set on the form) | No |
| **Gate decision records** | Shared to internal participants only (`GateDecisionRecordSharing`) | round 102 #10 | No | No | No |
| **Ownership** | A row a flagged user creates as themselves (model-driven app, Web API, a run-as-user write) is theirs until re-owned. A Restricted root owned by such a user is reported `owner-is-external`, never re-owned (task 114 decision 5). | I-6, I-14 | **Yes** | **Yes** | No |
| **Hierarchy security** | Widens: a manager reaches rows owned by or shared to direct reports. | Learn | Only where the setting is on and a flagged user is a manager | same | No |
| **Field security reader profiles** | Column read on secured columns only; never row access. | task 133, 188, ebilling 088 | No | No | No (088: the external unit's default team must never hold the Stored File reader profile, `Deploy-EbillingStoredFileSchema.ps1:540`) |
| **OBO AI tools** | `read_query`, `search_data`, `describe` run OBO and then narrow (tasks 078, 188). `grid_overview` runs saved FetchXML OBO with no narrowing (`GridOverviewHandler.cs:45`, #2125). Write tools are governed by Dataverse (owner 2026-10-20 on #1864). | — | Through depth | `grid_overview`: **yes**. Others: narrowed by the BFF. | No |
| **App-only BFF surfaces** | RAG retrieval and chat archive ingest (task 188 slices A, B; #2127 closed); the 036 read set; Insights, Workspace AI and scorecard (Appendix A, "Carried-in question": no new gate). Semantic search over an invoice's associated documents does not yet apply ebilling (a′) for flagged callers (#2369 part 2). | — | Narrowed | Narrowed, except the #2369 case | Refused by the file route |
| **SharePoint Embedded** | Business-unit containers hold no flagged standing member (`SpeContainerMembershipSync.cs:227-237`). On a **secure** container, Office edit-open grants a just-in-time writer role on the **whole container** to any caller with Write on the record; a flagged caller is refused only when the record is Restricted (`OfficeEditAccessService.cs:121-136`). | #2201 | No | **Yes**: container membership opens every file in the family, outside every Dataverse and BFF check | No (Procedure files are in the ebilling blob store, not SPE) |

**Who writes `sprk_isexternal`.** Only operators: `scripts/Set-ExternalFlagForB2BGuests.ps1` (flags outsider `#EXT#`
guests; `-CustomerTenantIds` keeps Model-1 customer staff internal) and administrators on the user form. No BFF code
writes it. It must stay unsecured: a masked value reads blank, and blank means internal (task 114 decision 6). No
Spaarke role a flagged user should hold grants `prvWriteUser`; `Spaarke AI Analysis Admin` does (Global), so the census
must check it.

### 1.3 Write paths that lower, unlink or re-parent today

| Change | Path | Guard today |
|---|---|---|
| Root or child `sprk_accesspermission` lowered | Form (TrackingFieldTrio), grid edit, Web API, OBO write tools. Needs Write on the row: Core User and Office Add In User give it at Deep. BFF writes made app-only are refused by `ParentLineage.RefuseAppOnlyWrite` (`ParentLineage.cs:1199`); caller-identity writes are left to Dataverse privileges and field security. | Unsecured. A filed work assignment or project below its floor is put back within 5 minutes (I-17); a document edited down to its floor counts as "own = none" (189 rule 5). A parentless root is not put back. |
| `sprk_isinternalonly` true → false | Form, Web API | Unsecured; no BFF update route |
| Thread Private → Open | Thread form, Web API | Unsecured |
| Unlink a document from its communication, or clear `sprk_parentdocument` | Web API only (not on a form) | **Field security** (task 188). Spike S1 confirmed both refused. |
| Re-parent out of a Restricted or internal-only ancestor | Lookup edit, Web API; BFF re-file routes check AppendTo **as the caller** | Depth passes the check today |

---

## 2. Dataverse mechanisms available without plugins

Platform facts: security is additive ("another role can't hide a specific contact from the user"); access needs the
privilege **and** an access path (ownership, team ownership, depth, share, hierarchy); team ownership grants access
regardless of business unit; matrix BUs are on. Every mechanism below is declarative or a BFF job (ADR-002, WP-1).

| Mechanism | What it enforces | Cost | What it breaks | Verdict |
|---|---|---|---|---|
| **Dedicated BU for flagged users, Basic-only role** | No depth and no default-team access to customer rows; only shares reach them. Spikes S1 and S2: reads were exactly the shared rows; the move strips direct roles and swaps the default team. | One BU, one role, a move job | A matrix role from an internal BU silently re-opens depth (census). The move removes every role, including Office and Console roles, unless the external role carries what they need (§3.2). | **Chosen** (round 115) |
| **Team ownership of internal-only rows** (park them where external depth cannot reach) | Exclusion by construction | A re-own engine over every lineage table on every transition | Fights I-6; flip-flop ownership; a same-BU user still reads everything else | Rejected (v1) |
| **Share to one External team, withdraw when internal-only** | Exactly the owner's visibility target, row by row, from the SPA's predicate | ≈ one POA row per visible row (dev: about 1,100). S3: 12.7 grants/s, revoke similar. | Fails open for the job window on out-of-band transitions; new rows are invisible to external users until shared | **Chosen** (round 115) |
| **Per-user child mirror** (v1) | Same rule, per user | POA = users × rows | Needs the "would lose" migration list; superseded | Replaced by the team share |
| **Per-firm access teams** | Firm-level row visibility (invoices) | One team per firm, membership from the linked contact | More moving parts | Only if Q6 = (b) |
| **Field-level security** | Protects columns from writers; masks readers | A profile per BU default team, re-run when a BU is added | Cannot hide a row ("column-level security doesn't grant access to a record"); a masked column reads blank | Used only for writer protection (§3.6, F1) |
| **Hierarchy security** | Widens only | — | Leak vector if on | Census: off, or no flagged manager. Off on dev. |
| **Filtered record ownership** | Row filters per privilege | — | Public preview (Oct 2026), union-only, cannot say "shared AND not internal-only" | Watch item |
| **Conditional Access blocking Dataverse** for a flagged group | Blocks the model-driven app, Web API and OBO; app-only BFF calls unaffected | Entra P1; a tenant-config write; group sync | Users become SPA-only, which conflicts with "licensed"; every BFF surface must narrow forever | Rejected (round 115 chose A) |
| **Access-team templates** | Per-record team with a template mask | One team per record | POA and team rows per record; no gain over one owner-less team for an org-wide audience | Not used |
| **Share revocation by BFF jobs** | The withdraw side of the team share | Existing jobs extended | Window ≤ 2 min out of product | Used |

**Model 1 and round 78.** On a Model-1 stamp, the customer's licensed staff are B2B guests too, so `#EXT#` is **not** the
external signal; only `sprk_isexternal` is (the flag script keeps `-CustomerTenantIds` users internal). A flagged user
may be licensed outside counsel who must still reach **secure, non-Restricted** records shared with them (round 78). The
design keeps that: per-user shares on secure roots stay, filtered by the predicate. Nothing in the design keys on `#EXT#`,
UPN shape or licence.

---

## 3. Recommendation

### 3.1 Shape

```
flagged systemuser ──► BU "External Collaborators" (owns 0 rows) ──► role "Spaarke External Collaborator" (Basic; no Share/Assign)
       │                       └─ default team "External Collaborators" (members = the BU's users, automatically)
       ├─ team share   ◄── job: every lineage row with ExternalVisibility = visible AND not secure  → grant; otherwise revoke
       ├─ secure roots ◄── per-user shares (round 78), I-14 bar unchanged
       └─ secure children, Direct messages ◄── existing per-user writers, now filtered by ExternalVisibility for flagged principals
ExternalVisibility = ¬Restricted(effective) ∧ 065 rule ∧ D7 document rule ∧ ebilling (a′) ∧ own marker ∧ taint closure (registry)
SPA/Teams (036 read set asks Dataverse) and AI tools: same Dataverse answer + the same predicate where they read app-only
```

### 3.2 Placement: business unit, team and role

- **BU `External Collaborators`**: a child of the root BU, holding no rows and no container. Its default team is the
  share target. It receives **only** the document-link and secure-flag **reader** column profiles (defence in depth for
  forms), never a writer profile and never ebilling's Stored File profile.
- **Role `Spaarke External Collaborator`**, assigned directly to each user **from the External BU**:
  - Basic Read, Write, Append, AppendTo and Create on the lineage tables partners use (S1 used 30 privileges on matter,
    project, document, communication, event and to-do); Basic Read on the other visible lineage tables (spend tables
    included: owner 2026-10-22);
  - `prvReadUser` at Local (not Basic-capable), no `prvWriteUser`, `prvRead/WriteUserSettings` at Basic;
  - **no privilege at all** on: `sprk_procedure`, `sprk_procedureversion`, `sprk_procedureassignment`, `sprk_storedfile`,
    `sprk_ruleset`, `sprk_policy`, `sprk_policyversion` (ebilling 088, D-185, round 131), the communication-content tables
    (`sprk_emailartifact`, `sprk_attachmentartifact`, `sprk_emailreviewlog`, `sprk_affinity`), and the invoice family if
    Q6 = (a);
  - no Share, no Assign, no Delete beyond Basic, no Global or Deep on any lineage table;
  - the Console and Office add-in entry privileges those surfaces need (S1 lists them; the move strips the old roles).
- **The user's other roles are removed** by the move. The census flags any role later assigned from another BU.
- **Unfiled creates by a flagged user.** `RecordOwnershipResolver`'s `ActingUserTeam` would hand the row to the External
  BU's default team, which every flagged user is a member of. For a flagged acting user it answers the customer BU's
  default team (`ExternalAccess:CustomerBusinessUnitId`) instead, and the team share then applies the predicate. The
  External BU's default team must never own a row (census).

### 3.3 The predicate and the registry

- **Registry.** `SecureChildLineage.Table` (`SecureChildLineage.cs:58`) gains a required `ExternalMarker`, so every
  table declares one at compile time:
  - `None`: only inherited taint applies;
  - `Predicate(...)`: the own-row rule (communication: 065; thread: Open; document: task 188's narrowing; invoice:
    ebilling (a′); legacy Procedure Library document: Never);
  - `Never`: never externally visible.
  - Tables outside the registry (procedure tables, `sprk_storedfile`) are kept out at the role level (no privilege) and
    are never team-shared, because the job walks only the registry.
- **Derived lists fold in.** `ExternalCallerCommunicationGuard.RefusedContentTables` (`:83`) and `DocumentDerivedTables`
  (`:141`) are computed from the registry, so there is one map.
- **`ExternalVisibility.DecideAsync(rows)`**, one class in `Infrastructure/ExternalAccess`, app-only reads, batched like
  task 188. A row is visible only when all hold:
  1. no linked root is Restricted, effective through filing (`EffectiveRootFlags`); a child's I-16 copy is never trusted;
  2. its own marker passes;
  3. **taint closure** (owner Q3): no lineage parent through any registry lookup is hidden by 1 or 2. It is bounded like
     the lineage walk, keeps task 188's 3 hops on the document chain, and treats a cycle, a too-deep chain or an
     unreadable value as hidden.
  The multi-root rule (`ExternalMultiRootRule`) stays where it is, on the per-user side (secure roots). The team share
  never touches a secure row.
- **Callers**: the team-share job, the secure-child mirror and the Direct-message writer for flagged principals, task
  188's surfaces (unchanged result), the SPA to-do, event and memo lists, the AI tools' derived-table filter, and
  `RetrievalAccessTrim`'s external narrowing (closes #2369 part 2).

### 3.4 The team-share job (Read and Write)

- **Placement.** An external-team mode of `SecureChildShareSynchronizer`, run by the existing
  `SecureChildShareReconciliationJob` (2 min) and the recent-changes pass of `SecureChildReconciliationJob`. No new job,
  no new DI surface beyond the predicate.
- **Scope.** Rows of the three roots and every registry table that are **not** owned by the Secure Record team.
- **Grant** the External team's share when the predicate says visible. Mask: **Read + AppendTo** (Q3 recommendation),
  so partners see the row and can file their own new to-dos, documents and events under it. Write comes only from a
  per-user share (Manage Access level, Assigned-To) on a root.
- **Revoke** when the predicate hides the row, or the row turns secure (the secure path takes over). It only ever touches
  POA rows whose principal is that one team.
- **Per-user shares to flagged users** on rows the predicate hides are revoked too, whatever made them (form Share,
  old mirror). A hand-made share is not restored when the row becomes visible again; the team share restores Read.
- **Writers that name a user on a child** (secure mirror, `DirectMessageParticipantShares`, `GateDecisionRecordSharing`,
  the Assigned-To materializer) call the predicate for a flagged principal before granting.
- **Re-own.** A lineage row owned by a flagged user is re-owned through `RecordOwnershipResolver` to its I-6 team and read
  back (S2 finding F4: a BU move does not move owned rows). Mailboxes, queues and other system rows are out of scope.

### 3.5 Secure records (round 78)

Secure roots stay per-user: a flagged user may be shared on a secure, non-Restricted root. The secure-child mirror
gives them only the children the predicate allows, and revokes the rest. A Restricted secure root stays closed (I-14).

### 3.6 Write: no lowering, no unlinking, no re-parenting

| Change | How it is refused |
|---|---|
| Write, unlink or re-parent a **hidden** row | No share → no access. Dataverse refuses (S1). |
| Re-parent a visible row under a hidden or Restricted target | No AppendTo on the target → refused (S1) |
| Unlink a document from its communication, clear its parent document | Task 188 field security (S1), unchanged |
| **Lower `sprk_accesspermission`, clear `sprk_isinternalonly`, open a Private thread** on a row the user can still Write (a Write-shared root; or the ≤ 2-min window after a row turned internal-only out of product) | **F1, open (Q1).** Recommended: field security on those three columns. Read profile for every BU default team including External; **Update only for the internal BU default teams and the two BFF app users**. Internal users keep editing; flagged users cannot lower. BFF app-only writes of these columns are already refused (`ParentLineage.RefuseAppOnlyWrite`); caller-identity writes then hit field security. |

Why this is safe for task 189 (§2.4 there): the BFF app users are explicit readers and writers, so the BFF never reads a
masked value; internal editors are unchanged, so 189's own-narrowing classification still sees their edits. It revisits
the owner-accepted "stays editable by Write holders" (`Set-SecureFlagFieldSecurity.ps1:47`) for flagged users only.

### 3.7 Transitions: revoke, regrant, and the window

| Transition | Made by | Effect | Window |
|---|---|---|---|
| Root becomes Restricted | Form → post-save sync; Update Access | `RestrictedExternalShareRemover` removes per-user shares (today) and calls the job for the root and its descendants: team share revoked | Inline; L4 net 5 min |
| Root stops being Restricted | same | Team share regranted on the root and visible descendants; per-user shares follow existing rules | Inline / ≤ 5 min |
| Inherited Restricted (I-16, I-17) | Parent change | The predicate reads the chain, so revocation does not wait for the stored copy | ≤ 2 min |
| Document own Restricted (189) | `ChildAccessPermissionReconciler`, `CoreAncestorResolver.RefreshInheritedAccessPermissionAsync` | Both call the job for that row | Inline on a BFF write; ≤ 2 min otherwise |
| Communication marked internal-only; thread made Private | Form or Web API only | Recent-changes sees the communication or thread, then walks down to derived documents and to records about it | **≤ 2 min + run** (owner Q4, accepted) |
| Communication lowered (`LowerWhenSafeAsync`) | BFF | Regrant after the lowering | Inline |
| Re-link or re-file | `SecureChildReconciler.AfterRefileAsync`; out of product | Job inline; recent-changes | Inline; ≤ 2 min |
| New ordinary row | Any | Hidden until shared (fail closed); BFF-created rows get the share inline | ≤ 2 min (usability, not security) |
| Row turns secure / leaves secure | Make Secure / Unsecure | Team share revoked / regranted; the secure mirror takes over / lets go | Inline |
| User flagged | Operator | Placement job moves the user, assigns the role | ≤ 2 min + run |
| User un-flagged | Operator | One-way (round 115): the user stays in the External BU until an operator moves them back | Operator step |

A catch-up sweep (each instance start, then hourly by default) walks every lineage row against the predicate, with the
existing r1c cursor and watermark rules (ADR-052 §5).

### 3.8 Fail-closed behaviour

| Fault | Result |
|---|---|
| The predicate cannot decide a row (unreadable flag, thread, chain; cycle; too deep) | No grant; an existing team or flagged-user share is revoked; the row is carried and retried; ids and counts logged |
| A principal's `sprk_isexternal` is unreadable | Treated as external for grants (no child grant); never moved by the placement job (it moves only on a definite true) |
| A share read or write fails | Nothing written for that row; carried |
| Job down or behind | No new grants. Out-of-product revocations wait (fail open within the window); heartbeat and census report it |
| Placement drift (a flagged user outside the External BU; a role from another BU; non-Basic depth; owner of a lineage row; membership of another owner team; hierarchy manager) | Census CRITICAL every 15 min. BFF surfaces still narrow by flag. |
| A masked secured column | The predicate reads app-only, so a mask never reads as "visible" |

### 3.9 Task 189 and the 065 rule

- **189.** The predicate reads a document's stored max(own, inherited) **and** the live chain, so a lagging copy is never a
  hole. 189's "edit to the floor is an un-narrowing" stays correct for internal editors; a flagged user can no longer make
  that edit (hidden rows are unshared; F1 covers visible ones). No change to `DecideDocument`.
- **065.** Reused through `CommunicationOpenThreadGate` (`:132`); its writers stay where they are. The rule is not copied.
- **No new column.** The decision is materialised as the presence or absence of the team share. Nothing needs a
  backfill and a missing share fails closed. D7's "derived at read time, one owner" holds: the share caches the predicate.

### 3.10 Events and to-dos

Under Q3 (decided) a to-do, event or memo regarding a hidden communication or document is hidden, in Dataverse (no team
share) and in the SPA (its lists call the predicate, T191h). An event built from an internal-only email carries the
email's subject, sender and recipients; the taint rule hides it with the email. A to-do or event under a Restricted root
is hidden on both planes. Their own `sprk_accesspermission` copy is never trusted.

### 3.11 Invoices, export files and Procedure files

- **Procedure files**: the role holds no privilege on the procedure tables or `sprk_storedfile`; a legacy Procedure
  Library document has marker Never; the ebilling file route keeps refusing (F1-e). Extracts in rules or decision
  records keep their own rules.
- **Invoices and exports**: Q6. Recommended (a): no privilege on the invoice family in the external role, so a flagged
  user's invoices are SPA-only, through the firm route that already applies 187, (a′) and the firm rule (round 116
  F1-4). This matches ebilling 088's "no Stored File privilege", so invoice files were already SPA-only.

### 3.12 SharePoint Embedded (#2201, spike S4)

Office edit-open must grant no container role to a flagged caller (Q2). Recommended: view and download through the
BFF broker only. `SpeContainerMembershipSync` removes any existing just-in-time grant held by a flagged user.

### 3.13 The placement job (owner Q5, round 115)

- A scheduled BFF job (ADR-036 `IScheduledJob`, existing scheduler, every 2 minutes) reads systemusers with
  `sprk_isexternal = true` (definite true only) outside the External BU, skips application users, disabled users and
  System Administrators, moves each (PATCH `businessunitid`), re-assigns the external role, reads both back, and logs
  ids and counts. A failed move raises an alert; nothing else alerts.
- Writes sit behind a setting, off by default; the owner turns it on per environment.
- The BFF app user already holds System Administrator (`SPAARKE-ENVIRONMENT-RESOURCE-INVENTORY.md`), so no new right.
  This is security-sensitive identity code: two adversarial passes (CLAUDE.md §8.5).

### 3.14 Migration of existing shares and users (dev first; every write owner-approved)

1. **Read-only inventory** (ids and counts): flagged users, their BU, roles and the BU each was assigned from, direct POA
   shares, owned rows, team memberships, hierarchy setting.
2. Create the BU (data) and import the role (solution).
3. Team-share **dry run**: rows it would share and rows it would refuse because they are internal-only, per table.
4. Turn team-share writes on (harmless while depth still covers the rows). Count POA with mask > 0 only (S3 F2: a revoke
   leaves the row at mask 0).
5. Field security for F1 (if Q1 = a): dry run, apply, verify.
6. Turn the placement job on; verify each user's BU, role and default team.
7. Re-own lineage rows owned by flagged users; reassign a Restricted root with a flagged owner (reverses task 114
   decision 5 for this case only).
8. First sweep revokes internal-only per-user shares left by the secure mirror and Direct messages.
9. Live gate (AC-1 to AC-9).
10. Every other environment: read hierarchy, matrix and role holdings before step 2.

### 3.15 Enforced by

| Rule | Mechanism |
|---|---|
| Every lineage table declares its external marker | **Type**: required `ExternalMarker` on `SecureChildLineage.Table` |
| One predicate; no second table map | **ArchTest**: `RefusedContentTables` / `DocumentDerivedTables` computed from the registry; no other class maps tables to internal-only (extends `ExternalDocumentNarrowingGuardTests`) |
| Every POA grant that can name the External team or a flagged user on a lineage row passes the predicate | **ArchTest**: allow-list of `Grant*`/`Modify*` callers on lineage tables, each calling `ExternalVisibility` |
| The role holds no privilege on the "never" tables, Basic only elsewhere, no Share/Assign | **ArchTest** over the role XML (pattern: `ProcedureRolePrivilegeGuardTests`) plus a `-Verify` against the live role |
| Placement, depth, team, ownership, hierarchy, matrix roles | **Census job** (`SecureRecordIsolationCensusJob`, 15 min, CRITICAL per finding) |
| Flagged users cannot lower the markers | **Field security** (`-Verify` mode of the profile script) |
| Office edit grants no container role to a flagged caller | Unit and route tests in `OfficeEditAccessService` |
| End to end | Live gate in the deployment guide, must-fire and must-not-fire |
| The out-of-product window (≤ 2 min) | **Prose** in I-14: cannot be mechanised without plugins (ADR-002) |

### 3.16 CLAUDE.md §11 answers (new surface only)

| New | 1 Existing | 2 Extension | 3 Cost of doing nothing |
|---|---|---|---|
| Role **Spaarke External Collaborator** | Basic User, Core User, Office Add In User (Deep) | Lowering their depth breaks every internal user; no existing role is Basic-only with "never" tables | Depth gives flagged users every non-secure Restricted and internal-only row in their subtree |
| BU **External Collaborators** (data) | Customer BUs; the Secure Record BU (no users, I-2) | No existing BU holds users and owns no rows | Default-team ownership gives every BU member access even at Basic |
| External team mode of the share synchronizer | `SecureChildShareSynchronizer` (secure children) | Extended: same POA seam and jobs, one more principal | After the move, flagged users would see nothing but their per-user roots |
| `ExternalVisibility` | `ExternalDocumentNarrowing`, the 065 gate, `EffectiveRootFlags`, ebilling (a′) | Composes them; it spans every registry table, so it cannot live in any one of them | Each surface re-derives the rule and the copies drift (D7 §6.2; #2189) |
| `ExternalMarker` on the registry | `RefusedContentTables`, `DocumentDerivedTables` | Folds them in | A new table needs registering in several places, and one gets missed |
| Placement job | `Set-ExternalFlagForB2BGuests.ps1` (sets the flag only) | A scheduled job on the existing scheduler; the script cannot run unattended | A newly flagged user keeps depth access until someone moves them |
| Census checks | `SecureRecordIsolationCensusJob` | Extended; no new job | Placement drift goes undetected |
| `RecordOwnershipResolver`: customer-BU team for a flagged acting user's unfiled create | `UnfiledOwnership` (`:194-210`) | Modification | The External team would own the row, and every flagged user of every firm would read it whatever the predicate says |
| Field-security target for the three marker columns (Q1 = a) | Task 133 engine, `Set-DocumentNarrowingFieldSecurity.ps1` | A new target in the existing engine and script | A flagged user with Write lowers Restricted or reopens a communication (S1 F1) |
| Setting: placement writes on/off | Existing reconciliation write switches | Same pattern | An unreviewed environment would start moving users on deploy |

**ADRs.** ADR-002: no plugins; jobs and inline L1 only. ADR-036/052: existing scheduler and jobs. ADR-028: no OBO or
identity-model change; the BFF app user's existing rights. Round 77's rejection of a separate BU is reversed by round 115
(an owner decision, not an ADR conflict). §6.5 path: C (comply) for all of them.

### 3.17 Acceptance criteria (closed set)

| # | Criterion | Must fire | Must not fire |
|---|---|---|---|
| AC-1 | A flagged test user's Web API GET on each kind returns 403 or no row: a Restricted root, a child of one, an internal-only communication, a document derived from it (1 and 3 hops), a to-do and an event regarding it, a communication in a Private thread, a Procedure version and its Stored File, a legacy Procedure Library document | each kind | an unflagged user with the same shares reads each |
| AC-2 | The same user reads every ordinary row of a visible matter (root, documents, events, to-dos, memos, spend rows) | — | read succeeds |
| AC-3 | The same user cannot lower `sprk_accesspermission`, clear `sprk_isinternalonly`, set a thread Open, clear `sprk_relatedcommunication` or `sprk_parentdocument`, or re-file a row out of a Restricted ancestor or under a hidden target (form, Web API, OBO write tool, BFF re-file route) | each refused | an internal user with Write succeeds |
| AC-4 | A row made internal-only out of product loses the team share within 2 min + run; made internal-only through the BFF, inline | revoked | an unrelated visible row keeps it |
| AC-5 | A row that stops being internal-only gets the team share back within the same window | regranted | — |
| AC-6 | An undecidable row gets no share and is carried | no grant | — |
| AC-7 | A newly flagged user is moved and gets only the external role within 2 min + run; an unflagged, blank-flag, disabled, application or administrator user is never moved | moved | not moved |
| AC-8 | Office edit-open by a flagged caller on a secure record grants no container role | refused / view-only | internal caller still gets the JIT writer |
| AC-9 | Census raises CRITICAL for each drift kind in §3.8 | each | clean environment: zero findings |
| AC-10 | `grid_overview`, `read_query` and Dataverse search as the flagged user show only visible rows (#2125, #2369 part 1); semantic search over an invoice's documents applies (a′) (#2369 part 2) | hidden | visible rows shown |
| AC-11 | BFF build 0 errors; targeted and full ArchTests green; publish-size delta measured against a fresh master build; deployed to dev via R3 with a server-side live gate | — | — |
| AC-12 | I-14, I-16, `.claude/constraints/auth.md` (:97 known limit removed, Enforced by named) and the deployment guide updated; #1864, #2125, #2179, #2201, #2369 closed or re-scoped; #2189 answered | — | — |

### 3.18 Build size

| Id | Scope | Size |
|---|---|---|
| T191b | `ExternalMarker` on the registry; `ExternalVisibility` (taint closure, (a′)); fold the two guard lists; unit tests must-fire/must-not-fire; ArchTests | M |
| T191c | External-team mode of the synchronizer: grant/revoke, L1 hooks (remover, `AfterRefileAsync`, 189's two writers, `LowerWhenSafeAsync`, Make Secure/Unsecure), recent-changes reverse walk, sweep; predicate in the secure mirror, Direct-message and gate-record writers; re-own of flagged-owned rows; resolver change | L |
| T191d | Role XML + role ArchTest + BU script (`-Apply/-Verify`) + reader-profile re-run | M |
| T191e | Placement job (setting-gated, one-way, read-back, alert) + census extension | M |
| T191f | F1 field-security target (if Q1 = a): engine target, script `-Verify`, tests | S |
| T191g | `OfficeEditAccessService`: no container role for flagged callers; membership-sync cleanup | S |
| T191h | SPA to-do/event/memo lists, AI derived-table filter and `RetrievalAccessTrim` call the predicate (Q3 parity, #2189, #2369) | M |
| T191i | Dev migration (§3.14) and live gate | M |
| T191j | Docs and issues (I-14, I-16, auth.md, deployment guide; close or re-scope issues) | S |

Overall **L/XL: about 11–15 agent-days** including two adversarial passes (security, tenant isolation). T191b, c, e, g
and h touch the BFF and carry the §10 publish-size delta.

### 3.19 Live and owner-go steps

1. Create the External Collaborators BU on dev (data write).
2. Import the role (solution import).
3. Reader column profiles on the new BU's default team (`-Apply` re-runs); never the Stored File profile.
4. F1 field security apply (if Q1 = a).
5. BFF deploy via R3 with every new write switch off; dry-run reports.
6. Turn the team-share writes on.
7. Turn the placement job on (moves users, strips and re-assigns roles); re-own flagged-owned rows.
8. Live gate with a test flagged user (writes as that user, undone afterwards); then the same for each customer stamp.

---

## 4. Owner questions

1. **F1: may a flagged user change Access Permission, internal-only or thread privacy on a row they can write?**
   (a) Field security on the three columns: everyone reads, only internal BU teams and the BFF write. **Recommended**:
   it makes AC-3 hold on every surface, internal users are unaffected, and it costs one target in the existing engine.
   (b) Give flagged users no Write anywhere: per-user shares to them capped at Read, Append and AppendTo. Simpler, but
   outside counsel lose editing on records shared with them for editing.
   (c) A job reverts a lowering made by a flagged user. Detective only, with a 2-minute window.
2. **S4 / #2201: Office edit on a secure record for a flagged user.**
   (a) View and download only, through the BFF; no edit-open. **Recommended**: no container role, nothing to test live.
   (b) An item-level permission on the one file, after a live test with the BFF identity (the az CLI token was refused).
   (c) Defer and keep today's behaviour.
3. **What the team share lets a flagged user do on a record they can see.**
   (a) Read and add new items under it (Read + AppendTo); edit only where a record is individually shared with Write.
   **Recommended**: least privilege; it matches "see all records".
   (b) Read, Write, Append and AppendTo on every visible record, which is close to today's Office Add In depth.
4. **Restricted access list.** Round 114 said "(unless for restricted they are added to the access list)", but round
   113 defines Restricted as internal-only, and I-14 bars flagged users from Restricted shares.
   (a) Restricted stays fully closed to flagged users; I-14 unchanged. **Recommended**: it is the round-113 rule.
   (b) An administrator may add a flagged user to a Restricted record's access list, as an explicit exception to I-14.
5. **Scope of the team share.**
   (a) Every visible lineage row in the environment (one customer per stamp). **Recommended**: round 115's one team; it
   equals today's reach for users in the customer BU.
   (b) Only rows in the BU subtree each user came from (one External team per source BU). Needed only if a customer
   keeps internal units hidden from each other's outside counsel.
6. **Invoices and exports in the model-driven app.**
   (a) No privilege on the invoice family for the external role: a flagged user's invoices are SPA-only. **Recommended**:
   invoice files are already SPA-only (ebilling 088), and the firm rule (round 116 F1-4) has no Dataverse form.
   (b) Per-firm External teams sharing invoice-family rows by vendor organization, for model-driven-app parity.
7. **Decided by the design unless you object.**
   (a) Accept all four. **Recommended.**
   (b) Discuss individually.
   The four: a flagged user's unfiled create is owned by the customer BU's team (§3.2); rows owned by flagged users are
   re-owned, and a Restricted root with a flagged owner is reassigned (§3.14 step 7); a hand-made share on a row that
   became internal-only is not restored when it becomes visible again (§3.4); the placement job never moves application
   users, administrators or disabled users (§3.13).

---

## 5. Risks and open items

- **Matrix roles** re-open depth silently: the census reads each assignment's BU, not the user's.
- **Dataverse search** (S1 F3): team-shared rows did not appear for 13.5 minutes and a deleted row still did. A residual
  window; re-check after 24 hours at the live gate.
- **`WhoAmI` ignores impersonation** (F5): the live gate tests through the user's own token or impersonated reads, never
  WhoAmI.
- **Lookup display names**: a visible row shows the primary name of a hidden parent through its lookup. The taint rule
  hides children of hidden parents, and a root never names its children; S1 saw names render only for shared rows.
- **Direct messages** between an internal and a flagged user: the predicate decides. A Direct message raised to
  internal-only stops being readable by the flagged participant, which is the rule but may surprise users.
- **Other environments**: hierarchy and matrix settings must be read per environment before step 2.
- **Secure-family owner teams** (#2362, not built): a flagged user must never be a member.

## 6. Sources

- Code: origin/master `df93d2029c`, cited inline.
- Docs: `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md` (I-2, I-6, I-12, I-14, I-16, I-17, I-18),
  `.claude/constraints/auth.md`, `.claude/adr/ADR-002-thin-plugins.md`, `.claude/adr/ADR-028-spaarke-auth-architecture.md`,
  `notes/task-188-d7-document-narrowing.md` (§6, §14–17), `notes/task-189-own-narrowing.md` (§2.4, §4),
  `notes/session27-owner-decisions-and-research.md` rounds 76–78, 111, 113–116, 121, 129–131,
  `notes/wrap-up-uat-and-decisions-2026-11-11.md` (D-13).
- Issues: #2179, #1864, #2125, #2127 (closed by #2170), #2189, #2201, #2369.
- Microsoft Learn: *Ownership-based security in Dataverse*; *How access to a record is determined*; *Filtered record
  ownership* (preview); *OAuth 2.0 On-Behalf-Of flow*; *Create or edit business units* (role removal on a BU change).

---

# Appendix A: decision record (v1, 2026-10-21 to 2026-10-28)

The sections below were written against the v1 recommendation and master `1cc7b39ff3`. They are kept as written, except
that statements of specific identities' role holdings and business-unit placement were made generic on 2026-11-11
(public-repo rule); the decisions are unchanged. Where they refer to "§3" or "the
recommendation above", they mean v1 (in git history at `656e00f66d`). The v2 body supersedes v1 where they differ.

## A.1 Owner decisions (rounds 114–115, 2026-10-21) — these supersede the recommendation above where they differ

- **Visibility target.** The model-driven app and the SPA must match. A flagged-external user sees every record except:
  - secure records, unless shared with them;
  - Restricted records, unless they are on the access list;
  - internal-only records.
- **Mechanism, design A with automatic sharing.**
  - Flagged-external users go into the "External Collaborators" business unit, with no business-unit depth.
  - Everything else follows the round 115 decision in `session27-owner-decisions-and-research.md`: what the share job shares with the one "External Collaborators" team, when it withdraws, and how Restricted and secure records are handled.
  - Spike S3 measures the cost in share (POA) rows.
  - This supersedes §3's "shares only for records shared or assigned to them" and Q2's "would lose" list.
- **Q3:** internal-only passes to records under or about a hidden record, using the SPA's exact rule.
- **Q4:** about 2 minutes is accepted for changes made outside the product.
- **Q5:** a scheduled BFF job moves a newly flagged user automatically, under the round 115 safeguards: one-way only, and it acts only on a flag read as definitely true. It alerts only when a move fails. This replaces the operator script for moving in.

## A.2 Carried-in question answered (main session, 2026-10-21): scorecard, Insights and Workspace AI

The POML's open question: do scorecard grades, `InsightsSearchEndpoint` and `WorkspaceAiEndpoints` count as internal-only for a flagged-external systemuser? **Answer: no new BFF gate on any of them.** None returns a class of data on round 113's internal-only list that 191's Dataverse enforcement or the existing trims don't already withhold. Facts are from `origin/master`, read-only, 2026-10-21.

| Surface | What it returns | Why no new gate |
|---|---|---|
| Scorecard: `POST /api/matters/{id}/recalculate-grades`, `POST /api/projects/{id}/recalculate-grades` (`ScorecardCalculatorEndpoints.cs:39`, `:60`) | Grades only. | **Record:** the gate is `FinanceAuthorizationFilter`'s Read check, made with the caller's own token (`FinanceAuthorizationFilter.cs:330`, no app-only branch). A Restricted or secure matter therefore follows 191's Dataverse enforcement automatically.<br>**Data:** the grades are columns on the same matter or project row, which the user reads directly anyway.<br>**Callers:** MDA web resources only. |
| Insights: `POST /api/insights/search`, `/ask` and `/assistant/query` | Document names, snippets, a summary. | **Subject:** gated by the caller's Read (task 163).<br>**Documents:** every one passes `RetrievalAccessTrim.SearchReadableAsync` (`InsightsOrchestrator.cs:969`): one read as the caller (task 176), then the D7 narrowing for a caller who is not an internal systemuser (task 188 round 3, `RetrievalAccessTrim.cs:20-26`). Restricted documents and documents derived from internal-only communications are withheld.<br>**Index:** `IndexRetrieveNode` rows are trimmed the same way (176 fix round 1). |
| Workspace AI: `POST /api/workspace/ai/summary` (`WorkspaceAiEndpoints.cs:30`) | An AI summary built from the record's columns; for a matter, that includes spend and budget. | **Documents:** narrowed (`InternalPlaneDocumentNarrowing`).<br>**Records:** the caller's Read decides, so 191 enforcement carries over.<br>**Spend:** spend and budget are not internal-only under round 113. Under the round-114 visibility target, a flagged-external user who sees a matter sees its row, and these columns are plain (not field-secured) columns on that row. |

- **Procedure files.** They live in the ebilling file store, not in the RAG index or the `sprk_document` table. None of the three surfaces reads them. T191 still checks that no Procedure file is indexed, and if one ever is, the trim must hide it (§3.8).
- **Spend for flagged-external users is ebilling's call, not a route gate.** Ebilling's step 0 refuses flagged-external callers on `/api/finance` (on `origin/work/ebilling-r1` only, `FinanceAuthorizationFilter.cs:378-390`). If ebilling also wants matter spend hidden from them, the mechanism has to be field security on the spend columns. A route gate would leave the same values visible in the model-driven app and in this summary. Ebilling has the facts; the decision stays theirs.
- **Correction to the inventory agent.** It reported Insights search as having "no per-document check". That is wrong: the code above shows the trim. The inventory is not cited further.

T191a (spikes) is unblocked.

## A.3 T191a read-only results (dev, 2026-10-22, GET only)

Raw output: `C:/Users/RALPHS~1/AppData/Local/Temp/uac191a/` (not in the repo); researcher memory `uac191a-flagged-external-dataverse-baseline-2026-10-21.md`.

- **Hierarchy security: off.** `ishierarchicalsecuritymodelenabled = false`; 0 `hierarchysecurityconfigurations`. Not a leak path on dev (closes the §7 open item for dev; every other environment must be read the same way).
- **Matrix business units: on.** `EnableOwnershipAcrossBusinessUnits = true`. `DoNotRemoveRolesOnChangeBusinessUnit` and `AlwaysMoveRecordToOwnerBusinessUnit` are unset, so the defaults apply: a business-unit change **removes the user's roles** and **moves the records they own** (Learn, *create-edit-business-units*). S2 confirms this live.
- **The leak path, confirmed.** Flagged users can hold roles whose depth reaches internal rows through their business unit, directly or through the unit's default team (the role-depth and default-team paths, §1.2). Deep anchored at a high business unit reaches every unit below it. No "External Collaborators" business unit or team exists yet.
- **Design change (§3.2):** `prvReadUser` and `prvWriteUser` are not Basic-capable (`canbebasic = false`).
  - The external role holds `prvReadUser` at **Local**: it sees the users of its own business unit only. Lookup display names still render from the parent row's formatted values, and S1 verifies this.
  - The role holds **no** `prvWriteUser`. The platform Basic User role doesn't hold it either, so a flagged user can't edit their own systemuser row or clear the flag.
- **S3 baseline:** the org has 1,980 share rows in total. Table sizes: document 600, communication 319, event 83, to-do 66, matter 62, project 23. Mirroring every non-secure, non-Restricted row to one team is roughly 1,100 share rows on dev.
- **S4 (docs only):** SPE item sharing with a non-member is documented (`driveItem invite` / permission create; "sharing an item doesn't grant access to the container"). App-only invites work for existing guests (`sendInvitation:false`). Office web edit is implied but not stated. Two things are open live: whether the restrictive sharing model blocks app-only invites, and the tenant's guest-sharing policy.
- **The writes that remain** (S1 setup and negatives, S2 move, S3 about 1,000 shares, S4 invite) are listed with undo steps in the researcher's report. They need the owner's in-session approval (POA constraint).

**Spend (ebilling, owner, 2026-10-22):** `sprk_totalspend` and `sprk_totalbudget` stay visible to flagged-external users. There is no field security and no task; it is recorded in ebilling's model note.

## A.4 T191a live spike results (dev, 2026-10-22, round 121; all undone)

Scripts, snapshots and logs: `C:/Users/RALPHS~1/AppData/Local/Temp/uac191b/` (not in the repo). A test flagged user played the external user. Teardown restored it to its pre-spike snapshot (business unit, roles, teams, shares, owned rows), verified against the snapshot. The spike business unit, team and role are deleted (404).

| Spike | Verdict | Facts |
|---|---|---|
| S2: business-unit move under matrix | **Go** | The move (PATCH `businessunitid`) strips the direct role and swaps the root default team for the new business unit's default team. Rows the user owns do **not** move; system rows such as mailboxes, queues and notifications stay where they are (F4). |
| S1: role and shares | **Go, with F1** | **Role:** 30 privileges at Basic (Read, Write, Append, AppendTo and Create on matter, project, document, communication, event, to-do), plus `prvReadUser` at Local, plus `prvRead/WriteUserSettings` at Basic. The platform adds 9 Global system-table reads itself.<br>**Reads:** exactly the 8 shared rows. Unshared rows and users outside the business unit get 403. Lookup names render.<br>**Blocked writes:** re-linking or unlinking `sprk_relatedcommunication` / `sprk_parentdocument` (field security from task 188, or AppendTo); re-filing to an unshared matter; writing an unshared row; clearing its own `sprk_isexternal` (no `prvWriteUser`).<br>**Not blocked:** lowering `sprk_accesspermission` on a shared row it can write (**F1**).<br>**Creates:** document, to-do and event under the shared matter: 204. |
| S3: about 1,000 shares | **Go** | Grant: 987 rows in 78 s (12.7 rows/s, batches of 100, no 429). Revoke: 76.6 s. The user saw exactly the granted set, then nothing. **F2:** `RevokeAccess` leaves the share row at mask 0, so the share-row count rose 1,980 → 2,975 and can't measure the mirror. |
| S4: SPE item invite | **Blocked** | The az CLI Graph token gets 403 on the container and the item. It needs the BFF managed identity, which only works on Azure-hosted compute (for example the App Service SSH console), or an interactive delegated sign-in through an app registered on the container type. |

- **F3, Dataverse search:** impersonation is honoured, but team-shared rows didn't appear in the user's search within 13.5 minutes, and a deleted row still did. Matters aren't indexed at all. Treat this as a residual window; re-check after 24 hours in the owner checklist.
- **F4:** the re-own of rows owned by flagged users stays in T191c. Mailboxes and queues are system rows, outside the registry.
- **F5:** `WhoAmI` ignores impersonation. Use it only for the operator, never for this check.
- **Owner browser checklist** (needs the setup re-run first, then the undo): the model-driven app opens as the user; only the shared rows are visible; no Share or Assign buttons; a direct URL to an unshared row is refused; Copilot and `grid_overview` see only the shared rows; Office edit, after S4.

## A.5 ebilling 088 input (ontology review, 2026-10-22)

- **Constraint for 191 (and ebilling 086).** The External Collaborator role gets **no** privilege on `sprk_storedfile`.
- **Why it matters.** A flagged-external user placed in a business unit that is in 088's reader-profile BU set gets column read only, with no table privilege: the same exposure that already exists on the secured `sprk_document` columns. The External Collaborators unit must stay out of that set.
- **What 191 checks.** It keeps the table privilege at none, and its role test (T191) asserts that.

## A.6 D-185 input (owner via ontology and ebilling, 2026-10-23)

- **Decision.** Only billing roles may read rule sets and rules directly in Dataverse (#2285).
  - ebilling sets `prvReadsprk_ruleset` to None on Spaarke Basic User and Core User.
  - Ontology removes `prvReadsprk_Policy` from Basic and Console User, and `prvReadsprk_PolicyVersion` from Console User, after its task 163.
- **Constraint for 191.** The External Collaborator role gets **no** privilege on `sprk_ruleset`, `sprk_policy` or `sprk_policyversion`, and T191's role test asserts it.
- **Not 191's scope.** 191 creates no billing-admin role. Granting explicit Read on those three tables to the billing-admin role or roles belongs to whoever creates them, which is ebilling. Today the billing admins are System Administrators.
- **Depth correction (2026-10-23, after ontology's #1911 review K1).** The input above originally said "org depth, O-6" for all three tables. That is right for `sprk_ruleset`, which has no matter lookup, but wrong for `sprk_policy` and `sprk_policyversion`.
  - Under round 102 item 4 (D-123), a matter-scoped rule and its versions are re-owned into the secure BU when the matter turns Secure, and Global Read always reaches that BU.
  - UAC's recommendation is therefore Deep on those two tables. It is assigned only to users whose BU is not an ancestor of the secure BU.
  - ebilling takes this to the owner before the role is assigned.
- **UAC impact.** No UAC surface reads those tables as the caller. `SecureChildLineage` and `RecordOwnershipResolver` read them app-only.

## A.7 Inputs from the ebilling 086 review (2026-10-28): `sprk_storedfile`

- **The share mirror gives Read only on `sprk_storedfile`:** no Write, Delete, Share, Assign or AppendTo. StoredFileService is the only writer, and a model-driven-app Delete would orphan the blob and destroy invoice evidence. This needs a per-table cap on `ChildMirrorMask`, with a must-fire test. Any role that 191 grants on the table gets Read only: the role grant is the control, and the mirror cap is defence in depth.
- **Inline mirror (086 K1):** StoredFileService does not call `SyncChildAsync` after create, so a Secure-team-owned file gets its root's sharees on the next 2-minute reconcile. Decide whether to add the inline mirror when a role first grants Stored File privileges.
- **Secure-held source copies (086 K5):** a copy of a document held by a Secure matter, attached to an ordinary invoice, is owned by the invoice's ordinary team (citation rule). Its bytes stay protected by the copy rule, but once Read on the table is granted to BU-depth roles, its name, size and hash become visible in the model-driven app. Decide before granting.
