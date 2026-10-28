# Task 193: one "Add person" entry (#2338): notes

Branch `fix/uac-193-add-person` (worktree C:/wt193), based on 078f62bcad (R3 #2360 merged).

## Runs
| Run | Head | What |
|---|---|---|
| A | fee1056ad2, 1c3b5b3360 | One contact → user rule (`ContactUserRepresentation`); the materializer, notifier and personal To Do assignee use it |
| B | ea01dc6f66 | `InternalUserShareCore` extracted from /share-user; caller-rights probe uses the gate question, with equalised timing |
| C | 78f5e7124b | /grant and /invite-and-grant decide share vs grant (`ContactInternalUserShare`) |
| C-fix | 298cbf57b9 | Required decision service; emails removed from two log lines |
| C-fix2 | 0427011e16 | Expiry ruling (c); no emails in any invite log template |
| D1 | 200bf9b89f | Modal: one "Add person" lookup; renders share vs grant |
| D1-fix | d6d78cde4b | /invite-and-grant → /grant parity; dead 503 branch removed; host tsc passes |
| D1-fix2 | 833fddbcdd | A shared contact is no longer offered (ledger adopt + Available hide) |
| D2 | a7d0f8186d | TrackingFieldTrio 1.0.55, builds, guide |

## Behaviour changes (owner sign-off table for the PR)
| Case | Before | After |
|---|---|---|
| Materializer: an oid linked to two contacts | — | Contact grant (the oid is not trusted as one user) |
| Materializer: the oid read fails | — | Nothing written (link unreadable) |
| To Do assignee: inactive contact | — | No single user |
| To Do assignee: oid-bound, unlinked, confirmed user | — | That user |
| To Do assignee: contact read fails | — | Throws (fail closed) |
| /grant on a contact representing exactly one internal user | Contact grant | Share at the requested level; `sharedWithUser`; no grant row or invite |
| …representing several users | Contact grant | 409 `contact_represents_several_users`; nothing written |
| …a disabled user, or a non-person | Contact grant | 422 (the share core's codes); nothing written |
| …a flagged-external user, on a Restricted record | Refused (Restricted) | 422 `user_not_internal`; nothing written |
| …representation unreadable | Contact grant | 503 `contact_representation_unreadable`; nothing written |
| Share requested with an expiry | (n/a) | Shared with no end date; `expiryNotApplied: true`; the modal says so (same as picking the user directly) |
| /invite-and-grant for an email resolving to one active contact that represents a user | CIAM invite + grant | Share; `onboardStatus: SharedWithInternalUser`; no CIAM account, email or grant row |
| Modal | "+ Contact" / "+ User" | One "Add person" lookup; on a Restricted record, users only |
| A No Access entry naming the contact or its firm, on a NON-secure record, contact representing a user | 422 `grantee_denied` | Unchanged: still 422, nothing written (V-fix; an explicit deny is honoured on the contact route; a direct /share-user pick keeps the owner's Q4 rule) |
| Visibility in the workforce SPA/Teams of a colleague added through their contact | Visible (contact grant) | **OWNER RULING PENDING:** share only = MDA access, not visible in the SPA while ImpersonatedRootSets is off (same as a direct user pick) |

## Security verifiers (a7d0f8186d)
- A (server): ready. Authorization parity with /share-user verified mechanically (the core diff is 2 lines). Fail-closed holds.
- B (client/contract): not ready. F1-a: the children_incomplete share path drops sharedWithUser, expiryNotApplied and the adopt. F1-b: SPA visibility (owner).
- V-fix round: F1-a; the delegation-filter timing oracle (#2334) fixed in scope; No Access on the contact route; no emails in ExternalInviteService logs or exception messages; a ceiling test on /grant; "Shared with X" wording; stale comments; guide line 212.
- Link columns (`contact.sprk_externalobjectid`, `systemuser.sprk_primarycontact`) are field-secured, with the BFF as the only writer (task 141, `Set-ContactIdentityBindingSchema.ps1`). That bounds who can make a contact "represent" a user (System Administrators excepted).

## Known limits
- K: Available hides a represented-and-shared role contact only through its Assigned-To ledger entry. A role contact with no ledger row (the Assigned-To sync never ran on that record) stays offered after a share to its colleague. Re-adding it is harmless: /grant shares again at the same level and names the colleague.
- K: A ledger-adopt failure after a confirmed share keeps the share and returns 200. It is logged at Error through the materializer's shared mark path (no email, no name).
- K: The SpaarkeMaster solution-export copies of the TrackingFieldTrio bundle and the `sprk_spaarkeai` web resource update at the next release export, not on this branch.
- K: a stale ledger link (contact relinked from user A to user B while A holds a share) briefly hides the candidate, until the next Assigned-To sync.
- K: re-adding a represented contact that already holds a contact grant shares and leaves the grant row as it was (still visible and revocable in Current Access).
- K: the /invite Retry doesn't ask who the contact represents (rare: the contact gets linked between the grant and the Retry).
- Filed: #2371 (inactive contact: /grant writes a dormant row; the contact-side route refuses); #2377 (an internal user's email with no active contact still gets a CIAM invite).

## Gates (D1-fix2 / D2)
Unit (touched): 8921/0/2 · ArchTests 1577 · BFF integration 186/0/8 · Spe integration 353/0/23 · jest (modal) 306 · lint ratchet ok · PCF build PASS (1.03 MiB, +9.4 KB) · no CVE. Publish delta: +7.4 KB at Run C; re-measure before the PR.
