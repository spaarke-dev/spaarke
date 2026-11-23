# Current Task State — specialist-legal-agents-r1 (umbrella program)

> **Last Updated**: 2026-11-23 ~21:40Z (setup session, checkpoint at 87% context)
> **Worktree**: `C:\code_files\spaarke-wt-specialist-legal-agents-r1` · **Branch**: `work/specialist-legal-agents-r1` · **Board**: Issue #3002 under Epic #421
> **Phase**: pre-`/design-to-spec`. No POML tasks. Do NOT run `/design-to-spec` until the owner says so.

## Quick Recovery

| Field | Value |
|---|---|
| **Active work** | Writing **design.md v0.6**. Owner said 2026-11-23: "and then yes write v0.6". It folds in Copilot's Ask/Do/Practice synopsis (with corrections), the G8 result, the build paths, the existing code inventory, the "Spaarke AI" agent's usefulness and an auth-ADR review |
| **Step** | v0.6 written and committed. Next: the owner reviews it; the owner decides on G8-P2 after v0.6 (coordination says do not create it yet) |
| **Design version** | design.md v0.5 + G8 interim result (last commit `9c557d7e95`) |
| **Uncommitted** | none |

## Next action (exact)
1. Read the code-mapper result.
2. Write these into design.md:
   - **§3.12 "Agent build paths after G8".** A table: Agent Builder ❌ / Copilot Studio (MCP tool = own tool; agents are Dataverse solution components, fits Spaarke ALM; billing to check) / **coded DA with own MCP action (default Practice vehicle)** / Spaarke runtime agent (custom engine; T3). Plus the existing code, and whether "Spaarke AI" (`src/solutions/CopilotAgent`, D1 in the inventory, v1.1.0 installed, v1.5.0 uploaded) is useful.
   - The answers to the owner's Q1 and Q2:
     - Q1: Ask/Do are **not agents**: they are skills + connector in a plugin package, so Copilot Studio is not needed for them.
     - Q2: Practice = a coded DA (Copilot runtime) or our own runtime. Correct, with Studio as a third option.
   - **§11 T-7 auth:**
     - ADR-028 **A10** (on the mcp-r1 branch, not master) already covers connector + plugin package + **declarative agent**, so a coded DA with its own MCP action is covered (same dedicated client).
     - A Studio MCP tool is covered only if its connection supports a Spaarke-tenant authority + PKCE + the dedicated client; otherwise an A10 follow-up.
     - A runtime agent is a new plane (channel SSO token) and needs **OBO to Work IQ** (`WorkIQAgent.Ask`, delegated-only), so amendment A11, path B.
     - Background Practice can't use Work IQ (delegated-only + ADR-052), so T-1/T-2 stand.
     - Owner principle: "if Microsoft requires an auth approach and it is safe, amend the ADR". Record it; security sign-off still applies.
   - **§12:** revise **D-A1** (the front door: coded DA default, runtime agent for T3, decided after G9/G10). §8 G8 final:
     - Agent Builder ❌;
     - P1 dropped;
     - **P2** = coded DA with its own MCP action on `/mcp/legal-operations` (does it run once, with no double-fire?).
   - **G9:** desk research. Can a custom engine agent appear in Chat, Word and Cowork after Ignite (GA vs preview), with SSO? Work IQ GA status, and delegated calls from the BFF.
   - **G10** (optional): a Copilot Studio MCP-tool comparison.
   - **Header:** v0.6.
3. Commit (`export PATH="/c/code_files/spaarke/node_modules/.bin:$PATH"` first: this worktree has no prettier) and push.
4. Message COPILOT-PLATFORM-COORDINATION [0afb62]:
   - the G8 result is final;
   - propose R1 = "one tenant-wide connector per URL; agent-private actions allowed", with Copilot's diagram as the rationale;
   - P2 needs an R4 row before upload. G8-a is still live (delete by 2026-11-30); G8-p was never uploaded.
5. Answer the owner: the corrections to Q1/Q2, the Spaarke AI verdict, the ADR verdict, and a link to v0.6.

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
