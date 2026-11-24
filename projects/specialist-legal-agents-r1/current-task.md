# Current Task State — specialist-legal-agents-r1 (umbrella program)

> **Last Updated**: 2026-11-24 ~04:40Z (checkpoint at 85% context)
> **Worktree**: `C:\code_files\spaarke-wt-specialist-legal-agents-r1` · **Branch**: `work/specialist-legal-agents-r1` · **Board**: Issue #3002 under Epic #421
> **Phase**: pre-`/design-to-spec`. No POML tasks. Do NOT run `/design-to-spec` until the owner says so.

## Quick Recovery

| Field | Value |
|---|---|
| **Active work** | **G8-P2 live test** (owner go 2026-11-23). This is the Practice-agent build path, so the owner said it must be RESOLVED, not just recorded. Run log: `phase0/g8/README.md` § "G8-P2 run log" |
| **Step** | v1.0.0–1.0.3 ❌ and the Learn control v1.0.4 (auth None) ❌: the action is listed in admin center but never reaches the model, with no BFF traffic. **Probable cause (GitHub ATK #16643, kuldeepkeshwar):** MCP agents work when **personally sideloaded** but not when **published to the org catalog**. The owner said this is ours to solve, not a support case. **Now: v1.1.0 sideload test**, two fresh-ID packages: `spaarke-legal-g8p2-sideload.zip` (real) and `spaarke-legal-g8p2-sideload-learn.zip` (control). Owner uploads both via Teams › Apps › Manage your apps › Upload an app › **Upload a custom app** (for me). R4 rows sent to coordination 2026-11-24 |
| **Design version** | design.md v0.6 + G9 (`4ed12dbee3`); latest commit `e48dc2e012` (pushed) |
| **Uncommitted** | none |

## Next action (exact)
0. Coordination S-3 (2026-11-24): "platform limit" not proven. Owner confirms which upload option 1.0.0–1.0.4 used, and screenshots the tenant checks (README § "Coordination S-3"). Third zip `spaarke-legal-g8p2-org-learn.zip` (fresh id `0421c980-…`) goes through the **org catalog**, paired with the sideloaded Learn control.
1. Owner sideloads both v1.1.0 zips (Upload a custom app, for me). In m365.cloud.microsoft/chat, new chat with each agent: the Learn one gets "Search Microsoft Learn for declarative agent MCP plugins", and `spaarke-legal-g8p2-sl` gets "Show my Spaarke decisions" (sign in if a card appears; Cancel any approval card).
2. Check App Insights for `initialize` / `tools/list` / `tools/call` from client "Sydney" in that minute.
3. **Sideload ✅:** the build path works. Record it, and log org-catalog distribution as a D-A1 deployment risk (track #16643). Then G8-P2's real questions: Word/Teams surfaces and one call per turn (R1′).
4. **Sideload ❌:** if the upload option is missing, custom app upload is off for the owner (Teams app setup policy). Otherwise, generate an agent with Microsoft's own tooling (ATK / WIQD; installing needs the owner's OK) and diff it against ours.
5. Record the result in the README run log and design §8 G8-P2. Send it to COPILOT-PLATFORM-COORDINATION [0afb62] with removal confirmations (G8-a, G8-p2, both -sl, by 2026-11-30).

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
