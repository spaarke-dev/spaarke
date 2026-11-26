# Current Task State

> **Last Updated**: 2026-11-26 ~01:50Z (task 042 completed on the branch; PR pending)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Active** | None. Task 042 is ✅ completed (FR-26 #2155, FR-27 #2449); details in `notes/task-042-notes.md`. The next 🔲 in `TASK-INDEX.md` is the next task. |
| **In flight** | **PR #3203** (task 042) open, pushed at `2e1a02dec3`; auto-merge OFF: **merge only on the owner's go.** #2449 now has only the r4 label. PR #3180 (041) and #3160 (040) MERGED. No uncommitted files. |
| **Next action** | Owner said "Merge when green" for #3203 (auto-merge squash ON, 2026-11-26) and "yes deploy SPA when available" (= once #3203 merges). When #3203 is MERGED: (1) send uac-r2 the merge SHA (their next BFF candidate; owner gives the SHA go in their session); (2) name the exact SHA to the owner in this session and run `deploy-external-spa.yml` (workflow_dispatch, environment dev) only after they approve THAT SHA (agreed with uac-r2: "when available" does not cover a SHA that did not exist yet). If master has moved past the merge SHA, list what the newer commits add. Check that the run's head_sha equals the approved SHA. Rollback = `0267ee64a2`. Then verify the dev SPA (loads, no console errors). uac-r2 was told the SPA contents (R4 040/041/042, ebilling ISS-052, other projects' shared-UI changes). Then the next 🔲 in `TASK-INDEX.md` via task-execute. After the 042 merge: flip #2449 / #2155 in R3's `notes/defer-issues.md` (ISS entries at ~:692 and ~:815) to Done with the PR link. |

## Task 042 outcome
- BFF: the workforce 403 `registration_required` prefills `email` with `ExternalRegistrationEndpoints.TokenEmail` (the registration's rule: the email claim only, one value). `WorkforceCallerClaims.TokenEmail` unchanged. The licensedUser hint reads the same value.
- SPA: an empty prefill email opens on the email-missing screen (no form). New text: ask the organization's IT administrator to set the Email on the Microsoft Entra account, then sign in again. A **Sign out** button there (browser only; the shell's own sign-out, threaded App → gate → page).
- One adversarial pass: no fix-now runtime defect; 2 doc/comment fixes and the Sign out button applied; K1 filed as D-42 #3201.
- Publish size +201 bytes (≈ 43.6 MB). No CVE, no package change. Conflict check clean (35 open PRs).

## Live on dev
- **BFF:** master `1e64e94168` (tasks 040 + 041 BFF) LIVE since 2026-11-26 01:56:50Z (uac-r2, owner's go; healthz 200); rollback `aa933c09e6`. uac-r2's 30-minute gate PASSED at 02:27Z (146 requests, 0 5xx). Task 042's BFF part is not in it (uac-r2 reviewed #3203's BFF hunk: no objection; it rides the next candidate after the merge, on the owner's SHA go).
- **SPA:** `0267ee64a2` (rollback `3067fb3bdc`). Tasks 040, 041 (and 042 once merged) are NOT in it. Master has moved past `0267ee64a2`, including ebilling ISS-052 #3082 (`2ac250cae4`): list it in the next SPA deploy proposal. The SPA deploy needs the owner's go on an exact SHA; re-check master HEAD before running `deploy-external-spa.yml`.

## Owner questions open
- Track the raw filing-reference footer seen in an older PRJ-000046 system email? (Told to the owner; not filed. The repo is public.)
- Track the residual export-hardening gaps privately?
- Docx preview as an Idea?
- "The client is reviewing this invoice." beyond the Information field?
- `useHostRowSource` row in `.claude/patterns/CAPABILITY-INDEX.md` row 31 (`.claude` edit, owner approval)?

## Filed
- D-24 #2939 … D-33 #3010; D-34 #3045 done; D-35 #3046; D-36 #3050; D-37 #3129; D-38 #3149; D-39 #3153; D-40 #3154; D-41 #3159; D-42 #3201.

## Merge trap (seen 2026-11-22, again 2026-11-25)
Master holds older squashes of this project's notes. On a master merge, keep the branch's copies of `projects/spaarke-SPA-external-access-platform-r4/**` (`git checkout --ours`). Also: after a squash merge of this branch's PR, the next master merge conflicts on the same source lines; take ours only after checking that master's only commit to those files is that squash.
