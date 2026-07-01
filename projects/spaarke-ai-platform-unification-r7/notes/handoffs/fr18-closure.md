# FR-18 Closure — Playbook Library Browse-Mode Affordance ≥3 Consumer Surfaces

> **Closes**: Wave 9 task 096 (LegalWorkspace ad-hoc launcher = surface 3 of 3)
> **Spec criterion (FR-18)**: ≥3 consumer surfaces with a "browse-mode" Playbook Library affordance; modal lists every playbook with consumer mapping per row; clicking invokes through Path A.5 routing.
> **Status**: ✅ MET — 3 of 3 surfaces wired. Wave 9 complete.
> **Date**: 2026-06-29

---

## Surfaces wired (3 of 3)

| # | Surface | Affordance | Task | Commit |
|---|---|---|---|---|
| 1 | **SpaarkeAi chat** | `/playbooks` hard slash (closed Pillar 8 vocabulary, registered in `CommandHelpPanel` for `/help` discoverability) | 094 ✅ | `23b1a7550` |
| 2 | **Daily Briefing widget** | "Browse Playbooks" overflow menu item on `DigestHeader` (always visible, ADR-021 dark-mode compliant) | 095 ✅ | `69a00e0a3` |
| 3 | **LegalWorkspace ad-hoc launcher** | 9th "Browse Playbooks" Get Started action card (`BookRegular` icon, appended after `schedule-new-meeting`) | 096 ✅ | _this commit_ |

All three surfaces open the same `PlaybookLibraryShell` (shared component in `@spaarke/ui-components`) hosted inside the `sprk_playbooklibrary` Code Page via `Xrm.Navigation.navigateTo({pageType:'webresource', ...}, {target:2, ...})` — i.e. the Dataverse modal-dialog overlay.

## Browse-mode invariants (per task 094 implementation)

- `mode='browse'` (PlaybookLibraryShell default) → full card grid, not the locked single-playbook IntentWizardFlow.
- `PlaybookCardGrid` extended in task 094 to render `sprk_playbookconsumer` chips per row via `loadPlaybooks({ includeConsumers: true })`. This same extension is consumed by all three surfaces.
- `IPlaybook.consumers?: IPlaybookConsumerMapping[]` is the public contract (added by task 094 to `@spaarke/ui-components`).

## Path A.5 invocation (per task 091 + audit Q5)

The Library shell creates an `sprk_analysis` record via BFF endpoint `POST /api/ai/analysis/create-and-associate`, which the Analysis Workspace consumer then invokes through `IInvokePlaybookAi` (Path A.5 canonical triangle). The browse-mode affordance does NOT change the execution path — it only changes how the user DISCOVERS + selects a playbook (no intent pre-lock).

This matches the audit Q5 finding: tasks 094–096 wire DISCOVERY affordances; the launch path remains the established Analysis-Workspace flow.

## Pattern parity across the 3 surfaces

| Concern | Surface 1 (chat) | Surface 2 (briefing) | Surface 3 (ad-hoc) |
|---|---|---|---|
| Trigger | Hard slash `/playbooks` | Overflow menu item | Get Started card |
| Discovery | `/help` panel auto-registration | Always-visible header trigger | Visible in Get Started row OR expand dialog |
| Browse mode (no intent) | ✅ `props.onOpenLibraryModal([])` | ✅ `onBrowsePlaybooks()` → host `navigateTo` no data | ✅ `ctx.onOpenWizard("sprk_playbooklibrary")` (no data arg) |
| Consumer chips in grid | ✅ via shared `PlaybookCardGrid` extension | ✅ via shared `PlaybookCardGrid` extension | ✅ via shared `PlaybookCardGrid` extension |
| Test classification | KEEP (component test) | KEEP (component test) | KEEP (doc-style config test — LegalWorkspace has no runner yet) |
| Dark mode (ADR-021) | ✅ semantic tokens | ✅ semantic tokens | ✅ inherits Get Started row styling (no hardcoded colors introduced) |

## Files modified by task 096

- `src/solutions/LegalWorkspace/src/components/GetStarted/getStartedConfig.ts` (+1 card config, +1 icon import)
- `src/solutions/LegalWorkspace/src/sections/getStarted.registration.ts` (+1 onCardClick handler — browse mode = no intent)
- `src/solutions/LegalWorkspace/src/components/Shell/WorkspaceGrid.tsx` (+1 fallback handler + +1 memoized callback for the fallback `cardClickHandlers` map consumed by `GetStartedExpandDialog`)
- `src/solutions/LegalWorkspace/src/components/GetStarted/GetStartedExpandDialog.tsx` (header comment updated 7 → 9)
- `src/solutions/LegalWorkspace/src/components/GetStarted/__tests__/BrowsePlaybooksCard.test.tsx` (NEW — documentation-style test mirroring `FeedTodoSyncContext.test.tsx` runner-pending pattern)

## Acceptance verification

All FR-18 acceptance criteria from spec (cross-referenced with task 093 audit Q3 + Q6):

- [x] ≥3 consumer surfaces wired (chat / briefing / ad-hoc) — **3 of 3 done**.
- [x] Modal lists every playbook with consumer mapping per row — shared `PlaybookCardGrid` extension (task 094) consumed by all three surfaces.
- [x] Clicking a playbook invokes through Path A.5 routing — Analysis-Workspace flow via `create-and-associate` BFF endpoint (unchanged); pre-existing invariant verified by task 091 SessionSummarizeOrchestrator migration + task 092 chat-summarize `sprk_playbookconsumer` row.
- [x] No direct call to `AnalysisOrchestrationService.ExecuteAnalysisAsync` from launch path — that surface was DELETED in Wave 4 task 042 (`c475787ff`).
- [x] ADR-021 dark-mode compliance — task 096 reuses Get Started row styling; no hardcoded colors introduced.
- [x] Pattern parity with tasks 094 + 095 confirmed (table above).

## Wave 9 status

✅ **Wave 9 COMPLETE** (all 7 tasks closed: 090, 091, 092, 093, 094, 095, 096).

This unblocks:
- **Wave 4 schema cascade** (tasks 043, 044, 046, 047) — was waiting on Wave 9 task 091 chat-summarize migration; that gate is closed.
- **Wave 10 wrap-up** (tasks 100, 101, 090-project-wrap-up) — these depend on all waves complete; Waves 5, 6, 7, 8 still in-progress.

## Open items NOT in scope of FR-18

Per audit Risk 2: if the chat/briefing/ad-hoc surfaces eventually want IN-FLOW invocation (e.g., chat surface streams the result back as a message rather than navigating away to Analysis Workspace), that's a separate UX decision and a follow-on task. Flag for stakeholder input only if the spec acceptance requires it; otherwise defer.
