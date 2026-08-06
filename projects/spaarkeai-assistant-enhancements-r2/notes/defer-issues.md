# Deferred work & discovered issues — spaarkeai-assistant-enhancements-r2

> Source of truth for deferred work + issues discovered during execution (CLAUDE.md project rule).
> Every entry names a **concrete behavior/contract that fails without the work** (§11).
> **GitHub filing**: PENDING for all entries below — file via `/defer` at project wrap or owner discretion
> (these are internal follow-ups discovered mid-wave; recorded locally now so nothing is lost).

---

## DI-01 — FR-D7 History rows need a BFF sessions-list projection extension (preview + message count + tab summary)

- **Discovered**: 2026-08-06, task 037 (HistoryOverlay rebuild).
- **Concrete failing behavior**: the rebuilt History rows render a last-message **preview**, **message count**, and a **tab summary** ("Email · Compose") when those fields are present, but `GET /api/ai/chat/sessions` does not return them today — `RecentSessionInfo` / `RecentSessionDto` carry only `{id, title, entityType, entityName, playbookName, updatedAt}`. So FR-D7's "rows show preview + message count + tab summary" is **UI-complete + forward-compatible (graceful omission, tested) but not end-to-end demonstrable** until the projection is extended. Without it, History rows show title + timestamp only.
- **Why deferred (not done in D3)**: this is un-planned BFF work (no POML); task 037 was scoped client-only ("Edits HistoryOverlay.tsx only"). The client degrades gracefully. The tab-summary also carries a small presentation-semantics decision (source from tab `DisplayName` vs `WidgetType`) worth a deliberate call.
- **Recommended fix** (small, additive; the Cosmos query already reads `conversationSummary` + `entityRefs`):
  - `SessionPersistenceService.ListRecentSessionsAsync` projection SQL — add `ARRAY_LENGTH(c.messages) AS messageCount` and `c.tabs` (the `firstMessage`/`conversationSummary` are already selected).
  - `RecentSessionProjection` — add `MessageCount` (int) + `Tabs` (`List<StoredWorkspaceTab>`).
  - `RecentSessionInfo` (`ISessionPersistenceService.cs`) + `RecentSessionDto` (`ChatEndpoints.cs`) — add `Preview` (`conversationSummary ?? firstMessage`, truncated), `MessageCount`, `TabsSummary` (join of tab `DisplayName`s by " · "). Client already reads `item.preview ?? item.conversationSummary`, `item.messageCount`, `item.tabSummary ?? item.tabs[].join(" · ")`.
  - Unit test for the projection mapping; §10 publish-size check.
- **Recommended home**: a small dedicated task in Phase D, OR fold into **task 039** (deploy+verify D) since it's BFF and deploys with D. Owner to slot.
- **GitHub issue**: PENDING.

## DI-02 — Un-flushed TipTap compose edits in the OUTGOING session could be lost on a History switch

- **Discovered**: 2026-08-06, task 035 (rich History restore) — escalation trigger evaluated, judged below the STOP bar.
- **Concrete failing behavior**: task 035 now **clears compose tabs on a genuine History switch** (required — preserving them would corrupt the reopened session's tab set and re-block restore). The compose **document is durable** (server-authoritative per ADR-049 + `composeRunPersistence` localStorage, explicit-close-only removal), so the document is never destroyed. BUT if the TipTap→server edit flush is **not** continuous/auto-on-unmount, a mid-edit draft in the *outgoing* session could lose keystrokes entered since the last flush when the user switches History before those edits flush.
- **Why below the STOP bar**: document durability is guaranteed; only un-flushed in-memory deltas since the last flush are at risk — and the same property pre-exists on any tab close (035 did not create the flush cadence, it added one more path that closes a compose tab).
- **Recommended fix / investigation**: confirm the TipTap edit-flush cadence (debounce interval / flush-on-blur / flush-on-unmount). If edits are NOT flushed on unmount, add an explicit flush before `clearAllTabs()` on the History-switch path (or make the compose editor flush-on-unmount). If flush is already continuous/on-blur, close this as a non-issue with a documenting comment.
- **GitHub issue**: PENDING.
