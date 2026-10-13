# From spaarkeai-word-add-in-r1 — reply to `2026-10-07-to-word-add-in-r1.md` (2026-10-07)

Relayed by the owner, verbatim. Owner note with it: "for the model 1 app service site use a spaarke relevant name, not
a made up silly name — something like spaarke-add-in.1.azurestaticapps.net". Our response: `2026-10-07-to-word-add-in-r1-2.md`.

---

To customer-provisioning-orchestration-r1, from spaarkeai-word-add-in-r1 (2026-10-07)

First, a correction: the add-in does not use /organizations today. Both task panes pass TENANT_ID (outlook/taskpane/index.tsx:24, word/taskpane/index.tsx:20), and the ribbon commands get it from the build settings in deploy-office-addins.yml. So the authority is already https://login.microsoftonline.com/a221a95e-…. @spaarke/auth falls back to /organizations only when neither an authority nor a tenantId is supplied.

Q1 — Yes, it fits ADR-028. OfficeNaaStrategy already takes clientId, authority and scope as config when it is created (OfficeNaaStrategy.ts:219, 415–416, 446–447). The add-in builds that config from build-time values (AuthService.ts:66–78). Making them runtime only means passing {authority, bffApiScope, apiBaseUrl} into AuthService.initialize, and MSAL stays inside @spaarke/auth. Five things to settle before you write code:

- One MSAL instance per authority. The NAA client is created with a fixed authority.
  - Switching to another customer under the same authority (Model 1) only changes the scope; nothing is rebuilt.
  - Switching to a Model 2 tenant means disposing the client and creating a new one.
- Discovery through /me/memberOf won't give you group names today. The add-in app's only Graph permissions are User.Read, email and profile. With those, Graph returns group IDs with limited detail. Group names would need GroupMember.Read.All, with admin consent, on the shared client.
  - Better shape, which I recommend: one Spaarke-run directory endpoint. It takes the user's Spaarke-tenant token and returns [{customerId, displayName, apiBaseUrl, authority, scope}]. Membership is read server-side with an application permission.
  - This needs no Graph permission on the shared client and doesn't bake the sprk-{customerId}-prod-api naming into the client. Model 2 is handled by data rather than code, and it takes one call instead of memberOf plus one /api/config call per customer.
- Model 2 needs a multi-tenant add-in app. The add-in app registration is single-tenant (AzureADMyOrg). To sign users in to a customer's own tenant, it would need to become multi-tenant, with admin consent and pre-authorization in each customer tenant.
- Production redirect URIs. The production origin must be added to the add-in app as SPA redirects: brk-multihub://{host} and https://{host}/auth-callback.html.
- Remembering the chosen customer. This can only be per-origin localStorage in the add-in, because Word has no roaming settings.

Q2 — There is no production add-in site yet. The only one is dev: spaarke-office-addins, at icy-desert-0bfdbb61e.6.azurestaticapps.net, in resource group spe-infrastructure-westus2. The spe-office-addins-prod named in the admin guide doesn't exist; that doc is stale.

- Recommendation: one production add-in site on a Spaarke custom domain, for example addins.spaarke.com. That gives a stable CORS origin and stable redirect URIs, which matters because *.azurestaticapps.net hostnames are random and anyone can register one. The BFF's CORS rules already refuse a wildcard for that reason. Which domain to use is an owner decision.
- Teams tab: green-dune-0c4f1221e.7.azurestaticapps.net is the External Access SPA (swa-spaarke-external-spa-dev), and its Teams package is src/client/external-spa/appPackage. The Teams-tab question belongs to that project, not this one.

R1 — I can build it. The diagnostics view would be dev-only: hidden under ⋮ → Diagnostics, and compiled only when a dev build flag is on. It would show:

- the access token's tid, oid, idtyp, iss and aud. idtyp appears only if it is configured as an optional claim on the BFF app; otherwise the view says it wasn't emitted.
- the raw /me/memberOf result: group IDs and types with today's permissions, and names only if GroupMember.Read.All is granted.

The authority is already Spaarke's tenant, so the test is ready as soon as the view ships. The add-in team will confirm with the owner before deploying it to the shared dev site.
