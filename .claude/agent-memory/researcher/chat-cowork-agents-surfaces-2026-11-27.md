---
name: chat-cowork-agents-surfaces-2026-11-27
description: Copilot Chat, Cowork, Researcher, Analyst as of 2026-11-27 - Chat skills GA (Premium only) with a documented closed sandbox (no network, no pip, python-docx/openpyxl/LibreOffice listed); Cowork runtime undocumented, no Office.js runner, built-in editor edits files in place; Researcher reads federated connectors read-only; IB tenants blocked; Claude outside EU Data Boundary
metadata:
  type: reference
---

# Copilot Chat, Cowork and built-in agents (2026-11-27)

Full note: `projects/spaarke-legal-ai-accelerator-r1/notes/research/office-surfaces-2026-11/02-chat-cowork-agents.md`
(part 2 of a 3-part surface spike; synthesis in `00-synthesis.md` there).

- Skills run in two hosts: Cowork, and Copilot Chat (GA Oct-Nov 2026, Premium only; free tier gets none).
- Chat/declarative-agent sandbox is documented: no network, no pip, no authenticated calls, fixed package list.
  Cowork's runtime is undocumented (our evidence: Python 3.12 Linux, python-docx, lxml, LibreOffice).
- Cowork has no Office.js runner (owner test 2026-11-27); its built-in editor saves changes to the same shared file.
- Plugin for both Chat and Cowork needs `OAuthPluginVault` (DCR shortcut is Cowork-only). Unannotated MCP tools are
  treated as destructive.
- Researcher: federated connectors, read only. Analyst: not extensible.
- Information Barriers tenants: no skills/plugins in either host. Claude outside EU Data Boundary.
- Gaps: Cowork egress/pip/time limit, MCP result as file, Purview detail for script runs (CC-1..CC-14 in the note).
