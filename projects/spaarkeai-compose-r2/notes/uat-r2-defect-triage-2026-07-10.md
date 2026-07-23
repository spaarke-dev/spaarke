# Compose R2 — UAT defect triage + remediation design input (2026-07-10)

> Source: owner smoke test of the live AI toolbar (spaarkedev1). Seven reported items root-caused
> by three parallel investigations. This overturns the earlier "AI activation complete E2E" claim —
> the surface was unit-green / user-broken. Standing rule: **fix now, do not defer.**
> Each remediation carries an E2E Definition-of-Done (WAF / real-PaneEventBus forcing test), per the
> project's binding E2E DoD. Feeds `/project-pipeline`.

## The seven UAT items (verbatim owner report)
1. Uploaded file to the Compose workspace layout widget — loads.
2. Click Save → `Save failed: sessionId is required in the request body for the first-Save promotion rebind.` (`POST /api/compose/documents/create-on-save` → 400).
3. Highlight text → popup toolbar: (a) format/layout issues; (b) click AI action → JSON-looking message in Assistant; (c) nothing else happens.
4. Assistant "Summarize this document" → does NOT recognize the file uploaded directly into Compose.
5. Assistant upload file + "edit in the workspace Compose" → Compose tab opens but mounts the SAME (stale) file from step 1, not the newly-uploaded one.
6. After closing Compose tabs, Assistant "open the file in compose tab" → "I need the exact layout name or layout ID…".
7. Assistant upload another file + "edit in compose" → same "need layout name/id" message.

## Root causes (5 themes)

### R1 — Save 400 (server contract too strict) — items #2
- **Server** `POST /api/compose/documents/create-on-save` → `CreateOnSave`. Guard at `src/server/api/Sprk.Bff.Api/Api/ComposeEndpoints.cs:1312` rejects empty `sessionId`. Mirrored throws: `ComposeService.cs:265-266` (SaveAsync) and `:431-432` (PromoteIfEphemeralAsync).
- The rebind the sessionId drives (`RebindSessionDocumentIdAsync`, `ComposeService.cs:1070-1077`) is **already null-tolerant** — warns + returns null when session absent. SPE upload + `sprk_document` create + indexing all complete without a session.
- **Client** `ComposeWorkspace.tsx:855-862` sends `sessionId: state.sessionId`. Browse local-file path (`handleBrowseFileSelected`, `:1170-1197`) dispatches `mountTransient` without ever setting a sessionId → stays `''` (`ComposeWorkspace.types.ts:141`). Assistant-upload path DOES set it (`requestUploadMount`, `types.ts:192-197`) so that path is unaffected.
- **Fix (server):** relax `sessionId` to optional on transient-create; skip FR-07 rebind when absent (already tolerated). BFF-touching → publish-size verify + code-review sign-off (repo §10).
- **E2E DoD:** WebApplicationFactory test — transient create-on-save with empty/absent sessionId returns 200 + creates `sprk_document`; chat-launched path (with session) still rebinds.

### R2 — AI popup toolbar layout (client) — item #3a
- `ComposeAiToolbar.tsx:482-485` returns a Fragment whose first child is a bare `<ToolbarDivider/>` followed by a **second** `<Toolbar>`, mounted as direct children of the BubbleMenu flex row (`ComposeEditor.tsx:461-470` `styles.bubbleMenu`, toolbar at `:849-885`, AiToolbar at `:892-899`). Divider has no Toolbar context → collapses/misaligns; two Toolbars each apply own padding; no `flexWrap`/`maxWidth` → text-labelled buttons overflow.
- **Fix (client):** emit the AI actions as items INSIDE the existing BubbleMenu `<Toolbar>` (divider gets context, one flex/gap system) — or keep nested Toolbar but move divider inside + add `flexWrap`/`maxWidth` to `bubbleMenu`.
- **E2E DoD:** render/visual assertion — popup is a single aligned bar; buttons don't overflow the popup width.

### R3 — AI result renders as raw JSON + "nothing happens" (client) — item #3b/#3c
- Path: `ComposeAiToolbar.handleActionClick` (`:463-465`) → `composeActionBridge` → `ConversationPane.dispatchComposeAction` (`ConversationPane.tsx:196-207`) → `dispatchConsumer` → `formatEventOutputMarkdown(dispatched.result)` → chat.
- `formatEventOutputMarkdown` (`DocumentUploadedEventStream.ts:218-232`) only understands the summarize schema (`tldr`/`summary`/`keywords`); when both null it dumps ` ```json … ``` `. Compose actions return other schemas (e.g. `compose-explain-clause` → `{explanation, keyConcepts, relatedPlaybookIds}`, `infra/dataverse/outputschemas/compose-explain-clause.schema.json`) → JSON fence. `makeLocalAssistantMessage` tags `responseType:"markdown"` → code block.
- "Nothing else happens": `dispatchConsumer.ts:487-527` also bridges the terminal result onto the **workspace** PaneEventBus channel (`section_started`/`section_completed`), which in Compose is the editor — no section renderer subscribed → no visible output.
- **Fix (client):** add a Compose-aware formatter keyed on `bindingId`/action id mapping each of the 5 action schemas → prose; keep the generic JSON fence only as a last-resort for genuinely unknown shapes. Decide intended destination for the workspace-channel bridge in Compose (likely suppress or route to the Q&A highlight, not the editor section renderer).
- **E2E DoD:** real-PaneEventBus test — dispatch `compose-explain-clause` result → chat message is prose (explanation + key concepts), not a JSON code block.

