# Design — spaarkeai-assistant-enhancements-r3

> **The Assistant ⇄ Workspace Interaction Contract**
> **Status**: Draft for alignment · **Created**: 2026-08-07 · **Predecessor**: spaarkeai-assistant-enhancements-r2 (shipped surface-awareness + true-resume; UAT surfaced the gaps this project addresses)
> **Companion artifact**: the reviewable one-pager rendered for alignment (same content, visual form).

---

## 1. Guiding principle

**The Assistant gives the overview and answers by querying the source; the Workspace shows the detail.** The Assistant is *aware* of tabs so it can **answer, open, or direct** — it does **not** read data out of a tab (Compose is the one interactive exception). Every fact the Assistant states comes from a **tool that queries the authoritative source**, never from a snapshot of what a widget rendered.

Corollary (the core of R3): **Conversational Capability Parity** — every workspace widget has a matching Assistant tool set, mounted only when its tab is open (tool economy). No widget ships without its Assistant contract.

---

## 2. Why now — evidence from R2 UAT

R2 made the Assistant *aware* of tabs (the `visibleToAssistant` active-tab-as-consent fix shipped). But awareness without capability parity produced a consistent failure shape:

| User asked | What happened | Root cause |
|---|---|---|
| "how many overdue tasks do I have" | Hand-wrote `GETDATE()` (unsupported by Dataverse), then asked the user to confirm today's date | No task tool; only a generic SQL-subset tool; no injected "today" |
| "do you see the daily briefing tab?" | Answered "yes" but opened a **second** Daily Briefing tab | No de-dup on `widget_load`; layout tab is invisible to the agent |
| "show me the narrated summary" | "No notifications today" while the briefing showed 12 updates / 11 overdue | The prompt had the tab **name**, not its **data** — and no data tool |

**The fix is not "put more data in the prompt." It is "give each surface a tool the Assistant can call."**

---

## 3. How the Workspace works today (ground truth)

Widgets **self-register** in the `WorkspaceWidgetRegistry`, then mount as tabs when a `widget_load` event fires on the **PaneEventBus** → `WorkspacePane` → `WorkspaceTabManager.addTab(...)`. Registration is spread across **four sites**:
- `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-workspace-widgets.ts` (24 registrations)
- `register-document-viewer-widget.ts`, `register-search-criteria-result-widget.ts`, `register-structured-output-stream-widget.ts` (one each)
- `src/solutions/SpaarkeAi/src/components/workspace/registerComposeWidget.ts` (Compose — lives in SpaarkeAi to avoid a package cycle)

**Assessment**: a **good modular pattern with one gap to close** — decentralized self-registration keeps the code modular; the only risk *for this project* is that a widget could register without its Assistant contract. **Fix**: make the contract fields (context-type · parity tools · interaction pattern) **required** in the registration metadata, so the registry enforces parity wherever a widget is declared.

### Which surfaces actually mount as tabs (R2 reconciliation)
Email and Document-viewer **do** mount as their own tabs (correcting the "Compose-only" assumption). **Decision (owner, 2026-08-07): keep standalone email + document tabs.** Three document-ish surfaces coexist: read-only `document-viewer`, editable `compose`, and `email`.

| Surface | Own tab | Reusable data layer |
|---|---|---|
| Grids — Documents/Matters/Projects/Invoices/Work-Assignments/My-Tasks/Messages/Analysis | yes | `DataGrid` + `sprk_gridconfiguration` (`DataGrid.tsx:649`, `useLazyLoad.ts:169`) — client Xrm |
| Daily Briefing | layout | `BriefingService.cs:128` + `/api/ai/daily-briefing/render` — BFF |
| Calendar | layout | `CalendarWorkspaceWidget` — client Xrm |
| Metrics dashboards | yes | `MetricsDashboardWidget` FetchXML catalog — client Xrm |
| Email | yes | `EmailWorkspace` — client Xrm + BFF (eml-render / draft) |
| Document viewer | yes | `GET /api/documents/{id}/preview-url` — BFF |
| Compose | yes | `/api/compose/*` — BFF (server-authoritative, ADR-049) |

---

## 4. The contract — seven elements (available vs build)

