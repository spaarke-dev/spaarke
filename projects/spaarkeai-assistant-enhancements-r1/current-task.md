# Current Task State — spaarkeai-assistant-enhancements-r1

> **Last Updated**: 2026-07-23 (by context-handoff)
> **Recovery**: Read "Quick Recovery" first. UAT remediation through **R7** + close-out shipped. **2026-07-23 close-out batch: 050 ✅ / 051 ✅ / 054 ✅** — R1 eval family authored + joined to the `Category=GoldenUtteranceEval` merge gate (92/92 green), deploy formalized (no re-deploy per owner), pre-090 doc review done. **ONLY 090 (wrap-up + /test-diet) remains.**

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Git** | branch + `origin/branch` + **origin/master** + main-repo local master ALL at **`71800366d`**. Working tree clean. |
| **Deployed (dev) 2026-07-22** | **BFF `spaarke-bff-dev` DEPLOYED from this wt** (hash-verified + healthz 200): notification idempotency fix (from master) + NEW `POST /api/notifications/{id}/dismiss` (ownership-checked) + tightened missing-context chip keywords. **`sprk_spaarkeai` code page** redeployed (banner + dismiss card UX + `[action:]` label-strip + all merged client work). Grid config `ac05e4f1` (My Tasks membership) live. |
| **Proactive suggestions (notification spine / R1.5) — LIVE ON DEV** | `Notifications__Suggestions__Enabled=true` set on dev BFF (engine ON; idempotency fix live so no dupes). SignalR NOT provisioned → client on **poll fallback**. Producer = `DailyBriefingSuggestionProducer` (fires on Daily-Briefing render, grounded+gated). Client: `useSuggestionCards`+`SuggestionCard` render at top of Assistant → **collapsed "You have N new notifications" banner** (no hover) → cards drop down → click opens record modal, **dismiss 'x'** (server-persisted via new endpoint) + dismiss-on-action. 20 stale pre-fix outbox dupes **bulk-dismissed** (clean slate). SuggestionCard tests 10/10. |
| **Create-task chip fixes (this session)** | Two bugs fixed + deployed: (1) **label leak** — `SprkChatSuggestions.tsx` now strips the `[action:<id>] ` routing prefix for DISPLAY (raw still routes); (2) **mis-fire** — tightened `EmitMissingContextChipsIfNeededAsync` keywords (`ChatEndpoints.cs`) so bare "please provide/share/send" no longer fires document chips on a task reply. |
| **"Add a task" routing (DIAGNOSED, no change)** | NOT broken: live catalog has `create-matter`/`create-task`/`create-todo` ALL `disposition=Surface Launch`, enabled, `captureMode=Loop Elicitation`. The chat Q&A is Loop Elicitation collecting missing required args before launching the pre-seeded surface; the confusing doc chips were the (now-fixed) mis-fire. **OPEN owner decision:** keep chat-elicit-first (LoopElicitation) vs launch-form-immediately (`captureMode=Modal`, 100000001) — a live catalog-data flip on the 3 bindings; VERIFY Modal semantics + required-args first. Re-test "add a task" with the chip fix live before deciding. |
| **Membership-filter feature (shipped earlier 2026-07-22)** | ✅ Reusable `behavior.membershipFilter` DataGrid feature + applied to 050 (`ac05e4f1` → savedquery `12a510e4` + `membershipFilter:true`). Graduated into `SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md` §6.6. Awaiting owner UAT of "what are my tasks?" (membership-scoped). |
| **Registry surface routing (shipped)** | `surfaceLaunchRegistry` drives `handleSurfaceLaunch` by `kind` (workspace-tab/wizard/oob-form); hardcoded `list-tasks` branch retired. Doc: `ASSISTANT-SURFACE-LAUNCH-MECHANISM.md` (§1.1 reactive-vs-proactive). |
| **Docs added this session** | `docs/architecture/ASSISTANT-SURFACE-LAUNCH-MECHANISM.md` (+CLAUDE.md §17 pointer +CHANGELOG) · `docs/standards/ASSISTANT-UI-ELEMENT-CRITERIA.md` (bubble/chip/card/tab criteria). r2 `surfaceTarget` project idea INVESTIGATED + ABANDONED (over-abstraction / data-defined routing) — folder deleted. |
| **Close-out status (2026-07-23)** | **050 ✅** (list-tasks + VIEW-vs-CREATE cue; FR-J1 via UAT) · **051 ✅** (R1 eval family `tests/integration/contract/Eval/assistant-r1-eval-cases.json` + `AssistantEnhancementsR1EvalTests.cs` → gate 92/92; Step 9.5 clean; commit `1ac8d2e42`) · **052 ✅** · **053 ✅** · **054 ✅** (deploy formalized, no re-deploy per owner — `notes/deploy-report.md`). Docs refreshed: eval README R1-family section, owed-eval-cases "CONSUMED by 051" note, root CLAUDE.md §17 UI-element-criteria pointer + CHANGELOG. |
| **Next Action** | Run **090 wrap-up** (`task-execute` → `/test-diet` reconciles the R1 eval family against ADR-038 build-vs-maintain; README→Complete; worktree-sync). Then owner UAT of the shipped batch remains open (suggestions/dismiss/"add a task"/membership) but does NOT gate 090. | |
| **Open follow-ups** | (a) "add a task" captureMode Modal-vs-Loop decision (owner). (b) `compose-draft-document` may have latent no-file issue (documentText fix pattern). (c) leave suggestions engine ON per owner (idempotency live). (d) SignalR not provisioned on dev (poll fallback only) — provision for live push if desired. |

