# Task 196: "Email members" recipients and the Manage Access notes bell

Owner decisions, 2026-11-12 (uac-r2 session). Client only: no BFF route, no Dataverse change.

## 1. "Email members" recipients

**Bug.** On a project with an active Full Access contact grant and an internal user share, the TrackingFieldTrio email icon said "This record has no membership contacts with an email address yet". `resolveEmailMembersRecipients` in `src/client/pcf/TrackingFieldTrio/index.ts` read only `fetchCandidates()`, the record's `sprk_assigned*` role lookups. It never read grants or shares.

**Rule now** (shared `AccessGrantModal/emailMembers.ts`, injected reads; the host is the only place that knows tables):

| Source | Read (reused) | Notes |
|---|---|---|
| a. Role contacts | the host's role-contact read behind `fetchCandidates` (strict variant `readRoleCandidates(true)`) | Kept whatever the record's policy says (owner's list), but left out when walled off |
| b. Active grants | the host's `fetchExistingGrants` (`GET /record-grants`, as Current Access) | Expired grants left out; grants the record's policy cancels ("No effect": Restricted, or org/standing on Limited/Secure) left out; emails read from `contact.emailaddress1` |
| c. Internal user shares | `GET /user-shares` through `userShares.ts`, the reader the modal now uses too | Direct and inherited shares; an external user's share on a Restricted record (`externalNoAccess`) left out; email = `systemuser.internalemailaddress` |
| Standing-grant members | not read separately | The host's standing rows are its role contacts whose global flag is set (`fetchStandingContacts` filters `fetchCandidates`), so they are already in (a) |
| Organization-wide grants | not expanded | They name no person; the notice says "people with access only through an organization-wide grant are not added" |

No Access: the record's No Access read (`buildNoAccessPath` + `parseNoAccessResponse`), `buildVetoIndex` + `vetoFor` as Current Access marks rows, organization walls through the host's `fetchContactOrganizationMemberships`. The record's effective policy is `effectiveAccessState` over the host's values and the server's answer, as in the modal.

De-dup: trimmed email, case-insensitive; the first spelling wins (order: role contacts, grants, shares).

**Failure handling (fail visible, never silently narrower).** Every read rejects on failure in this path (the modal's soft reads are unchanged). Outcome:

| Situation | Result |
|---|---|
| A source or check failed (grants 403 for a caller without Write + Share, shares, roles, emails, No Access not readable or not shown to the caller, org memberships, a capped grants list) | The composer opens with what was read and a warning bar: "Some people may be missing from the recipients: {what}. Check the list before sending." |
| Every source (roles, grants, shares) failed | Error dialog: "The people on this record could not be loaded, so no email was started. Close this and try again." |
| Every source and check succeeded, nobody with an email | Empty state: "This record has no members with an email address yet. Grant access or assign a role first." |
| Nobody found but something failed | The composer opens with no recipients and the notice (never the empty state) |
| Host is not a grant root (To Do, Event, harness) | Role contacts only, read as before, no notice |

A No Access list that could not be checked keeps the people and says so (the modal's precedent: unmarked rows plus a visible note); the sender sees the recipients before sending. A late answer for a record the form has left is dropped.

**Duplicate removed.** `TrackingFieldTrio.emailMembers.test.tsx` carried a verbatim copy of the recipient rule; it now calls the shared resolver.

## 2. Manage Access notes bell

A bell (`Alert20Regular`) in the modal header (`SprkModal` `headerActions`, before maximize and close), with a count badge and the accessible name "Access notes (N)". It opens a popover listing each note in full (MessageBar, same intent and title as before); the parent link still opens the parent. Shown only when the caller may manage access, as the banners were; notes are listed once the access data has loaded. No persistence.

### Classification of every message in the modal

| Message | Where | Why |
|---|---|---|
| Access Permission explanation: "Restricted Access" / "Secure – Restricted" / "Secure" / "Limited Access" (with "It follows the {parent} it is filed under") | **Bell** | Explains the record's policy; the controls it describes are already hidden or shown, nothing is disabled by the message |
| "Minimum access from the parent" (parent link, effective-value lines) | **Bell** | Display only (task 175: nothing is hidden or disabled by it) |
| "Secure record — Owner · Business Unit" | **Bell** | Read-only information |
| Action outcome notice (Success / Notice / Error, Retry, Try again; the 409 follows-parent refusal) | Inline | Reports the result or failure of the user's action and carries the retry |
| "Write and Share required" / sign-in deny banner | Inline | Blocks every write action |
| "You do not have permission to grant or revoke access" | Inline | Replaces the body; nothing can be done |
| "Loading access data…" | Inline | State of the body |
| "Only the first N grants are shown" | Inline | NFR-03: the cap is stated at the list it qualifies |
| "Current Access unavailable" / "Not shown." | Inline | Failure |
| "No Access List unavailable" | Inline | Failure |
| No Access List truncated warning | Inline | Partial data, stated at the list |
| Organization wall "could not be checked" | Inline | Failure |
| Expiration hints (no shares yet, different dates, not applied yet, renewed) and its validation message | Inline | At the point of action; the validation message blocks Apply |
| Row notes (No effect, walled off, external user — no access, inherited share, paired grant) | Inline | About that row |
| Revoke confirmation's residual-access warning | Inline (confirm dialog) | At the point of action |
| "Unsaved access permissions" | Inline (confirm dialog) | Blocks closing |

## 3. Versions and deploy

- TrackingFieldTrio 1.0.62 → **1.0.63** (manifest, `versionText`, `Solution/solution.xml`, `pack.ps1`, committed bundle). Import `TrackingFieldTrioSolution` with `scripts/Import-SolutionScoped.ps1`, then verify the served bundle hash.
- No other PCF bumped: the only other changed shared code reaching other bundles is `SendEmailDialog`'s optional `notice` prop, which renders nothing when unset; `AccessGrantModal` is rendered only by TrackingFieldTrio.
