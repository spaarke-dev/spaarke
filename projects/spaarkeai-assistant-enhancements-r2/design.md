# Spaarke AI Assistant Enhancements R2 — Design (Working Document)

> **Status**: DRAFT (2026-08-04). Human design document — the input to `/design-to-spec`. Not yet a spec.
> **Predecessor**: [`spaarkeai-assistant-enhancements-r1`](../spaarkeai-assistant-enhancements-r1/design.md) ("Follow-Through" — grounded dispatcher, structured creation, User Model, tool drop-down). R2 builds on R1's shipped dispatch spine + catalog; it does **not** re-open them.
> **Theme**: **Make the Assistant surface-aware.** Close the gap between what the user sees in the Workspace and what the Assistant knows — plus make chat history a usable, resumable "unit of work," and remove low-value UI (the Notifications banner).
> **Authorship note**: sections below marked _(confirm in spec)_ carry an open decision for the owner; the "As-built" claims are from code traces (2026-08-04) cited inline and MUST be re-verified at spec intake.

---

## 1. Product Statement

Users in the SpaarkeAi workspace expect the Assistant to **"see" whatever tab is open** in the Workspace pane — most concretely: open an email, then ask the Assistant to work with it. Today the Assistant cannot: it has no reliable signal for which tab is focused, the rich Email widget is a stub on master, and the content-awareness channel was built but left unconsumed. Separately, **chat history is central to the model but barely functional** (selecting an entry doesn't reliably reload the transcript; titles are bare timestamps; no rename/delete). And the **Notifications banner** in the Assistant consumes prime real estate for low user value.

R2 delivers a **surface-aware, proactive Assistant** operating under a clear pane-purpose model, with a **usable history/resume UX**, and **removes the Notifications banner**.

### 1.1 The pane-purpose model (the paradigm this project commits to)

This is the mental model R2 makes real and teaches the user through behavior:

| Pane | Purpose | User verbs |
|---|---|---|
| **Assistant** (left) | The **instruction + Q&A surface**. Where the user directs the AI and asks questions. | *summarize this · open a document · run a report · draft a response* |
| **Workspace** (right) | The **working surface** — where work product lives and hands-on work happens. | *edit the document · view the generated report/visual · work the matter grid* |

Gray area (accepted): a **summary** is technically work product but is currently produced as an **Assistant action**. That's fine — an Assistant action may *emit* an artifact into the Workspace. The paradigm is "instruct on the left → artifact on the right," not a hard content wall.

**The behavioral commitment:** whenever the user opens/focuses a Workspace tab, the Assistant will (1) become **context- and content-aware** of that tab, and (2) **proactively offer relevant follow-ons**. This generalizes today's single reactive case (Compose tab + file upload) into a widget-agnostic rule, guiding the user into the model and reducing ad-hoc requests.

---

## 2. Core Concepts & Vocabulary

- **Active tab / focused tab** — the Workspace tab the user is currently looking at. The authoritative signal is the `workspace.active_widget_changed` PaneEventBus event (real focus), **not** the server's current `UpdatedAt`-most-recent heuristic.
- **Visible-to-Assistant (Pillar 9)** — the shipped per-tab `visibleToAssistant` contract + server `BuildWorkspaceStateBlock` that injects a workspace-state block into the agent prompt for visible tabs. Content-bearing only for a closed 4-variant union (Summary, DocumentViewer, Dashboard, Table); email has no variant and the widget implements no `getAgentVisibleState()`.
- **Focus-stamp** — R2's key addition: stamp each outbound chat turn with the actually-focused tab's identity/context via the existing `onDecorateOutboundBody` seam, so "this" resolves deterministically and the LLM re-anchors on tab switches.
- **Grounded suggestion / follow-on chip** — a proactive next-action chip selected by the one grounded agent turn (ADR-039) from the catalog, filtered by the active widget's **context type**. Distinct from the **Notifications banner** (see §7) which R2 removes.
- **Session = unit of work** — one persistent Assistant session owns a chat thread **plus its tab set**; reopening a session restores both (§5, §6).
- **Compact-ambient vs full-on-demand** — content-sharing fidelity dial: ambiently include a compact view (e.g. email subject/from/snippet); pull the full body only when an action needs it.

---

## 3. Scope — the five workstreams

| # | Workstream | One-line |
|---|---|---|
| **A** | **Active-tab awareness (focus-stamp)** | Feed the *real* focused tab into each chat turn; replace the `UpdatedAt` "(active)" heuristic. |
| **B** | **Proactive follow-ons on tab open** | On tab focus, the grounded agent offers 2–3 context-relevant chips; generalizes the Compose-only reaction. |
| **C** | **Email widget integration** | Remove `EmailStubWidget`; wire the real `CommunicationsWorkspaceWidget`; implement its `getAgentVisibleState()` (compact-ambient + full-on-demand via `eml-render`). First real consumer of A/B. |
| **D** | **Chat history robustness & UX** | Route History through the **rich resume path** (data already in Cosmos); fix transcript-write reliability + tab-corruption + `404` contract; descriptive editable titles; rename/delete; rehydrate attachments; present session-as-resumable-unit-of-work; grouping/search. |
| **E** | **Remove the Notifications banner** | Remove the "You have N new notifications" surface from the Assistant pane (real-estate + low value). Preserve the underlying spine (§7). |

### 3.1 Why A/B/C converge on one mechanism

A (focus signal), B (proactive suggestions), and C (email as the first content-bearing widget) are **one engine**: `active_widget_changed` → derive the active widget's **context type + compact visible state** → (a) stamp the turn and (b) run a grounded suggestion turn filtered by context type → render chips. Email is simply the first widget to (i) opt into `visibleToAssistant`-derivable state and (ii) declare a context type. Building A/B without a real content-bearing widget (C) would be testing against a stub — hence C is in-scope, not deferred.

---

## 4. Workstream A — Active-tab awareness (the focus-stamp)

**Problem (as-built):** chat messages are **not** tagged with any tab; association is session-level only. The agent's ambient workspace-state block marks the "active" tab by **most-recently-`UpdatedAt`** — the wrong signal (viewing/re-focusing a tab need not bump `UpdatedAt`). So "summarize **this**" resolves against a guess. The correct signal, `workspace.active_widget_changed` (real focus), is already broadcast by `WorkspacePane` but has **no Assistant consumer**.

**Design:**
1. `ConversationPane` subscribes to `workspace.active_widget_changed` (alongside its existing `widget_load` handler) and holds `{ widgetType, contextType, tabId, displayName, compactState }` in a ref.
2. `handleDecorateOutboundBody` (already wired as `SprkChat`'s `onDecorateOutboundBody`) adds an `activeContext` field to the outbound chat body — the single sanctioned injection point; **no `SprkChat` change**.
3. Server prefers this explicit focus signal over the `UpdatedAt` heuristic when labeling the active tab in `BuildWorkspaceStateBlock`.

**Why this also fixes tab-switch continuity (Q3):** with one thread, prior-tab discussion is already in context; the focus-stamp is the explicit re-anchor that turns "hopefully the LLM follows" into reliable behavior. Residual limit: very long sessions get summarized (`conversationSummary`) → fidelity decay; **not** a reason to fragment into per-tab sessions; revisit tab-scoped retrieval only if it bites.

**Privacy rule (owner-approved 2026-08-04): active-tab-as-consent.** The **active** tab is content-visible to the Assistant; **background** tabs stay metadata-only. The user's attention is the implicit consent signal; defensible for privileged legal content. Fidelity = **compact-ambient + full-on-demand** (don't stream whole bodies every turn). ADR-015 tension noted in §9.

---

## 5. Workstream B — Proactive follow-ons on tab open

**Problem:** the Assistant only *reacts* today when a Compose tab opens with an uploaded file. Users get no guidance for any other tab, driving ad-hoc requests.

**Design (grounded orchestration — owner: "try it and see where the edges are"):**
- Each widget declares a **context type** (`email`, `document`, `matter-grid`, `compose-doc`, …) — small, code-side.
- Catalog **Bindings/Actions** (ADR-039, data — analyst-editable, no deploy) are **tagged with the context types they are relevant to**.
- On `active_widget_changed`, run a **grounded** proactive turn: "user is viewing an `email` with *this* compact content; from the catalog capabilities tagged `email`-relevant, select and phrase the 2–3 most useful." Render as follow-on chips on the existing chip surface.
- **Split:** pre-think only the *tagging* (data); the AI **orchestrates selection + phrasing** per real content. New capability = new Binding row; the engine picks it up with zero code change.

**Anti-Clippy discipline (binding for spec):** ≤3 high-confidence, **content-specific**, dismissible, non-modal chips. A generic "Summarize this email" on every message is worse than nothing. Grounded selection is what keeps them specific.

**Observability (owner directive):** "see where the edges are" must be *observable* — a dev-visible trace of what the agent selected for a given tab+content and whether it misfired. Cheap to build in from the start; expensive to retrofit.

**Relationship to E (binding):** B's follow-on chips use the **reactive chip surface** (`useConsumerChips` / `sprk_chiptransitions`) — a *different* surface from the spine-driven `useSuggestionCards` banner+cards that E removes. E removes irrelevant server-pushed suggestions; B adds relevant tab-contextual follow-ons. Coherent, not contradictory — but B MUST NOT reuse/resurrect `useSuggestionCards`.

### 5.1 Context-type taxonomy (proposed starting set)

A **context type** is a coarse label for *what a widget shows* — the join key between "what's open" and "which capabilities are relevant." Deliberately coarse + closed (a routing hint, not a schema). Three moving parts: widget **declares** its `contextType` (code); Bindings are **tagged** with relevant context types (data); the grounded turn **filters** the catalog by the active widget's context type, then selects/phrases ≤3.

| `contextType` | Widget | Example relevant follow-ons |
|---|---|---|
| `email` | `CommunicationsWorkspaceWidget` | Draft reply · Summarize thread · Extract dates/deadlines · Create task from email · File to matter |
| `document` | DocumentViewer / Summary | Summarize · Find similar · Compare · Extract key terms |
| `compose-doc` | Compose | Continue drafting · Insert citation · Review clause |
| `matter-grid` | DataGrid / My Tasks | Filter · Generate report · Bulk update |
| `dashboard` | Daily Briefing / dashboards | Explain a metric · Drill into an item |
| `calendar` | Calendar widget | Summarize the week · Find conflicts · Schedule follow-up |

New widget = pick an existing type if it fits. New capability = tag it in data; the engine picks it up (no code change). This is the "prethink only the tagging; AI orchestrates selection" split (Q2).

---

## 6. Workstream C — Email widget integration

**Problem (CORRECTED 2026-08-05, post-r5-merge):** the email widgets are **already real and registered** — `register-workspace-widgets.ts` maps widgetType **`email` → `EmailWorkspaceWidget`** and **`communications-list` → `CommunicationsWorkspaceWidget`** (rich Pattern D). `EmailStubWidget` is **not** the main email tab — it is a narrow "coming soon" preview used only for one Compose "Open in Email" draft affordance (`WorkspacePane.tsx:1644-1671`, FIX #10b). So the rich Email tab the user sees is a real widget. **The actual gap is that the real email widget implements no `getAgentVisibleState()`** — so the Assistant can't see the open email regardless of the stub. This **shrinks C**: it is an Assistant-visibility change, not a build/wire.

**Design:**
1. **Implement `getAgentVisibleState()` on the real email widget** (`EmailWorkspaceWidget`; and `CommunicationsWorkspaceWidget` if it can be a tab) → a compact shape (subject, from, date, thread id, short snippet or selection). Add a lean **Email** variant to the closed union `WorkspaceTabVisibleState` (client `getAgentVisibleState` + server `TryDeriveVisibleState`/`FormatVisibleStateFields`) — a small, structurally-typed server change (§9 decision 3, resolved: Email variant).
2. Declare context type `email` (for Workstream B tagging).
3. **Housekeeping — reconcile `EmailStubWidget`.** Verify the Compose "Open in Email" preview path; either route it to the real widget or keep the lightweight preview intentionally. _(Not a blocker; confirm at spec intake — the real email tab does NOT depend on this.)_
4. **Full-on-demand body (automatic — NO explicit consent gate).** The active-tab-as-consent rule (§4) already makes the open email visible; the user does **not** click "add this email." Fidelity is a *token-efficiency* mechanism, not a consent step: the **compact** view rides ambiently; when a turn needs the **full body/thread**, the agent fetches it on demand via an `eml-render` **tool call** (the user's request is the trigger). Respects the single-LLM-call attachment policy. An explicit "attach" affordance is only ever relevant for pulling in a *non-active/background* tab — out of scope for R2.

**Extensibility claim:** email is the *template* for every widget. Once A/B/C land, a new content-bearing widget = declare a context type + implement `getAgentVisibleState()` + tag relevant Bindings. No engine change.

---

## 7. Workstream D — Chat history robustness & UX

> **Reframe:** because a session = chat thread **+ its tab set** (§5), history is **"resume this unit of work,"** not "reopen a chat log." Entries should describe the *work* and show *what was open*, not a timestamp.

### 7.0 User synopsis — how you'll interact with this

**The everyday flow (nothing to save):** You work with the Assistant on a matter — chat, upload a file, run an analysis, a redline opens in a Compose tab. You close the browser. **You never "saved" anything — it's automatic.**

**Coming back (true resume):** Next morning you open the Assistant and click the **History** clock icon. Instead of a wall of "Conversation · Aug 4, 13:51 UTC" rows, you see **descriptive titles** ("NDA review – Corteva"), grouped **Today / Yesterday / This week**, each row showing a **short preview** and **what was open** ("Email · Compose"). You click one — and your **whole desk comes back**: the chat transcript, the tabs you had open, the attached-file chip, and the **NDA redline + comments loaded in Compose**, right where you left off. (Today, clicking History reloads only the chat text, if that; this makes it reload the *workspace*.)

**Managing history:** each row has an **overflow (⋮) menu** — *Open* (also the row click), *Rename* (give it your own label), *Set related record* (file it — below), *Delete*. The old confusing up-arrow is gone.

**Filing work that matters ("Set related record"):** most sessions are personal scratch and just live in your History. When a session is worth keeping and sharing, you pick **"Set related record."** A prompt asks: **"Associate this to an existing matter/project — or create a new document for it?"** You pick, say, the *Corteva* matter. Now it's **filed**: it appears on the **Corteva matter's Analyses tab**, is **retained indefinitely**, and **any colleague can open it from the matter and resume the exact same workspace**. (This is the only "save as a shared, tracked thing" — everything else is automatic personal history.)

**Re-running work:** with a document loaded, a **"Reanalyze"** chip lets you re-run the analysis on it — so transient results you didn't keep can be regenerated on demand rather than needing to have been frozen.

**What you'll notice is gone:** the "N new notifications" banner (Workstream E) — reclaimed space, no lost function.

### 7.1 Persistence reality (trace 2026-08-04 — CONFIRMED) — the resume data already exists

The AI work session is **Cosmos-driven as store-of-record**, Redis-cached hot, Dataverse reduced to a thin anchor + work-product role (ADR-040 / decision D-06):

| Artifact | Hot (Redis, 24h) | **Store-of-record (Cosmos)** | Dataverse |
|---|---|---|---|
| Transcript / messages | `session:{s}` mirror | **`sessions` container `StoredSession.Messages`** | `sprk_aichatmessage` write-only, **never read** (dead cold path) |
| Session state (DocumentId, **UploadedFiles**, ActiveDocument) | mirror | **`StoredSession`** | `sprk_aichatsummary` metadata anchor + `sprk_analysis` FK + archive flag only |
| Workspace tabs | `workspace-state:{s}` | **`memory` container + mirrored into `StoredSession.Tabs`** | none |
| Compose ledger (redline/comments, annotations, reference map) | mirror | **`StoredSession` — keyed by sessionId** | none |
| `sprk_analysis` anchor | — | referenced by FK | **Dataverse durable anchor** |
| Analysis work products (memo, working doc, output file) | — | — | **`sprk_analysisoutput` + `sprk_analysis` fields; files in SPE** |
| Memory (ADR-042) | — | **`memory-items` container** | none |

**The key consequence:** *almost everything needed to resume already lives, durably, in one Cosmos `StoredSession` keyed by session id* — transcript, tabs, the document reference, the uploaded-file manifest, and the Compose redline/comments. **Resume is overwhelmingly a READ/WIRING problem, not a storage problem.** We do not build new persistence.

### 7.2 As-built (CONFIRMED) — two restore paths; History reads the thin one

**The rich resume already exists — but only for the wrong door.** There are two restore paths, and they are wildly unequal:
- **Opening an `sprk_analysis`** (Analysis Hub row-click / matter subgrid → `openSpaarkeAi({analysisId})`) uses the **RICH path**: the deep-link `SessionRestoreManager` restores conversation **+ document + workspace tabs + widget-state shells** (`ThreePaneShell.tsx:582-685`, `useSessionRestore.ts`).
- **The History flyout** uses the **POOR path**: `handleSelectHistorySession` → remount → `loadHistory` restores **the chat transcript only** (`ConversationPane.tsx:2249-2255`, `SprkChat.tsx:1020-1065`, `useChatSession.ts:150-189`). It never calls `/restore`, never reads `widgetStates`, never re-hydrates tabs.

So the machinery to resume a full workspace is built and proven (that's what "open an Analysis" does) — **the History path just isn't wired to it.**

**Three real defects (not "missing features"):**
1. **Transcript-write reliability.** The Cosmos transcript write is **fire-and-forget** (`SessionPersistenceService.PersistMessageAsync` → Redis then fire-and-forget Cosmos upsert). When the write doesn't land and Redis (24h) later evicts, `GET /history` returns an **empty transcript → blank pane**. This is the same cause as timestamp titles: `BuildSessionTitle` falls back to `"Conversation · <ts>"` exactly when `messages[0]` is absent from Cosmos (`SessionPersistenceService.cs:811-821`). **"Ugly title" and "loads nothing" are the same sessions, same cause.**
2. **Tab restore no-ops / corrupts on the History path.** The tab-restore effect re-fires but `restoreFromPersistence` no-ops if any widget tab is already mounted (`WorkspaceTabManager.ts:756-757`), and `startNewSession` does **not** clear the previous session's tabs — so the reopened session's tabs are dropped, and the debounced write-through can **overwrite the reopened session's stored tabs with the stale previous set** (`WorkspacePane.tsx:604-615`). Actively destructive, not just lossy.
3. **Missing session returns `200`-empty, not `404`.** `GetHistoryAsync` returns an empty array wrapped in `Results.Ok`; the client's stale-session recovery fires **only on 404** (`useChatSession.ts:161-163`) → silent blank pane instead of "session expired — starting fresh."

**Plus the surface gaps:** the up-arrow is a real but mislabeled **"Set related record"** action (§7.6), **rename does not exist** (only `/context`,`/tabs` PATCH; `Title` is read-time-computed with no writable field), **delete's endpoint exists but has no UI**, and `mapSession` **drops** summary + message count from the list.

### 7.3 Target — durable-artifact resume (route History through the rich path)

1. **Wire History to the rich restore path.** Route `handleSelectHistorySession` through the same `/restore` (+ tabs) flow that "open an Analysis" uses, and **clear/remount the workspace first** so tab restore isn't blocked and can't corrupt the stored set (fixes defect 2). The data is already in Cosmos — this is a read/wiring change.
2. **Make the critical Cosmos writes reliable** (fixes defect 1) — at minimum the first/`messages[0]` write is confirmed-persisted, not fire-and-forget, so transcript + title seed survive Redis eviction.
3. **Return `404` for missing sessions** (fixes defect 3) so recovery fires.
4. **Re-hydrate the client bits that aren't wired** — the **attachment chip** from the existing `UploadedFiles` manifest (server already has it), and the document reference.
5. **Descriptive, editable titles** — add a **writable, stored** title; generate a 3–6 word work-descriptive label via a cheap **grounded** turn (fallback: generated → first user message → **never** bare timestamp); **rename** (✎) edits the stored field.
6. **Disambiguate row actions.** Row body = **open** (primary). Move the (renamed) "Set related record" action into a **per-row overflow menu**; add **delete** (endpoint exists) + **rename** there; optionally **pin**.
7. **Show the unit of work** — surface the dropped fields (preview / `conversationSummary`, message count) and **the tabs the session carried** ("Email · Matter grid").
8. **Time-grouping + search.**

### 7.4 The durable-artifact resume principle (why no full auto-resume is needed)

> **Restore reloads durable artifacts, not ephemeral UI.** Anything worth keeping becomes a durable artifact — the transcript + tabs + document refs + uploaded-file manifest + Compose redline/comments (all already in Cosmos `StoredSession`), and saved work products (Document/output in Dataverse+SPE). Reopening rehydrates from those. Genuinely-transient scratch that was never saved **re-derives** — which is what a **"Reanalyze" card** (a Workstream B follow-on chip on a `document` context type) is for.

This is why the user's full wishlist (reload chat + upload/action markers, reload the uploaded Document, open a saved analysis's redline+comments in Compose, offer Reanalyze) is achievable **without per-widget state serialization** — every item maps to an artifact that already persists. The saved-redline-into-Compose reload **already works** (render-follows-store, ADR-040: `GET …/compose-outputs` re-inserts the ledger output, comments re-projected on Load).

### 7.5 New surface needed (justify in spec, §11.3)

- **A writable, stored session `title`** (currently read-time-only) — for generated titles + rename.
- **Rename endpoint** — `PATCH /api/ai/chat/sessions/{id}` (title) — genuinely new.
- **Title-generation** — a cheap grounded label.
- **Attachment-chip re-hydration** from `UploadedFiles` on restore (client) — reads existing Cosmos data.
- Reuse / fix-in-place (do **not** rebuild): the rich `/restore` + `/tabs` read path, the list/history endpoints, the **existing `DELETE`** (wire UI), the **existing "Set related record"** action (§7.6, relabel), the Cosmos/Redis persistence tiers (make writes reliable, don't replace).

### 7.6 "Set related record" — the former "Promote" (RESOLVED with owner 2026-08-04)

Spaarke has a **two-tier model**: a **loose session** (Cosmos-persisted, resumable, private — what fills History) vs. a named **`sprk_analysis`** (Dataverse anchor, filed on a matter/project, listed on the record, resumable by anyone). The former "Promote to Analysis" (the up-arrow, `POST /api/ai/analysis/promote`) is really an **association** action, and is renamed **"Set related record."**

**What it does (user-facing):** give an *otherwise-unassociated* analysis a home. A chat started **inside a matter** is already filed (`regarding = matter`) — this action is moot for it. A chat started **loose (from home)** has no `regarding` — this is the action that homes it:
- **(a) Associate to an existing record** (matter/project) → sets `regarding`; **or**
- **(b) No parent record** → **create/attach a Document** to anchor it → `regarding = document` (the analysis stands on its own, still listed + resumable).
- It is a **choice/prompt** ("associate to existing, or create new"), not silent. Under the hood it names + creates the durable `sprk_analysis` anchor + binds the session (which is what makes it list on the matter's Analyses tab and resume via the rich path).

**Boundaries R2 must hold:**
- **History rename ≠ Set related record.** Rename edits a *loose-session label* (Cosmos); Set-related-record *files a named Analysis on a record* (Dataverse anchor). Do NOT build a parallel "named session" that duplicates Analysis. A session that has been filed reads differently in History (shows its Analysis / record, not a rename affordance).
- **Set related record ≠ saving the output document.** Filing the *conversation* as an Analysis is distinct from saving the *redline* as a `sprk_document` in SPE (`WorkingDocumentService.SaveToSpeAsync`, `_sprk_outputfileid_value`). The output-document save stays a **separate, auto/one-click** step — the durable-artifact principle (§7.4) wants work products saved automatically regardless of association.
- **Retention (RESOLVED — owner 2026-08-05): filed analyses are retained INDEFINITELY.** A loose (unfiled) session's Cosmos state is 90-day; the moment it is filed (Set related record → `sprk_analysis`), it becomes permanent **including its transcript** — a filed analysis must never expire. Spec must define the mechanism: on filing, **pin/extend the session's Cosmos retention (or persist the transcript to a durable store)** so the whole resumable workspace (transcript + tabs + redline) survives indefinitely alongside the permanent Dataverse anchor + SPE work products. Unfiled scratch sessions keep the 90-day default.

---

## 8. Workstream E — Remove the Notifications banner

**Problem:** the "You have N new notifications" banner at the top of the Assistant pane consumes prime vertical real estate for low user value (user-reported).

**As-built (trace 2026-08-05):** the banner is **not** a standalone widget — it's the collapsed header of the **spine-driven suggestion-card stack** (`useSuggestionCards.tsx:335`, rendered at `ConversationPane.tsx:2649` via `suggestionSlot`; data = `GET /api/notifications/pending` → `sprk_notificationoutbox`, ADR-047). The Communications widget's badge/toast share the same `NotificationsClient` (a *different* `kind`). **Daily Briefing is confirmed decoupled** from the notification entity since 2026-06-30 (R7 Wave 12 — now `POST /api/ai/daily-briefing/render`, off `appnotification`). No `sprk_notification` entity exists.

**Design:**
- Remove the **whole** spine-suggestion surface (banner + cards) — keeping cards without the header renders them unconditionally (worse). Delete `useSuggestionCards.tsx` + `SuggestionCard.tsx` (+ tests) and the hook+render site (`ConversationPane.tsx:941-987, 2649`).
- **Preserve** (do NOT touch): the shared `NotificationsClient` (`notificationsBootstrap.ts` — Communications widget depends on it), the spine (`sprk_notificationoutbox` / `OutboxService` / `/api/notifications/*`), and the Daily Briefing widget (already decoupled). `DailyBriefingSuggestionProducer` keeps emitting rows that simply no longer render in the Assistant — harmless; gating it is out of scope.
- **Distinct from B (binding):** B's tab-contextual follow-on chips use the **reactive chip surface** (`useConsumerChips` / `sprk_chiptransitions`), a *different* surface from the removed `useSuggestionCards`. E removes irrelevant server-pushed suggestions; B adds relevant tab-contextual follow-ons. B MUST NOT resurrect the removed surface.

> **Owner interpretation (confirm):** removing the **whole** surface (banner + cards), not just the header. Keeping cards without the banner is strictly worse, so full removal is the reading — flag if only the header was meant.

---

## 9. ADR-Level Decisions & Tensions (decide before task decomposition)

1. **ADR-039 (grounded execution / closed catalogs) — proactive suggestions + title generation must stay grounded.** The proactive follow-on selection (B) is the **same one grounded agent turn** filtered by context type — **not** a second intent mechanism (no classifier/keyword map/reranker). Title generation (D) is a cheap generative label, not a router. **Decision:** ratify as project-scoped design constraints (path A). Confirm no scoring/ranking stage is introduced.
2. **ADR-015 (deterministic-metadata-only agent context) vs. active-tab content visibility.** The active-tab-as-consent rule (§4) intentionally lets **content** (compact email view) reach the agent for the *active* tab — a deviation from the strict metadata-only posture. **Decision:** project-scoped exception (path A) — active tab is content-visible (compact-ambient + full-on-demand); background tabs remain metadata-only. Document rationale + the compact/on-demand bound in spec.
3. **Closed-union extension for Email (C).** Map email onto the existing **Summary** variant vs. add a lean **Email** variant to `WorkspaceTabVisibleState`. Small structural change either way; decide in spec.
4. **Notifications banner removal scope (E).** Confirm the banner component and that removal preserves the spine + Daily-Briefing/suggestion-card consumers.
5. **History title-generation trigger & cost (D).** When to generate (first substantive turn), model tier, and fallback order (generated → first user message → timestamp).
6. **ADR-040 (session ledger) / Cosmos persistence — resume reuses the store-of-record; the fix is write-reliability + reading the rich record, NOT a new store.** The transcript, tabs, ledger, and uploaded-file manifest already persist durably in Cosmos `StoredSession` (ADR-040 / D-06); Dataverse is a thin anchor. Workstream D adds **no new persistence store** — it (a) routes History through the already-existing rich `/restore` read and (b) makes the `messages[0]` Cosmos write reliable (currently fire-and-forget). **Decision:** ratify as an in-scope reliability + wiring fix within the existing tiering; confirm no new store, no move back to Dataverse.

---

## 10. Non-goals (R2)

- No per-tab sessions / session fragmentation (§5 — explicitly rejected; one thread + focus-stamp).
- No new dispatch pipeline or ranker (ADR-039 — reuse R1's shipped spine + catalog).
- No new real-time push infrastructure (R1.5 Azure SignalR is separate).
- No tab-scoped retrieval for long-session summarization decay (revisit only if it bites).
- No removal of the notification **spine** — only the Assistant **banner** surface (§8).

---

## 11. Architecture Placement & Governance (stubs — complete during spec)

### 11.1 Hot-Path Declaration (per root CLAUDE.md §10 / bff-extensions §G)

```xml
<hot-path-declaration>
  <bff-api>YES — Services/Ai/Chat: prefer explicit focus-stamp over the UpdatedAt "(active)" heuristic in SprkChatAgentFactory.BuildWorkspaceStateBlock (A); Email visible-state derivation in TryDeriveVisibleState/FormatVisibleStateFields + possible new WorkspaceTabVisibleState.Email variant (C); grounded proactive-suggestion turn tagged by widget context type (B, reuses dispatch/catalog — NO new dispatch endpoint); route History through the rich `/restore`+`/tabs` read path (data already in Cosmos `StoredSession`) + make critical Cosmos transcript writes reliable (not fire-and-forget for `messages[0]`) + `404`-on-missing-session contract + writable stored title + rename endpoint (D; delete + set-related-record/promote endpoints already exist). Reuses existing dispatch + Cosmos session/workspace-state seams. Publish-size check required.</bff-api>
  <spaarke-ai>YES — ConversationPane active_widget_changed subscriber + onDecorateOutboundBody focus-stamp (A); proactive follow-on chip subscriber/render (B); CommunicationsWorkspaceWidget registry wiring + EmailStubWidget deletion (C); History menu UX rebuild — load fix, titles, rename/delete, unit-of-work presentation, grouping/search (D); Notifications banner removal (E)</spaarke-ai>
  <ci-workflows>NO</ci-workflows>
  <skill-directives>NO</skill-directives>
  <root-CLAUDE-md>NO</root-CLAUDE-md>
</hot-path-declaration>
```

> **Registry obligation:** register this project in [`projects/INDEX.md`](../INDEX.md) (the §G/root-§17 active-project registry consumed by `/conflict-check`). Coordinate `Services/Ai` PRs via `/conflict-check` — likely concurrent with email-communication-solution-r5 (the email widget source) and any Assistant-touching worktrees.

### 11.2 Placement Justification (per root CLAUDE.md §10)

- **Focus-stamp reader** — the active-tab preference lives where the workspace-state block is already built (`SprkChatAgentFactory`, `Services/Ai/Chat`); no new service. Client side rides the existing `onDecorateOutboundBody` seam; no new BFF endpoint.
- **Proactive-suggestion turn (B)** — reuses the shipped dispatch/grounding path; the only placement question is where the context-type tag join + chip projection compose (client vs `Services/Ai`). **No new dispatch endpoint** (compose-r2 invariant).
- **Email visible-state (C)** — reuses `WorkspaceTabVisibleState`; full-body pull reuses r5 `eml-render` + chat-attachment path. No new content channel.
- **History endpoints (D)** — the defect is **persistence durability + a `200`-vs-`404` contract**, not wiring (load path already works). Fix message durability in place; add a writable title + rename endpoint. Delete + promote endpoints already exist (wire/relabel UI only). No parallel store.
- **Publish-size impact** — measure per §10-bullet-4 (≤60 MB ceiling; baseline ~49.63 MB incl. PDBs). Expected delta small (no new packages anticipated).

### 11.3 Component Justification (per root CLAUDE.md §11 — default to reuse)

Reuse-first (verify with grep before claiming "new"):
- **PaneEventBus `active_widget_changed`** (`@spaarke/ai-widgets`, `PaneEventTypes.ts`) — already broadcast by `WorkspacePane`; A just adds a consumer. Do not build a new signal.
- **`onDecorateOutboundBody` seam** (`SprkChat`) — the sanctioned outbound-context injection point; do not fork SprkChat.
- **Pillar 9 visible-state** (`getAgentVisibleState` / `BuildWorkspaceStateBlock` / `WorkspaceTabVisibleState`) — C extends it (one widget + maybe one variant); do not build a parallel content channel.
- **ADR-039 catalog (Bindings/Actions, `sprk_tooldescription`, `sprk_chiptransitions`)** — B tags relevance in data; do not build a parallel capability/suggestion registry.
- **Dispatch seam** (`POST /api/ai/chat/sessions/{id}/dispatch`) — no new dispatch endpoint.
- **`CommunicationsWorkspaceWidget`** (`Spaarke.Communication.Components`) + **`eml-render`** endpoint — reuse the r5 build; do not re-implement an email widget.
- **Chat-session + history endpoints** — reuse the list/history endpoints and the **existing `DELETE`** (wire UI) and **Promote-to-Analysis** (relabel) actions; do not build a new history store.
- **Persistence tiers (Redis→Cosmos→Dataverse)** — fix message durability in place; do not replace.
- **Notification spine** (ADR-047) — E removes a surface, keeps the spine.

Genuinely-new surface (needs the three-question justification in spec): the **focus-stamp `activeContext` field** on the chat body, a possible **`Email` variant** on the visible-state union, **context-type tags** on Bindings (data column), a **writable stored session title** + **rename endpoint** (`PATCH …/{id}` title — only `/context`+`/tabs` exist today), and **title-generation**. Everything else — load path, delete, promote, list/history endpoints, persistence tiers — is reuse/fix-in-place.

---

## 12. Relationship to Adjacent Projects

| Project | Relationship |
|---|---|
| **assistant-enhancements-r1** ("Follow-Through") | Direct predecessor. R2 consumes R1's grounded dispatcher, catalog (Bindings/`sprk_chiptransitions`), User Model, and tool drop-down. R2 does **not** reopen the dispatch spine or resolver. |
| **email-communication-solution-r5** | Source of the real `CommunicationsWorkspaceWidget` + `eml-render` endpoint (Workstream C). **Sequencing dependency**: C needs that widget in master (or as a dependency). Coordinate via `/conflict-check`. |
| **ai-advanced-capabilities-analysis-hub-r1** | Source of the **two-tier session model** + "Promote to Analysis" (`sprk_analysis`, `POST /api/ai/analysis/promote`). Workstream D must respect the loose-session ↔ named-Analysis boundary (§7.4) — do not duplicate Analysis with a parallel "named session." |
| **spaarke-notification-spine (ADR-047)** | Workstream E removes the Assistant *banner* surface but must **not** regress the spine or its other consumers (Daily Briefing, suggestion cards). |
| **spaarkeai-compose-r2/r4/r5** | Precedent for the one reactive case R2 generalizes (Compose tab + file → Assistant reaction). Reinforces the PaneEventBus + dispatch invariants R2 relies on; not a dependency. |

---

## 13. Open Decisions for the Owner (resolve at/ before spec intake)

1. **Notifications banner (E) — RESOLVED (owner 2026-08-04):** **remove** from the Assistant pane; **keep the spine.**
2. **Email visible-state (C) — RESOLVED (owner 2026-08-04):** add a lean **Email** variant to `WorkspaceTabVisibleState` (honest sender/subject/thread fields).
3. **History depth (D) — RESOLVED (owner 2026-08-04): FULL redesign.** Durability fix + `404` fix + generated titles + writable-title rename + delete UI + unit-of-work presentation (tabs/preview/count) + grouping/search + Promote disambiguation.
4. **"Promote" → "Set related record" (D) — RESOLVED (owner 2026-08-04):** the up-arrow is really an **association** action, renamed **"Set related record"** and moved to a **per-row overflow menu** (row-body = open). It files an *otherwise-unassociated* analysis: **(a)** associate to an existing matter/project, or **(b)** create/attach a Document to anchor it. Boundaries (§7.6): rename ≠ file-on-record; file-on-record ≠ save-output-document. Do NOT duplicate Analysis with a parallel "named session."
5. **Full-body email content (C) — RESOLVED:** **no explicit consent gate.** Active-tab-as-consent covers visibility; the agent fetches the full body **on-demand via an `eml-render` tool call** when a turn needs it (§6.4). "Compact vs full" is a token-efficiency mechanism, not a consent UX. (An explicit attach affordance would only apply to non-active tabs — out of scope.)
6. **Resume fidelity (D) — RESOLVED (owner 2026-08-04): durable-artifact resume, NOT full per-widget auto-resume.** Route History through the rich Cosmos-backed `/restore` path; reload durable artifacts (transcript, tabs, doc refs, uploaded files, saved redline); transient scratch re-derives via a **"Reanalyze" chip** (§7.4). No per-widget state serialization.
7. **Context-type taxonomy (B):** the initial closed set of widget context types to tag against. _(Proposed starting set in §5.1.)_

---

## 14. Suggested Next Step

`/design-to-spec` on this document once §13 decisions are resolved and §7.1 root-cause is finalized from the code trace. Then `/project-pipeline` → `/task-create`. FULL rigor throughout (BFF hot-path + `.cs`/`.tsx` + dispatch-adjacent).
