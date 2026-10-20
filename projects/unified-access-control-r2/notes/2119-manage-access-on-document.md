# #2119 — Manage Access on the Document record (read-only design check)

> 2026-10-20 · read against `origin/master` @ `2b07480ab` · no code changed · paths below are on master

## Summary for the owner

1. A document has no access of its own. It gets its access from the matter, project or work assignment it is filed under, and the server re-copies that every 2 minutes.
2. The "Access Permission: Standard" field on the document is only a display copy. It shows the Access Permission value and has no Secure value, so on a Secure matter it reads "Standard". That is the bug.
3. Manage Access (the person icon) today works only on matters, projects and work assignments. Every server route it calls accepts only those three record types.
4. An **editable** Manage Access on a document would add people the system then removes, or would grant access that nothing checks. It would create a second source of truth.
5. Recommended: a **read-only "inherited" Manage Access** on the document. It shows Secure / Restricted / Limited / Standard, names the matter the access comes from with a link, and offers "Manage access on {matter}". It replaces the misleading field.
6. This needs one new read-only server route and a read-only mode in the existing modal. The same build then works for to-dos, events and communications.

## 1. What `TrackingFieldTrio` + `AccessGrantModal` support today

- **Tables.** The root map has exactly three entries, at `src/client/pcf/TrackingFieldTrio/index.ts:391-395`: `sprk_project`→`project`, `sprk_matter`→`matter`, `sprk_workassignment`→`workassignment`. The modal's type is the same closed set: `ExternalGrantRootType = 'project' | 'matter' | 'workassignment'` (`AccessGrantModal/types.ts:207`). The BFF parser accepts only those three and rejects anything else (`Infrastructure/ExternalAccess/ExternalGrantRoot.cs:8-13`, `:97-120`).
- **How the record type is passed.** `getHostEntity()` reads `context.page.entityTypeName`, falling back to `sprk_project` (`index.ts:804-807`). `resolveGrantRoot()` maps it and falls back to **project** for any other table (`index.ts:810-812`). The modal receives `recordId` and `recordType: this.resolveGrantRoot().recordType` (`index.ts:1665-1677`). It sends `{recordType, recordId}` on every call.
- **Calls made by the host PCF.**
  - `GET /api/v1/external-access/can-manage-access`: Write+Share gate, plus `followsParents` / floor (`index.ts:983`).
  - `GET /record-grants` (`index.ts:1194-1200`).
  - `GET /api/v1/records/{table}/{id}/no-access`, for the status indicator. It is asked only for the three roots (`index.ts:626-641`).
  - `retrieveRecord(host, id, '$select=sprk_issecure')`, the Secure flag (`index.ts:670-704`), and the owner/BU read (`index.ts:1417`).
- **Calls made by the modal itself.**
  - Grants: `/grant`, `/invite-and-grant`, `/revoke`.
  - User shares: `/share-user`, `/unshare-user`, `/user-shares`.
  - Assigned-To: `/assigned-access` and `/dismiss`.
  - Expiry: `/set-record-share-expiry`.
  - The no-access read (`AccessGrantModal.tsx:431, 1312-1376, 1674-2190`).
  - Every `/external-access/*` route sits behind `DelegationRuleFilter`: Write AND Share on the record, as the caller (`AccessGrantModal.tsx:93-104`).
- **Make Secure is not part of this PCF.** `/provision-project` (transition `make-secure`) and `/unsecure-project` are called from the Access ribbon, `src/client/webresources/js/sprk_access_ribbon.js:1-30`, on the three root forms only. Their endpoints are at `ProvisionProjectEndpoint.cs:543` and `UnsecureProjectEndpoint.cs:163`.
- **There is no read-only or "view only" mode.**
  - When `canGrantAccess !== true`, the modal renders only "You do not have permission to grant or revoke access for this record." (`AccessGrantModal.tsx:2428-2431`). Its data load runs only when `open && canGrantAccess` (`:1564-1593`).
  - The nearest building blocks:
    - Read-only *inherited* share rows (`provenance: 'inherited'`, `:1323-1350`, task 158).
    - The parent bar "inherited from {parent}" with an open-parent link (`followsParents` / `accessFloor` / `onOpenParent`, `types.ts:419-438`; `index.ts:870-893`).
    - The server's effective-access state (`serverAccess`, task 174, `:1084-1112`).
  - These all assume the record *is* a root.
