# Lessons Learned — AI Playbook Node Builder R5

> **Date**: 2026-03-01
> **Project Duration**: ~2 sessions (autonomous execution with parallel agents)

---

## What Went Well

### 1. Parallel Agent Execution
- **4 parallel agent groups** (Canvas, Scope, Forms, AI Assistant) executed simultaneously
- Zero file conflicts — each group owned separate directories
- Combined build compiled with 0 errors after all 4 groups merged
- Estimated ~4x wall-clock speedup vs sequential execution

### 2. Proven Architecture Patterns
- AnalysisWorkspace auth pattern (5 Xrm strategies + MSAL fallback) transferred cleanly
- DocumentRelationshipViewer provided @xyflow v12 reference for node/edge patterns
- Webpack 5 + esbuild-loader + build-webresource.ps1 pipeline worked first time
- Zustand v5 stores maintained same API as R4 despite React 16→19 upgrade

### 3. Zero Mock Data Strategy
- All stores (scope, model, template) use real Dataverse REST API queries
- DataverseClient service with fetch + Bearer token replaced PCF `context.webAPI`
- N:N relationship sync (skills, knowledge) uses diff-based associate/disassociate
- Kahn's topological sort for execution order computes from canvas edges

### 4. Code Page Over Custom Page
- ADR-006 decision to use Code Page instead of Custom Page proved correct
- `Xrm.Navigation.navigateTo({ pageType: "webresource" })` simpler than Custom Page
- No Power Apps Maker portal needed — just a web resource upload
- URL parameters via `data` query string parsed with standard `URLSearchParams`

---

## Key Technical Decisions

### React 19 + @xyflow/react v12
- React 19 required for @xyflow v12 (React 18+ minimum)
- `createRoot()` from React 19 works identically to React 18 for our use case
- Typed generics: `NodeProps<Node<PlaybookNodeData>>` provide compile-time safety
- `screenToFlowPosition()` replaces manual coordinate math for drag-and-drop

### Webpack 5 Build Pipeline
- esbuild-loader (vs ts-loader) for faster builds (~3s vs ~12s)
- CSS rule required for `@xyflow/react` styles: `include: [/node_modules\/@xyflow/]`
- `build-webresource.ps1` inlines bundle.js into HTML template for Dataverse deployment
- Bundle: 1.15 MiB (acceptable for a full React Flow canvas app)

### Multi-Strategy Auth
- 5 Xrm frame-walk strategies handle different Dataverse embedding contexts
- MSAL `ssoSilent` fallback for dev/testing without Dataverse host
- 4-minute proactive token refresh prevents mid-session expiration
- Auth state exposed via `useAuth()` hook — components never touch tokens directly

### playbookNodeSync Design
- Canvas is source of truth — Dataverse records are derived
- `buildConfigJson()` maps all 7 node types to execution engine format
- `__canvasNodeId` embedded in ConfigJson for canvas↔Dataverse tracking
- Orphaned node cleanup: any Dataverse record not in canvas gets deleted

---

## Gotchas / Issues Encountered

### 1. npm workspace:* Protocol
- npm doesn't support `workspace:*` protocol (that's pnpm/yarn)
- Fixed by using `"file:../../shared/Spaarke.UI.Components"` in package.json
- This is a monorepo-specific issue that affects all Code Page projects

### 2. Edit Tool Requires Prior Read
- Claude Code's Edit tool requires files to be Read first in the session
- When working with many files across parallel agents, each agent must Read before Edit
- Not an issue in practice — just requires discipline in agent prompts

### 3. PCF Solution Isolation
- PlaybookBuilderHost PCF has its own self-contained solution directory
- No references in shared solution XML under `src/solutions/`
- "Remove from solution" task was a no-op — already isolated by design

### 4. Custom Page → Code Page Migration
- `sprk_playbook_commands.js` was the single file to update
- Changed `pageType: "custom"` → `pageType: "webresource"`
- Removed `sessionStorage` for parameter passing (Code Page uses URL params)
- Ribbon XML unchanged — same JS function names, same command definitions

---

## Patterns for Future Projects

### Code Page Template
The PlaybookBuilder establishes a reusable pattern for complex Code Pages:
```
src/client/code-pages/{Name}/
├── webpack.config.js          # Webpack 5 + esbuild-loader
├── build-webresource.ps1      # Inline bundled JS into HTML
├── index.html                 # Shell HTML
├── src/
│   ├── index.tsx              # createRoot + ThemeRoot + URL params
│   ├── App.tsx                # Auth-gated shell
│   ├── config/msalConfig.ts   # Azure AD config
│   ├── services/              # authService, dataverseClient, domain services
│   ├── stores/                # Zustand v5 stores
│   ├── hooks/                 # React hooks (auth, theme, keyboard)
│   ├── types/                 # TypeScript interfaces
│   └── components/            # React components by feature area
```

### Parallel Agent Decomposition
For 10+ file tasks, decompose into groups by directory ownership:
- Each agent owns non-overlapping file paths
- Shared types can be created by the first agent, consumed by others
- Build verification after all agents complete catches interface mismatches

---

## Metrics

| Metric | Value |
|--------|-------|
| Total tasks | 25 |
| Tasks completed | 25 (100%) |
| Files created | ~55 |
| Bundle size | 1.15 MiB |
| Web resource size | 1178 KB |
| Build errors | 0 |
| Parallel agent groups | 4 |

---

*Generated during AI Playbook Node Builder R5 project wrap-up.*
