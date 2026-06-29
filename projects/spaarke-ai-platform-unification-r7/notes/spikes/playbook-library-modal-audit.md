# Playbook Library Modal — Current Routing Audit (Task 093)

> **Task**: 093-audit-playbook-library-modal-routing
> **Project**: spaarke-ai-platform-unification-r7
> **FR coverage**: FR-18 (audit portion — preparatory work for tasks 094-096)
> **Author**: task-execute (STANDARD rigor — audit only, no source modification)
> **Last updated**: 2026-06-28

---

## 1. Goal Question Answers

### Q1 — Where does the Playbook Library modal live today?

The Playbook Library is implemented as a **shared component + thin consumers** pattern.
There are THREE concrete file surfaces:

| Surface | Path | Role |
|---|---|---|
| **Shared component (single source of truth)** | `src/client/shared/Spaarke.UI.Components/src/components/PlaybookLibraryShell/PlaybookLibraryShell.tsx` | The actual 2-tab UI: "Select Playbook" (card grid) + "Custom Scope" (manual action/skills/knowledge/tools). Supports `mode='browse'` (default, full UI) or `mode='intent'` (locked to one playbook, 3-step wizard). |
| **Dataverse Code Page** | `src/solutions/PlaybookLibrary/src/main.tsx` | Web resource `sprk_playbooklibrary`. Mounted inside Dataverse via `Xrm.Navigation.navigateTo({pageType:'webresource', webresourceName:'sprk_playbooklibrary', ...}, {target: 2, ...})` — i.e. the actual modal experience consumers see. Wraps PlaybookLibraryShell with auth + Xrm dataService + Xrm closeDialog + Analysis-Workspace handoff. |
| **External-SPA page** | `src/client/external-spa/src/pages/PlaybookLibraryPage.tsx` | React-Router route `/playbooks/:entityType/:entityId` (HashRouter inside Power Pages). Wraps PlaybookLibraryShell with MSAL auth + BFF-backed dataService. NOT a modal — full-page within the external-SPA shell. |

Sibling files inside `PlaybookLibraryShell/`:
- `IntentWizardFlow.tsx` — 3-step (Upload → Analysis → Results) wizard rendered when `mode='intent'`. Locks the playbook + scope.
- `DocumentSelector.tsx` — render only when 2+ document IDs provided; lets user switch active doc.
- `index.ts` — barrel: exports `PlaybookLibraryShell`, `IntentWizardFlow`, `DocumentSelector` + props.

**Summary**: the modal-ness comes from the Code Page wrapper (`sprk_playbooklibrary`) being launched with `target: 2` (Dataverse dialog overlay), NOT from the shell itself. The shell is a context-agnostic shell that can be rendered modal OR full-page.

---

### Q2 — What is the modal's public contract (props, callbacks)?

Source: `PlaybookLibraryShell.tsx` lines 50-91. Props (`IPlaybookLibraryShellProps`):

| Prop | Type | Required | Purpose |
|---|---|---|---|
| `entityType` | `string` | yes | Entity logical name (typically `sprk_document`). |
| `entityId` | `string` | yes | Active entity GUID — the document the analysis runs against. |
| `documentIds` | `string[]` | no | 2+ → renders DocumentSelector bar; user can switch active doc. |
| `allowedPlaybookIds` | `string[]` | no | Allowlist filter — only show playbooks whose IDs are in this list. |
| `mode` | `'browse' \| 'intent'` | no (default `'browse'`) | `'intent'` pre-selects a playbook (locked) + renders IntentWizardFlow. |
| `embedded` | `boolean` | no (default `false`) | Suppress header/footer chrome when hosted inside a parent shell. |
| `intent` | `string` | no | Intent string for `mode='intent'`; matched against `INTENT_PLAYBOOK_MAP` first, then fuzzy name match. |
| `onComplete` | `(result: { analysisId: string }) => void` | no | Called when `createAndAssociate` returns the new `sprk_analysis` GUID. |
| `onClose` | `() => void` | no | User cancelled / closed; host should dismiss the modal. |
| `dataService` | `IDataService` | yes | Data access abstraction — `createXrmDataService()` in MDA, `createBffDataService()` in external-SPA. |
| `authenticatedFetch` | `AuthenticatedFetchFn` | no (yes for run) | Required to call BFF `createAndAssociate` endpoint. |
| `bffBaseUrl` | `string` | no (yes for run) | BFF root URL. |
| `entityDisplayName` | `string` | no | Subtitle in header. |
| `executeButtonLabel` | `string` | no (default `'Run Analysis'`) | Primary button label. |
| `title` | `string` | no (default `'New Analysis'`) | Header title. |

