# Current Task State — spaarkeai-assistant-enhancements-r1

> **Last Updated**: 2026-07-20 (by context-handoff)
> **Recovery**: Read "Quick Recovery" first. This is a **UAT-driven remediation** stream (not formal POML task execution) — the Assistant repositioning features are shipped; we're iterating on UAT feedback (R3 → R4).

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Mode** | UAT remediation (R4 round-2 shipped; awaiting owner's next UAT pass) |
| **Status** | in-progress — clean handoff point |
| **Git** | branch + `origin/branch` at **`47fdfb83f`**, fully merged to master (origin/master since advanced to `80aaafb96` by ANOTHER project — my work is in its history). Working tree **clean**. |
| **Deployed** | dev: `sprk_spaarkeai` code page **and** `spaarke-bff-dev` (BFF) — both current |
| **Next Action** | Wait for owner's UAT pass against [`notes/UAT-CHECKLIST.md`](notes/UAT-CHECKLIST.md). Then pick up **R4-11** (post-summarize card set — needs owner's card choice, then a live `sprk_chiptransitions` PATCH, no deploy), **R4-6** (post-Draft Send-email/Save/Create-matter cards — Send/Save actions must exist first), **R4-12** (thread uploaded files into Quick Start wizard handoff — larger). |

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
