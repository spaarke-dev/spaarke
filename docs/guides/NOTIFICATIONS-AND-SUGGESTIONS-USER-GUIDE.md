# Notifications & Proactive Suggestions — User & Admin Guide

> **Audience**: End users (what you see), and admins/operators (how to turn it on per environment).
> **Feature**: The Spaarke Assistant can now **proactively** surface things worth your attention — it no longer only reacts to what you type.
> **Architecture**: [SPAARKE-NOTIFICATION-SPINE-ARCHITECTURE.md](../architecture/SPAARKE-NOTIFICATION-SPINE-ARCHITECTURE.md) · **Rules**: [ADR-047](../adr/ADR-047-notification-action-spine.md).

---

## Part 1 — What you see (end users)

### Proactive suggestions
When your **Daily Briefing** flags a high-priority matter (or another important item), the Spaarke Assistant shows a small **suggestion card** at the **top of the Assistant pane** — without you asking. It looks like the "Suggested next steps" cards you already see after the assistant does something, e.g.:

> 💡 **Review Acme v. Beta** →

**Clicking the card opens that record in a pop-up (modal) dialog** — you stay in the Assistant; it does not navigate you away. Review the record, close the dialog, and you're back where you were.

- Suggestions **expire** — a stale one simply stops appearing (it is never shown-but-broken).
- If a suggestion becomes stale or you've lost access to the record between the card appearing and your click, it opens nothing and shows a brief, calm message instead of an error.
- A suggestion only appears if it's genuinely actionable (it always has a real record to open).

### Communication notifications
When a relevant email or message arrives, the spine can update your **unread badge / communications list** in near-real-time (used by the Communication Workspace). This is the same delivery mechanism; it carries only identifiers — the app fetches the detail securely when you open the item.

### What is intentionally NOT on the wire
For privacy and security, a notification carries only identifiers and minimal display text (a title, a sender name, an optional short snippet). It never carries message bodies, privileged content, or a "ready-to-fire" action. When you act, the app re-fetches the detail through the secured backend, which re-checks your access at that moment.

---

## Part 2 — Turning it on (admins / operators)

The feature is **off by default** and **degrades gracefully**: with nothing configured, a normal user simply sees nothing (no errors). Light it up per environment with the steps below.

### Step 1 — Provision Azure SignalR (real-time push)
Notifications are delivered over **Azure SignalR (Serverless mode)** hosted inside the BFF.
- Create an Azure SignalR resource (Serverless) for the environment.
- Store its connection string in Key Vault and set **`Notifications:SignalR:ConnectionString`** (per ADR-027/028).
- **If you skip this**: there is no live push, but the client automatically falls back to **polling** the backend — notifications still arrive, just on a short delay.

### Step 2 — Allow the SignalR WebSocket in the environment CSP
The Power Platform environment's Content-Security-Policy `connect-src` must allow **`wss://*.service.signalr.net`**.
- **If you skip this**: the browser silently blocks the live connection → the client falls back to polling.

### Step 3 — Backfill the internal/external flag on users
Communication fan-out targeting uses the authoritative **`systemuser.sprk_isexternal`** field (two-option, default *No*).
- Backfill this field for the users who will receive communication notifications.
- **If you skip this**: fan-out **fails closed** (zero recipients) for un-backfilled users — correct, but silently "no notifications" for them.

### Step 4 — Enable proactive suggestions
Proactive suggestions are gated by a deny-by-default policy dial:

| Setting (`Notifications:Suggestions:*`) | Default | Meaning |
|---|---|---|
| `Enabled` | **false** | Master switch. Set **true** to produce proactive suggestions at all. |
| `MaxPerRun` | `3` | Cap on suggestions produced per Daily-Briefing render (avoids flooding). |
| `TtlHours` | `24` | How long a suggestion stays live before it expires and stops rendering. |

- Set **`Notifications:Suggestions:Enabled=true`** to switch suggestions on for the environment.

---

## Part 3 — How it decides what to suggest (transparency)

A suggestion is produced only when **both** checks pass (this is why suggestions are trustworthy, not noisy):
1. **Grounded** — it traces to a real record (a real entity type, a real record id, a real name). Anything that can't be tied to an actual record is never suggested.
2. **Gated** — the suggestion is admitted only when the environment has suggestions enabled AND the item is confirm-worthy (e.g. flagged high-priority or monitored).

Both checks run **before** anything is stored or pushed — nothing ungrounded or ungated ever reaches your screen.

---

## Part 4 — Troubleshooting

| Symptom | Likely cause & fix |
|---|---|
| No suggestions ever appear | `Notifications:Suggestions:Enabled` is `false` (default) — set it `true`. Also confirm the Daily Briefing is running and producing high-priority items. |
| Suggestions appear but on a delay (not instant) | Live SignalR isn't connected — check Step 1 (connection string) and Step 2 (CSP `wss://*.service.signalr.net`). The client is on the poll fallback (working, just slower). |
| Some users get communication notifications, others get none | The silent users are missing the `systemuser.sprk_isexternal` backfill (Step 3) — fan-out fails closed for them. Backfill the flag. |
| Clicking a suggestion shows "no longer available" | The suggestion expired or the record's access changed between the card appearing and the click — expected, safe behavior. Nothing opened by design. |
| A record opens by *navigating away* instead of a modal | The surface used `openRecord` (navigate-away) instead of `openRecordModal` (Layout 1 modal). Suggestion cards use the modal; report any surface that doesn't. |

---

## Part 5 — For makers extending this

- To surface a NEW kind of proactive suggestion, add a **producer** that grounds + gates and writes a `kind=suggestion` outbox row — the Assistant renders it and the "open the record in a modal" behavior for free. See the architecture doc §4 "How to extend".
- Do **not** build a second push channel, a second confirmation gate, or a per-app notification pipeline — there is exactly one spine (ADR-047).
- Related reading: [MODAL-DECISION-CRITERIA.md](../standards/MODAL-DECISION-CRITERIA.md) (why acting opens a Layout-1 modal), [SPAARKE-NOTIFICATION-SPINE-ARCHITECTURE.md](../architecture/SPAARKE-NOTIFICATION-SPINE-ARCHITECTURE.md) (the component model).