Callbacks the shell INVOKES:
- `onComplete({analysisId})` — user clicked Run, BFF returned 200, analysis created.
- `onClose()` — user cancelled OR finished and clicked Close on the success screen.

Callbacks the shell does NOT invoke (gap for Wave 9 tasks):
- No `onSelectPlaybook` — Wave 9 wiring (which only wants to LAUNCH a playbook, not create an analysis) cannot subscribe to "user picked playbook X" without modifying the shell. **See Risk 3 below.**

---

### Q3 — What consumer surfaces currently route to the modal?

Grep evidence (`webresourceName.*sprk_playbooklibrary`):

| Surface | Current state | File / line | How invoked |
|---|---|---|---|
| **LegalWorkspace Get Started** | ✅ wired (intent mode only, 2 of 8 cards) | `src/solutions/LegalWorkspace/src/sections/getStarted.registration.ts:67-70` + `ActionCardHandlers.ts:87` | `ctx.onOpenWizard("sprk_playbooklibrary", "intent=email-compose")` and `intent=meeting-schedule`. |
| **SpaarkeAi chat (PlaybookOptions menu)** | ✅ wired (with `sessionAttachmentIds` filter) | `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx:1744-1802` (`handleOpenLibraryModal`) | `Xrm.Navigation.navigateTo` with `target:2`. Triggered from the FR-51 "Open Library" link in chat. |
| **DocumentUploadWizard next-step launcher** | ✅ wired | `src/solutions/DocumentUploadWizard/src/services/nextStepLauncher.ts:24` | Constant `ANALYSIS_BUILDER_WEB_RESOURCE = "sprk_playbooklibrary"`. |
| **SummarizeFilesWizard analysis step** | ✅ wired | `src/client/shared/Spaarke.UI.Components/src/components/SummarizeFilesWizard/SummarizeAnalysisStep.tsx:36` | Constant `PLAYBOOK_LIBRARY_WEBRESOURCE = 'sprk_playbooklibrary'`. |
| **EmailComposeWidget / MeetingScheduleWidget (workspace widgets)** | ✅ wired (intent mode) | `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/EmailComposeWidget.tsx:72`, `MeetingScheduleWidget.tsx:69` | Constant + `navigateTo`. |
| **WorkspaceShell wizard launcher helper** | ✅ wired (helper exposed) | `src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/wizardLaunchers.ts:264-279` (`launchPlaybookIntent`) | Reusable `fireNavigateTo` helper — `intent` mode only. |
| **Ribbon command (matter form)** | ✅ wired (2 call sites) | `src/client/webresources/js/sprk_wizard_commands.js:276,283` (`openWizardWithBff`) | Ribbon-triggered Xrm.Navigation. |
| **Analysis Workspace ribbon** | ✅ wired | `src/client/webresources/js/sprk_analysis_commands.js:408` | Ribbon-triggered Xrm.Navigation. |
| **BFF agent handoff URL builder** | ✅ wired (server-side URL building) | `src/server/api/Sprk.Bff.Api/Api/Agent/HandoffUrlBuilder.cs:89` | Server returns URL pointing at `sprk_playbooklibrary` for agent handoff scenarios. |
| **External-SPA route** | ✅ wired | `src/client/external-spa/src/App.tsx:91` (route `/playbooks/:entityType/:entityId`) | React-Router HashRouter. |
| **Daily Briefing widget** | ❌ **NOT wired** | `src/client/shared/Spaarke.DailyBriefing.Components/src/components/DailyBriefingApp.tsx` | No PlaybookLibrary launcher anywhere in `Spaarke.DailyBriefing.Components/`. Confirmed via Grep. **This is the Wave 9 task 095 target gap.** |
| **Ad-hoc launcher (TBD)** | ❌ **Not yet identified** | — | LegalWorkspace's Get Started cards are the closest analog. Wave 9 task 096 needs an "ad-hoc" surface — see Q6 below for proposal. |

