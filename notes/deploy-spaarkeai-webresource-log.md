# Task 122 — Deploy SpaarkeAi Web Resource

**Status**: READY FOR EXECUTION
**Date Prepared**: 2026-05-17
**Dependencies**: Task 120 (Cosmos DB deployed — required for session persistence at runtime)

---

## Pre-Deployment Checklist

- [ ] Task 120 complete (Cosmos DB deployed)
- [ ] Node.js installed (v18+ recommended)
- [ ] Azure CLI authenticated (`az login`)
- [ ] Dataverse environment accessible (`https://spaarkedev1.crm.dynamics.com`)
- [ ] Shared libraries available (symlinked or present):
  - `src/client/shared/Spaarke.UI.Components`
  - `src/client/shared/Spaarke.Auth`
  - `src/client/shared/Spaarke.AI.Context`
  - `src/client/shared/Spaarke.AI.Outputs`
  - `src/client/shared/Spaarke.AI.Widgets`

---

## Build Steps

```bash
cd src/solutions/SpaarkeAi

# Install dependencies (use --legacy-peer-deps per CLAUDE.md guidance)
npm install --legacy-peer-deps --no-audit --no-fund

# Build single-file HTML (Vite + vite-plugin-singlefile)
# Output: dist/spaarkeai.html (renamed from index.html by build script)
npm run build

# Verify artifact exists
ls -la dist/spaarkeai.html
```

### Build Output

- **File**: `dist/spaarkeai.html`
- **Type**: Single self-contained HTML (all JS/CSS inlined via `vite-plugin-singlefile`)
- **Expected size**: Variable, typically 2-5 MB for a full React app with Fluent UI

### Build Troubleshooting

If `npm install` fails:
- Ensure `--legacy-peer-deps` flag is used (peer dep conflicts between React 19 and Fluent UI)
- Delete `node_modules` and retry

If `npm run build` fails:
- Check that shared library aliases resolve (see `vite.config.ts` aliases)
- Verify `@spaarke/*` packages exist at their `file:` paths

---

## Deployment Command

```powershell
# Deploy to dev environment (default)
.\scripts\Deploy-SpaarkeAi.ps1

# Deploy to production
.\scripts\Deploy-SpaarkeAi.ps1 -DataverseUrl 'https://spaarke-prod.crm.dynamics.com'
```

### Deployment Script Details

Script: `scripts/Deploy-SpaarkeAi.ps1` (modeled after `Deploy-AnalysisWorkspace.ps1`)

| Step | Action |
|---|---|
| 1/4 | Verify build artifact (`dist/spaarkeai.html`) |
| 2/4 | Get Dataverse access token via Azure CLI |
| 3/4 | Create or update web resource `sprk_spaarkeai` (Dataverse Web API) |
| 4/4 | Publish customizations (PublishXml) |

---

## Post-Deployment Verification

### 1. Three-Pane Layout Verification

- [ ] Open SpaarkeAi from Dataverse (navigate to `sprk_spaarkeai` web resource or launch via ribbon)
- [ ] **Left pane (SourcePanel)**: Displays source document list or context panel
- [ ] **Center pane (ChatPanel)**: Chat input visible, can type and submit messages
- [ ] **Right pane (OutputPanel)**: Output area renders (initially empty or with placeholder)
- [ ] Pane resizing works (drag splitter bars between panes)
- [ ] Layout is responsive — panes adjust on window resize

### 2. SSE Streaming Verification

- [ ] Send a chat message in the ChatPanel
- [ ] Observe streaming response (text appears incrementally, not all at once)
- [ ] Open browser DevTools > Network tab
- [ ] Verify request to `/api/ai/chat/sessions/{id}/messages` returns `Content-Type: text/event-stream`
- [ ] SSE events arrive as `data:` lines with proper JSON payloads
- [ ] Stream completes with a final event (no hanging connections)

### 3. Session Restore Verification

- [ ] Create a new chat session and send several messages
- [ ] Note the session ID from browser DevTools or URL
- [ ] Close the SpaarkeAi tab/window
- [ ] Re-open SpaarkeAi
- [ ] Verify previous session appears in session list
- [ ] Click to restore the session
- [ ] Verify full conversation history is displayed correctly
- [ ] Verify the session state (playbook context, widgets) is restored

### 4. Widget Rendering Verification

- [ ] Trigger an analysis that produces widgets (e.g., findings, risk assessment)
- [ ] Verify widgets render correctly in the OutputPanel
- [ ] Check that widget interactions work (expand/collapse, drill-down)

### 5. Cross-Pane Integration

- [ ] Select a source document in LeftPane — verify ChatPanel context updates
- [ ] Chat response with citations — verify clicking a citation highlights source in LeftPane
- [ ] Analysis output — verify OutputPanel shows structured results

---

## Results

| Check | Expected | Actual | Pass/Fail |
|---|---|---|---|
| Build succeeds | dist/spaarkeai.html created | | |
| Bundle size | Reasonable (2-5 MB) | | |
| Web resource deployed | sprk_spaarkeai in Dataverse | | |
| Customizations published | No errors | | |
| Three-pane layout renders | All 3 panes visible | | |
| Chat input works | Can type and submit | | |
| SSE streaming works | Incremental text rendering | | |
| Session create | New session persisted | | |
| Session restore | History fully recovered | | |
| Widget rendering | Widgets display correctly | | |
| Cross-pane interactions | Source/chat/output linked | | |

**Deployed By**: _______________
**Date Executed**: _______________
