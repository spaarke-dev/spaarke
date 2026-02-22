# Spaarke Document Studio - Design Document

> **Project**: SDAP Word Studio R2
> **Created**: February 20, 2026
> **Status**: Design
> **Author**: AI-Assisted (Claude Code)

---

## 1. Executive Summary

### The Problem

Legal professionals need powerful AI-assisted document review, editing, and analysis tools. Today, Spaarke's AI analysis features live exclusively inside Dataverse model-driven apps (the AnalysisWorkspace PCF control) and a lightweight Word add-in sidebar limited to ~350px width. Neither surface provides the full editing + AI analysis experience that competitors like Harvey, LegalOn, and Spellbook deliver.

The Word add-in sidebar is fundamentally constrained: it cannot host a rich document editor, cannot display side-by-side comparisons, and cannot surface complex AI tool panels. Yet Word remains the document format that legal professionals live in. We need a solution that bridges these two worlds.

### The Solution: Spaarke Document Studio

**Document Studio** is a browser-based application (PWA-capable) that provides a full-featured document editing and AI analysis experience. It opens `.docx` files from Spaarke's SharePoint Embedded (SPE) storage, renders them in a rich editor with track changes, and surfaces the full suite of Spaarke AI tools — analysis, redlining, chat, playbook execution — in a layout unconstrained by add-in limitations.

### Two-Surface Architecture

```
┌────────────────────────────────────────────────────────────────────┐
│                     WORD (Desktop or Web)                          │
│                                                                    │
│  ┌─────────────────────────────────────┐  ┌──────────────────────┐│
│  │                                     │  │  Spaarke Add-in      ││
│  │         Document Content            │  │  (350px sidebar)     ││
│  │                                     │  │                      ││
│  │                                     │  │  • Save to Spaarke   ││
│  │                                     │  │  • Quick AI Summary  ││
│  │                                     │  │  • Document Status   ││
│  │                                     │  │                      ││
│  │                                     │  │  ┌────────────────┐  ││
│  │                                     │  │  │ Open in Studio │  ││
│  │                                     │  │  │     →          │  ││
│  │                                     │  │  └────────────────┘  ││
│  └─────────────────────────────────────┘  └──────────────────────┘│
└────────────────────────────────────────────────────────────────────┘
                              │
                              │ "Open in Document Studio"
                              ▼
┌────────────────────────────────────────────────────────────────────┐
│               SPAARKE DOCUMENT STUDIO (Browser/PWA)                │
│                                                                    │
│  ┌──────────────────┐ ┌──────────────────┐ ┌────────────────────┐ │
│  │   AI Analysis    │ │  Document Editor  │ │   Chat / Tools     │ │
│  │    Panel         │ │  (Full TipTap)    │ │     Panel          │ │
│  │                  │ │                   │ │                    │ │
│  │  • Playbook      │ │  • Track Changes  │ │  • AI Chat         │ │
│  │    Results       │ │  • Comments       │ │  • Predefined      │ │
│  │  • Risk Flags    │ │  • Clause Nav     │ │    Prompts         │ │
│  │  • Clause List   │ │  • Redlines       │ │  • Context:        │ │
│  │  • Deviations    │ │  • Full Toolbar   │ │    Doc / Analysis  │ │
│  │  • Entity        │ │  • Split View     │ │  • Wording         │ │
│  │    Extraction    │ │  • Compare Mode   │ │    Suggestions     │ │
│  │                  │ │                   │ │  • Export Options   │ │
│  └──────────────────┘ └──────────────────┘ └────────────────────┘ │
└────────────────────────────────────────────────────────────────────┘
```

**Surface 1: Word Add-in** (Enhanced, lightweight)
- Quick actions: save, summarize, status check
- "Open in Document Studio" launcher button
- Minimal AI: quick summary, entity extraction preview
- Stays within the 350px constraint

**Surface 2: Document Studio** (Full experience)
- Full-width browser application
- Rich document editor with track changes and comments
- Complete AI analysis suite: playbooks, chat, redlining, comparisons
- Launched from Word add-in, Dataverse, or direct URL

---

## 2. Competitive Landscape

### How Competitors Handle the Word Limitation

| Vendor | Approach | Document Editing | AI Surface |
|--------|----------|-----------------|------------|
| **Harvey** | Standalone "Draft Editor" web app | Custom web editor (no Word) | Full-screen AI panels, multi-agent |
| **LegalOn** | Browser-based + Word add-in | Word for editing, browser for review | 2-surface: add-in flags issues, web shows full analysis |
| **Spellbook** | Word-native only | Word is the editor | Add-in sidebar only (~350px), clause library panel |
| **CoCounsel** | Standalone web UI | Upload-based (no live editing) | Full web experience, Deep Research |
| **Ivo** | Standalone platform | Custom contract editor | Full-screen agent workspace |
| **Wordsmith** | Unified browser platform | TipTap-based DOCX editor | Integrated editor + AI sidebar |

### Key Competitive Insights

1. **Harvey's Draft Editor** is the closest model: standalone web editor with AI superpowers, but requires users to leave Word entirely. No round-trip back to Word.

2. **LegalOn's two-surface model** is what we're proposing: lightweight Word add-in for quick checks + full browser experience for deep analysis. Their Word add-in flags issues; the browser app shows the complete picture.

3. **Spellbook's Word-only approach** hits the ceiling — they struggle to show complex analysis in 350px and have been building out their own web experiences.

4. **The winning pattern** emerging in 2026: browser-based editor with DOCX round-trip capability. Users import from Word, work in a richer environment, export back to Word. The editor must be DOCX-native, not convert to a proprietary format.

### Spaarke's Advantage

- **Microsoft ecosystem native**: SPE storage, AAD auth, Dataverse integration
- **Two-tier deployment**: customers can run the Studio in their own tenant
- **Existing AI infrastructure**: playbooks, tool handlers, RAG, streaming — all production-ready
- **Existing Word add-in**: already deployed, can serve as the launcher
- **Existing analysis patterns**: AnalysisWorkspace PCF proves the 3-panel UX works

---

## 3. Architecture

### 3.1 Technology Stack

| Layer | Technology | Rationale |
|-------|-----------|-----------|
| **Document Editor** | TipTap v3 (ProseMirror) | DOCX import/export, extensible, track changes, comments, clause-aware editing |
| **UI Framework** | React 18 + Fluent UI v9 | ADR-021 compliance, consistent with existing controls |
| **State Management** | Zustand | Lightweight, works well with React 18, no boilerplate |
| **Hosting** | Azure Static Web Apps | Same hosting as Word add-in; supports PWA |
| **Auth** | MSAL.js v3 (Browser) | AAD authentication, same app registration as BFF client |
| **API** | Existing BFF API endpoints | No new microservice (ADR-013, ADR-001) |
| **DOCX Processing** | docx-wasm (client) + Open XML SDK (server) | Client-side preview, server-side authoritative conversion |
| **SSE Streaming** | Existing SseClient pattern | Reuse from office-addins shared infrastructure |
| **Shared Components** | @spaarke/ui-components | RichTextEditor (Lexical), SprkButton, PageChrome, ChoiceDialog |

### 3.2 DOCX Round-Trip Architecture

A critical requirement is that documents maintain fidelity through the editing cycle: Word → Studio → Word. The approach uses a **hybrid client/server model**:

