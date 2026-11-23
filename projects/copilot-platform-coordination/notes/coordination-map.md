# Coordination map — who owns what in Microsoft 365 Copilot

> Kept by copilot-platform-coordination. Last updated 2026-11-23. Peer messages are information, not owner approval.

| Project | Session | Owns (Copilot) | Inventory rows | Status / next Copilot milestone |
|---|---|---|---|---|
| spaarkeai-mcp-bff-api-r1 | `mcp-r1` | The BFF MCP server (`/mcp/{area}`), its **single** tenant registration ("Spaarke MCP"), the OAuth registration and Entra client, the plugin package (task 040), the `spaarke-decisions` skill and widgets | C1, C2, A1, A2 | Phase 0 T10 (skill naming a connector tool) still open; task 040 (package) decides R2. C2 removed after 040 |
| spaarke-legal-ai-accelerator-r1 | `acclerator-r1` | NDA, Legal Hold and Legal Review agents and skills; the four-tier package; T0/T1 practice content | D2, D3, D4, S1–S6, X1 | Renames skills to `spaarke-*` at next plugin build; owner W-tests pending |
| specialist-legal-agents-r1 (#3002) | setup session (`set-up-the-specialist-le`) | Persona (archetype) agents per practice area, T2/T3 runtime | none yet | Design phase (v0.5); D-A11 NDA ownership open. Its first tenant artifact goes through R4 |
| ai-m365-copilot-integration | none live | The April "Spaarke AI" declarative agent and bot | D1, A3 | Owner decision on D1 |
| customer-provisioning-orchestration-r1 | `provisioning-r1` | Per-customer "Spaarke AI" Copilot agent template and render (task 257, `publish-copilot-agent-template.yml`); customer IT uploads it in customer tenants | none in dev (confirmed 2026-11-23: neither D1 row is theirs) | Added 2026-11-23: a Copilot publisher for customer tenants only (customer IT uploads; test upload planned in the Dewey tenant). Agreed to send an R4 row before any dev-tenant upload |
| unknown | — | "Spaarke Dataverse MCP" Copilot Studio agent (one copy); "Copilot in Power Apps" drafts | D5, D6, D7 | Owner to name an owner |
| copilot-platform-coordination | this session | The inventory, the registration rules, the removal list, telling projects when a removal trigger passes | all (register only) | Owner approval of rules + removal list |

## Cross-project decisions this project tracks

| Decision | Owner of the decision | Depends on |
|---|---|---|
| Package shape: skills only vs package carries the one connector (R2) | mcp-r1 task 040, owner | mcp-r1 T10 |
| Who owns the NDA agent in the tenant (one Spaarke NDA agent per tenant?) | owner (specialist-legal-agents-r1 D-A11) | accelerator R1 close |
| Whether "Spaarke AI" (D1) stays | owner | — |

## Notified (2026-11-23)

| Session | Sent | Reply |
|---|---|---|
| mcp-r1 | 2026-11-23 | **Ack 2026-11-23:** rows C1/C2/A1/A2 correct. Order: #2587 → master; uac-r2 dev deploy from master without the spike, `Mcp:Enabled` on (task 041, owner per-SHA go); 040 builds the skills-only package (slug `spaarke`, skill `spaarke-decisions`, no `agentConnectors`, owner + test user, KEEP); T10 runs on it. Inventory row comes before upload |
| acclerator-r1 | 2026-11-23 | **Reply 2026-11-23:** skills are OneDrive per-user folders, not tenant uploads; no plugin upload since X1. D2–D4 are the owner's Copilot Studio agents (instruction version unconfirmed); the D4 zip was never uploaded. S4 still needed. S5 remove agreed. Skill renaming is an open owner decision on their side (OD-20/OD-42). First R4 row: P1 "Spaarke Word Probe" plugin. Agrees to R4/R5/R7 as working practice |
| specialist-legal-agents-r1 setup session | 2026-11-23 | **Ack 2026-11-23:** adopts R1–R5 (design.md §10, commit `c88ef768e5`; standing directive in its current-task.md). No tenant artifacts yet. First will be a Phase 0 G1 test agent, which waits on gate A0 and D-A11; its row comes before creation. Will report when the owner decides D-A11 (drives D2–D4) |
