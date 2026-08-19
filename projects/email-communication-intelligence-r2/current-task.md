# Current Task State — email-communication-intelligence-r2

> **Last Updated**: 2026-08-19 (by context-handoff — UAT round 4 restore point)
> **Recovery**: Read "Quick Recovery" first. This is a FRESH-SESSION restore point after operator UAT.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Phase** | Reconciliation UX **prototype-parity** — Batches 1/2 (B2.1 attachment-text, B2.2 undo, B2.3 badges) are **DONE + DEPLOYED + MERGED TO MASTER** (PR #798). Operator ran **UAT round 4** and returned **8 new fixes** (below). None started yet. |
| **Branch** | `work/email-communication-intelligence-r2` · **clean** · all prior work merged to master · currently ~4 behind master (active repo — **re-sync master before building**). **master IS UNPROTECTED** (PR merges land instantly; the merge-to-master skill's "protected" note is stale). |
| **Deployed (dev, spaarkedev1)** | BFF `spaarke-bff-dev` (healthy) + code page `sprk_communicationreconciliation` (`1e191e05-cc96-f111-b8dc-7ced8ddc4a05`) + SpaarkeAi `sprk_spaarkeai` (`5206a442-3451-f111-bec7-7ced8d1dc988`) — all built from CURRENT master. |
| **Next Action** | Start UAT-round-4 fixes below. Recommend: **re-sync master → fix in priority order → build/test → deploy both surfaces + BFF (only if item needs it) → operator re-UAT**. Do NOT merge to master until operator says so. |

### FIRST STEPS in a fresh session
1. `git fetch origin && git merge origin/master --no-edit` (repo is very active; expect clean).
2. Read the prototype (the spec): `c:/code_files/spaarke-prototype/projects/email-communication-intelligence-r2-uat/src/App.tsx` — it is the source of truth for the reconcile modal UI.
3. Screenshots for this round are in the operator's UAT message (2026-08-19) — 3 side-by-side prototype(left)/live(right) images of the Related-to / Fields / Tasks tabs, plus the code-page grid.

---

## UAT ROUND 4 — the 8 fixes (operator, 2026-08-19)

### Item 1 — Code page: TWO view dropdowns, want ONE
The `sprk_communicationreconciliation` code page shows **two** view selectors: (a) the app-level heading dropdown ("Email Review Completed" ▾ with Active Communications / All Incoming Email / All Outgoing Email / Messages / All Email / Inactive Communications), AND (b) the dataset grid's own Communication view dropdown just below it. **There should be ONE subgrid/view selector for the Email review.**
- **Where**: `src/solutions/CommunicationReconciliation/src/**` (the host wiring `views` into `ReconciliationWorkspace`) + `ReconciliationWorkspace.tsx` `DataGridViewSelector` (renders when `views.length>1`, `s.viewBar`) + `ReconciliationGrid` (may render its OWN view dropdown). Dedupe: keep one, remove the other.

### Item 2 — Reconcile modal UI does not match the prototype
Compare prototype (LEFT of each screenshot) vs live (RIGHT).

**2a/2b/2c — LEFT EDGE CLIPPED** on the reconcile browse tab's right pane: the Related-to **record cards**, the **"Look up another record"** field, and the **"+ New record"** button are all cut off on their left side. The prototype has proper left padding. Likely a negative margin / missing left padding / `overflow` clip on the tabs pane or the `EmailConnectionsReview` reconcile root.
- **Where**: `ReconciliationBrowseShell.tsx` (`tabsPane` — has `overflowY:auto`; check horizontal clip + padding), `ReconciliationWorkspace.tsx` (`s.tabRoot`/`s.tabBody`/`s.tabList` padding), `EmailConnectionsReview.styles.ts` (`root`, `cardsStack`, `lookupField`, `newRecordFullWidth`). Add left padding / fix the clip so nothing is cut off.

**2d — "+ New record" wizard must LOAD THE .eml FILE** so it runs through the wizard's AI-profile + pre-fill (like the standard create wizard). Today the wizard launches un-seeded.
- This is the E1c path (`resolveEmlSource` → `onLaunchCreateRecord({emlSource})`). Known blocker (project item **064**): the `.eml` archive is linked via `sprk_document.sprk_relatedcommunication` (NOT `sprk_communication`); if no archive doc is linked → BFF `from-document` 404 → wizard un-seeded. **Verify** (a) `resolveEmlSource` resolves the archive doc id for the row (mirror `ReconciliationWorkspace.resolveEmlArchive` which uses `_sprk_relatedcommunication_value`), (b) the code page's `onLaunchCreateRecord` passes `emlSource` into the wizard's AI-prepopulate step, (c) whether it's a DATA gap (no archive doc) vs a wiring gap.
- **Where**: `ReconciliationWorkspace.tsx` (`resolveEmlSource` prop, `wrapReview`, `onLaunchCreateRecord`), the code page host wiring, the Create*Wizard AI-prepopulate step.

**2e — Email reader is MISSING the ATTACHMENTS** that were sent in the email. The prototype shows attachments; live shows none.
- B2.1 folds attachment TEXT via `GET /api/communications/{id}/attachments/text`, but the attachments aren't appearing at all. Investigate: does `ReconciliationWorkspace.resolveAttachments` return rows for these emails (query `sprk_communicationattachment` by `_sprk_communication_value`)? Does the attachment-text endpoint return items? Does `EmailBodyView` render the attachment names/folds? The operator likely wants to SEE the attachment list (names), not only folded text. **Test emails** "Test Email with Attachments 12" (visible bottom of screenshot 2) + the Henderson thread ("four attachments").
- **Where**: `ReconciliationWorkspace.tsx` (`resolveAttachments`, `resolveBrowseDetail`, `mergeAttachmentText`), `EmailBody/EmailBodyView.tsx` (attachment fold render), BFF `CommunicationAttachmentTextService` (does it return the attachments?).

**2f — Related-to: keep ALL candidates switchable even after a match** (behavior change). When the intelligence auto-matches/confirms a record, the Related-to tab should STILL show the other close-matching candidate cards **with a `Confirm` button** so the user can SWITCH the association. The **auto-matched** card's button should be **`Undo`** — clicking it resets that card back to `Confirm` (un-confirm).
- Today the reconcile variant HIDES the candidate cards once confirmed and shows only the green "Filed to X" banner + a single Undo. Change to: **always render all candidate cards**; the confirmed one → `Undo` (un-files, back to Confirm); the others → `Confirm` (clicking switches the primary regarding).
- **Where**: `EmailAssociationsAndTracking/EmailConnectionsReview.tsx` (the `isConfirmed` branch that currently hides cards + the "Filed to" banner), `EmailConnectionsReviewRows.tsx` (`CandidateCard` compact — add per-card Undo state for the confirmed card; switch calls `confirmCandidate` on a different card). Note the switch write path: `applyRegardingSelection` (additive) already supports downgrade/switch; Undo = `clearPrimaryRegarding`.

**2g — Fields tab EMPTY STATE missing "Update other fields"**. When there are NO field proposals ("No field updates proposed for this record"), the **"+ Update other fields"** button is missing. The prototype/first email shows it. Show it in the empty state (whenever `regarding` is confirmed), not only when proposals exist.
- **Where**: `ReconcileTabs/FieldUpdateReconcileTab.tsx` — the `updateOther` button (`data-testid="field-reconcile-update-other"`) is currently rendered only alongside the proposal list / gated; render it in the empty/ready state too.

**2h — Tasks tab EMPTY STATE missing "+ New task"**. When there are NO task proposals ("No tasks proposed for this record"), the **"+ New task"** affordance is missing. Show it in the empty state (whenever `regarding` is confirmed).
- **Where**: `ReconcileTabs/TaskReconcileTab.tsx` — the ad-hoc "+ New task" button; render it in the empty/ready state too.

### What UAT CONFIRMED working (do not regress)
- Related-to cards now stack vertically (card-fix). Progress badges render (`Related ✓ · Fields 0/2 · Tasks 0/1`). TRIAGE box, Date row, Open-original .eml link, Close/Save footer, friendly field labels, per-line Undo (Fields/Tasks), expanded Status dropdown, Save & confirm footer.

---

## Build / deploy / test reference
- **Re-sync first**: `git fetch origin && git merge origin/master --no-edit`.
- **Shared lib**: `cd src/client/shared/Spaarke.Communication.Components && npm install --legacy-peer-deps --no-audit --no-fund && npm run build` (revert incidental `package-lock.json` churn after).
- **Code page**: `cd src/solutions/CommunicationReconciliation && rm -rf dist node_modules/.vite .vite && npm run build` → `dist/sprk_communicationreconciliation.html`.
- **SpaarkeAi**: `cd src/solutions/SpaarkeAi && rm -rf dist node_modules/.vite .vite && npm run build` → `dist/spaarkeai.html` (tsc-surface-gate: only `Surface-owned: 0` matters; ~250 pre-existing shared-lib errors deferred).
- **Deploy web resource** (upload+publish, no rebuild): `pwsh scripts/Deploy-WebResourceInline.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com -WebResourceName {sprk_communicationreconciliation|sprk_spaarkeai} -FilePath {…/dist/*.html}` (needs `az login` — ralph.schroeder@spaarke.com / Spaarke Dev).
- **BFF** (only if a fix touches it): `pwsh scripts/Deploy-BffApi.ps1` (hash-verify + health). Baseline ~44.97 MB compressed, ceiling 60 MB.
- **Client tests**: `npx jest ReconcileTabs ReconciliationWorkspace ReconciliationBrowseShell EmailAssociationsAndTracking` in the shared-lib dir.
- **BFF tests**: `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~Communication"`.

## Likely-touched files (UAT round 4)
- `src/client/shared/Spaarke.Communication.Components/src/components/EmailAssociationsAndTracking/EmailConnectionsReview.tsx` + `.styles.ts` + `EmailConnectionsReviewRows.tsx` (2a/2b/2c padding · 2f switchable candidates)
- `.../ReconciliationBrowseShell/ReconciliationBrowseShell.tsx` (2a clip/padding)
- `.../ReconciliationWorkspace/ReconciliationWorkspace.tsx` (1 view dedupe · 2d emlSource · 2e attachments)
- `.../ReconcileTabs/FieldUpdateReconcileTab.tsx` (2g empty-state "Update other fields")
- `.../ReconcileTabs/TaskReconcileTab.tsx` (2h empty-state "+ New task")
- `.../EmailBody/EmailBodyView.tsx` (2e attachment render)
- `src/solutions/CommunicationReconciliation/src/**` (1 view dedupe host wiring)
- BFF `CommunicationAttachmentTextService.cs` (2e — only if attachments don't return)

## Environment / key IDs
- **BFF (dev)**: `spaarke-bff-dev` / `rg-spaarke-dev` · https://spaarke-bff-dev.azurewebsites.net · net10. Deploy `pwsh scripts/Deploy-BffApi.ps1`.
- **Dataverse**: `spaarkedev1` (`https://spaarkedev1.crm.dynamics.com`). Dev uses UNMANAGED solutions.
- **Reconcile endpoints (live)**: `GET /api/communications/{id}/attachments/text`, `POST /communications/proposals/{id}/apply|dismiss|undo`, `POST /communications/{commId}/tasks/{taskId}/undo`, `POST /communications/proposals/{id}/create-task/apply`, `POST /communications/{commId}/create-task`, `GET /communications/queue-feed?regarding=`.

## Other remaining project items (non-parity)
- **064** — Quick Start "+ New record" `.eml` pre-load (overlaps UAT item 2d). `.eml`-into-wizard is a DATA condition unless the archive doc is linked via `sprk_document.sprk_relatedcommunication`.
- **044** — Deploy Pillar B Outlook add-in (Azure SWA) — 🔲.
- **090** — Project wrap-up (test-diet, doc-drift, size) — 🔲 terminal.

## Merge/deploy status (as of this handoff)
- All Batch 1/2 work MERGED to master via PR #798 (`f60c487f8`). Branch clean, 0 ahead. **master unprotected** (merges land instantly; CI runs post-merge and is often cancelled by concurrency — not a code signal). Dev reflects current master + all reconciliation work.
