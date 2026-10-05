---
name: exo-sidecar-identity-2026-10-03
description: Exchange Online PowerShell identity for the L2 Worker's App Service sidecar (H14a ApplicationAccessPolicy) — MI vs MI-FIC -AccessToken vs cert; RBAC for Applications status
metadata:
  type: reference
---

## 2026-10-03: EXO PowerShell identity from App Service Linux sidecar
**Question**: Can the pwsh sidecar use `Connect-ExchangeOnline -ManagedIdentity`, or should it use MI-FIC `-AccessToken`/KV cert; what perms; is AAP superseded?

**Findings**:
- VERIFIED by string-dumping ExchangeOnlineManagement 3.9.2 `netCore/Microsoft.Exchange.Management.AdminApiProvider.dll`: `-ManagedIdentity` uses `IDENTITY_ENDPOINT` + `IDENTITY_HEADER` (header `X-IDENTITY-HEADER`, `api-version=2019-08-01`, `&client_id=`) and otherwise falls back to IMDS `169.254.169.254` (`api-version=2018-02-01`). This is the App Service/Functions protocol. Microsoft's documented supported list is still only Automation, VMs, VMSS, Functions; App Service is not on it. App Service docs say all containers (main and sidecars) share environment variables, but no doc names IDENTITY_* specifically. Treat as UNVERIFIED and check with `env` inside the sidecar.
- A managed identity is single-tenant, so `-ManagedIdentity` cannot reach Model 2 (customer tenant). MI-as-FIC on a MULTI-TENANT app supports other tenants. The FIC must be on an app in the same tenant as the UAMI, and the app must be provisioned (admin consent) in the target tenant.
- `-AccessToken` is in Connect-ExchangeOnline since 3.1.0. The module reads `tid`/`upn` from the token and errors with "Certificate or ManagedIdentity cannot be used with an AccessToken". App-only tokens for scope `https://outlook.office365.com/.default` with -Organization work (michev.info). There is no auto-renew, which doesn't matter for short per-request connects.
- `-CertificateThumbprint` is Windows-only (documented). On Linux, use `-Certificate <X509Certificate2>`.
- Version gates: module 3.10.x needs PS 7.6. 3.5.0–3.9.2 needs PS 7.4+. Official Linux support is Ubuntu only (Azure Linux not listed).
- Perms: `Exchange.ManageAsApp` (role id dc50a0fb-09a3-484d-be87-e023b12c6440) on 00000002-0000-0ff1-ce00-000000000000, plus an Entra role (Exchange Administrator) OR a custom Exchange role group via New-ServicePrincipal + Add-RoleGroupMember (documented "Option 2"). Which management role holds *-ApplicationAccessPolicy is NOT verified; community says the Organization Management role group has it. Check with `Get-ManagementRole -Cmdlet New-ApplicationAccessPolicy`.
- AAP is "legacy", replaced by RBAC for Applications. Docs say don't create new AAPs, but there is NO retirement date. AAP is capped at ~100–300 policies per org. RBAC for Apps allows 10k apps, uses `New-ManagementScope` (MemberOfGroup DN filter) + `New-ManagementRoleAssignment -App -Role "Application Mail.Send" -CustomResourceScope`, and REQUIRES removing the Entra-consented Mail.* grants (union semantics). Nested groups aren't honored. Cache takes 30 min–2 h. Assigning needs Organization Management (delegating) + Exchange Admin.

**Recommendation given**: the Worker mints the EXO token via the existing `WorkerDataverseCredentialFactory.CreateManagedIdentityFederatedCredential(tenantId, exoAppId)` with scope `https://outlook.office365.com/.default`, then passes it to the sidecar per request. The sidecar holds no credential.

**Sources**: learn.microsoft.com/powershell/exchange/connect-exo-powershell-managed-identity; .../module/exchangepowershell/connect-exchangeonline; .../exchange/exchange-online-powershell-v2 (release notes); .../exchange/app-only-auth-powershell-v2; learn.microsoft.com/exchange/permissions-exo/application-rbac; .../application-access-policies; .../module/exchangepowershell/new-applicationaccesspolicy; learn.microsoft.com/azure/app-service/configure-sidecar; learn.microsoft.com/entra/workload-id/workload-identity-federation-config-app-trust-managed-identity; michev.info/blog/post/4249

**Open questions**: whether sidecars actually receive IDENTITY_ENDPOINT/HEADER; which narrow management role contains the AAP / App-RBAC cmdlets; whether owner pivots H14a to RBAC for Applications (this changes BFF app consent).
