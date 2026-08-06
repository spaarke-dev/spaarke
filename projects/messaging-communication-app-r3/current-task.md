# Current Task State — messaging-communication-app-r3

> **Last Updated**: 2026-08-03 (by context-handoff)
> **Recovery**: Read "Quick Recovery" first. This file is self-contained — resume from it alone.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Active work** | Direct UAT loop on the Communications conversation UI (SpaarkeAi **workspace widget** + **PCF** record-form modal + **conversation code page**) + BFF thread reads/fixes. NOT a POML task — operator gives UAT rounds, I implement + deploy + merge. |
| **Status** | ✅ **Everything through round-8.4 + follow-up fixes is merged to master and live on dev.** Branch == `origin/master`. Nothing pending merge. |
| **Branch / HEAD** | `work/messaging-communication-app-r3` @ **`7c9220298`** = `origin/master`. Clean tree, 0 behind, 0 unpushed. |
| **Next Action** | **Await operator retest** of the widget (thread list loads again; record-anchored thread title is a clickable hyperlink to its record) **and import PCF `v1.18.0`**. Then next UAT round or PROD cutover. |
| **Deploy** | BFF + SpaarkeAi + conversation code page: **I deploy directly**. **PCF: I build + pack the zip → operator imports.** Latest packed = **v1.18.0**. |
| **PCF zip** | `src/client/pcf/CommunicationConversationPanel/Solution/bin/CommunicationConversationPanelSolution_v1.18.0.zip` |

### Critical Context (build topology + invariants)
The conversation UI is **shared** (`@spaarke/ui-components`: `ConversationView` / `ConversationWorkspace`) mounted by THREE surfaces — SpaarkeAi **workspace widget** (`Spaarke.Communication.Components` `CommunicationsWorkspaceWidget`), the **PCF** record-form modal (`CommunicationConversationPanel`, now composes the shared `SprkModal` shell + `ModalWindowControls` after the modal-system refactor landed on master), and the **`sprk_communicationconversationpage`** code page. A shared-lib change redeploys to all three.
- **Build order**: `Spaarke.UI.Components` must `npm run build` (tsc) FIRST. **SpaarkeAi aliases the shared libs to SOURCE** (no lib rebuild needed to pick up source; still clear `dist/ node_modules/.vite`). **Code page + PCF consume the lib `dist/`** — the tsc build is mandatory before building them.
- **Soft-delete = deactivate** (`statecode=1`); reads filter `statecode eq 0`. The write MUST set `statecode` as **`OptionSetValue`**, never raw int (SDK rejects raw int → silent 500) — see `ThreadResolver` deactivate.
- **Regarding = typed ADR-024 lookups** (`RegardingFieldMap`), never `sprk_regardingrecordtype`. **GOTCHA**: `sprk_communicationthread` has 11 of the 12 RegardingFieldMap lookups — it LACKS `sprk_regardingreportcard`. `ListThreadsAsync` uses `ThreadRegardingFields` (= `RegardingFieldMap.All` minus reportcard); selecting reportcard on the thread 400s the whole query.
- **Renderer seam** (`ConversationWorkspace.renderConversation` → `IConversationRendererProps`) now carries `threadName`, `regarding`, `onThreadRenamed` per selected thread — hosts forward them to `ConversationView`.
- Branch protection on master is **OFF** → merge is a direct `git push origin HEAD:master` fast-forward (I sync from master first).

