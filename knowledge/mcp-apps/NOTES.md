> ⚠️ STUB — senior engineer review pending

# mcp-apps — Spaarke project annotations

This file captures **how Spaarke applies the patterns** in `SOURCE.md`-listed samples and docs. Section structure follows the Phase 2.2 directive in `projects/coding-knowledge-base-setup-r1/SPAARKE-KNOWLEDGE-BASE-SETUP.md`. Each section is a TODO until annotated by a senior engineer who has shipped or reviewed an MCP app against a Spaarke workload.

---

## Inline vs side-by-side mode — when each applies for Spaarke skills

_TODO: For each Spaarke MCP skill we plan to expose (Redline, TabularReview, Compare, PlaybookDraft, CitationCheck), state which mode is the primary and which (if any) is the escalation target. Cite the UX guidance in `docs/plugin-mcp-apps-ui-guidelines.md` (inline = previews/confirmations/simple actions, side-by-side = multi-step editing, comparison, dashboards) and the trey-research widget split (dashboard = side-by-side; consultant-profile = inline) as concrete reference._

## Widget state management — receiving data from MCP tool, capturing user interactions, returning state

_TODO: Document the round-trip pattern using the Trey Research `useMcpApp.tsx` hook as the canonical reference. Cover: how `app.ontoolresult` delivers tool output to the widget, how `app.callServerTool` issues a follow-up server tool call from a user interaction, and how widget state survives across re-renders. For OpenAI Apps SDK variant (approvals-box pattern), note `window.openai.widgetState` / `setWidgetState` and the equivalent MCP Apps mechanism (`app.updateModelContext`) per `docs/plugin-mcp-apps.md` bridge table. Spaarke convention: which mechanism we standardize on._

## Fluent UI v9 usage — alignment with ADR-021

_TODO: Confirm whether the curated samples use Fluent UI v9 (the Spaarke standard per ADR-021 — see `c:/code_files/spaarke/.claude/adr/`) or v8. If v9, point to the specific components used in `Dashboard.tsx` and `BulkEditor.tsx`. If v8 or unbranded, document the Spaarke uplift required when copying patterns. Cross-link to `c:/code_files/spaarke/src/client/shared/Spaarke.UI.Components/` — most widget components likely have a Spaarke equivalent already._

## Sandboxed iframe constraints — no localStorage, scoped DOM, theme variables

_TODO: Enumerate the exact sandbox restrictions Copilot applies to widget iframes. The hashed-domain CORS host (`{hashed-mcp-domain}.widget-renderer.usercontent.microsoft.com`) is recorded in `docs/plugin-mcp-apps.md`. Confirm: which storage APIs work (sessionStorage? indexedDB?), how theme tokens arrive (`app.getHostContext()?.theme` per bridge table), and whether the widget can navigate / open external links (`app.openLink` is supported). Reference the CSP fields in the `_meta.ui.csp` table — `connectDomains` and `resourceDomains` are the two Spaarke widgets will need to populate (e.g., for BFF API calls)._

## Spaarke skill → widget pattern mapping

_TODO: Fill in the table below from the directive guidance, using the trey-research and approvals-box widgets as visual anchors._

| Spaarke skill | Widget pattern | Reference sample | Mode | Notes |
|---|---|---|---|---|
| Redline | Inline summary card + deep-link to side-by-side review pane | _TODO_ | inline → side-by-side | _TODO: where does the side-by-side review pane open — a Spaarke Code Page or an MCP widget?_ |
| TabularReview | Side-by-side grid widget | `trey-research/.../dashboard/` | side-by-side | _TODO: grid library — Fluent v9 DataGrid? Cross-link `Spaarke.UI.Components`_ |
| Compare | Side-by-side diff widget | _TODO — no direct sample analog_ | side-by-side | _TODO: diff component choice; size budgets_ |
| PlaybookDraft | Inline rationale card | `trey-research/.../consultant-profile/` (structurally) | inline | _TODO: max two actions per UX guidance — what are they?_ |
| CitationCheck | Inline status cards | _TODO_ | inline | _TODO: loading/success/error states explicit per UX guidance_ |

## `/generate-mcp-app-ui` skill (Microsoft, for Claude Code and GitHub Copilot CLI)

_TODO: Microsoft published a composable skill for scaffolding MCP app UI (referenced in `docs/mcp-apps-announcement.md` under "GitHub Copilot CLI skill"). Capture: install instructions, the skill's exact name as shipped, whether it can be invoked from Claude Code (vs. only GitHub Copilot CLI), and how it interacts with the Spaarke `.claude/skills/widget-design/` skill that will reference this knowledge base. Confirm with hands-on test before annotating — Microsoft's release post is the only current source._

## Open questions

- _TODO: Auth flow for Spaarke MCP server — OAuth 2.1 vs Entra SSO — which fits the BFF API integration? See `docs/plugin-mcp-apps.md` "MCP server requirements" section for the full redirect URI list to register._
- _TODO: Hosting target for the Spaarke MCP server widget HTML — same App Service as the BFF, or a separate static endpoint? CORS implication: the hashed widget host URL must allow our origin._
- _TODO: Does Spaarke want both an MCP Apps and an OpenAI Apps SDK variant, or just one? Trey-research and approvals-box exist as both flavors upstream — the directive favors MCP Apps as the open standard. Confirm._
