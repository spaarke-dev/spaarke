# mcp-apps — Provenance

> **Captured**: 2026-05-14 · **Curator**: Phase 2.2 of `coding-knowledge-base-setup-r1`

## Upstream repositories

| Repo | URL | Commit SHA | Branch | Captured |
|---|---|---|---|---|
| `microsoft/mcp-interactiveUI-samples` | https://github.com/microsoft/mcp-interactiveUI-samples | `3ff302642f19103f26b51582519e5baa880d71c5` | `main` | 2026-05-14 |

Commit detail: `2026-04-21 — Merge pull request #92 from shiladityamsft/fix-approvals-box`.

> `modelcontextprotocol/servers` was **not** cloned. The directive lists it "for understanding protocol idiom" — no specific sample required curation here. MCP transport idioms are amply represented by the curated `mcp-server.ts` / `index.ts` files below. Revisit at next refresh if a protocol-specific reference is needed.

## Curated samples

### `trey-research/` — HR Consultant Management (MCP Apps variant)

Source path in upstream: `mcp-apps/trey-research/node/`.

Closest pattern to a Spaarke workspace widget — Fluent UI dashboard, bulk editor, consultant detail. Demonstrates the **MCP Apps standard** (not OpenAI Apps SDK variant) — uses `app.callServerTool`, `app.getHostContext`, `app.sendSizeChanged`, etc. (see `docs/plugin-mcp-apps.md` for the full bridge table).

| File | Purpose |
|---|---|
| `README.md` | Sample overview and run instructions |
| `m365agents.yml` | Microsoft 365 Agents Toolkit lifecycle config |
| `appPackage/manifest.json` | Teams app manifest (declarative agent host) |
| `appPackage/declarativeAgent.json` | Declarative agent definition |
| `appPackage/ai-plugin.json` | MCP server action / plugin manifest |
| `appPackage/instruction.txt` | Agent instructions |
| `src/mcpserver/README.md` | Server-side architecture notes |
| `src/mcpserver/package.json` | Root server package |
| `src/mcpserver/db/{Assignment,Consultant,Project}.json` | Seed data — entity shapes for widgets to render |
| `src/mcpserver/server/src/index.ts` | MCP server bootstrap (transport + tool registration entry point) |
| `src/mcpserver/server/src/mcp-server.ts` | MCP server implementation — tool handlers, widget binding |
| `src/mcpserver/server/src/db.ts` | In-memory data access used by tool handlers |
| `src/mcpserver/server/src/seed.ts` | Seeds in-memory data from `db/*.json` on startup |
| `src/mcpserver/server/{tsconfig.json,package.json}` | TS / build config |
| `src/mcpserver/widgets/build.mts` | Vite build orchestration for the three widgets |
| `src/mcpserver/widgets/{package.json,tsconfig.json}` | Widget build config |
| `src/mcpserver/widgets/src/types.ts` | Shared widget types |
| `src/mcpserver/widgets/src/hooks/useMcpApp.tsx` | React hook wrapping the MCP Apps host bridge (`app.*` APIs) |
| `src/mcpserver/widgets/src/hooks/useThemeColors.ts` | Reads host theme from `app.getHostContext()` |
| `src/mcpserver/widgets/src/dashboard/{Dashboard.tsx,main.tsx,index.html}` | **Dashboard widget** — side-by-side mode pattern (table view of consultants) |
| `src/mcpserver/widgets/src/bulk-editor/{BulkEditor.tsx,main.tsx,index.html}` | **Bulk editor widget** — multi-row state editing, closest to Spaarke tabular review |
| `src/mcpserver/widgets/src/consultant-profile/{ConsultantProfile.tsx,main.tsx,index.html}` | **Detail widget** — inline summary card pattern |

**Skipped from upstream**:

- `src/mcpserver/server/dist/` — compiled output, regenerable via `npm run build`.
- `package-lock.json` files (server, widgets, root) — large, not architecturally informative.
- `appPackage/{color.png, outline.png}` — branding assets, not architecturally informative.
- `demos/` — contains `trey-demo.mp4` (37 MB). Watch on YouTube via repo README link instead.

### `approvals-box/` — Risk-triaged Approval Queue (OpenAI Apps SDK variant)

Source path in upstream: `oai-apps-sdk/approvals-box/node/`.

