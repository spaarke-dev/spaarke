# teams-app-r1 → R2 Coordination Response: Option A landed (principal-agnostic /external)

> **From**: teams-app-r1 (task 025)
> **To**: spaarke-SPA-external-access-platform-r2
> **Date**: 2026-08-05
> **Re**: your decision `notes/teams-app-r1-coordination.md` — "Proceed with Option A; it is R2 Phase-P1 delivered early"
> **Status**: Code complete + green (full BFF suite 9761 pass / 0 fail; 27 new tests). BFF deploy + live Teams E2E operator-gated (see §9).

Built to your 8 guardrails. This is the `CallerPrincipalResolver` (FR-22) realized on the collaboration module; lift it into the module framework as-is.

---

## 1. Files touched

**New:**
- `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/CallerPrincipalResolver.cs` — the resolver + abstractions + both strategies (see §2).
- `src/server/api/Sprk.Bff.Api/Api/Filters/CallerPrincipalAuthorizationFilter.cs` — the group-level ADR-008 endpoint filter that runs the resolver and sets `CallerPrincipal` on `HttpContext.Items`.

**Modified:**
- `Api/ExternalAccess/ExternalAccessEndpoints.cs` — `/api/v1/external` group now `RequireAuthorization(AuthPolicies.ExternalCollaboration)` (dual-scheme) + `.AddCallerPrincipalAuthorizationFilter()` at the group level. `/api/v1/collab` marked TRANSITIONAL (§7).
- `Api/ExternalAccess/ExternalUserContextEndpoint.cs` — `/me` reads `CallerPrincipal` (plane-agnostic).
- `Api/ExternalAccess/ExternalProjectDataEndpoints.cs` — all data + download handlers read `CallerPrincipal`; per-endpoint CIAM filter removed (now group-level).
- `Infrastructure/DI/AuthorizationModule.cs` — new `ExternalCollaboration` policy: `AuthenticationSchemes = { Ciam, JwtBearerDefaults }`.
- `Infrastructure/DI/ExternalAccessModule.cs` — registers `ICallerPrincipalResolver` + the two `ICallerPrincipalStrategy` (scoped).
- `Infrastructure/Authentication/AuthPolicies.cs` — `ExternalCollaboration` constant (supersedes `CiamExternal` on this group; `CiamExternal` retained for reference).

**Tests:**
- `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/CallerPrincipalTests.cs` — CIAM rights regression + workforce scope semantics.
- `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/CallerPrincipalResolverTests.cs` — plane selection + workforce NFR-08 scope + CIAM deny.
- `tests/integration/contract/Api/ExternalAccess/ExternalAccessContractTests.cs` — fixture re-points `ExternalCollaboration` at the test scheme + adds a CIAM issuer claim so the CIAM plane is selected (the 3 download/authz contract tests now exercise the dual-scheme path).
- `tests/unit/.../ExternalAccessEndpointTests.cs` — the one `/me` handler test updated to seed a `CallerPrincipal`.

---

## 2. `CallerPrincipalResolver` shape (the FR-22 component to lift)

```
ICallerPrincipalResolver.ResolveAsync(HttpContext, ct) → CallerPrincipalResolution
    └─ CallerPrincipalResolver: DeterminePlane(user) → selects the ICallerPrincipalStrategy whose .Plane matches

interface ICallerPrincipalStrategy { CallerPrincipalPlane Plane; ResolveAsync(HttpContext, ct) → CallerPrincipalResolution }
    ├─ CiamContactPrincipalStrategy   (Plane = CiamContact) — reuses ExternalParticipationService (resolve contact by oid/email + participations)
    └─ WorkforcePrincipalStrategy     (Plane = Workforce)   — wraps IWorkforcePrincipalResolver (020) + IAccessibleRecordSetService (022)

CallerPrincipal  { Plane, ContactId, SystemUserId?, Email, Oid, ProjectAccess[] (Tier-2 record scope) }
    exposes: GetAccessibleProjectIds(), HasProjectAccess(id), GetAccessLevel(id), GetEffectiveRights(id)
CallerPrincipalResolution = Resolved(CallerPrincipal) | Denied(IResult 401/403)
```

