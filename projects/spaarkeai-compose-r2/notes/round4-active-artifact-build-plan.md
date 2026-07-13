# Round-4 build plan — Active Workspace Document contract (UAT round-3 fixes + Compose first-class widget)

> **2026-07-12.** Owner-approved scope after 6 read-only investigations of the round-3 re-UAT (Tests #1-4 + trace + console). Root causes in `notes/uat-round3-reuat-diagnosis.md`. **ONE landing (no phasing / no deferral).** Core (redesign-r2) WAITS; we own the whole cluster and expose the create-on-save memory hook for their FR-30 (see `HANDOFF-to-core-fr30-anchor-2026-07-12.md`). Master merge HELD until owner UAT passes.
>
> **Execution rule (round-3 lesson, BINDING):** build each wave with ONE agent OR in the main session; NEVER let a build agent spawn nested sub-agents. Each Axis-I/II wave carries a wire-body/E2E test per the project E2E DoD (unit-green ≠ done).
> **Conflict-check (2026-07-12):** CLEAR — no active worktree/PR overlaps our files; fix-compose-launch-and-viz already merged; core paused (docs-only).

## The two axes
- **Axis I — Identity**: every mount door establishes a tab-scoped document session + registers the active artifact so the Assistant can target it. (Tests #1/#2/#3.)
- **Axis II — Anchoring**: AI edits anchor to the tab's CURRENT content via normalized matching. (Test #4.)

## Wave breakdown (dependency-ordered)

### Wave 1 — Anchor normalization (Axis II · Test #4) — INDEPENDENT, do first
- **Files**: `src/client/shared/Spaarke.Compose.Components/src/widgets/hooks/usePendingRedline.ts` (`resolveTargetSpans`/`buildCharIndex`); `.../widgets/ComposeEditor.tsx` (banner + selection fallback).
- **Change**: normalize BOTH the doc char-index and the LLM `target_text` before matching — smart/curly quotes → straight, NBSP (` `)→space, en/em/non-breaking dashes → hyphen, collapse runs of whitespace — keeping an offset map back to original positions so the redline lands on the right characters. If still unmatched AND the user has a selection, apply at the selection; else the honest banner.
- **Acceptance (closed set)**: (a) target differing only by smart-quotes/NBSP/dashes/whitespace MATCHES + places; (b) exact match unchanged; (c) `all`/`first`/`strict` count semantics + ambiguous(>1) unchanged; (d) genuinely-absent target + active selection → applies at selection; (e) no selection + no match → honest banner; (f) offsets correct (redline spans the intended chars).
- **Tests**: unit for normalization cases + selection fallback; existing 22 usePendingRedline tests green.

### Wave 2 — Session establishment on every door (Axis I: A + B) — CRUX
- **BFF**: `Api/Ai/ChatDocumentEndpoints.cs` (set `session.ActiveDocument` = upload identity {sessionFileId} on chat upload); `Models/Ai/Chat/ChatSession.cs` (`ActiveDocumentIdentity` source=upload if a new source value is needed). `SendWorkspaceArtifactHandler` already seeds `compose.upload` from `ActiveDocument`.
- **Client**: `ComposeWorkspace.tsx` + `ComposeWorkspace.types.ts` — browse-direct-upload establishes a real `state.sessionId` (route through the session-establishing upload path / `requestUploadMount`, NOT bare `mountTransient`). Invariant: **no mount without a session**.
- **Acceptance (closed set)**: (a) chat upload → `ActiveDocument` persisted w/ sessionFileId; (b) "open this file" → Compose loads the uploaded doc (not empty placeholder); (c) browse-direct-upload → `state.sessionId` non-empty; (d) Draft-alternative on a browse-uploaded doc routes as EDIT (documentSessionId non-empty) → DEF-12 confirmation + redline, NOT informational card; (e) stored-doc path unchanged.
- **Tests (wire-body)**: WebApplicationFactory — chat upload POST → assert `ActiveDocument` persisted + `/api/compose/upload` returns bytes; client test browse-mount sets sessionId; two-session edit-routing test (documentSessionId present end-to-end).

### Wave 3 — Open-in-Compose → artifact + tab-scoped ActiveDocument (Axis I: C + multi-tab) — after W2
- **Client**: `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` (`handleOpenInCompose`: if the session has an active artifact, open THAT via upload/active-document seed; seed message-text ONLY for genuine AI drafts); `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChatMessage.tsx` (affordance: don't seed raw prose on non-draft messages). **Tab-scoped ActiveDocument**: `ActiveDocument` follows the ACTIVE Compose tab (update on `tab_change`), so Assistant-typed edits target the viewed tab.
- **Acceptance (closed set)**: (a) "Open in Compose" on a revise/disambiguation message opens the SOURCE document, not the message prose; (b) genuine AI-drafted content still seeds a draft tab; (c) two Compose tabs → Assistant-typed edit targets the ACTIVE tab; (d) tab switch re-points ActiveDocument.
- **Tests (wire-body)**: open-in-compose→artifact seed; multi-tab active-document routing.

### Wave 4 — Context Trace auto-open (D) + context-mapping cleanup (F) — INDEPENDENT
- **Client**: `src/solutions/SpaarkeAi/src/components/context/ContextPaneController.tsx` — in the existing `usePaneEvent("workspace", tab_change)` handler, when the active tab is Compose (`widgetData.compose != null || layoutName === "Compose"`) call `setSelectedTool("execution-trace")`; add `setSelectedTool` to the dep array.
- **BFF**: `Services/Ai/Chat/StandaloneChatContextProvider.cs` — add `sprk_document` to the supported-types allowlist (or skip the fetch for document host) → kills the console 400 + enables playbook rec for document context.
- **Acceptance**: (a) activating a Compose tab auto-selects Execution Trace; (b) `sprk_document` context-mapping returns 200 (no console 400); (c) switching away from Compose doesn't strand the tool (decide: keep vs revert — default keep).
- **Tests**: unit tab_change→selectedTool; BFF test for sprk_document allowlist.

### Wave 5 — Compose first-class (Direct) widget + getVisibleState (additive/dual-use) — after W2/W3
- **Client**: `registerWorkspaceWidget('compose', metadata, () => import('@spaarke/compose-components').then(m => ({ default: m.ComposeWorkspace })), composeVisibility)` — ADDITIVE (keep the layout row, dropdown entry, single-tab reuse, standalone LW mount). Implement `getVisibleState(widgetData)` exposing the active-document identity object (the same serializable object shaped in W2/W3) as the sanctioned agent-visibility contract; retire/delegate the ad-hoc `composeActionBridge.registerActiveDocument` where `getVisibleState` covers it. Re-key single-tab-reuse if the widget path changes the discriminant.
- **Acceptance (closed set)**: (a) Compose registered as Direct widget + appears in picker (Daily Briefing/Calendar prove Direct widgets list — verify mechanism); (b) `getVisibleState` exposes active-document identity to the Assistant; (c) NO regression — layout mount, dropdown, standalone LW, single-tab reuse all still work; (d) ad-hoc active-doc bridge retired or delegated.
- **Tests**: widget registration + getVisibleState unit; regression on layout mount + tab reuse.

### Wave 6 — Non-docx reference-only guard (G) — INDEPENDENT, small
- **Client**: `ComposeEditor.tsx`/`ComposeWorkspace.tsx` — when a non-docx buffer reaches the editor (mammoth would throw), show an explicit "opened for reference, not editable in Compose" state instead of a silent empty editor. (Editable = DOCX only, by design; pdf/txt/md are chat-context only.)
- **Acceptance**: (a) pdf/txt opened in Compose → clear reference-only message, not blank; (b) docx still edits normally.
- **Tests**: unit non-docx guard.

## Cross-cutting (with the waves)
- Doc-drift fix: project `CLAUDE.md` FR-30 line ("core task 057 pending" → "057 shipped for chat path only; dispatched-action path unbuilt, #629").
- Update `notes/defer-issues.md` (DEF-10..19 round-3 → resolved/round-4) + GitHub issues.
- Verify the picker-registration mechanism for Direct widgets (correct the drifted doc §3.1).
- Each wave: FULL-rigor gate (code-review + adr-check) before commit. Consolidated gate + deploy after all waves. Then owner re-UAT → (on pass) merge branch → master.

## Order
Independent, any order: **W1, W4, W6.** Sequential core: **W2 → W3 → W5.** Recommended execution: W1 (safe warmup) → W2 → W3 → W4 → W6 → W5 (widget last, on stable session shape).
