# Current Task State — spaarke-intake-pipeline-r1

> **Last Updated**: 2026-11-25 (design review in progress)
> **Phase**: design review with the owner. No spec, no tasks. Do NOT run `/design-to-spec` until the owner says go.

## Quick Recovery
| Field | Value |
|---|---|
| **Active work** | Owner design review of `design.md`: §13 questions one at a time, then §10 cleanup (fix in Phase 0 or file an issue) |
| **Done in review** | Q2 orchestration host = A (BFF jobs + run record); Q3 NDA engines = A (Python in a Container Apps Job, with conditions) |
| **Next action** | Q4 invoice timing (move onto the pipeline as phase 3 after eBilling UAT, or earlier) |
| **Remaining** | Q4 invoice timing, Q5 sending mailbox, Q6 NDA standard owner, Q7 email door scope; then §10 items 1–8 |
| **Coordination owed** | Tell the accelerator project its design §6.3 "C# twins" plan changes (server hosts the same Python engines); provisioning needs a per-stamp Container Apps step |
| **§10 evidence** | `notes/section10-verification-2026-11-25.md` (all 8 re-checked on master 4af7aaecb5) |
| **Uncommitted** | none after each decision commit |

## Decided (owner)
- 2026-11-24: `sprk_servicerequest` is the request entity (no `sprk_legalrequest`; legal-front-door-r1 not going forward). An Inquiry is a different ontology concept.
- 2026-11-25: own project (split from specialist-legal-agents-r1); run record = extend `sprk_processingjob`; don't force paths onto one model where it isn't the right technical answer; document and clean up the code-review findings (design §10).
- 2026-11-25 Q2: orchestration host = BFF ADR-004 jobs + `sprk_processingjob` state machine; written ADR-052 reason in design §4. Provisioning control plane noted as the stronger Durable Task candidate (its own project's call).
- 2026-11-25 Q3: NDA engines = accelerator's Python engines in a Container Apps Job (pure engine, blob in/out, stamp MI, KEDA queue trigger); Phase 0 hosting spike; owner approval under ADR-052:77 given. C# port rejected.

## Standing directives
- Never `git stash`; never `python3` in Bash; every Agent launch names a model.
- Don't edit other projects' worktrees (accelerator, ebilling, ontology, mcp-r1, specialist-legal-agents); coordinate by message.
- Accelerator constraint: never put `answer-key/` or the real NDAs anywhere an agent can read.
- Commit and push after each review decision.
