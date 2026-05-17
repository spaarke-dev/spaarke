---
source: https://learn.microsoft.com/en-us/microsoft-365/copilot/extensibility/work-iq-cli
fetched: 2026-05-14
ms_date: 2026-05-12
ms_updated_at: 2026-05-12
github_repo: https://github.com/microsoft/work-iq-mcp
---

# Microsoft Work IQ CLI (preview)

Microsoft Work IQ is a command-line interface (CLI) and Model Context Protocol (MCP) server that connects AI assistants to your Microsoft 365 Copilot data. Work IQ enables you to query your emails, meetings, documents, Teams messages, workplace insights, people-related information, and more by using natural language.

Example queries:
- "What did my manager say about the project deadline?"
- "Find my recent documents about Q4 planning"
- "Summarize today's messages in the Engineering channel and propose a plan of action"

> **Important**: Work IQ is currently in public preview. Features and APIs might change.

## What is the Work IQ CLI?

The Work IQ CLI bridges AI coding assistants and Microsoft 365 data. By exposing your Microsoft 365 Copilot data through MCP, Work IQ enables AI assistants in your development environment to access and reason over your workplace information.

| Data type | Example queries |
| --- | --- |
| **Emails** | "What did John say about the proposal?" |
| **Meetings** | "What's on my calendar tomorrow?" |
| **Documents** | "Find my recent PowerPoint presentations" |
| **Teams messages** | "Summarize today's messages in the Engineering channel" |
| **People** | "Who is working on Project Alpha?" |

Source code: https://github.com/microsoft/work-iq-mcp

## Two modes

- **CLI mode**: `workiq ask` from terminal.
- **MCP server mode**: stdio MCP server, integrates with GitHub Copilot, Claude Code, VS Code. Lets coding assistant pull workplace context contextually.

## Prerequisites

- Node.js
- Microsoft 365 subscription with Copilot license
- Administrative consent for the Work IQ application in Microsoft Entra tenant
- GitHub Copilot CLI (optional)

## Platform support

- Windows (x64 and ARM64)
- Linux (x64 and ARM64)
- macOS (x64 and ARM64)
- Windows Subsystem for Linux (WSL) with browser support

## Install

### Via GitHub Copilot CLI

```bash
copilot
/plugin marketplace add github/copilot-plugins
/plugin install workiq@copilot-plugins
```

### Global npm install

```bash
npm install -g @microsoft/workiq
```

### Run via npx (no install)

```bash
npx -y @microsoft/workiq mcp
```

### Manual MCP configuration

```json
{
  "workiq": {
    "command": "npx",
    "args": [
      "-y",
      "@microsoft/workiq",
      "mcp"
    ],
    "tools": [
      "*"
    ]
  }
}
```

## Accept EULA

```bash
workiq accept-eula
```

Must run once before first use.

## CLI commands

| Command | Description |
| --- | --- |
| `workiq accept-eula` | Accept the End User License Agreement |
| `workiq ask` | Ask a question to a specific agent or run in interactive mode |
| `workiq mcp` | Start MCP stdio server for agent communication |
| `workiq version` | Show version information |

### Global options

| Option | Description | Default |
| --- | --- | --- |
| `-t, --tenant-id <tenant-id>` | Microsoft Entra tenant ID | `common` |
| `--version` | Show version |  |
| `-?, -h, --help` | Show help |  |

### `workiq ask` options

| Option | Description |
| --- | --- |
| `-q, --question <question>` | The question to ask the agent |

## Security and privacy

- Inherits Microsoft 365 Copilot data protections
- Respects permissions — only accesses data user already has access to
- Admin visibility into Work IQ usage
- No data storage — retrieves on-demand

## Feedback

GitHub Issues: https://github.com/microsoft/work-iq-mcp/issues
