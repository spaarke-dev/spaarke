# SpaarkeAI Assistant Enhancements R2 — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-08-05
> **Source**: [`design.md`](design.md)
> **Predecessor**: `spaarkeai-assistant-enhancements-r1` (grounded dispatcher, structured creation, User Model). R2 builds on R1's shipped dispatch spine + catalog; does not reopen them.

## Executive Summary

Make the SpaarkeAI Assistant **surface-aware**: it sees and can act on the currently-focused Workspace tab (starting with email), proactively offers relevant follow-ons on tab open, and provides a **true history/resume** where reopening a session restores the whole workspace (chat + tabs + open document + saved redline), not just the chat text. Also removes the low-value Notifications banner. Nearly all of it is **read/wiring/reliability work over existing machinery** — the resume data already lives durably in Cosmos; the grounded catalog and PaneEventBus already exist.

## Scope

### In Scope
- **A — Active-tab awareness (focus-stamp):** feed the real focused tab into each chat turn; replace the server's `UpdatedAt` "active" heuristic with the explicit `active_widget_changed` signal.
- **B — Proactive follow-ons:** on tab first-open, a grounded agent turn offers ≤3 context-relevant chips (tag context-relevance in data; AI selects/phrases). Fires once per tab (cached); user can manually re-run.
- **C — Email Assistant-visibility:** implement `getAgentVisibleState()` on the (already-real, already-registered) email widget + add a lean `Email` variant to the visible-state union + declare the `email` context type.
- **D — History robustness & true resume:** route History through the rich `/restore`+`/tabs` path; fix transcript-write reliability, tab no-op/corruption, and the `200`-vs-`404` contract; descriptive editable titles; rename/delete; rehydrate attachment chip; unit-of-work presentation; grouping/search; rename "Promote" → **"Set related record"** (association action); indefinite retention for filed analyses; a "Reanalyze" chip.
- **E — Remove the Notifications banner** from the Assistant pane (preserve the notification spine).

