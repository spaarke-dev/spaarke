# Lessons Learned — `ai-advanced-capabilities-nda-r1`

> One lesson per entry. Corrections AND confirmed non-obvious approaches. Promote durable cross-project lessons to `.claude/FAILURE-MODES.md` or an ADR at project close (doc-drift-audit).

## Architecture / governance
- **Grounding is mode-independent — state it as a shared invariant, not advisory fine-print.** The ADR-039 amendment initially buried "no hallucination even for advisory" inside advisory-mode bullets. Owner review (correctly) pushed for it to lead as the invariant BOTH modes share, and to cover advisory *reasoning* (recommendations traceable to sources), not just isolated facts. The strengthened version is the reference for future advisory verticals.
- **"Mode = catalog data" needs a real column.** The amendment says output-determinism is catalog data, but task 001 was docs-only; there's no `sprk_outputdeterminism` column yet, so NDA-REVIEW carries it as mirror data + prompt-enforcement. Author the ADR **and** the data column together next time, or the ADR's "data" claim outruns the implementation.
- **Cite the ADR that actually governs.** Tasks 010/011 cited ADR-016 (Cost/Rate-limits) for the "single tier→deployment surface" rule — ADR-016 says nothing about tiers; the real authority is ADR-039. The mis-citation propagated to ~7 files before code-review caught it. Ironic given the project's whole point — an authoritative-sounding claim not grounded in what the source says. Grep-verify ADR citations.

## Reuse-first wins (the pattern paid off repeatedly)
- **023 (orchestration) needed ZERO production code** — the existing dispatch spine already expressed the one-run→two-payloads fan-out (client-derived views of one ledger entry). Trace the spine before assuming you need an orchestration class.
- **033 (draft-alternative) was already shipped** by prior compose-r2/r4 work — the `bindingId:''` "stub" was a documented Phase-4 boundary, already resolved via capability-discovery. Hardcoding a GUID would have been a regression. Check git history for "stubs" before filling them.

## Execution / process
- **Env-blocked ≠ done, and must never be faked.** A large fraction of acceptance criteria (live model calls, RAG ingest, deploy, dark-mode) genuinely need a live environment. Every subagent flagged these with exact runbooks instead of fabricating a "verified" — consistent with the no-hallucination principle the project hardened. The 🔄/⛔ statuses + DEPLOYMENT-RUNBOOK.md are the honest handoff.
- **Parallel subagents racing on shared shell files is the main coordination cost.** `ConversationPane.tsx`, `ComposeWorkspace.tsx`, `ComposeService.cs`, `TASK-INDEX.md`/`current-task.md` were touched by multiple tasks. Sequencing tasks that share a file (and committing between them) avoided clobbers; disjoint-file waves ran cleanly 4-wide. Give each subagent explicit file ownership + "note exact lines you added" so the next task can add its own without conflict.
- **Tenant-isolation changes stay human-gated even under an autonomy grant.** The NFR-06 tenant-pin fix is the project's highest-value unblocker but touches a multi-tenant filter — CLAUDE.md §6/§9 security carve-out overrides "work autonomously." Surfaced with a recommendation; left for explicit sign-off.
- **Eval authoring surfaces real product gaps.** Task 050's non-NDA negative case (NEG-01) exposed that NDA-REVIEW had no "decline if not an NDA" guard — fixed as a tracked prompt strengthening (020 SCOPE GUARD), not silently.

## Measurement gotcha
- **Publish-size §10 metric is COMPRESSED, not the raw publish dir.** A raw `du` of `deploy/api-publish` read 148 MB (uncompressed, includes runtime) and looked like a massive regression; the actual compressed artifact is ~51 MB, under the 60 MB ceiling. Always measure the compressed artifact.
