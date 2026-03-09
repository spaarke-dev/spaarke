# End-to-End Smoke Test Plan -- SprkChat Interactive Collaboration (R2)

> **Created**: 2026-02-26
> **Task**: R2-147 (End-to-End Smoke Test on Deployed Environment)
> **Status**: Ready for Manual Execution
> **Estimated Duration**: 4-6 hours

---

## Prerequisites

### Environment

| Resource | URL / Name |
|----------|------------|
| Dataverse (Dev) | `https://spaarkedev1.crm.dynamics.com` |
| BFF API (Dev) | `https://spe-api-dev-67e2xz.azurewebsites.net` |
| BFF Health Check | `GET https://spe-api-dev-67e2xz.azurewebsites.net/healthz` |
| Azure OpenAI | `https://spaarke-openai-dev.openai.azure.com/` |
| Azure AI Search | `https://spaarke-search-dev.search.windows.net/` |

### Deployment Verification (Pre-Smoke)

Before running smoke tests, confirm all deployments from Tasks 143-146 are complete:

- [ ] **Task 143**: SprkChatPane Code Page deployed to Dataverse (`sprk_SprkChatPane` web resource published)
- [ ] **Task 144**: AnalysisWorkspace Code Page deployed to Dataverse (`sprk_AnalysisWorkspace` web resource published)
- [ ] **Task 145**: BFF API deployed with all R2 endpoints (health check returns 200)
- [ ] **Task 146**: Dataverse schema changes deployed (Playbook capability multi-select field exists)

### Test Data Requirements

| Entity | Requirement | Notes |
|--------|-------------|-------|
| Matter record | At least 1 test Matter with documents | Use designated test records only (ADR-015) |
| Project record | At least 1 test Project linked to a Matter | Must have associated documents |
| Analysis record | At least 1 test Analysis with completed analysis output | Must have working document content in editor |
| Playbook records | At least 2 playbooks: one with full capabilities, one with limited ("Quick Q&A") | Capability field must be populated |
| SPE documents | At least 2 documents accessible via SPE | For multi-document context testing |

### Browser & Tools

- [ ] Microsoft Edge (latest) or Google Chrome (latest)
- [ ] Browser DevTools open (Console + Network tabs)
- [ ] Dataverse dev user account with sufficient privileges
- [ ] Dark mode toggle accessible (OS-level or Dataverse theme setting)

---

## Test Execution Instructions

**For each test scenario:**
1. Execute the steps in order
2. Mark each step with the result: PASS / FAIL / BLOCKED / SKIP
3. If FAIL: capture screenshot, note the error in the "Failure Notes" field
4. If BLOCKED: note the blocker and skip dependent steps
5. Record the date and tester initials next to the result

**Severity Guide:**
- **P0 (Critical)**: Core functionality broken -- blocks sign-off
- **P1 (Major)**: Feature degraded but workaround exists
- **P2 (Minor)**: Cosmetic or non-blocking issue

---

## Scenario 1: SprkChatPane Side Pane Launch (Package A)

**Priority**: P0
**Prerequisites**: Task 143 deployed, test Matter record available
**Estimated Time**: 15 minutes

| # | Step | Expected Result | Pass/Fail | Notes |
|---|------|-----------------|-----------|-------|
| 1.1 | Navigate to Dataverse > Matter form > open a test Matter record | Matter form loads | | |
| 1.2 | Click the SprkChat side pane ribbon button (or command bar button) | Side pane opens on the right side of the form | | |
| 1.3 | Verify SprkChatPane Code Page loads inside the side pane | SprkChat UI renders: chat input, message area, header with context | | |
| 1.4 | Open browser DevTools > Console. Check for errors | No JavaScript errors related to SprkChatPane | | |
| 1.5 | Verify context auto-detection: header or context indicator shows `entityType=sprk_matter` and the correct `entityId` matching the open Matter record | Context matches the open record | | |
| 1.6 | Verify authentication: Network tab shows successful token acquisition (no 401/403 errors) | Auth tokens acquired via `Xrm.Utility.getGlobalContext()` | | |
| 1.7 | Type a message in the chat input (e.g., "Hello, what can you help me with?") and press Enter or click Send | Message appears in chat history as user message | | |
| 1.8 | Observe the AI response | SSE connection established (visible in Network tab as EventStream), tokens stream in progressively, response completes with a `done` event | | |
| 1.9 | Navigate to a different Matter record while the side pane remains open | Side pane stays open, context updates to the new record, previous chat session is preserved or gracefully transitioned | | |
| 1.10 | Close the side pane and reopen it | Side pane opens cleanly with no stale state or errors | | |

