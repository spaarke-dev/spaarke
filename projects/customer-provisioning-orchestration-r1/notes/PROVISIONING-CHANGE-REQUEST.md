> **Canonical source**: `c:/code_files/spaarke-wt-spaarke-auth-v4-dataverse-MI/projects/spaarke-auth-v4-dataverse-MI/notes/PROVISIONING-CHANGE-REQUEST.md`
>
> **This file is a MIRROR** per `notes/auth-v4-integration-remediation-plan.md` §6 canonical-copy rule.
> Refresh before reading — the canonical is authoritative. Last refreshed by `customer-provisioning-orchestration-r1` task 205f on 2026-08-26 (replaced the 280-line stale mirror; auth-v4 §10 addendum + 2026-08-25 CORRECTION captured).
>
> **Do not edit this mirror in place** — edit the canonical, then re-run the mirror refresh (currently manual `cp`; automated refresh is a follow-on).

---

# Change Request → `customer-provisioning-orchestration-r1`

## The BFF confidential credential is moving off client secrets

> **From**: `spaarke-auth-v4-dataverse-MI` · **Date**: 2026-08-19 · **Status**: ✅ **ACCEPTED + APPLIED by
> provisioning 2026-08-19** — see [`AUTH-V4-CHANGE-REQUEST-RESPONSE.md`](AUTH-V4-CHANGE-REQUEST-RESPONSE.md).
> **auth-v4's replies to their open items are in §9 below.**
> **Type**: change request against **shipped** handlers on PR #779 (~68% executed), not input to a design
> **Decision authority**: ADR-028 **Amendment A4** + exception **E-3**, applied 2026-08-17
> **Evidence**: [`PHASE-0-LIVE-VERIFICATION.md`](PHASE-0-LIVE-VERIFICATION.md) ·
> [`RESEARCH-FINDINGS.md`](RESEARCH-FINDINGS.md) · [`TENANCY-AND-CREDENTIALS.md`](TENANCY-AND-CREDENTIALS.md) ·
> [`CREDENTIAL-INVENTORY.md`](CREDENTIAL-INVENTORY.md)

---

## 0. Read this first

**Please run your own independent verification before acting on anything below.**

This assessment was built against `master` and against the live dev tenant on **2026-08-17 → 2026-08-19**. Your
branch is ~68% executed and has shipped H3, H4, H9, H10 and the UAMI Bicep — **changes on your branch that we
could not see may already invalidate specific claims here**, particularly:

- anything we state about `model1-shared.bicep` / `model2-full.bicep` / `customer.bicep`, which we read *from your
  branch* but which may have moved since;
- anything we state about H3/H4 handler internals;
- the identity topology in §5, where we are explicitly reasoning from your docs and flagging that they conflict.

Where we assert something about your code, treat it as **a question we are asking**, not a finding we are
reporting. Where we assert something about the BFF runtime, the Entra objects, or the live Azure state, we have
`file:line` or live `az` evidence and are reasonably confident.

## 1. TL;DR — what changes for you

| | Today (shipped) | Target |
|---|---|---|
| **H3** app registration | creates app reg + ~14 Graph/Dynamics grants + **a 24-month client secret** | creates app reg + grants + **a federated identity credential**; no secret |
| **H4** Key Vault | creates + populates `BFF-API-ClientSecret` | **deleted** — there is no secret to store |
| **Rotation** | `H4-rotate` handler + 24-month expiry alarms + U-CB-5 / U-CB-6 | **retired entirely** — Azure manages the credential lifecycle |
| **`spec.md:242` / `design.md:783`** never-delete MUST | in force | **rewritten** — the MUST it protects no longer exists |
| **Certificate provisioning** | (was on the risk list) | **dropped, not deferred** — no Spaarke deployment shape needs one |
| **New automation to build** | — | **FIC creation** (one step, one place) |
| **Net** | permanent per-customer secret + rotation cost, compounding at 20+ customers | **simpler** — one creation step replaces creation *plus* a rotation lifecycle |

**Your risk R23** (`design.md:1429`) flagged MI-FIC for Model 2, including the 20-FIC cap. This document is the
answer to R23. Short version: **MI-FIC works for every shape we deploy, and the cap is a non-factor** (§4).

## 2. Current state — what you may not know

Three things happened outside your branch that change the premises H3/H4 were designed against.

### 2.1 App-only Dataverse already moved to Managed Identity

`code-quality-and-assurance-r3` task 011 (**"#3b"**) migrated the app-only Dataverse paths from
`ClientSecretCredential` to `DefaultAzureCredential`. It is **live on dev** — `Graph__ManagedIdentity__Enabled=true`
is set on `spaarke-bff-dev` today. `DataverseServiceClientImpl` and `DataverseWebApiService` are flag-gated onto MI.

If your handlers assume the BFF needs a secret for *app-only* Dataverse access, that assumption is already stale.

### 2.2 The secret survived for one reason, and that reason was wrong

`#3b` could not remove `BFF-API-ClientSecret` because the same secret backs **OBO** — delegated user auth — across
Graph, Dataverse, Power BI and the M365 Copilot agent. Every prior audit concluded the secret was therefore
permanent. The r3 auth surface map states it plainly: *"Verdict: NEVER-REMOVE."*

