# Current Task State — spaarkeai-assistant-enhancements-r1

> **Last Updated**: 2026-07-22 (by context-handoff)
> **Recovery**: Read "Quick Recovery" first. UAT remediation shipped through **R7**; now in **project close-out**. **Membership-filter DataGrid feature SHIPPED to dev 2026-07-22** (awaiting owner UAT). Immediate next: **051 evals → 054 verify → 090 wrap-up**.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Mode** | UAT remediation shipped through R7; pivoting to **project close-out** — formal tasks 050/051/054/090 + deferrals D-043-01 / D-013-03. |
| **Status** | in-progress — clean handoff point |
| **Git** | branch + `origin/branch` + **origin/master** all at **`422a5b6f4`**. Main repo `C:/code_files/spaarke` local master synced. Working tree clean. |
| **050 dispatch fix (2026-07-22)** | UAT: "what are my tasks?" errored "No session files available" — a prompted Action with no file/operand hits `ResolveFileOperandAsync` (hard-fails on 0 files). Fix: list-tasks input schema now declares **`documentText`** (a ContextBinder OperandVocabulary field) so `HasStructuredOperand`=true → structured path relaxes the no-file stop; content ignored (grid queries tasks client-side). Live Action `57651aad` sprk_inputschema+systemprompt updated + seed mirrors + BFF **restarted** (healthz 200). **AWAITING owner re-test.** Note: `compose-draft-document` likely has the SAME latent no-file issue (its `request` also isn't an operand-vocab field) — verify if a fileless "write a brief" fails; same fix applies. |
| **Deployed (dev)** | `sprk_spaarkeai` (4825 KB — R7 + #8 Memory + D-043-01 chip reorder + **050 My Tasks**); `sprk_createeventwizard` (D-013-03); CommunicationActions PCF v1.1.4 (R6). **BFF unchanged across R4–R7 + close-out** (050 = catalog DATA + client only). Dev BFF **restarted 2026-07-22** to clear the capability cache (healthz 200) so `list-tasks` is discoverable. |
| **Closed by owner** | **R4-7** (won't-fix) · **R7-5b** (dropped) · **R4-9** (separate project). |
| **Close-out done (2026-07-22)** | **D-013-03** ✅ · **#8 Memory tool** ✅ · **D-043-01 v1** ✅ · **050 list-tasks / My Tasks grid tab** ✅ (deployed; catalog live: Binding `5b1870b9`, Action `57651aad`, grid config `ac05e4f1`; create-task/create-todo disambiguation cues live; eval debt E-050-01..12 logged). **Also fixed a master-breaking `@spaarke/notifications` build gap** (tsconfig path + vite source alias + `@microsoft/signalr` install + dedupe). **Pending: owner FR-J1 review of the 3 tool-descriptions** (presented 2026-07-22). |
| **Membership-filter feature (SHIPPED 2026-07-22)** | ✅ Built the reusable `behavior.membershipFilter` DataGrid feature + applied to 050. **NO BFF change** (endpoint pre-existed). Landed: `MembershipFilter` type (`types/DataGridConfiguration.ts`) · `createMembershipResolver` (`services/membership.ts`, barrel-exported) · `overlayMembershipFilter` (`fetchXmlOverlay.ts`) · gated async resolve stage + `membershipResolver` prop in `DataGrid.tsx` · `DataverseEntityViewWidget` reads `AiSessionContext` optionally → builds resolver from `authenticatedFetch`. **15 new unit tests + 43 DataGrid/services tests pass; ui-components builds clean.** 050 grid config `ac05e4f1` updated LIVE → `source=savedquery 12a510e4` ("My Tasks Open", verified Deadline+Task+Reminder/eventstatus=Open/**no owner filter**) + `membershipFilter: true` (all roles), dropped `ownerid eq-userid`. SpaarkeAi rebuilt + deployed (`sprk_spaarkeai` 5206a442, 4.83 MB). Graduated into `docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md` §6.6. **§7 decisions RESOLVED with recommended defaults** (all roles; reuse saved query) — both one-line-reversible via live grid-config data. **AWAITING owner UAT of "what are my tasks?"** (should now show tasks the user is a MEMBER of, not just owns). |
| **Next Action** | **051** eval suite (NFR-06 gate; E-050-01..12 already logged in `notes/owed-eval-cases.md`) → **054** deploy verify (DEV ONLY) → **090** wrap-up (`/test-diet` gate). |
| **050 status** | ✅ FUNCTIONAL + refined on dev. Membership scoping shipped (see row above). FR-J1 tool-descriptions ✅ approved. |

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
