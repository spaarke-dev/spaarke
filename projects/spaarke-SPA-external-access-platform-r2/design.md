# Design — Spaarke External Access Platform (R2): Multi-App Foundation + Legal Front Door

> **Status**: DRAFT scoping brief (input to `/design-to-spec`)
> **Author**: Owner + Claude (post-R1 scoping, 2026-07-21)
> **Predecessor**: `projects/spaarke-SPA-external-access-platform-r1/` — SHIPPED. Migrated the external Secure Project Workspace (Outside Counsel) from Power Pages + B2B guests to Azure Static Web Apps (SWA) + Entra External ID (CIAM), broker-only (ADR-028 Amendment A1). Live + owner-verified.
> **Grounding**: `notes/external-access-capability-synopsis.md` (code-based synopsis of what R1 shipped, cited to file:line).

---

## 1. What R2 actually is

R1 proved the pattern with **one** app (Outside Counsel). R2 turns that one-off into a **reusable platform for non-Power-App-licensed users**, and ships the **second** app on it (Legal Front Door).

Spaarke needs external/lightweight-SPA apps for three audiences:

| App | Audience | Identity plane | Core data domain | Status |
|-----|----------|----------------|------------------|--------|
| **Outside Counsel** (R1, shipped) | Outside law firms | **CIAM** (truly external) | `sprk_project` / `sprk_matter` / documents | Live |
| **Legal Front Door** (R2) | **Internal business users, unlicensed for Power Apps** | **Workforce Entra SSO** (employees) | `sprk_servicerequest` intake + NDA/trademark/invention/policy workflows | **This project** |
| **E-billing Portal** (R3) | Outside service providers / vendors | **CIAM** (truly external) | `sprk_invoice` / `sprk_billingevent` / budget submit + status | Next |

**Owner decisions (2026-07-21):**
- **Program shape**: R2 = platform **Foundation + Legal Front Door**; **R3 = E-billing**.
- **Legal Front Door identity**: **Workforce Entra SSO** — business users are employees with M365 accounts; no CIAM account, no invite/provision. They use a lightweight SPA only because they are unlicensed for Power Apps.
- **Volume**: ~10s of external users/month (not 100s) — provisioning automation is useful but not the driving constraint; per-app scoping and correct authz are.

**Consequence — the foundation is dual-identity-plane from day one**: CIAM (Outside Counsel, future E-billing) **and** Workforce Entra SSO (Legal Front Door). Choosing Legal Front Door as the R2 second app is deliberately the harder validation: it exercises a *different identity plane* AND a *different authorization model* than R1, so the generalization is real, not speculative (§11).

---

## 2. What R1 shipped (the baseline — see synopsis for citations)

- **Hosting**: SWA (`swa-spaarke-external-spa-dev`), BrowserRouter + navigationFallback + security headers; `deploy-external-spa.yml`.
- **Identity (CIAM)**: second `"Ciam"` JwtBearer scheme pinned to `/api/v1/external`; Contact-by-`oid` (`Contact.sprk_externalobjectid`); broker-only (no OBO, app-only downstream, no B2B guest).
- **Authorization model (Outside Counsel)**: `(Contact → Project)` participation grants (`sprk_externalrecordaccess`, 3 access levels → effective rights); server-enforced; Redis cache invalidation on grant/revoke.
- **Onboarding**: `invite` / `invite-and-grant` / `grant` / `revoke` / `provision-project` — **API-only, no admin UI**.
- **Key limitation for R2**: access is **app-agnostic and Project-shaped**. No app/portal concept; the grant model is hardwired to `sprk_projectid`; one CIAM identity plane; no admin UI.

---

## 3. Foundation work (the reusable platform) — R2 core

These generalize R1's plumbing so it can host N audience-apps across 2 identity planes. Each names a concrete gap (§11).

