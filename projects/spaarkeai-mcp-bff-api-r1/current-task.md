# Current Task State - spaarkeai-mcp-bff-api-r1

> **Last Updated**: 2026-11-15 (mcp-r1, after task 025 close and the master merge)
> **Recovery**: Read "Quick Recovery" first. History: commit messages and `notes/wave{6,7,8}-review.md`, `notes/merge-review-2026-11-15.md`.

---

## Quick Recovery (READ THIS FIRST)

**State:** 19/29 tasks ✅ (board #2608 synced). Branch `work/spaarkeai-mcp-bff-api-r1` pushed, clean, tip `79f2b99a55`. Draft PR #2587 is MERGEABLE again; its full CI was running at checkpoint time (check `gh pr checks 2587`; Tier 1 `Router` is the verdict, Tier 2 advisory).

**Done 2026-11-15:**
- **025 ✅** — owner chose Tier 1 ("Use tier 1"): job 8 `mcp-conformance` in `.github/workflows/ci-tier1-blocking.yml`, BLOCKING, owner-approved §6.5 Path-A exception (header entry, job comment, PR #2587 body). Node 22 required (suite imports `fs.globSync`; `run-conformance.sh` now stops below 22). Passing run 43736976687: 92 passed, 38 baselined, 5 min (< Arch Tests 7 min).
- **Master merged in** (448 commits; ontology task 191, #2792): `387f6bf88b` merge + `39c993c58c` MCP mapping (sendChoice pass-through, 409 stored failure, 422, `plan.unfinished`); Fable review (REST byte-equal to master; 5 F) → fix round `a66e9b5509..66060a8f40` → Sonnet re-check clean → `288f558ff4`; merged into the branch `f9d1061111`. Record: `notes/merge-review-2026-11-15.md`. Ontology reviewed it: PASS-WITH-CHANGES; F4 fixed `efa348273f` (`spaarke_complete_decision` no longer idempotent: a send-again retry can send again). Client-lint fix `850451d189`. **PR #2587 must not merge to master before ontology's #2580 and #2579** (the branch carries their 174/175 code; held until ebilling Invoice Review is on dev) — ontology tells us when they land.

**Next (nothing is workable without the owner or a gate):**
- 040 (plugin package + skill) — HELD on the owner's batch D results T2/T14/T10 → record in `notes/phase0-results.md` first.
- 041 (dev BFF deploy from this branch, `Mcp:Enabled` on) — gated on ontology's #2580/#2579 deploy + owner per-SHA go (through uac-r2). Never deploy this branch with the spike (both map `/mcp/legal-operations`).
- 042 (live E2E) after 040+041; 050/051/052 docs all gate on 042 (050/051 main session only: they write `.claude/` / root CLAUDE.md); 090 wrap-up last. 003 waits on owner-only items.

**Tool shape** (for new tools): `McpToolDefinition` subclass tagged `[McpArea]`, `Create(IServiceProvider)` with `Services = services`, handler = unqualified static method, `[Authorize]` on the class, explicit ReadOnly + title, entry in the `McpTools` ledger (`tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Mcp.cs`); a non-read-only tool must be app-only. **Agent worktrees are cut from master** — briefs must `git checkout -B <name> origin/work/spaarkeai-mcp-bff-api-r1` (or the local branch) first.

**Gotchas:** never `python3`/`python -` in Bash. The pre-commit hook skips lint-staged on merge commits. Under load, Fluent dialog role queries starve on aria-hidden → use `hidden: true`. Widget jest needs the lexical mappers (no shared node_modules in CI).

## Active Lanes
- **Dev BFF live `aa7d6345f3`** (uac-r2, since 2026-11-15 18:32Z): master `c1eb6e4a8e` + spike `d7369fecf1` (ledger 155); healthz 200, anon POST `/mcp` and `/api/mcp/spike` 401; rollback `5a9b687a0b`. Every uac-r2 dev deploy includes the spike until Phase 0 is done; never this branch.
- Spike `spike/spaarkeai-mcp-phase0` tip `d7369fecf1` (never merged; worktree `C:\code_files\spaarke-wt-spaarkeai-mcp-phase0-spike`).
- Task 003: every test-user test is recorded in `notes/phase0-results.md`; remaining owner-only items: T10 skill, T13 second connector, T2/T14 package, T5 consent memory, "Always allow"; then owner review.

## Open Owner Questions
- **Wave 8:** Is the 41-entry conformance baseline (36+ fixture-only) "green" for NFR-09? ISS-007 Host/Origin on `/mcp` (#2835) before 042? 032: (1) display chain — fullscreen refused → master-detail beside the lane? (2) bundle 1.54 MB (aim 1 MB): accept or lazy-load the email editor? (3) Console launch parameter so the last fallback opens the Work Item? (4) matter heading stays text — accept? (5) declined `openLink` now shows a status message — OK?
- **Wave 7:** 010 seeding-proof AC amendment (Stateless is the guard); 031 DEF-001 #2832 "Done today"; core record type + label on `WorklistCoreSummary`; widget lane default open/collapsed (D-195); co-located widget tests under ADR-038.
- **Wave 6:** (1) privileged count first-page only — lane-wide needs ontology change? (2) worklist 503 hides `too_many_signals`? (3) non-billing items link to the Console root? (4) 024 null `HttpContext` to the capability — keep? (5) 023 partial results — keep? (6) category counts hint at privileged work type — accept?
- FR-02 plain Bearer challenge instead of RFC 9728 metadata (recommended amend; ADR text is `.claude/` → main session).
- Worktree cleanup DONE 2026-11-15 (owner "Yes, list and remove"): 14 of this project's agent worktrees removed with `scripts/worktree/Remove-WorktreeSafe.ps1` (clean, every commit already in the pushed branch or master; 31 npm links unlinked as links). Left alone: `agent-a87a71b050eceafb1` (ontology `task/174-billing-refusal`, their held #2580), `agent-ab2e7e9f6b446fd8a` (`fix/2735`, owner unknown), all ebilling/uac worktrees. Local branch refs (`merge/mcp-master-2026-11-15`, `fix/mcp-merge-review`, `worktree-agent-*`, `fix/2749|2756|2762`) are kept; their commits are all pushed.
- Older: #2745/#2746/#2750; #2731 wording; #1717 close; #2747 sign-off; #2748 merge go; area connector names; Connect consent (T5); free a Copilot seat; PR #2622 review.
- Found on master (not ours): leftover conflict marker in `projects/ai-advanced-capabilities-agreements-r1/notes/uat4-execution-notes.md`.

## Owed
- Connector secret rotation runbook (052); ADR-028 A10 follow-up note; user-setup docs state the Copilot licence requirement.

## Peer obligations
- **ontology-r1**: tells us when #2580/#2579 land (then #2587 may merge); ping them when PR #2587 merges; owner's answer on area connector names.
- **uac-r2**: orchestrates dev deploys (owner per-SHA go there); reviewer for auth diffs; task 041's deploy goes through them.
