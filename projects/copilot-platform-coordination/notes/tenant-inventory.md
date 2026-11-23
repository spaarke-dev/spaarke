# Dev-tenant Copilot inventory

> **Register of every Spaarke artifact in the dev tenant's Microsoft 365 Copilot surfaces** (Chat, Cowork, Word, Outlook, Home, Copilot Studio, Teams Developer Portal, plus the Entra apps that exist only for them).
> **Owner of this file:** copilot-platform-coordination. **First written:** 2026-11-23 from repository notes. **Not yet checked against the live tenant.** Rows marked ❓ need an owner screenshot (list at the end).
> **Rule (R4):** a new artifact gets a row here, with owner project and removal plan, **before** it is uploaded. When an artifact is removed, move its row to "Removed" with the date; never delete a row.
> **Who removes:** every tenant action (admin center, Cowork Customize, Copilot Studio, Teams Developer Portal, Entra) is done by **the owner**. The owning project's session writes the step-by-step instruction and records the result here. Claude sessions never click admin actions or handle secrets.

> **Removal list approved by the owner 2026-11-23.** Steps and trigger order: `removal-runbook.md`.

Status values: **KEEP** (stays, has an owner) · **REPLACE** (stays until a named successor ships, then removed) · **REMOVE** (to delete; date or trigger given) · **DECIDE** (owner decision needed first).

## 1. MCP connectors and plugin packages

| # | Artifact (as shown in tenant) | Kind | Owner project | Purpose | Status | Removal trigger / who | Evidence |
|---|---|---|---|---|---|---|---|
| C1 | **Spaarke MCP** | Federated Copilot connector (admin center › Copilot › Connectors), OAuth, URL `https://spaarke-bff-dev.azurewebsites.net/mcp/legal-operations` | spaarkeai-mcp-bff-api-r1 | The **single** tenant registration of the BFF MCP server (rule R1) | **KEEP** | — | mcp-r1 `notes/phase0-registration-facts.md` (linked 2026-11-10); T0/T5 |
| C2 | **Spaarke MCP spike** (connectors "Spaarke MCP spike A" → `/mcp/legal-operations`, "Spaarke MCP spike B" → `/mcp/spike-area-b`) | Plugin package (manifest 1.30, two `agentConnectors`), admin center › Agents › Tools › Registry, installed for All users | spaarkeai-mcp-bff-api-r1 (Phase 0 tests T2/T13/T14) | Throwaway test of package-delivered connectors | **REMOVE** | After mcp-r1 task 040 ships (and T10 is recorded). Owner deletes it in Registry. **Interim hazard:** connector A has the same URL as C1, so while both are connected every tool runs twice (T2, 2026-11-23) | mcp-r1 `notes/phase0-results.md` T2, T13, T14 |

## 2. OAuth and app registrations

| # | Artifact | Kind | Owner project | Purpose | Status | Removal trigger / who | Evidence |
|---|---|---|---|---|---|---|---|
| A1 | Teams Developer Portal OAuth client registration for Spaarke MCP (secret label "Teams Dev Portal OAuth – dev 2", valid to 2028-11-09) | Dev Portal › Tools › OAuth client registration ("Any Teams app") | spaarkeai-mcp-bff-api-r1 | Sign-in for C1; also referenced by C2 A and B (shared `referenceId`, one sign-in) | **KEEP** | — (secret lifecycle per root CLAUDE.md §9 / ADR-028) | mcp-r1 `phase0-registration-facts.md` (re-created 2026-11-10; first one deleted) |
| A2 | Entra app **Spaarke Copilot Connector – dev** | Entra app registration (ADR-028 A10 client) | spaarkeai-mcp-bff-api-r1 | OAuth client behind A1 | **KEEP** | — | mcp-r1 `phase0-registration-facts.md` (created 2026-11-08) |
| A3 | Entra app + Azure Bot `spaarke-bot-dev` + its bot OAuth connection | Bot Service and Entra app for the April "Spaarke AI" agent | ai-m365-copilot-integration (no live session) | Backend of D1 | **DECIDE** ❓ | Follows D1. If D1 is removed, the owner removes these after it; this project writes the steps | master `projects/ai-m365-copilot-integration/current-task.md` |

## 3. Declarative agents and Copilot Studio agents

