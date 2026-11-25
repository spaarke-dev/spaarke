# Current Task State

> **Last Updated**: 2026-11-25 ~21:30Z (task 041 completed locally; not pushed)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Active** | None. Task 041 is ✅ completed (FR-25, #2192); details in `notes/task-041-notes.md`. The next 🔲 in `TASK-INDEX.md` is the next task. |
| **Unpushed on purpose** | The 041 commits (`026ff0396f`, `f3da9c8f7a`, close commit) are LOCAL ONLY. PR #3160 (task 040) is open on this same branch with auto-merge on, by the owner's go for 040 only. Pushing now would fold 041 into it. **After #3160 merges:** merge origin/master here (keep the branch's `projects/spaarke-SPA-external-access-platform-r4/**`), push, and open a separate 041 PR (Placement Justification, publish-size delta, the UAC review and the timeout decision in the body). Merge only on the owner's go. |
| **In flight** | PR #3160 (040): auto-merge on. After it merges, the dev deploy: the BFF through uac-r2 (owner go on the exact merged SHA, in uac-r2's session) and the SPA (owner go). |
| **Next action** | Wait for #3160 to merge (do not poll; use `mcp__ccd_pr__get_status`). Then push 041 and open its PR. Then continue with the next task via task-execute. |

## Task 041 outcome
- BFF: the workforce 403 `registration_required` carries a `licensedUser` boolean (the caller's own token email, the registration route's rule, read and decision; any doubt → false). SPA: `RegistrationPage` opens on "Link my account" when it is true; the popup opens only on the click.
- Popup timeout: uac-r2 decided (a), keep msal's 60 s default (`popupBridgeTimeout`); no change. UAC review: no objection (recorded in the notes).
- Two adversarial passes: no F-class. One comment fix and one test added. K-limits recorded (an extra Dataverse read per unregistered request; probes skipped; an unverified email gives the same answer as the POST).
- Publish size +1,401 bytes (≈ 43.6 MB). No CVE. ArchTests 2831/2831. The conflict check is clean.

## Task 040 outcome (details in notes/task-040-notes.md)
- `assigned-work` for a workforce contact with ≥ 1 readable project/matter/WA. The SPA's record tabs use `RECORD_TAB_GATE`. Submit invoice is hidden on the workforce plane (F1, owner 2026-11-25); the question went to ebilling as D-41 #3159.

## Live on dev
SPA `0267ee64a2` (rollback `3067fb3bdc`); BFF `aa933c09e6` since 2026-11-25 21:49:11Z (rollback `a1b7db1def`; uac-r2 deploys; owner go per SHA; uac-r2's 30-minute gate PASSED at 22:20Z). Tasks 040 and 041 are not in it. Master has moved past `0267ee64a2`, including ebilling ISS-052 #3082 (`2ac250cae4`): list it in the next SPA deploy proposal.

## Owner questions open
- Track the raw filing-reference footer seen in an older PRJ-000046 system email? (Told to the owner; not filed. The repo is public.)
- Track the residual export-hardening gaps privately?
- Docx preview as an Idea?
- "The client is reviewing this invoice." beyond the Information field?
- `useHostRowSource` row in `.claude/patterns/CAPABILITY-INDEX.md` row 31 (`.claude` edit, owner approval)?

## Filed
- D-24 #2939 … D-33 #3010; D-34 #3045 done; D-35 #3046; D-36 #3050; D-37 #3129; D-38 #3149; D-39 #3153; D-40 #3154; D-41 #3159.

## Merge trap (seen 2026-11-22, again 2026-11-25)
Master holds older squashes of this project's notes. On a master merge, keep the branch's copies of `projects/spaarke-SPA-external-access-platform-r4/**` (`git checkout --ours`).