**Plane selection** (`CallerPrincipalResolver.DeterminePlane`): CIAM iff the (already-validated) token's `iss` contains `ciamlogin.com` **OR** its `tid` equals the configured `Ciam:TenantId`; otherwise workforce. A token validates against exactly one authority, so exactly one scheme succeeds per request and exactly one plane applies. Spoof-safe: `iss`/`tid` are read from a cryptographically-validated token.

**Where a THIRD plane plugs in**: add one more `services.AddScoped<ICallerPrincipalStrategy, XyzStrategy>()` with a new `CallerPrincipalPlane` value + a `DeterminePlane` branch. Handlers and the filter are untouched. This is the extension seam R2 asked for.

---

## 3. Workforce Tier-2 record-scope predicate (the priority item — NFR-08)

**Predicate**: a workforce caller's accessible projects = `IAccessibleRecordSetService.ComposeAsync(principal, "sprk_project")` (task 022) — i.e.:
- **systemuser** principal → ADR-034 membership set (automatic, Dataverse-governed) **only**.
- **contact-only** principal → `sprk_externalrecordaccess` grants **∪** standing-grant runtime membership (only if `contact.sprk_standinggrant` is set).

**It is NOT "all projects".** A workforce user who merely authenticates gets the composed set and nothing more.

**Code path**: `WorkforcePrincipalStrategy.ResolveAsync` → `IWorkforcePrincipalResolver.ResolveAsync` (020, resolve/deny) → `IAccessibleRecordSetService.ComposeAsync(principal, "sprk_project")` (022) → `CallerPrincipal.ProjectAccess`. Every read handler filters by `CallerPrincipal.HasProjectAccess` / `GetAccessibleProjectIds`; the download handler additionally enforces document→project scoping. Enforcement is uniform with the CIAM plane (same handler bodies).

**Within-project rights**: every accessible project is surfaced at `ExternalAccessLevel.Collaborate` (Read|Create|Write, **no Delete**) — `WorkforcePrincipalStrategy.WorkforceProjectAccessLevel`. **This is a deliberate decision, flagged for you** (see §8-D1): the accessible-set IS the record-scope (NFR-08) boundary; the level only governs within-project verbs. If R2's F3/F5 admin layer needs per-project levels for workforce callers, that is a clean extension point (map the composed set to graded levels instead of a flat Collaborate).

---

## 4. CIAM regression result (guardrail #3 / FR-15)

CIAM path preserved byte-for-byte: same deny codes/statuses (`sdap.access.deny.contact_not_found`; 401 on missing oid+email), same participation load, same access-level → rights mapping, same `/me` payload shape/values. Evidence (all pass):
- `CallerPrincipalTests`: `GetEffectiveRights_CiamViewOnly/Collaborate/FullAccess_*`, `MeProjection_CiamAccessLevel_MapsToSameStringAsLegacyHandler`.
- `CallerPrincipalResolverTests`: `CiamStrategy_ResolvesContactAndParticipations_PreservesAccessLevels`, `CiamStrategy_MissingOidAndEmail_Returns401`, `CiamStrategy_ContactNotFound_Returns403`.
- `ExternalAccessContractTests` (through the HTTP surface): `ExternalGroup_WhenUnauthenticated_Returns401`, `DownloadDocument_WhenCallerLacksProjectAccess_Returns403`, `..._WhenDocumentNotInRequestedProject_Returns403`, `..._WhenAuthorizedAndDocumentInProject_Returns200`.
- Full BFF suite: **9761 passed / 0 failed**.

---

## 5. Auth / token detail

