# #2179 — Flagged-external systemusers reach no internal-only record in Dataverse (design study, task 191)

> 2026-10-21 · researcher (read-only; no Dataverse, Azure or Key Vault access). Code cited at **origin/master `1cc7b39ff3`**
> (task 189 / PR #2169 merged) unless marked **[188]** = `origin/uac/188-d7-document-narrowing` `620aa21eec` (PR #2170, open).
> This worktree's HEAD is 780 commits behind master, so paths are cited from master, not from the working tree.
> Inputs: issue #2179 (reopened, owner round 113), round 113 in `session27-owner-decisions-and-research.md`, the coordinator's
> two additions (036 read set; owner-confirmed definition; `matter` = every root; one-place registration), #1864, #2125,
> #2127, #2189, task POML `tasks/191-d7-flagged-external-dataverse-enforcement.poml`.
> Status: **design checkpoint** (POML `<design-checkpoint>`). Nothing here is built.

---

## 0. Summary

**The gap is mostly role depth, not shares.** A flagged-external licensed user today reads every non-secure row in their
business unit through role depth (`Spaarke Basic User` / `Spaarke Core User` Read at **Deep**) and through membership of
their BU's default owner team, which owns those rows (I-6). Root shares and the secure-child mirror are the minor source.
Dataverse has no deny and no GA row filter (§2), so no share-removal or column flag can hide a row that BU depth reaches.
Round 77 ran into exactly this and accepted it as a limit.

**Recommendation (A): isolate the user, then grant by share only.**
1. **Placement.** Every flagged-external systemuser sits in one dedicated **External Collaborators** business unit that
   owns no records. They hold one new role, **Spaarke External Collaborator**, at **Basic** depth only on every lineage
   table, with no Share or Assign. They then have no role-depth or default-team access to any customer row.
2. **Grants.** Their access comes only from POA shares. Root shares are unchanged: `/share-user`, the Assigned-To
   materializer and I-14's Restricted bar. Children get **an external-sharee mirror**, an extension of
   `SecureChildShareSynchronizer` to non-secure roots for flagged-external principals only. It grants a child only when
   **one predicate** says the row is externally visible, and it revokes on every row where the predicate hides it.
3. **The predicate.** It is the SPA's own rule, reused and not copied:
   - `ExternalMultiRootRule` (secure roots);
   - the effective Restricted bar (I-14 / `EffectiveRootFlags`);
   - the D7 document narrowing **[188]**;
   - the 065 communication rule;
   - a **taint closure** over `SecureChildLineage`, so a row filed under or about a hidden row is hidden;
   - a per-table internal-only marker, declared **in the one lineage registry** (Procedure tables never; Stored File by
     parent).
4. **No new Dataverse column.** "Internal-only" is materialised as the **absence of the share**. Fail-closed is the
   default state: a row nobody mirrored is invisible.

Read follows. **Write follows too:**
- To lower Access Permission, unlink or re-parent, a user needs Write on the hidden row (never shared) or AppendTo on the
  hidden target (never shared). Dataverse refuses both.
- Ownership is the only bypass. The design closes it: a flagged-external user never keeps ownership of a lineage row.

**Alternative (B):** block the Dataverse resource for flagged-external users with Entra Conditional Access, so their only
path is the BFF's app-only plane, which tasks 036 and 188 already narrow. This is simpler. Its cost: it removes the
model-driven app, Console, Office and every OBO feature from users the owner calls "licensed".

**Issues:**
- **#1864**: this design is the "real fix".
- **#2125** (`grid_overview`): closes with it, because it runs OBO FetchXML.
- **#2127**: does not close with it. Retrieval and archive ingest are app-only. Task 188 (slices A and B) closes it, so
  re-scope it to #2170.
- **#2189**: the taint rule (Q3) answers it for to-dos, events, memos, signals, decision records and analyses.

**Owner questions:** five, in §5. **Build:** about 7 tasks, roughly L/XL overall (§6). Spikes come first.

---

## 1. Inventory (current state)

### 1.1 What "internal-only" means, per table (owner-confirmed round 113 + coordinator)

| # | Internal-only when | Tables / columns | Decided today by (BFF) | Dataverse-side today |
|---|---|---|---|---|
| 1 | **Access Permission = Restricted** (100000002; `ExternalParticipationService.AccessPermissionRestricted`, `ExternalParticipationService.cs:887`). **Limited stays visible** (round 111 item 4). | Roots: `sprk_matter`, `sprk_project`, `sprk_workassignment` (later `sprk_agreement`), column `sprk_accesspermission` (unsecured, e.g. `sprk_Matter/Entity.xml:542-543`). Filed WA/project: stored max(own, floor) with `sprk_accessinheritance` (I-17, secured). Children: `sprk_todo`, `sprk_event`, `sprk_communication`, `sprk_document` carry an I-16 **display copy**. No decision reads it except a document's (I-14 as amended by 188). | Root veto: `InternalShareEndpoints.IsBarredOnRestricted` (`InternalShareEndpoints.cs:1413`), used by `AccessibleRecordSetService.cs:682-696` on the SPA/Teams plane. Effective (through filing): `EffectiveRootFlags.RestrictedThroughFilingAsync` (`EffectiveRootFlags.cs:164`). | Only **direct root shares** are barred (I-14: `RestrictedExternalShareRemover.cs:142,185`, the 5-min `AssignedAccessReconciliationJob.cs:81`, `AssignedAccessStore.ScanRestrictedRootsAsync` `:948`). **BU depth still reads a non-secure Restricted root and all its children** (round 77 limit, I-14 text). |
| 1b | A **document's own** Restricted, or a Restricted ancestor on its `sprk_parentdocument` walk | `sprk_document.sprk_accesspermission` + `sprk_accessinheritance` (task 189; **both unsecured**, `sprk_Document/Entity.xml:582-583, 621-622`); stored = max(own, inherited) (I-16, `InheritedAccessPermission.DecideDocument`) | **[188]** `ExternalDocumentNarrowing` (`Infrastructure/ExternalAccess/ExternalDocumentNarrowing.cs:62`, `MaxHops = 3` `:89`) | none |
| 2 | **Communication marked internal-only**, and the documents attached to or derived from it. The 065 rule: `sprk_isinternalonly` reads `false` AND a thread is set AND the thread is **Open** now. True, null, no thread, a Private thread or an unreadable value all hide. | `sprk_communication.sprk_isinternalonly` (unsecured, on the main form; `sprk_Communication/Entity.xml:1848-1849`); `sprk_communicationthread.sprk_privacystate` (Open 100000000 / Private 100000001, `MembershipDerivationModels.cs:46-50`; unsecured, on the form). Derived documents: `sprk_document.sprk_relatedcommunication`, `sprk_parentdocument`, up to 3 hops (unsecured; field security is a manual **[188]** gate, `scripts/Set-DocumentNarrowingFieldSecurity.ps1`). Communication-family tables are refused outright to external callers in the AI tools: `sprk_communication*`, `sprk_emailartifact`, `sprk_attachmentartifact`, `sprk_emailreviewlog`, `sprk_affinity` (`ExternalCallerCommunicationGuard.cs:92-98`). | Writers: created `true` and lowered only by `CommunicationInternalOnlyFloor.LowerWhenSafeAsync` (`CommunicationInternalOnlyFloor.cs:131-154`); outbound sets it at create (`CommunicationService.cs:915, 2222, 2422`). Reader: `CommunicationOpenThreadGate.ReadVisibleCommunicationIdsAsync` **[188]** (`CommunicationOpenThreadGate.cs:132`); AI tools: `ExternalCallerCommunicationGuard.FilterClause` (`:133-135`). | none (any Read holder reads the row, `sprk_body` included) |
| 2b | *(SPA parity, not in the owner's list)* Messages classified **Privileged / Potentially Privileged** | `sprk_communication.sprk_privilegeclassification` | `CommunicationThreadReadService.ExternallyHiddenPrivilege` / `ExternalPrivilegeFilterClause` / `IsHiddenFromExternal` (`CommunicationThreadReadService.cs:87-109`) | none. **Q3 asks whether Dataverse parity includes it.** |
| 3 | **All Procedure files** (ebilling) | **Old model:** Procedure Library documents (`sprk_document` rows in `FinanceOptions.ProcedureLibraryContainerId`; most unlinked, some filed, see ebilling DEF-015/#1975). **New model (ebilling `procedure-and-file-model.md` v4, not built):** `sprk_procedure`, `sprk_procedureversion`, and `sprk_storedfile` rows whose parent is a procedure version (Blob). Owner O-6: Procedures are internal only; internal roles get org Read on `sprk_procedure`. | ebilling F1-e: the internal file route refuses flagged-external callers for Procedure files, always (round 113 main-session reading) | none |
| — | **Not internal-only:** invoices and export files of "the related external user" | `sprk_invoice`, the planned `sprk_billingexport`, `sprk_storedfile` under an invoice or export (its "Firm-visible" flag is ebilling's) | SPA firm route: 036 read set → I-14 veto → task 187 invoice rule → file gate (coordinator; `ExternalMultiRootRule.cs` remarks "Invoices") | reachable by depth today |

**Events and to-dos (the coordinator's unchecked area).**
- **Links that can carry internal-only content.**
  - `sprk_todo` has `sprk_regardingcommunication` and `sprk_regardingdocument`.
  - `sprk_event` has `sprk_regardingcommunication`, `sprk_regardinganalysis`, and **copies of the email**: `sprk_emailsubject`, `sprk_emailfrom`, `sprk_emailto`, `sprk_emaildate` (`sprk_Event/Entity.xml`).
  - Both are lineage children through those lookups (`SecureChildLineage.cs:123-125, 163-165`).
- **What reads them today.**
  - Their own `sprk_accesspermission` is an I-16 display copy that no decision reads.
  - The external SPA lists apply only `ExternalMultiRootRule` and the task 187 live-parent check, plus the root Restricted veto through the root set. They make no 065 or D7 check (**[188]** `ExternalDataService.cs:1664-1734`, to-dos; events are analogous).
  - #2189 lists the same gap for the AI tools.
- **The gaps.**
  - A to-do or event **regarding an internal-only communication**, or **a hidden document**, under a visible matter is visible to a flagged-external user. This holds on the SPA and in Dataverse.
  - An event made from an email shows that email's subject, sender and recipients.
  - A to-do or event under a **Restricted root** is hidden on the SPA, through the root veto, but readable in Dataverse, through depth.
- **Same shape elsewhere.** `sprk_memo`, `sprk_analysis`/`sprk_analysisoutput`, `sprk_signal` and `sprk_decisionrecord` all have regarding-communication or regarding-document lookups (`SecureChildLineage.cs:64-158`).

### 1.2 How a flagged-external systemuser gets Dataverse access today

| Source | Mechanism | Evidence | Reaches internal-only rows? |
|---|---|---|---|
| **Role depth** | `Spaarke Basic User`: Read at **Deep** on matter, project, WA, document, event, to-do, invoice, and Read at Basic on communication. `Spaarke Core User`: Read, Write, Append, AppendTo, Assign and Share at **Deep** on every core table, communication included. Both hold `prvReadUser` Global and no `prvWriteUser`. | `src/dataverse/solutions/SpaarkeMaster/Roles/Spaarke Basic User.xml`, `Spaarke Core User.xml` (privilege greps); `notes/task-062-nfr05-role-depth.md` (finding A: Deep anchored at the user's BU reaches every descendant BU) | **Yes, the main source.** Every non-secure row in the user's BU and the BUs below it, internal-only or not. |
| **Default-team ownership** | Non-secure rows are owned by a **BU default owner team** (I-6, `RecordOwnershipResolver`; unfiled creates use the acting user's BU team, `RecordOwnershipResolver.cs:157-166`). Every BU user is a member of that team automatically. Learn: owning-team membership gives access "at any access level ... regardless of the business unit". | write-path I-6; Learn *how-record-access-determined* (Ownership) | **Yes, even at Basic depth.** This is why a role-depth fix alone is not enough: the user must not sit in a BU whose default team owns rows. |
| **Root shares (POA)** | `/share-user` and the Assigned-To materializer (I-12). Both refuse a flagged-external user on a Restricted root (`ClassifyEligibility`, `:1404`); the remover takes away shares made outside the product (≤ 5 min). Round 78: allowed on secure, non-Restricted roots. | I-12, I-14; `task-114-isexternal-eligibility.md` §2 | Restricted roots: no (≤ 5-min window). Their **children**: see the next row. |
| **Secure-child mirror** | `SecureChildShareSynchronizer` gives every **Secure-team-owned** child the root's sharees (intersection across secure roots), every 2 minutes (`SecureChildShareReconciliationJob.cs:49`) plus inline. On a Restricted root, external-flagged principals are barred (`SecureChildShareSynchronizer.cs:1085-1122`). It never touches children of non-secure roots (`:221-226`). | I-2 part 2, I-13 | **Yes.** On a secure, **non-Restricted** root shared with a flagged-external user (allowed by round 78), the mirror gives them every child, including internal-only communications, documents derived from them, and own-Restricted documents. |
| **Secure-root inheritance** | `SecureRootInheritance` passes a parent's sharees to filed WA/projects. Barred on Restricted (`Skipped(restricted)`). | I-13; `SecureRootInheritanceJob.cs:65` (5 min) | Not for Restricted. Other internal-only children: as the mirror. |
| **Direct-thread shares** | `DirectThreadAccessService` gives each participant Read on the messages of a Direct thread, which it creates Open (`DirectThreadAccessService.cs:24, 97`). | `SecureChildShareSynchronizer.cs:4-8` | Only if a message in a Direct thread with an external participant is later marked internal-only (an edge case; the predicate covers it). |
| **Gate decision records** | Shared to internal participants only (round 102 #10, `GateDecisionRecordSharing.cs:63`). | — | No (already excludes flagged-external users). |
| **Ownership** | Rows a flagged-external user creates **as themselves**: the model-driven app, the Web API, run-as-user writes. A Restricted root owned by such a user is reported `owner-is-external`, never re-owned (task 114 decision 5). | I-6 interim note; I-14 | **Yes**, if a row they own later becomes internal-only. |
| **Hierarchy security** | Not used in the design; the environment setting is unknown (no repo evidence). If on, a manager gets rows **owned by or shared to** direct reports (Learn). | Learn *how-record-access-determined* | Leak risk only if a flagged-external user manages someone. The census checks it (§3.13). |
| **SPE (adjacent, not Dataverse)** | Business-unit containers have **no** flagged-external standing members (`SpeContainerMembershipSync.cs:21, 225`). But on a **secure container**, an Office edit-open grants a JIT **writer on the whole container** to any caller with Write on the secure record. A flagged-external user is refused that grant only on a **Restricted** record (`OfficeEditAccessService.cs:110-160`). | task 171 | **Yes.** Container membership opens every file in the family container through SharePoint or Office directly, including internal-only .eml files, their attachments and own-Restricted documents. **[188]** narrows only the BFF routes. A new finding; §3.9 fixes it. |

**Who writes `sprk_isexternal`.**
- Only operators write it:
  - `scripts/Set-ExternalFlagForB2BGuests.ps1` flags outsider `#EXT#` guests (`-CustomerTenantIds` keeps Model-1 customer staff internal);
  - administrators edit it on the SystemUser main form (`SystemUser/FormXml/main/{53bf1941…}.xml:50`).
- **No BFF code writes it**: a grep of master finds readers only.
- It is **not field-secured** (`SystemUser/Entity.xml:245-280`, `IsSecured 0`). It must stay that way, because a masked value would read as blank, and blank means internal (task 114 decision 6).
- No Spaarke role grants `prvWriteUser`, so users cannot un-flag themselves. The platform's own Basic User role is not checked here (spike S1).

**The round-77 known limit** (write-path I-14, last sentences, master `DATAVERSE-WRITE-PATH-ARCHITECTURE.md:144`;
**[188]** adds the #2179 (c) sentence):
- "Dataverse role depth still lets an external-flagged licensed user READ a NON-secure Restricted record in their own
  business unit (MDA / Dataverse API)."
- Round 77 **rejected** "a separate business unit for external-flagged users". Recommendation A reverses that rejection
  (§5 Q1).

### 1.3 Write paths that let such a user lower, unlink or re-parent today

| Change | Path | Guard today |
|---|---|---|
| Root `sprk_accesspermission` Restricted → Standard/Limited | Model-driven form (TrackingFieldTrio / plain control), grid edit, Web API. Needs Write on the root: `Spaarke Core User` has Write at Deep. | Column unsecured. A parentless root is never put back. A filed WA/project below its floor is put back by `SecureRootInheritanceJob` (I-17, ≤ 5 min). The post-save sync then **acts on** the lowered value (shares follow). |
| Document own Restricted → floor | Form (parentless only; locked when filed), Web API, grid. Needs Write: Core User has Deep. | Both columns unsecured. 189's decision treats an edit that lands **equal to the floor** as "own = none": an accepted un-narrowing (`task-189-own-narrowing.md` §2.2 rule 5). An edit looser than the floor is put back (≤ 2 min). |
| Communication `sprk_isinternalonly` true → false | Main form, Web API. Needs Write on the communication: Core User has Deep. | Unsecured; no BFF update route (the BFF writes it only at create and through `LowerWhenSafeAsync`). |
| Thread Private → Open | Thread form, Web API. Needs Write on the thread (Basic). | Unsecured. |
| Unlink a document from its communication, or clear `sprk_parentdocument` | Web API (on no form) | **[188]** field security, a manual owner-approved gate, not applied yet |
| Re-parent out of a Restricted or internal-only ancestor | Model-driven lookup edit, Web API; BFF re-file routes (`ReparentAsync`, G5 as the caller) | BFF routes check AppendTo **as the caller** (Dataverse decides, so depth passes). Model-driven: nothing. |
| Read through AI tools as the user | `read_query` / `search_data` / `describe` (OBO) | 078 and **[188]**: the BFF narrows. `grid_overview` (OBO saved FetchXML): **nothing** (#2125). Write tools (`update_record` / `create_record`): Dataverse decides (owner 2026-10-20 on #1864: "governed by Dataverse access control"). |

### 1.4 The issues mapped

| Issue | Surface | Closed by |
|---|---|---|
| #1864 (ISS-010) | MDA, Web API, AI write tools | **This design** (Dataverse decides; the owner put the write tools here on 2026-10-20). |
| #2125 (ISS-014) | `spaarke.grid_overview` runs saved FetchXML **OBO** (`GridOverviewHandler.cs` ~:269/:356); 188 does not touch it | **This design**: OBO FetchXML returns only shared rows, and an internal-only row is never shared. No interim guard unless the owner wants one before 191 deploys. |
| #2127 (ISS-016) | RAG retrieval (app-only AI Search) and chat archive ingest (app-only download) | **Not by Dataverse security.** Closed by **[188]**: slice A `RetrievalAccessTrim.NarrowDocumentKeysForExternalCallerAsync` (`RetrievalAccessTrim.cs:233-285`); slice B / round 4 `ChatDocumentEndpoints.cs:1016-1027` through `InternalPlaneDocumentNarrowing`. Both narrow "every caller who is not a resolved internal systemuser". **Re-scope #2127 to #2170.** |
| #2189 | AI-derived tables in the AI tools | The taint rule (Q3) gives one answer for Dataverse and, if adopted, for the SPA lists and the AI tools (§3.3). |

---

## 2. Dataverse mechanisms (verified 2026-10-21)

Platform facts first:
- Security is **additive**: "another role can't hide a specific contact from the user" (Learn *wp-security-cds*, ms.date
  2026-10-05).
- Access needs **the privilege AND an access path**: ownership, team ownership, role depth, a share, or hierarchy. Each
  shared path holds only "provided the user passed the privilege check" (*how-record-access-determined*, updated
  2026-08-14).
- Team ownership grants access "at any access level ... regardless of the business unit".
- Matrix data access (**Record ownership across business units**) is **ON** in this org (write-path I-18:
  `EnableOwnershipAcrossBusinessUnits = true`). So roles can be assigned to a user **from any BU**.

| Mechanism | What it enforces | What it cannot | Cost | Operational risk | Limits that matter here |
|---|---|---|---|---|---|
| **Ownership/team isolation of internal-only ROWS** (park them in an internal BU or team that external users' depth cannot reach; what Secure does) | Exclusion by construction for parked rows | Internal-only is a **moving, per-row, derived** state (email flags, thread privacy, document chains, Restricted roots, children). Re-owning thousands of rows on each transition means Assign churn and cascades (Assign moves BU only when `AlwaysMoveRecordToOwnerBusinessUnit` is on). A user in the same BU still reads everything else by depth. | High: a moving re-own engine over every lineage table | High: an ownership flip-flop per transition; the 2-minute recent-changes engine already struggles with Secure (#1506, #1857 history) | Fights I-6 (BU team ownership). **Rejected**: isolate the few users, not the many rows. |
| **Separate BU for flagged-external users, Basic depth, shares only** | No depth access and no default-team access to customer rows; only explicit shares reach them. Shared access still needs the privilege, which the role holds at Basic. | Nothing is hidden **within** a share: the share itself must be withheld (next row). Under matrix, an admin can assign a role from an internal BU at Deep and re-open everything, so a census is needed. A BU change removes roles (the classic behaviour; to verify under matrix, spike S2). | Low schema cost; one role, one BU; scripted placement | Placement drift (a newly flagged user not moved); matrix role assignments; hierarchy, if on | Round 77 rejected it; reversing that is Q1. **Chosen (A).** |
| **Withholding / revoking the share mirror for internal-only children** | Exactly the owner's rule, row by row, from the same predicate the SPA uses | It only works once depth and default-team access are gone (row above). Fails open for the job's window on out-of-band transitions. | Medium: an extension of the existing synchronizer and jobs | POA growth: one row per (external user × visible descendant). Learn advises team shares to limit POA; community reports degradation past roughly 20M rows. Spaarke scale is far below that (§3.4). | Root→child relationships are NoCascade (`SecureChildLineage.cs:15-17`), so no platform cascade interferes. **Chosen (A).** |
| **Field-level security** | Masks or protects **columns**; protects writers (Create/Update to the BFF only) | "Column-level security doesn't grant access to a record", and it cannot hide one. Profiles are per principal, never per row value. Hiding `sprk_body` from every external user would hide visible messages too. | Low | A masked column reads empty: `sprk_isexternal` must never be secured; `sprk_issecure` already is | Use only for **writer protection** where it adds something: **[188]**'s document links stay. Nothing new is needed under A (§3.15). |
| **Hierarchy security** | Widens: managers reach rows owned by or shared to their direct reports | Never restricts | — | A **leak vector** if on and a flagged-external user is a manager | Census: hierarchy off for lineage tables, or no flagged-external manager. |
| **Filtered record ownership / record filters** (row-level, FetchXML predicates per privilege in a role) | Grants CRUD only on rows that match a filter, on existing user- or org-owned tables too | **Public preview** ("This is a preview feature", Learn page ms.date 2026-09-24; MC1486027 preview 2026-10-02 / MC1465569 2026-10-16). **Union only, no deny** (*wp-security-cds* 2026-10-05). A filter cannot express "shared with me AND not internal-only". Dataverse Search returns only the primary table for link-entity filters. The 2026 wave 2 release plans were not published (Microsoft moved to a roadmap in 2026-09). | — | Preview, not for production | **Watch item.** With a stored "externally visible" column it could later replace the mirror: Basic + a filter "visible eq true" over shared roots. Not designable now. |
| **(a) No direct Dataverse access** (still licensed) | Removing every role breaks the BFF's 036 app-only `RetrievePrincipalAccess` naming the user: with no privilege it says no, so the SPA would show nothing. **Conditional Access** blocking the *Microsoft Dataverse* cloud app for an Entra group **does** work: it blocks the MDA and the Web API, and **OBO to Dataverse** too, because CA is evaluated at the OBO token request (Learn *v2-oauth2-on-behalf-of-flow*, error response). App-only BFF calls are unaffected. | It also kills every OBO feature: Console, AI tools under OBO, Office add-in Dataverse calls, `IDataverseUserClient`. So the user is SPA-only. | Low build cost; needs Entra ID P1 in the resource tenant; an Entra group synced to the flag | A tenant-config write (owner-approved); CA mis-scoping could hit internal users; the group can drift from `sprk_isexternal` | See §4 (Alternative B). |
| **Model-driven app access only** (remove the app-role association) | Hides the app tile | The Web API and other apps still work with the same privileges | — | — | Not a control. |

---

## 3. Recommended design (A)

### 3.1 Shape

```
flagged-external systemuser ──► BU "External Collaborators" (owns 0 rows) ──► role "Spaarke External Collaborator" (Basic, no Share/Assign)
            │
            ├─ root shares   ◄── /share-user, Assigned-To (I-12), I-14 bar (unchanged)
            └─ child shares  ◄── external-sharee mirror: grant iff ExternalVisibility(row, user) = visible; revoke otherwise
                                 (L1 inline on BFF transitions; L4 2-min recent-changes + sweep)
ExternalVisibility = ONE predicate (registry-driven): multi-root rule ∧ ¬Restricted(effective) ∧ D7 doc narrowing
                     ∧ 065 comm rule ∧ taint closure over SecureChildLineage ∧ per-table marker
SPA/Teams (036 read set) and AI tools call the SAME predicate → parity
```

### 3.2 Placement: the business unit and the role

- **BU** `External Collaborators`: a child of root, a sibling of every customer BU, holding no records and no container.
  - Its default team is on the reader column-security profiles (`Set-RecordCreatorPersonSchema.ps1 -Apply` re-run, per
    the existing "a BU created later" rule).
  - It holds **no** role.
  - A census checks that it owns 0 lineage rows.
- **Role** `Spaarke External Collaborator`, assigned **from the External BU**:
  - every lineage table and root at **Basic**: Read, Write, Append, AppendTo, and Create where the product lets partners
    create (document upload, to-do, event; spike S1 lists them);
  - **no** Share, Assign or Delete beyond Basic;
  - **no privilege at all** on `sprk_procedure`, `sprk_procedureversion`, the communication-content tables
    (`sprk_emailartifact`, `sprk_attachmentartifact`, `sprk_emailreviewlog`, `sprk_affinity`) or the ebilling internal
    tables. That is a table-level "never", natively expressed;
  - the MDA user-settings privileges the platform requires for a role held outside the user's own BU (Learn, matrix note);
  - **no** Global or Deep depth on any lineage table.
- The user's **other roles are removed**: no Basic User, Core User or Office/Console role from an internal BU.
- Why both a BU and Basic depth: either alone leaks. Deep in the External BU reaches nothing, because the BU owns nothing.
  Basic in an internal BU still reaches every row the default team owns.
- **Who places users.** `scripts/Set-ExternalFlagForB2BGuests.ps1` is **extended** (not a new script) with a `-Place`
  step: dry run / `-Apply` / `-Verify`. It moves a flagged user to the External BU, assigns the one role, removes the
  rest, and reverses all of that on un-flagging.
  - This is an operator step and an owner-approved write.
  - The BFF application user gets no BU-change rights.
- **Fail closed between the flag and the move.** All BFF surfaces already narrow by the flag (036/188/065). Dataverse
  depth stays open until the move, and the census reports it CRITICAL within 15 minutes (§3.13). That window is Q5.

### 3.3 The predicate and the registry: one place to register a table

- **The registry.** `SecureChildLineage.Children` (`SecureChildLineage.cs:62-166`) is already the one map of every child
  table and its parent lookups, pinned by `SecureChildLineageTests`. The roots are `ExternalGrantRootType`
  (`ExternalGrantRoot.cs:8-13`); adding `Agreement` there makes agreements roots.
- **The change.** The `Table` record gains a required parameter `ExternalMarker`, so the compiler forces every table to
  declare one:
  - `None`: only inherited taint applies.
  - `Predicate(fn)`: an own-row rule:
    - communication: the 065 rule, plus the privilege rule if Q3 is yes;
    - thread: Open;
    - document: **[188]** narrowing;
    - Stored File: by parent (procedure version → Never; invoice or export → follow the parent, plus Firm-visible if
      ebilling requires it);
    - legacy Procedure Library document: its drive is the Procedure Library → Never.
  - `Never`: never externally visible (procedure, procedure version, the communication-content tables).
- **One place for derived tables too.** **[188]**'s `ExternalCallerCommunicationGuard.DocumentDerivedTables` (`:141`)
  and `RefusedContentTables` (`:92`) are **derived from the registry** instead of being a second list. That closes the
  "second copy drifts" risk that 188's own guard tests name.
- **`ExternalVisibility.DecideAsync(rows, principal)`** is one class in `Infrastructure/ExternalAccess`. It is a pure
  decision over app-only reads, batched like **[188]**, and composes the existing pieces without copying them:
  1. **Root rule.** The principal holds a share on at least one linked root, and every linked **secure** root is readable
     (`ExternalMultiRootRule.SecureRootsReadable`). For invoices, the task 187 live-parent rule.
  2. **No Restricted.** No linked root is Restricted, effective through filing (`EffectiveRootFlags`; a child's I-16 copy
     is never trusted).
  3. **Own marker.** The row's own `ExternalMarker` passes.
  4. **Taint closure (Q3).** No lineage parent, through ANY lookup in the registry, is hidden by steps 2 and 3.
     - It is bounded like the lineage walk (`ParentLineageWalk`'s depth 6; the document chain keeps **[188]**'s 3 hops).
     - Every extra lookup can only narrow, which is the same argument `SecureChildLineage.cs:18-23` makes for the mirror.
     - A cycle, a too-deep chain or an unreadable value → **hidden**.
- **Who calls it:**
  - the mirror (§3.4);
  - **[188]**'s `ExternalDocumentNarrowing`, which becomes the document branch of step 3, unchanged;
  - the SPA to-do, event and memo lists and the AI tools' derived-table filter (§6 T191h, if Q3 is yes).
- This is the "one predicate, every surface" rule of D7 §6.2, widened from documents to the registry.

### 3.4 The external-sharee mirror (Read and Write)

**Placement.**
- It extends `SecureChildShareSynchronizer` (`SecureChildShareSynchronizer.cs:215-240`) with an **external-sharee mode**.
- The mode applies to children of **non-secure** roots, and to children of secure roots for flagged-external principals.
- It **only ever touches POA rows whose principal is a flagged-external systemuser**. Internal users' shares, and every
  share on a secure child that the existing mode manages, keep their current owner.
- No new job: it runs inside the existing `SecureChildShareReconciliationJob` (2 min) and the recent-changes pass of
  `SecureChildReconciliationJob` (2 min, `SecureChildReconciliationJob.cs:100, 156-163`).

**For each flagged-external principal U and each root R that U holds a direct share on:**
1. Walk R's lineage descendants: the existing downward walk, `SecureChildReconciler`'s.
2. Decide every descendant through `ExternalVisibility`.
3. **Grant** U on each visible row:
   - rights = U's share mask on R ∩ `RecordShareLevels.ChildMirrorableMask`, **minus Share and Assign**;
   - across several shared roots, the **intersection** (task 149's fail-closed rule);
   - the role's Basic privileges cap it again.
4. **Revoke** every direct share of U on a row the predicate hides, **whatever its provenance**: a model-driven Share by an
   internal user, or an old mirror.
   - A revoked share made by hand is not restored automatically when the row becomes visible again. This is a known
     limit, consistent with "Restricted wins".
5. **Ownership.**
   - A lineage row **owned by** a flagged-external user (an MDA or Web API create) is re-owned through
     `RecordOwnershipResolver.ReparentAsync`'s owner step to its I-6 team, read back, and then mirrored. Nothing is
     released from isolation.
   - A flagged-external owner of a **root**: kept as today's `owner-is-external` report, plus the re-own in migration
     (§3.12).

**What Write now means.**
- Lower a Restricted value, unlink an internal-only document, flip a communication flag, open a Private thread: each needs
  **Write on a hidden row** → no share → Dataverse refuses (privilege passes at Basic, access fails).
- Re-parent a visible row **under** a hidden or Restricted parent: needs **AppendTo on the target** → no share → refused.
- Write on a visible row can only **narrow**: set Restricted, mark internal-only. The next pass revokes the share.
- So the owner's Write rule holds by construction, without new field security. Spike S1 verifies each case live.
- **[188]**'s document-link field security stays for internal users. It is not needed for this rule.

**Scale.**
- POA rows = Σ over flagged-external users of their visible descendants. For example, 50 users × 20 matters × 500 rows is
  about 500k rows: well inside what Learn and community data treat as healthy.
- Optimisation (only if spike S3 shows pressure): share to a **per-firm access team** instead of per user (one POA row
  per row per firm).

### 3.5 Transitions: revoke, regrant, and how fast

| Transition | Made by | Revoke / regrant | Window |
|---|---|---|---|
| Root becomes **Restricted** | Form save → post-save sync; "Update Access" | `RestrictedExternalShareRemover` (L1) removes U's root share and then calls the mirror for the root's descendants, inline | Inline; the L4 net is 5 min (`AssignedAccessReconciliationJob`) |
| Root stops being Restricted | same | Root share restored (`Skipped(restricted)` → given back, existing); the mirror regrants descendants | Inline / ≤ 5 min |
| Filed WA/project or child **inherits** Restricted (I-16/I-17) | Parent change | The effective check reads the chain, so the mirror revokes on the next pass without waiting for the stored copy | ≤ 2 min (recent-changes over the parent's descendants) |
| Document own Restricted (189) | `ChildAccessPermissionReconciler` / `CoreAncestorResolver.RefreshInheritedAccessPermissionAsync` | Those two writers call the mirror for that row (L1) | Inline on a BFF write; ≤ 2 min otherwise |
| Communication **marked internal-only** after creation; thread → **Private** | Only the form or Web API (no BFF route) | Recent-changes sees the modified communication or thread, then its derived documents (reverse `sprk_relatedcommunication`, `sprk_parentdocument`) and regarding to-dos, events and memos | **≤ 2 min + run** (Q4) |
| Communication **lowered** (`LowerWhenSafeAsync`) | BFF | The mirror regrants (L1 call after the lowering) | Inline |
| Re-link or re-parent | BFF re-file (`SecureChildReconciler.AfterRefileAsync`) | Mirror inline | Inline; ≤ 2 min outside the BFF |
| **New row** under a shared root | Any | Hidden until mirrored (fail closed) | ≤ 2 min for visibility (a usability window, not a security one) |
| User **flagged** / **un-flagged** | Operator | `-Place` moves the user; the mirror then includes or drops them; on un-flagging, mirror shares are revoked and depth takes over | Operator step; census ≤ 15 min |

**A catch-up sweep.** Every instance start, and a configurable sweep (default hourly), walks every
(flagged-external user × shared root). It follows the existing r1c catch-up pattern: per-instance cursor, carried rows,
watermark rules, under ADR-052 §5.

### 3.6 Fail-closed behaviour

| Fault | Result |
|---|---|
| The predicate cannot decide a row (unreadable flag, thread, chain; a cycle; too deep) | **No grant**; an existing U-share is **revoked**; the row is carried and retried; ids and counts logged |
| `sprk_isexternal` unreadable for a principal | Treated as external: no child grant (the mirror) and a census alert |
| A share read fails | Nothing is written for that row; carried |
| Job down or behind | No new grants (fail closed). Revocations of **out-of-band** transitions wait: fail open within the window. Heartbeat and census report it |
| Placement drift (a flagged user outside the External BU, or holding a role from another BU, or a non-Basic depth, or an owner-team membership) | Census CRITICAL every 15 min. The BFF surfaces still narrow by flag |
| Masked column (`sprk_issecure` reads empty for a user lacking the reader profile) | The predicate reads **app-only**, as **[188]** does, so masking never reads as "not secure" |

### 3.7 Interaction with task 189 and the 065 rule; materialisation

- **189.**
  - The predicate reads a document's **stored** value (189's max(own, inherited)) **and** the chain (**[188]** rule 3 +
    `EffectiveRootFlags`). So a lagging stored copy is never a hole.
  - 189's "edit to the floor = un-narrowing" (§1.3) stays correct for **internal** editors. A flagged-external user can
    no longer make that edit, because the row is not shared to them.
  - No change to `DecideDocument`.
- **065.**
  - Reused through `CommunicationOpenThreadGate.ReadVisibleCommunicationIdsAsync` **[188]** `:132`.
  - The rule's only writers stay where they are (`CommunicationInternalOnlyFloor`, `CommunicationService` at create).
- **Materialisation.**
  - Row-level filtering of email-derived documents cannot be expressed natively (§2). The design materialises the
    decision as **the presence or absence of U's POA share**, not as a new column:
    - nothing needs a backfill;
    - a missing share fails closed;
    - D7's "derived at read time, one owner of the invariant" stays true. The share is a cache of the predicate, and the
      predicate owns the rule.
  - A stored "externally visible" column only pays off if filtered record ownership reaches GA (§2) or AI Search must
    filter in-index. Neither applies now, so §11 (3) has no concrete cost, and the column is not added.

### 3.8 Invoices, export files and Procedure files

- **Invoice / export rows and their files.** These are lineage children: `sprk_invoice` today (`SecureChildLineage.cs:130-132`); ebilling registers `sprk_billingexport` and `sprk_storedfile`.
  - They are visible to U through the same root share and multi-root rule the SPA uses, which gives parity with the firm route the coordinator described. **No new relation for #2179.**
  - Whether "their invoice" must further narrow to `sprk_invoice.sprk_vendororg` = U's firm, so that firm A cannot see firm B's invoice on a shared matter, is **not an internal-only question**. It is a peer question for ebilling and SPA-r3; if adopted, it goes into the invoice table's `ExternalMarker`.
  - A Stored File's own **Firm-visible = No** (internal supporting files) → hidden, if ebilling confirms the flag applies to systemusers too.
- **Procedure files.**
  - **New model:** `sprk_procedure` and `sprk_procedureversion` get **no privilege** in the external role (table-level), and `sprk_storedfile` under a procedure version is `Never`.
  - **Old model:** a document whose drive is the Procedure Library container is `Never`. This covers DEF-015's filed rows.
  - Extracts in rules or decision records that a firm may see are not affected: those tables keep their own rules.
  - ebilling F1-e's route refusal stays as the BFF layer.

### 3.9 SharePoint Embedded: Office edit on a secure container (new finding)

- `OfficeEditAccessService` gives the caller a JIT writer role on the **whole** secure container (`:150-160`).
- For a flagged-external caller, the design changes that:
  - **no container membership, ever**;
  - instead, an **item-level** permission on the one document (Graph `driveItem` invite), if spike S4 confirms that SPE
    item invites to non-members work for this container type. Otherwise **view-only** (broker preview).
- `SpeContainerMembershipSync` removes any existing JIT grant held by a flagged-external user.
- This is needed whatever is decided for Dataverse: today it exposes a family's internal-only files outside every
  Dataverse and BFF check.

### 3.10 A new child table: registration in one place

To make a new table enforced end to end, add **one** `T(...)` entry in `SecureChildLineage.Children`: its parent lookups
**plus** its `ExternalMarker`. For a new root, add a value to `ExternalGrantRootType` (agreement, later).
- The mirror, the predicate, the census, **[188]**'s derived-table filter and the role-depth check all read the registry.
- The `Spaarke External Collaborator` role must hold Basic privileges for the table. A `-Verify` step in the role script
  compares the role's tables with the registry.
- Ebilling's Stored File, the record-scoped `sprk_ruleset` and the export table become entries.

### 3.11 Migration of existing shares and users on dev

Every write step below is owner-approved in-session (POML constraint). The agent runs dry runs only.
1. **Inventory (read-only script, counts and ids only).**
   - The flagged-external users, their BU, their roles: direct, team-conferred, and the BU each was assigned from.
   - Their direct POA shares on roots and children; rows they own; owner-team memberships; whether hierarchy security is on.
2. **"Would lose" report.** For each flagged user, the roots they reach today **only by depth** (no share). After the
   move, these disappear in the MDA **and** the SPA (036 asks Dataverse). An admin decides which to share explicitly
   first (Q2).
3. **Create** the External BU and the role (solution XML plus scripts).
4. **Mirror dry run.** Per user, the child grants it would add and the shares it would revoke because the row is
   internal-only.
5. **Grant first:** mirror writes on, before the move. That is harmless while depth still covers the rows.
6. **Move:** `Set-ExternalFlagForB2BGuests.ps1 -Place -Apply`, then `-Verify`. The verify step makes app-only
   `RetrievePrincipalAccess` calls naming each user against fixtures: None on a Restricted root, an internal-only
   communication, a derived document, a to-do regarding it and a Procedure file; Read on a visible child.
7. **Re-own** lineage rows owned by flagged-external users, and reassign Restricted roots with an external owner. The
   latter reverses task 114 decision 5, "the BFF never changes root ownership", for this case only; it is an operator
   script.
8. **Revoke** the internal-only shares left over from the existing secure mirror. This is the first sweep.
9. **Live gate:**
   - a test flagged-external user, Web API reads: must fire on each of the five internal-only kinds;
   - must not fire: an unflagged user with the same shares;
   - Write negatives: lower Access Permission, unlink, re-parent, flip the communication flag. Each is refused.

### 3.12 Enforced by (enforcement ladder)

| Rule | Mechanism |
|---|---|
| Every lineage table declares its external marker | **Type**: a required `ExternalMarker` constructor parameter on `SecureChildLineage.Table` |
| One predicate; no second list of derived or refused tables | **ArchTest**: `DocumentDerivedTables` / `RefusedContentTables` are computed from the registry; no other class defines a table → internal-only map (extends **[188]**'s `ExternalDocumentNarrowingGuardTests`) |
| Every POA grant that can name a systemuser on a lineage child passes the predicate | **ArchTest / source scan**: callers of `IDataverseRecordShareService.Grant*` / `Modify*` on child tables are an allow-list, and each allow-listed member calls `ExternalVisibility` (synchronizer, `DirectThreadAccessService`, `GateDecisionRecordSharing`, materializer) |
| Placement, depth, teams, ownership, hierarchy | **Census job** (extends `SecureRecordIsolationCensusJob`, 15 min, CRITICAL per finding) plus the NFR-05 assertion's live census (task 062) widened to flagged-external users |
| Role ↔ registry parity | `-Verify` in the role script, run as a deploy gate |
| End-to-end | Live gate (§3.11 step 9), must-fire and must-not-fire, in the deployment guide |
| Office edit grants no container role to a flagged-external caller | Unit and route tests in `OfficeEditAccessService`; the existing SPE membership sync removes strays |
| The out-of-band window (≤ 2 min) | **Prose** in I-14: it cannot be mechanised without plugins (ADR-002) |

### 3.13 CLAUDE.md §11 answers (new surface only)

| New | 1 Existing | 2 Extension | 3 Cost of doing nothing |
|---|---|---|---|
| Role **Spaarke External Collaborator** | Spaarke Basic User / Core User (Deep) | Lowering their depth breaks every internal user; no existing role is Basic-only | BU depth gives flagged-external users every internal-only row in their BU (round 77) |
| BU **External Collaborators** (data, created by a script) | The customer BUs; the Secure Record BU (no users, I-2, so it cannot be used) | No existing BU is free of team-owned rows that also holds users | Default-team ownership gives every BU member access even at Basic |
| `-Place` step | `Set-ExternalFlagForB2BGuests.ps1` | **Extended**, not new | Users are flagged but never moved |
| External-sharee mode | `SecureChildShareSynchronizer` (secure children only) | **Extended**: same POA seam, same jobs, scoped to flagged-external principals | After the move, flagged users would see no child of their matters (documents, events, invoices) |
| `ExternalVisibility` (one class) | **[188]** `ExternalDocumentNarrowing`, the 065 gate, `ExternalMultiRootRule`, `EffectiveRootFlags` | It **composes** them; no logic is copied. It cannot live in any one of them, because it spans every lineage table | Each surface would re-derive the rule, and the copies would drift (D7 §6.2) |
| `ExternalMarker` on the registry | `DocumentDerivedTables` / `RefusedContentTables` (**[188]**) | Folds them into the registry | New tables (ebilling) would need registering in several places |
| Census checks | `SecureRecordIsolationCensusJob` | **Extended**; no new job | Placement drift would go undetected |
| `RecordOwnershipResolver`: a flagged-external acting user with an unfiled create → `KeepCreator` (not the External BU team) | `UnfiledOwnership` (`RecordOwnershipResolver.cs:157-180`) | A modification | The External BU's default team would own the row, and every external user of every firm would read it |
| No new column, no new field security, no new job registration | — | — | — |

**ADRs.**
- ADR-002: no plugins; jobs and inline L1 only.
- ADR-052 / 036: existing scheduled jobs are extended.
- ADR-028: no OBO change in A.
- Round 77's rejection of a separate BU is reversed only if Q1 = A. That reversal is an owner decision, not an ADR
  conflict.

---

## 4. Alternative (B): no direct Dataverse for flagged-external users

- **How it works.**
  - An Entra **Conditional Access** policy in the environment's tenant blocks the *Microsoft Dataverse* cloud app for an
    Entra group of flagged-external users. CA applies to B2B guests in the resource tenant.
  - The group is kept in step with `sprk_isexternal` by the same script's `-Place` step.
  - Users keep their licence and their roles. The roles are needed, because 036's app-only `RetrievePrincipalAccess`
    naming the user must still answer.
  - All their data comes through the BFF's app-only plane, which tasks 036, 188, 065 and 187 already narrow.
- **For.**
  - No mirror, no BU move, no POA growth.
  - The rule lives only in the BFF predicates, which already exist.
- **Against.**
  - The MDA, Web API, Console, Office add-in Dataverse calls and every OBO AI tool fail for these users, because CA is
    enforced at the OBO request: `interaction_required` (Learn OBO). They become SPA-only, which conflicts with "licensed".
  - App-only RPA still reports depth access, so every BFF surface must keep narrowing forever, and any new surface that
    forgets to narrow leaks (#2125, #2189 history).
  - It needs Entra ID P1 and a tenant-config write.
  - On Model-1 stamps, the policy and group are per environment tenant.
- **When B is right:** if the owner confirms that flagged-external users never need the MDA or Console.

---

## 5. Owner questions (minimum)

| # | Question | Options | Recommendation |
|---|---|---|---|
| Q1 | **Model.** Reverses the round-77 rejection of "a separate business unit for external-flagged users". | (A) External BU + Basic-only role + mirrored shares (they keep the MDA, Console and Office). (B) Conditional Access blocks Dataverse for them (SPA only). | **A** |
| Q2 | **Visibility after the move.** A flagged-external user then sees only records shared or assigned to them (directly, or through Assigned-To), in the MDA **and** the SPA. Records they read today only through business-unit depth disappear. | (a) Accept, with a pre-move "would lose" list that admins share from. (b) Auto-share everything they see today, then narrow. | **(a)**: (b) would re-grant what round 113 removes |
| Q3 | **What "internal-only" reaches.** (i) Does it pass down to every record filed under or **about** a hidden record (to-dos, events, memos, analyses, signals and decision records regarding an internal-only communication or a hidden document)? (ii) Does Dataverse use the SPA's exact external rule: Private or non-Open threads, and Privileged / Potentially Privileged messages? | yes / no, each | **Yes to both.** Fail closed; one rule on both planes. This also answers #2189 for these tables and adds the same check to the SPA's to-do, event and memo lists |
| Q4 | **Window for changes made outside the product** (marking a communication internal-only or a thread Private on the form; a re-link through the Web API). | (a) Withdrawn within about 2 minutes (the existing jobs); BFF changes inline. (b) Make those columns writable only through the BFF (field security), adding a BFF command for the form toggle. | **(a)**: matches the accepted windows of rounds 11 and 28 |
| Q5 | **Moving a newly flagged user.** | (a) An operator script plus a 15-minute CRITICAL census alert. (b) The BFF moves them automatically, which needs business-unit change rights for the BFF application user. | **(a)**: a BU change strips roles, and (b) widens the BFF identity's reach |

Decided by the design unless the owner objects:
- a flagged-external caller gets **no SPE container role** on Office edit: an item-level permission, or view-only (§3.9);
- a Restricted root owned by a flagged-external user is reassigned in migration (§3.11 step 7).

---

## 6. Task breakdown (after 188 merges; 189 is on master)

| Id | Scope | Size |
|---|---|---|
| T191a | **Spikes** (go/no-go). S1: Basic-only role in an owner-less BU plus child shares, checked through the Web API, MDA, Dataverse search, Copilot and `grid_overview`; the Write negatives (AppendTo blocks a re-link; no Write on unshared rows); the platform Basic User role's `prvWriteUser`; the Create set partners need. S2: a BU change under matrix (roles kept or stripped; records owned move or stay). S3: POA rows and time per 1k mirrored rows. S4: an SPE item-level invite to a non-member for Office edit | M |
| T191b | Registry `ExternalMarker` + `ExternalVisibility` (composes 188, 065, I-14, the multi-root rule, 187; taint closure); fold `DocumentDerivedTables` / `RefusedContentTables`; unit tests with must-fire and must-not-fire cases; ArchTests (§3.12) | M |
| T191c | Mirror external-sharee mode: grant and revoke; L1 hooks (remover, `AfterRefileAsync`, 189's two writers, `LowerWhenSafeAsync`); recent-changes reverse walk (communication or thread → documents, to-dos, events); catch-up sweep; `DirectThreadAccessService` and `GateDecisionRecordSharing` through the predicate; re-own of flagged-owned rows; `RecordOwnershipResolver` KeepCreator for a flagged acting user | L |
| T191d | Role XML + BU + `Set-ExternalFlagForB2BGuests.ps1 -Place/-Verify` + role-registry `-Verify` + census extension (placement, depth, team, ownership, hierarchy) | M |
| T191e | `OfficeEditAccessService`: no container role for flagged callers (item permission or view-only); membership sync removes strays | S |
| T191f | Dev migration (§3.11): inventory, would-lose report, dry runs, owner-approved applies, live gate | M |
| T191g | Docs: I-14 / I-16 rewrite (remove the round-77 and #2179 (c) limits; Enforced by), `.claude/constraints/auth.md`, the deployment guide (BU, role, `-Place`, gate); close #1864 and #2125; re-scope #2127 to #2170; update #2189 | S |
| T191h | *(if Q3 = yes)* The SPA's to-do, event and memo lists and the AI tools' derived-table filter call `ExternalVisibility` (taint parity) | M |

Overall: roughly L/XL (about 8–12 agent-days including review loops). BFF-touching tasks carry the §10 publish-size delta.

---

## 7. Risks and open items

- **Matrix role assignments** can silently re-open depth. The census must read each role assignment's **BU**, not just
  the user's BU.
- **Lookup display names.** A visible row shows the primary name of a hidden parent through its lookup. The taint rule
  hides children of hidden parents, but a **visible root** never names its children, so no case is known. The spike
  confirms it.
- **Dataverse search / Copilot** respect record security (Learn). Security-change latency to the search index is
  undocumented (prior research, 2026-10-12). This is a residual window, covered by the live gate.
- **Secure-family owner teams** (round 100b, not built): a flagged-external user must **never** be a member. Family
  access must come through the mirror. That is a constraint on that project.
- **ebilling peers.** Whether Stored File's Firm-visible flag applies to licensed outside counsel too, and the
  vendor-org "their invoice" narrowing (§3.8).
- **Hierarchy security.** Its state in this environment is unknown (S1 reads it).

## 8. Sources

- Code: master `1cc7b39ff3` and **[188]** `620aa21eec`, as cited inline.
- Repo docs: `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md` (I-2, I-6, I-12, I-14, I-16, I-17, I-18), `notes/2119-manage-access-all-forms-design.md` §6, `task-114-isexternal-eligibility.md`, `task-189-own-narrowing.md`, `task-188-d7-document-narrowing.md` **[188]**, `task-062-nfr05-role-depth.md`, ebilling `notes/procedure-and-file-model.md` v4 (`origin/work/ebilling-r1`).
- Issues: #2179, #1864, #2125, #2127, #2189.
- Microsoft Learn:
  - *Ownership-based security in Dataverse* (`power-platform/admin/wp-security-cds`, ms.date 2026-10-05);
  - *How access to a record is determined* (`power-platform/admin/how-record-access-determined`, updated 2026-08-14);
  - *Filtered record ownership* (`power-apps/maker/data-platform/filtered-view-record-ownership`, ms.date 2026-09-24, preview);
  - *OAuth 2.0 On-Behalf-Of flow* (`entra/identity-platform/v2-oauth2-on-behalf-of-flow`; CA at OBO);
  - *Restrict access by location / CA on Microsoft Dataverse* (`power-platform/admin/restrict-access-online-trusted-ip-rules`).
- Message Center: MC1486027 (mc.merill.net) and MC1465569: filtered record ownership public preview, October 2026.

---

## Owner decisions (rounds 114–115, 2026-10-21) — these supersede the recommendation above where they differ

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

## Carried-in question answered (main session, 2026-10-21): scorecard, Insights and Workspace AI

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

## T191a read-only results (dev, 2026-10-22, GET only)

Raw output: `C:/Users/RALPHS~1/AppData/Local/Temp/uac191a/` (not in the repo); researcher memory `uac191a-flagged-external-dataverse-baseline-2026-10-21.md`.

- **Hierarchy security: off.** `ishierarchicalsecuritymodelenabled = false`; 0 `hierarchysecurityconfigurations`. Not a leak path on dev (closes the §7 open item for dev; every other environment must be read the same way).
- **Matrix business units: on.** `EnableOwnershipAcrossBusinessUnits = true`. `DoNotRemoveRolesOnChangeBusinessUnit` and `AlwaysMoveRecordToOwnerBusinessUnit` are unset, so the defaults apply: a business-unit change **removes the user's roles** and **moves the records they own** (Learn, *create-edit-business-units*). S2 confirms this live.
- **Today's leak, measured.** The 2 flagged users (1 enabled) are guests in the root business unit. They get Spaarke Basic User, Office Add In User and Console User through the root default team (inherited roles), and Spaarke Basic User directly. Spaarke Basic User reads document, event, matter and to-do at **Deep from the root business unit, which is org-wide**. Office Add In User grants Write, Assign and Share on matter at Deep. Neither user owns any row, and no "External Collaborators" business unit or team exists.
- **Design change (§3.2):** `prvReadUser` and `prvWriteUser` are not Basic-capable (`canbebasic = false`).
  - The external role holds `prvReadUser` at **Local**: it sees the users of its own business unit only. Lookup display names still render from the parent row's formatted values, and S1 verifies this.
  - The role holds **no** `prvWriteUser`. The platform Basic User role doesn't hold it either, so a flagged user can't edit their own systemuser row or clear the flag.
- **S3 baseline:** 5 share rows for the flagged users (self-shares plus 1 activity). The org has 1,980 share rows in total. Table sizes: document 600, communication 319, event 83, to-do 66, matter 62, project 23. Mirroring every non-secure, non-Restricted row to one team is roughly 1,100 share rows on dev.
- **S4 (docs only):** SPE item sharing with a non-member is documented (`driveItem invite` / permission create; "sharing an item doesn't grant access to the container"). App-only invites work for existing guests (`sendInvitation:false`). Office web edit is implied but not stated. Two things are open live: whether the restrictive sharing model blocks app-only invites, and the tenant's guest-sharing policy.
- **The writes that remain** (S1 setup and negatives, S2 move, S3 about 1,000 shares, S4 invite) are listed with undo steps in the researcher's report. They need the owner's in-session approval (POA constraint).

**Spend (ebilling, owner, 2026-10-22):** `sprk_totalspend` and `sprk_totalbudget` stay visible to flagged-external users. There is no field security and no task; it is recorded in ebilling's model note.

## T191a live spike results (dev, 2026-10-22, round 121; all undone)

Scripts, snapshots and logs: `C:/Users/RALPHS~1/AppData/Local/Temp/uac191b/` (not in the repo). Test user `e44317a0…`. Teardown verified against the snapshot: business unit, roles (11f93c04 only), teams (root default only), 3 share rows, owned rows. The spike business unit, team and role are deleted (404).

| Spike | Verdict | Facts |
|---|---|---|
| S2: business-unit move under matrix | **Go** | The move (PATCH `businessunitid`) strips the direct role and swaps the root default team for the new business unit's default team. Rows the user owns do **not** move (2 mailboxes, a queue, a notification row) (F4). |
| S1: role and shares | **Go, with F1** | **Role:** 30 privileges at Basic (Read, Write, Append, AppendTo and Create on matter, project, document, communication, event, to-do), plus `prvReadUser` at Local, plus `prvRead/WriteUserSettings` at Basic. The platform adds 9 Global system-table reads itself.<br>**Reads:** exactly the 8 shared rows. Unshared rows and users outside the business unit get 403. Lookup names render.<br>**Blocked writes:** re-linking or unlinking `sprk_relatedcommunication` / `sprk_parentdocument` (field security from task 188, or AppendTo); re-filing to an unshared matter; writing an unshared row; clearing its own `sprk_isexternal` (no `prvWriteUser`).<br>**Not blocked:** lowering `sprk_accesspermission` on a shared row it can write (**F1**).<br>**Creates:** document, to-do and event under the shared matter: 204. |
| S3: about 1,000 shares | **Go** | Grant: 987 rows in 78 s (12.7 rows/s, batches of 100, no 429). Revoke: 76.6 s. The user saw exactly the granted set, then nothing. **F2:** `RevokeAccess` leaves the share row at mask 0, so the share-row count rose 1,980 → 2,975 and can't measure the mirror. |
| S4: SPE item invite | **Blocked** | The az CLI Graph token gets 403 on the container and the item. It needs the BFF managed identity, which only works on Azure-hosted compute (for example the App Service SSH console), or an interactive delegated sign-in through an app registered on the container type. |

- **F3, Dataverse search:** impersonation is honoured, but team-shared rows didn't appear in the user's search within 13.5 minutes, and a deleted row still did. Matters aren't indexed at all. Treat this as a residual window; re-check after 24 hours in the owner checklist.
- **F4:** the re-own of rows owned by flagged users stays in T191c. Mailboxes and queues are system rows, outside the registry.
- **F5:** `WhoAmI` ignores impersonation. Use it only for the operator, never for this check.
- **Owner browser checklist** (needs the setup re-run first, then the undo): the model-driven app opens as the user; only the shared rows are visible; no Share or Assign buttons; a direct URL to an unshared row is refused; Copilot and `grid_overview` see only the shared rows; Office edit, after S4.

## ebilling 088 input (ontology review, 2026-10-22)

- **Constraint for 191 (and ebilling 086).** The External Collaborator role gets **no** privilege on `sprk_storedfile`.
- **Why it matters.** One flagged-external user on dev sits in the "Spaarke" BU, which is in 088's reader-profile BU set. That gives column read only, with no table privilege, the same exposure that already exists on the secured `sprk_document` columns.
- **What 191 checks.** It keeps the table privilege at none, and its role test (T191) asserts that.

## D-185 input (owner via ontology and ebilling, 2026-10-23)

- **Decision.** Only billing roles may read rule sets and rules directly in Dataverse (#2285).
  - ebilling sets `prvReadsprk_ruleset` to None on Spaarke Basic User and Core User.
  - Ontology removes `prvReadsprk_Policy` from Basic and Console User, and `prvReadsprk_PolicyVersion` from Console User, after its task 163.
- **Constraint for 191.** The External Collaborator role gets **no** privilege on `sprk_ruleset`, `sprk_policy` or `sprk_policyversion`, and T191's role test asserts it.
- **Not 191's scope.** 191 creates no billing-admin role. Granting explicit Read on those three tables to the billing-admin role or roles belongs to whoever creates them, which is ebilling. Today the billing admins are System Administrators.
- **Depth correction (2026-10-23, after ontology's #1911 review K1).** The input above originally said "org depth, O-6" for all three tables. That is right for `sprk_ruleset`, which has no matter lookup, but wrong for `sprk_policy` and `sprk_policyversion`.
  - Under round 102 item 4 (D-123), a matter-scoped rule and its versions are re-owned into the secure BU when the matter turns Secure, and Global Read always reaches that BU.
  - UAC's recommendation is therefore Deep on those two tables. It is assigned only to users whose BU is not an ancestor of the secure BU.
  - ebilling takes this to the owner. Nobody holds the role on dev (read-only check, 2026-10-23).
- **UAC impact.** No UAC surface reads those tables as the caller. `SecureChildLineage` and `RecordOwnershipResolver` read them app-only.

## Inputs from the ebilling 086 review (2026-10-28): `sprk_storedfile`

- **The share mirror gives Read only on `sprk_storedfile`:** no Write, Delete, Share, Assign or AppendTo. StoredFileService is the only writer, and a model-driven-app Delete would orphan the blob and destroy invoice evidence. This needs a per-table cap on `ChildMirrorMask`, with a must-fire test. Any role that 191 grants on the table gets Read only: the role grant is the control, and the mirror cap is defence in depth.
- **Inline mirror (086 K1):** StoredFileService does not call `SyncChildAsync` after create, so a Secure-team-owned file gets its root's sharees on the next 2-minute reconcile. Decide whether to add the inline mirror when a role first grants Stored File privileges.
- **Secure-held source copies (086 K5):** a copy of a document held by a Secure matter, attached to an ordinary invoice, is owned by the invoice's ordinary team (citation rule). Its bytes stay protected by the copy rule, but once Read on the table is granted to BU-depth roles, its name, size and hash become visible in the model-driven app. Decide before granting.