- **Forms carrying the PCF.** The main forms for these tables, under `src/dataverse/solutions/SpaarkeMaster/Entities/`:
  - Matter: `sprk_Matter/FormXml/main/{4fa382f2…}.xml:1300`. It binds `accessPermission` and has the title TRACKING.
  - Project: `sprk_Project/…/{5aa00242…}.xml:1195`.
  - Work Assignment: `sprk_WorkAssignment/…/{7e578eef…}.xml:451`.
  - To Do: `sprk_Todo/…/{eca59df4…}.xml:537`.
  - Event: `sprk_Event/…/{eaf22dcb…}.xml:614`.
  - **Document:** no main form carries it. "Document main form" (`sprk_Document/FormXml/main/{9088d6a4…}.xml:630`) has a plain choice control for `sprk_accesspermission` (`:27`) and the `sprk_accesspermission_inherited` OnLoad (`:619-624`). The "Information" form `{319381f0…}` has neither.
- **Side finding (on To Do and Event today).** On a To Do or Event the PCF falls back to the project root:
  - It asks `can-manage-access?recordType=project&recordId=<to-do id>`, so the person icon is always disabled.
  - It selects `sprk_issecure` on a table that has no such column, so the read fails and the state is Limited (`index.ts:685-689`).
  - Only `ensureAccessStatus` guards against non-root tables (`:626-630`).
  - The effect is cosmetic but misleading, and it is the same family as #2119. File it with the fix, or fix it as part of option (i).

## 2. The access model for a document

- **Derived, never its own.**
  - `docs/data-model/child-access-permission.md:15`: "No access decision reads a child table's `sprk_accesspermission`… A child's access comes from its parent root."
  - The document's filing lookups are the ONE map `SecureChildLineage` (`Services/Access/SecureChildLineage.cs`; listed at `child-access-permission.md:30`).
  - Ownership: on a Secure root, every child is owned by the Secure Record Owners team (write-path I-2, `DATAVERSE-WRITE-PATH-ARCHITECTURE.md:132`; `RecordOwnershipResolver`, `SecureChildReconciler`).
  - Internal shares: `SecureChildShareSynchronizer` keeps each Secure-team-owned child "shared with exactly the internal principals its secure root is shared with". "Every direct share on the child outside the mirror is revoked" (`SecureChildShareSynchronizer.cs:216-232`), every 2 minutes.
  - The displayed value: `ChildAccessPermissionReconciler` (I-16, `:146`). "A hand edit of a parented child… Reverted within one run" (`child-access-permission.md:54`).
- **Why it shows "Standard" on a Secure matter.**
  - The column holds only the Access Permission choice: Standard / Limited / Restricted (`child-access-permission.md:36`).
  - Secure is a separate flag (`sprk_issecure`), which exists only on the roots; `sprk_Document/Entity.xml` has no `sprk_issecure`.
  - The form library says only "Access permission is inherited from {parent}" for child tables (`sprk_accesspermission_inherited.js:197-207`). It adds "Secure is inherited…" only on work assignment and project forms (`:14-29`).
- **No per-document grants exist.**
  - `sprk_externalrecordaccess` binds exactly one typed root: project / matter / work assignment (`src/solutions/SpaarkeCore/entities/sprk_externalrecordaccess/entity-schema.md:46-48`).
  - The "Plane 2" document-approval columns were never built ("NOT PRESENT LIVE", `:62-69`).
  - The document share link from task 072 is **retired**: `POST /{documentId}/share-link` was removed on 2026-10-16 (`Api/FileAccessEndpoints.cs:32-35`).
