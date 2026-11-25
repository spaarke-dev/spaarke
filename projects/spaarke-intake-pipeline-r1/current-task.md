# Current Task State — spaarke-intake-pipeline-r1

> **Last Updated**: 2026-11-25 (spec.md written by /design-to-spec)
> **Phase**: spec written, awaiting owner review. Next: `/project-pipeline spaarke-intake-pipeline-r1` only on the owner's go.

## Quick Recovery
| Field | Value |
|---|---|
| **Active work** | `/design-to-spec` Step 5: owner reviews `spec.md` (22 FRs, 10 NFRs, Phases 0–3) |
| **Next action** | Wait for owner: `y` → `/project-pipeline`; `edit` → change spec; `done` → stop. Also offer to add the `<hot-path-declaration>` block + Placement Justification section to design.md (project-pipeline Step 3 warns without them) |
| **Files** | `spec.md`, `notes/reuse-scan.md`, `notes/section10-verification-2026-11-25.md`, design.md v0.3 |
| **Unresolved (spec)** | (1) Clean copy with prior markup: engine accepts only our revisions, or omit Clean (Phase 0 spike + accelerator); (2) ADR-049 scope confirm in /adr-check; (3) ontology columns on sprk_servicerequest before FR-05 |
| **ADR amendment** | ADR-004 A2 drafted on this branch (owner path B). Merges before/with FR-01 container code |
| **Issues** | #3162 filed (email ingestor seam). #1920 commented (producer in Phase 2). Portfolio issue #3128 |
| **Prereqs** | PR #3163 (accelerator engines on master) before FR-01 image; eBilling UAT (task 163) before Phase 3 |

## Decided (owner) — full table in spec.md "Owner Clarifications"
- Request entity `sprk_servicerequest`; run record = extend `sprk_processingjob`.
- Q2 BFF jobs + run record (ADR-052 written reason). Q3 Python engines in a Container Apps Job (approved). Q4 invoice Phase 3 after eBilling UAT. Q5 shared legal mailbox. Q6 accelerator NDA standard v1.2, pinned. Q7 intake mailboxes + Job B suggest-only.
- §10.1 cleanup: Phase 0 one PR (1b,2,4,5a,6,7,8-docs); Phase 1 (3, 8-description); Phase 2 (1a); 5b → #3162.
- Spec interview: r1 = Phases 0–3; approve from Console Work Item AND MDA form (one endpoint); team queue then claim; manual triage for no-harness; intake senders = anyone (+ per-sender cap); preserve all prior redlines/comments; status on every step + Decision Record for decisions + per-request "send email updates"; suggestion = Job B proposal kind.

## Standing directives
- Never `git stash`; never `python3` in Bash; every Agent launch names a model.
- Don't edit other projects' worktrees; coordinate by message (accelerator session: "acclerator-r1").
- Never put accelerator `answer-key/` or real NDAs where an agent can read them.
- No edits to `Services/Finance/Submission/` before eBilling UAT sign-off.
- Don't copy the accelerator engines; build from `src/accelerators` on master; Python 3.11, stdlib + lxml.
- Phase 0 cleanup is one PR with the real fixes; no hygiene-only PRs.
