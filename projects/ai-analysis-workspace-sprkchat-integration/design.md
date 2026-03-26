# Analysis Workspace + SprkChat Integration — Unified Code Page

> **Project**: ai-analysis-workspace-sprkchat-integration
> **Status**: Design
> **Priority**: High (prerequisite for SprkChat extensibility project)
> **Last Updated**: March 25, 2026

---

## Executive Summary

Merge the AnalysisWorkspace Code Page and SprkChatPane Code Page into a single unified Code Page. SprkChat becomes a built-in panel within the Analysis Workspace rather than a separate Dataverse side pane communicating via BroadcastChannel. This eliminates cross-pane serialization complexity, unifies authentication, enables direct React state sharing between the editor and chat, and provides layout flexibility (resizable, collapsible chat panel).

This project is a **prerequisite** for the SprkChat extensibility project (`ai-sprk-chat-extensibility-r1`) — the extensibility work (slash commands, compound actions, smart routing) should be built on the unified architecture, not the legacy two-pane model.

---

## Problem Statement

The Analysis Workspace and SprkChat are currently two separate Code Pages running in separate iframes:

- **AnalysisWorkspace** (`sprk_AnalysisWorkspace`) — renders in the main content area with a Lexical editor and source document viewer
- **SprkChatPane** (`sprk_SprkChatPane`) — renders in a 400px Dataverse side pane via `Xrm.App.sidePanes`

They communicate via `SprkChatBridge` (BroadcastChannel with postMessage fallback), which introduces:

1. **Serialization overhead** — all cross-pane data must be serialized/deserialized as JSON messages
2. **Timing issues** — message delivery is asynchronous with no delivery guarantee
3. **Duplicate authentication** — both pages independently initialize MSAL, acquire tokens, and maintain caches
4. **Duplicate context resolution** — both pages independently resolve analysis context, playbook mappings, and BFF configuration
5. **Fixed layout** — side pane is locked at 400px, not resizable by the user
6. **Lifecycle complexity** — side pane open/close management, orphaned pane detection, 2-second context polling, navigation-away cleanup
7. **Two deployment artifacts** — two web resources to build, version, and deploy

This architecture was designed when SprkChat was a general-purpose component attachable to any page. Now that SprkChat is exclusively the Analysis Workspace AI companion, the two-page architecture is overengineered.

---

## Goals

1. **Single Code Page** — AnalysisWorkspace renders both the editor and the chat panel in one React tree
2. **Eliminate BroadcastChannel** — direct React state/context sharing replaces serialized cross-pane messaging
3. **Single auth session** — one MSAL initialization, one token cache, one OBO flow
4. **Flexible layout** — chat panel is resizable, collapsible, and repositioned (right panel alongside source viewer)
5. **Shared components remain reusable** — SprkChat components in `@spaarke/ui-components` retain their callback-based API (ADR-012) so they can be embedded in other contexts in the future
6. **Remove SprkChatPane Code Page** — eliminate the separate Code Page, its launcher, and the SidePaneManager

---

## Design Principles

1. **SprkChat components stay in the shared library** — do NOT move SprkChat components into the AnalysisWorkspace. Import from `@spaarke/ui-components` as today. This preserves reusability.
2. **Replace bridge with React context** — create an `AnalysisAiContext` that both the editor and chat panels consume. No cross-pane messaging.
3. **Adapter pattern for SprkChat** — SprkChat currently receives callbacks for actions like "insert to editor." In the unified page, these callbacks are simple function calls instead of BroadcastChannel emissions. The SprkChat component doesn't need to know the difference.
4. **Three-panel layout** — Editor (left) + Source Document (center-right, collapsible) + Chat (right, collapsible). User controls which panels are visible.
5. **React 19** — full React 19 APIs (createRoot, useId, use, etc.) since this is a Code Page, not a PCF control. No React 16/17 constraints.

---

## Current Architecture

