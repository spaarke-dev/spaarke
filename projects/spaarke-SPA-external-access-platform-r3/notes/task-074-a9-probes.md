# Task 074: A9 live probes on dev (record)

> Masked evidence only: claim names and outcomes, initials, set/unset flags. No tokens, ids, emails or UPNs.
> Environment: dev BFF `136568cf70`; dev SPA from master `f07ab2ef1f` (run 40882434220, 2026-10-29 13:33Z; rollback tag `external-spa-dev-rollback-1fa0cf5cb8`).

## Status

| # | Probe (POML acceptance) | Result | Evidence |
|---|---|---|---|
| 1 | Member registration: new contact with tid/oid, WorkforceHome, owner = BU1 default team; workspace loads | ✅ 2026-10-20 | Tracy Cheatham registered via "Sign in with your company account" (`task-070-deploy-record.md`) |
| 2 | Partner sign-in | ✅ 2026-10-20 | Owner (`task-070-deploy-record.md`) |
| 3 | Licensed user: stored link; /me resolves to the systemuser | ✅ 2026-10-29 | §1 below |
| 4 | Resubmit: no second contact | pending | Needs a sign-in as the registered member (Tracy Cheatham) |
| 5 | Existing-record bind after an invitation; no duplicate | pending | Needs a Dewey Cheatham member with an email address and no contact; staff invite through Grant Access |
| 6 | Unlisted directory → 401 at the BFF and the not-set-up page | pending | Needs an account in a directory dev does not list |
| 7 | The Dewey Cheatham member token carries `email` | ✅ 2026-10-28 | `contact-identity-dev-rollout-record.md` §3 (customer-directory token probe: `email` present, `xms_edov` true) |
| 8 | Intake submit as the new contact | pending | Needs a sign-in as the registered member |

## 1. Licensed user, stored link (2026-10-29)

- **Before (read-only Dataverse):** one systemuser carries a Dewey Cheatham home link: initials RD, guest UPN, `sprk_hometenantid` = Dewey Cheatham, `sprk_homeobjectid` set, enabled. The link was written 2026-10-21 02:17Z, during the owner's smoke test after that night's SPA redeploy.
- **Owner run:** private window → dev SPA → "Sign in with your company account" as Ralph Dewey. The workspace loaded with the workforce modules (Quick Start, Policy Library, Service Requests). There was no profile form, no link step and no popup. The bell lists the owner's message and grant items.
- **Server (App Insights, 13:41–13:47Z):** 19 × `[WF-AUTH] Resolved home tid=… oid=… to linked systemuser …`, 0 resolutions to a WorkforceHome contact, 0 `[WF-AUTH]` denials. Every `/api/v1/external/*` request in the window answered 200: `me`, `me/entitlements`, `notifications`, `api/dataverse/fetch`, `metadata` and `record`.
- The run came after the C-1 revoke (`task-059-dev-setup-record.md`, 13:39Z), so removing the dev BFF's own grants in Dewey Cheatham does not affect workforce sign-in.
- **Not a probe finding:** two `GET /api/v1/external-access/can-manage-access` requests at 13:03Z (a staff route) carried no `[WF-AUTH]` line. They came from a staff session, 38 minutes before this run.
- **Tooling note:** an Azure CLI token for the dev BFF as a Dewey Cheatham user fails with AADSTS65001: the Azure CLI has no consent for the BFF in that directory. Probes that need a home token outside the browser therefore need that user's own consent first.
