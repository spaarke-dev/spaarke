# Spaarke Auth — New Environment Setup Guide (STUB — content merged)

> **Status**: **RETIRED as of 2026-08-17.** Content merged into the single authoritative customer-provisioning guide.
> **Authoritative sources**:
> - [`SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`](SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md) — end-to-end customer provisioning (this is where the auth-deployment mechanics now live: §6.5, §7.3, §7.7)
> - [`.claude/adr/ADR-028-spaarke-auth-architecture.md`](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) — the canonical Spaarke Auth v2 architecture (21 MUSTs, MI-first model)

This guide previously carried the 10-section mechanical operator checklist for standing up Spaarke Auth v2 in a fresh customer tenant — client-side Dataverse environment variables, App Service configuration, KV secrets, Azure AD permissions for the BFF managed identity, Dataverse Application User registration, Exchange `ApplicationAccessPolicy`, and post-deploy verification smoke tests. Per task 001 of `customer-provisioning-orchestration-r1` (spec.md Gap 4), that content is now consolidated into [`SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`](SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md) §6.4 (7 per-customer Dataverse env vars), §6.5 (App Service configuration), §7.3 (Phase 3 identity + secrets), §7.7 (Phase 7 App Users + Graph app-role parity from `GraphAppRoles.cs`), §7.9 (Phase 9 post-deploy integrations incl. Exchange policy), §9 (silent-fail traps T1–T7), and §13.1 (troubleshooting `/health` failures). The **binding auth architecture rules** live in [ADR-028](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) — the 21 MUSTs remain unchanged. **Follow the authoritative guide** for any new customer auth setup; this stub is retained for git history + inbound-link continuity.

## §6 (Dataverse application users) — the FR-20 impersonation prerequisite

> Kept here because unified-access-control-r2 task 036 names this file's §6; the procedure itself lives in
> [`SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`](SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md) §7.7 and §12.8.

- **The privilege.** The BFF's Dataverse application users must hold `prvActOnBehalfOfAnotherUser`, or Dataverse ignores
  the `MSCRMCallerID` header the BFF sends to read AS a signed-in internal user (the communication reads, the Office
  add-in picker, and the FR-20 root sets behind `ExternalAccess__ImpersonatedRootSets__Enabled`). No Spaarke role carries
  it. In dev it comes from the **System Administrator** role on the application users `# mi-bff-api-dev`,
  `SDAP-BFF-SPE-API` and `# Spaarke DMS-SPE Dev 1` — verified by role on 2026-09-30, not by a live call; it is unproven
  live until the task 034 negative canary passes (strictly smaller; equality is a failure). Never grant it to a Spaarke
  role as a workaround.
- **The application user must stay Organization-scoped** (register E-2 / E-3). An impersonated read returns what both the
  application user and the impersonated user can read, so a narrowed application user silently UNDER-grants: the user is
  shown fewer records than Dataverse lets them read, with no error.
