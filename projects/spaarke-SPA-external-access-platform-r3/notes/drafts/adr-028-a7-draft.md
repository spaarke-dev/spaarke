# ADR-028 Amendment A8 — draft for owner approval (task 001)

**Status:** DRAFT, 2026-10-10. Not yet in `.claude/adr/`. Written there only after the owner approves (CLAUDE.md §6.5 path B; project directive "editing `.claude/adr/` needs owner approval").
**Sources:**
- `notes/r3-auth-path.md` §2–§6;
- `spec.md` §ADR Tensions and Owner Clarifications;
- owner decisions 2026-10-09 and 2026-10-10.

**Placement in the ADR:**
- A new section `## Amendment A8 (2026-10-xx)`, after A6.
- One-line pointer markers at the four superseded spots: line 36 (the MUST NOT parenthetical "workforce Entra (multitenant, Teams SSO/NAA)"), A1 :52/:56, A2 :71 and A3 :125.
- No other rule text changes.

---

## Amendment A8 (2026-10-xx): Workforce client, scope, default modules and workforce-contact self-registration for the module-host SPA

> **Status**: Accepted (resolution path **B — amendment**, per root CLAUDE.md §6.5). **Driver project**:
> `spaarke-SPA-external-access-platform-r3` (spec §ADR Tensions rows A2, A3, A1). **Owner-approved** 2026-10-xx.
> **Amends one clause each of A1, A2 and A3; adds one rule.** Rationale: `projects/spaarke-SPA-external-access-platform-r3/notes/r3-auth-path.md`.
> The auth system-of-record findings (§12b) are **not** in A8; they are a separate amendment owned by `spaarke-auth-system-of-record-r1`.

### Why

**The A2 client shape does not work on stamps.** A2 required the Teams host to sign users in "via Teams SSO / NAA against a **multitenant** app". Model 1 (D-12) gives each customer its own BFF app registration in Spaarke's tenant. Licensed customer staff are B2B guests there, with `systemuser` rows keyed by the guest `oid`. That leads to three problems:
- **Authority.** Only an authority pinned to Spaarke's tenant issues a token with that guest `oid`. This was proven live on 2026-10-08: `tid` = Spaarke, `acct = 1`, `idp` = home tenant. A multitenant or `/organizations` sign-in lands the guest in their home tenant, with a different `oid` and no `systemuser` match.
- **Teams SSO.** Teams SSO (`getAuthToken`) needs `webApplicationInfo` naming one resource. Its token audience would be wrong for a per-customer server.

The Office add-in already runs the working shape: one single-tenant Spaarke client, NAA then popup.

**A3 leaves workforce users with no modules.** A3 forbids inferring Tier-1 entitlement from authentication or plane. Owner decision 2026-10-09 (option b) gives every caller a default module set by **user type**, so workforce users are not left with no modules and no per-user setup.

**A1's guest rule needs a scope.** A1's "no per-external-user B2B guest" rule was written for CIAM partners. Licence-free **customer staff** ("workforce contacts") are a different population. They must be B2B guests in Spaarke's tenant to sign in on the workforce plane. A1 :52 (External ID sign-up flows) is likewise a CIAM rule.

### New MUST rules

**Workforce client (replaces A2 :71 for Model 1; covers the browser "work account" sign-in and the Teams host)**

- **MUST** authenticate workforce-plane users of the module-host SPA and the Teams host through **one single-tenant public client registration in Spaarke's tenant**, shared by every Model 1 customer.
  - It is not the BFF app registration.
  - Its authority is pinned to `https://login.microsoftonline.com/{Spaarke tenant id}`.
- **MUST**, in Teams, acquire tokens with **Nested App Authentication first, then the MSAL popup**.
- **MUST** request `api://{customer BFF app id}/user_impersonation` on **both** planes: the workforce client and the CIAM client.
  - The customer BFF app id and server URL come from Spaarke-owned data: the T240c directory, or dev configuration until it ships.
  - They never come from a link, query string or other client-supplied input.

**Default Tier-1 modules (amends A3 :125)**

