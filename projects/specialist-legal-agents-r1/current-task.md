# Current Task State — specialist-legal-agents-r1 (umbrella program)

> **Last Updated**: 2026-11-25 (design v0.7: patent attorney is the first archetype; OD-4..OD-6)
> **Worktree**: `C:\code_files\spaarke-wt-specialist-legal-agents-r1` · **Branch**: `work/specialist-legal-agents-r1` · **Board**: Issue #3002 under Epic #421
> **Phase**: pre-`/design-to-spec`. No POML tasks. Do NOT run `/design-to-spec` until the owner says so.
>
> **2026-11-25 (owner):** OD-2 amended (patent attorney first; NDA stays a function, intake harness in `spaarke-intake-pipeline-r1` #3128); OD-4 Cowork + area agent for drafting; OD-5 Dataverse profile + Code Page (§3.14); OD-6 MCP shape and `docs/architecture/SPAARKE-AI-SURFACES-AND-PACKAGING.md` (PR #3147); MCP parity is `spaarkeai-mcp-bff-api-r2` (#3150). D-A8 decided (no supervision in Phase 1; chaining designed for, §3.15). Fable reuse review (`notes/profile-reuse-review-2026-11-25.md`): profile anchors on the existing `sprk_userprofile`; only new surface is a server-written `sprk_agentprofile`/`-version` pair; owner questions are D-A18. The Spaarke AI agent is removed now (mcp-r2 §4.5; tenant steps E1/E2 are the owner's). Phase 1 child slug now `specialist-legal-agents-patents-r1`.

## OPEN NOW (checkpoint 2026-11-25 evening, read first)
- **Design v0.8:** D-A18 (2)–(8) accepted; T1 specialty = `sprk_technologyarea` (DEV-only table, needs columns + solution export); taxonomy tables are keys for L2, not storage (§3.14).
- **Open with the owner:** D-A18 (1) after the persona clarification. `sprk_aipersona` is LIVE (chat default system prompt via `ResolvePersonaForChatAsync`); recommendation: keep it, profile template row points at it.
- **DEV facts (2026-11-25):** `sprk_playbookrun`/`noderun` 0 rows (BFF keeps runs in memory); `sprk_precedent` 0 rows; `sprk_technologyarea` 0 rows, 2 columns.
- **mcp-r2** (separate worktree) design v0.2 holds the MCP-side answers (incl. Dataverse MCP position §4.7).
- **Owner tenant action pending:** remove Spaarke AI (coordination runbook E1/E2).

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
- **Phase 0 runs HERE** (OD-3 amended 2026-11-24, no child project). **NDA current state reviewed 2026-11-24:** `notes/nda-current-state-2026-11-24.md`. There are three NDA review routes already: platform agreement-review, Word plus our playbook, and Cowork nda-review. There is no NDA drafting. Submit-and-return flow: the pieces exist, but nothing connects them. Two NDA standards (KNW-011 v0.1 vs accelerator v1.2) need to become one. Owner questions in §7 of that note (one standard; the submit-and-return flow as Phase 1; drafting). Do NOT start the Phase 0 spec until they are answered. **2026-11-24 owner:** `sprk_servicerequest` is the request entity (front-door-r1 not going forward; no `sprk_legalrequest`); aim for ONE intake process model; a separate processing record is likely. **2026-11-25: the submit-and-return pipeline is its OWN project, CREATED:** `spaarke-intake-pipeline-r1` (#3128, Epic #421), worktree `C:/code_files/spaarke-wt-spaarke-intake-pipeline-r1`, design v0.1 there; discussion continues in that project; run record = extend `sprk_processingjob` (owner agreed). Earlier: code review DONE (3 Fable reviews, key claims re-checked); proposal written: `notes/intake-process-model-proposal.md` (four records, ten stages, run record = extend `sprk_processingjob` recommended; invoice machinery = pattern not code). Owner decisions in its §7. Read-only master worktree removed 2026-11-25.
- **Still open, not A0 blockers:** D-A12 framing; D-A13 Microsoft-first; D-A1 front door (G8-P2 now done); D-A7 (at G1); D-A5 pricing; BR-5 (dynamic discovery as default); the R1 change (with coordination).

## Standing directives
- Never `git stash`; never `python3` in Bash; every Agent launch names a model; sub-agents can't write `.claude/`.
- Don't edit the accelerator or mcp-r1 worktrees; coordinate by message only.
- Copilot tenant artifacts: before uploading ANY agent, plugin, package or connector, send its R4 inventory row to COPILOT-PLATFORM-COORDINATION (Issue #3006). Install for the owner and test user only (R7). Tenant clicks are the owner's.
- App Insights query: use the PowerShell form in `phase0/g8/README.md`. `az` rejects KQL with `name=` aliases.
- Accelerator constraints: never put `answer-key/` or the real NDAs (Corteva, RPX/Helios) anywhere an agent can read; reuse `FORBIDDEN_IN_PACKAGE`.
