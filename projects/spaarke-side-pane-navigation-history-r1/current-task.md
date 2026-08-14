# Current Task State — spaarke-side-pane-navigation-history-r1

> **Last Updated**: 2026-08-14 (by context-handoff)
> **Recovery**: Read "Quick Recovery" first. Branch `work/side-pane-navigation-history-r1` @ `5d8666c8e`, 0 behind master, pushed, **NOT merged to master** (holding until post-UAT sign-off).

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Phase** | 086 deploy + UAT iteration (feature complete; owner UAT in progress) |
| **Status** | in-progress — 2 approved next actions, then owner UAT, then 087 + 090 |
| **Env** | spaarkedev1 (`https://spaarkedev1.crm.dynamics.com`), app Matter Management `729afe6d-ca73-f011-b4cb-6045bdd8b757` |
| **Next Action 1** | Create a **TodoRibbons** unmanaged solution (contains only `sprk_todo`) → apply the SAME Navigator enable-rule ribbon pattern → `pac solution import --publish-changes`. |
| **Next Action 2** | Add the one-liner `ensureNavigatorSidePane()` (from `@spaarke/ui-components`) to the **Email** + **CommunicationReconciliation** code pages' mount. Code change on this branch is fine; **DEPLOY is coordination-gated** with `email-communication-intelligence-r2` (they co-own those code pages — land via master, r2 redeploys). |
| **Then** | Owner continues UAT → **087** UI-test (light+dark) → **090** wrap (test-diet, lessons-learned, archive). |

### Critical Context
The Navigator side pane is fully deployed and working on spaarkedev1. Auto-load is SOLVED via two insertions: (a) an **entity ribbon enable-rule** (`CustomRule` → `Spaarke.SidePaneManager.initialize`, `Default=true`) that fires **silently** when an entity's grid/form command bar loads — proven on Matter, rolled to Document/Project/Event/Communication; (b) a **code-page registrar** `ensureNavigatorSidePane()` (SpaarkeAi home). Modern UCI has NO supported global page-load hook, so both insertions are needed; only OOB dashboards remain a gap. Full reference: `docs/architecture/SPAARKE-SIDE-PANE-NAVIGATION.md`.

---

## Full State (Detailed)

### DONE & LIVE on spaarkedev1
- **Waves 001→090 code**: framework host + `sprk_navitem` entity (deployed) + capture engine + NavigatorPane code page + Recent/Bookmarks/Monitored/Views + search + retention + security-trim + stub proof. 18/21 POML tasks ✅ (086 deployed*, 087/090 pending).
- **NavigatorPane webresource** `sprk_NavigatorPane.html` (id 6cebb920-…): 4 tabs (Recent · Bookmarks · Monitored · Views), top search (Ctrl/Cmd+K), record-type icons on left, inline bookmark rename (pencil), "Pin this page" on code pages (weblink named from page title), Views open the real view **in-app** (`navigateTo` viewType STRING `'userquery'`).
- **Bootstrap** `sprk_SidePaneManager` (JScript, id c5ae4b21-…): Path B `createPane` (canClose:false, alwaysRender:true, imageSrc star) → navigate to sprk_NavigatorPane.html; exposes `Spaarke.SidePaneManager.initialize` (enable-rule side-effect) + `openPane` (button).
- **Rail icon** `sprk_navigatorstar.svg` (id 287a3e97-…): outline star `#424242` (served outline; UCI rail may render filled — its styling, not controllable). Command buttons use the star via `ModernImage="$webresource:sprk_navigatorstar.svg"`.
- **Capture bug FIXED**: `startNavigatorCapture()` was never called — now started from `NavigatorBody` mount (guarded); Recent populates as you navigate with the pane docked.
- **Auto-load ribbons** (silent enable-rule + star button on grid+form): **Matter, Document, Project, Event, Communication** `*Ribbons` solutions imported+published. Modified customizations.xml saved under `deploy/ribbon/` (gitignored).
- **Code-page registrar**: reusable `ensureNavigatorSidePane()` in `@spaarke/ui-components` (barrel export) + `SpaarkeAi/src/ensureNavigatorPane.ts` thin wrapper (App.tsx unchanged, deployed).
- **Docs**: `docs/architecture/SPAARKE-SIDE-PANE-NAVIGATION.md` (system + two-insertion auto-load recipe + modern-UCI caveats).

### KNOWN CAVEATS (documented)
- No supported global app-load JS hook on modern UCI (global ribbon enable-rule regressed; AppModule onload unsupported; form/grid events only). OOB dashboards = the one auto-load gap.
- `navigateTo` view selection: `viewType` must be STRING `'userquery'`/`'savedquery'` (NOT numeric); UCI's per-table **sticky view selector** may still override exact selection.
- UCI renders rail `imageSrc` icons with its own styling (filled look) — command-button icon is the controllable one.
- Deploy-NavigatorPane.ps1 create-path uses `-ResponseHeadersVariable` (needs pwsh7); SVG first-create was done via Web API. Existing WRs PATCH fine.

### KEY DEPLOY COMMANDS
- NavigatorPane WRs: `powershell -File src/solutions/NavigatorPane/Deploy-NavigatorPane.ps1` (html + bootstrap + star svg).
- SpaarkeAi: build `cd src/solutions/SpaarkeAi && npx vite build && rename dist/index.html→spaarkeai.html` then `powershell -File scripts/Deploy-SpaarkeAi.ps1`. (Needs `npm install --legacy-peer-deps` for SpaarkeAi + LegalWorkspace; the `npm run build` surface-gate trips on pre-existing merge test-type errors — use `npx vite build` directly.)
- Ribbon: ribbon-edit skill — export `{Entity}Ribbons` → insert the 4 Navigator blocks → `pac solution import --publish-changes` (pac active profile = SPAARKE DEV 1).

### OPEN (non-blocking) DECISIONS
- 052 "assigned to me" Monitored axis: ships owner-only (Path A). Owner may later opt into the BFF membership resolver (Path B).
- 021 end-user security role for `sprk_navitem` owner-scoped CRUD: UAT runs as System Admin; name a role when ready + 2-user isolation test.

### NEXT WAVES
- Approved now: TodoRibbons + code-page registrar rollout (Email/Reconciliation, r2-coordinated).
- 087 UI-test (light+dark) → 090 wrap (/test-diet gate + lessons-learned + archive) → `/worktree-sync` full (merge to master) after UAT sign-off.

## Decisions log
- 2026-08-14: Auto-load solved via entity-ribbon enable-rule (silent) + code-page registrar; global ribbon/AppModule approaches rejected (regressed/unsupported). Single-contributor pane mounts NavigatorBody directly under a root FluentProvider (NOT SprkSidePaneHost) — the blank-render fix. Views nav = navigateTo viewType STRING 'userquery' (in-app). Rail icon outline star.
- 2026-08-13: 086 deployed; blank-render fixed (Calendar pattern); ribbon global-button reverted (regressed on UCI).