That conclusion rested on a single sentence in `.claude/constraints/auth.md:108` — *"OBO flow (OAuth spec requires
confidential client + secret)."*

**OAuth requires a confidential *credential*. A secret is one of three ways to satisfy it**, and Microsoft now
ranks it last: *"Development and testing only."* The other two are a certificate and a **federated identity
credential**, and Microsoft documents the OBO + FIC + managed-identity wire protocol explicitly. The constraint
file was corrected on 2026-08-17, along with ADR-028 (Amendment A4 + exception E-3) and the `adr-check` rule that
had been flagging every OBO site with no sanctioned alternative to move to.

**Why this matters to you specifically**: H3/H4 were designed to create and rotate a secret *forever, per
customer*, because the architecture said the secret could never go away. It can.

### 2.3 The platform prerequisites are already satisfied, and the dev FIC now exists

Verified live 2026-08-19 (full detail in [`PHASE-0-LIVE-VERIFICATION.md`](PHASE-0-LIVE-VERIFICATION.md)):

- The BFF app registration `SDAP-BFF-SPE-API` (`1e40baad-…`) is **`AzureADMultipleOrgs`** and already carried a
  **working federated identity credential** (GitHub Actions OIDC) before any of this work — proof that workload
  identity federation is live and functioning on this exact object.
- `spaarke-bff-dev` runs **user-assigned MI only** (`mi-bff-api-dev`) — MI-FIC's hard prerequisite, already met.
- **A second FIC was created on 2026-08-19** trusting `mi-bff-api-dev` to mint client assertions
  (`mi-bff-api-dev-assertion`, id `66bac39a-…`). It is inert until code consumes it, and reversible in one command.

## 3. Future state — the FIC provisioning step

### 3.1 The object to create

```
issuer:    https://login.microsoftonline.com/{TENANT_HOSTING_THE_DEPLOYMENT}/v2.0
subject:   {principalId of the UAMI that will perform OBO}   # object ID, NOT clientId
audiences: [ api://AzureADTokenExchange ]                     # exact string; sovereign clouds differ
```

Working dev example, for reference:

```bash
az ad app federated-credential create --id 1e40baad-e065-4aea-a8d4-4b7ab273458c --parameters '{
  "name": "mi-bff-api-dev-assertion",
  "issuer": "https://login.microsoftonline.com/a221a95e-6abc-4434-aecc-e48338a1b2f2/v2.0",
  "subject": "9fd47efb-7962-492b-ac44-e5ccd0268ebb",
  "audiences": ["api://AzureADTokenExchange"]
}'
```

A `Microsoft.Graph/applications/federatedIdentityCredentials` Bicep resource is the declarative equivalent if you
prefer IaC over CLI — note it hangs off the **application**, not the managed identity (§4).

### 3.2 Where it belongs in your estate

**No FIC automation exists anywhere in the repo today.** We verified this exhaustively, including gitignored and
untracked paths: zero scripts and zero Bicep resources create a federated identity credential for any runtime
identity. Even the existing GitHub Actions OIDC FIC was **hand-run** (`.github/D-11:61`, Application Administrator
required). The only trace is a pre-authorized permission entry in `.claude/settings.local.json` that nothing
invokes.

So this is genuinely new automation — **but it is not greenfield**, because the scripts it belongs in already
exist and are already idempotent and tenant-aware:

| Script | Today | Proposed |
|---|---|---|
| `scripts/Register-EntraAppRegistrations.ps1` | creates app registrations + grants | **primary home** for FIC creation — it already owns the app-registration lifecycle and is tenant-aware |
| `scripts/Rotate-Secrets.ps1` | rotates `BFF-API-ClientSecret` | **the BFF path retires**; keep the script for genuinely-remaining secrets (§6) |
| `scripts/Seed-ProductionKeyVault.ps1` | seeds the secret into KV | BFF-secret path retires |
| `scripts/Provision-Customer.ps1` | per-customer orchestration | calls the new FIC step instead of the secret step |
| `scripts/Configure-ProductionAppSettings.ps1` | fans one secret into 5 app-setting keys (`:69-81`) | fan-out retires; the UAMI clientId is configured instead |

⚠ **Caveat we owe you**: our inventory originally cited only 2 scripts touching `ClientSecret`. An independent
sweep found **11**: the five above plus `Deploy-Release.ps1`, `Deploy-DataverseSolutions.ps1`,
`Reconcile-DemoEnvironment.ps1`, `Test-EntraAppRegistrations.ps1`, `Test-SharePointToken.ps1`,
`naming-conformance-check.ps1` — plus roughly 25 documents. **Removing the secret is an operational-estate change,
not a Key Vault delete.** Please size accordingly on your side, and tell us if your branch adds more.

### 3.3 Failure modes to design for

These are different from secret failure modes, and both are quiet:

- **Silent misconfiguration** — a wrong issuer, subject, or audience **creates successfully** and fails only at
  token exchange, with a generic error. There is no validation at creation time. The commonest error is using the
  UAMI's **clientId** where the **principalId** is required. Automation must verify by *performing an exchange*,
  not by checking that creation returned 200.