- **Conflicts an editable Manage Access on a document would create:**
  - **"+ User" on a Secure-matter document.** `/share-user` cannot take a document today (parser rejects it). If it were widened, the share would be revoked by the synchronizer within about 2 minutes as "outside the mirror" (`SecureChildShareSynchronizer.cs:232`). The user sees it added, then silently gone.
  - **"+ User" on an ordinary document.** The share would stand, because nothing mirrors non-secure children. That makes a per-document POA grant that the matter's Manage Access never lists, that "Update Access", No Access enforcement and `/unsecure-project` don't know about, and that survives the matter becoming Restricted. That is a second source of truth and an over-share risk. It also breaks I-14's "no DIRECT share of an external user on a Restricted record" intent, one level down.
  - **"+ Contact" / "+ Organization".** There is no document lookup on `sprk_externalrecordaccess`, and the evaluator reads typed roots only (`ExternalParticipationService` / `AccessibleRecordSetService`). A document grant would need new schema and a new evaluator plane. Without that, it is a row the evaluator ignores.
  - **Pill edit.** Reverted by the reconciler within one run (I-16). The current form library already locks it.
  - **Make Secure on a document.** Meaningless: secure-ness follows the parent, and a child leaves isolation only through `/unsecure-project` on its root (I-2, `:132`, "c1").

## 3. Design options

### (i) Read-only "inherited" Manage Access on the document — recommended

- **Shows:**
  - The effective Secure / Restricted / Limited / Standard.
  - The top-of-filing root or roots it comes from, as named links.
  - For a caller who may manage the root, who has access, read from the root.
  - "Manage access on {parent}", which opens the parent form; Manage Access opens there.
  - Remove the plain `sprk_accesspermission` control from the Document main form, or keep it hidden as data.
- **Server.**
  - **No existing endpoint answers this for a child.** `can-manage-access`, `record-grants` and `user-shares` parse roots only (`ExternalGrantRoot.cs:97-120`). The `no-access` read is three fixed routes (`RecordNoAccessEndpoint.cs:53-55`).
  - Add one read-only route by extending `RecordNoAccessEndpoint` with a `sprk_document` (and later child-table) route, or a sibling `GET /api/v1/records/{childTable}/{id}/effective-access`. It is Read-gated as the caller, like the no-access route, and returns:
    - `secure` (from the roots above, via `SecureChildShareSynchronizer.SecureRootsAboveAsync`, `:665`).
    - `accessPermission` (`ParentLineageWalk` + `InheritedAccessPermission`, `Services/Dataverse/ParentLineage.cs:132, 474`, the same rule I-16 uses).
    - `inheritedFrom[]` (root type, id, name).
    - `noAccess`.
    - `unknown` on any unreadable step (fail closed, as `EffectiveRootFlags` does).
  - It reuses the existing walks: no new write, no new store.
  - The people list stays behind the root's existing Write+Share gate. Owner O2 / `DelegationRuleFilter.cs:365`: "the Current Access list discloses who can reach a record." A Read-only document viewer sees the state and the parent, not names.
  - BFF hygiene §10/§11: one endpoint, justified by "the document shows the wrong state".
- **Client.**
  - Either a `readOnly`/`inherited` mode in `AccessGrantModal` that renders the parent bar, the state banner and an "open {parent}" action and **skips** every write affordance and the `canGrantAccess` gate, or a small `InheritedAccessPanel` in the shared library.
  - The `TrackingFieldTrio` host gets a child-table branch: no `can-manage-access`, no `sprk_issecure` read, pill read-only and showing "Secure" through the existing `secureAccessPermission` slot (`index.ts:1617`), and the person icon opening the read-only view. This also fixes the To Do / Event fallback in §1.
  - Add the PCF (Monitor / High Priority / Access) to the Document main form. `sprk_document` has `sprk_monitor` and `sprk_highpriority` (`Entity.xml`).
