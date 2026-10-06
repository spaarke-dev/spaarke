---
name: exo-apponly-dc-write-error-2026-10-04
description: Cause/fix for EXO app-only "doesn't have write permission to target DC ... Insufficient access rights" — -Organization must be the primary .onmicrosoft.com domain, not tenant GUID/custom domain
metadata:
  type: reference
---

## 2026-10-04: EXO app-only "write permission to target DC" on writes (T251)
**Question**: Why do app-only (MI-FIC -AccessToken, Exchange.ManageAsApp) WRITE cmdlets fail with "doesn't have write permission to target DC ... INSUFF_ACCESS_RIGHTS" while reads work and delegated writes succeed? Does it need a support case?

**Findings**:
- The leading documented cause is the `-Organization` value. Connect-ExchangeOnline docs say the valid value is "the primary .onmicrosoft.com domain". The app-only doc has an Important box saying the same. Spaarke passed the tenant GUID. Reads work because they are routed by the token. Writes target the org's directory partition, and a wrong or unresolved org yields "target DC:." / "target forest isn't an account partition". Microsoft Q&A 946814 has an accepted fix (switching to onmicrosoft.com fixed Set-CASMailbox and New-TransportRule). Q&A 1820745 has an accepted fix too (the wrong org was in the connect string).
- Spaarke corroboration (inferred): adding Entra Exchange Admin turned RBAC denials into the same DC error. RBAC passed, then the directory write failed, so the fault is not RBAC.
- The App-RBAC page says "Applications can't become member of a Role Group". The app-only page Option 2 uses Add-RoleGroupMember. The two pages contradict each other. Direct `-App` assignment is the documented App-RBAC path.
- The only documented app-only exclusions are the UnifiedGroup cmdlets plus SCC eDiscovery. No propagation time is documented for Enable-OrganizationCustomization. The App-RBAC cache takes 30 min–2 h (documented for application roles).
- Q&A 692969 (2022) reports the error intermittently (~80%) with the correct setup, and the writes actually committed. So: verify state after an error, and treat it as a support case only if it persists after the org fix.

**Sources**: learn.microsoft.com/powershell/module/exchangepowershell/connect-exchangeonline (-Organization); .../powershell/exchange/app-only-auth-powershell-v2; learn.microsoft.com/exchange/permissions-exo/application-rbac; .../enable-organizationcustomization; Q&A 946814, 1820745, 692969, 1661763

**Confirmed live 2026-10-04 16:27**: switching to `-Organization spaarke.onmicrosoft.com` made `New-ServicePrincipal` and `New-ManagementRoleAssignment -RecipientGroupScope` succeed app-only. **Open questions**: whether `-AccessToken` + tenant GUID is officially supported at all (docs are silent).