- **Propagation delay** — `AADSTS70021` for a few minutes after creation. **Retry logic is mandatory** in any
  provisioning flow that creates a FIC and then immediately exercises it. Without it, onboarding will appear to
  fail intermittently for reasons that look like a permissions problem.

There is also an **identity-selection trap**. The dev subscription has five UAMIs, one of which
(`spaarke-bff-identity`) is named as though it were the BFF's but is **not attached** to the BFF App Service. Any
automation that resolves a UAMI by name is a silent-failure generator. Resolve by resource ID.

## 4. R23 answered — the 20-FIC cap does not bind

The cap counts credentials **held by** an object. In MI-as-FIC the FIC object lives on the **app registration**;
the UAMI is only the issuer and holds nothing. So the question is *how many UAMIs must one app registration
trust?* — and in every shape we deploy the answer is **one**:

- **Model 1** — many app registrations (or one, per §5), all trusting the **single shared BFF UAMI**
  (`sprk-{env}-shared-bff-uami`, your task-029 slot-swap stability fix). Each app registration holds **1 of 20**,
  regardless of customer count.
- **Model 2** — each stamp has its own app registration *and* its own UAMI. **1 of 20.**

The cap would bind only in the inverse shape — one app registration trusting many UAMIs — which requires
per-customer compute behind a single shared OAuth identity. Neither model does that. (And it could not be
engineered around: flexible FICs, which lift the cap via claim-matching, are preview **and explicitly do not
support managed identities**.)

Live headroom on the dev app registration: **2 of 20 used**.

**Please close R23 as a non-factor** unless your branch introduced a shape we haven't seen.

## 5. What we need from you — one decision, one doc fix, one contract

### 5.1 DECISION — which app registration does the shared Model 1 BFF act as?

> ## ✅ DECIDED 2026-08-25 (owner): **Reading 1 — ONE shared multitenant app registration for Model 1.**
>
> **What this means for you concretely:**
>
> - **One FIC, created once.** Customer onboarding creates **no** federated credential at all — the
>   per-customer FIC step described in §3.2 does **not** apply to Model 1.
> - The BFF does **not** select an app registration per request and does **not** validate 20+ audiences.
> - **`spec.md:236` and `design.md:57`** ("per-customer app registrations in both models") are hereby
>   **scoped to Model 2 only**. They read as written for Model 2 and generalised without re-testing against
>   Model 1's single shared BFF App Service. Please make that edit on your side — it is the one place the
>   estate still contradicts this decision.
> - §5.4's proposed invariant **I6 stands** (Model 1 only), consistent with this.
>
> **Why Reading 1** — three facts, all verifiable today:
>
> 1. The live app registration is **already `AzureADMultipleOrgs`** (verified 2026-08-19). That *is* the
>    multitenant shape; nothing needs to change to adopt it.
> 2. Model 1 deploys **one shared BFF App Service**. Under Reading 2 that single app would have to resolve
>    the correct app registration per request and accept 20+ audiences — substantial BFF complexity bought
>    for no capability.
> 3. Onboarding gets **simpler, not harder**: no per-customer credential object to create, rotate, or leak.
>
> **What this does NOT decide**: Model 2 (customer-owned tenants) keeps per-customer app registrations —
> credentials attach to the *application object*, so a customer-tenant UAMI can only be trusted by an app
> registration that lives in that tenant. §9.2's raised question about Model 2's FIC issuer tenancy is
> **still open** and unaffected by this.
>
> **If you disagree**, say so before wiring it — reversing later means adding per-request app-registration
> selection to a shared BFF, which is the expensive direction.

*(Original framing retained below for the reasoning trail.)*

This is yours to make. It is not a feasibility or scaling question; **MI-FIC works either way**. It is an
identity-design question, and it decides whether onboarding gains a per-customer step.

- **Reading 1 — one shared multitenant app registration.** The live BFF app reg is already `AzureADMultipleOrgs`
  (verified 2026-08-19), the standard shape for one app serving many tenants. → **One FIC, created once. Customer
  onboarding creates no federated credential at all.**
- **Reading 2 — one app registration per customer.** Each carries its own FIC pointing at the *same* shared BFF
  UAMI. → **Onboarding gains a per-customer FIC step**, and the BFF must select the correct app registration per
  request.

**Why we can't settle it**: your `design.md:57` (D2) states "per-customer app registrations in both models" and
`spec.md:236` makes it a binding MUST — but Model 1 deploys a *single shared* BFF App Service, and one app
validating 20+ audiences is an unusual design. Nothing in the provisioning docs reconciles these. Our working
assumption is **Reading 1** — that `spec.md:236` was written for Model 2 and generalised without re-testing it
against the shared-BFF composition — but **that is inference, not evidence, and it is your call.**

Knock-on effects of the choice: H10's Dataverse App User registrations and T3's 14-role Graph parity checks are
currently per-app-registration; and it determines which object each customer admin consents to.

### 5.2 DOC FIX — `design.md:1006` reads as licensing a shape we've ruled out

Two consecutive sentences state opposite things:

> *"…for Model 2 customer-owned tenants, **register the same multitenant BFF app in the customer tenant** (per D18
> consent-capture)."* → implies one Spaarke-owned app object, SP provisioned by consent
>
> *"The app registrations below **live in whichever tenant hosts the deployment**…"* → implies a distinct app
> object created in the customer's tenant

