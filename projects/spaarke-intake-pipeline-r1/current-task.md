# Current Task State — spaarke-intake-pipeline-r1

> **Last Updated**: 2026-11-25 (Wave 0-1 closed)
> **Phase**: Phase 0 executing. Wave 0-1 done (002, 003, 005, 007). Next: Wave 0-2.

## Quick Recovery
| Field | Value |
|---|---|
| **Done** | 007 (ADR-004 A2, PR #3191 merged `d148580e15`), 003 (engine image CI, `554862fc68`), 002 (Bicep, `2aceaa97bb`, `deployIntakeEngine=false` by default), 005 (cleanup fixes, `534724704b` + `78783a5e36`; publish 41.608 MB, −0.006 MB). Stale handler references fixed in the same branch. BFF build 0/0; ArchTests 120/120; drift check clean |
| **Open owner question** | Engine job identity: dedicated least-privilege UAMI (recommended — the job parses untrusted .docx from any intake sender with lxml; the stamp UAMI is the BFF identity with namespace-wide Service Bus rights and Graph/Dataverse/KV/OpenAI roles) vs the stamp UAMI as 002 built it. `intake-engine-job.bicep` takes the identity as parameters, so dedicated = new UAMI + its grants in `customer.bicep`. Ask before task 008 deploys |
| **Next action** | Wave 0-2 = 004 (app-only edit plan; needs PR #3163 on master) + 006 (cleanup part 2: item 7 threaded reply + docs part of 8; same PR as 005). Check #3163 state first; if not merged, run 006 alone. Then 001 (engine container) after #3163 |
| **Unresolved (spec)** | (1) Clean copy with prior markup → task 008 spike with the accelerator; (2) ADR-049 scope → `/adr-check` (090); (3) ontology columns on sprk_servicerequest → before 010 |
| **External gates** | PR #3163 (engines on master) → 001, 004; eBilling UAT (ebilling-r1 task 163) → 050; ontology coordination → 010; Model 2 registry import (provisioning) → before any Model 2 rollout |
| **Issues/PRs** | #3191 (A2, merged), #3162, #3200 (sidecar Trivy header, filed), #1920 (task 042), portfolio #3128 |
| **Task 008 needs** | Registry choice is recorded (platform ACR, `intakeEngineGrantAcrPull=false` + AcrPull granted there); CI push leg needs the federated credential for the master subject + AcrPush (the workflow header lists them); first CI run is 003's acceptance criterion 5; tighten the 400 MB size ceiling after the first measured size |

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
