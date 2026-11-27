---
name: copilot-extension-routes-by-surface-2026-11-27
description: M365 Copilot developer routes by surface as of 2026-11-27 - federated (MCP) connector is the only route documented for Word/Excel/PowerPoint/Outlook; plugin = packaging unit (skills + connectors + optional DA); Office add-in agent actions cancelled 2026-06-17 (office-js-docs-pr#5799), replaced by Excel-only preview Office.js skills; MCP Apps only Chat/Cowork; MCP certification needs client secret, no PKCE
metadata:
  type: reference
---

# Copilot extension routes by surface (2026-11-27)

Full note: `projects/spaarke-legal-ai-accelerator-r1/notes/research/office-surfaces-2026-11/03-extension-routes.md`
(part 3 of a 3-part surface spike; synthesis in `00-synthesis.md` there).

- Federated connector (MCP, admin-made or gallery): documented for Copilot in Word, Excel, PowerPoint, Outlook,
  plus Chat, agents, Cowork, Researcher (read only). Legal vendors (Harvey, Legora, iManage, NetDocuments, Litera)
  are already in the gallery.
- Plugin (`agentSkills` + `agentConnectors` + optional declarative agent) is the packaging unit; each host activates
  only what it supports. Office apps aren't on the skills page; Word works by our tests only.
- Office add-in functions as agent actions: cancelled 2026-06-17; store rules forbid them. Replacement: Office.js
  inside a skill, Excel only, Insider preview.
- Declarative agents read Word/PowerPoint documents as plain text; only write is image insert.
- MCP Apps: Chat (inside a DA) and Cowork. SPFx UX components: Chat and Teams. Nothing for Office panes.
- Store: Partner Center "Microsoft 365 and Copilot"; skills' documented store path is inside a DA; MCP certification
  preview, needs ClientId/ClientSecret in publisher Key Vault, no PKCE.