**Key insight**: The Playbook Library modal is ALREADY heavily wired into the platform, BUT predominantly in **intent-mode** (jump to one specific playbook). **Browse-mode** ("show me all playbooks, let me pick") routing is the gap FR-18 targets. The 3 acceptance-criterion surfaces (chat / briefing / ad-hoc) need a "Browse Playbooks" affordance that opens the Library in `mode='browse'` so the user can DISCOVER any playbook + its consumer mapping.

---

### Q4 — How does the modal list playbooks (data source)?

Trace (lines 263-277 of `PlaybookLibraryShell.tsx` + `playbookService.ts:50-65`):

```
PlaybookLibraryShell useEffect on mount
  → loadAllData(webApiAdapter)                                  // playbookService.ts
    → loadPlaybooks(webApi)
      → webApi.retrieveMultipleRecords(
          'sprk_analysisplaybook',
          '?$select=sprk_analysisplaybookid,sprk_name,sprk_description&$filter=statecode eq 0&$orderby=sprk_name'
        )
    → loadActions / loadSkills / loadKnowledge / loadTools  (same pattern)
```

The `webApi` is a thin shim `webApiAdapter` built from the `IDataService` prop (lines 244-256), which routes through:
- **MDA Code Page (`sprk_playbooklibrary`)**: `createXrmDataService()` → `Xrm.WebApi.retrieveMultipleRecords` (direct Dataverse).
- **External-SPA**: `createBffDataService(authenticatedFetch, BFF_API_URL)` → BFF proxy at `/api/dataverse/`.

**Compliance check against `DATA-ACCESS-DECISION-CRITERIA.md`**:
- MDA path uses `Xrm.WebApi` — appropriate for host-context Dataverse access (criterion 1: in-host Dataverse list query). ✅
- External-SPA path uses BFF — appropriate for non-host context (criterion 2: external surface without `Xrm`). ✅

**Consumer-mapping column**: Currently the shell loads ONLY `sprk_analysisplaybookid`, `sprk_name`, `sprk_description` from `sprk_analysisplaybook`. **It does NOT load or display `sprk_playbookconsumer` mappings**. FR-18 acceptance requires the Library to "list every playbook + its consumer mapping". **This is a documented gap — see Risk 1.**

---

### Q5 — How does the modal launch a playbook (Path A.5 confirmation)?

Trace (lines 365-421 of `PlaybookLibraryShell.tsx` + `analysisService.ts:49-95`):

```
User clicks "Run Analysis" in PlaybookLibraryShell
  → handleExecute()
    → builds IAnalysisConfig {documentId, playbookId, actionId, skillIds, ...}
    → createAndAssociate(authenticatedFetch, bffBaseUrl, config)
      → POST {bffBaseUrl}/api/ai/analysis/create-and-associate
        body: { documentId, playbookId, actionId, skillIds, knowledgeIds, toolIds, documentName? }
      → BFF returns { analysisId: string }
    → onComplete({ analysisId })
      → host (e.g. PlaybookLibrary Code Page) navigates to Analysis Workspace web resource
        (sprk_AnalysisWorkspace with ?analysisId={id}&documentId={id})
```

**Path A.5 status**: The shell **creates an `sprk_analysis` record** but does NOT directly invoke `IInvokePlaybookAi`. It defers actual playbook execution to a downstream consumer of `sprk_analysis` (the Analysis Workspace, which the Code Page wrapper opens via Xrm navigateTo on `onComplete`).

This is **architecturally intentional**: the Library modal is a "configure + create" surface. Whether the eventual execution flows through Path A.5 (`IConsumerRoutingService → IInvokePlaybookAi`) depends on what the Analysis Workspace does with the created `sprk_analysis` record. **This is OUT OF SCOPE for tasks 094-096** because those tasks only need to surface the Library affordance — they don't change the execution path.

