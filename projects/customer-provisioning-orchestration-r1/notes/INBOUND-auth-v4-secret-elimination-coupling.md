# INBOUND: auth model may change — coordinate the credential-provisioning step

> **From**: `spaarke-auth-v4-dataverse-MI` (seed/research-first, 2026-08-17) · **To**: customer-provisioning-orchestration-r1
> **Type**: cross-project dependency notice — **not a blocker**, but do not bake in the secret-based auth model prematurely.

## What changed since the r3 handoff

The r3 handoff (§5) told you "#3b credential migration is NOT yours." That credential track has now progressed:

- **#3b is DONE + live**: the BFF's **app-only** Dataverse paths (`DataverseServiceClientImpl`,
  `DataverseWebApiService`) now authenticate to Dataverse via **Managed Identity** (no secret). Proven on dev.
- A **new follow-on project** — `spaarke-auth-v4-dataverse-MI` — is chartered (research-first) to decide whether to
  **eliminate the BFF client secret entirely**, including for **OBO** (delegated user auth), via a Managed Identity
  Federated Identity Credential (MI-FIC) or a **certificate**. See its `notes/ASSESSMENT.md`.

## Why this matters to provisioning

You provision and store the auth credentials per environment/tenant, in **Model 1 (Spaarke-hosted)** and
**Model 2 (customer tenant)**. The auth-v4 outcome directly rewrites part of your runbook:

- **If MI-FIC wins** → **no per-customer BFF client secret** to create/store/rotate (simpler; removes a rotation
  lifecycle), but you ADD a per-tenant **Federated Identity Credential** on the app registration.
- **If certificate wins** → provision + rotate a per-tenant cert in Key Vault (the existing `CiamGraphClientFactory`
  already does exactly this — a working precedent).
- Either way, your "create `BFF-API-ClientSecret` in KV" step and the r3 handoff's **"never remove
  `BFF-API-ClientSecret`"** pre-check (§4a) would be **superseded / rewritten**.

**Model 2 is the hard case** and is a first-class input to auth-v4: MI-FIC / cert setup *inside a customer's own
tenant* is materially harder than in the Spaarke-hosted tenant. If your design surfaces Model-2 constraints on the
credential step, feed them to auth-v4 — the solution must work for both models.

## Ask (low-effort)

- **Don't hard-wire** the secret-based BFF auth into the provisioning pipeline as a fixed invariant. Model the
  "configure BFF confidential credential" step as **pluggable** (secret today; MI-FIC / cert later) so an auth-v4
  decision doesn't force provisioning rework.
- No action required now; **coordinate before finalizing** the credential-provisioning design. Ping auth-v4 with any
  Model-2 constraints.

Pointers: `projects/spaarke-auth-v4-dataverse-MI/notes/ASSESSMENT.md` (§9 broader secret inventory, §10 this coupling).
