# Lessons Learned — Spaarke External Access Platform (R1)

> Appended at project close (task 090, 2026-08-06). Project: migrate the external Secure Project Workspace from Power Pages + Entra B2B guests → Azure Static Web Apps (SWA) + Entra External ID (CIAM), broker-only (ADR-028 Amendment A1).

## What went well
- **Broker-only invariant held end-to-end.** External CIAM tokens authenticate ONLY to the BFF; no OBO on the external path, all downstream SPE/Dataverse app-only, no B2B guest, no synthetic SPE container membership. The authz-before-stream download (403/no-bytes/no-Graph before any pointer resolution) was the single highest-consequence property and was proven by the `ExternalAccessContractTests` centerpiece (`Times.Never` on `GetSpePointersAsync`/`DownloadFileAsync`).
- **Contact-by-`oid` resolution** (`sprk_externalobjectid`) with an email first-login fallback that binds the oid proved robust and anti-hijack (a Contact already bound to a different oid is refused).
- **Additive second JwtBearer scheme** (`Ciam`, pinned to `/api/v1/external` via `CiamExternal`) layered cleanly alongside the workforce default without disturbing existing auth — the pattern generalized directly into R2's dual-plane work.
- **Reuse-first** (per the BFF audit): `SpeFileStore.DownloadFileAsync`, `RegistrationEmailService`, `ExternalParticipationService`/`ExternalCallerAuthorizationFilter` were extended, not forked — kept publish size well under the 60 MB ceiling.

## What was hard / gotchas
- **CIAM `Ciam:Audience` = client-id GUID, not `api://` URI** — because the BFF-API app manifest sets `requestedAccessTokenVersion: 2` (v2 tokens carry `aud` = client-id GUID). Getting this wrong yields 401s that look like scheme misconfig. Confirm against a live token.
- **Missing App Service `Ciam:*` settings → 500, not 401.** When the deployed BFF had no `Ciam:*` config, the `Ciam` scheme couldn't build its authority (`{Instance}/{TenantId}/v2.0`) → `InvalidOperationException` on challenge → 500. App Service settings (not just repo config) must be set; `Deploy-BffApi.ps1` only pushes the zip.
- **CIAM tenant ops are irreducibly manual.** App registrations, admin consent, `requestedAccessTokenVersion:2` manifest, SSPR enablement, and user-flow association live in a separate CIAM tenant the workforce MI cannot reach — all portal/Graph admin actions, gated on the owner (tasks 001/002/DI-028-01).
- **Deploying from `master` mid-project shipped a stale SPA.** Running the SWA deploy workflow against `master` (which lacked the 011/012 + workflow CIAM env) shipped a placeholder-auth HashRouter build; fixed by merging branch→master and redeploying from the branch ref. Lesson: for `workflow_dispatch` SPA deploys, deploy from the ref that actually has the changes.
- **The Power Pages "site" is not a Dataverse web resource** — it's a Power Pages *site* (`sprk-external-workspace.powerappsportals.com`), so it can't be retired via API; owner ops in the admin portal (task 041). Two hosts technically coexisted until the owner took the site out of service.

## Deferrals carried forward (→ R2)
- **DI-029-01** — no core-user onboarding UI; onboarding is API-only (`invite-and-grant`). R2 F5 admin UI.
- **DI-025-01** — CIAM provisioner create-ok/persist-fail window (409 on retry). R2 FR-18 self-healing.
- **DI-030-01** — two acceptance sub-criteria (invalid-issuer→401, oid-bound-not-email-hijacked) need live CIAM+Dataverse; in-process tests would be false-green under ADR-038 bans. R2 FR-19 live-E2E.
- SSPR first-run flow verified only with existing creds (owner). R2 FR-20.

## Process notes
- The project's own success validated the **dual-plane + principal-agnostic** direction that became R2's foundation — teams-app-r1 then shipped the `CallerPrincipalResolver` (R2 FR-22) directly on this surface.
- Project-close ran on a branch far behind master (R1 code was merged mid-project); the wrap-up gates were satisfied at each task's Step 9.5 + at merge, so the close confirmed-from-record rather than re-running heavy gates against stale code (a reasoned, surfaced deviation, not a silent skip).
