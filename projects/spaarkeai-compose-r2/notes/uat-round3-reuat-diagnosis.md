# UAT round-3 RE-UAT diagnosis (2026-07-12) — Assistant ↔ mounted Compose tab

> Owner re-UAT of the deployed round-3 surface surfaced 4 tests + 2 console signals. Five read-only
> investigations pinned the root causes. **Nothing built yet; master merge HELD.** Owner wants the
> fix generalized as an "Active Workspace Artifact" contract (Assistant interacting with a mounted
> Workspace tab), not four point-patches. Awaiting owner steer on generalization scope + anchor fallback.

## Two independent axes (the key reframe)
- **Axis I — Identity**: does the mounted tab establish a document session the Assistant can target? (Tests #1/#2/#3 — upload doors fail this.)
- **Axis II — Anchoring**: even WITH a correct session, does the edit anchor to the tab's actual current content? (Test #4 — matcher too brittle.)
Both must hold. Fixing Axis I alone turns #3 into #4.

## Root causes (file:line confirmed)

### Test #1 — revise → "Open in Compose" seeds the chat message, not the file
- Affordance is hardwired to seed `message.content` as draft HTML: `SprkChatMessage.tsx:745` (`onOpenInCompose(message.content)`) → `ConversationPane.tsx:155-169` (`handleOpenInCompose` → `assistantTextToDraftHtml` → `compose.draft.html`). Only destination is the inline-HTML seed branch `ComposeWorkspace.tsx:1502-1508`.
- Gated on ANY completed assistant message (`SprkChatMessage.tsx:703`), so the revise-disambiguation prose (`composeReviseRouting.ts:63-67`, injected `ConversationPane.tsx:421`) gets it.
- Second layer: there is no session `ActiveDocument` to open instead (see #2).

### Test #2 — "open this file" (chat upload) → empty Compose tab
- Chat upload retains bytes + appends `ChatSessionFile` (`ChatDocumentEndpoints.cs:437-591`) but **never sets `session.ActiveDocument`**. Only writer is `POST /api/compose/active-document` (`ComposeEndpoints.cs:1675`), invoked only from ComposeWorkspace's own Browse/stored mounts.
- So `SendWorkspaceArtifactHandler` fallback `ResolveActiveDocumentAsync` returns null → bare tab `widgetData={layoutId,layoutName}` (`:621-628`) → `state.status==='empty'` → placeholder (`ComposeEmptyState.tsx:214`).
- Client transient-upload mount path already exists end-to-end (`initialUploadRef` → `POST /api/compose/upload {sessionId, sessionFileId}` → `mountTransient`); simply never triggered.

### Test #3 — browse-direct-upload into Compose → Draft-alternative shows prose card, no redline
- Browse path `handleBrowseFileSelected` → `mountTransient` (`ComposeWorkspace.tsx:1341`); reducer `mountTransient` (`ComposeWorkspace.types.ts:208-221`) **never sets `sessionId`** → stays `''` (`INITIAL_STATE`, `:149`).
- Toolbar threads `documentSessionId: ''` (`ComposeAiToolbar.tsx:543`); dispatcher guard `documentSessionId.length>0` false (`ConversationPane.tsx:317-318`) → edit reclassified INFORMATIONAL → chat session + full prose card (`composeResultFormat.ts:130-147`), DEF-12 confirmation skipped, no Flow-5 ledgerRef.
- Even with a ledger write, `materializeComposeDraftFromLedger` aborts on empty `state.sessionId` (`ComposeWorkspace.tsx:1076-1077`).
- Contrast: stored load sets sessionId (`loadSucceeded`, types `:181`); assistant-upload/draft sets it (`requestUploadMount`, types `:198-207`). Browse is the one door that doesn't. DEF-09 previously "passed" because it was tested on stored docs.

### Test #4 — stored doc → DEF-09/DEF-12 fire correctly BUT "target text was not found"
- **Not a routing bug** — confirmation + accept/reject worked (correct DEF-12). Failure is client anchoring.
- `resolveTargetSpans` (`usePendingRedline.ts:169-189`) uses raw `String.indexOf` (`:174/:179`) with **zero normalization**; `not_found` at `:182`; banner at `ComposeEditor.tsx:1103`. Fuzzy explicitly deferred (header `:20-21`).
- Grounding is consistent (LLM sees `textBetween(from,to,' ')` `ComposeAiToolbar.tsx:505`; editor searches same single-space block joins). The divergence is **punctuation**: stored DOCX has smart quotes / NBSP / en-em dashes; LLM straightens them in echoed `target_text` → exact match fails. Fresh-typed docs match (straight quotes).
- **Fix = normalize both sides** (smart→straight quotes, NBSP→space, dash fold, whitespace collapse) in `resolveTargetSpans`, keeping offset map; optional fallback to apply at the user selection.

### Console: `sprk_document not supported` (context mapping) — RED HERRING
- Expected 400: `StandaloneChatContextEndpoints.cs:117-125`; allowlist `StandaloneChatContextProvider.cs:63-71` = {contact,account,opportunity,incident,sprk_matter}. Client catch `AiSessionProvider.tsx:418-421`.
- Impact: only suppresses a playbook recommendation. Does NOT affect chat/context session binding (`chatSessionId` from `chatSessionKeyForContext`, independent) or redline. Optional cleanup (add sprk_document to allowlist or skip fetch) to kill console noise.

### Trace auto-open (owner ask)
- Context "Execution Trace" view exists (task 064); `selectedTool==='execution-trace'` renders `ComposeTraceHost` (`ContextPaneController.tsx:775`). Lever = `setSelectedTool('execution-trace')` (`useContextTool.ts`).
- Integration point: existing `usePaneEvent('workspace', tab_change)` handler (`ContextPaneController.tsx:564-615`); when active tab is Compose (`widgetData.compose!=null || layoutName==='Compose'`), set the tool. Add `setSelectedTool` to dep array. Design choice: on tab-open vs first-interaction; auto-revert or not.

## Proposed fix plan (pending owner alignment)
- **Axis I (identity)** — "Active Workspace Artifact" contract, Compose kind only for now:
  - A: chat upload sets `session.ActiveDocument` (BFF `ChatDocumentEndpoints`) → #2 loads, unblocks #1.
  - B: browse-direct-upload establishes a real session (route through session-establishing upload path) → #3.
  - C: "Open in Compose" opens the active artifact when present, message-text seed only for genuine AI drafts → #1.
- **Axis II (anchoring)** — E: normalize `target_text` matching + selection fallback (`usePendingRedline.ts`) → #4.
- **UX** — D: Context auto-opens Execution Trace on Compose activity.
- **Cleanup** — F: add `sprk_document` to context-mapping allowlist (or skip) → kill console 400.
- Each Axis-I/II fix carries a wire-body/E2E test per project E2E DoD.

## Open questions for owner
1. Generalization scope: general contract SHAPE (kind/source/content-handle), Compose kind implemented now only — or design a 2nd tab kind's Assistant interaction now?
2. Anchor-failure fallback order: normalize → apply at selection → honest banner. Confirm.

## Investigations (read-only, this session)
Test#1 affordance/seed · Test#2 upload→Compose load path · trace auto-open · Test#3 draft-alt redline routing · Test#4 target_text placement + context-mapping. All root causes file:line-confirmed above.
