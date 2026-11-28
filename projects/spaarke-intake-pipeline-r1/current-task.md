# Current Task State — spaarke-intake-pipeline-r1

> **Last Updated**: 2026-11-27
> **Phase**: Phase 0 done except 008's BFF steps; Phase 1 started. Done: 001–007, 009–013. 008 🔄 (BFF steps now unblocked: on master). **PR #3253 merged to master** at `a7712744d4` (2026-11-28, owner go).

## Quick Recovery
| Field | Value |
|---|---|
| **Active** | None in progress. 001–013 are on master (`a7712744d4`). uac-r2 cleared the whole access diff (items 1–7), incl. the reply-parent check `b7b6926ffd` (app-only threaded reply needs a parent the caller can Read; `ReplyParentSetByServer` server-only flag for FR-20; timing difference accepted as K-class). Codex P2 fixed in `d83e269b5d` (stage marked completed when its steps were stored by an engine completion) |
| **Next action** | Wave 1-3 (2026-11-28): **014** running as a background implementer agent in its own worktree (bring its branch into ours when it reports, then build, review, TASK-INDEX). **017 paused, to be rewritten:** owner chose "Yes, Decide lane" (notes/decisions.md 2026-11-28): approval = Decide-lane Signal on the service request + intake policy + trigger + `intake.approve`/`intake.reject` catalog actions through DecisionCommitService (review-kind Decision Record, no new writer kind); claim = intake action on `sprk_assignedtointernal`; auth = Read else 404, owning-team member else 403, claimer only after claim; harness kill switch after auth. Waiting on ontology-r1's answer (questions a–d) and uac-r2's early view (a–c); then rewrite 017's POML (and check 018/019 for the To Do assumption) and re-dispatch. Then 016, 023, 018, 019. Also the 008 follow-ups below. Dev BFF deploy of `a7712744d4`: uac-r2 sends a LIVE notice |
| **Merge / deploy path (uac-r2)** | For each later merge: master into branch, CI green, access diff vs merge base to uac-r2, their answer, master merge with owner go, SHA to uac-r2. Dev BFF deploys only from a master SHA the owner names in the uac-r2 session |
| **Known CI stalls (not ours)** | advisory `api-insights-integration` 40-min stall (#3255, quality alert #2060); `Server tests (office scope)` 20-min cap (#1474, #2767). Required checks are only `Router` + `Client lint` |
| **After master deploy (008 follow-ups)** | `Seed-PlaybookConsumers.ps1` for the agreement-edit-plan Binding (held: catalog health drift until the BFF has `ConsumerTypes.AgreementEditPlan`); redeploy `notes/spikes/dev-intake-engine.bicep` step 3 with the CI `latest.json` digest (dev runs spike image `spike-9bad3f4815`); `ServiceBus__IntakeEngineCompletionQueueName=intake-engine-completions` with task 014 |
| **Dev state** | Engine: `mi-intake-engine-dev`, queues `intake-engine-requests`/`-completions` (spaarke-servicebus-dev), `spaarke-intake-cae-dev` + `spaarke-intake-job-dev`, `intake-staging` in `sprkspaarkedevebillsa` (7-day rule; eBilling knows the one-policy rule), AcrPull on `sprkcontrolplanedevacr`. Dataverse: 010 columns live; 011 columns live; `agreement-edit-plan` Action row `22172ab1-82ea-f111-84ea-00224820fd20`. Dev `/healthz/catalog` was already 503 (8 bindings without constants, 3 tool description drifts) before us |
| **Spike facts (008)** | 3/3 NDAs completed, 9/9 .docx byte-identical to local; replica start ~47–50 s dominates (~2.2–2.5 min per NDA for segment + redline); consider one execution for both ops in 014 if needed |
| **Decided 2026-11-27 (owner)** | 010 column names/values agreed with ontology-r1 (notes/decisions.md; intake reads exclude null `sprk_intakestage`); assigned attorney = existing `sprk_assignedtointernal` (contact) via `ResolveContactForUserAsync`; 010 dev run OK; 008 dev deploy OK; 013 run ownership routed + role/column changes "do it now" |
| **Open owner questions** | (1) Staging retention on stamps: stamp storage has 7-day blob soft delete, so a deleted staged NDA stays recoverable ~14 days; accept or isolate staging (before customer rollout). (2) Copilot-door default for send-email-updates (column default No for now). (3) 006 live threaded-reply test send (recipient, shared mailbox) |
| **Unresolved (spec)** | (2) ADR-049 scope → `/adr-check` (090) |
| **External gates** | eBilling UAT (ebilling-r1 task 163) → 050; Model 2 registry import → before any Model 2 rollout |
| **CI** | PR #3253 draft, Auto-fix on, green at `d00ec01ca1`. Engine-image push leg is master-only (OIDC trusts master); a branch workflow_dispatch fails by design |
| **Commit hook gotcha** | `$env:PATH="C:\code_files\spaarke\node_modules\.bin;$env:PATH"` before `git commit`; message via `git commit -F <file>`; a bad pathspec in `git add` aborts the whole add |

## Standing directives
- Never `git stash`; never `python3`/`python -` heredocs in Bash (use the PowerShell tool); every Agent launch names a model.
- Don't edit other projects' worktrees; coordinate by message (accelerator "acclerator-r1", provisioning "provisioning-r1", access gate "uac-r2", ontology "ontology-r1", eBilling "ebilling-r1").
- Never put accelerator `answer-key/` or real NDAs where an agent can read them.
- No edits to `Services/Finance/Submission/` before eBilling UAT sign-off.
- Change accelerator engine code in `src/accelerators` when our requirements need it (never fork); tell acclerator-r1.
- New tests go under KEEP paths (`tests/CLAUDE.md`).
- Sub-agents commit only their own files by path; the main session owns TASK-INDEX.md, current-task.md and `.claude/`.
- Merge to master only with the owner's go-ahead; live Azure/Dataverse/email actions need the owner's OK; dev BFF deploys only via uac-r2 from master.
