# Compose → First-Class Widget — Requirements & Design (NEXT round, after current UAT)

> **2026-07-13.** Owner-approved to build **after the current UAT round settles**. Purpose: convert Compose
> from a LegalWorkspace *layout section* into a **true first-class Direct workspace widget**, with a
> **robust bidirectional Assistant↔Compose interaction** and the **Context / Execution Trace as a
> first-class part of the drafting/editing loop** (owner's two explicit emphases). This is pure
> architecture (no new user-facing feature) on the hot WorkspacePane tab path — build it ISOLATED, not
> folded into a bug-fix round.

---

## 0. Current state (what exists after round-4/5)

- **Wave 5 (`d5ae23cca`)** registered Compose as a Direct widget (`widgetType: 'compose'`) + a `getVisibleState`, BUT it is **registered-but-DORMANT**: nothing dispatches `widget_load{'compose'}`, so Compose **still mounts via the LegalWorkspace "Compose" layout row**, not as the Direct widget. Files: `src/solutions/SpaarkeAi/src/components/workspace/registerComposeWidget.ts`, `ComposeDirectWidget.tsx`, `composeWidgetData.ts` (the adapter + registration already exist and are tested — 8 tests).
- **The "Active Workspace Document" contract** (round-4/5) is the foundation this builds on:
  - Session established on EVERY mount door (Wave 2): chat upload sets `ChatSession.ActiveDocument` (source=session-upload, SessionFileId); browse-direct-upload mints a client `state.sessionId`; the assistant upload-mount door now registers `documentSessionId` (round-5 gap fix).
  - `ActiveDocument.DocumentSessionId` back-fills server-side (Wave 3 + round-5) → `BindingCapabilityTool` routes compose-disposition (TEXT/typed) dispatch to the DOCUMENT session.
  - "Open in Compose" opens the ARTIFACT (Wave 3); tab-scoped ActiveDocument (active tab wins).
  - Redline anchor normalization + selection fallback (Wave 1).
  - Revise-this-document → auto-mount + intent chips → redline (round-5 `46eaaef32`).
- **Two-wrapper model** (docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md): Direct widget registry vs Dashboard/layout. The **workspace picker lists BFF `sprk_workspacelayout` rows** (`useWorkspaceLayouts` → GET /api/workspace/layouts), NOT the registry — Daily Briefing/Calendar appear via layout rows (§3.1 corrected in Wave 5). **This is load-bearing for the migration (see §5 picker decision).**

---

## 1. Goal

Compose mounts and runs as a **Direct widget** (via `widget_load{widgetType:'compose'}`), with:
1. A clean host contract (typed `data` payload replacing the three ambient ComposeLaunchContext/ComposeActionBridge providers).
2. A **robust bidirectional Assistant↔Compose interaction** (§3).
3. The **Context / Execution Trace first-class** in the drafting loop (§4).
Zero regression to every existing entry door and to tab management.

---

## 2. The flip — what mechanically changes

| # | Change | Files (starting points) |
|---|---|---|
| 2.1 | Route ALL Compose open-paths through `widget_load{widgetType:'compose', widgetData:{compose:{…}}}` instead of the "Compose" layout row | `main.tsx` (SpaarkeAiWorkspaceRenderer, ribbon URL parse ~247-334/490-533), `ConversationPane.handleOpenInCompose` + `mountActiveSourceDocInCompose`, `ThreePaneShell` compose-launch |
| 2.2 | Re-key **WorkspacePane single-tab-reuse** to the `'compose'` widget discriminant (today keys on `'workspace'` tab + `layoutName==='Compose'`, ~815-880) — dedup so no duplicate Compose tabs; preserve DEF-08 reuse | `WorkspacePane.tsx:815-880, 1043, 538-543` |
| 2.3 | Replace the **active-layout override** (`composeMode==='editor'` → force "Compose" layout, `WorkspacePane.tsx:485-492`) with the widget mount | `WorkspacePane.tsx:485-492` |
| 2.4 | **Picker presence** — the picker lists BFF layout rows, not the registry. Keep a **thin "Compose" layout-row alias** that resolves to the `'compose'` widget (preserves dropdown presence) OR extend the picker to include Direct widgets. **Recommend: thin alias** (least blast radius; Daily Briefing/Calendar already prove layout-row + widget can coexist) | `sprk_workspacelayout` "Compose" row (GUID `c09d26be-…`), `Deploy-SystemWorkspaceLayouts.ps1`, WorkspacePane layout resolution |
| 2.5 | Ensure the **Direct adapter** (`ComposeDirectWidget.tsx`) fully supplies what the section shim (`composeEditor.registration.ts`) did: `initialDocumentRef/initialUploadRef/initialDraftRef`, `containerId`, `bffBaseUrl`, the ComposeActionBridge conduits (toolbar-queue, registerActiveDocument, redline-accept). If the Direct `data` prop can't carry the live sibling-pane conduits, keep those as context but move launch data to `data`. | `ComposeDirectWidget.tsx`, `composeEditor.registration.ts`, `composeActionBridge.ts` |
| 2.6 | **Standalone LegalWorkspace mount** — the section shim currently serves standalone (no ThreePaneShell). The widget path must serve standalone too, OR keep the shim as the standalone renderer. Decide + preserve. | `composeEditor.registration.ts:44-53` |

---

## 3. Bidirectional Assistant ⇄ Compose contract (ROBUST — owner emphasis #1)

The widget flip MUST make the two-way interaction robust and coherent. Enumerate and guarantee BOTH directions. The two-session model (chat session vs document session, DEF-09) stays — never unify.

### 3a. Assistant → Compose (drive the document)
- **Open** a document: `compose.upload` (chat-uploaded), `compose.draft` (AI-drafted, DEF-08), stored `sprk_document`. Via `SendWorkspaceArtifactHandler` (server) + `handleOpenInCompose` (client).
- **Dispatch an edit / revise** → inline **redline**: compose-disposition SessionOutput → routed to the **document session** (`ActiveDocument.DocumentSessionId`) → `usePendingRedline.materialize/materializeMany`. Single-clause (compose-draft-alternative) + whole-doc (compose-revise-document edits[]+comments[]).
- **Invariant (round-4)**: NO mount without a document session; every open door establishes it. This MUST hold on the widget path.

### 3b. Compose → Assistant (expose live state + confirmations)
This is where "robust" matters most — today it is split across TWO channels that must be made coherent:
- **Declarative visibility** (`getVisibleState`, the widget contract): WHAT document is open (identity, filename). This is a synchronous snapshot of `widgetData` — it does NOT carry live post-mount state. ADR-015 minimization: expose filename to the LLM, withhold internal ids.
- **Live routing** (`registerActiveDocument` → `POST /api/compose/active-document` → `ActiveDocument.DocumentSessionId`): WHERE edits route (the doc session id, back-filled after mount). This is a client→server write, a different channel than getVisibleState.
- **REQUIREMENT**: as a true widget, unify/coordinate these so the Assistant always has a **complete, current** picture — "the Compose tab holds document X, session Y, with these pending redlines." Consider: getVisibleState reads from the same active-document state the bridge registers, so the declarative + live views never disagree.
- **Selection context**: `compose_selection_changed` (Flow 1) → Context/Assistant know the highlighted span.
- **Confirmations**: accept/reject/try-another of redlines (DEF-12) render in the Assistant; the Assistant is the AI⇄user surface (owner principle). Summary-only, never restate the full edit.
- **Tab-scoped**: the ACTIVE Compose tab is what the Assistant targets (Wave 3). Resolve the multi-instance `tab_change` last-write-wins boundary (Wave 3 note) via the widget's `allowMultiple` policy + per-instance identity.

### 3c. Robustness checklist (must all hold post-flip)
- [ ] Every open door (ribbon, chat "Open in Compose", auto-mount/revise, Browse, stored, AI-draft) establishes a document session AND registers the active document (declarative + live).
- [ ] `getVisibleState` and the registered `ActiveDocument` never disagree about the open doc.
- [ ] TEXT/typed revise + CLICK/toolbar edit BOTH route to the document session and redline (not chat prose).
- [ ] Multi-tab: active tab drives the Assistant target; no cross-tab misroute.
- [ ] Two-session model (chat vs document) stays coherent; edits belong to the document session.
- [ ] Accept/reject confirmations surface in the Assistant; no full-text restatement.

---

## 4. Context / Execution Trace — first-class in the drafting loop (owner emphasis #2)

The Execution Trace is **part of the compose drafting/editing process**, not an afterthought — it is the transparency surface showing WHAT tools / knowledge / skills the AI deploys while drafting/editing. Round-4 Wave 4 auto-opens it on Compose activation; the widget flip MUST preserve and strengthen this.

### Requirements
- **Auto-open survives the flip**: Wave 4 wired `ContextPaneController` `tab_change` → `setSelectedTool('execution-trace')` using the discriminant `widgetData.compose != null || layoutName === 'Compose'`. When Compose mounts as `widgetType:'compose'`, **update the discriminant to also match `widgetType === 'compose'`** so the trace still auto-opens. (`ContextPaneController.tsx` ~564-615.)
- **Trace populates DURING compose edits**: every compose-disposition dispatch (explain / compare / draft-alternative / revise) must emit `tool_chain` / trace events to the session ledger → `ExecutionTraceWidget` (via `ComposeTraceHost`) renders them. Verify the compose dispatch spine emits trace events (it should — the trace reads the ADR-040 ledger projection: `GET /api/ai/chat/sessions/{id}/trace`). If a compose path does NOT emit trace events, that is a gap to close as part of this work.
- **Trace is scoped to the document being drafted**: when the user works document X in Compose, the trace shows X's AI activity (the compose document session's trace), not unrelated chat activity. Confirm `ComposeTraceHost`'s `sessionId` resolves to the compose document session (or the coherent session) so the trace reflects the drafting, not just the chat.
- **Context pane ⇄ Compose wiring stays intact**: `compose_selection_changed`, `compose_assistant_insight`, and the execution-trace tool selection must all remain wired through the widget mount (they flow on the PaneEventBus `context` + `workspace` channels — the widget must dispatch the same events the layout section does).
- **Design intent**: as the user drafts/edits, the Context pane is the "show your work" surface — the attorney sees what the AI consulted (playbook, precedent, tools) for each edit. Treat the trace as a co-equal pane in the compose experience, not optional chrome.

