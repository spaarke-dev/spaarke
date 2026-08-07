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
| **AWARENESS** (knows what's open + active) | `BuildWorkspaceStateBlock:1469`; active-tab-as-consent shipped; focus-stamp threading | Dashboards/Calendar invisible (layout tabs have no `kind` → derive `null`; `visibleToAssistant` not persisted). Add a layout/identity variant; **trim prompt to `{ type, label, active }`** |
| **PARITY** (widget ↔ tool set) | Generic Dataverse tools; DataGrid query stack; `BriefingService`; `IBriefingAi`; `EmailDraftToolHandler`; text/analysis handlers | Author per-widget tool sets (read + actions). Reuse the query **definition** executed server-side over OBO. Add widget-type → context-type map |
| **ECONOMY** (only open tabs' tools) | ADR-039 `PreFilter` (`AgentToolProjection.cs:101`); `Binding.ContextTypeTags` (FR-B2); seam `SprkChatAgentFactory.cs:826–869` | `ContextTypeTags` is client-only today. Add `OpenTabContextTypes` to the filter context + one predicate; hoist `tabs` |
| **INTERACTION** (respond/direct/hybrid) | dispatch + surface-launch plumbing | Author a **per-widget policy matrix** into the prompt |
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

## 6. Key decisions (owner-confirmed 2026-08-07)

1. **Keep standalone email + document tabs** (not Compose-only). Each gets a parity tool set with read + actions.
2. **Strip ambient content to identity-only.** The prompt carries `{ type, label, active }` per tab; the one exception is the live text-selection hint. All factual data comes via tools. This lightly walks back FR-C1's ambient email data (email tab stays aware; content via tool). Rationale: single source of truth, no stale-snapshot hallucination, leaner prompt, scales.
3. **Phasing**: Phase 0 (quick wins) ships on the R2 branch now; Phases 1–4 (the contract) are R3.
4. **Registration contract**: Assistant-contract fields become required registration metadata.

---

## 7. Phasing

- **Phase 0 — quick wins (R2 branch, now, independent of the contract)**: inject today's date into the agent context; de-dup guard on `widget_load` (kills duplicate tabs); SignalR first-mount resilience (the 401 hang); Compose tab naming; chat-pane scroll-to-top on send + thin scrollbar; harden the date-query tool.
- **Phase 1 — Awareness (identity-only)**: dashboards/layouts visible via a light identity variant; persist `visibleToAssistant`; trim prompt block.
- **Phase 2 — Capability parity**: author per-widget tool sets (read + actions) reusing existing query definitions/services; widget-type → context-type map. *(Where "how many overdue tasks" starts working.)*
- **Phase 3 — Tool economy**: wire `ContextTypeTags` into the server pre-filter; mount only open tabs' tools.
- **Phase 4 — Interaction patterns + accurate follow-ons**: publish the per-widget respond/direct/hybrid matrix; derive follow-on chips from mounted tools + pattern.
- **Cross-cutting — Registration contract**: required Assistant-contract fields; threads through 1–4.

---

## 8. Acceptance test (definition of done)

> **"how many overdue tasks do I have?"** → *"You have 6 overdue tasks. I've opened your Tasks tab showing them."*

No query error, no asking for the date, no duplicate tab. When this works end-to-end the contract is real.

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

- **ADR-015 (data governance)**: R3 *narrows* prompt exposure (identity-only), tightening ADR-015 rather than widening it. The active-tab-as-consent exception (R2, owner-approved) remains for the live-selection hint only.
- **ADR-039 (grounded execution, closed catalogs)**: tab-driven tool mounting uses the *permitted* deterministic pre-filter (context scoping) — no classifier, no second dispatch protocol. Parity capabilities are catalog rows.

## 12. Open items for spec

- The widget-type (4-variant) ↔ context-type (6-variant) mapping table.
- Which parity tools are **Bindings** (capability path) vs **`sprk_analysistool`+handler** (typed primitive).
- The per-widget interaction matrix (respond/direct/hybrid) — full enumeration.
- Whether the live-selection hint stays in the prompt or moves to a `get_selection` tool.
