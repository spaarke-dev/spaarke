# Dev-tenant removal runbook

> **Owner approved the removal list on 2026-11-23** ("yes let's proceed with the remove list cleanup"). Row numbers refer to `tenant-inventory.md`.
> **The owner does every step.** Claude sessions never click admin actions, sign in or handle secrets. After each step, send a screenshot or a one-line "done"; this session moves the row to "Removed" in the inventory.
> Paths checked against Microsoft Learn on 2026-11-23: *Manage plugins, skills, and MCP servers* (Agent 365 admin), *Governance and lifecycle actions for agents*, *Use plugins with Copilot Cowork*, Copilot Studio *Create and delete agents*.

## Step 0 — screenshots first (needed for steps 2 and 3)

Two of the removals need you to pick the right copy, and I can't see the tenant. Please send:

1. Admin center › **Agents › All agents › Registry**, search "Spaarke". Show both "Spaarke AI" rows and both "Spaarke Dataverse MCP" rows, with their details pane (version, publisher, created date, installed or not).
2. Copilot Studio › **Agents** (dev environment): the "Spaarke Dataverse MCP" entries, if they are Copilot Studio agents.

## Now

| # | What | Where / how | Notes |
|---|---|---|---|
| 1 | **S5 `spaarke-visual-probe`** skill | OneDrive › `Documents/Cowork/Skills/spaarke-visual-probe` › delete the folder. Then open Cowork › **+** › **Customize** and check it is no longer under Plugins › Added (if it still is, select it › **Remove from Cowork**) | Per-user skill folder; only your account sees it. Active Cowork conversations keep it until they end |
| 2 | **D5 duplicate "Spaarke Dataverse MCP"** | After step 0: if it is a Copilot Studio agent, Copilot Studio › **Agents** › select the duplicate › **…** next to Settings › **Delete** › type the name › **Delete agent**. If it is only a registry record, admin center › **Agents › All agents › Registry** › select it › **Delete** | Deleting a Copilot Studio agent needs Power Platform environment-admin rights and is permanent. **Also tell me which project owns the copy we keep** |
| 3 | **D1 stale "Spaarke AI" registration** | After step 0, I'll name the copy. Then admin center › **Agents › All agents › Registry** › select it › **Uninstall** (if installed) and then **Delete**, or Teams admin center › **Manage apps** › select the app › **Delete** if it is an org-catalog Teams app | Which copy to keep is decided from the screenshot (likely keep the installed v1.1.0 and delete the uploaded-but-not-installed v1.5.0, unless you want v1.5.0). **Whether "Spaarke AI" stays at all is still your decision** |

## On a trigger

| # | What | Trigger | Where / how |
|---|---|---|---|
| 4 | **P1 "Spaarke Word Probe"** plugin | Same day, right after the accelerator's Word tests WP-0..WP-2 | Cowork › **+** › **Customize** › select the plugin › **Remove from Cowork**. If WP-2c deployed it from the admin center: **Agents › Tools** › select it › **Uninstall**, then **Delete** |
| 5 | **C2 "Spaarke MCP spike"** plugin (connectors A and B) | After mcp-r1 task 040 ships and T10 is recorded (mcp-r1 tells this session) | Admin center › **Agents › Tools** › select the plugin › **Uninstall** › confirm, then **Delete** › confirm. Then check **Agents › Tools › Registry** no longer lists "Spaarke MCP spike A/B" as MCP servers. Do **not** touch the federated connector "Spaarke MCP" (C1) or the Dev Portal OAuth registration (A1): C1 still uses it |
| 6 | **D2–D4** Copilot Studio agents (NDA Review, Legal Hold Review, Legal Review) | When the replacement area/packaged agent ships (specialist-legal-agents-r1 D-A11) | Copilot Studio › **Agents** › select › **…** › **Delete** |

Note on step 5: per Microsoft's governance page, uninstalling or deleting a plugin does not revoke consent or credentials and does not stop another registration of the same server. That is what we want here: the federated connector keeps working.

## Done log

| # | Date | Done by | Evidence |
|---|---|---|---|
| | | | |
