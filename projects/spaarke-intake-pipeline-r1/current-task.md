# Current Task State — spaarke-intake-pipeline-r1

> **Last Updated**: 2026-11-27
> **Phase**: Phase 0 done except 008's BFF steps; Phase 1 started. Done: 001–007, 009–012. 008 🔄 (engine spike done; BFF steps wait for master). **013 in progress** (background agent `t013-orchestrator`, opus).

## Quick Recovery
| Field | Value |
|---|---|
| **Active** | 013 built, all local (not pushed): `104735c300`, `d494aba9a4`, `acea991584`, `04bcfcc205`, then `a27493413b` (sprk_createdbyperson column + role entry, live on dev: probe 403 → role Basic Read → 204, Verify 41/41), `68ce77e8af` (CreatorPerson guard `SecuredOnlyByDeployScript`), `312efb6e7b` (F1 heartbeat, seeding proof), `22d2ff128d` (notes). then the Task 182 entry (this checkpoint) |
| **Next action** | 013 ✅ (Task 182 path (b) chosen by uac-r2: `RoleSetNotInLineage` entry + `SecureChildLineageTests` mirror with staleness check; ArchTests 2845/2845). Follow-up **#3310**: the run does not follow a model-driven-app Assign of its request (MDA-only; server never re-owns a request; uac-r2 agreed, not for 013). Next: merge master INTO this branch, CI green, send uac-r2 the access diff vs merge base (incl. role entry, census waiver `RecordOwnerAssignmentCensusTests.cs:287-290`, #3310), wait for their answer, then master merge with owner go. Then Phase 1 waves: 014 + 017 |
| **Merge / deploy path (uac-r2, 2026-11-27)** | Dev BFF deploys only from a master SHA, named by the owner IN the uac-r2 session. Steps: (a) merge current master into this branch after 013, CI green, message uac-r2; (b) send uac-r2 the access-relevant diff vs merge base: EmailChannelSender createReply (mailbox, caller's right to reply), IncomingCommunicationProcessor, CommunicationService, CommunicationEnrichmentService, DocumentProcessingJobHandler removal, OfficeJobStatusService, IAgreementAi app-only (what it reads; callers must check record access), plus 013's ownership routing + role change; (c) merge to master with owner go; (d) uac-r2 offers the SHA to the owner. Then ping provisioning-r1 (ISS-046 starts once Phase 0/1 Bicep is on master) |
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
