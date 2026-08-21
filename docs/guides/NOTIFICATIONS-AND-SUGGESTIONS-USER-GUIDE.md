# Notifications & Proactive Suggestions — User & Admin Guide

> **Audience**: End users (what you see), and admins/operators (how to turn it on per environment).
> **Feature**: Spaarke can **proactively** surface things worth your attention — it no longer only reacts to what you type.
> **Architecture**: [SPAARKE-NOTIFICATION-SPINE-ARCHITECTURE.md](../architecture/SPAARKE-NOTIFICATION-SPINE-ARCHITECTURE.md) · **Rules**: [ADR-047](../adr/ADR-047-notification-action-spine.md).

> **⚠️ Status (2026-08-20) — read before Part 1.** The in-Assistant **suggestion card** described below was **removed** from the Assistant by a later change (`spaarkeai-assistant-enhancements-r2`). Today:
> - **Communication notifications DO work** (unread badge / list in the Communication Workspace).
> - **Proactive suggestions are temporarily NOT surfaced in the UI.** The backend still produces them (they persist in the outbox), but no on-screen card renders them.
> - They are being **reintroduced as native Dataverse notifications** (the OOB **bell** in the app header), where clicking opens the record in a modal — scoped in [`spaarke-notification-spine-r2`](../../projects/spaarke-notification-spine-r2/README.md). The "suggestion card" subsection below documents the *former* and *planned* behavior, not what renders today.

---

## Part 1 — What you see (end users)

### Proactive suggestions — ⚠️ temporarily not surfaced (see status banner above)
The intent: when your **Daily Briefing** flags a high-priority matter (or another important item), Spaarke proactively surfaces it — without you asking — as a small nudge you can act on:

> 💡 **Review Acme v. Beta** →

**Acting on the nudge opens that record in a pop-up (modal) dialog** — you stay where you are; it does not navigate you away.

**What renders this today:** *nothing* — the former in-Assistant suggestion card was removed. The nudge is being rebuilt as a native Dataverse **bell** notification in [`spaarke-notification-spine-r2`](../../projects/spaarke-notification-spine-r2/README.md); this subsection describes that former/planned behavior.

- Suggestions **expire** — a stale one simply stops appearing (it is never shown-but-broken).
- If a suggestion becomes stale or you've lost access to the record between it appearing and your click, it opens nothing and shows a brief, calm message instead of an error.
- A suggestion only appears if it's genuinely actionable (it always has a real record to open).

### Communication notifications
When a relevant email or message arrives, the spine can update your **unread badge / communications list** in near-real-time (used by the Communication Workspace). This is the same delivery mechanism; it carries only identifiers — the app fetches the detail securely when you open the item.

### What is intentionally NOT on the wire
For privacy and security, a notification carries only identifiers and minimal display text (a title, a sender name, an optional short snippet). It never carries message bodies, privileged content, or a "ready-to-fire" action. When you act, the app re-fetches the detail through the secured backend, which re-checks your access at that moment.

---

## Part 2 — Turning it on (admins / operators)

The feature is **off by default** and **degrades gracefully**: with nothing configured, a normal user simply sees nothing (no errors). Getting it operational is **three things that must be true** — the Dataverse schema is present, the BFF is configured, and the client is deployed — layered in **tiers** so you can get suggestions working with almost nothing and add real-time push + communication notifications incrementally.

> Config keys below are verified against the shipped code (`SignalRDeliveryOptions.IsConfigured`, `NotificationsModule` null-object gating, `SuggestionGateOptions`). Nothing is committed to source — these are BFF **App Settings** / **Key Vault** values set per environment.

### Hard requirements (nothing renders without all three)

| # | Requirement | Why it's non-negotiable |
|---|---|---|
| 1 | **`sprk_notificationoutbox` table exists in the target Dataverse environment** | It's the durable store — producers write to it, the client reads `/api/notifications/pending` from it. No table → nothing persists → nothing shows (even via polling). |
| 2 | **The SpaarkeAi code page is deployed** (current master build) | It ships the notifications client (auto-starts: negotiate → connect, else poll) and the **live `communication-arrived` consumer**. ⚠️ It NO LONGER ships a suggestion renderer (removed by `spaarkeai-assistant-enhancements-r2`) — so with only requirements 1–3 met, `suggestion` rows are produced and pollable but **not shown**. The visible suggestion surface returns via `spaarke-notification-spine-r2` (OOB bell). |
| 3 | **`Notifications:Suggestions:Enabled = true`** | Master switch for proactive suggestions (defaults **false** — deny-by-default). |

> **Schema note (dev today):** `sprk_notificationoutbox` (and, for communication notifications, `systemuser.sprk_isexternal` + `sprk_communicationrule`) currently exist in the **dev** environment where they were created directly. They must exist in whatever environment you're lighting up. (No production packaging is needed yet — revisit a managed-solution export when a non-dev deploy is planned.)

