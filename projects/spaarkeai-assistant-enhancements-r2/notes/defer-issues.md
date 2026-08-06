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

## DI-04 — FR-D9 "Set related record" needs the ADR-024 regarding machinery (not expressible on the promote endpoint) — task 034 escalated

- **Discovered**: 2026-08-06, task 034 (FR-D9) — escalation trigger FIRED; **no code changed**; working tree clean.
- **Concrete failing behavior**: filing an otherwise-unassociated analysis to a matter so it "appears on the matter's Analyses tab" (Success Criterion 7 / FR-D9 criterion #3) does not work as scoped. Ground truth: the Matter Analyses subgrid is driven by `sprk_analysis.sprk_regardingmatter` (ADR-024 dual-field; `sprk_matter/FormXml/.../matter-analyses-tab.xml`, relationship `sprk_analysis_RegardingMatter_sprk_matter`) — so it REQUIRES an ADR-024 `regarding` field-set write on the created `sprk_analysis`.
- **Why blocked in scope**:
  1. The promote endpoint (`AnalysisEndpoints.PromoteSession` → `DataverseWebApiService.CreateAnalysisAsync`) writes only `sprk_name/sprk_documentid(required)/statuscode/sprk_Playbook` — **no `regarding`**, and **400s if the session has no document**. `AnalysisPromoteRequest` can't carry regarding. No server-side `sprk_analysis` regarding resolver exists.
  2. The canonical association flow (`CreateAnalysisWizardWidget`) writes `regarding` CLIENT-side via `applyResolverFields` (ADR-024 `PolymorphicResolverService`) + `NavigationService` (record-lookup dialog) + `EntityCreationService` (SPE container for the document-create path). `HistoryMenu` has none of these props — reaching them needs a **ConversationPane wiring change** (the spine), which 034's parallel boundary forbade.
  3. Hand-rolling a `sprk_regardingmatter`-only write in `AnalysisEndpoints`/`Spaarke.Dataverse` would violate ADR-024 (MUST populate all 5 resolver fields via the shared resolver); duplicating the wizard inside a popover would violate §11/ADR-010.
  4. FR-D9 criterion #4 ("already-associated session doesn't offer the action") client-side ALSO needs the session-list projection to expose analysis-ownership/HostContext — overlaps **DI-01** (the server guard at `PromoteSession:1326-1330` already 400s re-promotion server-side, so the data is safe; only the client affordance-hiding needs the projection).
- **Options (owner picks — §6.5)**:
  - **Path C (agent-recommended)**: on "Set related record", launch the EXISTING `CreateAnalysisWizardWidget` association step (ADR-024-compliant regarding + document-create + `sprk_analysis` create) seeded with the loose session, then reuse the promote endpoint for the session-FK bind. Needs `NavigationService`/container context reaching the History trigger → **re-scope 034 to serialize with/after the ConversationPane spine** (not concurrent with a ConversationPane task), or add an explicit prop-passthrough owned by the spine.
  - **Path A (project exception)**: reduce FR-D9 to only "(b) attach to an EXISTING document" (expressible today via `DocumentId` + a document picker); defer the matter/project `regarding` path; document criterion #3 as partially met.
  - **Path B (contract change)**: extend the promote endpoint + `Spaarke.Dataverse` with a compliant server-side `sprk_analysis` regarding write + relax the document-anchor for document-less sessions. Largest blast radius; recommend a dedicated task, not this one.
- **Also needs an owner call**: FR-D9's explicit "otherwise-unassociated (document-less)" target has no document to anchor the `sprk_analysis` (the lookup is required) — auto-create a document? relax the anchor? restrict the action to sessions that already carry a document?
- **Note**: criterion #1 ("action named 'Set related record'") is ALREADY satisfied — task 037's committed overflow menu already shows that label (wired to a documented no-op). Leaving the no-op avoids shipping a broken/misleading prompt.
- **GitHub issue**: PENDING.

## DI-03 — FR-D5 attachment-chip rehydrate is a paired server+client slice (manifest not client-exposed; SprkChat has no restore seam) — task 036 escalated

- **Discovered**: 2026-08-06, task 036 (attachment chip rehydrate) — STOPPED at Step 1 and escalated (§6) rather than fabricate chip data; **no code changed**.
- **Concrete failing behavior**: reopening a session that had a file attached does NOT show the file chip (Success Criterion 4). The POML assumed a trivial client-only rehydrate; two blockers make it a paired slice:
  1. **Server**: the `UploadedFiles` manifest (`StoredSession.UploadedFiles`, written by `SessionPersistenceService.UpdateUploadedFilesAsync`) is read server-side ONLY (to build agent tool context on message-send at `ChatEndpoints.cs:671`). It is **not projected into any client-facing restore GET** — traced `/restore` (`SessionRestoreResponse`/`RestoredSession`), `/tabs` (`SessionTabsResponse`), `/history` (`ChatHistoryResponse`): none carry it. So there is no wire contract to rehydrate from.
  2. **Client**: the chip renders via `FilesAttachedIndicator` (`ConversationPane.tsx:2846-2857`) driven by `useAttachments.attachmentChips`, which is a **read-only mirror of SprkChat's `onAttachmentsChanged`** — SprkChat's internal `useChatFileAttachment` state is only seeded by user upload actions. Rehydrating needs either (a) a new `initialAttachments` prop seam on **SprkChat (`@spaarke/ui-components` shared lib)**, or (b) a parallel render path for restored files. Both are design decisions (shared-lib blast radius / new UI path) beyond a client-spine edit.
- **Why not done in this wave**: (1) requires a server DTO projection (out of 036's client-only scope), (2) requires a shared-lib SprkChat change or a parallel render — a design call that should not be rushed inline. **Escalated to owner** with options: (A) do the paired server+client slice now as a re-scoped/new task; (B) defer FR-D5 to a dedicated follow-on (do NOT fold client+shared-lib feature work into the 039 deploy task); (C) document as a known gap. Orchestrator recommendation: **(B)** — a small dedicated paired task (server restore-DTO projection + the SprkChat `initialAttachments` seam), sequenced after the current D wave; the server projection can share the DI-01 FR-D7 projection work but the client SprkChat seam is its own design.
- **Landing point (captured for the follow-on)**: server — add a minimal `uploadedFiles: {fileId, fileName, contentType, sizeBytes}[]` to the restore payload the History flow actually lands on (confirm `/restore` vs `/tabs`); client — seed SprkChat attachment state via a new `initialAttachments` prop (check `@spaarke/ui-components` SprkChat props first) or a parallel `FilesAttachedIndicator` render for restored files.
- **GitHub issue**: PENDING.

## DI-02 — Un-flushed TipTap compose edits in the OUTGOING session could be lost on a History switch

- **Discovered**: 2026-08-06, task 035 (rich History restore) — escalation trigger evaluated, judged below the STOP bar.
- **Concrete failing behavior**: task 035 now **clears compose tabs on a genuine History switch** (required — preserving them would corrupt the reopened session's tab set and re-block restore). The compose **document is durable** (server-authoritative per ADR-049 + `composeRunPersistence` localStorage, explicit-close-only removal), so the document is never destroyed. BUT if the TipTap→server edit flush is **not** continuous/auto-on-unmount, a mid-edit draft in the *outgoing* session could lose keystrokes entered since the last flush when the user switches History before those edits flush.
- **Why below the STOP bar**: document durability is guaranteed; only un-flushed in-memory deltas since the last flush are at risk — and the same property pre-exists on any tab close (035 did not create the flush cadence, it added one more path that closes a compose tab).
- **Recommended fix / investigation**: confirm the TipTap edit-flush cadence (debounce interval / flush-on-blur / flush-on-unmount). If edits are NOT flushed on unmount, add an explicit flush before `clearAllTabs()` on the History-switch path (or make the compose editor flush-on-unmount). If flush is already continuous/on-blur, close this as a non-issue with a documenting comment.
- **GitHub issue**: PENDING.
