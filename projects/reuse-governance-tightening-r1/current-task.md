# Current Task — Reuse Governance Tightening R1

> State, not history (root CLAUDE.md §5). Rewrite at each checkpoint; keep ≤ 10 KB.
> Decisions live in `design.md` §11; narrative lives in the commit messages.

---

**Phase**: `spec.md` written (2026-11-10, `/design-to-spec`, owner-approved design). Awaiting the owner's spec review. **No tasks exist yet** (no plan or POMLs).
**Active task**: none.
**Next action**: when the owner approves `spec.md`, run `/project-pipeline projects/reuse-governance-tightening-r1`. **Do not start it until the owner says so.**
**design-to-spec outcomes**: SpaarkeAi hot-path corrected to **Y** (item 4's three actions change `src/solutions/SpaarkeAi`) and the XML `<hot-path-declaration>` added to design.md. Owner accepted defaults: items 1–3 one PR, item 4 package actions their own PR(s), one adversarial pass over 1–4; dry-run rule both-caught→go / one-missed→owner sees `medium` cost / both-missed→no-go; FR-09 waits on owner approval of `package-reviews.md`.

## Where things stand (2026-11-10)

- **Worktree**: `C:\code_files\spaarke-wt-reuse-governance-tightening-r1`, branch `work/reuse-governance-tightening-r1` (tracks origin). No PR — open one only when the owner asks.
- **design.md**: recreated 2026-11-10 (the 2026-09-02 draft was lost, never committed). Five workstreams:
  1. **Capability index**: `.claude/patterns/CAPABILITY-INDEX.md` (~30 rows), cited by task-create 3.5.6 `<existing>`. Its paths are backticked repo paths, checked by the existing `Test-InstructionPaths.ps1`. No new ArchTest, no lint rule.
  2. **Reuse scan**: one `code-mapper` agent (Sonnet, effort low) in `/design-to-spec` Step 3. The orchestrator judges and writes `notes/reuse-scan.md`; ≥ 2 candidates become an owner question. `project-pipeline` Step 2 runs the full scan only when the file is missing. The dry-run against two closed projects is the **go/no-go** before any skill edit. design-to-spec SKILL.md:40/162 ("no comprehensive discovery") must be rewritten.
  3. **Weekly jscpd sweep**: observation only (exit 0) inside `nightly-health.yml`, with **no baseline file**. Alert `weekly/duplication-sweep` fires only for clusters ≥ 100 tokens **and** ≥ 10 lines across top-level areas that touch a file changed in the last 7 days. Plus a capability-duplication check (**F2**) in `adversarial-reviewer.md`.
  4. **Review of the 9 grandfathered `@spaarke/*` packages**: decide all 9, in one `notes/package-reviews.md`. Execute only `ai-context`, `legal-workspace` and `document-operations`; file the rest. Findings are never buried.
  5. **Quality-tooling quick wins**, each sub-item its own PR:
     - (a) Microsoft Learn MCP, which the researcher prefers.
     - (b) **DEFERRED** to after item 4.
     - (c) Nightly staleness **watchdog only** (no cloud routine).
     - (d) Requirements-trace check (**F4**) in adversarial-reviewer.
     - (e) **Re-arm `CS0618`** (remove the stale downgrade, fix callers, narrow `DiagnosticId` suppression only where a caller can't migrate).
     - (f) C# LSP A/B trial: 3 gates, half-day time-box.
- **Open questions**: all 16 (OQ-1..OQ-16) decided. The ROI review revised OQ-3, OQ-5, OQ-6, OQ-10, OQ-12 and OQ-14, and made OQ-13 and OQ-16 moot; see design.md §11. Every cut is recorded in §10.7.
- **Portfolio**: no Project Issue yet; register at `/project-pipeline` Step 4.2. Epic #427 (OQ-9).
- **Already done elsewhere**: F-1 (enumerate ADR-012's shared-package list + `SharedPackageCensusTests`) shipped in PR #2656 — 6 Sanctioned, 9 Grandfathered. Workstream 4 starts from that census.

## Standing directives (owner)

- Stop at the design until the owner says go; no `/design-to-spec` or `/project-pipeline` before that.
- No PR unless asked. Never `git stash` (shared stack across worktrees). Never commit secrets.
- OQ-2: if the reuse scan needs effort above `low`, bring the owner the measured token/cost impact first.
- OQ-5: the ≤ 3-consumer threshold limits effort, never significance — every defect is fixed or reported by name.
- Rejected alternatives in design.md §10 (Sonar, jscpd gate, ast-grep, generated manifest, deny hook on new files, shared LSP index), and the ROI-review cuts in §10.7: do not re-adopt them silently.

## Gotchas

- `code-mapper` is read-only and reports facts only — it cannot write `notes/reuse-scan.md` or judge candidates; the orchestrator does.
- Any repo-wide scanner must exclude `.claude/worktrees/agent-*/` (full source copies → phantom duplication).
- `Directory.Build.props:41` downgrades CS0618 repo-wide. Its justifying comment (line 18, 14 `DemoProvisioningOptions` callers) is stale: that file has no `[Obsolete]` member now. 5(e) removes the downgrade.
- `src/` has 21 `[Obsolete]` attributes in 15 files; 17 are provisioning rejection-code constants kept for compatibility. Check whether they have in-repo callers before re-arming.
- Bash tool can hang on this machine (120 s timeouts on trivial commands) — prefer PowerShell and the dedicated tools; never `python3` in Bash (Store stub spins).
- PowerShell `git commit -F -` with a here-string mis-parses; write the message to a scratch file and use `git commit -F <file>`.

## Context sources

- `design.md` (this folder) — the full design; §11 has every decision with its rationale.
- Auto-memory: `project_reuse-governance-tightening-r1.md` in the `C--code-files-spaarke` memory folder.
- Origin session transcript (design recreation + all owner Q&A): `C:\Users\RalphSchroeder\.claude\projects\C--code-files-spaarke-worktrees-sharp-gauss-3a6c71\f4bc0b9d-f961-4407-807d-065fa2cb239f.jsonl`.
