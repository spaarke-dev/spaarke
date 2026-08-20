---
name: appnotification-modal-click-schema-2026-08-20
description: Dataverse in-app notification (appnotification) — YES modal-on-click is native via URL action navigationTarget=dialog; full Data JSON action schema; no read/dismiss state field (dismiss deletes); background app-user create supported
metadata:
  type: project
---

## 2026-08-20: Can a Dataverse in-app notification open the record as a CENTER MODAL on click?

**Question**: Can clicking an OOB `appnotification` (MDA notification bell) open the referenced record as a center dialog/modal (navigateTo target:2 equivalent), not full-page? Plus Data JSON action schema, background creation, dismiss/read state.

**Findings**:
- **YES, natively.** The notification's `Data`/`Actions` JSON supports a **URL action** with `"navigationTarget": "dialog"` → "Opens in the center dialog." Documented values: `dialog` (center modal), `inline` (default, current page = full-page navigateTo), `newWindow` (new browser tab). This is the navigateTo target:2 equivalent, delivered as a labeled action link on the card (e.g. "Open matter"), NOT the primary title/body click. Body/title markdown hyperlinks (via `OverrideContent`) navigate `inline`/full-page with no navigationTarget control — so the modal MUST be exposed as a defined action.
- **Action schema** (open types; `@odata.type` = `#Microsoft.Dynamics.CRM.expando`): `Actions.actions[]` each has `title` (link label) + `data{ type, url, navigationTarget }`. Three action `type`s: `url`, `sidepane` (opens record in a side pane — alt to modal), `teamsChat`. For a modal-open-record: `type:"url"`, `url:"?pagetype=entityrecord&etn=sprk_matter&id=<guid>"` (same-origin `?`- or `/`-prefixed ONLY; bare relative paths + `javascript:`/`data:`/protocol-relative are BLOCKED for XSS since a 2026 security change — blocked URLs still render but do nothing), `navigationTarget:"dialog"`.
- **Background/server creation: YES.** Two paths: (a) `SendAppNotification` action — needs `prvSendAppNotification` privilege (Environment Maker default); (b) **direct Create of `appnotification` rows — privilege NOT required**, just Create on the table. So an app-user/S2S job can insert rows; notification appears without the user opening any UI. Delivery is CLIENT POLLING by the MDA shell (at app start + on page-nav if last poll >1 min ago) — not push.
- **Per-user targeting**: `OwnerId` = recipient. Metadata `Targets` = systemuser,team but the how-to is explicit: set to a USER only; team delivery unsupported; multi-user = one row each. `TTLInSeconds` (Expiry) = seconds-to-delete, default 14 days. `Data` memo max 5000 chars. Icon `IconType` picklist (Info 100000000…Custom 100000005). `ToastType` Timed 200000000 / Hidden 200000001 (Hidden = center-only, no toast pop).
- **Dismiss/read state: THERE IS NO read/dismiss field.** `appnotification` is an **Elastic table**; writable columns have NO statecode/statuscode/isread/dismissed. Dismiss = the row is **deleted** (same as TTL expiry deletes it). Consequence: a server process **cannot** query "was the user already notified (read/dismissed) about matter X" for dedup — dismissed rows are gone. You CAN query still-pending rows (filter `ownerid` + a marker you stash in `Data` JSON) to avoid duplicating a notification that's still sitting in the bell, but NOT post-dismiss. Durable dedup requires your OWN tracking table (e.g. `sprk_` row per (user, matter, nudge-type)).

**Sources**:
- https://learn.microsoft.com/power-apps/developer/model-driven-apps/clientapi/send-in-app-notifications (how-to; doc dated 2026-08-12 — navigationTarget table, URL/sidepane/teamsChat actions, supported/blocked URL formats, polling, privileges, direct-create-no-privilege note)
- https://learn.microsoft.com/power-apps/developer/data-platform/reference/entities/appnotification (entity ref — Elastic table, writable cols, no state field, TTLInSeconds deletes, OwnerId targets)

**Open questions**: Does clicking the card BODY (vs an action link) ever honor a default action navigationTarget, or is the modal strictly action-link-only? (Docs only show navigationTarget on actions.) Whether Spaarke's outbox/dedup table already exists in the notification-spine project or needs creating.
