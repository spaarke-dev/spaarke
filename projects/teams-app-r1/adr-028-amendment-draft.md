# ADR-028 Amendment A2 (DRAFT) — Workforce Auth for the Teams Collaboration Host

> **Status**: 🟡 DRAFT — for owner review. Not yet applied to canonical `.claude/adr/ADR-028-spaarke-auth-architecture.md`. (Resolution path **B — ADR amendment**, per root CLAUDE.md §6.5.)
> **Date**: 2026-08-03
> **Amends**: [ADR-028 (concise)](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) + [ADR-028 (full)](../../docs/adr/ADR-028-spaarke-auth-architecture.md); **builds on Amendment A1** (external SPA / Entra External ID).
> **Driver project**: `teams-app-r1`
> **Requires**: owner sign-off before merge into the canonical ADR.

---

## Why this amendment

ADR-028 mandates that internal auth flows through `@spaarke/auth` and forbids instantiating `PublicClientApplication` directly, with **one existing carve-out — Amendment A1** — that exempts the external SPA (standalone MSAL v5, Entra External ID / CIAM authority, `sessionStorage`, broker-only).

`teams-app-r1` extends the same **collaboration product line** to a second host — a Microsoft **Teams tab / personal app** — with two properties ADR-028 (even with A1) does not yet sanction:

1. **A workforce-Entra-authenticated standalone (non-Xrm) app.** A1 covers a *CIAM* standalone SPA; the Teams host authenticates the user's **workforce** identity (Teams SSO / NAA, multitenant) inside a **non-Xrm** standalone app. `@spaarke/auth` cannot serve it: it is **Xrm-context-bound** (frame-walks Dataverse for config) and **MSAL v3** (the collaboration line is MSAL v5). So the Teams host needs the same *standalone-MSAL* treatment A1 granted the SPA, but on the **workforce** authority.
2. **One shared standalone-MSAL module with a pluggable authority.** The external SPA (CIAM) and the Teams host (workforce) are the same collaboration core over two hosts; they share **one** standalone-MSAL auth module whose **authority is pluggable** (CIAM `*.ciamlogin.com` vs workforce `login.microsoftonline.com`, multitenant).

### Enabling finding

The collaboration surface is **broker-only** in both hosts (per A1): the user's token authenticates to the BFF and is **never** exchanged downstream; all SPE/Dataverse access is app-only. This holds for the workforce plane too. The workforce identity is resolved to a **principal** — a `systemuser` (→ ADR-034 membership) or, for a non-systemuser, a `contact` (→ contact-anchored membership) — and authorization is enforced server-side as an accessible-record-set check. The BFF **already** runs a workforce default JwtBearer scheme; A2 does not add a new IdP, it sanctions the workforce **client** plane for the collaboration line.

---

## Proposed changes to ADR-028

### New MUST rules (collaboration Teams host)

- **MUST** authenticate Teams-host collaboration users with their **workforce Microsoft Entra identity** via Teams SSO / NAA against a **multitenant** app registration (per-customer admin consent). CIAM is not used inside Teams.
- **MUST** serve both the external SPA (CIAM) and the Teams host (workforce) from **one shared standalone-MSAL module** whose **authority is config-driven/pluggable**; the module is exempt from the `@spaarke/auth`-only rule exactly as the A1 SPA is.
- **MUST** keep the collaboration surface **broker-only** in both hosts (A1 invariant): the user token authenticates to the BFF only and **MUST NOT** be exchanged for a downstream Graph/SPE/Dataverse token (no OBO on the collaboration path). Document content streams app-only.
- **MUST** resolve the workforce-authenticated caller to a **principal** (systemuser → membership; else contact → contact-anchored membership) and enforce authorization server-side via the accessible-record-set check. No Dataverse seat/OBO is required for read/download.

### New MUST NOT rules

- **MUST NOT** attempt CIAM/External-ID sign-in inside the Teams host (Teams is a workforce-identity host; a second in-tab login is an anti-pattern).
- **MUST NOT** route the collaboration hosts through `@spaarke/auth` while it remains Xrm-bound + MSAL v3; the shared standalone-MSAL module is the sanctioned surface until a future consolidation (MSAL v3→v5 across the internal estate) is undertaken under a superseding amendment.

### Edits to existing ADR-028 text

- **A1 exemption** (external-SPA `PublicClientApplication` / `sessionStorage` / External-ID authority) — **generalized** from "external SPA" to "the **collaboration hosts** (external SPA + Teams tab)", with a **pluggable authority** (CIAM *or* workforce-multitenant). All A1 exemptions (direct `PublicClientApplication`, per-tab isolation, Bearer-literal allowlist) are **preserved**.
- Note that `@spaarke/auth` remains **canonical for internal Xrm-hosted surfaces** (PCF / Code Pages); the collaboration line is the explicit, bounded exception — not a replacement.

---

## Alternatives considered (and rejected)

- **CIAM inside the Teams tab** (reuse A1 unchanged) — rejected as the default: Teams runs as a workforce identity; a separate in-tab CIAM login is double sign-in, is blocked by the desktop client's popup handling, and splits one person across two identities. (Retained only as a theoretical fallback, not used.)
- **Fold the collaboration hosts onto `@spaarke/auth` now** (Target 2) — rejected for R1: it forces an MSAL v3→v5 migration across the entire internal consumer estate + de-Xrm-ing config resolution. Large blast radius; deferred to a separate consolidation under a future amendment.
- **Path C (comply within existing ADR-028 / A1)** — not viable: A1 sanctions a *CIAM* standalone SPA only; it neither covers a workforce-authenticated standalone app nor the shared pluggable-authority module. Compliance would require *not* building the Teams host, failing the project's requirements.

---

## Impact if accepted (path B)

- Scope of change: generalize the A1 exemption to a **pluggable-authority** collaboration-auth module + workforce-plane MUST rules, applied to the **collaboration hosts only**. Internal surfaces (`@spaarke/auth`, PCFs, Code Pages) are **unaffected**.
- Merge ordering: merges **before or alongside** the dependent Teams-host auth code.
- Apply to both the concise `.claude/adr/ADR-028` and full `docs/adr/ADR-028` on approval; note the ADR-034 contact-anchored-entry extension where membership is discussed.