```
┌─────────────────────────────────────────────────────────────────┐
│                        DOCX Round-Trip                          │
│                                                                 │
│  ┌─────────┐    ┌──────────────┐    ┌──────────────────────┐   │
│  │  Word    │    │  BFF API     │    │  Document Studio     │   │
│  │  (.docx) │───►│  /documents/ │───►│  (TipTap Editor)     │   │
│  │          │    │  convert     │    │                      │   │
│  └─────────┘    │              │    │  Edit with:          │   │
│       ▲         │  • Parse OOXML│    │  • Track Changes     │   │
│       │         │  • Extract    │    │  • Comments          │   │
│       │         │    structure  │    │  • AI Suggestions    │   │
│       │         │  • Return    │    │  • Clause Navigation  │   │
│       │         │    TipTap    │    │                      │   │
│       │         │    JSON +    │    └──────────┬───────────┘   │
│       │         │    metadata  │               │               │
│       │         └──────────────┘               │ Save          │
│       │                                        ▼               │
│       │         ┌──────────────┐    ┌──────────────────────┐   │
│       │         │  BFF API     │    │  TipTap JSON +       │   │
│       └─────────│  /documents/ │◄───│  Change Tracking     │   │
│                 │  export      │    │  Metadata             │   │
│                 │              │    └──────────────────────┘   │
│                 │  • Apply edits│                               │
│                 │    to original│                               │
│                 │    OOXML     │                               │
│                 │  • Preserve  │                               │
│                 │    formatting│                               │
│                 │  • Generate  │                               │
│                 │    .docx     │                               │
│                 └──────────────┘                               │
└─────────────────────────────────────────────────────────────────┘
```

**Key principle**: The server retains the original OOXML and applies edits as a delta. This preserves formatting, styles, headers/footers, and other OOXML features that TipTap cannot represent. The client editor works with a simplified but faithful representation of the document content.

### 3.3 System Context

```
┌──────────────────────────────────────────────────────────────────────┐
│                        User's Browser                                │
│                                                                      │
│  ┌────────────────────────────────────────────────────────────────┐  │
│  │              Spaarke Document Studio (React SPA)               │  │
│  │                                                                │  │
│  │  ┌──────────┐  ┌──────────────┐  ┌─────────┐  ┌───────────┐  │  │
│  │  │ Auth     │  │ Document     │  │ Analysis│  │ Chat      │  │  │
│  │  │ (MSAL)  │  │ Editor       │  │ Engine  │  │ Engine    │  │  │
│  │  │         │  │ (TipTap)     │  │         │  │           │  │  │
│  │  └────┬─────┘  └──────┬───────┘  └────┬────┘  └─────┬─────┘  │  │
│  │       │               │               │             │         │  │
│  │       └───────────────┴───────────────┴─────────────┘         │  │
│  │                           │ BFF API Client                    │  │
│  └───────────────────────────┼────────────────────────────────────┘  │
└──────────────────────────────┼───────────────────────────────────────┘
                               │ HTTPS
                               ▼
┌──────────────────────────────────────────────────────────────────────┐
│                    Sprk.Bff.Api (.NET 8)                             │
│                                                                      │
│  ┌─────────────────┐  ┌──────────────────┐  ┌───────────────────┐   │
│  │ Document        │  │ Analysis         │  │ Document          │   │
│  │ Endpoints       │  │ Endpoints        │  │ Conversion        │   │
│  │ (existing)      │  │ (existing)       │  │ Endpoints (NEW)   │   │
│  │                 │  │                  │  │                   │   │
│  │ • GET file      │  │ • POST execute   │  │ • POST convert    │   │
│  │ • Upload file   │  │ • POST continue  │  │   (DOCX→TipTap)  │   │
│  │ • File metadata │  │ • POST save      │  │ • POST export     │   │
│  │                 │  │ • POST export    │  │   (TipTap→DOCX)   │   │
│  │                 │  │ • GET analysis   │  │ • POST redline    │   │
│  │                 │  │ • POST resume    │  │   (Apply AI edits)│   │
│  └────────┬────────┘  └────────┬─────────┘  └────────┬──────────┘   │
│           │                    │                      │              │
│           ▼                    ▼                      ▼              │
│  ┌────────────────────────────────────────────────────────────────┐  │
│  │                   Shared Services Layer                        │  │
│  │  SpeFileStore │ AnalysisOrchestration │ PlaybookEngine │ RAG  │  │
│  └────────────────────────────────────────────────────────────────┘  │
│           │                    │                      │              │
│           ▼                    ▼                      ▼              │
│  ┌────────────────┐  ┌────────────────┐  ┌────────────────────────┐ │
│  │ SharePoint     │  │ Azure OpenAI   │  │ Azure AI Search        │ │
│  │ Embedded (SPE) │  │ + Doc Intel    │  │ (RAG Index)            │ │
│  └────────────────┘  └────────────────┘  └────────────────────────┘ │
└──────────────────────────────────────────────────────────────────────┘
```

### 3.4 Application Structure

```
src/client/document-studio/
├── public/
│   ├── index.html
│   ├── manifest.webmanifest        # PWA manifest
│   └── service-worker.js           # Offline caching (future)
├── src/
│   ├── App.tsx                     # Root component, routing
│   ├── main.tsx                    # Entry point, MSAL init
│   │
│   ├── auth/
│   │   ├── AuthProvider.tsx        # MSAL React provider
│   │   ├── authConfig.ts           # AAD app registration config
│   │   └── useAuth.ts              # Auth hook (token acquisition)
│   │
│   ├── api/
│   │   ├── StudioApiClient.ts      # BFF API client (typed)
│   │   ├── SseClient.ts            # Reuse from shared (or fork)
│   │   └── types.ts                # Request/response types
│   │
│   ├── editor/
│   │   ├── DocumentEditor.tsx      # TipTap editor wrapper
│   │   ├── EditorToolbar.tsx       # Formatting toolbar
│   │   ├── extensions/
│   │   │   ├── TrackChanges.ts     # Track changes extension
│   │   │   ├── Comments.ts         # Comments extension
│   │   │   ├── ClauseMarker.ts     # AI clause boundary markers
│   │   │   ├── RiskHighlight.ts    # Risk-level highlights
│   │   │   ├── RedlineMarker.ts    # AI-suggested redlines
│   │   │   └── EntityTag.ts        # Entity annotations
│   │   ├── hooks/
│   │   │   ├── useDocxImport.ts    # DOCX → TipTap conversion
│   │   │   ├── useDocxExport.ts    # TipTap → DOCX conversion
│   │   │   ├── useAutoSave.ts      # Debounced auto-save
│   │   │   └── useClauseNav.ts     # Clause-to-clause navigation
│   │   └── utils/
│   │       ├── docxParser.ts       # Client-side DOCX preview parse
│   │       └── diffEngine.ts       # Document comparison logic
│   │
│   ├── analysis/
│   │   ├── AnalysisPanel.tsx       # Left panel: analysis output
│   │   ├── PlaybookSelector.tsx    # Playbook dropdown + config
│   │   ├── ClauseList.tsx          # Clause navigation list
│   │   ├── RiskDashboard.tsx       # Risk summary visualization
│   │   ├── DeviationReport.tsx     # Standard terms comparison
│   │   ├── EntityTable.tsx         # Extracted entities display
│   │   └── hooks/
│   │       ├── useAnalysis.ts      # Analysis execution + SSE
│   │       ├── usePlaybook.ts      # Playbook loading + execution
│   │       └── useDeviations.ts    # Deviation detection results
│   │
│   ├── chat/
│   │   ├── ChatPanel.tsx           # Right panel: AI conversation
│   │   ├── ChatMessage.tsx         # Individual message component
│   │   ├── ChatInput.tsx           # Input with predefined prompts
│   │   ├── ContextSwitch.tsx       # Document vs Analysis context
│   │   ├── PredefinedPrompts.tsx   # Copilot-style suggestion chips
│   │   └── hooks/
│   │       ├── useChat.ts          # Chat state + SSE streaming
│   │       └── useChatContext.ts   # Context switching logic
│   │
│   ├── redline/
│   │   ├── RedlinePanel.tsx        # AI redline suggestions view
│   │   ├── RedlineSuggestion.tsx   # Individual suggestion card
│   │   ├── AcceptRejectControls.tsx # Accept/reject/modify actions
│   │   └── hooks/
│   │       └── useRedline.ts       # Redline generation + application
│   │
│   ├── compare/
│   │   ├── CompareView.tsx         # Side-by-side version comparison
│   │   ├── DiffHighlight.tsx       # Diff rendering
│   │   └── VersionSelector.tsx     # Version picker
│   │
│   ├── layout/
│   │   ├── StudioLayout.tsx        # Main 3-panel layout
│   │   ├── PanelResizer.tsx        # Resizable panel dividers
│   │   ├── StudioHeader.tsx        # Top bar: file name, actions, nav
│   │   └── StatusBar.tsx           # Bottom: word count, AI status
│   │
│   ├── store/
│   │   ├── documentStore.ts        # Document state (Zustand)
│   │   ├── analysisStore.ts        # Analysis state
│   │   ├── chatStore.ts            # Chat history state
│   │   └── uiStore.ts              # UI preferences (panel widths)
│   │
│   └── shared/
│       ├── theme.ts                # Fluent UI v9 theme + dark mode
│       └── constants.ts            # API routes, defaults
│
├── package.json
├── tsconfig.json
├── vite.config.ts                  # Vite bundler config
└── staticwebapp.config.json        # Azure SWA routing
```

