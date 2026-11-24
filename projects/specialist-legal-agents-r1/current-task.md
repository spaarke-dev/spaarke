# Current Task State — specialist-legal-agents-r1 (umbrella program)

> **Last Updated**: 2026-11-24 ~04:40Z (checkpoint at 85% context)
> **Worktree**: `C:\code_files\spaarke-wt-specialist-legal-agents-r1` · **Branch**: `work/specialist-legal-agents-r1` · **Board**: Issue #3002 under Epic #421
> **Phase**: pre-`/design-to-spec`. No POML tasks. Do NOT run `/design-to-spec` until the owner says so.

## Quick Recovery

| Field | Value |
|---|---|
| **Active work** | **G8-P2 live test** (owner go 2026-11-23). This is the Practice-agent build path, so the owner said it must be RESOLVED, not just recorded. Run log: `phase0/g8/README.md` § "G8-P2 run log" |
| **Step** | v1.0.0–1.0.3 all ❌: the agent installs; admin center › Data & tools lists the action "Spaarke MCP G8-P2"; the model says the action is "not exposed among my available tools"; **no BFF traffic**; no debug card (owner has M365 Copilot Premium). Ruled out: OAuth registration (Any Teams app, base URL OK), package validation (passed), dynamic vs pinned tools, shared URL (trailing slash), instructions. **Now: control v1.0.4** (`phase0/g8/control-learn/`, zip `spaarke-legal-g8p2-control-learn.zip`, same app id; action on the public Learn MCP `https://learn.microsoft.com/api/mcp`, auth None). Owner uploads it and asks a Learn question |
| **Design version** | design.md v0.6 + G9 (`4ed12dbee3`); latest commit `e48dc2e012` (pushed) |
| **Uncommitted** | none |

## Next action (exact)
1. Owner uploads `phase0/g8/spaarke-legal-g8p2-control-learn.zip` (v1.0.4), opens m365.cloud.microsoft/chat, starts a new chat with the agent, and asks "Search Microsoft Learn for declarative agent MCP plugins".
2. **Control ✅** means MCP actions in agents work in this tenant, so the fault is our server or OAuth. Next: compare with a Work IQ Dev Tools / Agents Toolkit generated agent (`wiqd agent add action --mcp-server-url`, with its own provisioned OAuth registration; installing WIQD needs the owner's OK). Check whether our server needs something the Learn server doesn't.
3. **Control ❌** means the tenant or platform blocks MCP actions in agents. Next: tenant settings (Teams app policies / Copilot agent settings) and a Microsoft support case with the package plus the no-traffic evidence. D-A1 fallback: Copilot Studio MCP tool (G10).
4. Record the result in the README run log and design §8 G8-P2. Send it to COPILOT-PLATFORM-COORDINATION [0afb62], which also wants removal confirmations for G8-a and G8-p2 (latest 2026-11-30).
5. Afterwards, restore the real G8-P2 package (v1.0.3 is in `package-p2/`) if the control passes.

## G8 result (recorded in phase0/g8/README.md, commit 9c557d7e95)
- An Agent Builder agent cannot use the tenant federated connector (A1 ❌, A2 "No connectors available" ❌, skills inside the agent ❌). Plain Chat works (`spike_whoami` ✅).
- App Insights shows only `tools/list` from Sydney, with no `tools/call`.
- Agents reach MCP only through their **own** tool. The owner's Copilot synopsis agrees: one MCP server, tenant connector = the horizontal path, agents = direct MCP clients.

## Open owner decisions
- **D-A11:** NDA ownership. Claude proposed a split by layer. The owner chose "Discuss first"; not decided.
- **The rest:**
  - D-A12 framing; D-A13 Microsoft-first; D-A16 one roadmap; Q7 catalogue owner; D-A7 (decided at G1).
  - D-A1 revision (v0.6); the P2 go; the R1 change (with coordination).
  - Ontology-r1 must confirm "Policy versions".

## Standing directives
- Never `git stash`; never `python3` in Bash; every Agent launch names a model; sub-agents can't write `.claude/`.
- Don't edit the accelerator or mcp-r1 worktrees; coordinate by message only.
- Copilot tenant artifacts: before uploading ANY agent, plugin, package or connector, send its R4 inventory row to COPILOT-PLATFORM-COORDINATION (Issue #3006). Install for the owner and test user only (R7). Tenant clicks are the owner's.
- App Insights query: use the PowerShell form in `phase0/g8/README.md`. `az` rejects KQL with `name=` aliases.
- Accelerator constraints: never put `answer-key/` or the real NDAs (Corteva, RPX/Helios) anywhere an agent can read; reuse `FORBIDDEN_IN_PACKAGE`.
