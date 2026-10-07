# T240 — Shared M365 clients reach each customer's own BFF: plan (2026-10-07)

Background and the approved approach (owner D9/D11): `g14-shared-clients-auth-chain.md`. This note records what changed
on 2026-10-07, the split into three tasks, and the live test plan.

## Owner input, 2026-10-07

- The live tests may go ahead.
- **The add-ins and the Teams tab are used only by Dataverse-licensed users, internal or B2B guest. No external contact
  uses them.**
- Coordinate add-in and Teams client changes with spaarkeai-word-add-in-r1 directly, or give the owner a message to relay.

## Facts re-checked in code (2026-10-07)

| # | Fact | Where |
|---|---|---|
| F1 | 🔴 A customer BFF does not start: outside Development/Testing, empty `Cors:AllowedOrigins` throws at startup, and nothing in provisioning (manifest, H4b, `customer.bicep`) sets it. H13's CORS probe would fail too. Dev works only because its origins were set by hand. | `CorsModule.cs:17-45`; `git grep AllowedOrigins` |
| F2 | H3 sets no `preAuthorizedApplications` and no SPA redirect URIs on the customer's BFF app registration. | `GraphAppRegistrationProvisioner.cs` |
| F3 | Code pages use `redirectUri = window.location.origin` (the Dataverse org). H7 defaults `sprk_MsalClientId` to the customer's BFF app, which has no SPA redirect, so code-page sign-in fails on a provisioned stamp. Dev uses the shared client `SDAP-PCF-CLIENT`, whose redirect list names each org by hand. | `Spaarke.Auth/src/config.ts:113`; H7 `:331` |
| F4 | The add-in has no Dataverse context, so `@spaarke/auth` falls back to the `/organizations` authority: a Model 1 guest signs in to their HOME tenant. A customer BFF validates Spaarke's tenant and exchanges OBO to Spaarke-tenant Dataverse, so it needs a Spaarke-tenant token. | `Spaarke.Auth/src/config.ts:13`, `OfficeNaaStrategy` |
| F5 | Every BFF self-describes anonymously: `GET /api/config` returns the authority, client id and scope. | `ConfigEndpoints.cs:83-140` |
| F6 | Model 1 users are members of the environment security group `sprk-{customerId}-users` in Spaarke's tenant (T232; Dataverse requires it). | `provisioning.md` "Model 1 users" |
| F7 | The only guest in Spaarke's tenant today is a personal Microsoft account. The test needs a work account from another Entra tenant. | read-only `az ad user list`, 2026-10-07 |

## Changed recommendation: customer discovery by group membership

Because every add-in or Teams user is Dataverse-licensed (owner, 2026-10-07), every Model 1 user is a member of their
customer's `sprk-{customerId}-users` group in Spaarke's tenant (F6). That membership **is** the user → customer mapping,
and it is authoritative, so no separate directory and no home-tenant heuristic are needed:

1. The client signs in to **Spaarke's tenant** (Model 1) and reads the user's own group memberships
   (`/me/memberOf` or `checkMemberGroups`), keeping the `sprk-*-users` groups.
2. `customerId` → the BFF at its predictable address `sprk-{customerId}-prod-api` (`customer.bicep`) → `GET /api/config`
   (F5) gives the authority and scope.
3. The client requests a token for that scope (pre-authorized by H3, so no consent prompt) and calls that BFF.
4. A user in several customers' groups picks one, once; the choice is remembered.

Model 2: the customer has its own tenant, so the sign-in tenant identifies the customer; the same lookup runs there.

No anonymous endpoint is exposed and no customer list is published. It depends on step 1 working for guests; the live
test (240b) checks that first.

## Split

| Task | Scope | Depends on |
|---|---|---|
| **240a** server side, needed either way | (1) **CORS:** provisioning sets `Cors__AllowedOrigins__N` on every stamp BFF (the customer's Dataverse origins + the shared client origins), so the BFF starts (F1). (2) **H3:** SPA redirect = the customer's own Dataverse origin(s) on its BFF app registration (fixes code pages, F3; H7's default kept); `preAuthorizedApplications` for the shared add-in client `c1258e2d` (and the Teams client once it exists). | — |
| **240b** live test (owner-approved 2026-10-07) | A work-account guest from another tenant, in Outlook and Word (desktop + web) and Teams: (S1) get a Spaarke-tenant token through Office's nested app auth; (S2) read their own group memberships; (S3) the Teams tab in Spaarke's tenant vs. their home tenant. | a guest account; spaarkeai-word-add-in-r1 for the add-in test build |
| **240c** client discovery | The client steps above in `@spaarke/auth` + add-in/Teams configuration, a dedicated Teams client app, and H3 pre-authorizing it. | 240b; spaarkeai-word-add-in-r1 |
