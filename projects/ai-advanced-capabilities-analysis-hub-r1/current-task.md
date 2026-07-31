# Current Task State — ai-advanced-capabilities-analysis-hub-r1

> **Last Updated**: 2026-07-30 (by context-handoff)
> **Recovery**: Read **`notes/HANDOFF-2026-07-30.md`** first — it has the full status + the detailed build plan.

---

> **Recovery**: Read **`notes/HANDOFF-2026-07-31.md`** first — full status + the execution-machinery map for Phases 2/3.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Status** | Tabbed Quick Start + 4 UAT rounds SHIPPED (merged via PR #694 + more). **Phase 1 of the analysis-execution loop SHIPPED + deployed** (NDA Analysis card + open analysis document in the editable **Compose** surface). Phase 2 (auto-run review) + Phase 3 (durable history recall) NOT built. |
| **Next Action** | **Owner UAT of Phase 1** on spaarkedev1 (hard-refresh Ctrl+Shift+R): NDA Analysis card → wizard → upload → doc opens in the **editable Compose surface** with agreement-analysis tools (not a preview). Then **Phase 2**: confirm approach + build (bind session + auto-dispatch `nda-review`). |
| **Branch** | `work/ai-advanced-capabilities-analysis-hub-r1` · **0 behind / 5 ahead of origin/master** (merge `206d721d7`; NOT pushed) · working tree clean |

### The MUST-HAVE end-state (owner, this session)
NDA Analysis card → wizard → upload → **review RUNS** → file in **editable Compose/TipTap** with advisory comments + summary; **durable** (analysis + conversation history + review results bound to `sprk_analysis`); **reopen** → Compose + history + prior review. Cards are specific paths (option 2): live = **NDA Analysis** (worktype 100000000, wired to `nda-review` capability).

### Phases
- **Phase 1 (DONE, deployed):** open analysis document in editable Compose (wizard-finish + analysis-open). `8b93ad9e2`.
- **Phase 2 (NEXT — confirm approach):** on finish bind a chat session to the analysis (`HostContext.EntityType='sprk_analysisoutput'`) + auto-dispatch the `nda-review` binding → advisory comments in Compose. **Machinery map in HANDOFF §4.**
- **Phase 3:** durable recall — reopen restores session (history) + re-projects review results into Compose. Coordinates with `agreements-r1` + `Services/Ai` (owned by architecture-redesign-r2; PublicContracts only).

### Deploy recap
`pwsh scripts/Deploy-SpaarkeAi.ps1` (prebuilt `dist/spaarkeai.html`). Rebuild ui-components + ai-widgets dist first if changed; clear vite cache; `npm run build` in SpaarkeAi. Web resource `sprk_spaarkeai` on spaarkedev1.

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
