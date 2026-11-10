# Current Task — Reuse Governance Tightening R1

> State, not history (root CLAUDE.md §5). Rewrite at each checkpoint; keep ≤ 10 KB.
> Decisions live in `design.md` §11; narrative lives in the commit messages.

---

**Phase**: design complete, awaiting owner review. **No tasks exist yet** (no spec, plan or POMLs).
**Active task**: none.
**Next action**: when the owner says the design review is done, run `/design-to-spec projects/reuse-governance-tightening-r1`, then `/project-pipeline`. **Do not start either until the owner says so.**

## Where things stand (2026-11-10)

- **Worktree**: `C:\code_files\spaarke-wt-reuse-governance-tightening-r1`, branch `work/reuse-governance-tightening-r1` (tracks origin). No PR — open one only when the owner asks.
- **design.md**: recreated 2026-11-10 (the 2026-09-02 draft was lost, never committed). Five workstreams:
  1. Capability index — `.claude/patterns/CAPABILITY-INDEX.md` (~30 rows), cited by task-create 3.5.6 `<existing>`; freshness ArchTest; `Validate-TaskPoml.ps1` **warning** when the citation is missing.
  2. Reuse scan — one `code-mapper` agent (Sonnet, effort low) in `/design-to-spec` Step 3; the orchestrator judges and writes `notes/reuse-scan.md`; ≥ 2 candidates → owner question; `project-pipeline` Step 2 confirms and re-scans only changed capabilities.
  3. Weekly jscpd sweep — observation only (exit 0) inside `nightly-health.yml`; baseline file; alert `weekly/duplication-sweep` only for new clusters ≥ 100 tokens **and** ≥ 10 lines across top-level areas; plus a capability-duplication check (F4) in `adversarial-reviewer.md`.
  4. Review of the 9 grandfathered `@spaarke/*` packages — decide all 9 (sanction / consolidate / retire); execute consolidations only for ≤ 3 consumers; findings never buried (fixed or reported to the owner by name).
  5. Quality-tooling quick wins — (a) Microsoft Learn MCP + researcher prefers it; (b) TS strictness census ratchet; (c) monthly `knowledge/` refresh as a Claude Code cloud routine + nightly watchdog; (d) requirements-trace check in adversarial-reviewer; (e) full superseded-API process (`SPRK001`/`SPRK002` diagnostic IDs, re-arm CS0618, IlCallScan caller ratchet); (f) C# language-server A/B trial with fixed go/no-go thresholds.
- **Open questions**: all 16 (OQ-1..OQ-16) **decided** — see design.md §11. Owner setup step: a cloud environment connected to the GitHub repo for 5(c) (OQ-16).
- **Portfolio**: no Project Issue yet; register at `/project-pipeline` Step 4.2. Epic #427 (OQ-9).
- **Already done elsewhere**: F-1 (enumerate ADR-012's shared-package list + `SharedPackageCensusTests`) shipped in PR #2656 — 6 Sanctioned, 9 Grandfathered. Workstream 4 starts from that census.

## Standing directives (owner)

- Stop at the design until the owner says go; no `/design-to-spec` or `/project-pipeline` before that.
- No PR unless asked. Never `git stash` (shared stack across worktrees). Never commit secrets.
- OQ-2: if the reuse scan needs effort above `low`, bring the owner the measured token/cost impact first.
- OQ-5: the ≤ 3-consumer threshold limits effort, never significance — every defect is fixed or reported by name.
- Rejected alternatives in design.md §10 (Sonar, jscpd gate, ast-grep, generated manifest, deny hook on new files, shared LSP index) — do not re-adopt them silently.

## Gotchas

- `code-mapper` is read-only and reports facts only — it cannot write `notes/reuse-scan.md` or judge candidates; the orchestrator does.
- Any repo-wide scanner must exclude `.claude/worktrees/agent-*/` (full source copies → phantom duplication).
- `Directory.Build.props:41` downgrades CS0618 repo-wide (for 14 `DemoProvisioningOptions` callers) — that is why `[Obsolete]` currently signals nothing; 5(e) is built around this.
- Bash tool can hang on this machine (120 s timeouts on trivial commands) — prefer PowerShell and the dedicated tools; never `python3` in Bash (Store stub spins).
- PowerShell `git commit -F -` with a here-string mis-parses; write the message to a scratch file and use `git commit -F <file>`.

## Context sources

- `design.md` (this folder) — the full design; §11 has every decision with its rationale.
- Auto-memory: `project_reuse-governance-tightening-r1.md` in the `C--code-files-spaarke` memory folder.
- Origin session transcript (design recreation + all owner Q&A): `C:\Users\RalphSchroeder\.claude\projects\C--code-files-spaarke-worktrees-sharp-gauss-3a6c71\f4bc0b9d-f961-4407-807d-065fa2cb239f.jsonl`.