---

## 4. User Experience

### 4.1 Entry Points

Users can launch Document Studio from multiple entry points:

| Entry Point | How | URL Pattern |
|-------------|-----|-------------|
| **Word Add-in** | "Open in Studio" button in sidebar | `studio.spaarke.com/doc/{driveId}/{itemId}` |
| **Dataverse MDA** | Button on Document or Analysis form | `studio.spaarke.com/doc/{driveId}/{itemId}?analysisId={id}` |
| **Direct URL** | Bookmark, shared link, or notification | `studio.spaarke.com/doc/{driveId}/{itemId}` |
| **Dataverse PCF** | "Open in Studio" from AnalysisWorkspace | `studio.spaarke.com/analysis/{analysisId}` |

### 4.2 Document Loading Flow

```
User clicks "Open in Studio"
         │
         ▼
┌─────────────────────┐
│ Studio loads in      │
│ new browser tab      │
│                      │
│ 1. MSAL auth check   │──── Not authenticated ──► Login redirect
│ 2. Acquire token     │
└──────────┬──────────┘
           │ Authenticated
           ▼
┌─────────────────────┐
│ Load document from   │
│ SPE via BFF API      │
│                      │
│ GET /api/documents/  │
│   {driveId}/{itemId} │
│   /content           │
└──────────┬──────────┘
           │ .docx binary
           ▼
┌─────────────────────┐
│ Convert DOCX to      │
│ editor format        │
│                      │
│ POST /api/studio/    │
│   convert            │
│ Body: DOCX binary    │
│ Returns: TipTap JSON │
│   + metadata         │
│   + original OOXML   │
│     (cached server)  │
└──────────┬──────────┘
           │ TipTap JSON
           ▼
┌─────────────────────┐
│ Render in editor     │
│                      │
│ • Initialize TipTap  │
│ • Apply clause marks │
│ • Restore session    │
│   (if analysisId)    │
│ • Show ready state   │
└─────────────────────┘
```

### 4.3 Layout and Panels

The main layout follows the proven AnalysisWorkspace 3-panel pattern, but with the document editor as the primary surface:

```
┌──────────────────────────────────────────────────────────────────┐
│  Studio Header: [← Back] [Document Name ▼]  [Save] [Export ▼]  │
├──────────────┬───────────────────────────┬───────────────────────┤
│  Analysis    │    Document Editor        │    Chat / Tools       │
│  Panel       │                           │    Panel              │
│  (300px,     │    (flex, primary)        │    (380px,            │
│  collapsible)│                           │    collapsible)       │
│              │  ┌──────────────────────┐ │                       │
│  [Playbook ▼]│  │ Toolbar: B I U | ¶   │ │  [Context: Doc ▼]    │
│              │  │ H1 H2 | • — | ↩ ↪   │ │                       │
│  ┌──────────┐│  └──────────────────────┘ │  ┌───────────────┐   │
│  │ Clauses  ││                           │  │ Chat Messages │   │
│  │ 1. Def.. ││  Document content with    │  │               │   │
│  │ 2. Term..││  rich formatting,         │  │ AI: "Based on │   │
│  │ 3. Conf..││  track changes shown      │  │  the NDA..."  │   │
│  │ 4. Indem.││  inline, AI highlights    │  │               │   │
│  │ 5. Liab..││  for risks and entities   │  │ You: "What    │   │
│  │ ▸ More   ││                           │  │  about the    │   │
│  └──────────┘│                           │  │  indemnity?"  │   │
│              │                           │  │               │   │
│  ┌──────────┐│  ┌──────────────────────┐ │  └───────────────┘   │
│  │ Risks  ⚠ ││  │ [AI Suggestion]      │ │                       │
│  │ High: 2  ││  │ Replace "reasonable  │ │  ┌───────────────┐   │
│  │ Med:  5  ││  │ efforts" with        │ │  │ Suggestions   │   │
│  │ Low:  3  ││  │ "commercially        │ │  │ ┌───────────┐ │   │
│  └──────────┘│  │ reasonable efforts"  │ │  │ │ Summarize │ │   │
│              │  │ [Accept] [Reject]    │ │  │ │ key terms │ │   │
│  ┌──────────┐│  └──────────────────────┘ │  │ ├───────────┤ │   │
│  │ Entities ││                           │  │ │ Flag risks│ │   │
│  │ Parties: ││                           │  │ ├───────────┤ │   │
│  │  - Acme  ││                           │  │ │ Suggest   │ │   │
│  │  - Widget││                           │  │ │ redlines  │ │   │
│  │ Dates:   ││                           │  │ └───────────┘ │   │
│  │  - 3/1/26││                           │  └───────────────┘   │
│  └──────────┘│                           │                       │
├──────────────┴───────────────────────────┴───────────────────────┤
│  Status: Words: 4,521 | Clauses: 12 | AI: Ready | Auto-saved ✓ │
└──────────────────────────────────────────────────────────────────┘
```

### 4.4 Core User Workflows

#### Workflow 1: Contract Review with Playbook

1. User opens contract from Word → "Open in Studio"
2. Studio loads document in TipTap editor
3. User selects "Full NDA Analysis" playbook from dropdown
4. Studio calls `POST /api/ai/analysis/execute` with playbook scope
5. SSE streams analysis results into the Analysis Panel:
   - Clauses identified and marked in editor (highlight + margin markers)
   - Risk flags attached to specific clauses (color-coded)
   - Entities extracted and listed
   - Deviations from standard terms flagged
6. User clicks a clause in the Analysis Panel → editor scrolls to clause
7. User asks in chat: "Is the indemnity clause mutual?"
8. AI responds with analysis, referencing specific paragraph
9. User requests redline: "Suggest improvements for the liability cap"
10. AI generates redline suggestions as track changes in the editor
11. User accepts/rejects each suggestion
12. User clicks "Save" → DOCX written back to SPE with changes
13. User clicks "Export to Email" → sends redlined version to counterparty

#### Workflow 2: AI-Assisted Drafting

1. User opens a template document in Studio
2. User asks chat: "Draft a confidentiality clause for a SaaS vendor agreement"
3. AI generates clause text based on playbook + knowledge sources
4. Text appears as a "pending insertion" in the editor at cursor position
5. User accepts insertion → text added with "AI-generated" attribution
6. User highlights a paragraph and asks: "Make this more favorable to us"
7. AI generates alternative wording as a tracked change
8. User refines through conversation

#### Workflow 3: Document Comparison

1. User opens "Contract v3" in Studio
2. User clicks "Compare" → selects "Contract v2" from SPE
3. Studio loads both versions and computes diff
4. Split view shows: Original (left) | Current (right) with diff highlighting
5. AI summarizes changes: "12 clauses modified, 3 new clauses added, 2 removed"
6. User can chat about specific differences