```
Dataverse Shell (UCI)
│
├── Main Content Area
│   └── [AnalysisWorkspace Code Page]  ← iframe 1
│       ├── React 19 tree
│       ├── MSAL auth (independent)
│       ├── useAnalysisLoader → BFF API
│       ├── EditorPanel (Lexical RichTextEditor)
│       ├── PanelSplitter (draggable)
│       ├── SourceViewerPanel
│       ├── DocumentStreamBridge ← listens to BroadcastChannel
│       └── useSelectionBroadcast → emits to BroadcastChannel
│
└── Side Pane (Xrm.App.sidePanes, 400px)
    └── [SprkChatPane Code Page]  ← iframe 2
        ├── React 19 tree (separate)
        ├── MSAL auth (independent, duplicate)
        ├── contextService → polls Xrm every 2s
        ├── SprkChat component
        │   ├── useSseStream → BFF streaming
        │   ├── useChatSession → session management
        │   ├── useChatPlaybooks → playbook switching
        │   └── useChatContextMapping → context resolution
        └── SprkChatBridge → emits to BroadcastChannel

Communication: SprkChatBridge (BroadcastChannel)
  Events: document_stream_start/token/end, document_replaced,
          reanalysis_progress, selection_changed, inline_action
```

## Target Architecture

```
Dataverse Shell (UCI)
│
└── Main Content Area (full page)
    └── [AnalysisWorkspace Code Page]  ← single iframe
        ├── React 19 tree (unified)
        ├── MSAL auth (single instance)
        ├── AnalysisAiProvider (shared React context)
        │   ├── analysis state (record, content, metadata)
        │   ├── chat state (session, messages, playbook)
        │   ├── editor ref (for insert-to-editor)
        │   ├── selection state (editor selection text)
        │   └── auth context (token, user identity)
        │
        ├── Left Panel (flex, resizable)
        │   ├── AnalysisToolbar
        │   ├── EditorPanel (Lexical RichTextEditor)
        │   │   └── InlineAiToolbar (reads selection from context)
        │   ├── DiffReviewPanel
        │   └── ReAnalysisProgressOverlay
        │
        ├── PanelSplitter (draggable)
        │
        ├── Center Panel (collapsible)
        │   └── SourceViewerPanel
        │
        ├── PanelSplitter (draggable)
        │
        └── Right Panel (collapsible, resizable)
            └── SprkChat component (from @spaarke/ui-components)
                ├── Direct props: onInsertToEditor={editorRef.insert}
                ├── Direct props: editorSelection={selectionState}
                ├── Direct props: analysisContext={analysisState}
                ├── useSseStream → BFF streaming (unchanged)
                ├── useChatSession → session management (unchanged)
                └── All other hooks unchanged

Communication: React context + callback props (zero serialization)
```

---

## What Changes

### Removed

| Component | Current Location | Action |
|---|---|---|
| **SprkChatPane Code Page** | `src/client/code-pages/SprkChatPane/` | Remove entirely — no longer needed |
| **openSprkChatPane.ts** | `src/client/code-pages/SprkChatPane/launcher/` | Remove — no side pane to open |
| **SidePaneManager** | `src/client/side-pane-manager/` | Remove (already planned for extensibility Phase 0) |
| **SprkChat ribbon button** | `src/client/webresources/ribbon/sprk_application_ribbon_sprkchat.xml` | Remove |
| **SprkChatBridge** | `src/client/shared/.../services/SprkChatBridge.ts` | Deprecate — keep in library for potential future cross-pane use but remove from AnalysisWorkspace |
| **DocumentStreamBridge** | `src/client/code-pages/AnalysisWorkspace/src/components/DocumentStreamBridge.tsx` | Remove — streaming goes directly from chat hook to editor ref |
| **useSelectionBroadcast** | `src/client/code-pages/AnalysisWorkspace/src/hooks/useSelectionBroadcast.ts` | Remove — selection state shared via React context |
| **contextService.ts** | `src/client/code-pages/SprkChatPane/src/services/contextService.ts` | Remove — no need to poll Xrm; context comes from AnalysisWorkspace's own loader |
| **Side pane MSAL config** | `src/client/code-pages/SprkChatPane/src/config/msalConfig.ts` | Remove — single auth in AnalysisWorkspace |
| **LegalWorkspace SidePaneManager injection** | `src/solutions/LegalWorkspace/index.html` | Remove script injection |
| **Web resource: `sprk_SprkChatPane`** | Dataverse solution | Remove from solution |
| **Web resource: `sprk_openSprkChatPane`** | Dataverse solution | Remove from solution |
| **Web resource: `sprk_SidePaneManager`** | Dataverse solution | Remove from solution |

