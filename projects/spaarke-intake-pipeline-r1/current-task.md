# Current Task State — spaarke-intake-pipeline-r1

> **Last Updated**: 2026-11-25 (design review complete)
> **Phase**: design reviewed and decided. No spec, no tasks. Do NOT run `/design-to-spec` until the owner says go.

## Quick Recovery
| Field | Value |
|---|---|
| **Active work** | None in progress. `design.md` v0.3 holds every review decision (§4, §5, §7, §3.5, §13, §10.1) |
| **Next action** | Wait for the owner's go, then `/design-to-spec` |
| **Coordination owed** | (1) Tell the accelerator project its design §6.3 "C# twins in the BFF" plan changes: the server hosts the same Python engines in a Container Apps Job. (2) Provisioning needs a per-stamp Container Apps environment/Job/registry step. (3) eBilling: #1920 producer lands in our Phase 2 (comment posted) |
| **§10 evidence** | `notes/section10-verification-2026-11-25.md` |
| **Issues** | Filed #3162 (email ingestor seam, §10 item 5b). Commented #1920 (AttachmentClassification producer) |
| **Uncommitted** | none |

## Decided (owner)
- 2026-11-24: `sprk_servicerequest` is the request entity (no `sprk_legalrequest`). An Inquiry is a different ontology concept.
- 2026-11-25: own project (split from specialist-legal-agents-r1); run record = extend `sprk_processingjob`; don't force paths onto one model where it isn't the right technical answer.
- Q2: orchestration = BFF ADR-004 jobs + `sprk_processingjob` state machine; written ADR-052 reason in design §4. Provisioning control plane noted as the stronger Durable Task candidate (its own project's call).
- Q3: NDA engines = accelerator's Python engines in a Container Apps Job (pure engine, blob in/out, stamp MI, KEDA queue trigger); Phase 0 hosting spike; ADR-052:77 approval given. C# port rejected.
- Q4: invoice = second harness in Phase 3, after eBilling UAT (task 163), before customer rollout; no eBilling code edits until then.
- Q5: returns send from the shared legal mailbox, app-only, as the queued Return stage; attorney named + Cc'd.
- Q6: one NDA standard = accelerator "Spaarke NDA – Mutual" v1.2 (topic-keyed); KNW-011 rendered from it; owned by accelerator → specialist-legal-agents T3; we consume + version-stamp; counsel ratification gates customer rollout.
- Q7: email door = intake mailboxes (flag on `sprk_communicationaccount`) + suggest-only "Submit as request" elsewhere via the existing triage Action output. Both Phase 2.
- §10.1: Phase 0 one cleanup PR = 1b delete 3 dead handlers, 2 StartupDiagnostics, 4 profile doc, 5a idempotency keys + test, 6 ConsumerTypes, 7 reply threading via createReply, 8 ADR-036/data-model docs. Phase 1 = 3 DLQ re-drive in run admin view, 8 `sprk_backgroundjob` description. Phase 2 = 1a #1920 producer. 5b filed #3162.

## Standing directives
- Never `git stash`; never `python3` in Bash; every Agent launch names a model.
- Don't edit other projects' worktrees (accelerator, ebilling, ontology, mcp-r1, specialist-legal-agents); coordinate by message.
- Accelerator constraint: never put `answer-key/` or the real NDAs anywhere an agent can read.
- No edits to `Services/Finance/Submission/` (or other eBilling code) before eBilling UAT sign-off.
- Phase 0 cleanup is one PR with the real fixes; no hygiene-only PRs.
