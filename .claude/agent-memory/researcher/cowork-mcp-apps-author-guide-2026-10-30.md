---
name: cowork-mcp-apps-author-guide-2026-10-30
description: Cowork MCP Apps author guide (SEP-1865 2026-01-26): ui:// resourceUri on tool, HTML via resources/read text/html;profile=mcp-app, inline+fullscreen only, widget may call only resources/read, tools/call, ui/message; auth attached by Cowork (never in iframe); 64 KiB, 60/min, 10 s init, 200-720x640, CSP 16 origins + validDomains
metadata:
  type: reference
---

Source: https://learn.microsoft.com/en-us/copilot/cowork/mcp-apps-support ("MCP apps plugin author guide for Cowork", ms.date 2026-09-08, updated 2026-10-21; repo MicrosoftDocs/copilot-modes-pr @7963dbc1). **No GA/preview label on the page** (earlier notes said "GA 2026-09-08" — that is the publish date, not a stated status).

**Model**: SEP-1865 MCP Apps extension, spec 2026-01-26 (github.com/modelcontextprotocol/ext-apps `specification/2026-01-26/apps.mdx`); SDK `@modelcontextprotocol/ext-apps` emits the same protocol.
1. Tool in `tools/list` declares `_meta.ui.resourceUri` = `ui://…` (≤1024 chars; other schemes → no widget). Handler returns DATA (text / `structuredContent`), not HTML.
2. Cowork mounts widget, delivers result (`ui/notifications/tool-result`, `tool-input` once).
3. Cowork `resources/read`s the URI → HTML in `text` field (base64 `blob` NOT decoded), mime `text/html;profile=mcp-app`, self-contained (inline JS/CSS, `data:` assets; Cowork hosts nothing).

**Honored `_meta.ui`**: tool: `resourceUri`, `visibility` (["model","app"]; default both; missing "model" = hidden from agent; unrecognized = fail-closed hidden; widget tools/call refused if no "app"). Resource: `csp` {connectDomains (anchor — declare whenever any CSP), resourceDomains (only with connectDomains), frameDomains; baseUriDomains NOT applied; https/wss, ≤1 leading `*.`, ≤16 origins/directive}, `permissions` object map (array ignored) from {camera, microphone, geolocation, clipboardWrite}. `domain` ≤256 chars. `prefersBorder` and others ignored. **Marketplace package: every csp/domain origin must be in manifest `validDomains[]`, fail-closed, user sees "widget network blocked" card.**

**Bridge**: allowed from widget ONLY `resources/read` (own server), `tools/call` (own server, visibility enforced), `ui/message` (injected as a user turn). Also `ui/initialize` (must finish ≤10 s or error card), `ui/request-display-mode` (inline | fullscreen; pip rejected), `ui/open-link` (https only, user confirms — never make it the only path), size-changed, host-context-changed (theme, locale, tz, direction, style vars). Rejected: `ui/update-model-context`, `elicitation/*` from widgets, sampling, prompts, tools/list, resources/list, streaming/server-push, tool-input-partial, inline HTML in tool results (old MCP-UI). `Mcp-Session-Id` re-attached → stateful servers OK.

**Security**: sandboxed cross-origin iframe, per-server origin (widgets of the same server share cookies/storage; different servers isolated). Cowork brokers all widget→server traffic on an authenticated per-session channel; **user credentials never reach the iframe — Cowork attaches auth upstream**.

**Limits**: inline result 64 KiB (larger → data omitted, widget still mounts and can fetch via tools/call; errors not inlined); 60 widget calls/min/conversation; inline 200–720 px wide, ≤640 px tall (use fullscreen for more).

**Guidance**: widget vs elicitation — elicitation for confirmations, short enums, flat forms (no UI code); widget for searchable pickers, dashboards, charts, previews, live status. Files as tool input need no widget: param with `contentEncoding: base64` (cowork-plugin-development#accept-files-from-the-cowork-workspace). Graceful degradation: tool must return self-sufficient data when no widget renders; display widgets include a text summary.

**Not on the page**: plugin manifest/agentConnectors schema, install/admin rules, OAuth details, platform (mobile/desktop/web) support → see cowork-plugin-development, configure-plugin-mcp-servers. Related: [[copilot-legal-taxonomy-and-practice-pack-dev-refs-2026-10-29]], [[cowork-spe-playbooks-automation-2026-10-30]].