**Failure Notes**: _______________________________________________________________

---

## Scenario 2: SprkChatPane on Project and Analysis Forms (Package A)

**Priority**: P0
**Prerequisites**: Task 143 deployed, test Project and Analysis records available
**Estimated Time**: 10 minutes

| # | Step | Expected Result | Pass/Fail | Notes |
|---|------|-----------------|-----------|-------|
| 2.1 | Navigate to a test Project record | Project form loads | | |
| 2.2 | Launch SprkChat side pane from the Project form | Side pane opens, SprkChat loads | | |
| 2.3 | Verify context shows `entityType=sprk_project` with the correct `entityId` | Context matches the Project record | | |
| 2.4 | Send a chat message and verify streaming response works | Streaming response received successfully | | |
| 2.5 | Navigate to a test Analysis record | Analysis form loads | | |
| 2.6 | Launch SprkChat side pane from the Analysis form | Side pane opens, SprkChat loads | | |
| 2.7 | Verify context shows `entityType=sprk_analysis` with the correct `entityId` | Context matches the Analysis record | | |
| 2.8 | Open browser DevTools > Console. Verify `BroadcastChannel` connection is established (look for `sprk-workspace-*` channel messages) | BroadcastChannel connected for cross-pane communication with AnalysisWorkspace | | |

**Failure Notes**: _______________________________________________________________

---

## Scenario 3: AnalysisWorkspace Code Page Launch (Package C)

**Priority**: P0
**Prerequisites**: Task 144 deployed, test Analysis record with completed analysis output
**Estimated Time**: 15 minutes

| # | Step | Expected Result | Pass/Fail | Notes |
|---|------|-----------------|-----------|-------|
| 3.1 | Navigate to a test Analysis record | Analysis form loads | | |
| 3.2 | Click "Open Workspace" button (or equivalent launcher) | AnalysisWorkspace Code Page opens as an 85% modal dialog via `Xrm.Navigation.navigateTo()` | | |
| 3.3 | Verify the 2-panel layout renders: Editor panel (left) + Source Document Viewer (right) | Both panels visible, properly sized, no layout overflow | | |
| 3.4 | Verify the analysis document content loads in the RichTextEditor (left panel) | Analysis output text renders correctly in the editor with formatting preserved | | |
| 3.5 | Verify the source document viewer loads (right panel) | Source document renders or a "No source document" message displays if none is linked | | |
| 3.6 | Verify the toolbar is present with Save, Export, Undo/Redo buttons | All toolbar buttons render and are clickable | | |
| 3.7 | Open DevTools > Console. Check for errors | No JavaScript errors related to AnalysisWorkspace | | |
| 3.8 | Verify the Code Page authenticated independently (no auth tokens in BroadcastChannel messages) | Network tab shows independent token acquisition; no auth payloads in BroadcastChannel | | |
| 3.9 | Verify the load time: from click to interactive should be < 2 seconds | Measured load time: ___ seconds | | |
| 3.10 | Close the workspace dialog and reopen it | Dialog opens cleanly with no stale state | | |

**Failure Notes**: _______________________________________________________________

---

## Scenario 4: Streaming Write Flow (Package B)

**Priority**: P0
**Prerequisites**: AnalysisWorkspace open with loaded document, SprkChat side pane open on same Analysis record
**Estimated Time**: 20 minutes