### 4.5 Panel Behavior

| Panel | Default Width | Min Width | Collapsible | Keyboard |
|-------|-------------|-----------|-------------|----------|
| Analysis (left) | 300px | 250px | Yes (toggle) | `Ctrl+1` |
| Editor (center) | flex (remaining) | 400px | No | — |
| Chat (right) | 380px | 300px | Yes (toggle) | `Ctrl+3` |

When both side panels are collapsed, the editor takes full width — useful for focused editing.

---

## 5. Document Editor (TipTap)

### 5.1 Why TipTap

| Criterion | TipTap (ProseMirror) | Lexical (Meta) | Slate.js |
|-----------|---------------------|----------------|----------|
| DOCX support | Strong (docx packages) | Limited | Limited |
| Track changes | Tiptap Pro extension | Custom build | Custom build |
| Comments | Tiptap Pro extension | Custom build | Custom build |
| Extensibility | Extension-based, rich ecosystem | Plugin-based | Plugin-based |
| Collaborative editing | Yjs integration built-in | Requires custom | Requires custom |
| Legal document features | Multiple legal-tech adopters | Few legal-tech adopters | Few |
| Performance | Excellent for long documents | Excellent | Good |
| License | Open source core + paid Pro | MIT | MIT |

**Note**: The existing shared `RichTextEditor` component is Lexical-based and works well for the AnalysisWorkspace's working document (simple rich text editing). For Document Studio, we need DOCX-native editing with track changes and comments — TipTap's Pro extensions provide this out of the box.

### 5.2 TipTap Extensions

**Core Extensions** (open source):
- `StarterKit` — Basic formatting (bold, italic, headings, lists, etc.)
- `Table` — Table support for contract schedules
- `Link` — Hyperlinks
- `TextAlign` — Paragraph alignment
- `Placeholder` — Empty document placeholder
- `CharacterCount` — Word/character count for status bar
- `History` — Undo/redo

**Pro Extensions** (licensed):
- `TrackChanges` — Track insertions, deletions, format changes per author
- `Comments` — Inline comments with threads
- `FileHandler` — Drag-and-drop file handling

**Custom Extensions** (Spaarke-built):
- `ClauseMarker` — Mark clause boundaries from AI analysis; click to navigate
- `RiskHighlight` — Color-coded risk indicators on text ranges (red/amber/green)
- `RedlineMarker` — AI-suggested changes displayed as pending track changes
- `EntityTag` — Inline entity annotations (party names, dates, amounts)
- `DeviationFlag` — Mark text that deviates from standard terms
- `AiInsertionPoint` — Visual marker for where AI-generated text will be inserted

### 5.3 DOCX Conversion Strategy

**Import (DOCX → TipTap JSON)**:
```
DOCX binary
    │
    ▼ (Server: Open XML SDK)
Parse OOXML structure
    │
    ├── Extract paragraphs, styles, formatting
    ├── Extract tables and nested structures
    ├── Extract headers/footers (metadata only)
    ├── Extract comments and tracked changes
    ├── Extract images (store as SPE references)
    │
    ▼
Generate TipTap JSON document
    │
    ├── Map Word styles → TipTap marks/nodes
    ├── Preserve paragraph IDs for clause mapping
    ├── Convert existing track changes → TrackChanges marks
    ├── Convert comments → Comments annotations
    │
    ▼
Return to client:
{
  content: TipTapJSON,
  metadata: {
    wordCount: number,
    pageCount: number,
    authors: string[],
    styles: StyleMap,      // For preserving style names
    sections: SectionInfo  // Page layout (margins, etc.)
  },
  sessionId: string        // Server caches original OOXML
}
```

**Export (TipTap JSON → DOCX)**:
```
Client sends:
{
  content: TipTapJSON,         // Current editor state
  changes: ChangeSet,          // Track changes to apply
  sessionId: string,           // Retrieve cached original
  options: {
    acceptAllChanges: boolean, // Accept all before export?
    includeComments: boolean,  // Include comment annotations?
    format: "docx" | "pdf"
  }
}
    │
    ▼ (Server: Open XML SDK)
Load cached original OOXML
    │
    ├── Apply paragraph-level edits to original
    ├── Preserve all non-content OOXML (styles, themes, etc.)
    ├── Apply tracked changes as Word revision marks
    ├── Embed comments in Word comment format
    │
    ▼
Generate .docx binary
    │
    ▼
Return to client (or save to SPE)
```

### 5.4 Clause-Aware Editing

After AI analysis identifies clause boundaries, the editor marks them:

```typescript
// ClauseMarker extension marks clause regions
interface ClauseMark {
  clauseId: string;           // "clause-1", "clause-2", etc.
  clauseType: string;         // "definition", "termination", "indemnity"
  riskLevel: "high" | "medium" | "low" | "none";
  startParagraphId: string;   // Maps to OOXML paragraph IDs
  endParagraphId: string;
}
```

The Analysis Panel's clause list and the editor's clause markers are bidirectionally linked: clicking a clause in the list scrolls the editor; clicking in the editor highlights the clause in the list.

---

## 6. AI Integration

### 6.1 Existing API Endpoints (Reused)

All AI operations go through the existing BFF API. No new AI microservice.

| Endpoint | Method | Purpose | Used By |
|----------|--------|---------|---------|
| `/api/ai/analysis/execute` | POST (SSE) | Run playbook analysis | Analysis Panel |
| `/api/ai/analysis/{id}/continue` | POST (SSE) | Chat continuation | Chat Panel |
| `/api/ai/analysis/{id}/save` | POST | Save working document | Auto-save |
| `/api/ai/analysis/{id}/export` | POST | Export to email/PDF/DOCX | Export menu |
| `/api/ai/analysis/{id}` | GET | Load analysis + history | Session resume |
| `/api/ai/analysis/{id}/resume` | POST | Resume session | Entry from URL |

### 6.2 New API Endpoints (Document Studio Specific)

These endpoints extend the BFF API for document conversion and redlining:

```
/api/studio/
├── POST /convert                    # DOCX → TipTap JSON
│   Body: multipart/form-data (docx file)
│   Response: { content, metadata, sessionId }
│
├── POST /export                     # TipTap JSON → DOCX
│   Body: { content, changes, sessionId, options }
│   Response: binary .docx (or save to SPE)
│
├── POST /redline                    # Generate AI redline suggestions
│   Body: { analysisId, clauseId?, instruction, context }
│   Response (SSE): redline suggestions with track change format
│
├── POST /compare                    # Compare two document versions
│   Body: { sourceDocId, targetDocId }
│   Response: { diffs[], summary, statistics }
│
├── GET  /session/{sessionId}        # Retrieve cached session
│   Response: { metadata, hasUnsavedChanges }
│
└── DELETE /session/{sessionId}      # Clean up server-side cache
```

### 6.3 Redline Generation Flow

The redline feature is a key differentiator. When a user requests AI-suggested changes:

```
User: "Suggest improvements for the indemnity clause"
         │
         ▼
POST /api/studio/redline
{
  analysisId: "...",
  clauseId: "clause-7",          // Optional: scope to specific clause
  instruction: "Suggest improvements for the indemnity clause",
  context: {
    originalText: "...",          // Clause text from editor
    playbookId: "...",            // Active playbook for standards
    riskLevel: "high"             // Current risk assessment
  }
}
         │
         ▼ (Server: PlaybookEngine + OpenAI)
1. Load playbook standards for indemnity clauses
2. Retrieve knowledge context (standard terms, best practices)
3. Generate suggestions via OpenAI with structured output:
   - Original text segment
   - Suggested replacement
   - Reasoning for change
   - Confidence level
         │
         ▼ (SSE streaming)
{
  type: "redline-suggestion",
  data: {
    id: "sug-1",
    originalText: "Party shall indemnify...",
    suggestedText: "Party shall defend, indemnify, and hold harmless...",
    reason: "Strengthens protection by adding 'defend' and 'hold harmless' language",
    confidence: 0.92,
    paragraphId: "p-42",
    startOffset: 0,
    endOffset: 31
  }
}
         │
         ▼ (Client: TipTap)
Apply as TrackChanges marks in editor:
  - Deletion mark on original text (strikethrough, red)
  - Insertion mark on suggested text (underline, green)
  - Tooltip shows AI reasoning
  - Accept/Reject buttons inline
```

