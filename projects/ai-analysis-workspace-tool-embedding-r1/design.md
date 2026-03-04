# Embedded AI in Analysis Workspace

> **Project**: ai-analysis-workspace-tool-embedding-r1
> **Status**: Design
> **Priority**: 2 (Parallel with #3, independent UI surface)
> **Branch**: work/ai-analysis-workspace-tool-embedding-r1

---

## Problem Statement

In the Analysis Workspace, users must switch to the SprkChat side pane for any AI assistance — even for inline operations like "simplify this paragraph" or "expand this bullet point." The round-trip (select text in editor → switch to side pane → type instruction → wait for response → review in side pane → accept → see result in editor) is slow and breaks the writing flow.

Modern document editors (Word Copilot, Google Docs AI, Notion AI) provide inline AI assistance that appears at the point of action — a floating toolbar on text selection, inline prompt input, and streaming results directly into the document. SprkChat already has the infrastructure for this (SprkChatHighlightRefine, cross-pane bridge, refine endpoint, DiffReviewPanel) but it's all routed through the side pane.

## Goals

1. **Inline floating toolbar** — Appears on text selection within the RichTextEditor, not just in SprkChat
2. **Quick actions in editor** — One-tap Simplify, Expand, Rewrite, Make Formal directly from the toolbar
3. **Inline AI prompt** — Free-text "Ask AI about this" input anchored to the selection
4. **Direct streaming** — AI response streams into the editor at the selection point, no side pane round-trip
5. **Inline diff preview** — Proposed changes shown as strikethrough/highlight with Accept/Reject controls
6. **Keyboard-first** — `Ctrl+I` (or configurable shortcut) invokes inline AI on current selection

## What Exists Today

### Analysis Workspace
- `EditorPanel` — RichTextEditor with streaming indicator + toolbar
- `SourceViewerPanel` — Document viewer with collapse/expand
- `AnalysisToolbar` — Save, Export, Copy, Undo/Redo controls
- `DiffReviewPanel` — Side-by-side diff review with Accept/Reject/Edit (for cross-pane refine results)
- `useSelectionBroadcast` — Sends editor selections to SprkChatBridge (for cross-pane refine)
- `useAutoSave` — Dataverse PATCH on content changes

### SprkChat (Cross-Pane Path — Today's Flow)
- `SprkChatHighlightRefine` — Floating toolbar for text selection refinement (in chat messages only)
- `useSelectionListener` — Receives `selection_changed` events from bridge
- Editor selection → bridge `selection_changed` → SprkChat toolbar → user types instruction → `/refine` endpoint → bridge `document_stream_*` → editor receives tokens → DiffReviewPanel
- Quick actions: Simplify, Expand, Make Concise, Make Formal (defined in SprkChat types)

### BFF API
- `POST /api/ai/chat/sessions/{id}/refine` — Text refinement via SSE streaming
  - Request: `{ selectedText, instruction, source: "editor"|"chat", quickAction? }`
  - Response: SSE stream of tokens
- Existing endpoint works for both chat-sourced and editor-sourced refinements

### SprkChatBridge Events
- `document_stream_start` — `{ operationId, targetPosition, operationType: "insert"|"replace"|"diff" }`
- `document_stream_token` — `{ operationId, token, index }`
- `document_stream_end` — `{ operationId, cancelled, totalTokens }`
- `selection_changed` — `{ text, startOffset, endOffset, context? }`

## Design

### Architecture: Direct vs. Bridge

**Today (Bridge path)**:
```
Editor selection → Bridge → SprkChat → /refine API → Bridge → Editor
```

**Proposed (Direct path)**:
```
Editor selection → EditorInlineToolbar → /refine API → Editor
```

The inline toolbar calls the `/refine` endpoint directly from the Analysis Workspace — no SprkChat involvement, no bridge round-trip. The SprkChat cross-pane path continues to work for users who prefer the conversational flow.

**Key decision**: The inline toolbar needs an active chat session ID to call `/refine`. Options:
1. **Shared session**: Use the same session as SprkChat (read from bridge or sessionStorage)
2. **Lightweight session**: Create a separate "inline-refine" session on first use
3. **Sessionless refine**: New BFF endpoint that doesn't require a session

Recommend **Option 1** (shared session) for MVP — the SprkChat pane auto-loads when Analysis Workspace opens (task 063), so a session always exists. Fall back to Option 2 if SprkChat pane is closed.

### EditorInlineToolbar Component

```
User selects text in RichTextEditor
  ↓
EditorInlineToolbar appears above/below selection
  ↓
┌──────────────────────────────────────────────┐
│ ✨ [Simplify] [Expand] [Rewrite] [Ask AI...] │
└──────────────────────────────────────────────┘
  ↓ User clicks "Simplify" or types in "Ask AI..."
  ↓
POST /api/ai/chat/sessions/{id}/refine
  { selectedText, instruction: "Simplify", source: "editor", quickAction: "simplify" }
  ↓
SSE tokens stream back
  ↓
Inline diff preview at selection point:
  ┌─────────────────────────────────────────┐
  │ ~~The aforementioned agreement between~~ │  ← strikethrough original
  │ The agreement between                    │  ← highlighted replacement
  │                    [Accept] [Reject] [✏] │
  └─────────────────────────────────────────┘
  ↓
User clicks Accept → replaces selection in RichTextEditor
User clicks Reject → restores original text
User clicks ✏ → opens editable textarea with proposed text
```

### Component Architecture

```
AnalysisWorkspace
├── EditorPanel
│   ├── RichTextEditor
│   │   └── (text selection triggers toolbar)
│   ├── EditorInlineToolbar (NEW)
│   │   ├── QuickActionButtons (Simplify, Expand, Rewrite, Formal)
│   │   ├── InlinePromptInput (free-text "Ask AI...")
│   │   └── InlineDiffPreview (strikethrough/highlight + Accept/Reject)
│   └── AnalysisToolbar (existing)
├── SourceViewerPanel (existing)
└── DiffReviewPanel (existing — for cross-pane refine, kept as-is)
```

### Positioning Logic

The toolbar anchors to the text selection using the browser's Selection API:

```typescript
function getToolbarPosition(selection: Selection): { top: number; left: number } {
  const range = selection.getRangeAt(0);
  const rect = range.getBoundingClientRect();
  const editorRect = editorRef.current.getBoundingClientRect();

  // Position above selection, centered horizontally
  return {
    top: rect.top - editorRect.top - TOOLBAR_HEIGHT - 8,  // 8px gap
    left: rect.left - editorRect.left + (rect.width / 2) - (TOOLBAR_WIDTH / 2),
  };
  // Flip below if too close to top edge
}
```

### Inline Diff Preview

Instead of the full DiffReviewPanel (side-by-side), the inline version shows changes in-place:

```typescript
interface InlineDiffState {
  isActive: boolean;
  originalText: string;
  originalRange: Range;        // DOM range for restoration
  proposedText: string;        // Accumulated SSE tokens
  isStreaming: boolean;
}

// During streaming: show proposed text in highlighted span, original in strikethrough
// After streaming: show Accept/Reject buttons below the diff
// On Accept: replaceRange(originalRange, proposedText)
// On Reject: restoreRange(originalRange, originalText)
```

### Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+I` | Open inline AI toolbar on current selection |
| `Ctrl+Shift+S` | Quick action: Simplify selection |
| `Ctrl+Shift+E` | Quick action: Expand selection |
| `Escape` | Dismiss toolbar / reject inline diff |
| `Ctrl+Enter` | Accept inline diff (when diff preview active) |

### Interaction with SprkChat Side Pane

Both paths coexist:
- **Inline toolbar**: Quick, focused edits (simplify, expand, rewrite). Stays in editor flow.
- **SprkChat side pane**: Conversational, multi-turn. For complex instructions, questions, analysis.
- **Bridge still works**: `selection_changed` events still fire, so SprkChat can offer cross-pane refine too.
- **No conflict**: Inline toolbar dismisses if SprkChat starts a cross-pane refine (and vice versa).

## Phases

### Phase 1: Inline Toolbar (MVP)
- `EditorInlineToolbar` component with quick action buttons
- Selection detection in RichTextEditor (mouseup/keyup)
- Toolbar positioning logic (above selection, flip-below, boundary clamp)
- Direct `/refine` endpoint call (shared session from SprkChat)
- Basic streaming: replace selection with streamed text (no diff preview yet)
- Dismiss on click-away, Escape, or new selection

### Phase 2: Inline Diff Preview
- `InlineDiffPreview` component (strikethrough original, highlight proposed)
- Accept/Reject/Edit controls
- Undo integration (rejected changes go to undo stack)
- Streaming diff: show tokens as they arrive, finalize on stream end

### Phase 3: Inline Prompt & Keyboard
- `InlinePromptInput` — free-text input in the toolbar ("Ask AI...")
- `Ctrl+I` keyboard shortcut to invoke toolbar
- Quick-action keyboard shortcuts (Ctrl+Shift+S, etc.)
- Command history (up arrow recalls previous inline instructions)

### Phase 4: Proactive Suggestions (Future)
- AI analyzes document in background, suggests improvements
- Subtle indicators (underline, margin icon) on paragraphs with suggestions
- Click indicator → shows inline suggestion with Accept/Reject
- Grammar, clarity, completeness checks

## Success Criteria

1. User selects text in editor → floating toolbar appears within 100ms
2. Quick action (Simplify) → AI response streams directly into editor within 1s
3. Inline diff shows original (strikethrough) and proposed (highlighted) with Accept/Reject
4. Accept replaces text; Reject restores original; both update undo stack
5. Toolbar dismisses cleanly on click-away, Escape, or new selection
6. Works alongside SprkChat side pane without conflicts

## Dependencies

- RichTextEditor component in shared library (selection API access)
- Active chat session (from SprkChat auto-load on Analysis Workspace open)
- BFF API `/refine` endpoint (exists, no changes needed for MVP)
- Authentication token (from Analysis Workspace's authService)

## Risks

- RichTextEditor may not expose Selection API easily (mitigation: add ref method for selection access)
- Toolbar positioning in scrollable editor content (mitigation: use `position: absolute` relative to editor container, recalculate on scroll)
- Inline diff with rich text (HTML) is complex (mitigation: Phase 1 uses plain text replacement, Phase 2 adds HTML-aware diff)
- Session availability when SprkChat pane is closed (mitigation: lazy session creation on first inline action)