| # | Step | Expected Result | Pass/Fail | Notes |
|---|------|-----------------|-----------|-------|
| 4.1 | With AnalysisWorkspace and SprkChat side pane both open on the same Analysis record, verify BroadcastChannel is connected | Console shows channel connection established between both panes | | |
| 4.2 | In SprkChat, type a message that triggers a document write (e.g., "Add a new section summarizing the key financial risks") and send | Message sent, SSE stream begins | | |
| 4.3 | Observe SSE events in Network tab | `document_stream_start` event received, followed by `document_stream_token` events, ending with `document_stream_end` | | |
| 4.4 | Watch the RichTextEditor in AnalysisWorkspace | Tokens appear progressively in the editor (character-by-character or chunk-by-chunk insertion) | | |
| 4.5 | Verify streaming latency: time from SSE token event to editor insertion | Per-token latency < 100ms (NFR-01) | | |
| 4.6 | Wait for the streaming write to complete (`document_stream_end` event) | Final content in editor is correct and well-formatted | | |
| 4.7 | Verify the document state was snapshotted before the write (check undo availability) | Undo button is enabled; clicking Undo restores the pre-stream state | | |
| 4.8 | Redo the change | Redo restores the AI-written content | | |

**Failure Notes**: _______________________________________________________________

---

## Scenario 5: Streaming Write Cancellation (Package B)

**Priority**: P0
**Prerequisites**: Same setup as Scenario 4
**Estimated Time**: 10 minutes

| # | Step | Expected Result | Pass/Fail | Notes |
|---|------|-----------------|-----------|-------|
| 5.1 | In SprkChat, trigger a streaming write request (e.g., "Rewrite the entire introduction with more detail") | Streaming begins, tokens appear in editor | | |
| 5.2 | While tokens are still streaming, click the Cancel button in SprkChat | Streaming stops | | |
| 5.3 | Verify partial content remains in the editor | Partial content from the stream is visible and intact (not corrupted) | | |
| 5.4 | Verify Undo is available | Clicking Undo restores the editor to its pre-stream state (removing partial content) | | |
| 5.5 | Verify the editor is not in a corrupted state: type text manually | Manual text input works normally after cancellation | | |

**Failure Notes**: _______________________________________________________________

---

## Scenario 6: Selection-Based Revision (Package G)

**Priority**: P1
**Prerequisites**: AnalysisWorkspace open with document content, SprkChat side pane open
**Estimated Time**: 15 minutes

| # | Step | Expected Result | Pass/Fail | Notes |
|---|------|-----------------|-----------|-------|
| 6.1 | In the AnalysisWorkspace editor, select a paragraph of text by clicking and dragging | Text is highlighted in the editor | | |
| 6.2 | Observe the SprkChat side pane | HighlightRefine toolbar or refinement UI appears in SprkChat, showing the selected text context | | |
| 6.3 | Verify the selection was transmitted via BroadcastChannel (check DevTools Console for `selection_changed` event) | `selection_changed` event visible in Console with correct selection data | | |
| 6.4 | In SprkChat's refinement UI, type an instruction (e.g., "Make this more concise and formal") and submit | Revision request sent to BFF API | | |
| 6.5 | Observe the response | Revised text streams back via SSE | | |
| 6.6 | Verify the DiffCompareView appears (since this is a revision, not an addition) | Side-by-side or inline diff shows the original selected text vs. the revised text | | |
| 6.7 | Click "Accept" in the DiffCompareView | Revised text replaces the original selection in the editor | | |
| 6.8 | Undo the change | Original text is restored | | |
| 6.9 | Repeat steps 6.1-6.5, but this time click "Reject" in the DiffCompareView | Original text remains unchanged in the editor | | |

**Failure Notes**: _______________________________________________________________

---

## Scenario 7: Action Menu / Command Palette (Package D)

**Priority**: P0
**Prerequisites**: SprkChat side pane open with a playbook that has full capabilities
**Estimated Time**: 15 minutes

