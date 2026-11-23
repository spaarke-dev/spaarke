# Dev-tenant Copilot inventory

> **Register of every Spaarke artifact in the dev tenant's Microsoft 365 Copilot surfaces** (Chat, Cowork, Word, Outlook, Home, Copilot Studio, Teams Developer Portal, plus the Entra apps that exist only for them).
> **Owner of this file:** copilot-platform-coordination. **First written:** 2026-11-23 from repository notes. **Not yet checked against the live tenant.** Rows marked ❓ need an owner screenshot (list at the end).
> **Rule (R4):** a new artifact gets a row here, with owner project and removal plan, **before** it is uploaded. When an artifact is removed, move its row to "Removed" with the date; never delete a row.
> **Who removes:** every tenant action (admin center, Cowork Customize, Copilot Studio, Teams Developer Portal, Entra) is done by **the owner**. The owning project's session writes the step-by-step instruction and records the result here. Claude sessions never click admin actions or handle secrets.

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
| D2 | **Spaarke NDA Review** | Copilot Studio / Agent Builder agent (route A); instructions v0.3 `NDAR-1112-A` (v0.2 `NDAR-1110-A` earlier) | spaarke-legal-ai-accelerator-r1 | Word NDA review test agent (DA-1 spike, 2026-11-09; rebuilt from recipe 2026-11-12) | **REPLACE** ❓ | Replaced by the four-tier package's packaged agent or the specialist NDA agent (specialist-legal-agents-r1 D-A11, undecided). Any v0.2 copy still present: REMOVE now | accelerator `notes/poc-e2e/results.md` (11-09), `tier-test-script.md` |
| D3 | **Spaarke Legal Hold Review** | As D2; `LHR-1112-A` (v0.2 `LHR-1110-A`) | spaarke-legal-ai-accelerator-r1 | Word legal-hold-notice review test agent | **REPLACE** ❓ | As D2 | as D2 |
| D4 | **Spaarke Legal Review** (DA) | Declarative-agent package zip `spaarke-legal-review-da.zip` (both skills; picks one from the document) | spaarke-legal-ai-accelerator-r1 | Front-door test agent (DA-1 spike, test B-1) | **DECIDE** ❓ | Built; install not recorded in the repo. If installed and the spike is over: REMOVE (owner) | accelerator `notes/poc-e2e/da-spike-script.md` |
| D5 | **Spaarke Dataverse MCP** — appears **twice** in All agents, plus Teams app `fc2412d8…` | Copilot Studio agent (Dataverse MCP), seen in Chat under `/` and `@` | **Unknown** (owner-built in Copilot Studio; no repo source) | Dataverse MCP agent; the accelerator plans to extend it | **DECIDE** ❓ | Owner to name its owner project. Remove the duplicate copy (owner) | accelerator `results.md` L27; mcp-r1 E1/E5 |
| D6 | **Copilot in Power Apps – Spaarke Platform [Draft]** | Agent created from Power Apps | Unknown | Probably auto-created by the Power Apps Copilot | **DECIDE** ❓ | Owner | accelerator `results.md` L27 |

## 4. Cowork plugins and skills

| # | Artifact | Kind | Owner project | Purpose | Status | Removal trigger / who | Evidence |
|---|---|---|---|---|---|---|---|
| S1 | `nda-review` | Cowork skill (in the accelerator plugin builds) | spaarke-legal-ai-accelerator-r1 | NDA review + redline (skills-first decision 2026-11-12) | **REPLACE** | Renamed `spaarke-nda-review` at the next plugin build (naming rule, agreed with mcp-r1 2026-11-06); owner removes the old-name copy then | mcp-r1 naming note §2 |
| S2 | `legal-hold-review` | Cowork skill | spaarke-legal-ai-accelerator-r1 | Legal hold notice review | **REPLACE** | → `spaarke-legal-hold-review`, as S1 | as S1 |
| S3 | `accelerator-setup` | Cowork skill | spaarke-legal-ai-accelerator-r1 | Onboarding / tailoring interview | **REPLACE** | → `spaarke-{area}-setup` per area, as S1 | as S1 |
| S4 | `nda-library-review` (16 files) | Cowork personal skill (OneDrive `Cowork/Skills/`) | spaarke-legal-ai-accelerator-r1 | Library review test | **DECIDE** | Accelerator to say whether it is still used; otherwise REMOVE (owner, Cowork › Customize) | accelerator `notes/poc-e2e/results.md` |
| S5 | `spaarke-visual-probe` (3 files) | Cowork personal skill | spaarke-legal-ai-accelerator-r1 | Throwaway rendering probe (ran 2026-11-05) | **REMOVE** | Was due after Act 3 step 1; not recorded as deleted. **Remove now** (owner, Cowork › Customize, and OneDrive `Cowork/Skills/spaarke-visual-probe`) | accelerator `runbook.md`, `results.md` |
| S6 | Current accelerator Cowork plugin upload(s) (four-tier package builds) | Cowork plugin, uploaded "Only you" | spaarke-legal-ai-accelerator-r1 | Owner's test install | ❓ | Accelerator to name what is installed now | accelerator `current-task.md` |

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