### Tier 0 — Suggestions produced, poll-only (~30 s), no Azure needed
Requirements 1–3 above. That's the entire setup for **producing** suggestions (they're written to the outbox and are pollable at `/api/notifications/pending`). ⚠️ **They will not be visible on screen** until the OOB-bell surface from `spaarke-notification-spine-r2` ships — the former in-Assistant card was removed. Communication notifications (Tier 2) render today; suggestions do not.

### Tier 1 — Real-time push (add on top of Tier 0)

| Add | Detail |
|---|---|
| **Azure SignalR resource, Serverless mode** (`Microsoft.Azure.SignalR.Management`; per-customer per ADR-027) | Create it for the environment. |
| **`Notifications:SignalR:ConnectionString`** = its connection string (**Key Vault reference**, ADR-028) | The real delivery service is registered **only** when `Notifications:SignalR:Enabled` (default `true`) **AND** a non-empty connection string are BOTH present; otherwise a no-op null-object is used (poll-only, never a startup error). |
| **Env CSP `connect-src` allows `wss://*.service.signalr.net`** | Else the browser silently blocks the socket → poll fallback. Verify at provisioning. |

(`Notifications:SignalR:HubName` defaults to `"notifications"` — leave it.)

### Tier 2 — Communication notifications (email/message arrival badges)
Needed by the **Communication Workspace**, *not* by suggestions.

| Add | Why |
|---|---|
| Backfill **`systemuser.sprk_isexternal`** (two-option, default *No*) for recipients | Fan-out targeting **fails closed** (zero recipients) for un-backfilled users — correct, but silently "no notifications" for them. |
| **`sprk_communicationrule`** table present | The `communication-assessed` policy gate reads it; deny-by-default without rules. |
| **023 R-5 fan-out security sign-off** (named human) before enabling in a real environment | Fan-out is a compliance surface — a mis-targeted envelope is an incident. |

### The config block (BFF App Settings / Key Vault)

```jsonc
"Notifications": {
  "SignalR": {
    "Enabled": true,                                  // default true; false forces poll-only
    "ConnectionString": "@Microsoft.KeyVault(...)",   // Azure SignalR (Serverless). Absent → poll fallback
    "HubName": "notifications"                         // leave default
  },
  "Suggestions": {
    "Enabled": true,                                   // default FALSE — this is the suggestion master switch
    "MaxPerRun": 3,                                    // cap per Daily-Briefing render (anti-flood)
    "TtlHours": 24                                     // how long a suggestion stays live before it expires
  }
}
```

### What degrades gracefully (won't break anything)

| If missing | Effect |
|---|---|
| Azure SignalR / connection string | Live push off → **poll fallback** (works, ~30 s slower). |
| CSP `wss://*.service.signalr.net` entry | Socket blocked → **poll fallback**. |
| `systemuser.sprk_isexternal` backfill | Communication fan-out silent-zero for those users — **suggestions unaffected**. |
| `Notifications:Suggestions:Enabled=false` | No suggestions produced (silent, safe). |

**Shortest path to *producing* a suggestion:** deploy `sprk_notificationoutbox` + the SpaarkeAi code page, set `Notifications:Suggestions:Enabled=true`, and make sure the Daily Briefing runs. ⚠️ This produces rows but does not display them yet (no renderer today — see the status banner); verify production by polling `GET /api/notifications/pending?kind=suggestion`. Real-time push and communication notifications layer on after.

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
| No suggestions ever appear (on screen) | **Expected today** — the in-Assistant suggestion renderer was removed; there is no visible suggestion surface until `spaarke-notification-spine-r2` (OOB bell) ships. To confirm suggestions are being *produced*, poll `GET /api/notifications/pending?kind=suggestion` (also confirm `Notifications:Suggestions:Enabled=true`, `sprk_notificationoutbox` exists, and the Daily Briefing is running with high-priority items). |
| Some users get communication notifications, others get none | The silent users are missing the `systemuser.sprk_isexternal` backfill (Tier 2) — fan-out fails closed for them. Backfill the flag. |
| Communication notifications appear on a delay (not instant) | Live SignalR isn't connected — check Tier 1 (`Notifications:SignalR:ConnectionString` present + CSP allows `wss://*.service.signalr.net`). The client is on the poll fallback (working, just slower). |

---

## Part 5 — For makers extending this

- To produce a NEW kind of proactive suggestion, add a **producer** that grounds + gates and writes a `kind=suggestion` outbox row. ⚠️ **There is no automatic renderer today** — the former in-Assistant card was removed. The forthcoming OOB-bell surface (`spaarke-notification-spine-r2`) is what will display these rows (and give you the "open the record in a modal" behavior). Until then a producer's rows are produced-but-unrendered. See the architecture doc §4 "How to extend".
- Do **not** build a second push channel, a second confirmation gate, or a per-app notification pipeline — there is exactly one spine (ADR-047).
- Related reading: [MODAL-DECISION-CRITERIA.md](../standards/MODAL-DECISION-CRITERIA.md) (why acting opens a Layout-1 modal), [SPAARKE-NOTIFICATION-SPINE-ARCHITECTURE.md](../architecture/SPAARKE-NOTIFICATION-SPINE-ARCHITECTURE.md) (the component model).
