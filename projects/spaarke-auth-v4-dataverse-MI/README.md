# Spaarke Auth v4 — Zero-Secret BFF Confidential Credential (OBO → MI Federated Credentials)

> **Status**: **SPEC COMPLETE** — `spec.md` generated + ADR-checked 2026-08-19 · **ready for `/project-pipeline`** · **Epic**: AUTH & SSO (#426)
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
- ~~**Adoption is largely declarative**~~ — **CORRECTED 2026-08-19 (E4′).** `Microsoft.Identity.Web`'s ordered
  `ClientCredentials` list is the documented mechanism, but **this codebase cannot use it**: zero
  `EnableTokenAcquisition` / `ITokenAcquisition` / `ClientCredentials` in any `.cs`, and `Spaarke.Dataverse` has no
  Identity.Web reference at all. The mechanism is `.WithClientAssertion(...)`, and the ordered fallback the whole
  rollback story rests on must be **built**, not inherited.
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

- **After** #3b (done, live on dev). **`dataverse-access-unification-r1` is NOT a prerequisite** — the earlier
  "let it land first" framing is retracted; expect parallel execution against a four-file interlock.
- **`customer-provisioning-orchestration-r1` has ACCEPTED and APPLIED the change request** (2026-08-19,
  [`notes/AUTH-V4-CHANGE-REQUEST-RESPONSE.md`](notes/AUTH-V4-CHANGE-REQUEST-RESPONSE.md)): Model 1 = one shared
  multitenant app registration (no per-customer FIC); Model 2 = per-customer app registration + FIC; invariant
  **I6** adopted; risk **R23** closed; pluggability contract accepted as their FR-39.
- **FIC provisioning automation is now ours to land** — no FIC automation exists in the repo (the dev FIC was
  created by hand 2026-08-19). Their **task 130 (Wave G-3)** will invoke our extension to
  `Register-EntraAppRegistrations.ps1` if it lands first, else build its own. This is **spec FR-C4**, the one item
  outside the dev-only boundary.
- **Tenancy**: Model 1 and Model 2-in-Spaarke-tenant are intra-tenant and covered by MI-FIC. ⚠️ **One check
  outstanding** — provisioning's reply describes Model 2 as trusting the *shared* BFF UAMI, which would be
  cross-tenant (and therefore impossible for MI-FIC) in the customer-tenant shape. Raised back in
  [`notes/PROVISIONING-CHANGE-REQUEST.md`](notes/PROVISIONING-CHANGE-REQUEST.md) §9.2; must settle before their
  Wave G-3. The 20-FIC cap does not bind in any shape.
- Staged, slot-based rollout with explicit rollback. **Dev is P1v3, so slots are available** (the IaC's `B1` is
  drift). No in-session flips — #3b attempt 1 took dev down.

## Next step

Run **`/project-pipeline projects/spaarke-auth-v4-dataverse-MI`**.

[`spec.md`](spec.md) is generated and ADR-checked — **23 FRs across 6 workstreams**, 6 NFRs, 16 success criteria.
All five design §11 open questions are closed, Phase 0's external-admin dependency is gone, and both cross-project
coordination documents have been sent (provisioning has replied and applied).

`/adr-check` (2026-08-19) returned **9 compliant · 4 warnings · 1 violation**, all folded into the spec. The
violation was mechanical and would have reddened CI on the first PR: the new `IClientAssertionProvider` →
`ManagedIdentityAssertionProvider` pair pushes `ADR010_DITests.cs:164`'s 1:1-interface ceiling from 153 to 154, so
the ceiling must be raised in the same PR with the seam justification.

Read alongside `design.md`:

- **[`spec.md`](spec.md)** — the implementation specification (`/project-pipeline` input).

- **[`notes/PHASE-0-LIVE-VERIFICATION.md`](notes/PHASE-0-LIVE-VERIFICATION.md)** — live tenant state; the dev
  MI-FIC was created 2026-08-19; the E4′ correction (the declarative `ClientCredentials` path does **not** exist in
  this codebase — the mechanism is `.WithClientAssertion`).
- **[`notes/PROVISIONING-CHANGE-REQUEST.md`](notes/PROVISIONING-CHANGE-REQUEST.md)** — hand to
  `customer-provisioning-orchestration-r1`. One decision needed from them (§5.1), one doc fix (§5.2), one
  pluggability contract (§5.3). Answers their risk R23.
- **[`notes/COORDINATION-DATAVERSE-ACCESS-UNIFICATION.md`](notes/COORDINATION-DATAVERSE-ACCESS-UNIFICATION.md)** —
  hand to `dataverse-access-unification-r1`. Four-file interlock; not a prerequisite in either direction.
