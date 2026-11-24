# Dev-tenant Copilot inventory

> **Register of every Spaarke artifact in the dev tenant's Microsoft 365 Copilot surfaces** (Chat, Cowork, Word, Outlook, Home, Copilot Studio, Teams Developer Portal, plus the Entra apps that exist only for them).
> **Owner of this file:** copilot-platform-coordination. **First written:** 2026-11-23 from repository notes. **Partly checked against the live tenant 2026-11-23** (owner screenshots: agents, Copilot Studio, Plugins page, sources/skills list). Rows marked ❓ still need an owner screenshot (list at the end).
> **Rule (R4):** a new artifact gets a row here, with owner project and removal plan, **before** it is uploaded. When an artifact is removed, move its row to "Removed" with the date; never delete a row.
> **Who removes:** every tenant action (admin center, Cowork Customize, Copilot Studio, Teams Developer Portal, Entra) is done by **the owner**. The owning project's session writes the step-by-step instruction and records the result here. Claude sessions never click admin actions or handle secrets.

> **Removal list approved by the owner 2026-11-23.** Steps and trigger order: `removal-runbook.md`.

Status values: **KEEP** (stays, has an owner) · **REPLACE** (stays until a named successor ships, then removed) · **REMOVE** (to delete; date or trigger given) · **DECIDE** (owner decision needed first).

## 1. MCP connectors and plugin packages

