# Task 001 — SPIKE / GATE report: Path B bootstrap on current UCI

> **Status**: ✅ COMPLETE — validated on spaarkedev1 2026-08-13 (owner browser-observation, harness v1.0.2).
> **Verdict**: ✅ **GO** (core mechanics decisively proven; app-load auto-injection = Stage 2, validated at deploy task 086 using the recovered global-ribbon pattern).
> **Env**: spaarkedev1 (`https://spaarkedev1.crm.dynamics.com`) · **App**: Matter Management (`appid=729afe6d-ca73-f011-b4cb-6045bdd8b757`)
> **Maps to**: spec FR-00, plan.md Phase 0. Watch: R1 (Path B on current UCI), R2 (current-user filter), OQ-6 (custom-page name shape).

---

## What Path B is (mechanism confirmed from recovered code)

A single global bootstrap (JS web resource) calls `Xrm.App.sidePanes.createPane({ canClose:false, alwaysRender:true })` once, then `pane.navigate({ pageType:'webresource', webresourceName })` to a code page. `alwaysRender:true` keeps the pane's JS alive while collapsed, so a ~1.5s `Xrm.Utility.getPageContext()` poll can observe every record visit. At app load the bootstrap is triggered by a hidden **global-ribbon enable-rule** (`Mscrm.GlobalTab`) whose side effect registers the pane.

The spike splits this into two independently-testable stages so the **decisive core-mechanics question** (does the persistent pane + poll still work on current UCI?) is answered with minimal deploy friction, before the higher-friction ribbon-injection question.

## Harness artifacts (built this task — in `../spike/`)

| File | Role |
|---|---|
| `spike/sprk_sidepanespike.html` | Diagnostic pane body: frame-walks Xrm, polls `getPageContext()` every 1.5s, shows heartbeats (proves JS alive while collapsed), distinct-visit counter + log, and an "edited by me" WebApi probe. Self-diagnosing; has a **Copy results** button. |
| `spike/sprk_sidepanespikebootstrap.js` | Path B bootstrap: singleton-guarded `createPane`+`navigate`; entry paths = auto-init / `initialize()` / ribbon `enable()`+`openPane()`. |
| `spike/sprk_application_ribbon_sidepanespike.xml` | Stage-2 global-ribbon RibbonDiffXml (adapted from recovered `git 7d80565a6^`). |
| `spike/Deploy-SidePaneSpike.ps1` | UPSERT both web resources to spaarkedev1 + PublishXml. |

---

## Owner runbook

### Deploy (once) — I run this for you, or you can:
```powershell
az login   # if not already
projects/spaarke-side-pane-navigation-history-r1/spike/Deploy-SidePaneSpike.ps1
```
Deploys the two web resources to spaarkedev1 and publishes.

### Stage 1 — CORE MECHANICS (decisive go/no-go). No ribbon needed.

1. Open **Matter Management**: `https://spaarkedev1.crm.dynamics.com/main.aspx?appid=729afe6d-ca73-f011-b4cb-6045bdd8b757`
2. Open DevTools (F12) → **Console**. Paste and run (creates the docked pane directly):
   ```js
   Xrm.App.sidePanes.createPane({paneId:'sprk-spike',title:'Side-Pane Spike',canClose:false,width:420,alwaysRender:true,isSelected:true})
     .then(p => p.navigate({pageType:'webresource', webresourceName:'sprk_sidepanespike.html'}));
   ```
   *(Alternative that also exercises the real bootstrap: load `/WebResources/sprk_sidepanespikebootstrap.js` — it auto-inits.)*
3. The **Side-Pane Spike** pane should appear docked on the right, showing a heartbeat counter ticking.
4. **Navigate to ≥5 different records** (open matters/accounts/etc. from grids or links).
5. **Collapse** the pane (click its launcher icon to hide it), wait ~10s, then **expand** it again.
6. Click **"Run 'edited by me' WebApi probe"** in the pane.
7. Click **"Copy results"** and paste the copied text back to me.