### Added

| Component | Location | Purpose |
|---|---|---|
| **AnalysisAiProvider** | `src/client/code-pages/AnalysisWorkspace/src/context/AnalysisAiContext.tsx` | Shared React context for analysis state, chat state, editor refs, selection, auth |
| **ChatPanel** | `src/client/code-pages/AnalysisWorkspace/src/components/ChatPanel.tsx` | Thin wrapper that mounts `SprkChat` from shared library with props from AnalysisAiContext |
| **Three-panel layout** | `src/client/code-pages/AnalysisWorkspace/src/App.tsx` | Updated layout: Editor + Source + Chat with two splitters |
| **usePanelLayout hook** | `src/client/code-pages/AnalysisWorkspace/src/hooks/usePanelLayout.ts` | Manages three-panel visibility, sizes, and persistence |
| **Panel toggle toolbar** | Toolbar area | Buttons to show/hide Source panel and Chat panel |

### Modified

| Component | Change |
|---|---|
| **App.tsx** | Restructure from 2-panel to 3-panel layout; remove side pane launch code (lines 416-493); add AnalysisAiProvider wrapping; add ChatPanel |
| **EditorPanel.tsx** | Remove BroadcastChannel event listening; read selection state from context; expose editorRef via context |
| **InlineAiToolbar** | Remove BroadcastChannel emission; call chat action directly via context callback |
| **usePanelResize** | Extend to handle three panels with two splitters (or replace with usePanelLayout) |
| **SprkChat component** (shared library) | No changes needed — already callback-based (ADR-012). New props: `editorSelection`, `onInsertToEditor`, `analysisContext` passed by ChatPanel wrapper |

---

## Layout Design

### Default Layout (All Panels Visible)

```
┌─────────────────────────────────────────────────────────────────┐
│ [Run Analysis ▶]  [↩ Undo] [↪ Redo]  [📄 Source ◉] [💬 Chat ◉]│
├────────────────────────┬────┬───────────────┬────┬──────────────┤
│                        │ ⋮  │               │ ⋮  │              │
│  Editor Panel          │ ⋮  │ Source Doc    │ ⋮  │  SprkChat    │
│  (Lexical)             │ ⋮  │ Viewer        │ ⋮  │              │
│                        │ ⋮  │               │ ⋮  │  [messages]  │
│  "The patent claims    │ ⋮  │  [PDF/Office  │ ⋮  │              │
│   describe a novel     │ ⋮  │   iframe]     │ ⋮  │  [chips]     │
│   method for..."       │ ⋮  │               │ ⋮  │  [input]     │
│                        │ ⋮  │               │ ⋮  │              │
├────────────────────────┴────┴───────────────┴────┴──────────────┤
│  ← drag splitters to resize →                                    │
└─────────────────────────────────────────────────────────────────┘
     ~45%                  ~30%                  ~25%
```

### Editor + Chat Only (Source Hidden)

```
┌─────────────────────────────────────────────────────────────────┐
│ [Run Analysis ▶]  [↩ Undo] [↪ Redo]  [📄 Source ○] [💬 Chat ◉]│
├──────────────────────────────────────┬────┬──────────────────────┤
│                                      │ ⋮  │                      │
│  Editor Panel (Lexical)              │ ⋮  │  SprkChat            │
│                                      │ ⋮  │                      │
│  Full-width editor                   │ ⋮  │  [messages]          │
│                                      │ ⋮  │  [chips]             │
│                                      │ ⋮  │  [input]             │
│                                      │ ⋮  │                      │
├──────────────────────────────────────┴────┴──────────────────────┤
└─────────────────────────────────────────────────────────────────┘
     ~65%                                  ~35%
```

### Editor Only (Both Panels Hidden)

```
┌─────────────────────────────────────────────────────────────────┐
│ [Run Analysis ▶]  [↩ Undo] [↪ Redo]  [📄 Source ○] [💬 Chat ○]│
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  Editor Panel (Lexical)                                          │
│                                                                  │
│  Full-width, full-height editor                                  │
│                                                                  │
│  Maximum writing space                                           │
│                                                                  │
├─────────────────────────────────────────────────────────────────┤
└─────────────────────────────────────────────────────────────────┘
     100%
```

