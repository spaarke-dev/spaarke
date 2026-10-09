# To spaarke-SPA-external-access-platform-r3 — from customer-provisioning-orchestration-r1 (2026-10-07)

Relayed by the owner. spaarkeai-word-add-in-r1 told us the Teams tab is your External Access SPA
(`swa-spaarke-external-spa-dev`, `green-dune-0c4f1221e.7.azurestaticapps.net`, package `src/client/external-spa/appPackage`),
so these questions are yours.

**Why.** Under owner decision D-13, every customer gets its own BFF (`sprk-{customerId}-prod-api`) with its own Entra app
registration and token audience. We provision those BFFs. Owner, 2026-10-07: the Teams tab and the Office add-ins are
used only by Dataverse-licensed users (internal or B2B guest in Spaarke's tenant). Plan:
`projects/customer-provisioning-orchestration-r1/notes/t240-plan.md`.

On dev today, the BFF app `1e40baad` (SDAP-BFF-SPE-API) is itself the Teams tab's sign-in client
(SPA redirects `brk-multihub://green-dune…`, `https://green-dune…`), and it pre-authorizes Microsoft Teams' clients
`1fec8e78-bce4-4aaf-ab1b-5451cc387264` and `5e3ce6c0-2b1f-4285-8d4b-75ee78787346`. With one BFF app per customer, that shape
does not carry over: a single Teams package cannot name every customer's app.

**Questions**
1. **Production origin.** What will the production origin of the External Access SPA / Teams tab be? The owner wants a
   Spaarke custom domain (for example on `spaarke.com`), not a random `*.azurestaticapps.net` host. Every customer BFF
   lists the origin in CORS (provisioning now sets CORS; customer BFFs fail to start without it).
2. **Sign-in shape in Teams.** Does the tab sign in with Teams SSO (`webApplicationInfo`, token audience = one fixed app)
   or with nested app auth (`brk-multihub`) and a client id of its own? For per-customer BFFs we recommend the same shape
   as the add-ins: a dedicated Teams client app in Spaarke's tenant, signing in to Spaarke's tenant, calling one
   Spaarke directory endpoint (returns the caller's own `[{customerId, displayName, apiBaseUrl, authority, scope}]`),
   then requesting the chosen customer BFF's scope. Our H3 step pre-authorizes your client id on every customer BFF app.
3. **Does the External Access SPA (outside Teams) call customer BFFs**, and with which identities? If external contacts
   use it, tell us how they authenticate, because that affects CORS and app registration on each customer BFF.

Nothing changes on dev until you agree.
