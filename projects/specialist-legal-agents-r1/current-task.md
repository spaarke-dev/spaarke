# Current Task State — specialist-legal-agents-r1 (umbrella program)

> **Last Updated**: 2026-11-24 ~04:40Z (checkpoint at 85% context)
> **Worktree**: `C:\code_files\spaarke-wt-specialist-legal-agents-r1` · **Branch**: `work/specialist-legal-agents-r1` · **Board**: Issue #3002 under Epic #421
> **Phase**: pre-`/design-to-spec`. No POML tasks. Do NOT run `/design-to-spec` until the owner says so.

## Quick Recovery

| Field | Value |
|---|---|
| **Active work** | **G8-P2 live test** (owner go 2026-11-23). This is the Practice-agent build path, so the owner said it must be RESOLVED, not just recorded. Run log: `phase0/g8/README.md` § "G8-P2 run log" |
| **Step** | **Found:** the fresh-id Learn control through the **org catalog** WORKS (2026-11-24). So the route and the tenant are fine; the failures (1.0.0–1.0.4) were all updates to app id `802fd9cc`, which never loaded its action. Working hypothesis: catalog updates of an existing app id don't refresh its action. **Now:** owner installs `spaarke-legal-g8p2-sideload.zip` (real action, fresh id `d38f8df8-…`) through the **org catalog** for owner + test user, and asks "Show my Spaarke decisions" |
| **Design version** | design.md v0.6 + G9 (`4ed12dbee3`); latest commit `e48dc2e012` (pushed) |
| **Uncommitted** | none |

## Next action (exact)
1. Owner uploads `phase0/g8/spaarke-legal-g8p2-sideload.zip` through the admin center / org catalog (owner + test user, R7). New chat with `spaarke-legal-g8p2-sl` at m365.cloud.microsoft/chat: "Show my Spaarke decisions" (sign in if a card appears; Cancel any approval card). Note the time.
2. Check App Insights for `initialize` / `tools/list` / `tools/call` from client "Sydney" in that minute.
3. **✅:** the Practice build path works. Then G8-P2's real questions: Word and Teams surfaces, and one call per turn (R1′). Later D-A1 check: does an org-catalog **update** of an agent pick up a changed action?
4. **❌ while the Learn control works:** the difference is our server or OAuth. Get the debug card, compare the sign-in flow, and check whether the OAuth registration accepts the new app id.
5. Record the result in the README run log and design §8 G8-P2. Send it to COPILOT-PLATFORM-COORDINATION [0afb62] with removal confirmations (G8-a, G8-p2 `802fd9cc`, G8-p2-sl, G8-p2-org-learn, by 2026-11-30).

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