- **MUST** give each caller a default Tier-1 module set derived from the **server-resolved principal kind**:

  | Caller | Default modules |
  |---|---|
  | Workforce `systemuser` or workforce contact | `legal-front-door` + `policy-library` |
  | CIAM partner contact | `assigned-work` |
  | CIAM partner contact, when `ExternalAccess:PartnerServiceRequestsEnabled` is on (default off) | `assigned-work` + `legal-front-door` |

  - `sprk_approlemodulemap` adds extras only.
  - A3's other Tier-1 rules stand: entitlement is resolved server-side, never from client input, and an unentitled module is neither shown nor routable.

**Workforce-contact self-registration (new)**

- **MUST** create workforce-contact B2B guests only in Spaarke's tenant, and only through **one shared Spaarke-tenant registration path**. That path is either a shared registration service or Entra's B2B self-service sign-up user flow, whichever R3 task 003/060 selects.
- **MUST** approve a registration automatically only when the caller's home tenant is in the customer's `CustomerTenantIds` **and** the caller is a member of that home tenant (`acct = 0`). Every other case is refused or held for approval; it is never auto-approved.

### New MUST NOT rules

- **MUST NOT** declare `webApplicationInfo` in the Teams manifest or fall back to Teams SSO (`getAuthToken`) for the BFF token.
- **MUST NOT** use a multitenant workforce client, or a `common`/`organizations` authority, for Model 1. Multitenant is revisited only for Model 2 (customer-tenant stamps), under a further amendment.
- **MUST NOT** request or expose `access_as_user` for the module-host SPA or the Teams host; `user_impersonation` replaces it.
- **MUST NOT** grant `User.Invite.All` (or any invite permission) to a customer stamp identity.

### Clarifications (A1)

- **A1 :56** ("MUST NOT require or provision a per-external-user B2B guest") applies to **CIAM partners**.
  - It does not forbid B2B guests for **customer staff** (licensed U2, licence-free workforce contacts U3), who are workforce-plane users in Spaarke's tenant.
  - A1's broker-only reasoning is unchanged for them: no OBO, and app-only SPE/Dataverse.
- **A1 :52** (External ID self-service sign-up user flows) governs **CIAM partner** self-registration only. Workforce-contact self-registration is governed by the rule above. (Spike `notes/spikes/fr-12-self-registration-decision.md`: the built-in B2B self-service sign-up flow cannot enforce the tenant-and-`acct` rule, so it recommends the custom join flow.)

### Scope and preserved invariants

**Unchanged:**
- **No OBO on any collaboration plane.** A1/A2/A3 broker-only stays textually in force. The token authenticates to the BFF only; SPE and Dataverse access stay app-only, or impersonated under A5.
- **Plane selection** from validated `iss`/`tid` only (A3), and **plane-agnostic handlers**.
- **The shared standalone-MSAL module** with a pluggable authority (A2). Its workforce authority is now the Spaarke tenant rather than multitenant.
- **A5's** `systemuser` derivation.
- **Internal Xrm surfaces**: `@spaarke/auth`, PCFs and Code Pages are unaffected.

**Model 1 only.** Model 2 keeps A2's text until a further amendment.

### Alternatives considered and rejected

- **Keep A2 (multitenant + Teams SSO).** It signs guests into their home tenant, so the `oid` mismatches and no `systemuser` matches. The SSO token audience is wrong for per-customer servers, and every customer would need admin consent.
- **A new `access_as_user` scope per stamp.** The BFF checks only that a delegated scope is present (`src/server/shared/Spaarke.Core/Auth/CallerIdentity.cs:113,247-249`), and every stamp app already exposes `user_impersonation` (H3). A second scope adds provisioning work for no gain.
- **Per-user Tier-1 setup through the role map only (comply with A3).** Every workforce user would need manual setup before they see anything. The owner rejected this on 2026-10-09.
- **Invites from each stamp BFF.** It puts a tenant-wide invite permission on every customer identity. A single shared path keeps it in one place.
- **Combining with the §12b amendment.** §12b is not finished, and R3's Wave 2 (tasks 011, 012, 040) needs these rules first. Two dated amendments do not conflict, because they touch different clauses.

> **Note (A8)**: Applied concise-only, like A1–A6. This concise ADR now carries Amendments A1–A8.
> A8 changes the workforce client, scope, Tier-1 defaults and guest-creation path. It adds no IdP or token exchange, and does not weaken the no-OBO invariant.