### 6.4 Chat Context Switching (ENH-001)

Document Studio natively supports the chat context switching enhancement:

```typescript
type ChatContext = "document" | "analysis";

// When context = "document":
//   System prompt references raw document content
//   User questions are about the file itself
//
// When context = "analysis":
//   System prompt references the analysis output (structured findings)
//   User questions are about the AI's findings
//
// Switching context:
//   Chat history is preserved
//   System prompt changes
//   Visual indicator updates in Chat Panel header
```

### 6.5 Predefined Prompts (ENH-002)

Context-aware prompt suggestions appear as chips above the chat input:

| Context | Document Type | Suggestions |
|---------|--------------|-------------|
| Document + Contract | Any | "Summarize key terms", "List all parties", "What is the term?" |
| Document + NDA | NDA | "Is this mutual?", "What's the non-compete scope?", "Surviving obligations?" |
| Analysis + Any | Any | "Summarize findings", "What are the high risks?", "Explain this deviation" |
| Analysis + Post-redline | Any | "Review my changes", "Any remaining issues?", "Prepare negotiation summary" |

Suggestions are loaded from:
1. Static per-playbook definitions (from `sprk_aiplaybook` configuration)
2. Dynamic based on analysis results (from AI analysis metadata)
3. User favorites (from `sprk_aiprompttemplate` per ENH-003)

---

## 7. Word Add-in Enhancement

### 7.1 Updated Word Add-in Scope

The existing Word add-in is enhanced with:

1. **"Open in Studio" button** — Primary action, launches Document Studio in browser
2. **Quick Summary** — Lightweight analysis in the sidebar (no full playbook)
3. **Document Status** — Shows if analysis exists, last modified, linked entities
4. **Save to Spaarke** — Existing functionality (save document to SPE)

### 7.2 "Open in Studio" Implementation

```typescript
// In Word add-in command handler
async function openInStudio(): Promise<void> {
  // 1. Get current document identity
  const adapter = HostAdapterFactory.create();
  const itemId = await adapter.getItemId();

  // 2. Determine document's SPE location
  //    (document may already be saved to Spaarke, or needs saving first)
  const documentInfo = await apiClient.getDocumentBySpeId(itemId);

  if (!documentInfo) {
    // Document not yet in Spaarke — prompt save first
    const saved = await promptSaveToSpaarke();
    if (!saved) return;
  }

  // 3. Build Studio URL
  const studioUrl = buildStudioUrl({
    driveId: documentInfo.driveId,
    itemId: documentInfo.itemId,
    analysisId: documentInfo.latestAnalysisId, // Optional
  });

  // 4. Open in new browser tab
  window.open(studioUrl, '_blank');
}

function buildStudioUrl(params: StudioUrlParams): string {
  const base = getStudioBaseUrl(); // From environment config
  let url = `${base}/doc/${params.driveId}/${params.itemId}`;
  if (params.analysisId) {
    url += `?analysisId=${params.analysisId}`;
  }
  return url;
}
```

### 7.3 Updated Word Manifest

The Word add-in manifest is updated to include the Studio launcher button:

```xml
<!-- New button in the Spaarke ribbon group -->
<Control xsi:type="Button" id="StudioButton">
  <Label resid="StudioButton.Label"/>
  <Supertip>
    <Title resid="StudioButton.SupertipTitle"/>
    <Description resid="StudioButton.SupertipText"/>
  </Supertip>
  <Icon>
    <bt:Image size="16" resid="StudioIcon.16x16"/>
    <bt:Image size="32" resid="StudioIcon.32x32"/>
    <bt:Image size="80" resid="StudioIcon.80x80"/>
  </Icon>
  <Action xsi:type="ExecuteFunction">
    <FunctionName>openInStudio</FunctionName>
  </Action>
</Control>
```

Resource strings:
```xml
<bt:String id="StudioButton.Label" DefaultValue="Document Studio"/>
<bt:String id="StudioButton.SupertipTitle" DefaultValue="Open in Spaarke Document Studio"/>
<bt:String id="StudioButton.SupertipText"
  DefaultValue="Open this document in Spaarke Document Studio for full AI-powered review, analysis, and redlining"/>
```

---

## 8. Authentication and Authorization

### 8.1 Auth Flow

Document Studio uses the same MSAL-based auth as the existing Word add-in and PCF controls:

```
Browser (Document Studio)
    │
    ├── MSAL.js v3 (PublicClientApplication)
    │   └── AAD App Registration: Spaarke BFF Client (existing)
    │       └── Scopes: api://{bff-api-client-id}/access_as_user
    │
    ▼ Token
BFF API (Sprk.Bff.Api)
    │
    ├── Validates token (existing middleware)
    ├── OBO exchange for Graph token (existing)
    │   └── Access SPE files on behalf of user
    ├── Access AI services (app identity)
    │
    ▼
Azure Resources (OpenAI, AI Search, SPE, etc.)
```

### 8.2 Authorization

Document-level authorization follows existing patterns:

- **SPE access**: OBO token grants access only to containers the user has permission for
- **Analysis access**: Endpoint filter validates user owns/has access to the analysis record
- **Playbook access**: Filtered by organization (tenant-level scope)
- **Knowledge sources**: Access controlled by knowledge source visibility settings

No new authorization patterns are needed — Document Studio uses the same BFF endpoints with the same authorization filters.

---

## 9. Deployment Models

### 9.1 Model 1: Spaarke-Hosted (Multi-Tenant)

```
┌─────────────────────────────────────────────────────────────┐
│                    Spaarke Infrastructure                     │
│                                                              │
│  ┌──────────────────┐  ┌──────────────────────────────────┐ │
│  │ Azure Static     │  │ Sprk.Bff.Api (shared)            │ │
│  │ Web App          │  │                                   │ │
│  │                  │  │ • Studio endpoints                │ │
│  │ • Document Studio│  │ • Analysis endpoints              │ │
│  │   (React SPA)   │  │ • Document endpoints              │ │
│  │ • Word Add-in   │  │ • MULTI_TENANT_MODE=true          │ │
│  │   (shared host)  │  │                                   │ │
│  │                  │  │ Document conversion uses          │ │
│  │ URL:            │  │ shared compute (App Service Plan) │ │
│  │ studio.spaarke  │  │                                   │ │
│  │   .com          │  │ Per-tenant session isolation       │ │
│  └──────────────────┘  │ via tenant ID prefix on cache     │ │
│                         └──────────────────────────────────┘ │
│                                                              │
│  ┌──────────────────────────────────────────────────────────┐│
│  │ Shared AI Resources                                       ││
│  │ • Azure OpenAI (shared, rate-limited per tenant)         ││
│  │ • AI Search (per-tenant indexes)                          ││
│  │ • Redis (per-tenant key prefix)                           ││
│  │ • Storage (per-tenant containers for session cache)       ││
│  └──────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────┘
```

**Key considerations for Model 1:**
- Document conversion sessions cached in shared Redis with tenant prefix
- OOXML cache stored in shared blob storage with tenant-scoped containers
- Rate limiting applies per-tenant on AI operations
- Studio SPA is a single deployment serving all tenants (tenant resolved from token)
- Custom domains per customer (e.g., `studio.acmecorp.spaarke.com`) via SWA custom domains

### 9.2 Model 2: Customer-Hosted (Dedicated)

