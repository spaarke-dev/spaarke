# spaarkeai-assistant-enhancements-r3 — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-08-10
> **Source**: `design.md` (The Assistant ⇄ Workspace Interaction Contract; §5.5 orchestration model; owner-aligned through 2026-08-10, external review §A1–A6 applied)
> **Predecessor**: spaarkeai-assistant-enhancements-r2 (shipped surface-awareness + true-resume + Phase 0 quick-wins)

---

## Executive Summary

R3 delivers **Conversational Capability Parity**: every open workspace widget gets a matching Assistant tool set — an *overview/query* tool (answers chat questions from the authoritative source) and, for act-on surfaces, *per-item action* cards keyed to the selected item — mounted only while the widget's tab is open (tool economy). It closes the R2 UAT gap where the Assistant was *aware* of tabs but had no tool to answer from them (hand-wrote unsupported `GETDATE()`, opened duplicate tabs, narrated tab **names** instead of **data**). The spine is the **active-item handle model** (§5.5): the Assistant holds identity + a handle (`{id,type,label}`) to the selected item — **never its content** — and every fact/action is a tool that fetches by id. Generalized from the **shipped** Compose active-document flow.

---

## Scope

### In Scope
- **Awareness (Phase 1)**: layout/dashboard tabs (Daily Briefing, Calendar) become visible to the Assistant via an identity variant; persist `visibleToAssistant`; trim the prompt block to `{type,label,active}` per tab; publish + thread the **active-item handle** through a generalized active-item conduit.
- **Capability parity (Phase 2)**:
  - **Overview/query parity across all grids + Daily Briefing + Calendar** via **ONE parameterized `configId`-driven overview tool** (executes each surface's existing saved-query/FetchXML server-side over OBO, injects `today`) plus the lane-3 (`BriefingService`) and lane-1 (Calendar events) wrappers.
  - **Per-item action parity for Email and Documents** (the two per-item instances chosen for R3):
    - **Email**: cards *Reply · Reply All · Forward · Summarize the thread*; `draft_reply`/`draft_forward` (auto-draft, thread-preserving `bodyOverride`); `summarize_thread` (answers in chat like a file summary).
    - **Documents**: cards *Summarize · Draft response · Draft memo* backed by RAG (lane 2) + on-demand body.
  - widget-type ↔ context-type mapping table (registration metadata).
- **Tool economy (Phase 3)**: wire `Binding.ContextTypeTags` into the server ADR-039 `PreFilter`; mount only open tabs' tools.
- **Interaction patterns + follow-ons (Phase 4)**: declare respond/direct/hybrid as a **registration-contract field**; derive follow-on chips/cards deterministically from mounted parity tools + that field.
- **Cross-cutting — Registration contract**: Assistant-contract fields become **required** registration metadata; the registry enforces parity across all four registration sites.

### Out of Scope
- **Phase 0 quick-wins** — shipped on the R2 branch (inject today's date, `widget_load` de-dup guard, SignalR first-mount resilience, Compose tab naming, chat-pane scroll/scrollbar, date-query hardening). R3 **depends on** these but does not rebuild them.
- **Per-item action cards for Matters, Calendar, Invoices, Work-Assignments, Messages, Analysis, Metrics** — deferred to a follow-on. (These surfaces DO get **overview/query** parity in R3; only their *per-item cards* are deferred.)
- **`get_selection` tool** — owner chose to keep the `{id,type,label}` handle in the prompt (see Owner Clarifications). Not built.
- **New notification/push channel** — the reactive card surface must stay distinct from the ADR-047 proactive spine (NFR-07). No spine changes.
- **Compose write/read fidelity** — governed by ADR-049 / the compose projects; untouched here.

### Affected Areas
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` — `BuildWorkspaceStateBlock` (~:1469), `TryDeriveVisibleState`/`FormatVisibleStateFields` (~:1547/1679), tool-projection seam (~:826–869), active-tab-as-consent filter.
- `src/server/api/Sprk.Bff.Api/.../AgentToolProjection.cs` — `PreFilter` (~:101); `Binding.ContextTypeTags`.
- Parity-tool handlers in `Services/Ai/**` — the parameterized overview tool; email `draft_reply`/`draft_forward`/`summarize_thread` (extend `EmailDraftToolHandler`); document summarize/draft (reuse `IRagService`/`DocumentSearchHandler`/`DocumentContextService`); `BriefingService` wrapper; Calendar events query.
- `eml-render` — `FileAccessEndpoints.cs` (~:901).
- Registration sites: `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-workspace-widgets.ts` + `register-document-viewer-widget.ts` + `register-search-criteria-result-widget.ts` + `register-structured-output-stream-widget.ts` + `src/solutions/SpaarkeAi/src/components/workspace/registerComposeWidget.ts`.
- `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx` (~:2004 `setActiveTab`), `WorkspaceTabManager.addTab`.
- `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` — `activeSourceDocRef` (~:389/424), proactive-card surface (~:164-167), `composeActionBridge`/`registerActiveDocument` → generalize to the active-item conduit.
- Email surface (shared lib): `EmailWorkspace.tsx` (selection emit ~:199), `EmailWorkspaceWidget.tsx` (~:111), `useEmailComposeActions.tsx` (`openComposer` ~:93), `EmailComposer.tsx` (`runAiDraft` ~:789) + `EmailComposer.reducer.ts` (`deriveReplyState`/`deriveForwardState`, `quotedThread`), `fetchCommunicationPrefill.ts`, `SendEmailDialog`.

---

## Requirements

### Functional Requirements

**Phase 1 — Awareness (identity + active-item handle)**

1. **FR-01 (layout-tab visibility)**: Daily Briefing and Calendar (layout tabs, no `kind` → currently derive `null` → invisible to the agent) become visible via a light identity variant in `BuildWorkspaceStateBlock`. — **Acceptance**: with a Daily Briefing tab and a Calendar tab open, the agent prompt lists each by `{type,label}`; asking "do you see the daily briefing tab?" answers yes without opening a second tab.
2. **FR-02 (persist `visibleToAssistant`)**: the per-tab `visibleToAssistant` flag persists across reload/resume. — **Acceptance**: toggle off a tab's visibility, reload → it remains hidden from the agent prompt; toggle on → visible.
3. **FR-03 (trim prompt block)**: the workspace-state prompt block carries **only** `{type,label,active}` per tab — no ambient widget content. — **Acceptance**: a snapshot/characterization test of `BuildWorkspaceStateBlock` output shows no item content fields; only type/label/active per tab plus the single active-item handle (FR-04).
4. **FR-04 (active-item conduit + handle)**: generalize the Compose active-document flow (`composeActionBridge` → `registerActiveDocument` → `POST /api/compose/active-document` → client `activeSourceDocRef`) into a **widget-agnostic active-item conduit** carrying a single `{id,type,label}` handle; thread the handle into the prompt's active-item slot. The conduit is fed by **two publish patterns depending on widget archetype** (both have shipped precedent — reuse, don't invent): **(a) in-widget selection** for list-bearing widgets (email list pane → `onVisibleEmailChange`), and **(b) tab-focus** for single-item-per-tab surfaces (Compose + document-viewer → the existing `WorkspacePane` `active-doc-follows-tab` effect, `WorkspacePane.tsx:2283`). Enforce the **single-active-item invariant** (exactly one handle at a time) and **clear-on-deselect / tab-switch**. On a grid **multi-select**, no single active item is set and per-item cards suppress. — **Acceptance**: selecting an item in a list-bearing widget OR focusing a single-item tab places `{id,type,label}` in the prompt's active-item slot; deselecting or switching the tab's focus clears it; a grid multi-select yields no active-item handle (cards suppressed).
5. **FR-05 (email widget publishes selection as id handle)**: redirect the email widget's existing `onVisibleEmailChange` emit (`EmailWorkspace.tsx:199`; today consumed into tab `widgetData` as a content snapshot) to the active-item conduit as an **id handle** `{communicationId, emlDocumentId, subject}` — id/label only, **not** the content snapshot (the §6-decision-2 walk-back of FR-C1 ambient content). — **Acceptance**: selecting an email in the Email tab populates the active-item handle; the prompt carries the handle (id + subject label), never the body.

**Phase 2 — Capability parity (overview + per-item)**

6. **FR-06 (parameterized overview tool — the overview DoD driver)**: ONE parameterized `configId`-driven overview tool executes a surface's existing saved-query/FetchXML **server-side over OBO**, **injects `today`**, computes derived predicates server-side (e.g. `overdue = dueDate < today`), and returns grid-shaped rows with record-id citations. It reuses the query **definition** (not model-authored SQL). — **Acceptance**: "how many overdue tasks do I have?" answers with the correct count (overdue computed server-side with today injected, reusing the My Tasks saved-query), opens/directs to the Tasks tab, with **no** query error, **no** asking for the date, **no** duplicate tab.
7. **FR-07 (overview parity across all grids + Briefing + Calendar)**: wire the FR-06 tool across all grids via `configId`; wrap `BriefingService` (lane 3) for Daily Briefing; wrap the Calendar events query (lane 1) for Calendar. — **Acceptance**: each in-scope surface answers an overview question from its own data lane (e.g. "what's on my calendar this week", "summarize my daily briefing counts") citing record ids / service output, not a prompt snapshot.
8. **FR-08 (widget-type ↔ context-type map)**: enumerate the widget-type ↔ context-type mapping as registration metadata consumed by the registration contract (FR-13/FR-15) and the pre-filter (FR-12). — **Acceptance**: every in-scope widget declares its context-type; the mapping table is complete and referenced by the registry + pre-filter.
9. **FR-09 (email per-item parity tools)**: implement email per-item tools by **extending `EmailDraftToolHandler`** (not a new handler family): `draft_reply(communicationId, mode)` and `draft_forward(communicationId)` **auto-generate** the authored body and open the existing composer pre-filled; `summarize_thread(communicationId)` loads the `.eml` via `eml-render`/document-context and answers **in chat**. **Output shape (resolved)**: `summarize_thread` returns a **plain narrative summary identical to the existing file/document-summarize handler output** (the user's "same as file summarize" requirement) — NOT a new structured/JSON shape. Reuse the document-summarize prompt/response path over the `.eml`; do not author a bespoke thread-summary format. — **Acceptance**: the per-item Email DoD (FR-10 acceptance) plus: "Summarize the thread" card → a plain-narrative chat answer summarizing the thread (matching a file summary's form), no composer opened.
10. **FR-10 (`bodyOverride` — thread-preserving auto-draft) [BINDING invariant]**: add a `bodyOverride` parameter to `useEmailComposeActions.openComposer` that overrides **only the authored-message region**. The composer still derives the quoted previous thread from the source record (`deriveReplyState`/`deriveForwardState` → `state.quotedThread`) and appends it below the draft, so the composer opens as **[AI draft] + [separator] + [quoted thread]** — reaching parity with the in-dialog sparkle re-append (`EmailComposer.tsx` `runAiDraft`, owner UAT 2026-08-03 R5 items 1/2). A `bodyOverride` that replaces the whole body (dropping the quoted thread) is a **defect**. — **Acceptance** (per-item Email DoD): select an email → a **Reply** card appears with no typing → click → `SendEmailDialog` opens pre-filled with recipients + `Re:` subject + auto-drafted body **AND the quoted previous thread preserved below the draft**. A reducer/unit test asserts the composed body contains both the draft and the `quotedThread` block.
11. **FR-11 (document per-item parity — tab-focus active item)**: the document active item is the **focused `document-viewer` tab's document** — published via the **tab-focus pattern** (FR-04b), i.e. generalize the existing `WorkspacePane` `active-doc-follows-tab` effect (`WorkspacePane.tsx:2283`, today gated `widgetType === 'compose'`) to also fire for `document-viewer` tabs, publishing `{documentId, filename}` as the active-item handle. The stable `documentId` already rides on `DocumentViewerWidget`'s `widgetData` (`DocumentViewerWidget.tsx:99-112`) — **no new in-widget selection model is built**. Per-item cards *Summarize · Draft response · Draft memo* are backed by RAG (lane 2) + on-demand body; outputs land on the native surface (chat answer for summarize; composer/Compose for drafts, per the interaction pattern). — **Acceptance**: focus a `document-viewer` tab → the three cards appear (no typing) → "Summarize" answers in chat from RAG/body (record-id cited), not from a tab snapshot; switching away clears the handle (cards suppress).

**Phase 3 — Tool economy**

12. **FR-12 (open-tab tool scoping)**: add `OpenTabContextTypes` to the `PreFilter` filter context + one predicate; hoist `tabs`; mount a widget's parity tools **only when its tab is open**, keyed by the FR-08 context-type. No classifier, no second dispatch surface (ADR-039). — **Acceptance**: with only the Tasks tab open, the projection includes the tasks overview tool and excludes email/document per-item tools; opening the Email tab adds the email tools; closing it removes them.

**Phase 4 — Interaction patterns + accurate follow-ons**

13. **FR-13 (interaction pattern as registration field)**: each widget declares its respond/direct/hybrid pattern as a **registration-contract field** (not prompt prose). — **Acceptance**: the pattern is read from registration metadata; no per-widget matrix exists in the system prompt.
14. **FR-14 (deterministic follow-ons + element type)**: derive follow-on chips/cards deterministically from the **mounted parity tools + the interaction pattern** — never invented by the model. **Element type (resolved per `docs/standards/ASSISTANT-UI-ELEMENT-CRITERIA.md`)**: **per-item actions on an active item render as CARDS** (persistent act-on items — email Reply/Reply All/Forward/Summarize; document Summarize/Draft-response/Draft-memo); **overview/query turn-follow-ons render as CHIPS** (throwaway next-questions after a chat answer). Proactive card **stacks collapse behind one disclosure header** (criteria doc + design A4). — **Acceptance**: follow-ons for an open tab correspond exactly to that widget's mounted tools + declared pattern; no follow-on references an unmounted tool; per-item actions render as cards, query follow-ons as chips; multiple proactive cards collapse under one header.

**Cross-cutting — Registration contract**

15. **FR-15 (required Assistant-contract metadata)**: the registration metadata REQUIRES the Assistant-contract fields — context-type · overview tool(s) · per-item cards + landing target · interaction pattern — and the registry enforces their presence across **all four** registration sites. — **Acceptance**: a widget registered without the contract fields fails (compile-time type error or a registry runtime guard) at every registration site; adding a new widget without a contract cannot ship.

### Non-Functional Requirements

- **NFR-01 (BFF publish size)**: every BFF-touching task verifies compressed publish size; **≤60 MB ceiling**, ~49.63 MB baseline (2026-07-08). Report absolute + delta in task notes; ≥+5 MB single-task delta needs justification. (CLAUDE.md §10.)
- **NFR-02 (no new HIGH CVE)**: `dotnet list package --vulnerable --include-transitive` shows no new HIGH-severity finding.
- **NFR-03 (ADR-039 fidelity)**: tab-driven tool mounting uses only the sanctioned deterministic `PreFilter` — no classifier, no second dispatch protocol; parity capabilities are catalog rows.
- **NFR-04 (ADR-015 — id-not-content)**: the prompt carries the `{id,type,label}` handle + per-tab identity only; **no item content** ever enters the prompt. All content is tool-fetched by id from the source of record.
- **NFR-05 (dual-mount parity)**: shared email components stay host-agnostic (ADR-012); changes MUST NOT break the standalone `sprk_emailpage` code-page mount. `EmailWorkspace` renders unchanged across both mounts (email project NFR-06).
- **NFR-06 (reuse-first / §11)**: ONE parameterized overview tool, not N per-grid handlers; email tools extend `EmailDraftToolHandler`; document tools reuse `IRagService`/document-context. No new data path where an existing service suffices.
- **NFR-07 (reactive ≠ proactive)**: the reactive/local-selection card surface stays distinct from the ADR-047 server-initiated notification spine. No merge; no new push channel.
- **NFR-08 (test obligation)**: PRs modifying `Sprk.Bff.Api/Services/**` add/update tests in `tests/unit/Sprk.Bff.Api.Tests/` (CLAUDE.md §10.6); parity tools + the `bodyOverride` composition are unit-tested; email/reducer changes covered.
- **NFR-09 (registration enforcement)**: the contract is enforced structurally at all four registration sites (FR-15), not by convention/prose.

---

## Technical Constraints

### Applicable ADRs
- **ADR-039** (grounded execution, closed catalogs, deterministic pre-filter) — the mounting mechanism; no classifier.
- **ADR-015** (data governance) — id-not-content prompt exposure; see ADR Tensions.
- **ADR-030** (PaneEventBus) — widget mount/selection eventing.
- **ADR-047** (notification spine) — keep the reactive card surface distinct.
- **ADR-049** (Compose) — the shipped precedent being generalized; do not regress Compose.
- **ADR-012** (shared component library, host-agnostic) — email/document components take deps as props.
- **ADR-028** (auth / `authenticatedFetch`) — all BFF reads/writes over OBO.
- **ADR-038** (testing strategy) — integration-heavy; test obligations per NFR-08.
- **ADR-032** (Null-Object kill-switch) — **only if** any parity tool ships feature-gated (asymmetric-registration rule; default: register unconditionally).

### MUST Rules
- ✅ MUST fetch every stated fact/content via a tool that queries the source; MUST NOT read item content from a widget snapshot or the prompt.
- ✅ MUST keep the prompt to `{type,label,active}` per tab + one `{id,type,label}` active-item handle.
- ✅ MUST implement the overview surface as ONE parameterized `configId` tool reusing existing saved-queries; MUST NOT author per-grid handlers.
- ✅ MUST make `bodyOverride` compose (draft above reducer-derived quoted thread); MUST NOT replace the whole body.
- ✅ MUST route new email tools through an extension of `EmailDraftToolHandler`; MUST NOT inject AI-internal types into CRUD code (use `Services/Ai/PublicContracts/` facade — refined ADR-013).
- ✅ MUST enforce the registration contract at all four sites; MUST NOT let a widget ship without it.
- ✅ MUST keep the reactive card surface separate from the ADR-047 spine.

### Existing Patterns to Follow
- Active-item conduit: `ConversationPane.tsx` `activeSourceDocRef` + `registerActiveDocument` (generalize).
- Overview tool query execution: `DataverseSqlQueryTranslator` / `DataverseUserClient` behind `read_query`; saved-query/`configId` reuse per `DataGrid` + `sprk_gridconfiguration`.
- Thread-preserving draft: `EmailComposer.reducer.ts` `quotedThreadBlock`/`deriveReplyState` + `EmailComposer.tsx` `runAiDraft` re-append.
- Pre-filter: `AgentToolProjection.cs` `PreFilter` (~:101) + `Binding.ContextTypeTags`.
- Composed-service wrapper: `BriefingService` (lane 3) as a parity-tool backing.

---

## Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration
```xml
<hot-path-declaration>
  <bff>Y</bff>                <!-- tool projection, parity tools, Dataverse/RAG handlers, ContextTypeTags pre-filter -->
  <spaarkeai>Y</spaarkeai>    <!-- ConversationPane, WorkspacePane, WorkspaceTabManager, registration, chat UI -->
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

**Placement Justification (BFF=Y)**: all new BFF surface extends the **existing** two-catalog ADR-039 projection — no new dispatch mechanism, no new store. Parity tools reuse existing data services (`read_query`/`search_data`, `IRagService`, `BriefingService`, `EmailDraftToolHandler`) over OBO; the one new server predicate (`OpenTabContextTypes`) lives in the sanctioned `PreFilter`. Publish-size ceiling ≤60 MB applies per BFF task (NFR-01). Run `/conflict-check` before every BFF/ConversationPane PR (active worktrees: notification-spine, analysis-hub, compose lines historically touch these files).

### New Components (§11 three-question gate)

| New component | Existing overlap (grep) | Can extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| Active-item conduit (widget-agnostic) | `registerActiveDocument`/`composeActionBridge` (Compose-only) | **Yes — generalize** the Compose conduit to any widget's `{id,type,label}` | Per-item cards can't key to a selection; the entire §5.5 per-item flow is impossible |
| Parameterized `configId` overview tool | `read_query` (generic, rejects `GETDATE()`/aggregates) | **Extends** existing query execution (wraps saved-query + injects today) — ONE tool, not N | "how many overdue tasks" fails (model authors unsupported SQL) — the exact R2 UAT defect |
| `draft_reply` / `draft_forward` tools | `EmailDraftToolHandler` (exists) | **Yes — extend** `EmailDraftToolHandler` | Reply/Forward cards can't auto-draft; per-item Email DoD fails |
| `summarize_thread` tool | `IRagService`/`DocumentContextService` + `eml-render` | **Yes — reuse** the file-summarize path over the `.eml` | "Summarize the thread" card has no backing; no chat answer |
| `bodyOverride` param on `openComposer` | `useEmailComposeActions.openComposer` (exists) | **Yes — add one param** that composes above `quotedThread` | Auto-draft either can't inject a body or clobbers the thread (the invariant this project exists to hold) |
| `OpenTabContextTypes` predicate | `AgentToolProjection.PreFilter` (exists) | **Yes — one predicate** in the existing filter | Tool economy can't scope by open tabs; all tools mount always (token bloat, ADR-039 drift) |
| Required registration-contract fields | four registration sites (exist) | **Yes — add required metadata** to the existing registration shape | Widgets ship without an Assistant contract; parity silently regresses (the §3 gap) |
| Document per-item cards (Summarize/Draft response/Draft memo) | document RAG handlers (exist); `active-doc-follows-tab` (exists, Compose-only) | Cards are **new UI**; backing tools **reuse** lane 2; active-item **reuses** the tab-focus effect (generalize one condition, no new selection model) | Documents have overview parity but no act-on surface; the second per-item instance is missing |

*Not built (owner decision)*: `get_selection` tool — the `{id,type,label}` handle stays in the prompt.

---

## ADR Tensions (per CLAUDE.md §6.5)

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| **ADR-015** | data governance — minimize model-visible data | R3 carries `{id,type,label}` (the label is a thin slice of content, e.g. an email subject) in the prompt to enable instant selection-awareness + handle-driven cards | **A (project-scoped exception, documented honestly)** | Far tighter than R2's ambient content; content is always tool-fetched by id. Stated plainly (not overclaimed as pure identity-only). Owner chose handle-in-prompt over the `get_selection` tool after weighing the marginal governance gain vs. an extra round-trip (the label is in the handle either way). |
| **ADR-039** | closed catalogs / one decider — no second dispatch surface | none — tab-driven mounting uses the permitted deterministic `PreFilter`; parity tools are catalog rows | **C (comply)** | No classifier, no second dispatch protocol introduced. |
| **ADR-047** | notification spine is the push channel | none — the reactive card surface is client/local-selection-triggered, not server push | **C (comply)** | Surfaces kept distinct per NFR-07; no spine changes. |

---

## Success Criteria

1. [ ] **Overview DoD**: "how many overdue tasks do I have?" → correct count, opens/directs Tasks tab, no query error, no date prompt, no duplicate tab. — Verify: manual UAT + integration test over the parameterized tool with `today` injected.
2. [ ] **Per-item DoD**: select an email → **Reply** card appears (no typing) → click → `SendEmailDialog` pre-filled with recipients + `Re:` subject + **auto-drafted body AND preserved quoted thread**. — Verify: manual UAT + reducer/unit test asserting composed body contains draft + `quotedThread`.
3. [ ] **Summarize**: "Summarize the thread" card → chat answer summarizing the `.eml` thread (no composer). — Verify: manual UAT.
4. [ ] **Document per-item**: select a document → Summarize/Draft-response/Draft-memo cards appear → Summarize answers in chat from RAG/body with record-id citation. — Verify: manual UAT.
5. [ ] **Overview breadth**: all in-scope grids + Daily Briefing + Calendar each answer an overview question from their own lane. — Verify: UAT matrix per surface.
6. [ ] **Tool economy**: closed-tab tools are absent from the projection; opening/closing a tab adds/removes its tools. — Verify: projection unit test with varying open-tab sets.
7. [ ] **Registration enforcement**: a widget without contract fields fails at each of the four registration sites. — Verify: a deliberately-incomplete registration fails compile/guard in a test.
8. [ ] **Governance**: `BuildWorkspaceStateBlock` output carries no item content. — Verify: characterization test.
9. [ ] **BFF hygiene**: publish size ≤60 MB; no new HIGH CVE; dual-mount email parity intact (standalone code page unaffected). — Verify: publish measurement + CVE scan + code-page smoke.

---

## Dependencies

### Prerequisites
- **Phase 0 (R2 branch)** shipped: today-date injection, `widget_load` de-dup guard, SignalR first-mount resilience, Compose tab naming. R3's "no duplicate tab" DoD relies on the shipped de-dup guard (verify it covers the layout-tab path during FR-01).
- Shipped Compose active-document flow (the conduit being generalized).
- Shipped email surface (selection emit, composer, reducer quote machinery, `EmailDraftToolHandler`, `eml-render`).
- `sprk_gridconfiguration` saved-queries per grid (the overview tool's reusable asset).

### External Dependencies
- Azure OpenAI deployment (auto-draft + summarize).
- Azure AI Search / RAG index (document lane 2).
- OBO auth path (ADR-028) for all BFF reads/writes.

---

## Owner Clarifications

| Topic | Question | Answer | Impact |
|---|---|---|---|
| Reply-card behavior | Open composer only, or auto-draft the reply? | **Auto-draft** (proactive AI) | `draft_reply` generates the body before opening the composer; requires `bodyOverride`. |
| Thread preservation | Must the auto-draft keep the quoted thread? | **Yes — BINDING invariant** | `bodyOverride` composes draft above reducer-derived `quotedThread`; whole-body replace = defect (FR-10). |
| Per-item breadth | Which widgets get per-item cards in R3? | **Email + Documents** | Two per-item instances; Matters/etc. per-item deferred (still get overview parity). |
| Overview breadth | How many surfaces get overview parity? | **All grids + Briefing + Calendar** | One parameterized `configId` tool wired across all grids + BriefingService + Calendar (FR-06/FR-07). |
| Selection hint | `get_selection` tool or handle-in-prompt? | **Keep handle in prompt** | No `get_selection` tool; prompt carries `{id,type,label}`; instant selection-awareness, no extra round-trip (NFR-04 satisfied — content still tool-fetched). |
| Phase 0 | In or out of R3? | **Out (shipped on R2)** | R3 = Phases 1–4 + registration contract only. |

---

## Assumptions

- **De-dup guard coverage**: the Phase 0 `widget_load` de-dup guard covers the generic layout-tab path; FR-01 verifies this rather than rebuilding it. If a gap surfaces for layout tabs, a small guard extension is in-scope.
- **Interaction landing targets**: document "Draft response/memo" land on a composer/Compose surface per the widget's declared interaction pattern; exact target confirmed at FR-13 authoring.
- **Calendar/Briefing overview shape**: overview answers for Calendar (events query) and Briefing (`BriefingService`) return counts/records suitable for a chat answer; no new aggregate endpoints needed.

## Unresolved Questions

*All three questions flagged at first draft were resolved at spec authoring (2026-08-10) via code investigation:*

- ✅ **Document selection model (FR-11) — RESOLVED**: the document-viewer does NOT emit an in-widget selection and does not need to. The document active item is the **focused `document-viewer` tab's document**, published via the **tab-focus pattern** — generalizing the existing `WorkspacePane` `active-doc-follows-tab` effect (`WorkspacePane.tsx:2283`) from Compose-only to `document-viewer`. The stable `documentId` already rides on `widgetData` (`DocumentViewerWidget.tsx:99-112`). Sizing = **small** (reuse). Folded into FR-04b + FR-11.
- ✅ **`summarize_thread` output shape (FR-09) — RESOLVED**: plain narrative summary identical to the existing file/document-summarize output ("same as file summarize"); reuse that handler's prompt/response path over the `.eml`. No bespoke structured format. Folded into FR-09.
- ✅ **Follow-on card vs. chip (FR-14) — RESOLVED** (per `ASSISTANT-UI-ELEMENT-CRITERIA.md`): per-item actions on an active item → **cards**; overview/query turn-follow-ons → **chips**; proactive card stacks collapse behind one disclosure header. Folded into FR-14.

*(No open blocking questions remain. Any residual detail — e.g. the exact Compose-vs-composer landing target for document "Draft memo" — is a task-authoring decision under FR-13, not a spec blocker.)*

---

*AI-optimized specification. Original design: `design.md`.*
