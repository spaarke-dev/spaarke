# Spaarke Auth v4 — Zero-Secret BFF Confidential Credential (OBO → MI Federated Credentials)

> **Status**: RESEARCH COMPLETE · design drafted · **ready for `/design-to-spec`** · **Epic**: Auth / Code Quality (#427)
> **Origin**: surfaced during `code-quality-and-assurance-r3` task 011 / #3b (app-only Dataverse → MI, done + live)
> **Type**: auth architecture (**ADR-028 amendment — §6.5 path B**) · **Surface**: BFF confidential clients + `Spaarke.Dataverse`
> **Risk**: HIGH (OBO = all delegated user auth; fails closed)

## One-liner

`#3b` migrated the **app-only** Dataverse paths to Managed Identity. The client secret survived because the same
`BFF-API-ClientSecret` backs **OBO** (delegated user auth) across Graph and Dataverse. This project eliminates that
secret too — via **Managed Identity as a Federated Identity Credential (MI-FIC)** or a certificate — reaching a
true zero-secret BFF.

## Read in this order

1. **[`design.md`](design.md)** — the project design: problem, options, architecture, phased rollout, ADR tension,
   coordination, graduation criteria. **This is the `/design-to-spec` input.**
2. **[`notes/RESEARCH-FINDINGS.md`](notes/RESEARCH-FINDINGS.md)** — verified Microsoft platform research, live Azure
   tenant verification, option analysis, must-prototype list, and corrections to the seed.
3. **[`notes/TENANCY-AND-CREDENTIALS.md`](notes/TENANCY-AND-CREDENTIALS.md)** — which credential applies to each
   deployment shape (Model 1 and Model 2), the provisioning impact, and the one open onboarding question.
4. **[`notes/CREDENTIAL-INVENTORY.md`](notes/CREDENTIAL-INVENTORY.md)** — exhaustive `file:line` audit of every
   place the server authenticates.
5. [`notes/ASSESSMENT.md`](notes/ASSESSMENT.md) — the original seed. **Superseded**; kept as the origin record.

## What the research settled

- **MI-as-FIC is GA** (2025-05-08), and **OBO works with it** — Microsoft documents the OBO+FIC+MI wire protocol.
  The long-standing belief that "OAuth requires a *secret* for OBO" is wrong; it requires a confidential
  **credential**, and Microsoft now ranks secrets last: *"Development and testing only."*
- **Both hard prerequisites are already satisfied in our tenant.** The BFF app registration
  (`SDAP-BFF-SPE-API`, `1e40baad-…`) **already has a working federated identity credential** (GitHub Actions OIDC,
  audience `api://AzureADTokenExchange`, 1 of 20 used), and the App Service runs **user-assigned MI only**
  (`mi-bff-api-dev`) — exactly what MI-FIC requires.
- **Adoption is largely declarative** — `Microsoft.Identity.Web`'s ordered `ClientCredentials` list
  (MI-FIC → KV cert → dev secret), which also solves the local-dev story.
- **No downstream service constrains the credential type** — Dataverse, Graph/SPE, Power BI and Azure OpenAI
  validate only the resulting token.
- **The scope is bigger than the seed drew it**: 8 confidential-client sites, 5 config keys plus a 6th lowercase
  Key Vault alias, a DI-lifetime hazard, a live MI-flag gating defect, and 46 test fixtures to keep green.

## The finding that removes "do nothing"

**ADR-028 does not document the OBO secret as an exception.** Its registry holds only E-1 (SpeAdmin) and E-2
(Azure OpenAI), and its preamble requires every exception to be enumerated by PR. The OBO secret is therefore an
**undocumented deviation from ADR-028's own MUST** — so every outcome, including the status quo, requires ADR text
work. Formal §6.5 escalation is in [`design.md` §7](design.md).

## Sequencing

- **After** #3b (done, live on dev). **Let `dataverse-access-unification-r1` land first** where possible — it
  deletes `DataverseWebApiClient` (a secret consumer) for free.
- **Coordinate with `customer-provisioning-orchestration-r1` now, not later** — it is **executing** (PR #779, ~68%),
  and H3/H4/H9/H10 are already shipped on that branch. Auth-v4's outcome lands as a change request against them.
- Operator + Azure AD admin needed to create the federated identity credential per environment. **No FIC automation
  exists in the repo today** — that is build cost for either secret-free option.
- **Tenancy is settled**: every deployment shape — Model 1, Model 2-in-Spaarke-tenant, Model 2-in-customer-tenant —
  is **intra-tenant**, so **MI-FIC covers all of them** with no special cases and **no certificate path to build**.
  The app registration is created in whichever tenant hosts the deployment. The 20-FIC cap **does not bind** in any
  of our shapes.
- Staged, slot-based rollout with explicit rollback. **Dev is P1v3, so slots are available** (the IaC's `B1` is
  drift). No in-session flips — #3b attempt 1 took dev down.

## Next step

Run **`/design-to-spec`** against [`design.md`](design.md), then `/project-pipeline`.

**All five §11 open questions are now closed** (2026-08-19). Scope is settled (Power BI in; Group 2 non-Entra keys
in; Group 1 out; Group 3 explicitly closed), Phase 0's external-admin dependency is gone, and both cross-project
coordination documents are drafted.

Read alongside `design.md`:

- **[`notes/PHASE-0-LIVE-VERIFICATION.md`](notes/PHASE-0-LIVE-VERIFICATION.md)** — live tenant state; the dev
  MI-FIC was created 2026-08-19; the E4′ correction (the declarative `ClientCredentials` path does **not** exist in
  this codebase — the mechanism is `.WithClientAssertion`).
- **[`notes/PROVISIONING-CHANGE-REQUEST.md`](notes/PROVISIONING-CHANGE-REQUEST.md)** — hand to
  `customer-provisioning-orchestration-r1`. One decision needed from them (§5.1), one doc fix (§5.2), one
  pluggability contract (§5.3). Answers their risk R23.
- **[`notes/COORDINATION-DATAVERSE-ACCESS-UNIFICATION.md`](notes/COORDINATION-DATAVERSE-ACCESS-UNIFICATION.md)** —
  hand to `dataverse-access-unification-r1`. Four-file interlock; not a prerequisite in either direction.
