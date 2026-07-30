# Current Task State — messaging-communication-app-r3

> **Last Updated**: 2026-07-29 (by context-handoff)
> **Recovery**: Read "Quick Recovery" first. This file is self-contained — resume from it alone.

---

## Round 8 UAT — in progress (2026-07-30)

**11 UAT items** being implemented in one pass. Code COMPLETE for 10/11; item 1 deferred (needs pointer). Deploy pending.

Decisions (operator): soft-delete = **deactivate** (statecode/statuscode Inactive, reversible); item 11 = **full** (new PCF v1.9.0 auto-opens modal from deep-link param); **all 11 one pass**.

Done + verified (shared-lib tsc 0 errors, BFF build 0 errors, ConversationView 66/67 — 1 pre-existing forward-attachment fail):
- **2** thread card +2px pad · **3** dot↔name space · **4** pin show-on-select · **5** no mouse-focus dark border (keyboard `:focus-visible` only) · **6** row dividers (Email-style) · **7** delete-thread trash+confirm · in `ThreadList.tsx` + shell wiring `ConversationWorkspace.tsx`
- **8** delete-message (non-email) hover + confirm · **9** thread name in tools row · in `ConversationView.tsx` (self-owned deactivate)
- **10** modal expand-to-container toggle · `ConversationModal.tsx` (PCF)
- **11** notif deep-link `&sprk_openconversation=1` (`CommunicationArrivedProducer.BuildRecordDeepLink`) + PCF auto-open (`CommunicationConversationPanelApp.tsx shouldAutoOpenConversation`)
- **BFF delete backend**: `DELETE /api/communications/threads/{id}` + `DELETE /api/communications/{id}` (soft-delete); `IThreadResolver.DeactivateThread/MessageAsync`; `CanCallerSeeMessageAsync` gate. Client: `deactivateThread` (communicationThreadListApi), `deactivateMessage` (communicationTimelineApi).
- **PCF v1.8.0→1.9.0** bumped (manifest, index.ts, pack.ps1, solution.xml).

**Item 1 (widget "Messages ⌄" header elevation/font)** — DEFERRED: could not conclusively locate the shared tab-header component; changing blind risks regressing every workspace tab. Needs operator pointer to the exact component.

**Remaining deploy steps**: rebuild shared lib (done pre-titleLink-fix; REBUILD needed) → BFF publish+deploy → SpaarkeAi build+deploy → conversation code page build+deploy → PCF prod build + pack v1.9.0 zip (hand to operator).

