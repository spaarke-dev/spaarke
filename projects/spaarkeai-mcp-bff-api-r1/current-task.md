# Current Task State - spaarkeai-mcp-bff-api-r1

> **Last Updated**: 2026-11-14 (mcp-r1, Wave 6 done)
> **Recovery**: Read "Quick Recovery" first

---

## Quick Recovery (READ THIS FIRST)

**Done today:** 020 ✅, 021 ✅ (board 11/29). 021 commits `6b8f74a346`, `871b94d53e`, fix round `d9169b43b6`/`d372711ffa`/`a0f360e06d`, closure `7f5db98aed`; two adversarial passes + uac-r2 PASS (incl. fix-round re-check). Branch `work/spaarkeai-mcp-bff-api-r1` pushed, clean (tip `1124fbdd60`).

**PR #2757 (#2749 inbound backup poll) — MERGED 2026-11-13 on the owner's go (`5f86c3bbd5`), #2749 closed; worktree `agent-a39ef1abcda4cc091` removable with the owner's approval. History:** migrated to an ADR-036 `IScheduledJob` (ADR-052 Path C), three adversarial reviews' findings fixed; tip `74e055f141` (worktree `C:\code_files\spaarke\.claude\worktrees\agent-a39ef1abcda4cc091`, branch `fix/2749-inbound-poll-backlog`). Owner said "merge when green". CI: required `Router` + `Client lint` pass, 40 pass; two non-required jobs cancelled at their timeouts twice (re-run once): Markdown Link Validator (times out repo-wide, master too) and Office-scope server tests (api-office) — CI hit the 20-min job limit (master 9m39s, prior commit `d3f36f0322` 10m53s); locally on `74e055f141` with `--blame-hang` 569/569 passed in 12m15s, no hang. Held for the owner's call: merge now, or investigate the CI slowdown first.

**Open owner question (021):** amend FR-02 to the plain Bearer challenge instead of RFC 9728 protected-resource metadata (needs an anonymous `/.well-known` route; recommended amend, uac-r2 agrees). Same edit fixes FR-02 / ADR-028 A10 reason-code wording (`sdap.access.deny.caller_unresolved`, not `UserContextRequired`). ADR text is under `.claude/` → main session only.

**WAVE 6 DONE (2026-11-14):** 022, 023, 024, 030 ✅ (board 15/29). Merged into this branch (merges `9ccfe2a21d` 024, `4e836a4d6d` 023, `9da4a6f250` 030, `5aea8e3bd9` 022); one Fable adversarial pass per task, fix round `85272880c2` (BFF) / `99dfa03919` (widget, deploy, CI) / `537f8564d1` (guard), scoped Sonnet re-check → `4f11ee17c5`; main-session guard fix `b883e97a31` (F2 `is` keyword + widget ResourceCollection exception). Findings and known limits with their next owner: `notes/wave6-review.md`. Verified on the merged branch: BFF build 0 warnings, ArchTests 2538/2538, MCP/Signals/Decision/Worklist suites 5867 pass, widget jest 90/90, bundle 0.73 MB. MCP tool errors now carry `{reasonCode, message, status, correlationId, partial?}`; the widget shim turns them into REST's ProblemDetails. Publish ≈ 43.1 MB, no new CVEs. Open owner questions from Wave 6 are in "Open Owner Questions". **Next:** Wave 7 — 010 (caller-path verification, opus), 031 (Decide lane in the widget), 040 (plugin package + skill).
**Tool shape** (for 010/031/032): `McpToolDefinition` subclass tagged `[McpArea]`, `Create(IServiceProvider)` with `Services = services`, handler = an unqualified static method of the tool class, `[Authorize]` (or `[Authorize(Policy = "SystemAdmin")]`) on the class, explicit ReadOnly + title, entry in the `McpTools` ledger (`tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Mcp.cs`); no `Metadata =`, no `partial`; a non-read-only tool must be app-only (`ValidateTool`). With `Mcp:Enabled=true` a bad tool stops startup. **Agent worktrees (`isolation: worktree`) are cut from master, not this branch** — every brief must check `git merge-base --is-ancestor <branch tip> HEAD` first. Leftover worktree folders to remove with the owner's approval: the four Wave 6 agent worktrees under `C:\code_files\spaarke\.claude\worktrees\`, plus `C:\wt030b`.

**Gotchas:** 020's module and the spike both map `/mcp/legal-operations` → never in one build. Tool-written working copies can be LF → `rm` + `git checkout --` before `check-changed-cs-format.sh`. Never `python3`/`python -` in Bash. The Stop-hook quality gate fails while a background implementer is mid-edit in this worktree — that is in-progress work, not a branch break.

## Task 003 (Phase 0 live tests) — IN PROGRESS 2026-11-13 (Copilot back)

