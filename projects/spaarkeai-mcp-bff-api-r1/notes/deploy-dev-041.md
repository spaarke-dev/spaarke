# Task 041: dev BFF deploy with MCP on (record)

> Closed 2026-11-24 (owner: "Yes, close 041"). The deploys ran in uac-r2's session, which owns dev deploys; this file collects their evidence and ours.

## Deviation from the POML

The POML deploys `work/spaarkeai-mcp-bff-api-r1`. The project merged to master first (PR #2587, `6129a7856f`, 2026-11-23 21:56Z), so the owner's order of 2026-11-23 (`current-task.md`, "OWNER DECISIONS") deployed **master without the spike**. That is the branch's code plus everything else on master. Steps 2–4 (conflict check, build, deploy) therefore ran in uac-r2's deploy pipeline, not from this worktree.

## Gate (ontology condition 1, owner per-SHA go)

| Item | Value |
|---|---|
| Ontology pair | #2580 merged to master 2026-11-23 18:57:45Z (`6b69bc7e71`); #2579 merged 18:57:49Z (`ad86343230`). Ontology condition 3 held: both merged before #2587 (21:56Z). |
| Pair on dev | Both commits are ancestors of `22c42efad6` and **not** of the rollback commit `a48398746d` (`git merge-base --is-ancestor`, checked 2026-11-24). They therefore first reached dev **in the same build** as the MCP code, on 2026-11-23 ~23:49Z, not in an earlier deploy. Condition 1 exists so the MCP code never runs on dev without their pair; a single build containing both meets that. Ontology-r1 was told of the #2587 merge and raised no objection. |
| Owner's go | Given in uac-r2's session, per SHA: `22c42efad6` (deploy, ~2026-11-23 23:49Z); then a separate go for `Mcp__Enabled=true` (2026-11-24 00:22Z); then "BFF deploy 996928732e" (2026-11-24 ~03:01Z). This session relayed information only and never gave a go. |
| Negative criterion | No deploy happened before both conditions held: the earlier dev builds (`a48398746d` and before) carry neither the pair nor the MCP routes outside the spike. |

## Deploys

| SHA (master) | Live | Contents | Publish size | Rollback |
|---|---|---|---|---|
| `22c42efad6` | ~2026-11-23 23:49Z | MCP host + 5 tools + widget, no spike; MCP off at first | 41.98 MB (≤ 60 MB) | `a48398746d` |
| `996928732e` | ~2026-11-24 03:01Z | + #3036 (review opens inline, no host fullscreen) | 41.98 MB | `22c42efad6` |

Evidence (uac-r2 live notices): file hashes match the package; the widget is embedded; for `996928732e` `resources/read ui://spaarke/decisions.html` returns `text/html;profile=mcp-app` with the same SHA-256 as the fresh `dist/index.html`.

## Verification

| Check | Result |
|---|---|
| `Mcp__Enabled` unset | `/healthz` 200; anonymous `/mcp/legal-operations` → 401 `WWW-Authenticate: Bearer`; signed-in → 503 ProblemDetails `mcp.disabled`; `/mcp/spike-area-b` → 404 (uac-r2, 2026-11-23 23:49Z–00:19Z gate) |
| `Mcp__Enabled=true`, dev App Service only (00:22Z) | `tools/list` returns exactly the five v1 tools: `spaarke_complete_decision`, `spaarke_get_decision_plan`, `spaarke_list_decision_page`, `spaarke_search_decision_lookup`, `spaarke_show_decisions`; no spike tools. Same five after `996928732e`. |
| Live through Copilot (owner) | Chat and Cowork: the widget renders the lane (193 = 187 approve-or-rebudget + 6 invoice) and Start review opens the wizard; App Insights shows `tools/call` and `resources/read` 200, `IsError = False` (`notes/phase0-results.md` S-1, T10). |
| Console smoke (owner, after `22c42efad6`) | Worklist loads and the Decision wizard opens. |
| Other environments | No setting changed outside dev. |

**Known gap, accepted by the owner at close:** the POML asks for `tools/list` as a licensed non-admin test user. uac-r2's five-tool check does not record whose sign-in it used, and the 2026-11-24 Copilot runs used the owner's account. Task 042 (live E2E) runs as the test user and covers it.

## Rollback

Unset `Mcp__Enabled` (kill switch, 503 with no tool run), or redeploy the previous SHA in the table through uac-r2 with the owner's go.

## Follow-ups (not 041)

- PR #3043 (review opens maximized, owner 2026-11-24): auto-merge on; uac-r2 folds it into the next dev candidate, with the owner's go per SHA.
