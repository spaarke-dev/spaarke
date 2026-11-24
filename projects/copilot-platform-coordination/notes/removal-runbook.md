# Dev-tenant removal runbook

> **Owner approved the removal list on 2026-11-23** ("yes let's proceed with the remove list cleanup"). Row numbers refer to `tenant-inventory.md`.
> **The owner does every step.** Claude sessions never click admin actions, sign in or handle secrets. After each step, send a screenshot or a one-line "done"; this session moves the row to "Removed" in the inventory.
> Paths checked against Microsoft Learn on 2026-11-23: *Manage plugins, skills, and MCP servers* (Agent 365 admin), *Governance and lifecycle actions for agents*, *Use plugins with Copilot Cowork*, Copilot Studio *Create and delete agents*.

## Step 0 — screenshots (received 2026-11-23, one still needed)

Received: Registry "spaarke" (9 rows), Copilot Studio agents, Plugins page, sources/skills list. Findings:
- There is **only one** "Spaarke Dataverse MCP" now, so step 2 is cancelled.
- "Spaarke AI" has two Registry rows (last updated 2026-10-28 and 2026-10-08) but no version shown. **Still needed:** click each "Spaarke AI" row and screenshot its details pane (version, app id, installed or not).
- NDA Review, Legal Hold Review and Legal Review are **Agent Builder** agents, not Copilot Studio agents, and are shared with **All users**.

## End-of-testing removal list (written 2026-11-24)

> **Owner direction 2026-11-24** (relayed by specialist-legal-agents-r1): "once we get the testing done we need to remove all plugins/agents/skills etc. that will not be part of our production package".
> Groups A, B and F follow the removal list you approved on 2026-11-23. Group E (Spaarke AI and its bot) **approved by the owner 2026-11-24** ("Yes, remove both").

**What stays (the production set):**
- **C1 "Spaarke MCP"**: the federated connector.
- **C3 "Spaarke"**: the plugin package.
- **A1**: the Dev Portal OAuth registration.
- **A2**: the Entra app "Spaarke Copilot Connector – dev".

**Not production, but kept for now:**
- **S1–S4**, the accelerator skills. They become the `spaarke-*` plugin after the rename; the old-name folders are removed then.
- **D5 "Spaarke Dataverse MCP"**, until you name the project that owns it.

Everything else is listed below. After each step, send "done" or a screenshot, and I'll move the row to "Removed".

### A. Test agents: now (the specialist confirmed 2026-11-24 that no test needs them; latest 2026-11-30)

| # | What | Where / how |
|---|---|---|
| A1 | **G8-p2** `spaarke-legal-g8p2-test` (app id `802fd9cc…`) | Admin center › **Agents › Tools** › select › **Uninstall**, then **Delete**. Then check that **Agents › All agents › Registry** no longer lists it; if it does, select it › **Delete** |
| A2 | **G8-p2-sl** `spaarke-legal-g8p2-sl` (`d38f8df8…`) | As A1 |
| A3 | **G8-p2-org-learn** `spaarke-legal-g8p2-org-learn` (`0421c980…`) | As A1 |
| A4 | **G8-p2-dd** `spaarke-legal-g8p2-dd` (`cb5afd1c…`) | As A1 |
| A5 | **G8-a** `spaarke-legal-g8-ab-test` (Agent Builder), with its two skills `spaarke-mcp-operations` and `spaarke-tool-test` | Admin center › **Agents › All agents › Registry** › select › **Delete**, or Copilot › the agent › **…** › Delete. If the two skills also appear in your skills list outside the agent: **…** › **Remove** |

Any later test uses a fresh app id, rebuilt from `projects/specialist-legal-agents-r1/phase0/g8/`. Uninstalling them does **not** affect C1 or A1. The agents reused A1's sign-in setup, but no agent owns it.

### B. Now

| # | What | Where / how |
|---|---|---|
| B1 | **S5 `spaarke-visual-probe`** skill | Same as step 1 below |

### C. On a trigger

| # | What | Trigger |
|---|---|---|
| C1 | **P1 "Spaarke Word Probe"** plugin | Same day as the accelerator's Word tests (step 4 below) |
| C2 | **D2–D4** NDA Review, Legal Hold Review, Legal Review (Agent Builder, shared with All users) | When their replacement practice agent ships (step 6 below) |

### D. Your decision

| # | What | Choice |
|---|---|---|
| D1 | **D6/D7** "Copilot in Power Apps" drafts | Delete or keep (step 7 below). They aren't production, so under your direction they go unless someone uses them |

### E. "Spaarke AI" and its bot (approved 2026-11-24)