| Element | Available (reuse) | Build |
|---|---|---|
| **AWARENESS** (knows what's open + active) | `BuildWorkspaceStateBlock:1469`; active-tab-as-consent shipped; focus-stamp threading | Dashboards/Calendar invisible (layout tabs have no `kind` → derive `null`; `visibleToAssistant` not persisted). Add a layout/identity variant; **trim prompt to `{ type, label, active }` per tab + an active-item handle `{ id, type, label }` for the active/selected item** (id only, never content — see §5.5) |
| **PARITY** (widget ↔ tool set) | Generic Dataverse tools; DataGrid query stack; `BriefingService`; `IBriefingAi`; `EmailDraftToolHandler`; text/analysis handlers | Author per-widget tool sets (read + actions). Reuse the query **definition** executed server-side over OBO. Add widget-type → context-type map |
| **ECONOMY** (only open tabs' tools) | ADR-039 `PreFilter` (`AgentToolProjection.cs:101`); `Binding.ContextTypeTags` (FR-B2); seam `SprkChatAgentFactory.cs:826–869` | `ContextTypeTags` is client-only today. Add `OpenTabContextTypes` to the filter context + one predicate; hoist `tabs` |
| **INTERACTION** (respond/direct/hybrid) | dispatch + surface-launch plumbing | Declare the respond/direct/hybrid pattern **as a registration-contract field**; derive follow-ons deterministically from mounted parity tools + that field (not prose in the system prompt) |
| **OPEN/DIRECT** (never duplicate) | surface-launch registry; Compose + startup-default de-dup by key/layoutId | De-dup guard on the generic `widget_load` path (`WorkspacePane.tsx:2004` → `setActiveTab`) |
| **FOLLOW-ONS** (real next steps) | Phase-B chip surface + grounded `/suggest` + `ContextTypeTags` pre-filter | Derive follow-ons from mounted parity tools + interaction pattern |
| **NAMING** (by content) | `displayName` persists; `document-viewer` titles by filename; Compose hoists `widgetData.filename` | Compose tab hard-coded `"Compose"` → truncated 6–10 char label + tooltip |

---

## 5. The data-access layer (behind the parity tools)

Parity tools are the Assistant's **interface** to a widget's domain. Behind them are **three complementary data lanes, all already built** — the design principle is *one mechanism per data shape, no duplication*:

### Lane 1 — Structured Dataverse records (tasks, matters, grids, calendar)
- **Mechanism**: a native, OBO-secured, **Dataverse-MCP-*shaped*** toolset in the BFF — `dataverse.read_query` (SQL→OData via `DataverseSqlQueryTranslator`, `DataverseUserClient`), `dataverse.search_data` (Dataverse Search API), `dataverse.describe`. **This is NOT an external MCP server** — it is modeled on the Dataverse MCP GA tool shape but implemented natively so it respects the user's Dataverse security + emits record-id citations.
- **Why the generic tool fails today**: `read_query` deliberately rejects `GETDATE()` / `COUNT` / aggregates / JOIN — it pushes query authoring onto the model (which got the date function wrong).
- **Parity fix**: a **domain-shaped tool per grid** that encapsulates the query (e.g. tasks tool computes "overdue = dueDate < today" server-side with today injected). It reuses the widget's **saved-query / FetchXML / `configId`** — the reusable asset — executed server-side over OBO, returning exactly what the grid shows without model-authored SQL. (Vocab note: widgets query with FetchXML/saved-queries client-side; parity tools reconcile by reusing the query *definition* server-side.)

### Lane 2 — Unstructured / semantic document content
- **Mechanism**: the **RAG index** (Azure AI Search vector) via `IRagService` / `DocumentSearchHandler` / `KnowledgeRetrievalHandler` / `DocumentContextService`, plus on-demand document body (FR-C4 pattern).
- **Backs**: document parity actions — summarize, "what does it say", find-similar, draft-memo.

### Lane 3 — Composed domain services
- **Mechanism**: purpose-built services that already encapsulate the right queries — `BriefingService` (portfolio counts + narrative), the membership resolver ("records I'm on").
- **Backs**: Daily Briefing, "records I'm on" scoping. A parity tool wraps the service directly — no reinvention.

### Parity tool sets by surface (read + act — owner decision Q1)
| Surface | Read (lane) | Actions |
|---|---|---|
| Tasks / grids | Dataverse tool wrapping the grid query (lane 1) | create/update (write handlers) |
| Daily Briefing | `BriefingService` (lane 3) | direct-to-tab; refresh |
| Documents | RAG + on-demand body (lane 2) | summarize · draft response · draft memo |
| Email | email record (lane 1) + eml body (SPE) | summarize · draft reply (`EmailDraftToolHandler`) |
| Calendar | Dataverse events query (lane 1) | direct-to-tab |
| Matters | Dataverse tool (lane 1) | summarize · open |

---

## 5.5 Orchestration model — how the Assistant works with a widget (THE template)

**Refined principle (supersedes the strict "identity-only" line):** the Assistant gets a **handle to the active/selected item** (an id), **never its content**. The widget publishes *which* item is active; a tool fetches that item's data itself, by id. **Awareness = identity + active-item handle.**

**The pattern — generalized from the SHIPPED Compose active-document flow:**
1. **Widget publishes the active item on selection.** When the user selects/opens an item in a widget, the widget registers it as the active item — a handle `{ id, type, label }` — through the active-item conduit. (Compose precedent: `composeActionBridge` → `registerActiveDocument` → `POST /api/compose/active-document` → client `activeSourceDocRef` in ConversationPane.tsx:389/424.) No user "invoke" step; **selection is the trigger.**
2. **Assistant auto-presents follow-on cards** for the active item, from the widget's declared action set (the proactive-card surface). No typing required. (Compose already does this for documents — ConversationPane.tsx:164-167.) This is the **reactive/local-selection** card surface (the client reacts to active-item selection) — distinct from the ADR-047 **proactive** notification spine (server-initiated push). Do not merge them. Proactive cards obey `docs/standards/ASSISTANT-UI-ELEMENT-CRITERIA.md` — collapse stacks behind one disclosure header; cards are for persistent act-on items, chips for throwaway turn-follow-ons.
3. **Click a card → parity tool** loads the item **by id** from the source of record (never from the tab) and acts.
4. **Output lands on the native surface** — the widget's own editor/composer, a new Compose tab, or a chat answer.

**Worked example — EMAIL (the canonical template instance):**
- User selects an email → the Email widget publishes `{ communicationId, emlDocumentId, subject }` as the active item.
- The Assistant shows follow-on cards: **Reply · Reply All · Forward · Summarize the thread**.
- Click **Reply/Reply All/Forward** → `draft_reply(communicationId, mode)` loads the email by id (`sprk_communication` for addressing; the `.eml` for full thread), **auto-generates the reply body** (the proactive-AI decision, owner 2026-08-10), then calls the EXISTING `useEmailComposeActions.openComposer(mode, communicationId)` with a new `bodyOverride` → the native composer (`SendEmailDialog`) opens **pre-filled** (recipients + `Re:`/`Fwd:` subject already handled by `fetchCommunicationPrefill`).
  - **Thread-preservation invariant (BINDING — owner 2026-08-10).** `bodyOverride` overrides **only the authored-message region**, NEVER the whole body. The composer still derives the quoted previous thread from the source record (`deriveReplyState`/`deriveForwardState` → `state.quotedThread`) and appends it below the draft, so the composer opens as **[AI draft] + [separator] + [quoted thread]**. This reuses the SAME `quotedThread` machinery the in-dialog "draft with AI" sparkle already uses (`EmailComposer.tsx` `runAiDraft` re-append, owner UAT 2026-08-03 R5 items 1/2) — the new `bodyOverride` path MUST reach parity with it. A `bodyOverride` that replaces the entire body (dropping the quoted thread) is a defect, not the contract.
- Click **Summarize the thread** → loads the `.eml` like a file (`eml-render`/document-context) and answers in chat.

**Two representations of an email:** the **`sprk_communication` record** is the working surface (From/To/Subject/Body/Related-to/Attachments/Confirmed — Spaarke's tracking fields; this is what the reading pane + reply-quote render). The **`.eml`** (`emlDocumentId`) is the archived original message, loaded like any file via `eml-render`/document-context when full-thread fidelity is needed.

**Two tool kinds per widget (what "capability parity" means concretely):**
| Kind | Trigger | Target comes from | Output |
|---|---|---|---|
| **Overview / query** | a chat question | the tool's own query (no target needed) | chat answer / direct-to-tab |
| **Per-item action** | a follow-on card (on the active item) | the **widget** (active-item handle) | native surface (composer / Compose / chat answer) |

**Active-item lifecycle (generalized from the Compose single-document precedent — do not invent new mechanics):**
- **Single-active-item invariant** — there is exactly one `{ id, type, label }` active at a time. On a grid **multi-select**, no single active item is set and the per-item cards suppress (an overview/query tool still answers over the selection); a single-row selection sets the active item. (One rule, chosen: multi-select → cards suppress, not "last row wins.")
- **Clear-on-deselect / tab-switch** — the handle clears when the item is deselected or its tab loses focus, so a card can never fire (e.g. `draft_reply`) against an id the user has navigated away from. The active-item handle is bound to the focused tab's current selection, mirroring the Compose active-document clearing behavior.

**Registration contract:** each widget declares at registration its context-type + its overview tools + its per-item action cards + where each lands. The registry enforces it — no widget ships without its Assistant contract — regardless of which of the four registration sites declares it.

**Reuse vs. new:** *Reuse* — the active-document conduit (generalize to "active item"), the proactive-card surface, the email composer + recipient/subject prefill + **the existing `quotedThread` re-append machinery** (`deriveReplyState`/`deriveForwardState` + `runAiDraft`), `eml-render`. *New* — the email parity tools (`draft_reply`/`draft_forward`, `summarize_thread`); the 4 email cards keyed to an active email; one `bodyOverride` parameter on `openComposer` (**composes** the AI draft above the reducer-derived quoted thread — it does NOT replace the body; see the thread-preservation invariant above). *Already ships (verified 2026-08-10)* — the email widget already publishes its selection via `onVisibleEmailChange` (`EmailWorkspace.tsx:199`; consumed today into tab `widgetData`); R3's small delta is **redirecting that emit to the active-item conduit as an id handle** (not the current content snapshot), not building selection.

---

## 6. Key decisions (owner-confirmed 2026-08-07)

1. **Keep standalone email + document tabs** (not Compose-only). Each gets a parity tool set with read + actions.
2. **Awareness = identity + active-item HANDLE (not content).** The prompt carries `{ type, label, active }` per tab PLUS a handle to the active/selected item (an id — e.g. `communicationId`/`documentId`), so the Assistant can offer per-item cards. It carries **no item content**; every fact/action is a tool that fetches by id. This supersedes the earlier strict "identity-only" phrasing (owner discussion 2026-08-07, the Compose-precedent alignment) and lightly walks back FR-C1's ambient email *content* (email tab stays aware via its handle; content via tool). Rationale: single source of truth, no stale-snapshot hallucination, leaner prompt, scales. See §5.5.
3. **Phasing**: Phase 0 (quick wins) ships on the R2 branch now; Phases 1–4 (the contract) are R3.
4. **Registration contract**: Assistant-contract fields become required registration metadata.

---

## 7. Phasing

- **Phase 0 — quick wins (R2 branch, now, independent of the contract)**: inject today's date into the agent context; de-dup guard on `widget_load` (kills duplicate tabs); SignalR first-mount resilience (the 401 hang); Compose tab naming; chat-pane scroll-to-top on send + thin scrollbar; harden the date-query tool.
- **Phase 1 — Awareness (identity + active-item handle)**: dashboards/layouts visible via a light identity variant; persist `visibleToAssistant`; trim the prompt block to `{ type, label, active }` per tab; publish and thread the active-item handle `{ id, type, label }` (the generalized active-item conduit from §5.5 — id only, never content).
- **Phase 2 — Capability parity**: author per-widget tool sets (read + actions) reusing existing query definitions/services; widget-type → context-type map. *(Where "how many overdue tasks" starts working.)*
- **Phase 3 — Tool economy**: wire `ContextTypeTags` into the server pre-filter; mount only open tabs' tools.
- **Phase 4 — Interaction patterns + accurate follow-ons**: surface the per-widget respond/direct/hybrid pattern **from the registration contract**; derive follow-on chips from mounted tools + pattern.
- **Cross-cutting — Registration contract**: required Assistant-contract fields; threads through 1–4.

---

## 8. Acceptance test (definition of done)

**Overview / query DoD** (exercises the overview tool kind):

> **"how many overdue tasks do I have?"** → *"You have 6 overdue tasks. I've opened your Tasks tab showing them."*

No query error, no asking for the date, no duplicate tab.

**Per-item action DoD** (exercises the per-item action tool kind — §5.5's whole contribution):

> Select an email in the Email tab → a **Reply** card appears (no typing) → click → `SendEmailDialog` opens pre-filled with recipients + `Re:` subject + auto-drafted body **AND the quoted previous thread preserved below the draft** (thread-preservation invariant, §5.5 — a whole-body-replacing draft that drops the thread fails this DoD).

Two DoDs — one per tool kind in the §5.5 table — prove the contract's breadth. When both work end-to-end the contract is real.

---

## 9. Hot-Path Declaration (§10 binding)

```xml
<hot-path-declaration>
  <bff>Y</bff>                <!-- tool projection, parity tools, Dataverse/RAG handlers, ContextTypeTags pre-filter -->
  <spaarkeai>Y</spaarkeai>    <!-- ConversationPane, WorkspacePane, WorkspaceTabManager, registration, chat UI -->
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

## 10. Placement Justification (§10)

New BFF surface (parity tool handlers / capability Bindings) extends the **existing** two-catalog projection (ADR-039) — no new dispatch mechanism, no new store. Parity tools reuse existing data services (`read_query`/`search_data`, `IRagService`, `BriefingService`) rather than new data paths. The one new server predicate (`OpenTabContextTypes`) lives in the ADR-039-sanctioned `PreFilter`. Detailed per-component justification to be completed at spec authoring.

## 11. ADR tensions

- **ADR-015 (data governance)**: R3 carries **identity + an active-item `id`** (a handle, never item content) in the prompt — far tighter than R2's ambient content exposure, but not pure "identity-only." Stated honestly: the prompt exposes *which* item is active (an opaque id), and every fact/action is a tool that fetches by that id from the source of record. This tightens ADR-015 relative to R2 without overclaiming a pure-identity narrowing. The active-tab-as-consent exception (R2, owner-approved) remains for the live-selection hint only. See §5.5 / §6.2.
- **ADR-039 (grounded execution, closed catalogs)**: tab-driven tool mounting uses the *permitted* deterministic pre-filter (context scoping) — no classifier, no second dispatch protocol. Parity capabilities are catalog rows.

## 12. Open items for spec

- The widget-type (4-variant) ↔ context-type (6-variant) mapping table. **(Needed for the registration contract — A1/A2 depend on it. Resolve at spec time.)**
- Which parity tools are **Bindings** (capability path) vs **`sprk_analysistool`+handler** (typed primitive). **Default resolution → the parameterized-reuse pattern below (B1).**
- The per-widget interaction matrix (respond/direct/hybrid) — full enumeration, expressed as the **registration-contract field** (per A2), not prompt prose.
- Whether the live-selection hint stays in the prompt or moves to a `get_selection` tool. **The §5.5 handle model argues for the tool** (`get_selection` fetches by id — consistent with "id, never content"). Lean toward the tool at spec time.
- **The tasks-count parity tool (the §8 acceptance-test driver).** "How many overdue tasks" must ANSWER — compute overdue **server-side with *today* injected**, reusing the My Tasks saved-query. This is the overview/query DoD; author it explicitly.

### Spec-time obligations carried from the 2026-08-10 design review (§B)

- **B1 — Pre-commit against per-grid tool sprawl (reuse discipline / CLAUDE.md §11).** §5 Lane 1's "domain-shaped tool per grid" must NOT become 8 hand-authored handlers. **Spec MUST pre-commit to ONE parameterized `configId`-driven overview tool** that executes the grid's existing query definition (saved-query / FetchXML) server-side over OBO — not N bespoke tools. This resolves the "Bindings vs typed primitive" open item toward the parameterized-reuse default.
- **B2 — Email widget selection model — RESOLVED 2026-08-10 (confirmed in code, sizing = small).** `EmailWorkspace` already owns list-pane selection (`selectedId`/`onSelectedIdChange`) AND already emits on selection change via `onVisibleEmailChange` → `deriveEmailWorkspaceVisibleState` (`EmailWorkspace.tsx:195-201`), consumed today by `EmailWorkspaceWidget.handleVisibleEmailChange` into the tab's `widgetData` (FR-C1 carrier). The selection-emit primitive ships. R3's delta is **small**: (a) redirect that emit to the active-item conduit (generalize Compose `registerActiveDocument`) so it triggers cards, and (b) flip the payload from content snapshot → **id handle** (`{ communicationId, emlDocumentId, subject }`), the §6-decision-2 walk-back of FR-C1's ambient content. The reply-composer-with-thread path is **already fully shipped** (`useEmailComposeActions` + reducer quote); the genuinely-new server work is `draft_reply`/`draft_forward` (auto-draft, thread-preserving `bodyOverride`) + `summarize_thread`.
- **B3 — Resolve §12 open items with the §5.5 lens.** The widget↔context mapping table (needed by the registration contract), the Bindings-vs-primitive split (→ B1), and the live-selection-hint decision (→ `get_selection` tool) all resolve more cleanly now that the handle model is canonical.