### F1 — App/portal registry + per-app scoping  ·  gap G1
- **Gap**: nothing scopes a Contact/user to *which app* they may use; a token for one app can hit another's surface.
- **Deliver**: a first-class **application/portal concept** (e.g. `sprk_externalapp` or a config registry) declaring, per app: its identity plane (CIAM vs workforce), its external endpoint group, its allowed origin(s), its authz model. A user's access is scoped to specific app(s).
- **Cost-of-doing-nothing**: three apps share one undifferentiated `/api/v1/external` surface; a vendor could reach the counsel workspace endpoints.

### F2 — Dual identity-plane auth in the BFF  ·  gap G4
- **Gap**: R1 has one CIAM scheme. Legal Front Door needs the **workforce** scheme; the two must coexist and route per-app.
- **Deliver**: a per-app-group auth policy that selects the right scheme (CIAM for external apps, workforce default for Legal Front Door), reusing R1's additive-scheme pattern (`AuthorizationModule`, `AuthPolicies.CiamExternal`) — add a `WorkforceExternalApp`-style policy/group. No OBO on any external app path; internal SSO users are already workforce-validated.
- **Cost-of-doing-nothing**: Legal Front Door can't authenticate its (employee) users.

### F3 — Generalized access/authorization model  ·  gap G2
- **Gap**: `sprk_externalrecordaccess` is Project-shaped; `GetParticipations` returns `(Project, level)`. R2 needs (a) **self-service submitter** authz for Legal Front Door ("see my own service requests") and later (b) invoice/vendor scope for E-billing.
- **Deliver**: generalize the caller-context/authz so an app can plug in its authz model:
  - **Granted-participation** (Outside Counsel, E-billing) — existing/extended.
  - **Self-service submitter** (Legal Front Door) — a user sees/creates their own `sprk_servicerequest` records (scoped by submitter identity), not grants on others' records.
- **Cost-of-doing-nothing**: Legal Front Door would misuse the Project-grant model, which doesn't fit intake.

### F4 — Reusable SPA scaffold + per-app deploy  ·  builds on R1
- **Deliver**: extract the R1 SPA's reusable shell (SWA config, BrowserRouter/deep-link-through-login, MSAL/session or workforce-SSO auth guard, Fluent v9 theme, BFF client) into a scaffold a new app instantiates; per-app SWA + deploy workflow (config-only per the synopsis). Clean up the dead Power Pages proxy/config carried in R1 (`vite.config.ts`, `README.md`, `powerpages.config.json`).
- **Cost-of-doing-nothing**: each new app hand-copies R1 and re-introduces its stale Power-Pages cruft.

### F5 — Core-user admin UI  ·  gap G5 / DI-029-01
- **Gap**: onboarding/grant/revoke/provision are API-only.
- **Deliver**: a Dataverse command-bar / model-driven admin surface (Fluent v9, dark-mode per ADR-021) for the core-user actions every app needs — for Outside Counsel: invite-and-grant / revoke / provision-project on the Matter/Project form; generalized so future apps reuse it.
- **Cost-of-doing-nothing**: no app is operable by its intended core-user persona without curl.

### F6 — R1 reliability + verification hardening  ·  DI-025-01 / DI-030-01 / SSPR
- Provisioner self-healing on the CIAM `POST /users` 409 window (recover existing oid); live-E2E for wrong-issuer→401 and oid-bound-not-email-hijacked; verify the SSPR first-run.

---

## 4. Legal Front Door app (the R2 second app) — largely greenfield

The `sprk_servicerequest` entity exists but is a **stub** (`sprk_name` + statecode + regarding lookups only — no submitter, request-type, or workflow-status fields). Legal Front Door needs real intake schema + workflows.

### L1 — Intake data model (greenfield schema)
Extend `sprk_servicerequest` (or add child entities) with: **submitter** (workforce user), **request type** (NDA review/approval/signature · approval-to-publish · trademark search/filing · invention disclosure · policy & procedure), **status workflow**, assignment/routing to legal, and per-type fields. Design-time work.

### L2 — Business-user intake SPA (workforce SSO)
A lightweight SWA app where an employee signs in via workforce SSO, submits a request (typed form), uploads documents (app-only SPE, broker pattern), and tracks status of **their own** requests. Reuses F4 scaffold + F2 workforce plane + F3 submitter authz.

