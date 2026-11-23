# Current Task State - spaarkeai-mcp-bff-api-r1

> **Last Updated**: 2026-11-23 (mcp-r1, mid-merge of ontology ad86343230)
> **Recovery**: Read "Quick Recovery" first. History: commit messages and `notes/wave{6,7,8}-review.md`, `notes/merge-review-2026-11-15.md`.

---

## Quick Recovery (READ THIS FIRST)

**State:** 19/29 tasks ✅ (board #2608 synced). Branch `work/spaarkeai-mcp-bff-api-r1` pushed, clean, tip `79f2b99a55`. Draft PR #2587 is MERGEABLE again; its full CI was running at checkpoint time (check `gh pr checks 2587`; Tier 1 `Router` is the verdict, Tier 2 advisory).

**Done 2026-11-15:**
- **025 ✅** — owner chose Tier 1 ("Use tier 1"): job 8 `mcp-conformance` in `.github/workflows/ci-tier1-blocking.yml`, BLOCKING, owner-approved §6.5 Path-A exception (header entry, job comment, PR #2587 body). Node 22 required (suite imports `fs.globSync`; `run-conformance.sh` now stops below 22). Passing run 43736976687: 92 passed, 38 baselined, 5 min (< Arch Tests 7 min).
- **Master merged in** (448 commits; ontology task 191, #2792): `387f6bf88b` merge + `39c993c58c` MCP mapping (sendChoice pass-through, 409 stored failure, 422, `plan.unfinished`); Fable review (REST byte-equal to master; 5 F) → fix round `a66e9b5509..66060a8f40` → Sonnet re-check clean → `288f558ff4`; merged into the branch `f9d1061111`. Record: `notes/merge-review-2026-11-15.md`. Ontology reviewed it: PASS-WITH-CHANGES; F4 fixed `efa348273f` (`spaarke_complete_decision` no longer idempotent: a send-again retry can send again). Client-lint fix `850451d189`. **PR #2587 must not merge to master before ontology's #2580 and #2579** (the branch carries their 174/175 code; held until ebilling Invoice Review is on dev) — ontology tells us when they land.

**2026-11-20:** master merged in again (`418ea742cb`, 566 commits; only conflict the ledger count → 157; ArchTests 2732 ✅, Bff unit Signals|Mcp 5550 ✅; PR #2587 MERGEABLE; ontology-r1 told). T2/T14 spike package (from `d7369fecf1`) re-checked against the post-Ignite 1.30 schema: valid; zips built in the spike worktree's `src/solutions/CopilotPlugins/spike-phase0/` (gitignored). Researcher note: `.claude/agent-memory/researcher/agentconnectors-plugin-package-2026-11-20.md` (install path, `agentSkills` field for 040, Cowork client-id whitespace risk).

**2026-11-23 (IN PROGRESS at checkpoint):**
- Plugin package uploaded by owner. Results in `notes/phase0-results.md`: Cowork ✅ for T2/T13/T14 (one plugin name; A+B share one sign-in); Chat ❌/⚠️ (B fails before reaching the BFF; A ambiguous because the Dev Portal "Spaarke MCP" shares the URL). Rule for 040: register the server ONCE (package for Cowork, Dev Portal connector for Chat; never both in one surface). Open owner question: build a tagged A package (`?via=package`, v1.0.1) or accept the B result.
- ontology-r1: #2580/#2579 merged into `docs/ontology-platform-design` (NOT master). It asked us to merge `ad86343230` into our branch; then condition 3 holds whichever PR reaches master first. DONE and pushed: merge commit `a363c18b50` (tip `335169f2e3`); build 0 errors, ArchTests 2732/2732, SignalReadRouteTests 166/166, DecisionCommitServiceTests 360/360; ontology-r1 SIGNED OFF condition 3 on `a363c18b50` (2026-11-23); any later #2587 push touching `Services/Signals/**`, `Api/Signals/**` or DecisionWizard/Worklist UI needs its re-glance. One conflict, `DecisionCommitEndpoints.cs` description, took their side. 18/24 of their files equal `ad86343230`; 6 differ only where our MCP work changed them (DailyBriefing mock, DecisionCommitEndpoints, SignalReadService, SignalReadRouteTests, DecisionCommitServiceTests(.InvoiceReview)).
- **Exact next action:** put the #2587 master go to the owner (note: master merge auto-deploys the Console to dev; billing items open an empty Invoice Review dialog until ebilling 112). 040 decision pending: (a) skills-only package + existing "Spaarke MCP" connector (recommended; T10 on the real skill decides) or (b) package with its own connector (Cowork only). Owner chose: no tagged A package; Spike B result is final; minimise test artefacts; delete the spike plugin after 040.
- Dev BFF: see Active Lanes (`a48398746d` live 18:43Z).
- New project `specialist-legal-agents-r1` (#3002) runs in its own session (`local_a9bd8869…`); design.md v0.4, waiting on owner decisions.

**Next (nothing is workable without the owner or a gate):**
- 040 (plugin package + skill) — HELD on the owner's batch D results T2/T14/T10 → record in `notes/phase0-results.md` first.
- 041 (dev BFF deploy from this branch, `Mcp:Enabled` on) — gated on ontology's #2580/#2579 deploy + owner per-SHA go (through uac-r2). Never deploy this branch with the spike (both map `/mcp/legal-operations`).
- 042 (live E2E) after 040+041; 050/051/052 docs all gate on 042 (050/051 main session only: they write `.claude/` / root CLAUDE.md); 090 wrap-up last. 003 waits on owner-only items.

**Tool shape** (for new tools): `McpToolDefinition` subclass tagged `[McpArea]`, `Create(IServiceProvider)` with `Services = services`, handler = unqualified static method, `[Authorize]` on the class, explicit ReadOnly + title, entry in the `McpTools` ledger (`tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Mcp.cs`); a non-read-only tool must be app-only. **Agent worktrees are cut from master** — briefs must `git checkout -B <name> origin/work/spaarkeai-mcp-bff-api-r1` (or the local branch) first.

**Gotchas:** never `python3`/`python -` in Bash. The pre-commit hook skips lint-staged on merge commits. Under load, Fluent dialog role queries starve on aria-hidden → use `hidden: true`. Widget jest needs the lexical mappers (no shared node_modules in CI).

## Active Lanes
- **Dev BFF live `a48398746d`** (uac-r2, since ~2026-11-23 18:43Z, owner go; ledger 160): `9df4e5c819` + master `ca1684dd2f` (#2984, #2992); spike-v2 `973776a5a6` kept; file hashes match, healthz 200, signed-in route check passed; rollback `9df4e5c819`; 30-minute gate passed 19:11Z. Before it: `9df4e5c819` (~12:30Z, gate passed 13:01Z) = `c8ab2cba9e` + master `2dcbe35e34` (#2941, #2957, #2958, #2968, #2956, #2924). Before that: `c8ab2cba9e` (~2026-11-20 20:43Z) = `5ae2804884` + master `02bb05ba94`. Before that: `5ae2804884` (~2026-11-19 02:01Z) = `326781c50f` + master `fe51619cfc`. Before that: `326781c50f` (2026-11-17 22:03Z) = `e46b6b453b` + master `08161cfb0e` (#2884, #2894). Earlier spike-v2 builds: `5ef0a548df` (2026-11-16 05:47Z), `be0420d763` (~22:21Z), `e46b6b453b` (2026-11-17 03:40Z; deps.json M.E.AI 10.11.0, MCP 2.3.1, OpenAI 2.14.0). Owner re-test of AI draft pending. Every uac-r2 dev deploy includes the spike until Phase 0 is done; never this branch.
- Spike `spike/spaarkeai-mcp-phase0` tip **`973776a5a6` (spike-v2, pushed 2026-11-16)** (never merged; worktree `C:\code_files\spaarke-wt-spaarkeai-mcp-phase0-spike`). **Dev defect from our spike:** `d7369fecf1` carried MCP 2.3.1 (needs M.E.AI.Abstractions 10.11.0) without task 020's M.E.AI/OpenAI bump, so dev builds since ~2026-11-13 mixed Abstractions 10.11.0 with M.E.AI 10.3.0, and `POST /api/communications/draft` returned 503 TypeLoadException (found by ontology-r1, confirmed by uac-r2). spike-v2 = `d7369fecf1` + cherry-pick of `a940d5b2b7` (3 csproj files only; spike widget embedding kept). Build: 0 warnings. `~.Ai.` unit: 6931 passed, 1 skipped. uac-r2 builds the next dev merge as master + spike-v2 and puts that SHA to the owner. AI draft stays broken on dev until that deploy. Lesson: any package change on this branch must also go on the spike while the spike is in dev builds.
- Task 003: every test-user test is recorded in `notes/phase0-results.md`; remaining owner-only items: T10 skill, T13 second connector, T2/T14 package, T5 consent memory, "Always allow"; then owner review.

## Open Owner Questions
- **Wave 8:** Is the 41-entry conformance baseline (36+ fixture-only) "green" for NFR-09? ISS-007 Host/Origin on `/mcp` (#2835) before 042? 032: (1) display chain — fullscreen refused → master-detail beside the lane? (2) bundle 1.54 MB (aim 1 MB): accept or lazy-load the email editor? (3) Console launch parameter so the last fallback opens the Work Item? (4) matter heading stays text — accept? (5) declined `openLink` now shows a status message — OK?
- **Wave 7:** 010 seeding-proof AC amendment (Stateless is the guard); 031 DEF-001 #2832 "Done today"; core record type + label on `WorklistCoreSummary`; widget lane default open/collapsed (D-195); co-located widget tests under ADR-038.
- **Wave 6:** (1) privileged count first-page only — lane-wide needs ontology change? (2) worklist 503 hides `too_many_signals`? (3) non-billing items link to the Console root? (4) 024 null `HttpContext` to the capability — keep? (5) 023 partial results — keep? (6) category counts hint at privileged work type — accept?
- FR-02 plain Bearer challenge instead of RFC 9728 metadata (recommended amend; ADR text is `.claude/` → main session).
- Worktree cleanup DONE 2026-11-15 (owner "Yes, list and remove"): 14 of this project's agent worktrees removed with `scripts/worktree/Remove-WorktreeSafe.ps1` (clean, every commit already in the pushed branch or master; 31 npm links unlinked as links). Left alone: `agent-a87a71b050eceafb1` (ontology `task/174-billing-refusal`, their held #2580), all ebilling/uac worktrees. (`agent-ab2e7e9f6b446fd8a`, `fix/2735` = merged PR #2747, was removed 2026-11-23 by the #1821 session with Remove-WorktreeSafe.ps1.) Local branch refs (`merge/mcp-master-2026-11-15`, `fix/mcp-merge-review`, `worktree-agent-*`, `fix/2749|2756|2762`) are kept; their commits are all pushed.
- Older: #2745/#2746/#2750; #2731 wording; #1717 close; #2747 sign-off; #2748 merge go; area connector names; Connect consent (T5); free a Copilot seat; PR #2622 review.
- Found on master (not ours): leftover conflict marker in `projects/ai-advanced-capabilities-agreements-r1/notes/uat4-execution-notes.md`.

## Owed
- Connector secret rotation runbook (052); ADR-028 A10 follow-up note; user-setup docs state the Copilot licence requirement.

## Peer obligations
- **ontology-r1**: tells us when #2580/#2579 land (then #2587 may merge); ping them when PR #2587 merges; owner's answer on area connector names.
- **uac-r2**: orchestrates dev deploys (owner per-SHA go there); reviewer for auth diffs; task 041's deploy goes through them.