| # | Step | Expected Result | Pass/Fail | Notes |
|---|------|-----------------|-----------|-------|
| 7.1 | In SprkChat's chat input, type `/` as the first character | Action menu / command palette opens within 200ms (NFR: < 200ms) | | |
| 7.2 | Verify the menu shows categorized actions | Actions are grouped by category (e.g., Playbooks, Actions, Search, Settings) | | |
| 7.3 | Verify actions are filtered by the active playbook's capabilities | Only actions matching the current playbook's capability declarations are shown | | |
| 7.4 | Type additional characters to filter (e.g., `/sum`) | Menu filters to show matching actions (e.g., "Summarize") | | |
| 7.5 | Use arrow keys to navigate the menu | Focus moves between actions, highlighted action is visually distinct | | |
| 7.6 | Press Enter to select a highlighted action | Action is executed: the menu closes, and the appropriate command is triggered | | |
| 7.7 | Press Escape while the menu is open | Menu closes without executing any action; chat input is cleared or restored | | |
| 7.8 | Switch to a playbook with limited capabilities (e.g., "Quick Q&A" playbook) | Playbook switches successfully | | |
| 7.9 | Type `/` again | Action menu opens but shows only the limited set of actions matching the new playbook's capabilities (e.g., no write-back, no re-analysis) | | |
| 7.10 | Verify the actions list was fetched from `GET /api/ai/chat/actions` (check Network tab) | API call returns 200 with context-sensitive action list | | |

**Failure Notes**: _______________________________________________________________

---

## Scenario 8: Re-Analysis Pipeline (Package E)

**Priority**: P0
**Prerequisites**: AnalysisWorkspace open with a completed analysis, SprkChat side pane open
**Estimated Time**: 15 minutes

| # | Step | Expected Result | Pass/Fail | Notes |
|---|------|-----------------|-----------|-------|
| 8.1 | In SprkChat, trigger a re-analysis action (via `/reanalyze` command or action menu, or by typing "Re-analyze this document focusing on financial risks") | Re-analysis request sent to BFF API | | |
| 8.2 | Observe the AnalysisWorkspace editor | A progress overlay / progress indicator appears showing percent complete | | |
| 8.3 | Verify SSE events in Network tab | `progress` events with increasing percentage values are received | | |
| 8.4 | Wait for re-analysis to complete | Progress reaches 100%, overlay disappears | | |
| 8.5 | Verify the editor content is replaced with the new analysis | New analysis content replaces the previous content in the editor | | |
| 8.6 | Verify the previous version was pushed to the undo stack | Undo button is enabled; clicking Undo restores the previous analysis | | |
| 8.7 | Redo to bring back the new analysis | New analysis content restored | | |
| 8.8 | Verify that re-analysis respected the additional instructions provided (content reflects "financial risks" focus) | Content thematically matches the requested focus | | |

**Failure Notes**: _______________________________________________________________

---

## Scenario 9: Diff Compare View (Package F)

**Priority**: P1
**Prerequisites**: AnalysisWorkspace open, ability to trigger a revision (not addition) write
**Estimated Time**: 15 minutes

| # | Step | Expected Result | Pass/Fail | Notes |
|---|------|-----------------|-----------|-------|
| 9.1 | In SprkChat, request a revision of existing content (e.g., "Rewrite the conclusion to be more concise") | Revision request triggers diff mode (not live streaming) | | |
| 9.2 | Verify the DiffCompareView component appears | Side-by-side (or inline) diff view showing original content on the left and revised content on the right | | |
| 9.3 | Verify diff highlighting: additions shown in green, deletions shown in red | Visual diff markers clearly distinguish changes | | |
| 9.4 | Click "Accept" button | Revised content replaces the original in the editor; DiffCompareView closes | | |
| 9.5 | Trigger another revision request | DiffCompareView appears again | | |
| 9.6 | Click "Reject" button | Original content preserved; DiffCompareView closes; no changes to editor | | |
| 9.7 | Trigger another revision request | DiffCompareView appears again | | |
| 9.8 | Click "Edit" button (if available) | DiffCompareView enters an editable state where the user can manually modify the revised content before accepting | | |
| 9.9 | Verify automatic mode selection: request an addition (e.g., "Add a new section about compliance") | Addition triggers live streaming mode (not diff view) | | |
| 9.10 | Verify automatic mode selection: request a rewrite (e.g., "Rewrite paragraph 2") | Rewrite triggers diff review mode | | |

**Failure Notes**: _______________________________________________________________

---

## Scenario 10: Suggestions and Citations (Package H)

**Priority**: P1
**Prerequisites**: SprkChat side pane open, connected to a document context with knowledge base content
**Estimated Time**: 15 minutes