| # | Artifact | Kind | Owner project | Purpose | Status | Removal trigger / who | Evidence |
|---|---|---|---|---|---|---|---|
| D1 | **Spaarke AI** — two registrations (v1.1.0 installed; v1.5.0 uploaded, not installed) | Teams app with a declarative agent + API plugin (`src/solutions/CopilotAgent/appPackage/`) | ai-m365-copilot-integration | The April agent; the naming note keeps "Spaarke AI" as the @-mentionable name for a fallback agent. mcp-r1 cancelled the fallback (043) because Chat renders the widget (T1) | **DECIDE** ❓ | At minimum remove the stale duplicate registration (owner; screenshot needed to pick which). Whether the agent itself stays is an owner decision | accelerator `notes/mvp-nda/results.md` L27 (2026-11-02) |
| D2 | **Spaarke NDA Review** | Copilot Studio agent (DA spike route A), built by the owner 2026-11-09. Instruction version **unconfirmed**: the owner was asked 2026-11-12 to paste v0.3 (`NDAR-1112-A`); it may still carry v0.2 or the spike's "-A" text | spaarke-legal-ai-accelerator-r1 | Word NDA review test agent | **REPLACE** ❓ | Replaced when the area/packaged agent ships (four-tier package or specialist NDA agent; specialist-legal-agents-r1 D-A11, undecided). Owner removes it in Copilot Studio then | accelerator `notes/poc-e2e/results.md` (11-09); acclerator-r1 reply 2026-11-23 |
| D3 | **Spaarke Legal Hold Review** | As D2 (`LHR-1112-A` requested, unconfirmed) | spaarke-legal-ai-accelerator-r1 | Word legal-hold-notice review test agent | **REPLACE** ❓ | As D2 | as D2 |
| D4 | **Spaarke Legal Review** | Copilot Studio agent (route A), the third agent the owner built 2026-11-09. **Not** the package `spaarke-legal-review-da.zip`, which was built into OneDrive `POC-DA1/packages/` but has no recorded upload | spaarke-legal-ai-accelerator-r1 | Front-door test agent (picks NDA or legal hold from the document) | **REPLACE** ❓ | As D2 | accelerator `notes/poc-e2e/da-spike-script.md`; acclerator-r1 reply 2026-11-23 |
| D5 | **Spaarke Dataverse MCP** — appears **twice** in All agents, plus Teams app `fc2412d8…` | Copilot Studio agent (Dataverse MCP), seen in Chat under `/` and `@` | **Unknown** (owner-built in Copilot Studio; no repo source) | Dataverse MCP agent; the accelerator plans to extend it | **DECIDE** ❓ | Owner to name its owner project. Remove the duplicate copy (owner) | accelerator `results.md` L27; mcp-r1 E1/E5 |
| D6 | **Copilot in Power Apps – Spaarke Platform [Draft]** | Agent created from Power Apps | Unknown | Probably auto-created by the Power Apps Copilot | **DECIDE** ❓ | Owner | accelerator `results.md` L27 |

**Planned — R4 rows received 2026-11-23, not yet created** (specialist-legal-agents-r1 Phase 0 test G8 = structure test S-3; owner approved running G8 early, per specialist-legal-agents-r1; kit `projects/specialist-legal-agents-r1/phase0/g8/README.md` on `work/specialist-legal-agents-r1`, `5595b4ab8f`):

| # | Artifact | Kind | Owner project | Purpose | Status | Removal trigger / who | Evidence |
|---|---|---|---|---|---|---|---|
| G8-a | `spaarke-legal-g8-ab-test` | Agent Builder declarative agent, private to its creator, no actions. Knowledge: none (A1 control), then "Spaarke MCP" under Copilot connectors if the picker offers it (A2) | specialist-legal-agents-r1 | G8 / S-3: can an agent use the tenant connector without its own registration? | **PLANNED → REMOVE** | Owner deletes it in Agent Builder the day G8 is recorded, **latest 2026-11-30** | specialist row 2026-11-23 |
| G8-p | `spaarke-legal-g8-pkg-test` | App package (app id `ac7da782-71bc-4c13-ba90-44eea8ef48b8`), one declarative agent (v1.6) with the GraphConnectors capability; **no `agentConnectors`, no actions** (no second registration, R1) | specialist-legal-agents-r1 | as G8-a | **PLANNED → REMOVE** | Owner removes it from where it was uploaded, same trigger, latest 2026-11-30. Upload scope per R7: owner + test user only | specialist row 2026-11-23 |

