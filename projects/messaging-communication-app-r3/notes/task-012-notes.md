# Task 012 — Two-pane conversation shell + thread list (FR-01/10)

> **Status**: Implemented + verified 2026-07-20. Rigor FULL · sonnet · high. Ran in parallel with task 011 (`ConversationView`).
> **Spec**: FR-01 (mount-agnostic shell), FR-10 (thread list: name + unread only, word filter, create ＋), FR-16 (list endpoint), NFR-01 (no client-side membership union), NFR-05 (ARIA + keyboard).

---

## Files created

- `src/client/shared/Spaarke.UI.Components/src/components/ConversationWorkspace/ConversationWorkspace.tsx`
- `src/client/shared/Spaarke.UI.Components/src/components/ConversationWorkspace/subcomponents/ThreadList.tsx`
- `src/client/shared/Spaarke.UI.Components/src/components/ConversationWorkspace/index.ts` (local barrel only — NOT wired into the shared library's `src/components/index.ts` / `src/index.ts`, per the file-ownership boundary; main session adds that export)
- `src/client/shared/Spaarke.UI.Components/src/components/ConversationWorkspace/__tests__/ConversationWorkspace.test.tsx` (14 tests, all green)
- `src/client/shared/Spaarke.UI.Components/src/services/communicationThreadListApi.ts` (new service — NOT added to `communicationTimelineApi.ts`, per instructions)

No files owned by task 011 (`ConversationView/**`, `communicationTimelineApi.ts`, `CommunicationTimeline.types.ts`) were touched. `git status --porcelain` scoped to my paths shows only the files above as new (`??`); task 011's concurrent edits are visible in the wider worktree status but untouched by this task.

---

## Escalation — FR-16 cannot express the regarding filter (documented deviation, Path A)

🔔 **Contract gap found and resolved (not a blocking escalation)**

- **What the POML assumed**: constraint `FR-16` text says *"In record mode, pass the regarding filter so only that record's threads return"* — implying `GET /api/communications/threads` accepts a regarding-scoping parameter.
- **What the shipped endpoint actually does** (verified against `Sprk.Bff.Api/Api/CommunicationEndpoints.cs` line 114-128 and `CommunicationThreadReadModels.cs`, both landed by task 003, same project): `GET /api/communications/threads` takes only `search` / `top` / `pageToken`. Its route doc-comment states it is **deliberately** "NOT scoped to any regarding lookup" — that is exactly what lets record-less Direct threads appear in the all-mode list (FR-16 Success Criterion 5). There is no `entityType`/`id`/`regarding` parameter to pass.
- **Resolution chosen (CLAUDE.md §6.5 Path A — project-scoped exception)**: record mode (`regarding` prop present) routes to the EXISTING, already-shipped, server-access-filtered `GET /api/communications/by-regarding/{entityType}/{id}` endpoint (R2 task 010) instead of adding a parameter to FR-16's list endpoint. `communicationThreadListApi.ts`'s `listThreadsByRegarding()` adapts that endpoint's richer `RegardingReadResult.threads[]` (which carries full messages, since it also backs the regarding-mode Timeline) down to the same lightweight `IThreadListItemDto` row shape `listThreads()` returns, so `ConversationWorkspace` can render both modes through one row type.
- **Why this satisfies NFR-01**: both paths are 100% server-filtered (impersonation + Dataverse row-level security). This component does **not** fetch the all-mode set and filter it down for record mode — it calls a *different, already access-filtered* server endpoint. No membership union, no client-side inference of threads the server didn't return.
- **Alternative considered and rejected**: adding a `regarding`/`entityType`/`id` query param to FR-16's `/threads` endpoint (Path B — ADR/spec amendment). Rejected for this task because (a) it would touch `Services/Communication/` — a shared, `parallel-safe:false` surface per this project's CLAUDE.md, outside my file-ownership boundary and this task's scope; (b) a fully equivalent, already-shipped, already-tested endpoint (`by-regarding`) already does the job with zero new backend work; (c) no behavior gap remains — every acceptance criterion is met.
- **Recommendation for the human reviewer**: no backend change needed to close this out. If a future task wants FR-16 itself to support light-weight record-scoped paging (e.g. for a very large record with hundreds of threads, where `by-regarding`'s full-message payload would be wasteful), that's a legitimate Path B candidate — track as a defer/issue, not a blocker for this task.

---

## Component contract

### `ConversationWorkspace` props

```ts
export interface ConversationWorkspaceProps {
  authenticatedFetch: AuthenticatedFetchFn;   // ADR-028 — injected, never imports @spaarke/auth
  bffBaseUrl?: string;
  regarding?: { entityType: string; id: string };  // present = record mode; absent = all mode
  renderConversation?: (props: IConversationRendererProps) => React.ReactNode;  // right-pane seam, see below
  onCreateThread?: () => void;                // ＋ affordance callback (NewThreadModal is task 024)
  onThreadSelected?: (threadId: string | undefined) => void;
  onError?: (error: Error) => void;
  pageSize?: number;                          // all-mode `top` param, default 50
  className?: string;
}
```

### Renderer seam (right pane) — how the main session wires `ConversationView` in

`ConversationView` (task 011) does not exist yet at the time this task ran (concurrent wave). `ConversationWorkspace` never imports it. Instead:

```ts
export interface IConversationRendererProps {
  threadId: string;
  authenticatedFetch: AuthenticatedFetchFn;
  bffBaseUrl?: string;
}
```

- When `renderConversation` is supplied, the shell calls it with `{ threadId, authenticatedFetch, bffBaseUrl }` for the currently-selected thread and renders the returned node in the right pane.
- When omitted, a local `DefaultConversationPane` placeholder renders (`"Select a thread…"` / `"Conversation placeholder for thread {id}."`) — this is what the shipped component tests exercise, so `ConversationWorkspace` is independently testable/mountable before task 011 lands.
- **Wiring once both tasks are merged** (main session action — do NOT let either task edit the shared barrel per the file-ownership boundary):

  ```tsx
  <ConversationWorkspace
    authenticatedFetch={authenticatedFetch}
    bffBaseUrl={bffBaseUrl}
    regarding={regarding}
    renderConversation={({ threadId, authenticatedFetch, bffBaseUrl }) => (
      <ConversationView threadId={threadId} authenticatedFetch={authenticatedFetch} bffBaseUrl={bffBaseUrl} />
    )}
    onCreateThread={openNewThreadModal}
  />
  ```

  Also add `export * from './ConversationWorkspace';` to `src/components/index.ts` (and confirm the shared `src/index.ts` barrel re-exports `./components`) at that point — this task intentionally left the shared barrel untouched.

### `communicationThreadListApi.ts` contract

- `listThreads({ search?, top?, pageToken? }, client) → IThreadListResultDto` — FR-16 `GET /api/communications/threads`. All-mode.
- `listThreadsByRegarding(entityType, id, client) → IThreadListResultDto` — adapts `GET /api/communications/by-regarding/{entityType}/{id}` into the same row shape. Record mode (see escalation section above).
- `getThreadUnreadCount(threadId, client) → IThreadListUnreadCountDto` — `GET /threads/{threadId}/unread-count`, called with `since` omitted for each row in the currently-visible page (bounded — page size ≤ 200, record mode ≤ that record's thread count).

**Known limitation (logged, not blocking)**: neither `ThreadListItem` nor `by-regarding`'s `threads[]` project an unread signal, and there is no persisted per-user "last seen this thread" watermark endpoint — `since` is a purely client-side, per-mount concept currently owned by the open-thread timeline (`CommunicationTimeline`'s `ADVANCE_LAST_SEEN` reducer action, not persisted anywhere). Calling `unread-count` with `since` omitted therefore returns the **total readable message count** for the thread (server doc: "omitted = all"), not a true post-last-visit delta. It is still a fully server-computed, access-filtered number — just coarser than a persisted watermark would give. "Mark as read" on a list row is an **optimistic local clear only** (no server write — none exists to call). Filed to `notes/defer-issues.md` as a follow-up candidate (persisted per-user last-seen watermark) — out of scope for this task.

### Word filter

- **All mode**: `search` is passed server-side to FR-16's `/threads` (debounced 300ms, mirrors the existing `RecipientField.tsx` debounce pattern in this library).
- **Record mode**: `by-regarding` has no `search` parameter, so the word filter narrows the already-loaded, already-fully-access-filtered set **client-side**. This is safe under NFR-01 — it narrows *display* of a complete, server-returned set; it never widens visibility or infers threads the server didn't return.

### Default selection

First row of the loaded (server-ordered) set is selected on initial load. All-mode FR-16 orders `createdon desc` server-side, so "first" = "most recent," satisfying the acceptance criterion without client-side sorting. Selection resets to the new first row when `regarding` changes (record ↔ record, or record ↔ all); it is **not** reset when only the word filter changes (standard mail-client behavior — the right pane can keep showing a thread even if it's currently filtered out of the visible list).

---

## Tests (14, all green)

`npx jest --testPathPatterns "ConversationWorkspace"` — mount-agnostic (2), thread-list content incl. word filter both modes + create ＋ (4), selection/renderConversation seam (3), empty/loading/error + ARIA/keyboard (4), dark mode (1).

## Verification results

| Check | Result |
|---|---|
| `npx tsc --noEmit` | 2 pre-existing errors only (`EntityCreationService.ts` `@spaarke/sdap-client`, `useWizardPageBootstrap.ts` `@spaarke/auth` — unbuilt sibling dist, unrelated to this task). **No new errors.** |
| `npx jest --testPathPatterns "ConversationWorkspace"` | **14/14 passed.** |
| `npx eslint` on the 4 new/changed files | **Clean**, no warnings/errors. |
| `git status --porcelain` scoped to my paths | Only `ConversationWorkspace/**` (new dir) + `communicationThreadListApi.ts` (new file) — no task-011-owned files touched. |

---

## Quality gates (task-execute Step 9.5)

**code-review** — no Critical/Violation findings. Warnings/Suggestions surfaced and resolved or accepted:
- **[Warning, addressed]** Per-row unread-count fetches (`getThreadUnreadCount`, N calls via `Promise.allSettled`) previously swallowed rejections silently — added a `console.warn` per failed row so backend/network issues surface for debugging rather than disappearing.
- **[Warning, accepted]** `ConversationWorkspace.tsx` cyclomatic-complexity estimate (~28) exceeds the 15 "warning" threshold — inherent to orchestrating dual-source list load + debounce + client-filter + unread fan-out + selection-reset in one component. Density matches existing codebase precedent (`CommunicationTimeline.tsx`'s `ThreadModeCommunicationTimeline`, similarly dense). Flagged as a candidate for a future `useThreadList`/`useThreadUnreadCounts` hook extraction if the component grows further — not blocking for this task.
- **[Warning, accepted/documented]** N+1-style per-thread unread-count fetch pattern — bounded (page size ≤200 / record's thread count) and parallelized (`Promise.allSettled`, not sequential awaits), not a batch call. No batch unread-count endpoint exists server-side; documented as a known limitation in this file's "getThreadUnreadCount" section above.
- **[Suggestion, accepted]** No aria-live announcement on keyboard-driven selection change — matches the sophistication level of sibling components (only `CommunicationTimeline` has a live region, for new-message arrival, not selection). Not added; flagged for a future accessibility pass if needed.
- No security, ADR-028, or Fluent-token findings — confirmed no `any`, no hardcoded colors, no `@spaarke/auth` import (grep-verified), no raw `fetch`+`Authorization` header.

**adr-check** — ADR-021 (Fluent v9): compliant (tokens-only, `@fluentui/react-components` only, both themes tested). ADR-028 (auth v2): compliant (`authenticatedFetch` injected end-to-end, no `@spaarke/auth` import, no token-bridge symbols). ADR-012 (shared library, context-agnostic): compliant (no `Xrm`/`ComponentFramework` references, props-only configuration). ADR-038: N/A per its own Domain section (React/Jest tests are out of its .NET-only scope). **0 Violations, 0 Critical.**

Re-verified after the code-review fix: `npx tsc --noEmit` (2 pre-existing unrelated errors only), `npx eslint` (0 errors/warnings), `npx jest --testPathPatterns "ConversationWorkspace"` (14/14 passed).

---

## Justification (CLAUDE.md §11)

- **Existing**: `CommunicationsWorkspaceWidget.tsx` is a single-pane DataGrid-backed widget; no two-pane shell listing threads (incl. record-less) beside a conversation exists.
- **Extension**: not applicable — this is new shared surface (the reusable host for the widget/code-page/modal mounts), not a modification of the widget.
- **Cost of doing nothing**: without the shell, FR-01's single mount-agnostic widget doesn't exist, and the FR-16 record-less thread list has no UI home.
