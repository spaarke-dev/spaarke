# Phase 0 test G8: can a declarative agent use the tenant's Spaarke MCP connector?

> **Program:** specialist-legal-agents-r1 (design.md §8 G8). Same test as copilot-platform-coordination **S-3**.
> **Owner go:** 2026-11-23 ("Yes, run G8 early").
> **Throwaway.** Both test agents are removed as soon as the result is recorded here.
> **Who does what:**
> - **The owner** creates, uploads and removes the agents. Claude sessions never click tenant or admin actions (inventory rule).
> - **The owner or the test user** sends the prompts.
> - **This session** checks App Insights and records the result.

## The question

Can an agent with **no MCP action of its own** call tools from the "Spaarke MCP" federated connector already registered in the tenant (inventory C1, `/mcp/legal-operations`)?

**What the docs say.** Learn lists "Agents" among the experiences that support federated connectors. It also says an agent's own MCP actions are private to that agent. It says nothing about how an agent attaches a tenant connector.

**If the agent cannot use it**, every persona agent needs its own registration of the server. That breaks rule R1 (one registration per MCP URL) and goes to the owner before Phase 1.

## What is tested

| Step | Agent | Configuration | What it shows |
|---|---|---|---|
| **A1 (control)** | `spaarke-legal-g8-ab-test`, built in Agent Builder | No knowledge, no actions | Does an agent reach the tenant connector **by default**? |
| **A2** | the same agent, edited | Knowledge › Copilot connectors › **Spaarke MCP**, if the picker lists it | Can Agent Builder attach the federated connector, and do the calls then work? |
| **P1** | `spaarke-legal-g8-pkg-test`, packaged (`package/`) | `GraphConnectors` capability with no `connections` list (Learn: "all connectors the user can access") | Does the packaged route reach it, with no `agentConnectors` and no action? |

Run every step in **Chat**. Also run P1 in **Word (web)** and **Teams** where the agent is offered. Cowork cannot call declarative agents (design §3.11).

## Before you start

1. **"Spaarke MCP" is connected for the test user:** Chat › … › Settings › Sources › Spaarke MCP › Connected.
2. **The spike plugin (C2) is still installed.** It must stay until mcp-r1 runs S-1, so it is **not removed** for this test.
   - Its connector A shares the URL `/mcp/legal-operations`, and the BFF cannot tell the two registrations apart.
   - Two facts from earlier tests help: C2's package connectors failed in Chat before reaching the BFF (mcp-r1 T2/T13), and C2 is not an agent action.
   - So a BFF call during an agent conversation in Chat came through a **tenant** registration, not the agent's own action. That is what G8 asks.
   - Which tenant registration served it (C1 or C2-A) is recorded as "C1 probable". If it matters, repeat one prompt after C2 is removed.
3. **The dev BFF serves `spike_echo` and `spike_whoami`.** Dev build `a48398746d` includes spike-v2.
   - When mcp-r1 deploys without the spike (task 041), use a real read tool instead, for example the read tool listed first in `tools/list`.
4. **Send the inventory rows below to copilot-platform-coordination before creating anything (R4).** This is done (see "Inventory rows").

## Steps for the owner

### A1: Agent Builder, no knowledge

1. Open Microsoft Copilot › **New agent** › **Configure**.
2. Fill in:
   - **Name:** `spaarke-legal-g8-ab-test`
   - **Description:** `Phase 0 test G8. Not for real work.`
   - **Instructions:** paste the `instructions` text from `package/declarativeAgent.json`. The two agents must behave the same.
   - **Knowledge:** none. **Capabilities:** none.
3. Click **Create**. Keep it private: do not share it.
4. In the agent, as the test user, send these prompts in order, each in a new chat with the agent:
   1. `Call the Spaarke MCP tool spike_echo with the message g8-A1`
   2. `List every tool and source you can use in this conversation`
5. Note what the agent says, and the time.

### A2: the same agent with the connector as knowledge