While C2 is still installed, any BFF call in G8 is recorded as "tenant registration, C1 probable" (C2's package connectors fail in Chat before reaching the BFF and are not agent actions).

## 4. Cowork plugins and skills

> S1–S5 are **per-user skill folders** in the owner's OneDrive `Documents/Cowork/Skills/` (package v0.1, since 2026-11-10), not tenant uploads; Cowork loads them and lists them under Plugins › Added. Only the owner's account sees them. Whether the owner re-extracted the latest zip is unconfirmed. No accelerator plugin has been uploaded since X1 (acclerator-r1 reply 2026-11-23).

| # | Artifact | Kind | Owner project | Purpose | Status | Removal trigger / who | Evidence |
|---|---|---|---|---|---|---|---|
| S1 | `nda-review` | Cowork skill folder (OneDrive) | spaarke-legal-ai-accelerator-r1 | NDA review + redline (skills-first decision 2026-11-12) | **KEEP; rename pending owner** | The name fails the `^spaarke(-…)+$` gate. Names agreed between sessions 2026-11-06 (accelerator OD-42: plugin `spaarke-nda` "Spaarke NDA"; skills `spaarke-nda-review`, `spaarke-nda-setup`, `spaarke-nda-library-review`); **owner decision pending**; rename at the first build after OD-20 (one plugin vs K+E split), which waits on the Word probe WP-2. After the rename the owner deletes the old-name folder | mcp-r1 naming note §2; acclerator-r1 reply 2026-11-23 |
| S2 | `legal-hold-review` | as S1 | spaarke-legal-ai-accelerator-r1 | Legal hold notice review | **KEEP; rename pending owner** | as S1 | as S1 |
| S3 | `accelerator-setup` | as S1 | spaarke-legal-ai-accelerator-r1 | Onboarding / tailoring interview | **KEEP; rename pending owner** | as S1 | as S1 |
| S4 | `nda-library-review` (16 files) | as S1 | spaarke-legal-ai-accelerator-r1 | NDA portfolio review against the playbook (a Spaarke-owned function, in the package) | **KEEP; rename pending owner** | as S1 | acclerator-r1 reply 2026-11-23 |
| S5 | `spaarke-visual-probe` (3 files) | Cowork skill folder (OneDrive) | spaarke-legal-ai-accelerator-r1 | Throwaway rendering probe (POC T-P13, ran 2026-11-05) | **REMOVE now** | Was due after Act 3 step 1; never deleted. Owner deletes OneDrive `Documents/Cowork/Skills/spaarke-visual-probe` | accelerator `runbook.md`, `results.md`; acclerator-r1 agrees |
| P1 | **Spaarke Word Probe** plugin (`Spaarke Word probe plugin v0.1.zip`; test skills `spaarke-word-probe`, `spaarke-word-probe-script`; no legal content, no connector) | Cowork plugin, upload "Only you" (owner); optional WP-2c admin deploy scoped to the owner only | spaarke-legal-ai-accelerator-r1 (tests WP-0..WP-2) | Throwaway Word plugin probe | **PLANNED → REMOVE same day** | Built 2026-11-12, sent to owner, upload not recorded. Owner removes it (Cowork › Customize › Plugins › Remove) right after WP-0..WP-2, same day | acclerator-r1 row, 2026-11-23 (first R4 row) |

Planned later (rows will come before upload): the accelerator's four-tier team tiers — shared Cowork plugin, shared Agent Builder agent, admin-deployed plugin (I-1/T-14).

## 5. Adjacent Spaarke Teams apps (not Copilot artifacts; listed so nobody removes them by mistake)

Spaarke External Workspace bot (×2), Spaarke save/share and portal Teams apps, portal app `5d88b948…` (Blocked, submitted 2026-10-29). These belong to the external-access and Teams projects. They are **out of scope** for this tidy-up unless the owner says otherwise.

## Removed

| # | Artifact | Owner project | Removed on | By | Note |
|---|---|---|---|---|---|
| X1 | Spaarke NDA (MVP) plugin `spaarke-nda-mvp` 0.1.x | spaarke-legal-ai-accelerator-r1 | before Act 3 (2026-11) | owner | Recorded as deleted in accelerator `notes/poc-e2e/results.md`; ❓ confirm it is gone |
| X2 | First Teams Dev Portal OAuth registration for Spaarke MCP | spaarkeai-mcp-bff-api-r1 | 2026-11-10 | owner | Replaced by A1 |

## Screenshots needed from the owner (to confirm or correct this file)

1. Admin center › **Agents › All agents** (filter "Spaarke"): D1–D6.
2. Admin center › **Agents › Tools › Registry**: C2 and any other package.
3. Admin center › **Copilot › Connectors › Your connections**: C1, and whether a package also appears there.
4. Cowork › **Customize › Skills & Plugins** (owner account, and the `demo@` test user): S1–S6.
5. **Copilot Studio** agent list (dev environment): D2, D3, D5.
6. **Teams Developer Portal › Tools › OAuth client registrations** (names only, no secrets): A1.
7. **Entra › App registrations**, search "Spaarke Copilot" and "bot": A2, A3.