| # | Artifact (as shown in tenant) | Kind | Owner project | Purpose | Status | Removal trigger / who | Evidence |
|---|---|---|---|---|---|---|---|
| C1 | **Spaarke MCP** | Federated Copilot connector (admin center › Copilot › Connectors), OAuth, URL `https://spaarke-bff-dev.azurewebsites.net/mcp/legal-operations` | spaarkeai-mcp-bff-api-r1 | The **single** tenant registration of the BFF MCP server (rule R1) | **KEEP** | — | mcp-r1 `notes/phase0-registration-facts.md` (linked 2026-11-10); T0/T5 |
| C3 | **Spaarke** (slug `spaarke`), plugin package v1.0.0, manifest 1.30, app id `68427ffa-ce23-4bed-9cdf-f1d6ed16eec4` | Plugin package with **one skill, `spaarke-decisions`; no `agentConnectors`, no agents**. Uses C1 for `spaarke_show_decisions`. Install: owner + test user only (R7), admin center › Agents › Tools › Registry | spaarkeai-mcp-bff-api-r1 (task 040; source `src/solutions/CopilotPlugins/spaarke/`, PR #3025 draft) | The product package. First install runs T10 (= S-2: can a packaged skill call a tool from a connector registered elsewhere? decides R2) and S-1 in the same session | **KEEP** — uploaded to Registry by the owner 2026-11-24 ~03:15Z (ship date). ❓ install scope (R7: owner + test user) not yet confirmed | — | mcp-r1 R4 row 2026-11-23/24 |

## 2. OAuth and app registrations

| # | Artifact | Kind | Owner project | Purpose | Status | Removal trigger / who | Evidence |
|---|---|---|---|---|---|---|---|
| A1 | Teams Developer Portal OAuth client registration for Spaarke MCP (secret label "Teams Dev Portal OAuth – dev 2", valid to 2028-11-09) | Dev Portal › Tools › OAuth client registration ("Any Teams app") | spaarkeai-mcp-bff-api-r1 | Sign-in for C1; also referenced by C2 A and B (shared `referenceId`, one sign-in) | **KEEP** | — (secret lifecycle per root CLAUDE.md §9 / ADR-028) | mcp-r1 `phase0-registration-facts.md` (re-created 2026-11-10; first one deleted) |
| A2 | Entra app **Spaarke Copilot Connector – dev** | Entra app registration (ADR-028 A10 client) | spaarkeai-mcp-bff-api-r1 | OAuth client behind A1 | **KEEP** | — | mcp-r1 `phase0-registration-facts.md` (created 2026-11-08) |
| A3 | Entra app + Azure Bot `spaarke-bot-dev` + its bot OAuth connection | Bot Service and Entra app for the April "Spaarke AI" agent | ai-m365-copilot-integration (no live session) | Backend of D1 | **DECIDE** ❓ | Follows D1. If D1 is removed, the owner removes these after it; this project writes the steps | master `projects/ai-m365-copilot-integration/current-task.md` |

## 3. Declarative agents and Copilot Studio agents

| # | Artifact | Kind | Owner project | Purpose | Status | Removal trigger / who | Evidence |
|---|---|---|---|---|---|---|---|
| D1 | **Spaarke AI** — **two registrations confirmed** (Registry, last updated 2026-10-28 and 2026-10-08; platform blank; not shared). The Copilot Plugins page lists only one. Earlier note (2026-11-02): v1.1.0 installed, v1.5.0 uploaded but not installed. Which row is which: ❓ | Teams app with a declarative agent + API plugin (`src/solutions/CopilotAgent/appPackage/`, master manifest still `1.1.0`) | ai-m365-copilot-integration (provisioning-r1 confirmed 2026-11-23 that neither row is theirs: task 257 uploads only to customer tenants, none done yet) | The April agent; the naming note keeps "Spaarke AI" as the @-mentionable name for a fallback agent. mcp-r1 cancelled the fallback (043) because Chat renders the widget (T1) | **DECIDE** | Proposal (specialist design v0.6 §3.12, `fc0c62c257`): reuse its harness, retire the registration once MCP has parity. Remove the stale registration (owner) once each row's details pane shows version and install state. Whether the agent itself stays is an owner decision | Registry screenshot 2026-11-23; accelerator `notes/mvp-nda/results.md` L27 |
| D2 | **Spaarke NDA Review** | **Agent Builder** agent (Registry platform "Agent Builder in Microsoft 365 Copilot"; not in Copilot Studio), last updated 2026-11-09, **shared with All users** (breaks R7). Instruction version unconfirmed (v0.3 `NDAR-1112-A` requested 2026-11-12) | spaarke-legal-ai-accelerator-r1 | Word NDA review test agent | **REPLACE** | Replaced when the area/packaged agent ships (specialist-legal-agents-r1 D-A11). Owner deletes it from the Registry then (Agent Builder agents: the owner can delete there). Interim: restrict sharing to owner + test user (owner's call) | Registry + Plugins screenshots 2026-11-23 |
| D3 | **Spaarke Legal Hold Review** | As D2 (Agent Builder, All users, 2026-11-09; `LHR-1112-A` requested, unconfirmed) | spaarke-legal-ai-accelerator-r1 | Word legal-hold-notice review test agent | **REPLACE** | As D2 | as D2 |
| D4 | **Spaarke Legal Review** | As D2 (Agent Builder, All users, 2026-11-09). **Not** the package `spaarke-legal-review-da.zip`, which was never uploaded | spaarke-legal-ai-accelerator-r1 | Front-door test agent (picks NDA or legal hold from the document) | **REPLACE** | As D2 | as D2 |
| D5 | **Spaarke Dataverse MCP** — **one copy only** (Copilot Studio, Published, modified 2026-05-15; Registry 2026-07-25; not shared). The 2026-11-02 "twice" is no longer true. Teams app `fc2412d8…` not checked | Copilot Studio agent (Dataverse MCP), seen in Chat under `/` and `@` | **Unknown** — owner-built in Copilot Studio; no repo source | Dataverse MCP agent; the accelerator plans to extend it | **KEEP pending owner** | No duplicate to remove. Owner to name its owner project | Studio + Registry screenshots 2026-11-23 |
| D6 | **Copilot in Power Apps – Spaarke Platform** (Draft) | Copilot Studio draft (modified 2026-05-21; Registry 2026-07-21; not shared) | Unknown — most likely auto-created when Copilot was opened in a Power Apps app | Probably none | **DECIDE** | Owner: delete in Copilot Studio if nobody uses it | Studio + Registry screenshots 2026-11-23 |
| D7 | **Copilot in Power Apps – Corporate Counsel** (Draft) | Copilot Studio draft (modified 2026-04-28); not in the Registry "spaarke" search | Unknown — as D6 | Probably none | **DECIDE** | As D6 | Studio screenshot 2026-11-23 |

**G8 test agents — R4 rows received 2026-11-23; G8-a created and run ~20:45Z; G8 final 2026-11-23; G8-p dropped, never uploaded** (specialist-legal-agents-r1 Phase 0 test G8 = structure test S-3; owner approved running G8 early, per specialist-legal-agents-r1; kit `projects/specialist-legal-agents-r1/phase0/g8/README.md` on `work/specialist-legal-agents-r1`, `5595b4ab8f`):

| # | Artifact | Kind | Owner project | Purpose | Status | Removal trigger / who | Evidence |
|---|---|---|---|---|---|---|---|
| G8-a | `spaarke-legal-g8-ab-test` | Agent Builder declarative agent, private to its creator, no actions. Knowledge: none (A1 control), then "Spaarke MCP" under Copilot connectors if the picker offers it (A2) | specialist-legal-agents-r1 | G8 / S-3: can an agent use the tenant connector without its own registration? | **CREATED → REMOVE** | Contains two owner-added test skills (`spaarke-mcp-operations`, `spaarke-tool-test`) that go with it. G8 is final; owner deletes it, **latest 2026-11-30** | specialist row + interim result 2026-11-23 (commit `9c557d7e95`) |
| G8-p | `spaarke-legal-g8-pkg-test` | App package (app id `ac7da782-71bc-4c13-ba90-44eea8ef48b8`), one declarative agent (v1.6) with the GraphConnectors capability; **no `agentConnectors`, no actions** (no second registration, R1) | specialist-legal-agents-r1 | as G8-a | **DROPPED** (never uploaded) | Nothing to remove: P1 was dropped when G8 became final (specialist, 2026-11-23) | specialist row 2026-11-23 |
| G8-p2 | `spaarke-legal-g8p2-test` (app id `802fd9cc-feae-425a-aaef-8e97aba87ff5`) | App package: one declarative agent (v1.6) with **one private action**, MCP plugin v2.4, RemoteMCPServer `https://spaarke-bff-dev.azurewebsites.net/mcp/legal-operations`, OAuthPluginVault = A1's existing reference (no new registration), dynamic tool discovery. Install for owner + test user only (R7) | specialist-legal-agents-r1 | Phase 0 G8-P2: does an agent with its own MCP action work in Chat/Word/Teams, and does each call run once alongside C1? Decides R1′. Check: plain-Chat `spaarke_show_decisions` call count before and after upload must match (runbook `b644fc3154`; widget decisions and approval cards are cancelled during the test) | **PLANNED → REMOVE** (built, not uploaded; owner said "Yes, prep G8-P2", per specialist 2026-11-23) | Owner removes it where uploaded the day results are recorded; **latest 2026-11-30** | specialist R4 row 2026-11-23; runbook `phase0/g8/README.md` "G8-P2" |

While C2 is still installed, any BFF call in G8 is recorded as "tenant registration, C1 probable" (C2's package connectors fail in Chat before reaching the BFF and are not agent actions).

## 4. Cowork plugins and skills

> S1–S5 are **per-user skill folders** in the owner's OneDrive `Documents/Cowork/Skills/` (package v0.1, since 2026-11-10), not tenant uploads; Cowork loads them and lists them under Plugins › Added. Only the owner's account sees them. Whether the owner re-extracted the latest zip is unconfirmed. No accelerator plugin has been uploaded since X1 (acclerator-r1 reply 2026-11-23).

| # | Artifact | Kind | Owner project | Purpose | Status | Removal trigger / who | Evidence |
|---|---|---|---|---|---|---|---|
| S1 | `nda-review` | Cowork skill folder (OneDrive) | spaarke-legal-ai-accelerator-r1 | NDA review + redline (skills-first decision 2026-11-12) | **KEEP; rename pending owner** | The name fails the `^spaarke(-…)+$` gate. Names agreed between sessions 2026-11-06 (accelerator OD-42: plugin `spaarke-nda` "Spaarke NDA"; skills `spaarke-nda-review`, `spaarke-nda-setup`, `spaarke-nda-library-review`); **owner decision pending**; rename at the first build after OD-20 (one plugin vs K+E split), which waits on the Word probe WP-2. After the rename the owner deletes the old-name folder | mcp-r1 naming note §2; acclerator-r1 reply 2026-11-23 |
| S2 | `legal-hold-review` | as S1 | spaarke-legal-ai-accelerator-r1 | Legal hold notice review | **KEEP; rename pending owner** | as S1 | as S1 |
| S3 | `accelerator-setup` | as S1 | spaarke-legal-ai-accelerator-r1 | Onboarding / tailoring interview | **KEEP; rename pending owner** | as S1 | as S1 |
| S4 | `nda-library-review` (16 files) | as S1 | spaarke-legal-ai-accelerator-r1 | NDA portfolio review against the playbook (a Spaarke-owned function, in the package) | **KEEP; rename pending owner** | as S1. Present: Cowork listed "NDA library review" on 2026-11-24 (mcp-r1 S-1 run) | acclerator-r1 reply 2026-11-23 |
| S5 | `spaarke-visual-probe` (3 files) | Cowork skill folder (OneDrive) | spaarke-legal-ai-accelerator-r1 | Throwaway rendering probe (POC T-P13, ran 2026-11-05) | **REMOVE now** | Was due after Act 3 step 1; never deleted. Owner deletes OneDrive `Documents/Cowork/Skills/spaarke-visual-probe` | accelerator `runbook.md`, `results.md`; acclerator-r1 agrees |
| P1 | **Spaarke Word Probe** plugin (`Spaarke Word probe plugin v0.1.zip`; test skills `spaarke-word-probe`, `spaarke-word-probe-script`; no legal content, no connector) | Cowork plugin, upload "Only you" (owner); optional WP-2c admin deploy scoped to the owner only | spaarke-legal-ai-accelerator-r1 (tests WP-0..WP-2) | Throwaway Word plugin probe | **PLANNED → REMOVE same day** | Built 2026-11-12, sent to owner, upload not recorded. Owner removes it (Cowork › Customize › Plugins › Remove) right after WP-0..WP-2, same day | acclerator-r1 row, 2026-11-23 (first R4 row) |

Planned later (rows will come before upload): the accelerator's four-tier team tiers — shared Cowork plugin, shared Agent Builder agent, admin-deployed plugin (I-1/T-14).

## 5. Adjacent Spaarke Teams apps (not Copilot artifacts; listed so nobody removes them by mistake)

Copilot Studio (2026-11-23 screenshot) holds three bots: **Spaarke External Workspace bot** (Published, modified 2026-07-15), **External Workspace bot** (Published, 2026-05-30) and a second **Spaarke External Workspace bot** (Published, 2026-05-11); the Registry lists two "Spaarke External Workspace bot" rows, not shared. Two same-named published bots look like a duplicate; flagged for the external-access project, not removed here. Also: Spaarke save/share and portal Teams apps, portal app `5d88b948…` (Blocked, submitted 2026-10-29). These belong to the external-access and Teams projects and are **out of scope** for this tidy-up unless the owner says otherwise.

## Removed

| # | Artifact | Owner project | Removed on | By | Note |
|---|---|---|---|---|---|
| X1 | Spaarke NDA (MVP) plugin `spaarke-nda-mvp` 0.1.x | spaarke-legal-ai-accelerator-r1 | before Act 3 (2026-11) | owner | Recorded as deleted in accelerator `notes/poc-e2e/results.md`; ❓ confirm it is gone |
| X2 | First Teams Dev Portal OAuth registration for Spaarke MCP | spaarkeai-mcp-bff-api-r1 | 2026-11-10 | owner | Replaced by A1 |
| X3 | **Spaarke MCP spike** plugin (connectors A `/mcp/legal-operations`, B `/mcp/spike-area-b`) | spaarkeai-mcp-bff-api-r1 | 2026-11-24 ~03:15Z | owner | Deleted after S-1; per mcp-r1. Phase 0 tests T2/T13/T14/S-1 done. ❓ confirm Registry no longer lists "Spaarke MCP spike A/B" as MCP servers |

## Screenshots needed from the owner (to confirm or correct this file)

Received 2026-11-23: All agents › Registry ("spaarke", 9 rows), Copilot Studio agents (6), Copilot Plugins page ("spaarke", Chat tab), the sources/skills list ("spaarke": C1 on, C2 off, S1–S3, S5). Still needed:

1. ~~All agents~~ ✅ — plus the **details pane of each of the two "Spaarke AI" rows** (version, app id, installed or not) for D1.
2. Admin center › **Agents › Tools › Registry**: C2 and any other package.
3. Admin center › **Copilot › Connectors › Your connections**: C1, and whether a package also appears there.
4. The same skills list for the `demo@` test user, and a search for "nda-library" (S4).
5. ~~Copilot Studio~~ ✅
6. **Teams Developer Portal › Tools › OAuth client registrations** (names only, no secrets): A1.
7. **Entra › App registrations**, search "Spaarke Copilot" and "bot": A2, A3.