| # | Step | Expected Result | Pass/Fail | Notes |
|---|------|-----------------|-----------|-------|
| 10.1 | Send a chat message that would trigger knowledge-based response (e.g., "Summarize the key findings from the attached documents") | AI response streams and completes | | |
| 10.2 | After the response completes, look for suggestion chips below the assistant message | 2-3 contextual follow-up suggestion chips appear (e.g., "What are the risk factors?", "Explain the financial impact") | | |
| 10.3 | Verify suggestions are contextually relevant to the response content | Suggestion text relates to the document/analysis context | | |
| 10.4 | Click one of the suggestion chips | The suggestion text is populated into the chat input and automatically sent as the next message | | |
| 10.5 | Verify the new message triggers a response | AI responds to the suggested follow-up question | | |
| 10.6 | Look for citation markers in the AI response (e.g., `[1]`, `[2]`) | Superscript citation markers are visible inline with the response text | | |
| 10.7 | Click a citation marker (e.g., `[1]`) | A popover appears showing: source document name, page number (if applicable), and a relevant excerpt from the source | | |
| 10.8 | Verify the citation popover has a link to the source document | Clicking the link navigates to or opens the source document | | |
| 10.9 | Close the citation popover | Popover closes, chat remains in place | | |
| 10.10 | Verify SSE events: check for `suggestions` and `citations` event types in Network tab | Both event types present in the SSE stream | | |

**Failure Notes**: _______________________________________________________________

---

## Scenario 11: Web Search (Package I)

**Priority**: P1
**Prerequisites**: SprkChat side pane open, Azure Bing Search API provisioned (check PH-088 status)
**Estimated Time**: 10 minutes

| # | Step | Expected Result | Pass/Fail | Notes |
|---|------|-----------------|-----------|-------|
| 11.1 | In SprkChat, send a query that would benefit from web search (e.g., "What are the latest SEC regulations on ESG disclosures?") | Request sent to BFF API; web search tool invoked | | |
| 11.2 | Verify the response includes web-sourced information | Response contains information sourced from the web (not just internal documents) | | |
| 11.3 | Verify citations include web sources | Citation markers reference web URLs, not just internal documents | | |
| 11.4 | Click a web citation | Popover shows the web source URL, title, and excerpt | | |
| 11.5 | Verify the web source link is clickable and opens in a new tab | New browser tab opens with the web source page | | |

> **NOTE**: If PH-088 (Bing API) is unresolved, mark all steps as BLOCKED with note "Azure Bing Search API not provisioned."

**Failure Notes**: _______________________________________________________________

---

## Scenario 12: Dark Mode (ADR-021)

**Priority**: P1
**Prerequisites**: All components deployed, OS or Dataverse dark mode toggle available
**Estimated Time**: 20 minutes

| # | Step | Expected Result | Pass/Fail | Notes |
|---|------|-----------------|-----------|-------|
| 12.1 | Enable dark mode (OS-level dark theme or Dataverse dark mode setting) | System switches to dark theme | | |
| 12.2 | Open the SprkChat side pane | Side pane renders in dark mode: dark background, light text, no white flashes during load | | |
| 12.3 | Send a chat message and observe the response | Chat bubbles, text, and streaming indicators use dark theme colors | | |
| 12.4 | Type `/` to open the action menu | Action menu renders in dark mode: dark background, readable text, proper contrast | | |
| 12.5 | Open the AnalysisWorkspace Code Page | Workspace dialog renders in dark mode: dark background, both panels themed | | |
| 12.6 | Verify the RichTextEditor panel in dark mode | Editor background is dark, text is light, toolbar icons are visible | | |
| 12.7 | Verify the Source Document Viewer in dark mode | Viewer adapts to dark theme appropriately | | |
| 12.8 | Trigger a streaming write and observe in dark mode | Streaming text inserts with appropriate dark theme styling | | |
| 12.9 | Trigger a diff compare view in dark mode | Diff additions (green) and deletions (red) are clearly visible against dark background | | |
| 12.10 | Verify suggestion chips in dark mode | Chips are readable with proper contrast against dark background | | |
| 12.11 | Verify citation popovers in dark mode | Popover background, text, and links are all readable in dark mode | | |
| 12.12 | Verify no hard-coded colors (ADR-021): inspect elements via DevTools | All colors use Fluent v9 design tokens, no inline `color:` or `background:` with hex/rgb values | | |
| 12.13 | Toggle back to light mode and verify all components switch cleanly | All components return to light theme without page reload | | |