1. Edit the agent › **Configure** › **Knowledge**. Is **Spaarke MCP** offered under Copilot connectors?
   - Note **yes or no**, and **what the picker lists** (screenshot).
2. If yes, add it, then **Update**.
3. Send `Call the Spaarke MCP tool spike_echo with the message g8-A2`, then `Call the Spaarke MCP tool spike_whoami`.

### P1: packaged agent

1. In PowerShell, from `projects/specialist-legal-agents-r1/phase0/g8/`. Files go at the zip root; the zip is gitignored.

   ```powershell
   Compress-Archive -Path .\package\* -DestinationPath .\spaarke-legal-g8-pkg-test.zip -Force
   ```

2. Upload it in the **dev tenant only**, and install it for the **owner and the test user only, never All users** (coordination rule R7).
   - In admin center › Agents › Tools › Registry, or Integrated apps, choose **Specific users/groups** and pick those two accounts.
   - Alternatively, use Teams Developer Portal › Apps › Import app, then Preview/Install for yourself only.
   - If the route you use cannot be scoped this way, **don't upload**. Tell this session where it would land, so the removal step is right and coordination can be told.
3. In the agent (Chat), send:
   - `Call the Spaarke MCP tool spike_echo with the message g8-P1`
   - `Call the Spaarke MCP tool spike_whoami`
   - `List every tool and source you can use in this conversation`
4. If Word (web) or Teams offers the agent, repeat the echo there: `g8-P1-word`, `g8-P1-teams`.

**Afterwards:** tell this session that you are done, with the times and screenshots. This session checks App Insights for each `g8-…` call and records the result. Then remove both agents (Agent Builder › … › Delete; the package from where it was uploaded), and tell copilot-platform-coordination they are gone.

## App Insights check (this session)

Use PowerShell. The `az` query fails with `BadArgumentError` when the KQL has `name=` aliases, so use the form below.

```powershell
$id = az resource show -g spe-infrastructure-westus2 -n spe-insights-dev-67e2xz --resource-type Microsoft.Insights/components --query properties.AppId -o tsv
az monitor app-insights query --apps $id --offset 3h --analytics-query "requests | where url contains '/mcp/' | summarize count() by name, resultCode, bin(timestamp, 1m) | order by timestamp desc"
```

**Results:**
- **Pass:** a `tools/call` lands in the minute of the prompt.
- **Fail:** nothing reaches the BFF, and the agent says it has no such tool.
- **Neither:** a model that claims a call ran when nothing reached the BFF has its claim recorded separately, as mcp-r1 T4 saw.

## Inventory rows (sent to copilot-platform-coordination before creation, R4)

| # | Artifact | Kind | Owner project | Purpose | Status | Removal trigger / who |
|---|---|---|---|---|---|---|
| G8-a | `spaarke-legal-g8-ab-test` | Agent Builder declarative agent, private to its creator. No actions. Knowledge: none (A1), then Spaarke MCP if offered (A2) | specialist-legal-agents-r1 | Phase 0 G8 / S-3: can an agent use the tenant connector? | **REMOVE** (throwaway) | Owner deletes it in Agent Builder the same day G8 is recorded; latest **2026-11-30** |
| G8-p | `spaarke-legal-g8-pkg-test` (app id `ac7da782-71bc-4c13-ba90-44eea8ef48b8`) | App package with one declarative agent (v1.6). `GraphConnectors` capability, **no `agentConnectors`, no actions** | specialist-legal-agents-r1 | As G8-a, packaged route | **REMOVE** (throwaway) | As G8-a |

## Result (filled in after the run)

| Step | Surface | Time (UTC) | Agent's answer | BFF `tools/call` seen? | Verdict |
|---|---|---|---|---|---|
| A1 | Chat | | | | |
| A2 picker | — | | Spaarke MCP listed? | — | |
| A2 | Chat | | | | |
| P1 | Chat | | | | |
| P1 | Word (web) | | | | |
| P1 | Teams | | | | |

**Conclusion and what it means for R1/R2 and G2:** (to write)
