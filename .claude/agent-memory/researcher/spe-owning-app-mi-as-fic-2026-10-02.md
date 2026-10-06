---
name: spe-owning-app-mi-as-fic-2026-10-02
description: Can an ACA/App Service worker's UAMI act app-only as the SPE OWNING app via MI-as-FIC instead of a cert PFX? Covers GA status, same-tenant rule, appidacr=2 for FIC tokens, SharePoint REST acceptance evidence, registration API moved to Graph v1.0, .NET recipe.
metadata:
  type: reference
---

## 2026-10-02: SPE owning app authenticated via MI-as-FIC (no cert)
**Question**: Replace the owning app's X.509 PFX with a UAMI federated identity credential for container-type registration + container create?

**Findings**:
- MI-as-FIC is **GA (Identity blog 2025-05-08)**; supersedes the "GA in 2026" line in [[graph-spe-standards-2026-08-16]]. Rules: UAMI only (portal: "You can only use User-Assigned Managed Identities"); issuer `https://login.microsoftonline.com/{tenant}/v2.0`, subject = MI principal(object) id, audience `api://AzureADTokenExchange`; **MI and app reg must be in the SAME tenant**; target resource in ANOTHER tenant is supported if the app is multi-tenant and provisioned there; cross-CLOUD not supported; max 20 FICs per app; create FICs sequentially on a UAMI (409); propagation lag → AADSTS70021, add retries. Malaysia South UAMI region unsupported.
- **appidacr/azpacr for FIC tokens**: Learn claims ref only defines 0/1/2 (public/secret/cert), silent on FIC. Community decode (longbeach.cloud 2024-02-13) shows **appidacr = 2** for a WIF token — FIC is a client_assertion (jwt-bearer), same wire shape as a cert assertion. So SharePoint's "no secret (appidacr=1)" rule should not bite. Field evidence: dev.to SPFx app-catalog deploy via WIF (PnP), MS Q&A 5788577 (VM MI-as-FIC → SharePoint Sites.Selected worked for a week of QA). No Microsoft doc explicitly says "SharePoint REST accepts FIC" → MEDIUM-HIGH confidence.
- **Registration API moved**: Learn "Register application permissions" (ms.date 2026-07-13) documents ONLY Graph v1.0 `PUT /storage/fileStorage/containerTypeRegistrations/{ctId}` with `FileStorageContainerTypeReg.Selected` (app or delegated), client-credentials flow, "Configure an application credential, such as a certificate" — no cert-only statement, no FIC mention. Legacy SharePoint REST `/_api/v2.1/storageContainerTypes/.../applicationPermissions` no longer documented; repo script says it returns apiNotFound in Spaarke tenant.
- Repo empirical note (scripts/Register-BffMiWithContainerType.ps1): owner-app CLIENT-SECRET tokens rejected "invalid token" even on the Graph beta registration-grant API → SPE owner-app ops appear to enforce non-secret client auth on Graph too (undocumented). FIC (appidacr 2) should pass; needs a live probe.
- Container create: a guest app can hold `create` in the registration — the worker's MI can be a GUEST app and create containers as itself (BFF MI already works this way), no FIC needed. Only REGISTRATION requires acting as the owning app.
- .NET: Azure.Identity `ClientAssertionCredential(tenant, appClientId, async _ => (await new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(miClientId)).GetTokenAsync(new(["api://AzureADTokenExchange/.default"]))).Token)`; or Microsoft.Identity.Web `SourceType: SignedAssertionFromManagedIdentity` (class ManagedIdentityClientAssertion, pkg Microsoft.Identity.Web.Certificateless); or MSAL `WithClientAssertion` + `ManagedIdentityApplicationBuilder`.

**Sources**:
- https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation-config-app-trust-managed-identity (updated 2026-09-29)
- https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation-considerations
- https://devblogs.microsoft.com/identity/access-cloud-resources-across-tenants-without-secrets-ga/
- https://learn.microsoft.com/en-us/sharepoint/dev/embedded/build/register-application-permissions
- https://learn.microsoft.com/en-us/sharepoint/dev/embedded/build/configure-authentication-authorization
- https://learn.microsoft.com/en-us/graph/api/filestorage-post-containertyperegistrations?view=graph-rest-1.0
- https://learn.microsoft.com/en-us/entra/identity-platform/access-token-claims-reference
- https://longbeach.cloud/2024/02/13/azure-devops-with-workload-identity-federation/ (appidacr 2)
- https://learn.microsoft.com/en-us/answers/questions/5788577/managed-identity-vm-access-to-sharepoint-online-wi
- https://dev.to/kkazala/deploy-spfx-app-using-pipelines-workload-identity-federation-5fhi

**Open questions**: No MS doc states SharePoint REST/SPE owner-app ops accept FIC; verify with a live probe (decode token azpacr, call Graph v1.0 registration PUT). BFF `SpeAdminGraphService.RegisterContainerTypeAsync` still uses ClientSecretCredential + legacy SP REST.