| # | What | Where / how |
|---|---|---|
| E1 | **D1 "Spaarke AI", both Registry rows** (last updated 2026-10-28 and 2026-10-08) | Admin center › **Agents › All agents › Registry** › select each › **Uninstall** (if installed), then **Delete**. If it also appears under **Agents › Tools**, uninstall and delete it there too. This replaces step 3 below: there's no need to tell the rows apart if both go |
| E2 | **A3, the bot behind D1:** Azure Bot `spaarke-bot-dev`, its bot OAuth connection, and Entra app `f257a0a9…` ("Spaarke AI Assistant") | **After E1.** Azure portal › the Bot resource › **OAuth Connection Settings** › delete the connection; then delete the Bot resource. Then Entra › **App registrations** › `f257a0a9…` › **Delete**. **Leave any Key Vault secret for this app alone:** deleting a secret follows root CLAUDE.md §9 and `.claude/constraints/provisioning.md`, as a separate step |

What E removes, and what it doesn't:
- **Code stays.** The source in `src/solutions/CopilotAgent/` remains as the MCP tool-parity checklist.
- **Two code follow-ups, not tenant actions:** the BFF's `/api/agent/*` gateway loses its only Copilot caller, and `infrastructure/bot-service/` loses its resource.

### F. Checks

| # | What |
|---|---|
| F1 | **Agents › Tools › Registry** no longer lists "Spaarke MCP spike A/B" as MCP servers (X3) |
| F2 | **Agents › Tools**: `spaarke-nda-mvp` is gone (X1) |
| F3 | **C3 "Spaarke"**: its **Users** tab shows only you and the test user (R7) |

## Now

| # | What | Where / how | Notes |
|---|---|---|---|
| 1 | **S5 `spaarke-visual-probe`** skill (confirmed present 2026-11-23) | OneDrive › `Documents/Cowork/Skills/spaarke-visual-probe` › delete the folder. Then check it is gone from the skills list (if it is still there: **…** › **Remove**) | Per-user skill folder; only your account sees it. Active Cowork conversations keep it until they end |
| 2 | ~~D5 duplicate "Spaarke Dataverse MCP"~~ | **Cancelled 2026-11-23**: only one copy exists (Copilot Studio, Registry). Nothing to delete | Still open: which project owns it |
| 3 | **D1 stale "Spaarke AI" registration**. *Superseded by E1 (approved 2026-11-24): both rows go* | After the details-pane screenshot, I'll name the row. Then admin center › **Agents › All agents › Registry** › select it › **Uninstall** (if installed) and then **Delete** | Master's manifest is still `1.1.0`. Likely keep the installed v1.1.0 and delete the uploaded-but-not-installed v1.5.0. provisioning-r1 confirmed neither row is theirs (2026-11-23). **Whether "Spaarke AI" stays at all is still your decision** |

## On a trigger

| # | What | Trigger | Where / how |
|---|---|---|---|
| 4 | **P1 "Spaarke Word Probe"** plugin | Same day, right after the accelerator's Word tests WP-0..WP-2 | Cowork › **+** › **Customize** › select the plugin › **Remove from Cowork**. If WP-2c deployed it from the admin center: **Agents › Tools** › select it › **Uninstall**, then **Delete** |
| 5 | **C2 "Spaarke MCP spike"** plugin (connectors A and B) | **NOW (2026-11-24).** S-1 has run; mcp-r1 says T10 does not depend on C2, and C2 is a dead registration (B returns 404, A duplicates C1's URL) | Admin center › **Agents › Tools** › select the plugin › **Uninstall** › confirm, then **Delete** › confirm. Then check **Agents › Tools › Registry** no longer lists "Spaarke MCP spike A/B" as MCP servers. Do **not** touch the federated connector "Spaarke MCP" (C1) or the Dev Portal OAuth registration (A1): C1 still uses it |
| 6 | **D2–D4** Agent Builder agents (NDA Review, Legal Hold Review, Legal Review) | When the replacement area/packaged agent ships (specialist-legal-agents-r1 D-A11) | Admin center › **Agents › All agents › Registry** › select › **Delete** (the owner of an Agent Builder agent can delete it there). Interim, if you choose: change sharing from **All users** to you + the test user (R7) |
| 7 | **D6, D7** "Copilot in Power Apps – Spaarke Platform" and "– Corporate Counsel" (Drafts) | Your decision (not on the approved list) | Copilot Studio › **Agents** › select › **…** › **Delete** › type the name › **Delete agent** |

Note on step 5: per Microsoft's governance page, uninstalling or deleting a plugin does not revoke consent or credentials and does not stop another registration of the same server. That is what we want here: the federated connector keeps working.

## Done log

| # | Date | Done by | Evidence |
|---|---|---|---|
| 5 | 2026-11-24 ~03:15Z | owner | mcp-r1 report (C2 deleted) |
