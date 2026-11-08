# Anthropic legal plugin catalogue and the Microsoft 365 connector (2026-11-07)

> Prompted by the owner's screenshots of Claude's Customize › Skills/Plugins search ("legal": a "Legal" plugin from
> Anthropic under Yours, and claude-for-legal plugins under Discover) and the Microsoft 365 connector setup.
> Labels: [C] read from source, [I] inferred, [T] needs a live test.

## 1. Where the legal plugins come from

The Directory is fed by public plugin marketplaces on GitHub. Each has a `.claude-plugin/marketplace.json` that lists
every plugin, so the catalogue can be enumerated and diffed without screen-scraping [C, read via `gh api` 2026-11-07]:

| Marketplace | Plugins | Legal-relevant entries |
|---|---|---|
| `anthropics/knowledge-work-plugins` | 123 | **`legal`** ("Legal", from Anthropic: the one under *Yours* in the screenshot); partner `gc-ai`, `pdf-viewer` (mark up and sign contracts), `box`, `govtribe`, `desktop-commander` |
| `anthropics/claude-for-legal` | 13 | `commercial-legal`, `privacy-legal`, `product-legal`, `corporate-legal`, `employment-legal`, `regulatory-legal`, `ai-governance-legal`, `litigation-legal`, `ip-legal`, `law-student`, `legal-clinic`, `legal-builder-hub`, and Thomson Reuters `cocounsel-legal` (Westlaw Deep Research) |
| `anthropics/claude-plugins-official` | 317 | `legalzoom`, `gc-ai`, `govtribe` |

The 854 Directory hits for "legal" also include **connectors** (e.g. "Legal Data Hunter" by GoodLegal) and community
skills. Connectors are not in these GitHub catalogues [I]; listing them needs the Directory itself.

## 2. Two different Anthropic legal offerings

| | **Legal** (`knowledge-work-plugins/legal`) | **Claude for Legal** (`claude-for-legal`, 12 practice plugins) |
|---|---|---|
| Shape | One generalist in-house plugin, 9 skills: `review-contract`, `triage-nda`, `compliance-check`, `legal-risk-assessment`, `legal-response`, `brief`, `meeting-briefing`, `signature-request`, `vendor-check` [C] | Practice plugins, ~151 skills, setup interviews (see `claude-for-legal-synopsis.md`) |
| Last change | `c71b6f3`, 2026-09-21 [C] | `main` unchanged since 2026-07-22 |
| Where positions live | **`legal.local.md`**: "Look for the organization's contract review playbook in local settings (e.g., `legal.local.md`…)"; README says `.claude/legal.local.md`; if missing, the skill offers to help set one up or falls back to generic standards [C] | Profile Markdown under `~/.claude/plugins/config/claude-for-legal/<plugin>/` (breaks in Cowork; issues #55, #94) |
| Connectors in `.mcp.json` | Slack, Box, Egnyte, Atlassian, DocuSign, Google Calendar, Gmail (empty URLs) [C] | Per plugin; no auth details |
| Primary surface | "primarily designed for Cowork … also works in Claude Code" [C] | Chat, Cowork, Claude Code |

**Spaarke implication [I]:** both are "method plus your positions". The cheapest high-value Spaarke contribution is
the positions: a Spaarke-authored `legal.local.md` (NDA and contract playbook in the Legal plugin's expected shape)
and the equivalent claude-for-legal profile sections. That works with zero Spaarke plugin code. [T] Does the Legal
plugin find `legal.local.md` in a Cowork connected folder (not `.claude/`), and does it honour every section Spaarke
writes?

## 3. Bundling vs referencing

- **Reference, don't copy (recommended).** Users add Anthropic's plugins from the Directory (one click, verified badge,
  Anthropic's updates). Team Owners add them to the org as Required or Installed by default.
- **Claude Code / marketplace route:** a Spaarke `marketplace.json` can list Anthropic's plugins by external source,
  the same way `knowledge-work-plugins` lists partner plugins (`"source": "git-subdir"` / `"url"` with a pinned `sha`)
  [C: pattern seen in the catalogue]. That gives one "Spaarke legal" marketplace pinning reviewed versions.
  [T] Whether a claude.ai org marketplace honours external sources.
- **Copying** is allowed (Apache-2.0, `LICENSE` in each plugin) but loses updates and the verified badge, and plugin
  names starting `claude-` are reserved.
- `spaarke-start` can check which recommended skills are present from the skills Claude sees, and tell the user what
  to add [I].

## 4. Microsoft 365 connector (Anthropic-hosted)

From the owner's screenshots [C] and the connector page:
- Made by Anthropic; URL `https://microsoft365.mcp.claude.com/mcp`; sign-in required; "Claude can access only what
  your account can already access."
- Personal accounts (Outlook.com etc.) only on Free, Pro, Max and Ultra; **Team and Enterprise: work or school
  accounts only**. A work account adds Teams and SharePoint.
- 15 read-only tools (email, calendar, Teams chats and channels, SharePoint/OneDrive search and folder search,
  `read_resource`, people search) default to *Always allow*; 35 write/delete tools (send mail, forward, delete,
  calendar events, SharePoint upload/update/move/delete, Teams messages) default to *Needs approval*.
- The Word add-in offers it during first-run onboarding.

**Fit [I]:**
- v1 gets document access with no Spaarke server: contracts in OneDrive/SharePoint and Outlook attachments become
  reachable in chat, Cowork and Word.
- A **team playbook** (`legal.local.md`, NDA positions) can sit in a SharePoint/OneDrive folder that the whole team
  reads through the connector, which solves the "can't share one profile" problem.
- It doesn't reach Spaarke's SharePoint Embedded containers or Dataverse matters; that stays the Spaarke connector's
  job (v2). Generic M365 = Anthropic's connector; matter context = Spaarke's.
- Keep write tools on *Needs approval* (standing directive: Claude never sends email).
- In cloud-provider (Azure) mode the add-in uses only admin-configured MCP servers, so this connector is not there.
- [T] Work-tenant admin consent for Anthropic's Entra app; whether `read_resource` returns .docx bytes or only text
  (r1's Python engines need the file); SharePoint search coverage of the team playbook folder.
