# SprkChat Analysis Workspace Companion

> **Project**: ai-sprk-chat-context-awareness-r1 (Phase 2)
> **Status**: Design
> **Priority**: 1 — Next implementation phase
> **Branch**: work/ai-sprk-chat-context-awareness-r1
> **Date**: 2026-03-16
> **Prerequisite**: Context awareness infrastructure (Phase 1 — complete)

---

## Problem Statement

SprkChat is currently positioned as a general-purpose AI chat that auto-registers on every page via SidePaneManager injection. This puts it in direct competition with M365 Copilot (which is already available in the Spaarke model-driven app) and dilutes its value proposition.

Meanwhile, the Analysis Workspace already has a SprkChat integration via BroadcastChannel — but SprkChat launches as a generic chat without awareness of the analysis context (analysis type, matter type, practice area, source document). The user must manually explain their context.

Additionally, the Analysis Workspace editor lacks inline AI interaction — users cannot highlight text and apply AI actions directly within the content they're reviewing. All AI interaction requires switching to the chat pane.

## Vision

Reposition SprkChat as a **contextual AI component** — not a general chat, but a purpose-built companion that launches in specific contexts with curated tools, playbooks, and knowledge. The first and primary context is the Analysis Workspace.

When SprkChat opens as the Analysis Workspace companion:
1. It knows the analysis type, matter type, practice area, and source document
2. It pre-loads relevant playbooks and tools based on that context
3. It provides both **side pane chat** and **inline AI tools** within the editor
4. Both surfaces share a single session, context, and tool set

---

## What Exists Today

### Analysis Workspace (Production State)

The Analysis Workspace is a React 19 Code Page at `src/client/code-pages/AnalysisWorkspace/` with:

**Core Components:**
- `EditorPanel.tsx` — Lexical-based rich text editor for analysis output
- `SourceViewerPanel.tsx` — iframe document viewer (PDF/Office via BFF preview URL)
- `AnalysisToolbar.tsx` — Save, Export, Copy, Undo/Redo
- `DiffReviewPanel.tsx` — Side-by-side diff for AI-proposed revisions
- `PanelSplitter.tsx` — Draggable divider between editor and source viewer

**SprkChat Integration (Already Built):**
- `DocumentStreamBridge.tsx` — BroadcastChannel bridge to SprkChat
- `useDocumentStreaming.ts` — Receives streamed tokens from SprkChat, writes to editor
- `useSelectionBroadcast.ts` — Broadcasts editor text selections to SprkChat
- `useDiffReview.ts` — Buffers diff-mode streams, opens DiffReviewPanel
- `useReAnalysisProgress.ts` — Progress overlay during re-analysis
- SprkChat side pane auto-created on App mount (pane ID: `sprkchat-analysis`)
- BroadcastChannel: `sprk-workspace-{analysisId}`

**Data Model:**
```typescript
interface AnalysisRecord {
  id: string;                    // GUID
  title: string;                 // sprk_name
  content: string;               // sprk_workingdocument (HTML)
  status: 'draft' | 'in_progress' | 'completed' | 'error' | 'archived';
  sourceDocumentId: string;      // _sprk_documentid_value
  actionId?: string;             // _sprk_actionid_value
  playbookId?: string;           // _sprk_playbook_value
  statusCode?: number;           // 1=Draft, 2=Completed
}
```

**Key Insight**: The AnalysisWorkspace already broadcasts `selection_changed` events with `selectedText`, `selectedHtml`, and `boundingRect` — but nothing consumes the bounding rect for inline UI yet.

### SprkChat Integration Points (Already Built)

| Direction | Event | Channel | Purpose |
|-----------|-------|---------|---------|
| SprkChat → Editor | `document_stream_start/token/end` | BroadcastChannel | Stream content to editor |
| SprkChat → Editor | `document_replaced` | BroadcastChannel | Full content replacement |
| SprkChat → Editor | `reanalysis_progress` | BroadcastChannel | Progress overlay |
| Editor → SprkChat | `selection_changed` | BroadcastChannel | Text selection context |
| SprkChat → Editor | `document_stream_start` (diff mode) | BroadcastChannel | Proposed revision |

