# Current Task State — specialist-legal-agents-r1 (umbrella program)

> **Last Updated**: 2026-11-24 (gate A0 cleared; V3 deferred)
> **Worktree**: `C:\code_files\spaarke-wt-specialist-legal-agents-r1` · **Branch**: `work/specialist-legal-agents-r1` · **Board**: Issue #3002 under Epic #421
> **Phase**: pre-`/design-to-spec`. No POML tasks. Do NOT run `/design-to-spec` until the owner says so.

## Quick Recovery

| Field | Value |
|---|---|
| **Active work** | **G8-P2 live test** (owner go 2026-11-23). This is the Practice-agent build path, so the owner said it must be RESOLVED, not just recorded. Run log: `phase0/g8/README.md` § "G8-P2 run log" |
| **Step** | **G8-P2 ✅** (Chat, Word, plain Chat no leak, Teams = Chat). **V1 ⚠️:** an action-changing update takes the action offline for a while (1.1.2 back after ~40 min); BR-3 revised. **V2 ✅** (`cb5afd1c`, dynamic discovery + OAuth, owner + `demo@`); This includes a brand-new user's first sign-in. BR-5 now proposes dynamic discovery as the default. Design §3.13: MCP Apps widgets are the default UI (owner, D-A17); SPFx is the exception. **Next:** removals by 30 Nov; open: V3 deferred until D3; V1a dropped; G11 optional |
| **Design version** | design.md v0.6 + G9 (`4ed12dbee3`); latest commit `e48dc2e012` (pushed) |
| **Uncommitted** | none |

## Next action (exact)
1. Tell COPILOT-PLATFORM-COORDINATION [0afb62] the V2 result (the R4 row and V1 were sent ~17:45Z).
2. Removals by 2026-11-30 (owner, admin center): G8-a agent + 2 skills, G8-p2 `802fd9cc`, G8-p2-sl `d38f8df8`, G8-p2-org-learn `0421c980`, G8-p2-dd `cb5afd1c`.
3. Open tests:
   - V1a: `?pkg=` on a fresh id; wait an hour before judging.
   - V3: Model 1, second tenant.
   - V5 dropped: owner direction 2026-11-24 to remove the "Spaarke AI" agent (D1) and its bot backend (A3); coordination writes the removal steps.
   - V1a (`?pkg=`) recommended to drop: dynamic discovery makes package-version logging low value.
   - V3 DEFERRED (not runnable yet, no package built): the connector app `04f37fe4` is single-tenant (`AzureADMyOrg`) and A10 pins OAuth to the Spaarke tenant, so an ACME user cannot sign in. It needs a multitenant app, a new OAuth registration, ACME consent and a BFF issuer change (T-7, ADR-028 path B). Run it only if provisioning D3 keeps Model 1; candidate tenant ACME.
   - G11 is optional (MCP Apps widgets are the default).
   Build rules BR-1–BR-7: the note and the `declarative-agent` skill Step 5.5.
4. App Insights: BFF requests are sampled at about 25–33% now, and caller identity isn't logged. Use several turns per check.

## G8 result (recorded in phase0/g8/README.md, commit 9c557d7e95)
- An Agent Builder agent cannot use the tenant federated connector (A1 ❌, A2 "No connectors available" ❌, skills inside the agent ❌). Plain Chat works (`spike_whoami` ✅).
- App Insights shows only `tools/list` from Sydney, with no `tools/call`.
- Agents reach MCP only through their **own** tool. The owner's Copilot synopsis agrees: one MCP server, tenant connector = the horizontal path, agents = direct MCP clients.

## Open owner decisions
- **Gate A0 CLEAR (2026-11-24):** D-A11 split by layer, D-A16 one roadmap, Q7 shared catalogue (accelerator refreshes first); Q2 resolved from the ontology model: the positions master is a **Playbook** (D-209), not "Policy versions". Still to do: tell accelerator r1 about D-A11/Q7/D-A16.
- **Next step (owner go needed):** split off the Phase 0 child folder (design.md from §7–§8) and run `/design-to-spec` on it. Do NOT start without the owner saying go.
- **Still open, not A0 blockers:** D-A12 framing; D-A13 Microsoft-first; D-A1 front door (G8-P2 now done); D-A7 (at G1); D-A5 pricing; BR-5 (dynamic discovery as default); the R1 change (with coordination).

## Standing directives
- Never `git stash`; never `python3` in Bash; every Agent launch names a model; sub-agents can't write `.claude/`.
- Don't edit the accelerator or mcp-r1 worktrees; coordinate by message only.
- Copilot tenant artifacts: before uploading ANY agent, plugin, package or connector, send its R4 inventory row to COPILOT-PLATFORM-COORDINATION (Issue #3006). Install for the owner and test user only (R7). Tenant clicks are the owner's.
- App Insights query: use the PowerShell form in `phase0/g8/README.md`. `az` rejects KQL with `name=` aliases.
- Accelerator constraints: never put `answer-key/` or the real NDAs (Corteva, RPX/Helios) anywhere an agent can read; reuse `FORBIDDEN_IN_PACKAGE`.