---

## 5. Build plan (next round — isolated, heavy regression)

- **Phase A** — route open-paths through `widget_load{'compose'}`; keep the thin layout-row alias for picker presence (§2.4).
- **Phase B** — re-key WorkspacePane single-tab-reuse + active-layout override to the widget discriminant (§2.2/2.3).
- **Phase C** — coherent bidirectional contract: unify `getVisibleState` + `registerActiveDocument` so the Assistant's declarative + live views agree (§3b); verify the §3c checklist.
- **Phase D** — Context/Execution Trace: update the auto-open discriminant for `widgetType:'compose'`; verify trace populates + is document-scoped during edits; verify context-pane wiring (§4).
- **Phase E** — standalone LegalWorkspace mount preserved (§2.6).
- **Phase F** — HEAVY regression: ribbon open, chat "Open in Compose", auto-mount/revise, Browse-upload, stored-doc, AI-draft seed, single-tab reuse, relaunch, standalone mount, redline (single + whole-doc), accept/reject, trace auto-open + population, multi-tab targeting. Each with wire-body/mounted tests. SpaarkeAi build + workspace + conversation + Compose.Components suites green.

**Execution discipline**: ONE agent per phase (round-3 lesson — never nested sub-agents); no self-commit; the main session gates + commits each phase; consolidated gate (BFF publish-size if BFF touched, full suites) before deploy.

