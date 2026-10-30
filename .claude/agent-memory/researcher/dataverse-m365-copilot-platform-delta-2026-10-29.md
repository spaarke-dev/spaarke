---
name: dataverse-m365-copilot-platform-delta-2026-10-29
description: Oct-2026 delta for Dataverse MCP / BYO-MCP cert (NOT GA — Dec 2026 target), Entra Agent ID Dataverse (still preview), Business Apps in Work IQ default-on (MC1483492), Fabric IQ new ontology experience (richer, still preview), DA manifest v1.9, Copilot in MDA custom default agent, SPE Retrieval still preview
metadata:
  type: project
---

# Dataverse / M365 Copilot / Fabric IQ delta (2026-09-21 → 2026-10-29)

Updates [[fabric-iq-vs-dataverse-ontology-2026-09-21]], [[mcp-ecosystem-publishing-2026-09-01]], [[dataverse-mcp-refresh-2026-07-05]], [[m365-copilot-agent-guest-crosstenant-2026-10-08]]. Companion: [[foundry-work-iq-delta-2026-10-29]].

## Corrections to the 2026-09-21 synopsis
- **BYO-MCP / ISV MCP certification is NOT GA.** Learn MCP management server still preview (upd 08-03); roadmap **RM576752 (pub 10-16) "In development", GA target Dec 2026**. The 07-06 Dataverse blog stated no GA. `CreateToolWithCustomAPI` only on the preview server. → Direct distribution (DA plugin + Copilot Studio connector) is primary; certification = 2027 option.
- **Entra Agent ID for Dataverse** — Learn agent-users "(preview)" (upd 09-24), agent *application* users "not available yet"; RM576843 (10-16) says Launched/GA Aug 2026. Conflict → treat as preview.

## Changes
- Dataverse `/api/mcp`: no change (15 tools, delegated-only, metered; tools 10-02, FAQ 10-10).
- **MC1483492 (09-30): Business Apps in Work IQ (Dataverse data + semantic model + Business Skills) default-ON** for new production envs created ≥ 09-30 with Copilot Premium + Power Apps Premium/D365; excludes EEA/gov/non-prod. Release-plan page archived (public preview 06-22, no GA). Outlook/Word grounding still preview. (Contrast: Work IQ *API* page 10-13 says Dataverse "not a near-term priority" — different surface.)
- Dataverse semantic model: still preview (upd 10-21); "no API" dropped from limits but none documented; **polymorphic still unsupported**; no ALM; now powers MDA Copilot, Copilot Chat, Copilot Studio Dataverse knowledge.
- **Fabric IQ "new ontology experience"** (still preview; overview 10-16, bind-data 10-20): inheritance, namespaces, shared properties, **named versions** (restore replaces), RDF/OWL import/export, NL rules, DAX metrics, multi-source bindings, "virtual layer" live query (materialisation opt-in), respects OneLake security + source RLS/OLS/CLS. Old experience retires 2027-01-31. REST still definition CRUD only; Update overrides; Decimal → double/string. **Dataverse shortcut unchanged** (read-only, fixed sysadmin credential). Bind-data still says manual refresh for new rows (contradicts "virtual layer").
- **Declarative agent manifest v1.9** (09-30, upd 10-22): `agent_skills` (SKILL.md folders, max 8) + **`Fabric` capability**. MCP plugins: dynamic tool discovery default, MCP Apps widgets, auth static OAuth / **DCR** / Entra SSO (09-30). `wiqd` CLI preview.
- **M365 Copilot in model-driven apps**: GA for MDA (canvas preview), read-only (upd 10-10); **custom agent can be the default agent**; `Xrm.Copilot` `sendPromptToM365Copilot`, `addActionHandler` (MCP Apps post back into the app), `updateContext` (preview). Agents can't yet use in-app user context (10-13). Licence: Power Apps Premium + M365 Copilot.
- Copilot Studio: Copilot Chat agents GA 10-26 (MC1518233); MCP Apps in agent chat GA planned Nov [Reported].
- Agent 365 registry for ISVs: no change.
- SPE agent / Retrieval `sharePointEmbedded`: still preview (SPE agent page 09-17); Retrieval API reference (08-31) **omits** `sharePointEmbedded`; SPE what's-new nothing after July.
- Release Planner retires 2026-11-15 (MC1461529) → track roadmap RM items.

## Judgment
- L1/L2: reword to "L1 *vocabulary* contested in the analytical plane (Fabric IQ); operational L1/L2 (bound, gated, cited) unclaimed".
- Fabric IQ = L5 still holds; disqualifiers are now no instance writes / no authority / no ledger / restore-overwrites / fixed-credential Dataverse path — not "thin model". DA 1.9 `Fabric` raises the value of an L5 projection.
- Spaarke MCP server: stronger (DA plugins w/ dynamic discovery, MDA default agent + MCP Apps, Copilot Studio).