### Context Mapping Service (Phase 1 — Complete)

- `ChatContextMappingService.cs` — Resolves entity type + page type → playbooks
- `GET /api/ai/chat/context-mappings` — Returns default + available playbooks
- `sprk_aichatcontextmap` Dataverse entity — Admin-configurable mappings
- Redis caching (30-min TTL) + client sessionStorage (5-min TTL)
- Page type detection: form, list, dashboard, workspace, unknown

---

## Design

### Part A: SprkChat Contextual Launch Model

#### A1: Remove Global Auto-Launch

SprkChat should not auto-register on every page. Remove SidePaneManager injection from pages that don't need it.

**Changes:**

| File | Action | Reason |
|------|--------|--------|
| `src/solutions/EventsPage/index.html` | Remove injection snippet | EventsPage doesn't need SprkChat |
| `src/solutions/SpeAdminApp/index.html` | Remove injection snippet | Admin app doesn't need SprkChat |
| `src/client/code-pages/SprkChatPane/index.html` | Keep injection snippet | SprkChat's own page needs SidePaneManager |
| `src/client/code-pages/AnalysisWorkspace/index.html` | Keep injection snippet | Analysis Workspace launches SprkChat |
| Application Ribbon button | Keep | Manual trigger for users on any page |

#### A2: Expand Launch Context

Extend `openSprkChatPane.ts` to accept richer context:

```typescript
interface SprkChatLaunchContext {
  // Record context (existing)
  entityType: string;
  entityId: string;

  // Analysis context (new — resolved from analysis record)
  analysisType?: string;        // from actionId → action.type
  matterType?: string;          // from related matter → matter.type
  practiceArea?: string;        // from related matter → matter.practiceArea
  analysisId?: string;          // sprk_analysis GUID

  // Source file context (new)
  sourceFileId?: string;        // SPE document ID
  sourceContainerId?: string;   // SPE container ID

  // Playbook (new — pre-resolved or resolved by context mapping)
  playbookId?: string;
  availablePlaybooks?: string[];

  // Mode (new)
  mode?: 'standalone' | 'workspace-companion';
}
```

**Data flow**: URL params → `openSprkChatPane` → side pane `navigate()` with encoded data → SprkChatPane reads via `URLSearchParams` → passes to `SprkChat` component.

#### A3: Analysis Workspace Launches SprkChat

Instead of SidePaneManager auto-registering SprkChat, the Analysis Workspace App.tsx launches it with full context after loading the analysis record:

```typescript
// In AnalysisWorkspace App.tsx — after useAnalysisLoader resolves
useEffect(() => {
  if (analysis && documentMetadata) {
    openSprkChatCompanion({
      entityType: 'sprk_analysisoutput',
      entityId: analysis.id,
      analysisId: analysis.id,
      analysisType: analysis.actionId,  // resolved to action type
      sourceFileId: documentMetadata.id,
      sourceContainerId: documentMetadata.containerId,
      playbookId: analysis.playbookId,
      mode: 'workspace-companion'
    });
  }
}, [analysis, documentMetadata]);
```

The existing `Xrm.App.sidePanes.createPane()` call in App.tsx already does this (pane ID: `sprkchat-analysis`). This change enriches the context passed via URL params.

### Part B: Inline AI Tools

#### B1: Component Architecture

The inline AI toolbar is a floating UI element that appears when the user selects text in the editor. It provides context-specific AI actions that execute through the SprkChat session.

**New components in `@spaarke/ui-components`:**

```
src/components/
  InlineAiToolbar/
    InlineAiToolbar.tsx          // Floating toolbar positioned at selection
    InlineAiActions.tsx          // Action buttons (Summarize, Simplify, etc.)
    useInlineAiToolbar.ts        // Position calculation + visibility logic
    useInlineAiActions.ts        // Action execution via BroadcastChannel
    inlineAiToolbar.types.ts     // Types and action definitions
```