```
┌─────────────────────────────────────────────────────────────┐
│                 Customer's Azure Subscription                │
│                                                              │
│  ┌──────────────────┐  ┌──────────────────────────────────┐ │
│  │ Azure Static     │  │ Sprk.Bff.Api (dedicated)         │ │
│  │ Web App          │  │                                   │ │
│  │ (dedicated)      │  │ • Studio endpoints                │ │
│  │                  │  │ • Analysis endpoints              │ │
│  │ • Document Studio│  │ • MULTI_TENANT_MODE=false         │ │
│  │ • Word Add-in   │  │                                   │ │
│  │                  │  │ Customer controls:                │ │
│  │ URL: studio.    │  │ • OpenAI model/capacity           │ │
│  │ customer.com    │  │ • AI Search config                │ │
│  └──────────────────┘  │ • Data residency                  │ │
│                         └──────────────────────────────────┘ │
│                                                              │
│  ┌──────────────────────────────────────────────────────────┐│
│  │ Dedicated AI Resources                                    ││
│  │ • Azure OpenAI (customer's subscription)                 ││
│  │ • AI Search (customer's subscription)                     ││
│  │ • Redis (customer's subscription)                         ││
│  │ • Storage (customer's subscription)                       ││
│  └──────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────┘
```

**Key considerations for Model 2:**
- Customer's IT manages Azure AI resources, capacity, and costs
- Document conversion sessions in customer's own Redis/Storage
- OOXML caching in customer's blob storage (data sovereignty compliance)
- Customer can choose their own OpenAI model deployments
- Studio SPA deployed to customer's Static Web App (same binary, different config)
- Word add-in manifest points to customer's Studio URL

### 9.3 Configuration Abstraction

Document Studio uses the same environment-based configuration pattern as the BFF API:

```typescript
// Studio configuration resolved at runtime
interface StudioConfig {
  apiBaseUrl: string;           // BFF API endpoint
  authClientId: string;         // AAD app registration
  authAuthority: string;        // AAD tenant authority
  apiScopes: string[];          // BFF API scopes
  studioFeatures: {
    enableComparison: boolean;  // Model 2 may disable if no AI Search
    enableCollaboration: boolean; // Future: real-time co-editing
    maxDocumentSizeMb: number;  // May differ by deployment
  };
}

// Resolved from:
// 1. Static config (build-time defaults)
// 2. /api/config endpoint (runtime from BFF)
// 3. Environment variables (Azure SWA app settings)
```

---

## 10. Data Flow and State Management

### 10.1 Document State

```typescript
// documentStore.ts (Zustand)
interface DocumentState {
  // Identity
  driveId: string | null;
  itemId: string | null;
  sessionId: string | null;       // Server-side OOXML cache key

  // Content
  content: JSONContent | null;     // TipTap JSON
  metadata: DocumentMetadata | null;

  // Edit state
  isDirty: boolean;
  lastSavedAt: Date | null;
  changeHistory: ChangeRecord[];   // Track changes log

  // Actions
  loadDocument: (driveId: string, itemId: string) => Promise<void>;
  saveDocument: (options?: SaveOptions) => Promise<void>;
  exportDocument: (format: ExportFormat) => Promise<void>;
  applyRedline: (suggestion: RedlineSuggestion) => void;
  rejectRedline: (suggestionId: string) => void;
}
```

### 10.2 Analysis State

```typescript
// analysisStore.ts (Zustand)
interface AnalysisState {
  analysisId: string | null;
  playbookId: string | null;
  status: "idle" | "executing" | "complete" | "error";

  // Results
  clauses: ClauseResult[];
  risks: RiskResult[];
  entities: EntityResult[];
  deviations: DeviationResult[];

  // Actions
  executeAnalysis: (playbookId: string) => Promise<void>;
  selectClause: (clauseId: string) => void;
  requestRedline: (clauseId: string, instruction: string) => void;
}
```

### 10.3 Auto-Save Strategy

```
Editor change event
    │
    ▼ (3-second debounce)
Save to BFF API
    │
    ├── POST /api/ai/analysis/{id}/save  (if analysis active)
    │   Body: { workingDocument: TipTapJSON, chatHistory }
    │
    └── POST /api/studio/session/{id}    (editor-only save)
        Body: { content: TipTapJSON, changes: ChangeSet }
    │
    ▼
Update status bar: "Saved ✓" with timestamp
```

Auto-save targets:
1. **Analysis record** (`sprk_analysis`): Working document markdown + chat history (same as AnalysisWorkspace)
2. **Session cache** (Redis/Blob): TipTap JSON + change tracking metadata (for DOCX round-trip)

### 10.4 Session Lifecycle

```
Open Document
    │
    ▼
Create Session (server caches original OOXML)
    │
    ├── Session ID returned to client
    ├── OOXML stored in blob storage (24-hour TTL)
    ├── Metadata cached in Redis (1-hour TTL, refreshed on activity)
    │
    ▼
Edit / Analyze / Chat
    │
    ├── Auto-save every 3 seconds (debounced)
    ├── Session TTL refreshed on every API call
    │
    ▼
Save / Export
    │
    ├── DOCX regenerated from original OOXML + edits
    ├── Saved to SPE (original file updated or new version)
    │
    ▼
Close Tab / Navigate Away
    │
    ├── beforeunload: warn if unsaved changes
    ├── Session cleanup after TTL expiry (server-side)
    └── Analysis record persists in Dataverse (survives session)
```

---

## 11. Responsive Design and Accessibility

### 11.1 Responsive Breakpoints

| Viewport | Layout | Behavior |
|----------|--------|----------|
| >= 1440px | 3-panel (Analysis + Editor + Chat) | Full experience |
| 1024-1439px | 2-panel (Editor + one side panel) | Toggle between Analysis/Chat |
| 768-1023px | Single panel with tabs | Swipe/tab between Editor, Analysis, Chat |
| < 768px | Not supported | Show "Please use a larger screen" message |

### 11.2 Accessibility Requirements (WCAG 2.1 AA)

- All panels keyboard navigable (`Tab`, `Shift+Tab`, arrow keys)
- Screen reader announcements for AI streaming events
- High contrast mode support (Fluent UI v9 built-in)
- Focus management when panels collapse/expand
- Aria-live regions for chat messages and analysis results
- Keyboard shortcuts documented and customizable

### 11.3 Dark Mode

Per ADR-021, full dark mode support is required:
- Use Fluent UI v9 `webDarkTheme` tokens
- TipTap editor theme follows Fluent tokens
- Analysis Panel risk colors accessible in both modes
- Track changes colors distinguishable in both modes

---

## 12. Performance Considerations

### 12.1 Document Size Targets

| Document Size | Load Time Target | Editor Performance |
|--------------|-----------------|-------------------|
| < 10 pages | < 2 seconds | Smooth (60fps) |
| 10-50 pages | < 5 seconds | Smooth with virtualization |
| 50-200 pages | < 10 seconds | Virtualized rendering |
| > 200 pages | Warning + progressive load | Section-based loading |

### 12.2 Optimization Strategies

- **DOCX conversion**: Server-side, cached. Client receives ready-to-render JSON.
- **Editor rendering**: TipTap with ProseMirror provides efficient DOM diffing.
- **Large documents**: Section-based loading — only render visible sections, load others on scroll.
- **SSE streaming**: Same proven pattern from AnalysisWorkspace.
- **Image handling**: Images stored as SPE references, loaded lazily via signed URLs.
- **Session caching**: Redis for hot data (metadata, session state), Blob for cold data (OOXML).

### 12.3 Bundle Size

| Component | Estimated Size (gzip) |
|-----------|---------------------|
| React + Fluent UI v9 | ~120 KB |
| TipTap core + extensions | ~80 KB |
| TipTap Pro (Track Changes, Comments) | ~40 KB |
| MSAL.js | ~30 KB |
| Zustand + utilities | ~10 KB |
| App code | ~60 KB |
| **Total** | **~340 KB** |