**Status 2026-11-14 ~03:45Z:** every test runnable by the test user is done and recorded in `notes/phase0-results.md` — Chat (incl. T6b: the Sources switch does not stop tool calls; E4; E5 ❌ no `@`), Cowork (plugin was OFF by default in Customize, switched on with the owner's go; T1 ✅, Size 6 MB ✅, T3 no card, T4 card per call with "Always allow <tool>", T7 ❌, T12 card, E2 ✅, T8 n/a), Outlook T1 ❌ (connector not offered), Word T1 ⚠️ (Choose plugins → approval card even for reads → text only, no widget). Test artifact: the blank `Document.docx` was sent to the test user's OneDrive recycle bin on the owner's go (2026-11-14). **Remaining for 003:** owner-only items (T10 skill, T13 second connector, T2/T14 package, T5 consent memory, "Always allow"), then owner review (step 7). Earlier run notes:
**Live run (2026-11-13 ~21:25Z on):** the main session drives Copilot in the Claude Desktop built-in browser pane, signed in as the test user (Spaarke Demo Access, non-admin; the owner signed in, never enter credentials). Spaarke MCP source is on by default for this user. **Chat done and recorded in `notes/phase0-results.md`:** T1 dark ✅, Size 1/3/6 MB ✅, T3 (no host card for widget writes), T4 (per-call card with params; options Allow once / Always allow), T6 13/15 + 0/5 controls, T7 ❌ not honoured, T9 ✅, E1 ✅, T12 Chat side-observation. **Next, in order:** E5 (`@`), E4 (prompt gallery), T6b (toggle Spaarke MCP source OFF via + › Change data sources, prompts 1/3/5/7/9, then turn it back ON), then Cowork (T1, Size, T3, T4, T7, E2, T8, T12; T10 needs owner OK — creates a saved skill), then Word / Outlook T1. Not without the owner: "Always allow", T10, T13/T2/T14 admin steps, T5 memory. Evidence = browser-pane observations (no files); note the dev BFF SHA `b100effac3`. Never use `python -` in Bash (it hung once here 2026-11-13, stopped).

| Field | Value |
|-------|-------|
| **Task** | 003 Run and record Phase 0 tests, STANDARD, prescriptive. |
| **State** | `notes/phase0-results.md`: T5 ✅, T0 ✅, T1 Chat ✅, E3 ✅, T11 observed, Size 1 MB. 043 cancelled (T1 gate). Checklist `notes/phase0-test-checklist.md`: batch A runnable; B needs the owner to add connector "Spaarke MCP B" on `/mcp/spike-area-b` (T13); C (Size 3/6 MB, T3, T4, T7, T12, T2, T14) now runnable. |
| **Spike** | `spike/spaarkeai-mcp-phase0` tip `d7369fecf1` (never merged; worktree `C:\code_files\spaarke-wt-spaarkeai-mcp-phase0-spike`). |
| **Next** | When Copilot is back: run batches A–C with the owner + licensed non-admin test user; record results; acclerator-r1 sends WP-3/T1-Word results. |

## Active Lanes
- **Dev BFF live `0173e1486e`** (uac-r2, since 2026-11-14 ~12:44Z): master `bd43110169` (after #2818) + spike `d7369fecf1`; healthz/dataverse/ping 200, anon GET+POST /mcp 401; 30-min App Insights gate. Previous: `b100effac3` (2026-11-13 20:39Z): master `c2f366bf52` (includes #2757) + spike `d7369fecf1`, ledger conflict resolved in the build merge (census 153); healthz/dataverse 200, anon /mcp 401; 30-min gate to ~21:09Z. Previous: `0c06f464e3` (13:26Z). #1561 open → the backup poll is the only inbound path. uac-r2 pings before each swap so live Copilot tests pause. Rule: every uac-r2 dev deploy includes the spike tip until Phase 0 is done; never this branch.
- **Side PRs merged 2026-11-13:** #2759 (#2756 rate-limit log, `0d676cc66a`), #2763 (#2762 CopilotAuth failure log, `2da422bb9c`, uac-r2 gate PASS). ISS-005/006 Done in `notes/defer-issues.md`.
- Filed today: #2758 (JobSubmissionService sender per job); comments on #2134 (processor logs Subject/From; IP in two logs).

## Open Owner Questions
- **Wave 6 (2026-11-14):** (1) 022 privileged count is first-page only (now labelled so); lane-wide count needs a `Services/Signals` change (ontology) — want it? (2) 022 worklist 503 returns REST's `worklist_unavailable`, hiding `too_many_signals` — intended? (3) non-billing items link to the Console root (no per-item launch parameter) — want one later? (4) 024 passes a null `HttpContext` to the capability (fails closed there) instead of a pre-check in the tool — keep (recommended)? (5) 023 partial results + extra lane read — keep for v1 (recommended, reviewer agrees). (6) 022 K: whole-lane category counts can hint at a privileged item's work type — accept or drop the "None of the first N…" sentence?
- FR-02 plain Bearer challenge (above).
- #2745 / #2746 / #2750: fix them too? (asked, no answer)
- #2731 builder + issue wording (uac-r2 chose option A).
- #1717 close? · #2747 site-3 sign-off · #2748 merge go · area connector names · Connect consent question (T5) · one Copilot seat can be freed · PR #2622 review.

## Owed
- Connector secret rotation runbook (052); ADR-028 A10 follow-up note; user-setup docs state the Copilot licence requirement.

## Peer obligations
- **ontology-r1**: ping when PR #2587 merges; owner's answer on area connector names.
- **uac-r2**: orchestrates dev deploys (owner per-SHA go there); reviewer for auth diffs (021 done); task 041 deploy that turns `Mcp:Enabled` on goes through them.