### Only open items (both need an owner repro; NO code written yet)
- **R4-7** — new session showed header *"Actions available for '…'"* (text from the file in Compose) but **no actions listed** beneath — confusing empty state.
- **R4-9** — Context / **Execution-Trace pane** loads inconsistently; it logs the session's grounded tool calls from the ledger (so it reads empty when a turn made no tool call — see R5-6 answer). Owner to re-capture repro.

### Deploy recipes (fast reference)
- Client code page: `cd src/solutions/SpaarkeAi && rm -rf dist node_modules/.vite && npm run build` → PATCH `sprk_spaarkeai` web resource content + `PublishXml` (targeted, mirrors `Deploy-SpaarkeAi.ps1`). Catalog chips = live Web-API PATCH on `sprk_playbookconsumers.sprk_chiptransitions` (no deploy).
- Wizard code pages need `npm install --legacy-peer-deps` first (no node_modules); deploy = PATCH the `sprk_*wizard` web resource content + publish.
- **PCF** (e.g. CommunicationActions): bump 5 version locations, `npm install` + `npm run build:prod` (prebuild:prod auto-refreshes shared-lib `dist/`), copy `out/controls/<name>/{bundle.js,ControlManifest.xml,styles.css}` → `Solution/Controls/…`, `pack.ps1`, then `pac solution import --path bin/…zip --publish-changes` (disable `Directory.Packages.props` during import). pac is authed to SPAARKE DEV 1.