Code splitting: Analysis Panel, Chat Panel, and Compare View loaded on demand.

---

## 13. New BFF API Endpoints (Detailed)

### 13.1 Endpoint Registration

Following ADR-001 Minimal API patterns:

```csharp
// Api/Studio/StudioEndpoints.cs
public static class StudioEndpoints
{
    public static void MapStudioEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/studio")
            .RequireAuthorization()
            .WithTags("Studio");

        group.MapPost("/convert", ConvertDocx)
            .AddEndpointFilter<RateLimitFilter>("studio-convert")
            .DisableAntiforgery();

        group.MapPost("/export", ExportDocx)
            .AddEndpointFilter<RateLimitFilter>("studio-export");

        group.MapPost("/redline", GenerateRedline)
            .AddEndpointFilter<RateLimitFilter>("ai-stream");

        group.MapPost("/compare", CompareDocuments)
            .AddEndpointFilter<RateLimitFilter>("ai-batch");

        group.MapGet("/session/{sessionId}", GetSession);

        group.MapDelete("/session/{sessionId}", CleanupSession);
    }
}
```

### 13.2 Document Conversion Service

```csharp
// Services/Studio/IDocumentConversionService.cs
public interface IDocumentConversionService
{
    /// <summary>
    /// Convert a DOCX file to TipTap-compatible JSON representation.
    /// Caches the original OOXML for round-trip fidelity.
    /// </summary>
    Task<ConversionResult> ConvertToEditorFormatAsync(
        Stream docxStream,
        string fileName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Export editor content back to DOCX format.
    /// Applies edits to the cached original OOXML to preserve formatting.
    /// </summary>
    Task<Stream> ExportToDocxAsync(
        ExportRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Compare two documents and return structured diff.
    /// </summary>
    Task<ComparisonResult> CompareDocumentsAsync(
        Guid sourceDocId,
        Guid targetDocId,
        CancellationToken cancellationToken);
}

public record ConversionResult(
    JsonDocument Content,           // TipTap JSON
    DocumentMetadata Metadata,
    string SessionId);              // Cache reference

public record ExportRequest(
    JsonDocument Content,
    string SessionId,
    ExportOptions Options);

public record ComparisonResult(
    DiffSegment[] Diffs,
    string Summary,
    ComparisonStatistics Statistics);
```

### 13.3 Redline Generation Service

```csharp
// Services/Studio/IRedlineService.cs
public interface IRedlineService
{
    /// <summary>
    /// Generate AI redline suggestions for a clause or document section.
    /// Returns suggestions via SSE streaming.
    /// </summary>
    IAsyncEnumerable<RedlineSuggestion> GenerateRedlineAsync(
        RedlineRequest request,
        CancellationToken cancellationToken);
}

public record RedlineRequest(
    Guid AnalysisId,
    string? ClauseId,               // Null = whole document
    string Instruction,             // User's request
    RedlineContext Context);

public record RedlineContext(
    string OriginalText,            // Text to improve
    Guid? PlaybookId,               // For standard terms
    string? RiskLevel);             // Current risk assessment

public record RedlineSuggestion(
    string Id,
    string OriginalText,
    string SuggestedText,
    string Reason,                  // AI's rationale
    double Confidence,              // 0.0 - 1.0
    string ParagraphId,             // For editor positioning
    int StartOffset,
    int EndOffset);
```

---

## 14. Relationship to Existing Enhancements

Document Studio addresses or enables several items from the enhancement backlog:

| Enhancement | How Document Studio Addresses It |
|-------------|--------------------------------|
| **ENH-001**: Chat Context Switching | Native support — context toggle in Chat Panel |
| **ENH-002**: Predefined Prompts | Copilot-style chips in Chat Panel, context-aware |
| **ENH-003**: Prompt Library | `/prompts` command + favorites accessible from Chat Panel |
| **ENH-004**: AI Wording Refinement | Core feature — redline generation with track changes |
| **ENH-005**: Deviation Detection | Analysis Panel shows deviation report with clause-level flags |
| **ENH-006**: Ambiguity Detection | Future tool handler, results display in Analysis Panel |
| **ENH-008**: Version Comparison | Compare View with side-by-side diff |

Document Studio does **not** replace the AnalysisWorkspace PCF — that control continues to serve the Dataverse model-driven app experience. Document Studio is the expanded experience for users who need full editing + AI capabilities.

---

## 15. Implementation Phases

### Phase 1: Foundation (8-10 weeks)

**Goal**: Document loads in TipTap editor with basic formatting, connected to BFF API.

| Task | Scope | Estimate |
|------|-------|----------|
| Project scaffolding (Vite + React + Fluent UI v9) | Setup | 1 week |
| MSAL auth integration | Auth | 1 week |
| BFF API client + types | API | 1 week |
| TipTap editor with core extensions | Editor | 2 weeks |
| DOCX conversion service (server-side, Open XML SDK) | API | 2 weeks |
| 3-panel layout (StudioLayout) | UI | 1 week |
| Document load from SPE + render flow | Integration | 1 week |
| Azure Static Web App deployment + CI/CD | Infra | 1 week |

**Deliverable**: Open a DOCX from SPE, view and edit in browser, save back to SPE.

### Phase 2: AI Integration (6-8 weeks)

**Goal**: Full analysis + chat experience in Document Studio.

| Task | Scope | Estimate |
|------|-------|----------|
| Analysis Panel (playbook selector, results display) | UI | 2 weeks |
| Chat Panel with SSE streaming | UI | 2 weeks |
| Clause marker extension (TipTap) | Editor | 1 week |
| Risk highlight extension (TipTap) | Editor | 1 week |
| Context switching (document vs analysis) | Chat | 1 week |
| Predefined prompts (static per-playbook) | Chat | 1 week |

**Deliverable**: Run playbook analysis on loaded document, chat about results, clause navigation.

### Phase 3: Redlining and Track Changes (6-8 weeks)

**Goal**: AI-generated redlines appear as track changes in the editor.

| Task | Scope | Estimate |
|------|-------|----------|
| TipTap Pro integration (Track Changes, Comments) | Editor | 2 weeks |
| Redline service (server-side) | API | 2 weeks |
| Redline Panel UI (suggestions, accept/reject) | UI | 2 weeks |
| DOCX export with track changes (Open XML SDK) | API | 2 weeks |

**Deliverable**: Request AI redlines, review as track changes, export to DOCX with revision marks.

### Phase 4: Word Add-in Enhancement (3-4 weeks)

**Goal**: Word add-in has "Open in Studio" button and quick actions.

| Task | Scope | Estimate |
|------|-------|----------|
| "Open in Studio" command handler | Add-in | 1 week |
| Quick Summary action (sidebar) | Add-in | 1 week |
| Manifest update + deployment | Add-in | 1 week |
| End-to-end testing (Word → Studio → Word) | Testing | 1 week |

**Deliverable**: Click "Document Studio" in Word ribbon → opens in browser with document loaded.

### Phase 5: Advanced Features (6-8 weeks)

**Goal**: Document comparison, PWA, collaboration groundwork.

| Task | Scope | Estimate |
|------|-------|----------|
| Document comparison view | UI + API | 3 weeks |
| PWA manifest + offline indicators | PWA | 1 week |
| Auto-save + session management | State | 2 weeks |
| Model 1/Model 2 deployment testing | Infra | 1 week |
| Performance optimization (large documents) | Perf | 1 week |

**Deliverable**: Compare document versions, installable PWA, robust session management.

### Total Estimated Timeline: 29-38 weeks (sequential)

Phases 1-3 are sequential. Phases 4-5 can partially overlap with Phase 3.

---

## 16. Risk Assessment