### R4 — Missing chat↔Compose document-identity bridge (both) — items #4, #5
Four disjoint identity spaces; only connective tissue is the LLM choosing `documentId` vs `sessionFileId`:
1. Chat-uploaded → `ChatSessionFile{FileId, ExtractedText, …}` + bytes in `ITenantCache` `doc-upload-binary` keyed `(sessionId,fileId)` (`ChatSession.cs:343`, `ComposeEndpoints.cs:990-999`).
2. Compose stored → `sprk_document` + SPE pointer.
3. Compose direct/Browse upload → **client-only** FileReader bytes → `mountTransient`, never touches BFF, never a `ChatSessionFile`, never indexed (`ComposeWorkspace.tsx:1170-1197`).
4. Compose transient upload-mount → served from space #1 cache via `POST /api/compose/upload` keyed `(chatSessionId, sessionFileId)` (`ComposeWorkspace.tsx:1207-1280`).
- **Defect 4** = Compose→chat direction has **no bridge** — client-only bytes are invisible to the session; "summarize this document" reads only server-side `ChatSessionFile`/RAG (`ChatSession.cs:352-371`).
- **Defect 5** = chat→Compose plumbing is intact (`SendWorkspaceArtifactHandler.ExecuteOpenWorkspaceTabAsync` `:381-542` → SSE `workspace_open_tab` → `useContextEventBridge.ts:96-122` → `WorkspacePane.tsx:761-778` addTab → `WorkspaceLayoutWidget.tsx:283` → `main.tsx:247-304` `ComposeLaunchContext` → `composeEditor.registration.ts:186-202`), but **document identity is LLM-guessed** across two competing pointer types → grabs stale/wrong file. Handler's own comment (`:678-684`) acknowledges the pointer ambiguity.
- **Fix (both):** (a) when Compose mounts a transient/direct upload, register it with the active chat session (push bytes to `doc-upload-binary` + create `ChatSessionFile`, or emit a PaneEventBus event ConversationPane consumes) so chat can resolve it; (b) resolve the chat→Compose mount target server-side from an explicit shared session-scoped **active-document identity** (e.g. most-recent session upload / deterministic current-attachment binding) instead of trusting LLM `documentId`/`sessionFileId` choice.
- **E2E DoD:** (a) WAF + bus test — upload in Compose → chat "summarize" resolves that document; (b) WAF test — "edit in Compose" mounts the just-uploaded file, not a stale stored one.

### R5 — "need a layout name/id" (server tool-schema) — items #6, #7
- Message is LLM-generated, paraphrasing a hard tool requirement. `SendWorkspaceArtifactHandler.ValidateChat` (`:248-261`) + `ExecuteOpenWorkspaceTabAsync` (`:443-451`) require `layoutName` OR `layoutId`; **no server default of "Compose."** Only soft prose in the tool Description (`:144`). Literal-following model asks the user. Aggravated in 6/7 because after tab-close / fresh upload the model has also lost the doc pointer.
- **Fix (server):** default `layoutName="Compose"` when intent is Compose, or add a dedicated zero-ambiguity `open_compose_tab` tool (layout implicit). Handler already resolves correctly when supplied (`:459-461`).
- **E2E DoD:** WAF test — open-tab tool call with no layout arg defaults to Compose and mounts.

## Unifying insight
R4 + R5 are three faces of the same missing **shared session-scoped active-document identity**. R1/R2/R3 are contained fixes. Recommend R4 as the design anchor (BFF + client + chat tool) with R5 folded in (both are "resolve chat→Compose intent server-side, not by LLM args").

## Placement / hot-path note
- BFF-touching: R1, R5, and R4(a) server side → `<hot-path-declaration>` BFF=Y; publish-size verify + CVE + code-review per repo §10; run `/conflict-check` vs redesign-r2 (shared `SendWorkspaceArtifactHandler.cs` / `ComposeEndpoints.cs` / `ChatSession.cs`).
- Client-only: R2, R3, R4(a) client side.
- Coordinate with redesign-r2 before touching `SendWorkspaceArtifactHandler.cs` / chat-session model (shared surface).

## Key files
- `src/server/api/Sprk.Bff.Api/Api/ComposeEndpoints.cs`
- `src/server/api/Sprk.Bff.Api/Services/Compose/ComposeService.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/SendWorkspaceArtifactHandler.cs`
- `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/ChatSession.cs`
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.tsx`
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeAiToolbar.tsx`
- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeEditor.tsx`
- `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx`
- `src/solutions/SpaarkeAi/src/main.tsx`, `.../conversation/useContextEventBridge.ts`, `.../workspace/WorkspacePane.tsx`
- `src/solutions/LegalWorkspace/src/sections/composeEditor.registration.ts`