- **Risk.** Low. Read-only, fail-closed, no new access plane. The main risk is one more parent walk per form load; it is capped at 6 levels like the reconciler.
- **ADR / write path.**
  - Consistent: the server stays the one owner of the invariant; the client only displays (ADR-002 WP-2, `DATAVERSE-WRITE-PATH-ARCHITECTURE.md:17`).
  - I-2 / I-16 unchanged.
  - ADR-034 (A1/A4): the access-conferring registry stays on roots only, so documents gain no membership surface.
  - ADR-012: the modal stays entity-agnostic, because the host passes the parent.

### (ii) Fully editable Manage Access on the document

- **Work:**
  - Widen `ExternalGrantRootType` on the client and the server, plus `DelegationRuleFilter` targets.
  - Add a `sprk_document` lookup on `sprk_externalrecordaccess`, and teach the evaluator (`ExternalParticipationService`, `AccessibleRecordSetService`) per-document grants.
  - Exempt document shares from the mirror, or define a union rule.
  - Extend No Access / I-14 / unsecure / reconciliation to per-document rows, and add a new write-path invariant.
- **Risk.** High. It is a second source of truth (§2 conflicts), it over-shares on Secure and Restricted matters, and it is effectively a new project. It contradicts I-2 ("its children follow it"), I-16 ("no access decision reads a child's copy") and the shipped mirror rule (owner round 7 item 5: "never wider than the root's").
- **ADR / write path.** It needs ADR-034 and write-path amendments (CLAUDE.md §6.5 path B). Not recommended.

### (iii) Fix the field only

- **(a) A fourth "Secure" choice value.**
  - The choice is **global**, shared with the three roots (`child-access-permission.md:36`).
  - The roots' pill, the shared state mapping, the rank rule (Restricted > Limited > Standard) and every reader would gain an unknown value, mixing two independent axes. Secure is a flag, and a Secure record can also be Restricted.
  - Not recommended.
- **(b) Form-script override.**
  - Extend `sprk_accesspermission_inherited.js` child-table branch to read the top root's `sprk_issecure` and show "Secure (inherited from {parent})" as a form notification, keeping the field.
  - Small: one web resource plus its tests. It reuses the same parent reads the library already does for work assignments and projects.
  - It must stay pinned to the server walk (`ParentLineageTests`), and it reads only the direct parents, so a document under a to-do under a Secure matter needs a multi-hop client walk.
  - It shows no "who has access" and no path to Manage Access.
  - Acceptable as an **interim** stopgap only.

## 4. Other child tables

- `sprk_todo`, `sprk_event` and `sprk_communication` carry the same display column (`child-access-permission.md:41-43`).
  - To Do and Event already host `TrackingFieldTrio`, with the broken fallback in §1.
  - Communication shows a plain control on the "Message main form" (`:63`).
- **`sprk_memo` has no `sprk_accesspermission`** (`sprk_Memo/Entity.xml`: 0 matches), so memos need nothing for this issue.
- Option (i) extends to the other tables without new logic:
  - The server route is keyed by `{childTable}` over the same `SecureChildLineage` map, so adding a table is one route registration plus a test.
  - The PCF child-table branch keys off "host is not one of the three roots" instead of falling back to project.
  - Communications would add the PCF to the message form, or reuse the read-only panel alone. Owner round 81 chose no `TrackingFieldTrio` on Communication; re-confirm that.

## 5. Recommendation

Build **option (i)**: a read-only, inherited Manage Access on the Document main form, then on To Do, Event and Communication.

- The access model is deliberately derived: the root decides, and the reconciler and the share mirror copy it down every 2 minutes. An editable surface on a child would either be silently undone (shares on Secure children, pill edits) or create grants outside every enforcement path (shares on ordinary children, contact grants the evaluator never reads). That is exactly the second source of truth the write-path rule forbids.
- Option (i) answers the owner's real need: the document says "Secure, from Matter X", shows who has access when the caller may see that, and offers one click to "Manage access on Matter X".
- It reuses the modal's existing inherited and parent-bar pieces, the existing walks, and one new Read-gated route. It also fixes the To Do / Event root fallback the PCF has today.
- If a fix is wanted before that ships, option (iii-b), the form-notification override, is a safe stopgap. Do not add a "Secure" value to the global choice.
