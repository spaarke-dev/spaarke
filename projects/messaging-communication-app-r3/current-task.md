# Current Task State — messaging-communication-app-r3

> **Last Updated**: 2026-07-31 (by context-handoff)
> **Recovery**: Read "Quick Recovery" first. This file is self-contained — resume from it alone.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Active work** | Direct UAT loop on the Communications conversation UI (SpaarkeAi workspace widget + PCF record-form modal + conversation code page) + BFF thread/notification fixes. NOT a POML task. |
| **Status** | ✅ **Rounds 5–8 + 8.1 merged to master & live on dev.** Round **8.2** (section-header restyle) committed + pushed + deployed to dev, **NOT yet merged** (awaiting operator UAT of the header look). |
| **Branch / HEAD** | `work/messaging-communication-app-r3` @ **`e71d73226`** = origin/master + round-8.2. Clean tree, 0 behind master, **1 ahead** (the unmerged 8.2 commit). |
| **Next Action** | **Await operator UAT of round-8.2 header** (grid sections show only the DataGrid elevated header; Messages/Daily-Briefing/Calendar keep a 16px-semibold title). Then `/merge-to-master`. |
| **Deploy** | BFF + code pages + SpaarkeAi: I deploy directly. **PCF: I build + pack the zip → operator imports.** Operator has imported **PCF v1.10.0**. |

### Critical Context
The conversation UI is shared (`@spaarke/ui-components` `ConversationView` / `ConversationWorkspace`) mounted by THREE surfaces — SpaarkeAi **workspace widget**, **PCF** record-form modal (`CommunicationConversationPanel`), and the **`sprk_communicationconversationpage`** code page; a shared-lib fix redeploys to all three. **SpaarkeAi aliases the shared lib + LegalWorkspace to SOURCE** (no lib rebuild needed); the **code page + PCF consume the lib `dist/`** (must `npm run build` the lib first). **Soft-delete = deactivate** (`statecode=1`/`statuscode=2`); the round-8.1 fix added `statecode eq 0` to ALL Communication read queries in `CommunicationThreadReadService.cs` (thread list, message read, unread-count, by-regarding) so deactivated rows actually drop out — that was the "delete didn't work" root cause. BFF thread engine anchors via **typed ADR-024 lookups** (`RegardingFieldMap`), never `sprk_regardingrecordtype`. Branch protection on master is **OFF** (CI advisory) → merge via `gh pr create` + `gh pr merge {N} --merge`.

### Round-8.2 (this session — NOT merged)
Operator spec: keep the `SectionPanel` title but **16px / semibold** (was my wrong 20px/800), and **suppress it on dataset-grid sections** (they have the DataGrid's own elevated header). Impl: added `hideTitle` to `SectionConfig` → `SectionPanel` (drops the whole title bar when nothing else needs it); set `hideTitle:true` in the 5 grid registrations (`documents/matters/projects/invoices/workAssignments.registration.ts`); `communications` (conversation widget) intentionally keeps its title. Files: `SectionPanel.tsx`, `WorkspaceShell.tsx`, `types.ts` + 5 registrations. SpaarkeAi-only (code page/PCF don't use SectionPanel). Deployed to dev. Commit `e71d73226`.

### Open follow-ups (not blocking)
- **Merge round-8.2** to master once operator OKs the header.
- **§10 test obligation**: BFF `Services/Communication/` gained 2 soft-delete endpoints (round-8.0) + read-filter change (8.1) — still owe a seam test under `tests/integration/seam/Communication/`.
- **2 pre-existing non-messaging test failures** flagged, deliberately NOT force-passed: `buildDynamicWorkspaceConfig` rowHeight (`480px`→`100vh`) and `ConversationView.forward` `contract.pdf` attachment. Both in non-messaging code; need real investigation, not an assertion change. (The stale `sectionMetadataCatalog` "exactly 7" test WAS fixed → robust relative-order check.)
- **PROD cutover** (when the whole feature ships): BFF · SpaarkeAi · conversation code page · PCF v1.10.0 · the one-row `sprk_workspacelayout` "Communications"→"Messages" rename (dev record `b117d6e5-b575-f111-ab0e-7ced8ddc4a05`) · confirm target MDA app has **in-app notifications enabled** (for Q2 bell).

### Merged rounds (history)
- **Rounds 5–7 + Q2** clickable app-notifications → PR #700/#691.
- **Round 8** (11 UAT items: thread-list polish, delete-thread/message soft-delete, notification→PCF-modal auto-open, modal expand) → PR #701.
- **Round 8.1** (delete read-filter fix, section header, message-icon alignment, pack.ps1 CWD fix, stale-test fix) → PR #703. PCF bumped **1.8.0 → 1.9.0 → 1.10.0**.

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

**Live BFF** (`spaarke-bff-dev`) = **round-8 + round-8.1 redeployed 2026-07-31** (HEAD `9ddd9dc68` BFF tree). Hash-verified (4/4), health 200, DELETE endpoints smoke 401 (were 404 pre-deploy). ⚠️ **Root-cause note**: the BFF was NOT redeployed during rounds 8/8.1 even though the client surfaces were — deployed BFF was stuck at `e61d9eaeb` (pre-round-8), returning 404 on the DELETE routes and lacking the `sprk_openconversation=1` deep-link. That caused the "delete doesn't work / notification doesn't open modal" UAT regressions. Fixed 2026-07-31 by redeploying BFF. See memory `deploy-sequence-preference` (post-deploy verification recipe).

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
