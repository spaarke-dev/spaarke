# Current Task State — specialist-legal-agents-r1 (umbrella program)

> **Last Updated**: 2026-11-24 ~04:40Z (checkpoint at 85% context)
> **Worktree**: `C:\code_files\spaarke-wt-specialist-legal-agents-r1` · **Branch**: `work/specialist-legal-agents-r1` · **Board**: Issue #3002 under Epic #421
> **Phase**: pre-`/design-to-spec`. No POML tasks. Do NOT run `/design-to-spec` until the owner says so.

## Quick Recovery

| Field | Value |
|---|---|
| **Active work** | **G8-P2 live test** (owner go 2026-11-23). This is the Practice-agent build path, so the owner said it must be RESOLVED, not just recorded. Run log: `phase0/g8/README.md` § "G8-P2 run log" |
| **Step** | **G8-P2 ✅ in Chat, Word, plain Chat (no leak); Teams = Chat** (2026-11-24, App Insights verified). **V1 in progress:** owner uploaded `spaarke-legal-g8p2-sl` **1.1.1** (same id `d38f8df8`, action URL `…/mcp/legal-operations?pkg=1.1.1`) as an update ~16:2xZ. As of 16:35Z no Sydney call since the upload. **Next:** owner asks the agent in a new chat; read `requests.url` for `?pkg=1.1.1`. Also new: design §3.13 Copilot UX components (SPFx), G11, T-8, D-A17 |
| **Design version** | design.md v0.6 + G9 (`4ed12dbee3`); latest commit `e48dc2e012` (pushed) |
| **Uncommitted** | none |

## Next action (exact)
1. **V1 result:** after the owner's new-chat question to `spaarke-legal-g8p2-sl`, query `requests | where url has 'mcp' | summarize count(), max(timestamp) by url, resultCode` (no KQL aliases). `?pkg=1.1.1` = updates carry action changes (BR-3 holds); plain URL = old action still bound; no call = update broke the action. Note: the tenant connector also calls the plain URL, so match by time and by Sydney `tools/call` traces. Record in the README V1 section, note §5, BR-3, design §8; tell COPILOT-PLATFORM-COORDINATION [0afb62].
2. Removals by 2026-11-30: G8-a agent + 2 skills, G8-p2 `802fd9cc`, G8-p2-sl `d38f8df8`, G8-p2-org-learn `0421c980`.
3. V2 (dynamic discovery + OAuth on a fresh id), then G11 (Copilot UX component spike; needs the owner's OK to install the SPFx toolchain). Build rules BR-1–BR-7: the note and the `declarative-agent` skill Step 5.5.

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