**New hook in AnalysisWorkspace:**

```
src/hooks/
  useInlineAiToolbar.ts          // Wires InlineAiToolbar to editor + SprkChat bridge
```

#### B2: Inline Action Model

Actions are defined per-context and come from the playbook configuration:

```typescript
interface InlineAiAction {
  id: string;                    // e.g., 'summarize', 'simplify', 'fact-check'
  label: string;                 // Display text
  icon: string;                  // Fluent UI icon name
  description?: string;          // Tooltip
  requiresSelection: boolean;    // Whether text must be selected
  actionType: 'chat' | 'replace' | 'diff';  // How result is delivered
}

// Default actions (always available)
const DEFAULT_INLINE_ACTIONS: InlineAiAction[] = [
  { id: 'summarize', label: 'Summarize', icon: 'TextBulletListSquare', requiresSelection: true, actionType: 'chat' },
  { id: 'simplify', label: 'Simplify', icon: 'TextEditStyle', requiresSelection: true, actionType: 'diff' },
  { id: 'expand', label: 'Expand', icon: 'TextExpand', requiresSelection: true, actionType: 'diff' },
  { id: 'fact-check', label: 'Fact-check', icon: 'Checkmark', requiresSelection: true, actionType: 'chat' },
  { id: 'ask', label: 'Ask SprkChat', icon: 'Chat', requiresSelection: true, actionType: 'chat' },
];

// Context-specific actions (from playbook)
// e.g., Patent Claims Analysis adds:
//   { id: 'extract-claims', label: 'Extract Claims', actionType: 'chat' }
//   { id: 'prior-art-search', label: 'Search Prior Art', actionType: 'chat' }
```

#### B3: Toolbar Positioning

The toolbar appears above the text selection, anchored to the selection's bounding rect:

```typescript
function useInlineAiToolbar(editorRef: RefObject<HTMLElement>) {
  const [position, setPosition] = useState<{ top: number; left: number } | null>(null);
  const [selectedText, setSelectedText] = useState('');

  useEffect(() => {
    const handleSelectionChange = () => {
      const selection = window.getSelection();
      if (!selection || selection.isCollapsed || !selection.rangeCount) {
        setPosition(null);
        return;
      }

      // Verify selection is within editor
      const range = selection.getRangeAt(0);
      if (!editorRef.current?.contains(range.commonAncestorContainer)) {
        setPosition(null);
        return;
      }

      const rect = range.getBoundingClientRect();
      setSelectedText(selection.toString());
      setPosition({
        top: rect.top - TOOLBAR_HEIGHT - TOOLBAR_OFFSET,
        left: rect.left + (rect.width / 2),
      });
    };

    // Debounce to avoid flicker during drag-select
    const debounced = debounce(handleSelectionChange, 200);
    document.addEventListener('selectionchange', debounced);
    return () => document.removeEventListener('selectionchange', debounced);
  }, [editorRef]);

  return { position, selectedText, isVisible: position !== null };
}
```

#### B4: Action Execution Flow

When the user clicks an inline action:

```
User selects text in EditorPanel
  │
  ├─ InlineAiToolbar appears above selection
  │
  ▼
User clicks "Summarize"
  │
  ├─ actionType === 'chat':
  │   → Send message to SprkChat via BroadcastChannel:
  │     { type: 'inline_action', action: 'summarize', selectedText, selectedHtml }
  │   → SprkChat receives, sends to BFF API as a chat message
  │   → Result appears in SprkChat pane (persistent, scrollable)
  │   → User can follow up in chat
  │
  ├─ actionType === 'diff':
  │   → Send message to SprkChat via BroadcastChannel
  │   → SprkChat processes, streams back via document_stream_start (diff mode)
  │   → DiffReviewPanel opens with original vs. proposed
  │   → User accepts, rejects, or edits
  │
  └─ actionType === 'replace':
      → Send message to SprkChat via BroadcastChannel
      → SprkChat processes, streams back via document_stream_start
      → Content replaces selection directly (with undo support)
```