Closest pattern to Spaarke's redline review queue — risk triage, bulk decisions, inline review widgets. **Note**: this is the OpenAI Apps SDK variant (uses `window.openai.*` APIs) — see `docs/plugin-mcp-apps.md` "Component bridge" table to translate to MCP Apps equivalents.

| File | Purpose |
|---|---|
| `README.md` | Sample overview, prompts, demo storyline |
| `package.json`, `tsconfig.json` | Server build config |
| `appPackage/manifest.json` | Teams app manifest |
| `appPackage/declarativeAgent.json` | Declarative agent definition |
| `appPackage/ai-plugin.json` | MCP server action manifest — see `tools[].annotations` + `_meta` widget bindings |
| `src/index.ts` | MCP server entry — transport binding |
| `src/mcp-server.ts` | Tool implementations: `list_pending_approvals`, `get_approval_detail`, `approve`, `reject`, `create_approval`, etc., with widget resource URIs and `_meta` directives |
| `src/risk.ts` | Risk-scoring logic — pattern for risk-triage widgets |
| `src/db.ts` | In-memory data layer |
| `src/types.ts` | Domain types (Approval, RiskAssessment, AuditEvent, etc.) |
| `src/seed.ts` | ~50 demo approvals across 7 types — reference for realistic seed-data shape (2028 lines; kept intact for provenance) |

**Skipped from upstream**:

- `assets/{approval-detail,create-approval,pending-approvals}.html` — pre-bundled minified widget HTML (1.3 MB total). These are *built artifacts* not source — generated from upstream widget source not present in this directory. Use `trey-research/src/mcpserver/widgets/src/` for widget React source patterns.
- `appPackage/{color.png, outline.png}` — branding assets (52 KB / 4 KB), not architecturally informative.
- `demos/screenshots/approvals-box-detail.png` — single widget screenshot (252 KB); `agents-toolkit-screenshots/` provides equivalent visual reference.
- `package-lock.json`, `node_modules/` — regenerable.

### `agents-toolkit-screenshots/`

Source path in upstream: `agents-toolkit-screenshots/` (root). Visual references for the Agents Toolkit fetch-actions flow described in `M365-Agents-Toolkit-Instructions.md`.

| File | Size | Purpose |
|---|---|---|
| `image2.png` | 85 KB | Agents Toolkit pane / action selection step |
| `image3.png` | 28 KB | Auth-type selection step |
| `image4.png` | 63 KB | Provisioning step |

**Skipped** (each > 100 KB, directive curation budget):

- `image1.png` (296 KB) — initial Agents Toolkit pane
- `steps-for-tools.png` (300 KB) — multi-panel walkthrough

These remain available upstream at the commit SHA recorded above.

## Reference docs (snapshots)

`docs/` contains markdown snapshots of canonical Microsoft Learn pages. YAML frontmatter on each file records `source` and `fetched: 2026-05-14`.

| File | Source |
|---|---|
| `docs/plugin-mcp-apps.md` | `https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/plugin-mcp-apps` (page `ms.date: 2026-04-24`, page `updated_at: 2026-05-12`) |
| `docs/plugin-mcp-apps-ui-guidelines.md` | `https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/plugin-mcp-apps-ui-guidelines` (page `ms.date: 2026-03-06`) |
| `docs/mcp-apps-announcement.md` | `https://devblogs.microsoft.com/microsoft365dev/mcp-apps-now-available-in-copilot-chat/` |

## GAPs

- **`https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/declarative-agent-ui-widgets`** — URL returns HTTP 200 OK, but WebFetch returned content **identical** to `plugin-mcp-apps` (same `canonicalUrl`, same `document_id`, same body). The page appears to be a redirect/alias to `plugin-mcp-apps` after Microsoft consolidated the two articles. Not snapshotted separately to avoid duplicate content. Verify at next refresh — if the doc topic gets re-split or renamed, capture the new URL.
- **`modelcontextprotocol/servers`** — not cloned (see note above). If future Spaarke work needs canonical MCP server reference implementations (e.g., for transport edge cases), revisit.
- **Approvals Box widget React source** — the widget UI files in `oai-apps-sdk/approvals-box/node/assets/` are pre-bundled HTML only; the upstream React source for those widgets is not in this sample folder. Trey Research widget source is curated instead and demonstrates equivalent patterns.