| Risk | Impact | Likelihood | Mitigation |
|------|--------|-----------|------------|
| DOCX fidelity loss in round-trip | High | Medium | Server retains original OOXML, applies edits as delta; extensive test suite with real legal documents |
| TipTap Pro licensing cost | Medium | Low | Budget for Pro license; core features work without Pro; fallback to custom track changes if needed |
| Large document performance | Medium | Medium | Section-based loading, ProseMirror virtualization, server-side pagination |
| Word add-in ↔ Studio handoff UX | Medium | Low | Seamless auth (same AAD app), deep-link with document ID, session resume |
| Browser compatibility | Low | Low | Target modern browsers only (Chrome, Edge, Safari 16+, Firefox); no IE11 |
| Open XML SDK complexity | High | Medium | Incremental approach: start with basic paragraph/formatting, add table/image support progressively |

---

## 17. Success Criteria

| Metric | Target | How Measured |
|--------|--------|-------------|
| Document load time (10-page contract) | < 3 seconds | Performance monitoring |
| DOCX round-trip fidelity | > 95% visual match | Automated comparison tests |
| Analysis execution (playbook) | Same as AnalysisWorkspace | SSE timing metrics |
| Redline suggestion relevance | > 80% accepted by users | Accept/reject tracking |
| User adoption from Word | > 30% of Word add-in users try Studio | Usage analytics |
| Session completion rate | > 70% sessions result in save/export | Session tracking |

---

## 18. Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| BFF API analysis endpoints | Existing | Production | No changes needed for Phase 1-2 |
| SpeFileStore file operations | Existing | Production | Document download/upload |
| MSAL.js authentication | Existing | Production | Same app registration |
| Word add-in infrastructure | Existing | Production | Manifest update in Phase 4 |
| TipTap v3 | External | GA | Open source core + Pro license |
| Open XML SDK (.NET) | External | GA | Server-side DOCX processing |
| Azure Static Web Apps | Infrastructure | Available | Same hosting as current add-in |
| Playbook seed data (ENH-009-011) | Internal | Pending | Needed for meaningful analysis demos |

---

## 19. Open Questions

1. **TipTap Pro license**: Confirm budget approval for Track Changes and Comments extensions (~$299/year per developer, separate runtime pricing).

2. **PWA vs. simple web app**: Should we invest in PWA features (offline indicators, install prompt) in Phase 5, or defer to a later phase?

3. **Collaborative editing**: Should we plan the TipTap Yjs integration architecture now (for future multi-user editing), even if we don't implement it immediately?

4. **Mobile support**: The minimum viewport is 768px. Should we consider a separate mobile-optimized experience, or is tablet the minimum?

5. **Studio URL routing**: Should the Studio be a subdomain (`studio.spaarke.com`), a path (`app.spaarke.com/studio`), or integrated into the existing SWA deployment?

---

## Appendix A: Existing Infrastructure Inventory

### Reusable from Current Codebase

| Component | Location | Reuse Strategy |
|-----------|----------|---------------|
| `IHostAdapter` / `WordAdapter` | `src/client/office-addins/shared/adapters/` | Extend with Studio launcher |
| `SseClient` | `src/client/office-addins/shared/taskpane/services/` | Fork or import directly |
| `NaaAuthService` / `DialogAuthService` | `src/client/office-addins/shared/auth/` | Reference pattern for MSAL setup |
| `RichTextEditor` (Lexical) | `src/client/shared/Spaarke.UI.Components/` | Used for simple editing; TipTap replaces for Studio |
| `SprkButton`, `PageChrome`, `ChoiceDialog` | `src/client/shared/Spaarke.UI.Components/` | Direct reuse in Studio UI |
| `AnalysisWorkspaceApp` layout | `src/client/pcf/AnalysisWorkspace/` | Pattern reference for 3-panel layout |
| Analysis API endpoints | `src/server/api/Sprk.Bff.Api/Api/Ai/` | Direct reuse, no changes |
| `PlaybookExecutionEngine` | `src/server/api/Sprk.Bff.Api/Services/Ai/` | Direct reuse for analysis execution |
| `SpeFileStore` | `src/server/api/Sprk.Bff.Api/Services/` | Direct reuse for document access |
| `ITextExtractor` | `src/server/api/Sprk.Bff.Api/Services/Ai/` | Reuse for DOCX text extraction |

### New Components Required

| Component | Location | Purpose |
|-----------|----------|---------|
| Document Studio SPA | `src/client/document-studio/` | Browser application |
| `StudioEndpoints` | `src/server/api/Sprk.Bff.Api/Api/Studio/` | DOCX conversion + redline endpoints |
| `IDocumentConversionService` | `src/server/api/Sprk.Bff.Api/Services/Studio/` | DOCX ↔ TipTap conversion |
| `IRedlineService` | `src/server/api/Sprk.Bff.Api/Services/Studio/` | AI redline generation |
| `ISessionService` | `src/server/api/Sprk.Bff.Api/Services/Studio/` | Server-side session/cache management |
| TipTap custom extensions | `src/client/document-studio/src/editor/extensions/` | Clause markers, risk highlights, etc. |

---

## Appendix B: API Request/Response Examples

### Convert DOCX to Editor Format

**Request:**
```http
POST /api/studio/convert
Content-Type: multipart/form-data
Authorization: Bearer {token}

------boundary
Content-Disposition: form-data; name="file"; filename="contract.docx"
Content-Type: application/vnd.openxmlformats-officedocument.wordprocessingml.document

{binary DOCX content}
------boundary--
```

**Response:**
```json
{
  "content": {
    "type": "doc",
    "content": [
      {
        "type": "heading",
        "attrs": { "level": 1 },
        "content": [{ "type": "text", "text": "NON-DISCLOSURE AGREEMENT" }]
      },
      {
        "type": "paragraph",
        "attrs": { "paragraphId": "p-1" },
        "content": [
          { "type": "text", "text": "This Non-Disclosure Agreement..." }
        ]
      }
    ]
  },
  "metadata": {
    "wordCount": 4521,
    "pageCount": 12,
    "authors": ["John Smith"],
    "title": "NDA - Acme Corp",
    "createdDate": "2026-01-15T10:30:00Z",
    "modifiedDate": "2026-02-18T14:22:00Z",
    "styles": { "Heading1": "heading-1", "Normal": "paragraph" },
    "sections": [{ "orientation": "portrait", "margins": { "top": 1440 } }]
  },
  "sessionId": "sess-a1b2c3d4-e5f6-7890"
}
```

### Generate Redline Suggestion

**Request:**
```http
POST /api/studio/redline
Content-Type: application/json
Authorization: Bearer {token}
Accept: text/event-stream

{
  "analysisId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "clauseId": "clause-7",
  "instruction": "Make the indemnity clause mutual and add carve-outs for gross negligence",
  "context": {
    "originalText": "Customer shall indemnify and hold harmless Provider from any claims arising from Customer's use of the Service.",
    "playbookId": "9a3b5c7d-1234-5678-abcd-ef0123456789",
    "riskLevel": "high"
  }
}
```

**Response (SSE stream):**
```
event: redline-suggestion
data: {"id":"sug-1","originalText":"Customer shall indemnify and hold harmless Provider from any claims arising from Customer's use of the Service.","suggestedText":"Each Party shall indemnify, defend, and hold harmless the other Party and its officers, directors, and employees from any third-party claims arising from the indemnifying Party's breach of this Agreement or negligent acts, except to the extent such claims arise from the indemnified Party's gross negligence or willful misconduct.","reason":"Converted to mutual indemnification, added standard protections (defend, hold harmless), scoped to breach and negligence, added carve-outs for gross negligence and willful misconduct per standard commercial terms.","confidence":0.91,"paragraphId":"p-42","startOffset":0,"endOffset":127}

event: done
data: {"suggestionsCount":1,"tokenUsage":{"input":892,"output":156}}
```

---

*End of Design Document*
