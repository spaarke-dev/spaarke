# Current Task State — spaarke-intake-pipeline-r1

> **Last Updated**: 2026-11-25 (Wave 0-2 = 006 done; engine identity + completion queue reworked)
> **Phase**: Phase 0 executing. Done: 002, 003, 005, 006, 007. Blocked on #3163: 001, 004.

## Quick Recovery
| Field | Value |
|---|---|
| **Done** | 007 (ADR-004 A2, PR #3191 merged `d148580e15`), 003 (engine image CI, `554862fc68`), 002 (Bicep, `2aceaa97bb`, `deployIntakeEngine=false` by default), 005 (cleanup fixes, `534724704b` + `78783a5e36`; publish 41.608 MB, −0.006 MB). Stale handler references fixed in the same branch. BFF build 0/0; ArchTests 120/120; drift check clean |
| **Decided (owner, 2026-11-25)** | Engine job runs as its own identity `uamiIntakeEngine` (never the BFF/stamp UAMI); completions go to its own `intake-engine-completions` queue, consumed by a dedicated BFF `IntakeEngineCompletionProcessor` (task 014, `CommunicationJobProcessor` pattern) that treats completions as untrusted. Bicep reworked; customer.json regenerated; ArchTests 2831/2831, control-plane tests 3678 pass. Tasks 001/002/014/023 + spec/design/CLAUDE.md updated |
| **Task 006** | Done (`5e22bb7a83`, `611f9afd72`): threaded reply via Graph createReply, 15 tests incl. seeded failure, publish −0.001 MB. **Pending owner:** the live test send (needs dev deploy OK, a named test recipient, the dev shared mailbox). Unproven: the server-send marker riding on the createReply message (Learn does not list `internetMessageHeaders` as PATCH-able on a draft); if Exchange drops it, server-send replies with attachments get re-filed by the inbound path. Not built (owner decision if wanted): reply-all; attachments > 3 MB (413 `reply_attachment_too_large`, no upload session). Notes: notes/006-threaded-reply.md |
| **Next action** | The Phase 0 cleanup PR (005 + 006) can open once you OK it; the live 006 send follows a dev deploy. 001 and 004 wait on PR #3163 |
| **Unresolved (spec)** | (1) Clean copy with prior markup → task 008 spike with the accelerator; (2) ADR-049 scope → `/adr-check` (090); (3) ontology columns on sprk_servicerequest → before 010 |
| **External gates** | PR #3163 (engines on master) → 001, 004; eBilling UAT (ebilling-r1 task 163) → 050; ontology coordination → 010; Model 2 registry import (provisioning) → before any Model 2 rollout |
| **Issues/PRs** | #3191 (A2, merged), #3162, #3200 (sidecar Trivy header, filed), #1920 (task 042), portfolio #3128 |
| **Provisioning** | #3210 sent to session `provisioning-r1`: H2a params/outputs, platform-ACR AcrPull, H4b app setting, `Microsoft.App` readiness, H13 probe, Model 2 import. Awaiting their reply on names |
| **Task 008 needs** | `intakeEngineRegistryLoginServer` = platform ACR login server (module no longer looks the registry up in the stamp RG: that lookup would have failed for the platform ACR); AcrPull on the platform ACR for the engine identity (output `intakeEngineIdentityObjectId`); the BFF app setting `ServiceBus__IntakeEngineCompletionQueueName=intake-engine-completions` on dev (the processor is off without it); registry choice is recorded (platform ACR); CI push leg needs the federated credential for the master subject + AcrPush (the workflow header lists them); first CI run is 003's acceptance criterion 5; tighten the 400 MB size ceiling after the first measured size |

## Decided 2026-11-25 (owner: "take the correct technical approach"; "ensure consistent with other components")
- Engine wrapper uses hash-pinned azure-identity / azure-servicebus / azure-storage-blob (+ azure-monitor-opentelemetry). The engines stay stdlib + lxml.
- Engine job matches the BFF job conventions (DLQ reason codes, PriorDeliveryCount retry copies, message properties, OTel → App Insights, sidecar Dockerfile header format).
- Engine image registry = platform control-plane ACR for dev/Model 1; Model 2 needs an import at provisioning.
- Full log: notes/decisions.md. Found issues: notes/defer-issues.md.

## Standing directives
- Never `git stash`; never `python3` in Bash; every Agent launch names a model.
- Don't edit other projects' worktrees; coordinate by message (accelerator session: "acclerator-r1").
- Never put accelerator `answer-key/` or real NDAs where an agent can read them.
- No edits to `Services/Finance/Submission/` before eBilling UAT sign-off.
- Don't copy the accelerator engines; build from `src/accelerators` on master; Python 3.11; the engines use stdlib + lxml, and only the wrapper layer adds hash-pinned Azure SDKs.
- Phase 0 cleanup is one PR with the real fixes (005 + 006); no hygiene-only PRs.
- New tests go under KEEP paths (`tests/CLAUDE.md`).
- Sub-agents in this worktree commit only their own files by path; the main session owns TASK-INDEX.md, current-task.md and anything under `.claude/`.
- Merge to master only with the owner's go-ahead; live Azure/Dataverse/email actions need the owner's OK.
