---
name: spe-wopi-coauthoring-lock-423-2026-07-30
description: Whether Microsoft Graph can programmatically release a WOPI co-authoring lock (Word-for-web) on an SPE driveItem that causes 423 Locked on PUT /content. Verdict + checkout/checkin/discardCheckout SPE support + 30-min WOPI lock timeout. For an "Unlock & Save" button design on Compose.
metadata:
  type: project
---

# SPE + WOPI co-authoring lock / 423 on content PUT (2026-07-30)

**Question**: When a BFF PUTs to an SPE driveItem content while the doc is open in Word-for-web, it gets 423 Locked (a WOPI co-authoring lock, NOT a user checkout). Can Graph release that lock programmatically? Do checkout/checkin/discardCheckout exist + work on SPE + affect the co-authoring lock? What status code, how to distinguish, recommended pattern, lock timeout?

**Verdict**: **NO Graph API releases a WOPI co-authoring lock.** An "Unlock & Save" button backed by Graph can discard a FORMAL CHECKOUT (`discardCheckout`) but has ZERO effect on a Word-for-web co-authoring lock. That lock clears only when all editors close the doc (WOPI `Unlock`) OR after the SharePoint-side **30-minute** WOPI lock timeout (refreshed by Word activity via `RefreshLock`, so the 30-min countdown starts from last edit activity). Confirmed by Microsoft Q&A (no documented unlock; MS moderator deflected to community forum) + WOPI concepts doc.

**Findings by sub-question**:

1. **No programmatic co-authoring-lock release in Graph.** No unlock/abort-session endpoint. `driveItem: lockOrUnlockRecord` (PATCH .../retentionLabel, `isRecordLocked`) is RECORDS-MANAGEMENT / retention-label locking ONLY — unrelated to co-authoring. Graph respects the WOPI lock and returns 423; there is no documented API to read real-time lock state OR to force-release a co-authoring lock. Only cleared by all editors closing (WOPI `Unlock`) or the 30-min timeout.

2. **checkout / checkin / discardCheckout ALL exist and are EXPLICITLY SPE-supported** — each Graph v1.0 ref page carries the identical "SharePoint Embedded requires `FileStorageContainer.Selected` … container type permissions" note. `POST /drives/{id}/items/{id}/checkout|checkin|discardCheckout`. They act on the FORMAL CHECKOUT state (publication/versioning — "prevent others editing, changes not visible until checkin"), a DIFFERENT lock than co-authoring. They do NOT touch a WOPI co-authoring lock. Perms: delegated Files.ReadWrite (checkout/discard) / app Files.ReadWrite.All. checkout returns 204.

3. **Status code = 423 Locked** for both lock types on a content PUT — they are NOT cleanly distinguishable by status code alone. Nuance from discardCheckout doc: delegated `discardCheckout` returns **400 Bad Request if the file isn't checked out**, and **423 Locked if ANOTHER user has it checked out** (app-access can discard any checkout). So a formal checkout is detectable via the `publication` facet / checkout metadata; a co-authoring lock is NOT surfaced by any documented facet or error-code string. Search-attempt-and-interpret-423 is the only (undocumented) way to detect co-authoring lock state.

4. **Recommended pattern** (Microsoft has no "take control" / Graph-level force-save): the doc never leaves the Word engine, so save conflict is by design. Options: (a) retry with backoff and expect success only after Word idle/close; (b) prompt the user to close Word-for-web; (c) if YOU host the WOPI editor you can call WOPI `Unlock` — but Spaarke is NOT the WOPI host (Microsoft 365 for the web is), so this is unavailable. There is no Graph-level override.

5. **Timeout = 30 minutes.** WOPI concepts (authoritative): "WOPI locks must automatically expire after 30 minutes if not renewed by the WOPI client." Word-for-web `RefreshLock`s while active, so the 30-min clock effectively runs from last activity, not tab-open. Auto-save cadence: Word every 30s while editing; permission recheck every 5 min.

**Implication for Compose "Unlock & Save"**: A Graph-backed button can honestly only (i) discard a formal checkout (app-only can force any user's checkout via `discardCheckout`), and (ii) retry the content PUT. It CANNOT clear a Word-for-web co-authoring lock. UX must degrade to "document is open in Word — close it or wait up to ~30 min," not promise an instant unlock. Don't mislabel the button as clearing co-authoring.

**Sources** (all learn.microsoft.com, MOST authoritative = WOPI concepts + Graph refs):
- graph/api/driveitem-checkout — exists, SPE note, 204
- graph/api/driveitem-discardcheckout — SPE note; 400 if not checked out, 423 if another user checked out, app-access discards any
- graph/api/driveitem-checkin — checkin (formal)
- graph/api/driveitem-lockorunlockrecord — records/retention ONLY (isRecordLocked), NOT co-authoring
- microsoft-365/cloud-storage-partner-program/rest/concepts#lock — "expire after 30 minutes unless refreshed"; locks not user-owned
- microsoft-365/cloud-storage-partner-program/rest/files/lock + /unlock + /refreshlock — WOPI lock protocol (host-side; Spaarke is not the host)
- microsoft-365/cloud-storage-partner-program/online/scenarios/coauth — Word auto-save 30s, recheck 5min, Unlock-may-fail→lock-times-out
- MS Q&A 1470700 (how to unlock a driveItem) — no documented unlock; deflected to community
- MS Q&A 5654593 (detect open/locked) — no documented lock-state read API

**Open questions**:
- Does SPE expose the `publication` facet identically to classic SPO so a formal-checkout state is reliably readable? (likely yes; not spike-verified)
- Any beta/undocumented endpoint to end a co-authoring session? (none found; treat as no)