### L3 — Legal-side processing surface
Where legal reviews/approves/routes submitted requests. Likely the internal model-driven app / existing surfaces — R2 defines the intake→processing handoff; deep workflow automation (signature, filing) may phase to later.

> **Scope question for design-to-spec**: which request *types* are in R2's first cut (recommend: generic intake + NDA + one more) vs. phased later. NDA review/approval/signature is the richest workflow; the others may start as typed intake + manual routing.

---

## 5. Cross-cutting constraints (carry forward — binding)

- **ADR-028 (+A1) broker-only** for CIAM apps: external token authenticates ONLY to the BFF, never exchanged downstream; app-only SPE/Dataverse. Workforce-SSO apps (Legal Front Door) use the standard workforce path but **still no Power-Apps license dependency** and no elevation.
- **§10 BFF Hygiene**: Placement Justification for every BFF addition; `dotnet publish` size (≤60 MB, baseline ~49.63 MB); no new HIGH CVE; tests in `tests/unit/Sprk.Bff.Api.Tests/`; per-app endpoint groups via `Map{App}Endpoints` extensions.
- **§11 Component Justification**: the foundation is justified by ≥2 concrete consumers (counsel + front door); reuse `ExternalCallerAuthorizationFilter`/participation, `SpeFileStore`, `RegistrationEmailService`, the R1 SPA shell — extend, don't fork.
- **ADR-038 testing**; **ADR-021/022 Fluent v9 + React 18** for SPAs; **no plaintext secrets**; **preserve external-SPA sessionStorage** per-tab isolation for CIAM apps.

---

## 6. Out of scope (R2)

- **E-billing Portal** → R3 (data domain exists — `sprk_invoice` — external vendor surface is the R3 build).
- Deep Legal Front Door workflow automation beyond the R2 first-cut request types (e-signature integration, automated trademark filing) — phase as decided in design-to-spec.
- Self-service CIAM sign-up / public registration router — still deferred; not required by these three apps (all onboarding is admin-initiated or workforce-SSO).
- The E-3 direct-Office boundary (ADR-028 A1) — permanently out.

---

## 7. Draft success criteria (design-to-spec formalizes as FRs/NFRs)

1. A single BFF hosts **multiple external apps**, each scoped to its identity plane + authz model + data surface; a token for app A cannot read app B's surface.
2. Legal Front Door: an **employee signs in via workforce SSO** (no CIAM, no license), submits a typed service request with document upload, and tracks **their own** requests' status.
3. Outside Counsel continues to work unchanged on the generalized foundation.
4. A core user performs invite/grant/revoke/provision **from a UI** (no curl).
5. CIAM provisioner self-heals the create-ok/persist-fail window; live-E2E confirms wrong-issuer→401 + no email-hijack; SSPR first-run verified.
6. Standing up a **new app** is documented + largely config/scaffold (new SWA + app-reg/registry entry + endpoint group), not a fork.

---

## 8. Sequencing note (R2 is large — phase within it)

R2 spans a dual-plane foundation + a substantially-greenfield Legal Front Door + admin UI + hardening. Recommend design-to-spec phase it, e.g.: **P1** foundation (F1–F4) → **P2** Legal Front Door intake MVP (L1–L2, generic + NDA) → **P3** admin UI (F5) + hardening (F6) → **P4** Legal Front Door workflow depth (L3 + more request types). Owner confirms phase cut lines during design-to-spec.

---

## 9. References

- R1 project + code synopsis: `projects/spaarke-SPA-external-access-platform-r1/`, `projects/spaarke-SPA-external-access-platform-r2/notes/external-access-capability-synopsis.md`
- Architecture: `docs/architecture/external-access-spa-architecture.md`; ADR-028 (+A1) `.claude/adr/ADR-028-spaarke-auth-architecture.md`
- Data models: `docs/data-model/sprk_servicerequest.md` (stub — needs intake schema), `sprk_invoice.md` (R3), `sprk_externalrecordaccess` (`src/solutions/SpaarkeCore/entities/sprk_externalrecordaccess/`)
- North-star for a future public router: `projects/spaarke-self-service-registration-app`