**BroadcastChannel events (new):**

```typescript
// Editor → SprkChat: Request inline action
interface InlineActionRequest {
  type: 'inline_action';
  action: string;              // action ID from InlineAiAction
  selectedText: string;        // plain text
  selectedHtml: string;        // HTML fragment
  context: {
    analysisId: string;
    documentId: string;
    analysisType?: string;
  };
}

// SprkChat → Editor: Inline action result (for 'chat' type)
// Uses existing document_stream_start/token/end events

// SprkChat → Editor: Inline action result (for 'diff' type)
// Uses existing document_stream_start with operationType='diff'
```

#### B5: SprkChat Receives Inline Actions

In `SprkChat.tsx` / `SprkChatPane`, subscribe to `inline_action` events:

```typescript
bridge.on('inline_action', (event: InlineActionRequest) => {
  // Convert inline action to a chat message
  const prompt = buildInlinePrompt(event.action, event.selectedText);

  // Send as if user typed it — appears in chat history
  sendMessage(prompt, {
    metadata: {
      source: 'inline-action',
      action: event.action,
      selectedTextLength: event.selectedText.length,
    }
  });
});

function buildInlinePrompt(action: string, text: string): string {
  switch (action) {
    case 'summarize':
      return `Summarize the following selected text:\n\n"${text}"`;
    case 'simplify':
      return `Simplify the following text while preserving meaning:\n\n"${text}"`;
    case 'expand':
      return `Expand the following text with more detail:\n\n"${text}"`;
    case 'fact-check':
      return `Fact-check the following claims and identify any inaccuracies:\n\n"${text}"`;
    default:
      return `[${action}] ${text}`;
  }
}
```

### Part C: Context-Driven Playbook Resolution

#### C1: Analysis Record → Context Resolution

When the Analysis Workspace loads a record, the context mapping service resolves the full context:

```
sprk_analysisoutput record
  │
  ├─ actionId → sprk_analysisaction → analysisType (e.g., 'patent-claims')
  ├─ playbookId → sprk_analysisplaybook → playbook configuration
  │
  ├─ Related matter (_sprk_matterid_value) →
  │   ├─ sprk_mattertype → matterType (e.g., 'patent')
  │   └─ sprk_practicearea → practiceArea (e.g., 'intellectual-property')
  │
  └─ sourceDocumentId → document metadata (name, type, container)
```

**New BFF endpoint** (or extend existing):

```
GET /api/ai/chat/context-mappings/analysis/{analysisId}
```

Response includes resolved playbooks + inline actions based on the full context:

```json
{
  "defaultPlaybook": { "id": "...", "name": "Patent Claims Analysis" },
  "availablePlaybooks": [...],
  "inlineActions": [
    { "id": "summarize", "label": "Summarize", "icon": "TextBulletListSquare", "actionType": "chat" },
    { "id": "extract-claims", "label": "Extract Claims", "icon": "Gavel", "actionType": "chat" },
    { "id": "prior-art-search", "label": "Search Prior Art", "icon": "Search", "actionType": "chat" }
  ],
  "knowledgeSources": ["USPTO Patent Database", "Firm Patent Precedents"],
  "analysisContext": {
    "analysisType": "patent-claims",
    "matterType": "patent",
    "practiceArea": "intellectual-property"
  }
}
```

#### C2: Inline Actions from Playbook Configuration

Playbooks define which inline actions are available:

```json
{
  "name": "Patent Claims Analysis",
  "inlineActions": [
    { "id": "extract-claims", "label": "Extract Claims", "icon": "Gavel", "actionType": "chat" },
    { "id": "prior-art-search", "label": "Search Prior Art", "icon": "Search", "actionType": "chat" },
    { "id": "claim-mapping", "label": "Map to Specification", "icon": "Map", "actionType": "diff" }
  ]
}
```

