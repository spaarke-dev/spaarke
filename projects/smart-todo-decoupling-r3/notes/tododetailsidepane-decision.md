# TodoDetailSidePane — Consumer Audit & Decision (Task 080)

> **Status**: AUDIT COMPLETE
> **Recommendation**: **RETIRE**
> **Date**: 2026-06-08
> **Author**: smart-todo-decoupling-r3 / task 080
> **Resolves**: Assumption **A-2** (`spec.md` §Assumptions)
> **Feeds**: Task 081 (retire or refactor execution)

---

## 1. Summary

| Item | Value |
|---|---|
| Solution scanned | `src/solutions/TodoDetailSidePane/` |
| Solution name (deployment) | `TodoDetailSidePane` (Tier 3) |
| Web resource (per docs) | `sprk_tododetailsidepane` |
| **Hard consumers found** | **0** |
| **Soft consumers found** | 5 (dev/test/docs refs only) |
| **Recommendation** | **RETIRE** |

The audit found **zero runtime consumers** of `TodoDetailSidePane` anywhere in `src/`. The only `Xrm.App.sidePanes.getPane("todoDetailPane")` call lives inside the side pane's own `App.tsx` (a self-close handler). No other Code Page, web resource, ribbon JS, sitemap, ribbon XML, plugin, or BFF endpoint creates / opens / navigates to / references the `sprk_tododetailsidepane` web resource. The side pane is functionally orphaned in the current R3 architecture: the consolidated `SmartTodo` Code Page (R2) replaced the BroadcastChannel-coupled detail-pane pattern with an inline `TodoDetailPanel` rendered in the same React tree.

---

## 2. What TodoDetailSidePane Actually Is

A standalone React 19 + Vite single-file Code Page targeted at `Xrm.App.sidePanes` as a 400 px slide-in:

```
src/solutions/TodoDetailSidePane/
├── package.json              "todo-detail-sidepane" — builds tododetailsidepane.html
├── index.html
├── vite.config.ts            vite-plugin-singlefile; assetsInlineLimit: 100 MB
└── src/
    ├── main.tsx              React 19 createRoot → <App />
    ├── App.tsx               Reads ?eventId=… from URL; parallel-loads:
    │                           - sprk_event (loadTodoRecord)
    │                           - sprk_eventtodo (loadTodoExtension)  ← LEGACY entity (FR-02 deletes it)
    │                         Notifies SmartTodo parent via BroadcastChannel after save.
    │                         Renders <TodoDetail> from @spaarke/ui-components.
    ├── services/
    │   ├── sidePaneService.ts    closeSidePane(), openEventForm("sprk_event", …)
    │   └── todoService.ts        loadTodoExtension(), saveTodoExtensionFields(),
    │                             deactivateTodoExtension() — all hit sprk_eventtodo
    │                             via `_sprk_regardingevent_value` (LEGACY field)
    └── utils/
        ├── parseParams.ts        URL ?eventId parser
        ├── broadcastChannel.ts   sendTodoSaved(eventId) — IPC to SmartTodo
        └── xrmAccess.ts          Xrm bootstrap (current → parent → top)
```

**Key R3-relevant traits**:
1. **Built on the legacy two-entity model**: imports `loadTodoExtension`, `saveTodoExtensionFields`, `deactivateTodoExtension`, `ITodoExtension`, `IEventFieldUpdates`, `ITodoExtensionUpdates` from `@spaarke/ui-components`. All six of those names are deleted in **FR-09** (simplify `TodoDetail` to single-entity).
2. **Hard-coded `sprk_eventtodo` query**: `services/todoService.ts` filters by `_sprk_regardingevent_value` and deactivates via `setRecordState("sprk_eventtodos", …)`. The `sprk_eventtodo` entity is deleted in **FR-02**.
3. **Hard-coded `sprk_todoflag` write**: `App.tsx` `handleRemoveTodo` does `saveTodoFields(evtId, { sprk_todoflag: false })`. `sprk_todoflag` is deleted in **FR-03**.
4. **BroadcastChannel IPC to SmartTodo**: that channel was the R1/early-R2 contract; R2's consolidated SmartTodo Code Page replaced it (see `projects/events-smart-todo-kanban-r2/tasks/051-remove-broadcastchannel-legalworkspace.poml`).

After Phase 1 (schema cut) and Phase 2/3 (shared-lib + SmartTodo repoint), the side pane is non-functional even before retirement: every Xrm.WebApi call inside it will 400.

---

## 3. Consumer Audit Findings

### 3.1 Hard consumers (production runtime depends on this side pane)

**NONE.**

Specifically verified:

| Surface | Search | Result |
|---|---|---|
| Ribbon command IDs / XML | `Grep(TodoDetailSidePane\|tododetailsidepane)` in `src/**/*Ribbon*.xml`, `EventRibbonDiffXml.xml`, all entity `RibbonDiff.xml` | **0 matches** |
| Web-resource JS (ribbon button handlers) | `Grep([Tt]odo[Dd]etail[Ss]ide[Pp]ane\|sprk_tododetail)` in `src/client/webresources/` | **0 matches** |
| Sitemap XML | `Glob(src/**/SiteMap.xml)` | **No files found** (sitemaps live inside packed solution zips, not unpacked in repo) |
| Solution customizations.xml | `Grep([Tt]odo[Dd]etail)` in all `customizations.xml` | **0 matches** |
| Code Pages / Code-page launchers | `Grep(sprk_tododetailsidepane\|tododetailsidepane\.html)` in `src/` | **0 matches** outside the solution's own `package.json` rename step |
| Office add-in manifests | `Grep([Tt]odo[Dd]etail)` in `src/client/office-addins/` | **0 matches** |
| Dataverse plugins / BFF | `Grep(TodoDetailSidePane\|sprk_tododetail)` in `src/dataverse/`, `src/server/` | **0 matches** |
| PCF manifests / controls | `Glob` of PCF solution dirs + grep | **0 matches** referencing the side pane |
| `Xrm.App.sidePanes.createPane({ pageInputType: "webresource", … })` | `Grep(createPane)` in `src/` | 4 callers found, **none** target `sprk_tododetailsidepane`: `sprk_openSprkChatPane.js` (SprkChat), `VisualHost/ClickActionHandler.ts` (visual-host click action), `DataGridSidePaneOrchestrator.ts` (grid side pane), `xrmContext.ts` type def |
| `Xrm.Navigation.navigateTo({ pageType: "webresource", … })` to the page | `Grep` for variations | **0 matches** |
| In-product opener | `Grep(sprk_tododetailsidepane)` in `src/` | **0 matches** anywhere in `src/`; only docs + completed project artefacts |
| `getPane("todoDetailPane")` invocations | Self-close in `App.tsx:151` | **1 match — self-only** (the page closes itself; no external caller registers or opens this pane) |

The only `getPane("todoDetailPane")` call is the side pane's own `handleClose` — meaning whoever opens the pane must have used the literal pane id `"todoDetailPane"`. **No code in `src/` does this.** Whatever production opener existed (per R2 design notes: SmartTodo Code Page in R1) has already been removed; the R2 consolidation deleted the launch path in favour of inline `TodoDetailPanel`.

### 3.2 Soft consumers (dev / test / docs / build manifest — non-binding)