**The second is the intended rule.** The first describes the one shape that breaks MI-FIC — a Spaarke-owned app
registration with customer-tenant compute — because credentials attach to the *application object*, which would
stay in the Spaarke tenant, so no customer-tenant UAMI could ever be trusted by it. That shape was **explicitly
ruled out by owner decision on 2026-08-18** and would be the only reason to build certificate provisioning.

Please correct the first sentence so it does not read as licensing it. **Do not remove the surrounding
mechanism** — `AzureADMultipleOrgs`, the D18 `consent-callback` endpoint, and U-CB-3 re-consent are **correct and
necessary for Model 1**. Just scope them to Model 1 explicitly.

Full reasoning: [`TENANCY-AND-CREDENTIALS.md`](TENANCY-AND-CREDENTIALS.md) §3.1.

### 5.3 CONTRACT — keep the credential step pluggable

The single ask against shipped code: H3/H4's *"configure BFF confidential credential"* must be swappable between
**secret** and **FIC** without restructuring the handler. Auth-v4 rolls out per environment with the secret
retained as an ordered fallback until Phase 5, so both must be creatable during the transition.

### 5.4 RAISE — proposed invariant I6 (Model 1 only)

Under MI-FIC, the shared BFF UAMI can mint an assertion for **any** app registration that trusts it. Today part of
Model 1's isolation boundary is resource-level: the BFF must read customer X's secret from customer X's Key Vault.
That boundary becomes purely **code-level** — nothing but correct tenant routing stops the process authenticating
as the wrong customer.

Model 1 has already accepted logical-over-physical isolation (the ADR-027 Path A exception, invariants I1–I5), and
the practical delta is small since the shared BFF already needs read access to every per-tenant Key Vault. But it
deserves naming:

> **I6 (proposed)** — the app registration used for an OBO exchange MUST be derived from per-tenant request
> context; no default or fallback app registration. ArchTest-enforced, same pattern as I1–I5.

Load-bearing under Reading 2; worth stating either way. **Yours to adopt or reject** — we are not adopting it
unilaterally.

## 6. What does *not* change

- **Per-customer SpeAdmin secrets** (ADR-028 **E-1**) stay. They authenticate *other applications* — the
  per-customer SPE container-type owning apps — not the BFF identity. `sprk_specontainertypeconfig` rows and their
  KV secret names are untouched.
- **Inbound auth** is unaffected. JWT validation, the CIAM scheme, webhook HMAC keys and the API-key schemes
  validate what arrives; they are indifferent to how the BFF authenticates outbound.
- **No downstream service constrains the credential type.** Dataverse, Graph/SPE, Power BI and Azure OpenAI
  validate only the resulting token; how the client authenticated is invisible to them.
- **Certificate provisioning is dropped, not deferred.** No deployment shape requires one. The certificate remains
  ADR-028 A4's sanctioned alternative as *policy* (and is already in production use by `CiamGraphClientFactory`
  for the CIAM provisioner), but there is nothing for you to build.

## 7. Sequencing

Auth-v4 rolls out per environment: prerequisite fixes → provider seam (secret retained as ordered fallback) →
per-environment flip via slot swap → secret removal after a soak. **The secret is not deleted until the final
phase**, so nothing in your pipeline breaks on our schedule.

What we need from you, in order:

1. **Now** — the §5.1 decision and the §5.2 doc fix. Both are cheap and both are on your critical path, not ours.
2. **Before your H3/H4 land in a customer-facing state** — the §5.3 pluggability contract.
3. **At your convenience** — accept or reject I6 (§5.4); close R23 (§4).

Conflicts run through `/conflict-check` per PR. We touch `src/server/**` and `.claude/adr|constraints|patterns`;
your branch touches `infrastructure/bicep/**`, `scripts/**` and provisioning handlers. **`scripts/` is the
overlap** — specifically `Register-EntraAppRegistrations.ps1`, `Rotate-Secrets.ps1`, `Seed-ProductionKeyVault.ps1`
and `Configure-ProductionAppSettings.ps1`. Let's agree who edits those and when.

## 8. Open items we are carrying, that touch you

| # | Item | Owner |
|---|---|---|
| 1 | `config/spaarke-resources.yaml` records `sign_in_audience: AzureADMyOrg`; **live is `AzureADMultipleOrgs`**. Stale. | auth-v4 |
| 2 | The same file names the dev App Service `spe-api-dev-67e2xz` / `spe-infrastructure-westus2`. **That resource does not exist** — live is `spaarke-bff-dev` / `rg-spaarke-dev`. Any automation written against this inventory targets a phantom. | auth-v4, but **check your Bicep params** |
| 3 | `docs/architecture/auth-azure-resources.md` claims system-assigned MI (live is user-assigned) and contradicts itself on which app registration owns `BFF-API-ClientSecret` (`:705-708` vs `:349`). The live password credential is named `Dataverse-Checkout-20251218`, which matches neither cleanly. **Portal-confirm before automating any removal.** | auth-v4 |
| 4 | `stacks/dev.bicepparam:12` declares `B1`; live is **P1v3**. IaC drift — and it is the difference between "slots impossible" and "slots available". | shared |
| 5 | A duplicate lowercase KV alias **`bff-api-client-secret`** is used by the Office add-in deploy. Any removal ignoring it breaks the add-in. | auth-v4 |
| 6 | Master IaC creates a **system-assigned** identity while live uses a UAMI; the UAMI Bicep lives on *your* branch. | provisioning |

