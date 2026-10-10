# To spaarke-SPA-external-access-platform-r3 — from customer-provisioning-orchestration-r1 (2026-10-09)

Sent at the owner's request ("does this need to be coordinated with SPA-r3 — send them a note and advise them to look
at it if necessary"). **Please review; reply in your project notes and tell the owner.**

## What changed

The owner accepted the **T240d design** on 2026-10-09: how each customer's own BFF (Model 1, one BFF per customer)
serves **CIAM external contacts** (local accounts in `spaarkeextid`; no Dataverse user, no Entra guest; they use the SPA).
Full design: `projects/customer-provisioning-orchestration-r1/notes/t240d-ciam-external-contacts-design.md` (on branch
`work/customer-provisioning-orchestration-r1`).

- **Audience, one per customer.** Each customer's BFF app registration (created by our H3, D-13) also becomes that
  customer's CIAM audience: its service principal is provisioned into `spaarkeextid`, and the stamp sets
  `Ciam:Audience = {bffAppId}`. The SPA asks `spaarkeextid` for `api://{customerBffAppId}/user_impersonation` for the
  customer it is talking to, **not** the shared dev CIAM API app `4a4d5126-…`.
- **Keyless account creation.** Each stamp creates its contacts' CIAM accounts with its managed identity (MI-FIC,
  Graph `User.Create` in `spaarkeextid`). The Key Vault certificate path (`CiamGraphClientFactory`) does not run on
  stamps.
- **Routing.** An invitation link carries **only the customer key**, never a BFF URL or scope. The SPA resolves the key to
  the BFF base URL and audience through the T240c directory's CIAM-authenticated lookup.
- **Refusal.** A stamp refuses another customer's contact by audience (401) and by the oid binding in its own Dataverse
  (403).
- **Owner approvals today:** spike S1 (dev-only test in `spaarkeextid`) and the one-time `spaarkeextid` setup. The setup
  includes the **production CIAM SPA client** (single-tenant, redirect under `https://external.spaarke.com/...`): you own
  its configuration; we need only its client id.

## What you may need to look at

1. **SPA token request**: per-customer scope `api://{customerBffAppId}/user_impersonation` on the CIAM plane, chosen per
   customer after the key lookup (today the SPA requests the shared CIAM API scope).
2. **Invitation link format + customer picker / cold landing**: the link carries only the customer key; drop
   `apiBaseUrl` from your deep-link draft (design §9 row 4).
3. **Production CIAM SPA client**: we are about to register it in `spaarkeextid` as part of the approved setup unless you
   prefer to do it — tell us the redirect URIs you want.
4. **"Already has an account" invites** (a second customer inviting the same person) and the **pending-invite marker**
   that narrows E11 bind-by-email (ISS-009 / #1485, owned by unified-access-control-r2) affect the invite UX.
5. Your `design.md` §4.6 says auth items wait for `spaarke-auth-system-of-record-r1`; please confirm this design does not
   conflict with that.

## Timing

Spike S1 runs first (dev only). If its audience test fails, the fallback is one CIAM API app per customer in
`spaarkeextid` (design option 2); the SPA change in item 1 stays the same shape (a per-customer scope). We will tell you
the S1 result.
