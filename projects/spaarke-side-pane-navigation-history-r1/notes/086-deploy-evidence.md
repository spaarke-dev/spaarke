# Task 086 — NavigatorPane deploy evidence (spaarkedev1)

> **2026-08-13** · env `https://spaarkedev1.crm.dynamics.com` · app Matter Management `729afe6d-ca73-f011-b4cb-6045bdd8b757`
> Deployed by `src/solutions/NavigatorPane/Deploy-NavigatorPane.ps1` (az token → Web API base64 upsert → PublishXml).

## Build (steps 1–4) — fresh, cache-cleared, known-string verified ✅
- Recompiled `@spaarke/ui-components` first (tsc clean) — post-master-merge (branch @ `883dbc578`, 0 behind master).
- Cache-clear `rm -rf dist/ node_modules/.vite/ .vite/` then Vite `npm run build` (NOT build:prod).
- Built `dist/index.html` = **1,921,212 bytes** single-file bundle.
- Known-string checks in the built HTML: `navigator-quickswitcher-input` (070) ✅, `navigator-body` (040) ✅, `Pin this page` (051) ✅ — confirms the fresh, fully-merged feature set shipped (not a stale dist).
- NavigatorPane Jest: **139/139** green post-merge.

## Deploy (step 5) — two web resources, published ✅
| Web resource | Type | id | Source |
|---|---|---|---|
| `sprk_NavigatorPane.html` | 1 (HTML) | `6cebb920-4b97-f111-b8dc-70a8a590c51c` | `dist/index.html` (1.92 MB) |
| `sprk_SidePaneManager` | 3 (JScript) | `c5ae4b21-4b97-f111-b8dc-7ced8ddc4cc6` | `bootstrap/sprk_SidePaneManager.js` (6.2 KB) |

Both **created new** (neither existed — `sprk_SidePaneManager` was the deleted WR that left the ribbon rule dormant), then `PublishXml`. Verified server-side via read-only Web API: names + types confirmed.

## Bootstrap (step 6) — Path B wiring
`bootstrap/sprk_SidePaneManager.js` exposes `Spaarke.SidePaneManager.initialize` (+ `openPane`), the EXACT namespace/name the spaarkedev1 application-ribbon EnableRule already references:
```
CustomRule FunctionName="Spaarke.SidePaneManager.initialize" Library="$webresource:sprk_SidePaneManager"
```
`initialize()` is the enable-rule entry (returns true, never throws; registers the pane as a side effect). It `createPane({paneId:'sprk-navigator', canClose:false, alwaysRender:true})` → `navigate({pageType:'webresource', webresourceName:'sprk_NavigatorPane.html'})`, singleton-guarded, via the 3-frame `getSidePanesApi()`. Also auto-inits on load (console-paste / Code-Page injection) with backoff.

## Step 7 — app-load auto-launch: PENDING OWNER BROWSER CONFIRMATION (UAT / task 087)
Server-side deploy is complete + verified. Whether the pane **auto-appears at app load** depends on the dormant ribbon rule actually EVALUATING — the task-001 open sub-question (does a control still pin `sprk.Global.SidePaneManager.Command` so the enable rule fires?). This is only answerable in the browser and is the first UAT check.

### UAT runbook (owner)
1. **Fully reload** Matter Management: `https://spaarkedev1.crm.dynamics.com/main.aspx?appid=729afe6d-ca73-f011-b4cb-6045bdd8b757` (hard refresh / new tab).
2. **Expected:** the **Navigator** pane auto-appears docked on the right (collapsed rail icon), no click. Expand it → Recent / Pinned / Views tabs + search box.
3. Navigate to ≥3 records → Recent (Viewed) should capture them. Star one → Pinned. "Pin this page" → Bookmarks. Try Ctrl/Cmd+K search.

### Fallback if the pane does NOT auto-appear (rule not pinned)
The deploy is still good; only the auto-trigger is missing. In DevTools console on the app frame:
```js
Spaarke.SidePaneManager.initialize()   // if the WR loaded but the rule didn't fire
// —or, if the WR script wasn't injected at all—
var s=document.createElement('script'); s.src='/WebResources/sprk_SidePaneManager'; document.body.appendChild(s);
```
If the rule genuinely doesn't evaluate at app load, land a pinned global button via `/ribbon-edit` using `spike/sprk_application_ribbon_sidepanespike.xml` (adapted to the SidePaneManager command), or reuse the still-live SprkChat global-button slot pattern. No code change needed — bootstrap already supports `openPane()` for a button Command.

## Deferred (do NOT block UAT)
- **021 end-user security role**: UAT runs as System Administrator (already has `sprk_navitem` access). Owner-scoped User-level CRUD for a named end-user role + 2-user isolation test still pending a role choice.
- **052 Monitored "assigned to me"**: ships owner-only (Path A); the A/B (BFF membership resolver) decision remains open, non-blocking.