---

## 9. auth-v4 replies to provisioning's response (2026-08-19)

Answering [`AUTH-V4-CHANGE-REQUEST-RESPONSE.md`](AUTH-V4-CHANGE-REQUEST-RESPONSE.md). Their split (Model 1 =
Reading 1, Model 2 = Reading 2), invariant **I6** adoption, **R23** closure, **FR-39** pluggability contract and
the §5.2 doc fix are all accepted as applied — no further ask from us on any of those.

### 9.1 Answer to their judgment call #3 — the "already-migrated-to-FIC" KV sentinel contract

**Omit the secret entirely. Do not write a sentinel value.**

The BFF-side provider (spec FR-B2) performs **ordered credential selection** — MI-FIC → Key Vault certificate →
dev secret — and falls through when a higher-priority credential is absent. That makes absence the well-defined,
already-implemented signal:

- **Omit** → the selector finds no secret at the lowest tier and uses the FIC. Clean, and it is the same code path
  local development and post-Phase-5 production take.
- **Sentinel** → strictly worse. The selector cannot distinguish a sentinel string from a real secret, so if MI-FIC
  ever fails to resolve, it will attempt a token acquisition **with the sentinel** and fail at Entra with an opaque
  `AADSTS7000215` (invalid client secret) instead of falling through or failing fast with an actionable message.
  A sentinel converts a clean fallback into a confusing runtime error.

So: **H4 should skip secret creation for a FIC-migrated customer rather than writing a placeholder.** If you need a
positive marker that migration happened, put it somewhere that is not the credential slot — a provisioning-state
field or a KV tag — never a value in the secret the credential selector reads.

### 9.2 ⚠️ Raised back — Model 2's FIC issuer may break the same-tenant rule

Your TL;DR states Model 2 uses a per-customer BFF app registration **"+ a FIC trusting the shared BFF UAMI."**

For **Model 2 in the Spaarke tenant** that is intra-tenant and correct. For **Model 2 in a customer's tenant** it
is not: the app registration is customer-side while the shared BFF UAMI is in the Spaarke tenant, and **Entra
requires the app registration and the UAMI to be in the same tenant** — it is the single hard platform constraint
(ADR-028 A4; [`TENANCY-AND-CREDENTIALS.md`](TENANCY-AND-CREDENTIALS.md) §1). Cross-tenant *resource* access is
supported; a cross-tenant *FIC issuer* is not.

`TENANCY-AND-CREDENTIALS.md` §3 row 3 assumed the customer-tenant stamp issues from **its own stamp UAMI**, which
is what makes all three shapes intra-tenant. Either:

- **(a)** the customer-tenant shape uses its **own stamp UAMI** as the FIC issuer — our assumption, and the only
  reading under which MI-FIC covers every shape; or
- **(b)** it genuinely must trust the shared Spaarke UAMI, in which case **MI-FIC is structurally impossible for
  that shape** and it needs the ADR-028 A4 sanctioned alternative — a Key Vault certificate. That would reopen the
  certificate-provisioning work we recorded as *dropped, not deferred*.

We believe (a) is what you mean and the sentence is just compressed. **Please confirm before Wave G-3 task 130
executes**, because the failure mode is silent: a cross-tenant FIC **creates successfully** and fails only at
token exchange.

### 9.3 `Register-EntraAppRegistrations.ps1` FIC extension — accepted, with a caveat

Accepted as ours. It is now **spec FR-C4** with acceptance criteria covering idempotency, `AADSTS70021` retry, and
**verification by performing an actual token exchange** rather than trusting a successful create.

Caveat on timing: auth-v4's own rollout is scoped **dev-only**, so FR-C4 is the one item in our spec that exists
purely to serve your Wave G-3 dependency. We will sequence it early, but if your Wave G-3 dispatches first, take
the task-130 fallback path and we will reconcile — do not block on us. Please give us as much notice of the G-3
dispatch date as you can.

### 9.4 Accepted with no action

- **Your item 2** (Bicep phantom-name cross-check in Wave G-1 wrap-up) — agreed. Note our own finding softened on
  review: `config/spaarke-resources.yaml` correctly records the 2026-05-24 migration and marks the old app
  `status: legacy`. The genuine residue is one line — `STAGING_APP_NAME: spe-api-dev-67e2xz` (`:558`), a live
  GitHub Actions repo-secret mapping to a decommissioned resource. That is CI estate, not yours or ours.
- **Your item 6** — accepted: r1 Phase C is authoritative for UAMI Bicep and supersedes master IaC. We have
  removed it from our follow-up list.
- **Your judgment calls #1, #2, #4** (doc-location drift, FR renumbering, §9A consistency sweep) — all fine, no
  objection.
- **Your MI-FIC cap-inversion note** — appreciated. For the record the same inversion is what made "OBO requires a
  secret" survive three prior audits here; the failure mode is a plausible mechanism reasoned about from the wrong
  end, not missing evidence.