### R6 shipped 2026-07-21 (incl. standard email modal via PCF)
R6-1 Revise-document chip · R6-2 one chip row (revise-context vs consumer cards) · R6-3 redline whitespace-tolerant fallback (master's "Fix #4"; my dup dropped at merge) · R6-4 email dialog 760px · R6-5 recipient lookup wired on BOTH modals + shared `ILookupItem.email` fix (CommunicationActions PCF v1.1.4) · R6-6 rewrite→redline routing. Commits d3428b930 (batch) / f69a2ad46 (PCF+docs). Backlog: [`notes/uat-feedback-2026-07-21-R6.md`](notes/uat-feedback-2026-07-21-R6.md).

### R6 shipped 2026-07-21 (client + shared-lib email/compose)
R6-1 Revise-document chip · R6-2 one chip row · R6-3 (already fixed on master via "Fix #4") · R6-4 email dialog 760px · R6-5 recipient lookup + shared ILookupItem.email fix · R6-6 rewrite→redline routing. Merged `d3428b930`. Backlog: [`notes/uat-feedback-2026-07-21-R6.md`](notes/uat-feedback-2026-07-21-R6.md).

### R5 round-2 shipped 2026-07-20 (shared-lib + wizard code-pages)
R5-7 (Create Event) · R5-8 (Assign Work / Summarize / Find Similar) file legs · R5-4 (composer pills row). New shared `useHandoffFileLeg` hook (Matter/Event/Assign-Work/Summarize); Find Similar code-page-local single-doc variant. Registry → 7 entries. Commits e20d1cc7a / e498fc724 / 0a7d838bd / 7b9d249cf.

### R5 round-1 shipped 2026-07-20 (client-only, `sprk_spaarkeai`)
R5-1 Revise-as-card · R5-2 spacing · R5-3 remove ? icon · R5-5 history restore+styling · R5-9 Email Compose modal. New seams: `localActionChips.reviseInCompose`, `useConsumerChips.getAppendedLocalChips`/`onCorrespondenceDraft`, `QuickStartModal.onSendEmail`, `ConversationPane` mounts `SendEmailDialog`. 127 tests pass. Full backlog: [`notes/uat-feedback-2026-07-20-R5.md`](notes/uat-feedback-2026-07-20-R5.md).

### Shipped 2026-07-20 (R4 round-2 close-out — client-only + live catalog, NO BFF deploy)
- **R4-6** post-Draft cards: Create-a-matter (live `draft-correspondence` chiptransitions) + Send-as-email / Save-to-document (client `localActionChips.ts` `local:*` chips reusing draft-email / add-to-dms bridges).
- **R4-11** post-summarize cards: Create-a-matter + Draft-a-response (live `chat-summarize` chiptransitions) + Ask-about-these-files (client prompt-nudge chip). Replaces "Summarize again".
- **R4-12** Quick Start file context: `QuickStartModal.getFileContext` → `launchSurface` for create-matter + create-project (zero shared-lib change).
- **R1.5 → notification-spine-r1**: owner decision recorded in both design docs + memory ([[r15-folds-into-notification-spine]]).
- Mechanism: `local:*` sentinel chips intercepted in `useConsumerChips.handleConsumerChipClick` → `onLocalChipAction` (never a fake dispatch). Catalog cache caveat: `ConsumerRoutingService` may cache bindings — BFF restart clears if UAT shows stale chips.
- Tests: 23 pass. Deployed + merged (8973a1c1d).

### Critical Context
- Everything is committed, pushed, and merged. Two full UAT rounds shipped this session (R3 + R4). The **BFF history endpoint** (R4-8) and **Draft→Compose** are the headline new behaviors to verify.
- **Standing owner rule**: for BFF.API changes — (1) rebase this worktree from master first, (2) merge back to master so other projects' deploys don't conflict, (3) deploy the BFF. Followed for R4-8.
- Client deploys: `pwsh scripts/Deploy-SpaarkeAi.ps1 -SkipBuild` (after `npm run build` in `src/solutions/SpaarkeAi`, clearing vite cache). BFF deploy: `pwsh scripts/Deploy-BffApi.ps1`. Catalog chips = live Web-API PATCH (no deploy).
- Two pre-existing **broken test suites** in this worktree's `npx jest` run: `useConsumerChips.surface-launch` + `ConsumerChips` (a `.tsx` `jest.fn<…>()` babel-parse failure — fail identically with changes stashed). Not mine; worth a separate fix pass. New logic covered by `DocumentUploadedEventStream.test.ts`.

---

## Shipped this session (all on master)

### R3 batch (earlier)
- **MA cluster (MA-1..4)** — My Assistant: no auto-open + dismissible nudge/⋮ badge; small 3-step modal; Primary Work Location dropdown (`sprk_workoffice`); curated focus/preference chips. Files: `components/assistant/{MyAssistantDialog,useMyAssistant,userProfileService}.ts(x)`.
- **Chat-UX (CHAT-4/5/6)** — welcome cards on any empty transcript; taller composer + "Let's get started…"; slash menu removed. Additive SprkChat props (shared lib): `hideEmptyState`, `inputPlaceholder`, `inputMinRows`, `hidePromptMenu`.
- **UP-10** — "Attaching/Classifying file…" spinner (`UploadProgressIndicator` in `ConversationPaneChrome`).
- **Draft-a-response render** — `formatCorrespondenceDraft` (readable, not raw JSON) in `DocumentUploadedEventStream.ts`.
- **Files section (decision-1 A)** — collapsible `FilesAttachedIndicator` (1 inline / 2+ dropdown).
- **Context pane (decision-1 B)** — opens on **Execution Trace**; quick-start removed from the Context pane (`useContextTool` DEFAULT_TOOL, `ContextPaneMenu`, `ContextPaneController`, `contextToolPin`). Assistant ⋮→Quick Start modal is a SEPARATE surface (unchanged).
- **Draft → Compose** — `isCorrespondenceDraft` + `buildCorrespondenceComposeHtml`; `useConsumerChips.runBindingDispatch` opens a pre-filled Compose tab via the existing `compose.draft.html` seed (no shared-lib change).

### R4 batch (this round)
- **R4-1/2/3** — card whitespace · "More…" card → Quick Start modal · composer `inputMinRows` 3→6. (`WelcomeStartCards`, `ConversationPane`)
- **R4-4** — removed FIX #7 auto-open-Compose-on-attach; added on-demand **"Revise in Compose"** in the files tray (`mountFileInCompose` + `handleReviseInCompose`; `FilesAttachedIndicator.onRevise`). Test rewritten (`auto-load-upload-into-compose.test.tsx`).
- **R4-5** — RESOLVED (owner confirmed both files show in the tray — not a bug).
- **R4-8 (BFF)** — History was empty because `GET /api/ai/chat/sessions` was a STUB. Implemented `ISessionPersistenceService.ListRecentSessionsAsync` + `RecentSessionInfo` + Cosmos query (`SessionPersistenceService`) + wired `ChatEndpoints.ListRecentSessionsAsync` to return a top-level array. BFF deployed + hash-verified + health passed + endpoint 401 (live). Publish 44.89 MB excl PDBs.
- **R4-10** — `useConsumerChips.dispatching` → "Working…" spinner + composer lock while any chip capability (Summarize) runs.

## Remaining (owner-facing)
- **R4-11** post-summarize chips still "Summarize again" — needs owner's replacement set (proposed: Create a matter · Draft a response · Ask about these files), then a `sprk_chiptransitions` PATCH on the summarize binding (no code deploy).
- **R4-6** post-Draft cards (Send as email / Save to document / Create a matter) — Create-matter exists (surface-launch); Send/Save need to exist as actions/bindings first.
- **R4-12** wizard context — thread the session's uploaded files into the Quick Start wizard handoff seed (larger).
- **R4-7 / R4-9** — empty "Actions available…" header + Context-consistency — owner to re-capture repro.

## Key references
- UAT backlogs: [`notes/uat-feedback-2026-07-19.md`](notes/uat-feedback-2026-07-19.md) (R3), [`notes/uat-feedback-2026-07-19-R4.md`](notes/uat-feedback-2026-07-19-R4.md) (R4), [`notes/UAT-CHECKLIST.md`](notes/UAT-CHECKLIST.md).
- Seams (project CLAUDE.md): profile inject `ContextBinder.userFragment`; chips `useConsumerChips`; catalog `Services/Ai/PublicContracts/`; compose seed `composeWidgetData.ts` (`compose.draft.html` / `compose.upload`).
