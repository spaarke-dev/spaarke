---
name: exo-rbac-for-apps-design-2026-10-04
description: Concrete Exchange RBAC-for-Applications design for H14a (T251) — cmdlets, idempotency, caller permissions (Role Management escalation risk), union with Entra Graph grants, subscriptions gap, propagation, limits
metadata:
  type: reference
---

## 2026-10-04: RBAC for Applications concrete design (T251)
**Context**: Owner decision 2026-10-04 — H14a switches from ApplicationAccessPolicy to RBAC for Apps. The caller is the new single-tenant "Spaarke Exchange Policy Admin" app, reached via MI-FIC with `-AccessToken`. Follows [[exo-sidecar-identity-2026-10-03]].

**Findings (verified on Learn unless marked)**:
- `New-ServicePrincipal -AppId <appId> -ObjectId <Entra SERVICE PRINCIPAL object id>` (not the app-registration object id; `-ServiceId` is deprecated). Managed-identity SPs work: practical365 shows it for an Automation managed identity (community source).
- `New-ManagementRoleAssignment` App parameter set: `-App <Exchange SP ObjectId>`, `-Role`, `-CustomResourceScope` | `-RecipientAdministrativeUnitScope` | `-RecipientGroupScope` (direct members only, no management scope needed — documented on the cmdlet page but absent from the App-RBAC article, so test it), `-Delegating`. `-Name` max 64 chars, auto-generated if omitted. "With -App you can only specify application or management roles", so a custom ADMIN role can be assigned directly to the caller app.
- `MemberOfGroup` in `-RecipientRestrictionFilter` must be a DN (recipientfilter-properties). Get it with `(Get-Group <id>).DistinguishedName`. Scope `-Name` ≤64.
- `Test-ServicePrincipalAuthorization -Identity <sp> [-Resource <mbx>]` returns RoleName, GrantedPermissions, AllowedResourceScope, ScopeType, InScope (True/False/"Not Run"). It BYPASSES the cache and EXCLUDES Entra grants, so it shows configuration, not effective state.
- `Get-ManagementRoleAssignment -RoleAssigneeType`: the documented values do NOT include ServicePrincipal, although the output shows RoleAssigneeType=ServicePrincipal. Look assignments up by deterministic -Identity name, or filter client-side.
- Caller permissions: the App-RBAC page says Organization Management holds the delegating assignments, or grant delegating assignments per application role, AND "In Microsoft Entra ID, you need the Exchange Administrator role to assign these permissions" (ambiguous — this may make Entra Exchange Admin unavoidable). The Exchange split-permissions doc says Role Management holders and Org Mgmt "can bypass this delegate security check", so Role Management is effectively Exchange-admin-equivalent. A child role of Role Management stripped to the needed entries, plus delegating assignments for only the Application Mail.* roles, is the narrowest candidate. Whether the child role still bypasses delegation is UNVERIFIED. Even best case, the caller can grant ANY app (itself included) tenant-wide Mail.* for the delegated roles, because the scope is optional.
- App-only: the only documented app-only exclusions are the UnifiedGroup cmdlets. RBAC cmdlets under app-only are inferred OK. The App-RBAC page says "Applications can't become member of a Role Group" while app-only-auth Option 2 uses Add-RoleGroupMember with an SP. Use direct `-App` assignments instead.
- Union semantics (verified): the Entra Mail.*/Calendars.* app roles must be removed from the BFF app and the UAMI SP. Graph change notifications (subscriptions) under RBAC-only grants are UNDOCUMENTED and need a spike.
- Cache takes 30 min–2 h. There is no documented effective-state check other than a real Graph probe.
- Limits: 10,000 apps per org (documented). Scope and assignment counts are undocumented.

**Sources**: learn.microsoft.com/exchange/permissions-exo/application-rbac; .../powershell/module/exchangepowershell/{new-serviceprincipal,new-managementroleassignment,new-managementscope,test-serviceprincipalauthorization,get-managementroleassignment}; .../powershell/exchange/recipientfilter-properties; .../exchange/permissions/split-permissions/configure-exchange-for-split-permissions; .../exchange/understanding-management-role-assignments-exchange-2013-help; practical365.com/rbac-for-applications-azure-automation; mikecrowley.us 2026-07-10 migration post

**Open questions**: Graph subscriptions with RBAC-only; whether Entra Exchange Admin is required for the caller; whether a Role-Management child role bypasses the delegate check; RecipientGroupScope with -App in practice. **Answered live 2026-10-04**: it works app-only (with `-Organization <initial domain>`); the assignment reads back `RecipientWriteScope = Group` + `CustomResourceScope = <group Name>` (no RecipientGroupScope property); `Test-ServicePrincipalAuthorization` reports `ScopeType Group`, in/out correct.
