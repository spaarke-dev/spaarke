# ADR-028 Amendment A9: draft record (APPLIED 2026-10-13)

> Status: **Applied.** The owner approved A9 on 2026-10-13 ("yes approve A9") and assigned R3 end-to-end ownership ("yes r3 end to end"). The authoritative text is now in `.claude/adr/ADR-028-spaarke-auth-architecture.md` (Amendment A9). The draft below is kept as the record. Where it differs from the ADR, the ADR wins. The Status line changed: R3 now owns the SPA client, BFF token acceptance, registration and stored link, and UAC-r2 owns only the baseline role's access application.

---

## Amendment A9 (2026-10-13): Workforce contacts sign in with their customer's home directory (browser SPA)

> **Status**: Accepted (resolution path **B: amendment**, per root CLAUDE.md §6.5). **Decision**: owner, 2026-10-13 (R3 session; UAC-r2 round 97). **Driver projects**: `spaarke-SPA-external-access-platform-r3` (SPA client, this text) and `unified-access-control-r2` task 186 (BFF token acceptance, registration, linking). **Amends A8 for workforce contacts only.** Research: `projects/spaarke-SPA-external-access-platform-r3/notes/2026-10-13-option-a-identity-and-licensing-research.md`.

### Terms

- **Spaarke tenant**: Spaarke's Entra directory. In Model 1 it holds every customer's Dataverse environment, Azure resources, app registrations and SharePoint Embedded.
- **Customer's home directory**: the customer's own Entra directory, which holds its employees' company accounts.
- A bare "tenant" means the Spaarke tenant. Never use it for the customer's home directory.

### Who signs in how (Model 1)

| Population | Identity | Sign-in | Surfaces |
|---|---|---|---|
| Licensed core users | B2B guest in the Spaarke tenant; Dataverse `systemuser` | **Unchanged (A8):** single-tenant Spaarke client, Spaarke-tenant authority, NAA then popup | Model-driven apps, Office add-ins, Teams; the browser SPA through the stored link below |
| **Workforce contacts** (licence-free customer employees) | **Company account in the customer's home directory; Dataverse `contact`. No B2B guest** | **This amendment** | Browser SPA. Teams is deferred (see Scope) |
| Partners | CIAM local account (`spaarkeextid`) | **Unchanged (A1/A8)** | Browser SPA |

### New MUST rules

- **MUST** sign workforce contacts in to the browser SPA through **one multitenant Spaarke SPA client**.
  - The authority is the customer's home directory, `https://login.microsoftonline.com/{home directory id}`. The id comes from Spaarke-owned data (the T240c directory, or dev configuration until it ships), never from a link or other client input.
  - `organizations` is allowed for this client only, and only for discovery before the customer is known. The BFF rules below are the enforcement point either way.
- **MUST** make each customer's BFF API app multitenant, with `requestedAccessTokenVersion` = 2.
  - The customer's admin consents once in the customer's home directory (provisioning onboarding; `knownClientApplications` for one combined consent).
  - The scope stays `api://{bffAppId}/user_impersonation` (A8).
- **MUST**, in the BFF, accept a workforce-plane user token issued by a customer's home directory only when **all** of these hold:
  - `iss` exactly equals `https://login.microsoftonline.com/{tid}/v2.0`;
  - `tid` is in **that stamp's** `WorkforceIdentity:CustomerTenantIds`;
  - `aud` is that stamp's BFF app;
  - it is a user token;
  - `acct` = 0 (member of that home directory; the optional claim is configured).

  Every other home-directory token is refused (401). Identity is keyed on (`tid`, `oid`). Authorization never relies on `email`, `upn` or `preferred_username` (Microsoft claims-validation guidance).
- **MUST**, on first sign-in, handle a workforce contact who is not yet registered:
  - A member whose (`tid`, `oid`) binds to no contact gets `registration_required`.
  - The SPA's profile form then creates **one** workforce contact bound to (`tid`, `oid`), active immediately (owner: no approval).
  - Submitting again creates nothing new.
- **MUST** resolve a **licensed core user** who opens the browser SPA with their company account to their `systemuser` **only through a stored link**: home (`tid`, `oid`) to `systemuserid`.
  - The link is created once, at a moment when both identities are proven.
  - It is never created by matching email or UPN. A guest object does not expose the home `oid`.
  - There is no separate "core user sign-in" entry point (owner, 2026-10-13).
- **MUST** give workforce contacts their baseline access through the Dataverse role "Spaarke Internal Contact User (Workforce)", applied as UAC-r2 task 186's design states.
- **MUST** complete Microsoft publisher verification for Spaarke's multitenant apps before the first production customer.

### New MUST NOT rules

- **MUST NOT** create B2B guests for workforce contacts. A8's "Workforce-contact self-registration" rules (guest creation through a shared registration path) are withdrawn.
- **MUST NOT** accept a home-directory token whose `tid` is not in that stamp's list, a home-directory guest (`acct` = 1), or an app token, on this path.
- **MUST NOT** move licensed core users' Office add-in or Teams sign-in to the multitenant client. They stay on A8.

### Edits elsewhere in ADR-028 (cross-reference notes only)

- `:37`, `:75` (A2 notes): "Model 1 workforce authority superseded by A8" gains: "for licensed core users; workforce contacts on the browser SPA follow A9."
- `:54`, `:59` (A1 notes): "licensed users and licence-free workforce contacts are B2B guests" becomes "licensed core users are B2B guests; workforce contacts sign in with their home directory (A9)."
- A8 `:516-518` (single-tenant client) and `:548` (no `common`/`organizations` for Model 1) gain: "except workforce contacts on the browser SPA (A9)."
- A8 `:540-543` (workforce-contact self-registration) gains: "Withdrawn by A9."

### Scope and preserved invariants

- **Unchanged:** no OBO on any collaboration plane; app-only Dataverse and SPE; plane selection from validated `iss`/`tid`; A8's `user_impersonation` scope, default Tier-1 modules and CIAM rules; no invite permission on any stamp identity.
- **Teams for workforce contacts is deferred** (owner: a soft requirement). When it is added, it uses the documented multitenant NAA pattern under these same rules, after a live sign-in test.
- **Licensing** (multiplexing, External Users) is a commercial question outside this ADR.
- **Model 1 only.** Model 2 keeps A2's text until a further amendment.

### Alternatives considered and rejected

- **Keep A8 guests for workforce contacts.** This means a guest "Review permissions" screen and possibly a second MFA prompt, unless both directories configure automatic redemption and MFA trust. It also needs a cross-tenant guest token through NAA inside the customer's Teams (undocumented), leaves stale guests at off-boarding with no CAE for guests, and needs a central invite permission.
- **Link licensed users by email or UPN.** Microsoft says these claims must not be used for authorization; email can be renamed or reused.
- **A separate "core user sign-in" button.** The owner rejected it: non-core users would click it and be confused.
- **One multitenant client for every population.** Licensed users' `systemuser` is keyed on the guest identity, and the add-in already runs the A8 shape.

> **Note (A9)**: Applied concise-only, like A1-A8. A9 changes workforce-contact sign-in and registration only.