Default actions (summarize, simplify, expand, fact-check, ask) are always available. Playbook-specific actions are appended based on context.

---

## Deployment Changes

### Remove from Current Build

| File | Change |
|------|--------|
| `src/solutions/EventsPage/index.html` | Remove SidePaneManager injection snippet (lines 7-22) |
| `src/solutions/SpeAdminApp/index.html` | Remove SidePaneManager injection snippet (lines 7-22) |

### Keep Unchanged

| File | Reason |
|------|--------|
| `src/client/code-pages/SprkChatPane/index.html` | SprkChat needs SidePaneManager |
| `src/client/code-pages/AnalysisWorkspace/index.html` | Analysis Workspace launches SprkChat |
| Application Ribbon "SprkChat" button | Manual trigger on any page |
| `src/client/webresources/js/sprk_openSprkChatPane.js` | Launcher script (will be extended) |
| `src/client/side-pane-manager/SidePaneManager.ts` | Still used by SprkChatPane injection |

### Rebuild Required

| Component | Trigger | Build Command |
|-----------|---------|---------------|
| EventsPage | Injection snippet removed | `cd src/solutions/EventsPage && npm run build` |
| SpeAdminApp | Injection snippet removed | `cd src/solutions/SpeAdminApp && npm run build` |
| AnalysisWorkspace | Inline AI toolbar added | `cd src/client/code-pages/AnalysisWorkspace && npm run build` |
| SprkChatPane | Inline action handler added | `cd src/client/code-pages/SprkChatPane && npm run build` |
| @spaarke/ui-components | InlineAiToolbar component added | No separate build (bundled by consumers) |

---

## Phases

### Phase 2A: Contextual Launch (Deployment Fix)

**Scope**: Remove global auto-launch, enrich Analysis Workspace launch context.

1. Remove SidePaneManager injection from EventsPage and SpeAdminApp index.html
2. Extend `openSprkChatPane.ts` with `SprkChatLaunchContext` interface
3. Update Analysis Workspace App.tsx to pass enriched context on launch
4. Update SprkChatPane to consume new context parameters
5. Rebuild and deploy EventsPage, SpeAdminApp, SprkChatPane, AnalysisWorkspace

### Phase 2B: Inline AI Toolbar

**Scope**: Add floating AI action menu to the Analysis Workspace editor.

1. Create `InlineAiToolbar` component in `@spaarke/ui-components`
2. Create `useInlineAiToolbar` hook for position tracking
3. Wire `InlineAiToolbar` into `EditorPanel.tsx`
4. Add `inline_action` BroadcastChannel event handler in SprkChat
5. Test default actions: summarize, simplify, expand, fact-check, ask

### Phase 2C: Context-Driven Actions

**Scope**: Playbook-specific inline actions and knowledge scoping.

1. Extend context mapping endpoint to return inline actions per playbook
2. Configure patent-specific actions (extract claims, prior art search)
3. Add knowledge source scoping based on analysis context
4. Configure initial playbook-to-inline-action mappings in Dataverse

### Phase 2D: Insert-to-Editor Flow

**Scope**: Allow SprkChat responses to be inserted back into the editor.

1. Add "Insert" button to SprkChat message responses
2. On insert: broadcast `document_insert` event with content + cursor position
3. Editor receives and inserts at current cursor or replaces selection
4. Support for both plain text and formatted HTML insertion

---

## Implementation Constraints

| Constraint | Source | Impact |
|------------|--------|--------|
| React 19 in AnalysisWorkspace | Existing | InlineAiToolbar must use React 19 APIs |
| Lexical editor | Existing | Inline toolbar positions relative to Lexical selection |
| BroadcastChannel bridge | Existing | All inline actions route through existing bridge |
| SSE streaming for AI content | UX standard | Inline actions that generate AI content must stream |
| Fluent UI v9 | ADR-021 | Toolbar uses Fluent v9 tokens, supports dark mode |
| No Xrm dependency in shared lib | Architecture | `InlineAiToolbar` in shared lib must not depend on Xrm |
| Existing diff review flow | Existing | `actionType: 'diff'` reuses existing DiffReviewPanel |