Panel visibility toggles persist to `sessionStorage`. Default: Editor + Chat visible, Source collapsed (toggled on demand).

---

## Reusability Strategy

**Critical requirement**: SprkChat components MUST remain reusable for future contexts beyond Analysis Workspace.

### How Reusability Is Preserved

1. **SprkChat stays in `@spaarke/ui-components`** — no components move into the AnalysisWorkspace Code Page. The ChatPanel wrapper is the only AnalysisWorkspace-specific file.

2. **Callback-based API (ADR-012)** — SprkChat already uses callbacks, not direct service dependencies:
   ```typescript
   // SprkChat component props (unchanged)
   interface SprkChatProps {
     onSendMessage: (message: string, context?: ChatContext) => Promise<void>;
     onInsertToEditor?: (content: string) => void;     // optional callback
     editorSelection?: string;                          // optional prop
     analysisContext?: AnalysisContext;                  // optional prop
     authenticatedFetch: AuthenticatedFetch;             // injected
     bffBaseUrl: string;                                 // injected
     // ... existing props unchanged
   }
   ```

3. **Future embedding pattern** — any Code Page can mount SprkChat:
   ```typescript
   // In AnalysisWorkspace (this project):
   <SprkChat
     onInsertToEditor={(content) => editorRef.current.insert(content)}
     editorSelection={selectionState}
     analysisContext={analysis}
     authenticatedFetch={authenticatedFetch}
     bffBaseUrl={bffBaseUrl}
   />

   // In a future DocumentReviewWorkspace (hypothetical):
   <SprkChat
     onInsertToEditor={(content) => reviewEditorRef.current.insert(content)}
     editorSelection={reviewSelection}
     analysisContext={reviewContext}
     authenticatedFetch={authenticatedFetch}
     bffBaseUrl={bffBaseUrl}
   />

   // In a standalone chat page (hypothetical — no editor):
   <SprkChat
     // No onInsertToEditor — insert button hidden automatically
     // No editorSelection — inline toolbar disabled automatically
     authenticatedFetch={authenticatedFetch}
     bffBaseUrl={bffBaseUrl}
   />
   ```

4. **Feature detection via props** — SprkChat adapts based on what's provided:
   - `onInsertToEditor` provided → shows "Insert to Editor" button on messages
   - `editorSelection` provided → enables inline AI actions from selection
   - Neither provided → pure chat mode (no editor integration features)

---

## Legacy PCF Considerations

A previous PCF control combined workspace and AI chat functionality. That approach used React 16/17 (platform-provided) with significant constraints:

| Constraint | PCF (React 16/17) | Code Page (React 19) |
|---|---|---|
| React version | 16/17 (platform-provided) | 19 (bundled — full control) |
| `createRoot` / `useId` / `use` | Not available | Available |
| Bundle size | < 5MB recommended | No hard limit |
| Fluent UI | v9 with React 16 shims | v9 native |
| Lexical editor | Limited (needs `react/jsx-runtime`) | Full support |
| SSE streaming | Works but constrained | Full support |
| Deep imports | Required to avoid heavy deps | Standard imports |
| Deployment | PCF solution component | Web resource |

**Decision: Code Page (React 19) is the correct architecture.** The PCF approach is not reusable here — the React 16/17 constraint alone makes the Lexical editor integration impractical. The existing AnalysisWorkspace Code Page already uses React 19 and this project extends that foundation.

---

## Phases

### Phase 1: AnalysisAiContext + ChatPanel Integration

**Goal**: Mount SprkChat inside AnalysisWorkspace via shared React context. BroadcastChannel still exists as fallback but is not primary.

- Create `AnalysisAiContext` with analysis state, editor refs, selection state, auth
- Create `ChatPanel` wrapper that mounts `SprkChat` from shared library
- Update `App.tsx` layout to three-panel (Editor + Source + Chat)
- Extend `usePanelResize` / create `usePanelLayout` for three panels with two splitters
- Add panel visibility toggles to toolbar (Source, Chat)
- Wire `onInsertToEditor` callback directly to editor ref
- Wire `editorSelection` from editor state to SprkChat props
- SprkChat SSE streaming writes to editor via direct ref (no bridge)
- Verify: all existing functionality works in unified page

