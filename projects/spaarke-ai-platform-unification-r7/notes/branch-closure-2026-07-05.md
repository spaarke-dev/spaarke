# R7 Tactical Branch Closure — `work/spaarke-ai-platform-unification-r7`

> **Date**: 2026-07-05
> **Authored by**: `spaarke-ai-architecture-redesign-r1` task 025 (FR-P1-06)
> **Companion to**: [`close-out-absorbed-by-ai-architecture-redesign-r1.md`](close-out-absorbed-by-ai-architecture-redesign-r1.md) (task 013 — project/portfolio close; Issue #501 closed)
> **This note**: BRANCH disposition only — keep 4 fixes, drop the dispatch patches.

---

## 1. Topology finding (the key fact)

`git log HEAD..origin/work/spaarke-ai-platform-unification-r7` on branch
`work/spaarke-ai-architecture-redesign-r1` is **EMPTY**: the r7 branch tip
(`75e94fe4c`) is the exact merge-base — the redesign-r1 project branch was
**forked from the r7 tip**. Every r7 commit is already an ancestor of the
redesign branch.

Consequences:

- **No cherry-picks were needed.** All four keep-fixes are inherited by ancestry.
- **The drop-item CONTENT was also inherited** and had to be deleted in the
  working tree of the redesign branch (done by task 025, this note's commit).
- **Nothing is stranded** on the r7 branch: everything reachable from its tip is
  reachable from `work/spaarke-ai-architecture-redesign-r1`, which merges to
  master through the redesign-r1 PR train.

## 2. Unmerged-commit disposition table

Commits on `origin/work/spaarke-ai-platform-unification-r7` not yet on
`origin/master` (all are ancestors of the redesign branch), oldest first:

| Commit | Subject | Disposition |
|---|---|---|
| `358930e2e` | style: auto-format Prettier (CI) | **INHERITED** (formatting only) |
| `1dc51f209` | docs(r7): R7 close plan 2026-07-03 | **INHERITED** (project notes; historical record) |
| `b1e4a4b11` | docs(r7): revise D-13 slash-command decision | **INHERITED** (project notes; historical record) |
| `139014adc` | feat(bff/r7): server-side `linear_dispatch` SSE event + keyword-match bypass | **DROPPED** — content deleted by task 025: ChatEndpoints keyword bypass block, `TryDetectExplicitConsumerType`, `SummarizeKeywordRegex`, `LinearDispatchSseEvent.cs`, `ChatSseEventFactory.CreateLinearDispatchEvent`. Second intent-detection mechanism forbidden by ADR-039; routing surface outside the three entry paths. |
| `7f0e42b30` | feat(client/r7): client-side `linear_dispatch` wiring | **DROPPED** — content deleted by task 025: `executeLinearDispatch.ts`, ConversationPane `handleLinearDispatch` + `onLinearDispatch` prop, shared-lib `ILinearDispatchPayload` / SSE union member / `useSseStream` parser branch + callback-ref / `SprkChat` forwarding effect. |
| `a9bdd2f88` | fix(client/r7): retire NL `executeSummarizeIntent` branch | **KEEP-WITH-REASON** (inherited) — deletion-only commit that removed the client half of the double-dispatch race; aligned with the redesign (one dispatch decision per turn). Final deletion of `executeSummarizeIntent.ts` / `intentMatcher.ts` is owned by redesign task 023. Comment references to the dropped tokens were scrubbed by task 025. |
| `5ab21578b` | fix(bff+client/r7): persist ExtractedText on ChatSessionFile | **KEEPER 2/4** — verified present: `Models/Ai/Chat/ChatSession.cs` (`ExtractedText`), `Services/Ai/LinearConsumers/SessionFileTextSource.cs`, `Api/Ai/ChatDocumentEndpoints.cs`. |
| `ab8ab68a8` | fix(client/r7): resume, don't recreate persisted chat sessions | **KEEPER 1/4 (session-id fix)** — verified present: `useChatSession.resumeSession` (SprkChat hooks), SprkChat mount wiring, session-id persistence in `Spaarke.AI.Widgets/src/providers/AiSessionProvider.tsx` (module moved from SpaarkeAi to the shared lib since R7; behavior intact, now localStorage-backed per R4 task 031). |
| `68e8b96f1` | fix(client/r7): auto-promote ready chips to /documents | **KEEPER 3/4** — verified present: `ConversationPane.tsx` auto-promote block ("R7 Wave 12.3 Phase 12.3a UAT fix — auto-promote ready chips", `promotedChipIds` / `pendingPromotionIdsRef` guards). |
| `2d4e0c8d8` | fix(client/r7): bridge emits `field_delta` from complete chunk result | **KEEPER 4/4 (field_delta synthesis)** — client bridge synthesis verified present in `sseToPaneEventBridge.ts` (`case 'complete'` synthesizes per-property `field_delta` before `streaming_complete`). NOTE: this commit's 17-line ChatEndpoints hunk was the "Wave 12.3 keyword-check" **diagnostic log** — a named DROP item — deleted by task 025. The keeper substance (bridge synthesis) is untouched. |
| `1e366dc5b` | docs(r7): summarize-flow synopsis | **INHERITED** (project notes) |
| `1a8bf55a6` | checkpoint(r7): context-handoff pre-compact | **INHERITED** (checkpoint doc) |
| `5f77a1d9c` … `75e94fe4c` (12 commits) | docs(architecture/ai-audit/ai-redesign-r1): canonical AI architecture v0.1→v0.4, audit inventory, overlay matrix, ADR-039/040, design.md, spec.md | **INHERITED — redesign-r1 genesis**. These ARE the redesign project's foundation documents; they live on the project branch by construction. |

### Related drop outside the tactical branch

| Commit | Subject | Disposition |
|---|---|---|
| `2d861eb6a` (already on master, Wave 12) | diag(bff/r7/w12): log LinearConsumers dispatch state in AnalysisEndpoints | **DELETED by task 025** — temporary `[LinearDispatch]` config-dump diagnostic from the same dispatch saga; root cause long since identified. The surrounding `LinearConsumers` config-key routing itself is deleted later by FR-P3-01. |

## 3. Empty-attachments guard — behavior handoff (NOT merged as code)

The r7 branch carried the guard in two dropped places: the server-side
`sessionAttachmentIdsForDispatch.Count > 0` check inside the ChatEndpoints
bypass, and the client-side defensive skip in `handleLinearDispatch`. Both are
now deleted **as code**. The BEHAVIOR is preserved as a requirement:

> A summarize (or any file-consuming) dispatch MUST NOT fire with an empty
> session-file list — the transient window where a message arrives before
> `session.UploadedFiles` hydrates must fall back gracefully (no empty-fileIds
> POST → empty text → visible stream error).

**Owners**: redesign task 022 (Event-path precondition) and task 023
(Click-path `dispatchConsumer` helper precondition). Recorded in
`projects/spaarke-ai-architecture-redesign-r1/notes/task-025-r7-branch-disposition.md`.

## 4. Grep-zero evidence (redesign branch, post-deletion)

Case-insensitive sweep of `src/` + `tests/` for
`linear_dispatch | LinearDispatch | TryDetectExplicitConsumerType | executeLinearDispatch | ILinearDispatchPayload | onLinearDispatch | setOnLinearDispatch`
returns **zero hits**. Remaining mentions outside git history are
documentation-of-the-drop only: this note, project notes/POMLs, frozen audit
inputs, and the disposition table rows in
`docs/architecture/SPAARKE-AI-ARCHITECTURE-AND-COMPONENT-DESIGN.md` (lines
recording "DEL — never merges") — kept with reason: they ARE the decision record.

## 5. Branch disposition

- `origin/work/spaarke-ai-platform-unification-r7` is **safe to delete**
  (operator action or `/repo-cleanup`): zero open PRs (last merged PR #546,
  2026-07-03); tip fully contained in `work/spaarke-ai-architecture-redesign-r1`.
- The r7 **worktree** at `C:/code_files/spaarke-wt-spaarke-ai-platform-unification-r7`
  is PRESERVED per operator workflow (devops-project-archive override of D-18/NFR-09);
  registry row archives after task 025.
- Do NOT re-branch from the r7 tip: new work forks from master or the redesign branch.
