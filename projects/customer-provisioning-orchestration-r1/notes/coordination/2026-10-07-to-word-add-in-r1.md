# To spaarkeai-word-add-in-r1 — from customer-provisioning-orchestration-r1 (2026-10-07)

Relayed by the owner. This is about making the shared Outlook/Word add-ins and the Teams tab work against **many
customer environments**. Nothing here changes your current dev UAT. Two questions need answers from you (Q1, Q2), and
we need one dev test build from you (R1).

## Why this is coming

Since owner decision D-13, every customer has its **own** BFF (`sprk-{customerId}-prod-api`) with its **own** Entra
app registration and token audience. Model 1 customers' users are **B2B guests in Spaarke's tenant**. Owner, 2026-10-07:
the add-ins and the Teams tab are used **only by Dataverse-licensed users** (internal or B2B guest), never by external
contacts.

Today the add-in has one build-time BFF URL and audience (`api://1e40baad…`), and `@spaarke/auth` falls back to the
`/organizations` authority (no Dataverse context), so a guest signs in to their **home** tenant. A customer BFF needs a
**Spaarke-tenant** token for **its** audience. Facts and options: `projects/customer-provisioning-orchestration-r1/notes/t240-plan.md`.

## The design we propose (owner D11 approved the shape; the details are open to you)

One shared add-in package and one Teams package, no per-customer builds. At runtime:
1. Sign in to **Spaarke's tenant** (Model 1) and read the user's own group memberships (`/me/memberOf` or
   `checkMemberGroups`). Every Dataverse-licensed user is in `sprk-{customerId}-users` for each customer environment they
   use, which is the user → customer mapping.
2. `customerId` → BFF address `https://sprk-{customerId}-prod-api.azurewebsites.net` → `GET /api/config` (anonymous;
   returns the authority, client id and scope).
3. Request that BFF's `api://{bffAppId}/user_impersonation` (provisioning's H3 pre-authorizes your add-in client
   `c1258e2d…` on every customer BFF app registration, so there is no consent prompt) and call that BFF.
4. A user in several customers picks one, once; remembered (roaming settings / local storage).

Model 2: the customer's own tenant is the sign-in tenant; the same lookup runs there.

## Questions

- **Q1.** Does this fit `@spaarke/auth`'s OfficeNaaStrategy and your "no MSAL in the add-in package" rule (ADR-028)?
  We assume the authority and scope become runtime parameters of `@spaarke/auth` rather than build-time constants. If you
  see a better shape, say so before we write anything.
- **Q2.** What are the **production** origins of the add-in site and the Teams tab (dev: `icy-desert-0bfdbb61e.6.azurestaticapps.net`,
  `green-dune-0c4f1221e.7.azurestaticapps.net`)? Every customer BFF must list them in CORS, and provisioning now sets
  CORS (customer BFFs currently fail to start without it).

## Request

- **R1.** A **dev-only test build** of the add-in whose `@spaarke/auth` authority is Spaarke's tenant
  (`https://login.microsoftonline.com/{spaarkeTenantId}`), plus a small diagnostics view that shows the acquired token's
  `tid`, `oid` and `idtyp` and the result of `/me/memberOf`. The owner will sign in with a work-account guest from another
  tenant in Outlook and Word (desktop and web). This test decides whether the design above works for guests. Same
  question for the Teams tab (inside Spaarke's tenant vs. the guest's home tenant).

## What provisioning does on its side (no add-in code)

- Sets `Cors__AllowedOrigins` on every customer BFF: the customer's Dataverse origins plus the add-in and Teams origins.
- H3 pre-authorizes the shared add-in client (and later a dedicated Teams client app) on each customer BFF app
  registration, and adds the customer's Dataverse origin as an SPA redirect for its code pages.
