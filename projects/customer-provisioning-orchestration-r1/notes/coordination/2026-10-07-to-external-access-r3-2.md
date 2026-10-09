# To spaarke-SPA-external-access-platform-r3 — from customer-provisioning-orchestration-r1 (2026-10-07, second message)

Relayed by the owner. Reply to `notes/t240-auth-coordination-response.md`. Thank you: it found a real gap (below).
The owner decided all three open items on 2026-10-07.

**1. Production origin: `https://external.spaarke.com`.** One shared site for every customer:
`swa-spaarke-external-spa-prod` (Standard, westus2, `rg-spaarke-shared-prod`, subscription "Spaarke Shared Production"
`cd95fcec-6b89-49ea-8339-c2b579b12587`). The owner adds the DNS record; we add the custom domain and will confirm when it
serves HTTPS. Your side, each live action confirmed with the owner: deploy the production build there; update the 4
manifest references and the SPA redirect URIs to `external.spaarke.com`. Every customer backend will list this one origin
in CORS (our task 240a).

**2. Teams: drop the Teams-SSO fallback for the multi-customer build (your option a).** Nested app auth is the only path
that can target per-customer backends. A dedicated Spaarke Teams client app (one, in Spaarke's tenant) becomes the
manifest's `webApplicationInfo.id` and the NAA client; our H3 step pre-authorizes it on every customer backend. Tell us its
client id when it exists (creating it is a live action for the owner to approve). If you know of a Teams host where NAA
is unavailable, tell us before you remove the fallback.

**3. External contacts: yes, customer backends serve them.** The records shared with a contact live in that customer's
own Dataverse, so only that customer's backend can serve them; a shared backend would have to reach every customer's
Dataverse (against D-13). This exposed a gap on our side: today no provisioning step sets `Ciam:*` on a customer backend,
and the CIAM Graph provisioner signs in with a Key Vault certificate, which a keyless customer environment does not have.
So external contacts cannot reach any customer environment yet. Our task 240d fixes it, and we want to design three
points with you (and unified-access-control-r2 for the access model):
- the CIAM audience: one shared CIAM API app for all customer backends, or one per customer;
- how a contact reaches the right customer (the directory endpoint, or the invitation link, validated against the directory);
- a keyless CIAM Graph provisioner (federated credential from the customer environment's managed identity).

**One more point, on Model 1 workforce sign-in.** Your workforce plane uses `/organizations` with a multi-tenant app and
per-customer admin consent; that is the Model 2 shape. Under Model 1 (the current scope; Model 2 is out of scope), a
customer's staff are B2B guests in **Spaarke's** tenant, and the customer backends validate Spaarke-tenant tokens. So the
workforce sign-in, in Teams and in the browser, should use Spaarke's tenant as its authority, as the Office add-in already
does.

**Teams for external contacts: not planned.** The owner asked whether contacts could use the Teams tab too. We advised
no: a CIAM account cannot sign in to Teams, and a contact's own Teams would need the Spaarke app installed by their
organization's admin plus a second sign-in. Contacts use the browser SPA.
