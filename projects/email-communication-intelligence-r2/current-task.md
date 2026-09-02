# Current Task State — email-communication-intelligence-r2

> **Last Updated**: 2026-09-02 (context-handoff — Outlook add-in UX redesign in progress: Slices 1–2 done + 4 UI-feedback rounds, Slice 3 = BFF-backed inline "New record" being scoped)
> **Recovery**: Read "Quick Recovery" first.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Active work** | **Outlook add-in UX redesign** (UAT-driven, owner 2026-09-01/02). Iterating in the add-in's **own browser test harness** (`npm run start:outlook` → `https://localhost:3000/outlook/taskpane-test.html`, mock Office + mock auth + demo data). NOT the spaarke-prototype (that's Xrm-based; the add-in is Office.js). |
| **Branch** | `work/email-communication-intelligence-r2` — **7 commits ahead of master, all committed, tree clean.** Nothing merged yet (WIP on branch). |
| **Done** | **Slice 1** (`220070ed3`): consolidated toolbar (logo removed per feedback; tabs left + ⋮ overflow right) + "Related to" rename. **Slice 2** (`9377fff79` + refinements `9d799dfd2`, `c16a5b718`, `c881d6c76`): "Related to" = reconciliation-style **auto-match cards** (record + type + % match + check) + single-select type chips (default Matter, gray/blue, left of label) + green-check+× select state + wizard footer (Cancel left / Save right) + AI-processing toggles removed (always-on). Plus the earlier **nav + inline Create To Do** (`71d93eb12`). |
| **In progress** | **Slice 3 — BFF-backed inline "New record"** (behind the `+ New` button). Owner chose "BFF-backed inline create" over open-in-browser. Scope defaulted to **Matter + Project, minimal fields** (owner didn't pin it). An Explore agent is mapping the create mechanism (wizard path / existing BFF endpoint / required fields / impersonated-create pattern) before building. |
| **Next** | Read the agent's create-mechanism map → build the BFF create endpoint (§10 governance: placement justification, publish-size + CVE, tests, ADR-024 impersonation) + the add-in inline create form → auto-select the created record as Related-to. |

---

## Add-in redesign — architecture + key files

**Reconciliation reuse verdict (Explore agent, this session):** the add-in CANNOT reuse the reconciliation UI components (`EmailConnectionsReview`/`RelatedToCell`/`ReconciliationWorkspace`) — they're bound to a Dataverse `Xrm.WebApi` host the add-in doesn't have. It CAN reuse (and already does) the **host-agnostic candidate logic** (`@spaarke/communication-components/logic/connections/provenance` → `derivePrimaryReview`) + the **BFF suggestion endpoint** (`GET /api/office/communications/by-message-id/{id}/suggestions`, returns candidates with `ReinforcedConfidence` = % + server-resolved names). So we render **our own Fluent v9 cards** from that data. The **regarding is written at SAVE** (existing path) — Confirm on a card only *selects*; no new "confirm-regarding" BFF endpoint needed.

**Client files (all `src/client/office-addins/`):**
- `shared/taskpane/components/RelatedToPicker.tsx` — NEW: the auto-match cards + chips + search + select. Rewritten across 4 feedback rounds.
- `shared/taskpane/components/SaveFlow.tsx` — swapped EntityPicker → RelatedToPicker; removed AI-processing UI (defaults stay `{profileSummary:true, ragIndex:true}`); wizard footer (Cancel/Save); `relatedSearch` (GET `/api/office/search/entities?q=&type=&top=`); `handleCreateNew` (currently routes to onQuickCreate — Slice 3 replaces with BFF create). DEMO_RELATED_CANDIDATES seeded when `isBrowserTestMode()`.
- `shared/taskpane/services/communicationSuggestionsService.ts` — added `fetchRelatedCandidates()` + `RelatedCandidate` (candidates WITH confidence) alongside the existing single `fetchEnginePreSelection`.
- `shared/taskpane/components/TaskPaneToolbar.tsx` — NEW: single toolbar (tabs left + ⋮ overflow). `TaskPaneShell.tsx` uses it (dropped separate Header+Nav rows). `TaskPaneNavigation.tsx` exports `getAvailableTabs`.
- `shared/taskpane/components/views/CreateTodoView.tsx` — NEW inline Create To Do (title/due/regarding → `POST /communications/{id}/create-task`); `App.tsx` renders it under the `createTodo` tab; `outlook/taskpane/index.tsx` seeds demo saved-context + mocks auth in test mode.
- `outlook/taskpane/taskpane-test.html` — sets `window.__SPAARKE_TEST_MODE__ = true` (drives auth mock + demo data).

**Test-harness pattern:** `window.__SPAARKE_TEST_MODE__` (set in taskpane-test.html) → index.tsx mocks auth (no real Entra); SaveFlow seeds DEMO_RELATED_CANDIDATES; CreateTodoView gets a demo saved-context. Real `.env` has PLACEHOLDER GUIDs (tenant `…0002`) — that's why a real sign-in 404s; the harness bypasses it.

**Build/verify:** `cd src/client/office-addins && npm run build:dev` (webpack, babel transpile-only — the gate). `npm run typecheck` reports ~393 PRE-EXISTING errors (exactOptionalPropertyTypes / jest-dom not installed / etc.) — filter to changed files. Deps already installed (node_modules present).

---

## Still-open add-in items (post-redesign, before deploy)
- **Logo/app-tile**: manifest `icons.color`/`icons.outline` point at files that don't exist → blank Apps-list tile. (In-pane logo was removed per feedback; the app-tile icon is separate — still to fix at deploy.)
- **Production save-context wiring**: CreateTodoView's real `communicationId` + regarding must come from the Save flow (only demo-wired now).
- **Version bump + deploy + re-register** (Outlook manifest 1.0.20→ + Word) — after UX is locked. Add-in deploys via GitHub Actions `deploy-office-addins.yml` (task 044; live SWA `b64eb1876` on `icy-desert-0bfdbb61e.6.azurestaticapps.net`).
- **Jest**: `TaskPaneNavigation.test.tsx` (+ others) fail on missing `@testing-library/jest-dom` (pre-existing infra gap) — not blocking.

---

## Prior completed work (this project, merged to master)
- **#919 document-profile AI bug** — FIXED (PR #923, deployed to BFF). Renderer nested-string fix + convergence. Doc: `docs/architecture/DOCUMENT-PROFILE-AND-AI-EXECUTION-MODELS.md`.
- **Document Upload wizard "Send Email"** — rebuilt (dead-form fix #925 → EmailComposer #927 → Finish-guard #929 → centering #930), all merged + deployed to `sprk_documentuploadwizard`.
- **Task 044 add-in deploy** — deployed + current (3 deploys; live SWA `b64eb1876`); only the interactive live smoke remained, and owner UAT confirmed the core save flow works.
- **R-1/R-2/R-3 remediation** (affinity write / Compose content-dedup graduate-on-divergence / orphan cleanup) — all shipped + merged.

## Merge/deploy reference
- Master PROTECTED (ruleset `21824191`, required check literal `Router`). Use `gh pr create` + `gh pr merge {n} --auto --merge`. Always `git fetch origin && git merge origin/master` before pushing.
- Add-in: GitHub Actions `deploy-office-addins.yml` (holds env secrets); manifests re-registered via M365 admin (version bump required for re-upload).