**Failure Notes**: _______________________________________________________________

---

## Scenario 13: Cross-Pane Communication (Package A + C)

**Priority**: P0
**Prerequisites**: Both SprkChatPane and AnalysisWorkspace open on the same Analysis record
**Estimated Time**: 15 minutes

| # | Step | Expected Result | Pass/Fail | Notes |
|---|------|-----------------|-----------|-------|
| 13.1 | Open an Analysis record, launch both SprkChat side pane and AnalysisWorkspace Code Page | Both panes are open and connected via BroadcastChannel | | |
| 13.2 | In DevTools Console, verify BroadcastChannel `sprk-workspace-*` messages are flowing | Channel messages logged in console (if debug logging is enabled) | | |
| 13.3 | Trigger a streaming write from SprkChat | `document_stream_start/token/end` events flow from SprkChat to AnalysisWorkspace; tokens appear in editor | | |
| 13.4 | Select text in the AnalysisWorkspace editor | `selection_changed` event flows from AnalysisWorkspace to SprkChat; refinement UI appears | | |
| 13.5 | Close the AnalysisWorkspace dialog while SprkChat side pane remains open | SprkChat handles the disconnection gracefully (no errors, BroadcastChannel cleanup) | | |
| 13.6 | Send a chat message in SprkChat after the workspace is closed | Chat still works for non-document operations; document write operations are appropriately disabled or show an informative message | | |
| 13.7 | Reopen the AnalysisWorkspace | BroadcastChannel reconnects; cross-pane communication resumes | | |
| 13.8 | Navigate to a different Analysis record while both panes are open | Context changes: `context_changed` event fires, both panes update to the new record or handle the transition gracefully | | |

**Failure Notes**: _______________________________________________________________

---

## Scenario 14: Error Handling and Edge Cases

**Priority**: P1
**Prerequisites**: All components deployed
**Estimated Time**: 15 minutes

| # | Step | Expected Result | Pass/Fail | Notes |
|---|------|-----------------|-----------|-------|
| 14.1 | Temporarily disable network connectivity (go offline), then send a chat message | User-friendly error message displayed (not a raw stack trace); ProblemDetails format in API response | | |
| 14.2 | Restore network and retry | Chat works normally after reconnection | | |
| 14.3 | Open SprkChat on a form with no associated playbook | Graceful handling: default playbook loaded, or informative message shown | | |
| 14.4 | Attempt a streaming write when the AnalysisWorkspace is not open | Appropriate feedback: "Open the Analysis Workspace to enable document editing" or similar | | |
| 14.5 | Send an extremely long message (> 5000 characters) | Message is handled gracefully (either truncated with notice or accepted) | | |
| 14.6 | Rapidly send multiple messages without waiting for responses | Messages are queued or subsequent sends are throttled with user feedback | | |
| 14.7 | Open SprkChat side pane on a record type that has no playbook configured | Graceful fallback behavior; no crash; informative UI state | | |

**Failure Notes**: _______________________________________________________________

---

## Scenario 15: Performance Benchmarks

**Priority**: P1
**Prerequisites**: All components deployed, browser DevTools Performance tab available
**Estimated Time**: 15 minutes

| # | Metric | Target | Measured | Pass/Fail |
|---|--------|--------|----------|-----------|
| 15.1 | SprkChatPane side pane load time (from button click to interactive) | < 2 seconds (NFR-02) | ___ ms | |
| 15.2 | AnalysisWorkspace Code Page load time (from button click to content rendered) | < 2 seconds | ___ ms | |
| 15.3 | Action menu open latency (from `/` keypress to menu visible) | < 200ms (FR-10) | ___ ms | |
| 15.4 | Streaming write per-token latency (SSE event to editor insertion) | < 100ms (NFR-01) | ___ ms | |
| 15.5 | BroadcastChannel message delivery latency (cross-pane) | < 10ms (NFR-03) | ___ ms | |
| 15.6 | Initial bundle size (SprkChatPane, gzipped) | < 500KB (NFR-06) | ___ KB | |
| 15.7 | Initial bundle size (AnalysisWorkspace, gzipped) | < 500KB (NFR-06) | ___ KB | |