### Phase 2: Remove Cross-Pane Infrastructure

**Goal**: Eliminate all BroadcastChannel/side pane code from AnalysisWorkspace.

- Remove `DocumentStreamBridge` component
- Remove `useSelectionBroadcast` hook
- Remove SprkChat side pane launch code from `App.tsx` (lines 416-493)
- Remove SprkChatPane Code Page entirely (`src/client/code-pages/SprkChatPane/`)
- Remove `openSprkChatPane.ts` launcher
- Remove SidePaneManager (`src/client/side-pane-manager/`)
- Remove global ribbon button XML
- Remove LegalWorkspace SidePaneManager script injection
- Remove web resources from Dataverse solution: `sprk_SprkChatPane`, `sprk_openSprkChatPane`, `sprk_SidePaneManager`
- Deprecate `SprkChatBridge` in shared library (mark deprecated, do not delete — may be useful for future cross-pane scenarios)
- Remove `contextService.ts` (context polling no longer needed)
- Consolidate duplicate auth: single MSAL init in AnalysisWorkspace serves both editor and chat

### Phase 3: Unified Auth + Context Resolution

**Goal**: Single auth flow and single context resolution for the entire page.

- Single `initAuth()` in AnalysisWorkspace entry point
- Single `useAnalysisLoader()` provides analysis context to both editor and chat
- Single BFF base URL resolution
- Remove duplicate context mapping calls (SprkChatPane's `useChatContextMapping` was independent)
- Chat playbook resolved from same analysis context as editor (via `AnalysisAiContext`)
- Verify: token acquisition happens once, not twice; context API called once, not twice

### Phase 4: Layout Polish + Persistence

**Goal**: Polished three-panel UX with persistence.

- Three-panel layout with two draggable splitters
- Panel minimum widths: Editor 300px, Source 200px, Chat 280px
- Keyboard resize: Arrow keys on focused splitter
- Double-click splitter to reset to default ratios
- Panel visibility toggles with keyboard shortcuts
- Persist panel sizes and visibility to `sessionStorage`
- Responsive: on narrow viewport (< 800px), Chat panel becomes a bottom sheet or overlay
- Smooth collapse/expand animations

### Phase 5: Deployment + Cleanup

**Goal**: Clean deployment with removed artifacts.

- Update AnalysisWorkspace webpack/Vite config for combined bundle
- Verify bundle size is acceptable (SprkChat components are already in shared library — minimal increase)
- Update deployment scripts to remove deprecated web resources
- Update `code-page-deploy` skill if needed
- Integration test: full Analysis Workspace flow with embedded chat
- Update project documentation and CLAUDE.md references

---

## Migration Safety

### Parallel Operation (Phase 1)

During Phase 1, BOTH approaches can work simultaneously:
- SprkChat panel renders inside AnalysisWorkspace (new)
- SprkChatPane side pane can still be opened (legacy — for testing/comparison)
- Both use the same BFF endpoints and session management
- This allows A/B validation before removing the legacy path in Phase 2

### Rollback

If issues are discovered after Phase 2:
- SprkChatPane Code Page is in git history
- `SprkChatBridge` is deprecated but not deleted from shared library
- Side pane launch code is in git history
- Can revert to two-pane architecture by restoring removed files

---

## Success Criteria

1. [ ] **Single page**: AnalysisWorkspace renders editor + chat in one Code Page — no side pane
2. [ ] **No BroadcastChannel**: Zero cross-pane messaging; all communication via React context/props
3. [ ] **Single auth**: One MSAL initialization, one token acquisition — verify in network tab
4. [ ] **Insert to editor**: AI response "Insert" button writes directly to Lexical editor — verify content appears
5. [ ] **Inline toolbar**: Text selection in editor → toolbar appears → action routes through chat → response inserts — full round-trip works
6. [ ] **SSE streaming**: Chat streaming writes tokens directly to editor via ref — no serialization delay
7. [ ] **Resizable panels**: User can drag splitters to resize all three panels
8. [ ] **Collapsible panels**: Source and Chat panels can be toggled on/off
9. [ ] **Reusability preserved**: SprkChat components remain in `@spaarke/ui-components` with no AnalysisWorkspace-specific dependencies
10. [ ] **SprkChatPane removed**: No `sprk_SprkChatPane` web resource in deployed solution
11. [ ] **Performance**: Page load time not significantly increased vs current (both Code Pages loaded separately)

---

## Dependencies

### Prerequisites

- All current SprkChat projects COMPLETE (Context Awareness, Workspace Companion, Platform Enhancement R2) — all merged to master

### Existing Infrastructure (Reused Unchanged)

| Component | Location | Notes |
|---|---|---|
| SprkChat component suite | `@spaarke/ui-components/components/SprkChat/` | 17 components, 10 hooks — mounted via ChatPanel wrapper |
| InlineAiToolbar | `@spaarke/ui-components/components/InlineAiToolbar/` | Already shared library; rewire from BroadcastChannel to context callback |
| SlashCommandMenu | `@spaarke/ui-components/components/SlashCommandMenu/` | Already shared library; works unchanged |
| RichTextEditor | `@spaarke/ui-components/components/RichTextEditor/` | Already in AnalysisWorkspace; unchanged |
| PanelSplitter | `AnalysisWorkspace/components/PanelSplitter.tsx` | Extend for three-panel or create PanelLayout |
| BFF Chat endpoints | `Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` | Unchanged — same API, different consumer |
| BFF Analysis context | `Sprk.Bff.Api/Api/Ai/AnalysisChatContextEndpoints.cs` | Unchanged |
| All playbook infrastructure | BFF services | Unchanged |

### What This Enables

After this project completes, the SprkChat extensibility project (`ai-sprk-chat-extensibility-r1`) builds on the unified architecture:
- Slash commands operate within the same React tree as the editor
- Compound actions can directly read/write editor content
- Smart routing has direct access to analysis context (no serialization)
- Plan preview can show editor diffs inline
- Email drafting can reference analysis content directly

---

## Risks & Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Bundle size increase | Slower initial load | SprkChat components already in shared library (tree-shaken). Measure delta; lazy-load chat panel if needed. |
| Lexical editor + SprkChat in one React tree causes render performance issues | Janky UI during streaming | Use React.memo on panels; streaming updates bypass React re-render via editor ref |
| Removing SprkChatPane breaks other consumers | Broken functionality | No other consumers exist — SprkChat is Analysis Workspace only. Verified in codebase exploration. |
| Three-panel layout too cramped on smaller screens | Poor UX on laptops | Responsive design: collapse to two panels on < 1200px; overlay/bottom sheet on < 800px |
| Migration breaks existing chat sessions | Lost conversation history | Sessions stored server-side (BFF); session ID persisted in sessionStorage. Migration preserves session key format. |

---

## References

- [AnalysisWorkspace App.tsx](../../src/client/code-pages/AnalysisWorkspace/src/App.tsx) — current 2-panel layout (743 lines)
- [SprkChatPane Code Page](../../src/client/code-pages/SprkChatPane/) — to be removed
- [SprkChat shared components](../../src/client/shared/Spaarke.UI.Components/src/components/SprkChat/) — 17 components, 10 hooks
- [SprkChatBridge](../../src/client/shared/Spaarke.UI.Components/src/services/SprkChatBridge.ts) — to be deprecated (394 lines)
- [PanelSplitter](../../src/client/code-pages/AnalysisWorkspace/src/components/PanelSplitter.tsx) — to be extended
- [usePanelResize](../../src/client/code-pages/AnalysisWorkspace/src/hooks/usePanelResize.ts) — to be extended
- [InlineAiToolbar](../../src/client/shared/Spaarke.UI.Components/src/components/InlineAiToolbar/) — rewire from bridge to context
- ADR-006: Code Pages for standalone dialogs
- ADR-012: Shared component library (callback-based props)
- ADR-021: Fluent UI v9 design system
- ADR-022: React 19 for Code Pages

---

*Design for merging AnalysisWorkspace + SprkChat into a unified Code Page. Original architecture: two-page with BroadcastChannel cross-pane communication.*
