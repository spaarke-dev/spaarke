# Task 060: does a shared registration service conflict with the Model 1 directive? (2026-10-12)

Owner, 2026-10-12: "a shared service as long as does not create conflict with our Model 1 dedicated and segregated customer directive". Read-only analysis from code and docs. **Conclusion: no conflict, under the conditions in §5.** A per-customer inviter buys no segregation and is already forbidden by ADR-028 A7.

## 1. The directive

- `docs/architecture/INFRASTRUCTURE-PACKAGING-STRATEGY.md:28`: "Every Azure resource is dedicated per customer in both models" (App Service plan and App Service, AI Search, Redis, OpenAI, Cosmos, Key Vault, Storage, Service Bus, App Insights, SPE container, Dataverse environment). `:127-138`: only Static Web Apps and Content Safety may stay shared; a future candidate must hold "no privileged legal content at rest". Source decision: `projects/unified-access-control-r2/notes/D-12-deployment-model-redefinition.md:65-67, :120-124` ("sharing a *tenant* does not mean sharing *resources*").
- It segregates data at rest, compute, keys, telemetry and billing per **customer stamp**. It does not segregate Entra identity: every Model 1 customer's staff are B2B guests in Spaarke's tenant (`.claude/constraints/auth.md:64`).
- The platform plane is already shared by design: the L2 control plane, its identity, Cosmos and Key Vault, the Admin Dataverse registry, the SPE container type and owning app (`docs/architecture/SPAARKE-ENVIRONMENT-RESOURCE-INVENTORY.md:168-194`). ADR-052 Tenancy row (`.claude/adr/ADR-052-workload-placement.md:65`) names the category "fleet-scoped".

## 2. What a shared service touches across customers

| Item | Class | Why |
|---|---|---|
| Customer key → `CustomerTenantIds`, users group id | No conflict | Read from the registry (already "dedicated row in a shared table", inventory `:110`); routing metadata, no legal content |
| `User.Invite.All` + `GroupMember.ReadWrite.All` in Spaarke's tenant | No conflict | Tenant-wide by nature; the shared L2 Worker already holds both for every customer (`ControlPlaneGraphAppRoles.cs:52,61`) |
| Logs (email, UPN, home tenant, customer key) | Mitigable | The one real tension (D-12 `:112`). Precedent fix: H11 logs ids, no email (`GraphRestB2BInvitationClient.cs:113`) |
| Customer data | No conflict if kept so | The contact is created on the stamp (FR-15), not by this service |
| Mis-routing to the wrong customer's group | Mitigable | The stamp's FR-15 member test still requires `idp` ∈ its own `CustomerTenantIds` (`SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md:812-815`) |

## 3. Precedent

The T240c directory is already an approved shared, cross-customer service in `rg-spaarke-shared-prod` with its own read-only MI (`customer-provisioning-orchestration-r1/notes/t240-plan.md:54-56, :122`; D11 in `g14-shared-clients-auth-chain.md:53`). Co-hosting (owner Q5, 2026-10-11) is consistent if the registration service keeps a **separate** MI (spike `fr-12-self-registration-decision.md` §4, §7 item 2).

## 4. The tenancy fact

Every registered person becomes a guest in the one Spaarke tenant wherever the inviter runs (`auth.md:64`; ADR-028 `:456`). A per-stamp inviter would put N identities with tenant-wide power inside customer subscriptions, widening the blast radius. It is also forbidden: ADR-028 A7 `:492` ("one shared Spaarke-tenant registration path"), `:500` (no `User.Invite.All` for a stamp identity); task 261 removed it from stamps (`GraphAppRoles.cs:38-40`); `spec.md:299`.

## 5. Recommendation and conditions

One shared registration service co-hosted with the T240c directory, provided:
1. No customer data at rest; registry reads only; idempotency through Graph lookups (H11 lookup-then-invite).
2. `CustomerTenantIds` and the group id from the registry row, never the request; provisioning writes registry and stamp setting from the same intake, with an H13-style parity check.
3. Its own user-assigned MI with MI-FIC, no secret. Grants `User.Invite.All`, `User.Read.All`, `GroupMember.ReadWrite.All` (check scoping to `sprk-*-users` first; POML escalation trigger) plus registry read.
4. Logs carry guest `oid`, customer id, home tenant id and outcome code; never email, UPN or display name. Short retention, recorded in the placement decision.
5. Abuse controls: authenticated home-tenant sign-in first; rate limit per home oid/tenant and per customer key; no CAPTCHA needed; unknown key and non-listed tenant get the same refusal (no key enumeration); the `acct = 1` refusal stays distinct.
6. Paperwork: `notes/fr-14-placement-decision.md` under ADR-052 (fleet-scoped host); a row in the inventory's shared-platform table; a one-line D-12 / packaging-strategy clarification that the closed list covers the customer stamp.

**Separate conflict to resolve:** the multitenant "Spaarke Registration" client with an `/organizations` authority contradicts ADR-028 `:22`, A7 `:498` and `auth.md:66`. It needs an A7 carve-out (one registration-only client, ID token only, its token never reaches a BFF): §6.5 path B.

**Alternative:** a per-stamp invite function. It breaks ADR-028 A7 or needs an amendment plus a dedicated identity per customer (ADR-052 `:77`), a deployable per stamp (against "provisioning builds no new components"), per-customer link routing, and hits the 20-federated-credentials-per-app limit (inventory `:194`).

## 6. Open items for the owner

1. Accept that the directive covers the customer stamp, not the platform plane; approve the D-12 clarification line.
2. Approve the ADR-028 A7 carve-out for the `/organizations` registration client.
3. Set the log retention; confirm no email or UPN in logs.
4. If group write cannot be scoped below tenant-wide `GroupMember.ReadWrite.All`, approve that grant.
5. Doc fix: D-12 `:65` still says "three named exceptions"; the list has two.