---

## 10. DELIVERED — `Register-EntraAppRegistrations.ps1` FIC extension has landed (2026-08-21)

Task 030 / spec **FR-C4** is complete and on `work/spaarke-auth-v4-dataverse-MI`. This is the item §9.3
committed to sequencing early for your **Wave G-3 task 130** — it landed first, so **do not build the
duplicate**. Your branch currently contains zero federated-credential code, so nothing needs unwinding.

### Invocation contract

Two entry points, both on the existing script (no new file):

```powershell
# 1. Standalone — app-registration/Key Vault/consent steps are skipped
.\Register-EntraAppRegistrations.ps1 -FicOnly `
  -FederatedCredentialAppId <app-reg-id> `
  -UamiResourceId "/subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.ManagedIdentity/userAssignedIdentities/<uami>" `
  -TenantId <tenant>

# 2. Preview without changing anything
.\Register-EntraAppRegistrations.ps1 -DryRun -CreateFederatedCredential `
  -FederatedCredentialAppId <app-reg-id> -UamiResourceId <arm-id> -TenantId <tenant>
```

⚠️ **Invoke it; do not dot-source it.** An earlier draft of this notice offered a
`-ExportFunctionsOnly` dot-source mode for `Provision-Customer.ps1`. **That has been removed** — our
code-review gate proved it silently overwrote the *caller's* `$TenantId` with this script's
hard-coded production default (dot-sourcing executes `param()` in your scope), flipped your
`$ErrorActionPreference` to `Stop`, and replaced any same-named `Write-*` helpers you had, dropping
surplus arguments without erroring. For FIC work a wrong tenant is a wrong issuer — a credential that
creates cleanly and never works, which is the failure class this whole project exists to remove. If
you need an in-process contract rather than a subprocess call, tell us and we will extract a proper
`.psm1` module; the output helpers would have to move with it, which is why we did not do it inline.

`-UamiResourceId` is an **ARM resource ID, not a name** — five UAMIs exist in dev and
`spaarke-bff-identity` is a decoy that is not attached to the BFF.

### Exit codes — please branch on these

| Exit | Meaning | Your action |
|---|---|---|
| `0` | Verified by a real token exchange | Proceed |
| `1` | Fault — create failed, drift refused, structurally invalid, or exchange rejected | Stop; the message carries Azure CLI's verbatim error |
| `2` | Structurally correct but **not exchange-provable from this host** | Not a failure. See below |

**Exit `2` will be your normal result** if you run provisioning from an agent that does not carry the
target UAMI: a managed-identity assertion can only be minted from inside Azure on compute holding the
identity. Either pass `-AssertionToken` (minted on that compute), or pass `-AllowUnverified` and
schedule verification for after the App Service exists. We deliberately did **not** make "created" imply
"working" — a misconfigured FIC creates cleanly and fails only at exchange.

### Two things worth knowing

1. **Idempotency is keyed on `(issuer, subject, audience)`, not the credential name.** Entra enforces
   that triple's uniqueness per application itself and *rejects* a second credential carrying it. A
   name-only check therefore does not produce a duplicate — it produces a **failed run against a
   credential that was already correct**. We hit exactly that on the first live run and fixed it; if
   task 130 had been written against name-matching, it would have hit it too.
2. **Cross-tenant pairs are refused at runtime**, with a message citing §9.2 — see below.

### ⚠️ §9.2 is still unanswered, and it now has a runtime consequence

Our §9.2 question — whether Model 2's customer-tenant stamp trusts its **own stamp UAMI** (reading a) or
the **shared Spaarke UAMI** (reading b) — has not been answered. The script now **refuses** a
cross-tenant (app-registration, UAMI) pair rather than attempting it, because Entra requires both in the
same tenant and a cross-tenant FIC **creates successfully and fails only at token exchange**.

Practically: under reading (a) you are unaffected. Under reading (b), MI-FIC is structurally impossible
for that shape and it needs the ADR-028 A4 certificate alternative — which would reopen the
certificate-provisioning work recorded as *dropped, not deferred*.

**Please answer before Wave G-3 task 130 executes.** The failure mode is loud now instead of silent,
which is an improvement, but the underlying question is unchanged.

### Merge coordination

Your PR **#779** rewrites this same script (+707 / −257, the idempotency-contract rewrite). We simulated
the three-way merge: **one conflict hunk**, where both sides append to `param()` — resolution is keep
both (your `$SecretExpiryMonths` plus our FIC parameters). Everything else merges cleanly, and we
specifically checked that our `-FicOnly` Key Vault skip lands correctly on your restructured pre-flight.
Whoever merges second resolves that one hunk.

Full rationale + verification evidence:
[`notes/decisions/030-fic-automation.md`](decisions/030-fic-automation.md).

---

## 11. 🔔 ONE THING WE ARE HANDING YOU — your first FIC creation is the live test for two invariants

**Added 2026-08-24 by auth-v4 task 031.** Short version: two safety invariants in the script we gave you
are proven *structurally* but never *live*, and **Wave G-3 task 130 is the first thing that will exercise
them for real**. Nothing is broken and nothing is asked of you up front — but if task 130's FIC step
behaves oddly, read this before debugging.

