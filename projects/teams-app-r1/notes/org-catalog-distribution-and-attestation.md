# Org-Catalog Distribution + Publisher Attestation — teams-app-r1

> **Task**: 072 — Org-catalog distribution + Publisher Attestation prep
> **Status**: Distribution path + attestation checklist documented. Live admin-upload/install verification is **operator-gated** (see §6) — NOT performed as part of this task per the project's DEPLOY-PAUSE directive.
> **Operationalizes**: `design.md` §8 (Enterprise posture) + `spec.md` NFR-03. This document does not re-derive that posture — it turns it into a concrete, checkable procedure + checklist for R1 go-live.
> **Package artifact consumed**: `src/client/external-spa/appPackage/` (manifest v1.29 + icons + `env.dev.json`), built to `src/client/external-spa/appPackage/build/appPackage.dev.zip` (task 070 output).

---

## 1. Distribution path (NFR-03) — org catalog under App Centric Management

Per `design.md` §8 and `spec.md` NFR-03, distribution for R1 is **exclusively** the tenant's **org app catalog** managed through **App Centric Management (ACM)** — the Teams admin center's app-governance surface that replaced the legacy app-permission-policies model (deprecated April 2025). There are two supported ways to get the package **into** the org catalog; either satisfies NFR-03. ACM group assignment + app-setup-policy pinning is how the app then reaches end users regardless of which upload path was used.

### 1.1 Path A — Direct admin upload (manual, recommended for R1 go-live and this task's verification)

Steps a tenant admin (Global Admin or Teams Service Admin) follows in the customer/test tenant:

1. Sign in to the **Teams admin center** (`https://admin.teams.microsoft.com`) with an account holding the Teams Service Administrator or Global Administrator role.
2. Navigate to **Teams apps → Manage apps**.
3. Select **Upload new app → Upload**.
4. Upload the built package zip: `src/client/external-spa/appPackage/build/appPackage.dev.zip` (dev build; a `appPackage.prod.zip` equivalent is produced the same way from `env.prod.json` for the customer-tenant go-live package — task 071's CI workflow automates this build step but is not required for a manual upload).
5. Teams admin center validates the manifest against the declared schema version (`1.29`) and reports validation errors, if any, before the app is added to **Manage apps**.
6. Once uploaded, the app's default state is **Blocked**. The admin must explicitly change its status to **Allowed** in **Manage apps** before it can be assigned to anyone.
7. The app now exists in the tenant's **org app catalog** (distinct from the public Teams Store) and is eligible for ACM group assignment (§1.3).

### 1.2 Path B — Submission via API + admin approval (for scaled / self-service publisher submission)

For an organization that wants app owners/developers to submit candidate packages without direct catalog-upload rights, the equivalent flow is:

1. The app package (same zip artifact as Path A) is submitted through the **Teams admin submission workflow** (`Manage apps → Submit an app` in the Teams admin center, or the equivalent Graph/PowerShell submission surface where enabled) rather than being uploaded directly.
2. The submission enters a **pending admin approval** queue in **Manage apps**.
3. A Teams Service Administrator reviews the submission (manifest contents, permissions requested, `validDomains`, `webApplicationInfo` audience) and **approves** or **rejects** it.
4. On approval, the app is added to the org app catalog in the same **Blocked** default state as Path A and requires the same explicit **Allow** step before assignment.

Path A and Path B converge at the same place: an approved, **Allowed** app sitting in the org app catalog, not yet visible to any user until assigned.

### 1.3 ACM group assignment

App Centric Management assigns catalog apps to users via **Microsoft Entra security groups**, not per-user app-permission policies (the pre-April-2025 model):

1. In Teams admin center, open the app's detail page under **Manage apps**.
2. Select **Manage → Assign**.
3. Choose one or more Entra security groups scoped to the intended install population (e.g., a "Spaarke Teams App — Pilot Users" group for a test tenant, or the customer's designated legal-workspace user group for a production tenant).
4. Set the assignment as **Preinstalled and can't be uninstalled**, **Preinstalled but can be uninstalled**, or **Available for user install**, per the customer's rollout preference. R1 default recommendation: **Available for user install** for the pilot phase, escalating to preinstalled once the customer confirms.
5. ACM propagates the assignment to group members on the standard Teams app-sync cadence (typically within a few hours; can be forced via **Sync now** in some tenant configurations).

### 1.4 App-setup-policy pinning

Independently of assignment (which controls *who can install*), an **App setup policy** controls *pinning/visibility* in the Teams client shell:

1. In Teams admin center, go to **Teams apps → Setup policies**.
2. Edit the relevant policy (or create a customer-specific policy, e.g., "Spaarke Legal Workspace Users") and add the Spaarke app to the **Pinned apps** list.
3. Assign the setup policy to the same Entra group(s) used for ACM assignment (§1.3) so pinning and installability stay consistent for the same population.
4. Pinning is cosmetic (places the app in the left rail without requiring the user to search/install manually) — it is not a substitute for the ACM assignment step; an app can be assigned without being pinned, but should not be pinned without being assigned.

### 1.5 Explicit exclusions — NOT used for V1

The following distribution mechanisms exist in Teams generally but are **explicitly NOT used** for this app in V1, per NFR-03 and `design.md` §8:

- **Sideloading** (uploading a package directly into a single Teams client via "Upload a custom app" / developer sideloading) — this is a developer/test-inner-loop mechanism only (used ad hoc during task 070/071 development smoke-testing), never a production or customer-facing distribution path.
- **The public Microsoft Teams Store** — this app is not submitted to, listed on, or installable from the public Teams Store (AppSource) for V1. There is no Store submission step anywhere in this document's distribution path (§1.1–§1.4); the only catalog involved is the tenant's private **org app catalog**.

No step in §1.1–§1.4 references the public Store or a sideload-and-ship pattern — the recorded distribution path is Org Catalog (admin-upload or submission-API) → ACM group assignment → app-setup-policy pinning, full stop.

---

## 2. Publisher Attestation checklist

Per `design.md` §8, **Publisher Attestation is the minimum trust bar** documented for R1 — sufficient for a legal customer's security/vendor-risk review. The checklist below covers the items such a review typically expects. This is the artifact an operator/account team hands to a customer's security reviewer alongside the app package.

| # | Item | Status / Reference |
|---|---|---|
| 1 | **Publisher identity verified** — the app's `developer.name` and `developer.websiteUrl` resolve to a real, verifiable organization | `manifest.json`: `developer.name = "Spaarke"`, `developer.websiteUrl = "https://spaarke.com"` |
| 2 | **Privacy policy published and linked** — a publicly reachable privacy policy describing what data the app collects/processes | `manifest.json`: `developer.privacyUrl = "https://spaarke.com/privacy"` — referenced by value here; this document does not restate or fork the policy text (see §4) |
| 3 | **Terms of Use published and linked** — publicly reachable ToU governing use of the app | `manifest.json`: `developer.termsOfUseUrl = "https://spaarke.com/terms"` — referenced by value here; not restated (see §4) |
| 4 | **Data handling summary** — what the app accesses, where it flows, and what it does NOT do | See §2.1 below |
| 5 | **Auth model summary** — how users authenticate and what trust boundary the token crosses | See §2.2 below (references ADR-028) |
| 6 | **Least-privilege permission scopes** — the manifest requests only what the app needs | `manifest.json` `permissions: ["identity"]` only; `webApplicationInfo` scopes are delegated, least-privilege, matching the reused multitenant Entra app (task 061) |
| 7 | **Support / contact information** — a channel for the customer's security team to reach the publisher with findings or questions | See §2.3 below |
| 8 | **Admin-consent-only distribution** — the app cannot reach a tenant without an explicit tenant-admin action | Satisfied structurally by §1 (org catalog + ACM + admin consent on first sign-in; no self-service Store install) |
| 9 | **Data residency / storage disclosure** — where app data and files are stored | See §2.1 below |

### 2.1 Data-handling summary (for the checklist)

- The Teams tab is a **thin client** over the existing Spaarke collaboration core (`external-spa`, extended in place per D10) — it does not introduce a new data store. Documents remain in **SharePoint Embedded (SPE)** containers; membership/authorization records remain in **Dataverse**.
- Per NFR-02 (**broker-only invariant**), the user's Teams-issued token authenticates to the Spaarke BFF **only**; it is **never exchanged downstream**. All SPE and Dataverse access performed on the user's behalf is **app-only** (managed identity / service principal), not OBO. This means the Teams user's identity token itself never reaches SharePoint Embedded or Dataverse — only the BFF sees it, and only to authenticate the caller and resolve the caller's authorized record set server-side.
- Access is derived from the customer's own Dataverse membership records — no data is exposed to the Teams app beyond what the authenticated user's existing Spaarke role/membership already grants (role-allowlist safety, NFR-05).
- No bot, background service, or M365 Agents SDK component is part of this app (per ADR-029 / NFR-01) — it is a Teams **static tab** only; there is no persistent bot channel or message-extension surface collecting data outside the interactive tab session.

### 2.2 Auth model summary (references ADR-028)

- Teams-host users authenticate with their **workforce Microsoft Entra identity** via **Teams SSO / NAA** against a **multitenant** app registration, with **per-customer admin consent** required before any user in that tenant can obtain a token — this is Amendment **A2** to `.claude/adr/ADR-028-spaarke-auth-architecture.md` (2026-08-03), which extends the collaboration auth line (previously CIAM-only for the external SPA, Amendment A1) to a second host.
- CIAM (Entra External ID) is explicitly **not used** inside the Teams host — Teams is a workforce-identity surface; the auth model deliberately avoids a second in-tab login (ADR-028 A2 "MUST NOT attempt CIAM / External-ID sign-in inside the Teams host").
- The collaboration surface is **broker-only** in both hosts (A1 invariant, preserved unweakened by A2): the user token authenticates to the BFF only; document content and Dataverse access stream app-only. No Dataverse seat or OBO exchange is required for read/download.
- The authenticated workforce identity resolves server-side to a principal (`systemuser` → ADR-034 membership, or `contact` → contact-anchored membership) and authorization is enforced against the accessible-record-set check — not by the token's mere validity.
- Full detail: `.claude/adr/ADR-028-spaarke-auth-architecture.md` Amendment A2 section; do not restate the auth architecture beyond this summary in customer-facing security-review material — point the reviewer at the ADR (or an approved external-facing derivative of it) for implementation-level detail.

### 2.3 Support / contact information (for the checklist)

- Primary contact channel: `developer.websiteUrl` (`https://spaarke.com`) — the publisher's public site, from which support/contact routing is published.
- Security-specific findings/questions should be routed through the account team's standard support channel for the customer; this document does not mint a new support email distinct from the publisher's existing published channels (avoids drift between this note and the actual current support contact).

---

## 3. M365 Certified — separate, later, parallel commercial workstream (NOT an R1 gate)

Per `design.md` §8 ("Trust: **Publisher Attestation** (minimum for a legal customer's security review) → **M365 Certified** (ideal) as a **parallel commercial workstream**"):

- **Publisher Attestation (§2 above) is the R1 bar.** It is what this task documents and what ships with R1 go-live.
- **Microsoft 365 App Certification** ("M365 Certified") is explicitly treated as an **ideal, later, parallel commercial track** — it involves a formal third-party security assessment (data handling, application security, responsible AI where applicable) submitted through Microsoft's certification program, typically pursued once the app has production customers and a stable, audited surface.
- **M365 Certified is NOT an R1 gating requirement.** No task in this project blocks on it, no acceptance criterion in this project or task 072 requires it, and the go/no-go decision for R1 distribution (§1) does not depend on certification status.
- Pursuing M365 Certified is out of scope for `teams-app-r1` — it is a future, separate commercial/compliance initiative that can run in parallel with or after R1 ships, tracked (if pursued) as its own project rather than a task under this one.

---

## 4. Privacy policy + Terms of Use — referenced by value, not restated

Per the task constraint ("Reference the manifest's `developer.privacyUrl`/`developer.termsOfUseUrl` ... by value — do not restate/fork policy text"), the actual URLs are read directly from the shipped manifest and quoted here as **references only**:

- Privacy policy: `https://spaarke.com/privacy` — from `src/client/external-spa/appPackage/manifest.json` → `developer.privacyUrl`.
- Terms of Use: `https://spaarke.com/terms` — from `src/client/external-spa/appPackage/manifest.json` → `developer.termsOfUseUrl`.

These two values are the **single source of truth**. If they ever change, update the manifest — this document (and any customer-facing attestation packet built from it) should be regenerated/re-checked against the manifest rather than hand-edited, so the two never drift apart. Verified present and non-empty as of this task (2026-08-04): both fields are populated in the current manifest (see excerpt above; no placeholder/empty string).

---

## 5. Package artifact reference

- Manifest + appPackage source: `src/client/external-spa/appPackage/manifest.json`, `env.dev.json`, `color.png`, `outline.png` (task 070).
- Built distributable: `src/client/external-spa/appPackage/build/appPackage.dev.zip` (dev-tenant build; task 071's CI workflow produces the equivalent prod-tenant build from `env.prod.json` when a customer-tenant `id`/domains are finalized — not required for this task's dev-tenant verification path).
- Manifest identity: `id = ed3f4d89-eb9b-49a6-bf6e-8b28b86ceb86`, `webApplicationInfo.id = 1e40baad-e065-4aea-a8d4-4b7ab273458c` (reused multitenant Entra app, task 061) — unrelated to this task's scope beyond confirming which package is being distributed; not re-verified here (owned by task 070's acceptance criteria).

---

## 6. Operator-gated live verification (POML steps 5–6) — NOT performed

**🛑 This task did NOT perform a live Teams-admin action.** The project is under an explicit **DEPLOY PAUSE** (operator directive): no live admin upload, ACM assignment, or install verification was executed as part of this task. POML steps 5 ("perform or document an admin upload ... and confirm the app installs for a test user via admin consent") and 6 ("record the verification result") are satisfied only to the extent of **documenting the exact steps** (§1.1–§1.4 above); the actual execution is deferred to the operator.

### What an operator runs, when ready, to close this out

1. Confirm the target test/dev Teams tenant and an account with Teams Service Administrator (or Global Administrator) rights.
2. Follow §1.1 (Path A — direct admin upload) using `src/client/external-spa/appPackage/build/appPackage.dev.zip`.
3. Set the app to **Allowed** in **Manage apps**.
4. Follow §1.3 (ACM group assignment) against a designated pilot-user Entra security group in that tenant.
5. Optionally follow §1.4 (app-setup-policy pinning) for the same group.
6. Sign in to Teams as a member of the pilot group and confirm:
   a. The app appears (search or pinned rail, per the assignment mode chosen).
   b. Installing it triggers the expected **admin-consent / SSO** prompt (or silent SSO if consent was already granted at the tenant level) — NOT sideload semantics.
   c. The static tab loads the Teams-adapter route and completes Teams SSO/NAA sign-in per ADR-028 A2.
7. Record the outcome (tenant name/ID, admin account used, group used, pass/fail, timestamp) as an addendum to this file or in the project's `notes/defer-issues.md` if any deviation is found.

### Verification result

- **Tenant**: not run (operator-gated).
- **Admin action**: not run (operator-gated).
- **Outcome**: not run (operator-gated) — no result to fabricate. This section is intentionally left as a template for the operator to fill in after executing §6 steps 1–7.

---

## 7. Acceptance criteria — status

| # | Criterion | Status |
|---|---|---|
| 1 | App installs from the org catalog in a test/customer tenant via admin consent, verified and recorded with the specific tenant/admin action used | **Operator-gated — NOT met yet.** Steps documented in §1 + §6; execution deliberately deferred per the project's DEPLOY PAUSE directive. No result fabricated (§6 "Verification result" is a template). |
| 2 | Publisher-Attestation checklist documented in this file, covering the minimum items for a legal customer's security review, with M365 Certified explicitly noted as a separate parallel commercial workstream (not gating R1) | **Met.** §2 (checklist) + §3 (M365 Certified framing). |
| 3 | Documented posture references the manifest's published privacy policy and ToU URLs rather than restating them separately | **Met.** §4 quotes the two URL values read from `manifest.json` and states the manifest is the single source of truth; no forked policy text is included. |
| 4 (negative) | Documentation explicitly states the app is NOT distributed via sideloading and NOT via the public Teams Store for V1 | **Met.** §1.5 states this explicitly; no step in §1.1–§1.4 references sideloading or the public Store. |

**Net**: 3 of 4 acceptance criteria are fully met by this document. Criterion 1 (live install verification) is explicitly **operator-gated** per the operator's DEPLOY PAUSE directive for this project — the distribution + verification *procedure* is fully documented and ready to execute, but the live action itself was intentionally not performed in this task.