### Out of Scope
- Per-tab sessions / session fragmentation (explicitly rejected — one thread + focus-stamp).
- Full per-widget UI-state serialization ("full auto-resume") — durable-artifact resume only.
- New dispatch pipeline / ranker (reuse R1's spine + catalog).
- New real-time push infrastructure (R1.5 Azure SignalR is a separate project).
- Tab-scoped retrieval for long-session summarization decay (revisit only if it bites).
- Removal of the notification **spine** — E removes only the Assistant **banner** surface.
- Building/wiring the email widget (already real + registered post-r5).
- An explicit "attach email to conversation" affordance for **non-active/background** tabs.
- `EmailStubWidget` reconciliation (the Compose "Open in Email" preview) — **deferred to email-communication-solution-r5** (FR-C5).

### Affected Areas
- `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` — active-tab subscriber + focus-stamp decorate (A); History resume routing (D).
- `src/solutions/SpaarkeAi/src/components/conversation/HistoryOverlay.tsx` — history UX rebuild (D).
- `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx` / `WorkspaceTabManager.ts` — tab restore/clear-on-switch (D); EmailStub reconciliation (C).
- `src/client/shared/Spaarke.AI.Widgets/src/registry/WorkspaceWidgetRegistry.ts` + `types/*`, `events/PaneEventTypes.ts` — context-type + visible-state plumbing (A/B/C).
- `src/client/shared/Spaarke.Communication.Components/.../EmailWorkspaceWidget` (+ `CommunicationsWorkspaceWidget`) — `getAgentVisibleState()` (C).
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` — focus-stamp preference in `BuildWorkspaceStateBlock` (A); `Email` visible-state variant (C).
- `src/server/api/Sprk.Bff.Api/Models/Workspace/WorkspaceTabVisibleState.cs` — `Email` variant (C).
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SessionPersistenceService.cs` / `ChatSessionManager.cs` / `ChatEndpoints.cs` — awaited `messages[0]` write, `404` contract, stored title + rename endpoint, TTL-on-filing (D).
- ADR-039 catalog (`sprk_playbookconsumer` / `sprk_tooldescription` / `sprk_chiptransitions`) — context-type tags + Reanalyze binding (B/D, data).
- `src/solutions/SpaarkeAi/src/components/conversation/useSuggestionCards.tsx` + `SuggestionCard.tsx` (delete) and `ConversationPane.tsx:941-987,2649` (remove hook + render site) — the spine-driven suggestion surface (E). Keep `notificationsBootstrap.ts` (shared client).

## Requirements

### Functional Requirements

**Workstream A — Active-tab awareness (focus-stamp)**
1. **FR-A1**: `ConversationPane` subscribes to `workspace.active_widget_changed` and holds `{ widgetType, contextType, tabId, displayName, compactState }` in a ref. — Acceptance: switching the active Workspace tab updates the ref; verified by a seam test on the subscriber.
2. **FR-A2**: `handleDecorateOutboundBody` (existing `onDecorateOutboundBody` seam) adds an `activeContext` field carrying the focused tab's identity + compact state to the outbound chat body. **No `SprkChat` change.** — Acceptance: an outbound message body includes `activeContext` matching the focused tab; "summarize this" with an email focused resolves to that email.
3. **FR-A3**: Server `BuildWorkspaceStateBlock` prefers the explicit focus-stamp over the `UpdatedAt`-most-recent heuristic when labeling the active tab. — Acceptance: with a focus-stamp present, the "(active)" tab in the agent prompt matches the stamp, not the most-recently-updated tab.
4. **FR-A4** (privacy — ADR-015 exception, path A): the **active** tab is content-visible to the agent (compact-ambient); **background** tabs remain metadata-only. — Acceptance: background tabs emit only deterministic metadata; the active tab emits its compact content shape; documented bound respected.

**Workstream B — Proactive follow-ons**
5. **FR-B1**: Each workspace widget declares a `contextType` from the closed set { `email`, `document`, `compose-doc`, `matter-grid`, `dashboard`, `calendar` }. — Acceptance: every registered widget resolves to exactly one context type (or none).
6. **FR-B2**: Catalog Bindings carry a relevance tag for one or more context types (Dataverse, analyst-editable, no deploy). — Acceptance: a Binding tagged `email` is selectable for an email tab and not surfaced for a `matter-grid` tab.
7. **FR-B3**: On a tab's **first open/load**, run **one** grounded suggestion turn (the same ADR-039 agent turn, filtered by the tab's context type) and **cache the result per `tabId`**. **Do NOT re-fire on switch-back** to an already-suggested tab. — Acceptance: opening tab X fires exactly one suggestion turn; switching away and back fires zero additional turns; a suggestion cache is keyed by `tabId`.
8. **FR-B4**: A manual "refresh suggestions" affordance lets the user re-run the suggestion turn on demand. — Acceptance: clicking it re-fires the grounded turn for the active tab and replaces the cached chips.
9. **FR-B5**: Render ≤3 content-specific, dismissible, non-modal follow-on chips (reuse the existing chip surface). — Acceptance: never more than 3; generic boilerplate chips are not emitted for content-rich tabs; chips are dismissible.
10. **FR-B6**: The proactive selection is observable — a dev-visible trace records the context type, candidate Bindings, and selected chips for a given tab+content. — Acceptance: the trace is emitted and inspectable in dev.

**Workstream C — Email Assistant-visibility**
11. **FR-C1**: Implement `getAgentVisibleState()` on the real email workspace widget (`EmailWorkspaceWidget`; and `CommunicationsWorkspaceWidget` where it can be a tab), returning a compact shape (subject, from, date, thread id, short snippet or current selection). — Acceptance: an open, visible-to-assistant email tab contributes its compact shape to the agent prompt.
12. **FR-C2**: Add a lean **`Email`** variant to the closed union `WorkspaceTabVisibleState` — client `getAgentVisibleState` shape + server `TryDeriveVisibleState` / `FormatVisibleStateFields`. — Acceptance: the union deserializes the Email shape structurally (no fallback to Dashboard/Summary); server emits the Email fields.
13. **FR-C3**: Declare `email` as the email widget's context type (feeds FR-B1). — Acceptance: an email tab resolves to context type `email`.
14. **FR-C4**: When a turn needs the full body/thread, the agent fetches it **on-demand via an `eml-render` tool call** (no explicit consent gate — active-tab-as-consent covers it). — Acceptance: "summarize this email" triggers an `eml-render` fetch; the full body is not injected into every turn.
15. **FR-C5** — **DEFERRED to email-communication-solution-r5** (owner 2026-08-05). `EmailStubWidget` reconciliation (the Compose "Open in Email" preview path) is handled in the email-r5 project, not R2. R2 does not touch the stub; the real email tab does not depend on it.