### The two invariants

| # | Invariant | Proven | Not proven |
|---|---|---|---|
| 1 | A FIC whose `subject` is the UAMI's **clientId** instead of its **principalId** must be **DETECTED by the exchange verification**, not reported as success | The script's detection logic, at task 030 | The end-to-end run, against a real assertion |
| 2 | **AADSTS70021** immediately after FIC creation must be **retried**, not surfaced as a failure | The retry logic, at task 030. The flap itself is measured: ~8 failures over ~130 s as replicas converge | The retry firing against a genuinely fresh FIC |

### Why auth-v4 could not close them

Both need a **real managed-identity assertion**, and that can only be minted by code running inside the
App Service **app container**. Measured 2026-08-24: Kudu `/api/command` executes shell fine, but
`IDENTITY_ENDPOINT` / `IDENTITY_HEADER` are **absent** from the Kudu sidecar, and no visible
`/proc/*/environ` exposes them. The BFF itself has no endpoint that emits its assertion and must never
have one, and extracting an assertion to a workstation was refused — it is a live credential capable of
authenticating as the BFF app.

auth-v4 also has **no remaining task that creates or changes a FIC** (032, 033 and 090 were checked). The
dev FIC already exists and is proven working. So auth-v4's own UAT will never touch this path — which is
exactly why it is being handed to you rather than left to be "caught in testing".

### What this means for task 130 — practical, not procedural

1. **Your first `-FicOnly` run against a new environment IS the live test.** It costs you nothing extra.
2. **If the FIC appears to create successfully but token exchange later fails**, suspect invariant 1
   first: check that the FIC `subject` is the UAMI's **principalId** (the managed identity's
   service-principal objectId), **not** its clientId. This is the single commonest silent error in
   MI-FIC setup, and the two ids look interchangeable.
3. **If you see AADSTS70021 right after creating a FIC, do not treat it as a failure** — it is the
   convergence flap. The script retries; give it the ~2 minutes. Note it is **70025**, not 70021, that
   auth-v4 measured on *changes* to an existing FIC.
4. **Please tell auth-v4 (or whoever owns the script by then) what you observed** — a one-line "FIC
   created, exchange verified first try" closes both invariants live, permanently, for free.

### Not an ask, and not a defect

There is no Pester harness in the repo, so neither invariant is re-executed by anything today; a one-shot
live run by auth-v4 would not have protected you either. Handing it to the first real consumer is
strictly better than a synthetic run against throwaway infrastructure, because it tests the path that
actually matters, in the environment that actually matters.

Full reasoning: [`notes/decisions/031-obo-verification-dev.md`](decisions/031-obo-verification-dev.md) §6.1.


---

# 10. ADDENDUM (2026-08-25) — what §1–§9 did NOT tell you

> **Read this if you read anything.** Everything above was written **2026-08-19** and scoped to the *app
> registration credential* (the FIC). Tasks **051** and **053** cut over on **2026-08-24** and changed two
> more credentials, and the live app settings contract moved with them. **Sections 1–9 are incomplete, not
> wrong.** This addendum is the delta.
>
> **Short answer to "does provisioning need to change what it packages?" — yes, in five places.**

> ### ✏️ CORRECTION (2026-08-25, same day) — two of the five were already handled
>
> After writing §10 we checked [`docs/guides/auth-deployment-setup.md`](../../../docs/guides/auth-deployment-setup.md)
> rather than assuming. Task 033's doc sweep had **already** folded in two of the five:
>
> - **Delta 4 (`keyVaultReferenceIdentity`)** — covered in §1 Prerequisites.
> - **Delta 5 (Dataverse application user for the UAMI)** — covered by its own §6, including the exact
>   403 / `0x80072560` symptom.
> - Deltas **1–3** (Service Bus, AI Search, the credential app settings) were the genuine gaps. The
>   credential settings were partly covered; the **Group-2 RBAC was not covered at all**.
>
> **Genuinely new work is therefore 2 of 5, not 5 of 5.** Overstating the delta is its own failure — it
> invites a reader to discount the whole list.
>
> **This contract has since been PROMOTED** into `auth-deployment-setup.md` **§5.1 — Azure data-plane RBAC
> for the UAMI**, and the retired Key Vault secrets are struck through in its §4 table with the
> `Deploy-AllIndexes.ps1 -CutoverBffSettings` warning attached. **Use the guide as the operational source.**
> §10 below stays as the origin/reasoning record.

## 10.1 The five deltas

