---
name: claude-visual-outputs-2026-11-07
description: Claude visual channels as of 2026-11-07 - artifacts (template model since 2026-09-16), inline visuals, Cowork/Code artifacts, MCP Apps (local .mcpb = Desktop only; Claude Code no UI), files, Claude for Word tracked changes; cross-platform with M365 Copilot
metadata:
  type: reference
---

# Claude visual outputs (2026-11-07)

Full report: `projects/spaarke-legal-ai-accelerator-claude-r2/notes/research/claude-visual-outputs.md`. Microsoft side: r1 notes cowork-visual-outputs-2026-11-05, cowork-mcp-apps-author-guide-2026-10-30, [[mcp-server-dotnet-bff-copilot-widget-2026-11-05]].

**Question**: which visual mechanisms exist on Claude surfaces, which need a server, which ship in a zip plugin, which also work in M365 Copilot/Cowork.

**Findings**
- Chat artifacts: since 2026-09-16 template-based (Docs/Slides/Design all plans; Dashboards beta paid, Enterprise off by default; Motion beta); pre-09-16 = "legacy". 20 MB text storage, AI-powered (viewer's quota), viewer's own connectors. Cowork artifacts before 2026-08-19 = "live artifacts". Claude Design standalone closes 2026-12-14. Chat-artifact CSP undocumented; Claude Code artifact page documents CSP (5 CDNs, same-origin fetch, 16 MiB, downloads via declared capability), live connector calls (CC >= 2.1.209), not on Bedrock/Vertex/Foundry/CMEK/HIPAA/ZDR.
- MCP Apps: render on Claude web, Desktop, mobile (WebView), Cowork; **Claude Code CLI = text only**; Desktop Code tab renders widgets. Local stdio servers render in Desktop (examples use claude_desktop_config npx --stdio); no `ui.domain` for local; open-link modal always for custom/local. Plugin `mcpServers` may point at `./x.mcpb`; local servers ignored in chat (web/desktop/mobile), load in local Cowork and Claude Code. ~150k-char tool result limit (claude.ai/Desktop). frameDomains restricted.
- Inline visuals (2026-03-12, Cowork paid 2026-04-22): ephemeral, no template. Desktop bundles Chart.js, Grid.js, Mermaid for artifacts.
- Claude for Word: tracked-changes mode, comment-thread replies, redline summary; enabled account skills + turned-on plugins visible in add-in (script execution unknown). Uploaded add-in skills lost after M365 2608/2609 on Windows; save to claude.ai.
- Cross-platform: only files and remote-server MCP Apps author once for both; Claude-only strengths = artifacts, local .mcpb widgets, Spaarke skills running in Word.

**Sources**: claude.com/docs (plugins/platform-support, plugins/build, connectors/building/mcp-apps/*, connectors/building/mcpb, skills/*, office-agents/*, cowork/changelog), code.claude.com/docs/en/{artifacts,plugins/components}, support.claude.com 17153992/9547008/14729249/14604416/13454812/12138966, modelcontextprotocol client-matrix.mdx (GitHub raw; rendered page strips checkmarks), anthropics/claude-for-legal.

**Open questions**: local .mcpb UI in a local Cowork task; Cowork HTML file preview JS on local sessions; template fidelity when a skill hands Claude an `assets/` HTML for an artifact; skill scripts in Claude for Word; MCP Apps in Office add-ins.