**Workstream D — History robustness & true resume**
16. **FR-D1**: Route `handleSelectHistorySession` through the rich restore path (the `/restore` + `/tabs` flow that "open an Analysis" already uses), **clearing/remounting the workspace first** so tab restore is not blocked and cannot overwrite the reopened session's stored tabs. — Acceptance: reopening a session restores chat **+ its tabs + document reference**; the stored tab set is not corrupted by the previous session's tabs (regression test for the overwrite hazard).
17. **FR-D2** (reliability, Option A): make the critical Cosmos transcript write **awaited/confirmed** for `messages[0]` (first turn) so the transcript + title seed survive Redis eviction; remaining turns may stay async. — Acceptance: after a session's first turn, the Cosmos `StoredSession` contains `messages[0]` before the request completes; reopening after Redis TTL shows the transcript, not a blank pane.
18. **FR-D3**: `GET …/history` (and session load) returns **`404`** for a genuinely-missing session so the client's stale-session recovery fires. — Acceptance: a missing session yields 404 → "session expired — starting fresh," not a silent blank 200.
19. **FR-D4**: Add a **writable, stored** session `title`; generate a 3–6 word work-descriptive title via a cheap grounded turn at first substantive exchange (fallback: generated → first user message → **never** bare timestamp); add a **rename** endpoint `PATCH /api/ai/chat/sessions/{id}` (title). — Acceptance: new sessions get descriptive titles; a user can rename; rename persists across reloads.
20. **FR-D5**: Re-hydrate the client attachment chip from the server `UploadedFiles` manifest on restore. — Acceptance: reopening a session with an attached file shows the file chip again.
21. **FR-D6**: Row body = **open** (primary); secondary actions in a per-row **overflow (⋮) menu**: Open, Rename, Set related record, Delete. Wire the **existing `DELETE`** endpoint to a delete control. Remove the ambiguous up-arrow. — Acceptance: the up-arrow is gone; delete works from the menu; row click opens.
22. **FR-D7**: Surface the dropped list fields — last-message preview or `conversationSummary`, message count — and **the tabs the session carried**. — Acceptance: history rows show a preview + tab summary ("Email · Compose"), not just a timestamp.
23. **FR-D8**: Group history by Today / Yesterday / This week; add search once the list grows. — Acceptance: rows are time-grouped; search filters by title/preview.
24. **FR-D9** ("Set related record" — was "Promote"): rename the action and move it to the overflow menu. It files an *otherwise-unassociated* analysis via a **prompt**: **(a)** associate to an existing matter/project (sets `regarding`), or **(b)** create/attach a Document to anchor it (`regarding = document`). Under the hood it names + creates the `sprk_analysis` anchor + binds the session (existing `promote` endpoint). — Acceptance: the prompt offers existing-record vs create-document; a filed session appears on the target matter's Analyses tab; an already-associated session does not offer the action (or shows its Analysis instead).
25. **FR-D10** (retention — indefinite for filed analyses): a filed analysis's transcript + tabs + redline are retained **indefinitely**; unfiled sessions expire ~90 days. **Mechanism (owner-directed 2026-08-05):** prefer per-document Cosmos TTL extension on filing; **if per-document TTL is not available, remove the container-level TTL (make all docs permanent) and manage retention with an explicit `retentionDate`/`expiresAt` field + a scheduled cleanup process** — unfiled sessions get a ~90-day `expiresAt`; filing clears it (permanent); a scheduled job purges expired docs. — Acceptance: a filed session is resumable after >90 days; an unfiled session is purged after its `expiresAt`; the cleanup job is idempotent and only deletes past-due, unfiled sessions.
26. **FR-D11**: A **"Reanalyze"** follow-on chip on a `document` context re-runs the analysis on the loaded document (reuses the document's playbook). — Acceptance: the chip appears for a loaded document and re-runs analysis on demand.

**Workstream E — Remove the Notifications banner**
27. **FR-E1**: Remove the **spine-driven proactive-suggestion surface** from the Assistant pane. **Key finding (trace 2026-08-05):** the "You have N new notifications" banner is *not* a standalone widget — it is the collapsed header of the suggestion-card stack (`useSuggestionCards.tsx:335`, rendered at `ConversationPane.tsx:2649` via `suggestionSlot`; data = `GET /api/notifications/pending` → `sprk_notificationoutbox`, the ADR-047 spine). Keeping the cards while dropping only the header would render them unconditionally (worse). So remove the **whole surface**: the render site + hook block (`ConversationPane.tsx:941-987, 2649`) and delete `useSuggestionCards.tsx` + `SuggestionCard.tsx` (+ tests). **Preserve** (do NOT touch): the shared `NotificationsClient` (`notificationsBootstrap.ts` — the Communications widget's badge/toast depends on it), the spine (`sprk_notificationoutbox`, `OutboxService`, `/api/notifications/*`), and the **Daily Briefing widget** — confirmed **decoupled** from the notification entity since 2026-06-30 (R7 Wave 12; now `POST /api/ai/daily-briefing/render`, no `appnotification` read). — Acceptance: banner + suggestion cards gone from the Assistant; Communications badge/toast, Daily Briefing render, and the spine all still work (regression check). Note: `DailyBriefingSuggestionProducer` keeps writing outbox rows that no longer render in the Assistant — harmless; gating the producer is out of scope.

### Non-Functional Requirements
- **NFR-01** (BFF hygiene §10): publish size ≤ **60 MB** compressed (baseline ~49.63 MB incl. PDBs); report per BFF-touching task. No new packages anticipated.
- **NFR-02** (proactive cost): the suggestion turn fires **at most once per tab** (cached by `tabId`), never on switch-back; manual re-run only (FR-B3/B4). No LLM call per tab switch.
- **NFR-03** (send latency): the awaited `messages[0]` Cosmos write (FR-D2) adds bounded latency to the first turn only; subsequent turns remain async. Spec a latency budget at implementation.
- **NFR-04** (ADR-039): no second intent mechanism — proactive selection and title generation are grounded agent turns / cheap labels, not classifiers/rerankers/keyword maps.
- **NFR-05** (ADR-040): no new persistence store and no move back to Dataverse — reuse Cosmos `StoredSession` (store-of-record) + the existing tiering.
- **NFR-06** (privacy/ADR-015): active-tab content visibility is bounded to a compact shape ambiently + full-on-demand; background tabs metadata-only.

## Technical Constraints

### Applicable ADRs
- **ADR-039** (grounded execution / closed catalogs) — B (proactive chips) + D (title generation) stay within the one grounded turn.
- **ADR-015** (deterministic-metadata-only agent context) — A active-tab content visibility (**tension — path A exception**).
- **ADR-040** (session ledger / Cosmos persistence) — D resume + reliability; store-of-record reuse.
- **ADR-042** (memory) — orthogonal to resume; always-available, not session-restored (do not conflate).
- **ADR-024** (regarding field-set) — D "Set related record" uses the polymorphic `regarding`.
- **ADR-047** (notification/action spine) — E removes a surface, keeps the spine.
- **ADR-030** (PaneEventBus) — A/B use `active_widget_changed` (already broadcast).
- **ADR-007** (SpeFileStore) — C `eml-render`, output-document save.
- **ADR-049** (Compose shadow document) — D redline/comments reload (render-follows-store).

### MUST Rules
- ✅ MUST inject active-tab context via the existing `onDecorateOutboundBody` seam — MUST NOT fork `SprkChat`.
- ✅ MUST keep proactive selection + title generation as grounded turns — MUST NOT add a classifier/reranker/keyword map (ADR-039).
- ✅ MUST reuse Cosmos `StoredSession` — MUST NOT add a new persistence store or move transcripts back to Dataverse.
- ✅ MUST reuse the existing dispatch seam and catalog — MUST NOT add a new BFF dispatch endpoint (compose invariant).
- ✅ MUST keep background tabs metadata-only; active tab compact-ambient + full-on-demand (ADR-015 bound).
- ✅ MUST preserve the notification spine when removing the banner (E).
- ✅ MUST fire the proactive suggestion turn at most once per tab (cached); MUST NOT fire per switch-back.
- ✅ MUST render Workstream B's tab-contextual follow-on chips via the **reactive chip surface** (`useConsumerChips` / `sprk_chiptransitions`) — MUST NOT reuse or resurrect the removed **spine-driven suggestion surface** (`useSuggestionCards`, removed by E). B and E touch *different* proactive surfaces: E removes irrelevant server-pushed suggestions; B adds relevant tab-contextual follow-ons.

### Existing Patterns
- Rich resume: the `SessionRestoreManager` / `/restore` path that "open an Analysis" uses (`ThreePaneShell.tsx`, `useSessionRestore.ts`).
- Visible-state: `getAgentVisibleState` / `BuildWorkspaceStateBlock` / `WorkspaceTabVisibleState` (Pillar 9).
- Grounded catalog: `sprk_playbookconsumer` + `sprk_tooldescription` + `sprk_chiptransitions`.
- Redline reload: ADR-040 `GET …/compose-outputs` render-follows-store.

## Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration
```xml
<hot-path-declaration>
  <bff>Y</bff>
  <spaarkeai>Y</spaarkeai>
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```
**Placement justification (BFF):** all BFF work reuses existing seams in `Services/Ai/Chat` (`SprkChatAgentFactory`, `SessionPersistenceService`, `ChatEndpoints`) and `Models/Workspace` — no new services, no new dispatch endpoint, no new packages. Per `.claude/constraints/bff-extensions.md`; ≤60 MB publish-size check applies per BFF-touching task. Register project in [`projects/INDEX.md`](../INDEX.md); `/conflict-check` before `Services/Ai` PRs.

### New Components (§11 three-question gate)

| New component | Existing overlap (grep) | Can extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| `activeContext` field on chat body | none (body is `{message, documentId, attachments}`) | N/A — additive field on existing body via existing decorate seam | "summarize this" resolves against the wrong tab (UpdatedAt heuristic) |
| `Email` variant on `WorkspaceTabVisibleState` | union has Summary/DocumentViewer/Dashboard/Table; no Email | No — email has no honest fit; deserializes as Dashboard (name-only) | Assistant can't see email subject/from/thread; the core user complaint persists |
| Context-type tags on Bindings | `sprk_chiptransitions` (successor edges), `sprk_tooldescription` (intent) exist | Extend the catalog (add a tag column/field), not a new registry | Proactive chips can't be scoped to the active tab; either noisy or absent |
| Stored session `title` + `PATCH …/{id}` rename | only `/context`, `/tabs` PATCH; `Title` read-time-computed | No writable field exists | Can't rename; generated titles have nowhere to persist |
| "Reanalyze" Binding | analysis actions exist | Extend catalog (one Binding + context-type tag) | No one-click re-run on a loaded document |
| Retention cleanup job + `expiresAt` field (**conditional** — only if per-doc TTL unavailable, FR-D10) | Cosmos native TTL (preferred path needs no job) | Prefer native per-doc TTL; job only as fallback | Unfiled sessions never purged (unbounded Cosmos growth) once container TTL is removed |

Everything else — load path, `DELETE`, Promote/Set-related-record endpoint, list/history endpoints, persistence tiers, email widgets — is **reuse / fix-in-place**, not new surface.

## ADR Tensions (per CLAUDE.md §6.5 — MANDATORY)

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| **ADR-015** | agent context is deterministic-metadata-only | A (§4) lets the **active** tab's compact **content** (email subject/from/snippet) reach the agent | **A (project-scoped exception)** | The user deliberately focused the tab — attention is implicit consent; bounded to a compact shape + full-on-demand; background tabs stay metadata-only. Owner-approved 2026-08-05. |
| **ADR-039** | one grounded decider; no second intent mechanism | B adds proactive suggestions; D adds title generation | **C (comply)** | Both are the *same* grounded agent turn (B, filtered by context type) or a cheap generative label (D) — not a classifier/reranker. No second decider introduced. |
| **ADR-040** | Cosmos is store-of-record; 3-tier cascade | D makes `messages[0]` an awaited write + extends TTL on filing | **C (comply)** | Reliability + retention change *within* the existing tiering — widens *when/how long* it persists, not *where*. No new store. |

> All other listed ADRs apply without exception.

## Success Criteria
1. [ ] Asking "summarize this" with an email focused resolves to that email — Verify: focus-stamp present in body; agent prompt "(active)" matches the focused tab.
2. [ ] Opening an email tab shows ≤3 relevant follow-on chips; switching away and back fires **no** additional LLM turn — Verify: suggestion-cache seam test + trace.
3. [ ] The Assistant can state an open email's subject/sender/thread — Verify: `Email` visible-state variant emitted in the agent prompt for a visible email tab.
4. [ ] Reopening a History session restores chat **+ tabs + document + attachment chip**, and (for a saved redline) the redline/comments in Compose — Verify: rich-path restore integration test; manual reopen.
5. [ ] A session's first turn survives Redis eviction (reopen shows transcript, not blank) — Verify: awaited `messages[0]` write test; simulate eviction.
6. [ ] History rows show descriptive titles + preview + tab summary; rename/delete work; the up-arrow is gone — Verify: UI test.
7. [ ] "Set related record" prompts existing-vs-new, files the analysis on the matter's Analyses tab, and the filed session is resumable after >90 days — Verify: association test + TTL-extension check.
8. [ ] The Notifications banner is removed from the Assistant; the spine + Daily-Briefing/suggestion cards still work — Verify: regression check.
9. [ ] BFF publish size ≤60 MB on every BFF-touching task — Verify: `dotnet publish` measurement in task notes.

## Dependencies

### Prerequisites
- **email-communication-solution-r5 — MERGED to master** (confirmed 2026-08-05). `EmailWorkspaceWidget` + `CommunicationsWorkspaceWidget` + `eml-render` available. C is unblocked.
- R1 dispatch spine + catalog (shipped).

### External
- None. All work is in-repo; reuses existing Cosmos/Redis/Dataverse/SPE infrastructure.

## Owner Clarifications

| Topic | Question | Answer | Impact |
|---|---|---|---|
| Email widget (C) | Is email-r5 merged; is C build-or-visibility? | Merged. Widgets already real+registered; `EmailStubWidget` is a narrow Compose preview. | C = Assistant-visibility (`getAgentVisibleState` + Email variant + context type), **not** build/wire. |
| Proactive cost (B) | Fire the suggestion turn on every tab switch? | **No** — don't retrigger on switch; fire once per tab; let the user re-run on demand. | FR-B3 (cache per tabId, no re-fire) + FR-B4 (manual refresh). |
| Transcript reliability (D) | How to fix the fire-and-forget transcript write? | **Option A** — awaited/confirmed Cosmos write for `messages[0]`. | FR-D2. |
| Retention (D) | How to retain filed analyses? | **Indefinitely; Option A** — extend/remove Cosmos TTL on filing. | FR-D10. |
| Consent (A/C) | Explicit consent to share the active email? | **No gate** — active-tab-as-consent; full body on-demand via `eml-render`. | FR-A4, FR-C4. |
| Set related record (D) | Silent or prompted? | **Prompted** — "associate to existing, or create new document." | FR-D9. |
| Resume fidelity (D) | Full per-widget auto-resume? | **No** — durable-artifact resume; transient re-derives via Reanalyze. | FR-D1/D11, §7.4. |
| Phasing | Workstream order? | E → A → B → D → C (accepted). | Task sequencing. |
| Context types (B) | Bless the starting set? | Yes — email/document/compose-doc/matter-grid/dashboard/calendar. | FR-B1. |
| Focus-stamp payload (A) | Compact shape ok? | Yes — small per-widget compact + on-demand. | FR-A2/A4. |

## Assumptions
- **Notifications banner**: CONFIRMED self-contained (`useSuggestionCards.tsx`) — no assumption needed. Interpreting owner intent as removing the **whole** spine-suggestion surface (banner + cards), since keeping cards without the banner is strictly worse (unconditional render). Confirm if only the header (not the cards) was meant.
- **Cosmos TTL mechanics**: prefer per-document TTL override on filing (FR-D10); if unavailable, remove the container TTL and manage retention via an `expiresAt` field + scheduled cleanup job (owner-directed 2026-08-05).
- **`CommunicationsWorkspaceWidget` as a tab**: `getAgentVisibleState()` is applied to whichever email widget can be an active workspace tab; the messaging-r3 registration is reconciled, not duplicated.

## Unresolved Questions
- [x] **Notifications banner + Daily-Briefing coupling** — RESOLVED (trace 2026-08-05): banner = `useSuggestionCards.tsx` (spine `sprk_notificationoutbox`); Daily Briefing **confirmed decoupled** since 2026-06-30 (now `/api/ai/daily-briefing/render`, off `appnotification`). Banner removes cleanly (FR-E1 files) with zero impact on spine / Daily Briefing / Communications widget. No `sprk_notification` entity exists (spine table is `sprk_notificationoutbox`; `appnotification` is MS-native, off the widget path). No longer blocking.
- [x] **Cosmos retention mechanism** — RESOLVED (owner 2026-08-05): prefer per-doc TTL; else remove container TTL + `expiresAt` field + scheduled cleanup (FR-D10). No longer blocking.
- [x] **EmailStub reconciliation** — RESOLVED: deferred to email-communication-solution-r5 (FR-C5). Out of R2 scope.

---
*AI-optimized specification. Original design: design.md*