---

## 6. Open decisions (resolve at build time)

1. **Picker**: thin layout-row alias (recommended) vs extend picker to registry Direct widgets.
2. **Standalone**: Direct adapter serves standalone vs keep section shim as the standalone renderer.
3. **Multi-instance**: `allowMultiple` policy for the Compose widget + per-instance identity to fix the Wave-3 `tab_change` last-write-wins boundary (only matters if genuine multiple Compose tabs are allowed).
4. **getVisibleState ↔ registerActiveDocument coherence** (§3b): unify the source of truth so declarative + live never disagree.

---

## 7. Reference

- Round-4/5 commits: `89fbef95d` (W1 anchor), `7e1819db0` (W2 session), `209edb6cc` (W3 open-artifact + tab-scoped + TEXT-path), `4577a0608` (W4 trace + mapping), `3c451e933` (W6 non-docx), `d5ae23cca` (W5 dormant widget registration), `46eaaef32` (round-5 revise flow).
- Diagnosis: `notes/uat-round3-reuat-diagnosis.md`, `notes/round4-active-artifact-build-plan.md`.
- Architecture: `docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md` (two wrappers, §3.1 picker), `SPAARKEAI-WORKSPACE-ARCHITECTURE.md`, `SPAARKEAI-COMPONENT-MODEL.md`, `docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md` (§4.2 Pattern D Step 6 = the additive registration Wave 5 did).
- Widget registration (Wave 5, dormant): `src/solutions/SpaarkeAi/src/components/workspace/registerComposeWidget.ts`, `ComposeDirectWidget.tsx`, `composeWidgetData.ts`.
- Trace: `ContextPaneController.tsx`, `ComposeTraceHost.tsx`, `ExecutionTraceWidget` (@spaarke/ai-widgets).
