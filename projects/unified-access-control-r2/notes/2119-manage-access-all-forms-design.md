# #2119 / #2120 — Manage Access on every record form (read-only design check)

> 2026-10-20 · read against `origin/master` @ `2b07480ab` · no code changed · paths below are on master
> Follows `2119-manage-access-on-document.md` (that note: a read-only inherited mode for filed child records).
> Owner direction (round 108): "use the manage access PCF on all record forms so that it is consistent; … Document, To Do,
> Event do not require a parent so they will need to have their own manage access functionality. we do not want to over
> complicate it".

## Summary for the owner

1. **One fact changes the picture.** Only a **Secure** parent passes its access down. Under a Secure matter, project or work
   assignment, the record is owned by the Secure Record Owners team and the server keeps it shared with exactly the
   parent's people. Under any **other** parent, nothing is passed down except the Access Permission *label*: the parent's
   shares never reach the record (`SecureChildShareSynchronizer.cs:225-226`, "Children of non-secure roots are never
   touched"). On dev, a person shared on an ordinary matter gets 403 on its documents when they sit in another business
   unit (`notes/batch5-live-gates-2026-10-08.md:308`, "by design").
2. So the real line is not "has a parent / has none". It is **"does a Secure record decide this record's shares?"**
   - **Yes:** the record is *Inherited*. Read-only. Its access is managed on the Secure record.
   - **No:** the record is *Own*, whether it stands alone or is filed under an ordinary record. The only access control
     that exists on it today is its owner, role depth and its own direct shares. Those are what Manage Access should edit.
3. **What "Own" can offer is small:** internal users only (add, change level, remove).
   - No Access does nothing on a record that is not Secure (owner Q4).
   - Contact grants exist only on matter, project and work assignment.
   - A standalone record's own Restricted or Limited is a label that nothing enforces (owner 2026-10-08: "fine, revisit later").
   - The panel says each of these in one line instead of offering a control that does nothing.
4. **Server work:**
   - one read (extend the existing per-record `no-access` route to document, to-do and event);
   - the three existing share routes accept those tables, refusing when a Secure record decides the shares.
   - No new store, schema or job.
5. **Transitions already behave safely,** with one choice for the owner:
   - Under a Secure record, the record's own shares are replaced by the Secure record's (this is what happens today).
   - Under an ordinary record, its own shares stay (owner round 22). They stay visible and editable in Own mode.
6. **#2120** (the To Do / Event icon is always disabled and shows "Limited") is fixed by the first client task of this
   design: the PCF stops treating every unknown table as a project.

## 1. Facts

### 1.1 Which tables can stand alone, and what decides their access

Every filing lookup on the four tables is optional (`RequiredLevel none`):
- Document: `sprk_Document/Entity.xml:4171-4175` (`sprk_Matter`), `:4383-4387` (`sprk_ParentDocument`), `:4542-4546` (`sprk_Project`).
- To Do: `sprk_Todo/Entity.xml:1608-1612` and its other `sprk_Regarding*` columns.
- Event: `sprk_Event/Entity.xml:3424-3428`.
- Communication: `sprk_Communication/Entity.xml:1121-1125` (thread), `:2472-2476`.

What counts as "filed" for access is the ONE map `SecureChildLineage` minus `ParentLineage.NotFiling`
(`docs/data-model/child-access-permission.md:21-32`). Contacts, organizations, accounts and service requests are **not**
filing (`:32`; `SecureChildLineage.cs:24-27, 51-55`).

| Table | Standalone? | What decides a standalone record's access today | Reconciler / share mirror on a standalone record | PCF today |
|---|---|---|---|---|
| Document | Yes | **Owner:** the acting user's business-unit default team for BFF creates (`RecordOwnershipResolver.cs:157-166`, I-6). **Role depth:** "Spaarke Basic User" holds Read at Deep (`notes/task-062-nfr05-role-depth.md`, finding A). **Direct POA shares:** made only with the platform Share button, because no BFF route takes a document. **No Access:** inert (§1.2). **Own `sprk_accesspermission`:** set by the user, "recorded only" (`child-access-permission.md:13, 15`). **File reads:** follow Dataverse Read on the `sprk_document` row (`Api/FileAccessEndpoints.cs:21-24`). | **Left alone.** The mirror touches only Secure-team-owned rows (`SecureChildShareSynchronizer.cs:221-226`). I-16 never overwrites a parentless row (`child-access-permission.md:13`; write-path I-16, `DATAVERSE-WRITE-PATH-ARCHITECTURE.md:146`). | Not on any Document form; plain choice control (prior note §1) |
| To Do (`sprk_todo`) | Yes ("personal To Do", D-134: `Services/Signals/PersonalToDoAssignee.cs:3-6`) | Same as Document | Left alone (same refs) | On the main form (`sprk_Todo/FormXml/main/{eca59df4…}.xml:537`), broken (#2120) |
| Event (`sprk_event`) | Yes | Same as Document | Left alone | On the main form (`sprk_Event/FormXml/main/{eaf22dcb…}.xml:614`), broken (#2120) |
| Communication | Yes | **Owner:** the creator (E1, owner round 10 item 8: `RecordOwnershipResolver.cs:168-178`). **Direct-thread messages:** per-participant Read shares by `DirectThreadAccessService` (`SecureChildShareSynchronizer.cs:6-8`). | Left alone | None (owner round 81, `child-access-permission.md:63`) |
| Work assignment / Project | Roots that may be filed (I-17 floor) | Their own Manage Access (unchanged) | n/a | On their forms; working |
| Memo | — | No `sprk_accesspermission` (prior note §4) | — | Out of scope |

**Filed under an ordinary record.**
- A BFF re-file moves the owner to the parent's business-unit team (`RecordOwnershipResolver.cs:121-125`, "resolves through the same rule as a create").
- A re-file outside the BFF (a model-driven lookup edit) keeps the owner: the reconciler moves rows only across the isolation boundary (`SecureChildReconciler.cs:316-318`).
- In both cases the parent's shares do not reach the record (point 1 above).

### 1.2 What Manage Access can target today

| Capability | Tables | Evidence |
|---|---|---|
| Internal shares (`/share-user`, `/unshare-user`, `/user-shares`) | Project, matter, work assignment only | Handler: `InternalShareEndpoints.cs:298` → `GrantExternalAccessEndpoint.ResolveExplicitRoot` (`GrantExternalAccessEndpoint.cs:1362-1373`, "must be one of: project, matter, workassignment"). Gate: `DelegationRuleFilter.cs:345-352`. Parser: `ExternalGrantRoot.cs:97-118`. |
| `can-manage-access`, `record-grants` | Roots only | `DelegationRuleFilter.cs:361-362, 369-370` |
| No Access entries | **Stored** against any record id (`sprk_noaccessentry/entity-schema.md:79-80`), but **in force** for internal users only on Secure records (`SecureShareNoAccessGuard.cs:104-106`) | The enforcer removes shares only on Secure roots; a child is `not-a-secure-root` (`NoAccessShareEnforcer.cs:48, 588-595`). The per-record status route exists for the three roots only (`RecordNoAccessEndpoint.cs:53-55`). A standalone record is never Secure, so an entry on it does nothing internally. |
| Contact grants (`sprk_externalrecordaccess`) | Project, matter, work assignment only | `ExternalGrantRoot.cs:8-13`; `sprk_externalrecordaccess/entity-schema.md:46-48`. The evaluator's candidates are roots (`AccessibleRecordSetService.cs`, veto pipeline). **A standalone document cannot get a contact grant.** |
| Restricted / Limited | **Enforced on roots** (I-14, `DATAVERSE-WRITE-PATH-ARCHITECTURE.md:144`; `InternalShareEndpoints.ClassifyEligibility`) | A child's value is display only (`child-access-permission.md:15`). A standalone child's own value is recorded only (same line). |
| Secure | Roots only (Make Secure / Remove Secure through the Access ribbon) | A child is Secure only by being filed under a Secure record (I-2, `DATAVERSE-WRITE-PATH-ARCHITECTURE.md:132`) |

### 1.3 Transitions today

- **(a) Standalone with its own shares, filed under a parent.**
  - **Secure parent: replaced.** A BFF re-file runs `AfterRefileAsync` → `SyncChildAsync` inline (`SecureChildShareSynchronizer.cs:357-389`). Any other path takes up to about 2 minutes (recent-changes pass + share job, write-path I-2). The mirror "revokes every direct share on the child outside the mirror" (`:231-232`).
  - **Ordinary parent: kept.** "A share on a never-isolated row is its user's own intent and is kept" (owner round 22, `:362-364`).
  - Not a leak in the access sense: the record was shared on purpose, and the parent's shares never cover it anyway. But today the kept share is visible only in the platform Share dialog.
  - Edge: an ordinary **Restricted** parent does not remove a direct share held by a user flagged external on the child. I-14 covers roots only. This is the same class as the limit the owner accepted in round 77 (role depth lets such a user read a non-Secure Restricted record, `:144`).
- **(b) Filed, then un-filed.**
  - **From Secure, through the BFF.** Needs F3 (Full Access on the root, or the creator) (c1, `:132`). The mirrored shares are removed in the same request (`:362-364, 399-430`) and the owner moves to an ordinary team. Result: standalone, no shares.
  - **From Secure, outside the BFF.** The row stays Secure-team-owned. The mirror stops touching it ("outside every secure record", `ChildrenOutsideSecureRoots`, `:64-65`). It is stuck isolated until an F3 holder acts. Call this state **Held**.
  - **From an ordinary parent.** Shares unchanged; the label keeps its last value and becomes editable (`child-access-permission.md:17`).
- **(c) Filed under a Secure parent.**
  - Re-owned into isolation (`RecordOwnershipResolver`, task 146/148) and mirrored, as in (a).
  - A platform Share made on it later is revoked within about 2 minutes.
  - A share on it now sits outside every enforcement path.
- **(d) Standalone with its own Restricted or Limited.**
  - The value exists and the user may edit it. Nothing enforces it (`child-access-permission.md:15`).
  - If the record is then filed, I-16 overwrites it with the parent's value within one run.

### 1.4 Model-driven parity: the platform Share button vs the PCF

The platform Share button is available today on Document, To Do and Event forms: their `RibbonDiff.xml` hides no Share command. The Restricted hide rule `isShareAllowed` exists for the roots only (`sprk_access_ribbon.js:300-323`; I-14).

Why prefer the PCF on a record whose shares are its own:
- **Consistency.** The same panel on every form.
- **Levels.** View / Collaborate / Full Access, never Assign (`InternalShareEndpoints.cs:56-60`). The platform dialog can grant Assign and Share separately.
- **"You may grant only what you hold".** Re-probed as the caller (`:62-66`).
- **Eligibility.** Enabled persons only, no application users (`:46-55`).
- **Restricted.** Refusal of users flagged external (for a standalone label, see D3).
- **In-app notification.** `GrantAccessNotifier`, task 181.
- **Mode awareness.** The PCF can refuse a share on a record whose shares a Secure record decides. The platform button cannot: on such a record its share is silently undone within 2 minutes.

**No Access is not a reason.** It does not bind non-Secure records (Q4), so the PCF adds no enforcement there.

**Audit is mixed.**
- BFF shares are written app-only. Dataverse audit (Share 14 / Modify 48 / Unshare 49, when auditing is on) names the BFF application user.
- The person is recorded in the BFF log (`[USER-SHARE]`, caller oid).
- The platform button audits the person directly.
- This is the same as Manage Access on roots today.

## 2. Design

### 2.1 Mode selection (computed by the server, one rule)

For a record of a child table (document, to-do, event), the server computes:

| Mode | When | Editable |
|---|---|---|
| **Inherited** | It is owned by the Secure Record Owners team AND `SecureRootsAboveAsync` (`SecureChildShareSynchronizer.cs:665`) names at least one Secure record. Or, before the job catches up, a Secure record is above it. | No |
| **Held** | It is owned by the Secure Record Owners team, but no Secure record is above it (un-filed outside the BFF) | No. Text: "This record is still isolated after leaving a Secure record. An administrator must release it." |
| **Own** | Not isolated and no Secure record above, whether standalone or filed under ordinary records | Internal users: yes. Label: only when standalone. |
| **Unknown** | The lineage walk is Undetermined (unreadable, missing, looping or deeper than 6), or the owner or flag cannot be read | No (fail closed, ADR-003) |

Roots (project, matter, work assignment) keep exactly today's behaviour, including the I-17 floor. A table the PCF does not
know gets **no** access affordance: no project fallback.

**What the PCF shows**

| | Document | To Do | Event |
|---|---|---|---|
| **Pill** | Secure / Restricted / Limited / Standard from the server's effective value. "Secure" uses the existing `secureAccessPermission` slot (`TrackingFieldTrio/index.ts:1617`). Locked when the record has a parent (the form library already does this). | Same | Same |
| **Inherited** | "Secure — access comes from {root}" (each root linked), "Manage access on {root}". To a caller with Write+Share on this record: the names, which are this record's own direct shares, i.e. exactly the mirror, labelled "from {root}". Otherwise: state and link only. | Same | Same |
| **Own** | "+ User", the level, Remove (existing rows). One-line notes in place of: "+ Contact / + Organization" ("People outside your organization get access through a matter, project or work assignment. File this record under one to share it with them."); No Access ("No Access lists apply to Secure records."); Make Secure ("A record becomes Secure by being filed under a Secure record."); expiry (not offered). When filed under an ordinary record: "Its Access Permission follows {parent}. People shared on {parent} do not get this record — share it here." | Same | Same |

Communication: out of scope (owner round 81, and the thread service owns its shares). See D5.

### 2.2 Server surface

**S1 — extend the per-record read** to `GET /api/v1/records/{sprk_document|sprk_todo|sprk_event}/{id}/no-access`, on the same group as `RecordNoAccessEndpoint`, with the same `RecordRouteAccessAuthorizationFilter` (Read, as the caller, uniform 404).

- It adds an `access` block:
  - `mode` (inherited / held / own / unknown);
  - `secure`;
  - `accessPermission` (effective, from `ParentLineageWalk` + `InheritedAccessPermission`, `ParentLineage.cs:132, 474`; own value when parentless);
  - `inheritedFrom[]` (Secure roots, or the ordinary tops for the label: type, id, name);
  - `owner` (team or user name);
  - `canManage` (Write AND Share from the SAME probe answer the filter already publishes, AND mode = own).
- `noAccess` answers `doesNotApply` with entries `notShown` for these tables.
- The three answers:
  - **Existing:** `RecordNoAccessEndpoint` (roots only, `:53-55`); `can-manage-access` (roots only); the walks `ParentLineageWalk`, `SecureRootsAboveAsync`, `IsSecureTeamOwnedAsync` (`:337`).
  - **Extension:** three route registrations on the existing endpoint class, plus one handler branch composing those existing reads. No new endpoint family, store or cache.
  - **Cost of doing nothing:** the PCF on To Do / Event keeps asking about a "project" with the to-do's id, so the icon is always disabled and the state reads "Limited" (#2120). A Document under a Secure matter keeps showing "Standard" (#2119). The client cannot choose a mode without re-implementing the 6-level server walk.

**S2 — widen the three existing share routes** (`/share-user`, `/unshare-user`, `/user-shares`) and their `DelegationRuleFilter` cases to `recordType` ∈ {`document`, `todo`, `event`}. Root parsing is unchanged; a child target is a second resolution branch.

- **Child branch, share and unshare:**
  1. Read the mode (S1's rule, app-only). Anything but **own** → 409 `sdap.access.user_share.access_decided_by_parent`, naming the Secure record, or `held` / `unknown`. Write nothing.
  2. Eligibility through `ClassifyEligibility` with `rootIsRestricted` = the record's effective Restricted (filed: the top's; standalone: per D3).
  3. No No Access guard call (Q4: not Secure, proven in step 1).
  4. No last-reader rule, no `SyncRootAsync`, no Assigned-To ledger, no root-set cache invalidation (root-only concerns).
  5. Notification through `GrantAccessNotifier` with the record's label (D6).
- **`/user-shares` on a child:** allowed in every mode (Write+Share as the caller). For an Inherited record, items carry `inheritedFrom` = the Secure roots.
- The three answers:
  - **Existing:** `InternalShareEndpoints` is the one internal-share surface; `DelegationRuleFilter` is the one gate.
  - **Extension:** a target branch in both. No new route.
  - **Cost of doing nothing:** a standalone or ordinarily-filed document, to-do or event can be shared only through the platform button. That bypasses levels, eligibility and notifications, and the PCF on those forms has nothing to edit, so the owner's "same Manage Access on every form" is not met.

Nothing else is new: no schema, no job, no write-path invariant. The mirror (I-2) already enforces the Inherited side.

### 2.3 Transition rules

| Case | Rule | Work |
|---|---|---|
| (a) Standalone → filed under a **Secure** record | The Secure record's access **replaces** the record's own shares (today's behaviour: inline for BFF re-files, at most about 2 minutes otherwise). | Copy only. Own-mode panel: "If you file this under a Secure record, these shares are replaced by that record's." |
| (a) Standalone → filed under an **ordinary** record | Own shares **stay** (owner round 22) and stay visible and editable in Own mode. The label follows the parent (I-16). | None (D2 offers "replace") |
| (b) Secure-filed → un-filed through the BFF | Needs F3. The mirror goes, the owner becomes ordinary, mode becomes Own with no shares. | None |
| (b) Secure-filed → un-filed outside the BFF | **Held:** read-only with the administrator message. | S1 reports `held` |
| (b) Ordinary-filed → un-filed | No change. The label becomes editable. | None |
| (c) Filed under a Secure record | Inherited. S2 refuses edits with 409. The PCF shows the link to the root. | S1 + S2 guard |
| (d) Standalone own Restricted / Limited | A label. The panel says "On a record that is not filed, Access Permission is a label: it does not change who can open it." D3 decides whether share-time refusal of users flagged external applies. | Copy + D3 |

**Race.** A share written just before the record is filed under a Secure record lasts at most until the mirror runs
(about 2 minutes). Fail-safe direction: the mirror revokes everything outside the root's set.

### 2.4 External contacts on a standalone record

Not in scope. There is no grant target (`ExternalGrantRoot.cs:8-13`) and the evaluator reads roots only. The user sees no
"+ Contact" / "+ Organization", and instead: "People outside your organization get access through a matter, project or
work assignment. File this record under one to share it with them."

### 2.5 #2120 inside this design

T2 below is the fix:
- `TrackingFieldTrio` classifies its host as root / child / unknown.
- It deletes the project fallback (`index.ts:811-813`).
- On a child it skips the `can-manage-access?recordType=project` call (`:983`) and the `sprk_issecure` read (`:685-689`).
- It takes the state, the pill's Secure and the icon's enablement from S1.

Result:
- Inherited records open the read-only view.
- Own records enable the icon when `canManage` is true.
- An unknown table shows no icon.

## 3. Build order

**T1 — S1 per-record read for document, to-do and event (BFF).**
- AC: For each mode, a fixture returns the right `mode`, `secure`, `accessPermission`, `inheritedFrom` and `canManage`.
- AC: A To Do under a parentless Document under nothing is `own`, with the label taken from the Document.
- AC: A To Do regarding only a contact or a service request is `own` (not filing).
- Negative: a caller without Read gets 404 (also for a missing record).
- Negative: an unreadable lineage step gives `unknown`, never `own`.
- Negative: a Secure-team-owned row with no Secure root gives `held`.
- Negative: `canManage` is false whenever mode ≠ own, even for Write+Share callers.
- Negative: a root route's answer is byte-identical to today's.

**T2 — PCF host classification + read-only Inherited view (fixes #2120 and the #2119 display).**
- AC: A To Do under a Secure matter shows "Secure", and the icon opens the read-only view naming the matter, with a working "Manage access on {matter}".
- AC: A standalone To Do shows its own label.
- Negative: no request with `recordType=project` is sent from a To Do, Event or Document form.
- Negative: no `sprk_issecure` read on a child table.
- Negative: an S1 failure shows "Access status unavailable" with no write affordance.
- Negative: an unknown host table renders no access icon.
- Negative: root forms are unchanged (existing tests stay green).

**T3 — S2 share routes accept own-mode child records (BFF).**
- AC: share, change level, unshare and list on a standalone and on an ordinarily-filed document, each read back.
- AC: a notification is sent (D6).
- Negative: an Inherited, Held or Unknown record gets 409 with nothing written.
- Negative: a disabled or application user gets the existing refusal.
- Negative: without Write+Share on the record, 403 (filter).
- Negative: an effective-Restricted target with a user flagged external is refused (filed: always; standalone: per D3).
- Negative: an unknown `recordType` is still 400/403.
- Negative: root behaviour is unchanged (existing suites).

**T4 — Own mode in AccessGrantModal + names in Inherited.**
- AC: Own shows "+ User", level and Remove only, with the four one-line notes from §2.1. Inherited lists the mirror rows read-only, labelled "from {root}", to Write+Share callers.
- Negative: there is no "+ Contact", No Access, Make Secure or expiry control in Own or Inherited mode.
- Negative: a 409 from S2 refreshes the view into the read-only mode.
- Negative: a Read-only caller sees no names.

**T5 — Document form.**
- Add `TrackingFieldTrio` (monitor, highPriority, accessPermission; the columns exist: `sprk_Document/Entity.xml:3472, 4329`) to the Document main form.
- Remove the plain `sprk_accesspermission` control (`{9088d6a4…}.xml:27`). Keep the OnLoad lock.
- Optional, per D4: hide the platform Share on isolated child records.
- AC: the Document form behaves as To Do does after T2–T4.
- Negative: the form library still locks the pill on a filed document.
- Negative: no second Access Permission control remains.

**T6 — live check on dev.** Run the matrix as admin and testuser1:

| Mode | Document | To Do | Event |
|---|---|---|---|
| Standalone | | | |
| Ordinary-filed | | | |
| Secure-filed | | | |
| Held | | | |

It includes transition (a) under both a Secure and an ordinary matter, and (b) through the BFF.

## 4. Risks

- **R1 — ordinary parents do not pass shares down** (existing, by design). The copy must never imply they do; §2.1 text.
- **R2 — Office edit.** A person shared across business units on a document can read and preview it through the BFF. Whether Office desktop edit works without a container role in a business-unit container is unverified (`FileAccessEndpoints.cs:26-28`). Verify in T6.
- **R3 — audit.** BFF shares appear in the Dataverse audit as the BFF application user (§1.4). Same as roots today.
- **R4 — Restricted ordinary parent:** a direct share on the child held by a user flagged external is not removed (I-14 is roots-only; round-77 class).
- **R5 — two "filed" definitions.** `PersonalToDoAssignee` counts a contact/organization regarding as filed. The access map does not. The mode follows the access map.
- **R6 — cost:** one lineage walk (at most 6 levels) per form load of a child table.

## 5. Owner decisions

- **D1 — mode rule.**
  - Recommended: "a Secure record decides" (§2.1).
  - Alternative: literal "has a parent". Ordinary-filed records then become read-only too. Cost: the PCF can then give no cross-business-unit colleague a document of an ordinary matter; only the platform button can.
- **D2 — filing under an ordinary record.**
  - Recommended: keep the record's own shares (round 22).
  - Alternative: replace them always. This needs a standing "filed records carry no direct shares" rule and hiding the platform Share button, and it removes today's only cross-business-unit path to such records.
- **D3 — a standalone record labelled Restricted:** should S2 refuse a user flagged external?
  - Recommended: yes, at share time only (one existing predicate; no remover).
- **D4 — the platform Share button on child forms.**
  - Recommended: hide it on isolated records now (it is undone there within 2 minutes).
  - Decide "hide everywhere" after Own mode ships.
- **D5 — Communication:** stays out (round 81; thread-owned shares). Confirm.
- **D6 — notifications** for shares on these records: same in-app notification as roots. Recommended: yes.
- **D7 — a document can narrow its partner visibility (#2126 / R3 ISS-015).** **Proposed; awaiting the owner.** See §6.

## 6. D7: internal-only documents (#2126, owner direction 2026-10-20, relayed by R3)

**Owner direction.** When an email is internal-only, every document saved from it (the .eml and its attachments) is internal too. The same holds under Secure records. The owner wants this handled with #2119, not as a separate R3 column.

**Why #2119 alone does not do it.** §1.2: Restricted / Limited is enforced on roots only (I-14); a filed child's value is display only, and partners reach documents through root grants + `ExternalMultiRootRule`. Today only task 078 hides email documents, and only in the AI chat tools (`_sprk_relatedcommunication_value` → the 065 rule).

### 6.1 Rule (amends I-14: a child may narrow, never widen)

A document is visible to an external caller only when **all** of these hold:

1. **Root grants allow it.** Unchanged.
2. **Its own Access Permission is not Restricted.** The column exists on `sprk_document`; T5 puts `TrackingFieldTrio` on the form. Because visibility is "root AND own", an own value wider than the root has no effect, so no "never widen" validation is needed. T5's negative criterion changes: on a filed document the pill may be set to Restricted.
3. **It does not come from an internal-only or non-Open communication.** This is derived at read time, never stamped.
   - The document's communication is `_sprk_relatedcommunication_value`. When that is empty, the parent document's communication (`ParentDocumentLookup`) is used, one hop, then the grandparent's. The walk is bounded at 3 hops; a cycle or a longer chain fails closed.
   - The communication is visible only if the 065 rule says so (`ExternalCallerCommunicationGuard.ReadVisibleCommunicationIdsAsync`): `sprk_isinternalonly` reads JSON `false`, a thread is set, and `CommunicationOpenThreadGate` confirms the thread is Open. The rule is reused, not copied.
   - **Fail closed:** an unreadable flag, thread, or parent chain hides the document.

**Why derived and not stamped.** The invariant has one owner: `sprk_communication.sprk_isinternalonly`, whose only update is `CommunicationInternalOnlyFloor.LowerWhenSafeAsync`. No writer has to keep per-document copies in sync. Lowering the flag later makes the documents visible with nothing else written.

### 6.2 One predicate, every external document surface

- **Owner.** UAC owns one predicate in its external read path. The external document routes apply it: list, download, versions, preview, any SPE file route, and the AI chat tools. 078's rule moves onto this predicate, so there is no second copy.
- **Order.** The wiring follows 036 / PR #2078, which rewrites `ExternalProjectDataEndpoints`. R3 reviews the routes it owns.
- **Cache.** If a document-set cache is in play, an own Access Permission change and a communication flag change (`LowerWhenSafeAsync`) evict it. Otherwise the read is uncached. The task states which.

### 6.3 Writer gaps (R3's map, master c3ec5d6e5)

Three save paths can produce a document from an internal-only email with no communication link. Today all three are visible to partners and missed by the AI tools.

| Gap | Path | Closed by |
|---|---|---|
| 1 | Outlook add-in attachments of a saved email: child documents set only `ParentDocumentLookup` (`UploadFinalizationWorker.cs:1254/:1294`) | §6.1 rule 3's parent hop, **once gap 3 is fixed**: the hop reaches the add-in .eml document, which is exactly the one that may be unlinked. Until T8 lands, a child whose parent .eml missed its link stays visible. |
| 2 | Outlook add-in single-attachment save (`SaveContentType.Attachment`): no capture, no communication row, no link (`OfficeService.cs:506`, `OfficeDocumentPersistence.cs:795`) | **Recommended:** run the email capture, or the canonical lookup by InternetMessageId, for attachment saves too, and link the attachment to that communication. Only a save whose email still cannot be identified falls back to the writer rule: own Access Permission = Restricted, which fails closed; a user can widen it later only to the root's level (rule 2). **Trade-off if only the fallback ships:** the add-in runs no capture for `SaveContentType.Attachment`, so *every* single attachment saved from Outlook would become Restricted, including those from emails that are not internal-only, and partners would lose them until someone widens them. Owner: Office. |
| 3 | The add-in .eml whenever the best-effort canonical link misses (`OfficeDocumentPersistence.cs:790-842`; the link write is at `:840`, the null-services skip at `:793`; it misses on an empty InternetMessageId, a failed lookup, or null services) | The same writer rule as gap 2. Better still, link it to the communication that the add-in capture creates (`EmailUploadCaptureService`, flag true), so the link is never missing. Owner: Office. |

- Not email-related, so out of scope: the generic `POST /api/documents`, partner uploads (`ExternalDataService.cs:666`), the client `DocumentRecordService.ts`, and the AI, RAG and indexing updates.
- **Client writers:** a scan of `src/client` and `src/solutions` (2026-10-20) found only reads of `sprk_relatedcommunication`: `ReconciliationWorkspace.tsx:132` (a filter) and `sprk_accesspermission_inherited.js:121` (the parent map). word-add-in-r1 found that every BFF writer only *sets* it; `UpdateDocumentRequest` has no field for it.
- **Remaining hole: a model-driven edit.** A user with Write on a document could clear the lookup on the form. The document would then stop deriving "internal" from its email, which silently defeats the owner's rule. T7 checks whether the column is on any form or is editable. If it is, the column is secured the way the creator-person columns are (Read for every user, Create/Update for the BFF application user only; `scripts/Set-ChildRecordCreatorPersonSchema.ps1` reader/writer profile pattern), with a read-back.
- Re-file and set-regarding paths are untraced. They don't change the communication link, and the rule is computed at read time, so no gap is expected. The task confirms this.

### 6.4 Tasks (after the owner accepts D7)

- **T7 — predicate + wiring (UAC, after #2078).**
  - Must-fire tests, each on list, download, versions, preview and AI tools:
    - own Restricted;
    - linked to an internal-only communication;
    - linked to a communication on a non-Open thread;
    - an attachment whose parent .eml is linked to an internal-only communication;
    - an attachment whose parent .eml has no link (child → unlinked parent): visible until T8, so this test pins today's state and T8's test flips it;
    - an unreadable flag, which is hidden.
  - Must-not-fire tests:
    - linked to a visible communication;
    - no link anywhere in the chain and own Standard, which stays visible as today;
    - a 4-hop chain, which is hidden (bounded).
- **T7 additions (word-add-in-r1 task 130 finding, 2026-10-20).**
  - **T7a. The reconciler widens.** `ChildAccessPermissionReconciler` (task 173; owner rounds 81/84; `Services/Access/ChildAccessPermissionReconciler.cs:234-289` at origin/master 4f5f22694) writes the inherited value over every parented child's own value. So a filed document's own Restricted (T8's fallback, or a user's choice) is reset to its parent's value, which breaks rule 2 above.
    - The fix: the reconciler writes the more restrictive of inherited and the child's own narrowing, and never widens below it.
    - The stored value alone cannot tell a child's own Restricted from one inherited earlier. A parent that later widens must still widen an inherited Restricted. The own narrowing therefore needs its own BFF-written record: one column, or a reason on the row. T7 designs that with must-fire and must-not-fire tests:
      - own Restricted survives the parent being Standard;
      - an inherited Restricted follows a parent that widens back to Standard;
      - an unreadable own narrowing writes nothing.
  - **T7b. Legacy rows.** Documents saved through the three gap paths before T8 have no communication link. Rule 3 cannot see them, so they stay visible by root grants after T7. T7 needs a backfill that links them where the source email can be identified (through `sprk_communicationattachment`, or the .eml's message id; the Office owner confirms which) and reports the rest. Whether an unidentifiable legacy row becomes Restricted is an owner call, raised with T7.
    - **How to match (word-add-in-r1, from code, 2026-10-20):**
      - (1) `.eml` rows (`sprk_isemailarchive = true`): `sprk_document.sprk_emailmessageid` → `sprk_communication.sprk_internetmessageid`. Rows saved before task 121 hold the mailbox-specific Exchange item id rather than the RFC Message-ID (`OfficeEmailEnricher.ResolveStoredMessageId`), so they are unmatchable; the conversation-index + subject + date heuristic is NOT used, because a wrong match would expose. An email whose capture never created a communication has nothing to link to.
      - (2) Attachment children: not through `sprk_communicationattachment`, which the add-in never writes. Use `sprk_parentdocument` → the parent .eml → (1). The pre-121 caveat applies through `sprk_emailparentid`.
      - (3) Single attachment saves: no parent id on the row; the only trace is ProcessingJob `sprk_payload` `Attachment.ParentEmailId`. No shipped client is believed to use this path, so a dev count query confirms whether it is zero.
      - Everything unmatched is listed for the owner's call.
  - **Known limits (agreed with word-add-in-r1):**
    - The reverse path, `CrossPathLink.FindAndLinkArchiveDocumentsAsync`, links a document later and keeps its fallback Restricted, which fails closed. Lifting a fallback Restricted once a link exists needs T7a's own-narrowing record to carry its reason; that is an owner design call, not built now.
    - A byte-identical duplicate save returns the existing canonical document, which may be unlinked or linked to another email. The same bytes are already reachable under the canonical's own rule, so no new exposure.
- **T8 — Office writer rule (Office owner; UAC reviews).** Gaps 2 and 3: capture or look up the communication for attachment saves; make the .eml link non-best-effort; Restricted only when the email cannot be identified. Must-fire tests: the canonical-link miss; an attachment of an unidentifiable email; the chain child → parent .eml that missed its link, which is hidden after T8. Must-not-fire tests: a linked save keeps its own default; an attachment of an identified, non-internal-only email stays Standard.
- **ADR / architecture.** Amend I-14 in `DATAVERSE-WRITE-PATH-ARCHITECTURE.md` and the concise ADR-034 / auth constraint text: "a child's Access Permission may narrow partner visibility (Restricted hides it); it never widens it." Enforced by: T7's tests.
