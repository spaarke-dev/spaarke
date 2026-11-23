# Coordination map — who owns what in Microsoft 365 Copilot

> Kept by copilot-platform-coordination. Last updated 2026-11-23. Peer messages are information, not owner approval.

| Project | Session | Owns (Copilot) | Inventory rows | Status / next Copilot milestone |
|---|---|---|---|---|
| spaarkeai-mcp-bff-api-r1 | `mcp-r1` | The BFF MCP server (`/mcp/{area}`), its **single** tenant registration ("Spaarke MCP"), the OAuth registration and Entra client, the plugin package (task 040), the `spaarke-decisions` skill and widgets | C1, C2, A1, A2 | Phase 0 T10 (skill naming a connector tool) still open; task 040 (package) decides R2. C2 removed after 040 |
| spaarke-legal-ai-accelerator-r1 | `acclerator-r1` | NDA, Legal Hold and Legal Review agents and skills; the four-tier package; T0/T1 practice content | D2, D3, D4, S1–S6, X1 | Renames skills to `spaarke-*` at next plugin build; owner W-tests pending |
| specialist-legal-agents-r1 (#3002) | setup session (`set-up-the-specialist-le`) | Persona (archetype) agents per practice area, T2/T3 runtime | none yet | Design phase (v0.5); D-A11 NDA ownership open. Its first tenant artifact goes through R4 |
| ai-m365-copilot-integration | none live | The April "Spaarke AI" declarative agent and bot | D1, A3 | Owner decision on D1 |
| unknown | — | "Spaarke Dataverse MCP" Copilot Studio agent; "Copilot in Power Apps – Spaarke Platform [Draft]" | D5, D6 | Owner to name an owner |
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
| acclerator-r1 | 2026-11-23 | pending (asked: S6 installed now, D4 installed?, S4 still used?) |
| specialist-legal-agents-r1 setup session | 2026-11-23 | pending |
