# Current Task State — ai-advanced-capabilities-analysis-hub-r1

> **Last Updated**: 2026-07-30 (by context-handoff)
> **Recovery**: Read **`notes/HANDOFF-2026-07-30.md`** first — it has the full status + the detailed build plan.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Status** | **Tabbed Quick Start BUILT + deployed to spaarkedev1** (commit `980e6bc21`, NOT yet pushed). Front door + audit fixes already shipped on PR #694. |
| **Next Action** | **UAT the tabbed Quick Start** on spaarkedev1 (hard-refresh Ctrl+Shift+R — aggressive web-resource cache). Then push PR #694 + 090 wrap-up + `/test-diet`. |
| **Branch** | `work/ai-advanced-capabilities-analysis-hub-r1` · PR #694 · **1 commit ahead of origin** (980e6bc21, unpushed) · working tree clean |

### UAT checklist (tabbed Quick Start — commit 980e6bc21)
1. Open the Analysis workspace tab → it's now a **plain dataset grid** (no top cards). Row-click opens the OOB `sprk_analysis` form modal.
2. Grid **`+ New`** → opens the **Quick Start** modal on the **Analysis** tab (3 cards; Agreement Review live, other 2 "coming soon").
3. **Agreement Review** card → Quick Start closes → **Create Analysis wizard opens AS A MODAL**. On finish → result tab opens; Analysis grid tab stays.
4. Assistant ⋮ menu / chips "More…" → Quick Start opens on the **Create** tab (7 GetStarted cards, unchanged). Tab switching works.
5. Opened inside a Matter/Project record → wizard's regarding is pre-set to that record.

### The next build (tabbed Quick Start) — 6-step flow (HANDOFF-2026-07-30.md §3)
1. Analysis widget → plain dataset grid (remove cards + task-031 reopen; row-click = OOB form Layout 1); `+ New` overridden → dispatch `open_quick_start{tab:'analysis', regarding}`.
2. Quick Start = ONE Fluent `Dialog` + `TabList`: **Create** (7 GetStarted cards) + **Analysis** (3 cards). Grid `+ New` → Analysis tab; Assistant menu → Create tab.
3. Agreement Review card → close Quick Start → open Create Analysis wizard AS A MODAL (`CreateRecordWizard embedded={false}`) hosted by `WorkspacePane`; on finish → new tab (as today).
Files: PaneEventTypes (+2 intents) · AnalysisHubWidget · DataverseEntityViewWidget/DataGrid (onCreateNew) · NEW AnalysisCardsWidget · QuickStartModal · ConversationPane + WorkspacePane handlers.

### Shipped this session (commits)
`dc7a4fe8e` front door · `7317cb104` Dataverse seed · `552a091ee` P1 upload fix · `47c2058bf` P2 type hoist · `9231e7e8c` audit cleanup. Deployed + pushed.

### Deferred (owner-directed, HANDOFF §4)
- DocumentUploadWizard (Summarize Files) live bug → LEAVE, investigate separately (AIPU2-104).
- Tech-debt sweep: 4th WR delete (form ref), vestigial `ChatHistory` on GET, dead `BuildContinuationPrompt*`.
- 072 e2e/UAT → 090 wrap-up + `/test-diet`. agreements-r1 subDomain thread (owner col).

### Critical context
No generic modal shell exists — standard = Fluent `Dialog` + `TabList`. `CreateRecordWizard embedded={false}` = modal. `POST /api/documents/upload` does NOT exist (use `EntityCreationService.uploadFilesToSpe`). ADR-012: widgets can't import solution → cross-pane via PaneEventBus intents.