**Watch for (record answers below):**
- (A) Did the pane appear and **persist** as you navigated between records (not disappear/reload)?
- (B) While **collapsed**, did the heartbeat counter **keep climbing** (JS still running)? Compare heartbeat count before/after the collapse wait.
- (C) Did **"distinct record visits"** increase by ~1 per record you opened (poll observed each visit)?
- (D) Did the WebApi probe say **OK** with a row count (current-user filter works)?
- (E) In the log, what does the **raw input** JSON look like for a normal record vs. a custom page? (OQ-6)

### Stage 2 — TRUE auto-create-at-app-load (only if Stage 1 = GO)

The `Mscrm.GlobalTab` RibbonDiffXml **cannot** be applied via Web API. Land it via **solution import** or the **`ribbon-edit`** skill:
- Preferred: run `/ribbon-edit` (export application ribbon → merge `spike/sprk_application_ribbon_sidepanespike.xml` → re-import), OR
- Manual: add both web resources + the application-ribbon customization to an unmanaged solution, `pac solution pack`/import to spaarkedev1, publish.

Then **fully reload** Matter Management (no console). Expected: the **Side-Pane Spike** pane auto-appears at app load with no click, a "Spike Pane" button shows in the global command bar, and re-navigation does **not** create a duplicate pane.

---

## Acceptance criteria — verdict checklist

| # | Criterion (spec FR-00) | Result | Evidence |
|---|---|---|---|
| 1 | App-level pane auto-creates at app load, no user action; no duplicate on re-entry | 🟡 PROXY | Created via console + singleton guard proven. True app-load auto-injection = **Stage 2** (recovered `Mscrm.GlobalTab` enable-rule, historically shipped); validated at task 086 deploy. |
| 2 | Pane persists across ≥5 navigations + ≥1 collapse/expand; JS runs while collapsed | ✅ | v1.0.1 run: **147 heartbeats** through a collapse/expand (JS never stopped). v1.0.2 run: pane persisted across **6 navigations**. |
| 3 | `getPageContext()` polling observes every visit | ✅ | v1.0.2: **6 distinct visits** captured (invoice/matter, record+list); all 3 signals agree in every snapshot. |
| 4 | Report states explicit GO/NO-GO with per-criterion evidence | ✅ | This doc — **GO**. |
| 5 | Negative: if unreliable → Path A fallback + escalation | ✅ N/A | Not triggered (GO). Fallback still documented below for the record. |

**R2 (current-user filter for FR-03 "Edited"):** ✅ **Works.** `Xrm.WebApi.retrieveMultipleRecords("account", "?$filter=_modifiedby_value eq {userId}&$top=3")` returned 3 rows; `userId` from `getGlobalContext().userSettings.userId`. Note: the method is **`retrieveMultipleRecords`** (raw Client API), NOT `retrieveMultiple` (that's the repo's `xrmContext` wrapper name).

**OQ-6 (page-context shape):** entityrecord → `{entityName, entityId, pageType:"entityrecord", createFromEntity}`; entitylist → `{entityName, pageType:"entitylist"}` (no id). **Custom-page shape NOT yet observed** (none visited) — carry into task 040/086; low risk.

---

## Productionization approach (for tasks 010 / 011 / 030 / 086) — GO

