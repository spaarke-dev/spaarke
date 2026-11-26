# Current Task State

> **Last Updated**: 2026-11-26 ~17:00Z (task 043 in progress; checkpoint at 85% context)
> **Format**: CURRENT state only. Each checkpoint REWRITES this file. Target ≤ 10 KB.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Active** | **Task 043** (`tasks/043-binder-fault-503-retry.poml`, FR-28 #2269), FULL rigor, opus @ high, directional. Owner go "Yes, start 043" (2026-11-26). Master merged into the branch first (`efefd0f542`). Gate checks: ebilling task-130 `017a965312` IS on master (ProjectPage fold goes ahead); /conflict-check: no open PR touches 043's files. |
| **043 progress** | DONE (uncommitted): BFF `CallerPrincipalResolver.cs` — `CallerPrincipalResolution.IsBinderFault` / `BinderFault(code)` / `BinderFaultDetail` (503, title "Service Unavailable", reasonCode+errorCode = code); CIAM branch returns it before the not-invited fold; workforce switch has a new `when IsBinderFault` case before the default. New test `tests/integration/auth/ExternalAccess/BinderFaultStatusTests.cs` (workforce 3 codes via mocked resolver + real home resolver lookup case; must-not-fire not-invited/removed/registration_required/owner-unconfigured 500/other 403s incl. contact_create_unavailable; CIAM real binder lookup + bind; host GET /me CIAM + workforce). Legacy `CallerPrincipalResolverTests.cs`: removed the "lookup failure stays 403" test and the "unreadable" 403 row (comments point to the new file). RUNNING: filtered dotnet test (background) over resolver/contract/binding suites — fix any test that pinned 403 for the three codes. |
| **043 remaining** | (1) Fix failing tests from the run; (2) seeding proof (map one code back to 403, see its test fail, restore; record); (3) SPA: `me-client.ts` binder-503 retry 1/2/4 s ×3 + message "We couldn't finish signing you in. Try again" + Retry; `fetchMeEntitlements` must not degrade to plane defaults for the three codes; audit callers (fetchMeEntitlements, getExternalUserContext/WorkforceHomeGate, BffAccessState, AuthGuard) and list each in notes; new test `src/client/external-spa/tests/me-client.binderFaultRetry.test.tsx` (fake timers; don't touch no-mock-identity.test.tsx or widgetRegistry.ts); (4) ProjectPage: remove the 404 branch, 404 falls to 403 handling, update ProjectPage tests; (5) build, ArchTests, publish-size delta vs fresh master (short-path worktrees), CVE; (6) code-review + adr-check + TWO adversarial passes (auth); (7) UAC review via uac-r2 (send the diff summary); (8) notes/task-043-notes.md, TASK-INDEX ✅, POML completed, drift check; PR. |
| **In flight** | **PR #3203 MERGED** 2026-11-26 04:05:34Z as `d86c1ebeda`; #2449 and #2155 closed; R3 defer notes flipped (R3 `23747014ed`). SHA sent to uac-r2 (in its BFF candidate `140c01219a`, owner go pending there) and provisioning-r1 (now owns R3 task 073 / ACME; prod workforce client + consent procedure are with the owner). SPA deploy DONE (see Live on dev). BFF `140c01219a` gate passed. Next: task 043 via task-execute, on the owner's go. PR #3180 (041) and #3160 (040) MERGED. |
| **Next action** | Read the background test run result; fix failures; then continue "043 remaining" from (2). SPA deploys need the owner's exact-SHA go; dispatching from a tag (`external-spa-dev-<sha>`) builds exactly that SHA (used 2026-11-26). |

## Task 042 outcome
- BFF: the workforce 403 `registration_required` prefills `email` with `ExternalRegistrationEndpoints.TokenEmail` (the registration's rule: the email claim only, one value). `WorkforceCallerClaims.TokenEmail` unchanged. The licensedUser hint reads the same value.
- SPA: an empty prefill email opens on the email-missing screen (no form). New text: ask the organization's IT administrator to set the Email on the Microsoft Entra account, then sign in again. A **Sign out** button there (browser only; the shell's own sign-out, threaded App → gate → page).
- One adversarial pass: no fix-now runtime defect; 2 doc/comment fixes and the Sign out button applied; K1 filed as D-42 #3201.
- Publish size +201 bytes (≈ 43.6 MB). No CVE, no package change. Conflict check clean (35 open PRs).

## Live on dev
- **BFF:** master `140c01219a` LIVE since 2026-11-26 15:53:48Z (uac-r2, owner's go; healthz 200, smoke OK); it adds #3193 over `6a2a569ac9`. Task 042's BFF part (#3203 `d86c1ebeda`) has been live since uac-r2's `6a2a569ac9` build at 12:58Z. Rollback `6a2a569ac9`, then `1e64e94168`. 30-minute gate PASSED at 16:24Z (147 requests, 0 5xx; only known fail-closed container refusals on old dev data, #1506).
- **SPA:** master `24d53fb333` LIVE since 2026-11-26 ~15:50Z (run 45852469929, owner's exact-SHA go "Deploy 24d53fb333"). Contains tasks 040, 041, 042 and ebilling ISS-052. Master had moved to `51ce01a116` (no SPA changes) by dispatch time, so the run was dispatched from tag `external-spa-dev-24d53fb333` to build exactly the approved SHA; the run's headSha was checked. Rollback: tag `external-spa-dev-rollback-0267ee64a2` (then `3067fb3bdc`). Verified: the site loads the sign-in page, the bundle carries the FR-26 text and the Sign out prop, no console errors.

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