**Measurement Method**: Use browser DevTools Performance tab and Network tab. For streaming latency, add `performance.now()` timestamps in Console or use the existing debug logging.

**Failure Notes**: _______________________________________________________________

---

## Results Summary

### Execution Metadata

| Field | Value |
|-------|-------|
| **Tester** | |
| **Date** | |
| **Environment** | Dev (`spaarkedev1.crm.dynamics.com`) |
| **Browser** | |
| **OS** | |

### Scenario Results

| # | Scenario | Package | Priority | Result | Failures |
|---|----------|---------|----------|--------|----------|
| 1 | SprkChatPane Side Pane Launch | A | P0 | | |
| 2 | SprkChatPane on Project & Analysis Forms | A | P0 | | |
| 3 | AnalysisWorkspace Code Page Launch | C | P0 | | |
| 4 | Streaming Write Flow | B | P0 | | |
| 5 | Streaming Write Cancellation | B | P0 | | |
| 6 | Selection-Based Revision | G | P1 | | |
| 7 | Action Menu / Command Palette | D | P0 | | |
| 8 | Re-Analysis Pipeline | E | P0 | | |
| 9 | Diff Compare View | F | P1 | | |
| 10 | Suggestions and Citations | H | P1 | | |
| 11 | Web Search | I | P1 | | |
| 12 | Dark Mode | ADR-021 | P1 | | |
| 13 | Cross-Pane Communication | A + C | P0 | | |
| 14 | Error Handling and Edge Cases | All | P1 | | |
| 15 | Performance Benchmarks | All | P1 | | |

### Sign-Off Criteria

**PASS**: All P0 scenarios pass AND no more than 2 P1 failures (with logged issues).
**CONDITIONAL PASS**: All P0 scenarios pass BUT 3+ P1 failures exist (requires issue tracking and remediation plan).
**FAIL**: Any P0 scenario fails. Deployment must be rolled back or hotfixed before sign-off.

### Sign-Off

| Role | Name | Decision | Date |
|------|------|----------|------|
| QA Tester | | PASS / CONDITIONAL PASS / FAIL | |
| Project Owner | Ralph Schroeder | PASS / CONDITIONAL PASS / FAIL | |

---

## Known Limitations and Blockers

| Item | Status | Impact |
|------|--------|--------|
| PH-088: Azure Bing Search API not provisioned | Unresolved | Scenario 11 (Web Search) will be BLOCKED |
| PH-015-A: Side pane icon placeholder | Unresolved | Cosmetic only; default icon used |
| PH-062-A: `analysisApi.ts` API config placeholder | Unresolved | May affect Scenario 3 if API URL is hardcoded |

---

## Appendix: SSE Event Types Reference

These are the SSE event types to look for in the Network tab during testing:

| Event Type | Package | Description |
|------------|---------|-------------|
| `token` | B | Chat response token (existing) |
| `done` | B | Chat response complete (existing) |
| `error` | All | Error event (existing) |
| `document_stream_start` | B | Streaming write begins |
| `document_stream_token` | B | Individual token for editor insertion |
| `document_stream_end` | B | Streaming write complete |
| `document_replace` | E | Bulk document replacement (re-analysis) |
| `progress` | E | Re-analysis progress percentage |
| `suggestions` | H | Follow-up suggestion chips |
| `citations` | H | Citation metadata with source info |

## Appendix: BroadcastChannel Event Types Reference

| Event Type | Direction | Description |
|------------|-----------|-------------|
| `document_stream_start` | SprkChat -> AW | Streaming write begins |
| `document_stream_token` | SprkChat -> AW | Token for editor insertion |
| `document_stream_end` | SprkChat -> AW | Streaming write complete |
| `selection_changed` | AW -> SprkChat | User selected text in editor |
| `context_changed` | Either | Record context changed |

---

*Generated for Task R2-147 | SprkChat Interactive Collaboration R2*
