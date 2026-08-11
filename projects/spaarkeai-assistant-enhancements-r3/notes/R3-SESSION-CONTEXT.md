# R3 Session Context — everything a fresh session needs

> **Written**: 2026-08-10 by the R2 session, at worktree hand-off.
> **Read this first.** It is the bridge from the shipped R2 work into R3. Primary artifact is [`design.md`](../design.md) (the Assistant⇄Workspace Interaction Contract).

---

## 0. Where you are

- **Worktree**: `C:\code_files\spaarke-wt-spaarkeai-assistant-enhancements-r3`
- **Branch**: `work/spaarkeai-assistant-enhancements-r3` (created from `origin/master` @ `cb71cf3fc`, 2026-08-10).
- **Predecessor**: `work/spaarkeai-assistant-enhancements-r2` (shipped; still has an **undeployed auth revert** — see §4).
- This worktree started from **latest master**, then the R3 primitives were carried forward (master's copy of `design.md` was a stale pre-§5.5 version). The `design.md` here is the **current** one.

## 1. R3 project primitives (in this folder)

| File | What it is | State |
|---|---|---|
| `design.md` | The Assistant⇄Workspace Interaction Contract. **§5.5 = the orchestration model** (active-item handle → auto follow-on cards → parity tool loads by id → native surface). Owner-aligned 2026-08-07. | Draft; §5.5 present. **Review edits §A1–A6 NOT yet applied** (see next row). |
| `notes/design-review-2026-08-10.md` | External architecture review of the §5.5 design. **§A1–A6 are pending edits to make to `design.md` BEFORE `/design-to-spec`**; §B1–B3 are spec-time obligations. Verdict: strong, approve into design-to-spec after the edits. | **Action list — not yet executed.** |
| `notes/assistant-workspace-contract.html` | The visual one-pager (companion artifact) of the contract, rendered for owner alignment. | Reference. |
| `notes/R3-SESSION-CONTEXT.md` | This file. | — |

## 2. Immediate next actions (in order)

1. ~~**Apply the review edits** `§A1–A6` from `notes/design-review-2026-08-10.md` to `design.md`.~~ ✅ **DONE 2026-08-10.** A1 (drift reconciled in §4/§7/§11), A2 (interaction → registration-contract field in §4/§7), A3 (active-item lifecycle paragraph in §5.5), A4 (card-economy standard cited), A5 (reactive-vs-ADR-047 disambiguation), A6 (second/per-item DoD in §8) all applied. B1–B3 folded into §12. Review file banner-marked executed.
2. **Flesh out the §12 open items** the owner and R2 session flagged as the design-to-spec inputs (partially seeded in §12 now — resolve fully at spec time):
   - the widget-type (4) ↔ context-type (6) mapping table;
   - Bindings vs `sprk_analysistool`+handler split (default to **ONE parameterized `configId`-driven overview tool**, per review B1 — do NOT author 8 per-grid handlers; CLAUDE.md §11 reuse discipline);
   - the tasks-count parity tool (the acceptance-test driver — "how many overdue tasks" must ANSWER, computing overdue server-side with *today* injected, reusing the My Tasks saved-query);
   - the per-widget interaction matrix (respond/direct/hybrid) as a **registration-contract field**, not prompt prose;
   - registration-contract required fields.
3. **Run `/design-to-spec`** on `design.md` → then **`/project-pipeline`** to generate spec.md + POML tasks + README/plan/CLAUDE.md/current-task.md + register in `projects/INDEX.md`.
4. Recommend **stopping after task generation** (do not auto-execute) — R3 touches BFF + SpaarkeAi hot paths with active-worktree overlap (§6).

## 3. The orchestration model in one breath (so you don't re-derive it)

**Awareness = identity + active-item HANDLE (an id), never content.** Generalized from the **shipped** Compose active-document flow:
1. Widget publishes the selected item as `{id,type,label}` on selection (Compose precedent: `composeActionBridge` → `registerActiveDocument` → `POST /api/compose/active-document` → client `activeSourceDocRef`). **Selection is the trigger — no user "invoke" step.**
2. Assistant auto-presents follow-on cards from the widget's declared action set (reactive/local card surface — NOT the ADR-047 server push spine).
3. Click a card → **parity tool** loads the item **by id** from the source of record → acts.
4. Output lands on the **native surface** (the widget's own composer/editor, a new Compose tab, or a chat answer).

**Email worked example** (canonical): select email → cards *Reply · Reply All · Forward · Summarize thread* → `draft_reply(communicationId, mode)` → existing `useEmailComposeActions.openComposer(mode, communicationId)` with a new `bodyOverride` → native `SendEmailDialog` opens pre-filled. Two representations of an email: `sprk_communication` record (working surface) + `.eml` archive (`emlDocumentId`, loaded like a file via `eml-render`).

## 4. R2 shipped state + the one open deploy (background)

R2 shipped end-to-end (merged, deployed): surface-awareness (active-tab-as-consent), proactive follow-on chips, true resume (chat+tabs+document+redline), Notifications banner removed. UAT rounds 1–2 + Phase 0 quick-wins deployed.

**⚠️ One undeployed change on the R2 branch**: the auth cold-start hang fix. Root cause was the **SpaarkeAi-local `requireSilentOnly` flag** (`src/solutions/SpaarkeAi/src/services/authInit.ts`), reverted `true`→`false` (commit `5169c6c42`). **NOT a shared `@spaarke/auth` change** — zero blast radius beyond SpaarkeAi. It is **built + committed on the R2 branch but not yet deployed**; the deployed code still has the cold-start hang until the R2 branch ships it. This is owner-gated. It lives on R2, not R3 — noted here only so you don't rediscover the hang and re-investigate.

## 5. Key code references R3 will touch (map, not exhaustive)

- **Awareness / prompt block**: `SprkChatAgentFactory.cs` — `BuildWorkspaceStateBlock` (~:1469), `TryDeriveVisibleState` / `FormatVisibleStateFields` (~:1547/1679), active-tab-as-consent filter. Tool-projection seam ~:826–869.
- **Tool economy (ADR-039 pre-filter)**: `AgentToolProjection.cs` `PreFilter` (~:101); `Binding.ContextTypeTags` (client-only today — R3 wires it into the server filter).
- **Four widget registration sites** (see design.md §3): `register-workspace-widgets.ts` + `register-document-viewer-widget.ts` + `register-search-criteria-result-widget.ts` + `register-structured-output-stream-widget.ts` + `src/solutions/SpaarkeAi/src/components/workspace/registerComposeWidget.ts`. R3 makes the Assistant-contract fields **required** registration metadata.
- **Tab mount path**: PaneEventBus `widget_load` → `WorkspacePane` (~:2004 `setActiveTab`) → `WorkspaceTabManager.addTab`. De-dup guard shipped in Phase 0.
- **Data lanes behind parity tools** (design.md §5): Lane 1 native Dataverse-MCP-*shaped* BFF tools (`dataverse.read_query`/`search_data`/`describe`, OBO, record-id citations — NOT an external MCP server; rejects `GETDATE()`/`COUNT`/aggregates by design); Lane 2 RAG (`IRagService`/`DocumentSearchHandler`); Lane 3 composed services (`BriefingService`, membership resolver).
- **Email**: `EmailWorkspaceWidget` / `useEmailWorkspaceRecord` / `useEmailComposeActions.openComposer` / `fetchCommunicationPrefill` / `SendEmailDialog` / `EmailDraftToolHandler` / `eml-render` (`FileAccessEndpoints.cs` ~:901).
- **Authoritative docs**: `docs/architecture/ASSISTANT-SURFACE-LAUNCH-MECHANISM.md`, `docs/standards/ASSISTANT-UI-ELEMENT-CRITERIA.md`, `docs/architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md`.

## 6. Coordination / governance

- **Hot-path declaration** (design.md §9): BFF=Y, SpaarkeAi=Y. Run `/conflict-check` before every BFF/ConversationPane PR. Check `projects/INDEX.md` for overlapping active worktrees (notification-spine, analysis-hub, compose lines have historically touched the same files).
- **BFF hygiene §10**: any BFF addition needs a Placement Justification (design.md §10 stub) + publish-size check (≤60 MB ceiling; ~49–50 MB baseline). Parity tools must reuse existing services, not add new data paths.
- **Reuse §11**: default to ONE parameterized overview tool over N per-grid handlers (review B1).
- **ADRs in play**: 039 (grounded closed catalogs, deterministic pre-filter — no classifier), 015 (data governance — R3 tightens vs R2 to id-not-content; state honestly per review A1), 040 (Cosmos session), 024 (regarding), 047 (notification spine — keep distinct from the reactive card surface), 030 (PaneEventBus), 049 (Compose).

## 7. Standing owner directives (from R2)

- Run tasks **parallel / autonomous where safe**.
- **Deploys are owner-gated** (outward-facing — confirm before deploying).
- Prioritize **robustness / capability over build-ease**.
- Deploy incrementally when that's safer.
- Master drifts fast — **re-sync master before any deploy**; husky hook is env-broken (`--no-verify` is owner-approved); retry code-page publish on transient `0x80071151`.