**Risk for FR-18**: The current "Run Analysis" path creates an analysis. The FR-18 use case (chat / briefing / ad-hoc) may want a **direct invocation** ("invoke this playbook now against my current context, return result in this surface"), not "create an analysis record and navigate away to Analysis Workspace". **See Risk 2 below.**

---

### Q6 — Wave 9 mount points per consumer surface

#### Task 094 — Wire into SpaarkeAi chat surface

**Already partially wired**: `ConversationPane.handleOpenLibraryModal` (lines 1744-1802) exists and supports `sessionAttachmentIds` filtering. The current invocation paths into it are limited (FR-51 "Open Library" link in chat for attachments).

**FR-18 gap to close**: add a USER-DISCOVERABLE "Browse Playbooks" affordance that triggers `handleOpenLibraryModal([])` with no filter (so it opens in browse mode showing every playbook).

**Recommended mount points** (1-2 candidates):

1. **PRIMARY: `CommandHelpPanel.tsx` (slash command reference Dialog)** at `src/solutions/SpaarkeAi/src/components/conversation/CommandHelpPanel.tsx`. Add a new HARD-slash `/playbooks` (or `/library`) to `CommandRouter.HardSlashes` + a description in `HARD_SLASH_DESCRIPTIONS`. The existing `HardSlashExecutor` pattern dispatches to `handleOpenLibraryModal([])`. This integrates with the closed-vocabulary Pillar 8 model (FR-49) naturally — `/help` already lists hard slashes, so discoverability is automatic.

2. **SECONDARY: PaneHeader rightSlot Tooltip Button** — add a "Browse Playbooks" icon button (BookRegular or LibraryRegular) to `ConversationPane`'s PaneHeader rightSlot next to the existing HistoryMenu. Wires directly to `handleOpenLibraryModal([])`. Trade-off: adds chrome to a deliberately minimal header.

**Recommendation**: go with PRIMARY (`/playbooks` hard slash). It reuses the existing Pillar 8 vocabulary infrastructure, no new UI surface, automatic `/help` discoverability, and the existing `HardSlashExecutor` pattern (proven for `/clear`, `/new-session`, `/help`, etc.).

#### Task 095 — Wire into Daily Briefing widget

**Current state**: NO playbook library wiring exists in `Spaarke.DailyBriefing.Components/`.

**Recommended mount points** (1-2 candidates):

1. **PRIMARY: New overflow-menu item on `DigestHeader`** at `src/client/shared/Spaarke.DailyBriefing.Components/src/components/DigestHeader.tsx`. The header already hosts `preferencesSlot` (PreferencesDropdown) and refresh button. Add a Menu / overflow button "Browse Playbooks" that calls a new `onBrowsePlaybooks` callback prop, which the host (e.g. the DailyBriefing solution code page) wires to `Xrm.Navigation.navigateTo({pageType:'webresource', webresourceName:'sprk_playbooklibrary', data:''}, {target:2, ...})`.

2. **SECONDARY: `EmptyState.tsx`** at `src/client/shared/Spaarke.DailyBriefing.Components/src/components/EmptyState.tsx` — when the user is "all caught up", surface a CTA "Want to explore a playbook? → Browse Playbooks" with a button. Trade-off: only visible in caught-up state; less discoverable.

**Recommendation**: PRIMARY (DigestHeader overflow item) — always visible, follows the existing header-slot extension pattern.

**Important**: per shared-lib component model, the `onBrowsePlaybooks` callback must be plumbed THROUGH the shared component as a prop. The actual `Xrm.Navigation.navigateTo` call lives in the host code page (`src/solutions/DailyBriefing/...` or wherever the DailyBriefingApp is mounted in production). This preserves ADR-012 (no Xrm dependency in shared lib).

#### Task 096 — Wire into ad-hoc launcher

**Current state**: no explicit "ad-hoc launcher" surface exists. LegalWorkspace's Get Started cards (`getStarted.registration.ts`) are the closest analog, but they're intent-driven (specific cards launch specific playbooks).

**Recommended mount point**:

1. **PRIMARY: New Get Started action card** at `src/solutions/LegalWorkspace/src/sections/getStarted.registration.ts:67-70`. Add a 9th card "Browse Playbooks" (icon: `BookRegular` or `LibraryRegular`) that calls `ctx.onOpenWizard("sprk_playbooklibrary")` with NO intent param → opens in browse mode. Trade-off: the LegalWorkspace Get Started section is a "quick-action" surface; adding a discovery affordance fits if framed as "explore what's possible".