**§10 test obligation**: BFF Services/ modified → add delete-endpoint seam test before PR (deferred to post-UAT commit).

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Active work** | UAT iteration on the Communications conversation UI (workspace widget + PCF record-form modal + conversation code page) + BFF thread/notification fixes. Direct UAT loop, NOT a POML task. |
| **Status** | ✅ **Rounds 5–7 + Q2 all merged to master (PR #700) and deployed to dev.** Awaiting the operator's round-7 UAT feedback. |
| **Branch / HEAD** | `work/messaging-communication-app-r3` @ **`e61d9eaeb`** = origin/master (0 ahead, 0 behind, clean). |
| **Next Action** | **Await operator round-7 UAT feedback.** Everything for rounds 5–7 + Q2 is live on dev. When feedback arrives, fix → deploy the affected surface → (later) fold to master via PR. |
| **Deploy** | BFF + code pages: I deploy directly. **PCF: I build + hand the zip; operator uploads.** |

### Critical Context (5 sentences)
The conversation UI is shared (`@spaarke/ui-components` `ConversationView`/`ConversationWorkspace`) mounted by THREE surfaces: the SpaarkeAi **workspace widget**, the **PCF** record-form modal (`CommunicationConversationPanel`), and the **`sprk_communicationconversationpage`** code page — a shared-lib fix must be redeployed to all three. The BFF thread engine anchors threads/messages via the **typed ADR-024 regarding lookups** (`RegardingFieldMap`), NEVER the non-existent/`lookup` `sprk_regardingrecordtype` field (root cause of the 500 + auto-threading + membership bugs). **Q2** adds a clickable Dataverse **`appnotification`** (the MDA bell) per fan-out recipient on every arrival, deep-linked to the regarding record, via the `IActionSeam` facade inside `CommunicationArrivedProducer` (ADR-047-compliant mirror). The workspace **tab label** lives in a Dataverse `sprk_workspacelayout` record (data), not code — renamed to "Messages" in dev; **prod still needs that one-row rename**. Everything is on master as of PR #700 (`e61d9eaeb`).

---

## What is deployed / merged (all ✅ on dev + master)

| Item | On master | Deployed to dev |
|------|-----------|-----------------|
| Create-thread **500** + auto-threading + membership (typed ADR-024 lookups) | ✅ (#691) | ✅ BFF |
| Round 6 (widget "Messages", email open→record, email card layout) | ✅ (#700) | ✅ SpaarkeAi + PCF v1.8.0 + conv. code page |
| Round 7 **union bug** (thread-switch replaces, not merges) | ✅ (#700) | ✅ SpaarkeAi + PCF v1.8.0 + conv. code page |
| Round 7 **tab label** "Messages" (`sectionMetadataCatalog` + `sprk_workspacelayout` data) | ✅ code (#700) | ✅ dev (code + data) |
| Round 7 **toast flood** (toast live arrivals only) | ✅ (#700) | ✅ SpaarkeAi |
| **Q2** clickable app-notification (bell, deep-linked) | ✅ (#700) | ✅ BFF |

**Live BFF** (`spaarke-bff-dev`) = master `e61d9eaeb` (all thread fixes + Q2). Hash-verified, health 200, smoke 401.

## Verification at merge
- BFF build 0 errors; **50/50** Communication tests (incl. Q2 seam test 6/6); ConversationView 17/17 (thread-switch regression); widget 10/10. No new packages (publish ~47.5 MB, ceiling 60). No new HIGH CVE.
- Pre-existing (NOT ours): ~24 Compose Git-LFS-corpus test failures (PR #690 is the CI-infra fix) + 5 `SentByName` read-path failures — both predate this work, confirmed via stash/ancestry.

## Pending (operator / follow-up)
- **Round-7 UAT feedback** — awaiting (thread scoping on all 3 surfaces, "Messages" labels, toast flood gone, Q2 bell → click → lands on record).
- **PROD cutover** (when ready): BFF deploy · SpaarkeAi deploy · PCF v1.8.0 upload · `sprk_communicationconversationpage` deploy · **the one-row `sprk_workspacelayout` "Communications"→"Messages" rename in prod** (dev record `b117d6e5-b575-f111-ab0e-7ced8ddc4a05`).
- **Q2 config caveat**: the target model-driven app must have **in-app notifications enabled** (App Settings → Features) for the bell to show — likely already on (appnotification used elsewhere).

## Key files (for fast re-entry)
- BFF thread engine: `src/server/api/Sprk.Bff.Api/Services/Communication/ThreadResolver.cs`, `ThreadMembershipDerivationService.cs`
- Q2 producer: `src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationArrivedProducer.cs` (+ `IActionSeam`); design in `notes/q2-appnotification-design.md`
- Union fix: `Spaarke.UI.Components/src/components/CommunicationTimeline/hooks/useThreadPoll.ts` + `ConversationView.tsx` + `CommunicationTimeline.tsx` (SET_THREAD on initial load, MERGE_POLL on delta)
- Toast fix: `Spaarke.Communication.Components/.../CommunicationsWorkspaceWidget.tsx` (`event.source !== 'live'` gate)
- Tab label: `Spaarke.UI.Components/.../WorkspaceShell/sectionMetadataCatalog.ts` (code) + Dataverse `sprk_workspacelayout` (data)

## Deploy quick-reference
- **BFF**: `pwsh scripts/Deploy-BffApi.ps1` (hash-verify + health)
- **SpaarkeAi**: `cd src/solutions/SpaarkeAi && rm -rf dist/ node_modules/.vite && npm run build` → `pwsh scripts/Deploy-SpaarkeAi.ps1` (libs aliased to source — no lib rebuild)
- **conv. code page**: build `@spaarke/ui-components` first (`npm run build` = tsc, symlinked), then `cd src/solutions/sprk_communicationconversationpage && rm -rf dist/ node_modules/.vite && npm run build`; deploy web resource **`sprk_communicationconversationpage.html`** via `Invoke-RestMethod` PATCH content + PublishXml (NOT `Deploy-WebResourceInline.ps1` — it hangs on the 2 MB payload; use `$ProgressPreference='SilentlyContinue'` + Invoke-RestMethod).
- **PCF**: bump 5 version files, `npm run build:prod`, copy bundle to Solution/Controls, pack zip via inline `[System.IO.Compression.ZipFile]` (pack.ps1 CWD bug) → operator uploads.