| Reference | Type | Action on retire |
|---|---|---|
| `scripts/Deploy-DataverseSolutions.ps1:138` — `"TodoDetailSidePane" = @{ … Tier = 3 }` | Build/deploy manifest | Remove entry |
| `scripts/Build-AllClientComponents.ps1:86` — `"TodoDetailSidePane"` in Vite Code Pages array | Build manifest | Remove entry |
| `docs/architecture/event-to-do-architecture.md:143,165` | Doc (already slated for supersession by FR-30) | Will be replaced by new `spaarke-todo-architecture.md` — no action needed beyond FR-30 |
| `docs/architecture/SIDE-PANE-PLATFORM-ARCHITECTURE.md:25` | Doc (already marked **SUPERSEDED** at top) | Optional cleanup |
| `docs/architecture/ui-dialog-shell-architecture.md:83`; `client-resources-inventory.md:157`; `SPAARKE-DEPLOYMENT-GUIDE.md:728`; `PRODUCTION-DEPLOYMENT-GUIDE.md:1144` | Doc inventory tables | Strike line during repo-cleanup (task 085) |
| `.claude/adr/ADR-026-full-page-custom-page-standard.md:122` | ADR inventory table (React 18 list) | Strike line during repo-cleanup |
| `docs/adr/ADR-027-…:121` | ADR Tier-3 enumeration | Strike entry |
| `src/solutions/SmartTodo/src/services/xrmProvider.ts:34`; `src/solutions/LegalWorkspace/src/services/xrmProvider.ts:34` | Code comments mentioning "TodoDetailSidePane" in iframe-Xrm-bootstrap context | Update comment (purely documentary; no behaviour change) |
| `src/solutions/SmartTodo/README.md:46` — "TodoDetail — reusable detail form (also used by TodoDetailSidePane)" | README | Edit (remove second clause) |
| `src/client/external-spa/src/components/SmartTodo.tsx:14` — `Design reference: src/solutions/TodoDetailSidePane/src/components/TodoDetail.tsx` | Code comment (the path it points at doesn't exist any more — already extracted to shared lib in R2) | Update comment to point at `@spaarke/ui-components/TodoDetail` |
| Historical project artefacts (`projects/events-smart-todo-kanban*/`, `projects/sdap-secure-project-module/`, `projects/spaarke-mda-darkmode-theme-r2/`, `projects/ui-create-wizard-enhancements-r1/`, `projects/x-production-environment-setup-r1/`, `projects/spaarke-workspace-user-configuration-r1/`) | Frozen project docs | **No action** — historical record, must not be edited |
| R3 project artefacts (`projects/smart-todo-decoupling-r3/{spec,design,plan,notes}.md`, this file) | Active R3 docs | No action (intentional references) |

---

## 4. Why "zero hard consumers" is the correct conclusion

Three converging lines of evidence:

1. **R2 architecture explicitly retired the side pane as the click-target.** Per `projects/events-smart-todo-kanban-r2/design.md` and `spec.md`, R2 merged the Kanban board + detail pane into a single Code Page (`sprk_smarttodo`) with an inline `TodoDetailPanel`. Card click → React state, not `Xrm.App.sidePanes.createPane(...)`. R2 task 010 only refactored the SidePane to *import* `TodoDetail` from the shared lib — it did not re-establish a runtime opener.
2. **R2 design §"TodoDetailSidePane Retirement" called out the exact A-2 condition.** Quote: *"Exception: If other surfaces (e.g., EventsPage list view) need a standalone detail editor, keep `TodoDetailSidePane` as a separate Code Page for those contexts only. Evaluate at R2 implementation time."* Searching for that exception case repo-wide returns **0 actual openers** — EventsPage does not open `sprk_tododetailsidepane`.
3. **The side pane is structurally broken by R3 Phase 1.** Even if a stealth opener existed, the page would fail the moment Phase 1 lands: `sprk_eventtodo` is deleted (FR-02), `_sprk_regardingevent_value` legacy meaning changes, `sprk_todoflag` is removed (FR-03). Continuing to ship it post-Phase 1 means shipping a guaranteed-runtime-error component. Per **NFR-12** (no compat shims) and **OS-1** (hard cut, pre-release), retention is not viable.

A "refactor to a thin shell" alternative would require:
- Rewriting `services/todoService.ts` against `sprk_todo` (single-entity load/save)
- Removing the BroadcastChannel (its consumer in R2 SmartTodo was already removed by R2 task 051)
- Deleting `handleSaveTodoExtFields`, `handleDeactivateTodoExt`, `handleRemoveTodo`'s `sprk_todoflag` write
- Reducing `App.tsx` to `<TodoDetail recordId={…} onClose={…} />`

That is roughly **a full rewrite of the page** to no benefit, since **no opener exists**. The refactor would produce a Code Page that nothing launches. Per A-2's "default retirement unless an unavoidable consumer is found", the recommendation is unambiguous.

---

## 5. Recommendation: **RETIRE**

Delete the solution + the build/deploy manifest entries. No replacement Code Page is needed — the inline `TodoDetailPanel` inside `sprk_smarttodo` covers every current use case, and the eleven parent-form "To Dos" subgrids (FR-17) plus `Xrm.Navigation.openForm("sprk_todo", id)` covers any "open a single todo" need that arises after R3.

---

## 6. Ordered removal checklist for task 081

> Task 081 should consume this checklist directly.

### 6.1 Pre-deletion (do before removing source)

- [ ] **6.1.a** Confirm `sprk_tododetailsidepane` web resource is not currently active in the target Dataverse environment(s). If present, remove from any solution it belongs to (managed: uninstall the solution; unmanaged: delete the web resource record). Use `dataverse-deploy` skill or maker portal.
- [ ] **6.1.b** Confirm no published solution `TodoDetailSidePane` exists in the environment(s) — if found, export current state (defensive copy), then delete the solution.

### 6.2 Source-tree deletion

- [ ] **6.2.a** Delete the entire directory `src/solutions/TodoDetailSidePane/`.

### 6.3 Build / deploy manifest cleanup

- [ ] **6.3.a** `scripts/Deploy-DataverseSolutions.ps1` — remove line 138 (`"TodoDetailSidePane" = @{ … Tier = 3 }`).
- [ ] **6.3.b** `scripts/Build-AllClientComponents.ps1` — remove line 86 (`"TodoDetailSidePane"` from the Vite code-pages array).

### 6.4 Source-comment cleanup (purely documentary — no behaviour change)

- [ ] **6.4.a** `src/solutions/SmartTodo/src/services/xrmProvider.ts:34` — drop "(e.g. TodoDetailSidePane)" from the iframe-Xrm-bootstrap comment.
- [ ] **6.4.b** `src/solutions/LegalWorkspace/src/services/xrmProvider.ts:34` — same comment edit.
- [ ] **6.4.c** `src/solutions/SmartTodo/README.md:46` — strike "(also used by TodoDetailSidePane)" from the TodoDetail row.
- [ ] **6.4.d** `src/client/external-spa/src/components/SmartTodo.tsx:14` — update `Design reference:` comment to point at `@spaarke/ui-components/TodoDetail` (the actual current location).

### 6.5 Documentation cleanup (low-priority; can defer to repo-cleanup task 085 / FR-30)

- [ ] **6.5.a** `docs/architecture/event-to-do-architecture.md` — already slated for supersession per FR-30. New `spaarke-todo-architecture.md` should not mention `TodoDetailSidePane`.
- [ ] **6.5.b** `docs/architecture/SIDE-PANE-PLATFORM-ARCHITECTURE.md:25` — strike `TodoDetailSidePane` from the "What still exists" list (the doc is already marked SUPERSEDED).
- [ ] **6.5.c** `docs/architecture/ui-dialog-shell-architecture.md:83` — strike `TodoDetailSidePane` from the SidePaneShell consumers list.
- [ ] **6.5.d** `docs/architecture/client-resources-inventory.md:157` — remove the TodoDetailSidePane row.
- [ ] **6.5.e** `docs/guides/SPAARKE-DEPLOYMENT-GUIDE.md:728` & `docs/guides/PRODUCTION-DEPLOYMENT-GUIDE.md:1144` — strike `TodoDetailSidePane` from the Tier-3 listings.
- [ ] **6.5.f** `.claude/adr/ADR-026-full-page-custom-page-standard.md:122` and `docs/adr/ADR-027-…:121` — strike from inventory tables.

### 6.6 Verification

- [ ] **6.6.a** Repo-wide `Grep(TodoDetailSidePane|tododetailsidepane|sprk_tododetailsidepane)` returns matches **only** in: (a) frozen historical `projects/events-smart-todo-kanban*` and other completed projects; (b) the R3 project's own files (this doc, spec.md, plan.md, the eventtodo-reference-audit, tasks 080 / 081, TASK-INDEX.md). No matches in `src/`, `scripts/`, `docs/architecture/`, or `docs/guides/`.
- [ ] **6.6.b** `npm run build` (or `scripts/Build-AllClientComponents.ps1`) completes successfully — no missing-module errors from the removed solution.
- [ ] **6.6.c** Solution export from target env post-uninstall confirms `TodoDetailSidePane` solution and `sprk_tododetailsidepane` web resource are gone.

---

## 7. Rationale tie-back to spec

| Rule | How retirement satisfies it |
|---|---|
| **A-2** (default retire unless unavoidable consumer found) | Audit found zero hard consumers ⇒ default applies. |
| **OS-1** (no compat shims) | Refactor would require keeping a side-pane Code Page that nothing launches — pure dead weight. Retirement is the cleaner pre-release cut. |
| **FR-02** (delete `sprk_eventtodo`) | Side pane's `todoService.ts` queries `sprk_eventtodos` directly and would 404 post-Phase-1. Retirement avoids the dilemma. |
| **FR-03** (remove `sprk_todoflag` from `sprk_event`) | `handleRemoveTodo` writes `sprk_todoflag = false`; would 400 post-Phase-1. |
| **FR-09** (simplify `TodoDetail` to single-entity) | The side pane was the original two-entity caller; the shared-lib API's two-entity surface (`loadTodoExtension` et al.) is being deleted. The side pane would not compile post-FR-09. |
| **FR-29** (delete all `sprk_eventtodo` / legacy field code paths) | The side pane is one of the largest concentrations of such code (~30+ lines per `eventtodo-reference-audit.md` section 2D). Deleting the directory deletes them in one move. |
| **NFR-12** (pre-release no-compat rule) | Retirement avoids a thin-shell that has no production opener — that thin-shell would itself be a soft-compat surface. |

---

## 8. Unexpected findings

None. The audit confirmed what R2 design notes and R3 design §A-2 predicted: the side pane was already orphaned by the R2 consolidation, and R3's schema cut would have made retention untenable anyway. No external solution, no Office add-in, and no PCF was found to depend on the page.

One minor housekeeping observation:

- `src/client/external-spa/src/components/SmartTodo.tsx:14` still cites the **pre-R2** path (`src/solutions/TodoDetailSidePane/src/components/TodoDetail.tsx`) as its design reference. That path no longer exists — `TodoDetail` was hoisted to `@spaarke/ui-components` in R2 task 003. The comment is harmless but stale; included in checklist §6.4.d for cleanup.

---

*End of decision doc. Task 081 should treat §6 as binding execution checklist.*
