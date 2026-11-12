# Spaarke MCP spike (Phase 0) - Copilot app packages

> SPIKE ONLY (project spaarkeai-mcp-bff-api-r1, task 003). This folder lives on the `spike/spaarkeai-mcp-phase0`
> branch and **must never be merged to master**. It records nothing, holds no secrets and calls only the spike
> routes of the dev BFF.

Two upload-ready app packages for the remaining live Copilot tests. Both point at the dev BFF spike host:

| Plugin | MCP server URL | Tools served |
|---|---|---|
| Spaarke MCP spike A | `https://spaarke-bff-dev.azurewebsites.net/mcp/legal-operations` | `spike_echo`, `spike_echo_3mb`, `spike_echo_6mb`, `spike_write`, `spike_write_app` (app-only), `spike_unannotated`, `spike_whoami` |
| Spaarke MCP spike B | `https://spaarke-bff-dev.azurewebsites.net/mcp/spike-area-b` | `spike_echo_b` |

Auth for both is `OAuthPluginVault` with the existing Teams Developer Portal OAuth registration
(`reference_id` / `referenceId` is an identifier, not a secret).

## What each test needs

- **T2 (widget in Chat / Cowork via a plugin package)** and **T14 (two connectors in one package):** use the
  `connectors/` package. It declares two `agentConnectors` (A and B) in the Microsoft 365 app manifest (v1.30), which is
  the route Microsoft Learn documents for a plugin package that is not a declarative agent. Copilot Cowork still
  requires a packaged tool description file, so each connector references one (`toolDescription-a.json` /
  `toolDescription-b.json`, captured verbatim from the host's `tools/list`).
- **Declarative-agent route (fallback / comparison):** `declarative-agent/` is the same two servers as two
  plugin manifests (schema v2.4, runtime `RemoteMCPServer`) attached to one declarative agent. Microsoft Learn states that
  MCP plugin manifests are supported as declarative-agent actions only, so this package exists to test the same tools
  where that route is the supported one. In `ai-plugin-a.json` the app-only tool `spike_write_app` is deliberately not
  listed under `functions`/`run_for_functions` (so the model cannot see it); it stays in `mcp-tools-a.json`. If the
  widget's app-only call is refused on this route, switch plugin A to dynamic discovery: set `"functions": []`, remove
  `mcp_tool_description`, and set `"run_for_functions": ["*"]`.

## Zip

Put the files at the zip root (no wrapping folder). From this folder in PowerShell:

```powershell
Compress-Archive -Path .\connectors\* -DestinationPath .\spaarke-mcp-spike-connectors.zip -Force
Compress-Archive -Path .\declarative-agent\* -DestinationPath .\spaarke-mcp-spike-agent.zip -Force
```

The `.zip` files are not committed. Upload through Teams Developer Portal (Apps, Import app) or the Microsoft 365
admin center, in the dev tenant only.

## Before you upload

- The OAuth registration must be restricted to **Any Teams app** (Microsoft Learn: an app-bound registration makes
  every tool call return 404) and its **Base URL** should correspond to the MCP server `url`. See the report note on
  whether that Base URL covers `/mcp/spike-area-b`; if sign-in or tool calls fail on connector B only, that is the
  first thing to check.
- Each package has its own fresh app id, so both can be installed side by side. Remove the old one before re-uploading
  the same package with a changed manifest (bump `version`).
- No tokens, secrets or e-mail addresses are in these files; developer URLs are `example.com` placeholders.