2. **SECONDARY: Workspace ribbon command** — add a ribbon button on the matter form (`sprk_wizard_commands.js:276`) similar to existing playbook-launch commands but with no `intent`. Trade-off: ribbon real-estate is contended; less discoverable than a Get Started card.

**Recommendation**: PRIMARY (Get Started card). Reuses existing infrastructure, matches the precedent set by `email-compose` / `meeting-schedule` cards, and is highly discoverable.

---

### Q7 — UX gap: list playbooks + consumer mapping

FR-18 acceptance criterion: *modal lists every playbook WITH consumer mapping*.

**Current state**: `PlaybookCardGrid` (used by `PlaybookLibraryShell` in browse mode) shows playbook NAME + DESCRIPTION only. No `sprk_playbookconsumer` column.

**Gap to close** (downstream of this audit):
- Either:
  - **(a)** Extend `playbookService.loadPlaybooks` to JOIN `sprk_playbookconsumer` rows (1:N from playbook) and surface the resolved consumer codes per playbook. Display them as a `Tag` row on each `PlaybookCard`. Risk: an N+1 query if done naïvely — fold into a single `expand=sprk_playbookconsumer_playbook($select=sprk_consumertype,sprk_consumercode)` OData query.
  - **(b)** Add a BFF endpoint `GET /api/ai/playbooks/library-listing` that returns `{ playbookId, name, description, consumers: [{ type, code }] }[]` already pre-joined. Use this from both MDA + external-SPA dataServices.

**Recommendation for Wave 9 tasks**: Path (a) — fold the join into the existing `playbookService.loadPlaybooks` query via OData `$expand`. No new BFF endpoint needed; performance is fine for the ~94 playbooks in spaarkedev1 (single round-trip).

**Implementation surface**: `src/client/shared/Spaarke.UI.Components/src/components/Playbook/playbookService.ts` + `types.ts` (add `consumers?: { type: string; code?: string }[]` to `IPlaybook`) + `PlaybookCardGrid.tsx` (render `Tag` chips for consumers).

---

## 2. Wave 9 Task-Wiring Plan (Concrete Per-Task Action)

Each Wave 9 task gets the following per-surface action plan. These are RECOMMENDATIONS to be confirmed when the wire-up tasks run; no code changes here.

### Task 094 — spaarke-ai chat

1. Add `/playbooks` to `HardSlashes` array in `CommandRouter.ts` (alongside `/clear`, `/new-session`, etc.).
2. Add description in `HARD_SLASH_DESCRIPTIONS` map in `CommandHelpPanel.tsx`.
3. Add `execPlaybooks` method to `HardSlashExecutor` that calls `props.onOpenLibraryModal([])` (no attachments filter → browse mode).
4. No new modal infrastructure — reuse existing `handleOpenLibraryModal`.
5. Acceptance: typing `/playbooks` (or `/library`) in chat → opens Library modal in browse mode.

**Estimated effort**: 0.5-1 hour. Trivial extension of existing infrastructure.

### Task 095 — Daily Briefing widget

1. Add new `BookRegular` / `LibraryRegular` icon button to `DigestHeader.tsx` rightSlot (between refresh + PreferencesDropdown).
2. Add `onBrowsePlaybooks?: () => void` prop to `DigestHeaderProps` and `DailyBriefingAppProps`.
3. In the consuming host (e.g. `src/solutions/DailyBriefing/src/main.tsx` or wherever `<DailyBriefingApp/>` is mounted in production), wire `onBrowsePlaybooks` to `Xrm.Navigation.navigateTo({pageType:'webresource', webresourceName:'sprk_playbooklibrary', data:''}, {target:2, width:{value:85,unit:'%'}, height:{value:85,unit:'%'}, title:'Playbook Library'})`.
4. Acceptance: clicking the Browse Playbooks button on the Daily Briefing widget header → opens Library modal in browse mode.

**Estimated effort**: 1-2 hours (shared lib prop addition + host wire-up).