### What shipped this session (rounds 8.3 / 8.4 + fixes — all merged + deployed)
- **Soft-delete write fix** (`OptionSetValue`) — deletes actually persist (8 inactive threads confirmed live earlier).
- **Notification** enriched: title "New message · {thread}", body "From {sender **display name**}" (`sprk_sentby.Name`, not raw address); deep-link opens the **regarding** record (resolved from the thread) + `sprk_openconversation=1` auto-opens the PCF modal.
- **Message delete/refresh**: full-reload `refresh()` (delta poll can't drop a soft-deleted row); Refresh button + post-delete use it.
- **Delete icon** moved into the bubble header; **inline thread rename** (pencil in header → popover → `POST /threads/{id}/rename`, FR-17).
- **Widget/modal scroll**: independent pane scroll + pinned composer; scroll-to-bottom on load.
- **Thread name in message-pane header** (was missing on widget/code page — the shell now passes `threadName`). **Title = 18px, hard-truncated to 50 chars** + toolbar `nowrap` (tools stay on one line). **Title is a hyperlink** to the associated record (regarding flows BFF→shell→ConversationView). The separate open-record toolbar icon was added then **removed as redundant** (title link covers it).
- **PCF hover preview** sizing; **double-click** message row opens modal to that thread; **modal chrome** = shared `ModalWindowControls`.
- **BFF thread-list** now projects each thread's regarding (`ThreadRegardingFields`) so the title hyperlink works in all-mode surfaces. §10 seam tests added (`CommunicationListThreadsSeam` 13/13, incl. a regression test that fails if `regardingreportcard` re-enters the thread `$select`).

### Live-on-dev verification (as of this session)
BFF endpoints live (DELETE thread/message + POST rename → 401 not 404). SpaarkeAi widget web resource + conversation code page carry the latest markers. **Shared surfaces get redeployed by OTHER projects too** — always verify markers after another project's deploy (my work survived because it's merged to master first). Deployed BFF is my last deploy (thread-list fix); it does NOT include other projects' newer un-deployed BFF changes (their call).

### Open follow-ups (not blocking)
- **PROD cutover** (when the whole feature ships): BFF · SpaarkeAi · conversation code page · **PCF v1.18.0** · the one-row `sprk_workspacelayout` "Communications"→"Messages" rename (dev record `b117d6e5-b575-f111-ab0e-7ced8ddc4a05`) · confirm target MDA app has **in-app notifications enabled** (Q2 bell).
- **2 pre-existing non-messaging test failures** (NOT ours, deliberately not force-passed): `buildDynamicWorkspaceConfig` rowHeight (`480px`→`100vh`) and `ConversationView.forward` `contract.pdf` attachment. Need real investigation, not an assertion change.
- **Main repo** `c:\code_files\spaarke` local master has an unrelated unpushed commit (`879fef5e8` teams-app-r1 docs) that blocks its ff-sync — left untouched (not mine). Worktree is unaffected.

### PCF version history (operator imports the zip)
1.8.0 → 1.9.0 → 1.10.0 → 1.11.0 → 1.12.0 → 1.13.0 → 1.14.0 (shared ModalWindowControls) → **master bumped to 1.16.0 (SprkModal refactor)** → 1.17.0 → **1.18.0 (current)**. `pack.ps1` reads the version from `solution.xml` (single source; do NOT hardcode).

### Deploy quick-reference
- **BFF**: `pwsh scripts/Deploy-BffApi.ps1` (hash-verify + health; ≤60 MB, currently ~48 MB).
- **SpaarkeAi**: `cd src/solutions/SpaarkeAi && rm -rf dist/ node_modules/.vite && npm run build` → `pwsh scripts/Deploy-SpaarkeAi.ps1`.
- **conv. code page**: build `@spaarke/ui-components` first, then `cd src/solutions/sprk_communicationconversationpage && rm -rf dist/ node_modules/.vite && npm run build`; deploy via the scratch `deploy-convpage-r84.ps1` (Invoke-RestMethod PATCH; web resource name **`sprk_communicationconversationpage.html`** WITH the `.html`).
- **PCF**: bump 4 version files (index.ts CONTROL_VERSION + 3 XMLs), `npm run build:prod`, copy bundle to `Solution/Controls/.../bundle.js`, `pwsh pack.ps1` → operator imports `Solution/bin/...v{X}.zip`.

---

## Merged rounds (history)
- **Rounds 5–7 + Q2** clickable app-notifications → PR #700/#691.
- **Round 8** (11 UAT items) → PR #701. **8.1** (delete read-filter, header, pack.ps1) → PR #703. **8.2** (section-header 16px/semibold + hideTitle on grids) merged.
- **8.3** soft-delete `OptionSetValue` write fix. **8.4** (12 items: refresh, delete-icon, thread-name header, notification link+enrichment, widget/modal scroll, PCF hover, double-click) + follow-ups (rename, shared modal chrome, title 18px/truncate, open-record via title link, thread-list regarding + 400 fix). All on master @ `7c9220298`.
