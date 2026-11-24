# Dev-tenant removal runbook

> **Owner approved the removal list on 2026-11-23** ("yes let's proceed with the remove list cleanup"). Row numbers refer to `tenant-inventory.md`.
> **The owner does every step.** Claude sessions never click admin actions, sign in or handle secrets. After each step, send a screenshot or a one-line "done"; this session moves the row to "Removed" in the inventory.
> Paths checked against Microsoft Learn on 2026-11-23: *Manage plugins, skills, and MCP servers* (Agent 365 admin), *Governance and lifecycle actions for agents*, *Use plugins with Copilot Cowork*, Copilot Studio *Create and delete agents*.

## Step 0 — screenshots (received 2026-11-23, one still needed)

Received: Registry "spaarke" (9 rows), Copilot Studio agents, Plugins page, sources/skills list. Findings:
- There is **only one** "Spaarke Dataverse MCP" now, so step 2 is cancelled.
- "Spaarke AI" has two Registry rows (last updated 2026-10-28 and 2026-10-08) but no version shown. **Still needed:** click each "Spaarke AI" row and screenshot its details pane (version, app id, installed or not).
- NDA Review, Legal Hold Review and Legal Review are **Agent Builder** agents, not Copilot Studio agents, and are shared with **All users**.

## Now

| # | What | Where / how | Notes |
|---|---|---|---|
| 1 | **S5 `spaarke-visual-probe`** skill (confirmed present 2026-11-23) | OneDrive › `Documents/Cowork/Skills/spaarke-visual-probe` › delete the folder. Then check it is gone from the skills list (if it is still there: **…** › **Remove**) | Per-user skill folder; only your account sees it. Active Cowork conversations keep it until they end |
| 2 | ~~D5 duplicate "Spaarke Dataverse MCP"~~ | **Cancelled 2026-11-23**: only one copy exists (Copilot Studio, Registry). Nothing to delete | Still open: which project owns it |
| 3 | **D1 stale "Spaarke AI" registration** | After the details-pane screenshot, I'll name the row. Then admin center › **Agents › All agents › Registry** › select it › **Uninstall** (if installed) and then **Delete** | Master's manifest is still `1.1.0`. Likely keep the installed v1.1.0 and delete the uploaded-but-not-installed v1.5.0. provisioning-r1 confirmed neither row is theirs (2026-11-23). **Whether "Spaarke AI" stays at all is still your decision** |

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