- The BFF **workforce** default scheme is the `1e40baad-…` app (`AzureAd__Audience = api://1e40baad-e065-4aea-a8d4-4b7ab273458c`). It **accepts the Teams NAA token** (aud `api://1e40baad-…`, **v1**; `requestedAccessTokenVersion = null`) with **no config change** — verified Phase 0. Microsoft.Identity.Web handles the v1 issuer.
- The `ExternalCollaboration` policy lists both `AuthSchemes.Ciam` and `JwtBearerDefaults.AuthenticationScheme`. A CIAM token validates on Ciam only; a workforce token on the default only — no cross-validation, no CIAM regression.
- **No `AzureAd` audience / scheme config change** was required or made.

---

## 6. Entra config applied (dev — app `1e40baad-…`, obj id `c2aab303-…`, env spaarkedev1 / a221a95e)

1. Multitenant (`AzureADMultipleOrgs`) + `access_as_user` scope exposed.
2. Teams client apps pre-authorized on `access_as_user`: `1fec8e78-bce4-4aaf-ab1b-5451cc387264` (desktop/mobile) + `5e3ce6c0-2b1f-4285-8d4b-75ee78787346` (web).
3. SPA redirect URIs: `https://green-dune-0c4f1221e.7.azurestaticapps.net`, **`brk-multihub://green-dune-0c4f1221e.7.azurestaticapps.net`** (the NAA `AADSTS700046` fix — MSAL v5 sends `redirect_uri=brk-multihub://{host}`), plus per-broker `brk-1fec8e78` / `brk-5e3ce6c0` URIs.
4. SWA CSP framing: `frame-ancestors 'self' https://teams.microsoft.com https://*.cloud.microsoft`, no `X-Frame-Options`.

---

## 7. `/api/v1/collab` disposition

**Marked TRANSITIONAL, slated for removal** (guardrail #5 — no second maintained workforce entry point). `/collab/me` + `/collab/{…}/content` are now also served principal-agnostically under `/api/v1/external`; the external SPA and the Teams host both call `/api/v1/external/*`. `/collab` is kept mapped for one release for any direct caller.
**Removal plan**: delete `MapWorkforceCollaborationEndpoints` + `WorkforcePrincipalContextEndpoint` + `WorkforceCollaborationDownloadEndpoint` once no client calls `/api/v1/collab/*`. At the same time, delete the now-dead `ExternalCallerAuthorizationFilter` (superseded by `CiamContactPrincipalStrategy`; zero callers today — see §8-D2).

---

## 8. Deviations

- **D1 — workforce within-project rights = Collaborate (flat).** The accessible-set is the record-scope boundary (NFR-08 satisfied); the level is a deliberate flat `Collaborate` (no Delete) rather than a per-project graded level. Rationale: systemusers are internal staff (ADR-034 membership) and accessible-set contacts are deliberately-granted collaborators. **Flagged for R2** to refine in F3/F5 if graded workforce levels are needed (extension point named in §3).
- **D2 — `ExternalCallerAuthorizationFilter` left inert, not deleted.** Its logic is fully reproduced + tested in `CiamContactPrincipalStrategy`; it has zero code callers now. Left in place (not wired to any route → no security surface) to keep this change surgical; scheduled for deletion with the `/collab` transitional cleanup (§7).
- **D3 — systemuser-without-contact `/me` returns `ContactId = Guid.Empty`.** A workforce systemuser with no linked contact still authorizes via ADR-034 membership; the `/me` `ContactId` is `Guid.Empty`. The SPA navigates by the project list, so this is benign.

---

## 9. Operator-gated remainder

Code + gates + publish-size (46.90 MB compressed incl PDBs, vs ~49.63 MB baseline — under the 60 MB ceiling) are complete and committed. Still operator-gated (a coding agent cannot deploy shared infra + sign into a live Teams client):
1. Rebuild + redeploy the BFF to `spaarke-bff-dev` (task 065 mechanism — coordinate; shared env).
2. Re-run the live Teams E2E (task 080): workforce user opens the tab → workspace + records load via `/api/v1/external/*`.
