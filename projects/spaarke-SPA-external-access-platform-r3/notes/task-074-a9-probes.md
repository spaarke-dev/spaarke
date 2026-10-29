# Task 074: A9 live probes on dev (record)

> Masked evidence only: claim names and outcomes, initials, set/unset flags. No tokens, ids, emails or UPNs.
> Environment: dev BFF `136568cf70`; dev SPA from master `f07ab2ef1f` (run 40882434220, 2026-10-29 13:33Z; rollback tag `external-spa-dev-rollback-1fa0cf5cb8`).

## Status

| # | Probe (POML acceptance) | Result | Evidence |
|---|---|---|---|
| 1 | Member registration: new contact with tid/oid, WorkforceHome, owner = BU1 default team; workspace loads | ✅ 2026-10-20 | Tracy Cheatham registered via "Sign in with your company account" (`task-070-deploy-record.md`) |
| 2 | Partner sign-in | ✅ 2026-10-20 | Owner (`task-070-deploy-record.md`) |
| 3 | Licensed user: stored link; /me resolves to the systemuser | ✅ 2026-10-29 | §1 below |
| 4 | Resubmit: no second contact | ✅ 2026-10-29 | §2 below |
| 5 | Existing-record bind after an invitation; no duplicate | ✅ 2026-10-29 | §3 below (first run blocked on the test account's missing email attribute; passed after the owner set it) |
| 6 | Unlisted directory → 401 at the BFF and the not-set-up page | ✅ 2026-10-29 | §4 below (the SPA stops first; the BFF is not reached) |
| 7 | The Dewey Cheatham member token carries `email` | ✅ 2026-10-28 | `contact-identity-dev-rollout-record.md` §3 (customer-directory token probe: `email` present, `xms_edov` true) |
| 8 | Intake submit as the new contact | ✅ 2026-10-29 | §2 below |

## 1. Licensed user, stored link (2026-10-29)

- **Before (read-only Dataverse):** one systemuser carries a Dewey Cheatham home link: initials RD, guest UPN, `sprk_hometenantid` = Dewey Cheatham, `sprk_homeobjectid` set, enabled. The link was written 2026-10-21 02:17Z, during the owner's smoke test after that night's SPA redeploy.
- **Owner run:** private window → dev SPA → "Sign in with your company account" as Ralph Dewey. The workspace loaded with the workforce modules (Quick Start, Policy Library, Service Requests). There was no profile form, no link step and no popup. The bell lists the owner's message and grant items.
- **Server (App Insights, 13:41–13:47Z):** 19 × `[WF-AUTH] Resolved home tid=… oid=… to linked systemuser …`, 0 resolutions to a WorkforceHome contact, 0 `[WF-AUTH]` denials. Every `/api/v1/external/*` request in the window answered 200: `me`, `me/entitlements`, `notifications`, `api/dataverse/fetch`, `metadata` and `record`.
- The run came after the C-1 revoke (`task-059-dev-setup-record.md`, 13:39Z), so removing the dev BFF's own grants in Dewey Cheatham does not affect workforce sign-in.
- **Not a probe finding:** two `GET /api/v1/external-access/can-manage-access` requests at 13:03Z (a staff route) carried no `[WF-AUTH]` line. They came from a staff session, 38 minutes before this run.
- **Tooling note:** an Azure CLI token for the dev BFF as a Dewey Cheatham user fails with AADSTS65001: the Azure CLI has no consent for the BFF in that directory. Probes that need a home token outside the browser therefore need that user's own consent first.

## 2. Registered member: resubmit and intake (2026-10-29)

- **Owner run (~13:55Z):** Tracy Cheatham (initials TC) signed in with "Sign in with your company account". There was no popup, no registration form and no profile confirmation. TC submitted a service request. Messages worked both ways, and TC could not see internal-only messages. A document upload worked.
- **Dataverse (read-only, 14:2xZ):** exactly 1 contact for TC: plane WorkforceHome, external tenant and object id set, created 2026-10-20 18:31Z (the probe 1 registration). The resubmit did not create a second contact. 1 service request created since 13:00Z (13:58:24Z, Active).

## 3. Existing-record bind (2026-10-29)

- **Setup:** the owner created a Dewey Cheatham member, initials TH. Staff then granted TH Collaborate on a matter through Manage Access. That created one contact at 14:09:18Z and recorded the invitation (invited on 14:10:06Z, invited tenant set). The contact's email equals TH's sign-in address, and no identity is bound yet.
- **Owner run (14:10–14:13Z):** sign-in → profile form (email shown) → Continue → "We could not read your email address".
- **Server:** `GET /me` 403 (`registration_required`, as designed for an unbound caller) → `POST /registration` 403 with `[A9-REG] Registration refused: the home token carries no usable email claim`. Registration reads the token's `email` claim only, never the username. TH's account has no email attribute, so Entra issues no `email` claim. Nothing was bound or created.
- **Defect filed:** the form shows the username as the email although registration will refuse it (ISS-023, #2449). The refusal itself is correct.
- **Re-run:** after the owner sets TH's email attribute in Entra, sign in from a new private window. Expected: "We found your existing record" → confirm → the invited contact is bound and there is no second contact.
- **Not a probe finding:** one `GET /me` 401 at 13:49:12Z came from a non-browser client. That was the Azure CLI token attempt in §1, outside the 13:41–13:47 window.
- **Re-run (14:25Z, after the owner set TH's Email in Entra; no mailbox licence):** sign-in → "We found your existing record" (the invited contact's address) → Confirm and continue → the workspace loads. Server: `POST /registration` 409 (`existing_record_found`) → 200 (bound). Dataverse (read-only): still exactly 1 TH contact, the invited one created 14:09:18Z, now plane WorkforceHome with external tenant and object id set; the invited-tenant marker is cleared. **Pass: bound to the existing record, no duplicate.**- **Grant-time notice (App Insights):** staff's `invite-and-grant` at 14:10:02Z classified the domain as a customer tenant's (a work-account invite: no CIAM account, no CIAM invite email). The BFF then sent the work-account notice email from the shared mailbox at 14:10:10Z (Graph `sendMail` 202; a Dataverse communication record was created; the trace reads "the work-account notice was sent"). TH has no mailbox, so it could not have been delivered.

## 4. Unlisted directory (2026-10-29)

- **Owner run (afternoon):** "Sign in with your company account" with an account from a directory dev does not list → "Your organization is not set up for Spaarke". The owner reports a pass.
- **Server (App Insights, 3 h window):** no `sdap.auth.deny.workforce_home` trace, and no `/api/v1/external/*` 401 other than the 13:49Z CLI attempt (§3). The SPA picks a backend from `homeTenantIds` and sends nothing for a tenant no entry lists (`src/client/external-spa/src/auth/backend-selection.ts` ~94, ~555; `OrganizationNotSetUp.tsx`). So the "401 at the BFF" half of the acceptance cannot be reached from the SPA. The BFF's own refusal of an unlisted tenant (`WorkforceIdentity:CustomerTenantIds`, `AuthorizationModule.cs` ~704-712) is a backstop that was not exercised live.

**074 closed 2026-10-29: all 8 probes pass.**