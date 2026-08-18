# Spaarke Auth v4 — Eliminate the BFF client secret via Managed Identity (OBO → MI Federated Credentials)

> **Status**: SEED / RESEARCH-FIRST (folder + assessment only; NOT scoped, NOT execution-ready) · **Epic**: Auth / Code Quality (#427)
> **Origin**: surfaced during `code-quality-and-assurance-r3` task 011 / #3b (app-only Dataverse → MI, done + live)
> **Type**: auth architecture (ADR-028 amendment candidate) · **Surface**: BFF OBO path + `Spaarke.Dataverse` · **Risk**: HIGH (OBO = all delegated user auth)
>
> ⚠️ **Folder name**: created as `spaarke-auth-v4-dataverse-MI` (corrected the `spaakre` typo to match repo convention). Rename if you intended otherwise.

## One-liner

`#3b` migrated the **app-only** Dataverse paths to Managed Identity (live). The **client secret still can't be
removed** because it is the **same `BFF-API-ClientSecret`** used by **OBO** (delegated user auth) across Graph and
Dataverse. This project researches and (if warranted) executes eliminating even the OBO secret via **Managed
Identity as a Federated Identity Credential (MI-FIC)** — or a certificate — reaching a true **zero-secret** BFF.

## Read first

**[`notes/ASSESSMENT.md`](notes/ASSESSMENT.md)** — the full investigation: the shared-secret finding (verified live
on dev), the app-only-vs-OBO distinction, why OBO *can* be secret-free (MI-FIC), an honest analysis of **why prior
auth audits didn't raise this** (they correctly retained the secret for OBO; the *new* idea is eliminating it), and
the open questions the research phase must answer.

## Why this is NOT just "finish #3b"

- #3b was **app-only** Dataverse (BFF acting as itself) → MI is simple, and it's done + proven live.
- This is **OBO** (BFF acting as the user) → needs a confidential-client credential; today that's the secret. It
  can be MI-FIC or a certificate instead, but that is a **different, larger, higher-risk** change (OBO breakage =
  all delegated auth down), plus per-env app-registration federated-credential setup.

## Explicitly research-first

Do **not** pre-decide the solution. The project must first confirm:
1. Is **zero-secret** actually a requirement (vs. an OAuth-standard, rotated secret / certificate)?
2. Does **MI-FIC** work for our OBO exchange (Graph + Dataverse) on the App Service MI source, in our tenant?
3. **MI-FIC vs certificate** — which is the right secret-free credential (rotation, risk, portability)?

Only after that → ADR-028 amendment + staged, slot-based OBO migration per env.

## Prerequisites / sequencing

- **After** #3b (task 011) — done — and **coordinated with** `dataverse-access-unification-r1` (RED-4 C) since both
  touch `GraphClientFactory` / `Spaarke.Dataverse`.
- Operator + Azure AD admin needed for the app-registration Federated Identity Credential (per env).
- Highest-blast-radius auth surface — staged rollout + explicit rollback mandatory; no in-session flips.

## Graduation criteria (provisional — finalize after research)

- [ ] Research spike answers the §6 open questions; ADR-028 amendment decision recorded.
- [ ] If proceeding: every `IConfidentialClientApplication` uses the chosen secret-free credential; OBO verified
      (SPE/chat/Office/Dataverse row-level access) per env.
- [ ] `BFF-API-ClientSecret` removed from Key Vault (or an explicit decision to retain, documented).
- [ ] `DataverseOptions.ClientSecret` `[Required]` relaxed/removed consistently with the outcome.
