# Phase 0 test G2 remainder: packaging questions

> **Program:** specialist-legal-agents-r1 (design.md §8 G2 and G7; spec FR-08, FR-01).
> **Status:** kit written, not run. Nothing here is uploaded, installed or changed in the tenant.
> **Nature:** informational. It decides packaging, not the go/no-go. Each question is answered **yes, no or untestable**, with evidence (FR-08).
> **Who does what:** the owner uploads, installs and removes packages and sends prompts; this session builds the package files and reads App Insights. Claude sessions never click tenant or admin actions.
> **Content rule:** all test content is synthetic (NFR-02). Nothing in a package may name or quote the accelerator's reference material or any real agreement. Check each file with the accelerator's `FORBIDDEN_IN_PACKAGE` pattern (read only) before it goes in a package.

## What is already known

- **T2/T14 (2026-11-23):** a package with two **connectors** and one OAuth registration works in **Cowork**, and one sign-in connects both. In **Chat**, calls through the package connector failed before reaching the BFF.
- **G8-P2 (2026-11-24):** a coded declarative agent with its own MCP action works in Chat, Word and Teams, one call per turn. Build rules BR-1 to BR-7 in `notes/agent-package-identity-and-distribution.md` apply to everything below.
- **Still untested:** a declarative agent and a `SKILL.md` in one package; two agents in one package; a practice plugin depending on a core plugin.

## The three questions

| # | Question (FR-08) | Result (yes / no / untestable) | Evidence |
|---|---|---|---|
| **Q1** | Using the G5 test package, add one `SKILL.md`: do the agent and the skill both work in Chat? | | |
| **Q2** | Can one package hold two agents? | | |
| **Q3** | Can a practice plugin depend on the core plugin? | | |

Fill in the table above at the end. Each question has its own method and evidence sheet below.

## Common rules for every package in this test