### Task 096 — Ad-hoc launcher (LegalWorkspace Get Started)

1. Add 9th card config to `ACTION_CARD_CONFIGS` (`src/solutions/LegalWorkspace/src/components/GetStarted/getStartedConfig.ts`): `id: 'browse-playbooks'`, `label: 'Browse Playbooks'`, `icon: BookRegular`.
2. Add handler in `getStarted.registration.ts:67`: `"browse-playbooks": () => ctx.onOpenWizard("sprk_playbooklibrary")` (no intent arg → browse mode).
3. Optionally: also add to the Get Started expand-dialog (8 → 9 cards visible there).
4. Acceptance: clicking "Browse Playbooks" Get Started card → opens Library modal in browse mode.

**Estimated effort**: 1-2 hours.

### Cross-cutting (Wave 9 follow-on if FR-18 consumer-mapping criterion is binding)

If the Wave 9 wire-up tasks must ALSO close the consumer-mapping display gap (Q7), add ONE additional task (or fold into 094-096 acceptance scope):
- Extend `playbookService.loadPlaybooks` with `$expand=sprk_playbookconsumer_playbook($select=sprk_consumertype,sprk_consumercode)`.
- Update `IPlaybook` type with optional `consumers` field.
- Update `PlaybookCardGrid.tsx` to render consumer-mapping `Tag` chips below the description.

**Estimated effort**: 2-3 hours (single edit, shared-lib change touches all consumers automatically).

---

## 3. Risk Flags for Wave 9

### Risk 1 — Consumer-mapping column gap (MEDIUM)

`PlaybookLibraryShell` does NOT currently display `sprk_playbookconsumer` mappings. FR-18 acceptance literally requires this. Tasks 094-096 cannot pass the FR-18 acceptance gate without also extending `playbookService.loadPlaybooks` + `PlaybookCardGrid`. **Decision needed during Wave 9**: fold this into the wire-up tasks OR defer to a follow-on task with a tighter scope. Recommendation: fold (single shared-lib edit, scope is small).

### Risk 2 — Launch flow (modal opens, but does it run?) (LOW)

`PlaybookLibraryShell` creates an `sprk_analysis` record on Run. The chat / briefing / ad-hoc consumers may want to either:
- Just CONFIGURE + launch normally (the current Code Page flow → Analysis Workspace), OR
- INVOKE the playbook in-flow and return the result in their surface (e.g. chat surface shows the result as a streamed message).

This audit assumes Wave 9 just needs the AFFORDANCE to open the Library in browse mode — the launch flow stays unchanged (creates analysis → opens Analysis Workspace). If the chat/briefing/ad-hoc UX requires a DIFFERENT launch path (e.g. invoke-and-stream-back), that's a separate UX decision beyond the current FR-18 scope and should be flagged for owner input.

### Risk 3 — Modal contract does NOT expose `onSelectPlaybook` (LOW)

The shell exposes `onComplete({analysisId})` after Run, but no `onSelectPlaybook(playbookId)` callback. If a consumer needs "just tell me what the user picked, I'll handle the invoke" (Risk 2 alt path), the shell needs prop extension. Not required for the recommended wiring plan above, but flagged for completeness.

### Risk 4 — DailyBriefing surface ownership (LOW)

`Spaarke.DailyBriefing.Components` is a shared lib (per CLAUDE.md "Calendar shared components" + `SPAARKEAI-COMPONENT-MODEL.md`). The actual Daily Briefing widget is mounted in MULTIPLE solutions (SpaarkeAi as a workspace widget, possibly as a DailyBriefing standalone Code Page). The shared lib must NOT call `Xrm.Navigation.navigateTo` directly (per ADR-012 host-independence rule). The `onBrowsePlaybooks` callback must be plumbed as a PROP from each host. **Wave 9 task 095 must enumerate all hosts** and wire each.

### Risk 5 — Authentication / theme (NONE)

Both authentication and theme are already correctly handled:
- Auth: `@spaarke/auth` `authenticatedFetch` is already used by Library Code Page (`PlaybookLibrary/src/main.tsx:53`).
- Theme: `resolveCodePageTheme` + `setupCodePageThemeListener` is already wired in both PlaybookLibrary Code Page and PlaybookLibraryPage (external-spa).