1. **NEVER cache the Xrm reference — re-acquire `window.top.Xrm` (fallback `window.parent.Xrm`) EVERY poll.** Caching at mount (harness v1.0.1) made `getPageContext()` go stale → "(no context)" after the mount record. Fresh re-read each tick (v1.0.2) tracked navigation perfectly. This is the single most important lesson; bake it into the capture service (task 030) and the `xrmContext` widening (task 010).
2. **Primary capture signal = the top-window URL** (`pagetype`/`etn`/`id`), cross-checked against `getPageContext()`. In the run, `top.url`, `top.gpc`, `parent.gpc` were identical on every tick — URL parsing is dependency-light and equally reliable; keep getPageContext as corroboration.
3. **Poll interval 1.5s** observed every visit with no misses. Spec's 0.5–2s assumption holds; 1.5s is a good default.
4. **`alwaysRender:true` + `canClose:false`** confirmed as the mechanism that keeps the pane JS running while collapsed — mandatory per NFR-05.
5. **WebApi**: reachable host-context from parent/top frame; use `retrieveMultipleRecords`. FR-03 "Edited" derivation (`_modifiedby_value eq {userId}`) is viable with no audit entity.
6. **Injection (Stage 2 / task 086)**: use the recovered global-ribbon enable-rule pattern (`spike/sprk_application_ribbon_sidepanespike.xml`), landed via solution import / `ribbon-edit`. Historically shipped (git `7d80565a6^`); singleton guard prevents duplicate panes on re-eval.
7. **Capture granularity (design input for FR-02)**: the poll sees BOTH `entityrecord` and `entitylist` visits. Decide whether "Recent viewed" records lists too or filters to `entityrecord` only.

## Verdict

> ✅ **GO** — Path B is viable on the current UCI. Framework tasks (010, 011, 020) are unblocked. Auto-injection carried to task 086 (Stage 2) with the recovered ribbon pattern; if that specific mechanism regressed on current UCI, it is a deployment-mechanism choice (visible-button `openPane` fallback), NOT a Path-A re-baseline.

### Path A fallback (only if NO-GO)
Launch-on-open: no global bootstrap; the pane is opened on demand (e.g. from the recovered visible global-ribbon button `openPane()` alone, or a per-app command), and **capture starts at first open** rather than at app load. FR-02 (continuous capture) narrows to "capture while the pane is open"; FR-03 unaffected (still a WebApi query). This trades always-on history for a reliable, non-fragile mechanism. Raise the CLAUDE.md §6.5 "ADR Conflict / Resolution Required" note for owner sign-off before re-baselining the spec.

---

## Post-verdict discovery — global auto-launch plumbing ALREADY LIVE in spaarkedev1 (2026-08-13)

Retrieved the spaarkedev1 **application ribbon** (`RetrieveApplicationRibbon`). It still contains the purpose-built hidden global bootstrap from the retired side-pane platform — the wiring survived even though the code was deleted (git `7d80565a6`):

```xml
<CommandDefinition Id="sprk.Global.SidePaneManager.Command">
  <EnableRules><EnableRule Id="sprk.Global.SidePaneManager.EnableRule" /></EnableRules> …
</CommandDefinition>
<EnableRule Id="sprk.Global.SidePaneManager.EnableRule">
  <CustomRule FunctionName="Spaarke.SidePaneManager.initialize"
              Library="$webresource:sprk_SidePaneManager" Default="false" />
</EnableRule>
```

- Command + enable rule are present; the `sprk_SidePaneManager` **web resource is deleted**, so the rule is currently **dormant** (fires into nothing).
- The sibling **SprkChat** global button (`sprk.Global.SprkChat.*` → `sprk_openSprkChatPane`, WR still present) is **fully live**.
- Calendar/EventDetail panes are **code-launched** by `DataGridSidePaneOrchestrator` — NOT this ribbon.

**Impact on Stage 2 / task 086 (major de-risk):** app-load auto-launch likely needs **NO ribbon import**. Recreating the `sprk_SidePaneManager` web resource (exposing `Spaarke.SidePaneManager.initialize`, registering the Navigator pane) should reactivate the existing rule. One open sub-question: whether a *control* still pins `sprk.Global.SidePaneManager.Command` to the bar so the rule fires — cheapest to confirm empirically (redeploy the WR, reload app, watch for auto-appear). Carried into task 086. Fallback if the rule doesn't fire: the recovered `spike/sprk_application_ribbon_sidepanespike.xml` adds a fresh global button, or reuse the still-live SprkChat slot pattern.

---

*Harness built + validated 2026-08-13 (task-execute, FULL rigor). Verdict: GO. Plumbing-reuse finding recorded for task 086.*
