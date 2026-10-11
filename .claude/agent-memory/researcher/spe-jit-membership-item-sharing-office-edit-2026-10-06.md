---
name: spe-jit-membership-item-sharing-office-edit-2026-10-06
description: SPE item-level sharing to non-members, container roles for Office edit, permission removal timing, app-only preview/createLink, container permission API markers + limits — input to UAC-r2 round-70 design (standing writer on BU containers + JIT on secure containers)
metadata:
  type: project
---

# SPE JIT membership vs item sharing for Office edit (2026-10-06)

**Question**: For UAC-r2 round 70 (app-only bytes; standing writer on BU containers; JIT container writer on secure containers for Office edit), what do SPE/Graph docs say about item-level sharing, roles, removal timing, app-only links/preview, and permission markers?

**Findings (VERIFIED-IN-DOCS unless noted)**:
- Item-level sharing to a NON-member is documented: "To share individual items without granting container access, use driveItem invite or permission create ... Sharing an item doesn't grant access to the container or to any other item" (configure-authentication-authorization, 2026-07-13). App-only invite works for existing users/guests, not new guests. Open model (default) = members/guests with edit can add file permissions; restrictive (`isSharingRestricted`) = only Owner/Manager. Whether restrictive also blocks APP-ONLY invite is undocumented. Conflicting older sentence in plan/authentication-permissions.md: "Any user accessing a container must be a member".
- Open-office-files prereq: "The user has permission to view or edit the file" (file, not container). Desktop item-only open = INFERRED, needs probe.
- `DELETE /containers/{id}/permissions/{id}` by DEFAULT also removes the identity's item-level permissions; `Prefer: onlyRemoveContainerScopedPermission` keeps them. List permissions `includeAllContainerUsers=true` shows item-only users. PATCH upsert up to 10 perms/request. POST conflictBehavior fail(409)/replace.
- permission resource has NO grantedBy/createdBy field (only link.application for links). Container customProperties: string value + isSearchable + isPatternToken; no documented size/count limit.
- Removal timing: Word/PPT web re-check CheckFileInfo ≤5 min, Excel RefreshLock ≤15 min; if the revoked user's token is used for PutFile, ALL co-authors are ejected (CSPP coauth doc — WOPI-host doc, SharePoint-as-host INFERRED same). CAE does not cover ACL changes; guests unsupported by CAE.
- **driveItem preview getUrl: "anyone who accesses the URL acts as the caller with the caller's permissions"** — app-only preview works for non-members but carries the APP's rights. CORRECTS the 2026-07-18 memo claim that preview binds to the viewer's session.
- Container-type settings readable app-only only via `GET containerTypeRegistrations/{id}` with `FileStorageContainerTypeReg.Selected` (own registrations); `GET containerTypes` is delegated-only. `isOfficeRestricted` now on v1.0 (settings doc 2026-09-21) — knowledge/sharepoint-embedded/docs/learn-containertypes.md says beta-only (stale).
- Limits: 5,000 files with additive permissions per container; 5,000 users per file; permission ops cost 5 RU; 3,000 RU/min/container, 12,000 RU/min/app/tenant, 600 RU/min/user.

**Sources**:
- https://learn.microsoft.com/en-us/sharepoint/dev/embedded/build/share-files-manage-permissions
- https://learn.microsoft.com/en-us/sharepoint/dev/embedded/build/configure-authentication-authorization
- https://learn.microsoft.com/en-us/sharepoint/dev/embedded/build/open-office-files
- https://learn.microsoft.com/en-us/graph/api/driveitem-preview
- https://learn.microsoft.com/en-us/graph/api/filestoragecontainer-delete-permissions ; -list-permissions ; -post-permissions
- https://learn.microsoft.com/en-us/sharepoint/dev/embedded/plan/limits-calling-patterns
- https://learn.microsoft.com/en-us/microsoft-365/cloud-storage-partner-program/online/scenarios/coauth
- https://learn.microsoft.com/en-us/entra/identity/conditional-access/concept-continuous-access-evaluation

**Open questions**: live probe needed for (1) app-only invite under isSharingRestricted=true, (2) Word desktop edit with item-only permission, (3) desktop behaviour after removal mid-session, (4) customProperties size cap.
Related: [[spe-ciam-crosstenant-apponly-brokering-2026-07-18]], [[spe-wopi-coauthoring-lock-423-2026-07-30]].

## Follow-up (same day): desktop Word + webhooks
- Office URI `ofv` = read-only, `ofe` = edit; neither carries auth — desktop authenticates the USER to SharePoint, so even view needs a file/container permission (no doc distinguishes ofv/ofe on permission). INFERRED + open-office-files prereq.
- No doc gives a desktop ACL re-check interval. CAE = account events only; CAE coauth note: user "loses access completely after closing the document/app" (or 1h with CA IP policy). AutoSave saves "every few seconds"; version-history entries ~every 10 min (support.microsoft.com AutoSave). Access token 60-90 min default, up to 28h with CAE — tokens don't bypass ACL checks (INFERRED). Best residual: writes fail at next upload (seconds-minutes, INFERRED); already-open content readable until closed.
- SPE webhooks: `drives/{containerId}/root`, changeType `updated` only, driveItem latency avg <1 min / max 6 h; SPE doc says max lifetime 4,230 min, Graph overview says 42,300 min for driveItem (conflict). BFF already uses app-only root subscription + delta (SpeFileStore.CreateDriveRootSubscriptionAsync); live webhook fire was never verified (compose-r2 spike-6). `prefer:includesecuritywebhooks` gives permission-change notifications.