No changes needed at the consumer-surface wiring stage.

### Risk 6 — `entityType` / `entityId` for browse-mode (LOW)

The shell requires `entityType` + `entityId` props, but in browse-mode without a specific context (chat with no entity selected; briefing widget on welcome screen) these may be empty. The Code Page wrapper (`sprk_playbooklibrary/main.tsx:155-163`) already handles this — `resolvedEntityType` falls back to `''` / `sprk_document`, `resolvedEntityId` falls back to `''`. **The shell's `canExecute` check** (line 354) requires `effectiveDocumentId` to be truthy. So opening in pure-browse with no entity will let the user PICK a playbook but Run will be disabled until they have a doc context. **Likely acceptable for Wave 9 use cases** but should be confirmed with stakeholder during task 094 (chat surface most likely to hit this state).

---

## 4. Sources

| Reference | Used For |
|---|---|
| `src/client/shared/Spaarke.UI.Components/src/components/PlaybookLibraryShell/PlaybookLibraryShell.tsx` | Q1, Q2, Q4, Q5 |
| `src/client/shared/Spaarke.UI.Components/src/components/PlaybookLibraryShell/IntentWizardFlow.tsx` | Q1 |
| `src/client/shared/Spaarke.UI.Components/src/components/PlaybookLibraryShell/index.ts` | Q1 |
| `src/solutions/PlaybookLibrary/src/main.tsx` | Q1, Q3 |
| `src/client/external-spa/src/pages/PlaybookLibraryPage.tsx` + `App.tsx` | Q1, Q3 |
| `src/client/shared/Spaarke.UI.Components/src/components/Playbook/playbookService.ts` | Q4 |
| `src/client/shared/Spaarke.UI.Components/src/components/Playbook/analysisService.ts` | Q5 |
| `src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/wizardLaunchers.ts` | Q3 |
| `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` (lines 1735-1802) | Q3, Q6 (task 094) |
| `src/solutions/SpaarkeAi/src/components/conversation/CommandHelpPanel.tsx` | Q6 (task 094) |
| `src/solutions/SpaarkeAi/src/components/conversation/CommandRouter.ts` (vocabulary) | Q6 (task 094) |
| `src/client/shared/Spaarke.DailyBriefing.Components/src/components/DailyBriefingApp.tsx` | Q3, Q6 (task 095) |
| `src/solutions/LegalWorkspace/src/sections/getStarted.registration.ts` | Q3, Q6 (task 096) |
| `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/IConsumerRoutingService.cs` | Q5 (Path A.5 confirmation) |
| `docs/standards/DATA-ACCESS-DECISION-CRITERIA.md` | Q4 compliance check |

---

## 5. Summary for Wave 9 Owner

| Question | Answer |
|---|---|
| Does the modal exist? | YES — `PlaybookLibraryShell` (shared) + `sprk_playbooklibrary` Code Page (modal wrapper) + external-SPA page (full-page wrapper). |
| Is it routed from ANY consumer today? | YES, heavily — 10+ call sites. Intent-mode launches are universal (LegalWorkspace, widgets, ribbon, DocumentUpload, SummarizeFiles, chat FR-51 link). |
| Is "browse-mode" routed? | NO from any of the 3 FR-18 target surfaces (chat, briefing, ad-hoc). Library has the capability (`mode='browse'` is the default), but no surface currently launches it without an intent. |
| What does Wave 9 actually need to add? | The "Browse Playbooks" affordance on each of the 3 surfaces. ~1-2 hours per surface (094: hard-slash command; 095: header overflow item; 096: Get Started card). |
| What additional gap should Wave 9 close? | `sprk_playbookconsumer` mapping column in the Library card grid (Q7). Single shared-lib edit (~2-3 hrs). |
| Major risks? | NONE blocking. Risk 1 (consumer-mapping column) needs explicit Wave 9 decision; Risk 2 (launch flow alternatives) likely defer; Risks 3-6 informational. |

**Total Wave 9 effort estimate** (094 + 095 + 096 + consumer-mapping fold-in): **6-10 hours**, well within the 2-day plan budget.