| # | What provisioning does today | What the BFF now needs | Where |
|---|---|---|---|
| 1 | Mints a **Service Bus SAS** and writes `ServiceBus-ConnectionString` to the customer Key Vault | **Nothing.** The BFF authenticates with the UAMI. Set `ServiceBus__FullyQualifiedNamespace` instead and grant the UAMI a Service Bus data role | [`Provision-Customer.ps1:520`](../../../scripts/Provision-Customer.ps1), [`Configure-ProductionAppSettings.ps1:94`](../../../scripts/Configure-ProductionAppSettings.ps1) |
| 2 | Writes `AiSearch--AdminKey` and sets `AiSearch__AdminKey` / `AzureAISearchApiKey` as KV refs | **Nothing.** Set `AiSearch__ManagedIdentity__Enabled=true` and grant the UAMI Search data roles | [`scripts/ai-search/Deploy-AllIndexes.ps1`](../../../scripts/ai-search/Deploy-AllIndexes.ps1) |
| 3 | Does not set the credential-selection settings | **Four new app settings are now load-bearing** (§10.2). Without them a fresh environment falls back to the code-side default and will look for a secret | — |
| 4 | Does not set `keyVaultReferenceIdentity` | **Mandatory.** Without it every `@Microsoft.KeyVault(...)` app setting fails to resolve and the site aborts with **exit 134 (SIGABRT)** | task 001 finding **A** |
| 5 | Registers the **app registration** as a Dataverse application user | The **UAMI** must ALSO be a Dataverse application user, or every app-only Dataverse call 401s | §10.4 |

## 10.2 The exact live contract (copied from `spaarke-bff-dev`, 2026-08-25)

```
Graph__Credentials__Order__0                       = ManagedIdentityFederated   # the ONLY entry
Graph__Credentials__RequireSecretFreeIdentity      = true                       # refuses to BOOT otherwise
ManagedIdentity__ClientId                          = <UAMI clientId>
Graph__ManagedIdentity__ClientId                   = <UAMI clientId>
Graph__ManagedIdentity__Enabled                    = true
ServiceBus__FullyQualifiedNamespace                = <ns>.servicebus.windows.net   # NOT a connection string
Membership__EventPublisher__ServiceBusNamespace    = <ns>.servicebus.windows.net
Membership__JunctionUpdater__ServiceBusNamespace   = <ns>.servicebus.windows.net
AiSearch__ManagedIdentity__Enabled                 = true
AiSafety__ContentSafety__ManagedIdentity__Enabled  = true
```

Site property (**not** an app setting, and **not** copied by `az webapp deployment slot create
--configuration-source`):

```
keyVaultReferenceIdentity = /subscriptions/<sub>/resourcegroups/<rg>/providers/
                            Microsoft.ManagedIdentity/userAssignedIdentities/<uami>
```

⚠️ **`RequireSecretFreeIdentity=true` is fail-fast by design.** A fresh environment that sets it without a
working UAMI + FIC **will not start**. Provision the identity first, then the setting — never the reverse.

## 10.3 Azure RBAC the UAMI now needs (beyond Graph app roles)

Previously carried by the SAS / admin key, so it is new work:

| Resource | Role |
|---|---|
| Service Bus namespace | `Azure Service Bus Data Sender` + `Azure Service Bus Data Receiver` (or Data Owner) |
| Azure AI Search | `Search Index Data Contributor` + `Search Service Contributor` |
| Key Vault | `Key Vault Secrets User` — required for `keyVaultReferenceIdentity` to resolve |
| Content Safety / OpenAI | `Cognitive Services User` |

## 10.4 Dataverse application user — for the UAMI, not just the app registration

`spaarkedev1` carries **two** application users, and both are load-bearing:

| `fullname` | `applicationid` | Why |
|---|---|---|
| `SDAP-BFF-SPE-API` | app registration | OBO — the token's `appid` stays the app registration even under MI-FIC |
| `# mi-bff-api-dev` | **UAMI clientId** | app-only Dataverse — the MI is the principal here |

Its `azureactivedirectoryobjectid` must equal the UAMI's **principalId** (not clientId). Evidence that this is
the live path — every UAT row is stamped `# mi-bff-api-dev` — is in
[`notes/decisions/mi-proof-dataverse-side.md`](decisions/mi-proof-dataverse-side.md).

**This is the one most likely to be missed**, because §1–§9 only ever discussed the app registration.

## 10.5 Two traps worth naming

- **`appsettings.template.json` still declares** `"ServiceBus": "@Microsoft.KeyVault(SecretUri=#{KEY_VAULT_URL}#secrets/ServiceBus-ConnectionString)"`. If you deploy from that template you re-introduce the SAS contract on a BFF that no longer reads it. Tracked as auth-v4 obligation **051-E** (deliberately deferred while the KV rollback copy lives, to 2026-11-23).
- **`Deploy-AllIndexes.ps1 -CutoverBffSettings` re-introduces the key.** ⚠️ *Corrected 2026-08-25 — an earlier
  revision of this bullet claimed the script "silently re-mints a key". **That was wrong.** Its key-resolution
  fallback calls `az search admin-key show`, which **reads** the existing key (`renew` would regenerate), and it
  uses it for **index management** — a legitimate admin-key operation unrelated to the BFF's runtime auth.*
  The actual trap is `-CutoverBffSettings` (script line ~610): it sets `AzureAISearchApiKey` and
  `AiSearch__AdminKey` on the BFF as Key Vault references to `AiSearch--AdminKey` — **a secret that no longer
  exists** — re-introducing the key-based configuration task 053 removed. Do not run that switch against a
  migrated environment; gate it on the secret's existence.

## 10.6 What did NOT change

`Register-EntraAppRegistrations.ps1 -SkipClientSecret` (task 030) is still correct and still the contract for
the app-registration half. §5.1's open decision (one shared multitenant app registration vs one per customer)
is **still open and still yours** — MI-FIC works either way, so it does not block this addendum.