- **Fresh app id** for each package, and for each action-affecting change (BR-2). A package that adds a skill or a second agent is treated as an action change until shown otherwise. After any update wait at least an hour before judging it (BR-3).
- **Dynamic discovery** and the A1 `reference_id` on the one MCP action; no query string in the URL (BR-5, H2).
- **Org catalog, owner and test user only** (R7). Never All users.
- **R4 inventory row** sent to COPILOT-PLATFORM-COORDINATION (Issue #3006) before each upload. Rows are in "Inventory rows" below; "sent" cells are blank until sent.
- **Evidence is server-side** (BR-7): one `tools/call` per turn in App Insights from the Chat client, using the PowerShell query in `phase0/g8/README.md`. The model's own account is not evidence.
- **Schema first.** Before building or uploading, check the layout against the published manifest and plugin schemas (the same way the G8-P2 manifests were validated). If a schema alone answers a question ("not expressible"), record **no** with the schema reference; an upload is not needed.

## Q1: agent and `SKILL.md` in one package

**Why it matters.** The design wants each procedure written once and shipped both as an agent component and as a Cowork skill (G2, T10).

**Setup**
1. Copy the G5 main test package (`phase0/g5/README.md`, "Main test") to a new folder under `phase0/g2/package-q1/` with a **fresh app id**, name and short name.
2. Add **one** `SKILL.md`, placed where the plugin format requires it. Take the layout from mcp-r1's plugin package (task 040, the `/spaarke-decisions` skill) and from Learn; do not invent a path. Record the path used.
3. The skill is synthetic and self-identifying: its body says "When asked for the G2 marker, reply with the exact text G2-SKILL-OK and call no tool". Name it so that it cannot be confused with a function (for example `g2-marker`).
4. Zip the package files at the zip root.

**Built (task 020, 2026-11-26; not uploaded).** The package is `package/` (the task's output path; the setup step above says `package-q1/`), zip `spaarke-legal-g2-q1-1.0.0.zip` beside this file (gitignored).

| Item | Value |
|---|---|
| App id | `7bcab05e-3d88-4006-96c9-545edfc5a6ee`, version 1.0.0, short name `spaarke-legal-g2-q1-test`, namespace `spaarkeg2q1` |
| Built from | the G5 package (`../g5/package/`): same MCP URL, A1 `reference_id`, dynamic discovery, agent v1.6, plugin v2.4, no query string |
| Manifest | **1.30**, not 1.25: `agentSkills` exists only from 1.30 (checked against the published 1.25 and 1.30 schemas). The manifest holds both `copilotAgents.declarativeAgents` (one agent) and `agentSkills` (`skills/spaarke-g2-marker`). That is the one structural difference from the G5 package besides the instructions |
| Skill | `skills/spaarke-g2-marker/SKILL.md`, name `spaarke-g2-marker` (the `spaarke-` prefix follows mcp-r1's naming gate; the kit's `g2-marker` was an example). Path and layout copied from mcp-r1's plugin package (`src/solutions/CopilotPlugins/spaarke/`: `agentSkills: [{ "folder": "skills/<name>" }]`, one `SKILL.md` per folder). Body: "When asked for the G2 marker, reply with the exact text G2-SKILL-OK and call no tool." |
| Instructions | 1,893 characters: the G5 text plus two packaging-test sentences, "If the user asks for the G2 marker, use the skill spaarke-g2-marker and call no tool" and "If the user asks you to call the first read tool the server lists, call that one tool once and show its result". Without them the G5 line "call no tool for anything else" would make Q1-2 and Q1-3 fail for the wrong reason |
| Tools it will see | the same as the G5 agent: today `spike_get_profile`, `spike_review_nda`, `spaarke_show_decisions`. "The first read tool" is whichever the server lists first; Q1-2 passes on any single `tools/call` |
| Forbidden-pattern check | 0 hits in the folder and in the zip |
| Upload | The owner's, per `../g1-nda/packages/UPLOAD.md` row 5, after the R4 row is sent |

**Test script (Chat; each prompt in a new chat)**

| # | Prompt | Where | What it shows |
|---|---|---|---|
| Q1-1 | List every tool and source you can use in this conversation | agent selected | The action's tools are loaded (the agent works) |
| Q1-2 | Call the first read tool the server lists | agent selected | Agent action works: exactly one `tools/call` |
| Q1-3 | Give me the G2 marker | agent selected | Skill is available to the agent: reply contains `G2-SKILL-OK`, no `tools/call` |
| Q1-4 | Give me the G2 marker | plain Chat, no agent | Whether the skill is reachable without the agent |
| Q1-5 | Call the first read tool the server lists | plain Chat, no agent | No leak: the agent's action is not pulled into plain Chat (R1′), same call count as before the upload |

**Decide**
- **Yes:** Q1-2 shows one call, and Q1-3 returns the marker (in the agent, or in plain Chat if that is how the format delivers skills; record which).
- **No:** the package installs but the skill never loads, or the upload is refused for having both.
- **Untestable:** the package cannot be installed for a reason outside this question (for example the plugin route is blocked); say why.

**Evidence sheet**

| Prompt | Time (UTC) | Surface | Observed | `tools/call` count (App Insights) | Pass / fail | Evidence ref |
|---|---|---|---|---|---|---|
| Q1-1 | | Chat | | | | |
| Q1-2 | | Chat | | | | |
| Q1-3 | | Chat | | | | |
| Q1-4 | | Chat | | | | |
| Q1-5 | | Chat | | | | |

Also record (design G7, optional here): whether the skill appears in Cowork, and whether a skill that names a connector tool (T10) runs. These are noted, not scored.

## Q2: two agents in one package

**Why it matters.** It decides whether a practice area ships as one package with several role agents, or as one package per agent.

**Method**
1. **Schema check (first, no upload).** In the published Teams app manifest schema, read the `copilotAgents.declarativeAgents` definition: is it an array, and what is its maximum item count at the manifest version we use? Record the schema URL and the answer.
   - If the schema allows only one agent, the answer is **no**, with the schema reference. Stop.
2. **Build (only if the schema allows more than one).** Folder `phase0/g2/package-q2/`, fresh app id: two declarative agent files (`agent-a`, `agent-b`) with different names and different one-line instructions, each with its **own** action file (own namespace, same URL, same `reference_id`), each with a distinct marker in its instructions ("agent A answers with G2-A", "agent B answers with G2-B").
3. **Test (Chat, new chat each):**

| # | Prompt | Shows |
|---|---|---|
| Q2-1 | Open the agent picker: are both agents listed from this one package? (screenshot) | Both agents are offered |
| Q2-2 | Select agent A, send: Which agent are you? | A answers with its marker |
| Q2-3 | Select agent B, send: Which agent are you? | B answers with its marker |
| Q2-4 | Agent A: call the first read tool the server lists | One `tools/call` (A's action works) |
| Q2-5 | Agent B: call the first read tool the server lists | One `tools/call` (B's action works), none doubled |

**Decide**
- **Yes:** both agents are listed and answer separately, and each action works with one call per turn.
- **No:** the schema allows one agent, the upload is refused, or only one agent appears.
- **Untestable:** neither schema nor upload settles it (state why).

**Evidence sheet**

| Step | Time (UTC) | Surface | Observed | `tools/call` count | Pass / fail | Evidence ref (schema URL, screenshot, query) |
|---|---|---|---|---|---|---|
| Schema check | n.a. | n.a. | | n.a. | | |
| Q2-1 | | Chat | | n.a. | | |
| Q2-2 | | Chat | | n.a. | | |
| Q2-3 | | Chat | | n.a. | | |
| Q2-4 | | Chat | | | | |
| Q2-5 | | Chat | | | | |

**Desk finding (task 020, 2026-11-26): the schema check is done; task 023 confirms it into the table.** The published Teams app manifest schema `https://developer.microsoft.com/json-schemas/teams/v1.30/MicrosoftTeams.schema.json` (the latest version published; v1.31, v1.32 and devPreview do not resolve) defines `copilotAgents.declarativeAgents` as an array with `minItems: 1` and `maxItems: 1`, and its description reads "Currently, only one declarative agent per application is supported." So one package cannot hold two agents at the manifest version we use: Q2 is **no**, by the schema, and no upload is needed. Task 020 therefore built **no** Q2 package (row G2-q2 below: DROPPED, never uploaded, proposed; task 023 confirms).

## Q3: can a practice plugin depend on the core plugin?

**Why it matters.** It decides whether the "core plus practice plugins" shape (design §3.12) can ship the shared pieces once, or has to repeat them in every practice package.

**Method**
1. **Schema and docs check (first, no upload).** Search the published plugin and app manifest schemas and Learn for a dependency mechanism: a `dependencies` or `requires` field, an install-order rule, or a store-level "depends on" relation. Record the schema URLs read and the exact field, or "none found".
   - No mechanism found: the answer is **no** (not expressible), with the references. Say whether sequential install is the only workaround.
2. **Behaviour test (only if a mechanism exists, or to test the sequential workaround).** Two small packages, fresh app ids, same URL and `reference_id`, no new registration (R1):
   - **core:** the action plus one marker skill (`g2-core-marker`, replies `G2-CORE-OK`);
   - **practice:** one marker skill (`g2-practice-marker`, replies `G2-PRACTICE-OK`) that tells the model to use the core skill for a step, with the declared dependency (if there is a mechanism) on core.
3. **Test script (Chat, new chat each):**

| # | Step | Shows |
|---|---|---|
| Q3-1 | Install **practice only** (core not installed). Ask for the practice marker. | Does the platform block it, install core, or run it without core? |
| Q3-2 | Install **core**, then ask for the practice marker and a task that needs the core skill. | Does practice use core's skill? |
| Q3-3 | Remove **core**, ask the same task. | Does practice break cleanly, or silently fall back? |

**Decide**
- **Yes:** a declared dependency exists and the platform enforces or installs it, **or** practice demonstrably uses core's skill once core is present and fails closed when it is not.
- **No:** no mechanism exists and practice cannot reach core's content.
- **Untestable:** only some of the steps can run (state which).

**Desk lead (task 020, not a verdict).** In the v1.30 manifest schema the only dependency mechanism is `elementRelationshipSet` (`oneWayDependencies`, `mutualDependencies`), whose `elementReference` can name only `bots`, `staticTabs`, `composeExtensions` or `configurableTabs` inside the same manifest. A key search found no field that names another app, plugin or package. That points to **no** declared dependency across packages, with sequential install as the only workaround. Task 023 completes the check against the plugin schema and Learn before answering.

**Evidence sheet**

| Step | Time (UTC) | Surface | Observed | Pass / fail | Evidence ref (schema URL, screenshot, query) |
|---|---|---|---|---|---|
| Schema/docs check | n.a. | n.a. | | | |
| Q3-1 | | Chat | | | |
| Q3-2 | | Chat | | | |
| Q3-3 | | Chat | | | |

## Inventory rows (R4): send before any upload

| # | Artifact | Kind | Owner project | Purpose | Scope | Removal | Sent (date, link) |
|---|---|---|---|---|---|---|---|
| G2-q1 | `spaarke-legal-g2-q1-test` (app id `7bcab05e-3d88-4006-96c9-545edfc5a6ee`, v1.0.0, built 2026-11-26) | Package: one agent with the G5 action plus one marker `SKILL.md` | specialist-legal-agents-r1 | G2 Q1 | Owner + test user only, org catalog (R7) | Owner removes it when G2 is recorded | not yet (see "R4 row (to send)") |
| G2-q2 | `spaarke-legal-g2-q2-test` | **Not built.** The v1.30 schema allows one declarative agent per package (desk finding above) | specialist-legal-agents-r1 | G2 Q2 | n/a | n/a | **DROPPED (never uploaded)**, proposed; task 023 confirms |
| G2-q3a / q3b | `spaarke-legal-g2-q3-core-test`, `spaarke-legal-g2-q3-practice-test` (app ids minted at build) | Two small packages; practice declares a dependency on core if a mechanism exists | specialist-legal-agents-r1 | G2 Q3 | As above | As above | |

Rows for a package that is not built (for example Q2 stopped by the schema) are marked **DROPPED (never uploaded)**.

### R4 row (to send)

For the one G2 package task 020 built. Sent by the main session to COPILOT-PLATFORM-COORDINATION (Issue #3006) before upload; **not yet sent** (date and message link: ______).

| Artifact | Kind | Owner project | Scope | Removal plan | Removal date |
|---|---|---|---|---|---|
| `spaarke-legal-g2-q1-test` (app id `7bcab05e-3d88-4006-96c9-545edfc5a6ee`, v1.0.0) | App package, manifest 1.30: one declarative agent (v1.6) with one MCP action on `/mcp/legal-operations` (A1 `reference_id`, dynamic discovery) plus one marker skill `spaarke-g2-marker` | specialist-legal-agents-r1 | Owner + `demo@`, org catalog, never All users (R7) | Owner removes it in the admin center at the Phase 0 clean-up; coordination row closed after | at task 031 |

## Result

Copy the three answers into the table under "The three questions", then record them in the go/no-go record and `notes/decisions.md` (one dated line). If Q1 is **no**, record it as a packaging constraint for Phase 1: the skill and the agent ship as separate packages.

## Clean-up

When the results are recorded: the owner removes every uploaded G2 package from the admin center, and this session tells COPILOT-PLATFORM-COORDINATION they are gone (R4 rows closed).