---

## Success Criteria

1. SprkChat does NOT auto-register on EventsPage or SpeAdminApp
2. SprkChat launches with full analysis context in the Analysis Workspace
3. Inline AI toolbar appears on text selection in the editor
4. Inline actions execute through SprkChat session and appear in chat history
5. Diff-mode actions (simplify, expand) open the existing DiffReviewPanel
6. Context-specific actions appear based on analysis type / playbook
7. "Ask SprkChat" inline action sends selected text to chat pane
8. All inline actions stream results (no REST-returned AI content)
9. SprkChat companion pane ID (`sprkchat-analysis`) prevents duplicate panes

---

## Risks

| Risk | Mitigation |
|------|------------|
| Inline toolbar Z-index conflicts with Lexical popups | Use Fluent UI Portal with high z-index; test with all editor plugins |
| Selection lost when clicking toolbar button | Use `mousedown` event (not `click`) to prevent deselection; or preserve selection range in state |
| BroadcastChannel message ordering | Existing bridge handles ordering; inline actions are request/response, not concurrent streams |
| Playbook doesn't define inline actions | Fall back to default actions; always available regardless of playbook |
| Mobile/tablet: no text selection UX | Defer mobile inline toolbar; focus on desktop (Analysis Workspace is desktop-primary) |
| Editor performance with toolbar re-renders | Toolbar is a lightweight overlay; debounce selection events at 200ms |

---

## File Inventory

### New Files

| File | Purpose |
|------|---------|
| `src/client/shared/Spaarke.UI.Components/src/components/InlineAiToolbar/InlineAiToolbar.tsx` | Floating toolbar component |
| `src/client/shared/Spaarke.UI.Components/src/components/InlineAiToolbar/InlineAiActions.tsx` | Action button list |
| `src/client/shared/Spaarke.UI.Components/src/components/InlineAiToolbar/useInlineAiToolbar.ts` | Position + visibility hook |
| `src/client/shared/Spaarke.UI.Components/src/components/InlineAiToolbar/useInlineAiActions.ts` | Action execution hook |
| `src/client/shared/Spaarke.UI.Components/src/components/InlineAiToolbar/inlineAiToolbar.types.ts` | Types |
| `src/client/code-pages/AnalysisWorkspace/src/hooks/useInlineAiToolbar.ts` | Wires toolbar to editor + bridge |

### Modified Files

| File | Change |
|------|--------|
| `src/client/code-pages/SprkChatPane/launcher/openSprkChatPane.ts` | Expand launch context interface |
| `src/client/code-pages/AnalysisWorkspace/src/App.tsx` | Pass enriched context on SprkChat launch |
| `src/client/code-pages/AnalysisWorkspace/src/components/EditorPanel.tsx` | Mount InlineAiToolbar |
| `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChat.tsx` | Handle `inline_action` events |
| `src/solutions/EventsPage/index.html` | Remove injection snippet |
| `src/solutions/SpeAdminApp/index.html` | Remove injection snippet |

### Unchanged (Reference)

| File | Reason |
|------|--------|
| `src/client/code-pages/AnalysisWorkspace/src/hooks/useDocumentStreaming.ts` | Existing streaming pipeline — reused by inline actions |
| `src/client/code-pages/AnalysisWorkspace/src/hooks/useDiffReview.ts` | Existing diff review — reused by diff-type inline actions |
| `src/client/code-pages/AnalysisWorkspace/src/hooks/useSelectionBroadcast.ts` | Already broadcasts selections — complements inline toolbar |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/ChatContextMappingService.cs` | Phase 1 complete — may extend for inline action resolution |
