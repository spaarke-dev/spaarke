# Decisions — spaarkeai-mcp-bff-api-r1

> Full decision log. The project `CLAUDE.md` §3 keeps the one-line binding outcome of each decision that still constrains work and links here. Mark superseded entries with the date; never delete them.

## 2026-11-05 — Owner decisions O-1 to O-24 (design rounds 1–3)

Recorded in full in `design.md` §0 (O-1 to O-24, with O-25 and O-26 added 2026-11-06). Highlights that bind execution:
- O-2 / O-14: v1 includes completing a Work Item in the wizard; no model-callable complete tool.
- O-3 / O-23: federated connector primary; plugin package also built; Spaarke AI agent only as the T1 fallback.
- O-4: v1 users are in the BFF's own tenant (dev tenant).
- O-12 / O-24: production auth — OAuth at the Spaarke-tenant authority; ADR-028 amended by path B as **A10** (written, owner-approved).
- O-13: server-side lookups (B-3) in scope.
- O-15: the April 2026 Copilot surface stays (#2575, #1523) unless it interferes.
- O-16: widget embedded in the BFF; aim < 1 MB, hard ceiling 3 MB.
- O-17 / O-20: this project creates the connector and plugin package; the owner (Global Admin) runs the admin-center steps.
- O-18: `/` skill first plus direct prompts; org prompts later.
- O-19: one Copilot connector per product area, "Spaarke" + area; core member "Spaarke"; routes `/mcp/{area}`.
- O-22: a Signal produces a Work Item; resolving it writes a Decision Record.

Spec-interview decisions (2026-11-05): privileged Work Items appear to the model only as a neutral count line (FR-04); long lanes page through an app-only call (FR-05); the channel is not recorded on the Decision Record.

## 2026-11-06 — Naming conventions approved (O-25)

Route `/mcp/legal-operations`; functions are tool groups and skills, not routes; skills `spaarke-{area}-{capability}` (area dropped for core: `spaarke-decisions`); widget `ui://spaarke/decisions.html` in `src/client/widgets/spaarke-decisions/`; capability layer in `Services/Signals/`; "Spaarke AI" stays the agent name; "Spaarke Copilot" retired. Full table: `notes/coordination/2026-11-06-copilot-naming-and-ownership.md` §1.

## 2026-11-06 — Cross-project coordination is binding (O-26)

Agreements with legal-ai-accelerator-r1, ontology-platform-r1, ebilling-r1 and unified-access-control-r2: `notes/coordination/2026-11-06-copilot-naming-and-ownership.md` §2; design §20. Includes ontology rulings D-208 ("Finding" only for billing link-outs) and D-209 (qualify "playbook" by format).

## 2026-11-06 — Merge of #2580 / #2579 into this branch

The owner chose to merge ontology's held PRs into this branch so the plan covers all work with nothing deferred: #2580 `9423f37004` (merge `489f6508fc`) and #2579 `ab04492ab7` (merge `026b61554a`). BFF builds 0 warnings / 0 errors. Ontology's three conditions apply:
1. No dev BFF deploy from this branch until their pair has deployed (waits for ebilling task 112). Phase 0 runs on a master-based spike branch; if this branch must deploy sooner, back the merge out first.
2. D-207 order and replay guard unchanged; MCP commit only through `DecisionCommitService`.
3. Their pair merges to master first; re-merge on moves, never rebase.

Consequence: spec FR-12, Dependencies and the "PR #2580 timing" open question were updated the same day (only the deploy gate remains). Facts: `notes/pre-pipeline-context.md`.

## 2026-11-06 — Portfolio Epic

The project registers under Epic **#421 SPAARKE AI AND AUTOMATION** (owner choice).

## 2026-11-06 — Hot-path declaration: root-CLAUDE-md = YES

FR-18 adds a root `CLAUDE.md` §1.1 row ("Spaarke for Microsoft 365 Copilot" / "Spaarke"), so the hot-path declaration in design §16 and spec says `root-CLAUDE-md` = YES (commit `c384cf22fd`).

## 2026-11-06 — Plan-level choices (project-setup, delegated by the spec)

- FR-05 paging: a fifth **app-only, readOnly** tool with a cursor (working name `spaarke_list_decision_page`; final name fixed in task 022 under the naming note). The model-visible tool keeps its closed schema.
- Plugin package folder: `src/solutions/CopilotPlugins/spaarke/`; runbooks in `docs/guides/`. Task 040 may adjust if build tooling requires, and records the change here.
- B-4 split into two tasks (010 caller context + Dataverse client; 012 thread through executors, route cores and notifier) after discovery found `HttpContext` beyond the spec's list.
- B-5 adds `sprk_privilegeflagged` to the worklist list projection (projection only; read rules unchanged) so FR-04 can be met.
