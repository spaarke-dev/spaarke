# Current Task State — spaarke-intake-pipeline-r1

> **Last Updated**: 2026-11-28
> **Phase**: Phase 0 done except 008's BFF steps; Phase 1 started. Done: 001–007, 009–014, 017 (014 and 017 on the branch, not yet on master). 008 🔄 (BFF steps now unblocked: on master). **PR #3253 merged to master** at `a7712744d4` (2026-11-28, owner go).

## Quick Recovery
| Field | Value |
|---|---|
| **Active** | None in progress. 001–013 are on master (`a7712744d4`). uac-r2 cleared the whole access diff (items 1–7), incl. the reply-parent check `b7b6926ffd` (app-only threaded reply needs a parent the caller can Read; `ReplyParentSetByServer` server-only flag for FR-20; timing difference accepted as K-class). Codex P2 fixed in `d83e269b5d` (stage marked completed when its steps were stored by an engine completion) |
| **Next action** | **017 ✅** on the branch (Decide-lane approval: intake Signal via the evaluate path, claim route `POST /api/v1/intake/requests/{id}/claim`, `approve-intake`/`reject-intake` executors; 2 review passes; notes `notes/017-approval.md`). Send the access diff to uac-r2 and the Signals/catalog/Api-Signals parts to ontology-r1, with 3 open questions: (1) an execute-time refusal on the commit route answers 500 not 403 (amend criterion 7, or change the shared route = ontology sign-off); (2) Work Item owner (BU default team) vs claim check (request's owning team); (3) raising an Acted Signal again for a second cycle (#3340, must land before doors 021/022/043/044). Not built in 017: unclaim, the MDA approve/reject entry (018), WorkType for the Console card (019). Policy seed `scripts/intake/Apply-IntakeApprovalPolicy.ps1` written, not run (owner go). Next: revise 018/019 POMLs for the Decide lane, then wave 016, 023, 018, 019. **014** dev deploy needs `ServiceBus__IntakeEngineCompletionQueueName=intake-engine-completions`, `IntakePipeline__Engine__StagingBlobEndpoint=https://sprkspaarkedevebillsa.blob.core.windows.net`, and Storage Blob Data Contributor on `intake-staging` for the BFF identity (owner OK). 015 must use a new, higher dispatch attempt on re-drive and name engine steps by operation (014 notes). Dev BFF deploy of `a7712744d4`: uac-r2 sends a LIVE notice |
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
