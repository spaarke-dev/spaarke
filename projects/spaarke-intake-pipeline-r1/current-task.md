# Current Task State — spaarke-intake-pipeline-r1

> **Last Updated**: 2026-11-26 (checkpoint at 85% context)
> **Phase**: Phase 0/1 executing. Done: 002, 003, 005, 006, 007, 011, 012. Running (background implementer agents): **001** (opus) and **004** (sonnet). 010 waits on ontology coordination.

## Quick Recovery
| Field | Value |
|---|---|
| **Active** | Two background implementer agents in this worktree, commit locally, do not push. **001** engine container (`src/server/services/Sprk.Intake.EngineJob/**`, notes `notes/001-engine-container.md`; commits so far notes-only `9a5a86864c`, `2201e30d38`, `76ca8b184a`); building now with: `redline` + new `segment` operation, exit 3 → non-retryable `plan_does_not_fit` (index + code, stderr never logged), skip-and-report option OFF, parity = per-part bytes/CRC (zip timestamps excluded) + whole-file check report, in-image parity step in build-intake-engine-image.yml. **004** `IAgreementAi`/`AgreementAi`/`NullAgreementAi`, `agreement-edit-plan` Action, `AgreementEditPlanAssembler` (uncommitted files in tree are 004's); told to switch input to segment_clauses JSON + closed para_key list + exact-match pre-check (FR-02e). It touched `Sprk.Bff.Api.csproj` — **check for a new package and its justification (§10/§11) when it reports** |
| **Next action** | When each agent reports: review commits (`git log`, `git show --stat`), verify BFF build + named suites, check 004's csproj change, update TASK-INDEX (001/004 ✅ only if every criterion passes; 004's live-AI criterion may need an owner-OK dev run → 🔄), drift check, commit, push. Then: 008 (Phase 0 dev deploy + spike) needs owner OK for Azure resources; 013 is unblocked once 004 is done (deps 004, 010, 011, 012 — **010 still blocks 013**) |
| **Decided 2026-11-26 (owner)** | (1) Killed CPU-spinning stray processes (other sessions' `python -`, pubdelta.sh Compress-Archive with unexpanded `$d`, poll loops, orphan BuildHosts); CPU 80→12%; memory `feedback_python3-store-stub-spin` updated. (2) PR #3163 merged (squash `4c80b81ed4`), master merged into branch (`9110f14b26`). (3) 011 live on dev (job type 7 + 7 columns, idempotent; Entity.xml left to the next release export). (4) Refused edits: "Both: A, with B as backup" — accelerator skip-and-report mode + harness one AI rewrite then manual row. (5) Clean copy accepts all revisions from all sides; redline/external never lose or auto-accept (Unresolved Q1 resolved). (6) Clause anchoring deterministic (segment first, closed para_key list, exact-match pre-check, never fuzzy). All in spec, project CLAUDE.md §2, notes/decisions.md, POMLs 001/004/013/014/015 (commits up to the "clause anchoring is deterministic" commit) |
| **Accelerator (`acclerator-r1`)** | Accepted skip-and-report; contract: `ooxml_redline.py --skip-unplaceable REPORT.json` (codes only, 0-based edit index, closed code set incl. not_unique/crosses_markup/para_not_found…; exit 0 written / 2 bad input / 3 contract), `summary_table.py`/`check_outputs.py --refused REPORT.json`. Waiting on THEIR owner's go-ahead; one PR, they send the PR number. Also asked: fixed ZipInfo dates (reproducible .docx) + same code in default-mode exit-3 error. Flag agreed: in skip mode the external redline silently lacks refused changes → refused count + by-hand list on the approval Work Item (task 015 notes) |
| **Task 006** | Done. Pending owner: live test send (dev deploy OK, test recipient, shared mailbox). Unproven: server-send marker on createReply. Not built: reply-all; attachments > 3 MB |
| **Full suite caveat** | Full BFF unit suite ran starved (3h41m, 654 fails, all `TaskCanceledException` timeouts); not re-run after the kill — CI must confirm |
| **Open owner questions** | Phase 0 cleanup PR (005 + 006) OK to open? 008 dev deploy OK? |
| **Unresolved (spec)** | (2) ADR-049 scope → `/adr-check` (090); (3) ontology columns on sprk_servicerequest → before 010. (1) resolved 2026-11-26 |
| **External gates** | eBilling UAT (ebilling-r1 task 163) → 050; ontology coordination → 010; accelerator skip-mode PR → full FR-09f; Model 2 registry import → before any Model 2 rollout |
| **Provisioning** | #3210 / their ISS-046 agreed (3-step flow, digest manifest, `intakeEngineEnabled`). Constrained ACR grant NOT approved; 008 deploys by hand. **PING provisioning-r1 when Phase 0/1 merges to master** |
| **Task 008 needs** | `intakeEngineRegistryLoginServer` = platform ACR; AcrPull for `intakeEngineIdentityObjectId`; BFF setting `ServiceBus__IntakeEngineCompletionQueueName=intake-engine-completions`; CI push leg federated credential + AcrPush; tighten 400 MB ceiling after first size |
| **Commit hook gotcha** | `$env:PATH="C:\code_files\spaarke\node_modules\.bin;$env:PATH"` before `git commit`; message via `git commit -F <file>`; a bad pathspec in `git add` aborts the whole add (check `git diff --cached --stat`) |

## Standing directives
- Never `git stash`; never `python3`/`python -` heredocs in Bash (use the PowerShell tool); every Agent launch names a model.
- Don't edit other projects' worktrees; coordinate by message (accelerator: "acclerator-r1"; provisioning: "provisioning-r1").
- Never put accelerator `answer-key/` or real NDAs where an agent can read them.
- No edits to `Services/Finance/Submission/` before eBilling UAT sign-off.
- Don't copy or patch the accelerator engines; build from `src/accelerators` on master; Python 3.11.
- Phase 0 cleanup is one PR with the real fixes (005 + 006); no hygiene-only PRs.
- New tests go under KEEP paths (`tests/CLAUDE.md`).
- Sub-agents commit only their own files by path; the main session owns TASK-INDEX.md, current-task.md and `.claude/`.
- Merge to master only with the owner's go-ahead; live Azure/Dataverse/email actions need the owner's OK.
