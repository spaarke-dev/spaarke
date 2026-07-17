# Messaging Access Model — DECISION (2026-07-16)

> **Status**: DECIDED (owner, 2026-07-16). Supersedes the hand-computed-union approach in task 042. Basis: researcher findings (`a3470467b6e2fac1a`, memoized) + owner directive design §5 ("leverage the Dataverse security model; do not rebuild core access control").

## The rule (confirmed, MS Learn)
Dataverse effective record access = **additive UNION** of ownership + security-role (by privilege depth User/BU/BU+children/Org) + teams (owner/access/Entra-group) + sharing (POA) + hierarchy. **Sharing can only ADD, never restrict.** There is no OOB "deny." "Private / named individuals" is therefore a **base-ownership/scoping** decision, not a sharing one.

## DECISION: read enforcement = IMPERSONATION (not a hand-computed filter)
An app-only BFF hand-computing "who can see this" is **incorrect** (misses role depth / BU scope / hierarchy) and rebuilds what the platform already does. So:

- **Read path (050 thread-read + unread) → impersonation.** The BFF issues the Dataverse query with **`MSCRMCallerID: <user Entra oid>`** (Web API impersonation). Dataverse returns exactly the rows that user may see, honoring ALL access sources, in one query. Correct + native + performant for ~5s polling.
- **042 is reworked**: record-level access is now Dataverse's job (via impersonation). `CommunicationAccessFilter` shrinks to the two Spaarke business rules impersonation does NOT cover, applied **on top** of impersonated rows:
  - **internal-only** (`sprk_isinternalonly`) — hide from external users (D-05 user attribute).
  - **privilege** (`sprk_privilegeclassification`) — metadata only, never gates (owner decision 2026-07-16); drives review/labeling.
  The hand-computed union + `IThreadPrivateGrantProvider` deny-all path is retired for reads.
- **Discrete gates** ("can user X open/post to thread Y") → **`RetrievePrincipalAccess`** (effective rights for one principal+record, app-only).
- **Granting** → OOB **"Manage access"** (POA) on the `sprk_communicationthread` record (User/Team owned → shareable). No custom grant table, no custom grant endpoint.
- **ACS membership reconcile (041)** stays an **approximation** for R1 — acceptable because R1 has **no live channel** (polling), so ACS membership is not the leak-prevention path; **impersonated reads are.**

## Access wiring (open / internal-only / private / mixed)
- **Open (record-anchored)** — thread owned by / shared to the matter team (owner team or matter default team); whole team sees it.
- **Internal-only** — grant the **Internal** Entra-group team; never the External team; + the app-flag filter.
- **Private / named individuals** — own the thread narrowly (single user or restricted team the matter team does NOT inherit), then "Manage access" → named individuals (or an access team of those individuals).
- **Mixed internal/external** — additively grant both Internal + External teams/principals.
- **Internal/External cohorts** = **Entra group teams** (membership-driven; changes in Entra flow to Dataverse).

## ⚠️ CONFIG PREREQUISITES (owner / Dataverse admin — required for impersonation to work live)
1. **BFF app user needs the Delegate role** (`prvActOnBehalfOfAnotherUser`) so `MSCRMCallerID` impersonation is permitted. Keep the app user broadly scoped (System-Administrator-equivalent) so the impersonation intersection = exactly the target user's access (else results silently narrow).
2. **Messaging tables' role Read = User-level.** `sprk_communicationthread` + `sprk_communication` role Read MUST be **User** level (widened per-record via team ownership/shares). If any role has BU/Org Read on these tables, private threads **cannot be hidden** (the union rule).
3. **External participants** need Dataverse `SystemUser` records to be impersonated / `RetrievePrincipalAccess`-checked — **deferred (external portal is R2/R3)**; R1 is internal-focused.

## Task impact
- **042** → REWORK to impersonation model (record access via impersonation; app-flags for internal-only/privilege). Keep the 16 tests that are still valid (internal-only/privilege/point-forward-of-the-flag); drop the hand-computed-union tests.
- **NEW enabler** → per-request impersonation (`MSCRMCallerID`) on the messaging read path in `DataverseWebApiService`/the read used by 050 (small — client already builds per-request messages).
- **050** → thread-read + unread use impersonation + app-flags.
- **043 (1:1 direct)** → simplified: narrow thread ownership so exactly the two participants have access; enforced by impersonation.
- **041** → unchanged for R1 (approximation, documented).
- **ADR-046** → note read enforcement mechanism = impersonation (`MSCRMCallerID`) + `RetrievePrincipalAccess` for gates; Manage-access (POA) for granting.

## APIs (all Web API v9.2, app-only capable)
`GrantAccess` / `ModifyAccess` / `RevokeAccess` (POA write) · `RetrievePrincipalAccess` (effective rights, one principal+record) · `RetrieveSharedPrincipalsAndAccess` (shared-with only) · `RetrieveAccessOrigin` (why-access, debugging) · query `MSCRMCallerID` header (impersonated read). Precedent: `PlaybookSharingService` (POA reads + Grant/Revoke app-only).
